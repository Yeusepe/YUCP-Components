using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Components.PackageGuardian.Editor.Windows.Graph
{
    /// <summary>
    /// Visual element for a single commit row in the graph - GitHub/GitKraken style.
    /// </summary>
    public class GraphRow : VisualElement
    {
        private static readonly Color[] LaneColors = new[]
        {
            new Color(0.02f, 0.71f, 0.83f, 1f), // #06b6d4 - Cyan
            new Color(0.55f, 0.36f, 0.96f, 1f), // #8b5cf6 - Purple
            new Color(0.13f, 0.77f, 0.37f, 1f), // #22c55e - Green
            new Color(0.96f, 0.62f, 0.04f, 1f), // #f59e0b - Orange
            new Color(0.93f, 0.29f, 0.60f, 1f), // #ec4899 - Pink
        };
        
        private GraphNode _node;
        private Action<GraphNode> _onSelected;
        private bool _isSelected;
        
        public GraphRow(GraphNode node, Action<GraphNode> onSelected)
        {
            _node = node;
            _onSelected = onSelected;
            
            AddToClassList("pg-graph-item");
            this.RegisterCallback<ClickEvent>(OnClick);
            
            BuildUI();
        }
        
        public void SetSelected(bool selected)
        {
            _isSelected = selected;
            if (selected)
            {
                AddToClassList("pg-graph-item-selected");
                style.backgroundColor = new Color(0.15f, 0.2f, 0.3f);
                style.borderLeftWidth = 3;
                style.borderLeftColor = new Color(0.36f, 0.64f, 1.0f);
            }
            else
            {
                RemoveFromClassList("pg-graph-item-selected");
                style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
                style.borderLeftWidth = 0;
            }
        }
        
        private void BuildUI()
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.flexShrink = 0;
            style.minHeight = 60;
            style.paddingTop = 10;
            style.paddingBottom = 10;
            style.paddingLeft = 12;
            style.paddingRight = 12;
            style.marginBottom = 2;
            style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            style.borderTopLeftRadius = 0;
            style.borderTopRightRadius = 0;
            style.borderBottomLeftRadius = 0;
            style.borderBottomRightRadius = 0;
            
            RegisterCallback<MouseEnterEvent>(evt => {
                if (!_isSelected)
                {
                    style.backgroundColor = new Color(0.13f, 0.13f, 0.13f);
                }
            });
            RegisterCallback<MouseLeaveEvent>(evt => {
                if (!_isSelected)
                {
                    style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
                }
            });
            
            var lanesContainer = new VisualElement();
            lanesContainer.AddToClassList("pg-graph-lanes");
            lanesContainer.style.width = 60;
            lanesContainer.style.minWidth = 60;
            lanesContainer.style.maxWidth = 60;
            lanesContainer.style.height = 40;
            lanesContainer.style.flexShrink = 0;
            lanesContainer.style.flexDirection = FlexDirection.Row;
            lanesContainer.style.alignItems = Align.Center;
            lanesContainer.style.justifyContent = Justify.Center;
            lanesContainer.style.marginRight = 8;
            lanesContainer.style.alignSelf = Align.Center;
            
            // Draw lanes with commit node
            for (int i = 0; i < 5; i++)
            {
                var lane = new VisualElement();
                lane.style.width = 16;
                lane.style.height = Length.Percent(100);
                lane.style.alignItems = Align.Center;
                lane.style.justifyContent = Justify.Center;
                
                // Draw commit node on the active lane
                if (i == _node.Lane % 5)
                {
                    var node = new VisualElement();
                    node.AddToClassList("pg-graph-node");
                    
                    // Style using commit type
                    if (_node.IsStash)
                    {
                        node.AddToClassList("pg-graph-node-stash");
                    }
                    else if (_node.ParentCount > 1)
                    {
                        node.AddToClassList("pg-graph-node-merge");
                    }
                    else if (_node.Refs.Count > 0)
                    {
                        node.AddToClassList("pg-graph-node-branch");
                    }
                    else
                    {
                        node.AddToClassList("pg-graph-node-main");
                    }
                    
                    lane.Add(node);
                }
                
                lanesContainer.Add(lane);
            }
            
            Add(lanesContainer);
            
            var iconContainer = new VisualElement();
            iconContainer.AddToClassList("pg-graph-commit-icon");
            iconContainer.style.width = 28;
            iconContainer.style.height = 28;
            iconContainer.style.minWidth = 28;
            iconContainer.style.maxWidth = 28;
            iconContainer.style.flexShrink = 0;
            iconContainer.style.marginRight = 8;
            iconContainer.style.borderTopLeftRadius = 3;
            iconContainer.style.borderTopRightRadius = 3;
            iconContainer.style.borderBottomLeftRadius = 3;
            iconContainer.style.borderBottomRightRadius = 3;
            iconContainer.style.unityTextAlign = TextAnchor.MiddleCenter;
            iconContainer.style.justifyContent = Justify.Center;
            iconContainer.style.alignItems = Align.Center;
            iconContainer.style.alignSelf = Align.Center;
            
            string iconText;
            Color iconBgColor;
            Color iconTextColor;
            
            if (_node.IsStash)
            {
                iconText = "S";
                iconBgColor = new Color(0.55f, 0.36f, 0.96f, 0.2f);
                iconTextColor = new Color(0.75f, 0.65f, 0.95f);
            }
            else if (_node.Message.Contains("UPM:") || _node.Message.Contains("Package"))
            {
                iconText = "P";
                iconBgColor = new Color(0.36f, 0.64f, 1.0f, 0.2f);
                iconTextColor = new Color(0.36f, 0.64f, 1.0f);
            }
            else if (_node.ParentCount > 1)
            {
                iconText = "M";
                iconBgColor = new Color(0.89f, 0.65f, 0.29f, 0.2f);
                iconTextColor = new Color(0.89f, 0.65f, 0.29f);
            }
            else if (_node.Message.Contains("Manual"))
            {
                iconText = "U";
                iconBgColor = new Color(0.21f, 0.75f, 0.69f, 0.2f);
                iconTextColor = new Color(0.21f, 0.75f, 0.69f);
            }
            else
            {
                iconText = "A";
                iconBgColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);
                iconTextColor = new Color(0.7f, 0.7f, 0.7f);
            }
            
            iconContainer.style.backgroundColor = iconBgColor;
            
            var icon = new Label(iconText);
            icon.style.fontSize = 11;
            icon.style.unityFontStyleAndWeight = FontStyle.Bold;
            icon.style.unityTextAlign = TextAnchor.MiddleCenter;
            icon.style.color = iconTextColor;
            iconContainer.Add(icon);
            
            Add(iconContainer);
            
            var infoContainer = new VisualElement();
            infoContainer.AddToClassList("pg-graph-commit-info");
            infoContainer.style.flexGrow = 1;
            infoContainer.style.flexShrink = 1;
            infoContainer.style.flexDirection = FlexDirection.Column;
            infoContainer.style.minWidth = 0;
            infoContainer.style.justifyContent = Justify.Center;
            
            var firstLine = new VisualElement();
            firstLine.style.flexDirection = FlexDirection.Row;
            firstLine.style.alignItems = Align.Center;
            firstLine.style.marginBottom = 4;
            firstLine.style.flexWrap = Wrap.Wrap;
            firstLine.style.minWidth = 0;
            
            if (_node.Refs.Count > 0)
            {
                foreach (var refName in _node.Refs)
                {
                    var refBadge = new Label(refName);
                    refBadge.style.backgroundColor = new Color(0.55f, 0.36f, 0.96f, 0.2f);
                    refBadge.style.color = new Color(0.55f, 0.36f, 0.96f, 1f);
                    refBadge.style.paddingTop = 2;
                    refBadge.style.paddingBottom = 2;
                    refBadge.style.paddingLeft = 6;
                    refBadge.style.paddingRight = 6;
                    refBadge.style.borderTopLeftRadius = 3;
                    refBadge.style.borderTopRightRadius = 3;
                    refBadge.style.borderBottomLeftRadius = 3;
                    refBadge.style.borderBottomRightRadius = 3;
                    refBadge.style.fontSize = 9;
                    refBadge.style.marginRight = 6;
                    refBadge.style.marginBottom = 2;
                    refBadge.style.flexShrink = 0;
                    firstLine.Add(refBadge);
                }
            }
            
            var message = new Label(TruncateMessage(_node.Message));
            message.AddToClassList("pg-graph-commit-message");
            message.style.fontSize = 13;
            message.style.unityFontStyleAndWeight = FontStyle.Normal;
            message.style.color = new Color(0.95f, 0.95f, 0.95f);
            message.style.flexGrow = 1;
            message.style.flexShrink = 1;
            message.style.whiteSpace = WhiteSpace.Normal;
            message.style.overflow = Overflow.Hidden;
            message.style.textOverflow = TextOverflow.Ellipsis;
            message.style.marginBottom = 2;
            message.style.minWidth = 0;
            firstLine.Add(message);
            
            infoContainer.Add(firstLine);
            
            var meta = new VisualElement();
            meta.AddToClassList("pg-graph-commit-meta");
            meta.style.flexDirection = FlexDirection.Row;
            meta.style.alignItems = Align.Center;
            meta.style.flexWrap = Wrap.Wrap;
            meta.style.minWidth = 0;
            
            if (!string.IsNullOrEmpty(_node.Author))
            {
                var author = new Label(_node.Author);
                author.style.fontSize = 10;
                author.style.color = new Color(0.69f, 0.69f, 0.69f);
                author.style.marginRight = 8;
                author.style.flexShrink = 0;
                meta.Add(author);
            }
            
            var dt = DateTimeOffset.FromUnixTimeSeconds(_node.Timestamp);
            var time = new Label(FormatTimeAgo(dt));
            time.style.fontSize = 10;
            time.style.color = new Color(0.5f, 0.5f, 0.5f);
            time.style.marginRight = 8;
            time.style.flexShrink = 0;
            meta.Add(time);
            
            var hash = new Label(_node.CommitId.Substring(0, 8));
            hash.style.fontSize = 9;
            hash.style.color = new Color(0.5f, 0.5f, 0.5f);
            hash.style.flexShrink = 0;
            meta.Add(hash);
            
            infoContainer.Add(meta);
            
            Add(infoContainer);
        }
        
        private string TruncateMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "";
            
            // Get first line only
            int newlineIndex = message.IndexOfAny(new[] { '\r', '\n' });
            if (newlineIndex >= 0)
            {
                message = message.Substring(0, newlineIndex);
            }
            
            // Truncate if too long
            if (message.Length > 60)
            {
                message = message.Substring(0, 57) + "...";
            }
            
            return message;
        }
        
        private string FormatTimeAgo(DateTimeOffset timestamp)
        {
            var now = DateTimeOffset.Now;
            var diff = now - timestamp;
            
            if (diff.TotalMinutes < 1)
                return "just now";
            if (diff.TotalMinutes < 60)
                return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24)
                return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}d ago";
            if (diff.TotalDays < 30)
                return $"{(int)(diff.TotalDays / 7)}w ago";
            
            return timestamp.ToLocalTime().ToString("MMM d, yyyy");
        }
        
        private void OnClick(ClickEvent evt)
        {
            _onSelected?.Invoke(_node);
        }
    }
}
