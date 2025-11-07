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
            
            var refreshButton = new Button(Refresh);
            refreshButton.text = "Refresh";
            refreshButton.AddToClassList("pg-button");
            header.Add(refreshButton);
            
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
            
            var infoLabel = new Label("Stashes are automatically created before major operations");
            infoLabel.AddToClassList("pg-label-secondary");
            footer.Add(infoLabel);
            
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
                
                Debug.Log("[Package Guardian] Fetching stashes...");
                var stashes = repo.Stash.List();
                Debug.Log($"[Package Guardian] Found {stashes.Count} stashes");
                
                if (stashes.Count == 0)
                {
                    var emptyState = CreateEmptyState("No Stashes Yet", "Stashes will appear here when you create them.");
                    _stashList.Add(emptyState);
                    return;
                }
                
                foreach (var stash in stashes)
                {
                    Debug.Log($"[Package Guardian] Stash: {stash.RefName} - {stash.Message} - {stash.CommitId}");
                    var stashItem = CreateStashItem(stash);
                    _stashList.Add(stashItem);
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

