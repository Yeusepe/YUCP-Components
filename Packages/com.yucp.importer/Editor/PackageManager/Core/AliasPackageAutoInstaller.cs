using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
        private const string PersistentPromptKeyPrefix = "YUCP.PackageManager.AliasAutoInstaller.PromptedV1.";
        private const string PersistentPromptIndexKeyPrefix = "YUCP.PackageManager.AliasAutoInstaller.PromptedIndexV1.";
        private static readonly HashSet<string> ProcessingPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, HashSet<string>> AttemptSessionKeysByPackage =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        private sealed class InstallFlowSession
        {
            public InstallFlowSession(PackageMetadata metadata)
            {
                Metadata = metadata;
            }

            public PackageMetadata Metadata { get; }
            public bool TerminalDialogShown { get; set; }
        }

        static AliasPackageAutoInstaller()
        {
            if (!PackageManagerRuntimeSettings.IsEnabled())
            {
                return;
            }

            // Unity raises registeredPackages after UPM applies package-list changes and after refresh/compile/domain reload.
            // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PackageManager.Events-registeredPackages.html
            UnityEditor.PackageManager.Events.registeredPackages += OnRegisteredPackages;
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
                "Verify access to import the authorized package into this project. If sign-in is needed, YUCP will open your browser and continue automatically.";
        }

        private static bool ConfirmEnrichedInstall(
            PackageMetadata requestedMetadata,
            UpdateDeliveryService.AliasInstallPlan installPlan,
            out PackageMetadata previewMetadata)
        {
            previewMetadata = null;
            try
            {
                previewMetadata = UpdateDeliveryService.BuildPreviewMetadataFromPlan(
                    installPlan,
                    requestedMetadata?.aliasPackage);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} Could not build install preview metadata: {ex.Message}");
            }

            string message = BuildEnrichedInstallMessage(previewMetadata, requestedMetadata);
            return YucpEditorDialog.DisplayDialog(
                "Install YUCP Package",
                message,
                "Install",
                "Cancel");
        }

        internal static string BuildEnrichedInstallMessage(
            PackageMetadata preview,
            PackageMetadata fallback)
        {
            PackageMetadata source = preview ?? fallback;
            string name = FirstNonEmptyValue(
                source?.packageName,
                fallback?.packageName,
                source?.aliasPackage?.packageDisplayName,
                "this package");

            var builder = new StringBuilder();
            builder.Append("Install '").Append(name).Append("'?").Append('\n');

            string version = FirstNonEmptyValue(source?.version, fallback?.version);
            if (!string.IsNullOrWhiteSpace(version))
            {
                builder.Append('\n').Append("Version: ").Append(version);
            }

            string creator = FirstNonEmptyValue(source?.author, fallback?.author);
            if (!string.IsNullOrWhiteSpace(creator))
            {
                builder.Append('\n').Append("Creator: ").Append(creator);
            }

            string tagline = source?.tagline;
            if (!string.IsNullOrWhiteSpace(tagline) &&
                !string.Equals(tagline, name, StringComparison.OrdinalIgnoreCase))
            {
                builder.Append('\n').Append('\n').Append(tagline.Trim());
            }

            builder.Append('\n').Append('\n')
                .Append("This package will be imported into your project through the authorized YUCP installer.");
            return builder.ToString();
        }

        private static string FirstNonEmptyValue(params string[] values)
        {
            if (values == null)
            {
                return null;
            }

            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static void OnRegisteredPackages(PackageRegistrationEventArgs args)
        {
            if (args == null)
            {
                return;
            }

            if (args?.removed != null)
            {
                // A package that is also re-added/upgraded in the same event is a version change or
                // reinstall, not a real uninstall. Only reconcile genuine removals.
                var retainedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                AddPackageNames(retainedNames, args.added);
                AddPackageNames(retainedNames, args.changedTo);

                var removedNames = new List<string>();
                foreach (PackageInfo package in args.removed)
                {
                    ClearRecordedInstallAttempts(package?.name, package?.version);
                    if (!string.IsNullOrWhiteSpace(package?.name))
                    {
                        removedNames.Add(package.name);
                    }
                }

                foreach (string packageId in ResolvePackageIdsToReconcileOnRemoval(
                    removedNames,
                    retainedNames,
                    LookupInstalledPackage))
                {
                    ScheduleRemovalReconciliation(packageId);
                }
            }

            if (args.changedFrom != null)
            {
                foreach (PackageInfo package in args.changedFrom)
                {
                    ClearRecordedInstallAttempts(package?.name, package?.version);
                }
            }

            if (args.added != null)
            {
                foreach (PackageInfo package in args.added)
                {
                    TrySchedulePackage(package, null);
                }
            }

            if (args.changedTo != null)
            {
                foreach (PackageInfo package in args.changedTo)
                {
                    TrySchedulePackage(package, null);
                }
            }
        }

        private static void AddPackageNames(ISet<string> names, IEnumerable<PackageInfo> packages)
        {
            if (names == null || packages == null)
            {
                return;
            }

            foreach (PackageInfo package in packages)
            {
                if (!string.IsNullOrWhiteSpace(package?.name))
                {
                    names.Add(package.name);
                }
            }
        }

        /// <summary>
        /// Determines which YUCP-managed package IDs should have their imported files reconciled
        /// after Unity reports them as removed. Packages that are simultaneously re-added or upgraded
        /// (version changes / reinstalls) are skipped so an update does not delete imported assets.
        /// </summary>
        internal static List<string> ResolvePackageIdsToReconcileOnRemoval(
            IEnumerable<string> removedPackageNames,
            ICollection<string> retainedPackageNames,
            Func<string, InstalledPackageInfo> lookupInstalled)
        {
            var packageIds = new List<string>();
            if (removedPackageNames == null || lookupInstalled == null)
            {
                return packageIds;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string removedName in removedPackageNames)
            {
                if (string.IsNullOrWhiteSpace(removedName))
                {
                    continue;
                }

                if (retainedPackageNames != null && retainedPackageNames.Contains(removedName))
                {
                    // Re-added/upgraded in the same event: an update, not a removal.
                    continue;
                }

                InstalledPackageInfo installed = lookupInstalled(removedName);
                if (installed == null)
                {
                    continue;
                }

                string packageId = !string.IsNullOrWhiteSpace(installed.packageId)
                    ? installed.packageId
                    : removedName;
                if (seen.Add(packageId))
                {
                    packageIds.Add(packageId);
                }
            }

            return packageIds;
        }

        private static InstalledPackageInfo LookupInstalledPackage(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return null;
            }

            InstalledPackageRegistry registry = InstalledPackageRegistry.Load();
            return registry?.GetPackage(packageName);
        }

        private static void ScheduleRemovalReconciliation(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return;
            }

            // Defer until after UPM finishes applying the package-list change so the registry and
            // disk are in a settled state before the reconciler builds its removal plan.
            EditorApplication.delayCall += () =>
            {
                try
                {
                    Debug.Log($"{LogPrefix} Reconciling imported files for removed package '{packageId}'.");
                    PackageUninstaller.UninstallPackage(packageId, skipConfirmation: false);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LogPrefix} Failed to reconcile removal of '{packageId}': {ex.Message}\n{ex.StackTrace}");
                }
            };
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

            string persistentPromptKey = BuildPersistentPromptKey(metadata, packageJsonPath, packageJson);
            if (EditorPrefs.GetBool(persistentPromptKey, false))
            {
                return;
            }

            if (!ProcessingPackages.Add(metadata.aliasPackage.packageName))
            {
                return;
            }

            SessionState.SetBool(sessionKey, true);
            EditorPrefs.SetBool(persistentPromptKey, true);
            EditorPrefs.SetString(BuildPersistentPromptIndexKey(metadata), persistentPromptKey);
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

                parts.Add($"{label}:{normalizedPath}:{ComputeStableHash(File.ReadAllText(path))}");
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

            AliasPackageInstallStateManifest installState =
                AliasPackageInstallStateStore.Load(installed.installStateManifestPath);
            return IsManagedInstallCompleteForAlias(
                metadata,
                installed,
                installState,
                ProjectRelativePathExists);
        }

        internal static bool IsManagedInstallCompleteForAlias(
            PackageMetadata metadata,
            InstalledPackageInfo installed,
            AliasPackageInstallStateManifest installState,
            Func<string, bool> projectPathExists)
        {
            if (metadata?.aliasPackage == null || installed?.aliasPackage == null)
            {
                return false;
            }

            string expectedVersion = metadata.aliasPackage.packageVersion ?? metadata.version ?? string.Empty;
            bool versionMatches = string.IsNullOrWhiteSpace(expectedVersion) ||
                string.Equals(installed.installedVersion, expectedVersion, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(installed.version, expectedVersion, StringComparison.OrdinalIgnoreCase);
            if (!versionMatches)
            {
                return false;
            }

            if (installState == null)
            {
                return false;
            }

            var expectedTrackedPaths = BuildExpectedTrackedPaths(metadata);
            string packageJsonPath = BuildAliasPackageJsonPath(metadata.aliasPackage.packageName);
            bool currentMetadataDeclaresPayloadPaths = HasNonShimPath(expectedTrackedPaths, packageJsonPath);
            if (!currentMetadataDeclaresPayloadPaths &&
                !HasNonShimPath(installState.managedPaths, packageJsonPath))
            {
                return false;
            }

            foreach (string expectedPath in expectedTrackedPaths)
            {
                if (!ContainsPath(installState.managedPaths, expectedPath) &&
                    !ContainsPath(installState.generatedPaths, expectedPath))
                {
                    return false;
                }

                if (projectPathExists != null && !projectPathExists(NormalizeRelativePath(expectedPath)))
                {
                    return false;
                }
            }

            return true;
        }

        internal static string BuildPersistentPromptKey(
            PackageMetadata metadata,
            string packageJsonPath,
            string packageJson)
        {
            AliasPackageContract aliasPackage = metadata?.aliasPackage;
            string packageName = aliasPackage?.packageName ?? metadata?.packageName ?? "unknown-package";
            string version = aliasPackage?.packageVersion ?? metadata?.version ?? "unknown-version";
            string aliasId = aliasPackage?.aliasId ?? "unknown-alias";
            string projectIdentity = ResolvePromptProjectIdentity(packageJsonPath);
            string packageJsonHash = ComputeStableHash(packageJson ?? string.Empty);
            return PersistentPromptKeyPrefix +
                ComputeStableHash($"{projectIdentity}|{packageName}|{version}|{aliasId}|{packageJsonHash}");
        }

        private static string BuildPersistentPromptIndexKey(PackageMetadata metadata)
        {
            AliasPackageContract aliasPackage = metadata?.aliasPackage;
            string packageName = aliasPackage?.packageName ?? metadata?.packageName ?? "unknown-package";
            string version = aliasPackage?.packageVersion ?? metadata?.version ?? "unknown-version";
            return BuildPersistentPromptIndexKey(packageName, version);
        }

        private static string BuildPersistentPromptIndexKey(string packageName, string version)
        {
            string projectRoot = ResolvePromptProjectIdentity(null);
            return PersistentPromptIndexKeyPrefix +
                ComputeStableHash($"{projectRoot}|{packageName ?? "unknown-package"}");
        }

        private static string ResolvePromptProjectIdentity(string packageJsonPath)
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                if (!string.IsNullOrWhiteSpace(projectRoot))
                {
                    return NormalizePath(projectRoot) ?? projectRoot;
                }
            }
            catch (Exception)
            {
            }

            try
            {
                string path = string.IsNullOrWhiteSpace(packageJsonPath)
                    ? Directory.GetCurrentDirectory()
                    : Path.GetFullPath(packageJsonPath);
                return NormalizePath(path) ?? path;
            }
            catch (Exception)
            {
                return "unknown-project";
            }
        }

        private static string ComputeStableHash(string value)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static List<string> BuildExpectedTrackedPaths(PackageMetadata metadata)
        {
            var paths = new List<string>();
            string packageJsonPath = BuildAliasPackageJsonPath(metadata?.aliasPackage?.packageName);
            AddDistinctPath(paths, packageJsonPath);
            AddDistinctPaths(paths, metadata?.aliasPackage?.installPlan?.managedPaths);
            AddDistinctPaths(paths, metadata?.aliasPackage?.installPlan?.generatedPaths);
            return paths;
        }

        private static string BuildAliasPackageJsonPath(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return null;
            }

            return $"Packages/{packageName.Trim()}/package.json";
        }

        private static void AddDistinctPaths(List<string> paths, IEnumerable<string> candidates)
        {
            if (paths == null || candidates == null)
            {
                return;
            }

            foreach (string candidate in candidates)
            {
                AddDistinctPath(paths, candidate);
            }
        }

        private static void AddDistinctPath(List<string> paths, string candidate)
        {
            string normalized = NormalizeRelativePath(candidate);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (!ContainsPath(paths, normalized))
            {
                paths.Add(normalized);
            }
        }

        private static bool HasNonShimPath(IEnumerable<string> paths, string packageJsonPath)
        {
            string normalizedPackageJsonPath = NormalizeRelativePath(packageJsonPath);
            if (paths == null)
            {
                return false;
            }

            foreach (string path in paths)
            {
                string normalized = NormalizeRelativePath(path);
                if (!string.IsNullOrWhiteSpace(normalized) &&
                    !string.Equals(normalized, normalizedPackageJsonPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsPath(IEnumerable<string> paths, string expectedPath)
        {
            string normalizedExpectedPath = NormalizeRelativePath(expectedPath);
            if (paths == null || string.IsNullOrWhiteSpace(normalizedExpectedPath))
            {
                return false;
            }

            foreach (string path in paths)
            {
                if (string.Equals(
                        NormalizeRelativePath(path),
                        normalizedExpectedPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ProjectRelativePathExists(string relativePath)
        {
            string normalized = NormalizeRelativePath(relativePath);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string normalizedRoot = projectRoot
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                string absolutePath = Path.GetFullPath(
                    Path.Combine(normalizedRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
                if (!absolutePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return File.Exists(absolutePath) || Directory.Exists(absolutePath);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string NormalizeRelativePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimStart('/').Trim();
        }

        private static void PromptAndInstall(PackageMetadata metadata)
        {
            string packageId = metadata?.aliasPackage?.packageName ?? metadata?.packageName ?? "this package";
            var session = new InstallFlowSession(metadata);
            try
            {
                if (!YucpEditorDialog.DisplayDialog(
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
                    ShowTerminalInstallDialog(
                        session,
                        "YUCP Importer",
                        "The YUCP verification server URL is not configured. Open Project Settings > YUCP Package Manager and choose a trusted server.",
                        clearForRetry: true);
                    return;
                }

                if (!CreatorIdentityOAuthService.IsSignedIn())
                {
                    StartSignInThenInstall(serverUrl, session);
                    return;
                }

                ResolveAndApply(serverUrl, session);
            }
            finally
            {
                ProcessingPackages.Remove(packageId);
            }
        }

        private static void StartSignInThenInstall(
            string serverUrl,
            InstallFlowSession session,
            bool reauthenticationAttempted = false)
        {
            Task signInTask = CreatorIdentityOAuthService.SignInAsync(
                serverUrl,
                () => EditorApplication.delayCall += () => ResolveAndApply(serverUrl, session, reauthenticationAttempted),
                error => EditorApplication.delayCall += () =>
                {
                    ShowTerminalInstallDialog(
                        session,
                        "YUCP License Verification",
                        string.IsNullOrWhiteSpace(error) ? "Sign-in failed." : error,
                        clearForRetry: true);
                });

            signInTask.ContinueWith(task =>
            {
                if (task.Exception == null)
                {
                    return;
                }

                EditorApplication.delayCall += () =>
                {
                    Debug.LogError($"{LogPrefix} Sign-in failed: {task.Exception.GetBaseException().Message}");
                    ShowTerminalInstallDialog(
                        session,
                        "YUCP License Verification",
                        $"Sign-in failed: {task.Exception.GetBaseException().Message}",
                        clearForRetry: true);
                };
            });
        }

        private static void ResolveAndApply(
            string serverUrl,
            InstallFlowSession session,
            bool reauthenticationAttempted = false)
        {
            PackageMetadata metadata = session?.Metadata;
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
                    if (RequiresReauthentication(resolveError) && !reauthenticationAttempted)
                    {
                        StartSignInThenInstall(serverUrl, session, true);
                    }
                    else
                    {
                        ShowTerminalInstallDialog(
                            session,
                            "Complete YUCP Install",
                            string.IsNullOrWhiteSpace(resolveError)
                                ? "Could not resolve the authorized install plan."
                                : resolveError,
                            clearForRetry: true);
                    }

                    return;
                }

                EditorUtility.ClearProgressBar();

                // Show the real package details fetched from the server before importing anything,
                // so the user confirms what is actually being installed (not just the shim name).
                if (!ConfirmEnrichedInstall(metadata, installPlan, out PackageMetadata previewMetadata))
                {
                    ClearInstallAttemptForRetry(session.Metadata);
                    return;
                }

                packageLabel = previewMetadata?.packageName ?? packageLabel;
                EditorUtility.DisplayProgressBar(
                    "Installing YUCP Package",
                    $"Installing '{packageLabel}' through the authorized VPM resolver...",
                    0.7f);

                if (!UpdateDeliveryService.TryApplyAuthorizedInstallPlan(installPlan, out string applyError))
                {
                    EditorUtility.ClearProgressBar();
                    ShowTerminalInstallDialog(
                        session,
                        "Complete YUCP Install",
                        string.IsNullOrWhiteSpace(applyError)
                            ? "Could not apply the authorized install plan."
                            : applyError,
                        clearForRetry: true);
                    return;
                }

                EditorUtility.ClearProgressBar();
                Debug.Log($"{LogPrefix} '{packageLabel}' was verified and installed.");
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"{LogPrefix} Failed to complete alias install for '{packageLabel}': {ex.Message}\n{ex.StackTrace}");
                ShowTerminalInstallDialog(
                    session,
                    "Complete YUCP Install",
                    $"Could not complete the YUCP install for '{packageLabel}': {ex.Message}",
                    clearForRetry: true);
            }
        }

        private static void ShowTerminalInstallDialog(
            InstallFlowSession session,
            string title,
            string message,
            bool clearForRetry)
        {
            if (session == null || session.TerminalDialogShown)
            {
                return;
            }

            session.TerminalDialogShown = true;
            if (clearForRetry)
            {
                ClearInstallAttemptForRetry(session.Metadata);
            }

            YucpEditorDialog.DisplayErrorDialog(
                title,
                string.IsNullOrWhiteSpace(message) ? "The install did not complete." : message);
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
            // Package refresh/change events are part of a successful install. Keep the durable
            // "already auto-prompted" key so Unity does not reopen the same prompt after install.

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
                string packageJsonHash = File.Exists(fullPath)
                    ? ComputeStableHash(File.ReadAllText(fullPath))
                    : "missing";
                return $"{NormalizePath(packageDirectory ?? fullPath)}:{packageJsonHash}";
            }
            catch (Exception)
            {
                return NormalizePath(packageJsonPath) ?? "unknown-instance";
            }
        }
    }
}


