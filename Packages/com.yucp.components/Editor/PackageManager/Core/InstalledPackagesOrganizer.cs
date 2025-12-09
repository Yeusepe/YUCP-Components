using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Helper utilities for organizing installed YUCP packages under a dedicated Packages root.
    /// </summary>
    internal static class InstalledPackagesOrganizer
    {
        /// <summary>
        /// Name of the local container package that will hold all installed YUCP packages.
        /// </summary>
        public const string RootPackageName = "yucp.installed-packages";

        /// <summary>
        /// AssetDatabase path to the root container package.
        /// </summary>
        public const string RootAssetPath = "Packages/" + RootPackageName;

        /// <summary>
        /// AssetDatabase path to the shared registry asset.
        /// </summary>
        public const string RegistryAssetPath = RootAssetPath + "/PackageRegistry.asset";

        /// <summary>
        /// Full on-disk path to the root container package folder.
        /// </summary>
        private static string RootFullPath
        {
            get
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.Combine(projectRoot, RootAssetPath.Replace('/', Path.DirectorySeparatorChar));
            }
        }

        /// <summary>
        /// Ensures the root container package folder and its package.json exist.
        /// Safe to call multiple times.
        /// </summary>
        public static void EnsureInstalledPackagesRoot()
        {
            // Ensure folder hierarchy exists in AssetDatabase
            EnsureFolderHierarchy(RootAssetPath);

            // Ensure package.json exists on disk so Unity treats this as a local package
            try
            {
                string packageJsonPath = Path.Combine(RootFullPath, "package.json");
                if (!File.Exists(packageJsonPath))
                {
                    Directory.CreateDirectory(RootFullPath);

                    string packageJson = @"{
  ""name"": ""yucp.installed-packages"",
  ""displayName"": ""YUCP Installed Packages"",
  ""version"": ""1.0.0"",
  ""description"": ""Local container for YUCP-installed Unity packages. All imported YUCP Package+ assets are organized under this package."",
  ""unity"": ""2019.4"",
  ""hideInEditor"": true
}";
                    File.WriteAllText(packageJsonPath, packageJson);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[InstalledPackagesOrganizer] Failed to ensure package.json for '{RootPackageName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Move all imported assets for a given package from Assets/ into the installed-packages container
        /// and return their new asset paths. Non-Assets paths are returned unchanged.
        /// </summary>
        /// <param name="importItems">Unity ImportPackageItem[] array (all items in package).</param>
        /// <param name="packageId">Signed packageId from manifest (preferred for folder name).</param>
        /// <param name="packageName">Human-readable package name (fallback for folder name).</param>
        public static List<string> MoveImportedAssetsToInstalledPackage(System.Array importItems, string packageId, string packageName)
        {
            // Responsibility for physically moving files into Packages/yucp.installed-packages
            // has been moved into the injected installer (DirectVpmInstaller) so that it also
            // works in projects without com.yucp.components installed.
            //
            // This method now only normalizes and returns the installed file paths for tracking.

            Debug.Log($"[InstalledPackagesOrganizer] MoveImportedAssetsToInstalledPackage called for tracking only. " +
                      $"items={importItems?.Length ?? 0}, packageId='{packageId}', packageName='{packageName}'");

            return CollectOriginalPaths(importItems);
        }

        /// <summary>
        /// Returns a stable, filesystem-safe folder name for a package.
        /// </summary>
        private static string GetPackageFolderName(string packageId, string packageName)
        {
            string candidate = !string.IsNullOrWhiteSpace(packageId)
                ? packageId
                : (!string.IsNullOrWhiteSpace(packageName) ? packageName : "package-" + Guid.NewGuid().ToString("N"));

            // Replace characters that are problematic in asset paths
            char[] invalid = Path.GetInvalidFileNameChars();
            var safeChars = new char[candidate.Length];
            for (int i = 0; i < candidate.Length; i++)
            {
                char c = candidate[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    c = '-';
                else if (c == '/' || c == '\\' || c == ':' || c == '*' || c == '?' || c == '\"' || c == '<' || c == '>' || c == '|')
                    c = '-';
                else if (Array.IndexOf(invalid, c) >= 0)
                    c = '-';

                safeChars[i] = c;
            }

            string safe = new string(safeChars).Trim('-');
            if (string.IsNullOrEmpty(safe))
                safe = "package-" + Guid.NewGuid().ToString("N");

            return safe;
        }

        /// <summary>
        /// Ensures that the given folder (and any parents) exist as AssetDatabase folders.
        /// </summary>
        private static void EnsureFolderHierarchy(string assetFolderPath)
        {
            if (string.IsNullOrEmpty(assetFolderPath))
                return;

            assetFolderPath = assetFolderPath.Replace("\\", "/");

            if (AssetDatabase.IsValidFolder(assetFolderPath))
                return;

            string[] segments = assetFolderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return;

            string current = segments[0];
            // "Assets" and "Packages" should already exist, but this is safe even if they don't
            if (!AssetDatabase.IsValidFolder(current) && (current == "Assets" || current == "Packages"))
            {
                // Root folders are managed by Unity; don't try to create them
            }

            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[i]);
                }
                current = next;
            }
        }

        /// <summary>
        /// Move an asset or folder in the AssetDatabase, deleting any existing target first if necessary.
        /// Returns the final asset path if successful, or null if the move failed.
        /// </summary>
        private static string MoveAssetOrFolder(string sourcePath, string targetPath)
        {
            sourcePath = sourcePath.Replace("\\", "/");
            targetPath = targetPath.Replace("\\", "/");

            Debug.Log($"[InstalledPackagesOrganizer] MoveAssetOrFolder called. source='{sourcePath}' target='{targetPath}'");

            // If the source asset/folder doesn't exist, nothing to do
            if (!AssetExists(sourcePath))
            {
                Debug.LogWarning($"[InstalledPackagesOrganizer] Source asset/folder '{sourcePath}' does not exist in AssetDatabase. Skipping move.");
                return null;
            }

            // Ensure parent folders for target exist
            string parentFolder = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(parentFolder))
            {
                parentFolder = parentFolder.Replace("\\", "/");
                EnsureFolderHierarchy(parentFolder);
            }

            // If target already exists, delete it so we can replace with the newly imported asset
            if (AssetExists(targetPath))
            {
                if (!AssetDatabase.DeleteAsset(targetPath))
                {
                    Debug.LogWarning($"[InstalledPackagesOrganizer] Failed to delete existing asset at '{targetPath}' before move.");
                }
                else
                {
                    Debug.Log($"[InstalledPackagesOrganizer] Deleted existing asset at '{targetPath}' before move.");
                }
            }

            string error = AssetDatabase.MoveAsset(sourcePath, targetPath);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"[InstalledPackagesOrganizer] Failed to move '{sourcePath}' to '{targetPath}': {error}");
                return null;
            }

            Debug.Log($"[InstalledPackagesOrganizer] AssetDatabase.MoveAsset succeeded. New path='{targetPath}'");
            return targetPath;
        }

        /// <summary>
        /// Returns true if an asset or folder exists at the given AssetDatabase path.
        /// </summary>
        private static bool AssetExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            assetPath = assetPath.Replace("\\", "/");
            return AssetDatabase.IsValidFolder(assetPath) ||
                   AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null;
        }

        /// <summary>
        /// Fallback helper that simply collects the original destinationAssetPath values.
        /// Used if we cannot access ImportPackageItem internals.
        /// </summary>
        private static List<string> CollectOriginalPaths(System.Array importItems)
        {
            var result = new List<string>();
            if (importItems == null || importItems.Length == 0)
                return result;

            try
            {
                Type importItemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
                if (importItemType == null)
                    return result;

                FieldInfo destinationPathField = importItemType.GetField("destinationAssetPath", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (destinationPathField == null)
                    return result;

                foreach (var item in importItems)
                {
                    if (item == null) continue;
                    string destinationPath = destinationPathField.GetValue(item) as string;
                    if (!string.IsNullOrEmpty(destinationPath))
                    {
                        result.Add(destinationPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[InstalledPackagesOrganizer] Failed to collect original paths: {ex.Message}");
            }

            return result;
        }
    }
}


