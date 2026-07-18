using System;
using System.Collections.Generic;
using System.Linq;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Build-time acceptance gate for approximate Beta-retention realizations.
    /// The current controller remains the exact teacher. A reduced realization
    /// may be selected only when it is cheaper and satisfies both a universal
    /// simplex error bound and held-out causal replay limits.
    /// </summary>
    internal static class AdvancedVisemeRetentionReductionGate
    {
        internal const int Version = 1;
        internal const float MaximumCoefficientError = 0.05f;
        internal const float MaximumReplayRms = 0.01f;
        internal const float MaximumReplayP99 = 0.025f;
        internal const float MaximumReplayError = 0.05f;
        internal const float MaximumVelocityRms = 0.02f;

        internal sealed class Candidate
        {
            public string name;
            public float[] retention;
            public int estimatedActiveBindings;
            public bool preservesAnimatorEpochs;
            public bool preservesSteadyEndpoints;
            public bool preservesMandatoryConstraints;
            public ReplayMetrics replay;
        }

        internal readonly struct ReplayMetrics
        {
            public readonly int sampleCount;
            public readonly float rms;
            public readonly float p99;
            public readonly float maximum;
            public readonly float velocityRms;

            public ReplayMetrics(
                int sampleCount,
                float rms,
                float p99,
                float maximum,
                float velocityRms)
            {
                this.sampleCount = sampleCount;
                this.rms = rms;
                this.p99 = p99;
                this.maximum = maximum;
                this.velocityRms = velocityRms;
            }
        }

        internal sealed class Certificate
        {
            public string candidateName;
            public bool accepted;
            public float coefficientRms;
            public float coefficientMaximum;
            public readonly float[] groupCoefficientMaximum =
                new float[AdvancedVisemeTransitionRetention.GroupCount];
            public readonly List<string> rejectionReasons = new List<string>();
        }

        internal static Certificate Evaluate(
            Candidate candidate,
            int exactActiveBindings)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (exactActiveBindings <= 0)
                throw new ArgumentOutOfRangeException(nameof(exactActiveBindings));

            var expectedLength = AdvancedVisemeTransitionRetention.GroupCount *
                                 AdvancedVisemeTransitionRetention.VisemeCount *
                                 AdvancedVisemeTransitionRetention.VisemeCount;
            if (candidate.retention == null ||
                candidate.retention.Length != expectedLength)
                throw new ArgumentException(
                    $"A reduced retention tensor requires {expectedLength} coefficients.",
                    nameof(candidate));

            var certificate = new Certificate
            {
                candidateName = string.IsNullOrWhiteSpace(candidate.name)
                    ? "Unnamed reduction"
                    : candidate.name
            };
            var squared = 0d;
            var finite = true;
            var offset = 0;
            for (var groupIndex = 0;
                 groupIndex < AdvancedVisemeTransitionRetention.GroupCount;
                 groupIndex++)
            {
                var groupMaximum = 0f;
                for (var previous = 0;
                     previous < AdvancedVisemeTransitionRetention.VisemeCount;
                     previous++)
                for (var current = 0;
                     current < AdvancedVisemeTransitionRetention.VisemeCount;
                     current++, offset++)
                {
                    var value = candidate.retention[offset];
                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        finite = false;
                        continue;
                    }
                    var teacher = AdvancedVisemeCoarticulationModel.Retention(
                        (AdvancedVisemeArticulatorGroup)groupIndex,
                        previous, current);
                    var error = Math.Abs(value - teacher);
                    squared += error * error;
                    groupMaximum = Math.Max(groupMaximum, error);
                    certificate.coefficientMaximum = Math.Max(
                        certificate.coefficientMaximum, error);
                }
                certificate.groupCoefficientMaximum[groupIndex] = groupMaximum;
            }
            certificate.coefficientRms = finite
                ? (float)Math.Sqrt(squared / expectedLength)
                : float.PositiveInfinity;
            if (!finite)
                certificate.rejectionReasons.Add(
                    "The reduced tensor contains a non-finite coefficient.");

            // For legal context and destination simplexes c and f:
            // |c^T (R-Rhat) f| <= max_ij |R_ij-Rhat_ij|.
            // This bound covers every possible viseme history, not only replayed
            // corpus trajectories.
            if (certificate.coefficientMaximum > MaximumCoefficientError)
                certificate.rejectionReasons.Add(
                    $"Universal simplex bound {certificate.coefficientMaximum:G6} " +
                    $"exceeds {MaximumCoefficientError:G6}.");
            if (candidate.estimatedActiveBindings <= 0 ||
                candidate.estimatedActiveBindings >= exactActiveBindings)
                certificate.rejectionReasons.Add(
                    "The reduced graph does not lower estimated active bindings.");
            if (!candidate.preservesAnimatorEpochs)
                certificate.rejectionReasons.Add(
                    "Animator feedback epochs are not frame-for-frame preserved.");
            if (!candidate.preservesSteadyEndpoints)
                certificate.rejectionReasons.Add(
                    "Steady viseme or tracking endpoints are not preserved.");
            if (!candidate.preservesMandatoryConstraints)
                certificate.rejectionReasons.Add(
                    "Mandatory PP/FF/SS/CH constraints are not preserved.");

            ValidateReplay(candidate.replay, certificate.rejectionReasons);
            certificate.accepted = certificate.rejectionReasons.Count == 0;
            return certificate;
        }

        internal static float[] ExactTeacherTensor()
        {
            var values = new float[
                AdvancedVisemeTransitionRetention.GroupCount *
                AdvancedVisemeTransitionRetention.VisemeCount *
                AdvancedVisemeTransitionRetention.VisemeCount];
            var offset = 0;
            for (var group = 0;
                 group < AdvancedVisemeTransitionRetention.GroupCount;
                 group++)
            for (var previous = 0;
                 previous < AdvancedVisemeTransitionRetention.VisemeCount;
                 previous++)
            for (var current = 0;
                 current < AdvancedVisemeTransitionRetention.VisemeCount;
                 current++)
                values[offset++] = AdvancedVisemeCoarticulationModel.Retention(
                    (AdvancedVisemeArticulatorGroup)group, previous, current);
            return values;
        }

        private static void ValidateReplay(
            ReplayMetrics replay,
            ICollection<string> reasons)
        {
            if (replay.sampleCount <= 0)
            {
                reasons.Add("No held-out causal replay samples were supplied.");
                return;
            }
            var values = new[]
            {
                replay.rms, replay.p99, replay.maximum, replay.velocityRms
            };
            if (values.Any(value => float.IsNaN(value) || float.IsInfinity(value) ||
                                    value < 0f))
            {
                reasons.Add("Held-out replay metrics are invalid.");
                return;
            }
            if (replay.rms > MaximumReplayRms)
                reasons.Add($"Replay RMS {replay.rms:G6} exceeds " +
                            $"{MaximumReplayRms:G6}.");
            if (replay.p99 > MaximumReplayP99)
                reasons.Add($"Replay p99 {replay.p99:G6} exceeds " +
                            $"{MaximumReplayP99:G6}.");
            if (replay.maximum > MaximumReplayError)
                reasons.Add($"Replay maximum {replay.maximum:G6} exceeds " +
                            $"{MaximumReplayError:G6}.");
            if (replay.velocityRms > MaximumVelocityRms)
                reasons.Add($"Velocity RMS {replay.velocityRms:G6} exceeds " +
                            $"{MaximumVelocityRms:G6}.");
        }
    }
}
