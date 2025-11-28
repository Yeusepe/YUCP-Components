using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace YUCP.Components.PackageGuardian.Editor.Windows.Graph
{
    /// <summary>
    /// Graph view with virtualization, grouping, and organized commit display.
    /// Uses ListView for performance and groups commits by time period.
    /// </summary>
    public class GroupedGraphView : VisualElement
    {
        private GraphViewModel _viewModel;
        private ScrollView _scrollView;
        private VisualElement _groupsContainer;
        private List<CommitGroup> _groups;
        private Dictionary<string, GraphRow> _rowCache;
        private string _selectedCommitId;
        private bool _showStashesSeparately;
        private Toggle _stashToggle;
        
        public System.Action<GraphNode> OnCommitSelected { get; set; }
        
        public GroupedGraphView(bool showStashesSeparately = true)
        {
            style.flexGrow = 1;
            _viewModel = new GraphViewModel();
            _groups = new List<CommitGroup>();
            _rowCache = new Dictionary<string, GraphRow>();
            _showStashesSeparately = showStashesSeparately;
            
            CreateUI();
        }
        
        private void CreateUI()
        {
            // Filter bar
            var filterBar = new VisualElement();
            filterBar.AddToClassList("pg-filter-bar");
            filterBar.style.flexShrink = 0;
            
            _stashToggle = new Toggle("Show Stashes Separately");
            _stashToggle.value = _showStashesSeparately;
            _stashToggle.tooltip = "When enabled, stashes are grouped separately and collapsed by default";
            _stashToggle.RegisterValueChangedCallback(evt =>
            {
                _showStashesSeparately = evt.newValue;
                Refresh();
            });
            filterBar.Add(_stashToggle);
            
            Add(filterBar);
            
            _scrollView = new ScrollView();
            _scrollView.AddToClassList("pg-scrollview");
            _scrollView.style.flexGrow = 1;
            _scrollView.style.minHeight = 0;
            
            _groupsContainer = _scrollView.contentContainer;
            _groupsContainer.style.flexDirection = FlexDirection.Column;
            
            Add(_scrollView);
        }
        
        public void Refresh()
        {
            _viewModel.Load();
            _groups = CommitGrouper.GroupCommits(_viewModel.Nodes, _showStashesSeparately);
            _rowCache.Clear();
            
            UpdateGroupsDisplay();
        }
        
        private void UpdateGroupsDisplay()
        {
            _groupsContainer.Clear();
            
            foreach (var group in _groups)
            {
                var groupItem = new CommitGroupItem();
                groupItem.SetGroup(group, OnCommitSelected, OnGroupToggle, _selectedCommitId);
                _groupsContainer.Add(groupItem);
            }
        }
        
        private void OnGroupToggle(CommitGroup group)
        {
            group.IsExpanded = !group.IsExpanded;
            UpdateGroupsDisplay();
        }
        
        public void SetSelectedCommit(string commitId)
        {
            _selectedCommitId = commitId;
            _viewModel.SelectedCommitId = commitId;
            UpdateGroupsDisplay();
        }
    }
    
    /// <summary>
    /// Visual element representing a commit group with expand/collapse functionality.
    /// </summary>
        public class CommitGroupItem : VisualElement
        {
        private CommitGroup _group;
        private VisualElement _header;
        private VisualElement _content;
        private Label _titleLabel;
        private Label _countLabel;
        private bool _isExpanded;
        private bool _isStashGroup;
        
        public CommitGroupItem()
        {
            AddToClassList("pg-commit-group");
            style.flexDirection = FlexDirection.Column;
            style.flexShrink = 0;
            style.minHeight = 0;
            style.marginBottom = 12;
            style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            style.borderTopLeftRadius = 0;
            style.borderTopRightRadius = 0;
            style.borderBottomLeftRadius = 0;
            style.borderBottomRightRadius = 0;
            style.borderTopWidth = 1;
            style.borderBottomWidth = 1;
            style.borderLeftWidth = 1;
            style.borderRightWidth = 1;
            style.borderTopColor = new Color(0.165f, 0.165f, 0.165f);
            style.borderBottomColor = new Color(0.165f, 0.165f, 0.165f);
            style.borderLeftColor = new Color(0.165f, 0.165f, 0.165f);
            style.borderRightColor = new Color(0.165f, 0.165f, 0.165f);
            style.overflow = Overflow.Hidden;
            
            CreateHeader();
            CreateContent();
        }
        
        private void CreateHeader()
        {
            _header = new VisualElement();
            _header.AddToClassList("pg-commit-group-header");
            _header.style.flexDirection = FlexDirection.Row;
            _header.style.alignItems = Align.Center;
            _header.style.paddingTop = 14;
            _header.style.paddingBottom = 14;
            _header.style.paddingLeft = 16;
            _header.style.paddingRight = 16;
            _header.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            _header.style.flexShrink = 0;
            _header.style.minHeight = 48;
            _header.style.cursor = new UnityEngine.UIElements.Cursor { texture = null };
            _header.RegisterCallback<ClickEvent>(OnHeaderClick);
            
            _header.RegisterCallback<MouseEnterEvent>(OnHeaderHoverEnter);
            _header.RegisterCallback<MouseLeaveEvent>(OnHeaderHoverLeave);
            
            var expandIcon = new Label("▶");
            expandIcon.name = "expandIcon";
            expandIcon.style.width = 16;
            expandIcon.style.height = 16;
            expandIcon.style.fontSize = 10;
            expandIcon.style.color = new Color(0.69f, 0.69f, 0.69f);
            expandIcon.style.marginRight = 12;
            expandIcon.style.flexShrink = 0;
            expandIcon.style.unityTextAlign = TextAnchor.MiddleCenter;
            _header.Add(expandIcon);
            
            _titleLabel = new Label();
            _titleLabel.style.fontSize = 14;
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.color = new Color(0.95f, 0.95f, 0.95f);
            _titleLabel.style.flexGrow = 1;
            _titleLabel.style.whiteSpace = WhiteSpace.Normal;
            _header.Add(_titleLabel);
            
            _countLabel = new Label();
            _countLabel.style.fontSize = 10;
            _countLabel.style.color = new Color(0.69f, 0.69f, 0.69f);
            _countLabel.style.marginLeft = 12;
            _countLabel.style.paddingLeft = 8;
            _countLabel.style.paddingRight = 8;
            _countLabel.style.paddingTop = 4;
            _countLabel.style.paddingBottom = 4;
            _countLabel.style.backgroundColor = new Color(0.165f, 0.165f, 0.165f);
            _countLabel.style.borderTopLeftRadius = 3;
            _countLabel.style.borderTopRightRadius = 3;
            _countLabel.style.borderBottomLeftRadius = 3;
            _countLabel.style.borderBottomRightRadius = 3;
            _countLabel.style.flexShrink = 0;
            _header.Add(_countLabel);
            
            Add(_header);
        }
        
        private void CreateContent()
        {
            _content = new VisualElement();
            _content.AddToClassList("pg-commit-group-content");
            _content.style.flexDirection = FlexDirection.Column;
            _content.style.flexShrink = 0;
            _content.style.minHeight = 0;
            _content.style.paddingLeft = 16;
            _content.style.paddingRight = 16;
            _content.style.paddingTop = 4;
            _content.style.paddingBottom = 12;
            _content.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f);
            Add(_content);
        }
        
        public void SetGroup(CommitGroup group, System.Action<GraphNode> onCommitSelected, System.Action<CommitGroup> onToggle, string selectedCommitId)
        {
            _group = group;
            _isExpanded = group.IsExpanded;
            _isStashGroup = group.IsStashGroup;
            
            // Update header
            _titleLabel.text = group.Title;
            if (!string.IsNullOrEmpty(group.Subtitle))
            {
                _titleLabel.text += $" • {group.Subtitle}";
            }
            
            _countLabel.text = group.Count.ToString();
            
            // Update expand icon
            var expandIcon = _header.Q<Label>("expandIcon");
            if (expandIcon != null)
            {
                expandIcon.text = _isExpanded ? "▼" : "▶";
            }
            
            if (group.IsStashGroup)
            {
                _header.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
                _header.style.borderLeftWidth = 3;
                _header.style.borderLeftColor = new Color(0.55f, 0.36f, 0.96f);
                _titleLabel.style.color = new Color(0.75f, 0.65f, 0.95f);
                _countLabel.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
                _countLabel.style.color = new Color(0.75f, 0.65f, 0.95f);
            }
            else
            {
                _header.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
                _header.style.borderLeftWidth = 0;
                _titleLabel.style.color = new Color(0.95f, 0.95f, 0.95f);
            }
            
            _content.Clear();
            if (_isExpanded)
            {
                _content.style.display = DisplayStyle.Flex;
                foreach (var commit in group.Commits)
                {
                    var row = new GraphRow(commit, onCommitSelected);
                    if (commit.CommitId == selectedCommitId)
                    {
                        row.SetSelected(true);
                    }
                    _content.Add(row);
                }
            }
            else
            {
                _content.style.display = DisplayStyle.None;
            }
            
            _header.userData = onToggle;
        }
        
        private void OnHeaderClick(ClickEvent evt)
        {
            var onToggle = _header.userData as System.Action<CommitGroup>;
            onToggle?.Invoke(_group);
        }
        
        private void OnHeaderHoverEnter(MouseEnterEvent evt)
        {
            _header.style.backgroundColor = new Color(0.165f, 0.165f, 0.165f);
        }
        
        private void OnHeaderHoverLeave(MouseLeaveEvent evt)
        {
            _header.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
        }
    }
}

