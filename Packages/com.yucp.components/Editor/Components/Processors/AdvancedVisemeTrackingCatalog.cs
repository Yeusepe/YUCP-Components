using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace YUCP.Components.Editor
{
    internal sealed class AdvancedVisemeTrackingResolution
    {
        public string prefix;
        public string activeParameter;
        public bool quality;
        public int coverage;
        public int controllerOnlyCoverage;
        public readonly Dictionary<AdvancedVisemeArticulator, string> parameters =
            new Dictionary<AdvancedVisemeArticulator, string>();

        public string Summary =>
            $"reused '{(string.IsNullOrEmpty(prefix) ? "v2" : prefix + "/v2")}' ({coverage} channels" +
            (controllerOnlyCoverage > 0 ? $", {controllerOnlyCoverage} proxy" : string.Empty) + ")";
    }

    internal sealed class AdvancedVisemeExternalPose
    {
        public AnimationClip positive;
        public AnimationClip negative;
    }

    internal sealed class AdvancedVisemeTrackingCatalog
    {
        internal sealed class Entry
        {
            public VRCExpressionParameters.Parameter expression;
            public AnimatorControllerParameterType? animatorType;
        }

        private static readonly AdvancedVisemeArticulator[] Balanced =
        {
            AdvancedVisemeArticulator.JawOpen, AdvancedVisemeArticulator.LipClose,
            AdvancedVisemeArticulator.MouthOpen, AdvancedVisemeArticulator.LipFunnel,
            AdvancedVisemeArticulator.LipPucker, AdvancedVisemeArticulator.LipSuck,
            AdvancedVisemeArticulator.SmileSad, AdvancedVisemeArticulator.TongueOut
        };

        private static readonly AdvancedVisemeArticulator[] Quality =
        {
            AdvancedVisemeArticulator.JawX, AdvancedVisemeArticulator.JawZ,
            AdvancedVisemeArticulator.MouthX, AdvancedVisemeArticulator.TongueY
        };

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly HashSet<UnityEngine.Object> visitedAssets = new HashSet<UnityEngine.Object>();
        private readonly HashSet<AnimatorController> controllers = new HashSet<AnimatorController>();

        public IReadOnlyDictionary<string, Entry> Entries => entries;

        public Dictionary<string, VRCExpressionParameters.Parameter> ExpressionParameters => entries
            .Where(pair => pair.Value.expression != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.expression, StringComparer.Ordinal);

        public string DependencyFingerprint
        {
            get
            {
                var dependencies = visitedAssets.Select(AssetDatabase.GetAssetPath)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Select(path => path + "=" + AssetDatabase.GetAssetDependencyHash(path));
                return Hash128.Compute(string.Join("|", dependencies)).ToString();
            }
        }

        public static AdvancedVisemeTrackingCatalog Scan(GameObject avatarRoot, VRCAvatarDescriptor descriptor)
        {
            var catalog = new AdvancedVisemeTrackingCatalog();
            catalog.Visit(descriptor?.expressionParameters);
            if (descriptor != null)
            {
                if (descriptor.baseAnimationLayers != null)
                    foreach (var layer in descriptor.baseAnimationLayers) catalog.Visit(layer.animatorController);
                if (descriptor.specialAnimationLayers != null)
                    foreach (var layer in descriptor.specialAnimationLayers) catalog.Visit(layer.animatorController);
            }

            foreach (var animator in avatarRoot.GetComponentsInChildren<Animator>(true))
                catalog.Visit(animator.runtimeAnimatorController);

            foreach (var component in avatarRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform || component is AdvancedVisemeReconstructorData) continue;
                try
                {
                    var serialized = new SerializedObject(component);
                    var property = serialized.GetIterator();
                    if (!property.Next(true)) continue;
                    do
                    {
                        if (property.propertyType == SerializedPropertyType.ObjectReference)
                            catalog.Visit(property.objectReferenceValue);
                    } while (property.Next(true));
                }
                catch
                {
                    // Third-party components may have custom serialization that cannot be traversed.
                }
            }
            return catalog;
        }

        public AdvancedVisemeTrackingResolution Resolve(
            VisemeReconstructionProfile profile,
            string explicitPrefix,
            out string error)
        {
            error = null;
            var requestedPrefix = (explicitPrefix ?? string.Empty).Trim().Trim('/');
            var hasExplicitPrefix = !string.IsNullOrEmpty(requestedPrefix);
            var candidates = new Dictionary<string, AdvancedVisemeTrackingResolution>(StringComparer.Ordinal);

            foreach (var pair in entries)
            {
                if (pair.Value.animatorType != AnimatorControllerParameterType.Float) continue;
                if (!TrySplitV2(pair.Key, out var prefix, out var suffix)) continue;
                if (hasExplicitPrefix && !string.Equals(prefix, requestedPrefix, StringComparison.Ordinal)) continue;

                foreach (var articulator in Balanced.Concat(Quality))
                {
                    var binding = profile.FindBinding(articulator);
                    if (!SuffixAliases(articulator, binding?.trackingParameter).Contains(suffix, StringComparer.Ordinal)) continue;
                    if (!candidates.TryGetValue(prefix, out var candidate))
                    {
                        candidate = new AdvancedVisemeTrackingResolution { prefix = prefix };
                        candidates[prefix] = candidate;
                    }
                    if (candidate.parameters.ContainsKey(articulator)) continue;
                    candidate.parameters[articulator] = pair.Key;
                    candidate.coverage++;
                    if (pair.Value.expression == null) candidate.controllerOnlyCoverage++;
                }
            }

            foreach (var candidate in candidates.Values)
            {
                candidate.quality = Quality.Count(candidate.parameters.ContainsKey) >= 2;
                candidate.activeParameter = FindActiveParameter();
            }

            var viable = candidates.Values.Where(candidate =>
                Balanced.Count(candidate.parameters.ContainsKey) >= 4).ToArray();
            if (viable.Length == 0)
            {
                if (hasExplicitPrefix)
                    error = $"No compatible decoded Unified Expressions float channels were found under '{requestedPrefix}/v2'.";
                return null;
            }

            var bestScore = viable.Max(Score);
            var best = viable.Where(candidate => Score(candidate) == bestScore).ToArray();
            if (best.Length != 1)
            {
                error = "Multiple compatible VRCFaceTracking float/proxy prefixes have equal coverage. Select Existing Prefix explicitly.";
                return null;
            }
            return best[0];
        }

        public bool HasAnimatorFloat(string name)
        {
            return !string.IsNullOrEmpty(name) && entries.TryGetValue(name, out var entry) &&
                   entry.animatorType == AnimatorControllerParameterType.Float;
        }

        public Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose> ExtractPoses(
            AdvancedVisemeTrackingResolution resolution)
        {
            var output = new Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose>();
            if (resolution == null) return output;
            foreach (var pair in resolution.parameters)
            {
                var pose = FindPose(pair.Value);
                if (pose != null && (pose.positive != null || pose.negative != null)) output[pair.Key] = pose;
            }
            return output;
        }

        private static int Score(AdvancedVisemeTrackingResolution candidate)
        {
            return candidate.coverage * 100 + candidate.controllerOnlyCoverage * 20;
        }

        private string FindActiveParameter()
        {
            foreach (var name in new[] { "LipTrackingActive", "ExpressionTrackingActive" })
            {
                if (!entries.TryGetValue(name, out var entry)) continue;
                if (entry.animatorType == AnimatorControllerParameterType.Float ||
                    entry.animatorType == AnimatorControllerParameterType.Bool ||
                    entry.expression?.valueType == VRCExpressionParameters.ValueType.Bool)
                    return name;
            }
            return "LipTrackingActive";
        }

        private AdvancedVisemeExternalPose FindPose(string parameter)
        {
            AdvancedVisemeExternalPose best = null;
            var bestScore = 0;
            foreach (var controller in controllers)
            {
                foreach (var tree in EnumerateBlendTrees(controller))
                {
                    var candidate = PoseFromTree(tree, parameter);
                    if (candidate == null) continue;
                    var score = (candidate.positive != null ? 1 : 0) + (candidate.negative != null ? 1 : 0);
                    if (score <= bestScore) continue;
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        private static AdvancedVisemeExternalPose PoseFromTree(BlendTree tree, string parameter)
        {
            if (tree == null) return null;
            AnimationClip positive = null;
            AnimationClip negative = null;
            switch (tree.blendType)
            {
                case BlendTreeType.Simple1D when tree.blendParameter == parameter:
                {
                    var positiveChild = tree.children.Where(child => child.threshold > 0f)
                        .OrderByDescending(child => child.threshold).FirstOrDefault();
                    var negativeChild = tree.children.Where(child => child.threshold < 0f)
                        .OrderBy(child => child.threshold).FirstOrDefault();
                    positive = FirstBlendshapeClip(positiveChild.motion);
                    negative = FirstBlendshapeClip(negativeChild.motion);
                    break;
                }
                case BlendTreeType.Direct:
                    positive = tree.children
                        .Where(child => child.directBlendParameter == parameter)
                        .Select(child => FirstBlendshapeClip(child.motion))
                        .FirstOrDefault(clip => clip != null);
                    break;
                case BlendTreeType.FreeformCartesian2D:
                case BlendTreeType.SimpleDirectional2D:
                case BlendTreeType.FreeformDirectional2D:
                    if (tree.blendParameter == parameter)
                    {
                        positive = tree.children.Where(child => child.position.x > 0f)
                            .OrderByDescending(child => child.position.x)
                            .Select(child => FirstBlendshapeClip(child.motion)).FirstOrDefault(clip => clip != null);
                        negative = tree.children.Where(child => child.position.x < 0f)
                            .OrderBy(child => child.position.x)
                            .Select(child => FirstBlendshapeClip(child.motion)).FirstOrDefault(clip => clip != null);
                    }
                    else if (tree.blendParameterY == parameter)
                    {
                        positive = tree.children.Where(child => child.position.y > 0f)
                            .OrderByDescending(child => child.position.y)
                            .Select(child => FirstBlendshapeClip(child.motion)).FirstOrDefault(clip => clip != null);
                        negative = tree.children.Where(child => child.position.y < 0f)
                            .OrderBy(child => child.position.y)
                            .Select(child => FirstBlendshapeClip(child.motion)).FirstOrDefault(clip => clip != null);
                    }
                    break;
            }
            return positive == null && negative == null
                ? null
                : new AdvancedVisemeExternalPose { positive = positive, negative = negative };
        }

        private static AnimationClip FirstBlendshapeClip(Motion motion)
        {
            if (motion is AnimationClip clip)
            {
                return AnimationUtility.GetCurveBindings(clip).Any(binding =>
                    binding.type == typeof(SkinnedMeshRenderer) &&
                    binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                    ? clip
                    : null;
            }
            if (motion is BlendTree tree)
            {
                foreach (var child in tree.children)
                {
                    var nested = FirstBlendshapeClip(child.motion);
                    if (nested != null) return nested;
                }
            }
            return null;
        }

        private static IEnumerable<BlendTree> EnumerateBlendTrees(AnimatorController controller)
        {
            var seen = new HashSet<BlendTree>();
            foreach (var layer in controller.layers)
            foreach (var tree in EnumerateBlendTrees(layer.stateMachine, seen))
                yield return tree;
        }

        private static IEnumerable<BlendTree> EnumerateBlendTrees(
            AnimatorStateMachine stateMachine,
            HashSet<BlendTree> seen)
        {
            foreach (var state in stateMachine.states)
            foreach (var tree in EnumerateBlendTrees(state.state.motion, seen))
                yield return tree;
            foreach (var child in stateMachine.stateMachines)
            foreach (var tree in EnumerateBlendTrees(child.stateMachine, seen))
                yield return tree;
        }

        private static IEnumerable<BlendTree> EnumerateBlendTrees(Motion motion, HashSet<BlendTree> seen)
        {
            if (!(motion is BlendTree tree) || !seen.Add(tree)) yield break;
            yield return tree;
            foreach (var child in tree.children)
            foreach (var nested in EnumerateBlendTrees(child.motion, seen))
                yield return nested;
        }

        private static IEnumerable<string> SuffixAliases(AdvancedVisemeArticulator articulator, string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured)) yield return configured.Trim().Trim('/');
            switch (articulator)
            {
                case AdvancedVisemeArticulator.JawOpen: yield return "JawOpen"; break;
                case AdvancedVisemeArticulator.LipClose: yield return "MouthClosed"; break;
                case AdvancedVisemeArticulator.MouthOpen: yield return "MouthOpen"; break;
                case AdvancedVisemeArticulator.LipFunnel: yield return "LipFunnel"; break;
                case AdvancedVisemeArticulator.LipPucker: yield return "LipPucker"; break;
                case AdvancedVisemeArticulator.LipSuck:
                    yield return "LipSuck";
                    yield return "LipSuckLower";
                    yield return "LipSuckUpper";
                    break;
                case AdvancedVisemeArticulator.SmileSad:
                    yield return "SmileSad";
                    yield return "SmileFrown";
                    break;
                case AdvancedVisemeArticulator.TongueOut: yield return "TongueOut"; break;
                case AdvancedVisemeArticulator.JawX: yield return "JawX"; break;
                case AdvancedVisemeArticulator.JawZ:
                    yield return "JawZ";
                    yield return "JawForward";
                    break;
                case AdvancedVisemeArticulator.MouthX: yield return "MouthX"; break;
                case AdvancedVisemeArticulator.TongueY: yield return "TongueY"; break;
            }
        }

        private static bool TrySplitV2(string name, out string prefix, out string suffix)
        {
            prefix = null;
            suffix = null;
            if (string.IsNullOrEmpty(name)) return false;
            if (name.StartsWith("v2/", StringComparison.Ordinal))
            {
                prefix = string.Empty;
                suffix = name.Substring(3);
                return !string.IsNullOrEmpty(suffix);
            }
            var marker = name.LastIndexOf("/v2/", StringComparison.Ordinal);
            if (marker < 0) return false;
            prefix = name.Substring(0, marker);
            suffix = name.Substring(marker + 4);
            return !string.IsNullOrEmpty(suffix);
        }

        private void Visit(UnityEngine.Object asset)
        {
            if (asset == null || !visitedAssets.Add(asset)) return;
            switch (asset)
            {
                case VRCExpressionParameters parameters:
                    if (parameters.parameters == null) return;
                    foreach (var parameter in parameters.parameters)
                    {
                        if (parameter == null || string.IsNullOrEmpty(parameter.name)) continue;
                        GetOrCreateEntry(parameter.name).expression = parameter;
                    }
                    return;
                case AnimatorController controller:
                    controllers.Add(controller);
                    foreach (var parameter in controller.parameters)
                        GetOrCreateEntry(parameter.name).animatorType = parameter.type;
                    return;
                case AnimatorOverrideController overrides:
                    Visit(overrides.runtimeAnimatorController);
                    return;
            }
        }

        private Entry GetOrCreateEntry(string name)
        {
            if (!entries.TryGetValue(name, out var entry))
            {
                entry = new Entry();
                entries[name] = entry;
            }
            return entry;
        }
    }
}
