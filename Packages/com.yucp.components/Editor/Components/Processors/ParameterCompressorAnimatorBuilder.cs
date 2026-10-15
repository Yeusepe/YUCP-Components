#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDKBase;
using static YUCP.Components.Editor.ParameterCompressionPacking;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Event bus. Every VRChat sync tick carries one whole message, an Int code plus
    /// page Bools that name a value and its level, so a remote decodes each tick on
    /// its own: no framing, staging or commit. The wearer sends values whose remote
    /// copy is stale first (send-on-delta, https://doi.org/10.3390/s6010049) and
    /// spends idle ticks refreshing every value in turn for late joiners, which is
    /// max-age-first for equal weights (https://arxiv.org/abs/1801.01803). A stale
    /// value preempts a refresh still on the wire, never another stale value: the
    /// freshest update goes first (https://arxiv.org/abs/1608.08622), and a refresh
    /// carries nothing the remote is missing.
    /// </summary>
    internal static class ParameterCompressorAnimatorBuilder
    {
        private const string LayerName = "YUCP Parameter Compressor";
        private const string Sender = LayerName + " Sender";
        private const string Receiver = LayerName + " Receiver";

        // Playable sync sends at most every 0.1 s while values change, so each message
        // is held that long plus the extra frame VRCFury also waits.
        // Taken from: https://creators.vrchat.com/avatars/animator-parameters/
        internal const float HoldSeconds = 0.1f;
        internal const float TickSeconds = HoldSeconds + 2f / 60f;

        // A remote reads the same value's code again this often, so a dragged radial
        // shows each new level within half a hold.
        internal const float ReadSeconds = HoldSeconds / 2f;

        internal sealed class Entry
        {
            public string name;
            public AnimatorControllerParameterType type;
            public int levels;
            public float minimum;
            public float maximum;
            public float defaultValue;
            public int page;
            public int offset;
        }

        internal sealed class Request
        {
            public AnimatorController controller;
            public string prefix;
            public int pageBits;
            public IReadOnlyList<Entry> entries;
        }

        internal sealed class Result
        {
            public IReadOnlyList<string> carrierParameters;
            public int generatedStates;
            public float estimatedFullRefreshSeconds;
        }

        internal static Result Build(Request request)
        {
            var controller = request.controller;
            var entries = request.entries;
            if (entries == null || entries.Count == 0)
                throw new ArgumentException("The bus needs at least one value.", nameof(request));
            foreach (var entry in entries)
                if (entry.type != AnimatorControllerParameterType.Bool &&
                    !(entry.maximum > entry.minimum))
                    throw new ArgumentException("'" + entry.name + "' needs an increasing range.");

            var prefix = ParameterCompressionContract.NormalizePrefix(request.prefix);
            var code = prefix + "/_Bus/Code";
            var pages = Enumerable.Range(0, request.pageBits)
                .Select(bit => prefix + "/_Bus/Page" + bit).ToArray();
            var refresh = prefix + "/_Internal/Refresh";
            var clock = prefix + "/_Internal/Clock";
            var isLocalType = EnsureIsLocal(controller);
            var writeDefaults = DetermineWriteDefaults(controller);

            // The carrier defaults to "first value = its default", so a remote that decodes
            // it before the wearer's first message changes nothing.
            var first = entries.First(entry => entry.page == 0 && entry.offset == 0);
            EnsureParameter(controller, code, AnimatorControllerParameterType.Int,
                Level(first, first.defaultValue));
            foreach (var page in pages)
                EnsureParameter(controller, page, AnimatorControllerParameterType.Bool, 0f);
            EnsureParameter(controller, refresh, AnimatorControllerParameterType.Int, 0f);
            EnsureParameter(controller, clock, AnimatorControllerParameterType.Float, 0f);

            var layer = new GeneratedAnimatorLayer(controller, LayerName);
            AnimationClip Timer(string name, float seconds)
            {
                var clip = layer.Keep(new AnimationClip { name = name, frameRate = 60f });
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), clock),
                    AnimationCurve.Linear(0f, 0f, seconds, seconds));
                return clip;
            }
            var hold = Timer("YUCP Compressor Hold", HoldSeconds);
            var read = Timer("YUCP Compressor Read", ReadSeconds);

            var localStates = new List<AnimatorState>();
            var remoteStates = new List<AnimatorState>();
            AnimatorState State(string name, Motion motion, List<AnimatorState> role)
            {
                var state = layer.State(name, motion);
                state.writeDefaultValues = writeDefaults;
                role?.Add(state);
                return state;
            }

            var roleEntry = State("Role Entry", null, null);
            layer.DefaultState = roleEntry;
            // The router also holds each refresh on the wire, so a stale value can cut in.
            var router = State("Send Router", hold, localStates);
            var listen = State("Remote Listen", null, remoteStates);
            // NO CAMBIES: the role switch must be each state's first transition, so a
            // remote never runs the always-valid refresh routes in the sender.
            SwitchRole(layer, roleEntry, router, isLocalType, true);
            SwitchRole(layer, roleEntry, listen, isLocalType, false);
            SwitchRole(layer, router, listen, isLocalType, false);
            SwitchRole(layer, listen, router, isLocalType, true);

            var urgent = new List<AnimatorState>();
            var dirty = new List<(AnimatorState send, Action<AnimatorStateTransition> when)>();
            var periodic = new List<(AnimatorState send, Action<AnimatorStateTransition> when)>();
            // Same driver twice: the stale send holds the wire itself, the refresh turn
            // goes back to the router to hold there.
            (AnimatorState stale, AnimatorState turn) Send(
                string name, IEnumerable<VRC_AvatarParameterDriver.Parameter> writes)
            {
                var parameters = writes.ToList();
                var stale = State("Send " + name, hold, localStates);
                var turn = State("Refresh " + name, null, localStates);
                AddDriver(layer, stale, Sender, true, parameters);
                AddDriver(layer, turn, Sender, true, parameters);
                urgent.Add(stale);
                return (stale, turn);
            }
            var deltas = new List<(Entry entry, string shadow, string delta)>();
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var shadow = prefix + "/_Internal/Shadow/" + index;
                EnsureParameter(controller, entry.name, entry.type, entry.defaultValue);
                EnsureParameter(controller, shadow, entry.type, entry.defaultValue);
                var next = Set(refresh, (index + 1) % entries.Count);
                var refreshIndex = index;
                void Due(AnimatorStateTransition transition) =>
                    transition.Require(AnimatorConditionMode.Equals, refreshIndex, refresh);

                if (entry.type == AnimatorControllerParameterType.Bool)
                {
                    foreach (var value in new[] { false, true })
                    {
                        var word = entry.offset + (value ? 1 : 0);
                        var on = value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
                        var off = value ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If;
                        var send = Send(index + " " + value, PageWrites(pages, entry.page)
                            .Append(Set(code, word)).Append(Set(shadow, value ? 1f : 0f))
                            .Append(next));
                        dirty.Add((send.stale, transition =>
                        {
                            transition.Require(on, 0f, entry.name);
                            transition.Require(off, 0f, shadow);
                        }));
                        periodic.Add((send.turn, transition =>
                        {
                            Due(transition);
                            transition.Require(on, 0f, entry.name);
                        }));

                        var write = State("Receive " + index + " " + value, read, remoteStates);
                        AddDriver(layer, write, Receiver, false, new[] { Set(entry.name, value ? 1f : 0f) });
                        Decode(layer, listen, write, pages, entry.page, code, word, word);
                    }
                    continue;
                }

                var top = entry.offset + entry.levels - 1;
                var quantize = Range(entry.name, code, entry.minimum, entry.maximum,
                    entry.offset + 0.5f, top + 0.5f);
                (AnimatorState stale, AnimatorState turn) numericSend;
                if (entry.type == AnimatorControllerParameterType.Float)
                {
                    // Shadow keeps exactly what the remote shows, so |value - shadow| above
                    // half a level means the remote is stale.
                    var delta = prefix + "/_Internal/Delta/" + index;
                    EnsureParameter(controller, delta, AnimatorControllerParameterType.Float, 0f);
                    deltas.Add((entry, shadow, delta));
                    numericSend = Send(index + " " + Friendly(entry.name), PageWrites(pages, entry.page)
                        .Append(quantize)
                        .Append(Range(code, shadow, entry.offset, top, entry.minimum, entry.maximum))
                        .Append(next));
                    var half = (entry.maximum - entry.minimum) / (2f * (entry.levels - 1));
                    // A value outside its range would stay dirty forever and starve the
                    // others; refresh still sends it clamped.
                    dirty.Add((numericSend.stale, transition =>
                    {
                        transition.Require(AnimatorConditionMode.Greater, half, delta);
                        transition.Require(AnimatorConditionMode.Less, entry.maximum + half, entry.name);
                    }));
                    dirty.Add((numericSend.stale, transition =>
                    {
                        transition.Require(AnimatorConditionMode.Less, -half, delta);
                        transition.Require(AnimatorConditionMode.Greater, entry.minimum - half, entry.name);
                    }));
                }
                else
                {
                    numericSend = Send(index + " " + Friendly(entry.name), PageWrites(pages, entry.page)
                        .Append(quantize).Append(Copy(entry.name, shadow)).Append(next));
                    // PERF: Animator conditions cannot compare two parameters, so an Int
                    // checks each of its values against the shadow.
                    for (var value = Mathf.CeilToInt(entry.minimum);
                         value <= Mathf.FloorToInt(entry.maximum);
                         value++)
                    {
                        var exact = value;
                        dirty.Add((numericSend.stale, transition =>
                        {
                            transition.Require(AnimatorConditionMode.Equals, exact, entry.name);
                            transition.Require(AnimatorConditionMode.NotEqual, exact, shadow);
                        }));
                    }
                }
                periodic.Add((numericSend.turn, Due));

                // Int targets floor, so they land half a level up.
                var lift = entry.type == AnimatorControllerParameterType.Int ? 0.5f : 0f;
                var numericWrite = State("Receive " + index + " " + Friendly(entry.name), read, remoteStates);
                AddDriver(layer, numericWrite, Receiver, false, new[]
                {
                    Range(code, entry.name, entry.offset, top,
                        entry.minimum + lift, entry.maximum + lift)
                });
                Decode(layer, listen, numericWrite, pages, entry.page, code, entry.offset, top);
            }

            // Recuerda: Unity takes the first valid transition, so stale values win over
            // the refresh turn, which also waits out the hold.
            // https://docs.unity3d.com/Manual/class-Transition.html
            foreach (var (send, when) in dirty)
                when(Immediate(layer, router, send));
            foreach (var (send, when) in periodic)
                when(Timed(layer, router, send));
            foreach (var state in localStates.Where(state => state != router))
            {
                SwitchRole(layer, state, listen, isLocalType, false);
                if (urgent.Contains(state)) Timed(layer, state, router);
                else SwitchRole(layer, state, router, isLocalType, true);
            }
            foreach (var state in remoteStates.Where(state => state != listen))
                SwitchRole(layer, state, router, isLocalType, true);
            layer.Commit(0f);

            if (deltas.Count > 0)
                BuildDeltaLayer(controller, prefix, deltas, isLocalType, writeDefaults);

            return new Result
            {
                carrierParameters = new[] { code }.Concat(pages).ToArray(),
                generatedStates = layer.StateCount,
                estimatedFullRefreshSeconds = entries.Count * TickSeconds
            };
        }

        /// <summary>
        /// Delta = value - shadow for every Float, as animated Animator parameters.
        /// Unity clamps negative Direct weights to zero, so each term is a 1D tree
        /// that is linear from minimum to maximum, under a constant weight of one.
        /// </summary>
        private static void BuildDeltaLayer(
            AnimatorController controller,
            string prefix,
            IEnumerable<(Entry entry, string shadow, string delta)> deltas,
            AnimatorControllerParameterType isLocalType,
            bool writeDefaults)
        {
            var one = prefix + "/_Internal/One";
            EnsureParameter(controller, one, AnimatorControllerParameterType.Float, 1f);
            var layer = new GeneratedAnimatorLayer(controller, LayerName + " Delta");
            AnimationClip Constant(string parameter, float value)
            {
                var clip = layer.Keep(new AnimationClip { name = parameter + " = " + value });
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), parameter),
                    AnimationCurve.Constant(0f, 1f / 60f, value));
                return clip;
            }
            ChildMotion Line(string input, string output, float minimum, float maximum, float sign)
            {
                var line = layer.Keep(new BlendTree
                {
                    name = output + " from " + input,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = input,
                    useAutomaticThresholds = false,
                    children = new[]
                    {
                        new ChildMotion { motion = Constant(output, sign * minimum), threshold = minimum, timeScale = 1f },
                        new ChildMotion { motion = Constant(output, sign * maximum), threshold = maximum, timeScale = 1f }
                    }
                });
                return new ChildMotion { motion = line, directBlendParameter = one, timeScale = 1f };
            }
            var tree = layer.Keep(new BlendTree
            {
                name = "YUCP Compressor Delta",
                blendType = BlendTreeType.Direct,
                useAutomaticThresholds = false,
                children = deltas.SelectMany(item => new[]
                {
                    Line(item.entry.name, item.delta, item.entry.minimum, item.entry.maximum, 1f),
                    Line(item.shadow, item.delta, item.entry.minimum, item.entry.maximum, -1f)
                }).ToArray()
            });

            var idle = layer.State("Remote Idle");
            var measure = layer.State("Measure", tree);
            idle.writeDefaultValues = writeDefaults;
            measure.writeDefaultValues = writeDefaults;
            layer.DefaultState = idle;
            SwitchRole(layer, idle, measure, isLocalType, true);
            SwitchRole(layer, measure, idle, isLocalType, false);
            layer.Commit(1f);
        }

        private static void Decode(
            GeneratedAnimatorLayer layer,
            AnimatorState listen,
            AnimatorState write,
            IReadOnlyList<string> pages,
            int page,
            string code,
            int low,
            int high)
        {
            var enter = Immediate(layer, listen, write);
            for (var bit = 0; bit < pages.Count; bit++)
            {
                var set = (page >> bit & 1) != 0;
                enter.Require(set ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, pages[bit]);
                Immediate(layer, write, listen).Require(
                    set ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If, 0f, pages[bit]);
            }
            enter.Require(AnimatorConditionMode.Greater, low - 1, code);
            enter.Require(AnimatorConditionMode.Less, high + 1, code);
            Immediate(layer, write, listen).Require(AnimatorConditionMode.Less, low, code);
            Immediate(layer, write, listen).Require(AnimatorConditionMode.Greater, high, code);
            // A drag keeps the same value's code range, so read it again every ReadSeconds.
            Timed(layer, write, listen);
        }

        /// <summary>The level the sender's Convert Range picks for a value.</summary>
        internal static int Level(Entry entry, float value)
        {
            if (entry.type == AnimatorControllerParameterType.Bool) return value != 0f ? 1 : 0;
            var t = Mathf.Clamp01((value - entry.minimum) / (entry.maximum - entry.minimum));
            return Mathf.FloorToInt(0.5f + t * (entry.levels - 1));
        }

        private static IEnumerable<VRC_AvatarParameterDriver.Parameter> PageWrites(
            IReadOnlyList<string> pages, int page)
        {
            return pages.Select((name, bit) => Set(name, page >> bit & 1));
        }

        private static void SwitchRole(
            GeneratedAnimatorLayer layer,
            AnimatorState from,
            AnimatorState to,
            AnimatorControllerParameterType isLocalType,
            bool local)
        {
            AddIsLocal(Immediate(layer, from, to), isLocalType, local);
        }

        private static AnimatorStateTransition Immediate(
            GeneratedAnimatorLayer layer, AnimatorState from, AnimatorState to)
        {
            var transition = layer.Transition(from, to);
            ConfigureImmediate(transition);
            return transition;
        }

        private static AnimatorStateTransition Timed(
            GeneratedAnimatorLayer layer, AnimatorState from, AnimatorState to)
        {
            var transition = layer.Transition(from, to);
            ConfigureImmediate(transition);
            transition.hasExitTime = true;
            transition.exitTime = 1f;
            return transition;
        }

        private static VRC_AvatarParameterDriver.Parameter Copy(string source, string destination)
        {
            return new VRC_AvatarParameterDriver.Parameter
            {
                source = source,
                name = destination,
                type = VRC_AvatarParameterDriver.ChangeType.Copy
            };
        }

        /// <summary>
        /// Copy with Convert Range. VRChat floors when it writes an Int, and the range
        /// clamps, so a level's bounds sit half a code either side of it.
        /// Taken from: https://creators.vrchat.com/avatars/state-behaviors/
        /// </summary>
        private static VRC_AvatarParameterDriver.Parameter Range(
            string source,
            string destination,
            float sourceMinimum,
            float sourceMaximum,
            float destinationMinimum,
            float destinationMaximum)
        {
            return new VRC_AvatarParameterDriver.Parameter
            {
                source = source,
                name = destination,
                type = VRC_AvatarParameterDriver.ChangeType.Copy,
                convertRange = true,
                sourceMin = sourceMinimum,
                sourceMax = sourceMaximum,
                destMin = destinationMinimum,
                destMax = destinationMaximum
            };
        }

        private static string Friendly(string value)
        {
            var result = new string(value.Select(character =>
                char.IsLetterOrDigit(character) || character == '_' || character == '-'
                    ? character
                    : '_').ToArray());
            return result.Length <= 36 ? result : result.Substring(0, 36);
        }
    }
}
#endif
