using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// One value sent as binary planes: Bools named Base1, Base2, Base4 ... and
    /// optionally BaseNegative. The planes weigh 1, 2, 4 ... in the decoder, so
    /// dropping the lowest planes is a coarser uniform quantizer of the same value.
    /// </summary>
    internal sealed class ParameterBitPlaneGroup
    {
        internal ParameterBitPlaneGroup(string name, IReadOnlyList<string> planes, string sign)
        {
            Name = name;
            Planes = planes;
            Sign = sign;
        }

        internal string Name { get; }

        /// <summary>Least significant plane first.</summary>
        internal IReadOnlyList<string> Planes { get; }

        internal string Sign { get; }
        internal int Bits => Planes.Count;

        /// <summary>Squared surface displacement, summed over every vertex, that a
        /// full-scale swing of the value causes on remote clients. Infinite when the
        /// value also drives something that is not geometry.</summary>
        internal double Weight { get; set; } = double.PositiveInfinity;

        internal int Dropped { get; set; }

        /// <summary>A value with no measurable remote effect stops syncing whole.</summary>
        internal bool FullyDropped => Dropped == Bits;

        internal int SavedBits => Dropped + (FullyDropped && Sign != null ? 1 : 0);
    }

    internal sealed class ParameterPrecisionPlan
    {
        internal static readonly ParameterPrecisionPlan None =
            new ParameterPrecisionPlan(Array.Empty<ParameterBitPlaneGroup>());

        internal ParameterPrecisionPlan(IReadOnlyList<ParameterBitPlaneGroup> groups)
        {
            Groups = groups.Where(group => group.Dropped > 0).ToArray();
        }

        internal IReadOnlyList<ParameterBitPlaneGroup> Groups { get; }
        internal bool Any => Groups.Count > 0;
        internal int SavedBits => Groups.Sum(group => group.SavedBits);

        internal IEnumerable<string> UnsyncedNames => Groups.SelectMany(group =>
            group.Planes.Take(group.Dropped)
                .Concat(group.FullyDropped && group.Sign != null
                    ? new[] { group.Sign }
                    : Array.Empty<string>()));

        /// <summary>Remote copies that rebuild each dropped plane by bit replication.</summary>
        internal IEnumerable<(string destination, string source)> Copies => Groups
            .Where(group => !group.FullyDropped)
            .SelectMany(group => Enumerable.Range(0, group.Dropped).Select(plane =>
                (group.Planes[plane], group.Planes[
                    ParameterPrecisionReduction.ReplicationSource(
                        group.Bits, group.Dropped, plane)])));

        internal string Identity => string.Join(",", Groups
            .OrderBy(group => group.Name, StringComparer.Ordinal)
            .Select(group => group.Name + "-" + group.Dropped));

        internal string Describe()
        {
            var silent = Groups.Where(group => group.FullyDropped).ToArray();
            var reduced = Groups.Where(group => !group.FullyDropped).ToArray();
            var parts = new List<string>();
            if (silent.Length > 0)
                parts.Add("Stopped syncing " + silent.Length + " values with no measurable " +
                          "effect on remote clients (" + silent.Sum(group => group.SavedBits) +
                          " bits: " + string.Join(", ", silent.Select(group => group.Name)) + ").");
            if (reduced.Length > 0)
                parts.Add("Lowered remote precision where it moves the avatar least (" +
                          reduced.Sum(group => group.SavedBits) + " bits: " +
                          string.Join(", ", reduced.Select(group =>
                              group.Name + " " + group.Bits + "->" + (group.Bits - group.Dropped))) +
                          ").");
            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// The lossy stage, used only after lossless reductions and the bus fall
    /// short. It is rate-distortion allocation over remotely decoded precision:
    /// each value's distortion weight is the surface motion it causes on a remote
    /// client, measured by simulating the final FX on a copy of the avatar, and
    /// planes are dropped greedily where the added squared displacement per bit is
    /// smallest (reverse water-filling). The wearer's client keeps every plane, and
    /// remote clients rebuild dropped planes by bit replication, which keeps zero
    /// and full scale exact.
    /// </summary>
    internal static class ParameterPrecisionReduction
    {
        private const string IsLocalName = "IsLocal";

        internal static IReadOnlyList<ParameterBitPlaneGroup> FindGroups(
            IEnumerable<string> syncedBools)
        {
            var names = new HashSet<string>(syncedBools, StringComparer.Ordinal);
            var groups = new List<ParameterBitPlaneGroup>();
            foreach (var first in names.Where(name => name.Length > 1 && name.EndsWith("1", StringComparison.Ordinal))
                         .OrderBy(name => name, StringComparer.Ordinal))
            {
                var stem = first.Substring(0, first.Length - 1);

                if (char.IsDigit(stem[stem.Length - 1])) continue;
                var planes = new List<string>();
                for (var weight = 1; weight <= 1 << 15 && names.Contains(stem + weight); weight *= 2)
                    planes.Add(stem + weight);
                if (planes.Count < 2) continue;
                var sign = names.Contains(stem + "Negative") ? stem + "Negative" : null;
                groups.Add(new ParameterBitPlaneGroup(stem, planes, sign));
            }
            return groups;
        }

        /// <summary>The n-bit code a remote client rebuilds from the kept top planes.</summary>
        internal static int Replicate(int code, int bits, int dropped)
        {
            if (dropped >= bits) return 0;
            var top = code >> dropped;
            var result = top << dropped;
            for (var plane = 0; plane < dropped; plane++)
                result |= (top >> (ReplicationSource(bits, dropped, plane) - dropped) & 1) << plane;
            return result;
        }

        /// <summary>The kept plane whose value a dropped plane copies: the kept code
        /// repeated below itself, most significant plane first.</summary>
        internal static int ReplicationSource(int bits, int dropped, int plane)
        {
            var kept = bits - dropped;
            return bits - 1 - (dropped - 1 - plane) % kept;
        }

        /// <summary>Mean squared error, as a fraction of full scale, over uniformly
        /// distributed codes (the maximum-entropy prior).</summary>
        internal static double ReplicationError(int bits, int dropped)
        {
            var count = 1 << bits;
            var scale = (double)(count - 1);
            var sum = 0d;
            for (var code = 0; code < count; code++)
            {
                var error = (code - Replicate(code, bits, dropped)) / scale;
                sum += error * error;
            }
            return sum / count;
        }

        /// <summary>
        /// Values with no measurable effect go first and whole. Then one plane at a
        /// time goes from the value whose added distortion is smallest, until the
        /// planner can fit the rest. Returns null when every allowed plane is gone
        /// and it still does not fit.
        /// </summary>
        internal static ParameterPrecisionPlan Allocate(
            IReadOnlyList<ParameterBitPlaneGroup> groups,
            Func<int, bool> fits)
        {
            foreach (var group in groups) group.Dropped = 0;
            foreach (var group in groups.Where(group => group.Weight == 0d))
                group.Dropped = group.Bits;
            var saved = groups.Sum(group => group.SavedBits);
            while (!fits(saved))
            {
                ParameterBitPlaneGroup best = null;
                var bestCost = double.PositiveInfinity;
                foreach (var group in groups)
                {
                    if (double.IsInfinity(group.Weight) || group.Dropped >= group.Bits - 1)
                        continue;
                    var cost = group.Weight * (ReplicationError(group.Bits, group.Dropped + 1) -
                                               ReplicationError(group.Bits, group.Dropped));
                    if (cost < bestCost ||
                        cost == bestCost && best != null &&
                        string.CompareOrdinal(group.Name, best.Name) < 0)
                    {
                        best = group;
                        bestCost = cost;
                    }
                }
                if (best == null) return null;
                best.Dropped++;
                saved++;
            }
            return new ParameterPrecisionPlan(groups);
        }

        /// <summary>
        /// One layer that, on remote clients only, copies each dropped plane from its
        /// replication source every frame. Drivers write values the way the network
        /// does, so the wearer's OSC inputs are never animated.
        /// </summary>
        internal static void Build(
            AnimatorController controller,
            string prefix,
            ParameterPrecisionPlan plan)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            var copies = plan?.Copies.ToArray() ?? Array.Empty<(string, string)>();
            if (copies.Length == 0) return;

            var isLocalType = ParameterCompressionPacking.EnsureIsLocal(controller);
            var writeDefaults = ParameterCompressionPacking.DetermineWriteDefaults(controller);
            var layer = new GeneratedAnimatorLayer(controller, "YUCP Parameter Compressor Precision");
            var idle = layer.State("Wearer");
            idle.writeDefaultValues = writeDefaults;
            layer.DefaultState = idle;
            // Entering a state runs its driver, so alternating two states copies every frame.
            var even = layer.State("Replicate A");
            var odd = layer.State("Replicate B");
            foreach (var state in new[] { even, odd })
            {
                state.writeDefaultValues = writeDefaults;
                var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
                driver.name = "YUCP Parameter Compressor Precision Decoder";
                driver.localOnly = false;
                driver.isEnabled = true;
                driver.parameters = copies.Select(copy => new VRC_AvatarParameterDriver.Parameter
                {
                    type = VRC_AvatarParameterDriver.ChangeType.Copy,
                    source = copy.Item2,
                    name = copy.Item1,
                    convertRange = false
                }).ToList();
                state.behaviours = new StateMachineBehaviour[] { layer.Keep(driver) };
            }
            foreach (var (from, to) in new[] { (idle, even), (even, odd), (odd, even) })
            {
                var transition = layer.Transition(from, to);
                transition.duration = 0f;
                transition.canTransitionToSelf = false;
                ParameterCompressionPacking.AddIsLocal(transition, isLocalType, false);
            }
            layer.Commit(0f);
        }
    }

    /// <summary>
    /// Measures how far each bit-plane value moves what remote clients render.
    /// A static pass follows every blend-tree leaf, driver and animated parameter
    /// a value can influence. A value that reaches anything other than blend
    /// shapes is never reduced. The same pass names the synced Booleans that gate
    /// those leaves and, from the conditions and weights in the way, which state
    /// of each gate shows them. A dynamic pass then plays only the dependent FX
    /// layers as a remote client on a hidden copy of the avatar. Values whose
    /// blend shapes do not overlap swing together, each settled value is
    /// extrapolated from three frames, and a value's weight is the squared
    /// surface displacement its own shapes add, from their sparse vertex deltas.
    /// </summary>
    internal static class ParameterCompressionSensitivity
    {
        private const float Step = 1f / 60f;
        private const int MinimumSteps = 8;
        private const int MaximumSteps = 600;
        private const float Still = 0.001f;
        private const float Agreement = 0.01f;
        private const float SlowestDecay = 0.99f;
        private const double NoiseFloor = 1e-6;




        private const int ExhaustiveGates = 3;


        private const int Samples = 4;

        private sealed class Leaf
        {
            internal readonly HashSet<string> influencers = new HashSet<string>(StringComparer.Ordinal);
            internal readonly HashSet<string> writes = new HashSet<string>(StringComparer.Ordinal);
            internal readonly List<EditorCurveBinding> shapes = new List<EditorCurveBinding>();

            /// <summary>Direct blend-tree weights on the way down to this clip.</summary>
            internal readonly HashSet<string> weights = new HashSet<string>(StringComparer.Ordinal);

            internal bool other;
            internal int layer;
            internal AnimatorState state;
            internal AnimationClip clip;

            /// <summary>The Direct blend-tree weight this clip sits under, if any.</summary>
            internal string weight;
        }

        /// <summary>One animated blend shape and its vertex deltas per unit of weight.</summary>
        private sealed class Shape
        {
            internal SkinnedMeshRenderer renderer;
            internal int index;
            internal int slot;
            internal int[] vertices;
            internal Vector3[] deltas;
        }

        internal static void Measure(
            GameObject avatarRoot,
            AnimatorController fx,
            VRCExpressionParameters parameters,
            IReadOnlyList<ParameterBitPlaneGroup> groups,
            Action<string, float> report = null)
        {
            if (groups.Count == 0) return;
            report = report ?? ((message, progress) => { });
            report("Tracing what each of " + groups.Count + " bit-plane values can move", 0f);
            var synced = (parameters.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>())
                .Where(parameter => parameter != null && parameter.networkSynced)
                .GroupBy(parameter => parameter.name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var owned = new HashSet<string>(groups.SelectMany(Owned), StringComparer.Ordinal);
            var leaves = CollectLeaves(fx, out var incoming);
            var reach = new Dictionary<ParameterBitPlaneGroup, Leaf[]>();
            foreach (var group in groups)
            {
                var reached = Reach(leaves, Owned(group));
                reach[group] = reached;
                group.Weight = reached.Any(leaf => leaf.other) || !BinaryDecoded(group, leaves)
                    ? double.PositiveInfinity
                    : reached.Any(leaf => leaf.shapes.Count > 0) ? double.NaN : 0d;
            }
            var measured = groups.Where(group => double.IsNaN(group.Weight)).ToArray();
            if (measured.Length == 0) return;

            bool IsGate(string name) => !owned.Contains(name) &&
                synced.TryGetValue(name, out var parameter) &&
                parameter.valueType == VRCExpressionParameters.ValueType.Bool;
            var gates = measured.ToDictionary(group => group, group => reach[group]
                .SelectMany(leaf => leaf.influencers).Where(IsGate).Distinct()
                .OrderBy(name => name, StringComparer.Ordinal).ToArray());

            report("Preparing a remote-client simulation", 0.03f);
            var controller = Prune(fx, leaves, measured.SelectMany(group => reach[group]));
            var clone = UnityEngine.Object.Instantiate(avatarRoot);
            clone.hideFlags = HideFlags.HideAndDontSave;
            var graph = PlayableGraph.Create("YUCP Parameter Compressor Sensitivity");
            try
            {
                var animator = clone.GetComponent<Animator>();
                if (animator == null)
                    throw new InvalidOperationException("The avatar has no Animator to simulate.");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimatorControllerPlayable.Create(graph, controller);
                AnimationPlayableOutput.Create(graph, "FX", animator).SetSourcePlayable(playable);
                graph.Evaluate(0f);

                var types = controller.parameters.ToDictionary(
                    parameter => parameter.name, parameter => parameter.type, StringComparer.Ordinal);
                void Set(string name, float value)
                {
                    if (!types.TryGetValue(name, out var type)) return;
                    if (type == AnimatorControllerParameterType.Bool) playable.SetBool(name, value != 0f);
                    else if (type == AnimatorControllerParameterType.Int) playable.SetInteger(name, Mathf.RoundToInt(value));
                    else if (type == AnimatorControllerParameterType.Float) playable.SetFloat(name, value);
                }
                void SetCode(ParameterBitPlaneGroup group, int code, bool negative)
                {
                    for (var plane = 0; plane < group.Bits; plane++)
                        Set(group.Planes[plane], code >> plane & 1);
                    if (group.Sign != null) Set(group.Sign, negative ? 1f : 0f);
                }

                var shapes = Shapes(clone, measured.SelectMany(group => reach[group]).SelectMany(leaf => leaf.shapes));
                var own = measured.ToDictionary(group => group, group => reach[group]
                    .SelectMany(leaf => leaf.shapes).Select(binding => Find(shapes, clone, binding))
                    .Where(shape => shape != null).Distinct().ToArray());
                var accumulators = shapes.Values.Select(shape => shape.renderer).Distinct()
                    .ToDictionary(renderer => renderer, renderer => new Vector3[renderer.sharedMesh.vertexCount]);
                var probes = shapes.Values.OrderBy(shape => shape.slot).ToArray();
                float[] Probe() => probes.Select(shape => shape.renderer.GetBlendShapeWeight(shape.index)).ToArray();





                float[] Settle()
                {
                    var a = Probe();
                    graph.Evaluate(Step);
                    var b = Probe();
                    graph.Evaluate(Step);
                    var c = Probe();
                    float[] previous = null;
                    var agreed = 0;
                    for (var steps = 2; steps < MaximumSteps; steps++)
                    {
                        var estimate = new float[c.Length];
                        var regular = true;
                        for (var index = 0; index < c.Length; index++)
                        {
                            var before = b[index] - a[index];
                            var last = c[index] - b[index];
                            estimate[index] = c[index];
                            if (Mathf.Abs(last) < Still) continue;
                            var ratio = before == 0f ? float.PositiveInfinity : last / before;
                            if (ratio <= 0f || ratio >= SlowestDecay) regular = false;
                            else estimate[index] = c[index] + last * ratio / (1f - ratio);
                        }
                        agreed = regular && previous != null &&
                                 estimate.Select((value, index) => Mathf.Abs(value - previous[index]))
                                     .DefaultIfEmpty(0f).Max() < Agreement
                            ? agreed + 1
                            : 0;
                        if (agreed >= 2 && steps >= MinimumSteps) return estimate;
                        previous = estimate;
                        a = b;
                        b = c;
                        graph.Evaluate(Step);
                        c = Probe();
                    }
                    return c;
                }


                double Energy(IEnumerable<Shape> moved, float[] from, float[] to)
                {
                    var touched = new List<(Vector3[] buffer, int vertex)>();
                    foreach (var shape in moved)
                    {
                        var change = to[shape.slot] - from[shape.slot];
                        if (change == 0f) continue;
                        var buffer = accumulators[shape.renderer];
                        for (var index = 0; index < shape.vertices.Length; index++)
                        {
                            var vertex = shape.vertices[index];
                            if (buffer[vertex] == Vector3.zero) touched.Add((buffer, vertex));
                            buffer[vertex] += shape.deltas[index] * change;
                        }
                    }
                    var sum = 0d;
                    foreach (var (buffer, vertex) in touched)
                    {
                        sum += buffer[vertex].sqrMagnitude;
                        buffer[vertex] = Vector3.zero;
                    }
                    return sum;
                }

                var random = new System.Random(0x59C);
                Dictionary<ParameterBitPlaneGroup, (int code, bool negative)> operatingPoint = null;
                void Context(IReadOnlyDictionary<string, bool> assignment)
                {
                    foreach (var parameter in synced.Values.Where(parameter => !owned.Contains(parameter.name)))
                        Set(parameter.name, assignment.TryGetValue(parameter.name, out var on)
                            ? on ? 1f : 0f
                            : parameter.defaultValue);
                    Set("IsLocal", 0f);
                    operatingPoint = groups.ToDictionary(group => group,
                        group => (code: random.Next(1 << group.Bits), negative: random.Next(2) == 0));
                    foreach (var pair in operatingPoint)
                        SetCode(pair.Key, pair.Value.code, pair.Value.negative);
                }





                void MeasureBatches(IReadOnlyDictionary<string, bool> assignment,
                    IReadOnlyList<ParameterBitPlaneGroup> values, string label, float start, float span)
                {
                    var batches = Batches(values, own);
                    foreach (var group in values) group.Weight = 0d;
                    for (var sample = 0; sample < Samples; sample++)
                    {
                        Context(assignment);
                        for (var index = 0; index < batches.Count; index++)
                        {
                            var batch = batches[index];
                            report(label + " - operating point " + (sample + 1) + "/" + Samples +
                                   ", batch " + (index + 1) + "/" + batches.Count + ": " +
                                   string.Join(", ", batch.Select(group => group.Name)),
                                start + span * (sample * batches.Count + index) / (Samples * batches.Count));
                            foreach (var group in batch) SetCode(group, 0, false);
                            var rest = Settle();
                            foreach (var group in batch) SetCode(group, (1 << group.Bits) - 1, false);
                            var positive = Settle();
                            var energies = batch.ToDictionary(group => group,
                                group => Energy(own[group], rest, positive));
                            var signed = batch.Where(group => group.Sign != null).ToArray();
                            if (signed.Length > 0)
                            {
                                foreach (var group in signed) SetCode(group, (1 << group.Bits) - 1, true);
                                var negative = Settle();
                                foreach (var group in signed)
                                    energies[group] = (energies[group] + Energy(own[group], rest, negative)) / 2d;
                            }
                            foreach (var group in batch)
                            {
                                group.Weight += energies[group] / Samples;
                                SetCode(group, operatingPoint[group].code, operatingPoint[group].negative);
                            }
                        }
                    }
                }

                var contexts = Contexts(measured, group => Prefer(reach[group], gates[group], incoming));
                for (var index = 0; index < contexts.Count; index++)
                {
                    var (assignment, values) = contexts[index];
                    MeasureBatches(assignment, values, "Context " + (index + 1) + "/" + contexts.Count +
                        (assignment.Count == 0
                            ? " (defaults)"
                            : " (" + string.Join(", ", assignment.Select(pair => pair.Key + (pair.Value ? " on" : " off"))) + ")"),
                        0.05f + 0.8f * index / contexts.Count, 0.8f / contexts.Count);
                }



                var strongest = measured.Max(group => group.Weight);
                var hidden = measured.GroupBy(group => string.Join("|", gates[group]))
                    .Where(set => set.Key.Length > 0 && set.All(group => group.Weight < strongest * NoiseFloor))
                    .ToArray();
                for (var index = 0; index < hidden.Length; index++)
                {
                    var names = hidden[index].Key.Split('|');
                    var values = hidden[index].ToArray();
                    var shown = values.SelectMany(group => own[group]).Distinct().ToArray();
                    report("Searching which of " + string.Join(", ", names) + " reveal " +
                           string.Join(", ", values.Select(group => group.Name)),
                        0.85f + 0.1f * index / hidden.Length);
                    var tried = new Dictionary<int, double>();
                    double Activity(int mask)
                    {
                        if (tried.TryGetValue(mask, out var known)) return known;
                        Context(names.Select((name, gate) => (name, gate))
                            .ToDictionary(pair => pair.name, pair => (mask >> pair.gate & 1) != 0));
                        foreach (var group in values) SetCode(group, 0, false);
                        var rest = Settle();
                        foreach (var group in values) SetCode(group, (1 << group.Bits) - 1, false);
                        return tried[mask] = Energy(shown, rest, Settle());
                    }
                    var defaults = 0;
                    for (var gate = 0; gate < names.Length; gate++)
                        if (synced[names[gate]].defaultValue != 0f) defaults |= 1 << gate;
                    var all = (1 << names.Length) - 1;
                    var starts = names.Length <= ExhaustiveGates
                        ? Enumerable.Range(0, all + 1)
                        : new[] { defaults, all, 0 };
                    var best = starts.Distinct().OrderByDescending(Activity).ThenBy(mask => mask).First();
                    if (names.Length > ExhaustiveGates)
                        for (var gate = 0; gate < names.Length; gate++)
                            if (Activity(best ^ 1 << gate) > Activity(best)) best ^= 1 << gate;
                    MeasureBatches(names.Select((name, gate) => (name, gate))
                            .ToDictionary(pair => pair.name, pair => (best >> pair.gate & 1) != 0),
                        values, "Measuring " + values.Length + " revealed values",
                        0.85f + 0.1f * index / hidden.Length, 0.1f / hidden.Length);
                }



                strongest = measured.Max(group => group.Weight);
                foreach (var group in measured)
                    if (group.Weight < strongest * NoiseFloor) group.Weight = 0d;
                report("Measured " + measured.Length + " values", 1f);
            }
            finally
            {
                graph.Destroy();
                UnityEngine.Object.DestroyImmediate(clone);
                if (controller != fx) UnityEngine.Object.DestroyImmediate(controller);
                foreach (var group in groups.Where(group => double.IsNaN(group.Weight)))
                    group.Weight = double.PositiveInfinity;
            }
        }

        private static IEnumerable<string> Owned(ParameterBitPlaneGroup group)
        {
            return group.Sign != null ? group.Planes.Concat(new[] { group.Sign }) : group.Planes;
        }

        /// <summary>
        /// Which state of each gate shows these leaves: a transition into their
        /// state that needs it on or off, or a Direct blend weight above them.
        /// Gates nothing votes on keep their defaults.
        /// </summary>
        private static Dictionary<string, bool> Prefer(
            IEnumerable<Leaf> reached,
            IReadOnlyCollection<string> gates,
            IReadOnlyDictionary<AnimatorState, List<AnimatorCondition[]>> incoming)
        {
            var votes = gates.ToDictionary(gate => gate, _ => 0, StringComparer.Ordinal);
            foreach (var leaf in reached.Where(leaf => leaf.shapes.Count > 0))
            {
                foreach (var weight in leaf.weights)
                    if (votes.ContainsKey(weight)) votes[weight]++;
                if (leaf.state == null || !incoming.TryGetValue(leaf.state, out var entries)) continue;
                foreach (var condition in entries.SelectMany(conditions => conditions))
                    if (votes.ContainsKey(condition.parameter))
                        votes[condition.parameter] += Wants(condition);
            }
            return votes.Where(pair => pair.Value != 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value > 0, StringComparer.Ordinal);
        }

        private static int Wants(AnimatorCondition condition)
        {
            switch (condition.mode)
            {
                case AnimatorConditionMode.If: return 1;
                case AnimatorConditionMode.IfNot: return -1;
                case AnimatorConditionMode.Greater: return condition.threshold < 1f ? 1 : 0;
                case AnimatorConditionMode.Less: return condition.threshold > 0f ? -1 : 0;
                case AnimatorConditionMode.Equals: return condition.threshold == 1f ? 1 : condition.threshold == 0f ? -1 : 0;
                case AnimatorConditionMode.NotEqual: return condition.threshold == 0f ? 1 : condition.threshold == 1f ? -1 : 0;
                default: return 0;
            }
        }

        /// <summary>Values whose preferred gates agree share one simulated context.</summary>
        private static List<(Dictionary<string, bool> assignment, List<ParameterBitPlaneGroup> values)> Contexts(
            IEnumerable<ParameterBitPlaneGroup> values,
            Func<ParameterBitPlaneGroup, Dictionary<string, bool>> prefer)
        {
            var contexts = new List<(Dictionary<string, bool>, List<ParameterBitPlaneGroup>)>();
            foreach (var value in values)
            {
                var wanted = prefer(value);
                var match = contexts.FirstOrDefault(context => wanted.All(pair =>
                    !context.Item1.TryGetValue(pair.Key, out var on) || on == pair.Value));
                if (match.Item1 == null)
                {
                    contexts.Add((new Dictionary<string, bool>(wanted, StringComparer.Ordinal),
                        new List<ParameterBitPlaneGroup> { value }));
                    continue;
                }
                foreach (var pair in wanted) match.Item1[pair.Key] = pair.Value;
                match.Item2.Add(value);
            }
            return contexts;
        }

        /// <summary>Greedy colouring: values that share no blend shape swing together.</summary>
        private static List<List<ParameterBitPlaneGroup>> Batches(
            IEnumerable<ParameterBitPlaneGroup> values,
            IReadOnlyDictionary<ParameterBitPlaneGroup, Shape[]> own)
        {
            var batches = new List<(HashSet<Shape> used, List<ParameterBitPlaneGroup> members)>();
            foreach (var value in values.OrderByDescending(value => own[value].Length)
                         .ThenBy(value => value.Name, StringComparer.Ordinal))
            {
                var batch = batches.FirstOrDefault(candidate => !candidate.used.Overlaps(own[value]));
                if (batch.used == null)
                {
                    batch = (new HashSet<Shape>(), new List<ParameterBitPlaneGroup>());
                    batches.Add(batch);
                }
                batch.used.UnionWith(own[value]);
                batch.members.Add(value);
            }
            return batches.Select(batch => batch.members).ToList();
        }

        private static Leaf[] CollectLeaves(
            AnimatorController fx,
            out Dictionary<AnimatorState, List<AnimatorCondition[]>> incoming)
        {
            var leaves = new List<Leaf>();
            var entering = new Dictionary<AnimatorState, List<AnimatorCondition[]>>();
            void Enter(AnimatorState state, AnimatorCondition[] conditions)
            {
                if (state == null) return;
                if (!entering.TryGetValue(state, out var list)) entering[state] = list = new List<AnimatorCondition[]>();
                list.Add(conditions);
            }
            for (var layerIndex = 0; layerIndex < fx.layers.Length; layerIndex++)
            {
                var machine = fx.layers[layerIndex].stateMachine;
                var gates = new HashSet<string>(StringComparer.Ordinal);
                var states = new List<AnimatorState>();
                void Visit(AnimatorStateMachine current)
                {
                    if (current == null) return;
                    foreach (var transition in current.anyStateTransitions)
                    {
                        foreach (var condition in transition.conditions) gates.Add(condition.parameter);
                        Enter(transition.destinationState, transition.conditions);
                    }
                    foreach (var transition in current.entryTransitions)
                    {
                        foreach (var condition in transition.conditions) gates.Add(condition.parameter);
                        Enter(transition.destinationState, transition.conditions);
                    }
                    foreach (var child in current.states)
                    {
                        if (child.state == null) continue;
                        states.Add(child.state);
                        foreach (var transition in child.state.transitions)
                        {
                            foreach (var condition in transition.conditions) gates.Add(condition.parameter);
                            Enter(transition.destinationState, transition.conditions);
                        }
                    }
                    foreach (var child in current.stateMachines)
                    {
                        foreach (var transition in current.GetStateMachineTransitions(child.stateMachine))
                            foreach (var condition in transition.conditions) gates.Add(condition.parameter);
                        Visit(child.stateMachine);
                    }
                }
                Visit(machine);

                foreach (var state in states)
                {
                    var stateGates = new HashSet<string>(gates, StringComparer.Ordinal);
                    if (state.speedParameterActive) stateGates.Add(state.speedParameter);
                    if (state.timeParameterActive) stateGates.Add(state.timeParameter);
                    if (state.mirrorParameterActive) stateGates.Add(state.mirrorParameter);
                    if (state.cycleOffsetParameterActive) stateGates.Add(state.cycleOffsetParameter);
                    Walk(state.motion, stateGates, new HashSet<string>(StringComparer.Ordinal), null,
                        layerIndex, state, leaves);

                    foreach (var behaviour in state.behaviours)
                    {
                        var leaf = new Leaf { layer = layerIndex, state = state };
                        leaf.influencers.UnionWith(stateGates);
                        if (behaviour is VRCAvatarParameterDriver driver)
                        {
                            foreach (var write in driver.parameters ?? new List<VRC_AvatarParameterDriver.Parameter>())
                            {
                                if (write == null) continue;
                                leaf.writes.Add(write.name);
                                if (write.type == VRC_AvatarParameterDriver.ChangeType.Copy)
                                    leaf.influencers.Add(write.source);
                            }
                        }
                        else if (behaviour != null)
                        {

                            leaf.other = true;
                        }
                        leaves.Add(leaf);
                    }
                }
            }
            incoming = entering;
            return leaves.ToArray();
        }

        private static void Walk(Motion motion, HashSet<string> influencers, HashSet<string> weights,
            string weight, int layer, AnimatorState state, List<Leaf> leaves)
        {
            if (motion is AnimationClip clip)
            {
                var leaf = new Leaf { layer = layer, state = state, clip = clip, weight = weight };
                leaf.influencers.UnionWith(influencers);
                leaf.weights.UnionWith(weights);
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type == typeof(Animator) && string.IsNullOrEmpty(binding.path))
                        leaf.writes.Add(binding.propertyName);
                    else if (binding.type == typeof(SkinnedMeshRenderer) &&
                             binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                        leaf.shapes.Add(binding);


                    else leaf.other = true;
                }
                if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0) leaf.other = true;
                leaves.Add(leaf);
                return;
            }
            if (!(motion is BlendTree tree)) return;
            foreach (var child in tree.children)
            {
                var next = new HashSet<string>(influencers, StringComparer.Ordinal);
                var nextWeights = weights;
                var direct = tree.blendType == BlendTreeType.Direct;
                if (direct)
                {
                    next.Add(child.directBlendParameter);
                    nextWeights = new HashSet<string>(weights, StringComparer.Ordinal) { child.directBlendParameter };
                }
                else
                {
                    next.Add(tree.blendParameter);
                    if (tree.blendType != BlendTreeType.Simple1D) next.Add(tree.blendParameterY);
                }
                Walk(child.motion, next, nextWeights, direct ? child.directBlendParameter : null,
                    layer, state, leaves);
            }
        }

        /// <summary>
        /// Whether the planes really are one binary number: some output is driven
        /// by a Direct blend-tree child per plane, with values in the ratio 1:2:4.
        /// Separate toggles that merely share a name pattern fail this.
        /// </summary>
        private static bool BinaryDecoded(ParameterBitPlaneGroup group, IReadOnlyList<Leaf> leaves)
        {
            var byPlane = group.Planes.Select(plane => leaves
                .Where(leaf => leaf.clip != null && leaf.weight == plane).ToArray()).ToArray();
            if (byPlane.Any(planeLeaves => planeLeaves.Length == 0)) return false;
            float? Value(Leaf[] planeLeaves, EditorCurveBinding binding)
            {
                foreach (var leaf in planeLeaves)
                {
                    var curve = AnimationUtility.GetEditorCurve(leaf.clip, binding);
                    if (curve != null && curve.length > 0) return curve.keys[0].value;
                }
                return null;
            }
            foreach (var binding in byPlane[0].SelectMany(leaf => AnimationUtility.GetCurveBindings(leaf.clip)).Distinct())
            {
                var unit = Value(byPlane[0], binding);
                if (unit == null || Mathf.Abs(unit.Value) < 1e-6f) continue;
                var binary = true;
                for (var plane = 1; plane < byPlane.Length && binary; plane++)
                {
                    var value = Value(byPlane[plane], binding);
                    binary = value != null &&
                             Mathf.Abs(value.Value / unit.Value - (1 << plane)) <= 0.01f * (1 << plane);
                }
                if (binary) return true;
            }
            return false;
        }

        /// <summary>Every leaf whose output can change when any seed changes.</summary>
        private static Leaf[] Reach(IReadOnlyList<Leaf> leaves, IEnumerable<string> seeds)
        {
            var influenced = new HashSet<string>(seeds, StringComparer.Ordinal);
            var reached = new HashSet<Leaf>();
            for (var changed = true; changed;)
            {
                changed = false;
                foreach (var leaf in leaves)
                {
                    if (reached.Contains(leaf) || !leaf.influencers.Overlaps(influenced)) continue;
                    reached.Add(leaf);
                    influenced.UnionWith(leaf.writes);
                    changed = true;
                }
            }
            return reached.ToArray();
        }

        /// <summary>
        /// The layers holding reached leaves, plus every layer with a leaf that
        /// writes a value those leaves (transitively) read, in their original
        /// order. Leaves that feed nothing measured keep default inputs, and
        /// dropping a layer that overrides the same outputs can only overstate an
        /// effect, which keeps more precision.
        /// </summary>
        private static AnimatorController Prune(AnimatorController fx, IReadOnlyList<Leaf> leaves, IEnumerable<Leaf> reached)
        {
            if (fx.layers.Any(layer => layer.syncedLayerIndex >= 0)) return fx;
            var feeding = new HashSet<Leaf>(reached);
            var keep = new HashSet<int>(feeding.Select(leaf => leaf.layer));
            var needed = new HashSet<string>(feeding.SelectMany(leaf => leaf.influencers), StringComparer.Ordinal);
            for (var changed = true; changed;)
            {
                changed = false;
                foreach (var leaf in leaves)
                {
                    if (feeding.Contains(leaf) || !leaf.writes.Overlaps(needed)) continue;
                    feeding.Add(leaf);
                    keep.Add(leaf.layer);
                    needed.UnionWith(leaf.influencers);
                    changed = true;
                }
            }
            return new AnimatorController
            {
                name = fx.name + " (sensitivity)",
                parameters = fx.parameters,
                layers = fx.layers.Where((layer, index) => keep.Contains(index)).ToArray()
            };
        }

        /// <summary>Each animated blend shape on the copy, with its nonzero vertex
        /// deltas per unit of weight, scaled like a baked mesh.</summary>
        private static Dictionary<(SkinnedMeshRenderer, int), Shape> Shapes(
            GameObject root,
            IEnumerable<EditorCurveBinding> bindings)
        {
            var shapes = new Dictionary<(SkinnedMeshRenderer, int), Shape>();
            foreach (var binding in bindings.Distinct())
            {
                var key = Resolve(root, binding);
                if (key == null || shapes.ContainsKey(key.Value)) continue;
                var (renderer, index) = key.Value;
                var mesh = renderer.sharedMesh;
                var frame = mesh.GetBlendShapeFrameCount(index) - 1;
                var deltas = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(index, frame, deltas, null, null);
                var scale = renderer.transform.lossyScale / mesh.GetBlendShapeFrameWeight(index, frame);
                var vertices = new List<int>();
                var values = new List<Vector3>();
                for (var vertex = 0; vertex < deltas.Length; vertex++)
                {
                    if (deltas[vertex] == Vector3.zero) continue;
                    vertices.Add(vertex);
                    values.Add(Vector3.Scale(deltas[vertex], scale));
                }
                shapes[key.Value] = new Shape
                {
                    renderer = renderer,
                    index = index,
                    slot = shapes.Count,
                    vertices = vertices.ToArray(),
                    deltas = values.ToArray()
                };
            }
            return shapes;
        }

        private static Shape Find(
            IReadOnlyDictionary<(SkinnedMeshRenderer, int), Shape> shapes,
            GameObject root,
            EditorCurveBinding binding)
        {
            var key = Resolve(root, binding);
            return key != null && shapes.TryGetValue(key.Value, out var shape) ? shape : null;
        }

        private static (SkinnedMeshRenderer, int)? Resolve(GameObject root, EditorCurveBinding binding)
        {
            var target = string.IsNullOrEmpty(binding.path) ? root.transform : root.transform.Find(binding.path);
            if (target == null || !target.TryGetComponent<SkinnedMeshRenderer>(out var renderer) ||
                renderer.sharedMesh == null)
                return null;
            var index = renderer.sharedMesh.GetBlendShapeIndex(
                binding.propertyName.Substring("blendShape.".Length));
            return index < 0 ? ((SkinnedMeshRenderer, int)?)null : (renderer, index);
        }
    }
}
