using System;
using System.Collections.Generic;
using UnityEngine;
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
                    return _validator.ValidateProject();
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
        /// Create a snapshot with default author from settings.
        /// </summary>
        /// <param name="message">Commit message</param>
        /// <param name="validateFirst">If true, validates changes and shows warnings before creating snapshot</param>
        /// <returns>Commit ID if successful, null if cancelled by user</returns>
        public string CreateSnapshot(string message, bool validateFirst = true)
        {
            var settings = Settings.PackageGuardianSettings.Instance;
            string author = $"{settings.authorName} <{settings.authorEmail}>";
            
            try
            {
                // Validate if requested
                if (validateFirst)
                {
                    UnityEditor.EditorUtility.DisplayProgressBar("Package Guardian", "Validating changes...", 0f);
                    
                    var projectIssues = ValidateProject();
                    var changeIssues = ValidatePendingChanges();
                    var allIssues = new List<ValidationIssue>();
                    allIssues.AddRange(projectIssues);
                    allIssues.AddRange(changeIssues);
                    
                    // Check for errors or critical issues
                    var criticalIssues = allIssues.FindAll(i => 
                        i.Severity == IssueSeverity.Error || i.Severity == IssueSeverity.Critical);
                    
                    if (criticalIssues.Count > 0)
                    {
                        UnityEditor.EditorUtility.ClearProgressBar();
                        
                        string issueList = string.Join("\n\n", criticalIssues.ConvertAll(i => 
                            $"[{i.Severity}] {i.Title}\n{i.Description}"));
                        
                        bool proceed = UnityEditor.EditorUtility.DisplayDialog(
                            "Package Guardian - Validation Errors",
                            $"Found {criticalIssues.Count} critical issue(s):\n\n{issueList}\n\nDo you want to create the snapshot anyway?",
                            "Create Anyway",
                            "Cancel"
                        );
                        
                        if (!proceed)
                            return null;
                    }
                    else if (allIssues.Count > 0)
                    {
                        // Show warnings
                        var warnings = allIssues.FindAll(i => i.Severity == IssueSeverity.Warning);
                        if (warnings.Count > 0)
                        {
                            UnityEditor.EditorUtility.ClearProgressBar();
                            
                            string warningList = string.Join("\n\n", warnings.ConvertAll(w => 
                                $"{w.Title}\n{w.Description}").ToArray());
                            
                            bool proceed = UnityEditor.EditorUtility.DisplayDialog(
                                "Package Guardian - Warnings",
                                $"Found {warnings.Count} warning(s):\n\n{warningList}\n\nContinue?",
                                "Continue",
                                "Cancel"
                            );
                            
                            if (!proceed)
                                return null;
                        }
                    }
                }
                
                UnityEditor.EditorUtility.DisplayProgressBar("Package Guardian", "Creating snapshot...", 0f);
                
                // Create snapshot
                string commitId = _repository.CreateSnapshot(message, author);
                
                UnityEditor.EditorUtility.DisplayProgressBar("Package Guardian", "Finalizing...", 1f);
                
                return commitId;
            }
            finally
            {
                UnityEditor.EditorUtility.ClearProgressBar();
            }
        }
        
        /// <summary>
        /// Create an auto-stash with default author from settings.
        /// </summary>
        public string CreateAutoStash(string message)
        {
            var settings = Settings.PackageGuardianSettings.Instance;
            string author = $"{settings.authorName} <{settings.authorEmail}>";
            
            return _repository.Stash.CreateAutoStash(message, author);
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
    }
}

