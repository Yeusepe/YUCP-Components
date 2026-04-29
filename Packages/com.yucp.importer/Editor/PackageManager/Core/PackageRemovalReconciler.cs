using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    public static class PackageRemovalReconciler
    {
        public static PackageRemovalPlan BuildPlan(
            InstalledPackageInfo packageInfo,
            AliasPackageInstallStateManifest installState)
        {
            var plan = new PackageRemovalPlan();
            var ownershipLookup = BuildOwnershipLookup(packageInfo, installState);
            var installedHashes = BuildInstalledHashLookup(packageInfo, installState);

            foreach (KeyValuePair<string, PackageRemovalOwnershipClass> trackedPath in ownershipLookup
                         .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                plan.entries.Add(BuildEntry(trackedPath.Key, trackedPath.Value, ownershipLookup, installedHashes));
            }

            return plan;
        }

        private static Dictionary<string, PackageRemovalOwnershipClass> BuildOwnershipLookup(
            InstalledPackageInfo packageInfo,
            AliasPackageInstallStateManifest installState)
        {
            var ownershipLookup = new Dictionary<string, PackageRemovalOwnershipClass>(StringComparer.OrdinalIgnoreCase);

            if (installState != null)
            {
                AddPaths(ownershipLookup, installState.managedPaths, PackageRemovalOwnershipClass.Managed);
                AddPaths(ownershipLookup, installState.generatedPaths, PackageRemovalOwnershipClass.Generated);
                AddPaths(ownershipLookup, installState.sharedPaths, PackageRemovalOwnershipClass.Shared);
            }
            else
            {
                AddPaths(ownershipLookup, packageInfo?.installedFiles, PackageRemovalOwnershipClass.Managed);
            }

            if (!string.IsNullOrWhiteSpace(packageInfo?.installStateManifestPath))
            {
                AddPath(ownershipLookup, packageInfo.installStateManifestPath, PackageRemovalOwnershipClass.Generated);
            }

            return ownershipLookup;
        }

        private static Dictionary<string, string> BuildInstalledHashLookup(
            InstalledPackageInfo packageInfo,
            AliasPackageInstallStateManifest installState)
        {
            var hashLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (installState?.fileHashes != null)
            {
                foreach (InstallStateFileHashRecord record in installState.fileHashes)
                {
                    if (record == null || string.IsNullOrWhiteSpace(record.path))
                    {
                        continue;
                    }

                    string normalizedPath = NormalizeRelativePath(record.path);
                    hashLookup[normalizedPath] = GetInstalledHash(record.expectedSha256, record.observedSha256);
                }
            }
            else if (packageInfo?.fileHashes != null)
            {
                foreach (PackageFileHashEntry record in packageInfo.fileHashes)
                {
                    if (record == null || string.IsNullOrWhiteSpace(record.path))
                    {
                        continue;
                    }

                    hashLookup[NormalizeRelativePath(record.path)] = record.hash ?? string.Empty;
                }
            }

            return hashLookup;
        }

        private static PackageRemovalPlanEntry BuildEntry(
            string relativePath,
            PackageRemovalOwnershipClass ownershipClass,
            IReadOnlyDictionary<string, PackageRemovalOwnershipClass> ownershipLookup,
            IReadOnlyDictionary<string, string> installedHashes)
        {
            string absolutePath = ResolveProjectRelativePath(relativePath);
            bool fileExists = File.Exists(absolutePath);
            bool directoryExists = Directory.Exists(absolutePath);
            bool existsOnDisk = fileExists || directoryExists;

            var entry = new PackageRemovalPlanEntry
            {
                path = relativePath,
                ownershipClass = ownershipClass,
                existsOnDisk = existsOnDisk,
                isDirectory = directoryExists,
            };

            if (ownershipClass == PackageRemovalOwnershipClass.Shared)
            {
                entry.state = PackageRemovalReconcileState.SharedPathPreserve;
                entry.details = "Shared install-plan path is always preserved.";
                return entry;
            }

            if (!existsOnDisk)
            {
                entry.state = PackageRemovalReconcileState.MissingByUser;
                entry.details = "Tracked path is already missing on disk.";
                return entry;
            }

            if (fileExists)
            {
                ClassifyFileEntry(entry, absolutePath, installedHashes);
                return entry;
            }

            ClassifyDirectoryEntry(entry, absolutePath, ownershipLookup, installedHashes);
            return entry;
        }

        private static void ClassifyFileEntry(
            PackageRemovalPlanEntry entry,
            string absolutePath,
            IReadOnlyDictionary<string, string> installedHashes)
        {
            string currentSha256 = ComputeFileHash(absolutePath);
            installedHashes.TryGetValue(entry.path, out string installedSha256);
            entry.currentSha256 = currentSha256;
            entry.installedSha256 = installedSha256 ?? string.Empty;

            if (string.IsNullOrWhiteSpace(entry.installedSha256))
            {
                entry.state = PackageRemovalReconcileState.ModifiedByUser;
                entry.details = "Tracked file has no stored install hash, so it is preserved.";
                return;
            }

            if (string.Equals(entry.installedSha256, currentSha256, StringComparison.OrdinalIgnoreCase))
            {
                entry.state = PackageRemovalReconcileState.SafeToRemove;
                entry.details = "Current file matches the last installed hash.";
                return;
            }

            entry.state = PackageRemovalReconcileState.ModifiedByUser;
            entry.details = "Current file differs from the last installed hash.";
        }

        private static void ClassifyDirectoryEntry(
            PackageRemovalPlanEntry entry,
            string absolutePath,
            IReadOnlyDictionary<string, PackageRemovalOwnershipClass> ownershipLookup,
            IReadOnlyDictionary<string, string> installedHashes)
        {
            string[] files = Directory.GetFiles(absolutePath, "*", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                entry.state = PackageRemovalReconcileState.SafeToRemove;
                entry.details = "Tracked directory is empty.";
                return;
            }

            foreach (string filePath in files)
            {
                string relativeFilePath = NormalizeProjectPath(filePath);
                PackageRemovalOwnershipClass descendantOwnership = ResolveOwnership(relativeFilePath, ownershipLookup);
                if (descendantOwnership == PackageRemovalOwnershipClass.Shared)
                {
                    entry.state = PackageRemovalReconcileState.ModifiedByUser;
                    entry.details = "Tracked directory contains shared paths that must be preserved.";
                    return;
                }

                if (!installedHashes.TryGetValue(relativeFilePath, out string installedSha256) ||
                    string.IsNullOrWhiteSpace(installedSha256))
                {
                    entry.state = PackageRemovalReconcileState.ModifiedByUser;
                    entry.details = "Tracked directory contains files without stored install hashes.";
                    return;
                }

                string currentSha256 = ComputeFileHash(filePath);
                if (!string.Equals(installedSha256, currentSha256, StringComparison.OrdinalIgnoreCase))
                {
                    entry.state = PackageRemovalReconcileState.ModifiedByUser;
                    entry.details = $"Tracked directory contains modified file '{relativeFilePath}'.";
                    return;
                }
            }

            entry.state = PackageRemovalReconcileState.SafeToRemove;
            entry.details = "Tracked directory contents match the last installed hashes.";
        }

        private static PackageRemovalOwnershipClass ResolveOwnership(
            string relativePath,
            IReadOnlyDictionary<string, PackageRemovalOwnershipClass> ownershipLookup)
        {
            string normalizedPath = NormalizeRelativePath(relativePath);
            string current = normalizedPath;
            while (!string.IsNullOrEmpty(current))
            {
                if (ownershipLookup.TryGetValue(current, out PackageRemovalOwnershipClass ownershipClass))
                {
                    return ownershipClass;
                }

                int separatorIndex = current.LastIndexOf('/');
                if (separatorIndex < 0)
                {
                    break;
                }

                current = current.Substring(0, separatorIndex);
            }

            return PackageRemovalOwnershipClass.Managed;
        }

        private static void AddPaths(
            IDictionary<string, PackageRemovalOwnershipClass> ownershipLookup,
            IEnumerable<string> paths,
            PackageRemovalOwnershipClass ownershipClass)
        {
            if (paths == null)
            {
                return;
            }

            foreach (string path in paths)
            {
                AddPath(ownershipLookup, path, ownershipClass);
            }
        }

        private static void AddPath(
            IDictionary<string, PackageRemovalOwnershipClass> ownershipLookup,
            string path,
            PackageRemovalOwnershipClass ownershipClass)
        {
            string normalizedPath = NormalizeRelativePath(path);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return;
            }

            if (ownershipLookup.TryGetValue(normalizedPath, out PackageRemovalOwnershipClass existingOwnership))
            {
                ownershipLookup[normalizedPath] = SelectOwnership(existingOwnership, ownershipClass);
                return;
            }

            ownershipLookup[normalizedPath] = ownershipClass;
        }

        private static PackageRemovalOwnershipClass SelectOwnership(
            PackageRemovalOwnershipClass current,
            PackageRemovalOwnershipClass candidate)
        {
            if (current == PackageRemovalOwnershipClass.Shared || candidate == PackageRemovalOwnershipClass.Shared)
            {
                return PackageRemovalOwnershipClass.Shared;
            }

            if (current == PackageRemovalOwnershipClass.Generated || candidate == PackageRemovalOwnershipClass.Generated)
            {
                return PackageRemovalOwnershipClass.Generated;
            }

            return PackageRemovalOwnershipClass.Managed;
        }

        private static string GetInstalledHash(string expectedSha256, string observedSha256)
        {
            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                return expectedSha256.Trim();
            }

            if (!string.IsNullOrWhiteSpace(observedSha256))
            {
                return observedSha256.Trim();
            }

            return string.Empty;
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

        private static string ResolveProjectRelativePath(string path)
        {
            string normalizedPath = NormalizeRelativePath(path);
            if (Path.IsPathRooted(normalizedPath))
            {
                return normalizedPath;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string NormalizeProjectPath(string absolutePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string normalizedRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedAbsolute = Path.GetFullPath(absolutePath);

            if (!normalizedAbsolute.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeRelativePath(normalizedAbsolute);
            }

            string relativePath = normalizedAbsolute.Substring(normalizedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return NormalizeRelativePath(relativePath);
        }

        private static string NormalizeRelativePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
        }
    }
}
