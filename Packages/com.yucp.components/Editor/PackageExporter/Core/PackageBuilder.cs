using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.PackageExporter
{
    /// <summary>
    /// Orchestrates the complete package export process including obfuscation and icon injection.
    /// Handles validation, folder filtering, DLL obfuscation, and final package creation.
    /// </summary>
    public static class PackageBuilder
    {
        public class ExportResult
        {
            public bool success;
            public string errorMessage;
            public string outputPath;
            public float buildTimeSeconds;
            public int filesExported;
            public int assembliesObfuscated;
        }
        
        /// <summary>
        /// Export a package based on the provided profile
        /// </summary>
        public static ExportResult ExportPackage(ExportProfile profile, Action<float, string> progressCallback = null)
        {
            var result = new ExportResult();
            var startTime = DateTime.Now;
            
            try
            {
                // Validate profile
                progressCallback?.Invoke(0.05f, "Validating export profile...");
                if (!profile.Validate(out string errorMessage))
                {
                    result.success = false;
                    result.errorMessage = errorMessage;
                    return result;
                }
                
                // Handle obfuscation if enabled
                if (profile.enableObfuscation)
                {
                    progressCallback?.Invoke(0.1f, "Ensuring ConfuserEx is installed...");
                    
                    if (!ConfuserExManager.EnsureInstalled((progress, status) =>
                    {
                        progressCallback?.Invoke(0.1f + progress * 0.1f, status);
                    }))
                    {
                        result.success = false;
                        result.errorMessage = "Failed to install ConfuserEx";
                        return result;
                    }
                    
                    progressCallback?.Invoke(0.2f, "Obfuscating assemblies...");
                    
                    if (!ConfuserExManager.ObfuscateAssemblies(
                        profile.assembliesToObfuscate,
                        profile.obfuscationPreset,
                        (progress, status) =>
                        {
                            progressCallback?.Invoke(0.2f + progress * 0.3f, status);
                        }))
                    {
                        result.success = false;
                        result.errorMessage = "Assembly obfuscation failed";
                        return result;
                    }
                    
                    result.assembliesObfuscated = profile.assembliesToObfuscate.Count(a => a.enabled);
                }
                
                // Build list of assets to export
                progressCallback?.Invoke(0.5f, "Collecting assets to export...");
                
                List<string> assetsToExport = CollectAssetsToExport(profile);
                if (assetsToExport.Count == 0)
                {
                    result.success = false;
                    result.errorMessage = "No assets found to export";
                    return result;
                }
                
                // Handle bundled dependencies
                var bundledDeps = profile.dependencies.Where(d => d.enabled && d.exportMode == DependencyExportMode.Bundle).ToList();
                if (bundledDeps.Count > 0)
                {
                    progressCallback?.Invoke(0.55f, $"Adding {bundledDeps.Count} bundled dependencies...");
                    
                    foreach (var dep in bundledDeps)
                    {
                        var depPackageInfo = DependencyScanner.ScanInstalledPackages()
                            .FirstOrDefault(p => p.packageName == dep.packageName);
                        
                        if (depPackageInfo != null && Directory.Exists(depPackageInfo.packagePath))
                        {
                            // Add all assets from this package
                            string relativePath = GetRelativePackagePath(depPackageInfo.packagePath);
                            string[] depGuids = AssetDatabase.FindAssets("", new[] { relativePath });
                            
                            foreach (string guid in depGuids)
                            {
                                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                                if (!ShouldExcludeAsset(assetPath, profile))
                                {
                                    assetsToExport.Add(assetPath);
                                }
                            }
                            
                            Debug.Log($"[PackageBuilder] Bundled dependency: {dep.packageName}");
                        }
                    }
                }
                
                 // Generate package.json if needed (but don't add to Unity export - will inject later)
                 string packageJsonContent = null;
                 if (profile.generatePackageJson)
                 {
                     progressCallback?.Invoke(0.58f, "Generating package.json...");
                     
                     // Generate the content but don't create a file in the Assets folder yet
                     // This avoids Unity import issues
                     packageJsonContent = DependencyScanner.GeneratePackageJson(
                         profile,
                         profile.dependencies,
                         null
                     );
                     
                     Debug.Log("[PackageBuilder] Generated package.json content (will inject after export)");
                 }
                
                result.filesExported = assetsToExport.Count;                
                // Create temp package path
                progressCallback?.Invoke(0.6f, "Exporting Unity package...");
                
                string tempPackagePath = Path.Combine(Path.GetTempPath(), $"YUCP_Temp_{Guid.NewGuid():N}.unitypackage");
                
                 // Build export options (Interactive mode is never used in programmatic exports)
                 ExportPackageOptions options = ExportPackageOptions.Default;
                 if (profile.includeDependencies)
                     options |= ExportPackageOptions.IncludeDependencies;
                 if (profile.recurseFolders)
                     options |= ExportPackageOptions.Recurse;
                
                 // Convert all assets to Unity-relative paths and validate
                 var validAssets = new List<string>();
                 foreach (string asset in assetsToExport)
                 {
                     string unityPath = GetRelativePackagePath(asset);
                     
                     if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityPath) != null)
                     {
                         validAssets.Add(unityPath);
                     }
                     else
                     {
                         Debug.LogWarning($"[PackageBuilder] Could not load asset: {unityPath}");
                     }
                 }
                
                if (validAssets.Count == 0)
                {
                    throw new InvalidOperationException("No valid assets found to export. Check that the specified folders contain valid Unity assets.");
                }
                
                Debug.Log($"[PackageBuilder] Found {validAssets.Count} valid assets out of {assetsToExport.Count} total");
                
                 // Final validation of all assets before export
                 var finalValidAssets = new List<string>();
                 foreach (string asset in validAssets)
                 {
                     if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(asset) != null)
                     {
                         finalValidAssets.Add(asset);
                     }
                     else
                     {
                         Debug.LogWarning($"[PackageBuilder] Asset no longer valid during export: {asset}");
                     }
                 }
                 
                 if (finalValidAssets.Count == 0)
                 {
                     throw new InvalidOperationException("No valid assets remain for export after final validation.");
                 }
                 
                 Debug.Log($"[PackageBuilder] Exporting {finalValidAssets.Count} final assets to: {tempPackagePath}");
                 
                 // Log what we're about to export
                 Debug.Log($"[PackageBuilder] Assets to export: {string.Join(", ", finalValidAssets)}");
                 Debug.Log($"[PackageBuilder] Export options: {options}");
                 Debug.Log($"[PackageBuilder] Target path: {tempPackagePath}");
                 
                 // Export the package
                 try
                 {
                     AssetDatabase.ExportPackage(
                         finalValidAssets.ToArray(),
                         tempPackagePath,
                         options
                     );
                     Debug.Log("[PackageBuilder] ExportPackage call completed");
                 }
                 catch (Exception ex)
                 {
                     Debug.LogError($"[PackageBuilder] ExportPackage threw exception: {ex.Message}");
                     throw;
                 }
                 
                 // Wait for export to complete - Unity's export is synchronous but file I/O might be async
                 AssetDatabase.Refresh();
                 
                 // Wait for file to be created (with retry and longer delays)
                 int retryCount = 0;
                 while (!File.Exists(tempPackagePath) && retryCount < 30) // Increased to 30 attempts (6 seconds)
                 {
                     if (retryCount % 5 == 0) // Log every 5th attempt
                     {
                         Debug.Log($"[PackageBuilder] Waiting for temp package... attempt {retryCount + 1}/30");
                     }
                     System.Threading.Thread.Sleep(200); // Wait 200ms
                     retryCount++;
                 }
                 
                 // Verify the file was actually created
                 if (!File.Exists(tempPackagePath))
                 {
                     // Check if the temp directory is accessible
                     string tempDir = Path.GetDirectoryName(tempPackagePath);
                     Debug.LogError($"[PackageBuilder] Temp directory: {tempDir}");
                     Debug.LogError($"[PackageBuilder] Temp directory exists: {Directory.Exists(tempDir)}");
                     Debug.LogError($"[PackageBuilder] Temp directory writable: {CheckDirectoryWritable(tempDir)}");
                     
                     throw new FileNotFoundException($"Package export failed - temp file not created after retries: {tempPackagePath}");
                 }
                
                 Debug.Log($"[PackageBuilder] Package exported to temp location: {tempPackagePath}");
                 
                 // Inject package.json and auto-installer into the .unitypackage if needed
                 if (!string.IsNullOrEmpty(packageJsonContent))
                 {
                     progressCallback?.Invoke(0.75f, "Injecting package.json and dependency installer...");
                     
                     try
                     {
                         InjectPackageJsonAndInstaller(tempPackagePath, packageJsonContent);
                         Debug.Log("[PackageBuilder] Successfully injected package.json and auto-installer");
                     }
                     catch (Exception ex)
                     {
                         Debug.LogWarning($"[PackageBuilder] Failed to inject package.json: {ex.Message}");
                     }
                 }
                 
                 // Get final output path
                 string finalOutputPath = profile.GetOutputFilePath();
                
                // Ensure output directory exists
                string outputDir = Path.GetDirectoryName(finalOutputPath);
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                
                // Add icon if specified
                bool iconAdded = false;
                if (profile.icon != null)
                {
                    progressCallback?.Invoke(0.8f, "Adding package icon...");
                    
                    string iconPath = AssetDatabase.GetAssetPath(profile.icon);
                    if (!string.IsNullOrEmpty(iconPath))
                    {
                        string fullIconPath = Path.GetFullPath(iconPath);
                        
                        if (PackageIconInjector.AddIconToPackage(tempPackagePath, fullIconPath, finalOutputPath))
                        {
                            Debug.Log("[PackageBuilder] Icon successfully added to package");
                            iconAdded = true;
                        }
                        else
                        {
                            Debug.LogWarning("[PackageBuilder] Failed to add icon, using package without icon");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[PackageBuilder] Could not find icon asset path");
                    }
                }
                
                // Copy temp package to final location if icon wasn't added
                if (!iconAdded)
                {
                    File.Copy(tempPackagePath, finalOutputPath, true);
                }
                
                // Clean up temp package
                if (File.Exists(tempPackagePath))
                {
                    File.Delete(tempPackagePath);
                }
                
                // Restore original DLLs if obfuscation was used
                if (profile.enableObfuscation)
                {
                    progressCallback?.Invoke(0.95f, "Restoring original assemblies...");
                    ConfuserExManager.RestoreOriginalDlls(profile.assembliesToObfuscate);
                }
                
                progressCallback?.Invoke(1.0f, "Export complete!");
                
                // Update profile statistics
                profile.RecordExport();
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                
                // Build result
                result.success = true;
                result.outputPath = finalOutputPath;
                result.buildTimeSeconds = (float)(DateTime.Now - startTime).TotalSeconds;
                
                Debug.Log($"[PackageBuilder] Export successful! Package saved to: {finalOutputPath}");
                Debug.Log($"[PackageBuilder] Build time: {result.buildTimeSeconds:F2}s | Files: {result.filesExported} | Obfuscated: {result.assembliesObfuscated}");
                
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Export failed: {ex.Message}");
                Debug.LogException(ex);
                
                // Restore original DLLs on error
                if (profile.enableObfuscation)
                {
                    try
                    {
                        ConfuserExManager.RestoreOriginalDlls(profile.assembliesToObfuscate);
                    }
                    catch
                    {
                        // Ignore restoration errors
                    }
                }
                
                 result.success = false;
                 result.errorMessage = ex.Message;
                 result.buildTimeSeconds = (float)(DateTime.Now - startTime).TotalSeconds;
                 
                 return result;
            }
        }
        
        /// <summary>
        /// Generate a package.json file for the export
        /// </summary>
        private static string GeneratePackageJson(ExportProfile profile)
        {
            try
            {
                // Look for existing package.json in first export folder
                string existingPackageJsonPath = null;
                foreach (string folder in profile.foldersToExport)
                {
                    string testPath = Path.Combine(folder, "package.json");
                    if (File.Exists(testPath))
                    {
                        existingPackageJsonPath = testPath;
                        break;
                    }
                }
                
                // Generate package.json content
                string packageJsonContent = DependencyScanner.GeneratePackageJson(
                    profile,
                    profile.dependencies,
                    existingPackageJsonPath
                );
                
                // If we have an existing package.json, update it in place
                if (!string.IsNullOrEmpty(existingPackageJsonPath))
                {
                    File.WriteAllText(existingPackageJsonPath, packageJsonContent);
                    AssetDatabase.Refresh();
                    Debug.Log($"[PackageBuilder] Updated existing package.json: {existingPackageJsonPath}");
                    return existingPackageJsonPath;
                }
                
                 // Otherwise, create a temporary package.json in the first export folder
                 if (profile.foldersToExport.Count > 0)
                 {
                     string tempPackageJsonPath = Path.Combine(profile.foldersToExport[0], "package.json");
                     
                     // Ensure the file is created with proper permissions and timestamp
                     File.WriteAllText(tempPackageJsonPath, packageJsonContent);
                     
                     // Force file system sync and refresh
                     File.SetLastWriteTime(tempPackageJsonPath, DateTime.Now);
                     AssetDatabase.Refresh();
                     
                     // Wait a moment for Unity to process the file
                     System.Threading.Thread.Sleep(100);
                     AssetDatabase.Refresh();
                     
                     Debug.Log($"[PackageBuilder] Created package.json: {tempPackageJsonPath}");
                     return tempPackageJsonPath;
                 }
                
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageBuilder] Failed to generate package.json: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Convert absolute path to Unity-relative path (Assets/... or Packages/...)
        /// </summary>
        private static string GetRelativePackagePath(string absolutePath)
        {
            // If already a Unity-relative path, return as-is
            if (absolutePath.StartsWith("Assets/") || absolutePath.StartsWith("Packages/"))
            {
                return absolutePath;
            }
            
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            
            // Normalize both paths for comparison (use forward slashes)
            string normalizedInput = absolutePath.Replace('\\', '/');
            string normalizedProject = projectPath.Replace('\\', '/');
            
            if (normalizedInput.StartsWith(normalizedProject))
            {
                string relative = normalizedInput.Substring(normalizedProject.Length);
                
                if (relative.StartsWith("/"))
                {
                    relative = relative.Substring(1);
                }
                
                return relative;
            }
            
            return absolutePath;
        }
        
        /// <summary>
        /// Collect all assets to export based on profile settings
        /// </summary>
        private static List<string> CollectAssetsToExport(ExportProfile profile)
        {
            var assets = new HashSet<string>();
            
            foreach (string folder in profile.foldersToExport)
            {
                string assetFolder = folder;
                
                // Convert absolute path to relative path if needed
                if (folder.StartsWith(Application.dataPath))
                {
                    assetFolder = folder.Replace(Application.dataPath, "Assets");
                }
                else if (!folder.StartsWith("Assets"))
                {
                    // Try to convert to relative path
                    string relativePath = "Assets" + folder.Replace(Application.dataPath, "");
                    if (Directory.Exists(relativePath))
                    {
                        assetFolder = relativePath;
                    }
                }
                
                // Check if the folder exists on disk
                string physicalPath = assetFolder.StartsWith("Assets") ? 
                    assetFolder.Replace("Assets", Application.dataPath) : assetFolder;
                    
                if (!Directory.Exists(physicalPath))
                {
                    Debug.LogWarning($"[PackageBuilder] Folder not found: {physicalPath}");
                    continue;
                }
                
                // Refresh AssetDatabase to ensure the folder is recognized
                AssetDatabase.Refresh();
                
                // Check if the folder exists in AssetDatabase
                if (!AssetDatabase.IsValidFolder(assetFolder))
                {
                    Debug.LogWarning($"[PackageBuilder] Folder not recognized by AssetDatabase: {assetFolder}. Creating meta file...");
                    
                    // Try to create a .meta file for the folder
                    string metaPath = assetFolder + ".meta";
                    if (!File.Exists(metaPath))
                    {
                        try
                        {
                            // Create a basic .meta file
                            string metaContent = $"fileFormatVersion: 2\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                            File.WriteAllText(metaPath, metaContent);
                            AssetDatabase.Refresh();
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"[PackageBuilder] Failed to create meta file for {assetFolder}: {ex.Message}");
                            continue;
                        }
                    }
                }
                
                // Get all assets in this folder
                string[] guids = AssetDatabase.FindAssets("", new[] { assetFolder });
                
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    
                    // Apply exclusion filters
                    if (ShouldExcludeAsset(assetPath, profile))
                    {
                        continue;
                    }
                    
                    assets.Add(assetPath);
                }
            }
            
            return assets.ToList();
        }
        
        /// <summary>
        /// Check if an asset should be excluded based on filters
        /// </summary>
        private static bool ShouldExcludeAsset(string assetPath, ExportProfile profile)
        {
            // Check file pattern exclusions
            string fileName = Path.GetFileName(assetPath);
            foreach (string pattern in profile.excludeFilePatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;
                
                // Simple wildcard matching
                if (WildcardMatch(fileName, pattern))
                {
                    return true;
                }
            }
            
            // Check folder name exclusions
            string[] pathParts = assetPath.Split('/', '\\');
            foreach (string folderName in profile.excludeFolderNames)
            {
                if (string.IsNullOrWhiteSpace(folderName))
                    continue;
                
                if (pathParts.Any(part => part.Equals(folderName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Simple wildcard matching (* and ? support)
        /// </summary>
        private static bool WildcardMatch(string text, string pattern)
        {
            // Convert wildcard pattern to regex
            string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            
            return System.Text.RegularExpressions.Regex.IsMatch(text, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        
        /// <summary>
        /// Check if a directory is writable
        /// </summary>
        private static bool CheckDirectoryWritable(string directoryPath)
        {
            try
            {
                string testFile = Path.Combine(directoryPath, $"test_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Inject package.json and DirectVpmInstaller into a .unitypackage file
        /// </summary>
        private static void InjectPackageJsonAndInstaller(string unityPackagePath, string packageJsonContent)
        {
            // Unity packages are tar.gz archives
            // We need to:
            // 1. Extract the package
            // 2. Add package.json and DirectVpmInstaller.cs as new assets
            // 3. Recompress
            
            string tempExtractDir = Path.Combine(Path.GetTempPath(), $"YUCP_PackageExtract_{Guid.NewGuid():N}");
            
            try
            {
                // Create temp directory
                Directory.CreateDirectory(tempExtractDir);
                
                // Extract the .unitypackage (it's a tar.gz)
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
                using (var fileStream = File.OpenRead(unityPackagePath))
                using (var gzipStream = new ICSharpCode.SharpZipLib.GZip.GZipInputStream(fileStream))
                using (var tarArchive = ICSharpCode.SharpZipLib.Tar.TarArchive.CreateInputTarArchive(gzipStream, System.Text.Encoding.UTF8))
                {
                    tarArchive.ExtractContents(tempExtractDir);
                }
#else
                Debug.LogError("[PackageBuilder] ICSharpCode.SharpZipLib not available. Package injection disabled.");
                return;
#endif
                
                // Create a new folder for package.json in the tar structure
                // Unity packages have a specific structure: each asset gets a GUID folder with:
                // - asset (the actual file)
                // - asset.meta (metadata)
                // - pathname (path in the project)
                
                // 1. Inject package.json
                string packageJsonGuid = Guid.NewGuid().ToString("N");
                string packageJsonFolder = Path.Combine(tempExtractDir, packageJsonGuid);
                Directory.CreateDirectory(packageJsonFolder);
                
                File.WriteAllText(Path.Combine(packageJsonFolder, "asset"), packageJsonContent);
                File.WriteAllText(Path.Combine(packageJsonFolder, "pathname"), "Assets/package.json");
                
                string packageJsonMeta = "fileFormatVersion: 2\nguid: " + packageJsonGuid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                File.WriteAllText(Path.Combine(packageJsonFolder, "asset.meta"), packageJsonMeta);
                
                // 2. Inject DirectVpmInstaller.cs
                // Try to find the script in the package
                string installerScriptPath = null;
                string[] foundScripts = AssetDatabase.FindAssets("DirectVpmInstaller t:Script");
                
                if (foundScripts.Length > 0)
                {
                    installerScriptPath = AssetDatabase.GUIDToAssetPath(foundScripts[0]);
                    Debug.Log($"[PackageBuilder] Found DirectVpmInstaller at: {installerScriptPath}");
                }
                
                if (!string.IsNullOrEmpty(installerScriptPath) && File.Exists(installerScriptPath))
                {
                    string installerGuid = Guid.NewGuid().ToString("N");
                    string installerFolder = Path.Combine(tempExtractDir, installerGuid);
                    Directory.CreateDirectory(installerFolder);
                    
                    string installerContent = File.ReadAllText(installerScriptPath);
                    File.WriteAllText(Path.Combine(installerFolder, "asset"), installerContent);
                    File.WriteAllText(Path.Combine(installerFolder, "pathname"), "Assets/Editor/DirectVpmInstaller.cs");
                    
                    string installerMeta = "fileFormatVersion: 2\nguid: " + installerGuid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
                    File.WriteAllText(Path.Combine(installerFolder, "asset.meta"), installerMeta);
                    
                    Debug.Log("[PackageBuilder] Added DirectVpmInstaller.cs to package");
                }
                else
                {
                    Debug.LogWarning("[PackageBuilder] Could not find DirectVpmInstaller.cs template");
                }
                
                // Recompress the package
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
                string tempOutputPath = unityPackagePath + ".tmp";
                
                using (var outputStream = File.Create(tempOutputPath))
                using (var gzipStream = new ICSharpCode.SharpZipLib.GZip.GZipOutputStream(outputStream))
                using (var tarArchive = ICSharpCode.SharpZipLib.Tar.TarArchive.CreateOutputTarArchive(gzipStream, System.Text.Encoding.UTF8))
                {
                    tarArchive.RootPath = tempExtractDir.Replace('\\', '/');
                    if (tarArchive.RootPath.EndsWith("/"))
                        tarArchive.RootPath = tarArchive.RootPath.Remove(tarArchive.RootPath.Length - 1);
                    
                    AddDirectoryFilesToTar(tarArchive, tempExtractDir, true);
                }
                
                // Replace original with new package
                File.Delete(unityPackagePath);
                File.Move(tempOutputPath, unityPackagePath);
#endif
            }
            finally
            {
                // Clean up temp directory
                if (Directory.Exists(tempExtractDir))
                {
                    try
                    {
                        Directory.Delete(tempExtractDir, true);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }
        
        /// <summary>
        /// Helper to recursively add files to a tar archive
        /// </summary>
        private static void AddDirectoryFilesToTar(object tarArchive, string sourceDirectory, bool recurse)
        {
#if UNITY_EDITOR && UNITY_2022_3_OR_NEWER
            var archive = tarArchive as ICSharpCode.SharpZipLib.Tar.TarArchive;
            if (archive == null) return;
            
            var filenames = Directory.GetFiles(sourceDirectory);
            foreach (string filename in filenames)
            {
                var entry = ICSharpCode.SharpZipLib.Tar.TarEntry.CreateEntryFromFile(filename);
                archive.WriteEntry(entry, false);
            }

            if (recurse)
            {
                var directories = Directory.GetDirectories(sourceDirectory);
                foreach (string directory in directories)
                    AddDirectoryFilesToTar(archive, directory, recurse);
            }
#else
            Debug.LogError("[PackageBuilder] ICSharpCode.SharpZipLib not available. Please install the ICSharpCode.SharpZipLib package.");
#endif
        }
        
        /// <summary>
        /// Export multiple profiles in sequence
        /// </summary>
        public static List<ExportResult> ExportMultiple(List<ExportProfile> profiles, Action<int, int, float, string> progressCallback = null)
        {
            var results = new List<ExportResult>();
            
            for (int i = 0; i < profiles.Count; i++)
            {
                var profile = profiles[i];
                
                Debug.Log($"[PackageBuilder] Exporting profile {i + 1}/{profiles.Count}: {profile.name}");
                
                var result = ExportPackage(profile, (progress, status) =>
                {
                    progressCallback?.Invoke(i, profiles.Count, progress, status);
                });
                
                results.Add(result);
                
                if (!result.success)
                {
                    Debug.LogError($"[PackageBuilder] Export failed for profile '{profile.name}': {result.errorMessage}");
                }
            }
            
            return results;
        }
    }
}

