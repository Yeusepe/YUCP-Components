using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

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
        public void VersionSevenProfileDefaultsAreNeutralAndBackwardCompatible()
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
                for (var index = 0; index < VersionSixUnitFields.Length; index++)
                    SetUnitField(profile, VersionSixUnitFields[index], index % 2 == 0 ? -2f : 3f);

                InvokeOnValidate(profile);

                Assert.That(profile.trackingAcquireResponseSeconds, Is.InRange(0.005f, 0.1f));
                Assert.That(profile.speechHangoverSeconds, Is.InRange(0.04f, 0.4f));
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
                Is.EqualTo(7), "Speech must retain one free slot in VRChat's eight-control menu.");
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
                        : control == AdvancedVisemeTuningControl.QuietMotion ? 0.55f : 1f;
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
                Assert.That(component.tuningMenuPath, Is.EqualTo("YUCP/Viseme Settings"));
                Assert.That(component.tuningMenuSections, Is.EqualTo(AdvancedVisemeTuningMenuSections.All));
                foreach (var control in AdvancedVisemeTuning.Controls)
                {
                    Assert.That(component.TuningParameterName(control), Is.EqualTo(
                        "Demo/AdvancedViseme/Tuning/" + AdvancedVisemeTuning.ParameterSuffix(control)));
                }

                component.tuningMenuPath = " /Avatar/Voice/ ";
                component.tuningMenuSections = AdvancedVisemeTuningMenuSections.Speech |
                                               (AdvancedVisemeTuningMenuSections)(1 << 12);
                InvokeOnValidate(component);
                Assert.That(component.tuningMenuPath, Is.EqualTo("Avatar/Voice"));
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
                Assert.That(root.controls, Has.Count.EqualTo(Sections.Length));
                Assert.That(root.controls, Has.All.Matches<VRCExpressionsMenu.Control>(control =>
                    control.type == VRCExpressionsMenu.Control.ControlType.SubMenu &&
                    control.subMenu != null));

                foreach (var section in Sections)
                {
                    var sectionControl = root.controls.Single(control =>
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
                Assert.That(menuAssets, Has.Length.EqualTo(1 + Sections.Length));
                Assert.That(menuAssets, Has.All.Matches<VRCExpressionsMenu>(menu =>
                    menu.controls != null && menu.controls.Count <= 8));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
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

                for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
                {
                    var raw = internalPrefix + $"/Viseme/{viseme}/Raw";
                    var fast = internalPrefix + $"/Viseme/{viseme}/Fast";
                    var gateName = $"Hold {fast} across transient sil";
                    var gate = trees.SingleOrDefault(tree => tree.name == gateName);
                    var strengthGate = trees.SingleOrDefault(tree =>
                        tree.name == gateName + " (strength)");
                    var historyGate = trees.SingleOrDefault(tree =>
                        tree.name == gateName + " (history)");
                    Assert.That(gate, Is.Not.Null, gateName);
                    Assert.That(gate.blendParameter, Is.EqualTo(visemeIndex));
                    Assert.That(strengthGate, Is.Not.Null, gateName + " strength");
                    Assert.That(strengthGate.blendParameter, Is.EqualTo(stability));
                    Assert.That(strengthGate.children.Select(child => child.threshold),
                        Is.EquivalentTo(new[] { 0f, 0.5f, 1f }));
                    Assert.That(historyGate, Is.Not.Null, gateName + " history");
                    Assert.That(historyGate.blendParameter, Is.EqualTo(history));
                    Assert.That(historyGate.children.Select(child => child.threshold),
                        Is.EquivalentTo(new[]
                        {
                            AdvancedVisemeMath.SpeechHistoryHoldStart,
                            AdvancedVisemeMath.SpeechHistoryHoldFull
                        }));
                    Assert.That(trees.Any(tree => tree.name == $"Smooth {fast} toward {raw}"),
                        Is.True, $"Viseme {viseme} bypasses the release observer.");
                }

                var activityTree = trees.SingleOrDefault(tree =>
                    tree.name == $"Speech activity with transient-sil hold -> {talking}");
                Assert.That(activityTree, Is.Not.Null);
                Assert.That(activityTree.blendParameter, Is.EqualTo(visemeIndex));

                var voiceGainBase = internalPrefix + "/Voice/GainBase";
                var gainGateName = $"Hold {voiceGainBase} gain across transient sil";
                var gainGate = trees.SingleOrDefault(tree => tree.name == gainGateName);
                var gainHistoryGate = trees.SingleOrDefault(tree =>
                    tree.name == gainGateName + " (history)");
                Assert.That(gainGate, Is.Not.Null);
                Assert.That(gainGate.blendParameter, Is.EqualTo(visemeIndex));
                Assert.That(gainHistoryGate, Is.Not.Null);
                Assert.That(gainHistoryGate.blendParameter, Is.EqualTo(history));
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

                var contextGates = trees.Where(tree =>
                    tree.name.StartsWith("Hold " + internalPrefix +
                                         "/BetaCoarticulation/Context/", StringComparison.Ordinal) &&
                    !tree.name.EndsWith(" (history)", StringComparison.Ordinal) &&
                    !tree.name.EndsWith(" (strength)", StringComparison.Ordinal)).ToArray();
                var distinctDecayCount = Enumerable.Range(
                        0, AdvancedVisemeTransitionRetention.GroupCount)
                    .Select(index => Mathf.RoundToInt(
                        AdvancedVisemeCoarticulationModel.DecaySeconds(
                            (AdvancedVisemeArticulatorGroup)index) * 1000000f))
                    .Distinct().Count();
                Assert.That(contextGates.Length, Is.EqualTo(
                    distinctDecayCount * VisemeReconstructionProfile.VisemeCount));
                Assert.That(contextGates.All(tree => tree.blendParameter == visemeIndex), Is.True);

                var leadGates = trees.Where(tree =>
                    tree.name.StartsWith("Hold " + internalPrefix +
                                         "/BetaCoarticulation/", StringComparison.Ordinal) &&
                    tree.name.Contains(" coarticulation across transient sil") &&
                    !tree.name.EndsWith(" (history)", StringComparison.Ordinal) &&
                    !tree.name.EndsWith(" (strength)", StringComparison.Ordinal)).ToArray();
                Assert.That(leadGates.Length, Is.EqualTo(
                    (AdvancedVisemeTransitionRetention.GroupCount + 1) *
                    VisemeReconstructionProfile.VisemeCount));
                Assert.That(leadGates.All(tree => tree.blendParameter == visemeIndex), Is.True);
            }
            finally
            {
                fixture.Dispose();
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
            bool tracking)
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

        private static bool UsesParameter(BlendTree tree, string parameter)
        {
            return tree.blendParameter == parameter ||
                   tree.blendParameterY == parameter ||
                   tree.children.Any(child => child.directBlendParameter == parameter);
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
