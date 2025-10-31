using System;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.PackageGuardian.Editor.Windows.Graph;

namespace YUCP.Components.PackageGuardian.Editor.Windows.UnifiedDashboard
{
    public class HistoryTab : VisualElement
    {
        private GraphView _graphView;
        private VisualElement _detailsPanel;
        private Label _commitIdLabel;
        private Label _authorLabel;
        private Label _timestampLabel;
        private Label _messageLabel;
        private ScrollView _filesList;
        
        public HistoryTab()
        {
            AddToClassList("pg-split-view");
            CreateUI();
        }
        
        private void CreateUI()
        {
            // Left: Graph
            var leftPanel = new VisualElement();
            leftPanel.AddToClassList("pg-split-left");
            
            var graphTitle = new Label("Commit Graph");
            graphTitle.AddToClassList("pg-section-title");
            graphTitle.style.marginBottom = 12;
            leftPanel.Add(graphTitle);
            
            _graphView = new GraphView();
            _graphView.OnCommitSelected = OnCommitSelected;
            _graphView.style.flexGrow = 1;
            leftPanel.Add(_graphView);
            
            Add(leftPanel);
            
            // Right: Details
            _detailsPanel = new VisualElement();
            _detailsPanel.AddToClassList("pg-split-right");
            
            var detailsSection = new VisualElement();
            detailsSection.AddToClassList("pg-section");
            
            var detailsTitle = new Label("Commit Details");
            detailsTitle.AddToClassList("pg-section-title");
            detailsSection.Add(detailsTitle);
            
            _commitIdLabel = new Label("Select a commit...");
            _commitIdLabel.AddToClassList("pg-label-secondary");
            detailsSection.Add(_commitIdLabel);
            
            _authorLabel = new Label();
            _authorLabel.AddToClassList("pg-label-secondary");
            _authorLabel.style.marginTop = 8;
            detailsSection.Add(_authorLabel);
            
            _timestampLabel = new Label();
            _timestampLabel.AddToClassList("pg-label-small");
            _timestampLabel.style.marginTop = 4;
            detailsSection.Add(_timestampLabel);
            
            var sep = new VisualElement();
            sep.AddToClassList("pg-separator");
            detailsSection.Add(sep);
            
            _messageLabel = new Label();
            _messageLabel.AddToClassList("pg-label");
            _messageLabel.style.whiteSpace = WhiteSpace.Normal;
            detailsSection.Add(_messageLabel);
            
            _detailsPanel.Add(detailsSection);
            
            // Files
            var filesSection = new VisualElement();
            filesSection.AddToClassList("pg-section");
            filesSection.style.flexGrow = 1;
            filesSection.style.marginTop = 12;
            
            var filesTitle = new Label("Changed Files");
            filesTitle.AddToClassList("pg-section-title");
            filesSection.Add(filesTitle);
            
            _filesList = new ScrollView();
            _filesList.AddToClassList("pg-scrollview");
            filesSection.Add(_filesList);
            
            _detailsPanel.Add(filesSection);
            
            Add(_detailsPanel);
        }
        
        private void OnCommitSelected(GraphNode node)
        {
            if (node == null) return;
            
            _commitIdLabel.text = $"Commit: {node.CommitId.Substring(0, 8)}";
            _authorLabel.text = $"Author: {node.Author}";
            _timestampLabel.text = $"Date: {node.Timestamp}";
            _messageLabel.text = node.Message;
            
            LoadFiles(node.CommitId);
        }
        
        private void LoadFiles(string commitId)
        {
            _filesList.Clear();
            
            try
            {
                var repo = Services.RepositoryService.Instance.Repository;
                var commit = repo.Store.ReadObject(commitId) as global::PackageGuardian.Core.Objects.Commit;
                
                if (commit == null || commit.Parents == null || commit.Parents.Count == 0)
                {
                    _filesList.Add(new Label("Initial commit") { style = { color = new Color(0.64f, 0.74f, 0.86f) } });
                    return;
                }
                
                string parentId = BitConverter.ToString(commit.Parents[0]).Replace("-", "").ToLower();
                
                var diffEngine = new global::PackageGuardian.Core.Diff.DiffEngine(repo.Store);
                var changes = diffEngine.CompareCommits(parentId, commitId);
                
                foreach (var change in changes)
                {
                    var item = CreateFileChangeItem(change);
                    _filesList.Add(item);
                }
            }
            catch (Exception ex)
            {
                _filesList.Add(new Label($"Error: {ex.Message}") { style = { color = Color.red } });
            }
        }
        
        private VisualElement CreateFileChangeItem(global::PackageGuardian.Core.Diff.FileChange change)
        {
            var item = new VisualElement();
            item.AddToClassList("pg-list-item");
            item.style.flexDirection = FlexDirection.Row;
            item.style.alignItems = Align.Center;
            
            string icon = "+";
            string iconClass = "pg-file-icon-added";
            
            switch (change.Type)
            {
                case global::PackageGuardian.Core.Diff.ChangeType.Modified:
                    icon = "~";
                    iconClass = "pg-file-icon-modified";
                    break;
                case global::PackageGuardian.Core.Diff.ChangeType.Deleted:
                    icon = "-";
                    iconClass = "pg-file-icon-deleted";
                    break;
            }
            
            var iconLabel = new Label(icon);
            iconLabel.AddToClassList("pg-file-icon");
            iconLabel.AddToClassList(iconClass);
            item.Add(iconLabel);
            
            // For renames/copies, show both paths
            string displayPath = change.Path;
            if (change.Type == global::PackageGuardian.Core.Diff.ChangeType.Renamed && !string.IsNullOrEmpty(change.NewPath))
            {
                displayPath = $"{change.Path} -> {change.NewPath}";
                if (change.SimilarityScore > 0)
                    displayPath += $" ({change.SimilarityScore:P0})";
            }
            else if (change.Type == global::PackageGuardian.Core.Diff.ChangeType.Copied && !string.IsNullOrEmpty(change.NewPath))
            {
                displayPath = $"{change.Path} => {change.NewPath}";
                if (change.SimilarityScore > 0)
                    displayPath += $" ({change.SimilarityScore:P0})";
            }
            
            var pathLabel = new Label(displayPath);
            pathLabel.style.flexGrow = 1;
            pathLabel.AddToClassList("pg-label");
            item.Add(pathLabel);
            
            var viewBtn = new Button(() => ViewDiff(change));
            viewBtn.text = "Diff";
            viewBtn.AddToClassList("pg-button");
            viewBtn.AddToClassList("pg-button-small");
            item.Add(viewBtn);
            
            return item;
        }
        
        private void ViewDiff(global::PackageGuardian.Core.Diff.FileChange change)
        {
            DiffWindow.ShowDiff(change, "");
        }
        
        public void Refresh()
        {
            _graphView?.Refresh();
        }
    }
}
