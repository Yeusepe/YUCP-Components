using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using PackageGuardian.Core.Repository;
using PackageGuardian.Core.Validation;
using PackageGuardian.Core.Diff;

namespace YUCP.Components.PackageGuardian.Editor.Services
{
    /// <summary>
    /// Singleton service that manages the Package Guardian repository instance.
    /// </summary>
    public sealed class RepositoryService
    {
        private static RepositoryService _instance;
        private static readonly object _lock = new object();
        private readonly object _ioLock = new object();
        
        private Repository _repository;
        private readonly string _projectRoot;
        private ProjectValidator _validator;
        
        private RepositoryService()
        {
            // Get Unity project root (parent of Assets)
            _projectRoot = System.IO.Path.GetFullPath(Application.dataPath + "/..");
            
            // Initialize repository if needed
            if (!RepositoryInitializer.IsInitialized(_projectRoot))
            {
                RepositoryInitializer.Initialize(_projectRoot);
            }
            
            // Open repository
            try
            {
                _repository = Repository.Open(_projectRoot);
                _validator = new ProjectValidator(_projectRoot);
                Debug.Log("[Package Guardian] Repository opened successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Failed to open repository: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Get the singleton instance of RepositoryService.
        /// </summary>
        public static RepositoryService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new RepositoryService();
                        }
                    }
                }
                return _instance;
            }
        }
        
        /// <summary>
        /// Get the underlying repository instance.
        /// </summary>
        public Repository Repository
        {
            get
            {
                if (_repository == null)
                    throw new InvalidOperationException("Repository is not initialized");
                return _repository;
            }
        }
        
        /// <summary>
        /// Get the Unity project root path.
        /// </summary>
        public string ProjectRoot => _projectRoot;
        
        /// <summary>
        /// Validate the current project state.
        /// </summary>
        public List<ValidationIssue> ValidateProject()
        {
            return _validator.ValidateProject();
        }
        
        /// <summary>
        /// Validate pending changes before snapshot.
        /// </summary>
        public List<ValidationIssue> ValidatePendingChanges()
        {
            try
            {
                // Get pending changes
                var diffEngine = new DiffEngine(_repository.Store);
                var headCommitId = _repository.Refs.ReadRef("HEAD");
                
                if (string.IsNullOrEmpty(headCommitId))
                {
                    // No HEAD, first commit - validate all files
                    // ValidateProject() uses Unity APIs that require main thread, so skip if on background thread
                    // Check if we're on the main thread (main thread typically has ManagedThreadId == 1)
                    if (System.Threading.Thread.CurrentThread.ManagedThreadId == 1)
                    {
                        try
                        {
                            return _validator.ValidateProject();
                        }
                        catch
                        {
                            // If Unity API calls fail (e.g., not on main thread), return empty list
                            return new List<ValidationIssue>();
                        }
                    }
                    else
                    {
                        // On background thread, return empty list
                        return new List<ValidationIssue>();
                    }
                }
                
                // Compare working directory to HEAD
                var headCommit = _repository.Store.ReadObject(headCommitId) as global::PackageGuardian.Core.Objects.Commit;
                if (headCommit == null)
                    return new List<ValidationIssue>();
                
                string treeId = BitConverter.ToString(headCommit.TreeId).Replace("-", "").ToLower();
                var changes = diffEngine.CompareWorkingDirectory(_projectRoot, treeId, _repository.IgnoreRules);
                
                return _validator.ValidateChanges(changes);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Package Guardian] Failed to validate changes: {ex.Message}");
                return new List<ValidationIssue>();
            }
        }
        
        /// <summary>
        /// Create a snapshot with default author from settings (interactive, runs on main thread).
        /// </summary>
        public string CreateSnapshot(string message, bool validateFirst = true)
        {
            var settings = Settings.PackageGuardianSettings.Instance;
            string author = $"{settings.authorName} <{settings.authorEmail}>";
            var request = BuildSnapshotRequest(message, author, validateFirst);
            
            if (request.ValidateFirst && !RunValidation(interactive: true))
                return null;
            
            return ExecuteSnapshotRequest(request, interactive: true, CancellationToken.None);
        }
        
        /// <summary>
        /// Queue a snapshot to run on the guardian background runner (non-interactive).
        /// </summary>
        public Task<string> CreateSnapshotAsync(string message, bool validateFirst = true, CancellationToken token = default)
        {
            var settings = Settings.PackageGuardianSettings.Instance;
            string author = $"{settings.authorName} <{settings.authorEmail}>";
            var request = BuildSnapshotRequest(message, author, validateFirst);
            
            if (request.ValidateFirst && !RunValidation(interactive: false))
                return Task.FromResult<string>(null);
            
            var job = new SnapshotJob(_repository, request, progress =>
            {
                if (!string.IsNullOrEmpty(progress.Path) && progress.Processed % 250 == 0)
                {
                    Debug.Log($"[Package Guardian] Snapshot progress: {progress.Processed}/{progress.Total} {progress.Path}");
                }
            });
            
            return GuardianTaskRunner.Run($"Snapshot: {message}", ct =>
            {
                ct.ThrowIfCancellationRequested();
                return job.Execute(ct);
            }, token);
        }
        
        /// <summary>
        /// Create an auto-stash with default author from settings.
        /// </summary>
        public string CreateAutoStash(string message)
        {
            var settings = Settings.PackageGuardianSettings.Instance;
            string author = $"{settings.authorName} <{settings.authorEmail}>";
            return CreateAutoStashInternal(message, author, requireLock: true);
        }
        
        /// <summary>
        /// Queue an auto-stash to run on the guardian background runner.
        /// </summary>
        public Task<string> CreateAutoStashAsync(string message, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentNullException(nameof(message));
            
            var settings = Settings.PackageGuardianSettings.Instance;
            string author = $"{settings.authorName} <{settings.authorEmail}>";
            
            return GuardianTaskRunner.Run($"Auto-stash: {message}", ct =>
            {
                ct.ThrowIfCancellationRequested();
                return CreateAutoStashInternal(message, author, requireLock: true);
            }, token);
        }
        
        /// <summary>
        /// Reset the singleton (for testing or recovery).
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _instance = null;
            }
        }

        private SnapshotRequest BuildSnapshotRequest(string message, string author, bool validateFirst)
        {
            var settings = Settings.PackageGuardianSettings.Instance;
            var trackedRoots = settings?.trackedRoots != null && settings.trackedRoots.Count > 0
                ? settings.trackedRoots
                : new List<string> { "Assets", "Packages" };
            
            return new SnapshotRequest(message, author, trackedRoots, validateFirst);
        }
        
        private string ExecuteSnapshotRequest(SnapshotRequest request, bool interactive, CancellationToken token)
        {
            if (interactive)
            {
                UnityEditor.EditorUtility.DisplayProgressBar("Package Guardian", "Creating snapshot...", 0f);
            }

            try
            {
                var options = new SnapshotOptions
                {
                    Committer = request.Author,
                    IncludeRoots = request.TrackedRoots,
                    CancellationToken = token,
                    Progress = interactive
                        ? (processed, total, path) =>
                        {
                            UnityEditor.EditorUtility.DisplayProgressBar("Package Guardian", $"Processing {path}", total == 0 ? 0f : (float)processed / total);
                        }
                        : null
                };
                
                return _repository.CreateSnapshot(request.Message, request.Author, options);
            }
            finally
            {
                if (interactive)
                {
                    UnityEditor.EditorUtility.ClearProgressBar();
                }
            }
        }

        private bool RunValidation(bool interactive)
        {
            var projectIssues = ValidateProject();
            var changeIssues = ValidatePendingChanges();
            var allIssues = new List<ValidationIssue>();
            allIssues.AddRange(projectIssues);
            allIssues.AddRange(changeIssues);

            var criticalIssues = allIssues.FindAll(i =>
                i.Severity == IssueSeverity.Error || i.Severity == IssueSeverity.Critical);

            if (criticalIssues.Count > 0)
            {
                string issueList = string.Join("\n\n", criticalIssues.ConvertAll(i =>
                    $"[{i.Severity}] {i.Title}\n{i.Description}"));

                if (interactive)
                {
                    UnityEditor.EditorUtility.ClearProgressBar();
                    bool proceed = UnityEditor.EditorUtility.DisplayDialog(
                        "Package Guardian - Validation Errors",
                        $"Found {criticalIssues.Count} critical issue(s):\n\n{issueList}\n\nDo you want to create the snapshot anyway?",
                        "Create Anyway",
                        "Cancel"
                    );

                    if (!proceed)
                        return false;
                }
                else
                {
                    Debug.LogWarning($"[Package Guardian] Snapshot aborted due to {criticalIssues.Count} critical issue(s).");
                    return false;
                }
            }
            else if (allIssues.Count > 0)
            {
                var warnings = allIssues.FindAll(i => i.Severity == IssueSeverity.Warning);
                if (warnings.Count > 0)
                {
                    string warningList = string.Join("\n\n", warnings.ConvertAll(w =>
                        $"{w.Title}\n{w.Description}").ToArray());

                    if (interactive)
                    {
                        UnityEditor.EditorUtility.ClearProgressBar();
                        bool proceed = UnityEditor.EditorUtility.DisplayDialog(
                            "Package Guardian - Warnings",
                            $"Found {warnings.Count} warning(s):\n\n{warningList}\n\nContinue?",
                            "Continue",
                            "Cancel"
                        );

                        if (!proceed)
                            return false;
                    }
                    else
                    {
                        Debug.Log($"[Package Guardian] Snapshot warnings: {warnings.Count} issue(s). Continuing.");
                    }
                }
            }

            return true;
        }

        private string CreateAutoStashInternal(string message, string author, bool requireLock)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentNullException(nameof(message));
            if (string.IsNullOrWhiteSpace(author))
                throw new ArgumentNullException(nameof(author));

            if (requireLock)
            {
                lock (_ioLock)
                {
                    return _repository.Stash.CreateAutoStash(message, author);
                }
            }

            return _repository.Stash.CreateAutoStash(message, author);
        }
    }
}

