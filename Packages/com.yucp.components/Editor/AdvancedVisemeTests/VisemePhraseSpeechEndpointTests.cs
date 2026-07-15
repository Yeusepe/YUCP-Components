using System;
using NUnit.Framework;
using YUCP.Components.Editor.VisemePhrase;

namespace YUCP.Components.Editor.Tests
{
    public sealed class VisemePhraseSpeechEndpointTests
    {
        private const int SampleRate = 1000;

        [Test]
        public void BriefSpikeDoesNotConfirmSpeech()
        {
            var detector = Detector();

            Assert.That(detector.Observe(10, SampleRate, 0.7f), Is.False);
            Assert.That(detector.State,
                Is.EqualTo(VisemePhraseSpeechEndpointState.ConfirmingOnset));
            Assert.That(detector.Observe(60, SampleRate, 0.2f), Is.False);
            Assert.That(detector.State,
                Is.EqualTo(VisemePhraseSpeechEndpointState.WaitingForSpeech));
            Assert.That(detector.HasConfirmedSpeech, Is.False);
            Assert.That(detector.ElapsedSeconds, Is.EqualTo(0.06d).Within(1e-9));
        }

        [Test]
        public void SustainedOnsetConfirmsBySampleClock()
        {
            var detector = Detector();

            detector.Observe(10, SampleRate, 0.7f);
            detector.Observe(60, SampleRate, 0.4f);
            Assert.That(detector.HasConfirmedSpeech, Is.False);
            detector.Observe(110, SampleRate, 0.4f);

            Assert.That(detector.HasConfirmedSpeech, Is.True);
            Assert.That(detector.State,
                Is.EqualTo(VisemePhraseSpeechEndpointState.Speaking));
            Assert.That(detector.ConfirmedSpeechSeconds, Is.EqualTo(0.1d).Within(1e-9));
        }

        [Test]
        public void HysteresisKeepsSpeechActiveBetweenThresholds()
        {
            var detector = Detector();
            ConfirmSpeech(detector);

            detector.Observe(160, SampleRate, 0.4f);
            detector.Observe(260, SampleRate, 0.31f);

            Assert.That(detector.State,
                Is.EqualTo(VisemePhraseSpeechEndpointState.Speaking));
            Assert.That(detector.SilenceSeconds, Is.Zero.Within(1e-9));
        }

        [Test]
        public void HangoverAbsorbsShortPauseAndSpeechCanResume()
        {
            var detector = Detector();
            ConfirmSpeech(detector);

            detector.Observe(250, SampleRate, 0f);
            Assert.That(detector.State,
                Is.EqualTo(VisemePhraseSpeechEndpointState.Speaking));
            detector.Observe(260, SampleRate, 0.35f);
            detector.Observe(400, SampleRate, 0f);
            Assert.That(detector.State,
                Is.EqualTo(VisemePhraseSpeechEndpointState.Speaking));
            detector.Observe(410, SampleRate, 0f);

            Assert.That(detector.State,
                Is.EqualTo(VisemePhraseSpeechEndpointState.EndingSilence));
            Assert.That(detector.IsComplete, Is.False);
        }

        [Test]
        public void ConfirmedSpeechEndsAfterConfiguredSilence()
        {
            var detector = Detector();
            ConfirmSpeech(detector);
            detector.Observe(200, SampleRate, 0.5f);

            detector.Observe(350, SampleRate, 0f);
            Assert.That(detector.State,
                Is.EqualTo(VisemePhraseSpeechEndpointState.EndingSilence));
            Assert.That(detector.Observe(599, SampleRate, 0f), Is.False);
            Assert.That(detector.Observe(600, SampleRate, 0f), Is.True);

            Assert.That(detector.State,
                Is.EqualTo(VisemePhraseSpeechEndpointState.Complete));
            Assert.That(detector.Reason,
                Is.EqualTo(VisemePhraseSpeechEndpointReason.EndOfSpeech));
        }

        [Test]
        public void DelayedBatchSampleCanReachEndpointWithoutAnExtraFrame()
        {
            var detector = Detector();
            ConfirmSpeech(detector);
            detector.Observe(200, SampleRate, 0.5f);

            Assert.That(detector.Observe(800, SampleRate, 0f), Is.True);
            Assert.That(detector.Reason,
                Is.EqualTo(VisemePhraseSpeechEndpointReason.EndOfSpeech));
        }

        [Test]
        public void NoSpeechUsesSeparateWaitingTimeout()
        {
            var detector = new VisemePhraseSpeechEndpoint(
                new VisemePhraseSpeechEndpointSettings(
                    0.6f, 0.3f, 0.1d, 0.15d, 0.4d, 2d, 2d));
            detector.Begin(0L, SampleRate);

            Assert.That(detector.Observe(1999, SampleRate, 0f), Is.False);
            Assert.That(detector.Observe(2000, SampleRate, 0f), Is.True);
            Assert.That(detector.HasConfirmedSpeech, Is.False);
            Assert.That(detector.Reason,
                Is.EqualTo(VisemePhraseSpeechEndpointReason.WaitingTimeout));
        }

        [Test]
        public void ContinuousSpeechStillStopsAtMaximumDuration()
        {
            var detector = Detector();
            ConfirmSpeech(detector);

            Assert.That(detector.Observe(2009, SampleRate, 0.8f), Is.False);
            Assert.That(detector.Observe(2010, SampleRate, 0.8f), Is.True);
            Assert.That(detector.Reason,
                Is.EqualTo(VisemePhraseSpeechEndpointReason.MaximumDuration));
        }

        [Test]
        public void InvalidRegressingAndRateChangedSamplesDoNotMoveTimer()
        {
            var detector = Detector(1000);
            detector.Observe(1100, SampleRate, 0.7f);
            var elapsed = detector.ElapsedSeconds;

            Assert.That(detector.Observe(1050, SampleRate, 0.7f), Is.False);
            Assert.That(detector.Observe(1200, 800, 0.7f), Is.False);
            Assert.That(detector.Observe(-1, SampleRate, 0.7f), Is.False);
            Assert.That(detector.ElapsedSeconds, Is.EqualTo(elapsed).Within(1e-9));
            Assert.That(double.IsNaN(detector.ElapsedSeconds), Is.False);
            Assert.That(double.IsInfinity(detector.ElapsedSeconds), Is.False);
        }

        [Test]
        public void ElapsedTimerContinuesAfterEndpointUntilCaptureConsumesIt()
        {
            var detector = Detector();
            ConfirmSpeech(detector);
            detector.Observe(200, SampleRate, 0.5f);
            Assert.That(detector.Observe(600, SampleRate, 0f), Is.True);
            Assert.That(detector.ElapsedSeconds, Is.EqualTo(0.6d).Within(1e-9));

            Assert.That(detector.Observe(1000, SampleRate, 0f), Is.False);
            Assert.That(detector.ElapsedSeconds, Is.EqualTo(1d).Within(1e-9));
            Assert.That(detector.Reason,
                Is.EqualTo(VisemePhraseSpeechEndpointReason.EndOfSpeech));
        }

        [Test]
        public void EndpointTimingIsConsistentAcrossAnalysisFrameRates()
        {
            var fast = RunSyntheticPhrase(10);
            var slow = RunSyntheticPhrase(25);

            Assert.That(fast.reason,
                Is.EqualTo(VisemePhraseSpeechEndpointReason.EndOfSpeech));
            Assert.That(slow.reason,
                Is.EqualTo(VisemePhraseSpeechEndpointReason.EndOfSpeech));
            Assert.That(Math.Abs(fast.seconds - slow.seconds), Is.LessThanOrEqualTo(0.03d));
        }

        [Test]
        public void DefaultsMatchEightSecondPhraseTakeContract()
        {
            var settings = VisemePhraseSpeechEndpointSettings.Default;

            Assert.That(settings.onsetThreshold, Is.GreaterThan(settings.releaseThreshold));
            Assert.That(settings.onsetConfirmationSeconds, Is.GreaterThan(0d));
            Assert.That(settings.endSilenceSeconds,
                Is.GreaterThanOrEqualTo(settings.hangoverSeconds));
            Assert.That(settings.endSilenceSeconds, Is.GreaterThanOrEqualTo(0.5d));
            Assert.That(settings.maximumDurationSeconds, Is.EqualTo(8d));
            Assert.That(settings.maximumWaitingSeconds, Is.EqualTo(30d));
        }

        [Test]
        public void DefaultEndpointDoesNotSplitAWordLengthPause()
        {
            var detector = new VisemePhraseSpeechEndpoint();
            detector.Begin(0L, SampleRate);
            detector.Observe(10, SampleRate, 0.5f, 10);
            detector.Observe(60, SampleRate, 0.4f, 10);

            Assert.That(detector.HasConfirmedSpeech, Is.True);
            Assert.That(detector.Observe(300, SampleRate, 0f, 0), Is.False);
            Assert.That(detector.Observe(599, SampleRate, 0f, 0), Is.False);
            Assert.That(detector.Observe(610, SampleRate, 0f, 0), Is.True);
            Assert.That(detector.Reason,
                Is.EqualTo(VisemePhraseSpeechEndpointReason.EndOfSpeech));
        }

        [Test]
        public void QuietSpeechCanStartWhenAVisemeCorroboratesIt()
        {
            var detector = new VisemePhraseSpeechEndpoint();
            detector.Begin(0L, SampleRate);

            detector.Observe(10, SampleRate, 0.012f, 10);
            detector.Observe(60, SampleRate, 0.012f, 10);

            Assert.That(detector.HasConfirmedSpeech, Is.True);
            Assert.That(detector.State,
                Is.EqualTo(VisemePhraseSpeechEndpointState.Speaking));
        }

        [Test]
        public void IdleNoiseCalibrationLetsImmediateQuietSpeechStartBeforeVisemeSettles()
        {
            var detector = new VisemePhraseSpeechEndpoint();
            for (var i = 0; i < 12; i++)
                detector.ObserveAmbient(0.004f + i % 2 * 0.001f, 0);

            detector.BeginWithNoiseCalibration(1000L, SampleRate);
            detector.Observe(1010L, SampleRate, 0.04f, 0);
            detector.Observe(1060L, SampleRate, 0.04f, 0);

            Assert.That(detector.HasConfirmedSpeech, Is.True,
                "A pre-calibrated take should not consume its first quiet syllable as noise warmup.");
            Assert.That(detector.ConfirmedOnsetClock, Is.EqualTo(1010L));
            Assert.That(detector.State,
                Is.EqualTo(VisemePhraseSpeechEndpointState.Speaking));
        }

        [Test]
        public void AmbientCalibrationIgnoresSpeechShapedAnalyzerFrames()
        {
            var detector = new VisemePhraseSpeechEndpoint();
            for (var i = 0; i < 12; i++)
                detector.ObserveAmbient(0.8f, 10);

            detector.BeginWithNoiseCalibration(1000L, SampleRate);
            detector.Observe(1010L, SampleRate, 0.04f, 0);
            detector.Observe(1060L, SampleRate, 0.04f, 0);

            Assert.That(detector.HasConfirmedSpeech, Is.False,
                "Non-silence visemes must not be mistaken for an idle-room calibration.");
        }

        [Test]
        public void StaleNonSilenceVisemeCannotHoldRecordingOpen()
        {
            var detector = new VisemePhraseSpeechEndpoint();
            detector.Begin(0L, SampleRate);
            detector.Observe(10, SampleRate, 0.4f, 10);
            detector.Observe(60, SampleRate, 0.4f, 10);

            detector.Observe(200, SampleRate, 0f, 10);
            Assert.That(detector.State,
                Is.EqualTo(VisemePhraseSpeechEndpointState.EndingSilence));
            Assert.That(detector.Observe(610, SampleRate, 0f, 10), Is.True);
        }

        [Test]
        public void LowEnergyVisemeFlickerCannotRenewTheFinishDelayForever()
        {
            var detector = new VisemePhraseSpeechEndpoint();
            for (var i = 0; i < 12; i++) detector.ObserveAmbient(0.04f, 0);
            detector.BeginWithNoiseCalibration(0L, SampleRate);
            detector.Observe(10L, SampleRate, 0.2f, 10);
            detector.Observe(60L, SampleRate, 0.2f, 10);
            Assert.That(detector.HasConfirmedSpeech, Is.True);

            for (var clock = 110L; clock <= 560L; clock += 50L)
            {
                Assert.That(detector.Observe(
                    clock,
                    SampleRate,
                    0.04f,
                    (clock / 50L & 1L) == 0L ? 4 : 5), Is.False);
            }

            Assert.That(detector.Observe(610L, SampleRate, 0.04f, 4), Is.True);
            Assert.That(detector.Reason,
                Is.EqualTo(VisemePhraseSpeechEndpointReason.EndOfSpeech));
        }

        [Test]
        public void RobustNoiseFloorRaisesBothSchmittThresholds()
        {
            var detector = new VisemePhraseSpeechEndpoint();
            detector.Begin(0L, SampleRate);
            var noise = new[] { 0.016f, 0.018f, 0.019f, 0.017f, 0.018f, 0.019f, 0.016f, 0.018f };
            for (var i = 0; i < noise.Length; i++)
                detector.Observe((i + 1) * 10L, SampleRate, noise[i], 0);

            Assert.That(detector.AdaptiveOnsetThreshold, Is.GreaterThan(0.02f));
            Assert.That(detector.AdaptiveReleaseThreshold,
                Is.LessThan(detector.AdaptiveOnsetThreshold));
            Assert.That(detector.AdaptiveReleaseThreshold, Is.GreaterThan(0.008f));
            Assert.That(detector.HasConfirmedSpeech, Is.False);
        }

        [Test]
        public void SteadyNoiseAboveBaseThresholdDoesNotBecomeSpeechAtAnalyzerCadence()
        {
            const int analyzerRate = 48000;
            const long analyzerBlock = 1024L;
            var detector = new VisemePhraseSpeechEndpoint();
            detector.Begin(0L, analyzerRate);

            for (var block = 1; block <= 20; block++)
                Assert.That(detector.Observe(
                    block * analyzerBlock,
                    analyzerRate,
                    0.04f,
                    0), Is.False);

            Assert.That(detector.HasConfirmedSpeech, Is.False);
            Assert.That(detector.State,
                Is.EqualTo(VisemePhraseSpeechEndpointState.WaitingForSpeech));
            Assert.That(detector.AdaptiveOnsetThreshold, Is.GreaterThan(0.04f));
            Assert.That(detector.AdaptiveReleaseThreshold,
                Is.LessThan(detector.AdaptiveOnsetThreshold));
        }

        [Test]
        public void SettingsAlwaysPreserveHysteresisAndSilenceOrdering()
        {
            var settings = new VisemePhraseSpeechEndpointSettings(
                0.1f,
                0.9f,
                double.NaN,
                0.5d,
                0.1d,
                double.PositiveInfinity);

            Assert.That(settings.onsetThreshold, Is.GreaterThan(settings.releaseThreshold));
            Assert.That(settings.endSilenceSeconds,
                Is.GreaterThanOrEqualTo(settings.hangoverSeconds));
            Assert.That(double.IsNaN(settings.onsetConfirmationSeconds), Is.False);
            Assert.That(double.IsInfinity(settings.maximumDurationSeconds), Is.False);
            Assert.That(double.IsInfinity(settings.maximumWaitingSeconds), Is.False);
        }

        private static VisemePhraseSpeechEndpoint Detector(long startClock = 0L)
        {
            var detector = new VisemePhraseSpeechEndpoint(
                new VisemePhraseSpeechEndpointSettings(
                    0.6f,
                    0.3f,
                    0.1d,
                    0.15d,
                    0.4d,
                    2d));
            detector.Begin(startClock, SampleRate);
            return detector;
        }

        private static void ConfirmSpeech(VisemePhraseSpeechEndpoint detector)
        {
            detector.Observe(10, SampleRate, 0.7f);
            detector.Observe(110, SampleRate, 0.4f);
            Assert.That(detector.HasConfirmedSpeech, Is.True);
        }

        private static (double seconds, VisemePhraseSpeechEndpointReason reason)
            RunSyntheticPhrase(int frameMilliseconds)
        {
            var detector = new VisemePhraseSpeechEndpoint(
                new VisemePhraseSpeechEndpointSettings(
                    0.6f,
                    0.3f,
                    0.1d,
                    0.15d,
                    0.4d,
                    2.5d));
            detector.Begin(0L, SampleRate);
            for (var clock = frameMilliseconds; clock <= 2500; clock += frameMilliseconds)
            {
                var seconds = clock / 1000d;
                var voice = seconds >= 0.1d && seconds < 0.8d ? 0.7f : 0f;
                if (!detector.Observe(clock, SampleRate, voice)) continue;
                return (detector.ElapsedSeconds, detector.Reason);
            }
            Assert.Fail("Synthetic phrase did not reach an endpoint.");
            return default;
        }
    }
}
