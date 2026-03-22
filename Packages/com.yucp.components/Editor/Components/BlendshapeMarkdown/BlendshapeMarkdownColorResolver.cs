using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor
{
    internal static class BlendshapeMarkdownColorResolver
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
        private static readonly BlendshapeMarkdownSectionStyle DefaultDarkStyle =
            new BlendshapeMarkdownSectionStyle(false, new Color(0.94f, 0.94f, 0.96f, 1f), new Color(0.18f, 0.19f, 0.22f, 0.95f));

        private static readonly BlendshapeMarkdownSectionStyle DefaultLightStyle =
            new BlendshapeMarkdownSectionStyle(false, new Color(0.12f, 0.12f, 0.14f, 1f), new Color(0.82f, 0.84f, 0.88f, 1f));

        public static BlendshapeMarkdownSectionStyle Resolve(BlendshapeMarkdownData config, BlendshapeMarkdownSection section)
        {
            if (config != null && config.colorRules != null)
            {
                for (int index = 0; index < config.colorRules.Count; index++)
                {
                    BlendshapeMarkdownColorRule rule = config.colorRules[index];
                    if (rule == null || !rule.enabled || string.IsNullOrWhiteSpace(rule.matchText))
                    {
                        continue;
                    }

                    string targetText = rule.target == BlendshapeMarkdownColorMatchTarget.FullPath
                        ? section.FullPath
                        : section.Title;

                    if (!IsMatch(targetText, rule))
                    {
                        continue;
                    }

                    if (rule.usePresetColors)
                    {
                        GetPresetColors(rule.preset, out Color textColor, out Color backgroundColor);
                        return new BlendshapeMarkdownSectionStyle(true, textColor, backgroundColor);
                    }

                    return new BlendshapeMarkdownSectionStyle(true, rule.textColor, rule.backgroundColor);
                }
            }

            return EditorGUIUtility.isProSkin ? DefaultDarkStyle : DefaultLightStyle;
        }

        private static bool IsMatch(string targetText, BlendshapeMarkdownColorRule rule)
        {
            targetText ??= string.Empty;
            string needle = rule.matchText ?? string.Empty;

            StringComparison comparison = rule.ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            switch (rule.matchMode)
            {
                case BlendshapeMarkdownColorMatchMode.Contains:
                    return targetText.IndexOf(needle, comparison) >= 0;

                case BlendshapeMarkdownColorMatchMode.StartsWith:
                    return targetText.StartsWith(needle, comparison);

                case BlendshapeMarkdownColorMatchMode.EndsWith:
                    return targetText.EndsWith(needle, comparison);

                case BlendshapeMarkdownColorMatchMode.Exact:
                    return string.Equals(targetText, needle, comparison);

                case BlendshapeMarkdownColorMatchMode.Regex:
                    try
                    {
                        RegexOptions options = RegexOptions.Compiled;
                        if (rule.ignoreCase)
                        {
                            options |= RegexOptions.IgnoreCase;
                        }

                        return Regex.IsMatch(targetText, needle, options, RegexTimeout);
                    }
                    catch (ArgumentException)
                    {
                        return false;
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        return false;
                    }

                default:
                    return false;
            }
        }

        public static void GetPresetColors(PrettyHierarchyPreset preset, out Color textColor, out Color backgroundColor)
        {
            backgroundColor = preset switch
            {
                PrettyHierarchyPreset.Red => new Color(0.70f, 0.15f, 0.15f, 0.95f),
                PrettyHierarchyPreset.Orange => new Color(0.80f, 0.40f, 0.10f, 0.95f),
                PrettyHierarchyPreset.Yellow => new Color(0.72f, 0.66f, 0.12f, 0.95f),
                PrettyHierarchyPreset.Green => new Color(0.10f, 0.50f, 0.20f, 0.95f),
                PrettyHierarchyPreset.Blue => new Color(0.10f, 0.35f, 0.70f, 0.95f),
                PrettyHierarchyPreset.Purple => new Color(0.40f, 0.20f, 0.70f, 0.95f),
                PrettyHierarchyPreset.Pink => new Color(0.70f, 0.30f, 0.50f, 0.95f),
                PrettyHierarchyPreset.Gray => new Color(0.35f, 0.35f, 0.38f, 0.95f),
                PrettyHierarchyPreset.Black => new Color(0.12f, 0.12f, 0.14f, 0.95f),
                PrettyHierarchyPreset.White => new Color(0.90f, 0.90f, 0.92f, 0.95f),
                PrettyHierarchyPreset.Midnight => new Color(0.08f, 0.10f, 0.18f, 0.95f),
                PrettyHierarchyPreset.Sunset => new Color(0.60f, 0.25f, 0.35f, 0.95f),
                PrettyHierarchyPreset.Ocean => new Color(0.15f, 0.35f, 0.50f, 0.95f),
                PrettyHierarchyPreset.Forest => new Color(0.15f, 0.35f, 0.20f, 0.95f),
                _ => new Color(0.15f, 0.35f, 0.50f, 0.95f)
            };

            float luminance = (backgroundColor.r * 0.299f) + (backgroundColor.g * 0.587f) + (backgroundColor.b * 0.114f);
            textColor = luminance > 0.60f ? new Color(0.10f, 0.10f, 0.12f, 1f) : new Color(0.96f, 0.96f, 0.98f, 1f);
        }
    }
}
