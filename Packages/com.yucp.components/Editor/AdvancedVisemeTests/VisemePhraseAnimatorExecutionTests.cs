#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using YUCP.Components.Editor.VisemePhrase;

namespace YUCP.Components.Editor.Tests
{
    /// <summary>
    /// Executes the generated controller with Unity's real Animator evaluator.
    /// VRChat parameter drivers are client behaviours rather than Unity runtime
    /// behaviours, so the harness mirrors their ordered state-entry Set and Copy
    /// operations;
    /// all timing, conditions, failure links, and layer transitions remain the
    /// generated AnimatorController's own behaviour.
    /// </summary>
    public sealed class VisemePhraseAnimatorExecutionTests
    {
        private const string GeneratedRoot =
            "Assets/YUCP/GeneratedAssets/__VisemePhraseAnimatorExecutionTests";

        private string generatedFolder;

        [SetUp]
        public void SetUp()
        {
            EnsureFolder(GeneratedRoot);
            generatedFolder = GeneratedRoot + "/" + Guid.NewGuid().ToString("N");
            EnsureFolder(generatedFolder);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(generatedFolder) &&
                AssetDatabase.IsValidFolder(generatedFolder))
                AssetDatabase.DeleteAsset(generatedFolder);
            if (AssetDatabase.IsValidFolder(GeneratedRoot) &&
                AssetDatabase.GetSubFolders(GeneratedRoot).Length == 0)
                AssetDatabase.DeleteAsset(GeneratedRoot);
        }

        [TestCase(15, 0.5f)]
        [TestCase(15, 1f)]
        [TestCase(15, 2f)]
        [TestCase(50, 0.5f)]
        [TestCase(50, 1f)]
        [TestCase(50, 2f)]
        [TestCase(90, 0.5f)]
        [TestCase(90, 1f)]
        [TestCase(90, 2f)]
        [TestCase(144, 0.5f)]
        [TestCase(144, 1f)]
        [TestCase(144, 2f)]
        public void GeneratedAnimator_AcceptsExactAndStretchedTraceAtRepresentativeRates(
            int framesPerSecond,
            float durationScale)
        {
            var phrase = Phrase("accept", new[] { 4, 5, 6 });
            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, true, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);
                Play(runtime, framesPerSecond, durationScale, 4, 5, 6);
                runtime.RunFor(0.45f, framesPerSecond);

                Assert.That(runtime.CarrierEdgeCount, Is.EqualTo(1),
                    "One accepted trace must toggle the network carrier exactly once. " +
                    runtime.DiagnosticTrace + " Updates: " + runtime.DetailedTrace);
                Assert.That(runtime.MatchPulseCount, Is.EqualTo(1),
                    "The carrier edge must decode into exactly one public pulse.");
            }
        }

        [TestCase(50)]
        [TestCase(90)]
        [TestCase(144)]
        public void GeneratedAnimator_AcceptsSupportedLiveContextRecombinationAtRepresentativeRates(
            int framesPerSecond)
        {
            var liveVisemes = new[] { 1, 10, 5, 13, 5, 13, 8 };
            var liveDurations = new[]
                { 0.120f, 0.102f, 0.231f, 0.104f, 0.034f, 0.163f, 0.061f };
            var phrase = Phrase("mancojo_context", new[] { 1 });
            phrase.variants.Clear();
            phrase.variants.Add(TimedVariant(
                "enrolled_short",
                new[] { 1, 10, 5, 13 },
                new[] { 0.064f, 0.064f, 0.096f, 0.192f },
                new[] { 0.256f, 0.256f, 0.384f, 0.768f }));
            phrase.variants.Add(TimedVariant(
                "context_backoff",
                liveVisemes,
                Enumerable.Repeat(0.020f, liveVisemes.Length).ToArray(),
                new[] { 0.256f, 0.256f, 0.480f, 0.320f, 0.100f, 0.400f, 0.160f },
                true));

            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, true, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);
                for (var index = 0; index < liveVisemes.Length; index++)
                {
                    PlayToken(runtime, framesPerSecond,
                        liveVisemes[index], liveDurations[index]);
                    if (index == 3)
                        Assert.That(runtime.CarrierEdgeCount, Is.Zero,
                            "The strict four-run prefix must wait while its supported " +
                            "context continuation remains viable. " + runtime.DiagnosticTrace);
                }
                ExitUtterance(runtime, framesPerSecond);
                runtime.RunFor(0.45f, framesPerSecond);

                Assert.That(runtime.CarrierEdgeCount, Is.EqualTo(1),
                    "The supported live stream must toggle the carrier once. " +
                    runtime.DiagnosticTrace);
                Assert.That(runtime.MatchPulseCount, Is.EqualTo(1),
                    "The carrier edge must decode into one public Matched pulse. " +
                    runtime.DiagnosticTrace);
            }
        }

        [Test]
        public void GeneratedAnimator_AcceptsChangedTokenAtExactMaximumBoundary()
        {
            const int framesPerSecond = 50;
            var phrase = Phrase("exact_maximum", new[] { 4, 5, 6 });
            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, true, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);
                PlayToken(runtime, framesPerSecond, 4, 0.32f);
                PlayToken(runtime, framesPerSecond, 5, 0.32f);
                PlayToken(runtime, framesPerSecond, 6, 0.32f);
                ExitUtterance(runtime, framesPerSecond);
                runtime.RunFor(0.45f, framesPerSecond);

                Assert.That(runtime.CarrierEdgeCount, Is.EqualTo(1),
                    "A changed-token observation at the inclusive maximum must beat " +
                    "the held-token timeout. " + runtime.DiagnosticTrace + " Updates: " +
                    runtime.DetailedTrace);
                Assert.That(runtime.MatchPulseCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void GeneratedAnimator_AcceptsChangedTokenAtExactMinimumBoundary()
        {
            const int framesPerSecond = 50;
            var phrase = Phrase("exact_minimum", new[] { 4, 5, 6 });
            var observationAllowance = VisemePhraseTriggerContractAdapter
                .RuntimeObservationUncertaintyPerState(
                    phrase.variants.Single().states.Count);
            foreach (var state in phrase.variants.Single().states)
                state.minimumSeconds = Math.Max(0f, 0.04f -
                    observationAllowance);
            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, true, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);
                PlayTokenFrames(runtime, framesPerSecond, 4, 2);
                PlayTokenFrames(runtime, framesPerSecond, 5, 2);
                PlayTokenFrames(runtime, framesPerSecond, 6, 2);
                ExitUtterance(runtime, framesPerSecond);
                runtime.RunFor(0.45f, framesPerSecond);

                Assert.That(runtime.CarrierEdgeCount, Is.EqualTo(1),
                    "A changed-token observation at the inclusive minimum must advance. " +
                    runtime.DiagnosticTrace + " Updates: " + runtime.DetailedTrace);
                Assert.That(runtime.MatchPulseCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void GeneratedAnimator_AcceptsLongerMinimumWithoutDoubleChargingProbation()
        {
            const int framesPerSecond = 50;
            var phrase = Phrase("long_minimum", new[] { 4, 5, 6 });
            foreach (var state in phrase.variants.Single().states)
            {
                state.minimumSeconds = 0.08f;
                state.maximumSeconds = 0.24f;
            }
            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, true, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);
                PlayTokenFrames(runtime, framesPerSecond, 4, 4);
                PlayTokenFrames(runtime, framesPerSecond, 5, 4);
                PlayTokenFrames(runtime, framesPerSecond, 6, 4);
                ExitUtterance(runtime, framesPerSecond);
                runtime.RunFor(0.45f, framesPerSecond);

                Assert.That(runtime.CarrierEdgeCount, Is.EqualTo(1),
                    "The pair observer delays both token edges equally; an 80 ms raw " +
                    "phone must satisfy an 80 ms learned minimum exactly once. " +
                    runtime.DiagnosticTrace + " Updates: " + runtime.DetailedTrace);
                Assert.That(runtime.MatchPulseCount, Is.EqualTo(1));
            }
        }

        [TestCase(50)]
        [TestCase(90)]
        [TestCase(144)]
        public void GeneratedAnimator_IgnoresOneFrameWinnerBounceAndCommitsRealChanges(
            int framesPerSecond)
        {
            var phrase = Phrase("probation", new[] { 4, 5, 6 });
            foreach (var state in phrase.variants.Single().states)
                state.maximumSeconds = 0.60f;
            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, true, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);
                PlayToken(runtime, framesPerSecond, 4, 0.12f);
                PlayTokenFrames(runtime, framesPerSecond, 9, 1);
                PlayToken(runtime, framesPerSecond, 4, 0.08f);
                PlayToken(runtime, framesPerSecond, 5, 0.12f);
                PlayToken(runtime, framesPerSecond, 6, 0.16f);
                ExitUtterance(runtime, framesPerSecond);
                runtime.RunFor(0.45f, framesPerSecond);

                Assert.That(runtime.CarrierEdgeCount, Is.EqualTo(1),
                    "A sampled Oculus winner bounce measured below 30 ms must be ignored while " +
                    "the sustained following phones still commit. " +
                    runtime.DiagnosticTrace + " Updates: " + runtime.DetailedTrace);
                Assert.That(runtime.MatchPulseCount, Is.EqualTo(1));
                Assert.That(runtime.HasCommittedViseme(9), Is.False,
                    "A run shorter than 30 ms must never enter the matcher input.");
            }
        }

        [TestCase(90, 3)]
        [TestCase(125, 4)]
        [TestCase(144, 5)]
        public void GeneratedAnimator_PreservesShortRealPhoneImmediatelyAfterBounce(
            int framesPerSecond,
            int realPhoneFrames)
        {
            var phrase = Phrase("bounce_then_phone", new[] { 4, 5, 6 });
            foreach (var state in phrase.variants.Single().states)
                state.maximumSeconds = 0.60f;
            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, true, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);
                PlayToken(runtime, framesPerSecond, 4, 0.12f);
                PlayTokenFrames(runtime, framesPerSecond, 9, 1);
                PlayTokenFrames(runtime, framesPerSecond, 5, realPhoneFrames);
                PlayToken(runtime, framesPerSecond, 6, 0.16f);
                ExitUtterance(runtime, framesPerSecond);
                runtime.RunFor(0.45f, framesPerSecond);

                Assert.That((float)realPhoneFrames / framesPerSecond,
                    Is.GreaterThanOrEqualTo(0.03f));
                Assert.That(runtime.HasCommittedViseme(9), Is.False,
                    "The transient winner must remain below the matcher input.");
                Assert.That(runtime.HasCommittedViseme(5), Is.True,
                    "Driver dwell must not consume the following real phone.");
                Assert.That(runtime.CarrierEdgeCount, Is.EqualTo(1),
                    runtime.DiagnosticTrace + " Updates: " + runtime.DetailedTrace);
                Assert.That(runtime.MatchPulseCount, Is.EqualTo(1));
            }
        }

        [TestCase(90, 2)]
        [TestCase(144, 4)]
        public void GeneratedAnimator_RejectsClosestRepresentableSubThirtyMillisecondRun(
            int framesPerSecond,
            int bounceFrames)
        {
            var phrase = Phrase("near_threshold_bounce", new[] { 4, 5, 6 });
            foreach (var state in phrase.variants.Single().states)
                state.maximumSeconds = 0.60f;
            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, true, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);
                PlayToken(runtime, framesPerSecond, 4, 0.12f);
                PlayTokenFrames(runtime, framesPerSecond, 9, bounceFrames);
                PlayToken(runtime, framesPerSecond, 4, 0.08f);
                PlayToken(runtime, framesPerSecond, 5, 0.12f);
                PlayToken(runtime, framesPerSecond, 6, 0.16f);
                ExitUtterance(runtime, framesPerSecond);
                runtime.RunFor(0.45f, framesPerSecond);

                Assert.That((float)bounceFrames / framesPerSecond,
                    Is.LessThan(0.03f));
                Assert.That(runtime.HasCommittedViseme(9), Is.False);
                Assert.That(runtime.CarrierEdgeCount, Is.EqualTo(1),
                    runtime.DiagnosticTrace + " Updates: " + runtime.DetailedTrace);
            }
        }

        [Test]
        public void GeneratedAnimator_DoesNotCombineDifferentUnusedWinnerBounces()
        {
            const int framesPerSecond = 100;
            var phrase = Phrase("separate_other_bounces", new[] { 4, 5, 6 });
            foreach (var state in phrase.variants.Single().states)
                state.maximumSeconds = 0.60f;
            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, true, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);
                PlayToken(runtime, framesPerSecond, 4, 0.12f);
                PlayTokenFrames(runtime, framesPerSecond, 8, 2);
                PlayTokenFrames(runtime, framesPerSecond, 9, 2);
                PlayToken(runtime, framesPerSecond, 4, 0.08f);
                PlayToken(runtime, framesPerSecond, 5, 0.12f);
                PlayToken(runtime, framesPerSecond, 6, 0.16f);
                ExitUtterance(runtime, framesPerSecond);
                runtime.RunFor(0.45f, framesPerSecond);

                Assert.That(runtime.HasCommittedViseme(15), Is.False,
                    "Two different 20 ms unused winners are separate categorical runs; " +
                    "they may not share one 40 ms Other timer.");
                Assert.That(runtime.CarrierEdgeCount, Is.EqualTo(1),
                    runtime.DiagnosticTrace + " Updates: " + runtime.DetailedTrace);
            }
        }

        [Test]
        public void GeneratedAnimator_InternalOtherCannotMasqueradeAsU()
        {
            const int framesPerSecond = 90;
            var phrase = Phrase("bounded_u", new[] { 4, 14, 6 });
            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, true, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);
                Play(runtime, framesPerSecond, 1f, 4, 8, 6);
                runtime.RunFor(0.45f, framesPerSecond);

                Assert.That(runtime.HasCommittedViseme(15), Is.True);
                Assert.That(runtime.CarrierEdgeCount, Is.Zero,
                    "The internal Other=15 class must not satisfy U's 13.5..14.5 range. " +
                    runtime.DiagnosticTrace);
                Assert.That(runtime.MatchPulseCount, Is.Zero);
            }
        }

        [TestCase(20)]
        [TestCase(50)]
        [TestCase(90)]
        public void GeneratedAnimator_NaturalSpeechFiresOnStableFinalVisemeWithoutRelease(
            int framesPerSecond)
        {
            var phrase = Phrase("stable_final", new[] { 4, 5, 6 });
            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, true, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);

                // Raw Viseme is the identity stream. AVR Talking is derived and
                // may lag at onset or dip during a quiet consonant, so it must
                // neither gate a Natural Speech start nor abort an active path.
                runtime.SetFloat("YUCP/TestAVR/Speech/Talking", 0f);
                runtime.SetInt("Viseme", 4);
                runtime.RunFor(0.10f, framesPerSecond);
                runtime.SetInt("Viseme", 5);
                runtime.RunFor(0.10f, framesPerSecond);
                runtime.SetInt("Viseme", 6);
                runtime.RunFor(0.16f, framesPerSecond);

                Assert.That(runtime.CarrierEdgeCount, Is.EqualTo(1),
                    "Natural speech must accept a stable final token without " +
                    "waiting for silence, Release, or a changed Viseme. " +
                    runtime.DiagnosticTrace);
                Assert.That(runtime.MatchPulseCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void GeneratedAnimator_AcceptsBakedBoundaryObservationUncertainty()
        {
            const int framesPerSecond = 50;
            var phrase = Phrase("late_maximum", new[] { 4, 5, 6 });
            // Production plans already contain the compiler-calibrated
            // observation corridor. Model that baked 20 ms boundary allowance
            // explicitly; the Animator must not invent another hidden grace.
            phrase.variants.Single().states[0].maximumSeconds += 0.02f;
            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, true, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);
                // Entry consumes the first observation. The seventeenth held
                // frame reaches the inclusive baked 0.34 s maximum.
                PlayTokenFrames(runtime, framesPerSecond, 4, 17);
                PlayToken(runtime, framesPerSecond, 5, 0.10f);
                PlayToken(runtime, framesPerSecond, 6, 0.10f);
                ExitUtterance(runtime, framesPerSecond);
                runtime.RunFor(0.45f, framesPerSecond);

                Assert.That(runtime.CarrierEdgeCount, Is.EqualTo(1),
                    "One render-frame crossing is inside the categorical observer's " +
                    "bounded timing uncertainty. " +
                    runtime.DiagnosticTrace + " Updates: " + runtime.DetailedTrace +
                    " Transitions: " + runtime.TransitionSummary("Timed 0_"));
                Assert.That(runtime.MatchPulseCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void GeneratedAnimator_RejectsChatterUntilCommittedLabelChanges()
        {
            const int framesPerSecond = 50;
            var phrase = Phrase("bounded_chatter", new[] { 4, 5, 6 });
            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, true, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);
                PlayTokenFrames(runtime, framesPerSecond, 4, 17);
                // Neither label survives the 30 ms run filter. This used to
                // disable the raw-held timeout forever and let a much later 5
                // advance the stale candidate.
                for (var frame = 0; frame < 10; frame++)
                    PlayTokenFrames(runtime, framesPerSecond,
                        frame % 2 == 0 ? 8 : 9, 1);
                PlayToken(runtime, framesPerSecond, 5, 0.10f);
                PlayToken(runtime, framesPerSecond, 6, 0.10f);
                ExitUtterance(runtime, framesPerSecond);
                runtime.RunFor(0.45f, framesPerSecond);

                Assert.That(runtime.CarrierEdgeCount, Is.Zero,
                    "Sub-30 ms chatter must not extend a timed phone beyond the " +
                    "calibrated maximum or re-arm its stale committed label. " +
                    runtime.DiagnosticTrace +
                    " Updates: " + runtime.DetailedTrace);
                Assert.That(runtime.MatchPulseCount, Is.Zero);
            }
        }

        [Test]
        public void GeneratedAnimator_QuarantineConsumesFreshNaturalRootDirectly()
        {
            const int framesPerSecond = 50;
            var expiredPhrase = Phrase("quarantine_expired", new[] { 4, 9, 10 });
            var targetPhrase = Phrase("quarantine_target", new[] { 5, 6, 7 });
            var plan = new VisemePhraseBuildPlan();
            plan.phrases.Add(expiredPhrase);
            plan.phrases.Add(targetPhrase);
            var built = Build(plan);
            using (var runtime = new AnimatorRuntime(
                       built.controller, true, targetPhrase.matchedParameter,
                       targetPhrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);
                PlayToken(runtime, framesPerSecond, 4, 0.40f);
                // The expired path is quarantined on committed 4. At 50 FPS,
                // committed 5 can be visible for only the evaluation on which
                // raw 6 has already begun probation. It must enter the natural
                // 5-root directly rather than spending that sample in Ready.
                PlayTokenFrames(runtime, framesPerSecond, 5, 2);
                PlayToken(runtime, framesPerSecond, 6, 0.10f);
                PlayToken(runtime, framesPerSecond, 7, 0.16f);
                ExitUtterance(runtime, framesPerSecond);
                runtime.RunFor(0.45f, framesPerSecond);

                Assert.That(runtime.CarrierEdgeCount, Is.EqualTo(1),
                    "Quarantine recovery must not consume the first proven token of a " +
                    "new natural phrase. " + runtime.DiagnosticTrace +
                    " Updates: " + runtime.DetailedTrace);
                Assert.That(runtime.MatchPulseCount, Is.EqualTo(1));
            }
        }

        [TestCase("wrong")]
        [TestCase("reversed")]
        [TestCase("held")]
        public void GeneratedAnimator_RejectsWrongReversedAndHeldEndpointTraces(string scenario)
        {
            const int framesPerSecond = 90;
            var phrase = Phrase("reject", new[] { 4, 5, 6 });
            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, true, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);
                switch (scenario)
                {
                    case "wrong":
                        Play(runtime, framesPerSecond, 1f, 4, 9, 6);
                        break;
                    case "reversed":
                        Play(runtime, framesPerSecond, 1f, 6, 5, 4);
                        break;
                    case "held":
                        // A causal Natural Speech detector fires once its final
                        // token is valid, so a future final hold cannot revoke
                        // that event. Hold the leading endpoint instead; its
                        // learned maximum must expire before the suffix arrives.
                        PlayToken(runtime, framesPerSecond, 4, 0.52f);
                        PlayToken(runtime, framesPerSecond, 5, 0.10f);
                        PlayToken(runtime, framesPerSecond, 6, 0.10f);
                        ExitUtterance(runtime, framesPerSecond);
                        break;
                    default:
                        Assert.Fail("Unknown test scenario " + scenario);
                        break;
                }
                runtime.RunFor(0.45f, framesPerSecond);

                Assert.That(runtime.CarrierEdgeCount, Is.Zero);
                Assert.That(runtime.MatchPulseCount, Is.Zero);
            }
        }

        [Test]
        public void GeneratedAnimator_ProducesOnePulseForEachAcceptedNetworkEdge()
        {
            const int framesPerSecond = 90;
            var phrase = Phrase("repeat", new[] { 4, 5, 6 });
            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, true, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);
                Play(runtime, framesPerSecond, 1f, 4, 5, 6);
                runtime.RunFor(0.45f, framesPerSecond);
                Assert.That(runtime.CarrierEdgeCount, Is.EqualTo(1));
                Assert.That(runtime.MatchPulseCount, Is.EqualTo(1));

                runtime.RunFor(1.35f, framesPerSecond);
                Play(runtime, framesPerSecond, 1f, 4, 5, 6);
                runtime.RunFor(0.45f, framesPerSecond);

                Assert.That(runtime.CarrierEdgeCount, Is.EqualTo(2));
                Assert.That(runtime.MatchPulseCount, Is.EqualTo(2),
                    "Both rising and falling stable carrier edges must yield one pulse each.");
            }
        }

        [Test]
        public void RemoteMatcherStaysIdleWhileNetworkCarrierEdgeStillDecodes()
        {
            const int framesPerSecond = 90;
            var phrase = Phrase("remote", new[] { 4, 5, 6 });
            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, false, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                Warm(runtime, framesPerSecond);
                Play(runtime, framesPerSecond, 1f, 4, 5, 6);
                runtime.RunFor(0.20f, framesPerSecond);

                Assert.That(runtime.StateIn("YUCP Phrase Shared Matcher"),
                    Is.EqualTo("Remote"));
                Assert.That(runtime.StateIn("YUCP Phrase Cooldown remote"),
                    Is.EqualTo("Remote"));
                Assert.That(runtime.CarrierEdgeCount, Is.Zero,
                    "A remote avatar must never run the owner matcher.");
                Assert.That(runtime.MatchPulseCount, Is.Zero);

                runtime.SetBool(phrase.carrierParameter, true);
                runtime.RunFor(0.10f, framesPerSecond);
                Assert.That(runtime.MatchPulseCount, Is.EqualTo(1),
                    "The non-owner edge decoder must react to a received network edge.");
            }
        }

        [Test]
        public void LateJoinCarrierSampleIsSuppressedForTheFullInitializationWindow()
        {
            const int framesPerSecond = 90;
            var phrase = Phrase("late_join", new[] { 4, 5, 6 });
            var built = Build(Plan(phrase));
            using (var runtime = new AnimatorRuntime(
                       built.controller, false, phrase.matchedParameter,
                       phrase.carrierParameter))
            {
                runtime.SetBool(phrase.carrierParameter, true);
                runtime.RunFor(1.20f, framesPerSecond);
                Assert.That(runtime.StateIn("YUCP Phrase Edge late_join"),
                    Is.EqualTo("Initialize without pulse"));
                Assert.That(runtime.MatchPulseCount, Is.Zero);

                runtime.RunFor(0.12f, framesPerSecond);
                Assert.That(runtime.StateIn("YUCP Phrase Edge late_join"),
                    Is.EqualTo("Armed 1"));
                Assert.That(runtime.MatchPulseCount, Is.Zero,
                    "The carrier value sampled on join is state, not an edge.");

                runtime.SetBool(phrase.carrierParameter, false);
                runtime.RunFor(0.10f, framesPerSecond);
                Assert.That(runtime.MatchPulseCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void MaximumModelHasDeterministicBoundedGraphAndRemoteExecutionRemainsDormant()
        {
            var sequences = new[]
            {
                new[] { 1, 5, 9, 13, 2, 6, 10, 14 },
                new[] { 2, 6, 10, 14, 3, 7, 11, 1 },
                new[] { 3, 7, 11, 1, 4, 8, 12, 2 },
                new[] { 4, 8, 12, 2, 5, 9, 13, 3 }
            };
            var plan = new VisemePhraseBuildPlan();
            for (var index = 0; index < sequences.Length; index++)
                plan.phrases.Add(Phrase("max_" + index, sequences[index]));

            Assert.That(VisemePhraseGlobalTrie.TryBuild(
                plan, out _, out var sharedStates, out var error), Is.True, error);
            Assert.That(sharedStates, Is.EqualTo(32),
                "Four eight-run branches exercise the logical shared-state cap.");
            var built = Build(plan);
            var layers = built.controller.layers;
            var totalStates = layers.Sum(layer => layer.stateMachine.states.Length);
            var totalTransitions = layers.Sum(layer =>
                layer.stateMachine.states.Sum(item => item.state.transitions.Length));

            Assert.That(layers.Count(layer =>
                layer.name == "YUCP Phrase Shared Matcher"), Is.EqualTo(1));
            Assert.That(layers.Count(layer => layer.name.StartsWith(
                "YUCP Phrase Cooldown ", StringComparison.Ordinal)), Is.EqualTo(4));
            Assert.That(layers.Count(layer => layer.name.StartsWith(
                "YUCP Phrase Edge ", StringComparison.Ordinal)), Is.EqualTo(4));
            Assert.That(layers.Length, Is.InRange(9, 16),
                "Auxiliary cost/clock layers may evolve, but must remain linearly bounded.");
            Assert.That(totalStates, Is.LessThanOrEqualTo(160));
            Assert.That(totalTransitions, Is.LessThanOrEqualTo(2560),
                "Safe restart rectangles remain statically bounded at the 32-state cap.");
            Assert.That(layers.All(layer =>
                layer.stateMachine.anyStateTransitions.Length == 0), Is.True);

            using (var runtime = new AnimatorRuntime(
                       built.controller, false,
                       plan.phrases[0].matchedParameter,
                       plan.phrases[0].carrierParameter))
            {
                runtime.RunFor(2f, 144);
                runtime.SetFloat("YUCP/TestAVR/Speech/Talking", 1f);
                for (var viseme = 1; viseme <= 14; viseme++)
                {
                    runtime.SetInt("Viseme", viseme);
                    runtime.RunFor(0.04f, 144);
                }

                Assert.That(runtime.StateIn("YUCP Phrase Shared Matcher"),
                    Is.EqualTo("Remote"));
                for (var index = 0; index < 4; index++)
                    Assert.That(runtime.StateIn("YUCP Phrase Cooldown max_" + index),
                        Is.EqualTo("Remote"));
            }
        }

        private VisemePhraseTriggerAnimatorBuilder.Result Build(VisemePhraseBuildPlan plan) =>
            VisemePhraseTriggerAnimatorBuilder.Build(
                new VisemePhraseTriggerAnimatorBuilder.Request
                {
                    controllerPath = generatedFolder + "/Controller.controller",
                    parametersPath = generatedFolder + "/Parameters.asset",
                    plan = plan
                });

        private static VisemePhraseBuildPlan Plan(VisemePhraseBuildPhrase phrase)
        {
            var plan = new VisemePhraseBuildPlan();
            plan.phrases.Add(phrase);
            return plan;
        }

        private static VisemePhraseBuildPhrase Phrase(string key, IEnumerable<int> visemes)
        {
            const string prefix = "YUCP/TestPhrase";
            var phrase = new VisemePhraseBuildPhrase
            {
                ownerKey = "Avatar[0]/" + key,
                prompt = key,
                stableId = "id_" + key,
                parameterKey = key,
                sourcePrefix = "YUCP/TestAVR",
                talkingParameter = "YUCP/TestAVR/Speech/Talking",
                onsetParameter = "YUCP/TestAVR/Speech/Onset",
                releaseParameter = "YUCP/TestAVR/Speech/Release",
                matchedParameter = AdvancedVisemeParameterContract.PhraseMatched(prefix, key),
                confidenceParameter = AdvancedVisemeParameterContract.PhraseConfidence(prefix, key),
                progressParameter = AdvancedVisemeParameterContract.PhraseProgress(prefix, key),
                carrierParameter = AdvancedVisemeParameterContract.PhraseCarrier(
                    prefix, "id_" + key),
                pulseSeconds = 0.25f,
                cooldownSeconds = 1.25f,
                enrollmentFingerprint = "trace_" + key
            };
            var variant = new VisemePhraseBuildVariant { id = "v0" };
            foreach (var viseme in visemes)
                variant.states.Add(new VisemePhraseBuildState
                {
                    aliases = new[] { viseme },
                    minimumSeconds = 0.025f,
                    maximumSeconds = 0.32f,
                    confidence = 0.95f
                });
            phrase.variants.Add(variant);
            return phrase;
        }

        private static VisemePhraseBuildVariant TimedVariant(
            string id,
            IReadOnlyList<int> visemes,
            IReadOnlyList<float> minimums,
            IReadOnlyList<float> maximums,
            bool inferredContextPath = false)
        {
            Assert.That(minimums.Count, Is.EqualTo(visemes.Count));
            Assert.That(maximums.Count, Is.EqualTo(visemes.Count));
            var variant = new VisemePhraseBuildVariant
            {
                id = id,
                inferredContextPath = inferredContextPath,
                canonicalStateCount = visemes.Count,
                minimumTotalSeconds = minimums.Sum(),
                maximumTotalSeconds = maximums.Sum()
            };
            for (var index = 0; index < visemes.Count; index++)
            {
                var emissions = new float[15];
                emissions[visemes[index]] = 1f;
                variant.states.Add(new VisemePhraseBuildState
                {
                    aliases = new[] { visemes[index] },
                    minimumSeconds = minimums[index],
                    maximumSeconds = maximums[index],
                    confidence = 0.95f,
                    emissionLikelihoods = emissions
                });
            }
            return variant;
        }

        private static void Warm(AnimatorRuntime runtime, int framesPerSecond)
        {
            runtime.SetInt("Viseme", 0);
            runtime.SetFloat("YUCP/TestAVR/Speech/Talking", 0f);
            runtime.RunFor(1.40f, framesPerSecond);
        }

        private static void Play(
            AnimatorRuntime runtime,
            int framesPerSecond,
            float durationScale,
            params int[] visemes)
        {
            foreach (var viseme in visemes)
                PlayToken(runtime, framesPerSecond, viseme, 0.10f * durationScale);
            ExitUtterance(runtime, framesPerSecond);
        }

        private static void PlayToken(
            AnimatorRuntime runtime,
            int framesPerSecond,
            int viseme,
            float seconds)
        {
            runtime.SetFloat("YUCP/TestAVR/Speech/Talking", 1f);
            runtime.SetInt("Viseme", viseme);
            runtime.RunFor(seconds, framesPerSecond);
        }

        private static void PlayTokenFrames(
            AnimatorRuntime runtime,
            int framesPerSecond,
            int viseme,
            int frames)
        {
            runtime.SetFloat("YUCP/TestAVR/Speech/Talking", 1f);
            runtime.SetInt("Viseme", viseme);
            runtime.RunFrames(frames, framesPerSecond);
        }

        private static void ExitUtterance(AnimatorRuntime runtime, int framesPerSecond)
        {
            runtime.SetInt("Viseme", 0);
            runtime.SetFloat("YUCP/TestAVR/Speech/Talking", 1f);
            runtime.RunFor(0.06f, framesPerSecond);
            runtime.SetFloat("YUCP/TestAVR/Speech/Talking", 0f);
        }

        private static void EnsureFolder(string path)
        {
            var parts = path.Replace('\\', '/').Split('/');
            var cursor = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = cursor + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cursor, parts[index]);
                cursor = next;
            }
        }

        private sealed class AnimatorRuntime : IDisposable
        {
            private readonly AnimatorController controller;
            private readonly GameObject root;
            private readonly Animator animator;
            private readonly bool executeLocalDrivers;
            private readonly string matchedParameter;
            private readonly string carrierParameter;
            private readonly Dictionary<string, AnimatorControllerParameterType> parameterTypes;
            private readonly Dictionary<int, Dictionary<int, AnimatorState>> statesByLayer;
            private readonly int[] lastEnteredState;
            private readonly List<string> matcherTrace = new List<string>();
            private readonly List<string> detailedTrace = new List<string>();
            private readonly HashSet<int> committedVisemes = new HashSet<int>();
            private bool lastMatched;
            private bool lastCarrier;
            private int lastMatcherHash = int.MinValue;
            private float elapsedSeconds;

            internal AnimatorRuntime(
                AnimatorController controller,
                bool executeLocalDrivers,
                string matchedParameter,
                string carrierParameter)
            {
                this.controller = controller;
                this.executeLocalDrivers = executeLocalDrivers;
                this.matchedParameter = matchedParameter;
                this.carrierParameter = carrierParameter;
                parameterTypes = controller.parameters.ToDictionary(
                    parameter => parameter.name,
                    parameter => parameter.type,
                    StringComparer.Ordinal);
                statesByLayer = controller.layers
                    .Select((layer, index) => new { layer, index })
                    .ToDictionary(
                        item => item.index,
                        item => item.layer.stateMachine.states
                            .Select(child => child.state)
                            .ToDictionary(state => Animator.StringToHash(state.name)));
                lastEnteredState = Enumerable.Repeat(int.MinValue, controller.layers.Length)
                    .ToArray();
                root = new GameObject("YUCP Phrase Animator execution test");
                animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.Update(0f);
                SetFloat("IsLocal", executeLocalDrivers ? 1f : 0f);
                ApplyEnteredDrivers();
                committedVisemes.Add(StableVisemeValue);
                lastMatched = animator.GetBool(matchedParameter);
                lastCarrier = animator.GetBool(carrierParameter);
            }

            internal int MatchPulseCount { get; private set; }
            internal int CarrierEdgeCount { get; private set; }
            internal string DiagnosticTrace => string.Join(" | ", matcherTrace);
            internal string DetailedTrace => string.Join(" | ", detailedTrace);
            internal bool HasCommittedViseme(int viseme) =>
                committedVisemes.Contains(viseme);

            internal void SetFloat(string parameter, float value) =>
                animator.SetFloat(parameter, value);

            internal void SetInt(string parameter, int value) =>
                animator.SetInteger(parameter, value);

            internal void SetBool(string parameter, bool value) =>
                animator.SetBool(parameter, value);

            internal void RunFor(float seconds, int framesPerSecond)
            {
                var deltaTime = 1f / framesPerSecond;
                var frames = Math.Max(1, Mathf.CeilToInt(seconds / deltaTime));
                RunFrames(frames, framesPerSecond);
            }

            internal void RunFrames(int frames, int framesPerSecond)
            {
                var deltaTime = 1f / framesPerSecond;
                for (var frame = 0; frame < frames; frame++) Step(deltaTime);
            }

            internal string TransitionSummary(string statePrefix)
            {
                var layer = controller.layers.Single(item =>
                    item.name == "YUCP Phrase Shared Matcher");
                return string.Join(" | ", layer.stateMachine.states
                    .Select(item => item.state)
                    .Where(state => state.name.StartsWith(
                        statePrefix, StringComparison.Ordinal))
                    .OrderBy(state => state.name, StringComparer.Ordinal)
                    .SelectMany(state => state.transitions.Select((transition, index) =>
                        state.name + "#" + index + " exit=" +
                        (transition.hasExitTime
                            ? transition.exitTime.ToString("F6")
                            : "immediate") + " -> " +
                        (transition.destinationState != null
                            ? transition.destinationState.name
                            : "<none>") + " if " +
                        string.Join(",", transition.conditions.Select(condition =>
                            condition.parameter + ":" + condition.mode + ":" +
                            condition.threshold.ToString("F3"))))));
            }

            internal string StateIn(string layerName)
            {
                var layer = Array.FindIndex(controller.layers,
                    layer => string.Equals(layer.name, layerName, StringComparison.Ordinal));
                Assert.That(layer, Is.GreaterThanOrEqualTo(0), layerName);
                var stateHash = EffectiveStateInfo(layer).shortNameHash;
                return statesByLayer[layer].TryGetValue(stateHash, out var state)
                    ? state.name
                    : "<unknown:" + stateHash + ">";
            }

            public void Dispose()
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }

            private void Step(float deltaTime)
            {
                var matcherLayer = Array.FindIndex(controller.layers,
                    item => item.name == "YUCP Phrase Shared Matcher");
                var before = matcherLayer >= 0
                    ? EffectiveStateInfo(matcherLayer)
                    : default;
                var beforeName = matcherLayer >= 0
                    ? StateName(matcherLayer, before.shortNameHash)
                    : "<none>";
                var rawBefore = animator.GetInteger("Viseme");
                var stableBefore = StableVisemeValue;
                var start = elapsedSeconds;
                elapsedSeconds += deltaTime;
                animator.Update(deltaTime);
                ApplyEnteredDrivers();
                committedVisemes.Add(StableVisemeValue);
                if (matcherLayer >= 0)
                {
                    var after = EffectiveStateInfo(matcherLayer);
                    detailedTrace.Add(start.ToString("F3") + "->" +
                                      elapsedSeconds.ToString("F3") + " raw=" +
                                      rawBefore + "/" + animator.GetInteger("Viseme") + " " +
                                      "stable=" + stableBefore + "/" +
                                      StableVisemeValue + " " +
                                      beforeName + "@" + before.normalizedTime.ToString("F4") +
                                      " -> " + StateName(matcherLayer, after.shortNameHash) +
                                      "@" + after.normalizedTime.ToString("F4"));
                    if (detailedTrace.Count > 256) detailedTrace.RemoveAt(0);
                }
                CaptureMatcherTrace();
                ObserveEdges();
            }

            private string StateName(int layer, int stateHash) =>
                statesByLayer[layer].TryGetValue(stateHash, out var state)
                    ? state.name
                    : "<unknown:" + stateHash + ">";

            private void CaptureMatcherTrace()
            {
                var layer = Array.FindIndex(controller.layers,
                    item => item.name == "YUCP Phrase Shared Matcher");
                if (layer < 0) return;
                var stateInfo = EffectiveStateInfo(layer);
                if (stateInfo.shortNameHash == lastMatcherHash) return;
                lastMatcherHash = stateInfo.shortNameHash;
                var state = statesByLayer[layer].TryGetValue(
                    stateInfo.shortNameHash, out var resolved)
                    ? resolved.name
                    : "<unknown>";
                matcherTrace.Add(elapsedSeconds.ToString("F3") + "s " + state +
                                 " viseme=" + animator.GetInteger("Viseme") +
                                 " stable=" + StableVisemeValue +
                                 " talking=" + animator.GetFloat(
                                     "YUCP/TestAVR/Speech/Talking").ToString("F2") +
                                 " ready=" + animator.GetFloat(
                                     "__YUCP_Phrase_CooldownReady_0").ToString("F1"));
                if (matcherTrace.Count > 32) matcherTrace.RemoveAt(0);
            }

            private void ApplyEnteredDrivers()
            {
                for (var layer = 0; layer < controller.layers.Length; layer++)
                {
                    var stateHash = EffectiveStateInfo(layer).shortNameHash;
                    if (stateHash == 0 || stateHash == lastEnteredState[layer]) continue;
                    lastEnteredState[layer] = stateHash;
                    if (!statesByLayer[layer].TryGetValue(stateHash, out var state)) continue;
                    foreach (var driver in state.behaviours.OfType<VRCAvatarParameterDriver>())
                    {
                        if (!driver.isEnabled || driver.localOnly && !executeLocalDrivers) continue;
                        foreach (var parameter in driver.parameters)
                        {
                            if (parameter.type == VRC_AvatarParameterDriver.ChangeType.Set)
                            {
                                Set(parameter.name, parameter.value);
                                continue;
                            }
                            if (parameter.type != VRC_AvatarParameterDriver.ChangeType.Copy)
                                continue;
                            Set(parameter.name, Get(parameter.source));
                        }
                    }
                }
            }

            private int StableVisemeValue => Mathf.RoundToInt(animator.GetFloat(
                "__YUCP_Phrase_StableViseme"));

            private float Get(string parameter)
            {
                var definition = controller.parameters.FirstOrDefault(item =>
                    string.Equals(item.name, parameter, StringComparison.Ordinal));
                if (definition == null) return 0f;
                switch (definition.type)
                {
                    case AnimatorControllerParameterType.Int:
                        return animator.GetInteger(parameter);
                    case AnimatorControllerParameterType.Bool:
                        return animator.GetBool(parameter) ? 1f : 0f;
                    default:
                        return animator.GetFloat(parameter);
                }
            }

            private AnimatorStateInfo EffectiveStateInfo(int layer) =>
                animator.IsInTransition(layer)
                    ? animator.GetNextAnimatorStateInfo(layer)
                    : animator.GetCurrentAnimatorStateInfo(layer);

            private void Set(string parameter, float value)
            {
                if (!parameterTypes.TryGetValue(parameter, out var type))
                    Assert.Fail("Parameter driver referenced undeclared parameter " + parameter);
                switch (type)
                {
                    case AnimatorControllerParameterType.Bool:
                    case AnimatorControllerParameterType.Trigger:
                        animator.SetBool(parameter, value > 0.5f);
                        break;
                    case AnimatorControllerParameterType.Int:
                        animator.SetInteger(parameter, Mathf.RoundToInt(value));
                        break;
                    default:
                        animator.SetFloat(parameter, value);
                        break;
                }
            }

            private void ObserveEdges()
            {
                var matched = animator.GetBool(matchedParameter);
                var carrier = animator.GetBool(carrierParameter);
                if (matched && !lastMatched) MatchPulseCount++;
                if (carrier != lastCarrier) CarrierEdgeCount++;
                lastMatched = matched;
                lastCarrier = carrier;
            }
        }
    }
}
#endif
