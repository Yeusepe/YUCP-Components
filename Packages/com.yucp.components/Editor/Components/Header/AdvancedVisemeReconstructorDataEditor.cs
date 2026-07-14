using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.Components.Editor.MeshUtils;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(AdvancedVisemeReconstructorData))]
    public sealed class AdvancedVisemeReconstructorDataEditor : UnityEditor.Editor
    {
        private enum InspectorMode
        {
            Simple,
            Advanced
        }

        private const float MinimumVisemeResponseSeconds = 0.006f;
        private const float MaximumVisemeResponseSeconds = 0.12f;
        private const float MinimumSpeechHangoverSeconds = 0.04f;
        private const float MaximumSpeechHangoverSeconds = 0.4f;

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
            "Automatic (Recommended)",
            "Off",
            "Use Existing Setup",
            "Create Compact Inputs",
            "Create Detailed Inputs",
            "Create Full Tongue Inputs"
        };

        private AdvancedVisemeReconstructorData data;
        private SerializedProperty faceRendererProp;
        private SerializedProperty profileProp;
        private SerializedProperty mouthOwnershipProp;
        private SerializedProperty reconstructionModeProp;
        private SerializedProperty trackingInputsProp;
        private SerializedProperty trackingEncodingProp;
        private SerializedProperty fusionModeProp;
        private SerializedProperty parameterPrefixProp;
        private SerializedProperty createToggleProp;
        private SerializedProperty menuPathProp;
        private SerializedProperty existingPrefixProp;
        private SerializedProperty createTuningMenuProp;
        private SerializedProperty tuningMenuPathProp;
        private SerializedProperty saveTuningValuesProp;
        private SerializedProperty tuningMenuSectionsProp;
        private SerializedProperty verboseLoggingProp;

        private VisualElement validation;
        private VisualElement modeSwitcher;
        private VisualElement simpleModeHost;
        private VisualElement advancedModeHost;
        private VisualElement simpleProfileHost;
        private VisualElement simpleMenuOptions;
        private VisualElement simpleFaceRendererContainer;
        private Button simpleModeButton;
        private Button advancedModeButton;
        private PopupField<string> simpleTrackingPopup;
        private Toggle simpleNaturalTransitionsToggle;
        private Toggle simpleKeepSpeechClearToggle;
        private Label simpleTrackingStatusLabel;
        private Label simpleMenuStatusLabel;
        private Slider simpleSpeechMovementSlider;
        private Slider simpleSpeechLivelinessSlider;
        private Slider simpleQuietSpeechSlider;
        private Slider simpleReactionSpeedSlider;
        private Slider simplePauseStabilitySlider;
        private Slider simplePronunciationHelpSlider;
        private Slider simpleFaceTrackingPrioritySlider;
        private Slider simpleTongueMotionSlider;
        private VisualElement encodingContainer;
        private VisualElement fusionContainer;
        private VisualElement toggleContainer;
        private VisualElement menuPathContainer;
        private VisualElement reuseContainer;
        private VisualElement tuningMenuOptions;
        private VisualElement motionProfileHost;
        private VisualElement rigProfileHost;
        private VisualElement expertProfileHost;
        private VisualElement coarticulationContainer;
        private VisualElement buildStatus;
        private Label trackingBudgetLabel;
        private Label tuningMenuBudgetLabel;
        private Label mappingCoverageLabel;
        private Button autoMapButton;
        private Button remapAllButton;
        private Button analyzeFitButton;
        private Button emulatorButton;

        private VisemeReconstructionProfile displayedProfile;
        private SerializedObject profileSerializedObject;
        private bool profileUiBuilt;
        private int selectedVisemeIndex;
        private int selectedBindingIndex;
        private InspectorMode inspectorMode;

        private void OnEnable()
        {
            data = (AdvancedVisemeReconstructorData)target;
            faceRendererProp = serializedObject.FindProperty("faceRenderer");
            profileProp = serializedObject.FindProperty("profile");
            mouthOwnershipProp = serializedObject.FindProperty("mouthOwnership");
            reconstructionModeProp = serializedObject.FindProperty("reconstructionMode");
            trackingInputsProp = serializedObject.FindProperty("trackingInputs");
            trackingEncodingProp = serializedObject.FindProperty("trackingEncoding");
            fusionModeProp = serializedObject.FindProperty("fusionMode");
            parameterPrefixProp = serializedObject.FindProperty("parameterPrefix");
            createToggleProp = serializedObject.FindProperty("createFaceTrackingToggle");
            menuPathProp = serializedObject.FindProperty("faceTrackingMenuPath");
            existingPrefixProp = serializedObject.FindProperty("existingTrackingPrefix");
            createTuningMenuProp = serializedObject.FindProperty("createTuningMenu");
            tuningMenuPathProp = serializedObject.FindProperty("tuningMenuPath");
            saveTuningValuesProp = serializedObject.FindProperty("saveTuningValues");
            tuningMenuSectionsProp = serializedObject.FindProperty("tuningMenuSections");
            verboseLoggingProp = serializedObject.FindProperty("verboseLogging");

            var id = data != null ? data.GetInstanceID() : 0;
            selectedVisemeIndex = SessionState.GetInt($"YUCP_AVR_Viseme_{id}", 0);
            selectedBindingIndex = SessionState.GetInt($"YUCP_AVR_Articulator_{id}", 0);
            inspectorMode = (InspectorMode)Mathf.Clamp(
                SessionState.GetInt(InspectorModeSessionKey(id), (int)InspectorMode.Simple),
                (int)InspectorMode.Simple, (int)InspectorMode.Advanced);
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();

            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Advanced Viseme Reconstructor"));

            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(AdvancedVisemeReconstructorData));
            if (supportBanner != null) root.Add(supportBanner);

            validation = new VisualElement { name = "validation-messages" };
            root.Add(validation);

            var tabsStylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Layouts/YUCPTabs.uss");
            if (tabsStylesheet != null) root.styleSheets.Add(tabsStylesheet);

            BuildModeSwitcher(root);

            simpleModeHost = new VisualElement { name = "simple-mode-ui" };
            simpleModeHost.AddToClassList("yucp-tabs-content");
            root.Add(simpleModeHost);
            BuildSimpleInspector(simpleModeHost);

            advancedModeHost = new VisualElement { name = "advanced-mode-ui" };
            advancedModeHost.AddToClassList("yucp-tabs-content");
            root.Add(advancedModeHost);
            BuildSetupCard(advancedModeHost);
            BuildFaceTrackingCard(advancedModeHost);
            BuildMotionTuning(advancedModeHost);
            BuildAvatarMenuSettings(advancedModeHost);
            BuildRigTools(advancedModeHost);
            BuildExpertSettings(advancedModeHost);

            RebuildProfileSections(profileProp.objectReferenceValue as VisemeReconstructionProfile, true);
            UpdateDynamicUI();

            root.schedule.Execute(() =>
            {
                if (target == null) return;
                serializedObject.UpdateIfRequiredOrScript();
                var selectedProfile = profileProp.objectReferenceValue as VisemeReconstructionProfile;
                if (!profileUiBuilt || selectedProfile != displayedProfile)
                    RebuildProfileSections(selectedProfile, true);
                UpdateDynamicUI();
            }).Every(200);

            return root;
        }

        internal static string InspectorModeSessionKey(int instanceId)
        {
            return $"YUCP_AVR_UIMode_{instanceId}";
        }

        internal static float ReactionSpeedFromSeconds(float seconds)
        {
            var clamped = Mathf.Clamp(seconds,
                MinimumVisemeResponseSeconds, MaximumVisemeResponseSeconds);
            return Mathf.Clamp01(Mathf.Log(MaximumVisemeResponseSeconds / clamped) /
                                 Mathf.Log(MaximumVisemeResponseSeconds /
                                           MinimumVisemeResponseSeconds));
        }

        internal static float SecondsFromReactionSpeed(float reactionSpeed)
        {
            return MaximumVisemeResponseSeconds * Mathf.Pow(
                MinimumVisemeResponseSeconds / MaximumVisemeResponseSeconds,
                Mathf.Clamp01(reactionSpeed));
        }

        internal static float PauseStabilityFromSeconds(float seconds)
        {
            var clamped = Mathf.Clamp(seconds,
                MinimumSpeechHangoverSeconds, MaximumSpeechHangoverSeconds);
            return Mathf.Clamp01(Mathf.Log(clamped / MinimumSpeechHangoverSeconds) /
                                 Mathf.Log(MaximumSpeechHangoverSeconds /
                                           MinimumSpeechHangoverSeconds));
        }

        internal static float SecondsFromPauseStability(float pauseStability)
        {
            return MinimumSpeechHangoverSeconds * Mathf.Pow(
                MaximumSpeechHangoverSeconds / MinimumSpeechHangoverSeconds,
                Mathf.Clamp01(pauseStability));
        }

        private void BuildModeSwitcher(VisualElement root)
        {
            modeSwitcher = new VisualElement { name = "mode-switcher" };
            modeSwitcher.AddToClassList("yucp-tabs-header");

            simpleModeButton = new Button(() => SetInspectorMode(InspectorMode.Simple))
            {
                name = "simple-mode-tab",
                text = "Simple"
            };
            simpleModeButton.AddToClassList("yucp-tab");
            simpleModeButton.style.flexGrow = 1;
            modeSwitcher.Add(simpleModeButton);

            advancedModeButton = new Button(() => SetInspectorMode(InspectorMode.Advanced))
            {
                name = "advanced-mode-tab",
                text = "Advanced"
            };
            advancedModeButton.AddToClassList("yucp-tab");
            advancedModeButton.style.flexGrow = 1;
            modeSwitcher.Add(advancedModeButton);

            root.Add(modeSwitcher);
        }

        private void SetInspectorMode(InspectorMode mode)
        {
            inspectorMode = mode;
            SessionState.SetInt(InspectorModeSessionKey(data.GetInstanceID()), (int)mode);
            UpdateInspectorMode();
        }

        private void UpdateInspectorMode()
        {
            if (simpleModeHost != null)
                simpleModeHost.style.display = inspectorMode == InspectorMode.Simple
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            if (advancedModeHost != null)
                advancedModeHost.style.display = inspectorMode == InspectorMode.Advanced
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;

            if (simpleModeButton != null)
            {
                if (inspectorMode == InspectorMode.Simple)
                    simpleModeButton.AddToClassList("yucp-tab-selected");
                else
                    simpleModeButton.RemoveFromClassList("yucp-tab-selected");
            }
            if (advancedModeButton != null)
            {
                if (inspectorMode == InspectorMode.Advanced)
                    advancedModeButton.AddToClassList("yucp-tab-selected");
                else
                    advancedModeButton.RemoveFromClassList("yucp-tab-selected");
            }
        }

        private void BuildSimpleInspector(VisualElement root)
        {
            var setup = YUCPUIToolkitHelper.CreateCard(
                "Quick Setup", "Choose how speech and face tracking work together.");
            var setupContent = YUCPUIToolkitHelper.GetCardContent(setup);
            simpleFaceRendererContainer = new VisualElement
            {
                name = "simple-face-renderer"
            };
            simpleFaceRendererContainer.Add(YUCPUIToolkitHelper.CreateField(
                faceRendererProp, "Face Renderer"));
            setupContent.Add(simpleFaceRendererContainer);

            var trackingIndex = SimpleTrackingModeIndex(
                (AdvancedVisemeTrackingInputs)trackingInputsProp.enumValueIndex);
            simpleTrackingPopup = new PopupField<string>(
                "Face Tracking", SimpleTrackingLabels, trackingIndex)
            {
                name = "simple-face-tracking",
                tooltip = "Automatic reuses a compatible face-tracking setup when one is already installed. The create options add their own compact inputs."
            };
            simpleTrackingPopup.AddToClassList("yucp-field-input");
            simpleTrackingPopup.RegisterValueChangedCallback(evt =>
            {
                var selected = SimpleTrackingLabels.IndexOf(evt.newValue);
                if (selected < 0 || selected >= SimpleTrackingModes.Length) return;
                SetComponentEnum(
                    trackingInputsProp, (int)SimpleTrackingModes[selected],
                    "Change Advanced Viseme Face Tracking");
            });
            setupContent.Add(simpleTrackingPopup);

            simpleNaturalTransitionsToggle = CreateSimpleToggle(
                "simple-natural-transitions",
                "Natural Transitions",
                "Uses nearby sounds to ease into the next mouth shape and enables smart tongue inference.",
                value => SetComponentEnum(
                    reconstructionModeProp,
                    value
                        ? (int)AdvancedVisemeReconstructionMode.BetaCoarticulation
                        : (int)AdvancedVisemeReconstructionMode.Normal,
                    "Change Advanced Viseme Transitions"));
            setupContent.Add(simpleNaturalTransitionsToggle);

            simpleKeepSpeechClearToggle = CreateSimpleToggle(
                "simple-keep-speech-clear",
                "Keep Speech Shapes Clear",
                "Lets speech correct closures and sharp consonants only where face tracking leaves uncertainty.",
                value => SetComponentEnum(
                    fusionModeProp,
                    value
                        ? (int)AdvancedVisemeFusionMode.PhoneticAssist
                        : (int)AdvancedVisemeFusionMode.TrackerAuthoritative,
                    "Change Advanced Viseme Speech Assistance"));
            setupContent.Add(simpleKeepSpeechClearToggle);

            simpleTrackingStatusLabel = Caption("", "simple-tracking-status");
            setupContent.Add(simpleTrackingStatusLabel);
            root.Add(setup);

            var feel = YUCPUIToolkitHelper.CreateCard(
                "Speech Feel", "Shape the motion without needing animation terminology.");
            simpleProfileHost = YUCPUIToolkitHelper.GetCardContent(feel);
            root.Add(feel);

            var avatarControls = YUCPUIToolkitHelper.CreateCard(
                "Avatar Controls", "Optionally adjust the same settings while wearing the avatar.");
            var avatarControlsContent = YUCPUIToolkitHelper.GetCardContent(avatarControls);
            avatarControlsContent.Add(YUCPUIToolkitHelper.CreateField(
                createTuningMenuProp, "Add Settings to Avatar Menu"));
            simpleMenuOptions = new VisualElement { name = "simple-menu-options" };
            simpleMenuOptions.Add(YUCPUIToolkitHelper.CreateField(
                saveTuningValuesProp, "Remember Avatar Menu Changes"));
            avatarControlsContent.Add(simpleMenuOptions);
            simpleMenuStatusLabel = Caption("", "simple-menu-status");
            avatarControlsContent.Add(simpleMenuStatusLabel);
            root.Add(avatarControls);
        }

        private static Toggle CreateSimpleToggle(
            string name,
            string label,
            string tooltip,
            Action<bool> changed)
        {
            var toggle = new Toggle(label)
            {
                name = name,
                tooltip = tooltip
            };
            toggle.AddToClassList("yucp-field-input");
            toggle.RegisterValueChangedCallback(evt => changed(evt.newValue));
            return toggle;
        }

        private void SetComponentEnum(
            SerializedProperty property,
            int value,
            string undoName)
        {
            if (property == null || property.enumValueIndex == value) return;
            serializedObject.UpdateIfRequiredOrScript();
            Undo.RecordObject(data, undoName);
            property.enumValueIndex = value;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
            UpdateDynamicUI();
        }

        private static int SimpleTrackingModeIndex(AdvancedVisemeTrackingInputs mode)
        {
            var index = Array.IndexOf(SimpleTrackingModes, mode);
            return index < 0 ? 0 : index;
        }

        private void BuildSetupCard(VisualElement root)
        {
            var card = YUCPUIToolkitHelper.CreateCard("Setup", "The face rig and reconstruction mode.");
            var content = YUCPUIToolkitHelper.GetCardContent(card);
            content.Add(YUCPUIToolkitHelper.CreateField(faceRendererProp, "Face Renderer"));
            content.Add(YUCPUIToolkitHelper.CreateField(profileProp, "Profile"));
            content.Add(YUCPUIToolkitHelper.CreateField(mouthOwnershipProp, "Mouth Ownership"));
            content.Add(YUCPUIToolkitHelper.CreateField(reconstructionModeProp, "Reconstruction"));
            root.Add(card);
        }

        private void BuildFaceTrackingCard(VisualElement root)
        {
            var card = YUCPUIToolkitHelper.CreateCard("Face Tracking", "Reuse a tailored VRCFT rig or generate compact inputs.");
            var content = YUCPUIToolkitHelper.GetCardContent(card);
            content.Add(YUCPUIToolkitHelper.CreateField(trackingInputsProp, "Inputs"));

            encodingContainer = new VisualElement { name = "encoding-container" };
            encodingContainer.Add(YUCPUIToolkitHelper.CreateField(trackingEncodingProp, "Encoding"));
            content.Add(encodingContainer);

            fusionContainer = new VisualElement { name = "fusion-container" };
            fusionContainer.Add(YUCPUIToolkitHelper.CreateField(fusionModeProp, "Fusion"));
            content.Add(fusionContainer);

            toggleContainer = new VisualElement { name = "toggle-container" };
            toggleContainer.Add(YUCPUIToolkitHelper.CreateField(createToggleProp, "Face Tracking Toggle"));
            menuPathContainer = new VisualElement { name = "menu-path-container" };
            menuPathContainer.Add(YUCPUIToolkitHelper.CreateField(menuPathProp, "Toggle Path"));
            toggleContainer.Add(menuPathContainer);
            content.Add(toggleContainer);

            reuseContainer = new VisualElement { name = "reuse-prefix-container" };
            reuseContainer.Add(YUCPUIToolkitHelper.CreateField(existingPrefixProp, "Existing Prefix"));
            content.Add(reuseContainer);

            trackingBudgetLabel = Caption("", "tracking-budget");
            content.Add(trackingBudgetLabel);
            root.Add(card);
        }

        private void BuildMotionTuning(VisualElement root)
        {
            var foldout = RememberedFoldout("Motion Tuning", "motion-tuning", false);
            motionProfileHost = new VisualElement { name = "profile-settings-host" };
            foldout.Add(motionProfileHost);
            root.Add(foldout);
        }

        private void BuildAvatarMenuSettings(VisualElement root)
        {
            var foldout = RememberedFoldout("Avatar Menu", "avatar-menu-settings", false);
            foldout.Add(YUCPUIToolkitHelper.CreateField(createTuningMenuProp, "Add Tuning Sliders"));
            tuningMenuOptions = new VisualElement { name = "tuning-menu-options" };
            tuningMenuOptions.Add(YUCPUIToolkitHelper.CreateField(tuningMenuPathProp, "Menu Path"));
            tuningMenuOptions.Add(YUCPUIToolkitHelper.CreateField(saveTuningValuesProp, "Save Values"));
            tuningMenuOptions.Add(YUCPUIToolkitHelper.CreateField(tuningMenuSectionsProp, "Slider Groups"));
            foldout.Add(tuningMenuOptions);
            tuningMenuBudgetLabel = Caption("", "runtime-menu-budget");
            foldout.Add(tuningMenuBudgetLabel);
            root.Add(foldout);
        }

        private void BuildRigTools(VisualElement root)
        {
            var foldout = RememberedFoldout("Rig & Calibration", "rig-tools", false);
            rigProfileHost = new VisualElement();
            foldout.Add(rigProfileHost);
            root.Add(foldout);
        }

        private void BuildExpertSettings(VisualElement root)
        {
            var foldout = RememberedFoldout("Expert & Diagnostics", "expert-settings", false);
            expertProfileHost = new VisualElement();
            foldout.Add(expertProfileHost);
            root.Add(foldout);
        }

        private void RebuildProfileSections(VisemeReconstructionProfile profile, bool force)
        {
            if (!force && profileUiBuilt && profile == displayedProfile) return;
            profileUiBuilt = true;
            displayedProfile = profile;
            profileSerializedObject = null;
            coarticulationContainer = null;
            mappingCoverageLabel = null;
            autoMapButton = null;
            remapAllButton = null;
            analyzeFitButton = null;
            emulatorButton = null;
            buildStatus = null;

            simpleProfileHost.Clear();
            motionProfileHost.Clear();
            rigProfileHost.Clear();
            expertProfileHost.Clear();

            if (profile == null)
            {
                BuildSimpleProfileFields();
                BuildMissingProfilePrompt(motionProfileHost);
                rigProfileHost.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Create a profile to edit viseme poses, mappings, and calibration.",
                    YUCPUIToolkitHelper.MessageType.Info));
                BuildEmulatorButton(rigProfileHost);
                BuildExpertComponentFields(expertProfileHost);
                return;
            }

            profile.EnsureDefaults();
            profileSerializedObject = new SerializedObject(profile);
            BuildSimpleProfileFields();
            BuildMotionProfileFields();
            BuildRigProfileFields();
            BuildExpertProfileFields();
        }

        private void BuildSimpleProfileFields()
        {
            simpleSpeechMovementSlider = null;
            simpleSpeechLivelinessSlider = null;
            simpleQuietSpeechSlider = null;
            simpleReactionSpeedSlider = null;
            simplePauseStabilitySlider = null;
            simplePronunciationHelpSlider = null;
            simpleFaceTrackingPrioritySlider = null;
            simpleTongueMotionSlider = null;

            simpleSpeechMovementSlider = AddSimpleProfileSlider(
                "simple-speech-movement", "Expression Strength", "speechMotionStrength", 1f,
                value => value, value => value,
                "How boldly speech moves the mouth. Face tracking still owns movement it actually measures.");
            simpleSpeechLivelinessSlider = AddSimpleProfileSlider(
                "simple-speech-liveliness", "Speech Liveliness", "speechLiveliness", 0.5f,
                value => value, value => value,
                "Keeps speech-only mouth shapes quick and distinct. Lower is calmer; higher is livelier. Tracked movement stays unchanged.");
            simpleQuietSpeechSlider = AddSimpleProfileSlider(
                "simple-quiet-speech", "Quiet Speech Detail", "quietSpeechFloor", 0.55f,
                value => value, value => value,
                "Keeps mouth shapes visible while speaking softly or mumbling. This does not change the microphone threshold.");
            simpleReactionSpeedSlider = AddSimpleProfileSlider(
                "simple-reaction-speed", "Reaction Speed", "visemeResponseSeconds", 0.024f,
                ReactionSpeedFromSeconds, SecondsFromReactionSpeed,
                "How quickly the mouth catches each new sound. Faster is crisper; slower is softer.");
            simplePauseStabilitySlider = AddSimpleProfileSlider(
                "simple-pause-stability", "Pause Stability", "speechHangoverSeconds", 0.16f,
                PauseStabilityFromSeconds, SecondsFromPauseStability,
                "Bridges very short silent gaps between words. Higher reduces twitching, but holds the last shape a little longer.");
            simplePronunciationHelpSlider = AddSimpleProfileSlider(
                "simple-pronunciation-help", "Pronunciation Help", "phoneticConstraintStrength", 1f,
                value => value, value => value,
                "How strongly speech fixes closures and sharp consonants that tracking did not already measure.");
            simpleFaceTrackingPrioritySlider = AddSimpleProfileSlider(
                "simple-face-tracking-priority", "Follow My Face", "residualMismatchFade", 1f,
                value => value, value => value,
                "How strongly measured face movement replaces matching authored mouth motion. Untracked detail stays intact.");
            simpleTongueMotionSlider = AddSimpleProfileSlider(
                "simple-tongue-motion", "Tongue Motion", "tongueInferenceStrength", 1f,
                value => value, value => value,
                "Amount of inferred tongue motion when no real tongue measurement is available. Real tongue tracking always wins.");
        }

        private Slider AddSimpleProfileSlider(
            string name,
            string label,
            string propertyName,
            float fallbackValue,
            Func<float, float> toSimpleValue,
            Func<float, float> fromSimpleValue,
            string tooltip)
        {
            var slider = new Slider(label, 0f, 1f)
            {
                name = name,
                tooltip = tooltip,
                showInputField = false
            };
            slider.AddToClassList("yucp-field-input");
            var configured = ReadProfileFloat(propertyName, fallbackValue);
            slider.SetValueWithoutNotify(Mathf.Clamp01(toSimpleValue(configured)));
            slider.RegisterValueChangedCallback(evt =>
            {
                if (!EnsureProfileForSimpleEdit() || profileSerializedObject == null) return;
                profileSerializedObject.UpdateIfRequiredOrScript();
                var property = profileSerializedObject.FindProperty(propertyName);
                if (property == null) return;
                Undo.RecordObject(displayedProfile, "Tune Advanced Viseme Speech");
                property.floatValue = fromSimpleValue(Mathf.Clamp01(evt.newValue));
                profileSerializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(displayedProfile);
                UpdateDynamicUI();
            });
            simpleProfileHost.Add(slider);
            return slider;
        }

        private float ReadProfileFloat(string propertyName, float fallbackValue)
        {
            if (profileSerializedObject == null) return fallbackValue;
            profileSerializedObject.UpdateIfRequiredOrScript();
            var property = profileSerializedObject.FindProperty(propertyName);
            return property == null ? fallbackValue : property.floatValue;
        }

        private void RefreshSimpleProfileSlider(
            Slider slider,
            string propertyName,
            float fallbackValue,
            Func<float, float> toSimpleValue)
        {
            if (slider == null) return;
            slider.SetValueWithoutNotify(Mathf.Clamp01(
                toSimpleValue(ReadProfileFloat(propertyName, fallbackValue))));
            slider.SetEnabled(true);
        }

        private void BuildMissingProfilePrompt(VisualElement host)
        {
            host.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "This avatar uses YUCP's built-in natural tuning. Create a profile only when you want to customize it.",
                YUCPUIToolkitHelper.MessageType.Info));
            var button = YUCPUIToolkitHelper.CreateButton(
                "Create Custom Profile", CreateAndAssignProfile,
                YUCPUIToolkitHelper.ButtonVariant.Primary);
            button.name = "profile-action";
            host.Add(button);
        }

        private void BuildMotionProfileFields()
        {
            var speech = YUCPUIToolkitHelper.CreateFoldout("Speech", true);
            AddProfileField(speech, "visemeResponseSeconds", "Speech Response");
            AddProfileField(speech, "speechHangoverSeconds", "Silence Stability");
            AddProfileField(speech, "speechMotionStrength", "Speech Motion");
            AddProfileField(speech, "speechLiveliness", "Speech Lead");
            AddProfileField(speech, "authoredResidualDetail", "Authored Detail");
            coarticulationContainer = new VisualElement { name = "coarticulation-strength" };
            AddProfileField(coarticulationContainer, "betaCoarticulationStrength", "Coarticulation");
            speech.Add(coarticulationContainer);
            motionProfileHost.Add(speech);

            var tracking = YUCPUIToolkitHelper.CreateFoldout("Tracking Response", false);
            AddProfileField(tracking, "localTrackingResponseSeconds", "Local Response");
            AddProfileField(tracking, "remoteTrackingResponseSeconds", "Remote Response");
            AddProfileField(tracking, "trackingAcquireResponseSeconds", "Tracker Acquisition");
            AddProfileField(tracking, "trackingBlendResponseSeconds", "Tracker Release");
            AddProfileField(tracking, "remoteTrackingTrust", "Remote Trust");
            motionProfileHost.Add(tracking);

            var phonetics = YUCPUIToolkitHelper.CreateFoldout("Phonetic Detail", false);
            AddProfileField(phonetics, "phoneticConstraintStrength", "Constraint Amount");
            AddProfileField(phonetics, "bilabialAssistStrength", "PP Closure Assist");
            AddProfileField(phonetics, "labiodentalAssistStrength", "FF Bite Assist");
            AddProfileField(phonetics, "sibilantAssistStrength", "Sibilant Assist");
            AddProfileField(phonetics, "hiddenPhoneStrength", "M/N Recognition");
            AddProfileField(phonetics, "hiddenDetailStrength", "Hidden Detail");
            motionProfileHost.Add(phonetics);

            var tongue = YUCPUIToolkitHelper.CreateFoldout("Tongue", false);
            AddProfileField(tongue, "tongueInferenceStrength", "Inference");
            AddProfileField(tongue, "tongueOutStrength", "Out");
            AddProfileField(tongue, "tongueYStrength", "Vertical");
            AddProfileField(tongue, "tongueXStrength", "Lateral");
            AddProfileField(tongue, "tongueRollStrength", "Roll");
            AddProfileField(tongue, "tongueArchStrength", "Arch");
            AddProfileField(tongue, "tongueShapeStrength", "Shape");
            AddProfileField(tongue, "tongueTwistStrength", "Twist");
            motionProfileHost.Add(tongue);
        }

        private void BuildRigProfileFields()
        {
            mappingCoverageLabel = Caption("", "mapping-coverage");
            rigProfileHost.Add(mappingCoverageLabel);

            var mappingRow = ButtonRow();
            autoMapButton = RowButton(mappingRow, "Auto-map Missing", () => ApplyAutoMap(false),
                YUCPUIToolkitHelper.ButtonVariant.Primary, "auto-map-rig");
            remapAllButton = RowButton(mappingRow, "Remap All", ConfirmRemapAll,
                YUCPUIToolkitHelper.ButtonVariant.Secondary, "remap-all");
            rigProfileHost.Add(mappingRow);

            var analysisRow = ButtonRow();
            analyzeFitButton = RowButton(analysisRow, "Analyze Fit", AnalyzeFit,
                YUCPUIToolkitHelper.ButtonVariant.Secondary, "analyze-fit");
            BuildEmulatorButton(analysisRow);
            rigProfileHost.Add(analysisRow);

            var visemeEditor = YUCPUIToolkitHelper.CreateFoldout("Viseme Poses", false);
            visemeEditor.name = "viseme-pose-editor";
            BuildVisemePoseEditor(visemeEditor);
            rigProfileHost.Add(visemeEditor);

            var bindingEditor = YUCPUIToolkitHelper.CreateFoldout("Articulator Bindings", false);
            bindingEditor.name = "articulator-binding-editor";
            BuildArticulatorBindingEditor(bindingEditor);
            rigProfileHost.Add(bindingEditor);
        }

        private void BuildVisemePoseEditor(VisualElement parent)
        {
            var poses = profileSerializedObject.FindProperty("visemePoses");
            if (poses == null || poses.arraySize == 0)
            {
                parent.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "This profile has no viseme poses.", YUCPUIToolkitHelper.MessageType.Warning));
                return;
            }

            selectedVisemeIndex = Mathf.Clamp(selectedVisemeIndex, 0, poses.arraySize - 1);
            var choices = VisemeReconstructionProfile.VisemeNames.Take(poses.arraySize).ToList();
            var selector = new PopupField<string>("Viseme", choices, selectedVisemeIndex);
            selector.AddToClassList("yucp-field-input");
            parent.Add(selector);

            var content = new VisualElement();
            parent.Add(content);
            Action rebuild = () =>
            {
                content.Clear();
                profileSerializedObject.UpdateIfRequiredOrScript();
                var refreshed = profileSerializedObject.FindProperty("visemePoses");
                if (refreshed == null || refreshed.arraySize == 0) return;
                selectedVisemeIndex = Mathf.Clamp(selectedVisemeIndex, 0, refreshed.arraySize - 1);
                var pose = refreshed.GetArrayElementAtIndex(selectedVisemeIndex);
                AddBoundField(content, pose.FindPropertyRelative("animationOverride"), "Animation Override");

                var mouth = YUCPUIToolkitHelper.CreateFoldout("Jaw, Lips & Position", true);
                foreach (var field in new[]
                         {
                             "jawOpen", "lipClose", "mouthOpen", "lipFunnel", "lipPucker", "lipSuck",
                             "smileSad", "lipBite", "jawX", "jawZ", "mouthX"
                         })
                    AddBoundField(mouth, pose.FindPropertyRelative(field), Nicify(field));
                content.Add(mouth);

                var tongue = YUCPUIToolkitHelper.CreateFoldout("Tongue", false);
                foreach (var field in new[]
                         {
                             "tongueOut", "tongueY", "tongueX", "tongueRoll", "tongueArchY",
                             "tongueShape", "tongueTwistRight", "tongueTwistLeft"
                         })
                    AddBoundField(tongue, pose.FindPropertyRelative(field), Nicify(field));
                content.Add(tongue);
            };
            selector.RegisterValueChangedCallback(evt =>
            {
                selectedVisemeIndex = choices.IndexOf(evt.newValue);
                SessionState.SetInt($"YUCP_AVR_Viseme_{data.GetInstanceID()}", selectedVisemeIndex);
                rebuild();
            });
            rebuild();
        }

        private void BuildArticulatorBindingEditor(VisualElement parent)
        {
            var bindings = profileSerializedObject.FindProperty("articulatorBindings");
            if (bindings == null || bindings.arraySize == 0)
            {
                parent.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "This profile has no articulator bindings.", YUCPUIToolkitHelper.MessageType.Warning));
                return;
            }

            selectedBindingIndex = Mathf.Clamp(selectedBindingIndex, 0, bindings.arraySize - 1);
            var choices = new List<string>();
            for (var i = 0; i < bindings.arraySize; i++)
            {
                var articulator = bindings.GetArrayElementAtIndex(i).FindPropertyRelative("articulator");
                choices.Add(articulator == null ? $"Binding {i + 1}" : articulator.enumDisplayNames[articulator.enumValueIndex]);
            }

            var selector = new PopupField<string>("Articulator", choices, selectedBindingIndex);
            selector.AddToClassList("yucp-field-input");
            parent.Add(selector);
            var content = new VisualElement();
            parent.Add(content);

            Action rebuild = () =>
            {
                content.Clear();
                profileSerializedObject.UpdateIfRequiredOrScript();
                var refreshed = profileSerializedObject.FindProperty("articulatorBindings");
                if (refreshed == null || refreshed.arraySize == 0) return;
                selectedBindingIndex = Mathf.Clamp(selectedBindingIndex, 0, refreshed.arraySize - 1);
                var binding = refreshed.GetArrayElementAtIndex(selectedBindingIndex);

                var articulatorField = CreateBoundField(binding.FindPropertyRelative("articulator"), "Articulator");
                articulatorField.SetEnabled(false);
                content.Add(articulatorField);
                AddBoundField(content, binding.FindPropertyRelative("trackingParameter"), "VRCFT Suffix");
                BuildBlendShapePicker(content, binding.FindPropertyRelative("blendShapeName"));
                AddBoundField(content, binding.FindPropertyRelative("animationOverride"), "Positive Pose Clip");
                AddBoundField(content, binding.FindPropertyRelative("negativeAnimationOverride"), "Negative Pose Clip");

                var calibration = YUCPUIToolkitHelper.CreateFoldout("Calibration", false);
                AddBoundField(calibration, binding.FindPropertyRelative("trackingScale"), "Scale");
                AddBoundField(calibration, binding.FindPropertyRelative("trackingOffset"), "Offset");
                AddBoundField(calibration, binding.FindPropertyRelative("localReliability"), "Local Reliability");
                AddBoundField(calibration, binding.FindPropertyRelative("remoteReliability"), "Remote Reliability");
                content.Add(calibration);
            };
            selector.RegisterValueChangedCallback(evt =>
            {
                selectedBindingIndex = choices.IndexOf(evt.newValue);
                SessionState.SetInt($"YUCP_AVR_Articulator_{data.GetInstanceID()}", selectedBindingIndex);
                rebuild();
            });
            rebuild();
        }

        private void BuildBlendShapePicker(VisualElement parent, SerializedProperty property)
        {
            var renderer = EffectiveRenderer();
            var mesh = renderer != null ? renderer.sharedMesh : null;
            if (mesh == null)
            {
                AddBoundField(parent, property, "Blendshape");
                return;
            }

            const string none = "<None>";
            const string missingSuffix = " (missing)";
            var shapes = new List<string>();
            for (var i = 0; i < mesh.blendShapeCount; i++) shapes.Add(mesh.GetBlendShapeName(i));
            shapes = shapes.Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

            var choices = new List<string> { none };
            var current = property.stringValue ?? string.Empty;
            var currentChoice = none;
            if (!string.IsNullOrEmpty(current))
            {
                if (shapes.Contains(current)) currentChoice = current;
                else
                {
                    currentChoice = current + missingSuffix;
                    choices.Add(currentChoice);
                }
            }
            choices.AddRange(shapes);

            var popup = new PopupField<string>("Blendshape", choices, currentChoice);
            popup.AddToClassList("yucp-field-input");
            popup.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(displayedProfile, "Change Advanced Viseme Blendshape");
                var value = evt.newValue == none ? string.Empty : evt.newValue;
                if (value.EndsWith(missingSuffix, StringComparison.Ordinal))
                    value = value.Substring(0, value.Length - missingSuffix.Length);
                property.stringValue = value;
                profileSerializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(displayedProfile);
                UpdateMappingCoverage();
            });
            parent.Add(popup);
        }

        private void BuildExpertProfileFields()
        {
            BuildExpertComponentFields(expertProfileHost);

            var voice = YUCPUIToolkitHelper.CreateFoldout("Voice Thresholds", false);
            AddProfileField(voice, "quietSpeechFloor", "Quiet Speech Motion");
            AddProfileField(voice, "voiceNoiseFloor", "Noise Floor");
            AddProfileField(voice, "voiceFullScale", "Full Voice Level");
            expertProfileHost.Insert(0, voice);

            var constraints = YUCPUIToolkitHelper.CreateFoldout("Constraint Targets", false);
            AddProfileField(constraints, "bilabialClosure", "PP Closure Target");
            AddProfileField(constraints, "labiodentalBite", "FF Bite Target");
            AddProfileField(constraints, "sibilantJawMaximum", "Sibilant Jaw Maximum");
            AddProfileField(constraints, "residualMismatchFade", "Tracked Surface Yield");
            expertProfileHost.Insert(1, constraints);

            var reset = YUCPUIToolkitHelper.CreateButton(
                "Reset Profile To Defaults", ConfirmResetProfile,
                YUCPUIToolkitHelper.ButtonVariant.Danger);
            reset.name = "reset-profile";
            expertProfileHost.Add(reset);
        }

        private void BuildExpertComponentFields(VisualElement host)
        {
            host.Add(YUCPUIToolkitHelper.CreateField(parameterPrefixProp, "Parameter Prefix"));
            host.Add(YUCPUIToolkitHelper.CreateField(verboseLoggingProp, "Verbose Logging"));
            buildStatus = new VisualElement { name = "build-status" };
            host.Add(buildStatus);
        }

        private void BuildEmulatorButton(VisualElement parent)
        {
            emulatorButton = YUCPUIToolkitHelper.CreateButton(
                "Add Viseme Test Emulator", AddOrSelectEmulator,
                YUCPUIToolkitHelper.ButtonVariant.Secondary);
            emulatorButton.name = "viseme-test-emulator";
            emulatorButton.style.flexGrow = 1;
            parent.Add(emulatorButton);
        }

        private void UpdateDynamicUI()
        {
            var ownership = (AdvancedVisemeMouthOwnership)mouthOwnershipProp.enumValueIndex;
            var tracking = (AdvancedVisemeTrackingInputs)trackingInputsProp.enumValueIndex;
            var encoding = (AdvancedVisemeTrackingEncoding)trackingEncodingProp.enumValueIndex;
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            var renderer = EffectiveRenderer();
            var trackingEnabled = tracking != AdvancedVisemeTrackingInputs.Disabled;

            validation.Clear();
            if (descriptor == null)
            {
                validation.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "This component must be under a VRCAvatarDescriptor.",
                    YUCPUIToolkitHelper.MessageType.Error));
            }
            else if (ownership == AdvancedVisemeMouthOwnership.DriveLowerFace &&
                     (renderer == null || renderer.sharedMesh == null))
            {
                validation.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Drive Lower Face requires an Oculus-viseme renderer.",
                    YUCPUIToolkitHelper.MessageType.Error));
            }

            fusionContainer.style.display = trackingEnabled ? DisplayStyle.Flex : DisplayStyle.None;
            encodingContainer.style.display = tracking == AdvancedVisemeTrackingInputs.Balanced8 ||
                                              tracking == AdvancedVisemeTrackingInputs.Quality12 ||
                                              tracking == AdvancedVisemeTrackingInputs.FullTongue18
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            var canCreateToggle = trackingEnabled && tracking != AdvancedVisemeTrackingInputs.Auto;
            toggleContainer.style.display = canCreateToggle ? DisplayStyle.Flex : DisplayStyle.None;
            menuPathContainer.style.display = canCreateToggle && createToggleProp.boolValue
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            reuseContainer.style.display = tracking == AdvancedVisemeTrackingInputs.ReuseExisting ||
                                           tracking == AdvancedVisemeTrackingInputs.Auto
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            switch (tracking)
            {
                case AdvancedVisemeTrackingInputs.Balanced8:
                case AdvancedVisemeTrackingInputs.Quality12:
                case AdvancedVisemeTrackingInputs.FullTongue18:
                    trackingBudgetLabel.text = $"Up to {DisplayedTrackingBits(tracking, encoding)} synced bits";
                    break;
                case AdvancedVisemeTrackingInputs.ReuseExisting:
                    trackingBudgetLabel.text = "Reuses existing v2 parameters";
                    break;
                case AdvancedVisemeTrackingInputs.Auto:
                    trackingBudgetLabel.text = "Reuses existing inputs; otherwise speech only";
                    break;
                default:
                    trackingBudgetLabel.text = "No synced parameter cost";
                    break;
            }

            tuningMenuOptions.style.display = createTuningMenuProp.boolValue
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            UpdateTuningMenuBudget();

            if (coarticulationContainer != null)
                coarticulationContainer.style.display =
                    reconstructionModeProp.enumValueIndex == (int)AdvancedVisemeReconstructionMode.BetaCoarticulation
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;

            var canUseRigTools = displayedProfile != null && renderer != null && renderer.sharedMesh != null;
            autoMapButton?.SetEnabled(canUseRigTools);
            remapAllButton?.SetEnabled(canUseRigTools);
            analyzeFitButton?.SetEnabled(canUseRigTools && descriptor != null);
            UpdateEmulatorButton();
            UpdateMappingCoverage();
            UpdateBuildStatus();
            UpdateSimpleUI(tracking, encoding);
            UpdateInspectorMode();
        }

        private void UpdateSimpleUI(
            AdvancedVisemeTrackingInputs tracking,
            AdvancedVisemeTrackingEncoding encoding)
        {
            var trackingIndex = SimpleTrackingModeIndex(tracking);
            if (simpleTrackingPopup != null &&
                simpleTrackingPopup.index != trackingIndex)
                simpleTrackingPopup.SetValueWithoutNotify(SimpleTrackingLabels[trackingIndex]);

            simpleNaturalTransitionsToggle?.SetValueWithoutNotify(
                reconstructionModeProp.enumValueIndex ==
                (int)AdvancedVisemeReconstructionMode.BetaCoarticulation);
            simpleKeepSpeechClearToggle?.SetValueWithoutNotify(
                fusionModeProp.enumValueIndex ==
                (int)AdvancedVisemeFusionMode.PhoneticAssist);

            var trackingEnabled = tracking != AdvancedVisemeTrackingInputs.Disabled;
            simpleKeepSpeechClearToggle?.SetEnabled(trackingEnabled);
            if (simpleFaceRendererContainer != null)
                simpleFaceRendererContainer.style.display = rendererNeedsSimpleSelection()
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            if (simpleTrackingStatusLabel != null)
            {
                switch (tracking)
                {
                    case AdvancedVisemeTrackingInputs.Balanced8:
                    case AdvancedVisemeTrackingInputs.Quality12:
                    case AdvancedVisemeTrackingInputs.FullTongue18:
                        simpleTrackingStatusLabel.text =
                            $"Up to {DisplayedTrackingBits(tracking, encoding)} synced bits";
                        break;
                    case AdvancedVisemeTrackingInputs.ReuseExisting:
                        simpleTrackingStatusLabel.text = "Uses your current face-tracking setup";
                        break;
                    case AdvancedVisemeTrackingInputs.Auto:
                        simpleTrackingStatusLabel.text = "Reuses a compatible setup; otherwise speech only";
                        break;
                    default:
                        simpleTrackingStatusLabel.text = "Speech only - no synced input cost";
                        break;
                }
            }

            if (simpleMenuOptions != null)
                simpleMenuOptions.style.display = createTuningMenuProp.boolValue
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            if (simpleMenuStatusLabel != null)
                simpleMenuStatusLabel.text = createTuningMenuProp.boolValue
                    ? "Local controls - 0 synced bits"
                    : "No avatar-menu controls";

            RefreshSimpleProfileSlider(
                simpleSpeechMovementSlider, "speechMotionStrength", 1f, value => value);
            RefreshSimpleProfileSlider(
                simpleSpeechLivelinessSlider, "speechLiveliness", 0.5f, value => value);
            RefreshSimpleProfileSlider(
                simpleQuietSpeechSlider, "quietSpeechFloor", 0.55f, value => value);
            RefreshSimpleProfileSlider(
                simpleReactionSpeedSlider, "visemeResponseSeconds", 0.024f,
                ReactionSpeedFromSeconds);
            RefreshSimpleProfileSlider(
                simplePauseStabilitySlider, "speechHangoverSeconds", 0.16f,
                PauseStabilityFromSeconds);
            RefreshSimpleProfileSlider(
                simplePronunciationHelpSlider, "phoneticConstraintStrength", 1f,
                value => value);
            RefreshSimpleProfileSlider(
                simpleFaceTrackingPrioritySlider, "residualMismatchFade", 1f,
                value => value);
            RefreshSimpleProfileSlider(
                simpleTongueMotionSlider, "tongueInferenceStrength", 1f,
                value => value);

            var beta = reconstructionModeProp.enumValueIndex ==
                       (int)AdvancedVisemeReconstructionMode.BetaCoarticulation;
            simplePronunciationHelpSlider?.SetEnabled(
                trackingEnabled &&
                fusionModeProp.enumValueIndex ==
                (int)AdvancedVisemeFusionMode.PhoneticAssist);
            if (simpleFaceTrackingPrioritySlider != null)
                simpleFaceTrackingPrioritySlider.style.display = trackingEnabled
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            simpleTongueMotionSlider?.SetEnabled(
                trackingEnabled && beta);
            if (simpleTongueMotionSlider != null)
                simpleTongueMotionSlider.style.display = trackingEnabled && beta
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;

            bool rendererNeedsSimpleSelection()
            {
                if (faceRendererProp.objectReferenceValue != null) return true;
                var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
                return descriptor == null || descriptor.VisemeSkinnedMesh == null;
            }
        }

        private void UpdateTuningMenuBudget()
        {
            if (!createTuningMenuProp.boolValue)
            {
                tuningMenuBudgetLabel.text = "0 synced bits / 0 local sliders";
                return;
            }

            var sections = (AdvancedVisemeTuningMenuSections)tuningMenuSectionsProp.intValue;
            var count = AdvancedVisemeTuning.Controls.Count(control =>
                (sections & AdvancedVisemeTuning.Section(control)) != 0);
            tuningMenuBudgetLabel.text = saveTuningValuesProp.boolValue
                ? $"0 synced bits / up to {count} saved local sliders"
                : $"0 synced bits / up to {count} session local sliders";
        }

        private void UpdateMappingCoverage()
        {
            if (mappingCoverageLabel == null) return;
            var profile = displayedProfile;
            var renderer = EffectiveRenderer();
            var mesh = renderer != null ? renderer.sharedMesh : null;
            if (profile == null || mesh == null)
            {
                mappingCoverageLabel.text = "Select a profile and face renderer to inspect mapping coverage.";
                return;
            }

            var bindings = profile.articulatorBindings ?? Array.Empty<ArticulatorRigBinding>();
            var validBindings = bindings.Where(binding => binding != null).ToArray();
            var mapped = validBindings.Count(binding =>
                binding.animationOverride != null || binding.negativeAnimationOverride != null ||
                !string.IsNullOrWhiteSpace(binding.blendShapeName) &&
                mesh.GetBlendShapeIndex(binding.blendShapeName) >= 0);

            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            var visemes = descriptor != null
                ? AdvancedVisemeReconstructorProcessor.ResolveVisemeNames(descriptor, mesh)
                : Array.Empty<string>();
            var visemeCount = visemes.Count(name => !string.IsNullOrEmpty(name));
            mappingCoverageLabel.text =
                $"{mapped}/{validBindings.Length} articulators mapped  ·  {visemeCount}/{VisemeReconstructionProfile.VisemeCount} Oculus visemes found";
        }

        private void UpdateEmulatorButton()
        {
            if (emulatorButton == null) return;
            var emulator = FindEmulator();
            emulatorButton.text = emulator == null
                ? "Add Viseme Test Emulator"
                : "Select Viseme Test Emulator";
            emulatorButton.SetEnabled(emulator != null || !EditorApplication.isPlaying);
        }

        private void UpdateBuildStatus()
        {
            if (buildStatus == null) return;
            buildStatus.Clear();
            if (displayedProfile != null &&
                (displayedProfile.LastReconstructionRms > 0f || displayedProfile.LastReconstructionMaximum > 0f))
                buildStatus.Add(Caption(
                    $"Fit: RMS {displayedProfile.LastReconstructionRms:G4}, max {displayedProfile.LastReconstructionMaximum:G4}"));
            if (!string.IsNullOrEmpty(data.GetBuildSummary()))
                buildStatus.Add(Caption("Last build: " + data.GetBuildSummary()));
        }

        private int DisplayedTrackingBits(
            AdvancedVisemeTrackingInputs tracking,
            AdvancedVisemeTrackingEncoding encoding)
        {
            var bits = AdvancedVisemeMath.TrackingParameterBits(tracking, encoding);
            return createToggleProp.boolValue ? bits : Mathf.Max(0, bits - 1);
        }

        private void CreateAndAssignProfile()
        {
            EnsureProfileForSimpleEdit();
        }

        private bool EnsureProfileForSimpleEdit()
        {
            if (displayedProfile != null && profileSerializedObject != null) return true;

            serializedObject.UpdateIfRequiredOrScript();
            var assigned = profileProp.objectReferenceValue as VisemeReconstructionProfile;
            if (assigned == null)
            {
                Undo.RecordObject(data, "Create Viseme Reconstruction Profile");
                assigned = CreateProfileAsset(data);
                profileProp.objectReferenceValue = assigned;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
            }

            RebuildProfileSections(assigned, true);
            UpdateDynamicUI();
            return displayedProfile != null && profileSerializedObject != null;
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
            AutoMap(data, overwriteExisting);
            serializedObject.UpdateIfRequiredOrScript();
            if (displayedProfile != null) RebuildProfileSections(displayedProfile, true);
            UpdateDynamicUI();
        }

        private void ConfirmRemapAll()
        {
            if (!EditorUtility.DisplayDialog(
                    "Remap every articulator?",
                    "This replaces tailored blendshape mappings wherever an automatic match is available.",
                    "Remap All", "Cancel")) return;
            ApplyAutoMap(true);
        }

        private static void AutoMap(AdvancedVisemeReconstructorData data, bool overwriteExisting)
        {
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
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
        }

        private void AnalyzeFit()
        {
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            var renderer = EffectiveRenderer();
            if (descriptor == null || renderer == null || renderer.sharedMesh == null || data.profile == null) return;

            data.profile.EnsureDefaults();
            var names = AdvancedVisemeReconstructorProcessor.ResolveVisemeNames(descriptor, renderer.sharedMesh);
            var indices = new int[names.Length];
            for (var i = 0; i < names.Length; i++)
                indices[i] = string.IsNullOrEmpty(names[i]) ? -1 : renderer.sharedMesh.GetBlendShapeIndex(names[i]);
            var resolved = AdvancedVisemeReconstructorProcessor.ResolveArticulatorBlendShapes(
                renderer.sharedMesh, data.profile);
            var basis = AdvancedVisemeReconstructorProcessor.BuildCalibrationBasis(renderer.sharedMesh, resolved);
            var result = AdvancedVisemeMeshCalibrator.Build(renderer.sharedMesh, indices, basis);
            if (!result.success)
            {
                EditorUtility.DisplayDialog("Fit analysis failed", result.error, "OK");
                return;
            }

            Undo.RecordObject(data.profile, "Analyze Advanced Viseme Fit");
            data.profile.SetDiagnostics(result.fitRms, result.fitMaximum);
            EditorUtility.SetDirty(data.profile);
            UnityEngine.Object.DestroyImmediate(result.mesh);
            UpdateBuildStatus();
            EditorUtility.DisplayDialog(
                "Fit analysis complete",
                $"RMS: {result.fitRms:G4}\nMaximum: {result.fitMaximum:G4}",
                "OK");
        }

        private void AddOrSelectEmulator()
        {
            var emulator = FindEmulator();
            if (emulator == null)
            {
                emulator = Undo.AddComponent<VisemeTestEmulatorData>(data.gameObject);
                EditorUtility.SetDirty(data.gameObject);
            }
            Selection.activeGameObject = emulator.gameObject;
            EditorGUIUtility.PingObject(emulator);
            UpdateEmulatorButton();
        }

        private VisemeTestEmulatorData FindEmulator()
        {
            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            return descriptor != null
                ? descriptor.GetComponentInChildren<VisemeTestEmulatorData>(true)
                : data.GetComponent<VisemeTestEmulatorData>();
        }

        private void ConfirmResetProfile()
        {
            if (displayedProfile == null || !EditorUtility.DisplayDialog(
                    "Reset reconstruction profile?",
                    "This replaces all tuning, viseme poses, and rig mappings in the selected profile with YUCP defaults.",
                    "Reset Profile", "Cancel")) return;

            Undo.RecordObject(displayedProfile, "Reset Viseme Reconstruction Profile");
            displayedProfile.ResetToDefaults();
            EditorUtility.SetDirty(displayedProfile);
            RebuildProfileSections(displayedProfile, true);
            UpdateDynamicUI();
        }

        private SkinnedMeshRenderer EffectiveRenderer()
        {
            var explicitRenderer = faceRendererProp != null
                ? faceRendererProp.objectReferenceValue as SkinnedMeshRenderer
                : data.faceRenderer;
            return explicitRenderer != null
                ? explicitRenderer
                : data.GetComponentInParent<VRCAvatarDescriptor>()?.VisemeSkinnedMesh;
        }

        private void AddProfileField(VisualElement parent, string propertyName, string label)
        {
            if (profileSerializedObject == null) return;
            AddBoundField(parent, profileSerializedObject.FindProperty(propertyName), label);
        }

        private static VisualElement CreateBoundField(SerializedProperty property, string label)
        {
            if (property == null)
                return YUCPUIToolkitHelper.CreateHelpBox(
                    $"Missing profile property: {label}", YUCPUIToolkitHelper.MessageType.Error);
            var field = YUCPUIToolkitHelper.CreateField(property, label);
            field.BindProperty(property);
            return field;
        }

        private static void AddBoundField(VisualElement parent, SerializedProperty property, string label)
        {
            parent.Add(CreateBoundField(property, label));
        }

        private Foldout RememberedFoldout(string title, string name, bool defaultExpanded)
        {
            var key = $"YUCP_AVR_{name}_{data.GetInstanceID()}";
            var foldout = YUCPUIToolkitHelper.CreateFoldout(
                title, SessionState.GetBool(key, defaultExpanded));
            foldout.name = name;
            foldout.RegisterValueChangedCallback(evt => SessionState.SetBool(key, evt.newValue));
            return foldout;
        }

        private static VisualElement ButtonRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 4;
            row.style.marginBottom = 4;
            return row;
        }

        private static Button RowButton(
            VisualElement row,
            string text,
            Action action,
            YUCPUIToolkitHelper.ButtonVariant variant,
            string name)
        {
            var button = YUCPUIToolkitHelper.CreateButton(text, action, variant);
            button.name = name;
            button.style.flexGrow = 1;
            button.style.marginLeft = 2;
            button.style.marginRight = 2;
            row.Add(button);
            return button;
        }

        private static Label Caption(string text, string name = null)
        {
            var label = new Label(text) { name = name };
            label.AddToClassList("yucp-text-caption");
            label.style.marginTop = 4;
            label.style.marginBottom = 4;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static string Nicify(string value) => ObjectNames.NicifyVariableName(value);

        private static string Sanitize(string value)
        {
            var chars = (value ?? "Avatar").ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_') chars[i] = '_';
            return new string(chars);
        }
    }
}
