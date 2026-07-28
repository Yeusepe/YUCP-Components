using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor
{
    internal static class AdvancedVisemeAnimatorBuilder
    {
        // Test-only A/B seam. Production must leave this at its default false.
        // Retention pull kill-switch: the additive pipeline is mid-surgery
        // (transient renormalizer spike at switches reads as a visible pop).
        // Keep OFF for avatar builds until DirectRender contracts are green.
        internal static bool EnableRetentionPull = false;
        // Voice-conditioned target: the only continuous per-frame input we
        // receive. Measured on a real capture it cuts RMSE ~32% and brings the
        // park-then-jump speed ratio from 5.82 to ~1.97 (teacher 1.91).
        internal static bool EnableVoiceResponse = false;
        // Density-halo reconstruction: the argmax is a naturally dithered
        // 1-of-15 quantiser, so a higher-order low-pass of the one-hot recovers
        // the weights (sigma-delta decoding). Uses the refitted matrix and two
        // extra observer poles.
        // Superseded by the fusion path (plain sharpened observer + hold
        // generator), which matches the teacher's transition shape without the
        // halo's extra latency. Left toggleable for comparison.
        // Drive the lower-face mouth from the blended reconstructed simplex
        // (C.p over all 15 weights) instead of the argmax decoder's per-winner
        // constant (C.e_winner). The decoder path snaps between single-viseme
        // poses and never co-activates; the blended path is what native
        // VisemeBlendShape lip-sync does. This is the signal every simplex-side
        // stage was tuning, which the mouth had been bypassing.
        internal static bool DriveMouthFromBlendedSimplex = true;
        internal static bool EnableDensityHalo = false;
        // Syllabic-rate target: the voice LEVEL measures worse than the frozen
        // baseline, but the RATE of its syllabic band is the one every-frame
        // quantity in the channel that both keeps moving between switches and
        // stays phase-locked to the audio. Held out offline it takes reversals
        // 100.6 -> 170.4 (teacher 174.6) and still% 9.3 -> 0.4 while improving
        // RMSE.
        //
        internal static bool EnableSyllabicResponse = false;
        // Fusion hold generator: a FitzHugh-Nagumo relaxation bank, one unit per
        // channel, biased per channel by that channel's target so high-target
        // channels oscillate and low-target ones stay subthreshold (this bias is
        // what desynchronizes the bank; a shared bias synchronizes and cancels
        // to zero under the zero-sum projection). Gated to holds. This is the one
        // stage that supplies endogenous motion during a sustained viseme, where
        // the argmax is constant and every target-driven path necessarily
        // freezes. Chosen by a scored comparison against 39 alternatives on a
        // statistic-matching scoreboard (distance 0.167 vs 0.237 for the best
        // noise method; reversals 195 vs 266). Deterministic, no RNG.
        // On at low amplitude: this was the best-judged state ("wait a lot
        // better"). Higher amplitude flaps, so keep it subtle.
        internal static bool EnableFusionHoldGenerator = false;
        // The sharpen + slow-tau envelope was measured to introduce a ~4-frame
        // dead-time on the incoming viseme (the sharpen's renormalization
        // suppresses the rising channel until it crosses over the still-high
        // outgoing one). The plain observer has good transitions, so the fusion
        // ENVELOPE is off by default and only the generator rides on top of it.
        internal static bool EnableFusionEnvelope = false;
        // Switch-triggered feedforward kick: on the frame the argmax changes, step
        // the incoming channel's target up and the outgoing channel's down, then
        // let a fast pole carry them. This is the causal substitute for the
        // teacher's anticipatory overlap (which is not predictable from the
        // channel): the incoming viseme is present AT the switch instead of
        // crawling up over ~100 ms, which is what reads as discrete. Two-sided so
        // mass is conserved and co-activation does not blow up.
        // Off: in-graph the kick decays across the render-to-analysis sampling
        // (and may hit the graph's op-ordering) before it lands, so it added
        // reversals without raising the incoming-at-switch. Kept for a later,
        // ordering-robust rebuild; the dead-time removal is the shippable win.
        internal static bool EnableFusionKick = false;
        internal static float FusionKickAmp = 0.20f;
        internal static float FusionKickSeconds = 0.03f;
        // Conservative for the first live look: holds move without leaning into
        // the over-oscillation the replay harness measures (which is partly a
        // fixed-timestep artifact and partly real). Raise toward 0.08 if the
        // sustained visemes still read as too still on a real avatar.
        internal static float FusionHoldAmp = 0.04f;
        internal static float SpeechRenderLeadCap = 1.0f;
        // Mouth slew-rate limit. The raw Viseme int is an impulse train (flat,
        // then a full viseme swap), so exponential smoothing gives park-then-lurch
        // (piecewise-constant, the staircasing theorem). A slew limiter caps the
        // per-frame step, producing piecewise-LINEAR (constant-velocity) motion —
        // the class humans read as smooth/deliberate — and rejects the argmax's
        // sub-step chatter for free. Applied to mouth (jaw/lip) articulator POSES
        // only (signed, no simplex sum contract; tongue untouched). Speed in pose
        // units/sec; swept in-graph. See avr-lower-mouth-pivot memory.
        internal static bool EnableMouthSlew = false;
        internal static float MouthSlewSpeed = 2.3f;
        // Lookahead FIR (dominance model). The argmax stream is an impulse train;
        // no causal estimator reaches the teacher's motion UNIFORMITY (p99/p50
        // speed ratio: teacher 1.95, every causal filter >= 3.6 at any capacity,
        // shipped 5.82). A linear filter over the argmax one-hot with a few frames
        // of LOOKAHEAD hits 2.33 — anticipatory coarticulation, bought with
        // latency. Realized as a gamma memory (cascade of the sanctioned one-pole
        // Smooth, so nothing new can destabilize) whose readout predicts the
        // simplex delayed by LookaheadFrames; the latency is inherent in the
        // target, so no delay-line mechanism is needed. Mouth articulators only.
        internal static bool EnableLookaheadFir = true;
        // Asymmetric release on the mouth's viseme weights: rise fast (crisp
        // onset) but fall slow, so the outgoing viseme lingers and overlaps the
        // incoming one. Without this, each viseme rises and fully falls before
        // the next rises, so the mouth returns to rest between visemes ("goes and
        // comes back") instead of blending A->B. Slow release raises the
        // co-activation toward the teacher's.
        // Off: the recurrent state update is unstable on this substrate (the
        // graph does not guarantee the read-before-commit ordering it needs),
        // so it jittered the mouth worse than the hold it was meant to smooth.
        internal static bool EnableAsymmetricRelease = false;
        internal static float ReleaseAttackSeconds = 0.012f;
        internal static float ReleaseFallSeconds = 0.090f;
        // Envelope temperature: the per-index target is sharpened (raised to this
        // power, renormalized) before the observer so co-activation and peakedness
        // match the teacher. 1.0 disables sharpening.
        internal static float FusionEnvelopeGamma = 1.45f;
        // The fusion envelope is a plain two-pole observer at this time constant
        // (density halo off), which offline matches the teacher on transient
        // motion, between-switch motion, and co-activation. The hold generator
        // supplies the sustained-segment motion the observer cannot.
        internal static float FusionEnvelopeSeconds = 0.035f;
        // Measured in-graph against the teacher: this lands still% at 2.36
        // (teacher 3.07) and reversals at 144.6 (teacher 160.8). Reversals are
        // deliberately left UNDER the teacher because too-still reads as a
        // note while too-bouncy reads as broken, and the slopes are fitted
        // against the rate the graph measures, so raising this past ~1 is
        // pushing past the fit rather than scaling within it.
        internal static float SyllabicResponseGain = 0.4f;
        // The rectifier curves saturate here. Measured p99 |rate| is 1.68, so
        // this clips only the extreme transients; raising it costs resolution
        // across the range that carries the motion.
        internal const float SyllabicRateClamp = 2.5f;
        // Measurement hook: forces the viseme observer time constant past the
        // halo's paired value so a sweep actually varies something. Zero means
        // "use the normal resolution order".
        internal static float ObserverResponseOverride;
        internal static float VoiceResponseGain = 1f;
        internal static bool DisableInvariantTrackingBranchGatingForTests;
        internal static bool UseSingleInvariantTrackingObserverForTests;
        internal static bool UseSwitchedRetentionRowObserverForTests;
        internal static bool UseFactoredPrimarySilenceObserverForTests;
        internal static bool UseFactoredRetentionSilenceObserversForTests;
        internal static bool RunPostFoldOptimizerFixpointForTests;
        internal static bool UseUnconditionalSignedBindingProofForTests;
        internal static bool UseLegacyMapBatchAssetCriterionForTests;
        internal static bool UseLegacyVisibleTongueProductGraphForTests;
        internal static bool UseCollapsedVisibleTongueKernelForTests;
        internal static bool EnableConditionalLearnedDetailSleepForTests;
        internal static bool UseImmediateConditionalLearnedDetailAuthorityForTests;
        internal static bool UseModelMatchedConditionalLearnedDetailReadinessForTests;
        internal static bool KeepConditionalBetaContextAlwaysHotForTests;
        internal static bool UseBalancedNeutralSupportReductionForTests;
        internal static bool DisableOculusHaloForTests;

        internal const float ConditionalLearnedDetailLowFpsBypassFrameSeconds =
            0.5f * (1f / 24f + 1f / 25f);
        internal const float ConditionalLearnedDetailLowFpsTransitionSeconds =
            0.000001f;
        internal const float ConditionalLearnedDetailStartupHotSeconds = 0.1f;
        internal const float ConditionalLearnedDetailStartupTransitionSeconds =
            0.000001f;
        internal const float ConditionalLearnedDetailWarmthSeconds = 0.09f;
        internal const float ConditionalLearnedDetailAuthorityStart = 0.55f;
        internal const float ConditionalLearnedDetailAuthorityFull = 0.95f;
        internal const float ConditionalLearnedDetailReadinessStart = 0.25f;
        internal const float ConditionalLearnedDetailReadinessFull = 0.99f;
        // This duration is part of the fitted direct-render contract. Keeping it
        // in the generated model prevents the offline Unity-transition replay
        // and the emitted controller from silently drifting apart.
        // Motion-metric fit on the corpus (fit_transition_motion.py, held-out):
        // the decoder's interruptible target cross-fade is what spreads
        // displacement across the dwell instead of parking between switches.
        // Durations below ~107 ms leave most of a typical 142 ms dwell static,
        // which is the stair-step the observer alone cannot remove. At 128 ms
        // the reconstruction's park-then-jump ratio (p99/p50 speed) is 5.43
        // against the original continuous weights' 5.38, and median frame
        // speed rises from 0.073 to 0.172 (original 0.195). The learned
        // trajectory model's own TargetCrossfadeSeconds stays available for
        // the offline replay contract.
        internal const float VisemeTargetCrossfadeSeconds = 0.0f;

        private const int MaxRuntimeAwareMapBatchStoredBindings = 256;
        private const int MaxExtraMapBindingsPerCollapsedTree = 32;
        // The fast speech path is needed only at the exact no-tracker endpoint.
        // Above one 8-bit step, render the already fused/constrained articulation
        // directly so tracking authority is never applied twice.
        private const float PhysicalTrackingHandoffLow = 1f / 255f;
        private const float PhysicalTrackingHandoffHigh = 2f / 255f;

        internal static bool MapBatchPreservesActiveBindingBound(
            IReadOnlyList<int> separatePointCounts,
            int combinedThresholdCount,
            int combinedOutputCount)
        {
            if (separatePointCounts == null || combinedThresholdCount <= 0 ||
                combinedOutputCount <= 0)
                return false;
            var before = separatePointCounts.Sum(pointCount =>
                Math.Min(2, Math.Max(0, pointCount)));
            var after = Math.Min(2, combinedThresholdCount) * combinedOutputCount;
            return after <= before;
        }

        private static float[][][] retentionPullFoldedWeights;

        /// <summary>
        /// Decoder trajectory control values with the (cur, age)-only pull
        /// remainder folded in and retracted onto the simplex (clamp at zero,
        /// renormalize), so baked rows stay positive and sum to one exactly.
        /// Trajectory-static winners (sil) pass through unfolded. Shared with
        /// the trajectory contract tests so builder and tests cannot drift.
        /// </summary>
        internal static float RetentionPullFoldedDecoderWeight(
            int winner,
            int control,
            int output)
        {
            if (!EnableRetentionPull)
                return AdvancedVisemeOculusDynamics.Weight(
                    winner, control, output);
            if (retentionPullFoldedWeights == null)
            {
                var decay = RetentionPullDecayAtControls();
                var count = VisemeReconstructionProfile.VisemeCount;
                var table = new float[count][][];
                for (var w = 0; w < count; w++)
                {
                    table[w] = new float[decay.Length][];
                    var dynamic =
                        AdvancedVisemeOculusDynamics.HasDynamicTrajectory(w);
                    for (var k = 0; k < decay.Length; k++)
                    {
                        var row = new float[count];
                        var sum = 0f;
                        for (var o = 0; o < count; o++)
                        {
                            var value = AdvancedVisemeOculusDynamics.Weight(
                                w, k, o);
                            if (dynamic)
                                value += decay[k] *
                                         AdvancedVisemeRetentionPull
                                             .FoldedCurrentCorrection(w, o);
                            row[o] = Mathf.Max(0f, value);
                            sum += row[o];
                        }
                        if (sum > 1e-6f)
                            for (var o = 0; o < count; o++)
                                row[o] /= sum;
                        table[w][k] = row;
                    }
                }
                retentionPullFoldedWeights = table;
            }
            return retentionPullFoldedWeights[winner][control][output];
        }

        private struct FusionVectors
        {
            internal string[] fast;
            internal string[] slow;
        }

        /// <summary>
        /// Applies a per-channel asymmetric release: each weight rises with a
        /// fast attack pole and falls with a slow release pole. The outgoing
        /// viseme therefore lingers and overlaps the incoming one, so the mouth
        /// blends A->B instead of returning to rest between visemes. Signed
        /// deltas stay within +/-1, well inside the +/-2 passthrough clamp.
        /// </summary>
        private static string[] AppendAsymmetricRelease(
            MathGraph graph, BlendTree root, string frameTime,
            string[] weights, string tag)
        {
            var count = weights.Length;
            var alphaUp = graph.Param($"Release/{tag}/AlphaUp", 0.5f);
            var alphaDown = graph.Param($"Release/{tag}/AlphaDown", 0.5f);
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, alphaUp, ReleaseAttackSeconds));
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, alphaDown, ReleaseFallSeconds));
            var outState = new string[count];
            for (var i = 0; i < count; i++)
            {
                var state = graph.Param($"Release/{tag}/State/{i}",
                    i == 0 ? 1f : 0f);
                // delta = target - state (signed, in [-1,1])
                var delta = graph.Param($"Release/{tag}/Delta/{i}", 0f);
                graph.AddOperation(root, graph.Linear(delta, new[]
                {
                    Term.Signed(weights[i], 1f), Term.Signed(state, -1f)
                }));
                // deltaPos = max(delta, 0) (the rising part)
                var deltaPos = graph.Param($"Release/{tag}/Up/{i}", 0f);
                graph.AddOperation(root, graph.Map(delta, deltaPos, new[]
                {
                    Point(-2f, 0f), Point(0f, 0f), Point(2f, 2f)
                }));
                // riseTerm = alphaUp * deltaPos, fallTerm = alphaDown*(delta-deltaPos)
                var riseTerm = graph.Param($"Release/{tag}/Rise/{i}", 0f);
                graph.AddOperation(root, graph.Multiply(alphaUp, deltaPos, riseTerm, false));
                var deltaNeg = graph.Param($"Release/{tag}/Down/{i}", 0f);
                graph.AddOperation(root, graph.Linear(deltaNeg, new[]
                {
                    Term.Signed(delta, 1f), Term.Signed(deltaPos, -1f)
                }));
                var fallTerm = graph.Param($"Release/{tag}/Fall/{i}", 0f);
                graph.AddOperation(root, graph.Multiply(alphaDown, deltaNeg, fallTerm, true));
                var next = graph.Param($"Release/{tag}/Next/{i}", i == 0 ? 1f : 0f);
                graph.AddOperation(root, graph.Linear(next, new[]
                {
                    Term.Signed(state, 1f), Term.Signed(riseTerm, 1f),
                    Term.Signed(fallTerm, 1f)
                }));
                outState[i] = next;
                graph.AddOperation(root, graph.Copy(next, state, true));
            }
            return outState;
        }

        /// <summary>
        /// Appends the hold generator: a bank of undamped 2x2 rotation
        /// oscillators, one per channel, spread across 4-8 Hz. Every
        /// target-driven path freezes during a sustained viseme because the
        /// argmax is constant; a rotation has no equilibrium, so it keeps moving
        /// there. Frequency spread plus staggered initial phase desynchronizes
        /// the bank (a synchronized bank cancels under the zero-sum projection);
        /// each channel is masked by whether its own target is active, so quiet
        /// channels do not ring. The oscillation is zero-sum, gated to holds,
        /// added to the reconstructed simplex, then renormalized.
        ///
        /// Why a rotation and not FitzHugh-Nagumo (which scored better offline):
        /// FHN needs signed state out to +/-2.7, but every signed passthrough on
        /// this substrate clamps at +/-2, which decapitates the cubic's restoring
        /// force and collapses the relaxation cycle into a square wave. The
        /// rotation state stays within +/-1 by construction, so it transfers
        /// exactly. The angle step tracks FrameTime, making it render-rate
        /// independent, and the update is symplectic (area-preserving) so the
        /// amplitude neither grows nor decays.
        /// </summary>
        private static FusionVectors AppendFusionHoldGenerator(
            MathGraph graph,
            BlendTree root,
            string frameTime,
            string visemeIndex,
            string[] bias,
            string[] fastIn,
            string[] slowIn)
        {
            const int count = VisemeReconstructionProfile.VisemeCount;
            const float analysisPeriod = 1024f / 48000f;
            var gain = FusionHoldAmp;

            // Phase gate: frames-since-switch ramp, reset by an index-change pulse.
            // The index (0..14) is scaled to [0,1] first: a signed passthrough
            // clamps at +/-2, so a raw index difference cannot be formed and the
            // switch would never be detected for indices above 2.
            const float indexScale = 1f / (count - 1);
            var indexScaled = graph.Param("Fusion/IndexScaled", 0f);
            graph.AddOperation(root, graph.Linear(indexScaled, new[]
            {
                Term.Positive(visemeIndex, indexScale)
            }));
            var lastIndex = graph.Param("Fusion/LastIndex", 0f);
            var indexDelta = graph.Param("Fusion/IndexDelta", 0f);
            graph.AddOperation(root, graph.Linear(indexDelta, new[]
            {
                Term.Signed(indexScaled, 1f), Term.Signed(lastIndex, -1f)
            }));
            var switchPulse = graph.Param("Fusion/SwitchPulse", 0f);
            // one index step is indexScale ~= 0.071; a threshold at 0.02 fires
            // on any change and stays at 1 out to the full range.
            graph.AddOperation(root, graph.Map(indexDelta, switchPulse, new[]
            {
                Point(-1f, 1f), Point(-0.02f, 1f), Point(0f, 0f),
                Point(0.02f, 1f), Point(1f, 1f)
            }));
            var keep = graph.Param("Fusion/Keep", 0f); // 1 - pulse
            graph.AddOperation(root, graph.Map(switchPulse, keep, new[]
            {
                Point(0f, 1f), Point(1f, 0f)
            }));
            var phaseInc = graph.Param("Fusion/PhaseInc", 0f); // phase + FrameTime
            var phase = graph.Param("Fusion/Phase", 0f);
            graph.AddOperation(root, graph.Linear(phaseInc, new[]
            {
                Term.Positive(phase, 1f), Term.Positive(frameTime, 1f)
            }));
            var phaseNext = graph.Param("Fusion/PhaseNext", 0f);
            graph.AddOperation(root, graph.Multiply(keep, phaseInc, phaseNext, false));
            var gate = graph.Param("Fusion/Gate", 0f);
            // clip((phase - 2 frames) / 3 frames, 0, 1)
            var t0 = 2f * analysisPeriod;
            var t1 = 5f * analysisPeriod;
            graph.AddOperation(root, graph.Map(phase, gate, new[]
            {
                Point(0f, 0f), Point(t0, 0f), Point(t1, 1f), Point(10f, 1f)
            }));

            // Phase-driven sinusoid bank. Each channel's oscillation is a fixed
            // sine of the frames-since-switch ramp at a distinct 4-8 Hz frequency
            // and staggered phase, evaluated as one piecewise-linear map. This is
            // stateless: bounded to +/-1 by construction, so it cannot diverge
            // the way a free-running recurrent oscillator does on a substrate
            // whose signed passthrough clamps at +/-2. The frequency spread
            // desynchronizes the bank; each channel is masked by whether its own
            // target is active so quiet channels do not ring.
            const int knots = 25;
            const float sineSpan = 0.5f;   // seconds of phase the table covers
            var oscMasked = new string[count];
            for (var i = 0; i < count; i++)
            {
                var freq = 4f + 4f * i / (count - 1);         // 4..8 Hz
                var phase0 = 2f * Mathf.PI * i / count;
                var sinePoints = new (float, float)[knots];
                for (var kk = 0; kk < knots; kk++)
                {
                    var ph = sineSpan * kk / (knots - 1);
                    sinePoints[kk] = (ph,
                        Mathf.Sin(2f * Mathf.PI * freq * ph + phase0));
                }
                var sine = graph.Param($"Fusion/Sine/{i}", Mathf.Sin(phase0));
                graph.AddOperation(root, graph.Map(phase, sine,
                    sinePoints.Select(p => Point(p.Item1, p.Item2)).ToArray()));
                // active mask: 1 where this channel's target is meaningfully on.
                var active = graph.Param($"Fusion/Active/{i}", 0f);
                graph.AddOperation(root, graph.Map(bias[i], active, new[]
                {
                    Point(0f, 0f), Point(0.04f, 0f), Point(0.06f, 1f), Point(1f, 1f)
                }));
                oscMasked[i] = graph.Param($"Fusion/Masked/{i}", 0f);
                graph.AddOperation(root, graph.Multiply(active, sine, oscMasked[i], true));
            }

            // Zero-sum the oscillation: osc = masked - mean(masked). Sum-zero
            // keeps the correction on the simplex.
            var meanV = graph.Param("Fusion/MeanV", 0f);
            graph.AddOperation(root, graph.Linear(meanV,
                oscMasked.Select(x => Term.Signed(x, 1f / count)).ToArray()));

            // Switch-triggered feedforward kick. On the switch frame, step the
            // incoming channel up and the outgoing channel down, then let each
            // decay with a fast pole. This puts the incoming viseme present AT the
            // switch (the teacher has it at ~0.37; the crawl-up otherwise leaves it
            // at the ~0.07 floor, which reads as discrete). Two-sided so the
            // simplex mass is preserved. All values stay within +/-0.3.
            var kickState = new string[count];
            var kickNext = new string[count];
            if (EnableFusionKick)
            {
                var alphaKick = graph.Param("Fusion/AlphaKick", 0.5f);
                graph.AddOperation(root, graph.AlphaFromDeltaTime(
                    frameTime, alphaKick, FusionKickSeconds));
                var oneMinusAlpha = graph.Param("Fusion/KickRetain", 0f);
                graph.AddOperation(root, graph.Map(alphaKick, oneMinusAlpha, new[]
                {
                    Point(0f, 1f), Point(1f, 0f)
                }));
                var kickScale = FusionKickAmp;
                for (var i = 0; i < count; i++)
                {
                    var idxScale = 1f / (count - 1);
                    var centre = i * idxScale;
                    // one-hot of the current and previous winner from the scaled
                    // index; the triangle is 1 at this channel's index and 0 at
                    // its neighbours (0.5*idxScale half-width < one index step).
                    var hotIn = graph.Param($"Fusion/HotIn/{i}", i == 0 ? 1f : 0f);
                    graph.AddOperation(root, graph.Map(indexScaled, hotIn, new[]
                    {
                        Point(centre - 0.5f * idxScale, 0f), Point(centre, 1f),
                        Point(centre + 0.5f * idxScale, 0f)
                    }));
                    var hotOut = graph.Param($"Fusion/HotOut/{i}", i == 0 ? 1f : 0f);
                    graph.AddOperation(root, graph.Map(lastIndex, hotOut, new[]
                    {
                        Point(centre - 0.5f * idxScale, 0f), Point(centre, 1f),
                        Point(centre + 0.5f * idxScale, 0f)
                    }));
                    // target of the impulse: +kick on the incoming, -kick on the
                    // outgoing. Injected only on the switch frame; otherwise the
                    // previous kick state decays.
                    var kickTarget = graph.Param($"Fusion/KickTarget/{i}", 0f);
                    graph.AddOperation(root, graph.Linear(kickTarget, new[]
                    {
                        Term.Signed(hotIn, kickScale), Term.Signed(hotOut, -kickScale)
                    }));
                    kickState[i] = graph.Param($"Fusion/Kick/{i}", 0f);
                    var decayed = graph.Param($"Fusion/KickDecay/{i}", 0f);
                    graph.AddOperation(root, graph.Multiply(
                        oneMinusAlpha, kickState[i], decayed, true));
                    var kept = graph.Param($"Fusion/KickKept/{i}", 0f);
                    graph.AddOperation(root, graph.Multiply(keep, decayed, kept, true));
                    var injected = graph.Param($"Fusion/KickInj/{i}", 0f);
                    graph.AddOperation(root, graph.Multiply(
                        switchPulse, kickTarget, injected, true));
                    kickNext[i] = graph.Param($"Fusion/KickNext/{i}", 0f);
                    graph.AddOperation(root, graph.Linear(kickNext[i], new[]
                    {
                        Term.Signed(kept, 1f), Term.Signed(injected, 1f)
                    }));
                }
            }

            var fastOut = new string[count];
            var slowOut = new string[count];
            var fastRaw = new string[count];
            var slowRaw = new string[count];
            for (var i = 0; i < count; i++)
            {
                var osc = graph.Param($"Fusion/Osc/{i}", 0f);
                graph.AddOperation(root, graph.Linear(osc, new[]
                {
                    Term.Signed(oscMasked[i], 1f), Term.Signed(meanV, -1f)
                }));
                // gated oscillation = gate * osc (gate in [0,1], osc signed)
                var gated = graph.Param($"Fusion/Gated/{i}", 0f);
                graph.AddOperation(root, graph.Multiply(gate, osc, gated, true));

                var kickTerm = EnableFusionKick
                    ? new[] { Term.Signed(kickNext[i], 1f) }
                    : System.Array.Empty<Term>();
                fastRaw[i] = graph.Param($"Fusion/FastRaw/{i}", i == 0 ? 1f : 0f);
                graph.AddOperation(root, graph.Linear(fastRaw[i], new[]
                {
                    Term.Positive(fastIn[i], 1f), Term.Signed(gated, gain)
                }.Concat(kickTerm).ToArray()));
                slowRaw[i] = graph.Param($"Fusion/SlowRaw/{i}", i == 0 ? 1f : 0f);
                graph.AddOperation(root, graph.Linear(slowRaw[i], new[]
                {
                    Term.Positive(slowIn[i], 1f), Term.Signed(gated, gain)
                }.Concat(kickTerm).ToArray()));
                fastOut[i] = graph.Param($"Fusion/Fast/{i}", i == 0 ? 1f : 0f);
                slowOut[i] = graph.Param($"Fusion/Slow/{i}", i == 0 ? 1f : 0f);
            }
            EmitExactSimplexNormalizer(graph, root, fastRaw, fastOut,
                "Fusion fast renormalizer");
            EmitExactSimplexNormalizer(graph, root, slowRaw, slowOut,
                "Fusion slow renormalizer");

            // The oscillator is stateless; the switch-tracking state, phase ramp,
            // and kick state persist. Commit them after every read this frame.
            // lastIndex must be committed AFTER the kick's hotOut read it.
            if (EnableFusionKick)
                for (var i = 0; i < count; i++)
                    graph.AddOperation(root, graph.Copy(kickNext[i], kickState[i], true));
            graph.AddOperation(root, graph.Copy(indexScaled, lastIndex, false));
            graph.AddOperation(root, graph.Copy(phaseNext, phase, false));

            return new FusionVectors { fast = fastOut, slow = slowOut };
        }

        /// <summary>
        /// Emits an exact simplex normalization: outputs = inputs / sum(inputs).
        /// Unity's normalized Direct tree sums each DISTINCT weight parameter
        /// once and divides only when that sum exceeds one, so the inputs are
        /// first scaled by a constant through dedicated parameters; the scale
        /// cancels in the quotient and keeps the divider engaged for any input
        /// sum above 1/scale.
        /// </summary>
        private static void EmitExactSimplexNormalizer(
            MathGraph graph,
            BlendTree root,
            IReadOnlyList<string> inputs,
            IReadOnlyList<string> outputs,
            string name)
        {
            const float scale = 4f;
            var scaled = new string[inputs.Count];
            for (var i = 0; i < inputs.Count; i++)
            {
                scaled[i] = graph.Param(
                    $"{name}/Scaled/{i}", i == 0 ? scale : 0f);
                graph.AddOperation(root, graph.Linear(scaled[i], new[]
                {
                    Term.Positive(inputs[i], scale)
                }));
            }
            graph.AddOperation(root, graph.NormalizeVector(
                scaled, outputs, name));
        }

        internal static float[] RetentionPullDecayAtControls()
        {
            var core =
                AdvancedVisemeOculusDynamics.TrajectoryCoreDurationSeconds;
            var times = new[]
            {
                0f, core / 3f, 2f * core / 3f, core,
                AdvancedVisemeOculusDynamics.TrajectoryDurationSeconds
            };
            var decay = new float[times.Length];
            for (var control = 0; control < times.Length; control++)
                decay[control] =
                    AdvancedVisemeRetentionPull.Decay(times[control]);
            return decay;
        }

        internal static bool ShouldUseOculusHalo(bool trackingEnabled)
        {
            // A tracking-capable avatar still needs the learned speech fallback
            // whenever its runtime tracking authority fades to zero. Keep the
            // argument in the test contract to prove both build variants select
            // the same fallback; runtime TrackingBlend controls only the exact
            // interpolation back to one-hot decoding.
            _ = trackingEnabled;
            return !DisableOculusHaloForTests;
        }

        internal static float OculusHaloDecoderWeight(
            bool useHalo,
            int hardWinner,
            int outputViseme)
        {
            if (hardWinner < 0 ||
                hardWinner >= VisemeReconstructionProfile.VisemeCount)
                throw new ArgumentOutOfRangeException(nameof(hardWinner));
            if (outputViseme < 0 ||
                outputViseme >= VisemeReconstructionProfile.VisemeCount)
                throw new ArgumentOutOfRangeException(nameof(outputViseme));
            if (AdvancedVisemeOculusHalo.VisemeCount !=
                VisemeReconstructionProfile.VisemeCount)
                throw new InvalidOperationException(
                    "The generated Oculus halo does not match the Oculus viseme contract.");

            if (!useHalo) return hardWinner == outputViseme ? 1f : 0f;
            return EnableDensityHalo
                ? AdvancedVisemeDensityHalo.Weight(hardWinner, outputViseme)
                : AdvancedVisemeOculusHalo.Weight(hardWinner, outputViseme);
        }

        internal static float[] CommuteOculusHaloProjection(
            bool useHalo,
            IReadOnlyList<float> coefficients)
        {
            if (coefficients == null ||
                coefficients.Count != VisemeReconstructionProfile.VisemeCount)
                throw new InvalidOperationException(
                    "An Oculus halo projection requires exactly 15 coefficients.");

            var result = new float[VisemeReconstructionProfile.VisemeCount];
            for (var winner = 0; winner < result.Length; winner++)
            {
                var value = 0f;
                for (var output = 0; output < result.Length; output++)
                    value += OculusHaloDecoderWeight(
                        useHalo, winner, output) * coefficients[output];
                result[winner] = value;
            }
            return result;
        }

        internal static float OculusDynamicsDecoderWeight(
            int hardWinner,
            int controlPoint,
            int outputViseme)
        {
            if (AdvancedVisemeOculusDynamics.VisemeCount !=
                    VisemeReconstructionProfile.VisemeCount ||
                AdvancedVisemeOculusDynamics.ControlPointCount != 5)
                throw new InvalidOperationException(
                    "The generated Oculus dynamics model does not match the decoder contract.");
            if (EnableDensityHalo)
                return AdvancedVisemeDensityHalo.Weight(hardWinner, outputViseme);
            return AdvancedVisemeOculusDynamics.Weight(
                hardWinner, controlPoint, outputViseme);
        }

        /// <summary>
        /// Commutes a linear downstream projection with the learned Oculus
        /// target trajectory. This lets the decoder animate the already
        /// existing projected parameters without evaluating another runtime
        /// matrix or adding another BlendTree.
        /// </summary>
        internal static float[][] CommuteOculusDynamicsProjection(
            IReadOnlyList<float> coefficients)
        {
            if (coefficients == null ||
                coefficients.Count != VisemeReconstructionProfile.VisemeCount)
                throw new InvalidOperationException(
                    "An Oculus dynamics projection requires exactly 15 coefficients.");

            var result = new float[VisemeReconstructionProfile.VisemeCount][];
            for (var winner = 0; winner < result.Length; winner++)
            {
                result[winner] = new float[
                    AdvancedVisemeOculusDynamics.ControlPointCount];
                for (var control = 0; control < result[winner].Length; control++)
                {
                    var value = 0f;
                    for (var output = 0; output < result.Length; output++)
                        value += OculusDynamicsDecoderWeight(
                            winner, control, output) * coefficients[output];
                    result[winner][control] = value;
                }
            }
            return result;
        }

        internal sealed class Request
        {
            public string controllerPath;
            public string parametersPath;
            public string rendererPath;
            public AdvancedVisemeReconstructorData component;
            public VisemeReconstructionProfile profile;
            public string trackingPrefix;
            public AdvancedVisemeTrackingInputs effectiveTrackingInputs;
            public bool reuseExistingTracking;
            public string trackingActiveParameter;
            public AnimatorControllerParameterType? trackingActiveAnimatorType;
            public float trackingActiveDefault;
            public Dictionary<AdvancedVisemeArticulator, string> trackingParameterNames;
            public Dictionary<string, string> auxiliaryTrackingParameterNames;
            public IReadOnlyCollection<AdvancedVisemeArticulator> directPoseArticulators =
                Array.Empty<AdvancedVisemeArticulator>();
            public string[] sourceVisemeBlendShapes;
            public AdvancedVisemeMeshCalibrator.Result calibration;
            public IReadOnlyList<AdvancedVisemeMeshCalibrator.BasisInput> calibrationBasis;
            public Dictionary<AdvancedVisemeArticulator, string> resolvedBlendShapes;
            public Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose> externalPoses;
            public Mesh targetMesh;
            public bool trackingEnabled;
            // When the avatar owns a generic YUCP Parameter Compressor, tuning
            // values are declared as ordinary synced parameters and the late
            // compressor folds them into its shared transport. This avoids
            // stacking AVR's private 13-bit bus with another compressor.
            public bool useSharedParameterCompressor;
            public HashSet<string> existingExpressionParameters;
            public IReadOnlyList<LinkedRendererOutput> linkedRendererOutputs =
                Array.Empty<LinkedRendererOutput>();
        }

        internal sealed class LinkedRendererOutput
        {
            public string rendererPath;
            public string label;
            public SkinnedMeshRenderer renderer;
            public Mesh sourceMesh;
            public AdvancedVisemeMeshCalibrator.Result calibration;
        }

        internal sealed class Result
        {
            public AnimatorController controller;
            public VRCExpressionParameters parameters;
            public readonly List<string> globalParameters = new List<string>();
            public readonly List<string> externalParameters = new List<string>();
            public readonly Dictionary<AdvancedVisemeArticulator, string> articulationParameters =
                new Dictionary<AdvancedVisemeArticulator, string>();
            public readonly Dictionary<AdvancedVisemeArticulator, string> speechArticulationParameters =
                new Dictionary<AdvancedVisemeArticulator, string>();
            public readonly Dictionary<AdvancedVisemeArticulator, string> trackingContributionParameters =
                new Dictionary<AdvancedVisemeArticulator, string>();
            public readonly Dictionary<AdvancedVisemeArticulator, string> trackingGainParameters =
                new Dictionary<AdvancedVisemeArticulator, string>();
            public readonly Dictionary<AdvancedVisemeArticulator, string> inverseTrackingGainParameters =
                new Dictionary<AdvancedVisemeArticulator, string>();
            public readonly Dictionary<AdvancedVisemeTuningControl, string> tuningParameters =
                new Dictionary<AdvancedVisemeTuningControl, string>();
            public readonly Dictionary<AdvancedVisemeTuningControl, string>
                effectiveTuningParameters =
                    new Dictionary<AdvancedVisemeTuningControl, string>();
            public string tuningSyncDataParameter;
            public string tuningSyncFocusParameter;
            public readonly List<string> tuningSyncIndexParameters = new List<string>();
            public int tuningSyncBits;
            public string manualTrackingParameter;
            public string trackingBlendParameter;
            public string trackingActiveWeightParameter;
            public AdvancedVisemeAnimatorGraphOptimizer.Report optimizerReport;
        }

        private sealed class BetaWeights
        {
            public string[] fast;
            public string[] slow;
        }

        private sealed class BetaCoarticulationGraph
        {
            public BetaWeights common;
            public IReadOnlyList<string> phoneObservationFast;
            public IReadOnlyList<string> fast;
            public IReadOnlyList<string> slow;
            public readonly List<KeyValuePair<string, float>> sleepEquilibrium =
                new List<KeyValuePair<string, float>>();
            public readonly Dictionary<AdvancedVisemeArticulatorGroup, BetaWeights> groups =
                new Dictionary<AdvancedVisemeArticulatorGroup, BetaWeights>();
            public readonly Dictionary<AdvancedVisemeArticulatorGroup, string> leads =
                new Dictionary<AdvancedVisemeArticulatorGroup, string>();
        }

        private sealed class SpeechHangoverGraph
        {
            public string history;
            public string presence;
        }

        private sealed class SharedSilenceAuthorityLayer
        {
            public string authority;
            public AnimatorState silence;
            public AnimatorState speech;
        }

        private sealed class FacePhonePosteriorGraph
        {
            public string mShareFast;
            public string mShareSlow;
            public string confidence;
            public string hiddenResidualDelta;
            public readonly Dictionary<AdvancedVisemeArticulatorGroup, BetaNasalCorrection> corrections =
                new Dictionary<AdvancedVisemeArticulatorGroup, BetaNasalCorrection>();
        }

        private sealed class BetaNasalCorrection
        {
            public string fast;
            public string slow;
        }

        private sealed class ConstraintConfidenceBases
        {
            public string bilabial;
            public string labiodental;
            public string sibilant;
        }

        private static readonly AdvancedVisemeArticulator[] CoreArticulators =
        {
            AdvancedVisemeArticulator.JawOpen,
            AdvancedVisemeArticulator.LipClose,
            AdvancedVisemeArticulator.MouthOpen,
            AdvancedVisemeArticulator.LipFunnel,
            AdvancedVisemeArticulator.LipPucker,
            AdvancedVisemeArticulator.LipSuck,
            AdvancedVisemeArticulator.SmileSad,
            AdvancedVisemeArticulator.LipBite,
            AdvancedVisemeArticulator.TongueOut
        };

        private static readonly AdvancedVisemeArticulator[] QualityArticulators =
        {
            AdvancedVisemeArticulator.JawX,
            AdvancedVisemeArticulator.JawZ,
            AdvancedVisemeArticulator.MouthX,
            AdvancedVisemeArticulator.TongueY
        };

        private static readonly AdvancedVisemeArticulator[] FullTongueArticulators =
        {
            AdvancedVisemeArticulator.TongueX,
            AdvancedVisemeArticulator.TongueRoll,
            AdvancedVisemeArticulator.TongueArchY,
            AdvancedVisemeArticulator.TongueShape,
            AdvancedVisemeArticulator.TongueTwistRight,
            AdvancedVisemeArticulator.TongueTwistLeft
        };

        // A coupled source-viseme pose may be faded globally only when the
        // tracker supplies the complete visible mouth basis. Tongue capability is
        // deliberately excluded: absent tongue hardware must not leave a
        // percentage of the entire authored jaw/lip pose over face tracking.
        private static readonly AdvancedVisemeArticulator[] VisiblePoseOwnershipArticulators =
        {
            AdvancedVisemeArticulator.JawOpen,
            AdvancedVisemeArticulator.LipClose,
            AdvancedVisemeArticulator.MouthOpen,
            AdvancedVisemeArticulator.LipFunnel,
            AdvancedVisemeArticulator.LipPucker,
            AdvancedVisemeArticulator.LipSuck,
            AdvancedVisemeArticulator.SmileSad
        };

        // A coupled authored viseme must yield whenever any visible coordinate
        // that it would move is already measured. Unlike complete calibration
        // ownership above, this support set is evaluated independently for every
        // viseme. Tongue channels stay outside it so speech can still infer
        // internal articulation when the visible lower face is fully tracked.
        private static readonly AdvancedVisemeArticulator[] VisibleSpeechArticulators =
        {
            AdvancedVisemeArticulator.JawOpen,
            AdvancedVisemeArticulator.LipClose,
            AdvancedVisemeArticulator.MouthOpen,
            AdvancedVisemeArticulator.LipFunnel,
            AdvancedVisemeArticulator.LipPucker,
            AdvancedVisemeArticulator.LipSuck,
            AdvancedVisemeArticulator.SmileSad,
            AdvancedVisemeArticulator.LipBite,
            AdvancedVisemeArticulator.JawX,
            AdvancedVisemeArticulator.JawZ,
            AdvancedVisemeArticulator.MouthX
        };

        // Unified Expressions does not expose per-channel capability bits. An
        // unsupported decoded channel is conventionally held at exact zero, so
        // remember sustained, unambiguous motion independently for every tongue
        // channel. This avoids one supported axis erasing learned motion on the
        // unsupported axes.
        internal const float NativeTongueCapabilityNoiseFloor = 0.001f;
        internal const float NativeTongueCapabilityThreshold = 0.01f;

        // Animator-friendly, One-Euro-inspired adaptive observer. At rest the
        // two-pole estimate rejects OSC/quantization chatter; once the fast and
        // slow observers disagree by a deliberate amount, the one-pole estimate
        // takes over. Values live in calibrated normalized articulator space.
        internal const float LocalTrackingMotionDeadband = 0.0025f;
        internal const float LocalTrackingMotionFullScale = 0.035f;
        internal const float RemoteTrackingMotionDeadband = 0.006f;
        internal const float RemoteTrackingMotionFullScale = 0.075f;
        internal const float TrackingAuthorityAgreementDeadband = 0.01f;
        internal const float TrackingAuthorityDisagreement = 0.12f;
        private const float TrackingMotionResponseSeconds = 0.012f;
        private const float ConstraintProjectionWidth = 0.05f;

        internal static Result Build(Request request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            request.profile.EnsureDefaults();

            AssetDatabase.DeleteAsset(request.controllerPath);
            AssetDatabase.DeleteAsset(request.parametersPath);

            var controller = new AnimatorController { name = "YUCP Advanced Viseme Reconstructor" };
            AssetDatabase.CreateAsset(controller, request.controllerPath);
            var result = new Result { controller = controller };
            var prefix = request.component.NormalizedPrefix;
            var internalPrefix = prefix + "/_Internal";
            var useOculusHalo = ShouldUseOculusHalo(request.trackingEnabled);

            var graph = new MathGraph(controller, internalPrefix);
            graph.AddParameter("Viseme", AnimatorControllerParameterType.Int, 0f);
            graph.AddParameter("Voice", AnimatorControllerParameterType.Float, 0f);
            // VRChat explicitly converts the built-in Bool to an Animator Float
            // (0/1). Keeping it as a Float avoids a selector state machine and a
            // one-frame animated-parameter handoff before local tracking math.
            graph.AddParameter("IsLocal", AnimatorControllerParameterType.Float, 0f);
            result.externalParameters.Add("Viseme");
            result.externalParameters.Add("Voice");
            result.externalParameters.Add("IsLocal");

            // This parameter is declared before the decoder because a tracking-
            // capable decoder consumes its previous-frame value to select between
            // the learned fallback and the exact legacy one-hot endpoint. Math
            // updates the same value later in the frame; the one-frame phase offset
            // exists only while tracking starts or stops and adds no new layer.
            var trackingBlend = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "TrackingBlend"),
                0f, false);
            result.trackingBlendParameter = trackingBlend;
            result.globalParameters.Add(trackingBlend);

            var time = graph.Param("Time", 0f);
            var lastTime = graph.Param("LastTime", 0f);
            var frameTime = graph.Param("FrameTime", 1f / 60f);
            AddTimeLayer(controller, graph, time);
            var visemeIndex = graph.Param("Viseme/Index", 0f);

            var mathRoot = graph.Direct("Reconstruction Math");
            graph.AddOperation(mathRoot, graph.Linear(frameTime, new[]
            {
                Term.Positive(time, 1f), Term.Positive(lastTime, -1f)
            }));
            graph.AddOperation(mathRoot, graph.Copy(time, lastTime, false));

            var tuning = BuildTuningParameters(
                graph, mathRoot, request, result);

            // The density matrix is fitted against a specific reconstruction
            // bandwidth, so that path carries its own paired time constant
            // rather than the profile default. The tuning slider still scales
            // around it, and disabling the flag restores the profile value.
            // ObserverResponseOverride exists because the halo branch below
            // discards the profile value, which silently made every observer
            // sweep a no-op: six time constants produced bit-identical output
            // and the conclusion "all time constants falsified" was never
            // actually tested.
            var visemeResponseSeconds = ObserverResponseOverride > 0f
                ? ObserverResponseOverride
                : EnableFusionEnvelope
                    ? FusionEnvelopeSeconds
                    : EnableDensityHalo
                        ? AdvancedVisemeDensityHalo.ObserverResponseSeconds
                        : request.profile.visemeResponseSeconds;
            var alphaViseme = BuildTunableAlpha(
                graph, mathRoot, frameTime, "Alpha/Viseme",
                visemeResponseSeconds,
                tuning[AdvancedVisemeTuningControl.SpeechSmoothness],
                0.006f, 0.12f);

            var voiceRaw = BuildTunableVoiceEvidence(
                graph, mathRoot, request.profile,
                tuning[AdvancedVisemeTuningControl.VoiceSensitivity]);
            var voiceFast = graph.Param("Voice/Fast", 0f);
            var voiceSlow = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "Energy"), 0f, false);
            graph.AddOperation(mathRoot, graph.Smooth(voiceRaw, voiceFast, alphaViseme, false));
            graph.AddOperation(mathRoot, graph.Smooth(voiceFast, voiceSlow, alphaViseme, false));
            result.globalParameters.Add(voiceSlow);

            var quietMotion = tuning[AdvancedVisemeTuningControl.QuietMotion];
            var voiceAmplitude = graph.Param("Voice/Amplitude", request.profile.quietSpeechFloor);
            graph.AddOperation(mathRoot, graph.Interpolate(
                quietMotion, MathGraph.AlwaysOneParameter,
                voiceAmplitude, voiceSlow, false));

            var voiceVelocity = graph.Param("Voice/Velocity", 0f);
            graph.AddOperation(mathRoot, graph.Linear(voiceVelocity, new[]
            {
                Term.Positive(voiceFast, 1f / Mathf.Max(0.005f, request.profile.visemeResponseSeconds)),
                Term.Positive(voiceSlow, -1f / Mathf.Max(0.005f, request.profile.visemeResponseSeconds))
            }));
            var onset = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "Onset"), 0f, false);
            var release = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "Release"), 0f, false);
            graph.AddOperation(mathRoot, graph.Map(voiceVelocity, onset, new[] { Point(-1f, 0f), Point(0f, 0f), Point(1f, 1f) }));
            graph.AddOperation(mathRoot, graph.Map(voiceVelocity, release, new[] { Point(-1f, 1f), Point(0f, 0f), Point(1f, 0f) }));
            result.globalParameters.Add(onset);
            result.globalParameters.Add(release);

            var rawVisemes = new string[VisemeReconstructionProfile.VisemeCount];
            var fastVisemes = new string[VisemeReconstructionProfile.VisemeCount];
            var slowVisemes = new string[VisemeReconstructionProfile.VisemeCount];
            var sparseFastVisemes = new string[VisemeReconstructionProfile.VisemeCount];
            var sparseSlowVisemes = new string[VisemeReconstructionProfile.VisemeCount];
            var speechWeights = new string[VisemeReconstructionProfile.VisemeCount];
            var fastSpeechWeights = new string[VisemeReconstructionProfile.VisemeCount];
            var betaEnabled = request.component.reconstructionMode ==
                              AdvancedVisemeReconstructionMode.BetaCoarticulation;
            var betaFaceInferenceEnabled = betaEnabled &&
                                           CanBuildFaceConditionedTongueInference(request);
            var conditionalLearnedDetailEnabled =
                EnableConditionalLearnedDetailSleepForTests && betaEnabled;
            var conditionalBetaContextSleepEnabled =
                conditionalLearnedDetailEnabled &&
                !KeepConditionalBetaContextAlwaysHotForTests;
            var conditionalCompute = conditionalLearnedDetailEnabled
                ? graph.Param("ConditionalLearnedDetail/Compute", 1f)
                : MathGraph.AlwaysOneParameter;
            var conditionalAuthority = conditionalLearnedDetailEnabled
                ? ShouldDelayConditionalLearnedDetailAuthority(
                    betaFaceInferenceEnabled)
                    ? graph.Param("ConditionalLearnedDetail/Authority", 1f)
                    : conditionalCompute
                : MathGraph.AlwaysOneParameter;
            var betaContextRoot = conditionalBetaContextSleepEnabled
                ? graph.Direct("Conditional Beta context compute")
                : mathRoot;
            var inferenceRoot = conditionalLearnedDetailEnabled
                ? graph.Direct("Conditional learned inference compute")
                : mathRoot;
            var betaProjectionCoefficients = new Dictionary<
                AdvancedVisemeArticulator, float[]>();
            var betaProjectionOffsets = new Dictionary<
                AdvancedVisemeArticulator, float>();
            var betaProjectionScales = new Dictionary<
                AdvancedVisemeArticulator, float>();
            var betaProjectedRaw = new Dictionary<
                AdvancedVisemeArticulator, string>();
            var betaProjectedFast = new Dictionary<
                AdvancedVisemeArticulator, string>();
            var betaProjectedSlow = new Dictionary<
                AdvancedVisemeArticulator, string>();
            // Direct no-tracker rendering must use one temporal epoch for both
            // residual detail R*p and the calibrated basis U*(C*p). Commute each
            // driveable linear C row into the decoder itself. Unlike the Beta
            // observer carriers below these values may be signed: they never act
            // as Direct BlendTree weights and therefore need no affine encoding.
            var directProjectedRaw = new Dictionary<
                AdvancedVisemeArticulator, string>();
            var directProjectedFast = new Dictionary<
                AdvancedVisemeArticulator, string>();
            var directProjectedSlow = new Dictionary<
                AdvancedVisemeArticulator, string>();
            var directProjectionCoefficients = new Dictionary<
                AdvancedVisemeArticulator, float[]>();
            foreach (var articulator in SynthesizedArticulators())
            {
                var coefficients = GetAdjustedSpeechCoefficients(
                    request, articulator);
                if (coefficients == null ||
                    coefficients.All(value => Mathf.Abs(value) < 1e-8f) ||
                    coefficients.Any(value =>
                        float.IsNaN(value) || float.IsInfinity(value)))
                    continue;
                directProjectionCoefficients[articulator] = coefficients;
                directProjectedRaw[articulator] = graph.Param(
                    $"DirectRender/Projected/{articulator}", coefficients[0]);
                directProjectedFast[articulator] = graph.Param(
                    $"DirectRender/ProjectedFast/{articulator}", coefficients[0]);
                directProjectedSlow[articulator] = graph.Param(
                    $"DirectRender/ProjectedSlow/{articulator}", coefficients[0]);
            }
            var betaRetentionRowTargets = new Dictionary<
                AdvancedVisemeArticulatorGroup, string[]>();
            if (betaEnabled)
            {
                foreach (var articulator in SynthesizedArticulators())
                {
                    var coefficients = GetAdjustedSpeechCoefficients(
                        request, articulator);
                    if (!ShouldProjectBetaArticulationRow(
                            articulator, coefficients)) continue;
                    var encoded = EncodeBetaProjectionRow(
                        articulator, coefficients, out var offset, out var scale);
                    betaProjectionCoefficients[articulator] = encoded;
                    betaProjectionOffsets[articulator] = offset;
                    betaProjectionScales[articulator] = scale;
                    betaProjectedRaw[articulator] = graph.Param(
                        $"BetaCoarticulation/Projected/{articulator}/Raw",
                        encoded[0]);
                    betaProjectedFast[articulator] = graph.Param(
                        $"BetaCoarticulation/Projected/{articulator}/Fast",
                        encoded[0]);
                    betaProjectedSlow[articulator] = graph.Param(
                        $"BetaCoarticulation/Projected/{articulator}/Slow",
                        encoded[0]);
                }
                if (!UseSwitchedRetentionRowObserverForTests)
                {
                    for (var groupIndex = 0;
                         groupIndex < AdvancedVisemeTransitionRetention.GroupCount;
                         groupIndex++)
                    {
                        var group = (AdvancedVisemeArticulatorGroup)groupIndex;
                        betaRetentionRowTargets[group] = Enumerable.Range(
                                0, VisemeReconstructionProfile.VisemeCount)
                            .Select(current => graph.Param(
                                $"BetaCoarticulation/RetentionTarget/{group}/{current}",
                                AdvancedVisemeCoarticulationModel.Retention(
                                    group, 0, current)))
                            .ToArray();
                    }
                }
            }
            for (var i = 0; i < rawVisemes.Length; i++)
            {
                var defaultValue = i == 0 ? 1f : 0f;
                rawVisemes[i] = graph.Param($"Viseme/{i}/Raw", defaultValue);
                fastVisemes[i] = graph.Param($"Viseme/{i}/Fast", defaultValue);
                // Keep the observer state internal. The public viseme simplex is
                // published after TrackingBlend exists so speech-only liveliness
                // and the visible mesh share the same causal trajectory.
                slowVisemes[i] = graph.Param($"Viseme/{i}/Slow", defaultValue);
                sparseFastVisemes[i] = graph.Param(
                    $"Viseme/{i}/SparseFast", defaultValue);
                sparseSlowVisemes[i] = graph.Param(
                    $"Viseme/{i}/SparseSlow", defaultValue);
            }
            var identityDecodedVisemeVectors = directProjectedRaw.ToDictionary(
                pair => pair.Value,
                pair => directProjectionCoefficients[pair.Key].ToArray());
            foreach (var pair in betaProjectedRaw)
                identityDecodedVisemeVectors[pair.Value] =
                    CommuteOculusHaloProjection(
                        false, betaProjectionCoefficients[pair.Key]);
            // The conditional row is the minimum-MSE static estimate available
            // from one hard winner; temporal context remains in the shared live
            // observer rather than in a winner-local animation clock.
            var haloDecodedVisemeVectors = directProjectedRaw.ToDictionary(
                pair => pair.Value,
                pair => CommuteOculusHaloProjection(
                    true, directProjectionCoefficients[pair.Key]));
            foreach (var pair in betaProjectedRaw)
                haloDecodedVisemeVectors[pair.Value] =
                    CommuteOculusHaloProjection(
                        true, betaProjectionCoefficients[pair.Key]);
            // A linear projection of a cubic simplex trajectory is itself a
            // cubic trajectory. Bake those four projected controls beside the
            // raw-viseme controls so runtime pays no matrix or extra tree cost.
            var haloTrajectoryDecodedVisemeVectors = directProjectedRaw.ToDictionary(
                pair => pair.Value,
                pair => CommuteOculusDynamicsProjection(
                    directProjectionCoefficients[pair.Key]));
            foreach (var pair in betaProjectedRaw)
                haloTrajectoryDecodedVisemeVectors[pair.Value] =
                    CommuteOculusDynamicsProjection(
                        betaProjectionCoefficients[pair.Key]);
            var hardDecodedVisemeVectors =
                new Dictionary<string, float[]>(StringComparer.Ordinal);
            // Keep the corpus transition state conditioned on VRChat's hard
            // semantic winner. Held-out replay favors c_hard^T R q: applying H
            // to R here adds little fit and lets neighboring visual mass change
            // which phonetic context row is remembered.
            foreach (var pair in betaRetentionRowTargets)
            for (var current = 0;
                 current < VisemeReconstructionProfile.VisemeCount;
                 current++)
            {
                var group = pair.Key;
                var hardRetention = Enumerable.Range(
                        0, VisemeReconstructionProfile.VisemeCount)
                    .Select(previous =>
                        AdvancedVisemeCoarticulationModel.Retention(
                            group, previous, current))
                    .ToArray();
                hardDecodedVisemeVectors[pair.Value[current]] = hardRetention;
            }
            // Separable retention pull (Normal mode only; Beta owns its own
            // retention through TransitionLead). The semantics decoder emits
            // the current winner's f row; a vector EMA at PullResponseSeconds
            // carries it across switches, so PullScale * (ema - f[cur]) decays
            // exactly like d(age) * (f[prev] - f[cur]). The (cur, age)-only
            // remainder folds into the decoder trajectory curves below, and a
            // Normalize-Blend-Values Direct tree restores the exact simplex
            // after the additive step (native division by the weight sum).
            var pullEnabled = EnableRetentionPull && !betaEnabled;
            var pullFRows = new string[VisemeReconstructionProfile.VisemeCount];
            var pullFProjected = new Dictionary<
                AdvancedVisemeArticulator, string>();
            var pullFProjectedDefaults = new Dictionary<
                AdvancedVisemeArticulator, float>();
            if (pullEnabled)
            {
                for (var channel = 0;
                     channel < VisemeReconstructionProfile.VisemeCount;
                     channel++)
                {
                    var silDefault = AdvancedVisemeRetentionPull.PreviousRow(
                        0, channel);
                    pullFRows[channel] = graph.Param(
                        $"Viseme/Pull/F/{channel}", silDefault);
                    hardDecodedVisemeVectors[pullFRows[channel]] =
                        Enumerable.Range(
                                0, VisemeReconstructionProfile.VisemeCount)
                            .Select(winner =>
                                AdvancedVisemeRetentionPull.PreviousRow(
                                    winner, channel))
                            .ToArray();
                }
                // The physical direct-render path consumes projections of the
                // same halo rows, so both pull terms ride along or physical
                // and public trajectories diverge. Projection commutes with
                // the EMA, so the projected pull is an EMA of a decoder-
                // emitted projected f row.
                foreach (var pair in directProjectedRaw)
                {
                    var coefficients =
                        directProjectionCoefficients[pair.Key];
                    float ProjectF(int winner) => Enumerable.Range(
                            0, VisemeReconstructionProfile.VisemeCount)
                        .Sum(channel => coefficients[channel] *
                            AdvancedVisemeRetentionPull.PreviousRow(
                                winner, channel));
                    var projectedF = graph.Param(
                        $"DirectRender/PullF/{pair.Key}", ProjectF(0));
                    hardDecodedVisemeVectors[projectedF] = Enumerable.Range(
                            0, VisemeReconstructionProfile.VisemeCount)
                        .Select(ProjectF)
                        .ToArray();
                    pullFProjected[pair.Key] = projectedF;
                    pullFProjectedDefaults[pair.Key] = ProjectF(0);

                    if (useOculusHalo &&
                        haloTrajectoryDecodedVisemeVectors.TryGetValue(
                            pair.Value, out var trajectoryRows))
                        for (var winner = 0;
                             winner < trajectoryRows.Length;
                             winner++)
                        {
                            if (!AdvancedVisemeOculusDynamics
                                    .HasDynamicTrajectory(winner))
                                continue;
                            // Project the same retracted folded rows the
                            // one-hot decoder bakes, keeping the physical
                            // projection consistent with the public simplex.
                            for (var control = 0;
                                 control < trajectoryRows[winner].Length;
                                 control++)
                                trajectoryRows[winner][control] =
                                    Enumerable.Range(
                                            0,
                                            VisemeReconstructionProfile
                                                .VisemeCount)
                                        .Sum(channel =>
                                            coefficients[channel] *
                                            RetentionPullFoldedDecoderWeight(
                                                winner, control, channel));
                        }
                }
            }
            // The observer state remains an exact two-pole simplex. Its
            // exponentially decaying tails are useful for recurrence but costly
            // as Direct BlendTree weights: any positive float keeps a child live.
            // Publish soft-thresholded copies before Math observes the previous
            // exact observer state. The motion is attached to the already-present
            // Shared Silence layer below so the decoder contains only one static,
            // interruption-safe target row per hard winner.
            // With the retention pull active the observer state carries small
            // transient tails; a tighter cull keeps the epsilon-rescale sum
            // drift of the published simplex under half the test tolerance.
            var simplexSparsifier = graph.SparsifyNonnegativeVector(
                fastVisemes.Concat(slowVisemes).ToArray(),
                sparseFastVisemes.Concat(sparseSlowVisemes).ToArray(),
                pullEnabled ? 5e-6f : AdvancedVisemeMath.SimplexCullingEpsilon,
                "Sparse observer emission");
            // Winner-selected rows must be registered BEFORE the semantics
            // layer below consumes the dictionary; registering afterwards
            // leaves the parameter stuck at its authored default, which reads
            // as a correction that is wired but inert.
            var syllabicSlopes = new string[
                VisemeReconstructionProfile.VisemeCount];
            if (EnableSyllabicResponse && !betaEnabled)
            {
                for (var i = 0; i < syllabicSlopes.Length; i++)
                {
                    syllabicSlopes[i] = graph.Param(
                        $"Viseme/SyllabicSlope/{i}",
                        AdvancedVisemeSyllabicResponse.Slope(0, i));
                    var channel = i;
                    hardDecodedVisemeVectors[syllabicSlopes[i]] = Enumerable
                        .Range(0, VisemeReconstructionProfile.VisemeCount)
                        .Select(winner => AdvancedVisemeSyllabicResponse.Slope(
                            winner, channel))
                        .ToArray();
                }
            }

            // Hard semantic state must remain immediate for silence handling and
            // corpus selection. Only the continuous target is cross-faded; this
            // prevents fractional indices from leaking into categorical logic.
            AddIntToFloatLayer(
                controller, graph, "Viseme", visemeIndex, null,
                hardDecodedVisemeVectors, null, null,
                false, null, null, 0f,
                "YUCP AVR Viseme Semantics");
            AddIntToFloatLayer(
                controller, graph, "Viseme", null, rawVisemes,
                identityDecodedVisemeVectors, haloDecodedVisemeVectors,
                haloTrajectoryDecodedVisemeVectors,
                useOculusHalo,
                request.trackingEnabled ? trackingBlend : null,
                null, VisemeTargetCrossfadeSeconds,
                "YUCP AVR Viseme Decoder",
                foldRetentionPull: pullEnabled && useOculusHalo);

            // VRChat emits sil both at a real utterance endpoint and in short gaps
            // between words. A leaky speech-history observer treats a short sil as
            // a temporarily missing phonetic sample. Sustained speech charges more
            // history than a brief click, but Voice alone can never pin the mouth.
            // The hold is selected inside each observer motion, rather than through
            // sibling target/alpha parameters, so VRCFury's BlendTree optimization
            // cannot turn it into a delayed feedback pipeline.
            var speechHangover = BuildSpeechHangover(
                graph, mathRoot, frameTime, visemeIndex,
                request.profile, tuning[AdvancedVisemeTuningControl.SilenceStability],
                prefix, result);
            if (conditionalLearnedDetailEnabled)
                AddConditionalLearnedDetailMathControl(
                    graph, mathRoot, time, frameTime, visemeIndex,
                    sparseFastVisemes[0],
                    conditionalCompute, conditionalAuthority);
            // All ordinary transient-silence holds share the same scalar
            // authority. Publish it in a tiny state layer driven directly by
            // VRChat's hard Viseme input. Its one-frame AAP publication aligns
            // with the already delayed history/index epoch consumed by Math,
            // allowing the expensive release/freeze vectors to be evaluated
            // once instead of being repeated through three selector branches.
            var sharedSilenceLayer = AddSharedSilenceUpdateAuthorityLayer(
                controller, graph, speechHangover.history,
                tuning[AdvancedVisemeTuningControl.SilenceStability]);
            graph.UseSharedSilenceUpdateAuthority(
                sharedSilenceLayer.authority);
            var heldAlphaViseme = UseFactoredPrimarySilenceObserverForTests
                ? graph.RegisterSharedSilenceFactoredWeight(
                    alphaViseme, "Viseme")
                : null;
            var observerRawVisemes = rawVisemes;
            var directProjectedObserverRaw = directProjectedRaw;
            if (EnableVoiceResponse && !betaEnabled)
            {
                // target = raw + (Voice - VoiceMean) * Slope[winner]
                // Slope rows are sum-zero, so the simplex sum is preserved
                // exactly; the renormalizer below only repairs clamping.
                var voiceCentered = graph.Param("Voice/Centered", 0f);
                graph.AddOperation(mathRoot, graph.Linear(voiceCentered, new[]
                {
                    Term.Positive(voiceFast, 1f),
                    Term.Positive(MathGraph.AlwaysOneParameter,
                        -AdvancedVisemeVoiceResponse.VoiceMean)
                }));

                var voiced = new string[
                    VisemeReconstructionProfile.VisemeCount];
                var slopeRows = new string[
                    VisemeReconstructionProfile.VisemeCount];
                for (var i = 0; i < voiced.Length; i++)
                {
                    slopeRows[i] = graph.Param(
                        $"Viseme/VoiceSlope/{i}",
                        AdvancedVisemeVoiceResponse.Slope(0, i));
                    var channel = i;
                    hardDecodedVisemeVectors[slopeRows[i]] = Enumerable
                        .Range(0, VisemeReconstructionProfile.VisemeCount)
                        .Select(winner => AdvancedVisemeVoiceResponse.Slope(
                            winner, channel))
                        .ToArray();
                }
                for (var i = 0; i < voiced.Length; i++)
                {
                    var contribution = graph.Param(
                        $"Viseme/VoiceTerm/{i}", 0f);
                    graph.AddOperation(mathRoot, graph.Multiply(
                        voiceCentered, slopeRows[i], contribution, true));
                    voiced[i] = graph.Param(
                        $"Viseme/{i}/Voiced", i == 0 ? 1f : 0f);
                    graph.AddOperation(mathRoot, graph.Linear(voiced[i], new[]
                    {
                        Term.Positive(rawVisemes[i], 1f),
                        Term.Positive(contribution, VoiceResponseGain)
                    }));
                }
                var voicedNorm = new string[voiced.Length];
                for (var i = 0; i < voiced.Length; i++)
                    voicedNorm[i] = graph.Param(
                        $"Viseme/{i}/VoicedNorm", i == 0 ? 1f : 0f);
                EmitExactSimplexNormalizer(
                    graph, mathRoot, voiced, voicedNorm,
                    "Voice-conditioned target renormalizer");
                observerRawVisemes = voicedNorm;
            }
            if (EnableSyllabicResponse && !betaEnabled)
            {
                // A target conditioned only on the winner is constant between
                // switches, so the observer settles and the output staircases.
                // The rate of the syllabic band is the only every-frame channel
                // quantity that keeps moving there, and unlike generated noise
                // it is phase-locked to the audio the listener hears.
                //
                //   band = onepole(Voice, T1) - onepole(Voice, T2)   parallel
                //   rate = (band - onepole(band, TD)) / TD
                //
                // Both band poles read Voice directly. Cascading the second off
                // the first measures no better than the frozen baseline.
                var rawVoice = graph.Param("Voice", 0f, false);
                var alphaBandFast = graph.Param("Voice/Syllabic/AlphaFast", 0.5f);
                var alphaBandSlow = graph.Param("Voice/Syllabic/AlphaSlow", 0.5f);
                var alphaRate = graph.Param("Voice/Syllabic/AlphaRate", 0.5f);
                graph.AddOperation(mathRoot, graph.AlphaFromDeltaTime(
                    frameTime, alphaBandFast,
                    AdvancedVisemeSyllabicResponse.BandFastSeconds));
                graph.AddOperation(mathRoot, graph.AlphaFromDeltaTime(
                    frameTime, alphaBandSlow,
                    AdvancedVisemeSyllabicResponse.BandSlowSeconds));
                graph.AddOperation(mathRoot, graph.AlphaFromDeltaTime(
                    frameTime, alphaRate,
                    AdvancedVisemeSyllabicResponse.RateSeconds));

                var bandFast = graph.Param("Voice/Syllabic/Fast", 0f);
                var bandSlow = graph.Param("Voice/Syllabic/Slow", 0f);
                graph.AddOperation(mathRoot, graph.Smooth(
                    rawVoice, bandFast, alphaBandFast, false));
                graph.AddOperation(mathRoot, graph.Smooth(
                    rawVoice, bandSlow, alphaBandSlow, false));

                var band = graph.Param("Voice/Syllabic/Band", 0f);
                graph.AddOperation(mathRoot, graph.Linear(band, new[]
                {
                    Term.Positive(bandFast, 1f),
                    Term.Positive(bandSlow, -1f)
                }));

                // band is the first SIGNED value in this chain, so every node
                // that consumes it must be signed too. An unsigned Smooth lowers
                // to Copy -> WeightedSetter, which makes band its own Direct
                // blend weight and clamps the negative half to zero; the lag
                // would then track a half-wave-rectified band and the high-pass
                // below would differentiate the resulting corners into spikes.
                var bandLag = graph.Param("Voice/Syllabic/BandLag", 0f);
                graph.AddOperation(mathRoot, graph.Smooth(
                    band, bandLag, alphaRate, true));

                var inverseRate = 1f / Mathf.Max(
                    0.005f, AdvancedVisemeSyllabicResponse.RateSeconds);
                var syllabicRate = graph.Param("Voice/Syllabic/Rate", 0f);
                graph.AddOperation(mathRoot, graph.Linear(syllabicRate, new[]
                {
                    Term.Signed(band, inverseRate),
                    Term.Signed(bandLag, -inverseRate)
                }));

                // Slope rows are sum-zero, so this moves mass between visemes
                // and preserves the simplex total; the renormalizer below only
                // repairs clamping. The rows themselves are declared and
                // winner-registered above, before the semantics layer.
                //
                // Multiply drives a Direct blend tree by its first argument, and
                // Unity clamps a negative direct blend parameter to zero. The
                // rate is signed and near symmetric, so feeding it in raw
                // silently discards every falling half-cycle. Rectify into two
                // non-negative halves and recombine with opposite signs.
                var ratePositive = graph.Param("Voice/Syllabic/RatePositive", 0f);
                var rateNegative = graph.Param("Voice/Syllabic/RateNegative", 0f);
                const float rateClamp = SyllabicRateClamp;
                graph.AddOperation(mathRoot, graph.Map(syllabicRate, ratePositive,
                    new[] { Point(-rateClamp, 0f), Point(0f, 0f), Point(rateClamp, 1f) }));
                graph.AddOperation(mathRoot, graph.Map(syllabicRate, rateNegative,
                    new[] { Point(-rateClamp, 1f), Point(0f, 0f), Point(rateClamp, 0f) }));

                var syllabic = new string[
                    VisemeReconstructionProfile.VisemeCount];
                for (var i = 0; i < syllabic.Length; i++)
                {
                    var risingTerm = graph.Param($"Viseme/SyllabicRise/{i}", 0f);
                    var fallingTerm = graph.Param($"Viseme/SyllabicFall/{i}", 0f);
                    graph.AddOperation(mathRoot, graph.Multiply(
                        ratePositive, syllabicSlopes[i], risingTerm, true));
                    graph.AddOperation(mathRoot, graph.Multiply(
                        rateNegative, syllabicSlopes[i], fallingTerm, true));

                    // The rectifiers carry rate/rateClamp, so the recombination
                    // scale restores the fitted units.
                    var scale = SyllabicResponseGain * rateClamp;
                    syllabic[i] = graph.Param(
                        $"Viseme/{i}/Syllabic", i == 0 ? 1f : 0f);
                    // The slope rows are sum-zero, so for any winner about half
                    // the channels carry a negative slope and both terms go
                    // negative there. As positive terms those channels would be
                    // clamped away entirely: mass would be added to the rising
                    // channels and never removed from the others, destroying the
                    // sum-zero property before the renormalizer ever sees it.
                    graph.AddOperation(mathRoot, graph.Linear(syllabic[i], new[]
                    {
                        Term.Positive(observerRawVisemes[i], 1f),
                        Term.Signed(risingTerm, scale),
                        Term.Signed(fallingTerm, -scale)
                    }));
                }

                var syllabicNorm = new string[syllabic.Length];
                for (var i = 0; i < syllabic.Length; i++)
                    syllabicNorm[i] = graph.Param(
                        $"Viseme/{i}/SyllabicNorm", i == 0 ? 1f : 0f);
                EmitExactSimplexNormalizer(
                    graph, mathRoot, syllabic, syllabicNorm,
                    "Syllabic-rate target renormalizer");
                observerRawVisemes = syllabicNorm;
            }
            const bool enableConvexMemory = false; // perceptually over-smooth; see notes
            if (enableConvexMemory && !pullEnabled && !betaEnabled)
            {
                // Convex retention memory (production path; corpus-fitted on
                // the SPIRE Oculus extraction, dev split: blend 0.30, tau 25
                // ms; held-out RMSE 0.05296/0.05958 vs baseline
                // 0.05751/0.06699). A convex combination of simplex points,
                // so the public-simplex contract holds bit-for-bit. The
                // additive pull behind EnableRetentionPull scores better
                // offline but its animator realization is still being made
                // sum-exact; see avr-transition-design notes.
                const float memoryBlend = 0.30f;
                const float memoryResponseSeconds = 0.025f;
                var alphaMemory = graph.Param("Alpha/Pull", 0.5f);
                graph.AddOperation(mathRoot, graph.AlphaFromDeltaTime(
                    frameTime, alphaMemory, memoryResponseSeconds));
                var mixed = new string[
                    VisemeReconstructionProfile.VisemeCount];
                for (var i = 0; i < mixed.Length; i++)
                {
                    var memory = graph.Param(
                        $"Viseme/Pull/M/{i}", i == 0 ? 1f : 0f);
                    graph.AddOperation(mathRoot, graph.Smooth(
                        rawVisemes[i], memory, alphaMemory, false));
                    mixed[i] = graph.Param(
                        $"Viseme/{i}/Pulled", i == 0 ? 1f : 0f);
                    graph.AddOperation(mathRoot, graph.Linear(mixed[i], new[]
                    {
                        Term.Positive(rawVisemes[i], 1f - memoryBlend),
                        Term.Positive(memory, memoryBlend)
                    }));
                    if (request.trackingEnabled)
                    {
                        var gated = graph.Param(
                            $"Viseme/{i}/PullGated", i == 0 ? 1f : 0f);
                        graph.AddOperation(mathRoot, graph.Interpolate(
                            mixed[i], rawVisemes[i], gated,
                            trackingBlend, false));
                        mixed[i] = gated;
                    }
                }
                observerRawVisemes = mixed;

                if (directProjectedRaw.Count > 0)
                {
                    directProjectedObserverRaw = new Dictionary<
                        AdvancedVisemeArticulator, string>();
                    foreach (var pair in directProjectedRaw)
                    {
                        var memory = graph.Param(
                            $"DirectRender/PullM/{pair.Key}",
                            directProjectionCoefficients[pair.Key][0]);
                        graph.AddOperation(mathRoot, graph.Smooth(
                            pair.Value, memory, alphaMemory, true));
                        var mixedProjected = graph.Param(
                            $"DirectRender/Pulled/{pair.Key}",
                            directProjectionCoefficients[pair.Key][0]);
                        graph.AddOperation(mathRoot, graph.Linear(
                            mixedProjected, new[]
                            {
                                Term.Positive(pair.Value, 1f - memoryBlend),
                                Term.Positive(memory, memoryBlend)
                            }));
                        directProjectedObserverRaw[pair.Key] =
                            mixedProjected;
                    }
                }
            }
            if (pullEnabled)
            {
                var alphaPull = graph.Param("Alpha/Pull", 0.5f);
                graph.AddOperation(mathRoot, graph.AlphaFromDeltaTime(
                    frameTime, alphaPull,
                    AdvancedVisemeRetentionPull.PullResponseSeconds));
                var pulled = new string[
                    VisemeReconstructionProfile.VisemeCount];
                var normalized = new string[
                    VisemeReconstructionProfile.VisemeCount];
                for (var i = 0; i < pulled.Length; i++)
                {
                    var silDefault = AdvancedVisemeRetentionPull.PreviousRow(
                        0, i);
                    var ema = graph.Param($"Viseme/Pull/Z/{i}", silDefault);
                    graph.AddOperation(mathRoot, graph.Smooth(
                        pullFRows[i], ema, alphaPull, true));
                    // Scaled by four so the normalized Direct tree's divider
                    // (active only when the distinct-parameter sum exceeds
                    // one) always engages; the scale cancels in the quotient.
                    pulled[i] = graph.Param(
                        $"Viseme/{i}/Pulled4", i == 0 ? 4f : 0f);
                    graph.AddOperation(mathRoot, graph.Linear(pulled[i], new[]
                    {
                        Term.Positive(rawVisemes[i], 4f),
                        Term.Positive(
                            ema, 4f * AdvancedVisemeRetentionPull.PullScale),
                        Term.Positive(
                            pullFRows[i],
                            -4f * AdvancedVisemeRetentionPull.PullScale)
                    }));
                    normalized[i] = graph.Param(
                        $"Viseme/{i}/PullNorm", i == 0 ? 1f : 0f);
                }
                // Exact clamp-and-renormalize retraction: negative weights
                // clamp out of the sum and the division restores sum one.
                graph.AddOperation(mathRoot, graph.NormalizeVector(
                    pulled, normalized, "Retention pull renormalizer"));
                for (var i = 0; i < pulled.Length; i++)
                {
                    var target = normalized[i];
                    if (request.trackingEnabled)
                    {
                        // Tracked mouths must not inherit the speech-model
                        // pull; fade it out with tracking authority exactly
                        // like the liveliness lead. Both endpoints live on
                        // the simplex, so the gate preserves it.
                        var gated = graph.Param(
                            $"Viseme/{i}/PullGated", i == 0 ? 1f : 0f);
                        graph.AddOperation(mathRoot, graph.Interpolate(
                            target, rawVisemes[i], gated,
                            trackingBlend, false));
                        target = gated;
                    }
                    normalized[i] = target;
                }
                observerRawVisemes = normalized;

                if (directProjectedRaw.Count > 0)
                {
                    // Articulator projections are signed and carry no sum
                    // constraint, so the additive form needs no renormalizer
                    // here; projection commutes with the EMA.
                    directProjectedObserverRaw = new Dictionary<
                        AdvancedVisemeArticulator, string>();
                    foreach (var pair in directProjectedRaw)
                    {
                        var projectedF = pullFProjected[pair.Key];
                        var ema = graph.Param(
                            $"DirectRender/PullZ/{pair.Key}",
                            pullFProjectedDefaults[pair.Key]);
                        graph.AddOperation(mathRoot, graph.Smooth(
                            projectedF, ema, alphaPull, true));
                        var pulledProjected = graph.Param(
                            $"DirectRender/Pulled/{pair.Key}",
                            directProjectionCoefficients[pair.Key][0]);
                        graph.AddOperation(mathRoot, graph.Linear(
                            pulledProjected, new[]
                            {
                                Term.Positive(pair.Value, 1f),
                                Term.Positive(
                                    ema,
                                    AdvancedVisemeRetentionPull.PullScale),
                                Term.Positive(
                                    projectedF,
                                    -AdvancedVisemeRetentionPull.PullScale)
                            }));
                        directProjectedObserverRaw[pair.Key] =
                            pulledProjected;
                    }
                }
            }
            if (EnableFusionEnvelope && FusionEnvelopeGamma != 1f && !betaEnabled)
            {
                // Sharpen the target before the observer: raise each channel to
                // the envelope gamma and renormalize. This peaks the simplex so
                // co-activation and peakedness match the teacher; the observer
                // then smooths the sharpened target into the visible envelope.
                var sharp = new string[VisemeReconstructionProfile.VisemeCount];
                var sharpNorm = new string[VisemeReconstructionProfile.VisemeCount];
                var gammaPoints = new (float, float)[9];
                for (var kk = 0; kk < gammaPoints.Length; kk++)
                {
                    var x = kk / (float)(gammaPoints.Length - 1);
                    gammaPoints[kk] = (x, Mathf.Pow(x, FusionEnvelopeGamma));
                }
                for (var i = 0; i < sharp.Length; i++)
                {
                    sharp[i] = graph.Param($"Viseme/{i}/Sharp", i == 0 ? 1f : 0f);
                    graph.AddOperation(mathRoot, graph.Map(observerRawVisemes[i], sharp[i],
                        gammaPoints.Select(p => Point(p.Item1, p.Item2)).ToArray()));
                    sharpNorm[i] = graph.Param($"Viseme/{i}/SharpNorm", i == 0 ? 1f : 0f);
                }
                EmitExactSimplexNormalizer(graph, mathRoot, sharp, sharpNorm,
                    "Fusion envelope sharpen renormalizer");
                observerRawVisemes = sharpNorm;
            }

            var projectedOrder = betaProjectedRaw.Keys
                .OrderBy(articulator => (int)articulator)
                .ToArray();
            var speechObserverRaw = observerRawVisemes
                .Concat(projectedOrder.Select(
                    articulator => betaProjectedRaw[articulator])).ToArray();
            var speechObserverFast = fastVisemes.Concat(projectedOrder.Select(
                articulator => betaProjectedFast[articulator])).ToArray();
            var speechObserverSlow = slowVisemes.Concat(projectedOrder.Select(
                articulator => betaProjectedSlow[articulator])).ToArray();
            // C commutes with the shared linear observer. Selected dense
            // articulation rows ride inside the existing nonnegative vector
            // observer. Signed rows use an affine [0,1] coordinate and are
            // decoded only at the corpus output, avoiding two-sided copy trees.
            if (UseFactoredPrimarySilenceObserverForTests)
                graph.AddOperation(mathRoot, graph.SmoothVector(
                    speechObserverRaw, speechObserverFast, heldAlphaViseme,
                    "Viseme and projected-articulation factored fast observer"));
            else
                graph.AddOperation(mathRoot, graph.SmoothVectorUnlessHeldSilence(
                    speechObserverRaw, speechObserverFast, alphaViseme,
                    visemeIndex, speechHangover.history,
                    tuning[AdvancedVisemeTuningControl.SilenceStability],
                    "Viseme and projected-articulation fast observer"));
            graph.AddOperation(mathRoot, graph.SmoothVector(
                speechObserverFast, speechObserverSlow, alphaViseme,
                "Viseme and projected-articulation slow observer"));

            if (directProjectedRaw.Count > 0)
            {
                var directFastRelease = graph.InterpolateArticulationVector(
                    directProjectedFast, directProjectedObserverRaw,
                    directProjectedFast, alphaViseme,
                    "Direct projected fast observer release");
                var directFastFreeze = graph.CopyArticulationVector(
                    directProjectedFast, directProjectedFast,
                    "Direct projected fast observer freeze");
                graph.AddOperation(mathRoot, graph.SelectSilenceHoldMotion(
                    directFastRelease, directFastRelease, directFastFreeze,
                    visemeIndex, speechHangover.history,
                    tuning[AdvancedVisemeTuningControl.SilenceStability],
                    "Direct projected fast observer"));
                graph.AddOperation(mathRoot,
                    graph.InterpolateArticulationVector(
                        directProjectedSlow, directProjectedFast,
                        directProjectedSlow, alphaViseme,
                        "Direct projected slow observer"));
            }

            var reconstructedFastVisemes = sparseFastVisemes;
            var reconstructedSlowVisemes = sparseSlowVisemes;
            BetaCoarticulationGraph betaGraph = null;
            if (betaEnabled)
            {
                betaGraph = BuildBetaCoarticulationWeights(
                    graph, betaContextRoot, mathRoot,
                    tuning[AdvancedVisemeTuningControl.Coarticulation], frameTime,
                    rawVisemes, sparseFastVisemes, sparseSlowVisemes,
                    betaRetentionRowTargets,
                    visemeIndex, speechHangover.history,
                    tuning[AdvancedVisemeTuningControl.SilenceStability],
                    betaFaceInferenceEnabled);
                reconstructedFastVisemes = betaGraph.common.fast;
                reconstructedSlowVisemes = betaGraph.common.slow;
            }

            // Slow the release of each viseme weight so the outgoing viseme
            // lingers into the incoming one (raises co-activation, stops the
            // mouth returning to rest between visemes). Applied before the hold
            // generator so the generator rides the overlapped envelope.
            if (EnableAsymmetricRelease)
            {
                reconstructedFastVisemes = AppendAsymmetricRelease(
                    graph, mathRoot, frameTime, reconstructedFastVisemes, "Fast");
                reconstructedSlowVisemes = AppendAsymmetricRelease(
                    graph, mathRoot, frameTime, reconstructedSlowVisemes, "Slow");
            }

            // The lower-face mouth reads speechWeights, which come from THESE
            // vectors (reconstructedFast/Slow), not styledVisemes. So the hold
            // generator that keeps the mouth alive during a sustained viseme
            // must inject here, before the speechWeights projection below, and
            // in both Normal and Beta modes (the avatar runs Beta).
            if (EnableFusionHoldGenerator)
            {
                var hold = AppendFusionHoldGenerator(
                    graph, mathRoot, frameTime, visemeIndex,
                    rawVisemes, reconstructedFastVisemes, reconstructedSlowVisemes);
                reconstructedFastVisemes = hold.fast;
                reconstructedSlowVisemes = hold.slow;
            }

            var speechPresence = speechHangover.presence;
            var voiceGainBase = graph.Param("Voice/GainBase", 0f);
            graph.AddOperation(mathRoot, graph.MultiplyUnlessHeldSilence(
                speechPresence, voiceAmplitude, voiceGainBase,
                visemeIndex, speechHangover.history,
                tuning[AdvancedVisemeTuningControl.SilenceStability], false));
            var voiceGainBoosted = graph.Param("Voice/GainBoosted", 0f);
            graph.AddOperation(mathRoot, graph.Multiply(
                tuning[AdvancedVisemeTuningControl.SpeechMotion],
                voiceGainBase, voiceGainBoosted, false));
            var voiceGain = graph.Param("Voice/Gain", 0f);
            // Saturating gate, not a linear amplitude. Scaling the mouth SHAPE by
            // the loudness envelope (0.55 + 0.45*loudness) made the mouth pull
            // ~45% toward rest in every inter-syllable dip ("goes and comes back")
            // and overshoot on onset loudness peaks. Reaching full amplitude at
            // half the quiet floor keeps the mouth open across the phrase like
            // native visemes; onset peaks land in the flat region (no overshoot);
            // rest-close still happens smoothly through speechPresence's release.
            graph.AddOperation(mathRoot, graph.Map(
                voiceGainBoosted, voiceGain, new[]
                {
                    Point(0f, 0f), Point(0.5f, 1f), Point(2f, 1f)
                }));

            for (var i = 0; i < rawVisemes.Length; i++)
            {
                speechWeights[i] = graph.Param($"Viseme/{i}/SpeechWeight", 0f);
                fastSpeechWeights[i] = graph.Param($"Viseme/{i}/FastSpeechWeight", 0f);
            }
            AddElementwiseProductProjection(
                graph, mathRoot, voiceGain,
                reconstructedSlowVisemes, speechWeights,
                "Voice-weighted viseme simplex");
            AddElementwiseProductProjection(
                graph, mathRoot, voiceGain,
                reconstructedFastVisemes, fastSpeechWeights,
                "Voice-weighted fast viseme simplex");

            string alphaTracking = null;
            string alphaTrackingMotion = null;
            string localFactor = null;
            var trackingRaw = new Dictionary<AdvancedVisemeArticulator, string>();
            var trackingFast = new Dictionary<AdvancedVisemeArticulator, string>();
            var trackingSlow = new Dictionary<AdvancedVisemeArticulator, string>();

            if (request.trackingEnabled)
            {
                var manualTrackingWeight = MathGraph.AlwaysOneParameter;
                if (request.component.trackingInputs != AdvancedVisemeTrackingInputs.Auto)
                {
                    result.manualTrackingParameter = prefix + "/FaceTrackingEnabled";
                    graph.AddParameter(result.manualTrackingParameter, AnimatorControllerParameterType.Float, 1f);
                    result.externalParameters.Add(result.manualTrackingParameter);
                    manualTrackingWeight = result.manualTrackingParameter;
                }
                var activeParameter = string.IsNullOrEmpty(request.trackingActiveParameter)
                    ? "LipTrackingActive"
                    : request.trackingActiveParameter;
                var activeWeight = activeParameter;
                if (request.trackingActiveAnimatorType ==
                    AnimatorControllerParameterType.Bool)
                {
                    // Respect an authored Bool declaration when merging tailored
                    // controllers. Fresh/generated and established VRCFT buses
                    // use the documented Bool-on-wire/Float-in-Animator conversion
                    // and therefore avoid this compatibility selector entirely.
                    graph.AddParameter(activeParameter, AnimatorControllerParameterType.Bool,
                        request.trackingActiveDefault);
                    AddBoolFloatLayer(
                        controller, graph, activeParameter, "TrackingActiveFactor",
                        request.trackingActiveDefault > 0.5f,
                        "Tracking Active Selector", out activeWeight);
                }
                else
                {
                    graph.AddParameter(activeParameter, AnimatorControllerParameterType.Float,
                        request.trackingActiveDefault);
                }
                result.trackingActiveWeightParameter = activeWeight;
                result.externalParameters.Add(activeParameter);
                if (request.reuseExistingTracking && request.auxiliaryTrackingParameterNames != null)
                {
                    foreach (var parameter in request.auxiliaryTrackingParameterNames.Values
                                 .Where(value => !string.IsNullOrWhiteSpace(value))
                                 .Distinct(StringComparer.Ordinal))
                    {
                        graph.AddParameter(parameter, AnimatorControllerParameterType.Float, 0f);
                        result.externalParameters.Add(parameter);
                    }
                }
                var trackingGate = graph.Param("TrackingGate", 0f);
                graph.AddOperation(mathRoot,
                    graph.Multiply(activeWeight, manualTrackingWeight, trackingGate, false));
                localFactor = "IsLocal";

                var trackingSmoothness =
                    tuning[AdvancedVisemeTuningControl.TrackingSmoothness];
                var alphaLocal = BuildTunableAlpha(
                    graph, mathRoot, frameTime, "Alpha/TrackingLocal",
                    request.profile.localTrackingResponseSeconds,
                    trackingSmoothness, 0.006f, 0.08f);
                var alphaRemote = BuildTunableAlpha(
                    graph, mathRoot, frameTime, "Alpha/TrackingRemote",
                    request.profile.remoteTrackingResponseSeconds,
                    trackingSmoothness, 0.015f, 0.2f);
                alphaTracking = graph.Param("Alpha/Tracking", 0.5f);
                alphaTrackingMotion = graph.Param("Alpha/TrackingMotion", 0.5f);
                graph.AddOperation(mathRoot, graph.AlphaFromDeltaTime(
                    frameTime, alphaTrackingMotion, TrackingMotionResponseSeconds));
                graph.AddOperation(mathRoot, graph.Interpolate(
                    alphaRemote, alphaLocal, alphaTracking, localFactor, false));

                // Acquiring an already-live tracker should feel immediate; losing
                // it should still cross-fade conservatively back to speech. A
                // single asymmetric pole avoids the former ~0.57 s two-pole lag.
                var alphaTrackingBlendAttack = graph.Param("Alpha/TrackingBlendAttack", 0.35f);
                var alphaTrackingBlend = graph.Param("Alpha/TrackingBlend", 0.2f);
                graph.AddOperation(mathRoot, graph.AlphaFromDeltaTime(
                    frameTime, alphaTrackingBlendAttack,
                    request.profile.trackingAcquireResponseSeconds));
                var alphaTrackingBlendRelease = BuildTunableAlpha(
                    graph, mathRoot, frameTime, "Alpha/TrackingBlendRelease",
                    request.profile.trackingBlendResponseSeconds,
                    tuning[AdvancedVisemeTuningControl.TrackingRelease],
                    0.02f, 0.5f);
                graph.AddOperation(mathRoot, graph.Interpolate(
                    alphaTrackingBlendRelease, alphaTrackingBlendAttack,
                    alphaTrackingBlend, trackingGate, false));
                graph.AddOperation(mathRoot, graph.Smooth(
                    trackingGate, trackingBlend, alphaTrackingBlend, false));

                var observerRaw = new Dictionary<AdvancedVisemeArticulator, string>();
                var observerFast = new Dictionary<AdvancedVisemeArticulator, string>();
                var observerSlow = new Dictionary<AdvancedVisemeArticulator, string>();
                foreach (var articulator in TrackedArticulators(request.effectiveTrackingInputs))
                {
                    var binding = request.profile.FindBinding(articulator);
                    if (binding == null || string.IsNullOrWhiteSpace(binding.trackingParameter)) continue;
                    var signed = IsSigned(articulator);
                    if (!TryResolveTrackingParameter(request, articulator, binding, out var parameter)) continue;
                    var input = UsesBinaryTracking(request)
                        ? DecodeBinaryTracking(graph, mathRoot, parameter, articulator, signed, request.component.trackingEncoding)
                        : parameter;
                    if (UsesBinaryTracking(request))
                    {
                        result.externalParameters.AddRange(BinaryParameterNames(
                            parameter, articulator, request.component.trackingEncoding));
                    }
                    else
                    {
                        graph.AddParameter(input, AnimatorControllerParameterType.Float, 0f);
                        result.externalParameters.Add(input);
                    }
                    trackingRaw[articulator] = input;
                    if (request.directPoseArticulators != null &&
                        request.directPoseArticulators.Contains(articulator))
                    {
                        // This is already the template's final decoded pose bus.
                        // Preserve its native local/remote response exactly.
                        trackingFast[articulator] = input;
                        trackingSlow[articulator] = input;
                        continue;
                    }
                    var fast = graph.Param($"Tracking/{articulator}/Fast", 0f);
                    var slow = graph.Param($"Tracking/{articulator}/Slow", 0f);
                    trackingFast[articulator] = fast;
                    trackingSlow[articulator] = slow;
                    observerRaw[articulator] = input;
                    observerFast[articulator] = fast;
                    observerSlow[articulator] = slow;
                }

                if (observerFast.Count > 0)
                {
                    // Every non-native tracking coordinate uses the same pole.
                    // Evaluating the observer as two articulation vectors is
                    // algebraically identical to one Smooth tree per scalar, but
                    // shares the interpolation traversal and zero baselines.
                    graph.AddOperation(mathRoot, graph.InterpolateArticulationVector(
                        observerFast, observerRaw, observerFast, alphaTracking,
                        "Tracking observer fast vector"));
                    graph.AddOperation(mathRoot, graph.InterpolateArticulationVector(
                        observerSlow, observerFast, observerSlow, alphaTracking,
                        "Tracking observer slow vector"));
                }
            }
            // One signed, creator-facing character control selects three
            // persistent endpoints: the calm two-pole state, the responsive
            // one-pole state, or the learned state-local target. This is a
            // convex simplex interpolation at every point. The center therefore
            // stays smooth through arbitrary hard-viseme interruptions, while
            // the endpoints are deliberately and visibly different.
            var speechCharacter =
                tuning[AdvancedVisemeTuningControl.SpeechLiveliness];
            var styledVisemes = Enumerable.Range(0, rawVisemes.Length)
                .Select(i => graph.Param(
                    $"Viseme/{i}/Styled", i == 0 ? 1f : 0f))
                .ToArray();
            // A single exponential pole has a decaying step response: it moves
            // fast then settles, which reads as hold-jump-hold. Cascading poles
            // drives the impulse response rectangular -> triangular -> Gaussian,
            // giving an S-shaped step (slow-in, slow-out) and near-uniform
            // motion. The density matrix is fitted at this order.
            var styledLow = slowVisemes;
            var styledMid = fastVisemes;
            if (EnableDensityHalo)
            {
                var pole3 = Enumerable.Range(0, slowVisemes.Length)
                    .Select(i => graph.Param($"Viseme/{i}/Pole3", i == 0 ? 1f : 0f))
                    .ToArray();
                var pole4 = Enumerable.Range(0, slowVisemes.Length)
                    .Select(i => graph.Param($"Viseme/{i}/Pole4", i == 0 ? 1f : 0f))
                    .ToArray();
                var midPole = Enumerable.Range(0, fastVisemes.Length)
                    .Select(i => graph.Param($"Viseme/{i}/MidPole", i == 0 ? 1f : 0f))
                    .ToArray();
                graph.AddOperation(mathRoot, graph.SmoothVector(
                    slowVisemes, pole3, alphaViseme, "Density observer pole 3"));
                graph.AddOperation(mathRoot, graph.SmoothVector(
                    pole3, pole4, alphaViseme, "Density observer pole 4"));
                graph.AddOperation(mathRoot, graph.SmoothVector(
                    fastVisemes, midPole, alphaViseme, "Density observer mid pole"));
                styledLow = pole4;
                styledMid = midPole;
            }
            graph.AddOperation(mathRoot, graph.BlendThreeVectors(
                styledLow, styledMid, rawVisemes, styledVisemes,
                speechCharacter, "Speech character simplex"));
            var publishedStyledVisemes = styledVisemes;

            // (The hold generator now injects on reconstructedFast/Slow above,
            // which is what the lower-face mouth actually reads; styledVisemes is
            // only the published diagnostic simplex.)

            // Legacy physical helpers still take a nonnegative lead value. The
            // direct physical vectors below now use the same styled endpoint in
            // both slots, so this value is only a harmless compatibility input.
            // renderLead=1 rides the fast observer entirely (crisp but overshoots
            // as it leads the slow state). Capping the top endpoint below 1 blends
            // in the slow observer, damping the onset overshoot the user reported.
            var speechRenderLead = graph.Param("Speech/RenderLead", 0f);
            graph.AddOperation(mathRoot, graph.Map(
                speechCharacter, speechRenderLead, new[]
                {
                    Point(-1f, 0f), Point(0f, 0f), Point(1f, SpeechRenderLeadCap)
                }));

            var renderedVisemes = new string[reconstructedSlowVisemes.Length];
            for (var i = 0; i < renderedVisemes.Length; i++)
                renderedVisemes[i] = graph.Param(
                    AdvancedVisemeParameterContract.Viseme(prefix, i),
                    i == 0 ? 1f : 0f,
                    false);
            result.globalParameters.AddRange(renderedVisemes);

            var renderedSpeechWeights = new string[speechWeights.Length];
            for (var i = 0; i < renderedSpeechWeights.Length; i++)
                renderedSpeechWeights[i] = graph.Param(
                    $"Viseme/{i}/RenderedSpeechWeight", 0f);
            var styledSpeechWeights = Enumerable.Range(0, speechWeights.Length)
                .Select(i => graph.Param(
                    $"Viseme/{i}/StyledSpeechWeight", 0f))
                .ToArray();
            AddElementwiseProductProjection(
                graph, mathRoot, voiceGain,
                styledVisemes, styledSpeechWeights,
                "Styled voice-weighted speech simplex");

            if (request.trackingEnabled)
            {
                graph.AddOperation(mathRoot, graph.InterpolateVector(
                    publishedStyledVisemes.Concat(styledSpeechWeights).ToArray(),
                    reconstructedSlowVisemes.Concat(speechWeights).ToArray(),
                    renderedVisemes.Concat(renderedSpeechWeights).ToArray(),
                    trackingBlend,
                    "Styled speech to tracked observer handoff"));
            }
            else
            {
                graph.AddOperation(mathRoot, graph.CopyVector(
                    publishedStyledVisemes.Concat(styledSpeechWeights).ToArray(),
                    renderedVisemes.Concat(renderedSpeechWeights).ToArray(),
                    "Styled speech public render vector"));
            }

            var vowelWeightRaw = graph.Param("Speech/VowelWeightRaw", 0f);
            var vowelWeight = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "Vowel"), 0f, false);
            graph.AddOperation(mathRoot, graph.Linear(vowelWeightRaw, new[]
            {
                Term.Positive(renderedVisemes[10], 1f), Term.Positive(renderedVisemes[11], 1f),
                Term.Positive(renderedVisemes[12], 1f), Term.Positive(renderedVisemes[13], 1f),
                Term.Positive(renderedVisemes[14], 1f)
            }));
            graph.AddOperation(mathRoot,
                graph.Multiply(speechPresence, vowelWeightRaw, vowelWeight, false));
            result.globalParameters.Add(vowelWeight);

            var articulationFast = new Dictionary<AdvancedVisemeArticulator, string>();
            var articulationSlow = new Dictionary<AdvancedVisemeArticulator, string>();
            var speechArticulationFast = new Dictionary<AdvancedVisemeArticulator, string>();
            var speechArticulationSlow = new Dictionary<AdvancedVisemeArticulator, string>();
            var modelSpeechCenters = new Dictionary<AdvancedVisemeArticulator, string>();
            var calibratedTrackingRaw = new Dictionary<AdvancedVisemeArticulator, string>();
            var calibratedTrackingFast = new Dictionary<AdvancedVisemeArticulator, string>();
            var calibratedTrackingSlow = new Dictionary<AdvancedVisemeArticulator, string>();
            var calibratedTrackingPose = new Dictionary<AdvancedVisemeArticulator, string>();
            var calibratedTrackingLead = new Dictionary<AdvancedVisemeArticulator, string>();
            var trackingGains = new Dictionary<AdvancedVisemeArticulator, string>();
            var allArticulators = SynthesizedArticulators().ToArray();
            var articulators = allArticulators.Where(articulator =>
                    IsArticulationLaneActive(
                        request, articulator, trackingRaw,
                        betaFaceInferenceEnabled))
                .ToArray();
            var inactiveArticulators = allArticulators
                .Except(articulators)
                .ToArray();
            // Build the complete speech prior first. Beta inference needs both the
            // visible speech center and calibrated visible tracking before tongue
            // channels are fused, so articulation cannot be constructed in one
            // order-dependent pass.
            var normalFastProjection =
                new Dictionary<AdvancedVisemeArticulator, string>();
            var normalSlowProjection =
                new Dictionary<AdvancedVisemeArticulator, string>();
            var corpusFastProjection = new Dictionary<
                AdvancedVisemeArticulatorGroup,
                Dictionary<AdvancedVisemeArticulator, string>>();
            var corpusSlowProjection = new Dictionary<
                AdvancedVisemeArticulatorGroup,
                Dictionary<AdvancedVisemeArticulator, string>>();
            var betaUnscaledFast =
                new Dictionary<AdvancedVisemeArticulator, string>();
            var betaUnscaledSlow =
                new Dictionary<AdvancedVisemeArticulator, string>();

            foreach (var articulator in articulators)
            {
                var speechFast = graph.Param($"Articulation/{articulator}/SpeechFast", 0f);
                var speechSlow = graph.Param($"Articulation/{articulator}/SpeechSlow", 0f);
                var modelSpeechCenter = speechSlow;
                if (betaGraph == null)
                {
                    normalFastProjection[articulator] = speechFast;
                    normalSlowProjection[articulator] = speechSlow;
                }
                else if (GetAdjustedSpeechCoefficients(request, articulator)
                         .Any(value => value != 0f))
                {
                    var group = AdvancedVisemeCoarticulationModel.GroupFor(articulator);
                    if (!corpusFastProjection.TryGetValue(group, out var fastOutputs))
                    {
                        fastOutputs = new Dictionary<AdvancedVisemeArticulator, string>();
                        corpusFastProjection[group] = fastOutputs;
                        corpusSlowProjection[group] =
                            new Dictionary<AdvancedVisemeArticulator, string>();
                    }
                    var unscaledFast = graph.Param($"Articulation/{articulator}/CorpusFast", 0f);
                    var unscaledSlow = graph.Param($"Articulation/{articulator}/CorpusSlow", 0f);
                    fastOutputs[articulator] = unscaledFast;
                    corpusSlowProjection[group][articulator] = unscaledSlow;
                    betaUnscaledFast[articulator] = unscaledFast;
                    betaUnscaledSlow[articulator] = unscaledSlow;
                    // The corpus model is centered in normalized articulator space.
                    // Voice is an expressive amplitude, not part of that semantic
                    // calibration, so use the unscaled coarticulated center here.
                    modelSpeechCenter = unscaledSlow;

                }

                speechArticulationFast[articulator] = speechFast;
                speechArticulationSlow[articulator] = speechSlow;
                modelSpeechCenters[articulator] = modelSpeechCenter;
                // Output correction only needs to know which articulators have a
                // speech basis; it reconstructs that basis directly from the
                // visible simplex. Keeping a second projected scalar here was a
                // dead Animator output (and tongue tuning multiplied it again).
                result.speechArticulationParameters[articulator] = speechSlow;
            }

            if (betaGraph == null)
            {
                AddVisemeMatrixProjection(
                    graph, mathRoot, request, "Speech articulation fast",
                    fastSpeechWeights, normalFastProjection);
                AddVisemeMatrixProjection(
                    graph, mathRoot, request, "Speech articulation slow",
                    speechWeights, normalSlowProjection);
            }
            else
            {
                foreach (var group in corpusFastProjection.Keys
                             .OrderBy(value => (int)value))
                    AddContractedBetaArticulationProjection(
                        graph, mathRoot, request, group, betaGraph,
                        betaProjectedFast, betaProjectedSlow,
                        betaProjectionOffsets, betaProjectionScales,
                        corpusFastProjection[group],
                        corpusSlowProjection[group], visemeIndex,
                        speechHangover.history,
                        tuning[AdvancedVisemeTuningControl.SilenceStability]);
                if (betaUnscaledFast.Count > 0)
                {
                    var betaSpeechFastOutputs = betaUnscaledFast.Keys.ToDictionary(
                        articulator => articulator,
                        articulator => speechArticulationFast[articulator]);
                    var betaSpeechSlowOutputs = betaUnscaledSlow.Keys.ToDictionary(
                        articulator => articulator,
                        articulator => speechArticulationSlow[articulator]);
                    graph.AddOperation(mathRoot, graph.ScaleArticulationVector(
                        voiceGain, betaUnscaledFast, betaSpeechFastOutputs,
                        "Voice-scaled corpus articulation fast"));
                    graph.AddOperation(mathRoot, graph.ScaleArticulationVector(
                        voiceGain, betaUnscaledSlow, betaSpeechSlowOutputs,
                        "Voice-scaled corpus articulation slow"));
                }
            }

            foreach (var articulator in articulators)
            {
                if (!request.trackingEnabled || !trackingSlow.TryGetValue(articulator, out var trackedSlow))
                    continue;
                var binding = request.profile.FindBinding(articulator);
                AdvancedVisemeExternalPose externalPose = null;
                if (request.reuseExistingTracking && request.externalPoses != null)
                    request.externalPoses.TryGetValue(articulator, out externalPose);
                var calibratedRaw = Calibrate(
                    graph, mathRoot, trackingRaw[articulator], binding,
                    articulator, "ModelRaw", externalPose);
                calibratedTrackingRaw[articulator] = calibratedRaw;
                if (request.reuseExistingTracking &&
                    request.directPoseArticulators != null &&
                    request.directPoseArticulators.Contains(articulator))
                {
                    // A structurally pose-connected template proxy is already
                    // decoded, merged, and smoothed by that template. Calibrating
                    // it three times and feeding it through the adaptive observer
                    // only duplicated work and added parameter-frame latency.
                    calibratedTrackingSlow[articulator] = calibratedRaw;
                    calibratedTrackingFast[articulator] = calibratedRaw;
                    calibratedTrackingPose[articulator] = calibratedRaw;
                    calibratedTrackingLead[articulator] = calibratedRaw;
                    continue;
                }
                calibratedTrackingSlow[articulator] =
                    Calibrate(graph, mathRoot, trackedSlow, binding, articulator, "Slow", externalPose);
                calibratedTrackingFast[articulator] =
                    Calibrate(graph, mathRoot, trackingFast[articulator], binding, articulator, "Fast", externalPose);

                // Both reused proxy streams and freshly decoded parameters need one
                // and only one denoising stage here. The adaptive fast/slow observer
                // follows deliberate motion with the one-pole signal, but settles
                // onto the two-pole signal when only OSC or quantization noise is
                // moving. Raw values are never published to the mesh.
                var pose = BuildAdaptiveTrackingPose(
                    graph, mathRoot, articulator,
                    calibratedTrackingFast[articulator], calibratedTrackingSlow[articulator],
                    alphaTrackingMotion, localFactor, out var motion);
                calibratedTrackingPose[articulator] = pose;
                var lead = graph.Param($"Tracking/{articulator}/Lead", 0f);
                graph.AddOperation(mathRoot, graph.Interpolate(
                    calibratedTrackingFast[articulator], calibratedTrackingRaw[articulator],
                    lead, motion, IsSigned(articulator)));
                calibratedTrackingLead[articulator] = lead;
            }

            var nativeTongueCapabilities = BuildNativeTongueCapabilities(
                graph, mathRoot, request, frameTime, trackingBlend, trackingRaw);
            var buildsLowerFaceOutput = request.component.mouthOwnership ==
                                        AdvancedVisemeMouthOwnership.DriveLowerFace;
            var usesResidualOutput = buildsLowerFaceOutput &&
                                     UsesResidualOutputPath(request);

            foreach (var articulator in articulators)
            {
                if (!calibratedTrackingPose.ContainsKey(articulator)) continue;
                var binding = request.profile.FindBinding(articulator);
                var remoteReliability = request.component.fusionMode == AdvancedVisemeFusionMode.TrackerAuthoritative
                    ? 1f
                    : binding.remoteReliability;
                var reliability = BuildTrackingAuthority(
                    graph, mathRoot, articulator,
                    speechArticulationSlow[articulator], calibratedTrackingPose[articulator],
                    localFactor, remoteReliability,
                    tuning[AdvancedVisemeTuningControl.RemoteTrust]);
                var baseGain = graph.Param($"Tracking/{articulator}/BaseGain", 0f);
                graph.AddOperation(mathRoot, graph.Multiply(
                    trackingBlend, reliability, baseGain, false));
                var gain = baseGain;
                if (RequiresNativeTongueCapability(articulator))
                {
                    gain = graph.Param($"Tracking/{articulator}/Gain", 0f);
                    var nativeTongueCapability = nativeTongueCapabilities.TryGetValue(
                        articulator, out var capability)
                        ? capability
                        : graph.Param($"Tracking/{articulator}/NativeCapability", 0f);
                    graph.AddOperation(mathRoot,
                        graph.Multiply(baseGain, nativeTongueCapability, gain, false));
                }
                trackingGains[articulator] = gain;
                result.trackingGainParameters[articulator] = gain;
                if (request.reuseExistingTracking &&
                    request.directPoseArticulators != null &&
                    request.directPoseArticulators.Contains(articulator))
                    continue;
                // The inverse is consumed only by the signed-ray reconciliation
                // of an external calibrated basis. Ordinary residual ownership,
                // fallback clips, and Outputs Only never read it.
                if (!buildsLowerFaceOutput || !usesResidualOutput ||
                    !UsesExternalCalibratedRayOutput(request, articulator))
                    continue;
                var inverseGain = graph.Param($"Tracking/{articulator}/InverseGain", 1f);
                graph.AddOperation(mathRoot, graph.Linear(inverseGain, new[]
                {
                    Term.Constant(1f), Term.Positive(gain, -1f)
                }));
                result.inverseTrackingGainParameters[articulator] = inverseGain;
            }

            var visibleSpeechWeights = buildsLowerFaceOutput && !usesResidualOutput
                ? BuildVisibleSpeechWeights(
                    graph, mathRoot, request, renderedSpeechWeights, trackingGains)
                : renderedSpeechWeights.ToArray();

            // The learned estimator is intentionally absent from Normal mode.
            // It is inserted here, before direct tongue measurements, so a real
            // tongue tracker remains authoritative at gain=1.
            FacePhonePosteriorGraph facePhonePosterior = null;
            if (betaGraph != null && betaFaceInferenceEnabled)
            {
                facePhonePosterior = ApplyBetaTongueInference(
                    graph, mathRoot, inferenceRoot,
                    conditionalAuthority,
                    request, result, betaGraph, frameTime,
                    speechPresence, voiceGain,
                    speechArticulationFast, speechArticulationSlow, modelSpeechCenters,
                    calibratedTrackingRaw, trackingGains, tuning);
            }

            ApplyTongueAxisStrengths(
                graph, mathRoot, speechArticulationFast, speechArticulationSlow,
                tuning);

            // These are the earliest complete speech coordinates. Rendering can
            // interpolate their motions directly; publishing another scalar and
            // reading it back from a later Animator epoch is only needed for the
            // public/debug contract, not for the mesh.
            var physicalSpeechArticulationFast =
                new Dictionary<AdvancedVisemeArticulator, string>(
                    speechArticulationFast);
            var physicalSpeechArticulationSlow =
                new Dictionary<AdvancedVisemeArticulator, string>(
                    speechArticulationSlow);
            var physicalSpeechArticulationNeedsVoice =
                new HashSet<AdvancedVisemeArticulator>();
            if (betaGraph != null)
            {
                // Corpus coordinates are already the complete adjusted C*p
                // projection. For ordinary jaw/lip axes, apply Voice to their
                // pose motion instead of materializing another scalar first.
                // Tongue lanes retain their post-inference/tuning parameters.
                foreach (var articulator in betaUnscaledFast.Keys)
                {
                    if (IsTunableTongueArticulator(articulator) ||
                        !betaUnscaledSlow.ContainsKey(articulator)) continue;
                    physicalSpeechArticulationFast[articulator] =
                        betaUnscaledFast[articulator];
                    physicalSpeechArticulationSlow[articulator] =
                        betaUnscaledSlow[articulator];
                    physicalSpeechArticulationNeedsVoice.Add(articulator);
                }
            }
            var directPhysicalSpeechArticulationFast =
                new Dictionary<AdvancedVisemeArticulator, string>();
            var directPhysicalSpeechArticulationSlow =
                new Dictionary<AdvancedVisemeArticulator, string>();
            var directPhysicalSpeechArticulationNeedsVoice =
                new HashSet<AdvancedVisemeArticulator>();
            var styledDirectArticulation = articulators
                .Where(articulator =>
                    directProjectedRaw.ContainsKey(articulator) &&
                    directProjectedFast.ContainsKey(articulator) &&
                    directProjectedSlow.ContainsKey(articulator))
                .ToDictionary(
                    articulator => articulator,
                    articulator => graph.Param(
                        $"DirectRender/Styled/{articulator}", 0f));
            if (styledDirectArticulation.Count > 0)
                graph.AddOperation(mathRoot,
                    graph.BlendThreeArticulationVectors(
                        directProjectedSlow, directProjectedFast,
                        directProjectedRaw, styledDirectArticulation,
                        speechCharacter,
                        "Speech character direct articulation"));
            bool IsMouthLane(AdvancedVisemeArticulator candidate)
            {
                var group = AdvancedVisemeCoarticulationModel.GroupFor(candidate);
                return group == AdvancedVisemeArticulatorGroup.Jaw ||
                       group == AdvancedVisemeArticulatorGroup.Lips;
            }

            var firPoses = EnableLookaheadFir
                ? BuildLookaheadFirPose(
                    request, graph, mathRoot, frameTime, visemeIndex,
                    tuning[AdvancedVisemeTuningControl.SpeechSmoothness],
                    tuning[AdvancedVisemeTuningControl.SpeechMotion],
                    tuning[AdvancedVisemeTuningControl.SpeechLiveliness],
                    articulators.Where(IsMouthLane))
                : new Dictionary<AdvancedVisemeArticulator, string>();

            foreach (var articulator in articulators)
            {
                // The direct coordinate is the projection evaluated at the HARD
                // winner (C.e_winner), baked into the argmax decoder state
                // machine. It is a single per-winner pose, crossfaded between at
                // most two winners, so the mouth snaps between discrete viseme
                // shapes and never co-activates. The smooth path below is the
                // same projection evaluated on the reconstructed SIMPLEX
                // (C.p over all 15 blended weights), which is what native
                // VisemeBlendShape does. Prefer the blended path when it exists.
                var haveBlended =
                    physicalSpeechArticulationFast.ContainsKey(articulator) &&
                    physicalSpeechArticulationSlow.ContainsKey(articulator);
                if ((!DriveMouthFromBlendedSimplex || !haveBlended) &&
                    styledDirectArticulation.TryGetValue(
                        articulator, out var directCoordinate))
                {
                    directPhysicalSpeechArticulationFast[articulator] =
                        directCoordinate;
                    directPhysicalSpeechArticulationSlow[articulator] =
                        directCoordinate;
                    directPhysicalSpeechArticulationNeedsVoice.Add(articulator);
                    continue;
                }
                // The FIR pose already reproduces the teacher's own amplitude and
                // co-activation, so it replaces the observer path outright and is
                // NOT scaled by voiceGain (voice is one of its basis terms).
                if (firPoses.TryGetValue(articulator, out var firPose))
                {
                    directPhysicalSpeechArticulationFast[articulator] = firPose;
                    directPhysicalSpeechArticulationSlow[articulator] = firPose;
                    continue;
                }
                // Slew-rate limit the mouth (jaw/lip) pose: cap its per-frame
                // speed so the argmax's full-swap jumps become constant-velocity
                // ramps (piecewise-linear = perceived-smooth) and sub-step chatter
                // is rejected. Pose space is signed with no simplex sum contract,
                // so per-channel clipping is safe here; tongue lanes are excluded.
                if (EnableMouthSlew && haveBlended && IsMouthLane(articulator))
                {
                    var slew = graph.Param(
                        $"Articulation/{articulator}/Slew", 0f);
                    AppendPoseSlew(
                        graph, mathRoot, frameTime,
                        physicalSpeechArticulationFast[articulator],
                        slew, MouthSlewSpeed);
                    directPhysicalSpeechArticulationFast[articulator] = slew;
                    directPhysicalSpeechArticulationSlow[articulator] = slew;
                    if (physicalSpeechArticulationNeedsVoice.Contains(articulator))
                        directPhysicalSpeechArticulationNeedsVoice.Add(articulator);
                    continue;
                }
                directPhysicalSpeechArticulationFast[articulator] =
                    physicalSpeechArticulationFast[articulator];
                directPhysicalSpeechArticulationSlow[articulator] =
                    physicalSpeechArticulationSlow[articulator];
                if (physicalSpeechArticulationNeedsVoice.Contains(articulator))
                    directPhysicalSpeechArticulationNeedsVoice.Add(articulator);
            }

            var styledSpeechArticulation = articulators.ToDictionary(
                articulator => articulator,
                articulator => graph.Param(
                    $"Articulation/{articulator}/StyledSpeech", 0f));
            var styledDirectSpeech = styledDirectArticulation.Keys.ToDictionary(
                articulator => articulator,
                articulator => styledSpeechArticulation[articulator]);
            if (styledDirectSpeech.Count > 0)
                graph.AddOperation(mathRoot, graph.ScaleArticulationVector(
                    voiceGain, styledDirectArticulation, styledDirectSpeech,
                    "Voice-scaled styled direct articulation"));
            var nonDirectStyledInputs = articulators
                .Where(articulator =>
                    !styledDirectArticulation.ContainsKey(articulator))
                .ToDictionary(
                    articulator => articulator,
                    articulator => speechArticulationFast[articulator]);
            var nonDirectStyledOutputs = nonDirectStyledInputs.Keys.ToDictionary(
                articulator => articulator,
                articulator => styledSpeechArticulation[articulator]);
            if (nonDirectStyledInputs.Count > 0)
                graph.AddOperation(mathRoot, graph.CopyArticulationVector(
                    nonDirectStyledInputs, nonDirectStyledOutputs,
                    "Styled non-direct articulation"));

            var renderedSpeechArticulation = articulators.ToDictionary(
                articulator => articulator,
                articulator => graph.Param(
                    $"Articulation/{articulator}/RenderedSpeech", 0f));
            graph.AddOperation(mathRoot,
                request.trackingEnabled
                    ? graph.InterpolateArticulationVector(
                        styledSpeechArticulation, speechArticulationSlow,
                        renderedSpeechArticulation, trackingBlend,
                        "Styled speech to tracked articulation handoff")
                    : graph.CopyArticulationVector(
                        styledSpeechArticulation, renderedSpeechArticulation,
                        "Styled speech articulation output"));

            string hiddenResidualSpeechDelta = null;
            if (facePhonePosterior != null &&
                !string.IsNullOrEmpty(facePhonePosterior.hiddenResidualDelta) &&
                request.calibration != null && request.calibration.success &&
                !string.IsNullOrEmpty(request.calibration.hiddenPhoneResidualBlendShapeName))
            {
                var hiddenResidualSpeechBase = graph.Param(
                    "PhonePosterior/Residual/SpeechBase", 0f);
                graph.AddOperation(mathRoot, graph.Multiply(
                    facePhonePosterior.hiddenResidualDelta, voiceGain,
                    hiddenResidualSpeechBase, true));
                hiddenResidualSpeechDelta = graph.Param(
                    "PhonePosterior/Residual/SpeechDelta", 0f);
                graph.AddOperation(mathRoot, graph.Multiply(
                    hiddenResidualSpeechBase,
                    tuning[AdvancedVisemeTuningControl.HiddenDetail],
                    hiddenResidualSpeechDelta, true));
            }

            foreach (var articulator in articulators)
            {
                var signed = IsSigned(articulator);
                var speechFast = speechArticulationFast[articulator];
                var speechSlow = renderedSpeechArticulation[articulator];

                var finalFast = speechFast;
                var finalSlow = speechSlow;
                var directPose = request.reuseExistingTracking &&
                                 request.directPoseArticulators != null &&
                                 request.directPoseArticulators.Contains(articulator);
                if (!directPose && trackingGains.TryGetValue(articulator, out var gain))
                {
                    var calibratedPose = calibratedTrackingPose[articulator];
                    var calibratedLead = calibratedTrackingLead[articulator];

                    // Calibrated residual output consumes the final fused
                    // articulation and ownership gain directly. A separate
                    // tracking product is needed only by fallback pose output or
                    // external signed-ray reconciliation.
                    if (buildsLowerFaceOutput &&
                        (!usesResidualOutput ||
                         UsesExternalCalibratedRayOutput(request, articulator)))
                    {
                        var trackingContribution = graph.Param(
                            $"Tracking/{articulator}/Contribution", 0f);
                        graph.AddOperation(mathRoot, graph.Multiply(
                            gain, calibratedPose, trackingContribution, signed));
                        result.trackingContributionParameters[articulator] =
                            trackingContribution;
                    }

                    finalSlow = graph.Param($"Articulation/{articulator}/FusedSlow", 0f);
                    finalFast = graph.Param($"Articulation/{articulator}/FusedFast", 0f);
                    // Convex interpolation is exactly
                    //   (1 - gain) * speech + gain * tracking.
                    // Encoding it as one 1D tree removes four scalar products,
                    // two sums, and their temporary AAPs per articulator. It also
                    // keeps the measured tracker on the shortest Animator path.
                    graph.AddOperation(mathRoot, graph.Interpolate(
                        speechSlow, calibratedPose, finalSlow, gain, signed));
                    graph.AddOperation(mathRoot, graph.Interpolate(
                        speechFast, calibratedLead, finalFast, gain, signed));
                }

                articulationFast[articulator] = finalFast;
                articulationSlow[articulator] = finalSlow;
            }

            if (request.trackingEnabled && request.component.fusionMode == AdvancedVisemeFusionMode.PhoneticAssist)
            {
                var constraintBases = BuildConstraintConfidenceBases(
                    graph, mathRoot, speechPresence, trackingBlend, localFactor,
                    trackingGains, tuning);
                ApplyConstraints(
                    graph, mathRoot, request.profile, reconstructedFastVisemes,
                    constraintBases, articulationFast, "Fast");
                ApplyConstraints(
                    graph, mathRoot, request.profile, renderedVisemes,
                    constraintBases, articulationSlow, "Slow");
            }

            // Keep the constrained/fused source before the public articulation
            // mirror replaces it below. Active tracking still renders this exact
            // endpoint; speech-only rendering bypasses the mirror's frame delay.
            var physicalFinalArticulation =
                new Dictionary<AdvancedVisemeArticulator, string>(
                    articulationSlow);

            // Do not impose a generic MouthOpen/MouthClosed or Pucker/Suck
            // envelope after measurement fusion. Tailored VRCFT templates often
            // use coupled, non-exclusive coordinates; clamping them here changed a
            // valid measured pose and created a hard switching surface. Authored
            // speech remains rig-defined, while only the sparse phonetic
            // constraints above may alter a tracked lower face.

            var publicArticulationSources =
                new Dictionary<AdvancedVisemeArticulator, string>();
            var publicArticulationOutputs =
                new Dictionary<AdvancedVisemeArticulator, string>();
            // Keep the complete public contract even when a lane is provably
            // constant zero for this build. A declared, unwritten Animator
            // parameter stays at its zero default without paying for observers,
            // fusion, velocity, or mesh-output motions.
            foreach (var articulator in inactiveArticulators)
            {
                var output = graph.Param(
                    prefix + "/Articulation/" + articulator, 0f, false);
                var velocity = graph.Param(
                    prefix + "/Velocity/" + articulator, 0f, false);
                result.globalParameters.Add(output);
                result.globalParameters.Add(velocity);
            }
            foreach (var articulator in articulators)
            {
                var signed = IsSigned(articulator);
                var source = articulationSlow[articulator];
                var output = graph.Param(prefix + "/Articulation/" + articulator, 0f, false);
                if (request.reuseExistingTracking &&
                    request.directPoseArticulators != null &&
                    request.directPoseArticulators.Contains(articulator) &&
                    calibratedTrackingRaw.TryGetValue(articulator, out var directTrackingOutput) &&
                    trackingGains.TryGetValue(articulator, out var directTrackingGain))
                {
                    // Public articulation keeps its normalized YUCP contract.
                    // Only the visible calibrated pose consumes the native proxy
                    // directly; this diagnostic mirror is never read back into
                    // rendering, so its calibration stage cannot add face lag.
                    graph.AddOperation(mathRoot, graph.SelectMotion(
                        directTrackingGain,
                        graph.Copy(source, output, signed),
                        graph.Copy(directTrackingOutput, output, signed),
                        $"Native {articulator} public output gate"));
                }
                else
                {
                    publicArticulationSources[articulator] = source;
                    publicArticulationOutputs[articulator] = output;
                }

                articulationSlow[articulator] = output;
                result.articulationParameters[articulator] = output;
                result.globalParameters.Add(output);
            }

            if (publicArticulationOutputs.Count > 0)
                graph.AddOperation(mathRoot, graph.CopyArticulationVector(
                    publicArticulationSources, publicArticulationOutputs,
                    "Public articulation vector"));

            var velocityRawOutputs = new Dictionary<AdvancedVisemeArticulator, string>();
            foreach (var articulator in articulators)
                velocityRawOutputs[articulator] = graph.Param(
                    $"Velocity/{articulator}/Raw", 0f);
            graph.AddOperation(mathRoot, graph.DifferenceArticulationVector(
                articulationFast, articulationSlow, velocityRawOutputs,
                1f / Mathf.Max(0.005f, request.profile.visemeResponseSeconds),
                "Articulation velocity difference vector"));

            foreach (var articulator in articulators)
            {
                var velocity = graph.Param(prefix + "/Velocity/" + articulator, 0f, false);
                graph.AddOperation(mathRoot, graph.Map(
                    velocityRawOutputs[articulator], velocity, new[]
                    {
                        Point(-1f, -1f), Point(0f, 0f), Point(1f, 1f)
                    }));
                result.globalParameters.Add(velocity);
            }

            BuildSpeechEvidence(graph, mathRoot, prefix, renderedVisemes, voiceSlow, result);

            if (conditionalLearnedDetailEnabled)
            {
                AssertUnnormalizedDirectBlendTree(
                    mathRoot, "Reconstruction Math");
                AssertUnnormalizedDirectBlendTree(
                    inferenceRoot,
                    "Conditional learned inference compute");
                if (conditionalBetaContextSleepEnabled)
                {
                    AssertUnnormalizedDirectBlendTree(
                        betaContextRoot,
                        "Conditional Beta context compute");
                    graph.AddOperation(mathRoot, graph.SelectMotion(
                        conditionalCompute,
                        graph.MultiSetter(
                            "Conditional Beta context sleep equilibrium",
                            betaGraph.sleepEquilibrium),
                        betaContextRoot,
                        "Conditional Beta context compute endpoint"));
                }
                graph.AddGatedOperation(
                    mathRoot, inferenceRoot, conditionalCompute);
            }
            FinalizeSharedSilenceUpdateAuthorityLayer(
                graph, sharedSilenceLayer, speechHangover.history,
                tuning[AdvancedVisemeTuningControl.SilenceStability],
                simplexSparsifier);
            AddMotionLayer(controller, graph, "YUCP AVR Math", mathRoot);

            if (request.component.mouthOwnership == AdvancedVisemeMouthOwnership.DriveLowerFace)
            {
                var outputRoot = graph.Direct("Lower Face Output");
                // The output tree applies the same dead-zone as the sparse
                // publication directly as a motion threshold. Reading the dense
                // observer state here removes the extra sparse-AAP epoch without
                // reactivating its tiny exponential tails.
                // Beta context remains an inference signal. The physical mouth
                // keeps that inference for tracking, while the no-tracker mouth
                // follows the fitted direct simplex trajectory.
                BuildOutputTree(
                    request, result, graph, outputRoot,
                    renderedSpeechWeights, visibleSpeechWeights, styledVisemes,
                    voiceGain, speechRenderLead,
                    trackingBlend,
                    directPhysicalSpeechArticulationFast,
                    directPhysicalSpeechArticulationSlow,
                    directPhysicalSpeechArticulationNeedsVoice,
                    physicalFinalArticulation,
                    hiddenResidualSpeechDelta,
                    tuning[AdvancedVisemeTuningControl.AuthoredDetail],
                    tuning[AdvancedVisemeTuningControl.ContradictionFade],
                    trackingGains);
                AddMotionLayer(controller, graph, "YUCP AVR Output", outputRoot);
            }

            BuildCompactTuningSync(controller, graph, request, result);

            var expressionParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            expressionParameters.name = "YUCP Advanced Viseme Inputs";
            expressionParameters.parameters = BuildExpressionParameters(request, result).ToArray();
            AssetDatabase.CreateAsset(expressionParameters, request.parametersPath);
            result.parameters = expressionParameters;

            foreach (var global in result.globalParameters.Distinct())
            {
                graph.AddParameter(global, AnimatorControllerParameterType.Float, global.EndsWith("/Viseme/sil", StringComparison.Ordinal) ? 1f : 0f);
            }

            // First lower local algebra, then perform closed-world liveness on
            // AVR's private AAP namespace. The liveness pass never composes an
            // AAP epoch; it removes only writes that cannot reach a public or
            // physical output. A second structural pass discards any motion
            // subtrees made empty by those dead writes.
            graph.PruneUnreachableMotions();
            result.optimizerReport = AdvancedVisemeAnimatorGraphOptimizer.Optimize(
                controller, internalPrefix, result.globalParameters);
            if (request.component.verboseLogging &&
                result.optimizerReport.removedAnimatorCurves > 0)
            {
                var groups = string.Join(", ", result.optimizerReport
                    .removedCurvesByGroup
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Key + "=" + pair.Value));
                Debug.Log(
                    $"[YUCP Advanced Viseme] Exact graph liveness removed " +
                    $"{result.optimizerReport.removedInternalParameters} private " +
                    $"parameters and {result.optimizerReport.removedAnimatorCurves} " +
                    $"Animator curves ({result.optimizerReport.removedNeutralZeroCurves} " +
                    $"neutral-zero, {result.optimizerReport.removedDeadAnimatorCurves} " +
                    $"dead; {groups}).",
                    request.component);
            }
            graph.PruneUnreachableMotions();
            if (RunPostFoldOptimizerFixpointForTests)
            {
                // Structural folding can expose exact-zero cancellations after
                // the closed-world zero pass has already run. Iterate the two
                // exact transforms to a small bounded fixpoint for profiler A/B.
                for (var pass = 0; pass < 3; pass++)
                {
                    var postFold = AdvancedVisemeAnimatorGraphOptimizer.Optimize(
                        controller, internalPrefix, result.globalParameters);
                    MergeOptimizerReport(result.optimizerReport, postFold);
                    graph.PruneUnreachableMotions();
                    if (postFold.removedAnimatorCurves == 0 &&
                        postFold.removedInternalParameters == 0)
                        break;
                }
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            AssetDatabase.SaveAssetIfDirty(expressionParameters);
            AssetDatabase.ImportAsset(request.controllerPath);
            return result;
        }

        private static void MergeOptimizerReport(
            AdvancedVisemeAnimatorGraphOptimizer.Report cumulative,
            AdvancedVisemeAnimatorGraphOptimizer.Report next)
        {
            cumulative.internalParametersAfter =
                next.internalParametersAfter;
            cumulative.animatorCurvesAfter = next.animatorCurvesAfter;
            cumulative.removedInternalParameters +=
                next.removedInternalParameters;
            cumulative.removedAnimatorCurves +=
                next.removedAnimatorCurves;
            cumulative.removedNeutralZeroCurves +=
                next.removedNeutralZeroCurves;
            cumulative.removedDeadAnimatorCurves +=
                next.removedDeadAnimatorCurves;
            cumulative.internedCongruentParameters +=
                next.internedCongruentParameters;
            cumulative.removedCongruentCurves +=
                next.removedCongruentCurves;
            cumulative.liveInternalParameters =
                next.liveInternalParameters;
            cumulative.deadInternalParameters =
                next.deadInternalParameters;
            foreach (var mapping in next.internedParameterMappings)
            {
                cumulative.internedParameterMappings[mapping.Key] =
                    mapping.Value;
            }
            foreach (var group in next.removedCurvesByGroup)
            {
                cumulative.removedCurvesByGroup[group.Key] =
                    cumulative.removedCurvesByGroup.TryGetValue(
                        group.Key,
                        out int prior)
                        ? prior + group.Value
                        : group.Value;
            }
        }

        private static Dictionary<AdvancedVisemeTuningControl, string> BuildTuningParameters(
            MathGraph graph,
            BlendTree root,
            Request request,
            Result result)
        {
            var tuning = new Dictionary<AdvancedVisemeTuningControl, string>();
            foreach (var control in AdvancedVisemeTuning.Controls)
            {
                var configured =
                    AdvancedVisemeTuning.ConfiguredValue(request.profile, control);
                var section = AdvancedVisemeTuning.Section(control);
                var exposed = request.component.createTuningMenu &&
                              IsTuningControlRelevant(request, control) &&
                              (request.component.tuningMenuSections & section) != 0;
                if (!exposed)
                {
                    var configuredParameter = graph.Param(
                        "Tuning/" + control, configured);
                    tuning[control] = configuredParameter;
                    result.effectiveTuningParameters[control] =
                        configuredParameter;
                    continue;
                }

                var publicParameter = graph.Param(
                    request.component.TuningParameterName(control),
                    AdvancedVisemeTuning.SliderDefault, false);
                var effectiveParameter = graph.Param(
                    "Tuning/Effective/" + control, configured);
                var range = AdvancedVisemeTuning.Range(
                    request.profile, control);
                graph.AddOperation(root, graph.Map(
                    publicParameter, effectiveParameter, new[]
                    {
                        Point(0f, range.minimum),
                        Point(AdvancedVisemeTuning.SliderDefault,
                            range.configured),
                        Point(1f, range.maximum)
                    }));

                tuning[control] = effectiveParameter;
                result.tuningParameters[control] = publicParameter;
                result.effectiveTuningParameters[control] =
                    effectiveParameter;
                result.externalParameters.Add(publicParameter);
            }
            return tuning;
        }

        private static void BuildCompactTuningSync(
            AnimatorController controller,
            MathGraph graph,
            Request request,
            Result result)
        {
            if (request.useSharedParameterCompressor ||
                result.tuningParameters.Count == 0)
                return;

            var channels = result.tuningParameters
                .OrderBy(pair => (int)pair.Key)
                .ToArray();
            var prefix = request.component.NormalizedPrefix;
            result.tuningSyncDataParameter =
                AdvancedVisemeTuning.CompactSyncDataParameter(prefix);
            result.tuningSyncFocusParameter =
                AdvancedVisemeTuning.CompactSyncFocusParameter(prefix);
            result.tuningSyncBits =
                AdvancedVisemeTuning.CompactSyncTransportBits(channels.Length);

            graph.AddParameter(
                result.tuningSyncDataParameter,
                AnimatorControllerParameterType.Int, 0f);
            graph.AddParameter(
                result.tuningSyncFocusParameter,
                AnimatorControllerParameterType.Int, 0f);
            result.externalParameters.Add(result.tuningSyncDataParameter);
            result.externalParameters.Add(result.tuningSyncFocusParameter);

            var indexBitCount =
                AdvancedVisemeTuning.CompactSyncTransportIndexBits;
            for (var bit = 0; bit < indexBitCount; bit++)
            {
                var parameter =
                    AdvancedVisemeTuning.CompactSyncIndexParameter(prefix, bit);
                graph.AddParameter(parameter, AnimatorControllerParameterType.Bool, 0f);
                result.tuningSyncIndexParameters.Add(parameter);
                result.externalParameters.Add(parameter);
            }

            var clockParameter = graph.Param("TuningSync/Clock", 0f);
            var clock = graph.Clip("Compact tuning sync clock");
            const float batchSeconds = 0.1f;
            AnimationUtility.SetEditorCurve(
                clock,
                EditorCurveBinding.FloatCurve(
                    string.Empty, typeof(Animator), clockParameter),
                AnimationCurve.Linear(0f, 0f, batchSeconds, 1f));

            var machine = AddStateLayer(
                controller, graph, "YUCP AVR Compact Tuning Sync");
            // Parameter drivers and transitions continue to run at zero layer
            // weight, as in VRCFury's compressor. The timing clip supplies only
            // normalized state time and contributes no blended Animator output.
            var transportLayers = controller.layers;
            transportLayers[transportLayers.Length - 1].defaultWeight = 0f;
            controller.layers = transportLayers;
            var idle = machine.AddState("Route");
            var localRouter = machine.AddState("Local Focus Router");
            var remoteLost = machine.AddState("Remote Awaiting Channel");
            idle.writeDefaultValues = true;
            localRouter.writeDefaultValues = true;
            remoteLost.writeDefaultValues = true;
            machine.defaultState = idle;

            var localEntry = idle.AddTransition(localRouter);
            ConfigureImmediate(localEntry);
            localEntry.AddCondition(
                AnimatorConditionMode.Greater, 0.5f, "IsLocal");
            var remoteEntry = idle.AddTransition(remoteLost);
            ConfigureImmediate(remoteEntry);
            remoteEntry.AddCondition(
                AnimatorConditionMode.Less, 0.5f, "IsLocal");

            var localBecameRemote = localRouter.AddTransition(remoteLost);
            ConfigureImmediate(localBecameRemote);
            localBecameRemote.AddCondition(
                AnimatorConditionMode.Less, 0.5f, "IsLocal");
            var remoteBecameLocal = remoteLost.AddTransition(localRouter);
            ConfigureImmediate(remoteBecameLocal);
            remoteBecameLocal.AddCondition(
                AnimatorConditionMode.Greater, 0.5f, "IsLocal");

            var sendStates = new AnimatorState[channels.Length];
            var extraFrameStates = new AnimatorState[channels.Length];
            var receiveStates = new AnimatorState[channels.Length];
            for (var index = 0; index < channels.Length; index++)
            {
                var id = AdvancedVisemeTuning.CompactSyncChannelId(
                    channels[index].Key);
                var label = AdvancedVisemeTuning.Label(channels[index].Key);

                var send = machine.AddState("Send " + label);
                send.writeDefaultValues = true;
                send.motion = clock;
                AddTuningDriver(graph, send, true,
                    CompactIndexWrites(
                            id, result.tuningSyncIndexParameters)
                        .Concat(new[]
                        {
                            CompactCopy(
                                channels[index].Value,
                                result.tuningSyncDataParameter,
                                0f, 1f,
                                // Avatar Parameter Driver truncates Float->Int.
                                // The half-code offset makes that truncation an
                                // exact round-to-nearest over integer codes 0..254.
                                0.5f,
                                AdvancedVisemeTuning.CompactSyncQuantizationMaximum +
                                0.5f)
                        })
                        .ToArray());
                sendStates[index] = send;

                var extra = machine.AddState("Extra Frame " + label);
                extra.writeDefaultValues = true;
                extraFrameStates[index] = extra;

                var receive = machine.AddState("Receive " + label);
                receive.writeDefaultValues = true;
                receive.motion = clock;
                AddTuningDriver(graph, receive, false,
                    CompactCopy(
                        result.tuningSyncDataParameter,
                        channels[index].Value,
                        0f,
                        AdvancedVisemeTuning.CompactSyncQuantizationMaximum,
                        0f, 1f));
                receiveStates[index] = receive;

                var route = localRouter.AddTransition(send);
                ConfigureImmediate(route);
                route.AddCondition(
                    AnimatorConditionMode.Equals, id,
                    result.tuningSyncFocusParameter);

                var recover = remoteLost.AddTransition(receive);
                ConfigureImmediate(recover);
                recover.AddCondition(
                    AnimatorConditionMode.Less, 0.5f, "IsLocal");
                AddCompactIndexConditions(
                    recover, id, result.tuningSyncIndexParameters);
            }

            var unfocusedRoute = localRouter.AddTransition(sendStates[0]);
            ConfigureImmediate(unfocusedRoute);
            unfocusedRoute.AddCondition(
                AnimatorConditionMode.Equals, 0f,
                result.tuningSyncFocusParameter);

            for (var index = 0; index < channels.Length; index++)
            {
                var id = AdvancedVisemeTuning.CompactSyncChannelId(
                    channels[index].Key);
                var next = (index + 1) % channels.Length;
                var nextId = AdvancedVisemeTuning.CompactSyncChannelId(
                    channels[next].Key);

                var sendBecameRemote =
                    sendStates[index].AddTransition(remoteLost);
                ConfigureImmediate(sendBecameRemote);
                sendBecameRemote.AddCondition(
                    AnimatorConditionMode.Less, 0.5f, "IsLocal");

                var extraBecameRemote =
                    extraFrameStates[index].AddTransition(remoteLost);
                ConfigureImmediate(extraBecameRemote);
                extraBecameRemote.AddCondition(
                    AnimatorConditionMode.Less, 0.5f, "IsLocal");

                var receiveBecameLocal =
                    receiveStates[index].AddTransition(localRouter);
                ConfigureImmediate(receiveBecameLocal);
                receiveBecameLocal.AddCondition(
                    AnimatorConditionMode.Greater, 0.5f, "IsLocal");

                var continueFocused = sendStates[index].AddTransition(localRouter);
                ConfigureTimed(continueFocused, 1f);
                continueFocused.AddCondition(
                    AnimatorConditionMode.Greater, 0.5f,
                    result.tuningSyncFocusParameter);

                var continueCarousel =
                    sendStates[index].AddTransition(extraFrameStates[index]);
                ConfigureTimed(continueCarousel, 1f);
                continueCarousel.AddCondition(
                    AnimatorConditionMode.Equals, 0f,
                    result.tuningSyncFocusParameter);

                var prioritizeFocus =
                    extraFrameStates[index].AddTransition(localRouter);
                ConfigureImmediate(prioritizeFocus);
                prioritizeFocus.AddCondition(
                    AnimatorConditionMode.Greater, 0.5f,
                    result.tuningSyncFocusParameter);
                var nextSend =
                    extraFrameStates[index].AddTransition(sendStates[next]);
                ConfigureImmediate(nextSend);

                var receiveNext =
                    receiveStates[index].AddTransition(receiveStates[next]);
                ConfigureImmediate(receiveNext);
                receiveNext.AddCondition(
                    AnimatorConditionMode.Less, 0.5f, "IsLocal");
                AddCompactIndexConditions(
                    receiveNext, nextId, result.tuningSyncIndexParameters);

                // A focused radial can leave the same channel selected while its
                // data changes. Re-enter at the network cadence so the copy driver
                // samples the newest carrier value without a strobe parameter.
                var refresh =
                    receiveStates[index].AddTransition(receiveStates[index]);
                ConfigureTimed(refresh, 1f);
                refresh.canTransitionToSelf = true;
                refresh.AddCondition(
                    AnimatorConditionMode.Less, 0.5f, "IsLocal");
                AddCompactIndexConditions(
                    refresh, id, result.tuningSyncIndexParameters);

                // The expected-next transition is intentionally first. Every
                // other index mismatch falls back to the recovery router, which
                // handles dropped/reordered packets and focused-channel jumps.
                for (var bit = 0;
                     bit < result.tuningSyncIndexParameters.Count;
                     bit++)
                {
                    var expected = (id & (1 << bit)) != 0;
                    var lost = receiveStates[index].AddTransition(remoteLost);
                    ConfigureImmediate(lost);
                    lost.AddCondition(
                        AnimatorConditionMode.Less, 0.5f, "IsLocal");
                    lost.AddCondition(
                        expected ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If,
                        0f,
                        result.tuningSyncIndexParameters[bit]);
                }
            }
        }

        private static IEnumerable<VRC_AvatarParameterDriver.Parameter>
            CompactIndexWrites(int id, IReadOnlyList<string> parameters)
        {
            for (var bit = 0; bit < parameters.Count; bit++)
                yield return new VRC_AvatarParameterDriver.Parameter
                {
                    name = parameters[bit],
                    type = VRC_AvatarParameterDriver.ChangeType.Set,
                    value = (id & (1 << bit)) != 0 ? 1f : 0f
                };
        }

        private static VRC_AvatarParameterDriver.Parameter CompactCopy(
            string source,
            string destination,
            float sourceMinimum,
            float sourceMaximum,
            float destinationMinimum,
            float destinationMaximum)
        {
            return new VRC_AvatarParameterDriver.Parameter
            {
                source = source,
                name = destination,
                type = VRC_AvatarParameterDriver.ChangeType.Copy,
                convertRange = true,
                sourceMin = sourceMinimum,
                sourceMax = sourceMaximum,
                destMin = destinationMinimum,
                destMax = destinationMaximum
            };
        }

        private static void AddTuningDriver(
            MathGraph graph,
            AnimatorState state,
            bool localOnly,
            params VRC_AvatarParameterDriver.Parameter[] parameters)
        {
            var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
            driver.name = localOnly
                ? "YUCP Compact Tuning Sender"
                : "YUCP Compact Tuning Receiver";
            driver.localOnly = localOnly;
            driver.isEnabled = true;
            driver.debugString = localOnly
                ? "YUCP owner-only tuning transport"
                : "YUCP remote tuning decode";
            driver.parameters = parameters.ToList();
            graph.SubAsset(driver);
            state.behaviours = state.behaviours
                .Concat(new StateMachineBehaviour[] { driver })
                .ToArray();
        }

        private static void AddCompactIndexConditions(
            AnimatorStateTransition transition,
            int id,
            IReadOnlyList<string> parameters)
        {
            for (var bit = 0; bit < parameters.Count; bit++)
                transition.AddCondition(
                    (id & (1 << bit)) != 0
                        ? AnimatorConditionMode.If
                        : AnimatorConditionMode.IfNot,
                    0f, parameters[bit]);
        }

        private static void ConfigureImmediate(
            AnimatorStateTransition transition)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            transition.canTransitionToSelf = false;
            transition.interruptionSource = TransitionInterruptionSource.None;
        }

        private static void ConfigureTimed(
            AnimatorStateTransition transition,
            float exitTime)
        {
            transition.hasExitTime = true;
            transition.exitTime = Mathf.Max(0f, exitTime);
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            transition.canTransitionToSelf = false;
            transition.interruptionSource = TransitionInterruptionSource.None;
        }

        internal static bool IsTuningControlRelevant(
            Request request,
            AdvancedVisemeTuningControl control)
        {
            var beta = request.component.reconstructionMode ==
                       AdvancedVisemeReconstructionMode.BetaCoarticulation;
            var calibrated = request.calibration != null && request.calibration.success;
            var externalPoseCalibration = HasExternalPoseCalibration(request);
            var residualOutput = calibrated &&
                                 request.component.mouthOwnership ==
                                 AdvancedVisemeMouthOwnership.DriveLowerFace &&
                                 (!request.reuseExistingTracking || externalPoseCalibration) &&
                                 !request.profile.visemePoses.Any(
                                     pose => pose != null && pose.animationOverride != null) &&
                                 !request.profile.articulatorBindings.Any(binding =>
                                     binding != null &&
                                     (binding.animationOverride != null ||
                                      binding.negativeAnimationOverride != null));
            switch (control)
            {
                case AdvancedVisemeTuningControl.AuthoredDetail:
                    return residualOutput;
                case AdvancedVisemeTuningControl.Coarticulation:
                    return beta;
                case AdvancedVisemeTuningControl.TrackingSmoothness:
                case AdvancedVisemeTuningControl.TrackingRelease:
                case AdvancedVisemeTuningControl.RemoteTrust:
                    return request.trackingEnabled;
                case AdvancedVisemeTuningControl.ContradictionFade:
                    return request.trackingEnabled && residualOutput;
                case AdvancedVisemeTuningControl.ConstraintAmount:
                case AdvancedVisemeTuningControl.BilabialAssist:
                case AdvancedVisemeTuningControl.LabiodentalAssist:
                case AdvancedVisemeTuningControl.SibilantAssist:
                    return request.trackingEnabled &&
                           request.component.fusionMode ==
                           AdvancedVisemeFusionMode.PhoneticAssist;
                case AdvancedVisemeTuningControl.HiddenPhone:
                    return beta && request.trackingEnabled;
                case AdvancedVisemeTuningControl.HiddenDetail:
                    return beta && request.trackingEnabled && residualOutput &&
                           !string.IsNullOrEmpty(
                               request.calibration.hiddenPhoneResidualBlendShapeName);
                case AdvancedVisemeTuningControl.TongueInference:
                    return beta && request.trackingEnabled;
                default:
                    return true;
            }
        }

        private static SpeechHangoverGraph BuildSpeechHangover(
            MathGraph graph,
            BlendTree root,
            string frameTime,
            string visemeIndex,
            VisemeReconstructionProfile profile,
            string stabilityTuning,
            string publicPrefix,
            Result result)
        {
            // This is deliberately a soft VAD hangover rather than an explicit
            // countdown state machine. The asymmetric one-pole history rises in
            // roughly 60 ms, so sustained speech earns a full hold while a short
            // recognizer blip earns little or none. It then leaks away with the
            // profile response. There is no Voice input: a noisy microphone can
            // never keep an old phone alive after VRChat has stopped reporting it.
            var attackAlpha = graph.Param("Alpha/SpeechHistoryAttack", 0.25f);
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, attackAlpha,
                AdvancedVisemeMath.SpeechHistoryAttackSeconds));

            var configuredReleaseSeconds = Mathf.Clamp(
                profile.speechHangoverSeconds, 0.04f, 0.4f);
            var extendedReleaseSeconds = AdvancedVisemeMath.SpeechHistoryReleaseSeconds(
                configuredReleaseSeconds, 1f);
            var configuredReleaseAlpha = graph.Param(
                "Alpha/SpeechHistoryRelease/Configured", 0.1f);
            var extendedReleaseAlpha = graph.Param(
                "Alpha/SpeechHistoryRelease/Extended", 0.05f);
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, configuredReleaseAlpha, configuredReleaseSeconds));
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, extendedReleaseAlpha, extendedReleaseSeconds));
            var extendedReleaseBlend = graph.Param(
                "Speech/Hangover/ExtendedReleaseBlend", 0f);
            graph.AddOperation(root, graph.Map(
                stabilityTuning, extendedReleaseBlend, new[]
                {
                    Point(0f, 0f), Point(0.5f, 0f), Point(1f, 1f)
                }));
            var releaseAlpha = graph.Param("Alpha/SpeechHistoryRelease", 0.1f);
            graph.AddOperation(root, graph.Interpolate(
                configuredReleaseAlpha, extendedReleaseAlpha,
                releaseAlpha, extendedReleaseBlend, false));

            var history = graph.Param("Speech/Hangover/History", 0f);
            graph.AddOperation(root, graph.AsymmetricBinarySmooth(
                visemeIndex, history,
                0f, releaseAlpha,
                1f, attackAlpha,
                false));

            // Talking is a fast visual envelope, kept high by the same held-sil
            // decision. It must not inherit the 60 ms history attack because this
            // value gates speech gain and would otherwise erase quiet short phones.
            var activityAttack = graph.Param("Alpha/SpeechActivityAttack", 0.8f);
            var activityRelease = graph.Param("Alpha/SpeechActivityRelease", 0.25f);
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, activityAttack,
                AdvancedVisemeMath.SpeechPresenceAttackSeconds));
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, activityRelease,
                AdvancedVisemeMath.SpeechPresenceReleaseSeconds));
            var presence = graph.Param(
                AdvancedVisemeParameterContract.Speech(publicPrefix, "Talking"),
                0f, false);
            graph.AddOperation(root, graph.SmoothActivityWithSilenceHold(
                visemeIndex, history, stabilityTuning,
                presence, activityAttack, activityRelease));
            result.globalParameters.Add(presence);

            return new SpeechHangoverGraph
            {
                history = history,
                presence = presence
            };
        }

        private static string BuildTunableAlpha(
            MathGraph graph,
            BlendTree root,
            string frameTime,
            string key,
            float configuredSeconds,
            string tuning,
            float minimumSeconds,
            float maximumSeconds)
        {
            configuredSeconds = Mathf.Clamp(configuredSeconds, minimumSeconds, maximumSeconds);
            // A creator should be able to see the complete supported response
            // envelope. The center remains the authored profile exactly; the
            // endpoints now use the full safe timing range instead of the old
            // subtle 0.5x/2x neighborhood.
            var slowSeconds = maximumSeconds;
            var fastSeconds = minimumSeconds;
            var slow = graph.Param(key + "/Slow", 0.25f);
            var configured = graph.Param(key + "/Configured", 0.5f);
            var fast = graph.Param(key + "/Fast", 0.75f);
            graph.AddOperation(root, graph.AlphaFromDeltaTime(frameTime, slow, slowSeconds));
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, configured, configuredSeconds));
            graph.AddOperation(root, graph.AlphaFromDeltaTime(frameTime, fast, fastSeconds));
            return BlendAroundConfigured(
                graph, root, key + "/Tuned", slow, configured, fast, tuning, false);
        }

        private static string BuildTunableVoiceEvidence(
            MathGraph graph,
            BlendTree root,
            VisemeReconstructionProfile profile,
            string tuning)
        {
            var voice = graph.Param("Voice", 0f, false);
            var baseNoise = Mathf.Clamp01(profile.voiceNoiseFloor);
            var baseFull = Mathf.Clamp(profile.voiceFullScale, baseNoise + 0.001f, 1f);
            var lessSensitive = graph.Param("Voice/Evidence/LessSensitive", 0f);
            var configured = graph.Param("Voice/Evidence/Configured", 0f);
            var moreSensitive = graph.Param("Voice/Evidence/MoreSensitive", 0f);
            var lessNoise = Mathf.Clamp(
                Mathf.Max(baseNoise * 3f, baseNoise + 0.08f),
                0f, 0.85f);
            var lessFull = Mathf.Clamp(
                Mathf.Max(baseFull * 3f, lessNoise + 0.15f),
                lessNoise + 0.001f, 1f);
            var moreNoise = 0f;
            var moreFull = Mathf.Clamp(
                baseFull * 0.2f, 0.01f, 0.25f);
            graph.AddOperation(root, graph.Map(voice, lessSensitive, new[]
            {
                Point(0f, 0f), Point(lessNoise, 0f), Point(lessFull, 1f), Point(1f, 1f)
            }));
            graph.AddOperation(root, graph.Map(voice, configured, new[]
            {
                Point(0f, 0f), Point(baseNoise, 0f), Point(baseFull, 1f), Point(1f, 1f)
            }));
            graph.AddOperation(root, graph.Map(voice, moreSensitive, new[]
            {
                Point(0f, 0f), Point(moreNoise, 0f), Point(moreFull, 1f), Point(1f, 1f)
            }));
            return BlendAroundConfigured(
                graph, root, "Voice/Evidence/Tuned", lessSensitive, configured,
                moreSensitive, tuning, false);
        }

        private static string BlendAroundConfigured(
            MathGraph graph,
            BlendTree root,
            string key,
            string low,
            string configured,
            string high,
            string tuning,
            bool signed)
        {
            var output = graph.Param(key, 0f);
            graph.AddOperation(root, graph.BlendThreeParameters(
                low, configured, high, output, tuning, signed,
                key + " three-point tuning"));
            return output;
        }

        private static void ApplyTongueAxisStrengths(
            MathGraph graph,
            BlendTree root,
            IDictionary<AdvancedVisemeArticulator, string> fast,
            IDictionary<AdvancedVisemeArticulator, string> slow,
            IReadOnlyDictionary<AdvancedVisemeTuningControl, string> tuning)
        {
            var controls = new Dictionary<AdvancedVisemeArticulator, AdvancedVisemeTuningControl>
            {
                { AdvancedVisemeArticulator.TongueOut, AdvancedVisemeTuningControl.TongueOut },
                { AdvancedVisemeArticulator.TongueY, AdvancedVisemeTuningControl.TongueVertical },
                { AdvancedVisemeArticulator.TongueX, AdvancedVisemeTuningControl.TongueLateral },
                { AdvancedVisemeArticulator.TongueRoll, AdvancedVisemeTuningControl.TongueRoll },
                { AdvancedVisemeArticulator.TongueArchY, AdvancedVisemeTuningControl.TongueArch },
                { AdvancedVisemeArticulator.TongueShape, AdvancedVisemeTuningControl.TongueShape },
                { AdvancedVisemeArticulator.TongueTwistRight, AdvancedVisemeTuningControl.TongueTwist },
                { AdvancedVisemeArticulator.TongueTwistLeft, AdvancedVisemeTuningControl.TongueTwist }
            };
            foreach (var pair in controls)
            {
                if (!fast.TryGetValue(pair.Key, out var fastSource) ||
                    !slow.TryGetValue(pair.Key, out var slowSource)) continue;
                var signed = IsSigned(pair.Key);
                var strength = tuning[pair.Value];
                var tunedFast = graph.Param($"Articulation/{pair.Key}/TunedSpeechFast", 0f);
                var tunedSlow = graph.Param($"Articulation/{pair.Key}/TunedSpeechSlow", 0f);
                graph.AddOperation(root, graph.Multiply(
                    strength, fastSource, tunedFast, signed));
                graph.AddOperation(root, graph.Multiply(
                    strength, slowSource, tunedSlow, signed));
                fast[pair.Key] = tunedFast;
                slow[pair.Key] = tunedSlow;
            }
        }

        private static string BuildAdaptiveTrackingPose(
            MathGraph graph,
            BlendTree root,
            AdvancedVisemeArticulator articulator,
            string fast,
            string slow,
            string alphaMotion,
            string localFactor,
            out string motion)
        {
            var signed = IsSigned(articulator);
            var difference = graph.Param($"Tracking/{articulator}/MotionDifference", 0f);
            var magnitude = graph.Param($"Tracking/{articulator}/MotionMagnitude", 0f);
            graph.AddOperation(root, graph.Linear(difference, new[]
            {
                Term.Signed(fast, 1f), Term.Signed(slow, -1f)
            }));
            graph.AddOperation(root, graph.Abs(difference, magnitude));

            if (UseSingleInvariantTrackingObserverForTests)
            {
                // IsLocal is invariant for an avatar instance, so the selected
                // response curve can feed one recurrent observer. For either
                // fixed endpoint this is the same recurrence as the corresponding
                // local/remote branch below; the final copy preserves the extra
                // Animator-parameter evaluation epoch of the former interpolation.
                var selectedRaw = graph.Param(
                    $"Tracking/{articulator}/MotionRaw", 0f);
                graph.AddOperation(root, graph.SelectMotion(
                    localFactor,
                    graph.Map(magnitude, selectedRaw, SmoothStepPoints(
                        RemoteTrackingMotionDeadband,
                        RemoteTrackingMotionFullScale,
                        0f,
                        1f)),
                    graph.Map(magnitude, selectedRaw, SmoothStepPoints(
                        LocalTrackingMotionDeadband,
                        LocalTrackingMotionFullScale,
                        0f,
                        1f)),
                    $"Tracking {articulator} invariant motion response"));
                var selectedMotion = graph.Param(
                    $"Tracking/{articulator}/SelectedMotion", 0f);
                graph.AddOperation(root, graph.Smooth(
                    selectedRaw, selectedMotion, alphaMotion, false));
                motion = graph.Param($"Tracking/{articulator}/Motion", 0f);
                graph.AddOperation(root, graph.Copy(
                    selectedMotion, motion, false));
            }
            else
            {
                var localRaw = graph.Param(
                    $"Tracking/{articulator}/LocalMotionRaw", 0f);
                var remoteRaw = graph.Param(
                    $"Tracking/{articulator}/RemoteMotionRaw", 0f);
                string localMotion;
                string remoteMotion;
                if (DisableInvariantTrackingBranchGatingForTests)
                {
                    // Preserve the former graph verbatim for exact profiler A/B
                    // controllers: both response maps and smoothers stay live.
                    graph.AddOperation(root, graph.Map(
                        magnitude, localRaw, SmoothStepPoints(
                            LocalTrackingMotionDeadband,
                            LocalTrackingMotionFullScale,
                            0f,
                            1f)));
                    graph.AddOperation(root, graph.Map(
                        magnitude, remoteRaw, SmoothStepPoints(
                            RemoteTrackingMotionDeadband,
                            RemoteTrackingMotionFullScale,
                            0f,
                            1f)));
                    localMotion = graph.Param(
                        $"Tracking/{articulator}/LocalMotion", 0f);
                    remoteMotion = graph.Param(
                        $"Tracking/{articulator}/RemoteMotion", 0f);
                    graph.AddOperation(root, graph.Smooth(
                        localRaw, localMotion, alphaMotion, false));
                    graph.AddOperation(root, graph.Smooth(
                        remoteRaw, remoteMotion, alphaMotion, false));
                }
                else
                {
                    localMotion = graph.Param(
                        $"Tracking/{articulator}/LocalMotion", 0f);
                    remoteMotion = graph.Param(
                        $"Tracking/{articulator}/RemoteMotion", 0f);
                    var localObserver = graph.Direct(
                        $"Tracking {articulator} local motion observer");
                    localObserver.children = new[]
                    {
                        new ChildMotion
                        {
                            motion = graph.Map(magnitude, localRaw, SmoothStepPoints(
                                LocalTrackingMotionDeadband,
                                LocalTrackingMotionFullScale,
                                0f,
                                1f)),
                            directBlendParameter = MathGraph.AlwaysOneParameter,
                            timeScale = 1f
                        },
                        new ChildMotion
                        {
                            // Smooth the speed selector itself so a value sitting on a
                            // threshold cannot alternate between observer paths.
                            motion = graph.Smooth(
                                localRaw, localMotion, alphaMotion, false),
                            directBlendParameter = MathGraph.AlwaysOneParameter,
                            timeScale = 1f
                        }
                    };
                    var remoteObserver = graph.Direct(
                        $"Tracking {articulator} remote motion observer");
                    remoteObserver.children = new[]
                    {
                        new ChildMotion
                        {
                            motion = graph.Map(magnitude, remoteRaw, SmoothStepPoints(
                                RemoteTrackingMotionDeadband,
                                RemoteTrackingMotionFullScale,
                                0f,
                                1f)),
                            directBlendParameter = MathGraph.AlwaysOneParameter,
                            timeScale = 1f
                        },
                        new ChildMotion
                        {
                            motion = graph.Smooth(
                                remoteRaw, remoteMotion, alphaMotion, false),
                            directBlendParameter = MathGraph.AlwaysOneParameter,
                            timeScale = 1f
                        }
                    };

                    // IsLocal is invariant for an avatar instance. Select the matching
                    // observer before evaluating it instead of running both response
                    // curves forever. Keep the final interpolation below as its own AAP
                    // epoch so the visible pose and response timing remain unchanged.
                    graph.AddOperation(root, graph.SelectMotion(
                        localFactor,
                        remoteObserver,
                        localObserver,
                        $"Tracking {articulator} local-remote motion observer"));
                }
                motion = graph.Param($"Tracking/{articulator}/Motion", 0f);
                graph.AddOperation(root, graph.Interpolate(
                    remoteMotion, localMotion, motion, localFactor, false));
            }

            var pose = graph.Param($"Tracking/{articulator}/Pose", 0f);
            graph.AddOperation(root, graph.Interpolate(slow, fast, pose, motion, signed));
            return pose;
        }

        private static string BuildTrackingAuthority(
            MathGraph graph,
            BlendTree root,
            AdvancedVisemeArticulator articulator,
            string speech,
            string tracking,
            string localFactor,
            float remoteReliability,
            string remoteTrust)
        {
            remoteReliability = Mathf.Clamp01(remoteReliability);
            var difference = graph.Param($"Tracking/{articulator}/PriorDifference", 0f);
            var magnitude = graph.Param($"Tracking/{articulator}/PriorMismatch", 0f);
            if (DisableInvariantTrackingBranchGatingForTests)
            {
                graph.AddOperation(root, graph.Linear(difference, new[]
                {
                    Term.Signed(tracking, 1f), Term.Signed(speech, -1f)
                }));
                graph.AddOperation(root, graph.Abs(difference, magnitude));
            }

            // A valid local measurement is the ground truth for its own visible
            // coordinate, even when it happens to agree with the speech prior.
            // Remote measurements keep a conservative mismatch-conditioned
            // reliability to absorb quantization and packet jitter.
            var remoteAuthority = graph.Param(
                $"Tracking/{articulator}/RemoteAuthority", remoteReliability);
            if (DisableInvariantTrackingBranchGatingForTests)
            {
                graph.AddOperation(root, graph.Map(
                    magnitude,
                    remoteAuthority,
                    SmoothStepPoints(
                        TrackingAuthorityAgreementDeadband * 1.5f,
                        TrackingAuthorityDisagreement * 1.5f,
                        remoteReliability,
                        1f)));
            }
            else
            {
                var remoteEvidence = graph.Direct(
                    $"Tracking {articulator} remote authority evidence");
                remoteEvidence.children = new[]
                {
                    new ChildMotion
                    {
                        motion = graph.Linear(difference, new[]
                        {
                            Term.Signed(tracking, 1f),
                            Term.Signed(speech, -1f)
                        }),
                        directBlendParameter = MathGraph.AlwaysOneParameter,
                        timeScale = 1f
                    },
                    new ChildMotion
                    {
                        motion = graph.Abs(difference, magnitude),
                        directBlendParameter = MathGraph.AlwaysOneParameter,
                        timeScale = 1f
                    },
                    new ChildMotion
                    {
                        motion = graph.Map(
                            magnitude,
                            remoteAuthority,
                            SmoothStepPoints(
                                TrackingAuthorityAgreementDeadband * 1.5f,
                                TrackingAuthorityDisagreement * 1.5f,
                                remoteReliability,
                                1f)),
                        directBlendParameter = MathGraph.AlwaysOneParameter,
                        timeScale = 1f
                    }
                };

                // The mismatch prior is only consumed by remote avatars. Gate its
                // three-stage evidence path with the same invariant IsLocal select;
                // MathGraph batches it with the authority select below without
                // changing the final authority write epoch.
                graph.AddOperation(root, graph.SelectMotion(
                    localFactor,
                    remoteEvidence,
                    graph.EmptyClip(),
                    $"Tracking {articulator} remote authority evidence gate"));
            }
            var authority = graph.Param($"Tracking/{articulator}/Reliability", remoteReliability);
            graph.AddOperation(root, graph.SelectMotion(
                localFactor,
                graph.Multiply(remoteAuthority, remoteTrust, authority, false),
                graph.Setter(authority, 1f),
                $"Tracking {articulator} local authority bypass"));
            return authority;
        }

        private static string[] BuildVisibleSpeechWeights(
            MathGraph graph,
            BlendTree root,
            Request request,
            IReadOnlyList<string> speechWeights,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> trackingGains)
        {
            // A calibrated external basis is reconstructed as U(Cp) + Rp in the
            // authoritative output layer. Its common simplex must never be
            // support-suppressed; only the U coefficients are fused with tracking.
            if (HasExternalPoseCalibration(request)) return speechWeights.ToArray();

            var result = new string[speechWeights.Count];
            var suppressionMaximums = new Dictionary<string, string>(StringComparer.Ordinal);
            var unsuppressedInputs = new List<string>();
            var unsuppressedOutputs = new List<string>();
            var support = new Dictionary<AdvancedVisemeArticulator, float[]>();
            foreach (var articulator in VisibleSpeechArticulators)
            {
                if (!trackingGains.ContainsKey(articulator)) continue;

                // A reused template already owns its measured pose in a lower
                // controller layer. Fresh inputs count only when this generated
                // controller can actually drive the corresponding mesh basis.
                if (!request.reuseExistingTracking &&
                    !HasDriveableOutputPose(request, articulator))
                    continue;
                support[articulator] = GetAuthoredSpeechCoefficients(request, articulator);
            }

            var relevantGainsByViseme = Enumerable.Range(0, speechWeights.Count)
                .Select(viseme => support
                    .Where(pair => Mathf.Abs(pair.Value[viseme]) >= 1e-6f)
                    .Select(pair => trackingGains[pair.Key])
                    .Distinct()
                    .OrderBy(parameter => parameter, StringComparer.Ordinal)
                    .ToArray())
                .ToArray();
            // With no suppressible row, the complete vector can stay at its
            // current depth. The copy stage is needed only for a mixed vector.
            if (relevantGainsByViseme.All(gains => gains.Length == 0))
                return speechWeights.ToArray();

            for (var viseme = 0; viseme < speechWeights.Count; viseme++)
            {
                var relevantGains = relevantGainsByViseme[viseme];

                if (relevantGains.Length == 0)
                {
                    // Keep every visible viseme at the same AAP depth. Aliasing
                    // unsupported rows directly to speechWeights makes a mixed-
                    // frame pose whenever another row is tracking-suppressed.
                    var unsuppressedWeight = graph.Param(
                        $"Viseme/{viseme}/VisibleSpeechWeight", 0f);
                    unsuppressedInputs.Add(speechWeights[viseme]);
                    unsuppressedOutputs.Add(unsuppressedWeight);
                    result[viseme] = unsuppressedWeight;
                    continue;
                }

                string suppression;
                if (relevantGains.Length == 1)
                {
                    suppression = relevantGains[0];
                }
                else
                {
                    var key = string.Join("\u001f", relevantGains);
                    if (!suppressionMaximums.TryGetValue(key, out suppression))
                    {
                        suppression = MaxParameters(
                            graph, root,
                            $"Viseme/{viseme}/VisibleSuppressionMaximum",
                            relevantGains);
                        suppressionMaximums[key] = suppression;
                    }
                }

                var visibleWeight = graph.Param(
                    $"Viseme/{viseme}/VisibleSpeechWeight", 0f);
                graph.AddOperation(root, graph.ScaleByInverseUnitWeight(
                    speechWeights[viseme], suppression, visibleWeight, 1f));
                result[viseme] = visibleWeight;
            }
            if (unsuppressedInputs.Count > 0)
                graph.AddOperation(root, graph.CopyVector(
                    unsuppressedInputs, unsuppressedOutputs,
                    "Unsuppressed visible viseme vector"));
            return result;
        }

        private static (float input, float output)[] SmoothStepPoints(
            float start,
            float end,
            float low,
            float high)
        {
            end = Mathf.Max(start + 0.0001f, end);
            var span = end - start;
            return new[]
            {
                Point(0f, low),
                Point(start, low),
                Point(start + span * 0.25f, Mathf.Lerp(low, high, 0.15625f)),
                Point(start + span * 0.5f, Mathf.Lerp(low, high, 0.5f)),
                Point(start + span * 0.75f, Mathf.Lerp(low, high, 0.84375f)),
                Point(end, high),
                Point(2f, high)
            };
        }

        private static ConstraintConfidenceBases BuildConstraintConfidenceBases(
            MathGraph graph,
            BlendTree root,
            string speechPresence,
            string trackingBlend,
            string localFactor,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> trackingGains,
            IReadOnlyDictionary<AdvancedVisemeTuningControl, string> tuning)
        {
            var active = graph.Param("Constraint/Shared/ActiveBlend", 0f);
            graph.AddOperation(root, graph.Multiply(
                speechPresence, trackingBlend, active, false));
            return new ConstraintConfidenceBases
            {
                bilabial = BuildConstraintConfidenceBase(
                    graph, root, "Constraint/Shared/PP", active, localFactor,
                    trackingGains, AdvancedVisemeArticulator.LipClose,
                    tuning[AdvancedVisemeTuningControl.ConstraintAmount],
                    tuning[AdvancedVisemeTuningControl.BilabialAssist]),
                labiodental = BuildConstraintConfidenceBase(
                    graph, root, "Constraint/Shared/FF", active, localFactor,
                    trackingGains, AdvancedVisemeArticulator.LipBite,
                    tuning[AdvancedVisemeTuningControl.ConstraintAmount],
                    tuning[AdvancedVisemeTuningControl.LabiodentalAssist]),
                sibilant = BuildConstraintConfidenceBase(
                    graph, root, "Constraint/Shared/Sibilant", active, localFactor,
                    trackingGains, AdvancedVisemeArticulator.JawOpen,
                    tuning[AdvancedVisemeTuningControl.ConstraintAmount],
                    tuning[AdvancedVisemeTuningControl.SibilantAssist])
            };
        }

        private static string BuildConstraintConfidenceBase(
            MathGraph graph,
            BlendTree root,
            string key,
            string active,
            string localFactor,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> trackingGains,
            AdvancedVisemeArticulator target,
            string globalStrength,
            string channelStrength)
        {
            var strength = graph.Param(key + "/Strength", 0f);
            graph.AddOperation(root, graph.Multiply(
                globalStrength, channelStrength, strength, false));
            var activeStrength = graph.Param(key + "/ActiveStrength", 0f);
            graph.AddOperation(root, graph.Multiply(
                active, strength, activeStrength, false));
            if (!trackingGains.TryGetValue(target, out var gain))
                return activeStrength;

            var localAuthority = graph.Param(key + "/LocalAuthority", 0f);
            graph.AddOperation(root, graph.Multiply(
                localFactor, gain, localAuthority, false));
            var output = graph.Param(key + "/ConfidenceBase", 0f);
            graph.AddOperation(root, graph.ScaleByInverseUnitWeight(
                activeStrength, localAuthority, output, 1f));
            return output;
        }

        private static void ApplyConstraints(
            MathGraph graph,
            BlendTree root,
            VisemeReconstructionProfile profile,
            string[] visemes,
            ConstraintConfidenceBases bases,
            IDictionary<AdvancedVisemeArticulator, string> articulation,
            string stage)
        {
            var constraintRoot = "Constraint/" + stage;
            if (articulation.TryGetValue(AdvancedVisemeArticulator.LipClose, out var lipClose))
            {
                var confidence = graph.Param(constraintRoot + "/PPConfidence", 0f);
                graph.AddOperation(root, graph.Multiply(
                    bases.bilabial, visemes[1], confidence, false));
                articulation[AdvancedVisemeArticulator.LipClose] = SmoothFloorProjection(
                    graph, root, constraintRoot + "/PPClosure", lipClose,
                    profile.bilabialClosure, confidence);
            }
            if (articulation.TryGetValue(AdvancedVisemeArticulator.LipBite, out var lipBite))
            {
                var confidence = graph.Param(constraintRoot + "/FFConfidence", 0f);
                graph.AddOperation(root, graph.Multiply(
                    bases.labiodental, visemes[2], confidence, false));
                articulation[AdvancedVisemeArticulator.LipBite] = SmoothFloorProjection(
                    graph, root, constraintRoot + "/FFBite", lipBite,
                    profile.labiodentalBite, confidence);
            }
            if (articulation.TryGetValue(AdvancedVisemeArticulator.JawOpen, out var jaw))
            {
                var sibilant = graph.Param(constraintRoot + "/Sibilant", 0f);
                graph.AddOperation(root, graph.Linear(sibilant, new[]
                {
                    Term.Positive(visemes[6], 1f), Term.Positive(visemes[7], 1f)
                }));
                var confidence = graph.Param(constraintRoot + "/SibilantConfidence", 0f);
                graph.AddOperation(root, graph.Multiply(
                    bases.sibilant, sibilant, confidence, false));
                articulation[AdvancedVisemeArticulator.JawOpen] = SmoothCeilingProjection(
                    graph, root, constraintRoot + "/SibilantJaw", jaw,
                    profile.sibilantJawMaximum, confidence);
            }
        }

        private static string SmoothFloorProjection(
            MathGraph graph,
            BlendTree root,
            string key,
            string value,
            float floor,
            string confidence)
        {
            floor = Mathf.Clamp01(floor);
            var target = graph.Param(key + "/Target", floor);
            graph.AddOperation(root, graph.Map(
                value, target, MonotoneProjectionPoints(floor, ConstraintProjectionWidth, true)));
            var output = graph.Param(key + "/Projected", 0f);
            graph.AddOperation(root, graph.Interpolate(value, target, output, confidence, false));
            return output;
        }

        private static string SmoothCeilingProjection(
            MathGraph graph,
            BlendTree root,
            string key,
            string value,
            float ceiling,
            string confidence)
        {
            ceiling = Mathf.Clamp01(ceiling);
            var target = graph.Param(key + "/Target", ceiling);
            graph.AddOperation(root, graph.Map(
                value, target, MonotoneProjectionPoints(ceiling, ConstraintProjectionWidth, false)));
            var output = graph.Param(key + "/Projected", 0f);
            graph.AddOperation(root, graph.Interpolate(value, target, output, confidence, false));
            return output;
        }

        private static (float input, float output)[] MonotoneProjectionPoints(
            float boundary,
            float width,
            bool floor)
        {
            width = Mathf.Max(0.0001f, width);
            var samples = new List<float> { -1f, 0f, 1f, 2f };
            for (var i = 0; i <= 8; i++)
            {
                samples.Add(boundary - width + 2f * width * i / 8f);
            }
            samples.Sort();

            var points = new List<(float input, float output)>();
            foreach (var sample in samples)
            {
                if (points.Count > 0 && Mathf.Abs(points[points.Count - 1].input - sample) < 1e-6f)
                    continue;
                var output = floor
                    ? AdvancedVisemeMath.SmoothFloorProjection(sample, boundary, 1f, width)
                    : AdvancedVisemeMath.SmoothCeilingProjection(sample, boundary, 1f, width);
                points.Add(Point(sample, output));
            }
            return points.ToArray();
        }

        private static void ApplyMouthEnvelope(
            MathGraph graph,
            BlendTree root,
            string[] visemes,
            IDictionary<AdvancedVisemeArticulator, string> articulation)
        {
            if (articulation.TryGetValue(AdvancedVisemeArticulator.LipClose, out var lipClose) &&
                articulation.TryGetValue(AdvancedVisemeArticulator.MouthOpen, out var mouthOpen))
            {
                var maximum = graph.Param("Envelope/MouthOpenMaximum", 1f);
                graph.AddOperation(root, graph.Linear(maximum, new[]
                {
                    Term.Constant(1f), Term.Positive(lipClose, -1f)
                }));
                var constrained = graph.Param("Envelope/MouthOpen", 0f);
                graph.AddOperation(root, graph.Min(mouthOpen, maximum, constrained));
                articulation[AdvancedVisemeArticulator.MouthOpen] = constrained;
            }

            if (articulation.TryGetValue(AdvancedVisemeArticulator.LipSuck, out var lipSuck) &&
                articulation.TryGetValue(AdvancedVisemeArticulator.LipPucker, out var lipPucker))
            {
                var maximum = graph.Param("Envelope/LipPuckerMaximum", 1f);
                graph.AddOperation(root, graph.Linear(maximum, new[]
                {
                    Term.Constant(1f), Term.Positive(lipSuck, -1f)
                }));
                var constrained = graph.Param("Envelope/LipPucker", 0f);
                graph.AddOperation(root, graph.Min(lipPucker, maximum, constrained));
                articulation[AdvancedVisemeArticulator.LipPucker] = constrained;
            }

            if (articulation.TryGetValue(AdvancedVisemeArticulator.TongueOut, out var tongueOut))
            {
                var apertureRaw = graph.Param("Envelope/TongueApertureRaw", 0f);
                var aperture = graph.Param("Envelope/TongueAperture", 0f);
                var apertureTerms = new List<Term> { Term.Positive(visemes[3], 0.6f) };
                if (articulation.TryGetValue(AdvancedVisemeArticulator.JawOpen, out var jaw))
                    apertureTerms.Add(Term.Positive(jaw, 1f));
                if (articulation.TryGetValue(AdvancedVisemeArticulator.MouthOpen, out var mouth))
                    apertureTerms.Add(Term.Positive(mouth, 1f));
                graph.AddOperation(root, graph.Linear(apertureRaw, apertureTerms));
                graph.AddOperation(root, graph.Map(apertureRaw, aperture, new[]
                {
                    Point(0f, 0f), Point(1f, 1f), Point(2f, 1f)
                }));
                var maximum = graph.Param("Envelope/TongueOutMaximum", 0.08f);
                graph.AddOperation(root, graph.Linear(maximum, new[]
                {
                    Term.Constant(0.08f), Term.Positive(aperture, 0.92f)
                }));
                var constrained = graph.Param("Envelope/TongueOut", 0f);
                graph.AddOperation(root, graph.Min(tongueOut, maximum, constrained));
                articulation[AdvancedVisemeArticulator.TongueOut] = constrained;
            }
        }

        private static void BuildSpeechEvidence(
            MathGraph graph,
            BlendTree root,
            string prefix,
            string[] visemes,
            string energy,
            Result result)
        {
            PublishEvidence(graph, root,
                AdvancedVisemeParameterContract.Speech(prefix, "Bilabial"), result,
                new[] { Term.Positive(visemes[1], 1f) });
            PublishEvidence(graph, root,
                AdvancedVisemeParameterContract.Speech(prefix, "Labiodental"), result,
                new[] { Term.Positive(visemes[2], 1f) });
            PublishEvidence(graph, root,
                AdvancedVisemeParameterContract.Speech(prefix, "Sibilant"), result,
                new[] { Term.Positive(visemes[6], 1f), Term.Positive(visemes[7], 1f) });
            PublishEvidence(graph, root,
                AdvancedVisemeParameterContract.Speech(prefix, "Coronal"), result,
                new[]
                {
                    Term.Positive(visemes[3], 1f), Term.Positive(visemes[4], 1f),
                    Term.Positive(visemes[6], 1f), Term.Positive(visemes[7], 1f),
                    Term.Positive(visemes[8], 1f)
                });
            PublishEvidence(graph, root,
                AdvancedVisemeParameterContract.Speech(prefix, "Dorsal"), result,
                new[] { Term.Positive(visemes[5], 1f) });
            PublishEvidence(graph, root,
                AdvancedVisemeParameterContract.Speech(prefix, "Rhotic"), result,
                new[] { Term.Positive(visemes[9], 1f) });

            var lipClose = result.articulationParameters.TryGetValue(
                AdvancedVisemeArticulator.LipClose, out var closeParameter)
                ? closeParameter
                : graph.Param("Evidence/LipCloseFallback", 0f);
            var tongueContact = BuildTongueContact(graph, root, visemes, result);
            var tongueContactOutput = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "TongueContact"),
                0f, false);
            graph.AddOperation(root, graph.Copy(tongueContact, tongueContactOutput, false));
            result.globalParameters.Add(tongueContactOutput);

            var mSupport = graph.Param("Evidence/MSupport", 0.6f);
            graph.AddOperation(root, graph.Linear(mSupport, new[]
            {
                Term.Constant(0.6f), Term.Positive(lipClose, 0.4f)
            }));
            var mClosure = graph.Param("Evidence/MClosure", 0f);
            graph.AddOperation(root, graph.Multiply(visemes[1], mSupport, mClosure, false));
            var mOutput = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "M"), 0f, false);
            graph.AddOperation(root, graph.Multiply(energy, mClosure, mOutput, false));
            result.globalParameters.Add(mOutput);

            var nSupport = graph.Param("Evidence/NSupport", 0.6f);
            graph.AddOperation(root, graph.Linear(nSupport, new[]
            {
                Term.Constant(0.6f), Term.Positive(tongueContact, 0.4f)
            }));
            var nContact = graph.Param("Evidence/NContact", 0f);
            graph.AddOperation(root, graph.Multiply(visemes[8], nSupport, nContact, false));
            var nOutput = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "N"), 0f, false);
            // nn is the merged n/l class. Its visible lips are observational:
            // a speaker may produce it with closed lips, so closure cannot veto
            // otherwise valid tongue-contact evidence.
            graph.AddOperation(root, graph.Multiply(energy, nContact, nOutput, false));
            result.globalParameters.Add(nOutput);
        }

        private static string BuildTongueContact(
            MathGraph graph,
            BlendTree root,
            string[] visemes,
            Result result)
        {
            var candidates = new List<string>();
            foreach (var articulator in new[]
                     {
                         AdvancedVisemeArticulator.TongueY,
                         AdvancedVisemeArticulator.TongueArchY
                     })
            {
                if (!result.articulationParameters.TryGetValue(articulator, out var parameter)) continue;
                var positive = graph.Param("Evidence/" + articulator + "Positive", 0f);
                graph.AddOperation(root, graph.Map(parameter, positive, new[]
                {
                    Point(-1f, 0f), Point(0f, 0f), Point(1f, 1f)
                }));
                candidates.Add(positive);
            }

            var baseContact = graph.Param("Evidence/TongueContactBase", 0f);
            if (candidates.Count == 0)
            {
                graph.AddOperation(root, graph.Copy(visemes[8], baseContact, false));
            }
            else if (candidates.Count == 1)
            {
                graph.AddOperation(root, graph.Copy(candidates[0], baseContact, false));
            }
            else
            {
                graph.AddOperation(root, graph.Max(candidates[0], candidates[1], baseContact));
            }

            if (!result.articulationParameters.TryGetValue(
                    AdvancedVisemeArticulator.TongueOut, out var tongueOut)) return baseContact;
            var notOut = graph.Param("Evidence/TongueNotOut", 1f);
            graph.AddOperation(root, graph.Linear(notOut, new[]
            {
                Term.Constant(1f), Term.Positive(tongueOut, -1f)
            }));
            var contact = graph.Param("Evidence/TongueContact", 0f);
            graph.AddOperation(root, graph.Multiply(notOut, baseContact, contact, false));
            return contact;
        }

        private static void PublishEvidence(
            MathGraph graph,
            BlendTree root,
            string output,
            Result result,
            IEnumerable<Term> terms)
        {
            var parameter = graph.Param(output, 0f, false);
            var evidenceTerms = terms.ToArray();
            if (evidenceTerms.Length == 1 &&
                !evidenceTerms[0].constant &&
                !evidenceTerms[0].signed &&
                Mathf.Approximately(evidenceTerms[0].multiplier, 1f))
                graph.AddOperation(root,
                    graph.Copy(evidenceTerms[0].parameter, parameter, false));
            else
                graph.AddOperation(root, graph.Linear(parameter, evidenceTerms));
            result.globalParameters.Add(parameter);
        }

        private static bool RequiresNativeTongueCapability(AdvancedVisemeArticulator articulator)
        {
            return articulator == AdvancedVisemeArticulator.TongueOut ||
                   articulator == AdvancedVisemeArticulator.TongueY ||
                   articulator == AdvancedVisemeArticulator.TongueX ||
                   articulator == AdvancedVisemeArticulator.TongueRoll ||
                   articulator == AdvancedVisemeArticulator.TongueArchY ||
                   articulator == AdvancedVisemeArticulator.TongueShape ||
                   articulator == AdvancedVisemeArticulator.TongueTwistRight ||
                   articulator == AdvancedVisemeArticulator.TongueTwistLeft;
        }

        private static Dictionary<AdvancedVisemeArticulator, string> BuildNativeTongueCapabilities(
            MathGraph graph,
            BlendTree root,
            Request request,
            string frameTime,
            string trackingActivity,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> rawTracking)
        {
            var capabilities = new Dictionary<AdvancedVisemeArticulator, string>();
            // An explicitly generated FullTongue18 input set is a user declaration
            // of capability. Auto/reused templates merely declaring tongue
            // parameters are not evidence that the current hardware populates them.
            var explicitCapability = !request.reuseExistingTracking &&
                                     request.component.trackingInputs ==
                                     AdvancedVisemeTrackingInputs.FullTongue18;
            foreach (var pair in rawTracking)
            {
                var articulator = pair.Key;
                if (!RequiresNativeTongueCapability(articulator)) continue;
                if (explicitCapability)
                {
                    capabilities[articulator] = MathGraph.AlwaysOneParameter;
                    continue;
                }

                var parameter = pair.Value;
                var magnitude = graph.Param($"Tracking/{articulator}/NativeEvidenceMagnitude", 0f);
                graph.AddOperation(root, graph.Abs(parameter, magnitude));
                var observed = graph.Param($"Tracking/{articulator}/NativeCapabilityObserved", 0f);
                graph.AddOperation(root, graph.Map(magnitude, observed, new[]
                {
                    Point(0f, 0f), Point(NativeTongueCapabilityNoiseFloor, 0f),
                    Point(NativeTongueCapabilityThreshold, 1f), Point(1f, 1f)
                }));
                var activeObserved = graph.Param(
                    $"Tracking/{articulator}/NativeCapabilityActiveObserved", 0f);
                graph.AddOperation(root, graph.Multiply(
                    trackingActivity, observed, activeObserved, false));
                // Capability is channel-specific: TongueOut hardware must not
                // erase a learned TongueY (or vice versa). Require sustained
                // evidence before latching so one noisy OSC packet is harmless.
                var alphaEvidence = graph.Param($"Tracking/{articulator}/NativeEvidenceAlpha", 0.1f);
                graph.AddOperation(root, graph.AlphaFromDeltaTime(
                    frameTime, alphaEvidence, 0.12f));
                var accumulated = graph.Param($"Tracking/{articulator}/NativeEvidenceAccumulated", 0f);
                graph.AddOperation(root, graph.Smooth(
                    activeObserved, accumulated, alphaEvidence, false));
                var confirmed = graph.Param($"Tracking/{articulator}/NativeCapabilityConfirmed", 0f);
                graph.AddOperation(root, graph.Map(accumulated, confirmed, new[]
                {
                    Point(0f, 0f), Point(0.78f, 0f), Point(0.8f, 1f), Point(1f, 1f)
                }));
                var capability = graph.Param($"Tracking/{articulator}/NativeCapability", 0f);
                var latched = graph.Param($"Tracking/{articulator}/NativeCapabilityLatched", 0f);
                graph.AddOperation(root, graph.Max(capability, confirmed, latched));
                graph.AddOperation(root, graph.Copy(latched, capability, false));
                capabilities[articulator] = capability;
            }
            return capabilities;
        }

        internal static float StepNativeTongueCapability(
            float previousCapability,
            float tongueOut,
            float tongueY)
        {
            previousCapability = Mathf.Clamp01(previousCapability);
            var magnitude = Mathf.Max(Mathf.Abs(tongueOut), Mathf.Abs(tongueY));
            var observed = Mathf.InverseLerp(
                NativeTongueCapabilityNoiseFloor,
                NativeTongueCapabilityThreshold,
                magnitude);
            return Mathf.Max(previousCapability, observed);
        }

        internal static bool UsesPhoneticTrackingScale(AdvancedVisemeFusionMode mode)
        {
            // Retaining a percentage of a full authored vowel pose is additive
            // overshoot, not complementary fusion. PhoneticAssist now differs only
            // by its sparse PP/FF/sibilant projections.
            return false;
        }

        internal static bool UsesVowelIdentityRetention(AdvancedVisemeArticulator articulator)
        {
            // A measured funnel or pucker is already the visible vowel identity.
            // Hidden tongue-body channels retain the speech prior instead.
            return false;
        }

        internal static bool CanBuildFaceConditionedTongueInference(Request request)
        {
            if (request == null || !request.trackingEnabled || request.profile == null)
                return false;

            var available = new HashSet<AdvancedVisemeArticulator>(
                TrackedArticulators(request.effectiveTrackingInputs));
            var required = new[]
            {
                AdvancedVisemeArticulator.JawOpen,
                AdvancedVisemeArticulator.LipClose,
                AdvancedVisemeArticulator.MouthOpen
            };
            foreach (var articulator in required)
            {
                if (!available.Contains(articulator)) return false;
                var binding = request.profile.FindBinding(articulator);
                if (!TryResolveTrackingParameter(
                        request, articulator, binding, out _)) return false;
            }
            return true;
        }

        private static FacePhonePosteriorGraph ApplyBetaTongueInference(
            MathGraph graph,
            BlendTree alwaysRoot,
            BlendTree root,
            string authority,
            Request request,
            Result result,
            BetaCoarticulationGraph betaGraph,
            string frameTime,
            string speechPresence,
            string speechGain,
            IDictionary<AdvancedVisemeArticulator, string> speechFast,
            IDictionary<AdvancedVisemeArticulator, string> speechSlow,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> speechCenters,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> calibratedTrackingRaw,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> trackingGains,
            IReadOnlyDictionary<AdvancedVisemeTuningControl, string> tuning)
        {
            var apertureRequired = new[]
            {
                AdvancedVisemeArticulator.JawOpen,
                AdvancedVisemeArticulator.LipClose,
                AdvancedVisemeArticulator.MouthOpen
            };
            if (apertureRequired.Any(articulator =>
                    !calibratedTrackingRaw.ContainsKey(articulator) ||
                    !trackingGains.ContainsKey(articulator) ||
                    !speechCenters.ContainsKey(articulator)))
                return null;
            if (!speechSlow.ContainsKey(AdvancedVisemeArticulator.TongueOut) ||
                !speechSlow.ContainsKey(AdvancedVisemeArticulator.TongueY))
                return null;

            var protrusionRequired = new[]
            {
                AdvancedVisemeArticulator.LipFunnel,
                AdvancedVisemeArticulator.LipPucker,
                AdvancedVisemeArticulator.LipSuck
            };
            var hasProtrusion = protrusionRequired.All(articulator =>
                calibratedTrackingRaw.ContainsKey(articulator) &&
                trackingGains.ContainsKey(articulator) &&
                speechCenters.ContainsKey(articulator));

            var quality = hasProtrusion &&
                          calibratedTrackingRaw.ContainsKey(AdvancedVisemeArticulator.JawZ) &&
                          trackingGains.ContainsKey(AdvancedVisemeArticulator.JawZ) &&
                          speechCenters.ContainsKey(AdvancedVisemeArticulator.JawZ);
            AdvancedVisemeVisibleTongueModelKind? tongueKind = hasProtrusion
                ? quality
                    ? AdvancedVisemeVisibleTongueModelKind.Quality
                    : AdvancedVisemeVisibleTongueModelKind.Balanced
                : (AdvancedVisemeVisibleTongueModelKind?)null;
            var phoneKind = quality
                ? AdvancedVisemeHiddenPhoneModelKind.Quality
                : hasProtrusion
                    ? AdvancedVisemeHiddenPhoneModelKind.Balanced
                    : AdvancedVisemeHiddenPhoneModelKind.Aperture;

            var current = new Dictionary<AdvancedVisemeVisibleFeatureChannel, string>();
            var center = new Dictionary<AdvancedVisemeVisibleFeatureChannel, string>();
            var featureGain = new Dictionary<AdvancedVisemeVisibleFeatureChannel, string>();

            current[AdvancedVisemeVisibleFeatureChannel.JawOpen] =
                calibratedTrackingRaw[AdvancedVisemeArticulator.JawOpen];
            center[AdvancedVisemeVisibleFeatureChannel.JawOpen] =
                speechCenters[AdvancedVisemeArticulator.JawOpen];
            featureGain[AdvancedVisemeVisibleFeatureChannel.JawOpen] =
                trackingGains[AdvancedVisemeArticulator.JawOpen];

            if (quality)
            {
                current[AdvancedVisemeVisibleFeatureChannel.JawAdvance] = BuildSignedUnitValue(
                    graph, alwaysRoot,
                    calibratedTrackingRaw[AdvancedVisemeArticulator.JawZ],
                    "TongueInference/Visible/JawAdvance/Tracked");
                center[AdvancedVisemeVisibleFeatureChannel.JawAdvance] = BuildSignedUnitValue(
                    graph, alwaysRoot, speechCenters[AdvancedVisemeArticulator.JawZ],
                    "TongueInference/Visible/JawAdvance/Speech");
                featureGain[AdvancedVisemeVisibleFeatureChannel.JawAdvance] =
                    trackingGains[AdvancedVisemeArticulator.JawZ];
            }

            current[AdvancedVisemeVisibleFeatureChannel.LipAperture] = BuildOpposedUnitValue(
                graph, alwaysRoot,
                calibratedTrackingRaw[AdvancedVisemeArticulator.MouthOpen],
                calibratedTrackingRaw[AdvancedVisemeArticulator.LipClose],
                "TongueInference/Visible/LipAperture/Tracked");
            center[AdvancedVisemeVisibleFeatureChannel.LipAperture] = BuildOpposedUnitValue(
                graph, alwaysRoot,
                speechCenters[AdvancedVisemeArticulator.MouthOpen],
                speechCenters[AdvancedVisemeArticulator.LipClose],
                "TongueInference/Visible/LipAperture/Speech");
            featureGain[AdvancedVisemeVisibleFeatureChannel.LipAperture] = MinParameters(
                graph, alwaysRoot, "TongueInference/Gain/LipAperture",
                trackingGains[AdvancedVisemeArticulator.MouthOpen],
                trackingGains[AdvancedVisemeArticulator.LipClose]);

            if (hasProtrusion)
            {
                current[AdvancedVisemeVisibleFeatureChannel.LipProtrusion] = BuildProtrusionValue(
                    graph, alwaysRoot,
                    calibratedTrackingRaw[AdvancedVisemeArticulator.LipFunnel],
                    calibratedTrackingRaw[AdvancedVisemeArticulator.LipPucker],
                    calibratedTrackingRaw[AdvancedVisemeArticulator.LipSuck],
                    "TongueInference/Visible/LipProtrusion/Tracked");
                center[AdvancedVisemeVisibleFeatureChannel.LipProtrusion] = BuildProtrusionValue(
                    graph, alwaysRoot,
                    speechCenters[AdvancedVisemeArticulator.LipFunnel],
                    speechCenters[AdvancedVisemeArticulator.LipPucker],
                    speechCenters[AdvancedVisemeArticulator.LipSuck],
                    "TongueInference/Visible/LipProtrusion/Speech");
                var protrusionPositiveGain = MaxParameters(
                    graph, alwaysRoot,
                    "TongueInference/Gain/LipProtrusionPositive",
                    trackingGains[AdvancedVisemeArticulator.LipFunnel],
                    trackingGains[AdvancedVisemeArticulator.LipPucker]);
                featureGain[AdvancedVisemeVisibleFeatureChannel.LipProtrusion] = MinParameters(
                    graph, alwaysRoot, "TongueInference/Gain/LipProtrusion",
                    protrusionPositiveGain,
                    trackingGains[AdvancedVisemeArticulator.LipSuck]);
            }

            var alpha = graph.Param("TongueInference/Observer/Alpha", 0.5f);
            graph.AddOperation(alwaysRoot, graph.AlphaFromDeltaTime(
                frameTime, alpha, AdvancedVisemeHiddenPhonePosterior.ObserverResponseSeconds));
            var featureChannels = quality
                ? new[]
                {
                    AdvancedVisemeVisibleFeatureChannel.JawOpen,
                    AdvancedVisemeVisibleFeatureChannel.JawAdvance,
                    AdvancedVisemeVisibleFeatureChannel.LipAperture,
                    AdvancedVisemeVisibleFeatureChannel.LipProtrusion
                }
                : hasProtrusion
                    ? new[]
                    {
                        AdvancedVisemeVisibleFeatureChannel.JawOpen,
                        AdvancedVisemeVisibleFeatureChannel.LipAperture,
                        AdvancedVisemeVisibleFeatureChannel.LipProtrusion
                    }
                    : new[]
                    {
                        AdvancedVisemeVisibleFeatureChannel.JawOpen,
                        AdvancedVisemeVisibleFeatureChannel.LipAperture
                    };
            var featureParameters = new string[
                AdvancedVisemeHiddenPhonePosterior.FeatureCount(phoneKind)];
            var measurementOodFactors = new List<string>();
            var tongueOodFactors = new List<string>();
            var gainFactors = new List<string>();
            for (var channelIndex = 0; channelIndex < featureChannels.Length; channelIndex++)
            {
                var channel = featureChannels[channelIndex];

                var residual = BuildHeadroomNormalizedResidual(
                    graph, alwaysRoot, current[channel], center[channel],
                    "TongueInference/Feature/" + channel, out var ood);
                var fast = graph.Param($"TongueInference/Feature/{channel}/Fast", 0f);
                var slow = graph.Param($"TongueInference/Feature/{channel}/Slow", 0f);
                graph.AddOperation(alwaysRoot,
                    graph.Smooth(residual, fast, alpha, true));
                graph.AddOperation(alwaysRoot,
                    graph.Smooth(fast, slow, alpha, true));
                var currentMinusFast = graph.Param(
                    $"TongueInference/Feature/{channel}/CurrentMinusFast", 0f);
                var fastMinusSlow = graph.Param(
                    $"TongueInference/Feature/{channel}/FastMinusSlow", 0f);
                graph.AddOperation(alwaysRoot, graph.Linear(currentMinusFast, new[]
                {
                    Term.Signed(residual, 1f), Term.Signed(fast, -1f)
                }));
                graph.AddOperation(alwaysRoot, graph.Linear(fastMinusSlow, new[]
                {
                    Term.Signed(fast, 1f), Term.Signed(slow, -1f)
                }));
                featureParameters[channelIndex] = residual;
                featureParameters[featureChannels.Length + channelIndex] =
                    currentMinusFast;
                featureParameters[2 * featureChannels.Length + channelIndex] =
                    fastMinusSlow;
                measurementOodFactors.Add(ood);
                if (tongueKind.HasValue)
                {
                    var tongueChannelIndex = AdvancedVisemeVisibleTongueResidual.FeatureChannelIndex(
                        tongueKind.Value, channel);
                    for (var stage = 0;
                         stage < AdvancedVisemeVisibleTongueResidual.FeatureStageCount;
                         stage++)
                    {
                        var featureIndex = stage * featureChannels.Length + channelIndex;
                        var tongueFeatureIndex = stage *
                            AdvancedVisemeVisibleTongueResidual.FeatureChannelCount(
                                tongueKind.Value) + tongueChannelIndex;
                        tongueOodFactors.Add(BuildEmpiricalFeatureSupport(
                            graph, root, featureParameters[featureIndex],
                            tongueKind.Value, tongueFeatureIndex,
                            $"TongueInference/Feature/{channel}/Stage{stage}/Support"));
                    }
                }
                gainFactors.Add(featureGain[channel]);
            }
            if (featureParameters.Any(string.IsNullOrEmpty)) return null;
            tongueOodFactors.AddRange(measurementOodFactors);

            var visibleGain = MinParameters(
                graph, alwaysRoot,
                "TongueInference/VisibleGain", gainFactors.ToArray());
            FacePhonePosteriorGraph phonePosterior = null;
            if (AdvancedVisemeHiddenPhonePosterior.FeatureCount(phoneKind) == featureParameters.Length)
            {
                // The hidden-phone classifier is trained around the exact Beta
                // group-center trajectory. Its support envelope is therefore
                // separate from the older tongue-residual estimator's hard-center
                // envelope; sharing that gate can silently reject valid evidence.
                var phoneSupport = new List<string>(measurementOodFactors);
                phoneSupport.AddRange(featureParameters.Select((parameter, featureIndex) =>
                    BuildEmpiricalFeatureSupport(
                        graph, root, parameter,
                        AdvancedVisemeHiddenPhonePosterior.FeatureAbsP995(
                            phoneKind, featureIndex),
                        AdvancedVisemeHiddenPhonePosterior.FeatureSafeBound(
                            phoneKind, featureIndex),
                        $"PhonePosterior/Feature/{featureIndex}/Support")));
                var phoneOodConfidence = BuildSmoothedSupportConfidence(
                    graph, root, "PhonePosterior/OodConfidence", phoneSupport,
                    alpha);
                var phoneTrackingConfidence = graph.Param(
                    "PhonePosterior/Confidence/Tracking", 0f);
                var phoneActivityConfidence = graph.Param(
                    "PhonePosterior/Confidence/Activity", 0f);
                graph.AddOperation(root, graph.Multiply(
                    visibleGain, phoneOodConfidence, phoneTrackingConfidence, false));
                graph.AddOperation(root, graph.Multiply(
                    phoneTrackingConfidence, speechPresence, phoneActivityConfidence, false));
                var compatibleConfidence = graph.Param(
                    "PhonePosterior/Confidence/ModelCompatibility", 0f);
                graph.AddOperation(root, graph.Linear(compatibleConfidence, new[]
                {
                    Term.Positive(phoneActivityConfidence,
                        HiddenPhoneObserverCompatibility(
                            request.profile.visemeResponseSeconds))
                }));
                var coarticulatedConfidence = graph.Param(
                    "PhonePosterior/Confidence/Coarticulation", 0f);
                graph.AddOperation(root, graph.Multiply(
                    compatibleConfidence,
                    tuning[AdvancedVisemeTuningControl.Coarticulation],
                    coarticulatedConfidence, false));
                var phoneConfidence = graph.Param(
                    "PhonePosterior/Confidence/Tuned", 0f);
                graph.AddOperation(root, graph.Multiply(
                    coarticulatedConfidence,
                    tuning[AdvancedVisemeTuningControl.HiddenPhone],
                    phoneConfidence, false));
                phonePosterior = BuildFacePhonePosterior(
                    graph, root, alwaysRoot, authority,
                    request, result, betaGraph, phoneKind,
                    featureParameters, phoneConfidence, frameTime);
                RebuildConditionedTongueSpeech(
                    graph, alwaysRoot, root, authority,
                    request, phonePosterior, speechGain,
                    speechFast, speechSlow);
            }

            // Aperture-only tailored templates can still correct hidden M/N/L
            // tongue priors. The separate visible-to-tongue residual regressor
            // needs protrusion, so stop here only after applying that correction.
            if (!tongueKind.HasValue) return phonePosterior;
            var kind = tongueKind.Value;

            // The residual regressor's empirical envelope is from EMA rather
            // than paired UE captures. Leaving it is a conservative, smoothly
            // reversible abstention instead of a clamp or tracker failure.
            var oodConfidence = BuildSmoothedSupportConfidence(
                graph, root, "TongueInference/OodConfidence", tongueOodFactors,
                alpha);
            var confidenceTracking = graph.Param("TongueInference/Confidence/Tracking", 0f);
            var confidenceSpeech = graph.Param("TongueInference/Confidence/Speech", 0f);
            graph.AddOperation(root, graph.Multiply(
                visibleGain, oodConfidence, confidenceTracking, false));
            // Speech amplitude is applied exactly once to final articulation.
            // Posterior authority follows activity, so quiet tracked speech is
            // not weakened a second time.
            graph.AddOperation(root, graph.Multiply(
                confidenceTracking, speechPresence, confidenceSpeech, false));
            // Each visible-channel gain already contains its channel-specific
            // tracking blend; applying it again would square confidence.
            var betaTongueConfidence = graph.Param(
                "TongueInference/Confidence/Coarticulation", 0f);
            graph.AddOperation(root, graph.Multiply(
                confidenceSpeech,
                tuning[AdvancedVisemeTuningControl.Coarticulation],
                betaTongueConfidence, false));
            var tongueConfidence = graph.Param(
                "TongueInference/Confidence/Tuned", 0f);
            graph.AddOperation(root, graph.Multiply(
                betaTongueConfidence,
                tuning[AdvancedVisemeTuningControl.TongueInference],
                tongueConfidence, false));

            var visemeWeights =
                betaGraph.groups[AdvancedVisemeArticulatorGroup.TongueTip].slow;
            var visemeRankOneDelta = phonePosterior != null &&
                                     phonePosterior.corrections.TryGetValue(
                                         AdvancedVisemeArticulatorGroup.TongueTip,
                                         out var tongueTipCorrection)
                ? tongueTipCorrection.slow
                : null;
            var reliability = graph.Param("TongueInference/Model/Reliability", 0f);
            var modelOutputs = Enum.GetValues(typeof(AdvancedVisemeVisibleTongueOutput))
                .Cast<AdvancedVisemeVisibleTongueOutput>()
                .ToArray();
            var outputScales = modelOutputs.ToDictionary(
                output => output,
                output => Mathf.Max(1e-6f,
                    AdvancedVisemeVisibleTongueResidual.ConservativeOutputBound(kind, output)));
            var normalizedOutputs = modelOutputs.ToDictionary(
                output => output,
                output => graph.Param($"TongueInference/Model/{output}/Normalized", 0f));

            if (UseCollapsedVisibleTongueKernelForTests)
            {
                graph.AddOperation(root, graph.SimplexMatrixProjection(
                    visemeWeights,
                    new[] { reliability },
                    (viseme, _) =>
                        AdvancedVisemeVisibleTongueResidual.Reliability(kind, viseme),
                    visemeRankOneDelta,
                    _ => AdvancedVisemeVisibleTongueResidual.Reliability(kind, 1) -
                         AdvancedVisemeVisibleTongueResidual.Reliability(kind, 8),
                    "Tongue inference collapsed reliability mixture"));

                var featureUnits = featureParameters.Select((_, feature) =>
                        graph.Param($"TongueInference/Model/FeatureUnit/{feature}", 0.5f))
                    .ToArray();
                graph.AddOperation(root, graph.SignedMatrixProjection(
                    featureParameters,
                    featureUnits,
                    Enumerable.Repeat(0.5f, featureUnits.Length).ToArray(),
                    (input, output) => input == output
                        ? 0.5f / AdvancedVisemeVisibleTongueResidual
                            .FeatureSafeBound(kind, input)
                        : 0f,
                    "Tongue inference collapsed unit features"));

                var collapsed = graph.Direct(
                    "Tongue inference collapsed unit-feature kernel");
                var collapsedChildren = new List<ChildMotion>(featureUnits.Length + 1);
                for (var lane = 0; lane <= featureUnits.Length; lane++)
                {
                    var laneIndex = lane;
                    float Coefficient(int viseme, int outputIndex)
                    {
                        var output = modelOutputs[outputIndex];
                        var value = laneIndex == 0
                            ? CollapsedTongueUnitBias(kind, viseme, output)
                            : CollapsedTongueUnitFeatureCoefficient(
                                kind, viseme, laneIndex - 1, output);
                        return value / outputScales[output];
                    }

                    collapsedChildren.Add(new ChildMotion
                    {
                        motion = graph.DenseSimplexMatrixProjection(
                            visemeWeights,
                            modelOutputs.Select(output => normalizedOutputs[output]).ToArray(),
                            Coefficient,
                            visemeRankOneDelta,
                            outputIndex => Coefficient(1, outputIndex) -
                                           Coefficient(8, outputIndex),
                            $"Tongue inference collapsed lane {lane}"),
                        directBlendParameter = lane == 0
                            ? MathGraph.AlwaysOneParameter
                            : featureUnits[lane - 1],
                        timeScale = 1f
                    });
                }
                collapsed.children = collapsedChildren.ToArray();
                graph.AddOperation(root, collapsed);
            }
            else
            {
                var latent = new string[
                    AdvancedVisemeVisibleTongueResidual.LatentCount(kind)];
                var latentScales = new float[latent.Length];
                for (var latentIndex = 0; latentIndex < latent.Length; latentIndex++)
                {
                    var latentScale = Mathf.Max(1e-6f,
                        AdvancedVisemeVisibleTongueResidual.VisibleLatentSafeBound(
                            kind, latentIndex));
                    latentScales[latentIndex] = latentScale;
                    latent[latentIndex] = graph.Param(
                        $"TongueInference/Model/Visible/{latentIndex}", 0f);
                }
                graph.AddOperation(root, graph.SignedMatrixProjection(
                    featureParameters,
                    latent,
                    new float[latent.Length],
                    (featureIndex, latentIndex) =>
                        AdvancedVisemeVisibleTongueResidual.InputProjection(
                            kind, featureIndex, latentIndex) /
                        latentScales[latentIndex],
                    "Tongue inference visible latent contraction"));

            var contractedBase = modelOutputs.ToDictionary(
                output => output,
                output => graph.Param($"TongueInference/Model/{output}/ContractedBase", 0f));
            var contractedMix = new string[latent.Length, modelOutputs.Length];
            var contractedMixMinimum = new float[latent.Length, modelOutputs.Length];
            var contractedMixRange = new float[latent.Length, modelOutputs.Length];
            // For each latent/output ray, affine-shift the 15 viseme coefficients
            // into [0,1]. A simplex-weighted mixture stays in that interval and is
            // therefore a legal Direct-tree product weight. The final contraction
            // restores minimum + range * unitMix exactly; no model quantization or
            // latent-bound assumption is involved.
            for (var latentIndex = 0; latentIndex < latent.Length; latentIndex++)
            for (var outputIndex = 0; outputIndex < modelOutputs.Length; outputIndex++)
            {
                var output = modelOutputs[outputIndex];
                var coefficients = Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                    .Select(viseme => ContractedTongueMix(
                        kind, viseme, latentIndex, output, latentScales, outputScales))
                    .ToArray();
                var minimum = coefficients.Min();
                var maximum = coefficients.Max();
                contractedMixMinimum[latentIndex, outputIndex] = minimum;
                contractedMixRange[latentIndex, outputIndex] = maximum - minimum;
                contractedMix[latentIndex, outputIndex] = graph.Param(
                    $"TongueInference/Model/{output}/MixUnit/{latentIndex}", 0f);
            }

            var simplexOutputs = new List<string> { reliability };
            simplexOutputs.AddRange(modelOutputs.Select(output => contractedBase[output]));
            for (var latentIndex = 0; latentIndex < latent.Length; latentIndex++)
            for (var outputIndex = 0; outputIndex < modelOutputs.Length; outputIndex++)
                simplexOutputs.Add(contractedMix[latentIndex, outputIndex]);

            graph.AddOperation(root, graph.SimplexMatrixProjection(
                visemeWeights,
                simplexOutputs,
                (viseme, column) =>
                {
                    if (column == 0)
                        return AdvancedVisemeVisibleTongueResidual.Reliability(kind, viseme);

                    var baseEnd = 1 + modelOutputs.Length;
                    if (column < baseEnd)
                    {
                        var outputIndex = column - 1;
                        return ContractedTongueBias(
                            kind, viseme, modelOutputs[outputIndex], outputScales);
                    }

                    var mixColumn = column - baseEnd;
                    var latentIndexForColumn = mixColumn / modelOutputs.Length;
                    var outputIndexForColumn = mixColumn % modelOutputs.Length;
                    var range = contractedMixRange[
                        latentIndexForColumn, outputIndexForColumn];
                    if (range <= 1e-8f) return 0f;
                    return (ContractedTongueMix(
                                kind, viseme, latentIndexForColumn,
                                modelOutputs[outputIndexForColumn], latentScales, outputScales) -
                            contractedMixMinimum[
                                latentIndexForColumn, outputIndexForColumn]) / range;
                },
                visemeRankOneDelta,
                column =>
                {
                    if (column == 0)
                        return AdvancedVisemeVisibleTongueResidual.Reliability(kind, 1) -
                               AdvancedVisemeVisibleTongueResidual.Reliability(kind, 8);

                    var baseEnd = 1 + modelOutputs.Length;
                    if (column < baseEnd)
                    {
                        var outputIndex = column - 1;
                        return ContractedTongueBias(
                                   kind, 1, modelOutputs[outputIndex], outputScales) -
                               ContractedTongueBias(
                                   kind, 8, modelOutputs[outputIndex], outputScales);
                    }

                    var mixColumn = column - baseEnd;
                    var latentIndexForColumn = mixColumn / modelOutputs.Length;
                    var outputIndexForColumn = mixColumn % modelOutputs.Length;
                    var range = contractedMixRange[
                        latentIndexForColumn, outputIndexForColumn];
                    if (range <= 1e-8f) return 0f;
                    return (ContractedTongueMix(
                                kind, 1, latentIndexForColumn,
                                modelOutputs[outputIndexForColumn], latentScales, outputScales) -
                            ContractedTongueMix(
                                kind, 8, latentIndexForColumn,
                                modelOutputs[outputIndexForColumn], latentScales, outputScales)) / range;
                },
                "Tongue inference viseme contraction"));

            var useLegacyVisibleTongueProducts =
                UseLegacyVisibleTongueProductGraphForTests ||
                !FusedTongueAccumulatorFitsSignedHandoff(
                    kind, modelOutputs, latentScales, outputScales,
                    contractedMixMinimum, contractedMixRange);
            if (useLegacyVisibleTongueProducts)
            {
                var contractedProducts = new string[latent.Length, modelOutputs.Length];
                for (var latentIndex = 0; latentIndex < latent.Length; latentIndex++)
                for (var outputIndex = 0; outputIndex < modelOutputs.Length; outputIndex++)
                    contractedProducts[latentIndex, outputIndex] = graph.Param(
                        $"TongueInference/Model/{modelOutputs[outputIndex]}/Product/{latentIndex}",
                        0f);
                var productWeights = new List<string>();
                var productInputs = new string[latent.Length * modelOutputs.Length, 1];
                var productOutputs = new string[latent.Length * modelOutputs.Length, 1];
                for (var latentIndex = 0; latentIndex < latent.Length; latentIndex++)
                for (var outputIndex = 0; outputIndex < modelOutputs.Length; outputIndex++)
                {
                    var productIndex = latentIndex * modelOutputs.Length + outputIndex;
                    productWeights.Add(contractedMix[latentIndex, outputIndex]);
                    productInputs[productIndex, 0] = latent[latentIndex];
                    productOutputs[productIndex, 0] =
                        contractedProducts[latentIndex, outputIndex];
                }
                graph.AddOperation(root, graph.GroupedElementwiseProducts(
                    productWeights,
                    productInputs,
                    productOutputs,
                    "Tongue inference contracted bilinear products"));

                var legacySumInputs = new List<string>();
                legacySumInputs.AddRange(
                    modelOutputs.Select(output => contractedBase[output]));
                legacySumInputs.AddRange(latent);
                for (var latentIndex = 0; latentIndex < latent.Length; latentIndex++)
                for (var outputIndex = 0; outputIndex < modelOutputs.Length; outputIndex++)
                    legacySumInputs.Add(contractedProducts[latentIndex, outputIndex]);
                graph.AddOperation(root, graph.SignedMatrixProjection(
                    legacySumInputs,
                    modelOutputs.Select(output => normalizedOutputs[output]).ToArray(),
                    new float[modelOutputs.Length],
                    (inputIndex, outputIndex) =>
                    {
                        if (inputIndex < modelOutputs.Length)
                            return inputIndex == outputIndex ? 1f : 0f;
                        var latentEnd = modelOutputs.Length + latent.Length;
                        if (inputIndex < latentEnd)
                            return contractedMixMinimum[
                                inputIndex - modelOutputs.Length, outputIndex];
                        var productIndex = inputIndex - latentEnd;
                        return productIndex % modelOutputs.Length == outputIndex
                            ? contractedMixRange[
                                productIndex / modelOutputs.Length, outputIndex]
                            : 0f;
                    },
                    "Tongue inference contracted output sum"));
            }
            else
            {
                // Fuse the eight range * unitMix * latent products into the two
                // output accumulators at the former product publication epoch.
                // The affine minimum term deliberately remains in the following
                // sum: moving it into this accumulator would delay that term by a
                // frame and change the trained model's transient trajectory.
                var accumulatedProducts = modelOutputs
                    .Select(output => graph.Param(
                        $"TongueInference/Model/{output}/ProductAccumulator", 0f))
                    .ToArray();
                graph.AddOperation(root, graph.WeightedSignedMatrixAccumulator(
                    latent,
                    contractedMix,
                    accumulatedProducts,
                    (latentIndex, outputIndex) =>
                        contractedMixRange[latentIndex, outputIndex],
                    "Tongue inference two-output bilinear accumulator"));

                var sumInputs = new List<string>();
                sumInputs.AddRange(
                    modelOutputs.Select(output => contractedBase[output]));
                sumInputs.AddRange(latent);
                sumInputs.AddRange(accumulatedProducts);
                graph.AddOperation(root, graph.SignedMatrixProjection(
                    sumInputs,
                    modelOutputs.Select(output => normalizedOutputs[output]).ToArray(),
                    new float[modelOutputs.Length],
                    (inputIndex, outputIndex) =>
                    {
                        if (inputIndex < modelOutputs.Length)
                            return inputIndex == outputIndex ? 1f : 0f;
                        var latentEnd = modelOutputs.Length + latent.Length;
                        if (inputIndex < latentEnd)
                            return contractedMixMinimum[
                                inputIndex - modelOutputs.Length, outputIndex];
                        return inputIndex - latentEnd == outputIndex ? 1f : 0f;
                    },
                    "Tongue inference accumulated output handoff"));
            }
            }

            var predictions = new Dictionary<AdvancedVisemeVisibleTongueOutput, string>();
            foreach (var output in modelOutputs)
            {
                var outputScale = outputScales[output];
                var normalized = normalizedOutputs[output];
                var reliable = graph.Param($"TongueInference/Model/{output}/Reliable", 0f);
                graph.AddOperation(root, graph.Multiply(reliability, normalized, reliable, true));
                var prediction = graph.Param($"TongueInference/Model/{output}", 0f);
                graph.AddOperation(root, graph.Map(
                    reliable, prediction, ScaledClampPoints(outputScale)));
                // The regressor intentionally consumes the feature convention it
                // was trained on, including its raw residual stage. Filter the
                // inferred latent output instead so OSC chatter cannot bypass the
                // visible-pose denoiser without shifting the model's input domain.
                var predictionFast = graph.Param($"TongueInference/Model/{output}/StableFast", 0f);
                var predictionStable = graph.Param($"TongueInference/Model/{output}/Stable", 0f);
                AddTwoPoleObserver(
                    graph, root, prediction, predictionFast,
                    predictionStable, alpha, true);
                predictions[output] = predictionStable;
            }

            var tongueOutVisibility = graph.Param("TongueInference/TongueOut/Visibility", 0f);
            graph.AddOperation(root, graph.Linear(tongueOutVisibility, new[]
            {
                Term.Positive(visemeWeights[3], 0.85f),
                Term.Positive(current[AdvancedVisemeVisibleFeatureChannel.LipAperture], 0.15f)
            }));
            var tongueOutConfidenceVisible = graph.Param(
                "TongueInference/TongueOut/ConfidenceVisible", 0f);
            graph.AddOperation(root,
                graph.Multiply(tongueConfidence, tongueOutVisibility, tongueOutConfidenceVisible, false));
            var tongueOutConfidence = graph.Param("TongueInference/TongueOut/Confidence", 0f);
            graph.AddOperation(root, graph.Linear(tongueOutConfidence, new[]
            {
                Term.Positive(tongueOutConfidenceVisible, 0.30f)
            }));
            var tongueYConfidence = graph.Param("TongueInference/TongueY/Confidence", 0f);
            graph.AddOperation(root, graph.Linear(tongueYConfidence, new[]
            {
                Term.Positive(tongueConfidence, 0.65f)
            }));

            var tongueYPrediction = predictions[AdvancedVisemeVisibleTongueOutput.TongueY];
            var tongueYBinding = request.profile.FindBinding(AdvancedVisemeArticulator.TongueY);
            if (tongueYBinding != null && tongueYBinding.trackingScale < 0f)
            {
                var inverted = graph.Param("TongueInference/Model/TongueY/Inverted", 0f);
                graph.AddOperation(root, graph.Linear(inverted, new[]
                {
                    Term.Signed(tongueYPrediction, -1f)
                }));
                tongueYPrediction = inverted;
            }

            var tongueOutFastCenter =
                speechFast[AdvancedVisemeArticulator.TongueOut];
            var tongueOutSlowCenter =
                speechSlow[AdvancedVisemeArticulator.TongueOut];
            var tongueYFastCenter =
                speechFast[AdvancedVisemeArticulator.TongueY];
            var tongueYSlowCenter =
                speechSlow[AdvancedVisemeArticulator.TongueY];
            var inferredTongueOutFast = ApplyHeadroomResidual(
                graph, root, alwaysRoot, authority, tongueOutFastCenter,
                predictions[AdvancedVisemeVisibleTongueOutput.TongueOut], tongueOutConfidence,
                false, "TongueInference/TongueOut/Fast");
            var inferredTongueOutSlow = ApplyHeadroomResidual(
                graph, root, alwaysRoot, authority, tongueOutSlowCenter,
                predictions[AdvancedVisemeVisibleTongueOutput.TongueOut], tongueOutConfidence,
                false, "TongueInference/TongueOut/Slow");
            var inferredTongueYFast = ApplyHeadroomResidual(
                graph, root, alwaysRoot, authority, tongueYFastCenter,
                tongueYPrediction, tongueYConfidence,
                true, "TongueInference/TongueY/Fast");
            var inferredTongueYSlow = ApplyHeadroomResidual(
                graph, root, alwaysRoot, authority, tongueYSlowCenter,
                tongueYPrediction, tongueYConfidence,
                true, "TongueInference/TongueY/Slow");
            speechFast[AdvancedVisemeArticulator.TongueOut] =
                inferredTongueOutFast;
            speechSlow[AdvancedVisemeArticulator.TongueOut] =
                inferredTongueOutSlow;
            speechFast[AdvancedVisemeArticulator.TongueY] =
                inferredTongueYFast;
            speechSlow[AdvancedVisemeArticulator.TongueY] =
                inferredTongueYSlow;
            return phonePosterior;
        }

        private static FacePhonePosteriorGraph BuildFacePhonePosterior(
            MathGraph graph,
            BlendTree root,
            BlendTree alwaysRoot,
            string authority,
            Request request,
            Result result,
            BetaCoarticulationGraph betaGraph,
            AdvancedVisemeHiddenPhoneModelKind kind,
            IReadOnlyList<string> features,
            string visibleConfidence,
            string frameTime)
        {
            var bound = Mathf.Max(1f,
                AdvancedVisemeHiddenPhonePosterior.ConservativeLogitBound(kind));
            var observationWeights = betaGraph.phoneObservationFast;
            if (observationWeights == null ||
                observationWeights.Count != VisemeReconstructionProfile.VisemeCount)
                throw new InvalidOperationException(
                    "Face-conditioned tongue inference requires its trained private observation simplex.");
            var normalizedLogit = graph.Param("PhonePosterior/Model/NormalizedLogit", 0f);
            if (HiddenPhoneCoefficientsAreShared(kind, features.Count))
            {
                // The fitted observation likelihood is deliberately independent
                // of the hard Oculus class; only the empirical phone prior changes
                // with that class. Factor the shared affine likelihood once and
                // simplex-mix the 15 priors. This is algebraically identical to 15
                // full experts while avoiding their duplicated Animator work.
                var terms = observationWeights.Select((parameter, viseme) =>
                        Term.Positive(parameter,
                            AdvancedVisemeHiddenPhonePosterior.Bias(kind, viseme) / bound))
                    .ToList();
                for (var feature = 0; feature < features.Count; feature++)
                {
                    terms.Add(Term.Signed(features[feature],
                        AdvancedVisemeHiddenPhonePosterior.Coefficient(
                            kind, 0, feature) / bound));
                }
                graph.AddOperation(root, graph.Linear(normalizedLogit, terms));
            }
            else
            {
                // Retain the general mixture-of-experts form if a future generated
                // model intentionally introduces viseme-specific face likelihoods.
                var weightedExperts = new List<string>();
                for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
                {
                    var conditional = graph.Param(
                        $"PhonePosterior/Model/Viseme/{viseme}/NormalizedLogit", 0f);
                    var terms = new List<Term>
                    {
                        Term.Constant(AdvancedVisemeHiddenPhonePosterior.Bias(kind, viseme) / bound)
                    };
                    for (var feature = 0; feature < features.Count; feature++)
                    {
                        terms.Add(Term.Signed(features[feature],
                            AdvancedVisemeHiddenPhonePosterior.Coefficient(
                                kind, viseme, feature) / bound));
                    }
                    graph.AddOperation(root, graph.Linear(conditional, terms));

                    var weighted = graph.Param(
                        $"PhonePosterior/Model/Viseme/{viseme}/WeightedLogit", 0f);
                    graph.AddOperation(root, graph.Multiply(
                        observationWeights[viseme], conditional, weighted, true));
                    weightedExperts.Add(weighted);
                }
                graph.AddOperation(root, graph.Linear(normalizedLogit,
                    weightedExperts.Select(parameter => Term.Signed(parameter, 1f))));
            }

            var logit = graph.Param("PhonePosterior/Model/Logit", 0f);
            graph.AddOperation(root, graph.Linear(logit, new[]
            {
                Term.Signed(normalizedLogit, bound)
            }));
            var rawShare = graph.Param("PhonePosterior/Model/MShareRaw", 0.5f);
            graph.AddOperation(root, graph.Map(
                logit, rawShare, LogisticPoints(bound)));

            var alpha = graph.Param("PhonePosterior/Observer/Alpha", 0.5f);
            graph.AddOperation(alwaysRoot, graph.AlphaFromDeltaTime(
                frameTime, alpha, AdvancedVisemeHiddenPhonePosterior.ObserverResponseSeconds));
            var shareFast = graph.Param("PhonePosterior/Model/MShareFast", 0.5f);
            var shareSlow = graph.Param("PhonePosterior/Model/MShareSlow", 0.5f);
            AddTwoPoleObserver(
                graph, root, rawShare, shareFast, shareSlow, alpha, false);

            var reliability = graph.Param("PhonePosterior/Model/Reliability", 0f);
            graph.AddOperation(root, graph.Linear(reliability,
                observationWeights.Select((parameter, viseme) => Term.Positive(
                    parameter,
                    AdvancedVisemeHiddenPhonePosterior.Reliability(kind, viseme)))));
            var centered = graph.Param("PhonePosterior/Model/CenteredShare", 0f);
            var margin = graph.Param("PhonePosterior/Model/Margin", 0f);
            var marginConfidence = graph.Param("PhonePosterior/Model/MarginConfidence", 0f);
            graph.AddOperation(root, graph.Linear(centered, new[]
            {
                Term.Constant(-1f), Term.Positive(shareSlow, 2f)
            }));
            graph.AddOperation(root, graph.Abs(centered, margin));
            graph.AddOperation(root, graph.Map(
                margin, marginConfidence, SmoothStepPoints(0.12f, 0.65f, 0f, 1f)));

            var reliableConfidence = graph.Param("PhonePosterior/Confidence/Reliable", 0f);
            var posteriorConfidence = graph.Param("PhonePosterior/Confidence", 0f);
            graph.AddOperation(root, graph.Multiply(
                visibleConfidence, reliability, reliableConfidence, false));
            graph.AddOperation(root, graph.Multiply(
                reliableConfidence, marginConfidence, posteriorConfidence, false));

            // Unified Expressions exposes a real velum channel on richer
            // installations. Reuse it only when it already exists and has shown
            // sustained non-noise motion. A closed soft palate is oral evidence
            // (p/b/l), so it lowers posterior authority instead of incorrectly
            // transferring that mass to N. No fresh synced parameter is created.
            if (request.auxiliaryTrackingParameterNames != null &&
                request.auxiliaryTrackingParameterNames.TryGetValue(
                    "SoftPalateClose", out var softPalate) &&
                !string.IsNullOrWhiteSpace(softPalate))
            {
                var palateFast = graph.Param("PhonePosterior/SoftPalate/Fast", 0f);
                var palateSlow = graph.Param("PhonePosterior/SoftPalate/Slow", 0f);
                graph.AddOperation(alwaysRoot,
                    graph.Smooth(softPalate, palateFast, alpha, false));
                graph.AddOperation(alwaysRoot,
                    graph.Smooth(palateFast, palateSlow, alpha, false));
                var observed = graph.Param("PhonePosterior/SoftPalate/Observed", 0f);
                graph.AddOperation(alwaysRoot, graph.Map(softPalate, observed, new[]
                {
                    Point(0f, 0f), Point(0.005f, 0f), Point(0.03f, 1f), Point(1f, 1f)
                }));
                var capabilityAlpha = graph.Param("PhonePosterior/SoftPalate/CapabilityAlpha", 0.1f);
                graph.AddOperation(alwaysRoot, graph.AlphaFromDeltaTime(
                    frameTime, capabilityAlpha, 0.12f));
                var accumulated = graph.Param("PhonePosterior/SoftPalate/Accumulated", 0f);
                graph.AddOperation(alwaysRoot, graph.Smooth(
                    observed, accumulated, capabilityAlpha, false));
                var confirmed = graph.Param("PhonePosterior/SoftPalate/Confirmed", 0f);
                graph.AddOperation(alwaysRoot, graph.Map(accumulated, confirmed, new[]
                {
                    Point(0f, 0f), Point(0.78f, 0f), Point(0.8f, 1f), Point(1f, 1f)
                }));
                var capability = graph.Param("PhonePosterior/SoftPalate/Capability", 0f);
                var latched = graph.Param("PhonePosterior/SoftPalate/Latched", 0f);
                // Capability is a lifetime observation, not speech detail. Keep
                // its recurrent latch warm while the learned posterior sleeps.
                graph.AddOperation(alwaysRoot,
                    graph.Max(capability, confirmed, latched));
                graph.AddOperation(alwaysRoot,
                    graph.Copy(latched, capability, false));
                var oralEvidence = graph.Param("PhonePosterior/SoftPalate/OralEvidence", 0f);
                var nasalCompatibility = graph.Param(
                    "PhonePosterior/SoftPalate/NasalCompatibility", 1f);
                var palateAdjusted = graph.Param("PhonePosterior/Confidence/PalateAdjusted", 0f);
                graph.AddOperation(root, graph.Multiply(
                    capability, palateSlow, oralEvidence, false));
                graph.AddOperation(root, graph.Linear(nasalCompatibility, new[]
                {
                    Term.Constant(1f), Term.Positive(oralEvidence, -1f)
                }));
                graph.AddOperation(root, graph.Multiply(
                    posteriorConfidence, nasalCompatibility, palateAdjusted, false));
                posteriorConfidence = palateAdjusted;
            }

            // Preserve the authored/public viseme simplex. A calibrated build can
            // apply only the complement-space PP<->nn geometry as one signed
            // correction: delta = confidence * (posteriorPP - originalPP).
            var hiddenCandidateMass = graph.Param(
                "PhonePosterior/Residual/CandidateMass", 0f);
            var hiddenTargetPp = graph.Param("PhonePosterior/Residual/TargetPP", 0f);
            var hiddenRawDelta = graph.Param("PhonePosterior/Residual/RawDelta", 0f);
            var hiddenResidualDelta = graph.Param("PhonePosterior/Residual/Delta", 0f);
            graph.AddOperation(root, graph.Linear(hiddenCandidateMass, new[]
            {
                Term.Positive(betaGraph.common.slow[1], 1f),
                Term.Positive(betaGraph.common.slow[8], 1f)
            }));
            graph.AddOperation(root, graph.Multiply(
                shareSlow, hiddenCandidateMass, hiddenTargetPp, false));
            graph.AddOperation(root, graph.Linear(hiddenRawDelta, new[]
            {
                Term.Signed(hiddenTargetPp, 1f),
                Term.Signed(betaGraph.common.slow[1], -1f)
            }));
            var hiddenResidualProducer = graph.Multiply(
                hiddenRawDelta, posteriorConfidence, hiddenResidualDelta, true);
            if (string.Equals(
                    authority, MathGraph.AlwaysOneParameter,
                    StringComparison.Ordinal))
            {
                graph.AddOperation(root, hiddenResidualProducer);
            }
            else
            {
                // Select the original final producer so Delta keeps its legacy
                // publication epoch. Its downstream speech-gain chain therefore
                // sees zero at authority zero and the exact computed residual at
                // authority one without a computed->authorized handoff.
                graph.AddOperation(alwaysRoot, graph.SelectMotion(
                    authority,
                    graph.Setter(hiddenResidualDelta, 0f),
                    hiddenResidualProducer,
                    "Hidden phone residual authority endpoint"));
            }
            var output = new FacePhonePosteriorGraph
            {
                mShareFast = shareFast,
                mShareSlow = shareSlow,
                confidence = posteriorConfidence,
                hiddenResidualDelta = hiddenResidualDelta
            };
            BuildMergedNasalCorrections(
                graph, root, betaGraph, shareFast, shareSlow,
                posteriorConfidence, output);

            var prefix = request.component.NormalizedPrefix;
            var mOutput = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "Hypothesis/M"),
                0f, false);
            var nOutput = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "Hypothesis/N"),
                0f, false);
            var confidenceOutput = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "Hypothesis/Confidence"),
                0f, false);
            var hypothesisBase = graph.MultiSetter(
                "Hidden phone hypothesis normalized base",
                new[]
                {
                    new KeyValuePair<string, float>(mOutput, 0f),
                    new KeyValuePair<string, float>(nOutput, 0f),
                    new KeyValuePair<string, float>(confidenceOutput, 1f)
                });
            var whenN = graph.Direct("Hidden phone hypothesis N endpoint");
            whenN.children = new[]
            {
                new ChildMotion
                {
                    motion = graph.Setter(nOutput, 1f),
                    directBlendParameter = hiddenCandidateMass,
                    timeScale = 1f
                },
                new ChildMotion
                {
                    motion = hypothesisBase,
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                }
            };
            var whenM = graph.Direct("Hidden phone hypothesis M endpoint");
            whenM.children = new[]
            {
                new ChildMotion
                {
                    motion = graph.Setter(mOutput, 1f),
                    directBlendParameter = hiddenCandidateMass,
                    timeScale = 1f
                },
                new ChildMotion
                {
                    motion = hypothesisBase,
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                }
            };
            var distributed = graph.InterpolateMotions(
                whenN, whenM, shareSlow,
                "Hidden phone hypothesis M-N distribution");
            var weightedHypothesis = graph.Direct(
                "Hidden phone confidence-weighted hypothesis outputs");
            weightedHypothesis.children = new[]
            {
                new ChildMotion
                {
                    motion = distributed,
                    directBlendParameter = posteriorConfidence,
                    timeScale = 1f
                },
                new ChildMotion
                {
                    motion = graph.MultiSetter(
                        "Hidden phone hypothesis safety zero",
                        new[]
                        {
                            new KeyValuePair<string, float>(mOutput, 0f),
                            new KeyValuePair<string, float>(nOutput, 0f),
                            new KeyValuePair<string, float>(confidenceOutput, 0f)
                        }),
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                }
            };
            if (string.Equals(
                    authority, MathGraph.AlwaysOneParameter,
                    StringComparison.Ordinal))
            {
                graph.AddOperation(root, weightedHypothesis);
            }
            else
            {
                graph.AddOperation(alwaysRoot, graph.SelectMotion(
                    authority,
                    graph.MultiSetter(
                        "Hidden phone public hypothesis neutral fallback",
                        new[]
                        {
                            new KeyValuePair<string, float>(mOutput, 0f),
                            new KeyValuePair<string, float>(nOutput, 0f),
                            new KeyValuePair<string, float>(confidenceOutput, 0f)
                        }),
                    weightedHypothesis,
                    "Hidden phone public hypothesis authority endpoint"));
            }
            result.globalParameters.Add(mOutput);
            result.globalParameters.Add(nOutput);
            result.globalParameters.Add(confidenceOutput);
            return output;
        }

        internal static (float input, float output)[] LogisticPoints(float bound)
        {
            bound = Mathf.Max(1f, bound);
            return new[]
                {
                    -bound, -4.71307f, -3.19953f, -2.28008f, -1.56254f, -0.90370f,
                    0f,
                    0.90370f, 1.56254f, 2.28008f, 3.19953f, 4.71307f, bound
                }
                .Select(value => Mathf.Clamp(value, -bound, bound))
                .Distinct()
                .OrderBy(value => value)
                .Select(value => Point(value, AdvancedVisemeMath.Logistic(value)))
                .ToArray();
        }

        private static bool HiddenPhoneCoefficientsAreShared(
            AdvancedVisemeHiddenPhoneModelKind kind,
            int featureCount)
        {
            for (var viseme = 1; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
            for (var feature = 0; feature < featureCount; feature++)
            {
                if (!AdvancedVisemeHiddenPhonePosterior.Coefficient(kind, viseme, feature).Equals(
                        AdvancedVisemeHiddenPhonePosterior.Coefficient(kind, 0, feature)))
                    return false;
            }
            return true;
        }

        internal static float CollapsedTongueUnitBias(
            AdvancedVisemeVisibleTongueModelKind kind,
            int viseme,
            AdvancedVisemeVisibleTongueOutput output)
        {
            var value = AdvancedVisemeVisibleTongueResidual.CollapsedBias(
                kind, viseme, output);
            for (var feature = 0;
                 feature < AdvancedVisemeVisibleTongueResidual.FeatureCount(kind);
                 feature++)
                value -= AdvancedVisemeVisibleTongueResidual.FeatureSafeBound(
                             kind, feature) *
                         AdvancedVisemeVisibleTongueResidual
                             .CollapsedFeatureCoefficient(
                                 kind, viseme, feature, output);
            return value;
        }

        internal static float CollapsedTongueUnitFeatureCoefficient(
            AdvancedVisemeVisibleTongueModelKind kind,
            int viseme,
            int feature,
            AdvancedVisemeVisibleTongueOutput output)
        {
            return 2f * AdvancedVisemeVisibleTongueResidual.FeatureSafeBound(
                       kind, feature) *
                   AdvancedVisemeVisibleTongueResidual
                       .CollapsedFeatureCoefficient(
                           kind, viseme, feature, output);
        }

        private static float ContractedTongueBias(
            AdvancedVisemeVisibleTongueModelKind kind,
            int viseme,
            AdvancedVisemeVisibleTongueOutput output,
            IReadOnlyDictionary<AdvancedVisemeVisibleTongueOutput, float> outputScales)
        {
            var value = 0f;
            for (var target = 0;
                 target < AdvancedVisemeVisibleTongueResidual.TongueLatentCount(kind);
                 target++)
            {
                value += AdvancedVisemeVisibleTongueResidual.VisemeBias(
                             kind, viseme, target) *
                         AdvancedVisemeVisibleTongueResidual.OutputProjection(
                             kind, target, output) /
                         outputScales[output];
            }
            return value;
        }

        private static float ContractedTongueMix(
            AdvancedVisemeVisibleTongueModelKind kind,
            int viseme,
            int latent,
            AdvancedVisemeVisibleTongueOutput output,
            IReadOnlyList<float> latentScales,
            IReadOnlyDictionary<AdvancedVisemeVisibleTongueOutput, float> outputScales)
        {
            var value = 0f;
            for (var target = 0;
                 target < AdvancedVisemeVisibleTongueResidual.TongueLatentCount(kind);
                 target++)
            {
                value += AdvancedVisemeVisibleTongueResidual.VisemeMix(
                             kind, viseme, latent, target) *
                         AdvancedVisemeVisibleTongueResidual.OutputProjection(
                             kind, target, output);
            }
            return latentScales[latent] * value / outputScales[output];
        }

        private static bool FusedTongueAccumulatorFitsSignedHandoff(
            AdvancedVisemeVisibleTongueModelKind kind,
            IReadOnlyList<AdvancedVisemeVisibleTongueOutput> outputs,
            IReadOnlyList<float> latentScales,
            IReadOnlyDictionary<AdvancedVisemeVisibleTongueOutput, float> outputScales,
            float[,] minimum,
            float[,] range)
        {
            // The next SignedMatrixProjection copies each accumulated output with
            // a [-2,+2] Simple1D map. At a simplex vertex, every normalized latent
            // can independently reach magnitude two, so this is a conservative
            // handoff bound. Keep a margin for float interpolation and summation;
            // a future generated model outside it retains the legacy scalar AAPs.
            const double signedHandoffLimit = 2d;
            const double floatSafetyMargin = 1e-4d;
            for (var outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
            {
                var output = outputs[outputIndex];
                var maximumVertexMagnitude = 0d;
                for (var viseme = 0;
                     viseme < VisemeReconstructionProfile.VisemeCount;
                     viseme++)
                {
                    var vertexMagnitude = 0d;
                    for (var latentIndex = 0;
                         latentIndex < latentScales.Count;
                         latentIndex++)
                    {
                        var coefficientRange = range[latentIndex, outputIndex];
                        if (coefficientRange <= 1e-8f) continue;
                        var unitMix =
                            (ContractedTongueMix(
                                 kind, viseme, latentIndex, output,
                                 latentScales, outputScales) -
                             minimum[latentIndex, outputIndex]) /
                            coefficientRange;
                        var contribution =
                            (double)coefficientRange * unitMix;
                        if (double.IsNaN(contribution) ||
                            double.IsInfinity(contribution))
                            return false;
                        vertexMagnitude += Math.Abs(contribution);
                    }
                    maximumVertexMagnitude = Math.Max(
                        maximumVertexMagnitude, 2d * vertexMagnitude);
                }
                if (maximumVertexMagnitude >=
                    signedHandoffLimit - floatSafetyMargin)
                    return false;
            }
            return true;
        }

        internal static float HiddenPhoneObserverCompatibility(float responseSeconds)
        {
            // The checked model was fitted against one exact upstream observer.
            // A log-Gaussian support kernel is scale-symmetric and causes custom
            // response profiles to abstain instead of confidently evaluating a
            // differently phased trajectory. The default trained response is 1.
            if (!(responseSeconds > 0f) || float.IsNaN(responseSeconds) ||
                float.IsInfinity(responseSeconds)) return 0f;
            var logRatio = Mathf.Log(
                responseSeconds / AdvancedVisemeHiddenPhonePosterior.ObserverResponseSeconds);
            const float sigmaLog = 0.15f;
            return Mathf.Clamp01(Mathf.Exp(
                -0.5f * logRatio * logRatio / (sigmaLog * sigmaLog)));
        }

        private static string BuildEmpiricalFeatureSupport(
            MathGraph graph,
            BlendTree root,
            string feature,
            AdvancedVisemeVisibleTongueModelKind kind,
            int featureIndex,
            string key)
        {
            var safeBound = Mathf.Max(1e-6f,
                AdvancedVisemeVisibleTongueResidual.FeatureSafeBound(kind, featureIndex));
            var supported = Mathf.Clamp(
                AdvancedVisemeVisibleTongueResidual.FeatureAbsP995(kind, featureIndex),
                0f, safeBound);
            return BuildEmpiricalFeatureSupport(
                graph, root, feature, supported, safeBound, key);
        }

        private static string BuildEmpiricalFeatureSupport(
            MathGraph graph,
            BlendTree root,
            string feature,
            float supported,
            float safeBound,
            string key)
        {
            safeBound = Mathf.Max(1e-6f, safeBound);
            supported = Mathf.Clamp(supported, 0f, safeBound);
            if (safeBound - supported <= 1e-5f) return MathGraph.AlwaysOneParameter;

            var fadeEnd = Mathf.Min(
                safeBound,
                Mathf.Max(supported + 0.01f, supported * 1.5f));
            if (fadeEnd - supported <= 1e-5f) return MathGraph.AlwaysOneParameter;

            var magnitude = graph.Param(key + "/Magnitude", 0f);
            var confidence = graph.Param(key, 1f);
            graph.AddOperation(root, graph.Abs(feature, magnitude));
            var points = new List<(float input, float output)>
            {
                Point(0f, 1f), Point(supported, 1f),
                Point(fadeEnd, 0f)
            };
            if (safeBound - fadeEnd > 1e-5f) points.Add(Point(safeBound, 0f));
            graph.AddOperation(root, graph.Map(magnitude, confidence, points));
            return confidence;
        }

        private static string BuildSmoothedSupportConfidence(
            MathGraph graph,
            BlendTree root,
            string key,
            IReadOnlyList<string> factors,
            string alpha)
        {
            var factorArray = factors?.ToArray() ?? Array.Empty<string>();
            var raw = UseBalancedNeutralSupportReductionForTests
                ? MinUnitConfidenceBalanced(
                    graph, root, key + "/Raw", factorArray)
                : MinParameters(
                    graph, root, key + "/Raw", factorArray);
            var fast = graph.Param(key + "/Fast", 1f);
            var stable = graph.Param(key, 1f);
            AddTwoPoleObserver(
                graph, root, raw, fast, stable, alpha, false);
            return stable;
        }

        private static string MinUnitConfidenceBalanced(
            MathGraph graph,
            BlendTree root,
            string key,
            params string[] parameters)
        {
            if (parameters == null || parameters.Length == 0)
                return MathGraph.AlwaysOneParameter;
            if (parameters.Length == 1) return parameters[0];

            // min is associative and has identity 1 on confidence values. A
            // balanced reduction preserves the exact scalar result while cutting
            // Animator publication depth from n-1 to ceil(log2 n). Initializing
            // intermediates to the identity also makes certified-silence sleep
            // start from the correct optimistic support equilibrium instead of a
            // synthetic zero-confidence chain.
            var level = parameters.ToList();
            var depth = 0;
            while (level.Count > 1)
            {
                var nextLevel = new List<string>((level.Count + 1) / 2);
                for (var pair = 0; pair < level.Count; pair += 2)
                {
                    if (pair + 1 >= level.Count)
                    {
                        nextLevel.Add(level[pair]);
                        continue;
                    }

                    var next = graph.Param(
                        $"{key}/Balanced/{depth}/{pair / 2}", 1f);
                    graph.AddOperation(root, graph.Min(
                        level[pair], level[pair + 1], next));
                    nextLevel.Add(next);
                }
                level = nextLevel;
                depth++;
            }
            return level[0];
        }

        private static void AddTwoPoleObserver(
            MathGraph graph,
            BlendTree root,
            string target,
            string fast,
            string stable,
            string alpha,
            bool signed)
        {
            graph.AddOperation(root, graph.Smooth(target, fast, alpha, signed));
            graph.AddOperation(root, graph.Smooth(fast, stable, alpha, signed));
        }

        internal static (float input, float output)[] ScaledClampPoints(float scale)
        {
            scale = Mathf.Max(0f, scale);
            if (scale <= 1f)
            {
                return new[]
                {
                    Point(-1f, -scale), Point(0f, 0f), Point(1f, scale)
                };
            }

            var unitInput = 1f / scale;
            return new[]
            {
                Point(-1f, -1f), Point(-unitInput, -1f), Point(0f, 0f),
                Point(unitInput, 1f), Point(1f, 1f)
            };
        }

        private static string BuildSignedUnitValue(
            MathGraph graph, BlendTree root, string input, string key)
        {
            var output = graph.Param(key, 0.5f);
            graph.AddOperation(root, graph.Linear(output, new[]
            {
                Term.Constant(0.5f), Term.Signed(input, 0.5f)
            }));
            return output;
        }

        private static string BuildOpposedUnitValue(
            MathGraph graph, BlendTree root, string positive, string negative, string key)
        {
            var raw = graph.Param(key + "/Raw", 0f);
            var output = graph.Param(key, 0f);
            graph.AddOperation(root, graph.Linear(raw, new[]
            {
                Term.Positive(positive, 1f), Term.Positive(negative, -1f)
            }));
            graph.AddOperation(root, graph.Map(raw, output, new[]
            {
                Point(-1f, 0f), Point(0f, 0f), Point(1f, 1f), Point(2f, 1f)
            }));
            return output;
        }

        private static string BuildProtrusionValue(
            MathGraph graph,
            BlendTree root,
            string funnel,
            string pucker,
            string suck,
            string key)
        {
            var positive = MaxParameters(graph, root, key + "/Positive", funnel, pucker);
            return BuildOpposedUnitValue(graph, root, positive, suck, key);
        }

        private static string BuildHeadroomNormalizedResidual(
            MathGraph graph,
            BlendTree root,
            string tracked,
            string center,
            string key,
            out string oodConfidence)
        {
            var delta = graph.Param(key + "/Delta", 0f);
            graph.AddOperation(root, graph.Linear(delta, new[]
            {
                Term.Positive(tracked, 1f), Term.Positive(center, -1f)
            }));
            var positive = graph.Param(key + "/Positive", 0f);
            var negative = graph.Param(key + "/Negative", 0f);
            graph.AddOperation(root, graph.Map(delta, positive, new[]
            {
                Point(-2f, 0f), Point(0f, 0f), Point(2f, 2f)
            }));
            graph.AddOperation(root, graph.Map(delta, negative, new[]
            {
                Point(-2f, 2f), Point(0f, 0f), Point(2f, 0f)
            }));

            var reciprocalUpper = graph.Param(key + "/ReciprocalUpper", 1f);
            var reciprocalLower = graph.Param(key + "/ReciprocalLower", 1f);
            var headroomSamples = new[] { 0f, 0.075f, 0.125f, 0.25f, 0.5f, 0.75f, 0.875f, 0.925f, 1f };
            graph.AddOperation(root, graph.Map(center, reciprocalUpper,
                headroomSamples.Select(value => Point(value,
                    1f / Mathf.Max(1f - value, AdvancedVisemeVisibleTongueResidual.HeadroomFloor))).ToArray()));
            graph.AddOperation(root, graph.Map(center, reciprocalLower,
                headroomSamples.Select(value => Point(value,
                    1f / Mathf.Max(value, AdvancedVisemeVisibleTongueResidual.HeadroomFloor))).ToArray()));
            var positiveFraction = graph.Param(key + "/PositiveFraction", 0f);
            var negativeFraction = graph.Param(key + "/NegativeFraction", 0f);
            graph.AddOperation(root,
                graph.Multiply(positive, reciprocalUpper, positiveFraction, false));
            graph.AddOperation(root,
                graph.Multiply(negative, reciprocalLower, negativeFraction, false));
            var raw = graph.Param(key + "/Raw", 0f);
            graph.AddOperation(root, graph.Linear(raw, new[]
            {
                Term.Positive(positiveFraction, 1f), Term.Positive(negativeFraction, -1f)
            }));
            var magnitude = graph.Param(key + "/Magnitude", 0f);
            graph.AddOperation(root, graph.Abs(raw, magnitude));
            oodConfidence = graph.Param(key + "/OodConfidence", 1f);
            graph.AddOperation(root, graph.Map(magnitude, oodConfidence, new[]
            {
                Point(0f, 1f), Point(1f, 1f), Point(1.5f, 0f), Point(4f, 0f)
            }));
            var output = graph.Param(key + "/Clamped", 0f);
            graph.AddOperation(root, graph.Map(raw, output, new[]
            {
                Point(-4f, -1f), Point(-1f, -1f), Point(0f, 0f),
                Point(1f, 1f), Point(4f, 1f)
            }));
            return output;
        }

        private static string ApplyHeadroomResidual(
            MathGraph graph,
            BlendTree root,
            BlendTree alwaysRoot,
            string authority,
            string center,
            string residual,
            string confidence,
            bool signed,
            string key)
        {
            var positive = graph.Param(key + "/Positive", 0f);
            var negative = graph.Param(key + "/Negative", 0f);
            graph.AddOperation(root, graph.Map(residual, positive, new[]
            {
                Point(-1f, 0f), Point(0f, 0f), Point(1f, 1f)
            }));
            graph.AddOperation(root, graph.Map(residual, negative, new[]
            {
                Point(-1f, 1f), Point(0f, 0f), Point(1f, 0f)
            }));
            var upperHeadroom = graph.Param(key + "/UpperHeadroom", 1f);
            var lowerHeadroom = graph.Param(key + "/LowerHeadroom", signed ? 1f : 0f);
            graph.AddOperation(root, graph.Linear(upperHeadroom, new[]
            {
                Term.Constant(1f), Term.For(center, -1f, signed)
            }));
            graph.AddOperation(root, graph.Linear(lowerHeadroom, signed
                ? new[] { Term.Constant(1f), Term.Signed(center, 1f) }
                : new[] { Term.Positive(center, 1f) }));
            var positiveDelta = graph.Param(key + "/PositiveDelta", 0f);
            var negativeDelta = graph.Param(key + "/NegativeDelta", 0f);
            graph.AddOperation(root,
                graph.Multiply(positive, upperHeadroom, positiveDelta, false));
            graph.AddOperation(root,
                graph.Multiply(negative, lowerHeadroom, negativeDelta, false));
            var targetRaw = graph.Param(key + "/TargetRaw", 0f);
            graph.AddOperation(root, graph.Linear(targetRaw, new[]
            {
                Term.For(center, 1f, signed),
                Term.Positive(positiveDelta, 1f),
                Term.Positive(negativeDelta, -1f)
            }));
            var target = graph.Param(key + "/Target", 0f);
            graph.AddOperation(root, graph.Map(targetRaw, target, signed
                ? new[] { Point(-2f, -1f), Point(-1f, -1f), Point(0f, 0f), Point(1f, 1f), Point(2f, 1f) }
                : new[] { Point(-1f, 0f), Point(0f, 0f), Point(1f, 1f), Point(2f, 1f) }));
            var output = graph.Param(key + "/Output", 0f);
            var interpolation = graph.Interpolate(
                center, target, output, confidence, signed);
            if (string.Equals(
                    authority, MathGraph.AlwaysOneParameter,
                    StringComparison.Ordinal))
            {
                graph.AddOperation(root, interpolation);
            }
            else
            {
                // Publish the original output at its original AAP phase. The
                // authority selector owns the final interpolation motion itself;
                // an intermediate computed->authorized parameter would add a
                // frame and make the result frame-rate dependent.
                graph.AddOperation(alwaysRoot, graph.SelectMotion(
                    authority,
                    graph.Copy(center, output, signed),
                    interpolation,
                    key + " authority endpoint"));
            }
            return output;
        }

        private static string MinParameters(
            MathGraph graph, BlendTree root, string key, params string[] parameters)
        {
            if (parameters == null || parameters.Length == 0) return MathGraph.AlwaysOneParameter;
            var result = parameters[0];
            for (var i = 1; i < parameters.Length; i++)
            {
                var next = graph.Param(key + "/" + i, 0f);
                graph.AddOperation(root, graph.Min(result, parameters[i], next));
                result = next;
            }
            return result;
        }

        private static string MaxParameters(
            MathGraph graph, BlendTree root, string key, params string[] parameters)
        {
            if (parameters == null || parameters.Length == 0) return MathGraph.AlwaysOneParameter;
            var result = parameters[0];
            for (var i = 1; i < parameters.Length; i++)
            {
                var next = graph.Param(key + "/" + i, 0f);
                graph.AddOperation(root, graph.Max(result, parameters[i], next));
                result = next;
            }
            return result;
        }

        private static string Calibrate(
            MathGraph graph,
            BlendTree root,
            string input,
            ArticulatorRigBinding binding,
            AdvancedVisemeArticulator articulator,
            string stage,
            AdvancedVisemeExternalPose externalPose = null)
        {
            var calibrated = input;
            var templateNormalization = ExternalPoseNormalizationPoints(articulator, externalPose);
            if (templateNormalization != null)
            {
                // Tailored templates often reach their authored unit pose before
                // the semantic parameter reaches 1 (JawOpen commonly uses 0.8).
                // Reproduce that tree's linear coordinate system before applying
                // the user's profile calibration, so both the tracker endpoint and
                // the extracted pose remain mathematically identical.
                var normalized = graph.Param($"Tracking/{articulator}/{stage}TemplateNormalized", 0f);
                graph.AddOperation(root, graph.Map(input, normalized, templateNormalization));
                calibrated = normalized;
            }
            if (!Mathf.Approximately(binding.trackingScale, 1f) ||
                !Mathf.Approximately(binding.trackingOffset, 0f))
            {
                var profileCalibrationInput = calibrated;
                calibrated = graph.Param($"Tracking/{articulator}/{stage}CalibratedRaw", 0f);
                graph.AddOperation(root, graph.Linear(calibrated, new[]
                {
                    Term.For(profileCalibrationInput, binding.trackingScale, IsSigned(articulator)),
                    Term.Constant(binding.trackingOffset)
                }));
            }

            var output = graph.Param($"Tracking/{articulator}/{stage}Calibrated", 0f);
            graph.AddOperation(root, IsSigned(articulator)
                ? graph.Map(calibrated, output, new[]
                {
                    Point(-2f, -1f), Point(-1f, -1f), Point(0f, 0f), Point(1f, 1f), Point(2f, 1f)
                })
                : graph.Map(calibrated, output, new[]
                {
                    Point(-1f, 0f), Point(0f, 0f), Point(1f, 1f), Point(2f, 1f)
                }));
            return output;
        }

        internal static (float input, float output)[] ExternalPoseNormalizationPoints(
            AdvancedVisemeArticulator articulator,
            AdvancedVisemeExternalPose externalPose)
        {
            if (externalPose == null ||
                externalPose.positive == null && externalPose.negative == null)
                return null;
            var signed = IsSigned(articulator);
            if (!signed && externalPose.positive == null) return null;
            var needsCalibration = externalPose.positive == null ||
                                   !Mathf.Approximately(externalPose.positiveThreshold, 1f) ||
                                   signed && (externalPose.negative == null ||
                                              !Mathf.Approximately(
                                                  externalPose.negativeThreshold, -1f));
            if (!needsCalibration) return null;
            if (!signed)
                return new[]
                {
                    Point(0f, 0f), Point(externalPose.positiveThreshold, 1f)
                };
            if (externalPose.negative != null)
            {
                if (externalPose.positive == null)
                    return new[]
                    {
                        Point(externalPose.negativeThreshold, -1f),
                        Point(0f, 0f), Point(1f, 0f)
                    };
                return new[]
                {
                    Point(externalPose.negativeThreshold, -1f), Point(0f, 0f),
                    Point(externalPose.positiveThreshold, 1f)
                };
            }

            // A one-sided tailored tree explicitly defines negative values as
            // neutral, not as an inverse positive pose.
            return new[]
            {
                Point(-1f, 0f), Point(0f, 0f),
                Point(externalPose.positiveThreshold, 1f)
            };
        }

        private static Term[] BuildVisemeTerms(string[] inputs, float[] coefficients, bool signed)
        {
            var terms = new List<Term>();
            for (var i = 0; i < inputs.Length; i++)
            {
                if (Mathf.Abs(coefficients[i]) < 1e-6f) continue;
                terms.Add(Term.For(inputs[i], coefficients[i], signed || coefficients[i] < 0f));
            }
            if (terms.Count == 0) terms.Add(Term.Constant(0f));
            return terms.ToArray();
        }

        private static void AddVisemeMatrixProjection(
            MathGraph graph,
            BlendTree root,
            Request request,
            string name,
            IReadOnlyList<string> weights,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> outputs)
        {
            var projection = BuildVisemeMatrixProjectionMotion(
                graph, request, name, weights, outputs);
            if (projection != null) graph.AddOperation(root, projection);
        }

        private static Motion BuildVisemeMatrixProjectionMotion(
            MathGraph graph,
            Request request,
            string name,
            IReadOnlyList<string> weights,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> outputs)
        {
            if (weights == null || outputs == null || outputs.Count == 0) return null;
            if (weights.Count != VisemeReconstructionProfile.VisemeCount)
                throw new InvalidOperationException(
                    $"{name} expected {VisemeReconstructionProfile.VisemeCount} viseme weights, " +
                    $"but received {weights.Count}.");

            var ordered = outputs.OrderBy(pair => (int)pair.Key).ToArray();
            var coefficients = ordered.ToDictionary(
                pair => pair.Key,
                pair => GetAdjustedSpeechCoefficients(request, pair.Key));
            var projection = graph.Direct(name);
            var children = new List<ChildMotion>();
            for (var viseme = 0; viseme < weights.Count; viseme++)
            {
                var values = ordered
                    .Select(pair => new KeyValuePair<string, float>(
                        pair.Value, coefficients[pair.Key][viseme]))
                    .Where(pair => Mathf.Abs(pair.Value) >= 1e-8f)
                    .ToArray();
                if (values.Length == 0) continue;
                children.Add(new ChildMotion
                {
                    motion = graph.MultiSetter(
                        $"{name} from {VisemeReconstructionProfile.VisemeNames[viseme]}",
                        values),
                    directBlendParameter = weights[viseme],
                    timeScale = 1f
                });
            }
            children.Add(new ChildMotion
            {
                motion = graph.MultiSetter(
                    name + " safety zero",
                    ordered.Select(pair =>
                        new KeyValuePair<string, float>(pair.Value, 0f))),
                directBlendParameter = MathGraph.AlwaysOneParameter,
                timeScale = 1f
            });
            projection.children = children.ToArray();
            return projection;
        }

        private static void AddContractedBetaArticulationProjection(
            MathGraph graph,
            BlendTree root,
            Request request,
            AdvancedVisemeArticulatorGroup group,
            BetaCoarticulationGraph beta,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> projectedFast,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> projectedSlow,
            IReadOnlyDictionary<AdvancedVisemeArticulator, float> projectedOffsets,
            IReadOnlyDictionary<AdvancedVisemeArticulator, float> projectedScales,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> fastOutputs,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> slowOutputs,
            string visemeIndex,
            string speechHistory,
            string silenceStability)
        {
            if (!beta.leads.TryGetValue(group, out var lead))
                throw new InvalidOperationException(
                    $"Missing Beta coarticulation lead for {group}.");

            var projectedFastOutputs = fastOutputs
                .Where(pair => projectedFast.ContainsKey(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            var projectedSlowOutputs = slowOutputs
                .Where(pair => projectedSlow.ContainsKey(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            if (projectedFastOutputs.Count > 0)
            {
                // Beta is a continuous lead between the existing observer
                // stages. Never reintroduce the decoder's raw one-hot target:
                // that creates a visible pose step proportional to lead.
                var fastRelease = graph.DecodeAffineArticulationVector(
                    projectedFast, projectedFastOutputs,
                    projectedOffsets, projectedScales,
                    $"Corpus {group} projected fast");
                var fastFreeze = graph.CopyArticulationVector(
                    projectedFastOutputs, projectedFastOutputs,
                    $"Corpus {group} projected fast freeze");
                graph.AddOperation(root, graph.SelectSilenceHoldMotion(
                    fastRelease, fastRelease, fastFreeze,
                    visemeIndex, speechHistory, silenceStability,
                    $"Corpus {group} projected fast transient-sil hold"));
                graph.AddOperation(root, graph.InterpolateAffineArticulationVector(
                    projectedSlow, projectedFast, projectedSlowOutputs, lead,
                    projectedOffsets, projectedScales,
                    $"Corpus {group} projected slow"));
            }

            var matrixFastOutputs = fastOutputs
                .Where(pair => !projectedFast.ContainsKey(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            var matrixSlowOutputs = slowOutputs
                .Where(pair => !projectedSlow.ContainsKey(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            if (matrixFastOutputs.Count == 0) return;

            var matrixFastRelease = BuildVisemeMatrixProjectionMotion(
                graph, request, $"Corpus {group} sparse continuous fast",
                beta.fast, matrixFastOutputs);
            var matrixFastFreeze = graph.CopyArticulationVector(
                matrixFastOutputs, matrixFastOutputs,
                $"Corpus {group} sparse continuous fast freeze");
            graph.AddOperation(root, graph.SelectSilenceHoldMotion(
                matrixFastRelease, matrixFastRelease, matrixFastFreeze,
                visemeIndex, speechHistory, silenceStability,
                $"Corpus {group} sparse fast transient-sil hold"));

            var slowFrom = BuildVisemeMatrixProjectionMotion(
                graph, request, $"Corpus {group} sparse slow source",
                beta.slow, matrixSlowOutputs);
            var slowTo = BuildVisemeMatrixProjectionMotion(
                graph, request, $"Corpus {group} sparse fast-to-slow source",
                beta.fast, matrixSlowOutputs);
            graph.AddOperation(root, graph.InterpolateMotions(
                slowFrom, slowTo, lead,
                $"Corpus {group} sparse contracted slow"));
        }

        private static void AddElementwiseProductProjection(
            MathGraph graph,
            BlendTree root,
            string commonWeight,
            IReadOnlyList<string> inputs,
            IReadOnlyList<string> outputs,
            string name)
        {
            if (inputs == null || outputs == null || inputs.Count != outputs.Count)
                throw new InvalidOperationException(
                    $"{name} requires equally sized input and output vectors.");

            var vector = graph.Direct(name + " vector");
            var vectorChildren = new List<ChildMotion>();
            for (var i = 0; i < inputs.Count; i++)
            {
                vectorChildren.Add(new ChildMotion
                {
                    motion = graph.Setter(outputs[i], 1f),
                    directBlendParameter = inputs[i],
                    timeScale = 1f
                });
            }
            // The outer product's unconditional zero is the sole binder. The
            // inner vector is multiplied by commonWeight, so its own zero would
            // only add the redundant term commonWeight * 0.
            vector.children = vectorChildren.ToArray();

            var product = graph.Direct(name);
            product.children = new[]
            {
                new ChildMotion
                {
                    motion = vector,
                    directBlendParameter = commonWeight,
                    timeScale = 1f
                },
                new ChildMotion
                {
                    motion = graph.MultiSetter(
                        name + " safety zero",
                        outputs.Select(output =>
                            new KeyValuePair<string, float>(output, 0f))),
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                }
            };
            graph.AddOperation(root, product);
        }

        internal static float[] GetAuthoredSpeechCoefficients(
            Request request,
            AdvancedVisemeArticulator articulator)
        {
            var values = new float[VisemeReconstructionProfile.VisemeCount];
            if (HasExternalPoseCalibration(request))
            {
                var axes = request.calibration.poseBasisAxes;
                var hasMatchingAxis = axes.Any(axis => axis.articulator == articulator);
                if (!hasMatchingAxis)
                {
                    for (var viseme = 0; viseme < values.Length; viseme++)
                        values[viseme] = request.profile.visemePoses[viseme].Get(articulator);
                    return values;
                }
                for (var viseme = 0; viseme < values.Length; viseme++)
                {
                    var value = 0f;
                    for (var axis = 0; axis < axes.Length; axis++)
                    {
                        if (axes[axis].articulator != articulator) continue;
                        value += Mathf.Sign(axes[axis].direction) *
                                 request.calibration.coefficients[viseme, axis];
                    }
                    values[viseme] = value;
                }
                return values;
            }

            var basisIndex = -1;
            if (request.calibration != null && request.calibration.success && request.calibrationBasis != null)
            {
                for (var i = 0; i < request.calibrationBasis.Count; i++)
                {
                    if (request.calibrationBasis[i].articulator == articulator) { basisIndex = i; break; }
                }
            }
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = basisIndex >= 0
                    ? request.calibration.coefficients[i, basisIndex]
                    : request.profile.visemePoses[i].Get(articulator);
            }
            return values;
        }

        internal static float[] GetAdjustedSpeechCoefficients(
            Request request,
            AdvancedVisemeArticulator articulator)
        {
            var values = GetAuthoredSpeechCoefficients(request, articulator);
            if (request?.profile == null) return values;
            for (var viseme = 0; viseme < values.Length; viseme++)
                values[viseme] *= request.profile.GetVisemeArticulationMultiplier(
                    viseme, articulator);
            return values;
        }

        internal static bool ShouldProjectBetaArticulationRow(
            AdvancedVisemeArticulator articulator,
            IReadOnlyList<float> coefficients)
        {
            if (coefficients == null) return false;
            var signed = IsSigned(articulator);
            foreach (var value in coefficients)
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return false;
                // Observer coordinates use nonnegative Direct weights. Signed
                // rows are affine-normalized into [0,1] and decoded at output;
                // keep unusual hand-authored domains on the general matrix path.
                if ((!signed && value < 0f) || (signed && Mathf.Abs(value) > 2f))
                    return false;
            }
            var nonzero = coefficients.Count(value => Mathf.Abs(value) >= 1e-8f);
            if (signed && coefficients.Max() - coefficients.Min() < 1e-8f)
                return false;
            // Commuting C through the observer trades repeated 15-way matrix
            // samples for three stateful articulation coordinates. Signed lanes
            // need a two-sided copy map, so their profitable point is higher.
            // Keep sparse rows in the shared matrix clips and project only rows
            // safely beyond binding break-even; a merely equal curve count would
            // still add BlendTree traversal and internal parameters.
            return nonzero >= (signed ? 7 : 6);
        }

        internal static float[] EncodeBetaProjectionRow(
            AdvancedVisemeArticulator articulator,
            IReadOnlyList<float> coefficients,
            out float offset,
            out float scale)
        {
            if (!ShouldProjectBetaArticulationRow(articulator, coefficients))
                throw new InvalidOperationException(
                    $"{articulator} is not a safe dense Beta projection row.");

            var encoded = coefficients.ToArray();
            offset = 0f;
            scale = 1f;
            if (!IsSigned(articulator)) return encoded;

            offset = encoded.Min();
            scale = encoded.Max() - offset;
            for (var index = 0; index < encoded.Length; index++)
                encoded[index] = (encoded[index] - offset) / scale;
            return encoded;
        }

        private static bool IsArticulationLaneActive(
            Request request,
            AdvancedVisemeArticulator articulator,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> resolvedTracking,
            bool faceConditionedTongueInference)
        {
            // This is an exact liveness test, not an epsilon-based visual
            // approximation. A lane may be removed only when every possible
            // source in the generated graph is identically zero.
            if (GetAdjustedSpeechCoefficients(request, articulator)
                .Any(value => value != 0f))
                return true;
            if (resolvedTracking != null && resolvedTracking.ContainsKey(articulator))
                return true;
            if (faceConditionedTongueInference &&
                (articulator == AdvancedVisemeArticulator.TongueOut ||
                 articulator == AdvancedVisemeArticulator.TongueY))
                return true;

            if (request?.trackingEnabled == true &&
                request.component != null &&
                request.component.fusionMode == AdvancedVisemeFusionMode.PhoneticAssist &&
                request.profile != null)
            {
                if (articulator == AdvancedVisemeArticulator.LipClose &&
                    request.profile.bilabialClosure != 0f)
                    return true;
                if (articulator == AdvancedVisemeArticulator.LipBite &&
                    request.profile.labiodentalBite != 0f)
                    return true;
            }

            return false;
        }

        private static bool HasExternalPoseCalibration(Request request)
        {
            return request?.calibration != null && request.calibration.success &&
                   request.calibration.poseBasisAxes != null &&
                   request.calibration.poseBasisAxes.Length > 0 &&
                   request.calibration.coefficients != null;
        }

        private static bool UsesResidualOutputPath(Request request)
        {
            if (request == null || request.calibration == null ||
                !request.calibration.success || request.profile == null)
                return false;
            var anyVisemeOverride = request.profile.visemePoses.Any(pose =>
                pose != null && pose.animationOverride != null);
            var anyArticulatorOverride = request.profile.articulatorBindings.Any(binding =>
                binding != null &&
                (binding.animationOverride != null ||
                 binding.negativeAnimationOverride != null));
            return !anyVisemeOverride && !anyArticulatorOverride &&
                   (!request.reuseExistingTracking ||
                   HasExternalPoseCalibration(request));
        }

        private static bool UsesExternalCalibratedRayOutput(
            Request request,
            AdvancedVisemeArticulator articulator)
        {
            if (!HasExternalPoseCalibration(request)) return false;
            var axes = request.calibration.poseBasisAxes
                .Where(axis => axis.articulator == articulator)
                .ToArray();
            if (axes.Length == 0) return false;

            // This is the same fast path used by the output builder: a reused
            // native proxy drives its calibrated rays atomically, so no
            // generated tracking product or inverse gain is consumed.
            if (request.reuseExistingTracking &&
                request.directPoseArticulators != null &&
                request.directPoseArticulators.Contains(articulator) &&
                request.externalPoses != null &&
                request.externalPoses.TryGetValue(articulator, out var external) &&
                external != null &&
                TryResolveTrackingParameter(
                    request, articulator, request.profile.FindBinding(articulator),
                    out _))
                return false;

            var positiveCount = axes.Count(axis => axis.direction > 0);
            // One unsigned positive ray consumes the already-fused final
            // articulation directly. Only signed/two-ray reconciliation needs
            // separate generated speech and tracking magnitudes.
            return IsSigned(articulator) || positiveCount != 1;
        }

        private static BetaCoarticulationGraph BuildBetaCoarticulationWeights(
            MathGraph graph,
            BlendTree computeRoot,
            BlendTree publicationRoot,
            string strength,
            string frameTime,
            IReadOnlyList<string> raw,
            IReadOnlyList<string> fast,
            IReadOnlyList<string> slow,
            IReadOnlyDictionary<AdvancedVisemeArticulatorGroup, string[]>
                retentionRowTargets,
            string visemeIndex,
            string speechHistory,
            string silenceStability,
            bool materializeTongueSimplexes)
        {
            var output = new BetaCoarticulationGraph();
            var retentionParameters = new Dictionary<AdvancedVisemeArticulatorGroup, string>();
            for (var groupIndex = 0;
                 groupIndex < AdvancedVisemeTransitionRetention.GroupCount;
                 groupIndex++)
            {
                var group = (AdvancedVisemeArticulatorGroup)groupIndex;
                retentionParameters[group] = graph.Param(
                    $"BetaCoarticulation/Retention/{group}", 0f);
            }

            var groupsByDecay = retentionParameters.Keys
                .GroupBy(group => Mathf.RoundToInt(
                    AdvancedVisemeCoarticulationModel.DecaySeconds(group) * 1000000f))
                .OrderBy(grouping => grouping.Key);
            foreach (var decayGrouping in groupsByDecay)
            {
                var groups = decayGrouping.OrderBy(group => (int)group).ToArray();
                var decaySeconds = AdvancedVisemeCoarticulationModel.DecaySeconds(groups[0]);
                var contextAlpha = graph.Param(
                    $"BetaCoarticulation/Context/{decayGrouping.Key}/Alpha", 0.25f);
                graph.AddOperation(publicationRoot,
                    graph.AlphaFromDeltaTime(frameTime, contextAlpha, decaySeconds));
                var projected = groups.ToDictionary(
                    group => group,
                    group => Enumerable.Range(
                            0, VisemeReconstructionProfile.VisemeCount)
                        .Select(current => graph.Param(
                            $"BetaCoarticulation/RetentionProjected/{group}/{current}",
                            AdvancedVisemeCoarticulationModel.Retention(
                                group, 0, current)))
                        .ToArray());
                var projectedState = groups.ToDictionary(
                    group => group,
                    group => Enumerable.Range(
                            0, VisemeReconstructionProfile.VisemeCount)
                        .Select(current => graph.Param(
                            $"BetaCoarticulation/RetentionState/{group}/{current}",
                            AdvancedVisemeCoarticulationModel.Retention(
                                group, 0, current)))
                        .ToArray());
                foreach (var group in groups)
                {
                    for (var current = 0;
                         current < VisemeReconstructionProfile.VisemeCount;
                         current++)
                    {
                        var equilibrium =
                            AdvancedVisemeCoarticulationModel.Retention(
                                group, 0, current);
                        output.sleepEquilibrium.Add(
                            new KeyValuePair<string, float>(
                                projectedState[group][current], equilibrium));
                        output.sleepEquilibrium.Add(
                            new KeyValuePair<string, float>(
                                projected[group][current], equilibrium));
                    }
                    output.sleepEquilibrium.Add(
                        new KeyValuePair<string, float>(
                            retentionParameters[group],
                            AdvancedVisemeCoarticulationModel.Retention(
                                group, 0, 0)));
                }
                if (!UseSwitchedRetentionRowObserverForTests &&
                    groups.Any(group =>
                        retentionRowTargets == null ||
                        !retentionRowTargets.TryGetValue(group, out var targets) ||
                        targets == null ||
                        targets.Length != VisemeReconstructionProfile.VisemeCount))
                    throw new InvalidOperationException(
                        "Beta retention row targets are incomplete.");
                // Let c be the filtered hard-viseme context and R_g the learned
                // transition matrix. The previous implementation materialized c
                // and evaluated c^T R_g every frame. Linearity gives the exact
                // recurrence
                //   z_g = c^T R_g
                //   z'_g = lerp(z_g, R_g[Viseme,:], alpha).
                // Filtering the decoder's selected matrix row therefore removes
                // the dense context-matrix contraction without changing the
                // causal model, its silence hold, or any learned coefficient.
                var projectedStateVector = groups
                    .SelectMany(group => projectedState[group])
                    .ToArray();
                if (UseSwitchedRetentionRowObserverForTests)
                {
                    var selectedRows = Enumerable.Range(
                            0, VisemeReconstructionProfile.VisemeCount)
                        .Select(previous => groups
                            .SelectMany(group => Enumerable.Range(
                                    0, VisemeReconstructionProfile.VisemeCount)
                                .Select(current =>
                                    AdvancedVisemeCoarticulationModel.Retention(
                                        group, previous, current)))
                            .ToArray())
                        .ToArray();
                    graph.AddOperation(computeRoot,
                        graph.SmoothVectorTowardSelectedConstantsUnlessHeldSilence(
                            visemeIndex, raw, selectedRows, projectedStateVector,
                            contextAlpha, speechHistory, silenceStability,
                            $"Projected corpus switched context {decayGrouping.Key}"));
                }
                else
                {
                    var retentionTargets = groups
                        .SelectMany(group => retentionRowTargets[group])
                        .ToArray();
                    if (UseFactoredRetentionSilenceObserversForTests)
                    {
                        var factoredContextAlpha =
                            graph.RegisterSharedSilenceFactoredWeight(
                                contextAlpha,
                                "Retention" + decayGrouping.Key);
                        graph.AddOperation(computeRoot, graph.SmoothVector(
                            retentionTargets, projectedStateVector,
                            factoredContextAlpha,
                            $"Projected corpus factored context {decayGrouping.Key}"));
                    }
                    else
                    {
                        graph.AddOperation(computeRoot,
                            graph.SmoothVectorUnlessHeldSilence(
                                retentionTargets, projectedStateVector,
                                contextAlpha, visemeIndex, speechHistory,
                                silenceStability,
                                $"Projected corpus context {decayGrouping.Key}"));
                    }
                }
                // Preserve the existing Animator feedback phase. The previous
                // graph updated c, projected c^T R, and contracted against f as
                // sibling AAP operations, so the contraction observed a
                // one-frame-delayed projection. Keeping that explicit delay
                // makes the rewrite frame-for-frame equivalent while still
                // eliminating the dense matrix evaluation.
                graph.AddOperation(computeRoot, graph.CopyVector(
                    groups.SelectMany(group => projectedState[group]).ToArray(),
                    groups.SelectMany(group => projected[group]).ToArray(),
                    $"Delayed projected corpus context {decayGrouping.Key}"));

                var destinationContraction = graph.Direct(
                    $"Corpus destination contraction ({decaySeconds:0.###}s)");
                var destinationChildren = new List<ChildMotion>();
                for (var current = 0;
                     current < VisemeReconstructionProfile.VisemeCount;
                     current++)
                {
                    var vector = graph.Direct(
                        "Corpus projected destination " +
                        VisemeReconstructionProfile.VisemeNames[current]);
                    var vectorChildren = groups.Select(group => new ChildMotion
                    {
                        motion = graph.MultiSetter(
                            $"Projected {group} destination " +
                            VisemeReconstructionProfile.VisemeNames[current],
                            new[]
                            {
                                new KeyValuePair<string, float>(
                                    retentionParameters[group], 1f)
                            }),
                        directBlendParameter = projected[group][current],
                        timeScale = 1f
                    }).ToList();
                    // destinationContraction supplies one unconditional zero for
                    // the complete vector. This row is already weighted by a
                    // simplex coordinate, so a nested row zero is redundant.
                    vector.children = vectorChildren.ToArray();
                    destinationChildren.Add(new ChildMotion
                    {
                        motion = vector,
                        directBlendParameter = fast[current],
                        timeScale = 1f
                    });
                }
                destinationChildren.Add(new ChildMotion
                {
                    motion = graph.MultiSetter(
                        "Corpus destination contraction safety zero",
                        groups.Select(group => new KeyValuePair<string, float>(
                            retentionParameters[group], 0f))),
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                });
                destinationContraction.children = destinationChildren.ToArray();
                graph.AddOperation(computeRoot, destinationContraction);
            }

            output.fast = fast;
            output.slow = slow;
            var groupLeads = BuildBetaLeads(
                graph, publicationRoot, strength,
                retentionParameters, out var commonLead);
            output.common = BuildBetaStageWeights(
                graph, publicationRoot, commonLead, "Mean", raw, fast, slow,
                visemeIndex, speechHistory, silenceStability,
                materializeTongueSimplexes, out var phoneObservationFast);
            output.phoneObservationFast = phoneObservationFast;
            foreach (var pair in retentionParameters)
            {
                var lead = groupLeads[pair.Key];
                output.leads[pair.Key] = lead;
                if (!materializeTongueSimplexes ||
                    pair.Key != AdvancedVisemeArticulatorGroup.TongueTip &&
                    pair.Key != AdvancedVisemeArticulatorGroup.TongueBody)
                    continue;
                output.groups[pair.Key] = BuildBetaStageCoordinates(
                    graph, publicationRoot, lead, pair.Key, fast, slow,
                    visemeIndex, speechHistory, silenceStability);
            }
            return output;
        }

        private static Dictionary<AdvancedVisemeArticulatorGroup, string> BuildBetaLeads(
            MathGraph graph,
            BlendTree root,
            string strength,
            IReadOnlyDictionary<AdvancedVisemeArticulatorGroup, string> retentions,
            out string commonLead)
        {
            commonLead = graph.Param("BetaCoarticulation/Lead/Mean", 0f);
            var leads = retentions.Keys.ToDictionary(
                group => group,
                group => graph.Param($"BetaCoarticulation/Lead/{group}", 0f));
            var allLeads = leads.Values.Concat(new[] { commonLead }).ToArray();

            // All groups share one user strength. Evaluate
            //   lead_g = strength * (1 - retention_g)
            // and
            //   lead_mean = strength * (1 - mean_g retention_g)
            // as one nested vector motion. This removes five scalar remainder
            // parameters and an Animator-frame dependency without changing the
            // full-rank corpus table or its continuous contraction.
            var oneMinus = graph.Direct("Beta coarticulation one-minus retention vector");
            var oneMinusChildren = new List<ChildMotion>
            {
                new ChildMotion
                {
                    motion = graph.MultiSetter(
                        "Beta coarticulation lead unit vector",
                        allLeads.Select(lead =>
                            new KeyValuePair<string, float>(lead, 1f))),
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                }
            };
            foreach (var pair in retentions.OrderBy(pair => (int)pair.Key))
            {
                oneMinusChildren.Add(new ChildMotion
                {
                    motion = graph.MultiSetter(
                        $"Beta coarticulation {pair.Key} retention subtraction",
                        new[]
                        {
                            new KeyValuePair<string, float>(leads[pair.Key], -1f),
                            new KeyValuePair<string, float>(commonLead,
                                -1f / AdvancedVisemeTransitionRetention.GroupCount)
                        }),
                    directBlendParameter = pair.Value,
                    timeScale = 1f
                });
            }
            oneMinus.children = oneMinusChildren.ToArray();

            var scaled = graph.Direct("Beta coarticulation strength-scaled lead vector");
            scaled.children = new[]
            {
                new ChildMotion
                {
                    motion = oneMinus,
                    directBlendParameter = strength,
                    timeScale = 1f
                },
                new ChildMotion
                {
                    motion = graph.MultiSetter(
                        "Beta coarticulation lead safety zero",
                        allLeads.Select(lead =>
                            new KeyValuePair<string, float>(lead, 0f))),
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                }
            };
            graph.AddOperation(root, scaled);
            return leads;
        }

        private static BetaWeights BuildBetaStageWeights(
            MathGraph graph,
            BlendTree root,
            string lead,
            string key,
            IReadOnlyList<string> raw,
            IReadOnlyList<string> fast,
            IReadOnlyList<string> slow,
            string visemeIndex,
            string speechHistory,
            string silenceStability,
            bool materializePhoneObservation,
            out string[] phoneObservationFast)
        {
            phoneObservationFast = null;
            var output = new BetaWeights
            {
                fast = new string[VisemeReconstructionProfile.VisemeCount],
                slow = new string[VisemeReconstructionProfile.VisemeCount]
            };
            for (var i = 0; i < VisemeReconstructionProfile.VisemeCount; i++)
            {
                var defaultValue = i == 0 ? 1f : 0f;
                output.fast[i] = graph.Param($"BetaCoarticulation/{key}/Viseme/{i}/Fast", defaultValue);
                output.slow[i] = graph.Param($"BetaCoarticulation/{key}/Viseme/{i}/Slow", defaultValue);
            }
            // Preserve the existing publication epoch and silence freeze while
            // changing only the mathematical endpoint from raw to fast.
            graph.AddOperation(root, graph.CopyVectorUnlessHeldSilence(
                fast, output.fast,
                visemeIndex, speechHistory, silenceStability,
                $"BetaCoarticulation {key} visible fast"));

            if (materializePhoneObservation)
            {
                // The hidden-phone posterior was trained on this raw-advanced
                // feature. Keep it private and model-only; it must never be used
                // as a direct visible viseme or articulation interpolation endpoint.
                phoneObservationFast = new string[VisemeReconstructionProfile.VisemeCount];
                for (var i = 0; i < phoneObservationFast.Length; i++)
                {
                    phoneObservationFast[i] = graph.Param(
                        $"BetaCoarticulation/{key}/Viseme/{i}/PhoneObservationFast",
                        i == 0 ? 1f : 0f);
                }
                graph.AddOperation(root, graph.InterpolateVectorUnlessHeldSilence(
                    fast, raw, phoneObservationFast, lead,
                    visemeIndex, speechHistory, silenceStability,
                    $"BetaCoarticulation {key} phone observation fast"));
            }
            // Keep corpus lead private. Feeding it into the rendered simplex is
            // a phase-lead/high-frequency bypass around the persistent observer:
            // it makes categorical edges visible even when Snappiness is
            // zero. Copying the slow stage preserves the publication epoch and
            // simplex while leaving the trained phone observation above intact.
            graph.AddOperation(root, graph.CopyVector(
                slow, output.slow,
                $"BetaCoarticulation {key} persistent visible slow"));
            return output;
        }

        private static BetaWeights BuildBetaStageCoordinates(
            MathGraph graph,
            BlendTree root,
            string lead,
            AdvancedVisemeArticulatorGroup group,
            IReadOnlyList<string> fast,
            IReadOnlyList<string> slow,
            string visemeIndex,
            string speechHistory,
            string silenceStability)
        {
            var key = group.ToString();
            var output = new BetaWeights
            {
                fast = new string[VisemeReconstructionProfile.VisemeCount],
                slow = new string[VisemeReconstructionProfile.VisemeCount]
            };
            var pairCoordinates = new[] { 1, 8 }; // PP and nn
            var slowCoordinates = group == AdvancedVisemeArticulatorGroup.TongueTip
                ? Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount).ToArray()
                : pairCoordinates;

            foreach (var coordinate in pairCoordinates)
                output.fast[coordinate] = graph.Param(
                    $"BetaCoarticulation/{key}/Viseme/{coordinate}/Fast",
                    coordinate == 0 ? 1f : 0f);
            foreach (var coordinate in slowCoordinates)
                output.slow[coordinate] = graph.Param(
                    $"BetaCoarticulation/{key}/Viseme/{coordinate}/Slow",
                    coordinate == 0 ? 1f : 0f);

            graph.AddOperation(root, graph.CopyVectorUnlessHeldSilence(
                pairCoordinates.Select(index => fast[index]).ToArray(),
                pairCoordinates.Select(index => output.fast[index]).ToArray(),
                visemeIndex, speechHistory, silenceStability,
                $"BetaCoarticulation {key} observed fast"));
            graph.AddOperation(root, graph.InterpolateVector(
                slowCoordinates.Select(index => slow[index]).ToArray(),
                slowCoordinates.Select(index => fast[index]).ToArray(),
                slowCoordinates.Select(index => output.slow[index]).ToArray(),
                lead, $"BetaCoarticulation {key} observed slow"));
            return output;
        }

        private static void BuildMergedNasalCorrections(
            MathGraph graph,
            BlendTree root,
            BetaCoarticulationGraph beta,
            string mShareFast,
            string mShareSlow,
            string confidence,
            FacePhonePosteriorGraph output)
        {
            var groups = new[]
            {
                AdvancedVisemeArticulatorGroup.TongueTip,
                AdvancedVisemeArticulatorGroup.TongueBody
            };
            var shares = new[] { mShareFast, mShareSlow };
            var delta = new string[2, groups.Length];

            // Consumer-driven sum-product fusion. The former graph published
            // candidate, target, and raw-delta AAPs even though only the final
            // rank-one correction is observable:
            //   delta = confidence * (mShare * (PP + nn) - PP)
            // Keep the fast PP/nn evidence stateful (so transient silence still
            // freezes it), then evaluate this exact expression as two vectorized
            // nested motions with no intermediate Animator parameters.
            for (var stage = 0; stage < 2; stage++)
            {
                var stageName = stage == 0 ? "Fast" : "Slow";
                var outputs = new string[groups.Length];
                var sources = new BetaWeights[groups.Length];
                for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                {
                    var group = groups[groupIndex];
                    sources[groupIndex] = beta.groups[group];
                    outputs[groupIndex] = delta[stage, groupIndex] = graph.Param(
                        $"PhonePosterior/{group}/{stageName}/Delta", 0f);
                }

                var candidate = graph.Direct(
                    $"Hidden phone {stageName} PP-nn candidate vector");
                var candidateChildren = new List<ChildMotion>();
                for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                {
                    var source = stage == 0
                        ? sources[groupIndex].fast
                        : sources[groupIndex].slow;
                    candidateChildren.Add(new ChildMotion
                    {
                        motion = graph.Setter(outputs[groupIndex], 1f),
                        directBlendParameter = source[1],
                        timeScale = 1f
                    });
                    candidateChildren.Add(new ChildMotion
                    {
                        motion = graph.Setter(outputs[groupIndex], 1f),
                        directBlendParameter = source[8],
                        timeScale = 1f
                    });
                }
                candidateChildren.Add(new ChildMotion
                {
                    motion = graph.MultiSetter(
                        $"Hidden phone {stageName} candidate safety zero",
                        outputs.Select(parameter =>
                            new KeyValuePair<string, float>(parameter, 0f))),
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                });
                candidate.children = candidateChildren.ToArray();

                var centered = graph.Direct(
                    $"Hidden phone {stageName} posterior-centered vector");
                var centeredChildren = new List<ChildMotion>
                {
                    new ChildMotion
                    {
                        motion = candidate,
                        directBlendParameter = shares[stage],
                        timeScale = 1f
                    }
                };
                for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                {
                    var source = stage == 0
                        ? sources[groupIndex].fast
                        : sources[groupIndex].slow;
                    centeredChildren.Add(new ChildMotion
                    {
                        motion = graph.Setter(outputs[groupIndex], -1f),
                        directBlendParameter = source[1],
                        timeScale = 1f
                    });
                }
                centeredChildren.Add(new ChildMotion
                {
                    motion = graph.MultiSetter(
                        $"Hidden phone {stageName} centered safety zero",
                        outputs.Select(parameter =>
                            new KeyValuePair<string, float>(parameter, 0f))),
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                });
                centered.children = centeredChildren.ToArray();

                var weighted = graph.Direct(
                    $"Hidden phone {stageName} confidence-weighted vector");
                weighted.children = new[]
                {
                    new ChildMotion
                    {
                        motion = centered,
                        directBlendParameter = confidence,
                        timeScale = 1f
                    },
                    new ChildMotion
                    {
                        motion = graph.MultiSetter(
                            $"Hidden phone {stageName} correction safety zero",
                            outputs.Select(parameter =>
                                new KeyValuePair<string, float>(parameter, 0f))),
                        directBlendParameter = MathGraph.AlwaysOneParameter,
                        timeScale = 1f
                    }
                };
                graph.AddOperation(root, weighted);
            }

            for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                output.corrections[groups[groupIndex]] = new BetaNasalCorrection
                {
                    fast = delta[0, groupIndex],
                    slow = delta[1, groupIndex]
                };

        }

        private static void RebuildConditionedTongueSpeech(
            MathGraph graph,
            BlendTree alwaysRoot,
            BlendTree root,
            string authority,
            Request request,
            FacePhonePosteriorGraph posterior,
            string speechGain,
            IDictionary<AdvancedVisemeArticulator, string> speechFast,
            IDictionary<AdvancedVisemeArticulator, string> speechSlow)
        {
            var entries = new List<(AdvancedVisemeArticulator articulator, bool slow,
                string source, string output)>();
            foreach (var articulator in SynthesizedArticulators())
            {
                var group = AdvancedVisemeCoarticulationModel.GroupFor(articulator);
                if (!posterior.corrections.ContainsKey(group) ||
                    !speechFast.ContainsKey(articulator) ||
                    !speechSlow.ContainsKey(articulator)) continue;

                var conditionedFast = graph.Param(
                    $"PhonePosterior/Articulation/{articulator}/Fast", 0f);
                var conditionedSlow = graph.Param(
                    $"PhonePosterior/Articulation/{articulator}/Slow", 0f);
                entries.Add((articulator, false, speechFast[articulator], conditionedFast));
                entries.Add((articulator, true, speechSlow[articulator], conditionedSlow));
            }
            if (entries.Count == 0) return;

            var groups = new[]
            {
                AdvancedVisemeArticulatorGroup.TongueTip,
                AdvancedVisemeArticulatorGroup.TongueBody
            };
            var correctionInputs = new string[1, groups.Length * 2];
            var scaledCorrections = new string[1, groups.Length * 2];
            for (var stage = 0; stage < 2; stage++)
            for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                var index = stage * groups.Length + groupIndex;
                var correction = posterior.corrections[groups[groupIndex]];
                correctionInputs[0, index] = stage == 0 ? correction.fast : correction.slow;
                scaledCorrections[0, index] = graph.Param(
                    $"PhonePosterior/Articulation/{groups[groupIndex]}/" +
                    $"{(stage == 0 ? "Fast" : "Slow")}/ScaledDelta", 0f);
            }
            graph.AddOperation(root, graph.GroupedElementwiseProducts(
                new[] { speechGain }, correctionInputs, scaledCorrections,
                "Hidden phone speech-scaled rank-one deltas"));

            var projectionInputs = entries.Select(entry => entry.source).ToList();
            var flatScaledCorrections = new List<string>();
            for (var stage = 0; stage < 2; stage++)
            for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                flatScaledCorrections.Add(scaledCorrections[0, stage * groups.Length + groupIndex]);
            projectionInputs.AddRange(flatScaledCorrections);
            var conditionedProjection = graph.SignedMatrixProjection(
                projectionInputs,
                entries.Select(entry => entry.output).ToArray(),
                new float[entries.Count],
                (input, outputIndex) =>
                {
                    if (input < entries.Count) return input == outputIndex ? 1f : 0f;
                    var correctionIndex = input - entries.Count;
                    var correctionStage = correctionIndex / groups.Length;
                    var correctionGroup = groups[correctionIndex % groups.Length];
                    var entry = entries[outputIndex];
                    if ((entry.slow ? 1 : 0) != correctionStage ||
                        AdvancedVisemeCoarticulationModel.GroupFor(entry.articulator) !=
                        correctionGroup) return 0f;
                    var coefficients = GetAdjustedSpeechCoefficients(
                        request, entry.articulator);
                    return coefficients[1] - coefficients[8];
                },
                "Hidden phone rank-one tongue articulation correction");

            if (string.Equals(
                    authority, MathGraph.AlwaysOneParameter,
                    StringComparison.Ordinal))
            {
                graph.AddOperation(root, conditionedProjection);
            }
            else
            {
                // Select the endpoint motions, not endpoint parameters. This
                // keeps the conditioned articulation on the same AAP publication
                // epoch as the legacy graph while authority zero copies the exact
                // unconditioned speech vector.
                graph.AddOperation(alwaysRoot, graph.SelectMotion(
                    authority,
                    graph.CopyMixedVector(
                        entries.Select(entry => entry.source).ToArray(),
                        entries.Select(entry => entry.output).ToArray(),
                        entries.Select(entry => IsSigned(entry.articulator)).ToArray(),
                        "Hidden phone unconditioned tongue articulation fallback"),
                    conditionedProjection,
                    "Hidden phone tongue articulation authority endpoint"));
            }

            foreach (var entry in entries)
            {
                if (entry.slow) speechSlow[entry.articulator] = entry.output;
                else speechFast[entry.articulator] = entry.output;
            }
        }

        private static Motion BuildSparsePoseVector(
            MathGraph graph,
            IReadOnlyList<string> weights,
            IReadOnlyList<Motion> poses,
            string name)
        {
            if (weights == null || poses == null || weights.Count != poses.Count)
                throw new InvalidOperationException(
                    $"{name} requires equally sized weight and pose vectors.");
            var vector = graph.Direct(name);
            vector.children = poses.Select((pose, index) => pose == null
                    ? (ChildMotion?)null
                    : new ChildMotion
                    {
                        motion = graph.DriveSparsePose(
                            weights[index], pose,
                            AdvancedVisemeMath.SimplexCullingEpsilon),
                        directBlendParameter = MathGraph.AlwaysOneParameter,
                        timeScale = 1f
                    })
                .Where(child => child.HasValue)
                .Select(child => child.Value)
                .ToArray();
            return vector;
        }

        private static Motion BuildPhysicalVisemeVectorPose(
            MathGraph graph,
            IReadOnlyList<Motion> poses,
            IReadOnlyList<string> slowWeights,
            IReadOnlyList<string> fastWeights,
            string slowLead,
            string renderLead,
            string voiceGain,
            string detailGain,
            string name)
        {
            Motion rendered;
            if (slowWeights.SequenceEqual(fastWeights) &&
                string.IsNullOrEmpty(slowLead))
            {
                rendered = BuildSparsePoseVector(
                    graph, slowWeights, poses, name + " direct pose vector");
            }
            else
            {
                var fast = BuildSparsePoseVector(
                    graph, fastWeights, poses, name + " fast pose vector");
                Motion slow = BuildSparsePoseVector(
                    graph, slowWeights, poses, name + " slow pose vector");
                if (!string.IsNullOrEmpty(slowLead))
                    slow = graph.InterpolateMotions(
                        slow, fast, slowLead,
                        name + " coarticulated slow trajectory");
                rendered = graph.InterpolateMotions(
                    slow, fast, renderLead,
                    name + " continuous trajectory");
            }
            rendered = graph.ScaleMotion(
                voiceGain, rendered, name + " voice amplitude");
            if (!string.IsNullOrEmpty(detailGain))
                rendered = graph.ScaleMotion(
                    detailGain, rendered, name + " authored detail");
            return rendered;
        }

        private static Motion BuildSpeechArticulationPose(
            MathGraph graph,
            AdvancedVisemeArticulator articulator,
            Motion positive,
            Motion negative,
            string speechRenderLead,
            string voiceGain,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string>
                physicalSpeechArticulationFast,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string>
                physicalSpeechArticulationSlow,
            IReadOnlyCollection<AdvancedVisemeArticulator>
                physicalSpeechArticulationNeedsVoice,
            string fallback)
        {
            var signed = IsSigned(articulator);
            if (!physicalSpeechArticulationFast.TryGetValue(
                    articulator, out var fast) ||
                !physicalSpeechArticulationSlow.TryGetValue(
                    articulator, out var slow))
                return graph.DrivePoseAtThresholds(
                    fallback, positive, negative, 1f, -1f, signed);
            var speech = graph.InterpolateMotions(
                graph.DrivePoseAtThresholds(
                    slow, positive, negative, 1f, -1f, signed),
                graph.DrivePoseAtThresholds(
                    fast, positive, negative, 1f, -1f, signed),
                speechRenderLead,
                $"{articulator} continuous speech articulation");
            return physicalSpeechArticulationNeedsVoice.Contains(articulator)
                ? graph.ScaleMotion(
                    voiceGain, speech,
                    $"{articulator} direct speech voice amplitude")
                : speech;
        }

        private static Motion BuildPhysicalArticulationPose(
            Request request,
            MathGraph graph,
            AdvancedVisemeArticulator articulator,
            Motion positive,
            Motion negative,
            string speechRenderLead,
            string trackingBlend,
            string voiceGain,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string>
                physicalSpeechArticulationFast,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string>
                physicalSpeechArticulationSlow,
            IReadOnlyCollection<AdvancedVisemeArticulator>
                physicalSpeechArticulationNeedsVoice,
            string finalArticulation)
        {
            var speech = BuildSpeechArticulationPose(
                graph, articulator, positive, negative,
                speechRenderLead, voiceGain,
                physicalSpeechArticulationFast,
                physicalSpeechArticulationSlow,
                physicalSpeechArticulationNeedsVoice,
                finalArticulation);
            if (!request.trackingEnabled) return speech;
            var tracked = graph.DrivePoseAtThresholds(
                finalArticulation, positive, negative,
                1f, -1f, IsSigned(articulator));
            return graph.SelectMotion(
                trackingBlend,
                speech, PhysicalTrackingHandoffLow,
                tracked, PhysicalTrackingHandoffHigh,
                $"{articulator} speech/tracking render authority");
        }

        private static void BuildOutputTree(
            Request request,
            Result result,
            MathGraph graph,
            BlendTree outputRoot,
            string[] speechWeights,
            string[] visibleSpeechWeights,
            string[] directVisemeWeights,
            string voiceGain,
            string speechRenderLead,
            string trackingBlend,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string>
                physicalSpeechArticulationFast,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string>
                physicalSpeechArticulationSlow,
            IReadOnlyCollection<AdvancedVisemeArticulator>
                physicalSpeechArticulationNeedsVoice,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string>
                physicalFinalArticulation,
            string hiddenResidualSpeechDelta,
            string authoredDetail,
            string trackedSurfaceYield,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> trackingGains)
        {
            var externalPoseCalibration = HasExternalPoseCalibration(request);
            // Reused templates can contain a rig-specific linear mouth basis. Keep
            // that basis in the authoritative final layer instead of selecting a
            // separately auto-mapped calibration basis that may animate different
            // properties and leave the lower template visible.
            var useResiduals = UsesResidualOutputPath(request);

            if (request.trackingEnabled && request.reuseExistingTracking)
                ValidateReusedTrackingPoses(request, result);

            if (useResiduals)
            {
                if (externalPoseCalibration)
                {
                    BuildExternalCalibratedBasisOutput(
                        request, result, graph, outputRoot,
                        directVisemeWeights,
                        speechRenderLead, trackingBlend, voiceGain,
                        physicalSpeechArticulationFast,
                        physicalSpeechArticulationSlow,
                        physicalSpeechArticulationNeedsVoice,
                        physicalFinalArticulation);
                }
                else
                {
                    foreach (var pair in physicalFinalArticulation)
                    {
                        if (!request.resolvedBlendShapes.TryGetValue(pair.Key, out var shape) ||
                            string.IsNullOrEmpty(shape)) continue;
                        var positive = graph.BlendShapeClip(
                            request.rendererPath, shape, 100f);
                        Motion negative = null;
                        if (IsSigned(pair.Key))
                        {
                            var negativeShape = NegativeBasisShapeFor(
                                request.calibration, pair.Key, 1);
                            if (string.IsNullOrEmpty(negativeShape))
                                throw new InvalidOperationException(
                                    $"Calibrated signed articulator '{pair.Key}' has no " +
                                    "build-only inverse basis shape.");
                            negative = graph.BlendShapeClip(
                                request.rendererPath, negativeShape, 100f);
                        }
                        graph.AddOperation(outputRoot,
                            BuildPhysicalArticulationPose(
                                request, graph, pair.Key,
                                positive, negative,
                                speechRenderLead, trackingBlend,
                                voiceGain,
                                physicalSpeechArticulationFast,
                                physicalSpeechArticulationSlow,
                                physicalSpeechArticulationNeedsVoice,
                                pair.Value));
                    }
                }

                // The neutral calibrated identity is Vp = U(Cp) + Rp. A
                // per-viseme trim replaces C with C⊙M while deliberately leaving
                // complementary residual detail R untouched. R is always driven
                // in full first. Its measured component is then removed as the
                // low-rank basis correction U*diag(g)*A^T*p. This is exact at
                // tracking-off, yields independently per measured coordinate, and
                // needs at most two nonnegative ±geometry carriers per basis ray
                // instead of one conflict morph per viseme.
                var residualWeights = new string[speechWeights.Length];
                var residualPoses = new Motion[speechWeights.Length];
                for (var i = 0; i < speechWeights.Length; i++)
                {
                    residualWeights[i] = graph.Param($"Viseme/{i}/ResidualWeight", 0f);
                }
                AddElementwiseProductProjection(
                    graph, outputRoot, authoredDetail,
                    speechWeights, residualWeights,
                    "Authored residual simplex");
                for (var i = 0; i < speechWeights.Length; i++)
                {
                    var curves = new List<(string path, string blendShape, float value)>();
                    var residualName = request.calibration.residualBlendShapeNames[i];
                    if (!string.IsNullOrEmpty(residualName))
                        curves.Add((request.rendererPath, residualName, 100f));
                    if (request.linkedRendererOutputs != null)
                        foreach (var linked in request.linkedRendererOutputs)
                        {
                            var names = linked?.calibration?.residualBlendShapeNames;
                            var linkedName = names != null && i < names.Length
                                ? names[i]
                                : null;
                            if (linked?.calibration == null ||
                                !linked.calibration.success ||
                                linked.rendererPath == null ||
                                string.IsNullOrEmpty(linkedName)) continue;
                            curves.Add((linked.rendererPath, linkedName, 100f));
                        }
                    residualPoses[i] = graph.CompositeBlendShapeClip(
                        "Composite residual " +
                        VisemeReconstructionProfile.VisemeNames[i], curves);
                }
                var immediateResidual = BuildPhysicalVisemeVectorPose(
                    graph, residualPoses,
                    directVisemeWeights, directVisemeWeights,
                    null, MathGraph.AlwaysOneParameter,
                    voiceGain, authoredDetail,
                    "Direct authored residual simplex");
                var legacyResidual = graph.Direct(
                    "Legacy tracked authored residual simplex");
                legacyResidual.children = residualPoses
                    .Select((pose, index) => pose == null
                        ? (ChildMotion?)null
                        : new ChildMotion
                        {
                            motion = graph.DrivePose(
                                residualWeights[index], pose, false),
                            directBlendParameter = MathGraph.AlwaysOneParameter,
                            timeScale = 1f
                        })
                    .Where(child => child.HasValue)
                    .Select(child => child.Value)
                    .ToArray();
                graph.AddOperation(outputRoot,
                    request.trackingEnabled
                        ? graph.SelectMotion(
                            trackingBlend,
                            immediateResidual, PhysicalTrackingHandoffLow,
                            legacyResidual, PhysicalTrackingHandoffHigh,
                            "Residual tracking authority")
                        : immediateResidual);

                var sharedOwnershipGains = new Dictionary<AdvancedVisemeArticulator, string>();
                BuildLowRankOwnershipCorrection(
                    request.calibration,
                    request.rendererPath,
                    "Primary",
                    graph,
                    outputRoot,
                    residualWeights,
                    trackedSurfaceYield,
                    trackingGains,
                    sharedOwnershipGains);

                if (!string.IsNullOrEmpty(hiddenResidualSpeechDelta) &&
                    !string.IsNullOrEmpty(request.calibration.hiddenPhoneResidualBlendShapeName) &&
                    !string.IsNullOrEmpty(
                        request.calibration.hiddenPhoneResidualNegativeBlendShapeName))
                {
                    graph.AddOperation(outputRoot, graph.DrivePose(
                        hiddenResidualSpeechDelta,
                        graph.BlendShapeClip(
                            request.rendererPath,
                            request.calibration.hiddenPhoneResidualBlendShapeName,
                            100f),
                        graph.BlendShapeClip(
                            request.rendererPath,
                            request.calibration.hiddenPhoneResidualNegativeBlendShapeName,
                            100f),
                        true));
                }

                BuildLinkedRendererResidualOutputs(
                    request, result, graph, outputRoot, residualWeights,
                    hiddenResidualSpeechDelta, trackedSurfaceYield,
                    trackingGains, sharedOwnershipGains);
            }
            else
            {
                var fallbackPoses = new Motion[visibleSpeechWeights.Length];
                for (var i = 0; i < visibleSpeechWeights.Length; i++)
                {
                    var overrideClip = request.profile.visemePoses[i].animationOverride;
                    fallbackPoses[i] = overrideClip != null
                        ? graph.PoseClip(overrideClip, "Viseme " + VisemeReconstructionProfile.VisemeNames[i])
                        : graph.BlendShapeClip(request.rendererPath, request.sourceVisemeBlendShapes[i], 100f);
                }
                var immediateSpeech = BuildPhysicalVisemeVectorPose(
                    graph, fallbackPoses,
                    directVisemeWeights, directVisemeWeights,
                    null, MathGraph.AlwaysOneParameter,
                    voiceGain, null, "Fallback viseme simplex");
                var legacyTracked = graph.Direct(
                    "Legacy tracked fallback viseme simplex");
                legacyTracked.children = fallbackPoses
                    .Select((pose, index) => pose == null
                        ? (ChildMotion?)null
                        : new ChildMotion
                        {
                            motion = graph.DrivePose(
                                visibleSpeechWeights[index], pose, false),
                            directBlendParameter = MathGraph.AlwaysOneParameter,
                            timeScale = 1f
                        })
                    .Where(child => child.HasValue)
                    .Select(child => child.Value)
                    .ToArray();
                graph.AddOperation(outputRoot,
                    request.trackingEnabled
                        ? graph.SelectMotion(
                            trackingBlend,
                            immediateSpeech, PhysicalTrackingHandoffLow,
                            legacyTracked, PhysicalTrackingHandoffHigh,
                            "Fallback tracking authority")
                        : immediateSpeech);

                if (request.trackingEnabled)
                {
                    if (!request.reuseExistingTracking)
                    {
                        foreach (var pair in result.trackingContributionParameters)
                        {
                            var binding = request.profile.FindBinding(pair.Key);
                            Motion positive = null;
                            Motion negative = null;
                            if (binding != null && binding.animationOverride != null)
                            {
                                positive = graph.PoseClip(binding.animationOverride,
                                    "Articulation " + pair.Key);
                                if (binding.negativeAnimationOverride != null)
                                    negative = graph.PoseClip(binding.negativeAnimationOverride,
                                        "Articulation " + pair.Key + " Negative");
                            }
                            else if (request.resolvedBlendShapes.TryGetValue(pair.Key, out var shape) &&
                                     !string.IsNullOrEmpty(shape))
                            {
                                positive = graph.BlendShapeClip(request.rendererPath, shape, 100f);
                            }
                            if (positive != null || negative != null)
                                graph.AddOperation(outputRoot,
                                    graph.DrivePose(pair.Value, positive, negative, IsSigned(pair.Key)));
                        }
                    }
                }

                // Direct viseme clips contain coupled lower-face poses. Each one
                // has already yielded only to measurements in its own visible
                // support. Reconstruct the exact faded speech coordinate here,
                // then correct every available linear basis to the final fused
                // result. Beta needs the same correction even without tracking
                // because its articulator groups follow different trajectories.
                if (ShouldBuildFallbackArticulationCorrection(request))
                {
                    foreach (var pair in result.articulationParameters)
                    {
                        if (!result.speechArticulationParameters.ContainsKey(pair.Key)) continue;
                        var tongueTuningOnly = !request.trackingEnabled &&
                                               request.component.reconstructionMode ==
                                               AdvancedVisemeReconstructionMode.Normal;
                        if (tongueTuningOnly &&
                            !IsTunableTongueArticulator(pair.Key) &&
                            !request.profile.HasNonNeutralArticulationAdjustment(pair.Key))
                            continue;
                        var signed = IsSigned(pair.Key);
                        var coefficients = GetAuthoredSpeechCoefficients(
                            request, pair.Key);
                        var observerSpeechBase = graph.Param(
                            $"Fallback/{pair.Key}/ObserverSpeechBase", 0f);
                        graph.AddOperation(outputRoot, graph.Linear(
                            observerSpeechBase, BuildVisemeTerms(
                                visibleSpeechWeights, coefficients, signed)));
                        var directBaseRaw = graph.Param(
                            $"Fallback/{pair.Key}/DirectSpeechBaseRaw", 0f);
                        graph.AddOperation(outputRoot, graph.Linear(
                            directBaseRaw, BuildVisemeTerms(
                                directVisemeWeights, coefficients, signed)));
                        var directSpeechBase = graph.Param(
                            $"Fallback/{pair.Key}/DirectSpeechBase", 0f);
                        graph.AddOperation(outputRoot, graph.Multiply(
                            voiceGain, directBaseRaw, directSpeechBase, signed));
                        var speechBase = directSpeechBase;
                        if (request.trackingEnabled)
                        {
                            speechBase = graph.Param(
                                $"Fallback/{pair.Key}/SpeechBase", 0f);
                            graph.AddOperation(outputRoot, graph.SelectMotion(
                                trackingBlend,
                                graph.Copy(directSpeechBase, speechBase, signed),
                                PhysicalTrackingHandoffLow,
                                graph.Copy(observerSpeechBase, speechBase, signed),
                                PhysicalTrackingHandoffHigh,
                                $"Fallback {pair.Key} direct/tracked speech base"));
                        }
                        var renderedFinal = pair.Value;
                        if (physicalSpeechArticulationSlow.TryGetValue(
                                pair.Key, out var directFinalRaw))
                        {
                            var directFinal = directFinalRaw;
                            if (physicalSpeechArticulationNeedsVoice.Contains(pair.Key))
                            {
                                directFinal = graph.Param(
                                    $"Fallback/{pair.Key}/DirectFinal", 0f);
                                graph.AddOperation(outputRoot, graph.Multiply(
                                    voiceGain, directFinalRaw, directFinal, signed));
                            }
                            if (request.trackingEnabled)
                            {
                                renderedFinal = graph.Param(
                                    $"Fallback/{pair.Key}/RenderFinal", 0f);
                                graph.AddOperation(outputRoot, graph.SelectMotion(
                                    trackingBlend,
                                    graph.Copy(directFinal, renderedFinal, signed),
                                    PhysicalTrackingHandoffLow,
                                    graph.Copy(pair.Value, renderedFinal, signed),
                                    PhysicalTrackingHandoffHigh,
                                    $"Fallback {pair.Key} direct/tracked final"));
                            }
                            else
                            {
                                renderedFinal = directFinal;
                            }
                        }
                        string trackingContribution = null;
                        if (ShouldSubtractGeneratedTrackingContribution(request.reuseExistingTracking) &&
                            result.trackingContributionParameters.TryGetValue(
                                pair.Key, out var generatedTrackingContribution))
                            trackingContribution = generatedTrackingContribution;

                        Motion positive = null;
                        Motion negative = null;
                        if (request.reuseExistingTracking && request.externalPoses != null &&
                            request.externalPoses.TryGetValue(pair.Key, out var external))
                        {
                            positive = graph.TargetRendererBlendShapePose(external.positive,
                                "Correction " + pair.Key, request.rendererPath, request.targetMesh);
                            negative = graph.TargetRendererBlendShapePose(external.negative,
                                "Correction " + pair.Key + " Negative", request.rendererPath, request.targetMesh);
                        }
                        if (positive == null && negative == null)
                        {
                            var binding = request.profile.FindBinding(pair.Key);
                            if (binding != null && binding.animationOverride != null)
                            {
                                // Corrections may be negative. Filter overrides down
                                // to target-renderer blendshape deltas so absolute
                                // transforms, materials, and other non-linear curves
                                // are never treated as an invertible linear basis.
                                positive = graph.TargetRendererBlendShapePose(
                                    binding.animationOverride, "Correction " + pair.Key,
                                    request.rendererPath, request.targetMesh);
                                if (binding.negativeAnimationOverride != null)
                                    negative = graph.TargetRendererBlendShapePose(
                                        binding.negativeAnimationOverride,
                                        "Correction " + pair.Key + " Negative",
                                        request.rendererPath, request.targetMesh);
                            }
                            if (positive == null && negative == null &&
                                request.resolvedBlendShapes.TryGetValue(pair.Key, out var shape) &&
                                !string.IsNullOrEmpty(shape))
                            {
                                positive = graph.BlendShapeClip(request.rendererPath, shape, 100f);
                            }
                        }
                        if (positive == null && negative == null) continue;

                        // A signed coordinate with distinct positive/negative
                        // poses is a pair of rays, not one linear axis. Subtract
                        // each ray independently so crossing Smile->Sad (or a
                        // lateral axis) cannot leave both shapes visible.
                        if (signed && positive != null && negative != null)
                        {
                            AddSignedRayCorrection(
                                graph, outputRoot, pair.Key.ToString(), renderedFinal,
                                speechBase, trackingContribution, positive, negative);
                            continue;
                        }

                        var terms = new List<Term>
                        {
                            Term.For(renderedFinal, 1f, signed),
                            Term.For(speechBase, -1f, signed)
                        };
                        // Fresh inputs are driven above as g*f and must be removed
                        // from this correction. Reused tracking is already present
                        // only in a lower Override layer; the later generated layer
                        // replaces it, so this correction supplies the complete
                        // authoritative final-minus-speech-basis pose.
                        if (!string.IsNullOrEmpty(trackingContribution))
                            terms.Add(Term.For(trackingContribution, -1f, signed));
                        var correction = graph.Param($"Fallback/{pair.Key}/ArticulationCorrection", 0f);
                        graph.AddOperation(outputRoot, graph.Linear(correction, terms));
                        graph.AddOperation(outputRoot,
                            graph.DrivePose(correction, positive, negative, true));
                    }
                }
            }
        }

        private static void BuildLinkedRendererResidualOutputs(
            Request request,
            Result result,
            MathGraph graph,
            BlendTree outputRoot,
            string[] residualWeights,
            string hiddenResidualSpeechDelta,
            string trackedSurfaceYield,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> trackingGains,
            IDictionary<AdvancedVisemeArticulator, string> sharedOwnershipGains)
        {
            if (request.linkedRendererOutputs == null ||
                request.linkedRendererOutputs.Count == 0) return;

            for (var linkedIndex = 0;
                 linkedIndex < request.linkedRendererOutputs.Count;
                 linkedIndex++)
            {
                var linked = request.linkedRendererOutputs[linkedIndex];
                var calibration = linked?.calibration;
                if (calibration == null || !calibration.success ||
                    linked.rendererPath == null) continue;

                var key = $"LinkedRenderer/{linkedIndex}";
                BuildLowRankOwnershipCorrection(
                    calibration,
                    linked.rendererPath,
                    key,
                    graph,
                    outputRoot,
                    residualWeights,
                    trackedSurfaceYield,
                    trackingGains,
                    sharedOwnershipGains);

                // VRCFury copies the original positive basis curves. A signed
                // negative coordinate cannot be copied as a negative weight,
                // because VRChat clamps the target shape at zero. Drive the
                // target-local -U basis clone with a nonnegative magnitude.
                if (!HasExternalPoseCalibration(request))
                {
                    var basisNames = calibration.basisNegativeBlendShapeNames;
                    var articulators = calibration.basisArticulators;
                    var directions = calibration.basisDirections;
                    if (basisNames != null && articulators != null)
                    {
                        for (var column = 0; column < basisNames.Length; column++)
                        {
                            if (string.IsNullOrEmpty(basisNames[column]) ||
                                column >= articulators.Length ||
                                !IsSigned(articulators[column]) ||
                                directions != null && column < directions.Length &&
                                directions[column] < 0 ||
                                !result.articulationParameters.TryGetValue(
                                    articulators[column], out var articulation))
                                continue;
                            SplitSignedMagnitude(
                                graph, outputRoot,
                                $"{key}/Basis/{column}/Signed",
                                articulation, out _, out var negativeMagnitude);
                            graph.AddOperation(outputRoot, graph.DrivePose(
                                negativeMagnitude,
                                graph.BlendShapeClip(
                                    linked.rendererPath, basisNames[column], 100f),
                                false));
                        }
                    }
                }

                if (string.IsNullOrEmpty(hiddenResidualSpeechDelta) ||
                    string.IsNullOrEmpty(
                        calibration.hiddenPhoneResidualBlendShapeName) ||
                    string.IsNullOrEmpty(
                        calibration.hiddenPhoneResidualNegativeBlendShapeName)) continue;
                graph.AddOperation(outputRoot, graph.DrivePose(
                    hiddenResidualSpeechDelta,
                    graph.BlendShapeClip(
                        linked.rendererPath,
                        calibration.hiddenPhoneResidualBlendShapeName,
                        100f),
                    graph.BlendShapeClip(
                        linked.rendererPath,
                        calibration.hiddenPhoneResidualNegativeBlendShapeName,
                        100f),
                    true));
            }
        }

        private static string NegativeBasisShapeFor(
            AdvancedVisemeMeshCalibrator.Result calibration,
            AdvancedVisemeArticulator articulator,
            int direction)
        {
            var names = calibration?.basisNegativeBlendShapeNames;
            var articulators = calibration?.basisArticulators;
            var directions = calibration?.basisDirections;
            if (names == null || articulators == null) return null;
            for (var column = 0; column < names.Length &&
                                 column < articulators.Length; column++)
            {
                if (articulators[column] != articulator ||
                    directions != null && column < directions.Length &&
                    Math.Sign(directions[column]) != Math.Sign(direction))
                    continue;
                if (!string.IsNullOrEmpty(names[column])) return names[column];
            }
            return null;
        }

        private static void BuildLowRankOwnershipCorrection(
            AdvancedVisemeMeshCalibrator.Result calibration,
            string rendererPath,
            string key,
            MathGraph graph,
            BlendTree outputRoot,
            IReadOnlyList<string> residualWeights,
            string trackedSurfaceYield,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> trackingGains,
            IDictionary<AdvancedVisemeArticulator, string> sharedOwnershipGains)
        {
            var coefficients = calibration?.ownershipProjectionCoefficients;
            var positiveCarriers = calibration?.ownershipCarrierBlendShapeNames;
            var positiveCarrierScales = calibration?.ownershipCarrierScales;
            var negativeCarriers = calibration?.ownershipNegativeCarrierBlendShapeNames;
            var negativeCarrierScales = calibration?.ownershipNegativeCarrierScales;
            var nonZeroSelectedColumns = calibration?.ownershipNonZeroSelectedColumns;
            var authorityGroups = calibration?.ownershipAuthorityGroups;
            var articulators = calibration?.basisArticulators;
            if (coefficients == null || positiveCarriers == null ||
                positiveCarrierScales == null || negativeCarriers == null ||
                negativeCarrierScales == null ||
                articulators == null ||
                coefficients.GetLength(0) != residualWeights.Count ||
                coefficients.GetLength(1) != positiveCarriers.Length ||
                positiveCarrierScales.Length != positiveCarriers.Length ||
                negativeCarriers.Length != positiveCarriers.Length ||
                negativeCarrierScales.Length != positiveCarriers.Length ||
                articulators.Length != positiveCarriers.Length || rendererPath == null)
                return;

            var dependencyGains = new Dictionary<string, string>(StringComparer.Ordinal);
            var carrierProjections = new List<OwnershipCarrierProjection>();

            for (var column = 0; column < positiveCarriers.Length; column++)
            {
                if (string.IsNullOrEmpty(positiveCarriers[column]) &&
                    string.IsNullOrEmpty(negativeCarriers[column])) continue;
                var articulator = articulators[column];
                if (!trackingGains.TryGetValue(articulator, out var trackingGain)) continue;

                var participantColumns = authorityGroups != null &&
                                         authorityGroups.GetLength(0) == articulators.Length &&
                                         authorityGroups.GetLength(1) == articulators.Length
                    ? Enumerable.Range(0, articulators.Length)
                        .Where(candidate => authorityGroups[column, candidate])
                        .ToArray()
                    : calibration.ownershipBasisRankDeficient
                        ? Enumerable.Range(0, articulators.Length)
                            .Where(candidate => nonZeroSelectedColumns != null &&
                                                candidate < nonZeroSelectedColumns.Length &&
                                                nonZeroSelectedColumns[candidate])
                            .ToArray()
                        : new[] { column };
                if (participantColumns.Length == 0) participantColumns = new[] { column };
                var participantArticulators = participantColumns
                    .Select(candidate => articulators[candidate])
                    .Distinct()
                    .OrderBy(candidate => (int)candidate)
                    .ToArray();
                if (participantArticulators.Any(candidate =>
                        !trackingGains.ContainsKey(candidate)))
                    continue;

                string effectiveGain;
                if (participantArticulators.Length > 1)
                {
                    var signature = string.Join("_", participantArticulators
                        .Select(candidate => ((int)candidate).ToString()));
                    if (!dependencyGains.TryGetValue(signature, out effectiveGain))
                    {
                        var conservative = MinParameters(
                            graph, outputRoot,
                            $"{key}/Dependency/{signature}/Authority",
                            participantArticulators
                                .Select(candidate => trackingGains[candidate])
                                .ToArray());
                        effectiveGain = graph.Param(
                            $"{key}/Dependency/{signature}/Yield", 0f);
                        graph.AddOperation(outputRoot, graph.Multiply(
                            conservative, trackedSurfaceYield,
                            effectiveGain, false));
                        dependencyGains[signature] = effectiveGain;
                    }
                }
                else
                {
                    if (!sharedOwnershipGains.TryGetValue(articulator, out effectiveGain))
                    {
                        effectiveGain = graph.Param(
                            $"Residual/Ownership/{articulator}/Yield", 0f);
                        graph.AddOperation(outputRoot, graph.Multiply(
                            trackingGain, trackedSurfaceYield,
                            effectiveGain, false));
                        sharedOwnershipGains[articulator] = effectiveGain;
                    }
                }

                AddOwnershipCarrierProjection(
                    carrierProjections, residualWeights, coefficients, column,
                    positiveCarriers[column], positiveCarrierScales[column],
                    effectiveGain, $"{key}/Ownership/{column}/Add",
                    coefficient => coefficient < 0f ? -coefficient : 0f);
                AddOwnershipCarrierProjection(
                    carrierProjections, residualWeights, coefficients, column,
                    negativeCarriers[column], negativeCarrierScales[column],
                    effectiveGain, $"{key}/Ownership/{column}/Subtract",
                    coefficient => coefficient > 0f ? coefficient : 0f);
            }

            BuildOwnershipCarrierProjection(
                graph, outputRoot, residualWeights, carrierProjections,
                rendererPath, key);
        }

        private sealed class OwnershipCarrierProjection
        {
            public string carrier;
            public string effectiveGain;
            public string key;
            public float[] coefficients;
        }

        private static void AddOwnershipCarrierProjection(
            ICollection<OwnershipCarrierProjection> projections,
            IReadOnlyList<string> residualWeights,
            float[,] coefficients,
            int column,
            string carrier,
            float carrierScale,
            string effectiveGain,
            string key,
            Func<float, float> contributionMagnitude)
        {
            if (string.IsNullOrEmpty(carrier) ||
                float.IsNaN(carrierScale) || float.IsInfinity(carrierScale) ||
                carrierScale <= 1e-7f) return;

            var projectedCoefficients = new float[residualWeights.Count];
            var any = false;
            for (var viseme = 0; viseme < residualWeights.Count; viseme++)
            {
                var magnitude = contributionMagnitude(coefficients[viseme, column]);
                if (magnitude <= 1e-7f) continue;
                projectedCoefficients[viseme] = magnitude / carrierScale;
                any = true;
            }
            if (!any) return;

            projections.Add(new OwnershipCarrierProjection
            {
                carrier = carrier,
                effectiveGain = effectiveGain,
                key = key,
                coefficients = projectedCoefficients
            });
        }

        private static void BuildOwnershipCarrierProjection(
            MathGraph graph,
            BlendTree outputRoot,
            IReadOnlyList<string> residualWeights,
            IReadOnlyList<OwnershipCarrierProjection> projections,
            string rendererPath,
            string key)
        {
            if (projections == null || projections.Count == 0) return;

            var projected = projections
                .Select(projection => graph.Param(projection.key + "/Projected", 0f))
                .ToArray();
            var matrix = graph.Direct(key + " ownership matrix projection");
            var children = new List<ChildMotion>(residualWeights.Count + 1);
            for (var viseme = 0; viseme < residualWeights.Count; viseme++)
            {
                var values = projections.Select((projection, index) =>
                        new KeyValuePair<string, float>(
                            projected[index], projection.coefficients[viseme]))
                    .Where(pair => pair.Value != 0f)
                    .ToArray();
                if (values.Length == 0) continue;
                children.Add(new ChildMotion
                {
                    motion = graph.MultiSetter(
                        $"{key} ownership from " +
                        VisemeReconstructionProfile.VisemeNames[viseme],
                        values),
                    directBlendParameter = residualWeights[viseme],
                    timeScale = 1f
                });
            }
            children.Add(new ChildMotion
            {
                motion = graph.MultiSetter(
                    key + " ownership safety zero",
                    projected.Select(parameter =>
                        new KeyValuePair<string, float>(parameter, 0f))),
                directBlendParameter = MathGraph.AlwaysOneParameter,
                timeScale = 1f
            });
            matrix.children = children.ToArray();
            graph.AddOperation(outputRoot, matrix);

            for (var index = 0; index < projections.Count; index++)
                graph.AddOperation(outputRoot, graph.DrivePoseProduct(
                    projections[index].effectiveGain,
                    projected[index],
                    graph.BlendShapeClip(
                        rendererPath, projections[index].carrier, 100f),
                    projections[index].key));
        }

        private static void BuildExternalCalibratedBasisOutput(
            Request request,
            Result result,
            MathGraph graph,
            BlendTree outputRoot,
            string[] speechWeights,
            string speechRenderLead,
            string trackingBlend,
            string voiceGain,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string>
                physicalSpeechArticulationFast,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string>
                physicalSpeechArticulationSlow,
            IReadOnlyCollection<AdvancedVisemeArticulator>
                physicalSpeechArticulationNeedsVoice,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string>
                physicalFinalArticulation)
        {
            var axes = request.calibration.poseBasisAxes;
            var indexedAxes = axes
                .Select((axis, index) =>
                    new KeyValuePair<int, AdvancedVisemeMeshCalibrator.PoseBasisAxis>(index, axis))
                .GroupBy(pair => pair.Value.articulator);

            foreach (var group in indexedAxes)
            {
                var articulator = group.Key;
                if (!physicalFinalArticulation.TryGetValue(
                        articulator, out var finalArticulation))
                    continue;

                var positiveAxes = group.Where(pair => pair.Value.direction > 0).ToArray();
                var negativeAxes = group.Where(pair => pair.Value.direction < 0).ToArray();
                if (!IsSigned(articulator) && negativeAxes.Length > 0)
                    throw new InvalidOperationException(
                        $"Calibrated external articulator '{articulator}' has a negative pose ray, " +
                        "but the articulator is unsigned.");
                if (positiveAxes.Length > 1 || negativeAxes.Length > 1)
                    throw new InvalidOperationException(
                        $"Calibrated external articulator '{articulator}' contains multiple " +
                        "pose rays in the same direction. Clamp-safe ownership requires at most " +
                        "one positive and one negative endpoint per channel.");

                var poses = new Dictionary<int, Motion>();
                foreach (var pair in group)
                {
                    var axis = pair.Value;
                    if (!IsEntireLinearCorrectionClip(axis.clip, axis.rendererPath, request.targetMesh))
                        throw new InvalidOperationException(
                            $"Calibrated external pose '{axis.clip?.name}' for '{articulator}' is no longer " +
                            "a complete target-face blendshape pose. Rebuild the avatar calibration.");
                    var pose = graph.TargetRendererBlendShapePose(
                        axis.clip,
                        $"Calibrated {articulator} {(axis.direction > 0 ? "Positive" : "Negative")}",
                        axis.rendererPath,
                        request.targetMesh);
                    if (pose == null)
                        throw new InvalidOperationException(
                            $"Calibrated external pose '{axis.clip?.name}' for '{articulator}' has no driveable curves.");
                    poses[pair.Key] = pose;
                }

                // A rig-connected controller-only proxy is already the value the
                // template uses to render this exact pose. Let that parameter
                // reach the mesh atomically while tracking is active. The legacy
                // observer/fusion value remains the tracking-off speech fallback
                // and public diagnostic, but it is no longer read back through a
                // long animated-parameter pipeline to render local face motion.
                if (request.reuseExistingTracking &&
                    request.directPoseArticulators != null &&
                    request.directPoseArticulators.Contains(articulator) &&
                    request.externalPoses != null &&
                    request.externalPoses.TryGetValue(articulator, out var externalPose) &&
                    result.trackingGainParameters.TryGetValue(
                        articulator, out var directTrackingGain) &&
                    TryResolveTrackingParameter(
                        request, articulator,
                        request.profile.FindBinding(articulator),
                        out var directTrackingParameter))
                {
                    var positive = positiveAxes.Length == 1
                        ? poses[positiveAxes[0].Key]
                        : null;
                    var negative = negativeAxes.Length == 1
                        ? poses[negativeAxes[0].Key]
                        : null;
                    // Calibrated template rays may deliberately be one-sided
                    // (JawForward/JawZ is a common example). In that case the
                    // missing direction means neutral geometry, not an inverse
                    // blendshape. Use the same explicit ray sampler for the
                    // fused fallback so a negative value never becomes a
                    // negative final blendshape weight.
                    var fallback = graph.DrivePoseAtThresholds(
                        finalArticulation, positive, negative,
                        1f, -1f, IsSigned(articulator));
                    var native = graph.DrivePoseAtThresholds(
                        directTrackingParameter, positive, negative,
                        externalPose.positiveThreshold,
                        externalPose.negativeThreshold,
                        IsSigned(articulator));
                    var tracked = graph.SelectMotion(
                        directTrackingGain,
                        fallback, native,
                        $"Native {articulator} tracking gate");
                    var speech = BuildSpeechArticulationPose(
                        graph, articulator, positive, negative,
                        speechRenderLead, voiceGain,
                        physicalSpeechArticulationFast,
                        physicalSpeechArticulationSlow,
                        physicalSpeechArticulationNeedsVoice,
                        finalArticulation);
                    Motion selected = graph.SelectMotion(
                        trackingBlend,
                        speech, PhysicalTrackingHandoffLow,
                        tracked, PhysicalTrackingHandoffHigh,
                        $"Native {articulator} speech/tracking authority");
                    graph.AddOperation(outputRoot, selected);
                    continue;
                }

                // The normal tailored-template case has one non-negative positive
                // ray for an unsigned coordinate, so its fused coordinate is
                // already the exact coefficient required by that pose.
                if (!IsSigned(articulator) && positiveAxes.Length == 1)
                {
                    graph.AddOperation(outputRoot,
                        BuildPhysicalArticulationPose(
                            request, graph, articulator,
                            poses[positiveAxes[0].Key], null,
                            speechRenderLead, trackingBlend,
                            voiceGain,
                            physicalSpeechArticulationFast,
                            physicalSpeechArticulationSlow,
                            physicalSpeechArticulationNeedsVoice,
                            finalArticulation));
                    continue;
                }

                var rayFinalArticulation = finalArticulation;
                if (physicalSpeechArticulationSlow.TryGetValue(
                        articulator, out var directRayArticulation))
                {
                    if (physicalSpeechArticulationNeedsVoice.Contains(articulator))
                    {
                        var scaledDirect = graph.Param(
                            $"ExternalBasis/{articulator}/DirectSpeech", 0f);
                        graph.AddOperation(outputRoot, graph.Multiply(
                            voiceGain, directRayArticulation, scaledDirect,
                            IsSigned(articulator)));
                        directRayArticulation = scaledDirect;
                    }
                    if (request.trackingEnabled)
                    {
                        rayFinalArticulation = graph.Param(
                            $"ExternalBasis/{articulator}/RenderFinal", 0f);
                        graph.AddOperation(outputRoot, graph.SelectMotion(
                            trackingBlend,
                            graph.Copy(
                                directRayArticulation,
                                rayFinalArticulation,
                                IsSigned(articulator)),
                            PhysicalTrackingHandoffLow,
                            graph.Copy(
                                finalArticulation,
                                rayFinalArticulation,
                                IsSigned(articulator)),
                            PhysicalTrackingHandoffHigh,
                            $"External {articulator} direct/tracked final"));
                    }
                    else
                    {
                        rayFinalArticulation = directRayArticulation;
                    }
                }
                BuildExternalCalibratedRays(
                    request, result, graph, outputRoot, speechWeights,
                    voiceGain,
                    articulator, rayFinalArticulation,
                    positiveAxes, negativeAxes, poses);
            }
        }

        private static void BuildExternalCalibratedRays(
            Request request,
            Result result,
            MathGraph graph,
            BlendTree outputRoot,
            string[] speechWeights,
            string speechWeightGain,
            AdvancedVisemeArticulator articulator,
            string finalArticulation,
            IReadOnlyList<KeyValuePair<int, AdvancedVisemeMeshCalibrator.PoseBasisAxis>> positiveAxes,
            IReadOnlyList<KeyValuePair<int, AdvancedVisemeMeshCalibrator.PoseBasisAxis>> negativeAxes,
            IReadOnlyDictionary<int, Motion> poses)
        {
            // The multi-ray realization is algebraically
            //   (1-g) * authoredRayMass + g * trackedRayMass.
            // Unlike the unsigned one-ray fast path, both factors are required.
            // Fail at build time if the liveness plan and output lowering ever
            // disagree instead of silently falling back to additive speech.
            if (result.trackingGainParameters.ContainsKey(articulator) &&
                (!result.inverseTrackingGainParameters.ContainsKey(articulator) ||
                 !result.trackingContributionParameters.ContainsKey(articulator)))
                throw new InvalidOperationException(
                    $"External calibrated rays for '{articulator}' require both " +
                    "inverse speech authority and tracked contribution terms.");

            var positiveBase = BuildExternalRayBases(
                request, result, graph, outputRoot, speechWeights,
                speechWeightGain,
                articulator, "Positive", positiveAxes);
            var negativeBase = BuildExternalRayBases(
                request, result, graph, outputRoot, speechWeights,
                speechWeightGain,
                articulator, "Negative", negativeAxes);

            string trackingPositive = null;
            string trackingNegative = null;
            if (result.trackingContributionParameters.TryGetValue(
                    articulator, out var trackingContribution))
            {
                SplitSignedMagnitude(
                    graph, outputRoot, $"ExternalBasis/{articulator}/Tracking",
                    trackingContribution, out trackingPositive, out trackingNegative);
            }
            AddExternalTrackingRay(
                graph, outputRoot, positiveBase, trackingPositive,
                $"ExternalBasis/{articulator}/Positive/WithTracking");
            AddExternalTrackingRay(
                graph, outputRoot, negativeBase, trackingNegative,
                $"ExternalBasis/{articulator}/Negative/WithTracking");

            var positiveTotal = SumExternalRayBases(
                graph, outputRoot, $"ExternalBasis/{articulator}/PositiveBaseTotal", positiveBase);
            var negativeTotal = SumExternalRayBases(
                graph, outputRoot, $"ExternalBasis/{articulator}/NegativeBaseTotal", negativeBase);
            SplitSignedMagnitude(
                graph, outputRoot, $"ExternalBasis/{articulator}/Final",
                finalArticulation, out var finalPositive, out var finalNegative);

            // NNLS can legitimately use both signed rays at once. Preserve their
            // shared non-negative mass, then replace only their differential with
            // the final constrained articulation. This makes g=0 reproduce every
            // adjusted C⊙M ray (and the authored C ray when trims are neutral),
            // while g=1 reproduces the tracked coordinate.
            string common = null;
            if (positiveBase.Count > 0 && negativeBase.Count > 0)
                common = MinParameters(
                    graph, outputRoot, $"ExternalBasis/{articulator}/CommonRayMass",
                    positiveTotal, negativeTotal);
            ReconcileExternalRayDirection(
                graph, outputRoot, articulator, "Positive", positiveBase,
                common, finalPositive, poses);
            ReconcileExternalRayDirection(
                graph, outputRoot, articulator, "Negative", negativeBase,
                common, finalNegative, poses);
        }

        private static List<KeyValuePair<int, string>> BuildExternalRayBases(
            Request request,
            Result result,
            MathGraph graph,
            BlendTree outputRoot,
            string[] speechWeights,
            string speechWeightGain,
            AdvancedVisemeArticulator articulator,
            string direction,
            IReadOnlyList<KeyValuePair<int, AdvancedVisemeMeshCalibrator.PoseBasisAxis>> axes)
        {
            var bases = new List<KeyValuePair<int, string>>(axes.Count);
            foreach (var pair in axes)
            {
                var coefficients = new float[VisemeReconstructionProfile.VisemeCount];
                for (var viseme = 0; viseme < coefficients.Length; viseme++)
                    coefficients[viseme] = Mathf.Max(
                        0f, request.calibration.coefficients[viseme, pair.Key]) *
                        request.profile.GetVisemeArticulationMultiplier(
                            viseme, articulator);
                var rawMass = graph.Param(
                    $"ExternalBasis/{articulator}/{direction}/{pair.Key}/RawSpeechMass", 0f);
                graph.AddOperation(outputRoot, graph.Linear(
                    rawMass, BuildVisemeTerms(speechWeights, coefficients, false)));
                var mass = rawMass;
                if (!string.IsNullOrEmpty(speechWeightGain))
                {
                    mass = graph.Param(
                        $"ExternalBasis/{articulator}/{direction}/{pair.Key}/SpeechMass", 0f);
                    graph.AddOperation(outputRoot, graph.Multiply(
                        speechWeightGain, rawMass, mass, false));
                }

                var speechPart = mass;
                if (result.inverseTrackingGainParameters.TryGetValue(
                        articulator, out var inverseGain))
                {
                    speechPart = graph.Param(
                        $"ExternalBasis/{articulator}/{direction}/{pair.Key}/SpeechPart", 0f);
                    graph.AddOperation(outputRoot,
                        graph.Multiply(inverseGain, mass, speechPart, false));
                }
                bases.Add(new KeyValuePair<int, string>(pair.Key, speechPart));
            }
            return bases;
        }

        private static void AddExternalTrackingRay(
            MathGraph graph,
            BlendTree outputRoot,
            IList<KeyValuePair<int, string>> bases,
            string trackingRay,
            string key)
        {
            if (bases.Count == 0 || string.IsNullOrEmpty(trackingRay)) return;
            var first = bases[0];
            var fused = graph.Param(key, 0f);
            graph.AddOperation(outputRoot, graph.Linear(fused, new[]
            {
                Term.Positive(first.Value, 1f), Term.Positive(trackingRay, 1f)
            }));
            bases[0] = new KeyValuePair<int, string>(first.Key, fused);
        }

        private static string SumExternalRayBases(
            MathGraph graph,
            BlendTree outputRoot,
            string key,
            IReadOnlyList<KeyValuePair<int, string>> bases)
        {
            if (bases.Count == 0) return null;
            if (bases.Count == 1) return bases[0].Value;
            var sum = graph.Param(key, 0f);
            graph.AddOperation(outputRoot, graph.Linear(
                sum, bases.Select(pair => Term.Positive(pair.Value, 1f))));
            return sum;
        }

        private static void ReconcileExternalRayDirection(
            MathGraph graph,
            BlendTree outputRoot,
            AdvancedVisemeArticulator articulator,
            string direction,
            IReadOnlyList<KeyValuePair<int, string>> bases,
            string common,
            string finalMagnitude,
            IReadOnlyDictionary<int, Motion> poses)
        {
            if (bases.Count == 0) return;
            var targetRaw = finalMagnitude;
            if (!string.IsNullOrEmpty(common))
            {
                targetRaw = graph.Param(
                    $"ExternalBasis/{articulator}/{direction}/TargetMass", 0f);
                graph.AddOperation(outputRoot, graph.Linear(targetRaw, new[]
                {
                    Term.Positive(common, 1f), Term.Positive(finalMagnitude, 1f)
                }));
            }

            // The common coarticulation mass and the reconciled signed magnitude
            // are independently non-negative, but their sum can transiently
            // exceed one while tracking authority changes. Project the final ray
            // coordinate into the usable blendshape interval before it can reach
            // a tailored pose. This keeps every downstream pose drive compatible
            // with VRChat/VRCFury's final 0..100 blendshape clamp.
            var target = graph.Param(
                $"ExternalBasis/{articulator}/{direction}/TargetClamped", 0f);
            graph.AddOperation(outputRoot, graph.Map(targetRaw, target, new[]
            {
                Point(-1f, 0f), Point(0f, 0f), Point(1f, 1f), Point(2f, 1f)
            }));

            // There is normally one ray in each direction. If a future template
            // supplies several, retain every secondary speech ray and put the
            // aggregate reconciliation on the first ray; tracking-off remains an
            // exact reconstruction of every calibrated column.
            var total = SumExternalRayBases(
                graph, outputRoot, $"ExternalBasis/{articulator}/{direction}/BaseTotal", bases);
            var correction = graph.Param(
                $"ExternalBasis/{articulator}/{direction}/Reconciliation", 0f);
            graph.AddOperation(outputRoot, graph.Linear(correction, new[]
            {
                Term.Positive(target, 1f), Term.Positive(total, -1f)
            }));
            for (var i = 0; i < bases.Count; i++)
            {
                var weight = bases[i].Value;
                if (i == 0)
                {
                    weight = graph.Param(
                        $"ExternalBasis/{articulator}/{direction}/PrimaryWeight", 0f);
                    graph.AddOperation(outputRoot, graph.Linear(weight, new[]
                    {
                        Term.Positive(bases[i].Value, 1f), Term.Signed(correction, 1f)
                    }));
                }
                graph.AddOperation(outputRoot,
                    graph.DrivePose(weight, poses[bases[i].Key], false));
            }
        }

        private static void AddSignedRayCorrection(
            MathGraph graph,
            BlendTree root,
            string key,
            string final,
            string speechBase,
            string trackingContribution,
            Motion positivePose,
            Motion negativePose)
        {
            SplitSignedMagnitude(graph, root, "Fallback/" + key + "/Final", final,
                out var finalPositive, out var finalNegative);
            SplitSignedMagnitude(graph, root, "Fallback/" + key + "/Speech", speechBase,
                out var speechPositive, out var speechNegative);

            string trackingPositive = null;
            string trackingNegative = null;
            if (!string.IsNullOrEmpty(trackingContribution))
                SplitSignedMagnitude(
                    graph, root, "Fallback/" + key + "/Tracking", trackingContribution,
                    out trackingPositive, out trackingNegative);

            var positiveTerms = new List<Term>
            {
                Term.Positive(finalPositive, 1f),
                Term.Positive(speechPositive, -1f)
            };
            var negativeTerms = new List<Term>
            {
                Term.Positive(finalNegative, 1f),
                Term.Positive(speechNegative, -1f)
            };
            if (!string.IsNullOrEmpty(trackingPositive))
            {
                positiveTerms.Add(Term.Positive(trackingPositive, -1f));
                negativeTerms.Add(Term.Positive(trackingNegative, -1f));
            }

            var positiveCorrection = graph.Param(
                $"Fallback/{key}/PositiveRayCorrection", 0f);
            var negativeCorrection = graph.Param(
                $"Fallback/{key}/NegativeRayCorrection", 0f);
            graph.AddOperation(root, graph.Linear(positiveCorrection, positiveTerms));
            graph.AddOperation(root, graph.Linear(negativeCorrection, negativeTerms));
            graph.AddOperation(root,
                graph.DrivePose(positiveCorrection, positivePose, true));
            graph.AddOperation(root,
                graph.DrivePose(negativeCorrection, negativePose, true));
        }

        private static void SplitSignedMagnitude(
            MathGraph graph,
            BlendTree root,
            string key,
            string input,
            out string positive,
            out string negative)
        {
            positive = graph.Param(key + "/Positive", 0f);
            negative = graph.Param(key + "/Negative", 0f);
            graph.AddOperation(root, graph.Map(input, positive, new[]
            {
                Point(-2f, 0f), Point(0f, 0f), Point(2f, 2f)
            }));
            graph.AddOperation(root, graph.Map(input, negative, new[]
            {
                Point(-2f, 2f), Point(0f, 0f), Point(2f, 0f)
            }));
        }

        internal static bool ShouldBuildFallbackArticulationCorrection(Request request)
        {
            return request != null &&
                   (request.trackingEnabled ||
                    request.component != null && request.component.reconstructionMode ==
                    AdvancedVisemeReconstructionMode.BetaCoarticulation ||
                    request.component != null && request.component.createTuningMenu &&
                    (request.component.tuningMenuSections &
                     AdvancedVisemeTuningMenuSections.Tongue) != 0 ||
                    HasNonNeutralTongueStrength(request.profile) ||
                    request.profile != null &&
                    request.profile.HasNonNeutralVisemeAdjustments());
        }

        private static bool HasNonNeutralTongueStrength(VisemeReconstructionProfile profile)
        {
            if (profile == null) return false;
            return !Mathf.Approximately(profile.tongueOutStrength, 1f) ||
                   !Mathf.Approximately(profile.tongueYStrength, 1f) ||
                   !Mathf.Approximately(profile.tongueXStrength, 1f) ||
                   !Mathf.Approximately(profile.tongueRollStrength, 1f) ||
                   !Mathf.Approximately(profile.tongueArchStrength, 1f) ||
                   !Mathf.Approximately(profile.tongueShapeStrength, 1f) ||
                   !Mathf.Approximately(profile.tongueTwistStrength, 1f);
        }

        private static bool IsTunableTongueArticulator(
            AdvancedVisemeArticulator articulator)
        {
            return articulator == AdvancedVisemeArticulator.TongueOut ||
                   articulator == AdvancedVisemeArticulator.TongueY ||
                   articulator == AdvancedVisemeArticulator.TongueX ||
                   articulator == AdvancedVisemeArticulator.TongueRoll ||
                   articulator == AdvancedVisemeArticulator.TongueArchY ||
                   articulator == AdvancedVisemeArticulator.TongueShape ||
                   articulator == AdvancedVisemeArticulator.TongueTwistRight ||
                   articulator == AdvancedVisemeArticulator.TongueTwistLeft;
        }

        internal static bool ShouldSubtractGeneratedTrackingContribution(bool reuseExistingTracking)
        {
            return !reuseExistingTracking;
        }

        private static void ValidateReusedTrackingPoses(Request request, Result result)
        {
            foreach (var articulator in result.trackingContributionParameters.Keys)
            {
                if (request.externalPoses == null ||
                    !request.externalPoses.TryGetValue(articulator, out var pose) || pose == null ||
                    pose.positive == null && pose.negative == null)
                {
                    // A partial or highly tailored template is valid. BuildOutputTree
                    // first tries the profile's explicit clip/blendshape mapping; if
                    // none exists it emits no curve for this channel, allowing the
                    // installed lower tracking layer to keep ownership of that rig
                    // property instead of fabricating a generic pose.
                    continue;
                }

                if (pose.positive != null &&
                    !IsEntireLinearCorrectionClip(pose.positive, request.rendererPath, request.targetMesh) ||
                    pose.negative != null &&
                    !IsEntireLinearCorrectionClip(pose.negative, request.rendererPath, request.targetMesh))
                    throw new InvalidOperationException(
                        $"Existing tracking channel '{articulator}' animates a bone, material, another renderer, " +
                        "or a blendshape absent from the selected face mesh. Owning reuse requires the entire " +
                        "sampled pose to be target-face blendshape curves; use Outputs Only for this template.");
            }
        }

        private static IEnumerable<VRCExpressionParameters.Parameter> BuildExpressionParameters(Request request, Result result)
        {
            var names = request.existingExpressionParameters != null
                ? new HashSet<string>(request.existingExpressionParameters)
                : new HashSet<string>();
            if (request.trackingEnabled)
            {
                if (!request.reuseExistingTracking)
                {
                    foreach (var articulator in TrackedArticulators(request.effectiveTrackingInputs))
                    {
                        var binding = request.profile.FindBinding(articulator);
                        if (binding == null || string.IsNullOrWhiteSpace(binding.trackingParameter)) continue;
                        var name = TrackingParameterName(request.trackingPrefix, binding.trackingParameter);
                        if (UsesBinaryTracking(request))
                        {
                            foreach (var bitName in BinaryParameterNames(
                                         name, articulator, request.component.trackingEncoding))
                            {
                                if (!names.Add(bitName)) continue;
                                yield return ExpressionParameter(
                                    bitName, VRCExpressionParameters.ValueType.Bool, 0f);
                            }
                        }
                        else if (names.Add(name))
                        {
                            yield return ExpressionParameter(
                                name, VRCExpressionParameters.ValueType.Float, 0f);
                        }
                    }
                }
                var activeParameter = string.IsNullOrEmpty(request.trackingActiveParameter)
                    ? "LipTrackingActive"
                    : request.trackingActiveParameter;
                var autoReuseOnly = request.component.trackingInputs ==
                                    AdvancedVisemeTrackingInputs.Auto;
                if (!autoReuseOnly && names.Add(activeParameter))
                    yield return ExpressionParameter(
                        activeParameter, VRCExpressionParameters.ValueType.Bool, 0f);
                if (!autoReuseOnly && request.component.createFaceTrackingToggle &&
                    !string.IsNullOrEmpty(result.manualTrackingParameter) &&
                    names.Add(result.manualTrackingParameter))
                    yield return ExpressionParameter(
                        result.manualTrackingParameter,
                        VRCExpressionParameters.ValueType.Bool, 1f);
            }

            foreach (var pair in result.tuningParameters.OrderBy(pair => (int)pair.Key))
            {
                if (!names.Add(pair.Value)) continue;
                yield return ExpressionParameter(
                    pair.Value,
                    VRCExpressionParameters.ValueType.Float,
                    AdvancedVisemeTuning.DefaultValue(request.profile, pair.Key),
                    request.component.saveTuningValues,
                    request.useSharedParameterCompressor);
            }

            if (!string.IsNullOrEmpty(result.tuningSyncFocusParameter) &&
                names.Add(result.tuningSyncFocusParameter))
                yield return ExpressionParameter(
                    result.tuningSyncFocusParameter,
                    VRCExpressionParameters.ValueType.Int,
                    0f, false, false);

            if (!string.IsNullOrEmpty(result.tuningSyncDataParameter) &&
                names.Add(result.tuningSyncDataParameter))
                yield return ExpressionParameter(
                    result.tuningSyncDataParameter,
                    VRCExpressionParameters.ValueType.Int,
                    0f, false, true);

            foreach (var indexParameter in result.tuningSyncIndexParameters)
            {
                if (!names.Add(indexParameter)) continue;
                yield return ExpressionParameter(
                    indexParameter,
                    VRCExpressionParameters.ValueType.Bool,
                    0f, false, true);
            }
        }

        private static VRCExpressionParameters.Parameter ExpressionParameter(
            string name,
            VRCExpressionParameters.ValueType type,
            float defaultValue,
            bool saved = false,
            bool networkSynced = true)
        {
            return new VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = type,
                defaultValue = defaultValue,
                saved = saved,
                networkSynced = networkSynced
            };
        }

        private static string TrackingParameterName(string prefix, string suffix)
        {
            prefix = (prefix ?? string.Empty).Trim().Trim('/');
            suffix = (suffix ?? string.Empty).Trim().Trim('/');
            return string.IsNullOrEmpty(prefix) ? "v2/" + suffix : prefix + "/v2/" + suffix;
        }

        private static bool UsesBinaryTracking(Request request)
        {
            return request.trackingEnabled &&
                   !request.reuseExistingTracking &&
                   request.component.trackingEncoding != AdvancedVisemeTrackingEncoding.FullFloat;
        }

        private static IEnumerable<string> BinaryParameterNames(
            string baseName,
            AdvancedVisemeArticulator articulator,
            AdvancedVisemeTrackingEncoding encoding)
        {
            var bitCount = AdvancedVisemeMath.TrackingMagnitudeBits(articulator, encoding);
            for (var bit = 0; bit < bitCount; bit++) yield return baseName + (1 << bit);
            if (AdvancedVisemeMath.IsSignedTrackingArticulator(articulator)) yield return baseName + "Negative";
        }

        private static string DecodeBinaryTracking(
            MathGraph graph,
            BlendTree root,
            string baseName,
            AdvancedVisemeArticulator articulator,
            bool signed,
            AdvancedVisemeTrackingEncoding encoding)
        {
            var bitCount = AdvancedVisemeMath.TrackingMagnitudeBits(articulator, encoding);
            var maximum = (1 << bitCount) - 1f;
            var terms = new List<Term>(bitCount);
            for (var bit = 0; bit < bitCount; bit++)
            {
                var bitName = baseName + (1 << bit);
                // The Expression Parameter is Bool; a Float Animator parameter intentionally
                // uses VRChat's documented Bool-to-Float type conversion.
                graph.AddParameter(bitName, AnimatorControllerParameterType.Float, 0f);
                terms.Add(Term.Positive(bitName, (1 << bit) / maximum));
            }

            var magnitude = graph.Param($"Tracking/{articulator}/BinaryMagnitude", 0f);
            graph.AddOperation(root, graph.Linear(magnitude, terms));
            if (!signed) return magnitude;

            var negative = baseName + "Negative";
            graph.AddParameter(negative, AnimatorControllerParameterType.Float, 0f);
            var negativeMagnitude = graph.Param($"Tracking/{articulator}/BinaryNegativeMagnitude", 0f);
            graph.AddOperation(root, graph.Multiply(negative, magnitude, negativeMagnitude, false));
            var decoded = graph.Param($"Tracking/{articulator}/BinarySigned", 0f);
            graph.AddOperation(root, graph.Linear(decoded, new[]
            {
                Term.Positive(magnitude, 1f),
                Term.Positive(negativeMagnitude, -2f)
            }));
            return decoded;
        }

        internal static IEnumerable<AdvancedVisemeArticulator> SynthesizedArticulators()
        {
            foreach (var articulator in CoreArticulators) yield return articulator;
            foreach (var articulator in QualityArticulators) yield return articulator;
            foreach (var articulator in FullTongueArticulators) yield return articulator;
        }

        internal static bool HasCompleteVisiblePoseOwnership(
            IEnumerable<AdvancedVisemeArticulator> measured)
        {
            if (measured == null) return false;
            var available = measured as ISet<AdvancedVisemeArticulator> ??
                            new HashSet<AdvancedVisemeArticulator>(measured);
            return VisiblePoseOwnershipArticulators.All(available.Contains);
        }

        internal static bool HasDriveableOutputPose(
            Request request,
            AdvancedVisemeArticulator articulator)
        {
            if (request == null) return false;
            if (request.resolvedBlendShapes != null &&
                request.resolvedBlendShapes.TryGetValue(articulator, out var shape) &&
                !string.IsNullOrEmpty(shape))
                return true;

            var binding = request.profile != null
                ? request.profile.FindBinding(articulator)
                : null;
            if (!request.reuseExistingTracking)
                return binding != null && binding.animationOverride != null;

            // Reused tracking is already present in a lower Override layer, so
            // the later correction needs a verified, invertible target-renderer
            // basis. Parameter availability alone is not visual ownership.
            if (request.externalPoses != null &&
                request.externalPoses.TryGetValue(articulator, out var external) &&
                external != null && external.positive != null &&
                IsEntireLinearCorrectionClip(
                    external.positive, request.rendererPath, request.targetMesh))
                return true;
            return binding != null && binding.animationOverride != null &&
                   IsEntireLinearCorrectionClip(
                       binding.animationOverride, request.rendererPath, request.targetMesh);
        }

        internal static IEnumerable<AdvancedVisemeArticulator> TrackedArticulators(
            AdvancedVisemeTrackingInputs mode)
        {
            if (mode == AdvancedVisemeTrackingInputs.Disabled ||
                mode == AdvancedVisemeTrackingInputs.Auto ||
                mode == AdvancedVisemeTrackingInputs.ReuseExisting)
                yield break;

            foreach (var articulator in CoreArticulators) yield return articulator;
            if (mode == AdvancedVisemeTrackingInputs.Quality12 ||
                mode == AdvancedVisemeTrackingInputs.FullTongue18)
                foreach (var articulator in QualityArticulators) yield return articulator;
            if (mode == AdvancedVisemeTrackingInputs.FullTongue18)
                foreach (var articulator in FullTongueArticulators) yield return articulator;
        }

        internal static bool TryResolveTrackingParameter(
            Request request,
            AdvancedVisemeArticulator articulator,
            ArticulatorRigBinding binding,
            out string parameter)
        {
            parameter = null;
            if (request == null || binding == null || string.IsNullOrWhiteSpace(binding.trackingParameter))
                return false;
            if (request.reuseExistingTracking)
            {
                // A partial custom template is valid. Missing measurements remain
                // speech-driven instead of becoming fabricated zero-valued inputs.
                return request.trackingParameterNames != null &&
                       request.trackingParameterNames.TryGetValue(articulator, out parameter) &&
                       !string.IsNullOrEmpty(parameter);
            }

            parameter = TrackingParameterName(request.trackingPrefix, binding.trackingParameter);
            return !string.IsNullOrEmpty(parameter);
        }

        private static bool IsSigned(AdvancedVisemeArticulator articulator)
        {
            return articulator == AdvancedVisemeArticulator.SmileSad ||
                   articulator == AdvancedVisemeArticulator.JawX ||
                   articulator == AdvancedVisemeArticulator.JawZ ||
                   articulator == AdvancedVisemeArticulator.MouthX ||
                   articulator == AdvancedVisemeArticulator.TongueY ||
                   articulator == AdvancedVisemeArticulator.TongueX ||
                   articulator == AdvancedVisemeArticulator.TongueArchY ||
                   articulator == AdvancedVisemeArticulator.TongueShape;
        }

        internal static bool IsLinearCorrectionCurve(
            EditorCurveBinding binding,
            string rendererPath,
            Mesh targetMesh)
        {
            if (binding.type != typeof(SkinnedMeshRenderer) ||
                !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal) ||
                !string.Equals(binding.path, rendererPath, StringComparison.Ordinal))
                return false;
            var shape = binding.propertyName.Substring("blendShape.".Length);
            return targetMesh == null || targetMesh.GetBlendShapeIndex(shape) >= 0;
        }

        internal static bool IsEntireLinearCorrectionClip(
            AnimationClip source,
            string rendererPath,
            Mesh targetMesh)
        {
            if (source == null || AnimationUtility.GetObjectReferenceCurveBindings(source).Length != 0)
                return false;
            var bindings = AnimationUtility.GetCurveBindings(source);
            if (bindings.Length == 0) return false;
            var sampleTime = Mathf.Max(0f, source.length);
            foreach (var binding in bindings)
            {
                if (!IsLinearCorrectionCurve(binding, rendererPath, targetMesh)) return false;
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null) return false;
                var value = curve.Evaluate(sampleTime);
                if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            }
            return true;
        }

        private static (float input, float output) Point(float input, float output) => (input, output);

        private static void AddTimeLayer(AnimatorController controller, MathGraph graph, string timeParameter)
        {
            var clip = graph.Clip("Continuous Time");
            var curve = AnimationCurve.Linear(0f, 0f, 100000f, 100000f);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), timeParameter), curve);
            AddMotionLayer(controller, graph, "YUCP AVR Time", clip);
        }

        private static SharedSilenceAuthorityLayer
            AddSharedSilenceUpdateAuthorityLayer(
            AnimatorController controller,
            MathGraph graph,
            string speechHistory,
            string silenceStability)
        {
            var authority = graph.Param(
                "Speech/Hangover/UpdateAuthority", 1f);
            var machine = AddStateLayer(
                controller, graph, "YUCP AVR Shared Silence Authority");
            var silence = machine.AddState("Silence");
            silence.writeDefaultValues = true;
            var speech = machine.AddState("Speech");
            speech.writeDefaultValues = true;
            machine.defaultState = silence;

            // Animator transitions can read the built-in Int directly. AAP
            // curves become visible on the following Animator evaluation, which
            // is exactly when Math observes the matching decoded viseme/history
            // epoch. Materializing the decision from the private delayed index
            // would add an erroneous extra frame.
            var toSpeech = silence.AddTransition(speech);
            ConfigureImmediate(toSpeech);
            toSpeech.AddCondition(
                AnimatorConditionMode.NotEqual, 0f, "Viseme");
            var toSilence = speech.AddTransition(silence);
            ConfigureImmediate(toSilence);
            toSilence.AddCondition(
                AnimatorConditionMode.Equals, 0f, "Viseme");
            return new SharedSilenceAuthorityLayer
            {
                authority = authority,
                silence = silence,
                speech = speech
            };
        }

        private static void FinalizeSharedSilenceUpdateAuthorityLayer(
            MathGraph graph,
            SharedSilenceAuthorityLayer layer,
            string speechHistory,
            string silenceStability,
            Motion sharedMotion)
        {
            if (layer == null) return;
            var silenceMotion = graph.SharedSilenceUpdateAuthority(
                speechHistory, silenceStability, layer.authority,
                out var speechUpdate);

            Motion Combine(string name, Motion authorityMotion)
            {
                if (sharedMotion == null) return authorityMotion;
                var combined = graph.Direct(name);
                combined.children = new[]
                {
                    new ChildMotion
                    {
                        motion = authorityMotion,
                        directBlendParameter = MathGraph.AlwaysOneParameter,
                        timeScale = 1f
                    },
                    new ChildMotion
                    {
                        motion = sharedMotion,
                        directBlendParameter = MathGraph.AlwaysOneParameter,
                        timeScale = 1f
                    }
                };
                return combined;
            }

            layer.silence.motion = Combine(
                "Silence authority and sparse observer emission", silenceMotion);
            layer.speech.motion = Combine(
                "Speech authority and sparse observer emission", speechUpdate);
        }

        private static void AddConditionalLearnedDetailMathControl(
            MathGraph graph,
            BlendTree root,
            string time,
            string frameTime,
            string visemeIndex,
            string sparseFastSilence,
            string compute,
            string authority)
        {
            var epsilon = AdvancedVisemeMath.SimplexCullingEpsilon;
            var computeFromSparseSpeech = graph.Map(
                sparseFastSilence, compute, new[]
                {
                    Point(0f, 1f),
                    Point(1f - epsilon, 1f),
                    Point(1f - epsilon * 0.5f, 0f),
                    Point(1f, 0f)
                });
            var computeFromHardSpeech = graph.SelectMotion(
                visemeIndex,
                computeFromSparseSpeech, 0f,
                graph.Setter(compute, 1f), 1f,
                "Conditional learned-detail hard-speech wake");
            var lowFpsTransitionStart =
                ConditionalLearnedDetailLowFpsBypassFrameSeconds -
                ConditionalLearnedDetailLowFpsTransitionSeconds;
            var runtimeCompute = graph.SelectMotion(
                frameTime,
                computeFromHardSpeech, lowFpsTransitionStart,
                graph.Setter(compute, 1f),
                ConditionalLearnedDetailLowFpsBypassFrameSeconds,
                "Conditional learned-detail low-FPS compute bypass");
            graph.AddOperation(root, graph.SelectMotion(
                time,
                graph.Setter(compute, 1f),
                ConditionalLearnedDetailStartupHotSeconds,
                runtimeCompute,
                ConditionalLearnedDetailStartupHotSeconds +
                ConditionalLearnedDetailStartupTransitionSeconds,
                "Conditional learned-detail startup compute envelope"));

            // A/B seam: publish as soon as the same exact scalar wakes the
            // inference subtree. This removes a redundant wall-time observer;
            // the learned model's own speech-presence and confidence products
            // still bound every physical correction. Keep this test-only until
            // long-idle and adversarial face-motion replays certify the wake.
            if (string.Equals(compute, authority, StringComparison.Ordinal))
                return;

            if (UseModelMatchedConditionalLearnedDetailReadinessForTests)
            {
                // Every intentionally slept temporal lane in the learned model
                // is a two-pole observer using this generated 24 ms alpha.
                // Generate it here so readiness does not depend on the
                // separate face-conditioned inference alpha.
                var modelAlpha = graph.Param(
                    "ConditionalLearnedDetail/ReadinessAlpha", 0.5f);
                graph.AddOperation(root, graph.AlphaFromDeltaTime(
                    frameTime,
                    modelAlpha,
                    AdvancedVisemeHiddenPhonePosterior
                        .ObserverResponseSeconds));
                var readinessFast = graph.Param(
                    "ConditionalLearnedDetail/ReadinessFast", 1f);
                var readiness = graph.Param(
                    "ConditionalLearnedDetail/Readiness", 1f);
                graph.AddOperation(root, graph.Smooth(
                    compute, readinessFast, modelAlpha, false));
                graph.AddOperation(root, graph.Smooth(
                    readinessFast, readiness, modelAlpha, false));
                var authorityFromReadiness = graph.Map(
                    readiness, authority, SmoothStepPoints(
                        ConditionalLearnedDetailReadinessStart,
                        ConditionalLearnedDetailReadinessFull,
                        0f, 1f));
                var runtimeReadinessAuthority = graph.SelectMotion(
                    frameTime,
                    authorityFromReadiness, lowFpsTransitionStart,
                    graph.Setter(authority, 1f),
                    ConditionalLearnedDetailLowFpsBypassFrameSeconds,
                    "Conditional learned-detail low-FPS readiness authority bypass");
                graph.AddOperation(root, graph.SelectMotion(
                    time,
                    graph.Setter(authority, 1f),
                    ConditionalLearnedDetailStartupHotSeconds,
                    runtimeReadinessAuthority,
                    ConditionalLearnedDetailStartupHotSeconds +
                    ConditionalLearnedDetailStartupTransitionSeconds,
                    "Conditional learned-detail startup readiness authority envelope"));
                return;
            }

            // Computation wakes first; a single bounded pole delays publication
            // until the hidden recurrent bank has seen enough ordinary Math
            // evaluations. Defaults are hot, preserving first-frame behavior and
            // making a low-FPS build fail open instead of hiding speech.
            var warmthAlpha = graph.Param(
                "ConditionalLearnedDetail/WarmthAlpha", 0.2f);
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, warmthAlpha,
                ConditionalLearnedDetailWarmthSeconds));
            var warmth = graph.Param(
                "ConditionalLearnedDetail/Warmth", 1f);
            graph.AddOperation(root, graph.Smooth(
                compute, warmth, warmthAlpha, false));
            var authorityFromWarmth = graph.Map(
                warmth, authority, SmoothStepPoints(
                    ConditionalLearnedDetailAuthorityStart,
                    ConditionalLearnedDetailAuthorityFull,
                    0f, 1f));
            var runtimeAuthority = graph.SelectMotion(
                frameTime,
                authorityFromWarmth, lowFpsTransitionStart,
                graph.Setter(authority, 1f),
                ConditionalLearnedDetailLowFpsBypassFrameSeconds,
                "Conditional learned-detail low-FPS authority bypass");

            // The decoded Float viseme becomes visible to Math one evaluation
            // after VRChat's built-in Int. Keep both endpoints hot only during
            // controller initialization so a cold speech onset remains identical
            // to the legacy always-on graph. This is an absolute Time envelope,
            // not a state transition, and therefore remains safe when VRCFury
            // normalizes managed layers to Write Defaults On.
            graph.AddOperation(root, graph.SelectMotion(
                time,
                graph.Setter(authority, 1f),
                ConditionalLearnedDetailStartupHotSeconds,
                runtimeAuthority,
                ConditionalLearnedDetailStartupHotSeconds +
                ConditionalLearnedDetailStartupTransitionSeconds,
                "Conditional learned-detail startup authority envelope"));
        }

        private static bool ShouldDelayConditionalLearnedDetailAuthority(
            bool betaFaceInferenceEnabled)
        {
            return betaFaceInferenceEnabled &&
                   !UseImmediateConditionalLearnedDetailAuthorityForTests;
        }

        internal static float ConditionalLearnedDetailComputeTarget(
            float decodedVisemeIndex,
            float sparseFastSilence,
            float frameSeconds,
            float elapsedSeconds = float.PositiveInfinity)
        {
            var epsilon = AdvancedVisemeMath.SimplexCullingEpsilon;
            var evidence = decodedVisemeIndex >= 1f
                ? 1f
                : 1f - Mathf.InverseLerp(
                    1f - epsilon,
                    1f - epsilon * 0.5f,
                    sparseFastSilence);
            var lowFps = Mathf.InverseLerp(
                ConditionalLearnedDetailLowFpsBypassFrameSeconds -
                ConditionalLearnedDetailLowFpsTransitionSeconds,
                ConditionalLearnedDetailLowFpsBypassFrameSeconds,
                frameSeconds);
            var startup = 1f - Mathf.InverseLerp(
                ConditionalLearnedDetailStartupHotSeconds,
                ConditionalLearnedDetailStartupHotSeconds +
                ConditionalLearnedDetailStartupTransitionSeconds,
                elapsedSeconds);
            var runtime = Mathf.Clamp01(
                evidence + lowFps - evidence * lowFps);
            return Mathf.Clamp01(runtime + startup - runtime * startup);
        }

        internal static float ConditionalLearnedDetailAuthorityFromWarmth(
            float warmth,
            float frameSeconds,
            float elapsedSeconds = float.PositiveInfinity)
        {
            var boundedWarmth = AdvancedVisemeMath.SmoothStep(
                ConditionalLearnedDetailAuthorityStart,
                ConditionalLearnedDetailAuthorityFull,
                warmth);
            var lowFps = Mathf.InverseLerp(
                ConditionalLearnedDetailLowFpsBypassFrameSeconds -
                ConditionalLearnedDetailLowFpsTransitionSeconds,
                ConditionalLearnedDetailLowFpsBypassFrameSeconds,
                frameSeconds);
            var runtime = Mathf.Clamp01(
                boundedWarmth + lowFps - boundedWarmth * lowFps);
            var startup = 1f - Mathf.InverseLerp(
                ConditionalLearnedDetailStartupHotSeconds,
                ConditionalLearnedDetailStartupHotSeconds +
                ConditionalLearnedDetailStartupTransitionSeconds,
                elapsedSeconds);
            return Mathf.Clamp01(runtime + startup - runtime * startup);
        }

        internal static float ConditionalLearnedDetailAuthorityFromReadiness(
            float readiness,
            float frameSeconds,
            float elapsedSeconds = float.PositiveInfinity)
        {
            var boundedReadiness = AdvancedVisemeMath.SmoothStep(
                ConditionalLearnedDetailReadinessStart,
                ConditionalLearnedDetailReadinessFull,
                readiness);
            var lowFps = Mathf.InverseLerp(
                ConditionalLearnedDetailLowFpsBypassFrameSeconds -
                ConditionalLearnedDetailLowFpsTransitionSeconds,
                ConditionalLearnedDetailLowFpsBypassFrameSeconds,
                frameSeconds);
            var runtime = Mathf.Clamp01(
                boundedReadiness + lowFps - boundedReadiness * lowFps);
            var startup = 1f - Mathf.InverseLerp(
                ConditionalLearnedDetailStartupHotSeconds,
                ConditionalLearnedDetailStartupHotSeconds +
                ConditionalLearnedDetailStartupTransitionSeconds,
                elapsedSeconds);
            return Mathf.Clamp01(runtime + startup - runtime * startup);
        }

        private static void AssertUnnormalizedDirectBlendTree(
            BlendTree tree,
            string description)
        {
            if (tree == null || tree.blendType != BlendTreeType.Direct)
                throw new InvalidOperationException(
                    description + " must remain a Direct BlendTree.");
            var serialized = new SerializedObject(tree);
            var normalized = serialized.FindProperty("m_NormalizedBlendValues");
            if (normalized == null || normalized.boolValue)
                throw new InvalidOperationException(
                    description + " must keep normalized blend values disabled.");
        }

        // Slew-rate limit `output` toward `target` at a fixed max speed
        // (pose units/sec), expressed as the sanctioned one-pole Smooth with a
        // state-dependent alpha = min(1, step/|target-output|). This keeps the
        // recurrence inside the single-op self-read stability guarantee: alpha is
        // computed feed-forward and Map-clamped to [0,1], so the self-referential
        // update is always a convex combination of output and target and cannot
        // diverge, even if a stale epoch feeds it a slightly wrong alpha (that is
        // just a wrong SPEED for one render frame, never an overshoot). A raw
        // `output += clip(target-output)` accumulate would be the multi-op
        // recurrence that historically hit the +/-2 rails; this is not.
        private static void AppendPoseSlew(
            MathGraph graph, BlendTree root, string frameTime,
            string target, string output, float speed)
        {
            const float refHz = 90f;
            var s0 = Mathf.Max(1e-4f, speed / refHz); // per-render step at ref rate

            var delta = graph.Param(output + "/SlewDelta", 0f);
            graph.AddOperation(root, graph.Linear(delta, new[]
            {
                Term.Positive(target, 1f), Term.Positive(output, -1f)
            }));
            var absDelta = graph.Param(output + "/SlewAbs", 0f);
            graph.AddOperation(root, graph.Map(delta, absDelta, new[]
            {
                Point(-2f, 2f), Point(0f, 0f), Point(2f, 2f)
            }));
            // min(1, s0/x) sampled at octaves — a hyperbola within a few percent
            // everywhere; error only perturbs speed, never stability.
            var alphaBase = graph.Param(output + "/SlewAlphaBase", 1f);
            graph.AddOperation(root, graph.Map(absDelta, alphaBase, new[]
            {
                Point(0f, 1f), Point(s0, 1f), Point(2f * s0, 0.5f),
                Point(4f * s0, 0.25f), Point(8f * s0, 0.125f), Point(2f, 0.5f * s0)
            }));
            // Frame-rate scale: step = speed*dt_render, so alpha *= frameTime*refHz
            // (== 1 at the reference rate). Keeps constant real speed off 90 fps.
            var scaled = graph.Param(output + "/SlewScaled", 0f);
            graph.AddOperation(root,
                graph.Multiply(alphaBase, frameTime, scaled, false));
            var alphaRaw = graph.Param(output + "/SlewAlphaRaw", 0f);
            graph.AddOperation(root, graph.Linear(alphaRaw, new[]
            {
                Term.Positive(scaled, refHz)
            }));
            var alpha = graph.Param(output + "/SlewAlpha", 1f);
            graph.AddOperation(root, graph.Map(alphaRaw, alpha, new[]
            {
                Point(0f, 0f), Point(1f, 1f), Point(2f, 1f)
            }));
            graph.AddOperation(root, graph.Smooth(target, output, alpha, true));
        }

        /// <summary>
        /// Linear dominance filter over the hard-winner one-hot, evaluated through
        /// a gamma memory (cascaded one-pole Smooths). The readout is trained to
        /// reproduce the viseme simplex delayed by a few analysis frames, which is
        /// what makes anticipatory coarticulation available: the mouth can move
        /// toward the NEXT sound because, at the delayed render time, that sound
        /// has already been observed. The calibration matrix is folded into the
        /// readout at build time so each articulator costs one Linear op.
        /// </summary>
        private static Dictionary<AdvancedVisemeArticulator, string>
            BuildLookaheadFirPose(
                Request request, MathGraph graph, BlendTree root,
                string frameTime, string visemeIndex, string smoothnessTuning,
                string motionTuning, string livelinessTuning,
                IEnumerable<AdvancedVisemeArticulator> articulators)
        {
            var poses = new Dictionary<AdvancedVisemeArticulator, string>();
            var count = VisemeReconstructionProfile.VisemeCount;
            var stages = AdvancedVisemeLookaheadFir.Stages;

            // The winner index is an exact integer, so a triangular map recovers
            // the one-hot with no interpolation error.
            var basis = new List<string>();
            var oneHot = new string[count];
            for (var i = 0; i < count; i++)
            {
                var channel = graph.Param($"Fir/OneHot/{i}", i == 0 ? 1f : 0f);
                graph.AddOperation(root, graph.Map(visemeIndex, channel, new[]
                {
                    Point(i - 1f, 0f), Point(i, 1f), Point(i + 1f, 0f)
                }));
                oneHot[i] = channel;
            }
            basis.AddRange(oneHot);

            // Speech Smoothness drives the cascade time constant: it IS the
            // memory's response time, so the menu slider keeps its meaning on
            // the FIR path instead of going dead with the old observer.
            var alpha = BuildTunableAlpha(
                graph, root, frameTime, "Fir/Alpha",
                AdvancedVisemeLookaheadFir.StageTauSeconds,
                smoothnessTuning, 0.008f, 0.050f);
            var previous = oneHot;
            for (var stage = 0; stage < stages; stage++)
            {
                var current = new string[count];
                for (var j = 0; j < count; j++)
                {
                    var state = graph.Param($"Fir/G{stage}/{j}", 0f);
                    graph.AddOperation(root,
                        graph.Smooth(previous[j], state, alpha, false));
                    current[j] = state;
                }
                basis.AddRange(current);
                previous = current;
            }
            basis.Add(graph.Param("Voice", 0f, false));
            basis.Add(MathGraph.AlwaysOneParameter);

            if (basis.Count != AdvancedVisemeLookaheadFir.BasisCount)
                throw new InvalidOperationException(
                    $"Lookahead FIR basis is {basis.Count} but the trained readout " +
                    $"expects {AdvancedVisemeLookaheadFir.BasisCount}.");

            // Fold the avatar's calibration into the readout. The readout is
            // structurally sparse: only the basis rows that survived
            // prune-and-refit are read. Rows outside the support are still
            // COMPUTED when a later cascade stage needs them, and the optimizer's
            // closed-world DCE drops any that nothing reads.
            var folded = new Dictionary<AdvancedVisemeArticulator, float[]>();
            foreach (var articulator in articulators)
            {
                var coefficients = GetAdjustedSpeechCoefficients(request, articulator);
                if (coefficients == null || coefficients.Length != count) continue;
                var row = new float[AdvancedVisemeLookaheadFir.SupportCount];
                var live = false;
                for (var slot = 0; slot < row.Length; slot++)
                {
                    var weight = 0f;
                    for (var j = 0; j < count; j++)
                        weight += coefficients[j] *
                                  AdvancedVisemeLookaheadFir.Weight(slot, j);
                    // Basis values are all non-negative, so a negative fold is a
                    // plain negative COEFFICIENT, not a negative blend weight.
                    row[slot] = weight;
                    live |= Mathf.Abs(weight) >= 1e-4f;
                }
                if (!live) continue;
                folded[articulator] = row;
                poses[articulator] =
                    graph.Param($"Articulation/{articulator}/Fir", 0f);
            }
            if (folded.Count == 0) return poses;

            // Common-subexpression elimination, in the multiple-constant-
            // multiplication sense: every articulator multiplies the SAME basis
            // by different constants, so evaluate the basis ONCE and let each
            // child clip write all articulators at once. One Direct tree with a
            // child per basis term replaces one Linear per articulator, cutting
            // the child count by the number of articulators without changing the
            // algebra or the epoch. Same shape as the viseme matrix projection.
            var ordered = folded.OrderBy(pair => (int)pair.Key).ToArray();
            var readout = graph.Direct("Lookahead FIR articulation");
            var children = new List<ChildMotion>();
            for (var slot = 0;
                 slot < AdvancedVisemeLookaheadFir.SupportCount;
                 slot++)
            {
                var local = slot;
                var values = ordered
                    .Select(pair => new KeyValuePair<string, float>(
                        poses[pair.Key], pair.Value[local]))
                    .Where(pair => Mathf.Abs(pair.Value) >= 1e-4f)
                    .ToArray();
                if (values.Length == 0) continue;
                children.Add(new ChildMotion
                {
                    motion = graph.MultiSetter(
                        $"Lookahead FIR basis {local}", values),
                    directBlendParameter =
                        basis[AdvancedVisemeLookaheadFir.Support(local)],
                    timeScale = 1f
                });
            }
            // Bind every output unconditionally so Unity never blends a pose
            // against its default when the whole basis is zero.
            children.Add(new ChildMotion
            {
                motion = graph.MultiSetter(
                    "Lookahead FIR safety zero",
                    ordered.Select(pair => new KeyValuePair<string, float>(
                        poses[pair.Key], 0f))),
                directBlendParameter = MathGraph.AlwaysOneParameter,
                timeScale = 1f
            });
            readout.children = children.ToArray();
            graph.AddOperation(root, readout);

            // Give the speech sliders real authority over the FIR mouth.
            // Liveliness scales each pose's deviation from its own slow average
            // (0 flattens toward a steady shape, 1 is the trained behaviour, 2
            // exaggerates the peaks); Motion then scales the whole excursion.
            // Both move the pose along the ray from rest through a hull point,
            // and the render blend trees clamp at their end thresholds, so
            // neither can synthesize an uncalibrated shape.
            var meanAlpha = graph.Param("Fir/MeanAlpha", 0.05f);
            graph.AddOperation(root,
                graph.AlphaFromDeltaTime(frameTime, meanAlpha, 0.220f));
            // The liveliness control is signed, and a signed value used as a
            // Direct blend weight clamps at zero, so convert it to a
            // non-negative gain through a Map instead of a Linear.
            var livelinessGain = graph.Param("Fir/LivelinessGain", 1f);
            graph.AddOperation(root, graph.Map(
                livelinessTuning, livelinessGain, new[]
                {
                    Point(-1f, 0f), Point(0f, 1f), Point(1f, 2f)
                }));

            foreach (var pair in ordered)
            {
                var articulator = pair.Key;
                var pose = poses[articulator];
                var mean = graph.Param($"Articulation/{articulator}/FirMean", 0f);
                graph.AddOperation(root, graph.Smooth(pose, mean, meanAlpha, true));
                var deviation =
                    graph.Param($"Articulation/{articulator}/FirDeviation", 0f);
                graph.AddOperation(root, graph.Linear(deviation, new[]
                {
                    Term.Positive(pose, 1f), Term.Positive(mean, -1f)
                }));
                var lively =
                    graph.Param($"Articulation/{articulator}/FirLively", 0f);
                graph.AddOperation(root,
                    graph.Multiply(livelinessGain, deviation, lively, true));
                var shaped =
                    graph.Param($"Articulation/{articulator}/FirShaped", 0f);
                graph.AddOperation(root, graph.Linear(shaped, new[]
                {
                    Term.Positive(mean, 1f), Term.Positive(lively, 1f)
                }));
                var scaled = graph.Param($"Articulation/{articulator}/FirGain", 0f);
                graph.AddOperation(root,
                    graph.Multiply(motionTuning, shaped, scaled, true));
                poses[articulator] = scaled;
            }
            return poses;
        }

        private static void AddIntToFloatLayer(
            AnimatorController controller,
            MathGraph graph,
            string source,
            string output,
            IReadOnlyList<string> oneHotOutputs,
            IReadOnlyDictionary<string, float[]> identityDecodedVectors,
            IReadOnlyDictionary<string, float[]> haloDecodedVectors,
            IReadOnlyDictionary<string, float[][]> haloTrajectoryVectors,
            bool useOculusHalo,
            string trackingBlend,
            Motion sharedStateMotion,
            float transitionSeconds,
            string layerName,
            bool foldRetentionPull = false)
        {
            if (float.IsNaN(transitionSeconds) ||
                float.IsInfinity(transitionSeconds) || transitionSeconds < 0f)
                throw new InvalidOperationException(
                    $"{layerName} transition duration must be finite and nonnegative.");
            var stateMachine = AddStateLayer(controller, graph, layerName);
            AnimatorState silence = null;
            var states = new AnimatorState[
                VisemeReconstructionProfile.VisemeCount];
            for (var i = 0; i < VisemeReconstructionProfile.VisemeCount; i++)
            {
                var state = stateMachine.AddState(VisemeReconstructionProfile.VisemeNames[i]);
                states[i] = state;

                List<KeyValuePair<string, float>> DecodedValues(
                    bool halo,
                    IReadOnlyDictionary<string, float[]> vectors)
                {
                    var values = new List<KeyValuePair<string, float>>();
                    if (!string.IsNullOrEmpty(output))
                        values.Add(new KeyValuePair<string, float>(output, i));
                    if (oneHotOutputs != null)
                        for (var channel = 0; channel < oneHotOutputs.Count; channel++)
                            values.Add(new KeyValuePair<string, float>(
                                oneHotOutputs[channel],
                                OculusHaloDecoderWeight(halo, i, channel)));
                    if (vectors != null)
                        foreach (var pair in vectors)
                        {
                            if (pair.Value == null ||
                                pair.Value.Length != VisemeReconstructionProfile.VisemeCount)
                                throw new InvalidOperationException(
                                    $"Decoded vector '{pair.Key}' must contain " +
                                    $"{VisemeReconstructionProfile.VisemeCount} values.");
                            values.Add(new KeyValuePair<string, float>(
                                pair.Key, pair.Value[i]));
                        }
                    return values;
                }

                List<KeyValuePair<string, float[]>> DecodedTrajectoryValues()
                {
                    var controls = AdvancedVisemeOculusDynamics.ControlPointCount;
                    float[] Constant(float value) =>
                        Enumerable.Repeat(value, controls).ToArray();

                    var values = new List<KeyValuePair<string, float[]>>();
                    if (!string.IsNullOrEmpty(output))
                        values.Add(new KeyValuePair<string, float[]>(
                            output, Constant(i)));
                    if (oneHotOutputs != null)
                        for (var channel = 0; channel < oneHotOutputs.Count; channel++)
                        {
                            var local = channel;
                            values.Add(new KeyValuePair<string, float[]>(
                                oneHotOutputs[channel],
                                Enumerable.Range(0, controls)
                                    .Select(control => foldRetentionPull
                                        ? RetentionPullFoldedDecoderWeight(
                                            i, control, local)
                                        : OculusDynamicsDecoderWeight(
                                            i, control, local))
                                    .ToArray()));
                        }
                    if (haloDecodedVectors != null)
                        foreach (var pair in haloDecodedVectors)
                        {
                            if (pair.Value == null ||
                                pair.Value.Length != VisemeReconstructionProfile.VisemeCount)
                                throw new InvalidOperationException(
                                    $"Decoded vector '{pair.Key}' must contain " +
                                    $"{VisemeReconstructionProfile.VisemeCount} values.");

                            float[] trajectory;
                            if (haloTrajectoryVectors != null &&
                                haloTrajectoryVectors.TryGetValue(
                                    pair.Key, out var rows))
                            {
                                if (rows == null ||
                                    rows.Length != VisemeReconstructionProfile.VisemeCount ||
                                    rows[i] == null || rows[i].Length != controls)
                                    throw new InvalidOperationException(
                                        $"Decoded trajectory '{pair.Key}' must contain " +
                                        $"15 rows of {controls} controls.");
                                trajectory = rows[i];
                            }
                            else
                            {
                                // Phonetic retention remains keyed to the hard
                                // winner. It is deliberately constant while only
                                // the visible continuous target evolves.
                                trajectory = Constant(pair.Value[i]);
                            }
                            values.Add(new KeyValuePair<string, float[]>(
                                pair.Key, trajectory));
                        }
                    return values;
                }

                Motion HaloMotion(string name)
                {
                    if (!AdvancedVisemeOculusDynamics.HasDynamicTrajectory(i))
                        return graph.MultiSetter(
                            name, DecodedValues(true, haloDecodedVectors));
                    return graph.DirectTrajectorySetter(
                        name,
                        DecodedTrajectoryValues(),
                        AdvancedVisemeOculusDynamics.TrajectoryCoreDurationSeconds,
                        AdvancedVisemeOculusDynamics.TrajectoryDurationSeconds);
                }

                Motion decoded;
                var visemeName = VisemeReconstructionProfile.VisemeNames[i];
                var motionStem = "Decode " + visemeName +
                                 (oneHotOutputs == null &&
                                  !string.IsNullOrEmpty(output)
                                     ? " Semantics"
                                     : string.Empty);
                if (!useOculusHalo)
                {
                    decoded = graph.MultiSetter(
                        motionStem,
                        DecodedValues(false, identityDecodedVectors));
                }
                else if (string.IsNullOrEmpty(trackingBlend))
                {
                    decoded = HaloMotion(motionStem);
                }
                else
                {
                    var haloEndpoint = HaloMotion(
                        motionStem + " Halo");
                    var identityEndpoint = graph.MultiSetter(
                        motionStem + " Identity",
                        DecodedValues(false, identityDecodedVectors));
                    decoded = graph.InterpolateMotions(
                        haloEndpoint,
                        identityEndpoint,
                        trackingBlend,
                        motionStem + " by tracking authority");
                }
                if (sharedStateMotion == null)
                {
                    state.motion = decoded;
                }
                else
                {
                    // Keep the shared vector motion as one referenced subtree.
                    // Flattening it into every state would duplicate its children
                    // fifteen times in the serialized controller.
                    var combined = graph.Direct(
                        "Decode and sparsify " +
                        VisemeReconstructionProfile.VisemeNames[i]);
                    combined.children = new[]
                    {
                        new ChildMotion
                        {
                            motion = decoded,
                            directBlendParameter = MathGraph.AlwaysOneParameter,
                            timeScale = 1f
                        },
                        new ChildMotion
                        {
                            motion = sharedStateMotion,
                            directBlendParameter = MathGraph.AlwaysOneParameter,
                            timeScale = 1f
                        }
                    };
                    state.motion = combined;
                }
                state.writeDefaultValues = true;
                if (i == 0) silence = state;
            }
            stateMachine.defaultState = silence;

            if (transitionSeconds <= 0f)
            {
                for (var index = 0; index < states.Length; index++)
                {
                    var transition = stateMachine.AddAnyStateTransition(
                        states[index]);
                    transition.duration = 0f;
                    transition.hasExitTime = false;
                    transition.canTransitionToSelf = false;
                    transition.AddCondition(
                        AnimatorConditionMode.Equals,
                        index, source);
                }
                return;
            }

            // Pairwise edges make rapid A->B->A and A->B->C changes genuinely
            // interruptible. Inspecting the destination state's outgoing edges
            // avoids an AnyState-to-destination edge repeatedly restarting its
            // own cross-fade while the destination condition remains true.
            for (var from = 0; from < states.Length; from++)
            for (var to = 0; to < states.Length; to++)
            {
                if (from == to) continue;
                var transition = states[from].AddTransition(states[to]);
                transition.hasExitTime = false;
                transition.hasFixedDuration = true;
                transition.duration = transitionSeconds;
                transition.offset = 0f;
                transition.canTransitionToSelf = false;
                transition.interruptionSource =
                    TransitionInterruptionSource.Destination;
                transition.orderedInterruption = false;
                transition.AddCondition(
                    AnimatorConditionMode.Equals, to, source);
            }
        }

        private static void AddTrackingGateLayer(
            AnimatorController controller,
            MathGraph graph,
            string manual,
            string active,
            out string output)
        {
            output = graph.Param("TrackingGate", 0f);
            var layer = AddStateLayer(controller, graph, "YUCP AVR Tracking Gate");
            var off = layer.AddState("Off");
            var on = layer.AddState("On");
            off.motion = graph.Setter(output, 0f);
            on.motion = graph.Setter(output, 1f);
            layer.defaultState = off;
            var enter = off.AddTransition(on);
            enter.duration = 0f;
            enter.hasExitTime = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, active);
            enter.AddCondition(AnimatorConditionMode.If, 0f, manual);
            AddBoolExit(on, off, active);
            AddBoolExit(on, off, manual);
        }

        private static void AddBoolFloatLayer(
            AnimatorController controller,
            MathGraph graph,
            string source,
            string outputName,
            bool defaultValue,
            string layerName,
            out string output)
        {
            output = graph.Param(outputName, defaultValue ? 1f : 0f);
            var layer = AddStateLayer(controller, graph, layerName);
            var off = layer.AddState("False");
            var on = layer.AddState("True");
            off.motion = graph.Setter(output, 0f);
            on.motion = graph.Setter(output, 1f);
            layer.defaultState = defaultValue ? on : off;
            var toOn = off.AddTransition(on);
            toOn.duration = 0f;
            toOn.hasExitTime = false;
            toOn.AddCondition(AnimatorConditionMode.If, 0f, source);
            AddBoolExit(on, off, source);
        }

        private static void AddBoolExit(AnimatorState from, AnimatorState to, string parameter)
        {
            var transition = from.AddTransition(to);
            transition.duration = 0f;
            transition.hasExitTime = false;
            transition.AddCondition(AnimatorConditionMode.IfNot, 0f, parameter);
        }

        private static AnimatorStateMachine AddStateLayer(AnimatorController controller, MathGraph graph, string name)
        {
            controller.AddLayer(name);
            var layers = controller.layers;
            var index = layers.Length - 1;
            layers[index].defaultWeight = 1f;
            controller.layers = layers;
            var stateMachine = layers[index].stateMachine;
            graph.SubAsset(stateMachine);
            return stateMachine;
        }

        private static void AddMotionLayer(AnimatorController controller, MathGraph graph, string name, Motion motion)
        {
            var stateMachine = AddStateLayer(controller, graph, name);
            var state = stateMachine.AddState(name);
            state.motion = motion;
            state.writeDefaultValues = true;
            stateMachine.defaultState = state;
        }

        private readonly struct Term
        {
            public readonly string parameter;
            public readonly float multiplier;
            public readonly bool signed;
            public readonly bool constant;

            private Term(string parameter, float multiplier, bool signed, bool constant)
            {
                this.parameter = parameter;
                this.multiplier = multiplier;
                this.signed = signed;
                this.constant = constant;
            }

            public static Term Positive(string parameter, float multiplier) => new Term(parameter, multiplier, false, false);
            public static Term Signed(string parameter, float multiplier) => new Term(parameter, multiplier, true, false);
            public static Term Constant(float value) => new Term(null, value, false, true);
            public static Term For(string parameter, float multiplier, bool signed) => new Term(parameter, multiplier, signed, false);
            public Term WithMultiplierScale(float scale) =>
                new Term(parameter, multiplier * scale, signed, constant);
        }

        private sealed class MathGraph
        {
            private readonly AnimatorController controller;
            private readonly string prefix;
            private readonly HashSet<UnityEngine.Object> subAssets = new HashSet<UnityEngine.Object>();
            private readonly Dictionary<(string output, float value), AnimationClip> setterCache =
                new Dictionary<(string output, float value), AnimationClip>();
            private readonly Dictionary<string, AlphaBatch> alphaBatches =
                new Dictionary<string, AlphaBatch>(StringComparer.Ordinal);
            private readonly HashSet<Motion> normalizedTrees =
                new HashSet<Motion>();
            private readonly Dictionary<Motion, MapDescriptor> mapDescriptors =
                new Dictionary<Motion, MapDescriptor>();
            private readonly Dictionary<(BlendTree root, string input), MapBatch> mapBatches =
                new Dictionary<(BlendTree root, string input), MapBatch>();
            private readonly Dictionary<Motion, ParameterBlendDescriptor> parameterBlendDescriptors =
                new Dictionary<Motion, ParameterBlendDescriptor>();
            private readonly Dictionary<(BlendTree root, string driver, string thresholds), ParameterBlendBatch>
                parameterBlendBatches =
                    new Dictionary<(BlendTree root, string driver, string thresholds), ParameterBlendBatch>();
            private readonly Dictionary<Motion, BinarySelectDescriptor> binarySelectDescriptors =
                new Dictionary<Motion, BinarySelectDescriptor>();
            private readonly Dictionary<(BlendTree root, string driver), BinarySelectBatch>
                binarySelectBatches =
                    new Dictionary<(BlendTree root, string driver), BinarySelectBatch>();
            private readonly Dictionary<Motion, SilenceHoldDescriptor> silenceHoldDescriptors =
                new Dictionary<Motion, SilenceHoldDescriptor>();
            private readonly Dictionary<(
                BlendTree root, string viseme, string history, string stability,
                bool compactIdentity), SilenceHoldBatch>
                silenceHoldBatches =
                    new Dictionary<(
                        BlendTree root, string viseme, string history, string stability,
                        bool compactIdentity), SilenceHoldBatch>();
            private string sharedSilenceUpdateAuthority;
            private readonly Dictionary<string, string>
                sharedSilenceFactoredWeights =
                    new Dictionary<string, string>(StringComparer.Ordinal);
            private AnimationClip emptyClip;
            private const string AlwaysOne = "__YUCP_AVR_ONE";
            public const string AlwaysOneParameter = AlwaysOne;

            private sealed class AlphaBatch
            {
                public BlendTree tree;
                public float[] samples;
                public AnimationClip[] clips;
            }

            private sealed class MapDescriptor
            {
                public string input;
                public string output;
                public (float input, float output)[] points;
            }

            private sealed class MapBatch
            {
                public BlendTree tree;
                public readonly List<MapDescriptor> descriptors = new List<MapDescriptor>();
            }

            private sealed class ParameterBlendDescriptor
            {
                public string driver;
                public string output;
                public float[] thresholds;
                public string[] sources;
                public bool signed;
            }

            private sealed class ParameterBlendBatch
            {
                public BlendTree tree;
                public readonly List<ParameterBlendDescriptor> descriptors =
                    new List<ParameterBlendDescriptor>();
            }

            private sealed class BinarySelectDescriptor
            {
                public string driver;
                public Motion whenZero;
                public Motion whenOne;
            }

            private sealed class BinarySelectBatch
            {
                public BlendTree tree;
                public BlendTree whenZero;
                public BlendTree whenOne;
                public readonly HashSet<string> bindings =
                    new HashSet<string>(StringComparer.Ordinal);
            }

            private sealed class SilenceHoldDescriptor
            {
                public string viseme;
                public string history;
                public string stability;
                public Motion nonSilence;
                public Motion silenceRelease;
                public Motion silenceHold;
                public bool identityHold;
            }

            private sealed class SilenceHoldBatch
            {
                public BlendTree tree;
                public BlendTree nonSilence;
                public BlendTree silenceRelease;
                public BlendTree silenceHold;
                public readonly HashSet<string> bindings =
                    new HashSet<string>(StringComparer.Ordinal);
            }

            public MathGraph(AnimatorController controller, string prefix)
            {
                this.controller = controller;
                this.prefix = prefix;
                AddParameter(AlwaysOne, AnimatorControllerParameterType.Float, 1f);
            }

            public void UseSharedSilenceUpdateAuthority(string authority)
            {
                sharedSilenceUpdateAuthority = authority;
            }

            public string RegisterSharedSilenceFactoredWeight(
                string source,
                string key)
            {
                if (string.IsNullOrEmpty(source))
                    throw new ArgumentException(
                        "A factored silence weight needs a source parameter.",
                        nameof(source));
                if (sharedSilenceFactoredWeights.TryGetValue(
                        source, out var existing))
                    return existing;
                var sourceParameter = controller.parameters.FirstOrDefault(
                    parameter => parameter.name == source);
                var output = Param(
                    "Speech/Hangover/FactoredAlpha/" + Sanitize(key),
                    sourceParameter?.defaultFloat ?? 0f);
                sharedSilenceFactoredWeights[source] = output;
                return output;
            }

            public Motion SharedSilenceUpdateAuthority(
                string history,
                string stability,
                string output,
                out Motion update)
            {
                Motion freeze;
                if (sharedSilenceFactoredWeights.Count == 0)
                {
                    update = Setter(output, 1f);
                    freeze = Setter(output, 0f);
                }
                else
                {
                    var updateVector = Direct(
                        "Shared silence authority and factored alpha update");
                    updateVector.children = new[] { Child(Setter(output, 1f)) }
                        .Concat(sharedSilenceFactoredWeights.Select(pair =>
                            Child(Copy(pair.Key, pair.Value, false))))
                        .ToArray();
                    update = updateVector;
                    freeze = MultiSetter(
                        "Shared silence authority and factored alpha freeze",
                        new[] { new KeyValuePair<string, float>(output, 0f) }
                            .Concat(sharedSilenceFactoredWeights.Values.Select(
                                factoredOutput =>
                                    new KeyValuePair<string, float>(
                                        factoredOutput, 0f))));
                }
                var byHistory = OneDimensional(
                    "Shared silence authority by history", history,
                    new[]
                    {
                        Child(update, AdvancedVisemeMath.SpeechHistoryHoldStart),
                        Child(freeze, AdvancedVisemeMath.SpeechHistoryHoldFull)
                    });
                return OneDimensional(
                    "Shared silence authority by stability", stability,
                    new[]
                    {
                        Child(update, 0f),
                        Child(byHistory, 0.5f)
                    });
            }

            public string Param(string name, float defaultValue, bool internalName = true)
            {
                var parameter = internalName ? prefix + "/" + name : name;
                AddParameter(parameter, AnimatorControllerParameterType.Float, defaultValue);
                return parameter;
            }

            public void AddParameter(string name, AnimatorControllerParameterType type, float defaultValue)
            {
                if (controller.parameters.Any(p => p.name == name)) return;
                var parameter = new AnimatorControllerParameter { name = name, type = type };
                if (type == AnimatorControllerParameterType.Float) parameter.defaultFloat = defaultValue;
                else if (type == AnimatorControllerParameterType.Int) parameter.defaultInt = Mathf.RoundToInt(defaultValue);
                else if (type == AnimatorControllerParameterType.Bool) parameter.defaultBool = defaultValue > 0.5f;
                controller.AddParameter(parameter);
            }

            public BlendTree Direct(string name)
            {
                var tree = new BlendTree
                {
                    name = name,
                    blendType = BlendTreeType.Direct,
                    useAutomaticThresholds = false
                };
                SubAsset(tree);
                return tree;
            }

            public AnimationClip Clip(string name)
            {
                var clip = new AnimationClip { name = name };
                SubAsset(clip);
                return clip;
            }

            public AnimationClip EmptyClip()
            {
                if (emptyClip != null) return emptyClip;
                emptyClip = Clip("YUCP AVR Empty");
                return emptyClip;
            }

            public AnimationClip Setter(string output, float value)
            {
                var key = (output, value);
                if (setterCache.TryGetValue(key, out var existing)) return existing;
                var clip = Clip($"{output} = {value:0.###}");
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve("", typeof(Animator), output),
                    AnimationCurve.Constant(0f, 0f, value));
                setterCache[key] = clip;
                return clip;
            }

            public AnimationClip MultiSetter(
                string name,
                IEnumerable<KeyValuePair<string, float>> values,
                float duration = 0f)
            {
                var clip = Clip(name);
                duration = Mathf.Max(0f, duration);
                foreach (var pair in values)
                {
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve("", typeof(Animator), pair.Key),
                        FlatConstantCurve(pair.Value, duration));
                }
                return clip;
            }

            /// <summary>
            /// Encodes the positive direct-render trajectory as a cubic Bezier
            /// followed by a linear continuation. Both segments are convex
            /// combinations of simplex controls, so Unity samples a valid
            /// simplex at every state-local time.
            /// </summary>
            public AnimationClip DirectTrajectorySetter(
                string name,
                IEnumerable<KeyValuePair<string, float[]>> values,
                float coreDuration,
                float duration)
            {
                if (values == null) throw new ArgumentNullException(nameof(values));
                if (float.IsNaN(coreDuration) || float.IsInfinity(coreDuration) ||
                    float.IsNaN(duration) || float.IsInfinity(duration) ||
                    coreDuration <= 0f || duration <= coreDuration)
                    throw new ArgumentOutOfRangeException(nameof(duration));

                var clip = Clip(name);
                foreach (var pair in values)
                {
                    if (pair.Value == null || pair.Value.Length != 5)
                        throw new InvalidOperationException(
                            $"Direct trajectory '{pair.Key}' requires five controls.");
                    for (var control = 0; control < pair.Value.Length; control++)
                        if (float.IsNaN(pair.Value[control]) ||
                            float.IsInfinity(pair.Value[control]))
                            throw new InvalidOperationException(
                                $"Direct trajectory '{pair.Key}' contains a non-finite control.");

                    var start = new Keyframe(
                        0f,
                        pair.Value[0],
                        0f,
                        3f * (pair.Value[1] - pair.Value[0]) / coreDuration)
                    {
                        weightedMode = WeightedMode.None
                    };
                    var tailSlope =
                        (pair.Value[4] - pair.Value[3]) /
                        (duration - coreDuration);
                    var seam = new Keyframe(
                        coreDuration,
                        pair.Value[3],
                        3f * (pair.Value[3] - pair.Value[2]) / coreDuration,
                        tailSlope)
                    {
                        weightedMode = WeightedMode.None
                    };
                    var end = new Keyframe(
                        duration,
                        pair.Value[4],
                        tailSlope,
                        0f)
                    {
                        weightedMode = WeightedMode.None
                    };
                    var curve = new AnimationCurve(start, seam, end)
                    {
                        preWrapMode = WrapMode.ClampForever,
                        postWrapMode = WrapMode.ClampForever
                    };
                    AnimationUtility.SetEditorCurve(
                        clip,
                        EditorCurveBinding.FloatCurve(
                            string.Empty, typeof(Animator), pair.Key),
                        curve);
                }
                return clip;
            }

            private static AnimationCurve FlatConstantCurve(
                float value,
                float duration)
            {
                if (duration <= 0f)
                    return AnimationCurve.Constant(0f, 0f, value);
                return new AnimationCurve(
                    new Keyframe(0f, value, 0f, 0f)
                    {
                        weightedMode = WeightedMode.None
                    },
                    new Keyframe(duration, value, 0f, 0f)
                    {
                        weightedMode = WeightedMode.None
                    })
                {
                    preWrapMode = WrapMode.ClampForever,
                    postWrapMode = WrapMode.ClampForever
                };
            }

            public Motion Copy(string input, string output, bool signed)
            {
                return signed
                    ? Map(input, output, new[] { Point(-2f, -2f), Point(0f, 0f), Point(2f, 2f) })
                    : WeightedSetter(input, output, 1f);
            }

            public Motion CopyVector(
                IReadOnlyList<string> inputs,
                IReadOnlyList<string> outputs,
                string name,
                bool includeBaseline = true)
            {
                if (inputs == null || outputs == null || inputs.Count != outputs.Count)
                    throw new InvalidOperationException(
                        $"{name} requires equally sized input and output vectors.");

                // Every vector element is a nonnegative simplex coordinate. A
                // single Direct tree can therefore copy the complete vector in
                // one Animator stage. The shared zero clip gives every output a
                // deterministic neutral contribution without compiling one
                // WeightedSetter tree per scalar.
                var tree = Direct(name);
                var children = new List<ChildMotion>(inputs.Count + 1);
                for (var i = 0; i < inputs.Count; i++)
                    children.Add(new ChildMotion
                    {
                        motion = Setter(outputs[i], 1f),
                        directBlendParameter = inputs[i],
                        timeScale = 1f
                    });
                if (includeBaseline) children.Add(new ChildMotion
                {
                    motion = MultiSetter(
                        name + " safety zero",
                        outputs.Select(output =>
                            new KeyValuePair<string, float>(output, 0f))),
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
                tree.children = children.ToArray();
                return tree;
            }

            public Motion CopyMixedVector(
                IReadOnlyList<string> inputs,
                IReadOnlyList<string> outputs,
                IReadOnlyList<bool> signed,
                string name,
                float duration = 0f)
            {
                if (inputs == null || outputs == null || signed == null ||
                    inputs.Count != outputs.Count || inputs.Count != signed.Count)
                    throw new InvalidOperationException(
                        $"{name} requires equally sized mixed vectors.");

                var tree = Direct(name);
                var children = new List<ChildMotion>(inputs.Count + 1);
                for (var i = 0; i < inputs.Count; i++)
                {
                    if (signed[i])
                    {
                        children.Add(new ChildMotion
                        {
                            motion = Map(inputs[i], outputs[i], new[]
                            {
                                Point(-2f, -2f), Point(0f, 0f), Point(2f, 2f)
                            }),
                            directBlendParameter = AlwaysOne,
                            timeScale = 1f
                        });
                    }
                    else
                    {
                        children.Add(new ChildMotion
                        {
                            motion = Setter(outputs[i], 1f),
                            directBlendParameter = inputs[i],
                            timeScale = 1f
                        });
                    }
                }
                children.Add(new ChildMotion
                {
                    motion = MultiSetter(
                        name + " safety zero",
                        outputs.Select((output, index) => (output, index))
                            .Where(item =>
                                !UseUnconditionalSignedBindingProofForTests ||
                                !signed[item.index])
                            .Select(item =>
                                new KeyValuePair<string, float>(
                                    item.output, 0f)),
                        duration),
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
                tree.children = children.ToArray();
                return tree;
            }

            public Motion SimplexMatrixProjection(
                IReadOnlyList<string> weights,
                IReadOnlyList<string> outputs,
                Func<int, int, float> coefficient,
                string name)
            {
                return SimplexMatrixProjection(
                    weights, outputs, coefficient, null, null, name);
            }

            public Motion SimplexMatrixProjection(
                IReadOnlyList<string> weights,
                IReadOnlyList<string> outputs,
                Func<int, int, float> coefficient,
                string rankOneDelta,
                Func<int, float> rankOneCoefficient,
                string name)
            {
                if (weights == null || outputs == null || coefficient == null)
                    throw new ArgumentNullException(name);
                if (!string.IsNullOrEmpty(rankOneDelta) && rankOneCoefficient == null)
                    throw new InvalidOperationException(
                        $"{name} requires coefficients for its rank-one correction.");

                var tree = Direct(name);
                var children = new List<ChildMotion>(weights.Count + 2);
                for (var row = 0; row < weights.Count; row++)
                {
                    var rowIndex = row;
                    var values = outputs
                        .Select((output, column) => new KeyValuePair<string, float>(
                            output, coefficient(rowIndex, column)))
                        .Where(pair => Mathf.Abs(pair.Value) >= 1e-8f)
                        .ToArray();
                    children.Add(new ChildMotion
                    {
                        motion = values.Length == 0
                            ? EmptyClip()
                            : MultiSetter($"{name} row {row}", values),
                        directBlendParameter = weights[row],
                        timeScale = 1f
                    });
                }
                if (!string.IsNullOrEmpty(rankOneDelta))
                {
                    const float signedBound = 2f;
                    var negative = outputs
                        .Select((output, column) => new KeyValuePair<string, float>(
                            output, -signedBound * rankOneCoefficient(column)))
                        .Where(pair => Mathf.Abs(pair.Value) >= 1e-8f)
                        .ToArray();
                    var positive = outputs
                        .Select((output, column) => new KeyValuePair<string, float>(
                            output, signedBound * rankOneCoefficient(column)))
                        .Where(pair => Mathf.Abs(pair.Value) >= 1e-8f)
                        .ToArray();
                    if (negative.Length > 0 || positive.Length > 0)
                    {
                        var zero = negative.Select(pair =>
                            new KeyValuePair<string, float>(pair.Key, 0f));
                        var correction = OneDimensional(
                            name + " rank-one correction", rankOneDelta,
                            new[]
                            {
                                Child(MultiSetter(name + " rank-one negative", negative),
                                    -signedBound),
                                Child(MultiSetter(name + " rank-one zero", zero), 0f),
                                Child(MultiSetter(name + " rank-one positive", positive),
                                    signedBound)
                            });
                        children.Add(new ChildMotion
                        {
                            motion = correction,
                            directBlendParameter = AlwaysOne,
                            timeScale = 1f
                        });
                    }
                }
                children.Add(new ChildMotion
                {
                    motion = MultiSetter(
                        name + " safety zero",
                        outputs.Select((output, column) => (output, column))
                            .Where(item =>
                                !UseUnconditionalSignedBindingProofForTests ||
                                !Enumerable.Range(0, weights.Count).All(row =>
                                    Mathf.Abs(coefficient(row, item.column)) >= 1e-8f) &&
                                (string.IsNullOrEmpty(rankOneDelta) ||
                                 Mathf.Abs(rankOneCoefficient(item.column)) < 1e-8f))
                            .Select(item =>
                                new KeyValuePair<string, float>(
                                    item.output, 0f))),
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
                tree.children = children.ToArray();
                return tree;
            }

            public Motion DenseSimplexMatrixProjection(
                IReadOnlyList<string> weights,
                IReadOnlyList<string> outputs,
                Func<int, int, float> coefficient,
                string rankOneDelta,
                Func<int, float> rankOneCoefficient,
                string name)
            {
                if (weights == null || outputs == null || coefficient == null)
                    throw new ArgumentNullException(name);
                if (!string.IsNullOrEmpty(rankOneDelta) && rankOneCoefficient == null)
                    throw new InvalidOperationException(
                        $"{name} requires coefficients for its rank-one correction.");

                // Unlike the general simplex projector, every dense row writes
                // every output. Since the observer weights remain an exact
                // nonnegative simplex, one row is always authoritative and the
                // AlwaysOne zero binder would only add live curves.
                var tree = Direct(name);
                var children = new List<ChildMotion>(weights.Count + 1);
                for (var row = 0; row < weights.Count; row++)
                {
                    var rowIndex = row;
                    children.Add(new ChildMotion
                    {
                        motion = MultiSetter(
                            $"{name} row {row}",
                            outputs.Select((output, column) =>
                                new KeyValuePair<string, float>(
                                    output, coefficient(rowIndex, column)))),
                        directBlendParameter = weights[row],
                        timeScale = 1f
                    });
                }

                if (!string.IsNullOrEmpty(rankOneDelta))
                {
                    const float signedBound = 2f;
                    var negative = outputs.Select((output, column) =>
                        new KeyValuePair<string, float>(
                            output, -signedBound * rankOneCoefficient(column))).ToArray();
                    var zero = outputs.Select(output =>
                        new KeyValuePair<string, float>(output, 0f)).ToArray();
                    var positive = outputs.Select((output, column) =>
                        new KeyValuePair<string, float>(
                            output, signedBound * rankOneCoefficient(column))).ToArray();
                    children.Add(new ChildMotion
                    {
                        motion = OneDimensional(
                            name + " rank-one correction", rankOneDelta,
                            new[]
                            {
                                Child(MultiSetter(name + " rank-one negative", negative),
                                    -signedBound),
                                Child(MultiSetter(name + " rank-one zero", zero), 0f),
                                Child(MultiSetter(name + " rank-one positive", positive),
                                    signedBound)
                            }),
                        directBlendParameter = AlwaysOne,
                        timeScale = 1f
                    });
                }
                tree.children = children.ToArray();
                return tree;
            }

            public Motion SignedMatrixProjection(
                IReadOnlyList<string> inputs,
                IReadOnlyList<string> outputs,
                IReadOnlyList<float> constants,
                Func<int, int, float> coefficient,
                string name)
            {
                if (inputs == null || outputs == null || constants == null || coefficient == null)
                    throw new ArgumentNullException(name);
                if (outputs.Count != constants.Count)
                    throw new InvalidOperationException(
                        $"{name} requires one affine constant per output.");

                const float signedBound = 2f;
                var tree = Direct(name);
                var signedCarrier = Enumerable.Range(0, outputs.Count)
                    .Select(column => Enumerable.Range(0, inputs.Count).Any(inputIndex =>
                        Mathf.Abs(coefficient(inputIndex, column)) >= 1e-8f))
                    .ToArray();
                var children = new List<ChildMotion>(inputs.Count + 1)
                {
                    new ChildMotion
                    {
                        motion = MultiSetter(
                            name + " affine base",
                            outputs.Select((output, column) =>
                                    (output, column))
                                .Where(item =>
                                    !UseUnconditionalSignedBindingProofForTests ||
                                    constants[item.column] != 0f ||
                                    !signedCarrier[item.column])
                                .Select(item =>
                                    new KeyValuePair<string, float>(
                                        item.output, constants[item.column]))),
                        directBlendParameter = AlwaysOne,
                        timeScale = 1f
                    }
                };

                for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
                {
                    var row = inputIndex;
                    var negativeValues = outputs
                        .Select((output, column) => new KeyValuePair<string, float>(
                            output, -signedBound * coefficient(row, column)))
                        .Where(pair => Mathf.Abs(pair.Value) >= 1e-8f)
                        .ToArray();
                    var positiveValues = outputs
                        .Select((output, column) => new KeyValuePair<string, float>(
                            output, signedBound * coefficient(row, column)))
                        .Where(pair => Mathf.Abs(pair.Value) >= 1e-8f)
                        .ToArray();
                    if (negativeValues.Length == 0 && positiveValues.Length == 0) continue;

                    var signed = OneDimensional(
                        $"{name} signed row {inputIndex}", inputs[inputIndex],
                        new[]
                        {
                            Child(MultiSetter($"{name} row {inputIndex} negative", negativeValues),
                                -signedBound),
                            Child(MultiSetter(
                                    $"{name} row {inputIndex} zero",
                                    negativeValues.Select(pair =>
                                        new KeyValuePair<string, float>(pair.Key, 0f))),
                                0f),
                            Child(MultiSetter($"{name} row {inputIndex} positive", positiveValues),
                                signedBound)
                        });
                    children.Add(new ChildMotion
                    {
                        motion = signed,
                        directBlendParameter = AlwaysOne,
                        timeScale = 1f
                    });
                }
                tree.children = children.ToArray();
                return tree;
            }

            public Motion GroupedElementwiseProducts(
                IReadOnlyList<string> nonNegativeWeights,
                string[,] inputs,
                string[,] outputs,
                string name)
            {
                if (nonNegativeWeights == null || inputs == null || outputs == null)
                    throw new ArgumentNullException(name);
                if (inputs.GetLength(0) != nonNegativeWeights.Count ||
                    outputs.GetLength(0) != nonNegativeWeights.Count ||
                    inputs.GetLength(1) != outputs.GetLength(1))
                    throw new InvalidOperationException(
                        $"{name} requires matching weight, input, and output dimensions.");

                var allOutputs = new List<string>();
                var tree = Direct(name);
                var children = new List<ChildMotion>();
                for (var group = 0; group < nonNegativeWeights.Count; group++)
                {
                    var vector = Direct($"{name} group {group}");
                    var vectorChildren = new List<ChildMotion>();
                    for (var column = 0; column < inputs.GetLength(1); column++)
                    {
                        var output = outputs[group, column];
                        allOutputs.Add(output);
                        vectorChildren.Add(new ChildMotion
                        {
                            motion = Copy(inputs[group, column], output, true),
                            directBlendParameter = AlwaysOne,
                            timeScale = 1f
                        });
                    }
                    // The enclosing product owns the unconditional zero binder
                    // for every output. Repeating it inside this weighted group
                    // contributes only w*0 and samples an otherwise dead clip.
                    vector.children = vectorChildren.ToArray();
                    children.Add(new ChildMotion
                    {
                        motion = vector,
                        directBlendParameter = nonNegativeWeights[group],
                        timeScale = 1f
                    });
                }
                children.Add(new ChildMotion
                {
                    motion = MultiSetter(
                        name + " safety zero",
                        allOutputs.Select(output => new KeyValuePair<string, float>(output, 0f))),
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
                tree.children = children.ToArray();
                return tree;
            }

            public Motion WeightedSignedMatrixAccumulator(
                IReadOnlyList<string> signedInputs,
                string[,] nonNegativeWeights,
                IReadOnlyList<string> outputs,
                Func<int, int, float> coefficient,
                string name)
            {
                if (signedInputs == null || nonNegativeWeights == null ||
                    outputs == null || coefficient == null)
                    throw new ArgumentNullException(name);
                if (nonNegativeWeights.GetLength(0) != signedInputs.Count ||
                    nonNegativeWeights.GetLength(1) != outputs.Count)
                    throw new InvalidOperationException(
                        $"{name} requires one nonnegative weight per input/output pair.");

                // Each child evaluates c(i,o) * input[i] and the enclosing
                // Direct weight supplies the nonnegative simplex mixture. This
                // publishes one accumulated parameter per output without first
                // materializing every scalar product as its own AAP.
                const float signedBound = 2f;
                var tree = Direct(name);
                var children = new List<ChildMotion>(
                    signedInputs.Count * outputs.Count + 1);
                for (var inputIndex = 0; inputIndex < signedInputs.Count; inputIndex++)
                for (var outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
                {
                    var scale = coefficient(inputIndex, outputIndex);
                    if (Mathf.Abs(scale) < 1e-8f) continue;
                    children.Add(new ChildMotion
                    {
                        motion = Map(
                            signedInputs[inputIndex],
                            outputs[outputIndex],
                            new[]
                            {
                                Point(-signedBound, -signedBound * scale),
                                Point(0f, 0f),
                                Point(signedBound, signedBound * scale)
                            }),
                        directBlendParameter = nonNegativeWeights[
                            inputIndex, outputIndex],
                        timeScale = 1f
                    });
                }
                children.Add(new ChildMotion
                {
                    motion = MultiSetter(
                        name + " safety zero",
                        outputs.Select(output =>
                            new KeyValuePair<string, float>(output, 0f))),
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
                tree.children = children.ToArray();
                return tree;
            }

            public Motion CopyArticulationVector(
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> inputs,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> outputs,
                string name,
                bool includeBaseline = true)
            {
                if (inputs == null || outputs == null ||
                    outputs.Keys.Any(key => !inputs.ContainsKey(key)))
                    throw new InvalidOperationException(
                        $"{name} requires matching articulation vectors.");
                var ordered = outputs.OrderBy(pair => (int)pair.Key).ToArray();
                var tree = Direct(name);
                var children = new List<ChildMotion>(ordered.Length + 1);
                foreach (var pair in ordered)
                {
                    var input = inputs[pair.Key];
                    if (IsSigned(pair.Key))
                        children.Add(new ChildMotion
                        {
                            motion = Copy(input, pair.Value, true),
                            directBlendParameter = AlwaysOne,
                            timeScale = 1f
                        });
                    else
                        children.Add(new ChildMotion
                        {
                            motion = Setter(pair.Value, 1f),
                            directBlendParameter = input,
                            timeScale = 1f
                        });
                }
                if (includeBaseline) children.Add(new ChildMotion
                {
                    motion = MultiSetter(
                        name + " safety zero",
                        ordered.Where(pair =>
                                !UseUnconditionalSignedBindingProofForTests ||
                                !IsSigned(pair.Key))
                            .Select(pair =>
                                new KeyValuePair<string, float>(pair.Value, 0f))),
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
                tree.children = children.ToArray();
                return tree;
            }

            public Motion ScaleArticulationVector(
                string nonNegativeWeight,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> inputs,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> outputs,
                string name)
            {
                if (inputs == null || outputs == null ||
                    outputs.Keys.Any(key => !inputs.ContainsKey(key)))
                    throw new InvalidOperationException(
                        $"{name} requires matching articulation vectors.");

                var tree = Direct(name);
                tree.children = new[]
                {
                    new ChildMotion
                    {
                        motion = CopyArticulationVector(
                            inputs, outputs, name + " values", false),
                        directBlendParameter = nonNegativeWeight,
                        timeScale = 1f
                    },
                    new ChildMotion
                    {
                        motion = MultiSetter(
                            name + " safety zero",
                            outputs.Values.Select(output =>
                                new KeyValuePair<string, float>(output, 0f))),
                        directBlendParameter = AlwaysOne,
                        timeScale = 1f
                    }
                };
                return tree;
            }

            public Motion DifferenceArticulationVector(
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> positive,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> negative,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> outputs,
                float scale,
                string name)
            {
                if (positive == null || negative == null || outputs == null ||
                    outputs.Keys.Any(key =>
                        !positive.ContainsKey(key) || !negative.ContainsKey(key)))
                    throw new InvalidOperationException(
                        $"{name} requires matching articulation vectors.");

                var tree = Direct(name);
                var children = new List<ChildMotion>();
                foreach (var pair in outputs.OrderBy(pair => (int)pair.Key))
                {
                    var articulator = pair.Key;
                    var output = pair.Value;
                    if (IsSigned(articulator))
                    {
                        children.Add(new ChildMotion
                        {
                            motion = Map(positive[articulator], output, new[]
                            {
                                Point(-2f, -2f * scale), Point(0f, 0f),
                                Point(2f, 2f * scale)
                            }),
                            directBlendParameter = AlwaysOne,
                            timeScale = 1f
                        });
                        children.Add(new ChildMotion
                        {
                            motion = Map(negative[articulator], output, new[]
                            {
                                Point(-2f, 2f * scale), Point(0f, 0f),
                                Point(2f, -2f * scale)
                            }),
                            directBlendParameter = AlwaysOne,
                            timeScale = 1f
                        });
                    }
                    else
                    {
                        children.Add(new ChildMotion
                        {
                            motion = Setter(output, scale),
                            directBlendParameter = positive[articulator],
                            timeScale = 1f
                        });
                        children.Add(new ChildMotion
                        {
                            motion = Setter(output, -scale),
                            directBlendParameter = negative[articulator],
                            timeScale = 1f
                        });
                    }
                }
                children.Add(new ChildMotion
                {
                    motion = MultiSetter(
                        name + " safety zero",
                        outputs.Where(pair =>
                                !UseUnconditionalSignedBindingProofForTests ||
                                !IsSigned(pair.Key))
                            .Select(pair =>
                                new KeyValuePair<string, float>(pair.Value, 0f))),
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
                tree.children = children.ToArray();
                return tree;
            }

            public Motion InterpolateArticulationVector(
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> from,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> to,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> outputs,
                string weight,
                string name)
            {
                if (from == null || to == null || outputs == null ||
                    outputs.Keys.Any(key =>
                        !from.ContainsKey(key) || !to.ContainsKey(key)))
                    throw new InvalidOperationException(
                        $"{name} requires matching articulation vectors.");

                return OneDimensional(
                    name, weight,
                    new[]
                    {
                        Child(CopyArticulationVector(
                            from, outputs, name + " slow"), 0f),
                        Child(CopyArticulationVector(
                            to, outputs, name + " fast"), 1f)
                    });
            }

            public Motion BlendThreeArticulationVectors(
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> low,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> configured,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> high,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> outputs,
                string weight,
                string name)
            {
                if (low == null || configured == null || high == null ||
                    outputs == null || outputs.Keys.Any(key =>
                        !low.ContainsKey(key) || !configured.ContainsKey(key) ||
                        !high.ContainsKey(key)))
                    throw new InvalidOperationException(
                        $"{name} requires matching articulation vectors.");

                return OneDimensional(
                    name, weight,
                    new[]
                    {
                        Child(CopyArticulationVector(
                            low, outputs, name + " calm"), -1f),
                        Child(CopyArticulationVector(
                            configured, outputs,
                            name + " configured"), 0f),
                        Child(CopyArticulationVector(
                            high, outputs, name + " crisp"), 1f)
                    });
            }

            public Motion InterpolateAffineArticulationVector(
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> from,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> to,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> outputs,
                string weight,
                IReadOnlyDictionary<AdvancedVisemeArticulator, float> offsets,
                IReadOnlyDictionary<AdvancedVisemeArticulator, float> scales,
                string name)
            {
                if (from == null || to == null || outputs == null ||
                    offsets == null || scales == null ||
                    outputs.Keys.Any(key =>
                        !from.ContainsKey(key) || !to.ContainsKey(key) ||
                        !offsets.ContainsKey(key) || !scales.ContainsKey(key)))
                    throw new InvalidOperationException(
                        $"{name} requires matching affine articulation vectors.");

                return OneDimensional(
                    name, weight,
                    new[]
                    {
                        Child(DecodeAffineArticulationVector(
                            from, outputs, offsets, scales, name + " slow"), 0f),
                        Child(DecodeAffineArticulationVector(
                            to, outputs, offsets, scales, name + " fast"), 1f)
                    });
            }

            public Motion DecodeAffineArticulationVector(
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> inputs,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> outputs,
                IReadOnlyDictionary<AdvancedVisemeArticulator, float> offsets,
                IReadOnlyDictionary<AdvancedVisemeArticulator, float> scales,
                string name)
            {
                var ordered = outputs.OrderBy(pair => (int)pair.Key).ToArray();
                var tree = Direct(name);
                var children = new List<ChildMotion>(ordered.Length + 1);
                foreach (var pair in ordered)
                {
                    children.Add(new ChildMotion
                    {
                        motion = Setter(pair.Value, scales[pair.Key]),
                        directBlendParameter = inputs[pair.Key],
                        timeScale = 1f
                    });
                }
                // This is both the affine offset and the deterministic zero
                // binder for rows whose minimum happens to be zero.
                children.Add(new ChildMotion
                {
                    motion = MultiSetter(
                        name + " affine baseline",
                        ordered.Select(pair => new KeyValuePair<string, float>(
                            pair.Value, offsets[pair.Key]))),
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
                tree.children = children.ToArray();
                return tree;
            }

            public Motion WeightedSetter(string weight, string output, float value)
            {
                var tree = Direct($"{output} <- {weight} * {value:0.###}");
                tree.children = new[]
                {
                    new ChildMotion { motion = Setter(output, value), directBlendParameter = weight, timeScale = 1f },
                    new ChildMotion { motion = Setter(output, 0f), directBlendParameter = AlwaysOne, timeScale = 1f }
                };
                return tree;
            }

            public Motion Map(string input, string output, IReadOnlyList<(float input, float output)> points)
            {
                var orderedPoints = points.OrderBy(p => p.input).ToArray();
                var tree = new BlendTree
                {
                    name = $"Map {input} -> {output}",
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = input,
                    useAutomaticThresholds = false
                };
                SubAsset(tree);
                tree.children = orderedPoints.Select(p => new ChildMotion
                {
                    motion = Setter(output, p.output), threshold = p.input, timeScale = 1f
                }).ToArray();
                mapDescriptors[tree] = new MapDescriptor
                {
                    input = input,
                    output = output,
                    points = orderedPoints
                };
                return tree;
            }

            public Motion SparsifyNonnegativeVector(
                IReadOnlyList<string> inputs,
                IReadOnlyList<string> outputs,
                float epsilon,
                string name)
            {
                if (inputs == null || outputs == null ||
                    inputs.Count != outputs.Count)
                    throw new InvalidOperationException(
                        $"{name} requires equally sized input and output vectors.");
                epsilon = Mathf.Clamp(epsilon, 0f, 0.1f);
                var root = Direct(name);
                root.children = inputs.Select((input, index) => new ChildMotion
                {
                    // A Simple1D tree clamps below its first threshold. Two knots
                    // therefore implement max(0, (x-e)/(1-e)) exactly on [0,1].
                    motion = Map(input, outputs[index], new[]
                    {
                        Point(epsilon, 0f),
                        Point(1f, 1f)
                    }),
                    directBlendParameter = AlwaysOneParameter,
                    timeScale = 1f
                }).ToArray();
                return root;
            }

            public Motion EqualFloat(string input, string output, int value)
            {
                return Map(input, output, new[]
                {
                    Point(value - 0.001f, 0f), Point(value, 1f), Point(value + 0.001f, 0f)
                });
            }

            public Motion AlphaFromDeltaTime(string deltaTime, string output, float responseSeconds)
            {
                if (!alphaBatches.TryGetValue(deltaTime, out var batch))
                {
                    var samples = new[]
                    {
                        0f, 1f / 240f, 1f / 144f, 1f / 90f, 1f / 60f,
                        1f / 45f, 1f / 30f, 1f / 20f, 0.1f, 0.25f
                    };
                    var clips = samples.Select((_, index) =>
                        Clip($"Frame-rate alpha sample {index}")).ToArray();
                    batch = new AlphaBatch
                    {
                        samples = samples,
                        clips = clips,
                        tree = OneDimensional(
                            "Frame-rate-correct alpha vector", deltaTime,
                            samples.Select((sample, index) =>
                                Child(clips[index], sample)).ToArray())
                    };
                    alphaBatches[deltaTime] = batch;
                }

                var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), output);
                for (var index = 0; index < batch.samples.Length; index++)
                {
                    var value = AdvancedVisemeMath.Alpha(
                        batch.samples[index], responseSeconds);
                    AnimationUtility.SetEditorCurve(
                        batch.clips[index], binding,
                        AnimationCurve.Constant(0f, 0f, value));
                }
                return batch.tree;
            }

            public Motion Smooth(string target, string output, string alpha, bool signed)
            {
                var tree = new BlendTree
                {
                    name = $"Smooth {output} toward {target}",
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = alpha,
                    useAutomaticThresholds = false,
                    children = new[]
                    {
                        new ChildMotion { motion = Copy(output, output, signed), threshold = 0f, timeScale = 1f },
                        new ChildMotion { motion = Copy(target, output, signed), threshold = 1f, timeScale = 1f }
                    }
                };
                SubAsset(tree);
                parameterBlendDescriptors[tree] = new ParameterBlendDescriptor
                {
                    driver = alpha,
                    output = output,
                    thresholds = new[] { 0f, 1f },
                    sources = new[] { output, target },
                    signed = signed
                };
                return tree;
            }

            /// <summary>
            /// Exact simplex retraction via Unity's native normalized Direct
            /// BlendTree: each child weight is divided by the sum of all
            /// weights and negative weights clamp at zero, so the outputs are
            /// the clamp-and-renormalized inputs bit-for-bit. Child i writes
            /// output i to one and every other output to zero, making the
            /// blended value of output i exactly w_i / sum(w).
            /// </summary>
            public Motion NormalizeVector(
                IReadOnlyList<string> inputs,
                IReadOnlyList<string> outputs,
                string name)
            {
                if (inputs == null || outputs == null ||
                    inputs.Count != outputs.Count || inputs.Count == 0)
                    throw new ArgumentException(
                        "NormalizeVector requires matching input/output vectors.");

                var children = new ChildMotion[inputs.Count];
                for (var i = 0; i < inputs.Count; i++)
                {
                    // Every child must bind EVERY output explicitly: a missing
                    // binding makes Unity blend that property with its default
                    // value, which breaks the exactness of the division. Build
                    // the clips directly so no shared-clip cache or zero-curve
                    // pruning can thin them out.
                    var clip = Clip($"{name} basis {i}");
                    for (var j = 0; j < outputs.Count; j++)
                        AnimationUtility.SetEditorCurve(
                            clip,
                            EditorCurveBinding.FloatCurve(
                                string.Empty, typeof(Animator), outputs[j]),
                            AnimationCurve.Constant(0f, 0f, j == i ? 1f : 0f));
                    children[i] = new ChildMotion
                    {
                        motion = clip,
                        directBlendParameter = inputs[i],
                        timeScale = 1f
                    };
                }
                var tree = new BlendTree
                {
                    name = name,
                    blendType = BlendTreeType.Direct,
                    children = children
                };
                SubAsset(tree);
                var serialized = new SerializedObject(tree);
                serialized.FindProperty("m_NormalizedBlendValues").boolValue =
                    true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                normalizedTrees.Add(tree);
                return tree;
            }

            public Motion SmoothVector(
                IReadOnlyList<string> targets,
                IReadOnlyList<string> outputs,
                string alpha,
                string name)
            {
                return InterpolateVector(outputs, targets, outputs, alpha, name);
            }

            public Motion SmoothVectorUnlessHeldSilence(
                IReadOnlyList<string> targets,
                IReadOnlyList<string> outputs,
                string alpha,
                string visemeIndex,
                string speechHistory,
                string stability,
                string name)
            {
                var release = SmoothVector(targets, outputs, alpha, name + " release");
                var freeze = CopyVector(outputs, outputs, name + " freeze");
                return SelectSilenceHold(
                    release, release, freeze,
                    visemeIndex, speechHistory, stability,
                    name + " transient-sil hold");
            }

            public Motion SmoothVectorTowardSelectedConstantsUnlessHeldSilence(
                string selector,
                IReadOnlyList<string> targetWeights,
                IReadOnlyList<float[]> targetRows,
                IReadOnlyList<string> outputs,
                string alpha,
                string speechHistory,
                string stability,
                string name)
            {
                if (targetWeights == null || targetRows == null || outputs == null ||
                    targetWeights.Count != VisemeReconstructionProfile.VisemeCount ||
                    targetRows.Count != VisemeReconstructionProfile.VisemeCount ||
                    targetRows.Any(row => row == null || row.Length != outputs.Count))
                    throw new InvalidOperationException(
                        $"{name} requires one complete constant row per viseme.");

                // The decoded one-hot weights and the former dense target vector
                // were authored by the same decoder state, so selecting the
                // immutable table row here observes the identical AAP epoch. One
                // active multi-output clip replaces dense scalar target copies.
                var selectedTarget = Direct(name + " target row");
                var targetChildren = targetRows.Select((row, index) =>
                    new ChildMotion
                    {
                        motion = MultiSetter(
                            name + " " +
                            VisemeReconstructionProfile.VisemeNames[index] +
                            " target",
                            outputs.Select((output, column) =>
                                new KeyValuePair<string, float>(
                                    output, row[column]))),
                        directBlendParameter = targetWeights[index],
                        timeScale = 1f
                    }).ToList();
                targetChildren.Add(new ChildMotion
                {
                    // Keep zero coefficients explicit after constant-curve
                    // pruning, matching CopyVector's binding/default semantics.
                    motion = MultiSetter(
                        name + " target safety zero",
                        outputs.Select(output =>
                            new KeyValuePair<string, float>(output, 0f))),
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
                selectedTarget.children = targetChildren.ToArray();
                var release = InterpolateMotions(
                    CopyVector(outputs, outputs, name + " feedback"),
                    selectedTarget, alpha, name + " release");
                var freeze = CopyVector(outputs, outputs, name + " freeze");
                return SelectSilenceHold(
                    release, release, freeze,
                    selector, speechHistory, stability,
                    name + " transient-sil hold");
            }

            public Motion AsymmetricBinarySmooth(
                string binary,
                string output,
                float targetWhenZero,
                string alphaWhenZero,
                float targetWhenOne,
                string alphaWhenOne,
                bool signed)
            {
                var tree = OneDimensional(
                    $"Asymmetric smooth {output} by {binary}", binary,
                    new[]
                    {
                        Child(SmoothConstant(targetWhenZero, output, alphaWhenZero, signed), 0f),
                        Child(SmoothConstant(targetWhenOne, output, alphaWhenOne, signed), 1f)
                    });
                return tree;
            }

            public Motion SmoothUnlessHeldSilence(
                string target,
                string output,
                string alpha,
                string visemeIndex,
                string speechHistory,
                string stability,
                bool signed)
            {
                var release = Smooth(target, output, alpha, signed);
                var freeze = Copy(output, output, signed);
                return SelectSilenceHold(
                    release, release, freeze,
                    visemeIndex, speechHistory, stability,
                    $"Hold {output} across transient sil");
            }

            public Motion InterpolateUnlessHeldSilence(
                string from,
                string to,
                string output,
                string weight,
                string visemeIndex,
                string speechHistory,
                string stability,
                bool signed)
            {
                var release = Interpolate(from, to, output, weight, signed);
                var freeze = Copy(output, output, signed);
                return SelectSilenceHold(
                    release, release, freeze,
                    visemeIndex, speechHistory, stability,
                    $"Hold {output} coarticulation across transient sil");
            }

            public Motion MultiplyUnlessHeldSilence(
                string nonNegativeWeight,
                string value,
                string output,
                string visemeIndex,
                string speechHistory,
                string stability,
                bool valueSigned)
            {
                var release = Multiply(
                    nonNegativeWeight, value, output, valueSigned);
                var freeze = Copy(output, output, valueSigned);
                return SelectSilenceHold(
                    release, release, freeze,
                    visemeIndex, speechHistory, stability,
                    $"Hold {output} gain across transient sil");
            }

            public Motion SmoothActivityWithSilenceHold(
                string visemeIndex,
                string speechHistory,
                string stability,
                string output,
                string attackAlpha,
                string releaseAlpha)
            {
                var active = SmoothConstant(1f, output, attackAlpha, false);
                var inactive = SmoothConstant(0f, output, releaseAlpha, false);
                return SelectSilenceHold(
                    active, inactive, active,
                    visemeIndex, speechHistory, stability,
                    $"Speech activity with transient-sil hold -> {output}");
            }

            public Motion Interpolate(string from, string to, string output, string weight, bool signed)
            {
                var tree = new BlendTree
                {
                    name = $"Interpolate {from} -> {to}",
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = weight,
                    useAutomaticThresholds = false,
                    children = new[]
                    {
                        new ChildMotion { motion = Copy(from, output, signed), threshold = 0f, timeScale = 1f },
                        new ChildMotion { motion = Copy(to, output, signed), threshold = 1f, timeScale = 1f }
                    }
                };
                SubAsset(tree);
                parameterBlendDescriptors[tree] = new ParameterBlendDescriptor
                {
                    driver = weight,
                    output = output,
                    thresholds = new[] { 0f, 1f },
                    sources = new[] { from, to },
                    signed = signed
                };
                return tree;
            }

            public Motion BlendThreeParameters(
                string low,
                string configured,
                string high,
                string output,
                string weight,
                bool signed,
                string name)
            {
                var tree = OneDimensional(
                    name, weight,
                    new[]
                    {
                        Child(Copy(low, output, signed), 0f),
                        Child(Copy(configured, output, signed), 0.5f),
                        Child(Copy(high, output, signed), 1f)
                    });
                parameterBlendDescriptors[tree] = new ParameterBlendDescriptor
                {
                    driver = weight,
                    output = output,
                    thresholds = new[] { 0f, 0.5f, 1f },
                    sources = new[] { low, configured, high },
                    signed = signed
                };
                return tree;
            }

            public Motion InterpolateVector(
                IReadOnlyList<string> from,
                IReadOnlyList<string> to,
                IReadOnlyList<string> outputs,
                string weight,
                string name)
            {
                if (from == null || to == null || outputs == null ||
                    from.Count != to.Count || from.Count != outputs.Count)
                    throw new InvalidOperationException(
                        $"{name} requires equally sized vectors.");
                return OneDimensional(
                    name, weight,
                    new[]
                    {
                        Child(CopyVector(from, outputs, name + " from"), 0f),
                        Child(CopyVector(to, outputs, name + " to"), 1f)
                    });
            }

            public Motion BlendThreeVectors(
                IReadOnlyList<string> low,
                IReadOnlyList<string> configured,
                IReadOnlyList<string> high,
                IReadOnlyList<string> outputs,
                string weight,
                string name)
            {
                if (low == null || configured == null || high == null ||
                    outputs == null || low.Count != configured.Count ||
                    low.Count != high.Count || low.Count != outputs.Count)
                    throw new InvalidOperationException(
                        $"{name} requires four equally sized vectors.");
                return OneDimensional(
                    name, weight,
                    new[]
                    {
                        Child(CopyVector(low, outputs, name + " calm"), -1f),
                        Child(CopyVector(configured, outputs,
                            name + " configured"), 0f),
                        Child(CopyVector(high, outputs, name + " crisp"), 1f)
                    });
            }

            public Motion InterpolateMotions(
                Motion from,
                Motion to,
                string weight,
                string name)
            {
                return OneDimensional(
                    name, weight,
                    new[]
                    {
                        Child(from ?? EmptyClip(), 0f),
                        Child(to ?? EmptyClip(), 1f)
                    });
            }

            public Motion SelectSilenceHoldMotion(
                Motion nonSilence,
                Motion silenceRelease,
                Motion silenceHold,
                string visemeIndex,
                string speechHistory,
                string stability,
                string name)
            {
                return SelectSilenceHold(
                    nonSilence, silenceRelease, silenceHold,
                    visemeIndex, speechHistory, stability, name);
            }

            public Motion InterpolateVectorUnlessHeldSilence(
                IReadOnlyList<string> from,
                IReadOnlyList<string> to,
                IReadOnlyList<string> outputs,
                string weight,
                string visemeIndex,
                string speechHistory,
                string stability,
                string name)
            {
                var release = InterpolateVector(from, to, outputs, weight, name + " release");
                var freeze = CopyVector(outputs, outputs, name + " freeze");
                return SelectSilenceHold(
                    release, release, freeze,
                    visemeIndex, speechHistory, stability,
                    name + " transient-sil hold");
            }

            public Motion CopyVectorUnlessHeldSilence(
                IReadOnlyList<string> inputs,
                IReadOnlyList<string> outputs,
                string visemeIndex,
                string speechHistory,
                string stability,
                string name)
            {
                var release = CopyVector(inputs, outputs, name + " release");
                var freeze = CopyVector(outputs, outputs, name + " freeze");
                return SelectSilenceHold(
                    release, release, freeze,
                    visemeIndex, speechHistory, stability,
                    name + " transient-sil hold");
            }

            private Motion SmoothConstant(
                float target,
                string output,
                string alpha,
                bool signed)
            {
                return OneDimensional(
                    $"Smooth {output} toward {target:0.###}", alpha,
                    new[]
                    {
                        Child(Copy(output, output, signed), 0f),
                        Child(Setter(output, target), 1f)
                    });
            }

            private Motion SelectSilenceHold(
                Motion nonSilence,
                Motion silenceRelease,
                Motion silenceHold,
                string visemeIndex,
                string speechHistory,
                string stability,
                string name)
            {
                var byHistory = OneDimensional(
                    name + " (history)", speechHistory,
                    new[]
                    {
                        Child(silenceRelease, AdvancedVisemeMath.SpeechHistoryHoldStart),
                        Child(silenceHold, AdvancedVisemeMath.SpeechHistoryHoldFull)
                    });

                // Silence Stability is centered: zero is an exact bypass, the
                // default midpoint applies the complete configured hold, and the
                // upper half extends its release response without exceeding full
                // authority. Encoding this choice inside the same Motion avoids a
                // sibling enable parameter and its extra Animator-frame latency.
                var byStability = OneDimensional(
                    name + " (strength)", stability,
                    new[]
                    {
                        Child(silenceRelease, 0f),
                        Child(byHistory, 0.5f)
                    });

                var tree = OneDimensional(
                    name, visemeIndex,
                    new[]
                    {
                        Child(byStability, 0f),
                        Child(nonSilence, 1f)
                    });
                silenceHoldDescriptors[tree] = new SilenceHoldDescriptor
                {
                    viseme = visemeIndex,
                    history = speechHistory,
                    stability = stability,
                    nonSilence = nonSilence,
                    silenceRelease = silenceRelease,
                    silenceHold = silenceHold,
                    identityHold = ReferenceEquals(nonSilence, silenceRelease)
                };
                return tree;
            }

            private BlendTree OneDimensional(
                string name,
                string parameter,
                ChildMotion[] children)
            {
                var tree = new BlendTree
                {
                    name = name,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = parameter,
                    useAutomaticThresholds = false,
                    children = children
                };
                SubAsset(tree);
                return tree;
            }

            private static ChildMotion Child(Motion motion, float threshold)
            {
                return new ChildMotion
                {
                    motion = motion,
                    threshold = threshold,
                    timeScale = 1f
                };
            }

            public Motion Linear(string output, IEnumerable<Term> terms)
            {
                var tree = Direct("Linear -> " + output);
                var children = new List<ChildMotion>();
                var hasUnconditionalBinding = false;
                foreach (var term in terms)
                {
                    if (term.constant)
                    {
                        hasUnconditionalBinding = true;
                        children.Add(new ChildMotion
                        {
                            motion = Setter(output, term.multiplier),
                            directBlendParameter = AlwaysOne,
                            timeScale = 1f
                        });
                    }
                    else if (term.signed)
                    {
                        if (UseUnconditionalSignedBindingProofForTests)
                        {
                            // Every reachable knot in Map binds output, and the
                            // map is weighted by the invariant AlwaysOne. It is
                            // therefore a constructive all-domain zero binder as
                            // well as the signed term itself.
                            hasUnconditionalBinding = true;
                        }
                        children.Add(new ChildMotion
                        {
                            motion = Map(term.parameter, output, new[]
                            {
                                Point(-2f, -2f * term.multiplier), Point(0f, 0f),
                                Point(2f, 2f * term.multiplier)
                            }),
                            directBlendParameter = AlwaysOne,
                            timeScale = 1f
                        });
                    }
                    else
                    {
                        // A Direct BlendTree's weight is already the nonnegative
                        // input parameter. Wrapping this clip in WeightedSetter and
                        // then weighting that tree by AlwaysOne emitted a redundant
                        // scalar tree for every affine coefficient.
                        children.Add(new ChildMotion
                        {
                            motion = Setter(output, term.multiplier),
                            directBlendParameter = term.parameter,
                            timeScale = 1f
                        });
                    }
                }
                if (!hasUnconditionalBinding)
                    children.Add(new ChildMotion
                    {
                        motion = Setter(output, 0f),
                        directBlendParameter = AlwaysOne,
                        timeScale = 1f
                    });
                tree.children = children.ToArray();
                return tree;
            }

            public Motion Multiply(string nonNegativeWeight, string value, string output, bool valueSigned)
            {
                var tree = Direct($"Multiply {nonNegativeWeight} * {value} -> {output}");
                Motion valueMotion;
                if (valueSigned)
                {
                    valueMotion = Copy(value, output, true);
                }
                else
                {
                    // The outer tree owns output's zero binder. Lower the inner
                    // nonnegative copy as the single term value*1; retaining the
                    // usual Copy baseline would evaluate weight*(value+0).
                    var term = Direct($"Baseline-free {value} -> {output}");
                    term.children = new[]
                    {
                        new ChildMotion
                        {
                            motion = Setter(output, 1f),
                            directBlendParameter = value,
                            timeScale = 1f
                        }
                    };
                    valueMotion = term;
                }
                tree.children = new[]
                {
                    new ChildMotion { motion = valueMotion, directBlendParameter = nonNegativeWeight, timeScale = 1f },
                    new ChildMotion { motion = Setter(output, 0f), directBlendParameter = AlwaysOne, timeScale = 1f }
                };
                return tree;
            }

            public Motion ScaleByInverseUnitWeight(
                string nonNegativeValue,
                string unitWeight,
                string output,
                float scale)
            {
                scale = Mathf.Max(0f, scale);
                var unsuppressed = Mathf.Approximately(scale, 1f)
                    ? Copy(nonNegativeValue, output, false)
                    : WeightedSetter(nonNegativeValue, output, scale);
                return OneDimensional(
                    $"Scale {nonNegativeValue} by inverse {unitWeight}",
                    unitWeight,
                    new[]
                    {
                        Child(unsuppressed, 0f),
                        Child(Setter(output, 0f), 1f)
                    });
            }

            public Motion Abs(string input, string output)
            {
                return Map(input, output, new[] { Point(-2f, 2f), Point(0f, 0f), Point(2f, 2f) });
            }

            public Motion Max(string a, string b, string output)
            {
                var diff = Param("Max/" + Sanitize(output) + "/Diff", 0f);
                var abs = Param("Max/" + Sanitize(output) + "/Abs", 0f);
                var tree = Direct("Max -> " + output);
                tree.children = new[]
                {
                    Child(Linear(diff, new[] { Term.Positive(a, 1f), Term.Positive(b, -1f) })),
                    Child(Abs(diff, abs)),
                    Child(Linear(output, new[] { Term.Positive(a, 0.5f), Term.Positive(b, 0.5f), Term.Positive(abs, 0.5f) }))
                };
                return tree;
            }

            public Motion Min(string a, string b, string output)
            {
                var diff = Param("Min/" + Sanitize(output) + "/Diff", 0f);
                var abs = Param("Min/" + Sanitize(output) + "/Abs", 0f);
                var tree = Direct("Min -> " + output);
                tree.children = new[]
                {
                    Child(Linear(diff, new[] { Term.Positive(a, 1f), Term.Positive(b, -1f) })),
                    Child(Abs(diff, abs)),
                    Child(Linear(output, new[] { Term.Positive(a, 0.5f), Term.Positive(b, 0.5f), Term.Positive(abs, -0.5f) }))
                };
                return tree;
            }

            public Motion DrivePose(string weight, Motion pose, bool signed)
            {
                if (!signed)
                {
                    var tree = Direct("Drive pose by " + weight);
                    tree.children = new[]
                    {
                        new ChildMotion { motion = pose, directBlendParameter = weight, timeScale = 1f },
                        new ChildMotion { motion = EmptyClip(), directBlendParameter = AlwaysOne, timeScale = 1f }
                    };
                    return tree;
                }

                if (ContainsBlendShapeCurves(pose))
                    throw new InvalidOperationException(
                        $"Signed pose '{pose?.name}' has no build-only inverse geometry. " +
                        "A negative final blendshape weight would be clamped by VRChat.");

                var negative = NegatedPose(pose);
                var zero = EmptyClip();
                var signedTree = new BlendTree
                {
                    name = "Signed pose " + weight,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = weight,
                    useAutomaticThresholds = false,
                    children = new[]
                    {
                        new ChildMotion { motion = negative, threshold = -1f, timeScale = 1f },
                        new ChildMotion { motion = zero, threshold = 0f, timeScale = 1f },
                        new ChildMotion { motion = pose, threshold = 1f, timeScale = 1f }
                    }
                };
                SubAsset(signedTree);
                return signedTree;
            }

            public Motion DriveSparsePose(
                string weight,
                Motion pose,
                float epsilon)
            {
                epsilon = Mathf.Clamp(epsilon, 0f, 0.1f);
                // This is the motion-domain form of SparsifyNonnegativeVector:
                // clamp below epsilon, then map [epsilon,1] to [0,1]. It avoids
                // publishing a second weight while retaining the same dead-zone,
                // so inactive exponential tails never evaluate the pose clip.
                return OneDimensional(
                    "Sparse pose by " + weight,
                    weight,
                    new[]
                    {
                        Child(EmptyClip(), epsilon),
                        Child(pose ?? EmptyClip(), 1f)
                    });
            }

            public Motion ScaleMotion(
                string weight,
                Motion motion,
                string name)
            {
                var tree = Direct(name);
                tree.children = new[]
                {
                    new ChildMotion
                    {
                        motion = motion ?? EmptyClip(),
                        directBlendParameter = weight,
                        timeScale = 1f
                    },
                    new ChildMotion
                    {
                        motion = EmptyClip(),
                        directBlendParameter = AlwaysOne,
                        timeScale = 1f
                    }
                };
                return tree;
            }

            public Motion DrivePoseProduct(
                string firstWeight,
                string secondWeight,
                Motion pose,
                string name)
            {
                // Both ownership weights are nonnegative. Nesting their Direct
                // weights evaluates g * projected geometry atomically and avoids
                // publishing a correction AAP that would add another Animator
                // frame before the carrier reaches the mesh.
                var tree = Direct(name + " product pose");
                tree.children = new[]
                {
                    new ChildMotion
                    {
                        motion = DrivePose(secondWeight, pose, false),
                        directBlendParameter = firstWeight,
                        timeScale = 1f
                    },
                    new ChildMotion
                    {
                        motion = EmptyClip(),
                        directBlendParameter = AlwaysOne,
                        timeScale = 1f
                    }
                };
                return tree;
            }

            public Motion DrivePose(string weight, Motion positive, Motion negative, bool signed)
            {
                if (!signed) return positive != null ? DrivePose(weight, positive, false) : EmptyClip();
                if (positive == null && negative == null) return EmptyClip();
                positive = positive ?? EmptyClip();
                if (negative == null && ContainsBlendShapeCurves(positive))
                    throw new InvalidOperationException(
                        $"Signed pose '{positive.name}' has no negative endpoint or build-only " +
                        "inverse geometry. A negative final blendshape weight would be clamped by VRChat.");
                negative = negative ?? NegatedPose(positive);
                var tree = new BlendTree
                {
                    name = "Signed pose " + weight,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = weight,
                    useAutomaticThresholds = false,
                    children = new[]
                    {
                        new ChildMotion { motion = negative, threshold = -1f, timeScale = 1f },
                        new ChildMotion { motion = EmptyClip(), threshold = 0f, timeScale = 1f },
                        new ChildMotion { motion = positive, threshold = 1f, timeScale = 1f }
                    }
                };
                SubAsset(tree);
                return tree;
            }

            public Motion DrivePoseAtThresholds(
                string input,
                Motion positive,
                Motion negative,
                float positiveThreshold,
                float negativeThreshold,
                bool signed)
            {
                var zero = EmptyClip();
                if (!signed)
                {
                    if (positive == null) return zero;
                    var threshold = Mathf.Max(1e-5f, positiveThreshold);
                    return OneDimensional(
                        "Native pose by " + input,
                        input,
                        new[]
                        {
                            Child(zero, 0f),
                            Child(positive, threshold)
                        });
                }

                var children = new List<ChildMotion>();
                if (negative != null)
                    children.Add(Child(
                        negative, Mathf.Min(-1e-5f, negativeThreshold)));
                else
                    children.Add(Child(zero, -1f));
                children.Add(Child(zero, 0f));
                if (positive != null)
                    children.Add(Child(
                        positive, Mathf.Max(1e-5f, positiveThreshold)));
                else
                    children.Add(Child(zero, 1f));
                return OneDimensional(
                    "Native signed pose by " + input,
                    input,
                    children.ToArray());
            }

            public Motion SelectMotion(
                string weight,
                Motion whenZero,
                Motion whenOne,
                string name)
            {
                whenZero = whenZero ?? EmptyClip();
                whenOne = whenOne ?? EmptyClip();
                var tree = OneDimensional(
                    name, weight,
                    new[]
                    {
                        Child(whenZero, 0f),
                        Child(whenOne, 1f)
                    });
                binarySelectDescriptors[tree] = new BinarySelectDescriptor
                {
                    driver = weight,
                    whenZero = whenZero,
                    whenOne = whenOne
                };
                return tree;
            }

            public Motion SelectMotion(
                string weight,
                Motion whenLow,
                float lowThreshold,
                Motion whenHigh,
                float highThreshold,
                string name)
            {
                if (!(highThreshold > lowThreshold))
                    throw new InvalidOperationException(
                        $"'{name}' requires an increasing threshold interval.");
                return OneDimensional(
                    name, weight,
                    new[]
                    {
                        Child(whenLow ?? EmptyClip(), lowThreshold),
                        Child(whenHigh ?? EmptyClip(), highThreshold)
                    });
            }

            public AnimationClip BlendShapeClip(string path, string blendShape, float value)
            {
                var clip = Clip("Blendshape " + blendShape);
                if (string.IsNullOrEmpty(blendShape)) return clip;
                if (!IsBlendShapeWeightInRange(value))
                    throw new InvalidOperationException(
                        $"Blendshape '{blendShape}' requests weight {value:G9}; VRChat " +
                        "supports only the 0..100 final range.");
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + blendShape),
                    AnimationCurve.Constant(0f, 0f, value));
                return clip;
            }

            public AnimationClip CompositeBlendShapeClip(
                string name,
                IEnumerable<(string path, string blendShape, float value)> curves)
            {
                var values = curves?
                    .Where(curve => !string.IsNullOrEmpty(curve.blendShape))
                    .ToArray() ??
                    Array.Empty<(string path, string blendShape, float value)>();
                if (values.Length == 0) return null;
                var clip = Clip(name);
                foreach (var curve in values)
                {
                    if (!IsBlendShapeWeightInRange(curve.value))
                        throw new InvalidOperationException(
                            $"Blendshape '{curve.blendShape}' requests weight " +
                            $"{curve.value:G9}; VRChat supports only the 0..100 final range.");
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(
                            curve.path ?? string.Empty,
                            typeof(SkinnedMeshRenderer),
                            "blendShape." + curve.blendShape),
                        AnimationCurve.Constant(0f, 0f, curve.value));
                }
                return clip;
            }

            public AnimationClip PoseClip(AnimationClip source, string name)
            {
                var clip = Clip(name);
                if (source == null) return clip;
                var time = Mathf.Max(0f, source.length);
                foreach (var binding in AnimationUtility.GetCurveBindings(source))
                {
                    var curve = AnimationUtility.GetEditorCurve(source, binding);
                    if (curve == null) continue;
                    var endpoint = curve.Evaluate(time);
                    ValidateBlendShapeEndpoint(binding, endpoint, source.name);
                    AnimationUtility.SetEditorCurve(clip, binding,
                        AnimationCurve.Constant(0f, 0f, endpoint));
                }
                return clip;
            }

            public AnimationClip TargetRendererBlendShapePose(
                AnimationClip source,
                string name,
                string rendererPath,
                Mesh targetMesh)
            {
                if (source == null) return null;
                var clip = Clip(name);
                var time = Mathf.Max(0f, source.length);
                foreach (var sourceBinding in AnimationUtility.GetCurveBindings(source))
                {
                    if (!IsLinearCorrectionCurve(sourceBinding, rendererPath, targetMesh)) continue;
                    var curve = AnimationUtility.GetEditorCurve(source, sourceBinding);
                    if (curve == null) continue;
                    var endpoint = curve.Evaluate(time);
                    ValidateBlendShapeEndpoint(sourceBinding, endpoint, source.name);
                    AnimationUtility.SetEditorCurve(clip, sourceBinding,
                        AnimationCurve.Constant(0f, 0f, endpoint));
                }
                return AnimationUtility.GetCurveBindings(clip).Length == 0 ? null : clip;
            }

            public Motion NegatedPose(Motion motion)
            {
                if (!(motion is AnimationClip source)) return EmptyClip();
                var clip = Clip(source.name + " Negative");
                foreach (var binding in AnimationUtility.GetCurveBindings(source))
                {
                    var curve = AnimationUtility.GetEditorCurve(source, binding);
                    if (curve == null) continue;
                    var keys = curve.keys;
                    for (var i = 0; i < keys.Length; i++) keys[i].value = -keys[i].value;
                    curve.keys = keys;
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
                return clip;
            }

            private static bool ContainsBlendShapeCurves(Motion motion)
            {
                return motion is AnimationClip clip &&
                       AnimationUtility.GetCurveBindings(clip).Any(binding =>
                           binding.type == typeof(SkinnedMeshRenderer) &&
                           binding.propertyName.StartsWith(
                               "blendShape.", StringComparison.Ordinal));
            }

            private static bool IsBlendShapeWeightInRange(float value)
            {
                return !float.IsNaN(value) && !float.IsInfinity(value) &&
                       value >= -1e-5f && value <= 100.00001f;
            }

            private static void ValidateBlendShapeEndpoint(
                EditorCurveBinding binding,
                float value,
                string poseName)
            {
                if (binding.type != typeof(SkinnedMeshRenderer) ||
                    !binding.propertyName.StartsWith(
                        "blendShape.", StringComparison.Ordinal) ||
                    IsBlendShapeWeightInRange(value)) return;
                throw new InvalidOperationException(
                    $"Pose '{poseName}' drives '{binding.propertyName}' to {value:G9}. " +
                    "Advanced Viseme cannot preserve an endpoint outside VRChat's " +
                    "0..100 final blendshape range.");
            }

            public void AddOperation(BlendTree root, Motion motion)
            {
                if (root == null || motion == null) return;
                // AlphaFromDeltaTime deliberately returns one mutable batched
                // lookup for every alpha request. Deduplicate only that motion;
                // other repeated references can be intentional additive output
                // contributions (notably linked residual geometry).
                if (alphaBatches.Values.Any(batch => batch.tree == motion) &&
                    root.children.Any(child => child.motion == motion)) return;

                AppendOperationChild(root, new ChildMotion
                {
                    motion = motion,
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
            }

            public void AddGatedOperation(
                BlendTree root,
                Motion motion,
                string gate)
            {
                if (root == null || motion == null || string.IsNullOrEmpty(gate))
                    return;

                // Keep the complete Beta-state/inference Direct subtree behind one raw
                // child weight. In particular, do not flatten or batch its safety
                // zero clips into the always-on Math root: at gate=0 Unity can
                // cull the entire child, while gate=1 retains the original AAP
                // sibling epoch and additive algebra exactly.
                AppendRawChild(root, new ChildMotion
                {
                    motion = motion,
                    directBlendParameter = gate,
                    timeScale = 1f
                });
            }

            private void AppendOperationChild(BlendTree root, ChildMotion child)
            {
                if (child.motion == null) return;
                var unweighted = child.directBlendParameter == AlwaysOne &&
                                 Mathf.Approximately(child.timeScale, 1f) &&
                                 !child.mirror &&
                                 Mathf.Approximately(child.cycleOffset, 0f);

                // The math/output roots are non-normalized Direct trees. An
                // unweighted Direct child is pure grouping, so lower its children
                // directly into the parent. This is the same semantics-preserving
                // rewrite VRCFury performs later, but doing it here prevents the
                // generated controller from containing hundreds of scalar wrapper
                // trees in the first place. A NORMALIZED Direct child is not
                // grouping — its weights divide by the sibling sum — so it must
                // stay an intact nested tree.
                if (unweighted && child.motion is BlendTree direct &&
                    direct.blendType == BlendTreeType.Direct &&
                    !normalizedTrees.Contains(direct))
                {
                    foreach (var nested in direct.children)
                        AppendOperationChild(root, nested);
                    return;
                }

                if (unweighted &&
                    silenceHoldDescriptors.TryGetValue(child.motion, out var silenceHold) &&
                    TryAppendSilenceHoldBatch(root, silenceHold)) return;

                if (unweighted &&
                    binarySelectDescriptors.TryGetValue(child.motion, out var binarySelect) &&
                    TryAppendBinarySelectBatch(root, binarySelect)) return;

                if (unweighted && mapDescriptors.TryGetValue(child.motion, out var map) &&
                    TryAppendMapBatch(root, map)) return;

                if (unweighted &&
                    parameterBlendDescriptors.TryGetValue(child.motion, out var parameterBlend) &&
                    TryAppendParameterBlendBatch(root, parameterBlend)) return;

                AppendRawChild(root, child);
            }

            private static void AppendRawChild(BlendTree root, ChildMotion child)
            {
                var children = root.children.ToList();
                children.Add(child);
                root.children = children.ToArray();
            }

            private bool TryAppendBinarySelectBatch(
                BlendTree root,
                BinarySelectDescriptor descriptor)
            {
                if (descriptor == null) return false;
                var descriptorBindings = MotionBindings(descriptor.whenZero)
                    .Concat(MotionBindings(descriptor.whenOne))
                    .ToHashSet(StringComparer.Ordinal);
                var key = (root, descriptor.driver ?? string.Empty);
                if (!binarySelectBatches.TryGetValue(key, out var batch))
                {
                    var whenZero = Direct($"Vector select {descriptor.driver} zero");
                    var whenOne = Direct($"Vector select {descriptor.driver} one");
                    batch = new BinarySelectBatch
                    {
                        whenZero = whenZero,
                        whenOne = whenOne,
                        tree = OneDimensional(
                            $"Vector select by {descriptor.driver}", descriptor.driver,
                            new[] { Child(whenZero, 0f), Child(whenOne, 1f) })
                    };
                    binarySelectBatches[key] = batch;
                    AppendRawChild(root, Child(batch.tree));
                }
                if (batch.bindings.Overlaps(descriptorBindings)) return false;

                batch.bindings.UnionWith(descriptorBindings);
                AppendOperationChild(batch.whenZero, Child(descriptor.whenZero));
                AppendOperationChild(batch.whenOne, Child(descriptor.whenOne));
                return true;
            }

            private bool TryAppendSilenceHoldBatch(
                BlendTree root,
                SilenceHoldDescriptor descriptor)
            {
                if (descriptor == null) return false;
                var descriptorBindings = MotionBindings(descriptor.nonSilence)
                    .Concat(MotionBindings(descriptor.silenceRelease))
                    .Concat(MotionBindings(descriptor.silenceHold))
                    .ToHashSet(StringComparer.Ordinal);
                var compactIdentity = descriptor.identityHold &&
                                      !string.IsNullOrEmpty(
                                          sharedSilenceUpdateAuthority);
                var key = (
                    root,
                    descriptor.viseme ?? string.Empty,
                    descriptor.history ?? string.Empty,
                    descriptor.stability ?? string.Empty,
                    compactIdentity);
                if (!silenceHoldBatches.TryGetValue(key, out var batch))
                {
                    var silenceRelease = Direct("Vector silence hold release");
                    var silenceHold = Direct("Vector silence hold freeze");
                    if (compactIdentity)
                    {
                        batch = new SilenceHoldBatch
                        {
                            silenceRelease = silenceRelease,
                            silenceHold = silenceHold,
                            tree = OneDimensional(
                                "Vector compact transient-silence hold",
                                sharedSilenceUpdateAuthority,
                                new[]
                                {
                                    Child(silenceHold, 0f),
                                    Child(silenceRelease, 1f)
                                })
                        };
                    }
                    else
                    {
                        var nonSilence = Direct("Vector silence hold active");
                        var byHistory = OneDimensional(
                            "Vector silence hold history", descriptor.history,
                            new[]
                            {
                                Child(silenceRelease,
                                    AdvancedVisemeMath.SpeechHistoryHoldStart),
                                Child(silenceHold,
                                    AdvancedVisemeMath.SpeechHistoryHoldFull)
                            });
                        var byStability = OneDimensional(
                            "Vector silence hold strength", descriptor.stability,
                            new[]
                            {
                                Child(silenceRelease, 0f),
                                Child(byHistory, 0.5f)
                            });
                        batch = new SilenceHoldBatch
                        {
                            nonSilence = nonSilence,
                            silenceRelease = silenceRelease,
                            silenceHold = silenceHold,
                            tree = OneDimensional(
                                "Vector transient-silence hold", descriptor.viseme,
                                new[]
                                {
                                    Child(byStability, 0f),
                                    Child(nonSilence, 1f)
                                })
                        };
                    }
                    silenceHoldBatches[key] = batch;
                    AppendRawChild(root, Child(batch.tree));
                }
                if (batch.bindings.Overlaps(descriptorBindings)) return false;

                batch.bindings.UnionWith(descriptorBindings);
                AppendOperationChild(batch.silenceRelease, Child(descriptor.silenceRelease));
                AppendOperationChild(batch.silenceHold, Child(descriptor.silenceHold));
                if (!compactIdentity)
                    AppendOperationChild(
                        batch.nonSilence, Child(descriptor.nonSilence));
                return true;
            }

            private static IEnumerable<string> MotionBindings(Motion motion)
            {
                var result = new HashSet<string>(StringComparer.Ordinal);
                var visited = new HashSet<Motion>();
                void Visit(Motion current)
                {
                    if (current == null || !visited.Add(current)) return;
                    if (current is AnimationClip clip)
                    {
                        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                            result.Add(
                                $"{binding.type?.FullName}|{binding.path}|{binding.propertyName}");
                        return;
                    }
                    if (!(current is BlendTree tree)) return;
                    foreach (var child in tree.children) Visit(child.motion);
                }
                Visit(motion);
                return result;
            }

            private bool TryAppendMapBatch(BlendTree root, MapDescriptor descriptor)
            {
                if (descriptor == null || descriptor.points == null ||
                    descriptor.points.Length == 0 ||
                    descriptor.points.GroupBy(point => point.input).Any(group => group.Count() > 1))
                    return false;

                var key = (root, descriptor.input ?? string.Empty);
                if (!mapBatches.TryGetValue(key, out var batch))
                {
                    batch = new MapBatch
                    {
                        tree = OneDimensional(
                            $"Vector map {descriptor.input}", descriptor.input,
                            Array.Empty<ChildMotion>())
                    };
                    mapBatches[key] = batch;
                    AppendRawChild(root, Child(batch.tree));
                }
                else
                {
                    var existingThresholds = batch.descriptors
                        .SelectMany(existing => existing.points.Select(point => point.input))
                        .Distinct().Count();
                    var existingOutputs = batch.descriptors
                        .Select(existing => existing.output)
                        .Distinct(StringComparer.Ordinal).Count();
                    var combinedThresholds = batch.descriptors
                        .SelectMany(existing => existing.points.Select(point => point.input))
                        .Concat(descriptor.points.Select(point => point.input))
                        .Distinct().Count();
                    var combinedOutputs = batch.descriptors
                        .Select(existing => existing.output)
                        .Append(descriptor.output)
                        .Distinct(StringComparer.Ordinal).Count();
                    var currentBindings = existingThresholds * existingOutputs;
                    var separateBindings = currentBindings + descriptor.points.Length;
                    var combinedBindings = combinedThresholds * combinedOutputs;
                    var legacyAccepts = combinedBindings <= separateBindings + 2;
                    if (UseLegacyMapBatchAssetCriterionForTests)
                    {
                        if (!legacyAccepts) return false;
                    }
                    else if (!legacyAccepts)
                    {
                        var outputIsDisjoint = batch.descriptors.All(existing =>
                            !string.Equals(
                                existing.output,
                                descriptor.output,
                                StringComparison.Ordinal));
                        var unbatchedBindings = batch.descriptors.Sum(existing =>
                                                    existing.points.Length) +
                                                descriptor.points.Length;
                        var collapsedTrees = batch.descriptors.Count;
                        var extraStoredBindings =
                            combinedBindings - unbatchedBindings;
                        var activeBindingBoundPreserved =
                            MapBatchPreservesActiveBindingBound(
                                batch.descriptors
                                    .Select(existing => existing.points.Length)
                                    .Append(descriptor.points.Length)
                                    .ToArray(),
                                combinedThresholds,
                                combinedOutputs);

                        // A Simple1D map samples at most its two adjacent knots.
                        // On disjoint outputs, require the union grid's active
                        // binding bound to be no larger than all separate maps;
                        // this matters when a one-knot constant lane needs only one
                        // active curve before batching. Extra union knots affect
                        // serialized clip data, not the two-adjacent-knot bound.
                        // Trade a small, explicitly bounded amount of asset data
                        // for each runtime tree removed. Since every original knot
                        // is in the union, linear interpolation remains exact.
                        var runtimeAwareAccepts = outputIsDisjoint &&
                            activeBindingBoundPreserved &&
                            combinedBindings <=
                            MaxRuntimeAwareMapBatchStoredBindings &&
                            extraStoredBindings <= collapsedTrees *
                            MaxExtraMapBindingsPerCollapsedTree;
                        if (!runtimeAwareAccepts) return false;
                    }
                }

                batch.descriptors.Add(descriptor);
                RebuildMapBatch(batch);
                return true;
            }

            private void RebuildMapBatch(MapBatch batch)
            {
                var thresholds = batch.descriptors
                    .SelectMany(descriptor => descriptor.points.Select(point => point.input))
                    .Distinct()
                    .OrderBy(value => value)
                    .ToArray();
                var outputs = batch.descriptors
                    .Select(descriptor => descriptor.output)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();

                batch.tree.children = thresholds.Select(threshold => Child(
                    MultiSetter(
                        $"{batch.tree.name} at {threshold:0.#####}",
                        outputs.Select(output => new KeyValuePair<string, float>(
                            output,
                            batch.descriptors
                                .Where(descriptor => descriptor.output == output)
                                .Sum(descriptor => EvaluateMap(descriptor.points, threshold))))),
                    threshold)).ToArray();
            }

            private static float EvaluateMap(
                IReadOnlyList<(float input, float output)> points,
                float input)
            {
                if (input <= points[0].input) return points[0].output;
                for (var i = 1; i < points.Count; i++)
                {
                    if (input > points[i].input) continue;
                    var previous = points[i - 1];
                    var next = points[i];
                    var denominator = next.input - previous.input;
                    if (Mathf.Abs(denominator) <= 1e-8f) return next.output;
                    return Mathf.LerpUnclamped(
                        previous.output, next.output,
                        (input - previous.input) / denominator);
                }
                return points[points.Count - 1].output;
            }

            private bool TryAppendParameterBlendBatch(
                BlendTree root,
                ParameterBlendDescriptor descriptor)
            {
                if (descriptor == null || descriptor.thresholds == null ||
                    descriptor.sources == null ||
                    descriptor.thresholds.Length == 0 ||
                    descriptor.thresholds.Length != descriptor.sources.Length)
                    return false;

                var thresholdKey = string.Join(",",
                    descriptor.thresholds.Select(value => value.ToString("R")));
                var key = (root, descriptor.driver ?? string.Empty, thresholdKey);
                if (!parameterBlendBatches.TryGetValue(key, out var batch))
                {
                    batch = new ParameterBlendBatch
                    {
                        tree = OneDimensional(
                            $"Vector blend by {descriptor.driver}", descriptor.driver,
                            Array.Empty<ChildMotion>())
                    };
                    parameterBlendBatches[key] = batch;
                    AppendRawChild(root, Child(batch.tree));
                }

                // Two independent operations writing the same AAP are additive,
                // not a vector lane. Keep the later operation separate instead of
                // silently changing that authored behavior.
                if (batch.descriptors.Any(existing => existing.output == descriptor.output))
                    return false;

                batch.descriptors.Add(descriptor);
                RebuildParameterBlendBatch(batch);
                return true;
            }

            private void RebuildParameterBlendBatch(ParameterBlendBatch batch)
            {
                var outputs = batch.descriptors.Select(descriptor => descriptor.output).ToArray();
                var signed = batch.descriptors.Select(descriptor => descriptor.signed).ToArray();
                var thresholds = batch.descriptors[0].thresholds;
                batch.tree.children = Enumerable.Range(0, thresholds.Length)
                    .Select(index => Child(
                        CopyMixedVector(
                            batch.descriptors.Select(descriptor => descriptor.sources[index]).ToArray(),
                            outputs,
                            signed,
                            $"{batch.tree.name} sample {index}"),
                        thresholds[index]))
                    .ToArray();
            }

            public void PruneUnreachableMotions()
            {
                bool IsEmptyMotion(Motion motion)
                {
                    if (motion == null) return true;
                    if (motion is AnimationClip clip)
                        return clip.events.Length == 0 &&
                               AnimationUtility.GetCurveBindings(clip).Length == 0 &&
                               AnimationUtility.GetObjectReferenceCurveBindings(clip)
                                   .Length == 0;
                    if (!(motion is BlendTree tree)) return false;
                    return tree.children.Length == 0 ||
                           tree.children.All(child => IsEmptyMotion(child.motion));
                }

                Motion OptimizeMotion(Motion motion)
                {
                    if (!(motion is BlendTree tree)) return motion;

                    var optimizedChildren = tree.children;
                    for (var i = 0; i < optimizedChildren.Length; i++)
                    {
                        var child = optimizedChildren[i];
                        child.motion = OptimizeMotion(child.motion);
                        optimizedChildren[i] = child;
                    }
                    tree.children = optimizedChildren;

                    if (tree.blendType == BlendTreeType.Simple1D)
                        CanonicalizeSimple1DConstantKnots(tree);

                    if (tree.blendType == BlendTreeType.Direct)
                    {
                        var flattened = new List<ChildMotion>();
                        foreach (var child in tree.children)
                        {
                            if (child.directBlendParameter == AlwaysOne &&
                                Mathf.Approximately(child.timeScale, 1f) &&
                                !child.mirror &&
                                Mathf.Approximately(child.cycleOffset, 0f) &&
                                child.motion is BlendTree direct &&
                                direct.blendType == BlendTreeType.Direct &&
                                // A normalized Direct child is not grouping:
                                // its weights divide by the sibling sum, so
                                // lowering it into the parent changes the math.
                                !UsesNormalizedBlendValues(direct))
                                flattened.AddRange(direct.children);
                            else
                                flattened.Add(child);
                        }

                        // Empty children have no binding and contribute nothing
                        // to a non-normalized Direct sum. Removing them is exact
                        // and lets the liveness pass erase whole dead branches.
                        if (!UsesNormalizedBlendValues(tree))
                            flattened.RemoveAll(child =>
                                IsEmptyMotion(child.motion));

                        // Factor w*A + w*B as w*(A+B). Unity clamps Direct
                        // weights to nonnegative values, but distributivity still
                        // holds exactly for generated Direct math trees. This
                        // turns repeated scalar products sharing one gate into a
                        // single vector product without adding an AAP stage.
                        var factoredIndices = new HashSet<int>();
                        var replacements = new Dictionary<int, ChildMotion>();
                        var groups = flattened
                            .Select((child, index) => (child, index))
                            .Where(item => item.child.directBlendParameter != AlwaysOne &&
                                           Mathf.Approximately(item.child.timeScale, 1f) &&
                                           !item.child.mirror &&
                                           Mathf.Approximately(item.child.cycleOffset, 0f) &&
                                           item.child.motion is BlendTree nested &&
                                           nested.blendType == BlendTreeType.Direct)
                            .GroupBy(item => item.child.directBlendParameter,
                                StringComparer.Ordinal)
                            .Where(group => group.Count() > 1)
                            .ToArray();
                        foreach (var group in groups)
                        {
                            var items = group.ToArray();
                            var factored = Direct("Vector product by " + group.Key);
                            factored.children = items
                                .SelectMany(item => ((BlendTree)item.child.motion).children)
                                .ToArray();
                            var replacement = items[0].child;
                            replacement.motion = OptimizeMotion(factored);
                            replacements[items[0].index] = replacement;
                            foreach (var item in items.Skip(1)) factoredIndices.Add(item.index);
                        }

                        if (groups.Length > 0)
                        {
                            var rewritten = new List<ChildMotion>();
                            for (var index = 0; index < flattened.Count; index++)
                            {
                                if (factoredIndices.Contains(index)) continue;
                                rewritten.Add(replacements.TryGetValue(index, out var replacement)
                                    ? replacement
                                    : flattened[index]);
                            }
                            tree.children = rewritten.ToArray();
                        }
                        else
                        {
                            tree.children = flattened.ToArray();
                        }

                        FoldConstantAnimatorChildren(tree);
                    }

                    if (tree.children.Length == 0 ||
                        tree.children.All(child => IsEmptyMotion(child.motion)))
                        return EmptyClip();

                    if (tree.blendType == BlendTreeType.Direct &&
                        tree.children.Length == 1 &&
                        tree.children[0].directBlendParameter == AlwaysOne &&
                        Mathf.Approximately(tree.children[0].timeScale, 1f) &&
                        !tree.children[0].mirror &&
                        Mathf.Approximately(tree.children[0].cycleOffset, 0f))
                        return tree.children[0].motion;
                    return tree;
                }

                void OptimizeStateMachine(AnimatorStateMachine stateMachine)
                {
                    if (stateMachine == null) return;
                    foreach (var state in stateMachine.states)
                        state.state.motion = OptimizeMotion(state.state.motion);
                    foreach (var child in stateMachine.stateMachines)
                        OptimizeStateMachine(child.stateMachine);
                }

                foreach (var layer in controller.layers)
                    OptimizeStateMachine(layer.stateMachine);

                var reachable = new HashSet<Motion>();
                Action<Motion> visitMotion = null;
                visitMotion = motion =>
                {
                    if (motion == null || !reachable.Add(motion)) return;
                    if (!(motion is BlendTree tree)) return;
                    foreach (var child in tree.children) visitMotion(child.motion);
                };
                Action<AnimatorStateMachine> visitStateMachine = null;
                visitStateMachine = stateMachine =>
                {
                    if (stateMachine == null) return;
                    foreach (var state in stateMachine.states)
                        visitMotion(state.state.motion);
                    foreach (var child in stateMachine.stateMachines)
                        visitStateMachine(child.stateMachine);
                };
                foreach (var layer in controller.layers)
                    visitStateMachine(layer.stateMachine);

                var unreachable = subAssets
                    .OfType<Motion>()
                    .Where(motion => !reachable.Contains(motion))
                    .OrderByDescending(motion => motion is BlendTree)
                    .ToArray();
                foreach (var motion in unreachable)
                {
                    subAssets.Remove(motion);
                    UnityEngine.Object.DestroyImmediate(motion, true);
                }
            }

            private static void CanonicalizeSimple1DConstantKnots(BlendTree tree)
            {
                if (tree == null || tree.blendType != BlendTreeType.Simple1D ||
                    tree.children.Length < 2)
                    return;

                var children = tree.children.ToList();
                bool SameMetadata(ChildMotion left, ChildMotion right) =>
                    Mathf.Approximately(left.timeScale, right.timeScale) &&
                    left.mirror == right.mirror &&
                    Mathf.Approximately(left.cycleOffset, right.cycleOffset);

                bool SameValues(ChildMotion left, ChildMotion right)
                {
                    if (!SameMetadata(left, right)) return false;
                    var leftValues = TryReadConstantAnimatorClip(
                        left.motion as AnimationClip);
                    var rightValues = TryReadConstantAnimatorClip(
                        right.motion as AnimationClip);
                    return leftValues != null && rightValues != null &&
                           leftValues.Count == rightValues.Count &&
                           leftValues.All(pair =>
                               rightValues.TryGetValue(pair.Key, out var value) &&
                               value.Equals(pair.Value));
                }

                // Simple1D clamps outside its first/last thresholds. Equal
                // plateau endpoints are therefore redundant at either edge.
                while (children.Count > 1 && SameValues(children[0], children[1]))
                    children.RemoveAt(0);
                while (children.Count > 1 &&
                       SameValues(children[children.Count - 2], children[children.Count - 1]))
                    children.RemoveAt(children.Count - 1);

                var index = 1;
                while (index < children.Count - 1)
                {
                    var previous = children[index - 1];
                    var current = children[index];
                    var next = children[index + 1];
                    if (!SameMetadata(previous, current) ||
                        !SameMetadata(current, next))
                    {
                        index++;
                        continue;
                    }

                    var denominator = next.threshold - previous.threshold;
                    if (!(denominator > 0f))
                    {
                        index++;
                        continue;
                    }
                    var previousValues = TryReadConstantAnimatorClip(
                        previous.motion as AnimationClip);
                    var currentValues = TryReadConstantAnimatorClip(
                        current.motion as AnimationClip);
                    var nextValues = TryReadConstantAnimatorClip(
                        next.motion as AnimationClip);
                    if (previousValues == null || currentValues == null ||
                        nextValues == null ||
                        previousValues.Count != currentValues.Count ||
                        previousValues.Count != nextValues.Count)
                    {
                        index++;
                        continue;
                    }

                    var position =
                        (current.threshold - previous.threshold) / denominator;
                    var collinear = previousValues.All(pair =>
                        currentValues.TryGetValue(pair.Key, out var currentValue) &&
                        nextValues.TryGetValue(pair.Key, out var nextValue) &&
                        currentValue.Equals(Mathf.LerpUnclamped(
                            pair.Value, nextValue, position)));
                    if (collinear)
                        children.RemoveAt(index);
                    else
                        index++;
                }

                tree.children = children.ToArray();
            }

            private void FoldConstantAnimatorChildren(BlendTree tree)
            {
                if (tree == null || tree.blendType != BlendTreeType.Direct ||
                    UsesNormalizedBlendValues(tree))
                    return;

                var indexed = tree.children
                    .Select((child, index) => (child, index))
                    .Where(item =>
                        Mathf.Approximately(item.child.timeScale, 1f) &&
                        !item.child.mirror &&
                        Mathf.Approximately(item.child.cycleOffset, 0f) &&
                        item.child.motion is AnimationClip)
                    .Select(item =>
                    {
                        var values = TryReadConstantAnimatorClip(
                            item.child.motion as AnimationClip);
                        return (item.child, item.index, values);
                    })
                    .Where(item => item.values != null)
                    .GroupBy(item => item.child.directBlendParameter,
                        StringComparer.Ordinal);

                var replacements = new Dictionary<int, ChildMotion>();
                var removed = new HashSet<int>();
                foreach (var group in indexed)
                {
                    var items = group.OrderBy(item => item.index).ToArray();
                    if (items.Length < 2) continue;

                    // A non-normalized Direct tree is a linear sum. Children
                    // sharing one weight therefore satisfy
                    //   w*A + w*B = w*(A+B)
                    // even when A and B write the same AAP. Folding those
                    // overlaps turns independent affine terms into one sampled
                    // constant vector instead of leaving one clip per term.
                    var sums = new Dictionary<string, float>(StringComparer.Ordinal);
                    foreach (var item in items)
                    foreach (var pair in item.values)
                        sums[pair.Key] = sums.TryGetValue(pair.Key, out var value)
                            ? value + pair.Value
                            : pair.Value;

                    // Keep exact-zero cancellations bound. An unbound Animator
                    // parameter retains its previous value, which is not the same
                    // as this weighted group explicitly contributing zero.
                    var folded = sums.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .ToArray();
                    var first = items[0];
                    var replacement = first.child;
                    replacement.motion = MultiSetter(
                        "Folded constants by " + group.Key, folded);
                    replacements[first.index] = replacement;
                    foreach (var item in items.Skip(1)) removed.Add(item.index);
                }

                if (removed.Count == 0) return;
                var rewritten = new List<ChildMotion>();
                for (var index = 0; index < tree.children.Length; index++)
                {
                    if (removed.Contains(index)) continue;
                    rewritten.Add(replacements.TryGetValue(index, out var replacement)
                        ? replacement
                        : tree.children[index]);
                }
                tree.children = rewritten.ToArray();
            }

            private static IReadOnlyDictionary<string, float>
                TryReadConstantAnimatorClip(AnimationClip clip)
            {
                if (clip == null || clip.events.Length != 0 ||
                    AnimationUtility.GetObjectReferenceCurveBindings(clip).Length != 0)
                    return null;
                var bindings = AnimationUtility.GetCurveBindings(clip);
                if (bindings.Length == 0 || bindings.Any(binding =>
                        binding.type != typeof(Animator) ||
                        !string.IsNullOrEmpty(binding.path)))
                    return null;

                var values = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (var binding in bindings)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || curve.keys.Length == 0)
                        return null;
                    var value = curve.keys[0].value;
                    if (curve.keys.Any(key =>
                            key.value != value ||
                            float.IsNaN(key.inTangent) ||
                            float.IsNaN(key.outTangent) ||
                            (!float.IsInfinity(key.inTangent) && key.inTangent != 0f) ||
                            (!float.IsInfinity(key.outTangent) && key.outTangent != 0f)) ||
                        values.ContainsKey(binding.propertyName))
                        return null;
                    values[binding.propertyName] = value;
                }
                return values;
            }

            private static bool UsesNormalizedBlendValues(BlendTree tree)
            {
                var serializedTree = new SerializedObject(tree);
                var normalized = serializedTree.FindProperty("m_NormalizedBlendValues");
                return normalized != null && normalized.boolValue;
            }

            public void SubAsset(UnityEngine.Object obj)
            {
                if (obj == null || subAssets.Contains(obj) || AssetDatabase.Contains(obj)) return;
                obj.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(obj, controller);
                subAssets.Add(obj);
            }

            private static ChildMotion Child(Motion motion)
            {
                return new ChildMotion { motion = motion, directBlendParameter = AlwaysOne, timeScale = 1f };
            }

            private static string Sanitize(string value)
            {
                return new string((value ?? string.Empty).Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            }
        }
    }
}
