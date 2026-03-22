using System.Collections.Generic;

namespace YUCP.Components.Editor
{
    internal sealed class BlendshapeMarkdownDocument
    {
        public BlendshapeMarkdownSection Root { get; } = new BlendshapeMarkdownSection
        {
            Key = "root",
            Title = "Root",
            FullPath = string.Empty,
            Depth = 0,
            SourceIndex = -1
        };

        public int HeadingCount { get; set; }
        public int TotalBlendshapeCount { get; set; }
    }

    internal abstract class BlendshapeMarkdownNode
    {
        public int Depth { get; set; }
        public int SourceIndex { get; set; }
    }

    internal sealed class BlendshapeMarkdownSection : BlendshapeMarkdownNode
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public List<BlendshapeMarkdownNode> Children { get; } = new List<BlendshapeMarkdownNode>();

        public int CountBlendshapeDescendants()
        {
            int total = 0;

            for (int index = 0; index < Children.Count; index++)
            {
                if (Children[index] is BlendshapeMarkdownBlendshapeItem)
                {
                    total++;
                }
                else if (Children[index] is BlendshapeMarkdownSection section)
                {
                    total += section.CountBlendshapeDescendants();
                }
            }

            return total;
        }
    }

    internal sealed class BlendshapeMarkdownBlendshapeItem : BlendshapeMarkdownNode
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    internal readonly struct BlendshapeMarkdownHeadingMatch
    {
        public BlendshapeMarkdownHeadingMatch(string ruleName, int depth, string title)
        {
            RuleName = ruleName;
            Depth = depth;
            Title = title;
        }

        public string RuleName { get; }
        public int Depth { get; }
        public string Title { get; }
    }

    internal readonly struct BlendshapeMarkdownSectionStyle
    {
        public BlendshapeMarkdownSectionStyle(bool hasCustomColors, UnityEngine.Color textColor, UnityEngine.Color backgroundColor)
        {
            HasCustomColors = hasCustomColors;
            TextColor = textColor;
            BackgroundColor = backgroundColor;
        }

        public bool HasCustomColors { get; }
        public UnityEngine.Color TextColor { get; }
        public UnityEngine.Color BackgroundColor { get; }
    }
}
