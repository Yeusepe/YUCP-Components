using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace YUCP.Components.Editor.PackageExporter
{
    /// <summary>
    /// Scans the Unity project for installed packages and their versions.
    /// Detects both standard Unity packages and VRChat VPM packages.
    /// </summary>
    public static class DependencyScanner
    {
        public class PackageInfo
        {
            public string packageName;
            public string version;
            public string displayName;
            public string description;
            public bool isVpmPackage;
            public string packagePath;
            
            public PackageInfo(string name, string version, string displayName, string desc, bool isVpm, string path)
            {
                this.packageName = name;
                this.version = version;
                this.displayName = displayName;
                this.description = desc;
                this.isVpmPackage = isVpm;
                this.packagePath = path;
            }
        }
        
        /// <summary>
        /// Scan all installed packages in the project
        /// </summary>
        public static List<PackageInfo> ScanInstalledPackages()
        {
            var packages = new List<PackageInfo>();
            
            // Scan Packages folder for package.json files
            string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
            
            if (!Directory.Exists(packagesPath))
            {
                Debug.LogWarning("[DependencyScanner] Packages directory not found");
                return packages;
            }
            
            // Get all subdirectories in Packages
            string[] packageDirs = Directory.GetDirectories(packagesPath);
            
            foreach (string packageDir in packageDirs)
            {
                string packageJsonPath = Path.Combine(packageDir, "package.json");
                
                if (!File.Exists(packageJsonPath))
                    continue;
                
                try
                {
                    var packageInfo = ParsePackageJson(packageJsonPath);
                    if (packageInfo != null)
                    {
                        packages.Add(packageInfo);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DependencyScanner] Failed to parse {packageJsonPath}: {ex.Message}");
                }
            }
            
            // Also check VPM manifest for VRChat-specific packages
            string vpmManifestPath = Path.Combine(packagesPath, "vpm-manifest.json");
            if (File.Exists(vpmManifestPath))
            {
                try
                {
                    var vpmPackages = ParseVpmManifest(vpmManifestPath);
                    
                    // Mark VPM packages
                    foreach (var pkg in vpmPackages)
                    {
                        var existing = packages.Find(p => p.packageName == pkg.packageName);
                        if (existing != null)
                        {
                            existing.isVpmPackage = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DependencyScanner] Failed to parse VPM manifest: {ex.Message}");
                }
            }
            
            Debug.Log($"[DependencyScanner] Found {packages.Count} installed packages");
            
            return packages;
        }
        
        /// <summary>
        /// Parse a package.json file to extract package information
        /// </summary>
        private static PackageInfo ParsePackageJson(string packageJsonPath)
        {
            string json = File.ReadAllText(packageJsonPath);
            var jsonObj = JObject.Parse(json);
            
            string packageName = jsonObj["name"]?.ToString();
            string version = jsonObj["version"]?.ToString();
            string displayName = jsonObj["displayName"]?.ToString() ?? packageName;
            string description = jsonObj["description"]?.ToString() ?? "";
            
            if (string.IsNullOrEmpty(packageName))
                return null;
            
            string packagePath = Path.GetDirectoryName(packageJsonPath);
            
            return new PackageInfo(packageName, version, displayName, description, false, packagePath);
        }
        
        /// <summary>
        /// Parse VPM manifest to identify VRChat packages
        /// </summary>
        private static List<PackageInfo> ParseVpmManifest(string vpmManifestPath)
        {
            var packages = new List<PackageInfo>();
            
            try
            {
                string json = File.ReadAllText(vpmManifestPath);
                var jsonObj = JObject.Parse(json);
                
                var locked = jsonObj["locked"];
                if (locked != null)
                {
                    foreach (var prop in locked.Children<JProperty>())
                    {
                        string packageName = prop.Name;
                        var versionObj = prop.Value["version"];
                        string version = versionObj?.ToString() ?? "unknown";
                        
                        packages.Add(new PackageInfo(packageName, version, packageName, "", true, ""));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DependencyScanner] Failed to parse VPM manifest: {ex.Message}");
            }
            
            return packages;
        }
        
        /// <summary>
        /// Convert PackageInfo list to PackageDependency list for export profile
        /// </summary>
        public static List<PackageDependency> ConvertToPackageDependencies(List<PackageInfo> packages)
        {
            var dependencies = new List<PackageDependency>();
            
            foreach (var pkg in packages)
            {
                // Skip Unity's built-in packages
                if (pkg.packageName.StartsWith("com.unity."))
                    continue;
                
                // Skip the current package if it's YUCP components
                if (pkg.packageName == "com.yucp.components")
                    continue;
                
                var dependency = new PackageDependency(
                    pkg.packageName,
                    pkg.version,
                    pkg.displayName,
                    pkg.isVpmPackage
                );
                
                dependencies.Add(dependency);
            }
            
            return dependencies;
        }
        
        /// <summary>
        /// Scan export folders for used scripts/components and detect which packages they belong to.
        /// Automatically enables dependencies that are actively used in the export.
        /// </summary>
        public static void AutoDetectUsedDependencies(ExportProfile profile)
        {
            Debug.Log("[DependencyScanner] Auto-detecting used dependencies...");
            
            if (profile.foldersToExport.Count == 0)
            {
                Debug.LogWarning("[DependencyScanner] No export folders configured");
                return;
            }
            
            var installedPackages = ScanInstalledPackages();
            var usedPackages = new HashSet<string>();
            
            // Scan all prefabs and scenes in export folders for components
            foreach (string folder in profile.foldersToExport)
            {
                if (!Directory.Exists(folder))
                    continue;
                
                // Find all prefabs and scenes
                string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
                string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { folder });
                
                var allGuids = prefabGuids.Concat(sceneGuids).ToArray();
                
                foreach (string guid in allGuids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    var usedInAsset = DetectPackagesInAsset(assetPath, installedPackages);
                    
                    foreach (var packageName in usedInAsset)
                    {
                        usedPackages.Add(packageName);
                    }
                }
            }
            
            if (usedPackages.Count > 0)
            {
                Debug.Log($"[DependencyScanner] Auto-detected {usedPackages.Count} used packages: {string.Join(", ", usedPackages)}");
                
                // Enable dependencies that are used
                foreach (var dep in profile.dependencies)
                {
                    if (usedPackages.Contains(dep.packageName))
                    {
                        dep.enabled = true;
                        
                        // Default to Dependency mode for known packages
                        if (dep.isVpmDependency || dep.packageName.Contains("vrcfury"))
                        {
                            dep.exportMode = DependencyExportMode.Dependency;
                        }
                        
                        Debug.Log($"[DependencyScanner] Auto-enabled dependency: {dep.packageName}");
                    }
                    else
                    {
                        // Disable unused dependencies by default
                        dep.enabled = false;
                    }
                }
            }
        }
        
        /// <summary>
        /// Detect which packages are used in a specific asset (prefab or scene)
        /// </summary>
        private static List<string> DetectPackagesInAsset(string assetPath, List<PackageInfo> installedPackages)
        {
            var usedPackages = new List<string>();
            
            try
            {
                // Read the asset file as text to look for script references
                string assetContent = File.ReadAllText(assetPath);
                
                // Look for MonoScript GUIDs in the file
                var guidMatches = System.Text.RegularExpressions.Regex.Matches(assetContent, @"guid:\s*([a-f0-9]{32})");
                
                foreach (System.Text.RegularExpressions.Match match in guidMatches)
                {
                    string guid = match.Groups[1].Value;
                    string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                    
                    if (string.IsNullOrEmpty(scriptPath))
                        continue;
                    
                    // Check which package this script belongs to
                    foreach (var pkg in installedPackages)
                    {
                        if (!string.IsNullOrEmpty(pkg.packagePath) && scriptPath.StartsWith(GetRelativePackagePath(pkg.packagePath)))
                        {
                            if (!usedPackages.Contains(pkg.packageName))
                            {
                                usedPackages.Add(pkg.packageName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DependencyScanner] Failed to scan asset {assetPath}: {ex.Message}");
            }
            
            return usedPackages;
        }
        
        /// <summary>
        /// Convert absolute path to Unity-relative path
        /// </summary>
        private static string GetRelativePackagePath(string absolutePath)
        {
            string projectPath = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            
            if (absolutePath.StartsWith(projectPath))
            {
                string relative = absolutePath.Substring(projectPath.Length);
                if (relative.StartsWith("\\") || relative.StartsWith("/"))
                {
                    relative = relative.Substring(1);
                }
                return relative.Replace('\\', '/');
            }
            
            return absolutePath.Replace('\\', '/');
        }
        
        /// <summary>
        /// Generate package.json content with dependencies
        /// </summary>
        public static string GeneratePackageJson(
            ExportProfile profile,
            List<PackageDependency> dependencies,
            string existingPackageJsonPath = null)
        {
            JObject packageJson;
            
            // Load existing package.json if provided
            if (!string.IsNullOrEmpty(existingPackageJsonPath) && File.Exists(existingPackageJsonPath))
            {
                string existingJson = File.ReadAllText(existingPackageJsonPath);
                packageJson = JObject.Parse(existingJson);
            }
            else
            {
                // Create new package.json
                packageJson = new JObject();
            }
            
            // Set basic fields
            packageJson["name"] = profile.packageName.ToLower().Replace(" ", ".");
            packageJson["displayName"] = profile.packageName;
            packageJson["version"] = profile.version;
            
            if (!string.IsNullOrEmpty(profile.description))
            {
                packageJson["description"] = profile.description;
            }
            
            if (!string.IsNullOrEmpty(profile.author))
            {
                var authorObj = new JObject();
                authorObj["name"] = profile.author;
                packageJson["author"] = authorObj;
            }
            
            // Add unity version requirement
            packageJson["unity"] = "2022.3";
            
            // Build dependencies
            var depsToReference = dependencies.Where(d => d.enabled && d.exportMode == DependencyExportMode.Dependency).ToList();
            
            if (depsToReference.Count > 0)
            {
                // Separate VPM and regular dependencies
                var vpmDeps = depsToReference.Where(d => d.isVpmDependency).ToList();
                var regularDeps = depsToReference.Where(d => !d.isVpmDependency).ToList();
                
                // Add regular dependencies
                if (regularDeps.Count > 0)
                {
                    var depsObj = new JObject();
                    foreach (var dep in regularDeps)
                    {
                        depsObj[dep.packageName] = dep.packageVersion;
                    }
                    packageJson["dependencies"] = depsObj;
                }
                
                // Add VPM dependencies (VRChat packages)
                if (vpmDeps.Count > 0)
                {
                    var vpmDepsObj = new JObject();
                    foreach (var dep in vpmDeps)
                    {
                        // Use >= for VPM dependencies to allow updates
                        vpmDepsObj[dep.packageName] = $">={dep.packageVersion}";
                    }
                    packageJson["vpmDependencies"] = vpmDepsObj;
                    
                    // Only add repositories for the VPM dependencies we actually need
                    var vpmRepositories = GetRequiredRepositories(vpmDeps);
                    if (vpmRepositories.Count > 0)
                    {
                        var vpmReposObj = new JObject();
                        foreach (var repo in vpmRepositories)
                        {
                            vpmReposObj[repo.Key] = repo.Value;
                        }
                        packageJson["vpmRepositories"] = vpmReposObj;
                    }
                }
            }
            
            // Return formatted JSON
            return packageJson.ToString(Newtonsoft.Json.Formatting.Indented);
        }
        
        private static Dictionary<string, string> GetRequiredRepositories(List<PackageDependency> vpmDeps)
        {
            var repositories = new Dictionary<string, string>();
            
            try
            {
                string vccSettingsPath = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "VRChatCreatorCompanion",
                    "settings.json"
                );
                
                if (!File.Exists(vccSettingsPath))
                    return repositories;
                
                var settings = JObject.Parse(File.ReadAllText(vccSettingsPath));
                var userRepos = settings["userRepos"] as JArray;
                
                if (userRepos == null || userRepos.Count == 0)
                    return repositories;
                
                string vpmManifestPath = Path.Combine(UnityEngine.Application.dataPath, "..", "Packages", "vpm-manifest.json");
                if (!File.Exists(vpmManifestPath))
                    return repositories;
                
                var manifest = JObject.Parse(File.ReadAllText(vpmManifestPath));
                var locked = manifest["locked"] as JObject;
                
                if (locked == null || locked.Count == 0)
                    return repositories;
                
                var vccRepositories = new Dictionary<string, string>();
                
                foreach (var repoObj in userRepos)
                {
                    var repo = repoObj as JObject;
                    if (repo == null) continue;
                    
                    string repoUrl = repo["url"]?.ToString();
                    string repoName = repo["name"]?.ToString();
                    string repoId = repo["id"]?.ToString();
                    
                    if (string.IsNullOrEmpty(repoUrl)) continue;
                    
                    string key = !string.IsNullOrEmpty(repoName) ? repoName : repoId;
                    if (!string.IsNullOrEmpty(key))
                        vccRepositories[key] = repoUrl;
                }
                
                vccRepositories["VRChat Official"] = "https://packages.vrchat.com/official?download";
                vccRepositories["VRChat Curated"] = "https://packages.vrchat.com/curated?download";
                
                foreach (var dep in vpmDeps)
                {
                    string packageName = dep.packageName;
                    
                    if (locked[packageName] == null)
                        continue;
                    
                    foreach (var repo in vccRepositories)
                    {
                        try
                        {
                            using (var client = new System.Net.WebClient())
                            {
                                string repoData = client.DownloadString(repo.Value);
                                var repoJson = JObject.Parse(repoData);
                                var packages = repoJson["packages"] as JObject;
                                
                                if (packages != null && packages[packageName] != null)
                                {
                                    if (!repositories.ContainsKey(repo.Key))
                                        repositories[repo.Key] = repo.Value;
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DependencyScanner] Failed to get required repositories: {ex.Message}");
            }
            
            return repositories;
        }
    }
}
