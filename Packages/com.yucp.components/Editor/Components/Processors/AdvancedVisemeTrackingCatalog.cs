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
        public AnimatorControllerParameterType? activeAnimatorType;
        public float activeAnimatorDefault;
        public bool quality;
        public bool fullTongue;
        public int coverage;
        public int controllerOnlyCoverage;
        public int poseCoverage;
        public readonly Dictionary<AdvancedVisemeArticulator, string> parameters =
            new Dictionary<AdvancedVisemeArticulator, string>();
        public readonly HashSet<AdvancedVisemeArticulator> directPoseArticulators =
            new HashSet<AdvancedVisemeArticulator>();
        public readonly Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose> poses =
            new Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose>();
        public readonly Dictionary<string, string> auxiliaryParameters =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public string Summary =>
            $"reused '{(string.IsNullOrEmpty(prefix) ? "v2" : prefix + "/v2")}' ({coverage} channels" +
            (poseCoverage > 0 ? $", {poseCoverage} rig-mapped" : string.Empty) +
            (controllerOnlyCoverage > 0 ? $", {controllerOnlyCoverage} proxy" : string.Empty) + ")";
    }

    internal sealed class AdvancedVisemeExternalPose
    {
        public AnimationClip positive;
        public AnimationClip negative;
        public float positiveThreshold = 1f;
        public float negativeThreshold = -1f;
    }

    internal sealed class AdvancedVisemeTrackingCatalog
    {
        private readonly struct PoseTreeCandidate
        {
            public readonly BlendTree tree;
            public readonly int structuralCost;

            public PoseTreeCandidate(BlendTree tree, int structuralCost)
            {
                this.tree = tree;
                this.structuralCost = structuralCost;
            }
        }

        internal sealed class Entry
        {
            public VRCExpressionParameters.Parameter expression;
            public bool expressionTypeConflict;
            public bool expressionMetadataConflict;
            public AnimatorControllerParameterType? animatorType;
            public float animatorDefault;
            public bool animatorTypeConflict;
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

        private static readonly AdvancedVisemeArticulator[] FullTongue =
        {
            AdvancedVisemeArticulator.TongueX,
            AdvancedVisemeArticulator.TongueRoll,
            AdvancedVisemeArticulator.TongueArchY,
            AdvancedVisemeArticulator.TongueShape,
            AdvancedVisemeArticulator.TongueTwistRight,
            AdvancedVisemeArticulator.TongueTwistLeft
        };

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly HashSet<UnityEngine.Object> visitedAssets = new HashSet<UnityEngine.Object>();
        private readonly HashSet<AnimatorController> controllers = new HashSet<AnimatorController>();

        public IReadOnlyDictionary<string, Entry> Entries => entries;

        public Dictionary<string, VRCExpressionParameters.Parameter> ExpressionParameters => entries
            .Where(pair => pair.Value.expression != null && !pair.Value.expressionTypeConflict)
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
                if (pair.Value.expressionTypeConflict ||
                    (pair.Value.expression != null &&
                     (pair.Value.expression.valueType !=
                          VRCExpressionParameters.ValueType.Float ||
                      !pair.Value.expression.networkSynced)) ||
                    pair.Value.animatorTypeConflict ||
                    pair.Value.animatorType != AnimatorControllerParameterType.Float) continue;
                if (!TrySplitV2(pair.Key, out var prefix, out var suffix)) continue;
                if (hasExplicitPrefix && !string.Equals(prefix, requestedPrefix, StringComparison.Ordinal)) continue;

                foreach (var articulator in Balanced.Concat(Quality).Concat(FullTongue))
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
                candidate.fullTongue = FullTongue.Count(candidate.parameters.ContainsKey) >= 2;
                AddAuxiliaryParameter(candidate, "SoftPalateClose");
                candidate.activeParameter = FindActiveParameter(
                    candidate.prefix,
                    out var activeAnimatorType,
                    out var activeAnimatorDefault);
                candidate.activeAnimatorType = activeAnimatorType;
                candidate.activeAnimatorDefault = activeAnimatorDefault;
                foreach (var pair in candidate.parameters)
                {
                    var pose = FindPose(pair.Value, candidate.activeParameter);
                    if (pose == null) continue;
                    candidate.poseCoverage++;
                    candidate.poses[pair.Key] = pose;

                    // A controller-only parameter that is structurally wired to
                    // the final authored pose is a decoded/template proxy, not a
                    // raw OSC wire. Re-filtering it duplicates the template's own
                    // local/remote handling and adds several Animator-frame
                    // dependencies before the face can move.
                    if (entries.TryGetValue(pair.Value, out var entry) &&
                        entry.expression == null)
                        candidate.directPoseArticulators.Add(pair.Key);
                }
            }

            var viable = candidates.Values.Where(candidate =>
                (Balanced.Count(candidate.parameters.ContainsKey) >= 4 ||
                 HasApertureBasis(candidate)) &&
                !string.IsNullOrEmpty(candidate.activeParameter)).ToArray();
            if (viable.Length == 0)
            {
                if (candidates.Count > 0 || hasExplicitPrefix)
                {
                    error = hasExplicitPrefix
                        ? $"No compatible decoded Unified Expressions source with a tracking-active signal was found under '{requestedPrefix}/v2'."
                        : "Compatible Unified Expressions channels were found, but none had an unambiguous tracking-active signal.";
                }
                return null;
            }

            var bestScore = viable.Max(Score);
            var best = viable.Where(candidate => Score(candidate) == bestScore).ToArray();
            if (best.Length != 1)
            {
                error = "Multiple compatible VRCFaceTracking sources have equal channel and rig-pose coverage. Select Existing Prefix explicitly.";
                return null;
            }
            return best[0];
        }

        private static bool HasApertureBasis(AdvancedVisemeTrackingResolution candidate)
        {
            return candidate.parameters.ContainsKey(AdvancedVisemeArticulator.JawOpen) &&
                   candidate.parameters.ContainsKey(AdvancedVisemeArticulator.LipClose) &&
                   candidate.parameters.ContainsKey(AdvancedVisemeArticulator.MouthOpen);
        }

        private void AddAuxiliaryParameter(
            AdvancedVisemeTrackingResolution resolution,
            string suffix)
        {
            var matches = entries.Where(pair =>
                !pair.Value.expressionTypeConflict &&
                (pair.Value.expression == null ||
                 pair.Value.expression.valueType == VRCExpressionParameters.ValueType.Float &&
                 pair.Value.expression.networkSynced) &&
                !pair.Value.animatorTypeConflict &&
                pair.Value.animatorType == AnimatorControllerParameterType.Float &&
                TrySplitV2(pair.Key, out var prefix, out var candidateSuffix) &&
                string.Equals(prefix, resolution.prefix, StringComparison.Ordinal) &&
                string.Equals(candidateSuffix, suffix, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (matches.Length == 1) resolution.auxiliaryParameters[suffix] = matches[0];
        }

        public bool HasAnimatorFloat(string name)
        {
            return !string.IsNullOrEmpty(name) && entries.TryGetValue(name, out var entry) &&
                   !entry.animatorTypeConflict &&
                   entry.animatorType == AnimatorControllerParameterType.Float;
        }

        public Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose> ExtractPoses(
            AdvancedVisemeTrackingResolution resolution)
        {
            var output = new Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose>();
            if (resolution == null) return output;
            if (resolution.poses.Count > 0)
            {
                foreach (var pair in resolution.poses) output[pair.Key] = pair.Value;
                return output;
            }
            foreach (var pair in resolution.parameters)
            {
                var pose = FindPose(pair.Value, resolution.activeParameter);
                if (pose != null && (pose.positive != null || pose.negative != null)) output[pair.Key] = pose;
            }
            return output;
        }

        private static int Score(AdvancedVisemeTrackingResolution candidate)
        {
            // A source that is structurally connected to the avatar's final rig is
            // more useful than an equally complete raw or smoothed bus. This is a
            // topology test, not a prefix-name preference, so tailored templates
            // and future VRCFT layouts resolve the same way.
            return candidate.poseCoverage * 100000 + candidate.coverage * 100 +
                   candidate.controllerOnlyCoverage;
        }

        private string FindActiveParameter(
            string sourcePrefix,
            out AnimatorControllerParameterType? animatorType,
            out float animatorDefault)
        {
            animatorType = null;
            animatorDefault = 0f;
            var normalizedPrefix = (sourcePrefix ?? string.Empty).Trim().Trim('/');
            var candidates = new List<(string name, Entry entry, int score)>();
            foreach (var pair in entries)
            {
                var expression = pair.Value.expression;
                if (pair.Value.expressionTypeConflict || pair.Value.animatorTypeConflict ||
                    expression == null ||
                    expression.valueType != VRCExpressionParameters.ValueType.Bool ||
                    !expression.networkSynced) continue;
                if (pair.Value.animatorType.HasValue &&
                    pair.Value.animatorType != AnimatorControllerParameterType.Float &&
                    pair.Value.animatorType != AnimatorControllerParameterType.Bool)
                    continue;

                var isExpression = HasSuffix(pair.Key, "ExpressionTrackingActive");
                var isLip = HasSuffix(pair.Key, "LipTrackingActive");
                if (!isExpression && !isLip) continue;

                var expectedPrefix = string.IsNullOrEmpty(normalizedPrefix)
                    ? string.Empty
                    : normalizedPrefix + "/";
                var exactForSource = string.Equals(
                    pair.Key,
                    expectedPrefix + (isExpression ? "ExpressionTrackingActive" : "LipTrackingActive"),
                    StringComparison.Ordinal);
                var root = pair.Key.IndexOf('/') < 0;
                // VRCFT accepts suffix-compatible active parameters, but pairing a
                // source with another installation's prefixed gate can make a
                // perfectly valid tailored template appear permanently disabled.
                // Only the source's own gate or the documented root aliases are
                // unambiguous enough to reuse automatically.
                if (!exactForSource && !root) continue;
                var score = exactForSource ? 300 : 200;
                if (isExpression) score += 1; // Modern alias when both equivalent declarations exist.
                if (pair.Value.animatorType.HasValue) score += 10;
                candidates.Add((pair.Key, pair.Value, score));
            }

            if (candidates.Count == 0) return null;
            var bestScore = candidates.Max(candidate => candidate.score);
            var best = candidates.Where(candidate => candidate.score == bestScore).ToArray();
            if (best.Length != 1) return null;

            animatorType = best[0].entry.animatorType;
            animatorDefault = best[0].entry.animatorType.HasValue
                ? best[0].entry.animatorDefault
                : Mathf.Clamp01(best[0].entry.expression.defaultValue);
            return best[0].name;
        }

        private static bool HasSuffix(string parameter, string suffix)
        {
            return string.Equals(parameter, suffix, StringComparison.Ordinal) ||
                   parameter.EndsWith("/" + suffix, StringComparison.Ordinal);
        }

        private AdvancedVisemeExternalPose FindPose(string parameter, string activeParameter)
        {
            AdvancedVisemeExternalPose best = null;
            var bestScore = int.MinValue;
            string bestSignature = null;
            var ambiguous = false;
            foreach (var controller in controllers)
            {
                foreach (var reachable in EnumerateBlendTrees(controller, activeParameter))
                {
                    var candidate = PoseFromTree(reachable.tree, parameter);
                    if (candidate == null) continue;
                    var directions = (candidate.positive != null ? 1 : 0) +
                                     (candidate.negative != null ? 1 : 0);
                    var score = directions * 10000 - reachable.structuralCost * 100 +
                                PoseNameAffinity(parameter, candidate);
                    var signature = PoseSignature(candidate);
                    if (score > bestScore)
                    {
                        best = candidate;
                        bestScore = score;
                        bestSignature = signature;
                        ambiguous = false;
                    }
                    else if (score == bestScore &&
                             !string.Equals(signature, bestSignature, StringComparison.Ordinal))
                    {
                        ambiguous = true;
                    }
                }
            }
            return ambiguous ? null : best;
        }

        private static int PoseNameAffinity(string parameter, AdvancedVisemeExternalPose pose)
        {
            var suffix = parameter?.Split('/').LastOrDefault();
            var normalizedSuffix = NormalizeName(suffix);
            if (string.IsNullOrEmpty(normalizedSuffix)) return 0;
            var best = 0;
            foreach (var clip in new[] { pose.positive, pose.negative })
            {
                if (clip == null) continue;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var shape = binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)
                        ? binding.propertyName.Substring("blendShape.".Length)
                        : binding.propertyName;
                    var normalizedShape = NormalizeName(shape);
                    if (string.Equals(normalizedShape, normalizedSuffix, StringComparison.Ordinal))
                        best = Mathf.Max(best, 20);
                    else if (normalizedShape.Contains(normalizedSuffix) ||
                             normalizedSuffix.Contains(normalizedShape))
                        best = Mathf.Max(best, 10);
                }
            }
            return best;
        }

        private static string NormalizeName(string value)
        {
            return new string((value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private static string PoseSignature(AdvancedVisemeExternalPose pose)
        {
            string ClipSignature(AnimationClip clip)
            {
                if (clip == null) return "-";
                return string.Join(";", AnimationUtility.GetCurveBindings(clip)
                    .OrderBy(CurveBindingKey, StringComparer.Ordinal)
                    .Select(binding =>
                    {
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        var value = curve != null && curve.length > 0 ? curve.keys[0].value : 0f;
                        return CurveBindingKey(binding) + "=" + value.ToString("R");
                    }));
            }

            return ClipSignature(pose.positive) + "|" + ClipSignature(pose.negative) + "|" +
                   pose.positiveThreshold.ToString("R") + "|" + pose.negativeThreshold.ToString("R");
        }

        internal static AdvancedVisemeExternalPose PoseFromTree(BlendTree tree, string parameter)
        {
            if (tree == null) return null;
            switch (tree.blendType)
            {
                case BlendTreeType.Simple1D when tree.blendParameter == parameter:
                    return PoseFromUnitOneDimensionalTree(tree);
                case BlendTreeType.Direct:
                    return PoseFromDirectTree(tree, parameter);
                default:
                    // 2D and nested/arbitrary trees couple the requested parameter
                    // to other coordinates. Sampling a convenient child is not an
                    // invertible unit parameter-to-pose mapping.
                    return null;
            }
        }

        private static AdvancedVisemeExternalPose PoseFromUnitOneDimensionalTree(BlendTree tree)
        {
            var children = tree.children;
            if (children == null || children.Length == 0) return null;
            ChildMotion? neutral = null;
            ChildMotion? positive = null;
            ChildMotion? negative = null;
            foreach (var child in children)
            {
                if (Mathf.Approximately(child.threshold, 0f))
                {
                    if (neutral.HasValue) return null;
                    neutral = child;
                }
                else if (child.threshold > 0f && IsFinite(child.threshold))
                {
                    if (positive.HasValue) return null;
                    positive = child;
                }
                else if (child.threshold < 0f && IsFinite(child.threshold))
                {
                    if (negative.HasValue) return null;
                    negative = child;
                }
                else
                {
                    // NaN/Infinity and duplicate zero thresholds are not an
                    // invertible parameter-to-pose mapping.
                    return null;
                }
            }

            if (!neutral.HasValue || (!positive.HasValue && !negative.HasValue) ||
                !IsStaticZeroBlendshapeMotion(neutral.Value.motion))
                return null;

            AnimationClip positiveClip = null;
            if (positive.HasValue &&
                !TryGetStaticBlendshapePose(positive.Value.motion, out positiveClip))
                return null;
            AnimationClip negativeClip = null;
            if (negative.HasValue &&
                !TryGetStaticBlendshapePose(negative.Value.motion, out negativeClip))
                return null;

            return new AdvancedVisemeExternalPose
            {
                positive = positiveClip,
                negative = negativeClip,
                positiveThreshold = positive?.threshold ?? -negative.Value.threshold,
                negativeThreshold = negative?.threshold ?? -positive.Value.threshold
            };
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static AdvancedVisemeExternalPose PoseFromDirectTree(BlendTree tree, string parameter)
        {
            if (DirectTreeNormalizesBlendValues(tree)) return null;
            var children = tree.children;
            if (children == null || children.Length == 0) return null;
            var matching = children.Where(child =>
                string.Equals(child.directBlendParameter, parameter, StringComparison.Ordinal)).ToArray();
            if (matching.Length != 1) return null;

            // A Direct tree is a separable linear sum only when every child is a
            // static blendshape pose (or an empty safety clip). Rejecting the
            // entire tree prevents a bone/material or nested nonlinear sibling
            // from being silently ignored while taking ownership of the mouth.
            foreach (var child in children)
            {
                if (child.motion == null) continue;
                if (!(child.motion is AnimationClip clip) ||
                    !IsStaticBlendshapeClip(clip, allowEmpty: true, requireZero: false))
                    return null;
            }

            if (!TryGetStaticBlendshapePose(matching[0].motion, out var positive)) return null;
            var ownedBindings = new HashSet<string>(AnimationUtility.GetCurveBindings(positive)
                .Select(CurveBindingKey), StringComparer.Ordinal);
            foreach (var sibling in children)
            {
                if (string.Equals(sibling.directBlendParameter, parameter, StringComparison.Ordinal) ||
                    !(sibling.motion is AnimationClip siblingClip))
                    continue;
                if (AnimationUtility.GetCurveBindings(siblingClip)
                    .Select(CurveBindingKey).Any(ownedBindings.Contains))
                    return null;
            }
            return new AdvancedVisemeExternalPose { positive = positive };
        }

        private static string CurveBindingKey(EditorCurveBinding binding)
        {
            return binding.path + "\u001f" + binding.type.AssemblyQualifiedName + "\u001f" + binding.propertyName;
        }

        internal static bool DirectTreeNormalizesBlendValues(BlendTree tree)
        {
            if (tree == null) return false;
            // Unity 2022 serializes this Direct BlendTree option but does not
            // expose it through the public BlendTree API.
            var serialized = new SerializedObject(tree);
            var property = serialized.FindProperty("m_NormalizedBlendValues");
            return property != null && property.boolValue;
        }

        private static bool TryGetStaticBlendshapePose(Motion motion, out AnimationClip clip)
        {
            clip = motion as AnimationClip;
            return clip != null && IsStaticBlendshapeClip(clip, allowEmpty: false, requireZero: false);
        }

        private static bool IsStaticZeroBlendshapeMotion(Motion motion)
        {
            if (motion == null) return true;
            return motion is AnimationClip clip &&
                   IsStaticBlendshapeClip(clip, allowEmpty: true, requireZero: true);
        }

        private static bool IsStaticBlendshapeClip(
            AnimationClip clip,
            bool allowEmpty,
            bool requireZero)
        {
            if (clip == null || AnimationUtility.GetObjectReferenceCurveBindings(clip).Length != 0)
                return false;
            var bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings.Length == 0) return allowEmpty;
            foreach (var binding in bindings)
            {
                if (binding.type != typeof(SkinnedMeshRenderer) ||
                    !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                    return false;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (!IsStaticCurve(curve, requireZero)) return false;
            }
            return true;
        }

        private static bool IsStaticCurve(AnimationCurve curve, bool requireZero)
        {
            if (curve == null || curve.length == 0) return false;
            var keys = curve.keys;
            var value = keys[0].value;
            if (float.IsNaN(value) || float.IsInfinity(value) ||
                requireZero && !Mathf.Approximately(value, 0f))
                return false;
            foreach (var key in keys)
            {
                if (float.IsNaN(key.value) || float.IsInfinity(key.value) ||
                    !Mathf.Approximately(key.value, value))
                    return false;
                if (keys.Length > 1 &&
                    (!IsConstantTangent(key.inTangent) || !IsConstantTangent(key.outTangent)))
                    return false;
            }
            return true;
        }

        private static bool IsConstantTangent(float tangent)
        {
            return float.IsInfinity(tangent) || Mathf.Approximately(tangent, 0f);
        }

        private IEnumerable<PoseTreeCandidate> EnumerateBlendTrees(
            AnimatorController controller,
            string activeParameter)
        {
            var seen = new Dictionary<BlendTree, int>();
            foreach (var layer in controller.layers)
            foreach (var tree in EnumerateBlendTrees(layer.stateMachine, seen, activeParameter))
                yield return tree;
        }

        private IEnumerable<PoseTreeCandidate> EnumerateBlendTrees(
            AnimatorStateMachine stateMachine,
            Dictionary<BlendTree, int> seen,
            string activeParameter)
        {
            foreach (var state in stateMachine.states)
            {
                if (!(state.state.motion is BlendTree tree)) continue;
                foreach (var reachable in EnumerateBlendTrees(tree, seen, activeParameter, 0))
                    yield return reachable;
            }
            foreach (var child in stateMachine.stateMachines)
            foreach (var tree in EnumerateBlendTrees(child.stateMachine, seen, activeParameter))
                yield return tree;
        }

        private IEnumerable<PoseTreeCandidate> EnumerateBlendTrees(
            BlendTree tree,
            Dictionary<BlendTree, int> seen,
            string activeParameter,
            int structuralCost)
        {
            if (tree == null || seen.TryGetValue(tree, out var previousCost) && previousCost <= structuralCost)
                yield break;
            seen[tree] = structuralCost;
            yield return new PoseTreeCandidate(tree, structuralCost);

            foreach (var child in tree.children)
            {
                if (!(child.motion is BlendTree nested)) continue;
                var factorable = tree.blendType == BlendTreeType.Direct &&
                                 !DirectTreeNormalizesBlendValues(tree) &&
                                 IsSafeAncestorWeight(child.directBlendParameter, activeParameter);
                // A non-factorable parent is a selector or coupled helper. Its
                // static child can still be an exact rig basis, but rank it below
                // mappings reached through only identity/activity gates. Requiring
                // PoseFromTree itself to be static and separable prevents animator,
                // material, bone, and nonlinear motions from leaking through.
                var childCost = structuralCost + (factorable ? 0 : 1);
                foreach (var reachable in EnumerateBlendTrees(
                             nested, seen, activeParameter, childCost))
                    yield return reachable;
            }
        }

        private bool IsSafeAncestorWeight(string parameter, string activeParameter)
        {
            if (!string.IsNullOrEmpty(activeParameter) &&
                string.Equals(parameter, activeParameter, StringComparison.Ordinal))
                return true;

            // Some controller generators use a private, unwired Float initialized
            // to one as their Direct-tree identity weight. It has no network source
            // and therefore cannot be another face channel.
            return !string.IsNullOrEmpty(parameter) &&
                   entries.TryGetValue(parameter, out var entry) &&
                   entry.expression == null &&
                   !entry.animatorTypeConflict &&
                   entry.animatorType == AnimatorControllerParameterType.Float &&
                   Mathf.Approximately(entry.animatorDefault, 1f);
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
                case AdvancedVisemeArticulator.TongueX: yield return "TongueX"; break;
                case AdvancedVisemeArticulator.TongueRoll: yield return "TongueRoll"; break;
                case AdvancedVisemeArticulator.TongueArchY:
                    yield return "TongueArchY";
                    yield return "TongueBend";
                    break;
                case AdvancedVisemeArticulator.TongueShape: yield return "TongueShape"; break;
                case AdvancedVisemeArticulator.TongueTwistRight: yield return "TongueTwistRight"; break;
                case AdvancedVisemeArticulator.TongueTwistLeft: yield return "TongueTwistLeft"; break;
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
            if (asset == null) return;
            switch (asset)
            {
                case VRCExpressionParameters parameters:
                    if (!visitedAssets.Add(parameters)) return;
                    if (parameters.parameters == null) return;
                    foreach (var parameter in parameters.parameters)
                    {
                        if (parameter == null || string.IsNullOrEmpty(parameter.name)) continue;
                        var entry = GetOrCreateEntry(parameter.name);
                        if (entry.expression != null && entry.expression.valueType != parameter.valueType)
                        {
                            entry.expressionTypeConflict = true;
                            continue;
                        }
                        if (entry.expression != null &&
                            (entry.expression.saved != parameter.saved ||
                             entry.expression.networkSynced != parameter.networkSynced))
                            entry.expressionMetadataConflict = true;
                        if (!entry.expressionTypeConflict) entry.expression = parameter;
                    }
                    return;
                case AnimatorController controller:
                    if (!visitedAssets.Add(controller)) return;
                    controllers.Add(controller);
                    foreach (var parameter in controller.parameters)
                    {
                        var entry = GetOrCreateEntry(parameter.name);
                        if (entry.animatorType.HasValue && entry.animatorType != parameter.type)
                        {
                            entry.animatorTypeConflict = true;
                            continue;
                        }
                        if (entry.animatorTypeConflict) continue;
                        entry.animatorType = parameter.type;
                        entry.animatorDefault = parameter.type == AnimatorControllerParameterType.Bool
                            ? (parameter.defaultBool ? 1f : 0f)
                            : parameter.type == AnimatorControllerParameterType.Int
                                ? parameter.defaultInt
                                : parameter.defaultFloat;
                    }
                    return;
                case AnimatorOverrideController overrides:
                    if (!visitedAssets.Add(overrides)) return;
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
