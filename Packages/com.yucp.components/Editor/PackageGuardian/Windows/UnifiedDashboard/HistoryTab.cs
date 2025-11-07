using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.PackageGuardian.Editor.Windows.Graph;
using YUCP.Components.PackageGuardian.Editor.Services;

namespace YUCP.Components.PackageGuardian.Editor.Windows.UnifiedDashboard
{
    public class HistoryTab : VisualElement
    {
        private GroupedGraphView _graphView;
        private VisualElement _detailsPanel;
        private ScrollView _detailsScrollView;
        
        // Commit Details UI Elements
        private VisualElement _commitHeaderSection;
        private Label _commitHashLabel;
        private Label _commitTypeBadge;
        private Label _commitMessageLabel;
        private VisualElement _commitMetaSection;
        private Label _authorLabel;
        private Label _timestampLabel;
        private VisualElement _statsSection;
        private VisualElement _filesSection;
        private ScrollView _filesList;
        private Label _emptyStateLabel;
        
        public HistoryTab()
        {
            AddToClassList("pg-split-view");
            CreateUI();
        }
        
        private void CreateUI()
        {
            // Left: Commit History Graph
            var leftPanel = new VisualElement();
            leftPanel.AddToClassList("pg-split-left");
            leftPanel.style.paddingTop = 20;
            leftPanel.style.paddingBottom = 20;
            leftPanel.style.paddingLeft = 20;
            leftPanel.style.paddingRight = 20;
            
            var graphTitle = new Label("COMMIT HISTORY");
            graphTitle.AddToClassList("pg-section-title");
            graphTitle.style.marginBottom = 16;
            leftPanel.Add(graphTitle);
            
            _graphView = new GroupedGraphView(showStashesSeparately: true);
            _graphView.OnCommitSelected = OnCommitSelected;
            _graphView.style.flexGrow = 1;
            leftPanel.Add(_graphView);
            
            Add(leftPanel);
            
            // Right: Commit Details Panel
            _detailsPanel = new VisualElement();
            _detailsPanel.AddToClassList("pg-split-right");
            _detailsPanel.style.paddingTop = 20;
            _detailsPanel.style.paddingBottom = 20;
            _detailsPanel.style.paddingLeft = 20;
            _detailsPanel.style.paddingRight = 20;
            
            _detailsScrollView = new ScrollView();
            _detailsScrollView.AddToClassList("pg-scrollview");
            _detailsScrollView.style.flexGrow = 1;
            
            // Commit Header Section
            _commitHeaderSection = CreateCommitHeaderSection();
            _detailsScrollView.Add(_commitHeaderSection);
            
            // Commit Meta Section
            _commitMetaSection = CreateCommitMetaSection();
            _detailsScrollView.Add(_commitMetaSection);
            
            // Statistics Section
            _statsSection = CreateStatsSection();
            _detailsScrollView.Add(_statsSection);
            
            // Files Section
            _filesSection = CreateFilesSection();
            _detailsScrollView.Add(_filesSection);
            
            // Empty State
            _emptyStateLabel = new Label("Select a commit to view details");
            _emptyStateLabel.AddToClassList("pg-empty-state-description");
            _emptyStateLabel.style.marginTop = 40;
            _emptyStateLabel.style.marginBottom = 40;
            _emptyStateLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _detailsScrollView.Add(_emptyStateLabel);
            
            _detailsPanel.Add(_detailsScrollView);
            Add(_detailsPanel);
            
            ShowEmptyState();
        }
        
        private VisualElement CreateCommitHeaderSection()
        {
            var section = new VisualElement();
            section.AddToClassList("pg-section");
            section.style.marginBottom = 16;
            
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 12;
            
            _commitHashLabel = new Label();
            _commitHashLabel.style.fontSize = 14;
            _commitHashLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _commitHashLabel.style.color = new Color(0.36f, 0.64f, 1.0f); // Blue
            _commitHashLabel.style.marginRight = 12;
            headerRow.Add(_commitHashLabel);
            
            _commitTypeBadge = new Label();
            _commitTypeBadge.style.fontSize = 10;
            _commitTypeBadge.style.paddingTop = 4;
            _commitTypeBadge.style.paddingBottom = 4;
            _commitTypeBadge.style.paddingLeft = 8;
            _commitTypeBadge.style.paddingRight = 8;
            _commitTypeBadge.style.borderTopLeftRadius = 0;
            _commitTypeBadge.style.borderTopRightRadius = 0;
            _commitTypeBadge.style.borderBottomLeftRadius = 0;
            _commitTypeBadge.style.borderBottomRightRadius = 0;
            headerRow.Add(_commitTypeBadge);
            
            section.Add(headerRow);
            
            _commitMessageLabel = new Label();
            _commitMessageLabel.style.fontSize = 13;
            _commitMessageLabel.style.color = new Color(0.9f, 0.9f, 0.9f);
            _commitMessageLabel.style.whiteSpace = WhiteSpace.Normal;
            _commitMessageLabel.style.marginBottom = 8;
            section.Add(_commitMessageLabel);
            
            return section;
        }
        
        private VisualElement CreateCommitMetaSection()
        {
            var section = new VisualElement();
            section.AddToClassList("pg-section");
            section.style.marginBottom = 16;
            
            var sectionTitle = new Label("INFORMATION");
            sectionTitle.AddToClassList("pg-section-title");
            sectionTitle.style.marginBottom = 12;
            section.Add(sectionTitle);
            
            var metaContainer = new VisualElement();
            
            // Author Row
            var authorRow = new VisualElement();
            authorRow.style.flexDirection = FlexDirection.Row;
            authorRow.style.marginBottom = 8;
            
            var authorLabelTitle = new Label("Author:");
            authorLabelTitle.style.fontSize = 11;
            authorLabelTitle.style.color = new Color(0.7f, 0.7f, 0.7f);
            authorLabelTitle.style.width = 80;
            authorRow.Add(authorLabelTitle);
            
            _authorLabel = new Label();
            _authorLabel.style.fontSize = 12;
            _authorLabel.style.color = new Color(0.9f, 0.9f, 0.9f);
            _authorLabel.style.flexGrow = 1;
            authorRow.Add(_authorLabel);
            
            metaContainer.Add(authorRow);
            
            // Timestamp Row
            var timestampRow = new VisualElement();
            timestampRow.style.flexDirection = FlexDirection.Row;
            
            var timestampLabelTitle = new Label("Date:");
            timestampLabelTitle.style.fontSize = 11;
            timestampLabelTitle.style.color = new Color(0.7f, 0.7f, 0.7f);
            timestampLabelTitle.style.width = 80;
            timestampRow.Add(timestampLabelTitle);
            
            _timestampLabel = new Label();
            _timestampLabel.style.fontSize = 12;
            _timestampLabel.style.color = new Color(0.9f, 0.9f, 0.9f);
            _timestampLabel.style.flexGrow = 1;
            timestampRow.Add(_timestampLabel);
            
            metaContainer.Add(timestampRow);
            
            section.Add(metaContainer);
            
            return section;
        }
        
        private VisualElement CreateStatsSection()
        {
            var section = new VisualElement();
            section.AddToClassList("pg-section");
            section.style.marginBottom = 16;
            
            var sectionTitle = new Label("STATISTICS");
            sectionTitle.AddToClassList("pg-section-title");
            sectionTitle.style.marginBottom = 12;
            section.Add(sectionTitle);
            
            var statsContainer = new VisualElement();
            statsContainer.style.flexDirection = FlexDirection.Row;
            statsContainer.style.flexWrap = Wrap.Wrap;
            
            // Stats will be added dynamically
            section.Add(statsContainer);
            
            return section;
        }
        
        private VisualElement CreateFilesSection()
        {
            var section = new VisualElement();
            section.AddToClassList("pg-section");
            section.style.flexGrow = 1;
            section.style.minHeight = 200;
            
            var sectionHeader = new VisualElement();
            sectionHeader.style.flexDirection = FlexDirection.Row;
            sectionHeader.style.alignItems = Align.Center;
            sectionHeader.style.justifyContent = Justify.SpaceBetween;
            sectionHeader.style.marginBottom = 12;
            
            var sectionTitle = new Label("CHANGED FILES");
            sectionTitle.AddToClassList("pg-section-title");
            sectionTitle.style.marginBottom = 0;
            sectionHeader.Add(sectionTitle);
            
            section.Add(sectionHeader);
            
            _filesList = new ScrollView();
            _filesList.AddToClassList("pg-scrollview");
            _filesList.style.flexGrow = 1;
            _filesList.style.minHeight = 200;
            section.Add(_filesList);
            
            return section;
        }
        
        private void OnCommitSelected(GraphNode node)
        {
            if (node == null)
            {
                ShowEmptyState();
                return;
            }
            
            HideEmptyState();
            
            // Update header
            _commitHashLabel.text = node.CommitId.Substring(0, 8);
            
            // Update commit type badge
            UpdateCommitTypeBadge(node);
            
            // Update message
            _commitMessageLabel.text = FormatCommitMessage(node.Message);
            
            // Update meta
            _authorLabel.text = string.IsNullOrEmpty(node.Author) ? "Unknown" : node.Author;
            _timestampLabel.text = FormatTimestamp(node.Timestamp);
            
            // Update statistics
            UpdateStatistics(node);
            
            // Load files
            LoadFiles(node.CommitId);
        }
        
        private void UpdateCommitTypeBadge(GraphNode node)
        {
            string badgeText;
            Color badgeColor;
            Color textColor = Color.white;
            
            if (node.IsStash)
            {
                badgeText = "STASH";
                badgeColor = new Color(0.7f, 0.6f, 0.9f, 0.2f); // Purple tint
                textColor = new Color(0.7f, 0.6f, 0.9f);
            }
            else if (node.ParentCount > 1)
            {
                badgeText = "MERGE";
                badgeColor = new Color(0.89f, 0.65f, 0.29f, 0.2f); // Orange tint
                textColor = new Color(0.89f, 0.65f, 0.29f);
            }
            else if (node.Message.Contains("UPM:") || node.Message.Contains("Package"))
            {
                badgeText = "PACKAGE";
                badgeColor = new Color(0.36f, 0.64f, 1.0f, 0.2f); // Blue tint
                textColor = new Color(0.36f, 0.64f, 1.0f);
            }
            else if (node.Message.Contains("Manual") || node.Message.Contains("Snapshot"))
            {
                badgeText = "MANUAL";
                badgeColor = new Color(0.21f, 0.75f, 0.69f, 0.2f); // Teal tint
                textColor = new Color(0.21f, 0.75f, 0.69f);
            }
            else
            {
                badgeText = "AUTO";
                badgeColor = new Color(0.5f, 0.5f, 0.5f, 0.2f); // Gray tint
                textColor = new Color(0.7f, 0.7f, 0.7f);
            }
            
            _commitTypeBadge.text = badgeText;
            _commitTypeBadge.style.backgroundColor = badgeColor;
            _commitTypeBadge.style.color = textColor;
            _commitTypeBadge.style.borderLeftWidth = 2;
            _commitTypeBadge.style.borderLeftColor = textColor;
        }
        
        private string FormatCommitMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "No message";
            
            // Get first line only for display
            int newlineIndex = message.IndexOfAny(new[] { '\r', '\n' });
            if (newlineIndex >= 0)
            {
                message = message.Substring(0, newlineIndex);
            }
            
            return message;
        }
        
        private string FormatTimestamp(long timestamp)
        {
            var dateTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime();
            return dateTime.ToString("MMM dd, yyyy 'at' HH:mm");
        }
        
        private void UpdateStatistics(GraphNode node)
        {
            var statsContainer = _statsSection.Q<VisualElement>();
            if (statsContainer == null || statsContainer.parent == null)
                return;
                
            statsContainer.Clear();
            
            try
            {
                var repo = RepositoryService.Instance.Repository;
                var commit = repo.Store.ReadObject(node.CommitId) as global::PackageGuardian.Core.Objects.Commit;
                
                if (commit == null || commit.Parents == null || commit.Parents.Count == 0)
                {
                    AddStatItem(statsContainer, "Type", "Initial Commit");
                    return;
                }
                
                string parentId = BitConverter.ToString(commit.Parents[0]).Replace("-", "").ToLower();
                var diffEngine = new global::PackageGuardian.Core.Diff.DiffEngine(repo.Store);
                var changes = diffEngine.CompareCommits(parentId, node.CommitId);
                
                int added = changes.Count(c => c.Type == global::PackageGuardian.Core.Diff.ChangeType.Added);
                int modified = changes.Count(c => c.Type == global::PackageGuardian.Core.Diff.ChangeType.Modified);
                int deleted = changes.Count(c => c.Type == global::PackageGuardian.Core.Diff.ChangeType.Deleted);
                int renamed = changes.Count(c => c.Type == global::PackageGuardian.Core.Diff.ChangeType.Renamed);
                
                AddStatItem(statsContainer, "Files Changed", changes.Count.ToString());
                if (added > 0) AddStatItem(statsContainer, "Added", added.ToString(), new Color(0.21f, 0.75f, 0.69f));
                if (modified > 0) AddStatItem(statsContainer, "Modified", modified.ToString(), new Color(0.36f, 0.64f, 1.0f));
                if (deleted > 0) AddStatItem(statsContainer, "Deleted", deleted.ToString(), new Color(0.89f, 0.29f, 0.29f));
                if (renamed > 0) AddStatItem(statsContainer, "Renamed", renamed.ToString(), new Color(0.89f, 0.65f, 0.29f));
            }
            catch
            {
                // Ignore errors in stats
            }
        }
        
        private void AddStatItem(VisualElement container, string label, string value, Color? valueColor = null)
        {
            var statItem = new VisualElement();
            statItem.style.flexDirection = FlexDirection.Column;
            statItem.style.marginRight = 24;
            statItem.style.marginBottom = 8;
            
            var statLabel = new Label(label);
            statLabel.style.fontSize = 10;
            statLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            statLabel.style.marginBottom = 4;
            statItem.Add(statLabel);
            
            var statValue = new Label(value);
            statValue.style.fontSize = 16;
            statValue.style.unityFontStyleAndWeight = FontStyle.Bold;
            statValue.style.color = valueColor ?? new Color(0.9f, 0.9f, 0.9f);
            statItem.Add(statValue);
            
            container.Add(statItem);
        }
        
        private void LoadFiles(string commitId)
        {
            _filesList.Clear();
            
            try
            {
                var repo = RepositoryService.Instance.Repository;
                var commit = repo.Store.ReadObject(commitId) as global::PackageGuardian.Core.Objects.Commit;
                
                if (commit == null || commit.Parents == null || commit.Parents.Count == 0)
                {
                    var emptyLabel = new Label("Initial commit - no file changes");
                    emptyLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
                    emptyLabel.style.marginTop = 20;
                    emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    _filesList.Add(emptyLabel);
                    return;
                }
                
                string parentId = BitConverter.ToString(commit.Parents[0]).Replace("-", "").ToLower();
                
                var diffEngine = new global::PackageGuardian.Core.Diff.DiffEngine(repo.Store);
                var changes = diffEngine.CompareCommits(parentId, commitId);
                
                if (changes.Count == 0)
                {
                    var emptyLabel = new Label("No file changes");
                    emptyLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
                    emptyLabel.style.marginTop = 20;
                    emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    _filesList.Add(emptyLabel);
                    return;
                }
                
                // Group files by type
                var addedFiles = changes.Where(c => c.Type == global::PackageGuardian.Core.Diff.ChangeType.Added).ToList();
                var modifiedFiles = changes.Where(c => c.Type == global::PackageGuardian.Core.Diff.ChangeType.Modified).ToList();
                var deletedFiles = changes.Where(c => c.Type == global::PackageGuardian.Core.Diff.ChangeType.Deleted).ToList();
                var renamedFiles = changes.Where(c => c.Type == global::PackageGuardian.Core.Diff.ChangeType.Renamed).ToList();
                
                // Add grouped file lists
                if (addedFiles.Count > 0)
                {
                    AddFileGroup(_filesList, "Added", addedFiles, new Color(0.21f, 0.75f, 0.69f));
                }
                if (modifiedFiles.Count > 0)
                {
                    AddFileGroup(_filesList, "Modified", modifiedFiles, new Color(0.36f, 0.64f, 1.0f));
                }
                if (renamedFiles.Count > 0)
                {
                    AddFileGroup(_filesList, "Renamed", renamedFiles, new Color(0.89f, 0.65f, 0.29f));
                }
                if (deletedFiles.Count > 0)
                {
                    AddFileGroup(_filesList, "Deleted", deletedFiles, new Color(0.89f, 0.29f, 0.29f));
                }
            }
            catch (Exception ex)
            {
                var errorLabel = new Label($"Error loading files: {ex.Message}");
                errorLabel.style.color = new Color(0.89f, 0.29f, 0.29f);
                _filesList.Add(errorLabel);
            }
        }
        
        private void AddFileGroup(VisualElement container, string groupTitle, List<global::PackageGuardian.Core.Diff.FileChange> files, Color accentColor)
        {
            var groupContainer = new VisualElement();
            groupContainer.style.marginBottom = 16;
            
            var groupHeader = new VisualElement();
            groupHeader.style.flexDirection = FlexDirection.Row;
            groupHeader.style.alignItems = Align.Center;
            groupHeader.style.marginBottom = 8;
            groupHeader.style.paddingBottom = 6;
            groupHeader.style.borderBottomWidth = 1;
            groupHeader.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f);
            
            var groupTitleLabel = new Label(groupTitle);
            groupTitleLabel.style.fontSize = 11;
            groupTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            groupTitleLabel.style.color = accentColor;
            groupTitleLabel.style.marginRight = 8;
            groupHeader.Add(groupTitleLabel);
            
            var countLabel = new Label($"({files.Count})");
            countLabel.style.fontSize = 10;
            countLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            groupHeader.Add(countLabel);
            
            groupContainer.Add(groupHeader);
            
            foreach (var change in files)
            {
                var item = CreateFileChangeItem(change, accentColor);
                groupContainer.Add(item);
            }
            
            container.Add(groupContainer);
        }
        
        private VisualElement CreateFileChangeItem(global::PackageGuardian.Core.Diff.FileChange change, Color accentColor)
        {
            var item = new VisualElement();
            item.AddToClassList("pg-file-item");
            item.style.flexDirection = FlexDirection.Row;
            item.style.alignItems = Align.Center;
            item.style.paddingTop = 8;
            item.style.paddingBottom = 8;
            item.style.paddingLeft = 12;
            item.style.paddingRight = 12;
            item.style.marginBottom = 2;
            item.style.borderLeftWidth = 3;
            item.style.borderLeftColor = accentColor;
            
            string icon = "+";
            switch (change.Type)
            {
                case global::PackageGuardian.Core.Diff.ChangeType.Modified:
                    icon = "~";
                    break;
                case global::PackageGuardian.Core.Diff.ChangeType.Deleted:
                    icon = "-";
                    break;
                case global::PackageGuardian.Core.Diff.ChangeType.Renamed:
                    icon = "→";
                    break;
            }
            
            var iconLabel = new Label(icon);
            iconLabel.style.width = 20;
            iconLabel.style.fontSize = 12;
            iconLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            iconLabel.style.color = accentColor;
            iconLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            iconLabel.style.marginRight = 10;
            item.Add(iconLabel);
            
            // File path
            string displayPath = change.Path;
            if (change.Type == global::PackageGuardian.Core.Diff.ChangeType.Renamed && !string.IsNullOrEmpty(change.NewPath))
            {
                displayPath = $"{change.Path} → {change.NewPath}";
                if (change.SimilarityScore > 0)
                    displayPath += $" ({change.SimilarityScore:P0})";
            }
            else if (change.Type == global::PackageGuardian.Core.Diff.ChangeType.Copied && !string.IsNullOrEmpty(change.NewPath))
            {
                displayPath = $"{change.Path} ⇒ {change.NewPath}";
            }
            
            var pathLabel = new Label(displayPath);
            pathLabel.style.flexGrow = 1;
            pathLabel.style.fontSize = 11;
            pathLabel.style.color = new Color(0.9f, 0.9f, 0.9f);
            pathLabel.style.whiteSpace = WhiteSpace.Normal;
            item.Add(pathLabel);
            
            var viewBtn = new Button(() => ViewDiff(change));
            viewBtn.text = "View Diff";
            viewBtn.AddToClassList("pg-button");
            viewBtn.AddToClassList("pg-button-small");
            viewBtn.style.marginLeft = 8;
            item.Add(viewBtn);
            
            return item;
        }
        
        private void ViewDiff(global::PackageGuardian.Core.Diff.FileChange change)
        {
            DiffWindow.ShowDiff(change, "");
        }
        
        private void ShowEmptyState()
        {
            _commitHeaderSection.style.display = DisplayStyle.None;
            _commitMetaSection.style.display = DisplayStyle.None;
            _statsSection.style.display = DisplayStyle.None;
            _filesSection.style.display = DisplayStyle.None;
            _emptyStateLabel.style.display = DisplayStyle.Flex;
        }
        
        private void HideEmptyState()
        {
            _commitHeaderSection.style.display = DisplayStyle.Flex;
            _commitMetaSection.style.display = DisplayStyle.Flex;
            _statsSection.style.display = DisplayStyle.Flex;
            _filesSection.style.display = DisplayStyle.Flex;
            _emptyStateLabel.style.display = DisplayStyle.None;
        }
        
        public void Refresh()
        {
            _graphView?.Refresh();
        }
        
        public void SetSelectedCommit(string commitId)
        {
            _graphView?.SetSelectedCommit(commitId);
        }
    }
}
