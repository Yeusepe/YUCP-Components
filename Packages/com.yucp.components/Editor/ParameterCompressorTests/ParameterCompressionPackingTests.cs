using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace YUCP.Components.Editor.Tests
{
    public sealed class ParameterCompressionPackingTests
    {
        private const string Prefix = "YUCP/TestCompressor";

        [Test]
        public void ObservabilityPartitionsByRemoteReadsOnly()
        {
            var controller = Controller(
                ("Selector", AnimatorControllerParameterType.Int),
                ("Slider", AnimatorControllerParameterType.Int),
                ("Toggle", AnimatorControllerParameterType.Float),
                ("LocalOnlySource", AnimatorControllerParameterType.Float),
                ("RemoteSource", AnimatorControllerParameterType.Float),
                ("Copied", AnimatorControllerParameterType.Float));
            var machine = controller.layers[0].stateMachine;
            var a = machine.AddState("A");
            var b = machine.AddState("B");
            var toB = a.AddTransition(b);
            toB.AddCondition(AnimatorConditionMode.Equals, 1f, "Selector");
            toB.AddCondition(AnimatorConditionMode.Greater, 0.5f, "Toggle");
            a.AddTransition(b).AddCondition(AnimatorConditionMode.Equals, 3f, "Selector");
            var tree = new BlendTree { blendParameter = "Slider" };
            tree.AddChild(null, 0f);
            tree.AddChild(null, 1f);
            b.motion = tree;
            a.behaviours = new StateMachineBehaviour[]
            {
                Driver(true, Copy("LocalOnlySource", "Copied")),
                Driver(false, Copy("RemoteSource", "Copied"))
            };

            var result = ParameterCompressionObservability.Analyze(
                new[] { controller },
                Parameters(
                    ("Selector", VRCExpressionParameters.ValueType.Int, 0f),
                    ("Slider", VRCExpressionParameters.ValueType.Int, 0f),
                    ("Toggle", VRCExpressionParameters.ValueType.Bool, 0f),
                    ("LocalOnlySource", VRCExpressionParameters.ValueType.Float, 0f),
                    ("RemoteSource", VRCExpressionParameters.ValueType.Float, 0f),
                    ("Unused", VRCExpressionParameters.ValueType.Int, 0f)));

            Assert.That(result.Complete, Is.True);
            var observed = result.Observations;
            // Equals 1 and Equals 3 split 0..255 into {1}, {3} and everything else.
            Assert.That(observed["Selector"].CellCount, Is.EqualTo(3));
            Assert.That(observed["Selector"].Cells.Single(cell => cell.Length > 1).Length,
                Is.EqualTo(254));
            Assert.That(observed["Slider"].CellCount, Is.EqualTo(256));
            // A Bool read through a Float declaration still has its two values.
            Assert.That(observed["Toggle"].CellCount, Is.EqualTo(2));
            Assert.That(observed["LocalOnlySource"].Read, Is.False);
            Assert.That(observed["LocalOnlySource"].CellCount, Is.EqualTo(1));
            Assert.That(observed["RemoteSource"].ReadAsNumber, Is.True);
            Assert.That(observed["RemoteSource"].CellCount, Is.EqualTo(255));
            Assert.That(observed["Unused"].Read, Is.False);
        }

        [Test]
        public void ConditionThatDisagreesWithTheDeclaredTypeIsTreatedAsANumericRead()
        {
            var controller = Controller(("Value", AnimatorControllerParameterType.Int));
            var machine = controller.layers[0].stateMachine;
            machine.AddState("A").AddTransition(machine.AddState("B"))
                .AddCondition(AnimatorConditionMode.Greater, 0.5f, "Value");

            var result = ParameterCompressionObservability.Analyze(
                new[] { controller },
                Parameters(("Value", VRCExpressionParameters.ValueType.Float, 0f)));

            Assert.That(result.Observations["Value"].ReadAsNumber, Is.True);
        }

        [Test]
        public void PlannerPacksSmallAlphabetsAndKeepsOnlyGroupsThatSaveBits()
        {
            var groups = ParameterCompressionPacking.Plan(new[]
            {
                Candidate("Outfit", VRCExpressionParameters.ValueType.Int, 6),
                Candidate("Hair", VRCExpressionParameters.ValueType.Int, 6),
                Candidate("Shoes", VRCExpressionParameters.ValueType.Int, 6),
                Candidate("Wide", VRCExpressionParameters.ValueType.Int, 200),
                Candidate("Hat", VRCExpressionParameters.ValueType.Bool, 2)
            }, Prefix);

            Assert.That(groups.Count, Is.EqualTo(1));
            var group = groups[0];
            // 6 * 6 * 6 = 216; the Boolean would need 432 and does not fit.
            Assert.That(group.Members.Select(member => member.Candidate.Name),
                Is.EquivalentTo(new[] { "Outfit", "Hair", "Shoes" }));
            Assert.That(group.Cardinality, Is.EqualTo(216));
            Assert.That(group.SavedBits, Is.EqualTo(16));
            Assert.That(group.Carrier, Is.EqualTo(Prefix + "/_Pack/0"));
        }

        [Test]
        public void TwoCellIntegerTravelsAsOneBooleanCarrier()
        {
            var groups = ParameterCompressionPacking.Plan(new[]
            {
                Candidate("Mode", VRCExpressionParameters.ValueType.Int, 2)
            }, Prefix);

            Assert.That(groups.Count, Is.EqualTo(1));
            Assert.That(groups[0].BoolCarrier, Is.True);
            Assert.That(groups[0].SavedBits, Is.EqualTo(7));
        }

        [Test]
        public void BooleansAloneNeverFormAGroup()
        {
            var groups = ParameterCompressionPacking.Plan(Enumerable.Range(0, 8)
                .Select(index => Candidate("Flag" + index,
                    VRCExpressionParameters.ValueType.Bool, 2)), Prefix);

            Assert.That(groups, Is.Empty);
        }

        [Test]
        public void GeneratedGraphRoundTripsEveryObservableStateExactly()
        {
            // An Int remote clients compare against two values, a Bool that
            // VRCFury-style controllers declare as Float, a plain Bool, and a
            // Float read only through two thresholds.
            var source = Controller(
                ("Emote", AnimatorControllerParameterType.Int),
                ("Hoodie", AnimatorControllerParameterType.Float),
                ("Glasses", AnimatorControllerParameterType.Bool),
                ("Blend", AnimatorControllerParameterType.Float));
            var machine = source.layers[0].stateMachine;
            var a = machine.AddState("A");
            var b = machine.AddState("B");
            a.AddTransition(b).AddCondition(AnimatorConditionMode.Equals, 2f, "Emote");
            a.AddTransition(b).AddCondition(AnimatorConditionMode.Greater, 9f, "Emote");
            a.AddTransition(b).AddCondition(AnimatorConditionMode.Greater, 0.5f, "Hoodie");
            a.AddTransition(b).AddCondition(AnimatorConditionMode.If, 0f, "Glasses");
            a.AddTransition(b).AddCondition(AnimatorConditionMode.Greater, 0.25f, "Blend");
            a.AddTransition(b).AddCondition(AnimatorConditionMode.Less, -0.5f, "Blend");

            var observed = ParameterCompressionObservability.Analyze(
                new[] { source },
                Parameters(
                    ("Emote", VRCExpressionParameters.ValueType.Int, 2f),
                    ("Hoodie", VRCExpressionParameters.ValueType.Bool, 1f),
                    ("Glasses", VRCExpressionParameters.ValueType.Bool, 0f),
                    ("Blend", VRCExpressionParameters.ValueType.Float, 0f))).Observations;
            var groups = ParameterCompressionPacking.Plan(new[]
            {
                new ParameterPackCandidate(observed["Emote"], AnimatorControllerParameterType.Int, false),
                new ParameterPackCandidate(observed["Hoodie"], AnimatorControllerParameterType.Float, true),
                new ParameterPackCandidate(observed["Glasses"], AnimatorControllerParameterType.Bool, true),
                new ParameterPackCandidate(observed["Blend"], AnimatorControllerParameterType.Float, false)
            }, Prefix);
            Assert.That(groups.Count, Is.EqualTo(1));
            var group = groups[0];
            Assert.That(group.Cardinality, Is.EqualTo(3 * 3 * 2 * 2));

            var target = new AnimatorController();
            target.AddLayer("Base");
            foreach (var parameter in source.parameters) target.AddParameter(parameter);
            ParameterCompressionPacking.Build(target, Prefix, groups);
            var layer = target.layers.Single(item => item.name.StartsWith(
                "YUCP Parameter Compressor Pack", StringComparison.Ordinal)).stateMachine;
            var router = layer.defaultState;
            Assert.That(router.name, Is.EqualTo("Router"));
            Assert.That(layer.anyStateTransitions, Is.Empty);

            // Every network value of every member, in every combination.
            var memberValues = group.Members.Select(member =>
                ParameterCompressionObservability.NetworkValues(
                    member.Observation.ValueType)).ToArray();
            var checkedStates = 0;
            foreach (var combination in Cartesian(memberValues))
            {
                var local = new Dictionary<string, float> { { "IsLocal", 1f } };
                for (var index = 0; index < group.Members.Count; index++)
                    local[group.Members[index].Candidate.Name] = combination[index];

                var encodeRoutes = router.transitions.Where(transition =>
                    transition.destinationState.name.StartsWith("Encode ", StringComparison.Ordinal) &&
                    Holds(transition.conditions, local)).ToArray();
                Assert.That(encodeRoutes, Has.Length.EqualTo(1),
                    "Exactly one code must match " + string.Join(",", combination));
                var encode = encodeRoutes[0].destinationState;
                var code = Mathf.RoundToInt(SingleSet(encode, true, group.Carrier));
                Assert.That(encode.transitions.Where(transition => !transition.hasExitTime)
                        .Any(transition => Holds(transition.conditions, local)), Is.False,
                    "An encode state must hold while its values stay in their cells.");

                var remote = new Dictionary<string, float>
                {
                    { "IsLocal", 0f },
                    { group.Carrier, code }
                };
                var decodeRoutes = router.transitions.Where(transition =>
                    transition.destinationState.name.StartsWith("Decode ", StringComparison.Ordinal) &&
                    Holds(transition.conditions, remote)).ToArray();
                Assert.That(decodeRoutes, Has.Length.EqualTo(1));
                var decode = decodeRoutes[0].destinationState;
                var writes = decode.behaviours.OfType<VRCAvatarParameterDriver>()
                    .Single(driver => !driver.localOnly).parameters
                    .ToDictionary(write => write.name, write => write.value);

                for (var index = 0; index < group.Members.Count; index++)
                {
                    var observation = group.Members[index].Observation;
                    var original = observation.CellOf(ParameterCompressionObservability.Quantize(
                        observation.ValueType, combination[index]));
                    var decoded = observation.CellOf(writes[observation.Name]);
                    Assert.That(decoded, Is.EqualTo(original),
                        observation.Name + " must decode into the cell it was sent from.");
                }
                checkedStates++;
            }
            Assert.That(checkedStates, Is.EqualTo(256 * 2 * 2 * 255));
        }

        [Test]
        public void CarrierDefaultEncodesEveryMemberDefault()
        {
            var controller = Controller(("Emote", AnimatorControllerParameterType.Int));
            var machine = controller.layers[0].stateMachine;
            machine.AddState("A").AddTransition(machine.AddState("B"))
                .AddCondition(AnimatorConditionMode.Equals, 4f, "Emote");
            var observed = ParameterCompressionObservability.Analyze(
                new[] { controller },
                Parameters(("Emote", VRCExpressionParameters.ValueType.Int, 4f))).Observations;
            // Equals 4 leaves two cells, so the Int travels as one Boolean.
            var group = ParameterCompressionPacking.Plan(new[]
            {
                new ParameterPackCandidate(observed["Emote"], AnimatorControllerParameterType.Int, false)
            }, Prefix);

            Assert.That(group.Count, Is.EqualTo(1));
            var emote = group[0].Members.Single().Observation;
            Assert.That(group[0].CellOf(group[0].DefaultCode, 0),
                Is.EqualTo(emote.CellOf(4f)));
            Assert.That(emote.Representatives[emote.CellOf(4f)], Is.EqualTo(4f));
        }

        private static IEnumerable<float[]> Cartesian(IReadOnlyList<float[]> axes)
        {
            var indices = new int[axes.Count];
            while (true)
            {
                yield return indices.Select((value, axis) => axes[axis][value]).ToArray();
                var position = 0;
                while (position < indices.Length && ++indices[position] == axes[position].Length)
                    indices[position++] = 0;
                if (position == indices.Length) yield break;
            }
        }

        private static bool Holds(
            IEnumerable<AnimatorCondition> conditions,
            IReadOnlyDictionary<string, float> values)
        {
            foreach (var condition in conditions)
            {
                var value = values[condition.parameter];
                bool holds;
                switch (condition.mode)
                {
                    case AnimatorConditionMode.If: holds = value != 0f; break;
                    case AnimatorConditionMode.IfNot: holds = value == 0f; break;
                    case AnimatorConditionMode.Greater: holds = value > condition.threshold; break;
                    case AnimatorConditionMode.Less: holds = value < condition.threshold; break;
                    case AnimatorConditionMode.Equals: holds = value == condition.threshold; break;
                    case AnimatorConditionMode.NotEqual: holds = value != condition.threshold; break;
                    default: throw new ArgumentOutOfRangeException();
                }
                if (!holds) return false;
            }
            return true;
        }

        private static float SingleSet(AnimatorState state, bool localOnly, string name)
        {
            return state.behaviours.OfType<VRCAvatarParameterDriver>()
                .Single(driver => driver.localOnly == localOnly).parameters
                .Single(write => write.name == name &&
                                 write.type == VRC_AvatarParameterDriver.ChangeType.Set).value;
        }

        private static ParameterPackCandidate Candidate(
            string name,
            VRCExpressionParameters.ValueType type,
            int cells)
        {
            var values = ParameterCompressionObservability.NetworkValues(type);
            var partition = Enumerable.Range(0, cells).Select(cell =>
                    values.Where((_, index) => Math.Min(index, cells - 1) == cell).ToArray())
                .ToArray();
            var observation = new ParameterObservation(
                name, type, 0f, partition, true, false, false);
            return new ParameterPackCandidate(observation,
                type == VRCExpressionParameters.ValueType.Bool
                    ? AnimatorControllerParameterType.Bool
                    : type == VRCExpressionParameters.ValueType.Int
                        ? AnimatorControllerParameterType.Int
                        : AnimatorControllerParameterType.Float,
                false);
        }

        private static AnimatorController Controller(
            params (string name, AnimatorControllerParameterType type)[] parameters)
        {
            var controller = new AnimatorController();
            controller.AddLayer("Base");
            foreach (var (name, type) in parameters)
                controller.AddParameter(name, type);
            return controller;
        }

        private static VRCExpressionParameters Parameters(
            params (string name, VRCExpressionParameters.ValueType type, float value)[] parameters)
        {
            var asset = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            asset.parameters = parameters.Select(parameter =>
                new VRCExpressionParameters.Parameter
                {
                    name = parameter.name,
                    valueType = parameter.type,
                    defaultValue = parameter.value,
                    networkSynced = true
                }).ToArray();
            return asset;
        }

        private static VRCAvatarParameterDriver Driver(
            bool localOnly,
            params VRC_AvatarParameterDriver.Parameter[] writes)
        {
            var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
            driver.localOnly = localOnly;
            driver.parameters = writes.ToList();
            return driver;
        }

        private static VRC_AvatarParameterDriver.Parameter Copy(string source, string destination)
        {
            return new VRC_AvatarParameterDriver.Parameter
            {
                type = VRC_AvatarParameterDriver.ChangeType.Copy,
                source = source,
                name = destination
            };
        }
    }
}
