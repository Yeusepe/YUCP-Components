using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using YUCP.Components.Editor.VisemePhrase;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(VisemePhraseTriggerData))]
    public sealed class VisemePhraseTriggerDataEditor : UnityEditor.Editor
    {
        /// <summary>What the inspector knows about one phrase. Worked out only when the phrases or their takes change.</summary>
        private sealed class PhraseInfo
        {
            internal VisemePhraseEnrollmentStatus status;
            internal VisemePhraseCompiledModel model;
        }

        private readonly Dictionary<string, PhraseInfo> info = new Dictionary<string, PhraseInfo>();
        private (string matcher, string bits, string total) cost;
        private string infoKey;
        private string pendingKey;
        private string selectedId;

        public override VisualElement CreateInspectorGUI()
        {
            var data = (VisemePhraseTriggerData)target;
            data.EnsureDefaults();
            var ui = YUCPInspector.Create(this, "Viseme Phrase Trigger");
            ui.Status(() => data.GetComponentInParent<VRCAvatarDescriptor>(true) == null
                ? new[] { YUCPInspector.Message.Error("Put this inside an avatar (under a VRC Avatar Descriptor).") }
                : null);

            BuildPhrases(ui, data);

            var advanced = ui.Advanced();
            ui.Field(advanced, "sourcePrefix", "Viseme Source Prefix");
            ui.Field(advanced, "parameterPrefix", "Output Prefix");
            ui.Field(advanced, "enrollmentProfile");
            BuildDiagnostics(ui, data, advanced);
            ui.Refresh();
            return ui.Root;
        }

        private VisemePhraseDefinition Selected(VisemePhraseTriggerData data)
        {
            var phrases = data.phrases ?? new List<VisemePhraseDefinition>();
            return phrases.FirstOrDefault(p => p != null && p.id == selectedId) ?? phrases.FirstOrDefault(p => p != null);
        }

        private PhraseInfo InfoOf(VisemePhraseDefinition phrase) =>
            phrase != null && info.TryGetValue(phrase.id, out var found) ? found : null;

        /// <summary>The phrase list, and the selected phrase's settings and takes under it.</summary>
        private void BuildPhrases(YUCPInspector ui, VisemePhraseTriggerData data)
        {
            var section = ui.Section("Phrases");
            var items = new List<VisemePhraseDefinition>();
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
                var phrase = items[i];
                row.Q<Label>(className: "yucp-list-item-name").text = Name(phrase, i);
                var status = InfoOf(phrase)?.status;
                var badge = row.Q<Label>(className: "yucp-badge");
                badge.text = status == null ? "" : status.IsReady ? "Ready" : $"{status.CompletedTakes} of {VisemePhraseEnrollmentStatus.RequiredTakeCount} Takes";
                badge.EnableInClassList("yucp-badge-success", status != null && status.IsReady);
                badge.EnableInClassList("yucp-badge-warning", status != null && !status.IsReady);
            },
                "Add a phrase, then record four takes of it.",
                () => AddPhrase(ui, data),
                i =>
                {
                    // Applying serialized changes recreates the phrase objects, so match by id.
                    var index = data.phrases.FindIndex(p => p != null && p.id == items[i].id);
                    if (index < 0) return;
                    ui.Object.Update();
                    ui.Object.FindProperty("phrases").DeleteArrayElementAtIndex(index);
                    ui.Object.ApplyModifiedProperties();
                    ui.Refresh();
                },
                "Add Phrase", "Remove Phrase");
            list.View.tooltip = "Each phrase matches the mouth shapes you recorded, not words or audio. Drag to reorder.";
            list.View.selectionChanged += _ =>
            {
                if (list.View.selectedItem is VisemePhraseDefinition phrase) selectedId = phrase.id;
                ui.Refresh();
            };
            list.View.itemIndexChanged += (from, to) =>
            {
                ui.Object.Update();
                ui.Object.FindProperty("phrases").MoveArrayElement(from, to);
                ui.Object.ApplyModifiedProperties();
                ui.Refresh();
            };

            // Runs before the refreshers below so they read current statuses.
            string shownItems = null;
            ui.OnRefresh(() =>
            {
                UpdateInfo(data);
                var phrases = (data.phrases ?? new List<VisemePhraseDefinition>()).Where(p => p != null).ToList();
                var key = infoKey + "|" + string.Join("|", phrases.Select(p => p.id + p.prompt));
                if (key != shownItems)
                {
                    shownItems = key;
                    items.Clear();
                    items.AddRange(phrases);
                    list.Refresh();
                }
                var selected = Selected(data);
                list.Select(selected == null ? -1 : items.FindIndex(p => p.id == selected.id));
            });

            // The selected phrase. Its fields are rebuilt only when the selection moves to another
            // phrase or index, so typing and dragging are never interrupted.
            var detail = ui.Group(section);
            var naming = new VisualElement();
            detail.Add(naming);
            ui.ShowWhen(ui.InfoRow(detail, "Matched Parameter", () => Matched(data)), () => Matched(data).Length > 0);
            var tuning = new VisualElement();
            detail.Add(tuning);
            ui.Messages(detail, () => PhraseMessages(InfoOf(Selected(data))?.status));
            var record = ui.Button(detail, "Record Takes…", () =>
            {
                var phrase = Selected(data);
                if (phrase != null) VisemePhraseEnrollmentOverlay.Open(data, phrase.id);
            }, YUCPUIToolkitHelper.ButtonVariant.Primary, "Opens the recorder for this phrase. No audio is saved.");
            ui.ShowWhen(detail, () => Selected(data) != null);

            string shownDetail = null;
            ui.OnRefresh(() =>
            {
                var ready = InfoOf(Selected(data))?.status?.IsReady == true;
                var text = ready ? "Improve Takes…" : "Record Takes…";
                if (record.text != text) record.text = text;
                record.EnableInClassList("yucp-button-primary", !ready);
                record.EnableInClassList("yucp-button-secondary", ready);

                var phrase = Selected(data);
                var index = phrase == null ? -1 : data.phrases.IndexOf(phrase);
                var key = index + "/" + phrase?.id;
                if (key == shownDetail) return;
                shownDetail = key;
                naming.Clear();
                tuning.Clear();
                if (index < 0) return;
                var path = $"phrases.Array.data[{index}].";
                naming.Add(DelayedText(ui.Object, path + "prompt", "Phrase"));
                naming.Add(DelayedText(ui.Object, path + "parameterKey", "Output Parameter"));
                ui.Field(tuning, path + "mode", "Context");
                ui.Field(tuning, path + "strictness", "Exactness");
                ui.Field(tuning, path + "pulseSeconds", "Pulse Length");
                ui.Field(tuning, path + "cooldownSeconds", "Cooldown");
            });
        }

        /// <summary>Read-only details for experts: the selected phrase's compiled model and the avatar-wide cost.</summary>
        private void BuildDiagnostics(YUCPInspector ui, VisemePhraseTriggerData data, VisualElement advanced)
        {
            VisemePhraseCompiledModel Model() => InfoOf(Selected(data))?.model;

            var compiled = ui.Group(advanced, "Compiled model");
            ui.InfoRow(compiled, "Phrase", () => Name(Selected(data), data.phrases.IndexOf(Selected(data))));
            var rows = new VisualElement();
            compiled.Add(rows);
            ui.InfoRow(rows, "Branches", () => string.Join("\n", VisemePhraseInspectorDiagnostics.Branches(Model())));
            ui.InfoRow(rows, "Token Timing", () =>
            {
                var timing = VisemePhraseInspectorDiagnostics.Timing(Model());
                return timing.available
                    ? $"{timing.minimum:0.000} s min, {timing.median:0.000} s median, {timing.maximum:0.000} s max"
                    : "Unavailable";
            });
            ui.InfoRow(rows, "Fit", () => Model()?.diagnostics == null
                ? "Unavailable"
                : $"{Model().diagnostics.positiveConsistency:P0} consistency, {Model().diagnostics.distinctiveness:P0} distinctiveness");
            ui.Caption(rows, () => Model() == null ? "" : VisemePhraseInspectorDiagnostics.Calibration(Model()) + ".");
            ui.ShowWhen(rows, () => Model() != null);
            ui.Caption(compiled, () => Model() != null ? "" : "Not compiled for the current phrase and settings. Record four takes to compile it.");
            ui.ShowWhen(compiled, () => Selected(data) != null);

            var avatar = ui.Group(advanced, "Cost");
            ui.InfoRow(avatar, "Matcher States", () => cost.matcher);
            ui.InfoRow(avatar, "Synced Bits", () => cost.bits);
            ui.ShowWhen(ui.InfoRow(avatar, "Avatar Total", () => cost.total ?? ""), () => cost.total != null);
        }

        /// <summary>
        /// Evaluates each phrase's takes and the avatar cost when the phrases or their takes change.
        /// Evaluating can recompile a model, so a value still changing (a dragged Exactness slider)
        /// is evaluated once it settles; a new phrase is evaluated at once.
        /// </summary>
        private void UpdateInfo(VisemePhraseTriggerData data)
        {
            var phrases = (data.phrases ?? new List<VisemePhraseDefinition>()).Where(p => p != null).ToList();
            var key = Signature(data, phrases);
            if (key == infoKey) return;
            var settled = key == pendingKey;
            pendingKey = key;
            if (!settled && infoKey != null && phrases.All(p => info.ContainsKey(p.id))) return;
            infoKey = key;
            info.Clear();
            foreach (var phrase in phrases)
                info[phrase.id] = new PhraseInfo
                {
                    status = VisemePhraseEnrollmentStatus.Evaluate(data, phrase),
                    model = data.enrollmentProfile != null ? data.enrollmentProfile.FindCompiledModel(phrase.id, phrase.PromptFingerprint) : null
                };
            cost = Cost(data);
        }

        private static string Signature(VisemePhraseTriggerData data, List<VisemePhraseDefinition> phrases)
        {
            var profile = data.enrollmentProfile;
            var profileKey = profile == null
                ? "none"
                : profile.GetInstanceID() + ":" + EditorUtility.GetDirtyCount(profile) + ":" + profile.profileSchemaVersion;
            return profileKey + "|" + data.parameterPrefix + "|" + string.Join("|", phrases.Select(phrase =>
            {
                var enrollment = profile != null ? profile.FindEnrollment(phrase.id, phrase.PromptFingerprint) : null;
                return string.Join(":",
                    phrase.id,
                    phrase.prompt,
                    phrase.parameterKey,
                    (int)phrase.mode,
                    phrase.strictness.ToString("R"),
                    enrollment?.positiveTakes?.Count ?? 0,
                    enrollment?.negativeTraces?.Count ?? 0,
                    enrollment?.compiledModel?.contentFingerprint ?? string.Empty);
            }));
        }

        private static (string matcher, string bits, string total) Cost(VisemePhraseTriggerData data)
        {
            var count = data.phrases?.Count ?? 0;
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (descriptor == null) return ("Needs an avatar", $"1 per phrase, {count} here", null);

            var components = descriptor.GetComponentsInChildren<VisemePhraseTriggerData>(true);
            var matcher = VisemePhraseTriggerContractAdapter.TryCreatePlan(descriptor.gameObject, descriptor, components, out var plan, out var error) &&
                          VisemePhraseGlobalTrie.TryBuild(plan, out _, out var states, out error)
                ? $"{states} of {VisemePhraseBuildPlan.MaximumCompiledStates}"
                : error;

            var existing = descriptor.expressionParameters != null ? descriptor.expressionParameters.CalcTotalCost() : 0;
            var existingNames = (descriptor.expressionParameters?.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>())
                .Where(parameter => parameter != null)
                .Select(parameter => parameter.name);
            var carriers = components
                .Where(component => component != null && component.phrases != null)
                .SelectMany(component => component.phrases
                    .Where(phrase => phrase != null)
                    .Select(phrase => AdvancedVisemeParameterContract.PhraseCarrier(component.NormalizedPrefix, phrase.id)))
                .Distinct(StringComparer.Ordinal);
            var memory = VisemePhraseInspectorDiagnostics.ParameterMemory(existing, existingNames, carriers);
            return (matcher,
                $"1 per phrase, {count} here and {memory.phraseCount} on the avatar",
                $"{memory.existingBits} + {memory.newBits} = {memory.estimatedTotal} of {VRCExpressionParameters.MAX_PARAMETER_COST} bits");
        }

        private static IEnumerable<YUCPInspector.Message> PhraseMessages(VisemePhraseEnrollmentStatus status)
        {
            if (status == null) yield break;
            if (!status.IsReady)
            {
                yield return YUCPInspector.Message.Error(status.BlockingReason);
                yield break;
            }
            yield return YUCPInspector.Message.Warning(status.issues
                .FirstOrDefault(issue => issue.level == VisemePhraseEnrollmentIssueLevel.Warning).message);
        }

        private string Matched(VisemePhraseTriggerData data)
        {
            var phrase = Selected(data);
            return phrase == null || string.IsNullOrWhiteSpace(phrase.parameterKey)
                ? ""
                : AdvancedVisemeParameterContract.PhraseMatched(data.NormalizedPrefix, phrase.parameterKey);
        }

        private static string Name(VisemePhraseDefinition phrase, int index) =>
            phrase == null ? "" : string.IsNullOrWhiteSpace(phrase.prompt) ? $"Phrase {index + 1}" : phrase.prompt;

        private void AddPhrase(YUCPInspector ui, VisemePhraseTriggerData data)
        {
            Undo.RecordObject(data, "Add Viseme Phrase");
            if (data.phrases == null) data.phrases = new List<VisemePhraseDefinition>();
            var phrase = new VisemePhraseDefinition();
            data.phrases.Add(phrase);
            data.EnsureDefaults();
            PrefabUtility.RecordPrefabInstancePropertyModifications(data);
            selectedId = phrase.id;
            ui.Refresh();
        }

        /// <summary>
        /// Phrase text commits on Enter or when focus leaves, so the component's clean-up (trimming,
        /// the default output name) runs on the finished text rather than on each keystroke.
        /// </summary>
        private static TextField DelayedText(SerializedObject serializedObject, string path, string label)
        {
            var property = serializedObject.FindProperty(path);
            var field = new TextField(label) { isDelayed = true, tooltip = property.tooltip };
            field.AddToClassList("yucp-field-input");
            field.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            field.BindProperty(property);
            return field;
        }
    }
}
