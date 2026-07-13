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
        }

        private static readonly Dictionary<int, State> Sessions = new Dictionary<int, State>();
        private static readonly HashSet<int> AutoStartAttempted = new HashSet<int>();

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
                : "Oculus LipSync is not installed — microphone preview will use YUCP's local fallback. Manual output remains exact." +
                  (string.IsNullOrEmpty(detail) ? string.Empty : " " + detail);
        }

        internal static bool Start(VisemeTestEmulatorData data, out string error)
        {
            error = string.Empty;
            if (data == null) { error = "Component is missing."; return false; }
            Stop(data);

            var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null) { error = "Place this component on or below a VRChat Avatar Descriptor."; return false; }

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
                var deltaTime = Mathf.Clamp((float)(now - state.lastUpdateTime), 0f, 0.25f);
                state.lastUpdateTime = now;

                if (state.data.input == VisemeTestInput.Manual)
                {
                    ApplyFrame(state, state.data.manualViseme, state.data.manualVoice);
                    continue;
                }

                ReadMicrophone(state, deltaTime);
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
                state.microphoneClip = Microphone.Start(state.microphone, true, 2, state.sampleRate);
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

        private static void ReadMicrophone(State state, float deltaTime)
        {
            if (state.microphoneClip == null) return;
            var position = Microphone.GetPosition(state.microphone);
            if (position < 0 || position == state.lastMicrophonePosition) return;

            var clipSamples = state.microphoneClip.samples;
            var available = position >= state.lastMicrophonePosition
                ? position - state.lastMicrophonePosition
                : clipSamples - state.lastMicrophonePosition + position;
            available = Mathf.Min(available, state.sampleRate / 2);
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
            while (state.pendingSamples.Count >= state.analysisFrame.Length && processed++ < 8)
            {
                for (var i = 0; i < state.analysisFrame.Length; i++) state.analysisFrame[i] = state.pendingSamples.Dequeue();
                AnalyzeFrame(state, deltaTime);
            }
            while (state.pendingSamples.Count > state.sampleRate / 4) state.pendingSamples.Dequeue();
        }

        private static void AnalyzeFrame(State state, float deltaTime)
        {
            var rms = VisemeTestMath.RootMeanSquare(state.analysisFrame);
            var targetVoice = VisemeTestMath.VoiceFromRms(rms, state.data.noiseGate, 1f);
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
