using System;
using System.IO;
using System.Net;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;
using Newtonsoft.Json.Linq;

namespace YUCP.Components.Editor.Utils
{
    /// <summary>
    /// Utility for installing Final IK Stub via VPM package manager.
    /// Downloads and installs the latest version from the VRLabs VPM repository.
    /// </summary>
    public static class FinalIKStubInstaller
    {
        private const string PACKAGE_NAME = "dev.vrlabs.final-ik-stub";
        private const string DISPLAY_NAME = "Final IK Stub";
        private const string REPOSITORY_URL = "https://api.vrlabs.dev/listings/ids/cwMA";
        private const string FALLBACK_VERSION = "1.9.3"; // Fallback if we can't fetch latest
        
        /// <summary>
        /// Install Final IK Stub by downloading and extracting the package directly
        /// </summary>
        public static void InstallFinalIKStub(Action onSuccess = null, Action<string> onError = null)
        {
            try
            {
                Debug.Log("[FinalIKStubInstaller] Starting installation...");
                
                // Lock assemblies during installation
                EditorApplication.LockReloadAssemblies();
                AssetDatabase.DisallowAutoRefresh();
                AssetDatabase.StartAssetEditing();
                
                try
                {
                    // Find package in repository and download
                    string downloadUrl = null;
                    string resolvedVersion = null;
                    
                    try
                    {
                        var repoData = JObject.Parse(new WebClient().DownloadString(REPOSITORY_URL));
                        var packages = repoData["packages"] as JObject;
                        
                        if (packages?[PACKAGE_NAME] != null)
                        {
                            var packageData = packages[PACKAGE_NAME] as JObject;
                            var versions = packageData["versions"] as JObject;
                            
                            if (versions != null)
                            {
                                // Find best version that satisfies requirement
                                string bestVersion = null;
                                string bestUrl = null;
                                
                                foreach (var versionEntry in versions.Properties())
                                {
                                    try
                                    {
                                        string version = versionEntry.Name;
                                        if (VersionSatisfiesRequirement(version, FALLBACK_VERSION))
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
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FinalIKStubInstaller] Failed to fetch from repository, using fallback: {ex.Message}");
                        // Will use fallback version
                    }
                    
                    // If we couldn't resolve from repo, try to construct URL from known structure
                    if (string.IsNullOrEmpty(downloadUrl))
                    {
                        resolvedVersion = FALLBACK_VERSION;
                        // Try to construct download URL (VRLabs packages typically follow this pattern)
                        downloadUrl = $"https://github.com/VRLabs/Final-IK-Stub/releases/download/{resolvedVersion}/{PACKAGE_NAME}-{resolvedVersion}.zip";
                    }
                    
                    if (string.IsNullOrEmpty(downloadUrl))
                    {
                        onError?.Invoke("Could not determine download URL for package");
                        return;
                    }
                    
                    // Download package
                    string tempZipPath = Path.Combine(Path.GetTempPath(), $"{PACKAGE_NAME}.zip");
                    string packageDestination = Path.Combine(Application.dataPath, "..", "Packages", PACKAGE_NAME);
                    
                    Debug.Log($"[FinalIKStubInstaller] Downloading {PACKAGE_NAME}@{resolvedVersion} from {downloadUrl}");
                    new WebClient().DownloadFile(downloadUrl, tempZipPath);
                    
                    // Extract package
                    if (Directory.Exists(packageDestination))
                        Directory.Delete(packageDestination, true);
                    
                    System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, packageDestination);
                    File.Delete(tempZipPath);
                    
                    // Add to VPM manifest
                    AddToVpmManifest(PACKAGE_NAME, resolvedVersion);
                    
                    Debug.Log($"[FinalIKStubInstaller] Installed {PACKAGE_NAME}@{resolvedVersion}");
                }
                finally
                {
                    // Unlock everything
                    AssetDatabase.StopAssetEditing();
                    AssetDatabase.AllowAutoRefresh();
                    EditorApplication.UnlockReloadAssemblies();
                }
                
                // Trigger refresh and domain reload
                AssetDatabase.Refresh();
                
                // Trigger domain reload to compile the new package
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        CompilationPipeline.RequestScriptCompilation();
                        EditorUtility.RequestScriptReload();
                        Debug.Log("[FinalIKStubInstaller] Triggered domain reload to compile package");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FinalIKStubInstaller] Failed to trigger domain reload: {ex.Message}");
                    }
                };
                
                onSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FinalIKStubInstaller] Installation failed: {ex.Message}");
                Debug.LogException(ex);
                onError?.Invoke(ex.Message);
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
                    manifest = new JObject
                    {
                        ["dependencies"] = new JObject(),
                        ["locked"] = new JObject()
                    };
                }
                
                // Add to dependencies
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
                
                // Add to locked
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
                
                Debug.Log($"[FinalIKStubInstaller] Added {packageName}@{version} to vpm-manifest.json");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FinalIKStubInstaller] Failed to update vpm-manifest.json: {ex.Message}");
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
        
        /// <summary>
        /// Check if Final IK Stub is currently installed
        /// Only checks for the actual package directory - type reflection can give false positives
        /// if the actual Final IK package is installed instead of the stub.
        /// </summary>
        public static bool IsInstalled()
        {
            // Primary check: Verify the package directory exists
            string packagePath = Path.Combine(Application.dataPath, "..", "Packages", PACKAGE_NAME);
            if (!Directory.Exists(packagePath))
            {
                return false;
            }
            
            // Secondary check: Verify package.json exists to confirm it's a valid package
            string packageJsonPath = Path.Combine(packagePath, "package.json");
            if (!File.Exists(packageJsonPath))
            {
                return false;
            }
            
            // Optional: Verify the package.json contains the correct package name
            try
            {
                string packageJsonContent = File.ReadAllText(packageJsonPath);
                if (packageJsonContent.Contains(PACKAGE_NAME))
                {
                    return true;
                }
            }
            catch
            {
                // If we can't read the file, but directory and package.json exist, assume it's installed
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Alternative installation via direct GitHub download (fallback)
        /// </summary>
        public static void OpenGitHubReleasePage()
        {
            Application.OpenURL("https://github.com/VRLabs/Final-IK-Stub/releases/latest");
        }
    }
}
