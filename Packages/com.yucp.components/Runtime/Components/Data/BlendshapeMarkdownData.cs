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
        [Tooltip("Display name shown in the configuration UI.")]
        public string name = "Heading Rule";

        [Tooltip("Disable a rule without deleting it.")]
        public bool enabled = true;

        [Tooltip("How this rule detects markdown headings.")]
        public BlendshapeMarkdownHeadingRuleMode mode = BlendshapeMarkdownHeadingRuleMode.PrefixToken;

        [Tooltip("The repeated token that determines heading depth.\n\nExamples:\n• # for markdown\n• = for =Title=\n• - for |-Title-|")]
        public string repeatToken = "#";

        [Tooltip("Text that must appear before the repeated token for wrapped headings.\n\nExamples:\n• | for |-Title-|\n• || for ||---Title---||")]
        public string leftWrapper = "";

        [Tooltip("Text that must appear after the repeated token for wrapped headings.")]
        public string rightWrapper = "";

        [Tooltip("Require whitespace after the repeated token in Prefix mode.\n\nExample:\n• Enabled: # Title\n• Disabled: #Title")]
        public bool requireWhitespaceAfterPrefix = true;

        [Tooltip("Ignore case when matching text-based wrappers or regex.")]
        public bool ignoreCase = false;

        [Tooltip("Regex used in Raw Regex mode.\n\nCapture either the marker text or a numeric depth in the depth group, and the clean heading text in the title group.")]
        public string rawRegex = "";

        [Tooltip("Regex group that provides heading depth. If the capture is numeric, that number is used. Otherwise its length is used.")]
        [Min(1)]
        public int rawRegexDepthGroup = 1;

        [Tooltip("Regex group that provides the heading title.")]
        [Min(1)]
        public int rawRegexTitleGroup = 2;

        [Tooltip("Trim whitespace around the final heading title.")]
        public bool trimTitleWhitespace = true;
    }

    [Serializable]
    public class BlendshapeMarkdownColorRule
    {
        [Tooltip("Display name shown in the configuration UI.")]
        public string name = "Color Rule";

        [Tooltip("Disable a rule without deleting it.")]
        public bool enabled = true;

        [Tooltip("Which part of the parsed section to test.")]
        public BlendshapeMarkdownColorMatchTarget target = BlendshapeMarkdownColorMatchTarget.SectionTitle;

        [Tooltip("How the section text should be matched.")]
        public BlendshapeMarkdownColorMatchMode matchMode = BlendshapeMarkdownColorMatchMode.Contains;

        [Tooltip("The text or regex pattern to match.")]
        public string matchText = "";

        [Tooltip("Ignore case when matching.")]
        public bool ignoreCase = true;

        [Tooltip("Use a Pretty Hierarchy-inspired preset instead of custom colors.")]
        public bool usePresetColors = true;

        [Tooltip("Preset palette to apply when this rule matches.")]
        public PrettyHierarchyPreset preset = PrettyHierarchyPreset.Ocean;

        [Tooltip("Custom text color when Use Preset Colors is disabled.")]
        public Color textColor = Color.white;

        [Tooltip("Custom background color when Use Preset Colors is disabled.")]
        public Color backgroundColor = new Color(0.15f, 0.35f, 0.5f, 1f);
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("YUCP/Blendshape Markdown")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [SupportBanner]
    public class BlendshapeMarkdownData : MonoBehaviour, IEditorOnly
    {
        [Header("Target Renderer")]
        [Tooltip("Optional explicit renderer to organize. Leave empty to use the SkinnedMeshRenderer on this GameObject.")]
        public SkinnedMeshRenderer targetRenderer;

        [Header("Native Inspector Integration")]
        [Tooltip("When enabled, this component replaces the flat BlendShapes list in Unity's native SkinnedMeshRenderer inspector with the markdown-organized view.")]
        public bool enableNativeInspectorIntegration = true;

        [Tooltip("Replace Unity's default flat BlendShapes list. Disable this to keep the native list without the markdown grouping.")]
        public bool replaceDefaultBlendshapeList = true;

        [Tooltip("Show the search field above the organized blendshape tree.")]
        public bool showSearchBar = true;

        [Tooltip("Show blendshape counts on section headers.")]
        public bool showBlendshapeCounts = true;

        [Header("Heading Parsing")]
        [Tooltip("Quick preset for common heading styles. Use Apply Preset in the inspector to rebuild the rule list.")]
        public BlendshapeMarkdownPreset preset = BlendshapeMarkdownPreset.MixedConvenience;

        [Tooltip("Treat forward slashes in a heading title as nested paths.\n\nExample:\n==Body/Head== becomes Body > Head.")]
        public bool useSlashAsPathSeparator = true;

        [Tooltip("Group direct blendshapes under an Ungrouped foldout when they appear between markdown headings.")]
        public bool showUngroupedBlendshapes = true;

        [Tooltip("Label used for direct blendshapes that are not under a named subsection.")]
        public string ungroupedSectionTitle = "Ungrouped";

        [Tooltip("If a top-level blendshape starts with this text and is not already inside a markdown section, it is moved into the auto-group below.\n\nLeave empty to disable this behavior.")]
        public string topLevelAutoGroupPrefix = "VRC";

        [Tooltip("Label used for top-level blendshapes matched by the auto-group prefix.")]
        public string topLevelAutoGroupTitle = "Visemes";

        [Tooltip("Section matching rules. The first matching rule wins.")]
        public List<BlendshapeMarkdownHeadingRule> headingRules = new List<BlendshapeMarkdownHeadingRule>();

        [Header("Default Expansion")]
        [Tooltip("Default state for top-level markdown sections before any SessionState is stored.")]
        public bool expandTopLevelByDefault = true;

        [Tooltip("Default state for nested markdown sections before any SessionState is stored.")]
        public bool expandNestedByDefault = false;

        [Header("Section Colors")]
        [Tooltip("Optional color rules for section headers, similar in spirit to Pretty Hierarchy.")]
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
