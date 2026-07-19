using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor.Tests
{
    /// <summary>
    /// Exercises the controller at its real DriveLowerFace endpoint. These tests
    /// deliberately read both public Animator parameters and the weights applied
    /// to a SkinnedMeshRenderer; inspecting decoder clips alone would miss an
    /// accidental observer, voice, or tracking handoff on the physical path.
    /// </summary>
    public sealed class AdvancedVisemeDirectRenderTests
    {
        private static readonly int[] SampleRates = { 15, 30, 60, 90, 144 };
        private const float SimplexTolerance = 2e-4f;

        [TestCase(10, 11, 10, TestName = "DriveLowerFace_A_B_A_InterruptionsRemainContinuous")]
        [TestCase(10, 11, 12, TestName = "DriveLowerFace_A_B_C_InterruptionsRemainContinuous")]
        public void DriveLowerFaceRapidInterruptionsRemainContinuousAtReviewedRates(
            int first,
            int second,
            int third)
        {
            using (var fixture = new Fixture(tracking: false))
            {
                var writerLayers = fixture.PublicVisemeWriterLayers();
                Assert.That(writerLayers, Has.Length.EqualTo(1),
                    "The tracking-disabled public simplex must have one Animator " +
                    "writer layer. Parallel observer and direct writers add their " +
                    "simplexes instead of selecting an endpoint. Writers: " +
                    string.Join(", ", writerLayers));
                foreach (var rate in SampleRates)
                {
                    using (var runtime = fixture.CreateRuntime())
                    {
                        runtime.SetVoice(1f);
                        runtime.Hold(first, 0.55f, rate);
                        var samples = new List<FrameSample>
                        {
                            runtime.Sample()
                        };

                        // Interrupt before the fitted 72 ms target cross-fade can
                        // complete. At 15 Hz, one frame is already 66.7 ms; at
                        // higher rates two frames retain a similarly early edge.
                        runtime.Step(second, rate);
                        samples.Add(runtime.Sample());
                        if (rate >= 30)
                        {
                            runtime.Step(second, rate);
                            samples.Add(runtime.Sample());
                        }
                        runtime.Step(third, rate);
                        samples.Add(runtime.Sample());

                        // Continue through the positive 224 ms fitted trajectory
                        // so the test observes interpolation, not only the state
                        // transition's first frame.
                        var tailFrames = Mathf.CeilToInt(
                            (AdvancedVisemeOculusDynamics.TrajectoryDurationSeconds +
                             AdvancedVisemeOculusDynamics.TargetCrossfadeSeconds) * rate) + 2;
                        for (var frame = 0; frame < tailFrames; frame++)
                        {
                            runtime.Step(third, rate);
                            samples.Add(runtime.Sample());
                        }

                        foreach (var sample in samples)
                        {
                            AssertSimplex(sample.publicVisemes, rate);
                            Assert.That(sample.renderedVisemes.All(IsFinite), Is.True,
                                $"The DriveLowerFace pose became non-finite at {rate} Hz.");
                            Assert.That(sample.renderedVisemes,
                                Is.All.GreaterThanOrEqualTo(-SimplexTolerance),
                                $"The positive render trajectory produced a negative " +
                                $"blendshape at {rate} Hz.");
                        }

                        var publicSteps = ConsecutiveL1(samples
                            .Select(sample => sample.publicVisemes).ToArray());
                        var renderedSteps = ConsecutiveL1(samples
                            .Select(sample => NormalizeRendered(sample.renderedVisemes))
                            .ToArray());
                        var limit = rate == 15 ? 1.45f : 0.95f;
                        Assert.That(publicSteps.Max(), Is.LessThan(limit),
                            $"The public simplex made a near-categorical step at {rate} Hz.");
                        Assert.That(renderedSteps.Max(), Is.LessThan(limit),
                            $"DriveLowerFace made a near-categorical step at {rate} Hz.");

                        // A hard switch has only one changing sample. The fitted
                        // direct path must keep moving after the interruption at
                        // every reviewed render rate, including 15 Hz.
                        Assert.That(renderedSteps.Count(step => step > 0.004f),
                            Is.GreaterThanOrEqualTo(2),
                            $"DriveLowerFace collapsed to a stair step at {rate} Hz.");
                        Assert.That(publicSteps.Count(step => step > 0.004f),
                            Is.GreaterThanOrEqualTo(2),
                            $"The public simplex collapsed to a stair step at {rate} Hz.");

                        // Both endpoints consume the same direct decoder epoch.
                        // AAP publication can lag physical curves by one Animator
                        // evaluation, so accept the closer of current/previous.
                        for (var frame = 1; frame < samples.Count; frame++)
                        {
                            var rendered = NormalizeRendered(
                                samples[frame].renderedVisemes);
                            var sameEpoch = L1(rendered,
                                samples[frame].publicVisemes);
                            var previousEpoch = L1(rendered,
                                samples[frame - 1].publicVisemes);
                            Assert.That(Mathf.Min(sameEpoch, previousEpoch),
                                Is.LessThan(0.035f),
                                $"Physical and public trajectories diverged by more " +
                                $"than one publication epoch at {rate} Hz, frame {frame}.");
                        }
                    }
                }
            }
        }

        [Test]
        public void TrackingCapableBuildUsesDirectTrajectoryWhenTrackerIsInactive()
        {
            using (var fixture = new Fixture(tracking: true))
            using (var runtime = fixture.CreateRuntime())
            {
                const int rate = 90;
                runtime.SetVoice(1f);
                runtime.SetTracking(0f, active: false);
                runtime.Hold(10, 0.55f, rate);
                Assert.That(runtime.Animator.GetFloat(
                        fixture.Result.trackingBlendParameter),
                    Is.LessThan(1f / 255f),
                    "An installed tracking bus must not select the tracked " +
                    "endpoint while LipTrackingActive is false.");

                var samples = new List<FrameSample> { runtime.Sample() };
                var geometry = new List<Vector3[]> { runtime.RenderedVertexDeltas() };
                runtime.Step(11, rate);
                samples.Add(runtime.Sample());
                geometry.Add(runtime.RenderedVertexDeltas());
                runtime.Step(11, rate);
                samples.Add(runtime.Sample());
                geometry.Add(runtime.RenderedVertexDeltas());
                runtime.Step(12, rate);
                samples.Add(runtime.Sample());
                geometry.Add(runtime.RenderedVertexDeltas());
                var tailFrames = Mathf.CeilToInt(
                    (AdvancedVisemeOculusDynamics.TrajectoryDurationSeconds +
                     AdvancedVisemeOculusDynamics.TargetCrossfadeSeconds) * rate) + 2;
                for (var frame = 0; frame < tailFrames; frame++)
                {
                    runtime.Step(12, rate);
                    samples.Add(runtime.Sample());
                    geometry.Add(runtime.RenderedVertexDeltas());
                }

                foreach (var sample in samples)
                    AssertSimplex(sample.publicVisemes, rate);
                var geometrySteps = ConsecutiveGeometryDistance(geometry);
                Assert.That(geometrySteps.Count(step => step > 1e-5f),
                    Is.GreaterThanOrEqualTo(2),
                    "A tracking-capable build fell back to a stair-step mouth " +
                    "when its tracker was inactive.");

                for (var frame = 1; frame < samples.Count; frame++)
                {
                    var expectedNow = fixture.ExpectedAuthoredVertexDeltas(
                        samples[frame].publicVisemes);
                    var expectedPrevious = fixture.ExpectedAuthoredVertexDeltas(
                        samples[frame - 1].publicVisemes);
                    var error = Mathf.Min(
                        GeometryDistance(geometry[frame], expectedNow),
                        GeometryDistance(geometry[frame], expectedPrevious));
                    Assert.That(error, Is.LessThan(8e-4f),
                        $"The inactive-tracker physical pose left the exact " +
                        $"authored direct trajectory at frame {frame} (error {error:R}).");
                }
            }
        }

        [Test]
        public void TrackerAuthoritativeEndpointRemainsResponsiveAndSourceMeshIsUntouched()
        {
            using (var fixture = new Fixture(tracking: true))
            using (var runtime = fixture.CreateRuntime())
            {
                var sourceSignature = fixture.SourceMeshSignature;
                runtime.SetVoice(0f);
                runtime.SetTracking(1f);

                // Allow the documented acquisition pole to reach its endpoint.
                runtime.Hold(10, 0.35f, 144);
                var jawBeforeVisemeChange = runtime.JawOpenWeight;
                Assert.That(jawBeforeVisemeChange, Is.GreaterThan(97f),
                    "The tracker-authoritative endpoint no longer reaches the " +
                    "authored JawOpen pose.");

                runtime.Step(11, 144);
                runtime.Step(12, 144);
                var jawAfterVisemeChanges = runtime.JawOpenWeight;
                Assert.That(jawAfterVisemeChanges,
                    Is.EqualTo(jawBeforeVisemeChange).Within(1.5f),
                    "The new speech trajectory leaked into the tracked endpoint.");
                Assert.That(runtime.Animator.GetFloat(
                        fixture.Result.trackingBlendParameter),
                    Is.GreaterThan(0.99f));

                Assert.That(fixture.CaptureSourceMeshSignature(),
                    Is.EqualTo(sourceSignature),
                    "Building or evaluating DriveLowerFace modified the source mesh.");
                Assert.That(runtime.Renderer.sharedMesh, Is.SameAs(fixture.RenderMesh),
                    "The DriveLowerFace endpoint stopped using its calibrated build mesh.");
                Assert.That(fixture.RenderMesh, Is.Not.SameAs(fixture.SourceMesh),
                    "The calibrated test must exercise a build clone, not animate " +
                    "the persistent source asset.");
            }
        }

        private static void AssertSimplex(float[] values, int rate)
        {
            Assert.That(values.All(IsFinite), Is.True,
                $"The public simplex became non-finite at {rate} Hz.");
            Assert.That(values, Is.All.GreaterThanOrEqualTo(-SimplexTolerance),
                $"The public simplex became negative at {rate} Hz.");
            Assert.That(values, Is.All.LessThanOrEqualTo(1f + SimplexTolerance),
                $"The public simplex exceeded one at {rate} Hz.");
            Assert.That(values.Sum(), Is.EqualTo(1f).Within(SimplexTolerance),
                $"The public simplex lost normalization at {rate} Hz: " +
                string.Join(", ", values.Select(value => value.ToString("R"))));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float[] NormalizeRendered(float[] weights)
        {
            var normalized = weights.Select(weight => Mathf.Max(0f, weight)).ToArray();
            var sum = normalized.Sum();
            if (sum <= 1e-6f) return normalized;
            for (var i = 0; i < normalized.Length; i++) normalized[i] /= sum;
            return normalized;
        }

        private static float[] ConsecutiveL1(IReadOnlyList<float[]> values)
        {
            var result = new float[Mathf.Max(0, values.Count - 1)];
            for (var index = 1; index < values.Count; index++)
                result[index - 1] = L1(values[index], values[index - 1]);
            return result;
        }

        private static float[] ConsecutiveGeometryDistance(
            IReadOnlyList<Vector3[]> values)
        {
            var result = new float[Mathf.Max(0, values.Count - 1)];
            for (var index = 1; index < values.Count; index++)
                result[index - 1] = GeometryDistance(
                    values[index], values[index - 1]);
            return result;
        }

        private static float GeometryDistance(
            IReadOnlyList<Vector3> left,
            IReadOnlyList<Vector3> right)
        {
            var sum = 0f;
            for (var index = 0; index < left.Count; index++)
                sum += Vector3.Distance(left[index], right[index]);
            return sum;
        }

        private static float L1(IReadOnlyList<float> left, IReadOnlyList<float> right)
        {
            var sum = 0f;
            for (var index = 0; index < left.Count; index++)
                sum += Mathf.Abs(left[index] - right[index]);
            return sum;
        }

        private readonly struct FrameSample
        {
            internal readonly float[] publicVisemes;
            internal readonly float[] renderedVisemes;

            internal FrameSample(float[] publicVisemes, float[] renderedVisemes)
            {
                this.publicVisemes = publicVisemes;
                this.renderedVisemes = renderedVisemes;
            }
        }

        private sealed class RuntimeFixture : IDisposable
        {
            private readonly Fixture fixture;
            private readonly GameObject root;
            internal readonly Animator Animator;
            internal readonly SkinnedMeshRenderer Renderer;

            internal RuntimeFixture(Fixture fixture)
            {
                this.fixture = fixture;
                root = new GameObject("Advanced Viseme Direct Render Runtime");
                var face = new GameObject("Face");
                face.transform.SetParent(root.transform, false);
                Renderer = face.AddComponent<SkinnedMeshRenderer>();
                Renderer.sharedMesh = fixture.RenderMesh;

                Animator = root.AddComponent<Animator>();
                Animator.runtimeAnimatorController = fixture.Result.controller;
                Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Animator.Rebind();
                Animator.Update(0f);
                Animator.SetFloat("IsLocal", 1f);
                if (!string.IsNullOrEmpty(fixture.Result.manualTrackingParameter))
                    Animator.SetFloat(fixture.Result.manualTrackingParameter, 1f);
            }

            internal float JawOpenWeight => Renderer.GetBlendShapeWeight(
                fixture.RenderMesh.GetBlendShapeIndex("JawOpen"));

            internal void SetVoice(float voice)
            {
                Animator.SetFloat("Voice", voice);
            }

            internal void SetTracking(float value, bool active = true)
            {
                if (!fixture.Tracking) return;
                Animator.SetFloat("YUCP/TestTracking/LipTrackingActive",
                    active ? 1f : 0f);
                Animator.SetFloat("YUCP/TestTracking/v2/JawOpen", value);
            }

            internal void Hold(int viseme, float seconds, int rate)
            {
                var frameCount = Mathf.CeilToInt(seconds * rate);
                for (var frame = 0; frame < frameCount; frame++) Step(viseme, rate);
            }

            internal void Step(int viseme, int rate)
            {
                Animator.SetInteger("Viseme", viseme);
                var deltaTime = 1f / rate;
                Animator.Update(deltaTime);
            }

            internal FrameSample Sample()
            {
                var prefix = fixture.Component.NormalizedPrefix;
                var publicWeights = Enumerable.Range(
                        0, VisemeReconstructionProfile.VisemeCount)
                    .Select(index => Animator.GetFloat(
                        AdvancedVisemeParameterContract.Viseme(prefix, index)))
                    .ToArray();
                var renderedWeights = fixture.VisemeBlendShapeIndices
                    .Select(Renderer.GetBlendShapeWeight)
                    .Select(weight => weight / 100f)
                    .ToArray();
                return new FrameSample(publicWeights, renderedWeights);
            }

            internal Vector3[] RenderedVertexDeltas()
            {
                var mesh = fixture.RenderMesh;
                var output = new Vector3[mesh.vertexCount];
                var vertices = new Vector3[mesh.vertexCount];
                var normals = new Vector3[mesh.vertexCount];
                var tangents = new Vector3[mesh.vertexCount];
                for (var shape = 0; shape < mesh.blendShapeCount; shape++)
                {
                    var weight = Renderer.GetBlendShapeWeight(shape) / 100f;
                    if (Mathf.Abs(weight) <= 1e-8f ||
                        mesh.GetBlendShapeFrameCount(shape) == 0) continue;
                    var frame = mesh.GetBlendShapeFrameCount(shape) - 1;
                    mesh.GetBlendShapeFrameVertices(
                        shape, frame, vertices, normals, tangents);
                    var frameWeight = mesh.GetBlendShapeFrameWeight(shape, frame);
                    var scale = Mathf.Abs(frameWeight) <= 1e-8f
                        ? 0f
                        : weight * 100f / frameWeight;
                    for (var vertex = 0; vertex < output.Length; vertex++)
                        output[vertex] += vertices[vertex] * scale;
                }
                return output;
            }

            public void Dispose()
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly string folder;
            private readonly GameObject authoringRoot;
            private readonly VisemeReconstructionProfile profile;

            internal readonly bool Tracking;
            internal readonly AdvancedVisemeReconstructorData Component;
            internal readonly Mesh SourceMesh;
            internal readonly Mesh RenderMesh;
            internal readonly int[] VisemeBlendShapeIndices;
            internal readonly AdvancedVisemeAnimatorBuilder.Result Result;
            internal readonly string SourceMeshSignature;
            private readonly AdvancedVisemeMeshCalibrator.Result calibration;

            internal Fixture(bool tracking)
            {
                Tracking = tracking;
                folder = "Assets/__YUCP_AVR_DirectRender_" +
                         Guid.NewGuid().ToString("N");
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                authoringRoot = new GameObject("Advanced Viseme Direct Render Authoring");
                profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
                SourceMesh = CreateFaceMesh();
                VisemeBlendShapeIndices = Enumerable.Range(
                        0, VisemeReconstructionProfile.VisemeCount)
                    .Select(index => SourceMesh.GetBlendShapeIndex(
                        "vrc.v_" + VisemeReconstructionProfile.VisemeNames[index]))
                    .ToArray();
                SourceMeshSignature = CaptureSourceMeshSignature();
                var calibrationBasis = tracking
                    ? new[]
                    {
                        new AdvancedVisemeMeshCalibrator.BasisInput(
                            AdvancedVisemeArticulator.JawOpen,
                            SourceMesh.GetBlendShapeIndex("JawOpen"))
                    }
                    : Array.Empty<AdvancedVisemeMeshCalibrator.BasisInput>();
                if (tracking)
                {
                    calibration = AdvancedVisemeMeshCalibrator.Build(
                        SourceMesh, VisemeBlendShapeIndices, calibrationBasis);
                    Assert.That(calibration.success, Is.True, calibration.error);
                }
                RenderMesh = calibration?.mesh ?? SourceMesh;

                Component = authoringRoot.AddComponent<AdvancedVisemeReconstructorData>();
                Component.mouthOwnership = AdvancedVisemeMouthOwnership.DriveLowerFace;
                Component.reconstructionMode = AdvancedVisemeReconstructionMode.Normal;
                Component.trackingInputs = tracking
                    ? AdvancedVisemeTrackingInputs.Balanced8
                    : AdvancedVisemeTrackingInputs.Disabled;
                Component.trackingEncoding = AdvancedVisemeTrackingEncoding.FullFloat;
                Component.fusionMode = AdvancedVisemeFusionMode.TrackerAuthoritative;
                Component.createFaceTrackingToggle = false;
                Component.createTuningMenu = false;

                Result = AdvancedVisemeAnimatorBuilder.Build(
                    new AdvancedVisemeAnimatorBuilder.Request
                    {
                        controllerPath = folder + "/AdvancedViseme.controller",
                        parametersPath = folder + "/Parameters.asset",
                        rendererPath = "Face",
                        component = Component,
                        profile = profile,
                        trackingPrefix = "YUCP/TestTracking",
                        effectiveTrackingInputs = tracking
                            ? AdvancedVisemeTrackingInputs.Balanced8
                            : AdvancedVisemeTrackingInputs.Disabled,
                        reuseExistingTracking = false,
                        trackingActiveParameter =
                            "YUCP/TestTracking/LipTrackingActive",
                        trackingActiveAnimatorType =
                            AnimatorControllerParameterType.Float,
                        trackingActiveDefault = tracking ? 1f : 0f,
                        trackingParameterNames = new Dictionary<
                            AdvancedVisemeArticulator, string>(),
                        auxiliaryTrackingParameterNames =
                            new Dictionary<string, string>(),
                        sourceVisemeBlendShapes = Enumerable.Range(
                                0, VisemeReconstructionProfile.VisemeCount)
                            .Select(index => "vrc.v_" +
                                VisemeReconstructionProfile.VisemeNames[index])
                            .ToArray(),
                        calibration = calibration,
                        calibrationBasis = calibrationBasis,
                        resolvedBlendShapes = tracking
                            ? new Dictionary<AdvancedVisemeArticulator, string>
                            {
                                [AdvancedVisemeArticulator.JawOpen] = "JawOpen"
                            }
                            : new Dictionary<AdvancedVisemeArticulator, string>(),
                        externalPoses = new Dictionary<
                            AdvancedVisemeArticulator, AdvancedVisemeExternalPose>(),
                        targetMesh = SourceMesh,
                        trackingEnabled = tracking,
                        existingExpressionParameters = new HashSet<string>()
                    });

                Assert.That(CaptureSourceMeshSignature(),
                    Is.EqualTo(SourceMeshSignature),
                    "The controller build modified its source mesh.");
            }

            internal RuntimeFixture CreateRuntime()
            {
                return new RuntimeFixture(this);
            }

            internal string[] PublicVisemeWriterLayers()
            {
                var publicNames = new HashSet<string>(Enumerable.Range(
                        0, VisemeReconstructionProfile.VisemeCount)
                    .Select(index => AdvancedVisemeParameterContract.Viseme(
                        Component.NormalizedPrefix, index)), StringComparer.Ordinal);
                return Result.controller.layers
                    .Where(layer => layer.stateMachine.states.Any(child =>
                        MotionWritesAny(child.state.motion, publicNames,
                            new HashSet<Motion>())))
                    .Select(layer => layer.name)
                    .ToArray();
            }

            internal Vector3[] ExpectedAuthoredVertexDeltas(
                IReadOnlyList<float> publicVisemes)
            {
                var output = new Vector3[SourceMesh.vertexCount];
                var vertices = new Vector3[SourceMesh.vertexCount];
                var normals = new Vector3[SourceMesh.vertexCount];
                var tangents = new Vector3[SourceMesh.vertexCount];
                for (var viseme = 0;
                     viseme < VisemeBlendShapeIndices.Length;
                     viseme++)
                {
                    var shape = VisemeBlendShapeIndices[viseme];
                    var frame = SourceMesh.GetBlendShapeFrameCount(shape) - 1;
                    SourceMesh.GetBlendShapeFrameVertices(
                        shape, frame, vertices, normals, tangents);
                    for (var vertex = 0; vertex < output.Length; vertex++)
                        output[vertex] += vertices[vertex] * publicVisemes[viseme];
                }
                return output;
            }

            private static bool MotionWritesAny(
                Motion motion,
                ISet<string> propertyNames,
                ISet<Motion> visited)
            {
                if (motion == null || !visited.Add(motion)) return false;
                if (motion is AnimationClip clip)
                    return AnimationUtility.GetCurveBindings(clip)
                        .Any(binding => propertyNames.Contains(binding.propertyName));
                if (motion is BlendTree tree)
                    return tree.children.Any(child =>
                        MotionWritesAny(child.motion, propertyNames, visited));
                return false;
            }

            internal string CaptureSourceMeshSignature()
            {
                var builder = new StringBuilder();
                builder.Append(SourceMesh.vertexCount).Append('|')
                    .Append(SourceMesh.blendShapeCount).Append('|');
                var vertices = new Vector3[SourceMesh.vertexCount];
                var normals = new Vector3[SourceMesh.vertexCount];
                var tangents = new Vector3[SourceMesh.vertexCount];
                for (var shape = 0; shape < SourceMesh.blendShapeCount; shape++)
                {
                    builder.Append(SourceMesh.GetBlendShapeName(shape)).Append(':')
                        .Append(SourceMesh.GetBlendShapeFrameCount(shape)).Append(';');
                    for (var frame = 0;
                         frame < SourceMesh.GetBlendShapeFrameCount(shape);
                         frame++)
                    {
                        builder.Append(SourceMesh.GetBlendShapeFrameWeight(shape, frame))
                            .Append('=');
                        SourceMesh.GetBlendShapeFrameVertices(
                            shape, frame, vertices, normals, tangents);
                        for (var vertex = 0; vertex < vertices.Length; vertex++)
                            builder.Append(vertices[vertex]).Append('/')
                                .Append(normals[vertex]).Append('/')
                                .Append(tangents[vertex]).Append(',');
                    }
                }
                return builder.ToString();
            }

            public void Dispose()
            {
                AssetDatabase.DeleteAsset(folder);
                if (calibration != null && calibration.mesh != null)
                    UnityEngine.Object.DestroyImmediate(calibration.mesh);
                if (SourceMesh != null) UnityEngine.Object.DestroyImmediate(SourceMesh);
                if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
                if (authoringRoot != null)
                    UnityEngine.Object.DestroyImmediate(authoringRoot);
            }

            private static Mesh CreateFaceMesh()
            {
                var mesh = new Mesh { name = "Advanced Viseme Direct Render Face" };
                mesh.vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up
                };
                mesh.normals = Enumerable.Repeat(Vector3.forward, 3).ToArray();
                mesh.triangles = new[] { 0, 1, 2 };
                var zero = new Vector3[3];
                mesh.AddBlendShapeFrame("JawOpen", 100f,
                    new[] { new Vector3(0f, -0.1f, 0f), zero[1], zero[2] },
                    zero, zero);
                for (var viseme = 0;
                     viseme < VisemeReconstructionProfile.VisemeCount;
                     viseme++)
                {
                    var delta = new Vector3[3];
                    delta[viseme % delta.Length] = new Vector3(
                        0.001f * (viseme + 1),
                        0.0007f * (viseme + 1),
                        0.0003f * (viseme + 1));
                    mesh.AddBlendShapeFrame(
                        "vrc.v_" + VisemeReconstructionProfile.VisemeNames[viseme],
                        100f, delta, zero, zero);
                }
                return mesh;
            }
        }
    }
}
