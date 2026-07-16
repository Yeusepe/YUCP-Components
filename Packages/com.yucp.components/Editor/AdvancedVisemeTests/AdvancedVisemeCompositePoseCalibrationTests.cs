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
    public sealed class AdvancedVisemeCompositePoseCalibrationTests
    {
        private const string RendererPath = "Face";
        private const float ReconstructionTolerance = 1e-5f;

        [Test]
        public void OverRangeAuthoredPoseUsesBoundedBasisAndExactResidual()
        {
            const int vertexCount = 3;
            var source = new Mesh { name = "Bounded Calibration" };
            source.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            source.normals = Enumerable.Repeat(Vector3.forward, vertexCount).ToArray();
            source.triangles = new[] { 0, 1, 2 };

            var basis = Delta.WithSingleVertex(
                vertexCount, 0,
                new Vector3(0.2f, 0.04f, -0.01f),
                new Vector3(0.03f, -0.005f, 0.002f),
                new Vector3(0.018f, 0.004f, -0.003f));
            var basisIndex = source.blendShapeCount;
            AddShape(source, "JawOpen", 100f, basis);

            var targets = new Delta[VisemeReconstructionProfile.VisemeCount];
            for (var viseme = 0; viseme < targets.Length; viseme++)
            {
                var scale = viseme == 1 ? 1.5f : 0.025f * viseme;
                var detail = Delta.WithSingleVertex(
                    vertexCount, 2,
                    new Vector3(0.001f, -0.0004f, 0.0007f) * viseme,
                    new Vector3(-0.0002f, 0.0003f, 0.0001f) * viseme,
                    new Vector3(0.0004f, 0.0002f, -0.0001f) * viseme);
                targets[viseme] = Add(Scale(basis, scale), detail);
                AddShape(
                    source,
                    "vrc.v_" + VisemeReconstructionProfile.VisemeNames[viseme],
                    100f,
                    targets[viseme]);
            }

            AdvancedVisemeMeshCalibrator.Result result = null;
            try
            {
                result = AdvancedVisemeMeshCalibrator.Build(
                    source,
                    VisemeIndices(source),
                    new[]
                    {
                        new AdvancedVisemeMeshCalibrator.BasisInput(
                            AdvancedVisemeArticulator.JawOpen, basisIndex)
                    });

                Assert.That(result.success, Is.True, result.error);
                for (var viseme = 0; viseme < targets.Length; viseme++)
                {
                    Assert.That(result.coefficients[viseme, 0], Is.InRange(0f, 1f),
                        $"production coefficient {viseme}");
                }

                var coefficient = result.coefficients[1, 0];
                Assert.That(coefficient, Is.EqualTo(1f).Within(1e-6f),
                    "The 150% authored ray must saturate at the usable blendshape endpoint.");
                Assert.That(coefficient * 100f, Is.LessThanOrEqualTo(100f),
                    "The generated basis drive may not rely on a VRChat-clamped weight.");

                var residual = ReadDelta(
                    result.mesh,
                    result.mesh.GetBlendShapeIndex(result.residualBlendShapeNames[1]));
                var expectedResidual = Subtract(targets[1], Scale(basis, coefficient));
                AssertDeltaEqual(expectedResidual, residual, "bounded 150% residual");

                var overflow = Scale(basis, 0.5f);
                Assert.That(
                    Vector3.Distance(residual.vertices[0], overflow.vertices[0]),
                    Is.LessThan(ReconstructionTolerance),
                    "The geometry above 100% must remain in the residual.");
                Assert.That(
                    Vector3.Distance(residual.normals[0], overflow.normals[0]),
                    Is.LessThan(ReconstructionTolerance));
                Assert.That(
                    Vector3.Distance(residual.tangents[0], overflow.tangents[0]),
                    Is.LessThan(ReconstructionTolerance));

                AssertDeltaEqual(
                    targets[1],
                    Add(Scale(basis, coefficient), residual),
                    "bounded 150% reconstruction");
            }
            finally
            {
                if (result?.mesh != null) UnityEngine.Object.DestroyImmediate(result.mesh);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CompositePositiveAndNegativePoseRaysExactlyReconstructVisemesAndConvexBlends()
        {
            var source = CreateCompositeCalibrationMesh();
            var positive = CreatePoseClip(
                "Composite Positive",
                (RendererPath, "PositiveA", 80f),
                (RendererPath, "PositiveB", 50f));
            var negative = CreatePoseClip(
                "Composite Negative",
                (RendererPath, "NegativeA", 60f),
                (RendererPath, "NegativeB", 25f));
            AdvancedVisemeMeshCalibrator.Result result = null;
            try
            {
                var sourceSnapshot = Snapshot(source);
                var visemeIndices = VisemeIndices(source);
                var positiveRay = CompositeRay(source, ("PositiveA", 0.8f), ("PositiveB", 0.5f));
                var negativeRay = CompositeRay(source, ("NegativeA", 0.6f), ("NegativeB", 0.25f));
                var basis = new[]
                {
                    new AdvancedVisemeMeshCalibrator.PoseBasisInput(
                        AdvancedVisemeArticulator.MouthX, 1, positive, RendererPath),
                    new AdvancedVisemeMeshCalibrator.PoseBasisInput(
                        AdvancedVisemeArticulator.MouthX, -1, negative, RendererPath)
                };

                result = AdvancedVisemeMeshCalibrator.BuildFromPoses(
                    source, visemeIndices, basis);

                Assert.That(result.success, Is.True, result.error);
                AssertSourceUnchanged(source, sourceSnapshot);
                Assert.That(result.mesh, Is.Not.SameAs(source));
                Assert.That(result.poseBasisAxes, Has.Length.EqualTo(2));
                Assert.That(Enumerable.Range(0, result.mesh.blendShapeCount)
                    .Select(result.mesh.GetBlendShapeName)
                    .Any(name => name.Contains("_Conflict_", StringComparison.Ordinal)), Is.False,
                    "Low-rank ownership must not emit one conflict morph per viseme.");
                AssertPoseAxis(
                    result.poseBasisAxes[0], AdvancedVisemeArticulator.MouthX,
                    1, positive, RendererPath);
                AssertPoseAxis(
                    result.poseBasisAxes[1], AdvancedVisemeArticulator.MouthX,
                    -1, negative, RendererPath);

                var targets = new Delta[VisemeReconstructionProfile.VisemeCount];
                var residuals = new Delta[VisemeReconstructionProfile.VisemeCount];
                var stableResiduals = new Delta[VisemeReconstructionProfile.VisemeCount];
                var projected = new Delta[VisemeReconstructionProfile.VisemeCount];
                var positiveCarriers = ReadCarrierDeltas(
                    result.mesh, result.ownershipCarrierBlendShapeNames);
                var negativeCarriers = ReadCarrierDeltas(
                    result.mesh, result.ownershipNegativeCarrierBlendShapeNames);
                for (var column = 0; column < positiveCarriers.Length; column++)
                {
                    AssertCarrierWeightsAreNormalizedNonnegative(
                        result, column, result.ownershipCarrierScales[column], false);
                    AssertCarrierWeightsAreNormalizedNonnegative(
                        result, column, result.ownershipNegativeCarrierScales[column], true);
                }
                for (var viseme = 0; viseme < targets.Length; viseme++)
                {
                    targets[viseme] = ReadDelta(source, visemeIndices[viseme]);
                    residuals[viseme] = ReadDelta(
                        result.mesh,
                        result.mesh.GetBlendShapeIndex(result.residualBlendShapeNames[viseme]));
                    projected[viseme] = Delta.Zero(source.vertexCount);
                    for (var column = 0; column < positiveCarriers.Length; column++)
                    {
                        var coefficient = result.ownershipProjectionCoefficients[viseme, column];
                        var positiveScale = result.ownershipCarrierScales[column];
                        if (coefficient < 0f && positiveScale > 1e-7f)
                            AddScaled(projected[viseme], positiveCarriers[column],
                                coefficient / positiveScale);
                        var negativeScale = result.ownershipNegativeCarrierScales[column];
                        if (coefficient > 0f && negativeScale > 1e-7f)
                            AddScaled(projected[viseme], negativeCarriers[column],
                                -coefficient / negativeScale);
                    }
                    stableResiduals[viseme] = Subtract(
                        residuals[viseme], projected[viseme]);

                    Assert.That(
                        result.coefficients[viseme, 0],
                        Is.EqualTo(Mathf.Max(0f, AuthoredPositiveCoefficient(viseme))).Within(1e-5f),
                        $"Positive composite-ray coefficient for {VisemeReconstructionProfile.VisemeNames[viseme]}.");
                    Assert.That(
                        result.coefficients[viseme, 1],
                        Is.EqualTo(NegativeCoefficient(viseme)).Within(1e-5f),
                        $"Negative composite-ray coefficient for {VisemeReconstructionProfile.VisemeNames[viseme]}.");

                    AssertDeltaEqual(
                        residuals[viseme],
                        Add(stableResiduals[viseme], projected[viseme]),
                        $"stable plus projection {VisemeReconstructionProfile.VisemeNames[viseme]}");
                    AssertOrthogonalToBasis(
                        stableResiduals[viseme],
                        positiveRay,
                        negativeRay,
                        $"stable residual {VisemeReconstructionProfile.VisemeNames[viseme]}");

                    var expectedConflict = Scale(
                        positiveRay,
                        Mathf.Min(0f, AuthoredPositiveCoefficient(viseme)));
                    AssertDeltaEqual(
                        expectedConflict,
                        projected[viseme],
                        $"basis projection {VisemeReconstructionProfile.VisemeNames[viseme]}");

                    // Retention one is the exact authored identity V=UC+R.
                    var reconstructedAtRetentionOne = Add(
                        Scale(positiveRay, result.coefficients[viseme, 0]),
                        Scale(negativeRay, result.coefficients[viseme, 1]),
                        residuals[viseme]);
                    AssertDeltaEqual(
                        targets[viseme], reconstructedAtRetentionOne,
                        $"retention-one viseme {VisemeReconstructionProfile.VisemeNames[viseme]}");

                    // Retention zero removes only the U-span conflict. The held
                    // tracked basis coordinate remains invariant while unique R
                    // detail at the fifth vertex survives.
                    var trackedBasis = Add(
                        Scale(positiveRay, 0.37f),
                        Scale(negativeRay, 0.22f));
                    var reconstructedAtRetentionZero = Add(
                        trackedBasis,
                        residuals[viseme],
                        Scale(projected[viseme], -1f));
                    var retainedDetail = Subtract(
                        reconstructedAtRetentionZero, trackedBasis);
                    Assert.That(SquaredMagnitude(retainedDetail.vertices), Is.GreaterThan(1e-12d),
                        "Conflict removal erased the stable authored complement.");
                    AssertOrthogonalToBasis(
                        retainedDetail,
                        positiveRay,
                        negativeRay,
                        $"retention-zero tracked projection {VisemeReconstructionProfile.VisemeNames[viseme]}");
                }

                var random = new System.Random(0x5A17E);
                for (var sample = 0; sample < 64; sample++)
                {
                    var weights = RandomSimplex(random, targets.Length);
                    var authored = WeightedSum(targets, weights);
                    var reconstructed = Delta.Zero(source.vertexCount);
                    for (var viseme = 0; viseme < weights.Length; viseme++)
                    {
                        var decomposed = Add(
                            Scale(positiveRay, result.coefficients[viseme, 0]),
                            Scale(negativeRay, result.coefficients[viseme, 1]),
                            residuals[viseme]);
                        AddScaled(reconstructed, decomposed, weights[viseme]);
                    }
                    AssertDeltaEqual(authored, reconstructed, $"convex blend {sample}");
                }
            }
            finally
            {
                if (result?.mesh != null) UnityEngine.Object.DestroyImmediate(result.mesh);
                UnityEngine.Object.DestroyImmediate(positive);
                UnityEngine.Object.DestroyImmediate(negative);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CompositePoseRejectsNonlinearMultiFrameBlendShape()
        {
            var source = CreateCompositeCalibrationMesh(includeNonlinearShape: true);
            var clip = CreatePoseClip(
                "Nonlinear Composite Pose",
                (RendererPath, "Nonlinear", 100f));
            try
            {
                var result = AdvancedVisemeMeshCalibrator.BuildFromPoses(
                    source,
                    VisemeIndices(source),
                    new[]
                    {
                        new AdvancedVisemeMeshCalibrator.PoseBasisInput(
                            AdvancedVisemeArticulator.JawOpen, 1, clip, RendererPath)
                    });

                Assert.That(result.success, Is.False);
                Assert.That(result.mesh, Is.Null);
                Assert.That(result.error, Does.Contain("nonlinear"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CompositePoseRejectsEndpointAboveVrchatClamp()
        {
            var source = CreateCompositeCalibrationMesh();
            var clip = CreatePoseClip(
                "Over-range Composite Pose",
                (RendererPath, "PositiveA", 150f));
            try
            {
                var result = AdvancedVisemeMeshCalibrator.BuildFromPoses(
                    source,
                    VisemeIndices(source),
                    new[]
                    {
                        new AdvancedVisemeMeshCalibrator.PoseBasisInput(
                            AdvancedVisemeArticulator.JawOpen, 1, clip, RendererPath)
                    });

                Assert.That(result.success, Is.False);
                Assert.That(result.mesh, Is.Null);
                Assert.That(result.error,
                    Does.Contain("0..100").And.Contain("150"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CompositePoseRejectsBlendShapeSharedBySignedEndpoints()
        {
            var source = CreateCompositeCalibrationMesh();
            var positive = CreatePoseClip(
                "Shared Positive Endpoint",
                (RendererPath, "PositiveA", 80f));
            var negative = CreatePoseClip(
                "Shared Negative Endpoint",
                (RendererPath, "PositiveA", 45f));
            try
            {
                var result = AdvancedVisemeMeshCalibrator.BuildFromPoses(
                    source,
                    VisemeIndices(source),
                    new[]
                    {
                        new AdvancedVisemeMeshCalibrator.PoseBasisInput(
                            AdvancedVisemeArticulator.MouthX, 1, positive, RendererPath),
                        new AdvancedVisemeMeshCalibrator.PoseBasisInput(
                            AdvancedVisemeArticulator.MouthX, -1, negative, RendererPath)
                    });

                Assert.That(result.success, Is.False);
                Assert.That(result.mesh, Is.Null);
                Assert.That(result.error,
                    Does.Contain("share active blendshape")
                        .And.Contain("PositiveA")
                        .And.Contain("positive endpoint")
                        .And.Contain("negative endpoint")
                        .And.Contain("0..100"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(positive);
                UnityEngine.Object.DestroyImmediate(negative);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void PrimaryCalibrationRejectsNonlinearMultiFrameVisemesAndBasisShapes()
        {
            var source = CreateCompositeCalibrationMesh(includeNonlinearShape: true);
            try
            {
                var visemes = VisemeIndices(source);
                var nonlinear = source.GetBlendShapeIndex("Nonlinear");
                var nonlinearBasis = AdvancedVisemeMeshCalibrator.Build(
                    source,
                    visemes,
                    new[]
                    {
                        new AdvancedVisemeMeshCalibrator.BasisInput(
                            AdvancedVisemeArticulator.JawOpen, nonlinear)
                    });
                Assert.That(nonlinearBasis.success, Is.False);
                Assert.That(nonlinearBasis.error, Does.Contain("nonlinear"));

                visemes[1] = nonlinear;
                var nonlinearViseme = AdvancedVisemeMeshCalibrator.Build(
                    source,
                    visemes,
                    new[]
                    {
                        new AdvancedVisemeMeshCalibrator.BasisInput(
                            AdvancedVisemeArticulator.JawOpen,
                            source.GetBlendShapeIndex("PositiveA"))
                    });
                Assert.That(nonlinearViseme.success, Is.False);
                Assert.That(nonlinearViseme.error, Does.Contain("nonlinear"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CompositePoseRejectsCurveOnWrongRenderer()
        {
            var source = CreateCompositeCalibrationMesh();
            var clip = CreatePoseClip(
                "Wrong Renderer Pose",
                ("OtherFace", "PositiveA", 100f));
            try
            {
                var result = AdvancedVisemeMeshCalibrator.BuildFromPoses(
                    source,
                    VisemeIndices(source),
                    new[]
                    {
                        new AdvancedVisemeMeshCalibrator.PoseBasisInput(
                            AdvancedVisemeArticulator.JawOpen, 1, clip, RendererPath)
                    });

                Assert.That(result.success, Is.False);
                Assert.That(result.mesh, Is.Null);
                Assert.That(result.error, Does.Contain("unsupported curve"));
                Assert.That(result.error, Does.Contain("OtherFace"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CompositePoseRejectsNonBlendShapeCurve()
        {
            var source = CreateCompositeCalibrationMesh();
            var clip = new AnimationClip { name = "Transform Pose" };
            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(
                    RendererPath, typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Constant(0f, 1f / 60f, 0.1f));
            try
            {
                var result = AdvancedVisemeMeshCalibrator.BuildFromPoses(
                    source,
                    VisemeIndices(source),
                    new[]
                    {
                        new AdvancedVisemeMeshCalibrator.PoseBasisInput(
                            AdvancedVisemeArticulator.JawOpen, 1, clip, RendererPath)
                    });

                Assert.That(result.success, Is.False);
                Assert.That(result.mesh, Is.Null);
                Assert.That(result.error, Does.Contain("unsupported curve"));
                Assert.That(result.error, Does.Contain("m_LocalPosition.x"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CalibratedReuseGraphUsesFullSpeechResidualAndDrivesCompositeExternalBasis()
        {
            var source = CreateCompositeCalibrationMesh();
            var positive = CreatePoseClip(
                "Composite Jaw Positive",
                (RendererPath, "PositiveA", 80f),
                (RendererPath, "PositiveB", 50f));
            var root = new GameObject("Calibrated Composite Reuse Graph");
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            var folderName = "__YUCP_AVR_CompositeReuse_" + Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            AdvancedVisemeMeshCalibrator.Result calibration = null;
            try
            {
                calibration = AdvancedVisemeMeshCalibrator.BuildFromPoses(
                    source,
                    VisemeIndices(source),
                    new[]
                    {
                        new AdvancedVisemeMeshCalibrator.PoseBasisInput(
                            AdvancedVisemeArticulator.JawOpen, 1, positive, RendererPath)
                    });
                Assert.That(calibration.success, Is.True, calibration.error);
                Assert.That(calibration.ownershipCarrierBlendShapeNames
                                .Concat(calibration.ownershipNegativeCarrierBlendShapeNames)
                                .Count(name => !string.IsNullOrEmpty(name)),
                    Is.GreaterThan(0));
                Assert.That(calibration.hiddenPhoneResidualBlendShapeName,
                    Is.Not.Null.And.Not.Empty);

                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.mouthOwnership = AdvancedVisemeMouthOwnership.DriveLowerFace;
                component.trackingInputs = AdvancedVisemeTrackingInputs.ReuseExisting;
                component.reconstructionMode = AdvancedVisemeReconstructionMode.BetaCoarticulation;
                component.createTuningMenu = false;
                var controllerPath = folder + "/AdvancedViseme.controller";
                var result = AdvancedVisemeAnimatorBuilder.Build(
                    new AdvancedVisemeAnimatorBuilder.Request
                    {
                        controllerPath = controllerPath,
                        parametersPath = folder + "/TrackingParameters.asset",
                        rendererPath = RendererPath,
                        component = component,
                        profile = profile,
                        trackingPrefix = "Tailored",
                        effectiveTrackingInputs = AdvancedVisemeTrackingInputs.Balanced8,
                        reuseExistingTracking = true,
                        trackingActiveParameter = "Tailored/v2/LipTrackingActive",
                        trackingActiveAnimatorType = AnimatorControllerParameterType.Float,
                        trackingActiveDefault = 1f,
                        trackingParameterNames = new Dictionary<AdvancedVisemeArticulator, string>
                        {
                            [AdvancedVisemeArticulator.JawOpen] = "Tailored/v2/JawOpen",
                            [AdvancedVisemeArticulator.LipClose] = "Tailored/v2/MouthClosed",
                            [AdvancedVisemeArticulator.MouthOpen] = "Tailored/v2/MouthOpen"
                        },
                        directPoseArticulators = new[]
                        {
                            AdvancedVisemeArticulator.JawOpen
                        },
                        sourceVisemeBlendShapes = VisemeReconstructionProfile.VisemeNames
                            .Select(name => "vrc.v_" + name).ToArray(),
                        calibration = calibration,
                        calibrationBasis = Array.Empty<AdvancedVisemeMeshCalibrator.BasisInput>(),
                        resolvedBlendShapes = new Dictionary<AdvancedVisemeArticulator, string>(),
                        externalPoses = new Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose>
                        {
                            [AdvancedVisemeArticulator.JawOpen] = new AdvancedVisemeExternalPose
                            {
                                positive = positive,
                                positiveThreshold = 1f
                            },
                            [AdvancedVisemeArticulator.LipClose] = new AdvancedVisemeExternalPose
                            {
                                positive = positive,
                                positiveThreshold = 1f
                            },
                            [AdvancedVisemeArticulator.MouthOpen] = new AdvancedVisemeExternalPose
                            {
                                positive = positive,
                                positiveThreshold = 1f
                            }
                        },
                        targetMesh = source,
                        trackingEnabled = true,
                        existingExpressionParameters = new HashSet<string>()
                    });

                var controllerParameters = result.controller.parameters
                    .Select(parameter => parameter.name).ToArray();
                Assert.That(controllerParameters.Any(parameter =>
                    parameter.Contains("RetainedResidualWeight")), Is.False,
                    "A calibrated residual must not pass through the unsafe visible-pose remainder.");
                Assert.That(controllerParameters.Any(parameter =>
                    parameter.EndsWith("/Residual/Retention", StringComparison.Ordinal)), Is.False);
                Assert.That(controllerParameters.Any(parameter =>
                    parameter.EndsWith("/Residual/ConflictRemoval", StringComparison.Ordinal)), Is.False);

                var trees = AssetDatabase.LoadAllAssetsAtPath(controllerPath)
                    .OfType<BlendTree>().ToArray();
                var dependencies = BuildParameterDependencies(result.controller);
                var authoredDetail = controllerParameters.Single(parameter =>
                    parameter.EndsWith("/Tuning/AuthoredDetail", StringComparison.Ordinal));
                for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
                {
                    var speechWeight = controllerParameters.Single(parameter =>
                        parameter.EndsWith(
                            $"/Viseme/{viseme}/SpeechWeight", StringComparison.Ordinal));
                    var residualWeight = controllerParameters.Single(parameter =>
                        parameter.EndsWith(
                            $"/Viseme/{viseme}/ResidualWeight", StringComparison.Ordinal));
                    Assert.That(DependsOn(
                            dependencies, residualWeight, speechWeight), Is.True,
                        $"Calibrated residual {viseme} must consume the full speech simplex.");
                    Assert.That(DependsOn(
                            dependencies, residualWeight, authoredDetail), Is.True,
                        "Authored detail must scale the complete residual simplex.");
                }
                Assert.That(controllerParameters
                        .Where(parameter => parameter.Contains(
                            "/VisibleSpeechWeight", StringComparison.Ordinal))
                        .Any(parameter => controllerParameters
                            .Where(candidate => candidate.EndsWith(
                                "/ResidualWeight", StringComparison.Ordinal))
                            .Any(residual => DependsOn(
                                dependencies, residual, parameter))),
                    Is.False,
                    "Tracking authority may replace U(Cp), but must not erase R.");

                for (var column = 0;
                     column < calibration.ownershipCarrierBlendShapeNames.Length;
                     column++)
                {
                    AssertNonnegativeCarrierDrive(
                        calibration.ownershipCarrierBlendShapeNames[column],
                        $"Primary/Ownership/{column}/Add",
                        controllerParameters, trees, dependencies, controllerPath);
                    AssertNonnegativeCarrierDrive(
                        calibration.ownershipNegativeCarrierBlendShapeNames[column],
                        $"Primary/Ownership/{column}/Subtract",
                        controllerParameters, trees, dependencies, controllerPath);
                }

                var hiddenSpeechDelta = controllerParameters.Single(parameter =>
                    parameter.EndsWith(
                        "/PhonePosterior/Residual/SpeechDelta",
                        StringComparison.Ordinal));
                Assert.That(controllerParameters.Any(parameter =>
                    parameter.Contains("PhonePosterior/Residual/RetainedSpeechDelta")), Is.False,
                    "The stable hidden-phone complement must not consume contradiction retention.");
                var hiddenClip = AssetDatabase.LoadAllAssetsAtPath(controllerPath)
                    .OfType<AnimationClip>()
                    .Single(clip => clip.name == "Blendshape " +
                        calibration.hiddenPhoneResidualBlendShapeName);
                Assert.That(trees.Any(tree =>
                        ContainsMotion(tree, hiddenClip) &&
                        UsesParameter(tree, hiddenSpeechDelta)), Is.True,
                    "The stable hidden-phone residual was not driven directly.");

                var externalBasisClip = AssetDatabase.LoadAllAssetsAtPath(controllerPath)
                    .OfType<AnimationClip>()
                    .SingleOrDefault(clip => clip.name == "Calibrated JawOpen Positive");
                Assert.That(externalBasisClip, Is.Not.Null,
                    "The calibrated tailored-template basis was not emitted into the output layer.");
                var externalBindings = AnimationUtility.GetCurveBindings(externalBasisClip);
                Assert.That(externalBindings.Select(binding => binding.path),
                    Is.All.EqualTo(RendererPath));
                Assert.That(externalBindings.Select(binding => binding.propertyName),
                    Is.EquivalentTo(new[]
                    {
                        "blendShape.PositiveA",
                        "blendShape.PositiveB"
                    }));
                var directTrackingGain = result.trackingGainParameters[
                    AdvancedVisemeArticulator.JawOpen];
                Assert.That(trees.Any(tree =>
                        ContainsMotion(tree, externalBasisClip) &&
                        UsesParameter(tree, directTrackingGain) &&
                        UsesParameter(tree, "Tailored/v2/JawOpen")), Is.True,
                    "A rig-connected decoded proxy must reach the calibrated pose through " +
                    "the native tracking gate.");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                if (calibration?.mesh != null) UnityEngine.Object.DestroyImmediate(calibration.mesh);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(positive);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CalibratedSignedExternalRayProjectsTargetIntoUnitIntervalBeforePoseDrive()
        {
            var source = CreateCompositeCalibrationMesh();
            var positive = CreatePoseClip(
                "Signed Mouth Positive",
                (RendererPath, "PositiveA", 80f),
                (RendererPath, "PositiveB", 50f));
            var negative = CreatePoseClip(
                "Signed Mouth Negative",
                (RendererPath, "NegativeA", 60f),
                (RendererPath, "NegativeB", 25f));
            var root = new GameObject("Signed External Clamp Graph");
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            var folderName = "__YUCP_AVR_SignedExternalClamp_" + Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            AdvancedVisemeMeshCalibrator.Result calibration = null;
            try
            {
                calibration = AdvancedVisemeMeshCalibrator.BuildFromPoses(
                    source,
                    VisemeIndices(source),
                    new[]
                    {
                        new AdvancedVisemeMeshCalibrator.PoseBasisInput(
                            AdvancedVisemeArticulator.MouthX, 1, positive, RendererPath),
                        new AdvancedVisemeMeshCalibrator.PoseBasisInput(
                            AdvancedVisemeArticulator.MouthX, -1, negative, RendererPath)
                    });
                Assert.That(calibration.success, Is.True, calibration.error);

                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.mouthOwnership = AdvancedVisemeMouthOwnership.DriveLowerFace;
                component.trackingInputs = AdvancedVisemeTrackingInputs.ReuseExisting;
                component.reconstructionMode = AdvancedVisemeReconstructionMode.BetaCoarticulation;
                component.createTuningMenu = false;
                var controllerPath = folder + "/AdvancedViseme.controller";
                var result = AdvancedVisemeAnimatorBuilder.Build(
                    new AdvancedVisemeAnimatorBuilder.Request
                    {
                        controllerPath = controllerPath,
                        parametersPath = folder + "/TrackingParameters.asset",
                        rendererPath = RendererPath,
                        component = component,
                        profile = profile,
                        trackingPrefix = "Tailored",
                        effectiveTrackingInputs = AdvancedVisemeTrackingInputs.Quality12,
                        reuseExistingTracking = true,
                        trackingActiveParameter = "Tailored/v2/LipTrackingActive",
                        trackingActiveAnimatorType = AnimatorControllerParameterType.Float,
                        trackingActiveDefault = 1f,
                        trackingParameterNames = new Dictionary<AdvancedVisemeArticulator, string>
                        {
                            [AdvancedVisemeArticulator.MouthX] = "Tailored/v2/MouthX"
                        },
                        sourceVisemeBlendShapes = VisemeReconstructionProfile.VisemeNames
                            .Select(name => "vrc.v_" + name).ToArray(),
                        calibration = calibration,
                        calibrationBasis = Array.Empty<AdvancedVisemeMeshCalibrator.BasisInput>(),
                        resolvedBlendShapes = new Dictionary<AdvancedVisemeArticulator, string>(),
                        externalPoses = new Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose>
                        {
                            [AdvancedVisemeArticulator.MouthX] = new AdvancedVisemeExternalPose
                            {
                                positive = positive,
                                negative = negative,
                                positiveThreshold = 1f,
                                negativeThreshold = -1f
                            }
                        },
                        targetMesh = source,
                        trackingEnabled = true,
                        existingExpressionParameters = new HashSet<string>()
                    });

                var parameters = result.controller.parameters
                    .Select(parameter => parameter.name).ToArray();
                var trees = AssetDatabase.LoadAllAssetsAtPath(controllerPath)
                    .OfType<BlendTree>().ToArray();
                foreach (var direction in new[] { "Positive", "Negative" })
                {
                    var targetRaw = parameters.Single(parameter => parameter.EndsWith(
                        $"/ExternalBasis/MouthX/{direction}/TargetMass",
                        StringComparison.Ordinal));
                    var targetClamped = parameters.Single(parameter => parameter.EndsWith(
                        $"/ExternalBasis/MouthX/{direction}/TargetClamped",
                        StringComparison.Ordinal));
                    var reconciliation = parameters.Single(parameter => parameter.EndsWith(
                        $"/ExternalBasis/MouthX/{direction}/Reconciliation",
                        StringComparison.Ordinal));
                    var primaryWeight = parameters.Single(parameter => parameter.EndsWith(
                        $"/ExternalBasis/MouthX/{direction}/PrimaryWeight",
                        StringComparison.Ordinal));

                    var clamp = trees.SingleOrDefault(tree =>
                        tree.blendType == BlendTreeType.Simple1D &&
                        tree.blendParameter == targetRaw &&
                        WritesParameter(tree, targetClamped));
                    Assert.That(clamp, Is.Not.Null,
                        $"The {direction.ToLowerInvariant()} tailored ray bypasses its unit clamp.");
                    AssertMapEndpoints(
                        clamp,
                        targetClamped,
                        (-1f, 0f), (0f, 0f), (1f, 1f), (2f, 1f));
                    var dependencies = BuildParameterDependencies(result.controller);
                    Assert.That(DependsOn(
                            dependencies, reconciliation, targetClamped), Is.True,
                        "Reconciliation must consume the clamped coordinate, not the raw sum.");
                    Assert.That(DependsOn(
                            dependencies, primaryWeight, reconciliation), Is.True,
                        "The clamped reconciliation must remain connected to the final pose drive.");
                    var poseName = $"Calibrated MouthX {direction}";
                    var pose = AssetDatabase.LoadAllAssetsAtPath(controllerPath)
                        .OfType<AnimationClip>()
                        .Single(clip => clip.name == poseName);
                    Assert.That(trees.Any(tree =>
                            ContainsMotion(tree, pose) &&
                            UsesParameter(tree, primaryWeight)), Is.True,
                        "The reconciled coordinate must drive the matching calibrated ray.");
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                if (calibration?.mesh != null) UnityEngine.Object.DestroyImmediate(calibration.mesh);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(positive);
                UnityEngine.Object.DestroyImmediate(negative);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private static Mesh CreateCompositeCalibrationMesh(bool includeNonlinearShape = false)
        {
            const int vertexCount = 5;
            var mesh = new Mesh { name = "Composite Pose Calibration" };
            mesh.vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up,
                Vector3.forward,
                Vector3.one
            };
            mesh.normals = Enumerable.Repeat(Vector3.forward, vertexCount).ToArray();
            mesh.triangles = new[] { 0, 1, 2, 2, 3, 4 };

            var positiveA = Delta.WithSingleVertex(
                vertexCount, 0,
                new Vector3(0.12f, 0.01f, 0f),
                new Vector3(0.02f, 0.003f, 0f),
                new Vector3(0.01f, 0.004f, 0f));
            var positiveB = Delta.WithSingleVertex(
                vertexCount, 1,
                new Vector3(0f, 0.16f, 0.01f),
                new Vector3(0f, 0.025f, 0.004f),
                new Vector3(0f, 0.012f, 0.003f));
            var negativeA = Delta.WithSingleVertex(
                vertexCount, 2,
                new Vector3(0.01f, 0f, 0.14f),
                new Vector3(0.003f, 0f, 0.021f),
                new Vector3(0.004f, 0f, 0.015f));
            var negativeB = Delta.WithSingleVertex(
                vertexCount, 3,
                new Vector3(0.18f, 0f, 0.01f),
                new Vector3(0.028f, 0f, 0.002f),
                new Vector3(0.016f, 0f, 0.004f));
            AddShape(mesh, "PositiveA", 100f, positiveA);
            AddShape(mesh, "PositiveB", 100f, positiveB);
            AddShape(mesh, "NegativeA", 100f, negativeA);
            AddShape(mesh, "NegativeB", 100f, negativeB);

            if (includeNonlinearShape)
            {
                var half = Delta.WithSingleVertex(
                    vertexCount, 0,
                    new Vector3(0.01f, 0f, 0f),
                    new Vector3(0.002f, 0f, 0f),
                    new Vector3(0.001f, 0f, 0f));
                var full = Delta.WithSingleVertex(
                    vertexCount, 0,
                    new Vector3(0.05f, 0f, 0f),
                    new Vector3(0.01f, 0f, 0f),
                    new Vector3(0.005f, 0f, 0f));
                AddShape(mesh, "Nonlinear", 50f, half);
                AddShape(mesh, "Nonlinear", 100f, full);
            }

            var positiveRay = Add(Scale(positiveA, 0.8f), Scale(positiveB, 0.5f));
            var negativeRay = Add(Scale(negativeA, 0.6f), Scale(negativeB, 0.25f));
            for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
            {
                var residual = Delta.WithSingleVertex(
                    vertexCount, 4,
                    new Vector3(
                        0.00031f * (viseme + 1),
                        -0.00017f * (viseme + 1),
                        0.00023f * (viseme + 1)),
                    new Vector3(
                        -0.00007f * (viseme + 1),
                        0.00011f * (viseme + 1),
                        0.00005f * (viseme + 1)),
                    new Vector3(
                        0.00009f * (viseme + 1),
                        0.00004f * (viseme + 1),
                        -0.00006f * (viseme + 1)));
                var target = Add(
                    Scale(positiveRay, AuthoredPositiveCoefficient(viseme)),
                    Scale(negativeRay, NegativeCoefficient(viseme)),
                    residual);
                AddShape(
                    mesh,
                    "vrc.v_" + VisemeReconstructionProfile.VisemeNames[viseme],
                    100f,
                    target);
            }
            return mesh;
        }

        private static float AuthoredPositiveCoefficient(int viseme)
        {
            if (viseme % 3 == 0) return -0.05f - 0.012f * viseme;
            return 0.08f + 0.035f * viseme;
        }

        private static float NegativeCoefficient(int viseme)
        {
            return 0.64f - 0.021f * viseme;
        }

        private static AnimationClip CreatePoseClip(
            string name,
            params (string path, string blendShape, float weight)[] curves)
        {
            var clip = new AnimationClip { name = name, frameRate = 60f };
            foreach (var curve in curves)
            {
                AnimationUtility.SetEditorCurve(
                    clip,
                    EditorCurveBinding.FloatCurve(
                        curve.path,
                        typeof(SkinnedMeshRenderer),
                        "blendShape." + curve.blendShape),
                    AnimationCurve.Constant(0f, 1f / 60f, curve.weight));
            }
            return clip;
        }

        private static int[] VisemeIndices(Mesh mesh)
        {
            return VisemeReconstructionProfile.VisemeNames
                .Select(name => mesh.GetBlendShapeIndex("vrc.v_" + name))
                .ToArray();
        }

        private static Delta CompositeRay(
            Mesh mesh,
            params (string blendShape, float scale)[] shapes)
        {
            var output = Delta.Zero(mesh.vertexCount);
            foreach (var shape in shapes)
                AddScaled(
                    output,
                    ReadDelta(mesh, mesh.GetBlendShapeIndex(shape.blendShape)),
                    shape.scale);
            return output;
        }

        private static void AssertPoseAxis(
            AdvancedVisemeMeshCalibrator.PoseBasisAxis actual,
            AdvancedVisemeArticulator articulator,
            int direction,
            AnimationClip clip,
            string rendererPath)
        {
            Assert.That(actual.articulator, Is.EqualTo(articulator));
            Assert.That(actual.direction, Is.EqualTo(direction));
            Assert.That(actual.clip, Is.SameAs(clip));
            Assert.That(actual.rendererPath, Is.EqualTo(rendererPath));
        }

        private static bool UsesParameter(Motion motion, string parameter)
        {
            if (!(motion is BlendTree tree)) return false;
            if (string.Equals(tree.blendParameter, parameter, StringComparison.Ordinal) ||
                string.Equals(tree.blendParameterY, parameter, StringComparison.Ordinal))
                return true;
            foreach (var child in tree.children)
            {
                if (string.Equals(
                        child.directBlendParameter, parameter, StringComparison.Ordinal) ||
                    UsesParameter(child.motion, parameter))
                    return true;
            }
            return false;
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

        private static bool ContainsMotion(Motion root, Motion target)
        {
            if (root == target) return true;
            if (!(root is BlendTree tree)) return false;
            return tree.children.Any(child => ContainsMotion(child.motion, target));
        }

        private static void AddShape(Mesh mesh, string name, float weight, Delta delta)
        {
            mesh.AddBlendShapeFrame(
                name, weight, delta.vertices, delta.normals, delta.tangents);
        }

        private static Delta ReadDelta(Mesh mesh, int index, int frame = -1)
        {
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            if (frame < 0) frame = mesh.GetBlendShapeFrameCount(index) - 1;
            var result = Delta.Zero(mesh.vertexCount);
            mesh.GetBlendShapeFrameVertices(
                index, frame, result.vertices, result.normals, result.tangents);
            return result;
        }

        private static Delta[] ReadCarrierDeltas(Mesh mesh, IEnumerable<string> names)
        {
            return names.Select(name => string.IsNullOrEmpty(name)
                    ? Delta.Zero(mesh.vertexCount)
                    : ReadDelta(mesh, mesh.GetBlendShapeIndex(name)))
                .ToArray();
        }

        private static void AssertCarrierWeightsAreNormalizedNonnegative(
            AdvancedVisemeMeshCalibrator.Result result,
            int column,
            float scale,
            bool positiveCoefficients)
        {
            var magnitudes = Enumerable.Range(
                    0, VisemeReconstructionProfile.VisemeCount)
                .Select(viseme => result.ownershipProjectionCoefficients[viseme, column])
                .Select(coefficient => positiveCoefficients
                    ? Mathf.Max(0f, coefficient)
                    : Mathf.Max(0f, -coefficient))
                .ToArray();
            if (magnitudes.All(value => value <= 1e-7f)) return;
            Assert.That(scale, Is.GreaterThan(1e-7f));
            Assert.That(magnitudes.Min(), Is.GreaterThanOrEqualTo(0f));
            Assert.That(magnitudes.Max() / scale, Is.LessThanOrEqualTo(1f + 1e-6f),
                "Ownership carrier weights must stay inside VRChat's 0..100 blendshape range.");
        }

        private static void AssertNonnegativeCarrierDrive(
            string carrier,
            string projectionKey,
            IReadOnlyList<string> controllerParameters,
            IReadOnlyList<BlendTree> trees,
            IReadOnlyDictionary<string, HashSet<string>> dependencies,
            string controllerPath)
        {
            if (string.IsNullOrEmpty(carrier)) return;
            var projectedParameter = controllerParameters.Single(parameter =>
                parameter.EndsWith(
                    "/" + projectionKey + "/Projected", StringComparison.Ordinal));
            Assert.That(dependencies.ContainsKey(projectedParameter), Is.True,
                "The ownership matrix must publish every carrier projection.");
            Assert.That(trees.Any(tree =>
                tree.name == "Signed pose " + projectedParameter), Is.False,
                "Ownership may not rely on negative final blendshape weights.");
            var clip = AssetDatabase.LoadAllAssetsAtPath(controllerPath)
                .OfType<AnimationClip>()
                .Single(candidate => candidate.name == "Blendshape " + carrier);
            var yieldParameters = controllerParameters.Where(parameter =>
                parameter.EndsWith("/Yield", StringComparison.Ordinal)).ToArray();
            var product = trees
                .Where(tree =>
                    ContainsMotion(tree, clip) &&
                    UsesParameter(tree, projectedParameter) &&
                    yieldParameters.Any(parameter => UsesParameter(tree, parameter)))
                .OrderBy(MotionNodeCount)
                .FirstOrDefault();
            Assert.That(product, Is.Not.Null,
                "Each ownership carrier must multiply its matching projection and " +
                "authority yield inside the pose path.");
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            foreach (var key in AnimationUtility.GetEditorCurve(clip, binding).keys)
                Assert.That(key.value, Is.InRange(0f, 100f));
        }

        private static int MotionNodeCount(Motion motion)
        {
            if (!(motion is BlendTree tree)) return 1;
            return 1 + tree.children.Sum(child => MotionNodeCount(child.motion));
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

        private static void AssertMapEndpoints(
            BlendTree map,
            string outputParameter,
            params (float input, float output)[] expected)
        {
            var children = map.children.OrderBy(child => child.threshold).ToArray();
            Assert.That(children, Has.Length.EqualTo(expected.Length));
            for (var index = 0; index < expected.Length; index++)
            {
                Assert.That(children[index].threshold,
                    Is.EqualTo(expected[index].input).Within(1e-6f));
                Assert.That(children[index].motion, Is.TypeOf<AnimationClip>());
                var clip = (AnimationClip)children[index].motion;
                var binding = AnimationUtility.GetCurveBindings(clip).Single(candidate =>
                    candidate.type == typeof(Animator) &&
                    candidate.propertyName == outputParameter);
                var value = AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0f);
                Assert.That(value, Is.EqualTo(expected[index].output).Within(1e-6f));
            }
        }

        private static Delta Add(params Delta[] values)
        {
            Assert.That(values, Is.Not.Empty);
            var output = Delta.Zero(values[0].vertices.Length);
            foreach (var value in values) AddScaled(output, value, 1f);
            return output;
        }

        private static Delta Scale(Delta value, float scale)
        {
            var output = Delta.Zero(value.vertices.Length);
            AddScaled(output, value, scale);
            return output;
        }

        private static Delta Subtract(Delta left, Delta right)
        {
            return Add(left, Scale(right, -1f));
        }

        private static void AddScaled(Delta target, Delta value, float scale)
        {
            for (var vertex = 0; vertex < target.vertices.Length; vertex++)
            {
                target.vertices[vertex] += value.vertices[vertex] * scale;
                target.normals[vertex] += value.normals[vertex] * scale;
                target.tangents[vertex] += value.tangents[vertex] * scale;
            }
        }

        private static float[] RandomSimplex(System.Random random, int count)
        {
            var values = new float[count];
            var sum = 0f;
            for (var index = 0; index < count; index++)
            {
                values[index] = 0.001f + (float)random.NextDouble();
                sum += values[index];
            }
            for (var index = 0; index < count; index++) values[index] /= sum;
            return values;
        }

        private static Delta WeightedSum(IReadOnlyList<Delta> values, IReadOnlyList<float> weights)
        {
            var output = Delta.Zero(values[0].vertices.Length);
            for (var index = 0; index < values.Count; index++)
                AddScaled(output, values[index], weights[index]);
            return output;
        }

        private static void AssertDeltaEqual(Delta expected, Delta actual, string context)
        {
            Assert.That(actual.vertices, Has.Length.EqualTo(expected.vertices.Length));
            for (var vertex = 0; vertex < expected.vertices.Length; vertex++)
            {
                Assert.That(
                    Vector3.Distance(actual.vertices[vertex], expected.vertices[vertex]),
                    Is.LessThan(ReconstructionTolerance),
                    $"{context} vertex delta {vertex}");
                Assert.That(
                    Vector3.Distance(actual.normals[vertex], expected.normals[vertex]),
                    Is.LessThan(ReconstructionTolerance),
                    $"{context} normal delta {vertex}");
                Assert.That(
                    Vector3.Distance(actual.tangents[vertex], expected.tangents[vertex]),
                    Is.LessThan(ReconstructionTolerance),
                    $"{context} tangent delta {vertex}");
            }
        }

        private static void AssertOrthogonalToBasis(
            Delta stable,
            Delta positiveBasis,
            Delta negativeBasis,
            string context)
        {
            Assert.That(
                Math.Abs(Dot(stable.vertices, positiveBasis.vertices)),
                Is.LessThan(1e-8d),
                context + " has a positive-ray vertex projection.");
            Assert.That(
                Math.Abs(Dot(stable.vertices, negativeBasis.vertices)),
                Is.LessThan(1e-8d),
                context + " has a negative-ray vertex projection.");
        }

        private static double Dot(IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right)
        {
            var value = 0d;
            for (var index = 0; index < left.Count; index++)
                value += Vector3.Dot(left[index], right[index]);
            return value;
        }

        private static double SquaredMagnitude(IReadOnlyList<Vector3> values)
        {
            var value = 0d;
            for (var index = 0; index < values.Count; index++)
                value += values[index].sqrMagnitude;
            return value;
        }

        private static MeshSnapshot Snapshot(Mesh mesh)
        {
            var shapes = new List<ShapeSnapshot>();
            for (var shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                var frames = new List<FrameSnapshot>();
                for (var frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    frames.Add(new FrameSnapshot(
                        mesh.GetBlendShapeFrameWeight(shape, frame),
                        ReadDelta(mesh, shape, frame)));
                }
                shapes.Add(new ShapeSnapshot(mesh.GetBlendShapeName(shape), frames));
            }
            return new MeshSnapshot(shapes);
        }

        private static void AssertSourceUnchanged(Mesh source, MeshSnapshot expected)
        {
            Assert.That(source.blendShapeCount, Is.EqualTo(expected.shapes.Count));
            Assert.That(source.GetBlendShapeIndex("YUCP_AVR_Residual_sil"), Is.EqualTo(-1));
            for (var shape = 0; shape < expected.shapes.Count; shape++)
            {
                var expectedShape = expected.shapes[shape];
                Assert.That(source.GetBlendShapeName(shape), Is.EqualTo(expectedShape.name));
                Assert.That(source.GetBlendShapeFrameCount(shape), Is.EqualTo(expectedShape.frames.Count));
                for (var frame = 0; frame < expectedShape.frames.Count; frame++)
                {
                    Assert.That(
                        source.GetBlendShapeFrameWeight(shape, frame),
                        Is.EqualTo(expectedShape.frames[frame].weight).Within(1e-7f));
                    AssertDeltaEqual(
                        expectedShape.frames[frame].delta,
                        ReadDelta(source, shape, frame),
                        $"unchanged source {expectedShape.name} frame {frame}");
                }
            }
        }

        private sealed class MeshSnapshot
        {
            public readonly IReadOnlyList<ShapeSnapshot> shapes;

            public MeshSnapshot(IReadOnlyList<ShapeSnapshot> shapes)
            {
                this.shapes = shapes;
            }
        }

        private sealed class ShapeSnapshot
        {
            public readonly string name;
            public readonly IReadOnlyList<FrameSnapshot> frames;

            public ShapeSnapshot(string name, IReadOnlyList<FrameSnapshot> frames)
            {
                this.name = name;
                this.frames = frames;
            }
        }

        private readonly struct FrameSnapshot
        {
            public readonly float weight;
            public readonly Delta delta;

            public FrameSnapshot(float weight, Delta delta)
            {
                this.weight = weight;
                this.delta = delta;
            }
        }

        private sealed class Delta
        {
            public readonly Vector3[] vertices;
            public readonly Vector3[] normals;
            public readonly Vector3[] tangents;

            private Delta(int vertexCount)
            {
                vertices = new Vector3[vertexCount];
                normals = new Vector3[vertexCount];
                tangents = new Vector3[vertexCount];
            }

            public static Delta Zero(int vertexCount)
            {
                return new Delta(vertexCount);
            }

            public static Delta WithSingleVertex(
                int vertexCount,
                int vertex,
                Vector3 vertexDelta,
                Vector3 normalDelta,
                Vector3 tangentDelta)
            {
                var value = Zero(vertexCount);
                value.vertices[vertex] = vertexDelta;
                value.normals[vertex] = normalDelta;
                value.tangents[vertex] = tangentDelta;
                return value;
            }
        }
    }
}
#endif
