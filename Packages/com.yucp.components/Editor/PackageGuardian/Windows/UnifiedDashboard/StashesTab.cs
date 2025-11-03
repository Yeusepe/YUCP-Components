using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.PackageGuardian.Editor.Services;
using global::PackageGuardian.Core.Diff;
using global::PackageGuardian.Core.Objects;

namespace YUCP.Components.PackageGuardian.Editor.Windows.UnifiedDashboard
{
    public class StashesTab : VisualElement
    {
        private ScrollView _stashList;
        private Label _statusLabel;
        
        public StashesTab()
        {
            AddToClassList("pg-container");
            CreateUI();
            Refresh();
        }
        
        private void CreateUI()
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 20;
            
            _statusLabel = new Label("Loading stashes...");
            _statusLabel.AddToClassList("pg-label");
            header.Add(_statusLabel);
            
            var refreshBtn = new Button(Refresh);
            refreshBtn.text = "Refresh";
            refreshBtn.AddToClassList("pg-button");
            header.Add(refreshBtn);
            
            Add(header);
            
            var section = new VisualElement();
            section.AddToClassList("pg-section");
            section.style.flexGrow = 1;
            
            var title = new Label("Saved Stashes");
            title.AddToClassList("pg-section-title");
            section.Add(title);
            
            _stashList = new ScrollView();
            _stashList.AddToClassList("pg-scrollview");
            section.Add(_stashList);
            
            Add(section);
        }
        
        public void Refresh()
        {
            _stashList.Clear();
            
            try
            {
                var repo = RepositoryService.Instance.Repository;
                var stashes = repo.Stash.List();
                
                _statusLabel.text = $"{stashes.Count} stash(es)";
                
                if (stashes.Count == 0)
                {
                    var empty = new VisualElement();
                    empty.AddToClassList("pg-empty-state");
                    
                    var icon = new Label("S");
                    icon.AddToClassList("pg-empty-state-icon");
                    empty.Add(icon);
                    
                    var emptyTitle = new Label("No Stashes");
                    emptyTitle.AddToClassList("pg-empty-state-title");
                    empty.Add(emptyTitle);
                    
                    var emptyDesc = new Label("Stashes are created automatically after imports and package changes");
                    emptyDesc.AddToClassList("pg-empty-state-description");
                    empty.Add(emptyDesc);
                    
                    _stashList.Add(empty);
                    return;
                }
                
                foreach (var stash in stashes.OrderByDescending(s => s.Timestamp))
                {
                    _stashList.Add(CreateStashItem(stash));
                }
            }
            catch (Exception ex)
            {
                _statusLabel.text = $"Error: {ex.Message}";
                Debug.LogError($"[PG] Stash refresh error: {ex}");
            }
        }
        
        private VisualElement CreateStashItem(global::PackageGuardian.Core.Repository.StashEntry stash)
        {
            var card = new VisualElement();
            card.AddToClassList("pg-card");
            
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 12;
            
            var message = new Label(stash.Message);
            message.AddToClassList("pg-label");
            message.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(message);
            
            card.Add(header);
            
            var dt = DateTimeOffset.FromUnixTimeSeconds(stash.Timestamp);
            var info = new Label($"{dt.ToLocalTime():g} • {stash.CommitId.Substring(0, 8)}");
            info.AddToClassList("pg-label-small");
            card.Add(info);
            
            // Add diff summary if available
            var diffSummary = GetDiffSummary(stash);
            if (!string.IsNullOrEmpty(diffSummary))
            {
                var summaryLabel = new Label(diffSummary);
                summaryLabel.AddToClassList("pg-label-small");
                summaryLabel.style.marginTop = 4;
                summaryLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
                card.Add(summaryLabel);
            }
            
            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.marginTop = 12;
            
            var applyBtn = new Button(() => ApplyStash(stash));
            applyBtn.text = "Apply";
            applyBtn.AddToClassList("pg-button");
            applyBtn.AddToClassList("pg-button-primary");
            actions.Add(applyBtn);
            
            var dropBtn = new Button(() => DropStash(stash));
            dropBtn.text = "Drop";
            dropBtn.AddToClassList("pg-button");
            dropBtn.AddToClassList("pg-button-danger");
            actions.Add(dropBtn);
            
            card.Add(actions);
            
            return card;
        }
        
        private string GetDiffSummary(global::PackageGuardian.Core.Repository.StashEntry stash)
        {
            try
            {
                var repo = RepositoryService.Instance.Repository;
                
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
                $"Apply stash: {stash.Message}?\n\nThis will restore the stashed snapshot.", 
                "Apply", "Cancel"))
                return;
            
            try
            {
                var repo = RepositoryService.Instance.Repository;
                repo.Stash.Apply(stash.RefName);
                Debug.Log($"[PG] Applied stash: {stash.Message}");
                EditorUtility.DisplayDialog("Success", "Stash applied successfully", "OK");
                Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PG] Apply stash error: {ex}");
                EditorUtility.DisplayDialog("Error", $"Failed to apply stash: {ex.Message}", "OK");
            }
        }
        
        private void DropStash(global::PackageGuardian.Core.Repository.StashEntry stash)
        {
            if (!EditorUtility.DisplayDialog("Drop Stash", 
                $"Permanently delete stash: {stash.Message}?\n\nThis cannot be undone!", 
                "Drop", "Cancel"))
                return;
            
            try
            {
                var repo = RepositoryService.Instance.Repository;
                repo.Stash.Drop(stash.RefName);
                Debug.Log($"[PG] Dropped stash: {stash.Message}");
                Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PG] Drop stash error: {ex}");
                EditorUtility.DisplayDialog("Error", $"Failed to drop stash: {ex.Message}", "OK");
            }
        }
    }
}
