using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace YUCP.Components.Editor.Tests
{
    /// <summary>
    /// Contract for user-facing Advanced Viseme tuning. Tuning is allowed to
    /// reshape the speech prior, but it must never weaken an active local
    /// measurement or add network cost unless that is explicitly designed.
    /// </summary>
    public sealed class AdvancedVisemeTuningTests
    {
        private static readonly AdvancedVisemeTuningMenuSections[] Sections =
        {
            AdvancedVisemeTuningMenuSections.Speech,
            AdvancedVisemeTuningMenuSections.Tracking,
            AdvancedVisemeTuningMenuSections.Phonetics,
            AdvancedVisemeTuningMenuSections.Tongue
        };

        private static readonly AdvancedVisemeArticulator[] VisibleArticulators =
        {
            AdvancedVisemeArticulator.JawOpen,
            AdvancedVisemeArticulator.LipClose,
            AdvancedVisemeArticulator.MouthOpen,
            AdvancedVisemeArticulator.LipFunnel,
            AdvancedVisemeArticulator.LipPucker,
            AdvancedVisemeArticulator.LipSuck,
            AdvancedVisemeArticulator.SmileSad,
            AdvancedVisemeArticulator.LipBite,
            AdvancedVisemeArticulator.JawX,
            AdvancedVisemeArticulator.JawZ,
            AdvancedVisemeArticulator.MouthX
        };

        private static readonly AdvancedVisemeArticulator[] TongueArticulators =
        {
            AdvancedVisemeArticulator.TongueOut,
            AdvancedVisemeArticulator.TongueY,
            AdvancedVisemeArticulator.TongueX,
            AdvancedVisemeArticulator.TongueRoll,
            AdvancedVisemeArticulator.TongueArchY,
            AdvancedVisemeArticulator.TongueShape,
            AdvancedVisemeArticulator.TongueTwistRight,
            AdvancedVisemeArticulator.TongueTwistLeft
        };

        private static readonly string[] VersionSixUnitFields =
        {
            nameof(VisemeReconstructionProfile.speechMotionStrength),
            nameof(VisemeReconstructionProfile.authoredResidualDetail),
            nameof(VisemeReconstructionProfile.remoteTrackingTrust),
            nameof(VisemeReconstructionProfile.phoneticConstraintStrength),
            nameof(VisemeReconstructionProfile.bilabialAssistStrength),
            nameof(VisemeReconstructionProfile.labiodentalAssistStrength),
            nameof(VisemeReconstructionProfile.sibilantAssistStrength),
            nameof(VisemeReconstructionProfile.hiddenPhoneStrength),
            nameof(VisemeReconstructionProfile.hiddenDetailStrength),
            nameof(VisemeReconstructionProfile.tongueInferenceStrength),
            nameof(VisemeReconstructionProfile.tongueOutStrength),
            nameof(VisemeReconstructionProfile.tongueYStrength),
            nameof(VisemeReconstructionProfile.tongueXStrength),
            nameof(VisemeReconstructionProfile.tongueRollStrength),
            nameof(VisemeReconstructionProfile.tongueArchStrength),
            nameof(VisemeReconstructionProfile.tongueShapeStrength),
            nameof(VisemeReconstructionProfile.tongueTwistStrength)
        };

        [Test]
        public void VersionElevenProfileDefaultsUseLearnedSmoothTrajectoryObserver()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                Assert.That(profile.visemeResponseSeconds, Is.EqualTo(0.017f).Within(1e-6f));
                Assert.That(profile.speechHangoverSeconds, Is.EqualTo(0.16f).Within(1e-6f));
                Assert.That(profile.localTrackingResponseSeconds, Is.EqualTo(0.018f).Within(1e-6f));
                Assert.That(profile.remoteTrackingResponseSeconds, Is.EqualTo(0.065f).Within(1e-6f));
                Assert.That(profile.trackingAcquireResponseSeconds, Is.EqualTo(0.035f).Within(1e-6f));
                Assert.That(profile.trackingBlendResponseSeconds, Is.EqualTo(0.12f).Within(1e-6f));
                Assert.That(profile.quietSpeechFloor, Is.EqualTo(0.55f).Within(1e-6f));
                Assert.That(profile.voiceNoiseFloor, Is.EqualTo(0.05f).Within(1e-6f));
                Assert.That(profile.voiceFullScale, Is.EqualTo(0.25f).Within(1e-6f));
                Assert.That(profile.BetaCoarticulationStrength, Is.EqualTo(1f).Within(1e-6f));
                Assert.That(profile.speechLiveliness, Is.Zero.Within(1e-6f));

                foreach (var fieldName in VersionSixUnitFields)
                {
                    Assert.That(UnitField(profile, fieldName), Is.EqualTo(1f).Within(1e-6f),
                        fieldName + " must be a neutral multiplier in migrated/default profiles.");
                }

                Assert.That(profile.bilabialClosure, Is.EqualTo(0.9f).Within(1e-6f));
                Assert.That(profile.labiodentalBite, Is.EqualTo(0.85f).Within(1e-6f));
                Assert.That(profile.sibilantJawMaximum, Is.EqualTo(0.22f).Within(1e-6f));
                Assert.That(profile.residualMismatchFade, Is.EqualTo(1f).Within(1e-6f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void VersionSevenMigrationInitializesSilenceStabilityOnlyOnce()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                SetPrivateInt(profile, "defaultsVersion", 6);
                profile.speechHangoverSeconds = 0f;
                profile.EnsureDefaults();
                Assert.That(profile.speechHangoverSeconds, Is.EqualTo(0.16f).Within(1e-6f));

                profile.speechHangoverSeconds = 0.07f;
                profile.EnsureDefaults();
                Assert.That(profile.speechHangoverSeconds, Is.EqualTo(0.07f).Within(1e-6f),
                    "Migration must not overwrite a deliberate post-upgrade preference.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void VersionElevenMigrationUpgradesFormerRecommendedPairOnlyOnce()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                SetPrivateInt(profile, "defaultsVersion", 10);
                profile.visemeResponseSeconds = 0.024f;
                profile.speechLiveliness = 1f;
                profile.EnsureDefaults();
                Assert.That(profile.visemeResponseSeconds,
                    Is.EqualTo(0.017f).Within(1e-6f));
                Assert.That(profile.speechLiveliness, Is.Zero.Within(1e-6f));

                profile.visemeResponseSeconds = 0.024f;
                profile.speechLiveliness = 1f;
                profile.EnsureDefaults();
                Assert.That(profile.visemeResponseSeconds,
                    Is.EqualTo(0.024f).Within(1e-6f));
                Assert.That(profile.speechLiveliness, Is.EqualTo(1f).Within(1e-6f),
                    "The former pair can be a deliberate preference after migration.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void VersionTenMigrationPreservesCustomSpeechLiveliness()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                SetPrivateInt(profile, "defaultsVersion", 9);
                profile.speechLiveliness = 0.37f;
                profile.EnsureDefaults();
                Assert.That(profile.speechLiveliness, Is.EqualTo(0.37f).Within(1e-6f),
                    "Version 10 may upgrade only the former recommended midpoint.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void VersionSixMigrationInitializesMissingFieldsOnlyOnce()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                foreach (var fieldName in VersionSixUnitFields) SetUnitField(profile, fieldName, 0f);
                SetPrivateInt(profile, "defaultsVersion", 5);
                profile.trackingAcquireResponseSeconds = 0f;
                profile.EnsureDefaults();

                Assert.That(profile.trackingAcquireResponseSeconds, Is.EqualTo(0.035f).Within(1e-6f));
                foreach (var fieldName in VersionSixUnitFields)
                    Assert.That(UnitField(profile, fieldName), Is.EqualTo(1f).Within(1e-6f), fieldName);

                // Once migrated, zero is a deliberate user setting and must not
                // be silently restored on every EnsureDefaults call.
                profile.speechMotionStrength = 0f;
                profile.tongueInferenceStrength = 0f;
                profile.EnsureDefaults();
                Assert.That(profile.speechMotionStrength, Is.Zero.Within(1e-6f));
                Assert.That(profile.tongueInferenceStrength, Is.Zero.Within(1e-6f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ProfileValidationClampsEveryVersionSevenPreference()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                profile.trackingAcquireResponseSeconds = 2f;
                profile.speechHangoverSeconds = 2f;
                profile.speechLiveliness = 3f;
                for (var index = 0; index < VersionSixUnitFields.Length; index++)
                    SetUnitField(profile, VersionSixUnitFields[index], index % 2 == 0 ? -2f : 3f);

                InvokeOnValidate(profile);

                Assert.That(profile.trackingAcquireResponseSeconds, Is.InRange(0.005f, 0.1f));
                Assert.That(profile.speechHangoverSeconds, Is.InRange(0.04f, 0.4f));
                Assert.That(profile.speechLiveliness, Is.EqualTo(1f).Within(1e-6f));
                foreach (var fieldName in VersionSixUnitFields)
                    Assert.That(UnitField(profile, fieldName), Is.InRange(0f, 1f), fieldName);

                profile.speechHangoverSeconds = -2f;
                InvokeOnValidate(profile);
                Assert.That(profile.speechHangoverSeconds, Is.EqualTo(0.04f).Within(1e-6f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void TuningMetadataIsCompleteUniqueAndFitsVrchatMenus()
        {
            var enumValues = ((AdvancedVisemeTuningControl[])Enum.GetValues(
                typeof(AdvancedVisemeTuningControl))).ToArray();
            Assert.That(AdvancedVisemeTuning.Controls, Is.EquivalentTo(enumValues));
            Assert.That(AdvancedVisemeTuning.Controls.Distinct().Count(), Is.EqualTo(enumValues.Length));
            Assert.That(AdvancedVisemeTuning.Controls.Select(AdvancedVisemeTuning.ParameterSuffix),
                Is.Unique);

            foreach (var control in AdvancedVisemeTuning.Controls)
            {
                Assert.That(AdvancedVisemeTuning.Label(control), Is.Not.Null.And.Not.Empty, control.ToString());
                Assert.That(AdvancedVisemeTuning.ParameterSuffix(control),
                    Is.Not.Null.And.Not.Empty, control.ToString());
                Assert.That(Sections, Does.Contain(AdvancedVisemeTuning.Section(control)),
                    control.ToString());
                Assert.That(AdvancedVisemeTuning.DefaultValue(null, control),
                    Is.InRange(0f, 1f), control.ToString());
            }

            foreach (var section in Sections)
            {
                var count = AdvancedVisemeTuning.Controls.Count(control =>
                    AdvancedVisemeTuning.Section(control) == section);
                Assert.That(count, Is.InRange(1, 8),
                    $"{AdvancedVisemeTuning.SectionLabel(section)} contains {count} controls; VRChat allows eight.");
            }
            Assert.That(AdvancedVisemeTuning.Controls.Count(control =>
                    AdvancedVisemeTuning.Section(control) == AdvancedVisemeTuningMenuSections.Speech),
                Is.EqualTo(8), "Speech must fit exactly in VRChat's eight-control menu.");

            Assert.That(AdvancedVisemeTuning.SimpleControls.Count,
                Is.InRange(1, AdvancedVisemeRuntimeMenuBuilder.MaxControlsPerMenu));
            Assert.That(AdvancedVisemeTuning.SimpleControls, Is.Unique);
            Assert.That(AdvancedVisemeTuning.SimpleControls,
                Has.All.Matches<AdvancedVisemeTuningControl>(control =>
                    AdvancedVisemeTuning.Controls.Contains(control)));
            Assert.That(AdvancedVisemeTuning.SimpleControls,
                Does.Contain(AdvancedVisemeTuningControl.SpeechMotion));
            Assert.That(AdvancedVisemeTuning.SimpleControls,
                Does.Contain(AdvancedVisemeTuningControl.SpeechLiveliness));
            Assert.That(AdvancedVisemeTuning.SimpleControls.Contains(
                    AdvancedVisemeTuningControl.Coarticulation),
                Is.False,
                "Natural Transitions remains in Advanced because the Simple inspector already exposes its mode toggle.");
            Assert.That(AdvancedVisemeTuning.SimpleControls
                .Select(AdvancedVisemeTuning.SimpleLabel), Is.Unique);
        }

        [Test]
        public void CompactSyncUsesOneByteAndTheMinimumIndexBits()
        {
            Assert.That(AdvancedVisemeTuning.CompactSyncIndexBits(0), Is.Zero);
            Assert.That(AdvancedVisemeTuning.CompactSyncBits(0), Is.Zero);

            Assert.That(AdvancedVisemeTuning.CompactSyncIndexBits(1), Is.EqualTo(1));
            Assert.That(AdvancedVisemeTuning.CompactSyncBits(1), Is.EqualTo(9));
            Assert.That(AdvancedVisemeTuning.CompactSyncIndexBits(7), Is.EqualTo(3));
            Assert.That(AdvancedVisemeTuning.CompactSyncBits(7), Is.EqualTo(11));
            Assert.That(AdvancedVisemeTuning.CompactSyncIndexBits(8), Is.EqualTo(4));
            Assert.That(AdvancedVisemeTuning.CompactSyncBits(8), Is.EqualTo(12));
            Assert.That(AdvancedVisemeTuning.CompactSyncIndexBits(15), Is.EqualTo(4));
            Assert.That(AdvancedVisemeTuning.CompactSyncBits(15), Is.EqualTo(12));
            Assert.That(AdvancedVisemeTuning.CompactSyncIndexBits(16), Is.EqualTo(5));
            Assert.That(AdvancedVisemeTuning.CompactSyncBits(16), Is.EqualTo(13));

            Assert.That(AdvancedVisemeTuning.Controls.Count, Is.EqualTo(26));
            Assert.That(AdvancedVisemeTuning.CompactSyncIndexBits(
                    AdvancedVisemeTuning.Controls.Count),
                Is.EqualTo(5));
            Assert.That(AdvancedVisemeTuning.CompactSyncBits(
                    AdvancedVisemeTuning.Controls.Count),
                Is.EqualTo(13),
                "All 26 saved controls must share one 8-bit payload and five index bits.");

            Assert.That(AdvancedVisemeTuning.CompactSyncTransportIndexBits,
                Is.EqualTo(5));
            Assert.That(AdvancedVisemeTuning.CompactSyncTransportBits(0), Is.Zero);
            Assert.That(AdvancedVisemeTuning.CompactSyncTransportBits(1), Is.EqualTo(13));
            Assert.That(AdvancedVisemeTuning.CompactSyncTransportBits(8), Is.EqualTo(13));
            Assert.That(AdvancedVisemeTuning.CompactSyncTransportBits(26), Is.EqualTo(13),
                "Every non-empty platform uses the same wire width and channel namespace.");
        }

        [Test]
        public void CompactSyncQuantizationIsClampedMonotonicAndRoundTripsEveryCode()
        {
            const int maximum = AdvancedVisemeTuning.CompactSyncQuantizationMaximum;
            Assert.That(maximum, Is.EqualTo(254));
            Assert.That(AdvancedVisemeTuning.QuantizeCompactSync(-1f), Is.Zero);
            Assert.That(AdvancedVisemeTuning.QuantizeCompactSync(0f), Is.Zero);
            Assert.That(AdvancedVisemeTuning.QuantizeCompactSync(1f), Is.EqualTo(maximum));
            Assert.That(AdvancedVisemeTuning.QuantizeCompactSync(2f), Is.EqualTo(maximum));
            Assert.That(AdvancedVisemeTuning.DequantizeCompactSync(-1), Is.Zero);
            Assert.That(AdvancedVisemeTuning.DequantizeCompactSync(maximum + 1),
                Is.EqualTo(1f).Within(1e-7f));
            Assert.That(AdvancedVisemeTuning.QuantizeCompactSync(0.5f / maximum),
                Is.EqualTo(1), "Exact half-code boundaries use the runtime's half-up rule.");
            Assert.That(AdvancedVisemeTuning.QuantizeCompactSync(2.5f / maximum),
                Is.EqualTo(3), "Quantization must not use banker's rounding.");

            var previous = -1;
            for (var sample = 0; sample <= 4096; sample++)
            {
                var value = sample / 4096f;
                var code = AdvancedVisemeTuning.QuantizeCompactSync(value);
                var decoded = AdvancedVisemeTuning.DequantizeCompactSync(code);
                Assert.That(code, Is.InRange(0, maximum));
                Assert.That(code, Is.GreaterThanOrEqualTo(previous));
                Assert.That(decoded, Is.EqualTo(value).Within(0.5f / maximum + 1e-6f));
                previous = code;
            }

            for (var code = 0; code <= maximum; code++)
            {
                var decoded = AdvancedVisemeTuning.DequantizeCompactSync(code);
                Assert.That(AdvancedVisemeTuning.QuantizeCompactSync(decoded),
                    Is.EqualTo(code), "Quantized code " + code);
            }
        }

        [Test]
        public void DefaultControlValuesExactlyRepresentTheConfiguredProfile()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                foreach (var control in AdvancedVisemeTuning.Controls)
                {
                    var expected = AdvancedVisemeTuning.IsCenteredControl(control)
                        ? 0.5f
                        : control == AdvancedVisemeTuningControl.QuietMotion
                            ? 0.55f
                            : control == AdvancedVisemeTuningControl.SpeechLiveliness
                                ? 0.5f
                                : 1f;
                    Assert.That(AdvancedVisemeTuning.DefaultValue(profile, control),
                        Is.EqualTo(expected).Within(1e-6f), control.ToString());
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ComponentValidationRestoresSafeMenuDefaultsAndStableNames()
        {
            var root = new GameObject("Advanced Viseme Tuning Component Test");
            try
            {
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.parameterPrefix = " Demo/AdvancedViseme/// ";
                component.createTuningMenu = false;
                component.saveTuningValues = false;
                component.tuningMenuPath = "  ";
                component.tuningMenuSections = (AdvancedVisemeTuningMenuSections)(1 << 12);
                SetPrivateInt(component, "settingsVersion", 0);

                InvokeOnValidate(component);

                Assert.That(component.NormalizedPrefix, Is.EqualTo("Demo/AdvancedViseme"));
                Assert.That(component.createTuningMenu, Is.True);
                Assert.That(component.saveTuningValues, Is.True);
                Assert.That(component.tuningSyncMode,
                    Is.EqualTo(AdvancedVisemeTuningSyncMode.CompactSynced));
                Assert.That(component.tuningMenuPath, Is.EqualTo("YUCP/Viseme Settings"));
                Assert.That(component.tuningMenuSections, Is.EqualTo(AdvancedVisemeTuningMenuSections.All));
                foreach (var control in AdvancedVisemeTuning.Controls)
                {
                    Assert.That(component.TuningParameterName(control), Is.EqualTo(
                        "Demo/AdvancedViseme/Tuning/" + AdvancedVisemeTuning.ParameterSuffix(control)));
                }

                component.tuningMenuPath = " /Avatar/Voice/ ";
                component.tuningSyncMode = (AdvancedVisemeTuningSyncMode)999;
                component.tuningMenuSections = AdvancedVisemeTuningMenuSections.Speech |
                                               (AdvancedVisemeTuningMenuSections)(1 << 12);
                InvokeOnValidate(component);
                Assert.That(component.tuningMenuPath, Is.EqualTo("Avatar/Voice"));
                Assert.That(component.tuningSyncMode,
                    Is.EqualTo(AdvancedVisemeTuningSyncMode.CompactSynced));
                Assert.That(component.tuningMenuSections, Is.EqualTo(AdvancedVisemeTuningMenuSections.Speech));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeMenuBuilderCreatesSemanticRadialSubmenus()
        {
            var folderName = "__YUCP_AVR_TuningMenu_" + Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            var assetPath = folder + "/TuningMenu.asset";
            AssetDatabase.CreateFolder("Assets", folderName);
            try
            {
                var parameters = AdvancedVisemeTuning.Controls.ToDictionary(
                    control => control,
                    control => "Test/Tuning/" + AdvancedVisemeTuning.ParameterSuffix(control));
                var root = AdvancedVisemeRuntimeMenuBuilder.Build(assetPath, parameters);

                Assert.That(root, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(root), Is.EqualTo(assetPath));
                Assert.That(root.controls, Has.Count.EqualTo(2));
                Assert.That(root.controls, Has.All.Matches<VRCExpressionsMenu.Control>(control =>
                    control.type == VRCExpressionsMenu.Control.ControlType.SubMenu &&
                    control.subMenu != null));

                var simple = root.controls.Single(control => control.name == "Simple").subMenu;
                var advanced = root.controls.Single(control => control.name == "Advanced").subMenu;
                Assert.That(simple.controls, Has.Count.EqualTo(
                    AdvancedVisemeTuning.SimpleControls.Count));
                Assert.That(advanced.controls, Has.Count.EqualTo(Sections.Length));

                foreach (var tuningControl in AdvancedVisemeTuning.SimpleControls)
                {
                    var radial = simple.controls.Single(control =>
                        control.name == AdvancedVisemeTuning.SimpleLabel(tuningControl));
                    Assert.That(radial.type,
                        Is.EqualTo(VRCExpressionsMenu.Control.ControlType.RadialPuppet));
                    Assert.That(radial.subParameters, Is.Not.Null.And.Length.EqualTo(1));
                    Assert.That(radial.subParameters[0].name,
                        Is.EqualTo(parameters[tuningControl]),
                        "Simple and Advanced must share one local tuning parameter.");
                }

                foreach (var section in Sections)
                {
                    var sectionControl = advanced.controls.Single(control =>
                        control.name == AdvancedVisemeTuning.SectionLabel(section));
                    var menu = sectionControl.subMenu;
                    var expectedControls = AdvancedVisemeTuning.Controls.Where(control =>
                        AdvancedVisemeTuning.Section(control) == section).ToArray();

                    Assert.That(menu.controls, Has.Count.EqualTo(expectedControls.Length));
                    Assert.That(menu.controls.Count, Is.LessThanOrEqualTo(8));
                    foreach (var tuningControl in expectedControls)
                    {
                        var radial = menu.controls.Single(control =>
                            control.name == AdvancedVisemeTuning.Label(tuningControl));
                        Assert.That(radial.type,
                            Is.EqualTo(VRCExpressionsMenu.Control.ControlType.RadialPuppet));
                        Assert.That(radial.subParameters, Is.Not.Null.And.Length.EqualTo(1));
                        Assert.That(radial.subParameters[0].name,
                            Is.EqualTo(parameters[tuningControl]));
                    }
                }

                var menuAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                    .OfType<VRCExpressionsMenu>().ToArray();
                Assert.That(menuAssets, Has.Length.EqualTo(3 + Sections.Length));
                Assert.That(menuAssets, Has.All.Matches<VRCExpressionsMenu>(menu =>
                    menu.controls != null && menu.controls.Count <= 8));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void RuntimeMenuFocusChannelsAreCanonicalAndSharedBySimpleAliases()
        {
            var folderName = "__YUCP_AVR_TuningFocus_" + Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            var assetPath = folder + "/TuningMenu.asset";
            const string focus = "Test/_TuningSync/Focused";
            AssetDatabase.CreateFolder("Assets", folderName);
            try
            {
                // Reverse insertion order deliberately: channel IDs are a serialized
                // contract and may never depend on dictionary enumeration order.
                var parameters = AdvancedVisemeTuning.Controls
                    .Reverse()
                    .ToDictionary(
                        control => control,
                        control => "Test/Tuning/" +
                                   AdvancedVisemeTuning.ParameterSuffix(control));
                var root = AdvancedVisemeRuntimeMenuBuilder.Build(
                    assetPath, parameters, focus);
                var radials = RadialControls(root).ToArray();

                for (var index = 0; index < AdvancedVisemeTuning.Controls.Count; index++)
                {
                    var control = AdvancedVisemeTuning.Controls[index];
                    var parameter = parameters[control];
                    var aliases = radials.Where(radial =>
                        radial.subParameters != null &&
                        radial.subParameters.Length == 1 &&
                        radial.subParameters[0].name == parameter).ToArray();
                    var expectedAliasCount = AdvancedVisemeTuning.SimpleControls.Contains(control)
                        ? 2
                        : 1;

                    Assert.That(AdvancedVisemeRuntimeMenuBuilder.ChannelId(control, parameters),
                        Is.EqualTo(AdvancedVisemeTuning.CompactSyncChannelId(control)),
                        control.ToString());
                    Assert.That(AdvancedVisemeTuning.CompactSyncChannelId(control),
                        Is.EqualTo(index + 1), control.ToString());
                    Assert.That(aliases, Has.Length.EqualTo(expectedAliasCount), control.ToString());
                    Assert.That(aliases, Has.All.Matches<VRCExpressionsMenu.Control>(radial =>
                            radial.parameter != null &&
                            radial.parameter.name == focus &&
                            Mathf.Approximately(radial.value, index + 1)),
                        control + " aliases must select one identical transport channel.");
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void SimpleRuntimeMenuFiltersUnavailableControlsWithoutReplacingThem()
        {
            var parameters = new Dictionary<AdvancedVisemeTuningControl, string>
            {
                { AdvancedVisemeTuningControl.SpeechMotion, "Test/Tuning/SpeechMotion" },
                { AdvancedVisemeTuningControl.SpeechLiveliness, "Test/Tuning/SpeechLiveliness" },
                { AdvancedVisemeTuningControl.TongueArch, "Test/Tuning/TongueArch" },
                { AdvancedVisemeTuningControl.ConstraintAmount, "Test/Tuning/ConstraintAmount" }
            };

            Assert.That(AdvancedVisemeRuntimeMenuBuilder.OrderedSimpleControls(parameters),
                Is.EqualTo(new[]
                {
                    AdvancedVisemeTuningControl.SpeechMotion,
                    AdvancedVisemeTuningControl.SpeechLiveliness,
                    AdvancedVisemeTuningControl.ConstraintAmount
                }));
            Assert.That(AdvancedVisemeRuntimeMenuBuilder.OrderedControlsForSection(
                    AdvancedVisemeTuningMenuSections.Tongue, parameters),
                Is.EqualTo(new[] { AdvancedVisemeTuningControl.TongueArch }),
                "Advanced must retain controls that are intentionally absent from Simple.");

            foreach (var control in parameters.Keys)
                Assert.That(AdvancedVisemeRuntimeMenuBuilder.ChannelId(control, parameters),
                    Is.EqualTo(AdvancedVisemeTuning.CompactSyncChannelId(control)),
                    control + " must not be renumbered when other platform controls are absent.");
            Assert.That(AdvancedVisemeRuntimeMenuBuilder.ChannelId(
                    AdvancedVisemeTuningControl.SpeechLiveliness, parameters),
                Is.EqualTo(26),
                "Sparse platform menus retain the full catalog's serialized channel IDs.");
        }

        [Test]
        public void GeneratedTuningInputsAreSavedLocalOnlyFloatsWithExactDefaults()
        {
            var fixture = BuildGraph(
                AdvancedVisemeTuningMenuSections.All,
                saveValues: true,
                createMenu: true,
                beta: true,
                tracking: true);
            try
            {
                var generated = fixture.result.parameters.parameters
                    .Where(parameter => parameter.name.StartsWith(
                        fixture.component.NormalizedPrefix + "/Tuning/", StringComparison.Ordinal))
                    .ToDictionary(parameter => parameter.name);

                Assert.That(generated, Has.Count.EqualTo(AdvancedVisemeTuning.Controls.Count));
                Assert.That(fixture.result.tuningParameters.Keys,
                    Is.EquivalentTo(AdvancedVisemeTuning.Controls));
                foreach (var control in AdvancedVisemeTuning.Controls)
                {
                    var name = fixture.component.TuningParameterName(control);
                    Assert.That(fixture.result.tuningParameters[control], Is.EqualTo(name));
                    Assert.That(generated.ContainsKey(name), Is.True);
                    Assert.That(generated[name].valueType,
                        Is.EqualTo(VRCExpressionParameters.ValueType.Float), control.ToString());
                    Assert.That(generated[name].saved, Is.True, control.ToString());
                    Assert.That(generated[name].networkSynced, Is.False,
                        control + " must not consume eight synced bits.");
                    Assert.That(generated[name].defaultValue, Is.EqualTo(
                        AdvancedVisemeTuning.DefaultValue(fixture.profile, control)).Within(1e-6f),
                        control.ToString());
                    Assert.That(fixture.result.externalParameters, Does.Contain(name));
                }

                var controllerParameters = fixture.result.controller.parameters
                    .ToDictionary(parameter => parameter.name);
                foreach (var pair in fixture.result.tuningParameters)
                {
                    Assert.That(controllerParameters.ContainsKey(pair.Value), Is.True);
                    Assert.That(controllerParameters[pair.Value].type,
                        Is.EqualTo(AnimatorControllerParameterType.Float));
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void CompactSyncMetadataCostsThirteenBitsAndAddsNoBlendTrees()
        {
            var compact = BuildGraph(
                AdvancedVisemeTuningMenuSections.All,
                saveValues: true,
                createMenu: true,
                beta: false,
                tracking: false,
                tuningSyncMode: AdvancedVisemeTuningSyncMode.CompactSynced);
            var localOnly = BuildGraph(
                AdvancedVisemeTuningMenuSections.All,
                saveValues: true,
                createMenu: true,
                beta: false,
                tracking: false,
                tuningSyncMode: AdvancedVisemeTuningSyncMode.LocalOnly);
            try
            {
                Assert.That(compact.result.tuningSyncBits, Is.EqualTo(13));
                Assert.That(compact.result.tuningSyncDataParameter,
                    Is.EqualTo(AdvancedVisemeTuning.CompactSyncDataParameter(
                        compact.component.NormalizedPrefix)));
                Assert.That(compact.result.tuningSyncFocusParameter,
                    Is.EqualTo(AdvancedVisemeTuning.CompactSyncFocusParameter(
                        compact.component.NormalizedPrefix)));
                Assert.That(compact.result.tuningSyncIndexParameters,
                    Is.EqualTo(Enumerable.Range(0, 5).Select(bit =>
                        AdvancedVisemeTuning.CompactSyncIndexParameter(
                            compact.component.NormalizedPrefix, bit))));

                var parameters = compact.result.parameters.parameters
                    .ToDictionary(parameter => parameter.name);
                var data = parameters[compact.result.tuningSyncDataParameter];
                Assert.That(data.valueType, Is.EqualTo(VRCExpressionParameters.ValueType.Int));
                Assert.That(data.saved, Is.False);
                Assert.That(data.networkSynced, Is.True);

                var focus = parameters[compact.result.tuningSyncFocusParameter];
                Assert.That(focus.valueType, Is.EqualTo(VRCExpressionParameters.ValueType.Int));
                Assert.That(focus.saved, Is.False);
                Assert.That(focus.networkSynced, Is.False);

                foreach (var name in compact.result.tuningSyncIndexParameters)
                {
                    var index = parameters[name];
                    Assert.That(index.valueType,
                        Is.EqualTo(VRCExpressionParameters.ValueType.Bool), name);
                    Assert.That(index.saved, Is.False, name);
                    Assert.That(index.networkSynced, Is.True, name);
                }

                var carrierCost = compact.result.parameters.parameters
                    .Where(parameter =>
                        parameter.name == compact.result.tuningSyncDataParameter ||
                        compact.result.tuningSyncIndexParameters.Contains(parameter.name))
                    .Sum(parameter => parameter.valueType ==
                                      VRCExpressionParameters.ValueType.Bool ? 1 : 8);
                Assert.That(carrierCost, Is.EqualTo(13));

                foreach (var name in compact.result.tuningParameters.Values)
                {
                    Assert.That(parameters[name].saved, Is.True, name);
                    Assert.That(parameters[name].networkSynced, Is.False, name);
                }

                Assert.That(localOnly.result.tuningSyncBits, Is.Zero);
                Assert.That(localOnly.result.tuningSyncDataParameter, Is.Null.Or.Empty);
                Assert.That(localOnly.result.tuningSyncFocusParameter, Is.Null.Or.Empty);
                Assert.That(localOnly.result.tuningSyncIndexParameters, Is.Empty);
                Assert.That(localOnly.result.parameters.parameters.Any(parameter =>
                    parameter.name.Contains("/_TuningSync/")), Is.False);

                var compactTrees = AssetDatabase.LoadAllAssetsAtPath(compact.controllerPath)
                    .OfType<BlendTree>().Count();
                var localOnlyTrees = AssetDatabase.LoadAllAssetsAtPath(localOnly.controllerPath)
                    .OfType<BlendTree>().Count();
                Assert.That(compactTrees, Is.EqualTo(localOnlyTrees),
                    "The compact transport must remain state/driver-only and add zero BlendTrees.");
                var transport = compact.result.controller.layers.Single(layer =>
                    layer.name == "YUCP AVR Compact Tuning Sync");
                Assert.That(transport.defaultWeight, Is.Zero,
                    "The state/driver transport must contribute no blended output.");
                var drivers = transport.stateMachine.states
                    .SelectMany(child => child.state.behaviours)
                    .OfType<VRCAvatarParameterDriver>()
                    .ToArray();
                var senderCopy = drivers.Where(driver => driver.localOnly)
                    .SelectMany(driver => driver.parameters)
                    .First(parameter =>
                        parameter.name == compact.result.tuningSyncDataParameter);
                Assert.That(senderCopy.convertRange, Is.True);
                Assert.That(senderCopy.sourceMin, Is.Zero);
                Assert.That(senderCopy.sourceMax, Is.EqualTo(1f));
                Assert.That(senderCopy.destMin, Is.EqualTo(0.5f));
                Assert.That(senderCopy.destMax, Is.EqualTo(
                    AdvancedVisemeTuning.CompactSyncQuantizationMaximum + 0.5f));
                var receiverCopy = drivers.Where(driver => !driver.localOnly)
                    .SelectMany(driver => driver.parameters)
                    .First(parameter =>
                        parameter.source == compact.result.tuningSyncDataParameter);
                Assert.That(receiverCopy.sourceMin, Is.Zero);
                Assert.That(receiverCopy.sourceMax, Is.EqualTo(
                    AdvancedVisemeTuning.CompactSyncQuantizationMaximum));
                Assert.That(receiverCopy.destMin, Is.Zero);
                Assert.That(receiverCopy.destMax, Is.EqualTo(1f));
                Assert.That(localOnly.result.controller.layers.Any(layer =>
                    layer.name == "YUCP AVR Compact Tuning Sync"), Is.False);
            }
            finally
            {
                compact.Dispose();
                localOnly.Dispose();
            }
        }

        [Test]
        public void TuningSectionsAndSaveChoiceControlOnlyExpressionInputs()
        {
            var fixture = BuildGraph(
                AdvancedVisemeTuningMenuSections.Phonetics,
                saveValues: false,
                createMenu: true,
                beta: true,
                tracking: true);
            try
            {
                var tuningInputs = fixture.result.parameters.parameters.Where(parameter =>
                    parameter.name.StartsWith(
                        fixture.component.NormalizedPrefix + "/Tuning/", StringComparison.Ordinal)).ToArray();
                var expected = AdvancedVisemeTuning.Controls.Where(control =>
                    AdvancedVisemeTuning.Section(control) ==
                    AdvancedVisemeTuningMenuSections.Phonetics).ToArray();

                Assert.That(tuningInputs.Select(parameter => parameter.name), Is.EquivalentTo(
                    expected.Select(fixture.component.TuningParameterName)));
                Assert.That(tuningInputs, Has.All.Matches<VRCExpressionParameters.Parameter>(parameter =>
                    parameter.valueType == VRCExpressionParameters.ValueType.Float &&
                    !parameter.saved && !parameter.networkSynced));

                // All preferences remain graph parameters at profile defaults even
                // if a section is hidden; section selection controls UI exposure,
                // not the mathematical model.
                Assert.That(fixture.result.tuningParameters.Keys,
                    Is.EquivalentTo(expected));
            }
            finally
            {
                fixture.Dispose();
            }

            fixture = BuildGraph(
                AdvancedVisemeTuningMenuSections.All,
                saveValues: true,
                createMenu: false,
                beta: false,
                tracking: false);
            try
            {
                Assert.That(fixture.result.parameters.parameters.Any(parameter =>
                    parameter.name.Contains("/Tuning/")), Is.False);
                Assert.That(fixture.result.tuningParameters, Is.Empty);
                Assert.That(fixture.result.controller.parameters.Any(parameter =>
                    parameter.name.Contains("/Tuning/SpeechMotion")), Is.True,
                    "Hiding the menu must retain profile-backed internal tuning constants.");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void RepresentativeTuningParametersParticipateInGeneratedMath()
        {
            var fixture = BuildGraph(
                AdvancedVisemeTuningMenuSections.All,
                saveValues: true,
                createMenu: true,
                beta: true,
                tracking: true);
            try
            {
                var trees = AssetDatabase.LoadAllAssetsAtPath(fixture.controllerPath)
                    .OfType<BlendTree>().ToArray();
                foreach (var control in new[]
                         {
                             AdvancedVisemeTuningControl.SpeechSmoothness,
                             AdvancedVisemeTuningControl.SilenceStability,
                             AdvancedVisemeTuningControl.SpeechMotion,
                             AdvancedVisemeTuningControl.SpeechLiveliness,
                             AdvancedVisemeTuningControl.TrackingSmoothness,
                             AdvancedVisemeTuningControl.RemoteTrust,
                             AdvancedVisemeTuningControl.ConstraintAmount,
                             AdvancedVisemeTuningControl.HiddenPhone,
                             AdvancedVisemeTuningControl.TongueInference,
                             AdvancedVisemeTuningControl.TongueArch
                         })
                {
                    var parameter = fixture.result.tuningParameters[control];
                    Assert.That(trees.Any(tree => UsesParameter(tree, parameter)), Is.True,
                        control + " exists but does not participate in generated math.");
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void CollapsedVisibleTongueKernelIsOptInAndRemovesTheLegacyFactorGraph()
        {
            var previous = AdvancedVisemeAnimatorBuilder
                .UseCollapsedVisibleTongueKernelForTests;
            GraphFixture fixture = null;
            try
            {
                Assert.That(previous, Is.False,
                    "The collapsed-kernel A/B seam must remain disabled in production.");
                AdvancedVisemeAnimatorBuilder.UseCollapsedVisibleTongueKernelForTests = true;
                fixture = BuildGraph(
                    AdvancedVisemeTuningMenuSections.All,
                    saveValues: true,
                    createMenu: true,
                    beta: true,
                    tracking: true);

                var trees = AssetDatabase.LoadAllAssetsAtPath(fixture.controllerPath)
                    .OfType<BlendTree>().ToArray();
                var parameterNames = fixture.result.controller.parameters
                    .Select(parameter => parameter.name).ToArray();
                var internalPrefix = fixture.component.NormalizedPrefix + "/_Internal/";
                var featureCount = AdvancedVisemeVisibleTongueResidual.FeatureCount(
                    AdvancedVisemeVisibleTongueModelKind.Quality);

                Assert.That(parameterNames.Count(name => name.StartsWith(
                        internalPrefix + "TongueInference/Model/FeatureUnit/",
                        StringComparison.Ordinal)),
                    Is.EqualTo(featureCount));
                Assert.That(parameterNames.Count(name => name.EndsWith(
                        "/TongueInference/Model/TongueOut/Normalized",
                        StringComparison.Ordinal) || name.EndsWith(
                        "/TongueInference/Model/TongueY/Normalized",
                        StringComparison.Ordinal)),
                    Is.EqualTo(2));
                Assert.That(parameterNames,
                    Does.Contain(internalPrefix + "TongueInference/Model/Reliability"));
                foreach (var output in new[] { "TongueOut", "TongueY" })
                foreach (var suffix in new[]
                         {
                             "/Reliable", string.Empty, "/StableFast", "/Stable"
                         })
                    Assert.That(parameterNames, Does.Contain(
                        internalPrefix + "TongueInference/Model/" + output + suffix),
                        output + suffix + " must retain scaling, clamp, and stabilization.");
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/TongueInference/Model/Visible/", StringComparison.Ordinal)), Is.False);
                Assert.That(parameterNames.Any(name => name.Contains(
                    "/MixUnit/", StringComparison.Ordinal)), Is.False);
                Assert.That(parameterNames.Any(name => name.EndsWith(
                    "/ProductAccumulator", StringComparison.Ordinal)), Is.False);

                var featureLanes = trees.Where(tree =>
                        tree.name.StartsWith("Tongue inference collapsed lane ",
                            StringComparison.Ordinal) &&
                        !tree.name.EndsWith("rank-one correction",
                            StringComparison.Ordinal))
                    .ToArray();
                Assert.That(featureLanes, Has.Length.EqualTo(featureCount),
                    "The constant lane is lowered directly; one weighted Direct lane " +
                    "must remain for each unit feature.");
                Assert.That(featureLanes.All(tree =>
                    tree.blendType == BlendTreeType.Direct), Is.True);
                Assert.That(trees.Count(tree => tree.name.StartsWith(
                                "Tongue inference collapsed lane ",
                                StringComparison.Ordinal) &&
                            tree.name.EndsWith("rank-one correction",
                                StringComparison.Ordinal)),
                    Is.EqualTo(featureCount + 1),
                    "Every unit-feature lane, including the constant lane, must " +
                    "carry the exact signed PP-to-nn correction.");
                Assert.That(trees.Any(tree => tree.name ==
                    "Tongue inference visible latent contraction"), Is.False);
                Assert.That(trees.Any(tree => tree.name ==
                    "Tongue inference viseme contraction"), Is.False);
                Assert.That(trees.Any(tree => tree.name ==
                    "Tongue inference two-output bilinear accumulator"), Is.False);
            }
            finally
            {
                fixture?.Dispose();
                AdvancedVisemeAnimatorBuilder.UseCollapsedVisibleTongueKernelForTests =
                    previous;
            }
        }

        [Test]
        public void GeneratedLivelinessPublishesOneSharedLedSimplexAndArticulationVector()
        {
            foreach (var beta in new[] { false, true })
            {
                var fixture = BuildGraph(
                    AdvancedVisemeTuningMenuSections.All,
                    saveValues: true,
                    createMenu: true,
                    beta: beta,
                    tracking: true);
                try
                {
                    var trees = AssetDatabase.LoadAllAssetsAtPath(fixture.controllerPath)
                        .OfType<BlendTree>().ToArray();
                    var prefix = fixture.component.NormalizedPrefix;
                    var lead = prefix + "/_Internal/Speech/RenderLead";
                    var trackingBlend = prefix + "/Speech/TrackingBlend";
                    var liveliness = fixture.result.tuningParameters[
                        AdvancedVisemeTuningControl.SpeechLiveliness];

                    var leadGate = trees.SingleOrDefault(tree =>
                        tree.name == $"Scale {liveliness} by inverse {trackingBlend}");
                    Assert.That(leadGate, Is.Not.Null, beta ? "Beta" : "Normal");
                    Assert.That(leadGate.blendParameter, Is.EqualTo(trackingBlend));
                    Assert.That(leadGate.children.Select(child => child.threshold),
                        Is.EqualTo(new[] { 0f, 1f }));

                    var renderVector = trees.SingleOrDefault(tree =>
                        tree.name == "Speech-liveliness viseme render vector");
                    var articulation = trees.SingleOrDefault(tree =>
                        tree.name == "Speech-liveliness articulation vector");
                    Assert.That(renderVector, Is.Not.Null);
                    Assert.That(articulation, Is.Not.Null);
                    Assert.That(renderVector.blendParameter, Is.EqualTo(lead));
                    Assert.That(articulation.blendParameter, Is.EqualTo(lead));
                    if (beta)
                    {
                        Assert.That(trees.Any(tree => tree.name.EndsWith(
                                "coarticulated slow trajectory",
                                StringComparison.Ordinal)), Is.False,
                            "Beta context must not bypass the persistent physical " +
                            "viseme observer; Speech Liveliness is the only visible lead.");
                    }

                    foreach (var viseme in VisemeReconstructionProfile.VisemeNames)
                    {
                        var publicName = prefix + "/Viseme/" + viseme;
                        Assert.That(fixture.result.globalParameters,
                            Does.Contain(publicName));
                        Assert.That(fixture.result.controller.parameters.Any(parameter =>
                            parameter.name == publicName &&
                            parameter.type == AnimatorControllerParameterType.Float), Is.True);
                    }
                }
                finally
                {
                    fixture.Dispose();
                }
            }
        }

        [Test]
        public void SpeechHistoryIsLocalAndGatesEveryVisemeObserverWithoutFeedbackState()
        {
            var fixture = BuildGraph(
                AdvancedVisemeTuningMenuSections.Speech,
                saveValues: true,
                createMenu: true,
                beta: false,
                tracking: true);
            try
            {
                var prefix = fixture.component.NormalizedPrefix;
                var internalPrefix = prefix + "/_Internal";
                var talking = prefix + "/Speech/Talking";
                var controllerParameters = fixture.result.controller.parameters;
                var trees = AssetDatabase.LoadAllAssetsAtPath(fixture.controllerPath)
                    .OfType<BlendTree>().ToArray();

                Assert.That(controllerParameters.Any(parameter =>
                    parameter.name == talking &&
                    parameter.type == AnimatorControllerParameterType.Float), Is.True);
                Assert.That(fixture.result.globalParameters, Does.Contain(talking));
                Assert.That(fixture.result.parameters.parameters.Any(parameter =>
                    parameter.name == talking), Is.False,
                    "Talking is reusable local Animator state, not a synced expression input.");

                foreach (var stateName in new[]
                         {
                             "/Alpha/SpeechHistoryAttack",
                             "/Alpha/SpeechHistoryRelease/Configured",
                             "/Alpha/SpeechHistoryRelease/Extended",
                             "/Alpha/SpeechHistoryRelease",
                             "/Speech/Hangover/History",
                         })
                {
                    Assert.That(controllerParameters.Any(parameter =>
                        parameter.name == internalPrefix + stateName), Is.True, stateName);
                }

                foreach (var removedState in new[]
                         {
                             "/Speech/Hangover/VoiceLatched",
                             "/Speech/Hangover/Armed",
                             "/Speech/Hangover/BurstSeconds",
                             "/Speech/Hangover/Seconds",
                             "/Speech/Hangover/VoiceBridge/Seconds",
                             "/Speech/Hangover/TimerFrameFraction",
                             "/Speech/HeldSilenceWeight"
                         })
                {
                    Assert.That(controllerParameters.Any(parameter =>
                        parameter.name == internalPrefix + removedState), Is.False,
                        removedState + " would reintroduce delayed feedback state.");
                }

                var visemeIndex = internalPrefix + "/Viseme/Index";
                var stability = fixture.result.tuningParameters[
                    AdvancedVisemeTuningControl.SilenceStability];
                var history = internalPrefix + "/Speech/Hangover/History";
                var historyTree = trees.SingleOrDefault(tree =>
                    tree.name == $"Asymmetric smooth {history} by {visemeIndex}");
                Assert.That(historyTree, Is.Not.Null);
                Assert.That(historyTree.blendParameter, Is.EqualTo(visemeIndex));

                var gate = trees.SingleOrDefault(tree =>
                    tree.name == "Vector transient-silence hold");
                var strengthGate = trees.SingleOrDefault(tree =>
                    tree.name == "Vector silence hold strength");
                var historyGate = trees.SingleOrDefault(tree =>
                    tree.name == "Vector silence hold history");
                Assert.That(gate, Is.Not.Null,
                    "All transient-silence lanes must share one vector router.");
                Assert.That(gate.blendParameter, Is.EqualTo(visemeIndex));
                Assert.That(strengthGate, Is.Not.Null);
                Assert.That(strengthGate.blendParameter, Is.EqualTo(stability));
                Assert.That(strengthGate.children.Select(child => child.threshold),
                    Is.EquivalentTo(new[] { 0f, 0.5f, 1f }));
                Assert.That(historyGate, Is.Not.Null);
                Assert.That(historyGate.blendParameter, Is.EqualTo(history));
                Assert.That(historyGate.children.Select(child => child.threshold),
                    Is.EquivalentTo(new[]
                    {
                        AdvancedVisemeMath.SpeechHistoryHoldStart,
                        AdvancedVisemeMath.SpeechHistoryHoldFull
                    }));

                for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
                {
                    var raw = internalPrefix + $"/Viseme/{viseme}/Raw";
                    var fast = internalPrefix + $"/Viseme/{viseme}/Fast";
                    Assert.That(WritesParameter(gate, fast), Is.True,
                        $"The shared router does not publish viseme {viseme}.");
                    Assert.That(UsesParameter(gate, raw), Is.True,
                        $"Viseme {viseme} bypasses the release observer.");
                }

                var voiceGainBase = internalPrefix + "/Voice/GainBase";
                Assert.That(WritesParameter(gate, talking), Is.True);
                Assert.That(WritesParameter(gate, voiceGainBase), Is.True);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void BetaCoarticulationGatesContinuousFastAndRetentionContext()
        {
            var fixture = BuildGraph(
                AdvancedVisemeTuningMenuSections.Speech,
                saveValues: true,
                createMenu: true,
                beta: true,
                tracking: false);
            try
            {
                var internalPrefix = fixture.component.NormalizedPrefix + "/_Internal";
                var visemeIndex = internalPrefix + "/Viseme/Index";
                var trees = AssetDatabase.LoadAllAssetsAtPath(fixture.controllerPath)
                    .OfType<BlendTree>().ToArray();
                var parameterNames = fixture.result.controller.parameters
                    .Select(parameter => parameter.name).ToArray();
                var gate = trees.SingleOrDefault(tree =>
                    tree.name == "Vector transient-silence hold");
                Assert.That(gate, Is.Not.Null);
                Assert.That(gate.blendParameter, Is.EqualTo(visemeIndex));
                var silenceRouters = trees.Where(tree =>
                        tree.name == "Vector transient-silence hold" ||
                        tree.name == "Vector compact transient-silence hold")
                    .ToArray();
                var contextAlphas = fixture.result.controller.parameters
                    .Select(parameter => parameter.name)
                    .Where(parameter => parameter.StartsWith(
                        internalPrefix + "/BetaCoarticulation/Context/",
                        StringComparison.Ordinal))
                    .Where(parameter => parameter.EndsWith(
                        "/Alpha", StringComparison.Ordinal))
                    .ToArray();
                var retentionStates = parameterNames.Where(parameter =>
                        parameter.Contains("/BetaCoarticulation/RetentionState/",
                            StringComparison.Ordinal))
                    .ToArray();
                const string retentionMarker =
                    "/BetaCoarticulation/RetentionState/";
                var liveGroups = retentionStates
                    .Select(parameter => parameter.Substring(
                        parameter.IndexOf(retentionMarker,
                            StringComparison.Ordinal) + retentionMarker.Length))
                    .Select(suffix =>
                    {
                        var separator = suffix.IndexOf('/');
                        Assert.That(
                            separator,
                            Is.GreaterThan(0),
                            "Unexpected retention-state parameter suffix: " +
                            suffix);
                        return suffix.Substring(0, separator);
                    })
                    .Distinct(StringComparer.Ordinal)
                    .Select(name =>
                    {
                        Assert.That(
                            Enum.TryParse(name, out
                                AdvancedVisemeArticulatorGroup group),
                            Is.True,
                            "Unknown retention group: " + name);
                        return group;
                    })
                    .ToArray();
                Assert.That(liveGroups, Is.Not.Empty,
                    "At least one corpus-retention family must survive liveness.");
                foreach (var group in liveGroups)
                {
                    var marker = retentionMarker + group + "/";
                    Assert.That(retentionStates.Count(parameter =>
                            parameter.Contains(marker, StringComparison.Ordinal)),
                        Is.EqualTo(VisemeReconstructionProfile.VisemeCount),
                        $"Live {group} retention must keep one complete viseme row.");
                }
                var expectedAlphaNames = liveGroups
                    .Select(group => Mathf.RoundToInt(
                        AdvancedVisemeCoarticulationModel.DecaySeconds(group) *
                        1000000f))
                    .Distinct()
                    .Select(decay => internalPrefix +
                        $"/BetaCoarticulation/Context/{decay}/Alpha")
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                var declaredParameters = parameterNames.ToHashSet(
                    StringComparer.Ordinal);
                foreach (var expectedAlpha in expectedAlphaNames)
                {
                    var representative = expectedAlpha;
                    if (!declaredParameters.Contains(expectedAlpha))
                    {
                        Assert.That(fixture.result.optimizerReport
                                .internedParameterMappings.TryGetValue(
                                    expectedAlpha, out representative), Is.True,
                            expectedAlpha +
                            " was neither retained nor exactly interned.");
                    }
                    Assert.That(declaredParameters, Does.Contain(representative),
                        "An interned response-time alpha needs a declared representative.");
                    Assert.That(trees.Any(tree =>
                            DirectlyUsesParameter(tree, representative)), Is.True,
                        "Every surviving decay family needs an active alpha reader.");
                }
                Assert.That(contextAlphas.All(parameter => trees.Any(tree =>
                        DirectlyUsesParameter(tree, parameter))), Is.True,
                    "A retained family-specific alpha must remain live in the graph.");
                Assert.That(retentionStates.All(parameter =>
                        silenceRouters.Any(router =>
                            WritesParameter(router, parameter))), Is.True,
                    "Every retained transition-row state must pass through the shared router.");

                string RepresentativeFor(string parameter)
                {
                    var visited = new HashSet<string>(StringComparer.Ordinal);
                    while (visited.Add(parameter) &&
                           fixture.result.optimizerReport
                               .internedParameterMappings.TryGetValue(
                                   parameter, out var representative))
                        parameter = representative;
                    return parameter;
                }

                for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
                {
                    var visibleFast = RepresentativeFor(
                        internalPrefix + $"/Viseme/{viseme}/Fast");
                    Assert.That(silenceRouters.Any(router => WritesParameter(
                            router, visibleFast)),
                        Is.True);
                    var betaFast = RepresentativeFor(
                        internalPrefix +
                        $"/BetaCoarticulation/Mean/Viseme/{viseme}/Fast");
                    Assert.That(silenceRouters.Any(router => WritesParameter(
                            router, betaFast)),
                        Is.True);
                }
                Assert.That(trees.Any(tree => tree.name.Contains(
                    "sparse raw source", StringComparison.Ordinal)), Is.False,
                    "Visible Beta articulation must never project toward the raw winner.");
                Assert.That(parameterNames.Any(parameter => parameter.EndsWith(
                    "/PhoneObservationFast", StringComparison.Ordinal)), Is.False,
                    "A no-tracking build must not materialize the hidden-phone model feature.");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void SpeechLivelinessIsConvexSimplexSafeAndYieldsExactlyToTracking()
        {
            var random = new System.Random(81422);
            var slow = new float[VisemeReconstructionProfile.VisemeCount];
            var fast = new float[VisemeReconstructionProfile.VisemeCount];
            for (var sample = 0; sample < 250; sample++)
            {
                var slowSum = 0f;
                var fastSum = 0f;
                for (var index = 0; index < slow.Length; index++)
                {
                    slow[index] = (float)random.NextDouble();
                    fast[index] = (float)random.NextDouble();
                    slowSum += slow[index];
                    fastSum += fast[index];
                }
                for (var index = 0; index < slow.Length; index++)
                {
                    slow[index] /= slowSum;
                    fast[index] /= fastSum;
                }

                foreach (var liveliness in new[] { 0f, 0.2f, 0.5f, 1f })
                foreach (var trackingBlend in new[] { 0f, 0.15f, 0.7f, 1f })
                {
                    var lead = AdvancedVisemeMath.SpeechLivelinessLead(
                        liveliness, trackingBlend);
                    Assert.That(lead, Is.InRange(
                        0f, AdvancedVisemeMath.MaximumSpeechLivelinessLead));

                    var renderedSum = 0f;
                    for (var index = 0; index < slow.Length; index++)
                    {
                        var rendered = AdvancedVisemeMath.ApplySpeechLiveliness(
                            slow[index], fast[index], liveliness, trackingBlend);
                        Assert.That(rendered,
                            Is.InRange(Mathf.Min(slow[index], fast[index]) - 1e-6f,
                                Mathf.Max(slow[index], fast[index]) + 1e-6f));
                        renderedSum += rendered;
                        if (liveliness <= 0f || trackingBlend >= 1f)
                            Assert.That(rendered, Is.EqualTo(slow[index]).Within(1e-7f));
                    }
                    Assert.That(renderedSum, Is.EqualTo(1f).Within(2e-6f));
                }
            }

            Assert.That(AdvancedVisemeMath.SpeechLivelinessLead(float.NaN, 0f),
                Is.Zero.Within(1e-7f));
            Assert.That(AdvancedVisemeMath.SpeechLivelinessLead(1f, float.NaN),
                Is.EqualTo(AdvancedVisemeMath.MaximumSpeechLivelinessLead).Within(1e-7f));
        }

        [Test]
        public void SpeechLivelinessNeverAddsDelayAndCanAdvanceRenderedTransitions()
        {
            var advancedAtARepresentableRate = false;
            foreach (var fps in new[] { 15f, 30f, 60f, 90f, 144f })
            {
                var fast = new float[VisemeReconstructionProfile.VisemeCount];
                var slow = new float[VisemeReconstructionProfile.VisemeCount];
                fast[0] = 1f;
                slow[0] = 1f;
                var deltaTime = 1f / fps;
                var elapsed = 0f;
                var renderedNinety = float.PositiveInfinity;
                var slowNinety = float.PositiveInfinity;

                for (var frame = 0; frame < Mathf.CeilToInt(fps); frame++)
                {
                    AdvancedVisemeMath.StepSimplex(
                        10, deltaTime, 0.024f, fast, slow);
                    elapsed += deltaTime;
                    var rendered = AdvancedVisemeMath.ApplySpeechLiveliness(
                        slow[10], fast[10], 1f, 0f);
                    Assert.That(rendered, Is.InRange(0f, 1f));
                    if (rendered >= 0.9f && float.IsPositiveInfinity(renderedNinety))
                        renderedNinety = elapsed;
                    if (slow[10] >= 0.9f && float.IsPositiveInfinity(slowNinety))
                        slowNinety = elapsed;
                }

                Assert.That(renderedNinety, Is.LessThanOrEqualTo(slowNinety),
                    $"Speech Liveliness delayed the 90% transition at {fps} FPS.");
                advancedAtARepresentableRate |= renderedNinety < slowNinety;
                Assert.That(AdvancedVisemeMath.ApplySpeechLiveliness(
                        slow[10], fast[10], 1f, 1f),
                    Is.EqualTo(slow[10]).Within(1e-7f),
                    "Fully active tracking must recover the exact slow prior.");
            }
            Assert.That(advancedAtARepresentableRate, Is.True,
                "The bounded lead never advanced a transition at any tested render rate.");
        }

        [Test]
        public void ConfigurableSpeechAndConstraintsCannotMoveAnExactLocalMeasurement()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            var random = new System.Random(70241);
            try
            {
                foreach (var articulator in VisibleArticulators)
                for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
                foreach (var strength in new[] { 0f, 0.2f, 0.65f, 1f })
                {
                    var tracked = IsSigned(articulator)
                        ? Mathf.Lerp(-1f, 1f, (float)random.NextDouble())
                        : (float)random.NextDouble();
                    var speech = profile.visemePoses[viseme].Get(articulator) * strength;
                    var authority = AdvancedVisemeMath.TrackingAuthority(
                        speech, tracked, true, 0f, 0f);
                    Assert.That(AdvancedVisemeMath.Fuse(speech, tracked, authority),
                        Is.EqualTo(tracked).Within(1e-6f),
                        $"{articulator} moved under {VisemeReconstructionProfile.VisemeNames[viseme]} at {strength}.");

                    var unsafeCoupledFallback = strength * (float)random.NextDouble() *
                                                AdvancedVisemeMath.VisibleSpeechRemainder(authority);
                    Assert.That(unsafeCoupledFallback, Is.Zero.Within(1e-6f),
                        "An uncalibrated coupled fallback escaped the visible complement.");
                }

                foreach (var constraintAmount in new[] { 0f, 0.25f, 0.7f, 1f })
                {
                    var jaw = 0.82f;
                    var close = 0.13f;
                    var bite = 0.07f;
                    var remainder = AdvancedVisemeMath.PhoneticConstraintRemainder(1f, 1f);
                    AdvancedVisemeMath.ApplyPhoneticConstraints(
                        constraintAmount * remainder,
                        constraintAmount * remainder,
                        constraintAmount * remainder,
                        constraintAmount * remainder,
                        profile.bilabialClosure,
                        profile.labiodentalBite,
                        profile.sibilantJawMaximum,
                        ref jaw, ref close, ref bite);
                    Assert.That(jaw, Is.EqualTo(0.82f).Within(1e-6f));
                    Assert.That(close, Is.EqualTo(0.13f).Within(1e-6f));
                    Assert.That(bite, Is.EqualTo(0.07f).Within(1e-6f));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void NativeTongueTrackingRemainsExactAtEveryInferenceSetting()
        {
            var random = new System.Random(30117);
            foreach (var articulator in TongueArticulators)
            foreach (var inference in new[] { 0f, 0.15f, 0.5f, 1f })
            foreach (var axisStrength in new[] { 0f, 0.4f, 1f })
            {
                var signed = IsSigned(articulator);
                var speech = signed
                    ? Mathf.Lerp(-1f, 1f, (float)random.NextDouble())
                    : (float)random.NextDouble();
                var inferred = AdvancedVisemeMath.ApplyBoundedResidual(
                    speech,
                    Mathf.Lerp(-1f, 1f, (float)random.NextDouble()) * axisStrength,
                    inference,
                    signed);
                var tracked = signed
                    ? Mathf.Lerp(-1f, 1f, (float)random.NextDouble())
                    : (float)random.NextDouble();

                Assert.That(AdvancedVisemeMath.Fuse(inferred, tracked, 1f),
                    Is.EqualTo(tracked).Within(1e-6f),
                    $"Native {articulator} lost authority at inference={inference}, scale={axisStrength}.");
            }
        }

        [Test]
        public void ConditionalLearnedDetailSleepRemainsOptIn()
        {
            var previous = AdvancedVisemeAnimatorBuilder
                .EnableConditionalLearnedDetailSleepForTests;
            GraphFixture fixture = null;
            try
            {
                AdvancedVisemeAnimatorBuilder
                    .EnableConditionalLearnedDetailSleepForTests = false;
                fixture = BuildGraph(
                    AdvancedVisemeTuningMenuSections.All,
                    saveValues: false,
                    createMenu: false,
                    beta: true,
                    tracking: false,
                    tuningSyncMode: AdvancedVisemeTuningSyncMode.LocalOnly);

                Assert.That(fixture.result.controller.layers.Any(layer =>
                    layer.name.IndexOf(
                        "Conditional Beta", StringComparison.Ordinal) >= 0),
                    Is.False);
                Assert.That(fixture.result.controller.layers.Any(layer =>
                    layer.name.IndexOf(
                        "Observer Reset", StringComparison.Ordinal) >= 0),
                    Is.False);
                Assert.That(fixture.result.controller.parameters.Any(parameter =>
                    parameter.name.Contains(
                        "/ConditionalLearnedDetail/",
                        StringComparison.Ordinal)), Is.False);
            }
            finally
            {
                fixture?.Dispose();
                AdvancedVisemeAnimatorBuilder
                    .EnableConditionalLearnedDetailSleepForTests = previous;
            }
        }

        [Test]
        public void ConditionalMathGateAddsNoStateLayersWithoutInferenceLanes()
        {
            var previous = AdvancedVisemeAnimatorBuilder
                .EnableConditionalLearnedDetailSleepForTests;
            GraphFixture fixture = null;
            try
            {
                AdvancedVisemeAnimatorBuilder
                    .EnableConditionalLearnedDetailSleepForTests = true;
                fixture = BuildGraph(
                    AdvancedVisemeTuningMenuSections.All,
                    saveValues: false,
                    createMenu: false,
                    beta: true,
                    tracking: false,
                    tuningSyncMode: AdvancedVisemeTuningSyncMode.LocalOnly);

                Assert.That(fixture.result.controller.layers.Any(layer =>
                    layer.name.IndexOf(
                        "Conditional Beta", StringComparison.Ordinal) >= 0),
                    Is.False);
                Assert.That(fixture.result.controller.layers.Any(layer =>
                    layer.name.IndexOf(
                        "Observer Reset", StringComparison.Ordinal) >= 0),
                    Is.False,
                    "The pure-Math gate must never generate a reset layer.");
                var prefix = fixture.component.NormalizedPrefix + "/_Internal/";
                Assert.That(fixture.result.controller.parameters.Any(parameter =>
                    parameter.name == prefix +
                    "ConditionalLearnedDetail/Compute"), Is.True);
                foreach (var deadInferenceControl in new[]
                         {
                             "ConditionalLearnedDetail/Warmth",
                             "ConditionalLearnedDetail/Authority"
                         })
                    Assert.That(fixture.result.controller.parameters.Any(parameter =>
                        parameter.name == prefix + deadInferenceControl),
                        Is.False,
                        "Inference-only control must be pruned when no learned " +
                        "inference consumer exists.");
            }
            finally
            {
                fixture?.Dispose();
                AdvancedVisemeAnimatorBuilder
                    .EnableConditionalLearnedDetailSleepForTests = previous;
            }
        }

        [Test]
        public void ConditionalMathControlHasExactCullAndBoundedAuthorityEndpoints()
        {
            var epsilon = AdvancedVisemeMath.SimplexCullingEpsilon;
            var normalFrame = 1f / 60f;
            Assert.That(AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailComputeTarget(
                        0, 1f, normalFrame),
                Is.EqualTo(0f).Within(1e-7f),
                "Certified silence must cull the inference child exactly.");
            Assert.That(AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailComputeTarget(
                        10, 1f, normalFrame),
                Is.EqualTo(1f).Within(1e-7f),
                "A hard phone must wake compute even before the sparse tail moves.");
            Assert.That(AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailComputeTarget(
                        0, 1f - epsilon, normalFrame),
                Is.EqualTo(1f).Within(1e-7f));
            Assert.That(AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailComputeTarget(
                        0, 1f - epsilon * 0.5f, normalFrame),
                Is.EqualTo(0f).Within(1e-7f));
            Assert.That(AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailComputeTarget(
                        0, 1f,
                        AdvancedVisemeAnimatorBuilder
                            .ConditionalLearnedDetailLowFpsBypassFrameSeconds),
                Is.EqualTo(1f).Within(1e-7f),
                "Low frame rates must fail open.");
            foreach (var lowRate in new[] { 15f, 22f, 23f, 24f })
                Assert.That(AdvancedVisemeAnimatorBuilder
                        .ConditionalLearnedDetailComputeTarget(
                            0, 1f, 1f / lowRate),
                    Is.EqualTo(1f).Within(1e-7f),
                    $"The gate must fail open at {lowRate:0} FPS.");
            foreach (var normalRate in new[] { 25f, 30f, 60f, 90f, 144f })
                Assert.That(AdvancedVisemeAnimatorBuilder
                        .ConditionalLearnedDetailComputeTarget(
                            0, 1f, 1f / normalRate),
                    Is.EqualTo(0f).Within(1e-7f),
                    $"Stable silence at {normalRate:0} FPS must still cull.");
            Assert.That(AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailComputeTarget(
                        0, 1f, normalFrame, 0f),
                Is.EqualTo(1f).Within(1e-7f),
                "Cold startup must remain fail-open until the decoded viseme is valid.");
            Assert.That(AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailComputeTarget(
                        0, 1f, normalFrame,
                        AdvancedVisemeAnimatorBuilder
                            .ConditionalLearnedDetailStartupHotSeconds + 0.01f),
                Is.EqualTo(0f).Within(1e-7f),
                "Startup must not keep inference awake after initialization.");

            Assert.That(AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailAuthorityFromWarmth(
                        AdvancedVisemeAnimatorBuilder
                            .ConditionalLearnedDetailAuthorityStart,
                        normalFrame),
                Is.EqualTo(0f).Within(1e-7f));
            Assert.That(AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailAuthorityFromWarmth(
                        AdvancedVisemeAnimatorBuilder
                            .ConditionalLearnedDetailAuthorityFull,
                        normalFrame),
                Is.EqualTo(1f).Within(1e-7f));
            Assert.That(AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailAuthorityFromWarmth(
                        (AdvancedVisemeAnimatorBuilder
                             .ConditionalLearnedDetailAuthorityStart +
                         AdvancedVisemeAnimatorBuilder
                             .ConditionalLearnedDetailAuthorityFull) * 0.5f,
                        normalFrame),
                Is.EqualTo(0.5f).Within(1e-6f),
                "The authority admission curve must use smoothstep.");
            Assert.That(AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailAuthorityFromWarmth(
                        0f,
                        AdvancedVisemeAnimatorBuilder
                            .ConditionalLearnedDetailLowFpsBypassFrameSeconds),
                Is.EqualTo(1f).Within(1e-7f));
            Assert.That(AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailAuthorityFromWarmth(
                        0f, normalFrame, 0f),
                Is.EqualTo(1f).Within(1e-7f),
                "Cold startup must publish the legacy endpoint.");
            Assert.That(AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailAuthorityFromReadiness(
                        AdvancedVisemeAnimatorBuilder
                            .ConditionalLearnedDetailReadinessStart,
                        normalFrame),
                Is.EqualTo(0f).Within(1e-7f));
            Assert.That(AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailAuthorityFromReadiness(
                        AdvancedVisemeAnimatorBuilder
                            .ConditionalLearnedDetailReadinessFull,
                        normalFrame),
                Is.EqualTo(1f).Within(1e-7f));
            Assert.That(AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailAuthorityFromReadiness(
                        (AdvancedVisemeAnimatorBuilder
                             .ConditionalLearnedDetailReadinessStart +
                         AdvancedVisemeAnimatorBuilder
                             .ConditionalLearnedDetailReadinessFull) * 0.5f,
                        normalFrame),
                Is.EqualTo(0.5f).Within(1e-6f));

            var previousAuthority = 0f;
            var previousReadinessAuthority = 0f;
            for (var index = 0; index <= 1000; index++)
            {
                var sparseSilence = index / 1000f;
                var compute = AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailComputeTarget(
                        0, sparseSilence, normalFrame);
                Assert.That(float.IsNaN(compute) || float.IsInfinity(compute),
                    Is.False);
                Assert.That(compute, Is.InRange(0f, 1f));

                var warmth = index / 1000f;
                var authority = AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailAuthorityFromWarmth(
                        warmth, normalFrame);
                Assert.That(float.IsNaN(authority) ||
                            float.IsInfinity(authority), Is.False);
                Assert.That(authority, Is.InRange(0f, 1f));
                Assert.That(authority + 1e-7f,
                    Is.GreaterThanOrEqualTo(previousAuthority),
                    "Authority must be monotone in observer warmth.");
                previousAuthority = authority;

                var readinessAuthority = AdvancedVisemeAnimatorBuilder
                    .ConditionalLearnedDetailAuthorityFromReadiness(
                        warmth, normalFrame);
                Assert.That(float.IsNaN(readinessAuthority) ||
                            float.IsInfinity(readinessAuthority), Is.False);
                Assert.That(readinessAuthority, Is.InRange(0f, 1f));
                Assert.That(readinessAuthority + 1e-7f,
                    Is.GreaterThanOrEqualTo(previousReadinessAuthority),
                    "Authority must be monotone in model readiness.");
                previousReadinessAuthority = readinessAuthority;
            }

            float WarmFor(float framesPerSecond, float seconds)
            {
                var warmth = 0f;
                var deltaTime = 1f / framesPerSecond;
                var steps = Mathf.RoundToInt(seconds * framesPerSecond);
                for (var step = 0; step < steps; step++)
                {
                    var alpha = AdvancedVisemeMath.Alpha(
                        deltaTime,
                        AdvancedVisemeAnimatorBuilder
                            .ConditionalLearnedDetailWarmthSeconds);
                    warmth += alpha * (1f - warmth);
                }
                return warmth;
            }

            var referenceWarmth = WarmFor(60f, 0.25f);
            foreach (var frameRate in new[] { 15f, 30f, 60f, 90f, 144f })
                Assert.That(WarmFor(frameRate, 0.25f),
                    Is.EqualTo(referenceWarmth).Within(0.025f),
                    "The warmth pole must remain frame-rate-correct.");
        }

        [Test]
        public void ConditionalBetaMathGateWarmsAndPublishesSafeFallbacks()
        {
            var previous = AdvancedVisemeAnimatorBuilder
                .EnableConditionalLearnedDetailSleepForTests;
            GraphFixture fixture = null;
            try
            {
                AdvancedVisemeAnimatorBuilder
                    .EnableConditionalLearnedDetailSleepForTests = true;
                fixture = BuildGraph(
                    AdvancedVisemeTuningMenuSections.All,
                    saveValues: false,
                    createMenu: false,
                    beta: true,
                    tracking: true,
                    tuningSyncMode: AdvancedVisemeTuningSyncMode.LocalOnly,
                    includeSoftPalate: true);

                var controller = fixture.result.controller;
                var internalPrefix = fixture.component.NormalizedPrefix + "/_Internal/";
                var computeName = internalPrefix +
                                  "ConditionalLearnedDetail/Compute";
                var authorityName = internalPrefix +
                                    "ConditionalLearnedDetail/Authority";
                var stageClockName = internalPrefix +
                                     "ConditionalLearnedDetail/StageClock";
                var resetStageClockName = internalPrefix +
                    "ConditionalLearnedDetail/ResetStageClock";
                var frameTimeName = internalPrefix + "FrameTime";
                foreach (var name in new[] { computeName, authorityName })
                {
                    var parameter = controller.parameters.Single(candidate =>
                        candidate.name == name);
                    Assert.That(parameter.type,
                        Is.EqualTo(AnimatorControllerParameterType.Float));
                    Assert.That(parameter.defaultFloat,
                        Is.EqualTo(1f).Within(1e-6f),
                        "Hot initialization must match the default Active state.");
                }
                Assert.That(controller.parameters.Any(parameter =>
                    parameter.name.EndsWith(
                        "/ConditionalLearnedDetail/ResetMode",
                        StringComparison.Ordinal)), Is.False,
                    "Reset must not add a selector parameter to the active math path.");

                var hasLegacyConditionalLayer = controller.layers.Any(layer =>
                    layer.name.IndexOf(
                        "Conditional Beta", StringComparison.Ordinal) >= 0 ||
                    layer.name.IndexOf(
                        "Observer Reset", StringComparison.Ordinal) >= 0);
                if (!hasLegacyConditionalLayer)
                {
                    Assert.That(controller.layers.Any(layer =>
                        layer.name.IndexOf(
                            "Observer Reset", StringComparison.Ordinal) >= 0),
                        Is.False,
                        "The WDon-safe candidate must contain no reset state layer.");
                    Assert.That(controller.parameters.Any(parameter =>
                        parameter.name == stageClockName ||
                        parameter.name == resetStageClockName), Is.False,
                        "Pure Math control needs no state timing clocks.");

                    var warmthName = internalPrefix +
                        "ConditionalLearnedDetail/Warmth";
                    foreach (var name in new[]
                             {
                                 computeName, authorityName, warmthName
                             })
                    {
                        var parameter = controller.parameters.Single(candidate =>
                            candidate.name == name);
                        Assert.That(parameter.type,
                            Is.EqualTo(AnimatorControllerParameterType.Float));
                        Assert.That(parameter.defaultFloat,
                            Is.EqualTo(1f).Within(1e-6f),
                            "Layer-free control must initialize hot.");
                    }

                    var conditionalDrivers = controller.layers
                        .SelectMany(layer => layer.stateMachine.states)
                        .SelectMany(child => child.state.behaviours)
                        .OfType<VRC_AvatarParameterDriver>()
                        .Where(driver => driver.parameters.Any(parameter =>
                            (parameter.name ?? string.Empty).IndexOf(
                                "Conditional",
                                StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (parameter.source ?? string.Empty).IndexOf(
                                "Conditional",
                                StringComparison.OrdinalIgnoreCase) >= 0))
                        .ToArray();
                    Assert.That(conditionalDrivers, Is.Empty,
                        "The Math-root gate must not depend on state behaviours.");

                    var mathLayer = controller.layers.Single(layer =>
                        layer.name == "YUCP AVR Math");
                    var mathRoot = mathLayer.stateMachine.defaultState.motion as
                        BlendTree;
                    Assert.That(mathRoot, Is.Not.Null);
                    Assert.That(UsesNormalizedBlendValues(mathRoot), Is.False);
                    var trees = DescendantBlendTrees(mathRoot).ToArray();
                    var sparseFastSilence = internalPrefix +
                        "Viseme/0/SparseFast";
                    var sparseEvidence = trees.Single(tree =>
                        tree.blendParameter == sparseFastSilence &&
                        WritesParameter(tree, computeName));
                    Assert.That(WritesConstantParameter(
                        sparseEvidence, computeName, 0f), Is.True,
                        "Sparse evidence must retain an exact idle cull endpoint.");
                    Assert.That(WritesConstantParameter(
                        sparseEvidence, computeName, 1f), Is.True,
                        "Sparse evidence must retain an exact active endpoint.");
                    var hardWake = trees.Single(tree =>
                        tree.name ==
                        "Conditional learned-detail hard-speech wake");
                    Assert.That(hardWake.blendParameter,
                        Is.EqualTo(internalPrefix + "Viseme/Index"));
                    Assert.That(WritesParameter(hardWake, computeName), Is.True);
                    var computeByRate = trees.Single(tree =>
                        tree.name ==
                        "Conditional learned-detail low-FPS compute bypass");
                    Assert.That(computeByRate.blendParameter,
                        Is.EqualTo(frameTimeName));
                    Assert.That(WritesParameter(computeByRate, computeName),
                        Is.True);

                    var warmthPole = trees.Where(tree =>
                            WritesParameter(tree, warmthName) &&
                            UsesParameter(tree, computeName) &&
                            UsesParameter(tree, warmthName))
                        .OrderBy(tree => DescendantBlendTrees(tree).Count())
                        .First();
                    Assert.That(warmthPole, Is.Not.Null,
                        "Warmth must be one recurrent pole toward Compute.");
                    var authorityByRate = trees.Single(tree =>
                        tree.name ==
                        "Conditional learned-detail low-FPS authority bypass");
                    Assert.That(authorityByRate.blendParameter,
                        Is.EqualTo(frameTimeName));
                    Assert.That(WritesParameter(
                        authorityByRate, authorityName), Is.True);

                    var gatedInference = mathRoot.children.Where(child =>
                        child.directBlendParameter == computeName).ToArray();
                    Assert.That(gatedInference, Has.Length.EqualTo(1));
                    Assert.That(gatedInference[0].motion, Is.TypeOf<BlendTree>());
                    Assert.That(gatedInference[0].motion.name,
                        Is.EqualTo("Conditional learned inference compute"));
                    var retentionState = internalPrefix +
                        "BetaCoarticulation/RetentionState/Jaw/0";
                    Assert.That(trees.Any(tree =>
                        tree.blendParameter == computeName &&
                        WritesParameter(tree, retentionState)), Is.True,
                        "Beta context must select its exact idle equilibrium by C.");
                    Assert.That(UsesParameter(mathRoot, authorityName), Is.True);
                    return;
                }
                Assert.That(hasLegacyConditionalLayer, Is.False,
                    "Legacy conditional state layers must never be generated.");
            }
            finally
            {
                fixture?.Dispose();
                AdvancedVisemeAnimatorBuilder
                    .EnableConditionalLearnedDetailSleepForTests = previous;
            }
        }

        [Test]
        public void ConditionalImmediateAuthoritySharesComputeAndDropsWarmth()
        {
            var previousSleep = AdvancedVisemeAnimatorBuilder
                .EnableConditionalLearnedDetailSleepForTests;
            var previousImmediate = AdvancedVisemeAnimatorBuilder
                .UseImmediateConditionalLearnedDetailAuthorityForTests;
            GraphFixture fixture = null;
            try
            {
                AdvancedVisemeAnimatorBuilder
                    .EnableConditionalLearnedDetailSleepForTests = true;
                AdvancedVisemeAnimatorBuilder
                    .UseImmediateConditionalLearnedDetailAuthorityForTests = true;
                fixture = BuildGraph(
                    AdvancedVisemeTuningMenuSections.All,
                    saveValues: false,
                    createMenu: false,
                    beta: true,
                    tracking: true,
                    tuningSyncMode: AdvancedVisemeTuningSyncMode.LocalOnly,
                    includeSoftPalate: true);

                var controller = fixture.result.controller;
                var internalPrefix = fixture.component.NormalizedPrefix +
                                     "/_Internal/ConditionalLearnedDetail/";
                var compute = internalPrefix + "Compute";
                Assert.That(controller.parameters.Count(parameter =>
                    parameter.name == compute), Is.EqualTo(1));
                foreach (var removed in new[]
                         {
                             "Authority", "Warmth", "WarmthAlpha"
                         })
                    Assert.That(controller.parameters.Any(parameter =>
                            parameter.name == internalPrefix + removed),
                        Is.False,
                        $"Immediate authority must not retain {removed}.");

                var mathRoot = controller.layers.Single(layer =>
                    layer.name == "YUCP AVR Math").stateMachine.defaultState
                    .motion as BlendTree;
                Assert.That(mathRoot, Is.Not.Null);
                Assert.That(DescendantBlendTrees(mathRoot).Any(tree =>
                    tree.name.IndexOf(
                        "low-FPS authority", StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False);
                var gatedInference = mathRoot.children.Single(child =>
                    child.directBlendParameter == compute);
                Assert.That(gatedInference.motion.name,
                    Is.EqualTo("Conditional learned inference compute"));
                Assert.That(UsesParameter(mathRoot, compute), Is.True,
                    "The same exact scalar must own compute and publication endpoints.");
            }
            finally
            {
                fixture?.Dispose();
                AdvancedVisemeAnimatorBuilder
                    .EnableConditionalLearnedDetailSleepForTests = previousSleep;
                AdvancedVisemeAnimatorBuilder
                    .UseImmediateConditionalLearnedDetailAuthorityForTests =
                    previousImmediate;
            }
        }

        [Test]
        public void ConditionalModelMatchedReadinessReusesLearnedObserverAlpha()
        {
            var previousSleep = AdvancedVisemeAnimatorBuilder
                .EnableConditionalLearnedDetailSleepForTests;
            var previousImmediate = AdvancedVisemeAnimatorBuilder
                .UseImmediateConditionalLearnedDetailAuthorityForTests;
            var previousReadiness = AdvancedVisemeAnimatorBuilder
                .UseModelMatchedConditionalLearnedDetailReadinessForTests;
            GraphFixture fixture = null;
            try
            {
                AdvancedVisemeAnimatorBuilder
                    .EnableConditionalLearnedDetailSleepForTests = true;
                AdvancedVisemeAnimatorBuilder
                    .UseImmediateConditionalLearnedDetailAuthorityForTests = false;
                AdvancedVisemeAnimatorBuilder
                    .UseModelMatchedConditionalLearnedDetailReadinessForTests = true;
                fixture = BuildGraph(
                    AdvancedVisemeTuningMenuSections.All,
                    saveValues: false,
                    createMenu: false,
                    beta: true,
                    tracking: true,
                    tuningSyncMode: AdvancedVisemeTuningSyncMode.LocalOnly,
                    includeSoftPalate: true);

                var controller = fixture.result.controller;
                var prefix = fixture.component.NormalizedPrefix + "/_Internal/";
                var conditional = prefix + "ConditionalLearnedDetail/";
                var compute = conditional + "Compute";
                var authority = conditional + "Authority";
                var readinessFast = conditional + "ReadinessFast";
                var readiness = conditional + "Readiness";
                foreach (var name in new[]
                         {
                             compute, authority, readinessFast, readiness
                         })
                    Assert.That(controller.parameters.Count(parameter =>
                        parameter.name == name), Is.EqualTo(1), name);
                foreach (var removed in new[] { "Warmth", "WarmthAlpha" })
                    Assert.That(controller.parameters.Any(parameter =>
                            parameter.name == conditional + removed),
                        Is.False);
                var mathRoot = controller.layers.Single(layer =>
                    layer.name == "YUCP AVR Math").stateMachine.defaultState
                    .motion as BlendTree;
                Assert.That(mathRoot, Is.Not.Null);
                var trees = DescendantBlendTrees(mathRoot).ToArray();
                Assert.That(trees.Any(tree =>
                    WritesParameter(tree, readinessFast) &&
                    UsesParameter(tree, compute)), Is.True);
                Assert.That(trees.Any(tree =>
                    WritesParameter(tree, readiness) &&
                    UsesParameter(tree, readinessFast)), Is.True);
                var readinessObserver = trees.Single(tree =>
                    tree.name == "Vector blend by " + prefix +
                    "ConditionalLearnedDetail/ReadinessAlpha" &&
                    WritesParameter(tree, readinessFast) &&
                    WritesParameter(tree, readiness));
                Assert.That(readinessObserver.blendParameter,
                    Is.EqualTo(prefix +
                        "ConditionalLearnedDetail/ReadinessAlpha"),
                    "Readiness must use its fixed 24 ms model pole.");
                Assert.That(controller.parameters.Count(parameter =>
                    parameter.name == prefix +
                    "ConditionalLearnedDetail/ReadinessAlpha"), Is.EqualTo(1));
                Assert.That(trees.Any(tree => WritesParameter(
                    tree,
                    readinessObserver.blendParameter)), Is.True,
                    "The readiness alpha must have a frame-time writer.");
                var lowFpsAuthority = trees.Single(tree =>
                    tree.name ==
                    "Conditional learned-detail low-FPS readiness authority bypass");
                Assert.That(WritesParameter(lowFpsAuthority, authority), Is.True);
                Assert.That(controller.layers.Any(layer =>
                    layer.name.StartsWith(
                        "YUCP AVR Conditional", StringComparison.Ordinal)),
                    Is.False);
            }
            finally
            {
                fixture?.Dispose();
                AdvancedVisemeAnimatorBuilder
                    .EnableConditionalLearnedDetailSleepForTests = previousSleep;
                AdvancedVisemeAnimatorBuilder
                    .UseImmediateConditionalLearnedDetailAuthorityForTests =
                    previousImmediate;
                AdvancedVisemeAnimatorBuilder
                    .UseModelMatchedConditionalLearnedDetailReadinessForTests =
                    previousReadiness;
            }
        }

        [Test]
        public void ConditionalModelMatchedReadinessRequiresFaceInference()
        {
            var previousImmediate = AdvancedVisemeAnimatorBuilder
                .UseImmediateConditionalLearnedDetailAuthorityForTests;
            var previousReadiness = AdvancedVisemeAnimatorBuilder
                .UseModelMatchedConditionalLearnedDetailReadinessForTests;
            try
            {
                AdvancedVisemeAnimatorBuilder
                    .UseImmediateConditionalLearnedDetailAuthorityForTests =
                    false;
                AdvancedVisemeAnimatorBuilder
                    .UseModelMatchedConditionalLearnedDetailReadinessForTests =
                    true;
                MethodInfo method = typeof(AdvancedVisemeAnimatorBuilder)
                    .GetMethod(
                        "ShouldDelayConditionalLearnedDetailAuthority",
                        BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null);

                Assert.That(
                    method.Invoke(null, new object[] { false }),
                    Is.False,
                    "A Beta graph without face inference needs no readiness.");
                Assert.That(
                    method.Invoke(null, new object[] { true }),
                    Is.True,
                    "Face inference needs the model-matched readiness delay.");
            }
            finally
            {
                AdvancedVisemeAnimatorBuilder
                    .UseImmediateConditionalLearnedDetailAuthorityForTests =
                    previousImmediate;
                AdvancedVisemeAnimatorBuilder
                    .UseModelMatchedConditionalLearnedDetailReadinessForTests =
                    previousReadiness;
            }
        }

        [Test]
        public void ConditionalLearnedDetailSleepCanKeepCoreBetaContextHot()
        {
            var previousSleep = AdvancedVisemeAnimatorBuilder
                .EnableConditionalLearnedDetailSleepForTests;
            var previousImmediate = AdvancedVisemeAnimatorBuilder
                .UseImmediateConditionalLearnedDetailAuthorityForTests;
            var previousReadiness = AdvancedVisemeAnimatorBuilder
                .UseModelMatchedConditionalLearnedDetailReadinessForTests;
            var previousHotCore = AdvancedVisemeAnimatorBuilder
                .KeepConditionalBetaContextAlwaysHotForTests;
            GraphFixture fixture = null;
            try
            {
                AdvancedVisemeAnimatorBuilder
                    .EnableConditionalLearnedDetailSleepForTests = true;
                AdvancedVisemeAnimatorBuilder
                    .UseImmediateConditionalLearnedDetailAuthorityForTests = false;
                AdvancedVisemeAnimatorBuilder
                    .UseModelMatchedConditionalLearnedDetailReadinessForTests = true;
                AdvancedVisemeAnimatorBuilder
                    .KeepConditionalBetaContextAlwaysHotForTests = true;
                fixture = BuildGraph(
                    AdvancedVisemeTuningMenuSections.All,
                    saveValues: false,
                    createMenu: false,
                    beta: true,
                    tracking: true,
                    tuningSyncMode: AdvancedVisemeTuningSyncMode.LocalOnly,
                    includeSoftPalate: true);

                var controller = fixture.result.controller;
                var prefix = fixture.component.NormalizedPrefix + "/_Internal/";
                var compute = prefix +
                              "ConditionalLearnedDetail/Compute";
                var mathRoot = controller.layers.Single(layer =>
                    layer.name == "YUCP AVR Math").stateMachine.defaultState
                    .motion as BlendTree;
                Assert.That(mathRoot, Is.Not.Null);
                var gatedInference = mathRoot.children.Single(child =>
                    child.directBlendParameter == compute);
                Assert.That(gatedInference.motion.name,
                    Is.EqualTo("Conditional learned inference compute"));

                var trees = DescendantBlendTrees(mathRoot).ToArray();
                Assert.That(trees.Any(tree => tree.name.IndexOf(
                        "Conditional Beta context",
                        StringComparison.Ordinal) >= 0), Is.False,
                    "The exact core coarticulation observer must not be gated.");
                Assert.That(controller.layers.Any(layer =>
                    layer.name.StartsWith(
                        "YUCP AVR Conditional", StringComparison.Ordinal)),
                    Is.False);
            }
            finally
            {
                fixture?.Dispose();
                AdvancedVisemeAnimatorBuilder
                    .EnableConditionalLearnedDetailSleepForTests = previousSleep;
                AdvancedVisemeAnimatorBuilder
                    .UseImmediateConditionalLearnedDetailAuthorityForTests =
                    previousImmediate;
                AdvancedVisemeAnimatorBuilder
                    .UseModelMatchedConditionalLearnedDetailReadinessForTests =
                    previousReadiness;
                AdvancedVisemeAnimatorBuilder
                    .KeepConditionalBetaContextAlwaysHotForTests = previousHotCore;
            }
        }

        [Test]
        public void BalancedSupportReductionUsesUnitIdentityAndLogarithmicDepth()
        {
            var previous = AdvancedVisemeAnimatorBuilder
                .UseBalancedNeutralSupportReductionForTests;
            GraphFixture fixture = null;
            try
            {
                AdvancedVisemeAnimatorBuilder
                    .UseBalancedNeutralSupportReductionForTests = true;
                fixture = BuildGraph(
                    AdvancedVisemeTuningMenuSections.All,
                    saveValues: false,
                    createMenu: false,
                    beta: true,
                    tracking: true,
                    tuningSyncMode: AdvancedVisemeTuningSyncMode.LocalOnly,
                    includeSoftPalate: true);

                var controller = fixture.result.controller;
                var balanced = controller.parameters.Where(parameter =>
                        parameter.name.Contains(
                            "/OodConfidence/Raw/Balanced/",
                            StringComparison.Ordinal))
                    .ToArray();
                Assert.That(balanced, Is.Not.Empty);
                Assert.That(balanced.All(parameter =>
                        parameter.type == AnimatorControllerParameterType.Float &&
                        Mathf.Abs(parameter.defaultFloat - 1f) <= 1e-6f),
                    Is.True,
                    "Every min intermediate must initialize to the unit-confidence identity.");

                var sequential = controller.parameters.Where(parameter =>
                    parameter.name.Contains(
                        "/OodConfidence/Raw/", StringComparison.Ordinal) &&
                    !parameter.name.Contains(
                        "/OodConfidence/Raw/Balanced/", StringComparison.Ordinal) &&
                    int.TryParse(parameter.name.Substring(
                            parameter.name.LastIndexOf('/') + 1), out _))
                    .ToArray();
                Assert.That(sequential, Is.Empty,
                    "The serial n-1 publication chain must be absent.");

                var maximumDepth = balanced.Select(parameter =>
                    {
                        var marker = parameter.name.IndexOf(
                            "/Balanced/", StringComparison.Ordinal) +
                                     "/Balanced/".Length;
                        var separator = parameter.name.IndexOf('/', marker);
                        Assert.That(separator, Is.GreaterThan(marker),
                            "Unexpected balanced-reduction parameter name: " +
                            parameter.name);
                        return int.Parse(parameter.name.Substring(
                            marker, separator - marker));
                    })
                    .Max();
                Assert.That(maximumDepth, Is.LessThanOrEqualTo(3),
                    "Twelve confidence factors require at most four reduction " +
                    "levels (zero-based index 3).");
            }
            finally
            {
                fixture?.Dispose();
                AdvancedVisemeAnimatorBuilder
                    .UseBalancedNeutralSupportReductionForTests = previous;
            }
        }

        private static GraphFixture BuildGraph(
            AdvancedVisemeTuningMenuSections sections,
            bool saveValues,
            bool createMenu,
            bool beta,
            bool tracking,
            AdvancedVisemeTuningSyncMode tuningSyncMode =
                AdvancedVisemeTuningSyncMode.CompactSynced,
            bool includeSoftPalate = false)
        {
            var fixture = new GraphFixture();
            fixture.folderName = "__YUCP_AVR_TuningGraph_" + Guid.NewGuid().ToString("N");
            fixture.folder = "Assets/" + fixture.folderName;
            fixture.controllerPath = fixture.folder + "/AdvancedViseme.controller";
            AssetDatabase.CreateFolder("Assets", fixture.folderName);
            fixture.root = new GameObject("Advanced Viseme Tuning Graph Test");
            fixture.profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            fixture.calibrationMesh = new Mesh { name = "Synthetic tuning calibration" };
            fixture.component = fixture.root.AddComponent<AdvancedVisemeReconstructorData>();
            fixture.component.mouthOwnership = AdvancedVisemeMouthOwnership.DriveLowerFace;
            fixture.component.reconstructionMode = beta
                ? AdvancedVisemeReconstructionMode.BetaCoarticulation
                : AdvancedVisemeReconstructionMode.Normal;
            fixture.component.trackingInputs = tracking
                ? AdvancedVisemeTrackingInputs.FullTongue18
                : AdvancedVisemeTrackingInputs.Disabled;
            fixture.component.createTuningMenu = createMenu;
            fixture.component.saveTuningValues = saveValues;
            fixture.component.tuningMenuSections = sections;
            fixture.component.tuningSyncMode = tuningSyncMode;

            fixture.result = AdvancedVisemeAnimatorBuilder.Build(
                new AdvancedVisemeAnimatorBuilder.Request
                {
                    controllerPath = fixture.controllerPath,
                    parametersPath = fixture.folder + "/Parameters.asset",
                    component = fixture.component,
                    profile = fixture.profile,
                    trackingPrefix = "YUCP/TestTracking",
                    effectiveTrackingInputs = tracking
                        ? AdvancedVisemeTrackingInputs.FullTongue18
                        : AdvancedVisemeTrackingInputs.Disabled,
                    reuseExistingTracking = false,
                    trackingActiveParameter = "YUCP/TestTracking/LipTrackingActive",
                    trackingActiveAnimatorType = AnimatorControllerParameterType.Float,
                    trackingActiveDefault = tracking ? 1f : 0f,
                    trackingParameterNames = new Dictionary<AdvancedVisemeArticulator, string>(),
                    auxiliaryTrackingParameterNames = includeSoftPalate
                        ? new Dictionary<string, string>
                        {
                            {
                                "SoftPalateClose",
                                "YUCP/TestTracking/SoftPalateClose"
                            }
                        }
                        : new Dictionary<string, string>(),
                    sourceVisemeBlendShapes = new string[VisemeReconstructionProfile.VisemeCount],
                    calibration = new MeshUtils.AdvancedVisemeMeshCalibrator.Result
                    {
                        mesh = fixture.calibrationMesh,
                        coefficients = new float[VisemeReconstructionProfile.VisemeCount, 0],
                        residualBlendShapeNames = new string[VisemeReconstructionProfile.VisemeCount],
                        hiddenPhoneResidualBlendShapeName = "SyntheticHiddenPhoneResidual",
                        hiddenPhoneResidualNegativeBlendShapeName = includeSoftPalate
                            ? "SyntheticHiddenPhoneResidualNegative"
                            : null
                    },
                    calibrationBasis = Array.Empty<MeshUtils.AdvancedVisemeMeshCalibrator.BasisInput>(),
                    resolvedBlendShapes = new Dictionary<AdvancedVisemeArticulator, string>(),
                    externalPoses = new Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose>(),
                    trackingEnabled = tracking,
                    existingExpressionParameters = new HashSet<string>()
                });
            return fixture;
        }

        private static IEnumerable<VRCExpressionsMenu.Control> RadialControls(
            VRCExpressionsMenu menu)
        {
            if (menu == null || menu.controls == null) yield break;
            foreach (var control in menu.controls)
            {
                if (control.type == VRCExpressionsMenu.Control.ControlType.RadialPuppet)
                    yield return control;
                if (control.type != VRCExpressionsMenu.Control.ControlType.SubMenu ||
                    control.subMenu == null)
                    continue;
                foreach (var child in RadialControls(control.subMenu)) yield return child;
            }
        }

        private static bool UsesParameter(Motion motion, string parameter)
        {
            if (!(motion is BlendTree tree)) return false;
            return tree.blendParameter == parameter ||
                   tree.blendParameterY == parameter ||
                   tree.children.Any(child =>
                       child.directBlendParameter == parameter ||
                       UsesParameter(child.motion, parameter));
        }

        private static bool DirectlyUsesParameter(
            BlendTree tree,
            string parameter)
        {
            return tree != null &&
                   (tree.blendParameter == parameter ||
                    tree.blendParameterY == parameter ||
                    tree.children.Any(child =>
                        child.directBlendParameter == parameter));
        }

        private static IEnumerable<BlendTree> DescendantBlendTrees(Motion motion)
        {
            if (!(motion is BlendTree tree)) yield break;
            yield return tree;
            foreach (var child in tree.children)
            foreach (var descendant in DescendantBlendTrees(child.motion))
                yield return descendant;
        }

        private static IEnumerable<AnimationClip> DescendantAnimationClips(
            Motion motion)
        {
            if (motion is AnimationClip clip)
            {
                yield return clip;
                yield break;
            }
            if (!(motion is BlendTree tree)) yield break;
            foreach (var child in tree.children)
            foreach (var descendant in DescendantAnimationClips(child.motion))
                yield return descendant;
        }

        private static bool WritesParameter(Motion motion, string parameter)
        {
            if (motion is AnimationClip clip)
                return AnimationUtility.GetCurveBindings(clip).Any(binding =>
                    binding.type == typeof(Animator) &&
                    binding.propertyName == parameter);
            return motion is BlendTree tree &&
                   tree.children.Any(child => WritesParameter(child.motion, parameter));
        }

        private static bool WritesConstantParameter(
            Motion motion,
            string parameter,
            float value)
        {
            if (motion is AnimationClip clip)
            {
                var binding = AnimationUtility.GetCurveBindings(clip)
                    .FirstOrDefault(candidate =>
                        candidate.type == typeof(Animator) &&
                        candidate.propertyName == parameter);
                if (string.IsNullOrEmpty(binding.propertyName)) return false;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                return curve != null && curve.keys.Length > 0 &&
                       curve.keys.All(key => Mathf.Approximately(key.value, value));
            }
            return motion is BlendTree tree && tree.children.Any(child =>
                WritesConstantParameter(child.motion, parameter, value));
        }

        private static bool UsesNormalizedBlendValues(BlendTree tree)
        {
            var serialized = new SerializedObject(tree);
            var normalized = serialized.FindProperty("m_NormalizedBlendValues");
            Assert.That(normalized, Is.Not.Null);
            return normalized.boolValue;
        }

        private static bool IsSigned(AdvancedVisemeArticulator articulator)
        {
            return articulator == AdvancedVisemeArticulator.SmileSad ||
                   articulator == AdvancedVisemeArticulator.JawX ||
                   articulator == AdvancedVisemeArticulator.JawZ ||
                   articulator == AdvancedVisemeArticulator.MouthX ||
                   articulator == AdvancedVisemeArticulator.TongueY ||
                   articulator == AdvancedVisemeArticulator.TongueX ||
                   articulator == AdvancedVisemeArticulator.TongueArchY ||
                   articulator == AdvancedVisemeArticulator.TongueShape;
        }

        private static float UnitField(VisemeReconstructionProfile profile, string name)
        {
            return (float)typeof(VisemeReconstructionProfile).GetField(name).GetValue(profile);
        }

        private static void SetUnitField(
            VisemeReconstructionProfile profile,
            string name,
            float value)
        {
            typeof(VisemeReconstructionProfile).GetField(name).SetValue(profile, value);
        }

        private static void SetPrivateInt(object target, string name, int value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        private static void InvokeOnValidate(object target)
        {
            target.GetType().GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(target, null);
        }

        private sealed class GraphFixture : IDisposable
        {
            public string folderName;
            public string folder;
            public string controllerPath;
            public GameObject root;
            public VisemeReconstructionProfile profile;
            public Mesh calibrationMesh;
            public AdvancedVisemeReconstructorData component;
            public AdvancedVisemeAnimatorBuilder.Result result;

            public void Dispose()
            {
                AssetDatabase.DeleteAsset(folder);
                if (calibrationMesh != null) UnityEngine.Object.DestroyImmediate(calibrationMesh);
                if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
