using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(VisemeTestEmulatorData))]
    public sealed class VisemeTestEmulatorDataEditor : UnityEditor.Editor
    {
        internal const string OculusLipSyncDownloadUrl =
            "https://developers.meta.com/horizon/downloads/package/oculus-lipsync-unity/";

        private VisemeTestEmulatorData data;
        private SerializedProperty inputProp;
        private SerializedProperty microphoneProp;
        private SerializedProperty backendProp;
        private SerializedProperty automaticGainProp;
        private SerializedProperty gainProp;
        private SerializedProperty gateProp;
        private SerializedProperty manualVisemeProp;
        private SerializedProperty manualVoiceProp;
        private SerializedProperty gestureManagerProp;
        private SerializedProperty animatorProp;
        private SerializedProperty startWithPlayModeProp;
        private PopupField<string> microphonePopup;
        private VisualElement microphoneContainer;
        private VisualElement microphonePickerRoot;
        private VisualElement manualContainer;
        private VisualElement validation;
        private Button previewButton;
        private Button recordButton;
        private Label recordLabel;
        private ProgressBar voiceMeter;
        private Label liveLabel;
        private int previousInput;
        private int previousBackend;
        private string previousMicrophone;

        private void OnEnable()
        {
            data = (VisemeTestEmulatorData)target;
            inputProp = serializedObject.FindProperty("input");
            microphoneProp = serializedObject.FindProperty("microphoneDevice");
            backendProp = serializedObject.FindProperty("analysisBackend");
            automaticGainProp = serializedObject.FindProperty("automaticGain");
            gainProp = serializedObject.FindProperty("microphoneGain");
            gateProp = serializedObject.FindProperty("noiseGate");
            manualVisemeProp = serializedObject.FindProperty("manualViseme");
            manualVoiceProp = serializedObject.FindProperty("manualVoice");
            gestureManagerProp = serializedObject.FindProperty("driveGestureManager");
            animatorProp = serializedObject.FindProperty("driveAnimator");
            startWithPlayModeProp = serializedObject.FindProperty("startWithPlayMode");
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Viseme Test Emulator"));

            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(VisemeTestEmulatorData));
            if (supportBanner != null) root.Add(supportBanner);

            validation = new VisualElement();
            root.Add(validation);

            var sourceCard = YUCPUIToolkitHelper.CreateCard("Input", "Talk into a microphone or choose a viseme.");
            var source = YUCPUIToolkitHelper.GetCardContent(sourceCard);
            source.Add(YUCPUIToolkitHelper.CreateField(inputProp, "Source"));

            microphoneContainer = new VisualElement();
            microphonePickerRoot = new VisualElement();
            microphoneContainer.Add(microphonePickerRoot);
            BuildMicrophonePicker();
            microphoneContainer.Add(YUCPUIToolkitHelper.CreateField(backendProp, "Classifier"));
            microphoneContainer.Add(YUCPUIToolkitHelper.CreateField(automaticGainProp, "Automatic Gain"));
            source.Add(microphoneContainer);
            root.Add(sourceCard);

            var previewCard = YUCPUIToolkitHelper.CreateCard("Preview", "Applies the avatar descriptor exactly as VRChat does.");
            var preview = YUCPUIToolkitHelper.GetCardContent(previewCard);
            previewButton = YUCPUIToolkitHelper.CreateButton("Start Preview", TogglePreview, YUCPUIToolkitHelper.ButtonVariant.Primary);
            previewButton.style.height = 30;
            preview.Add(previewButton);

            liveLabel = new Label("Stopped");
            liveLabel.style.marginTop = 5;
            liveLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            preview.Add(liveLabel);

            voiceMeter = new ProgressBar { title = "Voice", lowValue = 0f, highValue = 1f, value = 0f };
            voiceMeter.style.marginTop = 4;
            preview.Add(voiceMeter);

            // Recording captures the exact Oculus teacher weights this session
            // already computes together with whatever reconstruction each
            // avatar publishes, so the two are directly comparable offline.
            recordButton = YUCPUIToolkitHelper.CreateButton(
                "Record Comparison", ToggleRecording,
                YUCPUIToolkitHelper.ButtonVariant.Secondary);
            recordButton.style.height = 26;
            recordButton.style.marginTop = 6;
            preview.Add(recordButton);

            recordLabel = new Label(string.Empty);
            recordLabel.style.marginTop = 3;
            recordLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            recordLabel.style.whiteSpace = WhiteSpace.Normal;
            preview.Add(recordLabel);

            manualContainer = BuildManualControls();
            preview.Add(manualContainer);
            root.Add(previewCard);

            var advanced = YUCPUIToolkitHelper.CreateFoldout("Advanced", false);
            advanced.Add(YUCPUIToolkitHelper.CreateField(gainProp, "Microphone Gain"));
            advanced.Add(YUCPUIToolkitHelper.CreateField(gateProp, "Noise Gate"));
            advanced.Add(YUCPUIToolkitHelper.CreateField(gestureManagerProp, "Gesture Manager"));
            advanced.Add(YUCPUIToolkitHelper.CreateField(animatorProp, "Animator Parameters"));
            advanced.Add(YUCPUIToolkitHelper.CreateField(startWithPlayModeProp, "Start With Play Mode"));
            root.Add(advanced);

            previousInput = inputProp.enumValueIndex;
            previousBackend = backendProp.enumValueIndex;
            previousMicrophone = microphoneProp.stringValue;
            UpdateUI();

            root.schedule.Execute(() =>
            {
                if (target == null) return;
                serializedObject.UpdateIfRequiredOrScript();
                if (previousInput != inputProp.enumValueIndex || previousBackend != backendProp.enumValueIndex ||
                    previousMicrophone != microphoneProp.stringValue)
                {
                    previousInput = inputProp.enumValueIndex;
                    previousBackend = backendProp.enumValueIndex;
                    previousMicrophone = microphoneProp.stringValue;
                    VisemeTestPreviewSession.Restart(data);
                }
                VisemeTestPreviewSession.ApplyManual(data);
                UpdateUI();
            }).Every(75);
            return root;
        }

        private void BuildMicrophonePicker()
        {
            microphonePickerRoot.Clear();
            var devices = new List<string> { "System Default" };
            devices.AddRange(Microphone.devices.Where(device => !string.IsNullOrWhiteSpace(device)));
            var selected = string.IsNullOrEmpty(microphoneProp.stringValue) ? "System Default" : microphoneProp.stringValue;
            if (!devices.Contains(selected))
            {
                selected += " (missing)";
                devices.Add(selected);
            }

            microphonePopup = new PopupField<string>("Microphone", devices,
                Mathf.Max(0, devices.IndexOf(selected)));
            microphonePopup.RegisterValueChangedCallback(evt =>
            {
                serializedObject.Update();
                microphoneProp.stringValue = evt.newValue == "System Default" ? string.Empty : evt.newValue.Replace(" (missing)", string.Empty);
                serializedObject.ApplyModifiedProperties();
            });
            microphonePickerRoot.Add(microphonePopup);

            var refresh = YUCPUIToolkitHelper.CreateButton("Refresh Microphones", () =>
            {
                serializedObject.Update();
                BuildMicrophonePicker();
            }, YUCPUIToolkitHelper.ButtonVariant.Ghost);
            refresh.style.marginTop = 3;
            microphonePickerRoot.Add(refresh);
        }

        private VisualElement BuildManualControls()
        {
            var container = new VisualElement();
            container.style.marginTop = 8;
            container.Add(YUCPUIToolkitHelper.CreateField(manualVoiceProp, "Voice"));

            for (var rowIndex = 0; rowIndex < 3; rowIndex++)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                for (var column = 0; column < 5; column++)
                {
                    var index = rowIndex * 5 + column;
                    var captured = index;
                    var button = YUCPUIToolkitHelper.CreateButton(
                        VisemeTestMath.VisemeNames[index],
                        () =>
                        {
                            serializedObject.Update();
                            manualVisemeProp.intValue = captured;
                            serializedObject.ApplyModifiedProperties();
                            VisemeTestPreviewSession.ApplyManual(data);
                        },
                        YUCPUIToolkitHelper.ButtonVariant.Secondary);
                    button.style.flexGrow = 1;
                    button.style.marginLeft = column == 0 ? 0 : 2;
                    button.style.marginBottom = 2;
                    row.Add(button);
                }
                container.Add(row);
            }
            return container;
        }

        private void ToggleRecording()
        {
            if (VisemeTestRecorder.IsRecording)
            {
                var path = VisemeTestRecorder.Stop();
                if (!string.IsNullOrEmpty(path))
                    Debug.Log("[YUCP Viseme Test] recorded comparison -> " + path);
            }
            else
            {
                VisemeTestRecorder.Start(data);
            }
        }

        private void TogglePreview()
        {
            if (VisemeTestPreviewSession.IsRunning(data))
            {
                VisemeTestPreviewSession.Stop(data);
                UpdateUI();
                return;
            }

            serializedObject.ApplyModifiedProperties();
            if (!VisemeTestPreviewSession.Start(data, out var error))
                EditorUtility.DisplayDialog("Viseme preview could not start", error, "OK");
            UpdateUI();
        }

        private void UpdateUI()
        {
            if (data == null) return;
            var microphoneMode = inputProp.enumValueIndex == (int)VisemeTestInput.Microphone;
            microphoneContainer.style.display = microphoneMode ? DisplayStyle.Flex : DisplayStyle.None;
            manualContainer.style.display = microphoneMode ? DisplayStyle.None : DisplayStyle.Flex;

            validation.Clear();
            if (!VisemeTestPreviewSession.TryResolveDescriptors(
                    data,
                    out var descriptors,
                    out var targetSource,
                    out var targetError))
            {
                validation.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    targetError,
                    YUCPUIToolkitHelper.MessageType.Error));
            }
            else
            {
                if (targetSource != VisemeTestPreviewSession.DescriptorTargetSource.Parent)
                {
                    var targetNames = string.Join(", ", descriptors.Select(descriptor => $"'{descriptor.name}'"));
                    var targetDescription = targetSource == VisemeTestPreviewSession.DescriptorTargetSource.GestureManager
                        ? $"{descriptors.Length} Gesture Manager avatar target{(descriptors.Length == 1 ? string.Empty : "s")}"
                        : "the only active avatar in this scene";
                    validation.Add(YUCPUIToolkitHelper.CreateHelpBox(
                        $"Automatically targeting {targetNames} from {targetDescription}.",
                        YUCPUIToolkitHelper.MessageType.Info));
                }

                foreach (var descriptor in descriptors)
                {
                    var message = ValidateDescriptor(descriptor);
                    if (!string.IsNullOrEmpty(message))
                        validation.Add(YUCPUIToolkitHelper.CreateHelpBox(
                            descriptors.Length > 1 ? $"{descriptor.name}: {message}" : message,
                            YUCPUIToolkitHelper.MessageType.Warning));
                }
            }

            if (microphoneMode && backendProp.enumValueIndex != (int)VisemeTestAnalysisBackend.BuiltIn)
            {
                validation.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    VisemeTestPreviewSession.ExactBackendStatus(),
                    YUCPUIToolkitHelper.MessageType.Info));

                if (!VisemeTestPreviewSession.OculusBridge.IsAvailable(out _))
                {
                    var installButton = YUCPUIToolkitHelper.CreateButton(
                        "Install Oculus LipSync",
                        () => Application.OpenURL(OculusLipSyncDownloadUrl),
                        YUCPUIToolkitHelper.ButtonVariant.Secondary);
                    installButton.name = "oculus-lipsync-install";
                    installButton.style.marginTop = 3;
                    validation.Add(installButton);
                }
            }

            var running = VisemeTestPreviewSession.IsRunning(data);
            previewButton.text = running ? "Stop & Restore" : "Start Preview";
            if (recordButton != null)
            {
                recordButton.SetEnabled(running || VisemeTestRecorder.IsRecording);
                recordButton.text = VisemeTestRecorder.IsRecording
                    ? "Stop Recording"
                    : "Record Comparison";
                if (VisemeTestRecorder.IsRecording)
                    recordLabel.text =
                        $"recording {VisemeTestRecorder.DurationSeconds:F1}s  " +
                        $"({VisemeTestRecorder.FrameCount} frames)\n" +
                        VisemeTestRecorder.SubjectSummary;
                else if (!string.IsNullOrEmpty(VisemeTestRecorder.LastWritePath))
                    recordLabel.text = "Saved " +
                        System.IO.Path.GetFileName(VisemeTestRecorder.LastWritePath);
                else
                    recordLabel.text = string.Empty;
            }
            var state = VisemeTestPreviewSession.GetState(data);
            if (state == null)
            {
                liveLabel.text = "Stopped";
                voiceMeter.value = 0f;
                voiceMeter.title = "Voice";
            }
            else
            {
                var veryQuiet = microphoneMode &&
                                state.currentVoice < 0.025f &&
                                state.currentInputRms < Mathf.Max(
                                    0.00001f, data.noiseGate * 0.1f);
                liveLabel.text = veryQuiet
                    ? "Listening · input is very quiet"
                    : $"{VisemeTestMath.VisemeNames[state.currentViseme]}  ·  {state.engineName}";
                voiceMeter.value = state.currentVoice;
                voiceMeter.title = data.automaticGain && state.automaticInputGain > 1.25f
                    ? $"Voice {state.currentVoice:0.00} · auto boost {state.automaticInputGain:0.0}×"
                    : $"Voice {state.currentVoice:0.00}";
            }
        }

        private static string ValidateDescriptor(VRCAvatarDescriptor descriptor)
        {
            switch (descriptor.lipSync)
            {
                case VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape:
                    if (descriptor.VisemeSkinnedMesh == null) return "The descriptor has no viseme renderer.";
                    if (descriptor.VisemeBlendShapes == null || descriptor.VisemeBlendShapes.Length < 15)
                        return "The descriptor does not contain all 15 Oculus viseme mappings.";
                    break;
                case VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape:
                    if (descriptor.VisemeSkinnedMesh == null || string.IsNullOrEmpty(descriptor.MouthOpenBlendShapeName))
                        return "The descriptor's jaw-flap blendshape is not configured.";
                    break;
                case VRC_AvatarDescriptor.LipSyncStyle.JawFlapBone:
                    if (descriptor.lipSyncJawBone == null) return "The descriptor's jaw bone is not configured.";
                    break;
                case VRC_AvatarDescriptor.LipSyncStyle.Default:
                    return "Lip Sync is still set to Default. Run Auto Detect on the Avatar Descriptor first.";
            }
            return string.Empty;
        }
    }
}
