using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Performs closed-world dead-code elimination on AVR's private animated
    /// parameters after the graph has been lowered to ordinary Animator motions.
    ///
    /// A curve write is one Animator evaluation epoch. The optimizer therefore
    /// never composes or reorders writes: it only removes a private write when no
    /// public parameter, physical animation curve, transition, behaviour, or
    /// state-time control can observe it. This keeps the generated controller's
    /// feedback timing exactly unchanged.
    /// </summary>
    internal static class AdvancedVisemeAnimatorGraphOptimizer
    {
        internal const int Version = 4;

        // Test seam: structure-inspection tests assert properties of the
        // pre-interning lowering (duplicate-producing fixtures are sometimes
        // intentionally degenerate, e.g. single-vertex calibration meshes
        // whose normalized projections are bitwise identical). Interning
        // itself is covered by its own unit tests and by causal replay.
        internal static bool SkipCongruenceInterningForStructureTests;
        internal static bool DisableOperationLocalNeutralZeroEliminationForTests;

        internal sealed class Report
        {
            public int internalParametersBefore;
            public int internalParametersAfter;
            public int animatorCurvesBefore;
            public int animatorCurvesAfter;
            public int removedInternalParameters;
            public int removedAnimatorCurves;
            public int removedNeutralZeroCurves;
            public int removedDeadAnimatorCurves;
            public int internedCongruentParameters;
            public int removedCongruentCurves;
            public int liveInternalParameters;
            public int deadInternalParameters;
            internal readonly Dictionary<string, string>
                internedParameterMappings =
                    new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> removedCurvesByGroup =
                new Dictionary<string, int>(StringComparer.Ordinal);
        }

        private sealed class WriteSite
        {
            public AnimationClip clip;
            public EditorCurveBinding binding;
            public readonly HashSet<string> dependencies =
                new HashSet<string>(StringComparer.Ordinal);
        }

        private sealed class Analysis
        {
            public readonly Dictionary<string, List<WriteSite>> writers =
                new Dictionary<string, List<WriteSite>>(StringComparer.Ordinal);
            public readonly HashSet<string> roots =
                new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<AnimationClip> clips =
                new HashSet<AnimationClip>();
        }

        private sealed class CurveUse
        {
            public AnimationClip clip;
            public EditorCurveBinding binding;
            public bool safeBlendPath;
            public bool constantZero;
        }

        private sealed class ZeroCandidate
        {
            public AnimationClip clip;
            public EditorCurveBinding binding;
            public bool eligible = true;
            public int useCount;
        }

        internal static Report Optimize(
            AnimatorController controller,
            string internalPrefix,
            IEnumerable<string> publicParameters)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (string.IsNullOrWhiteSpace(internalPrefix))
                throw new ArgumentException("An AVR internal prefix is required.",
                    nameof(internalPrefix));

            internalPrefix = internalPrefix.TrimEnd('/') + "/";
            var parameterNames = controller.parameters
                .Select(parameter => parameter.name)
                .ToHashSet(StringComparer.Ordinal);
            var analysisBefore = Analyze(controller, parameterNames, internalPrefix);
            var internalParameters = controller.parameters
                .Where(parameter => parameter.name.StartsWith(
                    internalPrefix, StringComparison.Ordinal))
                .Select(parameter => parameter.name)
                .ToHashSet(StringComparer.Ordinal);
            var report = new Report
            {
                internalParametersBefore = internalParameters.Count,
                animatorCurvesBefore = CountAnimatorCurves(analysisBefore.clips)
            };

            // Unity evaluates an unbound Animator float as the neutral zero of a
            // Simple1D or non-normalized Direct blend. The specialized retention
            // proof is model-certified; the generic proof additionally requires
            // a private +0-default Float and a nonzero direct-clip sibling at the
            // same BlendTree site. Children and threshold knots stay untouched,
            // so no reachable feedback epoch or selector geometry changes.
            EliminateNeutralZeroBindings(
                controller, internalPrefix, report);

            // Merge private parameters that provably carry the same value on
            // every frame. This runs after neutral-zero removal so write sets
            // are normalized before congruence is decided, and before liveness
            // so that a merged duplicate's parameter is collected as dead.
            if (!SkipCongruenceInterningForStructureTests)
                InternCongruentParameters(controller, internalPrefix, report);

            // Re-analyze after neutral-zero removal. Otherwise a zero-valued
            // binder would falsely make its Direct weight look like a live data
            // dependency and prevent the subsequent closed-world DCE.
            var analysis = Analyze(controller, parameterNames, internalPrefix);
            if (publicParameters != null)
            {
                foreach (var parameter in publicParameters.Where(
                             parameter => !string.IsNullOrWhiteSpace(parameter)))
                    analysis.roots.Add(parameter);
            }

            // Every non-private Animator output remains a root. It may be read
            // later by Blendshape Link, OSC, or another merged controller even
            // when the current controller has no local reader for it.
            foreach (var output in analysis.writers.Keys)
            {
                if (!output.StartsWith(internalPrefix, StringComparison.Ordinal))
                    analysis.roots.Add(output);
            }

            // An authored/external clip is outside this generated asset's
            // ownership. Conservatively retain any private output it writes.
            var controllerPath = AssetDatabase.GetAssetPath(controller);
            foreach (var pair in analysis.writers)
            foreach (var site in pair.Value)
            {
                var clipPath = AssetDatabase.GetAssetPath(site.clip);
                if (!string.IsNullOrEmpty(clipPath) &&
                    !string.Equals(clipPath, controllerPath,
                        StringComparison.OrdinalIgnoreCase))
                    analysis.roots.Add(pair.Key);
            }

            var live = BackwardLiveParameters(analysis);
            var dead = internalParameters
                .Where(parameter => !live.Contains(parameter))
                .ToHashSet(StringComparer.Ordinal);
            report.liveInternalParameters = internalParameters.Count - dead.Count;
            report.deadInternalParameters = dead.Count;

            if (dead.Count != 0)
            {
                foreach (var clip in analysis.clips)
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (!IsAnimatorParameter(binding) ||
                            !dead.Contains(binding.propertyName)) continue;
                        AnimationUtility.SetEditorCurve(clip, binding, null);
                        report.removedDeadAnimatorCurves++;
                        RecordCurveRemoval(
                            report, binding.propertyName, internalPrefix);
                    }
                }

                var retainedParameters = controller.parameters
                    .Where(parameter => !dead.Contains(parameter.name))
                    .ToArray();
                report.removedInternalParameters =
                    controller.parameters.Length - retainedParameters.Length;
                controller.parameters = retainedParameters;
            }

            report.internalParametersAfter = controller.parameters.Count(
                parameter => parameter.name.StartsWith(
                    internalPrefix, StringComparison.Ordinal));
            report.animatorCurvesAfter = CountAnimatorCurves(analysis.clips);
            return report;
        }

        private sealed class CongruenceSite
        {
            public AnimationClip clip;
            public EditorCurveBinding binding;
            // Alternating literal/parameter tokens describing the layer, state,
            // full blend-tree context chain, clip timing, and curve keys of one
            // write. Parameter tokens are resolved through the current
            // partition each refinement round, making equality a congruence.
            public readonly List<(bool isParameter, string value)> tokens =
                new List<(bool isParameter, string value)>();
        }

        /// <summary>
        /// Partition-refinement value numbering over the controller's private
        /// float parameters (Alpern-Wegman-Zadeck congruence on the synchronous
        /// dataflow graph). Two parameters are merged only when their complete
        /// write-site multisets are structurally identical modulo the
        /// equivalence itself and their defaults match, so both carry bitwise
        /// identical values on every frame. Merging rewrites readers to one
        /// representative and deletes the duplicate's writes; no evaluation
        /// epoch moves because congruent sites always live in the same layer.
        /// </summary>
        private static void InternCongruentParameters(
            AnimatorController controller,
            string internalPrefix,
            Report report)
        {
            var parameters = controller.parameters;
            var candidates = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var parameter in parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Float &&
                    parameter.name.StartsWith(internalPrefix, StringComparison.Ordinal))
                    candidates[parameter.name] = parameter.defaultFloat;
            }
            if (candidates.Count < 2) return;

            // A parameter with a non-curve writer or an opaque reader is not a
            // pure dataflow value; keep it out of the congruence entirely.
            if (!TryCollectBehaviourConstraints(
                    controller, internalPrefix, out var ineligible))
                return;

            var sites = new Dictionary<string, List<CongruenceSite>>(
                StringComparer.Ordinal);
            for (var layerIndex = 0;
                 layerIndex < controller.layers.Length;
                 layerIndex++)
                CollectCongruenceSites(
                    controller.layers[layerIndex].stateMachine, layerIndex,
                    sites);

            var written = candidates.Keys
                .Where(name => sites.ContainsKey(name) &&
                               !ineligible.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            if (written.Count < 2) return;

            // Optimistic initial partition: same default value. Refinement can
            // only split classes, and a stable partition whose members share
            // default values and congruent update functions is a bisimulation,
            // so equality holds inductively on every frame.
            var classOf = new Dictionary<string, int>(StringComparer.Ordinal);
            var initial = new Dictionary<int, int>();
            foreach (var name in written)
            {
                var bits = BitConverter.SingleToInt32Bits(candidates[name]);
                if (!initial.TryGetValue(bits, out var classIndex))
                {
                    classIndex = initial.Count;
                    initial[bits] = classIndex;
                }
                classOf[name] = classIndex;
            }

            var builder = new System.Text.StringBuilder();
            while (true)
            {
                var signatures = new Dictionary<string, string>(
                    StringComparer.Ordinal);
                foreach (var name in written)
                {
                    builder.Clear();
                    builder.Append(classOf[name]).Append('\u0001');
                    var siteSignatures = sites[name]
                        .Select(site => SiteSignature(site, classOf))
                        .OrderBy(value => value, StringComparer.Ordinal);
                    foreach (var signature in siteSignatures)
                        builder.Append(signature).Append('\u0002');
                    signatures[name] = builder.ToString();
                }

                var next = new Dictionary<string, int>(StringComparer.Ordinal);
                var byRefinedSignature = new Dictionary<string, int>(
                    StringComparer.Ordinal);
                foreach (var name in written)
                {
                    if (!byRefinedSignature.TryGetValue(
                            signatures[name], out var classIndex))
                    {
                        classIndex = byRefinedSignature.Count;
                        byRefinedSignature[signatures[name]] = classIndex;
                    }
                    next[name] = classIndex;
                }

                var changed = written.Any(name => next[name] != classOf[name]);
                classOf = next;
                if (!changed) break;
            }

            var classes = written
                .GroupBy(name => classOf[name])
                .Where(group => group.Count() > 1)
                .ToList();
            if (classes.Count == 0) return;

            var rename = new Dictionary<string, string>(StringComparer.Ordinal);
            var removed = new HashSet<(AnimationClip clip, string property)>();
            var controllerPath = AssetDatabase.GetAssetPath(controller);
            foreach (var congruenceClass in classes)
            {
                var members = congruenceClass
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
                bool hasExternalWriter = members
                    .SelectMany(member => sites[member])
                    .Any(site =>
                    {
                        string clipPath =
                            AssetDatabase.GetAssetPath(site.clip);
                        return !string.IsNullOrEmpty(clipPath) &&
                            !string.Equals(
                                clipPath,
                                controllerPath,
                                StringComparison.OrdinalIgnoreCase);
                    });
                if (hasExternalWriter)
                {
                    continue;
                }
                var representative = members[0];
                foreach (var duplicate in members.Skip(1))
                {
                    rename[duplicate] = representative;
                    report.internedParameterMappings[duplicate] = representative;
                    report.internedCongruentParameters++;
                    foreach (var site in sites[duplicate])
                    {
                        // Shared subtrees enumerate one clip through several
                        // paths; the curve is removed (and counted) once.
                        if (!removed.Add((site.clip, site.binding.propertyName)))
                            continue;
                        AnimationUtility.SetEditorCurve(
                            site.clip, site.binding, null);
                        report.removedCongruentCurves++;
                        RecordCurveRemoval(
                            report, duplicate, internalPrefix);
                    }
                }
            }

            RewriteParameterReads(controller, rename);
        }

        private static string SiteSignature(
            CongruenceSite site,
            IReadOnlyDictionary<string, int> classOf)
        {
            var builder = new System.Text.StringBuilder();
            foreach (var (isParameter, value) in site.tokens)
            {
                if (!isParameter) builder.Append(value);
                else if (classOf.TryGetValue(value, out var classIndex))
                    builder.Append('#').Append(classIndex);
                else builder.Append('$').Append(value);
                builder.Append('\u0003');
            }
            return builder.ToString();
        }

        private static void CollectCongruenceSites(
            AnimatorStateMachine stateMachine,
            int layerIndex,
            IDictionary<string, List<CongruenceSite>> sites)
        {
            if (stateMachine == null) return;
            foreach (var child in stateMachine.states)
            {
                var state = child.state;
                if (state?.motion == null) continue;
                var prefix = new List<(bool isParameter, string value)>
                {
                    (false, "L" + layerIndex),
                    (false, "S" + state.GetInstanceID()),
                    (false, "spd" + state.speed.ToString("R")),
                    (false, "spm" + state.speedParameterActive),
                    (false, "tp" + state.timeParameterActive)
                };
                if (state.speedParameterActive)
                    prefix.Add((true, state.speedParameter ?? string.Empty));
                if (state.timeParameterActive)
                    prefix.Add((true, state.timeParameter ?? string.Empty));
                CollectMotionSites(state.motion, prefix, sites);
            }
            foreach (var child in stateMachine.stateMachines)
                CollectCongruenceSites(child.stateMachine, layerIndex, sites);
        }

        private static void CollectMotionSites(
            Motion motion,
            List<(bool isParameter, string value)> context,
            IDictionary<string, List<CongruenceSite>> sites)
        {
            if (motion is AnimationClip clip)
            {
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                var clipTokens = string.Concat(
                    "C", settings.startTime.ToString("R"),
                    "|", settings.stopTime.ToString("R"),
                    "|", settings.loopTime,
                    "|", settings.cycleOffset.ToString("R"),
                    "|", clip.frameRate.ToString("R"));
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!IsAnimatorParameter(binding)) continue;
                    var site = new CongruenceSite { clip = clip, binding = binding };
                    site.tokens.AddRange(context);
                    site.tokens.Add((false, clipTokens));
                    site.tokens.Add((false, CurveSignature(
                        AnimationUtility.GetEditorCurve(clip, binding))));
                    if (!sites.TryGetValue(binding.propertyName, out var list))
                    {
                        list = new List<CongruenceSite>();
                        sites[binding.propertyName] = list;
                    }
                    list.Add(site);
                }
                return;
            }

            if (!(motion is BlendTree tree)) return;
            var children = tree.children;
            var normalized = tree.blendType == BlendTreeType.Direct &&
                             UsesNormalizedBlendValues(tree);
            var treeTokens = new List<(bool isParameter, string value)>
            {
                // A non-normalized Direct child's weight is independent of its
                // siblings, so the sibling count stays out of the signature; a
                // normalized Direct weight divides by the sibling sum, so the
                // whole weight vector becomes part of the context.
                (false, "T" + (int)tree.blendType + "n" + normalized +
                        (tree.blendType == BlendTreeType.Direct && !normalized
                            ? string.Empty
                            : "c" + children.Length))
            };
            if (normalized)
            {
                foreach (var child in children)
                    treeTokens.Add(
                        (true, child.directBlendParameter ?? string.Empty));
            }
            if (tree.blendType != BlendTreeType.Direct)
                treeTokens.Add((true, tree.blendParameter ?? string.Empty));
            if (UsesSecondBlendParameter(tree.blendType))
                treeTokens.Add((true, tree.blendParameterY ?? string.Empty));
            if (tree.blendType == BlendTreeType.Simple1D)
            {
                foreach (var child in children)
                    treeTokens.Add((false, "t" + child.threshold.ToString("R")));
            }
            else if (tree.blendType != BlendTreeType.Direct)
            {
                foreach (var child in children)
                    treeTokens.Add((false,
                        "p" + child.position.x.ToString("R") +
                        "," + child.position.y.ToString("R")));
            }

            for (var index = 0; index < children.Length; index++)
            {
                var child = children[index];
                var childContext = new List<(bool isParameter, string value)>(
                    context.Count + treeTokens.Count + 3);
                childContext.AddRange(context);
                childContext.AddRange(treeTokens);
                // Direct children sum commutatively, so the sibling index is
                // not part of a Direct child's weight function; for threshold
                // and positional trees the child's slot determines its basis
                // function and must discriminate.
                var slot = tree.blendType == BlendTreeType.Direct
                    ? string.Empty
                    : "i" + index;
                childContext.Add((false, slot +
                                        "s" + child.timeScale.ToString("R") +
                                        "m" + child.mirror +
                                        "o" + child.cycleOffset.ToString("R")));
                if (tree.blendType == BlendTreeType.Direct)
                    childContext.Add(
                        (true, child.directBlendParameter ?? string.Empty));
                CollectMotionSites(child.motion, childContext, sites);
            }
        }

        private static string CurveSignature(AnimationCurve curve)
        {
            if (curve == null) return "K-";
            var builder = new System.Text.StringBuilder("K");
            builder.Append((int)curve.preWrapMode)
                .Append('/')
                .Append((int)curve.postWrapMode);
            foreach (var key in curve.keys)
            {
                builder.Append('|')
                    .Append(BitConverter.SingleToInt32Bits(key.time))
                    .Append(',')
                    .Append(BitConverter.SingleToInt32Bits(key.value))
                    .Append(',')
                    .Append(BitConverter.SingleToInt32Bits(key.inTangent))
                    .Append(',')
                    .Append(BitConverter.SingleToInt32Bits(key.outTangent))
                    .Append(',')
                    .Append((int)key.weightedMode)
                    .Append(',')
                    .Append(BitConverter.SingleToInt32Bits(key.inWeight))
                    .Append(',')
                    .Append(BitConverter.SingleToInt32Bits(key.outWeight));
            }
            return builder.ToString();
        }

        /// <summary>
        /// Collects private parameters that behaviours write or opaquely read.
        /// Returns false when an unknown behaviour exposes no discoverable
        /// parameter references, in which case interning is skipped entirely.
        /// </summary>
        private static bool TryCollectBehaviourConstraints(
            AnimatorController controller,
            string internalPrefix,
            out HashSet<string> ineligible)
        {
            ineligible = new HashSet<string>(StringComparer.Ordinal);
            var parameterNames = controller.parameters
                .Select(parameter => parameter.name)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var behaviour in EnumerateBehaviours(controller))
            {
                if (behaviour == null) continue;
                if (behaviour is VRCAvatarParameterDriver driver)
                {
                    if (driver.parameters == null) continue;
                    foreach (var parameter in driver.parameters)
                    {
                        // A driver write is a non-curve writer; a driver read
                        // could also be rewritten, but keeping both endpoints
                        // out of the congruence keeps the proof local to
                        // curve-defined dataflow.
                        if (!string.IsNullOrEmpty(parameter.name))
                            ineligible.Add(parameter.name);
                        if (!string.IsNullOrEmpty(parameter.source))
                            ineligible.Add(parameter.source);
                    }
                    continue;
                }

                var found = false;
                try
                {
                    var serialized = new SerializedObject(behaviour);
                    var iterator = serialized.GetIterator();
                    var enterChildren = true;
                    while (iterator.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (iterator.propertyType != SerializedPropertyType.String)
                            continue;
                        var value = iterator.stringValue;
                        if (!parameterNames.Contains(value)) continue;
                        ineligible.Add(value);
                        found = true;
                    }
                }
                catch (Exception)
                {
                    found = false;
                }

                if (!found) return false;
            }
            return true;
        }

        private static IEnumerable<StateMachineBehaviour> EnumerateBehaviours(
            AnimatorController controller)
        {
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;
                var queue = new Queue<AnimatorStateMachine>();
                queue.Enqueue(layer.stateMachine);
                while (queue.Count != 0)
                {
                    var machine = queue.Dequeue();
                    if (machine == null) continue;
                    foreach (var behaviour in machine.behaviours)
                        yield return behaviour;
                    foreach (var child in machine.states)
                    {
                        if (child.state == null) continue;
                        foreach (var behaviour in child.state.behaviours)
                            yield return behaviour;
                    }
                    foreach (var child in machine.stateMachines)
                        queue.Enqueue(child.stateMachine);
                }
            }
        }

        private static void RewriteParameterReads(
            AnimatorController controller,
            IReadOnlyDictionary<string, string> rename)
        {
            string Map(string name)
            {
                return !string.IsNullOrEmpty(name) &&
                       rename.TryGetValue(name, out var replacement)
                    ? replacement
                    : name;
            }

            var visited = new HashSet<Motion>();
            void RewriteMotion(Motion motion)
            {
                if (!(motion is BlendTree tree) || !visited.Add(tree)) return;
                if (tree.blendType != BlendTreeType.Direct)
                    tree.blendParameter = Map(tree.blendParameter);
                if (UsesSecondBlendParameter(tree.blendType))
                    tree.blendParameterY = Map(tree.blendParameterY);
                var children = tree.children;
                var changed = false;
                for (var index = 0; index < children.Length; index++)
                {
                    var mapped = Map(children[index].directBlendParameter);
                    if (!string.Equals(mapped,
                            children[index].directBlendParameter,
                            StringComparison.Ordinal))
                    {
                        children[index].directBlendParameter = mapped;
                        changed = true;
                    }
                    RewriteMotion(children[index].motion);
                }
                if (changed) tree.children = children;
                EditorUtility.SetDirty(tree);
            }

            void RewriteConditions(AnimatorStateTransition[] transitions)
            {
                if (transitions == null) return;
                foreach (var transition in transitions)
                    RewriteTransition(transition);
            }

            void RewriteTransition(AnimatorTransitionBase transition)
            {
                if (transition == null) return;
                var conditions = transition.conditions;
                var changed = false;
                for (var index = 0; index < conditions.Length; index++)
                {
                    var mapped = Map(conditions[index].parameter);
                    if (!string.Equals(mapped, conditions[index].parameter,
                            StringComparison.Ordinal))
                    {
                        conditions[index].parameter = mapped;
                        changed = true;
                    }
                }
                if (changed)
                {
                    transition.conditions = conditions;
                    EditorUtility.SetDirty(transition);
                }
            }

            void RewriteMachine(AnimatorStateMachine machine)
            {
                if (machine == null) return;
                RewriteConditions(machine.anyStateTransitions);
                foreach (var transition in machine.entryTransitions)
                    RewriteTransition(transition);
                foreach (var child in machine.states)
                {
                    var state = child.state;
                    if (state == null) continue;
                    if (state.speedParameterActive)
                        state.speedParameter = Map(state.speedParameter);
                    if (state.timeParameterActive)
                        state.timeParameter = Map(state.timeParameter);
                    if (state.mirrorParameterActive)
                        state.mirrorParameter = Map(state.mirrorParameter);
                    if (state.cycleOffsetParameterActive)
                        state.cycleOffsetParameter = Map(state.cycleOffsetParameter);
                    RewriteConditions(state.transitions);
                    RewriteMotion(state.motion);
                    EditorUtility.SetDirty(state);
                }
                foreach (var child in machine.stateMachines)
                    RewriteMachine(child.stateMachine);
            }

            foreach (var layer in controller.layers)
                RewriteMachine(layer.stateMachine);
        }

        private static void EliminateNeutralZeroBindings(
            AnimatorController controller,
            string internalPrefix,
            Report report)
        {
            var zeroDefaultPrivateFloats = controller.parameters
                .Where(parameter =>
                    parameter.type == AnimatorControllerParameterType.Float &&
                    BitConverter.SingleToInt32Bits(parameter.defaultFloat) == 0 &&
                    parameter.name.StartsWith(internalPrefix,
                        StringComparison.Ordinal))
                .Select(parameter => parameter.name)
                .ToHashSet(StringComparer.Ordinal);
            var retentionPrefix =
                internalPrefix + "BetaCoarticulation/Retention";
            var generalZeroDefaultPrivateFloats = zeroDefaultPrivateFloats
                .Where(parameter => !parameter.StartsWith(
                    retentionPrefix, StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);
            var candidates = new Dictionary<
                (AnimationClip clip, string parameter), ZeroCandidate>();
            var allowOperationLocalElimination =
                !DisableOperationLocalNeutralZeroEliminationForTests &&
                controller.layers.All(layer => layer.syncedLayerIndex < 0);
            foreach (var layer in controller.layers)
            {
                CollectNeutralZeroCandidates(
                    layer.stateMachine, internalPrefix, candidates);
                if (allowOperationLocalElimination)
                    CollectLocalNeutralZeroCandidates(
                        layer.stateMachine, generalZeroDefaultPrivateFloats,
                        candidates);
            }

            var controllerPath = AssetDatabase.GetAssetPath(controller);
            foreach (var candidate in candidates.Values.Where(candidate =>
                         candidate.eligible && candidate.useCount > 0))
            {
                var clipPath = AssetDatabase.GetAssetPath(candidate.clip);
                var controllerIsSaved = !string.IsNullOrEmpty(controllerPath);
                if (controllerIsSaved
                        ? !string.Equals(clipPath, controllerPath,
                            StringComparison.OrdinalIgnoreCase)
                        : !string.IsNullOrEmpty(clipPath))
                    continue;

                // A shared clip is edited only after every reachable occurrence
                // of this binding has passed the same safety proof.
                AnimationUtility.SetEditorCurve(
                    candidate.clip, candidate.binding, null);
                report.removedNeutralZeroCurves++;
                RecordCurveRemoval(
                    report, candidate.binding.propertyName, internalPrefix);
            }
        }

        private static void CollectNeutralZeroCandidates(
            AnimatorStateMachine stateMachine,
            string internalPrefix,
            IDictionary<(AnimationClip clip, string parameter), ZeroCandidate>
                candidates)
        {
            if (stateMachine == null) return;
            foreach (var child in stateMachine.states)
            {
                var state = child.state;
                if (state?.motion == null) continue;
                var uses = new List<CurveUse>();
                CollectCurveUses(
                    state.motion, true, false, uses);
                var safeNonzero = uses
                    .Where(use => use.safeBlendPath && !use.constantZero)
                    .Select(use => use.binding.propertyName)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var use in uses.Where(use => use.constantZero &&
                             IsNeutralZeroCandidateParameter(
                                 use.binding.propertyName, internalPrefix)))
                {
                    var key = (use.clip, use.binding.propertyName);
                    if (!candidates.TryGetValue(key, out var candidate))
                    {
                        candidate = new ZeroCandidate
                        {
                            clip = use.clip,
                            binding = use.binding
                        };
                        candidates[key] = candidate;
                    }
                    candidate.useCount++;
                    candidate.eligible &= use.safeBlendPath &&
                                          safeNonzero.Contains(
                                              use.binding.propertyName);
                }
            }

            foreach (var child in stateMachine.stateMachines)
                CollectNeutralZeroCandidates(
                    child.stateMachine, internalPrefix, candidates);
        }

        private static void CollectLocalNeutralZeroCandidates(
            AnimatorStateMachine stateMachine,
            IReadOnlyCollection<string> zeroDefaultPrivateFloats,
            IDictionary<(AnimationClip clip, string parameter), ZeroCandidate>
                candidates)
        {
            if (stateMachine == null) return;
            foreach (var child in stateMachine.states)
            {
                var state = child.state;
                if (state?.motion == null) continue;
                CollectLocalNeutralZeroCandidates(
                    state.motion, true, null,
                    zeroDefaultPrivateFloats, candidates);
            }

            foreach (var child in stateMachine.stateMachines)
                CollectLocalNeutralZeroCandidates(
                    child.stateMachine, zeroDefaultPrivateFloats, candidates);
        }

        private static void CollectLocalNeutralZeroCandidates(
            Motion motion,
            bool safePath,
            IReadOnlyCollection<string> immediateSiblingNonzero,
            IReadOnlyCollection<string> zeroDefaultPrivateFloats,
            IDictionary<(AnimationClip clip, string parameter), ZeroCandidate>
                candidates)
        {
            if (motion == null) return;
            if (motion is AnimationClip clip)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!IsAnimatorParameter(binding)) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (!IsRemovableFlatPositiveZero(curve) ||
                        zeroDefaultPrivateFloats == null ||
                        !zeroDefaultPrivateFloats.Contains(binding.propertyName))
                        continue;
                    var key = (clip, binding.propertyName);
                    if (!candidates.TryGetValue(key, out var candidate))
                    {
                        candidate = new ZeroCandidate
                        {
                            clip = clip,
                            binding = binding
                        };
                        candidates[key] = candidate;
                    }
                    candidate.useCount++;
                    // The folded root clip establishes the whole state's AAP
                    // binding baseline. Its zeros are not operation-local even
                    // when another direct root child writes the same property.
                    candidate.eligible &=
                        !clip.name.StartsWith(
                            "Folded constants by ", StringComparison.Ordinal) &&
                        safePath && immediateSiblingNonzero != null &&
                        immediateSiblingNonzero.Contains(binding.propertyName);
                }
                return;
            }

            if (!(motion is BlendTree tree)) return;
            var safeTree = tree.blendType == BlendTreeType.Simple1D ||
                           tree.blendType == BlendTreeType.Direct &&
                           !UsesNormalizedBlendValues(tree);
            var childSafePath = safePath && safeTree;
            var immediateNonzero = tree.children
                .Select(child => child.motion as AnimationClip)
                .Where(childClip => childClip != null)
                .SelectMany(childClip =>
                    AnimationUtility.GetCurveBindings(childClip)
                        .Where(IsAnimatorParameter)
                        .Where(binding => !IsIdenticallyFlatZeroIgnoringSign(
                            AnimationUtility.GetEditorCurve(childClip, binding)))
                        .Select(binding => binding.propertyName))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var child in tree.children)
            {
                CollectLocalNeutralZeroCandidates(
                    child.motion, childSafePath,
                    child.motion is AnimationClip ? immediateNonzero : null,
                    zeroDefaultPrivateFloats, candidates);
            }
        }

        private static void CollectCurveUses(
            Motion motion,
            bool safePath,
            bool insideSafeBlend,
            ICollection<CurveUse> uses)
        {
            if (motion == null) return;
            if (motion is AnimationClip clip)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!IsAnimatorParameter(binding)) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    uses.Add(new CurveUse
                    {
                        clip = clip,
                        binding = binding,
                        safeBlendPath = safePath && insideSafeBlend,
                        constantZero = IsConstantZero(curve)
                    });
                }
                return;
            }

            if (!(motion is BlendTree tree)) return;
            var safeTree = tree.blendType == BlendTreeType.Simple1D ||
                           tree.blendType == BlendTreeType.Direct &&
                           !UsesNormalizedBlendValues(tree);
            var childSafePath = safePath && safeTree;
            var childInsideSafeBlend = insideSafeBlend || safeTree;
            foreach (var child in tree.children)
                CollectCurveUses(
                    child.motion, childSafePath, childInsideSafeBlend, uses);
        }

        private static bool IsConstantZero(AnimationCurve curve)
        {
            return IsIdenticallyFlatZeroIgnoringSign(curve);
        }

        private static bool IsIdenticallyFlatZeroIgnoringSign(AnimationCurve curve)
        {
            if (curve == null || curve.keys.Length == 0) return false;
            if (curve.keys.Length == 1) return curve.keys[0].value == 0f;
            return curve.keys.All(key =>
                key.value == 0f && key.inTangent == 0f &&
                key.outTangent == 0f);
        }

        private static bool IsRemovableFlatPositiveZero(AnimationCurve curve)
        {
            if (curve == null || curve.keys.Length != 1) return false;
            var key = curve.keys[0];
            return BitConverter.SingleToInt32Bits(key.time) == 0 &&
                   BitConverter.SingleToInt32Bits(key.value) == 0 &&
                   BitConverter.SingleToInt32Bits(key.inTangent) == 0 &&
                   BitConverter.SingleToInt32Bits(key.outTangent) == 0 &&
                   BitConverter.SingleToInt32Bits(key.inWeight) == 0 &&
                   BitConverter.SingleToInt32Bits(key.outWeight) == 0;
        }

        private static bool IsNeutralZeroCandidateParameter(
            string parameter,
            string internalPrefix)
        {
            // The runtime neutral-zero proof applies generally to the tested
            // BlendTree primitives, but signed/affine articulation cones also
            // rely on authored baselines and cancellation binders. Keep this
            // first shipping pass on the nonnegative Beta-retention observer,
            // whose zero contributions are algebraically neutral and whose full
            // frame staging is covered by the generated-controller replay.
            return !string.IsNullOrEmpty(parameter) &&
                   parameter.StartsWith(
                       internalPrefix + "BetaCoarticulation/Retention",
                       StringComparison.Ordinal);
        }

        private static Analysis Analyze(
            AnimatorController controller,
            IReadOnlyCollection<string> parameterNames,
            string internalPrefix)
        {
            var analysis = new Analysis();
            foreach (var layer in controller.layers)
                AnalyzeStateMachine(layer.stateMachine, analysis, parameterNames,
                    internalPrefix);
            return analysis;
        }

        private static void AnalyzeStateMachine(
            AnimatorStateMachine stateMachine,
            Analysis analysis,
            IReadOnlyCollection<string> parameterNames,
            string internalPrefix)
        {
            if (stateMachine == null) return;
            AnalyzeBehaviours(stateMachine.behaviours, analysis, parameterNames,
                internalPrefix);
            AnalyzeConditions(stateMachine.anyStateTransitions, analysis.roots);
            AnalyzeConditions(stateMachine.entryTransitions, analysis.roots);

            foreach (var child in stateMachine.states)
            {
                var state = child.state;
                if (state == null) continue;
                AnalyzeConditions(state.transitions, analysis.roots);
                AnalyzeBehaviours(state.behaviours, analysis, parameterNames,
                    internalPrefix);
                AddStateControlReads(state, analysis.roots);
                AnalyzeMotion(state.motion, new HashSet<string>(
                    StringComparer.Ordinal), analysis);
            }

            foreach (var child in stateMachine.stateMachines)
                AnalyzeStateMachine(child.stateMachine, analysis, parameterNames,
                    internalPrefix);
        }

        private static void AnalyzeMotion(
            Motion motion,
            HashSet<string> dependencies,
            Analysis analysis)
        {
            if (motion == null) return;
            if (motion is AnimationClip clip)
            {
                analysis.clips.Add(clip);
                var bindings = AnimationUtility.GetCurveBindings(clip);
                var hasPhysicalOutput = clip.events.Length != 0 ||
                                        AnimationUtility
                                            .GetObjectReferenceCurveBindings(clip)
                                            .Length != 0;
                foreach (var binding in bindings)
                {
                    if (!IsAnimatorParameter(binding))
                    {
                        hasPhysicalOutput = true;
                        continue;
                    }

                    if (!analysis.writers.TryGetValue(binding.propertyName,
                            out var sites))
                    {
                        sites = new List<WriteSite>();
                        analysis.writers[binding.propertyName] = sites;
                    }
                    var site = new WriteSite { clip = clip, binding = binding };
                    site.dependencies.UnionWith(dependencies);
                    sites.Add(site);
                }

                if (hasPhysicalOutput) analysis.roots.UnionWith(dependencies);
                return;
            }

            if (!(motion is BlendTree tree)) return;
            var treeDependencies = new HashSet<string>(dependencies,
                StringComparer.Ordinal);
            if (tree.blendType != BlendTreeType.Direct &&
                !string.IsNullOrWhiteSpace(tree.blendParameter))
                treeDependencies.Add(tree.blendParameter);
            if (UsesSecondBlendParameter(tree.blendType) &&
                !string.IsNullOrWhiteSpace(tree.blendParameterY))
                treeDependencies.Add(tree.blendParameterY);

            foreach (var child in tree.children)
            {
                var childDependencies = new HashSet<string>(treeDependencies,
                    StringComparer.Ordinal);
                if (tree.blendType == BlendTreeType.Direct &&
                    !string.IsNullOrWhiteSpace(child.directBlendParameter))
                    childDependencies.Add(child.directBlendParameter);
                AnalyzeMotion(child.motion, childDependencies, analysis);
            }
        }

        private static HashSet<string> BackwardLiveParameters(Analysis analysis)
        {
            var live = new HashSet<string>(analysis.roots, StringComparer.Ordinal);
            var queue = new Queue<string>(live);
            while (queue.Count != 0)
            {
                var output = queue.Dequeue();
                if (!analysis.writers.TryGetValue(output, out var sites)) continue;
                foreach (var dependency in sites.SelectMany(site => site.dependencies))
                {
                    if (!live.Add(dependency)) continue;
                    queue.Enqueue(dependency);
                }
            }
            return live;
        }

        private static void AddStateControlReads(
            AnimatorState state,
            ISet<string> roots)
        {
            if (state.speedParameterActive) AddParameter(state.speedParameter, roots);
            if (state.timeParameterActive) AddParameter(state.timeParameter, roots);
            if (state.mirrorParameterActive) AddParameter(state.mirrorParameter, roots);
            if (state.cycleOffsetParameterActive)
                AddParameter(state.cycleOffsetParameter, roots);
        }

        private static void AnalyzeConditions(
            IEnumerable<AnimatorStateTransition> transitions,
            ISet<string> roots)
        {
            if (transitions == null) return;
            foreach (var transition in transitions)
            foreach (var condition in transition.conditions)
                AddParameter(condition.parameter, roots);
        }

        private static void AnalyzeConditions(
            IEnumerable<AnimatorTransition> transitions,
            ISet<string> roots)
        {
            if (transitions == null) return;
            foreach (var transition in transitions)
            foreach (var condition in transition.conditions)
                AddParameter(condition.parameter, roots);
        }

        private static void AnalyzeBehaviours(
            IEnumerable<StateMachineBehaviour> behaviours,
            Analysis analysis,
            IReadOnlyCollection<string> parameterNames,
            string internalPrefix)
        {
            if (behaviours == null) return;
            foreach (var behaviour in behaviours)
            {
                if (behaviour == null) continue;
                if (behaviour is VRCAvatarParameterDriver driver)
                {
                    if (driver.parameters == null) continue;
                    foreach (var parameter in driver.parameters)
                    {
                        AddParameter(parameter.name, analysis.roots);
                        AddParameter(parameter.source, analysis.roots);
                    }
                    continue;
                }

                // Unknown behaviours can read Animator parameters through native
                // state APIs. Serialized exact-name references are retained, and
                // if none are discoverable all private values remain live.
                var found = false;
                try
                {
                    var serialized = new SerializedObject(behaviour);
                    var iterator = serialized.GetIterator();
                    var enterChildren = true;
                    while (iterator.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (iterator.propertyType != SerializedPropertyType.String)
                            continue;
                        var value = iterator.stringValue;
                        if (!parameterNames.Contains(value)) continue;
                        analysis.roots.Add(value);
                        found = true;
                    }
                }
                catch (Exception)
                {
                    found = false;
                }

                if (!found)
                {
                    foreach (var parameter in parameterNames.Where(parameter =>
                                 parameter.StartsWith(internalPrefix,
                                     StringComparison.Ordinal)))
                        analysis.roots.Add(parameter);
                }
            }
        }

        private static int CountAnimatorCurves(IEnumerable<AnimationClip> clips)
        {
            return clips.Sum(clip => AnimationUtility.GetCurveBindings(clip)
                .Count(IsAnimatorParameter));
        }

        private static bool IsAnimatorParameter(EditorCurveBinding binding)
        {
            return binding.type == typeof(Animator) &&
                   string.IsNullOrEmpty(binding.path) &&
                   !string.IsNullOrEmpty(binding.propertyName);
        }

        private static bool UsesSecondBlendParameter(BlendTreeType type)
        {
            return type == BlendTreeType.SimpleDirectional2D ||
                   type == BlendTreeType.FreeformDirectional2D ||
                   type == BlendTreeType.FreeformCartesian2D;
        }

        private static bool UsesNormalizedBlendValues(BlendTree tree)
        {
            if (tree == null || tree.blendType != BlendTreeType.Direct)
                return false;
            var serialized = new SerializedObject(tree);
            var normalized = serialized.FindProperty("m_NormalizedBlendValues");
            return normalized != null && normalized.boolValue;
        }

        private static void AddParameter(string parameter, ISet<string> values)
        {
            if (!string.IsNullOrWhiteSpace(parameter)) values.Add(parameter);
        }

        private static void RecordCurveRemoval(
            Report report,
            string parameter,
            string internalPrefix)
        {
            report.removedAnimatorCurves++;
            var group = InternalGroup(parameter, internalPrefix);
            report.removedCurvesByGroup[group] =
                report.removedCurvesByGroup.TryGetValue(group,
                    out var removed)
                    ? removed + 1
                    : 1;
        }

        private static string InternalGroup(string parameter, string internalPrefix)
        {
            if (string.IsNullOrEmpty(parameter) ||
                !parameter.StartsWith(internalPrefix, StringComparison.Ordinal))
                return "<external>";
            var remainder = parameter.Substring(internalPrefix.Length);
            var separator = remainder.IndexOf('/');
            return separator < 0 ? remainder : remainder.Substring(0, separator);
        }
    }
}
