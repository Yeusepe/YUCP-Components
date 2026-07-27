using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;

namespace YUCP.Importer.Editor.PackageManager
{
    /// <summary>
    /// Utility class for extracting package metadata from ImportPackageItem arrays.
    /// </summary>
    internal static class PackageMetadataExtractor
    {
        private const string MetadataFileName = "YUCP_PackageInfo.json";
        private const string MetadataAssetPath = "Assets/YUCP_PackageInfo.json";
        private const string PackageJsonFileName = "package.json";
        private const string PackageJsonMetadataFileName = "package.json.yucp";
        private const string PackageJsonAssetPath = "Assets/package.json";
        private const string InstalledPackagesRootAssetPath = "Packages/yucp.installed-packages/";
        private const string InstalledPackagesTempSegment = "/_temp/";

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
            PackageJsonImportData packageJsonData = LoadPackageJsonImportData(importItems);

            if (importItems == null || importItems.Length == 0)
            {
                return CreateFallbackMetadata(packagePath, packageJsonData, packageIconPath, importItems);
            }

            // Find metadata item in import items
            object metadataItem = FindMetadataItem(importItems);
            if (metadataItem == null)
            {
                if (packageJsonData?.packageMetadataJson == null)
                {
                    Debug.Log("[YUCP PackageManager] No importer metadata was found in import items. Falling back to package name and Unity icon.");
                    return CreateFallbackMetadata(packagePath, packageJsonData, packageIconPath, importItems);
                }

                PackageMetadata aliasMetadata = ParsePackageMetadataJson(
                    packageJsonData.packageMetadataJson,
                    importItems,
                    packagePath,
                    packageIconPath);
                if (aliasMetadata != null)
                {
                    ApplyPackageJsonData(aliasMetadata, packageJsonData);
                    return aliasMetadata;
                }

                return CreateFallbackMetadata(packagePath, packageJsonData, packageIconPath, importItems);
            }

            // Read metadata file from extracted package location
            string sourceFolder = GetFieldValue<string>(metadataItem, _sourceFolderField);
            string exportedPath = GetFieldValue<string>(metadataItem, _exportedAssetPathField);
            string destinationPath = GetFieldValue<string>(metadataItem, _destinationAssetPathField);

            if (string.IsNullOrEmpty(sourceFolder) || string.IsNullOrEmpty(exportedPath))
            {
                Debug.LogWarning("[YUCP PackageManager] Source folder or exported path is empty, creating fallback metadata");
                return CreateFallbackMetadata(packagePath, packageJsonData, packageIconPath, importItems);
            }

            Debug.Log($"[YUCP PackageManager] Reading metadata from import item '{destinationPath ?? exportedPath}'.");
            string json = ReadMetadataFile(sourceFolder, exportedPath);
            
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning("[YUCP PackageManager] Failed to read metadata file, creating fallback metadata");
                return CreateFallbackMetadata(packagePath, packageJsonData, packageIconPath, importItems);
            }

            PackageMetadata metadata = ParsePackageMetadataJson(json, importItems, packagePath, packageIconPath);
            if (metadata == null)
            {
                return CreateFallbackMetadata(packagePath, packageJsonData, packageIconPath, importItems);
            }

            ApplyPackageJsonData(metadata, packageJsonData);
            Debug.Log($"[YUCP PackageManager] Parsed package metadata. packageName='{metadata.packageName}', version='{metadata.version}', iconLoaded={metadata.icon != null}, bannerLoaded={metadata.banner != null}, productLinks={metadata.productLinks.Count}, dependencies={metadata.dependencies.Count}");
            return metadata;
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
            public List<LicensePackageJson> licensePackages;
            
            // Storefront metadata
            public string tagline;
            public string category;
            public List<string> supportedPlatforms;
            public string minimumUnityVersion;
            public string creatorNote;
            public string releaseNotes;
            public List<string> galleryImages;
            public List<string> tags;
            public int totalFileCount;
            public long totalFileSize;
            public List<AssetBreakdownJsonImport> assetBreakdown;
            public string exportDate;
            public List<FileHashJsonImport> fileHashes;
        }

        [Serializable]
        private class LicensePackageJson
        {
            public string packageId;
            public string packageName;
            public string productId;
            public string gumroadPermalink;
            public string jinxxyProductId;
            public string discordGuildId;
            public string discordRoleId;
            public string creatorAuthUserId;
        }

        [Serializable]
        private class ProductLinkJson
        {
            public string label;
            public string url;
            public string icon; // Path to custom icon texture
        }

        [Serializable]
        private class AssetBreakdownJsonImport
        {
            public string type;
            public int count;
        }

        [Serializable]
        private class FileHashJsonImport
        {
            public string path;
            public string hash;
        }

        internal sealed class PackageJsonImportData
        {
            public string packageName;
            public string displayName;
            public string version;
            public string author;
            public string description;
            public Dictionary<string, string> dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public AliasPackageContract aliasPackage;
            public string packageMetadataJson;

            public void ApplyMetadataJson(string metadataJson)
            {
                if (!string.IsNullOrWhiteSpace(metadataJson))
                {
                    packageMetadataJson = metadataJson;
                }
            }
        }

        private static object FindMetadataItem(System.Array importItems)
        {
            object packageJsonMetadataItem = FindItemByDestinationPath(importItems, IsPackageJsonMetadataAssetPath);
            return packageJsonMetadataItem ?? FindItemByDestinationPath(importItems, IsMetadataAssetPath);
        }

        private static object FindItemByDestinationPath(System.Array importItems, Func<string, bool> predicate)
        {
            if (_destinationAssetPathField == null || predicate == null || importItems == null)
                return null;

            foreach (var item in importItems)
            {
                if (item == null) continue;

                string destinationPath = GetFieldValue<string>(item, _destinationAssetPathField);
                if (predicate(destinationPath))
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

            string[] normalizedCandidates = BuildDestinationPathCandidates(relativePath);

            // Find matching ImportPackageItem
            foreach (var item in importItems)
            {
                if (item == null) continue;

                string destinationPath = GetFieldValue<string>(item, _destinationAssetPathField);

                if (destinationPath != null && normalizedCandidates.Any(candidate => destinationPath.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                {
                    string sourceFolder = GetFieldValue<string>(item, _sourceFolderField);
                    string exportedPath = GetFieldValue<string>(item, _exportedAssetPathField);
                    
                    if (!string.IsNullOrEmpty(sourceFolder))
                    {
                        string fullPath = Path.Combine(sourceFolder, "asset");
                        
                        if (File.Exists(fullPath))
                        {
                            Debug.Log($"[YUCP PackageManager] Resolved texture '{relativePath}' from import item '{destinationPath}'.");
                            return fullPath;
                        }
                        else
                        {
                            Debug.LogWarning($"[YUCP PackageManager] Texture file does not exist at: {fullPath}");
                            // Try alternative path
                            string altPath = Path.Combine(sourceFolder, exportedPath);
                            if (File.Exists(altPath))
                            {
                                Debug.Log($"[YUCP PackageManager] Resolved texture '{relativePath}' from alternate extracted path '{altPath}'.");
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

            // Fallback: package-based imports may preserve only the tail portion of the original Assets path.
            string normalizedRelativePath = relativePath.Replace('\\', '/');
            string exportProfilesSuffix = TryGetExportProfilesSuffix(normalizedRelativePath);
            string fileName = Path.GetFileName(normalizedRelativePath);

            foreach (var item in importItems)
            {
                if (item == null) continue;

                string destinationPath = GetFieldValue<string>(item, _destinationAssetPathField);
                if (string.IsNullOrEmpty(destinationPath)) continue;

                string normalizedDestinationPath = destinationPath.Replace('\\', '/');
                bool suffixMatch = !string.IsNullOrEmpty(exportProfilesSuffix) &&
                    normalizedDestinationPath.EndsWith(exportProfilesSuffix, StringComparison.OrdinalIgnoreCase);
                bool fileNameMatch = !string.IsNullOrEmpty(fileName) &&
                    string.Equals(Path.GetFileName(normalizedDestinationPath), fileName, StringComparison.OrdinalIgnoreCase);

                if (!suffixMatch && !fileNameMatch)
                {
                    continue;
                }

                string sourceFolder = GetFieldValue<string>(item, _sourceFolderField);
                string exportedPath = GetFieldValue<string>(item, _exportedAssetPathField);
                if (string.IsNullOrEmpty(sourceFolder))
                {
                    continue;
                }

                string fullPath = Path.Combine(sourceFolder, "asset");
                if (File.Exists(fullPath))
                {
                    Debug.Log($"[YUCP PackageManager] Resolved texture '{relativePath}' via fallback match on import item '{destinationPath}'.");
                    return fullPath;
                }

                string altPath = Path.Combine(sourceFolder, exportedPath);
                if (File.Exists(altPath))
                {
                    Debug.Log($"[YUCP PackageManager] Resolved texture '{relativePath}' via fallback extracted path '{altPath}'.");
                    return altPath;
                }
            }

            Debug.LogWarning($"[YUCP PackageManager] No matching import item found for texture path '{relativePath}'. Candidates: {string.Join(", ", normalizedCandidates)}");
            return null;
        }

        private static string TryGetExportProfilesSuffix(string normalizedPath)
        {
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return null;
            }

            const string marker = "YUCP/ExportProfiles/";
            int markerIndex = normalizedPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return null;
            }

            return normalizedPath.Substring(markerIndex);
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

        internal static PackageMetadata ParsePackageMetadataJson(
            string json,
            System.Array importItems,
            string packagePath,
            string packageIconPath)
        {
            try
            {
                var metadataJson = JsonUtility.FromJson<PackageMetadataJson>(json);
                if (metadataJson == null)
                {
                    return null;
                }

                var metadata = new PackageMetadata
                {
                    packageName = metadataJson.packageName ?? "",
                    version = metadataJson.version ?? "",
                    author = metadataJson.author ?? "",
                    description = metadataJson.description ?? "",
                    versionRule = metadataJson.versionRule ?? "semver",
                    versionRuleName = metadataJson.versionRuleName ?? metadataJson.versionRule ?? "semver"
                };

                if (metadataJson.productLinks != null)
                {
                    foreach (ProductLinkJson link in metadataJson.productLinks)
                    {
                        if (link == null)
                        {
                            continue;
                        }

                        var productLink = new ProductLink(link.url ?? "", link.label ?? "");
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

                if (metadataJson.licensePackages != null)
                {
                    foreach (LicensePackageJson lp in metadataJson.licensePackages)
                    {
                        if (lp == null || string.IsNullOrEmpty(lp.packageId))
                        {
                            continue;
                        }

                        metadata.licensePackages.Add(new LicensePackageRequirement
                        {
                            packageId = lp.packageId,
                            packageName = lp.packageName ?? lp.packageId,
                            productId = lp.productId ?? "",
                            gumroadPermalink = lp.gumroadPermalink ?? "",
                            jinxxyProductId = lp.jinxxyProductId ?? "",
                            discordGuildId = lp.discordGuildId ?? "",
                            discordRoleId = lp.discordRoleId ?? "",
                            creatorAuthUserId = lp.creatorAuthUserId ?? "",
                        });
                    }
                }

                metadata.tagline = metadataJson.tagline ?? "";
                metadata.category = metadataJson.category ?? "";
                metadata.minimumUnityVersion = metadataJson.minimumUnityVersion ?? "";
                metadata.creatorNote = metadataJson.creatorNote ?? "";
                metadata.releaseNotes = metadataJson.releaseNotes ?? "";
                metadata.exportDate = metadataJson.exportDate ?? "";
                metadata.totalFileCount = metadataJson.totalFileCount;
                metadata.totalFileSize = metadataJson.totalFileSize;

                if (metadataJson.supportedPlatforms != null)
                    metadata.supportedPlatforms = new List<string>(metadataJson.supportedPlatforms);
                if (metadataJson.tags != null)
                    metadata.tags = new List<string>(metadataJson.tags);

                if (metadataJson.assetBreakdown != null)
                {
                    foreach (AssetBreakdownJsonImport ab in metadataJson.assetBreakdown)
                    {
                        if (ab != null && !string.IsNullOrEmpty(ab.type))
                            metadata.assetBreakdown.Add(new AssetBreakdownEntry(ab.type, ab.count));
                    }
                }

                if (metadataJson.fileHashes != null)
                {
                    foreach (FileHashJsonImport fileHash in metadataJson.fileHashes)
                    {
                        if (fileHash == null || string.IsNullOrWhiteSpace(fileHash.path))
                        {
                            continue;
                        }

                        metadata.fileHashes.Add(new PackageFileHashEntry
                        {
                            path = fileHash.path ?? string.Empty,
                            hash = fileHash.hash ?? string.Empty,
                        });
                    }
                }

                if (metadataJson.galleryImages != null)
                {
                    foreach (string galleryPath in metadataJson.galleryImages)
                    {
                        if (string.IsNullOrEmpty(galleryPath)) continue;
                        Texture2D tex = ResolveTextureFromPath(galleryPath, importItems);
                        if (tex != null)
                            metadata.galleryImages.Add(tex);
                    }
                }

                return metadata;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to parse metadata JSON: {ex.Message}");
                return null;
            }
        }

        private static void ApplyPackageJsonData(PackageMetadata metadata, PackageJsonImportData packageJsonData)
        {
            if (metadata == null || packageJsonData == null)
            {
                return;
            }

            if (metadata.dependencies == null)
            {
                metadata.dependencies = new Dictionary<string, string>();
            }

            foreach (KeyValuePair<string, string> dependency in packageJsonData.dependencies)
            {
                metadata.dependencies[dependency.Key] = dependency.Value;
            }

            if (string.IsNullOrWhiteSpace(metadata.author) && !string.IsNullOrWhiteSpace(packageJsonData.author))
            {
                metadata.author = packageJsonData.author;
            }

            if (string.IsNullOrWhiteSpace(metadata.description) && !string.IsNullOrWhiteSpace(packageJsonData.description))
            {
                metadata.description = packageJsonData.description;
            }

            metadata.aliasPackage = packageJsonData.aliasPackage?.Clone();
        }

        internal static PackageJsonImportData ParsePackageJsonImportData(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return ParsePackageJsonImportDataStrict(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to parse package.json metadata: {ex.Message}");
                return null;
            }
        }

        internal static PackageJsonImportData ParsePackageJsonImportDataStrict(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new FormatException("package.json is empty.");
            }
            JObject packageJson = JObject.Parse(json);
            var result = new PackageJsonImportData
            {
                packageName = GetString(packageJson, "name"),
                displayName = GetString(packageJson, "displayName"),
                version = GetString(packageJson, "version"),
                description = GetString(packageJson, "description"),
                author = ParseAuthor(packageJson["author"]),
                dependencies = ParseDependencyMap(packageJson["vpmDependencies"]),
            };

            JObject yucp = packageJson["yucp"] as JObject;
            if (yucp != null)
            {
                result.packageMetadataJson =
                    (yucp["packageMetadata"] as JObject)?.ToString(Formatting.None);
                result.aliasPackage = ParseAliasPackageContract(packageJson, yucp);
            }
            return result;
        }

        private static PackageJsonImportData LoadPackageJsonImportData(System.Array importItems)
        {
            if (importItems == null || importItems.Length == 0)
            {
                return null;
            }

            try
            {
                object packageJsonItem = FindPackageJsonItemForMetadata(importItems);
                if (packageJsonItem == null)
                {
                    return null;
                }

                string sourceFolder = GetFieldValue<string>(packageJsonItem, _sourceFolderField);
                string exportedPath = GetFieldValue<string>(packageJsonItem, _exportedAssetPathField);
                if (string.IsNullOrEmpty(sourceFolder) || string.IsNullOrEmpty(exportedPath))
                {
                    Debug.LogWarning("[YUCP PackageManager] Source folder or exported path is empty for package.json");
                    return null;
                }

                string destinationPath = GetFieldValue<string>(packageJsonItem, _destinationAssetPathField);
                Debug.Log($"[YUCP PackageManager] Reading package.json import metadata from '{destinationPath ?? exportedPath}'.");

                string json = ReadMetadataFile(sourceFolder, exportedPath);
                PackageJsonImportData packageJsonData = string.IsNullOrWhiteSpace(json) ? null : ParsePackageJsonImportData(json);
                return MergePackageJsonImportData(packageJsonData, LoadPackageJsonMetadataJson(importItems));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to extract package.json metadata: {ex.Message}");
                return null;
            }
        }

        internal static PackageJsonImportData MergePackageJsonImportData(
            PackageJsonImportData packageJsonData,
            string packageMetadataJson)
        {
            if (packageJsonData == null)
            {
                if (string.IsNullOrWhiteSpace(packageMetadataJson))
                {
                    return null;
                }

                packageJsonData = new PackageJsonImportData();
            }

            packageJsonData.ApplyMetadataJson(packageMetadataJson);
            return packageJsonData;
        }

        internal static PackageMetadata LoadMetadataFromInstalledPackage(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return null;
            }

            string packageJsonAssetPath = $"Packages/{packageId.Trim()}/package.json";
            string packageJson = AssetDatabase.LoadAssetAtPath<TextAsset>(packageJsonAssetPath)?.text;
            if (string.IsNullOrWhiteSpace(packageJson))
            {
                string absolutePath = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
                        packageJsonAssetPath.Replace('/', Path.DirectorySeparatorChar)));
                if (File.Exists(absolutePath))
                {
                    packageJson = File.ReadAllText(absolutePath);
                }
            }

            if (string.IsNullOrWhiteSpace(packageJson))
            {
                Debug.LogWarning($"[YUCP PackageManager] Could not read installed package.json for '{packageId}'.");
                return null;
            }

            PackageJsonImportData packageJsonData = ParsePackageJsonImportData(packageJson);
            if (packageJsonData == null)
            {
                return null;
            }

            PackageMetadata metadata = null;
            if (!string.IsNullOrWhiteSpace(packageJsonData.packageMetadataJson))
            {
                metadata = ParseInstalledPackageMetadataJson(packageJsonData.packageMetadataJson);
            }

            metadata ??= CreateFallbackMetadata(packageJsonAssetPath, packageJsonData);
            ApplyPackageJsonData(metadata, packageJsonData);
            return metadata;
        }

        private static string LoadPackageJsonMetadataJson(System.Array importItems)
        {
            if (importItems == null || importItems.Length == 0)
            {
                return null;
            }

            try
            {
                object metadataItem = FindItemByDestinationPath(importItems, IsPackageJsonMetadataAssetPath);
                if (metadataItem == null)
                {
                    return null;
                }

                string sourceFolder = GetFieldValue<string>(metadataItem, _sourceFolderField);
                string exportedPath = GetFieldValue<string>(metadataItem, _exportedAssetPathField);
                if (string.IsNullOrEmpty(sourceFolder) || string.IsNullOrEmpty(exportedPath))
                {
                    Debug.LogWarning("[YUCP PackageManager] Source folder or exported path is empty for package.json.yucp");
                    return null;
                }

                return ReadMetadataFile(sourceFolder, exportedPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to extract package.json.yucp metadata: {ex.Message}");
                return null;
            }
        }

        internal static PackageMetadata CreateFallbackMetadata(
            string packagePath,
            PackageJsonImportData packageJsonData = null,
            string packageIconPath = null,
            System.Array importItems = null)
        {
            string packageName = packageJsonData?.aliasPackage?.packageDisplayName;
            if (string.IsNullOrEmpty(packageName))
            {
                packageName = packageJsonData?.displayName;
            }

            if (string.IsNullOrEmpty(packageName))
            {
                packageName = packageJsonData?.packageName;
            }

            if (string.IsNullOrEmpty(packageName))
            {
                packageName = Path.GetFileNameWithoutExtension(packagePath);
            }
             
            var metadata = new PackageMetadata(packageName);
            metadata.version = packageJsonData?.version ?? string.Empty;
            metadata.author = packageJsonData?.author ?? string.Empty;
            metadata.description = packageJsonData?.description ?? string.Empty;
             
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

            ApplyPackageJsonData(metadata, packageJsonData ?? LoadPackageJsonImportData(importItems));
            return metadata;
        }

        internal static PackageMetadata ParseEmbeddedAliasMetadataJson(
            string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            PackageMetadataJson source =
                JsonUtility.FromJson<PackageMetadataJson>(json);
            if (source == null)
            {
                throw new FormatException(
                    "Alias package metadata is invalid.");
            }

            return new PackageMetadata
            {
                packageName = NormalizeUserFacingText(
                    source.packageName,
                    160,
                    "package name"),
                version = string.Empty,
                author = NormalizeUserFacingText(
                    source.author,
                    160,
                    "package author"),
                description = NormalizeUserFacingText(
                    source.description,
                    2000,
                    "package description",
                    true),
                tagline = NormalizeUserFacingText(
                    source.tagline,
                    240,
                    "package tagline"),
                category = NormalizeUserFacingText(
                    source.category,
                    120,
                    "package category"),
                minimumUnityVersion = NormalizeUserFacingText(
                    source.minimumUnityVersion,
                    64,
                    "minimum Unity version"),
                creatorNote = NormalizeUserFacingText(
                    source.creatorNote,
                    2000,
                    "creator note",
                    true),
                releaseNotes = NormalizeUserFacingText(
                    source.releaseNotes,
                    4000,
                    "release notes",
                    true),
                supportedPlatforms = NormalizeUserFacingList(
                    source.supportedPlatforms,
                    16,
                    80,
                    "supported platform"),
                tags = NormalizeUserFacingList(
                    source.tags,
                    32,
                    80,
                    "package tag"),
            };
        }

        private static PackageMetadata ParseInstalledPackageMetadataJson(string json)
        {
            try
            {
                var metadataJson = JsonUtility.FromJson<PackageMetadataJson>(json);
                if (metadataJson == null)
                {
                    return null;
                }

                var metadata = new PackageMetadata
                {
                    packageName = metadataJson.packageName ?? string.Empty,
                    version = metadataJson.version ?? string.Empty,
                    author = metadataJson.author ?? string.Empty,
                    description = metadataJson.description ?? string.Empty,
                    versionRule = metadataJson.versionRule ?? "semver",
                    versionRuleName = metadataJson.versionRuleName ?? metadataJson.versionRule ?? "semver",
                    tagline = metadataJson.tagline ?? string.Empty,
                    category = metadataJson.category ?? string.Empty,
                    minimumUnityVersion = metadataJson.minimumUnityVersion ?? string.Empty,
                    creatorNote = metadataJson.creatorNote ?? string.Empty,
                    releaseNotes = metadataJson.releaseNotes ?? string.Empty,
                    exportDate = metadataJson.exportDate ?? string.Empty,
                    totalFileCount = metadataJson.totalFileCount,
                    totalFileSize = metadataJson.totalFileSize,
                };

                if (metadataJson.productLinks != null)
                {
                    foreach (ProductLinkJson link in metadataJson.productLinks)
                    {
                        if (link == null)
                        {
                            continue;
                        }

                        var productLink = new ProductLink(link.url ?? string.Empty, link.label ?? string.Empty);
                        if (!string.IsNullOrWhiteSpace(link.icon))
                        {
                            productLink.customIcon = ResolveProjectTexture(link.icon);
                        }

                        metadata.productLinks.Add(productLink);
                    }
                }

                if (!string.IsNullOrWhiteSpace(metadataJson.icon))
                {
                    metadata.icon = ResolveProjectTexture(metadataJson.icon);
                }

                if (!string.IsNullOrWhiteSpace(metadataJson.banner))
                {
                    metadata.banner = ResolveProjectTexture(metadataJson.banner);
                }

                if (metadataJson.licensePackages != null)
                {
                    foreach (LicensePackageJson lp in metadataJson.licensePackages)
                    {
                        if (lp == null || string.IsNullOrEmpty(lp.packageId))
                        {
                            continue;
                        }

                        metadata.licensePackages.Add(new LicensePackageRequirement
                        {
                            packageId = lp.packageId,
                            packageName = lp.packageName ?? lp.packageId,
                            productId = lp.productId ?? string.Empty,
                            gumroadPermalink = lp.gumroadPermalink ?? string.Empty,
                            jinxxyProductId = lp.jinxxyProductId ?? string.Empty,
                            discordGuildId = lp.discordGuildId ?? string.Empty,
                            discordRoleId = lp.discordRoleId ?? string.Empty,
                            creatorAuthUserId = lp.creatorAuthUserId ?? string.Empty,
                        });
                    }
                }

                if (metadataJson.supportedPlatforms != null)
                {
                    metadata.supportedPlatforms = new List<string>(metadataJson.supportedPlatforms);
                }

                if (metadataJson.tags != null)
                {
                    metadata.tags = new List<string>(metadataJson.tags);
                }

                if (metadataJson.assetBreakdown != null)
                {
                    foreach (AssetBreakdownJsonImport ab in metadataJson.assetBreakdown)
                    {
                        if (ab != null && !string.IsNullOrEmpty(ab.type))
                        {
                            metadata.assetBreakdown.Add(new AssetBreakdownEntry(ab.type, ab.count));
                        }
                    }
                }

                if (metadataJson.fileHashes != null)
                {
                    foreach (FileHashJsonImport fileHash in metadataJson.fileHashes)
                    {
                        if (fileHash == null || string.IsNullOrWhiteSpace(fileHash.path))
                        {
                            continue;
                        }

                        metadata.fileHashes.Add(new PackageFileHashEntry
                        {
                            path = fileHash.path ?? string.Empty,
                            hash = fileHash.hash ?? string.Empty,
                        });
                    }
                }

                if (metadataJson.galleryImages != null)
                {
                    foreach (string galleryPath in metadataJson.galleryImages)
                    {
                        if (string.IsNullOrWhiteSpace(galleryPath))
                        {
                            continue;
                        }

                        Texture2D texture = ResolveProjectTexture(galleryPath);
                        if (texture != null)
                        {
                            metadata.galleryImages.Add(texture);
                        }
                    }
                }

                return metadata;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to parse installed package metadata JSON: {ex.Message}");
                return null;
            }
        }

        private static Texture2D ResolveProjectTexture(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            foreach (string candidate in BuildDestinationPathCandidates(relativePath))
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(candidate);
                if (texture != null)
                {
                    return texture;
                }

                texture = LoadTextureFromDiskPath(candidate);
                if (texture != null)
                {
                    return texture;
                }
            }

            return null;
        }

        internal static bool HasTempInstallDescriptor(System.Array importItems)
        {
            return FindTempInstallPackageJsonItem(importItems) != null;
        }

        internal static string GetPackageJsonDestinationPath(System.Array importItems)
        {
            object packageJsonItem = FindPackageJsonItem(importItems);
            if (packageJsonItem == null)
            {
                return null;
            }

            return GetFieldValue<string>(packageJsonItem, _destinationAssetPathField);
        }

        /// <summary>
        /// Extract dependencies from package.json file in the package.
        /// </summary>
        private static void ExtractDependenciesFromPackageJson(PackageMetadata metadata, System.Array importItems)
        {
            if (metadata == null)
            {
                return;
            }

            PackageJsonImportData packageJsonData = LoadPackageJsonImportData(importItems);
            if (packageJsonData == null)
            {
                return;
            }

            metadata.dependencies.Clear();
            foreach (KeyValuePair<string, string> dependency in packageJsonData.dependencies)
            {
                metadata.dependencies[dependency.Key] = dependency.Value;
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

                // Prefer the temp-install package descriptor when both it and the
                // installed-packages container package.json are present in the same import.
                if (IsTempInstallPackageJsonPath(destinationPath))
                {
                    return item;
                }
            }

            foreach (var item in importItems)
            {
                if (item == null) continue;

                string destinationPath = GetFieldValue<string>(item, _destinationAssetPathField);
                if (destinationPath == null) continue;

                if (IsRootPackageJsonPath(destinationPath))
                {
                    return item;
                }

                // Check for exact match (Assets/package.json)
                if (IsPackageJsonAssetPath(destinationPath))
                {
                    return item;
                }
            }
            return null;
        }

        private static object FindPackageJsonItemForMetadata(System.Array importItems)
        {
            if (_destinationAssetPathField == null || importItems == null) return null;

            object selected = FindPackageJsonCandidate(
                importItems,
                path => IsRootPackageJsonPath(path) && !IsTempInstallPackageJsonPath(path),
                data => IsAliasPackageJsonData(data) && HasEmbeddedPackageMetadata(data),
                "alias package metadata");
            if (selected != null) return selected;

            selected = FindPackageJsonCandidate(
                importItems,
                IsTempInstallPackageJsonPath,
                data => IsAliasPackageJsonData(data) && HasEmbeddedPackageMetadata(data),
                "temp alias package metadata");
            if (selected != null) return selected;

            selected = FindPackageJsonCandidate(
                importItems,
                IsRootPackageJsonPath,
                HasEmbeddedPackageMetadata,
                "embedded package metadata");
            if (selected != null) return selected;

            selected = FindPackageJsonCandidate(
                importItems,
                IsTempInstallPackageJsonPath,
                data => data != null,
                "temp package descriptor");
            if (selected != null) return selected;

            return FindPackageJsonItem(importItems);
        }

        private static object FindTempInstallPackageJsonItem(System.Array importItems)
        {
            if (_destinationAssetPathField == null || importItems == null) return null;

            foreach (var item in importItems)
            {
                if (item == null) continue;

                string destinationPath = GetFieldValue<string>(item, _destinationAssetPathField);
                if (IsTempInstallPackageJsonPath(destinationPath))
                {
                    return item;
                }
            }

            return null;
        }

        private static object FindPackageJsonCandidate(
            System.Array importItems,
            Func<string, bool> pathPredicate,
            Func<PackageJsonImportData, bool> dataPredicate,
            string reason)
        {
            if (importItems == null || pathPredicate == null || dataPredicate == null)
            {
                return null;
            }

            foreach (var item in importItems)
            {
                if (item == null) continue;

                string destinationPath = GetFieldValue<string>(item, _destinationAssetPathField);
                if (!pathPredicate(destinationPath))
                {
                    continue;
                }

                if (!TryReadPackageJsonImportData(item, out PackageJsonImportData importData))
                {
                    continue;
                }

                if (!dataPredicate(importData))
                {
                    continue;
                }

                Debug.Log($"[YUCP PackageManager] Selected package.json import metadata source '{destinationPath}' ({reason}).");
                return item;
            }

            return null;
        }

        private static bool TryReadPackageJsonImportData(object item, out PackageJsonImportData importData)
        {
            importData = null;
            if (item == null)
            {
                return false;
            }

            string sourceFolder = GetFieldValue<string>(item, _sourceFolderField);
            string exportedPath = GetFieldValue<string>(item, _exportedAssetPathField);
            if (string.IsNullOrEmpty(sourceFolder) || string.IsNullOrEmpty(exportedPath))
            {
                return false;
            }

            string json = ReadMetadataFile(sourceFolder, exportedPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            importData = ParsePackageJsonImportData(json);
            return importData != null;
        }

        private static bool IsAliasPackageJsonData(PackageJsonImportData importData)
        {
            return string.Equals(
                importData?.aliasPackage?.kind,
                "alias-v1",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasEmbeddedPackageMetadata(PackageJsonImportData importData)
        {
            return !string.IsNullOrWhiteSpace(importData?.packageMetadataJson);
        }

        internal static bool IsTempInstallPackageJsonPath(string destinationPath)
        {
            if (string.IsNullOrEmpty(destinationPath))
            {
                return false;
            }

            string normalizedPath = destinationPath.Replace('\\', '/');
            if (normalizedPath.StartsWith("Assets/YUCP_TempInstall_", StringComparison.OrdinalIgnoreCase) &&
                normalizedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return normalizedPath.StartsWith(InstalledPackagesRootAssetPath, StringComparison.OrdinalIgnoreCase) &&
                normalizedPath.Contains(InstalledPackagesTempSegment, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(normalizedPath).StartsWith("YUCP_TempInstall_", StringComparison.OrdinalIgnoreCase) &&
                normalizedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsMetadataAssetPath(string destinationPath)
        {
            if (string.IsNullOrEmpty(destinationPath))
            {
                return false;
            }

            string normalizedPath = destinationPath.Replace('\\', '/');
            return normalizedPath.Equals(MetadataAssetPath, StringComparison.OrdinalIgnoreCase) ||
                (normalizedPath.StartsWith(InstalledPackagesRootAssetPath, StringComparison.OrdinalIgnoreCase) &&
                 normalizedPath.EndsWith("/" + MetadataFileName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsPackageJsonAssetPath(string destinationPath)
        {
            if (string.IsNullOrEmpty(destinationPath))
            {
                return false;
            }

            string normalizedPath = destinationPath.Replace('\\', '/');
            return normalizedPath.Equals(PackageJsonAssetPath, StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Equals("Packages/yucp.installed-packages/package.json", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPackageJsonMetadataAssetPath(string destinationPath)
        {
            if (string.IsNullOrEmpty(destinationPath))
            {
                return false;
            }

            string normalizedPath = destinationPath.Replace('\\', '/');
            return normalizedPath.Equals("Assets/" + PackageJsonMetadataFileName, StringComparison.OrdinalIgnoreCase) ||
                (normalizedPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) &&
                 normalizedPath.EndsWith("/" + PackageJsonMetadataFileName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsRootPackageJsonPath(string destinationPath)
        {
            if (string.IsNullOrEmpty(destinationPath))
            {
                return false;
            }

            string normalizedPath = destinationPath.Replace('\\', '/');
            return normalizedPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) &&
                normalizedPath.EndsWith("/package.json", StringComparison.OrdinalIgnoreCase) &&
                !normalizedPath.Equals("Packages/yucp.installed-packages/package.json", StringComparison.OrdinalIgnoreCase);
        }

        private static string[] BuildDestinationPathCandidates(string relativePath)
        {
            string normalizedPath = relativePath.Replace('\\', '/');
            var candidates = new List<string>();

            if (!string.IsNullOrEmpty(normalizedPath))
            {
                candidates.Add(normalizedPath);

                if (!normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                    !normalizedPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add("Assets/" + normalizedPath);
                }
            }

            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>
        /// Parse package.json JSON to extract vpmDependencies.
        /// </summary>
        private static void ParsePackageJsonDependencies(PackageMetadata metadata, string json)
        {
            if (metadata == null)
            {
                return;
            }

            PackageJsonImportData packageJsonData = ParsePackageJsonImportData(json);
            metadata.dependencies.Clear();
            if (packageJsonData == null)
            {
                return;
            }

            foreach (KeyValuePair<string, string> dependency in packageJsonData.dependencies)
            {
                metadata.dependencies[dependency.Key] = dependency.Value;
            }
        }

        private static Dictionary<string, string> ParseDependencyMap(JToken token)
        {
            var dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            JObject dependencyObject = token as JObject;
            if (dependencyObject == null)
            {
                return dependencies;
            }

            foreach (JProperty property in dependencyObject.Properties())
            {
                string name = property.Name?.Trim();
                string value = property.Value?.Type == JTokenType.Null ? null : property.Value?.ToString();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                dependencies[name] = value.Trim();
            }

            return dependencies;
        }

        private static AliasPackageContract ParseAliasPackageContract(JObject packageJson, JObject yucp)
        {
            if (yucp == null)
            {
                return null;
            }

            if (yucp["installPlan"] != null ||
                yucp["plan"] != null ||
                yucp["resolvedRelease"] != null ||
                yucp["resolvedArtifact"] != null ||
                yucp["release"] != null ||
                yucp["artifact"] != null)
            {
                throw new FormatException(
                    "Alias package metadata contains a removed delivery field.");
            }

            var contract = new AliasPackageContract
            {
                kind = GetString(yucp, "kind") ?? string.Empty,
                aliasId = GetString(yucp, "aliasId") ?? string.Empty,
                packageName = GetString(packageJson, "name") ?? string.Empty,
                packageDisplayName = GetString(yucp, "packageDisplayName") ?? GetString(packageJson, "displayName") ?? string.Empty,
                packageVersion = GetString(packageJson, "version") ?? string.Empty,
                installStrategy = GetString(yucp, "installStrategy") ?? string.Empty,
                importerPackage = GetString(yucp, "importerPackage") ?? string.Empty,
                minImporterVersion = GetString(yucp, "minImporterVersion") ?? string.Empty,
                channel = GetString(yucp, "channel") ?? string.Empty,
                media = ParseAliasPackageMedia(yucp["media"]),
                rawContractJson = yucp.ToString(Formatting.None),
            };

            return string.IsNullOrWhiteSpace(contract.aliasId) ? null : contract;
        }

        private static AliasPackageMediaSet ParseAliasPackageMedia(
            JToken media)
        {
            var result = new AliasPackageMediaSet();
            if (media == null)
            {
                return result;
            }
            if (!(media is JArray entries) || entries.Count > 2)
            {
                throw new FormatException(
                    "Alias package media must be an array with at most two entries.");
            }

            foreach (JToken entry in entries)
            {
                if (!(entry is JObject descriptor))
                {
                    throw new FormatException(
                        "Alias package media entries must be objects.");
                }
                AliasPackageMediaDescriptor parsed =
                    ParseAliasPackageMediaDescriptor(descriptor);
                if (string.Equals(
                        parsed.kind,
                        "icon",
                        StringComparison.Ordinal))
                {
                    if (!string.IsNullOrEmpty(result.icon.kind))
                    {
                        throw new FormatException(
                            "Alias package media contains a duplicate icon.");
                    }
                    result.icon = parsed;
                    continue;
                }
                if (string.Equals(
                        parsed.kind,
                        "banner",
                        StringComparison.Ordinal))
                {
                    if (!string.IsNullOrEmpty(result.banner.kind))
                    {
                        throw new FormatException(
                            "Alias package media contains a duplicate banner.");
                    }
                    result.banner = parsed;
                    continue;
                }
                throw new FormatException(
                    "Alias package media kind is not supported.");
            }
            return result;
        }

        private static AliasPackageMediaDescriptor
            ParseAliasPackageMediaDescriptor(JObject descriptor)
        {
            if (descriptor == null)
            {
                return new AliasPackageMediaDescriptor();
            }
            return new AliasPackageMediaDescriptor
            {
                kind = GetString(descriptor, "kind") ?? string.Empty,
                contentType =
                    GetString(descriptor, "contentType") ?? string.Empty,
                byteSize = descriptor["byteSize"]?.Type ==
                    JTokenType.Integer
                        ? descriptor["byteSize"].Value<long>()
                        : 0,
                sha256 = GetString(descriptor, "sha256") ?? string.Empty,
                localPath =
                    GetString(descriptor, "localPath") ?? string.Empty,
            };
        }

        private static List<string> ParseStringList(JToken token)
        {
            var values = new List<string>();
            if (!(token is JArray array))
            {
                return values;
            }

            foreach (JToken item in array)
            {
                if (item?.Type != JTokenType.String)
                {
                    continue;
                }

                string value = item.Value<string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value.Trim());
                }
            }

            return values;
        }

        private static List<string> NormalizeUserFacingList(
            IEnumerable<string> values,
            int maximumItems,
            int maximumLength,
            string fieldName)
        {
            var normalized = new List<string>();
            foreach (string value in values ?? Enumerable.Empty<string>())
            {
                string item = NormalizeUserFacingText(
                    value,
                    maximumLength,
                    fieldName);
                if (string.IsNullOrEmpty(item) ||
                    normalized.Contains(item, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (normalized.Count >= maximumItems)
                {
                    throw new FormatException(
                        $"Alias {fieldName} metadata has too many values.");
                }
                normalized.Add(item);
            }
            return normalized;
        }

        private static string NormalizeUserFacingText(
            string value,
            int maximumLength,
            string fieldName,
            bool allowLineBreaks = false)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length > maximumLength)
            {
                throw new FormatException(
                    $"Alias {fieldName} metadata is too long.");
            }
            foreach (char character in normalized)
            {
                if (!char.IsControl(character))
                {
                    continue;
                }
                if (allowLineBreaks &&
                    (character == '\r' ||
                        character == '\n' ||
                        character == '\t'))
                {
                    continue;
                }
                throw new FormatException(
                    $"Alias {fieldName} metadata contains invalid text.");
            }
            return normalized;
        }

        private static string ParseAuthor(JToken authorToken)
        {
            if (authorToken == null || authorToken.Type == JTokenType.Null)
            {
                return null;
            }

            if (authorToken.Type == JTokenType.String)
            {
                return authorToken.Value<string>();
            }

            JObject authorObject = authorToken as JObject;
            return GetString(authorObject, "name") ?? authorObject?.ToString(Formatting.None);
        }

        private static string GetString(JObject obj, string propertyName)
        {
            if (obj == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            JToken token = obj[propertyName];
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            return token.Type == JTokenType.String ? token.Value<string>() : token.ToString(Formatting.None);
        }
    }
}

