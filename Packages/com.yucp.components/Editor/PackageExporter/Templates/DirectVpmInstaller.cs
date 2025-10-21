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
        private const string PACKAGE_JSON_PATH = "Assets/package.json";
        
        static DirectVpmInstaller()
        {
            EditorApplication.delayCall += CheckAndInstallVpmPackages;
        }
        
        private static void CheckAndInstallVpmPackages()
        {
            if (!File.Exists(PACKAGE_JSON_PATH))
                return;
            
            try
            {
                var packageInfo = JObject.Parse(File.ReadAllText(PACKAGE_JSON_PATH));
                var vpmDependencies = packageInfo["vpmDependencies"] as JObject;
                var vpmRepositories = packageInfo["vpmRepositories"] as JObject;
                
                if (vpmDependencies == null || vpmDependencies.Count == 0)
                    return;
                
                if (vpmRepositories == null || vpmRepositories.Count == 0)
                {
                    Debug.LogError("[DirectVpmInstaller] No VPM repositories found in package.json");
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
                    return;
                
                string packageList = string.Join("\n", packagesToInstall.Select(p => $"  - {p.Item1}@{p.Item2}"));
                bool install = EditorUtility.DisplayDialog(
                    "Install VPM Dependencies",
                    $"This package requires the following VPM dependencies:\n\n{packageList}\n\nWould you like to install them?",
                    "Install",
                    "Cancel"
                );
                
                if (!install)
                    return;
                
                foreach (var package in packagesToInstall)
                    InstallPackage(package.Item1, package.Item2, repositories);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DirectVpmInstaller] Error: {ex.Message}");
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
        
        private static void InstallPackage(string packageName, string versionRequirement, Dictionary<string, string> repositories)
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
                    Debug.LogError($"[DirectVpmInstaller] Package {packageName} not found");
                    return;
                }
                
                string tempZipPath = Path.Combine(Path.GetTempPath(), $"{packageName}.zip");
                string packageDestination = $"Packages/{packageName}";
                
                new WebClient().DownloadFile(downloadUrl, tempZipPath);
                
                if (Directory.Exists(packageDestination))
                    Directory.Delete(packageDestination, true);
                
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, packageDestination);
                File.Delete(tempZipPath);
                
                Debug.Log($"[DirectVpmInstaller] Installed {packageName}@{resolvedVersion}");
                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DirectVpmInstaller] Failed to install {packageName}: {ex.Message}");
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
