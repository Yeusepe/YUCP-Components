using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Utility class for extracting package metadata from ImportPackageItem arrays.
    /// </summary>
    internal static class PackageMetadataExtractor
    {
        private const string MetadataFileName = "YUCP_PackageInfo.json";
        private const string MetadataAssetPath = "Assets/YUCP_PackageInfo.json";
        private const string PackageJsonFileName = "package.json";
        private const string PackageJsonAssetPath = "Assets/package.json";

        private static Type _importPackageItemType;
        private static FieldInfo _destinationAssetPathField;
        private static FieldInfo _sourceFolderField;
        private static FieldInfo _exportedAssetPathField;

        static PackageMetadataExtractor()
        {
            _importPackageItemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
            if (_importPackageItemType != null)
            {
                _destinationAssetPathField = _importPackageItemType.GetField("destinationAssetPath");
                _sourceFolderField = _importPackageItemType.GetField("sourceFolder");
                _exportedAssetPathField = _importPackageItemType.GetField("exportedAssetPath");
            }
        }

        /// <summary>
        /// Extract metadata from ImportPackageItem array.
        /// Looks for YUCP_PackageInfo.json in the package.
        /// Also extracts icon from packageIconPath if provided (for packages without YUCP metadata).
        /// </summary>
        public static PackageMetadata ExtractMetadataFromImportItems(System.Array importItems, string packagePath, string packageIconPath = null)
        {
            if (importItems == null || importItems.Length == 0)
            {
                return CreateFallbackMetadata(packagePath, null, packageIconPath, importItems);
            }

            // Find metadata item in import items
            object metadataItem = FindMetadataItem(importItems);
            if (metadataItem == null)
            {
                return CreateFallbackMetadata(packagePath, null, packageIconPath, importItems);
            }

            // Read metadata file from extracted package location
            string sourceFolder = GetFieldValue<string>(metadataItem, _sourceFolderField);
            string exportedPath = GetFieldValue<string>(metadataItem, _exportedAssetPathField);

            if (string.IsNullOrEmpty(sourceFolder) || string.IsNullOrEmpty(exportedPath))
            {
                Debug.LogWarning("[YUCP PackageManager] Source folder or exported path is empty, creating fallback metadata");
                return CreateFallbackMetadata(packagePath, null, packageIconPath, importItems);
            }

            // Read JSON from extracted location
            string fullPath = Path.Combine(sourceFolder, exportedPath);
            string json = ReadMetadataFile(sourceFolder, exportedPath);
            
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning("[YUCP PackageManager] Failed to read metadata file, creating fallback metadata");
                return CreateFallbackMetadata(packagePath, null, packageIconPath, importItems);
            }


            try
            {
                // First deserialize to a helper class that has icon/banner as strings
                var metadataJson = JsonUtility.FromJson<PackageMetadataJson>(json);
                if (metadataJson == null)
                {
                    return CreateFallbackMetadata(packagePath, null, packageIconPath, importItems);
                }

                // Convert to PackageMetadata
                var metadata = new PackageMetadata
                {
                    packageName = metadataJson.packageName ?? "",
                    version = metadataJson.version ?? "",
                    author = metadataJson.author ?? "",
                    description = metadataJson.description ?? "",
                    versionRule = metadataJson.versionRule ?? "semver",
                    versionRuleName = metadataJson.versionRuleName ?? metadataJson.versionRule ?? "semver"
                };

                // Convert product links
                if (metadataJson.productLinks != null)
                {
                    foreach (var link in metadataJson.productLinks)
                    {
                        var productLink = new ProductLink(link.url ?? "", link.label ?? "");
                        
                        // Resolve custom icon if path is provided
                        if (!string.IsNullOrEmpty(link.icon))
                        {
                            productLink.customIcon = ResolveTextureFromPath(link.icon, importItems);
                            if (productLink.customIcon == null)
                            {
                                Debug.LogWarning($"[YUCP PackageManager] Failed to load product link icon from path: {link.icon}");
                            }
                        }
                        
                        metadata.productLinks.Add(productLink);
                    }
                }
                else
                {
                }

                // Resolve icon and banner textures from paths
                if (!string.IsNullOrEmpty(metadataJson.icon))
                {
                    metadata.icon = ResolveTextureFromPath(metadataJson.icon, importItems);
                    if (metadata.icon == null)
                    {
                        Debug.LogWarning($"[YUCP PackageManager] Failed to load icon from path: {metadataJson.icon}");
                    }
                }

                if (!string.IsNullOrEmpty(metadataJson.banner))
                {
                    metadata.banner = ResolveTextureFromPath(metadataJson.banner, importItems);
                    if (metadata.banner == null)
                    {
                        Debug.LogWarning($"[YUCP PackageManager] Failed to load banner from path: {metadataJson.banner}");
                    }
                }

                // Extract dependencies from package.json if available
                ExtractDependenciesFromPackageJson(metadata, importItems);

                return metadata;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to parse metadata JSON: {ex.Message}");
                return CreateFallbackMetadata(packagePath, null, packageIconPath, importItems);
            }
        }

        [Serializable]
        private class PackageMetadataJson
        {
            public string packageName;
            public string version;
            public string author;
            public string description;
            public string icon;
            public string banner;
            public List<ProductLinkJson> productLinks;
            public string versionRule;
            public string versionRuleName;
        }

        [Serializable]
        private class ProductLinkJson
        {
            public string label;
            public string url;
            public string icon; // Path to custom icon texture
        }

        private static object FindMetadataItem(System.Array importItems)
        {
            if (_destinationAssetPathField == null) return null;

            foreach (var item in importItems)
            {
                if (item == null) continue;

                string destinationPath = GetFieldValue<string>(item, _destinationAssetPathField);
                if (destinationPath != null && destinationPath.Equals(MetadataAssetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return null;
        }

        private static string ReadMetadataFile(string sourceFolder, string exportedPath)
        {
            try
            {
                if (string.IsNullOrEmpty(sourceFolder))
                {
                    Debug.LogWarning("[YUCP PackageManager] ReadMetadataFile: sourceFolder is empty");
                    return null;
                }

                string assetFilePath = Path.Combine(sourceFolder, "asset");
                
                if (File.Exists(assetFilePath))
                {
                    string content = File.ReadAllText(assetFilePath);
                    return content;
                }
                else
                {
                    Debug.LogWarning($"[YUCP PackageManager] Metadata file does not exist at: {assetFilePath}");
                    string altPath = Path.Combine(sourceFolder, exportedPath);
                    if (File.Exists(altPath))
                    {
                        return File.ReadAllText(altPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Failed to read metadata file: {ex.Message}\n{ex.StackTrace}");
            }

            return null;
        }

        private static Texture2D ResolveTextureFromPath(string relativePath, System.Array importItems)
        {
            if (string.IsNullOrEmpty(relativePath))
                return null;

            string fullPath = ResolveTexturePath(relativePath, importItems);
            if (fullPath != null)
            {
                return LoadTextureFromPath(fullPath);
            }

            return null;
        }

        private static string ResolveTexturePath(string relativePath, System.Array importItems)
        {
            if (string.IsNullOrEmpty(relativePath) || _destinationAssetPathField == null || 
                _sourceFolderField == null || _exportedAssetPathField == null)
            {
                Debug.LogWarning("[YUCP PackageManager] ResolveTexturePath: Invalid parameters");
                return null;
            }

            // Normalize path
            string normalizedPath = relativePath;
            if (!normalizedPath.StartsWith("Assets/"))
            {
                normalizedPath = "Assets/" + normalizedPath;
            }

            // Find matching ImportPackageItem
            foreach (var item in importItems)
            {
                if (item == null) continue;

                string destinationPath = GetFieldValue<string>(item, _destinationAssetPathField);
                
                if (destinationPath != null && destinationPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    string sourceFolder = GetFieldValue<string>(item, _sourceFolderField);
                    string exportedPath = GetFieldValue<string>(item, _exportedAssetPathField);
                    
                    if (!string.IsNullOrEmpty(sourceFolder))
                    {
                        string fullPath = Path.Combine(sourceFolder, "asset");
                        
                        if (File.Exists(fullPath))
                        {
                            return fullPath;
                        }
                        else
                        {
                            Debug.LogWarning($"[YUCP PackageManager] Texture file does not exist at: {fullPath}");
                            // Try alternative path
                            string altPath = Path.Combine(sourceFolder, exportedPath);
                            if (File.Exists(altPath))
                            {
                                return altPath;
                            }
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[YUCP PackageManager] Source folder is empty");
                    }
                }
            }

            Debug.LogWarning($"[YUCP PackageManager] No matching item found for path: {normalizedPath}");
            return null;
        }

        /// <summary>
        /// Load texture from a disk file path (relative to Unity project root).
        /// Used for Unity's temporary package icon paths (e.g., "Temp/Export Package/.../.icon.png").
        /// </summary>
        private static Texture2D LoadTextureFromDiskPath(string relativePath)
        {
            try
            {
                if (string.IsNullOrEmpty(relativePath))
                {
                    Debug.LogWarning("[YUCP PackageManager] LoadTextureFromDiskPath: relativePath is empty");
                    return null;
                }

                // Construct full path relative to Unity project root
                // Application.dataPath is "ProjectRoot/Assets", so we need to go up one level
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string fullPath = Path.Combine(projectRoot, relativePath);
                
                // Normalize path separators
                fullPath = Path.GetFullPath(fullPath);
                
                if (!File.Exists(fullPath))
                {
                    Debug.LogWarning($"[YUCP PackageManager] Texture file does not exist: {fullPath}");
                    return null;
                }

                byte[] data = File.ReadAllBytes(fullPath);
                
                Texture2D texture = new Texture2D(2, 2);
                if (texture.LoadImage(data))
                {
                    return texture;
                }
                else
                {
                    Debug.LogWarning($"[YUCP PackageManager] Failed to load image data from: {fullPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Exception loading texture from disk path {relativePath}: {ex.Message}\n{ex.StackTrace}");
            }

            return null;
        }

        /// <summary>
        /// Load texture from a full file system path.
        /// Used for textures extracted from package contents (via ImportPackageItem).
        /// </summary>
        private static Texture2D LoadTextureFromPath(string fullPath)
        {
            try
            {
                Debug.Log($"[YUCP PackageManager] Loading texture from: {fullPath}");
                
                if (!File.Exists(fullPath))
                {
                    Debug.LogWarning($"[YUCP PackageManager] Texture file does not exist: {fullPath}");
                    return null;
                }

                byte[] data = File.ReadAllBytes(fullPath);
                Debug.Log($"[YUCP PackageManager] Read {data.Length} bytes from texture file");
                
                Texture2D texture = new Texture2D(2, 2);
                if (texture.LoadImage(data))
                {
                    Debug.Log($"[YUCP PackageManager] Texture loaded successfully: {texture.width}x{texture.height}, format: {texture.format}");
                    return texture;
                }
                else
                {
                    Debug.LogWarning($"[YUCP PackageManager] Failed to load image data from: {fullPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP PackageManager] Exception loading texture from {fullPath}: {ex.Message}");
            }

            return null;
        }

        private static T GetFieldValue<T>(object obj, FieldInfo field)
        {
            if (field == null || obj == null) return default(T);
            try
            {
                object value = field.GetValue(obj);
                if (value is T)
                    return (T)value;
            }
            catch { }
            return default(T);
        }

        internal static PackageMetadata CreateFallbackMetadata(string packagePath, string packageName = null, string packageIconPath = null, System.Array importItems = null)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                packageName = Path.GetFileNameWithoutExtension(packagePath);
            }
            
            var metadata = new PackageMetadata(packageName);
            
            // Extract icon from packageIconPath if provided (Unity's standard package icon)
            // packageIconPath is a temporary disk path, not an asset path within the package
            if (!string.IsNullOrEmpty(packageIconPath))
            {
                Debug.Log($"[YUCP PackageManager] Extracting icon from Unity's packageIconPath (disk path): {packageIconPath}");
                metadata.icon = LoadTextureFromDiskPath(packageIconPath);
                if (metadata.icon != null)
                {
                    Debug.Log($"[YUCP PackageManager] Icon loaded from packageIconPath successfully ({metadata.icon.width}x{metadata.icon.height})");
                }
                else
                {
                    Debug.LogWarning($"[YUCP PackageManager] Failed to load icon from packageIconPath: {packageIconPath}");
                }
            }
            
            // Extract dependencies from package.json if available (even for fallback metadata)
            ExtractDependenciesFromPackageJson(metadata, importItems);
            
            return metadata;
        }

        /// <summary>
        /// Extract dependencies from package.json file in the package.
        /// </summary>
        private static void ExtractDependenciesFromPackageJson(PackageMetadata metadata, System.Array importItems)
        {
            if (importItems == null || importItems.Length == 0)
            {
                return;
            }

            try
            {
                // Find package.json item in import items
                object packageJsonItem = FindPackageJsonItem(importItems);
                if (packageJsonItem == null)
                {
                    return;
                }

                // Read package.json file from extracted package location
                string sourceFolder = GetFieldValue<string>(packageJsonItem, _sourceFolderField);
                string exportedPath = GetFieldValue<string>(packageJsonItem, _exportedAssetPathField);

                if (string.IsNullOrEmpty(sourceFolder) || string.IsNullOrEmpty(exportedPath))
                {
                    Debug.LogWarning("[YUCP PackageManager] Source folder or exported path is empty for package.json");
                    return;
                }

                // Read JSON from extracted location
                string json = ReadMetadataFile(sourceFolder, exportedPath);
                
                if (string.IsNullOrEmpty(json))
                {
                    Debug.LogWarning("[YUCP PackageManager] Failed to read package.json file");
                    return;
                }


                // Parse package.json to extract vpmDependencies
                ParsePackageJsonDependencies(metadata, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to extract dependencies from package.json: {ex.Message}");
            }
        }

        /// <summary>
        /// Find package.json item in ImportPackageItem array.
        /// package.json can be at Assets/package.json or Assets/YUCP_TempInstall_{guid}.json
        /// </summary>
        private static object FindPackageJsonItem(System.Array importItems)
        {
            if (_destinationAssetPathField == null) return null;

            foreach (var item in importItems)
            {
                if (item == null) continue;

                string destinationPath = GetFieldValue<string>(item, _destinationAssetPathField);
                if (destinationPath == null) continue;

                // Check for exact match (Assets/package.json)
                if (destinationPath.Equals(PackageJsonAssetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }

                // Check for temporary install path pattern (Assets/YUCP_TempInstall_{guid}.json)
                if (destinationPath.StartsWith("Assets/YUCP_TempInstall_", StringComparison.OrdinalIgnoreCase) &&
                    destinationPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
            return null;
        }

        /// <summary>
        /// Parse package.json JSON to extract vpmDependencies.
        /// Uses simple string parsing since JsonUtility doesn't support Dictionary.
        /// </summary>
        private static void ParsePackageJsonDependencies(PackageMetadata metadata, string json)
        {
            try
            {
                metadata.dependencies.Clear();

                // Find vpmDependencies section in JSON
                int vpmDepsIndex = json.IndexOf("\"vpmDependencies\"", StringComparison.OrdinalIgnoreCase);
                if (vpmDepsIndex < 0)
                {
                    return;
                }

                // Find the opening brace after "vpmDependencies"
                int startIndex = json.IndexOf('{', vpmDepsIndex);
                if (startIndex < 0)
                {
                    Debug.LogWarning("[YUCP PackageManager] Invalid vpmDependencies format in package.json");
                    return;
                }

                // Find the matching closing brace
                int braceCount = 0;
                int endIndex = startIndex;
                for (int i = startIndex; i < json.Length; i++)
                {
                    if (json[i] == '{')
                        braceCount++;
                    else if (json[i] == '}')
                    {
                        braceCount--;
                        if (braceCount == 0)
                        {
                            endIndex = i;
                            break;
                        }
                    }
                }

                if (endIndex <= startIndex)
                {
                    Debug.LogWarning("[YUCP PackageManager] Could not find end of vpmDependencies in package.json");
                    return;
                }

                // Extract the vpmDependencies JSON object
                string vpmDepsJson = json.Substring(startIndex, endIndex - startIndex + 1);
                
                // Parse each key-value pair
                // Format: "packageName": "version"
                int currentIndex = 1; // Skip opening brace
                while (currentIndex < vpmDepsJson.Length - 1)
                {
                    // Find next quote (start of key)
                    int keyStart = vpmDepsJson.IndexOf('"', currentIndex);
                    if (keyStart < 0) break;

                    // Find end of key
                    int keyEnd = vpmDepsJson.IndexOf('"', keyStart + 1);
                    if (keyEnd < 0) break;

                    string packageName = vpmDepsJson.Substring(keyStart + 1, keyEnd - keyStart - 1);

                    // Find colon
                    int colonIndex = vpmDepsJson.IndexOf(':', keyEnd);
                    if (colonIndex < 0) break;

                    // Find value (quoted string)
                    int valueStart = vpmDepsJson.IndexOf('"', colonIndex);
                    if (valueStart < 0) break;

                    int valueEnd = vpmDepsJson.IndexOf('"', valueStart + 1);
                    if (valueEnd < 0) break;

                    string version = vpmDepsJson.Substring(valueStart + 1, valueEnd - valueStart - 1);

                    // Add dependency
                    if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(version))
                    {
                        metadata.dependencies[packageName] = version;
                    }

                    // Move to next entry (skip comma if present)
                    currentIndex = valueEnd + 1;
                    if (currentIndex < vpmDepsJson.Length && vpmDepsJson[currentIndex] == ',')
                        currentIndex++;
                }

            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to parse package.json dependencies: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}

