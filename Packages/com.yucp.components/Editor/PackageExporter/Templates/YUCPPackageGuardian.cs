using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Threading;

namespace YUCP.PackageGuardian
{
    /// <summary>
    /// YUCP Package Guardian - Comprehensive import protection and recovery system.
    /// Bundled with YUCP packages when com.yucp.components is not a dependency.
    /// Provides transaction-based operations, state rollback, and automatic error recovery.
    /// </summary>
    [InitializeOnLoad]
    public class YUCPPackageGuardian : AssetPostprocessor
    {
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int RETRY_DELAY_MS = 100;
        private const int FILE_OPERATION_TIMEOUT_MS = 5000;
        private const int MAX_CONSECUTIVE_FAILURES = 3;
        private const string STATE_FILE = "YUCP_Guardian_State.json";
        
        private static bool hasProcessedThisSession = false;
        private static int consecutiveFailures = 0;
        private static readonly object lockObject = new object();
        private static ImportTransaction currentTransaction = null;
        private static Dictionary<string, byte[]> fileBackups = new Dictionary<string, byte[]>();
        
        static YUCPPackageGuardian()
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
                    Debug.LogWarning("[YUCP Guardian] Circuit breaker active - too many recent failures. Skipping automatic protection.");
                    Debug.LogWarning("[YUCP Guardian] Manual cleanup recommended. Use Tools > YUCP > Reset Guardian State");
                    return;
                }
                
                // Verify Unity state before running
                if (!CanSafelyOperate())
                {
                    Debug.Log("[YUCP Guardian] Unity not ready for operations. Will retry later.");
                    EditorApplication.delayCall += SafeInitialize;
                    return;
                }
                
                PerformStartupCleanup();
                CheckForMigration();
                RecoverFromCrash();
            }
            catch (Exception ex)
            {
                LogError("Initialization", ex);
                consecutiveFailures++;
            }
        }
        
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            lock (lockObject)
            {
                if (hasProcessedThisSession || IsCircuitBroken())
                    return;
                
                bool hasYucpFiles = importedAssets.Any(a => 
                    a.EndsWith(".yucp_disabled") || 
                    a.Contains("YUCP_TempInstall_") ||
                    a.Contains("YUCP_Installer_"));
                
                if (!hasYucpFiles)
                    return;
                
                hasProcessedThisSession = true;
                
                try
                {
                    // Wait for Unity to be ready
                    if (!WaitForUnityReady())
                    {
                        Debug.LogWarning("[YUCP Guardian] Unity not ready - skipping protection this session");
                        return;
                    }
                    
                    // Start transaction
                    currentTransaction = new ImportTransaction();
                    currentTransaction.StartTime = DateTime.UtcNow;
                    SaveTransactionState();
                    
                    Debug.Log("[YUCP Guardian] Import detected. Protecting project integrity...");
                    
                    // Execute protection sequence
                    ExecuteProtectionSequence();
                    
                    // Commit transaction
                    currentTransaction.Success = true;
                    currentTransaction = null;
                    DeleteTransactionState();
                    
                    // Reset failure counter on success
                    consecutiveFailures = 0;
                    
                    Debug.Log("[YUCP Guardian] Protection complete. Import proceeding safely.");
                }
                catch (Exception ex)
                {
                    LogError("Protection sequence", ex);
                    consecutiveFailures++;
                    
                    // Attempt rollback
                    if (currentTransaction != null)
                    {
                        Debug.LogWarning("[YUCP Guardian] Attempting transaction rollback...");
                        RollbackTransaction();
                    }
                    
                    // Emergency recovery
                    EmergencyRecovery();
                }
                finally
                {
                    // Cleanup
                    fileBackups.Clear();
                    EditorApplication.delayCall += () => { hasProcessedThisSession = false; };
                }
            }
        }
        
        private static void ExecuteProtectionSequence()
        {
            // Phase 1: Self-cleanup (remove duplicate guardians/installers)
            ExecutePhase("Cleanup Duplicates", () =>
            {
                CleanupDuplicateGuardians();
                CleanupDuplicateInstallers();
            });
            
            // Phase 2: Handle file conflicts
            ExecutePhase("Resolve Conflicts", HandleDisabledFileConflicts);
            
            // Phase 3: Verify integrity
            ExecutePhase("Verify Integrity", VerifyProjectIntegrity);
            
            // Phase 4: Validate manifests
            ExecutePhase("Validate Manifests", ValidateManifests);
        }
        
        private static void ExecutePhase(string phaseName, Action phaseAction)
        {
            try
            {
                Debug.Log($"[YUCP Guardian] Phase: {phaseName}");
                
                var startTime = DateTime.UtcNow;
                phaseAction();
                var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                
                if (duration > 1000)
                    Debug.LogWarning($"[YUCP Guardian] Phase '{phaseName}' took {duration:F0}ms (unusually long)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Guardian] Phase '{phaseName}' failed: {ex.Message}");
                throw; // Re-throw to trigger rollback
            }
        }
        
        private static bool CanSafelyOperate()
        {
            try
            {
                // Check if Unity is in a safe state for file operations
                if (EditorApplication.isCompiling)
                    return false;
                
                if (EditorApplication.isUpdating)
                    return false;
                
                if (BuildPipeline.isBuildingPlayer)
                    return false;
                
                // Verify AssetDatabase is accessible
                try
                {
                    AssetDatabase.GetAssetPath(0);
                }
                catch
                {
                    return false;
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        private static bool WaitForUnityReady(int maxWaitMs = 5000)
        {
            var startTime = DateTime.UtcNow;
            
            while (!CanSafelyOperate())
            {
                if ((DateTime.UtcNow - startTime).TotalMilliseconds > maxWaitMs)
                    return false;
                
                Thread.Sleep(100);
            }
            
            return true;
        }
        
        private static bool IsCircuitBroken()
        {
            return consecutiveFailures >= MAX_CONSECUTIVE_FAILURES;
        }
        
        private static void HandleDisabledFileConflicts()
        {
            string packagesPath = GetPackagesPath();
            if (!SafeDirectoryExists(packagesPath))
            {
                Debug.LogWarning("[YUCP Guardian] Packages folder not accessible");
                return;
            }
            
            string[] disabledFiles = SafeFindFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
            
            if (disabledFiles.Length == 0)
                return;
            
            Debug.Log($"[YUCP Guardian] Found {disabledFiles.Length} .yucp_disabled files");
            
            var operations = new List<FileOperation>();
            
            foreach (string disabledFile in disabledFiles)
            {
                try
                {
                    string enabledFile = disabledFile.Substring(0, disabledFile.Length - ".yucp_disabled".Length);
                    
                    if (!File.Exists(enabledFile))
                        continue; // No conflict
                    
                    // Create backup before any operation
                    BackupFile(disabledFile);
                    BackupFile(enabledFile);
                    
                    // Determine operation based on multiple heuristics
                    var decision = DetermineConflictResolution(disabledFile, enabledFile);
                    
                    operations.Add(new FileOperation
                    {
                        DisabledFile = disabledFile,
                        EnabledFile = enabledFile,
                        Decision = decision
                    });
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[YUCP Guardian] Error analyzing '{Path.GetFileName(disabledFile)}': {ex.Message}");
                }
            }
            
            // Execute operations with retry logic
            foreach (var op in operations)
            {
                ExecuteFileOperation(op);
            }
        }
        
        private static ConflictDecision DetermineConflictResolution(string disabledFile, string enabledFile)
        {
            var decision = new ConflictDecision();
            int confidence = 0;
            
            // Method 1: File size comparison
            try
            {
                FileInfo disabledInfo = new FileInfo(disabledFile);
                FileInfo enabledInfo = new FileInfo(enabledFile);
                
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
                // Can't compare sizes - be conservative
                decision.IsDuplicate = true;
                confidence += 20;
                decision.Reason += "Size comparison failed (assume duplicate). ";
            }
            
            // Method 2: Content hash comparison (for small files)
            try
            {
                FileInfo disabledInfo = new FileInfo(disabledFile);
                if (disabledInfo.Length < 1024 * 100) // Only hash files < 100KB
                {
                    string disabledHash = ComputeFileHash(disabledFile);
                    string enabledHash = ComputeFileHash(enabledFile);
                    
                    if (disabledHash == enabledHash)
                    {
                        decision.IsDuplicate = true;
                        confidence += 50; // High confidence
                        decision.Reason += "Identical content hash. ";
                    }
                    else
                    {
                        decision.IsUpdate = true;
                        confidence += 40;
                        decision.Reason += "Different content hash. ";
                    }
                }
            }
            catch
            {
                // Hashing failed - continue with other methods
            }
            
            // Method 3: Timestamp comparison
            try
            {
                DateTime disabledTime = File.GetLastWriteTimeUtc(disabledFile);
                DateTime enabledTime = File.GetLastWriteTimeUtc(enabledFile);
                TimeSpan timeDiff = disabledTime - enabledTime;
                
                // If files are very close in time (< 5 seconds), likely duplicate from same export
                if (Math.Abs(timeDiff.TotalSeconds) < 5)
                {
                    decision.IsDuplicate = true;
                    confidence += 20;
                    decision.Reason += "Similar timestamps. ";
                }
                // If disabled is newer, might be an update
                else if (timeDiff.TotalSeconds > 60)
                {
                    decision.IsUpdate = true;
                    confidence += 15;
                    decision.Reason += "Newer timestamp. ";
                }
            }
            catch
            {
                // Timestamp comparison failed
            }
            
            // Method 4: Check package version if available
            try
            {
                var versionInfo = ExtractVersionFromPath(disabledFile);
                if (versionInfo != null)
                {
                    decision.PackageName = versionInfo.Item1;
                    decision.Version = versionInfo.Item2;
                    confidence += 10;
                    decision.Reason += $"Package: {versionInfo.Item1}@{versionInfo.Item2}. ";
                }
            }
            catch
            {
                // Version extraction failed
            }
            
            decision.Confidence = confidence;
            
            // Final decision: If confidence is low, default to duplicate (safer)
            if (confidence < 50 && !decision.IsDuplicate && !decision.IsUpdate)
            {
                decision.IsDuplicate = true;
                decision.Reason += "Low confidence - defaulting to duplicate for safety. ";
            }
            
            return decision;
        }
        
        private static void ExecuteFileOperation(FileOperation op)
        {
            string fileName = Path.GetFileName(op.DisabledFile);
            
            if (op.Decision.IsDuplicate)
            {
                Debug.Log($"[YUCP Guardian] Duplicate: {fileName} - {op.Decision.Reason}");
                
                // Delete the new .yucp_disabled file (keep existing enabled)
                SafeDeleteWithRetry(op.DisabledFile);
                SafeDeleteWithRetry(op.DisabledFile + ".meta");
            }
            else if (op.Decision.IsUpdate)
            {
                Debug.Log($"[YUCP Guardian] Update: {fileName} - {op.Decision.Reason}");
                
                // Delete old enabled file (make room for new)
                SafeDeleteWithRetry(op.EnabledFile);
                SafeDeleteWithRetry(op.EnabledFile + ".meta");
            }
            else
            {
                Debug.LogWarning($"[YUCP Guardian] Unclear: {fileName} - {op.Decision.Reason}. Defaulting to duplicate.");
                
                // Be conservative - delete .yucp_disabled
                SafeDeleteWithRetry(op.DisabledFile);
                SafeDeleteWithRetry(op.DisabledFile + ".meta");
            }
        }
        
        private static bool SafeDeleteWithRetry(string filePath, int maxAttempts = MAX_RETRY_ATTEMPTS)
        {
            if (!File.Exists(filePath))
                return true;
            
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    // Check if file is locked
                    if (IsFileLocked(filePath))
                    {
                        if (attempt < maxAttempts - 1)
                        {
                            Thread.Sleep(RETRY_DELAY_MS * (attempt + 1));
                            continue;
                        }
                        else
                        {
                            Debug.LogWarning($"[YUCP Guardian] File locked after {maxAttempts} attempts: {Path.GetFileName(filePath)}");
                            return false;
                        }
                    }
                    
                    // Attempt deletion
                    File.SetAttributes(filePath, FileAttributes.Normal); // Remove read-only
                    File.Delete(filePath);
                    return true;
                }
                catch (IOException) when (attempt < maxAttempts - 1)
                {
                    Thread.Sleep(RETRY_DELAY_MS * (attempt + 1));
                }
                catch (UnauthorizedAccessException)
                {
                    try
                    {
                        File.SetAttributes(filePath, FileAttributes.Normal);
                        File.Delete(filePath);
                        return true;
                    }
                    catch
                    {
                        Debug.LogWarning($"[YUCP Guardian] Permission denied: {Path.GetFileName(filePath)}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    if (attempt == maxAttempts - 1)
                    {
                        Debug.LogWarning($"[YUCP Guardian] Delete failed: {Path.GetFileName(filePath)} - {ex.Message}");
                        return false;
                    }
                }
            }
            
            return false;
        }
        
        private static bool IsFileLocked(string filePath)
        {
            if (!File.Exists(filePath))
                return false;
            
            try
            {
                using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    stream.Close();
                }
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        private static string ComputeFileHash(string filePath)
        {
            try
            {
                using (var md5 = MD5.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch
            {
                return null;
            }
        }
        
        private static void BackupFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath) && !fileBackups.ContainsKey(filePath))
                {
                    FileInfo info = new FileInfo(filePath);
                    if (info.Length < 1024 * 1024) // Only backup files < 1MB
                    {
                        fileBackups[filePath] = File.ReadAllBytes(filePath);
                    }
                }
            }
            catch
            {
                // Backup failed - continue anyway
            }
        }
        
        private static void RollbackTransaction()
        {
            try
            {
                foreach (var backup in fileBackups)
                {
                    try
                    {
                        File.WriteAllBytes(backup.Key, backup.Value);
                        Debug.Log($"[YUCP Guardian] Restored: {Path.GetFileName(backup.Key)}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[YUCP Guardian] Rollback failed for {Path.GetFileName(backup.Key)}: {ex.Message}");
                    }
                }
                
                if (fileBackups.Count > 0)
                {
                    AssetDatabase.Refresh();
                    Debug.Log($"[YUCP Guardian] Rolled back {fileBackups.Count} files");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Guardian] Rollback error: {ex.Message}");
            }
        }
        
        private static void CheckForMigration()
        {
            string packagesPath = GetPackagesPath();
            string yucpComponentsPath = Path.Combine(packagesPath, "com.yucp.components");
            string standaloneGuardianPath = Path.Combine(packagesPath, "yucp.packageguardian");
            
            if (SafeDirectoryExists(yucpComponentsPath) && SafeDirectoryExists(standaloneGuardianPath))
            {
                Debug.Log("[YUCP Guardian] Migrating to com.yucp.components...");
                
                if (SafeDeleteDirectory(standaloneGuardianPath))
                {
                    Debug.Log("[YUCP Guardian] Migration complete");
                    AssetDatabase.Refresh();
                }
            }
        }
        
        private static void CleanupDuplicateGuardians()
        {
            string editorPath = Path.Combine(Application.dataPath, "Editor");
            string packagesPath = GetPackagesPath();
            string yucpComponentsPath = Path.Combine(packagesPath, "com.yucp.components");
            string standaloneGuardianPath = Path.Combine(packagesPath, "yucp.packageguardian");
            
            // If com.yucp.components exists, remove standalone
            if (SafeDirectoryExists(yucpComponentsPath) && SafeDirectoryExists(standaloneGuardianPath))
            {
                SafeDeleteDirectory(standaloneGuardianPath);
            }
            
            // Clean old-style guardians in Assets/Editor
            if (SafeDirectoryExists(editorPath))
            {
                string[] guardianFiles = SafeFindFiles(editorPath, "YUCP_Guardian_*.cs", SearchOption.TopDirectoryOnly);
                
                if (guardianFiles.Length > 1)
                {
                    var ordered = guardianFiles.OrderByDescending(f => SafeGetFileTime(f)).ToArray();
                    
                    for (int i = 1; i < ordered.Length; i++)
                    {
                        SafeDeleteWithRetry(ordered[i]);
                        SafeDeleteWithRetry(ordered[i] + ".meta");
                    }
                }
            }
        }
        
        private static void CleanupDuplicateInstallers()
        {
            string editorPath = Path.Combine(Application.dataPath, "Editor");
            if (!SafeDirectoryExists(editorPath))
                return;
            
            string[] installerCs = SafeFindFiles(editorPath, "YUCP_Installer_*.cs", SearchOption.TopDirectoryOnly);
            string[] installerAsmdef = SafeFindFiles(editorPath, "YUCP_Installer_*.asmdef", SearchOption.TopDirectoryOnly);
            
            // Keep only newest of each
            CleanupDuplicatesByPattern(installerCs, "installer script");
            CleanupDuplicatesByPattern(installerAsmdef, "installer asmdef");
        }
        
        private static void CleanupDuplicatesByPattern(string[] files, string fileType)
        {
            if (files.Length <= 1)
                return;
            
            var ordered = files.OrderByDescending(f => SafeGetFileTime(f)).ToArray();
            int deletedCount = 0;
            
            for (int i = 1; i < ordered.Length; i++)
            {
                if (SafeDeleteWithRetry(ordered[i]))
                {
                    SafeDeleteWithRetry(ordered[i] + ".meta");
                    deletedCount++;
                }
            }
            
            if (deletedCount > 0)
            {
                Debug.Log($"[YUCP Guardian] Removed {deletedCount} duplicate {fileType}(s)");
            }
        }
        
        private static void VerifyProjectIntegrity()
        {
            // Check for orphaned .yucp_disabled files
            string packagesPath = GetPackagesPath();
            string[] disabledFiles = SafeFindFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
            int orphanedCount = 0;
            
            foreach (string disabledFile in disabledFiles)
            {
                // Check if file belongs to a valid package
                if (IsOrphanedFile(disabledFile))
                {
                    if (SafeDeleteWithRetry(disabledFile))
                    {
                        SafeDeleteWithRetry(disabledFile + ".meta");
                        orphanedCount++;
                    }
                }
                
                // Check if enabled version exists (orphaned disabled file)
                string enabledFile = disabledFile.Substring(0, disabledFile.Length - ".yucp_disabled".Length);
                if (File.Exists(enabledFile))
                {
                    if (SafeDeleteWithRetry(disabledFile))
                    {
                        SafeDeleteWithRetry(disabledFile + ".meta");
                        orphanedCount++;
                    }
                }
            }
            
            if (orphanedCount > 0)
            {
                Debug.Log($"[YUCP Guardian] Removed {orphanedCount} orphaned files");
            }
        }
        
        private static bool IsOrphanedFile(string filePath)
        {
            try
            {
                string packagesPath = GetPackagesPath();
                string currentDir = Path.GetDirectoryName(filePath);
                
                // Walk up looking for package.json
                while (!string.IsNullOrEmpty(currentDir) && 
                       currentDir.StartsWith(packagesPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(Path.Combine(currentDir, "package.json")))
                        return false; // Found package.json, not orphaned
                    
                    string parentDir = Path.GetDirectoryName(currentDir);
                    if (parentDir == currentDir)
                        break;
                    
                    currentDir = parentDir;
                }
                
                return true; // No package.json found
            }
            catch
            {
                return false; // Can't determine, assume not orphaned
            }
        }
        
        private static void ValidateManifests()
        {
            // Validate vpm-manifest.json
            string vpmManifestPath = Path.Combine(GetPackagesPath(), "vpm-manifest.json");
            
            if (File.Exists(vpmManifestPath))
            {
                try
                {
                    string content = File.ReadAllText(vpmManifestPath);
                    
                    // Basic JSON validation
                    if (!content.TrimStart().StartsWith("{") || !content.TrimEnd().EndsWith("}"))
                    {
                        Debug.LogWarning("[YUCP Guardian] vpm-manifest.json appears corrupted");
                        BackupAndRepairManifest(vpmManifestPath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[YUCP Guardian] Error validating vpm-manifest: {ex.Message}");
                }
            }
        }
        
        private static void BackupAndRepairManifest(string manifestPath)
        {
            try
            {
                string backupPath = manifestPath + ".backup_" + DateTime.Now.Ticks;
                File.Copy(manifestPath, backupPath, true);
                Debug.Log($"[YUCP Guardian] Created backup: {Path.GetFileName(backupPath)}");
                
                // Try to repair by creating minimal valid manifest
                string minimalManifest = @"{
  ""dependencies"": {},
  ""locked"": {}
}";
                File.WriteAllText(manifestPath, minimalManifest);
                Debug.Log("[YUCP Guardian] Reset vpm-manifest.json to minimal valid state");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Guardian] Manifest repair failed: {ex.Message}");
            }
        }
        
        private static void PerformStartupCleanup()
        {
            // CRITICAL: Clean up duplicate installers FIRST to prevent compilation errors
            CleanupDuplicateInstallers();
            
            // Clean up stale temp files
            string[] tempFiles = SafeFindFiles(Application.dataPath, "YUCP_TempInstall_*.json", SearchOption.TopDirectoryOnly);
            int cleanedCount = 0;
            
            foreach (string file in tempFiles)
            {
                try
                {
                    DateTime fileTime = File.GetLastWriteTime(file);
                    if ((DateTime.Now - fileTime).TotalMinutes > 5) // Older than 5 minutes
                    {
                        if (SafeDeleteWithRetry(file))
                        {
                            SafeDeleteWithRetry(file + ".meta");
                            cleanedCount++;
                        }
                    }
                }
                catch
                {
                    // Continue with other files
                }
            }
            
            if (cleanedCount > 0)
            {
                Debug.Log($"[YUCP Guardian] Startup: Cleaned {cleanedCount} stale temp files");
            }
        }
        
        private static void RecoverFromCrash()
        {
            // Check if there's a saved transaction state (indicates previous crash)
            string statePath = Path.Combine(Application.temporaryCachePath, STATE_FILE);
            
            if (File.Exists(statePath))
            {
                Debug.LogWarning("[YUCP Guardian] Detected incomplete transaction from previous session");
                
                try
                {
                    // Try to parse state
                    string stateJson = File.ReadAllText(statePath);
                    
                    Debug.LogWarning("[YUCP Guardian] Running recovery cleanup...");
                    EmergencyRecovery();
                    
                    File.Delete(statePath);
                    Debug.Log("[YUCP Guardian] Recovery complete");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[YUCP Guardian] Crash recovery failed: {ex.Message}");
                }
            }
        }
        
        private static void SaveTransactionState()
        {
            try
            {
                string statePath = Path.Combine(Application.temporaryCachePath, STATE_FILE);
                string stateJson = $"{{\"startTime\":\"{currentTransaction.StartTime:O}\"}}";
                File.WriteAllText(statePath, stateJson);
            }
            catch
            {
                // State save failed - continue anyway
            }
        }
        
        private static void DeleteTransactionState()
        {
            try
            {
                string statePath = Path.Combine(Application.temporaryCachePath, STATE_FILE);
                if (File.Exists(statePath))
                    File.Delete(statePath);
            }
            catch
            {
                // Ignore
            }
        }
        
        private static void EmergencyRecovery()
        {
            Debug.LogWarning("[YUCP Guardian] Emergency Recovery Mode");
            
            int recoveredFiles = 0;
            var errors = new List<string>();
            
            // 1. Delete all .yucp_disabled files
            try
            {
                string packagesPath = GetPackagesPath();
                string[] disabledFiles = SafeFindFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
                
                foreach (string file in disabledFiles)
                {
                    if (SafeDeleteWithRetry(file, 1))
                    {
                        SafeDeleteWithRetry(file + ".meta", 1);
                        recoveredFiles++;
                    }
                    else
                    {
                        errors.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Guardian] Recovery step 1 failed: {ex.Message}");
            }
            
            // 2. Delete all temp JSON files
            try
            {
                string[] tempFiles = SafeFindFiles(Application.dataPath, "YUCP_TempInstall_*.json", SearchOption.TopDirectoryOnly);
                
                foreach (string file in tempFiles)
                {
                    if (SafeDeleteWithRetry(file, 1))
                    {
                        SafeDeleteWithRetry(file + ".meta", 1);
                        recoveredFiles++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Guardian] Recovery step 2 failed: {ex.Message}");
            }
            
            // 3. Clean up all installer files
            try
            {
                string editorPath = Path.Combine(Application.dataPath, "Editor");
                if (SafeDirectoryExists(editorPath))
                {
                    string[] installers = SafeFindFiles(editorPath, "YUCP_Installer_*", SearchOption.TopDirectoryOnly);
                    
                    foreach (string file in installers)
                    {
                        if (SafeDeleteWithRetry(file, 1))
                        {
                            SafeDeleteWithRetry(file + ".meta", 1);
                            recoveredFiles++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Guardian] Recovery step 3 failed: {ex.Message}");
            }
            
            // 4. Force AssetDatabase refresh
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            catch
            {
                try
                {
                    AssetDatabase.Refresh();
                }
                catch
                {
                    Debug.LogError("[YUCP Guardian] Cannot refresh AssetDatabase");
                }
            }
            
            // 5. Report results
            if (recoveredFiles > 0)
            {
                Debug.LogWarning($"[YUCP Guardian] Recovery: Removed {recoveredFiles} files");
            }
            
            if (errors.Count > 0)
            {
                Debug.LogError($"[YUCP Guardian] {errors.Count} files could not be auto-removed:");
                foreach (string error in errors.Take(5))
                {
                    Debug.LogError($"  - {error}");
                }
                if (errors.Count > 5)
                {
                    Debug.LogError($"  ... and {errors.Count - 5} more");
                }
                Debug.LogError("[YUCP Guardian] Manual cleanup required. Please delete these files manually.");
            }
        }
        
        // Safe wrapper methods with error handling
        
        private static string GetPackagesPath()
        {
            try
            {
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages"));
            }
            catch
            {
                return Path.Combine(Application.dataPath, "..", "Packages");
            }
        }
        
        private static bool SafeDirectoryExists(string path)
        {
            try
            {
                return !string.IsNullOrEmpty(path) && Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }
        
        private static bool SafeDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                    return true;
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP Guardian] Failed to delete directory {Path.GetFileName(path)}: {ex.Message}");
                return false;
            }
        }
        
        private static string[] SafeFindFiles(string path, string pattern, SearchOption option)
        {
            try
            {
                if (!Directory.Exists(path))
                    return new string[0];
                
                return Directory.GetFiles(path, pattern, option);
            }
            catch (UnauthorizedAccessException)
            {
                Debug.LogWarning($"[YUCP Guardian] Access denied to directory: {path}");
                return new string[0];
            }
            catch (PathTooLongException)
            {
                Debug.LogWarning($"[YUCP Guardian] Path too long: {path}");
                return new string[0];
            }
            catch
            {
                return new string[0];
            }
        }
        
        private static DateTime SafeGetFileTime(string filePath)
        {
            try
            {
                return File.GetLastWriteTimeUtc(filePath);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }
        
        private static Tuple<string, string> ExtractVersionFromPath(string filePath)
        {
            try
            {
                // Try to extract package name from path
                string packagesPath = GetPackagesPath();
                string relativePath = filePath.Replace(packagesPath, "").TrimStart('\\', '/');
                string[] parts = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                
                if (parts.Length > 0)
                {
                    string packageName = parts[0];
                    string packageJsonPath = Path.Combine(packagesPath, packageName, "package.json");
                    
                    if (File.Exists(packageJsonPath))
                    {
                        string json = File.ReadAllText(packageJsonPath);
                        var versionMatch = System.Text.RegularExpressions.Regex.Match(json, @"""version""\s*:\s*""([^""]+)""");
                        
                        if (versionMatch.Success)
                        {
                            return new Tuple<string, string>(packageName, versionMatch.Groups[1].Value);
                        }
                    }
                }
            }
            catch
            {
                // Extraction failed
            }
            
            return null;
        }
        
        private static void LogError(string operation, Exception ex)
        {
            Debug.LogError($"[YUCP Guardian] {operation} failed: {ex.GetType().Name}: {ex.Message}");
            
            if (ex.InnerException != null)
            {
                Debug.LogError($"[YUCP Guardian] Inner: {ex.InnerException.Message}");
            }
        }
        
        // Data structures
        
        private class ImportTransaction
        {
            public DateTime StartTime;
            public bool Success;
            public List<string> ModifiedFiles = new List<string>();
            public List<string> DeletedFiles = new List<string>();
        }
        
        private class FileOperation
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
            public string PackageName;
            public string Version;
        }
        
        // Menu items for manual recovery
        
        [MenuItem("Tools/YUCP/Guardian - Manual Cleanup")]
        public static void ManualCleanup()
        {
            if (EditorUtility.DisplayDialog(
                "YUCP Guardian Manual Cleanup",
                "This will remove all .yucp_disabled files, temp files, and duplicate installers.\n\n" +
                "Use this if automatic import protection failed.\n\nContinue?",
                "Yes, Clean Up",
                "Cancel"))
            {
                EmergencyRecovery();
                Debug.Log("[YUCP Guardian] Manual cleanup complete");
            }
        }
        
        [MenuItem("Tools/YUCP/Guardian - Reset Circuit Breaker")]
        public static void ResetCircuitBreaker()
        {
            consecutiveFailures = 0;
            Debug.Log("[YUCP Guardian] Circuit breaker reset. Protection re-enabled.");
        }
        
        [MenuItem("Tools/YUCP/Guardian - Show Status")]
        public static void ShowStatus()
        {
            string packagesPath = GetPackagesPath();
            string[] disabledFiles = Directory.GetFiles(packagesPath, "*.yucp_disabled", SearchOption.AllDirectories);
            string[] tempFiles = Directory.GetFiles(Application.dataPath, "YUCP_TempInstall_*.json", SearchOption.TopDirectoryOnly);
            
            string editorPath = Path.Combine(Application.dataPath, "Editor");
            string[] installers = Directory.Exists(editorPath) 
                ? Directory.GetFiles(editorPath, "YUCP_Installer_*", SearchOption.TopDirectoryOnly) 
                : new string[0];
            
            string[] guardians = Directory.Exists(editorPath)
                ? Directory.GetFiles(editorPath, "YUCP_Guardian_*", SearchOption.TopDirectoryOnly)
                : new string[0];
            
            bool hasComponentsPackage = Directory.Exists(Path.Combine(packagesPath, "com.yucp.components"));
            bool hasStandaloneGuardian = Directory.Exists(Path.Combine(packagesPath, "yucp.packageguardian"));
            
            string status = $"YUCP Guardian Status:\n\n" +
                           $"Circuit Breaker: {(IsCircuitBroken() ? "ACTIVE (protection disabled)" : "OK")}\n" +
                           $"Consecutive Failures: {consecutiveFailures}/{MAX_CONSECUTIVE_FAILURES}\n\n" +
                           $"Files:\n" +
                           $"  .yucp_disabled files: {disabledFiles.Length}\n" +
                           $"  Temp JSON files: {tempFiles.Length}\n" +
                           $"  Installer scripts: {installers.Length}\n" +
                           $"  Guardian scripts: {guardians.Length}\n\n" +
                           $"Packages:\n" +
                           $"  com.yucp.components: {(hasComponentsPackage ? "Installed" : "Not Installed")}\n" +
                           $"  yucp.packageguardian: {(hasStandaloneGuardian ? "Installed" : "Not Installed")}\n\n" +
                           $"Recommendations:\n";
            
            if (disabledFiles.Length > 0)
                status += "  • .yucp_disabled files found - may indicate incomplete import\n";
            if (tempFiles.Length > 0)
                status += "  • Temp files found - may indicate failed import\n";
            if (installers.Length > 1)
                status += "  • Multiple installers found - duplicates should be cleaned\n";
            if (guardians.Length > 1)
                status += "  • Multiple guardians found - duplicates should be cleaned\n";
            if (hasComponentsPackage && hasStandaloneGuardian)
                status += "  • Both guardian versions present - standalone should migrate\n";
            
            if (disabledFiles.Length == 0 && tempFiles.Length == 0 && installers.Length <= 1 && guardians.Length <= 1)
                status += "  ✓ All clear - no issues detected\n";
            
            EditorUtility.DisplayDialog("YUCP Guardian Status", status, "OK");
        }
        
        // Helper methods
        
        private static void SafeExecute(string operationName, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogError(operationName, ex);
            }
        }
    }
}
