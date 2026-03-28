using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
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
        private const string ProtectedPayloadFileName = "YUCP_ProtectedPayload.json";
        private const string PackageJsonFileName = "package.json";
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
            if (importItems == null || importItems.Length == 0)
            {
                return CreateFallbackMetadata(packagePath, null, packageIconPath, importItems);
            }

            // Find metadata item in import items
            object metadataItem = FindMetadataItem(importItems);
            if (metadataItem == null)
            {
                Debug.Log("[YUCP PackageManager] No YUCP metadata file found in import items. Falling back to package name and Unity icon.");
                return CreateFallbackMetadata(packagePath, null, packageIconPath, importItems);
            }

            // Read metadata file from extracted package location
            string sourceFolder = GetFieldValue<string>(metadataItem, _sourceFolderField);
            string exportedPath = GetFieldValue<string>(metadataItem, _exportedAssetPathField);
            string destinationPath = GetFieldValue<string>(metadataItem, _destinationAssetPathField);

            if (string.IsNullOrEmpty(sourceFolder) || string.IsNullOrEmpty(exportedPath))
            {
                Debug.LogWarning("[YUCP PackageManager] Source folder or exported path is empty, creating fallback metadata");
                return CreateFallbackMetadata(packagePath, null, packageIconPath, importItems);
            }

            Debug.Log($"[YUCP PackageManager] Reading metadata from import item '{destinationPath ?? exportedPath}'.");
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

                // Propagate license requirements
                if (metadataJson.licensePackages != null)
                {
                    foreach (var lp in metadataJson.licensePackages)
                    {
                        if (lp == null || string.IsNullOrEmpty(lp.packageId)) continue;
                        metadata.licensePackages.Add(new LicensePackageRequirement
                        {
                            packageId        = lp.packageId,
                            packageName      = lp.packageName ?? lp.packageId,
                            productId        = lp.productId ?? "",
                            gumroadPermalink = lp.gumroadPermalink ?? "",
                            jinxxyProductId  = lp.jinxxyProductId ?? "",
                            discordGuildId   = lp.discordGuildId ?? "",
                            discordRoleId    = lp.discordRoleId  ?? "",
                            creatorAuthUserId = lp.creatorAuthUserId ?? "",
                        });
                    }
                }

                // Propagate storefront metadata
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
                    foreach (var ab in metadataJson.assetBreakdown)
                    {
                        if (ab != null && !string.IsNullOrEmpty(ab.type))
                            metadata.assetBreakdown.Add(new AssetBreakdownEntry(ab.type, ab.count));
                    }
                }

                // Resolve gallery images from embedded paths
                if (metadataJson.galleryImages != null)
                {
                    foreach (var galleryPath in metadataJson.galleryImages)
                    {
                        if (string.IsNullOrEmpty(galleryPath)) continue;
                        var tex = ResolveTextureFromPath(galleryPath, importItems);
                        if (tex != null)
                            metadata.galleryImages.Add(tex);
                    }
                }

                metadata.protectedPayload = ExtractProtectedPayloadDescriptor(importItems);

                Debug.Log($"[YUCP PackageManager] Parsed package metadata. packageName='{metadata.packageName}', version='{metadata.version}', iconLoaded={metadata.icon != null}, bannerLoaded={metadata.banner != null}, productLinks={metadata.productLinks.Count}, dependencies={metadata.dependencies.Count}");
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

        private static object FindMetadataItem(System.Array importItems)
        {
            return FindItemByDestinationPath(importItems, IsMetadataAssetPath);
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

        internal static ProtectedPayloadDescriptor ExtractProtectedPayloadDescriptor(System.Array importItems)
        {
            if (importItems == null || importItems.Length == 0)
                return null;

            object descriptorItem = FindItemByDestinationPath(importItems, IsProtectedPayloadAssetPath);
            if (descriptorItem == null)
                return null;

            string sourceFolder = GetFieldValue<string>(descriptorItem, _sourceFolderField);
            string exportedPath = GetFieldValue<string>(descriptorItem, _exportedAssetPathField);
            if (string.IsNullOrEmpty(sourceFolder) || string.IsNullOrEmpty(exportedPath))
                return null;

            string json = ReadMetadataFile(sourceFolder, exportedPath);
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                var descriptor = JsonUtility.FromJson<ProtectedPayloadDescriptor>(json);
                if (descriptor == null)
                    return null;

                descriptor.formatVersion = string.IsNullOrEmpty(descriptor.formatVersion) ? "1" : descriptor.formatVersion;
                descriptor.protectedAssetId ??= "";
                descriptor.blobAssetPath ??= "";
                descriptor.cipher ??= "";
                descriptor.archiveFormat ??= "";
                descriptor.ciphertextSha256 ??= "";
                descriptor.plaintextSha256 ??= "";
                descriptor.payloadAssetPaths =
                    ProtectedPayloadIntegrityUtility.NormalizeUnityPaths(descriptor.payloadAssetPaths);
                descriptor.manifestBindingSha256 = string.IsNullOrWhiteSpace(descriptor.manifestBindingSha256)
                    ? ProtectedPayloadIntegrityUtility.ComputeManifestBindingSha256(descriptor)
                    : descriptor.manifestBindingSha256;
                return descriptor;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to parse protected payload descriptor: {ex.Message}");
                return null;
            }
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

        internal static PackageMetadata ExtractMetadataFromInstalledShell(
            string metadataAssetPath,
            string tempInstallAssetPath = null,
            string protectedPayloadAssetPath = null,
            string fallbackPackageName = null)
        {
            if (string.IsNullOrWhiteSpace(metadataAssetPath))
                return CreateInstalledShellFallbackMetadata(fallbackPackageName ?? "Protected Package", fallbackPackageName, tempInstallAssetPath, protectedPayloadAssetPath);

            string metadataJsonText = LoadTextAssetContents(metadataAssetPath);
            if (string.IsNullOrWhiteSpace(metadataJsonText))
                return CreateInstalledShellFallbackMetadata(metadataAssetPath, fallbackPackageName, tempInstallAssetPath, protectedPayloadAssetPath);

            string shellRootAssetPath = Path.GetDirectoryName(metadataAssetPath)?.Replace('\\', '/') ?? string.Empty;

            try
            {
                var metadataJson = JsonUtility.FromJson<PackageMetadataJson>(metadataJsonText);
                if (metadataJson == null)
                    return CreateInstalledShellFallbackMetadata(metadataAssetPath, fallbackPackageName, tempInstallAssetPath, protectedPayloadAssetPath);

                var metadata = new PackageMetadata
                {
                    packageName = metadataJson.packageName ?? fallbackPackageName ?? string.Empty,
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

                if (!string.IsNullOrEmpty(metadataJson.icon))
                    metadata.icon = LoadInstalledTexture(metadataJson.icon, shellRootAssetPath);

                if (!string.IsNullOrEmpty(metadataJson.banner))
                    metadata.banner = LoadInstalledTexture(metadataJson.banner, shellRootAssetPath);

                if (metadataJson.productLinks != null)
                {
                    foreach (var link in metadataJson.productLinks)
                    {
                        var productLink = new ProductLink(link?.url ?? string.Empty, link?.label ?? string.Empty);
                        if (!string.IsNullOrEmpty(link?.icon))
                            productLink.customIcon = LoadInstalledTexture(link.icon, shellRootAssetPath);
                        metadata.productLinks.Add(productLink);
                    }
                }

                if (metadataJson.licensePackages != null)
                {
                    foreach (var lp in metadataJson.licensePackages)
                    {
                        if (lp == null || string.IsNullOrEmpty(lp.packageId)) continue;
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
                    metadata.supportedPlatforms = new List<string>(metadataJson.supportedPlatforms);
                if (metadataJson.tags != null)
                    metadata.tags = new List<string>(metadataJson.tags);

                if (metadataJson.assetBreakdown != null)
                {
                    foreach (var ab in metadataJson.assetBreakdown)
                    {
                        if (ab != null && !string.IsNullOrEmpty(ab.type))
                            metadata.assetBreakdown.Add(new AssetBreakdownEntry(ab.type, ab.count));
                    }
                }

                if (metadataJson.galleryImages != null)
                {
                    foreach (var galleryPath in metadataJson.galleryImages)
                    {
                        if (string.IsNullOrEmpty(galleryPath)) continue;
                        var tex = LoadInstalledTexture(galleryPath, shellRootAssetPath);
                        if (tex != null)
                            metadata.galleryImages.Add(tex);
                    }
                }

                if (!string.IsNullOrWhiteSpace(tempInstallAssetPath))
                {
                    string packageJsonText = LoadTextAssetContents(tempInstallAssetPath);
                    if (!string.IsNullOrWhiteSpace(packageJsonText))
                        ParsePackageJsonDependencies(metadata, packageJsonText);
                }

                metadata.protectedPayload = ExtractProtectedPayloadDescriptorFromAssetPath(protectedPayloadAssetPath);
                return metadata;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to parse installed-shell metadata '{metadataAssetPath}': {ex.Message}");
                return CreateInstalledShellFallbackMetadata(metadataAssetPath, fallbackPackageName, tempInstallAssetPath, protectedPayloadAssetPath);
            }
        }

        private static PackageMetadata CreateInstalledShellFallbackMetadata(
            string packagePathOrName,
            string fallbackPackageName,
            string tempInstallAssetPath,
            string protectedPayloadAssetPath)
        {
            var fallback = CreateFallbackMetadata(packagePathOrName, fallbackPackageName);
            if (!string.IsNullOrWhiteSpace(tempInstallAssetPath))
            {
                string packageJsonText = LoadTextAssetContents(tempInstallAssetPath);
                if (!string.IsNullOrWhiteSpace(packageJsonText))
                    ParsePackageJsonDependencies(fallback, packageJsonText);
            }

            fallback.protectedPayload = ExtractProtectedPayloadDescriptorFromAssetPath(protectedPayloadAssetPath);
            return fallback;
        }

        internal static ProtectedPayloadDescriptor ExtractProtectedPayloadDescriptorFromAssetPath(string protectedPayloadAssetPath)
        {
            if (string.IsNullOrWhiteSpace(protectedPayloadAssetPath))
                return null;

            string json = LoadTextAssetContents(protectedPayloadAssetPath);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            string shellRootAssetPath = Path.GetDirectoryName(protectedPayloadAssetPath)?.Replace('\\', '/') ?? string.Empty;

            try
            {
                var descriptor = JsonUtility.FromJson<ProtectedPayloadDescriptor>(json);
                if (descriptor == null)
                    return null;

                descriptor.formatVersion = string.IsNullOrEmpty(descriptor.formatVersion) ? "1" : descriptor.formatVersion;
                descriptor.protectedAssetId ??= string.Empty;
                descriptor.blobAssetPath ??= string.Empty;
                descriptor.cipher ??= string.Empty;
                descriptor.archiveFormat ??= string.Empty;
                descriptor.ciphertextSha256 ??= string.Empty;
                descriptor.plaintextSha256 ??= string.Empty;
                descriptor.payloadAssetPaths =
                    ProtectedPayloadIntegrityUtility.NormalizeUnityPaths(descriptor.payloadAssetPaths);
                descriptor.manifestBindingSha256 = string.IsNullOrWhiteSpace(descriptor.manifestBindingSha256)
                    ? ProtectedPayloadIntegrityUtility.ComputeManifestBindingSha256(descriptor)
                    : descriptor.manifestBindingSha256;
                descriptor.blobAssetPath = ResolveInstalledAssetPath(descriptor.blobAssetPath, shellRootAssetPath) ?? descriptor.blobAssetPath;
                return descriptor;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to parse installed-shell protected payload descriptor '{protectedPayloadAssetPath}': {ex.Message}");
                return null;
            }
        }

        private static Texture2D LoadInstalledTexture(string textureAssetPath, string shellRootAssetPath)
        {
            string resolved = ResolveInstalledAssetPath(textureAssetPath, shellRootAssetPath);
            if (string.IsNullOrWhiteSpace(resolved))
                return null;

            return AssetDatabase.LoadAssetAtPath<Texture2D>(resolved);
        }

        private static string ResolveInstalledAssetPath(string assetPath, string shellRootAssetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            string normalized = assetPath.Replace('\\', '/').TrimStart('/');
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            if (string.IsNullOrWhiteSpace(shellRootAssetPath))
                return normalized;

            return (shellRootAssetPath.TrimEnd('/') + "/" + normalized).Replace('\\', '/');
        }

        private static string LoadTextAssetContents(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            string normalized = assetPath.Replace('\\', '/');
            var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(normalized);
            if (textAsset != null)
                return textAsset.text;

            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string diskPath = Path.Combine(projectRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(diskPath) ? File.ReadAllText(diskPath) : null;
            }
            catch
            {
                return null;
            }
        }

        internal static bool HasTempInstallDescriptor(System.Array importItems)
        {
            string destinationPath = GetPackageJsonDestinationPath(importItems);
            return IsTempInstallPackageJsonPath(destinationPath);
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
                if (IsPackageJsonAssetPath(destinationPath))
                {
                    return item;
                }

                // Check for temporary install path pattern
                if (IsTempInstallPackageJsonPath(destinationPath))
                {
                    return item;
                }
            }
            return null;
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

        internal static bool IsProtectedPayloadAssetPath(string destinationPath)
        {
            if (string.IsNullOrEmpty(destinationPath))
            {
                return false;
            }

            string normalizedPath = destinationPath.Replace('\\', '/');
            return normalizedPath.Equals("Assets/" + ProtectedPayloadFileName, StringComparison.OrdinalIgnoreCase) ||
                (normalizedPath.StartsWith(InstalledPackagesRootAssetPath, StringComparison.OrdinalIgnoreCase) &&
                 normalizedPath.EndsWith("/" + ProtectedPayloadFileName, StringComparison.OrdinalIgnoreCase));
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
