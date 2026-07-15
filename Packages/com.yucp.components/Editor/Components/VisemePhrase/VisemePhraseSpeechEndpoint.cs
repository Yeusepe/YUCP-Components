using System;

namespace YUCP.Components.Editor.VisemePhrase
{
    internal enum VisemePhraseSpeechEndpointState
    {
        WaitingForSpeech,
        ConfirmingOnset,
        Speaking,
        EndingSilence,
        Complete
    }

    internal enum VisemePhraseSpeechEndpointReason
    {
        None,
        EndOfSpeech,
        MaximumDuration,
        WaitingTimeout
    }

    internal readonly struct VisemePhraseSpeechEndpointSettings
    {
        internal readonly float onsetThreshold;
        internal readonly float releaseThreshold;
        internal readonly double onsetConfirmationSeconds;
        internal readonly double hangoverSeconds;
        internal readonly double endSilenceSeconds;
        internal readonly double maximumDurationSeconds;
        internal readonly double maximumWaitingSeconds;

        internal static VisemePhraseSpeechEndpointSettings Default =>
            new VisemePhraseSpeechEndpointSettings(
                0.02f,
                0.008f,
                0.04d,
                0.08d,
                0.55d,
                8d,
                30d);

        internal VisemePhraseSpeechEndpointSettings(
            float onsetThreshold,
            float releaseThreshold,
            double onsetConfirmationSeconds,
            double hangoverSeconds,
            double endSilenceSeconds,
            double maximumDurationSeconds,
            double maximumWaitingSeconds = 30d)
        {
            this.releaseThreshold = Math.Min(
                0.999f,
                Clamp01(Finite(releaseThreshold) ? releaseThreshold : 0.008f));
            this.onsetThreshold = Math.Max(
                this.releaseThreshold + 0.001f,
                Clamp01(Finite(onsetThreshold) ? onsetThreshold : 0.02f));
            this.onsetConfirmationSeconds = NonNegative(onsetConfirmationSeconds, 0.04d);
            this.hangoverSeconds = NonNegative(hangoverSeconds, 0.08d);
            this.endSilenceSeconds = Math.Max(
                this.hangoverSeconds,
                NonNegative(endSilenceSeconds, 0.55d));
            this.maximumDurationSeconds = Math.Max(
                0.001d,
                NonNegative(maximumDurationSeconds, 8d));
            this.maximumWaitingSeconds = Math.Max(
                0.001d,
                NonNegative(maximumWaitingSeconds, 30d));
        }

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));

        private static double NonNegative(double value, double fallback) =>
            double.IsNaN(value) || double.IsInfinity(value)
                ? fallback
                : Math.Max(0d, value);
    }

    /// <summary>
    /// Causal speech endpoint detector driven only by analyzer sample clocks.
    /// It deliberately has no dependency on editor frames or wall-clock time, so
    /// delayed batches of audio blocks produce exactly the same decision.
    /// </summary>
    internal sealed class VisemePhraseSpeechEndpoint
    {
        private const int NoiseWindowSize = 48;
        private const int MinimumNoiseSamples = 8;
        private const double NoiseWarmupSeconds = 0.12d;
        private const double RecentVisemeTransitionSeconds = 0.08d;

        private readonly VisemePhraseSpeechEndpointSettings settings;
        private readonly float[] noiseWindow = new float[NoiseWindowSize];
        private readonly float[] noiseScratch = new float[NoiseWindowSize];
        private bool clockInitialized;
        private int sampleRate;
        private int noiseSampleCount;
        private int noiseWriteIndex;
        private int lastViseme;
        private long startClock;
        private long latestClock;
        private long onsetCandidateClock;
        private long confirmedOnsetClock;
        private long lastSpeechClock;
        private long lastVisemeChangeClock;
        private float adaptiveOnsetThreshold;
        private float adaptiveReleaseThreshold;
        private bool hadNoiseCalibrationAtBegin;

        internal VisemePhraseSpeechEndpointState State { get; private set; }
        internal VisemePhraseSpeechEndpointReason Reason { get; private set; }
        internal bool HasConfirmedSpeech { get; private set; }
        internal long ConfirmedOnsetClock => HasConfirmedSpeech
            ? confirmedOnsetClock
            : -1L;
        internal bool IsComplete => State == VisemePhraseSpeechEndpointState.Complete;
        internal double ElapsedSeconds => SecondsBetween(startClock, latestClock);
        internal double ConfirmedSpeechSeconds => HasConfirmedSpeech
            ? SecondsBetween(confirmedOnsetClock, latestClock)
            : 0d;
        internal double SilenceSeconds => HasConfirmedSpeech
            ? SecondsBetween(lastSpeechClock, latestClock)
            : 0d;
        internal float AdaptiveOnsetThreshold => adaptiveOnsetThreshold;
        internal float AdaptiveReleaseThreshold => adaptiveReleaseThreshold;

        internal VisemePhraseSpeechEndpoint()
            : this(VisemePhraseSpeechEndpointSettings.Default)
        {
        }

        internal VisemePhraseSpeechEndpoint(
            VisemePhraseSpeechEndpointSettings settings)
        {
            this.settings = settings;
            Begin();
        }

        internal void Begin()
        {
            ResetDetectionState(true);
        }

        private void ResetDetectionState(bool clearNoiseCalibration)
        {
            clockInitialized = false;
            sampleRate = 0;
            startClock = 0L;
            latestClock = 0L;
            onsetCandidateClock = 0L;
            confirmedOnsetClock = 0L;
            lastSpeechClock = 0L;
            lastVisemeChangeClock = 0L;
            lastViseme = 0;
            hadNoiseCalibrationAtBegin = !clearNoiseCalibration &&
                                         noiseSampleCount >= MinimumNoiseSamples;
            if (clearNoiseCalibration)
            {
                noiseSampleCount = 0;
                noiseWriteIndex = 0;
                Array.Clear(noiseWindow, 0, noiseWindow.Length);
                Array.Clear(noiseScratch, 0, noiseScratch.Length);
                adaptiveOnsetThreshold = settings.onsetThreshold;
                adaptiveReleaseThreshold = settings.releaseThreshold;
            }
            State = VisemePhraseSpeechEndpointState.WaitingForSpeech;
            Reason = VisemePhraseSpeechEndpointReason.None;
            HasConfirmedSpeech = false;
        }

        internal void Begin(long sampleClock, int sampleRate)
        {
            Begin();
            if (sampleClock < 0L || sampleRate <= 0) return;
            InitializeClock(sampleClock, sampleRate);
        }

        /// <summary>
        /// Starts a new utterance while retaining the idle microphone noise model.
        /// Enrollment keeps the analyzer running between takes, so throwing this
        /// information away would make the first quiet syllable double as a fresh
        /// calibration window and could prevent the recording timer from starting.
        /// </summary>
        internal void BeginWithNoiseCalibration(long sampleClock, int sampleRate)
        {
            ResetDetectionState(false);
            if (sampleClock < 0L || sampleRate <= 0) return;
            InitializeClock(sampleClock, sampleRate);
        }

        /// <summary>
        /// Learns from analyzer frames while enrollment is armed but not recording.
        /// Only hard silence is accepted; speech-shaped frames cannot raise the
        /// threshold that the following take must cross.
        /// </summary>
        internal void ObserveAmbient(float voice, int viseme = 0)
        {
            if (Math.Max(0, Math.Min(14, viseme)) != 0) return;
            ObserveNoise(NormalizeVoice(voice));
        }

        /// <summary>
        /// Returns true only on the sample that first reaches an endpoint.
        /// Samples after completion continue advancing ElapsedSeconds so legacy
        /// maximum-duration UI remains safe until it consumes the endpoint signal.
        /// </summary>
        internal bool Observe(
            long sampleClock,
            int sampleRate,
            float voice,
            int viseme = 0)
        {
            if (sampleClock < 0L || sampleRate <= 0) return false;
            if (!clockInitialized)
                InitializeClock(sampleClock, sampleRate);
            else if (sampleRate != this.sampleRate || sampleClock <= latestClock)
                return false;
            else
                latestClock = sampleClock;

            if (IsComplete) return false;
            if (!HasConfirmedSpeech &&
                ElapsedSeconds >= settings.maximumWaitingSeconds)
                return Complete(VisemePhraseSpeechEndpointReason.WaitingTimeout);
            if (HasConfirmedSpeech &&
                ConfirmedSpeechSeconds >= settings.maximumDurationSeconds)
                return Complete(VisemePhraseSpeechEndpointReason.MaximumDuration);

            voice = NormalizeVoice(voice);
            viseme = Math.Max(0, Math.Min(14, viseme));
            if (viseme != lastViseme)
            {
                lastViseme = viseme;
                lastVisemeChangeClock = sampleClock;
            }

            var nonSilenceViseme = viseme != 0;
            if (State == VisemePhraseSpeechEndpointState.WaitingForSpeech &&
                !nonSilenceViseme &&
                (noiseSampleCount < MinimumNoiseSamples ||
                 ElapsedSeconds < NoiseWarmupSeconds ||
                 voice < adaptiveOnsetThreshold))
            {
                ObserveNoise(voice);
            }

            var recentVisemeTransition = nonSilenceViseme &&
                SecondsBetween(lastVisemeChangeClock, sampleClock) <=
                RecentVisemeTransitionSeconds;
            var startsSpeech = voice >= adaptiveOnsetThreshold ||
                               (nonSilenceViseme &&
                                voice >= adaptiveReleaseThreshold);
            var hasEnergyEvidence = voice >= adaptiveReleaseThreshold;
            var bridgesUnvoicedTransition = nonSilenceViseme &&
                recentVisemeTransition &&
                voice >= adaptiveReleaseThreshold * 0.75f &&
                SecondsBetween(lastSpeechClock, sampleClock) <=
                RecentVisemeTransitionSeconds;
            var continuesSpeech = hasEnergyEvidence || bridgesUnvoicedTransition;

            // Give the robust noise estimate a brief head start. A real mouth
            // shape transition can still start immediately, so this never adds
            // perceptible latency to analyzed speech.
            var learningNoise = noiseSampleCount < MinimumNoiseSamples ||
                                (!hadNoiseCalibrationAtBegin &&
                                 ElapsedSeconds < NoiseWarmupSeconds);
            if (settings.onsetThreshold <= 0.05f &&
                learningNoise && !nonSilenceViseme &&
                voice < Math.Max(0.15f, adaptiveOnsetThreshold * 3f))
                startsSpeech = false;

            switch (State)
            {
                case VisemePhraseSpeechEndpointState.WaitingForSpeech:
                    if (startsSpeech)
                    {
                        onsetCandidateClock = sampleClock;
                        lastSpeechClock = sampleClock;
                        State = VisemePhraseSpeechEndpointState.ConfirmingOnset;
                    }
                    break;

                case VisemePhraseSpeechEndpointState.ConfirmingOnset:
                    if (!continuesSpeech)
                    {
                        State = VisemePhraseSpeechEndpointState.WaitingForSpeech;
                        break;
                    }
                    if (hasEnergyEvidence) lastSpeechClock = sampleClock;
                    if (SecondsBetween(onsetCandidateClock, sampleClock) >=
                        settings.onsetConfirmationSeconds)
                    {
                        confirmedOnsetClock = onsetCandidateClock;
                        HasConfirmedSpeech = true;
                        State = VisemePhraseSpeechEndpointState.Speaking;
                    }
                    break;

                case VisemePhraseSpeechEndpointState.Speaking:
                    if (continuesSpeech)
                    {
                        // A classifier transition can bridge one unvoiced block,
                        // but it cannot recursively renew itself. Otherwise quiet
                        // background label flicker can keep a finished take open
                        // until the maximum-duration failsafe.
                        if (hasEnergyEvidence) lastSpeechClock = sampleClock;
                        break;
                    }
                    if (SilenceSeconds >= settings.endSilenceSeconds)
                        return Complete(VisemePhraseSpeechEndpointReason.EndOfSpeech);
                    if (SilenceSeconds >= settings.hangoverSeconds)
                        State = VisemePhraseSpeechEndpointState.EndingSilence;
                    break;

                case VisemePhraseSpeechEndpointState.EndingSilence:
                    if (continuesSpeech)
                    {
                        if (hasEnergyEvidence) lastSpeechClock = sampleClock;
                        State = VisemePhraseSpeechEndpointState.Speaking;
                    }
                    else if (SilenceSeconds >= settings.endSilenceSeconds)
                    {
                        return Complete(VisemePhraseSpeechEndpointReason.EndOfSpeech);
                    }
                    break;
            }

            return false;
        }

        private void ObserveNoise(float voice)
        {
            noiseWindow[noiseWriteIndex] = voice;
            noiseWriteIndex = (noiseWriteIndex + 1) % noiseWindow.Length;
            if (noiseSampleCount < noiseWindow.Length) noiseSampleCount++;
            if (noiseSampleCount < MinimumNoiseSamples) return;

            Array.Copy(noiseWindow, noiseScratch, noiseSampleCount);
            Array.Sort(noiseScratch, 0, noiseSampleCount);
            var median = Median(noiseScratch, noiseSampleCount);
            for (var i = 0; i < noiseSampleCount; i++)
                noiseScratch[i] = Math.Abs(noiseScratch[i] - median);
            Array.Sort(noiseScratch, 0, noiseSampleCount);
            var sigma = 1.4826f * Median(noiseScratch, noiseSampleCount);
            var onset = Math.Max(
                settings.onsetThreshold,
                median + Math.Max(0.006f, 4f * sigma));
            var release = Math.Max(
                settings.releaseThreshold,
                Math.Max(
                    median + Math.Max(0.003f, 2f * sigma),
                    onset * 0.6f));
            adaptiveOnsetThreshold = Math.Min(1f, onset);
            adaptiveReleaseThreshold = Math.Min(
                adaptiveOnsetThreshold - 0.001f,
                release);
        }

        private static float Median(float[] sorted, int count)
        {
            if (sorted == null || count <= 0) return 0f;
            var middle = count / 2;
            return (count & 1) == 0
                ? (sorted[middle - 1] + sorted[middle]) * 0.5f
                : sorted[middle];
        }

        private void InitializeClock(long sampleClock, int sampleRate)
        {
            clockInitialized = true;
            this.sampleRate = Math.Max(1, sampleRate);
            startClock = Math.Max(0L, sampleClock);
            latestClock = startClock;
        }

        private bool Complete(VisemePhraseSpeechEndpointReason reason)
        {
            State = VisemePhraseSpeechEndpointState.Complete;
            Reason = reason;
            return true;
        }

        private double SecondsBetween(long earlier, long later)
        {
            if (!clockInitialized || sampleRate <= 0 || later <= earlier) return 0d;
            return (later - earlier) / (double)sampleRate;
        }

        private static float NormalizeVoice(float voice)
        {
            if (float.IsNaN(voice) || float.IsInfinity(voice)) return 0f;
            return Math.Max(0f, Math.Min(1f, voice));
        }
    }
}
