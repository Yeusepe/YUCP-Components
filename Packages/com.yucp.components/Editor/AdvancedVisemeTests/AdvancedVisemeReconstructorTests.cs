#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor.Tests
{
    public sealed class AdvancedVisemeReconstructorTests
    {
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
        public void TrackingBudgetsMatchThePublishedPresets()
        {
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(AdvancedVisemeTrackingInputs.Disabled), Is.Zero);
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(AdvancedVisemeTrackingInputs.ReuseExisting), Is.Zero);
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(AdvancedVisemeTrackingInputs.Auto), Is.EqualTo(25));
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(AdvancedVisemeTrackingInputs.Balanced8), Is.EqualTo(25));
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(AdvancedVisemeTrackingInputs.Quality12), Is.EqualTo(39));
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(
                AdvancedVisemeTrackingInputs.Balanced8, AdvancedVisemeTrackingEncoding.Uniform4BitBinary), Is.EqualTo(35));
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(
                AdvancedVisemeTrackingInputs.Quality12, AdvancedVisemeTrackingEncoding.Uniform4BitBinary), Is.EqualTo(55));
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(
                AdvancedVisemeTrackingInputs.Balanced8, AdvancedVisemeTrackingEncoding.FullFloat), Is.EqualTo(66));
            Assert.That(AdvancedVisemeMath.TrackingParameterBits(
                AdvancedVisemeTrackingInputs.Quality12, AdvancedVisemeTrackingEncoding.FullFloat), Is.EqualTo(98));
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
            var editor = UnityEditor.Editor.CreateEditor(component);
            try
            {
                var root = editor.CreateInspectorGUI();
                Assert.That(editor, Is.TypeOf<AdvancedVisemeReconstructorDataEditor>());
                Assert.That(root.ClassListContains("yucp-root"), Is.True);
                Assert.That(UQueryExtensions.Query<VisualElement>(root, className: "yucp-card").ToList().Count,
                    Is.EqualTo(3));
                Assert.That(UQueryExtensions.Q<Button>(root, "profile-action"), Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Foldout>(root, "advanced-settings"), Is.Not.Null);
                Assert.That(UQueryExtensions.Q<Label>(root, "tracking-budget").text, Does.Contain("25"));
                Assert.That(UQueryExtensions.Q<VisualElement>(root, "reuse-prefix-container").style.display.value,
                    Is.EqualTo(DisplayStyle.Flex));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
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
