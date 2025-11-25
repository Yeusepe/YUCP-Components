using System;
using System.Threading.Tasks;
using UnityEngine;
using YUCP.Components.PackageGuardian.Editor.Services;

namespace YUCP.Components.PackageGuardian.Editor.Integration.ImportMonitor
{
    /// <summary>
    /// Transaction scope that automatically creates stashes on enter and can optionally snapshot on exit.
    /// Use with 'using' statement for automatic disposal.
    /// </summary>
    public sealed class ImportTransactionScope : IDisposable
    {
        private readonly string _reason;
        private readonly string _description;
        private readonly bool _snapshotOnExit;
        private bool _disposed;
        private Task<string> _preStashTask;
        
        /// <summary>
        /// Begin an import transaction.
        /// </summary>
        /// <param name="reason">Reason for the transaction (Import, UPM Add, UPM Remove, UPM Update)</param>
        /// <param name="description">Detailed description</param>
        /// <param name="snapshotOnExit">Whether to create a post-snapshot on exit</param>
        public ImportTransactionScope(string reason, string description, bool snapshotOnExit = false)
        {
            _reason = reason ?? throw new ArgumentNullException(nameof(reason));
            _description = description ?? string.Empty;
            _snapshotOnExit = snapshotOnExit;
            
            // Create pre-import stash
            CreatePreStash();
        }
        
        private void CreatePreStash()
        {
            try
            {
                var service = RepositoryService.Instance;
                string message = $"Auto-stash before {_reason}: {_description}";
                
                _preStashTask = service.CreateAutoStashAsync(message);
                _preStashTask.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Debug.LogWarning($"[Package Guardian] Failed to queue pre-stash: {t.Exception?.GetBaseException().Message}");
                    }
                    else
                    {
                        Debug.Log($"[Package Guardian] Queued auto-stash before {_reason}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Package Guardian] Failed to create pre-stash: {ex.Message}");
            }
        }
        
        private void CreatePostSnapshot()
        {
            if (!_snapshotOnExit)
                return;
            
            try
            {
                var service = RepositoryService.Instance;
                string message = $"Post-{_reason} snapshot: {_description}";
                
                _ = service.CreateSnapshotAsync(message, validateFirst: false).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Debug.LogWarning($"[Package Guardian] Failed to queue post-snapshot: {t.Exception?.GetBaseException().Message}");
                    }
                    else
                    {
                        Debug.Log($"[Package Guardian] Queued post-snapshot for {_reason}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Package Guardian] Failed to create post-snapshot: {ex.Message}");
            }
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
            
            // Create post-snapshot if requested
            CreatePostSnapshot();
            
            _disposed = true;
        }
    }
}

