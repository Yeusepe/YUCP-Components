using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.Components.Editor.MeshUtils;
using YUCP.UI.DesignSystem.Utilities;
using Toolkit = YUCP.UI.DesignSystem.Utilities.YUCPUIToolkitHelper;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Simple tab: the setup and the friendly speech sliders. Advanced tab: the profile, per-viseme
    /// trims and rig tools. Each setting appears once, under one label (DESIGN.md).
    /// </summary>
    [CustomEditor(typeof(AdvancedVisemeReconstructorData))]
    public sealed class AdvancedVisemeReconstructorDataEditor : UnityEditor.Editor
    {
        private const float MinimumVisemeResponseSeconds = 0.006f;
        private const float MaximumVisemeResponseSeconds = 0.12f;
        private const float DefaultVisemeResponseSeconds = 0.017f;
        private const float MinimumSpeechHangoverSeconds = 0.04f;
        private const float MaximumSpeechHangoverSeconds = 0.4f;
        private const float DefaultSpeechHangoverSeconds = 0.16f;

        private static readonly AdvancedVisemeTrackingInputs[] SimpleTrackingModes =
        {
            AdvancedVisemeTrackingInputs.Auto,
            AdvancedVisemeTrackingInputs.Disabled,
            AdvancedVisemeTrackingInputs.ReuseExisting,
            AdvancedVisemeTrackingInputs.Balanced8,
            AdvancedVisemeTrackingInputs.Quality12,
            AdvancedVisemeTrackingInputs.FullTongue18
        };

        private static readonly List<string> SimpleTrackingLabels = new List<string>
        {
            "Automatic", "Off", "Existing Setup", "Compact Inputs", "Detailed Inputs", "Full Tongue Inputs"
        };

        private static readonly string[] FineTuneVisemeLabels =
        {
            "Silence", "P / B / M", "F / V", "TH", "T / D", "K / G", "CH / J", "S / Z",
            "N / L", "R", "A", "E", "I", "O", "U"
        };

        private static readonly string[] FineTuneJawFields = { "jawOpen", "mouthOpen" };
        private static readonly string[] FineTuneLipFields =
        {
            "lipClose", "lipFunnel", "lipPucker", "lipSuck", "smileSad", "lipBite"
        };
        private static readonly string[] FineTuneTongueFields =
        {
            "tongueOut", "tongueY", "tongueX", "tongueRoll", "tongueArchY", "tongueShape",
            "tongueTwistRight", "tongueTwistLeft"
        };
        private static readonly string[] FineTuneAllFields =
        {
            "jawOpen", "lipClose", "mouthOpen", "lipFunnel", "lipPucker", "lipSuck",
            "smileSad", "lipBite", "tongueOut", "jawX", "jawZ", "mouthX", "tongueY",
            "tongueX", "tongueRoll", "tongueArchY", "tongueShape", "tongueTwistRight",
            "tongueTwistLeft"
        };
        private static readonly string[] PoseMouthFields =
        {
            "jawOpen", "lipClose", "mouthOpen", "lipFunnel", "lipPucker", "lipSuck",
            "smileSad", "lipBite", "jawX", "jawZ", "mouthX"
        };

        private AdvancedVisemeReconstructorData data;
        private YUCPInspector ui;
        private VisemeReconstructionProfile displayedProfile;
        private SerializedObject profileSerializedObject;
        // Refreshers for the profile UI; replaced whenever that UI is rebuilt for another profile.
        private readonly List<Action> profileRefreshers = new List<Action>();
        private VisualElement motionHost;
        private VisualElement fineTuneHost;
        private VisualElement rigHost;
        private string fitError;
        private int selectedVisemeIndex;
        private int selectedFineTuneVisemeIndex;
        private int selectedBindingIndex;

        private string VisemeKey => $"YUCP_AVR_Viseme_{data.GetInstanceID()}";
        private string FineTuneKey => $"YUCP_AVR_FineTuneViseme_{data.GetInstanceID()}";
        private string BindingKey => $"YUCP_AVR_Articulator_{data.GetInstanceID()}";

        private bool TrackingOn => data.trackingInputs != AdvancedVisemeTrackingInputs.Disabled;
        private bool CreatesInputs => data.trackingInputs == AdvancedVisemeTrackingInputs.Balanced8 ||
                                      data.trackingInputs == AdvancedVisemeTrackingInputs.Quality12 ||
                                      data.trackingInputs == AdvancedVisemeTrackingInputs.FullTongue18;
        private bool NaturalTransitions => data.reconstructionMode == AdvancedVisemeReconstructionMode.BetaCoarticulation;

        private void OnEnable()
        {
            data = (AdvancedVisemeReconstructorData)target;
            if (data == null) return;
            selectedVisemeIndex = SessionState.GetInt(VisemeKey, 0);
            selectedFineTuneVisemeIndex = SessionState.GetInt(FineTuneKey, 0);
            selectedBindingIndex = SessionState.GetInt(BindingKey, 0);
        }

        public override VisualElement CreateInspectorGUI()
        {
            ui = YUCPInspector.Create(this, "Advanced Viseme Reconstructor");
            ui.Status(Problems);
            ui.OnRefresh(RefreshProfile);
            displayedProfile = null;
            profileSerializedObject = null;

            var tabs = ui.Tabs("mode", "Simple", "Advanced");
            tabs.Buttons[0].name = "simple-mode-tab";
            tabs.Buttons[1].name = "advanced-mode-tab";
            tabs.Pages[0].name = "simple-mode-ui";
            tabs.Pages[1].name = "advanced-mode-ui";
            // The view is per component, and never an avatar setting.
            var modeKey = InspectorModeSessionKey(data.GetInstanceID());
            tabs.Select(Mathf.Clamp(SessionState.GetInt(modeKey, 0), 0, 1));
            tabs.Changed += i => SessionState.SetInt(modeKey, i);

            BuildSimpleTab(tabs.Pages[0]);
            BuildAdvancedTab(tabs.Pages[1]);
            BuildAvatarMenu();

            var advanced = ui.Advanced();
            advanced.GetFirstAncestorOfType<Foldout>().name = "expert-settings";
            ui.Field(advanced, "verboseLogging");
            var reset = ui.Button(advanced, "Reset Profile", ResetProfile, Toolkit.ButtonVariant.Danger,
                "Replaces all tuning, viseme poses and rig mappings in the profile with the defaults.");
            reset.name = "reset-profile";
            ui.ShowWhen(reset, () => data.profile != null);
            ui.BuildSummary(data.GetBuildSummary);

            RebuildProfileUI();
            ui.Refresh();
            return ui.Root;
        }

        internal static string InspectorModeSessionKey(int instanceId)
        {
            return $"YUCP_AVR_UIMode_{instanceId}";
        }

        // ---- Friendly slider mappings (each default sits at 50%) ------------------------------------

        internal static float ReactionSpeedFromSeconds(float seconds)
        {
            var clamped = Mathf.Clamp(seconds,
                MinimumVisemeResponseSeconds, MaximumVisemeResponseSeconds);
            if (clamped >= DefaultVisemeResponseSeconds)
                return 0.5f * Mathf.Log(
                    MaximumVisemeResponseSeconds / clamped) /
                    Mathf.Log(MaximumVisemeResponseSeconds /
                              DefaultVisemeResponseSeconds);
            return 0.5f + 0.5f * Mathf.Log(
                DefaultVisemeResponseSeconds / clamped) /
                Mathf.Log(DefaultVisemeResponseSeconds /
                          MinimumVisemeResponseSeconds);
        }

        internal static float SecondsFromReactionSpeed(float reactionSpeed)
        {
            reactionSpeed = Mathf.Clamp01(reactionSpeed);
            if (reactionSpeed <= 0.5f)
                return MaximumVisemeResponseSeconds * Mathf.Pow(
                    DefaultVisemeResponseSeconds /
                    MaximumVisemeResponseSeconds,
                    reactionSpeed * 2f);
            return DefaultVisemeResponseSeconds * Mathf.Pow(
                MinimumVisemeResponseSeconds /
                DefaultVisemeResponseSeconds,
                (reactionSpeed - 0.5f) * 2f);
        }

        internal static float PauseStabilityFromSeconds(float seconds)
        {
            var clamped = Mathf.Clamp(seconds,
                MinimumSpeechHangoverSeconds, MaximumSpeechHangoverSeconds);
            if (clamped <= DefaultSpeechHangoverSeconds)
                return 0.5f * Mathf.Log(
                    clamped / MinimumSpeechHangoverSeconds) /
                    Mathf.Log(DefaultSpeechHangoverSeconds /
                              MinimumSpeechHangoverSeconds);
            return 0.5f + 0.5f * Mathf.Log(
                clamped / DefaultSpeechHangoverSeconds) /
                Mathf.Log(MaximumSpeechHangoverSeconds /
                          DefaultSpeechHangoverSeconds);
        }

        internal static float SecondsFromPauseStability(float pauseStability)
        {
            pauseStability = Mathf.Clamp01(pauseStability);
            if (pauseStability <= 0.5f)
                return MinimumSpeechHangoverSeconds * Mathf.Pow(
                    DefaultSpeechHangoverSeconds /
                    MinimumSpeechHangoverSeconds,
                    pauseStability * 2f);
            return DefaultSpeechHangoverSeconds * Mathf.Pow(
                MaximumSpeechHangoverSeconds /
                DefaultSpeechHangoverSeconds,
                (pauseStability - 0.5f) * 2f);
        }

        internal static float CenteredLinearToSlider(
            float value, float minimum, float configured, float maximum)
        {
            value = Mathf.Clamp(value, minimum, maximum);
            if (value <= configured)
                return Mathf.Approximately(configured, minimum)
                    ? 0.5f
                    : 0.5f * Mathf.InverseLerp(minimum, configured, value);
            return Mathf.Approximately(maximum, configured)
                ? 0.5f
                : 0.5f + 0.5f * Mathf.InverseLerp(
                    configured, maximum, value);
        }

        internal static float SliderToCenteredLinear(
            float slider, float minimum, float configured, float maximum)
        {
            slider = Mathf.Clamp01(slider);
            return slider <= 0.5f
                ? Mathf.Lerp(minimum, configured, slider * 2f)
                : Mathf.Lerp(configured, maximum, (slider - 0.5f) * 2f);
        }

        // ---- Status ---------------------------------------------------------------------------------

        private IEnumerable<YUCPInspector.Message> Problems()
        {
            if (Descriptor() == null)
            {
                yield return YUCPInspector.Message.Error("Put this inside an avatar (under a VRC Avatar Descriptor).");
                yield break;
            }
            var renderer = EffectiveRenderer();
            if (data.mouthOwnership == AdvancedVisemeMouthOwnership.DriveLowerFace &&
                (renderer == null || renderer.sharedMesh == null))
                yield return YUCPInspector.Message.Error(
                    "To drive the lower face, assign the face renderer or set the avatar's viseme mesh.");
        }

        // ---- Simple tab -----------------------------------------------------------------------------

        private void BuildSimpleTab(VisualElement page)
        {
            var setup = ui.Section(page, "Setup");
            ui.Field(setup, "faceRenderer", "Face Renderer");
            var tracking = Align(new PopupField<string>("Face Tracking", SimpleTrackingLabels, 0)
            {
                name = "simple-face-tracking",
                tooltip = serializedObject.FindProperty("trackingInputs").tooltip
            });
            tracking.RegisterValueChangedCallback(_ =>
                SetEnum("trackingInputs", (int)SimpleTrackingModes[tracking.index]));
            void SyncTracking()
            {
                var index = Mathf.Max(0, Array.IndexOf(SimpleTrackingModes, data.trackingInputs));
                if (tracking.index != index) tracking.SetValueWithoutNotify(SimpleTrackingLabels[index]);
            }
            ui.OnRefresh(SyncTracking);
            SyncTracking();
            setup.Add(tracking);
            ui.Caption(setup, TrackingBudget).name = "tracking-budget";
            EnumToggle(setup, "simple-natural-transitions", "Natural Transitions", "reconstructionMode",
                (int)AdvancedVisemeReconstructionMode.BetaCoarticulation, (int)AdvancedVisemeReconstructionMode.Normal);

            var feel = ui.Section(page, "Speech feel");
            SimpleSlider(feel, "simple-speech-movement", "Exaggeration", "speechMotionStrength", 1f,
                v => CenteredLinearToSlider(v, 0f, 1f, 2f), v => SliderToCenteredLinear(v, 0f, 1f, 2f),
                "Makes small and quiet mouth movements bigger, up to the full viseme shape.");
            SimpleSlider(feel, "simple-speech-liveliness", "Snappiness", "speechLiveliness", 0f,
                v => CenteredLinearToSlider(v, -1f, 0f, 1f), v => SliderToCenteredLinear(v, -1f, 0f, 1f),
                "Lower is soft and flowing, higher is sharp and immediate. Face tracking isn't affected.");
            SimpleSlider(feel, "simple-quiet-speech", "Soft Speech", "quietSpeechFloor", 0.55f,
                v => CenteredLinearToSlider(v, 0f, 0.55f, 1f), v => SliderToCenteredLinear(v, 0f, 0.55f, 1f),
                "Keeps mouth shapes visible when speaking softly. The microphone threshold doesn't change.");
            SimpleSlider(feel, "simple-reaction-speed", "Reaction Speed", "visemeResponseSeconds", DefaultVisemeResponseSeconds,
                ReactionSpeedFromSeconds, SecondsFromReactionSpeed,
                "How quickly the mouth reaches each new sound. Faster is crisper, slower is softer.");
            SimpleSlider(feel, "simple-pause-stability", "Smooth Pauses", "speechHangoverSeconds", DefaultSpeechHangoverSeconds,
                PauseStabilityFromSeconds, SecondsFromPauseStability,
                "Bridges short gaps between words. Higher reduces twitching but holds the last shape longer.");

            var face = ui.Section(page, "Face tracking");
            ui.ShowWhen(face.parent, () => TrackingOn);
            EnumToggle(face, "simple-keep-speech-clear", "Keep Speech Shapes Clear", "fusionMode",
                (int)AdvancedVisemeFusionMode.PhoneticAssist, (int)AdvancedVisemeFusionMode.TrackerAuthoritative);
            var assist = ui.Indent(face);
            SimpleSlider(assist, "simple-pronunciation-help", "Clear Consonants", "phoneticConstraintStrength", 1f,
                v => CenteredLinearToSlider(v, 0f, 1f, 2f), v => SliderToCenteredLinear(v, 0f, 1f, 2f),
                "How strongly speech corrects closures and sharp consonants that face tracking misses.");
            ui.ShowWhen(assist, () => data.fusionMode == AdvancedVisemeFusionMode.PhoneticAssist);
            SimpleSlider(face, "simple-face-tracking-priority", "Follow My Face", "residualMismatchFade", 1f,
                v => CenteredLinearToSlider(v, 0f, 1f, 2f), v => SliderToCenteredLinear(v, 0f, 1f, 2f),
                "How strongly tracked face movement replaces the matching viseme motion. Untracked detail stays.");
            ui.ShowWhen(SimpleSlider(face, "simple-tongue-motion", "Tongue Motion", "tongueInferenceStrength", 1f,
                v => CenteredLinearToSlider(v, 0f, 1f, 2f), v => SliderToCenteredLinear(v, 0f, 1f, 2f),
                "How much tongue motion is inferred without tongue tracking. Used while Natural Transitions is on."),
                () => NaturalTransitions);
        }

        private string TrackingBudget()
        {
            switch (data.trackingInputs)
            {
                case AdvancedVisemeTrackingInputs.Balanced8:
                case AdvancedVisemeTrackingInputs.Quality12:
                case AdvancedVisemeTrackingInputs.FullTongue18:
                    return $"Uses up to {DisplayedTrackingBits()} synced bits.";
                case AdvancedVisemeTrackingInputs.ReuseExisting:
                    return "Uses the avatar's existing face tracking parameters.";
                case AdvancedVisemeTrackingInputs.Auto:
                    return "Uses the avatar's face tracking if it has a compatible setup; otherwise speech only.";
                default:
                    return "Speech only, with no synced parameters.";
            }
        }

        private int DisplayedTrackingBits()
        {
            var bits = AdvancedVisemeMath.TrackingParameterBits(data.trackingInputs, data.trackingEncoding);
            return data.createFaceTrackingToggle ? bits : Mathf.Max(0, bits - 1);
        }

        /// <summary>A 0-1 slider over one profile value. The first edit creates the profile.</summary>
        private Slider SimpleSlider(VisualElement parent, string name, string label, string propertyName,
            float fallback, Func<float, float> toSlider, Func<float, float> fromSlider, string tooltip)
        {
            var slider = Align(new Slider(label, 0f, 1f) { name = name, tooltip = tooltip });
            slider.RegisterValueChangedCallback(e => ApplySimpleProfileValue(propertyName, e.newValue, fromSlider));
            void Sync()
            {
                var property = profileSerializedObject?.FindProperty(propertyName);
                var value = Mathf.Clamp01(toSlider(property != null ? property.floatValue : fallback));
                if (Mathf.Abs(slider.value - value) > 1e-5f) slider.SetValueWithoutNotify(value);
            }
            ui.OnRefresh(Sync);
            Sync();
            parent.Add(slider);
            return slider;
        }

        internal bool ApplySimpleProfileValue(
            string propertyName,
            float sliderValue,
            Func<float, float> fromSimpleValue)
        {
            if (fromSimpleValue == null || !EnsureProfile()) return false;
            profileSerializedObject.UpdateIfRequiredOrScript();
            var property = profileSerializedObject.FindProperty(propertyName);
            if (property == null) return false;
            property.floatValue = fromSimpleValue(Mathf.Clamp01(sliderValue));
            profileSerializedObject.ApplyModifiedProperties();
            return true;
        }

        /// <summary>A checkbox over a two-value enum.</summary>
        private void EnumToggle(VisualElement parent, string name, string label, string path, int on, int off)
        {
            var toggle = YUCPInspector.Checkbox(label, serializedObject.FindProperty(path).tooltip);
            toggle.name = name;
            toggle.RegisterValueChangedCallback(e => SetEnum(path, e.newValue ? on : off));
            void Sync() => toggle.SetValueWithoutNotify(serializedObject.FindProperty(path).enumValueIndex == on);
            ui.OnRefresh(Sync);
            Sync();
            parent.Add(toggle);
        }

        private void SetEnum(string path, int value)
        {
            serializedObject.Update();
            var property = serializedObject.FindProperty(path);
            if (property.enumValueIndex == value) return;
            property.enumValueIndex = value;
            serializedObject.ApplyModifiedProperties();
        }

        private static T Align<T>(T field) where T : VisualElement
        {
            field.AddToClassList("yucp-field-input");
            field.AddToClassList(BaseField<bool>.alignedFieldUssClassName);
            return field;
        }

        // ---- Advanced tab ---------------------------------------------------------------------------

        private void BuildAdvancedTab(VisualElement page)
        {
            var reconstruction = ui.Section(page, "Reconstruction");
            ui.Segmented(reconstruction, "mouthOwnership", "Output", "Lower Face", "Parameters Only");
            ui.Field(reconstruction, "profile", "Profile");
            var motion = ui.Disclosure(reconstruction, "Motion Tuning", "motion-tuning");
            motion.name = "motion-tuning";
            motionHost = new VisualElement();
            motion.Add(motionHost);

            fineTuneHost = ui.Section(page, "Fine-tune visemes");
            fineTuneHost.parent.name = "fine-tune-visemes";

            var rig = ui.Section(page, "Rig and calibration");
            ui.Caption(rig, MappingCoverage).name = "mapping-coverage";
            var mapping = ui.ButtonRow(rig);
            ui.ShowWhen(mapping, () => data.profile != null);
            var map = ui.Button(mapping, "Map Missing", () => ApplyAutoMap(false), Toolkit.ButtonVariant.Primary,
                "Finds a blendshape for each articulator that has none.");
            map.name = "auto-map-rig";
            ui.EnableWhen(map, CanUseRigTools);
            var remap = ui.Button(mapping, "Remap All", () => ApplyAutoMap(true), Toolkit.ButtonVariant.Danger,
                "Replaces every articulator's blendshape with the automatic match, where there is one.");
            remap.name = "remap-all";
            ui.EnableWhen(remap, CanUseRigTools);

            var checks = ui.ButtonRow(rig);
            var analyze = ui.Button(checks, "Analyze Fit", AnalyzeFit, Toolkit.ButtonVariant.Secondary,
                "Measures how closely the mapped articulators rebuild each viseme.");
            analyze.name = "analyze-fit";
            ui.ShowWhen(analyze, () => data.profile != null);
            ui.EnableWhen(analyze, () => CanUseRigTools() && Descriptor() != null);
            var emulator = ui.Button(checks, "Add Viseme Test Emulator", AddOrSelectEmulator, Toolkit.ButtonVariant.Secondary,
                "Plays visemes in the editor so you can check the result without speaking.");
            emulator.name = "viseme-test-emulator";
            ui.OnRefresh(() =>
            {
                var found = FindEmulator() != null;
                var text = found ? "Select Viseme Test Emulator" : "Add Viseme Test Emulator";
                if (emulator.text != text) emulator.text = text;
                emulator.SetEnabled(found || !EditorApplication.isPlaying);
            });
            ui.Caption(rig, FitResult);
            ui.Messages(rig, () => new[] { YUCPInspector.Message.Error(fitError) });

            var editors = ui.Disclosure(rig, "Viseme Poses and Bindings", "rig-tools");
            editors.name = "rig-tools";
            ui.ShowWhen(editors, () => data.profile != null);
            rigHost = new VisualElement();
            editors.Add(rigHost);
        }

        private void RefreshProfile()
        {
            var profile = data.profile;
            if (profile != displayedProfile || (profile == null) != (profileSerializedObject == null))
                RebuildProfileUI();
            profileSerializedObject?.UpdateIfRequiredOrScript();
            foreach (var refresh in profileRefreshers) refresh();
        }

        /// <summary>Rebuilds the profile's fields when another profile is assigned (never on a timer).</summary>
        private void RebuildProfileUI()
        {
            displayedProfile = data.profile;
            profileSerializedObject = null;
            profileRefreshers.Clear();
            motionHost.Clear();
            fineTuneHost.Clear();
            rigHost.Clear();

            if (displayedProfile == null)
            {
                ui.Caption(motionHost, "Create a profile to change the built-in tuning.");
                ui.Button(motionHost, "Create Profile", CreateProfile).name = "profile-action";
                ui.Caption(fineTuneHost, "Create a profile to fine-tune each sound.");
                ui.Button(fineTuneHost, "Create Profile", CreateProfile, Toolkit.ButtonVariant.Primary)
                    .name = "fine-tune-create-profile";
                return;
            }

            displayedProfile.EnsureDefaults();
            profileSerializedObject = new SerializedObject(displayedProfile);
            BuildMotionTuning(motionHost);
            BuildFineTune(fineTuneHost);
            BuildRigEditors(rigHost);
        }

        private void ProfileShowWhen(VisualElement element, Func<bool> visible)
        {
            void Update()
            {
                var display = visible() ? DisplayStyle.Flex : DisplayStyle.None;
                if (element.style.display != display) element.style.display = display;
            }
            profileRefreshers.Add(Update);
            Update();
        }

        private VisualElement ProfileGroup(VisualElement host, string title, params (string path, string label)[] fields)
        {
            var group = ui.Group(host, title);
            foreach (var (path, label) in fields) ui.Field(group, profileSerializedObject, path, label);
            return group;
        }

        private void BuildMotionTuning(VisualElement host)
        {
            var speech = ProfileGroup(host, "Speech", ("authoredResidualDetail", "Authored Detail"));
            ProfileShowWhen(ui.Field(speech, profileSerializedObject, "betaCoarticulationStrength", "Transition Strength"),
                () => NaturalTransitions);
            ProfileGroup(host, "Voice",
                ("voiceNoiseFloor", "Noise Floor"),
                ("voiceFullScale", "Full Voice Level"));
            ProfileShowWhen(ProfileGroup(host, "Tracking response",
                ("localTrackingResponseSeconds", "Local Response"),
                ("remoteTrackingResponseSeconds", "Remote Response"),
                ("trackingAcquireResponseSeconds", "Tracking Start"),
                ("trackingBlendResponseSeconds", "Tracking Release"),
                ("remoteTrackingTrust", "Remote Trust")), () => TrackingOn);
            ProfileGroup(host, "Consonants",
                ("bilabialAssistStrength", "P / B / M Assist"),
                ("bilabialClosure", "P / B / M Closure"),
                ("labiodentalAssistStrength", "F / V Assist"),
                ("labiodentalBite", "F / V Bite"),
                ("sibilantAssistStrength", "S / Z Assist"),
                ("sibilantJawMaximum", "S / Z Jaw Maximum"),
                ("hiddenPhoneStrength", "M / N Recognition"),
                ("hiddenDetailStrength", "Hidden Detail"));
            ProfileGroup(host, "Tongue",
                ("tongueOutStrength", "Out"),
                ("tongueYStrength", "Vertical"),
                ("tongueXStrength", "Lateral"),
                ("tongueRollStrength", "Roll"),
                ("tongueArchStrength", "Arch"),
                ("tongueShapeStrength", "Shape"),
                ("tongueTwistStrength", "Twist"));
        }

        // ---- Fine-tune visemes ----------------------------------------------------------------------

        private void BuildFineTune(VisualElement host)
        {
            var adjustments = profileSerializedObject.FindProperty("visemeAdjustments");
            if (adjustments == null || adjustments.arraySize != VisemeReconstructionProfile.VisemeCount)
            {
                host.Add(Toolkit.CreateHelpBox("Reset the profile to repair its viseme adjustments.",
                    Toolkit.MessageType.Warning));
                return;
            }

            // Three rows of five: consonants, consonants, vowels.
            var chips = new Button[adjustments.arraySize];
            var edited = new Label();
            var selection = new VisualElement();
            Action refreshSelection = null;
            VisualElement row = null;
            for (var i = 0; i < chips.Length; i++)
            {
                if (i % 5 == 0) row = ui.ButtonRow(host);
                var index = i;
                chips[i] = ui.Button(row, FineTuneVisemeLabels[i], () =>
                {
                    selectedFineTuneVisemeIndex = index;
                    SessionState.SetInt(FineTuneKey, index);
                    ShowSelection();
                }, Toolkit.ButtonVariant.Secondary,
                    $"Fine-tune the {FineTuneVisemeLabels[i]} sound (Oculus viseme {VisemeReconstructionProfile.VisemeNames[i]}).");
                chips[i].name = $"fine-tune-viseme-{i}";
            }
            edited.AddToClassList("yucp-caption");
            host.Add(edited);
            host.Add(selection);

            void Refresh()
            {
                var names = new List<string>();
                for (var i = 0; i < chips.Length; i++)
                {
                    chips[i].EnableInClassList("yucp-segment--selected", i == selectedFineTuneVisemeIndex);
                    if (!IsFineTuneAdjustmentNeutral(adjustments.GetArrayElementAtIndex(i)))
                        names.Add(FineTuneVisemeLabels[i]);
                }
                var text = names.Count == 0 ? "" : "Edited: " + string.Join(", ", names) + ".";
                if (edited.text != text) edited.text = text;
                edited.style.display = text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                refreshSelection?.Invoke();
            }

            void ShowSelection()
            {
                selection.Clear();
                var index = selectedFineTuneVisemeIndex = Mathf.Clamp(selectedFineTuneVisemeIndex, 0, chips.Length - 1);
                var adjustment = adjustments.GetArrayElementAtIndex(index);
                var group = ui.Group(selection,
                    $"{FineTuneVisemeLabels[index]} (Oculus {VisemeReconstructionProfile.VisemeNames[index]})");
                ui.Caption(group, "Lower only the part that clips; 100% keeps the original shape.");
                var sliders = new List<(Slider slider, string label, string[] fields)>();
                void Add(VisualElement parent, string name, string label, string[] fields)
                {
                    var slider = Align(new Slider(label, 0f, 150f) { name = name, showInputField = true });
                    slider.RegisterValueChangedCallback(e =>
                    {
                        ApplyFineTune(adjustment, fields, e.newValue);
                        Refresh();
                    });
                    parent.Add(slider);
                    sliders.Add((slider, label, fields));
                }
                Add(group, "fine-tune-jaw-opening", "Jaw and Opening", FineTuneJawFields);
                Add(group, "fine-tune-lips", "Lips", FineTuneLipFields);
                Add(group, "fine-tune-tongue", "Tongue", FineTuneTongueFields);
                var precise = ui.Disclosure(group, "Precise Controls", "fine-tune-precise");
                precise.name = "fine-tune-precise-controls";
                foreach (var field in FineTuneAllFields)
                    Add(precise, "fine-tune-axis-" + field, FineTuneAxisLabel(field), new[] { field });
                var reset = ui.Button(group, "Reset Viseme", () =>
                {
                    ApplyFineTune(adjustment, FineTuneAllFields, 100f);
                    Refresh();
                }, Toolkit.ButtonVariant.Ghost, "Sets every part of this viseme back to 100%.");
                reset.name = "fine-tune-reset-viseme";
                refreshSelection = () =>
                {
                    foreach (var (slider, label, fields) in sliders)
                        RefreshFineTuneSlider(slider, label, adjustment, fields);
                    reset.SetEnabled(!IsFineTuneAdjustmentNeutral(adjustment));
                };
                Refresh();
            }

            profileRefreshers.Add(Refresh);
            ShowSelection();
        }

        private void ApplyFineTune(SerializedProperty adjustment, IReadOnlyList<string> fields, float percentage)
        {
            profileSerializedObject.UpdateIfRequiredOrScript();
            var value = Mathf.Clamp(percentage / 100f,
                VisemeArticulationAdjustment.Minimum,
                VisemeArticulationAdjustment.Maximum);
            foreach (var fieldName in fields)
            {
                var property = adjustment.FindPropertyRelative(fieldName);
                if (property != null) property.floatValue = value;
            }
            profileSerializedObject.ApplyModifiedProperties();
        }

        /// <summary>Shows the average of the fields, and "(Mixed)" when they differ.</summary>
        private static void RefreshFineTuneSlider(Slider slider, string label, SerializedProperty adjustment,
            IReadOnlyList<string> fields)
        {
            var total = 0f;
            var count = 0;
            var first = 1f;
            var mixed = false;
            foreach (var fieldName in fields)
            {
                var property = adjustment.FindPropertyRelative(fieldName);
                if (property == null) continue;
                if (count == 0) first = property.floatValue;
                else if (Mathf.Abs(property.floatValue - first) > 0.0001f) mixed = true;
                total += property.floatValue;
                count++;
            }
            var text = mixed ? label + " (Mixed)" : label;
            if (slider.label != text) slider.label = text;
            var value = (count == 0 ? 1f : total / count) * 100f;
            if (Mathf.Abs(slider.value - value) > 1e-4f) slider.SetValueWithoutNotify(value);
        }

        private static bool IsFineTuneAdjustmentNeutral(SerializedProperty adjustment)
        {
            foreach (var fieldName in FineTuneAllFields)
            {
                var property = adjustment.FindPropertyRelative(fieldName);
                if (property != null && Mathf.Abs(property.floatValue - 1f) > 0.0001f)
                    return false;
            }
            return true;
        }

        private static string FineTuneAxisLabel(string fieldName)
        {
            switch (fieldName)
            {
                case "smileSad": return "Smile / Sad";
                case "jawX": return "Jaw Left / Right";
                case "jawZ": return "Jaw Forward / Back";
                case "mouthX": return "Mouth Left / Right";
                case "tongueY": return "Tongue Up / Down";
                case "tongueX": return "Tongue Left / Right";
                case "tongueArchY": return "Tongue Arch";
                default: return ObjectNames.NicifyVariableName(fieldName);
            }
        }

        // ---- Rig and calibration --------------------------------------------------------------------

        private void BuildRigEditors(VisualElement host)
        {
            var so = profileSerializedObject;
            var poses = ui.Group(host, "Viseme poses");
            var poseCount = so.FindProperty("visemePoses")?.arraySize ?? 0;
            if (poseCount == 0)
                ui.Caption(poses, "Reset the profile to restore its viseme poses.");
            else
                Picker(poses, "Viseme", FineTuneVisemeLabels.Take(poseCount).ToList(), selectedVisemeIndex,
                    i => SessionState.SetInt(VisemeKey, selectedVisemeIndex = i), (content, i) =>
                    {
                        var path = $"visemePoses.Array.data[{i}].";
                        ui.Field(content, so, path + "animationOverride", "Animation Override");
                        foreach (var field in PoseMouthFields.Concat(FineTuneTongueFields))
                            ui.Field(content, so, path + field, FineTuneAxisLabel(field));
                    });

            var bindings = ui.Group(host, "Articulator bindings");
            var bindingArray = so.FindProperty("articulatorBindings");
            if (bindingArray == null || bindingArray.arraySize == 0)
            {
                ui.Caption(bindings, "Reset the profile to restore its articulator bindings.");
                return;
            }
            var names = new List<string>();
            for (var i = 0; i < bindingArray.arraySize; i++)
            {
                var articulator = bindingArray.GetArrayElementAtIndex(i).FindPropertyRelative("articulator");
                names.Add(articulator == null ? $"Binding {i + 1}" : articulator.enumDisplayNames[articulator.enumValueIndex]);
            }
            Picker(bindings, "Articulator", names, selectedBindingIndex,
                i => SessionState.SetInt(BindingKey, selectedBindingIndex = i), (content, i) =>
                {
                    var path = $"articulatorBindings.Array.data[{i}].";
                    ui.Field(content, so, path + "trackingParameter", "Tracking Parameter");
                    BuildBlendShapePicker(content, so.FindProperty(path + "blendShapeName"));
                    ui.Field(content, so, path + "animationOverride", "Positive Pose Clip");
                    ui.Field(content, so, path + "negativeAnimationOverride", "Negative Pose Clip");
                    ui.Field(content, so, path + "trackingScale", "Tracking Scale");
                    ui.Field(content, so, path + "trackingOffset", "Tracking Offset");
                    ui.Field(content, so, path + "localReliability", "Local Reliability");
                    ui.Field(content, so, path + "remoteReliability", "Remote Reliability");
                });
        }

        /// <summary>A pop-up that chooses which item the fields below it edit.</summary>
        private void Picker(VisualElement parent, string label, List<string> choices, int selected,
            Action<int> remember, Action<VisualElement, int> build)
        {
            var popup = Align(new PopupField<string>(label, choices, Mathf.Clamp(selected, 0, choices.Count - 1)));
            var content = new VisualElement();
            parent.Add(popup);
            parent.Add(content);
            void Show()
            {
                content.Clear();
                profileSerializedObject.UpdateIfRequiredOrScript();
                build(content, popup.index);
            }
            popup.RegisterValueChangedCallback(_ =>
            {
                remember(popup.index);
                Show();
            });
            Show();
        }

        private void BuildBlendShapePicker(VisualElement parent, SerializedProperty property)
        {
            var renderer = EffectiveRenderer();
            var mesh = renderer != null ? renderer.sharedMesh : null;
            if (mesh == null)
            {
                ui.Field(parent, profileSerializedObject, property.propertyPath, "Blendshape");
                return;
            }

            const string none = "<None>";
            const string missingSuffix = " (missing)";
            var shapes = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

            var choices = new List<string> { none };
            var current = property.stringValue ?? string.Empty;
            var currentChoice = none;
            if (!string.IsNullOrEmpty(current))
            {
                currentChoice = shapes.Contains(current) ? current : current + missingSuffix;
                if (currentChoice != current) choices.Add(currentChoice);
            }
            choices.AddRange(shapes);

            var popup = Align(new PopupField<string>("Blendshape", choices, currentChoice) { tooltip = property.tooltip });
            popup.RegisterValueChangedCallback(evt =>
            {
                var value = evt.newValue == none ? string.Empty : evt.newValue;
                if (value.EndsWith(missingSuffix, StringComparison.Ordinal))
                    value = value.Substring(0, value.Length - missingSuffix.Length);
                profileSerializedObject.UpdateIfRequiredOrScript();
                property.stringValue = value;
                profileSerializedObject.ApplyModifiedProperties();
                ui.Refresh();
            });
            parent.Add(popup);
        }

        private bool CanUseRigTools()
        {
            var renderer = EffectiveRenderer();
            return data.profile != null && renderer != null && renderer.sharedMesh != null;
        }

        private string MappingCoverage()
        {
            var profile = data.profile;
            if (profile == null) return "Create a profile to map the rig and edit viseme poses.";
            var renderer = EffectiveRenderer();
            var mesh = renderer != null ? renderer.sharedMesh : null;
            if (mesh == null) return "Assign the face renderer to map the rig.";

            var bindings = (profile.articulatorBindings ?? Array.Empty<ArticulatorRigBinding>())
                .Where(binding => binding != null).ToArray();
            var mapped = bindings.Count(binding =>
                binding.animationOverride != null || binding.negativeAnimationOverride != null ||
                !string.IsNullOrWhiteSpace(binding.blendShapeName) &&
                mesh.GetBlendShapeIndex(binding.blendShapeName) >= 0);
            var descriptor = Descriptor();
            var visemes = descriptor != null
                ? AdvancedVisemeReconstructorProcessor.ResolveVisemeNames(descriptor, mesh)
                : Array.Empty<string>();
            var found = visemes.Count(name => !string.IsNullOrEmpty(name));
            return $"{mapped} of {bindings.Length} articulators mapped, " +
                   $"{found} of {VisemeReconstructionProfile.VisemeCount} Oculus visemes found.";
        }

        private string FitResult()
        {
            var profile = data.profile;
            return profile != null && (profile.LastReconstructionRms > 0f || profile.LastReconstructionMaximum > 0f)
                ? $"Last fit: RMS error {profile.LastReconstructionRms:G4}, maximum {profile.LastReconstructionMaximum:G4}."
                : "";
        }

        // ---- Avatar menu ----------------------------------------------------------------------------

        private void BuildAvatarMenu()
        {
            var menu = ui.Section("Avatar menu");
            ui.Field(menu, "createTuningMenu", "Add Tuning Sliders");
            var sliders = ui.Indent(menu);
            ui.Field(sliders, "tuningMenuPath", "Menu Path");
            ui.Field(sliders, "saveTuningValues", "Saved");
            ui.Field(sliders, "tuningMenuSections", "Slider Groups");
            ui.Caption(sliders, TuningBudget).name = "runtime-menu-budget";
            ui.ShowWhen(sliders, () => data.createTuningMenu);

            // The toggle only exists when this component reuses or creates the tracking inputs.
            bool CanCreateToggle() => TrackingOn && data.trackingInputs != AdvancedVisemeTrackingInputs.Auto;
            ui.ShowWhen(ui.Field(menu, "createFaceTrackingToggle", "Face Tracking Toggle"), CanCreateToggle);
            var togglePath = ui.Indent(menu);
            ui.Field(togglePath, "faceTrackingMenuPath", "Menu Path");
            ui.ShowWhen(togglePath, () => CanCreateToggle() && data.createFaceTrackingToggle);

            var parameters = ui.Disclosure(menu, "Parameters", "parameters");
            parameters.name = "avatar-menu-settings";
            ui.Field(parameters, "parameterPrefix", "Parameter Prefix");
            ui.ShowWhen(ui.Field(parameters, "existingTrackingPrefix", "Existing Prefix"), () =>
                data.trackingInputs == AdvancedVisemeTrackingInputs.Auto ||
                data.trackingInputs == AdvancedVisemeTrackingInputs.ReuseExisting).name = "reuse-prefix-container";
            ui.ShowWhen(ui.Field(parameters, "trackingEncoding", "Encoding"), () => CreatesInputs);
        }

        private string TuningBudget()
        {
            var sections = data.tuningMenuSections;
            var count = AdvancedVisemeTuning.Controls.Count(control =>
                (sections & AdvancedVisemeTuning.Section(control)) != 0);
            return count == 0
                ? "Choose at least one slider group."
                : $"Shares {count} sliders with other players in {AdvancedVisemeTuning.CompactSyncTransportBits(count)} synced bits.";
        }

        // ---- Actions --------------------------------------------------------------------------------

        private void CreateProfile()
        {
            EnsureProfile();
            ui.Refresh();
        }

        /// <summary>The profile being edited, created and assigned on the first edit.</summary>
        private bool EnsureProfile()
        {
            if (profileSerializedObject != null && displayedProfile == data.profile) return true;
            if (data.profile == null)
            {
                var created = CreateProfileAsset(data);
                serializedObject.Update();
                serializedObject.FindProperty("profile").objectReferenceValue = created;
                serializedObject.ApplyModifiedProperties();
            }
            RebuildProfileUI();
            return profileSerializedObject != null;
        }

        private static VisemeReconstructionProfile CreateProfileAsset(AdvancedVisemeReconstructorData data)
        {
            const string folder = "Assets/YUCP/AdvancedVisemeProfiles";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{Sanitize(data.gameObject.name)}.asset");
            var profile = ScriptableObject.CreateInstance<VisemeReconstructionProfile>();
            profile.ResetToDefaults();
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private void ApplyAutoMap(bool overwriteExisting)
        {
            var descriptor = Descriptor();
            if (descriptor == null || data.profile == null) return;
            Undo.RecordObject(data, "Auto-map Advanced Viseme Renderer");
            if (data.faceRenderer == null) data.faceRenderer = descriptor.VisemeSkinnedMesh;
            var renderer = data.faceRenderer;
            if (renderer == null || renderer.sharedMesh == null) return;

            Undo.RecordObject(data.profile, overwriteExisting
                ? "Remap Advanced Viseme Rig"
                : "Auto-map Missing Advanced Viseme Bindings");
            data.profile.EnsureDefaults();
            var resolved = AdvancedVisemeReconstructorProcessor.ResolveArticulatorBlendShapes(
                renderer.sharedMesh, data.profile);
            foreach (var binding in data.profile.articulatorBindings)
            {
                if (binding == null || !resolved.TryGetValue(binding.articulator, out var shape)) continue;
                if (overwriteExisting || string.IsNullOrWhiteSpace(binding.blendShapeName))
                    binding.blendShapeName = shape;
            }
            EditorUtility.SetDirty(data.profile);
            EditorUtility.SetDirty(data);
            RebuildProfileUI();
            ui.Refresh();
        }

        private void AnalyzeFit()
        {
            var descriptor = Descriptor();
            var renderer = EffectiveRenderer();
            if (descriptor == null || renderer == null || renderer.sharedMesh == null || data.profile == null) return;

            data.profile.EnsureDefaults();
            var names = AdvancedVisemeReconstructorProcessor.ResolveVisemeNames(descriptor, renderer.sharedMesh);
            var indices = names.Select(name =>
                string.IsNullOrEmpty(name) ? -1 : renderer.sharedMesh.GetBlendShapeIndex(name)).ToArray();
            var resolved = AdvancedVisemeReconstructorProcessor.ResolveArticulatorBlendShapes(
                renderer.sharedMesh, data.profile);
            var basis = AdvancedVisemeReconstructorProcessor.BuildCalibrationBasis(renderer.sharedMesh, resolved);
            var result = AdvancedVisemeMeshCalibrator.Build(renderer.sharedMesh, indices, basis);
            fitError = result.success ? null : result.error;
            if (result.success)
            {
                Undo.RecordObject(data.profile, "Analyze Advanced Viseme Fit");
                data.profile.SetDiagnostics(result.fitRms, result.fitMaximum);
                EditorUtility.SetDirty(data.profile);
                UnityEngine.Object.DestroyImmediate(result.mesh);
            }
            ui.Refresh();
        }

        private void AddOrSelectEmulator()
        {
            var emulator = FindEmulator();
            if (emulator == null) emulator = Undo.AddComponent<VisemeTestEmulatorData>(data.gameObject);
            Selection.activeGameObject = emulator.gameObject;
            EditorGUIUtility.PingObject(emulator);
            ui.Refresh();
        }

        private VisemeTestEmulatorData FindEmulator()
        {
            var descriptor = Descriptor();
            return descriptor != null
                ? descriptor.GetComponentInChildren<VisemeTestEmulatorData>(true)
                : data.GetComponent<VisemeTestEmulatorData>();
        }

        // Undoable, so no confirmation dialog (DESIGN.md §5).
        private void ResetProfile()
        {
            if (data.profile == null) return;
            Undo.RecordObject(data.profile, "Reset Viseme Reconstruction Profile");
            data.profile.ResetToDefaults();
            EditorUtility.SetDirty(data.profile);
            RebuildProfileUI();
            ui.Refresh();
        }

        private VRCAvatarDescriptor Descriptor() => data.GetComponentInParent<VRCAvatarDescriptor>(true);

        private SkinnedMeshRenderer EffectiveRenderer()
        {
            if (data.faceRenderer != null) return data.faceRenderer;
            var descriptor = Descriptor();
            return descriptor != null ? descriptor.VisemeSkinnedMesh : null;
        }

        private static string Sanitize(string value)
        {
            var chars = (value ?? "Avatar").ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_') chars[i] = '_';
            return new string(chars);
        }
    }
}
