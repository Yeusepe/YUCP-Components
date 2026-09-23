#if UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.Components.Editor.Tests
{
    public sealed class AdvancedVisemeSpeechHangoverTests
    {
        private const float ResponseSeconds = 0.024f;
        private const float ConfiguredReleaseSeconds = 0.16f;
        private const float DefaultStability = 0.5f;

        [Test]
        public void CenteredStabilityUsesConfiguredReleaseAndZeroIsAnExactBypass()
        {
            Assert.That(AdvancedVisemeMath.SpeechHistoryReleaseSeconds(
                    ConfiguredReleaseSeconds, 0f),
                Is.EqualTo(ConfiguredReleaseSeconds).Within(1e-7f));
            Assert.That(AdvancedVisemeMath.SpeechHistoryReleaseSeconds(
                    ConfiguredReleaseSeconds, DefaultStability),
                Is.EqualTo(ConfiguredReleaseSeconds).Within(1e-7f));
            Assert.That(AdvancedVisemeMath.SpeechHistoryReleaseSeconds(
                    ConfiguredReleaseSeconds, 1f),
                Is.EqualTo(2f * ConfiguredReleaseSeconds).Within(1e-7f));

            const float deltaTime = 1f / 90f;
            Assert.That(AdvancedVisemeMath.SpeechHistoryReleaseAlpha(
                    deltaTime, ConfiguredReleaseSeconds, 0.25f),
                Is.EqualTo(AdvancedVisemeMath.Alpha(
                    deltaTime, ConfiguredReleaseSeconds)).Within(1e-7f));
            Assert.That(AdvancedVisemeMath.SpeechHistoryReleaseAlpha(
                    deltaTime, ConfiguredReleaseSeconds, 1f),
                Is.EqualTo(AdvancedVisemeMath.Alpha(
                    deltaTime, 2f * ConfiguredReleaseSeconds)).Within(1e-7f));

            Assert.That(AdvancedVisemeMath.SpeechHistoryHoldWeight(1f, 0, 0f),
                Is.Zero);
            Assert.That(AdvancedVisemeMath.SpeechHistoryHoldWeight(1f, 0, 0.25f),
                Is.EqualTo(0.5f).Within(1e-7f));
            Assert.That(AdvancedVisemeMath.SpeechHistoryHoldWeight(
                    1f, 0, DefaultStability),
                Is.EqualTo(1f).Within(1e-7f));
        }

        [Test]
        public void HistoryAndHoldMapAreContinuousAndMonotone()
        {
            var previous = 0f;
            for (var sample = 0; sample <= 1000; sample++)
            {
                var history = sample / 1000f;
                var hold = AdvancedVisemeMath.SpeechHistoryHoldWeight(
                    history, 0, DefaultStability);
                Assert.That(hold, Is.InRange(previous - 1e-7f, 1f));
                previous = hold;
            }

            Assert.That(AdvancedVisemeMath.SpeechHistoryHoldWeight(
                    AdvancedVisemeMath.SpeechHistoryHoldStart, 0, DefaultStability),
                Is.Zero);
            Assert.That(AdvancedVisemeMath.SpeechHistoryHoldWeight(
                    AdvancedVisemeMath.SpeechHistoryHoldFull, 0, DefaultStability),
                Is.EqualTo(1f).Within(1e-7f));

            const float epsilon = 0.0001f;
            var nearStart = AdvancedVisemeMath.SpeechHistoryHoldWeight(
                AdvancedVisemeMath.SpeechHistoryHoldStart + epsilon,
                0, DefaultStability);
            var nearFull = AdvancedVisemeMath.SpeechHistoryHoldWeight(
                AdvancedVisemeMath.SpeechHistoryHoldFull - epsilon,
                0, DefaultStability);
            var expectedEdgeWeight = epsilon /
                                     (AdvancedVisemeMath.SpeechHistoryHoldFull -
                                      AdvancedVisemeMath.SpeechHistoryHoldStart);
            Assert.That(nearStart, Is.EqualTo(expectedEdgeWeight).Within(1e-6f));
            Assert.That(1f - nearFull,
                Is.EqualTo(expectedEdgeWeight).Within(1e-6f));
        }

        [Test]
        public void SustainedSpeechEarnsMoreReleaseInertiaThanAShortBurst()
        {
            var shortState = ChargeHistory(0.02f, 0.002f);
            var mediumState = ChargeHistory(0.04f, 0.002f);
            var sustainedState = ChargeHistory(0.18f, 0.002f);

            var shortHold = MeasureSilenceHold(ref shortState);
            var mediumHold = MeasureSilenceHold(ref mediumState);
            var sustainedHold = MeasureSilenceHold(ref sustainedState);

            Assert.That(shortHold, Is.Zero,
                "A transient shorter than the attack observer should not earn a stale pose.");
            Assert.That(mediumHold, Is.InRange(0.6f, 0.9f));
            Assert.That(sustainedHold, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(shortState.History, Is.LessThan(mediumState.History));
            Assert.That(mediumState.History, Is.LessThan(sustainedState.History));
        }

        [Test]
        public void AsymmetricHistoryIsFrameRateCorrect()
        {
            float? referenceCharged = null;
            float? referenceReleased = null;
            foreach (var fps in new[] { 15, 20, 30, 45, 60, 90, 144 })
            {
                var state = default(AdvancedVisemeMath.SpeechHistoryState);
                Advance(0.18f, fps, dt => AdvancedVisemeMath.StepSpeechHistory(
                    10, 0f, dt, ConfiguredReleaseSeconds, DefaultStability, ref state));

                var expectedCharged = 1f - Mathf.Exp(
                    -0.18f / AdvancedVisemeMath.SpeechHistoryAttackSeconds);
                Assert.That(state.History,
                    Is.EqualTo(expectedCharged).Within(2e-6f), $"{fps} FPS attack");
                if (referenceCharged.HasValue)
                    Assert.That(state.History,
                        Is.EqualTo(referenceCharged.Value).Within(2e-6f));
                else
                    referenceCharged = state.History;

                Advance(0.12f, fps, dt => AdvancedVisemeMath.StepSpeechHistory(
                    0, 1f, dt, ConfiguredReleaseSeconds, DefaultStability, ref state));
                var expectedReleased = expectedCharged * Mathf.Exp(
                    -0.12f / ConfiguredReleaseSeconds);
                Assert.That(state.History,
                    Is.EqualTo(expectedReleased).Within(2e-6f), $"{fps} FPS release");
                if (referenceReleased.HasValue)
                    Assert.That(state.History,
                        Is.EqualTo(referenceReleased.Value).Within(2e-6f));
                else
                    referenceReleased = state.History;
            }
        }

        [Test]
        public void SilenceHistoryDecaysMonotonicallyAndEventuallyReleases()
        {
            var state = ChargeHistory(0.8f, 0.005f);
            var previousHistory = state.History;
            var previousHold = MeasureSilenceHold(ref state);
            Assert.That(previousHold, Is.EqualTo(1f).Within(1e-6f));

            for (var frame = 0; frame < 180; frame++)
            {
                AdvancedVisemeMath.StepSpeechHistory(
                    0, 1f, 1f / 90f, ConfiguredReleaseSeconds,
                    DefaultStability, ref state);
                Assert.That(state.History, Is.InRange(0f, previousHistory + 1e-7f));
                Assert.That(state.HoldWeight, Is.InRange(0f, previousHold + 1e-7f));
                previousHistory = state.History;
                previousHold = state.HoldWeight;
            }

            Assert.That(state.History, Is.LessThan(1e-5f));
            Assert.That(state.HoldWeight, Is.Zero);
            Assert.That(state.Presence, Is.LessThan(1e-5f));
        }

        [Test]
        public void FullHoldFreezesFastStageWhileSlowStageSettles()
        {
            foreach (var fps in new[] { 15, 30, 60, 90, 144 })
            {
                var state = default(AdvancedVisemeMath.SpeechHistoryState);
                var fast = NewSilenceSimplex();
                var slow = NewSilenceSimplex();
                Advance(0.15f, fps, dt =>
                    AdvancedVisemeMath.StepSimplexWithSpeechHistory(
                        10, 0f, dt, ResponseSeconds, ConfiguredReleaseSeconds,
                        DefaultStability, ref state, fast, slow));

                var frozenFast = (float[])fast.Clone();
                var previousDistance = Mathf.Abs(fast[10] - slow[10]);
                AdvancedVisemeMath.StepSimplexWithSpeechHistory(
                    0, 0f, 1f / fps, ResponseSeconds, ConfiguredReleaseSeconds,
                    DefaultStability, ref state, fast, slow);

                Assert.That(state.HoldWeight,
                    Is.EqualTo(1f).Within(1e-6f), $"{fps} FPS full hold");
                AssertVectorsEqual(frozenFast, fast, 0f, $"frozen fast at {fps} FPS");
                Assert.That(Mathf.Abs(fast[10] - slow[10]),
                    Is.LessThan(previousDistance));
                AssertSimplex(fast);
                AssertSimplex(slow);
            }
        }

        [Test]
        public void NewHardPhoneBypassesAnActiveSilenceHoldImmediately()
        {
            var state = default(AdvancedVisemeMath.SpeechHistoryState);
            var fast = NewSilenceSimplex();
            var slow = NewSilenceSimplex();
            Advance(0.5f, 100, dt => AdvancedVisemeMath.StepSimplexWithSpeechHistory(
                10, 0f, dt, ResponseSeconds, ConfiguredReleaseSeconds,
                DefaultStability, ref state, fast, slow));
            AdvancedVisemeMath.StepSimplexWithSpeechHistory(
                0, 0f, 0.04f, ResponseSeconds, ConfiguredReleaseSeconds,
                DefaultStability, ref state, fast, slow);
            Assert.That(state.HoldWeight, Is.EqualTo(1f).Within(1e-6f));

            var expectedFast = (float[])fast.Clone();
            var expectedSlow = (float[])slow.Clone();
            AdvancedVisemeMath.StepSimplex(
                13, 0.01f, ResponseSeconds, expectedFast, expectedSlow);
            AdvancedVisemeMath.StepSimplexWithSpeechHistory(
                13, 1f, 0.01f, ResponseSeconds, ConfiguredReleaseSeconds,
                DefaultStability, ref state, fast, slow);

            Assert.That(state.HoldWeight, Is.Zero);
            AssertVectorsEqual(expectedFast, fast, 0f, "immediate phone fast");
            AssertVectorsEqual(expectedSlow, slow, 0f, "immediate phone slow");
        }

        [Test]
        public void VoiceAloneCanNeitherChargeNorPinSpeechHistory()
        {
            var state = default(AdvancedVisemeMath.SpeechHistoryState);
            var noisyVoice = new[] { 0f, 0.12f, 0.21f, 0.8f, 1f, float.NaN };
            for (var frame = 0; frame < 360; frame++)
            {
                AdvancedVisemeMath.StepSpeechHistory(
                    0, noisyVoice[frame % noisyVoice.Length], 1f / 90f,
                    ConfiguredReleaseSeconds, DefaultStability, ref state);
                Assert.That(state.History, Is.Zero);
                Assert.That(state.HoldWeight, Is.Zero);
                Assert.That(state.Presence, Is.Zero);
            }

            var fast = NewSilenceSimplex();
            var slow = NewSilenceSimplex();
            Advance(0.6f, 90, dt => AdvancedVisemeMath.StepSimplexWithSpeechHistory(
                10, 1f, dt, ResponseSeconds, ConfiguredReleaseSeconds,
                DefaultStability, ref state, fast, slow));
            Advance(1.5f, 90, dt => AdvancedVisemeMath.StepSimplexWithSpeechHistory(
                0, 1f, dt, ResponseSeconds, ConfiguredReleaseSeconds,
                DefaultStability, ref state, fast, slow));

            Assert.That(state.History, Is.LessThan(1e-4f));
            Assert.That(state.HoldWeight, Is.Zero);
            Assert.That(state.Presence, Is.LessThan(1e-4f));
            Assert.That(fast[0], Is.GreaterThan(0.999f));
            Assert.That(slow[0], Is.GreaterThan(0.999f));
        }

        [Test]
        public void ZeroStabilityIsExactlyTheOriginalObserverForEveryViseme()
        {
            foreach (var fps in new[] { 15, 30, 60, 90, 144 })
            {
                var state = default(AdvancedVisemeMath.SpeechHistoryState);
                var expectedFast = NewSilenceSimplex();
                var expectedSlow = NewSilenceSimplex();
                var actualFast = NewSilenceSimplex();
                var actualSlow = NewSilenceSimplex();
                var random = new System.Random(1701 + fps);
                for (var frame = 0; frame < fps * 3; frame++)
                {
                    var phone = random.Next(VisemeReconstructionProfile.VisemeCount);
                    var voice = (float)random.NextDouble();
                    AdvancedVisemeMath.StepSimplex(
                        phone, 1f / fps, ResponseSeconds, expectedFast, expectedSlow);
                    AdvancedVisemeMath.StepSimplexWithSpeechHistory(
                        phone, voice, 1f / fps, ResponseSeconds,
                        ConfiguredReleaseSeconds, 0f,
                        ref state, actualFast, actualSlow);

                    Assert.That(state.HoldWeight, Is.Zero);
                    AssertVectorsEqual(expectedFast, actualFast, 0f, $"fast frame {frame}");
                    AssertVectorsEqual(expectedSlow, actualSlow, 0f, $"slow frame {frame}");
                }
            }
        }

        [Test]
        public void ContinuousNonSilenceIsExactlyTheOriginalObserver()
        {
            var state = default(AdvancedVisemeMath.SpeechHistoryState);
            var expectedFast = NewSilenceSimplex();
            var expectedSlow = NewSilenceSimplex();
            var actualFast = NewSilenceSimplex();
            var actualSlow = NewSilenceSimplex();
            var random = new System.Random(7519);
            for (var frame = 0; frame < 500; frame++)
            {
                var phone = random.Next(1, VisemeReconstructionProfile.VisemeCount);
                var dt = Mathf.Lerp(1f / 144f, 1f / 15f, (float)random.NextDouble());
                AdvancedVisemeMath.StepSimplex(
                    phone, dt, ResponseSeconds, expectedFast, expectedSlow);
                AdvancedVisemeMath.StepSimplexWithSpeechHistory(
                    phone, float.PositiveInfinity, dt, ResponseSeconds,
                    ConfiguredReleaseSeconds, DefaultStability,
                    ref state, actualFast, actualSlow);

                Assert.That(state.HoldWeight, Is.Zero);
                AssertVectorsEqual(expectedFast, actualFast, 0f, $"fast frame {frame}");
                AssertVectorsEqual(expectedSlow, actualSlow, 0f, $"slow frame {frame}");
            }
        }

        [Test]
        public void RandomStreamsRemainFiniteAndOnTheSimplexAtAllFrameRates()
        {
            foreach (var fps in new[] { 15, 20, 30, 45, 60, 90, 144 })
            {
                var state = default(AdvancedVisemeMath.SpeechHistoryState);
                var fast = NewSilenceSimplex();
                var slow = NewSilenceSimplex();
                var random = new System.Random(983 + fps);
                for (var frame = 0; frame < fps * 8; frame++)
                {
                    var phone = random.NextDouble() < 0.42
                        ? 0
                        : random.Next(1, VisemeReconstructionProfile.VisemeCount);
                    var voice = frame % 41 == 0
                        ? float.NaN
                        : (float)random.NextDouble();
                    AdvancedVisemeMath.StepSimplexWithSpeechHistory(
                        phone, voice, 1f / fps, ResponseSeconds,
                        ConfiguredReleaseSeconds, DefaultStability,
                        ref state, fast, slow);

                    Assert.That(state.History, Is.InRange(0f, 1f));
                    Assert.That(state.HoldWeight, Is.InRange(0f, 1f));
                    Assert.That(state.Presence, Is.InRange(0f, 1f));
                    AssertSimplex(fast);
                    AssertSimplex(slow);
                }
            }
        }

        [Test]
        public void PresenceKeepsFastAttackAndReleaseWhenHoldIsDisabled()
        {
            var state = default(AdvancedVisemeMath.SpeechHistoryState);
            AdvancedVisemeMath.StepSpeechHistory(
                10, 0f, AdvancedVisemeMath.SpeechPresenceAttackSeconds,
                ConfiguredReleaseSeconds, 0f, ref state);
            var afterAttack = 1f - Mathf.Exp(-1f);
            Assert.That(state.Presence, Is.EqualTo(afterAttack).Within(1e-6f));

            AdvancedVisemeMath.StepSpeechHistory(
                0, 1f, AdvancedVisemeMath.SpeechPresenceReleaseSeconds,
                ConfiguredReleaseSeconds, 0f, ref state);
            Assert.That(state.Presence,
                Is.EqualTo(afterAttack * Mathf.Exp(-1f)).Within(1e-6f));
        }

        [Test]
        public void PartialHoldPresenceMatchesTheNestedAnimatorMotions()
        {
            var state = new AdvancedVisemeMath.SpeechHistoryState
            {
                History = Mathf.Lerp(
                    AdvancedVisemeMath.SpeechHistoryHoldStart,
                    AdvancedVisemeMath.SpeechHistoryHoldFull, 0.5f),
                Presence = 0.3f
            };
            const float deltaTime = 0.01f;
            var hold = AdvancedVisemeMath.SpeechHistoryHoldWeight(
                state.History, 0, DefaultStability);
            var attack = AdvancedVisemeMath.Alpha(
                deltaTime, AdvancedVisemeMath.SpeechPresenceAttackSeconds);
            var release = AdvancedVisemeMath.Alpha(
                deltaTime, AdvancedVisemeMath.SpeechPresenceReleaseSeconds);
            var expected = Mathf.Lerp(
                state.Presence * (1f - release),
                state.Presence + attack * (1f - state.Presence),
                hold);

            AdvancedVisemeMath.StepSpeechHistory(
                0, 1f, deltaTime, ConfiguredReleaseSeconds,
                DefaultStability, ref state);

            Assert.That(hold, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(state.Presence, Is.EqualTo(expected).Within(1e-6f));
        }

        [Test]
        public void ProtectedSilenceRetainsExistingGainWithoutBoostingIt()
        {
            const float previousGain = 0.63f;
            const float livePresence = 0.92f;
            const float liveAmplitude = 0.55f;
            var normalGain = livePresence * liveAmplitude;

            Assert.That(AdvancedVisemeMath.HeldSpeechGain(
                    previousGain, livePresence, liveAmplitude,
                    1f, 0, DefaultStability),
                Is.EqualTo(previousGain).Within(1e-7f),
                "A full transient-sil hold must retain, not amplify, the existing pose.");
            Assert.That(AdvancedVisemeMath.HeldSpeechGain(
                    previousGain, livePresence, liveAmplitude,
                    Mathf.Lerp(AdvancedVisemeMath.SpeechHistoryHoldStart,
                        AdvancedVisemeMath.SpeechHistoryHoldFull, 0.5f),
                    0, DefaultStability),
                Is.EqualTo(Mathf.Lerp(normalGain, previousGain, 0.5f)).Within(1e-7f));
            Assert.That(AdvancedVisemeMath.HeldSpeechGain(
                    previousGain, livePresence, liveAmplitude,
                    1f, 0, 0f),
                Is.EqualTo(normalGain).Within(1e-7f));
            Assert.That(AdvancedVisemeMath.HeldSpeechGain(
                    previousGain, livePresence, liveAmplitude,
                    1f, 10, DefaultStability),
                Is.EqualTo(normalGain).Within(1e-7f),
                "A real non-sil phone must always use the live gain.");
        }

        private static AdvancedVisemeMath.SpeechHistoryState ChargeHistory(
            float seconds,
            float deltaTime)
        {
            var state = default(AdvancedVisemeMath.SpeechHistoryState);
            var elapsed = 0f;
            while (elapsed < seconds - 1e-7f)
            {
                var step = Mathf.Min(deltaTime, seconds - elapsed);
                AdvancedVisemeMath.StepSpeechHistory(
                    10, 0f, step, ConfiguredReleaseSeconds,
                    DefaultStability, ref state);
                elapsed += step;
            }
            return state;
        }

        private static float MeasureSilenceHold(
            ref AdvancedVisemeMath.SpeechHistoryState state)
        {
            return AdvancedVisemeMath.StepSpeechHistory(
                0, 0f, 0f, ConfiguredReleaseSeconds,
                DefaultStability, ref state);
        }

        private static void Advance(float seconds, int fps, Action<float> step)
        {
            var elapsed = 0f;
            var nominalDelta = 1f / fps;
            while (elapsed < seconds - 1e-7f)
            {
                var delta = Mathf.Min(nominalDelta, seconds - elapsed);
                step(delta);
                elapsed += delta;
            }
        }

        private static float[] NewSilenceSimplex()
        {
            var values = new float[VisemeReconstructionProfile.VisemeCount];
            values[0] = 1f;
            return values;
        }

        private static void AssertSimplex(float[] values)
        {
            var sum = 0f;
            foreach (var value in values)
            {
                Assert.That(float.IsNaN(value) || float.IsInfinity(value), Is.False);
                Assert.That(value, Is.InRange(0f, 1f));
                sum += value;
            }
            Assert.That(sum, Is.EqualTo(1f).Within(1e-5f));
        }

        private static void AssertVectorsEqual(
            float[] expected,
            float[] actual,
            float tolerance,
            string context)
        {
            Assert.That(actual, Has.Length.EqualTo(expected.Length));
            for (var i = 0; i < expected.Length; i++)
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(tolerance),
                    $"{context}, channel {i}");
        }
    }
}
#endif
