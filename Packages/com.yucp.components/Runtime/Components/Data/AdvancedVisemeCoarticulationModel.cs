using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components
{
    /// <summary>
    /// Data boundary for the avatar-side coarticulation observer. The runtime
    /// consumes only dimensionless aggregate retention, never corpus geometry.
    /// </summary>
    public interface IAdvancedVisemeCoarticulationSource
    {
        int Version { get; }
        string ContentSha256 { get; }
        float Retention(AdvancedVisemeArticulatorGroup group, int previousViseme, int currentViseme);
    }

    public static class AdvancedVisemeCoarticulationModel
    {
        public const int VisemeCount = 15;
        // Increment when the generated observer contract changes even if the
        // embedded transition corpus is unchanged. This participates in the
        // generated-asset hash so existing avatars rebuild deterministically.
        public const int ReconstructionVersion = 2;

        private sealed class EmbeddedCorpusSource : IAdvancedVisemeCoarticulationSource
        {
            public int Version => AdvancedVisemeTransitionRetention.ModelVersion;
            public string ContentSha256 => AdvancedVisemeTransitionRetention.ContentSha256;

            public float Retention(
                AdvancedVisemeArticulatorGroup group,
                int previousViseme,
                int currentViseme)
            {
                return AdvancedVisemeTransitionRetention.Retention(group, previousViseme, currentViseme);
            }
        }

        private static readonly IAdvancedVisemeCoarticulationSource Embedded = new EmbeddedCorpusSource();

        public static IAdvancedVisemeCoarticulationSource DefaultSource => Embedded;
        public static int ModelVersion => Embedded.Version;
        public static string ContentSha256 => Embedded.ContentSha256;

        public static float DecaySeconds(AdvancedVisemeArticulatorGroup group)
        {
            return Mathf.Max(0.001f, AdvancedVisemeTransitionRetention.DecaySeconds(group));
        }

        public static float MeanDecaySeconds
        {
            get
            {
                var sum = 0f;
                for (var group = 0; group < AdvancedVisemeTransitionRetention.GroupCount; group++)
                    sum += DecaySeconds((AdvancedVisemeArticulatorGroup)group);
                return sum / AdvancedVisemeTransitionRetention.GroupCount;
            }
        }

        public static AdvancedVisemeArticulatorGroup GroupFor(AdvancedVisemeArticulator articulator)
        {
            switch (articulator)
            {
                case AdvancedVisemeArticulator.JawOpen:
                case AdvancedVisemeArticulator.JawX:
                case AdvancedVisemeArticulator.JawZ:
                    return AdvancedVisemeArticulatorGroup.Jaw;

                case AdvancedVisemeArticulator.TongueOut:
                case AdvancedVisemeArticulator.TongueY:
                case AdvancedVisemeArticulator.TongueX:
                case AdvancedVisemeArticulator.TongueRoll:
                case AdvancedVisemeArticulator.TongueTwistRight:
                case AdvancedVisemeArticulator.TongueTwistLeft:
                    return AdvancedVisemeArticulatorGroup.TongueTip;

                case AdvancedVisemeArticulator.TongueArchY:
                case AdvancedVisemeArticulator.TongueShape:
                    return AdvancedVisemeArticulatorGroup.TongueBody;

                default:
                    return AdvancedVisemeArticulatorGroup.Lips;
            }
        }

        public static float Retention(
            AdvancedVisemeArticulatorGroup group,
            int previousViseme,
            int currentViseme)
        {
            return Mathf.Clamp01(Embedded.Retention(group, previousViseme, currentViseme));
        }

        public static float MeanRetention(int previousViseme, int currentViseme)
        {
            var sum = 0f;
            for (var group = 0; group < AdvancedVisemeTransitionRetention.GroupCount; group++)
            {
                sum += Retention((AdvancedVisemeArticulatorGroup)group, previousViseme, currentViseme);
            }
            return sum / AdvancedVisemeTransitionRetention.GroupCount;
        }

        /// <summary>
        /// Converts learned previous-pose retention into an interruptible observer
        /// lead. High carryover keeps the slow stage; low carryover moves toward the
        /// fast stage. Strength zero is therefore bit-for-bit Normal behavior.
        /// </summary>
        public static float TransitionLead(
            AdvancedVisemeArticulatorGroup group,
            IReadOnlyList<float> previousWeights,
            int currentViseme,
            float strength = 1f)
        {
            ValidateWeights(previousWeights);
            if ((uint)currentViseme >= VisemeCount) throw new ArgumentOutOfRangeException(nameof(currentViseme));

            var retention = 0f;
            for (var previous = 0; previous < VisemeCount; previous++)
            {
                retention += Mathf.Max(0f, previousWeights[previous]) *
                             Retention(group, previous, currentViseme);
            }
            return Mathf.Clamp01((1f - Mathf.Clamp01(retention)) * Mathf.Clamp01(strength));
        }

        public static float MeanTransitionLead(
            IReadOnlyList<float> previousWeights,
            int currentViseme,
            float strength = 1f)
        {
            ValidateWeights(previousWeights);
            if ((uint)currentViseme >= VisemeCount) throw new ArgumentOutOfRangeException(nameof(currentViseme));

            var retention = 0f;
            for (var previous = 0; previous < VisemeCount; previous++)
            {
                retention += Mathf.Max(0f, previousWeights[previous]) *
                             MeanRetention(previous, currentViseme);
            }
            return Mathf.Clamp01((1f - Mathf.Clamp01(retention)) * Mathf.Clamp01(strength));
        }

        public static void ReconstructWeights(
            IReadOnlyList<float> slow,
            IReadOnlyList<float> fast,
            int currentViseme,
            AdvancedVisemeArticulatorGroup group,
            float strength,
            float[] output)
        {
            ValidateWeights(slow);
            ValidateWeights(fast);
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (output.Length != VisemeCount)
                throw new ArgumentException("Coarticulation weights require exactly 15 output channels.", nameof(output));

            // Beta never leads the fast stage toward the raw one-hot winner.
            // The primary fast observer is the continuous upper envelope; this
            // method reconstructs only the slow head between slow and fast.
            var lead = TransitionLead(group, slow, currentViseme, strength);
            for (var i = 0; i < VisemeCount; i++) output[i] = Mathf.Lerp(slow[i], fast[i], lead);
        }

        private static void ValidateWeights(IReadOnlyList<float> weights)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            if (weights.Count != VisemeCount)
                throw new ArgumentException("Coarticulation weights require exactly 15 viseme channels.", nameof(weights));
        }
    }
}
