using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.Components.Editor.Tests
{
    public sealed class AdvancedVisemeOculusDynamicsTests
    {
        private const float SimplexTolerance = 4e-6f;
        private const float CurveTolerance = 5e-5f;
        private const float SupportTolerance = 1e-7f;

        [Test]
        public void GeneratedControlsAreFiniteNonnegativeSimplexesWithDominantWinner()
        {
            Assert.That(AdvancedVisemeOculusDynamics.VisemeCount,
                Is.EqualTo(VisemeReconstructionProfile.VisemeCount));
            Assert.That(AdvancedVisemeOculusDynamics.ControlPointCount,
                Is.EqualTo(5),
                "The direct trajectory requires four cubic controls and one " +
                "positive continuation endpoint.");

            for (var winner = 0;
                 winner < AdvancedVisemeOculusDynamics.VisemeCount;
                 winner++)
            for (var control = 0;
                 control < AdvancedVisemeOculusDynamics.ControlPointCount;
                 control++)
            {
                var sum = 0f;
                var diagonal = AdvancedVisemeOculusDynamics.Weight(
                    winner, control, winner);
                AssertFiniteUnit(diagonal,
                    $"trajectory[{winner},{control},{winner}]");

                for (var output = 0;
                     output < AdvancedVisemeOculusDynamics.VisemeCount;
                     output++)
                {
                    var value = AdvancedVisemeOculusDynamics.Weight(
                        winner, control, output);
                    AssertFiniteUnit(value,
                        $"trajectory[{winner},{control},{output}]");
                    sum += value;
                    if (output != winner)
                        Assert.That(diagonal, Is.GreaterThan(value),
                            $"Control {control} of winner {winner} must keep " +
                            "the hard Oculus winner as its unique maximum.");
                }

                Assert.That(sum, Is.EqualTo(1f).Within(SimplexTolerance),
                    $"Control {control} of winner {winner} must be normalized.");
            }
        }

        [Test]
        public void GeneratedTrajectoriesReuseTheStaticHaloSupportExactly()
        {
            for (var winner = 0;
                 winner < AdvancedVisemeOculusDynamics.VisemeCount;
                 winner++)
            for (var output = 0;
                 output < AdvancedVisemeOculusDynamics.VisemeCount;
                 output++)
            {
                var staticIsLive = AdvancedVisemeOculusHalo.Weight(winner, output) >
                                   SupportTolerance;
                for (var control = 0;
                     control < AdvancedVisemeOculusDynamics.ControlPointCount;
                     control++)
                {
                    var dynamicIsLive = AdvancedVisemeOculusDynamics.Weight(
                        winner, control, output) > SupportTolerance;
                    Assert.That(dynamicIsLive, Is.EqualTo(staticIsLive),
                        $"Winner {winner}, output {output}, control {control} " +
                        "changed the reviewed static TopK support.");
                }
            }
        }

        [Test]
        public void SilenceIsBitExactAndSpeechRowsContainLearnedMotion()
        {
            Assert.That(AdvancedVisemeOculusDynamics.HasDynamicTrajectory(0),
                Is.False, "Silence must not run a mouth trajectory.");
            for (var control = 0;
                 control < AdvancedVisemeOculusDynamics.ControlPointCount;
                 control++)
            for (var output = 0;
                 output < AdvancedVisemeOculusDynamics.VisemeCount;
                 output++)
                Assert.That(AdvancedVisemeOculusDynamics.Weight(
                        0, control, output),
                    Is.EqualTo(output == 0 ? 1f : 0f),
                    "Silence is an exact semantic endpoint.");

            for (var winner = 1;
                 winner < AdvancedVisemeOculusDynamics.VisemeCount;
                 winner++)
                Assert.That(
                    AdvancedVisemeOculusDynamics.HasDynamicTrajectory(winner),
                    Is.True,
                    $"Speech row {winner} unexpectedly collapsed to a static halo.");
        }

        [Test]
        public void PositivePiecewiseTrajectoryRemainsOnTheReviewedSimplex()
        {
            for (var winner = 0;
                 winner < AdvancedVisemeOculusDynamics.VisemeCount;
                 winner++)
            for (var sample = 0; sample <= 32; sample++)
            {
                var normalizedTime = sample / 32f;
                var values = Enumerable.Range(
                        0, AdvancedVisemeOculusDynamics.VisemeCount)
                    .Select(output => Trajectory(
                        winner, output,
                        normalizedTime *
                        AdvancedVisemeOculusDynamics.TrajectoryDurationSeconds))
                    .ToArray();

                foreach (var value in values)
                    AssertFiniteUnit(value,
                        $"winner {winner}, t={normalizedTime:R}");
                Assert.That(values.Sum(),
                    Is.EqualTo(1f).Within(SimplexTolerance),
                    $"Winner {winner} left the simplex at t={normalizedTime:R}.");
                for (var output = 0; output < values.Length; output++)
                {
                    var staticIsLive = AdvancedVisemeOculusHalo.Weight(
                        winner, output) > SupportTolerance;
                    Assert.That(values[output] > SupportTolerance,
                        Is.EqualTo(staticIsLive),
                        $"Winner {winner}, output {output} changed support at " +
                        $"t={normalizedTime:R}.");
                }
            }
        }

        [Test]
        public void ThreeKeyHermiteEncodingMatchesPositivePiecewiseTrajectory()
        {
            var duration = AdvancedVisemeOculusDynamics.TrajectoryDurationSeconds;
            var core = AdvancedVisemeOculusDynamics.TrajectoryCoreDurationSeconds;
            Assert.That(duration, Is.GreaterThan(0f));
            Assert.That(core, Is.GreaterThan(0f).And.LessThan(duration));

            for (var winner = 0;
                 winner < AdvancedVisemeOculusDynamics.VisemeCount;
                 winner++)
            for (var output = 0;
                 output < AdvancedVisemeOculusDynamics.VisemeCount;
                 output++)
            {
                var p0 = AdvancedVisemeOculusDynamics.Weight(winner, 0, output);
                var p1 = AdvancedVisemeOculusDynamics.Weight(winner, 1, output);
                var p2 = AdvancedVisemeOculusDynamics.Weight(winner, 2, output);
                var p3 = AdvancedVisemeOculusDynamics.Weight(winner, 3, output);
                var p4 = AdvancedVisemeOculusDynamics.Weight(winner, 4, output);
                var start = new Keyframe(
                    0f, p0, 0f, 3f * (p1 - p0) / core);
                var tailSlope = (p4 - p3) / (duration - core);
                var seam = new Keyframe(
                    core, p3, 3f * (p3 - p2) / core, tailSlope);
                var end = new Keyframe(
                    duration, p4, tailSlope, 0f);
                start.weightedMode = WeightedMode.None;
                seam.weightedMode = WeightedMode.None;
                end.weightedMode = WeightedMode.None;
                var curve = new AnimationCurve(start, seam, end)
                {
                    preWrapMode = WrapMode.ClampForever,
                    postWrapMode = WrapMode.ClampForever
                };

                for (var sample = 0; sample <= 32; sample++)
                {
                    var normalizedTime = sample / 32f;
                    var time = normalizedTime * duration;
                    var expected = Trajectory(
                        p0, p1, p2, p3, p4, time, core, duration);
                    var actual = curve.Evaluate(time);
                    Assert.That(actual,
                        Is.EqualTo(expected).Within(CurveTolerance),
                        $"Winner {winner}, output {output}, " +
                        $"t={normalizedTime:R}");
                }

                Assert.That(curve.Evaluate(-duration),
                    Is.EqualTo(p0).Within(CurveTolerance));
                Assert.That(curve.Evaluate(2f * duration),
                    Is.EqualTo(p4).Within(CurveTolerance));
            }
        }

        [Test]
        public void GeneratedDynamicsPublishesVersionedProvenance()
        {
            Assert.That(AdvancedVisemeOculusDynamics.ModelVersion,
                Is.EqualTo(2));
            Assert.That(AdvancedVisemeOculusDynamics.TrajectoryDurationSeconds,
                Is.EqualTo(0.224f).Within(1e-7f));
            Assert.That(AdvancedVisemeOculusDynamics.TrajectoryCoreDurationSeconds,
                Is.EqualTo(0.168f).Within(1e-7f));
            Assert.That(AdvancedVisemeOculusDynamics.TargetCrossfadeSeconds,
                Is.EqualTo(0.072f).Within(1e-7f));
            Assert.That(AdvancedVisemeOculusDynamics.ObserverResponseSeconds,
                Is.EqualTo(0.017f).Within(1e-7f));
            Assert.That(AdvancedVisemeOculusDynamics.EvaluationLiveliness,
                Is.Zero.Within(1e-7f),
                "The legacy observer audit baseline remains pure-slow; direct " +
                "rendering does not consume this value.");
            Assert.That(AdvancedVisemeOculusDynamics.SourceHaloTableSha256,
                Is.EqualTo(AdvancedVisemeOculusHalo.TableSha256),
                "Dynamics must be fitted over the exact reviewed halo support.");

            var hashes = typeof(AdvancedVisemeOculusDynamics)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(string) &&
                                field.Name.EndsWith(
                                    "Sha256", StringComparison.Ordinal))
                .Select(field => new
                {
                    field.Name,
                    Value = (string)field.GetValue(null)
                })
                .ToArray();

            Assert.That(hashes.Select(hash => hash.Name), Is.EquivalentTo(new[]
            {
                "ContentSha256",
                "ModelSha256",
                "SourceHaloTableSha256"
            }));
            foreach (var hash in hashes)
                Assert.That(hash.Value, Does.Match("^[0-9a-f]{64}$"),
                    hash.Name + " must be a lowercase SHA-256 digest.");
            Assert.That(hashes.Select(hash => hash.Value), Is.Unique);
        }

        private static float Trajectory(
            int winner,
            int output,
            float time)
        {
            return Trajectory(
                AdvancedVisemeOculusDynamics.Weight(winner, 0, output),
                AdvancedVisemeOculusDynamics.Weight(winner, 1, output),
                AdvancedVisemeOculusDynamics.Weight(winner, 2, output),
                AdvancedVisemeOculusDynamics.Weight(winner, 3, output),
                AdvancedVisemeOculusDynamics.Weight(winner, 4, output),
                time,
                AdvancedVisemeOculusDynamics.TrajectoryCoreDurationSeconds,
                AdvancedVisemeOculusDynamics.TrajectoryDurationSeconds);
        }

        private static float Trajectory(
            float p0,
            float p1,
            float p2,
            float p3,
            float p4,
            float time,
            float core,
            float duration)
        {
            if (time >= core)
                return Mathf.Lerp(
                    p3, p4,
                    Mathf.InverseLerp(core, duration, time));
            var normalizedTime = Mathf.Clamp01(time / core);
            var inverse = 1f - normalizedTime;
            return inverse * inverse * inverse * p0 +
                   3f * inverse * inverse * normalizedTime * p1 +
                   3f * inverse * normalizedTime * normalizedTime * p2 +
                   normalizedTime * normalizedTime * normalizedTime * p3;
        }

        private static void AssertFiniteUnit(float value, string description)
        {
            Assert.That(float.IsNaN(value) || float.IsInfinity(value), Is.False,
                description + " must be finite.");
            Assert.That(value,
                Is.InRange(-SimplexTolerance, 1f + SimplexTolerance),
                description + " must remain in [0, 1].");
        }
    }
}
