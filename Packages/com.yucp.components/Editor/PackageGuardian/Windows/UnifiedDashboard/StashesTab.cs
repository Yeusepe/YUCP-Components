using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.PackageGuardian.Editor.Services;

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
                    
                    var emptyDesc = new Label("Stashes are created automatically before imports");
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
