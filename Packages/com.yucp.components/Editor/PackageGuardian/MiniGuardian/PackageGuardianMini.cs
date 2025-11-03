using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using PackageGuardian.Core.Transactions;

namespace YUCP.PackageGuardian.Mini
{
    /// <summary>
    /// Lightweight Package Guardian for bundling with YUCP packages.
    /// Focuses on import protection: duplicate detection, error reversion, and ensuring error-free imports.
    /// Uses the same core methods as the full guardian for consistency.
    /// </summary>
    [InitializeOnLoad]
    public class PackageGuardianMini : AssetPostprocessor
    {
        private const int MAX_CONSECUTIVE_FAILURES = 3;
        private const string PREF_KEY_FAILURES = "MiniGuardian_Failures";
        private const string PREF_KEY_LAST_IMPORT = "MiniGuardian_LastImport";
        
        private static bool _hasProcessedThisSession = false;
        private static GuardianTransaction _currentTransaction = null;
        private static readonly string MarkerDir = Path.Combine(Application.dataPath, "..", "Library", "YUCP");
        private static string MarkerPath(string name) => Path.Combine(MarkerDir, "install." + name);
        private static bool HasMarker(string name) { try { return File.Exists(MarkerPath(name)); } catch { return false; } }
        private static bool IsMarkerStale(string name, TimeSpan maxAge)
        {
            try { var p = MarkerPath(name); if (!File.Exists(p)) return false; return (DateTime.UtcNow - File.GetLastWriteTimeUtc(p)) > maxAge; } catch { return false; }
        }
        
        static PackageGuardianMini()
        {
            EditorApplication.delayCall += SafeInitialize;
        }
        
        private static void SafeInitialize()
        {
            try
            {
                // Check circuit breaker
				if (IsCircuitBroken())
                {
					Debug.LogWarning("[Mini Guardian] Too many failures - protection disabled. Use Tools > Package Guardian > Reset Mini Guardian");
                    return;
                }
                
                // Wait for Unity to be ready
                if (!IsUnityReady())
                {
                    EditorApplication.delayCall += SafeInitialize;
                    return;
                }
                
                // Perform startup checks
                CleanupOldFiles();
                RecoverFromCrash();
            }
            catch (Exception ex)
            {
                RecordFailure("Initialization", ex);
            }
        }
        
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (_hasProcessedThisSession || IsCircuitBroken())
                return;
                
            // Check if this is a YUCP package import
            bool hasYucpFiles = importedAssets.Any(a => 
                a.EndsWith(".yucp_disabled") || 
                a.Contains("YUCP_") ||
                (a.Contains("Packages") && a.EndsWith("package.json")));
                
            if (!hasYucpFiles)
                return;
                
            _hasProcessedThisSession = true;
            
            try
            {
                Debug.Log("[Mini Guardian] Import detected - protecting...");
                
                // Start transaction
                _currentTransaction = new GuardianTransaction();
                
                // Execute protection (but skip duplicate detection - installer scripts need to run first)
                ProtectImport(importedAssets);
                
                // Commit if successful
                _currentTransaction.Commit();
                _currentTransaction = null;
                
                RecordSuccess();
                
                Debug.Log("[Mini Guardian] Protection complete");
            }
            catch (Exception ex)
            {
                RecordFailure("Import protection", ex);
                
                // Rollback transaction
                if (_currentTransaction != null)
                {
                    Debug.LogWarning("[Mini Guardian] Import failed - rolling back...");
                    _currentTransaction.Rollback();
                    _currentTransaction = null;
                }
                
                // Emergency cleanup
                EmergencyCleanup();
            }
            finally
            {
                EditorApplication.delayCall += () => { _hasProcessedThisSession = false; };
            }
        }
        
        /// <summary>
        /// Main protection logic
        /// </summary>
        private static void ProtectImport(string[] importedAssets)
        {
            // Step 1: Handle duplicate detection (but skip installer scripts - they need to run first)
            DetectAndRemoveDuplicates(importedAssets);
            
            // Step 2: Handle .yucp_disabled files
            HandleDisabledFiles();
            
            // Step 3: Detect redundant imports
            DetectRedundantImport();
            
            // Step 4: Verify import integrity
            VerifyImportIntegrity();
        }
        
        /// <summary>
        /// Detects and removes duplicate guardian/installer scripts
        /// </summary>
        private static void DetectAndRemoveDuplicates(string[] importedAssets)
        {
            string editorPath = Path.Combine(Application.dataPath, "Editor");
            if (!Directory.Exists(editorPath))
                return;
                
            // Normalize imported asset paths for comparison
            var importedPaths = new HashSet<string>(importedAssets.Select(a => Path.GetFullPath(a).Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
            bool installActive = HasMarker("lock") || HasMarker("pending");
                
            // Find guardian scripts
            var guardians = Directory.GetFiles(editorPath, "*Guardian*.cs", SearchOption.TopDirectoryOnly)
                .Where(f => f.Contains("Package") || f.Contains("YUCP"))
                .ToArray();
                
            if (guardians.Length > 1)
            {
                Debug.Log($"[Mini Guardian] Found {guardians.Length} guardian scripts - removing duplicates");
                
                // Keep only the one in Packages folder, remove standalone ones
                foreach (var guardian in guardians)
                {
                    string normalizedPath = Path.GetFullPath(guardian).Replace('\\', '/');
                    // Don't remove if it was just imported (part of current import)
                    if (importedPaths.Contains(normalizedPath))
                        continue;
                        
                    if (guardian.Contains("Assets") && !guardian.Contains("Packages"))
                    {
                        try
                        {
                            _currentTransaction.BackupFile(guardian);
                            File.Delete(guardian);
                            File.Delete(guardian + ".meta");
                            Debug.Log($"[Mini Guardian] Removed duplicate: {Path.GetFileName(guardian)}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[Mini Guardian] Failed to remove duplicate: {ex.Message}");
                        }
                    }
                }
            }
            
            // Find installer scripts - but DON'T remove them during active install (they need to run first)
            // Installer scripts clean themselves up after running
            var installers = Directory.GetFiles(editorPath, "*Installer*.cs", SearchOption.TopDirectoryOnly)
                .Where(f => f.Contains("YUCP"))
                .ToArray();
                
            if (installers.Length > 1)
            {
                if (installActive)
                {
                    Debug.Log($"[Mini Guardian] Found {installers.Length} installer scripts - skipping removal (active install)");
                }
                
                // If install is complete, aggressively clean up leftovers in Assets/Editor
                if (!installActive && HasMarker("complete"))
                {
                    var leftovers = installers.Where(i => {
                        string normalizedPath = Path.GetFullPath(i).Replace('\\', '/');
                        return !importedPaths.Contains(normalizedPath) && i.Contains("Assets") && !i.Contains("Packages");
                    }).ToArray();

                    foreach (var installer in leftovers)
                    {
                        try
                        {
                            _currentTransaction.BackupFile(installer);
                            File.Delete(installer);
                            File.Delete(installer + ".meta");
                            Debug.Log($"[Mini Guardian] Cleaned leftover installer: {Path.GetFileName(installer)}");
                        }
                        catch { }
                    }
                }
                else
                {
                    // Only log a warning if there are truly old duplicates (not part of current import)
                    var oldInstallers = installers.Where(i => {
                        string normalizedPath = Path.GetFullPath(i).Replace('\\', '/');
                        return !importedPaths.Contains(normalizedPath) && i.Contains("Assets") && !i.Contains("Packages");
                    }).ToArray();
                    if (oldInstallers.Length > 0)
                        Debug.LogWarning($"[Mini Guardian] Found {oldInstallers.Length} installer script(s) - awaiting installer cleanup");
                }
            }
        }
        
        /// <summary>
        /// Handles .yucp_disabled files - the core import protection
        /// </summary>
        private static void HandleDisabledFiles()
        {
            string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
            if (!Directory.Exists(packagesPath))
                return;
                
            var disabledFiles = Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
            
            if (disabledFiles.Length == 0)
                return;
                
            Debug.Log($"[Mini Guardian] Found {disabledFiles.Length} .yucp_disabled file(s)");
            
            foreach (var disabledFile in disabledFiles)
            {
                try
                {
                    string enabledFile = disabledFile.Substring(0, disabledFile.Length - ".yucp_disabled".Length);
                    
                    // Backup both files
                    _currentTransaction.BackupFile(disabledFile);
                    if (File.Exists(enabledFile))
                        _currentTransaction.BackupFile(enabledFile);
                    
                    // If no conflict, just enable
                    if (!File.Exists(enabledFile))
                    {
                        _currentTransaction.ExecuteFileOperation(disabledFile, enabledFile, FileOperationType.Move);
                        Debug.Log($"[Mini Guardian] Enabled: {Path.GetFileName(enabledFile)}");
                        continue;
                    }
                    
                    // Conflict detected - determine action
                    var decision = ResolveConflict(disabledFile, enabledFile);
                    
                    if (decision == ConflictResolution.KeepDisabled)
                    {
                        // Replace enabled with disabled
                        _currentTransaction.ExecuteFileOperation(enabledFile, enabledFile + ".old", FileOperationType.Move);
                        _currentTransaction.ExecuteFileOperation(disabledFile, enabledFile, FileOperationType.Move);
                        Debug.Log($"[Mini Guardian] Updated: {Path.GetFileName(enabledFile)}");
                    }
                    else if (decision == ConflictResolution.RemoveDisabled)
                    {
                        // Remove disabled file (duplicate)
                        _currentTransaction.ExecuteFileOperation(disabledFile, null, FileOperationType.Delete);
                        Debug.Log($"[Mini Guardian] Removed duplicate: {Path.GetFileName(disabledFile)}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Mini Guardian] Failed to handle {Path.GetFileName(disabledFile)}: {ex.Message}");
                    throw; // Let transaction rollback
                }
            }
        }
        
        /// <summary>
        /// Resolves file conflicts using lightweight heuristics
        /// </summary>
        private static ConflictResolution ResolveConflict(string disabledFile, string enabledFile)
        {
            try
            {
                var disabledInfo = new FileInfo(disabledFile);
                var enabledInfo = new FileInfo(enabledFile);
                
                // Method 1: Same size = likely duplicate
                if (disabledInfo.Length == enabledInfo.Length)
                {
                    // Verify with hash for small files
                    if (disabledInfo.Length < 100 * 1024) // < 100KB
                    {
                        if (ComputeFileHash(disabledFile) == ComputeFileHash(enabledFile))
                        {
                            return ConflictResolution.RemoveDisabled; // Exact duplicate
                        }
                    }
                    else
                    {
                        // Assume duplicate for large files with same size
                        return ConflictResolution.RemoveDisabled;
                    }
                }
                
                // Method 2: Disabled is newer = likely update
                if (disabledInfo.LastWriteTimeUtc > enabledInfo.LastWriteTimeUtc)
                {
                    return ConflictResolution.KeepDisabled;
                }
                
                // Default: keep existing (safer)
                return ConflictResolution.RemoveDisabled;
            }
            catch
            {
                // On error, remove disabled (safer default)
                return ConflictResolution.RemoveDisabled;
            }
        }
        
        /// <summary>
        /// Detects if this is a redundant import (no changes)
        /// </summary>
        private static void DetectRedundantImport()
        {
            string lastImportHash = EditorPrefs.GetString(PREF_KEY_LAST_IMPORT, "");
            
            // Compute current import hash (based on package files)
            string currentHash = ComputeImportHash();
            
            if (!string.IsNullOrEmpty(lastImportHash) && lastImportHash == currentHash)
            {
                Debug.LogWarning("[Mini Guardian] Redundant import detected - no file changes");
                Debug.LogWarning("[Mini Guardian] This import appears identical to the previous one");
            }
            
            // Store current hash
            EditorPrefs.SetString(PREF_KEY_LAST_IMPORT, currentHash);
        }
        
        /// <summary>
        /// Verifies import integrity
        /// </summary>
        private static void VerifyImportIntegrity()
        {
            // Check for compilation errors
            if (EditorApplication.isCompiling)
            {
                Debug.Log("[Mini Guardian] Compilation in progress - will verify after compile");
                return;
            }
            
            // Verify no .yucp_disabled files remain
            string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
            if (Directory.Exists(packagesPath))
            {
                var remaining = Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
                if (remaining.Length > 0)
                {
                    throw new Exception($"Import incomplete: {remaining.Length} .yucp_disabled files remain");
                }
            }
        }
        
        /// <summary>
        /// Computes a hash representing the current import state
        /// </summary>
        private static string ComputeImportHash()
        {
            try
            {
                string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                var packageFiles = Directory.GetFiles(packagesPath, "package.json", SearchOption.AllDirectories)
                    .OrderBy(f => f)
                    .ToArray();
                    
                using (var md5 = MD5.Create())
                {
                    var combined = string.Join("|", packageFiles.Select(f => {
                        var info = new FileInfo(f);
                        return $"{f}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
                    }));
                    
                    byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined));
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch
            {
                return Guid.NewGuid().ToString("N");
            }
        }
        
        /// <summary>
        /// Computes file hash
        /// </summary>
        private static string ComputeFileHash(string filePath)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }
        
        // ===== SAFETY & RECOVERY =====
        
        private static bool IsUnityReady()
        {
            try
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    return false;
                    
                try { AssetDatabase.GetAssetPath(0); }
                catch { return false; }
                
                return true;
            }
            catch { return false; }
        }
        
        private static void CleanupOldFiles()
        {
            try
            {
                // Remove old .old files
                var oldFiles = Directory.GetFiles(Application.dataPath, "*.old", SearchOption.AllDirectories)
                    .Where(f => (DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalHours > 1)
                    .ToArray();
                    
                foreach (var file in oldFiles)
                {
                    try
                    {
                        File.Delete(file);
                        File.Delete(file + ".meta");
                    }
                    catch { }
                }
            }
            catch { }
        }
        
        private static void RecoverFromCrash()
        {
            string stateFile = Path.Combine(Application.dataPath, "..", "Temp", "MiniGuardian_Transaction.json");
            if (File.Exists(stateFile))
            {
                Debug.LogWarning("[Mini Guardian] Incomplete transaction detected - recovering...");
                
                try
                {
                    File.Delete(stateFile);
                    EmergencyCleanup();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Mini Guardian] Crash recovery failed: {ex.Message}");
                }
            }
        }
        
        private static void EmergencyCleanup()
        {
            try
            {
                string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                
                // Remove .yucp_disabled files
                if (Directory.Exists(packagesPath))
                {
                    var disabledFiles = Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
                    foreach (var file in disabledFiles)
                    {
                        try
                        {
                            File.Delete(file);
                            File.Delete(file + ".meta");
                        }
                        catch { }
                    }
                }
                
                // Remove .old files
                var oldFiles = Directory.GetFiles(Application.dataPath, "*.old", SearchOption.AllDirectories);
                foreach (var file in oldFiles)
                {
                    try
                    {
                        File.Delete(file);
                        File.Delete(file + ".meta");
                    }
                    catch { }
                }
                
                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Mini Guardian] Emergency cleanup failed: {ex.Message}");
            }
        }
        
        // ===== CIRCUIT BREAKER =====
        
        private static bool IsCircuitBroken()
        {
            int failures = EditorPrefs.GetInt(PREF_KEY_FAILURES, 0);
            return failures >= MAX_CONSECUTIVE_FAILURES;
        }
        
        private static void RecordFailure(string operation, Exception ex)
        {
            int failures = EditorPrefs.GetInt(PREF_KEY_FAILURES, 0) + 1;
            EditorPrefs.SetInt(PREF_KEY_FAILURES, failures);
            
            Debug.LogError($"[Mini Guardian] {operation} failed ({failures}/{MAX_CONSECUTIVE_FAILURES}): {ex.Message}");
            
			if (failures >= MAX_CONSECUTIVE_FAILURES)
            {
                Debug.LogError("[Mini Guardian] Circuit breaker activated - protection disabled!");
				Debug.LogError("[Mini Guardian] Use Tools > Package Guardian > Reset Mini Guardian to re-enable");
            }
        }
        
        private static void RecordSuccess()
        {
            EditorPrefs.SetInt(PREF_KEY_FAILURES, 0);
        }
        
		[MenuItem("Tools/Package Guardian/Reset Mini Guardian")]
        public static void ResetCircuitBreaker()
        {
            EditorPrefs.SetInt(PREF_KEY_FAILURES, 0);
            Debug.Log("[Mini Guardian] Circuit breaker reset - protection re-enabled");
        }
        
		[MenuItem("Tools/Package Guardian/Mini Guardian Status")]
        public static void ShowStatus()
        {
            int failures = EditorPrefs.GetInt(PREF_KEY_FAILURES, 0);
            string lastImport = EditorPrefs.GetString(PREF_KEY_LAST_IMPORT, "Never");
            
            string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
            int disabledCount = Directory.Exists(packagesPath) 
                ? Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories).Length 
                : 0;
            
            string status = "Mini Package Guardian Status:\n\n";
            status += failures >= MAX_CONSECUTIVE_FAILURES 
                ? "Circuit Breaker: ACTIVE (protection disabled)\n" 
                : $"Circuit Breaker: OK ({failures}/{MAX_CONSECUTIVE_FAILURES} failures)\n";
            status += $".yucp_disabled files: {disabledCount}\n";
            status += $"Last import: {(lastImport == "Never" ? "Never" : "Recently")}\n\n";
            
            if (failures >= MAX_CONSECUTIVE_FAILURES)
            {
                status += "Protection is disabled. Use 'Reset Mini Guardian' to re-enable.\n";
            }
            else if (disabledCount > 0)
            {
                status += "Warning: Import may be incomplete. Restart Unity if issues persist.\n";
            }
            else
            {
                status += "All clear - no issues detected.";
            }
            
            EditorUtility.DisplayDialog("Mini Guardian Status", status, "OK");
        }
        
        private enum ConflictResolution
        {
            KeepDisabled,    // Replace enabled with disabled
            RemoveDisabled,  // Remove disabled (keep enabled)
        }
    }
}




