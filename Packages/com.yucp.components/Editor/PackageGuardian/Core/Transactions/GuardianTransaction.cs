using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PackageGuardian.Core.Transactions
{
    /// <summary>
    /// Represents a transaction for Package Guardian operations with rollback support
    /// </summary>
    public class GuardianTransaction : IDisposable
    {
        private readonly List<ITransactionOperation> _operations = new List<ITransactionOperation>();
        private readonly Dictionary<string, byte[]> _fileBackups = new Dictionary<string, byte[]>();
        private bool _committed = false;
        private bool _disposed = false;
        
        public DateTime StartTime { get; private set; }
        public string TransactionId { get; private set; }
        public bool IsActive { get; private set; }
        
        public GuardianTransaction()
        {
            StartTime = DateTime.UtcNow;
            TransactionId = Guid.NewGuid().ToString("N");
            IsActive = true;
        }
        
        /// <summary>
        /// Backs up a file before modification
        /// </summary>
        public void BackupFile(string filePath)
        {
            if (!File.Exists(filePath))
                return;
                
            if (_fileBackups.ContainsKey(filePath))
                return; // Already backed up
                
            try
            {
                byte[] content = File.ReadAllBytes(filePath);
                _fileBackups[filePath] = content;
                Debug.Log($"[Guardian Transaction] Backed up: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Guardian Transaction] Failed to backup {filePath}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Adds an operation to the transaction
        /// </summary>
        public void AddOperation(ITransactionOperation operation)
        {
            if (!IsActive)
                throw new InvalidOperationException("Transaction is not active");
                
            _operations.Add(operation);
        }
        
        /// <summary>
        /// Executes a file operation within the transaction
        /// </summary>
        public void ExecuteFileOperation(string sourcePath, string destPath, FileOperationType operationType)
        {
            var operation = new FileOperation(sourcePath, destPath, operationType);
            AddOperation(operation);
            
            // Backup affected files
            if (operationType == FileOperationType.Move || operationType == FileOperationType.Delete)
            {
                BackupFile(sourcePath);
            }
            if (operationType == FileOperationType.Move || operationType == FileOperationType.Copy)
            {
                BackupFile(destPath);
            }
            
            // Execute immediately
            operation.Execute();
        }
        
        /// <summary>
        /// Commits the transaction
        /// </summary>
        public void Commit()
        {
            if (!IsActive)
                throw new InvalidOperationException("Transaction is not active");
                
            _committed = true;
            IsActive = false;
            
            // Clear backups on successful commit
            _fileBackups.Clear();
            
            Debug.Log($"[Guardian Transaction] Committed transaction {TransactionId} ({_operations.Count} operations)");
        }
        
        /// <summary>
        /// Rolls back the transaction
        /// </summary>
        public void Rollback()
        {
            if (_committed)
            {
                Debug.LogWarning("[Guardian Transaction] Cannot rollback committed transaction");
                return;
            }
            
            Debug.LogWarning($"[Guardian Transaction] Rolling back transaction {TransactionId}...");
            
            int restoredCount = 0;
            int failedCount = 0;
            
            // Restore backed up files
            foreach (var backup in _fileBackups)
            {
                try
                {
                    File.WriteAllBytes(backup.Key, backup.Value);
                    restoredCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Guardian Transaction] Failed to restore {backup.Key}: {ex.Message}");
                    failedCount++;
                }
            }
            
            IsActive = false;
            
            if (failedCount > 0)
            {
                Debug.LogError($"[Guardian Transaction] Rollback completed with errors: {restoredCount} restored, {failedCount} failed");
            }
            else
            {
                Debug.Log($"[Guardian Transaction] Rollback complete: {restoredCount} files restored");
            }
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            // If not committed, rollback
            if (IsActive && !_committed)
            {
                Debug.LogWarning("[Guardian Transaction] Transaction disposed without commit - rolling back");
                Rollback();
            }
            
            _fileBackups.Clear();
            _operations.Clear();
            _disposed = true;
        }
    }
    
    /// <summary>
    /// Interface for transaction operations
    /// </summary>
    public interface ITransactionOperation
    {
        void Execute();
        void Undo();
    }
    
    /// <summary>
    /// File operation types
    /// </summary>
    public enum FileOperationType
    {
        Copy,
        Move,
        Delete,
        Create
    }
    
    /// <summary>
    /// File operation implementation
    /// </summary>
    public class FileOperation : ITransactionOperation
    {
        private readonly string _sourcePath;
        private readonly string _destPath;
        private readonly FileOperationType _operationType;
        private byte[] _originalContent;
        
        public FileOperation(string sourcePath, string destPath, FileOperationType operationType)
        {
            _sourcePath = sourcePath;
            _destPath = destPath;
            _operationType = operationType;
        }
        
        public void Execute()
        {
            switch (_operationType)
            {
                case FileOperationType.Copy:
                    if (File.Exists(_destPath))
                        _originalContent = File.ReadAllBytes(_destPath);
                    File.Copy(_sourcePath, _destPath, true);
                    break;
                    
                case FileOperationType.Move:
                    if (File.Exists(_destPath))
                        _originalContent = File.ReadAllBytes(_destPath);
                    File.Move(_sourcePath, _destPath);
                    break;
                    
                case FileOperationType.Delete:
                    if (File.Exists(_sourcePath))
                    {
                        _originalContent = File.ReadAllBytes(_sourcePath);
                        File.Delete(_sourcePath);
                    }
                    break;
                    
                case FileOperationType.Create:
                    // Content should be provided separately
                    break;
            }
        }
        
        public void Undo()
        {
            // Restore original state
            if (_originalContent != null && !string.IsNullOrEmpty(_destPath))
            {
                File.WriteAllBytes(_destPath, _originalContent);
            }
        }
    }
}

