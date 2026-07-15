using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace YUCP.Components.Editor
{
    [InitializeOnLoad]
    internal static class VisemeTestPreviewSession
    {
        internal const int PreviewMicrophoneBufferSeconds = 2;
        internal const int LosslessMicrophoneBufferSeconds = 32;

        internal enum DescriptorTargetSource
        {
            Parent,
            GestureManager,
            SoleSceneAvatar
        }

        internal readonly struct AnalysisSample
        {
            internal readonly VisemeTestEmulatorData source;
            internal readonly int viseme;
            internal readonly float voice;
            internal readonly long sampleClock;
            internal readonly int sampleRate;
            internal readonly string engineName;

            internal AnalysisSample(
                VisemeTestEmulatorData source,
                int viseme,
                float voice,
                long sampleClock,
                int sampleRate,
                string engineName)
            {
                this.source = source;
                this.viseme = viseme;
                this.voice = voice;
                this.sampleClock = sampleClock;
                this.sampleRate = sampleRate;
                this.engineName = engineName;
            }

            internal double timeSeconds => sampleRate > 0 ? sampleClock / (double)sampleRate : 0d;
        }

        internal sealed class State
        {
            internal VisemeTestEmulatorData data;
            internal VRCAvatarDescriptor descriptor;
            internal AudioClip microphoneClip;
            internal string microphone;
            internal int sampleRate = 48000;
            internal int lastMicrophonePosition;
            internal readonly Queue<float> pendingSamples = new Queue<float>();
            internal readonly float[] analysisFrame = new float[1024];
            internal readonly Dictionary<int, float> originalBlendShapes = new Dictionary<int, float>();
            internal Quaternion originalJawRotation;
            internal bool hasJawSnapshot;
            internal Animator animator;
            internal bool hasVisemeParameter;
            internal bool hasVoiceParameter;
            internal int originalViseme;
            internal float originalVoice;
            internal int currentViseme;
            internal float currentVoice;
            internal readonly float[] currentWeights = new float[VisemeTestMath.VisemeCount];
            internal string engineName = "Manual";
            internal OculusBridge oculus;
            internal double lastUpdateTime;
            internal long analysisSampleClock;
            internal float currentInputRms;
            internal float noiseFloorRms;
            internal float effectiveNoiseGate;
            internal float automaticInputGain = 1f;
            internal int speechHangoverFrames;
            internal int noiseCalibrationFrames = 12;
        }

        private static readonly Dictionary<int, State> Sessions = new Dictionary<int, State>();
        private static readonly HashSet<int> AutoStartAttempted = new HashSet<int>();
        private static readonly HashSet<int> LosslessAnalysisSources = new HashSet<int>();

        /// <summary>
        /// Raised once for every analyzed audio block. The sample clock, rather than
        /// EditorApplication.update, makes enrollment recordings independent of how
        /// many microphone blocks Unity delivers in one editor tick.
        /// </summary>
        internal static event Action<AnalysisSample> AnalysisFrameProcessed;

        /// <summary>
        /// Enrollment uses every classifier block. Ordinary live preview remains
        /// bounded so a long editor stall cannot monopolize the main thread.
        /// </summary>
        internal static IDisposable BeginLosslessAnalysis(VisemeTestEmulatorData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var id = data.GetInstanceID();
            LosslessAnalysisSources.Add(id);
            return new LosslessAnalysisScope(id);
        }

        static VisemeTestPreviewSession()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
            EditorApplication.quitting += StopAll;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            TrackingControlBridge.Initialize();
        }

        internal static bool IsRunning(VisemeTestEmulatorData data) =>
            data != null && Sessions.ContainsKey(data.GetInstanceID());

        internal static State GetState(VisemeTestEmulatorData data)
        {
            if (data == null) return null;
            Sessions.TryGetValue(data.GetInstanceID(), out var state);
            return state;
        }

        internal static string ExactBackendStatus()
        {
            return OculusBridge.IsAvailable(out var detail)
                ? "Oculus LipSync detected — microphone classification uses the same engine family as VRChat."
                : "Approximate enrollment: Oculus LipSync is not installed, so microphone preview will use YUCP's local fallback. Manual output remains exact." +
                  (string.IsNullOrEmpty(detail) ? string.Empty : " " + detail);
        }

        internal static bool Start(VisemeTestEmulatorData data, out string error)
        {
            error = string.Empty;
            if (data == null) { error = "Component is missing."; return false; }
            Stop(data);

            if (!TryResolveDescriptor(data, out var descriptor, out _, out error)) return false;

            var state = new State
            {
                data = data,
                descriptor = descriptor,
                animator = descriptor.GetComponent<Animator>(),
                lastUpdateTime = EditorApplication.timeSinceStartup
            };
            Snapshot(state);

            if (data.input == VisemeTestInput.Microphone)
            {
                if (!StartMicrophone(state, out error))
                {
                    Restore(state);
                    return false;
                }

                if (data.analysisBackend != VisemeTestAnalysisBackend.BuiltIn)
                {
                    state.oculus = OculusBridge.TryCreate(state.sampleRate, state.analysisFrame.Length, out var bridgeError);
                    if (state.oculus == null && data.analysisBackend == VisemeTestAnalysisBackend.OculusLipSync)
                    {
                        StopMicrophone(state);
                        Restore(state);
                        error = bridgeError;
                        return false;
                    }
                }
                state.engineName = state.oculus != null ? "Oculus LipSync" : "YUCP fallback";
            }

            Sessions[data.GetInstanceID()] = state;
            ApplyFrame(state, data.manualViseme, data.input == VisemeTestInput.Manual ? data.manualVoice : 0f);
            SceneView.RepaintAll();
            return true;
        }

        internal static bool TryResolveDescriptor(
            VisemeTestEmulatorData data,
            out VRCAvatarDescriptor descriptor,
            out DescriptorTargetSource source,
            out string error)
        {
            descriptor = null;
            source = DescriptorTargetSource.Parent;
            error = string.Empty;
            if (data == null)
            {
                error = "Component is missing.";
                return false;
            }

            var parent = data.GetComponentInParent<VRCAvatarDescriptor>();
            if (parent != null)
            {
                descriptor = parent;
                source = DescriptorTargetSource.Parent;
                return true;
            }

            var sceneDescriptors = UnityEngine.Object.FindObjectsByType<VRCAvatarDescriptor>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Where(IsActiveSceneDescriptor)
                .ToArray();
            var gestureManagerTargets = sceneDescriptors.Length > 1
                ? GestureManagerBridge.FindTargetedDescriptors(sceneDescriptors)
                : Array.Empty<VRCAvatarDescriptor>();
            return TrySelectDescriptor(
                parent,
                sceneDescriptors,
                gestureManagerTargets,
                out descriptor,
                out source,
                out error);
        }

        internal static bool TrySelectDescriptor(
            VRCAvatarDescriptor parent,
            IReadOnlyCollection<VRCAvatarDescriptor> sceneDescriptors,
            IReadOnlyCollection<VRCAvatarDescriptor> gestureManagerTargets,
            out VRCAvatarDescriptor descriptor,
            out DescriptorTargetSource source,
            out string error)
        {
            descriptor = null;
            source = DescriptorTargetSource.Parent;
            error = string.Empty;

            if (parent != null)
            {
                descriptor = parent;
                return true;
            }

            var candidates = DistinctDescriptors(sceneDescriptors);
            var candidateIds = new HashSet<int>(candidates.Select(candidate => candidate.GetInstanceID()));
            var targeted = DistinctDescriptors(gestureManagerTargets)
                .Where(candidate => candidateIds.Contains(candidate.GetInstanceID()))
                .ToArray();

            if (targeted.Length == 1)
            {
                descriptor = targeted[0];
                source = DescriptorTargetSource.GestureManager;
                return true;
            }

            if (candidates.Length == 1)
            {
                descriptor = candidates[0];
                source = DescriptorTargetSource.SoleSceneAvatar;
                return true;
            }

            if (candidates.Length == 0)
            {
                error = "No active VRChat avatar was found in an open scene. Add an avatar, or place this component below its Avatar Descriptor.";
                return false;
            }

            error = targeted.Length > 1
                ? $"Gesture Managers currently target {targeted.Length} different avatars. Place this component below the Avatar Descriptor you want to preview."
                : $"Found {candidates.Length} active VRChat avatars and no unambiguous Gesture Manager target. Place this component below the Avatar Descriptor you want to preview.";
            return false;
        }

        private static VRCAvatarDescriptor[] DistinctDescriptors(
            IEnumerable<VRCAvatarDescriptor> descriptors)
        {
            return (descriptors ?? Enumerable.Empty<VRCAvatarDescriptor>())
                .Where(candidate => candidate != null)
                .GroupBy(candidate => candidate.GetInstanceID())
                .Select(group => group.First())
                .ToArray();
        }

        private static bool IsActiveSceneDescriptor(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null || !descriptor.gameObject.activeInHierarchy) return false;
            var scene = descriptor.gameObject.scene;
            return scene.IsValid() && scene.isLoaded && !EditorUtility.IsPersistent(descriptor);
        }

        internal static void Stop(VisemeTestEmulatorData data)
        {
            if (data == null) return;
            if (!Sessions.TryGetValue(data.GetInstanceID(), out var state)) return;
            Sessions.Remove(data.GetInstanceID());
            Dispose(state);
        }

        internal static void Restart(VisemeTestEmulatorData data)
        {
            if (!IsRunning(data)) return;
            Start(data, out _);
        }

        internal static void ApplyManual(VisemeTestEmulatorData data)
        {
            var state = GetState(data);
            if (state == null || data.input != VisemeTestInput.Manual) return;
            ApplyFrame(state, data.manualViseme, data.manualVoice);
        }

        private static void Update()
        {
            EnsurePlayModeSessions();
            if (Sessions.Count == 0) return;
            foreach (var state in Sessions.Values.ToArray())
            {
                if (state.data == null || state.descriptor == null || !state.data.isActiveAndEnabled)
                {
                    Sessions.Remove(state.data != null ? state.data.GetInstanceID() : Sessions.First(x => x.Value == state).Key);
                    Dispose(state);
                    continue;
                }

                var now = EditorApplication.timeSinceStartup;
                state.lastUpdateTime = now;

                if (state.data.input == VisemeTestInput.Manual)
                {
                    ApplyFrame(state, state.data.manualViseme, state.data.manualVoice);
                    continue;
                }

                ReadMicrophone(state);
            }
        }

        private static void EnsurePlayModeSessions()
        {
            if (!Application.isPlaying) return;

            var components = UnityEngine.Object.FindObjectsByType<VisemeTestEmulatorData>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var component in components)
            {
                if (component == null || !component.isActiveAndEnabled || !component.startWithPlayMode || IsRunning(component))
                    continue;
                var id = component.GetInstanceID();
                if (!AutoStartAttempted.Add(id)) continue;
                if (!Start(component, out var error) && !string.IsNullOrEmpty(error))
                    Debug.LogWarning($"[YUCP Viseme Test Emulator] Play Mode preview could not start on '{component.name}': {error}", component);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                case PlayModeStateChange.ExitingPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    StopAll();
                    AutoStartAttempted.Clear();
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    AutoStartAttempted.Clear();
                    EditorApplication.delayCall += EnsurePlayModeSessions;
                    break;
            }
        }

        private static bool StartMicrophone(State state, out string error)
        {
            error = string.Empty;
            var devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                error = "Unity did not report any microphone devices.";
                return false;
            }

            var requested = state.data.microphoneDevice;
            if (!string.IsNullOrEmpty(requested) && !devices.Contains(requested))
            {
                error = $"Microphone '{requested}' is no longer available. Refresh the device list.";
                return false;
            }

            state.microphone = string.IsNullOrEmpty(requested) ? null : requested;
            Microphone.GetDeviceCaps(state.microphone, out var minimumRate, out var maximumRate);
            if (maximumRate > 0) state.sampleRate = maximumRate >= 48000 ? 48000 : Mathf.Max(16000, maximumRate);
            else if (minimumRate > 48000) state.sampleRate = minimumRate;

            try
            {
                state.microphoneClip = Microphone.Start(
                    state.microphone,
                    true,
                    MicrophoneBufferSeconds(state.data),
                    state.sampleRate);
            }
            catch (Exception exception)
            {
                error = "Could not open the microphone: " + exception.Message;
                return false;
            }

            if (state.microphoneClip == null)
            {
                error = "Unity could not start the selected microphone.";
                return false;
            }
            state.lastMicrophonePosition = 0;
            return true;
        }

        private static void ReadMicrophone(State state)
        {
            if (state.microphoneClip == null) return;
            var position = Microphone.GetPosition(state.microphone);
            if (position < 0 || position == state.lastMicrophonePosition) return;

            var clipSamples = state.microphoneClip.samples;
            var available = AvailableMicrophoneSamples(
                state.lastMicrophonePosition,
                position,
                clipSamples);
            var lossless = state.data != null && LosslessAnalysisSources.Contains(state.data.GetInstanceID());
            if (!lossless) available = Mathf.Min(available, state.sampleRate / 2);
            if (available <= 0) return;

            var samples = new float[available * state.microphoneClip.channels];
            if (!state.microphoneClip.GetData(samples, state.lastMicrophonePosition)) return;
            state.lastMicrophonePosition = position;

            var channels = Mathf.Max(1, state.microphoneClip.channels);
            for (var i = 0; i < samples.Length; i += channels)
            {
                var mono = 0f;
                for (var channel = 0; channel < channels; channel++) mono += samples[i + channel];
                state.pendingSamples.Enqueue(mono / channels * state.data.microphoneGain);
            }

            var processed = 0;
            var maximumFrames = lossless ? int.MaxValue : 8;
            while (state.pendingSamples.Count >= state.analysisFrame.Length && processed++ < maximumFrames)
            {
                for (var i = 0; i < state.analysisFrame.Length; i++) state.analysisFrame[i] = state.pendingSamples.Dequeue();
                AnalyzeFrame(state, state.analysisFrame.Length / (float)Mathf.Max(1, state.sampleRate));
            }
            if (!lossless)
                while (state.pendingSamples.Count > state.sampleRate / 4) state.pendingSamples.Dequeue();
        }

        internal static int MicrophoneBufferSeconds(VisemeTestEmulatorData data)
        {
            return data != null && LosslessAnalysisSources.Contains(data.GetInstanceID())
                ? LosslessMicrophoneBufferSeconds
                : PreviewMicrophoneBufferSeconds;
        }

        internal static int AvailableMicrophoneSamples(
            int previousPosition,
            int currentPosition,
            int clipSamples)
        {
            clipSamples = Mathf.Max(1, clipSamples);
            previousPosition = Mathf.Clamp(previousPosition, 0, clipSamples - 1);
            currentPosition = Mathf.Clamp(currentPosition, 0, clipSamples - 1);
            return currentPosition >= previousPosition
                ? currentPosition - previousPosition
                : clipSamples - previousPosition + currentPosition;
        }

        private static void AnalyzeFrame(State state, float deltaTime)
        {
            var rms = VisemeTestMath.RootMeanSquare(state.analysisFrame);
            if (state.noiseFloorRms <= 0f)
                state.noiseFloorRms = Mathf.Max(0.000001f, rms);
            var calibratingNoise = state.noiseCalibrationFrames > 0;
            if (calibratingNoise)
            {
                state.noiseCalibrationFrames--;
                state.noiseFloorRms = VisemeTestMath.ExpSmooth(
                    state.noiseFloorRms,
                    Mathf.Max(0.000001f, rms),
                    deltaTime,
                    0.08f);
            }
            var gate = VisemeTestMath.AdaptiveNoiseGate(
                state.noiseFloorRms, state.data.noiseGate);
            var speechEvidence = !calibratingNoise && rms > Mathf.Max(
                gate * 1.12f,
                state.noiseFloorRms * 1.8f);
            if (!calibratingNoise)
                state.noiseFloorRms = VisemeTestMath.UpdateNoiseFloor(
                    state.noiseFloorRms, rms, speechEvidence, deltaTime);
            gate = VisemeTestMath.AdaptiveNoiseGate(
                state.noiseFloorRms, state.data.noiseGate);

            if (speechEvidence) state.speechHangoverFrames = 6;
            else if (state.speechHangoverFrames > 0) state.speechHangoverFrames--;
            var desiredGain = speechEvidence
                ? VisemeTestMath.AutomaticInputGain(rms, gate)
                : 1f;
            state.automaticInputGain = VisemeTestMath.ExpSmooth(
                state.automaticInputGain,
                desiredGain,
                deltaTime,
                desiredGain > state.automaticInputGain ? 0.08f : 0.8f);
            var analysisGain = speechEvidence || state.speechHangoverFrames > 0
                ? state.automaticInputGain
                : 1f;
            if (analysisGain > 1.0001f)
                for (var index = 0; index < state.analysisFrame.Length; index++)
                    state.analysisFrame[index] *= analysisGain;

            state.currentInputRms = rms;
            state.effectiveNoiseGate = gate;
            var targetVoice = VisemeTestMath.VoiceFromRms(
                rms * analysisGain, gate * analysisGain, 1f);
            var response = targetVoice > state.currentVoice ? 0.025f : 0.09f;
            var voice = VisemeTestMath.ExpSmooth(state.currentVoice, targetVoice, deltaTime, response);
            int viseme;
            float[] weights = null;

            if (voice < 0.025f)
            {
                viseme = 0;
                weights = new float[VisemeTestMath.VisemeCount];
                weights[0] = 1f;
            }
            else if (state.oculus != null && state.oculus.TryProcess(state.analysisFrame, out weights))
            {
                viseme = VisemeTestMath.DominantViseme(weights);
            }
            else
            {
                if (state.oculus != null)
                {
                    state.oculus.Dispose();
                    state.oculus = null;
                    state.engineName = "YUCP fallback";
                }
                viseme = VisemeTestMath.ApproximateViseme(state.analysisFrame, state.sampleRate, voice, state.currentViseme);
                weights = BuildFallbackWeights(state, viseme, voice, deltaTime);
            }

            ApplyFrame(state, viseme, voice, weights);
            state.analysisSampleClock += state.analysisFrame.Length;
            PublishAnalysisSample(new AnalysisSample(
                state.data,
                viseme,
                voice,
                state.analysisSampleClock,
                state.sampleRate,
                state.engineName));
        }

        private static void PublishAnalysisSample(AnalysisSample sample)
        {
            var subscribers = AnalysisFrameProcessed;
            if (subscribers == null) return;
            foreach (Action<AnalysisSample> subscriber in subscribers.GetInvocationList())
            {
                try { subscriber(sample); }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private static float[] BuildFallbackWeights(State state, int viseme, float voice, float deltaTime)
        {
            var result = new float[VisemeTestMath.VisemeCount];
            for (var i = 0; i < result.Length; i++)
            {
                var target = i == viseme ? voice : 0f;
                if (i == 0) target = 1f - voice;
                result[i] = VisemeTestMath.ExpSmooth(state.currentWeights[i], target, deltaTime, 0.055f);
            }
            return result;
        }

        private static void ApplyFrame(State state, int viseme, float voice, float[] weights = null)
        {
            viseme = Mathf.Clamp(viseme, 0, 14);
            voice = Mathf.Clamp01(voice);
            state.currentViseme = viseme;
            state.currentVoice = voice;
            if (weights == null)
            {
                weights = new float[VisemeTestMath.VisemeCount];
                weights[viseme] = 1f;
            }
            for (var i = 0; i < state.currentWeights.Length; i++)
                state.currentWeights[i] = i < weights.Length ? Mathf.Clamp01(weights[i]) : 0f;

            if (!GestureManagerBridge.MouthTrackingEnabled(state.descriptor, state.data.driveGestureManager))
                return;

            var parameterViseme = IsJawFlap(state.descriptor.lipSync) ? Mathf.RoundToInt(voice * 100f) : viseme;
            if (state.data.driveAnimator) ApplyAnimator(state, parameterViseme, voice);
            if (state.data.driveGestureManager) GestureManagerBridge.SetParameters(state.descriptor, parameterViseme, voice);
            ApplyDescriptor(state.descriptor, state.currentWeights, viseme, voice);
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        internal static void ApplyDescriptor(VRCAvatarDescriptor descriptor, int viseme, float voice)
        {
            var weights = new float[VisemeTestMath.VisemeCount];
            weights[Mathf.Clamp(viseme, 0, VisemeTestMath.VisemeCount - 1)] = 1f;
            ApplyDescriptor(descriptor, weights, viseme, voice);
        }

        internal static void ApplyDescriptor(VRCAvatarDescriptor descriptor, float[] weights, int viseme, float voice)
        {
            if (descriptor == null) return;
            var renderer = descriptor.VisemeSkinnedMesh;
            switch (descriptor.lipSync)
            {
                case VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape:
                    if (renderer == null || renderer.sharedMesh == null || descriptor.VisemeBlendShapes == null) return;
                    for (var i = 0; i < Mathf.Min(15, descriptor.VisemeBlendShapes.Length); i++)
                    {
                        var index = renderer.sharedMesh.GetBlendShapeIndex(descriptor.VisemeBlendShapes[i]);
                        if (index >= 0)
                        {
                            var weight = weights != null && i < weights.Length ? weights[i] : i == viseme ? 1f : 0f;
                            renderer.SetBlendShapeWeight(index, Mathf.Clamp01(weight) * 100f);
                        }
                    }
                    break;
                case VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape:
                    if (renderer == null || renderer.sharedMesh == null) return;
                    var shape = renderer.sharedMesh.GetBlendShapeIndex(descriptor.MouthOpenBlendShapeName);
                    if (shape >= 0) renderer.SetBlendShapeWeight(shape, voice * 100f);
                    break;
                case VRC_AvatarDescriptor.LipSyncStyle.JawFlapBone:
                    if (descriptor.lipSyncJawBone != null)
                        descriptor.lipSyncJawBone.localRotation = Quaternion.SlerpUnclamped(
                            descriptor.lipSyncJawClosed, descriptor.lipSyncJawOpen, voice);
                    break;
            }
        }

        private static bool IsJawFlap(VRC_AvatarDescriptor.LipSyncStyle style) =>
            style == VRC_AvatarDescriptor.LipSyncStyle.JawFlapBone ||
            style == VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape;

        private static void Snapshot(State state)
        {
            var descriptor = state.descriptor;
            var renderer = descriptor.VisemeSkinnedMesh;
            if (renderer != null && renderer.sharedMesh != null)
            {
                if (descriptor.lipSync == VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape && descriptor.VisemeBlendShapes != null)
                {
                    foreach (var name in descriptor.VisemeBlendShapes)
                    {
                        var index = renderer.sharedMesh.GetBlendShapeIndex(name);
                        if (index >= 0 && !state.originalBlendShapes.ContainsKey(index))
                            state.originalBlendShapes[index] = renderer.GetBlendShapeWeight(index);
                    }
                }
                else if (descriptor.lipSync == VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape)
                {
                    var index = renderer.sharedMesh.GetBlendShapeIndex(descriptor.MouthOpenBlendShapeName);
                    if (index >= 0) state.originalBlendShapes[index] = renderer.GetBlendShapeWeight(index);
                }
            }

            if (descriptor.lipSyncJawBone != null)
            {
                state.originalJawRotation = descriptor.lipSyncJawBone.localRotation;
                state.hasJawSnapshot = true;
            }

            if (state.animator == null) return;
            state.hasVisemeParameter = HasAnimatorParameter(state.animator, "Viseme", AnimatorControllerParameterType.Int);
            state.hasVoiceParameter = HasAnimatorParameter(state.animator, "Voice", AnimatorControllerParameterType.Float);
            if (state.hasVisemeParameter) state.originalViseme = state.animator.GetInteger("Viseme");
            if (state.hasVoiceParameter) state.originalVoice = state.animator.GetFloat("Voice");
        }

        private static void Restore(State state)
        {
            var renderer = state.descriptor != null ? state.descriptor.VisemeSkinnedMesh : null;
            if (renderer != null)
                foreach (var pair in state.originalBlendShapes)
                    if (pair.Key >= 0 && renderer.sharedMesh != null && pair.Key < renderer.sharedMesh.blendShapeCount)
                        renderer.SetBlendShapeWeight(pair.Key, pair.Value);

            if (state.hasJawSnapshot && state.descriptor != null && state.descriptor.lipSyncJawBone != null)
                state.descriptor.lipSyncJawBone.localRotation = state.originalJawRotation;
            if (state.animator != null)
            {
                if (state.hasVisemeParameter) state.animator.SetInteger("Viseme", state.originalViseme);
                if (state.hasVoiceParameter) state.animator.SetFloat("Voice", state.originalVoice);
            }
        }

        private static bool HasAnimatorParameter(Animator animator, string name, AnimatorControllerParameterType type) =>
            animator.parameters.Any(parameter => parameter.name == name && parameter.type == type);

        private static void ApplyAnimator(State state, int viseme, float voice)
        {
            if (state.animator == null) return;
            if (state.hasVisemeParameter) state.animator.SetInteger("Viseme", viseme);
            if (state.hasVoiceParameter) state.animator.SetFloat("Voice", voice);
        }

        private static void StopMicrophone(State state)
        {
            if (state.microphoneClip != null) Microphone.End(state.microphone);
            state.microphoneClip = null;
        }

        private static void Dispose(State state)
        {
            StopMicrophone(state);
            state.oculus?.Dispose();
            Restore(state);
            SceneView.RepaintAll();
        }

        private static void StopAll()
        {
            foreach (var state in Sessions.Values.ToArray()) Dispose(state);
            Sessions.Clear();
            TrackingControlBridge.Clear();
        }

        private sealed class LosslessAnalysisScope : IDisposable
        {
            private readonly int sourceId;
            private bool disposed;

            internal LosslessAnalysisScope(int sourceId)
            {
                this.sourceId = sourceId;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                LosslessAnalysisSources.Remove(sourceId);
            }
        }

        internal static class TrackingControlBridge
        {
            private static readonly Dictionary<int, bool> MouthTracking = new Dictionary<int, bool>();

            internal static void Initialize()
            {
                VRC_AnimatorTrackingControl.Initialize -= OnInitialize;
                VRC_AnimatorTrackingControl.Initialize += OnInitialize;
            }

            internal static bool TryGet(VRCAvatarDescriptor descriptor, out bool enabled)
            {
                enabled = true;
                return descriptor != null && MouthTracking.TryGetValue(descriptor.GetInstanceID(), out enabled);
            }

            internal static void Clear() => MouthTracking.Clear();

            internal static void ApplyForTests(VRC_AnimatorTrackingControl control, Animator animator)
            {
                OnInitialize(control);
                OnApplySettings(control, animator);
            }

            private static void OnInitialize(VRC_AnimatorTrackingControl control)
            {
                control.ApplySettings -= OnApplySettings;
                control.ApplySettings += OnApplySettings;
            }

            private static void OnApplySettings(VRC_AnimatorTrackingControl control, Animator animator)
            {
                if (control == null || animator == null ||
                    control.trackingMouth == VRC_AnimatorTrackingControl.TrackingType.NoChange) return;
                var descriptor = animator.GetComponentInParent<VRCAvatarDescriptor>();
                if (descriptor == null) return;
                MouthTracking[descriptor.GetInstanceID()] =
                    control.trackingMouth != VRC_AnimatorTrackingControl.TrackingType.Animation;
            }
        }

        internal sealed class OculusBridge : IDisposable
        {
            private readonly Type type;
            private readonly Type frameType;
            private readonly MethodInfo processFrame;
            private readonly MethodInfo destroyContext;
            private readonly MethodInfo shutdown;
            private readonly object frame;
            private readonly FieldInfo visemesField;
            private uint context;

            private OculusBridge(Type type, Type frameType, MethodInfo processFrame, MethodInfo destroyContext,
                MethodInfo shutdown, object frame, FieldInfo visemesField, uint context)
            {
                this.type = type;
                this.frameType = frameType;
                this.processFrame = processFrame;
                this.destroyContext = destroyContext;
                this.shutdown = shutdown;
                this.frame = frame;
                this.visemesField = visemesField;
                this.context = context;
            }

            internal static bool IsAvailable(out string detail)
            {
                var type = FindType();
                detail = type == null ? "Install Meta/Oculus LipSync to enable its native classifier." : string.Empty;
                return type != null;
            }

            internal static OculusBridge TryCreate(int sampleRate, int bufferSize, out string error)
            {
                error = string.Empty;
                try
                {
                    var type = FindType();
                    if (type == null) { error = "Oculus LipSync was requested, but no OVRLipSync type is loaded."; return null; }
                    var frameType = type.GetNestedType("Frame", BindingFlags.Public | BindingFlags.NonPublic);
                    var providerType = type.GetNestedType("ContextProviders", BindingFlags.Public | BindingFlags.NonPublic);
                    if (frameType == null || providerType == null) { error = "The installed Oculus LipSync API is incompatible."; return null; }

                    var initialize = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(method => method.Name == "Initialize" && method.GetParameters().Length == 2);
                    initialize?.Invoke(null, new object[] { sampleRate, bufferSize });

                    var create = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(method => method.Name == "CreateContext" && method.GetParameters().Length >= 2);
                    var process = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(method => method.Name == "ProcessFrame" &&
                                                  method.GetParameters().Length == 4 &&
                                                  method.GetParameters()[1].ParameterType == typeof(float[]));
                    if (create == null || process == null) { error = "The installed Oculus LipSync API does not expose the expected context methods."; return null; }

                    uint context = 0;
                    var providerName = Enum.GetNames(providerType).Contains("Enhanced") ? "Enhanced" : Enum.GetNames(providerType)[0];
                    var provider = Enum.Parse(providerType, providerName);
                    var parameters = create.GetParameters();
                    var args = new object[parameters.Length];
                    args[0] = context;
                    args[1] = provider;
                    for (var i = 2; i < args.Length; i++)
                        args[i] = parameters[i].ParameterType == typeof(bool) ? false : sampleRate;
                    var result = create.Invoke(null, args);
                    context = Convert.ToUInt32(args[0]);
                    if (context == 0 || Convert.ToInt32(result) != 0) { error = "Oculus LipSync could not create an analysis context (" + result + ")."; return null; }

                    var sendSignal = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(method => method.Name == "SendSignal" && method.GetParameters().Length == 4);
                    var signalsType = type.GetNestedType("Signals", BindingFlags.Public | BindingFlags.NonPublic);
                    if (sendSignal != null && signalsType != null && Enum.GetNames(signalsType).Contains("VisemeSmoothing"))
                    {
                        var smoothing = Enum.Parse(signalsType, "VisemeSmoothing");
                        sendSignal.Invoke(null, new object[] { context, smoothing, 70, 0 });
                    }

                    var frame = Activator.CreateInstance(frameType);
                    var visemes = frameType.GetField("Visemes", BindingFlags.Public | BindingFlags.Instance);
                    if (frame == null || visemes == null) { error = "The installed Oculus LipSync frame format is incompatible."; return null; }
                    return new OculusBridge(type, frameType, process,
                        type.GetMethod("DestroyContext", BindingFlags.Public | BindingFlags.Static),
                        type.GetMethod("Shutdown", BindingFlags.Public | BindingFlags.Static), frame, visemes, context);
                }
                catch (Exception exception)
                {
                    error = "Oculus LipSync could not start: " + (exception.InnerException?.Message ?? exception.Message);
                    return null;
                }
            }

            internal bool TryProcess(float[] samples, out float[] weights)
            {
                weights = null;
                try
                {
                    var result = processFrame.Invoke(null, new[] { (object)context, samples, frame, false });
                    if (Convert.ToInt32(result) != 0) return false;
                    weights = visemesField.GetValue(frame) as float[];
                    return weights != null && weights.Length >= 15;
                }
                catch { return false; }
            }

            public void Dispose()
            {
                try { if (context != 0) destroyContext?.Invoke(null, new object[] { context }); }
                catch { /* Keep editor teardown safe. */ }
                context = 0;
                // Do not shut down a shared Oculus singleton; another preview component may own it.
            }

            private static Type FindType()
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var exact = assembly.GetType("OVRLipSync", false);
                    if (exact != null) return exact;
                    try
                    {
                        var match = assembly.GetTypes().FirstOrDefault(candidate => candidate.Name == "OVRLipSync");
                        if (match != null) return match;
                    }
                    catch (ReflectionTypeLoadException) { }
                }
                return null;
            }
        }

        internal static class GestureManagerBridge
        {
            private static Component cachedManager;
            private static object cachedModule;
            private static int cachedDescriptorId;

            internal static VRCAvatarDescriptor[] FindTargetedDescriptors(
                IEnumerable<VRCAvatarDescriptor> candidates)
            {
                var descriptors = (candidates ?? Enumerable.Empty<VRCAvatarDescriptor>())
                    .Where(candidate => candidate != null)
                    .GroupBy(candidate => candidate.GetInstanceID())
                    .ToDictionary(group => group.Key, group => group.First());
                if (descriptors.Count == 0) return Array.Empty<VRCAvatarDescriptor>();

                var targets = new Dictionary<int, VRCAvatarDescriptor>();
                var managers = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None)
                    .Where(component => component != null &&
                                        component.gameObject.activeInHierarchy &&
                                        component.gameObject.scene.IsValid() &&
                                        component.gameObject.scene.isLoaded &&
                                        component.GetType().Name == "GestureManager");

                foreach (var manager in managers)
                {
                    var target = DescriptorFromModule(manager) ?? DescriptorFromFavourite(manager);
                    if (target == null || !descriptors.ContainsKey(target.GetInstanceID())) continue;
                    targets[target.GetInstanceID()] = target;
                }

                return targets.Values.ToArray();
            }

            internal static void SetParameters(VRCAvatarDescriptor descriptor, int viseme, float voice)
            {
                try
                {
                    if (!EnsureModule(descriptor)) return;
                    Set("Viseme", viseme);
                    Set("Voice", voice);
                }
                catch { /* Gesture Manager is optional and versioned independently. */ }
            }

            internal static bool MouthTrackingEnabled(VRCAvatarDescriptor descriptor, bool consultGestureManager = true)
            {
                try
                {
                    if (TrackingControlBridge.TryGet(descriptor, out var directState)) return directState;
                    if (!consultGestureManager) return true;
                    if (!EnsureModule(descriptor)) return true;
                    var trackingField = cachedModule.GetType().GetField(
                        "TrackingControls", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var tracking = trackingField?.GetValue(cachedModule) as IDictionary;
                    if (tracking == null || !tracking.Contains("Mouth & Jaw")) return true;
                    return !string.Equals(tracking["Mouth & Jaw"]?.ToString(), "Animation", StringComparison.Ordinal);
                }
                catch { return true; }
            }

            private static bool EnsureModule(VRCAvatarDescriptor descriptor)
            {
                if (descriptor == null) return false;
                if (cachedManager == null || cachedDescriptorId != descriptor.GetInstanceID())
                {
                    cachedManager = FindManager(descriptor);
                    cachedModule = null;
                    cachedDescriptorId = descriptor.GetInstanceID();
                }
                if (cachedManager == null) return false;
                var moduleField = cachedManager.GetType().GetField(
                    "Module", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var managerModule = moduleField?.GetValue(cachedManager);
                if (managerModule != null && ModuleTargets(managerModule, descriptor)) cachedModule = managerModule;
                else cachedModule = null;
                if (cachedModule != null) return true;
                TryInitializeModule(cachedManager, descriptor);
                cachedModule = moduleField?.GetValue(cachedManager);
                return cachedModule != null && ModuleTargets(cachedModule, descriptor);
            }

            private static VRCAvatarDescriptor DescriptorFromModule(Component manager)
            {
                var moduleField = manager.GetType().GetField(
                    "Module",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var module = moduleField?.GetValue(manager);
                if (module == null) return null;
                var avatarField = module.GetType().GetField(
                    "Avatar",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return DescriptorFromTarget(avatarField?.GetValue(module));
            }

            private static VRCAvatarDescriptor DescriptorFromFavourite(Component manager)
            {
                var settingsField = manager.GetType().GetField(
                    "settings",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var settings = settingsField?.GetValue(manager);
                if (settings == null) return null;
                var favouriteField = settings.GetType().GetField(
                    "favourite",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return DescriptorFromTarget(favouriteField?.GetValue(settings));
            }

            private static VRCAvatarDescriptor DescriptorFromTarget(object target)
            {
                if (target is VRCAvatarDescriptor descriptor) return descriptor;
                if (target is GameObject gameObject)
                    return gameObject.GetComponent<VRCAvatarDescriptor>() ??
                           gameObject.GetComponentInParent<VRCAvatarDescriptor>();
                if (target is Component component)
                    return component.GetComponent<VRCAvatarDescriptor>() ??
                           component.GetComponentInParent<VRCAvatarDescriptor>();
                return null;
            }

            private static Component FindManager(VRCAvatarDescriptor descriptor)
            {
                var root = descriptor.transform;
                var managers = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(component => component != null && component.GetType().Name == "GestureManager")
                    .Cast<Component>()
                    .ToArray();

                return managers.FirstOrDefault(manager =>
                           manager.transform == root || manager.transform.IsChildOf(root) || root.IsChildOf(manager.transform))
                       ?? (managers.Length == 1 ? managers[0] : null);
            }

            private static void TryInitializeModule(Component manager, VRCAvatarDescriptor descriptor)
            {
                try
                {
                    var moduleType = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(assembly => assembly.GetType(
                            "BlackStartX.GestureManager.Editor.Modules.Vrc3.ModuleVrc3", false))
                        .FirstOrDefault(candidate => candidate != null);
                    if (moduleType == null) return;
                    var constructor = moduleType.GetConstructor(new[] { typeof(VRCAvatarDescriptor) });
                    if (constructor == null) return;
                    var module = constructor.Invoke(new object[] { descriptor });
                    var setModule = manager.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(method => method.Name == "SetModule" && method.GetParameters().Length == 1);
                    setModule?.Invoke(manager, new[] { module });
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "[YUCP Viseme Test Emulator] Gesture Manager could not be initialized: " +
                        (exception.InnerException?.Message ?? exception.Message), descriptor);
                }
            }

            private static bool ModuleTargets(object module, VRCAvatarDescriptor descriptor)
            {
                var avatarField = module.GetType().GetField("Avatar", BindingFlags.Public | BindingFlags.Instance);
                return avatarField == null || avatarField.GetValue(module) as GameObject == descriptor.gameObject;
            }

            private static void Set(string name, float value)
            {
                var paramsMember = cachedModule.GetType().GetField("Params", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var dictionary = paramsMember?.GetValue(cachedModule);
                if (dictionary == null) return;
                var contains = dictionary.GetType().GetMethod("ContainsKey", new[] { typeof(string) });
                var item = dictionary.GetType().GetProperty("Item");
                if (contains == null || item == null || !(bool)contains.Invoke(dictionary, new object[] { name })) return;
                var parameter = item.GetValue(dictionary, new object[] { name });
                if (parameter == null) return;
                var set = parameter.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(method => method.Name == "Set" && method.GetParameters().Length >= 2);
                if (set == null) return;
                var arguments = set.GetParameters().Length == 2
                    ? new[] { cachedModule, (object)value }
                    : new[] { cachedModule, (object)value, null };
                set.Invoke(parameter, arguments);
            }
        }
    }
}
