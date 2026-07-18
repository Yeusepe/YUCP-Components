using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YUCP.Components
{
    /// <summary>Pure numerical reference for the Animator implementation and editor tests.</summary>
    public static class AdvancedVisemeMath
    {
        public const float SpeechHistoryAttackSeconds = 0.06f;
        public const float SpeechHistoryHoldStart = 0.35f;
        public const float SpeechHistoryHoldFull = 0.55f;
        public const float SpeechPresenceAttackSeconds = 0.009f;
        public const float SpeechPresenceReleaseSeconds = 0.055f;
        public const float MaximumSpeechLivelinessLead = 0.85f;
        // The observer remains exact. This tiny output dead-zone turns only its
        // numerically negligible tails into exact zero so Direct BlendTrees can
        // skip dormant children. The affine upper branch preserves both 0 and 1.
        // Direct BlendTrees keep every positive child live. Values below this
        // bound are visually negligible observer tails, but leaving them as
        // denormally small weights keeps large Beta/fusion subgraphs sampling.
        // The 3e-4 bound is certified over 15-144 FPS: public pose/viseme RMS
        // stays below 1.5e-4, maximum below 0.0016, and velocity RMS below
        // 0.0013 while removing roughly 110 live clip evaluations per avatar
        // frame in the reference speech/tracking trace.
        public const float SimplexCullingEpsilon = 0.0003f;

        /// <summary>
        /// State for the causal, one-pole speech-history observer. History is
        /// charged only by hard non-silence visemes and leaks toward zero during
        /// silence. Voice is deliberately excluded so microphone noise cannot
        /// pin a stale mouth pose.
        /// </summary>
        public struct SpeechHistoryState
        {
            public float History;
            public float HoldWeight;
            public float Presence;
        }

        public static float Alpha(float deltaTime, float responseSeconds)
        {
            if (!IsFinite(deltaTime) || deltaTime <= 0f) return 0f;
            if (!IsFinite(responseSeconds) || responseSeconds <= 0f) return 1f;
            return Mathf.Clamp01(1f - Mathf.Exp(-deltaTime / responseSeconds));
        }

        /// <summary>
        /// Applies a continuous nonnegative soft dead-zone without feeding the
        /// approximation back into the simplex observer. Values at or below the
        /// threshold become exactly zero; the remaining interval is rescaled so
        /// a unit endpoint remains exactly one.
        /// </summary>
        public static float SparsifySimplexCoordinate(
            float value,
            float epsilon = SimplexCullingEpsilon)
        {
            value = Sanitize01(value);
            epsilon = IsFinite(epsilon)
                ? Mathf.Clamp(epsilon, 0f, 0.1f)
                : SimplexCullingEpsilon;
            if (value <= epsilon) return 0f;
            return Mathf.Clamp01((value - epsilon) / (1f - epsilon));
        }

        public static void StepSimplex(
            int observedViseme,
            float deltaTime,
            float responseSeconds,
            float[] fast,
            float[] slow)
        {
            if (fast == null || slow == null || fast.Length != VisemeReconstructionProfile.VisemeCount || slow.Length != fast.Length)
                throw new ArgumentException("Observer buffers must contain exactly 15 values.");

            observedViseme = Mathf.Clamp(observedViseme, 0, fast.Length - 1);
            var alpha = Alpha(deltaTime, responseSeconds);
            for (var i = 0; i < fast.Length; i++)
            {
                var target = i == observedViseme ? 1f : 0f;
                fast[i] += alpha * (target - fast[i]);
                slow[i] += alpha * (fast[i] - slow[i]);
                fast[i] = Sanitize01(fast[i]);
                slow[i] = Sanitize01(slow[i]);
            }
            NormalizeSimplex(fast);
            NormalizeSimplex(slow);
        }

        /// <summary>
        /// Converts the centered Silence Stability control to the release time
        /// constant used by the history observer. The default control value of
        /// 0.5 uses the configured response and the upper half extends it up to
        /// 2x. The lower half changes hold authority, not the history trajectory;
        /// its zero endpoint is still an exact observer bypass.
        /// </summary>
        public static float SpeechHistoryReleaseSeconds(
            float configuredReleaseSeconds,
            float silenceStability)
        {
            configuredReleaseSeconds = IsFinite(configuredReleaseSeconds)
                ? Mathf.Max(0f, configuredReleaseSeconds)
                : 0f;
            var extension = Mathf.Clamp01(2f * Sanitize01(silenceStability) - 1f);
            return configuredReleaseSeconds * (1f + extension);
        }

        /// <summary>
        /// Frame update used by the Animator graph. Runtime tuning interpolates
        /// the already frame-correct configured and extended coefficients, so
        /// the numerical reference performs the same interpolation.
        /// </summary>
        public static float SpeechHistoryReleaseAlpha(
            float deltaTime,
            float configuredReleaseSeconds,
            float silenceStability)
        {
            configuredReleaseSeconds = IsFinite(configuredReleaseSeconds)
                ? Mathf.Max(0f, configuredReleaseSeconds)
                : 0f;
            var extension = Mathf.Clamp01(2f * Sanitize01(silenceStability) - 1f);
            var configuredAlpha = Alpha(deltaTime, configuredReleaseSeconds);
            var extendedAlpha = Alpha(deltaTime, 2f * configuredReleaseSeconds);
            return Mathf.Lerp(configuredAlpha, extendedAlpha, extension);
        }

        /// <summary>
        /// Maps accumulated speech history to a continuous silence-hold weight. A
        /// hard non-silence phone always bypasses the hold, making interruptions
        /// immediate. Below the centered control value the same control also
        /// fades the maximum hold authority.
        /// </summary>
        public static float SpeechHistoryHoldWeight(
            float history,
            int observedViseme,
            float silenceStability)
        {
            observedViseme = Mathf.Clamp(
                observedViseme, 0, VisemeReconstructionProfile.VisemeCount - 1);
            var stability = Sanitize01(silenceStability);
            if (observedViseme != 0 || stability <= 0f) return 0f;

            var authority = Mathf.Clamp01(2f * stability);
            return authority * Mathf.InverseLerp(
                SpeechHistoryHoldStart, SpeechHistoryHoldFull, Sanitize01(history));
        }

        /// <summary>
        /// Advances a frame-rate-correct asymmetric speech-history observer and
        /// returns its current silence-hold weight. Only VRChat's hard Viseme
        /// index charges history. voiceEvidence remains in the reference API to
        /// prove that Voice alone neither creates nor extends a mouth pose.
        /// </summary>
        public static float StepSpeechHistory(
            int observedViseme,
            float voiceEvidence,
            float deltaTime,
            float configuredReleaseSeconds,
            float silenceStability,
            ref SpeechHistoryState state)
        {
            observedViseme = Mathf.Clamp(
                observedViseme, 0, VisemeReconstructionProfile.VisemeCount - 1);
            deltaTime = IsFinite(deltaTime) ? Mathf.Max(0f, deltaTime) : 0f;
            _ = voiceEvidence; // Voice is expressive amplitude, never VAD memory.

            state.History = Sanitize01(state.History);
            state.Presence = Sanitize01(state.Presence);
            var hardPhone = observedViseme != 0;
            // BlendTree selectors sample h_k while the history Motion writes
            // h_(k+1). Compute this frame's authority first so the reference and
            // generated Animator share the same causal ordering.
            var holdWeight = SpeechHistoryHoldWeight(
                state.History, observedViseme, silenceStability);
            var presenceAttack = Alpha(deltaTime, SpeechPresenceAttackSeconds);
            if (hardPhone)
            {
                state.Presence += presenceAttack * (1f - state.Presence);
            }
            else
            {
                // The generated nested tree blends an inactive release Motion
                // with an active attack Motion. Reproduce that composition
                // exactly instead of approximating it as smoothing toward w.
                var presenceRelease = Alpha(
                    deltaTime, SpeechPresenceReleaseSeconds);
                var inactivePresence = state.Presence * (1f - presenceRelease);
                var activePresence = state.Presence +
                                     presenceAttack * (1f - state.Presence);
                state.Presence = Mathf.Lerp(
                    inactivePresence, activePresence, holdWeight);
            }
            state.Presence = Sanitize01(state.Presence);

            var historyTarget = hardPhone ? 1f : 0f;
            var historyAlpha = hardPhone
                ? Alpha(deltaTime, SpeechHistoryAttackSeconds)
                : SpeechHistoryReleaseAlpha(
                    deltaTime, configuredReleaseSeconds, silenceStability);
            state.History += historyAlpha *
                             (historyTarget - state.History);
            state.History = Sanitize01(state.History);
            state.HoldWeight = holdWeight;
            return state.HoldWeight;
        }

        /// <summary>
        /// Reference two-pole observer with a soft silence hangover. During hard
        /// silence the fast stage is blended between its normal release and a
        /// frozen missing-measurement update; the slow stage always settles
        /// toward fast. Non-silence and zero stability are exactly StepSimplex.
        /// </summary>
        public static void StepSimplexWithSpeechHistory(
            int observedViseme,
            float voiceEvidence,
            float deltaTime,
            float responseSeconds,
            float configuredReleaseSeconds,
            float silenceStability,
            ref SpeechHistoryState state,
            float[] fast,
            float[] slow)
        {
            if (fast == null || slow == null ||
                fast.Length != VisemeReconstructionProfile.VisemeCount ||
                slow.Length != fast.Length)
                throw new ArgumentException("Observer buffers must contain exactly 15 values.");

            observedViseme = Mathf.Clamp(observedViseme, 0, fast.Length - 1);
            deltaTime = IsFinite(deltaTime) ? Mathf.Max(0f, deltaTime) : 0f;
            var holdWeight = StepSpeechHistory(
                observedViseme, voiceEvidence, deltaTime,
                configuredReleaseSeconds, silenceStability, ref state);
            var alpha = Alpha(deltaTime, responseSeconds);
            var fastAlpha = observedViseme == 0
                ? alpha * (1f - holdWeight)
                : alpha;

            for (var i = 0; i < fast.Length; i++)
            {
                var target = i == observedViseme ? 1f : 0f;
                fast[i] += fastAlpha * (target - fast[i]);
                slow[i] += alpha * (fast[i] - slow[i]);
                fast[i] = Sanitize01(fast[i]);
                slow[i] = Sanitize01(slow[i]);
            }
            NormalizeSimplex(fast);
            NormalizeSimplex(slow);
        }

        public static float Fuse(float speech, float tracking, float gain)
        {
            gain = Sanitize01(gain);
            return Mathf.Lerp(speech, tracking, gain);
        }

        /// <summary>
        /// Shared render lead used for every viseme and articulator. A common
        /// convex weight preserves the reconstructed simplex and the calibrated
        /// identity U(Cp) + Rp = Vp. The lead disappears exactly when tracking is
        /// fully active, so it cannot alter an authoritative tracked pose.
        /// </summary>
        public static float SpeechLivelinessLead(
            float speechLiveliness,
            float trackingBlend)
        {
            return MaximumSpeechLivelinessLead *
                   Sanitize01(speechLiveliness) *
                   (1f - Sanitize01(trackingBlend));
        }

        /// <summary>Numerical reference for the Animator's convex render lead.</summary>
        public static float ApplySpeechLiveliness(
            float slow,
            float fast,
            float speechLiveliness,
            float trackingBlend)
        {
            slow = IsFinite(slow) ? slow : 0f;
            fast = IsFinite(fast) ? fast : 0f;
            return Mathf.Lerp(
                slow, fast,
                SpeechLivelinessLead(speechLiveliness, trackingBlend));
        }

        /// <summary>
        /// Complement of the visible measurement authority. This is the share
        /// of an authored, coupled viseme pose that may remain without moving a
        /// lower-face coordinate already owned by tracking.
        /// </summary>
        public static float VisibleSpeechRemainder(float visibleTrackingGain)
        {
            return 1f - Sanitize01(visibleTrackingGain);
        }

        /// <summary>
        /// Leaves phonetic constraints available for remote or unmeasured
        /// channels, but removes them from a locally measured target.
        /// </summary>
        public static float PhoneticConstraintRemainder(
            float localFactor,
            float targetTrackingGain)
        {
            return 1f - Sanitize01(localFactor) * Sanitize01(targetTrackingGain);
        }

        public static float SmoothStep(float edge0, float edge1, float value)
        {
            if (!IsFinite(value)) return 0f;
            edge1 = Mathf.Max(edge0 + 1e-6f, edge1);
            var t = Mathf.Clamp01((value - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Reference for the Animator's low-lag fast/slow tracking observer.
        /// Stationary input resolves to the two-pole estimate; deliberate motion
        /// continuously selects the one-pole estimate. Raw input is never exposed.
        /// </summary>
        public static float AdaptiveTrackingPose(
            float fast,
            float slow,
            bool local,
            float localDeadband = 0.0025f,
            float localFullScale = 0.035f,
            float remoteDeadband = 0.006f,
            float remoteFullScale = 0.075f)
        {
            fast = IsFinite(fast) ? Mathf.Clamp(fast, -1f, 1f) : 0f;
            slow = IsFinite(slow) ? Mathf.Clamp(slow, -1f, 1f) : 0f;
            var motion = Mathf.Abs(fast - slow);
            var weight = local
                ? SmoothStep(localDeadband, localFullScale, motion)
                : SmoothStep(remoteDeadband, remoteFullScale, motion);
            return Mathf.Lerp(slow, fast, weight);
        }

        /// <summary>
        /// Authority for a bounded speech/tracking interpolation. A valid local
        /// measurement is exact; remote measurements retain their configured
        /// reliability near agreement and become authoritative on disagreement.
        /// </summary>
        public static float TrackingAuthority(
            float speech,
            float tracking,
            bool local,
            float localReliability,
            float remoteReliability,
            float agreementDeadband = 0.01f,
            float disagreement = 0.12f)
        {
            speech = IsFinite(speech) ? Mathf.Clamp(speech, -1f, 1f) : 0f;
            tracking = IsFinite(tracking) ? Mathf.Clamp(tracking, -1f, 1f) : 0f;
            if (local) return 1f;

            var mismatch = Mathf.Abs(tracking - speech);
            var baseline = Sanitize01(remoteReliability);
            return Mathf.Lerp(
                baseline, 1f,
                SmoothStep(agreementDeadband * 1.5f, disagreement * 1.5f, mismatch));
        }

        public static float SpeechActivityTarget(float silenceWeight, float voiceEvidence)
        {
            return Mathf.Clamp01(1f - Sanitize01(silenceWeight) + Sanitize01(voiceEvidence));
        }

        public static float SmoothFloorProjection(
            float value,
            float floor,
            float confidence,
            float projectionWidth = 0.05f)
        {
            value = Sanitize01(value);
            floor = Sanitize01(floor);
            confidence = Sanitize01(confidence);
            var width = Mathf.Max(1e-6f, projectionWidth);
            var difference = value - floor;
            float projected;
            if (difference <= -width)
            {
                projected = floor;
            }
            else if (difference >= width)
            {
                projected = value;
            }
            else
            {
                // C1-continuous soft maximum. Unlike blending toward the floor
                // by a violation-dependent weight, its derivative never becomes
                // negative near the boundary.
                var shifted = difference + width;
                projected = floor + shifted * shifted / (4f * width);
            }
            return Mathf.Lerp(value, Mathf.Clamp01(projected), confidence);
        }

        public static float SmoothCeilingProjection(
            float value,
            float ceiling,
            float confidence,
            float projectionWidth = 0.05f)
        {
            value = Sanitize01(value);
            ceiling = Sanitize01(ceiling);
            confidence = Sanitize01(confidence);
            var width = Mathf.Max(1e-6f, projectionWidth);
            var difference = value - ceiling;
            float projected;
            if (difference <= -width)
            {
                projected = value;
            }
            else if (difference >= width)
            {
                projected = ceiling;
            }
            else
            {
                // Symmetric C1-continuous soft minimum.
                var shifted = width - difference;
                projected = ceiling - shifted * shifted / (4f * width);
            }
            return Mathf.Lerp(value, Mathf.Clamp01(projected), confidence);
        }

        public static float ComplementaryTrackingGain(
            float trackingGain,
            float vowelWeight,
            float vowelIdentityRetention)
        {
            return Sanitize01(trackingGain) *
                   (1f - Sanitize01(vowelWeight) * Sanitize01(vowelIdentityRetention));
        }

        public static float SpeechPresence(float silenceWeight)
        {
            return 1f - Sanitize01(silenceWeight);
        }

        public static float SpeechGain(
            float silenceWeight,
            float voiceEnergy,
            float quietSpeechFloor)
        {
            var amplitude = Sanitize01(quietSpeechFloor) +
                            Sanitize01(voiceEnergy) * (1f - Sanitize01(quietSpeechFloor));
            return SpeechPresence(silenceWeight) * amplitude;
        }

        /// <summary>
        /// Preserves the already-computed speech-pose gain across a protected
        /// transient sil without boosting quiet speech. As history releases, the
        /// result continuously returns to the live presence/amplitude product.
        /// </summary>
        public static float HeldSpeechGain(
            float previousGain,
            float presence,
            float voiceAmplitude,
            float history,
            int observedViseme,
            float silenceStability)
        {
            var normalGain = Sanitize01(presence) * Sanitize01(voiceAmplitude);
            var holdWeight = SpeechHistoryHoldWeight(
                history, observedViseme, silenceStability);
            return Mathf.Lerp(normalGain, Sanitize01(previousGain), holdWeight);
        }

        /// <summary>
        /// Evaluates one column of the low-rank residual-ownership correction:
        /// -detail * yield * gain * sum_i(p_i * projection_i). Projection values
        /// are signed least-squares coefficients; viseme weights are the causal
        /// reconstructed simplex. Each articulator calls this independently, so a
        /// weak unrelated tracking channel cannot delay an already measured axis.
        /// </summary>
        public static float LowRankOwnershipCorrection(
            IReadOnlyList<float> visemeWeights,
            IReadOnlyList<float> projectionColumn,
            float trackingGain,
            float authoredDetail,
            float trackedSurfaceYield)
        {
            if (visemeWeights == null || projectionColumn == null ||
                visemeWeights.Count != projectionColumn.Count)
                return 0f;
            var projected = 0f;
            for (var index = 0; index < visemeWeights.Count; index++)
            {
                var weight = IsFinite(visemeWeights[index])
                    ? Mathf.Max(0f, visemeWeights[index])
                    : 0f;
                var coefficient = IsFinite(projectionColumn[index])
                    ? projectionColumn[index]
                    : 0f;
                projected += weight * coefficient;
            }
            return -Sanitize01(trackingGain) *
                   Sanitize01(authoredDetail) *
                   Sanitize01(trackedSurfaceYield) *
                   projected;
        }

        public static float HeadroomNormalizedResidual(float tracked, float center)
        {
            tracked = Sanitize01(tracked);
            center = Sanitize01(center);
            var delta = tracked - center;
            var headroom = delta >= 0f ? 1f - center : center;
            return Mathf.Clamp(delta / Mathf.Max(
                headroom, AdvancedVisemeVisibleTongueResidual.HeadroomFloor), -1f, 1f);
        }

        public static float ApplyBoundedResidual(
            float center,
            float residual,
            float confidence,
            bool signed)
        {
            center = IsFinite(center)
                ? Mathf.Clamp(center, signed ? -1f : 0f, 1f)
                : 0f;
            residual = IsFinite(residual) ? Mathf.Clamp(residual, -1f, 1f) : 0f;
            confidence = Sanitize01(confidence);
            var target = residual >= 0f
                ? center + residual * (1f - center)
                : signed
                    ? center + residual * (1f + center)
                    : center + residual * center;
            return Mathf.Lerp(center, Mathf.Clamp(target, signed ? -1f : 0f, 1f), confidence);
        }

        public static void SignedRayCorrection(
            float final,
            float fadedSpeech,
            float generatedTracking,
            out float positiveRay,
            out float negativeRay)
        {
            final = IsFinite(final) ? Mathf.Clamp(final, -1f, 1f) : 0f;
            fadedSpeech = IsFinite(fadedSpeech) ? Mathf.Clamp(fadedSpeech, -1f, 1f) : 0f;
            generatedTracking = IsFinite(generatedTracking)
                ? Mathf.Clamp(generatedTracking, -1f, 1f)
                : 0f;
            positiveRay = Mathf.Max(0f, final) - Mathf.Max(0f, fadedSpeech) -
                          Mathf.Max(0f, generatedTracking);
            negativeRay = Mathf.Max(0f, -final) - Mathf.Max(0f, -fadedSpeech) -
                          Mathf.Max(0f, -generatedTracking);
        }

        public static void ProjectMouthEnvelope(
            float thWeight,
            ref float jawOpen,
            ref float lipClose,
            ref float mouthOpen,
            ref float lipPucker,
            ref float lipSuck,
            ref float tongueOut)
        {
            jawOpen = Sanitize01(jawOpen);
            lipClose = Sanitize01(lipClose);
            mouthOpen = Mathf.Min(Sanitize01(mouthOpen), 1f - lipClose);
            lipSuck = Sanitize01(lipSuck);
            lipPucker = Mathf.Min(Sanitize01(lipPucker), 1f - lipSuck);

            var aperture = Mathf.Clamp01(jawOpen + mouthOpen + Sanitize01(thWeight) * 0.6f);
            tongueOut = Mathf.Min(Sanitize01(tongueOut), 0.08f + 0.92f * aperture);
        }

        public static void NasalEvidence(
            float ppWeight,
            float nnWeight,
            float energy,
            float lipClose,
            float tongueContact,
            out float mConfidence,
            out float nConfidence)
        {
            energy = Sanitize01(energy);
            lipClose = Sanitize01(lipClose);
            tongueContact = Sanitize01(tongueContact);
            mConfidence = Sanitize01(ppWeight) * energy * Mathf.Lerp(0.6f, 1f, lipClose);
            // Oculus nn represents n/l. Those phones constrain the tongue, not
            // the lips, so observed closure must not veto otherwise valid nn
            // evidence (for example a deliberately closed-mouth /n/).
            nConfidence = Sanitize01(nnWeight) * energy * Mathf.Lerp(0.6f, 1f, tongueContact);
        }

        /// <summary>
        /// Conservatively redistributes only the merged PP/nn candidate mass.
        /// This is the portable runtime form of the Beta face-conditioned phone
        /// observer: confidence zero is exactly the original Oculus prior, while
        /// confidence one uses the learned M-compatible share. No probability is
        /// created or removed and every other viseme remains untouched.
        /// </summary>
        public static void ConditionMergedNasalPair(
            float ppWeight,
            float nnWeight,
            float mShare,
            float confidence,
            out float conditionedPp,
            out float conditionedNn)
        {
            ppWeight = Sanitize01(ppWeight);
            nnWeight = Sanitize01(nnWeight);
            mShare = Sanitize01(mShare);
            confidence = Sanitize01(confidence);

            var candidateMass = ppWeight + nnWeight;
            var modelPp = candidateMass * mShare;
            conditionedPp = Mathf.Lerp(ppWeight, modelPp, confidence);
            conditionedNn = candidateMass - conditionedPp;

            // Guard Animator-facing callers against malformed non-simplex input
            // without perturbing the valid simplex path above.
            if (!IsFinite(conditionedPp) || !IsFinite(conditionedNn))
            {
                conditionedPp = ppWeight;
                conditionedNn = nnWeight;
            }
            else
            {
                conditionedPp = Mathf.Max(0f, conditionedPp);
                conditionedNn = Mathf.Max(0f, conditionedNn);
            }
        }

        public static float Logistic(float logit)
        {
            if (!IsFinite(logit)) return 0.5f;
            logit = Mathf.Clamp(logit, -16f, 16f);
            return 1f / (1f + Mathf.Exp(-logit));
        }

        public static void ApplyPhoneticConstraints(
            float ppWeight,
            float ffWeight,
            float ssWeight,
            float chWeight,
            float bilabialClosure,
            float labiodentalBite,
            float sibilantJawMaximum,
            ref float jawOpen,
            ref float lipClose,
            ref float lipBite)
        {
            lipClose = SmoothFloorProjection(
                lipClose, bilabialClosure, Sanitize01(ppWeight));
            lipBite = SmoothFloorProjection(
                lipBite, labiodentalBite, Sanitize01(ffWeight));
            var sibilant = Mathf.Clamp01(Sanitize01(ssWeight) + Sanitize01(chWeight));
            jawOpen = SmoothCeilingProjection(
                jawOpen, sibilantJawMaximum, sibilant);
        }

        public static int TrackingParameterBits(AdvancedVisemeTrackingInputs mode)
        {
            return TrackingParameterBits(mode, AdvancedVisemeTrackingEncoding.AdaptiveBinary);
        }

        public static int TrackingParameterBits(
            AdvancedVisemeTrackingInputs mode,
            AdvancedVisemeTrackingEncoding encoding)
        {
            if (mode == AdvancedVisemeTrackingInputs.Disabled ||
                mode == AdvancedVisemeTrackingInputs.ReuseExisting ||
                mode == AdvancedVisemeTrackingInputs.Auto)
                return 0;
            if (encoding == AdvancedVisemeTrackingEncoding.FullFloat)
            {
                if (mode == AdvancedVisemeTrackingInputs.FullTongue18) return 146;
                return mode == AdvancedVisemeTrackingInputs.Quality12 ? 98 : 66;
            }

            var articulators = mode == AdvancedVisemeTrackingInputs.Quality12 ||
                               mode == AdvancedVisemeTrackingInputs.FullTongue18
                ? new[]
                {
                    AdvancedVisemeArticulator.JawOpen, AdvancedVisemeArticulator.LipClose,
                    AdvancedVisemeArticulator.MouthOpen, AdvancedVisemeArticulator.LipFunnel,
                    AdvancedVisemeArticulator.LipPucker, AdvancedVisemeArticulator.LipSuck,
                    AdvancedVisemeArticulator.SmileSad, AdvancedVisemeArticulator.TongueOut,
                    AdvancedVisemeArticulator.JawX, AdvancedVisemeArticulator.JawZ,
                    AdvancedVisemeArticulator.MouthX, AdvancedVisemeArticulator.TongueY
                }
                : new[]
                {
                    AdvancedVisemeArticulator.JawOpen, AdvancedVisemeArticulator.LipClose,
                    AdvancedVisemeArticulator.MouthOpen, AdvancedVisemeArticulator.LipFunnel,
                    AdvancedVisemeArticulator.LipPucker, AdvancedVisemeArticulator.LipSuck,
                    AdvancedVisemeArticulator.SmileSad, AdvancedVisemeArticulator.TongueOut
                };

            if (mode == AdvancedVisemeTrackingInputs.FullTongue18)
            {
                articulators = articulators.Concat(new[]
                {
                    AdvancedVisemeArticulator.TongueX,
                    AdvancedVisemeArticulator.TongueRoll,
                    AdvancedVisemeArticulator.TongueArchY,
                    AdvancedVisemeArticulator.TongueShape,
                    AdvancedVisemeArticulator.TongueTwistRight,
                    AdvancedVisemeArticulator.TongueTwistLeft
                }).ToArray();
            }

            var bits = 2; // LipTrackingActive and the manual enable toggle.
            foreach (var articulator in articulators)
            {
                bits += TrackingMagnitudeBits(articulator, encoding);
                if (IsSignedTrackingArticulator(articulator)) bits++;
            }
            return bits;
        }

        public static int TrackingMagnitudeBits(
            AdvancedVisemeArticulator articulator,
            AdvancedVisemeTrackingEncoding encoding)
        {
            if (encoding == AdvancedVisemeTrackingEncoding.FullFloat) return 8;
            if (encoding == AdvancedVisemeTrackingEncoding.Uniform4BitBinary) return 4;
            switch (articulator)
            {
                case AdvancedVisemeArticulator.JawOpen: return 4;
                case AdvancedVisemeArticulator.LipClose: return 2;
                case AdvancedVisemeArticulator.MouthOpen: return 3;
                case AdvancedVisemeArticulator.LipFunnel: return 3;
                case AdvancedVisemeArticulator.LipPucker: return 3;
                case AdvancedVisemeArticulator.LipSuck: return 2;
                case AdvancedVisemeArticulator.SmileSad: return 3;
                case AdvancedVisemeArticulator.TongueOut: return 2;
                case AdvancedVisemeArticulator.JawX: return 3;
                case AdvancedVisemeArticulator.JawZ: return 2;
                case AdvancedVisemeArticulator.MouthX: return 3;
                case AdvancedVisemeArticulator.TongueY: return 2;
                case AdvancedVisemeArticulator.TongueX: return 3;
                case AdvancedVisemeArticulator.TongueRoll: return 2;
                case AdvancedVisemeArticulator.TongueArchY: return 3;
                case AdvancedVisemeArticulator.TongueShape: return 3;
                case AdvancedVisemeArticulator.TongueTwistRight: return 2;
                case AdvancedVisemeArticulator.TongueTwistLeft: return 2;
                default: return 2;
            }
        }

        public static bool IsSignedTrackingArticulator(AdvancedVisemeArticulator articulator)
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

        public static void NormalizeSimplex(float[] values)
        {
            var sum = 0f;
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = Sanitize01(values[i]);
                sum += values[i];
            }

            if (sum <= 1e-8f)
            {
                Array.Clear(values, 0, values.Length);
                values[0] = 1f;
                return;
            }

            for (var i = 0; i < values.Length; i++) values[i] /= sum;
        }

        private static float Sanitize01(float value)
        {
            return IsFinite(value) ? Mathf.Clamp01(value) : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
