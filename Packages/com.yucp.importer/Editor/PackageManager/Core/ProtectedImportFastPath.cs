using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace YUCP.Importer.Editor.PackageManager
{
    internal static class ProtectedImportFastPath
    {
        internal sealed class PreparedDirectApplyState
        {
            internal string quarantineRoot;
            internal readonly List<QuarantinedAssetEntry> quarantinedAssets = new List<QuarantinedAssetEntry>();
        }

        internal sealed class QuarantinedAssetEntry
        {
            internal string source;
            internal string destination;
            internal bool isDirectory;
        }

        internal static bool TryPrepareForDirectApply(
            InstalledPackageInfo packageInfo,
            out PreparedDirectApplyState preparedState,
            out bool requiresAssetRefresh,
            out string message)
        {
            preparedState = null;
            requiresAssetRefresh = false;

            if (packageInfo?.protectedPayload == null)
            {
                message = "The import did not include a protected payload descriptor.";
                return false;
            }

            if (!HasTempInstallDescriptor(packageInfo.installedFiles))
            {
                message = "The import did not include a temp-install descriptor.";
                return false;
            }

            if (!AreDependenciesSatisfied(packageInfo.dependencies, out string dependencyMessage))
            {
                message = dependencyMessage;
                return false;
            }

            List<string> unexpectedDisabledAssets = packageInfo.installedFiles
                .Where(IsDisabledAssetPath)
                .Where(path => !IsExpectedDisabledAssetPath(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (unexpectedDisabledAssets.Count > 0)
            {
                message =
                    $"The import still contains non-installer disabled assets ({unexpectedDisabledAssets[0]}), so the generated installer handoff is still required.";
                return false;
            }

            List<string> transientAssets = packageInfo.installedFiles
                .Where(IsTransientManagedAssetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!TryQuarantineTransientAssets(transientAssets, out preparedState, out string cleanupError))
            {
                message = cleanupError;
                return false;
            }

            requiresAssetRefresh = transientAssets.Count > 0;
            packageInfo.installedFiles = packageInfo.installedFiles
                .Where(path => !IsTransientManagedAssetPath(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            message = transientAssets.Count > 0
                ? $"Removed {transientAssets.Count} transient installer assets and skipped the generated installer compile handoff."
                : "Skipped the generated installer compile handoff because the protected shell already has everything required for direct apply.";
            return true;
        }

        internal static void CommitPreparedDirectApply(PreparedDirectApplyState preparedState)
        {
            if (preparedState == null)
            {
                return;
            }

            TryDeleteDirectory(preparedState.quarantineRoot);
        }

        internal static void RollbackPreparedDirectApply(PreparedDirectApplyState preparedState)
        {
            if (preparedState == null)
            {
                return;
            }

            RollbackQuarantinedAssets(preparedState.quarantinedAssets);
            TryDeleteDirectory(preparedState.quarantineRoot);
        }

        internal static bool IsTransientManagedAssetPath(string assetPath)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return false;
            }

            if (normalizedPath.StartsWith("Packages/yucp.packageguardian/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!normalizedPath.StartsWith("Packages/yucp.installed-packages/Editor/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string fileName = Path.GetFileName(normalizedPath);
            return fileName.StartsWith("YUCP_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExpectedDisabledAssetPath(string assetPath)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return false;
            }

            if (!normalizedPath.StartsWith("Packages/yucp.installed-packages/Editor/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string fileName = Path.GetFileName(normalizedPath);
            return fileName.StartsWith("YUCP_", StringComparison.OrdinalIgnoreCase) &&
                   fileName.EndsWith(".yucp_disabled", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasTempInstallDescriptor(IEnumerable<string> installedFiles)
        {
            foreach (string path in installedFiles ?? Enumerable.Empty<string>())
            {
                if (PackageMetadataExtractor.IsTempInstallPackageJsonPath(path))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDisabledAssetPath(string assetPath)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            return !string.IsNullOrWhiteSpace(normalizedPath) &&
                   normalizedPath.EndsWith(".yucp_disabled", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryQuarantineTransientAssets(
            IReadOnlyList<string> transientAssets,
            out PreparedDirectApplyState preparedState,
            out string error)
        {
            preparedState = null;
            error = null;
            if (transientAssets == null || transientAssets.Count == 0)
            {
                return true;
            }

            string quarantineRoot = Path.Combine(
                Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..")),
                "Library",
                "YUCP",
                "ProtectedImportFastPath",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(quarantineRoot);

            preparedState = new PreparedDirectApplyState
            {
                quarantineRoot = quarantineRoot,
            };

            try
            {
                foreach (string transientAsset in transientAssets)
                {
                    string normalizedPath = NormalizeAssetPath(transientAsset);
                    if (string.IsNullOrWhiteSpace(normalizedPath))
                    {
                        continue;
                    }

                    string diskPath = AssetPathToDiskPath(normalizedPath);
                    if (!File.Exists(diskPath) && !Directory.Exists(diskPath))
                    {
                        continue;
                    }

                    string destinationPath = Path.Combine(quarantineRoot, preparedState.quarantinedAssets.Count.ToString("D4"));
                    string destinationDirectory = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    bool isDirectory = Directory.Exists(diskPath);
                    if (isDirectory)
                    {
                        Directory.Move(diskPath, destinationPath);
                    }
                    else
                    {
                        File.Move(diskPath, destinationPath);
                    }

                    preparedState.quarantinedAssets.Add(new QuarantinedAssetEntry
                    {
                        source = diskPath,
                        destination = destinationPath,
                        isDirectory = isDirectory,
                    });

                    string metaPath = diskPath + ".meta";
                    if (File.Exists(metaPath))
                    {
                        string metaDestinationPath = destinationPath + ".meta";
                        File.Move(metaPath, metaDestinationPath);
                        preparedState.quarantinedAssets.Add(new QuarantinedAssetEntry
                        {
                            source = metaPath,
                            destination = metaDestinationPath,
                            isDirectory = false,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                RollbackPreparedDirectApply(preparedState);
                preparedState = null;
                error = $"Failed to remove transient installer artifacts before direct protected apply: {ex.Message}";
                return false;
            }
            return true;
        }

        private static bool AreDependenciesSatisfied(
            IReadOnlyDictionary<string, string> dependencies,
            out string message)
        {
            foreach (KeyValuePair<string, string> dependency in dependencies ?? new Dictionary<string, string>())
            {
                string installedVersion = GetInstalledPackageVersion(dependency.Key);
                if (string.IsNullOrWhiteSpace(installedVersion))
                {
                    message = $"The project is missing required dependency '{dependency.Key}'.";
                    return false;
                }

                string normalizedRequirement = NormalizeRequirement(dependency.Value);
                if (HasPrereleaseTag(installedVersion) || HasPrereleaseTag(normalizedRequirement))
                {
                    if (!string.Equals(
                            NormalizeVersionText(installedVersion),
                            NormalizeVersionText(normalizedRequirement),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        message =
                            $"Dependency '{dependency.Key}' uses prerelease version metadata, so the generated installer still owns that handoff.";
                        return false;
                    }

                    continue;
                }

                if (IsExactVersionRequirement(normalizedRequirement))
                {
                    int compare = CompareVersions(installedVersion, normalizedRequirement);
                    if (compare > 0)
                    {
                        message =
                            $"Dependency '{dependency.Key}' is newer than the requested exact version {normalizedRequirement}, so the generated installer still owns that downgrade-sensitive handoff.";
                        return false;
                    }

                    if (compare < 0)
                    {
                        message =
                            $"Dependency '{dependency.Key}' requires {normalizedRequirement}, but the project only has {installedVersion}.";
                        return false;
                    }

                    continue;
                }

                if (!VersionSatisfiesRequirement(installedVersion, normalizedRequirement))
                {
                    message =
                        $"Dependency '{dependency.Key}' requires {normalizedRequirement}, but the project only has {installedVersion}.";
                    return false;
                }
            }

            message = null;
            return true;
        }

        private static string GetInstalledPackageVersion(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return null;
            }

            string packageJsonPath = Path.Combine("Packages", packageName, "package.json");
            if (!File.Exists(packageJsonPath))
            {
                return UnityEditor.PackageManager.PackageInfo
                    .GetAllRegisteredPackages()
                    .FirstOrDefault(package => string.Equals(package.name, packageName, StringComparison.OrdinalIgnoreCase))
                    ?.version;
            }

            try
            {
                JObject packageData = JObject.Parse(File.ReadAllText(packageJsonPath));
                return packageData["version"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static bool VersionSatisfiesRequirement(string installedVersion, string requirement)
        {
            requirement = NormalizeRequirement(requirement);

            if (string.IsNullOrEmpty(requirement) || requirement == "*")
            {
                return true;
            }

            if (requirement.StartsWith(">=", StringComparison.Ordinal))
            {
                string minVersion = requirement.Substring(2).Trim();
                return CompareVersions(installedVersion, minVersion) >= 0;
            }

            if (requirement.StartsWith("^", StringComparison.Ordinal))
            {
                string baseVersion = requirement.Substring(1).Trim();
                (int major, int minor, int patch) baseParts = ParseVersion(baseVersion);
                (int major, int minor, int patch) installedParts = ParseVersion(installedVersion);

                if (baseParts.major != installedParts.major)
                {
                    return false;
                }

                if (baseParts.major == 0)
                {
                    if (baseParts.minor == 0)
                    {
                        return installedParts.minor == 0 && installedParts.patch == baseParts.patch;
                    }

                    if (baseParts.minor != installedParts.minor)
                    {
                        return false;
                    }
                }

                return CompareVersions(installedVersion, baseVersion) >= 0;
            }

            if (requirement.StartsWith("~", StringComparison.Ordinal))
            {
                string baseVersion = requirement.Substring(1).Trim();
                (int major, int minor, int patch) baseParts = ParseVersion(baseVersion);
                (int major, int minor, int patch) installedParts = ParseVersion(installedVersion);

                if (baseParts.major != installedParts.major || baseParts.minor != installedParts.minor)
                {
                    return false;
                }

                return CompareVersions(installedVersion, baseVersion) >= 0;
            }

            return CompareVersions(installedVersion, requirement) == 0;
        }

        private static int CompareVersions(string version1, string version2)
        {
            (int major, int minor, int patch) v1 = ParseVersion(version1);
            (int major, int minor, int patch) v2 = ParseVersion(version2);

            if (v1.major != v2.major)
            {
                return v1.major.CompareTo(v2.major);
            }

            if (v1.minor != v2.minor)
            {
                return v1.minor.CompareTo(v2.minor);
            }

            return v1.patch.CompareTo(v2.patch);
        }

        private static (int major, int minor, int patch) ParseVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return (0, 0, 0);
            }

            string normalizedVersion = version.Trim().TrimStart('v', 'V');
            int dashIndex = normalizedVersion.IndexOf('-');
            if (dashIndex > 0)
            {
                normalizedVersion = normalizedVersion.Substring(0, dashIndex);
            }

            string[] parts = normalizedVersion.Split('.');
            int major = parts.Length > 0 ? SafeParseVersionPart(parts[0]) : 0;
            int minor = parts.Length > 1 ? SafeParseVersionPart(parts[1]) : 0;
            int patch = parts.Length > 2 ? SafeParseVersionPart(parts[2]) : 0;
            return (major, minor, patch);
        }

        private static string NormalizeRequirement(string requirement)
        {
            return string.IsNullOrWhiteSpace(requirement) ? string.Empty : requirement.Trim();
        }

        private static bool HasPrereleaseTag(string version)
        {
            string normalized = NormalizeVersionText(version);
            return !string.IsNullOrWhiteSpace(normalized) && normalized.IndexOf('-') >= 0;
        }

        private static string NormalizeVersionText(string version)
        {
            return string.IsNullOrWhiteSpace(version) ? string.Empty : version.Trim().TrimStart('v', 'V');
        }

        private static bool IsExactVersionRequirement(string requirement)
        {
            string normalized = NormalizeRequirement(requirement);
            if (string.IsNullOrEmpty(normalized) || normalized == "*")
            {
                return false;
            }

            return !normalized.StartsWith(">=", StringComparison.Ordinal) &&
                   !normalized.StartsWith("^", StringComparison.Ordinal) &&
                   !normalized.StartsWith("~", StringComparison.Ordinal);
        }

        private static int SafeParseVersionPart(string value)
        {
            return int.TryParse(value, out int parsed) ? parsed : 0;
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return assetPath?.Replace('\\', '/').Trim();
        }

        private static string AssetPathToDiskPath(string assetPath)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return null;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            return Path.Combine(projectRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void RollbackQuarantinedAssets(IEnumerable<QuarantinedAssetEntry> movedAssets)
        {
            foreach (QuarantinedAssetEntry move in movedAssets.Reverse().ToArray())
            {
                try
                {
                    string sourceDirectory = Path.GetDirectoryName(move.source);
                    if (!string.IsNullOrWhiteSpace(sourceDirectory))
                    {
                        Directory.CreateDirectory(sourceDirectory);
                    }

                    if (move.isDirectory)
                    {
                        if (Directory.Exists(move.destination))
                        {
                            Directory.Move(move.destination, move.source);
                        }
                    }
                    else if (File.Exists(move.destination))
                    {
                        File.Move(move.destination, move.source);
                    }
                }
                catch
                {
                    // Leave the original failure to surface; this rollback is best-effort.
                }
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, true);
            }
            catch
            {
                // Quarantine lives under Library/YUCP and is safe to leave behind if cleanup races.
            }
        }
    }
}
