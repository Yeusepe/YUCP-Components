#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor.Tests
{
    public sealed class AdvancedVisemeReconstructorTests
    {
        public static string GeneratedBetaGraphProfileCopyPath;

        [Test]
        public void ObserverRemainsASimplexAcrossFrameRatesAndInterruptions()
        {
            foreach (var fps in new[] { 15, 20, 30, 45, 60, 90, 144 })
            {
                var fast = NewSilenceSimplex();
                var slow = NewSilenceSimplex();
                var random = new System.Random(1701 + fps);
                for (var frame = 0; frame < fps * 8; frame++)
                {
                    var observed = frame % 3 == 0 ? random.Next(0, 15) : random.Next(1, 15);
                    AdvancedVisemeMath.StepSimplex(observed, 1f / fps, 0.024f, fast, slow);
                    AssertSimplex(fast);
                    AssertSimplex(slow);
                }
            }
        }

        [Test]
        public void MapBatchActiveBindingGuardRejectsConstantLaneExpansion()
        {
            Assert.That(
                AdvancedVisemeAnimatorBuilder.MapBatchPreservesActiveBindingBound(
                    new[] { 7, 7 }, 12, 2),
                Is.True,
                "Two ordinary maps retain the same four-active-binding bound.");
            Assert.That(
                AdvancedVisemeAnimatorBuilder.MapBatchPreservesActiveBindingBound(
                    new[] { 1, 7 }, 7, 2),
                Is.False,
                "A one-point constant map must not expand from one to two active bindings.");
            Assert.That(
                AdvancedVisemeAnimatorBuilder.MapBatchPreservesActiveBindingBound(
                    new[] { 1, 1 }, 1, 2),
                Is.True,
                "Constant maps sharing one knot retain one active binding per output.");
        }

        [Test]
        public void SimplexEmissionSparsifierIsContinuousBoundedAndEndpointExact()
        {
            var epsilon = AdvancedVisemeMath.SimplexCullingEpsilon;
            Assert.That(AdvancedVisemeMath.SparsifySimplexCoordinate(0f), Is.Zero);
            Assert.That(AdvancedVisemeMath.SparsifySimplexCoordinate(epsilon), Is.Zero);
            Assert.That(AdvancedVisemeMath.SparsifySimplexCoordinate(1f), Is.EqualTo(1f));

            var previous = 0f;
            for (var sample = 0; sample <= 100000; sample++)
            {
                var value = sample / 100000f;
                var sparse = AdvancedVisemeMath.SparsifySimplexCoordinate(value);
                Assert.That(sparse, Is.GreaterThanOrEqualTo(previous));
                Assert.That(sparse, Is.InRange(0f, 1f));
                Assert.That(Mathf.Abs(sparse - value),
                    Is.LessThanOrEqualTo(epsilon / (1f - epsilon) + 1e-7f));
                previous = sparse;
            }
        }

        [Test]
        public void SimplexEmissionSparsifierHasACertifiedRandomVectorError()
        {
            var random = new System.Random(7319);
            var epsilon = AdvancedVisemeMath.SimplexCullingEpsilon;
            var coordinateBound = epsilon / (1f - epsilon) + 1e-7f;
            for (var trial = 0; trial < 10000; trial++)
            {
                var values = Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                    .Select(_ => (float)random.NextDouble()).ToArray();
                var sum = values.Sum();
                for (var i = 0; i < values.Length; i++) values[i] /= sum;

                var sparse = values.Select(value =>
                    AdvancedVisemeMath.SparsifySimplexCoordinate(value)).ToArray();
                Assert.That(sparse.Sum(), Is.InRange(0f, 1.000001f));
                for (var i = 0; i < values.Length; i++)
                    Assert.That(Mathf.Abs(sparse[i] - values[i]),
                        Is.LessThanOrEqualTo(coordinateBound));
            }
        }

        [Test]
        public void ObserverConvergenceIsFrameRateIndependentAndHasNoOvershoot()
        {
            float ValueAfterHalfSecond(int fps)
            {
                var fast = NewSilenceSimplex();
                var slow = NewSilenceSimplex();
                for (var i = 0; i < Mathf.RoundToInt(fps * 0.5f); i++)
                {
                    AdvancedVisemeMath.StepSimplex(10, 1f / fps, 0.024f, fast, slow);
                    Assert.That(slow[10], Is.InRange(0f, 1f));
                }
                return slow[10];
            }

            var low = ValueAfterHalfSecond(15);
            var high = ValueAfterHalfSecond(144);
            Assert.That(Mathf.Abs(low - high), Is.LessThan(0.001f));
            Assert.That(low, Is.GreaterThan(0.999f));
        }

        [Test]
        public void ObserverRecoversToSilenceAfterRapidInterruptions()
        {
            var fast = NewSilenceSimplex();
            var slow = NewSilenceSimplex();
            for (var frame = 0; frame < 180; frame++)
                AdvancedVisemeMath.StepSimplex(1 + frame % 14, 1f / 90f, 0.024f, fast, slow);
            for (var frame = 0; frame < 90; frame++)
                AdvancedVisemeMath.StepSimplex(0, 1f / 90f, 0.024f, fast, slow);

            AssertSimplex(fast);
            AssertSimplex(slow);
            Assert.That(slow[0], Is.GreaterThan(0.999f));
            for (var i = 1; i < slow.Length; i++) Assert.That(slow[i], Is.LessThan(0.001f));
        }

        [Test]
        public void ObserverStageDifferenceHasExpectedOnsetAndReleaseSigns()
        {
            var fast = NewSilenceSimplex();
            var slow = NewSilenceSimplex();
            AdvancedVisemeMath.StepSimplex(10, 1f / 60f, 0.024f, fast, slow);
            Assert.That(fast[10] - slow[10], Is.GreaterThan(0f));

            for (var frame = 0; frame < 60; frame++)
                AdvancedVisemeMath.StepSimplex(10, 1f / 60f, 0.024f, fast, slow);
            AdvancedVisemeMath.StepSimplex(0, 1f / 60f, 0.024f, fast, slow);
            Assert.That(fast[10] - slow[10], Is.LessThan(0f));
        }

        [Test]
        public void FusionEndpointsAndPhoneticConstraintsAreExact()
        {
            Assert.That(AdvancedVisemeMath.Fuse(0.2f, 0.9f, 0f), Is.EqualTo(0.2f).Within(1e-6f));
            Assert.That(AdvancedVisemeMath.Fuse(0.2f, 0.9f, 1f), Is.EqualTo(0.9f).Within(1e-6f));

            var jaw = 0.9f;
            var close = 0.1f;
            var bite = 0.05f;
            AdvancedVisemeMath.ApplyPhoneticConstraints(1f, 1f, 0.7f, 0.3f, 0.9f, 0.85f, 0.22f,
                ref jaw, ref close, ref bite);
            Assert.That(close, Is.GreaterThanOrEqualTo(0.9f));
            Assert.That(bite, Is.GreaterThanOrEqualTo(0.85f));
            Assert.That(jaw, Is.LessThanOrEqualTo(0.22f + 1e-6f));
        }

        [Test]
        public void TrackingConfidenceFadeIsContinuousBoundedAndMonotonic()
        {
            var gain = 0f;
            var previous = AdvancedVisemeMath.Fuse(0.2f, 0.9f, gain);
            var alpha = AdvancedVisemeMath.Alpha(1f / 90f, 0.12f);
            for (var frame = 0; frame < 120; frame++)
            {
                gain += alpha * (1f - gain);
                var fused = AdvancedVisemeMath.Fuse(0.2f, 0.9f, gain);
                Assert.That(fused, Is.InRange(previous, 0.9f));
                previous = fused;
            }

            for (var frame = 0; frame < 120; frame++)
            {
                gain += alpha * (0f - gain);
                var fused = AdvancedVisemeMath.Fuse(0.2f, 0.9f, gain);
                Assert.That(fused, Is.InRange(0.2f, previous));
                previous = fused;
            }
        }

        [Test]
        public void MeasuredVowelChannelsCanReachTheExactTrackingEndpoint()
        {
            const float authoredVowel = 0.92f;
            const float measuredAperture = 0.16f;
            var authority = AdvancedVisemeMath.TrackingAuthority(
                authoredVowel, measuredAperture, true, 0.82f, 0.65f);

            Assert.That(authority, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(AdvancedVisemeMath.Fuse(authoredVowel, measuredAperture, authority),
                Is.EqualTo(measuredAperture).Within(1e-6f),
                "A tracked quiet /aa/ must cap visible aperture instead of retaining an additive authored vowel.");
        }

        [Test]
        public void TrackerAuthoritativeDoesNotAttenuateTheTrackingEndpointForVowels()
        {
            Assert.That(AdvancedVisemeAnimatorBuilder.UsesPhoneticTrackingScale(
                AdvancedVisemeFusionMode.PhoneticAssist), Is.False);
            Assert.That(AdvancedVisemeAnimatorBuilder.UsesPhoneticTrackingScale(
                AdvancedVisemeFusionMode.TrackerAuthoritative), Is.False);

            foreach (var mode in new[]
                     {
                         AdvancedVisemeFusionMode.PhoneticAssist,
                         AdvancedVisemeFusionMode.TrackerAuthoritative
                     })
            {
                var trackingGain = AdvancedVisemeAnimatorBuilder.UsesPhoneticTrackingScale(mode)
                    ? AdvancedVisemeMath.ComplementaryTrackingGain(1f, 1f, 0.75f)
                    : 1f;
                Assert.That(AdvancedVisemeMath.Fuse(0.9f, 0.2f, trackingGain),
                    Is.EqualTo(0.2f).Within(1e-6f), mode.ToString());
            }
        }

        [Test]
        public void VowelIdentityRetentionNeverSuppressesTrackedApertureOrExpression()
        {
            Assert.That(AdvancedVisemeAnimatorBuilder.UsesVowelIdentityRetention(
                AdvancedVisemeArticulator.JawOpen), Is.False);
            Assert.That(AdvancedVisemeAnimatorBuilder.UsesVowelIdentityRetention(
                AdvancedVisemeArticulator.MouthOpen), Is.False);
            Assert.That(AdvancedVisemeAnimatorBuilder.UsesVowelIdentityRetention(
                AdvancedVisemeArticulator.SmileSad), Is.False);
            Assert.That(AdvancedVisemeAnimatorBuilder.UsesVowelIdentityRetention(
                AdvancedVisemeArticulator.LipFunnel), Is.False);
            Assert.That(AdvancedVisemeAnimatorBuilder.UsesVowelIdentityRetention(
                AdvancedVisemeArticulator.LipPucker), Is.False);
        }

        [Test]
        public void AdaptiveTrackingPoseRejectsSmallJitterAndUsesFastDeliberateMotion()
        {
            const float slow = 0.4f;

            Assert.That(AdvancedVisemeMath.AdaptiveTrackingPose(0.401f, slow, true),
                Is.EqualTo(slow).Within(1e-6f),
                "Sub-deadband local motion should remain on the stable two-pole estimate.");
            Assert.That(AdvancedVisemeMath.AdaptiveTrackingPose(0.405f, slow, false),
                Is.EqualTo(slow).Within(1e-6f),
                "Remote quantization inside the larger deadband should remain stable.");

            var intermediate = AdvancedVisemeMath.AdaptiveTrackingPose(0.42f, slow, true);
            Assert.That(intermediate, Is.InRange(slow, 0.42f));

            Assert.That(AdvancedVisemeMath.AdaptiveTrackingPose(0.45f, slow, true),
                Is.EqualTo(0.45f).Within(1e-6f),
                "Large deliberate motion should select the low-lag one-pole estimate.");
            Assert.That(AdvancedVisemeMath.AdaptiveTrackingPose(0.32f, slow, true),
                Is.InRange(0.32f, slow),
                "The adaptive pose must remain bounded when motion is negative.");
        }

        [Test]
        public void AdaptiveTrackingObserverSuppressesQuantizedSequencesAcrossFrameRates()
        {
            var stepEndpoints = new System.Collections.Generic.List<float>();
            foreach (var fps in new[] { 15, 30, 60, 90, 144 })
            {
                var deltaTime = 1f / fps;
                var alpha = AdvancedVisemeMath.Alpha(deltaTime, 0.018f);
                var motionAlpha = AdvancedVisemeMath.Alpha(deltaTime, 0.012f);
                var fast = 0.42f;
                var slow = 0.42f;
                var motion = 0f;
                var rawSquared = 0f;
                var filteredSquared = 0f;
                var samples = 0;

                for (var frame = 0; frame < fps * 4; frame++)
                {
                    var input = 0.42f + (frame % 2 == 0 ? 0.004f : -0.004f);
                    fast += alpha * (input - fast);
                    slow += alpha * (fast - slow);
                    var selector = AdvancedVisemeMath.SmoothStep(
                        AdvancedVisemeAnimatorBuilder.LocalTrackingMotionDeadband,
                        AdvancedVisemeAnimatorBuilder.LocalTrackingMotionFullScale,
                        Mathf.Abs(fast - slow));
                    motion += motionAlpha * (selector - motion);
                    var pose = Mathf.Lerp(slow, fast, motion);
                    if (frame < fps * 2) continue;
                    rawSquared += Mathf.Pow(input - 0.42f, 2f);
                    filteredSquared += Mathf.Pow(pose - 0.42f, 2f);
                    samples++;
                }

                var rawRms = Mathf.Sqrt(rawSquared / samples);
                var filteredRms = Mathf.Sqrt(filteredSquared / samples);
                Assert.That(filteredRms, Is.LessThan(rawRms * 0.95f),
                    $"The observer did not suppress alternating local quantization at {fps} FPS.");

                fast = slow = 0f;
                motion = 0f;
                var endpoint = 0f;
                var stepFrames = Mathf.CeilToInt(0.12f * fps);
                for (var frame = 0; frame < stepFrames; frame++)
                {
                    fast += alpha * (0.8f - fast);
                    slow += alpha * (fast - slow);
                    var selector = AdvancedVisemeMath.SmoothStep(
                        AdvancedVisemeAnimatorBuilder.LocalTrackingMotionDeadband,
                        AdvancedVisemeAnimatorBuilder.LocalTrackingMotionFullScale,
                        Mathf.Abs(fast - slow));
                    motion += motionAlpha * (selector - motion);
                    endpoint = Mathf.Lerp(slow, fast, motion);
                    Assert.That(endpoint, Is.InRange(0f, 0.8f + 1e-6f),
                        $"The tracking step overshot at {fps} FPS.");
                }
                Assert.That(endpoint, Is.GreaterThan(0.72f),
                    $"The denoiser added excessive deliberate-motion lag at {fps} FPS.");
                stepEndpoints.Add(endpoint);
            }

            Assert.That(stepEndpoints.Max() - stepEndpoints.Min(), Is.LessThan(0.08f),
                "Adaptive tracking latency changed excessively with frame rate.");
        }

        [Test]
        public void TrackingAuthorityIsBoundedMonotonicAndMakesStrongDisagreementExact()
        {
            const float localReliability = 0.72f;
            const float remoteReliability = 0.5f;

            var previous = 0f;
            for (var step = 0; step <= 20; step++)
            {
                var mismatch = step * 0.01f;
                var authority = AdvancedVisemeMath.TrackingAuthority(
                    0.4f, 0.4f + mismatch, true, localReliability, remoteReliability);
                Assert.That(authority, Is.InRange(localReliability, 1f));
                Assert.That(authority, Is.GreaterThanOrEqualTo(previous - 1e-6f));
                previous = authority;
            }

            const float nnSpeechAperture = 0.38f;
            const float closedTrackedMouth = 0f;
            var nnAuthority = AdvancedVisemeMath.TrackingAuthority(
                nnSpeechAperture, closedTrackedMouth, true, localReliability, remoteReliability);
            Assert.That(nnAuthority, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(AdvancedVisemeMath.Fuse(nnSpeechAperture, closedTrackedMouth, nnAuthority),
                Is.EqualTo(closedTrackedMouth).Within(1e-6f),
                "Visible lips and jaw for /n/ should follow the closed measured mouth.");

            const float aaSpeechAperture = 0.95f;
            const float restrainedTrackedAperture = 0.12f;
            var aaAuthority = AdvancedVisemeMath.TrackingAuthority(
                aaSpeechAperture, restrainedTrackedAperture, true, localReliability, remoteReliability);
            Assert.That(aaAuthority, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(AdvancedVisemeMath.Fuse(aaSpeechAperture, restrainedTrackedAperture, aaAuthority),
                Is.EqualTo(restrainedTrackedAperture).Within(1e-6f),
                "A strongly disagreeing tracked /aa/ aperture should be the exact visible endpoint.");
        }

        [Test]
        public void SpeechActivityTargetUsesVoiceToBridgeTransientSilence()
        {
            Assert.That(AdvancedVisemeMath.SpeechActivityTarget(0f, 0f),
                Is.EqualTo(1f).Within(1e-6f));
            Assert.That(AdvancedVisemeMath.SpeechActivityTarget(1f, 0f), Is.Zero,
                "Settled silence with no Voice evidence must release speech completely.");
            Assert.That(AdvancedVisemeMath.SpeechActivityTarget(1f, 0.35f),
                Is.EqualTo(0.35f).Within(1e-6f),
                "Voice should bridge a transient one-frame sil index without restoring full speech.");
            Assert.That(AdvancedVisemeMath.SpeechActivityTarget(0.8f, 0.35f),
                Is.EqualTo(0.55f).Within(1e-6f));
            Assert.That(AdvancedVisemeMath.SpeechActivityTarget(0.1f, 0.8f),
                Is.EqualTo(1f).Within(1e-6f), "Activity must remain normalized.");
        }

        [Test]
        public void SmoothConstraintProjectionsAreContinuousBoundedAndExactOutsideTheirWidths()
        {
            const float floor = 0.6f;
            const float ceiling = 0.4f;
            const float width = 0.05f;

            Assert.That(AdvancedVisemeMath.SmoothFloorProjection(0.7f, floor, 1f, width),
                Is.EqualTo(0.7f).Within(1e-6f));
            Assert.That(AdvancedVisemeMath.SmoothFloorProjection(0.5f, floor, 1f, width),
                Is.EqualTo(floor).Within(1e-6f));
            var floorTransition = AdvancedVisemeMath.SmoothFloorProjection(0.575f, floor, 1f, width);
            Assert.That(floorTransition, Is.InRange(floor, floor + width * 0.25f));

            Assert.That(AdvancedVisemeMath.SmoothCeilingProjection(0.3f, ceiling, 1f, width),
                Is.EqualTo(0.3f).Within(1e-6f));
            Assert.That(AdvancedVisemeMath.SmoothCeilingProjection(0.5f, ceiling, 1f, width),
                Is.EqualTo(ceiling).Within(1e-6f));
            var ceilingTransition = AdvancedVisemeMath.SmoothCeilingProjection(0.425f, ceiling, 1f, width);
            Assert.That(ceilingTransition, Is.InRange(ceiling - width * 0.25f, ceiling));

            const float epsilon = 1e-4f;
            var floorAtBoundary = AdvancedVisemeMath.SmoothFloorProjection(floor, floor, 1f, width);
            var floorNearBoundary = AdvancedVisemeMath.SmoothFloorProjection(
                floor - epsilon, floor, 1f, width);
            Assert.That(Mathf.Abs(floorNearBoundary - floorAtBoundary), Is.LessThan(epsilon * 1.1f));

            var ceilingAtBoundary = AdvancedVisemeMath.SmoothCeilingProjection(ceiling, ceiling, 1f, width);
            var ceilingNearBoundary = AdvancedVisemeMath.SmoothCeilingProjection(
                ceiling + epsilon, ceiling, 1f, width);
            Assert.That(Mathf.Abs(ceilingNearBoundary - ceilingAtBoundary), Is.LessThan(epsilon * 1.1f));

            foreach (var confidence in new[] { 0f, 0.25f, 0.5f, 1f })
            {
                var previousFloor = -1f;
                var previousCeiling = -1f;
                for (var step = 0; step <= 1000; step++)
                {
                    var value = step / 1000f;
                    var projectedFloor = AdvancedVisemeMath.SmoothFloorProjection(
                        value, floor, confidence, width);
                    var projectedCeiling = AdvancedVisemeMath.SmoothCeilingProjection(
                        value, ceiling, confidence, width);

                    Assert.That(projectedFloor,
                        Is.InRange(value, Mathf.Clamp01(
                            Mathf.Max(value, floor) + confidence * width * 0.25f)),
                        $"Floor projection escaped its bounds at x={value}, confidence={confidence}.");
                    Assert.That(projectedCeiling,
                        Is.InRange(Mathf.Clamp01(
                            Mathf.Min(value, ceiling) - confidence * width * 0.25f), value),
                        $"Ceiling projection escaped its bounds at x={value}, confidence={confidence}.");
                    Assert.That(projectedFloor,
                        Is.GreaterThanOrEqualTo(previousFloor - 1e-6f),
                        $"Floor projection was not monotonic at x={value}, confidence={confidence}.");
                    Assert.That(projectedCeiling,
                        Is.GreaterThanOrEqualTo(previousCeiling - 1e-6f),
                        $"Ceiling projection was not monotonic at x={value}, confidence={confidence}.");

                    if (Mathf.Approximately(confidence, 0f))
                    {
                        Assert.That(projectedFloor, Is.EqualTo(value).Within(1e-6f));
                        Assert.That(projectedCeiling, Is.EqualTo(value).Within(1e-6f));
                    }

                    previousFloor = projectedFloor;
                    previousCeiling = projectedCeiling;
                }
            }
        }

        [Test]
        public void QuietSpeechFloorReleasesCompletelyOnVrchatSilence()
        {
            Assert.That(AdvancedVisemeMath.SpeechGain(1f, 1f, 0.55f), Is.Zero,
                "A stale Voice value must not keep an old viseme over live tracking after Viseme returns to sil.");
            Assert.That(AdvancedVisemeMath.SpeechGain(0f, 0f, 0.55f),
                Is.EqualTo(0.55f).Within(1e-6f),
                "The floor still protects genuinely quiet non-silence speech.");
            Assert.That(AdvancedVisemeMath.SpeechGain(0f, 1f, 0.55f),
                Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void NativeTongueCapabilityLatchesAfterEvidenceAndPreservesNeutralEndpoints()
        {
            var capability = AdvancedVisemeAnimatorBuilder.StepNativeTongueCapability(0f, 0f, 0f);
            Assert.That(capability, Is.Zero);

            capability = AdvancedVisemeAnimatorBuilder.StepNativeTongueCapability(
                capability, AdvancedVisemeAnimatorBuilder.NativeTongueCapabilityThreshold, 0f);
            Assert.That(capability, Is.EqualTo(1f).Within(1e-6f));

            capability = AdvancedVisemeAnimatorBuilder.StepNativeTongueCapability(
                capability, 0f, 0f);
            Assert.That(capability, Is.EqualTo(1f).Within(1e-6f),
                "A supported tracker must remain authoritative when it returns to neutral.");

            capability = AdvancedVisemeAnimatorBuilder.StepNativeTongueCapability(
                capability, AdvancedVisemeAnimatorBuilder.NativeTongueCapabilityNoiseFloor * 0.5f, 0f);
            Assert.That(capability, Is.EqualTo(1f).Within(1e-6f),
                "Small native motion must not be multiplied by its own instantaneous amplitude.");
        }

        [Test]
        public void NativeTongueAxesParticipateInOwnershipOnlyWhenMeasured()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                Assert.That(
                    AdvancedVisemeReconstructorProcessor.IsObservableCalibrationArticulator(
                        AdvancedVisemeArticulator.TongueOut,
                        true,
                        false,
                        AdvancedVisemeTrackingInputs.Balanced8,
                        null,
                        profile),
                    Is.True,
                    "A real TongueOut input may own its calibrated surface; runtime capability " +
                    "gating keeps its gain at zero when hardware does not populate it.");
                Assert.That(
                    AdvancedVisemeReconstructorProcessor.IsObservableCalibrationArticulator(
                        AdvancedVisemeArticulator.TongueX,
                        true,
                        false,
                        AdvancedVisemeTrackingInputs.Balanced8,
                        null,
                        profile),
                    Is.False,
                    "A synthesized tongue axis must never erase authored tongue detail.");
                Assert.That(
                    AdvancedVisemeReconstructorProcessor.IsObservableCalibrationArticulator(
                        AdvancedVisemeArticulator.TongueX,
                        true,
                        false,
                        AdvancedVisemeTrackingInputs.FullTongue18,
                        null,
                        profile),
                    Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void LowRankResidualOwnershipHasExactIndependentEndpoints()
        {
            var weights = new[] { 0.25f, 0.75f };
            var jawProjection = new[] { 0.4f, -0.2f };
            var lipProjection = new[] { -0.1f, 0.6f };

            Assert.That(AdvancedVisemeMath.LowRankOwnershipCorrection(
                weights, jawProjection, 0f, 1f, 1f), Is.Zero,
                "Tracking-off must preserve the exact authored residual.");
            Assert.That(AdvancedVisemeMath.LowRankOwnershipCorrection(
                    weights, jawProjection, 1f, 1f, 1f),
                Is.EqualTo(0.05f).Within(1e-6f));
            Assert.That(AdvancedVisemeMath.LowRankOwnershipCorrection(
                    weights, lipProjection, 0.25f, 1f, 1f),
                Is.EqualTo(-0.10625f).Within(1e-6f),
                "Each measured coordinate must use its own authority.");
            Assert.That(AdvancedVisemeMath.LowRankOwnershipCorrection(
                    weights, lipProjection, 1f, 0f, 1f),
                Is.Zero,
                "Authored-detail zero removes residual and its correction together.");
        }

        [Test]
        public void MouthEnvelopePreventsCompetingPosesAndHiddenTongueProtrusion()
        {
            var jaw = 0f;
            var close = 0.8f;
            var open = 0.9f;
            var pucker = 0.9f;
            var suck = 0.7f;
            var tongue = 1f;
            AdvancedVisemeMath.ProjectMouthEnvelope(
                0f, ref jaw, ref close, ref open, ref pucker, ref suck, ref tongue);

            Assert.That(close + open, Is.LessThanOrEqualTo(1f + 1e-6f));
            Assert.That(pucker + suck, Is.LessThanOrEqualTo(1f + 1e-6f));
            Assert.That(tongue, Is.LessThanOrEqualTo(0.08f + 0.92f * open + 1e-6f));

            tongue = 1f;
            AdvancedVisemeMath.ProjectMouthEnvelope(
                1f, ref jaw, ref close, ref open, ref pucker, ref suck, ref tongue);
            Assert.That(tongue, Is.GreaterThan(0.5f));
        }

        [Test]
        public void MAndNEvidenceRemainDistinctAndUseTheMatchingArticulator()
        {
            AdvancedVisemeMath.NasalEvidence(1f, 0f, 1f, 1f, 0f, out var m, out var n);
            Assert.That(m, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(n, Is.Zero);

            AdvancedVisemeMath.NasalEvidence(0f, 1f, 1f, 0f, 1f, out m, out n);
            Assert.That(m, Is.Zero);
            Assert.That(n, Is.EqualTo(1f).Within(1e-6f));

            AdvancedVisemeMath.NasalEvidence(0f, 1f, 1f, 1f, 1f, out m, out n);
            Assert.That(n, Is.EqualTo(1f).Within(1e-6f),
                "Observed lip closure must not veto the merged n/l tongue evidence.");
        }

        [Test]
        public void FaceConditionedNasalPairConservesMassAndAbstainsExactly()
        {
            var random = new System.Random(0x4D4E);
            for (var iteration = 0; iteration < 2000; iteration++)
            {
                var pp = (float)random.NextDouble();
                var nn = (float)random.NextDouble() * (1f - pp);
                var share = (float)random.NextDouble();
                var confidence = (float)random.NextDouble();

                AdvancedVisemeMath.ConditionMergedNasalPair(
                    pp, nn, share, confidence, out var conditionedPp, out var conditionedNn);

                Assert.That(conditionedPp, Is.GreaterThanOrEqualTo(0f));
                Assert.That(conditionedNn, Is.GreaterThanOrEqualTo(0f));
                Assert.That(conditionedPp + conditionedNn,
                    Is.EqualTo(pp + nn).Within(1e-6f));
                Assert.That(conditionedPp,
                    Is.EqualTo(Mathf.Lerp(pp, (pp + nn) * share, confidence)).Within(1e-6f));
            }

            AdvancedVisemeMath.ConditionMergedNasalPair(
                0.15f, 0.75f, 1f, 0f, out var abstainedPp, out var abstainedNn);
            Assert.That(abstainedPp, Is.EqualTo(0.15f).Within(1e-7f));
            Assert.That(abstainedNn, Is.EqualTo(0.75f).Within(1e-7f));
        }

        [Test]
        public void FaceConditionedNasalPairCanCrossTheRawVisemeFamilyContinuously()
        {
            AdvancedVisemeMath.ConditionMergedNasalPair(
                0f, 1f, 1f, 1f, out var closedM, out var closedN);
            Assert.That(closedM, Is.EqualTo(1f).Within(1e-7f),
                "A high-confidence face observation must be able to correct nn into M-compatible mass.");
            Assert.That(closedN, Is.Zero.Within(1e-7f));

            AdvancedVisemeMath.ConditionMergedNasalPair(
                0f, 1f, 1f, 0.35f, out var partialM, out var partialN);
            Assert.That(partialM, Is.EqualTo(0.35f).Within(1e-7f));
            Assert.That(partialN, Is.EqualTo(0.65f).Within(1e-7f));

            AdvancedVisemeMath.ConditionMergedNasalPair(
                1f, 0f, 0f, 1f, out var openM, out var openN);
            Assert.That(openM, Is.Zero.Within(1e-7f));
            Assert.That(openN, Is.EqualTo(1f).Within(1e-7f));
        }

        [Test]
        public void ContractedTongueTensorAndRankOneNasalUpdateMatchTheFullModel()
        {
            var random = new System.Random(0x54454E53);
            foreach (AdvancedVisemeVisibleTongueModelKind kind in Enum.GetValues(
                         typeof(AdvancedVisemeVisibleTongueModelKind)))
            for (var iteration = 0; iteration < 300; iteration++)
            {
                var weights = new float[VisemeReconstructionProfile.VisemeCount];
                var sum = 0f;
                for (var viseme = 0; viseme < weights.Length; viseme++)
                {
                    weights[viseme] = (float)random.NextDouble();
                    sum += weights[viseme];
                }
                for (var viseme = 0; viseme < weights.Length; viseme++)
                    weights[viseme] /= sum;

                var share = (float)random.NextDouble();
                var confidence = (float)random.NextDouble();
                AdvancedVisemeMath.ConditionMergedNasalPair(
                    weights[1], weights[8], share, confidence,
                    out var conditionedPp, out var conditionedNn);
                var delta = conditionedPp - weights[1];
                var conditioned = (float[])weights.Clone();
                conditioned[1] = conditionedPp;
                conditioned[8] = conditionedNn;

                var latentCount = AdvancedVisemeVisibleTongueResidual.LatentCount(kind);
                var latent = Enumerable.Range(0, latentCount)
                    .Select(_ => Mathf.Lerp(-1f, 1f, (float)random.NextDouble()))
                    .ToArray();
                foreach (AdvancedVisemeVisibleTongueOutput output in Enum.GetValues(
                             typeof(AdvancedVisemeVisibleTongueOutput)))
                {
                    var outputScale = Mathf.Max(1e-6f,
                        AdvancedVisemeVisibleTongueResidual.ConservativeOutputBound(kind, output));
                    var biasByViseme = new float[weights.Length];
                    var mixByViseme = new float[weights.Length, latentCount];
                    var expected = 0f;
                    for (var viseme = 0; viseme < weights.Length; viseme++)
                    {
                        var bias = 0f;
                        var mix = new float[latentCount];
                        for (var target = 0;
                             target < AdvancedVisemeVisibleTongueResidual.TongueLatentCount(kind);
                             target++)
                        {
                            var projection = AdvancedVisemeVisibleTongueResidual.OutputProjection(
                                kind, target, output) / outputScale;
                            bias += AdvancedVisemeVisibleTongueResidual.VisemeBias(
                                kind, viseme, target) * projection;
                            for (var latentIndex = 0; latentIndex < latentCount; latentIndex++)
                                mix[latentIndex] +=
                                    AdvancedVisemeVisibleTongueResidual.VisibleLatentSafeBound(
                                        kind, latentIndex) *
                                    AdvancedVisemeVisibleTongueResidual.VisemeMix(
                                        kind, viseme, latentIndex, target) * projection;
                        }
                        biasByViseme[viseme] = bias;
                        for (var latentIndex = 0; latentIndex < latentCount; latentIndex++)
                            mixByViseme[viseme, latentIndex] = mix[latentIndex];

                        var conditional = bias;
                        for (var latentIndex = 0; latentIndex < latentCount; latentIndex++)
                            conditional += latent[latentIndex] * mix[latentIndex];
                        expected += conditioned[viseme] * conditional;
                    }

                    var contracted = 0f;
                    for (var viseme = 0; viseme < weights.Length; viseme++)
                        contracted += weights[viseme] * biasByViseme[viseme];
                    contracted += delta * (biasByViseme[1] - biasByViseme[8]);
                    for (var latentIndex = 0; latentIndex < latentCount; latentIndex++)
                    {
                        var minimum = Enumerable.Range(0, weights.Length)
                            .Min(viseme => mixByViseme[viseme, latentIndex]);
                        var maximum = Enumerable.Range(0, weights.Length)
                            .Max(viseme => mixByViseme[viseme, latentIndex]);
                        var range = maximum - minimum;
                        var unitMix = 0f;
                        if (range > 1e-8f)
                        {
                            for (var viseme = 0; viseme < weights.Length; viseme++)
                                unitMix += weights[viseme] *
                                           ((mixByViseme[viseme, latentIndex] - minimum) / range);
                            unitMix += delta *
                                       (mixByViseme[1, latentIndex] -
                                        mixByViseme[8, latentIndex]) / range;
                            Assert.That(unitMix, Is.InRange(-2e-6f, 1f + 2e-6f),
                                "The affine coefficient shift must remain a legal Direct-tree weight.");
                        }
                        contracted += latent[latentIndex] *
                                      (minimum + range * unitMix);
                    }
                    Assert.That(contracted, Is.EqualTo(expected).Within(2e-5f),
                        $"Contracted {kind}/{output} tensor changed the fitted model.");
                }
            }
        }

        [Test]
        public void CollapsedUnitFeatureKernelAndRankOneNasalUpdateMatchTheFullModel()
        {
            var random = new System.Random(0x554E4954);
            foreach (AdvancedVisemeVisibleTongueModelKind kind in Enum.GetValues(
                         typeof(AdvancedVisemeVisibleTongueModelKind)))
            for (var iteration = 0; iteration < 400; iteration++)
            {
                var weights = new float[VisemeReconstructionProfile.VisemeCount];
                var weightSum = 0f;
                for (var viseme = 0; viseme < weights.Length; viseme++)
                {
                    weights[viseme] = (float)random.NextDouble();
                    weightSum += weights[viseme];
                }
                for (var viseme = 0; viseme < weights.Length; viseme++)
                    weights[viseme] /= weightSum;

                AdvancedVisemeMath.ConditionMergedNasalPair(
                    weights[1], weights[8],
                    (float)random.NextDouble(), (float)random.NextDouble(),
                    out var conditionedPp, out var conditionedNn);
                var delta = conditionedPp - weights[1];
                var conditioned = (float[])weights.Clone();
                conditioned[1] = conditionedPp;
                conditioned[8] = conditionedNn;

                var featureCount = AdvancedVisemeVisibleTongueResidual.FeatureCount(kind);
                var features = Enumerable.Range(0, featureCount)
                    .Select(feature =>
                    {
                        var bound = AdvancedVisemeVisibleTongueResidual
                            .FeatureSafeBound(kind, feature);
                        return Mathf.Lerp(-bound, bound, (float)random.NextDouble());
                    })
                    .ToArray();
                var featureUnits = features.Select((feature, index) =>
                        0.5f + 0.5f * feature /
                        AdvancedVisemeVisibleTongueResidual.FeatureSafeBound(kind, index))
                    .ToArray();
                var expected = new float[AdvancedVisemeVisibleTongueResidual.OutputCount];
                AdvancedVisemeVisibleTongueResidual.PredictUnclamped(
                    kind, conditioned, features, expected);

                var reliability = 0f;
                for (var viseme = 0; viseme < weights.Length; viseme++)
                    reliability += weights[viseme] *
                                   AdvancedVisemeVisibleTongueResidual.Reliability(
                                       kind, viseme);
                reliability += delta *
                    (AdvancedVisemeVisibleTongueResidual.Reliability(kind, 1) -
                     AdvancedVisemeVisibleTongueResidual.Reliability(kind, 8));

                foreach (AdvancedVisemeVisibleTongueOutput output in Enum.GetValues(
                             typeof(AdvancedVisemeVisibleTongueOutput)))
                {
                    float Conditional(int viseme)
                    {
                        var value = AdvancedVisemeAnimatorBuilder.CollapsedTongueUnitBias(
                            kind, viseme, output);
                        for (var feature = 0; feature < featureCount; feature++)
                            value += featureUnits[feature] *
                                     AdvancedVisemeAnimatorBuilder
                                         .CollapsedTongueUnitFeatureCoefficient(
                                             kind, viseme, feature, output);
                        return value;
                    }

                    var kernel = 0f;
                    for (var viseme = 0; viseme < weights.Length; viseme++)
                        kernel += weights[viseme] * Conditional(viseme);
                    kernel += delta * (Conditional(1) - Conditional(8));
                    var actual = reliability * kernel;
                    Assert.That(actual,
                        Is.EqualTo(expected[(int)output]).Within(3e-5f),
                        $"Collapsed {kind}/{output} unit-feature kernel changed " +
                        "the fitted model or its PP-to-nn correction.");
                }
            }
        }

        [Test]
        public void QuantizedLogisticLookupUsesFewerKnotsWithBoundedError()
        {
            foreach (AdvancedVisemeHiddenPhoneModelKind kind in Enum.GetValues(
                         typeof(AdvancedVisemeHiddenPhoneModelKind)))
            {
                var bound = AdvancedVisemeHiddenPhonePosterior.ConservativeLogitBound(kind);
                var points = AdvancedVisemeAnimatorBuilder.LogisticPoints(bound);
                Assert.That(points.Length, Is.EqualTo(13));
                var maxError = 0f;
                for (var sample = 0; sample <= 20000; sample++)
                {
                    var input = Mathf.Lerp(-bound, bound, sample / 20000f);
                    var segment = 0;
                    while (segment + 1 < points.Length - 1 &&
                           points[segment + 1].input < input) segment++;
                    var left = points[segment];
                    var right = points[Mathf.Min(segment + 1, points.Length - 1)];
                    var t = Mathf.InverseLerp(left.input, right.input, input);
                    var approximate = Mathf.Lerp(left.output, right.output, t);
                    maxError = Mathf.Max(maxError,
                        Mathf.Abs(approximate - AdvancedVisemeMath.Logistic(input)));
                }
                Assert.That(maxError, Is.LessThanOrEqualTo(0.0054f),
                    $"The compact {kind} sigmoid LUT exceeded its minimax error envelope.");
            }
        }

        [Test]
        public void HiddenPhonePosteriorIsBoundedSharedAndMatchesRuntimeFeatureClamping()
        {
            foreach (AdvancedVisemeHiddenPhoneModelKind kind in Enum.GetValues(
                         typeof(AdvancedVisemeHiddenPhoneModelKind)))
            {
                var featureCount = AdvancedVisemeHiddenPhonePosterior.FeatureCount(kind);
                Assert.That(featureCount,
                    Is.EqualTo(kind == AdvancedVisemeHiddenPhoneModelKind.Aperture
                        ? 6
                        : kind == AdvancedVisemeHiddenPhoneModelKind.Balanced ? 9 : 12));

                var boundedCoefficientMagnitude = 0f;
                for (var feature = 0; feature < featureCount; feature++)
                {
                    var coefficient = AdvancedVisemeHiddenPhonePosterior.Coefficient(
                        kind, 0, feature);
                    boundedCoefficientMagnitude += Mathf.Abs(coefficient) *
                                                   AdvancedVisemeHiddenPhonePosterior.FeatureSafeBound(
                                                       kind, feature);
                    for (var viseme = 1;
                         viseme < VisemeReconstructionProfile.VisemeCount;
                         viseme++)
                        Assert.That(AdvancedVisemeHiddenPhonePosterior.Coefficient(
                                kind, viseme, feature), Is.EqualTo(coefficient),
                            "The Animator's exact shared-likelihood factorization requires bit-identical rows.");
                }

                var maxBias = Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                    .Max(viseme => Mathf.Abs(
                        AdvancedVisemeHiddenPhonePosterior.Bias(kind, viseme)));
                Assert.That(AdvancedVisemeHiddenPhonePosterior.ConservativeLogitBound(kind),
                    Is.GreaterThanOrEqualTo(maxBias + boundedCoefficientMagnitude - 2e-5f));

                var visemes = new float[VisemeReconstructionProfile.VisemeCount];
                visemes[8] = 1f;
                var outside = Enumerable.Repeat(7f, featureCount).ToArray();
                var clamped = Enumerable.Range(0, featureCount)
                    .Select(feature => AdvancedVisemeHiddenPhonePosterior.FeatureSafeBound(
                        kind, feature)).ToArray();
                Assert.That(AdvancedVisemeHiddenPhonePosterior.PredictLogit(
                        kind, visemes, outside),
                    Is.EqualTo(AdvancedVisemeHiddenPhonePosterior.PredictLogit(
                        kind, visemes, clamped)).Within(1e-6f),
                    "The pure predictor must match Term.Signed's [-2,2] Animator saturation.");

                for (var viseme = 0;
                     viseme < VisemeReconstructionProfile.VisemeCount;
                     viseme++)
                    Assert.That(AdvancedVisemeHiddenPhonePosterior.Reliability(kind, viseme),
                        Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void ClosedNnObservationRaisesMCompatiblePosteriorWithoutBecomingAHardRule()
        {
            var visemes = new float[VisemeReconstructionProfile.VisemeCount];
            visemes[8] = 1f;
            var closed = new float[AdvancedVisemeHiddenPhonePosterior.FeatureCount(
                AdvancedVisemeHiddenPhoneModelKind.Balanced)];
            var open = new float[closed.Length];
            closed[0] = closed[1] = -1f;
            open[0] = open[1] = 1f;

            var closedShare = AdvancedVisemeHiddenPhonePosterior.PredictShare(
                AdvancedVisemeHiddenPhoneModelKind.Balanced, visemes, closed);
            var openShare = AdvancedVisemeHiddenPhonePosterior.PredictShare(
                AdvancedVisemeHiddenPhoneModelKind.Balanced, visemes, open);

            Assert.That(closedShare, Is.GreaterThan(0.5f));
            Assert.That(openShare, Is.LessThan(0.5f));
            Assert.That(closedShare - openShare, Is.GreaterThan(0.5f),
                "Lip/jaw closure should strongly alter compatibility while confidence and stop ambiguity remain separate gates.");

            var nnReliability = AdvancedVisemeHiddenPhonePosterior.Reliability(
                AdvancedVisemeHiddenPhoneModelKind.Balanced, 8);
            var ppReliability = AdvancedVisemeHiddenPhonePosterior.Reliability(
                AdvancedVisemeHiddenPhoneModelKind.Balanced, 1);
            Assert.That(nnReliability, Is.InRange(0.25f, 0.5f),
                "All other phones must remain in the eligibility denominator instead of allowing an overconfident hard flip.");
            Assert.That(ppReliability, Is.LessThan(nnReliability),
                "P/B stop ambiguity should make the PP expert more conservative than nn.");
            AdvancedVisemeMath.ConditionMergedNasalPair(
                0f, 1f, closedShare, nnReliability,
                out var conditionedM, out var conditionedN);
            Assert.That(conditionedM,
                Is.EqualTo(closedShare * nnReliability).Within(1e-6f));
            Assert.That(conditionedM, Is.GreaterThan(0.25f),
                "The correction should be material while remaining a confidence-gated hypothesis.");
            Assert.That(conditionedM + conditionedN, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void HiddenPhonePosteriorAbstainsWhenTheUpstreamObserverIsOutOfDomain()
        {
            var trained = AdvancedVisemeHiddenPhonePosterior.ObserverResponseSeconds;
            Assert.That(AdvancedVisemeAnimatorBuilder.HiddenPhoneObserverCompatibility(trained),
                Is.EqualTo(1f).Within(1e-6f));
            Assert.That(AdvancedVisemeAnimatorBuilder.HiddenPhoneObserverCompatibility(trained / 2f),
                Is.LessThan(0.001f));
            Assert.That(AdvancedVisemeAnimatorBuilder.HiddenPhoneObserverCompatibility(trained * 2f),
                Is.LessThan(0.001f));
            Assert.That(AdvancedVisemeAnimatorBuilder.HiddenPhoneObserverCompatibility(0f),
                Is.Zero);
        }

        [Test]
        public void TrackingBudgetsMatchThePublishedPresets()
        {
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(AdvancedVisemeTrackingInputs.Disabled), Is.Zero);
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(AdvancedVisemeTrackingInputs.ReuseExisting), Is.Zero);
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(AdvancedVisemeTrackingInputs.Auto), Is.Zero);
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(AdvancedVisemeTrackingInputs.Balanced8), Is.EqualTo(25));
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(AdvancedVisemeTrackingInputs.Quality12), Is.EqualTo(39));
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(AdvancedVisemeTrackingInputs.FullTongue18), Is.EqualTo(57));
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(
                AdvancedVisemeTrackingInputs.Balanced8, AdvancedVisemeTrackingEncoding.Uniform4BitBinary), Is.EqualTo(35));
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(
                AdvancedVisemeTrackingInputs.Quality12, AdvancedVisemeTrackingEncoding.Uniform4BitBinary), Is.EqualTo(55));
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(
                AdvancedVisemeTrackingInputs.FullTongue18, AdvancedVisemeTrackingEncoding.Uniform4BitBinary), Is.EqualTo(82));
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(
                AdvancedVisemeTrackingInputs.Balanced8, AdvancedVisemeTrackingEncoding.FullFloat), Is.EqualTo(66));
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(
                AdvancedVisemeTrackingInputs.Quality12, AdvancedVisemeTrackingEncoding.FullFloat), Is.EqualTo(98));
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(
                AdvancedVisemeTrackingInputs.FullTongue18, AdvancedVisemeTrackingEncoding.FullFloat), Is.EqualTo(146));
        }

        [Test]
        public void AutoOnlyEnablesTrackingWhenACompatibleExistingSourceWasFound()
        {
            Assert.That(AdvancedVisemeReconstructorProcessor.ShouldEnableTracking(
                AdvancedVisemeTrackingInputs.Auto, false), Is.False);
            Assert.That(AdvancedVisemeReconstructorProcessor.ShouldEnableTracking(
                AdvancedVisemeTrackingInputs.Auto, true), Is.True);
            Assert.That(AdvancedVisemeReconstructorProcessor.ShouldEnableTracking(
                AdvancedVisemeTrackingInputs.Disabled, true), Is.False);
            Assert.That(AdvancedVisemeReconstructorProcessor.ShouldEnableTracking(
                AdvancedVisemeTrackingInputs.Balanced8, false), Is.True);
            Assert.That(AdvancedVisemeReconstructorProcessor.ShouldEnableTracking(
                AdvancedVisemeTrackingInputs.Quality12, false), Is.True);
            Assert.That(AdvancedVisemeReconstructorProcessor.ShouldEnableTracking(
                AdvancedVisemeTrackingInputs.FullTongue18, false), Is.True);
        }

        [Test]
        public void AutoNeverCreatesAnAdditionalTrackingToggle()
        {
            var root = new GameObject("Advanced Viseme Auto Toggle Test");
            try
            {
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.trackingInputs = AdvancedVisemeTrackingInputs.Auto;
                component.createFaceTrackingToggle = true;
                Assert.That(AdvancedVisemeReconstructorProcessor.ShouldCreateTrackingToggle(
                    component, true), Is.False);

                component.trackingInputs = AdvancedVisemeTrackingInputs.ReuseExisting;
                Assert.That(AdvancedVisemeReconstructorProcessor.ShouldCreateTrackingToggle(
                    component, true), Is.True);
                Assert.That(AdvancedVisemeReconstructorProcessor.ShouldCreateTrackingToggle(
                    component, false), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TrackingCatalogPairsPrefixedSourceWithItsOwnBoolActivityGate()
        {
            var root = new GameObject("Tracking Catalog Prefix Test");
            var controller = new AnimatorController();
            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                var animator = new GameObject("Template Controller").AddComponent<Animator>();
                animator.transform.SetParent(root.transform, false);
                animator.runtimeAnimatorController = controller;

                var channels = new[]
                {
                    "Custom/v2/JawOpen", "Custom/v2/MouthClosed",
                    "Custom/v2/MouthOpen", "Custom/v2/LipFunnel"
                };
                var rootGateChannels = new[]
                {
                    "FT/v2/JawOpen", "FT/v2/MouthClosed",
                    "FT/v2/MouthOpen", "FT/v2/LipFunnel"
                };
                foreach (var channel in channels.Concat(rootGateChannels))
                    controller.AddParameter(channel, AnimatorControllerParameterType.Float);
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = "Custom/ExpressionTrackingActive",
                    type = AnimatorControllerParameterType.Bool,
                    defaultBool = true
                });
                controller.AddParameter("LipTrackingActive", AnimatorControllerParameterType.Float);

                parameters.parameters = channels.Concat(rootGateChannels).Select(channel => new VRCExpressionParameters.Parameter
                    {
                        name = channel,
                        valueType = VRCExpressionParameters.ValueType.Float
                    })
                    .Concat(new[]
                    {
                        new VRCExpressionParameters.Parameter
                        {
                            name = "Custom/ExpressionTrackingActive",
                            valueType = VRCExpressionParameters.ValueType.Bool,
                            defaultValue = 1f
                        },
                        new VRCExpressionParameters.Parameter
                        {
                            name = "LipTrackingActive",
                            valueType = VRCExpressionParameters.ValueType.Bool
                        }
                    }).ToArray();
                foreach (var parameter in parameters.parameters)
                    parameter.networkSynced = true;
                descriptor.expressionParameters = parameters;

                var catalog = AdvancedVisemeTrackingCatalog.Scan(root, descriptor);
                var resolution = catalog.Resolve(profile, "Custom", out var error);

                Assert.That(error, Is.Null);
                Assert.That(resolution, Is.Not.Null);
                Assert.That(resolution.activeParameter, Is.EqualTo("Custom/ExpressionTrackingActive"));
                Assert.That(resolution.activeAnimatorType, Is.EqualTo(AnimatorControllerParameterType.Bool));
                Assert.That(resolution.activeAnimatorDefault, Is.EqualTo(1f));

                var rootGateResolution = catalog.Resolve(profile, "FT", out error);
                Assert.That(error, Is.Null);
                Assert.That(rootGateResolution.activeParameter, Is.EqualTo("LipTrackingActive"));
                Assert.That(rootGateResolution.activeAnimatorType,
                    Is.EqualTo(AnimatorControllerParameterType.Float));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(parameters);
                UnityEngine.Object.DestroyImmediate(controller);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TrackingCatalogReusesSoftPalateOnlyFromTheResolvedPrefix()
        {
            var root = new GameObject("Tracking Catalog Soft Palate Test");
            var controller = new AnimatorController();
            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                var animator = new GameObject("Tailored Template").AddComponent<Animator>();
                animator.transform.SetParent(root.transform, false);
                animator.runtimeAnimatorController = controller;

                foreach (var channel in new[]
                         {
                             "Tailored/v2/JawOpen", "Tailored/v2/MouthClosed",
                             "Tailored/v2/MouthOpen",
                             "Tailored/v2/SoftPalateClose",
                             "Other/v2/SoftPalateClose"
                         })
                    controller.AddParameter(channel, AnimatorControllerParameterType.Float);
                controller.AddParameter(
                    "Tailored/LipTrackingActive", AnimatorControllerParameterType.Float);
                parameters.parameters = new[]
                {
                    new VRCExpressionParameters.Parameter
                    {
                        name = "Tailored/LipTrackingActive",
                        valueType = VRCExpressionParameters.ValueType.Bool,
                        defaultValue = 1f
                    }
                };
                foreach (var parameter in parameters.parameters)
                    parameter.networkSynced = true;
                descriptor.expressionParameters = parameters;

                var resolution = AdvancedVisemeTrackingCatalog.Scan(root, descriptor)
                    .Resolve(profile, "Tailored", out var error);

                Assert.That(error, Is.Null);
                Assert.That(resolution, Is.Not.Null);
                Assert.That(resolution.auxiliaryParameters.ContainsKey("SoftPalateClose"),
                    Is.True);
                Assert.That(resolution.auxiliaryParameters["SoftPalateClose"],
                    Is.EqualTo("Tailored/v2/SoftPalateClose"));
                Assert.That(resolution.auxiliaryParameters.Values,
                    Does.Not.Contain("Other/v2/SoftPalateClose"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(parameters);
                UnityEngine.Object.DestroyImmediate(controller);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TrackingCatalogNeverBorrowsAnotherPrefixesActivityGate()
        {
            var root = new GameObject("Tracking Catalog Isolation Test");
            var controller = new AnimatorController();
            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                var animator = new GameObject("Template Controller").AddComponent<Animator>();
                animator.transform.SetParent(root.transform, false);
                animator.runtimeAnimatorController = controller;
                var channels = new[]
                {
                    "Source/v2/JawOpen", "Source/v2/MouthClosed",
                    "Source/v2/MouthOpen", "Source/v2/LipFunnel"
                };
                foreach (var channel in channels)
                    controller.AddParameter(channel, AnimatorControllerParameterType.Float);
                controller.AddParameter("Other/LipTrackingActive", AnimatorControllerParameterType.Float);
                parameters.parameters = channels.Select(channel => new VRCExpressionParameters.Parameter
                    {
                        name = channel,
                        valueType = VRCExpressionParameters.ValueType.Float
                    })
                    .Concat(new[]
                    {
                        new VRCExpressionParameters.Parameter
                        {
                            name = "Other/LipTrackingActive",
                            valueType = VRCExpressionParameters.ValueType.Bool
                        }
                    }).ToArray();
                foreach (var parameter in parameters.parameters)
                    parameter.networkSynced = true;
                descriptor.expressionParameters = parameters;

                var resolution = AdvancedVisemeTrackingCatalog.Scan(root, descriptor)
                    .Resolve(profile, "Source", out var error);

                Assert.That(resolution, Is.Null);
                Assert.That(error, Does.Contain("tracking-active signal"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(parameters);
                UnityEngine.Object.DestroyImmediate(controller);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TrackingCatalogRejectsConflictingAnimatorParameterTypes()
        {
            var root = new GameObject("Tracking Catalog Type Conflict Test");
            var floatController = new AnimatorController();
            var boolController = new AnimatorController();
            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                foreach (var controller in new[] { floatController, boolController })
                {
                    var animator = new GameObject("Template Controller").AddComponent<Animator>();
                    animator.transform.SetParent(root.transform, false);
                    animator.runtimeAnimatorController = controller;
                }
                var channels = new[]
                {
                    "Source/v2/JawOpen", "Source/v2/MouthClosed",
                    "Source/v2/MouthOpen", "Source/v2/LipFunnel"
                };
                foreach (var channel in channels)
                    floatController.AddParameter(channel, AnimatorControllerParameterType.Float);
                floatController.AddParameter("Source/LipTrackingActive", AnimatorControllerParameterType.Float);
                boolController.AddParameter("Source/LipTrackingActive", AnimatorControllerParameterType.Bool);
                parameters.parameters = channels.Select(channel => new VRCExpressionParameters.Parameter
                    {
                        name = channel,
                        valueType = VRCExpressionParameters.ValueType.Float
                    })
                    .Concat(new[]
                    {
                        new VRCExpressionParameters.Parameter
                        {
                            name = "Source/LipTrackingActive",
                            valueType = VRCExpressionParameters.ValueType.Bool
                        }
                    }).ToArray();
                foreach (var parameter in parameters.parameters)
                    parameter.networkSynced = true;
                descriptor.expressionParameters = parameters;

                var resolution = AdvancedVisemeTrackingCatalog.Scan(root, descriptor)
                    .Resolve(profile, "Source", out var error);

                Assert.That(resolution, Is.Null);
                Assert.That(error, Does.Contain("tracking-active signal"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(parameters);
                UnityEngine.Object.DestroyImmediate(floatController);
                UnityEngine.Object.DestroyImmediate(boolController);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TrackingCatalogRejectsNonFloatAnalogWireParameters()
        {
            var root = new GameObject("Tracking Catalog Wire Type Test");
            var controller = new AnimatorController();
            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                var animator = new GameObject("Template Controller").AddComponent<Animator>();
                animator.transform.SetParent(root.transform, false);
                animator.runtimeAnimatorController = controller;
                var channels = new[]
                {
                    "Source/v2/JawOpen", "Source/v2/MouthClosed",
                    "Source/v2/MouthOpen", "Source/v2/LipFunnel"
                };
                foreach (var channel in channels)
                    controller.AddParameter(channel, AnimatorControllerParameterType.Float);
                controller.AddParameter("Source/LipTrackingActive", AnimatorControllerParameterType.Float);

                parameters.parameters = channels.Select((channel, index) =>
                        new VRCExpressionParameters.Parameter
                        {
                            name = channel,
                            valueType = index == 0
                                ? VRCExpressionParameters.ValueType.Bool
                                : VRCExpressionParameters.ValueType.Float
                        })
                    .Concat(new[]
                    {
                        new VRCExpressionParameters.Parameter
                        {
                            name = "Source/LipTrackingActive",
                            valueType = VRCExpressionParameters.ValueType.Bool
                        }
                    }).ToArray();
                foreach (var parameter in parameters.parameters)
                    parameter.networkSynced = true;
                descriptor.expressionParameters = parameters;

                var resolution = AdvancedVisemeTrackingCatalog.Scan(root, descriptor)
                    .Resolve(profile, "Source", out var error);

                Assert.That(resolution, Is.Null,
                    "A Bool on the expression wire must not be reused as an analog tracking channel.");
                Assert.That(error, Does.Contain("No compatible decoded Unified Expressions source"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(parameters);
                UnityEngine.Object.DestroyImmediate(controller);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TrackingCatalogRejectsUnsyncedActivityGate()
        {
            var root = new GameObject("Tracking Catalog Unsynced Gate Test");
            var controller = new AnimatorController();
            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                var animator = new GameObject("Template Controller").AddComponent<Animator>();
                animator.transform.SetParent(root.transform, false);
                animator.runtimeAnimatorController = controller;
                var channels = new[]
                {
                    "Source/v2/JawOpen", "Source/v2/MouthClosed",
                    "Source/v2/MouthOpen", "Source/v2/LipFunnel"
                };
                foreach (var channel in channels)
                    controller.AddParameter(channel, AnimatorControllerParameterType.Float);
                controller.AddParameter(
                    "Source/LipTrackingActive", AnimatorControllerParameterType.Float);

                parameters.parameters = channels
                    .Select(channel => new VRCExpressionParameters.Parameter
                    {
                        name = channel,
                        valueType = VRCExpressionParameters.ValueType.Float,
                        networkSynced = true
                    })
                    .Concat(new[]
                    {
                        new VRCExpressionParameters.Parameter
                        {
                            name = "Source/LipTrackingActive",
                            valueType = VRCExpressionParameters.ValueType.Bool,
                            networkSynced = false
                        }
                    })
                    .ToArray();
                descriptor.expressionParameters = parameters;

                var resolution = AdvancedVisemeTrackingCatalog.Scan(root, descriptor)
                    .Resolve(profile, "Source", out var error);

                Assert.That(resolution, Is.Null,
                    "A local-only activity gate would leave remote tracking permanently disabled.");
                Assert.That(error, Does.Contain("tracking-active signal"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(parameters);
                UnityEngine.Object.DestroyImmediate(controller);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TrackingCatalogPrefersTheSourceConnectedThroughACommonActivityGate()
        {
            var root = new GameObject("Tracking Catalog Rig Topology Test");
            var controller = new AnimatorController();
            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            var objects = new System.Collections.Generic.List<UnityEngine.Object>();
            try
            {
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                var animator = new GameObject("Tailored Face Template").AddComponent<Animator>();
                animator.transform.SetParent(root.transform, false);
                animator.runtimeAnimatorController = controller;

                const string active = "LipTrackingActive";
                controller.AddParameter(active, AnimatorControllerParameterType.Float);
                var suffixes = new[] { "JawOpen", "MouthClosed", "MouthOpen", "LipFunnel" };
                foreach (var suffix in suffixes)
                {
                    controller.AddParameter("FT/v2/" + suffix, AnimatorControllerParameterType.Float);
                    controller.AddParameter("Tailored/Proxy/v2/" + suffix, AnimatorControllerParameterType.Float);
                }

                parameters.parameters = new[]
                {
                    new VRCExpressionParameters.Parameter
                    {
                        name = active,
                        valueType = VRCExpressionParameters.ValueType.Bool,
                        defaultValue = 1f
                    }
                };
                foreach (var parameter in parameters.parameters)
                    parameter.networkSynced = true;
                descriptor.expressionParameters = parameters;

                var stateMachine = new AnimatorStateMachine { name = "Tailored Face" };
                objects.Add(stateMachine);
                controller.AddLayer(new AnimatorControllerLayer
                {
                    name = "Tailored Face",
                    defaultWeight = 1f,
                    stateMachine = stateMachine
                });
                var output = new BlendTree
                {
                    name = "Final Face Output",
                    blendType = BlendTreeType.Direct,
                    useAutomaticThresholds = false
                };
                objects.Add(output);
                var outputChildren = new System.Collections.Generic.List<ChildMotion>();
                for (var i = 0; i < suffixes.Length; i++)
                {
                    var neutral = new AnimationClip { name = suffixes[i] + " Neutral" };
                    var pose = new AnimationClip { name = suffixes[i] + " Pose" };
                    var unit = new BlendTree
                    {
                        name = suffixes[i] + " Unit",
                        blendType = BlendTreeType.Simple1D,
                        blendParameter = "Tailored/Proxy/v2/" + suffixes[i],
                        useAutomaticThresholds = false,
                        children = new[]
                        {
                            new ChildMotion { motion = neutral, threshold = 0f, timeScale = 1f },
                            new ChildMotion { motion = pose, threshold = 1f, timeScale = 1f }
                        }
                    };
                    AnimationUtility.SetEditorCurve(
                        pose,
                        EditorCurveBinding.FloatCurve(
                            "Face", typeof(SkinnedMeshRenderer), "blendShape.Test" + i),
                        AnimationCurve.Constant(0f, 1f / 60f, 100f));
                    objects.Add(neutral);
                    objects.Add(pose);
                    objects.Add(unit);
                    outputChildren.Add(new ChildMotion
                    {
                        motion = unit,
                        directBlendParameter = active,
                        timeScale = 1f
                    });
                }
                output.children = outputChildren.ToArray();
                stateMachine.AddState("Drive Face").motion = output;

                var catalog = AdvancedVisemeTrackingCatalog.Scan(root, descriptor);
                var resolution = catalog.Resolve(profile, string.Empty, out var error);

                Assert.That(error, Is.Null);
                Assert.That(resolution, Is.Not.Null);
                Assert.That(resolution.prefix, Is.EqualTo("Tailored/Proxy"));
                Assert.That(resolution.poseCoverage, Is.EqualTo(4));
                Assert.That(catalog.ExtractPoses(resolution).Count, Is.EqualTo(4));
            }
            finally
            {
                foreach (var instance in objects.AsEnumerable().Reverse())
                    if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(parameters);
                UnityEngine.Object.DestroyImmediate(controller);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PartialReusedTemplateNeverFabricatesMissingTrackingChannels()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                var request = new AdvancedVisemeAnimatorBuilder.Request
                {
                    profile = profile,
                    reuseExistingTracking = true,
                    trackingPrefix = "FT",
                    trackingParameterNames = new System.Collections.Generic.Dictionary<AdvancedVisemeArticulator, string>
                    {
                        [AdvancedVisemeArticulator.JawOpen] = "FT/v2/JawOpen"
                    }
                };

                Assert.That(AdvancedVisemeAnimatorBuilder.TryResolveTrackingParameter(
                    request,
                    AdvancedVisemeArticulator.JawOpen,
                    profile.FindBinding(AdvancedVisemeArticulator.JawOpen),
                    out var jaw), Is.True);
                Assert.That(jaw, Is.EqualTo("FT/v2/JawOpen"));
                Assert.That(AdvancedVisemeAnimatorBuilder.TryResolveTrackingParameter(
                    request,
                    AdvancedVisemeArticulator.MouthOpen,
                    profile.FindBinding(AdvancedVisemeArticulator.MouthOpen),
                    out var missing), Is.False);
                Assert.That(missing, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void FaceConditionedInferenceRequiresCompleteApertureTracking()
        {
            var root = new GameObject("Face Inference Capability Test");
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.reconstructionMode =
                    AdvancedVisemeReconstructionMode.BetaCoarticulation;
                var request = new AdvancedVisemeAnimatorBuilder.Request
                {
                    component = component,
                    profile = profile,
                    trackingEnabled = true,
                    reuseExistingTracking = true,
                    effectiveTrackingInputs = AdvancedVisemeTrackingInputs.Balanced8,
                    trackingParameterNames = new System.Collections.Generic.Dictionary<
                        AdvancedVisemeArticulator, string>
                    {
                        [AdvancedVisemeArticulator.JawOpen] = "Tailored/v2/JawOpen",
                        [AdvancedVisemeArticulator.LipClose] = "Tailored/v2/MouthClosed"
                    }
                };

                Assert.That(AdvancedVisemeAnimatorBuilder
                    .CanBuildFaceConditionedTongueInference(request), Is.False,
                    "A partial template must not pay for an unreachable hidden-phone graph.");

                request.trackingParameterNames[AdvancedVisemeArticulator.MouthOpen] =
                    "Tailored/v2/MouthOpen";
                Assert.That(AdvancedVisemeAnimatorBuilder
                    .CanBuildFaceConditionedTongueInference(request), Is.True);

                request.trackingEnabled = false;
                Assert.That(AdvancedVisemeAnimatorBuilder
                    .CanBuildFaceConditionedTongueInference(request), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PartialBetaTemplateContractsTongueGroupsAndUsesConvexFusion()
        {
            var root = new GameObject("Optimized Partial Beta Graph Test");
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            var folderName = "__YUCP_AVR_OptimizedPartial_" + Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            try
            {
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.reconstructionMode =
                    AdvancedVisemeReconstructionMode.BetaCoarticulation;
                component.mouthOwnership = AdvancedVisemeMouthOwnership.OutputsOnly;
                component.trackingInputs = AdvancedVisemeTrackingInputs.Auto;
                component.createTuningMenu = false;
                var reused = new System.Collections.Generic.Dictionary<
                    AdvancedVisemeArticulator, string>
                {
                    [AdvancedVisemeArticulator.JawOpen] = "Tailored/v2/JawOpen"
                };

                var controllerPath = folder + "/AdvancedViseme.controller";
                var result = AdvancedVisemeAnimatorBuilder.Build(
                    new AdvancedVisemeAnimatorBuilder.Request
                    {
                        controllerPath = controllerPath,
                        parametersPath = folder + "/TrackingParameters.asset",
                        component = component,
                        profile = profile,
                        trackingPrefix = "Tailored",
                        effectiveTrackingInputs = AdvancedVisemeTrackingInputs.Balanced8,
                        reuseExistingTracking = true,
                        trackingActiveParameter = "Tailored/LipTrackingActive",
                        trackingActiveAnimatorType = AnimatorControllerParameterType.Float,
                        trackingActiveDefault = 1f,
                        trackingParameterNames = reused,
                        sourceVisemeBlendShapes =
                            new string[VisemeReconstructionProfile.VisemeCount],
                        calibrationBasis =
                            Array.Empty<AdvancedVisemeMeshCalibrator.BasisInput>(),
                        resolvedBlendShapes = new System.Collections.Generic.Dictionary<
                            AdvancedVisemeArticulator, string>(),
                        externalPoses = new System.Collections.Generic.Dictionary<
                            AdvancedVisemeArticulator, AdvancedVisemeExternalPose>(),
                        trackingEnabled = true,
                        existingExpressionParameters = new System.Collections.Generic.HashSet<string>(
                            reused.Values.Concat(new[] { "Tailored/LipTrackingActive" }))
                    });

                var parameters = result.controller.parameters
                    .Select(parameter => parameter.name).ToArray();
                Assert.That(parameters.Any(name => name.Contains(
                    "/BetaCoarticulation/TongueTip/Viseme/")), Is.False);
                Assert.That(parameters.Any(name => name.Contains(
                    "/BetaCoarticulation/TongueBody/Viseme/")), Is.False,
                    "Unobservable tongue simplexes must be contracted out of the Animator.");
                Assert.That(parameters.Any(name => name.EndsWith(
                    "/SpeechSlowPart", StringComparison.Ordinal) ||
                    name.EndsWith("/SpeechFastPart", StringComparison.Ordinal) ||
                    name.EndsWith("/TrackingSlowPart", StringComparison.Ordinal) ||
                    name.EndsWith("/TrackingFastPart", StringComparison.Ordinal)), Is.False,
                    "Convex fusion must not materialize scalar product temporaries.");
                Assert.That(parameters.Any(name => name.Contains(
                    "/VisibleSpeechWeight", StringComparison.Ordinal)), Is.False,
                    "Outputs Only must not build mesh-ownership suppression weights.");
                Assert.That(parameters.Any(name => name.EndsWith(
                    "/Contribution", StringComparison.Ordinal)), Is.False,
                    "Outputs Only must not multiply a tracking pose that no output consumes.");
                Assert.That(parameters.Any(name => name.EndsWith(
                    "/InverseGain", StringComparison.Ordinal)), Is.False,
                    "External calibrated-ray inverses must remain demand driven.");

                var trees = AssetDatabase.LoadAllAssetsAtPath(controllerPath)
                    .OfType<BlendTree>().ToArray();
                Assert.That(trees.Count(tree => tree.name ==
                    "Vector transient-silence hold"), Is.EqualTo(1),
                    "All compatible speech observers should share one silence router.");
                Assert.That(trees.Any(tree => tree.name.Contains(
                    "Tracking/JawOpen/FusedSlow", StringComparison.Ordinal) ||
                    tree.name.Contains("Tracking/JawOpen/BaseGain", StringComparison.Ordinal)),
                    Is.True, "Tracking fusion should remain a continuous interpolation tree.");
                Assert.That(trees.Any(tree => tree.name ==
                    "Tracking observer fast vector"), Is.True);
                Assert.That(trees.Any(tree => tree.name ==
                    "Tracking observer slow vector"), Is.True,
                    "Tracking coordinates sharing one pole should use two vector observers.");
                Assert.That(trees.Any(tree => tree.name.StartsWith(
                    "Vector product by YUCP/AdvancedViseme/_Internal/Voice/Gain",
                    StringComparison.Ordinal)), Is.True,
                    "Beta speech amplitude should be applied once per articulation vector.");
                Assert.That(parameters, Does.Contain(
                    "YUCP/AdvancedViseme/Articulation/JawOpen"));
                Assert.That(parameters, Does.Contain(
                    "YUCP/AdvancedViseme/Velocity/JawOpen"));
                Assert.That(trees.Any(tree => tree.name.StartsWith(
                    "Smooth YUCP/AdvancedViseme/_Internal/Tracking/JawOpen/Fast toward Tailored/v2/JawOpen",
                    StringComparison.Ordinal) || tree.name.StartsWith(
                    "Smooth YUCP/AdvancedViseme/_Internal/Tracking/JawOpen/Slow toward YUCP/AdvancedViseme/_Internal/Tracking/JawOpen/Fast",
                    StringComparison.Ordinal)), Is.False,
                    "The scalar per-coordinate tracking observers must not return.");
                Assert.That(trees.Any(tree => tree.name.StartsWith(
                    "Multiply YUCP/AdvancedViseme/_Internal/Voice/Gain *",
                    StringComparison.Ordinal) && tree.name.Contains(
                    "/Corpus", StringComparison.Ordinal)), Is.False,
                    "The scalar per-articulator Beta amplitude pass must not return.");
                Assert.That(parameters.Any(name => name.Contains(
                    "/Constraint/Shared/", StringComparison.Ordinal)), Is.True);
                Assert.That(parameters.Any(name => name.Contains(
                    "/Constraint/Fast/PP/GloballyTuned", StringComparison.Ordinal) ||
                    name.Contains("/Constraint/Slow/PP/GloballyTuned",
                        StringComparison.Ordinal)), Is.False,
                    "Fast and slow constraints should reuse their common tuning gate.");

                var random = new System.Random(0x46555345);
                for (var sample = 0; sample < 128; sample++)
                {
                    var speech = (float)random.NextDouble();
                    var tracking = (float)random.NextDouble();
                    var gain = (float)random.NextDouble();
                    Assert.That(Mathf.LerpUnclamped(speech, tracking, gain),
                        Is.EqualTo((1f - gain) * speech + gain * tracking).Within(1e-6f));

                    var voiceGain = (float)random.NextDouble();
                    var signedArticulation = (float)(2d * random.NextDouble() - 1d);
                    Assert.That(voiceGain * signedArticulation,
                        Is.EqualTo(signedArticulation * voiceGain).Within(1e-6f));

                    var fast = (float)(2d * random.NextDouble() - 1d);
                    var slow = (float)(2d * random.NextDouble() - 1d);
                    var response = 0.005f + 0.115f * (float)random.NextDouble();
                    Assert.That((fast - slow) / response,
                        Is.EqualTo(fast / response - slow / response).Within(1e-5f));

                    var active = (float)random.NextDouble();
                    var viseme = (float)random.NextDouble();
                    var globalStrength = (float)random.NextDouble();
                    var channelStrength = (float)random.NextDouble();
                    var local = (float)random.NextDouble();
                    var authority = (float)random.NextDouble();
                    var scalarConstraint = active * viseme * globalStrength *
                                           channelStrength * (1f - local * authority);
                    var factoredConstraint = active * (globalStrength * channelStrength) *
                                             (1f - local * authority) * viseme;
                    Assert.That(factoredConstraint,
                        Is.EqualTo(scalarConstraint).Within(1e-6f));
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BetaFallbackBuildsGroupCorrectionWithoutFaceTracking()
        {
            var root = new GameObject("Beta Fallback Correction Test");
            try
            {
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                var request = new AdvancedVisemeAnimatorBuilder.Request
                {
                    component = component,
                    trackingEnabled = false
                };

                component.reconstructionMode = AdvancedVisemeReconstructionMode.Normal;
                Assert.That(AdvancedVisemeAnimatorBuilder.ShouldBuildFallbackArticulationCorrection(request),
                    Is.True,
                    "Runtime tongue sliders need a linear correction path even in Normal speech-only mode.");

                component.createTuningMenu = false;
                Assert.That(AdvancedVisemeAnimatorBuilder.ShouldBuildFallbackArticulationCorrection(request),
                    Is.False);

                component.reconstructionMode = AdvancedVisemeReconstructionMode.BetaCoarticulation;
                Assert.That(AdvancedVisemeAnimatorBuilder.ShouldBuildFallbackArticulationCorrection(request),
                    Is.True,
                    "Beta's group-specific articulation must not disappear in direct-viseme fallback.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BetaArticulatorContractionCommutesWithContinuousObserverLead()
        {
            var random = new System.Random(0x425443);
            const int articulatorCount = 7;
            for (var sample = 0; sample < 256; sample++)
            {
                var raw = RandomSimplex();
                var fast = RandomSimplex();
                var slow = RandomSimplex();
                var lead = Mathf.Lerp(0.2f, 1f, (float)random.NextDouble());
                var matrix = new float[articulatorCount, VisemeReconstructionProfile.VisemeCount];
                for (var articulator = 0; articulator < articulatorCount; articulator++)
                for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
                    matrix[articulator, viseme] = (float)(random.NextDouble() * 2d - 1d);

                AssertFastStage(fast, raw, lead);
                AssertStage(slow, fast, lead);

                void AssertFastStage(float[] values, float[] rawTarget, float amount)
                {
                    var detectedLegacyShortcut = false;
                    for (var articulator = 0; articulator < articulatorCount; articulator++)
                    {
                        var projectedFast = 0f;
                        var projectedRaw = 0f;
                        for (var viseme = 0;
                             viseme < VisemeReconstructionProfile.VisemeCount;
                             viseme++)
                        {
                            projectedFast += matrix[articulator, viseme] * values[viseme];
                            projectedRaw += matrix[articulator, viseme] * rawTarget[viseme];
                        }
                        var correctedFast = projectedFast;
                        var legacyRawAdvanced = Mathf.LerpUnclamped(
                            projectedFast, projectedRaw, amount);
                        Assert.That(correctedFast,
                            Is.EqualTo(projectedFast).Within(1e-7f));
                        detectedLegacyShortcut |= Mathf.Abs(
                            legacyRawAdvanced - correctedFast) > 1e-4f;
                    }
                    Assert.That(detectedLegacyShortcut, Is.True,
                        "The randomized fixture must distinguish direct observer-fast " +
                        "projection from the removed raw-advanced projection.");
                }

                void AssertStage(float[] from, float[] to, float amount)
                {
                    for (var articulator = 0; articulator < articulatorCount; articulator++)
                    {
                        var projectAfterInterpolation = 0f;
                        var projectFrom = 0f;
                        var projectTo = 0f;
                        for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
                        {
                            var coefficient = matrix[articulator, viseme];
                            projectAfterInterpolation += coefficient *
                                Mathf.LerpUnclamped(from[viseme], to[viseme], amount);
                            projectFrom += coefficient * from[viseme];
                            projectTo += coefficient * to[viseme];
                        }
                        var interpolateAfterProjection = Mathf.LerpUnclamped(
                            projectFrom, projectTo, amount);
                        Assert.That(projectAfterInterpolation,
                            Is.EqualTo(interpolateAfterProjection).Within(2e-6f));
                    }
                }

                float[] RandomSimplex()
                {
                    var values = new float[VisemeReconstructionProfile.VisemeCount];
                    var sum = 0f;
                    for (var i = 0; i < values.Length; i++)
                    {
                        values[i] = (float)random.NextDouble();
                        sum += values[i];
                    }
                    for (var i = 0; i < values.Length; i++)
                        values[i] /= sum;
                    return values;
                }
            }
        }

        [Test]
        public void BetaProjectionOnlySelectsDomainSafeProfitableRows()
        {
            float[] WithNonzero(int count, float value = 0.5f)
            {
                var coefficients = new float[VisemeReconstructionProfile.VisemeCount];
                for (var index = 0; index < count; index++) coefficients[index] = value;
                return coefficients;
            }

            Assert.That(AdvancedVisemeAnimatorBuilder.ShouldProjectBetaArticulationRow(
                AdvancedVisemeArticulator.JawOpen, WithNonzero(5)), Is.False);
            Assert.That(AdvancedVisemeAnimatorBuilder.ShouldProjectBetaArticulationRow(
                AdvancedVisemeArticulator.JawOpen, WithNonzero(6)), Is.True);
            Assert.That(AdvancedVisemeAnimatorBuilder.ShouldProjectBetaArticulationRow(
                AdvancedVisemeArticulator.TongueY, WithNonzero(6)), Is.False);
            var signedDense = WithNonzero(7, -0.5f);
            signedDense[1] = 0.5f;
            Assert.That(AdvancedVisemeAnimatorBuilder.ShouldProjectBetaArticulationRow(
                AdvancedVisemeArticulator.TongueY, signedDense), Is.True);
            var encodedSigned = AdvancedVisemeAnimatorBuilder.EncodeBetaProjectionRow(
                AdvancedVisemeArticulator.TongueY, signedDense,
                out var signedOffset, out var signedScale);
            Assert.That(encodedSigned.All(value => value >= 0f && value <= 1f), Is.True);
            for (var index = 0; index < signedDense.Length; index++)
                Assert.That(signedOffset + signedScale * encodedSigned[index],
                    Is.EqualTo(signedDense[index]).Within(1e-7f));

            var negativeUnsigned = WithNonzero(6);
            negativeUnsigned[0] = -0.1f;
            Assert.That(AdvancedVisemeAnimatorBuilder.ShouldProjectBetaArticulationRow(
                AdvancedVisemeArticulator.JawOpen, negativeUnsigned), Is.False);
            var outOfRangeSigned = WithNonzero(7);
            outOfRangeSigned[0] = 2.01f;
            Assert.That(AdvancedVisemeAnimatorBuilder.ShouldProjectBetaArticulationRow(
                AdvancedVisemeArticulator.TongueY, outOfRangeSigned), Is.False);
            var nonFinite = WithNonzero(7);
            nonFinite[0] = float.NaN;
            Assert.That(AdvancedVisemeAnimatorBuilder.ShouldProjectBetaArticulationRow(
                AdvancedVisemeArticulator.TongueY, nonFinite), Is.False);
        }

        [Test]
        public void GeneratedBetaGraphUsesContinuousContextAndDenoisedTongueInference()
        {
            var root = new GameObject("Generated Beta Graph Test");
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            var folderName = "__YUCP_AVR_GraphTest_" + Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            // This test asserts the lowering's structure (per-decay alphas,
            // retention rows). Congruence interning may legitimately share
            // identical values across subsystems and is verified separately.
            var previousSkipCongruence = AdvancedVisemeAnimatorGraphOptimizer
                .SkipCongruenceInterningForStructureTests;
            AdvancedVisemeAnimatorGraphOptimizer
                .SkipCongruenceInterningForStructureTests = true;
            try
            {
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.reconstructionMode = AdvancedVisemeReconstructionMode.BetaCoarticulation;
                component.mouthOwnership = AdvancedVisemeMouthOwnership.OutputsOnly;
                component.trackingInputs = AdvancedVisemeTrackingInputs.Balanced8;

                var result = AdvancedVisemeAnimatorBuilder.Build(
                    new AdvancedVisemeAnimatorBuilder.Request
                    {
                        controllerPath = folder + "/AdvancedViseme.controller",
                        parametersPath = folder + "/TrackingParameters.asset",
                        component = component,
                        profile = profile,
                        trackingPrefix = "YUCP/TestFaceTracking",
                        effectiveTrackingInputs = AdvancedVisemeTrackingInputs.Balanced8,
                        reuseExistingTracking = false,
                        trackingActiveParameter = "YUCP/TestFaceTracking/LipTrackingActive",
                        trackingActiveAnimatorType = AnimatorControllerParameterType.Float,
                        trackingActiveDefault = 1f,
                        trackingParameterNames = new System.Collections.Generic.Dictionary<AdvancedVisemeArticulator, string>(),
                        sourceVisemeBlendShapes = new string[VisemeReconstructionProfile.VisemeCount],
                        calibrationBasis = Array.Empty<AdvancedVisemeMeshCalibrator.BasisInput>(),
                        // One driveable channel deliberately creates mixed
                        // tracking-supported/unsupported viseme rows. This keeps
                        // their Animator write depth covered by the runtime test.
                        resolvedBlendShapes = new System.Collections.Generic.Dictionary<AdvancedVisemeArticulator, string>
                        {
                            { AdvancedVisemeArticulator.JawOpen, "DummyJawOpen" }
                        },
                        externalPoses = new System.Collections.Generic.Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose>(),
                        trackingEnabled = true,
                        existingExpressionParameters = new System.Collections.Generic.HashSet<string>()
                    });

                var trees = AssetDatabase.LoadAllAssetsAtPath(folder + "/AdvancedViseme.controller")
                    .OfType<BlendTree>().ToArray();
                var generatedClips = AssetDatabase.LoadAllAssetsAtPath(
                        folder + "/AdvancedViseme.controller")
                    .OfType<AnimationClip>().ToArray();
                Debug.Log(
                    $"AVR sparse fixture inventory: trees={trees.Length}, " +
                    $"clips={generatedClips.Length}, curves=" +
                    $"{generatedClips.Sum(clip => AnimationUtility.GetCurveBindings(clip).Length)}");
                if (!string.IsNullOrEmpty(GeneratedBetaGraphProfileCopyPath))
                {
                    AssetDatabase.DeleteAsset(GeneratedBetaGraphProfileCopyPath);
                    Assert.That(AssetDatabase.CopyAsset(
                        folder + "/AdvancedViseme.controller",
                        GeneratedBetaGraphProfileCopyPath), Is.True);
                    AssetDatabase.ImportAsset(GeneratedBetaGraphProfileCopyPath);
                }
                Assert.That(result.optimizerReport, Is.Not.Null);
                Assert.That(result.optimizerReport.internalParametersAfter,
                    Is.EqualTo(result.optimizerReport.internalParametersBefore -
                               result.optimizerReport.removedInternalParameters));
                Assert.That(result.optimizerReport.animatorCurvesAfter,
                    Is.EqualTo(result.optimizerReport.animatorCurvesBefore -
                               result.optimizerReport.removedAnimatorCurves));
                Assert.That(result.optimizerReport.removedNeutralZeroCurves,
                    Is.GreaterThanOrEqualTo(240),
                    "The exact lowerer should remove the remaining proven " +
                    "neutral-zero binders after constructive baseline hoisting.");
                Assert.That(generatedClips.Any(clip => clip.name ==
                    "Projected destination safety zero"), Is.False,
                    "A weighted destination row must reuse its enclosing vector binder.");
                var parameterNames = result.controller.parameters.Select(parameter => parameter.name).ToArray();
                Assert.That(parameterNames.Count(name => name.EndsWith(
                        "/PhoneObservationFast", StringComparison.Ordinal)),
                    Is.EqualTo(VisemeReconstructionProfile.VisemeCount),
                    "Face-conditioned inference keeps one trained private observation " +
                    "simplex, separate from the visible continuous-fast simplex.");
                Assert.That(parameterNames.Count(name => name.EndsWith(
                        "/ProductAccumulator", StringComparison.Ordinal)),
                    Is.EqualTo(2),
                    "The visible-tongue bilinear stage should publish one fused accumulator per output.");
                Assert.That(parameterNames.Any(name => name.Contains(
                        "/TongueInference/Model/TongueOut/Product/", StringComparison.Ordinal) ||
                    name.Contains(
                        "/TongueInference/Model/TongueY/Product/", StringComparison.Ordinal)),
                    Is.False,
                    "The fused tongue graph must not rematerialize eight scalar product AAPs.");
                foreach (var direct in trees.Where(tree =>
                             tree.blendType == BlendTreeType.Direct))
                {
                    var serializedTree = new SerializedObject(direct);
                    var normalized = serializedTree.FindProperty("m_NormalizedBlendValues");
                    if (normalized != null)
                        Assert.That(normalized.boolValue, Is.False,
                            $"Direct BlendTree '{direct.name}' must not normalize mathematical weights.");
                }
                Assert.That(parameterNames.Count(name => name.Contains(
                    "/BetaCoarticulation/Context/", StringComparison.Ordinal)),
                    Is.EqualTo(2),
                    "Only one alpha per learned decay should survive context projection.");
                Assert.That(parameterNames.Count(name => name.Contains(
                    "/BetaCoarticulation/RetentionTarget/", StringComparison.Ordinal)),
                    Is.EqualTo(AdvancedVisemeTransitionRetention.GroupCount *
                        VisemeReconstructionProfile.VisemeCount),
                    "Every learned retention row must remain available to the observer.");
                Assert.That(parameterNames.Count(name => name.Contains(
                    "/BetaCoarticulation/RetentionState/", StringComparison.Ordinal)),
                    Is.EqualTo(AdvancedVisemeTransitionRetention.GroupCount *
                        VisemeReconstructionProfile.VisemeCount),
                    "The EMA must run directly in the learned matrix row space.");
                Assert.That(trees.Any(tree => tree.name.StartsWith(
                    "Corpus context projection", StringComparison.Ordinal)), Is.False,
                    "The dense c^T R contraction must not return.");
                Assert.That(trees.Any(tree => tree.name.StartsWith(
                    "Previous context ->", StringComparison.Ordinal)), Is.False,
                    "The old 15x15 nested table expansion must not return.");
                Assert.That(trees.Count(tree => tree.name ==
                    "Vector transient-silence hold"), Is.EqualTo(1),
                    "Compatible Beta observers must share one vector silence router.");
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/BetaCoarticulation/Jaw/Viseme/")), Is.False);
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/BetaCoarticulation/Lips/Viseme/")), Is.False,
                    "Jaw and lip groups must not materialize redundant 15-weight posteriors.");
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/BetaCoarticulation/TongueTip/Viseme/")), Is.True);
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/BetaCoarticulation/TongueBody/Viseme/")), Is.True,
                    "The observed PP/nn tongue coordinates remain available to hidden-phone inference.");
                var tongueTipFast = parameterNames.Where(name => name.Contains(
                    "/BetaCoarticulation/TongueTip/Viseme/") &&
                    name.EndsWith("/Fast", StringComparison.Ordinal)).ToArray();
                var tongueTipSlow = parameterNames.Where(name => name.Contains(
                    "/BetaCoarticulation/TongueTip/Viseme/") &&
                    name.EndsWith("/Slow", StringComparison.Ordinal)).ToArray();
                var tongueBodyFast = parameterNames.Where(name => name.Contains(
                    "/BetaCoarticulation/TongueBody/Viseme/") &&
                    name.EndsWith("/Fast", StringComparison.Ordinal)).ToArray();
                var tongueBodySlow = parameterNames.Where(name => name.Contains(
                    "/BetaCoarticulation/TongueBody/Viseme/") &&
                    name.EndsWith("/Slow", StringComparison.Ordinal)).ToArray();
                Assert.That(tongueTipFast.Length, Is.EqualTo(2));
                Assert.That(tongueTipSlow.Length,
                    Is.EqualTo(VisemeReconstructionProfile.VisemeCount));
                Assert.That(tongueBodyFast.Length, Is.EqualTo(2));
                Assert.That(tongueBodySlow.Length, Is.EqualTo(2),
                    "Consumer-driven projection must not publish unobserved tongue coordinates.");
                Assert.That(trees.Length, Is.LessThan(850),
                    "The generated graph must stay vector-lowered instead of returning to scalar expansion.");
                var curveBindingCount = AssetDatabase
                    .LoadAllAssetsAtPath(folder + "/AdvancedViseme.controller")
                    .OfType<AnimationClip>()
                    .Sum(clip => AnimationUtility.GetCurveBindings(clip).Length);
                Assert.That(curveBindingCount, Is.LessThan(6000),
                    "The generated math graph must not republish zero or dead AAP curves.");
                Assert.That(parameterNames.Length, Is.LessThan(1200),
                    "Internal parameter growth is a proxy for additional frame-staged math.");
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/FallbackCommonSpeechSlow", StringComparison.Ordinal) ||
                    name.Contains("/TunedFallbackSpeech", StringComparison.Ordinal)), Is.False,
                    "Output-basis membership must not materialize dead articulation values.");
                Assert.That(parameterNames.Any(name =>
                    name.EndsWith("/LowBlend", StringComparison.Ordinal) ||
                    name.EndsWith("/HighBlend", StringComparison.Ordinal) ||
                    name.EndsWith("/Lower", StringComparison.Ordinal)), Is.False,
                    "Three-point tuning should lower to one piecewise-linear motion.");
                Assert.That(parameterNames.Any(name =>
                    name.Contains("/Alpha/TrackingDifference", StringComparison.Ordinal) ||
                    name.Contains("/Alpha/TrackingLocalPart", StringComparison.Ordinal) ||
                    name.Contains("/Alpha/TrackingBlendDifference", StringComparison.Ordinal) ||
                    name.Contains("/Alpha/TrackingBlendAttackPart", StringComparison.Ordinal)), Is.False,
                    "Binary alpha selection should lower directly to interpolation.");
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/TongueInference/Model/TongueOut/Stable")), Is.True);
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/TongueInference/Model/TongueY/Stable")), Is.True);
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/PhonePosterior/Model/MShareSlow")), Is.True,
                    "Beta must stabilize the trained face-conditioned phone posterior.");
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/PhonePosterior/OodConfidence")), Is.True);
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/TongueInference/OodConfidence")), Is.True,
                    "The exact-Beta phone model and older tongue residual need independent empirical support gates.");
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/PhonePosterior/Model/Viseme/0/NormalizedLogit")), Is.False,
                    "The shared face likelihood must be factored once rather than duplicated for every viseme prior.");
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/PhonePosterior/TongueTip/Slow/Delta")), Is.True,
                    "The hidden PP/nn update should be retained as one rank-one correction.");
                Assert.That(parameterNames.Any(name =>
                    name.Contains("/PhonePosterior/Tongue", StringComparison.Ordinal) &&
                    (name.Contains("/CandidateMass", StringComparison.Ordinal) ||
                     name.Contains("/TargetPP", StringComparison.Ordinal) ||
                     name.Contains("/RawDelta", StringComparison.Ordinal))), Is.False,
                    "The nasal sum-product must be fused into its final observable delta.");
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/PhonePosterior/TongueTip/Slow/Viseme/")), Is.False,
                    "The rank-one PP/nn update must not materialize another 15-weight simplex.");
                Assert.That(parameterNames.Any(name =>
                    name.Contains("/PhonePosterior/Hypothesis/NShare", StringComparison.Ordinal) ||
                    name.Contains("/PhonePosterior/Hypothesis/NMass", StringComparison.Ordinal)), Is.False,
                    "M/N diagnostics must be emitted from one phase-coherent nested motion.");
                Assert.That(trees.Any(tree => tree.name ==
                    "Hidden phone hypothesis M-N distribution"), Is.True);
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/TongueInference/Model/Viseme/")), Is.False,
                    "The visible-tongue tensor must stay in its contracted matrix form.");
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/PhonePosterior/TongueTip/Fast/Delta", StringComparison.Ordinal)), Is.True);
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/TongueInference/Model/TongueOut/Stable", StringComparison.Ordinal)), Is.True);
                var alphaVectors = trees.Where(tree => tree.name ==
                    "Frame-rate-correct alpha vector").ToArray();
                Assert.That(alphaVectors.Length, Is.EqualTo(1),
                    "All frame-rate alpha outputs should share one sampled lookup tree.");
                Assert.That(alphaVectors[0].children.Length, Is.EqualTo(10));
                var alphaBindingCounts = alphaVectors[0].children
                    .Select(child => child.motion as AnimationClip)
                    .Select(clip => clip == null ? 0 : AnimationUtility.GetCurveBindings(clip).Length)
                    .ToArray();
                var alphaBindingUnion = alphaVectors[0].children
                    .Select(child => child.motion as AnimationClip)
                    .Where(clip => clip != null)
                    .SelectMany(AnimationUtility.GetCurveBindings)
                    .Select(binding => binding.propertyName)
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                Assert.That(alphaBindingUnion, Is.GreaterThan(5),
                    "The shared lookup must still publish every response-time alpha.");
                Assert.That(alphaBindingCounts.Sum(),
                    Is.LessThanOrEqualTo(alphaBindingUnion * alphaBindingCounts.Length),
                    "The alpha lookup must not duplicate a parameter binding within a knot.");
                Assert.That(result.globalParameters, Does.Contain(
                    component.NormalizedPrefix + "/Speech/Hypothesis/M"));
                Assert.That(result.globalParameters, Does.Contain(
                    component.NormalizedPrefix + "/Speech/Hypothesis/N"));
                Assert.That(result.globalParameters, Does.Contain(
                    component.NormalizedPrefix + "/Speech/Hypothesis/Confidence"));
                Assert.That(result.parameters.parameters.Any(parameter =>
                    parameter.name.Contains("/Speech/Hypothesis/")), Is.False,
                    "Posterior diagnostics are local Animator globals, not synced inputs.");
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/Constraint/Fast/PPConfidence")), Is.True);
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/Constraint/Slow/PPConfidence")), Is.True);
                Assert.That(parameterNames.Any(name => name.Contains("ViolationActive")), Is.False,
                    "The old non-monotone constraint graph must not return.");

                var visibleWeightNames = Enumerable.Range(
                        0, VisemeReconstructionProfile.VisemeCount)
                    .Select(index => component.NormalizedPrefix +
                        $"/_Internal/Viseme/{index}/VisibleSpeechWeight")
                    .ToArray();
                Assert.That(visibleWeightNames.Any(parameterNames.Contains), Is.False,
                    "Outputs Only must not build mesh-ownership suppression weights.");

                var runtimeRoot = new GameObject("Generated Beta Graph Runtime Test");
                try
                {
                    var animator = runtimeRoot.AddComponent<Animator>();
                    animator.runtimeAnimatorController = result.controller;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.Rebind();
                    animator.Update(0f);

                    var internalPrefix = component.NormalizedPrefix + "/_Internal";
                    var projectedRows = Enum.GetValues(typeof(AdvancedVisemeArticulator))
                        .Cast<AdvancedVisemeArticulator>()
                        .Select(articulator => new
                        {
                            articulator,
                            coefficients = Enumerable.Range(
                                    0, VisemeReconstructionProfile.VisemeCount)
                                .Select(viseme => profile.visemePoses[viseme]
                                    .Get(articulator) *
                                    profile.GetVisemeArticulationMultiplier(
                                        viseme, articulator))
                                .ToArray()
                        })
                        .Where(row => AdvancedVisemeAnimatorBuilder
                            .ShouldProjectBetaArticulationRow(
                                row.articulator, row.coefficients))
                        .Select(row =>
                        {
                            var encoded = AdvancedVisemeAnimatorBuilder
                                .EncodeBetaProjectionRow(
                                    row.articulator, row.coefficients,
                                    out var offset, out var scale);
                            return new
                            {
                                row.articulator,
                                row.coefficients,
                                encoded,
                                offset,
                                scale
                            };
                        })
                        .ToArray();
                    Assert.That(projectedRows, Is.Not.Empty,
                        "The fixture must exercise the exact articulation-observer projection.");
                    var projectedVisemeStages = new[] { "Raw", "Fast", "Slow" };
                    var silenceStabilityName = parameterNames.Single(name =>
                        name.EndsWith("/Tuning/SilenceStability",
                            StringComparison.Ordinal));
                    Assert.That(projectedRows.All(row => projectedVisemeStages.All(stage =>
                            parameterNames.Contains(internalPrefix +
                                $"/BetaCoarticulation/Projected/{row.articulator}/{stage}"))),
                        Is.True);
                    var candidateName = internalPrefix +
                        "/PhonePosterior/Residual/CandidateMass";
                    var shareName = internalPrefix +
                        "/PhonePosterior/Model/MShareSlow";
                    var confidenceName = internalPrefix +
                        "/PhonePosterior/Confidence";
                    var mName = component.NormalizedPrefix +
                        "/Speech/Hypothesis/M";
                    var nName = component.NormalizedPrefix +
                        "/Speech/Hypothesis/N";
                    var outputConfidenceName = component.NormalizedPrefix +
                        "/Speech/Hypothesis/Confidence";
                    var trackingNames = parameterNames.Where(name =>
                            name.StartsWith("YUCP/TestFaceTracking/", StringComparison.Ordinal))
                        .ToArray();
                    var frames = new List<(
                        float candidate,
                        float share,
                        float confidence,
                        float m,
                        float n,
                        float outputConfidence)>();
                    var nasalChannels = new[]
                    {
                        (group: "TongueTip", stage: "Fast", share: "MShareFast"),
                        (group: "TongueTip", stage: "Slow", share: "MShareSlow"),
                        (group: "TongueBody", stage: "Fast", share: "MShareFast"),
                        (group: "TongueBody", stage: "Slow", share: "MShareSlow")
                    };
                    var nasalFrames = new List<(
                        float[] pp,
                        float[] nn,
                        float[] share,
                        float confidence,
                        float[] delta)>();
                    var projectedFrames = new List<(
                        int viseme,
                        float history,
                        float stability,
                        float[] raw,
                        float[] fast,
                        float[] slow,
                        float[] lead,
                        float[] corpusFast,
                        float[] corpusSlow)>();
                    var commonFrames = new List<(
                        int viseme,
                        float history,
                        float stability,
                        float meanLead,
                        float renderLead,
                        float[] raw,
                        float[] sparseFast,
                        float[] sparseSlow,
                        float[] visibleFast,
                        float[] phoneObservationFast,
                        float[] commonSlow,
                        float[] rendered)>();
                    for (var frame = 0; frame < 96; frame++)
                    {
                        var decodedViseme = frame % 11 < 2
                            ? 0
                            : 1 + (frame * 7 + frame / 9) % 14;
                        animator.SetInteger("Viseme", decodedViseme);
                        animator.SetFloat("Voice", 0.2f +
                            0.75f * Mathf.Abs(Mathf.Sin(frame * 0.17f)));
                        animator.SetFloat("IsLocal", 1f);
                        foreach (var trackingName in trackingNames)
                        {
                            var value = trackingName.EndsWith(
                                    "/LipTrackingActive", StringComparison.Ordinal)
                                ? 1f
                                : Mathf.Clamp01(0.45f + 0.42f *
                                    Mathf.Sin(frame * (0.09f +
                                        trackingName.Length * 0.0007f)));
                            animator.SetFloat(trackingName, value);
                        }
                        animator.Update(frame % 3 == 0 ? 1f / 15f :
                            frame % 3 == 1 ? 1f / 60f : 1f / 144f);
                        foreach (var row in projectedRows)
                        foreach (var stage in projectedVisemeStages)
                        {
                            var expectedProjection = 0f;
                            for (var viseme = 0;
                                 viseme < VisemeReconstructionProfile.VisemeCount;
                                 viseme++)
                            {
                                expectedProjection += row.encoded[viseme] *
                                    animator.GetFloat(internalPrefix +
                                        $"/Viseme/{viseme}/{stage}");
                            }
                            var actualProjection = animator.GetFloat(internalPrefix +
                                $"/BetaCoarticulation/Projected/{row.articulator}/{stage}");
                            Assert.That(actualProjection,
                                Is.EqualTo(expectedProjection).Within(2e-5f),
                                $"Projected {row.articulator}/{stage} diverged at frame {frame}.");
                        }
                        frames.Add((
                            animator.GetFloat(candidateName),
                            animator.GetFloat(shareName),
                            animator.GetFloat(confidenceName),
                            animator.GetFloat(mName),
                            animator.GetFloat(nName),
                            animator.GetFloat(outputConfidenceName)));
                        nasalFrames.Add((
                            nasalChannels.Select(channel => animator.GetFloat(
                                $"{internalPrefix}/BetaCoarticulation/{channel.group}" +
                                $"/Viseme/1/{channel.stage}")).ToArray(),
                            nasalChannels.Select(channel => animator.GetFloat(
                                $"{internalPrefix}/BetaCoarticulation/{channel.group}" +
                                $"/Viseme/8/{channel.stage}")).ToArray(),
                            nasalChannels.Select(channel => animator.GetFloat(
                                $"{internalPrefix}/PhonePosterior/Model/{channel.share}"))
                                .ToArray(),
                            animator.GetFloat(confidenceName),
                            nasalChannels.Select(channel => animator.GetFloat(
                                $"{internalPrefix}/PhonePosterior/{channel.group}" +
                                $"/{channel.stage}/Delta")).ToArray()));
                        projectedFrames.Add((
                            decodedViseme,
                            animator.GetFloat(internalPrefix +
                                "/Speech/Hangover/History"),
                            animator.GetFloat(silenceStabilityName),
                            projectedRows.Select(row => animator.GetFloat(
                                    internalPrefix +
                                    $"/BetaCoarticulation/Projected/{row.articulator}/Raw"))
                                .ToArray(),
                            projectedRows.Select(row => animator.GetFloat(
                                    internalPrefix +
                                    $"/BetaCoarticulation/Projected/{row.articulator}/Fast"))
                                .ToArray(),
                            projectedRows.Select(row => animator.GetFloat(
                                    internalPrefix +
                                    $"/BetaCoarticulation/Projected/{row.articulator}/Slow"))
                                .ToArray(),
                            projectedRows.Select(row => animator.GetFloat(
                                    internalPrefix + "/BetaCoarticulation/Lead/" +
                                    AdvancedVisemeCoarticulationModel.GroupFor(
                                        row.articulator)))
                                .ToArray(),
                            projectedRows.Select(row => animator.GetFloat(
                                    internalPrefix +
                                    $"/Articulation/{row.articulator}/CorpusFast"))
                                .ToArray(),
                            projectedRows.Select(row => animator.GetFloat(
                                    internalPrefix +
                                    $"/Articulation/{row.articulator}/CorpusSlow"))
                                .ToArray()));
                        commonFrames.Add((
                            decodedViseme,
                            animator.GetFloat(internalPrefix +
                                "/Speech/Hangover/History"),
                            animator.GetFloat(silenceStabilityName),
                            animator.GetFloat(internalPrefix +
                                "/BetaCoarticulation/Lead/Mean"),
                            animator.GetFloat(internalPrefix +
                                "/Speech/RenderLead"),
                            Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                                .Select(viseme => animator.GetFloat(internalPrefix +
                                    $"/Viseme/{viseme}/Raw")).ToArray(),
                            Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                                .Select(viseme => animator.GetFloat(internalPrefix +
                                    $"/Viseme/{viseme}/SparseFast")).ToArray(),
                            Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                                .Select(viseme => animator.GetFloat(internalPrefix +
                                    $"/Viseme/{viseme}/SparseSlow")).ToArray(),
                            Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                                .Select(viseme => animator.GetFloat(internalPrefix +
                                    $"/BetaCoarticulation/Mean/Viseme/{viseme}/Fast"))
                                .ToArray(),
                            Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                                .Select(viseme => animator.GetFloat(internalPrefix +
                                    $"/BetaCoarticulation/Mean/Viseme/{viseme}" +
                                    "/PhoneObservationFast")).ToArray(),
                            Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                                .Select(viseme => animator.GetFloat(internalPrefix +
                                    $"/BetaCoarticulation/Mean/Viseme/{viseme}/Slow"))
                                .ToArray(),
                            Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                                .Select(viseme => animator.GetFloat(
                                    AdvancedVisemeParameterContract.Viseme(
                                        component.NormalizedPrefix, viseme))).ToArray()));
                    }

                    var privateObservationDiffersFromVisible = false;
                    var maximumPrivateVisibleDifference = 0f;
                    var maximumMeanLead = 0f;
                    var maximumRawFastDifference = 0f;
                    var maximumRawStep = 0f;
                    var maximumSparseFastStep = 0f;
                    for (var frame = 4; frame < frames.Count; frame++)
                    {
                        var sampled = frames[frame - 1];
                        var actual = frames[frame];
                        var expectedM = sampled.confidence * sampled.share *
                            sampled.candidate;
                        var expectedN = sampled.confidence * (1f - sampled.share) *
                            sampled.candidate;
                        Assert.That(actual.m, Is.GreaterThanOrEqualTo(-1e-6f));
                        Assert.That(actual.n, Is.GreaterThanOrEqualTo(-1e-6f));
                        Assert.That(actual.outputConfidence,
                            Is.GreaterThanOrEqualTo(-1e-6f));
                        Assert.That(actual.m,
                            Is.EqualTo(expectedM).Within(2e-5f));
                        Assert.That(actual.n,
                            Is.EqualTo(expectedN).Within(2e-5f));
                        Assert.That(actual.outputConfidence,
                            Is.EqualTo(sampled.confidence).Within(2e-5f));
                        Assert.That(actual.m + actual.n,
                            Is.EqualTo(sampled.confidence * sampled.candidate)
                                .Within(3e-5f),
                            "The phase-coherent M/N output must conserve candidate mass.");

                        var sampledNasal = nasalFrames[frame - 1];
                        var actualNasal = nasalFrames[frame];
                        for (var channel = 0;
                             channel < nasalChannels.Length;
                             channel++)
                        {
                            var expectedDelta = sampledNasal.confidence *
                                (sampledNasal.share[channel] *
                                 (sampledNasal.pp[channel] + sampledNasal.nn[channel]) -
                                 sampledNasal.pp[channel]);
                            Assert.That(actualNasal.delta[channel],
                                Is.EqualTo(expectedDelta).Within(2e-5f),
                                $"Nasal correction mixed Animator frames at {frame}.");
                            Assert.That(actualNasal.delta[channel],
                                Is.GreaterThanOrEqualTo(
                                    -sampledNasal.confidence * sampledNasal.pp[channel] -
                                    2e-5f));
                            Assert.That(actualNasal.delta[channel],
                                Is.LessThanOrEqualTo(
                                    sampledNasal.confidence * sampledNasal.nn[channel] +
                                2e-5f));
                        }

                        var sampledProjected = projectedFrames[frame - 1];
                        var actualProjected = projectedFrames[frame];
                        for (var row = 0; row < projectedRows.Length; row++)
                        {
                            var releaseFast = projectedRows[row].offset +
                                projectedRows[row].scale * sampledProjected.fast[row];
                            var expectedFast = releaseFast;
                            // Viseme/Index is decoded by the preceding Animator
                            // layer, so this Math-layer selector intentionally
                            // observes the previous frame's decoded index.
                            if (sampledProjected.viseme == 0)
                            {
                                var historyBlend = Mathf.InverseLerp(
                                    AdvancedVisemeMath.SpeechHistoryHoldStart,
                                    AdvancedVisemeMath.SpeechHistoryHoldFull,
                                    sampledProjected.history);
                                var heldFast = Mathf.LerpUnclamped(
                                    releaseFast,
                                    sampledProjected.corpusFast[row],
                                    historyBlend);
                                var holdStrength = Mathf.Clamp01(
                                    sampledProjected.stability / 0.5f);
                                expectedFast = Mathf.LerpUnclamped(
                                    releaseFast, heldFast, holdStrength);
                            }
                            var expectedSlow = projectedRows[row].offset +
                                projectedRows[row].scale * Mathf.LerpUnclamped(
                                    sampledProjected.slow[row],
                                    sampledProjected.fast[row],
                                    sampledProjected.lead[row]);
                            Assert.That(actualProjected.corpusFast[row],
                                Is.EqualTo(expectedFast).Within(3e-5f),
                                $"Projected {projectedRows[row].articulator} fast output " +
                                $"changed Animator stage at frame {frame}. " +
                                $"viseme={actualProjected.viseme}, previousViseme=" +
                                $"{sampledProjected.viseme}, history={sampledProjected.history}, " +
                                $"stability={sampledProjected.stability}, release={releaseFast}, " +
                                $"previous={sampledProjected.corpusFast[row]}");
                            Assert.That(actualProjected.corpusSlow[row],
                                Is.EqualTo(expectedSlow).Within(3e-5f),
                                $"Projected {projectedRows[row].articulator} slow output " +
                                $"changed Animator stage at frame {frame}.");
                        }

                        var sampledCommon = commonFrames[frame - 1];
                        var actualCommon = commonFrames[frame];
                        for (var viseme = 0;
                             viseme < VisemeReconstructionProfile.VisemeCount;
                             viseme++)
                        {
                            var visibleRelease = sampledCommon.sparseFast[viseme];
                            var expectedVisibleFast = visibleRelease;
                            var phoneRelease = Mathf.LerpUnclamped(
                                sampledCommon.sparseFast[viseme],
                                sampledCommon.raw[viseme],
                                sampledCommon.meanLead);
                            var expectedPhoneObservation = phoneRelease;
                            if (sampledCommon.viseme == 0)
                            {
                                var historyBlend = Mathf.InverseLerp(
                                    AdvancedVisemeMath.SpeechHistoryHoldStart,
                                    AdvancedVisemeMath.SpeechHistoryHoldFull,
                                    sampledCommon.history);
                                var holdStrength = Mathf.Clamp01(
                                    sampledCommon.stability / 0.5f);
                                expectedVisibleFast = Mathf.LerpUnclamped(
                                    visibleRelease,
                                    Mathf.LerpUnclamped(
                                        visibleRelease,
                                        sampledCommon.visibleFast[viseme],
                                        historyBlend),
                                    holdStrength);
                                expectedPhoneObservation = Mathf.LerpUnclamped(
                                    phoneRelease,
                                    Mathf.LerpUnclamped(
                                        phoneRelease,
                                        sampledCommon.phoneObservationFast[viseme],
                                        historyBlend),
                                    holdStrength);
                            }
                            var expectedCommonSlow =
                                sampledCommon.sparseSlow[viseme];
                            var expectedRendered = Mathf.LerpUnclamped(
                                sampledCommon.commonSlow[viseme],
                                sampledCommon.visibleFast[viseme],
                                sampledCommon.renderLead);
                            Assert.That(actualCommon.visibleFast[viseme],
                                Is.EqualTo(expectedVisibleFast).Within(3e-5f),
                                $"Visible Beta fast lane used a raw endpoint at frame " +
                                $"{frame}, viseme {viseme}.");
                            Assert.That(actualCommon.phoneObservationFast[viseme],
                                Is.EqualTo(expectedPhoneObservation).Within(3e-5f),
                                $"Private phone observation lost training parity at " +
                                $"frame {frame}, viseme {viseme}.");
                            Assert.That(actualCommon.commonSlow[viseme],
                                Is.EqualTo(expectedCommonSlow).Within(3e-5f));
                            Assert.That(actualCommon.rendered[viseme],
                                Is.EqualTo(expectedRendered).Within(3e-5f),
                                $"Public viseme rendering consumed the private phone " +
                                $"observation at frame {frame}, viseme {viseme}.");
                            var privateVisibleDifference = Mathf.Abs(
                                sampledCommon.phoneObservationFast[viseme] -
                                sampledCommon.visibleFast[viseme]);
                            maximumPrivateVisibleDifference = Mathf.Max(
                                maximumPrivateVisibleDifference,
                                privateVisibleDifference);
                            maximumMeanLead = Mathf.Max(
                                maximumMeanLead,
                                Mathf.Abs(sampledCommon.meanLead));
                            maximumRawFastDifference = Mathf.Max(
                                maximumRawFastDifference,
                                Mathf.Abs(sampledCommon.raw[viseme] -
                                          sampledCommon.sparseFast[viseme]));
                            maximumRawStep = Mathf.Max(
                                maximumRawStep,
                                Mathf.Abs(actualCommon.raw[viseme] -
                                          sampledCommon.raw[viseme]));
                            maximumSparseFastStep = Mathf.Max(
                                maximumSparseFastStep,
                                Mathf.Abs(actualCommon.sparseFast[viseme] -
                                          sampledCommon.sparseFast[viseme]));
                            privateObservationDiffersFromVisible |=
                                privateVisibleDifference > 1e-4f;
                        }
                    }
                    var allRawParametersPresent = Enumerable.Range(
                            0, VisemeReconstructionProfile.VisemeCount)
                        .All(viseme => parameterNames.Contains(
                            internalPrefix + $"/Viseme/{viseme}/Raw"));
                    var allSparseFastParametersPresent = Enumerable.Range(
                            0, VisemeReconstructionProfile.VisemeCount)
                        .All(viseme => parameterNames.Contains(
                            internalPrefix + $"/Viseme/{viseme}/SparseFast"));
                    Assert.That(privateObservationDiffersFromVisible, Is.True,
                        "The runtime trace must distinguish the visible and private " +
                        "fast simplexes or it cannot detect accidental routing. " +
                        $"maxPrivateVisible={maximumPrivateVisibleDifference:R}, " +
                        $"maxMeanLead={maximumMeanLead:R}, " +
                        $"maxRawFast={maximumRawFastDifference:R}, " +
                        $"maxRawStep={maximumRawStep:R}, " +
                        $"maxSparseFastStep={maximumSparseFastStep:R}, " +
                        $"rawPresent={allRawParametersPresent}, " +
                        $"sparseFastPresent={allSparseFastParametersPresent}.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(runtimeRoot);
                }
            }
            finally
            {
                AdvancedVisemeAnimatorGraphOptimizer
                    .SkipCongruenceInterningForStructureTests =
                    previousSkipCongruence;
                AssetDatabase.DeleteAsset(folder);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BlendTreeMissingAnimatorBindingsContributeNeutralZero()
        {
            var folderName = "__YUCP_AVR_ZeroWeight_" + Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            var runtimeRoot = new GameObject("Direct Zero Weight Runtime");
            try
            {
                var controller = AnimatorController.CreateAnimatorControllerAtPath(
                    folder + "/ZeroWeight.controller");
                controller.AddParameter("Weight", AnimatorControllerParameterType.Float);
                controller.AddParameter("Output", AnimatorControllerParameterType.Float);
                controller.AddParameter("Branch", AnimatorControllerParameterType.Float);
                controller.AddParameter("BranchOutput", AnimatorControllerParameterType.Float);
                var clip = new AnimationClip { name = "One" };
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve("", typeof(Animator), "Output"),
                    AnimationCurve.Constant(0f, 0f, 1f));
                AssetDatabase.AddObjectToAsset(clip, controller);
                var tree = new BlendTree
                {
                    name = "Unnormalized Direct",
                    blendType = BlendTreeType.Direct,
                    useAutomaticThresholds = false,
                    children = new[]
                    {
                        new ChildMotion
                        {
                            motion = clip,
                            directBlendParameter = "Weight",
                            timeScale = 1f
                        }
                    }
                };
                AssetDatabase.AddObjectToAsset(tree, controller);
                var serialized = new SerializedObject(tree);
                serialized.FindProperty("m_NormalizedBlendValues").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var state = controller.layers[0].stateMachine.AddState("Direct");
                state.motion = tree;
                controller.layers[0].stateMachine.defaultState = state;

                controller.AddLayer("Missing Branch Binding");
                var branchLayer = controller.layers[1];
                branchLayer.defaultWeight = 1f;
                var branchOne = new AnimationClip { name = "Branch One" };
                AnimationUtility.SetEditorCurve(branchOne,
                    EditorCurveBinding.FloatCurve("", typeof(Animator), "BranchOutput"),
                    AnimationCurve.Constant(0f, 0f, 1f));
                var branchEmpty = new AnimationClip { name = "Branch Empty" };
                AssetDatabase.AddObjectToAsset(branchOne, controller);
                AssetDatabase.AddObjectToAsset(branchEmpty, controller);
                var branchTree = new BlendTree
                {
                    name = "Missing branch binding",
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = "Branch",
                    useAutomaticThresholds = false,
                    children = new[]
                    {
                        new ChildMotion { motion = branchOne, threshold = 0f, timeScale = 1f },
                        new ChildMotion { motion = branchEmpty, threshold = 1f, timeScale = 1f }
                    }
                };
                AssetDatabase.AddObjectToAsset(branchTree, controller);
                var branchState = branchLayer.stateMachine.AddState("Branch");
                branchState.motion = branchTree;
                branchLayer.stateMachine.defaultState = branchState;
                var layers = controller.layers;
                layers[1] = branchLayer;
                controller.layers = layers;
                AssetDatabase.SaveAssets();

                var animator = runtimeRoot.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.Update(0f);
                var initial = animator.GetFloat("Output");
                animator.SetFloat("Weight", 1f);
                animator.Update(1f / 60f);
                var weighted = animator.GetFloat("Output");
                animator.SetFloat("Weight", 0f);
                animator.Update(1f / 60f);
                var zero = animator.GetFloat("Output");
                animator.SetFloat("Weight", 0.25f);
                animator.Update(1f / 60f);
                var quarter = animator.GetFloat("Output");
                animator.SetFloat("Branch", 0f);
                animator.Update(1f / 60f);
                var branchBound = animator.GetFloat("BranchOutput");
                animator.SetFloat("Branch", 1f);
                animator.Update(1f / 60f);
                var branchMissing = animator.GetFloat("BranchOutput");
                Assert.That(initial, Is.EqualTo(0f).Within(1e-6f));
                Assert.That(weighted, Is.EqualTo(1f).Within(1e-6f));
                Assert.That(zero, Is.EqualTo(0f).Within(1e-6f),
                    "A zero-weight Direct child must contribute the property's neutral value.");
                Assert.That(quarter, Is.EqualTo(0.25f).Within(1e-6f));
                Assert.That(branchBound, Is.EqualTo(1f).Within(1e-6f));
                Assert.That(branchMissing, Is.EqualTo(0f).Within(1e-6f),
                    "An unbound Simple1D branch must contribute the property's neutral value.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(runtimeRoot);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void ApertureOnlyTailoredTemplateStillBuildsHiddenPhonePosterior()
        {
            var root = new GameObject("Aperture Template Posterior Test");
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            var folderName = "__YUCP_AVR_ApertureTest_" + Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            try
            {
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.reconstructionMode = AdvancedVisemeReconstructionMode.BetaCoarticulation;
                component.mouthOwnership = AdvancedVisemeMouthOwnership.OutputsOnly;
                component.trackingInputs = AdvancedVisemeTrackingInputs.Auto;
                var reused = new System.Collections.Generic.Dictionary<AdvancedVisemeArticulator, string>
                {
                    [AdvancedVisemeArticulator.JawOpen] = "Tailored/v2/JawOpen",
                    [AdvancedVisemeArticulator.LipClose] = "Tailored/v2/MouthClosed",
                    [AdvancedVisemeArticulator.MouthOpen] = "Tailored/v2/MouthOpen"
                };

                var result = AdvancedVisemeAnimatorBuilder.Build(
                    new AdvancedVisemeAnimatorBuilder.Request
                    {
                        controllerPath = folder + "/AdvancedViseme.controller",
                        parametersPath = folder + "/TrackingParameters.asset",
                        component = component,
                        profile = profile,
                        trackingPrefix = "Tailored",
                        effectiveTrackingInputs = AdvancedVisemeTrackingInputs.Balanced8,
                        reuseExistingTracking = true,
                        trackingActiveParameter = "Tailored/LipTrackingActive",
                        trackingActiveAnimatorType = AnimatorControllerParameterType.Float,
                        trackingActiveDefault = 1f,
                        trackingParameterNames = reused,
                        sourceVisemeBlendShapes = new string[VisemeReconstructionProfile.VisemeCount],
                        calibrationBasis = Array.Empty<AdvancedVisemeMeshCalibrator.BasisInput>(),
                        resolvedBlendShapes = new System.Collections.Generic.Dictionary<AdvancedVisemeArticulator, string>(),
                        externalPoses = new System.Collections.Generic.Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose>(),
                        trackingEnabled = true,
                        existingExpressionParameters = new System.Collections.Generic.HashSet<string>(
                            reused.Values.Concat(new[] { "Tailored/LipTrackingActive" }))
                    });

                var parameterNames = result.controller.parameters.Select(parameter => parameter.name).ToArray();
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/PhonePosterior/Model/MShareSlow")), Is.True,
                    "Jaw plus opposed lip aperture should select the six-feature Aperture model.");
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/TongueInference/Model/TongueOut/Stable")), Is.False,
                    "The separate protrusion-dependent tongue residual must abstain without fabricating missing channels.");
                Assert.That(result.globalParameters, Does.Contain(
                    component.NormalizedPrefix + "/Speech/Hypothesis/Confidence"));
                Assert.That(result.parameters.parameters, Is.Not.Empty,
                    "The optional tuning menu should still be available on a tailored rig.");
                var tuningSources = result.parameters.parameters.Where(parameter =>
                    parameter.name.StartsWith(
                        component.NormalizedPrefix + "/Tuning/",
                        StringComparison.Ordinal)).ToArray();
                Assert.That(tuningSources, Is.Not.Empty);
                Assert.That(tuningSources.All(parameter =>
                        parameter.valueType == VRCExpressionParameters.ValueType.Float &&
                        !parameter.networkSynced), Is.True,
                    "Full-precision saved tuning sources must remain local.");
                var synced = result.parameters.parameters
                    .Where(parameter => parameter.networkSynced)
                    .ToArray();
                Assert.That(synced.Select(parameter => parameter.name),
                    Is.EquivalentTo(new[]
                    {
                        AdvancedVisemeTuning.CompactSyncDataParameter(
                            component.NormalizedPrefix)
                    }.Concat(Enumerable.Range(0, 5).Select(bit =>
                        AdvancedVisemeTuning.CompactSyncIndexParameter(
                            component.NormalizedPrefix, bit)))));
                Assert.That(synced.Any(parameter =>
                    parameter.name.Contains("/v2/", StringComparison.Ordinal)), Is.False,
                    "Reusing a tailored template must not create another synced face-tracking input stream.");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CalibratedBetaBuildDrivesOnlyTheSignedHiddenPhoneResidual()
        {
            var root = new GameObject("Hidden Phone Residual Graph Test");
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            var source = CreateCalibrationMesh();
            var folderName = "__YUCP_AVR_HiddenResidualTest_" + Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            AdvancedVisemeMeshCalibrator.Result calibration = null;
            try
            {
                var visemeIndices = Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                    .Select(index => source.GetBlendShapeIndex(
                        "vrc.v_" + VisemeReconstructionProfile.VisemeNames[index]))
                    .ToArray();
                var basis = new[]
                {
                    new AdvancedVisemeMeshCalibrator.BasisInput(
                        AdvancedVisemeArticulator.JawOpen,
                        source.GetBlendShapeIndex("JawOpen")),
                    new AdvancedVisemeMeshCalibrator.BasisInput(
                        AdvancedVisemeArticulator.LipPucker,
                        source.GetBlendShapeIndex("LipPucker"))
                };
                calibration = AdvancedVisemeMeshCalibrator.Build(source, visemeIndices, basis);
                Assert.That(calibration.success, Is.True, calibration.error);
                Assert.That(calibration.hiddenPhoneResidualBlendShapeName,
                    Is.Not.Null.And.Not.Empty);

                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.reconstructionMode = AdvancedVisemeReconstructionMode.BetaCoarticulation;
                component.mouthOwnership = AdvancedVisemeMouthOwnership.DriveLowerFace;
                component.trackingInputs = AdvancedVisemeTrackingInputs.Balanced8;
                var controllerPath = folder + "/AdvancedViseme.controller";
                var result = AdvancedVisemeAnimatorBuilder.Build(
                    new AdvancedVisemeAnimatorBuilder.Request
                    {
                        controllerPath = controllerPath,
                        parametersPath = folder + "/TrackingParameters.asset",
                        rendererPath = "Face",
                        component = component,
                        profile = profile,
                        trackingPrefix = "YUCP/TestFaceTracking",
                        effectiveTrackingInputs = AdvancedVisemeTrackingInputs.Balanced8,
                        reuseExistingTracking = false,
                        trackingActiveParameter = "YUCP/TestFaceTracking/LipTrackingActive",
                        trackingActiveAnimatorType = AnimatorControllerParameterType.Float,
                        trackingActiveDefault = 1f,
                        trackingParameterNames = new System.Collections.Generic.Dictionary<AdvancedVisemeArticulator, string>(),
                        sourceVisemeBlendShapes = Enumerable.Range(
                                0, VisemeReconstructionProfile.VisemeCount)
                            .Select(index => "vrc.v_" + VisemeReconstructionProfile.VisemeNames[index])
                            .ToArray(),
                        calibration = calibration,
                        calibrationBasis = basis,
                        resolvedBlendShapes = new System.Collections.Generic.Dictionary<AdvancedVisemeArticulator, string>
                        {
                            [AdvancedVisemeArticulator.JawOpen] = "JawOpen",
                            [AdvancedVisemeArticulator.LipPucker] = "LipPucker"
                        },
                        externalPoses = new System.Collections.Generic.Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose>(),
                        targetMesh = source,
                        trackingEnabled = true,
                        existingExpressionParameters = new System.Collections.Generic.HashSet<string>()
                    });

                var internalParameters = result.controller.parameters
                    .Select(parameter => parameter.name).ToArray();
                Assert.That(internalParameters.Any(name => name.Contains(
                    "/PhonePosterior/Residual/RetainedSpeechDelta")), Is.False,
                    "The complement-space hidden residual must not be erased by visible contradiction retention.");
                Assert.That(result.parameters.parameters.Any(parameter =>
                    parameter.name.Contains("PhonePosterior")), Is.False,
                    "The build-only hidden residual must not add a synced input.");
                var hiddenProperty = "blendShape." +
                                     calibration.hiddenPhoneResidualBlendShapeName;
                var hiddenNegativeProperty = "blendShape." +
                    calibration.hiddenPhoneResidualNegativeBlendShapeName;
                var hiddenClips = AssetDatabase.LoadAllAssetsAtPath(controllerPath)
                    .OfType<AnimationClip>()
                    .SelectMany(clip => AnimationUtility.GetCurveBindings(clip)
                        .Where(binding => binding.propertyName == hiddenProperty ||
                                          binding.propertyName == hiddenNegativeProperty)
                        .Select(binding => new
                        {
                            clip,
                            binding
                        }))
                    .ToArray();
                Assert.That(hiddenClips.Any(item =>
                    item.binding.propertyName == hiddenProperty), Is.True);
                Assert.That(hiddenClips.Any(item =>
                    item.binding.propertyName == hiddenNegativeProperty), Is.True);
                foreach (var item in hiddenClips)
                foreach (var key in AnimationUtility.GetEditorCurve(
                             item.clip, item.binding).keys)
                    Assert.That(key.value, Is.InRange(0f, 100f),
                        "Signed hidden-phone transfer must use paired geometry, not a negative final weight.");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                if (calibration != null && calibration.mesh != null)
                    UnityEngine.Object.DestroyImmediate(calibration.mesh);
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FallbackCorrectionAlgebraMakesTheLaterReuseLayerAuthoritative()
        {
            var random = new System.Random(91371);
            for (var i = 0; i < 128; i++)
            {
                var fadedSpeechBase = Mathf.Lerp(-1f, 1f, (float)random.NextDouble());
                var generatedTracking = Mathf.Lerp(-1f, 1f, (float)random.NextDouble());
                var rawExternalTracking = Mathf.Lerp(-1f, 1f, (float)random.NextDouble());
                var final = Mathf.Lerp(-1f, 1f, (float)random.NextDouble());

                var freshCorrection = final - fadedSpeechBase -
                                      (AdvancedVisemeAnimatorBuilder
                                           .ShouldSubtractGeneratedTrackingContribution(false)
                                          ? generatedTracking
                                          : 0f);
                var freshTotal = fadedSpeechBase + generatedTracking + freshCorrection;
                Assert.That(freshTotal, Is.EqualTo(final).Within(1e-6f));

                var reusedCorrection = final - fadedSpeechBase -
                                       (AdvancedVisemeAnimatorBuilder
                                            .ShouldSubtractGeneratedTrackingContribution(true)
                                           ? generatedTracking
                                           : 0f);
                var reusedOverrideLayer = fadedSpeechBase + reusedCorrection;
                Assert.That(reusedOverrideLayer, Is.EqualTo(final).Within(1e-6f),
                    "The later Override layer must publish the complete final pose, not a negative cancellation.");
                Assert.That(rawExternalTracking + reusedOverrideLayer - final,
                    Is.EqualTo(rawExternalTracking).Within(1e-6f),
                    "Adding the lower layer would reintroduce its raw value; Override semantics replace it.");
            }

            AdvancedVisemeMath.SignedRayCorrection(
                -0.5f, 0.1f, 0f, out var positiveRay, out var negativeRay);
            Assert.That(positiveRay, Is.EqualTo(-0.1f).Within(1e-6f),
                "Crossing from Smile to Sad must explicitly cancel the faded smile ray.");
            Assert.That(negativeRay, Is.EqualTo(0.5f).Within(1e-6f),
                "The distinct sad ray must receive the requested final magnitude only.");
            Assert.That(0.1f + positiveRay, Is.Zero.Within(1e-6f));
        }

        [Test]
        public void OneSidedSignedTemplateTreatsTheUnsupportedDirectionAsNeutral()
        {
            var positive = new AnimationClip { name = "One-sided Smile" };
            var negative = new AnimationClip { name = "One-sided Sad" };
            try
            {
                var external = new AdvancedVisemeExternalPose
                {
                    positive = positive,
                    positiveThreshold = 1f,
                    negative = null
                };
                var points = AdvancedVisemeAnimatorBuilder.ExternalPoseNormalizationPoints(
                    AdvancedVisemeArticulator.SmileSad, external);
                Assert.That(points, Is.Not.Null);
                Assert.That(points[0].input, Is.EqualTo(-1f));
                Assert.That(points[0].output, Is.Zero,
                    "A positive-only template is neutral, not inverse-smile, for negative input.");
                Assert.That(points.Any(point =>
                    Mathf.Approximately(point.input, 1f) && Mathf.Approximately(point.output, 1f)),
                    Is.True);

                external.positive = null;
                external.negative = negative;
                external.negativeThreshold = -0.75f;
                points = AdvancedVisemeAnimatorBuilder.ExternalPoseNormalizationPoints(
                    AdvancedVisemeArticulator.SmileSad, external);
                Assert.That(points, Is.Not.Null);
                Assert.That(points.Any(point =>
                    Mathf.Approximately(point.input, -0.75f) &&
                    Mathf.Approximately(point.output, -1f)), Is.True);
                Assert.That(points[points.Length - 1].input, Is.EqualTo(1f));
                Assert.That(points[points.Length - 1].output, Is.Zero,
                    "A negative-only template is neutral for positive input.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(positive);
                UnityEngine.Object.DestroyImmediate(negative);
            }
        }

        [Test]
        public void CorrectionBasisAcceptsOnlyTargetRendererBlendshapeCurves()
        {
            var mesh = new Mesh { name = "Correction Basis Mesh" };
            var materialAsset = new Material(Shader.Find("Standard"));
            var validClip = new AnimationClip { name = "Valid target pose" };
            var mixedBoneClip = new AnimationClip { name = "Mixed target and bone pose" };
            var otherRendererClip = new AnimationClip { name = "Other renderer pose" };
            var materialClip = new AnimationClip { name = "Mixed material pose" };
            var objectReferenceClip = new AnimationClip { name = "Object-reference pose" };
            try
            {
                mesh.vertices = new[] { Vector3.zero };
                var zero = new[] { Vector3.zero };
                mesh.AddBlendShapeFrame("JawOpen", 100f, new[] { Vector3.right }, zero, zero);

                var blendshape = EditorCurveBinding.FloatCurve(
                    "Face", typeof(SkinnedMeshRenderer), "blendShape.JawOpen");
                var wrongShape = EditorCurveBinding.FloatCurve(
                    "Face", typeof(SkinnedMeshRenderer), "blendShape.DoesNotExist");
                var wrongRenderer = EditorCurveBinding.FloatCurve(
                    "OtherFace", typeof(SkinnedMeshRenderer), "blendShape.JawOpen");
                var bone = EditorCurveBinding.FloatCurve(
                    "Face/Jaw", typeof(Transform), "m_LocalPosition.x");
                var materialBinding = EditorCurveBinding.FloatCurve(
                    "Face", typeof(SkinnedMeshRenderer), "material._Glossiness");

                Assert.That(AdvancedVisemeAnimatorBuilder.IsLinearCorrectionCurve(
                    blendshape, "Face", mesh), Is.True);
                Assert.That(AdvancedVisemeAnimatorBuilder.IsLinearCorrectionCurve(
                    wrongShape, "Face", mesh), Is.False);
                Assert.That(AdvancedVisemeAnimatorBuilder.IsLinearCorrectionCurve(
                    wrongRenderer, "Face", mesh), Is.False);
                Assert.That(AdvancedVisemeAnimatorBuilder.IsLinearCorrectionCurve(
                    bone, "Face", mesh), Is.False);
                Assert.That(AdvancedVisemeAnimatorBuilder.IsLinearCorrectionCurve(
                    materialBinding, "Face", mesh), Is.False);

                var poseCurve = AnimationCurve.Constant(0f, 1f / 60f, 100f);
                AnimationUtility.SetEditorCurve(validClip, blendshape, poseCurve);
                AnimationUtility.SetEditorCurve(mixedBoneClip, blendshape, poseCurve);
                AnimationUtility.SetEditorCurve(mixedBoneClip, bone,
                    AnimationCurve.Constant(0f, 1f / 60f, 0.01f));
                AnimationUtility.SetEditorCurve(otherRendererClip, wrongRenderer, poseCurve);
                AnimationUtility.SetEditorCurve(materialClip, blendshape, poseCurve);
                AnimationUtility.SetEditorCurve(materialClip, materialBinding,
                    AnimationCurve.Constant(0f, 1f / 60f, 0.5f));
                AnimationUtility.SetEditorCurve(objectReferenceClip, blendshape, poseCurve);
                AnimationUtility.SetObjectReferenceCurve(objectReferenceClip,
                    EditorCurveBinding.PPtrCurve("Face", typeof(SkinnedMeshRenderer),
                        "m_Materials.Array.data[0]"),
                    new[] { new ObjectReferenceKeyframe { time = 0f, value = materialAsset } });

                Assert.That(AdvancedVisemeAnimatorBuilder.IsEntireLinearCorrectionClip(
                    validClip, "Face", mesh), Is.True);
                Assert.That(AdvancedVisemeAnimatorBuilder.IsEntireLinearCorrectionClip(
                    mixedBoneClip, "Face", mesh), Is.False);
                Assert.That(AdvancedVisemeAnimatorBuilder.IsEntireLinearCorrectionClip(
                    otherRendererClip, "Face", mesh), Is.False);
                Assert.That(AdvancedVisemeAnimatorBuilder.IsEntireLinearCorrectionClip(
                    materialClip, "Face", mesh), Is.False);
                Assert.That(AdvancedVisemeAnimatorBuilder.IsEntireLinearCorrectionClip(
                    objectReferenceClip, "Face", mesh), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(validClip);
                UnityEngine.Object.DestroyImmediate(mixedBoneClip);
                UnityEngine.Object.DestroyImmediate(otherRendererClip);
                UnityEngine.Object.DestroyImmediate(materialClip);
                UnityEngine.Object.DestroyImmediate(objectReferenceClip);
                UnityEngine.Object.DestroyImmediate(materialAsset);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TrackingCatalogOnlyExtractsStaticLinearSeparableMappings()
        {
            const string parameter = "FT/v2/JawOpen";
            var neutral = new AnimationClip { name = "Neutral" };
            var pose = new AnimationClip { name = "JawOpen" };
            var overlappingPose = new AnimationClip { name = "Overlapping JawOpen" };
            var bonePose = new AnimationClip { name = "Bone pose" };
            var unit = new BlendTree
            {
                name = "Unit", blendType = BlendTreeType.Simple1D,
                blendParameter = parameter, useAutomaticThresholds = false
            };
            var nonUnit = new BlendTree
            {
                name = "Non-unit", blendType = BlendTreeType.Simple1D,
                blendParameter = parameter, useAutomaticThresholds = false
            };
            var twoDimensional = new BlendTree
            {
                name = "2D", blendType = BlendTreeType.FreeformCartesian2D,
                blendParameter = parameter, blendParameterY = "FT/v2/MouthOpen"
            };
            var nested = new BlendTree
            {
                name = "Nested", blendType = BlendTreeType.Simple1D,
                blendParameter = parameter, useAutomaticThresholds = false
            };
            var direct = new BlendTree { name = "Direct", blendType = BlendTreeType.Direct };
            var normalizedDirect = new BlendTree
            {
                name = "Normalized direct", blendType = BlendTreeType.Direct
            };
            var unsafeDirect = new BlendTree
            {
                name = "Unsafe direct", blendType = BlendTreeType.Direct
            };
            var overlappingDirect = new BlendTree
            {
                name = "Overlapping direct", blendType = BlendTreeType.Direct
            };
            try
            {
                var jawBinding = EditorCurveBinding.FloatCurve(
                    "Face", typeof(SkinnedMeshRenderer), "blendShape.JawOpen");
                AnimationUtility.SetEditorCurve(pose, jawBinding,
                    AnimationCurve.Constant(0f, 1f / 60f, 100f));
                AnimationUtility.SetEditorCurve(overlappingPose, jawBinding,
                    AnimationCurve.Constant(0f, 1f / 60f, 25f));
                AnimationUtility.SetEditorCurve(bonePose,
                    EditorCurveBinding.FloatCurve("Armature/Jaw", typeof(Transform), "m_LocalPosition.x"),
                    AnimationCurve.Constant(0f, 1f / 60f, 0.01f));

                unit.children = new[]
                {
                    new ChildMotion { motion = neutral, threshold = 0f, timeScale = 1f },
                    new ChildMotion { motion = pose, threshold = 1f, timeScale = 1f }
                };
                var extracted = AdvancedVisemeTrackingCatalog.PoseFromTree(unit, parameter);
                Assert.That(extracted, Is.Not.Null);
                Assert.That(extracted.positive, Is.SameAs(pose));

                nonUnit.children = new[]
                {
                    new ChildMotion { motion = neutral, threshold = 0f, timeScale = 1f },
                    new ChildMotion { motion = pose, threshold = 0.75f, timeScale = 1f }
                };
                var scaled = AdvancedVisemeTrackingCatalog.PoseFromTree(nonUnit, parameter);
                Assert.That(scaled, Is.Not.Null,
                    "A single non-unit threshold is still an exactly invertible linear coordinate.");
                Assert.That(scaled.positiveThreshold, Is.EqualTo(0.75f).Within(1e-6f));

                twoDimensional.children = new[]
                {
                    new ChildMotion { motion = neutral, position = Vector2.zero, timeScale = 1f },
                    new ChildMotion { motion = pose, position = Vector2.right, timeScale = 1f }
                };
                Assert.That(AdvancedVisemeTrackingCatalog.PoseFromTree(twoDimensional, parameter), Is.Null);

                nested.children = new[]
                {
                    new ChildMotion { motion = neutral, threshold = 0f, timeScale = 1f },
                    new ChildMotion { motion = direct, threshold = 1f, timeScale = 1f }
                };
                Assert.That(AdvancedVisemeTrackingCatalog.PoseFromTree(nested, parameter), Is.Null);

                direct.children = new[]
                {
                    new ChildMotion { motion = pose, directBlendParameter = parameter, timeScale = 1f },
                    new ChildMotion { motion = neutral, directBlendParameter = "YUCP/One", timeScale = 1f }
                };
                Assert.That(AdvancedVisemeTrackingCatalog.PoseFromTree(direct, parameter)?.positive,
                    Is.SameAs(pose));

                normalizedDirect.children = direct.children;
                var serializedDirect = new SerializedObject(normalizedDirect);
                serializedDirect.FindProperty("m_NormalizedBlendValues").boolValue = true;
                serializedDirect.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(AdvancedVisemeTrackingCatalog.PoseFromTree(normalizedDirect, parameter), Is.Null);

                unsafeDirect.children = new[]
                {
                    new ChildMotion { motion = pose, directBlendParameter = parameter, timeScale = 1f },
                    new ChildMotion
                    {
                        motion = bonePose, directBlendParameter = "FT/v2/JawX", timeScale = 1f
                    }
                };
                Assert.That(AdvancedVisemeTrackingCatalog.PoseFromTree(unsafeDirect, parameter), Is.Null,
                    "A separable-looking child must not hide unsafe sibling curves in the sampled tree.");

                overlappingDirect.children = new[]
                {
                    new ChildMotion { motion = pose, directBlendParameter = parameter, timeScale = 1f },
                    new ChildMotion
                    {
                        motion = overlappingPose, directBlendParameter = "Unrecognized/ExtraJaw",
                        timeScale = 1f
                    }
                };
                Assert.That(AdvancedVisemeTrackingCatalog.PoseFromTree(overlappingDirect, parameter), Is.Null,
                    "A later Override cannot preserve an unreconstructed sibling contribution to the same curve.");
            }
            finally
            {
                foreach (var instance in new UnityEngine.Object[]
                         {
                             neutral, pose, overlappingPose, bonePose, unit, nonUnit, twoDimensional, nested,
                             direct, normalizedDirect, unsafeDirect, overlappingDirect
                         })
                    UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void CompleteVisiblePoseOwnershipRequiresEveryVisibleAxisButNoTongueAxis()
        {
            var visible = new[]
            {
                AdvancedVisemeArticulator.JawOpen,
                AdvancedVisemeArticulator.LipClose,
                AdvancedVisemeArticulator.MouthOpen,
                AdvancedVisemeArticulator.LipFunnel,
                AdvancedVisemeArticulator.LipPucker,
                AdvancedVisemeArticulator.LipSuck,
                AdvancedVisemeArticulator.SmileSad
            };
            var tongue = new[]
            {
                AdvancedVisemeArticulator.TongueOut,
                AdvancedVisemeArticulator.TongueY,
                AdvancedVisemeArticulator.TongueArchY,
                AdvancedVisemeArticulator.TongueShape
            };

            Assert.That(AdvancedVisemeAnimatorBuilder.HasCompleteVisiblePoseOwnership(visible), Is.True);
            foreach (var omitted in visible)
            {
                Assert.That(AdvancedVisemeAnimatorBuilder.HasCompleteVisiblePoseOwnership(
                        visible.Where(articulator => articulator != omitted)),
                    Is.False, $"Missing {omitted} must make visible-pose ownership incomplete.");
            }

            Assert.That(AdvancedVisemeAnimatorBuilder.HasCompleteVisiblePoseOwnership(
                visible.Concat(tongue)), Is.True,
                "Adding tongue measurements must not change complete visible-pose ownership.");
            Assert.That(AdvancedVisemeAnimatorBuilder.HasCompleteVisiblePoseOwnership(
                visible.Concat(tongue).Except(tongue)), Is.True,
                "Removing tongue measurements must not change complete visible-pose ownership.");
            Assert.That(AdvancedVisemeAnimatorBuilder.HasCompleteVisiblePoseOwnership(
                visible.Skip(1).Concat(tongue)), Is.False,
                "Tongue measurements cannot compensate for a missing visible axis.");

            var request = new AdvancedVisemeAnimatorBuilder.Request
            {
                reuseExistingTracking = true,
                resolvedBlendShapes = visible.ToDictionary(
                    articulator => articulator, articulator => articulator.ToString())
            };
            Assert.That(AdvancedVisemeAnimatorBuilder.HasCompleteVisiblePoseOwnership(
                visible.Where(articulator =>
                    AdvancedVisemeAnimatorBuilder.HasDriveableOutputPose(request, articulator))), Is.True);
            request.resolvedBlendShapes.Remove(AdvancedVisemeArticulator.MouthOpen);
            Assert.That(AdvancedVisemeAnimatorBuilder.HasCompleteVisiblePoseOwnership(
                visible.Where(articulator =>
                    AdvancedVisemeAnimatorBuilder.HasDriveableOutputPose(request, articulator))), Is.False,
                "A complete parameter bus must not own/fade speech when one visible output pose is missing.");
        }

        [Test]
        public void FullTongueSynthesisDoesNotDependOnTheTrackingInputPreset()
        {
            var synthesized = AdvancedVisemeAnimatorBuilder.SynthesizedArticulators().ToArray();
            foreach (var articulator in new[]
                     {
                         AdvancedVisemeArticulator.TongueOut,
                         AdvancedVisemeArticulator.TongueX,
                         AdvancedVisemeArticulator.TongueY,
                         AdvancedVisemeArticulator.TongueRoll,
                         AdvancedVisemeArticulator.TongueArchY,
                         AdvancedVisemeArticulator.TongueShape,
                         AdvancedVisemeArticulator.TongueTwistRight,
                         AdvancedVisemeArticulator.TongueTwistLeft
                     })
            {
                Assert.That(synthesized, Does.Contain(articulator));
            }

            Assert.That(AdvancedVisemeAnimatorBuilder.TrackedArticulators(
                AdvancedVisemeTrackingInputs.Disabled), Is.Empty);
            Assert.That(AdvancedVisemeAnimatorBuilder.TrackedArticulators(
                AdvancedVisemeTrackingInputs.Auto), Is.Empty);

            var balanced = AdvancedVisemeAnimatorBuilder.TrackedArticulators(
                AdvancedVisemeTrackingInputs.Balanced8).ToArray();
            Assert.That(balanced, Does.Contain(AdvancedVisemeArticulator.TongueOut));
            Assert.That(balanced.Contains(AdvancedVisemeArticulator.TongueY), Is.False);
            Assert.That(balanced.Contains(AdvancedVisemeArticulator.TongueX), Is.False);

            var quality = AdvancedVisemeAnimatorBuilder.TrackedArticulators(
                AdvancedVisemeTrackingInputs.Quality12).ToArray();
            Assert.That(quality, Does.Contain(AdvancedVisemeArticulator.TongueY));
            Assert.That(quality.Contains(AdvancedVisemeArticulator.TongueX), Is.False);

            var fullTongue = AdvancedVisemeAnimatorBuilder.TrackedArticulators(
                AdvancedVisemeTrackingInputs.FullTongue18).ToArray();
            Assert.That(fullTongue, Does.Contain(AdvancedVisemeArticulator.TongueX));
            Assert.That(fullTongue, Does.Contain(AdvancedVisemeArticulator.TongueTwistLeft));
        }

        [Test]
        public void DefaultProfileIncludesTheCompleteUnifiedExpressionsTongueSet()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                foreach (var articulator in new[]
                         {
                             AdvancedVisemeArticulator.TongueOut,
                             AdvancedVisemeArticulator.TongueX,
                             AdvancedVisemeArticulator.TongueY,
                             AdvancedVisemeArticulator.TongueRoll,
                             AdvancedVisemeArticulator.TongueArchY,
                             AdvancedVisemeArticulator.TongueShape,
                             AdvancedVisemeArticulator.TongueTwistRight,
                             AdvancedVisemeArticulator.TongueTwistLeft
                         })
                {
                    Assert.That(profile.FindBinding(articulator), Is.Not.Null, articulator.ToString());
                    Assert.That(profile.FindBinding(articulator).trackingParameter, Is.Not.Empty);
                }
                Assert.That(profile.visemePoses[8].tongueY, Is.GreaterThan(0f));
                Assert.That(profile.visemePoses[5].tongueArchY, Is.LessThan(0f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ProfileMigrationPreservesCustomTongueAndIntentionalMissingBindings()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                profile.visemePoses[8].tongueY = 0.314159f;
                profile.articulatorBindings = new[]
                {
                    new ArticulatorRigBinding
                    {
                        articulator = AdvancedVisemeArticulator.JawOpen,
                        trackingParameter = "CustomJaw"
                    }
                };
                var serialized = new SerializedObject(profile);
                serialized.FindProperty("defaultsVersion").intValue = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                profile.EnsureDefaults();

                Assert.That(profile.visemePoses[8].tongueY, Is.EqualTo(0.314159f).Within(1e-7f));
                Assert.That(profile.FindBinding(AdvancedVisemeArticulator.MouthOpen), Is.Null,
                    "Migration must not restore an intentionally absent legacy binding.");
                Assert.That(profile.FindBinding(AdvancedVisemeArticulator.TongueX), Is.Not.Null,
                    "The newly introduced extended tongue bindings migrate once.");

                profile.articulatorBindings = profile.articulatorBindings
                    .Where(binding => binding.articulator != AdvancedVisemeArticulator.TongueX)
                    .ToArray();
                profile.EnsureDefaults();
                Assert.That(profile.FindBinding(AdvancedVisemeArticulator.TongueX), Is.Null,
                    "A migrated profile may intentionally remove a generated binding.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ProfileDefaultsAndMigrationPromoteResidualMismatchFadeToFullAuthority()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                Assert.That(profile.residualMismatchFade, Is.EqualTo(1f).Within(1e-6f),
                    "New profiles should fully remove incompatible authored residuals.");

                profile.residualMismatchFade = 0.9f;
                var serialized = new SerializedObject(profile);
                var version = serialized.FindProperty("defaultsVersion");
                version.intValue = 4;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                profile.EnsureDefaults();

                serialized.Update();
                Assert.That(profile.residualMismatchFade, Is.EqualTo(1f).Within(1e-6f),
                    "The untouched 0.9 legacy default should migrate to full authority.");
                Assert.That(version.intValue, Is.GreaterThan(4),
                    "Residual-authority migration must advance the defaults version.");

                serialized.FindProperty("residualMismatchFade").floatValue = 0.63f;
                version.intValue = 4;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                profile.EnsureDefaults();
                Assert.That(profile.residualMismatchFade, Is.EqualTo(0.63f).Within(1e-6f),
                    "Migration must preserve an explicitly calibrated residual fade.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void EstablishedProfilePreservesIntentionallyEmptyArticulatorBindingsDuringValidation()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                profile.articulatorBindings = Array.Empty<ArticulatorRigBinding>();

                profile.EnsureDefaults();
                Assert.That(profile.articulatorBindings, Is.Empty,
                    "An established profile may intentionally opt out of every tracking binding.");

                var onValidate = typeof(VisemeReconstructionProfile).GetMethod(
                    "OnValidate",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.That(onValidate, Is.Not.Null);
                onValidate.Invoke(profile, null);

                Assert.That(profile.articulatorBindings, Is.Empty,
                    "Inspector validation must not silently restore intentionally removed bindings.");
                Assert.That(profile.FindBinding(AdvancedVisemeArticulator.JawOpen), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void VisemeResolverRecoversOculusVowelsFromInvalidDescriptorEntries()
        {
            var root = new GameObject("Oculus Viseme Resolver Test");
            var descriptor = root.AddComponent<VRCAvatarDescriptor>();
            var mesh = CreateOculusNamedVisemeMesh();
            try
            {
                descriptor.VisemeBlendShapes = new[]
                {
                    "vrc.v_sil", "vrc.v_pp", "vrc.v_ff", "vrc.v_th", "vrc.v_dd",
                    "vrc.v_kk", "vrc.v_ch", "vrc.v_ss", "vrc.v_nn", "vrc.v_rr",
                    "===Visemes ===", "===Visemes ===", "===Visemes ===", "===Visemes ===", "===Visemes ==="
                };

                var resolved = AdvancedVisemeReconstructorProcessor.ResolveVisemeNames(descriptor, mesh);

                Assert.That(resolved, Is.EqualTo(new[]
                {
                    "vrc.v_sil", "vrc.v_pp", "vrc.v_ff", "vrc.v_th", "vrc.v_dd",
                    "vrc.v_kk", "vrc.v_ch", "vrc.v_ss", "vrc.v_nn", "vrc.v_rr",
                    "vrc.v_aa", "vrc.v_e", "vrc.v_ih", "vrc.v_oh", "vrc.v_ou"
                }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void VisemeDecoderNeverUsesTheIntegerParameterAsABlendTreeDriver()
        {
            var loadedControllers = UnityEngine.Resources.FindObjectsOfTypeAll<AnimatorController>();
            foreach (var controller in loadedControllers)
            {
                if (controller == null || !controller.parameters.Any(p => p.name == "Viseme")) continue;
                var parameterTypes = controller.parameters.ToDictionary(p => p.name, p => p.type);
                foreach (var layer in controller.layers)
                foreach (var childState in layer.stateMachine.states)
                    AssertBlendTreesUseFloatParameters(childState.state.motion, parameterTypes);
            }
        }

        [Test]
        public void InspectorUsesTheYucpUiToolkitDesignSystem()
        {
            var gameObject = new GameObject("Advanced Viseme Inspector Test");
            var component = gameObject.AddComponent<AdvancedVisemeReconstructorData>();
            var modeKey = AdvancedVisemeReconstructorDataEditor.InspectorModeSessionKey(
                component.GetInstanceID());
            var previousMode = SessionState.GetInt(modeKey, 0);
            SessionState.SetInt(modeKey, 0);
            var editor = UnityEditor.Editor.CreateEditor(component);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.That(editor, Is.TypeOf<AdvancedVisemeReconstructorDataEditor>());
                Assert.That(root.ClassListContains("yucp-root"), Is.True);
                var simple = UQueryExtensions.Q<VisualElement>(root, "simple-mode-ui");
                var advanced = UQueryExtensions.Q<VisualElement>(root, "advanced-mode-ui");
                Assert.That(simple, Is.Not.Null);
                Assert.That(advanced, Is.Not.Null);
                Assert.That(simple.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(advanced.style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(UQueryExtensions.Q<Button>(root, "simple-mode-tab")
                    .ClassListContains("yucp-tab-selected"), Is.True);
                Assert.That(UQueryExtensions.Q<Button>(root, "advanced-mode-tab")
                    .ClassListContains("yucp-tab-selected"), Is.False);
                Assert.That(UQueryExtensions.Query<VisualElement>(simple, className: "yucp-card")
                    .ToList().Count, Is.EqualTo(3));
                Assert.That(UQueryExtensions.Q<PopupField<string>>(root, "simple-face-tracking"),
                    Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Toggle>(root, "simple-natural-transitions"),
                    Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Toggle>(root, "simple-share-tuning"),
                    Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Toggle>(root, "simple-share-tuning").value,
                    Is.True);
                Assert.That(UQueryExtensions.Q<Slider>(root, "simple-speech-movement"),
                    Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Slider>(root, "simple-speech-liveliness"),
                    Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Slider>(root, "simple-reaction-speed"),
                    Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Slider>(root, "simple-pause-stability"),
                    Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Slider>(root, "simple-face-tracking-priority"),
                    Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Slider>(root, "simple-speech-movement").enabledSelf,
                    Is.True, "The first friendly edit should create its profile automatically.");
                Assert.That(UQueryExtensions.Q<Button>(root, "simple-profile-action"), Is.Null);
                Assert.That(UQueryExtensions.Q<Button>(root, "profile-action"), Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Foldout>(root, "motion-tuning"), Is.Not.Null);
                Assert.That(UQueryExtensions.Q<VisualElement>(root, "fine-tune-visemes"),
                    Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Button>(root, "fine-tune-create-profile"),
                    Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Foldout>(root, "avatar-menu-settings"), Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Foldout>(root, "rig-tools"), Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Foldout>(root, "expert-settings"), Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Label>(root, "tracking-budget").text,
                    Does.Contain("speech only"));
                Assert.That(UQueryExtensions.Q<Label>(root, "runtime-menu-budget").text,
                    Does.Contain("13 synced bits"));
                Assert.That(UQueryExtensions.Q<VisualElement>(root, "reuse-prefix-container").style.display.value,
                    Is.EqualTo(DisplayStyle.Flex));
            }
            finally
            {
                SessionState.SetInt(modeKey, previousMode);
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void AdvancedInspectorShowsFocusedPerVisemeControls()
        {
            const int rrViseme = 9;
            const int aaViseme = 10;
            var gameObject = new GameObject("Advanced Viseme Per-Sound UI Test");
            var component = gameObject.AddComponent<AdvancedVisemeReconstructorData>();
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            component.profile = profile;
            var modeKey = AdvancedVisemeReconstructorDataEditor.InspectorModeSessionKey(
                component.GetInstanceID());
            var visemeKey = $"YUCP_AVR_FineTuneViseme_{component.GetInstanceID()}";
            var previousMode = SessionState.GetInt(modeKey, 0);
            var previousViseme = SessionState.GetInt(visemeKey, 0);
            SessionState.SetInt(modeKey, 1);
            SessionState.SetInt(visemeKey, rrViseme);
            var editor = UnityEditor.Editor.CreateEditor(component);
            try
            {
                var root = editor.CreateInspectorGUI();
                var chips = UQueryExtensions.Query<Button>(root)
                    .ToList()
                    .Where(button => button.name != null &&
                                     button.name.StartsWith("fine-tune-viseme-",
                                         StringComparison.Ordinal))
                    .ToArray();
                Assert.That(chips, Has.Length.EqualTo(
                    VisemeReconstructionProfile.VisemeCount));
                Assert.That(UQueryExtensions.Q<Button>(root, "fine-tune-viseme-9").text,
                    Does.StartWith("R"));

                var jaw = UQueryExtensions.Q<Slider>(root, "fine-tune-jaw-opening");
                var lips = UQueryExtensions.Q<Slider>(root, "fine-tune-lips");
                var tongue = UQueryExtensions.Q<Slider>(root, "fine-tune-tongue");
                Assert.That(jaw, Is.Not.Null);
                Assert.That(lips, Is.Not.Null);
                Assert.That(tongue, Is.Not.Null);
                Assert.That(jaw.value, Is.EqualTo(100f).Within(1e-6f));
                Assert.That(lips.value, Is.EqualTo(100f).Within(1e-6f));
                Assert.That(tongue.value, Is.EqualTo(100f).Within(1e-6f));

                Assert.That(profile.GetVisemeArticulationMultiplier(
                        rrViseme, AdvancedVisemeArticulator.JawOpen),
                    Is.EqualTo(1f).Within(1e-6f));
                Assert.That(profile.GetVisemeArticulationMultiplier(
                        aaViseme, AdvancedVisemeArticulator.JawOpen),
                    Is.EqualTo(1f).Within(1e-6f));
                Assert.That(UQueryExtensions.Q<Foldout>(
                    root, "fine-tune-precise-controls"), Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Slider>(
                    root, "fine-tune-axis-jawOpen"), Is.Not.Null);
            }
            finally
            {
                SessionState.SetInt(modeKey, previousMode);
                SessionState.SetInt(visemeKey, previousViseme);
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void InspectorRestoresAdvancedViewWithoutChangingAvatarSettings()
        {
            var gameObject = new GameObject("Advanced Viseme Inspector Mode Test");
            var component = gameObject.AddComponent<AdvancedVisemeReconstructorData>();
            var modeKey = AdvancedVisemeReconstructorDataEditor.InspectorModeSessionKey(
                component.GetInstanceID());
            var previousMode = SessionState.GetInt(modeKey, 0);
            SessionState.SetInt(modeKey, 1);
            var before = EditorJsonUtility.ToJson(component);
            var editor = UnityEditor.Editor.CreateEditor(component);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.That(UQueryExtensions.Q<VisualElement>(root, "simple-mode-ui")
                    .style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(UQueryExtensions.Q<VisualElement>(root, "advanced-mode-ui")
                    .style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(UQueryExtensions.Q<Button>(root, "advanced-mode-tab")
                    .ClassListContains("yucp-tab-selected"), Is.True);
                Assert.That(EditorJsonUtility.ToJson(component), Is.EqualTo(before),
                    "Choosing an inspector view must never become an avatar setting.");
            }
            finally
            {
                SessionState.SetInt(modeKey, previousMode);
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void FriendlyTimeSlidersRoundTripTheirAdvancedValues()
        {
            foreach (var seconds in new[] { 0.006f, 0.024f, 0.07f, 0.12f })
            {
                var simple = AdvancedVisemeReconstructorDataEditor
                    .ReactionSpeedFromSeconds(seconds);
                Assert.That(simple, Is.InRange(0f, 1f));
                Assert.That(AdvancedVisemeReconstructorDataEditor
                        .SecondsFromReactionSpeed(simple),
                    Is.EqualTo(seconds).Within(1e-6f));
            }

            foreach (var seconds in new[] { 0.04f, 0.08f, 0.16f, 0.4f })
            {
                var simple = AdvancedVisemeReconstructorDataEditor
                    .PauseStabilityFromSeconds(seconds);
                Assert.That(simple, Is.InRange(0f, 1f));
                Assert.That(AdvancedVisemeReconstructorDataEditor
                        .SecondsFromPauseStability(simple),
                    Is.EqualTo(seconds).Within(1e-6f));
            }

            Assert.That(AdvancedVisemeReconstructorDataEditor
                .ReactionSpeedFromSeconds(0.12f), Is.Zero.Within(1e-6f));
            Assert.That(AdvancedVisemeReconstructorDataEditor
                .ReactionSpeedFromSeconds(0.006f), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(AdvancedVisemeReconstructorDataEditor
                .PauseStabilityFromSeconds(0.04f), Is.Zero.Within(1e-6f));
            Assert.That(AdvancedVisemeReconstructorDataEditor
                .PauseStabilityFromSeconds(0.4f), Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void SimpleInspectorEditsTheSameProfileValuesAsAdvanced()
        {
            var gameObject = new GameObject("Advanced Viseme Simple Settings Test");
            var component = gameObject.AddComponent<AdvancedVisemeReconstructorData>();
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            component.profile = profile;
            var modeKey = AdvancedVisemeReconstructorDataEditor.InspectorModeSessionKey(
                component.GetInstanceID());
            var previousMode = SessionState.GetInt(modeKey, 0);
            SessionState.SetInt(modeKey, 0);
            var editor = UnityEditor.Editor.CreateEditor(component);
            try
            {
                var untouchedBilabialAssist = profile.bilabialAssistStrength;
                var root = editor.CreateInspectorGUI();
                Assert.That(UQueryExtensions.Q<Button>(root, "simple-profile-action"), Is.Null,
                    "An assigned profile should be immediately editable.");

                var movement = UQueryExtensions.Q<Slider>(root, "simple-speech-movement");
                var liveliness = UQueryExtensions.Q<Slider>(root, "simple-speech-liveliness");
                var response = UQueryExtensions.Q<Slider>(root, "simple-reaction-speed");
                var stability = UQueryExtensions.Q<Slider>(root, "simple-pause-stability");
                Assert.That(movement.enabledSelf, Is.True);
                Assert.That(liveliness.enabledSelf, Is.True);
                Assert.That(response.enabledSelf, Is.True);
                Assert.That(stability.enabledSelf, Is.True);

                movement.value = 0.37f;
                liveliness.value = 0.82f;
                response.value = 1f;
                stability.value = 0f;

                Assert.That(profile.speechMotionStrength, Is.EqualTo(0.37f).Within(1e-6f));
                Assert.That(profile.speechLiveliness, Is.EqualTo(0.82f).Within(1e-6f));
                Assert.That(profile.visemeResponseSeconds, Is.EqualTo(0.006f).Within(1e-6f));
                Assert.That(profile.speechHangoverSeconds, Is.EqualTo(0.04f).Within(1e-6f));
                Assert.That(profile.bilabialAssistStrength,
                    Is.EqualTo(untouchedBilabialAssist).Within(1e-6f),
                    "A friendly slider must not overwrite unrelated expert tuning.");
            }
            finally
            {
                SessionState.SetInt(modeKey, previousMode);
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void FirstFriendlyEditCreatesAProfileAndKeepsTheRequestedValue()
        {
            var gameObject = new GameObject("Advanced Viseme Copy On Edit Test");
            var component = gameObject.AddComponent<AdvancedVisemeReconstructorData>();
            var modeKey = AdvancedVisemeReconstructorDataEditor.InspectorModeSessionKey(
                component.GetInstanceID());
            var previousMode = SessionState.GetInt(modeKey, 0);
            SessionState.SetInt(modeKey, 0);
            var editor = UnityEditor.Editor.CreateEditor(component);
            string createdPath = null;
            try
            {
                Assert.That(component.profile, Is.Null);
                var root = editor.CreateInspectorGUI();
                var liveliness = UQueryExtensions.Q<Slider>(root, "simple-speech-liveliness");
                Assert.That(liveliness.enabledSelf, Is.True);

                liveliness.value = 0.82f;

                Assert.That(component.profile, Is.Not.Null);
                createdPath = AssetDatabase.GetAssetPath(component.profile);
                Assert.That(createdPath, Does.StartWith("Assets/YUCP/AdvancedVisemeProfiles/"));
                Assert.That(component.profile.speechLiveliness,
                    Is.EqualTo(0.82f).Within(1e-6f));
                Assert.That(component.profile.speechMotionStrength,
                    Is.EqualTo(1f).Within(1e-6f));
                Assert.That(component.profile.visemeResponseSeconds,
                    Is.EqualTo(0.017f).Within(1e-6f),
                    "Copy-on-edit must preserve every unrelated recommended default.");
            }
            finally
            {
                SessionState.SetInt(modeKey, previousMode);
                UnityEngine.Object.DestroyImmediate(editor);
                if (!string.IsNullOrEmpty(createdPath)) AssetDatabase.DeleteAsset(createdPath);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ResidualCalibrationPreservesEveryAuthoredDeltaAndSourceMesh()
        {
            var source = CreateCalibrationMesh();
            var originalBlendShapeCount = source.blendShapeCount;
            try
            {
                var visemes = new int[15];
                for (var i = 0; i < visemes.Length; i++) visemes[i] = source.GetBlendShapeIndex("vrc.v_" + VisemeReconstructionProfile.VisemeNames[i]);
                var basis = new[]
                {
                    new AdvancedVisemeMeshCalibrator.BasisInput(AdvancedVisemeArticulator.JawOpen, source.GetBlendShapeIndex("JawOpen")),
                    new AdvancedVisemeMeshCalibrator.BasisInput(AdvancedVisemeArticulator.LipPucker, source.GetBlendShapeIndex("LipPucker"))
                };

                var result = AdvancedVisemeMeshCalibrator.Build(source, visemes, basis);
                Assert.That(result.success, Is.True, result.error);
                try
                {
                    Assert.That(source.blendShapeCount, Is.EqualTo(originalBlendShapeCount));
                    for (var i = 0; i < 15; i++)
                    {
                        var target = ReadDelta(source, visemes[i]);
                        var basisA = ReadDelta(source, basis[0].blendShapeIndex);
                        var basisB = ReadDelta(source, basis[1].blendShapeIndex);
                        var residual = ReadDelta(result.mesh, result.mesh.GetBlendShapeIndex(result.residualBlendShapeNames[i]));
                        for (var v = 0; v < target.vertices.Length; v++)
                        {
                            var reconstructedVertex = basisA.vertices[v] * result.coefficients[i, 0] +
                                                      basisB.vertices[v] * result.coefficients[i, 1] + residual.vertices[v];
                            var reconstructedNormal = basisA.normals[v] * result.coefficients[i, 0] +
                                                      basisB.normals[v] * result.coefficients[i, 1] + residual.normals[v];
                            var reconstructedTangent = basisA.tangents[v] * result.coefficients[i, 0] +
                                                       basisB.tangents[v] * result.coefficients[i, 1] + residual.tangents[v];
                            Assert.That(Vector3.Distance(reconstructedVertex, target.vertices[v]), Is.LessThan(1e-5f));
                            Assert.That(Vector3.Distance(reconstructedNormal, target.normals[v]), Is.LessThan(1e-5f));
                            Assert.That(Vector3.Distance(reconstructedTangent, target.tangents[v]), Is.LessThan(1e-5f));
                        }
                    }

                    var aggregateBasisA = ReadDelta(source, basis[0].blendShapeIndex);
                    var aggregateBasisB = ReadDelta(source, basis[1].blendShapeIndex);
                    var random = new System.Random(8472);
                    for (var sample = 0; sample < 64; sample++)
                    {
                        var weights = new float[15];
                        var weightSum = 0f;
                        for (var i = 0; i < weights.Length; i++)
                        {
                            weights[i] = (float)random.NextDouble();
                            weightSum += weights[i];
                        }
                        for (var i = 0; i < weights.Length; i++) weights[i] /= weightSum;

                        for (var v = 0; v < source.vertexCount; v++)
                        {
                            var authored = Vector3.zero;
                            var reconstructed = Vector3.zero;
                            for (var i = 0; i < weights.Length; i++)
                            {
                                var target = ReadDelta(source, visemes[i]);
                                var residual = ReadDelta(result.mesh,
                                    result.mesh.GetBlendShapeIndex(result.residualBlendShapeNames[i]));
                                authored += target.vertices[v] * weights[i];
                                reconstructed += (aggregateBasisA.vertices[v] * result.coefficients[i, 0] +
                                                  aggregateBasisB.vertices[v] * result.coefficients[i, 1] +
                                                  residual.vertices[v]) * weights[i];
                            }
                            Assert.That(Vector3.Distance(reconstructed, authored), Is.LessThan(1e-5f));
                        }
                    }
                }
                finally
                {
                    if (result.mesh != null) UnityEngine.Object.DestroyImmediate(result.mesh);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private static float[] NewSilenceSimplex()
        {
            var values = new float[15];
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

        private static Mesh CreateCalibrationMesh()
        {
            var mesh = new Mesh { name = "Calibration" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.triangles = new[] { 0, 1, 2 };

            var zero = new Vector3[3];
            var basisA = new[] { new Vector3(0.1f, 0f, 0f), Vector3.zero, Vector3.zero };
            var basisB = new[] { Vector3.zero, new Vector3(0f, 0.1f, 0f), Vector3.zero };
            mesh.AddBlendShapeFrame("JawOpen", 100f, basisA, basisA, basisA);
            mesh.AddBlendShapeFrame("LipPucker", 100f, basisB, basisB, basisB);
            for (var i = 0; i < 15; i++)
            {
                var scaleA = 0.15f + i * 0.02f;
                var scaleB = 0.8f - i * 0.015f;
                var residual = new Vector3(0f, 0f, 0.002f * (i + 1));
                var delta = new[]
                {
                    basisA[0] * scaleA + residual,
                    basisB[1] * scaleB + residual,
                    residual
                };
                mesh.AddBlendShapeFrame("vrc.v_" + VisemeReconstructionProfile.VisemeNames[i], 100f, delta, delta, delta);
            }
            return mesh;
        }

        private static Mesh CreateOculusNamedVisemeMesh()
        {
            var mesh = new Mesh { name = "Oculus Named Visemes" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            var delta = new Vector3[mesh.vertexCount];
            foreach (var suffix in new[]
                     {
                         "sil", "pp", "ff", "th", "dd", "kk", "ch", "ss", "nn", "rr",
                         "aa", "e", "ih", "oh", "ou"
                     })
            {
                mesh.AddBlendShapeFrame("vrc.v_" + suffix, 100f, delta, delta, delta);
            }
            return mesh;
        }

        [Test]
        public void StableHashIgnoresProfileDiagnosticsAndMigrationBookkeeping()
        {
            var root = new GameObject("Stable hash fixture");
            var profile = ScriptableObject.CreateInstance<VisemeReconstructionProfile>();
            try
            {
                profile.ResetToDefaults();
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.parameterPrefix = "YUCP/StableHash";
                component.profile = profile;

                var method = typeof(AdvancedVisemeReconstructorProcessor).GetMethod(
                    "StableHash",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);

                string Hash()
                {
                    return (string)method.Invoke(null, new object[]
                    {
                        component,
                        profile,
                        null,
                        "Face",
                        "tracking",
                        "links",
                        false,
                        false
                    });
                }

                var baseline = Hash();
                profile.SetDiagnostics(0.123f, 0.987f);
                var defaultsVersion = typeof(VisemeReconstructionProfile).GetField(
                    "defaultsVersion",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.That(defaultsVersion, Is.Not.Null);
                defaultsVersion.SetValue(profile, 12345);
                Assert.That(Hash(), Is.EqualTo(baseline),
                    "Cached editor diagnostics must not rename generated assets.");

                profile.visemeResponseSeconds += 0.005f;
                Assert.That(Hash(), Is.Not.EqualTo(baseline),
                    "A rendering input must remain part of the content address.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void StableHashUsesTransientMeshContentInsteadOfSessionIdentity()
        {
            var root = new GameObject("Transient mesh hash fixture");
            var profile = ScriptableObject.CreateInstance<VisemeReconstructionProfile>();
            Mesh first = null;
            Mesh identical = null;
            Mesh changed = null;
            try
            {
                profile.ResetToDefaults();
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.parameterPrefix = "YUCP/TransientMeshHash";
                component.profile = profile;

                Mesh CreateMesh(float visemeDelta)
                {
                    var mesh = new Mesh { name = "Unsaved generated face" };
                    mesh.vertices = new[]
                    {
                        Vector3.zero,
                        Vector3.right,
                        Vector3.up
                    };
                    mesh.normals = new[]
                    {
                        Vector3.forward,
                        Vector3.forward,
                        Vector3.forward
                    };
                    mesh.tangents = new[]
                    {
                        new Vector4(1f, 0f, 0f, 1f),
                        new Vector4(1f, 0f, 0f, 1f),
                        new Vector4(1f, 0f, 0f, 1f)
                    };
                    mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
                    mesh.triangles = new[] { 0, 1, 2 };
                    var vertices = new[]
                    {
                        new Vector3(visemeDelta, 0f, 0f),
                        Vector3.zero,
                        Vector3.zero
                    };
                    var normals = new[]
                    {
                        new Vector3(0f, visemeDelta * 0.25f, 0f),
                        Vector3.zero,
                        Vector3.zero
                    };
                    var tangents = new[]
                    {
                        new Vector3(0f, 0f, visemeDelta * 0.5f),
                        Vector3.zero,
                        Vector3.zero
                    };
                    mesh.AddBlendShapeFrame(
                        "vrc.v_aa", 100f, vertices, normals, tangents);
                    return mesh;
                }

                first = CreateMesh(0.1f);
                identical = CreateMesh(0.1f);
                changed = CreateMesh(0.1005f);
                Assert.That(AssetDatabase.Contains(first), Is.False);
                Assert.That(AssetDatabase.Contains(identical), Is.False);

                var method = typeof(AdvancedVisemeReconstructorProcessor).GetMethod(
                    "StableHash",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);

                string Hash(Mesh mesh)
                {
                    return (string)method.Invoke(null, new object[]
                    {
                        component,
                        profile,
                        mesh,
                        "Face",
                        "tracking",
                        "links",
                        false,
                        false
                    });
                }

                Assert.That(Hash(identical), Is.EqualTo(Hash(first)),
                    "Equivalent transient meshes must keep generated asset paths stable across builds.");
                Assert.That(Hash(changed), Is.Not.EqualTo(Hash(first)),
                    "A transient blendshape geometry change must regenerate calibrated outputs.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(identical);
                UnityEngine.Object.DestroyImmediate(changed);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        private static void AssertBlendTreesUseFloatParameters(
            Motion motion,
            System.Collections.Generic.IReadOnlyDictionary<string, AnimatorControllerParameterType> parameterTypes)
        {
            if (!(motion is BlendTree tree)) return;
            if (tree.blendType != BlendTreeType.Direct &&
                !string.IsNullOrEmpty(tree.blendParameter) &&
                parameterTypes.TryGetValue(tree.blendParameter, out var blendType))
            {
                Assert.That(blendType, Is.EqualTo(AnimatorControllerParameterType.Float),
                    $"BlendTree '{tree.name}' uses non-Float parameter '{tree.blendParameter}'.");
            }

            foreach (var child in tree.children)
            {
                if (tree.blendType == BlendTreeType.Direct &&
                    !string.IsNullOrEmpty(child.directBlendParameter) &&
                    parameterTypes.TryGetValue(child.directBlendParameter, out var directType))
                {
                    Assert.That(directType, Is.EqualTo(AnimatorControllerParameterType.Float),
                        $"Direct BlendTree '{tree.name}' uses non-Float parameter '{child.directBlendParameter}'.");
                }
                AssertBlendTreesUseFloatParameters(child.motion, parameterTypes);
            }
        }

        private static (Vector3[] vertices, Vector3[] normals, Vector3[] tangents) ReadDelta(Mesh mesh, int index)
        {
            var vertices = new Vector3[mesh.vertexCount];
            var normals = new Vector3[mesh.vertexCount];
            var tangents = new Vector3[mesh.vertexCount];
            var frame = mesh.GetBlendShapeFrameCount(index) - 1;
            mesh.GetBlendShapeFrameVertices(index, frame, vertices, normals, tangents);
            return (vertices, normals, tangents);
        }
    }
}
#endif
