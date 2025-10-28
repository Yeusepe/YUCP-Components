using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace YUCP.Components.Editor.PackageExporter
{
    /// <summary>
    /// Handles .yucpignore files for automatic folder/file exclusion from exports.
    /// Similar to .gitignore but for YUCP package exports.
    /// </summary>
    public static class YucpIgnoreHandler
    {
        private const string IGNORE_FILENAME = ".yucpignore";
        
        /// <summary>
        /// Cache of ignore patterns per directory
        /// </summary>
        private static Dictionary<string, List<string>> ignoreCache = new Dictionary<string, List<string>>();
        
        /// <summary>
        /// Check if a file or folder should be ignored based on .yucpignore files
        /// </summary>
        public static bool ShouldIgnore(string path, string projectRoot)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            
            path = Path.GetFullPath(path).Replace("\\", "/");
            projectRoot = Path.GetFullPath(projectRoot).Replace("\\", "/");
            
            // Walk up directory tree checking for .yucpignore files
            string currentDir = Path.GetDirectoryName(path);
            
            while (!string.IsNullOrEmpty(currentDir) && currentDir.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                string ignoreFilePath = Path.Combine(currentDir, IGNORE_FILENAME);
                
                if (File.Exists(ignoreFilePath))
                {
                    var patterns = GetIgnorePatterns(ignoreFilePath);
                    
                    // Get relative path from ignore file directory
                    string relativePath = GetRelativePathFrom(path, currentDir);
                    
                    foreach (string pattern in patterns)
                    {
                        if (MatchesPattern(relativePath, pattern))
                        {
                            Debug.Log($"[YucpIgnore] Ignoring '{relativePath}' due to pattern '{pattern}' in {ignoreFilePath}");
                            return true;
                        }
                    }
                }
                
                // Move up to parent directory
                string parentDir = Path.GetDirectoryName(currentDir);
                if (parentDir == currentDir) // Root reached
                    break;
                currentDir = parentDir;
            }
            
            return false;
        }
        
        /// <summary>
        /// Get all ignore patterns from a .yucpignore file
        /// </summary>
        private static List<string> GetIgnorePatterns(string ignoreFilePath)
        {
            // Check cache first
            if (ignoreCache.TryGetValue(ignoreFilePath, out var cachedPatterns))
            {
                // Validate cache is still fresh
                if (File.Exists(ignoreFilePath))
                {
                    var fileInfo = new FileInfo(ignoreFilePath);
                    // Cache is valid for 5 seconds
                    if ((DateTime.Now - fileInfo.LastWriteTime).TotalSeconds < 5)
                    {
                        return cachedPatterns;
                    }
                }
            }
            
            var patterns = new List<string>();
            
            try
            {
                if (!File.Exists(ignoreFilePath))
                    return patterns;
                
                string[] lines = File.ReadAllLines(ignoreFilePath);
                
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    
                    // Skip empty lines and comments
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                        continue;
                    
                    // Normalize path separators
                    trimmed = trimmed.Replace("\\", "/");
                    
                    patterns.Add(trimmed);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YucpIgnore] Failed to read {ignoreFilePath}: {ex.Message}");
            }
            
            // Update cache
            ignoreCache[ignoreFilePath] = patterns;
            
            return patterns;
        }
        
        /// <summary>
        /// Check if a path matches an ignore pattern
        /// Supports wildcards (*), directory matching (/), and negation (!)
        /// </summary>
        private static bool MatchesPattern(string path, string pattern)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(pattern))
                return false;
            
            // Normalize separators
            path = path.Replace("\\", "/");
            pattern = pattern.Replace("\\", "/");
            
            // Handle negation (! prefix means don't ignore)
            bool negate = false;
            if (pattern.StartsWith("!"))
            {
                negate = true;
                pattern = pattern.Substring(1).TrimStart();
            }
            
            bool matches = false;
            
            // Pattern starting with / matches from root of ignore file location
            if (pattern.StartsWith("/"))
            {
                pattern = pattern.Substring(1);
                matches = MatchesWildcard(path, pattern);
            }
            // Pattern ending with / matches directories only
            else if (pattern.EndsWith("/"))
            {
                pattern = pattern.TrimEnd('/');
                // Check if path is a directory or starts with this directory
                matches = path.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                         path.StartsWith(pattern + "/", StringComparison.OrdinalIgnoreCase);
            }
            // Pattern with / in middle matches path structure
            else if (pattern.Contains("/"))
            {
                matches = MatchesWildcard(path, pattern);
            }
            // Simple pattern matches filename anywhere in tree
            else
            {
                string fileName = Path.GetFileName(path);
                matches = MatchesWildcard(fileName, pattern) ||
                         path.Split('/').Any(segment => MatchesWildcard(segment, pattern));
            }
            
            // Apply negation
            return negate ? !matches : matches;
        }
        
        /// <summary>
        /// Match string against wildcard pattern
        /// Supports * (any characters) and ? (single character)
        /// </summary>
        private static bool MatchesWildcard(string text, string pattern)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
                return text == pattern;
            
            // Simple optimization for exact match
            if (!pattern.Contains("*") && !pattern.Contains("?"))
            {
                return text.Equals(pattern, StringComparison.OrdinalIgnoreCase);
            }
            
            // Convert wildcard pattern to regex-like matching
            int textIndex = 0;
            int patternIndex = 0;
            int starIndex = -1;
            int matchIndex = 0;
            
            while (textIndex < text.Length)
            {
                if (patternIndex < pattern.Length)
                {
                    char patternChar = pattern[patternIndex];
                    
                    if (patternChar == '*')
                    {
                        starIndex = patternIndex;
                        matchIndex = textIndex;
                        patternIndex++;
                        continue;
                    }
                    else if (patternChar == '?' || 
                            char.ToLowerInvariant(patternChar) == char.ToLowerInvariant(text[textIndex]))
                    {
                        textIndex++;
                        patternIndex++;
                        continue;
                    }
                }
                
                // Mismatch - backtrack if we had a *
                if (starIndex >= 0)
                {
                    patternIndex = starIndex + 1;
                    matchIndex++;
                    textIndex = matchIndex;
                }
                else
                {
                    return false;
                }
            }
            
            // Consume remaining * in pattern
            while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                patternIndex++;
            }
            
            return patternIndex == pattern.Length;
        }
        
        /// <summary>
        /// Get relative path from a base directory
        /// </summary>
        private static string GetRelativePathFrom(string fullPath, string baseDir)
        {
            fullPath = Path.GetFullPath(fullPath).Replace("\\", "/");
            baseDir = Path.GetFullPath(baseDir).Replace("\\", "/");
            
            if (!baseDir.EndsWith("/"))
                baseDir += "/";
            
            if (fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(baseDir.Length);
            }
            
            return fullPath;
        }
        
        /// <summary>
        /// Create a .yucpignore file in a directory
        /// </summary>
        public static bool CreateIgnoreFile(string directory, string[] initialPatterns = null)
        {
            try
            {
                string ignoreFilePath = Path.Combine(directory, IGNORE_FILENAME);
                
                var lines = new List<string>
                {
                    "# YUCP Package Exporter Ignore File",
                    "# Patterns in this file will be excluded from package exports",
                    "# ",
                    "# Syntax:",
                    "#   - Lines starting with # are comments",
                    "#   - Use * for wildcards (e.g., *.tmp)",
                    "#   - Use / at start to match from this directory (e.g., /Temp/)",
                    "#   - Use / at end to match directories only (e.g., .git/)",
                    "#   - Use ! to negate (e.g., !important.txt)",
                    "# ",
                    "# Common examples:",
                    "#   .git/",
                    "#   *.tmp",
                    "#   *.log",
                    "#   /Build/",
                    "#   node_modules/",
                    ""
                };
                
                if (initialPatterns != null && initialPatterns.Length > 0)
                {
                    lines.Add("# Custom patterns:");
                    lines.AddRange(initialPatterns);
                }
                
                File.WriteAllLines(ignoreFilePath, lines);
                
                // Clear cache for this file
                ignoreCache.Remove(ignoreFilePath);
                
                Debug.Log($"[YucpIgnore] Created ignore file: {ignoreFilePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YucpIgnore] Failed to create ignore file: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Check if a directory has a .yucpignore file
        /// </summary>
        public static bool HasIgnoreFile(string directory)
        {
            string ignoreFilePath = Path.Combine(directory, IGNORE_FILENAME);
            return File.Exists(ignoreFilePath);
        }
        
        /// <summary>
        /// Get the path to .yucpignore file in a directory
        /// </summary>
        public static string GetIgnoreFilePath(string directory)
        {
            return Path.Combine(directory, IGNORE_FILENAME);
        }
        
        /// <summary>
        /// Clear the pattern cache (useful when files are modified)
        /// </summary>
        public static void ClearCache()
        {
            ignoreCache.Clear();
        }
        
        /// <summary>
        /// Find all .yucpignore files in a directory tree
        /// </summary>
        public static List<string> FindAllIgnoreFiles(string rootDirectory)
        {
            var ignoreFiles = new List<string>();
            
            try
            {
                if (!Directory.Exists(rootDirectory))
                    return ignoreFiles;
                
                SearchForIgnoreFiles(rootDirectory, ignoreFiles);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YucpIgnore] Error searching for ignore files: {ex.Message}");
            }
            
            return ignoreFiles;
        }
        
        private static void SearchForIgnoreFiles(string directory, List<string> results)
        {
            try
            {
                string ignoreFile = Path.Combine(directory, IGNORE_FILENAME);
                if (File.Exists(ignoreFile))
                {
                    results.Add(ignoreFile);
                }
                
                // Search subdirectories
                foreach (string subdir in Directory.GetDirectories(directory))
                {
                    SearchForIgnoreFiles(subdir, results);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we can't access
            }
        }
    }
}


