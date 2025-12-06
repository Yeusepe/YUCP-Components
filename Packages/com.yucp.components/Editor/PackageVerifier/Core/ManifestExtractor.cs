using System;
using System.IO;
using System.Text;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using YUCP.Components.Editor.PackageVerifier.Data;

namespace YUCP.Components.Editor.PackageVerifier.Core
{
    /// <summary>
    /// Extracts manifest and signature from .unitypackage
    /// Unity packages are tar.gz files containing asset files
    /// </summary>
    public static class ManifestExtractor
    {
        private const string ManifestPath = "Assets/_Signing/PackageManifest.json";
        private const string SignaturePath = "Assets/_Signing/PackageManifest.sig";

        // Reflection cache for ImportPackageItem
        private static Type _importPackageItemType;
        private static FieldInfo _destinationAssetPathField;
        private static FieldInfo _sourceFolderField;
        private static FieldInfo _exportedAssetPathField;

        static ManifestExtractor()
        {
            // Cache reflection info for ImportPackageItem
            _importPackageItemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
            if (_importPackageItemType != null)
            {
                _destinationAssetPathField = _importPackageItemType.GetField("destinationAssetPath");
                _sourceFolderField = _importPackageItemType.GetField("sourceFolder");
                _exportedAssetPathField = _importPackageItemType.GetField("exportedAssetPath");
            }
        }

        /// <summary>
        /// Extract manifest and signature from .unitypackage
        /// Prefers ImportPackageItem array if available (during import), otherwise uses SharpZipLib
        /// </summary>
        public static ExtractionResult ExtractSigningData(string packagePath, System.Array importItems = null)
        {
            try
            {
                if (!File.Exists(packagePath))
                {
                    return new ExtractionResult
                    {
                        success = false,
                        error = "Package file not found"
                    };
                }

                PackageManifest manifest = null;
                SignatureData signature = null;

                // Method 1: Extract from ImportPackageItem array (preferred during import)
                if (importItems != null && importItems.Length > 0)
                {
                    bool extracted = TryExtractFromImportItems(importItems, out manifest, out signature);
                    if (extracted && manifest != null && signature != null)
                    {
                        return new ExtractionResult
                        {
                            success = true,
                            manifest = manifest,
                            signature = signature
                        };
                    }
                    
                    if (manifest == null || signature == null)
                    {
                        return new ExtractionResult
                        {
                            success = false,
                            error = "Package is not signed (manifest or signature not found)",
                            manifest = null,
                            signature = null
                        };
                    }
                }

                // Method 2: Try to use SharpZipLib if available
                bool useSharpZipLib = TryExtractWithSharpZipLib(packagePath, out manifest, out signature);
                
                if (!useSharpZipLib)
                {
                    // Fallback: Return error suggesting SharpZipLib
                    return new ExtractionResult
                    {
                        success = false,
                        error = "SharpZipLib not found. Please install ICSharpCode.SharpZipLib to enable package verification. " +
                                "It's available in many Unity projects (VRChat SDK includes it)."
                    };
                }

                if (manifest == null || signature == null)
                {
                    return new ExtractionResult
                    {
                        success = false,
                        error = "Package is not signed (manifest or signature not found)",
                        manifest = null,
                        signature = null
                    };
                }

                return new ExtractionResult
                {
                    success = true,
                    manifest = manifest,
                    signature = signature
                };
            }
            catch (Exception ex)
            {
                return new ExtractionResult
                {
                    success = false,
                    error = $"Extraction failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Extract manifest and signature from ImportPackageItem array (during import)
        /// This uses Unity's already-extracted package files
        /// </summary>
        private static bool TryExtractFromImportItems(System.Array importItems, out PackageManifest manifest, out SignatureData signature)
        {
            manifest = null;
            signature = null;

            if (importItems == null || importItems.Length == 0)
            {
                Debug.LogWarning("[ManifestExtractor] ImportPackageItem array is null or empty");
                return false;
            }

            if (_destinationAssetPathField == null || _sourceFolderField == null)
            {
                Debug.LogWarning("[ManifestExtractor] ImportPackageItem reflection fields not available");
                return false;
            }


            try
            {
                object manifestItem = null;
                object signatureItem = null;

                // Find manifest and signature items
                int itemIndex = 0;
                foreach (var item in importItems)
                {
                    if (item == null)
                    {
                        itemIndex++;
                        continue;
                    }

                    string destinationPath = GetFieldValue<string>(item, _destinationAssetPathField);
                    if (destinationPath == null)
                    {
                        itemIndex++;
                        continue;
                    }

                    if (destinationPath.Equals(ManifestPath, StringComparison.OrdinalIgnoreCase))
                    {
                        manifestItem = item;
                    }
                    else if (destinationPath.Equals(SignaturePath, StringComparison.OrdinalIgnoreCase))
                    {
                        signatureItem = item;
                    }
                    
                    itemIndex++;
                }


                // Extract manifest
                if (manifestItem != null)
                {
                    string sourceFolder = GetFieldValue<string>(manifestItem, _sourceFolderField);
                    
                    if (!string.IsNullOrEmpty(sourceFolder))
                    {
                        string assetFile = Path.Combine(sourceFolder, "asset");
                        
                        if (File.Exists(assetFile))
                        {
                            try
                            {
                                string manifestJson = File.ReadAllText(assetFile);
                                manifest = JsonUtility.FromJson<PackageManifest>(manifestJson);
                                if (manifest == null)
                                {
                                    Debug.LogWarning("[ManifestExtractor] Manifest JSON deserialization returned null");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"[ManifestExtractor] Failed to parse manifest JSON: {ex.Message}");
                            }
                        }
                        else
                        {
                            // Try alternative path
                            string altPath = Path.Combine(sourceFolder, "PackageManifest.json");
                            if (File.Exists(altPath))
                            {
                                string manifestJson = File.ReadAllText(altPath);
                                manifest = JsonUtility.FromJson<PackageManifest>(manifestJson);
                            }
                            else
                            {
                                Debug.LogWarning($"[ManifestExtractor] Manifest asset file not found at: {assetFile} or {altPath}");
                            }
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[ManifestExtractor] Manifest item sourceFolder is null or empty");
                    }
                }

                // Extract signature
                if (signatureItem != null)
                {
                    string sourceFolder = GetFieldValue<string>(signatureItem, _sourceFolderField);
                    
                    if (!string.IsNullOrEmpty(sourceFolder))
                    {
                        string assetFile = Path.Combine(sourceFolder, "asset");
                        
                        if (File.Exists(assetFile))
                        {
                            try
                            {
                                string signatureJson = File.ReadAllText(assetFile);
                                signature = JsonUtility.FromJson<SignatureData>(signatureJson);
                                if (signature == null)
                                {
                                    Debug.LogWarning("[ManifestExtractor] Signature JSON deserialization returned null");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"[ManifestExtractor] Failed to parse signature JSON: {ex.Message}");
                            }
                        }
                        else
                        {
                            // Try alternative path
                            string altPath = Path.Combine(sourceFolder, "PackageManifest.sig");
                            if (File.Exists(altPath))
                            {
                                string signatureJson = File.ReadAllText(altPath);
                                signature = JsonUtility.FromJson<SignatureData>(signatureJson);
                            }
                            else
                            {
                                Debug.LogWarning($"[ManifestExtractor] Signature asset file not found at: {assetFile} or {altPath}");
                            }
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[ManifestExtractor] Signature item sourceFolder is null or empty");
                    }
                }

                return manifest != null && signature != null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ManifestExtractor] Exception extracting from ImportPackageItem: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get field value using reflection
        /// </summary>
        private static T GetFieldValue<T>(object obj, FieldInfo field)
        {
            if (field == null || obj == null) return default(T);
            try
            {
                object value = field.GetValue(obj);
                if (value is T)
                    return (T)value;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ManifestExtractor] Failed to get field value: {ex.Message}");
            }
            return default(T);
        }

        /// <summary>
        /// Try to extract using SharpZipLib (if available)
        /// </summary>
        private static bool TryExtractWithSharpZipLib(string packagePath, out PackageManifest manifest, out SignatureData signature)
        {
            manifest = null;
            signature = null;

            try
            {
                // Check if SharpZipLib is available
                var tarArchiveType = Type.GetType("ICSharpCode.SharpZipLib.Tar.TarArchive, ICSharpCode.SharpZipLib");
                var gzipInputStreamType = Type.GetType("ICSharpCode.SharpZipLib.GZip.GZipInputStream, ICSharpCode.SharpZipLib");
                
                if (tarArchiveType == null || gzipInputStreamType == null)
                {
                    return false; // SharpZipLib not available
                }

                // Use reflection to call SharpZipLib methods
                using (var fileStream = File.OpenRead(packagePath))
                {
                    var gzipCtor = gzipInputStreamType.GetConstructor(new[] { typeof(Stream) });
                    var gzipStream = gzipCtor.Invoke(new object[] { fileStream });
                    
                    var createInputMethod = tarArchiveType.GetMethod("CreateInputTarArchive", new[] { typeof(Stream), typeof(Encoding) });
                    var tarArchive = createInputMethod.Invoke(null, new object[] { gzipStream, Encoding.UTF8 });

                    // Create temp directory
                    string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        // Extract contents
                        var extractMethod = tarArchiveType.GetMethod("ExtractContents");
                        extractMethod.Invoke(tarArchive, new object[] { tempDir });

                        // Find manifest and signature
                        string[] folders = Directory.GetDirectories(tempDir);
                        foreach (string folder in folders)
                        {
                            string pathnameFile = Path.Combine(folder, "pathname");
                            if (!File.Exists(pathnameFile))
                                continue;

                            string pathname = File.ReadAllText(pathnameFile).Trim();
                            string assetFile = Path.Combine(folder, "asset");
                            
                            if (!File.Exists(assetFile))
                                continue;

                            if (pathname == ManifestPath)
                            {
                                string manifestJson = File.ReadAllText(assetFile);
                                manifest = JsonUtility.FromJson<PackageManifest>(manifestJson);
                            }
                            else if (pathname == SignaturePath)
                            {
                                string signatureJson = File.ReadAllText(assetFile);
                                signature = JsonUtility.FromJson<SignatureData>(signatureJson);
                            }
                        }
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }


        public class ExtractionResult
        {
            public bool success;
            public string error;
            public PackageManifest manifest;
            public SignatureData signature;
        }
    }
}


