using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.PackageGuardian.Editor.Services;
using global::PackageGuardian.Core.Diff;
using global::PackageGuardian.Core.Objects;

namespace YUCP.Components.PackageGuardian.Editor.Windows
{
    /// <summary>
    /// Stash manager window for viewing and managing stashes.
    /// </summary>
    public class StashManagerWindow : EditorWindow
    {
        private ScrollView _stashList;
        
        public static void ShowWindow()
        {
            var window = GetWindow<StashManagerWindow>();
            window.titleContent = new GUIContent("Stash Manager");
            window.minSize = new Vector2(600, 400);
            window.Show();
        }
        
        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("pg-window");
            
            // Load stylesheet
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.components/Editor/PackageGuardian/Styles/PackageGuardian.uss");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }
            
            CreateUI();
            Refresh();
        }
        
        private void CreateUI()
        {
            var root = rootVisualElement;
            
            // Header
            var header = new VisualElement();
            header.style.paddingTop = 20;
            header.style.paddingBottom = 20;
            header.style.paddingLeft = 20;
            header.style.paddingRight = 20;
            header.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.16f, 0.16f, 0.16f);
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            
            var titleSection = new VisualElement();
            
            var title = new Label("Stash Manager");
            title.AddToClassList("pg-title");
            title.style.marginBottom = 4;
            titleSection.Add(title);
            
            var subtitle = new Label("View and manage your saved stashes");
            subtitle.AddToClassList("pg-label-secondary");
            titleSection.Add(subtitle);
            
            header.Add(titleSection);
            
            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            
            var cleanupButton = new Button(ShowCleanupDialog);
            cleanupButton.text = "Cleanup Stashes";
            cleanupButton.AddToClassList("pg-button");
            cleanupButton.AddToClassList("pg-button-danger");
            cleanupButton.style.marginRight = 8;
            buttonContainer.Add(cleanupButton);
            
            var refreshButton = new Button(Refresh);
            refreshButton.text = "Refresh";
            refreshButton.AddToClassList("pg-button");
            buttonContainer.Add(refreshButton);
            
            header.Add(buttonContainer);
            
            root.Add(header);
            
            // Content
            var content = new VisualElement();
            content.AddToClassList("pg-panel");
            content.style.flexGrow = 1;
            
            _stashList = new ScrollView();
            _stashList.AddToClassList("pg-scrollview");
            _stashList.style.flexGrow = 1;
            content.Add(_stashList);
            
            root.Add(content);
            
            // Footer
            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.SpaceBetween;
            footer.style.paddingTop = 16;
            footer.style.paddingBottom = 16;
            footer.style.paddingLeft = 16;
            footer.style.paddingRight = 16;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = new Color(0.16f, 0.16f, 0.16f);
            footer.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            
            var infoContainer = new VisualElement();
            infoContainer.style.flexDirection = FlexDirection.Column;
            
            var infoLabel = new Label("Stashes are automatically created before major operations");
            infoLabel.AddToClassList("pg-label-secondary");
            infoContainer.Add(infoLabel);
            
            var countLabel = new Label();
            countLabel.name = "stashCountLabel";
            countLabel.AddToClassList("pg-label-small");
            countLabel.style.marginTop = 4;
            infoContainer.Add(countLabel);
            
            footer.Add(infoContainer);
            
            var closeButton = new Button(Close);
            closeButton.text = "Close";
            closeButton.AddToClassList("pg-button");
            footer.Add(closeButton);
            
            root.Add(footer);
        }
        
        private void Refresh()
        {
            _stashList.Clear();
            
            try
            {
                var service = RepositoryService.Instance;
                var repo = service.Repository;
                
                if (repo == null)
                {
                    Debug.LogWarning("[Package Guardian] Repository is null");
                    var emptyState = CreateEmptyState("Repository Not Initialized", "Please initialize the repository first.");
                    _stashList.Add(emptyState);
                    return;
                }
                
                var stashes = repo.Stash.List();
                
                UpdateStashCount(stashes.Count);
                
                if (stashes.Count == 0)
                {
                    var emptyState = CreateEmptyState("No Stashes Yet", "Stashes will appear here when you create them.");
                    _stashList.Add(emptyState);
                    return;
                }
                
                if (stashes.Count > 100)
                {
                    Debug.Log($"[Package Guardian] Loading {stashes.Count} stashes (showing first 100 in UI for performance)...");
                    // Limit UI display to first 100
                    foreach (var stash in stashes.Take(100))
                    {
                        var stashItem = CreateStashItem(stash);
                        _stashList.Add(stashItem);
                    }
                    
                    var warningLabel = new Label($"Note: Showing first 100 of {stashes.Count} stashes. Use Cleanup to reduce the number of stashes.");
                    warningLabel.style.color = new Color(0.89f, 0.65f, 0.29f);
                    warningLabel.style.paddingTop = 12;
                    warningLabel.style.paddingBottom = 12;
                    warningLabel.style.paddingLeft = 16;
                    warningLabel.style.paddingRight = 16;
                    warningLabel.style.marginTop = 12;
                    warningLabel.style.backgroundColor = new Color(0.2f, 0.15f, 0.1f);
                    warningLabel.style.whiteSpace = WhiteSpace.Normal;
                    _stashList.Add(warningLabel);
                }
                else
                {
                    foreach (var stash in stashes)
                    {
                        var stashItem = CreateStashItem(stash);
                        _stashList.Add(stashItem);
                    }
                }
            }
            catch (Exception ex)
            {
                var errorState = CreateEmptyState("Error Loading Stashes", $"Details: {ex.Message}");
                _stashList.Add(errorState);
                Debug.LogError($"[Package Guardian] Error loading stashes: {ex}");
                Debug.LogException(ex);
            }
        }
        
        private void UpdateStashCount(int count)
        {
            var countLabel = rootVisualElement.Q<Label>("stashCountLabel");
            if (countLabel != null)
            {
                countLabel.text = $"Total stashes: {count}";
                if (count > 100)
                {
                    countLabel.style.color = new Color(0.89f, 0.65f, 0.29f);
                }
                else
                {
                    countLabel.style.color = new Color(0.69f, 0.69f, 0.69f);
                }
            }
        }
        
        private void ShowCleanupDialog()
        {
            try
            {
                var service = RepositoryService.Instance;
                var repo = service.Repository;
                
                if (repo == null)
                {
                    EditorUtility.DisplayDialog("Error", "Repository not initialized.", "OK");
                    return;
                }
                
                int totalStashes = repo.Stash.GetStashCount();
                
                if (totalStashes == 0)
                {
                    EditorUtility.DisplayDialog("Cleanup Stashes", "No stashes to clean up.", "OK");
                    return;
                }
                
                string message = $"You have {totalStashes} stashes. This can slow down Unity.\n\n" +
                              "Choose a cleanup option:\n\n" +
                              "• Keep only recent stashes (recommended)\n" +
                              "• Delete old stashes by age\n\n" +
                              "Note: Deleting stash refs does NOT delete the commit data. " +
                              "The changes are preserved in the repository, but you won't be able to " +
                              "easily access them through the stash system.";
                
                int option = EditorUtility.DisplayDialogComplex(
                    "Cleanup Stashes",
                    message,
                    "Keep Recent",
                    "Delete Old",
                    "Cancel"
                );
                
                if (option == 0) // Keep Recent
                {
                    ShowKeepRecentDialog(totalStashes);
                }
                else if (option == 1) // Delete Old
                {
                    ShowDeleteOldDialog();
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to show cleanup dialog: {ex.Message}", "OK");
                Debug.LogError($"[Package Guardian] Error in cleanup dialog: {ex}");
            }
        }
        
        private void ShowKeepRecentDialog(int totalStashes)
        {
            int input = EditorUtility.DisplayDialogComplex(
                "Keep Recent Stashes",
                $"You have {totalStashes} stashes.\n\n" +
                "How many of the most recent stashes would you like to keep?\n\n" +
                "Recommended: 50-100 stashes",
                "Keep 50",
                "Keep 100",
                "Keep 200"
            );
            
            int keepCount = 0;
            
            if (input == 0) // Keep 50
            {
                keepCount = 50;
            }
            else if (input == 1) // Keep 100
            {
                keepCount = 100;
            }
            else if (input == 2) // Keep 200
            {
                keepCount = 200;
            }
            else
            {
                return;
            }
            
            if (keepCount > totalStashes)
            {
                keepCount = totalStashes;
            }
            
            int toDelete = totalStashes - keepCount;
            
            if (toDelete <= 0)
            {
                EditorUtility.DisplayDialog("Cleanup Stashes", 
                    "No stashes need to be deleted.", "OK");
                return;
            }
            
            if (!EditorUtility.DisplayDialog("Confirm Cleanup",
                $"This will delete {toDelete} old stashes, keeping only the {keepCount} most recent ones.\n\n" +
                "The commit data will be preserved, but you won't be able to access these stashes through the stash manager.\n\n" +
                "Continue?",
                "Delete", "Cancel"))
            {
                return;
            }
            
            PerformCleanupKeepRecent(keepCount);
        }
        
        private void ShowDeleteOldDialog()
        {
            int input = EditorUtility.DisplayDialogComplex(
                "Delete Old Stashes",
                "Delete stashes older than how many days?\n\n" +
                "Recommended: 30-90 days",
                "30 days",
                "90 days",
                "180 days"
            );
            
            int days = 0;
            
            if (input == 0) // 30 days
            {
                days = 30;
            }
            else if (input == 1) // 90 days
            {
                days = 90;
            }
            else if (input == 2) // 180 days
            {
                days = 180;
            }
            else
            {
                return;
            }
            
            try
            {
                var service = RepositoryService.Instance;
                var repo = service.Repository;
                
                var allStashes = repo.Stash.List();
                long cutoffTimestamp = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeSeconds();
                int toDelete = allStashes.Count(s => s.Timestamp < cutoffTimestamp);
                
                if (toDelete == 0)
                {
                    EditorUtility.DisplayDialog("Cleanup Stashes", 
                        $"No stashes are older than {days} days.", "OK");
                    return;
                }
                
                if (!EditorUtility.DisplayDialog("Confirm Cleanup",
                    $"This will delete {toDelete} stashes older than {days} days.\n\n" +
                    "The commit data will be preserved, but you won't be able to access these stashes through the stash manager.\n\n" +
                    "Continue?",
                    "Delete", "Cancel"))
                {
                    return;
                }
                
                PerformCleanupOld(days);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to calculate stashes to delete: {ex.Message}", "OK");
                Debug.LogError($"[Package Guardian] Error in delete old dialog: {ex}");
            }
        }
        
        private void PerformCleanupKeepRecent(int keepCount)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Package Guardian", "Cleaning up stashes...", 0.5f);
                
                var service = RepositoryService.Instance;
                var repo = service.Repository;
                
                int deleted = repo.Stash.CleanupKeepRecent(keepCount);
                
                EditorUtility.ClearProgressBar();
                
                EditorUtility.DisplayDialog("Cleanup Complete", 
                    $"Successfully deleted {deleted} old stashes.\n\n" +
                    $"Kept {keepCount} most recent stashes.", "OK");
                
                Debug.Log($"[Package Guardian] Cleaned up {deleted} stashes, kept {keepCount} most recent");
                
                Refresh();
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", $"Failed to clean up stashes: {ex.Message}", "OK");
                Debug.LogError($"[Package Guardian] Error cleaning up stashes: {ex}");
            }
        }
        
        private void PerformCleanupOld(int days)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Package Guardian", "Cleaning up old stashes...", 0.5f);
                
                var service = RepositoryService.Instance;
                var repo = service.Repository;
                
                int deleted = repo.Stash.CleanupOldStashes(days);
                
                EditorUtility.ClearProgressBar();
                
                EditorUtility.DisplayDialog("Cleanup Complete", 
                    $"Successfully deleted {deleted} stashes older than {days} days.", "OK");
                
                Debug.Log($"[Package Guardian] Cleaned up {deleted} stashes older than {days} days");
                
                Refresh();
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", $"Failed to clean up stashes: {ex.Message}", "OK");
                Debug.LogError($"[Package Guardian] Error cleaning up stashes: {ex}");
            }
        }
        
        private VisualElement CreateEmptyState(string title, string description)
        {
            var emptyState = new VisualElement();
            emptyState.AddToClassList("pg-empty-state");
            
            var emptyTitle = new Label(title);
            emptyTitle.AddToClassList("pg-empty-state-title");
            emptyState.Add(emptyTitle);
            
            var emptyDesc = new Label(description);
            emptyDesc.AddToClassList("pg-empty-state-description");
            emptyState.Add(emptyDesc);
            
            return emptyState;
        }
        
        private VisualElement CreateStashItem(global::PackageGuardian.Core.Repository.StashEntry stash)
        {
            var container = new VisualElement();
            container.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            container.style.paddingTop = 16;
            container.style.paddingBottom = 16;
            container.style.paddingLeft = 16;
            container.style.paddingRight = 16;
            container.style.marginBottom = 12;
            container.style.borderLeftWidth = 3;
            container.style.borderLeftColor = new Color(0.21f, 0.75f, 0.69f);
            
            // Header row
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.justifyContent = Justify.SpaceBetween;
            headerRow.style.alignItems = Align.FlexStart;
            headerRow.style.marginBottom = 12;
            
            var infoSection = new VisualElement();
            infoSection.style.flexGrow = 1;
            
            var message = new Label(stash.Message);
            message.AddToClassList("pg-label");
            message.style.fontSize = 14;
            message.style.unityFontStyleAndWeight = FontStyle.Bold;
            message.style.marginBottom = 4;
            infoSection.Add(message);
            
            var metaRow = new VisualElement();
            metaRow.style.flexDirection = FlexDirection.Row;
            metaRow.style.alignItems = Align.Center;
            
            var dt = DateTimeOffset.FromUnixTimeSeconds(stash.Timestamp);
            var timeLabel = new Label($"{dt.ToLocalTime():g}");
            timeLabel.AddToClassList("pg-label-secondary");
            timeLabel.style.marginRight = 12;
            metaRow.Add(timeLabel);
            
            var idLabel = new Label($"#{stash.CommitId.Substring(0, 8)}");
            idLabel.AddToClassList("pg-label-small");
            idLabel.style.paddingTop = 4;
            idLabel.style.paddingBottom = 4;
            idLabel.style.paddingLeft = 4;
            idLabel.style.paddingRight = 4;
            idLabel.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);
            metaRow.Add(idLabel);
            
            infoSection.Add(metaRow);
            
            // Add diff summary if available
            var diffSummary = GetDiffSummary(stash);
            if (!string.IsNullOrEmpty(diffSummary))
            {
                var summaryLabel = new Label(diffSummary);
                summaryLabel.AddToClassList("pg-label-small");
                summaryLabel.style.marginTop = 4;
                summaryLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
                infoSection.Add(summaryLabel);
            }
            
            headerRow.Add(infoSection);
            
            // Actions
            var actionsRow = new VisualElement();
            actionsRow.style.flexDirection = FlexDirection.Column;
            
            var applyButton = new Button(() => ApplyStash(stash));
            applyButton.text = "Apply";
            applyButton.AddToClassList("pg-button");
            applyButton.AddToClassList("pg-button-primary");
            applyButton.style.marginBottom = 4;
            actionsRow.Add(applyButton);
            
            var dropButton = new Button(() => DropStash(stash));
            dropButton.text = "Drop";
            dropButton.AddToClassList("pg-button");
            actionsRow.Add(dropButton);
            
            headerRow.Add(actionsRow);
            
            container.Add(headerRow);
            
            return container;
        }
        
        private string GetDiffSummary(global::PackageGuardian.Core.Repository.StashEntry stash)
        {
            try
            {
                var service = RepositoryService.Instance;
                var repo = service.Repository;
                
                if (repo == null) return null;
                
                // Get the commit
                var commitObj = repo.Store.ReadObject(stash.CommitId);
                if (commitObj is not Commit commit) return null;
                
                // Get parent commit
                string parentCommitId = null;
                if (commit.Parents.Any())
                {
                    parentCommitId = repo.Hasher.ToHex(commit.Parents.First());
                }
                
                if (string.IsNullOrEmpty(parentCommitId)) return null;
                
                // Compare with parent to get changes
                var diffEngine = new DiffEngine(repo.Store);
                var changes = diffEngine.CompareCommits(parentCommitId, stash.CommitId);
                
                if (changes.Count == 0) return null;
                
                var added = changes.Count(c => c.Type == ChangeType.Added);
                var modified = changes.Count(c => c.Type == ChangeType.Modified);
                var deleted = changes.Count(c => c.Type == ChangeType.Deleted);
                var renamed = changes.Count(c => c.Type == ChangeType.Renamed || c.Type == ChangeType.Copied);
                
                // Categorize changes by file type
                var packages = changes.Where(c => 
                    (c.Path.Contains("Packages/") || c.Path.Contains("manifest.json")) &&
                    (c.Type == ChangeType.Added || c.Type == ChangeType.Modified)).Count();
                var scenes = changes.Where(c => 
                    (c.Type == ChangeType.Modified || c.Type == ChangeType.Added) && 
                    c.Path.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase)).Count();
                var prefabs = changes.Where(c => 
                    (c.Type == ChangeType.Modified || c.Type == ChangeType.Added) && 
                    c.Path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)).Count();
                var scripts = changes.Where(c => 
                    (c.Type == ChangeType.Modified || c.Type == ChangeType.Added) && 
                    (c.Path.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase) || 
                     c.Path.EndsWith(".js", System.StringComparison.OrdinalIgnoreCase))).Count();
                var configs = changes.Where(c => 
                    c.Type == ChangeType.Modified && (
                    c.Path.Contains("ProjectSettings/") || 
                    c.Path.Contains("manifest.json") ||
                    c.Path.Contains("settings.json"))).Count();
                
                var summaryParts = new System.Collections.Generic.List<string>();
                
                // Show file counts
                if (added > 0) summaryParts.Add($"+{added}");
                if (modified > 0) summaryParts.Add($"~{modified}");
                if (deleted > 0) summaryParts.Add($"-{deleted}");
                if (renamed > 0) summaryParts.Add($"→{renamed}");
                
                // Show notable types
                if (packages > 0) summaryParts.Add($"pkg:{packages}");
                if (scenes > 0) summaryParts.Add($"scene:{scenes}");
                if (prefabs > 0) summaryParts.Add($"prefab:{prefabs}");
                if (scripts > 0) summaryParts.Add($"script:{scripts}");
                if (configs > 0) summaryParts.Add($"config:{configs}");
                
                if (summaryParts.Count > 0)
                {
                    return $"[{string.Join(" ", summaryParts)}]";
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Package Guardian] Failed to get diff summary for stash: {ex.Message}");
            }
            
            return null;
        }
        
        private void ApplyStash(global::PackageGuardian.Core.Repository.StashEntry stash)
        {
            if (!EditorUtility.DisplayDialog("Apply Stash",
                                            $"Are you sure you want to apply stash '{stash.Message}'?",
                                            "Apply", "Cancel"))
            {
                return;
            }
            
            try
            {
                EditorUtility.DisplayProgressBar("Package Guardian", "Applying stash...", 0.5f);
                
                var service = RepositoryService.Instance;
                service.Repository.Stash.Apply(stash.RefName);
                
                EditorUtility.ClearProgressBar();
                Debug.Log($"[Package Guardian] Stash '{stash.Message}' applied.");
                EditorUtility.DisplayDialog("Success", "Stash applied successfully!", "OK");
                
                Refresh();
                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", $"Failed to apply stash: {ex.Message}", "OK");
                Debug.LogError($"[Package Guardian] Failed to apply stash: {ex}");
            }
        }
        
        private void DropStash(global::PackageGuardian.Core.Repository.StashEntry stash)
        {
            if (!EditorUtility.DisplayDialog("Drop Stash",
                                            $"Are you sure you want to drop stash '{stash.Message}'? This action cannot be undone.",
                                            "Drop", "Cancel"))
            {
                return;
            }
            
            try
            {
                var service = RepositoryService.Instance;
                service.Repository.Stash.Drop(stash.RefName);
                Debug.Log($"[Package Guardian] Stash '{stash.Message}' dropped.");
                Refresh();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to drop stash: {ex.Message}", "OK");
                Debug.LogError($"[Package Guardian] Failed to drop stash: {ex}");
            }
        }
    }
}

