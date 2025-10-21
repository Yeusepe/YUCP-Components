using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace YUCP.Components.Editor.PackageExporter
{
    /// <summary>
    /// Scans project folders for assembly definition files and their compiled DLLs.
    /// Maps .asmdef files to their corresponding DLLs in Library/ScriptAssemblies.
    /// </summary>
    public static class AssemblyScanner
    {
        public class AssemblyInfo
        {
            public string assemblyName;
            public string asmdefPath;
            public string dllPath;
            public bool exists;
            public long fileSize;
            
            public AssemblyInfo(string name, string asmdefPath)
            {
                this.assemblyName = name;
                this.asmdefPath = asmdefPath;
                this.dllPath = FindDllPath(name);
                this.exists = !string.IsNullOrEmpty(dllPath) && File.Exists(dllPath);
                this.fileSize = exists ? new FileInfo(dllPath).Length : 0;
            }
            
            private static string FindDllPath(string assemblyName)
            {
                // Unity compiles assemblies to Library/ScriptAssemblies/
                string libraryPath = Path.Combine(Application.dataPath, "..", "Library", "ScriptAssemblies");
                string dllName = assemblyName + ".dll";
                string fullPath = Path.Combine(libraryPath, dllName);
                
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
                
                return null;
            }
        }
        
        /// <summary>
        /// Scan specified folders for all .asmdef files
        /// </summary>
        public static List<AssemblyInfo> ScanFolders(List<string> folders)
        {
            var assemblies = new List<AssemblyInfo>();
            var foundAsmdefPaths = new HashSet<string>();
            
            foreach (string folder in folders)
            {
                if (!Directory.Exists(folder))
                {
                    Debug.LogWarning($"[AssemblyScanner] Folder does not exist: {folder}");
                    continue;
                }
                
                // Find all .asmdef files in this folder
                string[] asmdefFiles = Directory.GetFiles(folder, "*.asmdef", SearchOption.AllDirectories);
                
                foreach (string asmdefPath in asmdefFiles)
                {
                    if (foundAsmdefPaths.Contains(asmdefPath))
                        continue;
                    
                    foundAsmdefPaths.Add(asmdefPath);
                    
                    string assemblyName = ExtractAssemblyName(asmdefPath);
                    if (!string.IsNullOrEmpty(assemblyName))
                    {
                        var assemblyInfo = new AssemblyInfo(assemblyName, asmdefPath);
                        assemblies.Add(assemblyInfo);
                        
                        Debug.Log($"[AssemblyScanner] Found assembly: {assemblyName} (DLL exists: {assemblyInfo.exists})");
                    }
                }
            }
            
            return assemblies;
        }
        
        /// <summary>
        /// Extract assembly name from .asmdef JSON file
        /// </summary>
        private static string ExtractAssemblyName(string asmdefPath)
        {
            try
            {
                string json = File.ReadAllText(asmdefPath);
                var jsonObj = JObject.Parse(json);
                
                string name = jsonObj["name"]?.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
                
                Debug.LogWarning($"[AssemblyScanner] No 'name' field in {asmdefPath}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AssemblyScanner] Failed to parse {asmdefPath}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Get all assemblies in the project (not just from selected folders)
        /// </summary>
        public static List<AssemblyInfo> ScanAllAssemblies()
        {
            var assemblies = new List<AssemblyInfo>();
            
            // Scan entire project for .asmdef files
            string[] asmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
            
            foreach (string guid in asmdefGuids)
            {
                string asmdefPath = AssetDatabase.GUIDToAssetPath(guid);
                string fullPath = Path.GetFullPath(asmdefPath);
                
                string assemblyName = ExtractAssemblyName(fullPath);
                if (!string.IsNullOrEmpty(assemblyName))
                {
                    var assemblyInfo = new AssemblyInfo(assemblyName, fullPath);
                    assemblies.Add(assemblyInfo);
                }
            }
            
            return assemblies;
        }
        
        /// <summary>
        /// Scan VPM packages for assemblies based on enabled dependencies
        /// </summary>
        public static List<AssemblyInfo> ScanVpmPackages(List<PackageDependency> dependencies)
        {
            var assemblies = new List<AssemblyInfo>();
            
            // Look for VPM packages in the Packages folder
            string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
            if (!Directory.Exists(packagesPath))
                return assemblies;
            
            // Only scan packages that are enabled in dependencies
            foreach (var dependency in dependencies)
            {
                if (!dependency.enabled || string.IsNullOrEmpty(dependency.packageName))
                    continue;
                
                string packagePath = Path.Combine(packagesPath, dependency.packageName);
                if (!Directory.Exists(packagePath))
                    continue;
                
                var packageAssemblies = ScanFolders(new List<string> { packagePath });
                if (packageAssemblies.Count > 0)
                {
                    Debug.Log($"[AssemblyScanner] Found {packageAssemblies.Count} assemblies in dependency: {dependency.packageName}");
                    assemblies.AddRange(packageAssemblies);
                }
            }
            
            return assemblies;
        }
        
        /// <summary>
        /// Validate that all required DLLs exist for obfuscation
        /// </summary>
        public static bool ValidateAssemblies(List<AssemblyObfuscationSettings> settings, out List<string> missingAssemblies)
        {
            missingAssemblies = new List<string>();
            
            foreach (var setting in settings)
            {
                if (!setting.enabled)
                    continue;
                
                var assemblyInfo = new AssemblyInfo(setting.assemblyName, setting.asmdefPath);
                if (!assemblyInfo.exists)
                {
                    missingAssemblies.Add(setting.assemblyName);
                }
            }
            
            return missingAssemblies.Count == 0;
        }
        
        /// <summary>
        /// Get the size of an assembly in a human-readable format
        /// </summary>
        public static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
