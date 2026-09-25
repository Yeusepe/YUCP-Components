using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace YUCP.Components.Editor.Tests
{
    public sealed class ParameterPrecisionReductionTests
    {
        [Test]
        public void ReplicationKeepsZeroFullScaleAndOrder()
        {
            for (var bits = 2; bits <= 8; bits++)
            {
                var maximum = (1 << bits) - 1;
                Assert.That(ParameterPrecisionReduction.ReplicationError(bits, 0), Is.EqualTo(0d));
                for (var dropped = 0; dropped < bits; dropped++)
                {
                    Assert.That(ParameterPrecisionReduction.Replicate(0, bits, dropped), Is.EqualTo(0));
                    Assert.That(ParameterPrecisionReduction.Replicate(maximum, bits, dropped), Is.EqualTo(maximum));
                    for (var code = 1; code <= maximum; code++)
                        Assert.That(ParameterPrecisionReduction.Replicate(code, bits, dropped),
                            Is.GreaterThanOrEqualTo(ParameterPrecisionReduction.Replicate(code - 1, bits, dropped)));
                    if (dropped > 0)
                        Assert.That(ParameterPrecisionReduction.ReplicationError(bits, dropped),
                            Is.GreaterThan(ParameterPrecisionReduction.ReplicationError(bits, dropped - 1)));
                }
            }
        }

        [Test]
        public void EachDroppedPlaneCopiesTheBitReplicationPredicts()
        {
            // The remote decoder copies planes; this is the same mapping as the math.
            for (var bits = 2; bits <= 7; bits++)
            for (var dropped = 1; dropped < bits; dropped++)
            for (var code = 0; code < 1 << bits; code++)
            {
                var rebuilt = ParameterPrecisionReduction.Replicate(code, bits, dropped);
                Assert.That(rebuilt >> dropped, Is.EqualTo(code >> dropped));
                for (var plane = 0; plane < dropped; plane++)
                    Assert.That(rebuilt >> plane & 1, Is.EqualTo(
                        code >> ParameterPrecisionReduction.ReplicationSource(bits, dropped, plane) & 1));
            }
        }

        [Test]
        public void FindsContiguousBinaryPlanesWithOptionalSign()
        {
            var groups = ParameterPrecisionReduction.FindGroups(new[]
            {
                "A1", "A2", "A4", "ANegative", "B1", "B2", "C1", "D2", "D4",
                "E1", "E2", "E8", "Viseme11", "Viseme12"
            }).ToDictionary(group => group.Name);

            Assert.That(groups.Keys, Is.EquivalentTo(new[] { "A", "B", "E" }));
            Assert.That(groups["A"].Planes, Is.EqualTo(new[] { "A1", "A2", "A4" }));
            Assert.That(groups["A"].Sign, Is.EqualTo("ANegative"));
            Assert.That(groups["B"].Sign, Is.Null);
            Assert.That(groups["E"].Planes, Is.EqualTo(new[] { "E1", "E2" }));
        }

        [Test]
        public void AllocationDropsSilentValuesWholeThenTheCheapestPlanes()
        {
            var silent = Group("Silent", 3, true, 0d);
            var big = Group("Big", 3, false, 100d);
            var small = Group("Small", 5, false, 1d);
            var guarded = Group("Guarded", 3, false, double.PositiveInfinity);
            var groups = new[] { silent, big, small, guarded };

            var plan = ParameterPrecisionReduction.Allocate(groups, saved => saved >= 8);

            Assert.That(plan, Is.Not.Null);
            Assert.That(silent.Dropped, Is.EqualTo(3));
            Assert.That(silent.SavedBits, Is.EqualTo(4));
            // Four more bits come from the low-weight value, never below one plane.
            Assert.That(small.Dropped, Is.EqualTo(4));
            Assert.That(big.Dropped, Is.EqualTo(0));
            Assert.That(guarded.Dropped, Is.EqualTo(0));
            Assert.That(plan.SavedBits, Is.EqualTo(8));
            Assert.That(plan.UnsyncedNames, Is.EquivalentTo(new[]
            {
                "Silent1", "Silent2", "Silent4", "SilentNegative",
                "Small1", "Small2", "Small4", "Small8"
            }));

            Assert.That(ParameterPrecisionReduction.Allocate(groups, saved => saved >= 100), Is.Null);
        }

        [Test]
        public void RemoteDecoderCopiesDroppedPlanesOnlyOnRemoteClients()
        {
            var controller = new AnimatorController();
            controller.AddLayer("Base");
            foreach (var name in new[] { "A1", "A2", "A4" })
                controller.AddParameter(name, AnimatorControllerParameterType.Float);
            var group = Group("A", 3, false, 1d);
            group.Dropped = 2;

            ParameterPrecisionReduction.Build(controller, "YUCP/Test",
                new ParameterPrecisionPlan(new[] { group }));

            var layer = controller.layers.Last();
            var machine = layer.stateMachine;
            Assert.That(machine.defaultState.name, Is.EqualTo("Wearer"));
            Assert.That(machine.defaultState.behaviours, Is.Empty);
            var copies = machine.states.Select(child => child.state)
                .Where(state => state != machine.defaultState).ToArray();
            Assert.That(copies.Length, Is.EqualTo(2));
            foreach (var state in copies)
            {
                var driver = state.behaviours.OfType<VRCAvatarParameterDriver>().Single();
                Assert.That(driver.localOnly, Is.False);
                Assert.That(driver.parameters.Select(write => (write.type, write.source, write.name)),
                    Is.EquivalentTo(new[]
                    {
                        (VRC_AvatarParameterDriver.ChangeType.Copy, "A4", "A2"),
                        (VRC_AvatarParameterDriver.ChangeType.Copy, "A4", "A1")
                    }));
            }
            foreach (var transition in machine.states.SelectMany(child => child.state.transitions))
                Assert.That(transition.conditions.Select(condition => (condition.parameter, condition.mode)),
                    Is.EqualTo(new[] { ("IsLocal", AnimatorConditionMode.IfNot) }));
        }

        [Test]
        public void SensitivityRanksValuesBySurfaceMotionAndGuardsOtherOutputs()
        {
            var root = new GameObject("Precision Test Avatar");
            try
            {
                root.AddComponent<Animator>();
                var body = new GameObject("Body");
                body.transform.SetParent(root.transform, false);
                var mesh = new Mesh { vertices = new Vector3[4], triangles = new[] { 0, 1, 2, 0, 2, 3 } };
                mesh.AddBlendShapeFrame("Large", 100f, Enumerable.Repeat(Vector3.up * 0.04f, 4).ToArray(), null, null);
                mesh.AddBlendShapeFrame("Fine", 100f, Enumerable.Repeat(Vector3.up * 0.001f, 4).ToArray(), null, null);
                body.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;

                var controller = new AnimatorController();
                controller.AddLayer("FT");
                controller.AddParameter("IsLocal", AnimatorControllerParameterType.Bool);
                var tree = new BlendTree { blendType = BlendTreeType.Direct };
                foreach (var (value, target) in new[] { ("Large", "Body"), ("Fine", "Body"), ("Tint", "Body"), ("Hidden", "Missing"), ("Toggles", "Body") })
                    for (var plane = 0; plane < 3; plane++)
                    {
                        var name = value + (1 << plane);
                        controller.AddParameter(name, AnimatorControllerParameterType.Float);
                        var clip = new AnimationClip();
                        var binding = value == "Tint"
                            ? EditorCurveBinding.FloatCurve(target, typeof(SkinnedMeshRenderer), "material._Glow")
                            : EditorCurveBinding.FloatCurve(target, typeof(SkinnedMeshRenderer), "blendShape." + value);
                        // Three separate toggles share the naming pattern but not the 1:2:4 weights.
                        AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f,
                            value == "Toggles" ? 100f : 100f * (1 << plane) / 7f));
                        tree.AddChild(clip);
                        var children = tree.children;
                        children[children.Length - 1].directBlendParameter = name;
                        tree.children = children;
                    }
                controller.layers[0].stateMachine.AddState("Face").motion = tree;

                var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                var groups = ParameterPrecisionReduction.FindGroups(new[] { "Large", "Fine", "Tint", "Hidden", "Toggles" }
                    .SelectMany(value => new[] { value + 1, value + 2, value + 4 })).ToDictionary(group => group.Name);
                parameters.parameters = groups.Values.SelectMany(group => group.Planes)
                    .Select(name => new VRCExpressionParameters.Parameter
                    {
                        name = name,
                        valueType = VRCExpressionParameters.ValueType.Bool,
                        networkSynced = true
                    }).ToArray();

                ParameterCompressionSensitivity.Measure(root, controller, parameters,
                    groups.Values.ToArray());

                // Four vertices moving 4 cm and 1 mm at full scale.
                Assert.That(groups["Large"].Weight, Is.EqualTo(4 * 0.04 * 0.04).Within(1e-5));
                Assert.That(groups["Fine"].Weight, Is.EqualTo(4 * 0.001 * 0.001).Within(1e-7));
                Assert.That(groups["Tint"].Weight, Is.EqualTo(double.PositiveInfinity));
                // Its clips animate a path that does not exist on this avatar.
                Assert.That(groups["Hidden"].Weight, Is.EqualTo(0d));
                Assert.That(groups["Toggles"].Weight, Is.EqualTo(double.PositiveInfinity));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SensitivityFindsTheContextWhereGatedValuesAreVisible()
        {
            var root = new GameObject("Precision Gate Test Avatar");
            try
            {
                root.AddComponent<Animator>();
                var body = new GameObject("Body");
                body.transform.SetParent(root.transform, false);
                var mesh = new Mesh { vertices = new Vector3[2] };
                mesh.AddBlendShapeFrame("Mouth", 100f, Enumerable.Repeat(Vector3.up * 0.01f, 2).ToArray(), null, null);
                body.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;

                var controller = new AnimatorController();
                controller.AddLayer("Visemes");
                controller.AddParameter("IsLocal", AnimatorControllerParameterType.Bool);
                controller.AddParameter("Enabled", AnimatorControllerParameterType.Bool);
                controller.AddParameter("Active", AnimatorControllerParameterType.Bool);
                var tree = new BlendTree { blendType = BlendTreeType.Direct };
                for (var plane = 0; plane < 2; plane++)
                {
                    controller.AddParameter("Viseme" + (1 << plane), AnimatorControllerParameterType.Float);
                    var clip = new AnimationClip();
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Mouth"),
                        AnimationCurve.Constant(0f, 1f, 100f * (1 << plane) / 3f));
                    tree.AddChild(clip);
                    var children = tree.children;
                    children[plane].directBlendParameter = "Viseme" + (1 << plane);
                    tree.children = children;
                }
                // Visible only while two Booleans that both default to off are on.
                var machine = controller.layers[0].stateMachine;
                var idle = machine.AddState("Idle");
                var shown = machine.AddState("Shown");
                shown.motion = tree;
                var enter = idle.AddTransition(shown);
                enter.AddCondition(AnimatorConditionMode.If, 0f, "Enabled");
                enter.AddCondition(AnimatorConditionMode.If, 0f, "Active");
                enter.duration = 0f;

                var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                parameters.parameters = new[] { "Viseme1", "Viseme2", "Enabled", "Active" }
                    .Select(name => new VRCExpressionParameters.Parameter
                    {
                        name = name,
                        valueType = VRCExpressionParameters.ValueType.Bool,
                        networkSynced = true
                    }).ToArray();
                var group = ParameterPrecisionReduction.FindGroups(new[] { "Viseme1", "Viseme2" }).Single();

                ParameterCompressionSensitivity.Measure(root, controller, parameters, new[] { group });

                Assert.That(group.Weight, Is.EqualTo(2 * 0.01 * 0.01).Within(1e-7));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static ParameterBitPlaneGroup Group(string name, int bits, bool signed, double weight)
        {
            return new ParameterBitPlaneGroup(name,
                Enumerable.Range(0, bits).Select(plane => name + (1 << plane)).ToArray(),
                signed ? name + "Negative" : null)
            {
                Weight = weight
            };
        }
    }
}
