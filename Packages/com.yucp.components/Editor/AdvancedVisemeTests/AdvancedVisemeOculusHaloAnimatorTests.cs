using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YUCP.Components.Editor.Tests
{
    public sealed class AdvancedVisemeOculusHaloAnimatorTests
    {
        private const float Tolerance = 2e-6f;
        private const float CurveTolerance = 5e-5f;

        [Test]
        public void TestDisableSeamRestoresExactOneHotDecoderAndProjection()
        {
            var previous = AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests;
            try
            {
                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = true;
                Assert.That(AdvancedVisemeAnimatorBuilder.ShouldUseOculusHalo(false),
                    Is.False);

                for (var winner = 0;
                     winner < VisemeReconstructionProfile.VisemeCount;
                     winner++)
                for (var output = 0;
                     output < VisemeReconstructionProfile.VisemeCount;
                     output++)
                    Assert.That(AdvancedVisemeAnimatorBuilder.OculusHaloDecoderWeight(
                            false, winner, output),
                        Is.EqualTo(winner == output ? 1f : 0f));

                var coefficients = Enumerable.Range(
                        0, VisemeReconstructionProfile.VisemeCount)
                    .Select(index => (index - 7f) / 8f)
                    .ToArray();
                Assert.That(AdvancedVisemeAnimatorBuilder
                        .CommuteOculusHaloProjection(false, coefficients),
                    Is.EqualTo(coefficients).AsCollection);
            }
            finally
            {
                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = previous;
            }
        }

        [Test]
        public void SpeechOnlyDecoderRowsUseGeneratedStaticHaloAndProjection()
        {
            var previous = AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests;
            try
            {
                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = false;
                Assert.That(AdvancedVisemeAnimatorBuilder.ShouldUseOculusHalo(false),
                    Is.True);

                var coefficients = Enumerable.Range(
                        0, VisemeReconstructionProfile.VisemeCount)
                    .Select(index => Mathf.Sin(0.37f * index) * 0.7f)
                    .ToArray();
                var commuted = AdvancedVisemeAnimatorBuilder
                    .CommuteOculusHaloProjection(true, coefficients);

                for (var winner = 0;
                     winner < VisemeReconstructionProfile.VisemeCount;
                     winner++)
                {
                    var expectedProjection = 0f;
                    for (var output = 0;
                         output < VisemeReconstructionProfile.VisemeCount;
                         output++)
                    {
                        var expected =
                            AdvancedVisemeOculusHalo.Weight(winner, output);
                        Assert.That(AdvancedVisemeAnimatorBuilder
                                .OculusHaloDecoderWeight(true, winner, output),
                            Is.EqualTo(expected));
                        expectedProjection += expected * coefficients[output];
                    }

                    Assert.That(commuted[winner],
                        Is.EqualTo(expectedProjection).Within(Tolerance));
                }
            }
            finally
            {
                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = previous;
            }
        }

        [Test]
        public void TrackingCapableBuildRetainsStaticHaloFallback()
        {
            var previous = AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests;
            try
            {
                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = false;
                Assert.That(AdvancedVisemeAnimatorBuilder.ShouldUseOculusHalo(true),
                    Is.True,
                    "Installed tracking must not remove the no-tracker speech path.");

                var random = new System.Random(0x7A6C1);
                var coefficients = Enumerable.Range(
                        0, VisemeReconstructionProfile.VisemeCount)
                    .Select(_ => (float)(2.0 * random.NextDouble() - 1.0))
                    .ToArray();
                var identity = AdvancedVisemeAnimatorBuilder
                    .CommuteOculusHaloProjection(false, coefficients);
                for (var index = 0; index < identity.Length; index++)
                    Assert.That(identity[index],
                        Is.EqualTo(coefficients[index]).Within(Tolerance),
                        "Full tracking authority must recover the exact identity row.");
            }
            finally
            {
                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = previous;
            }
        }

        [Test]
        public void SpeechDecoderClipsUseExactLearnedPositiveTrajectory()
        {
            var previous = AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests;
            BuildFixture fixture = null;
            try
            {
                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = false;
                fixture = BuildSpeechOnlyController("StaticHalo");
                AssertTrajectoryDecoderCurves(fixture);
            }
            finally
            {
                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = previous;
                fixture?.Dispose();
            }
        }

        [Test]
        public void HaloAndIdentityBuildsHaveIdenticalTopologyAndBindingInventory()
        {
            var previous = AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests;
            BuildFixture halo = null;
            BuildFixture identity = null;
            try
            {
                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = false;
                halo = BuildSpeechOnlyController("Halo");
                var haloInventory = Inventory.Capture(halo);

                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = true;
                identity = BuildSpeechOnlyController("Identity");
                var identityInventory = Inventory.Capture(identity);

                Assert.That(haloInventory.parameters,
                    Is.EqualTo(identityInventory.parameters).AsCollection,
                    "Static target substitution must not add runtime parameters.");
                Assert.That(haloInventory.layers,
                    Is.EqualTo(identityInventory.layers).AsCollection,
                    "Static target substitution must not add states or transitions.");
                Assert.That(haloInventory.blendTrees,
                    Is.EqualTo(identityInventory.blendTrees).AsCollection,
                    "Static target substitution must not reshape the graph.");
                Assert.That(haloInventory.clipBindings,
                    Is.EqualTo(identityInventory.clipBindings).AsCollection,
                    "Halo rows must reuse the existing decoder bindings.");
            }
            finally
            {
                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = previous;
                identity?.Dispose();
                halo?.Dispose();
            }
        }

        [Test]
        public void TrackingBlendCrossfadesLearnedTrajectoryAndExactIdentityEndpoint()
        {
            var previous = AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests;
            BuildFixture fixture = null;
            try
            {
                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = false;
                fixture = new BuildFixture(
                    "TrackingEndpoints", beta: true, tracking: true);
                fixture.Build();

                var assets = AssetDatabase.LoadAllAssetsAtPath(fixture.controllerPath);
                var trees = assets.OfType<BlendTree>().ToArray();
                var clips = assets.OfType<AnimationClip>().ToArray();
                var trackingBlend = fixture.result.trackingBlendParameter;
                Assert.That(trackingBlend, Is.Not.Null.And.Not.Empty);
                Assert.That(fixture.result.controller.parameters.Count(parameter =>
                        parameter.name == trackingBlend),
                    Is.EqualTo(1),
                    "The decoder must reuse the tracking-confidence parameter.");

                for (var winner = 0;
                     winner < VisemeReconstructionProfile.VisemeCount;
                     winner++)
                {
                    var viseme = VisemeReconstructionProfile.VisemeNames[winner];
                    var tree = trees.Single(candidate => candidate.name ==
                        "Decode " + viseme + " by tracking authority");
                    Assert.That(tree.blendType, Is.EqualTo(BlendTreeType.Simple1D));
                    Assert.That(tree.blendParameter, Is.EqualTo(trackingBlend));
                    if (winner == 0)
                    {
                        // Halo and identity are identical for silence, so the
                        // graph optimizer is allowed to collapse the duplicate.
                        Assert.That(tree.children.Length, Is.EqualTo(1));
                        Assert.That(tree.children[0].motion.name,
                            Is.EqualTo("Decode " + viseme + " Identity"));
                    }
                    else
                    {
                        Assert.That(tree.children.Length, Is.EqualTo(2));
                        Assert.That(tree.children[0].threshold, Is.EqualTo(0f));
                        Assert.That(tree.children[0].motion.name,
                            Is.EqualTo("Decode " + viseme + " Halo"));
                        Assert.That(tree.children[1].threshold, Is.EqualTo(1f));
                        Assert.That(tree.children[1].motion.name,
                            Is.EqualTo("Decode " + viseme + " Identity"));
                    }

                    AssertHardRetention(clips, fixture, winner);
                }

                AssertTrajectoryDecoderCurves(fixture, " Halo", 1, folded: false);
                AssertStaticDecoderCurves(fixture, false, " Identity");
            }
            finally
            {
                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = previous;
                fixture?.Dispose();
            }
        }

        [Test]
        public void BetaContextRemainsTiedToTheHardWinnerAndStaticInTime()
        {
            var previous = AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests;
            BuildFixture fixture = null;
            try
            {
                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = false;
                fixture = new BuildFixture("BetaHardContext", beta: true);
                fixture.Build();

                var clips = AssetDatabase.LoadAllAssetsAtPath(fixture.controllerPath)
                    .OfType<AnimationClip>()
                    .ToArray();
                for (var winner = 0;
                     winner < VisemeReconstructionProfile.VisemeCount;
                     winner++)
                    AssertHardRetention(clips, fixture, winner);
            }
            finally
            {
                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = previous;
                fixture?.Dispose();
            }
        }

        [Test]
        public void DecoderSeparatesImmediateSemanticsFromInterruptibleTargetMotion()
        {
            var previous = AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests;
            BuildFixture fixture = null;
            try
            {
                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = false;
                fixture = new BuildFixture("SplitDecoder", beta: true);
                fixture.Build();

                var controller = fixture.result.controller;
                var semantics = controller.layers.Single(layer =>
                    layer.name == "YUCP AVR Viseme Semantics");
                var target = controller.layers.Single(layer =>
                    layer.name == "YUCP AVR Viseme Decoder");
                var semanticStates = semantics.stateMachine.states
                    .Select(child => child.state).ToArray();
                var targetStates = target.stateMachine.states
                    .Select(child => child.state).ToArray();
                Assert.That(semanticStates, Has.Length.EqualTo(
                    VisemeReconstructionProfile.VisemeCount));
                Assert.That(targetStates, Has.Length.EqualTo(
                    VisemeReconstructionProfile.VisemeCount));

                Assert.That(semantics.stateMachine.anyStateTransitions,
                    Has.Length.EqualTo(VisemeReconstructionProfile.VisemeCount));
                Assert.That(semanticStates.SelectMany(state => state.transitions),
                    Is.Empty,
                    "The categorical index must switch without a cross-fade.");
                foreach (var transition in semantics.stateMachine.anyStateTransitions)
                {
                    Assert.That(transition.duration, Is.Zero);
                    Assert.That(transition.conditions, Has.Length.EqualTo(1));
                    Assert.That(transition.conditions[0].parameter,
                        Is.EqualTo("Viseme"));
                    Assert.That(transition.conditions[0].mode,
                        Is.EqualTo(AnimatorConditionMode.Equals));
                }

                Assert.That(target.stateMachine.anyStateTransitions, Is.Empty,
                    "AnyState can restart its own destination during a blend.");
                foreach (var state in targetStates)
                {
                    Assert.That(state.transitions,
                        Has.Length.EqualTo(
                            VisemeReconstructionProfile.VisemeCount - 1));
                    foreach (var transition in state.transitions)
                    {
                        Assert.That(transition.hasExitTime, Is.False);
                        Assert.That(transition.hasFixedDuration, Is.True);
                        Assert.That(transition.duration,
                            Is.EqualTo(
                                AdvancedVisemeAnimatorBuilder
                                    .VisemeTargetCrossfadeSeconds));
                        Assert.That(transition.interruptionSource,
                            Is.EqualTo(TransitionInterruptionSource.Destination));
                        Assert.That(transition.orderedInterruption, Is.False);
                        Assert.That(transition.conditions, Has.Length.EqualTo(1));
                        Assert.That(transition.conditions[0].parameter,
                            Is.EqualTo("Viseme"));
                        Assert.That(transition.conditions[0].mode,
                            Is.EqualTo(AnimatorConditionMode.Equals));
                    }
                }

                var internalPrefix = fixture.component.NormalizedPrefix + "/_Internal";
                var indexName = internalPrefix + "/Viseme/Index";
                foreach (var state in semanticStates)
                {
                    var bindings = AnimationUtility.GetCurveBindings(
                        (AnimationClip)state.motion);
                    Assert.That(bindings.Any(binding =>
                        binding.propertyName == indexName), Is.True);
                    Assert.That(bindings.Any(binding =>
                        binding.propertyName.Contains("/Viseme/") &&
                        binding.propertyName.EndsWith("/Raw",
                            StringComparison.Ordinal)), Is.False);
                }
                foreach (var state in targetStates)
                {
                    var bindings = AnimationUtility.GetCurveBindings(
                        (AnimationClip)state.motion);
                    Assert.That(bindings.Any(binding =>
                        binding.propertyName == indexName), Is.False);
                    Assert.That(bindings.Any(binding =>
                        binding.propertyName.Contains(
                            "/BetaCoarticulation/RetentionTarget/")), Is.False);
                    Assert.That(bindings.Any(binding =>
                        binding.propertyName.Contains("/Viseme/") &&
                        binding.propertyName.EndsWith("/Raw",
                            StringComparison.Ordinal)), Is.True);
                }
            }
            finally
            {
                AdvancedVisemeAnimatorBuilder.DisableOculusHaloForTests = previous;
                fixture?.Dispose();
            }
        }

        private static void AssertHardRetention(
            IReadOnlyCollection<AnimationClip> clips,
            BuildFixture fixture,
            int winner)
        {
            var clip = clips.Single(candidate => candidate.name ==
                "Decode " + VisemeReconstructionProfile.VisemeNames[winner] +
                " Semantics");
            var internalPrefix = fixture.component.NormalizedPrefix + "/_Internal";
            for (var groupIndex = 0;
                 groupIndex < AdvancedVisemeTransitionRetention.GroupCount;
                 groupIndex++)
            for (var current = 0;
                 current < VisemeReconstructionProfile.VisemeCount;
                 current++)
            {
                var group = (AdvancedVisemeArticulatorGroup)groupIndex;
                var binding = EditorCurveBinding.FloatCurve(
                    string.Empty,
                    typeof(Animator),
                    internalPrefix + "/BetaCoarticulation/RetentionTarget/" +
                    group + "/" + current);
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                Assert.That(curve, Is.Not.Null,
                    $"Semantic decoder row {winner} does not publish " +
                    $"{binding.propertyName}.");
                var expected = AdvancedVisemeCoarticulationModel.Retention(
                    group, winner, current);
                AssertConstantCurve(curve, expected,
                    $"retention {group}, {winner} -> {current}");
            }
        }

        private static void AssertTrajectoryDecoderCurves(
            BuildFixture fixture,
            string suffix = "",
            int firstWinner = 0,
            bool folded = true)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(fixture.controllerPath)
                .OfType<AnimationClip>()
                .ToArray();
            var internalPrefix = fixture.component.NormalizedPrefix + "/_Internal";
            var duration = AdvancedVisemeOculusDynamics.TrajectoryDurationSeconds;
            var core = AdvancedVisemeOculusDynamics.TrajectoryCoreDurationSeconds;
            for (var winner = firstWinner;
                 winner < VisemeReconstructionProfile.VisemeCount;
                 winner++)
            {
                var clip = clips.Single(candidate => candidate.name ==
                    "Decode " + VisemeReconstructionProfile.VisemeNames[winner] + suffix);
                var dynamic = AdvancedVisemeOculusDynamics.HasDynamicTrajectory(winner);
                Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime,
                    Is.False);
                Assert.That(clip.length,
                    Is.EqualTo(dynamic ? duration : 0f).Within(CurveTolerance),
                    $"Decoder row {winner}{suffix} has the wrong trajectory duration.");

                for (var output = 0;
                     output < VisemeReconstructionProfile.VisemeCount;
                     output++)
                {
                    var rawParameter =
                        internalPrefix + "/Viseme/" + output + "/Raw";
                    var trajectoryParameter = ResolveInternedParameter(
                        fixture, rawParameter);
                    var binding = EditorCurveBinding.FloatCurve(
                        string.Empty,
                        typeof(Animator),
                        trajectoryParameter);
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    // The decoder bakes the learned trajectory with the
                    // retention-pull remainder folded in and retracted onto
                    // the simplex. The shared builder helper is the single
                    // source of truth so this contract cannot drift.
                    var controls = Enumerable.Range(
                            0, AdvancedVisemeOculusDynamics.ControlPointCount)
                        .Select(control => folded
                            ? AdvancedVisemeAnimatorBuilder
                                .RetentionPullFoldedDecoderWeight(
                                    winner, control, output)
                            : AdvancedVisemeOculusDynamics.Weight(
                                winner, control, output))
                        .ToArray();
                    if (curve == null)
                    {
                        Assert.That(controls,
                            Is.All.EqualTo(0f).Within(CurveTolerance),
                            $"Decoder row {winner}, output {output} omitted a " +
                            $"nonzero trajectory curve (resolved {rawParameter} " +
                            $"to {trajectoryParameter}).");
                        continue;
                    }

                    if (!dynamic)
                    {
                        AssertStaticCurve(curve, controls[0],
                            $"decoder row {winner}, output {output}");
                        continue;
                    }

                    Assert.That(curve.keys.Length, Is.EqualTo(3));
                    Assert.That(curve.keys[0].time, Is.Zero.Within(CurveTolerance));
                    Assert.That(curve.keys[1].time,
                        Is.EqualTo(core).Within(CurveTolerance));
                    Assert.That(curve.keys[2].time,
                        Is.EqualTo(duration).Within(CurveTolerance));
                    for (var sample = 0; sample <= 16; sample++)
                    {
                        var time = sample / 16f * duration;
                        float expected;
                        if (time >= core)
                        {
                            expected = Mathf.Lerp(
                                controls[3], controls[4],
                                Mathf.InverseLerp(core, duration, time));
                        }
                        else
                        {
                            var t = Mathf.Clamp01(time / core);
                            var inverse = 1f - t;
                            expected = inverse * inverse * inverse * controls[0] +
                                       3f * inverse * inverse * t * controls[1] +
                                       3f * inverse * t * t * controls[2] +
                                       t * t * t * controls[3];
                        }
                        Assert.That(curve.Evaluate(time),
                            Is.EqualTo(expected).Within(CurveTolerance),
                            $"decoder row {winner}, output {output}, time={time:R}");
                    }
                }
            }
        }

        private static string ResolveInternedParameter(
            BuildFixture fixture,
            string parameter)
        {
            var mappings = fixture.result.optimizerReport?
                .internedParameterMappings;
            if (mappings == null || mappings.Count == 0) return parameter;

            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (mappings.TryGetValue(parameter, out var representative))
            {
                Assert.That(visited.Add(parameter), Is.True,
                    "Optimizer congruence mappings contain a cycle at " + parameter + ".");
                parameter = representative;
            }
            return parameter;
        }

        private static void AssertStaticDecoderCurves(
            BuildFixture fixture,
            bool useHalo,
            string suffix = "",
            int firstWinner = 0)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(fixture.controllerPath)
                .OfType<AnimationClip>()
                .ToArray();
            var internalPrefix = fixture.component.NormalizedPrefix + "/_Internal";
            for (var winner = firstWinner;
                 winner < VisemeReconstructionProfile.VisemeCount;
                 winner++)
            {
                var clip = clips.Single(candidate => candidate.name ==
                    "Decode " + VisemeReconstructionProfile.VisemeNames[winner] + suffix);
                Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime,
                    Is.False);
                Assert.That(clip.length, Is.Zero.Within(CurveTolerance),
                    $"Decoder row {winner}{suffix} contains a state-local clock.");

                for (var output = 0;
                     output < VisemeReconstructionProfile.VisemeCount;
                     output++)
                {
                    var rawParameter =
                        internalPrefix + "/Viseme/" + output + "/Raw";
                    var targetParameter = ResolveInternedParameter(
                        fixture, rawParameter);
                    var binding = EditorCurveBinding.FloatCurve(
                        string.Empty,
                        typeof(Animator),
                        targetParameter);
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    var expected = useHalo
                        ? AdvancedVisemeOculusHalo.Weight(winner, output)
                        : winner == output ? 1f : 0f;
                    if (curve == null)
                    {
                        Assert.That(expected, Is.Zero.Within(CurveTolerance),
                            $"Decoder row {winner}, output {output} omitted a " +
                            $"nonzero target curve (resolved {rawParameter} " +
                            $"to {targetParameter}).");
                        continue;
                    }
                    AssertStaticCurve(curve, expected,
                        $"decoder row {winner}, output {output}");
                }
            }
        }

        private static void AssertStaticCurve(
            AnimationCurve curve,
            float expected,
            string description)
        {
            Assert.That(curve.keys, Is.Not.Empty, description);
            foreach (var key in curve.keys)
            {
                Assert.That(key.time, Is.Zero.Within(CurveTolerance),
                    description + " contains a timed key.");
                Assert.That(key.value, Is.EqualTo(expected).Within(CurveTolerance),
                    description + " is not constant.");
            }
            Assert.That(curve.Evaluate(0f),
                Is.EqualTo(expected).Within(CurveTolerance), description);
            Assert.That(curve.Evaluate(1f),
                Is.EqualTo(expected).Within(CurveTolerance), description);
        }

        private static void AssertConstantCurve(
            AnimationCurve curve,
            float expected,
            string description)
        {
            Assert.That(curve.keys, Is.Not.Empty, description);
            foreach (var key in curve.keys)
            {
                Assert.That(key.value,
                    Is.EqualTo(expected).Within(CurveTolerance),
                    description + " is not constant.");
                Assert.That(key.inTangent == 0f || float.IsInfinity(key.inTangent),
                    Is.True, description + " has a non-flat incoming tangent.");
                Assert.That(key.outTangent == 0f || float.IsInfinity(key.outTangent),
                    Is.True, description + " has a non-flat outgoing tangent.");
            }
            Assert.That(curve.Evaluate(0f),
                Is.EqualTo(expected).Within(CurveTolerance), description);
            Assert.That(curve.Evaluate(
                    AdvancedVisemeOculusDynamics.TrajectoryDurationSeconds),
                Is.EqualTo(expected).Within(CurveTolerance), description);
        }

        private static BuildFixture BuildSpeechOnlyController(string label)
        {
            var fixture = new BuildFixture(label);
            fixture.Build();
            return fixture;
        }

        private sealed class BuildFixture : IDisposable
        {
            private readonly string folderName;
            internal readonly string folder;
            internal readonly string controllerPath;
            private readonly GameObject root;
            private readonly VisemeReconstructionProfile profile;
            private readonly bool tracking;
            internal readonly AdvancedVisemeReconstructorData component;
            internal AdvancedVisemeAnimatorBuilder.Result result;

            internal BuildFixture(
                string label,
                bool beta = false,
                bool tracking = false)
            {
                folderName = "__YUCP_AVR_Halo_" + label + "_" +
                             Guid.NewGuid().ToString("N");
                folder = "Assets/" + folderName;
                controllerPath = folder + "/AdvancedViseme.controller";
                AssetDatabase.CreateFolder("Assets", folderName);
                root = new GameObject("Oculus Halo " + label + " Test");
                profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
                this.tracking = tracking;
                component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.mouthOwnership = AdvancedVisemeMouthOwnership.OutputsOnly;
                component.reconstructionMode = beta
                    ? AdvancedVisemeReconstructionMode.BetaCoarticulation
                    : AdvancedVisemeReconstructionMode.Normal;
                component.trackingInputs = tracking
                    ? AdvancedVisemeTrackingInputs.Balanced8
                    : AdvancedVisemeTrackingInputs.Disabled;
                component.trackingEncoding = AdvancedVisemeTrackingEncoding.FullFloat;
                component.createTuningMenu = false;
            }

            internal void Build()
            {
                result = AdvancedVisemeAnimatorBuilder.Build(
                    new AdvancedVisemeAnimatorBuilder.Request
                    {
                        controllerPath = controllerPath,
                        parametersPath = folder + "/Parameters.asset",
                        component = component,
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
                        trackingActiveDefault = 0f,
                        trackingParameterNames = new Dictionary<
                            AdvancedVisemeArticulator, string>(),
                        auxiliaryTrackingParameterNames =
                            new Dictionary<string, string>(),
                        sourceVisemeBlendShapes = new string[
                            VisemeReconstructionProfile.VisemeCount],
                        calibrationBasis = Array.Empty<
                            MeshUtils.AdvancedVisemeMeshCalibrator.BasisInput>(),
                        resolvedBlendShapes = new Dictionary<
                            AdvancedVisemeArticulator, string>(),
                        externalPoses = new Dictionary<
                            AdvancedVisemeArticulator, AdvancedVisemeExternalPose>(),
                        trackingEnabled = tracking,
                        existingExpressionParameters = new HashSet<string>()
                    });
            }

            public void Dispose()
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private sealed class Inventory
        {
            internal string[] parameters;
            internal string[] layers;
            internal string[] blendTrees;
            internal string[] clipBindings;

            internal static Inventory Capture(BuildFixture fixture)
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath(fixture.controllerPath);
                return new Inventory
                {
                    parameters = fixture.result.controller.parameters
                        .Select(parameter => parameter.name + "|" +
                                             parameter.type + "|" +
                                             parameter.defaultFloat.ToString("R"))
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray(),
                    layers = fixture.result.controller.layers
                        .SelectMany(LayerInventory)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray(),
                    blendTrees = assets.OfType<BlendTree>()
                        .Select(TreeInventory)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray(),
                    clipBindings = assets.OfType<AnimationClip>()
                        .Select(ClipBindingInventory)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray()
                };
            }

            private static IEnumerable<string> LayerInventory(
                AnimatorControllerLayer layer)
            {
                yield return "layer|" + layer.name + "|" + layer.blendingMode;
                foreach (var state in layer.stateMachine.states)
                {
                    yield return "state|" + layer.name + "|" + state.state.name +
                                 "|" + (state.state.motion?.name ?? "null") + "|" +
                                 state.state.writeDefaultValues;
                    foreach (var transition in state.state.transitions)
                        yield return TransitionInventory(
                            layer.name + "|" + state.state.name, transition);
                }
                foreach (var transition in layer.stateMachine.anyStateTransitions)
                    yield return TransitionInventory(layer.name + "|Any", transition);
            }

            private static string TransitionInventory(
                string source,
                AnimatorStateTransition transition)
            {
                var conditions = transition.conditions
                    .Select(condition => condition.parameter + ":" + condition.mode +
                                         ":" + condition.threshold.ToString("R"))
                    .OrderBy(value => value, StringComparer.Ordinal);
                return "transition|" + source + "|" +
                       (transition.destinationState?.name ?? "null") + "|" +
                       string.Join(",", conditions);
            }

            private static string TreeInventory(BlendTree tree)
            {
                var children = tree.children.Select(child =>
                        (child.motion?.name ?? "null") + "@" +
                        (child.directBlendParameter ?? string.Empty) + "@" +
                        child.threshold.ToString("R"))
                    .OrderBy(value => value, StringComparer.Ordinal);
                return tree.name + "|" + tree.blendType + "|" +
                       tree.blendParameter + "|" + tree.blendParameterY + "|" +
                       string.Join(",", children);
            }

            private static string ClipBindingInventory(AnimationClip clip)
            {
                var bindings = AnimationUtility.GetCurveBindings(clip)
                    .Select(binding => binding.path + "|" +
                                       binding.type.FullName + "|" +
                                       binding.propertyName)
                    .OrderBy(value => value, StringComparer.Ordinal);
                return clip.name + "|" + string.Join(",", bindings);
            }
        }
    }
}
