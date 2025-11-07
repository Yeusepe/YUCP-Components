using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEngine;
using PackageGuardian.Core.Transactions;
using YUCP.Components.PackageGuardian.Editor.Settings;

namespace YUCP.Components.PackageGuardian.Editor.Services
{
    /// <summary>
    /// Import protection service - handles .yucp_disabled files and protects against import failures
    /// Consolidated from template guardian
    /// </summary>
    [InitializeOnLoad]
    public static class ImportProtectionService
    {
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int RETRY_DELAY_MS = 100;
        private const int FILE_OPERATION_TIMEOUT_MS = 5000;
        
        private static bool _hasProcessedThisSession = false;
        
        static ImportProtectionService()
        {
            EditorApplication.delayCall += SafeInitialize;
        }
        
        private static void SafeInitialize()
        {
            try
            {
                // Check if Package Guardian is enabled
                if (!PackageGuardianSettings.IsEnabled())
                {
                    Debug.Log("[Import Protection] Package Guardian is disabled - skipping import protection");
                    return;
                }
                
                if (CircuitBreakerService.IsCircuitBroken())
                {
                    Debug.LogWarning("[Import Protection] Circuit breaker active - skipping automatic protection");
                    return;
                }
                
                if (!CanSafelyOperate())
                {
                    EditorApplication.delayCall += SafeInitialize;
                    return;
                }
                
                PerformStartupCleanup();
                RecoverFromCrash();
            }
            catch (Exception ex)
            {
                CircuitBreakerService.RecordFailure("Initialization", ex);
            }
        }
        
        /// <summary>
        /// Checks if Unity is in a safe state for file operations
        /// </summary>
        private static bool CanSafelyOperate()
        {
            try
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    return false;
                    
                // Verify AssetDatabase is accessible
                try { AssetDatabase.GetAssetPath(0); }
                catch { return false; }
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Performs startup cleanup
        /// </summary>
        private static void PerformStartupCleanup()
        {
            try
            {
                CleanupOldTempFiles();
                CleanupDuplicateGuardians();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Import Protection] Startup cleanup failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Cleans up old temp files
        /// </summary>
        private static void CleanupOldTempFiles()
        {
            string[] patterns = new[] {
                "YUCP_TempInstall_*.json",
                "YUCP_Installer_*",
                "*_old_*",
                "*.tmp"
            };
            
            int cleaned = 0;
            foreach (var pattern in patterns)
            {
                var files = Directory.GetFiles(Application.dataPath, pattern, SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        // Only delete if older than 1 hour
                        if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalHours > 1)
                        {
                            File.Delete(file);
                            cleaned++;
                        }
                    }
                    catch { }
                }
            }
            
            if (cleaned > 0)
            {
                Debug.Log($"[Import Protection] Cleaned up {cleaned} old temp file(s)");
            }
        }
        
        /// <summary>
        /// Removes duplicate guardian scripts
        /// </summary>
        private static void CleanupDuplicateGuardians()
        {
            string editorPath = Path.Combine(Application.dataPath, "Editor");
            if (!Directory.Exists(editorPath))
                return;
                
            var guardians = Directory.GetFiles(editorPath, "*PackageGuardian*.cs", SearchOption.TopDirectoryOnly)
                .Where(f => !f.Contains("Editor") && f.Contains("YUCP") || f.Contains("Guardian"))
                .ToArray();
                
            if (guardians.Length > 1)
            {
                Debug.LogWarning($"[Import Protection] Found {guardians.Length} guardian scripts - cleaning duplicates");
                
                // Keep the one in Packages, delete standalone ones
                foreach (var guardian in guardians)
                {
                    if (guardian.Contains("Assets"))
                    {
                        try
                        {
                            File.Delete(guardian);
                            File.Delete(guardian + ".meta");
                            Debug.Log($"[Import Protection] Removed duplicate: {Path.GetFileName(guardian)}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[Import Protection] Failed to remove {Path.GetFileName(guardian)}: {ex.Message}");
                        }
                    }
                }
                
                AssetDatabase.Refresh();
            }
        }
        
        /// <summary>
        /// Recovers from previous crash
        /// </summary>
        private static void RecoverFromCrash()
        {
            // Check for transaction state file
            string stateFile = Path.Combine(Application.dataPath, "..", "Temp", "Guardian_Transaction.json");
            if (File.Exists(stateFile))
            {
                Debug.LogWarning("[Import Protection] Detected incomplete transaction - recovering...");
                
                try
                {
                    File.Delete(stateFile);
                    EmergencyRecovery();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Import Protection] Crash recovery failed: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Handles .yucp_disabled file conflicts
        /// </summary>
        public static void HandleDisabledFileConflicts()
        {
            if (CircuitBreakerService.IsCircuitBroken())
                return;
                
            using (var transaction = new GuardianTransaction())
            {
                try
                {
                    string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                    if (!Directory.Exists(packagesPath))
                        return;
                        
                    var disabledFiles = Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
                    
                    if (disabledFiles.Length == 0)
                        return;
                        
                    Debug.Log($"[Import Protection] Found {disabledFiles.Length} .yucp_disabled file(s) - analyzing conflicts...");
                    
                    var operations = new List<FileConflictOperation>();
                    
                    foreach (var disabledFile in disabledFiles)
                    {
                        try
                        {
                            string enabledFile = disabledFile.Substring(0, disabledFile.Length - ".yucp_disabled".Length);
                            
                            if (!File.Exists(enabledFile))
                            {
                                // No conflict - just enable the file
                                transaction.ExecuteFileOperation(disabledFile, enabledFile, FileOperationType.Move);
                                continue;
                            }
                            
                            // Backup both files
                            transaction.BackupFile(disabledFile);
                            transaction.BackupFile(enabledFile);
                            
                            // Determine resolution
                            var decision = DetermineConflictResolution(disabledFile, enabledFile);
                            operations.Add(new FileConflictOperation
                            {
                                DisabledFile = disabledFile,
                                EnabledFile = enabledFile,
                                Decision = decision
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[Import Protection] Error analyzing '{Path.GetFileName(disabledFile)}': {ex.Message}");
                        }
                    }
                    
                    // Execute operations
                    foreach (var op in operations)
                    {
                        ExecuteConflictResolution(op, transaction);
                    }
                    
                    // Commit transaction
                    transaction.Commit();
                    CircuitBreakerService.RecordSuccess();
                    
                    Debug.Log("[Import Protection] File conflict resolution complete");
                    AssetDatabase.Refresh();
                }
                catch (Exception ex)
                {
                    CircuitBreakerService.RecordFailure("HandleDisabledFileConflicts", ex);
                    throw; // Let transaction rollback
                }
            }
        }
        
        /// <summary>
        /// Determines how to resolve a file conflict using multiple heuristics
        /// </summary>
        private static ConflictDecision DetermineConflictResolution(string disabledFile, string enabledFile)
        {
            var decision = new ConflictDecision();
            int confidence = 0;
            
            // Method 1: File size comparison
            try
            {
                var disabledInfo = new FileInfo(disabledFile);
                var enabledInfo = new FileInfo(enabledFile);
                
                if (disabledInfo.Length == enabledInfo.Length)
                {
                    decision.IsDuplicate = true;
                    confidence += 40;
                    decision.Reason += "Same size. ";
                }
                else
                {
                    decision.IsUpdate = true;
                    confidence += 30;
                    decision.Reason += "Different size. ";
                }
            }
            catch
            {
                decision.IsDuplicate = true;
                confidence += 20;
                decision.Reason += "Size comparison failed. ";
            }
            
            // Method 2: Content hash (for small files)
            try
            {
                var disabledInfo = new FileInfo(disabledFile);
                if (disabledInfo.Length < 1024 * 100) // < 100KB
                {
                    string disabledHash = ComputeFileHash(disabledFile);
                    string enabledHash = ComputeFileHash(enabledFile);
                    
                    if (disabledHash == enabledHash)
                    {
                        decision.IsDuplicate = true;
                        confidence += 50;
                        decision.Reason += "Identical hash. ";
                    }
                    else
                    {
                        decision.IsUpdate = true;
                        confidence += 40;
                        decision.Reason += "Different hash. ";
                    }
                }
            }
            catch { }
            
            // Method 3: Timestamp
            try
            {
                var disabledTime = File.GetLastWriteTimeUtc(disabledFile);
                var enabledTime = File.GetLastWriteTimeUtc(enabledFile);
                
                if (disabledTime > enabledTime)
                {
                    decision.IsUpdate = true;
                    confidence += 20;
                    decision.Reason += "Disabled is newer. ";
                }
            }
            catch { }
            
            // Method 4: Version detection from package.json
            try
            {
                string packageDir = Path.GetDirectoryName(disabledFile);
                string packageJson = Path.Combine(packageDir, "package.json");
                
                if (File.Exists(packageJson))
                {
                    string content = File.ReadAllText(packageJson);
                    var versionMatch = Regex.Match(content, @"""version""\s*:\s*""([^""]+)""");
                    if (versionMatch.Success)
                    {
                        decision.Version = versionMatch.Groups[1].Value;
                        decision.Reason += $"Package v{decision.Version}. ";
                    }
                }
            }
            catch { }
            
            decision.Confidence = confidence;
            return decision;
        }
        
        /// <summary>
        /// Executes a conflict resolution operation
        /// </summary>
        private static void ExecuteConflictResolution(FileConflictOperation op, GuardianTransaction transaction)
        {
            try
            {
                if (op.Decision.IsDuplicate)
                {
                    // Just remove the disabled file
                    transaction.ExecuteFileOperation(op.DisabledFile, null, FileOperationType.Delete);
                    Debug.Log($"[Import Protection] Removed duplicate: {Path.GetFileName(op.DisabledFile)} ({op.Decision.Reason})");
                }
                else if (op.Decision.IsUpdate)
                {
                    // Replace enabled with disabled
                    transaction.ExecuteFileOperation(op.EnabledFile, op.EnabledFile + ".old", FileOperationType.Move);
                    transaction.ExecuteFileOperation(op.DisabledFile, op.EnabledFile, FileOperationType.Move);
                    Debug.Log($"[Import Protection] Updated: {Path.GetFileName(op.EnabledFile)} ({op.Decision.Reason})");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Import Protection] Failed to resolve conflict for {Path.GetFileName(op.DisabledFile)}: {ex.Message}");
                throw;
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
        
        /// <summary>
        /// Emergency recovery - removes all problematic files
        /// </summary>
        [MenuItem("Tools/Package Guardian/Emergency Recovery", priority = 201)]
        public static void EmergencyRecovery()
        {
            if (!EditorUtility.DisplayDialog(
                "Emergency Recovery",
                "This will remove all .yucp_disabled files, temp files, and perform a full cleanup.\n\n" +
                "Use this if automatic import protection failed.\n\nContinue?",
                "Yes, Recover",
                "Cancel"))
            {
                return;
            }
            
            int cleaned = 0;
            
            try
            {
                // Remove .yucp_disabled files
                string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                if (Directory.Exists(packagesPath))
                {
                    var disabledFiles = Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
                    foreach (var file in disabledFiles)
                    {
                        try
                        {
                            File.Delete(file);
                            File.Delete(file + ".meta");
                            cleaned++;
                        }
                        catch { }
                    }
                }
                
                // Remove temp files
                var tempFiles = Directory.GetFiles(Application.dataPath, "YUCP_*", SearchOption.AllDirectories);
                foreach (var file in tempFiles)
                {
                    try
                    {
                        File.Delete(file);
                        File.Delete(file + ".meta");
                        cleaned++;
                    }
                    catch { }
                }
                
                // Remove .old files
                var oldFiles = Directory.GetFiles(Application.dataPath, "*.old", SearchOption.AllDirectories);
                foreach (var file in oldFiles)
                {
                    try
                    {
                        File.Delete(file);
                        File.Delete(file + ".meta");
                        cleaned++;
                    }
                    catch { }
                }
                
                AssetDatabase.Refresh();
                CircuitBreakerService.ResetCircuitBreaker();
                
                EditorUtility.DisplayDialog(
                    "Recovery Complete",
                    $"Emergency recovery complete!\n\nRemoved {cleaned} problematic file(s).\n\nCircuit breaker has been reset.",
                    "OK"
                );
                
                Debug.Log($"[Import Protection] Emergency recovery complete: {cleaned} files cleaned");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Import Protection] Emergency recovery failed: {ex.Message}");
                EditorUtility.DisplayDialog("Recovery Failed", $"Emergency recovery failed: {ex.Message}", "OK");
            }
        }
        
        /// <summary>
        /// Shows import protection status
        /// </summary>
        [MenuItem("Tools/Package Guardian/Import Protection Status", priority = 202)]
        public static void ShowStatus()
        {
            try
            {
                string packagesPath = Path.Combine(Application.dataPath, "..", "Packages");
                var disabledFiles = Directory.Exists(packagesPath) 
                    ? Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories) 
                    : new string[0];
                    
                var tempFiles = Directory.GetFiles(Application.dataPath, "YUCP_*", SearchOption.AllDirectories);
                
                string status = "Import Protection Status:\n\n";
                status += CircuitBreakerService.GetStatusMessage() + "\n\n";
                status += $".yucp_disabled files: {disabledFiles.Length}\n";
                status += $"Temp files: {tempFiles.Length}\n\n";
                
                if (disabledFiles.Length > 0)
                {
                    status += "Recommendations:\n";
                    status += "• Run 'Handle Import Conflicts' to resolve file conflicts\n";
                    status += "• Or use 'Emergency Recovery' to clean all temp files\n";
                }
                else if (tempFiles.Length > 0)
                {
                    status += "Recommendations:\n";
                    status += "• Use 'Emergency Recovery' to clean temp files\n";
                }
                else
                {
                    status += "All clear - no issues detected";
                }
                
                EditorUtility.DisplayDialog("Import Protection Status", status, "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Import Protection] Failed to get status: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Manual trigger for handling import conflicts
        /// </summary>
        [MenuItem("Tools/Package Guardian/Handle Import Conflicts", priority = 203)]
        public static void ManualHandleConflicts()
        {
            try
            {
                HandleDisabledFileConflicts();
                EditorUtility.DisplayDialog("Import Protection", "Import conflict resolution complete!", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Import Protection] Failed to handle conflicts: {ex.Message}");
                EditorUtility.DisplayDialog("Import Protection", $"Failed to handle conflicts: {ex.Message}", "OK");
            }
        }
        
        // Data structures
        
        private class FileConflictOperation
        {
            public string DisabledFile;
            public string EnabledFile;
            public ConflictDecision Decision;
        }
        
        private class ConflictDecision
        {
            public bool IsDuplicate;
            public bool IsUpdate;
            public string Reason = "";
            public int Confidence;
            public string Version;
        }
    }
}







