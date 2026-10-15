using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    public enum BlendshapeMarkdownPreset
    {
        MarkdownHashes = 0,
        EqualsWrapped = 1,
        PipeDashWrapped = 2,
        DoublePipeDashWrapped = 3,
        MixedConvenience = 4,
        Custom = 5
    }

    public enum BlendshapeMarkdownHeadingRuleMode
    {
        PrefixToken = 0,
        WrappedToken = 1,
        RawRegex = 2
    }

    public enum BlendshapeMarkdownColorMatchTarget
    {
        SectionTitle = 0,
        FullPath = 1
    }

    public enum BlendshapeMarkdownColorMatchMode
    {
        Contains = 0,
        StartsWith = 1,
        EndsWith = 2,
        Exact = 3,
        Regex = 4
    }

    [Serializable]
    public class BlendshapeMarkdownHeadingRule
    {
        [Tooltip("A name for this rule.")]
        public string name = "Heading Rule";

        [Tooltip("Uses this rule. Turn it off to keep the rule without using it.")]
        public bool enabled = true;

        [Tooltip("Prefix matches # Title; Wrapped matches ==Title== or |-Title-|. Use Regex only when neither fits.")]
        public BlendshapeMarkdownHeadingRuleMode mode = BlendshapeMarkdownHeadingRuleMode.PrefixToken;

        [Tooltip("The repeated text that marks a heading. Its count sets the depth, so ## is nested under #.")]
        public string repeatToken = "#";

        [Tooltip("Text before the repeated token, such as | in |-Title-|.")]
        public string leftWrapper = "";

        [Tooltip("Text after the repeated token, such as | in |-Title-|.")]
        public string rightWrapper = "";

        [Tooltip("Requires a space after the token, so # Title matches but #Title doesn't.")]
        public bool requireWhitespaceAfterPrefix = true;

        [Tooltip("Matches upper and lower case alike.")]
        public bool ignoreCase = false;

        [Tooltip("A regular expression with one group for the depth and one for the title. A pattern that takes too long is skipped.")]
        public string rawRegex = "";

        [Tooltip("The group that sets the depth: a number, or marker text whose length is the depth.")]
        [Min(1)]
        public int rawRegexDepthGroup = 1;

        [Tooltip("The group that holds the heading title.")]
        [Min(1)]
        public int rawRegexTitleGroup = 2;

        [Tooltip("Removes spaces around the title.")]
        public bool trimTitleWhitespace = true;
    }

    [Serializable]
    public class BlendshapeMarkdownColorRule
    {
        [Tooltip("A name for this rule.")]
        public string name = "Color Rule";

        [Tooltip("Uses this rule. Turn it off to keep the rule without using it.")]
        public bool enabled = true;

        [Tooltip("Title matches the section's own name. Full Path matches its whole path, such as Body/Head.")]
        public BlendshapeMarkdownColorMatchTarget target = BlendshapeMarkdownColorMatchTarget.SectionTitle;

        [Tooltip("How the text is compared with the section.")]
        public BlendshapeMarkdownColorMatchMode matchMode = BlendshapeMarkdownColorMatchMode.Contains;

        [Tooltip("The text or regular expression to match. A rule without text is skipped.")]
        public string matchText = "";

        [Tooltip("Matches upper and lower case alike.")]
        public bool ignoreCase = true;

        [Tooltip("Preset uses a Pretty Hierarchy color style. Custom uses your own colors.")]
        public bool usePresetColors = true;

        [Tooltip("The color style for matching sections.")]
        public PrettyHierarchyPreset preset = PrettyHierarchyPreset.Ocean;

        [Tooltip("Header text color for matching sections.")]
        public Color textColor = Color.white;

        [Tooltip("Header background color for matching sections.")]
        public Color backgroundColor = new Color(0.15f, 0.35f, 0.5f, 1f);
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("YUCP/Blendshape Markdown")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [SupportBanner]
    public class BlendshapeMarkdownData : MonoBehaviour, IEditorOnly
    {
        [Header("Target Renderer")]
        [Tooltip("The renderer to organize. Leave empty to use the one on this object.")]
        public SkinnedMeshRenderer targetRenderer;

        [Header("Native Inspector Integration")]
        [Tooltip("Shows the renderer's blendshapes in collapsible sections in its inspector.")]
        public bool enableNativeInspectorIntegration = true;

        [Tooltip("Replaces Unity's flat blendshape list with the sections. Turn it off to keep the flat list.")]
        public bool replaceDefaultBlendshapeList = true;

        [Tooltip("Shows a search field above the sections.")]
        public bool showSearchBar = true;

        [Tooltip("Shows how many blendshapes each section holds.")]
        public bool showBlendshapeCounts = true;

        [Header("Heading Parsing")]
        [Tooltip("Common heading styles. Add their rules to yours, or replace yours with them.")]
        public BlendshapeMarkdownPreset preset = BlendshapeMarkdownPreset.MixedConvenience;

        [Tooltip("Treats / in a heading as nesting, so ==Body/Head== becomes Head inside Body.")]
        public bool useSlashAsPathSeparator = true;

        [Tooltip("Collects blendshapes that sit beside subsections into their own section.")]
        public bool showUngroupedBlendshapes = true;

        [Tooltip("The title of the ungrouped section.")]
        public string ungroupedSectionTitle = "Ungrouped";

        [Tooltip("Top-level blendshapes that start with this text, such as VRC visemes, get their own section. Leave empty to turn this off.")]
        public string topLevelAutoGroupPrefix = "VRC";

        [Tooltip("The title of the auto-grouped section.")]
        public string topLevelAutoGroupTitle = "Visemes";

        [Tooltip("Rules that find headings in blendshape names. The first rule that matches wins.")]
        public List<BlendshapeMarkdownHeadingRule> headingRules = new List<BlendshapeMarkdownHeadingRule>();

        [Header("Default Expansion")]
        [Tooltip("Top-level sections start expanded.")]
        public bool expandTopLevelByDefault = true;

        [Tooltip("Nested sections start expanded.")]
        public bool expandNestedByDefault = false;

        [Header("Section Colors")]
        [Tooltip("Colors for matching section headers. The first enabled rule that matches wins.")]
        public List<BlendshapeMarkdownColorRule> colorRules = new List<BlendshapeMarkdownColorRule>();

        [Header("Debug")]
        [Tooltip("Enable extra debug logging in editor integration code.")]
        public bool debugLogging = false;

        public SkinnedMeshRenderer GetTargetRenderer()
        {
            if (targetRenderer != null)
            {
                return targetRenderer;
            }

            return GetComponent<SkinnedMeshRenderer>();
        }

        public string GetUngroupedSectionTitle()
        {
            return string.IsNullOrWhiteSpace(ungroupedSectionTitle) ? "Ungrouped" : ungroupedSectionTitle.Trim();
        }

        public string GetTopLevelAutoGroupPrefix()
        {
            return string.IsNullOrWhiteSpace(topLevelAutoGroupPrefix) ? string.Empty : topLevelAutoGroupPrefix.Trim();
        }

        public string GetTopLevelAutoGroupTitle()
        {
            return string.IsNullOrWhiteSpace(topLevelAutoGroupTitle) ? "Visemes" : topLevelAutoGroupTitle.Trim();
        }

        public bool TargetsRenderer(SkinnedMeshRenderer renderer)
        {
            if (renderer == null)
            {
                return false;
            }

            return GetTargetRenderer() == renderer;
        }

        public void ApplyPresetRules(BlendshapeMarkdownPreset presetToApply)
        {
            preset = presetToApply;

            if (headingRules == null)
            {
                headingRules = new List<BlendshapeMarkdownHeadingRule>();
            }

            headingRules.Clear();

            switch (presetToApply)
            {
                case BlendshapeMarkdownPreset.MarkdownHashes:
                    headingRules.Add(CreateHashRule());
                    break;

                case BlendshapeMarkdownPreset.EqualsWrapped:
                    headingRules.Add(CreateEqualsRule());
                    break;

                case BlendshapeMarkdownPreset.PipeDashWrapped:
                    headingRules.Add(CreatePipeDashRule());
                    break;

                case BlendshapeMarkdownPreset.DoublePipeDashWrapped:
                    headingRules.Add(CreateDoublePipeDashRule());
                    break;

                case BlendshapeMarkdownPreset.MixedConvenience:
                    headingRules.Add(CreateHashRule());
                    headingRules.Add(CreateEqualsRule());
                    headingRules.Add(CreatePipeDashRule());
                    headingRules.Add(CreateDoublePipeDashRule());
                    break;

                case BlendshapeMarkdownPreset.Custom:
                default:
                    break;
            }
        }

        private static BlendshapeMarkdownHeadingRule CreateHashRule()
        {
            return new BlendshapeMarkdownHeadingRule
            {
                name = "Markdown Hashes",
                mode = BlendshapeMarkdownHeadingRuleMode.PrefixToken,
                repeatToken = "#",
                requireWhitespaceAfterPrefix = true,
                enabled = true,
                trimTitleWhitespace = true
            };
        }

        private static BlendshapeMarkdownHeadingRule CreateEqualsRule()
        {
            return new BlendshapeMarkdownHeadingRule
            {
                name = "Equals Wrapper",
                mode = BlendshapeMarkdownHeadingRuleMode.WrappedToken,
                repeatToken = "=",
                leftWrapper = "",
                rightWrapper = "",
                enabled = true,
                trimTitleWhitespace = true
            };
        }

        private static BlendshapeMarkdownHeadingRule CreatePipeDashRule()
        {
            return new BlendshapeMarkdownHeadingRule
            {
                name = "Pipe Dash Wrapper",
                mode = BlendshapeMarkdownHeadingRuleMode.WrappedToken,
                repeatToken = "-",
                leftWrapper = "|",
                rightWrapper = "|",
                enabled = true,
                trimTitleWhitespace = true
            };
        }

        private static BlendshapeMarkdownHeadingRule CreateDoublePipeDashRule()
        {
            return new BlendshapeMarkdownHeadingRule
            {
                name = "Double Pipe Dash Wrapper",
                mode = BlendshapeMarkdownHeadingRuleMode.WrappedToken,
                repeatToken = "-",
                leftWrapper = "||",
                rightWrapper = "||",
                enabled = true,
                trimTitleWhitespace = true
            };
        }

        private void Reset()
        {
            targetRenderer = GetComponent<SkinnedMeshRenderer>();

            if (headingRules == null || headingRules.Count == 0)
            {
                ApplyPresetRules(preset);
            }

            if (colorRules == null)
            {
                colorRules = new List<BlendshapeMarkdownColorRule>();
            }
        }

        private void OnValidate()
        {
            if (headingRules == null)
            {
                headingRules = new List<BlendshapeMarkdownHeadingRule>();
            }

            if (colorRules == null)
            {
                colorRules = new List<BlendshapeMarkdownColorRule>();
            }

            if (string.IsNullOrWhiteSpace(ungroupedSectionTitle))
            {
                ungroupedSectionTitle = "Ungrouped";
            }

            if (string.IsNullOrWhiteSpace(topLevelAutoGroupTitle))
            {
                topLevelAutoGroupTitle = "Visemes";
            }
        }

        private void Awake()
        {
#if !UNITY_EDITOR
            Destroy(this);
#endif
        }
    }
}
