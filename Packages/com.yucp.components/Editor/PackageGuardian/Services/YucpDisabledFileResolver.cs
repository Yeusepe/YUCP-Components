using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace YUCP.Components.PackageGuardian.Editor.Services
{
    /// <summary>
    /// Resolves "*.yucp_disabled" files created by YUCP exports into their enabled counterparts.
    /// This stays in Package Guardian so com.yucp.components does not depend on the importer package.
    /// </summary>
    public static class YucpDisabledFileResolver
    {
        private const string DisabledSuffix = ".yucp_disabled";
        // Package Guardian retains the legacy keys for compatibility. The importer uses scoped keys,
        // preventing both resolver implementations from resuming the same operation after a reload.
        private const string PendingKey = "YUCP.PackageManager.ResolveYucpDisabled.Pending";
        private const string PendingStartTicksKey = "YUCP.PackageManager.ResolveYucpDisabled.StartTicksUtc";
        private const string PendingTimeoutSecondsKey = "YUCP.PackageManager.ResolveYucpDisabled.TimeoutSeconds";
        private const string VerboseKey = "YUCP.PackageManager.ResolveYucpDisabled.Verbose";
        private const double ScanIntervalSeconds = 0.5;
        private const double VerboseStatusIntervalSeconds = 5.0;
        private static bool _isRunning;
        private static bool _compilationHooked;

        private static bool IsVerbose()
        {
            try { return EditorPrefs.GetBool(VerboseKey, false); }
            catch { return false; }
        }

        private static void Log(string message)
        {
            Debug.Log($"[YUCP Disabled Resolver] {message}");
        }

        private static void LogWarning(string message)
        {
            Debug.LogWarning($"[YUCP Disabled Resolver] {message}");
        }

        [InitializeOnLoadMethod]
        private static void ResumePendingAfterDomainReload()
        {
            try
            {
                if (!EditorPrefs.GetBool(PendingKey, false))
                    return;

                long startTicksUtc = 0;
                try { startTicksUtc = long.Parse(EditorPrefs.GetString(PendingStartTicksKey, "0")); }
                catch { startTicksUtc = 0; }

                float timeoutSeconds = EditorPrefs.GetFloat(PendingTimeoutSecondsKey, 60f);
                if (timeoutSeconds < 5f) timeoutSeconds = 5f;

                if (startTicksUtc > 0)
                {
                    var elapsed = DateTime.UtcNow - new DateTime(startTicksUtc, DateTimeKind.Utc);
                    Log($"Resuming pending resolve after domain reload. elapsedSeconds={elapsed.TotalSeconds:0.###}, timeoutSeconds={timeoutSeconds:0.###}");
                }
                else
                {
                    Log($"Resuming pending resolve after domain reload (no timestamp). timeoutSeconds={timeoutSeconds:0.###}");
                }

                EnsureCompilationHook(timeoutSeconds);
                ScheduleResolveAfterImport(timeoutSeconds);
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to resume pending resolve: {ex.Message}");
            }
        }

        public static void SetPendingResolve(double timeoutSeconds = 60.0)
        {
            if (timeoutSeconds < 5.0)
                timeoutSeconds = 5.0;

            MarkPending(timeoutSeconds);
            if (IsVerbose())
                Log($"SetPendingResolve(timeoutSeconds={timeoutSeconds:0.###})");

            EnsureCompilationHook(timeoutSeconds);
        }

        private static void EnsureCompilationHook(double timeoutSeconds)
        {
            if (_compilationHooked)
                return;

            _compilationHooked = true;
            CompilationPipeline.compilationFinished += _ =>
            {
                try
                {
                    if (!EditorPrefs.GetBool(PendingKey, false))
                        return;

                    if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    {
                        if (IsVerbose())
                            Log($"compilationFinished: still not ready. isCompiling={EditorApplication.isCompiling}, isUpdating={EditorApplication.isUpdating}");
                        return;
                    }

                    if (IsVerbose())
                        Log($"compilationFinished: pending resolve detected -> scheduling resolve (timeoutSeconds={timeoutSeconds:0.###})");
                    ScheduleResolveAfterImport(timeoutSeconds);
                }
                catch (Exception ex)
                {
                    LogWarning($"compilationFinished handler failed: {ex.Message}");
                }
            };
        }

        private static void MarkPending(double timeoutSeconds)
        {
            try
            {
                EditorPrefs.SetBool(PendingKey, true);
                EditorPrefs.SetString(PendingStartTicksKey, DateTime.UtcNow.Ticks.ToString());
                EditorPrefs.SetFloat(PendingTimeoutSecondsKey, (float)timeoutSeconds);
            }
            catch
            {
            }
        }

        private static void ClearPending()
        {
            try { EditorPrefs.SetBool(PendingKey, false); } catch { }
            try { EditorPrefs.DeleteKey(PendingStartTicksKey); } catch { }
            try { EditorPrefs.DeleteKey(PendingTimeoutSecondsKey); } catch { }
        }

        public static void ScheduleResolveAfterImport(double timeoutSeconds = 15.0)
        {
            if (_isRunning)
            {
                if (IsVerbose())
                    Log($"ScheduleResolveAfterImport ignored (already running). timeoutSeconds={timeoutSeconds:0.###}");
                return;
            }

            _isRunning = true;

            if (timeoutSeconds < 5.0)
                timeoutSeconds = 5.0;

            MarkPending(timeoutSeconds);

            double remainingReadySeconds = timeoutSeconds;
            double lastTime = EditorApplication.timeSinceStartup;
            double nextScanTime = lastTime;
            double nextVerboseStatusTime = lastTime + VerboseStatusIntervalSeconds;
            bool isSubscribed = false;
            bool loggedSkipState = false;
            int scanCount = 0;

            void Tick()
            {
                double now = EditorApplication.timeSinceStartup;
                double dt = Math.Max(0, now - lastTime);
                lastTime = now;

                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    if (!loggedSkipState && IsVerbose())
                    {
                        Log($"Paused while Unity is busy: isCompiling={EditorApplication.isCompiling}, isUpdating={EditorApplication.isUpdating}");
                    }
                    loggedSkipState = true;
                    return;
                }

                loggedSkipState = false;
                remainingReadySeconds -= dt;

                if (remainingReadySeconds <= 0)
                {
                    if (isSubscribed) EditorApplication.update -= Tick;
                    if (IsVerbose())
                        Log($"Stopped waiting: no .yucp_disabled files appeared within {timeoutSeconds:0.###} ready seconds (scans={scanCount}).");
                    ClearPending();
                    _isRunning = false;
                    return;
                }

                // EditorApplication.update can run many times per second. Recursive scans of Assets and
                // Packages are intentionally much less frequent.
                if (now < nextScanTime)
                    return;

                nextScanTime = now + ScanIntervalSeconds;
                scanCount++;

                if (IsVerbose() && now >= nextVerboseStatusTime)
                {
                    nextVerboseStatusTime = now + VerboseStatusIntervalSeconds;
                    Log($"Waiting for .yucp_disabled files: remainingReadySeconds={remainingReadySeconds:0.###}, scans={scanCount}");
                }

                if (!TryResolveAll(out var stats))
                    return;

                Log($"Resolved .yucp_disabled files: enabled={stats.enabled}, updated={stats.updated}, duplicatesDeleted={stats.duplicatesDeleted}, rejected={stats.rejected} (scans={scanCount})");

                try
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    CompilationPipeline.RequestScriptCompilation();
                }
                catch (Exception ex)
                {
                    LogWarning($"Failed to refresh/compile after resolving: {ex.Message}");
                }
                finally
                {
                    if (isSubscribed) EditorApplication.update -= Tick;
                    ClearPending();
                    _isRunning = false;
                }
            }

            EditorApplication.delayCall += () =>
            {
                if (isSubscribed) return;
                isSubscribed = true;
                if (IsVerbose())
                    Log($"Waiting for .yucp_disabled files to land (post-install). timeoutSeconds={timeoutSeconds:0.###}");
                EditorApplication.update += Tick;
            };
        }

        public static bool ResolveNow(bool requestCompilation = true)
        {
            try
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    return false;

                if (!TryResolveAll(out var stats))
                    return false;

                Log($"ResolveNow: enabled={stats.enabled}, updated={stats.updated}, duplicatesDeleted={stats.duplicatesDeleted}, rejected={stats.rejected}");

                try
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    if (requestCompilation)
                        CompilationPipeline.RequestScriptCompilation();
                }
                catch (Exception ex)
                {
                    LogWarning($"ResolveNow: refresh/compile failed: {ex.Message}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogWarning($"ResolveNow failed: {ex.Message}");
                return false;
            }
        }

        private struct ResolveStats
        {
            public int enabled;
            public int updated;
            public int duplicatesDeleted;
            public int rejected;
        }

        private static bool TryResolveAll(out ResolveStats stats)
        {
            stats = new ResolveStats();

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string packagesPath = Path.Combine(projectRoot, "Packages");
            string assetsPath = Path.Combine(projectRoot, "Assets");

            var roots = new[] { packagesPath, assetsPath }.Where(Directory.Exists).ToArray();
            if (roots.Length == 0)
                return false;

            string[] disabledFiles;
            try
            {
                disabledFiles = roots
                    .SelectMany(root => Directory.GetFiles(root, "*" + DisabledSuffix, SearchOption.AllDirectories))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return false;
            }

            if (disabledFiles.Length == 0)
                return false;

            if (IsVerbose())
            {
                Log($"TryResolveAll: found {disabledFiles.Length} file(s). roots=[{string.Join(", ", roots)}]");
                foreach (var p in disabledFiles.Take(10))
                    Log($"TryResolveAll: sample '{p}'");
                if (disabledFiles.Length > 10)
                    Log($"TryResolveAll: (+{disabledFiles.Length - 10} more)");
            }

            foreach (var disabledFile in disabledFiles)
            {
                if (string.IsNullOrEmpty(disabledFile))
                    continue;

                if (!disabledFile.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string enabledFile = disabledFile.Substring(0, disabledFile.Length - DisabledSuffix.Length);
                string disabledMeta = disabledFile + ".meta";
                string enabledMeta = enabledFile + ".meta";

                try
                {
                    if (!File.Exists(enabledFile))
                    {
                        if (IsVerbose()) Log($"Enable (no conflict): '{disabledFile}' -> '{enabledFile}'");
                        MoveFileIfExists(disabledFile, enabledFile);
                        MoveFileIfExists(disabledMeta, enabledMeta);
                        TryRestoreOriginalGuidInMeta(enabledMeta);
                        stats.enabled++;
                        continue;
                    }

                    var decision = DetermineDecision(disabledFile, enabledFile);

                    if (decision == Decision.UpdateEnabledWithDisabled)
                    {
                        string backupEnabled = enabledFile + ".old";
                        string backupEnabledMeta = enabledMeta + ".old";

                        if (IsVerbose()) Log($"Update enabled with disabled: enabled='{enabledFile}' disabled='{disabledFile}' backup='{backupEnabled}'");
                        MoveFileIfExists(enabledFile, backupEnabled);
                        MoveFileIfExists(enabledMeta, backupEnabledMeta);

                        MoveFileIfExists(disabledFile, enabledFile);
                        MoveFileIfExists(disabledMeta, enabledMeta);
                        TryRestoreOriginalGuidInMeta(enabledMeta);

                        stats.updated++;
                        continue;
                    }

                    if (decision == Decision.DeleteDisabledAsDuplicate)
                    {
                        if (IsVerbose()) Log($"Delete disabled duplicate: '{disabledFile}' (enabled exists '{enabledFile}')");
                        DeleteFileIfExists(disabledFile);
                        DeleteFileIfExists(disabledMeta);
                        stats.duplicatesDeleted++;
                        continue;
                    }

                    string rejectedPath = enabledFile + ".incoming";
                    string rejectedMeta = rejectedPath + ".meta";
                    if (IsVerbose()) Log($"Reject (keep enabled): disabled='{disabledFile}' -> '{rejectedPath}' (enabled exists '{enabledFile}')");
                    MoveFileIfExists(disabledFile, rejectedPath);
                    MoveFileIfExists(disabledMeta, rejectedMeta);
                    stats.rejected++;
                }
                catch (Exception ex)
                {
                    LogWarning($"Failed to resolve '{Path.GetFileName(disabledFile)}': {ex.Message}");
                }
            }

            try
            {
                foreach (var root in roots)
                {
                    foreach (var meta in Directory.GetFiles(root, "*" + DisabledSuffix + ".meta", SearchOption.AllDirectories))
                    {
                        var disabled = meta.Substring(0, meta.Length - ".meta".Length);
                        if (!File.Exists(disabled))
                            DeleteFileIfExists(meta);
                    }
                }
            }
            catch { }

            return true;
        }

        private enum Decision
        {
            UpdateEnabledWithDisabled,
            DeleteDisabledAsDuplicate,
            RejectKeepEnabled
        }

        private static Decision DetermineDecision(string disabledFile, string enabledFile)
        {
            try
            {
                var disabledInfo = new FileInfo(disabledFile);
                var enabledInfo = new FileInfo(enabledFile);

                if (disabledInfo.Length == enabledInfo.Length)
                {
                    if (disabledInfo.Length <= 100 * 1024)
                    {
                        if (ComputeFileHash(disabledFile) == ComputeFileHash(enabledFile))
                            return Decision.DeleteDisabledAsDuplicate;
                    }
                    else
                    {
                        return Decision.DeleteDisabledAsDuplicate;
                    }
                }

                if (disabledInfo.LastWriteTimeUtc > enabledInfo.LastWriteTimeUtc)
                    return Decision.UpdateEnabledWithDisabled;

                return Decision.RejectKeepEnabled;
            }
            catch
            {
                return Decision.RejectKeepEnabled;
            }
        }

        private static void MoveFileIfExists(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
                return;

            if (!File.Exists(source))
                return;

            try
            {
                string dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
            }
            catch
            {
            }

            if (File.Exists(target))
            {
                try { File.Delete(target); }
                catch { }
            }

            File.Move(source, target);
        }

        private static void DeleteFileIfExists(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static void TryRestoreOriginalGuidInMeta(string metaPath)
        {
            try
            {
                if (string.IsNullOrEmpty(metaPath) || !File.Exists(metaPath))
                    return;

                string content = File.ReadAllText(metaPath);
                string originalGuid = ExtractOriginalGuidFromMetaContent(content);
                if (string.IsNullOrEmpty(originalGuid))
                    return;

                content = Regex.Replace(
                    content,
                    @"guid:\s*([a-f0-9]{32})",
                    $"guid: {originalGuid}",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline
                );

                content = Regex.Replace(
                    content,
                    @"(\s+userData:\s*)(?:['""])?YUCP_ORIGINAL_GUID=[a-f0-9]{32}(?:['""])?\s*$",
                    "$1",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline
                );
                content = Regex.Replace(
                    content,
                    @"(\s+userData:\s*)\{\s*""originalGuid""\s*:\s*""[a-f0-9]{32}""\s*\}\s*$",
                    "$1",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline
                );

                File.WriteAllText(metaPath, content);
            }
            catch
            {
            }
        }

        private static string ExtractOriginalGuidFromMetaContent(string metaContent)
        {
            try
            {
                var tokenMatch = Regex.Match(
                    metaContent,
                    @"userData:\s*(?:['""])?YUCP_ORIGINAL_GUID=([a-f0-9]{32})(?:['""])?\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline
                );
                if (tokenMatch.Success)
                    return tokenMatch.Groups[1].Value;

                var legacyMatch = Regex.Match(
                    metaContent,
                    @"userData:\s*(?:['""])?\{\s*""originalGuid""\s*:\s*""([a-f0-9]{32})""\s*\}(?:['""])?\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline
                );
                if (legacyMatch.Success)
                    return legacyMatch.Groups[1].Value;
            }
            catch { }

            return null;
        }

        private static string ComputeFileHash(string filePath)
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = md5.ComputeHash(stream);
                return string.Concat(hash.Select(b => b.ToString("x2")));
            }
        }
    }
}
