using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace YUCP.Components.Editor.Tests
{
    /// <summary>
    /// Isolated proof for the native-state expert proposed by the AVR performance
    /// investigation. Nothing in this file is called by the production builder.
    /// The experiment deliberately animates parameters only; it never touches a
    /// renderer or mesh.
    /// </summary>
    public sealed class AdvancedVisemeExpertPoseCodecTests
    {
        private const int TrainingSamplesPerViseme = 96;
        private const int HeldOutSamplesPerViseme = 192;

        [Test]
        public void LocalAffineFitPreservesTrackerIdentityAndProtectedSpeech()
        {
            var reference = AdvancedVisemeExpertPosePrototype.CreateReferenceModel();
            var training = AdvancedVisemeExpertPosePrototype.CreateSamples(
                reference, TrainingSamplesPerViseme, 0x51A7E);
            var fitted = AdvancedVisemeExpertPosePrototype.Fit(training, 1e-10, 1e-7f);
            var heldOut = AdvancedVisemeExpertPosePrototype.CreateSamples(
                reference, HeldOutSamplesPerViseme, 0xC0A47);

            var errors = new List<float>();
            var velocityErrors = new List<float>();
            var previousReference = new float[AdvancedVisemeExpertPosePrototype.OutputCount];
            var previousCandidate = new float[AdvancedVisemeExpertPosePrototype.OutputCount];
            var first = true;
            foreach (var sample in heldOut)
            {
                var expected = AdvancedVisemeExpertPosePrototype.Evaluate(
                    reference, sample.viseme, sample.features, true);
                var actual = AdvancedVisemeExpertPosePrototype.Evaluate(
                    fitted, sample.viseme, sample.features, true);
                for (var output = 0; output < expected.Length; output++)
                {
                    errors.Add(Mathf.Abs(expected[output] - actual[output]));
                    if (!first)
                    {
                        var expectedVelocity = expected[output] - previousReference[output];
                        var actualVelocity = actual[output] - previousCandidate[output];
                        velocityErrors.Add(Mathf.Abs(expectedVelocity - actualVelocity));
                    }
                    previousReference[output] = expected[output];
                    previousCandidate[output] = actual[output];
                }
                first = false;

                Assert.That(actual[AdvancedVisemeExpertPosePrototype.JawOutput],
                    sample.viseme == 6 || sample.viseme == 7
                        ? Is.LessThanOrEqualTo(0.22001f)
                        : Is.EqualTo(sample.features[0]).Within(0.00002f),
                    "The common raw-jaw passthrough drifted inside a viseme expert.");
                if (sample.viseme == 1)
                    Assert.That(actual[AdvancedVisemeExpertPosePrototype.LipCloseOutput],
                        Is.GreaterThanOrEqualTo(0.89999f));
                if (sample.viseme == 2)
                    Assert.That(actual[AdvancedVisemeExpertPosePrototype.LipBiteOutput],
                        Is.GreaterThanOrEqualTo(0.84999f));
            }

            errors.Sort();
            velocityErrors.Sort();
            var rms = Mathf.Sqrt(errors.Average(value => value * value));
            var p99 = Percentile(errors, 0.99f);
            var maximum = errors[errors.Count - 1];
            var velocityMaximum = velocityErrors[velocityErrors.Count - 1];
            TestContext.WriteLine(
                $"expert-fit rms={rms:E3} p99={p99:E3} max={maximum:E3} " +
                $"velocityMax={velocityMaximum:E3} retainedDeltaColumns={fitted.RetainedDeltaColumns}");

            Assert.That(rms, Is.LessThan(1e-5f));
            Assert.That(p99, Is.LessThan(2e-5f));
            Assert.That(maximum, Is.LessThan(5e-5f));
            Assert.That(velocityMaximum, Is.LessThan(8e-5f));
            Assert.That(fitted.RetainedDeltaColumns, Is.LessThanOrEqualTo(45),
                "The fitted residual stopped being sparse enough for state-local evaluation.");
        }

        [Test]
        public void CompiledGraphHasOneHardRoutedExpertLayerAndSparseStateClosures()
        {
            var fitted = FitReference();
            using (var graph = AdvancedVisemeExpertPosePrototype.CreateController(fitted))
            {
                var controller = graph.controller;
                Assert.That(controller.layers, Has.Length.EqualTo(2));
                var baselineLayer = controller.layers.Single(candidate =>
                    candidate.name == AdvancedVisemeExpertPosePrototype.BaselineLayerName);
                var layer = controller.layers.Single(candidate =>
                    candidate.name == AdvancedVisemeExpertPosePrototype.LayerName);
                Assert.That(layer.name, Is.EqualTo(AdvancedVisemeExpertPosePrototype.LayerName));
                Assert.That(layer.blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Override),
                    "The expert writes an encoded residual; the following layer composes it.");
                var states = layer.stateMachine.states.Select(child => child.state).ToArray();
                Assert.That(states, Has.Length.EqualTo(VisemeReconstructionProfile.VisemeCount));
                Assert.That(states.Select(state => state.name),
                    Is.EquivalentTo(VisemeReconstructionProfile.VisemeNames));
                Assert.That(states.SelectMany(state => state.transitions).Count(),
                    Is.EqualTo(VisemeReconstructionProfile.VisemeCount *
                               (VisemeReconstructionProfile.VisemeCount - 1)));

                var baselineLeaves = AdvancedVisemeExpertPosePrototype.CountClipLeaves(
                    baselineLayer.stateMachine.defaultState.motion);
                var residualClosures = states.Select(state =>
                    AdvancedVisemeExpertPosePrototype.CountClipLeaves(state.motion)).ToArray();
                var closures = residualClosures.Select(value => value + baselineLeaves).ToArray();
                TestContext.WriteLine(
                    $"expert-topology states={states.Length} baselineLeaves={baselineLeaves} " +
                    $"steadyLeaves={closures.Min()}-{closures.Max()} " +
                    $"transitionLeaves<={baselineLeaves + residualClosures.OrderByDescending(value => value).Take(2).Sum()} " +
                    $"totalClips={controller.animationClips.Distinct().Count()}");

                Assert.That(closures.Max(), Is.LessThanOrEqualTo(40),
                    "A state closure grew beyond the useful local-expert budget.");
                Assert.That(baselineLeaves + residualClosures
                        .OrderByDescending(value => value).Take(2).Sum(),
                    Is.LessThanOrEqualTo(48));
                Assert.That(states.All(state => state.motion is BlendTree), Is.True);
                Assert.That(states.Select(state => state.motion).Distinct().Count(),
                    Is.EqualTo(states.Length),
                    "Each state must own a separate residual root; a global all-expert tree would stay hot.");
                Assert.That(baselineLeaves +
                            AdvancedVisemeExpertPosePrototype.CountClipLeaves(
                                states.Single(state => state.name == "aa").motion),
                    Is.LessThanOrEqualTo(36),
                    "A tracked vowel should contain almost no visible speech correction.");
            }
        }

        [Test]
        public void NativeTransitionsCannotDelaySharedJawTracking()
        {
            var fitted = FitReference();
            using (var graph = AdvancedVisemeExpertPosePrototype.CreateController(fitted))
            {
                var avatar = new GameObject("AVR expert execution test")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                try
                {
                    var animator = avatar.AddComponent<Animator>();
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.runtimeAnimatorController = graph.controller;
                    animator.Rebind();

                    var features = Enumerable.Repeat(0.25f,
                        AdvancedVisemeExpertPosePrototype.FeatureCount).ToArray();
                    features[AdvancedVisemeExpertPosePrototype.VoiceFeature] = 0.8f;
                    Drive(animator, 10, features);
                    animator.Update(1f / 60f);
                    animator.Update(0.2f);
                    animator.Update(1f / 60f);
                    AssertOutputs(animator, fitted, 10, features, 0.00008f);

                    // Begin a non-zero-duration aa -> E transition while changing
                    // raw jaw by a large amount. Both states reference the same
                    // baseline motion, so its cross-fade is the identity.
                    features[0] = 0.87f;
                    Drive(animator, 11, features);
                    animator.Update(1f / 144f);
                    Assert.That(animator.GetFloat(
                            AdvancedVisemeExpertPosePrototype.OutputParameter(
                                AdvancedVisemeExpertPosePrototype.JawOutput)),
                        Is.EqualTo(0.87f).Within(0.00008f));

                    // Interrupt before the first transition completes. This is
                    // the ABA/ABC case that previously made native observers lag.
                    features[0] = 0.36f;
                    Drive(animator, 12, features);
                    animator.Update(1f / 144f);
                    // An interruption is selected at the end of this Animator
                    // epoch. The shared Direct inputs are visible on the next
                    // epoch, but must never inherit the 40 ms speech cross-fade.
                    animator.Update(1f / 144f);
                    Assert.That(animator.GetFloat(
                            AdvancedVisemeExpertPosePrototype.OutputParameter(
                                AdvancedVisemeExpertPosePrototype.JawOutput)),
                        Is.EqualTo(0.36f).Within(0.00008f));

                    // One tick selects the destination transition; the next
                    // advances it. Animator.Update does not retroactively spend
                    // the selecting tick's delta inside a newly selected edge.
                    animator.Update(1f / 60f);
                    animator.Update(0.2f);
                    animator.Update(1f / 60f);
                    AssertOutputs(animator, fitted, 12, features, 0.00012f);

                    // Verify every endpoint, including the exact protected
                    // PP/FF/CH/SS states, after an interruptible transition.
                    for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
                    {
                        features[0] = 0.73f;
                        features[1] = 0.08f;
                        features[AdvancedVisemeExpertPosePrototype.VoiceFeature] = 0.91f;
                        Drive(animator, viseme, features);
                        animator.Update(1f / 60f);
                        animator.Update(0.2f);
                        animator.Update(1f / 60f);
                        AssertOutputs(animator, fitted, viseme, features, 0.0002f);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(avatar);
                }
            }
        }

        [Test]
        public void ManualPlayableBenchmarkReportsRealConnectedClipReduction()
        {
            var fitted = FitReference();
            using (var expert = AdvancedVisemeExpertPosePrototype.CreateController(fitted))
            using (var dense = AdvancedVisemeExpertPosePrototype.CreateDenseReference(2084))
            using (var expertPlayer = new ManualPlayer(expert.controller, "expert"))
            using (var densePlayer = new ManualPlayer(dense.controller, "dense"))
            {
                expertPlayer.SetExpertInputs();
                densePlayer.SetDenseInputs();
                expertPlayer.Warm(160);
                densePlayer.Warm(160);

                var expertClips = expertPlayer.ClipCounts();
                var denseClips = densePlayer.ClipCounts();
                var denseCurves = dense.controller.animationClips.Distinct()
                    .Sum(clip => AnimationUtility.GetCurveBindings(clip).Length);
                var expertCurves = expert.controller.animationClips.Distinct()
                    .Sum(clip => AnimationUtility.GetCurveBindings(clip).Length);
                Assert.That(expertClips.active, Is.LessThanOrEqualTo(40));
                Assert.That(expertClips.total, Is.LessThanOrEqualTo(64));
                Assert.That(denseClips.active, Is.EqualTo(2084));
                Assert.That(denseCurves, Is.EqualTo(4453));

                const int batches = 9;
                const int frames = 320;
                var expertTimes = new List<double>();
                var denseTimes = new List<double>();
                for (var batch = 0; batch < batches; batch++)
                {
                    if ((batch & 1) == 0)
                    {
                        denseTimes.Add(densePlayer.Measure(frames));
                        expertTimes.Add(expertPlayer.Measure(frames));
                    }
                    else
                    {
                        expertTimes.Add(expertPlayer.Measure(frames));
                        denseTimes.Add(densePlayer.Measure(frames));
                    }
                }

                expertTimes.Sort();
                denseTimes.Sort();
                var expertMedian = expertTimes[expertTimes.Count / 2];
                var denseMedian = denseTimes[denseTimes.Count / 2];
                TestContext.WriteLine(
                    $"expert-benchmark dense={denseMedian:F6}ms expert={expertMedian:F6}ms " +
                    $"delta={denseMedian - expertMedian:F6}ms ratio={expertMedian / denseMedian:F4} " +
                    $"activeClips={denseClips.active}->{expertClips.active} " +
                    $"connectedClips={denseClips.total}->{expertClips.total} " +
                    $"authoredCurves={denseCurves}->{expertCurves}");
                UnityEngine.Debug.Log(
                    $"[YUCP AVR Expert Test] dense={denseMedian:F6}ms expert={expertMedian:F6}ms " +
                    $"saved={denseMedian - expertMedian:F6}ms " +
                    $"activeClips={denseClips.active}->{expertClips.active} " +
                    $"connectedClips={denseClips.total}->{expertClips.total} " +
                    $"authoredCurves={denseCurves}->{expertCurves}");

                // Wall-clock timing is intentionally reported, not gated: editor
                // scheduling and host power make a Stopwatch threshold flaky.
                Assert.That(expertMedian, Is.GreaterThan(0d));
                Assert.That(denseMedian, Is.GreaterThan(0d));
            }
        }

        private static AdvancedVisemeExpertPosePrototype.Model FitReference()
        {
            var reference = AdvancedVisemeExpertPosePrototype.CreateReferenceModel();
            return AdvancedVisemeExpertPosePrototype.Fit(
                AdvancedVisemeExpertPosePrototype.CreateSamples(
                    reference, TrainingSamplesPerViseme, 0x51A7E),
                1e-10, 1e-7f);
        }

        private static void Drive(Animator animator, int viseme, IReadOnlyList<float> features)
        {
            animator.SetInteger("Viseme", viseme);
            for (var feature = 0; feature < features.Count; feature++)
                animator.SetFloat(
                    AdvancedVisemeExpertPosePrototype.FeatureParameter(feature),
                    features[feature]);
        }

        private static void AssertOutputs(
            Animator animator,
            AdvancedVisemeExpertPosePrototype.Model model,
            int viseme,
            float[] features,
            float tolerance)
        {
            var expected = AdvancedVisemeExpertPosePrototype.Evaluate(
                model, viseme, features, true);
            for (var output = 0; output < expected.Length; output++)
                Assert.That(animator.GetFloat(
                        AdvancedVisemeExpertPosePrototype.OutputParameter(output)),
                    Is.EqualTo(expected[output]).Within(tolerance),
                    $"Output {AdvancedVisemeExpertPosePrototype.Outputs[output]} drifted for " +
                    $"{VisemeReconstructionProfile.VisemeNames[viseme]}.");
        }

        private static float Percentile(IReadOnlyList<float> sorted, float percentile)
        {
            var index = Mathf.Clamp(
                Mathf.CeilToInt(percentile * sorted.Count) - 1, 0, sorted.Count - 1);
            return sorted[index];
        }

        private sealed class ManualPlayer : IDisposable
        {
            private readonly GameObject target;
            private readonly PlayableGraph graph;
            private readonly AnimatorControllerPlayable playable;

            public ManualPlayer(AnimatorController controller, string name)
            {
                target = new GameObject("AVR " + name + " benchmark")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                var animator = target.AddComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph = PlayableGraph.Create("AVR " + name + " benchmark");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable = AnimatorControllerPlayable.Create(graph, controller);
                var output = AnimationPlayableOutput.Create(graph, "Animator", animator);
                output.SetSourcePlayable(playable);
                graph.Play();
            }

            public void SetExpertInputs()
            {
                playable.SetInteger("Viseme", 10);
                for (var feature = 0;
                     feature < AdvancedVisemeExpertPosePrototype.FeatureCount;
                     feature++)
                    playable.SetFloat(
                        AdvancedVisemeExpertPosePrototype.FeatureParameter(feature),
                        0.15f + feature * 0.07f);
            }

            public void SetDenseInputs()
            {
                playable.SetFloat(AdvancedVisemeExpertPosePrototype.DenseWeightParameter,
                    1f / 2084f);
            }

            public void Warm(int frames)
            {
                for (var frame = 0; frame < frames; frame++)
                    graph.Evaluate(1f / 90f);
            }

            public (int total, int active) ClipCounts()
            {
                var total = 0;
                var active = 0;
                var stack = new Stack<(Playable node, double weight)>();
                stack.Push((playable, 1d));
                while (stack.Count > 0)
                {
                    var item = stack.Pop();
                    var node = item.node;
                    if (!node.IsValid()) continue;
                    if (node.GetPlayableType() == typeof(AnimationClipPlayable))
                    {
                        total++;
                        if (Math.Abs(item.weight) > 1e-7) active++;
                    }
                    for (var input = 0; input < node.GetInputCount(); input++)
                    {
                        var child = node.GetInput(input);
                        if (child.IsValid())
                            stack.Push((child,
                                item.weight * node.GetInputWeight(input)));
                    }
                }
                return (total, active);
            }

            public double Measure(int frames)
            {
                var timer = Stopwatch.StartNew();
                for (var frame = 0; frame < frames; frame++)
                    graph.Evaluate(1f / 90f);
                timer.Stop();
                return timer.Elapsed.TotalMilliseconds / frames;
            }

            public void Dispose()
            {
                if (graph.IsValid()) graph.Destroy();
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }

    internal static class AdvancedVisemeExpertPosePrototype
    {
        internal const string BaselineLayerName = "YUCP AVR Immediate Tracking Baseline";
        internal const string LayerName = "YUCP AVR Expert Prototype";
        internal const string DenseWeightParameter = "YUCP/ExpertTest/DenseWeight";
        internal const int TrackerFeatureCount = 8;
        internal const int VoiceFeature = 8;
        internal const int FeatureCount = 9;
        private const float ResidualBound = 2f;

        internal static readonly AdvancedVisemeArticulator[] Outputs =
            ((AdvancedVisemeArticulator[])Enum.GetValues(
                typeof(AdvancedVisemeArticulator))).OrderBy(value => (int)value).ToArray();

        internal static int OutputCount => Outputs.Length;
        internal static int JawOutput => OutputIndex(AdvancedVisemeArticulator.JawOpen);
        internal static int LipCloseOutput => OutputIndex(AdvancedVisemeArticulator.LipClose);
        internal static int LipBiteOutput => OutputIndex(AdvancedVisemeArticulator.LipBite);

        private static readonly AdvancedVisemeArticulator[] TrackerOutputs =
        {
            AdvancedVisemeArticulator.JawOpen,
            AdvancedVisemeArticulator.LipClose,
            AdvancedVisemeArticulator.MouthOpen,
            AdvancedVisemeArticulator.LipFunnel,
            AdvancedVisemeArticulator.LipPucker,
            AdvancedVisemeArticulator.LipSuck,
            AdvancedVisemeArticulator.SmileSad,
            AdvancedVisemeArticulator.TongueOut
        };

        internal sealed class Model
        {
            internal readonly float[,] common = new float[OutputCount, FeatureCount];
            internal readonly float[,] bias = new float[
                VisemeReconstructionProfile.VisemeCount, OutputCount];
            internal readonly float[,,] delta = new float[
                VisemeReconstructionProfile.VisemeCount, OutputCount, FeatureCount];

            internal int RetainedDeltaColumns => Enumerable.Range(
                    0, VisemeReconstructionProfile.VisemeCount)
                .Sum(viseme => Enumerable.Range(0, FeatureCount).Count(feature =>
                    Enumerable.Range(0, OutputCount).Any(output =>
                        Mathf.Abs(delta[viseme, output, feature]) > 1e-7f)));
        }

        internal readonly struct Sample
        {
            internal readonly int viseme;
            internal readonly float[] features;
            internal readonly float[] rawOutput;

            internal Sample(int viseme, float[] features, float[] rawOutput)
            {
                this.viseme = viseme;
                this.features = features;
                this.rawOutput = rawOutput;
            }
        }

        internal sealed class ControllerGraph : IDisposable
        {
            internal readonly AnimatorController controller;
            private readonly List<UnityEngine.Object> owned;
            private string assetPath;

            internal ControllerGraph(
                AnimatorController controller,
                List<UnityEngine.Object> owned)
            {
                this.controller = controller;
                this.owned = owned;
            }

            internal void Persist(string path)
            {
                if (!string.IsNullOrEmpty(assetPath))
                    throw new InvalidOperationException("Prototype is already persistent.");
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(controller, path);
                var added = new HashSet<int> { controller.GetInstanceID() };
                foreach (var item in owned)
                    AddSubAsset(item, added);
                foreach (var layer in controller.layers)
                    AddStateMachine(layer.stateMachine, added);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                assetPath = path;
            }

            private void AddStateMachine(AnimatorStateMachine machine, ISet<int> added)
            {
                if (machine == null) return;
                AddSubAsset(machine, added);
                foreach (var child in machine.states)
                {
                    AddSubAsset(child.state, added);
                    foreach (var transition in child.state.transitions)
                        AddSubAsset(transition, added);
                }
                foreach (var transition in machine.anyStateTransitions)
                    AddSubAsset(transition, added);
                foreach (var transition in machine.entryTransitions)
                    AddSubAsset(transition, added);
                foreach (var child in machine.stateMachines)
                {
                    foreach (var transition in machine.GetStateMachineTransitions(
                                 child.stateMachine))
                        AddSubAsset(transition, added);
                    AddStateMachine(child.stateMachine, added);
                }
            }

            private void AddSubAsset(UnityEngine.Object item, ISet<int> added)
            {
                if (item == null || !added.Add(item.GetInstanceID()) ||
                    AssetDatabase.Contains(item)) return;
                AssetDatabase.AddObjectToAsset(item, controller);
            }

            public void Dispose()
            {
                if (!string.IsNullOrEmpty(assetPath))
                {
                    AssetDatabase.DeleteAsset(assetPath);
                    assetPath = null;
                    return;
                }
                for (var index = owned.Count - 1; index >= 0; index--)
                    if (owned[index] != null)
                        UnityEngine.Object.DestroyImmediate(owned[index]);
            }
        }

        internal static Model CreateReferenceModel()
        {
            var model = new Model();
            for (var tracker = 0; tracker < TrackerOutputs.Length; tracker++)
                model.common[OutputIndex(TrackerOutputs[tracker]), tracker] = 1f;

            var profile = ScriptableObject.CreateInstance<VisemeReconstructionProfile>();
            try
            {
                profile.EnsureDefaults();
                for (var viseme = 0;
                     viseme < VisemeReconstructionProfile.VisemeCount;
                     viseme++)
                {
                    var pose = profile.visemePoses[viseme];
                    for (var output = 0; output < OutputCount; output++)
                    {
                        if (Array.IndexOf(TrackerOutputs, Outputs[output]) >= 0) continue;
                        var value = pose.Get(Outputs[output]);
                        model.bias[viseme, output] = value * profile.quietSpeechFloor;
                        model.delta[viseme, output, VoiceFeature] =
                            value * (1f - profile.quietSpeechFloor);
                    }

                    // A small, sparse visible-to-hidden inference term makes the
                    // experiment exercise residual coefficient pruning without
                    // changing any measured lower-face coordinate.
                    foreach (var articulator in new[]
                             {
                                 AdvancedVisemeArticulator.TongueArchY,
                                 AdvancedVisemeArticulator.TongueShape,
                                 AdvancedVisemeArticulator.TongueRoll
                             })
                    {
                        var output = OutputIndex(articulator);
                        var sign = Mathf.Sign(pose.Get(articulator));
                        if (Mathf.Abs(sign) < 0.5f) sign = (viseme & 1) == 0 ? 1f : -1f;
                        model.delta[viseme, output, 0] = sign * 0.012f;
                        model.delta[viseme, output, 2] = sign * 0.018f;
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
            return model;
        }

        internal static List<Sample> CreateSamples(Model model, int perViseme, int seed)
        {
            var random = new System.Random(seed);
            var output = new List<Sample>(
                perViseme * VisemeReconstructionProfile.VisemeCount);
            for (var viseme = 0;
                 viseme < VisemeReconstructionProfile.VisemeCount;
                 viseme++)
            {
                // Axis endpoints make the affine fit well conditioned and also
                // pin exact tracker authority at zero and one.
                for (var sampleIndex = 0; sampleIndex < perViseme; sampleIndex++)
                {
                    var features = new float[FeatureCount];
                    for (var feature = 0; feature < FeatureCount; feature++)
                        features[feature] = sampleIndex <= FeatureCount
                            ? sampleIndex == feature + 1 ? 1f : 0f
                            : (float)random.NextDouble();
                    output.Add(new Sample(
                        viseme, features, Evaluate(model, viseme, features, false)));
                }
            }
            return output;
        }

        internal static Model Fit(
            IReadOnlyList<Sample> samples,
            double ridge,
            float pruneThreshold)
        {
            var perState = new float[
                VisemeReconstructionProfile.VisemeCount, OutputCount, FeatureCount + 1];
            for (var viseme = 0;
                 viseme < VisemeReconstructionProfile.VisemeCount;
                 viseme++)
            {
                var stateSamples = samples.Where(sample => sample.viseme == viseme).ToArray();
                Assert.That(stateSamples.Length, Is.GreaterThan(FeatureCount + 1));
                var gram = new double[FeatureCount + 1, FeatureCount + 1];
                var rhs = new double[OutputCount, FeatureCount + 1];
                foreach (var sample in stateSamples)
                {
                    var row = new double[FeatureCount + 1];
                    row[0] = 1d;
                    for (var feature = 0; feature < FeatureCount; feature++)
                        row[feature + 1] = sample.features[feature];
                    for (var left = 0; left < row.Length; left++)
                    {
                        for (var right = 0; right < row.Length; right++)
                            gram[left, right] += row[left] * row[right];
                        for (var output = 0; output < OutputCount; output++)
                            rhs[output, left] += row[left] * sample.rawOutput[output];
                    }
                }
                for (var diagonal = 1; diagonal < FeatureCount + 1; diagonal++)
                    gram[diagonal, diagonal] += ridge;
                for (var output = 0; output < OutputCount; output++)
                {
                    var target = new double[FeatureCount + 1];
                    for (var column = 0; column < target.Length; column++)
                        target[column] = rhs[output, column];
                    var solved = Solve(gram, target);
                    for (var column = 0; column < solved.Length; column++)
                        perState[viseme, output, column] = (float)solved[column];
                }
            }

            var model = new Model();
            for (var output = 0; output < OutputCount; output++)
            {
                for (var feature = 0; feature < FeatureCount; feature++)
                {
                    model.common[output, feature] = Enumerable.Range(
                            0, VisemeReconstructionProfile.VisemeCount)
                        .Average(viseme => perState[viseme, output, feature + 1]);
                }
            }
            for (var viseme = 0;
                 viseme < VisemeReconstructionProfile.VisemeCount;
                 viseme++)
            {
                for (var output = 0; output < OutputCount; output++)
                {
                    model.bias[viseme, output] = perState[viseme, output, 0];
                    for (var feature = 0; feature < FeatureCount; feature++)
                        model.delta[viseme, output, feature] =
                            perState[viseme, output, feature + 1] -
                            model.common[output, feature];
                }
                for (var feature = 0; feature < FeatureCount; feature++)
                {
                    var norm = Mathf.Sqrt(Enumerable.Range(0, OutputCount).Sum(output =>
                        model.delta[viseme, output, feature] *
                        model.delta[viseme, output, feature]));
                    if (norm >= pruneThreshold) continue;
                    for (var output = 0; output < OutputCount; output++)
                        model.delta[viseme, output, feature] = 0f;
                }
            }

            // The safety row is not learned approximately. If all state fits
            // agree on raw jaw identity, pin that common row and erase residuals.
            var jaw = JawOutput;
            var jawIdentity = true;
            for (var viseme = 0;
                 viseme < VisemeReconstructionProfile.VisemeCount;
                 viseme++)
            {
                jawIdentity &= Mathf.Abs(model.bias[viseme, jaw]) < 2e-5f;
                for (var feature = 0; feature < FeatureCount; feature++)
                {
                    var expected = feature == 0 ? 1f : 0f;
                    jawIdentity &= Mathf.Abs(
                        model.common[jaw, feature] +
                        model.delta[viseme, jaw, feature] - expected) < 2e-5f;
                }
            }
            Assert.That(jawIdentity, Is.True,
                "Training data did not prove the protected raw-jaw identity.");
            for (var feature = 0; feature < FeatureCount; feature++)
                model.common[jaw, feature] = feature == 0 ? 1f : 0f;
            for (var viseme = 0;
                 viseme < VisemeReconstructionProfile.VisemeCount;
                 viseme++)
            {
                model.bias[viseme, jaw] = 0f;
                for (var feature = 0; feature < FeatureCount; feature++)
                    model.delta[viseme, jaw, feature] = 0f;
            }
            return model;
        }

        internal static float[] Evaluate(
            Model model,
            int viseme,
            IReadOnlyList<float> features,
            bool constraints)
        {
            var output = new float[OutputCount];
            for (var target = 0; target < OutputCount; target++)
            {
                var value = model.bias[viseme, target];
                for (var feature = 0; feature < FeatureCount; feature++)
                    value += (model.common[target, feature] +
                              model.delta[viseme, target, feature]) *
                             features[feature];
                output[target] = value;
            }
            if (!constraints) return output;
            if (viseme == 1)
                output[LipCloseOutput] = Mathf.Max(0.9f, output[LipCloseOutput]);
            if (viseme == 2)
                output[LipBiteOutput] = Mathf.Max(0.85f, output[LipBiteOutput]);
            if (viseme == 6 || viseme == 7)
                output[JawOutput] = Mathf.Min(0.22f, output[JawOutput]);
            return output;
        }

        internal static ControllerGraph CreateController(Model model)
        {
            var owned = new List<UnityEngine.Object>();
            var controller = new AnimatorController { name = "YUCP AVR Expert Prototype" };
            owned.Add(controller);
            AddParameter(controller, "Viseme", AnimatorControllerParameterType.Int, 0f);
            AddParameter(controller, OneParameter, AnimatorControllerParameterType.Float, 1f);
            for (var feature = 0; feature < FeatureCount; feature++)
                AddParameter(controller, FeatureParameter(feature),
                    AnimatorControllerParameterType.Float, 0f);
            for (var output = 0; output < OutputCount; output++)
                AddParameter(controller, OutputParameter(output),
                    AnimatorControllerParameterType.Float, 0f);
            var residualOutputs = ResidualOutputs(model);
            foreach (var output in residualOutputs)
                AddParameter(controller, ResidualParameter(output),
                    AnimatorControllerParameterType.Float, 0.5f);

            controller.AddLayer(LayerName);
            var layers = controller.layers;
            var layer = layers[layers.Length - 1];
            layer.defaultWeight = 1f;
            layers[layers.Length - 1] = layer;
            controller.layers = layers;
            var machine = layer.stateMachine;

            var states = new AnimatorState[VisemeReconstructionProfile.VisemeCount];
            for (var viseme = 0; viseme < states.Length; viseme++)
            {
                var state = machine.AddState(VisemeReconstructionProfile.VisemeNames[viseme]);
                state.writeDefaultValues = true;
                var root = Direct(owned, "Expert " + state.name);
                AddDirectChild(root,
                    ResidualClip(owned, state.name + " encoded bias",
                        Row(model.bias, viseme), residualOutputs, true, 0.5f),
                    OneParameter);
                for (var feature = 0; feature < FeatureCount; feature++)
                {
                    var values = DeltaColumn(model.delta, viseme, feature);
                    if (!AnyNonZero(values)) continue;
                    AddDirectChild(root,
                        ResidualClip(owned, state.name + " residual " + feature,
                            values, residualOutputs),
                        FeatureParameter(feature));
                }
                var constraint = Constraint(owned, model, viseme, true);
                if (constraint != null) AddDirectChild(root, constraint, OneParameter);
                state.motion = root;
                states[viseme] = state;
            }
            machine.defaultState = states[0];
            for (var source = 0; source < states.Length; source++)
            {
                for (var destination = 0; destination < states.Length; destination++)
                {
                    if (source == destination) continue;
                    var transition = states[source].AddTransition(states[destination]);
                    transition.name = states[source].name + " -> " + states[destination].name;
                    transition.AddCondition(AnimatorConditionMode.Equals, destination, "Viseme");
                    transition.hasExitTime = false;
                    transition.hasFixedDuration = true;
                    transition.duration = 0.04f;
                    transition.canTransitionToSelf = false;
                    // Inspect only the destination state's outgoing edges while
                    // blending. It can redirect to every other viseme (including
                    // the original source), but the still-true source edge cannot
                    // repeatedly restart its own transition every frame.
                    transition.interruptionSource = TransitionInterruptionSource.Destination;
                    transition.orderedInterruption = false;
                }
            }

            // The state machine writes a bounded unit-domain residual. This
            // always-active layer decodes it and adds W0f in one Direct tree.
            // Tracker inputs therefore never enter a state transition.
            controller.AddLayer(BaselineLayerName);
            layers = controller.layers;
            layer = layers[layers.Length - 1];
            layer.defaultWeight = 1f;
            layers[layers.Length - 1] = layer;
            controller.layers = layers;
            var compose = Direct(owned, "Immediate affine output composition");
            for (var feature = 0; feature < FeatureCount; feature++)
            {
                var values = Column(model.common, feature);
                if (!AnyNonZero(values)) continue;
                AddDirectChild(compose,
                    Clip(owned, "Common affine " + feature, values),
                    FeatureParameter(feature));
            }
            var decodeBias = new float[OutputCount];
            foreach (var output in residualOutputs) decodeBias[output] = -ResidualBound;
            AddDirectChild(compose,
                Clip(owned, "Residual decode bias", decodeBias), OneParameter);
            foreach (var output in residualOutputs)
            {
                var decode = new float[OutputCount];
                decode[output] = 2f * ResidualBound;
                AddDirectChild(compose,
                    Clip(owned, "Residual decode " + Outputs[output], decode),
                    ResidualParameter(output));
            }
            var composeState = layer.stateMachine.AddState("Immediate");
            composeState.writeDefaultValues = true;
            composeState.motion = compose;
            layer.stateMachine.defaultState = composeState;
            return new ControllerGraph(controller, owned);
        }

        internal static ControllerGraph CreateDenseReference(int clipCount)
        {
            const int denseOutputCount = 128;
            var owned = new List<UnityEngine.Object>();
            var controller = new AnimatorController { name = "YUCP AVR Dense Reference" };
            owned.Add(controller);
            AddParameter(controller, DenseWeightParameter,
                AnimatorControllerParameterType.Float, 1f / clipCount);
            for (var output = 0; output < denseOutputCount; output++)
                AddParameter(controller, DenseOutputParameter(output),
                    AnimatorControllerParameterType.Float, 0f);
            controller.AddLayer("Dense connected math");
            var layers = controller.layers;
            var layer = layers[layers.Length - 1];
            layer.defaultWeight = 1f;
            layers[layers.Length - 1] = layer;
            controller.layers = layers;
            var root = Direct(owned, "Dense 2084-leaf reference");
            for (var index = 0; index < clipCount; index++)
            {
                var clip = new AnimationClip { name = "Dense operation " + index };
                owned.Add(clip);
                var firstOutput = index % denseOutputCount;
                var secondOutput = (index * 47 + 13) % denseOutputCount;
                SetCurve(clip, DenseOutputParameter(firstOutput),
                    (index & 1) == 0 ? 1f : -1f);
                SetCurve(clip, DenseOutputParameter(secondOutput),
                    (index % 7) / 7f);
                // 2,084 * 2 + 285 = 4,453 curves: the measured dense AVR
                // inventory, spread over many AAP bindings instead of two
                // unrealistically cache-friendly properties.
                if (index < 285)
                {
                    var thirdOutput = (firstOutput + 64) % denseOutputCount;
                    if (thirdOutput == secondOutput)
                        thirdOutput = (firstOutput + 32) % denseOutputCount;
                    SetCurve(clip, DenseOutputParameter(thirdOutput),
                        (index % 11) / 11f);
                }
                AddDirectChild(root, clip, DenseWeightParameter);
            }
            var state = layer.stateMachine.AddState("Dense");
            state.writeDefaultValues = true;
            state.motion = root;
            layer.stateMachine.defaultState = state;
            return new ControllerGraph(controller, owned);
        }

        internal static int CountClipLeaves(Motion motion)
        {
            if (motion == null) return 0;
            var tree = motion as BlendTree;
            if (tree == null) return motion is AnimationClip ? 1 : 0;
            return tree.children.Sum(child => CountClipLeaves(child.motion));
        }

        internal static string FeatureParameter(int feature) =>
            feature == VoiceFeature
                ? "Voice"
                : "YUCP/ExpertTest/Track/" + feature;

        internal static string OutputParameter(int output) =>
            "YUCP/ExpertTest/Out/" + Outputs[output];

        private static string ResidualParameter(int output) =>
            "YUCP/ExpertTest/ResidualUnit/" + Outputs[output];

        private static string DenseOutputParameter(int output) =>
            "YUCP/ExpertTest/DenseOut/" + output;

        private const string OneParameter = "YUCP/ExpertTest/One";

        private static Motion Constraint(
            List<UnityEngine.Object> owned,
            Model model,
            int viseme,
            bool encodedResidual)
        {
            if (viseme == 1)
                return CorrectionMap(owned, "PP closure", FeatureParameter(1),
                    new[] { 0f, 0.9f, 1f },
                    input => Mathf.Max(0.9f, input) - input,
                    LipCloseOutput, encodedResidual);
            if (viseme == 6 || viseme == 7)
                return CorrectionMap(owned, "Sibilant jaw cap", FeatureParameter(0),
                    new[] { 0f, 0.22f, 1f },
                    input => Mathf.Min(0.22f, input) - input,
                    JawOutput, encodedResidual);
            if (viseme != 2) return null;

            var bias = model.bias[viseme, LipBiteOutput];
            var slope = model.common[LipBiteOutput, VoiceFeature] +
                        model.delta[viseme, LipBiteOutput, VoiceFeature];
            var knots = new List<float> { 0f, 1f };
            if (Mathf.Abs(slope) > 1e-7f)
            {
                var crossing = (0.85f - bias) / slope;
                if (crossing > 0.0001f && crossing < 0.9999f) knots.Add(crossing);
            }
            knots.Sort();
            return CorrectionMap(owned, "FF bite floor", FeatureParameter(VoiceFeature),
                knots.ToArray(),
                input => Mathf.Max(0.85f, bias + slope * input) -
                         (bias + slope * input),
                LipBiteOutput, encodedResidual);
        }

        private static Motion CorrectionMap(
            List<UnityEngine.Object> owned,
            string name,
            string parameter,
            IReadOnlyList<float> knots,
            Func<float, float> correction,
            int output,
            bool encodedResidual)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = parameter,
                useAutomaticThresholds = false
            };
            owned.Add(tree);
            var children = new ChildMotion[knots.Count];
            for (var index = 0; index < knots.Count; index++)
            {
                var values = new float[OutputCount];
                values[output] = correction(knots[index]);
                children[index] = new ChildMotion
                {
                    motion = encodedResidual
                        ? ResidualClip(owned, name + " " + index, values,
                            new[] { output })
                        : Clip(owned, name + " " + index, values),
                    threshold = knots[index],
                    timeScale = 1f
                };
            }
            tree.children = children;
            return tree;
        }

        private static BlendTree Direct(List<UnityEngine.Object> owned, string name)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Direct,
                useAutomaticThresholds = false
            };
            owned.Add(tree);
            var serialized = new SerializedObject(tree);
            serialized.FindProperty("m_NormalizedBlendValues").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return tree;
        }

        private static AnimationClip Clip(
            List<UnityEngine.Object> owned,
            string name,
            IReadOnlyList<float> values,
            bool includeZeros = false)
        {
            var clip = new AnimationClip { name = name, frameRate = 60f };
            owned.Add(clip);
            for (var output = 0; output < values.Count; output++)
            {
                if (!includeZeros && Mathf.Abs(values[output]) < 1e-8f) continue;
                SetCurve(clip, OutputParameter(output), values[output]);
            }
            return clip;
        }

        private static AnimationClip ResidualClip(
            List<UnityEngine.Object> owned,
            string name,
            IReadOnlyList<float> values,
            IReadOnlyCollection<int> residualOutputs,
            bool includeZeros = false,
            float offset = 0f)
        {
            var clip = new AnimationClip { name = name, frameRate = 60f };
            owned.Add(clip);
            foreach (var output in residualOutputs)
            {
                var value = offset + values[output] / (2f * ResidualBound);
                if (!includeZeros && Mathf.Abs(value) < 1e-8f) continue;
                SetCurve(clip, ResidualParameter(output), value);
            }
            return clip;
        }

        private static void SetCurve(AnimationClip clip, string parameter, float value)
        {
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), parameter),
                AnimationCurve.Constant(0f, 1f, value));
        }

        private static void AddDirectChild(BlendTree tree, Motion motion, string parameter)
        {
            var children = tree.children;
            Array.Resize(ref children, children.Length + 1);
            children[children.Length - 1] = new ChildMotion
            {
                motion = motion,
                directBlendParameter = parameter,
                timeScale = 1f
            };
            tree.children = children;
        }

        private static void AddParameter(
            AnimatorController controller,
            string name,
            AnimatorControllerParameterType type,
            float defaultValue)
        {
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = type,
                defaultFloat = defaultValue,
                defaultInt = Mathf.RoundToInt(defaultValue),
                defaultBool = defaultValue > 0.5f
            });
        }

        private static float[] Column(float[,] values, int column)
        {
            var output = new float[OutputCount];
            for (var row = 0; row < output.Length; row++) output[row] = values[row, column];
            return output;
        }

        private static float[] Row(float[,] values, int row)
        {
            var output = new float[OutputCount];
            for (var column = 0; column < output.Length; column++)
                output[column] = values[row, column];
            return output;
        }

        private static float[] DeltaColumn(float[,,] values, int viseme, int feature)
        {
            var output = new float[OutputCount];
            for (var target = 0; target < output.Length; target++)
                output[target] = values[viseme, target, feature];
            return output;
        }

        private static bool AnyNonZero(IEnumerable<float> values) =>
            values.Any(value => Mathf.Abs(value) > 1e-7f);

        private static int[] ResidualOutputs(Model model)
        {
            return Enumerable.Range(0, OutputCount).Where(output =>
            {
                if (output == JawOutput || output == LipCloseOutput ||
                    output == LipBiteOutput) return true;
                for (var viseme = 0;
                     viseme < VisemeReconstructionProfile.VisemeCount;
                     viseme++)
                {
                    if (Mathf.Abs(model.bias[viseme, output]) > 1e-7f) return true;
                    for (var feature = 0; feature < FeatureCount; feature++)
                        if (Mathf.Abs(model.delta[viseme, output, feature]) > 1e-7f)
                            return true;
                }
                return false;
            }).ToArray();
        }

        private static int OutputIndex(AdvancedVisemeArticulator articulator) =>
            Array.IndexOf(Outputs, articulator);

        private static double[] Solve(double[,] matrix, double[] vector)
        {
            var size = vector.Length;
            var augmented = new double[size, size + 1];
            for (var row = 0; row < size; row++)
            {
                for (var column = 0; column < size; column++)
                    augmented[row, column] = matrix[row, column];
                augmented[row, size] = vector[row];
            }
            for (var pivot = 0; pivot < size; pivot++)
            {
                var best = pivot;
                for (var row = pivot + 1; row < size; row++)
                    if (Math.Abs(augmented[row, pivot]) >
                        Math.Abs(augmented[best, pivot])) best = row;
                if (best != pivot)
                    for (var column = pivot; column <= size; column++)
                    {
                        var swap = augmented[pivot, column];
                        augmented[pivot, column] = augmented[best, column];
                        augmented[best, column] = swap;
                    }
                var scale = augmented[pivot, pivot];
                Assert.That(Math.Abs(scale), Is.GreaterThan(1e-12));
                for (var column = pivot; column <= size; column++)
                    augmented[pivot, column] /= scale;
                for (var row = 0; row < size; row++)
                {
                    if (row == pivot) continue;
                    var factor = augmented[row, pivot];
                    for (var column = pivot; column <= size; column++)
                        augmented[row, column] -= factor * augmented[pivot, column];
                }
            }
            var output = new double[size];
            for (var row = 0; row < size; row++) output[row] = augmented[row, size];
            return output;
        }
    }
}
