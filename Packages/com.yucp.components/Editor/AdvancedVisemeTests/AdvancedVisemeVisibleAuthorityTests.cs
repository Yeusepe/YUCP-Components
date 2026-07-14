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
    /// <summary>
    /// Regression contract for visible lower-face ownership. A calibrated
    /// residual may retain authored geometry outside the measured tracking
    /// basis, while an unsafe coupled fallback still yields wherever it cannot
    /// separate that geometry from a locally measured coordinate.
    /// </summary>
    public sealed class AdvancedVisemeVisibleAuthorityTests
    {
        private static readonly AdvancedVisemeArticulator[] VisibleChannels =
        {
            AdvancedVisemeArticulator.JawOpen,
            AdvancedVisemeArticulator.MouthOpen,
            AdvancedVisemeArticulator.LipClose,
            AdvancedVisemeArticulator.LipFunnel,
            AdvancedVisemeArticulator.LipPucker,
            AdvancedVisemeArticulator.LipSuck
        };

        private static readonly int[] SilenceAndVowels = { 0, 10, 11, 12, 13, 14 };

        [Test]
        public void ActiveLocalTrackingHeldFixedIsInvariantAcrossHardVowels()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            var tracked = new Dictionary<AdvancedVisemeArticulator, float>
            {
                [AdvancedVisemeArticulator.JawOpen] = 0.83f,
                [AdvancedVisemeArticulator.MouthOpen] = 0.67f,
                [AdvancedVisemeArticulator.LipClose] = 0.12f,
                [AdvancedVisemeArticulator.LipFunnel] = 0.29f,
                [AdvancedVisemeArticulator.LipPucker] = 0.38f,
                [AdvancedVisemeArticulator.LipSuck] = 0.17f
            };

            try
            {
                foreach (var articulator in VisibleChannels)
                {
                    var binding = profile.FindBinding(articulator);
                    Assert.That(binding, Is.Not.Null, articulator.ToString());
                    Assert.That(binding.localReliability, Is.EqualTo(1f).Within(1e-6f),
                        $"A default local {articulator} measurement must own its visible coordinate.");

                    float? previous = null;
                    foreach (var viseme in SilenceAndVowels)
                    {
                        var speech = profile.visemePoses[viseme].Get(articulator);
                        var authority = AdvancedVisemeMath.TrackingAuthority(
                            speech, tracked[articulator], true,
                            binding.localReliability, binding.remoteReliability);
                        var output = AdvancedVisemeMath.Fuse(
                            speech, tracked[articulator], authority);

                        Assert.That(output, Is.EqualTo(tracked[articulator]).Within(1e-6f),
                            $"Hard {VisemeReconstructionProfile.VisemeNames[viseme]} moved tracked {articulator}.");
                        if (previous.HasValue)
                            Assert.That(output, Is.EqualTo(previous.Value).Within(1e-6f),
                                $"Changing the Oculus vowel moved fixed local {articulator} tracking.");
                        previous = output;
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void TrackerLossFadesFromMeasuredVowelPoseWithoutASnap()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                const float deltaTime = 1f / 90f;
                var alpha = AdvancedVisemeMath.Alpha(
                    deltaTime, profile.trackingBlendResponseSeconds);

                foreach (var articulator in VisibleChannels)
                {
                    var speech = profile.visemePoses[10].Get(articulator); // hard aa
                    var tracking = speech < 0.5f
                        ? Mathf.Min(1f, speech + 0.35f)
                        : Mathf.Max(0f, speech - 0.35f);
                    var authority = AdvancedVisemeMath.TrackingAuthority(
                        speech, tracking, true, 1f, 0.85f);
                    var trackingBlend = 1f;
                    var previous = AdvancedVisemeMath.Fuse(
                        speech, tracking, trackingBlend * authority);
                    var previousDistanceToSpeech = Mathf.Abs(previous - speech);

                    Assert.That(previous, Is.EqualTo(tracking).Within(1e-6f));
                    for (var frame = 0; frame < 240; frame++)
                    {
                        trackingBlend += alpha * (0f - trackingBlend);
                        var output = AdvancedVisemeMath.Fuse(
                            speech, tracking, trackingBlend * authority);
                        var distanceToSpeech = Mathf.Abs(output - speech);

                        Assert.That(output, Is.InRange(
                            Mathf.Min(speech, tracking) - 1e-6f,
                            Mathf.Max(speech, tracking) + 1e-6f), articulator.ToString());
                        Assert.That(distanceToSpeech,
                            Is.LessThanOrEqualTo(previousDistanceToSpeech + 1e-6f),
                            $"{articulator} moved away from its speech fallback during tracker loss.");
                        previous = output;
                        previousDistanceToSpeech = distanceToSpeech;
                    }

                    var firstLostFrameBlend = 1f + alpha * (0f - 1f);
                    var firstLostFrame = AdvancedVisemeMath.Fuse(
                        speech, tracking, firstLostFrameBlend * authority);
                    Assert.That(Mathf.Abs(firstLostFrame - speech), Is.GreaterThan(0.05f),
                        $"{articulator} snapped to speech on the first inactive frame.");
                    Assert.That(previous, Is.EqualTo(speech).Within(1e-5f),
                        $"{articulator} did not eventually release to speech.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void RemoteTrackingMayRetainAConservativeSpeechPriorNearAgreement()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                foreach (var articulator in VisibleChannels)
                {
                    var binding = profile.FindBinding(articulator);
                    Assert.That(binding, Is.Not.Null, articulator.ToString());
                    Assert.That(binding.remoteReliability, Is.LessThan(1f), articulator.ToString());

                    const float speech = 0.4f;
                    const float tracking = 0.405f;
                    var localAuthority = AdvancedVisemeMath.TrackingAuthority(
                        speech, tracking, true,
                        binding.localReliability, binding.remoteReliability);
                    var remoteAuthority = AdvancedVisemeMath.TrackingAuthority(
                        speech, tracking, false,
                        binding.localReliability, binding.remoteReliability);
                    var localOutput = AdvancedVisemeMath.Fuse(speech, tracking, localAuthority);
                    var remoteOutput = AdvancedVisemeMath.Fuse(speech, tracking, remoteAuthority);

                    Assert.That(localOutput, Is.EqualTo(tracking).Within(1e-6f), articulator.ToString());
                    Assert.That(remoteOutput, Is.GreaterThan(speech), articulator.ToString());
                    Assert.That(remoteOutput, Is.LessThan(tracking),
                        $"Remote {articulator} should be allowed to retain a small stabilizing prior.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void UncalibratedCoupledPoseUsesComplementaryRemainderWithoutAResidualBasis()
        {
            // This helper belongs to the conservative direct-pose fallback. When
            // no V=UC+R decomposition exists, a coupled pose must yield rather
            // than risk moving a coordinate already owned by local tracking.
            Assert.That(AdvancedVisemeMath.VisibleSpeechRemainder(1f),
                Is.Zero.Within(1e-6f));

            // Missing/remote/conservative measurements retain only their exact
            // complementary share. This also makes tracker loss a continuous
            // reveal rather than a hard switch.
            Assert.That(AdvancedVisemeMath.VisibleSpeechRemainder(0f),
                Is.EqualTo(1f).Within(1e-6f));
            Assert.That(AdvancedVisemeMath.VisibleSpeechRemainder(0.4f),
                Is.EqualTo(0.6f).Within(1e-6f));
            Assert.That(AdvancedVisemeMath.VisibleSpeechRemainder(0.85f),
                Is.EqualTo(0.15f).Within(1e-6f));
        }

        [Test]
        public void LocallyMeasuredTargetsSuppressConstraintsButMissingOrRemoteTargetsRetainThem()
        {
            var measuredLocal = AdvancedVisemeMath.PhoneticConstraintRemainder(1f, 1f);
            var missingLocal = AdvancedVisemeMath.PhoneticConstraintRemainder(1f, 0f);
            var measuredRemote = AdvancedVisemeMath.PhoneticConstraintRemainder(0f, 1f);

            Assert.That(measuredLocal, Is.Zero.Within(1e-6f));
            Assert.That(missingLocal, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(measuredRemote, Is.EqualTo(1f).Within(1e-6f));

            foreach (var viseme in new[] { 1, 2, 6, 7 })
            {
                var baseline = ConstraintStressPose();
                var constrained = baseline;
                ApplyConstraintForHardViseme(viseme, measuredLocal, ref constrained);
                AssertPoseEqual(baseline, constrained,
                    $"A local measured target must win over {VisemeReconstructionProfile.VisemeNames[viseme]}.");

                constrained = baseline;
                ApplyConstraintForHardViseme(viseme, missingLocal, ref constrained);
                Assert.That(ConstraintTargetChanged(viseme, baseline, constrained), Is.True,
                    $"A missing target still needs the {VisemeReconstructionProfile.VisemeNames[viseme]} constraint.");

                constrained = baseline;
                ApplyConstraintForHardViseme(viseme, measuredRemote, ref constrained);
                Assert.That(ConstraintTargetChanged(viseme, baseline, constrained), Is.True,
                    $"Remote stabilization may retain the {VisemeReconstructionProfile.VisemeNames[viseme]} constraint.");
            }
        }

        [Test]
        public void GeneratedUncalibratedPartialReuseGraphContainsLocalGeometryPrecedenceAndConstraintGates()
        {
            var root = new GameObject("Visible Authority Graph Test");
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            var folderName = "__YUCP_AVR_VisibleAuthority_" + Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);

            try
            {
                // LipBite is normally an inferred channel. Give this synthetic
                // tailored template an explicit measurement so every sparse
                // constraint target can be checked independently.
                profile.FindBinding(AdvancedVisemeArticulator.LipBite).trackingParameter = "LipBite";

                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.mouthOwnership = AdvancedVisemeMouthOwnership.DriveLowerFace;
                component.trackingInputs = AdvancedVisemeTrackingInputs.ReuseExisting;
                component.fusionMode = AdvancedVisemeFusionMode.PhoneticAssist;

                var measured = new Dictionary<AdvancedVisemeArticulator, string>
                {
                    [AdvancedVisemeArticulator.JawOpen] = "Tailored/v2/JawOpen",
                    [AdvancedVisemeArticulator.LipClose] = "Tailored/v2/MouthClosed",
                    [AdvancedVisemeArticulator.LipBite] = "Tailored/v2/LipBite"
                };
                var result = AdvancedVisemeAnimatorBuilder.Build(
                    new AdvancedVisemeAnimatorBuilder.Request
                    {
                        controllerPath = folder + "/AdvancedViseme.controller",
                        parametersPath = folder + "/TrackingParameters.asset",
                        component = component,
                        profile = profile,
                        trackingPrefix = "Tailored",
                        effectiveTrackingInputs = AdvancedVisemeTrackingInputs.Balanced8,
                        reuseExistingTracking = true,
                        trackingActiveParameter = "Tailored/v2/LipTrackingActive",
                        trackingActiveAnimatorType = AnimatorControllerParameterType.Float,
                        trackingActiveDefault = 1f,
                        trackingParameterNames = measured,
                        sourceVisemeBlendShapes = VisemeReconstructionProfile.VisemeNames
                            .Select(name => "vrc.v_" + name).ToArray(),
                        calibrationBasis = Array.Empty<AdvancedVisemeMeshCalibrator.BasisInput>(),
                        resolvedBlendShapes = measured.Keys.ToDictionary(
                            articulator => articulator, articulator => articulator.ToString()),
                        externalPoses = new Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose>(),
                        trackingEnabled = true,
                        existingExpressionParameters = new HashSet<string>()
                    });

                var parameters = result.controller.parameters.Select(parameter => parameter.name).ToArray();
                Assert.That(parameters.Any(name => name.EndsWith(
                    "/Viseme/10/VisibleSuppression", StringComparison.Ordinal)), Is.True,
                    "Unsafe direct aa geometry needs a per-viseme measurement suppression term.");
                Assert.That(parameters.Any(name => name.EndsWith(
                    "/Viseme/10/VisibleSpeechWeight", StringComparison.Ordinal)), Is.True,
                    "The uncalibrated aa fallback needs a complementary visible speech weight.");

                foreach (var stage in new[] { "Fast", "Slow" })
                foreach (var articulator in new[]
                         {
                             AdvancedVisemeArticulator.LipClose,
                             AdvancedVisemeArticulator.LipBite,
                             AdvancedVisemeArticulator.JawOpen
                         })
                {
                    var suffix = $"/Constraint/{stage}/{articulator}/MeasurementRemainder";
                    Assert.That(parameters.Any(name => name.EndsWith(suffix, StringComparison.Ordinal)), Is.True,
                        $"{stage} {articulator} constraint lacks a per-target local measurement gate.");
                }

                var trees = AssetDatabase.LoadAllAssetsAtPath(folder + "/AdvancedViseme.controller")
                    .OfType<BlendTree>().ToArray();
                Assert.That(trees.Any(tree => tree.name.Contains("Viseme/10/VisibleSuppression")), Is.True,
                    "Unsafe per-viseme suppression must participate in generated math, not merely exist as an unused parameter.");
                Assert.That(trees.Any(tree => tree.name.Contains("Viseme/10/VisibleSpeechWeight")), Is.True,
                    "Uncalibrated direct aa fallback geometry must consume the complementary visible speech weight.");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PhoneticAssistOnlyProjectsBilabialLabiodentalAndSibilantCoordinates()
        {
            var baseline = ConstraintStressPose();

            for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
            {
                var constrained = baseline;
                ApplyConstraintForHardViseme(viseme, 1f, ref constrained);

                switch (viseme)
                {
                    case 1: // PP: mandatory bilabial closure only.
                        AssertOnlyChanged(baseline, constrained, nameof(VisiblePose.lipClose));
                        Assert.That(constrained.lipClose, Is.GreaterThanOrEqualTo(0.9f));
                        break;
                    case 2: // FF: mandatory labiodental bite only.
                        AssertOnlyChanged(baseline, constrained, nameof(VisiblePose.lipBite));
                        Assert.That(constrained.lipBite, Is.GreaterThanOrEqualTo(0.85f));
                        break;
                    case 6: // CH
                    case 7: // SS: sibilant jaw ceiling only.
                        AssertOnlyChanged(baseline, constrained, nameof(VisiblePose.jawOpen));
                        Assert.That(constrained.jawOpen, Is.LessThanOrEqualTo(0.22f + 1e-6f));
                        break;
                    default:
                        AssertPoseEqual(baseline, constrained,
                            $"{VisemeReconstructionProfile.VisemeNames[viseme]} is not a visible constraint.");
                        break;
                }
            }
        }

        private static VisiblePose ConstraintStressPose()
        {
            return new VisiblePose
            {
                jawOpen = 0.8f,
                lipClose = 0.1f,
                mouthOpen = 0.62f,
                lipFunnel = 0.31f,
                lipPucker = 0.44f,
                lipSuck = 0.16f,
                lipBite = 0.05f
            };
        }

        private static void ApplyConstraintForHardViseme(
            int viseme,
            float measurementRemainder,
            ref VisiblePose pose)
        {
            AdvancedVisemeMath.ApplyPhoneticConstraints(
                viseme == 1 ? measurementRemainder : 0f,
                viseme == 2 ? measurementRemainder : 0f,
                viseme == 7 ? measurementRemainder : 0f,
                viseme == 6 ? measurementRemainder : 0f,
                0.9f,
                0.85f,
                0.22f,
                ref pose.jawOpen,
                ref pose.lipClose,
                ref pose.lipBite);
        }

        private static bool ConstraintTargetChanged(
            int viseme,
            VisiblePose before,
            VisiblePose after)
        {
            if (viseme == 1) return Mathf.Abs(after.lipClose - before.lipClose) > 1e-6f;
            if (viseme == 2) return Mathf.Abs(after.lipBite - before.lipBite) > 1e-6f;
            return Mathf.Abs(after.jawOpen - before.jawOpen) > 1e-6f;
        }

        private static void AssertOnlyChanged(VisiblePose expected, VisiblePose actual, string allowed)
        {
            foreach (var field in typeof(VisiblePose).GetFields())
            {
                if (field.Name == allowed) continue;
                Assert.That((float)field.GetValue(actual),
                    Is.EqualTo((float)field.GetValue(expected)).Within(1e-6f),
                    $"Sparse constraint unexpectedly changed {field.Name}.");
            }
        }

        private static void AssertPoseEqual(VisiblePose expected, VisiblePose actual, string message)
        {
            foreach (var field in typeof(VisiblePose).GetFields())
                Assert.That((float)field.GetValue(actual),
                    Is.EqualTo((float)field.GetValue(expected)).Within(1e-6f),
                    message + " Channel: " + field.Name);
        }

        private struct VisiblePose
        {
            public float jawOpen;
            public float lipClose;
            public float mouthOpen;
            public float lipFunnel;
            public float lipPucker;
            public float lipSuck;
            public float lipBite;
        }
    }
}
