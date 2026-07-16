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
        public void VersionEightProfileDefaultsAreNeutralAndBackwardCompatible()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                Assert.That(profile.visemeResponseSeconds, Is.EqualTo(0.024f).Within(1e-6f));
                Assert.That(profile.speechHangoverSeconds, Is.EqualTo(0.16f).Within(1e-6f));
                Assert.That(profile.localTrackingResponseSeconds, Is.EqualTo(0.018f).Within(1e-6f));
                Assert.That(profile.remoteTrackingResponseSeconds, Is.EqualTo(0.065f).Within(1e-6f));
                Assert.That(profile.trackingAcquireResponseSeconds, Is.EqualTo(0.035f).Within(1e-6f));
                Assert.That(profile.trackingBlendResponseSeconds, Is.EqualTo(0.12f).Within(1e-6f));
                Assert.That(profile.quietSpeechFloor, Is.EqualTo(0.55f).Within(1e-6f));
                Assert.That(profile.voiceNoiseFloor, Is.EqualTo(0.05f).Within(1e-6f));
                Assert.That(profile.voiceFullScale, Is.EqualTo(0.25f).Within(1e-6f));
                Assert.That(profile.BetaCoarticulationStrength, Is.EqualTo(1f).Within(1e-6f));
                Assert.That(profile.speechLiveliness, Is.EqualTo(0.5f).Within(1e-6f));

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
        public void VersionEightMigrationInitializesSpeechLivelinessOnlyOnce()
        {
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            try
            {
                SetPrivateInt(profile, "defaultsVersion", 7);
                profile.speechLiveliness = 0f;
                profile.EnsureDefaults();
                Assert.That(profile.speechLiveliness, Is.EqualTo(0.5f).Within(1e-6f));

                profile.speechLiveliness = 0f;
                profile.EnsureDefaults();
                Assert.That(profile.speechLiveliness, Is.Zero.Within(1e-6f),
                    "Zero is a deliberate legacy-response preference after migration.");
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
        public void BetaCoarticulationGatesBothRawSilenceIngressPaths()
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
                var gate = trees.SingleOrDefault(tree =>
                    tree.name == "Vector transient-silence hold");
                Assert.That(gate, Is.Not.Null);
                Assert.That(gate.blendParameter, Is.EqualTo(visemeIndex));
                var distinctDecayCount = Enumerable.Range(
                        0, AdvancedVisemeTransitionRetention.GroupCount)
                    .Select(index => Mathf.RoundToInt(
                        AdvancedVisemeCoarticulationModel.DecaySeconds(
                            (AdvancedVisemeArticulatorGroup)index) * 1000000f))
                    .Distinct().Count();
                var contextWeights = fixture.result.controller.parameters
                    .Select(parameter => parameter.name)
                    .Where(parameter => parameter.StartsWith(
                        internalPrefix + "/BetaCoarticulation/Context/",
                        StringComparison.Ordinal))
                    .Where(parameter => int.TryParse(
                        parameter.Substring(parameter.LastIndexOf('/') + 1), out _))
                    .ToArray();
                Assert.That(contextWeights.Length, Is.EqualTo(
                    distinctDecayCount * VisemeReconstructionProfile.VisemeCount));
                Assert.That(contextWeights.All(parameter => WritesParameter(gate, parameter)),
                    Is.True, "Every retained context lane must pass through the shared router.");

                for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
                {
                    Assert.That(WritesParameter(
                            gate, internalPrefix + $"/Viseme/{viseme}/Fast"), Is.True);
                    Assert.That(WritesParameter(
                            gate, internalPrefix +
                                  $"/BetaCoarticulation/Mean/Viseme/{viseme}/Fast"), Is.True);
                }
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
        public void SpeechLivelinessMakesSpeechOnlyTransitionsEarlierWithoutOvershoot()
        {
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

                Assert.That(renderedNinety, Is.LessThan(slowNinety),
                    $"Speech Liveliness did not improve the 90% transition at {fps} FPS.");
                Assert.That(AdvancedVisemeMath.ApplySpeechLiveliness(
                        slow[10], fast[10], 1f, 1f),
                    Is.EqualTo(slow[10]).Within(1e-7f),
                    "Fully active tracking must recover the exact slow prior.");
            }
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

        private static GraphFixture BuildGraph(
            AdvancedVisemeTuningMenuSections sections,
            bool saveValues,
            bool createMenu,
            bool beta,
            bool tracking,
            AdvancedVisemeTuningSyncMode tuningSyncMode =
                AdvancedVisemeTuningSyncMode.CompactSynced)
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
                    auxiliaryTrackingParameterNames = new Dictionary<string, string>(),
                    sourceVisemeBlendShapes = new string[VisemeReconstructionProfile.VisemeCount],
                    calibration = new MeshUtils.AdvancedVisemeMeshCalibrator.Result
                    {
                        mesh = fixture.calibrationMesh,
                        coefficients = new float[VisemeReconstructionProfile.VisemeCount, 0],
                        residualBlendShapeNames = new string[VisemeReconstructionProfile.VisemeCount],
                        hiddenPhoneResidualBlendShapeName = "SyntheticHiddenPhoneResidual"
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

        private static bool WritesParameter(Motion motion, string parameter)
        {
            if (motion is AnimationClip clip)
                return AnimationUtility.GetCurveBindings(clip).Any(binding =>
                    binding.type == typeof(Animator) &&
                    binding.propertyName == parameter);
            return motion is BlendTree tree &&
                   tree.children.Any(child => WritesParameter(child.motion, parameter));
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
