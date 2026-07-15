using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor.VisemePhrase
{
    /// <summary>
    /// Guided enrollment for the avatar creator who consumes a configured prefab.
    /// Phrase configuration stays in the component inspector; this window only
    /// teaches the configured phrases to the creator's voice and accent.
    /// </summary>
    public sealed class VisemePhraseEnrollmentOverlay : EditorWindow
    {
        private const double NegativeSampleSeconds = 15d;
        private const double AcceptedStateSeconds = 0.85d;
        private const double RetryStateSeconds = 1.15d;
        private const int VisualizerBarCount = 15;
        private const string OverlayStylePath =
            "Packages/com.yucp.components/Editor/Components/VisemePhrase/VisemePhraseEnrollmentOverlay.uss";

        private readonly List<IVisemePhraseEnrollmentDraft> drafts =
            new List<IVisemePhraseEnrollmentDraft>();

        private VisemePhraseEnrollmentFlow flow;
        private VisemePhraseEnrollmentCaptureSession capture;
        private string microphoneDevice = string.Empty;
        private VisemeTestAnalysisBackend backend = VisemeTestAnalysisBackend.Auto;
        private float microphoneGain = 1f;
        private float noiseGate = 0.012f;
        private int recordingTake = -1;
        private bool recordingNegative;
        private bool pendingNegativeRecording;
        private bool advancedExpanded;
        private bool microphoneAutoStartAttempted;
        private bool retakeRequested;
        private bool rerecordAllRequested;
        private int acceptedTake = -1;
        private int rejectedTake = -1;
        private double automaticResumeAt;
        private double microphoneStartedAt;
        private int microphoneReconnectAttempts;
        private long observedAnalysisFrameCount;
        private double lastAnalysisFrameAt;
        private double lastVisualizerUpdate;
        private float visualizerLevel;
        private string visibleStageStatus = string.Empty;
        private VisemePhraseCapturedTake takeBeingReplaced;
        private bool returnToReviewAfterRejectedReplacement;
        private string transientMessage = string.Empty;
        private string lastBackendStatus = string.Empty;
        private Vector2 savedScrollOffset;
        private int scrollPhraseIndex = -1;
        private VisemePhraseEnrollmentStep scrollStep;
        private bool hasScrollContext;
        private IVisualElementScheduledItem updateSchedule;
        private Action<bool> resumePlay;

        private VisualElement microphonePickerHost;
        private VisualElement backendStatusHost;
        private VisualElement statusHost;
        private VisualElement liveDot;
        private VisualElement recordingStage;
        private VisualElement visualizer;
        private readonly List<VisualElement> visualizerBars = new List<VisualElement>();
        private Label technicalVisemeLabel;
        private Label recordingStateLabel;
        private Label stageTimerLabel;
        private Label negativeStatus;
        private Button stageActionButton;
        private Button negativeButton;
        private Button clearNegativeButton;
        private Button backButton;
        private Button primaryButton;
        private Label assetPathLabel;

        private IVisemePhraseEnrollmentDraft Draft => flow?.CurrentDraft;
        internal bool HasPlayModeResume => resumePlay != null;

        public static void Open(VisemePhraseTriggerData component)
        {
            Open(component, string.Empty);
        }

        public static void Open(VisemePhraseTriggerData component, string phraseId)
        {
            if (component == null) return;
            component.EnsureDefaults();
            if (component.phrases.Count == 0)
            {
                Undo.RecordObject(component, "Add Viseme Phrase");
                component.phrases.Add(new VisemePhraseDefinition());
                component.EnsureDefaults();
                EditorUtility.SetDirty(component);
            }

            var contexts = component.phrases
                .Where(candidate => candidate != null)
                .Select(candidate => (IVisemePhraseEnrollmentDraft)
                    new VisemePhraseEnrollmentDraft(
                        component,
                        candidate,
                        createProfile: false))
                .ToList();
            var preferred = string.IsNullOrWhiteSpace(phraseId)
                ? null
                : contexts.OfType<VisemePhraseEnrollmentDraft>().FirstOrDefault(candidate =>
                    string.Equals(candidate.Phrase.id, phraseId, StringComparison.Ordinal));
            ShowOverlay(
                contexts,
                ResolveAvatarRoot(component.gameObject),
                preferred,
                savePromptsOnOpen: true,
                resumePlay: null);
        }

        public static void OpenForAvatar(
            GameObject avatarRoot,
            IReadOnlyList<VisemePhraseTriggerData> components)
        {
            OpenForAvatar(
                avatarRoot,
                components,
                createProfiles: false,
                resumePlay: null);
        }

        internal static VisemePhraseEnrollmentOverlay OpenForAvatar(
            GameObject avatarRoot,
            IReadOnlyList<VisemePhraseTriggerData> components,
            bool createProfiles,
            Action<bool> resumePlay)
        {
            var contexts = new List<IVisemePhraseEnrollmentDraft>();
            if (components != null)
            {
                foreach (var component in components.Where(candidate => candidate != null))
                {
                    component.EnsureDefaults();
                    foreach (var phrase in component.phrases.Where(candidate => candidate != null))
                        contexts.Add(new VisemePhraseEnrollmentDraft(
                            component,
                            phrase,
                            createProfiles));
                }
            }

            // Keep the prefab author's order. Select the first phrase needing help
            // without rearranging the list the avatar creator sees.
            var preferred = contexts.FirstOrDefault(candidate =>
                StatusFor(candidate).HasBlockingIssues) ?? contexts.FirstOrDefault();
            return ShowOverlay(
                contexts,
                avatarRoot,
                preferred,
                savePromptsOnOpen: createProfiles,
                resumePlay: resumePlay);
        }

        private static VisemePhraseEnrollmentOverlay ShowOverlay(
            IReadOnlyList<IVisemePhraseEnrollmentDraft> contexts,
            GameObject avatarRoot,
            IVisemePhraseEnrollmentDraft preferred,
            bool savePromptsOnOpen,
            Action<bool> resumePlay)
        {
            var window = GetWindow<VisemePhraseEnrollmentOverlay>(
                true,
                "Teach Avatar Phrases",
                true);
            window.minSize = new Vector2(600f, 620f);
            window.Initialize(
                contexts,
                avatarRoot,
                preferred,
                savePromptsOnOpen,
                resumePlay);
            window.Show();
            window.Focus();
            return window;
        }

        internal static VisemePhraseEnrollmentOverlay OpenForDrafts(
            IReadOnlyList<IVisemePhraseEnrollmentDraft> contexts)
        {
            var window = CreateInstance<VisemePhraseEnrollmentOverlay>();
            window.titleContent = new GUIContent("Teach Avatar Phrases");
            window.minSize = new Vector2(600f, 620f);
            window.Initialize(
                contexts,
                contexts?.FirstOrDefault()?.AvatarRoot,
                contexts?.FirstOrDefault(),
                savePromptsOnOpen: true,
                resumePlay: null);
            window.ShowUtility();
            return window;
        }

        private void Initialize(
            IReadOnlyList<IVisemePhraseEnrollmentDraft> contexts,
            GameObject requestedAvatarRoot,
            IVisemePhraseEnrollmentDraft preferred,
            bool savePromptsOnOpen,
            Action<bool> resumePlay)
        {
            capture?.Dispose();
            capture = null;
            drafts.Clear();
            if (contexts != null) drafts.AddRange(contexts.Where(candidate => candidate != null));
            this.resumePlay = resumePlay;

            var preferredIndex = preferred == null ? -1 : drafts.IndexOf(preferred);
            if (preferredIndex < 0 && requestedAvatarRoot != null)
                preferredIndex = drafts.FindIndex(candidate => candidate.AvatarRoot == requestedAvatarRoot);

            // Opening enrollment also refreshes completed raw takes when recognition
            // settings changed. No valid recording is thrown away.
            if (savePromptsOnOpen)
            {
                foreach (var candidate in drafts.Where(candidate =>
                             !string.IsNullOrWhiteSpace(candidate.Prompt)))
                    candidate.SavePrompt();
            }

            flow = new VisemePhraseEnrollmentFlow(drafts, preferredIndex);
            recordingTake = -1;
            recordingNegative = false;
            pendingNegativeRecording = false;
            microphoneAutoStartAttempted = false;
            retakeRequested = false;
            rerecordAllRequested = false;
            acceptedTake = -1;
            rejectedTake = -1;
            automaticResumeAt = 0d;
            microphoneStartedAt = 0d;
            microphoneReconnectAttempts = 0;
            observedAnalysisFrameCount = 0L;
            lastAnalysisFrameAt = 0d;
            visualizerLevel = 0f;
            visibleStageStatus = string.Empty;
            takeBeingReplaced = null;
            returnToReviewAfterRejectedReplacement = false;
            savedScrollOffset = Vector2.zero;
            scrollPhraseIndex = -1;
            hasScrollContext = false;
            transientMessage = string.Empty;
            if (flow.Step == VisemePhraseEnrollmentStep.Review &&
                TryGetBlockingTakeIndex(flow.CurrentStatus, out var blockingTake))
            {
                RouteToConcreteRetake(blockingTake);
            }
            if (rootVisualElement != null) BuildUi();
        }

        private void CreateGUI()
        {
            BuildUi();
        }

        private void OnDisable()
        {
            updateSchedule?.Pause();
            updateSchedule = null;
            capture?.Dispose();
            capture = null;
        }

        internal void HandleSkipForNow()
        {
            CloseAndOptionallyResume(skipPhraseGeneration: true);
        }

        internal void HandleDone()
        {
            CloseAndOptionallyResume(skipPhraseGeneration: false);
        }

        private void CloseAndOptionallyResume(bool skipPhraseGeneration)
        {
            var resume = resumePlay;
            resumePlay = null;
            capture?.Dispose();
            capture = null;
            Close();
            resume?.Invoke(skipPhraseGeneration);
        }

        private void BuildUi()
        {
            var root = rootVisualElement;
            var previousScroll = root.Q<ScrollView>("phrase-enrollment-scroll");
            var sameScrollContext = previousScroll != null &&
                                    hasScrollContext &&
                                    flow != null &&
                                    scrollPhraseIndex == flow.PhraseIndex &&
                                    scrollStep == flow.Step;
            savedScrollOffset = sameScrollContext
                ? previousScroll.scrollOffset
                : Vector2.zero;
            updateSchedule?.Pause();
            updateSchedule = null;
            ResetVisualReferences();
            root.Clear();
            root.AddToClassList("yucp-phrase-wizard-root");
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            var overlayStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(OverlayStylePath);
            if (overlayStyle != null && !root.styleSheets.Contains(overlayStyle))
                root.styleSheets.Add(overlayStyle);

            if (flow == null || Draft == null)
            {
                root.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "This prefab does not contain a phrase to teach yet. Select its Viseme Phrase Trigger component and ask the prefab creator to configure a phrase.",
                    YUCPUIToolkitHelper.MessageType.Error,
                    "Nothing to teach"));
                return;
            }

            // A diagnosed take error is not a separate review step. Route to it
            // immediately so the microphone can auto-arm without a redundant
            // "Fix take" click.
            if (flow.Step == VisemePhraseEnrollmentStep.Review &&
                TryGetBlockingTakeIndex(flow.CurrentStatus, out var blockingTake))
            {
                flow.SelectTake(blockingTake);
                retakeRequested = true;
            }

            if (flow.Step == VisemePhraseEnrollmentStep.Review &&
                recordingTake < 0 &&
                !recordingNegative &&
                !pendingNegativeRecording &&
                capture != null)
            {
                capture.Dispose();
                capture = null;
                flow.SetMicrophoneReady(false);
                microphoneAutoStartAttempted = false;
                microphoneReconnectAttempts = 0;
                microphoneStartedAt = 0d;
                observedAnalysisFrameCount = 0L;
                lastAnalysisFrameAt = 0d;
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "phrase-enrollment-scroll"
            };
            scroll.AddToClassList("yucp-phrase-wizard-scroll");
            var shell = new VisualElement { name = "phrase-enrollment-shell" };
            shell.AddToClassList("yucp-phrase-wizard-shell");
            scroll.Add(shell);
            root.Add(scroll);

            BuildSessionHeader(shell);

            var pageHost = new VisualElement { name = "phrase-enrollment-page-host" };
            pageHost.AddToClassList("yucp-phrase-page");
            shell.Add(pageHost);
            switch (flow.Step)
            {
                case VisemePhraseEnrollmentStep.Phrase:
                    BuildPhrasePage(pageHost);
                    break;
                case VisemePhraseEnrollmentStep.Microphone:
                    BuildMicrophonePage(pageHost);
                    break;
                case VisemePhraseEnrollmentStep.Takes:
                    BuildTakesPage(pageHost);
                    break;
                case VisemePhraseEnrollmentStep.Review:
                    BuildReviewPage(pageHost);
                    break;
            }

            BuildAdvanced(shell);
            BuildFooter(root);
            var restoreOffset = savedScrollOffset;
            scrollPhraseIndex = flow.PhraseIndex;
            scrollStep = flow.Step;
            hasScrollContext = true;
            scroll.schedule.Execute(() =>
            {
                if (scroll.panel != null) scroll.scrollOffset = restoreOffset;
            }).ExecuteLater(1);
            UpdateUi();
            updateSchedule = root.schedule.Execute(UpdateUi).Every(33);
        }

        private void ResetVisualReferences()
        {
            microphonePickerHost = null;
            backendStatusHost = null;
            statusHost = null;
            liveDot = null;
            recordingStage = null;
            visualizer = null;
            visualizerBars.Clear();
            technicalVisemeLabel = null;
            recordingStateLabel = null;
            stageTimerLabel = null;
            negativeStatus = null;
            stageActionButton = null;
            negativeButton = null;
            clearNegativeButton = null;
            backButton = null;
            primaryButton = null;
            assetPathLabel = null;
            lastBackendStatus = string.Empty;
            visibleStageStatus = string.Empty;
        }

        private void BuildSessionHeader(VisualElement parent)
        {
            if (flow.Drafts.Count <= 1) return;
            var header = new VisualElement { name = "phrase-session-header" };
            header.AddToClassList("yucp-phrase-session-header");

            var copy = new VisualElement();
            copy.AddToClassList("yucp-phrase-session-copy");
            var title = new Label($"Phrase {flow.PhraseIndex + 1} of {flow.Drafts.Count}");
            title.AddToClassList("yucp-phrase-session-title");
            copy.Add(title);
            header.Add(copy);

            if (flow.CurrentStatus.IsReady)
                header.Add(CreateBadge("Ready", "success"));

            var canNavigate = !flow.RecordingLocked &&
                              acceptedTake < 0 &&
                              rejectedTake < 0 &&
                              automaticResumeAt <= 0d;
            var navigation = new VisualElement();
            navigation.AddToClassList("yucp-phrase-header-navigation");
            var previous = YUCPUIToolkitHelper.CreateButton(
                "‹",
                () => SelectPhrase(flow.PhraseIndex - 1),
                YUCPUIToolkitHelper.ButtonVariant.Ghost);
            previous.AddToClassList("yucp-phrase-nav-button");
            previous.tooltip = "Previous phrase";
            previous.SetEnabled(flow.PhraseIndex > 0 && canNavigate);
            navigation.Add(previous);
            var next = YUCPUIToolkitHelper.CreateButton(
                "›",
                () => SelectPhrase(flow.PhraseIndex + 1),
                YUCPUIToolkitHelper.ButtonVariant.Ghost);
            next.AddToClassList("yucp-phrase-nav-button");
            next.tooltip = "Next phrase";
            next.SetEnabled(flow.PhraseIndex < flow.Drafts.Count - 1 && canNavigate);
            navigation.Add(next);
            header.Add(navigation);

            parent.Add(header);
        }

        private void BuildPhrasePage(VisualElement parent)
        {
            parent.name = "phrase-enrollment-page";
            AddPageHeading(
                parent,
                "SETUP NEEDED",
                "This prefab has no phrase",
                "Select its Viseme Phrase Trigger and add a phrase first.");
            parent.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "The prefab creator must configure this before it can be taught.",
                YUCPUIToolkitHelper.MessageType.Error));
        }

        private void BuildMicrophonePage(VisualElement parent)
        {
            BuildRecordingPage(parent, true);
        }

        private void BuildTakesPage(VisualElement parent)
        {
            BuildRecordingPage(parent, false);
        }

        private void BuildRecordingPage(VisualElement parent, bool startingMicrophone)
        {
            parent.name = startingMicrophone
                ? "microphone-enrollment-page"
                : "takes-enrollment-page";
            AddPageHeading(
                parent,
                string.Empty,
                $"Say “{DisplayPrompt(Draft)}”",
                startingMicrophone
                    ? "Your microphone is starting automatically."
                    : "Say it naturally, then pause. We’ll save it for you.");

            BuildTakeStrip(parent);

            recordingStage = new VisualElement { name = "recording-stage" };
            recordingStage.AddToClassList("yucp-phrase-record-stage");
            visualizer = new VisualElement { name = "speech-visualizer" };
            visualizer.AddToClassList("yucp-phrase-visualizer");
            for (var i = 0; i < VisualizerBarCount; i++)
            {
                var bar = new VisualElement();
                bar.AddToClassList("yucp-phrase-visualizer-bar");
                visualizerBars.Add(bar);
                visualizer.Add(bar);
            }
            recordingStage.Add(visualizer);

            var stateRow = new VisualElement();
            stateRow.AddToClassList("yucp-phrase-stage-state-row");
            liveDot = new VisualElement();
            liveDot.AddToClassList("yucp-phrase-live-dot");
            stateRow.Add(liveDot);
            recordingStateLabel = new Label();
            recordingStateLabel.AddToClassList("yucp-phrase-record-state");
            stateRow.Add(recordingStateLabel);
            stageTimerLabel = new Label();
            stageTimerLabel.AddToClassList("yucp-phrase-stage-timer");
            stateRow.Add(stageTimerLabel);
            recordingStage.Add(stateRow);

            stageActionButton = YUCPUIToolkitHelper.CreateButton(
                "Try microphone again",
                RetryAutomaticMicrophone,
                YUCPUIToolkitHelper.ButtonVariant.Secondary);
            stageActionButton.name = "recording-stage-action";
            stageActionButton.AddToClassList("yucp-phrase-stage-action");
            stageActionButton.style.display = DisplayStyle.None;
            recordingStage.Add(stageActionButton);
            parent.Add(recordingStage);

            var controls = new VisualElement();
            controls.AddToClassList("yucp-phrase-compact-controls");
            microphonePickerHost = new VisualElement { name = "microphone-picker" };
            microphonePickerHost.AddToClassList("yucp-phrase-microphone-picker");
            controls.Add(microphonePickerHost);
            BuildMicrophonePicker();
            parent.Add(controls);

            backendStatusHost = new VisualElement { name = "analyzer-status" };
            parent.Add(backendStatusHost);
            statusHost = new VisualElement { name = "take-feedback" };
            statusHost.AddToClassList("yucp-phrase-feedback");
            parent.Add(statusHost);
            if (!startingMicrophone) RebuildTakeFeedback();
            else RebuildTransientStatus();
        }

        private void BuildReviewPage(VisualElement parent)
        {
            parent.name = "review-enrollment-page";
            var ready = flow.CurrentStatus.IsReady;
            AddPageHeading(
                parent,
                string.Empty,
                ready ? "Your phrase is ready" : "Needs attention",
                ready
                    ? "Four examples saved."
                    : "Choose an example to record again.");

            BuildTakeStrip(parent);

            var summary = new VisualElement();
            summary.AddToClassList("yucp-phrase-review-summary");
            var summaryMark = new Label(ready ? "✓" : "!");
            summaryMark.AddToClassList("yucp-phrase-review-mark");
            summary.Add(summaryMark);
            var summaryText = new Label(ready
                ? $"“{DisplayPrompt(Draft)}” is saved"
                : FriendlyReviewIssue(flow.CurrentStatus.issues.FirstOrDefault(issue =>
                    issue.level == VisemePhraseEnrollmentIssueLevel.Blocking)));
            summaryText.AddToClassList("yucp-phrase-review-title");
            summary.Add(summaryText);
            parent.Add(summary);

            statusHost = new VisualElement { name = "review-status" };
            parent.Add(statusHost);
            RebuildReviewStatus();

            // A recorded safety sample can itself be the reason an otherwise
            // complete phrase cannot compile. Keep its remove/retake controls
            // visible instead of sending the creator into an unrelated take.
            if (ready || Draft.NegativeSample != null)
                BuildOptionalSafetySample(parent);
        }

        private void BuildOptionalSafetySample(VisualElement parent)
        {
            var expanded = recordingNegative || Draft.NegativeSample != null;
            var foldout = YUCPUIToolkitHelper.CreateFoldout(
                "Optional: reduce accidental triggers",
                expanded,
                badge: Draft.NegativeSample == null ? "OPTIONAL" : "SAVED");
            foldout.name = "optional-safety-sample";
            var explanation = new Label(
                $"Talk normally for 15 seconds without saying \"{DisplayPrompt(Draft)}\". This can reduce accidental matches.");
            explanation.AddToClassList("yucp-phrase-advanced-note");
            foldout.Add(explanation);
            negativeStatus = new Label();
            negativeStatus.AddToClassList("yucp-phrase-listening-state");
            foldout.Add(negativeStatus);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            negativeButton = YUCPUIToolkitHelper.CreateButton(
                Draft.NegativeSample == null ? "Record 15-second sample" : "Record it again",
                ToggleNegativeRecording,
                YUCPUIToolkitHelper.ButtonVariant.Secondary);
            negativeButton.style.flexGrow = 1f;
            buttons.Add(negativeButton);
            clearNegativeButton = YUCPUIToolkitHelper.CreateButton(
                "Remove sample",
                () =>
                {
                    if (flow.RecordingLocked) return;
                    Draft.ClearNegativeSample();
                    transientMessage = "The optional sample was removed.";
                    BuildUi();
                },
                YUCPUIToolkitHelper.ButtonVariant.Ghost);
            clearNegativeButton.AddToClassList("yucp-phrase-optional-secondary");
            buttons.Add(clearNegativeButton);
            foldout.Add(buttons);
            parent.Add(foldout);
        }

        private void BuildTakeStrip(VisualElement parent)
        {
            var strip = new VisualElement { name = "take-strip" };
            strip.AddToClassList("yucp-phrase-take-strip");
            var hasProblemTake = TryGetBlockingTakeIndex(
                flow.CurrentStatus,
                out var problemTake);
            for (var i = 0; i < VisemePhraseEnrollmentStatus.RequiredTakeCount; i++)
            {
                var take = GetTake(Draft, i);
                var isProblem = hasProblemTake && i == problemTake;
                var slot = new VisualElement { name = $"take-slot-{i + 1}" };
                slot.AddToClassList("yucp-phrase-take-slot");
                if (i == flow.TakeIndex)
                    slot.AddToClassList("yucp-phrase-take-slot-active");
                if (IsTakeReady(take) && !isProblem)
                    slot.AddToClassList("yucp-phrase-take-slot-ready");
                if (isProblem)
                {
                    slot.AddToClassList("yucp-phrase-take-slot-problem");
                    slot.tooltip = "This example will be recorded again.";
                }
                if (i == acceptedTake)
                    slot.AddToClassList("yucp-phrase-take-slot-accepted");
                var mark = new Label(isProblem
                    ? "!"
                    : IsTakeReady(take) ? "✓" : (i + 1).ToString());
                mark.AddToClassList("yucp-phrase-take-slot-mark");
                slot.Add(mark);

                var canRetake = flow.Step == VisemePhraseEnrollmentStep.Review &&
                                IsTakeReady(take) &&
                                !flow.RecordingLocked;
                if (canRetake)
                {
                    var takeIndex = i;
                    slot.AddToClassList("yucp-phrase-take-slot-clickable");
                    slot.focusable = true;
                    slot.tooltip = $"Re-record example {i + 1}";
                    slot.RegisterCallback<ClickEvent>(_ => SelectTakeForReplacement(takeIndex));
                    slot.RegisterCallback<KeyDownEvent>(evt =>
                    {
                        if (evt.keyCode != KeyCode.Return &&
                            evt.keyCode != KeyCode.KeypadEnter &&
                            evt.keyCode != KeyCode.Space) return;
                        evt.StopPropagation();
                        SelectTakeForReplacement(takeIndex);
                    });
                }
                strip.Add(slot);
            }
            parent.Add(strip);
        }

        private void BuildAdvanced(VisualElement parent)
        {
            var foldout = YUCPUIToolkitHelper.CreateFoldout(
                "Advanced details",
                advancedExpanded);
            foldout.name = "enrollment-advanced";
            foldout.AddToClassList("yucp-phrase-advanced");
            foldout.RegisterValueChangedCallback(evt => advancedExpanded = evt.newValue);
            var note = new Label(
                "Only change these when troubleshooting.");
            note.AddToClassList("yucp-phrase-advanced-note");
            foldout.Add(note);

            var backendField = new EnumField("Mouth-shape analyzer", backend);
            backendField.SetEnabled(!flow.RecordingLocked);
            backendField.RegisterValueChangedCallback(evt =>
            {
                if (flow.RecordingLocked) return;
                backend = (VisemeTestAnalysisBackend)evt.newValue;
                RestartMicrophoneIfNeeded();
            });
            foldout.Add(backendField);
            var gain = new Slider("Microphone boost", 0.1f, 5f) { value = microphoneGain };
            gain.SetEnabled(!flow.RecordingLocked);
            gain.RegisterValueChangedCallback(evt =>
            {
                if (flow.RecordingLocked) return;
                microphoneGain = evt.newValue;
                RestartMicrophoneIfNeeded();
            });
            foldout.Add(gain);
            var gate = new Slider("Ignore background noise below", 0f, 0.2f) { value = noiseGate };
            gate.SetEnabled(!flow.RecordingLocked);
            gate.RegisterValueChangedCallback(evt =>
            {
                if (flow.RecordingLocked) return;
                noiseGate = evt.newValue;
                RestartMicrophoneIfNeeded();
            });
            foldout.Add(gate);

            technicalVisemeLabel = new Label("Detected mouth shape: not listening");
            technicalVisemeLabel.AddToClassList("yucp-phrase-advanced-note");
            foldout.Add(technicalVisemeLabel);

            if (flow.Step == VisemePhraseEnrollmentStep.Takes)
                foldout.Add(BuildTokenChips(GetTake(Draft, flow.TakeIndex)));

            assetPathLabel = new Label(Draft.AssetPath);
            assetPathLabel.AddToClassList("yucp-phrase-advanced-note");
            foldout.Add(assetPathLabel);
            var selectProfile = YUCPUIToolkitHelper.CreateButton(
                "Show saved enrollment asset",
                () =>
                {
                    if (Draft.ProfileAsset == null) return;
                    Selection.activeObject = Draft.ProfileAsset;
                    EditorGUIUtility.PingObject(Draft.ProfileAsset);
                },
                YUCPUIToolkitHelper.ButtonVariant.Ghost);
            foldout.Add(selectProfile);
            parent.Add(foldout);
        }

        private void BuildFooter(VisualElement root)
        {
            var footer = new VisualElement { name = "enrollment-footer" };
            footer.AddToClassList("yucp-phrase-footer");
            var footerInner = new VisualElement();
            footerInner.AddToClassList("yucp-phrase-footer-inner");
            footer.Add(footerInner);

            var skipButton = YUCPUIToolkitHelper.CreateButton(
                "Skip for now",
                HandleSkipForNow,
                YUCPUIToolkitHelper.ButtonVariant.Ghost);
            skipButton.name = "enrollment-skip-action";
            skipButton.AddToClassList("yucp-phrase-footer-button");
            skipButton.AddToClassList("yucp-phrase-footer-back");
            footerInner.Add(skipButton);

            var spacer = new VisualElement();
            spacer.AddToClassList("yucp-phrase-footer-spacer");
            footerInner.Add(spacer);

            if (flow.Step != VisemePhraseEnrollmentStep.Review)
            {
                root.Add(footer);
                return;
            }

            if (flow.CurrentStatus.IsReady)
            {
                backButton = YUCPUIToolkitHelper.CreateButton(
                    "Re-record all",
                    HandleRerecord,
                    YUCPUIToolkitHelper.ButtonVariant.Secondary);
                backButton.name = "enrollment-rerecord-action";
                backButton.AddToClassList("yucp-phrase-footer-button");
                footerInner.Add(backButton);
            }

            primaryButton = YUCPUIToolkitHelper.CreateButton(
                PrimaryActionText(),
                HandlePrimary,
                YUCPUIToolkitHelper.ButtonVariant.Primary);
            primaryButton.name = "enrollment-primary-action";
            primaryButton.AddToClassList("yucp-phrase-footer-button");
            primaryButton.AddToClassList("yucp-phrase-footer-primary");
            footerInner.Add(primaryButton);
            root.Add(footer);
        }

        private void BuildMicrophonePicker()
        {
            if (microphonePickerHost == null) return;
            microphonePickerHost.Clear();
            var devices = new List<string> { "System default" };
            devices.AddRange((Microphone.devices ?? Array.Empty<string>())
                .Where(device => !string.IsNullOrWhiteSpace(device)));
            var selected = string.IsNullOrEmpty(microphoneDevice)
                ? "System default"
                : microphoneDevice;
            if (!devices.Contains(selected)) devices.Add(selected + " (not connected)");
            var selectedIndex = Mathf.Max(0, devices.FindIndex(choice =>
                choice.StartsWith(selected, StringComparison.Ordinal)));
            var picker = new PopupField<string>("Microphone", devices, selectedIndex)
            {
                name = "microphone-device"
            };
            picker.SetEnabled(CanChangeMicrophone());
            picker.RegisterValueChangedCallback(evt =>
            {
                if (!CanChangeMicrophone()) return;
                if (recordingTake >= 0 && capture != null && !capture.HasConfirmedSpeech)
                {
                    capture.CancelTake();
                    recordingTake = -1;
                    flow.SetRecordingLocked(false);
                    retakeRequested = true;
                    takeBeingReplaced = null;
                    returnToReviewAfterRejectedReplacement = false;
                }
                microphoneDevice = evt.newValue == "System default"
                    ? string.Empty
                    : evt.newValue.Replace(" (not connected)", string.Empty);
                microphoneAutoStartAttempted = false;
                RestartMicrophoneIfNeeded();
            });
            microphonePickerHost.Add(picker);
        }

        private bool CanChangeMicrophone()
        {
            if (!flow.RecordingLocked) return true;
            return recordingTake >= 0 && capture != null && !capture.HasConfirmedSpeech;
        }

        private void AddPageHeading(
            VisualElement parent,
            string eyebrow,
            string title,
            string description)
        {
            if (!string.IsNullOrWhiteSpace(eyebrow))
            {
                var eyebrowLabel = new Label(eyebrow);
                eyebrowLabel.AddToClassList("yucp-phrase-eyebrow");
                parent.Add(eyebrowLabel);
            }
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("yucp-phrase-page-title");
            parent.Add(titleLabel);
            var descriptionLabel = new Label(description);
            descriptionLabel.AddToClassList("yucp-phrase-page-description");
            parent.Add(descriptionLabel);
        }

        private static Label CreateBadge(string text, string variant)
        {
            var badge = new Label(text);
            badge.AddToClassList("yucp-badge");
            badge.AddToClassList("yucp-badge-" + variant);
            return badge;
        }

        private static VisualElement BuildTokenChips(VisemePhraseCapturedTake take)
        {
            var host = new VisualElement();
            host.style.flexDirection = FlexDirection.Row;
            host.style.flexWrap = Wrap.Wrap;
            host.style.marginTop = 4f;
            host.style.marginBottom = 7f;
            var tokens = VisemePhraseEnrollmentStatus.TokenRuns(take);
            if (tokens.Count == 0)
            {
                host.Add(new Label("No recorded mouth-shape sequence for this take."));
                return host;
            }

            foreach (var token in tokens.Take(16))
            {
                var chip = CreateBadge(
                    VisemeTestMath.VisemeNames[Mathf.Clamp(token, 0, 14)],
                    "info");
                chip.style.marginRight = 3f;
                chip.style.marginBottom = 3f;
                host.Add(chip);
            }
            if (tokens.Count > 16) host.Add(CreateBadge($"+{tokens.Count - 16}", "default"));
            return host;
        }

        private void SelectPhrase(int index)
        {
            if (flow == null || flow.RecordingLocked) return;
            var result = flow.SelectPhrase(index);
            if (result != VisemePhraseEnrollmentNavigationResult.Success &&
                result != VisemePhraseEnrollmentNavigationResult.AlreadyThere)
                return;
            Draft.SavePrompt();
            retakeRequested = false;
            acceptedTake = -1;
            rejectedTake = -1;
            automaticResumeAt = 0d;
            savedScrollOffset = Vector2.zero;
            transientMessage = string.Empty;
            BuildUi();
        }

        private bool EnsureMicrophone()
        {
            if (IsListening) return true;
            microphoneAutoStartAttempted = true;
            capture?.Dispose();
            capture = new VisemePhraseEnrollmentCaptureSession();
            if (capture.Start(
                    Draft.AvatarRoot,
                    microphoneDevice,
                    backend,
                    microphoneGain,
                    noiseGate,
                    out var error))
            {
                microphoneStartedAt = EditorApplication.timeSinceStartup;
                observedAnalysisFrameCount = 0L;
                lastAnalysisFrameAt = microphoneStartedAt;
                transientMessage = string.Empty;
                return true;
            }

            capture.Dispose();
            capture = null;
            flow.SetMicrophoneReady(false);
            microphoneStartedAt = 0d;
            observedAnalysisFrameCount = 0L;
            lastAnalysisFrameAt = 0d;
            transientMessage = FriendlyMicrophoneError(error);
            return false;
        }

        private void RetryAutomaticMicrophone()
        {
            if (flow == null || flow.RecordingLocked) return;
            capture?.Dispose();
            capture = null;
            flow.SetMicrophoneReady(false);
            microphoneAutoStartAttempted = false;
            microphoneReconnectAttempts = 0;
            microphoneStartedAt = 0d;
            observedAnalysisFrameCount = 0L;
            lastAnalysisFrameAt = 0d;
            transientMessage = string.Empty;
            EnsureMicrophone();
            BuildUi();
        }

        private void BeginAutomaticTake(int index)
        {
            if (flow.RecordingLocked || recordingTake >= 0 || recordingNegative) return;
            Draft.SavePrompt();
            PrepareAutomaticTake(index);
            if (!EnsureMicrophone())
            {
                BuildUi();
                return;
            }
            capture.BeginTake();
            recordingTake = index;
            retakeRequested = false;
            flow.SetRecordingLocked(true);
            transientMessage = string.Empty;
            BuildUi();
        }

        private void PrepareAutomaticTake(int index)
        {
            var existing = GetTake(Draft, index);
            takeBeingReplaced = IsTakeReady(existing) ? existing : null;
            returnToReviewAfterRejectedReplacement =
                !rerecordAllRequested &&
                takeBeingReplaced != null && flow.CurrentStatus.IsReady;
            flow.SelectTake(index);
        }

        private void FinishPositiveTake(int index)
        {
            if (capture == null || !capture.IsRecording || recordingTake != index) return;
            var take = capture.FinishTake();
            recordingTake = -1;
            flow.SetRecordingLocked(false);
            var shouldReturnToReviewIfRejected =
                returnToReviewAfterRejectedReplacement;
            var runs = VisemePhraseEnrollmentStatus.InformativeRuns(take);
            var accepted = IsTakeReady(take);
            if (accepted)
                accepted = TrySavePositiveTake(index, take);

            takeBeingReplaced = null;
            if (accepted)
            {
                returnToReviewAfterRejectedReplacement = false;
                acceptedTake = index;
                rejectedTake = -1;
                automaticResumeAt = EditorApplication.timeSinceStartup + AcceptedStateSeconds;
                transientMessage = $"Take {index + 1} saved.";
            }
            else
            {
                returnToReviewAfterRejectedReplacement =
                    shouldReturnToReviewIfRejected;
                acceptedTake = -1;
                rejectedTake = index;
                automaticResumeAt = EditorApplication.timeSinceStartup + RetryStateSeconds;
                if (string.IsNullOrWhiteSpace(transientMessage))
                    transientMessage = FriendlyRetakeMessage(take, runs);
            }
            BuildUi();
        }

        private bool TrySavePositiveTake(
            int index,
            VisemePhraseCapturedTake take)
        {
            if (returnToReviewAfterRejectedReplacement &&
                Draft is VisemePhraseEnrollmentDraft stored &&
                !stored.WouldCompileWithReplacement(index, take))
            {
                transientMessage =
                    "That version did not match your saved examples, so we kept the previous one.";
                return false;
            }

            Draft.SaveTake(index, take);
            return true;
        }

        private void ToggleNegativeRecording()
        {
            if (recordingNegative || pendingNegativeRecording)
            {
                if (recordingNegative) capture?.CancelTake();
                recordingNegative = false;
                pendingNegativeRecording = false;
                flow.SetRecordingLocked(false);
                transientMessage = "The optional recording was cancelled. Your phrase takes are still safe.";
                BuildUi();
                return;
            }
            if (flow.RecordingLocked || recordingTake >= 0 || !EnsureMicrophone())
            {
                BuildUi();
                return;
            }
            if (!capture.HasAnalysisFrame)
            {
                pendingNegativeRecording = true;
                flow.SetRecordingLocked(true);
                transientMessage = string.Empty;
                BuildUi();
                return;
            }
            BeginNegativeRecording();
        }

        private void BeginNegativeRecording()
        {
            if (capture == null || !capture.IsRunning || capture.IsRecording) return;
            capture.BeginTake();
            pendingNegativeRecording = false;
            recordingNegative = true;
            flow.SetRecordingLocked(true);
            transientMessage = string.Empty;
            BuildUi();
        }

        private void FinishNegativeSample()
        {
            if (!recordingNegative || capture == null || !capture.IsRecording) return;
            var take = capture.FinishTake(false);
            recordingNegative = false;
            pendingNegativeRecording = false;
            flow.SetRecordingLocked(false);
            Draft.SaveNegativeSample(take);
            transientMessage = "Optional speech sample saved. This phrase now has extra false-trigger protection.";
            BuildUi();
        }

        private void RestartMicrophoneIfNeeded()
        {
            microphoneReconnectAttempts = 0;
            microphoneStartedAt = 0d;
            if (!IsListening) return;
            capture.Dispose();
            capture = null;
            flow.SetMicrophoneReady(false);
            EnsureMicrophone();
            BuildUi();
        }

        private void HandleRerecord()
        {
            if (flow == null || flow.RecordingLocked) return;
            if (flow.CurrentStatus.IsReady)
            {
                // The review-level action means a fresh four-example
                // enrollment. Keep the saved recordings in place until each
                // replacement succeeds, while the numbered slots remain the
                // precise single-take retake controls.
                rerecordAllRequested = true;
                RouteToConcreteRetake(0);
                transientMessage = "We’ll record all four examples again.";
                BuildUi();
                return;
            }

            var takeIndex = TryGetBlockingTakeIndex(flow.CurrentStatus, out var blockingTake)
                ? blockingTake
                : Mathf.Clamp(
                    flow.TakeIndex,
                    0,
                    VisemePhraseEnrollmentStatus.RequiredTakeCount - 1);
            SelectTakeForReplacement(takeIndex);
        }

        private void SelectTakeForReplacement(int takeIndex)
        {
            if (flow == null || flow.RecordingLocked) return;
            if (takeIndex < 0 ||
                takeIndex >= VisemePhraseEnrollmentStatus.RequiredTakeCount) return;
            rerecordAllRequested = false;
            RouteToConcreteRetake(takeIndex);
            transientMessage = $"Say example {takeIndex + 1} once more.";
            BuildUi();
        }

        private void HandlePrimary()
        {
            if (flow == null || flow.RecordingLocked) return;
            switch (flow.Step)
            {
                case VisemePhraseEnrollmentStep.Phrase:
                case VisemePhraseEnrollmentStep.Microphone:
                {
                    var result = flow.TryNext();
                    if (result == VisemePhraseEnrollmentNavigationResult.Success)
                    {
                        transientMessage = string.Empty;
                        BuildUi();
                    }
                    else if (result == VisemePhraseEnrollmentNavigationResult.ValidationBlocked)
                    {
                        transientMessage = flow.Step == VisemePhraseEnrollmentStep.Microphone
                            ? "Start the mic first."
                            : flow.CurrentStatus.BlockingReason;
                        BuildUi();
                    }
                    return;
                }
                case VisemePhraseEnrollmentStep.Takes:
                {
                    var nextTake = FindNextTakeNeedingWork(Draft, flow.TakeIndex + 1);
                    if (nextTake >= 0)
                    {
                        flow.SelectTake(nextTake);
                        transientMessage = string.Empty;
                        BuildUi();
                        return;
                    }
                    var result = flow.TryNext();
                    if (result == VisemePhraseEnrollmentNavigationResult.Success)
                    {
                        transientMessage = string.Empty;
                        BuildUi();
                    }
                    else
                    {
                        transientMessage = flow.CurrentStatus.BlockingReason;
                        RebuildTakeFeedback();
                        UpdateUi();
                    }
                    return;
                }
                case VisemePhraseEnrollmentStep.Review:
                    if (!flow.CurrentStatus.IsReady)
                    {
                        HandleRerecord();
                        return;
                    }
                    if (flow.AllPhrasesReady)
                    {
                        HandleDone();
                        return;
                    }
                    var next = flow.TryNext();
                    if (next == VisemePhraseEnrollmentNavigationResult.Success ||
                        next == VisemePhraseEnrollmentNavigationResult.NoIncompletePhrase)
                    {
                        transientMessage = string.Empty;
                        BuildUi();
                    }
                    return;
            }
        }

        private void UpdateUi()
        {
            if (flow == null || Draft == null) return;
            if (recordingTake >= 0 && capture != null && capture.ShouldFinishTake)
            {
                if (capture.EndpointReason ==
                    VisemePhraseSpeechEndpointReason.WaitingTimeout)
                {
                    // Keep the hands-free session armed without saving a fake
                    // silent take or growing an unbounded pre-speech buffer.
                    capture.BeginTake();
                    return;
                }
                FinishPositiveTake(recordingTake);
                return;
            }
            if (recordingNegative && capture != null &&
                capture.RecordingDuration >= NegativeSampleSeconds)
            {
                FinishNegativeSample();
                return;
            }

            if (!IsListening &&
                !microphoneAutoStartAttempted &&
                !flow.RecordingLocked &&
                (flow.Step == VisemePhraseEnrollmentStep.Microphone ||
                 flow.Step == VisemePhraseEnrollmentStep.Takes))
            {
                EnsureMicrophone();
            }

            var listening = IsListening;
            if (!listening &&
                (recordingTake >= 0 || recordingNegative || pendingNegativeRecording))
            {
                RecoverFromAnalyzerStall();
                return;
            }
            var now = EditorApplication.timeSinceStartup;
            var frameCount = listening ? capture.AnalysisFrameCount : 0L;
            if (listening && frameCount != observedAnalysisFrameCount)
            {
                observedAnalysisFrameCount = frameCount;
                lastAnalysisFrameAt = now;
                microphoneReconnectAttempts = 0;
            }
            var analyzerProducedFrame = listening && frameCount > 0L;
            var analyzerFresh = analyzerProducedFrame &&
                                now - lastAnalysisFrameAt <= 1.5d;
            var watchdogAnchor = analyzerProducedFrame
                ? lastAnalysisFrameAt
                : microphoneStartedAt;
            var watchdogDelay = analyzerProducedFrame ? 1.5d : 4d;
            var monitorCapture = flow.Step == VisemePhraseEnrollmentStep.Microphone ||
                                 flow.Step == VisemePhraseEnrollmentStep.Takes ||
                                 recordingTake >= 0 ||
                                 recordingNegative ||
                                 pendingNegativeRecording;
            if (listening && monitorCapture && watchdogAnchor > 0d &&
                now - watchdogAnchor > watchdogDelay)
            {
                RecoverFromAnalyzerStall();
                return;
            }
            if (analyzerFresh && !flow.MicrophoneReady)
                flow.SetMicrophoneReady();

            if (pendingNegativeRecording && analyzerFresh)
            {
                BeginNegativeRecording();
                return;
            }

            if (analyzerFresh &&
                flow.Step == VisemePhraseEnrollmentStep.Microphone &&
                !flow.RecordingLocked)
            {
                flow.TryNext();
                transientMessage = string.Empty;
                BuildUi();
                return;
            }

            if (automaticResumeAt > 0d &&
                EditorApplication.timeSinceStartup >= automaticResumeAt &&
                !flow.RecordingLocked)
            {
                ResumeAutomaticEnrollment();
                return;
            }

            if (flow.Step == VisemePhraseEnrollmentStep.Takes &&
                listening &&
                analyzerFresh &&
                recordingTake < 0 &&
                !recordingNegative &&
                acceptedTake < 0 &&
                rejectedTake < 0 &&
                (!IsTakeReady(GetTake(Draft, flow.TakeIndex)) || retakeRequested))
            {
                BeginAutomaticTake(flow.TakeIndex);
                return;
            }

            if (technicalVisemeLabel != null)
                technicalVisemeLabel.text = listening
                    ? $"Detected mouth shape: {VisemeTestMath.VisemeNames[Mathf.Clamp(capture.Viseme, 0, 14)]} - Analyzer: {capture.Backend}"
                    : "Detected mouth shape: not listening";

            UpdateRecordingStage(listening, analyzerFresh);
            UpdateVisualizer(listening);

            UpdateBackendStatus(listening);
            UpdateNegativeStatus();
            UpdateFooter();
            if (assetPathLabel != null) assetPathLabel.text = Draft.AssetPath;
        }

        private void RecoverFromAnalyzerStall()
        {
            var positiveInterrupted = recordingTake >= 0;
            var negativeInterrupted = recordingNegative || pendingNegativeRecording;
            if (capture != null && capture.IsRecording) capture.CancelTake();
            recordingTake = -1;
            recordingNegative = false;
            pendingNegativeRecording = false;
            takeBeingReplaced = null;
            returnToReviewAfterRejectedReplacement = false;
            flow.SetRecordingLocked(false);
            flow.SetMicrophoneReady(false);
            capture?.Dispose();
            capture = null;
            microphoneStartedAt = 0d;
            observedAnalysisFrameCount = 0L;
            lastAnalysisFrameAt = 0d;

            if (negativeInterrupted)
            {
                microphoneAutoStartAttempted = true;
                transientMessage =
                    "The optional sample stopped because audio paused. Nothing was saved; try it again when ready.";
            }
            else if (microphoneReconnectAttempts == 0)
            {
                microphoneReconnectAttempts = 1;
                microphoneAutoStartAttempted = false;
                retakeRequested = positiveInterrupted || retakeRequested;
                transientMessage = string.Empty;
            }
            else
            {
                microphoneAutoStartAttempted = true;
                retakeRequested = positiveInterrupted || retakeRequested;
                transientMessage =
                    "No audio is arriving. Choose the microphone again or check Unity's microphone permission.";
            }
            BuildUi();
        }

        private void ResumeAutomaticEnrollment()
        {
            automaticResumeAt = 0d;
            var wasAccepted = acceptedTake >= 0;
            var completedIndex = wasAccepted ? acceptedTake : rejectedTake;
            acceptedTake = -1;
            rejectedTake = -1;

            if (!wasAccepted)
            {
                if (returnToReviewAfterRejectedReplacement)
                {
                    returnToReviewAfterRejectedReplacement = false;
                    retakeRequested = false;
                    flow.RouteToRecommendedStep();
                    transientMessage =
                        "We kept your previous example. Choose it again whenever you want another try.";
                    BuildUi();
                    return;
                }
                retakeRequested = true;
                transientMessage = string.Empty;
                BuildUi();
                return;
            }

            if (rerecordAllRequested &&
                completedIndex + 1 < VisemePhraseEnrollmentStatus.RequiredTakeCount)
            {
                flow.SelectTake(completedIndex + 1);
                retakeRequested = true;
                transientMessage = string.Empty;
                BuildUi();
                return;
            }
            if (rerecordAllRequested)
                rerecordAllRequested = false;

            var nextTake = FindNextTakeNeedingWork(Draft, completedIndex + 1);
            if (nextTake >= 0)
            {
                flow.SelectTake(nextTake);
                retakeRequested = false;
                transientMessage = string.Empty;
                BuildUi();
                return;
            }

            var next = flow.TryNext();
            if (next == VisemePhraseEnrollmentNavigationResult.Success)
            {
                if (flow.Step == VisemePhraseEnrollmentStep.Review &&
                    !flow.CurrentStatus.IsReady)
                {
                    if (TryGetBlockingTakeIndex(
                            flow.CurrentStatus,
                            out var blockingTake))
                    {
                        RouteToConcreteRetake(blockingTake);
                        transientMessage =
                            "One example needs another try.";
                    }
                    else
                    {
                        // A phrase-level compiler issue is not evidence that the
                        // last take was wrong. Keep the truthful review instead
                        // of trapping the creator in arbitrary automatic retakes.
                        transientMessage = string.Empty;
                    }
                    BuildUi();
                    return;
                }
                if (flow.Step == VisemePhraseEnrollmentStep.Review &&
                    flow.CurrentStatus.IsReady &&
                    !flow.AllPhrasesReady)
                {
                    flow.TryNext();
                }
                transientMessage = string.Empty;
                BuildUi();
                return;
            }

            // Route only when validation identified a concrete take. A global
            // compiler issue belongs on the review page and must not blame the
            // most recently recorded example by default.
            if (TryGetBlockingTakeIndex(flow.CurrentStatus, out var diagnosedTake))
            {
                RouteToConcreteRetake(diagnosedTake);
                retakeRequested = true;
                transientMessage = "One example needs another try.";
            }
            else
            {
                flow.RouteToRecommendedStep();
                transientMessage = string.Empty;
            }
            BuildUi();
        }

        private void RouteToConcreteRetake(int takeIndex)
        {
            flow.SelectTake(Mathf.Clamp(
                takeIndex,
                0,
                VisemePhraseEnrollmentStatus.RequiredTakeCount - 1));
            retakeRequested = true;
            acceptedTake = -1;
            rejectedTake = -1;
            automaticResumeAt = 0d;
            returnToReviewAfterRejectedReplacement = false;
        }

        private void UpdateRecordingStage(bool listening, bool analyzerProducedFrame)
        {
            if (recordingStateLabel == null) return;

            var stateClass = "idle";
            string text;
            if (!listening)
            {
                stateClass = microphoneAutoStartAttempted ? "error" : "starting";
                text = microphoneAutoStartAttempted
                    ? "Microphone unavailable"
                    : "Starting microphone…";
            }
            else if (!analyzerProducedFrame)
            {
                stateClass = "starting";
                text = "Connecting…";
            }
            else if (acceptedTake >= 0)
            {
                stateClass = "success";
                text = "Saved";
            }
            else if (rejectedTake >= 0)
            {
                stateClass = "retry";
                text = "Let’s try that once more";
            }
            else if (recordingTake < 0)
            {
                stateClass = "starting";
                text = "Getting ready…";
            }
            else
            {
                switch (capture.EndpointState)
                {
                    case VisemePhraseSpeechEndpointState.ConfirmingOnset:
                        stateClass = "hearing";
                        text = "I hear you…";
                        break;
                    case VisemePhraseSpeechEndpointState.Speaking:
                        stateClass = "speaking";
                        text = "Keep going";
                        break;
                    case VisemePhraseSpeechEndpointState.EndingSilence:
                        stateClass = "finishing";
                        text = "Finishing…";
                        break;
                    case VisemePhraseSpeechEndpointState.Complete:
                        stateClass = "finishing";
                        text = "Checking…";
                        break;
                    default:
                        stateClass = "listening";
                        text = "Listening — start when you’re ready";
                        break;
                }
            }

            ApplyStageClass(stateClass);
            SetAnimatedStageStatus(text);

            if (stageTimerLabel != null)
            {
                var showTimer = recordingTake >= 0 &&
                                capture != null &&
                                capture.HasConfirmedSpeech;
                stageTimerLabel.style.display = showTimer
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                stageTimerLabel.text = showTimer
                    ? $"{capture.SpeechDuration:0.0}s"
                    : string.Empty;
            }

            if (stageActionButton != null)
            {
                var showRetry = !listening && microphoneAutoStartAttempted;
                stageActionButton.style.display = showRetry
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                stageActionButton.SetEnabled(showRetry && !flow.RecordingLocked);
            }
        }

        private void ApplyStageClass(string state)
        {
            if (recordingStage == null) return;
            var states = new[]
            {
                "idle", "starting", "listening", "hearing", "speaking",
                "finishing", "success", "retry", "error"
            };
            foreach (var candidate in states)
                recordingStage.RemoveFromClassList("yucp-phrase-stage-" + candidate);
            recordingStage.AddToClassList("yucp-phrase-stage-" + state);
        }

        private void SetAnimatedStageStatus(string text)
        {
            if (recordingStateLabel == null ||
                string.Equals(text, visibleStageStatus, StringComparison.Ordinal)) return;
            visibleStageStatus = text;
            recordingStateLabel.style.opacity = 0f;
            var label = recordingStateLabel;
            label.schedule.Execute(() =>
            {
                if (label.panel == null) return;
                label.text = text;
                label.style.opacity = 1f;
            }).ExecuteLater(70);
        }

        private void UpdateVisualizer(bool listening)
        {
            if (visualizerBars.Count == 0) return;
            var now = EditorApplication.timeSinceStartup;
            var delta = lastVisualizerUpdate <= 0d
                ? 1f / 30f
                : Mathf.Clamp((float)(now - lastVisualizerUpdate), 0.001f, 0.1f);
            lastVisualizerUpdate = now;
            var target = listening && capture != null ? Mathf.Clamp01(capture.Voice) : 0f;
            var response = target > visualizerLevel ? 18f : 8f;
            visualizerLevel = Mathf.Lerp(
                visualizerLevel,
                target,
                1f - Mathf.Exp(-response * delta));

            var phase = (float)now;
            var speaking = recordingTake >= 0 && capture != null &&
                           (capture.EndpointState == VisemePhraseSpeechEndpointState.Speaking ||
                            capture.EndpointState == VisemePhraseSpeechEndpointState.ConfirmingOnset);
            var processing = recordingTake >= 0 && capture != null &&
                             capture.EndpointState == VisemePhraseSpeechEndpointState.EndingSilence;
            for (var i = 0; i < visualizerBars.Count; i++)
            {
                var centerDistance = Mathf.Abs(i - (VisualizerBarCount - 1) * 0.5f) /
                                     (VisualizerBarCount * 0.5f);
                float energy;
                if (!listening)
                {
                    energy = 0.04f;
                }
                else if (acceptedTake >= 0)
                {
                    energy = 0.18f + 0.12f * (1f - centerDistance);
                }
                else if (processing)
                {
                    energy = 0.12f + 0.32f *
                        (0.5f + 0.5f * Mathf.Sin(phase * 8f - i * 0.72f));
                }
                else if (speaking)
                {
                    var texture = 0.45f + 0.55f *
                        Mathf.Abs(Mathf.Sin(i * 1.37f + phase * 7.5f));
                    energy = Mathf.Clamp01(visualizerLevel * 2.1f) * texture;
                }
                else
                {
                    var pulse = 0.5f + 0.5f * Mathf.Sin(phase * 3.2f - i * 0.38f);
                    energy = 0.05f + 0.1f * pulse * (1f - centerDistance * 0.55f);
                }

                visualizerBars[i].style.height = 4f + energy * 48f;
            }
        }

        private void UpdateBackendStatus(bool listening)
        {
            if (backendStatusHost == null) return;
            var exact = listening
                ? string.Equals(capture.Backend, "Oculus LipSync", StringComparison.OrdinalIgnoreCase)
                : VisemeTestPreviewSession.OculusBridge.IsAvailable(out _);
            if (exact)
            {
                if (!string.Equals(lastBackendStatus, "exact", StringComparison.Ordinal))
                {
                    lastBackendStatus = "exact";
                    backendStatusHost.Clear();
                }
                return;
            }

            const string message =
                "Approximate matching · Oculus LipSync gives the closest VRChat result";
            var key = "fallback|" + message;
            if (string.Equals(key, lastBackendStatus, StringComparison.Ordinal)) return;
            lastBackendStatus = key;
            backendStatusHost.Clear();
            var label = new Label(message);
            label.AddToClassList("yucp-phrase-inline-note");
            backendStatusHost.Add(label);
        }

        private void UpdateNegativeStatus()
        {
            if (negativeStatus == null) return;
            var negative = Draft.NegativeSample;
            negativeStatus.text = pendingNegativeRecording
                ? "Starting microphone…"
                : recordingNegative
                ? $"Recording ordinary speech - {Math.Max(0d, NegativeSampleSeconds - capture.RecordingDuration):0.0} seconds left"
                : negative == null
                    ? "No optional sample recorded. Skipping is safe."
                    : $"Saved - {negative.durationSeconds:0.0} seconds";
            if (negativeButton != null)
            {
                negativeButton.text = recordingNegative || pendingNegativeRecording
                    ? "Cancel recording"
                    : negative == null
                        ? "Record 15-second sample"
                        : "Record it again";
                negativeButton.SetEnabled(recordingTake < 0);
            }
            if (clearNegativeButton != null)
                clearNegativeButton.SetEnabled(
                    !recordingNegative && !pendingNegativeRecording && negative != null);
        }

        private void UpdateFooter()
        {
            if (backButton != null)
            {
                backButton.SetEnabled(!flow.RecordingLocked);
            }
            if (primaryButton != null)
            {
                primaryButton.text = PrimaryActionText();
                primaryButton.SetEnabled(CanUsePrimaryAction());
            }
        }

        private string PrimaryActionText()
        {
            if (flow == null) return "Continue";
            switch (flow.Step)
            {
                case VisemePhraseEnrollmentStep.Phrase:
                    return "Continue";
                case VisemePhraseEnrollmentStep.Microphone:
                    return flow.MicrophoneReady ? "Continue" : "Waiting for mic";
                case VisemePhraseEnrollmentStep.Takes:
                {
                    var current = GetTake(Draft, flow.TakeIndex);
                    if (!IsTakeReady(current)) return "Record this take";
                    return FindNextTakeNeedingWork(Draft, flow.TakeIndex + 1) >= 0
                        ? "Next take"
                        : "Review";
                }
                case VisemePhraseEnrollmentStep.Review:
                    if (!flow.CurrentStatus.IsReady) return "Re-record";
                    if (flow.AllPhrasesReady)
                        return HasPlayModeResume ? "Continue to Play" : "Done";
                    return "Next phrase";
                default:
                    return "Continue";
            }
        }

        private bool CanUsePrimaryAction()
        {
            if (flow == null || flow.RecordingLocked) return false;
            switch (flow.Step)
            {
                case VisemePhraseEnrollmentStep.Phrase:
                    return !string.IsNullOrWhiteSpace(Draft.Prompt);
                case VisemePhraseEnrollmentStep.Microphone:
                    return flow.MicrophoneReady && IsListening;
                case VisemePhraseEnrollmentStep.Takes:
                    return IsTakeReady(GetTake(Draft, flow.TakeIndex));
                case VisemePhraseEnrollmentStep.Review:
                    return true;
                default:
                    return false;
            }
        }

        private void RebuildTransientStatus()
        {
            if (statusHost == null) return;
            statusHost.Clear();
            if (!string.IsNullOrWhiteSpace(transientMessage))
                statusHost.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    transientMessage,
                    transientMessage.IndexOf("couldn't", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    transientMessage.IndexOf("not", StringComparison.OrdinalIgnoreCase) >= 0
                        ? YUCPUIToolkitHelper.MessageType.Warning
                        : YUCPUIToolkitHelper.MessageType.Info));
        }

        private void RebuildTakeFeedback()
        {
            if (statusHost == null) return;
            statusHost.Clear();
            var take = GetTake(Draft, flow.TakeIndex);
            if (recordingTake >= 0) return;
            if (take == null)
                return;

            var runs = VisemePhraseEnrollmentStatus.InformativeRuns(take);
            if (!IsTakeReady(take))
            {
                statusHost.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    FriendlyRetakeMessage(take, runs),
                    YUCPUIToolkitHelper.MessageType.Warning,
                    "Let's try that take again"));
                return;
            }

            if (AllTakesIndividuallyReady(Draft) && flow.CurrentStatus.HasBlockingIssues)
                statusHost.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    flow.CurrentStatus.BlockingReason,
                    YUCPUIToolkitHelper.MessageType.Warning,
                    "Try one take again"));
        }

        private void RebuildReviewStatus()
        {
            if (statusHost == null) return;
            statusHost.Clear();
            if (!string.IsNullOrWhiteSpace(transientMessage))
                statusHost.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    transientMessage,
                    YUCPUIToolkitHelper.MessageType.Info));
        }

        private static string FriendlyMicrophoneError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return "Unity couldn't start this microphone. Choose another input or check microphone permission.";
            if (error.IndexOf("device", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("microphone", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Unity couldn't access this microphone. Choose another input, reconnect it, or check microphone permission. " + error;
            return error;
        }

        private static string FriendlyRetakeMessage(VisemePhraseCapturedTake take, int runs)
        {
            if (take == null || !take.IsUseful())
                return "Too quiet. Move closer and try again.";
            if (runs < VisemePhraseEnrollmentStatus.MinimumInformativeRuns)
                return "Say the whole phrase once, a little more slowly.";
            return "Say it again at your normal pace.";
        }

        private static string FriendlyIssue(VisemePhraseEnrollmentIssue issue)
        {
            if (issue.target == VisemePhraseEnrollmentIssueTarget.PositiveTake && issue.takeIndex >= 0)
                return $"Take {issue.takeIndex + 1}: {issue.message}";
            return issue.message;
        }

        private static string FriendlyReviewIssue(VisemePhraseEnrollmentIssue issue)
        {
            if (string.IsNullOrWhiteSpace(issue.message))
                return "One example needs another try.";
            if (issue.target == VisemePhraseEnrollmentIssueTarget.PositiveTake &&
                issue.takeIndex >= 0)
                return $"Take {issue.takeIndex + 1} needs another try.";
            if (issue.target == VisemePhraseEnrollmentIssueTarget.Compilation)
                return "This phrase does not have a safe visual pattern yet.";
            return FriendlyIssue(issue);
        }

        private static bool TryGetBlockingTakeIndex(
            VisemePhraseEnrollmentStatus status,
            out int takeIndex)
        {
            takeIndex = -1;
            if (status == null) return false;
            var found = status.issues
                .Where(issue =>
                    issue.level == VisemePhraseEnrollmentIssueLevel.Blocking &&
                    issue.target == VisemePhraseEnrollmentIssueTarget.PositiveTake &&
                    issue.takeIndex >= 0)
                .Select(issue => (int?)issue.takeIndex)
                .FirstOrDefault();
            if (!found.HasValue) return false;
            takeIndex = found.Value;
            return true;
        }

        private static string DisplayPrompt(IVisemePhraseEnrollmentDraft candidate)
        {
            return string.IsNullOrWhiteSpace(candidate?.Prompt)
                ? "Unconfigured phrase"
                : candidate.Prompt.Trim();
        }

        private static bool IsTakeReady(VisemePhraseCapturedTake take)
        {
            return take != null && take.IsUseful() &&
                   VisemePhraseEnrollmentStatus.InformativeRuns(take) >=
                   VisemePhraseEnrollmentStatus.MinimumInformativeRuns;
        }

        private static bool AllTakesIndividuallyReady(IVisemePhraseEnrollmentDraft candidate)
        {
            for (var i = 0; i < VisemePhraseEnrollmentStatus.RequiredTakeCount; i++)
                if (!IsTakeReady(GetTake(candidate, i))) return false;
            return true;
        }

        private static int CountReadyTakes(IVisemePhraseEnrollmentDraft candidate)
        {
            var ready = 0;
            for (var i = 0; i < VisemePhraseEnrollmentStatus.RequiredTakeCount; i++)
                if (IsTakeReady(GetTake(candidate, i))) ready++;
            return ready;
        }

        private static int FindNextTakeNeedingWork(
            IVisemePhraseEnrollmentDraft candidate,
            int startIndex)
        {
            for (var offset = 0; offset < VisemePhraseEnrollmentStatus.RequiredTakeCount; offset++)
            {
                var index = (Mathf.Max(0, startIndex) + offset) %
                            VisemePhraseEnrollmentStatus.RequiredTakeCount;
                if (!IsTakeReady(GetTake(candidate, index))) return index;
            }
            return -1;
        }

        private static VisemePhraseCapturedTake GetTake(
            IVisemePhraseEnrollmentDraft candidate,
            int index)
        {
            if (candidate?.Takes == null || index < 0 || index >= candidate.Takes.Count)
                return null;
            return candidate.Takes[index];
        }

        private static VisemePhraseEnrollmentStatus StatusFor(
            IVisemePhraseEnrollmentDraft candidate)
        {
            if (candidate is VisemePhraseEnrollmentDraft stored)
                return VisemePhraseEnrollmentStatus.Evaluate(stored.Component, stored.Phrase);
            return VisemePhraseEnrollmentStatus.Evaluate(
                candidate?.Prompt,
                candidate?.Takes,
                candidate?.NegativeSample);
        }

        private static GameObject ResolveAvatarRoot(GameObject source)
        {
            if (source == null) return null;
            var descriptor = source.GetComponentInParent<
                VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            return descriptor != null
                ? descriptor.gameObject
                : source.transform.root.gameObject;
        }

        private bool IsListening => capture != null && capture.IsRunning;
    }
}
