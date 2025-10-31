using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using YUCP.Components.PackageGuardian.Editor.Services;
using YUCP.Components.PackageGuardian.Editor.Settings;

namespace YUCP.Components.PackageGuardian.Editor.Integration.ImportMonitor
{
    /// <summary>
    /// Global import monitor for YUCP packages integrated with Package Guardian.
    /// Automatically creates stashes on asset imports and UPM events.
    /// </summary>
    [InitializeOnLoad]
    public class PackageImportMonitor : AssetPostprocessor
    {
        private static readonly ImportEventDebouncer _debouncer = new ImportEventDebouncer(0.5f);
        private static bool _isInitialized = false;
        
        static PackageImportMonitor()
        {
            // Delay initialization to avoid issues during Unity startup
            EditorApplication.delayCall += Initialize;
        }
        
        private static void Initialize()
        {
            if (_isInitialized)
                return;
            
            _isInitialized = true;
            
            Debug.Log("[Package Guardian] Import Monitor initialized");
            
            // Hook Unity Package Manager events
            #if UNITY_2020_1_OR_NEWER
            UnityEditor.PackageManager.Events.registeredPackages += OnPackagesRegistered;
            #endif
            
            // Check for standalone guardian and offer migration
            CheckForStandaloneGuardian();
        }
        
        /// <summary>
        /// Called when assets are imported, deleted, or moved.
        /// </summary>
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            // Skip during compilation or updating
            if (UnityEditor.EditorApplication.isUpdating || UnityEditor.EditorApplication.isCompiling)
                return;
            
            // Try to get settings, but don't fail if unavailable
            PackageGuardianSettings settings = null;
            try
            {
                settings = PackageGuardianSettings.Instance;
            }
            catch
            {
                // Settings not available during asset import, skip
                return;
            }
            
            // Only auto-stash if enabled in settings
            if (settings == null || !settings.autoSnapshotOnSave)
                return;
            
            // Check if there are significant changes
            int totalChanges = importedAssets.Length + deletedAssets.Length + movedAssets.Length;
            if (totalChanges == 0)
                return;
            
            // Filter out meta files and ignored assets
            var significantImports = importedAssets.Where(IsSignificantAsset).ToArray();
            var significantDeletes = deletedAssets.Where(IsSignificantAsset).ToArray();
            var significantMoves = movedAssets.Where(IsSignificantAsset).ToArray();
            
            int significantChanges = significantImports.Length + significantDeletes.Length + significantMoves.Length;
            if (significantChanges == 0)
                return;
            
            // Debounce rapid changes
            _debouncer.QueueEvent(importedAssets, () =>
            {
                CreateImportStash(significantImports, significantDeletes, significantMoves);
            });
        }
        
        private static bool IsSignificantAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return false;
            
            // Ignore meta files
            if (assetPath.EndsWith(".meta"))
                return false;
            
            // Ignore .pg directory
            if (assetPath.StartsWith(".pg/") || assetPath.Contains("/.pg/"))
                return false;
            
            // Ignore Unity temp files
            if (assetPath.StartsWith("Temp/") || assetPath.StartsWith("Library/"))
                return false;
            
            return true;
        }
        
        private static void CreateImportStash(string[] imported, string[] deleted, string[] moved)
        {
            try
            {
                int totalChanges = imported.Length + deleted.Length + moved.Length;
                string description = $"{totalChanges} file(s) changed";
                
                if (imported.Length > 0)
                    description += $", {imported.Length} imported";
                if (deleted.Length > 0)
                    description += $", {deleted.Length} deleted";
                if (moved.Length > 0)
                    description += $", {moved.Length} moved";
                
                using (var scope = new ImportTransactionScope("Import", description))
                {
                    // Stash is created automatically on scope creation
                    // Scope will dispose automatically at end of using block
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Package Guardian] Failed to create import stash: {ex.Message}");
            }
        }
        
        #if UNITY_2020_1_OR_NEWER
        private static void OnPackagesRegistered(UnityEditor.PackageManager.PackageRegistrationEventArgs args)
        {
            Debug.Log("[Package Guardian] OnPackagesRegistered called");
            
            PackageGuardianSettings settings = null;
            try
            {
                settings = PackageGuardianSettings.Instance;
                Debug.Log($"[Package Guardian] Settings loaded - autoSnapshotOnUPM: {settings.autoSnapshotOnUPM}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Package Guardian] Failed to load settings: {ex.Message}");
                return;
            }
            
            // Only auto-stash if enabled in settings
            if (!settings.autoSnapshotOnUPM)
            {
                Debug.Log("[Package Guardian] Auto-snapshot on UPM is disabled");
                return;
            }
            
            try
            {
                int addedCount = args.added != null ? args.added.Count : 0;
                int removedCount = args.removed != null ? args.removed.Count : 0;
                int changedCount = args.changedFrom != null && args.changedTo != null ? 1 : 0;
                
                Debug.Log($"[Package Guardian] Package changes - Added: {addedCount}, Removed: {removedCount}, Changed: {changedCount}");
                
                if (addedCount == 0 && removedCount == 0 && changedCount == 0)
                {
                    Debug.Log("[Package Guardian] No package changes detected");
                    return;
                }
                
                // Validate package changes
                ValidatePackageChanges(args);
                
                string description = "";
                string reason = "UPM";
                
                if (addedCount > 0)
                {
                    reason = "UPM Add";
                    var names = new List<string>();
                    foreach (var pkg in args.added)
                    {
                        names.Add(pkg.name);
                        Debug.Log($"[Package Guardian] Added package: {pkg.name} v{pkg.version}");
                    }
                    var packageNames = string.Join(", ", names);
                    description = $"Added {addedCount} package(s): {packageNames}";
                }
                else if (removedCount > 0)
                {
                    reason = "UPM Remove";
                    var names = new List<string>();
                    foreach (var pkg in args.removed)
                    {
                        names.Add(pkg.name);
                        Debug.Log($"[Package Guardian] Removed package: {pkg.name} v{pkg.version}");
                    }
                    var packageNames = string.Join(", ", names);
                    description = $"Removed {removedCount} package(s): {packageNames}";
                }
                else if (changedCount > 0)
                {
                    reason = "UPM Update";
                    // changedTo is a collection, get first package name
                    string packageName = "Unknown";
                    if (args.changedTo != null && args.changedTo.Count > 0)
                    {
                        // Get the first changed package
                        var pkg = args.changedTo[0];
                        packageName = pkg.name;
                        Debug.Log($"[Package Guardian] Updated package: {pkg.name} v{pkg.version}");
                    }
                    description = $"Updated package: {packageName}";
                }
                
                Debug.Log($"[Package Guardian] Creating auto-stash: {reason} - {description}");
                
                using (var scope = new ImportTransactionScope(reason, description))
                {
                    // Stash is created automatically
                }
                
                Debug.Log($"[Package Guardian] Auto-stash created successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Failed to create UPM stash: {ex.Message}");
                Debug.LogException(ex);
            }
        }
        
        private static void ValidatePackageChanges(UnityEditor.PackageManager.PackageRegistrationEventArgs args)
        {
            try
            {
                var service = RepositoryService.Instance;
                var issues = service.ValidateProject();
                
                // Filter for package-related issues
                var packageIssues = issues.Where(i => 
                    i.Category == global::PackageGuardian.Core.Validation.IssueCategory.PackageConflict ||
                    i.Category == global::PackageGuardian.Core.Validation.IssueCategory.MissingDependency)
                    .ToList();
                
                // Show warnings for critical package issues
                foreach (var issue in packageIssues)
                {
                    if (issue.Severity == global::PackageGuardian.Core.Validation.IssueSeverity.Error ||
                        issue.Severity == global::PackageGuardian.Core.Validation.IssueSeverity.Critical)
                    {
                        Debug.LogError($"[Package Guardian] {issue.Title}: {issue.Description}");
                        
                        if (!string.IsNullOrEmpty(issue.SuggestedAction))
                        {
                            Debug.LogWarning($"[Package Guardian] Suggested: {issue.SuggestedAction}");
                        }
                    }
                    else if (issue.Severity == global::PackageGuardian.Core.Validation.IssueSeverity.Warning)
                    {
                        Debug.LogWarning($"[Package Guardian] {issue.Title}: {issue.Description}");
                    }
                }
                
                // If there are critical issues, show a dialog
                var criticalIssues = packageIssues.Where(i => 
                    i.Severity == global::PackageGuardian.Core.Validation.IssueSeverity.Critical).ToList();
                
                if (criticalIssues.Count > 0)
                {
                    string message = string.Join("\n\n", criticalIssues.Select(i => 
                        $"{i.Title}\n{i.Description}").ToArray());
                    
                    EditorApplication.delayCall += () =>
                    {
                        EditorUtility.DisplayDialog(
                            "Package Guardian - Critical Package Issues",
                            $"Detected {criticalIssues.Count} critical package issue(s):\n\n{message}\n\nCheck the Console and Health & Safety window for details.",
                            "OK"
                        );
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Package Guardian] Failed to validate package changes: {ex.Message}");
            }
        }
        #endif
        
        private static void CheckForStandaloneGuardian()
        {
            try
            {
                string standaloneGuardianPath = System.IO.Path.Combine(Application.dataPath, "..", "Packages", "yucp.packageguardian");
                
                if (System.IO.Directory.Exists(standaloneGuardianPath))
                {
                    Debug.LogWarning("[Package Guardian] Found standalone guardian package. Package Guardian now provides this functionality.");
                    Debug.Log("[Package Guardian] Consider removing the standalone yucp.packageguardian package.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Package Guardian] Error checking for standalone guardian: {ex.Message}");
            }
        }
    }
}

