using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace YUCP.Components.Editor
{
    internal static class BlendshapeMarkdownParser
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

        public static BlendshapeMarkdownDocument Parse(SkinnedMeshRenderer renderer, BlendshapeMarkdownData config)
        {
            var document = new BlendshapeMarkdownDocument();

            if (renderer == null || renderer.sharedMesh == null)
            {
                return document;
            }

            Mesh mesh = renderer.sharedMesh;
            document.TotalBlendshapeCount = mesh.blendShapeCount;

            var sectionStack = new Stack<BlendshapeMarkdownSection>();
            sectionStack.Push(document.Root);

            for (int blendshapeIndex = 0; blendshapeIndex < mesh.blendShapeCount; blendshapeIndex++)
            {
                string blendshapeName = mesh.GetBlendShapeName(blendshapeIndex) ?? string.Empty;

                if (TryMatchHeading(blendshapeName, config, out BlendshapeMarkdownHeadingMatch headingMatch))
                {
                    document.HeadingCount++;

                    string[] pathSegments = SplitPathSegments(headingMatch.Title, config != null && config.useSlashAsPathSeparator);
                    int baseDepth = Mathf.Max(1, headingMatch.Depth);

                    for (int segmentIndex = 0; segmentIndex < pathSegments.Length; segmentIndex++)
                    {
                        string segment = pathSegments[segmentIndex];
                        if (string.IsNullOrWhiteSpace(segment))
                        {
                            continue;
                        }

                        int sectionDepth = baseDepth + segmentIndex;

                        while (sectionStack.Count > 0 && sectionStack.Peek().Depth >= sectionDepth)
                        {
                            sectionStack.Pop();
                        }

                        if (sectionStack.Count == 0)
                        {
                            sectionStack.Push(document.Root);
                        }

                        BlendshapeMarkdownSection parent = sectionStack.Peek();
                        string normalizedPath = string.IsNullOrEmpty(parent.FullPath)
                            ? segment
                            : $"{parent.FullPath}/{segment}";

                        var section = new BlendshapeMarkdownSection
                        {
                            Key = $"{normalizedPath}@{blendshapeIndex}:{segmentIndex}",
                            Title = segment,
                            FullPath = normalizedPath,
                            Depth = sectionDepth,
                            SourceIndex = blendshapeIndex
                        };

                        parent.Children.Add(section);
                        sectionStack.Push(section);
                    }

                    continue;
                }

                if (sectionStack.Count == 0)
                {
                    sectionStack.Push(document.Root);
                }

                sectionStack.Peek().Children.Add(new BlendshapeMarkdownBlendshapeItem
                {
                    Index = blendshapeIndex,
                    Name = blendshapeName,
                    Depth = sectionStack.Peek().Depth + 1,
                    SourceIndex = blendshapeIndex
                });
            }

            return document;
        }

        public static bool TryMatchHeading(string input, BlendshapeMarkdownData config, out BlendshapeMarkdownHeadingMatch match)
        {
            match = default;

            if (config == null || config.headingRules == null || config.headingRules.Count == 0)
            {
                return false;
            }

            for (int ruleIndex = 0; ruleIndex < config.headingRules.Count; ruleIndex++)
            {
                BlendshapeMarkdownHeadingRule rule = config.headingRules[ruleIndex];

                if (rule == null || !rule.enabled)
                {
                    continue;
                }

                switch (rule.mode)
                {
                    case BlendshapeMarkdownHeadingRuleMode.PrefixToken:
                        if (TryMatchPrefixRule(input, rule, out match))
                        {
                            return true;
                        }

                        break;

                    case BlendshapeMarkdownHeadingRuleMode.WrappedToken:
                        if (TryMatchWrappedRule(input, rule, out match))
                        {
                            return true;
                        }

                        break;

                    case BlendshapeMarkdownHeadingRuleMode.RawRegex:
                        if (TryMatchRegexRule(input, rule, out match))
                        {
                            return true;
                        }

                        break;
                }
            }

            return false;
        }

        private static bool TryMatchPrefixRule(string input, BlendshapeMarkdownHeadingRule rule, out BlendshapeMarkdownHeadingMatch match)
        {
            match = default;

            string token = rule.repeatToken ?? string.Empty;
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            string trimmed = (input ?? string.Empty).Trim();
            int depth = CountRepeatedTokenFromStart(trimmed, token, rule.ignoreCase);
            if (depth <= 0)
            {
                return false;
            }

            int offset = depth * token.Length;
            if (offset > trimmed.Length)
            {
                return false;
            }

            string remainder = trimmed.Substring(offset);
            if (rule.requireWhitespaceAfterPrefix)
            {
                if (remainder.Length == 0 || !char.IsWhiteSpace(remainder[0]))
                {
                    return false;
                }
            }

            string title = rule.trimTitleWhitespace ? remainder.Trim() : remainder;
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            match = new BlendshapeMarkdownHeadingMatch(rule.name, depth, title);
            return true;
        }

        private static bool TryMatchWrappedRule(string input, BlendshapeMarkdownHeadingRule rule, out BlendshapeMarkdownHeadingMatch match)
        {
            match = default;

            string token = rule.repeatToken ?? string.Empty;
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            string trimmed = (input ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            StringComparison comparison = rule.ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            string leftWrapper = rule.leftWrapper ?? string.Empty;
            string rightWrapper = rule.rightWrapper ?? string.Empty;

            if (!trimmed.StartsWith(leftWrapper, comparison) || !trimmed.EndsWith(rightWrapper, comparison))
            {
                return false;
            }

            string inner = trimmed.Substring(leftWrapper.Length, trimmed.Length - leftWrapper.Length - rightWrapper.Length);
            int leftDepth = CountRepeatedTokenFromStart(inner, token, rule.ignoreCase);
            int rightDepth = CountRepeatedTokenFromEnd(inner, token, rule.ignoreCase);

            if (leftDepth <= 0 || rightDepth <= 0 || leftDepth != rightDepth)
            {
                return false;
            }

            int markerLength = leftDepth * token.Length;
            if (markerLength * 2 > inner.Length)
            {
                return false;
            }

            string title = inner.Substring(markerLength, inner.Length - (markerLength * 2));
            title = rule.trimTitleWhitespace ? title.Trim() : title;

            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            match = new BlendshapeMarkdownHeadingMatch(rule.name, leftDepth, title);
            return true;
        }

        private static bool TryMatchRegexRule(string input, BlendshapeMarkdownHeadingRule rule, out BlendshapeMarkdownHeadingMatch match)
        {
            match = default;

            if (string.IsNullOrWhiteSpace(rule.rawRegex))
            {
                return false;
            }

            try
            {
                RegexOptions options = RegexOptions.Compiled;
                if (rule.ignoreCase)
                {
                    options |= RegexOptions.IgnoreCase;
                }

                Match regexMatch = Regex.Match(input ?? string.Empty, rule.rawRegex, options, RegexTimeout);
                if (!regexMatch.Success)
                {
                    return false;
                }

                if (rule.rawRegexDepthGroup >= regexMatch.Groups.Count || rule.rawRegexTitleGroup >= regexMatch.Groups.Count)
                {
                    return false;
                }

                string depthCapture = regexMatch.Groups[rule.rawRegexDepthGroup].Value;
                string title = regexMatch.Groups[rule.rawRegexTitleGroup].Value;

                if (rule.trimTitleWhitespace)
                {
                    title = title.Trim();
                }

                if (string.IsNullOrWhiteSpace(title))
                {
                    return false;
                }

                int depth;
                if (!int.TryParse(depthCapture, out depth))
                {
                    depth = depthCapture?.Length ?? 0;
                }

                if (depth <= 0)
                {
                    return false;
                }

                match = new BlendshapeMarkdownHeadingMatch(rule.name, depth, title);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        private static int CountRepeatedTokenFromStart(string input, string token, bool ignoreCase)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(token))
            {
                return 0;
            }

            int count = 0;
            int offset = 0;
            StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            while (offset + token.Length <= input.Length &&
                   string.Compare(input, offset, token, 0, token.Length, comparison) == 0)
            {
                count++;
                offset += token.Length;
            }

            return count;
        }

        private static int CountRepeatedTokenFromEnd(string input, string token, bool ignoreCase)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(token))
            {
                return 0;
            }

            int count = 0;
            int offset = input.Length - token.Length;
            StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            while (offset >= 0 &&
                   string.Compare(input, offset, token, 0, token.Length, comparison) == 0)
            {
                count++;
                offset -= token.Length;
            }

            return count;
        }

        private static string[] SplitPathSegments(string title, bool useSlashAsPathSeparator)
        {
            if (!useSlashAsPathSeparator)
            {
                return new[] { title.Trim() };
            }

            string[] parts = (title ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return new[] { title.Trim() };
            }

            for (int index = 0; index < parts.Length; index++)
            {
                parts[index] = parts[index].Trim();
            }

            return parts;
        }
    }
}
