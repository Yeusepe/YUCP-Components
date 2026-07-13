using System;
using UnityEngine;

namespace YUCP.Components
{
    /// <summary>Pure numerical reference for the Animator implementation and editor tests.</summary>
    public static class AdvancedVisemeMath
    {
        public static float Alpha(float deltaTime, float responseSeconds)
        {
            if (!IsFinite(deltaTime) || deltaTime <= 0f) return 0f;
            if (!IsFinite(responseSeconds) || responseSeconds <= 0f) return 1f;
            return Mathf.Clamp01(1f - Mathf.Exp(-deltaTime / responseSeconds));
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

        public static float Fuse(float speech, float tracking, float gain)
        {
            gain = Sanitize01(gain);
            return Mathf.Lerp(speech, tracking, gain);
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
            lipClose = Mathf.Max(lipClose, Sanitize01(ppWeight) * Sanitize01(bilabialClosure));
            lipBite = Mathf.Max(lipBite, Sanitize01(ffWeight) * Sanitize01(labiodentalBite));
            var sibilant = Mathf.Clamp01(Sanitize01(ssWeight) + Sanitize01(chWeight));
            jawOpen = Mathf.Min(jawOpen, Mathf.Lerp(1f, Sanitize01(sibilantJawMaximum), sibilant));
        }

        public static int TrackingParameterBits(AdvancedVisemeTrackingInputs mode)
        {
            return TrackingParameterBits(mode, AdvancedVisemeTrackingEncoding.AdaptiveBinary);
        }

        public static int TrackingParameterBits(
            AdvancedVisemeTrackingInputs mode,
            AdvancedVisemeTrackingEncoding encoding)
        {
            if (mode == AdvancedVisemeTrackingInputs.Disabled || mode == AdvancedVisemeTrackingInputs.ReuseExisting)
                return 0;
            if (encoding == AdvancedVisemeTrackingEncoding.FullFloat)
                return mode == AdvancedVisemeTrackingInputs.Quality12 ? 98 : 66;

            var articulators = mode == AdvancedVisemeTrackingInputs.Quality12
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
                default: return 2;
            }
        }

        public static bool IsSignedTrackingArticulator(AdvancedVisemeArticulator articulator)
        {
            return articulator == AdvancedVisemeArticulator.SmileSad ||
                   articulator == AdvancedVisemeArticulator.JawX ||
                   articulator == AdvancedVisemeArticulator.JawZ ||
                   articulator == AdvancedVisemeArticulator.MouthX ||
                   articulator == AdvancedVisemeArticulator.TongueY;
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
