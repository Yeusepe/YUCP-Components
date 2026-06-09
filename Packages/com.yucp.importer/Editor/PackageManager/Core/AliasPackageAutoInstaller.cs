using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using PackageRegistrationEventArgs = UnityEditor.PackageManager.PackageRegistrationEventArgs;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    /// <summary>
    /// Detects VPM alias shim packages after VRCGet/VCC installs them and hands them to the
    /// server-authorized YUCP install flow.
    /// </summary>
    [InitializeOnLoad]
    internal static class AliasPackageAutoInstaller
    {
        private const string LogPrefix = "[YUCP PackageManager][AliasAutoInstaller]";
        private const string SessionKeyPrefix = "YUCP.PackageManager.AliasAutoInstaller.AttemptedV3.";
        private const double VpmAliasStatePollIntervalSeconds = 1.0d;
        private static readonly HashSet<string> ProcessingPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, HashSet<string>> AttemptSessionKeysByPackage =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private static bool s_scanQueued;
        private static string s_lastVpmAliasStateFingerprint;
        private static double s_nextVpmAliasStatePollTime;

        static AliasPackageAutoInstaller()
        {
            if (!PackageManagerRuntimeSettings.IsEnabled())
            {
                return;
            }

            UnityEditor.PackageManager.Events.registeredPackages += OnRegisteredPackages;
            EditorApplication.update += PollVpmAliasState;
            s_lastVpmAliasStateFingerprint = BuildVpmAliasStateFingerprint(GetProjectPackagesDirectory());
            QueueScan();
        }

        internal static bool TryBuildAliasPackageMetadata(
            string packageId,
            string packageJson,
            out PackageMetadata metadata,
            out string error)
        {
            metadata = null;
            error = null;

            PackageMetadataExtractor.PackageJsonImportData importData =
                PackageMetadataExtractor.ParsePackageJsonImportData(packageJson);
            if (importData == null)
            {
                error = "package.json could not be parsed.";
                return false;
            }

            if (!IsServerAuthorizedAlias(importData.aliasPackage))
            {
                error = "package.json does not declare a server-authorized YUCP alias.";
                return false;
            }

            string resolvedPackageId = !string.IsNullOrWhiteSpace(importData.packageName)
                ? importData.packageName
                : packageId;

            string packageDisplayName = !string.IsNullOrWhiteSpace(importData.aliasPackage?.packageDisplayName)
                ? importData.aliasPackage.packageDisplayName
                : importData.displayName;

            metadata = new PackageMetadata(!string.IsNullOrWhiteSpace(packageDisplayName)
                ? packageDisplayName
                : resolvedPackageId)
            {
                version = importData.version ?? string.Empty,
                author = importData.author ?? string.Empty,
                description = importData.description ?? string.Empty,
                dependencies = importData.dependencies ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                aliasPackage = importData.aliasPackage.Clone(),
            };

            metadata.aliasPackage.packageName = !string.IsNullOrWhiteSpace(metadata.aliasPackage.packageName)
                ? metadata.aliasPackage.packageName
                : resolvedPackageId;
            metadata.aliasPackage.packageDisplayName = !string.IsNullOrWhiteSpace(metadata.aliasPackage.packageDisplayName)
                ? metadata.aliasPackage.packageDisplayName
                : metadata.packageName;
            metadata.aliasPackage.packageVersion = !string.IsNullOrWhiteSpace(metadata.aliasPackage.packageVersion)
                ? metadata.aliasPackage.packageVersion
                : metadata.version;

            return true;
        }

        internal static bool IsServerAuthorizedAlias(AliasPackageContract aliasPackage)
        {
            return aliasPackage != null &&
                string.Equals(aliasPackage.kind, "alias-v1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(aliasPackage.installStrategy, UpdateDeliveryService.ServerAuthorizedInstallStrategy, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(aliasPackage.aliasId);
        }

        internal static string BuildSessionKey(PackageMetadata metadata)
        {
            AliasPackageContract aliasPackage = metadata?.aliasPackage;
            string packageName = aliasPackage?.packageName ?? metadata?.packageName ?? "unknown-package";
            string version = aliasPackage?.packageVersion ?? metadata?.version ?? "unknown-version";
            return BuildSessionKey(packageName, version, "unknown-instance");
        }

        internal static string BuildSessionKey(PackageMetadata metadata, string packageJsonPath)
        {
            AliasPackageContract aliasPackage = metadata?.aliasPackage;
            string packageName = aliasPackage?.packageName ?? metadata?.packageName ?? "unknown-package";
            string version = aliasPackage?.packageVersion ?? metadata?.version ?? "unknown-version";
            return BuildSessionKey(packageName, version, ResolveInstallInstanceFingerprint(packageJsonPath));
        }

        private static string BuildSessionKey(string packageName, string version, string installInstance)
        {
            return SessionKeyPrefix +
                (string.IsNullOrWhiteSpace(packageName) ? "unknown-package" : packageName.Trim()) +
                "@" +
                (string.IsNullOrWhiteSpace(version) ? "unknown-version" : version.Trim()) +
                "#" +
                (string.IsNullOrWhiteSpace(installInstance) ? "unknown-instance" : installInstance.Trim());
        }

        internal static string BuildInstallPromptMessage(PackageMetadata metadata)
        {
            string packageLabel = !string.IsNullOrWhiteSpace(metadata?.packageName)
                ? metadata.packageName.Trim()
                : "this package";

            return $"Ready to finish installing '{packageLabel}'.\n\n" +
                "Verify access to import the authorized package into this project.";
        }

        private static void OnRegisteredPackages(PackageRegistrationEventArgs args)
        {
            if (args?.removed != null)
            {
                foreach (PackageInfo package in args.removed)
                {
                    ClearRecordedInstallAttempts(package?.name, package?.version);
                }
            }

            QueueScan();
        }

        private static void PollVpmAliasState()
        {
            if (!PackageManagerRuntimeSettings.IsEnabled())
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now < s_nextVpmAliasStatePollTime)
            {
                return;
            }

            s_nextVpmAliasStatePollTime = now + VpmAliasStatePollIntervalSeconds;

            string fingerprint = BuildVpmAliasStateFingerprint(GetProjectPackagesDirectory());
            if (string.Equals(fingerprint, s_lastVpmAliasStateFingerprint, StringComparison.Ordinal))
            {
                return;
            }

            s_lastVpmAliasStateFingerprint = fingerprint;
            Debug.Log($"{LogPrefix} Detected VPM package state change; scanning for server-authorized package aliases.");
            QueueScan();
        }

        private static void QueueScan()
        {
            if (s_scanQueued)
            {
                return;
            }

            s_scanQueued = true;
            EditorApplication.delayCall += ScanInstalledAliasPackages;
        }

        private static void ScanInstalledAliasPackages()
        {
            s_scanQueued = false;
            if (!PackageManagerRuntimeSettings.IsEnabled())
            {
                return;
            }

            var inspectedPackageRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PackageInfo[] packages;
            try
            {
                packages = PackageInfo.GetAllRegisteredPackages();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} Could not inspect registered packages: {ex.Message}");
                packages = Array.Empty<PackageInfo>();
            }

            foreach (PackageInfo package in packages)
            {
                TrySchedulePackage(package, inspectedPackageRoots);
            }

            ScanEmbeddedPackageFolders(inspectedPackageRoots);
        }

        internal static string[] FindEmbeddedPackageJsonPaths(string packagesDirectory, ISet<string> excludedResolvedPaths)
        {
            if (string.IsNullOrWhiteSpace(packagesDirectory) || !Directory.Exists(packagesDirectory))
            {
                return Array.Empty<string>();
            }

            var packageJsonPaths = new List<string>();
            foreach (string packageDirectory in Directory.GetDirectories(packagesDirectory))
            {
                string normalizedDirectory = NormalizePath(packageDirectory);
                if (!string.IsNullOrEmpty(normalizedDirectory) && excludedResolvedPaths?.Contains(normalizedDirectory) == true)
                {
                    continue;
                }

                string packageJsonPath = Path.Combine(packageDirectory, "package.json");
                if (File.Exists(packageJsonPath))
                {
                    packageJsonPaths.Add(packageJsonPath);
                }
            }

            return packageJsonPaths.ToArray();
        }

        internal static string BuildVpmAliasStateFingerprint(string packagesDirectory)
        {
            if (string.IsNullOrWhiteSpace(packagesDirectory) || !Directory.Exists(packagesDirectory))
            {
                return "packages:missing";
            }

            var parts = new List<string>();
            AddFileFingerprint(parts, "vpm", Path.Combine(packagesDirectory, "vpm-manifest.json"));

            string[] packageJsonPaths = FindEmbeddedPackageJsonPaths(packagesDirectory, null);
            Array.Sort(packageJsonPaths, StringComparer.OrdinalIgnoreCase);
            foreach (string packageJsonPath in packageJsonPaths)
            {
                AddFileFingerprint(parts, "package", packageJsonPath);
            }

            return string.Join("|", parts);
        }

        private static void ScanEmbeddedPackageFolders(ISet<string> inspectedPackageRoots)
        {
            string packagesDirectory = GetProjectPackagesDirectory();
            foreach (string packageJsonPath in FindEmbeddedPackageJsonPaths(packagesDirectory, inspectedPackageRoots))
            {
                TrySchedulePackageJson(Path.GetFileName(Path.GetDirectoryName(packageJsonPath)), packageJsonPath);
            }
        }

        private static string GetProjectPackagesDirectory()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return string.IsNullOrWhiteSpace(projectRoot) ? null : Path.Combine(projectRoot, "Packages");
        }

        private static void TrySchedulePackage(PackageInfo package, ISet<string> inspectedPackageRoots)
        {
            if (package == null || string.IsNullOrWhiteSpace(package.resolvedPath))
            {
                return;
            }

            string normalizedResolvedPath = NormalizePath(package.resolvedPath);
            if (!string.IsNullOrEmpty(normalizedResolvedPath))
            {
                inspectedPackageRoots?.Add(normalizedResolvedPath);
            }

            TrySchedulePackageJson(package.name, Path.Combine(package.resolvedPath, "package.json"));
        }

        private static void TrySchedulePackageJson(string packageId, string packageJsonPath)
        {
            if (string.IsNullOrWhiteSpace(packageJsonPath) || !File.Exists(packageJsonPath))
            {
                return;
            }

            string packageJson;
            try
            {
                packageJson = File.ReadAllText(packageJsonPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} Could not read package.json for '{packageId}': {ex.Message}");
                return;
            }

            if (!TryBuildAliasPackageMetadata(packageId, packageJson, out PackageMetadata metadata, out _))
            {
                return;
            }

            if (IsAlreadyManaged(metadata))
            {
                return;
            }

            string sessionKey = BuildSessionKey(metadata, packageJsonPath);
            if (SessionState.GetBool(sessionKey, false))
            {
                return;
            }

            if (!ProcessingPackages.Add(metadata.aliasPackage.packageName))
            {
                return;
            }

            SessionState.SetBool(sessionKey, true);
            RecordInstallAttempt(metadata, sessionKey);
            Debug.Log($"{LogPrefix} Queued server-authorized alias package '{metadata.aliasPackage.packageName}' for completion.");
            EditorApplication.delayCall += () => PromptAndInstall(metadata);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private static void AddFileFingerprint(List<string> parts, string label, string path)
        {
            string normalizedPath = NormalizePath(path) ?? label;
            try
            {
                if (!File.Exists(path))
                {
                    parts.Add($"{label}:{normalizedPath}:missing");
                    return;
                }

                var fileInfo = new FileInfo(path);
                parts.Add($"{label}:{normalizedPath}:{fileInfo.Length}:{fileInfo.LastWriteTimeUtc.Ticks}");
            }
            catch (Exception ex)
            {
                parts.Add($"{label}:{normalizedPath}:error:{ex.GetType().Name}");
            }
        }

        private static bool IsAlreadyManaged(PackageMetadata metadata)
        {
            string packageId = metadata?.aliasPackage?.packageName;
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            InstalledPackageRegistry registry = InstalledPackageRegistry.Load();
            InstalledPackageInfo installed = registry?.GetPackage(packageId);
            if (installed == null || installed.aliasPackage == null)
            {
                return false;
            }

            string expectedVersion = metadata.aliasPackage.packageVersion ?? metadata.version ?? string.Empty;
            return string.IsNullOrWhiteSpace(expectedVersion) ||
                string.Equals(installed.installedVersion, expectedVersion, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(installed.version, expectedVersion, StringComparison.OrdinalIgnoreCase);
        }

        private static void PromptAndInstall(PackageMetadata metadata)
        {
            string packageId = metadata?.aliasPackage?.packageName ?? metadata?.packageName ?? "this package";
            try
            {
                if (!EditorUtility.DisplayDialog(
                    "YUCP Importer",
                        BuildInstallPromptMessage(metadata),
                        "Verify and Install",
                        "Later"))
                {
                    return;
                }

                string serverUrl = LicenseServerResolver.GetLicenseServerUrl();
                if (string.IsNullOrWhiteSpace(serverUrl))
                {
                    EditorUtility.DisplayDialog(
                        "YUCP Importer",
                        "The YUCP verification server URL is not configured. Open Project Settings > YUCP Package Manager and choose a trusted server.",
                        "OK");
                    ClearInstallAttemptForRetry(metadata);
                    return;
                }

                if (!CreatorIdentityOAuthService.IsSignedIn())
                {
                    StartSignInThenInstall(serverUrl, metadata);
                    return;
                }

                ResolveAndApply(serverUrl, metadata);
            }
            finally
            {
                ProcessingPackages.Remove(packageId);
            }
        }

        private static void StartSignInThenInstall(string serverUrl, PackageMetadata metadata)
        {
            EditorUtility.DisplayDialog(
                "YUCP License Verification",
                "Your browser will open so you can sign in and verify access before YUCP imports the real package.",
                "Continue");

            Task signInTask = CreatorIdentityOAuthService.SignInAsync(
                serverUrl,
                () => EditorApplication.delayCall += () => ResolveAndApply(serverUrl, metadata),
                error => EditorApplication.delayCall += () =>
                {
                    ClearInstallAttemptForRetry(metadata);
                    EditorUtility.DisplayDialog(
                        "YUCP License Verification",
                        string.IsNullOrWhiteSpace(error) ? "Sign-in failed." : error,
                        "OK");
                });

            signInTask.ContinueWith(task =>
            {
                if (task.Exception == null)
                {
                    return;
                }

                EditorApplication.delayCall += () =>
                {
                    ClearInstallAttemptForRetry(metadata);
                    Debug.LogError($"{LogPrefix} Sign-in failed: {task.Exception.GetBaseException().Message}");
                    EditorUtility.DisplayDialog(
                        "YUCP License Verification",
                        $"Sign-in failed: {task.Exception.GetBaseException().Message}",
                        "OK");
                };
            });
        }

        private static void ResolveAndApply(string serverUrl, PackageMetadata metadata)
        {
            string packageLabel = metadata?.packageName ?? metadata?.aliasPackage?.packageName ?? "this package";

            try
            {
                EditorUtility.DisplayProgressBar(
                    "Resolving YUCP Install",
                    $"Fetching the authorized install plan for '{packageLabel}'...",
                    0.35f);

                if (!UpdateDeliveryService.TryResolveAuthorizedInstallPlan(
                    serverUrl,
                    metadata.aliasPackage,
                    out UpdateDeliveryService.AliasInstallPlan installPlan,
                    out string resolveError))
                {
                    EditorUtility.ClearProgressBar();
                    if (RequiresReauthentication(resolveError) &&
                        EditorUtility.DisplayDialog(
                            "YUCP License Verification",
                            resolveError + "\n\nSign in again now?",
                            "Sign In",
                            "Later"))
                    {
                        StartSignInThenInstall(serverUrl, metadata);
                    }
                    else
                    {
                        ClearInstallAttemptForRetry(metadata);
                        EditorUtility.DisplayDialog(
                            "Complete YUCP Install",
                            string.IsNullOrWhiteSpace(resolveError)
                                ? "Could not resolve the authorized install plan."
                                : resolveError,
                            "OK");
                    }

                    return;
                }

                EditorUtility.ClearProgressBar();
                if (!AliasInstallPlanConfirmationService.ConfirmInstall(metadata))
                {
                    ClearInstallAttemptForRetry(metadata);
                    return;
                }

                EditorUtility.DisplayProgressBar(
                    "Installing YUCP Package",
                    $"Installing '{packageLabel}' through the authorized VPM resolver...",
                    0.7f);

                if (!UpdateDeliveryService.TryApplyAuthorizedInstallPlan(installPlan, out string applyError))
                {
                    EditorUtility.ClearProgressBar();
                    ClearInstallAttemptForRetry(metadata);
                    EditorUtility.DisplayDialog(
                        "Complete YUCP Install",
                        string.IsNullOrWhiteSpace(applyError)
                            ? "Could not apply the authorized install plan."
                            : applyError,
                        "OK");
                    return;
                }

                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog(
                    "YUCP Install Complete",
                    $"'{packageLabel}' was verified and installed.",
                    "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                ClearInstallAttemptForRetry(metadata);
                Debug.LogError($"{LogPrefix} Failed to complete alias install for '{packageLabel}': {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog(
                    "Complete YUCP Install",
                    $"Could not complete the YUCP install for '{packageLabel}': {ex.Message}",
                    "OK");
            }
        }

        private static bool RequiresReauthentication(string error)
        {
            return !string.IsNullOrWhiteSpace(error) &&
                error.IndexOf("package delivery access", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void RecordInstallAttempt(PackageMetadata metadata, string sessionKey)
        {
            string packageName = metadata?.aliasPackage?.packageName;
            if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(sessionKey))
            {
                return;
            }

            if (!AttemptSessionKeysByPackage.TryGetValue(packageName, out HashSet<string> sessionKeys))
            {
                sessionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                AttemptSessionKeysByPackage[packageName] = sessionKeys;
            }

            sessionKeys.Add(sessionKey);
        }

        private static void ClearInstallAttemptForRetry(PackageMetadata metadata)
        {
            string packageName = metadata?.aliasPackage?.packageName;
            if (!string.IsNullOrWhiteSpace(packageName) &&
                AttemptSessionKeysByPackage.TryGetValue(packageName, out HashSet<string> sessionKeys))
            {
                foreach (string sessionKey in sessionKeys)
                {
                    SessionState.SetBool(sessionKey, false);
                }

                AttemptSessionKeysByPackage.Remove(packageName);
                return;
            }

            SessionState.SetBool(BuildSessionKey(metadata), false);
        }

        private static void ClearRecordedInstallAttempts(string packageName, string packageVersion)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return;
            }

            SessionState.SetBool(BuildSessionKey(packageName, packageVersion, "unknown-instance"), false);

            if (AttemptSessionKeysByPackage.TryGetValue(packageName, out HashSet<string> sessionKeys))
            {
                foreach (string sessionKey in sessionKeys)
                {
                    SessionState.SetBool(sessionKey, false);
                }

                AttemptSessionKeysByPackage.Remove(packageName);
            }

            ProcessingPackages.Remove(packageName);
        }

        private static string ResolveInstallInstanceFingerprint(string packageJsonPath)
        {
            if (string.IsNullOrWhiteSpace(packageJsonPath))
            {
                return "unknown-instance";
            }

            try
            {
                string fullPath = Path.GetFullPath(packageJsonPath);
                string packageDirectory = Path.GetDirectoryName(fullPath);
                long fileWriteTicks = File.Exists(fullPath)
                    ? File.GetLastWriteTimeUtc(fullPath).Ticks
                    : 0;
                long directoryWriteTicks = !string.IsNullOrEmpty(packageDirectory) && Directory.Exists(packageDirectory)
                    ? Directory.GetLastWriteTimeUtc(packageDirectory).Ticks
                    : 0;
                return $"{NormalizePath(packageDirectory ?? fullPath)}:{directoryWriteTicks}:{fileWriteTicks}";
            }
            catch (Exception)
            {
                return NormalizePath(packageJsonPath) ?? "unknown-instance";
            }
        }
    }
}


