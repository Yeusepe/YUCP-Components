using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class AliasPackageInstallStateStore
    {
        private const string WorkspaceRelativeRoot = ".yucp-dvi/Importer/InstallState";
        private const string ManifestFileSuffix = ".install-state.json";

        internal static bool TryPersist(InstalledPackageInfo packageInfo, out string manifestRelativePath, out string error)
        {
            manifestRelativePath = null;
            error = null;

            if (!ShouldTrack(packageInfo))
            {
                return false;
            }

            try
            {
                manifestRelativePath = BuildManifestRelativePath(packageInfo.aliasPackage.aliasId);
                AliasPackageInstallStateManifest manifest = BuildManifest(packageInfo, manifestRelativePath);
                string absolutePath = GetAbsoluteProjectPath(manifestRelativePath);
                string directory = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(absolutePath, Serialize(manifest));
                packageInfo.installStateManifestPath = manifestRelativePath;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static AliasPackageInstallStateManifest Load(string manifestRelativePath)
        {
            if (string.IsNullOrWhiteSpace(manifestRelativePath))
            {
                return null;
            }

            string absolutePath = GetAbsoluteProjectPath(manifestRelativePath);
            if (!File.Exists(absolutePath))
            {
                return null;
            }

            return Deserialize(File.ReadAllText(absolutePath));
        }

        internal static bool Delete(string manifestRelativePath)
        {
            if (string.IsNullOrWhiteSpace(manifestRelativePath))
            {
                return false;
            }

            string absolutePath = GetAbsoluteProjectPath(manifestRelativePath);
            if (!File.Exists(absolutePath))
            {
                return false;
            }

            File.Delete(absolutePath);
            return true;
        }

        internal static string BuildManifestRelativePath(string aliasId)
        {
            string safeAliasId = SanitizeFileName(string.IsNullOrWhiteSpace(aliasId) ? "unknown-alias" : aliasId.Trim());
            return $"{WorkspaceRelativeRoot}/{safeAliasId}{ManifestFileSuffix}";
        }

        internal static AliasPackageInstallStateManifest BuildManifest(
            InstalledPackageInfo packageInfo,
            string manifestRelativePath)
        {
            if (packageInfo == null)
            {
                throw new ArgumentNullException(nameof(packageInfo));
            }

            if (!ShouldTrack(packageInfo))
            {
                throw new InvalidOperationException("Install-state manifests require alias package metadata.");
            }

            AliasPackageContract aliasPackage = packageInfo.aliasPackage.Clone();
            List<string> managedPaths = NormalizeDistinctPaths(packageInfo.installedFiles);
            var generatedPaths = new List<string>();
            if (!string.IsNullOrWhiteSpace(manifestRelativePath))
            {
                string normalizedManifestPath = NormalizeRelativePath(manifestRelativePath);
                if (!generatedPaths.Any(path => string.Equals(path, normalizedManifestPath, StringComparison.OrdinalIgnoreCase)))
                {
                    generatedPaths.Add(normalizedManifestPath);
                }
            }

            var sharedPaths = new List<string>();
            var manifest = new AliasPackageInstallStateManifest
            {
                aliasId = aliasPackage.aliasId ?? string.Empty,
                aliasVersion = !string.IsNullOrWhiteSpace(aliasPackage.packageVersion)
                    ? aliasPackage.packageVersion
                    : packageInfo.installedVersion ?? packageInfo.version ?? string.Empty,
                packageId = packageInfo.packageId ?? string.Empty,
                packageName = aliasPackage.packageName ?? string.Empty,
                packageDisplayName = !string.IsNullOrWhiteSpace(aliasPackage.packageDisplayName)
                    ? aliasPackage.packageDisplayName
                    : packageInfo.packageName ?? string.Empty,
                installedVersion = packageInfo.installedVersion ?? packageInfo.version ?? string.Empty,
                installedAt = packageInfo.installedDate ?? string.Empty,
                managedPaths = managedPaths,
                generatedPaths = generatedPaths,
                sharedPaths = sharedPaths,
                fileHashes = BuildFileHashRecords(packageInfo, managedPaths, generatedPaths),
            };

            return manifest;
        }

        internal static string Serialize(AliasPackageInstallStateManifest manifest)
        {
            return JsonConvert.SerializeObject(manifest, Formatting.Indented);
        }

        internal static AliasPackageInstallStateManifest Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<AliasPackageInstallStateManifest>(json);
        }

        internal static bool ShouldTrack(InstalledPackageInfo packageInfo)
        {
            return packageInfo?.aliasPackage != null &&
                string.Equals(packageInfo.aliasPackage.kind, "alias-v1", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(packageInfo.aliasPackage.aliasId);
        }

        private static List<InstallStateFileHashRecord> BuildFileHashRecords(
            InstalledPackageInfo packageInfo,
            IEnumerable<string> managedPaths,
            IEnumerable<string> generatedPaths)
        {
            var expectedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (packageInfo.fileHashes != null)
            {
                foreach (PackageFileHashEntry entry in packageInfo.fileHashes)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.path))
                    {
                        continue;
                    }

                    expectedHashes[NormalizeRelativePath(entry.path)] = entry.hash ?? string.Empty;
                }
            }

            var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddPaths(candidatePaths, managedPaths);
            AddPaths(candidatePaths, generatedPaths);
            foreach (string expectedPath in expectedHashes.Keys)
            {
                candidatePaths.Add(expectedPath);
            }

            var records = new List<InstallStateFileHashRecord>();
            foreach (string relativePath in candidatePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string absolutePath = GetAbsoluteProjectPath(relativePath);
                string observedHash = File.Exists(absolutePath) ? ComputeFileHash(absolutePath) : string.Empty;
                expectedHashes.TryGetValue(relativePath, out string expectedHash);
                records.Add(new InstallStateFileHashRecord
                {
                    path = relativePath,
                    expectedSha256 = expectedHash ?? string.Empty,
                    observedSha256 = observedHash ?? string.Empty,
                });
            }

            return records;
        }

        private static void AddPaths(HashSet<string> destination, IEnumerable<string> paths)
        {
            if (destination == null || paths == null)
            {
                return;
            }

            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                destination.Add(NormalizeRelativePath(path));
            }
        }

        private static List<string> NormalizeDistinctPaths(IEnumerable<string> paths)
        {
            var normalizedPaths = new List<string>();
            if (paths == null)
            {
                return normalizedPaths;
            }

            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string normalized = NormalizeRelativePath(path);
                if (!normalizedPaths.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    normalizedPaths.Add(normalized);
                }
            }

            return normalizedPaths;
        }

        private static string ComputeFileHash(string absolutePath)
        {
            using (var stream = File.OpenRead(absolutePath))
            using (var sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(stream);
                return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string GetAbsoluteProjectPath(string relativePath)
        {
            string normalized = NormalizeRelativePath(relativePath);
            if (Path.IsPathRooted(normalized))
            {
                return normalized;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (normalized.StartsWith(".yucp-dvi/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(Path.Combine(projectRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            }

            return Path.GetFullPath(Path.Combine(projectRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string NormalizeRelativePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
        }

        private static string SanitizeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var buffer = new char[value.Length];
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (Array.IndexOf(invalid, current) >= 0 || current == '/' || current == '\\' || current == ':')
                {
                    current = '-';
                }

                buffer[i] = current;
            }

            string sanitized = new string(buffer).Trim('-');
            return string.IsNullOrEmpty(sanitized) ? "unknown-alias" : sanitized;
        }
    }
}
