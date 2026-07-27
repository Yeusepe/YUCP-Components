using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace YUCP.Components.Editor.Tests
{
    public sealed class AdvancedVisemeAnimatorGraphOptimizerTests
    {
        private const string Prefix = "YUCP/Test/_Internal/";
        private const string AlwaysOne = "__YUCP_AVR_ONE";

        [Test]
        public void RemovesOnlyPrivateWritesOutsideTheObservableCone()
        {
            var objects = new List<UnityEngine.Object>();
            try
            {
                var controller = NewController(objects);
                AddFloat(controller, AlwaysOne, 1f);
                AddFloat(controller, "Input", 0f);
                AddFloat(controller, Prefix + "Live", 0f);
                AddFloat(controller, Prefix + "Dead", 0f);
                AddFloat(controller, "YUCP/Test/Public", 0f);

                var dead = Setter(objects, Prefix + "Dead", 1f, "Dead");
                var live = Setter(objects, Prefix + "Live", 1f, "Live");
                var publish = Setter(objects, "YUCP/Test/Public", 1f, "Publish");
                var root = Direct(objects, "Root",
                    Child(dead, AlwaysOne),
                    Child(live, "Input"),
                    Child(publish, Prefix + "Live"));
                AddState(controller, root);

                var report = AdvancedVisemeAnimatorGraphOptimizer.Optimize(
                    controller, Prefix.TrimEnd('/'), new[] { "YUCP/Test/Public" });

                Assert.That(AnimationUtility.GetCurveBindings(dead), Is.Empty);
                Assert.That(AnimationUtility.GetCurveBindings(live), Has.Length.EqualTo(1));
                Assert.That(AnimationUtility.GetCurveBindings(publish), Has.Length.EqualTo(1));
                Assert.That(controller.parameters.Select(parameter => parameter.name),
                    Does.Not.Contain(Prefix + "Dead"));
                Assert.That(controller.parameters.Select(parameter => parameter.name),
                    Does.Contain(Prefix + "Live"));
                Assert.That(report.removedAnimatorCurves, Is.EqualTo(1));
                Assert.That(report.removedInternalParameters, Is.EqualTo(1));
            }
            finally
            {
                Destroy(objects);
            }
        }

        [Test]
        public void PhysicalCurvesAndTransitionConditionsAreConservativeRoots()
        {
            var objects = new List<UnityEngine.Object>();
            try
            {
                var controller = NewController(objects);
                AddFloat(controller, AlwaysOne, 1f);
                AddFloat(controller, "Input", 0f);
                AddFloat(controller, Prefix + "Physical", 0f);
                AddFloat(controller, Prefix + "Condition", 0f);

                // Distinct constants keep the two writes non-congruent so this
                // test exercises root conservativeness, not interning.
                var physicalValue = Setter(
                    objects, Prefix + "Physical", 1f, "Physical value");
                var conditionValue = Setter(
                    objects, Prefix + "Condition", 0.5f, "Condition value");
                var mesh = new AnimationClip { name = "Physical output" };
                objects.Add(mesh);
                AnimationUtility.SetEditorCurve(mesh,
                    EditorCurveBinding.FloatCurve(
                        "Body", typeof(SkinnedMeshRenderer), "blendShape.Test"),
                    AnimationCurve.Constant(0f, 0f, 100f));
                var root = Direct(objects, "Root",
                    Child(physicalValue, "Input"),
                    Child(conditionValue, "Input"),
                    Child(mesh, Prefix + "Physical"));
                var state = AddState(controller, root);
                var target = state.stateMachine.AddState("Target");
                var transition = state.state.AddTransition(target);
                transition.AddCondition(
                    AnimatorConditionMode.Greater, 0.5f, Prefix + "Condition");

                var report = AdvancedVisemeAnimatorGraphOptimizer.Optimize(
                    controller, Prefix.TrimEnd('/'), Array.Empty<string>());

                Assert.That(AnimationUtility.GetCurveBindings(physicalValue),
                    Has.Length.EqualTo(1));
                Assert.That(AnimationUtility.GetCurveBindings(conditionValue),
                    Has.Length.EqualTo(1));
                Assert.That(AnimationUtility.GetCurveBindings(mesh),
                    Has.Length.EqualTo(1));
                Assert.That(report.removedAnimatorCurves, Is.Zero);
                Assert.That(report.deadInternalParameters, Is.Zero);
            }
            finally
            {
                Destroy(objects);
            }
        }

        [Test]
        public void NormalizedDirectPhysicalOutputRetainsEverySiblingWeight()
        {
            var objects = new List<UnityEngine.Object>();
            try
            {
                var controller = NewController(objects);
                var physicalWeight = Prefix + "PhysicalWeight";
                var siblingWeight = Prefix + "SiblingWeight";
                var deadOutput = Prefix + "DeadOutput";
                AddFloat(controller, physicalWeight, 1f);
                AddFloat(controller, siblingWeight, 1f);
                AddFloat(controller, deadOutput, 0f);

                var physical = new AnimationClip { name = "Physical output" };
                objects.Add(physical);
                AnimationUtility.SetEditorCurve(
                    physical,
                    EditorCurveBinding.FloatCurve(
                        "Body",
                        typeof(SkinnedMeshRenderer),
                        "blendShape.Test"),
                    AnimationCurve.Constant(0f, 0f, 100f));
                var normalized = Direct(
                    objects,
                    "Normalized root",
                    Child(physical, physicalWeight),
                    Child(
                        Setter(objects, deadOutput, 1f, "Dead sibling"),
                        siblingWeight));
                var serialized = new SerializedObject(normalized);
                serialized.FindProperty("m_NormalizedBlendValues").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AddState(controller, normalized);

                AdvancedVisemeAnimatorGraphOptimizer.Optimize(
                    controller,
                    Prefix.TrimEnd('/'),
                    Array.Empty<string>());

                Assert.That(
                    controller.parameters.Select(parameter => parameter.name),
                    Does.Contain(siblingWeight),
                    "Every normalized sibling weight changes the physical output.");
            }
            finally
            {
                Destroy(objects);
            }
        }

        [Test]
        public void RemovesNeutralZeroOnlyInsideProvenBlendCones()
        {
            var objects = new List<UnityEngine.Object>();
            try
            {
                var controller = NewController(objects);
                AddFloat(controller, AlwaysOne, 1f);
                AddFloat(controller, "Input", 0f);
                var safeParameter = Prefix +
                                    "BetaCoarticulation/Retention/Safe";
                var normalizedParameter = Prefix +
                                          "BetaCoarticulation/Retention/Normalized";
                var stateResetParameter = Prefix +
                                          "BetaCoarticulation/Retention/StateReset";
                AddFloat(controller, safeParameter, 0f);
                AddFloat(controller, normalizedParameter, 0f);
                AddFloat(controller, stateResetParameter, 0f);

                var safeOne = Setter(
                    objects, safeParameter, 1f, "Safe one");
                var safeZero = Setter(
                    objects, safeParameter, 0f, "Safe zero");
                AddState(controller, Direct(objects, "Safe root",
                    Child(safeOne, "Input"),
                    Child(safeZero, AlwaysOne)));

                var normalizedOne = Setter(
                    objects, normalizedParameter, 1f, "Normalized one");
                var normalizedZero = Setter(
                    objects, normalizedParameter, 0f, "Normalized zero");
                var normalized = Direct(objects, "Normalized root",
                    Child(normalizedOne, "Input"),
                    Child(normalizedZero, AlwaysOne));
                var serialized = new SerializedObject(normalized);
                serialized.FindProperty("m_NormalizedBlendValues").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AddState(controller, normalized);

                var stateReset = Setter(
                    objects, stateResetParameter, 0f, "State reset");
                AddState(controller, stateReset);

                var report = AdvancedVisemeAnimatorGraphOptimizer.Optimize(
                    controller, Prefix.TrimEnd('/'), new[]
                    {
                        safeParameter,
                        normalizedParameter,
                        stateResetParameter
                    });

                Assert.That(AnimationUtility.GetCurveBindings(safeZero), Is.Empty);
                Assert.That(AnimationUtility.GetCurveBindings(safeOne),
                    Has.Length.EqualTo(1));
                Assert.That(AnimationUtility.GetCurveBindings(normalizedZero),
                    Has.Length.EqualTo(1),
                    "Normalized Direct trees are outside the proven neutral cone.");
                Assert.That(AnimationUtility.GetCurveBindings(stateReset),
                    Has.Length.EqualTo(1),
                    "A state-level zero may be an intentional reset.");
                Assert.That(report.removedNeutralZeroCurves, Is.EqualTo(1));
                Assert.That(report.removedDeadAnimatorCurves, Is.Zero);
            }
            finally
            {
                Destroy(objects);
            }
        }

        [Test]
        public void RemovesOperationLocalZeroForPrivateFloatSiblings()
        {
            var objects = new List<UnityEngine.Object>();
            var previousDisable = AdvancedVisemeAnimatorGraphOptimizer
                .DisableOperationLocalNeutralZeroEliminationForTests;
            var previousSkip = AdvancedVisemeAnimatorGraphOptimizer
                .SkipCongruenceInterningForStructureTests;
            try
            {
                AdvancedVisemeAnimatorGraphOptimizer
                    .DisableOperationLocalNeutralZeroEliminationForTests = false;
                AdvancedVisemeAnimatorGraphOptimizer
                    .SkipCongruenceInterningForStructureTests = true;
                var controller = NewController(objects);
                AddFloat(controller, AlwaysOne, 1f);
                AddFloat(controller, "Input", 0f);
                var directParameter = Prefix + "Operation/Direct";
                var mapParameter = Prefix + "Operation/Map";
                AddFloat(controller, directParameter, 0f);
                AddFloat(controller, mapParameter, 0f);

                var directZero = Setter(
                    objects, directParameter, 0f, "Direct safety zero");
                var directOne = Setter(
                    objects, directParameter, 1f, "Direct value");
                AddState(controller, Direct(objects, "Direct operation",
                    Child(directZero, AlwaysOne), Child(directOne, "Input")));

                var mapZero = Setter(
                    objects, mapParameter, 0f, "Map safety zero");
                var mapOne = Setter(
                    objects, mapParameter, 1f, "Map value");
                AddState(controller, OneDimensional(
                    objects, "Map operation", "Input",
                    (mapZero, 0f), (mapOne, 1f)));

                var report = AdvancedVisemeAnimatorGraphOptimizer.Optimize(
                    controller, Prefix.TrimEnd('/'),
                    new[] { directParameter, mapParameter });

                Assert.That(AnimationUtility.GetCurveBindings(directZero), Is.Empty);
                Assert.That(AnimationUtility.GetCurveBindings(mapZero), Is.Empty);
                Assert.That(AnimationUtility.GetCurveBindings(directOne),
                    Has.Length.EqualTo(1));
                Assert.That(AnimationUtility.GetCurveBindings(mapOne),
                    Has.Length.EqualTo(1));
                Assert.That(report.removedNeutralZeroCurves,
                    Is.EqualTo(2));
            }
            finally
            {
                AdvancedVisemeAnimatorGraphOptimizer
                    .DisableOperationLocalNeutralZeroEliminationForTests =
                    previousDisable;
                AdvancedVisemeAnimatorGraphOptimizer
                    .SkipCongruenceInterningForStructureTests = previousSkip;
                Destroy(objects);
            }
        }

        [Test]
        public void OperationLocalZeroRequiresPrivatePositiveZeroContract()
        {
            var objects = new List<UnityEngine.Object>();
            var previousSkip = AdvancedVisemeAnimatorGraphOptimizer
                .SkipCongruenceInterningForStructureTests;
            try
            {
                AdvancedVisemeAnimatorGraphOptimizer
                    .SkipCongruenceInterningForStructureTests = true;
                var controller = NewController(objects);
                AddFloat(controller, AlwaysOne, 1f);
                AddFloat(controller, "Input", 0f);
                var nonzeroDefault = Prefix + "Operation/NonzeroDefault";
                var negativeZeroDefault = Prefix + "Operation/NegativeZeroDefault";
                var publicParameter = "YUCP/Test/PublicOperation";
                AddFloat(controller, nonzeroDefault, 0.25f);
                AddFloat(controller, negativeZeroDefault,
                    BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)));
                AddFloat(controller, publicParameter, 0f);

                AnimationClip AddGuardedOperation(string parameter, string label)
                {
                    var zero = Setter(objects, parameter, 0f, label + " zero");
                    var one = Setter(objects, parameter, 1f, label + " one");
                    AddState(controller, Direct(objects, label,
                        Child(zero, AlwaysOne), Child(one, "Input")));
                    return zero;
                }

                var nonzeroDefaultZero = AddGuardedOperation(
                    nonzeroDefault, "Nonzero default");
                var negativeZero = AddGuardedOperation(
                    negativeZeroDefault, "Negative zero default");
                var publicZero = AddGuardedOperation(
                    publicParameter, "Public parameter");

                AdvancedVisemeAnimatorGraphOptimizer.Optimize(
                    controller, Prefix.TrimEnd('/'),
                    new[] { nonzeroDefault, negativeZeroDefault, publicParameter });

                Assert.That(AnimationUtility.GetCurveBindings(nonzeroDefaultZero),
                    Has.Length.EqualTo(1));
                Assert.That(AnimationUtility.GetCurveBindings(negativeZero),
                    Has.Length.EqualTo(1));
                Assert.That(AnimationUtility.GetCurveBindings(publicZero),
                    Has.Length.EqualTo(1));
            }
            finally
            {
                AdvancedVisemeAnimatorGraphOptimizer
                    .SkipCongruenceInterningForStructureTests = previousSkip;
                Destroy(objects);
            }
        }

        [Test]
        public void OperationLocalZeroRejectsCurvedAndDifferentDepthWriters()
        {
            var objects = new List<UnityEngine.Object>();
            var previousSkip = AdvancedVisemeAnimatorGraphOptimizer
                .SkipCongruenceInterningForStructureTests;
            try
            {
                AdvancedVisemeAnimatorGraphOptimizer
                    .SkipCongruenceInterningForStructureTests = true;
                var controller = NewController(objects);
                AddFloat(controller, AlwaysOne, 1f);
                AddFloat(controller, "Input", 0f);
                var curvedParameter = Prefix + "Operation/CurvedZero";
                var nestedParameter = Prefix + "Operation/NestedWriter";
                AddFloat(controller, curvedParameter, 0f);
                AddFloat(controller, nestedParameter, 0f);

                var curved = new AnimationClip { name = "Curved zero" };
                objects.Add(curved);
                var first = new Keyframe(0f, 0f, 0f, 4f);
                var second = new Keyframe(1f, 0f, -4f, 0f);
                AnimationUtility.SetEditorCurve(curved,
                    EditorCurveBinding.FloatCurve(
                        "", typeof(Animator), curvedParameter),
                    new AnimationCurve(first, second));
                AddState(controller, Direct(objects, "Curved operation",
                    Child(curved, AlwaysOne),
                    Child(Setter(objects, curvedParameter, 1f, "Curved one"),
                        "Input")));

                var nestedZero = Setter(
                    objects, nestedParameter, 0f, "Outer zero");
                var nestedWriter = Direct(objects, "Nested writer",
                    Child(Setter(objects, nestedParameter, 1f, "Nested one"),
                        "Input"));
                AddState(controller, Direct(objects, "Different depth",
                    Child(nestedZero, AlwaysOne),
                    Child(nestedWriter, AlwaysOne)));

                AdvancedVisemeAnimatorGraphOptimizer.Optimize(
                    controller, Prefix.TrimEnd('/'),
                    new[] { curvedParameter, nestedParameter });

                Assert.That(AnimationUtility.GetCurveBindings(curved),
                    Has.Length.EqualTo(1));
                Assert.That(AnimationUtility.GetCurveBindings(nestedZero),
                    Has.Length.EqualTo(1));
            }
            finally
            {
                AdvancedVisemeAnimatorGraphOptimizer
                    .SkipCongruenceInterningForStructureTests = previousSkip;
                Destroy(objects);
            }
        }

        [Test]
        public void OperationLocalZeroRequiresEverySharedClipUseToBeSafe()
        {
            var objects = new List<UnityEngine.Object>();
            var previousSkip = AdvancedVisemeAnimatorGraphOptimizer
                .SkipCongruenceInterningForStructureTests;
            try
            {
                AdvancedVisemeAnimatorGraphOptimizer
                    .SkipCongruenceInterningForStructureTests = true;
                var controller = NewController(objects);
                AddFloat(controller, AlwaysOne, 1f);
                AddFloat(controller, "Input", 0f);
                var parameter = Prefix + "Operation/Shared";
                AddFloat(controller, parameter, 0f);
                var sharedZero = Setter(
                    objects, parameter, 0f, "Shared safety zero");
                AddState(controller, Direct(objects, "Safe occurrence",
                    Child(sharedZero, AlwaysOne),
                    Child(Setter(objects, parameter, 1f, "Shared one"), "Input")));
                AddState(controller, sharedZero);

                AdvancedVisemeAnimatorGraphOptimizer.Optimize(
                    controller, Prefix.TrimEnd('/'), new[] { parameter });

                Assert.That(AnimationUtility.GetCurveBindings(sharedZero),
                    Has.Length.EqualTo(1));
            }
            finally
            {
                AdvancedVisemeAnimatorGraphOptimizer
                    .SkipCongruenceInterningForStructureTests = previousSkip;
                Destroy(objects);
            }
        }

        [Test]
        public void InternsCongruentParametersAndRewritesTheirReaders()
        {
            var objects = new List<UnityEngine.Object>();
            var previousDisable = AdvancedVisemeAnimatorGraphOptimizer
                .DisableOperationLocalNeutralZeroEliminationForTests;
            try
            {
                AdvancedVisemeAnimatorGraphOptimizer
                    .DisableOperationLocalNeutralZeroEliminationForTests = true;
                var controller = NewController(objects);
                AddFloat(controller, AlwaysOne, 1f);
                AddFloat(controller, "Input", 0f);
                AddFloat(controller, Prefix + "First", 0f);
                AddFloat(controller, Prefix + "Second", 0f);
                AddFloat(controller, "YUCP/Test/PublicA", 0f);
                AddFloat(controller, "YUCP/Test/PublicB", 0f);

                var firstLow = Setter(objects, Prefix + "First", 0f, "First low");
                var firstHigh = Setter(objects, Prefix + "First", 1f, "First high");
                var secondLow = Setter(objects, Prefix + "Second", 0f, "Second low");
                var secondHigh = Setter(objects, Prefix + "Second", 1f, "Second high");
                var publishA = Setter(objects, "YUCP/Test/PublicA", 1f, "Publish A");
                var publishB = Setter(objects, "YUCP/Test/PublicB", 1f, "Publish B");
                var root = Direct(objects, "Root",
                    Child(OneDimensional(objects, "First map", "Input",
                        (firstLow, 0f), (firstHigh, 1f)), AlwaysOne),
                    Child(OneDimensional(objects, "Second map", "Input",
                        (secondLow, 0f), (secondHigh, 1f)), AlwaysOne),
                    Child(publishA, Prefix + "First"),
                    Child(publishB, Prefix + "Second"));
                AddState(controller, root);

                var report = AdvancedVisemeAnimatorGraphOptimizer.Optimize(
                    controller, Prefix.TrimEnd('/'),
                    new[] { "YUCP/Test/PublicA", "YUCP/Test/PublicB" });

                Assert.That(report.internedCongruentParameters, Is.EqualTo(1));
                Assert.That(report.removedCongruentCurves, Is.EqualTo(2));
                Assert.That(AnimationUtility.GetCurveBindings(secondLow), Is.Empty);
                Assert.That(AnimationUtility.GetCurveBindings(secondHigh), Is.Empty);
                Assert.That(AnimationUtility.GetCurveBindings(firstLow),
                    Has.Length.EqualTo(1));
                var rewritten = root.children
                    .Single(child => child.motion == publishB)
                    .directBlendParameter;
                Assert.That(rewritten, Is.EqualTo(Prefix + "First"));
                Assert.That(controller.parameters.Select(parameter => parameter.name),
                    Does.Not.Contain(Prefix + "Second"));
            }
            finally
            {
                AdvancedVisemeAnimatorGraphOptimizer
                    .DisableOperationLocalNeutralZeroEliminationForTests =
                    previousDisable;
                Destroy(objects);
            }
        }

        [Test]
        public void UnknownBehaviourRetainsEveryPrivateParameter()
        {
            var objects = new List<UnityEngine.Object>();
            var previousDisable = AdvancedVisemeAnimatorGraphOptimizer
                .DisableOperationLocalNeutralZeroEliminationForTests;
            try
            {
                AdvancedVisemeAnimatorGraphOptimizer
                    .DisableOperationLocalNeutralZeroEliminationForTests = true;
                var controller = NewController(objects);
                AddFloat(controller, AlwaysOne, 1f);
                AddFloat(controller, "Input", 0f);
                AddFloat(controller, Prefix + "First", 0f);
                AddFloat(controller, Prefix + "Second", 0f);
                AddFloat(controller, Prefix + "Third", 0f);

                var first = Setter(objects, Prefix + "First", 1f, "First");
                var second = Setter(objects, Prefix + "Second", 1f, "Second");
                var third = Setter(objects, Prefix + "Third", 1f, "Third");
                var state = AddState(controller, Direct(
                    objects,
                    "Root",
                    Child(first, "Input"),
                    Child(second, "Input"),
                    Child(third, "Input")));
                var behaviour = state.state
                    .AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
                Assert.That(behaviour, Is.Not.Null);
                behaviour.debugString = Prefix + "First";
                Assert.That(state.state.behaviours, Does.Contain(behaviour));

                var report = AdvancedVisemeAnimatorGraphOptimizer.Optimize(
                    controller,
                    Prefix.TrimEnd('/'),
                    Array.Empty<string>());

                Assert.That(report.internedCongruentParameters, Is.Zero);
                Assert.That(report.removedInternalParameters, Is.Zero);
                Assert.That(
                    controller.parameters.Select(parameter => parameter.name),
                    Does.Contain(Prefix + "First"));
                Assert.That(
                    controller.parameters.Select(parameter => parameter.name),
                    Does.Contain(Prefix + "Second"));
                Assert.That(
                    controller.parameters.Select(parameter => parameter.name),
                    Does.Contain(Prefix + "Third"));
                Assert.That(AnimationUtility.GetCurveBindings(first),
                    Has.Length.EqualTo(1));
                Assert.That(AnimationUtility.GetCurveBindings(second),
                    Has.Length.EqualTo(1));
                Assert.That(AnimationUtility.GetCurveBindings(third),
                    Has.Length.EqualTo(1));
            }
            finally
            {
                AdvancedVisemeAnimatorGraphOptimizer
                    .DisableOperationLocalNeutralZeroEliminationForTests =
                    previousDisable;
                Destroy(objects);
            }
        }

        [Test]
        public void CongruenceInterningDoesNotMutateExternalAuthoredClips()
        {
            var objects = new List<UnityEngine.Object>();
            string suffix = Guid.NewGuid().ToString("N");
            string externalPath =
                "Assets/YUCPOptimizerOwnership-" + suffix + ".anim";
            try
            {
                var controller = NewController(objects);
                AddFloat(controller, AlwaysOne, 1f);
                AddFloat(controller, "Input", 0f);
                AddFloat(controller, Prefix + "First", 0f);
                AddFloat(controller, Prefix + "Second", 0f);
                AddFloat(controller, "YUCP/Test/PublicA", 0f);
                AddFloat(controller, "YUCP/Test/PublicB", 0f);

                var authored = new AnimationClip { name = "Authored writes" };
                objects.Add(authored);
                AnimationUtility.SetEditorCurve(
                    authored,
                    EditorCurveBinding.FloatCurve(
                        "",
                        typeof(Animator),
                        Prefix + "First"),
                    AnimationCurve.Constant(0f, 0f, 1f));
                AnimationUtility.SetEditorCurve(
                    authored,
                    EditorCurveBinding.FloatCurve(
                        "",
                        typeof(Animator),
                        Prefix + "Second"),
                    AnimationCurve.Constant(0f, 0f, 1f));
                AssetDatabase.CreateAsset(authored, externalPath);
                AssetDatabase.SaveAssets();
                AddState(controller, authored);
                var readers = Direct(
                    objects,
                    "Readers",
                    Child(
                        Setter(
                            objects,
                            "YUCP/Test/PublicA",
                            1f,
                            "Publish A"),
                        Prefix + "First"),
                    Child(
                        Setter(
                            objects,
                            "YUCP/Test/PublicB",
                            1f,
                            "Publish B"),
                        Prefix + "Second"));
                AddState(controller, readers);

                var report = AdvancedVisemeAnimatorGraphOptimizer.Optimize(
                    controller,
                    Prefix.TrimEnd('/'),
                    new[] { "YUCP/Test/PublicA", "YUCP/Test/PublicB" });

                Assert.That(report.internedCongruentParameters, Is.Zero);
                Assert.That(
                    AnimationUtility.GetCurveBindings(authored)
                        .Select(binding => binding.propertyName),
                    Is.EquivalentTo(
                        new[] { Prefix + "First", Prefix + "Second" }));
                Assert.That(
                    controller.parameters.Select(parameter => parameter.name),
                    Does.Contain(Prefix + "Second"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(externalPath);
                Destroy(objects);
            }
        }

        [Test]
        public void CongruenceIsTransitiveThroughCongruentInputs()
        {
            var objects = new List<UnityEngine.Object>();
            try
            {
                var controller = NewController(objects);
                AddFloat(controller, AlwaysOne, 1f);
                AddFloat(controller, "Input", 0f);
                AddFloat(controller, Prefix + "StageA/1", 0f);
                AddFloat(controller, Prefix + "StageA/2", 0f);
                AddFloat(controller, Prefix + "StageB/1", 0f);
                AddFloat(controller, Prefix + "StageB/2", 0f);
                AddFloat(controller, "YUCP/Test/Public1", 0f);
                AddFloat(controller, "YUCP/Test/Public2", 0f);

                var root = Direct(objects, "Root",
                    Child(Setter(objects, Prefix + "StageA/1", 0.5f, "A1"), "Input"),
                    Child(Setter(objects, Prefix + "StageA/2", 0.5f, "A2"), "Input"),
                    Child(Setter(objects, Prefix + "StageB/1", 2f, "B1"),
                        Prefix + "StageA/1"),
                    Child(Setter(objects, Prefix + "StageB/2", 2f, "B2"),
                        Prefix + "StageA/2"),
                    Child(Setter(objects, "YUCP/Test/Public1", 1f, "P1"),
                        Prefix + "StageB/1"),
                    Child(Setter(objects, "YUCP/Test/Public2", 1f, "P2"),
                        Prefix + "StageB/2"));
                AddState(controller, root);

                var report = AdvancedVisemeAnimatorGraphOptimizer.Optimize(
                    controller, Prefix.TrimEnd('/'),
                    new[] { "YUCP/Test/Public1", "YUCP/Test/Public2" });

                // StageB duplicates are congruent only because their StageA
                // weights are congruent; both pairs collapse in one pass.
                Assert.That(report.internedCongruentParameters, Is.EqualTo(2));
                var names = controller.parameters
                    .Select(parameter => parameter.name).ToArray();
                Assert.That(names, Does.Not.Contain(Prefix + "StageA/2"));
                Assert.That(names, Does.Not.Contain(Prefix + "StageB/2"));
                Assert.That(names, Does.Contain(Prefix + "StageA/1"));
                Assert.That(names, Does.Contain(Prefix + "StageB/1"));
            }
            finally
            {
                Destroy(objects);
            }
        }

        [Test]
        public void CongruenceRespectsDefaultsExtraWritersAndDistinctConstants()
        {
            var objects = new List<UnityEngine.Object>();
            try
            {
                var controller = NewController(objects);
                AddFloat(controller, AlwaysOne, 1f);
                AddFloat(controller, "Input", 0f);
                AddFloat(controller, Prefix + "Feedback/1", 0f);
                AddFloat(controller, Prefix + "Feedback/2", 1f);
                AddFloat(controller, Prefix + "Accumulated", 0f);
                AddFloat(controller, Prefix + "Single", 0f);
                AddFloat(controller, Prefix + "OtherConstant", 0f);
                AddFloat(controller, "YUCP/Test/Public", 0f);

                var root = Direct(objects, "Root",
                    // Same write structure but different defaults: a smoothed
                    // trajectory depends on its initial state.
                    Child(Setter(objects, Prefix + "Feedback/1", 1f, "F1"),
                        Prefix + "Feedback/1"),
                    Child(Setter(objects, Prefix + "Feedback/2", 1f, "F2"),
                        Prefix + "Feedback/2"),
                    // An additive second writer distinguishes site multisets.
                    Child(Setter(objects, Prefix + "Accumulated", 1f, "Acc a"),
                        "Input"),
                    Child(Setter(objects, Prefix + "Accumulated", 0.25f, "Acc b"),
                        AlwaysOne),
                    Child(Setter(objects, Prefix + "Single", 1f, "Single"),
                        "Input"),
                    // A different constant is a different function.
                    Child(Setter(objects, Prefix + "OtherConstant", 0.75f, "Other"),
                        "Input"),
                    Child(Setter(objects, "YUCP/Test/Public", 1f, "P"),
                        Prefix + "Accumulated"));
                AddState(controller, root);
                var publicReaders = Direct(objects, "Readers",
                    Child(Setter(objects, "YUCP/Test/Public", 1f, "P2"),
                        Prefix + "Feedback/1"),
                    Child(Setter(objects, "YUCP/Test/Public", 1f, "P3"),
                        Prefix + "Feedback/2"),
                    Child(Setter(objects, "YUCP/Test/Public", 1f, "P4"),
                        Prefix + "Single"),
                    Child(Setter(objects, "YUCP/Test/Public", 1f, "P5"),
                        Prefix + "OtherConstant"));
                AddState(controller, publicReaders);

                var report = AdvancedVisemeAnimatorGraphOptimizer.Optimize(
                    controller, Prefix.TrimEnd('/'),
                    new[] { "YUCP/Test/Public" });

                Assert.That(report.internedCongruentParameters, Is.Zero);
                Assert.That(report.removedCongruentCurves, Is.Zero);
            }
            finally
            {
                Destroy(objects);
            }
        }

        private static BlendTree OneDimensional(
            ICollection<UnityEngine.Object> objects,
            string name,
            string parameter,
            params (Motion motion, float threshold)[] children)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = parameter,
                useAutomaticThresholds = false,
                children = children.Select(child => new ChildMotion
                {
                    motion = child.motion,
                    threshold = child.threshold,
                    timeScale = 1f
                }).ToArray()
            };
            objects.Add(tree);
            return tree;
        }

        [Test]
        public void ReductionGateAcceptsOnlyCheaperCertifiedTeacherEquivalent()
        {
            var candidate = new AdvancedVisemeRetentionReductionGate.Candidate
            {
                name = "Exact lowered teacher",
                retention = AdvancedVisemeRetentionReductionGate.ExactTeacherTensor(),
                estimatedActiveBindings = 200,
                preservesAnimatorEpochs = true,
                preservesSteadyEndpoints = true,
                preservesMandatoryConstraints = true,
                replay = new AdvancedVisemeRetentionReductionGate.ReplayMetrics(
                    223802, 1e-7f, 2e-7f, 5e-7f, 1e-7f)
            };

            var certificate = AdvancedVisemeRetentionReductionGate.Evaluate(
                candidate, 360);

            Assert.That(certificate.accepted, Is.True,
                string.Join("\n", certificate.rejectionReasons));
            Assert.That(certificate.coefficientMaximum, Is.Zero);
        }

        [Test]
        public void ReductionGateRejectsOptimisticReplayWhenUniversalBoundFails()
        {
            var tensor = AdvancedVisemeRetentionReductionGate.ExactTeacherTensor();
            tensor[17] += AdvancedVisemeRetentionReductionGate
                              .MaximumCoefficientError + 0.001f;
            var candidate = new AdvancedVisemeRetentionReductionGate.Candidate
            {
                name = "Hidden coefficient outlier",
                retention = tensor,
                estimatedActiveBindings = 200,
                preservesAnimatorEpochs = true,
                preservesSteadyEndpoints = true,
                preservesMandatoryConstraints = true,
                replay = new AdvancedVisemeRetentionReductionGate.ReplayMetrics(
                    223802, 0.001f, 0.002f, 0.003f, 0.001f)
            };

            var certificate = AdvancedVisemeRetentionReductionGate.Evaluate(
                candidate, 360);

            Assert.That(certificate.accepted, Is.False);
            Assert.That(certificate.rejectionReasons,
                Has.Some.Contains("Universal simplex bound"));
        }

        [Test]
        public void ReductionGateRejectsMeasuredSharedCpCandidate()
        {
            var candidate = new AdvancedVisemeRetentionReductionGate.Candidate
            {
                name = "Shared CP H12 with structured patches",
                retention = AdvancedVisemeRetentionReductionGate.ExactTeacherTensor(),
                estimatedActiveBindings = 240,
                preservesAnimatorEpochs = true,
                preservesSteadyEndpoints = true,
                preservesMandatoryConstraints = true,
                replay = new AdvancedVisemeRetentionReductionGate.ReplayMetrics(
                    223802, 0.06261f, 0.18103f, 0.34694f, 0.03f)
            };

            var certificate = AdvancedVisemeRetentionReductionGate.Evaluate(
                candidate, 360);

            Assert.That(certificate.accepted, Is.False);
            Assert.That(certificate.rejectionReasons,
                Has.Some.Contains("Replay RMS"));
            Assert.That(certificate.rejectionReasons,
                Has.Some.Contains("Replay p99"));
            Assert.That(certificate.rejectionReasons,
                Has.Some.Contains("Replay maximum"));
        }

        private static AnimatorController NewController(
            ICollection<UnityEngine.Object> objects)
        {
            var controller = new AnimatorController { name = "Optimizer Test" };
            objects.Add(controller);
            return controller;
        }

        private static (AnimatorStateMachine stateMachine, AnimatorState state)
            AddState(AnimatorController controller, Motion motion)
        {
            controller.AddLayer("Test");
            var layer = controller.layers[controller.layers.Length - 1];
            var state = layer.stateMachine.AddState("Run");
            state.motion = motion;
            layer.stateMachine.defaultState = state;
            return (layer.stateMachine, state);
        }

        private static BlendTree Direct(
            ICollection<UnityEngine.Object> objects,
            string name,
            params ChildMotion[] children)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Direct,
                useAutomaticThresholds = false,
                children = children
            };
            objects.Add(tree);
            return tree;
        }

        private static ChildMotion Child(Motion motion, string weight)
        {
            return new ChildMotion
            {
                motion = motion,
                directBlendParameter = weight,
                timeScale = 1f
            };
        }

        private static AnimationClip Setter(
            ICollection<UnityEngine.Object> objects,
            string parameter,
            float value,
            string name)
        {
            var clip = new AnimationClip { name = name };
            objects.Add(clip);
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("", typeof(Animator), parameter),
                AnimationCurve.Constant(0f, 0f, value));
            return clip;
        }

        private static void AddFloat(
            AnimatorController controller,
            string parameter,
            float defaultValue)
        {
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = parameter,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = defaultValue
            });
        }

        private static void Destroy(IEnumerable<UnityEngine.Object> objects)
        {
            foreach (var value in objects.Reverse())
            {
                if (value != null) UnityEngine.Object.DestroyImmediate(value);
            }
        }
    }
}
