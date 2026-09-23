using System;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.Components.Editor.Tests
{
    public sealed class AdvancedVisemeCoarticulationModelTests
    {
        [Test]
        public void ComponentDefaultsToNormalReconstruction()
        {
            var root = new GameObject("Advanced Viseme Mode Test");
            try
            {
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                Assert.AreEqual(AdvancedVisemeReconstructionMode.Normal, component.reconstructionMode);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CorpusTableCoversEveryTransitionGroupAndStaysBounded()
        {
            Assert.Greater(AdvancedVisemeCoarticulationModel.ModelVersion, 0);
            Assert.That(AdvancedVisemeCoarticulationModel.ContentSha256, Has.Length.EqualTo(64));
            for (var groupIndex = 0; groupIndex < AdvancedVisemeTransitionRetention.GroupCount; groupIndex++)
            {
                var group = (AdvancedVisemeArticulatorGroup)groupIndex;
                for (var previous = 0; previous < AdvancedVisemeCoarticulationModel.VisemeCount; previous++)
                for (var current = 0; current < AdvancedVisemeCoarticulationModel.VisemeCount; current++)
                {
                    var retention = AdvancedVisemeCoarticulationModel.Retention(group, previous, current);
                    Assert.That(retention, Is.InRange(0f, 1f), $"{group} {previous}->{current}");
                    if (previous == current) Assert.That(retention, Is.Zero);
                }
            }
        }

        [Test]
        public void CorpusModelContainsPairAndArticulatorSpecificInformation()
        {
            var jawPpToSilence = AdvancedVisemeCoarticulationModel.Retention(
                AdvancedVisemeArticulatorGroup.Jaw, 1, 0);
            var lipsPpToSilence = AdvancedVisemeCoarticulationModel.Retention(
                AdvancedVisemeArticulatorGroup.Lips, 1, 0);
            var jawSilenceToPp = AdvancedVisemeCoarticulationModel.Retention(
                AdvancedVisemeArticulatorGroup.Jaw, 0, 1);

            Assert.That(jawPpToSilence, Is.Not.EqualTo(lipsPpToSilence).Within(1e-4f));
            Assert.That(jawPpToSilence, Is.Not.EqualTo(jawSilenceToPp).Within(1e-4f));
        }

        [Test]
        public void ReconstructedWeightsRemainANormalizedSimplex()
        {
            var random = new System.Random(712367);
            var slow = RandomSimplex(random);
            var fast = RandomSimplex(random);
            var output = new float[AdvancedVisemeCoarticulationModel.VisemeCount];

            for (var groupIndex = 0; groupIndex < AdvancedVisemeTransitionRetention.GroupCount; groupIndex++)
            for (var destination = 0; destination < output.Length; destination++)
            {
                AdvancedVisemeCoarticulationModel.ReconstructWeights(
                    slow, fast, destination, (AdvancedVisemeArticulatorGroup)groupIndex, 1f, output);

                var sum = 0f;
                for (var i = 0; i < output.Length; i++)
                {
                    Assert.That(output[i], Is.InRange(0f, 1f));
                    sum += output[i];
                }
                Assert.That(sum, Is.EqualTo(1f).Within(1e-6f));
            }
        }

        [Test]
        public void LearnedTransitionLeadIsAlwaysConvex()
        {
            var context = new float[AdvancedVisemeCoarticulationModel.VisemeCount];
            context[3] = 1f;
            for (var groupIndex = 0; groupIndex < AdvancedVisemeTransitionRetention.GroupCount; groupIndex++)
            for (var destination = 0; destination < AdvancedVisemeCoarticulationModel.VisemeCount; destination++)
            {
                var lead = AdvancedVisemeCoarticulationModel.TransitionLead(
                    (AdvancedVisemeArticulatorGroup)groupIndex, context, destination);
                Assert.That(lead, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void ZeroStrengthExactlyPreservesNormalSlowStage()
        {
            var slow = RandomSimplex(new System.Random(12));
            var fast = RandomSimplex(new System.Random(34));
            var output = new float[AdvancedVisemeCoarticulationModel.VisemeCount];
            AdvancedVisemeCoarticulationModel.ReconstructWeights(
                slow, fast, 1, AdvancedVisemeArticulatorGroup.Lips, 0f, output);
            for (var i = 0; i < output.Length; i++)
                Assert.That(output[i], Is.EqualTo(slow[i]).Within(1e-7f));
        }

        [TestCase(AdvancedVisemeVisibleTongueModelKind.Balanced)]
        [TestCase(AdvancedVisemeVisibleTongueModelKind.Quality)]
        public void VisibleTongueModelIsFiniteBoundedAndDeterministic(
            AdvancedVisemeVisibleTongueModelKind kind)
        {
            Assert.That(AdvancedVisemeVisibleTongueResidual.ContentSha256(kind), Has.Length.EqualTo(64));
            var random = new System.Random(20260713 + (int)kind);
            var visemes = RandomSimplex(random);
            var features = new float[AdvancedVisemeVisibleTongueResidual.FeatureCount(kind)];
            for (var i = 0; i < features.Length; i++)
                features[i] = Mathf.Lerp(-1f, 1f, (float)random.NextDouble());
            var first = new float[AdvancedVisemeVisibleTongueResidual.OutputCount];
            var second = new float[AdvancedVisemeVisibleTongueResidual.OutputCount];

            AdvancedVisemeVisibleTongueResidual.Predict(kind, visemes, features, first);
            AdvancedVisemeVisibleTongueResidual.Predict(kind, visemes, features, second);

            for (var i = 0; i < first.Length; i++)
            {
                Assert.That(float.IsNaN(first[i]) || float.IsInfinity(first[i]), Is.False);
                Assert.That(first[i], Is.InRange(-1f, 1f));
                Assert.That(second[i], Is.EqualTo(first[i]).Within(1e-7f));
            }
        }

        [TestCase(AdvancedVisemeVisibleTongueModelKind.Balanced)]
        [TestCase(AdvancedVisemeVisibleTongueModelKind.Quality)]
        public void VisibleTongueAnimatorFactorizationIsExactAndEveryStageIsSafelyNormalized(
            AdvancedVisemeVisibleTongueModelKind kind)
        {
            var featureCount = AdvancedVisemeVisibleTongueResidual.FeatureCount(kind);
            var latentCount = AdvancedVisemeVisibleTongueResidual.LatentCount(kind);
            var tongueCount = AdvancedVisemeVisibleTongueResidual.TongueLatentCount(kind);

            for (var feature = 0; feature < featureCount; feature++)
            {
                var p99 = AdvancedVisemeVisibleTongueResidual.FeatureAbsP99(kind, feature);
                var p995 = AdvancedVisemeVisibleTongueResidual.FeatureAbsP995(kind, feature);
                var safe = AdvancedVisemeVisibleTongueResidual.FeatureSafeBound(kind, feature);
                Assert.That(p99, Is.GreaterThan(0f));
                Assert.That(p995, Is.GreaterThanOrEqualTo(p99));
                Assert.That(p995, Is.LessThanOrEqualTo(safe));
            }

            for (var latent = 0; latent < latentCount; latent++)
            {
                var analytical = 0f;
                for (var feature = 0; feature < featureCount; feature++)
                    analytical += AdvancedVisemeVisibleTongueResidual.FeatureSafeBound(kind, feature) *
                                  Mathf.Abs(AdvancedVisemeVisibleTongueResidual.InputProjection(
                                      kind, feature, latent));
                Assert.That(analytical, Is.LessThanOrEqualTo(
                    AdvancedVisemeVisibleTongueResidual.VisibleLatentSafeBound(kind, latent)));
            }

            for (var viseme = 0; viseme < AdvancedVisemeVisibleTongueResidual.VisemeCount; viseme++)
            for (var tongue = 0; tongue < tongueCount; tongue++)
            {
                var analytical = Mathf.Abs(
                    AdvancedVisemeVisibleTongueResidual.VisemeBias(kind, viseme, tongue));
                for (var latent = 0; latent < latentCount; latent++)
                    analytical += AdvancedVisemeVisibleTongueResidual.VisibleLatentSafeBound(kind, latent) *
                                  Mathf.Abs(AdvancedVisemeVisibleTongueResidual.VisemeMix(
                                      kind, viseme, latent, tongue));
                Assert.That(analytical, Is.LessThanOrEqualTo(
                    AdvancedVisemeVisibleTongueResidual.ConditionalTongueLatentSafeBound(
                        kind, viseme, tongue)));
                Assert.That(AdvancedVisemeVisibleTongueResidual.ConditionalTongueLatentSafeBound(
                        kind, viseme, tongue), Is.LessThanOrEqualTo(
                        AdvancedVisemeVisibleTongueResidual.TongueLatentSafeBound(kind, tongue)));
            }

            foreach (AdvancedVisemeVisibleTongueOutput output in Enum.GetValues(
                         typeof(AdvancedVisemeVisibleTongueOutput)))
            {
                var analytical = 0f;
                for (var tongue = 0; tongue < tongueCount; tongue++)
                    analytical += AdvancedVisemeVisibleTongueResidual.TongueLatentSafeBound(kind, tongue) *
                                  Mathf.Abs(AdvancedVisemeVisibleTongueResidual.OutputProjection(
                                      kind, tongue, output));
                Assert.That(analytical, Is.LessThanOrEqualTo(
                    AdvancedVisemeVisibleTongueResidual.ConservativeOutputBound(kind, output)));
            }

            var random = new System.Random(0x51A7 + (int)kind);
            var collapsed = new float[AdvancedVisemeVisibleTongueResidual.OutputCount];
            var clamped = new float[AdvancedVisemeVisibleTongueResidual.OutputCount];
            for (var sample = 0; sample < 512; sample++)
            {
                var visemes = RandomSimplex(random);
                var features = new float[featureCount];
                for (var feature = 0; feature < featureCount; feature++)
                {
                    var bound = AdvancedVisemeVisibleTongueResidual.FeatureSafeBound(kind, feature);
                    features[feature] = sample < 2
                        ? (sample == 0 ? -bound : bound)
                        : Mathf.Lerp(-bound, bound, (float)random.NextDouble());
                }

                AdvancedVisemeVisibleTongueResidual.PredictUnclamped(
                    kind, visemes, features, collapsed);
                AdvancedVisemeVisibleTongueResidual.Predict(
                    kind, visemes, features, clamped);
                foreach (AdvancedVisemeVisibleTongueOutput output in Enum.GetValues(
                             typeof(AdvancedVisemeVisibleTongueOutput)))
                {
                    var normalized = EvaluateNormalizedFactorGraph(
                        kind, visemes, features, output, out var maximumStageMagnitude);
                    var outputScale = AdvancedVisemeVisibleTongueResidual.ConservativeOutputBound(
                        kind, output);
                    var factorValue = normalized * outputScale;
                    Assert.That(maximumStageMagnitude, Is.LessThanOrEqualTo(1.00001f));
                    Assert.That(factorValue, Is.EqualTo(collapsed[(int)output]).Within(2e-5f));

                    var mapped = EvaluatePiecewise(
                        AdvancedVisemeAnimatorBuilder.ScaledClampPoints(outputScale), normalized);
                    Assert.That(mapped, Is.EqualTo(clamped[(int)output]).Within(2e-5f));
                }
            }
        }

        [Test]
        public void BalancedTongueModelCannotSilentlyZeroFillJawAdvance()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                AdvancedVisemeVisibleTongueResidual.FeatureChannelIndex(
                    AdvancedVisemeVisibleTongueModelKind.Balanced,
                    AdvancedVisemeVisibleFeatureChannel.JawAdvance));
            Assert.That(AdvancedVisemeVisibleTongueResidual.FeatureChannelCount(
                AdvancedVisemeVisibleTongueModelKind.Balanced), Is.EqualTo(3));
            Assert.That(AdvancedVisemeVisibleTongueResidual.FeatureChannelCount(
                AdvancedVisemeVisibleTongueModelKind.Quality), Is.EqualTo(4));
        }

        [Test]
        public void HeadroomResidualNeverExtendsPastAuthoredRange()
        {
            Assert.That(AdvancedVisemeMath.ApplyBoundedResidual(0.8f, 1f, 1f, false),
                Is.EqualTo(1f).Within(1e-7f));
            Assert.That(AdvancedVisemeMath.ApplyBoundedResidual(0.8f, -1f, 1f, false),
                Is.EqualTo(0f).Within(1e-7f));
            Assert.That(AdvancedVisemeMath.ApplyBoundedResidual(-0.25f, 1f, 1f, true),
                Is.EqualTo(1f).Within(1e-7f));
            Assert.That(AdvancedVisemeMath.ApplyBoundedResidual(-0.25f, -1f, 1f, true),
                Is.EqualTo(-1f).Within(1e-7f));
            Assert.That(AdvancedVisemeMath.ApplyBoundedResidual(0.6f, 0.9f, 0f, false),
                Is.EqualTo(0.6f).Within(1e-7f));
        }

        [Test]
        public void VisibleResidualUsesAvailableHeadroomInsteadOfAdditiveDistance()
        {
            Assert.That(AdvancedVisemeMath.HeadroomNormalizedResidual(1f, 0.8f),
                Is.EqualTo(1f).Within(1e-6f));
            Assert.That(AdvancedVisemeMath.HeadroomNormalizedResidual(0f, 0.8f),
                Is.EqualTo(-1f).Within(1e-6f));
            Assert.That(AdvancedVisemeMath.HeadroomNormalizedResidual(0.9f, 0.8f),
                Is.EqualTo(0.5f).Within(1e-6f));
        }

        private static float[] RandomSimplex(System.Random random)
        {
            var output = new float[AdvancedVisemeCoarticulationModel.VisemeCount];
            var sum = 0f;
            for (var i = 0; i < output.Length; i++)
            {
                output[i] = (float)random.NextDouble() + 0.001f;
                sum += output[i];
            }
            for (var i = 0; i < output.Length; i++) output[i] /= sum;
            return output;
        }

        private static float EvaluateNormalizedFactorGraph(
            AdvancedVisemeVisibleTongueModelKind kind,
            float[] visemes,
            float[] features,
            AdvancedVisemeVisibleTongueOutput output,
            out float maximumStageMagnitude)
        {
            maximumStageMagnitude = 0f;
            var latent = new float[AdvancedVisemeVisibleTongueResidual.LatentCount(kind)];
            for (var index = 0; index < latent.Length; index++)
            {
                var scale = AdvancedVisemeVisibleTongueResidual.VisibleLatentSafeBound(kind, index);
                for (var feature = 0; feature < features.Length; feature++)
                    latent[index] += features[feature] *
                                     AdvancedVisemeVisibleTongueResidual.InputProjection(
                                         kind, feature, index) / scale;
                maximumStageMagnitude = Mathf.Max(maximumStageMagnitude, Mathf.Abs(latent[index]));
            }

            var tongue = new float[AdvancedVisemeVisibleTongueResidual.TongueLatentCount(kind)];
            for (var target = 0; target < tongue.Length; target++)
            {
                var tongueScale = AdvancedVisemeVisibleTongueResidual.TongueLatentSafeBound(kind, target);
                for (var viseme = 0; viseme < visemes.Length; viseme++)
                {
                    var conditionalScale =
                        AdvancedVisemeVisibleTongueResidual.ConditionalTongueLatentSafeBound(
                            kind, viseme, target);
                    var conditional = AdvancedVisemeVisibleTongueResidual.VisemeBias(
                                          kind, viseme, target) / conditionalScale;
                    for (var index = 0; index < latent.Length; index++)
                        conditional += latent[index] *
                                       AdvancedVisemeVisibleTongueResidual.VisibleLatentSafeBound(kind, index) *
                                       AdvancedVisemeVisibleTongueResidual.VisemeMix(
                                           kind, viseme, index, target) / conditionalScale;
                    maximumStageMagnitude = Mathf.Max(maximumStageMagnitude, Mathf.Abs(conditional));
                    var weighted = visemes[viseme] * conditional;
                    maximumStageMagnitude = Mathf.Max(maximumStageMagnitude, Mathf.Abs(weighted));
                    tongue[target] += weighted * conditionalScale / tongueScale;
                }
                maximumStageMagnitude = Mathf.Max(maximumStageMagnitude, Mathf.Abs(tongue[target]));
            }

            var outputScale = AdvancedVisemeVisibleTongueResidual.ConservativeOutputBound(kind, output);
            var normalized = 0f;
            for (var target = 0; target < tongue.Length; target++)
                normalized += tongue[target] *
                              AdvancedVisemeVisibleTongueResidual.TongueLatentSafeBound(kind, target) *
                              AdvancedVisemeVisibleTongueResidual.OutputProjection(kind, target, output) /
                              outputScale;
            maximumStageMagnitude = Mathf.Max(maximumStageMagnitude, Mathf.Abs(normalized));

            var reliability = 0f;
            for (var viseme = 0; viseme < visemes.Length; viseme++)
                reliability += visemes[viseme] *
                               AdvancedVisemeVisibleTongueResidual.Reliability(kind, viseme);
            normalized *= reliability;
            maximumStageMagnitude = Mathf.Max(maximumStageMagnitude, Mathf.Abs(normalized));
            return normalized;
        }

        private static float EvaluatePiecewise(
            (float input, float output)[] points,
            float input)
        {
            if (input <= points[0].input) return points[0].output;
            for (var index = 1; index < points.Length; index++)
            {
                if (input > points[index].input) continue;
                return Mathf.Lerp(
                    points[index - 1].output,
                    points[index].output,
                    Mathf.InverseLerp(points[index - 1].input, points[index].input, input));
            }
            return points[points.Length - 1].output;
        }
    }
}
