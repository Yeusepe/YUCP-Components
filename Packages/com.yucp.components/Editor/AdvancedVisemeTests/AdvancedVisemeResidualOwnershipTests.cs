#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor.Tests
{
    public sealed class AdvancedVisemeResidualOwnershipTests
    {
        [Test]
        public void LowRankOwnershipIsLinearFiniteAndIndependentPerAxis()
        {
            var weights = new[] { 0.1f, 0.6f, 0.3f };
            var jaw = new[] { 0.5f, -0.25f, 0.75f };
            var lips = new[] { -0.4f, 0.2f, 0.1f };

            Assert.That(AdvancedVisemeMath.LowRankOwnershipCorrection(
                weights, jaw, 0f, 1f, 1f), Is.Zero);
            Assert.That(AdvancedVisemeMath.LowRankOwnershipCorrection(
                    weights, jaw, 1f, 1f, 1f),
                Is.EqualTo(-0.125f).Within(1e-6f));
            Assert.That(AdvancedVisemeMath.LowRankOwnershipCorrection(
                    weights, lips, 0.2f, 1f, 1f),
                Is.EqualTo(-0.022f).Within(1e-6f),
                "Lip authority must not inherit the jaw gain.");
            Assert.That(AdvancedVisemeMath.LowRankOwnershipCorrection(
                    weights, new[] { float.NaN, 1f, 0f }, 1f, 1f, 1f),
                Is.EqualTo(-0.6f).Within(1e-6f),
                "Invalid calibration values must fail conservatively and remain finite.");
        }

        [Test]
        public void GeneratedGraphUsesIndependentLowRankGainsForPrimaryAndLinkedCarriers()
        {
            var root = new GameObject("Residual Ownership Graph Test");
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            var mesh = new Mesh { name = "Residual ownership calibration" };
            mesh.vertices = new[] { Vector3.zero };
            var folderName = "__YUCP_AVR_ResidualOwnership_" + Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            try
            {
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.mouthOwnership = AdvancedVisemeMouthOwnership.DriveLowerFace;
                component.trackingInputs = AdvancedVisemeTrackingInputs.Balanced8;
                component.createTuningMenu = false;

                var primary = SyntheticCalibration(
                    mesh,
                    new[]
                    {
                        AdvancedVisemeArticulator.JawOpen,
                        AdvancedVisemeArticulator.LipClose
                    },
                    new[] { true, true });
                primary.residualBlendShapeNames[1] = "PrimaryResidualPP";
                primary.ownershipNegativeCarrierBlendShapeNames[0] = "PrimaryJawCarrier";
                primary.ownershipCarrierBlendShapeNames[1] = "PrimaryLipCarrier";
                primary.ownershipNegativeCarrierScales[0] = 2f;
                primary.ownershipCarrierScales[1] = 0.5f;
                primary.ownershipProjectionCoefficients[1, 0] = 2f;
                primary.ownershipProjectionCoefficients[1, 1] = -0.5f;

                var linked = SyntheticCalibration(
                    mesh,
                    new[]
                    {
                        AdvancedVisemeArticulator.JawOpen,
                        AdvancedVisemeArticulator.LipClose
                    },
                    new[] { true, true });
                linked.residualBlendShapeNames[1] = "LinkedResidualPP";
                linked.ownershipNegativeCarrierBlendShapeNames[0] = "LinkedJawCarrier";
                linked.ownershipNegativeCarrierBlendShapeNames[1] = "LinkedLipCarrier";
                linked.ownershipNegativeCarrierScales[0] = 0.75f;
                linked.ownershipNegativeCarrierScales[1] = 1.25f;
                linked.ownershipProjectionCoefficients[1, 0] = 0.75f;
                linked.ownershipProjectionCoefficients[1, 1] = 1.25f;

                var result = AdvancedVisemeAnimatorBuilder.Build(
                    new AdvancedVisemeAnimatorBuilder.Request
                    {
                        controllerPath = folder + "/AdvancedViseme.controller",
                        parametersPath = folder + "/Parameters.asset",
                        rendererPath = "Face",
                        component = component,
                        profile = profile,
                        trackingPrefix = "YUCP/TestTracking",
                        effectiveTrackingInputs = AdvancedVisemeTrackingInputs.Balanced8,
                        reuseExistingTracking = false,
                        trackingActiveParameter = "YUCP/TestTracking/LipTrackingActive",
                        trackingActiveAnimatorType = AnimatorControllerParameterType.Float,
                        trackingActiveDefault = 1f,
                        trackingParameterNames =
                            new Dictionary<AdvancedVisemeArticulator, string>(),
                        auxiliaryTrackingParameterNames = new Dictionary<string, string>(),
                        sourceVisemeBlendShapes = VisemeReconstructionProfile.VisemeNames
                            .Select(name => "vrc.v_" + name).ToArray(),
                        calibration = primary,
                        calibrationBasis = new[]
                        {
                            new AdvancedVisemeMeshCalibrator.BasisInput(
                                AdvancedVisemeArticulator.JawOpen, 0),
                            new AdvancedVisemeMeshCalibrator.BasisInput(
                                AdvancedVisemeArticulator.LipClose, 1)
                        },
                        resolvedBlendShapes = new Dictionary<AdvancedVisemeArticulator, string>
                        {
                            [AdvancedVisemeArticulator.JawOpen] = "JawBasis",
                            [AdvancedVisemeArticulator.LipClose] = "LipBasis"
                        },
                        externalPoses =
                            new Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose>(),
                        targetMesh = mesh,
                        trackingEnabled = true,
                        existingExpressionParameters = new HashSet<string>(),
                        linkedRendererOutputs = new[]
                        {
                            new AdvancedVisemeAnimatorBuilder.LinkedRendererOutput
                            {
                                rendererPath = "LinkedFace",
                                label = "Linked Face",
                                calibration = linked
                            }
                        }
                    });

                var parameters = result.controller.parameters.Select(item => item.name).ToArray();
                var internalPrefix = component.NormalizedPrefix + "/_Internal/";
                string ResolveGeneratedParameter(string relative)
                {
                    var parameter = internalPrefix + relative;
                    var mappings = result.optimizerReport?.internedParameterMappings;
                    var visited = new HashSet<string>(StringComparer.Ordinal);
                    while (mappings != null &&
                           mappings.TryGetValue(parameter, out var representative))
                    {
                        Assert.That(visited.Add(parameter), Is.True,
                            "Optimizer congruence mappings contain a cycle at " +
                            parameter + ".");
                        parameter = representative;
                    }
                    Assert.That(parameters, Does.Contain(parameter),
                        $"Generated ownership parameter '{relative}' resolved to " +
                        $"missing representative '{parameter}'.");
                    return parameter;
                }
                var jawGain = result.trackingGainParameters[AdvancedVisemeArticulator.JawOpen];
                var lipGain = result.trackingGainParameters[AdvancedVisemeArticulator.LipClose];
                var jawYield = ResolveGeneratedParameter(
                    "Residual/Ownership/JawOpen/Yield");
                var lipYield = ResolveGeneratedParameter(
                    "Residual/Ownership/LipClose/Yield");
                var primaryJawProjected = ResolveGeneratedParameter(
                    "Primary/Ownership/0/Subtract/Projected");
                var primaryLipProjected = ResolveGeneratedParameter(
                    "Primary/Ownership/1/Add/Projected");
                var linkedJawProjected = ResolveGeneratedParameter(
                    "LinkedRenderer/0/Ownership/0/Subtract/Projected");
                var linkedLipProjected = ResolveGeneratedParameter(
                    "LinkedRenderer/0/Ownership/1/Subtract/Projected");
                Assert.That(parameters.Any(name =>
                        name.Contains("/Ownership/", StringComparison.Ordinal) &&
                        name.EndsWith("/Correction", StringComparison.Ordinal)),
                    Is.False,
                    "Ownership products must not publish a frame-delayed correction parameter.");

                var trees = AssetDatabase.LoadAllAssetsAtPath(folder + "/AdvancedViseme.controller")
                    .OfType<BlendTree>().ToArray();
                var dependencies = BuildParameterDependencies(result.controller);
                Assert.That(DependsOn(dependencies, jawYield, jawGain), Is.True);
                Assert.That(DependsOn(dependencies, jawYield, lipGain), Is.False,
                    "Jaw ownership must not wait for an unrelated lip channel.");
                Assert.That(DependsOn(dependencies, lipYield, lipGain), Is.True);
                Assert.That(DependsOn(dependencies, lipYield, jawGain), Is.False,
                    "Lip ownership must not wait for an unrelated jaw channel.");

                Assert.That(dependencies.ContainsKey(primaryJawProjected), Is.True);
                Assert.That(dependencies.ContainsKey(primaryLipProjected), Is.True);
                Assert.That(dependencies.ContainsKey(linkedJawProjected), Is.True);
                Assert.That(dependencies.ContainsKey(linkedLipProjected), Is.True);

                AssertOwnershipProduct(
                    trees, folder + "/AdvancedViseme.controller", "PrimaryJawCarrier",
                    primaryJawProjected, jawYield);
                AssertOwnershipProduct(
                    trees, folder + "/AdvancedViseme.controller", "PrimaryLipCarrier",
                    primaryLipProjected, lipYield);
                AssertOwnershipProduct(
                    trees, folder + "/AdvancedViseme.controller", "LinkedJawCarrier",
                    linkedJawProjected, jawYield,
                    "Linked renderers must reuse renderer-independent ownership gains.");
                AssertOwnershipProduct(
                    trees, folder + "/AdvancedViseme.controller", "LinkedLipCarrier",
                    linkedLipProjected, lipYield);

                Assert.That(parameters.Any(name => name.Contains(
                    "/Residual/Retention", StringComparison.Ordinal)), Is.False);
                Assert.That(parameters.Any(name => name.Contains(
                    "ConflictRemoval", StringComparison.Ordinal)), Is.False);

                Assert.That(parameters.Any(name => name.Contains(
                    "ResidualMismatch", StringComparison.Ordinal)), Is.False);
                Assert.That(parameters.Any(name => name.Contains(
                    "/Residual/Mismatch", StringComparison.Ordinal)), Is.False);
                Assert.That(parameters.Any(name => name.Contains(
                    "/Residual/MismatchFiltered", StringComparison.Ordinal)), Is.False,
                    "Residual ownership must not add a second mismatch pole after tracking gain smoothing.");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RankDeficientCarrierUsesEveryDependentArticulatorGain()
        {
            var root = new GameObject("Rank Deficient Ownership Graph Test");
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            var mesh = new Mesh { name = "Rank deficient ownership calibration" };
            mesh.vertices = new[] { Vector3.zero };
            var folderName = "__YUCP_AVR_RankOwnership_" + Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            try
            {
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.mouthOwnership = AdvancedVisemeMouthOwnership.DriveLowerFace;
                component.trackingInputs = AdvancedVisemeTrackingInputs.Balanced8;
                component.createTuningMenu = false;

                var calibration = SyntheticCalibration(
                    mesh,
                    new[]
                    {
                        AdvancedVisemeArticulator.JawOpen,
                        AdvancedVisemeArticulator.LipClose,
                        AdvancedVisemeArticulator.TongueOut
                    },
                    new[] { true, true, true });
                calibration.ownershipBasisRankDeficient = true;
                calibration.residualBlendShapeNames[1] = "RankResidualPP";
                calibration.residualBlendShapeNames[10] = "IndependentTongueResidual";
                calibration.ownershipNegativeCarrierBlendShapeNames[0] =
                    "SharedJawLipCarrier";
                calibration.ownershipNegativeCarrierScales[0] = 1f;
                calibration.ownershipProjectionCoefficients[1, 0] = 1f;
                calibration.ownershipNegativeCarrierBlendShapeNames[2] =
                    "IndependentTongueCarrier";
                calibration.ownershipNegativeCarrierScales[2] = 1f;
                calibration.ownershipProjectionCoefficients[10, 2] = 1f;
                // Column one is a nonzero measured ray eliminated as dependent.
                calibration.ownershipNonZeroSelectedColumns[0] = true;
                calibration.ownershipNonZeroSelectedColumns[1] = true;
                calibration.ownershipNonZeroSelectedColumns[2] = true;
                calibration.ownershipAuthorityGroups = new bool[3, 3];
                calibration.ownershipAuthorityGroups[0, 0] = true;
                calibration.ownershipAuthorityGroups[0, 1] = true;
                calibration.ownershipAuthorityGroups[1, 0] = true;
                calibration.ownershipAuthorityGroups[1, 1] = true;
                calibration.ownershipAuthorityGroups[2, 2] = true;

                var result = AdvancedVisemeAnimatorBuilder.Build(
                    new AdvancedVisemeAnimatorBuilder.Request
                    {
                        controllerPath = folder + "/AdvancedViseme.controller",
                        parametersPath = folder + "/Parameters.asset",
                        rendererPath = "Face",
                        component = component,
                        profile = profile,
                        trackingPrefix = "YUCP/TestTracking",
                        effectiveTrackingInputs = AdvancedVisemeTrackingInputs.FullTongue18,
                        reuseExistingTracking = false,
                        trackingActiveParameter = "YUCP/TestTracking/LipTrackingActive",
                        trackingActiveAnimatorType = AnimatorControllerParameterType.Float,
                        trackingActiveDefault = 1f,
                        trackingParameterNames =
                            new Dictionary<AdvancedVisemeArticulator, string>(),
                        auxiliaryTrackingParameterNames = new Dictionary<string, string>(),
                        sourceVisemeBlendShapes = VisemeReconstructionProfile.VisemeNames
                            .Select(name => "vrc.v_" + name).ToArray(),
                        calibration = calibration,
                        calibrationBasis = new[]
                        {
                            new AdvancedVisemeMeshCalibrator.BasisInput(
                                AdvancedVisemeArticulator.JawOpen, 0),
                            new AdvancedVisemeMeshCalibrator.BasisInput(
                                AdvancedVisemeArticulator.LipClose, 1),
                            new AdvancedVisemeMeshCalibrator.BasisInput(
                                AdvancedVisemeArticulator.TongueOut, 2)
                        },
                        resolvedBlendShapes = new Dictionary<AdvancedVisemeArticulator, string>
                        {
                            [AdvancedVisemeArticulator.JawOpen] = "JawBasis",
                            [AdvancedVisemeArticulator.LipClose] = "LipBasis",
                            [AdvancedVisemeArticulator.TongueOut] = "TongueBasis"
                        },
                        externalPoses =
                            new Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose>(),
                        targetMesh = mesh,
                        trackingEnabled = true,
                        existingExpressionParameters = new HashSet<string>(),
                        linkedRendererOutputs = Array.Empty<
                            AdvancedVisemeAnimatorBuilder.LinkedRendererOutput>()
                    });

                var jawGain = result.trackingGainParameters[AdvancedVisemeArticulator.JawOpen];
                var lipGain = result.trackingGainParameters[AdvancedVisemeArticulator.LipClose];
                var tongueGain = result.trackingGainParameters[AdvancedVisemeArticulator.TongueOut];
                var minimum = result.controller.parameters.Select(parameter => parameter.name)
                    .Single(name => name.Contains(
                                           "/Primary/Dependency/", StringComparison.Ordinal) &&
                                    name.EndsWith("/Authority/1", StringComparison.Ordinal));
                var dependencies = BuildParameterDependencies(result.controller);
                Assert.That(DependsOn(dependencies, minimum, jawGain), Is.True);
                Assert.That(DependsOn(dependencies, minimum, lipGain), Is.True,
                    "A dependent lip ray must be able to veto removal of shared jaw/lip geometry.");
                Assert.That(DependsOn(dependencies, minimum, tongueGain), Is.False,
                    "An independent tongue ray must not inherit jaw/lip rank ambiguity.");

                var tongueYield = result.controller.parameters.Select(parameter => parameter.name)
                    .Single(name => name.EndsWith(
                        "/Residual/Ownership/TongueOut/Yield", StringComparison.Ordinal));
                Assert.That(DependsOn(dependencies, tongueYield, tongueGain), Is.True);
                Assert.That(DependsOn(dependencies, tongueYield, jawGain), Is.False);
                Assert.That(DependsOn(dependencies, tongueYield, lipGain), Is.False);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void AssertOwnershipProduct(
            IEnumerable<BlendTree> trees,
            string controllerPath,
            string carrier,
            string projected,
            string authorityYield,
            string message = null)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(controllerPath)
                .OfType<AnimationClip>()
                .Single(candidate => candidate.name == "Blendshape " + carrier);
            var product = trees
                .Where(tree =>
                    ContainsMotion(tree, clip) &&
                    UsesParameter(tree, projected) &&
                    UsesParameter(tree, authorityYield))
                .OrderBy(MotionNodeCount)
                .FirstOrDefault();
            Assert.That(product, Is.Not.Null,
                message ?? "The carrier pose must consume its matching matrix projection " +
                "and corresponding authority yield.");
        }

        private static int MotionNodeCount(Motion motion)
        {
            if (!(motion is BlendTree tree)) return 1;
            return 1 + tree.children.Sum(child => MotionNodeCount(child.motion));
        }

        private static bool ContainsMotion(Motion root, Motion target)
        {
            if (root == target) return true;
            return root is BlendTree tree &&
                   tree.children.Any(child => ContainsMotion(child.motion, target));
        }

        private static Dictionary<string, HashSet<string>> BuildParameterDependencies(
            AnimatorController controller)
        {
            var dependencies = new Dictionary<string, HashSet<string>>(
                StringComparer.Ordinal);

            void Visit(Motion motion, HashSet<string> controls)
            {
                if (motion is AnimationClip clip)
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type != typeof(Animator)) continue;
                        if (!dependencies.TryGetValue(binding.propertyName, out var inputs))
                            dependencies[binding.propertyName] = inputs =
                                new HashSet<string>(StringComparer.Ordinal);
                        inputs.UnionWith(controls);
                    }
                    return;
                }
                if (!(motion is BlendTree tree)) return;

                var treeControls = new HashSet<string>(controls, StringComparer.Ordinal);
                if (tree.blendType != BlendTreeType.Direct)
                {
                    if (!string.IsNullOrEmpty(tree.blendParameter))
                        treeControls.Add(tree.blendParameter);
                    if (tree.blendType != BlendTreeType.Simple1D &&
                        !string.IsNullOrEmpty(tree.blendParameterY))
                        treeControls.Add(tree.blendParameterY);
                }
                foreach (var child in tree.children)
                {
                    var childControls = new HashSet<string>(
                        treeControls, StringComparer.Ordinal);
                    if (tree.blendType == BlendTreeType.Direct &&
                        !string.IsNullOrEmpty(child.directBlendParameter) &&
                        child.directBlendParameter != "__YUCP_AVR_ONE")
                        childControls.Add(child.directBlendParameter);
                    Visit(child.motion, childControls);
                }
            }

            foreach (var layer in controller.layers)
            foreach (var state in layer.stateMachine.states)
                Visit(state.state.motion, new HashSet<string>(StringComparer.Ordinal));
            return dependencies;
        }

        private static bool DependsOn(
            IReadOnlyDictionary<string, HashSet<string>> dependencies,
            string output,
            string input)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            bool Visit(string parameter)
            {
                if (!visited.Add(parameter) ||
                    !dependencies.TryGetValue(parameter, out var direct)) return false;
                return direct.Contains(input) || direct.Any(Visit);
            }
            return Visit(output);
        }

        private static AdvancedVisemeMeshCalibrator.Result SyntheticCalibration(
            Mesh mesh,
            AdvancedVisemeArticulator[] basisArticulators,
            bool[] observable)
        {
            return new AdvancedVisemeMeshCalibrator.Result
            {
                mesh = mesh,
                coefficients = new float[
                    VisemeReconstructionProfile.VisemeCount,
                    basisArticulators.Length],
                ownershipProjectionCoefficients = new float[
                    VisemeReconstructionProfile.VisemeCount,
                    basisArticulators.Length],
                ownershipCarrierBlendShapeNames = new string[basisArticulators.Length],
                ownershipCarrierScales = new float[basisArticulators.Length],
                ownershipNegativeCarrierBlendShapeNames = new string[basisArticulators.Length],
                ownershipNegativeCarrierScales = new float[basisArticulators.Length],
                ownershipNonZeroSelectedColumns = Enumerable.Repeat(
                    true, basisArticulators.Length).ToArray(),
                residualBlendShapeNames =
                    new string[VisemeReconstructionProfile.VisemeCount],
                poseBasisAxes = Array.Empty<AdvancedVisemeMeshCalibrator.PoseBasisAxis>(),
                basisArticulators = (AdvancedVisemeArticulator[])basisArticulators.Clone(),
                basisDirections = Enumerable.Repeat(1, basisArticulators.Length).ToArray(),
                observableBasisColumns = (bool[])observable.Clone()
            };
        }

        private static bool UsesParameter(Motion motion, string parameter)
        {
            return UsesParameter(motion, parameter, new HashSet<int>());
        }

        private static bool WritesParameter(Motion motion, string parameter)
        {
            if (motion is AnimationClip clip)
                return AnimationUtility.GetCurveBindings(clip).Any(binding =>
                    binding.type == typeof(Animator) &&
                    string.Equals(binding.propertyName, parameter, StringComparison.Ordinal));
            if (!(motion is BlendTree tree)) return false;
            return tree.children.Any(child => WritesParameter(child.motion, parameter));
        }

        private static bool UsesParameter(
            Motion motion,
            string parameter,
            ISet<int> visited)
        {
            if (!(motion is BlendTree tree) || !visited.Add(tree.GetInstanceID())) return false;
            if (tree.blendParameter == parameter || tree.blendParameterY == parameter)
                return true;
            foreach (var child in tree.children)
            {
                if (child.directBlendParameter == parameter ||
                    UsesParameter(child.motion, parameter, visited))
                    return true;
            }
            return false;
        }
    }
}
#endif
