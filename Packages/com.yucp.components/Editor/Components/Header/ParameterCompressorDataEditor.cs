using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(ParameterCompressorData))]
    public sealed class ParameterCompressorDataEditor : UnityEditor.Editor
    {
        private const int Budget = ParameterCompressionContract.VrchatParameterBudget;

        public override VisualElement CreateInspectorGUI()
        {
            var data = (ParameterCompressorData)target;
            var ui = YUCPInspector.Create(this, "Parameter Compressor");
            ui.Status(() => Problems(data));

            var compression = ui.Section("Compression");
            ui.Field(compression, "automaticSelection");
            ui.Field(compression, "reserveSyncedBits", "Reserved Bits");

            var bits = ui.Section("Synced bits");
            ui.InfoRow(bits, "Current Use", () =>
            {
                var current = CurrentSyncedBits(data);
                return current < 0 ? "Unknown outside an avatar" : $"{current} of {Budget} bits";
            }).tooltip = "Synced parameters on the avatar now. Other tools can add more during the build.";
            ui.InfoRow(bits, "Target", () => $"{TargetBits(data)} bits or fewer");
            var last = ui.Group(bits, "Last build");
            ui.InfoRow(last, "Synced Bits", () => $"{data.GetBuildSummary().beforeBits} → {data.GetBuildSummary().afterBits}");
            ui.InfoRow(last, "Compressed", () => Parameters(data.GetBuildSummary().compressedParameters));
            ui.InfoRow(last, "Protected", () => Parameters(data.GetBuildSummary().protectedParameters));
            ui.InfoRow(last, "Carrier Bits", () => data.GetBuildSummary().carrierBits.ToString());
            ui.ShowWhen(ui.InfoRow(last, "Full Refresh", () => $"Up to {data.GetBuildSummary().nominalFullRefreshSeconds:0.0} s"),
                () => data.GetBuildSummary().nominalFullRefreshSeconds > 0f);
            ui.ShowWhen(ui.InfoRow(last, "Transport", () => data.GetBuildSummary().transportName),
                () => !string.IsNullOrWhiteSpace(data.GetBuildSummary().transportName));
            ui.ShowWhen(last, () => data.GetBuildSummary().hasResult);
            ui.Caption(bits, () => Note(data));
            ui.Messages(bits, () =>
            {
                var build = data.GetBuildSummary();
                return new[] { YUCPInspector.Message.Warning(build.hasResult ? null : build.message) };
            });

            BuildRules(ui, data);

            ui.Field(ui.Advanced(), "parameterPrefix");
            return ui.Root;
        }

        /// <summary>Rules: the list, the selected rule's settings under it, and problems with the rules.</summary>
        private static void BuildRules(YUCPInspector ui, ParameterCompressorData data)
        {
            var section = ui.Section("Parameter rules");
            ui.Field(section, "profile");
            var selected = -1;
            var items = new List<ParameterCompressionRule>();
            var list = ui.List(section, items, () =>
            {
                var row = new VisualElement();
                row.AddToClassList("yucp-list-item");
                var name = new Label();
                name.AddToClassList("yucp-list-item-name");
                row.Add(name);
                var badge = new Label();
                badge.AddToClassList("yucp-badge");
                row.Add(badge);
                return row;
            }, (row, i) =>
            {
                var rule = items[i];
                row.Q<Label>(className: "yucp-list-item-name").text =
                    string.IsNullOrWhiteSpace(rule.parameterName) ? "New Rule" : rule.parameterName;
                var badge = row.Q<Label>(className: "yucp-badge");
                badge.text = Badge(rule.selection);
                badge.EnableInClassList("yucp-badge-default", rule.selection == ParameterCompressionRuleSelection.Automatic ||
                                                              rule.selection == ParameterCompressionRuleSelection.KeepDirect);
                badge.EnableInClassList("yucp-badge-primary", rule.selection == ParameterCompressionRuleSelection.Include);
                badge.EnableInClassList("yucp-badge-warning", rule.selection == ParameterCompressionRuleSelection.IncludeUnverified);
            },
                "No rules yet. Add one to include, protect or tune a parameter.",
                () =>
                {
                    selected = AddRule(ui);
                    ui.Refresh();
                },
                i =>
                {
                    selected = RemoveRule(ui, i, selected);
                    ui.Refresh();
                },
                "Add Rule", "Remove Rule");
            list.View.tooltip = "Rules apply after the profile's rules.";
            list.View.selectionChanged += _ =>
            {
                selected = list.View.selectedIndex;
                ui.Refresh();
            };
            list.View.itemIndexChanged += (from, to) =>
            {
                ui.Object.Update();
                ui.Object.FindProperty("rules").MoveArrayElement(from, to);
                ui.Object.ApplyModifiedProperties();
                selected = to;
                ui.Refresh();
            };

            // The selected rule's settings. Rebuilt only when the selection or the rule count changes.
            var detail = ui.Group(section);
            var top = new VisualElement();
            detail.Add(top);
            var custom = ui.Indent(detail);
            var bottom = new VisualElement();
            detail.Add(bottom);
            ParameterCompressionRule Selected() =>
                data.rules != null && selected >= 0 && selected < data.rules.Count ? data.rules[selected] : null;
            ui.ShowWhen(detail, () => Selected() != null);
            ui.ShowWhen(custom, () => Selected()?.range == ParameterCompressionRangeMode.Custom);

            string shownItems = null;
            string shownDetail = null;
            ui.OnRefresh(() =>
            {
                var rules = data.rules ?? new List<ParameterCompressionRule>();
                var key = string.Join("|", rules.Select(r => r == null ? "" : r.parameterName + (int)r.selection));
                if (key != shownItems)
                {
                    shownItems = key;
                    items.Clear();
                    items.AddRange(rules);
                    list.Refresh();
                }
                if (selected >= rules.Count) selected = rules.Count - 1;
                list.Select(selected);

                var detailKey = Selected() == null ? "" : selected + "/" + rules.Count;
                if (detailKey == shownDetail) return;
                shownDetail = detailKey;
                top.Clear();
                custom.Clear();
                bottom.Clear();
                if (Selected() == null) return;
                var path = $"rules.Array.data[{selected}].";
                ui.Field(top, path + "parameterName", "Parameter");
                ui.Field(top, path + "selection", "Include");
                ui.Field(top, path + "priority", "Update Priority");
                ui.Field(top, path + "precision", "Precision");
                ui.Field(top, path + "range", "Range");
                ui.Field(custom, path + "minimum", "Minimum");
                ui.Field(custom, path + "maximum", "Maximum");
                ui.Field(bottom, path + "group", "Group");
            });

            ui.Messages(section, () => RuleProblems(data));
        }

        private static int AddRule(YUCPInspector ui)
        {
            ui.Object.Update();
            var rules = ui.Object.FindProperty("rules");
            var index = rules.arraySize;
            rules.arraySize++;
            var rule = rules.GetArrayElementAtIndex(index);
            rule.FindPropertyRelative("parameterName").stringValue = string.Empty;
            rule.FindPropertyRelative("selection").enumValueIndex = (int)ParameterCompressionRuleSelection.Automatic;
            rule.FindPropertyRelative("priority").enumValueIndex = (int)ParameterCompressionPriority.Normal;
            rule.FindPropertyRelative("precision").enumValueIndex = (int)ParameterCompressionPrecision.Automatic;
            rule.FindPropertyRelative("range").enumValueIndex = (int)ParameterCompressionRangeMode.Automatic;
            rule.FindPropertyRelative("minimum").floatValue = 0f;
            rule.FindPropertyRelative("maximum").floatValue = 1f;
            rule.FindPropertyRelative("group").stringValue = ParameterCompressionContract.DefaultGroup;
            rule.FindPropertyRelative("stableId").stringValue = ParameterCompressionContract.NewStableId();
            ui.Object.ApplyModifiedProperties();
            return index;
        }

        private static int RemoveRule(YUCPInspector ui, int index, int selected)
        {
            ui.Object.Update();
            var rules = ui.Object.FindProperty("rules");
            if (index < 0 || index >= rules.arraySize) return selected;
            rules.DeleteArrayElementAtIndex(index);
            ui.Object.ApplyModifiedProperties();
            return Math.Min(index, rules.arraySize - 1);
        }

        private static string Badge(ParameterCompressionRuleSelection selection) => selection switch
        {
            ParameterCompressionRuleSelection.Include => "Included",
            ParameterCompressionRuleSelection.IncludeUnverified => "Unverified",
            ParameterCompressionRuleSelection.KeepDirect => "Direct",
            _ => "Automatic"
        };

        private static string Parameters(int count) => count == 1 ? "1 parameter" : count + " parameters";

        private static int TargetBits(ParameterCompressorData data) => Math.Max(0, Budget - data.reserveSyncedBits);

        /// <summary>Bits the avatar's synced parameters use now, or -1 outside an avatar.</summary>
        private static int CurrentSyncedBits(ParameterCompressorData data)
        {
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (descriptor == null) return -1;
            var parameters = descriptor.expressionParameters;
            if (parameters == null || parameters.parameters == null) return 0;
            return parameters.parameters
                .Where(parameter => parameter != null && parameter.networkSynced)
                .Sum(parameter => parameter.valueType == VRCExpressionParameters.ValueType.Bool ? 1 : 8);
        }

        private static string Note(ParameterCompressorData data)
        {
            var build = data.GetBuildSummary();
            if (build.hasResult) return build.message;
            var current = CurrentSyncedBits(data);
            if (current < 0) return null;
            return current <= TargetBits(data)
                ? "The avatar already fits. Safe parameters stay direct."
                : "The build compresses parameters on the finished avatar until it fits the target.";
        }

        private static IEnumerable<YUCPInspector.Message> Problems(ParameterCompressorData data)
        {
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (descriptor == null)
            {
                yield return YUCPInspector.Message.Error("Put this inside an avatar (under a VRC Avatar Descriptor).");
                yield break;
            }
            if (descriptor.GetComponentsInChildren<ParameterCompressorData>(true).Length > 1)
                yield return YUCPInspector.Message.Error("Use one Parameter Compressor per avatar. It already sees every parameter on the avatar.");
        }

        private static IEnumerable<YUCPInspector.Message> RuleProblems(ParameterCompressorData data)
        {
            var rules = (data.rules ?? new List<ParameterCompressionRule>()).Where(rule => rule != null).ToList();
            var unnamed = rules.Count(rule => string.IsNullOrWhiteSpace(rule.parameterName));
            if (unnamed > 0)
                yield return YUCPInspector.Message.Warning(unnamed == 1
                    ? "A rule has no parameter name. Enter one or remove the rule."
                    : $"{unnamed} rules have no parameter name. Enter them or remove the rules.");

            var duplicates = rules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.parameterName))
                .GroupBy(rule => ParameterCompressionContract.NormalizeParameterName(rule.parameterName), StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicates.Length > 0)
                yield return YUCPInspector.Message.Error("Combine the duplicate rules for " + string.Join(", ", duplicates) + ".");

            if (!data.automaticSelection && !data.EnumerateRules().Any(rule =>
                    rule.selection == ParameterCompressionRuleSelection.Include ||
                    rule.selection == ParameterCompressionRuleSelection.IncludeUnverified))
                yield return YUCPInspector.Message.Info("Automatic Selection is off, so nothing is compressed until a rule includes a parameter.");
        }
    }
}
