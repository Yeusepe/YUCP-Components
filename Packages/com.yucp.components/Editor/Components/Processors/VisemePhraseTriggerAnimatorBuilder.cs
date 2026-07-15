#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace YUCP.Components.Editor.VisemePhrase
{
    /// <summary>
    /// Builds one owner-only, avatar-wide timed DFA. All enrolled phrases share
    /// prefix states and failure links; only their tiny network edge decoders are
    /// separate. No AnyState transition is used anywhere in the controller.
    /// </summary>
    internal static class VisemePhraseTriggerAnimatorBuilder
    {
        private const string RawViseme = "Viseme";
        private const string IsLocal = "IsLocal";
        private const float TalkingThreshold = 0.08f;
        private const float TransientThreshold = 0.025f;
        // VRChat recommends at least 20 ms for states carrying State Behaviours;
        // shorter states may transition before a parameter driver executes.
        private const float DriverStateSeconds = 1f / 50f;
        // Pure timing states carry no StateMachineBehaviour and must not inherit
        // the driver safety dwell. Keeping this epsilon separate prevents every
        // learned short phone/window from being silently inflated to 20 ms.
        private const float TimingEpsilonSeconds = 0.0001f;
        private const float BoundaryConsumeSeconds = 0.12f;

        internal sealed class Request
        {
            internal string controllerPath;
            internal string parametersPath;
            internal VisemePhraseBuildPlan plan;
        }

        internal sealed class Result
        {
            internal AnimatorController controller;
            internal VRCExpressionParameters parameters;
            internal readonly List<string> globalParameters = new List<string>();
            internal readonly List<string> externalParameters = new List<string>();
        }

        private sealed class MatcherClass
        {
            internal int index;
            internal string key;
            internal string talking;
            internal string onset;
            internal bool hasPaused;
            internal string armedParameter;
            internal readonly HashSet<int> phraseIndices = new HashSet<int>();
        }

        internal static Result Build(Request request)
        {
            if (request?.plan == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.controllerPath) ||
                string.IsNullOrWhiteSpace(request.parametersPath))
                throw new ArgumentException("Generated controller and parameter paths are required.");
            if (!VisemePhraseGlobalTrie.TryBuild(
                    request.plan, out var trieRoot, out var stateCount, out var trieError))
                throw new InvalidOperationException(trieError);
            if (stateCount > VisemePhraseBuildPlan.MaximumCompiledStates)
                throw new InvalidOperationException(
                    $"The shared phrase matcher needs {stateCount} states; the maximum is " +
                    VisemePhraseBuildPlan.MaximumCompiledStates + ".");
            if (!VisemePhraseTimedSubsetPlanner.TryPlan(
                    trieRoot, request.plan, out var timedGraph,
                    out stateCount, out var timedError))
                throw new InvalidOperationException(timedError);

            AssetDatabase.DeleteAsset(request.controllerPath);
            AssetDatabase.DeleteAsset(request.parametersPath);
            var controller = new AnimatorController { name = "YUCP Viseme Phrase Trigger" };
            AssetDatabase.CreateAsset(controller, request.controllerPath);
            var graph = new ControllerGraph(controller);
            var result = new Result { controller = controller };

            graph.AddParameter(RawViseme, AnimatorControllerParameterType.Int);
            graph.AddParameter(IsLocal, AnimatorControllerParameterType.Float);
            result.externalParameters.Add(RawViseme);
            result.externalParameters.Add(IsLocal);

            for (var phraseIndex = 0; phraseIndex < request.plan.phrases.Count; phraseIndex++)
            {
                var phrase = request.plan.phrases[phraseIndex];
                graph.AddParameter(phrase.talkingParameter, AnimatorControllerParameterType.Float);
                graph.AddParameter(phrase.onsetParameter, AnimatorControllerParameterType.Float);
                graph.AddParameter(phrase.releaseParameter, AnimatorControllerParameterType.Float);
                graph.AddParameter(phrase.matchedParameter, AnimatorControllerParameterType.Bool);
                graph.AddParameter(phrase.confidenceParameter, AnimatorControllerParameterType.Float);
                graph.AddParameter(phrase.progressParameter, AnimatorControllerParameterType.Float);
                graph.AddParameter(phrase.carrierParameter, AnimatorControllerParameterType.Bool);
                result.externalParameters.Add(phrase.talkingParameter);
                result.externalParameters.Add(phrase.onsetParameter);
                result.externalParameters.Add(phrase.releaseParameter);
                result.globalParameters.Add(phrase.matchedParameter);
                result.globalParameters.Add(phrase.confidenceParameter);
                result.globalParameters.Add(phrase.progressParameter);
                result.globalParameters.Add(phrase.carrierParameter);
            }

            var classes = BuildMatcherClasses(request.plan);
            foreach (var matcherClass in classes)
            {
                if (!matcherClass.hasPaused) continue;
                matcherClass.armedParameter = "__YUCP_Phrase_Armed_" + matcherClass.index;
                graph.AddParameter(matcherClass.armedParameter, AnimatorControllerParameterType.Float);
                AddBoundaryLayer(graph, matcherClass);
            }
            for (var phraseIndex = 0; phraseIndex < request.plan.phrases.Count; phraseIndex++)
            {
                graph.AddParameter(CooldownReady(phraseIndex),
                    AnimatorControllerParameterType.Float);
                graph.AddParameter(CooldownTrigger(phraseIndex),
                    AnimatorControllerParameterType.Bool);
                AddPhraseCooldownLayer(
                    graph, request.plan.phrases[phraseIndex], phraseIndex);
            }

            AddTimedSubsetMatcherLayer(graph, request.plan, timedGraph, classes);
            foreach (var phrase in request.plan.phrases)
                AddEdgeDecoderLayer(graph, phrase);

            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            parameters.name = "YUCP Viseme Phrase Network Carriers";
            parameters.parameters = request.plan.phrases
                .Where(phrase => phrase.declareCarrier)
                .Select(phrase => new VRCExpressionParameters.Parameter
                {
                    name = phrase.carrierParameter,
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = false,
                    networkSynced = true
                })
                .ToArray();
            AssetDatabase.CreateAsset(parameters, request.parametersPath);
            result.parameters = parameters;
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(parameters);
            AssetDatabase.SaveAssetIfDirty(controller);
            AssetDatabase.SaveAssetIfDirty(parameters);
            return result;
        }

        internal static bool TryLoadExisting(
            string controllerPath,
            string parametersPath,
            VisemePhraseBuildPlan plan,
            out Result result)
        {
            result = null;
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var parameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(parametersPath);
            if (controller == null || parameters == null || plan == null) return false;
            var loaded = new Result { controller = controller, parameters = parameters };
            loaded.externalParameters.Add(RawViseme);
            loaded.externalParameters.Add(IsLocal);
            foreach (var phrase in plan.phrases)
            {
                loaded.externalParameters.Add(phrase.talkingParameter);
                loaded.externalParameters.Add(phrase.onsetParameter);
                loaded.externalParameters.Add(phrase.releaseParameter);
                loaded.globalParameters.Add(phrase.matchedParameter);
                loaded.globalParameters.Add(phrase.confidenceParameter);
                loaded.globalParameters.Add(phrase.progressParameter);
                loaded.globalParameters.Add(phrase.carrierParameter);
            }
            result = loaded;
            return true;
        }

        private static List<MatcherClass> BuildMatcherClasses(VisemePhraseBuildPlan plan)
        {
            var output = new List<MatcherClass>();
            foreach (var group in plan.phrases
                         .Select((phrase, index) => new { phrase, index })
                         .GroupBy(item => item.phrase.MatcherClassKey, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var first = group.First().phrase;
                var matcherClass = new MatcherClass
                {
                    index = output.Count + 1,
                    key = group.Key,
                    talking = first.talkingParameter,
                    onset = first.onsetParameter,
                    hasPaused = group.Any(item => item.phrase.leadingPauseSeconds > 0f)
                };
                foreach (var item in group) matcherClass.phraseIndices.Add(item.index);
                output.Add(matcherClass);
            }
            return output;
        }

        private static void AddBoundaryLayer(ControllerGraph graph, MatcherClass matcherClass)
        {
            var layer = graph.AddLayer("YUCP Phrase Boundary " + matcherClass.index);
            var remote = graph.AddState(layer, "Remote",
                graph.SetterClip("Boundary remote " + matcherClass.index,
                    DriverStateSeconds, matcherClass.armedParameter, 0f));
            var unarmed = graph.AddState(layer, "Unarmed",
                graph.SetterClip("Boundary unarmed " + matcherClass.index,
                    DriverStateSeconds, matcherClass.armedParameter, 0f));
            var silence = graph.AddState(layer, "Leading silence",
                graph.SetterClip("Boundary silence " + matcherClass.index,
                    0.18f, matcherClass.armedParameter, 0f));
            var armed = graph.AddState(layer, "Armed",
                graph.SetterClip("Boundary armed " + matcherClass.index,
                    DriverStateSeconds, matcherClass.armedParameter, 1f));
            var consume = graph.AddState(layer, "Consume boundary",
                graph.SetterClip("Boundary consume " + matcherClass.index,
                    BoundaryConsumeSeconds, matcherClass.armedParameter, 1f));
            layer.defaultState = remote;

            AddImmediate(remote, unarmed, AnimatorConditionMode.Greater,
                0.5f, IsLocal);
            AddImmediate(unarmed, remote, AnimatorConditionMode.Less,
                0.5f, IsLocal);
            AddImmediate(silence, remote, AnimatorConditionMode.Less,
                0.5f, IsLocal);
            AddImmediate(armed, remote, AnimatorConditionMode.Less,
                0.5f, IsLocal);
            AddImmediate(consume, remote, AnimatorConditionMode.Less,
                0.5f, IsLocal);
            AddImmediate(unarmed, silence, AnimatorConditionMode.Less,
                TalkingThreshold * 0.5f, matcherClass.talking);
            AddImmediate(silence, unarmed, AnimatorConditionMode.Greater,
                TalkingThreshold, matcherClass.talking);
            var armedAfterSilence = silence.AddTransition(armed);
            ConfigureTimed(armedAfterSilence, 1f);
            AddImmediate(armed, consume, AnimatorConditionMode.Greater,
                TransientThreshold, matcherClass.onset);
            // Some microphones produce a weak onset derivative. Talking is the
            // secondary consume signal so an armed command can never be reused
            // to join a later token in the same utterance.
            AddImmediate(armed, consume, AnimatorConditionMode.Greater,
                TalkingThreshold, matcherClass.talking);
            var consumed = consume.AddTransition(unarmed);
            ConfigureTimed(consumed, 1f);
        }

        private static void AddPhraseCooldownLayer(
            ControllerGraph graph,
            VisemePhraseBuildPhrase phrase,
            int phraseIndex)
        {
            var readyParameter = CooldownReady(phraseIndex);
            var triggerParameter = CooldownTrigger(phraseIndex);
            var layer = graph.AddLayer("YUCP Phrase Cooldown " + FriendlyName(phrase));
            var remote = graph.AddState(layer, "Remote",
                graph.SetterClip("Cooldown remote " + phraseIndex,
                    DriverStateSeconds, readyParameter, 0f));
            var ready = graph.AddState(layer, "Ready",
                graph.SetterClip("Cooldown ready " + phraseIndex,
                    DriverStateSeconds, readyParameter, 1f));
            var cooling = graph.AddState(layer, "Cooling",
                graph.SetterClip("Cooldown active " + phraseIndex,
                    Math.Max(VisemePhraseBuildPlan.MinimumNetworkCooldownSeconds,
                        phrase.cooldownSeconds), readyParameter, 0f));
            layer.defaultState = remote;
            AddImmediate(remote, ready, AnimatorConditionMode.Greater, 0.5f, IsLocal);
            AddImmediate(ready, remote, AnimatorConditionMode.Less, 0.5f, IsLocal);
            AddImmediate(cooling, remote, AnimatorConditionMode.Less, 0.5f, IsLocal);
            AddImmediateBool(ready, cooling, triggerParameter, true);
            graph.AddDriver(cooling, true, Set(triggerParameter, 0f));
            var complete = cooling.AddTransition(ready);
            ConfigureTimed(complete, 1f);
        }

        private static void AddTimedSubsetMatcherLayer(
            ControllerGraph graph,
            VisemePhraseBuildPlan plan,
            VisemePhraseTimedSubsetPlanner.Graph timedGraph,
            IReadOnlyList<MatcherClass> classes)
        {
            var layer = graph.AddLayer("YUCP Phrase Shared Matcher");
            var remote = graph.AddState(layer, "Remote",
                graph.SubsetOutputMotion("Matcher remote", DriverStateSeconds,
                    null, Array.Empty<int>(), plan, timedGraph.root));
            var ready = graph.AddState(layer, "Ready",
                graph.SubsetOutputMotion("Matcher ready", DriverStateSeconds,
                    null, Array.Empty<int>(), plan, timedGraph.root));
            layer.defaultState = remote;
            AddImmediate(remote, ready, AnimatorConditionMode.Greater, 0.5f, IsLocal);
            AddImmediate(ready, remote, AnimatorConditionMode.Less, 0.5f, IsLocal);

            var emitStates = new Dictionary<(int phrase, int carrier), AnimatorState>();
            var trailingWaitStates = new Dictionary<int, AnimatorState>();
            for (var phraseIndex = 0; phraseIndex < plan.phrases.Count; phraseIndex++)
            {
                var phrase = plan.phrases[phraseIndex];
                for (var carrier = 0; carrier <= 1; carrier++)
                {
                    var target = 1 - carrier;
                    var emit = graph.AddState(layer,
                        "Emit " + FriendlyName(phrase) + " edge " + target,
                        graph.AcceptedOutputMotion("Emit " + FriendlyName(phrase),
                            DriverStateSeconds, plan, phraseIndex));
                    graph.AddDriver(emit, true,
                        Set(phrase.carrierParameter, target),
                        Set(CooldownTrigger(phraseIndex), 1f));
                    var emitted = emit.AddTransition(ready);
                    ConfigureTimed(emitted, 1f);
                    emitStates[(phraseIndex, carrier)] = emit;
                }

                if (phrase.trailingPauseSeconds <= 0f) continue;
                var wait = graph.AddState(layer,
                    "Await trailing silence " + FriendlyName(phrase),
                    graph.AcceptedOutputMotion("Await trailing " + FriendlyName(phrase),
                        DriverStateSeconds, plan, phraseIndex));
                var timer = graph.AddState(layer,
                    "Trailing silence " + FriendlyName(phrase),
                    graph.AcceptedOutputMotion("Trailing silence " + FriendlyName(phrase),
                        phrase.trailingPauseSeconds, plan, phraseIndex));
                trailingWaitStates[phraseIndex] = wait;
                AddImmediate(wait, ready, AnimatorConditionMode.Greater,
                    TransientThreshold, phrase.onsetParameter);
                AddImmediate(wait, timer, AnimatorConditionMode.Less,
                    TalkingThreshold * 0.5f, phrase.talkingParameter);
                AddImmediate(timer, ready, AnimatorConditionMode.Greater,
                    TalkingThreshold, phrase.talkingParameter);
                AddImmediate(timer, ready, AnimatorConditionMode.Greater,
                    TransientThreshold, phrase.onsetParameter);
                for (var carrier = 0; carrier <= 1; carrier++)
                {
                    var transition = timer.AddTransition(emitStates[(phraseIndex, carrier)]);
                    ConfigureTimed(transition, 1f);
                    transition.AddCondition(carrier == 0
                            ? AnimatorConditionMode.IfNot
                            : AnimatorConditionMode.If,
                        0f, phrase.carrierParameter);
                    transition.AddCondition(AnimatorConditionMode.Greater,
                        0.5f, CooldownReady(phraseIndex));
                    transition.AddCondition(AnimatorConditionMode.IfNot,
                        0f, CooldownTrigger(phraseIndex));
                }
            }

            var segmentsByState = timedGraph.states.ToDictionary(
                state => state,
                state => VisemePhraseTimedSubsetPlanner.TimingSegments(
                    state.node, state.candidateIds));
            var animatorStates = new Dictionary<
                (VisemePhraseTimedSubsetPlanner.State state, int segment), AnimatorState>();
            foreach (var timedState in timedGraph.states.OrderBy(state => state.index))
            {
                var segments = segmentsByState[timedState];
                for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
                {
                    var segment = segments[segmentIndex];
                    var duration = Math.Max(TimingEpsilonSeconds,
                        segment.maximumSeconds - segment.minimumSeconds);
                    var label = "Timed " + timedState.index + "." + segmentIndex +
                                " depth " + timedState.node.depth + " [" +
                                string.Join(",", Aliases(timedState.node.value)) + "] " +
                                (timedState.pausedSession ? "paused" : "natural") +
                                " candidates " + string.Join(",", segment.candidateIds);
                    animatorStates[(timedState, segmentIndex)] = graph.AddState(layer, label,
                        graph.SubsetOutputMotion(label, duration, timedState.node,
                            segment.candidateIds, plan, timedGraph.root));
                }
            }

            // Paused starts are added first so an armed boundary preserves its
            // session when both modes share the same source and first viseme.
            foreach (var start in timedGraph.rootStarts
                         .OrderByDescending(item => item.paused)
                         .ThenBy(item => item.state.index))
            {
                var matcherClass = classes.Single(item => string.Equals(
                    item.key, start.state.matcherClass, StringComparison.Ordinal));
                foreach (var phraseIndex in start.phraseIndices)
                foreach (var alias in Aliases(start.state.node.value))
                {
                    var transition = ready.AddTransition(animatorStates[(start.state, 0)]);
                    ConfigureImmediate(transition);
                    if (start.paused)
                    {
                        transition.AddCondition(AnimatorConditionMode.Greater,
                            TalkingThreshold, matcherClass.talking);
                        transition.AddCondition(AnimatorConditionMode.Greater,
                            0.5f, matcherClass.armedParameter);
                    }
                    transition.AddCondition(AnimatorConditionMode.Greater,
                        0.5f, CooldownReady(phraseIndex));
                    transition.AddCondition(AnimatorConditionMode.IfNot,
                        0f, CooldownTrigger(phraseIndex));
                    transition.AddCondition(AnimatorConditionMode.Equals, alias, RawViseme);
                }
            }

            foreach (var timedState in timedGraph.states.OrderBy(state => state.index))
            {
                var segments = segmentsByState[timedState];
                for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
                {
                    var from = animatorStates[(timedState, segmentIndex)];
                    var segment = segments[segmentIndex];
                    AddImmediate(from, remote, AnimatorConditionMode.Less, 0.5f, IsLocal);

                    if (segmentIndex + 1 < segments.Count)
                    {
                        var next = segments[segmentIndex + 1];
                        AddSegmentObservationTransitions(
                            from, true, next, timedState, timedGraph, plan,
                            animatorStates, emitStates, trailingWaitStates);
                        var held = new HashSet<int>(Aliases(timedState.node.value));
                        for (var alias = 0; alias < 15; alias++)
                        {
                            if (held.Contains(alias)) continue;
                            AddFailureOrRestartTransitions(
                                from, ready, true, alias, timedState, timedGraph,
                                plan, segment, animatorStates);
                        }
                        foreach (var alias in held)
                        {
                            var advanceTime = from.AddTransition(
                                animatorStates[(timedState, segmentIndex + 1)]);
                            ConfigureTimed(advanceTime, 1f);
                            advanceTime.AddCondition(
                                AnimatorConditionMode.Equals, alias, RawViseme);
                        }
                    }
                    AddSegmentObservationTransitions(
                        from, false, segment, timedState, timedGraph, plan,
                        animatorStates, emitStates, trailingWaitStates);
                    var heldAliases = new HashSet<int>(Aliases(timedState.node.value));
                    for (var alias = 0; alias < 15; alias++)
                    {
                        if (heldAliases.Contains(alias)) continue;
                        AddFailureOrRestartTransitions(
                            from, ready, false, alias, timedState, timedGraph,
                            plan, segment, animatorStates);
                    }
                    if (segmentIndex + 1 >= segments.Count)
                    {
                        // Changed-token observation and acceptance transitions
                        // are deliberately ordered before timeout. At a sampled
                        // exact maximum both are eligible, and the scorer's
                        // interval is inclusive. A held token has no such
                        // transition and therefore still expires here.
                        var timeout = from.AddTransition(ready);
                        // Exact-max observations win because their conditioned
                        // transitions were added first. If the phone is still
                        // held at that boundary, expire it immediately so a
                        // change on the following sampled frame cannot borrow
                        // another frame beyond the learned maximum.
                        // Unity evaluates an exit time of exactly 1 only after
                        // equality, which lets a next-frame phone change win.
                        // One float epsilon below 1 expires the held phone on
                        // the boundary sample; a simultaneous valid change still
                        // wins because its conditioned transition is ordered first.
                        ConfigureTimed(timeout, 1f - 0.000001f);
                    }
                    // Talking is a smoothed observer output and can lag the raw
                    // Viseme by a frame at onset or briefly dip inside quiet
                    // consonants. Once a candidate has started, its learned
                    // timing envelope is the authoritative expiry condition.
                    // Resetting on a single low Talking sample drops ordinary
                    // whispered/mumbled words and contradicts VAD hangover.
                }
            }
        }

        private static void AddFailureOrRestartTransitions(
            AnimatorState from,
            AnimatorState ready,
            bool timed,
            int observedViseme,
            VisemePhraseTimedSubsetPlanner.State current,
            VisemePhraseTimedSubsetPlanner.Graph timedGraph,
            VisemePhraseBuildPlan plan,
            VisemePhraseTimedSubsetPlanner.TimingBucket currentSegment,
            IReadOnlyDictionary<
                (VisemePhraseTimedSubsetPlanner.State state, int segment), AnimatorState>
                animatorStates)
        {
            var longest = FindLongestSafeFailureState(
                current, timedGraph, plan, currentSegment, observedViseme, timed);
            if (longest != null)
            {
                foreach (var phraseIndex in longest.candidateIds.Select(id =>
                             VisemePhraseTimedSubsetPlanner.CandidatePhrase(
                                 timedGraph.root, id)).Distinct().OrderBy(index => index))
                {
                    var failure = from.AddTransition(animatorStates[(longest, 0)]);
                    if (timed) ConfigureTimed(failure, 1f);
                    else ConfigureImmediate(failure);
                    failure.AddCondition(AnimatorConditionMode.Equals,
                        observedViseme, RawViseme);
                    AddCooldownConditions(failure, phraseIndex);
                }
            }

            // Consume the current observation as the first phone of a fresh
            // natural-speech candidate whenever possible. Going through Ready
            // would lose one-frame tokens at low frame rates. Paused commands
            // deliberately do not restart inside an utterance: they require a
            // newly armed leading-silence boundary.
            foreach (var start in timedGraph.rootStarts
                         .Where(item => !item.paused &&
                                        string.Equals(item.state.matcherClass,
                                            current.matcherClass,
                                            StringComparison.Ordinal) &&
                                        Aliases(item.state.node.value)
                                            .Contains(observedViseme))
                         .OrderBy(item => item.state.index))
            foreach (var phraseIndex in start.phraseIndices)
            {
                var restart = from.AddTransition(animatorStates[(start.state, 0)]);
                if (timed) ConfigureTimed(restart, 1f);
                else ConfigureImmediate(restart);
                restart.AddCondition(AnimatorConditionMode.Equals,
                    observedViseme, RawViseme);
                AddCooldownConditions(restart, phraseIndex);
            }

            var failed = from.AddTransition(ready);
            if (timed) ConfigureTimed(failed, 1f);
            else ConfigureImmediate(failed);
            failed.AddCondition(AnimatorConditionMode.Equals, observedViseme, RawViseme);
        }

        private static VisemePhraseTimedSubsetPlanner.State FindLongestSafeFailureState(
            VisemePhraseTimedSubsetPlanner.State current,
            VisemePhraseTimedSubsetPlanner.Graph timedGraph,
            VisemePhraseBuildPlan plan,
            VisemePhraseTimedSubsetPlanner.TimingBucket currentSegment,
            int observedViseme,
            bool sampledAtSegmentEnd)
        {
            var sourcePath = FindTriePath(timedGraph.root, current.node);
            if (sourcePath == null || sourcePath.Count == 0) return null;
            var sourceCandidateIds = currentSegment.candidateIds.Length > 0
                ? currentSegment.candidateIds
                : current.candidateIds;
            var matches = new List<VisemePhraseTimedSubsetPlanner.State>();
            foreach (var target in timedGraph.states.Where(state =>
                         !state.pausedSession &&
                         state.node.depth > 1 &&
                         state.node.depth <= sourcePath.Count + 1 &&
                         string.Equals(state.matcherClass, current.matcherClass,
                             StringComparison.Ordinal) &&
                         Aliases(state.node.value).Contains(observedViseme)))
            {
                var targetPath = FindTriePath(timedGraph.root, target.node);
                if (targetPath == null || targetPath.Count != target.node.depth) continue;
                var suffixStart = sourcePath.Count - (targetPath.Count - 1);
                if (suffixStart < 0) continue;

                var validTargetCandidates = new HashSet<int>();
                foreach (var targetCandidate in target.candidateIds)
                {
                    var phraseIndex = VisemePhraseTimedSubsetPlanner.CandidatePhrase(
                        timedGraph.root, targetCandidate);
                    if (plan.phrases[phraseIndex].leadingPauseSeconds > 0f) continue;
                    var compatible = true;
                    for (var targetIndex = 0;
                         targetIndex + 1 < targetPath.Count && compatible;
                         targetIndex++)
                    {
                        var sourceNode = sourcePath[suffixStart + targetIndex];
                        var targetNode = targetPath[targetIndex];
                        if (!targetNode.candidateStates.TryGetValue(
                                targetCandidate, out var targetTiming))
                        {
                            compatible = false;
                            break;
                        }

                        var possibleAliases = sourceCandidateIds
                            .Where(sourceNode.candidateStates.ContainsKey)
                            .SelectMany(id => sourceNode.candidateStates[id].aliases)
                            .Distinct().ToArray();
                        if (possibleAliases.Length == 0 ||
                            possibleAliases.Any(alias =>
                                !targetTiming.aliases.Contains(alias)))
                        {
                            compatible = false;
                            break;
                        }

                        float possibleMinimum;
                        float possibleMaximum;
                        if (ReferenceEquals(sourceNode, current.node))
                        {
                            possibleMinimum = sampledAtSegmentEnd
                                ? currentSegment.maximumSeconds
                                : currentSegment.minimumSeconds;
                            possibleMaximum = currentSegment.maximumSeconds;
                        }
                        else
                        {
                            var sourceTimings = sourceCandidateIds
                                .Where(sourceNode.candidateStates.ContainsKey)
                                .Select(id => sourceNode.candidateStates[id])
                                .ToArray();
                            if (sourceTimings.Length == 0)
                            {
                                compatible = false;
                                break;
                            }
                            possibleMinimum = sourceTimings.Min(state => state.minimumSeconds);
                            possibleMaximum = sourceTimings.Max(state => state.maximumSeconds);
                        }
                        if (possibleMinimum + 0.000001f < targetTiming.minimumSeconds ||
                            possibleMaximum - 0.000001f > targetTiming.maximumSeconds)
                            compatible = false;
                    }
                    if (compatible) validTargetCandidates.Add(targetCandidate);
                }

                if (target.candidateIds.Length > 0 && target.candidateIds.All(
                        validTargetCandidates.Contains))
                    matches.Add(target);
            }

            var deepest = matches.Count == 0
                ? 0
                : matches.Max(state => state.node.depth);
            var deepestMatches = matches.Where(state => state.node.depth == deepest)
                .OrderByDescending(state => state.candidateIds.Length)
                .ThenBy(state => state.index)
                .ToArray();
            // Multiple different longest suffix nodes with the same observation
            // would make Animator transition order semantic. Fall back to the
            // unambiguous one-token restart instead of guessing.
            return deepestMatches.Select(state => state.node).Distinct().Count() == 1
                ? deepestMatches[0]
                : null;
        }

        private static List<VisemePhraseGlobalTrie.Node> FindTriePath(
            VisemePhraseGlobalTrie.Node root,
            VisemePhraseGlobalTrie.Node target)
        {
            var path = new List<VisemePhraseGlobalTrie.Node>();
            return TryFindTriePath(root, target, path) ? path : null;
        }

        private static bool TryFindTriePath(
            VisemePhraseGlobalTrie.Node node,
            VisemePhraseGlobalTrie.Node target,
            ICollection<VisemePhraseGlobalTrie.Node> path)
        {
            foreach (var child in node.children)
            {
                path.Add(child);
                if (ReferenceEquals(child, target)) return true;
                if (TryFindTriePath(child, target, path)) return true;
                path.Remove(child);
            }
            return false;
        }

        private static void AddSegmentObservationTransitions(
            AnimatorState from,
            bool timed,
            VisemePhraseTimedSubsetPlanner.TimingBucket segment,
            VisemePhraseTimedSubsetPlanner.State timedState,
            VisemePhraseTimedSubsetPlanner.Graph timedGraph,
            VisemePhraseBuildPlan plan,
            IReadOnlyDictionary<
                (VisemePhraseTimedSubsetPlanner.State state, int segment), AnimatorState>
                animatorStates,
            IReadOnlyDictionary<(int phrase, int carrier), AnimatorState> emitStates,
            IReadOnlyDictionary<int, AnimatorState> trailingWaitStates)
        {
            if (segment.candidateIds.Length == 0) return;
            var sample = segment.minimumSeconds +
                         (segment.maximumSeconds - segment.minimumSeconds) * 0.5f;
            foreach (var advance in timedState.advances
                         .Where(item => sample > item.bucket.minimumSeconds - 0.000001f &&
                                        sample < item.bucket.maximumSeconds + 0.000001f)
                         .OrderByDescending(item => item.bucket.candidateIds.Length)
                         .ThenBy(item => item.destination.index))
            foreach (var alias in advance.aliases)
            {
                var transition = from.AddTransition(animatorStates[(advance.destination, 0)]);
                if (timed) ConfigureTimed(transition, 1f);
                else ConfigureImmediate(transition);
                transition.AddCondition(AnimatorConditionMode.Equals, alias, RawViseme);
            }

            var candidatePhrases = segment.candidateIds
                .Select(id => VisemePhraseTimedSubsetPlanner.CandidatePhrase(
                    timedGraph.root, id))
                .Distinct().OrderBy(index => index);
            foreach (var phraseIndex in candidatePhrases)
            {
                var phrase = plan.phrases[phraseIndex];
                if (phrase.leadingPauseSeconds > 0f && !timedState.pausedSession) continue;
                if (!timedState.node.acceptingCandidateIds.Any(id =>
                        segment.candidateIds.Contains(id) &&
                        VisemePhraseTimedSubsetPlanner.CandidatePhrase(
                            timedGraph.root, id) == phraseIndex)) continue;
                if (phrase.trailingPauseSeconds > 0f)
                {
                    var accept = from.AddTransition(trailingWaitStates[phraseIndex]);
                    if (timed) ConfigureTimed(accept, 1f);
                    else ConfigureImmediate(accept);
                    accept.AddCondition(AnimatorConditionMode.Greater,
                        TransientThreshold, phrase.releaseParameter);
                    AddCooldownConditions(accept, phraseIndex);
                    continue;
                }

                // Natural speech is complete once its final observed Viseme has
                // survived the learned minimum residence time. Do not wait for
                // silence or a different token: both can be delayed by AVR
                // hangover, and a short final token may disappear between two
                // Animator evaluations. The segment passed by the caller starts
                // at that minimum, so a timed transition fires at its boundary
                // and an immediate transition fires after entering the eligible
                // segment. Carrier polarity remains explicit and edge-safe.
                for (var carrier = 0; carrier <= 1; carrier++)
                {
                    var accept = from.AddTransition(emitStates[(phraseIndex, carrier)]);
                    if (timed) ConfigureTimed(accept, 1f);
                    else ConfigureImmediate(accept);
                    accept.AddCondition(carrier == 0
                            ? AnimatorConditionMode.IfNot
                            : AnimatorConditionMode.If,
                        0f, phrase.carrierParameter);
                    AddCooldownConditions(accept, phraseIndex);
                }

                var held = new HashSet<int>(Aliases(timedState.node.value));
                var continuations = new HashSet<int>(timedState.advances
                    .SelectMany(advance => advance.aliases));
                for (var exitAlias = 0; exitAlias < 15; exitAlias++)
                {
                    if (held.Contains(exitAlias) || continuations.Contains(exitAlias)) continue;
                    for (var carrier = 0; carrier <= 1; carrier++)
                    {
                        var accept = from.AddTransition(emitStates[(phraseIndex, carrier)]);
                        if (timed) ConfigureTimed(accept, 1f);
                        else ConfigureImmediate(accept);
                        accept.AddCondition(AnimatorConditionMode.Equals, exitAlias, RawViseme);
                        accept.AddCondition(carrier == 0
                                ? AnimatorConditionMode.IfNot
                                : AnimatorConditionMode.If,
                            0f, phrase.carrierParameter);
                        AddCooldownConditions(accept, phraseIndex);
                    }
                }
            }
        }

        private static void AddCooldownConditions(
            AnimatorStateTransition transition,
            int phraseIndex)
        {
            transition.AddCondition(AnimatorConditionMode.Greater,
                0.5f, CooldownReady(phraseIndex));
            transition.AddCondition(AnimatorConditionMode.IfNot,
                0f, CooldownTrigger(phraseIndex));
        }

        private static IEnumerable<int> Aliases(VisemePhraseBuildState state) =>
            (state.aliases ?? Array.Empty<int>()).Where(alias => alias >= 0 && alias < 15)
            .Distinct().OrderBy(alias => alias);

        private static void AddEdgeDecoderLayer(
            ControllerGraph graph,
            VisemePhraseBuildPhrase phrase)
        {
            var layer = graph.AddLayer("YUCP Phrase Edge " + FriendlyName(phrase));
            var init = graph.AddState(layer, "Initialize without pulse",
                graph.TimerClip("Initialize " + FriendlyName(phrase),
                    VisemePhraseBuildPlan.InitialNetworkSuppressionSeconds));
            var armedFalse = graph.AddState(layer, "Armed 0",
                graph.TimerClip("Armed 0 " + FriendlyName(phrase), DriverStateSeconds));
            var armedTrue = graph.AddState(layer, "Armed 1",
                graph.TimerClip("Armed 1 " + FriendlyName(phrase), DriverStateSeconds));
            var pulseRise = graph.AddState(layer, "Pulse rising edge",
                graph.TimerClip("Pulse rising " + FriendlyName(phrase),
                    Math.Max(0.05f, phrase.pulseSeconds)));
            var pulseFall = graph.AddState(layer, "Pulse falling edge",
                graph.TimerClip("Pulse falling " + FriendlyName(phrase),
                    Math.Max(0.05f, phrase.pulseSeconds)));
            var reset = graph.AddState(layer, "Reset pulse",
                graph.TimerClip("Reset pulse " + FriendlyName(phrase), DriverStateSeconds));
            layer.defaultState = init;
            graph.AddDriver(init, false, Set(phrase.matchedParameter, 0f));
            graph.AddDriver(armedFalse, false, Set(phrase.matchedParameter, 0f));
            graph.AddDriver(armedTrue, false, Set(phrase.matchedParameter, 0f));
            graph.AddDriver(pulseRise, false, Set(phrase.matchedParameter, 1f));
            graph.AddDriver(pulseFall, false, Set(phrase.matchedParameter, 1f));
            graph.AddDriver(reset, false, Set(phrase.matchedParameter, 0f));
            AddTimedBool(init, armedFalse, phrase.carrierParameter, false);
            AddTimedBool(init, armedTrue, phrase.carrierParameter, true);
            AddImmediateBool(armedFalse, pulseRise, phrase.carrierParameter, true);
            AddImmediateBool(armedTrue, pulseFall, phrase.carrierParameter, false);
            var riseDone = pulseRise.AddTransition(reset);
            ConfigureTimed(riseDone, 1f);
            var fallDone = pulseFall.AddTransition(reset);
            ConfigureTimed(fallDone, 1f);
            // The owner matcher already enforces the >=1.25 s network cadence.
            // Re-arm the decoder after one reset frame so a delayed second edge
            // cannot be sampled and lost inside an additional remote cooldown.
            AddTimedBool(reset, armedFalse, phrase.carrierParameter, false);
            AddTimedBool(reset, armedTrue, phrase.carrierParameter, true);
        }

        private static void AddImmediate(
            AnimatorState from,
            AnimatorState to,
            AnimatorConditionMode mode,
            float threshold,
            string parameter)
        {
            var transition = from.AddTransition(to);
            ConfigureImmediate(transition);
            transition.AddCondition(mode, threshold, parameter);
        }

        private static void AddTimedBool(
            AnimatorState from, AnimatorState to, string parameter, bool value)
        {
            var transition = from.AddTransition(to);
            ConfigureTimed(transition, 1f);
            transition.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                0f, parameter);
        }

        private static void AddImmediateBool(
            AnimatorState from, AnimatorState to, string parameter, bool value)
        {
            var transition = from.AddTransition(to);
            ConfigureImmediate(transition);
            transition.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                0f, parameter);
        }

        private static void ConfigureImmediate(AnimatorStateTransition transition)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            transition.canTransitionToSelf = false;
            transition.interruptionSource = TransitionInterruptionSource.None;
        }

        private static void ConfigureTimed(AnimatorStateTransition transition, float exitTime)
        {
            transition.hasExitTime = true;
            // Values just above one are used for inclusive maximum boundaries.
            // Unity keeps evaluating normalized time on non-looping clips, so a
            // 1+epsilon timeout distinguishes the exact maximum from a late
            // observation without another clock parameter.
            transition.exitTime = Math.Max(0f, exitTime);
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            transition.canTransitionToSelf = false;
            transition.interruptionSource = TransitionInterruptionSource.None;
        }

        private static VRC_AvatarParameterDriver.Parameter Set(string parameter, float value) =>
            new VRC_AvatarParameterDriver.Parameter
            {
                name = parameter,
                type = VRC_AvatarParameterDriver.ChangeType.Set,
                value = value
            };

        private static string CooldownReady(int phraseIndex) =>
            "__YUCP_Phrase_CooldownReady_" + phraseIndex;

        private static string CooldownTrigger(int phraseIndex) =>
            "__YUCP_Phrase_CooldownTrigger_" + phraseIndex;

        private static string FriendlyName(VisemePhraseBuildPhrase phrase) =>
            new string((string.IsNullOrWhiteSpace(phrase.parameterKey)
                    ? phrase.stableId
                    : phrase.parameterKey)
                .Select(character => char.IsLetterOrDigit(character) || character == '_'
                    ? character
                    : '_').ToArray());

        private sealed class ControllerGraph
        {
            private const string TimerSink = "__YUCP_Phrase_Timer";
            private readonly AnimatorController controller;
            private readonly HashSet<UnityEngine.Object> subAssets =
                new HashSet<UnityEngine.Object>();

            internal ControllerGraph(AnimatorController controller)
            {
                this.controller = controller;
                AddParameter(TimerSink, AnimatorControllerParameterType.Float);
            }

            internal void AddParameter(string name, AnimatorControllerParameterType type)
            {
                if (string.IsNullOrWhiteSpace(name))
                    throw new InvalidOperationException("Generated Animator parameters cannot be blank.");
                var existing = controller.parameters.FirstOrDefault(parameter =>
                    string.Equals(parameter.name, name, StringComparison.Ordinal));
                if (existing != null)
                {
                    if (existing.type != type)
                        throw new InvalidOperationException(
                            $"Animator parameter '{name}' is both {existing.type} and {type}.");
                    return;
                }
                controller.AddParameter(new AnimatorControllerParameter { name = name, type = type });
            }

            internal AnimatorStateMachine AddLayer(string name)
            {
                controller.AddLayer(name);
                var layers = controller.layers;
                var index = layers.Length - 1;
                layers[index].defaultWeight = 1f;
                controller.layers = layers;
                AddSubAsset(layers[index].stateMachine);
                return layers[index].stateMachine;
            }

            internal AnimatorState AddState(
                AnimatorStateMachine machine, string name, Motion motion)
            {
                var state = machine.AddState(name);
                state.writeDefaultValues = true;
                state.motion = motion;
                return state;
            }

            internal AnimationClip TimerClip(string name, float seconds) =>
                OutputClip(name, seconds, Array.Empty<KeyValuePair<string, float>>());

            internal AnimationClip SetterClip(
                string name, float seconds, string parameter, float value) =>
                OutputClip(name, seconds,
                    new[] { new KeyValuePair<string, float>(parameter, value) });

            internal Motion SubsetOutputMotion(
                string name,
                float seconds,
                VisemePhraseGlobalTrie.Node node,
                IEnumerable<int> candidateIds,
                VisemePhraseBuildPlan plan,
                VisemePhraseGlobalTrie.Node root)
            {
                var values = ZeroOutputs(plan);
                if (node != null && root != null)
                {
                    foreach (var group in candidateIds.Distinct().GroupBy(id =>
                                 VisemePhraseTimedSubsetPlanner.CandidatePhrase(root, id)))
                    {
                        var phrase = plan.phrases[group.Key];
                        var maximumDepth = Math.Max(1,
                            phrase.variants.Max(variant => variant.states.Count));
                        var progress = Mathf.Clamp01((float)node.depth / maximumDepth);
                        var budget = Mathf.Clamp01(phrase.runtimeAcceptanceCost);
                        var confidence = group.Select(id =>
                        {
                            var candidate = root.candidates.First(item => item.id == id);
                            var normalizedCost = candidate.runtimePathCost /
                                                 Math.Max(1, candidate.canonicalStateCount);
                            if (budget <= 0.000001f)
                                return normalizedCost <= 0.000001f ? 1f : 0f;
                            return Mathf.Clamp01((budget - normalizedCost) / budget);
                        }).DefaultIfEmpty(0f).Max();
                        SetOutput(values, phrase.progressParameter, progress);
                        // Confidence is live path-budget margin. Matched is the
                        // authoritative accepted pulse; confidence intentionally
                        // resets when the matcher leaves the candidate path.
                        SetOutput(values, phrase.confidenceParameter, confidence);
                    }
                }
                return OutputClip(name, seconds, values);
            }

            internal Motion AcceptedOutputMotion(
                string name,
                float seconds,
                VisemePhraseBuildPlan plan,
                int acceptedPhrase)
            {
                var values = ZeroOutputs(plan);
                // Do not bind the accepted phrase's live confidence here. Its
                // final remaining-budget margin persists through trailing-silence
                // and edge emission, then Ready explicitly resets it to zero.
                values.RemoveAll(pair => string.Equals(pair.Key,
                    plan.phrases[acceptedPhrase].confidenceParameter,
                    StringComparison.Ordinal));
                SetOutput(values, plan.phrases[acceptedPhrase].progressParameter, 1f);
                return OutputClip(name, seconds, values);
            }

            internal void AddDriver(
                AnimatorState state,
                bool localOnly,
                params VRC_AvatarParameterDriver.Parameter[] parameters)
            {
                var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
                driver.name = "YUCP Phrase Parameter Driver";
                driver.localOnly = localOnly;
                driver.isEnabled = true;
                driver.debugString = localOnly
                    ? "YUCP owner-only phrase edge"
                    : "YUCP decoded phrase pulse";
                driver.parameters = parameters.ToList();
                AddSubAsset(driver);
                state.behaviours = state.behaviours.Concat(new StateMachineBehaviour[] { driver })
                    .ToArray();
            }

            private AnimationClip OutputClip(
                string name,
                float seconds,
                IEnumerable<KeyValuePair<string, float>> values,
                string timerParameter = TimerSink)
            {
                var duration = Math.Max(TimingEpsilonSeconds, seconds);
                var clip = new AnimationClip { name = name, frameRate = 60f };
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve("", typeof(Animator), timerParameter),
                    AnimationCurve.Linear(0f, 0f, duration, duration));
                foreach (var pair in values)
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve("", typeof(Animator), pair.Key),
                        AnimationCurve.Constant(0f, duration, pair.Value));
                AddSubAsset(clip);
                return clip;
            }

            private static List<KeyValuePair<string, float>> ZeroOutputs(
                VisemePhraseBuildPlan plan)
            {
                var values = new List<KeyValuePair<string, float>>();
                foreach (var phrase in plan.phrases)
                {
                    values.Add(new KeyValuePair<string, float>(phrase.confidenceParameter, 0f));
                    values.Add(new KeyValuePair<string, float>(phrase.progressParameter, 0f));
                }
                return values;
            }

            private static void SetOutput(
                IList<KeyValuePair<string, float>> values,
                string parameter,
                float value)
            {
                for (var index = 0; index < values.Count; index++)
                {
                    if (!string.Equals(values[index].Key, parameter, StringComparison.Ordinal)) continue;
                    values[index] = new KeyValuePair<string, float>(parameter, value);
                    return;
                }
                values.Add(new KeyValuePair<string, float>(parameter, value));
            }

            private void AddSubAsset(UnityEngine.Object value)
            {
                if (value == null || subAssets.Contains(value) || AssetDatabase.Contains(value)) return;
                value.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(value, controller);
                subAssets.Add(value);
            }
        }
    }
}
#endif
