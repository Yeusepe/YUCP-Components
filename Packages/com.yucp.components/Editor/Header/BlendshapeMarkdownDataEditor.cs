using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;
using Toolkit = YUCP.UI.DesignSystem.Utilities.YUCPUIToolkitHelper;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(BlendshapeMarkdownData))]
    public class BlendshapeMarkdownDataEditor : UnityEditor.Editor
    {
        private static readonly string[] ModeNames = { "Prefix", "Wrapped", "Regex" };

        public override VisualElement CreateInspectorGUI()
        {
            var data = (BlendshapeMarkdownData)target;
            var ui = YUCPInspector.Create(this, "Blendshape Markdown");
            bool On(string path) => serializedObject.FindProperty(path).boolValue;

            var renderer = ui.Section("Renderer");
            ui.Field(renderer, "targetRenderer", "Renderer");
            ui.Messages(renderer, () => Problems(data));
            ui.Field(renderer, "enableNativeInspectorIntegration", "Group in Inspector");
            var view = ui.ShowWhen(ui.Indent(renderer), () => On("enableNativeInspectorIntegration"));
            ui.Field(view, "showSearchBar", "Show Search Bar");
            ui.Field(view, "showBlendshapeCounts", "Show Counts");
            ui.Field(view, "expandTopLevelByDefault", "Expand Top Sections");
            ui.Field(view, "expandNestedByDefault", "Expand Nested Sections");

            var headings = ui.Section("Heading rules");
            RuleList<BlendshapeMarkdownHeadingRule>(ui, headings, "headingRules", "Heading Rule",
                "No headings are found. Add a rule, or add a preset's rules below.",
                rule => ModeNames[Mathf.Clamp(rule.FindPropertyRelative("mode").enumValueIndex, 0, ModeNames.Length - 1)],
                detail =>
                {
                    bool Mode(BlendshapeMarkdownHeadingRuleMode mode) => detail.Get("mode").enumValueIndex == (int)mode;
                    detail.Field("name", "Name");
                    detail.Field("enabled", "Enabled");
                    detail.Segmented("mode", "Mode", ModeNames);
                    detail.Field("repeatToken", "Token", () => !Mode(BlendshapeMarkdownHeadingRuleMode.RawRegex));
                    detail.Field("leftWrapper", "Left Wrapper", () => Mode(BlendshapeMarkdownHeadingRuleMode.WrappedToken));
                    detail.Field("rightWrapper", "Right Wrapper", () => Mode(BlendshapeMarkdownHeadingRuleMode.WrappedToken));
                    detail.Field("requireWhitespaceAfterPrefix", "Space After Token", () => Mode(BlendshapeMarkdownHeadingRuleMode.PrefixToken));
                    detail.Field("rawRegex", "Regex", () => Mode(BlendshapeMarkdownHeadingRuleMode.RawRegex));
                    detail.Field("rawRegexDepthGroup", "Depth Group", () => Mode(BlendshapeMarkdownHeadingRuleMode.RawRegex));
                    detail.Field("rawRegexTitleGroup", "Title Group", () => Mode(BlendshapeMarkdownHeadingRuleMode.RawRegex));
                    detail.Field("ignoreCase", "Ignore Case");
                    detail.Field("trimTitleWhitespace", "Trim Title");
                });
            var presets = ui.Group(headings, "Presets");
            ui.Field(presets, "preset");
            bool HasPreset() => serializedObject.FindProperty("preset").enumValueIndex != (int)BlendshapeMarkdownPreset.Custom;
            var buttons = ui.ButtonRow(presets);
            ui.EnableWhen(ui.Button(buttons, "Add Preset Rules", () => AddPreset(ui, data, false),
                Toolkit.ButtonVariant.Secondary, "Adds the preset's rules after yours."), HasPreset);
            ui.EnableWhen(ui.Button(buttons, "Replace with Preset", () => AddPreset(ui, data, true),
                Toolkit.ButtonVariant.Danger, "Replaces your heading rules with the preset's rules."), HasPreset);

            var colors = ui.Section("Color rules");
            RuleList<BlendshapeMarkdownColorRule>(ui, colors, "colorRules", "Color Rule",
                "Sections use the default colors. Add a rule to color matching sections.",
                rule => rule.FindPropertyRelative("usePresetColors").boolValue ? DisplayName(rule.FindPropertyRelative("preset")) : "Custom",
                detail =>
                {
                    bool Preset() => detail.Get("usePresetColors").boolValue;
                    detail.Field("name", "Name");
                    detail.Field("enabled", "Enabled");
                    detail.Segmented("target", "Match In", "Title", "Full Path");
                    detail.Field("matchMode", "Match");
                    detail.Field("matchText", "Text");
                    detail.Field("ignoreCase", "Ignore Case");
                    // usePresetColors: false is Custom, true is Preset.
                    detail.Segmented("usePresetColors", "Colors", "Custom", "Preset");
                    detail.Field("preset", "Preset", Preset);
                    detail.Field("textColor", "Text Color", () => !Preset());
                    detail.Field("backgroundColor", "Background Color", () => !Preset());
                });

            var grouping = ui.Section("Grouping");
            ui.Field(grouping, "useSlashAsPathSeparator", "Split Paths at /");
            ui.Field(grouping, "showUngroupedBlendshapes", "Ungrouped Section");
            ui.Field(ui.ShowWhen(ui.Indent(grouping), () => On("showUngroupedBlendshapes")), "ungroupedSectionTitle", "Title");
            ui.Field(grouping, "topLevelAutoGroupPrefix", "Auto-Group Prefix");
            ui.Field(ui.ShowWhen(ui.Indent(grouping),
                () => !string.IsNullOrWhiteSpace(serializedObject.FindProperty("topLevelAutoGroupPrefix").stringValue)), "topLevelAutoGroupTitle", "Title");

            // Parsed once per refresh; the preview rows below read it.
            BlendshapeMarkdownDocument document = null;
            void Parse()
            {
                var smr = data.GetTargetRenderer();
                document = smr != null && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0
                    ? BlendshapeMarkdownParser.Parse(smr, data) : null;
            }
            Parse();
            ui.OnRefresh(Parse);
            var preview = ui.Section("Preview");
            ui.ShowWhen(preview.parent, () => document != null);
            ui.InfoRow(preview, "Blendshapes", () => document?.TotalBlendshapeCount.ToString() ?? "");
            ui.InfoRow(preview, "Headings Found", () => document?.HeadingCount.ToString() ?? "");
            ui.ShowWhen(ui.InfoRow(preview, "Sections", () => Sections(document)), () => document != null && document.HeadingCount > 0);
            ui.Messages(preview, () => new[]
            {
                document != null && document.HeadingCount == 0
                    ? YUCPInspector.Message.Warning("No blendshape names match a heading rule. Add or change a rule to match your section markers.")
                    : default
            });

            ui.Field(ui.Advanced(), "replaceDefaultBlendshapeList", "Replace Default List");
            ui.Refresh();
            return ui.Root;
        }

        private static IEnumerable<YUCPInspector.Message> Problems(BlendshapeMarkdownData data)
        {
            var renderer = data.GetTargetRenderer();
            if (renderer == null)
            {
                yield return YUCPInspector.Message.Error("Add this to an object with a Skinned Mesh Renderer, or assign Renderer.");
                yield break;
            }
            if (renderer.sharedMesh == null)
            {
                yield return YUCPInspector.Message.Error("Assign a mesh to " + renderer.name + ".");
                yield break;
            }
            if (renderer.sharedMesh.blendShapeCount == 0)
                yield return YUCPInspector.Message.Warning("This mesh has no blendshapes. Choose a renderer whose mesh has some.");
            var count = UnityEngine.Object.FindObjectsByType<BlendshapeMarkdownData>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Count(c => c != null && c.TargetsRenderer(renderer));
            if (count > 1)
                yield return YUCPInspector.Message.Warning(count + " Blendshape Markdown components target this renderer. Only the first with Group in Inspector on is used; remove the others.");
        }

        /// <summary>The first sections found, one path per line.</summary>
        private static string Sections(BlendshapeMarkdownDocument document)
        {
            if (document == null) return "";
            var paths = new List<string>();
            void Walk(BlendshapeMarkdownSection section)
            {
                foreach (var child in section.Children.OfType<BlendshapeMarkdownSection>())
                {
                    paths.Add(child.FullPath);
                    Walk(child);
                }
            }
            Walk(document.Root);
            const int shown = 8;
            return string.Join("\n", paths.Take(shown)) + (paths.Count > shown ? $"\n{paths.Count - shown} more" : "");
        }

        private static string DisplayName(SerializedProperty enumProperty)
        {
            var names = enumProperty.enumDisplayNames;
            var index = enumProperty.enumValueIndex;
            return index >= 0 && index < names.Length ? names[index] : "";
        }

        /// <summary>
        /// Adds the selected preset's rules after the current ones, or replaces them. Undoable;
        /// replacing existing rules asks first (HIG Alerts: confirm destructive actions).
        /// </summary>
        private static void AddPreset(YUCPInspector ui, BlendshapeMarkdownData data, bool replace)
        {
            ui.Object.ApplyModifiedProperties();
            var rules = data.headingRules ?? new List<BlendshapeMarkdownHeadingRule>();
            // Undoable, so no confirmation (DESIGN.md §5).
            var kept = replace ? new List<BlendshapeMarkdownHeadingRule>() : new List<BlendshapeMarkdownHeadingRule>(rules);
            Undo.RecordObject(data, replace ? "Replace Heading Rules" : "Add Preset Rules");
            // ponytail: the component owns the preset rules; ApplyPresetRules clears the list, so kept rules go back in front.
            data.ApplyPresetRules(data.preset);
            data.headingRules.InsertRange(0, kept);
            PrefabUtility.RecordPrefabInstancePropertyModifications(data);
            ui.Object.Update();
            ui.Refresh();
        }

        /// <summary>
        /// A rule list (HIG Lists and tables): + adds a rule, drag reorders (the first match wins),
        /// and the selected rule is edited below the list.
        /// </summary>
        private static void RuleList<T>(YUCPInspector ui, VisualElement parent, string path, string noun, string emptyText,
            Func<SerializedProperty, string> badge, Action<RuleDetail> buildDetail) where T : new()
        {
            var rows = new List<(string name, string badge)>();
            var selected = -1;
            SerializedProperty Rules() => ui.Object.FindProperty(path);
            void Change(Action<SerializedProperty> change, int select)
            {
                ui.Object.Update();
                change(Rules());
                ui.Object.ApplyModifiedProperties();
                selected = select;
                ui.Refresh();
            }

            var list = ui.List(parent, rows, () =>
            {
                var row = new VisualElement();
                row.AddToClassList("yucp-list-item");
                var name = new Label();
                name.AddToClassList("yucp-list-item-name");
                row.Add(name);
                var tag = new Label();
                tag.AddToClassList("yucp-badge");
                tag.AddToClassList("yucp-badge-default");
                row.Add(tag);
                return row;
            }, (row, i) =>
            {
                row.Q<Label>(className: "yucp-list-item-name").text = rows[i].name;
                row.Q<Label>(className: "yucp-badge").text = rows[i].badge;
            }, emptyText,
                () => Change(rules =>
                {
                    rules.arraySize++;
                    rules.GetArrayElementAtIndex(rules.arraySize - 1).boxedValue = new T();
                }, Rules().arraySize),
                i => Change(rules => rules.DeleteArrayElementAtIndex(i), Mathf.Min(i, Rules().arraySize - 2)),
                "Add " + noun, "Remove " + noun);
            list.View.tooltip = "Drag to reorder. The first rule that matches wins.";
            list.View.selectionChanged += _ =>
            {
                selected = list.View.selectedIndex;
                ui.Refresh();
            };
            list.View.itemIndexChanged += (from, to) => Change(rules => rules.MoveArrayElement(from, to), to);

            var detail = new RuleDetail(ui, ui.Group(parent));
            var shown = (index: -2, count: -1);
            ui.OnRefresh(() =>
            {
                var rules = Rules();
                var current = Enumerable.Range(0, rules.arraySize).Select(i =>
                {
                    var rule = rules.GetArrayElementAtIndex(i);
                    var name = rule.FindPropertyRelative("name").stringValue;
                    return (string.IsNullOrWhiteSpace(name) ? "Rule " + (i + 1) : name,
                        rule.FindPropertyRelative("enabled").boolValue ? badge(rule) : "Off");
                }).ToList();
                if (!current.SequenceEqual(rows))
                {
                    rows.Clear();
                    rows.AddRange(current);
                    list.Refresh();
                }
                selected = Mathf.Min(selected, rows.Count - 1);
                list.Select(selected);
                if (shown != (selected, rows.Count))
                {
                    shown = (selected, rows.Count);
                    detail.Clear(path + ".Array.data[" + selected + "]");
                    if (selected >= 0) buildDetail(detail);
                    else if (rows.Count > 0) ui.Caption(detail.Root, "Select a rule to edit it.");
                    var display = rows.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    if (detail.Root.style.display != display) detail.Root.style.display = display;
                }
                detail.Refresh();
            });
        }

        /// <summary>
        /// The selected rule's fields. Rebuilt when the selection changes, so it keeps its own
        /// refreshers instead of adding to the inspector's (which are never removed).
        /// </summary>
        private sealed class RuleDetail
        {
            private readonly YUCPInspector ui;
            private readonly List<Action> refreshers = new List<Action>();
            private string path;

            public VisualElement Root { get; }

            public RuleDetail(YUCPInspector ui, VisualElement root)
            {
                this.ui = ui;
                Root = root;
            }

            public SerializedProperty Get(string field) => ui.Object.FindProperty(path + "." + field);

            public void Clear(string rulePath)
            {
                path = rulePath;
                Root.Clear();
                refreshers.Clear();
            }

            public void Refresh()
            {
                foreach (var refresh in refreshers) refresh();
            }

            public void Field(string field, string label, Func<bool> visible = null) =>
                ShowWhen(ui.Field(Root, path + "." + field, label), visible);

            /// <summary>A segmented control over a bool or enum field of the rule.</summary>
            public void Segmented(string field, string label, params string[] names)
            {
                var segmented = new SegmentedField(label, names) { tooltip = Get(field).tooltip };
                segmented.RegisterValueChangedCallback(e =>
                {
                    ui.Object.Update();
                    var property = Get(field);
                    if (property.propertyType == SerializedPropertyType.Boolean) property.boolValue = e.newValue == 1;
                    else property.enumValueIndex = e.newValue;
                    ui.Object.ApplyModifiedProperties();
                    ui.Refresh();
                });
                refreshers.Add(() =>
                {
                    var property = Get(field);
                    segmented.SetValueWithoutNotify(property.propertyType == SerializedPropertyType.Boolean
                        ? (property.boolValue ? 1 : 0) : property.enumValueIndex);
                });
                Root.Add(segmented);
            }

            private void ShowWhen(VisualElement element, Func<bool> visible)
            {
                if (element == null || visible == null) return;
                refreshers.Add(() =>
                {
                    var display = visible() ? DisplayStyle.Flex : DisplayStyle.None;
                    if (element.style.display != display) element.style.display = display;
                });
            }
        }
    }
}
