using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YUCP.Components.Editor.PackageManager;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Registry for tracking installed packages
    /// Stored as ScriptableObject at Assets/YUCP/PackageRegistry.asset
    /// </summary>
    [CreateAssetMenu(fileName = "PackageRegistry", menuName = "YUCP/Package Registry", order = 1)]
    public class InstalledPackageRegistry : ScriptableObject
    {
        [SerializeField]
        private List<InstalledPackageInfo> _packages = new List<InstalledPackageInfo>();

        private Dictionary<string, InstalledPackageInfo> _packageById = new Dictionary<string, InstalledPackageInfo>();
        private Dictionary<string, InstalledPackageInfo> _packageByHash = new Dictionary<string, InstalledPackageInfo>();
        private bool _dictionariesBuilt = false;

        private void OnEnable()
        {
            BuildDictionaries();
        }

        /// <summary>
        /// Build lookup dictionaries from serialized list
        /// </summary>
        private void BuildDictionaries()
        {
            _packageById.Clear();
            _packageByHash.Clear();

            foreach (var package in _packages)
            {
                if (package == null) continue;

                if (!string.IsNullOrEmpty(package.packageId))
                {
                    _packageById[package.packageId] = package;
                }

                if (!string.IsNullOrEmpty(package.archiveSha256))
                {
                    _packageByHash[package.archiveSha256.ToLowerInvariant()] = package;
                }
            }

            _dictionariesBuilt = true;
        }

        /// <summary>
        /// Register a new package or update existing one
        /// </summary>
        public void RegisterPackage(InstalledPackageInfo packageInfo)
        {
            if (packageInfo == null)
            {
                Debug.LogError("[InstalledPackageRegistry] Cannot register null package");
                return;
            }

            if (string.IsNullOrEmpty(packageInfo.packageId))
            {
                Debug.LogWarning("[InstalledPackageRegistry] Package has no packageId, cannot register");
                return;
            }

            // Remove existing package with same ID
            UnregisterPackage(packageInfo.packageId);

            // Add new package
            _packages.Add(packageInfo);
            BuildDictionaries();

            Save();
        }

        /// <summary>
        /// Unregister a package by packageId
        /// </summary>
        public void UnregisterPackage(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
                return;

            var existing = _packages.FirstOrDefault(p => p != null && p.packageId == packageId);
            if (existing != null)
            {
                _packages.Remove(existing);
                BuildDictionaries();
                Save();
            }
        }

        /// <summary>
        /// Get package by packageId
        /// </summary>
        public InstalledPackageInfo GetPackage(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
                return null;

            if (!_dictionariesBuilt)
                BuildDictionaries();

            _packageById.TryGetValue(packageId, out InstalledPackageInfo package);
            return package;
        }

        /// <summary>
        /// Get package by archive hash
        /// </summary>
        public InstalledPackageInfo GetPackageByHash(string archiveSha256)
        {
            if (string.IsNullOrEmpty(archiveSha256))
                return null;

            if (!_dictionariesBuilt)
                BuildDictionaries();

            _packageByHash.TryGetValue(archiveSha256.ToLowerInvariant(), out InstalledPackageInfo package);
            return package;
        }

        /// <summary>
        /// Get all installed packages
        /// </summary>
        public List<InstalledPackageInfo> GetAllPackages()
        {
            return new List<InstalledPackageInfo>(_packages.Where(p => p != null));
        }

        /// <summary>
        /// Save registry to disk
        /// </summary>
        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Get or create the registry instance
        /// </summary>
        public static InstalledPackageRegistry GetOrCreate()
        {
            // Ensure the installed-packages container exists
            InstalledPackagesOrganizer.EnsureInstalledPackagesRoot();

            string registryPath = InstalledPackagesOrganizer.RegistryAssetPath;

            // Try to load from new location first
            var registry = AssetDatabase.LoadAssetAtPath<InstalledPackageRegistry>(registryPath);
            
            if (registry == null)
            {
                // If an old registry exists under Assets/YUCP, migrate it into the new package
                const string legacyPath = "Assets/YUCP/PackageRegistry.asset";
                var legacy = AssetDatabase.LoadAssetAtPath<InstalledPackageRegistry>(legacyPath);
                if (legacy != null)
                {
                    // Ensure parent folders for new path exist
                    string parentFolder = Path.GetDirectoryName(registryPath);
                    if (!string.IsNullOrEmpty(parentFolder))
                    {
                        parentFolder = parentFolder.Replace("\\", "/");
                        EnsureFolderHierarchy(parentFolder);
                    }

                    string error = AssetDatabase.MoveAsset(legacyPath, registryPath);
                    if (!string.IsNullOrEmpty(error))
                    {
                        Debug.LogWarning($"[InstalledPackageRegistry] Failed to migrate legacy registry from '{legacyPath}' to '{registryPath}': {error}");
                    }
                    registry = AssetDatabase.LoadAssetAtPath<InstalledPackageRegistry>(registryPath);
                }
            }

            if (registry == null)
            {
                // Create a fresh registry asset in the container package
                registry = CreateInstance<InstalledPackageRegistry>();
                AssetDatabase.CreateAsset(registry, registryPath);
                AssetDatabase.SaveAssets();
            }

            return registry;
        }

        /// <summary>
        /// Load registry from disk
        /// </summary>
        public static InstalledPackageRegistry Load()
        {
            string registryPath = InstalledPackagesOrganizer.RegistryAssetPath;
            return AssetDatabase.LoadAssetAtPath<InstalledPackageRegistry>(registryPath);
        }

        /// <summary>
        /// Ensure that a folder hierarchy exists for a given AssetDatabase path.
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
    }
}




