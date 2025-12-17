#define YUCP_PACKAGE_MANAGER_DISABLED
#if !YUCP_PACKAGE_MANAGER_DISABLED
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Resolves "*.yucp_disabled" files created by YUCP exports into their enabled counterparts.
    /// This runs as part of the import flow so it works even when Package Guardian is disabled.
    /// </summary>
    internal static class YucpDisabledFileResolver
    {
        private const string DisabledSuffix = ".yucp_disabled";
        private const string PendingKey = "YUCP.PackageManager.ResolveYucpDisabled.Pending";
        private const string PendingStartTicksKey = "YUCP.PackageManager.ResolveYucpDisabled.StartTicksUtc";
        private const string PendingTimeoutSecondsKey = "YUCP.PackageManager.ResolveYucpDisabled.TimeoutSeconds";
        private const string VerboseKey = "YUCP.PackageManager.ResolveYucpDisabled.Verbose";
        private static bool _isRunning;
        private static bool _compilationHooked;

        private static bool IsVerbose()
        {
            try { return EditorPrefs.GetBool(VerboseKey, false); }
            catch { return false; }
        }

        private static void Log(string message)
        {
            Debug.Log($"[YUCP PackageManager][YucpDisabledFileResolver] {message}");
        }

        private static void LogWarning(string message)
        {
            Debug.LogWarning($"[YUCP PackageManager][YucpDisabledFileResolver] {message}");
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

                // Important: don't subtract wall-clock elapsed time here. Imports often spend most of their time compiling,
                // and we intentionally don't want compilation time to burn the resolver timeout.
                EnsureCompilationHook(timeoutSeconds);
                ScheduleResolveAfterImport(timeoutSeconds);
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to resume pending resolve: {ex.Message}");
                // Don't clear pending here; allow another reload attempt.
            }
        }

        /// <summary>
        /// Marks a pending resolve that will run after the next domain reload (or immediately if no reload happens).
        /// This is the safest way to survive imports that trigger a forced domain reload right after completion.
        /// </summary>
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

                    // If we're compiling, defer. This callback should fire after compilation, so normally false here,
                    // but keep it safe in case Unity re-enters compilation immediately.
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    {
                        if (IsVerbose())
                            Log($"compilationFinished: still not ready. isCompiling={EditorApplication.isCompiling}, isUpdating={EditorApplication.isUpdating}");
                        return;
                    }

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
                // ignore
            }
        }

        private static void ClearPending()
        {
            try { EditorPrefs.SetBool(PendingKey, false); } catch { }
            try { EditorPrefs.DeleteKey(PendingStartTicksKey); } catch { }
            try { EditorPrefs.DeleteKey(PendingTimeoutSecondsKey); } catch { }
        }

        /// <summary>
        /// Polls for any .yucp_disabled files under the project's Packages folder (where DirectVpmInstaller moves content),
        /// resolves them, then forces a refresh + compilation to activate editor scripts.
        /// </summary>
        public static void ScheduleResolveAfterImport(double timeoutSeconds = 15.0)
        {
            if (_isRunning)
            {
                if (IsVerbose())
                    Log($"ScheduleResolveAfterImport ignored (already running). timeoutSeconds={timeoutSeconds:0.###}");
                return;
            }

            _isRunning = true;

            // DirectVpmInstaller and other installers may perform System.IO operations for a while after Unity's
            // importPackageCompleted fires. Give this a generous timeout.
            if (timeoutSeconds < 5.0)
                timeoutSeconds = 5.0;

            // Critical: RequestScriptCompilation() may trigger a domain reload immediately, which would wipe delayCall/update callbacks.
            // Persist a pending flag so we can resume after reload and still resolve the files.
            MarkPending(timeoutSeconds);

            // Timeout is counted only while Unity is in a "ready" state (not compiling/updating).
            // Otherwise a long compile would consume the whole timeout and we'd never attempt resolution.
            double remainingReadySeconds = timeoutSeconds;
            double lastTime = EditorApplication.timeSinceStartup;

            // Avoid multiple concurrent schedules.
            bool isSubscribed = false;
            bool everSawDisabledFiles = false;
            bool loggedSkipState = false;
            double lastVerboseTickLog = 0;
            int tickCount = 0;

            void Tick()
            {
                tickCount++;

                double now = EditorApplication.timeSinceStartup;
                double dt = Math.Max(0, now - lastTime);
                lastTime = now;

                // Wait for Unity to be in a stable state
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    if (!loggedSkipState)
                    {
                        loggedSkipState = true;
                        Log($"Tick skipped: isCompiling={EditorApplication.isCompiling}, isUpdating={EditorApplication.isUpdating}");
                    }
                    return;
                }

                loggedSkipState = false;
                remainingReadySeconds -= dt;

                if (remainingReadySeconds <= 0)
                {
                    if (isSubscribed) EditorApplication.update -= Tick;
                    if (!everSawDisabledFiles)
                        Log("No .yucp_disabled files were found to resolve (timeout).");
                    ClearPending();
                    _isRunning = false;
                    return;
                }

                if (IsVerbose())
                {
                    // Throttle verbose tick logs to ~2/sec
                    if (EditorApplication.timeSinceStartup - lastVerboseTickLog > 0.5)
                    {
                        lastVerboseTickLog = EditorApplication.timeSinceStartup;
                        Log($"Tick: remainingReadySeconds={remainingReadySeconds:0.###} everSaw={everSawDisabledFiles} ticks={tickCount}");
                    }
                }

                if (!TryResolveAll(out var stats))
                    return; // keep polling (DirectVpmInstaller may not have moved files yet)

                everSawDisabledFiles = true;
                Log($"Resolved .yucp_disabled files: enabled={stats.enabled}, updated={stats.updated}, duplicatesDeleted={stats.duplicatesDeleted}, rejected={stats.rejected} (ticks={tickCount})");

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

            // Subscribe on next frame to avoid reentrancy.
            EditorApplication.delayCall += () =>
            {
                if (isSubscribed) return;
                isSubscribed = true;
                Log($"Waiting for .yucp_disabled files to land (post-install), then resolving... timeoutSeconds={timeoutSeconds:0.###}");
                EditorApplication.update += Tick;
            };
        }

        private struct ResolveStats
        {
            public int enabled;
            public int updated;
            public int duplicatesDeleted;
            public int rejected;
        }

        /// <summary>
        /// Attempts to resolve .yucp_disabled files under Packages/. Returns true if any were found (resolved or handled).
        /// Returns false if none exist (caller may keep polling).
        /// </summary>
        private static bool TryResolveAll(out ResolveStats stats)
        {
            stats = new ResolveStats();

            // Scan both Assets/ and Packages/ because .unitypackage imports land in Assets/, while
            // DirectVpmInstaller later moves content into Packages/ via System.IO.
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
                    // No conflict: enable by renaming the file (and meta if present).
                    if (!File.Exists(enabledFile))
                    {
                        if (IsVerbose()) Log($"Enable (no conflict): '{disabledFile}' -> '{enabledFile}'");
                        MoveFileIfExists(disabledFile, enabledFile);
                        MoveFileIfExists(disabledMeta, enabledMeta);
                        stats.enabled++;
                        continue;
                    }

                    // Conflict: decide whether this is an update, a duplicate, or should be rejected.
                    var decision = DetermineDecision(disabledFile, enabledFile);

                    if (decision == Decision.UpdateEnabledWithDisabled)
                    {
                        // Keep a backup of the old enabled file so nothing is lost.
                        string backupEnabled = enabledFile + ".old";
                        string backupEnabledMeta = enabledMeta + ".old";

                        if (IsVerbose()) Log($"Update enabled with disabled: enabled='{enabledFile}' disabled='{disabledFile}' backup='{backupEnabled}'");
                        MoveFileIfExists(enabledFile, backupEnabled);
                        MoveFileIfExists(enabledMeta, backupEnabledMeta);

                        MoveFileIfExists(disabledFile, enabledFile);
                        MoveFileIfExists(disabledMeta, enabledMeta);

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

                    // Rejected: keep for inspection, but rename so it won't be picked up again as ".yucp_disabled"
                    // and won't compile as C#.
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

                // Same size: likely duplicate; confirm with hash for small files.
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

                // Disabled newer => treat as update.
                if (disabledInfo.LastWriteTimeUtc > enabledInfo.LastWriteTimeUtc)
                    return Decision.UpdateEnabledWithDisabled;

                // Otherwise keep enabled, but keep incoming for inspection (renamed by caller).
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

            // Ensure target directory exists
            try
            {
                string dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
            }
            catch
            {
                // ignore
            }

            if (File.Exists(target))
            {
                try { File.Delete(target); }
                catch { /* ignore */ }
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
                // ignore
            }
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
#endif


