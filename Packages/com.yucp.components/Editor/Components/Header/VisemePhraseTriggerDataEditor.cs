using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
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
        private enum InspectorMode
        {
            Simple,
            Advanced
        }

        private VisemePhraseTriggerData data;
        private SerializedProperty sourcePrefixProp;
        private SerializedProperty parameterPrefixProp;
        private SerializedProperty enrollmentProfileProp;
        private SerializedProperty phrasesProp;
        private VisualElement simpleHost;
        private VisualElement advancedHost;
        private VisualElement phraseListHost;
        private VisualElement validationHost;
        private VisualElement advancedDiagnosticsHost;
        private Button simpleTab;
        private Button advancedTab;
        private InspectorMode mode;
        private int previousPhraseCount = -1;
        private string previousPhraseSignature = string.Empty;

        private void OnEnable()
        {
            data = (VisemePhraseTriggerData)target;
            data.EnsureDefaults();
            sourcePrefixProp = serializedObject.FindProperty("sourcePrefix");
            parameterPrefixProp = serializedObject.FindProperty("parameterPrefix");
            enrollmentProfileProp = serializedObject.FindProperty("enrollmentProfile");
            phrasesProp = serializedObject.FindProperty("phrases");
            mode = (InspectorMode)Mathf.Clamp(
                SessionState.GetInt(ModeKey(data.GetInstanceID()), (int)InspectorMode.Simple),
                0,
                1);
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            var tabsStylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Layouts/YUCPTabs.uss");
            if (tabsStylesheet != null) root.styleSheets.Add(tabsStylesheet);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay(
                "Viseme Phrase Trigger"));

            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(
                typeof(VisemePhraseTriggerData));
            if (supportBanner != null) root.Add(supportBanner);

            validationHost = new VisualElement();
            root.Add(validationHost);
            BuildTabs(root);

            simpleHost = new VisualElement { name = "simple-phrase-trigger" };
            simpleHost.AddToClassList("yucp-tabs-content");
            root.Add(simpleHost);
            BuildSimple(simpleHost);

            advancedHost = new VisualElement { name = "advanced-phrase-trigger" };
            advancedHost.AddToClassList("yucp-tabs-content");
            root.Add(advancedHost);
            BuildAdvanced(advancedHost);

            ApplyMode();
            UpdateUi();
            root.schedule.Execute(() =>
            {
                if (target == null) return;
                serializedObject.UpdateIfRequiredOrScript();
                var signature = PhraseSignature();
                if (phrasesProp.arraySize != previousPhraseCount ||
                    !string.Equals(signature, previousPhraseSignature, StringComparison.Ordinal))
                    RebuildPhraseList();
                UpdateUi();
            }).Every(200);
            return root;
        }

        private void BuildTabs(VisualElement root)
        {
            var tabs = new VisualElement();
            tabs.AddToClassList("yucp-tabs-header");
            simpleTab = new Button(() => SetMode(InspectorMode.Simple)) { text = "Simple" };
            simpleTab.AddToClassList("yucp-tab");
            simpleTab.style.flexGrow = 1f;
            tabs.Add(simpleTab);
            advancedTab = new Button(() => SetMode(InspectorMode.Advanced)) { text = "Advanced" };
            advancedTab.AddToClassList("yucp-tab");
            advancedTab.style.flexGrow = 1f;
            tabs.Add(advancedTab);
            root.Add(tabs);
        }

        private void BuildSimple(VisualElement root)
        {
            var intro = YUCPUIToolkitHelper.CreateCard(
                "Phrase shapes",
                "Turn a distinctive spoken mouth pattern into Animator parameters. This matches your enrolled viseme pattern, not words or audio.");
            var introContent = YUCPUIToolkitHelper.GetCardContent(intro);
            introContent.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "No microphone audio is saved. Each phrase stores four small Viseme + Voice traces.",
                YUCPUIToolkitHelper.MessageType.Info));
            root.Add(intro);

            phraseListHost = new VisualElement();
            root.Add(phraseListHost);
            RebuildPhraseList();

            var add = YUCPUIToolkitHelper.CreateButton(
                "Add another phrase",
                AddPhrase,
                YUCPUIToolkitHelper.ButtonVariant.Ghost);
            root.Add(add);
        }

        private void BuildAdvanced(VisualElement root)
        {
            var inputCard = YUCPUIToolkitHelper.CreateCard(
                "Input & outputs",
                "Leave the source blank to detect the compatible viseme source automatically.");
            var input = YUCPUIToolkitHelper.GetCardContent(inputCard);
            input.Add(YUCPUIToolkitHelper.CreateField(sourcePrefixProp, "Viseme Source Prefix"));
            input.Add(YUCPUIToolkitHelper.CreateField(parameterPrefixProp, "Output Prefix"));
            input.Add(YUCPUIToolkitHelper.CreateField(enrollmentProfileProp, "Enrollment Profile"));
            root.Add(inputCard);

            var phraseCard = YUCPUIToolkitHelper.CreateCard(
                "Phrase definitions",
                "Expert timing, confidence and public parameter-key settings.");
            YUCPUIToolkitHelper.GetCardContent(phraseCard)
                .Add(YUCPUIToolkitHelper.CreateField(phrasesProp, "Phrases"));
            root.Add(phraseCard);

            var diagnosticsCard = YUCPUIToolkitHelper.CreateCard(
                "Compiled model",
                "Read-only enrollment branches, timing, calibration and build cost.");
            advancedDiagnosticsHost = YUCPUIToolkitHelper.GetCardContent(diagnosticsCard);
            root.Add(diagnosticsCard);
            RebuildAdvancedDiagnostics();

            var parameters = YUCPUIToolkitHelper.CreateCard(
                "Animator parameters",
                "These are local controller parameters unless another component explicitly synchronizes them.");
            var parameterContent = YUCPUIToolkitHelper.GetCardContent(parameters);
            parameterContent.Add(new Label(
                "…/{Phrase}/Matched     one-shot pulse\n" +
                "…/{Phrase}/Confidence  live remaining acceptance margin\n" +
                "…/{Phrase}/Progress    current phrase progress"));
            root.Add(parameters);
        }

        private void RebuildPhraseList()
        {
            if (phraseListHost == null) return;
            serializedObject.UpdateIfRequiredOrScript();
            phraseListHost.Clear();
            previousPhraseCount = phrasesProp.arraySize;
            previousPhraseSignature = PhraseSignature();

            if (data.phrases == null || data.phrases.Count == 0)
            {
                var empty = YUCPUIToolkitHelper.CreateCard(
                    "No phrase yet",
                    "Add one, enter what you will say, then record four examples.");
                YUCPUIToolkitHelper.GetCardContent(empty).Add(
                    YUCPUIToolkitHelper.CreateButton(
                        "Add your first phrase",
                        AddPhrase,
                        YUCPUIToolkitHelper.ButtonVariant.Primary));
                phraseListHost.Add(empty);
                return;
            }

            for (var i = 0; i < data.phrases.Count; i++)
            {
                var phrase = data.phrases[i];
                if (phrase == null) continue;
                var index = i;
                var card = YUCPUIToolkitHelper.CreateCard(
                    $"Phrase {i + 1}",
                    "Use wording long enough to make at least four distinct mouth shapes.");
                var content = YUCPUIToolkitHelper.GetCardContent(card);

                var prompt = new TextField("What will you say?")
                {
                    value = phrase.prompt ?? string.Empty,
                    isDelayed = true
                };
                prompt.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(data, "Edit Viseme Phrase");
                    phrase.prompt = (evt.newValue ?? string.Empty).Trim();
                    data.EnsureDefaults();
                    EditorUtility.SetDirty(data);
                    UpdateUi();
                });
                content.Add(prompt);

                var outputKey = new TextField("Output parameter")
                {
                    value = phrase.parameterKey ?? string.Empty,
                    isDelayed = true,
                    tooltip = "Readable key used in the public .../Matched, .../Confidence and .../Progress parameters."
                };
                outputKey.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(data, "Edit Viseme Phrase Output");
                    var requested = (evt.newValue ?? string.Empty).Trim();
                    phrase.parameterKey = string.IsNullOrEmpty(requested)
                        ? string.IsNullOrWhiteSpace(phrase.prompt)
                            ? string.Empty
                            : AdvancedVisemeParameterContract.DefaultParameterKey(
                                phrase.prompt,
                                phrase.id)
                        : AdvancedVisemeParameterContract.NormalizePhraseId(requested);
                    data.EnsureDefaults();
                    EditorUtility.SetDirty(data);
                    RebuildPhraseList();
                    UpdateUi();
                });
                content.Add(outputKey);

                var paused = new Toggle("Only after a short pause")
                {
                    value = phrase.mode == VisemePhraseContextMode.PausedCommand,
                    tooltip = "Reduces accidental matches by requiring quiet speech boundaries around the phrase."
                };
                paused.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(data, "Change Viseme Phrase Context");
                    phrase.mode = evt.newValue
                        ? VisemePhraseContextMode.PausedCommand
                        : VisemePhraseContextMode.NaturalSpeech;
                    EditorUtility.SetDirty(data);
                });
                content.Add(paused);

                var exactness = new Slider("How exact should it be?", 0f, 1f)
                {
                    value = phrase.strictness,
                    tooltip = "Lower accepts more variation. Higher rejects recordings that differ from your examples."
                };
                exactness.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(data, "Change Viseme Phrase Exactness");
                    phrase.strictness = Mathf.Clamp01(evt.newValue);
                    EditorUtility.SetDirty(data);
                });
                content.Add(exactness);

                var status = VisemePhraseEnrollmentStatus.Evaluate(data, phrase);
                content.Add(EnrollmentSummary(status));

                var output = string.IsNullOrWhiteSpace(phrase.parameterKey)
                    ? "Enter a phrase to create its default output key."
                    : "Matched parameter: " + AdvancedVisemeParameterContract.PhraseMatched(
                        data.NormalizedPrefix,
                        phrase.parameterKey);
                var outputLabel = new Label(output);
                outputLabel.style.opacity = 0.7f;
                outputLabel.style.whiteSpace = WhiteSpace.Normal;
                content.Add(outputLabel);

                var actions = new VisualElement();
                actions.style.flexDirection = FlexDirection.Row;
                var enroll = YUCPUIToolkitHelper.CreateButton(
                    "Record / Improve",
                    () => VisemePhraseEnrollmentOverlay.Open(data, phrase.id),
                    status.IsReady
                        ? YUCPUIToolkitHelper.ButtonVariant.Secondary
                        : YUCPUIToolkitHelper.ButtonVariant.Primary);
                enroll.style.flexGrow = 1f;
                actions.Add(enroll);
                var remove = YUCPUIToolkitHelper.CreateButton(
                    "Remove",
                    () => RemovePhrase(index),
                    YUCPUIToolkitHelper.ButtonVariant.Ghost);
                actions.Add(remove);
                content.Add(actions);
                phraseListHost.Add(card);
            }
            RebuildAdvancedDiagnostics();
        }

        private void RebuildAdvancedDiagnostics()
        {
            if (advancedDiagnosticsHost == null) return;
            advancedDiagnosticsHost.Clear();
            var profile = data.enrollmentProfile;
            if (profile == null || data.phrases == null || data.phrases.Count == 0)
            {
                advancedDiagnosticsHost.Add(new Label(
                    "Record four takes to create compiled diagnostics."));
                return;
            }

            foreach (var phrase in data.phrases.Where(candidate => candidate != null))
            {
                var model = profile.FindCompiledModel(phrase.id, phrase.PromptFingerprint);
                var section = YUCPUIToolkitHelper.CreateSection(
                    string.IsNullOrWhiteSpace(phrase.prompt) ? "Unnamed phrase" : phrase.prompt);
                var content = YUCPUIToolkitHelper.GetSectionContent(section);
                if (model == null)
                {
                    content.Add(new Label("Not compiled for the current prompt and settings."));
                    advancedDiagnosticsHost.Add(section);
                    continue;
                }

                foreach (var branchText in VisemePhraseInspectorDiagnostics.Branches(model))
                {
                    var branch = new Label(branchText);
                    branch.style.whiteSpace = WhiteSpace.Normal;
                    content.Add(branch);
                }

                var timing = VisemePhraseInspectorDiagnostics.Timing(model);
                if (timing.available)
                {
                    content.Add(Caption(
                        $"Learned token timing: {timing.minimum:0.000}s min  ·  " +
                        $"{timing.median:0.000}s median  ·  " +
                        $"{timing.maximum:0.000}s max"));
                }

                content.Add(Caption(VisemePhraseInspectorDiagnostics.Calibration(model)));
                if (model.diagnostics != null)
                    content.Add(Caption(
                        $"Fit: {model.diagnostics.positiveConsistency:P0} consistency  ·  " +
                        $"{model.diagnostics.distinctiveness:P0} distinctiveness"));
                advancedDiagnosticsHost.Add(section);
            }

            advancedDiagnosticsHost.Add(YUCPUIToolkitHelper.CreateDivider());
            AddComplexityDiagnostics(advancedDiagnosticsHost);
            AddParameterMemoryDiagnostics(advancedDiagnosticsHost);
        }

        private void AddComplexityDiagnostics(VisualElement host)
        {
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                host.Add(Caption("Matcher complexity: unavailable without an Avatar Descriptor."));
                return;
            }
            var avatarRoot = descriptor.gameObject;
            var components = avatarRoot.GetComponentsInChildren<VisemePhraseTriggerData>(true);
            if (!VisemePhraseTriggerContractAdapter.TryCreatePlan(
                    avatarRoot,
                    descriptor,
                    components,
                    out var plan,
                    out var error) ||
                !VisemePhraseGlobalTrie.TryBuild(plan, out _, out var states, out error))
            {
                host.Add(Caption("Matcher complexity: " + error));
                return;
            }
            host.Add(Caption(
                $"Unique matcher states: {states}/{VisemePhraseBuildPlan.MaximumCompiledStates}"));
        }

        private void AddParameterMemoryDiagnostics(VisualElement host)
        {
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            var phraseCount = data.phrases?.Count ?? 0;
            if (descriptor == null)
            {
                host.Add(Caption($"Parameter memory: exactly {phraseCount} synced bit(s) " +
                                 "(1 Bool carrier per phrase)."));
                return;
            }

            var existing = descriptor.expressionParameters != null
                ? descriptor.expressionParameters.CalcTotalCost()
                : 0;
            var existingNames = (descriptor.expressionParameters?.parameters ??
                                 Array.Empty<VRCExpressionParameters.Parameter>())
                .Where(parameter => parameter != null)
                .Select(parameter => parameter.name)
                .ToHashSet(StringComparer.Ordinal);
            var avatarComponents = descriptor.gameObject
                .GetComponentsInChildren<VisemePhraseTriggerData>(true);
            var avatarPhrases = avatarComponents
                .Where(component => component != null && component.phrases != null)
                .SelectMany(component => component.phrases
                    .Where(phrase => phrase != null)
                    .Select(phrase => AdvancedVisemeParameterContract.PhraseCarrier(
                        component.NormalizedPrefix,
                        phrase.id)))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var memory = VisemePhraseInspectorDiagnostics.ParameterMemory(
                existing,
                existingNames,
                avatarPhrases);
            host.Add(Caption(
                $"Parameter memory: exactly 1 synced bit per phrase " +
                $"({phraseCount} on this component, {memory.phraseCount} avatar-wide); " +
                $"avatar estimate {memory.existingBits} + {memory.newBits} = {memory.estimatedTotal}/" +
                $"{VRCExpressionParameters.MAX_PARAMETER_COST} bits."));
        }

        private static Label Caption(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.opacity = 0.75f;
            label.style.marginTop = 3f;
            return label;
        }

        private static VisualElement EnrollmentSummary(VisemePhraseEnrollmentStatus status)
        {
            if (status.IsReady)
            {
                var warning = status.issues.FirstOrDefault(issue =>
                    issue.level == VisemePhraseEnrollmentIssueLevel.Warning);
                return YUCPUIToolkitHelper.CreateHelpBox(
                    string.IsNullOrEmpty(warning.message)
                        ? "Four takes are ready."
                        : warning.message,
                    string.IsNullOrEmpty(warning.message)
                        ? YUCPUIToolkitHelper.MessageType.Success
                        : YUCPUIToolkitHelper.MessageType.Warning,
                    "Enrollment ready");
            }
            return YUCPUIToolkitHelper.CreateHelpBox(
                status.BlockingReason,
                YUCPUIToolkitHelper.MessageType.Warning,
                $"{status.CompletedTakes}/4 takes ready");
        }

        private void AddPhrase()
        {
            Undo.RecordObject(data, "Add Viseme Phrase");
            if (data.phrases == null) data.phrases = new System.Collections.Generic.List<VisemePhraseDefinition>();
            data.phrases.Add(new VisemePhraseDefinition());
            data.EnsureDefaults();
            EditorUtility.SetDirty(data);
            serializedObject.Update();
            RebuildPhraseList();
            UpdateUi();
        }

        private void RemovePhrase(int index)
        {
            if (index < 0 || index >= data.phrases.Count) return;
            var label = string.IsNullOrWhiteSpace(data.phrases[index]?.prompt)
                ? $"Phrase {index + 1}"
                : data.phrases[index].prompt;
            if (!EditorUtility.DisplayDialog(
                    "Remove phrase?",
                    $"Remove “{label}” from this component? The reusable enrollment profile asset will not be deleted.",
                    "Remove",
                    "Cancel"))
                return;
            Undo.RecordObject(data, "Remove Viseme Phrase");
            data.phrases.RemoveAt(index);
            data.EnsureDefaults();
            EditorUtility.SetDirty(data);
            serializedObject.Update();
            RebuildPhraseList();
            UpdateUi();
        }

        private void UpdateUi()
        {
            if (data == null || validationHost == null) return;
            validationHost.Clear();
            if (data.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>() == null)
                validationHost.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Place this component on or below a VRChat Avatar Descriptor.",
                    YUCPUIToolkitHelper.MessageType.Error));
            if (data.phrases == null || data.phrases.Count == 0)
                validationHost.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Add at least one phrase before building.",
                    YUCPUIToolkitHelper.MessageType.Warning));
            else if (VisemePhraseEnrollmentStatus.TryGetBlockingReason(data, out var reason))
                validationHost.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    reason,
                    YUCPUIToolkitHelper.MessageType.Warning));
        }

        private void SetMode(InspectorMode next)
        {
            mode = next;
            SessionState.SetInt(ModeKey(data.GetInstanceID()), (int)mode);
            ApplyMode();
        }

        private void ApplyMode()
        {
            if (simpleHost != null)
                simpleHost.style.display = mode == InspectorMode.Simple
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            if (advancedHost != null)
                advancedHost.style.display = mode == InspectorMode.Advanced
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            if (simpleTab != null)
            {
                if (mode == InspectorMode.Simple) simpleTab.AddToClassList("yucp-tab-selected");
                else simpleTab.RemoveFromClassList("yucp-tab-selected");
            }
            if (advancedTab != null)
            {
                if (mode == InspectorMode.Advanced) advancedTab.AddToClassList("yucp-tab-selected");
                else advancedTab.RemoveFromClassList("yucp-tab-selected");
            }
        }

        private static string ModeKey(int instanceId) => $"YUCP_VPT_UIMode_{instanceId}";

        private string PhraseSignature()
        {
            if (data == null || data.phrases == null) return string.Empty;
            var profile = data.enrollmentProfile;
            return string.Join("|", data.phrases.Select(phrase =>
            {
                if (phrase == null) return "null";
                var enrollment = profile?.FindEnrollment(phrase.id, phrase.PromptFingerprint);
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
    }
}
