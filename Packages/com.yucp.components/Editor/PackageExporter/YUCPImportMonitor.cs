using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace YUCP.Components.Editor.PackageExporter
{
    /// <summary>
    /// Global import monitor for YUCP packages.
    /// This script is part of com.yucp.components and monitors for incoming package imports.
    /// Provides the same protection as YUCPPackageGuardian (bundled version), but always present.
    /// </summary>
    [InitializeOnLoad]
    public class YUCPImportMonitor : AssetPostprocessor
    {
        private static HashSet<string> processedImports = new HashSet<string>();
        
        static YUCPImportMonitor()
        {
            // On editor load, check if standalone guardian should be removed
            EditorApplication.delayCall += CheckForStandaloneGuardian;
        }
        
        /// <summary>
        /// Remove standalone guardian if com.yucp.components is present
        /// </summary>
        private static void CheckForStandaloneGuardian()
        {
            try
            {
                string standaloneGuardianPath = Path.Combine(Application.dataPath, "..", "Packages", "yucp.packageguardian");
                
                if (Directory.Exists(standaloneGuardianPath))
                {
                    Debug.Log("[YUCP ImportMonitor] Found standalone guardian - removing (com.yucp.components provides protection)...");
                    
                    try
                    {
                        Directory.Delete(standaloneGuardianPath, true);
                        Debug.Log("[YUCP ImportMonitor] Removed standalone guardian package");
                        AssetDatabase.Refresh();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[YUCP ImportMonitor] Could not auto-remove standalone guardian: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP ImportMonitor] Error checking for standalone guardian: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Runs when assets are imported - detects .yucp_disabled files and handles conflicts
        /// </summary>
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            // Check if any .yucp_disabled files were imported
            var disabledFiles = importedAssets.Where(a => a.EndsWith(".yucp_disabled")).ToArray();
            
            if (disabledFiles.Length == 0)
                return;
            
            Debug.Log($"[YUCP ImportMonitor] Detected {disabledFiles.Length} .yucp_disabled files being imported");
            
            // Check for conflicts with existing enabled files
            HandleImportConflicts(disabledFiles);
        }
        
        private static void HandleImportConflicts(string[] disabledFiles)
        {
            try
            {
                var conflictingFiles = new List<string>();
                var safeFiles = new List<string>();
                
                // Analyze each disabled file
                foreach (string disabledAssetPath in disabledFiles)
                {
                    // Convert to full path
                    string disabledFullPath = Path.GetFullPath(disabledAssetPath);
                    string enabledFullPath = disabledFullPath.Substring(0, disabledFullPath.Length - ".yucp_disabled".Length);
                    
                    if (File.Exists(enabledFullPath))
                    {
                        // Conflict detected - enabled version already exists
                        conflictingFiles.Add(disabledAssetPath);
                    }
                    else
                    {
                        // No conflict - this is a new file
                        safeFiles.Add(disabledAssetPath);
                    }
                }
                
                if (conflictingFiles.Count == 0)
                {
                    Debug.Log($"[YUCP ImportMonitor] No conflicts detected. {safeFiles.Count} new files importing.");
                    return;
                }
                
                // Determine if this is a duplicate or an update
                string detectionResult = DetectImportType(conflictingFiles);
                
                if (detectionResult == "DUPLICATE")
                {
                    // Same version already installed - delete the incoming .yucp_disabled files
                    Debug.LogWarning($"[YUCP ImportMonitor] DUPLICATE IMPORT DETECTED!");
                    Debug.LogWarning($"[YUCP ImportMonitor] Package is already installed. Removing {conflictingFiles.Count} conflicting files...");
                    
                    DeleteConflictingDisabledFiles(conflictingFiles);
                }
                else if (detectionResult == "UPDATE")
                {
                    // Newer version - delete the OLD enabled files to make room for new ones
                    Debug.Log($"[YUCP ImportMonitor] UPDATE DETECTED!");
                    Debug.Log($"[YUCP ImportMonitor] Removing {conflictingFiles.Count} old files to make room for update...");
                    
                    DeleteOldEnabledFiles(conflictingFiles);
                }
                else
                {
                    // Unknown - use heuristic
                    // If >90% of files conflict, probably duplicate
                    float conflictPercentage = (float)conflictingFiles.Count / disabledFiles.Length;
                    
                    if (conflictPercentage > 0.9f)
                    {
                        Debug.LogWarning($"[YUCP ImportMonitor] {conflictPercentage:P0} of files conflict - assuming DUPLICATE");
                        DeleteConflictingDisabledFiles(conflictingFiles);
                    }
                    else
                    {
                        Debug.Log($"[YUCP ImportMonitor] {conflictPercentage:P0} of files conflict - assuming UPDATE");
                        DeleteOldEnabledFiles(conflictingFiles);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP ImportMonitor] Error handling import conflicts: {ex.Message}");
                Debug.LogError("[YUCP ImportMonitor] Import will proceed but may have GUID conflicts.");
            }
        }
        
        private static string DetectImportType(List<string> conflictingFiles)
        {
            try
            {
                // Try to find temp install JSON
                string[] tempJsonFiles = Directory.GetFiles(Application.dataPath, "YUCP_TempInstall_*.json", SearchOption.TopDirectoryOnly);
                
                if (tempJsonFiles.Length > 0)
                {
                    string jsonContent = File.ReadAllText(tempJsonFiles[0]);
                    var nameMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, @"""name""\s*:\s*""([^""]+)""");
                    var newVersionMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, @"""version""\s*:\s*""([^""]+)""");
                    
                    if (nameMatch.Success && newVersionMatch.Success)
                    {
                        string packageName = nameMatch.Groups[1].Value;
                        string newVersion = newVersionMatch.Groups[1].Value;
                        
                        // Check existing package.json
                        string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                        string existingPackageJson = Path.Combine(packagesPath, packageName, "package.json");
                        
                        if (File.Exists(existingPackageJson))
                        {
                            string existingJson = File.ReadAllText(existingPackageJson);
                            var existingVersionMatch = System.Text.RegularExpressions.Regex.Match(existingJson, @"""version""\s*:\s*""([^""]+)""");
                            
                            if (existingVersionMatch.Success)
                            {
                                string existingVersion = existingVersionMatch.Groups[1].Value;
                                
                                int comparison = CompareVersions(existingVersion, newVersion);
                                
                                if (comparison == 0)
                                {
                                    Debug.Log($"[YUCP ImportMonitor] Version match: {packageName} v{newVersion} (DUPLICATE)");
                                    return "DUPLICATE";
                                }
                                else if (comparison < 0)
                                {
                                    Debug.Log($"[YUCP ImportMonitor] Version upgrade: {packageName} {existingVersion} → {newVersion} (UPDATE)");
                                    return "UPDATE";
                                }
                                else
                                {
                                    Debug.LogWarning($"[YUCP ImportMonitor] Version downgrade: {packageName} {existingVersion} → {newVersion}");
                                    return "DOWNGRADE"; // Treat as update (delete old, install new)
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP ImportMonitor] Could not determine import type: {ex.Message}");
            }
            
            return "UNKNOWN";
        }
        
        private static void DeleteConflictingDisabledFiles(List<string> conflictingDisabledAssetPaths)
        {
            int deletedCount = 0;
            
            foreach (string assetPath in conflictingDisabledAssetPaths)
            {
                try
                {
                    string fullPath = Path.GetFullPath(assetPath);
                    
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        string metaPath = fullPath + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                        deletedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[YUCP ImportMonitor] Failed to delete '{Path.GetFileName(assetPath)}': {ex.Message}");
                }
            }
            
            if (deletedCount > 0)
            {
                Debug.Log($"[YUCP ImportMonitor] Deleted {deletedCount} duplicate .yucp_disabled files");
                AssetDatabase.Refresh();
            }
        }
        
        private static void DeleteOldEnabledFiles(List<string> conflictingDisabledAssetPaths)
        {
            int deletedCount = 0;
            
            foreach (string disabledAssetPath in conflictingDisabledAssetPaths)
            {
                try
                {
                    string disabledFullPath = Path.GetFullPath(disabledAssetPath);
                    string enabledFullPath = disabledFullPath.Substring(0, disabledFullPath.Length - ".yucp_disabled".Length);
                    
                    if (File.Exists(enabledFullPath))
                    {
                        // Delete the OLD enabled file to make room for the new one
                        File.Delete(enabledFullPath);
                        string metaPath = enabledFullPath + ".meta";
                        if (File.Exists(metaPath))
                            File.Delete(metaPath);
                        deletedCount++;
                        
                        Debug.Log($"[YUCP ImportMonitor] Deleted old file to make room for update: {Path.GetFileName(enabledFullPath)}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[YUCP ImportMonitor] Failed to delete old file '{Path.GetFileName(disabledAssetPath)}': {ex.Message}");
                }
            }
            
            if (deletedCount > 0)
            {
                Debug.Log($"[YUCP ImportMonitor] Deleted {deletedCount} old files to prepare for update");
                AssetDatabase.Refresh();
            }
        }
        
        private static int CompareVersions(string v1, string v2)
        {
            try
            {
                var version1 = ParseVersion(v1);
                var version2 = ParseVersion(v2);
                
                if (version1.major != version2.major)
                    return version1.major.CompareTo(version2.major);
                if (version1.minor != version2.minor)
                    return version1.minor.CompareTo(version2.minor);
                return version1.patch.CompareTo(version2.patch);
            }
            catch
            {
                return 0;
            }
        }
        
        private static (int major, int minor, int patch) ParseVersion(string version)
        {
            version = version.Trim().TrimStart('v', 'V');
            int dashIndex = version.IndexOf('-');
            if (dashIndex > 0)
                version = version.Substring(0, dashIndex);
            
            var parts = version.Split('.');
            int major = parts.Length > 0 && int.TryParse(parts[0], out int m) ? m : 0;
            int minor = parts.Length > 1 && int.TryParse(parts[1], out int n) ? n : 0;
            int patch = parts.Length > 2 && int.TryParse(parts[2], out int p) ? p : 0;
            
            return (major, minor, patch);
        }
    }
}

