#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Compiles a positional, return-to-zero snapshot transport into one flat FX
    /// layer.  The wire alphabet has constant Hamming weight, so a torn update on
    /// the way to or from zero cannot be mistaken for another symbol.  Receiver
    /// values are staged and become visible only after the expected closing sync.
    /// </summary>
    internal static class ParameterCompressorAnimatorBuilder
    {
        private const string LayerName = "YUCP Parameter Compressor";
        private const string IsLocalName = "IsLocal";
        private const float DriverStateSeconds = 1f / 50f;
        private const float MinimumWireSymbolSeconds = 0.1f;
        private const float EstimatedAnimatorFrameSeconds = 1f / 60f;

        internal sealed class Entry
        {
            public string name;
            public AnimatorControllerParameterType type;
            public int levels;
            public float minimum;
            public float maximum;
            public int priority;
            public string group;
        }

        internal sealed class Request
        {
            public AnimatorController controller;
            public string prefix;
            public int busBits;
            public IReadOnlyList<Entry> entries;
            public int blockSize = 8;
            public float signalSeconds = MinimumWireSymbolSeconds;
            public float spacerSeconds = MinimumWireSymbolSeconds;
        }

        internal sealed class Result
        {
            public AnimatorController controller;
            public string layerName;
            public IReadOnlyList<string> carrierParameters;
            public IReadOnlyList<string> stagingParameters;
            public int busBits;
            public int radix;
            public int blockCount;
            public int blockIdDigits;
            public int signalSymbolsPerCycle;
            public int generatedStates;
            public float estimatedFullRefreshSeconds;
        }

        internal static Result Build(Request request)
        {
            return new Graph(request).Build();
        }

        private enum StateRole
        {
            Neutral,
            Local,
            Remote
        }

        private sealed class DigitStep
        {
            internal int cursor;
            internal int divisor;
        }

        private sealed class EntryPlan
        {
            internal Entry entry;
            internal int index;
            internal string stagingParameter;
            internal int digitCount;
            internal int senderLoadCursor;
            internal DigitStep[] senderDigits;
            internal DigitStep[] receiverDigits;
            internal int receiverStageCursor;
        }

        private sealed class BlockPlan
        {
            internal int id;
            internal EntryPlan[] entries;
            internal DigitStep[] senderIdDigits;
            internal int senderCloseCursor;
            internal int receiverFirstCursor;
            internal int receiverCloseCursor;
        }

        private sealed class Graph
        {
            private readonly Request request;
            private readonly AnimatorController controller;
            private readonly ParameterCompressionAlphabet alphabet;
            private readonly string prefix;
            private readonly Entry[] entries;
            private readonly List<BlockPlan> blocks = new List<BlockPlan>();
            private readonly List<UnityEngine.Object> subAssets =
                new List<UnityEngine.Object>();
            private readonly List<AnimatorState> localStates =
                new List<AnimatorState>();
            private readonly List<AnimatorState> remoteStates =
                new List<AnimatorState>();
            private readonly Dictionary<string, AnimatorState> localPayloadStates =
                new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
            private readonly Dictionary<string, AnimatorState> remoteAccumulateStates =
                new Dictionary<string, AnimatorState>(StringComparer.Ordinal);

            private readonly string sendWork;
            private readonly string sendCursor;
            private readonly string receiveWork;
            private readonly string receiveCursor;
            private readonly string clockParameter;
            private readonly string[] carriers;

            private AnimatorControllerParameterType isLocalType;
            private bool writeDefaults;
            private int blockIdDigits;
            private int receiverIdDoneCursor;
            private AnimatorStateMachine machine;
            private AnimationClip signalClip;
            private AnimationClip spacerClip;
            private AnimationClip driverClip;
            private AnimatorState localRouter;
            private AnimatorState localSignalExtraFrame;
            private AnimatorState localSpacer;
            private AnimatorState localSpacerExtraFrame;
            private AnimatorState remoteAwaitSync;
            private AnimatorState remoteBeginFrame;
            private AnimatorState remoteWaitForZero;
            private AnimatorState remoteRouter;
            private AnimatorState remoteBlockRouter;
            private AnimatorState[] localSyncStates;
            private AnimatorState[] remoteBeginBlockStates;
            private AnimatorState[] remoteCommitStates;

            internal Graph(Request source)
            {
                request = source ?? throw new ArgumentNullException(nameof(source));
                controller = request.controller ??
                             throw new ArgumentNullException(nameof(request.controller));
                if (request.busBits < 3 || request.busBits > 7)
                    throw new ArgumentOutOfRangeException(nameof(request.busBits),
                        "The avatar transport supports 3 through 7 Boolean carriers.");
                if (request.blockSize <= 0)
                    throw new ArgumentOutOfRangeException(nameof(request.blockSize));
                if (!Finite(request.signalSeconds) ||
                    request.signalSeconds < MinimumWireSymbolSeconds)
                    throw new ArgumentOutOfRangeException(nameof(request.signalSeconds),
                        "A wire symbol must be held for at least 0.1 seconds.");
                if (!Finite(request.spacerSeconds) ||
                    request.spacerSeconds < MinimumWireSymbolSeconds)
                    throw new ArgumentOutOfRangeException(nameof(request.spacerSeconds),
                        "The return-to-zero spacer must be held for at least 0.1 seconds.");

                prefix = ParameterCompressionContract.NormalizePrefix(request.prefix);
                alphabet = new ParameterCompressionAlphabet(request.busBits);
                entries = MaterializeEntries(request.entries);
                sendWork = prefix + "/_Internal/SendWork";
                sendCursor = prefix + "/_Internal/SendCursor";
                receiveWork = prefix + "/_Internal/ReceiveWork";
                receiveCursor = prefix + "/_Internal/ReceiveCursor";
                clockParameter = prefix + "/_Internal/Clock";
                carriers = Enumerable.Range(0, request.busBits)
                    .Select(index => prefix + "/_Bus/Bit" + index)
                    .ToArray();
            }

            internal Result Build()
            {
                BuildPlans();
                writeDefaults = DetermineWriteDefaults(controller);
                AddParameters();
                AddLayer();
                CreateClips();
                CreateCommonStates();
                WireLocalSender();
                WireRemoteReceiver();
                WireRoleEntryAndSwitches();

                var valueSymbols = entries.Sum(entry =>
                    ParameterCompressionEnumerativeLayout.DigitsRequired(
                        entry.levels, alphabet.Radix));
                var symbols = blocks.Count * (1 + blockIdDigits) + valueSymbols;
                var secondsPerSymbol = request.signalSeconds +
                                       request.spacerSeconds +
                                       2f * EstimatedAnimatorFrameSeconds;
                return new Result
                {
                    controller = controller,
                    layerName = LayerName,
                    carrierParameters = new ReadOnlyCollection<string>(carriers),
                    stagingParameters = new ReadOnlyCollection<string>(
                        blocks.SelectMany(block => block.entries)
                            .OrderBy(entry => entry.index)
                            .Select(entry => entry.stagingParameter)
                            .ToArray()),
                    busBits = request.busBits,
                    radix = alphabet.Radix,
                    blockCount = blocks.Count,
                    blockIdDigits = blockIdDigits,
                    signalSymbolsPerCycle = symbols,
                    generatedStates = machine.states.Length,
                    estimatedFullRefreshSeconds =
                        symbols * secondsPerSymbol +
                        entries.Length * DriverStateSeconds
                };
            }

            private Entry[] MaterializeEntries(IReadOnlyList<Entry> source)
            {
                if (source == null) throw new ArgumentNullException(nameof(request.entries));
                if (source.Count == 0)
                    throw new ArgumentException(
                        "The transport needs at least one parameter.",
                        nameof(request.entries));

                var names = new HashSet<string>(StringComparer.Ordinal);
                var materialized = new Entry[source.Count];
                for (var index = 0; index < source.Count; index++)
                {
                    var value = source[index] ??
                                throw new ArgumentException(
                                    "A compressor entry cannot be null.",
                                    nameof(request.entries));
                    var name = (value.name ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(name))
                        throw new ArgumentException(
                            "A compressor entry needs an Animator parameter name.",
                            nameof(request.entries));
                    if (!names.Add(name))
                        throw new ArgumentException(
                            "Parameter '" + name + "' is listed more than once.",
                            nameof(request.entries));
                    if (name.StartsWith(prefix + "/", StringComparison.Ordinal))
                        throw new ArgumentException(
                            "Parameter '" + name + "' collides with the generated transport prefix.",
                            nameof(request.entries));
                    if (value.type != AnimatorControllerParameterType.Bool &&
                        value.type != AnimatorControllerParameterType.Int &&
                        value.type != AnimatorControllerParameterType.Float)
                        throw new ArgumentException(
                            "Parameter '" + name + "' has an unsupported Animator type.",
                            nameof(request.entries));
                    if (value.type == AnimatorControllerParameterType.Bool && value.levels != 2)
                        throw new ArgumentException(
                            "Boolean parameter '" + name + "' must have exactly two levels.",
                            nameof(request.entries));
                    if (value.type != AnimatorControllerParameterType.Bool && value.levels < 2)
                        throw new ArgumentException(
                            "Numeric parameter '" + name + "' needs at least two levels.",
                            nameof(request.entries));
                    if (value.type != AnimatorControllerParameterType.Bool &&
                        (!Finite(value.minimum) || !Finite(value.maximum) ||
                         value.maximum <= value.minimum))
                        throw new ArgumentException(
                            "Parameter '" + name + "' needs a finite increasing range.",
                            nameof(request.entries));

                    materialized[index] = new Entry
                    {
                        name = name,
                        type = value.type,
                        levels = value.levels,
                        minimum = value.type == AnimatorControllerParameterType.Bool
                            ? 0f
                            : value.minimum,
                        maximum = value.type == AnimatorControllerParameterType.Bool
                            ? 1f
                            : value.maximum,
                        priority = value.priority,
                        group = ParameterCompressionContract.NormalizeGroup(value.group)
                    };
                }
                return materialized;
            }

            private void BuildPlans()
            {
                var blockCount = (entries.Length + request.blockSize - 1) /
                                 request.blockSize;
                blockIdDigits = Math.Max(1,
                    ParameterCompressionEnumerativeLayout.DigitsRequired(
                        blockCount, alphabet.Radix));

                var entryPlans = new EntryPlan[entries.Length];
                for (var index = 0; index < entries.Length; index++)
                {
                    var digits = Math.Max(1,
                        ParameterCompressionEnumerativeLayout.DigitsRequired(
                            entries[index].levels, alphabet.Radix));
                    entryPlans[index] = new EntryPlan
                    {
                        entry = entries[index],
                        index = index,
                        stagingParameter = prefix + "/_Internal/Staging/" + index,
                        digitCount = digits
                    };
                }

                var senderCursorValue = 1;
                for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
                {
                    var blockEntries = entryPlans
                        .Skip(blockIndex * request.blockSize)
                        .Take(request.blockSize)
                        .ToArray();
                    var block = new BlockPlan
                    {
                        id = blockIndex,
                        entries = blockEntries,
                        senderIdDigits = AllocateDigits(
                            ref senderCursorValue, blockIdDigits, alphabet.Radix)
                    };
                    foreach (var entry in blockEntries)
                    {
                        entry.senderLoadCursor = senderCursorValue++;
                        entry.senderDigits = AllocateDigits(
                            ref senderCursorValue, entry.digitCount, alphabet.Radix);
                    }
                    block.senderCloseCursor = senderCursorValue++;
                    blocks.Add(block);
                }

                var receiverCursorValue = 1;
                var receiverIdDigits = AllocateDigits(
                    ref receiverCursorValue, blockIdDigits, alphabet.Radix);
                receiverIdDoneCursor = receiverCursorValue++;
                foreach (var block in blocks)
                {
                    foreach (var entry in block.entries)
                    {
                        entry.receiverDigits = AllocateDigits(
                            ref receiverCursorValue, entry.digitCount, alphabet.Radix);
                        entry.receiverStageCursor = receiverCursorValue++;
                    }
                    block.receiverFirstCursor = block.entries[0]
                        .receiverDigits[0].cursor;
                    block.receiverCloseCursor = receiverCursorValue++;
                }

                // All blocks share the same receiver ID positions.  Their own
                // value positions remain unique so a missed block route cannot
                // accidentally commit a different positional schema.
                receiverSharedIdDigits = receiverIdDigits;
            }

            private DigitStep[] receiverSharedIdDigits;

            private static DigitStep[] AllocateDigits(
                ref int cursor, int count, int radix)
            {
                var result = new DigitStep[count];
                // Consume the most-significant digit first.  The sender removes
                // each emitted digit from sendWork, so a least-significant-first
                // schedule leaves values >= radix outside every first-digit
                // transition (for example 19 on a radix-19 bus) and stalls the
                // state machine.  Starting at radix^(count - 1) keeps every
                // remainder in the range represented by the following steps.
                var divisor = 1;
                for (var index = 1; index < count; index++)
                {
                    checked
                    {
                        divisor *= radix;
                    }
                }
                for (var index = 0; index < count; index++)
                {
                    result[index] = new DigitStep
                    {
                        cursor = cursor++,
                        divisor = divisor
                    };
                    if (index + 1 < count) divisor /= radix;
                }
                return result;
            }

            private void AddParameters()
            {
                var existingIsLocal = controller.parameters.FirstOrDefault(parameter =>
                    string.Equals(parameter.name, IsLocalName, StringComparison.Ordinal));
                if (existingIsLocal == null)
                {
                    EnsureParameter(IsLocalName, AnimatorControllerParameterType.Bool,
                        defaultBool: true);
                    isLocalType = AnimatorControllerParameterType.Bool;
                }
                else
                {
                    if (existingIsLocal.type != AnimatorControllerParameterType.Bool &&
                        existingIsLocal.type != AnimatorControllerParameterType.Int &&
                        existingIsLocal.type != AnimatorControllerParameterType.Float)
                        throw new InvalidOperationException(
                            "Animator parameter 'IsLocal' has an unsupported type.");
                    isLocalType = existingIsLocal.type;
                }

                foreach (var carrier in carriers)
                    EnsureParameter(carrier, AnimatorControllerParameterType.Bool);
                EnsureParameter(sendWork, AnimatorControllerParameterType.Int);
                EnsureParameter(sendCursor, AnimatorControllerParameterType.Int);
                EnsureParameter(receiveWork, AnimatorControllerParameterType.Int);
                EnsureParameter(receiveCursor, AnimatorControllerParameterType.Int);
                EnsureParameter(clockParameter, AnimatorControllerParameterType.Float);

                foreach (var block in blocks)
                foreach (var entry in block.entries)
                {
                    var defaultValue = entry.entry.minimum <= 0f &&
                                       entry.entry.maximum >= 0f
                        ? 0f
                        : entry.entry.minimum;
                    EnsureParameter(entry.entry.name, entry.entry.type,
                        defaultFloat: defaultValue,
                        defaultInt: Mathf.RoundToInt(defaultValue));
                    EnsureParameter(entry.stagingParameter, entry.entry.type,
                        defaultFloat: defaultValue,
                        defaultInt: Mathf.RoundToInt(defaultValue));
                }
            }

            private void EnsureParameter(
                string name,
                AnimatorControllerParameterType type,
                float defaultFloat = 0f,
                int defaultInt = 0,
                bool defaultBool = false)
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

                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = name,
                    type = type,
                    defaultFloat = defaultFloat,
                    defaultInt = defaultInt,
                    defaultBool = defaultBool
                });
            }

            private void AddLayer()
            {
                if (controller.layers.Any(layer =>
                    string.Equals(layer.name, LayerName, StringComparison.Ordinal)))
                    throw new InvalidOperationException(
                        "The controller already contains a YUCP Parameter Compressor layer.");
                controller.AddLayer(LayerName);
                var layers = controller.layers;
                var index = layers.Length - 1;
                layers[index].defaultWeight = 0f;
                controller.layers = layers;
                machine = controller.layers[index].stateMachine;
                AddSubAsset(machine);
            }

            private void CreateClips()
            {
                signalClip = TimerClip("YUCP Compressor Signal Hold", request.signalSeconds);
                spacerClip = TimerClip("YUCP Compressor Zero Hold", request.spacerSeconds);
                driverClip = TimerClip("YUCP Compressor Driver Dwell", DriverStateSeconds);
            }

            private void CreateCommonStates()
            {
                localRouter = State("Local Cursor Router", null, StateRole.Local);
                localSignalExtraFrame = State(
                    "Local Signal Extra Frame", null, StateRole.Local);
                localSpacer = State(
                    "Local Return To Zero", spacerClip, StateRole.Local);
                localSpacerExtraFrame = State(
                    "Local Zero Extra Frame", null, StateRole.Local);
                AddDriver(localSpacer, true, "YUCP compressor RTZ sender",
                    WordWrites(0));

                remoteAwaitSync = State(
                    "Remote Await Sync", null, StateRole.Remote);
                remoteBeginFrame = State(
                    "Remote Begin Frame", driverClip, StateRole.Remote);
                remoteWaitForZero = State(
                    "Remote Wait For Zero", null, StateRole.Remote);
                remoteRouter = State(
                    "Remote Symbol Router", null, StateRole.Remote);
                remoteBlockRouter = State(
                    "Remote Block Router", null, StateRole.Remote);

                AddDriver(remoteBeginFrame, false,
                    "YUCP compressor frame recovery",
                    new[]
                    {
                        Set(receiveWork, 0f),
                        Set(receiveCursor, receiverSharedIdDigits[0].cursor)
                    });

                localSyncStates = new AnimatorState[blocks.Count];
                remoteBeginBlockStates = new AnimatorState[blocks.Count];
                remoteCommitStates = new AnimatorState[blocks.Count];
                for (var index = 0; index < blocks.Count; index++)
                {
                    var block = blocks[index];
                    var sync = State(
                        "Local Sync Block " + block.id,
                        signalClip, StateRole.Local);
                    AddDriver(sync, true, "YUCP compressor frame sender",
                        WordWrites(alphabet.SyncWord)
                            .Concat(new[]
                            {
                                Set(sendWork, block.id),
                                Set(sendCursor, block.senderIdDigits[0].cursor)
                            }));
                    localSyncStates[index] = sync;

                    var begin = State(
                        "Remote Begin Block " + block.id,
                        driverClip, StateRole.Remote);
                    AddDriver(begin, false, "YUCP compressor block decoder",
                        new[]
                        {
                            Set(receiveWork, 0f),
                            Set(receiveCursor, block.receiverFirstCursor)
                        });
                    remoteBeginBlockStates[index] = begin;

                    var commit = State(
                        "Remote Commit Block " + block.id,
                        driverClip, StateRole.Remote);
                    AddDriver(commit, false, "YUCP compressor atomic block commit",
                        block.entries.Select(entry =>
                            Copy(entry.stagingParameter, entry.entry.name)));
                    remoteCommitStates[index] = commit;
                }
            }

            private void WireLocalSender()
            {
                AddImmediate(localSignalExtraFrame, localSpacer, StateRole.Local);
                AddTimed(localSpacer, localSpacerExtraFrame, StateRole.Local);
                AddImmediate(localSpacerExtraFrame, localRouter, StateRole.Local);

                foreach (var sync in localSyncStates)
                    AddTimed(sync, localSignalExtraFrame, StateRole.Local);

                foreach (var block in blocks)
                {
                    foreach (var step in block.senderIdDigits)
                        WireLocalDigit(step, blocks.Count);

                    foreach (var entry in block.entries)
                    {
                        WireLocalLoad(entry);
                        foreach (var step in entry.senderDigits)
                            WireLocalDigit(step, entry.entry.levels);
                    }

                    var next = (block.id + 1) % blocks.Count;
                    var close = AddImmediate(
                        localRouter, localSyncStates[next], StateRole.Local);
                    AddCursorCondition(close, sendCursor, block.senderCloseCursor);
                    AddWordConditions(close, 0);
                }
            }

            private void WireLocalDigit(DigitStep step, int cardinality)
            {
                var maximumDigit = Math.Min(
                    alphabet.Radix - 1,
                    (cardinality - 1) / step.divisor);
                for (var digit = 0; digit <= maximumDigit; digit++)
                {
                    var target = LocalPayload(step.divisor, digit);
                    var transition = AddImmediate(
                        localRouter, target, StateRole.Local);
                    AddCursorCondition(transition, sendCursor, step.cursor);
                    AddDigitRangeConditions(
                        transition, sendWork, step.divisor, digit);
                    AddWordConditions(transition, 0);
                }
            }

            private void WireLocalLoad(EntryPlan plan)
            {
                if (plan.entry.type == AnimatorControllerParameterType.Bool)
                {
                    var loadFalse = State(
                        "Local Load " + plan.index + " False",
                        driverClip, StateRole.Local);
                    var loadTrue = State(
                        "Local Load " + plan.index + " True",
                        driverClip, StateRole.Local);
                    AddDriver(loadFalse, true, "YUCP compressor Boolean sampler",
                        new[]
                        {
                            Set(sendWork, 0f),
                            Add(sendCursor, 1f)
                        });
                    AddDriver(loadTrue, true, "YUCP compressor Boolean sampler",
                        new[]
                        {
                            Set(sendWork, 1f),
                            Add(sendCursor, 1f)
                        });
                    AddTimed(loadFalse, localRouter, StateRole.Local);
                    AddTimed(loadTrue, localRouter, StateRole.Local);

                    var whenFalse = AddImmediate(
                        localRouter, loadFalse, StateRole.Local);
                    AddCursorCondition(
                        whenFalse, sendCursor, plan.senderLoadCursor);
                    AddWordConditions(whenFalse, 0);
                    whenFalse.AddCondition(
                        AnimatorConditionMode.IfNot, 0f, plan.entry.name);

                    var whenTrue = AddImmediate(
                        localRouter, loadTrue, StateRole.Local);
                    AddCursorCondition(
                        whenTrue, sendCursor, plan.senderLoadCursor);
                    AddWordConditions(whenTrue, 0);
                    whenTrue.AddCondition(
                        AnimatorConditionMode.If, 0f, plan.entry.name);
                    return;
                }

                var load = State(
                    "Local Load " + plan.index + " " + Friendly(plan.entry.name),
                    driverClip, StateRole.Local);
                AddDriver(load, true, "YUCP compressor numeric sampler",
                    new[]
                    {
                        CopyRange(
                            plan.entry.name,
                            sendWork,
                            plan.entry.minimum,
                            plan.entry.maximum,
                            0.5f,
                            plan.entry.levels - 0.5f),
                        Add(sendCursor, 1f)
                    });
                AddTimed(load, localRouter, StateRole.Local);
                var route = AddImmediate(localRouter, load, StateRole.Local);
                AddCursorCondition(route, sendCursor, plan.senderLoadCursor);
                AddWordConditions(route, 0);
            }

            private AnimatorState LocalPayload(int divisor, int digit)
            {
                var key = divisor + ":" + digit;
                if (localPayloadStates.TryGetValue(key, out var existing))
                    return existing;
                var state = State(
                    "Local Digit d" + divisor + " v" + digit,
                    signalClip, StateRole.Local);
                var writes = WordWrites(alphabet.EncodeDigit(digit)).ToList();
                if (digit != 0)
                    writes.Add(Add(sendWork, -digit * divisor));
                writes.Add(Add(sendCursor, 1f));
                AddDriver(state, true, "YUCP compressor payload sender", writes);
                AddTimed(state, localSignalExtraFrame, StateRole.Local);
                localPayloadStates.Add(key, state);
                return state;
            }

            private void WireRemoteReceiver()
            {
                var findSync = AddImmediate(
                    remoteAwaitSync, remoteBeginFrame, StateRole.Remote);
                AddWordConditions(findSync, alphabet.SyncWord);

                AddTimed(remoteBeginFrame, remoteWaitForZero, StateRole.Remote);
                var observedZero = AddImmediate(
                    remoteWaitForZero, remoteRouter, StateRole.Remote);
                AddWordConditions(observedZero, 0);

                foreach (var block in blocks)
                {
                    var commit = AddImmediate(
                        remoteRouter,
                        remoteCommitStates[block.id],
                        StateRole.Remote);
                    AddCursorCondition(
                        commit, receiveCursor, block.receiverCloseCursor);
                    AddWordConditions(commit, alphabet.SyncWord);
                    AddTimed(
                        remoteCommitStates[block.id],
                        remoteBeginFrame,
                        StateRole.Remote);
                }

                // This fallback is deliberately after the cursor-specific commit
                // transitions.  A sync at any other position abandons staging and
                // starts a fresh frame, which supplies loss and late-join recovery.
                var recover = AddImmediate(
                    remoteRouter, remoteBeginFrame, StateRole.Remote);
                AddWordConditions(recover, alphabet.SyncWord);

                foreach (var step in receiverSharedIdDigits)
                    WireRemoteDigit(step, blocks.Count);

                var routeBlock = AddImmediate(
                    remoteRouter, remoteBlockRouter, StateRole.Remote);
                AddCursorCondition(
                    routeBlock, receiveCursor, receiverIdDoneCursor);
                AddWordConditions(routeBlock, 0);

                var blockRouterRecovery = AddImmediate(
                    remoteBlockRouter, remoteBeginFrame, StateRole.Remote);
                AddWordConditions(blockRouterRecovery, alphabet.SyncWord);
                foreach (var block in blocks)
                {
                    var choose = AddImmediate(
                        remoteBlockRouter,
                        remoteBeginBlockStates[block.id],
                        StateRole.Remote);
                    choose.AddCondition(
                        AnimatorConditionMode.Equals, block.id, receiveWork);
                    AddWordConditions(choose, 0);
                    AddTimed(
                        remoteBeginBlockStates[block.id],
                        remoteRouter,
                        StateRole.Remote);

                    foreach (var entry in block.entries)
                    {
                        foreach (var step in entry.receiverDigits)
                            WireRemoteDigit(step, entry.entry.levels);
                        WireRemoteStage(entry);
                    }
                }
            }

            private void WireRemoteDigit(DigitStep step, int cardinality)
            {
                var maximumDigit = Math.Min(
                    alphabet.Radix - 1,
                    (cardinality - 1) / step.divisor);
                for (var digit = 0; digit <= maximumDigit; digit++)
                {
                    var target = RemoteAccumulate(step.divisor, digit);
                    var transition = AddImmediate(
                        remoteRouter, target, StateRole.Remote);
                    AddCursorCondition(
                        transition, receiveCursor, step.cursor);
                    AddWordConditions(transition, alphabet.EncodeDigit(digit));
                }
            }

            private AnimatorState RemoteAccumulate(int divisor, int digit)
            {
                var key = divisor + ":" + digit;
                if (remoteAccumulateStates.TryGetValue(key, out var existing))
                    return existing;
                var state = State(
                    "Remote Digit d" + divisor + " v" + digit,
                    driverClip, StateRole.Remote);
                var writes = new List<VRC_AvatarParameterDriver.Parameter>();
                if (digit != 0)
                    writes.Add(Add(receiveWork, digit * divisor));
                writes.Add(Add(receiveCursor, 1f));
                AddDriver(state, false, "YUCP compressor payload decoder", writes);
                AddTimed(state, remoteWaitForZero, StateRole.Remote);
                remoteAccumulateStates.Add(key, state);
                return state;
            }

            private void WireRemoteStage(EntryPlan plan)
            {
                if (plan.entry.type == AnimatorControllerParameterType.Bool)
                {
                    var stageFalse = State(
                        "Remote Stage " + plan.index + " False",
                        driverClip, StateRole.Remote);
                    var stageTrue = State(
                        "Remote Stage " + plan.index + " True",
                        driverClip, StateRole.Remote);
                    AddDriver(stageFalse, false, "YUCP compressor Boolean staging",
                        new[]
                        {
                            Set(plan.stagingParameter, 0f),
                            Set(receiveWork, 0f),
                            Add(receiveCursor, 1f)
                        });
                    AddDriver(stageTrue, false, "YUCP compressor Boolean staging",
                        new[]
                        {
                            Set(plan.stagingParameter, 1f),
                            Set(receiveWork, 0f),
                            Add(receiveCursor, 1f)
                        });
                    AddTimed(stageFalse, remoteRouter, StateRole.Remote);
                    AddTimed(stageTrue, remoteRouter, StateRole.Remote);

                    var falseRoute = AddImmediate(
                        remoteRouter, stageFalse, StateRole.Remote);
                    AddCursorCondition(
                        falseRoute, receiveCursor, plan.receiverStageCursor);
                    falseRoute.AddCondition(
                        AnimatorConditionMode.Equals, 0f, receiveWork);
                    AddWordConditions(falseRoute, 0);

                    var trueRoute = AddImmediate(
                        remoteRouter, stageTrue, StateRole.Remote);
                    AddCursorCondition(
                        trueRoute, receiveCursor, plan.receiverStageCursor);
                    trueRoute.AddCondition(
                        AnimatorConditionMode.Equals, 1f, receiveWork);
                    AddWordConditions(trueRoute, 0);
                    return;
                }

                var stage = State(
                    "Remote Stage " + plan.index + " " + Friendly(plan.entry.name),
                    driverClip, StateRole.Remote);
                AddDriver(stage, false, "YUCP compressor numeric staging",
                    new[]
                    {
                        CopyRange(
                            receiveWork,
                            plan.stagingParameter,
                            0f,
                            plan.entry.levels - 1f,
                            plan.entry.minimum,
                            plan.entry.maximum),
                        Set(receiveWork, 0f),
                        Add(receiveCursor, 1f)
                    });
                AddTimed(stage, remoteRouter, StateRole.Remote);
                var route = AddImmediate(
                    remoteRouter, stage, StateRole.Remote);
                AddCursorCondition(route, receiveCursor, plan.receiverStageCursor);
                route.AddCondition(
                    AnimatorConditionMode.Less, plan.entry.levels, receiveWork);
                AddWordConditions(route, 0);
            }

            private void WireRoleEntryAndSwitches()
            {
                var roleEntry = State("Role Entry", null, StateRole.Neutral);
                machine.defaultState = roleEntry;

                var becomeLocal = AddImmediate(
                    roleEntry, localSyncStates[0], StateRole.Neutral);
                AddIsLocalCondition(becomeLocal, true);
                var becomeRemote = AddImmediate(
                    roleEntry, remoteAwaitSync, StateRole.Neutral);
                AddIsLocalCondition(becomeRemote, false);

                foreach (var state in localStates.Distinct())
                {
                    var transition = AddImmediate(
                        state, remoteAwaitSync, StateRole.Neutral);
                    AddIsLocalCondition(transition, false);
                }
                foreach (var state in remoteStates.Distinct())
                {
                    var transition = AddImmediate(
                        state, localSyncStates[0], StateRole.Neutral);
                    AddIsLocalCondition(transition, true);
                }
            }

            private AnimatorState State(
                string name, Motion motion, StateRole role)
            {
                var state = machine.AddState(name);
                state.writeDefaultValues = writeDefaults;
                state.motion = motion;
                if (role == StateRole.Local) localStates.Add(state);
                if (role == StateRole.Remote) remoteStates.Add(state);
                return state;
            }

            private AnimatorStateTransition AddImmediate(
                AnimatorState from, AnimatorState to, StateRole role)
            {
                var transition = from.AddTransition(to);
                ConfigureImmediate(transition);
                if (role == StateRole.Local) AddIsLocalCondition(transition, true);
                if (role == StateRole.Remote) AddIsLocalCondition(transition, false);
                return transition;
            }

            private AnimatorStateTransition AddTimed(
                AnimatorState from, AnimatorState to, StateRole role)
            {
                var transition = from.AddTransition(to);
                ConfigureTimed(transition);
                if (role == StateRole.Local) AddIsLocalCondition(transition, true);
                if (role == StateRole.Remote) AddIsLocalCondition(transition, false);
                return transition;
            }

            private void AddIsLocalCondition(
                AnimatorStateTransition transition, bool local)
            {
                if (isLocalType == AnimatorControllerParameterType.Bool)
                {
                    transition.AddCondition(
                        local ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                        0f, IsLocalName);
                    return;
                }
                transition.AddCondition(
                    local ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
                    0.5f, IsLocalName);
            }

            private static void AddCursorCondition(
                AnimatorStateTransition transition,
                string parameter,
                int cursor)
            {
                transition.AddCondition(
                    AnimatorConditionMode.Equals, cursor, parameter);
            }

            private static void AddDigitRangeConditions(
                AnimatorStateTransition transition,
                string work,
                int divisor,
                int digit)
            {
                var lower = checked(digit * divisor);
                var upper = (long)(digit + 1) * divisor;
                if (lower > 0)
                    transition.AddCondition(
                        AnimatorConditionMode.Greater, lower - 1, work);
                if (upper <= int.MaxValue)
                    transition.AddCondition(
                        AnimatorConditionMode.Less, (float)upper, work);
            }

            private void AddWordConditions(
                AnimatorStateTransition transition, int word)
            {
                for (var bit = 0; bit < carriers.Length; bit++)
                    transition.AddCondition(
                        (word & (1 << bit)) != 0
                            ? AnimatorConditionMode.If
                            : AnimatorConditionMode.IfNot,
                        0f, carriers[bit]);
            }

            private IEnumerable<VRC_AvatarParameterDriver.Parameter> WordWrites(
                int word)
            {
                for (var bit = 0; bit < carriers.Length; bit++)
                    yield return Set(
                        carriers[bit],
                        (word & (1 << bit)) != 0 ? 1f : 0f);
            }

            private void AddDriver(
                AnimatorState state,
                bool localOnly,
                string debugString,
                IEnumerable<VRC_AvatarParameterDriver.Parameter> parameters)
            {
                var values = (parameters ??
                              Enumerable.Empty<VRC_AvatarParameterDriver.Parameter>())
                    .ToList();
                if (values.Count == 0) return;
                var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
                driver.name = localOnly
                    ? "YUCP Parameter Compressor Sender"
                    : "YUCP Parameter Compressor Receiver";
                driver.localOnly = localOnly;
                driver.isEnabled = true;
                driver.debugString = debugString;
                driver.parameters = values;
                AddSubAsset(driver);
                state.behaviours = state.behaviours
                    .Concat(new StateMachineBehaviour[] { driver })
                    .ToArray();
            }

            private static VRC_AvatarParameterDriver.Parameter Set(
                string name, float value)
            {
                return new VRC_AvatarParameterDriver.Parameter
                {
                    name = name,
                    type = VRC_AvatarParameterDriver.ChangeType.Set,
                    value = value
                };
            }

            private static VRC_AvatarParameterDriver.Parameter Add(
                string name, float value)
            {
                return new VRC_AvatarParameterDriver.Parameter
                {
                    name = name,
                    type = VRC_AvatarParameterDriver.ChangeType.Add,
                    value = value
                };
            }

            private static VRC_AvatarParameterDriver.Parameter Copy(
                string source, string destination)
            {
                return new VRC_AvatarParameterDriver.Parameter
                {
                    source = source,
                    name = destination,
                    type = VRC_AvatarParameterDriver.ChangeType.Copy,
                    convertRange = false
                };
            }

            private static VRC_AvatarParameterDriver.Parameter CopyRange(
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

            private AnimationClip TimerClip(string name, float seconds)
            {
                var clip = new AnimationClip
                {
                    name = name,
                    frameRate = 60f
                };
                AnimationUtility.SetEditorCurve(
                    clip,
                    EditorCurveBinding.FloatCurve(
                        string.Empty, typeof(Animator), clockParameter),
                    AnimationCurve.Linear(0f, 0f, seconds, seconds));
                AddSubAsset(clip);
                return clip;
            }

            private void AddSubAsset(UnityEngine.Object value)
            {
                if (value == null || subAssets.Contains(value) ||
                    AssetDatabase.Contains(value)) return;
                var path = AssetDatabase.GetAssetPath(controller);
                if (string.IsNullOrEmpty(path)) return;
                value.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(value, controller);
                subAssets.Add(value);
            }

            private static void ConfigureImmediate(
                AnimatorStateTransition transition)
            {
                transition.hasExitTime = false;
                transition.duration = 0f;
                transition.hasFixedDuration = true;
                transition.canTransitionToSelf = false;
                transition.interruptionSource = TransitionInterruptionSource.None;
            }

            private static void ConfigureTimed(
                AnimatorStateTransition transition)
            {
                transition.hasExitTime = true;
                transition.exitTime = 1f;
                transition.duration = 0f;
                transition.hasFixedDuration = true;
                transition.canTransitionToSelf = false;
                transition.interruptionSource = TransitionInterruptionSource.None;
            }

            private static bool DetermineWriteDefaults(
                AnimatorController value)
            {
                var states = value.layers
                    .SelectMany(layer => EnumerateStates(layer.stateMachine))
                    .ToArray();
                return states.Length == 0 || states.All(state => state.writeDefaultValues);
            }

            private static IEnumerable<AnimatorState> EnumerateStates(
                AnimatorStateMachine stateMachine)
            {
                if (stateMachine == null) yield break;
                foreach (var child in stateMachine.states)
                    if (child.state != null)
                        yield return child.state;
                foreach (var child in stateMachine.stateMachines)
                foreach (var state in EnumerateStates(child.stateMachine))
                    yield return state;
            }

            private static string Friendly(string value)
            {
                var result = new string((value ?? string.Empty)
                    .Select(character => char.IsLetterOrDigit(character) ||
                                         character == '_' || character == '-'
                        ? character
                        : '_')
                    .ToArray());
                return result.Length <= 36 ? result : result.Substring(0, 36);
            }

            private static bool Finite(float value)
            {
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }
        }
    }
}
#endif
