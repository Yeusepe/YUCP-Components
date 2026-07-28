using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YUCP.Components.Editor.Tests
{
    // Temporary empirical probe of Unity's normalized Direct BlendTree
    // semantics for AAP parameter accumulation. Delete after use.
    public sealed class NormalizedDbtProbeTests
    {

        [Test]
        public void ProbeRawTargetContinuityThroughRealController()
        {
            var folderName = "__YUCP_RawProbe_" + System.Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            var root = new GameObject("Raw Probe Build");
            GameObject rt = null;
            try
            {
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.mouthOwnership = AdvancedVisemeMouthOwnership.OutputsOnly;
                component.reconstructionMode = AdvancedVisemeReconstructionMode.Normal;
                component.trackingInputs = AdvancedVisemeTrackingInputs.Disabled;
                component.createTuningMenu = false;
                var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
                var result = AdvancedVisemeAnimatorBuilder.Build(
                    new AdvancedVisemeAnimatorBuilder.Request
                    {
                        controllerPath = folder + "/AdvancedViseme.controller",
                        parametersPath = folder + "/Parameters.asset",
                        component = component, profile = profile,
                        trackingPrefix = "YUCP/TestTracking",
                        effectiveTrackingInputs = AdvancedVisemeTrackingInputs.Disabled,
                        reuseExistingTracking = false,
                        trackingActiveParameter = "YUCP/TestTracking/LipTrackingActive",
                        trackingActiveAnimatorType = AnimatorControllerParameterType.Float,
                        trackingActiveDefault = 0f,
                        trackingParameterNames = new Dictionary<AdvancedVisemeArticulator, string>(),
                        auxiliaryTrackingParameterNames = new Dictionary<string, string>(),
                        sourceVisemeBlendShapes = new string[VisemeReconstructionProfile.VisemeCount],
                        calibrationBasis = Array.Empty<MeshUtils.AdvancedVisemeMeshCalibrator.BasisInput>(),
                        resolvedBlendShapes = new Dictionary<AdvancedVisemeArticulator, string>(),
                        externalPoses = new Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose>(),
                        trackingEnabled = false,
                        existingExpressionParameters = new HashSet<string>()
                    });

                // Curve completeness of decoder clips: a missing curve makes a
                // state contribute nothing for that channel during a blend.
                var clips = AssetDatabase.LoadAllAssetsAtPath(
                    AssetDatabase.GetAssetPath(result.controller))
                    .OfType<AnimationClip>()
                    .Where(c => c.name.StartsWith("Decode ")).ToArray();
                var report = new StringBuilder();
                var names = "sil PP FF TH DD kk CH SS nn RR aa E I O U".Split(' ');
                foreach (var c in clips.Take(4))
                {
                    var raws = AnimationUtility.GetCurveBindings(c)
                        .Count(b => b.propertyName.EndsWith("/Raw"));
                    report.Append($" | clip '{c.name}' rawCurves={raws} len={c.length:F3}");
                }

                rt = new GameObject("Raw Probe RT");
                var an = rt.AddComponent<Animator>();
                an.runtimeAnimatorController = result.controller;
                an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                an.Rebind(); an.Update(0f);
                an.SetFloat("IsLocal", 1f); an.SetFloat("Voice", 1f);

                var prefix = component.NormalizedPrefix + "/_Internal";
                var rawNames = Enumerable.Range(0, 15)
                    .Select(i => prefix + "/Viseme/" + i + "/Raw").ToArray();
                var have = rawNames.Select(n =>
                    an.parameters.Any(p => p.name == n)).ToArray();
                report.Append($" || rawParamsPresent={have.Count(b => b)}");

                // Realistic rapid speech: dwell ~4 frames at 60fps (~67ms).
                int[] seq = {10, 11, 4, 10, 8, 13, 10, 11, 4, 8};
                var log = new List<float[]>();
                foreach (var v in seq)
                {
                    for (var f = 0; f < 4; f++)
                    {
                        an.SetInteger("Viseme", v);
                        an.Update(1f / 60f);
                        log.Add(rawNames.Select(an.GetFloat).ToArray());
                    }
                }
                // Biggest single-frame jump in the raw target vector.
                var maxJump = 0f; var jumpAt = -1;
                for (var t = 1; t < log.Count; t++)
                {
                    var d = 0f;
                    for (var i = 0; i < 15; i++) d += Mathf.Abs(log[t][i] - log[t - 1][i]);
                    if (d > maxJump) { maxJump = d; jumpAt = t; }
                }
                var steps = new List<float>();
                for (var t = 1; t < log.Count; t++)
                {
                    var d = 0f;
                    for (var i = 0; i < 15; i++) d += Mathf.Abs(log[t][i] - log[t - 1][i]);
                    steps.Add(d);
                }
                steps.Sort();
                report.Append($" || rawStep median={steps[steps.Count / 2]:F4}" +
                    $" p90={steps[(int)(steps.Count * 0.9f)]:F4} max={maxJump:F4} at f{jumpAt}");
                report.Append(" || sumsPerFrame=" + string.Join(",",
                    log.Take(12).Select(v => v.Sum().ToString("F3"))));
                Assert.Fail("PROBE RESULT: " + report);
            }
            finally
            {
                if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void ProbeBuiltRenormalizerTrees()
        {
            var folderName = "__YUCP_NormProbe_" +
                             System.Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            var root = new GameObject("Norm Probe Build");
            try
            {
                var component =
                    root.AddComponent<AdvancedVisemeReconstructorData>();
                component.mouthOwnership =
                    AdvancedVisemeMouthOwnership.OutputsOnly;
                component.reconstructionMode =
                    AdvancedVisemeReconstructionMode.Normal;
                component.trackingInputs =
                    AdvancedVisemeTrackingInputs.Disabled;
                component.createTuningMenu = false;
                var profile =
                    VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
                var result = AdvancedVisemeAnimatorBuilder.Build(
                    new AdvancedVisemeAnimatorBuilder.Request
                    {
                        controllerPath = folder + "/AdvancedViseme.controller",
                        parametersPath = folder + "/Parameters.asset",
                        component = component,
                        profile = profile,
                        trackingPrefix = "YUCP/TestTracking",
                        effectiveTrackingInputs =
                            AdvancedVisemeTrackingInputs.Disabled,
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
                            AdvancedVisemeArticulator,
                            AdvancedVisemeExternalPose>(),
                        trackingEnabled = false,
                        existingExpressionParameters = new HashSet<string>()
                    });
                var everything = AssetDatabase
                    .LoadAllAssetsAtPath(
                        AssetDatabase.GetAssetPath(result.controller))
                    .OfType<BlendTree>()
                    .ToArray();
                var trees = everything
                    .Where(tree => tree.name.Contains("renormalizer"))
                    .ToArray();
                var report = new StringBuilder();
                var styledParams = result.controller.parameters
                    .Count(parameter => parameter.name.Contains("StyledNorm"));
                var scaledParams = result.controller.parameters
                    .Count(parameter => parameter.name.Contains("/Scaled/"));
                var normalizedTrees = everything.Count(tree =>
                {
                    var s = new SerializedObject(tree);
                    var n = s.FindProperty("m_NormalizedBlendValues");
                    return n != null && n.boolValue;
                });
                report.Append(
                    $"totalTrees={everything.Length} " +
                    $"styledNormParams={styledParams} " +
                    $"scaledParams={scaledParams} " +
                    $"normalizedFlagTrees={normalizedTrees} " +
                    $"removedParams={result.optimizerReport.removedInternalParameters} ");
                report.Append($"renormalizer trees: {trees.Length}");
                var basisClips = AssetDatabase
                    .LoadAllAssetsAtPath(
                        AssetDatabase.GetAssetPath(result.controller))
                    .OfType<AnimationClip>()
                    .Count(clip => clip.name.Contains("basis"));
                report.Append($" basisClips={basisClips}");
                var seen = new HashSet<Motion>();
                var stack = new Stack<Motion>();
                foreach (var layer in result.controller.layers)
                foreach (var childState in layer.stateMachine.states)
                    if (childState.state.motion != null)
                        stack.Push(childState.state.motion);
                var memoryNormalized = 0;
                var memoryNamed = 0;
                while (stack.Count > 0)
                {
                    var motion = stack.Pop();
                    if (!(motion is BlendTree tree2) || !seen.Add(tree2))
                        continue;
                    if (tree2.name.Contains("renormalizer")) memoryNamed++;
                    var s2 = new SerializedObject(tree2);
                    var n2 = s2.FindProperty("m_NormalizedBlendValues");
                    if (n2 != null && n2.boolValue) memoryNormalized++;
                    foreach (var c2 in tree2.children)
                        if (c2.motion != null) stack.Push(c2.motion);
                }
                report.Append(
                    $" memNormalized={memoryNormalized} memNamed={memoryNamed}");
                foreach (var tree in trees)
                {
                    var serialized = new SerializedObject(tree);
                    var normalized = serialized
                        .FindProperty("m_NormalizedBlendValues");
                    report.Append(
                        $" || {tree.name}: children={tree.children.Length}" +
                        $" normalized={normalized?.boolValue}" +
                        $" firstWeight={tree.children.FirstOrDefault().directBlendParameter}");
                    var clip = tree.children.FirstOrDefault().motion
                        as AnimationClip;
                    if (clip != null)
                    {
                        var bindings =
                            AnimationUtility.GetCurveBindings(clip);
                        report.Append($" clip0curves={bindings.Length}");
                    }
                }
                Assert.Fail("PROBE RESULT: " + report);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void ProbeStageSumsAtFifteenHz()
        {
            var folderName = "__YUCP_SumProbe_" +
                             System.Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            var root = new GameObject("Sum Probe Build");
            GameObject runtime = null;
            try
            {
                var component =
                    root.AddComponent<AdvancedVisemeReconstructorData>();
                component.mouthOwnership =
                    AdvancedVisemeMouthOwnership.OutputsOnly;
                component.reconstructionMode =
                    AdvancedVisemeReconstructionMode.Normal;
                component.trackingInputs =
                    AdvancedVisemeTrackingInputs.Disabled;
                component.createTuningMenu = false;
                var profile =
                    VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
                var result = AdvancedVisemeAnimatorBuilder.Build(
                    new AdvancedVisemeAnimatorBuilder.Request
                    {
                        controllerPath = folder + "/AdvancedViseme.controller",
                        parametersPath = folder + "/Parameters.asset",
                        component = component,
                        profile = profile,
                        trackingPrefix = "YUCP/TestTracking",
                        effectiveTrackingInputs =
                            AdvancedVisemeTrackingInputs.Disabled,
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
                            AdvancedVisemeArticulator,
                            AdvancedVisemeExternalPose>(),
                        trackingEnabled = false,
                        existingExpressionParameters = new HashSet<string>()
                    });

                runtime = new GameObject("Sum Probe Runtime");
                var animator = runtime.AddComponent<Animator>();
                animator.runtimeAnimatorController = result.controller;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.Update(0f);
                animator.SetFloat("IsLocal", 1f);
                animator.SetFloat("Voice", 1f);

                // Group every per-viseme parameter family by its stage name.
                var stages = new SortedDictionary<string, List<string>>(
                    StringComparer.Ordinal);
                var pattern = new System.Text.RegularExpressions.Regex(
                    @"/Viseme/(\d+)/([A-Za-z0-9]+)$");
                foreach (var parameter in animator.parameters)
                {
                    var match = pattern.Match(parameter.name);
                    if (!match.Success) continue;
                    var stage = match.Groups[2].Value;
                    if (!stages.TryGetValue(stage, out var list))
                        stages[stage] = list = new List<string>();
                    list.Add(parameter.name);
                }

                var report = new StringBuilder();
                report.Append("stages: " + string.Join(",", stages
                    .Where(pair => pair.Value.Count == 15)
                    .Select(pair => pair.Key)));

                void StepFrames(int viseme, int frames)
                {
                    for (var frame = 0; frame < frames; frame++)
                    {
                        animator.SetInteger("Viseme", viseme);
                        animator.Update(1f / 15f);
                    }
                }

                void SampleFrame(string label)
                {
                    foreach (var pair in stages)
                    {
                        if (pair.Value.Count != 15) continue;
                        var sum = pair.Value.Sum(animator.GetFloat);
                        if (Mathf.Abs(sum - 1f) > 1.2e-4f)
                            report.Append(
                                $" | {label} {pair.Key}={sum:F6}");
                    }
                }

                StepFrames(10, 9);
                SampleFrame("hold");
                // Per-channel leak location at steady hold.
                if (stages.ContainsKey("Fast") && stages.ContainsKey("PullNorm"))
                {
                    var fast = stages["Fast"]
                        .OrderBy(n => n.Length).ThenBy(n => n).ToArray();
                    var norm = stages["PullNorm"]
                        .OrderBy(n => n.Length).ThenBy(n => n).ToArray();
                    for (var i = 0; i < 15; i++)
                    {
                        var d = animator.GetFloat(fast[i]) -
                                animator.GetFloat(norm[i]);
                        if (Mathf.Abs(d) > 3e-5f)
                            report.Append($" | hold ch{i} F-N={d:F6}" +
                                $" N={animator.GetFloat(norm[i]):F6}");
                    }
                    if (stages.ContainsKey("SparseFast"))
                        report.Append(" | hold sparseFastSum=" +
                            stages["SparseFast"].Sum(animator.GetFloat)
                                .ToString("F6"));
                }
                for (var step = 0; step < 6; step++)
                {
                    var viseme = step == 0 ? 11 : 10;
                    animator.SetInteger("Viseme", viseme);
                    animator.Update(1f / 15f);
                    SampleFrame($"f{step}");
                    if (step == 2 && stages.ContainsKey("Pulled4"))
                        report.Append(" | f2 pulled4=[" + string.Join(",",
                            stages["Pulled4"]
                                .OrderBy(n => n.Length).ThenBy(n => n)
                                .Select(n => animator.GetFloat(n)
                                    .ToString("F4"))) + "]");
                }
                // Writers of a leaking channel: every clip binding it.
                var controllerPath2 = AssetDatabase.GetAssetPath(
                    result.controller);
                foreach (var clip in AssetDatabase
                    .LoadAllAssetsAtPath(controllerPath2)
                    .OfType<AnimationClip>())
                {
                    foreach (var binding in
                        AnimationUtility.GetCurveBindings(clip))
                    {
                        if (!binding.propertyName.EndsWith("/Viseme/10/Fast"))
                            continue;
                        var curve = AnimationUtility.GetEditorCurve(
                            clip, binding);
                        report.Append(
                            $" | writer '{clip.name}' v={curve.keys[0].value:F4}" +
                            (curve.keys.Length > 1
                                ? $"..{curve.keys[curve.keys.Length - 1].value:F4}"
                                : ""));
                    }
                }
                Assert.Fail("PROBE RESULT: " + report);
            }
            finally
            {
                if (runtime != null)
                    UnityEngine.Object.DestroyImmediate(runtime);
                UnityEngine.Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void ProbeNormalizedDirectTreeSemantics()
        {
            var controller = new AnimatorController { name = "NormProbe" };
            controller.AddParameter("W0", AnimatorControllerParameterType.Float);
            controller.AddParameter("W1", AnimatorControllerParameterType.Float);
            controller.AddParameter("Out0", AnimatorControllerParameterType.Float);
            controller.AddParameter("Out1", AnimatorControllerParameterType.Float);
            controller.AddLayer("L");

            var clip0 = new AnimationClip();
            clip0.SetCurve("", typeof(Animator), "Out0", AnimationCurve.Constant(0f, 1f, 1f));
            clip0.SetCurve("", typeof(Animator), "Out1", AnimationCurve.Constant(0f, 1f, 0f));
            var clip1 = new AnimationClip();
            clip1.SetCurve("", typeof(Animator), "Out0", AnimationCurve.Constant(0f, 1f, 0f));
            clip1.SetCurve("", typeof(Animator), "Out1", AnimationCurve.Constant(0f, 1f, 1f));

            var tree = new BlendTree
            {
                name = "NormTree",
                blendType = BlendTreeType.Direct,
                children = new[]
                {
                    new ChildMotion { motion = clip0, directBlendParameter = "W0", timeScale = 1f },
                    new ChildMotion { motion = clip1, directBlendParameter = "W1", timeScale = 1f }
                }
            };
            var serialized = new SerializedObject(tree);
            var property = serialized.FindProperty("m_NormalizedBlendValues");
            var propState = property == null
                ? "PROPERTY NOT FOUND"
                : "prop found, was " + property.boolValue;
            if (property != null)
            {
                property.boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            // Nest the normalized tree under a non-normalized Direct root
            // driven by a constant-one weight, mirroring the math layer.
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "ONE",
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f
            });
            var root = new BlendTree
            {
                name = "Root",
                blendType = BlendTreeType.Direct,
                children = new[]
                {
                    new ChildMotion
                    {
                        motion = tree,
                        directBlendParameter = "ONE",
                        timeScale = 1f
                    }
                }
            };
            var state = controller.layers[0].stateMachine.AddState("S");
            state.motion = root;
            state.writeDefaultValues = true;

            var go = new GameObject("NormProbeGO");
            try
            {
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.Rebind();

                var results = new StringBuilder(propState);
                var cases = new[]
                {
                    new[] { 0.3f, 0.4f },
                    new[] { 0.8f, 0.8f },
                    new[] { 1.2f, 2.4f },
                    new[] { -0.5f, 0.8f }
                };
                foreach (var c in cases)
                {
                    animator.SetFloat("W0", c[0]);
                    animator.SetFloat("W1", c[1]);
                    animator.Update(1f / 60f);
                    animator.Update(1f / 60f);
                    results.Append(
                        $" | W=({c[0]},{c[1]}) -> ({animator.GetFloat("Out0"):F4},{animator.GetFloat("Out1"):F4})");
                }
                Assert.Fail("PROBE RESULT: " + results);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
