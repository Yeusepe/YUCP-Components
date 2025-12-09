using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.PackageManager
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

            // Show confirmation dialog
            if (!skipConfirmation)
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "Uninstall Package",
                    $"Are you sure you want to uninstall '{package.packageName}'?\n\n" +
                    $"This will delete all installed files. This action cannot be undone.",
                    "Uninstall",
                    "Cancel"
                );

                if (!confirmed)
                {
                    return false;
                }
            }

            try
            {
                // Delete all installed files
                int deletedCount = 0;
                int failedCount = 0;

                foreach (string filePath in package.installedFiles)
                {
                    if (string.IsNullOrEmpty(filePath))
                        continue;

                    try
                    {
                        // Check if file exists
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                            deletedCount++;
                        }
                        else if (Directory.Exists(filePath))
                        {
                            Directory.Delete(filePath, true);
                            deletedCount++;
                        }
                        else if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(filePath) != null)
                        {
                            // Asset exists in AssetDatabase but file might be in a different location
                            AssetDatabase.DeleteAsset(filePath);
                            deletedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PackageUninstaller] Failed to delete {filePath}: {ex.Message}");
                        failedCount++;
                    }
                }

                // Refresh AssetDatabase after deletions
                AssetDatabase.Refresh();

                // Remove from registry
                registry.UnregisterPackage(packageId);

                Debug.Log($"[PackageUninstaller] Uninstalled package '{package.packageName}'. " +
                         $"Deleted {deletedCount} files{(failedCount > 0 ? $", {failedCount} failed" : "")}");

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageUninstaller] Error uninstalling package: {ex.Message}");
                Debug.LogException(ex);
                return false;
            }
        }
    }
}






