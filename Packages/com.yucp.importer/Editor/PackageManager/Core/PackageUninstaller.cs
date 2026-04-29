using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.PackageManager
{
    /// <summary>
    /// Handles package uninstallation (file deletion and registry cleanup)
    /// </summary>
    public static class PackageUninstaller
    {
        /// <summary>
        /// Uninstall a package by packageId
        /// Shows confirmation dialog before uninstalling
        /// </summary>
        public static bool UninstallPackage(string packageId, bool skipConfirmation = false)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                Debug.LogError("[PackageUninstaller] Cannot uninstall package with empty packageId");
                return false;
            }

            var registry = InstalledPackageRegistry.GetOrCreate();
            var package = registry.GetPackage(packageId);

            if (package == null)
            {
                Debug.LogWarning($"[PackageUninstaller] Package with ID {packageId} not found in registry");
                return false;
            }

            try
            {
                AliasPackageInstallStateManifest installState =
                    AliasPackageInstallStateStore.Load(package.installStateManifestPath);

                if (!UninstallPackage(package, installState, skipConfirmation))
                {
                    return false;
                }

                registry.UnregisterPackage(packageId);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageUninstaller] Error uninstalling package: {ex.Message}");
                Debug.LogException(ex);
                return false;
            }
        }

        public static bool UninstallPackage(
            InstalledPackageInfo package,
            AliasPackageInstallStateManifest installState,
            bool skipConfirmation = false,
            Func<PackageRemovalPlan, bool> confirmationHandler = null)
        {
            if (package == null)
            {
                Debug.LogError("[PackageUninstaller] Cannot uninstall a null package.");
                return false;
            }

            PackageRemovalPlan plan = PackageRemovalReconciler.BuildPlan(package, installState);
            if (!skipConfirmation && !ConfirmRemoval(package, plan, confirmationHandler))
            {
                return false;
            }

            PackageRemovalExecutionResult result = ExecuteRemovalPlan(plan);
            if (!result.Succeeded)
            {
                Debug.LogWarning($"[PackageUninstaller] Removal of '{package.packageName}' failed. " +
                    $"Deleted {result.deletedCount} path(s), {result.failedCount} failed.");
                return false;
            }

            int modifiedCount = plan.Count(PackageRemovalReconcileState.ModifiedByUser);
            int sharedCount = plan.Count(PackageRemovalReconcileState.SharedPathPreserve);
            int missingCount = plan.Count(PackageRemovalReconcileState.MissingByUser);
            Debug.Log($"[PackageUninstaller] Uninstalled package '{package.packageName}'. " +
                $"Deleted {result.deletedCount} path(s), preserved {modifiedCount + sharedCount} path(s), skipped {missingCount} missing path(s).");
            return true;
        }

        private static PackageRemovalExecutionResult ExecuteRemovalPlan(PackageRemovalPlan plan)
        {
            var result = new PackageRemovalExecutionResult();
            if (plan == null)
            {
                return result;
            }

            List<PackageRemovalPlanEntry> safeEntries = plan.GetEntries(PackageRemovalReconcileState.SafeToRemove);
            result.preservedCount = plan.Count(PackageRemovalReconcileState.ModifiedByUser) +
                plan.Count(PackageRemovalReconcileState.SharedPathPreserve);
            result.missingCount = plan.Count(PackageRemovalReconcileState.MissingByUser);

            foreach (PackageRemovalPlanEntry entry in safeEntries
                         .Where(item => item != null && !item.isDirectory)
                         .OrderByDescending(item => item.path.Count(character => character == '/')))
            {
                if (TryDeletePath(entry.path, entry.isDirectory, recursiveDirectoryDelete: false))
                {
                    result.deletedCount++;
                }
                else
                {
                    result.failedCount++;
                }
            }

            foreach (PackageRemovalPlanEntry entry in safeEntries
                         .Where(item => item != null && item.isDirectory)
                         .OrderByDescending(item => item.path.Count(character => character == '/')))
            {
                if (TryDeletePath(entry.path, entry.isDirectory, recursiveDirectoryDelete: true))
                {
                    result.deletedCount++;
                }
                else
                {
                    result.failedCount++;
                }
            }

            if (safeEntries.Count > 0)
            {
                AssetDatabase.Refresh();
            }

            return result;
        }

        private static bool ConfirmRemoval(
            InstalledPackageInfo package,
            PackageRemovalPlan plan,
            Func<PackageRemovalPlan, bool> confirmationHandler)
        {
            if (confirmationHandler != null)
            {
                return confirmationHandler(plan);
            }

            string packageName = !string.IsNullOrWhiteSpace(package?.packageName)
                ? package.packageName
                : package?.packageId ?? "package";

            int safeCount = plan?.Count(PackageRemovalReconcileState.SafeToRemove) ?? 0;
            int modifiedCount = plan?.Count(PackageRemovalReconcileState.ModifiedByUser) ?? 0;
            int sharedCount = plan?.Count(PackageRemovalReconcileState.SharedPathPreserve) ?? 0;
            int missingCount = plan?.Count(PackageRemovalReconcileState.MissingByUser) ?? 0;

            string message;
            if (plan != null && plan.HasDestructiveRisk())
            {
                string modifiedPreview = BuildPathPreview(
                    plan.GetEntries(PackageRemovalReconcileState.ModifiedByUser),
                    "Modified paths that will be preserved");
                message =
                    $"Uninstall '{packageName}'?\n\n" +
                    $"Safe to remove: {safeCount}\n" +
                    $"Modified by user and preserved: {modifiedCount}\n" +
                    $"Shared and preserved: {sharedCount}\n" +
                    $"Already missing: {missingCount}\n\n" +
                    "Only safe tracked paths will be removed. Modified or shared paths stay on disk.\n\n" +
                    modifiedPreview;
            }
            else
            {
                message =
                    $"Uninstall '{packageName}'?\n\n" +
                    $"Safe to remove: {safeCount}\n" +
                    $"Shared and preserved: {sharedCount}\n" +
                    $"Already missing: {missingCount}\n\n" +
                    "Only tracked safe paths will be removed.";
            }

            return EditorUtility.DisplayDialog(
                "Uninstall Package",
                message,
                "Remove Safe Files",
                "Cancel");
        }

        private static string BuildPathPreview(IReadOnlyList<PackageRemovalPlanEntry> entries, string label)
        {
            if (entries == null || entries.Count == 0)
            {
                return string.Empty;
            }

            const int PreviewCount = 5;
            var previewLines = new List<string>
            {
                $"{label}:"
            };

            int count = Math.Min(entries.Count, PreviewCount);
            for (int i = 0; i < count; i++)
            {
                previewLines.Add($"- {entries[i].path}");
            }

            if (entries.Count > PreviewCount)
            {
                previewLines.Add($"- ...and {entries.Count - PreviewCount} more");
            }

            return string.Join("\n", previewLines);
        }

        private static bool TryDeletePath(string path, bool isDirectory, bool recursiveDirectoryDelete)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return true;
            }

            string absolutePath = ResolveProjectRelativePath(path);

            try
            {
                if (UsesAssetDatabaseDeletion(path))
                {
                    if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null ||
                        AssetDatabase.IsValidFolder(path))
                    {
                        return AssetDatabase.DeleteAsset(path);
                    }
                }

                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                    return true;
                }

                if (!Directory.Exists(absolutePath))
                {
                    return true;
                }

                if (isDirectory && recursiveDirectoryDelete)
                {
                    Directory.Delete(absolutePath, true);
                    return true;
                }

                if (Directory.GetFileSystemEntries(absolutePath).Length == 0)
                {
                    Directory.Delete(absolutePath, false);
                    return true;
                }

                Debug.LogWarning($"[PackageUninstaller] Preserving non-empty directory '{path}'.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PackageUninstaller] Failed to delete {path}: {ex.Message}");
                return false;
            }
        }

        private static bool UsesAssetDatabaseDeletion(string path)
        {
            string normalizedPath = (path ?? string.Empty).Replace('\\', '/');
            return normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveProjectRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, path.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
