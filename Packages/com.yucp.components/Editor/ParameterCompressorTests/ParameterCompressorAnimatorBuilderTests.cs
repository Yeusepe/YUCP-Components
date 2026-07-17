using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace YUCP.Components.Editor.Tests
{
    public sealed class ParameterCompressorAnimatorBuilderTests
    {
        private const string Prefix = "YUCP/TestCompressor";
        private const string LayerName = "YUCP Parameter Compressor";

        [TestCase(0, 0, 0)]
        [TestCase(18, 0, 18)]
        [TestCase(19, 1, 0)]
        [TestCase(254, 13, 7)]
        public void GeneratedSenderConsumesRadixNineteenValuesMostSignificantFirst(
            int quantizedValue,
            int expectedHighDigit,
            int expectedLowDigit)
        {
            var controller = BuildController(1, out _);
            try
            {
                var machine = controller.layers.Single(layer =>
                    layer.name == LayerName).stateMachine;
                var router = machine.states.Select(child => child.state).Single(state =>
                    state.name == "Local Cursor Router");
                var load = router.transitions.Single(transition =>
                    transition.destinationState != null &&
                    transition.destinationState.name.StartsWith(
                        "Local Load 0 ", StringComparison.Ordinal));
                var sendCursorName = Prefix + "/_Internal/SendCursor";
                var sendWorkName = Prefix + "/_Internal/SendWork";
                var cursor = Mathf.RoundToInt(load.conditions.Single(condition =>
                    condition.parameter == sendCursorName &&
                    condition.mode == AnimatorConditionMode.Equals).threshold) + 1;
                var work = quantizedValue;
                var decoded = new List<int>();
                var alphabet = new ParameterCompressionAlphabet(6);

                for (var step = 0; step < 2; step++)
                {
                    var route = router.transitions
                        .Where(transition => transition.destinationState != null &&
                                             transition.destinationState.name.StartsWith(
                                                 "Local Digit ", StringComparison.Ordinal))
                        .Single(transition => ConditionsMatch(
                            transition.conditions,
                            sendCursorName,
                            cursor,
                            sendWorkName,
                            work));
                    var driver = route.destinationState.behaviours
                        .OfType<VRCAvatarParameterDriver>()
                        .Single(behaviour => behaviour.localOnly);

                    var word = 0;
                    for (var bit = 0; bit < 6; bit++)
                    {
                        var carrier = Prefix + "/_Bus/Bit" + bit;
                        var write = driver.parameters.Single(parameter =>
                            parameter.name == carrier &&
                            parameter.type ==
                            VRC_AvatarParameterDriver.ChangeType.Set);
                        if (write.value > 0.5f) word |= 1 << bit;
                    }
                    Assert.That(alphabet.TryDecodeDigit(word, out var digit), Is.True);
                    decoded.Add(digit);

                    foreach (var parameter in driver.parameters.Where(parameter =>
                                 parameter.type ==
                                 VRC_AvatarParameterDriver.ChangeType.Add))
                    {
                        if (parameter.name == sendWorkName)
                            work += Mathf.RoundToInt(parameter.value);
                        if (parameter.name == sendCursorName)
                            cursor += Mathf.RoundToInt(parameter.value);
                    }
                }

                Assert.That(decoded, Is.EqualTo(new[]
                {
                    expectedHighDigit,
                    expectedLowDigit
                }));
                Assert.That(work, Is.Zero,
                    "Every emitted digit must be removed from the sender accumulator.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controller);
            }
        }

        [Test]
        public void SixWireTwentySixFloatGraphIsFlatBoundedAndPositional()
        {
            var controller = BuildController(26, out var result);
            try
            {
                Assert.That(result.busBits, Is.EqualTo(6));
                Assert.That(result.radix, Is.EqualTo(19));
                Assert.That(result.blockCount, Is.EqualTo(4));
                Assert.That(result.blockIdDigits, Is.EqualTo(1));
                Assert.That(result.signalSymbolsPerCycle, Is.EqualTo(60));
                Assert.That(result.carrierParameters, Is.EqualTo(
                    Enumerable.Range(0, 6).Select(index =>
                        Prefix + "/_Bus/Bit" + index)));
                Assert.That(result.stagingParameters, Is.EqualTo(
                    Enumerable.Range(0, 26).Select(index =>
                        Prefix + "/_Internal/Staging/" + index)));
                Assert.That(result.estimatedFullRefreshSeconds,
                    Is.GreaterThan(0f).And.LessThan(float.PositiveInfinity));

                var layer = controller.layers.Single(item => item.name == LayerName);
                var machine = layer.stateMachine;
                var states = machine.states.Select(child => child.state).ToArray();
                Assert.That(layer.defaultWeight, Is.Zero);
                Assert.That(machine.anyStateTransitions, Is.Empty);
                Assert.That(machine.stateMachines, Is.Empty,
                    "The transport should stay in one flat utility layer.");
                Assert.That(states.Select(state => state.motion)
                    .OfType<BlendTree>(), Is.Empty);
                Assert.That(result.generatedStates, Is.EqualTo(states.Length));
                Assert.That(states.Length, Is.LessThan(300));

                Assert.That(states.Any(state =>
                    state.name == "Local Digit d19 v13"), Is.True);
                Assert.That(states.Any(state =>
                    state.name == "Local Digit d1 v18"), Is.True);
                Assert.That(states.Any(state =>
                    state.name == "Remote Digit d19 v13"), Is.True);
                Assert.That(states.Any(state =>
                    state.name == "Remote Digit d1 v18"), Is.True);

                var parameters = controller.parameters.ToDictionary(parameter =>
                    parameter.name, StringComparer.Ordinal);
                Assert.That(parameters, Has.Count.EqualTo(64));
                Assert.That(result.carrierParameters, Has.All.Matches<string>(name =>
                    parameters[name].type == AnimatorControllerParameterType.Bool));
                Assert.That(result.stagingParameters, Has.All.Matches<string>(name =>
                    parameters[name].type == AnimatorControllerParameterType.Float));
                for (var index = 0; index < 26; index++)
                    Assert.That(parameters["Compressed/Float" + index.ToString("D2")].type,
                        Is.EqualTo(AnimatorControllerParameterType.Float));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controller);
            }
        }

        private static AnimatorController BuildController(
            int floatCount,
            out ParameterCompressorAnimatorBuilder.Result result)
        {
            var controller = new AnimatorController
            {
                name = "Parameter Compressor Test Controller"
            };
            result = ParameterCompressorAnimatorBuilder.Build(
                new ParameterCompressorAnimatorBuilder.Request
                {
                    controller = controller,
                    prefix = Prefix,
                    busBits = 6,
                    blockSize = 8,
                    entries = Enumerable.Range(0, floatCount)
                        .Select(index => new ParameterCompressorAnimatorBuilder.Entry
                        {
                            name = "Compressed/Float" + index.ToString("D2"),
                            type = AnimatorControllerParameterType.Float,
                            levels = 255,
                            minimum = -1f,
                            maximum = 1f
                        })
                        .ToArray()
                });
            return controller;
        }

        private static bool ConditionsMatch(
            IEnumerable<AnimatorCondition> conditions,
            string sendCursorName,
            int cursor,
            string sendWorkName,
            int work)
        {
            foreach (var condition in conditions)
            {
                float value;
                if (condition.parameter == sendCursorName) value = cursor;
                else if (condition.parameter == sendWorkName) value = work;
                else if (condition.parameter == "IsLocal") value = 1f;
                else if (condition.parameter.StartsWith(
                             Prefix + "/_Bus/Bit", StringComparison.Ordinal)) value = 0f;
                else value = 0f;

                var matches = condition.mode switch
                {
                    AnimatorConditionMode.If => value > 0.5f,
                    AnimatorConditionMode.IfNot => value <= 0.5f,
                    AnimatorConditionMode.Greater => value > condition.threshold,
                    AnimatorConditionMode.Less => value < condition.threshold,
                    AnimatorConditionMode.Equals => Mathf.Approximately(
                        value, condition.threshold),
                    AnimatorConditionMode.NotEqual => !Mathf.Approximately(
                        value, condition.threshold),
                    _ => false
                };
                if (!matches) return false;
            }
            return true;
        }
    }
}
