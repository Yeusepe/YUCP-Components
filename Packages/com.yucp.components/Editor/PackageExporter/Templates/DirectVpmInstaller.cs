using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;

namespace YUCP.DirectVpmInstaller
{
    [InitializeOnLoad]
    public static class DirectVpmInstaller
    {
        
        static DirectVpmInstaller()
        {
            EditorApplication.delayCall += CheckAndInstallVpmPackages;
        }
        
        private static void CheckAndInstallVpmPackages()
        {
            // Find any YUCP temp install JSON files
            string[] tempJsonFiles = Directory.GetFiles(Application.dataPath, "YUCP_TempInstall_*.json", SearchOption.TopDirectoryOnly);
            
            if (tempJsonFiles.Length == 0)
                return;
            
            string packageJsonPath = tempJsonFiles[0]; // Use the first one found
            
            try
            {
                var packageInfo = JObject.Parse(File.ReadAllText(packageJsonPath));
                string bundledPackageName = packageInfo["name"]?.Value<string>();
                string bundledPackageVersion = packageInfo["version"]?.Value<string>();
                
                // Check if this bundled package is already installed
                if (!string.IsNullOrEmpty(bundledPackageName))
                {
                    string existingPackagePath = Path.Combine(Application.dataPath, "..", "Packages", bundledPackageName);
                    if (Directory.Exists(existingPackagePath))
                    {
                        string existingPackageJson = Path.Combine(existingPackagePath, "package.json");
                        if (File.Exists(existingPackageJson))
                        {
                            try
                            {
                                var existingData = JObject.Parse(File.ReadAllText(existingPackageJson));
                                string existingVersion = existingData["version"]?.Value<string>();
                                
                                if (!string.IsNullOrEmpty(existingVersion) && !string.IsNullOrEmpty(bundledPackageVersion))
                                {
                                    if (CompareVersions(existingVersion, bundledPackageVersion) >= 0)
                                    {
                                        Debug.Log($"[DirectVpmInstaller] Bundled package {bundledPackageName}@{bundledPackageVersion} is already installed (current: {existingVersion}). Skipping extraction.");
                                        
                                        // Still enable any .yucp_disabled files (might be from previous failed install)
                                        EnableBundledPackages();
                                        CleanupTemporaryFiles(packageJsonPath);
                                        return;
                                    }
                                    else
                                    {
                                        Debug.Log($"[DirectVpmInstaller] Upgrading bundled package {bundledPackageName} from {existingVersion} to {bundledPackageVersion}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[DirectVpmInstaller] Failed to check existing package version: {ex.Message}");
                            }
                        }
                    }
                }
                
                var vpmDependencies = packageInfo["vpmDependencies"] as JObject;
                var vpmRepositories = packageInfo["vpmRepositories"] as JObject;
                
                if (vpmDependencies == null || vpmDependencies.Count == 0)
                {
                    CleanupTemporaryFiles(packageJsonPath);
                    return;
                }
                
                if (vpmRepositories == null || vpmRepositories.Count == 0)
                {
                    Debug.LogError("[DirectVpmInstaller] No VPM repositories found in package.json");
                    CleanupTemporaryFiles(packageJsonPath);
                    return;
                }
                
                var repositories = vpmRepositories.Properties().ToDictionary(p => p.Name, p => p.Value.ToString());
                var packagesToInstall = new List<Tuple<string, string>>();
                
                foreach (var dep in vpmDependencies.Properties())
                {
                    string packageName = dep.Name;
                    string versionRequirement = dep.Value.ToString();
                    
                    if (!IsPackageInstalled(packageName, versionRequirement))
                        packagesToInstall.Add(new Tuple<string, string>(packageName, versionRequirement));
                }
                
                if (packagesToInstall.Count == 0)
                {
                    CleanupTemporaryFiles(packageJsonPath);
                    return;
                }
                
                string packageList = string.Join("\n", packagesToInstall.Select(p => $"  - {p.Item1}@{p.Item2}"));
                bool install = EditorUtility.DisplayDialog(
                    "Install VPM Dependencies",
                    $"This package requires the following VPM dependencies:\n\n{packageList}\n\nWould you like to install them now?\n\n(Required for compilation)",
                    "Install",
                    "Cancel"
                );
                
                if (!install)
                {
                    Debug.LogWarning("[DirectVpmInstaller] Dependencies not installed. Compilation errors may occur.");
                    CleanupTemporaryFiles(packageJsonPath);
                    return;
                }
                
                // Lock assemblies and disable auto-refresh to prevent mid-install compilation
                EditorApplication.LockReloadAssemblies();
                AssetDatabase.DisallowAutoRefresh();
                AssetDatabase.StartAssetEditing();
                
                try
                {
                    Debug.Log("[DirectVpmInstaller] Installing dependencies with compilation locked...");
                    
                    // Install all packages
                    bool allSucceeded = true;
                    foreach (var package in packagesToInstall)
                    {
                        if (!InstallPackage(package.Item1, package.Item2, repositories))
                            allSucceeded = false;
                    }
                    
                    if (allSucceeded)
                    {
                        Debug.Log("[DirectVpmInstaller] Dependencies installed. Enabling bundled packages...");
                        
                        // Enable bundled packages while still locked
                        EnableBundledPackages();
                        
                        // Clean up temporary files
                        CleanupTemporaryFiles(packageJsonPath);
                    }
                    else
                    {
                        CleanupTemporaryFiles(packageJsonPath);
                    }
                }
                finally
                {
                    // Unlock everything in one atomic operation
                    AssetDatabase.StopAssetEditing();
                    AssetDatabase.AllowAutoRefresh();
                    EditorApplication.UnlockReloadAssemblies();
                    
                    Debug.Log("[DirectVpmInstaller] Unlocked. Triggering full domain reload...");
                    
                    // Force a focus-grade full domain reload (includes UPM resolve, compile, and reload)
                    FullDomainReload.Run(() =>
                    {
                        Debug.Log("[DirectVpmInstaller] Installation complete. Domain fully reloaded with all dependencies functional.");
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DirectVpmInstaller] Error: {ex.Message}");
            }
        }
        
        private static void EnableBundledPackages()
        {
            try
            {
                string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                if (!Directory.Exists(packagesPath))
                    return;
                
                // Find all .yucp_disabled files in Packages folder
                string[] disabledFiles = Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
                int enabledCount = 0;
                
                foreach (string disabledFile in disabledFiles)
                {
                    // Remove .yucp_disabled extension to enable the file
                    string enabledFile = disabledFile.Substring(0, disabledFile.Length - ".yucp_disabled".Length);
                    string disabledMeta = disabledFile + ".meta";
                    string enabledMeta = enabledFile + ".meta";
                    
                    // Check if target already exists and compare timestamps
                    bool shouldReplace = true;
                    if (File.Exists(enabledFile))
                    {
                        DateTime disabledTime = File.GetLastWriteTimeUtc(disabledFile);
                        DateTime enabledTime = File.GetLastWriteTimeUtc(enabledFile);
                        
                        if (disabledTime <= enabledTime)
                        {
                            // Existing file is newer or same, skip replacement
                            Debug.Log($"[DirectVpmInstaller] Skipping '{Path.GetFileName(enabledFile)}' (existing file is same or newer)");
                            shouldReplace = false;
                        }
                    }
                    
                    if (shouldReplace)
                    {
                        // Delete existing files if they exist (File.Move can't overwrite)
                        if (File.Exists(enabledFile))
                            File.Delete(enabledFile);
                        if (File.Exists(enabledMeta))
                            File.Delete(enabledMeta);
                        
                        // Move the file
                        File.Move(disabledFile, enabledFile);
                        
                        // Move the .meta file if it exists
                        if (File.Exists(disabledMeta))
                        {
                            File.Move(disabledMeta, enabledMeta);
                        }
                        
                        enabledCount++;
                    }
                }
                
                if (enabledCount > 0)
                {
                    Debug.Log($"[DirectVpmInstaller] Enabled {enabledCount} bundled package files");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] Failed to enable bundled packages: {ex.Message}");
            }
        }
        
        private static void CleanupTemporaryFiles(string packageJsonPath)
        {
            try
            {
                // Delete the temporary package.json
                if (File.Exists(packageJsonPath))
                {
                    File.Delete(packageJsonPath);
                    string metaPath = packageJsonPath + ".meta";
                    if (File.Exists(metaPath))
                        File.Delete(metaPath);
                    
                    Debug.Log($"[DirectVpmInstaller] Cleaned up temporary file: {packageJsonPath}");
                }
                
                // Find and delete the installer script itself
                string editorPath = Path.Combine(Application.dataPath, "Editor");
                if (Directory.Exists(editorPath))
                {
                    string[] installerScripts = Directory.GetFiles(editorPath, "YUCP_Installer_*.cs", SearchOption.TopDirectoryOnly);
                    foreach (string installerPath in installerScripts)
                    {
                        File.Delete(installerPath);
                        string metaPath = installerPath + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                        
                        Debug.Log($"[DirectVpmInstaller] Deleted installer script: {installerPath}");
                    }
                    
                    // Also delete the .asmdef
                    string[] asmdefFiles = Directory.GetFiles(editorPath, "YUCP_Installer_*.asmdef", SearchOption.TopDirectoryOnly);
                    foreach (string asmdefPath in asmdefFiles)
                    {
                        File.Delete(asmdefPath);
                        string metaPath = asmdefPath + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                        
                        Debug.Log($"[DirectVpmInstaller] Deleted installer asmdef: {asmdefPath}");
                    }
                    
                    // Also delete the FullDomainReload helper script
                    string[] reloadScripts = Directory.GetFiles(editorPath, "YUCP_FullDomainReload_*.cs", SearchOption.TopDirectoryOnly);
                    foreach (string reloadPath in reloadScripts)
                    {
                        File.Delete(reloadPath);
                        string metaPath = reloadPath + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                        
                        Debug.Log($"[DirectVpmInstaller] Deleted FullDomainReload script: {reloadPath}");
                    }
                }
                
                // Delete all .yucp_disabled files from bundled packages (orphaned after enabling)
                string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                if (Directory.Exists(packagesPath))
                {
                    string[] disabledFiles = Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
                    int deletedCount = 0;
                    
                    foreach (string disabledFile in disabledFiles)
                    {
                        File.Delete(disabledFile);
                        string metaPath = disabledFile + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                        
                        deletedCount++;
                    }
                    
                    if (deletedCount > 0)
                    {
                        Debug.Log($"[DirectVpmInstaller] Deleted {deletedCount} orphaned .yucp_disabled files");
                    }
                }
                
                // Refresh AssetDatabase to remove deleted files from Unity
                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] Failed to cleanup temporary files: {ex.Message}");
            }
        }
        
        private static bool IsPackageInstalled(string packageName, string versionRequirement)
        {
            string packageJsonPath = $"Packages/{packageName}/package.json";
            if (!File.Exists(packageJsonPath))
                return false;
            
            try
            {
                var packageData = JObject.Parse(File.ReadAllText(packageJsonPath));
                string installedVersion = packageData["version"]?.ToString();
                return !string.IsNullOrEmpty(installedVersion) && VersionSatisfiesRequirement(installedVersion, versionRequirement);
            }
            catch
            {
                return false;
            }
        }
        
        private static bool InstallPackage(string packageName, string versionRequirement, Dictionary<string, string> repositories)
        {
            try
            {
                string downloadUrl = null;
                string resolvedVersion = null;
                
                foreach (var repo in repositories)
                {
                    try
                    {
                        var repoData = JObject.Parse(new WebClient().DownloadString(repo.Value));
                        var packages = repoData["packages"] as JObject;
                        
                        if (packages?[packageName] == null)
                            continue;
                        
                        var packageData = packages[packageName] as JObject;
                        var versions = packageData["versions"] as JObject;
                        
                        if (versions == null)
                            continue;
                        
                        string bestVersion = null;
                        string bestUrl = null;
                        
                        foreach (var versionEntry in versions.Properties())
                        {
                            try
                            {
                                string version = versionEntry.Name;
                                if (VersionSatisfiesRequirement(version, versionRequirement))
                                {
                                    if (bestVersion == null || CompareVersions(version, bestVersion) > 0)
                                    {
                                        bestVersion = version;
                                        bestUrl = (versionEntry.Value as JObject)?["url"]?.ToString();
                                    }
                                }
                            }
                            catch { }
                        }
                        
                        if (bestVersion != null && bestUrl != null)
                        {
                            downloadUrl = bestUrl;
                            resolvedVersion = bestVersion;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[DirectVpmInstaller] Failed to check repository {repo.Key}: {ex.Message}");
                    }
                }
                
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    Debug.LogError($"[DirectVpmInstaller] Package {packageName} not found in any repository");
                    return false;
                }
                
                string tempZipPath = Path.Combine(Path.GetTempPath(), $"{packageName}.zip");
                string packageDestination = $"Packages/{packageName}";
                
                new WebClient().DownloadFile(downloadUrl, tempZipPath);
                
                if (Directory.Exists(packageDestination))
                    Directory.Delete(packageDestination, true);
                
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, packageDestination);
                File.Delete(tempZipPath);
                
                // Add to VPM manifest so VCC recognizes it as installed
                AddToVpmManifest(packageName, resolvedVersion);
                
                Debug.Log($"[DirectVpmInstaller] Installed {packageName}@{resolvedVersion}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DirectVpmInstaller] Failed to install {packageName}: {ex.Message}");
                return false;
            }
        }
        
        private static void AddToVpmManifest(string packageName, string version)
        {
            try
            {
                string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "vpm-manifest.json");
                JObject manifest;
                
                if (File.Exists(manifestPath))
                {
                    manifest = JObject.Parse(File.ReadAllText(manifestPath));
                }
                else
                {
                    // Create new manifest if it doesn't exist
                    manifest = new JObject();
                    manifest["dependencies"] = new JObject();
                    manifest["locked"] = new JObject();
                }
                
                // Add to dependencies section
                var dependencies = manifest["dependencies"] as JObject;
                if (dependencies == null)
                {
                    dependencies = new JObject();
                    manifest["dependencies"] = dependencies;
                }
                dependencies[packageName] = new JObject
                {
                    ["version"] = version
                };
                
                // Add to locked section
                var locked = manifest["locked"] as JObject;
                if (locked == null)
                {
                    locked = new JObject();
                    manifest["locked"] = locked;
                }
                locked[packageName] = new JObject
                {
                    ["version"] = version
                };
                
                // Save manifest
                File.WriteAllText(manifestPath, manifest.ToString(Newtonsoft.Json.Formatting.Indented));
                Debug.Log($"[DirectVpmInstaller] Added {packageName}@{version} to vpm-manifest.json");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectVpmInstaller] Failed to update vpm-manifest.json: {ex.Message}");
            }
        }
        
        private static bool VersionSatisfiesRequirement(string installedVersion, string requirement)
        {
            requirement = requirement.Trim();
            
            if (requirement.StartsWith(">="))
            {
                string minVersion = requirement.Substring(2).Trim();
                return CompareVersions(installedVersion, minVersion) >= 0;
            }
            
            if (requirement.StartsWith("^"))
            {
                string baseVersion = requirement.Substring(1).Trim();
                var baseParts = ParseVersion(baseVersion);
                var installedParts = ParseVersion(installedVersion);
                
                if (baseParts.major != installedParts.major)
                    return false;
                
                return CompareVersions(installedVersion, baseVersion) >= 0;
            }
            
            if (requirement.StartsWith("~"))
            {
                string baseVersion = requirement.Substring(1).Trim();
                var baseParts = ParseVersion(baseVersion);
                var installedParts = ParseVersion(installedVersion);
                
                if (baseParts.major != installedParts.major || baseParts.minor != installedParts.minor)
                    return false;
                
                return CompareVersions(installedVersion, baseVersion) >= 0;
            }
            
            return CompareVersions(installedVersion, requirement) >= 0;
        }
        
        private static int CompareVersions(string version1, string version2)
        {
            var v1 = ParseVersion(version1);
            var v2 = ParseVersion(version2);
            
            if (v1.major != v2.major) return v1.major.CompareTo(v2.major);
            if (v1.minor != v2.minor) return v1.minor.CompareTo(v2.minor);
            return v1.patch.CompareTo(v2.patch);
        }
        
        private static (int major, int minor, int patch) ParseVersion(string version)
        {
            version = version.Trim().TrimStart('v', 'V');
            int dashIndex = version.IndexOf('-');
            if (dashIndex > 0)
                version = version.Substring(0, dashIndex);
            
            var parts = version.Split('.');
            int major = parts.Length > 0 ? int.Parse(parts[0]) : 0;
            int minor = parts.Length > 1 ? int.Parse(parts[1]) : 0;
            int patch = parts.Length > 2 ? int.Parse(parts[2]) : 0;
            
            return (major, minor, patch);
        }
        
        [MenuItem("Tools/YUCP/Manual - Install VPM Dependencies")]
        public static void ManualInstallVpmDependencies()
        {
            CheckAndInstallVpmPackages();
        }
    }
}
