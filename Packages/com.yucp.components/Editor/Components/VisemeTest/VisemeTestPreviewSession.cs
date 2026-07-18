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
            internal TargetState[] targets = Array.Empty<TargetState>();
            internal AudioClip microphoneClip;
            internal string microphone;
            internal int sampleRate = 48000;
            internal int lastMicrophonePosition;
            internal readonly Queue<float> pendingSamples = new Queue<float>();
            internal readonly float[] analysisFrame = new float[1024];
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

        internal sealed class TargetState
        {
            internal VRCAvatarDescriptor descriptor;
            internal readonly Dictionary<int, float> originalBlendShapes = new Dictionary<int, float>();
            internal Quaternion originalJawRotation;
            internal bool hasJawSnapshot;
            internal Animator animator;
            internal bool hasVisemeParameter;
            internal bool hasVoiceParameter;
            internal int originalViseme;
            internal float originalVoice;
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
                ? "Oculus LipSync detected. Microphone classification uses the same engine family as VRChat."
                : "Approximate enrollment: Oculus LipSync is not installed, so microphone preview will use YUCP's local fallback. Manual output remains exact." +
                  (string.IsNullOrEmpty(detail) ? string.Empty : " " + detail);
        }

        internal static bool Start(VisemeTestEmulatorData data, out string error)
        {
            error = string.Empty;
            if (data == null) { error = "Component is missing."; return false; }
            Stop(data);

            if (!TryResolveDescriptors(data, out var descriptors, out _, out error)) return false;

            var state = new State
            {
                data = data,
                targets = descriptors.Select(descriptor => new TargetState
                {
                    descriptor = descriptor,
                    animator = descriptor.GetComponent<Animator>()
                }).ToArray(),
                lastUpdateTime = EditorApplication.timeSinceStartup
            };
            foreach (var target in state.targets) Snapshot(target);

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

        internal static bool TryResolveDescriptors(
            VisemeTestEmulatorData data,
            out VRCAvatarDescriptor[] descriptors,
            out DescriptorTargetSource source,
            out string error)
        {
            descriptors = Array.Empty<VRCAvatarDescriptor>();
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
                descriptors = new[] { parent };
                source = DescriptorTargetSource.Parent;
                return true;
            }

            var sceneDescriptors = UnityEngine.Object.FindObjectsByType<VRCAvatarDescriptor>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Where(IsActiveSceneDescriptor)
                .ToArray();
            var gestureManagerTargets = GestureManagerBridge.FindTargetedDescriptors(sceneDescriptors);
            return TrySelectDescriptors(
                parent,
                sceneDescriptors,
                gestureManagerTargets,
                out descriptors,
                out source,
                out error);
        }

        internal static bool TrySelectDescriptors(
            VRCAvatarDescriptor parent,
            IReadOnlyCollection<VRCAvatarDescriptor> sceneDescriptors,
            IReadOnlyCollection<VRCAvatarDescriptor> gestureManagerTargets,
            out VRCAvatarDescriptor[] descriptors,
            out DescriptorTargetSource source,
            out string error)
        {
            descriptors = Array.Empty<VRCAvatarDescriptor>();
            source = DescriptorTargetSource.Parent;
            error = string.Empty;

            if (parent != null)
            {
                descriptors = new[] { parent };
                return true;
            }

            var candidates = DistinctDescriptors(sceneDescriptors);
            var candidateIds = new HashSet<int>(candidates.Select(candidate => candidate.GetInstanceID()));
            var targeted = DistinctDescriptors(gestureManagerTargets)
                .Where(candidate => candidateIds.Contains(candidate.GetInstanceID()))
                .ToArray();

            if (targeted.Length > 0)
            {
                descriptors = targeted;
                source = DescriptorTargetSource.GestureManager;
                return true;
            }

            if (candidates.Length == 1)
            {
                descriptors = new[] { candidates[0] };
                source = DescriptorTargetSource.SoleSceneAvatar;
                return true;
            }

            if (candidates.Length == 0)
            {
                error = "No active VRChat avatar was found in an open scene. Add an avatar, or place this component below its Avatar Descriptor.";
                return false;
            }

            error = $"Found {candidates.Length} active VRChat avatars and no Gesture Manager targets. Place this component below the Avatar Descriptor you want to preview, or target the avatars with Gesture Manager.";
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
                if (state.data == null ||
                    !state.data.isActiveAndEnabled ||
                    state.targets == null ||
                    !state.targets.Any(target => target != null && target.descriptor != null))
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
            if (state.data.automaticGain)
            {
                var desiredGain = speechEvidence
                    ? VisemeTestMath.AutomaticInputGain(rms, gate)
                    : 1f;
                state.automaticInputGain = VisemeTestMath.ExpSmooth(
                    state.automaticInputGain,
                    desiredGain,
                    deltaTime,
                    desiredGain > state.automaticInputGain ? 0.08f : 0.8f);
            }
            else
            {
                state.automaticInputGain = 1f;
            }
            var analysisGain = ResolveAnalysisGain(
                state.data.automaticGain,
                speechEvidence || state.speechHangoverFrames > 0,
                state.automaticInputGain);
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

        internal static float ResolveAnalysisGain(
            bool automaticGainEnabled,
            bool speechActive,
            float automaticInputGain)
        {
            return automaticGainEnabled && speechActive
                ? Mathf.Clamp(automaticInputGain, 1f, 15f)
                : 1f;
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

        internal static void ApplyFrame(State state, int viseme, float voice, float[] weights = null)
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

            foreach (var target in state.targets)
            {
                if (target == null || target.descriptor == null) continue;
                if (!GestureManagerBridge.MouthTrackingEnabled(
                        target.descriptor,
                        state.data.driveGestureManager)) continue;

                var parameterViseme = IsJawFlap(target.descriptor.lipSync)
                    ? Mathf.RoundToInt(voice * 100f)
                    : viseme;
                if (state.data.driveAnimator) ApplyAnimator(target, parameterViseme, voice);
                if (state.data.driveGestureManager)
                    GestureManagerBridge.SetParameters(target.descriptor, parameterViseme, voice);
                ApplyDescriptor(target.descriptor, state.currentWeights, viseme, voice);
            }
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

        private static void Snapshot(TargetState state)
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

        private static void Restore(TargetState state)
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

        private static void Restore(State state)
        {
            if (state?.targets == null) return;
            foreach (var target in state.targets)
                if (target != null) Restore(target);
        }

        private static bool HasAnimatorParameter(Animator animator, string name, AnimatorControllerParameterType type) =>
            animator.parameters.Any(parameter => parameter.name == name && parameter.type == type);

        private static void ApplyAnimator(TargetState state, int viseme, float voice)
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
            private static readonly Dictionary<int, Component[]> CachedManagersByDescriptor =
                new Dictionary<int, Component[]>();
            private static readonly Dictionary<int, int> InferredDescriptorByManager =
                new Dictionary<int, int>();

            static GestureManagerBridge()
            {
                EditorApplication.hierarchyChanged += ClearCaches;
            }

            internal static VRCAvatarDescriptor[] FindTargetedDescriptors(
                IEnumerable<VRCAvatarDescriptor> candidates)
            {
                var descriptors = (candidates ?? Enumerable.Empty<VRCAvatarDescriptor>())
                    .Where(candidate => candidate != null)
                    .GroupBy(candidate => candidate.GetInstanceID())
                    .ToDictionary(group => group.Key, group => group.First());
                if (descriptors.Count == 0) return Array.Empty<VRCAvatarDescriptor>();

                var targets = new Dictionary<int, VRCAvatarDescriptor>();
                var bindings = new Dictionary<int, List<Component>>();
                var managers = FindLoadedManagers();
                var unboundManagers = new List<Component>();
                InferredDescriptorByManager.Clear();

                foreach (var manager in managers)
                {
                    var target = DescriptorFromModule(manager) ?? DescriptorFromFavourite(manager);
                    if (target == null)
                    {
                        unboundManagers.Add(manager);
                        continue;
                    }
                    if (!descriptors.TryGetValue(target.GetInstanceID(), out var candidate)) continue;
                    AddBinding(bindings, candidate, manager);
                    targets[candidate.GetInstanceID()] = candidate;
                }

                var unassignedDescriptors = descriptors.Values
                    .Where(candidate => !targets.ContainsKey(candidate.GetInstanceID()))
                    .OrderBy(candidate => candidate.gameObject.scene.handle)
                    .ThenBy(candidate => candidate.transform.GetSiblingIndex())
                    .ThenBy(candidate => candidate.name)
                    .ToArray();
                var orderedUnboundManagers = unboundManagers
                    .OrderBy(manager => manager.gameObject.scene.handle)
                    .ThenBy(manager => manager.transform.GetSiblingIndex())
                    .ThenBy(manager => manager.name)
                    .ToArray();

                if (unassignedDescriptors.Length == 1)
                {
                    foreach (var manager in orderedUnboundManagers)
                        AddInferredBinding(bindings, targets, unassignedDescriptors[0], manager);
                }
                else if (unassignedDescriptors.Length > 1 &&
                         unassignedDescriptors.Length == orderedUnboundManagers.Length)
                {
                    for (var index = 0; index < unassignedDescriptors.Length; index++)
                        AddInferredBinding(
                            bindings,
                            targets,
                            unassignedDescriptors[index],
                            orderedUnboundManagers[index]);
                }

                foreach (var descriptorId in descriptors.Keys)
                {
                    if (bindings.TryGetValue(descriptorId, out var descriptorManagers))
                        CachedManagersByDescriptor[descriptorId] = descriptorManagers.ToArray();
                    else
                        CachedManagersByDescriptor.Remove(descriptorId);
                }
                return targets.Values.ToArray();
            }

            private static void AddInferredBinding(
                IDictionary<int, List<Component>> bindings,
                IDictionary<int, VRCAvatarDescriptor> targets,
                VRCAvatarDescriptor descriptor,
                Component manager)
            {
                InferredDescriptorByManager[manager.GetInstanceID()] = descriptor.GetInstanceID();
                AddBinding(bindings, descriptor, manager);
                targets[descriptor.GetInstanceID()] = descriptor;
            }

            private static void AddBinding(
                IDictionary<int, List<Component>> bindings,
                VRCAvatarDescriptor descriptor,
                Component manager)
            {
                if (!bindings.TryGetValue(descriptor.GetInstanceID(), out var managers))
                {
                    managers = new List<Component>();
                    bindings[descriptor.GetInstanceID()] = managers;
                }
                managers.Add(manager);
            }

            private static void ClearCaches()
            {
                CachedManagersByDescriptor.Clear();
                InferredDescriptorByManager.Clear();
            }

            internal static void SetParameters(VRCAvatarDescriptor descriptor, int viseme, float voice)
            {
                foreach (var manager in FindManagers(descriptor))
                {
                    try
                    {
                        if (!EnsureModule(manager, descriptor, out var module)) continue;
                        Set(module, "Viseme", viseme);
                        Set(module, "Voice", voice);
                    }
                    catch { /* Gesture Manager is optional and versioned independently. */ }
                }
            }

            internal static bool MouthTrackingEnabled(VRCAvatarDescriptor descriptor, bool consultGestureManager = true)
            {
                if (TrackingControlBridge.TryGet(descriptor, out var directState)) return directState;
                if (!consultGestureManager) return true;

                foreach (var manager in FindManagers(descriptor))
                {
                    try
                    {
                        if (!EnsureModule(manager, descriptor, out var module)) continue;
                        var trackingField = module.GetType().GetField(
                            "TrackingControls", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        var tracking = trackingField?.GetValue(module) as IDictionary;
                        if (tracking != null && tracking.Contains("Mouth & Jaw") &&
                            string.Equals(tracking["Mouth & Jaw"]?.ToString(), "Animation", StringComparison.Ordinal))
                            return false;
                    }
                    catch { /* A broken manager must not prevent the other targets from updating. */ }
                }
                return true;
            }

            private static bool EnsureModule(
                Component manager,
                VRCAvatarDescriptor descriptor,
                out object module)
            {
                module = null;
                if (manager == null || descriptor == null) return false;
                var moduleField = manager.GetType().GetField(
                    "Module", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                module = moduleField?.GetValue(manager);
                if (module != null && ModuleTargets(module, descriptor)) return true;
                TryInitializeModule(manager, descriptor);
                module = moduleField?.GetValue(manager);
                return module != null && ModuleTargets(module, descriptor);
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

            private static Component[] FindManagers(VRCAvatarDescriptor descriptor)
            {
                if (descriptor == null) return Array.Empty<Component>();
                var descriptorId = descriptor.GetInstanceID();
                if (CachedManagersByDescriptor.TryGetValue(descriptorId, out var cached) &&
                    cached.Length > 0 &&
                    cached.All(manager => manager != null && ManagerTargets(manager, descriptor)))
                    return cached;

                var root = descriptor.transform;
                var managers = FindLoadedManagers();
                var matches = managers.Where(manager => ManagerTargets(manager, descriptor)).ToArray();
                if (matches.Length == 0)
                    matches = managers.Where(manager =>
                        manager.transform == root ||
                        manager.transform.IsChildOf(root) ||
                        root.IsChildOf(manager.transform)).ToArray();
                if (matches.Length == 0 && managers.Length == 1) matches = managers;

                if (matches.Length > 0) CachedManagersByDescriptor[descriptorId] = matches;
                else CachedManagersByDescriptor.Remove(descriptorId);
                return matches;
            }

            private static Component[] FindLoadedManagers()
            {
                return UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None)
                    .Where(component => component != null &&
                                        component.gameObject.activeInHierarchy &&
                                        component.gameObject.scene.IsValid() &&
                                        component.gameObject.scene.isLoaded &&
                                        component.GetType().Name == "GestureManager")
                    .Cast<Component>()
                    .ToArray();
            }

            private static bool ManagerTargets(Component manager, VRCAvatarDescriptor descriptor)
            {
                var target = DescriptorFromModule(manager) ?? DescriptorFromFavourite(manager);
                if (target != null)
                    return descriptor != null && target.GetInstanceID() == descriptor.GetInstanceID();
                return descriptor != null &&
                       InferredDescriptorByManager.TryGetValue(manager.GetInstanceID(), out var descriptorId) &&
                       descriptorId == descriptor.GetInstanceID();
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
                var avatarField = module.GetType().GetField(
                    "Avatar", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (avatarField == null) return true;
                var target = DescriptorFromTarget(avatarField.GetValue(module));
                return target != null && descriptor != null &&
                       target.GetInstanceID() == descriptor.GetInstanceID();
            }

            private static void Set(object module, string name, float value)
            {
                var paramsMember = module.GetType().GetField(
                    "Params", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var dictionary = paramsMember?.GetValue(module);
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
                    ? new[] { module, (object)value }
                    : new[] { module, (object)value, null };
                set.Invoke(parameter, arguments);
            }
        }
    }
}
