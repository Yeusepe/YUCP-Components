using System.Collections.Generic;
using System.Linq;
using UnityEditor;
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

        private const string SystemDefault = "System Default";
        private const string Missing = " (Missing)";

        public override VisualElement CreateInspectorGUI()
        {
            var data = (VisemeTestEmulatorData)target;
            var ui = YUCPInspector.Create(this, "Viseme Test Emulator");
            ui.Status(() => Targets(data));

            var input = ui.Section("Input");
            ui.Segmented(input, "input", "Source", "Microphone", "Manual");
            BuildMicrophone(ui, data, input);
            BuildManual(ui, data, input);

            BuildPreview(ui, data);

            var advanced = ui.Advanced();
            ui.Field(advanced, "microphoneGain");
            ui.Field(advanced, "noiseGate");
            ui.Field(advanced, "driveGestureManager");
            ui.Field(advanced, "driveAnimator");

            // A running preview reads these when it starts; restart it when they change.
            var shownSource = (data.input, data.analysisBackend, data.microphoneDevice);
            ui.OnRefresh(() =>
            {
                var source = (data.input, data.analysisBackend, data.microphoneDevice);
                if (source.Equals(shownSource)) return;
                shownSource = source;
                VisemeTestPreviewSession.Restart(data);
            });
            return ui.Root;
        }

        private static void BuildMicrophone(YUCPInspector ui, VisemeTestEmulatorData data, VisualElement parent)
        {
            var microphone = new VisualElement();
            parent.Add(microphone);
            var property = ui.Object.FindProperty("microphoneDevice");
            var picker = new DropdownField("Microphone") { tooltip = property.tooltip };
            picker.AddToClassList("yucp-field-input");
            picker.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            picker.RegisterValueChangedCallback(e =>
            {
                ui.Object.Update();
                ui.Object.FindProperty("microphoneDevice").stringValue =
                    e.newValue == SystemDefault ? string.Empty : e.newValue.Replace(Missing, string.Empty);
                ui.Object.ApplyModifiedProperties();
            });
            microphone.Add(picker);

            List<string> devices = null;
            void Sync(bool rescan)
            {
                if (rescan || devices == null)
                    devices = new[] { SystemDefault }.Concat(Microphone.devices.Where(d => !string.IsNullOrWhiteSpace(d))).ToList();
                var current = string.IsNullOrEmpty(data.microphoneDevice) ? SystemDefault : data.microphoneDevice;
                var shown = devices.Contains(current) ? current : current + Missing;
                var choices = devices.Contains(shown) ? devices : devices.Append(shown).ToList();
                if (picker.choices == null || !picker.choices.SequenceEqual(choices)) picker.choices = choices;
                if (picker.value != shown) picker.SetValueWithoutNotify(shown);
            }
            ui.OnRefresh(() => Sync(false));
            Sync(false);
            ui.Button(microphone, "Refresh Microphones", () => Sync(true), tooltip: "List the microphones connected now.");

            ui.Field(microphone, "analysisBackend", "Classifier");
            // The installed packages only change with a domain reload, which rebuilds this inspector.
            var oculus = VisemeTestPreviewSession.OculusBridge.IsAvailable(out _);
            ui.Messages(microphone, () => oculus || data.analysisBackend == VisemeTestAnalysisBackend.BuiltIn
                ? null
                : new[]
                {
                    data.analysisBackend == VisemeTestAnalysisBackend.OculusLipSync
                        ? YUCPInspector.Message.Error("Install Oculus LipSync, or choose Auto.")
                        : YUCPInspector.Message.Info("Oculus LipSync isn't installed, so the preview uses YUCP's approximate classifier.")
                });
            ui.ShowWhen(ui.Button(microphone, "Install Oculus LipSync…", () => Application.OpenURL(OculusLipSyncDownloadUrl),
                    tooltip: "Open the Oculus LipSync download page."),
                () => !oculus && data.analysisBackend != VisemeTestAnalysisBackend.BuiltIn);
            ui.Field(microphone, "automaticGain");
            ui.ShowWhen(microphone, () => data.input == VisemeTestInput.Microphone);
        }

        private static void BuildManual(YUCPInspector ui, VisemeTestEmulatorData data, VisualElement parent)
        {
            var manual = new VisualElement();
            parent.Add(manual);
            var viseme = new DropdownField("Viseme", VisemeTestMath.VisemeNames.ToList(), 0)
            {
                tooltip = ui.Object.FindProperty("manualViseme").tooltip
            };
            viseme.AddToClassList("yucp-field-input");
            viseme.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            viseme.RegisterValueChangedCallback(_ =>
            {
                ui.Object.Update();
                ui.Object.FindProperty("manualViseme").intValue = viseme.index;
                ui.Object.ApplyModifiedProperties();
            });
            manual.Add(viseme);
            void Sync()
            {
                var index = Mathf.Clamp(data.manualViseme, 0, VisemeTestMath.VisemeNames.Length - 1);
                if (viseme.index != index) viseme.SetValueWithoutNotify(VisemeTestMath.VisemeNames[index]);
            }
            ui.OnRefresh(Sync);
            Sync();
            ui.Field(manual, "manualVoice", "Voice");
            ui.ShowWhen(manual, () => data.input == VisemeTestInput.Manual);
        }

        /// <summary>Start and stop, then a live readout built once and shown while the preview runs.</summary>
        private static void BuildPreview(YUCPInspector ui, VisemeTestEmulatorData data)
        {
            var preview = ui.Section("Preview");
            ui.Field(preview, "startWithPlayMode", "Start in Play Mode");
            string startError = null;
            var start = ui.Button(preview, "Start Preview", () =>
            {
                if (VisemeTestPreviewSession.IsRunning(data))
                {
                    VisemeTestPreviewSession.Stop(data);
                    startError = null;
                }
                else if (!VisemeTestPreviewSession.Start(data, out var error)) startError = error;
                else startError = null;
                ui.Refresh();
            }, YUCPUIToolkitHelper.ButtonVariant.Primary, "Applies the avatar's lip sync the way VRChat does.");
            ui.OnRefresh(() =>
            {
                var text = VisemeTestPreviewSession.IsRunning(data) ? "Stop Preview" : "Start Preview";
                if (start.text != text) start.text = text;
            });
            ui.Messages(preview, () => new[] { YUCPInspector.Message.Error(startError) });

            var live = new VisualElement();
            preview.Add(live);
            var output = ui.InfoRow(live, "Output", () => Output(data)).Q<Label>(className: "yucp-info-value");
            var meter = new ProgressBar { lowValue = 0f, highValue = 1f };
            meter.AddToClassList("yucp-progress-bar");
            live.Add(meter);
            var record = ui.Button(live, "Record Comparison", () =>
            {
                if (!VisemeTestRecorder.IsRecording) VisemeTestRecorder.Start(data);
                else
                {
                    var path = VisemeTestRecorder.Stop();
                    if (!string.IsNullOrEmpty(path)) Debug.Log("[YUCP Viseme Test] recorded comparison -> " + path);
                }
                ui.Refresh();
            }, tooltip: "Records the classifier and the avatar's blend shapes to a CSV file in VisemeCapture.");
            var recording = ui.Caption(live, Recording);
            ui.ShowWhen(live, () => VisemeTestPreviewSession.IsRunning(data) || VisemeTestRecorder.IsRecording);

            // Microphone frames arrive many times a second: update only the readout, not the whole inspector.
            void UpdateLive()
            {
                var text = Output(data);
                if (output.text != text) output.text = text;
                var state = VisemeTestPreviewSession.GetState(data);
                var voice = state?.currentVoice ?? 0f;
                meter.value = voice;
                meter.title = state != null && data.automaticGain && state.automaticInputGain > 1.25f
                    ? $"Voice {voice:0.00}, auto boost {state.automaticInputGain:0.0}×"
                    : $"Voice {voice:0.00}";
                var recordText = VisemeTestRecorder.IsRecording ? "Stop Recording" : "Record Comparison";
                if (record.text != recordText) record.text = recordText;
                var caption = Recording();
                if (recording.text != caption) recording.text = caption;
            }
            void OnFrame(VisemeTestPreviewSession.AnalysisSample sample)
            {
                if (sample.source == data) UpdateLive();
            }
            ui.OnRefresh(UpdateLive);
            UpdateLive();
            ui.Root.RegisterCallback<AttachToPanelEvent>(_ => VisemeTestPreviewSession.AnalysisFrameProcessed += OnFrame);
            ui.Root.RegisterCallback<DetachFromPanelEvent>(_ => VisemeTestPreviewSession.AnalysisFrameProcessed -= OnFrame);
        }

        private static string Recording() => VisemeTestRecorder.IsRecording
            ? $"Recording: {VisemeTestRecorder.DurationSeconds:F1} s, {VisemeTestRecorder.FrameCount} frames. {VisemeTestRecorder.SubjectSummary}"
            : string.IsNullOrEmpty(VisemeTestRecorder.LastWritePath)
                ? ""
                : "Saved " + System.IO.Path.GetFileName(VisemeTestRecorder.LastWritePath) + ".";

        private static string Output(VisemeTestEmulatorData data)
        {
            var state = VisemeTestPreviewSession.GetState(data);
            if (state == null) return "Stopped";
            var quiet = data.input == VisemeTestInput.Microphone &&
                        state.currentVoice < 0.025f &&
                        state.currentInputRms < Mathf.Max(0.00001f, data.noiseGate * 0.1f);
            return quiet
                ? "Listening. The input is very quiet."
                : $"{VisemeTestMath.VisemeNames[state.currentViseme]} ({state.engineName})";
        }

        /// <summary>Which avatars the preview drives, and what on them would stop it from looking right.</summary>
        private static IEnumerable<YUCPInspector.Message> Targets(VisemeTestEmulatorData data)
        {
            if (!VisemeTestPreviewSession.TryResolveDescriptors(data, out var descriptors, out var source, out var error))
            {
                yield return YUCPInspector.Message.Error(error);
                yield break;
            }
            if (source != VisemeTestPreviewSession.DescriptorTargetSource.Parent)
            {
                var names = string.Join(", ", descriptors.Select(descriptor => descriptor.name));
                yield return YUCPInspector.Message.Info(source == VisemeTestPreviewSession.DescriptorTargetSource.GestureManager
                    ? $"Previews {names}, targeted by Gesture Manager."
                    : $"Previews {names}, the only avatar in the scene.");
            }
            foreach (var descriptor in descriptors)
            {
                var message = ValidateDescriptor(descriptor);
                if (!string.IsNullOrEmpty(message))
                    yield return YUCPInspector.Message.Warning(descriptors.Length > 1 ? $"{descriptor.name}: {message}" : message);
            }
        }

        private static string ValidateDescriptor(VRCAvatarDescriptor descriptor)
        {
            switch (descriptor.lipSync)
            {
                case VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape:
                    if (descriptor.VisemeSkinnedMesh == null) return "Assign the avatar's viseme mesh.";
                    if (descriptor.VisemeBlendShapes == null || descriptor.VisemeBlendShapes.Length < 15)
                        return "Assign all 15 viseme blend shapes on the avatar.";
                    break;
                case VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape:
                    if (descriptor.VisemeSkinnedMesh == null || string.IsNullOrEmpty(descriptor.MouthOpenBlendShapeName))
                        return "Assign the avatar's jaw flap blend shape.";
                    break;
                case VRC_AvatarDescriptor.LipSyncStyle.JawFlapBone:
                    if (descriptor.lipSyncJawBone == null) return "Assign the avatar's jaw bone.";
                    break;
                case VRC_AvatarDescriptor.LipSyncStyle.Default:
                    return "Lip Sync is set to Default. Choose Auto Detect on the avatar to set it.";
            }
            return string.Empty;
        }
    }
}
