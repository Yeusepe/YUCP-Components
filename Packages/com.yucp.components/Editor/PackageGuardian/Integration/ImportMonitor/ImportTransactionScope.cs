using System;
using UnityEngine;
using YUCP.Components.PackageGuardian.Editor.Services;
using YUCP.Components.PackageGuardian.Editor.Settings;

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
        private string _preStashId;
        
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
                
                _preStashId = service.CreateAutoStash(message);
                Debug.Log($"[Package Guardian] Created auto-stash: {_preStashId}");
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
                
                string commitId = service.CreateSnapshot(message);
                Debug.Log($"[Package Guardian] Created post-snapshot: {commitId}");
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

