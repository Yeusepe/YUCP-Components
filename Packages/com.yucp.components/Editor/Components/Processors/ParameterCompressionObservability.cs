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
    /// <summary>
    /// One synced parameter as every remote client can observe it. Its network
    /// values are partitioned into cells by the outcome of every remote read, so
    /// two values in one cell drive every remote Animator identically. A value
    /// that is read as a number (blend trees, state parameters, Copy and Add
    /// drivers) keeps one cell per network value; a value nothing reads has a
    /// single cell and does not need to be synced at all.
    /// </summary>
    internal sealed class ParameterObservation
    {
        internal ParameterObservation(
            string name,
            VRCExpressionParameters.ValueType valueType,
            float defaultValue,
            IReadOnlyList<float[]> cells,
            bool read,
            bool readAsNumber,
            bool remotelyWritten)
        {
            Name = name;
            ValueType = valueType;
            Cells = cells;
            Read = read;
            ReadAsNumber = readAsNumber;
            RemotelyWritten = remotelyWritten;
            var quantizedDefault = ParameterCompressionObservability.Quantize(
                valueType, defaultValue);
            Representatives = cells.Select(cell =>
                cell.Contains(quantizedDefault) ? quantizedDefault : cell[0]).ToArray();
            DefaultCell = Array.FindIndex(cells.ToArray(), cell =>
                cell.Contains(quantizedDefault));
        }

        internal string Name { get; }
        internal VRCExpressionParameters.ValueType ValueType { get; }

        /// <summary>Network values per cell, each ascending; cells ascend by first value.</summary>
        internal IReadOnlyList<float[]> Cells { get; }

        /// <summary>The value a remote decoder writes for each cell. The default
        /// value represents its own cell, so an undecoded remote starts unchanged.</summary>
        internal IReadOnlyList<float> Representatives { get; }

        internal int DefaultCell { get; }
        internal bool Read { get; }
        internal bool ReadAsNumber { get; }

        /// <summary>A driver that also runs on remote clients writes this value.
        /// Decoding would then fight that driver, so it is never packed.</summary>
        internal bool RemotelyWritten { get; }

        internal int CellCount => Cells.Count;

        internal int CellOf(float networkValue)
        {
            for (var index = 0; index < Cells.Count; index++)
                if (Cells[index].Contains(networkValue)) return index;
            return -1;
        }
    }

    internal sealed class ParameterObservability
    {
        internal ParameterObservability(
            bool complete,
            string incompleteReason,
            IReadOnlyDictionary<string, ParameterObservation> observations)
        {
            Complete = complete;
            IncompleteReason = incompleteReason;
            Observations = observations;
        }

        /// <summary>False when some playable layer could not be inspected. Every
        /// reduction must then be skipped, because an unseen layer may read anything.</summary>
        internal bool Complete { get; }

        internal string IncompleteReason { get; }
        internal IReadOnlyDictionary<string, ParameterObservation> Observations { get; }
    }

    /// <summary>
    /// Derives, from the final merged avatar, what remote clients can tell apart
    /// about each synced parameter. It inspects readers rather than writers on
    /// purpose: OSC, contacts and other tools may write any value, but a remote
    /// client can only react to a difference that one of its Animators reads.
    /// The analysis is therefore safe for any avatar, whoever writes the values.
    /// </summary>
    internal static class ParameterCompressionObservability
    {
        private sealed class Reads
        {
            internal readonly List<(AnimatorConditionMode mode, float threshold)> conditions =
                new List<(AnimatorConditionMode, float)>();
            internal bool read;
            internal bool asNumber;
            internal bool remotelyWritten;
        }



        private static readonly Dictionary<VRCAvatarDescriptor.AnimLayerType, string>
            DefaultControllers = new Dictionary<VRCAvatarDescriptor.AnimLayerType, string>
            {
                { VRCAvatarDescriptor.AnimLayerType.Base, "vrc_AvatarV3LocomotionLayer" },
                { VRCAvatarDescriptor.AnimLayerType.Additive, "vrc_AvatarV3IdleLayer" },
                { VRCAvatarDescriptor.AnimLayerType.Gesture, "vrc_AvatarV3HandsLayer" },
                { VRCAvatarDescriptor.AnimLayerType.Action, "vrc_AvatarV3ActionLayer" },
                { VRCAvatarDescriptor.AnimLayerType.FX, "vrc_AvatarV3FaceLayer" },
                { VRCAvatarDescriptor.AnimLayerType.Sitting, "vrc_AvatarV3SittingLayer" },
                { VRCAvatarDescriptor.AnimLayerType.TPose, "vrc_AvatarV3UtilityTPose" },
                { VRCAvatarDescriptor.AnimLayerType.IKPose, "vrc_AvatarV3UtilityIKPose" }
            };

        internal static ParameterObservability Analyze(
            VRCAvatarDescriptor descriptor,
            VRCExpressionParameters parameters,
            Func<VRCAvatarDescriptor.AnimLayerType, bool> includeLayer = null)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));

            var controllers = new List<AnimatorController>();
            string incomplete = null;
            foreach (var layer in (descriptor.baseAnimationLayers ??
                                   Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                     .Concat(descriptor.specialAnimationLayers ??
                             Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>()))
            {
                if (includeLayer != null && !includeLayer(layer.type)) continue;
                var runtime = layer.isDefault || !descriptor.customizeAnimationLayers
                    ? null
                    : layer.animatorController;
                if (runtime == null)
                {
                    var fallback = FindDefaultController(layer.type);
                    if (fallback == null)
                        incomplete = "The SDK's default " + layer.type +
                                     " controller could not be found to inspect.";
                    else controllers.Add(fallback);
                    continue;
                }
                var resolved = Resolve(runtime);
                if (resolved == null)
                    incomplete = "The " + layer.type + " layer uses a " +
                                 runtime.GetType().Name + " that cannot be inspected.";
                else controllers.Add(resolved);
            }

            return Analyze(controllers.Distinct(), parameters, incomplete);
        }

        /// <summary>Analysis over explicit controllers, used by the descriptor
        /// overload and directly by tests.</summary>
        internal static ParameterObservability Analyze(
            IEnumerable<AnimatorController> controllers,
            VRCExpressionParameters parameters,
            string incompleteReason = null)
        {
            var synced = (parameters.parameters ??
                          Array.Empty<VRCExpressionParameters.Parameter>())
                .Where(parameter => parameter != null &&
                                    !string.IsNullOrWhiteSpace(parameter.name) &&
                                    parameter.networkSynced)
                .GroupBy(parameter => parameter.name, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            var expressionTypes = synced.ToDictionary(
                parameter => parameter.name, parameter => parameter.valueType,
                StringComparer.Ordinal);
            var reads = synced.ToDictionary(
                parameter => parameter.name, _ => new Reads(), StringComparer.Ordinal);

            foreach (var controller in controllers)
                if (controller != null)
                    CollectReads(controller, expressionTypes, reads);

            var output = new Dictionary<string, ParameterObservation>(StringComparer.Ordinal);
            foreach (var parameter in synced)
            {
                var read = reads[parameter.name];
                output.Add(parameter.name, new ParameterObservation(
                    parameter.name,
                    parameter.valueType,
                    parameter.defaultValue,
                    Partition(parameter.valueType, read),
                    read.read,
                    read.asNumber,
                    read.remotelyWritten));
            }
            return new ParameterObservability(
                incompleteReason == null, incompleteReason, output);
        }

        internal static float[] NetworkValues(VRCExpressionParameters.ValueType type)
        {
            switch (type)
            {
                case VRCExpressionParameters.ValueType.Bool:
                    return new[] { 0f, 1f };
                case VRCExpressionParameters.ValueType.Int:
                    return Enumerable.Range(0, 256).Select(value => (float)value).ToArray();
                default:
                    return Enumerable.Range(-127, 255).Select(code => code / 127f).ToArray();
            }
        }

        internal static float Quantize(VRCExpressionParameters.ValueType type, float value)
        {
            switch (type)
            {
                case VRCExpressionParameters.ValueType.Bool:
                    return Mathf.Approximately(value, 0f) ? 0f : 1f;
                case VRCExpressionParameters.ValueType.Int:
                    return Mathf.Clamp(Mathf.RoundToInt(value), 0, 255);
                default:
                    return Mathf.Clamp(Mathf.RoundToInt(value * 127f), -127, 127) / 127f;
            }
        }

        private static IReadOnlyList<float[]> Partition(
            VRCExpressionParameters.ValueType type,
            Reads read)
        {
            var values = NetworkValues(type);
            if (!read.read) return new[] { values };
            if (read.asNumber) return values.Select(value => new[] { value }).ToArray();

            var conditions = read.conditions.Distinct().ToArray();
            return values
                .GroupBy(value => string.Concat(conditions.Select(condition =>
                    Holds(condition.mode, condition.threshold, value) ? '1' : '0')))
                .Select(group => group.ToArray())
                .OrderBy(cell => cell[0])
                .ToArray();
        }

        private static bool Holds(AnimatorConditionMode mode, float threshold, float value)
        {
            switch (mode)
            {
                case AnimatorConditionMode.If: return value != 0f;
                case AnimatorConditionMode.IfNot: return value == 0f;
                case AnimatorConditionMode.Greater: return value > threshold;
                case AnimatorConditionMode.Less: return value < threshold;
                case AnimatorConditionMode.Equals: return value == threshold;
                case AnimatorConditionMode.NotEqual: return value != threshold;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        private static void CollectReads(
            AnimatorController controller,
            IReadOnlyDictionary<string, VRCExpressionParameters.ValueType> expressionTypes,
            IReadOnlyDictionary<string, Reads> reads)
        {
            var declared = new Dictionary<string, AnimatorControllerParameterType>(
                StringComparer.Ordinal);
            foreach (var parameter in controller.parameters)
                declared[parameter.name] = parameter.type;

            void AsNumber(string name)
            {
                if (string.IsNullOrEmpty(name) || !reads.TryGetValue(name, out var read)) return;
                read.read = true;
                read.asNumber = true;
            }

            void Condition(AnimatorCondition condition)
            {
                if (!reads.TryGetValue(condition.parameter ?? string.Empty, out var read)) return;
                read.read = true;
                if (!ConditionIsExact(condition, expressionTypes[condition.parameter],
                        declared.TryGetValue(condition.parameter, out var animatorType)
                            ? animatorType
                            : (AnimatorControllerParameterType?)null))
                {
                    read.asNumber = true;
                    return;
                }
                read.conditions.Add((condition.mode, condition.threshold));
            }

            void Transitions(IEnumerable<AnimatorTransitionBase> transitions)
            {
                foreach (var transition in transitions)
                    if (transition != null)
                        foreach (var condition in transition.conditions)
                            Condition(condition);
            }

            void Motion(Motion motion)
            {
                if (!(motion is BlendTree tree)) return;
                if (tree.blendType == BlendTreeType.Direct)
                {
                    foreach (var child in tree.children)
                        AsNumber(child.directBlendParameter);
                }
                else
                {
                    AsNumber(tree.blendParameter);
                    if (tree.blendType != BlendTreeType.Simple1D)
                        AsNumber(tree.blendParameterY);
                }
                foreach (var child in tree.children) Motion(child.motion);
            }

            void Behaviours(IEnumerable<StateMachineBehaviour> behaviours)
            {
                foreach (var behaviour in behaviours)
                {
                    if (behaviour == null) continue;
                    if (behaviour is VRCAvatarParameterDriver driver)
                    {
                        if (driver.localOnly || driver.parameters == null) continue;
                        foreach (var write in driver.parameters)
                        {
                            if (write == null) continue;
                            if (reads.TryGetValue(write.name ?? string.Empty, out var target))
                                target.remotelyWritten = true;
                            if (write.type == VRC_AvatarParameterDriver.ChangeType.Copy)
                                AsNumber(write.source);
                            else if (write.type == VRC_AvatarParameterDriver.ChangeType.Add)
                                AsNumber(write.name);
                        }
                        continue;
                    }



                    var serialized = new SerializedObject(behaviour);
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.String)
                            AsNumber(property.stringValue);
                }
            }

            void Machine(AnimatorStateMachine machine)
            {
                if (machine == null) return;
                Transitions(machine.anyStateTransitions);
                Transitions(machine.entryTransitions);
                Behaviours(machine.behaviours);
                foreach (var child in machine.states)
                {
                    var state = child.state;
                    if (state == null) continue;
                    Transitions(state.transitions);
                    Motion(state.motion);
                    Behaviours(state.behaviours);
                    if (state.speedParameterActive) AsNumber(state.speedParameter);
                    if (state.timeParameterActive) AsNumber(state.timeParameter);
                    if (state.mirrorParameterActive) AsNumber(state.mirrorParameter);
                    if (state.cycleOffsetParameterActive) AsNumber(state.cycleOffsetParameter);
                }
                foreach (var child in machine.stateMachines)
                {
                    Transitions(machine.GetStateMachineTransitions(child.stateMachine));
                    Machine(child.stateMachine);
                }
            }

            foreach (var layer in controller.layers) Machine(layer.stateMachine);
        }

        /// <summary>
        /// A condition is evaluated exactly on the network value only when the
        /// Animator's declared type receives that value unchanged: a Bool arrives
        /// as 0 or 1 in any type, and an Int arrives as itself in Int or Float.
        /// Any other conversion, an undeclared parameter, a fractional Int
        /// threshold or a mode the type does not support counts as a numeric read.
        /// </summary>
        private static bool ConditionIsExact(
            AnimatorCondition condition,
            VRCExpressionParameters.ValueType expressionType,
            AnimatorControllerParameterType? animatorType)
        {
            if (animatorType == null) return false;
            var exactConversion =
                expressionType == VRCExpressionParameters.ValueType.Bool ||
                expressionType == VRCExpressionParameters.ValueType.Int &&
                (animatorType == AnimatorControllerParameterType.Int ||
                 animatorType == AnimatorControllerParameterType.Float) ||
                expressionType == VRCExpressionParameters.ValueType.Float &&
                animatorType == AnimatorControllerParameterType.Float;
            if (!exactConversion) return false;

            switch (animatorType.Value)
            {
                case AnimatorControllerParameterType.Bool:
                    return condition.mode == AnimatorConditionMode.If ||
                           condition.mode == AnimatorConditionMode.IfNot;
                case AnimatorControllerParameterType.Float:
                    return condition.mode == AnimatorConditionMode.Greater ||
                           condition.mode == AnimatorConditionMode.Less;
                case AnimatorControllerParameterType.Int:
                    return (condition.mode == AnimatorConditionMode.Greater ||
                            condition.mode == AnimatorConditionMode.Less ||
                            condition.mode == AnimatorConditionMode.Equals ||
                            condition.mode == AnimatorConditionMode.NotEqual) &&
                           condition.threshold == Mathf.Round(condition.threshold);
                default:
                    return false;
            }
        }

        private static AnimatorController Resolve(RuntimeAnimatorController runtime)
        {
            // An override controller only swaps clips, so its base reads parameters.
            while (runtime is AnimatorOverrideController overrideController)
                runtime = overrideController.runtimeAnimatorController;
            return runtime as AnimatorController;
        }

        private static AnimatorController FindDefaultController(
            VRCAvatarDescriptor.AnimLayerType type)
        {
            if (!DefaultControllers.TryGetValue(type, out var fileName)) return null;
            return AssetDatabase.FindAssets(fileName + " t:AnimatorController")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(
                    System.IO.Path.GetFileNameWithoutExtension(path), fileName,
                    StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<AnimatorController>)
                .FirstOrDefault(controller => controller != null);
        }
    }
}
