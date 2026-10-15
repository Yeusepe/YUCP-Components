using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace YUCP.Components.Editor
{
    internal sealed class ParameterPackCandidate
    {
        internal ParameterPackCandidate(
            ParameterObservation observation,
            AnimatorControllerParameterType animatorType,
            bool preferBus)
        {
            Observation = observation ?? throw new ArgumentNullException(nameof(observation));
            AnimatorType = animatorType;
            PreferBus = preferBus;
        }

        internal ParameterObservation Observation { get; }
        internal string Name => Observation.Name;

        /// <summary>The type the FX controller declares, which the encoder reads.</summary>
        internal AnimatorControllerParameterType AnimatorType { get; }

        /// <summary>Also an event bus candidate; packed only after the rest.</summary>
        internal bool PreferBus { get; }

        internal int CurrentBits =>
            Observation.ValueType == VRCExpressionParameters.ValueType.Bool ? 1 : 8;
    }

    internal sealed class ParameterPackMember
    {
        internal ParameterPackMember(ParameterPackCandidate candidate, int stride)
        {
            Candidate = candidate;
            Stride = stride;
        }

        internal ParameterPackCandidate Candidate { get; }
        internal ParameterObservation Observation => Candidate.Observation;
        internal int Stride { get; }
    }

    /// <summary>
    /// Several parameters carried by one synced parameter as the digits of a
    /// mixed-radix number. Each digit is a cell index, so the group needs only
    /// the product of its members' cell counts. One parameter is delivered whole,
    /// so a remote client never sees one member updated without the others.
    /// </summary>
    internal sealed class ParameterPackGroup
    {
        internal ParameterPackGroup(string carrier, IReadOnlyList<ParameterPackMember> members)
        {
            Carrier = carrier;
            Members = members;
            Cardinality = members.Aggregate(1, (product, member) =>
                product * member.Observation.CellCount);
            DefaultCode = Encode(members.Select(member =>
                Math.Max(0, member.Observation.DefaultCell)).ToArray());
        }

        internal string Carrier { get; }
        internal IReadOnlyList<ParameterPackMember> Members { get; }
        internal int Cardinality { get; }
        internal int DefaultCode { get; }
        internal bool BoolCarrier => Cardinality <= 2;
        internal int CarrierBits => BoolCarrier ? 1 : 8;
        internal int MemberBits => Members.Sum(member => member.Candidate.CurrentBits);
        internal int SavedBits => MemberBits - CarrierBits;

        internal int Encode(IReadOnlyList<int> cells)
        {
            var code = 0;
            for (var index = 0; index < Members.Count; index++)
                code += cells[index] * Members[index].Stride;
            return code;
        }

        internal int CellOf(int code, int memberIndex)
        {
            var member = Members[memberIndex];
            return code / member.Stride % member.Observation.CellCount;
        }
    }

    /// <summary>
    /// Plans and compiles exact packing. Only cells are transmitted, and every
    /// value in a cell drives remote Animators identically, so packing changes
    /// nothing a remote client can observe. A group exists only when it frees bits.
    /// </summary>
    internal static class ParameterCompressionPacking
    {
        internal const int CarrierCardinality = 256;

        // The encoder needs one transition per combination of value runs. This
        // bounds the generated graph for members whose cells are fragmented.
        internal const int MaximumEncoderTransitions = 4096;

        private const string IsLocalName = "IsLocal";
        private const float RefreshSeconds = 1f;
        private const float FloatBoundarySlack = 1e-5f;

        internal static IReadOnlyList<ParameterPackGroup> Plan(
            IEnumerable<ParameterPackCandidate> candidates,
            string prefix)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            var ordered = candidates
                .Where(candidate => candidate.Observation.CellCount >= 2 &&
                                    candidate.Observation.CellCount <= CarrierCardinality / 2)
                .OrderByDescending(candidate => candidate.CurrentBits)
                .ThenBy(candidate => candidate.PreferBus)
                .ThenByDescending(candidate => candidate.Observation.CellCount)
                .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
                .ToArray();

            // First-fit decreasing on the multiplicative capacity. Single-bit
            // members never open a group: Booleans alone cannot share a word
            // more cheaply than their own bits, but they fill leftover capacity.
            var bins = new List<List<ParameterPackCandidate>>();
            foreach (var candidate in ordered)
            {
                var bin = bins.FirstOrDefault(members =>
                    Product(members) * candidate.Observation.CellCount <= CarrierCardinality &&
                    EncoderTransitions(members) * Runs(candidate.Observation) <=
                    MaximumEncoderTransitions);
                if (bin != null) bin.Add(candidate);
                else if (candidate.CurrentBits > 1 &&
                         Runs(candidate.Observation) <= MaximumEncoderTransitions)
                    bins.Add(new List<ParameterPackCandidate> { candidate });
            }

            var groups = new List<ParameterPackGroup>();
            foreach (var bin in bins)
            {
                var stride = 1;
                var members = new List<ParameterPackMember>();
                foreach (var candidate in bin)
                {
                    members.Add(new ParameterPackMember(candidate, stride));
                    stride *= candidate.Observation.CellCount;
                }
                var group = new ParameterPackGroup(
                    ParameterCompressionContract.NormalizePrefix(prefix) +
                    "/_Pack/" + groups.Count, members);
                if (group.SavedBits > 0) groups.Add(group);
            }

            // Carrier names stay dense after discarding groups that saved nothing.
            return groups.Select((group, index) => new ParameterPackGroup(
                ParameterCompressionContract.NormalizePrefix(prefix) + "/_Pack/" + index,
                group.Members)).ToArray();
        }

        /// <summary>Whether the encoder can read the local value exactly through
        /// the type the FX controller declares for it.</summary>
        internal static bool CanEncode(
            VRCExpressionParameters.ValueType expressionType,
            AnimatorControllerParameterType animatorType)
        {
            switch (expressionType)
            {
                case VRCExpressionParameters.ValueType.Bool:
                    return animatorType == AnimatorControllerParameterType.Bool ||
                           animatorType == AnimatorControllerParameterType.Float ||
                           animatorType == AnimatorControllerParameterType.Int;
                case VRCExpressionParameters.ValueType.Int:
                    return animatorType == AnimatorControllerParameterType.Int ||
                           animatorType == AnimatorControllerParameterType.Float;
                default:
                    return animatorType == AnimatorControllerParameterType.Float;
            }
        }

        /// <summary>
        /// Adds one layer per group to the FX controller. On the wearer's client
        /// the router enters the encode state whose cells match every member, and
        /// that state writes its code to the carrier. On remote clients the router
        /// enters the decode state for the received code, which writes each
        /// member's representative value. A code state returns to the router when
        /// its code goes stale, and about once a second regardless, because
        /// drivers can be skipped for a few frames after an avatar loads.
        /// </summary>
        internal static void Build(
            AnimatorController controller,
            string prefix,
            IReadOnlyList<ParameterPackGroup> groups)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (groups == null || groups.Count == 0) return;

            var isLocalType = EnsureIsLocal(controller);
            var writeDefaults = DetermineWriteDefaults(controller);
            var clock = ParameterCompressionContract.NormalizePrefix(prefix) +
                        "/_Internal/PackClock";
            EnsureParameter(controller, clock, AnimatorControllerParameterType.Float, 0f);
            var refreshClip = new AnimationClip
            {
                name = "YUCP Compressor Pack Refresh",
                frameRate = 60f
            };
            AnimationUtility.SetEditorCurve(refreshClip,
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), clock),
                AnimationCurve.Linear(0f, 0f, RefreshSeconds, RefreshSeconds));
            AddSubAsset(controller, refreshClip);

            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                EnsureParameter(controller, group.Carrier,
                    group.BoolCarrier
                        ? AnimatorControllerParameterType.Bool
                        : AnimatorControllerParameterType.Int,
                    group.DefaultCode);
                foreach (var member in group.Members)
                    EnsureParameter(controller, member.Candidate.Name,
                        member.Candidate.AnimatorType,
                        member.Observation.Representatives[
                            Math.Max(0, member.Observation.DefaultCell)]);

                var layer = new GeneratedAnimatorLayer(controller,
                    "YUCP Parameter Compressor Pack " + groupIndex);

                // A code state only watches for its own code going stale, so a
                // steady avatar evaluates a few conditions per frame. The router
                // searches every code only after a change or a periodic refresh.
                var router = layer.State("Router");
                router.writeDefaultValues = writeDefaults;
                layer.DefaultState = router;

                for (var code = 0; code < group.Cardinality; code++)
                {
                    var cells = Enumerable.Range(0, group.Members.Count)
                        .Select(memberIndex => group.CellOf(code, memberIndex))
                        .ToArray();

                    var encode = layer.State("Encode " + code);
                    encode.writeDefaultValues = writeDefaults;
                    encode.motion = refreshClip;
                    AddDriver(layer, encode, "YUCP Parameter Compressor Pack Encoder", true, new[]
                    {
                        Set(group.Carrier, code)
                    });
                    Refresh(layer, encode, router);
                    foreach (var combination in RunCombinations(group, cells))
                    {
                        var enter = layer.Transition(router, encode);
                        ConfigureImmediate(enter);
                        AddIsLocal(enter, isLocalType, true);
                        for (var memberIndex = 0; memberIndex < group.Members.Count; memberIndex++)
                            AddRunConditions(enter, group.Members[memberIndex],
                                ValueRuns(group.Members[memberIndex].Observation,
                                    cells[memberIndex])[combination[memberIndex]],
                                true);
                    }
                    for (var memberIndex = 0; memberIndex < group.Members.Count; memberIndex++)
                    foreach (var outside in OutsideRuns(
                                 group.Members[memberIndex].Observation, cells[memberIndex]))
                    {
                        var leave = layer.Transition(encode, router);
                        ConfigureImmediate(leave);
                        AddRunConditions(leave, group.Members[memberIndex], outside, false);
                    }

                    var decode = layer.State("Decode " + code);
                    decode.writeDefaultValues = writeDefaults;
                    decode.motion = refreshClip;
                    AddDriver(layer, decode, "YUCP Parameter Compressor Pack Decoder", false, group.Members.Select(
                        (member, memberIndex) => Set(member.Candidate.Name,
                            member.Observation.Representatives[cells[memberIndex]])));
                    Refresh(layer, decode, router);
                    var receive = layer.Transition(router, decode);
                    ConfigureImmediate(receive);
                    AddIsLocal(receive, isLocalType, false);
                    AddCarrierCondition(receive, group, code, true);
                    var stale = layer.Transition(decode, router);
                    ConfigureImmediate(stale);
                    AddCarrierCondition(stale, group, code, false);
                }
                layer.Commit(0f);
            }
        }

        private static void AddCarrierCondition(
            AnimatorStateTransition transition,
            ParameterPackGroup group,
            int code,
            bool equals)
        {
            if (group.BoolCarrier)
                transition.Require((code != 0) == equals
                    ? AnimatorConditionMode.If
                    : AnimatorConditionMode.IfNot, 0f, group.Carrier);
            else
                transition.Require(equals
                    ? AnimatorConditionMode.Equals
                    : AnimatorConditionMode.NotEqual, code, group.Carrier);
        }

        private static int Product(IEnumerable<ParameterPackCandidate> members)
        {
            return members.Aggregate(1, (product, member) =>
                product * member.Observation.CellCount);
        }

        private static int EncoderTransitions(IEnumerable<ParameterPackCandidate> members)
        {
            return members.Aggregate(1, (product, member) =>
                product * Runs(member.Observation));
        }

        private static int Runs(ParameterObservation observation)
        {
            return Enumerable.Range(0, observation.CellCount)
                .Sum(cell => ValueRuns(observation, cell).Count);
        }

        /// <summary>Contiguous runs of network values that make up one cell.</summary>
        internal static IReadOnlyList<(float low, float high)> ValueRuns(
            ParameterObservation observation,
            int cell)
        {
            return Runs(observation, new HashSet<float>(observation.Cells[cell]), true);
        }

        /// <summary>Contiguous runs of network values outside one cell.</summary>
        internal static IReadOnlyList<(float low, float high)> OutsideRuns(
            ParameterObservation observation,
            int cell)
        {
            return Runs(observation, new HashSet<float>(observation.Cells[cell]), false);
        }

        private static IReadOnlyList<(float low, float high)> Runs(
            ParameterObservation observation,
            HashSet<float> cell,
            bool inside)
        {
            var values = ParameterCompressionObservability.NetworkValues(observation.ValueType);
            var runs = new List<(float, float)>();
            for (var index = 0; index < values.Length; index++)
            {
                if (cell.Contains(values[index]) != inside) continue;
                var start = index;
                while (index + 1 < values.Length &&
                       cell.Contains(values[index + 1]) == inside)
                    index++;
                runs.Add((values[start], values[index]));
            }
            return runs;
        }

        private static IEnumerable<int[]> RunCombinations(
            ParameterPackGroup group,
            IReadOnlyList<int> cells)
        {
            var runCounts = group.Members.Select((member, index) =>
                ValueRuns(member.Observation, cells[index]).Count).ToArray();
            var combination = new int[group.Members.Count];
            while (true)
            {
                yield return (int[])combination.Clone();
                var position = 0;
                while (position < combination.Length &&
                       ++combination[position] == runCounts[position])
                    combination[position++] = 0;
                if (position == combination.Length) yield break;
            }
        }

        /// <summary>
        /// Conditions that hold while the local value would be sent as a network
        /// value inside the run. Float bounds sit halfway between network values.
        /// Entering a cell widens them slightly so no local value falls between
        /// two runs; leaving one narrows them so a value on a boundary cannot
        /// bounce between the router and a code state.
        /// </summary>
        private static void AddRunConditions(
            AnimatorStateTransition transition,
            ParameterPackMember member,
            (float low, float high) run,
            bool widen)
        {
            var name = member.Candidate.Name;
            var type = member.Candidate.AnimatorType;
            var (low, high) = run;
            var values = ParameterCompressionObservability.NetworkValues(
                member.Observation.ValueType);
            var minimum = values[0];
            var maximum = values[values.Length - 1];

            switch (member.Observation.ValueType)
            {
                case VRCExpressionParameters.ValueType.Bool:
                    // Every cell of a two-cell Boolean is a single value.
                    if (type == AnimatorControllerParameterType.Bool)
                        transition.Require(low == 0f
                            ? AnimatorConditionMode.IfNot
                            : AnimatorConditionMode.If, 0f, name);
                    else if (type == AnimatorControllerParameterType.Int)
                        transition.Require(low == 0f
                            ? AnimatorConditionMode.Equals
                            : AnimatorConditionMode.NotEqual, 0f, name);
                    else
                        transition.Require(low == 0f
                            ? AnimatorConditionMode.Less
                            : AnimatorConditionMode.Greater, 0.5f, name);
                    return;

                case VRCExpressionParameters.ValueType.Int:
                    if (type == AnimatorControllerParameterType.Int)
                    {
                        if (low == high)
                        {
                            transition.Require(AnimatorConditionMode.Equals, low, name);
                            return;
                        }
                        if (low > minimum)
                            transition.Require(AnimatorConditionMode.Greater, low - 1f, name);
                        if (high < maximum)
                            transition.Require(AnimatorConditionMode.Less, high + 1f, name);
                        return;
                    }
                    if (low > minimum)
                        transition.Require(AnimatorConditionMode.Greater, low - 0.5f, name);
                    if (high < maximum)
                        transition.Require(AnimatorConditionMode.Less, high + 0.5f, name);
                    return;

                default:
                    const float halfStep = 0.5f / 127f;
                    var slack = widen ? FloatBoundarySlack : -FloatBoundarySlack;
                    if (low > minimum)
                        transition.Require(AnimatorConditionMode.Greater,
                            low - halfStep - slack, name);
                    if (high < maximum)
                        transition.Require(AnimatorConditionMode.Less,
                            high + halfStep + slack, name);
                    return;
            }
        }

        internal static AnimatorControllerParameterType EnsureIsLocal(
            AnimatorController controller)
        {
            var existing = controller.parameters.FirstOrDefault(parameter =>
                string.Equals(parameter.name, IsLocalName, StringComparison.Ordinal));
            if (existing == null)
            {
                controller.AppendParameter(new AnimatorControllerParameter
                {
                    name = IsLocalName,
                    type = AnimatorControllerParameterType.Bool,
                    defaultBool = true
                });
                return AnimatorControllerParameterType.Bool;
            }
            if (existing.type != AnimatorControllerParameterType.Bool &&
                existing.type != AnimatorControllerParameterType.Int &&
                existing.type != AnimatorControllerParameterType.Float)
                throw new InvalidOperationException(
                    "Animator parameter 'IsLocal' has an unsupported type.");
            return existing.type;
        }

        internal static void AddIsLocal(
            AnimatorStateTransition transition,
            AnimatorControllerParameterType isLocalType,
            bool local)
        {
            if (isLocalType == AnimatorControllerParameterType.Bool)
                transition.Require(
                    local ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                    0f, IsLocalName);
            else
                transition.Require(
                    local ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
                    0.5f, IsLocalName);
        }

        internal static void EnsureParameter(
            AnimatorController controller,
            string name,
            AnimatorControllerParameterType type,
            float defaultValue)
        {
            var existing = controller.parameters.FirstOrDefault(parameter =>
                string.Equals(parameter.name, name, StringComparison.Ordinal));
            if (existing != null)
            {
                if (existing.type != type)
                    throw new InvalidOperationException(
                        "Animator parameter '" + name + "' is both " +
                        existing.type + " and " + type + ".");
                return;
            }
            controller.AppendParameter(new AnimatorControllerParameter
            {
                name = name,
                type = type,
                defaultFloat = defaultValue,
                defaultInt = Mathf.RoundToInt(defaultValue),
                defaultBool = defaultValue != 0f
            });
        }

        private static void Refresh(GeneratedAnimatorLayer layer, AnimatorState state, AnimatorState idle)
        {
            var transition = layer.Transition(state, idle);
            transition.hasExitTime = true;
            transition.exitTime = 1f;
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            transition.canTransitionToSelf = false;
            transition.interruptionSource = TransitionInterruptionSource.None;
        }

        internal static void ConfigureImmediate(AnimatorStateTransition transition)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            transition.canTransitionToSelf = false;
            transition.interruptionSource = TransitionInterruptionSource.None;
        }

        internal static void AddDriver(
            GeneratedAnimatorLayer layer,
            AnimatorState state,
            string name,
            bool localOnly,
            IEnumerable<VRC_AvatarParameterDriver.Parameter> parameters)
        {
            var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
            driver.name = name;
            driver.localOnly = localOnly;
            driver.isEnabled = true;
            driver.parameters = parameters.ToList();
            layer.Keep(driver);
            state.behaviours = state.behaviours
                .Concat(new StateMachineBehaviour[] { driver })
                .ToArray();
        }

        internal static VRC_AvatarParameterDriver.Parameter Set(string name, float value)
        {
            return new VRC_AvatarParameterDriver.Parameter
            {
                name = name,
                type = VRC_AvatarParameterDriver.ChangeType.Set,
                value = value
            };
        }

        internal static void AddSubAsset(AnimatorController controller, UnityEngine.Object value)
        {
            if (value == null || AssetDatabase.Contains(value)) return;
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(controller))) return;
            value.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(value, controller);
        }

        // Generated states follow the controller's Write Defaults so they never mix modes.
        internal static bool DetermineWriteDefaults(AnimatorController controller)
        {
            var states = controller.layers
                .SelectMany(layer => EnumerateStates(layer.stateMachine))
                .ToArray();
            return states.Length == 0 || states.All(state => state.writeDefaultValues);
        }

        private static IEnumerable<AnimatorState> EnumerateStates(AnimatorStateMachine machine)
        {
            if (machine == null) yield break;
            foreach (var child in machine.states)
                if (child.state != null) yield return child.state;
            foreach (var child in machine.stateMachines)
            foreach (var state in EnumerateStates(child.stateMachine))
                yield return state;
        }
    }
}
