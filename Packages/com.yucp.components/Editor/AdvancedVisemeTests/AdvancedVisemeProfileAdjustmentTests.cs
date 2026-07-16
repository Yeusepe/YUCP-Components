#if UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.Tests
{
    public sealed class AdvancedVisemeProfileAdjustmentTests
    {
        private const int RrViseme = 9;
        private const int AaViseme = 10;

        [Test]
        public void NewProfileStartsWithNeutralPerVisemeAdjustments()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                Assert.That(profile.visemeAdjustments, Has.Length.EqualTo(
                    VisemeReconstructionProfile.VisemeCount));
                Assert.That(profile.HasNonNeutralVisemeAdjustments(), Is.False);

                for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
                {
                    Assert.That(profile.GetVisemeAdjustment(viseme), Is.Not.Null,
                        VisemeReconstructionProfile.VisemeNames[viseme]);
                    foreach (AdvancedVisemeArticulator articulator in
                             Enum.GetValues(typeof(AdvancedVisemeArticulator)))
                    {
                        Assert.That(profile.GetVisemeArticulationMultiplier(viseme, articulator),
                            Is.EqualTo(1f).Within(1e-6f),
                            $"{VisemeReconstructionProfile.VisemeNames[viseme]}/{articulator}");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void VersionNineMigrationCreatesNeutralAdjustmentsAndDoesNotRewriteThemLater()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                profile.visemeAdjustments = null;
                var serialized = new SerializedObject(profile);
                serialized.FindProperty("defaultsVersion").intValue = 8;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                profile.EnsureDefaults();

                Assert.That(profile.visemeAdjustments, Has.Length.EqualTo(
                    VisemeReconstructionProfile.VisemeCount));
                Assert.That(profile.HasNonNeutralVisemeAdjustments(), Is.False,
                    "A legacy profile must not lose speech when the trim model is introduced.");

                profile.GetVisemeAdjustment(RrViseme)
                    .Set(AdvancedVisemeArticulator.JawOpen, 0.4f);
                profile.EnsureDefaults();

                Assert.That(profile.GetVisemeArticulationMultiplier(
                        RrViseme, AdvancedVisemeArticulator.JawOpen),
                    Is.EqualTo(0.4f).Within(1e-6f),
                    "Once migrated, validation must preserve the creator's RR trim.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void RrJawTrimChangesOnlyRrJawOpenCoefficient()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                profile.visemePoses[RrViseme].jawOpen = 0.8f;
                profile.visemePoses[RrViseme].tongueOut = 0.65f;
                profile.visemePoses[AaViseme].jawOpen = 0.7f;

                var request = new AdvancedVisemeAnimatorBuilder.Request { profile = profile };
                var authoredJaw = AdvancedVisemeAnimatorBuilder.GetAuthoredSpeechCoefficients(
                    request, AdvancedVisemeArticulator.JawOpen);
                var authoredTongue = AdvancedVisemeAnimatorBuilder.GetAuthoredSpeechCoefficients(
                    request, AdvancedVisemeArticulator.TongueOut);

                profile.GetVisemeAdjustment(RrViseme)
                    .Set(AdvancedVisemeArticulator.JawOpen, 0.25f);

                var adjustedJaw = AdvancedVisemeAnimatorBuilder.GetAdjustedSpeechCoefficients(
                    request, AdvancedVisemeArticulator.JawOpen);
                var adjustedTongue = AdvancedVisemeAnimatorBuilder.GetAdjustedSpeechCoefficients(
                    request, AdvancedVisemeArticulator.TongueOut);

                Assert.That(adjustedJaw[RrViseme],
                    Is.EqualTo(authoredJaw[RrViseme] * 0.25f).Within(1e-6f));
                Assert.That(adjustedJaw[AaViseme],
                    Is.EqualTo(authoredJaw[AaViseme]).Within(1e-6f),
                    "Trimming RR must not reduce the open vowel.");

                for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
                {
                    if (viseme != RrViseme)
                        Assert.That(adjustedJaw[viseme],
                            Is.EqualTo(authoredJaw[viseme]).Within(1e-6f),
                            "Only RR Jaw Open may change.");
                    Assert.That(adjustedTongue[viseme],
                        Is.EqualTo(authoredTongue[viseme]).Within(1e-6f),
                        "A jaw trim must not reduce any tongue coefficient.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void PerVisemeTrimEnablesFallbackCorrectionOnlyWhileNonNeutral()
        {
            var root = new GameObject("Per-viseme fallback correction test");
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.reconstructionMode = AdvancedVisemeReconstructionMode.Normal;
                component.createTuningMenu = false;
                var request = new AdvancedVisemeAnimatorBuilder.Request
                {
                    component = component,
                    profile = profile,
                    trackingEnabled = false
                };

                Assert.That(AdvancedVisemeAnimatorBuilder
                    .ShouldBuildFallbackArticulationCorrection(request), Is.False);

                profile.GetVisemeAdjustment(RrViseme)
                    .Set(AdvancedVisemeArticulator.JawOpen, 0.5f);
                Assert.That(AdvancedVisemeAnimatorBuilder
                    .ShouldBuildFallbackArticulationCorrection(request), Is.True,
                    "Direct-viseme fallback needs a correction graph for a profile trim.");

                profile.ResetVisemeAdjustment(RrViseme);
                Assert.That(AdvancedVisemeAnimatorBuilder
                    .ShouldBuildFallbackArticulationCorrection(request), Is.False,
                    "Resetting RR should restore the zero-cost neutral fallback path.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
#endif
