using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.PackageExporter
{
    /// <summary>
    /// Scans export folders and collects all assets that will be included in the package.
    /// Provides detailed analysis for the Export Inspector UI.
    /// </summary>
    public static class AssetCollector
    {
        /// <summary>
        /// Scan all export folders and collect discovered assets with dependencies
        /// </summary>
        public static List<DiscoveredAsset> ScanExportFolders(ExportProfile profile, bool includeDependencies = true)
        {
            var discoveredAssets = new List<DiscoveredAsset>();
            var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            Debug.Log($"[AssetCollector] Starting scan of {profile.foldersToExport.Count} export folders");
            
            // First pass: collect all assets from export folders
            foreach (string folder in profile.foldersToExport)
            {
                if (string.IsNullOrWhiteSpace(folder))
                    continue;
                
                string fullPath = Path.GetFullPath(folder);
                
                if (!Directory.Exists(fullPath))
                {
                    Debug.LogWarning($"[AssetCollector] Folder does not exist: {fullPath}");
                    continue;
                }
                
                Debug.Log($"[AssetCollector] Scanning folder: {folder}");
                ScanFolder(fullPath, folder, profile, discoveredAssets, processedPaths);
            }
            
            // Second pass: collect dependencies if requested
            if (includeDependencies && profile.includeDependencies)
            {
                Debug.Log($"[AssetCollector] Collecting dependencies for {discoveredAssets.Count} assets");
                CollectDependencies(discoveredAssets, profile, processedPaths);
            }
            
            // Sort by path for better UI display
            discoveredAssets.Sort((a, b) => string.Compare(a.assetPath, b.assetPath, StringComparison.OrdinalIgnoreCase));
            
            Debug.Log($"[AssetCollector] Scan complete. Found {discoveredAssets.Count} total assets");
            
            return discoveredAssets;
        }
        
        /// <summary>
        /// Recursively scan a folder and add all assets
        /// </summary>
        private static void ScanFolder(string folderPath, string sourceFolder, ExportProfile profile, 
            List<DiscoveredAsset> discoveredAssets, HashSet<string> processedPaths)
        {
            // Check if this folder should be ignored
            string folderName = Path.GetFileName(folderPath);
            if (ShouldIgnoreFolder(folderPath, folderName, profile))
            {
                Debug.Log($"[AssetCollector] Ignoring folder: {folderPath}");
                return;
            }
            
            // Add the folder itself
            if (!processedPaths.Contains(folderPath))
            {
                processedPaths.Add(folderPath);
                discoveredAssets.Add(new DiscoveredAsset(folderPath, sourceFolder, isDir: true));
            }
            
            try
            {
                // Get all files in this folder
                string[] files = Directory.GetFiles(folderPath);
                foreach (string file in files)
                {
                    if (ShouldIgnoreFile(file, profile))
                        continue;
                    
                    if (!processedPaths.Contains(file))
                    {
                        processedPaths.Add(file);
                        discoveredAssets.Add(new DiscoveredAsset(file, sourceFolder, isDir: false));
                    }
                }
                
                // Recursively scan subfolders if enabled
                if (profile.recurseFolders)
                {
                    string[] subdirs = Directory.GetDirectories(folderPath);
                    foreach (string subdir in subdirs)
                    {
                        ScanFolder(subdir, sourceFolder, profile, discoveredAssets, processedPaths);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetCollector] Error scanning folder {folderPath}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Collect dependencies for all discovered assets
        /// </summary>
        private static void CollectDependencies(List<DiscoveredAsset> discoveredAssets, ExportProfile profile, 
            HashSet<string> processedPaths)
        {
            // Get list of direct assets (not folders, not already marked as dependencies)
            var directAssets = discoveredAssets
                .Where(a => !a.isFolder && !a.isDependency)
                .Select(a => a.assetPath)
                .ToList();
            
            if (directAssets.Count == 0)
                return;
            
            // Use Unity's AssetDatabase to get dependencies
            var allDependencies = new HashSet<string>();
            
            foreach (string assetPath in directAssets)
            {
                try
                {
                    // Convert to Unity-relative path (Assets/...)
                    string unityPath = GetUnityRelativePath(assetPath);
                    
                    if (string.IsNullOrEmpty(unityPath) || !AssetDatabase.IsValidFolder(Path.GetDirectoryName(unityPath)))
                        continue;
                    
                    // Get dependencies for this asset
                    string[] dependencies = AssetDatabase.GetDependencies(unityPath, recursive: false);
                    
                    foreach (string dep in dependencies)
                    {
                        // Skip self-reference
                        if (dep == unityPath)
                            continue;
                        
                        // Convert back to full path
                        string fullDepPath = Path.GetFullPath(dep);
                        
                        // Skip if already processed
                        if (processedPaths.Contains(fullDepPath))
                            continue;
                        
                        // Skip if in an ignored folder
                        if (ShouldIgnoreFile(fullDepPath, profile))
                            continue;
                        
                        allDependencies.Add(fullDepPath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AssetCollector] Error getting dependencies for {assetPath}: {ex.Message}");
                }
            }
            
            // Add dependencies to discovered assets
            foreach (string depPath in allDependencies)
            {
                if (!processedPaths.Contains(depPath))
                {
                    processedPaths.Add(depPath);
                    var depAsset = new DiscoveredAsset(depPath, "[Dependency]", isDir: false);
                    depAsset.isDependency = true;
                    discoveredAssets.Add(depAsset);
                }
            }
            
            Debug.Log($"[AssetCollector] Found {allDependencies.Count} dependencies");
        }
        
        /// <summary>
        /// Check if a folder should be ignored
        /// </summary>
        private static bool ShouldIgnoreFolder(string folderPath, string folderName, ExportProfile profile)
        {
            // Check .yucpignore files first (highest priority)
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (YucpIgnoreHandler.ShouldIgnore(folderPath, projectRoot))
                return true;
            
            // Check permanent ignore list
            if (profile.permanentIgnoreFolders != null)
            {
                foreach (string ignorePath in profile.permanentIgnoreFolders)
                {
                    string fullIgnorePath = Path.GetFullPath(ignorePath);
                    if (folderPath.StartsWith(fullIgnorePath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            
            // Check exclude folder names
            if (profile.excludeFolderNames != null && profile.excludeFolderNames.Contains(folderName))
                return true;
            
            return false;
        }
        
        /// <summary>
        /// Check if a file should be ignored
        /// </summary>
        private static bool ShouldIgnoreFile(string filePath, ExportProfile profile)
        {
            string fileName = Path.GetFileName(filePath);
            string extension = Path.GetExtension(filePath);
            
            // Check .yucpignore files first (highest priority)
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (YucpIgnoreHandler.ShouldIgnore(filePath, projectRoot))
                return true;
            
            // Check if file is in an ignored folder
            string folderPath = Path.GetDirectoryName(filePath);
            string folderName = Path.GetFileName(folderPath);
            if (ShouldIgnoreFolder(folderPath, folderName, profile))
                return true;
            
            // Check exclude file patterns
            if (profile.excludeFilePatterns != null)
            {
                foreach (string pattern in profile.excludeFilePatterns)
                {
                    if (IsFileMatchingPattern(fileName, pattern))
                        return true;
                }
            }
            
            // Always ignore .meta files (Unity handles these)
            if (extension == ".meta")
                return true;
            
            return false;
        }
        
        /// <summary>
        /// Check if a filename matches a pattern (supports * wildcards)
        /// </summary>
        private static bool IsFileMatchingPattern(string fileName, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;
            
            // Simple wildcard matching
            if (pattern.StartsWith("*"))
            {
                string suffix = pattern.Substring(1);
                return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
            }
            else if (pattern.EndsWith("*"))
            {
                string prefix = pattern.Substring(0, pattern.Length - 1);
                return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            else if (pattern.Contains("*"))
            {
                // More complex wildcard - use regex-like matching
                string[] parts = pattern.Split('*');
                int currentIndex = 0;
                
                foreach (string part in parts)
                {
                    int foundIndex = fileName.IndexOf(part, currentIndex, StringComparison.OrdinalIgnoreCase);
                    if (foundIndex < 0)
                        return false;
                    currentIndex = foundIndex + part.Length;
                }
                
                return true;
            }
            else
            {
                // Exact match
                return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
            }
        }
        
        /// <summary>
        /// Convert full path to Unity-relative path (Assets/... or Packages/...)
        /// </summary>
        private static string GetUnityRelativePath(string fullPath)
        {
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            fullPath = Path.GetFullPath(fullPath);
            
            if (fullPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = fullPath.Substring(projectPath.Length + 1);
                return relativePath.Replace("\\", "/");
            }
            
            return null;
        }
        
        /// <summary>
        /// Get summary statistics for discovered assets
        /// </summary>
        public static string GetAssetSummary(List<DiscoveredAsset> assets)
        {
            if (assets == null || assets.Count == 0)
                return "No assets discovered";
            
            int totalAssets = assets.Count;
            int folders = assets.Count(a => a.isFolder);
            int files = assets.Count(a => !a.isFolder);
            int dependencies = assets.Count(a => a.isDependency);
            int included = assets.Count(a => a.included);
            int excluded = totalAssets - included;
            long totalSize = assets.Where(a => !a.isFolder).Sum(a => a.fileSize);
            
            return $"Total: {totalAssets} ({files} files, {folders} folders)\n" +
                   $"Dependencies: {dependencies}\n" +
                   $"Included: {included} | Excluded: {excluded}\n" +
                   $"Total Size: {FormatBytes(totalSize)}";
        }
        
        /// <summary>
        /// Format bytes to human-readable size
        /// </summary>
        private static string FormatBytes(long bytes)
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

