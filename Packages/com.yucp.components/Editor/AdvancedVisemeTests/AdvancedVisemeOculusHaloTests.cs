using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.Components.Editor.Tests
{
    public sealed class AdvancedVisemeOculusHaloTests
    {
        private const float MatrixTolerance = 3e-6f;
        private const float ObserverTolerance = 8e-6f;

        [Test]
        public void GeneratedHaloIsTopK5FiniteRowStochasticAndWinnerDominant()
        {
            Assert.That(AdvancedVisemeOculusHalo.ModelVersion, Is.EqualTo(3));
            Assert.That(AdvancedVisemeOculusHalo.VisemeCount,
                Is.EqualTo(VisemeReconstructionProfile.VisemeCount));
            Assert.That(AdvancedVisemeOculusHalo.TopK, Is.EqualTo(5),
                "The selected shipping model is the held-out TopK5 halo.");

            var hasOffDiagonalMass = false;
            for (var winner = 0;
                 winner < AdvancedVisemeOculusHalo.VisemeCount;
                 winner++)
            {
                var sum = 0f;
                var support = 0;
                var diagonal = AdvancedVisemeOculusHalo.Weight(winner, winner);
                AssertFiniteUnit(diagonal, $"halo[{winner},{winner}]");

                for (var output = 0;
                     output < AdvancedVisemeOculusHalo.VisemeCount;
                     output++)
                {
                    var value = AdvancedVisemeOculusHalo.Weight(winner, output);
                    AssertFiniteUnit(value, $"halo[{winner},{output}]");
                    sum += value;
                    if (value > 1e-7f) support++;
                    if (winner == output) continue;

                    hasOffDiagonalMass |= value > MatrixTolerance;
                    Assert.That(diagonal, Is.GreaterThan(value),
                        $"Winner {winner} must remain the unique maximum of its row.");
                }

                Assert.That(sum, Is.EqualTo(1f).Within(MatrixTolerance),
                    $"Halo row {winner} must remain a normalized simplex.");
                if (winner == 0)
                    Assert.That(support, Is.EqualTo(1),
                        "Silence must contain one live coordinate.");
                else
                    Assert.That(support,
                        Is.LessThanOrEqualTo(AdvancedVisemeOculusHalo.TopK),
                        $"Halo row {winner} exceeds its generated support budget.");
            }

            Assert.That(hasOffDiagonalMass, Is.True,
                "A learned halo must contain useful off-diagonal probability mass.");
        }

        [Test]
        public void GeneratedHaloKeepsSilenceExactlyNeutral()
        {
            for (var output = 0;
                 output < AdvancedVisemeOculusHalo.VisemeCount;
                 output++)
                Assert.That(AdvancedVisemeOculusHalo.Weight(0, output),
                    Is.EqualTo(output == 0 ? 1f : 0f),
                    "Silence is an exact semantic endpoint, not a speech halo.");
        }

        [Test]
        public void GeneratedContractContainsNoStateLocalTrajectoryClock()
        {
            var trajectoryMembers = typeof(AdvancedVisemeOculusHalo)
                .GetMembers(BindingFlags.Public | BindingFlags.Static)
                .Where(member => member.Name.IndexOf(
                    "Trajectory", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(member => member.Name)
                .ToArray();

            Assert.That(trajectoryMembers, Is.Empty,
                "A hard-viseme switch must retarget the shared live observer; " +
                "it must not restart a state-local canned trajectory.");
        }

        [Test]
        public void GeneratedHaloPublishesVersionedStaticProvenance()
        {
            Assert.That(AdvancedVisemeOculusHalo.EvaluationLiveliness,
                Is.EqualTo(0.85f).Within(1e-6f),
                "Static-halo provenance must not change when runtime observer " +
                "defaults evolve.");
            Assert.That(AdvancedVisemeOculusHalo.ObserverResponseSeconds,
                Is.EqualTo(0.024f).Within(1e-6f));
            Assert.That(AdvancedVisemeOculusHalo.HaloStrength,
                Is.EqualTo(0.79f).Within(1e-6f));

            var hashes = typeof(AdvancedVisemeOculusHalo)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(string) &&
                                field.Name.EndsWith("Sha256", StringComparison.Ordinal))
                .Select(field => new
                {
                    field.Name,
                    Value = (string)field.GetValue(null)
                })
                .ToArray();

            Assert.That(hashes.Select(hash => hash.Name),
                Is.EquivalentTo(new[] { "ContentSha256", "TableSha256" }));
            foreach (var hash in hashes)
                Assert.That(hash.Value, Does.Match("^[0-9a-f]{64}$"),
                    hash.Name + " must be a lowercase SHA-256 digest.");
            Assert.That(hashes.Select(hash => hash.Value), Is.Unique);
        }

        [Test]
        public void HaloMapsEverySampledSimplexToAConvexSimplex()
        {
            var random = new System.Random(0x51A1E5);
            var count = AdvancedVisemeOculusHalo.VisemeCount;
            var halo = HaloMatrix();
            for (var trial = 0; trial < 1024; trial++)
            {
                var input = RandomSimplex(random, count);
                var output = Multiply(input, halo);
                AssertSimplex(output, "halo simplex output");

                var authoredPose = Enumerable.Range(0, count)
                    .Select(_ => (float)(4.0 * random.NextDouble() - 2.0))
                    .ToArray();
                var projected = Dot(output, authoredPose);
                Assert.That(projected, Is.InRange(
                    authoredPose.Min() - MatrixTolerance,
                    authoredPose.Max() + MatrixTolerance),
                    "A row-stochastic halo may only select a convex authored pose.");
            }
        }

        [Test]
        public void HaloMatrixCommutesWithLinearPoseProjection()
        {
            var random = new System.Random(0xC0A471C);
            var count = AdvancedVisemeOculusHalo.VisemeCount;
            var halo = HaloMatrix();
            const int articulationCount = 12;

            for (var trial = 0; trial < 256; trial++)
            {
                var simplex = RandomSimplex(random, count);
                var coefficients = new float[count, articulationCount];
                for (var viseme = 0; viseme < count; viseme++)
                for (var articulation = 0;
                     articulation < articulationCount;
                     articulation++)
                    coefficients[viseme, articulation] =
                        (float)(2.0 * random.NextDouble() - 1.0);

                var haloThenProject = Multiply(
                    Multiply(simplex, halo), coefficients);
                var commuteThenProject = Multiply(
                    simplex, Multiply(halo, coefficients));
                for (var articulation = 0;
                     articulation < articulationCount;
                     articulation++)
                    Assert.That(commuteThenProject[articulation],
                        Is.EqualTo(haloThenProject[articulation])
                            .Within(MatrixTolerance));
            }
        }

        [TestCase(15)]
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(90)]
        [TestCase(144)]
        public void SharedTwoPoleObserverStaysFiniteAndOnTheSimplexAtRenderRates(
            int framesPerSecond)
        {
            var random = new System.Random(0x0B5E7E + framesPerSecond);
            var fast = HaloRow(0);
            var slow = HaloRow(0);
            var deltaTime = 1f / framesPerSecond;
            var lead = AdvancedVisemeOculusHalo.EvaluationLiveliness;

            for (var frame = 0; frame < framesPerSecond * 20; frame++)
            {
                var target = HaloRow(random.Next(
                    AdvancedVisemeOculusHalo.VisemeCount));
                ObserverStep(fast, slow, target, deltaTime);
                AssertSimplex(fast, $"{framesPerSecond} FPS fast frame {frame}");
                AssertSimplex(slow, $"{framesPerSecond} FPS slow frame {frame}");
                AssertSimplex(Blend(slow, fast, lead),
                    $"{framesPerSecond} FPS render frame {frame}");
            }
        }

        [TestCase(15)]
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(90)]
        [TestCase(144)]
        public void HeldStaticTargetConvergesWithoutOvershoot(int framesPerSecond)
        {
            var deltaTime = 1f / framesPerSecond;
            var lead = AdvancedVisemeOculusHalo.EvaluationLiveliness;
            var frameCount = Mathf.CeilToInt(16f *
                AdvancedVisemeOculusHalo.ObserverResponseSeconds / deltaTime);

            for (var winner = 1;
                 winner < AdvancedVisemeOculusHalo.VisemeCount;
                 winner++)
            {
                var target = HaloRow(winner);
                var fast = HaloRow(0);
                var slow = HaloRow(0);
                var previous = Blend(slow, fast, lead);
                for (var frame = 0; frame < frameCount; frame++)
                {
                    ObserverStep(fast, slow, target, deltaTime);
                    var rendered = Blend(slow, fast, lead);
                    for (var output = 0; output < rendered.Length; output++)
                    {
                        var direction = Math.Sign(target[output] - previous[output]);
                        if (direction > 0)
                            Assert.That(rendered[output],
                                Is.InRange(previous[output] - ObserverTolerance,
                                    target[output] + ObserverTolerance));
                        else if (direction < 0)
                            Assert.That(rendered[output],
                                Is.InRange(target[output] - ObserverTolerance,
                                    previous[output] + ObserverTolerance));
                    }
                    previous = rendered;
                }

                for (var output = 0; output < target.Length; output++)
                    Assert.That(previous[output],
                        Is.EqualTo(target[output]).Within(8e-4f),
                        $"{framesPerSecond} FPS winner {winner}, output {output}");
            }
        }

        [Test]
        public void InterruptionRetargetsTheLiveObserverWithoutAStateReset()
        {
            const float deltaTime = 1f / 90f;
            var lead = AdvancedVisemeOculusHalo.EvaluationLiveliness;
            var fast = HaloRow(0);
            var slow = HaloRow(0);
            var firstTarget = HaloRow(10);
            for (var frame = 0; frame < 4; frame++)
                ObserverStep(fast, slow, firstTarget, deltaTime);

            var fastBefore = (float[])fast.Clone();
            var slowBefore = (float[])slow.Clone();
            var renderBefore = Blend(slowBefore, fastBefore, lead);
            var secondTarget = HaloRow(12);
            ObserverStep(fast, slow, secondTarget, deltaTime);
            var renderAfter = Blend(slow, fast, lead);

            var expectedFast = (float[])fastBefore.Clone();
            var expectedSlow = (float[])slowBefore.Clone();
            ObserverStep(expectedFast, expectedSlow, secondTarget, deltaTime);
            for (var output = 0; output < fast.Length; output++)
            {
                Assert.That(fast[output],
                    Is.EqualTo(expectedFast[output]).Within(ObserverTolerance));
                Assert.That(slow[output],
                    Is.EqualTo(expectedSlow[output]).Within(ObserverTolerance));
            }
            AssertSimplex(renderAfter, "interrupted render");

            // A convex observer step can move toward the new target, but cannot
            // jump farther than the complete remaining target displacement.
            Assert.That(L1(renderAfter, renderBefore),
                Is.LessThan(L1(secondTarget, renderBefore)));

            var coldFast = (float[])secondTarget.Clone();
            var coldSlow = (float[])secondTarget.Clone();
            Assert.That(L1(renderAfter, Blend(coldSlow, coldFast, lead)),
                Is.GreaterThan(1e-3f),
                "The interrupt was incorrectly replaced by the new state's endpoint.");
        }

        private static void ObserverStep(
            float[] fast,
            float[] slow,
            float[] target,
            float deltaTime)
        {
            var alpha = AdvancedVisemeMath.Alpha(
                deltaTime, AdvancedVisemeOculusHalo.ObserverResponseSeconds);
            for (var output = 0; output < target.Length; output++)
            {
                fast[output] += alpha * (target[output] - fast[output]);
                slow[output] += alpha * (fast[output] - slow[output]);
            }
        }

        private static float[] HaloRow(int winner)
        {
            return Enumerable.Range(0, AdvancedVisemeOculusHalo.VisemeCount)
                .Select(output => AdvancedVisemeOculusHalo.Weight(winner, output))
                .ToArray();
        }

        private static float[,] HaloMatrix()
        {
            var count = AdvancedVisemeOculusHalo.VisemeCount;
            var result = new float[count, count];
            for (var row = 0; row < count; row++)
            for (var column = 0; column < count; column++)
                result[row, column] = AdvancedVisemeOculusHalo.Weight(row, column);
            return result;
        }

        private static float[] RandomSimplex(System.Random random, int count)
        {
            var result = new float[count];
            var sum = 0f;
            for (var i = 0; i < count; i++)
            {
                result[i] = (float)(-Math.Log(
                    Math.Max(1e-12, random.NextDouble())));
                sum += result[i];
            }
            for (var i = 0; i < count; i++) result[i] /= sum;
            return result;
        }

        private static float[] Blend(float[] from, float[] to, float weight)
        {
            var result = new float[from.Length];
            for (var i = 0; i < result.Length; i++)
                result[i] = from[i] + weight * (to[i] - from[i]);
            return result;
        }

        private static float[] Multiply(float[] row, float[,] matrix)
        {
            var result = new float[matrix.GetLength(1)];
            for (var column = 0; column < result.Length; column++)
            for (var index = 0; index < row.Length; index++)
                result[column] += row[index] * matrix[index, column];
            return result;
        }

        private static float[,] Multiply(float[,] left, float[,] right)
        {
            var result = new float[left.GetLength(0), right.GetLength(1)];
            for (var row = 0; row < result.GetLength(0); row++)
            for (var column = 0; column < result.GetLength(1); column++)
            for (var index = 0; index < left.GetLength(1); index++)
                result[row, column] += left[row, index] * right[index, column];
            return result;
        }

        private static float Dot(float[] left, float[] right)
        {
            var result = 0f;
            for (var i = 0; i < left.Length; i++) result += left[i] * right[i];
            return result;
        }

        private static float L1(float[] left, float[] right)
        {
            var result = 0f;
            for (var i = 0; i < left.Length; i++)
                result += Math.Abs(left[i] - right[i]);
            return result;
        }

        private static void AssertSimplex(float[] values, string description)
        {
            foreach (var value in values)
                AssertFiniteUnit(value, description);
            Assert.That(values.Sum(), Is.EqualTo(1f).Within(ObserverTolerance),
                description + " must remain normalized.");
        }

        private static void AssertFiniteUnit(float value, string description)
        {
            Assert.That(float.IsNaN(value) || float.IsInfinity(value), Is.False,
                description + " must be finite.");
            Assert.That(value, Is.InRange(-ObserverTolerance, 1f + ObserverTolerance),
                description + " must remain in [0, 1].");
        }
    }
}
