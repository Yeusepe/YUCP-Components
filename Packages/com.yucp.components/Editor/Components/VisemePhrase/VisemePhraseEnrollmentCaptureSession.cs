using System;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace YUCP.Components.Editor.VisemePhrase
{
    /// <summary>
    /// Reuses the same analyzer and avatar preview path as Viseme Test Emulator while
    /// keeping the temporary runner out of the scene and out of saved assets.
    /// </summary>
    internal sealed class VisemePhraseEnrollmentCaptureSession : IDisposable
    {
        private GameObject runnerObject;
        private VisemeTestEmulatorData runner;
        private IDisposable losslessAnalysisScope;
        private readonly VisemePhraseCaptureBuffer buffer = new VisemePhraseCaptureBuffer();
        private readonly VisemePhraseSpeechEndpoint endpoint =
            new VisemePhraseSpeechEndpoint();
        private bool recording;
        private bool subscribed;
        private bool hasLatestSampleClock;
        private long latestSampleClock;
        private int latestSampleRate;

        internal bool IsRunning => runner != null && VisemeTestPreviewSession.IsRunning(runner);
        internal bool IsRecording => recording;
        internal float Voice { get; private set; }
        internal int Viseme { get; private set; }
        internal string Backend { get; private set; } = string.Empty;
        internal bool HasAnalysisFrame { get; private set; }
        internal long AnalysisFrameCount { get; private set; }
        internal double RecordingDuration => recording
            ? endpoint.ElapsedSeconds
            : 0d;
        internal double SpeechDuration => recording
            ? endpoint.ConfirmedSpeechSeconds
            : 0d;
        internal bool ShouldFinishTake => recording && endpoint.IsComplete;
        internal bool HasConfirmedSpeech => recording && endpoint.HasConfirmedSpeech;
        internal double EndingSilenceDuration => recording ? endpoint.SilenceSeconds : 0d;
        internal VisemePhraseSpeechEndpointState EndpointState => endpoint.State;
        internal VisemePhraseSpeechEndpointReason EndpointReason => endpoint.Reason;

        internal static string BackendAvailability => VisemeTestPreviewSession.ExactBackendStatus();

        internal bool Start(
            GameObject avatarRoot,
            string microphoneDevice,
            VisemeTestAnalysisBackend backend,
            float microphoneGain,
            float noiseGate,
            out string error)
        {
            DisposeRunner();
            error = string.Empty;

            if (avatarRoot == null)
            {
                error = "The avatar is missing.";
                return false;
            }

            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>() ??
                             avatarRoot.GetComponentInChildren<VRCAvatarDescriptor>(true);
            if (descriptor == null)
            {
                error = "The selected object does not contain a VRChat Avatar Descriptor.";
                return false;
            }

            var devices = Microphone.devices ?? Array.Empty<string>();
            if (devices.Length == 0)
            {
                error = "Unity did not report any microphone devices.";
                return false;
            }
            if (!string.IsNullOrEmpty(microphoneDevice) && !devices.Contains(microphoneDevice))
            {
                error = $"Microphone '{microphoneDevice}' is no longer available.";
                return false;
            }

            runnerObject = new GameObject("YUCP Phrase Enrollment Preview")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            runnerObject.transform.SetParent(descriptor.transform, false);
            runner = runnerObject.AddComponent<VisemeTestEmulatorData>();
            runner.hideFlags = HideFlags.HideAndDontSave;
            runner.input = VisemeTestInput.Microphone;
            runner.microphoneDevice = microphoneDevice ?? string.Empty;
            runner.analysisBackend = backend;
            runner.microphoneGain = Mathf.Clamp(microphoneGain, 0.1f, 5f);
            runner.noiseGate = Mathf.Clamp(noiseGate, 0f, 0.2f);
            runner.driveAnimator = false;
            runner.driveGestureManager = false;
            runner.startWithPlayMode = false;

            losslessAnalysisScope = VisemeTestPreviewSession.BeginLosslessAnalysis(runner);
            Subscribe();
            if (VisemeTestPreviewSession.Start(runner, out error)) return true;

            DisposeRunner();
            return false;
        }

        internal void BeginTake()
        {
            if (!IsRunning) throw new InvalidOperationException("The enrollment microphone is not running.");
            buffer.Clear();
            recording = true;
            if (hasLatestSampleClock)
                endpoint.BeginWithNoiseCalibration(latestSampleClock, latestSampleRate);
            else
                endpoint.Begin();
        }

        internal VisemePhraseCapturedTake FinishTake(
            bool constrainToConfirmedSpeech = true)
        {
            recording = false;
            var onset = constrainToConfirmedSpeech
                ? endpoint.ConfirmedOnsetClock
                : -1L;
            return buffer.Finish(onset);
        }

        internal void CancelTake()
        {
            recording = false;
            buffer.Clear();
            endpoint.Begin();
        }

        private void OnAnalysisFrame(VisemeTestPreviewSession.AnalysisSample sample)
        {
            if (sample.source != runner) return;
            HasAnalysisFrame = true;
            AnalysisFrameCount++;
            Voice = sample.voice;
            Viseme = sample.viseme;
            Backend = sample.engineName ?? string.Empty;
            if (sample.sampleRate > 0 && sample.sampleClock >= 0L &&
                (!hasLatestSampleClock ||
                 sample.sampleRate != latestSampleRate ||
                 sample.sampleClock > latestSampleClock))
            {
                hasLatestSampleClock = true;
                latestSampleClock = sample.sampleClock;
                latestSampleRate = sample.sampleRate;
            }
            if (!recording)
            {
                // The microphone already runs before the creator speaks. Reuse
                // those hard-silence frames as the next take's robust noise prior
                // instead of making every take relearn the room from scratch.
                endpoint.ObserveAmbient(sample.voice, sample.viseme);
                return;
            }
            if (!buffer.Append(
                sample.viseme,
                sample.voice,
                sample.sampleClock,
                sample.sampleRate,
                sample.engineName))
                return;
            endpoint.Observe(
                sample.sampleClock,
                sample.sampleRate,
                sample.voice,
                sample.viseme);
        }

        public void Dispose()
        {
            DisposeRunner();
            GC.SuppressFinalize(this);
        }

        private void DisposeRunner()
        {
            Unsubscribe();
            recording = false;
            buffer.Clear();
            if (runner != null) VisemeTestPreviewSession.Stop(runner);
            losslessAnalysisScope?.Dispose();
            losslessAnalysisScope = null;
            runner = null;
            if (runnerObject != null) UnityEngine.Object.DestroyImmediate(runnerObject);
            runnerObject = null;
            Voice = 0f;
            Viseme = 0;
            Backend = string.Empty;
            HasAnalysisFrame = false;
            AnalysisFrameCount = 0L;
            hasLatestSampleClock = false;
            latestSampleClock = 0L;
            latestSampleRate = 0;
            endpoint.Begin();
        }

        private void Subscribe()
        {
            if (subscribed) return;
            VisemeTestPreviewSession.AnalysisFrameProcessed += OnAnalysisFrame;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            VisemeTestPreviewSession.AnalysisFrameProcessed -= OnAnalysisFrame;
            subscribed = false;
        }
    }
}
