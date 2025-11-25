using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.PackageGuardian.Editor.Services;

namespace YUCP.Components.PackageGuardian.Editor.Windows.UnifiedDashboard
{
    public class ChangesTab : VisualElement
    {
        private ScrollView _filesView;
        private Label _statusLabel;
        private TextField _messageField;
        private Button _commitButton;
        private List<FileChange> _changes = new List<FileChange>();
        
        public ChangesTab()
        {
            AddToClassList("pg-container");
            CreateUI();
            Refresh();
        }
        
        private void CreateUI()
        {
            // Stats row
            var statsRow = new VisualElement();
            statsRow.style.flexDirection = FlexDirection.Row;
            statsRow.style.marginBottom = 20;
            statsRow.style.unityTextAlign = TextAnchor.UpperLeft;
            
            var modifiedCard = CreateStatCard("Modified", "0", "#3b82f6");
            var addedCard = CreateStatCard("Added", "0", "#10b981");
            var deletedCard = CreateStatCard("Deleted", "0", "#ef4444");
            
            statsRow.Add(modifiedCard);
            statsRow.Add(addedCard);
            statsRow.Add(deletedCard);
            Add(statsRow);
            
            // Message section
            var messageSection = new VisualElement();
            messageSection.AddToClassList("pg-section");
            
            var messageTitle = new Label("Commit Message");
            messageTitle.AddToClassList("pg-section-title");
            messageSection.Add(messageTitle);
            
            _messageField = new TextField();
            _messageField.multiline = true;
            _messageField.value = "Manual snapshot";
            _messageField.style.minHeight = 80;
			_messageField.AddToClassList("pg-input");
            messageSection.Add(_messageField);
            
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 12;
            
            var commitBtn = new Button(OnCommit);
            commitBtn.text = "Create Snapshot";
            commitBtn.AddToClassList("pg-button");
            commitBtn.AddToClassList("pg-button-primary");
            _commitButton = commitBtn;
            buttonRow.Add(commitBtn);
            
            var discardBtn = new Button(OnDiscardAll);
            discardBtn.text = "Discard All";
            discardBtn.AddToClassList("pg-button");
            discardBtn.AddToClassList("pg-button-danger");
            buttonRow.Add(discardBtn);
            
            messageSection.Add(buttonRow);
            Add(messageSection);
            
            // Files section
            var filesSection = new VisualElement();
            filesSection.AddToClassList("pg-section");
            filesSection.style.flexGrow = 1;
            
            var filesTitle = new Label("Changed Files");
            filesTitle.AddToClassList("pg-section-title");
            filesSection.Add(filesTitle);
            
            _statusLabel = new Label("Loading...");
            _statusLabel.AddToClassList("pg-label-secondary");
            filesSection.Add(_statusLabel);
            
            _filesView = new ScrollView();
            _filesView.AddToClassList("pg-scrollview");
            _filesView.style.marginTop = 12;
            filesSection.Add(_filesView);
            
            Add(filesSection);
        }
        
        private VisualElement CreateStatCard(string label, string value, string color)
        {
            var card = new VisualElement();
            card.AddToClassList("pg-card");
            card.style.flexGrow = 1;
            card.style.marginRight = 12;
            card.style.minWidth = 120;
            
            var valueLabel = new Label(value);
            valueLabel.style.fontSize = 28;
            valueLabel.style.color = ColorUtility.TryParseHtmlString(color, out var c) ? c : Color.white;
            valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            valueLabel.style.marginBottom = 4;
            card.Add(valueLabel);
            
            var titleLabel = new Label(label);
            titleLabel.AddToClassList("pg-label-secondary");
            titleLabel.style.fontSize = 11;
            card.Add(titleLabel);
            
            return card;
        }
        
        public void Refresh()
        {
            _changes.Clear();
            _filesView.Clear();
            
            try
            {
                var repo = RepositoryService.Instance.Repository;
                string headCommitId = repo.Refs.ResolveHead();
                
                if (string.IsNullOrEmpty(headCommitId))
                {
                    _statusLabel.text = "No commits yet";
                    return;
                }
                
                DetectChanges(repo);
                
                if (_changes.Count == 0)
                {
                    _statusLabel.text = "No changes detected";
                    return;
                }
                
                _statusLabel.text = $"{_changes.Count} file(s) changed";
                
                foreach (var change in _changes)
                {
                    var item = CreateFileItem(change);
                    _filesView.Add(item);
                }
            }
            catch (Exception ex)
            {
                _statusLabel.text = $"Error: {ex.Message}";
                Debug.LogError($"[PG] Refresh error: {ex}");
            }
        }
        
        private void DetectChanges(global::PackageGuardian.Core.Repository.Repository repo)
        {
            var roots = new[] { "Assets", "Packages" };
            foreach (var root in roots)
            {
                string rootPath = Path.Combine(RepositoryService.Instance.ProjectRoot, root);
                if (Directory.Exists(rootPath))
                {
                    ScanDirectory(rootPath, RepositoryService.Instance.ProjectRoot, repo);
                }
            }
        }
        
        private void ScanDirectory(string dirPath, string repoRoot, global::PackageGuardian.Core.Repository.Repository repo)
        {
            try
            {
                foreach (var filePath in Directory.GetFiles(dirPath))
                {
                    string relPath = Path.GetRelativePath(repoRoot, filePath).Replace('\\', '/');
                    
                    if (repo.IgnoreRules.IsIgnored(relPath))
                        continue;
                    
                    if (repo.Index.TryGet(relPath, out var entry))
                    {
                        var fileInfo = new FileInfo(filePath);
                        if (fileInfo.Length != entry.Size || fileInfo.LastWriteTimeUtc.Ticks != entry.MTimeUtc)
                        {
                            _changes.Add(new FileChange { Path = relPath, Type = ChangeType.Modified });
                        }
                    }
                    else
                    {
                        _changes.Add(new FileChange { Path = relPath, Type = ChangeType.Added });
                    }
                }
                
                foreach (var subDir in Directory.GetDirectories(dirPath))
                {
                    string relPath = Path.GetRelativePath(repoRoot, subDir).Replace('\\', '/');
                    if (!repo.IgnoreRules.IsIgnored(relPath))
                    {
                        ScanDirectory(subDir, repoRoot, repo);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PG] Scan error in {dirPath}: {ex.Message}");
            }
        }
        
        private VisualElement CreateFileItem(FileChange change)
        {
            var item = new VisualElement();
            item.AddToClassList("pg-list-item");
            item.style.flexDirection = FlexDirection.Row;
            item.style.alignItems = Align.Center;
            
            // Icon
            string icon = change.Type == ChangeType.Added ? "+" : change.Type == ChangeType.Modified ? "~" : "-";
            string iconClass = change.Type == ChangeType.Added ? "pg-file-icon-added" : 
                               change.Type == ChangeType.Modified ? "pg-file-icon-modified" : "pg-file-icon-deleted";
            
            var iconLabel = new Label(icon);
            iconLabel.AddToClassList("pg-file-icon");
            iconLabel.AddToClassList(iconClass);
            item.Add(iconLabel);
            
            // Path
            var pathLabel = new Label(change.Path);
            pathLabel.style.flexGrow = 1;
            pathLabel.AddToClassList("pg-label");
            item.Add(pathLabel);
            
            return item;
        }
        
        private void OnCommit()
        {
            string message = _messageField.value;
            if (string.IsNullOrWhiteSpace(message)) message = "Manual snapshot";
            
            _commitButton?.SetEnabled(false);
            _statusLabel.text = "Snapshot queued...";
            
            var task = RepositoryService.Instance.CreateSnapshotAsync(message);
            task.ContinueWith(t =>
            {
                EditorApplication.delayCall += () =>
                {
                    _commitButton?.SetEnabled(true);
                    
                    if (t.IsFaulted)
                    {
                        var error = t.Exception?.GetBaseException().Message ?? "Unknown error";
                        Debug.LogError($"[PG] Commit error: {error}");
                        EditorUtility.DisplayDialog("Error", $"Failed to create snapshot: {error}", "OK");
                        return;
                    }
                    
                    string commitId = t.Result;
                    if (string.IsNullOrEmpty(commitId))
                    {
                        EditorUtility.DisplayDialog("Package Guardian", "Snapshot was cancelled due to validation errors.", "OK");
                        return;
                    }
                    
                    Debug.Log($"[PG] Snapshot created: {commitId}");
                    EditorUtility.DisplayDialog("Success", $"Snapshot created: {commitId.Substring(0, Math.Min(commitId.Length, 8))}", "OK");
                    
                    _messageField.value = "Manual snapshot";
                    Refresh();
                };
            });
        }
        
        private void OnDiscardAll()
        {
            EditorUtility.DisplayDialog("Not Implemented", "Discard all is not yet implemented", "OK");
        }
        
        private enum ChangeType { Added, Modified, Deleted }
        
        private class FileChange
        {
            public string Path { get; set; }
            public ChangeType Type { get; set; }
        }
    }
}
