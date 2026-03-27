using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using YUCP.Importer.Editor.PackageManager;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class ImportedAssetRollbackService
    {
        internal static bool TryRollbackPackage(InstalledPackageInfo packageInfo, out string error)
        {
            if (packageInfo == null)
            {
                error = "Installed package information was missing.";
                return false;
            }

            return TryRollbackImportedAssets(packageInfo.installedFiles, out error);
        }

        internal static bool TryRollbackImportedAssets(IReadOnlyList<string> installedFiles, out string error)
        {
            error = null;
            if (installedFiles == null || installedFiles.Count == 0)
            {
                return true;
            }

            var failures = new List<string>();
            var parentFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assetPaths = installedFiles
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeAssetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string assetPath in assetPaths)
            {
                try
                {
                    CollectParentFolders(assetPath, parentFolders);
                    FileUtil.DeleteFileOrDirectory(assetPath);
                    FileUtil.DeleteFileOrDirectory(assetPath + ".meta");
                }
                catch (Exception ex)
                {
                    failures.Add($"{assetPath}: {ex.Message}");
                }
            }

            foreach (string folderPath in parentFolders.OrderByDescending(GetFolderDepth))
            {
                TryDeleteEmptyFolder(folderPath, failures);
            }

            AssetDatabase.Refresh();

            if (failures.Count > 0)
            {
                error = "Could not roll back some imported assets:\n - " + string.Join("\n - ", failures);
                return false;
            }

            return true;
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return assetPath.Replace('\\', '/').Trim();
        }

        private static void CollectParentFolders(string assetPath, ISet<string> parentFolders)
        {
            string current = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            while (!string.IsNullOrWhiteSpace(current) &&
                   !string.Equals(current, "Assets", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(current, "Packages", StringComparison.OrdinalIgnoreCase))
            {
                parentFolders.Add(current);
                current = Path.GetDirectoryName(current)?.Replace('\\', '/');
            }
        }

        private static void TryDeleteEmptyFolder(string assetPath, ICollection<string> failures)
        {
            if (!AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            string diskPath = AssetPathToDiskPath(assetPath);
            if (string.IsNullOrWhiteSpace(diskPath) || !Directory.Exists(diskPath))
            {
                return;
            }

            if (Directory.EnumerateFileSystemEntries(diskPath).Any())
            {
                return;
            }

            try
            {
                FileUtil.DeleteFileOrDirectory(assetPath);
                FileUtil.DeleteFileOrDirectory(assetPath + ".meta");
            }
            catch (Exception ex)
            {
                failures.Add($"{assetPath}: {ex.Message}");
            }
        }

        private static string AssetPathToDiskPath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static int GetFolderDepth(string assetPath)
        {
            return assetPath.Count(ch => ch == '/' || ch == '\\');
        }
    }
}
