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
            }
            else
            {
                RemoveFromClassList("pg-graph-item-selected");
            }
        }
        
        private void BuildUI()
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            
            // Lanes + Commit Node
            var lanesContainer = new VisualElement();
            lanesContainer.AddToClassList("pg-graph-lanes");
            lanesContainer.style.width = 80;
            lanesContainer.style.height = 50;
            lanesContainer.style.flexDirection = FlexDirection.Row;
            lanesContainer.style.alignItems = Align.Center;
            lanesContainer.style.justifyContent = Justify.Center;
            
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
                    
                    // Style based on commit type
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
            
            // Commit Type Icon
            var iconContainer = new VisualElement();
            iconContainer.AddToClassList("pg-graph-commit-icon");
            
            string iconText;
            string iconClass;
            
            // Determine icon based on commit type
            if (_node.IsStash)
            {
                iconText = "S";
                iconClass = "pg-icon-auto";
            }
            else if (_node.Message.Contains("UPM:") || _node.Message.Contains("Package"))
            {
                iconText = "P";
                iconClass = "pg-icon-package";
            }
            else if (_node.ParentCount > 1)
            {
                iconText = "M";
                iconClass = "pg-icon-merge";
            }
            else if (_node.Message.Contains("Manual"))
            {
                iconText = "U";
                iconClass = "pg-icon-manual";
            }
            else
            {
                iconText = "A";
                iconClass = "pg-icon-auto";
            }
            
            var icon = new Label(iconText);
            icon.style.fontSize = 12;
            icon.style.unityTextAlign = TextAnchor.MiddleCenter;
            iconContainer.Add(icon);
            iconContainer.AddToClassList(iconClass);
            
            Add(iconContainer);
            
            // Commit Info
            var infoContainer = new VisualElement();
            infoContainer.AddToClassList("pg-graph-commit-info");
            
            // First line: Refs + Message
            var firstLine = new VisualElement();
            firstLine.style.flexDirection = FlexDirection.Row;
            firstLine.style.alignItems = Align.Center;
            firstLine.style.marginBottom = 4;
            
            // Refs (branches/tags)
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
                    refBadge.style.fontSize = 10;
                    refBadge.style.marginRight = 6;
                    firstLine.Add(refBadge);
                }
            }
            
            // Message
            var message = new Label(TruncateMessage(_node.Message));
            message.AddToClassList("pg-graph-commit-message");
            firstLine.Add(message);
            
            infoContainer.Add(firstLine);
            
            // Second line: Meta info
            var meta = new VisualElement();
            meta.AddToClassList("pg-graph-commit-meta");
            
            var author = new Label(_node.Author);
            author.AddToClassList("pg-graph-commit-author");
            meta.Add(author);
            
            var dt = DateTimeOffset.FromUnixTimeSeconds(_node.Timestamp);
            var time = new Label(FormatTimeAgo(dt));
            time.AddToClassList("pg-graph-commit-time");
            meta.Add(time);
            
            var hash = new Label(_node.CommitId.Substring(0, 8));
            hash.AddToClassList("pg-graph-commit-hash");
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
