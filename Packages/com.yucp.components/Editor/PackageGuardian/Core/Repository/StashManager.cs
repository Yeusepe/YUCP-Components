using System;
using System.Collections.Generic;
using System.Linq;
using PackageGuardian.Core.Hashing;
using PackageGuardian.Core.Objects;
using PackageGuardian.Core.Storage;
using PackageGuardian.Core.Diff;

namespace PackageGuardian.Core.Repository
{
    /// <summary>
    /// Manages stashes for temporary snapshots.
    /// </summary>
    public sealed class StashManager
    {
        private readonly Repository _repository;
        private readonly RefDatabase _refs;
        private readonly SnapshotBuilder _snapshots;
        private readonly CheckoutService _checkout;
        private readonly IObjectStore _store;

        public StashManager(Repository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _refs = repository.Refs;
            _snapshots = repository.Snapshots;
            _checkout = new CheckoutService(repository.Root, repository.Store, repository.Hasher, repository.Index);
            _store = repository.Store;
        }

        /// <summary>
        /// Create an auto-stash with timestamp and enhanced metadata.
        /// </summary>
        public string CreateAutoStash(string message, string author)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentNullException(nameof(message));
            if (string.IsNullOrWhiteSpace(author))
                throw new ArgumentNullException(nameof(author));
            
            // Get current HEAD as parent
            string parentCommitId = _refs.ResolveHead();
            
            // Build snapshot
            string commitId = _snapshots.BuildSnapshotCommit(message, author, author, parentCommitId);
            
            // Calculate detailed diff summary for better naming
            string enhancedMessage = message;
            if (!string.IsNullOrEmpty(parentCommitId))
            {
                try
                {
                    var diffEngine = new DiffEngine(_store);
                    var changes = diffEngine.CompareCommits(parentCommitId, commitId);
                    
                    if (changes.Count > 0)
                    {
                        var added = changes.Count(c => c.Type == ChangeType.Added);
                        var modified = changes.Count(c => c.Type == ChangeType.Modified);
                        var deleted = changes.Count(c => c.Type == ChangeType.Deleted);
                        var renamed = changes.Count(c => c.Type == ChangeType.Renamed || c.Type == ChangeType.Copied);
                        
                        // Categorize changes by file type for better insight
                        var packagesAdded = changes.Where(c => c.Type == ChangeType.Added && 
                            (c.Path.Contains("Packages/") || c.Path.Contains("manifest.json"))).ToList();
                        var scenesModified = changes.Where(c => 
                            (c.Type == ChangeType.Modified || c.Type == ChangeType.Added) && 
                            c.Path.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase)).ToList();
                        var prefabsModified = changes.Where(c => 
                            (c.Type == ChangeType.Modified || c.Type == ChangeType.Added) && 
                            c.Path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)).ToList();
                        var scriptsModified = changes.Where(c => 
                            (c.Type == ChangeType.Modified || c.Type == ChangeType.Added) && 
                            (c.Path.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase) || 
                             c.Path.EndsWith(".js", System.StringComparison.OrdinalIgnoreCase))).ToList();
                        var configFiles = changes.Where(c => 
                            c.Type == ChangeType.Modified && (
                            c.Path.Contains("ProjectSettings/") || 
                            c.Path.Contains("manifest.json") ||
                            c.Path.Contains("settings.json"))).ToList();
                        
                        var details = new List<string>();
                        
                        // Always show file counts first
                        if (added > 0) details.Add($"{added}+");
                        if (modified > 0) details.Add($"{modified}~");
                        if (deleted > 0) details.Add($"{deleted}-");
                        if (renamed > 0) details.Add($"{renamed}→");
                        
                        // Add specific notable changes
                        if (packagesAdded.Count > 0)
                            details.Add($"{packagesAdded.Count} pkg" + (packagesAdded.Count > 1 ? "s" : ""));
                        if (scenesModified.Count > 0)
                            details.Add($"{scenesModified.Count} scene" + (scenesModified.Count > 1 ? "s" : ""));
                        if (prefabsModified.Count > 0)
                            details.Add($"{prefabsModified.Count} prefab" + (prefabsModified.Count > 1 ? "s" : ""));
                        if (scriptsModified.Count > 0)
                            details.Add($"{scriptsModified.Count} script" + (scriptsModified.Count > 1 ? "s" : ""));
                        if (configFiles.Count > 0)
                            details.Add("config");
                        
                        if (details.Count > 0)
                        {
                            enhancedMessage = $"{message} [{string.Join(" ", details)}]";
                        }
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[Package Guardian] Failed to calculate stash diff summary: {ex.Message}");
                }
            }
            
            // Create stash ref with timestamp
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string refName = $"refs/stash/auto/{timestamp}";
            
            _refs.UpdateRef(refName, commitId, $"Auto-stash: {enhancedMessage}");
            
            return commitId;
        }

        /// <summary>
        /// List all stashes.
        /// </summary>
        public IReadOnlyList<StashEntry> List()
        {
            var stashes = new List<StashEntry>();
            
            var allRefs = _refs.ListRefs("refs/stash/").ToList();
            UnityEngine.Debug.Log($"[Package Guardian] StashManager.List() found {allRefs.Count} refs under refs/stash/");
            
            foreach (string refName in allRefs)
            {
                UnityEngine.Debug.Log($"[Package Guardian] Processing stash ref: {refName}");
                
                string commitId = _refs.ReadRef(refName);
                if (string.IsNullOrWhiteSpace(commitId))
                {
                    UnityEngine.Debug.LogWarning($"[Package Guardian] Ref {refName} has no commit ID");
                    continue;
                }
                
                UnityEngine.Debug.Log($"[Package Guardian] Ref {refName} -> Commit {commitId}");
                
                try
                {
                    // Read commit to get metadata
                    var commitObj = _store.ReadObject(commitId);
                    if (commitObj is not Commit commit)
                    {
                        UnityEngine.Debug.LogWarning($"[Package Guardian] Object {commitId} is not a Commit");
                        continue;
                    }
                    
                    stashes.Add(new StashEntry(
                        refName,
                        commitId,
                        commit.Timestamp,
                        commit.Message));
                    
                    UnityEngine.Debug.Log($"[Package Guardian] Added stash: {commit.Message}");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[Package Guardian] Failed to read stash {refName}: {ex.Message}");
                    continue;
                }
            }
            
            // Sort by timestamp descending (newest first)
            return stashes.OrderByDescending(s => s.Timestamp).ToList();
        }

        /// <summary>
        /// Drop (delete) a stash.
        /// </summary>
        public void Drop(string refName)
        {
            if (string.IsNullOrWhiteSpace(refName))
                throw new ArgumentNullException(nameof(refName));
            
            if (!_refs.RefExists(refName))
                throw new InvalidOperationException($"Stash not found: {refName}");
            
            _refs.DeleteRef(refName);
        }

        /// <summary>
        /// Apply a stash (restore files from stash commit).
        /// </summary>
        public void Apply(string refName, IEnumerable<string> paths = null, CheckoutOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(refName))
                throw new ArgumentNullException(nameof(refName));
            
            string commitId = _refs.ReadRef(refName);
            if (string.IsNullOrWhiteSpace(commitId))
                throw new InvalidOperationException($"Stash not found: {refName}");
            
            options = options ?? CheckoutOptions.Default;
            
            if (paths == null)
            {
                // Apply full stash
                _checkout.CheckoutCommit(commitId, options);
            }
            else
            {
                // Apply partial stash
                _checkout.CheckoutPaths(commitId, paths, options);
            }
        }

        /// <summary>
        /// Get the commit ID of a stash.
        /// </summary>
        public string GetStashCommit(string refName)
        {
            if (string.IsNullOrWhiteSpace(refName))
                throw new ArgumentNullException(nameof(refName));
            
            return _refs.ReadRef(refName);
        }

        /// <summary>
        /// Create a branch from a stash.
        /// </summary>
        public void ConvertToBranch(string stashRefName, string branchName)
        {
            if (string.IsNullOrWhiteSpace(stashRefName))
                throw new ArgumentNullException(nameof(stashRefName));
            if (string.IsNullOrWhiteSpace(branchName))
                throw new ArgumentNullException(nameof(branchName));
            
            string commitId = _refs.ReadRef(stashRefName);
            if (string.IsNullOrWhiteSpace(commitId))
                throw new InvalidOperationException($"Stash not found: {stashRefName}");
            
            // Create branch ref
            string branchRef = $"refs/heads/{branchName}";
            _refs.UpdateRef(branchRef, commitId, $"Branch from stash: {stashRefName}");
        }
    }
}

