#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace YUCP.Components.Editor.Tests
{
    /// <summary>
    /// This is intentionally a black-box build test. Unlike the structural
    /// BlendShape Link tests, it invokes every registered SDK preprocess callback
    /// in its real order, including AVR, VRCFury's Full Controller merge, the
    /// installed BlendShape Link builder, and VRCFury's optimizer.
    /// </summary>
    public sealed class AdvancedVisemeVrcFuryPipelineTests
    {
        private const string GeneratedRoot =
            "Assets/YUCP/GeneratedAssets/AdvancedVisemeReconstructor";
        private const string PrimaryPath = "Primary Face";
        private const string Prefix = "YUCP/PipelineParity";
        private const string JawShape = "JawOpen";
        private const string SmileShape = "SmileSad";

        [TearDown]
        public void CloseLeakedFixtureScenes()
        {
            for (var index = SceneManager.sceneCount - 1; index >= 0; index--)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!scene.isLoaded || !string.IsNullOrEmpty(scene.path)) continue;
                var roots = scene.GetRootGameObjects();
                if (roots.Length == 0 || roots.Any(root =>
                        !root.name.StartsWith("AVR Pipeline ", StringComparison.Ordinal)))
                    continue;
                if (EditorSceneManager.IsPreviewScene(scene))
                    EditorSceneManager.ClosePreviewScene(scene);
                else
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void RealPipelinePreservesPrimaryContractAndAddsOneRootTargetOverlay()
        {
            var sourceFolderName = "__YUCP_AVR_PipelineSource_" +
                                   Guid.NewGuid().ToString("N");
            var sourceFolder = "Assets/" + sourceFolderName;
            AssetDatabase.CreateFolder("Assets", sourceFolderName);
            var primarySourcePath = sourceFolder + "/Primary.asset";
            AssetDatabase.CreateAsset(
                CreateFaceMesh("Shared primary source", 0.013f),
                primarySourcePath);
            AssetDatabase.ImportAsset(primarySourcePath);
            var sharedPrimarySource = AssetDatabase.LoadAssetAtPath<Mesh>(primarySourcePath);
            var sourceDependencyBefore = AssetDatabase.GetAssetDependencyHash(primarySourcePath);
            try
            {
                string[] baselinePublicParameters;
                string[] baselinePrimaryCurves;
                string[] baselineExpressionParameters;
                int baselineSyncedBits;
                using (var baseline = new PipelineFixture(
                           withRootLink: false,
                           primarySourceOverride: sharedPrimarySource))
                {
                    var baselineSourceFingerprint = MeshFingerprint(baseline.primarySource);
                    baseline.Build();
                    Assert.That(MeshFingerprint(baseline.primarySource),
                        Is.EqualTo(baselineSourceFingerprint),
                        "The no-link build modified its source face mesh in place.");
                    baselinePublicParameters = PublicParameterFingerprint(baseline.descriptor);
                    baselinePrimaryCurves = PrimaryCurveFingerprint(baseline.FxController);
                    baselineExpressionParameters = ExpressionParameterFingerprint(
                        baseline.descriptor);
                    baselineSyncedBits = SyncedParameterBits(baseline.descriptor);
                    Assert.That(baselineSyncedBits, Is.EqualTo(24),
                        "Toggle-disabled Balanced8 must retain the optimized 24-bit wire contract.");
                }

                using (var linked = new PipelineFixture(
                           withRootLink: true,
                           primarySourceOverride: sharedPrimarySource))
                {
                    var linkedPrimarySourceFingerprint = MeshFingerprint(linked.primarySource);
                    var linkedTargetSourceFingerprint = MeshFingerprint(linked.targetSource);
                    linked.Build();

                    Assert.That(MeshFingerprint(linked.primarySource),
                        Is.EqualTo(linkedPrimarySourceFingerprint),
                        "The linked build modified the primary source mesh in place.");
                    Assert.That(MeshFingerprint(linked.targetSource),
                        Is.EqualTo(linkedTargetSourceFingerprint),
                        "The linked build modified the root target's source mesh in place.");

                    CollectionAssert.AreEqual(
                        baselinePublicParameters,
                        PublicParameterFingerprint(linked.descriptor),
                        "A BlendShape Link must not alter AVR's public parameter contract.");
                    CollectionAssert.AreEqual(
                        baselinePrimaryCurves,
                        PrimaryCurveFingerprint(linked.FxController),
                        "A linked renderer must not rename or rewrite any primary-face animation curve.");
                    CollectionAssert.AreEqual(
                        baselineExpressionParameters,
                        ExpressionParameterFingerprint(linked.descriptor),
                        "A BlendShape Link must not add or alter any expression parameter.");
                    Assert.That(SyncedParameterBits(linked.descriptor),
                        Is.EqualTo(baselineSyncedBits),
                        "Linked residuals must add zero synced parameter bits.");

                    AssertRootTargetOverlay(linked);
                }

                Assert.That(AssetDatabase.GetAssetDependencyHash(primarySourcePath),
                    Is.EqualTo(sourceDependencyBefore),
                    "The real pipeline changed the persistent source mesh asset.");
            }
            finally
            {
                AssetDatabase.DeleteAsset(sourceFolder);
            }
        }

        [Test]
        public void RealPipelinePreservesPureMathConditionalLearnedDetailGate()
        {
            var previous = AdvancedVisemeAnimatorBuilder
                .EnableConditionalLearnedDetailSleepForTests;
            try
            {
                AdvancedVisemeAnimatorBuilder
                    .EnableConditionalLearnedDetailSleepForTests = true;
                using (var fixture = new PipelineFixture(withRootLink: false))
                {
                    fixture.component.reconstructionMode =
                        AdvancedVisemeReconstructionMode.BetaCoarticulation;
                    fixture.component.trackingInputs =
                        AdvancedVisemeTrackingInputs.Balanced8;
                    fixture.Build();

                    var controller = fixture.FxController;
                    var conditionalLayers = controller.layers
                        .Where(layer =>
                            layer.name.IndexOf(
                                "Conditional", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            layer.name.IndexOf(
                                "Observer Reset", StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(layer => layer.name)
                        .ToArray();
                    Assert.That(conditionalLayers, Is.Empty,
                        "The pure-Math gate must remain inside the Math Direct BlendTree " +
                        "after VRCFury merges and optimizes the controller.");

                    var allStates = controller.layers
                        .SelectMany(layer => DescendantStates(layer.stateMachine))
                        .ToArray();
                    var conditionalDrivers = allStates
                        .SelectMany(state => state.behaviours ??
                            Array.Empty<StateMachineBehaviour>())
                        .OfType<VRCAvatarParameterDriver>()
                        .Where(driver => ContainsConditionalControl(driver.name) ||
                                         driver.parameters.Any(parameter =>
                                             ContainsConditionalControl(parameter.name) ||
                                             ContainsConditionalControl(parameter.source)))
                        .ToArray();
                    Assert.That(conditionalDrivers, Is.Empty,
                        "Conditional learned detail must not regain a state-behaviour " +
                        "dependency in the installed VRCFury pipeline.");

                    var internalPrefix = Prefix + "/_Internal/";
                    var sourceCompute = internalPrefix +
                        "ConditionalLearnedDetail/Compute";
                    var sourceAuthority = internalPrefix +
                        "ConditionalLearnedDetail/Authority";
                    var compute = FindPrivateParameter(controller, sourceCompute);
                    var authority = FindPrivateParameter(controller, sourceAuthority);
                    foreach (var parameter in new[] { compute, authority })
                    {
                        Assert.That(parameter.type,
                            Is.EqualTo(AnimatorControllerParameterType.Float));
                        Assert.That(parameter.name,
                            Does.StartWith("VF").And.EndWith(parameter == compute
                                ? sourceCompute
                                : sourceAuthority),
                            "VRCFury must preserve the private parameter identity while " +
                            "giving it a collision-free feature namespace.");
                    }

                    var expressionNames = (fixture.descriptor.expressionParameters?.parameters ??
                                           Array.Empty<VRCExpressionParameters.Parameter>())
                        .Where(parameter => parameter != null)
                        .Select(parameter => parameter.name)
                        .ToArray();
                    foreach (var sourceName in new[] { sourceCompute, sourceAuthority })
                        Assert.That(expressionNames.Any(name =>
                                !string.IsNullOrEmpty(name) &&
                                name.EndsWith(sourceName, StringComparison.Ordinal)),
                            Is.False,
                            "Math feedback controls are private Animator parameters, not " +
                            "avatar expression parameters.");

                    var mathLayer = controller.layers.Single(layer =>
                        layer.name == "YUCP AVR Math");
                    var mathState = mathLayer.stateMachine.defaultState;
                    Assert.That(mathState, Is.Not.Null);
                    Assert.That(mathState.writeDefaultValues, Is.True,
                        "VRCFury Direct BlendTree math must remain Write Defaults On.");
                    var mathRoot = mathState.motion as BlendTree;
                    Assert.That(mathRoot, Is.Not.Null);
                    Assert.That(mathRoot.blendType, Is.EqualTo(BlendTreeType.Direct));

                    var gatedLearnedMotions = DescendantBlendTrees(mathRoot)
                        .SelectMany(tree => tree.children)
                        .Where(child => child.directBlendParameter == compute.name)
                        .Select(child => child.motion)
                        .Where(motion => motion != null)
                        .ToArray();
                    Assert.That(gatedLearnedMotions, Is.Not.Empty,
                        "VRCFury rewrote the private Compute parameter but disconnected " +
                        "the learned conditional subtree.");
                    Assert.That(gatedLearnedMotions.Any(motion =>
                            motion.name.IndexOf(
                                "Conditional learned inference",
                                StringComparison.OrdinalIgnoreCase) >= 0 ||
                            DescendantBlendTrees(motion).Any(tree =>
                                tree.name.IndexOf(
                                    "Conditional learned inference",
                                    StringComparison.OrdinalIgnoreCase) >= 0)),
                        Is.True,
                        "The Compute-gated motion must still be the learned inference " +
                        "subtree after VRCFury optimization.");
                }
            }
            finally
            {
                AdvancedVisemeAnimatorBuilder
                    .EnableConditionalLearnedDetailSleepForTests = previous;
            }
        }

        [Test]
        public void RealPipelineKeepsLocalAffineExpertsStructurallyDisconnected()
        {
            var sourceFolderName = "__YUCP_AVR_ExpertPipeline_" +
                                   Guid.NewGuid().ToString("N");
            var sourceFolder = "Assets/" + sourceFolderName;
            AssetDatabase.CreateFolder("Assets", sourceFolderName);
            try
            {
                var reference = AdvancedVisemeExpertPosePrototype.CreateReferenceModel();
                var fitted = AdvancedVisemeExpertPosePrototype.Fit(
                    AdvancedVisemeExpertPosePrototype.CreateSamples(
                        reference, 96, 0x51A7E),
                    1e-10, 1e-7f);
                using (var prototype =
                       AdvancedVisemeExpertPosePrototype.CreateController(fitted))
                {
                    prototype.Persist(sourceFolder + "/Expert.controller");
                    using (var fixture = new PipelineFixture(withRootLink: false))
                    {
                        AddFullController(
                            fixture.root,
                            prototype.controller,
                            prototype.controller.parameters.Select(parameter => parameter.name));
                        fixture.Build();

                        var controller = fixture.FxController;
                        var layerNames = controller.layers.Select(layer => layer.name).ToArray();
                        var expertLayer = controller.layers.SingleOrDefault(layer =>
                            layer.name.IndexOf("Expert Prototype",
                                StringComparison.OrdinalIgnoreCase) >= 0);
                        Assert.That(expertLayer, Is.Not.Null,
                            "Merged layers: " + string.Join(", ", layerNames));
                        var states = DescendantStates(expertLayer.stateMachine).ToArray();
                        Assert.That(states, Has.Length.EqualTo(
                            VisemeReconstructionProfile.VisemeCount),
                            "VRCFury flattened the 15-way hard router back into one hot Direct tree.");
                        Assert.That(states.Select(state => state.motion).Distinct().Count(),
                            Is.EqualTo(states.Length),
                            "VRCFury connected every expert through a shared state motion.");
                        Assert.That(states.SelectMany(state => state.transitions).Count(),
                            Is.EqualTo(VisemeReconstructionProfile.VisemeCount *
                                       (VisemeReconstructionProfile.VisemeCount - 1)),
                            "The interruptible viseme trellis changed during the real merge.");

                        var stateLeaves = states.Select(state =>
                                AdvancedVisemeExpertPosePrototype.CountClipLeaves(state.motion))
                            .ToArray();
                        Assert.That(stateLeaves.Max(), Is.LessThanOrEqualTo(12),
                            "An expert state gained references to inactive experts after merging.");

                        const string prototypeOutputPrefix = "YUCP/ExpertTest/Out/";
                        var composeLayer = controller.layers.SingleOrDefault(layer =>
                            DescendantStates(layer.stateMachine).Any(state =>
                                DescendantClips(state.motion).Any(clip =>
                                    AnimationUtility.GetCurveBindings(clip).Any(binding =>
                                        binding.type == typeof(Animator) &&
                                        binding.propertyName.StartsWith(
                                            prototypeOutputPrefix,
                                            StringComparison.Ordinal)))));
                        Assert.That(composeLayer, Is.Not.Null,
                            "Merged layers: " + string.Join(", ", layerNames));
                        var composeStates = DescendantStates(composeLayer.stateMachine).ToArray();
                        Assert.That(composeStates, Has.Length.EqualTo(1));
                        // VRCFury intentionally folds all eligible one-state
                        // layers into its shared LayerToTreeService layer. Count
                        // only this prototype's final-output leaves; the same
                        // merged motion also contains the production AVR fixture.
                        var composeLeaves = DescendantClips(composeStates[0].motion)
                            .Count(clip => AnimationUtility.GetCurveBindings(clip).Any(binding =>
                                binding.type == typeof(Animator) &&
                                binding.propertyName.StartsWith(
                                    prototypeOutputPrefix,
                                    StringComparison.Ordinal)));
                        Assert.That(composeLeaves, Is.LessThanOrEqualTo(32));
                        var transitionLeaves = composeLeaves + stateLeaves
                            .OrderByDescending(value => value).Take(2).Sum();
                        TestContext.WriteLine(
                            $"post-vrcfury expertStates={states.Length} " +
                            $"residualLeaves={stateLeaves.Min()}-{stateLeaves.Max()} " +
                            $"composeLeaves={composeLeaves} " +
                            $"steadyLeaves<={composeLeaves + stateLeaves.Max()} " +
                            $"transitionLeaves<={transitionLeaves}");
                        UnityEngine.Debug.Log(
                            $"[YUCP AVR Expert VRCFury] states={states.Length} " +
                            $"residualLeaves={stateLeaves.Min()}-{stateLeaves.Max()} " +
                            $"composeLeaves={composeLeaves} " +
                            $"steadyLeaves={composeLeaves + stateLeaves.Max()} " +
                            $"transitionLeaves={transitionLeaves}");
                    }
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(sourceFolder);
            }
        }

        [Test]
        public void LateTuningConflictRestoresEveryRendererAndRemovesStagingAssets()
        {
            using (var fixture = new PipelineFixture(withRootLink: true))
            {
                var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                try
                {
                    fixture.component.createTuningMenu = true;
                    fixture.component.tuningMenuSections =
                        AdvancedVisemeTuningMenuSections.Speech;
                    parameters.parameters = new[]
                    {
                        new VRCExpressionParameters.Parameter
                        {
                            name = fixture.component.TuningParameterName(
                                AdvancedVisemeTuningControl.SpeechSmoothness),
                            valueType = VRCExpressionParameters.ValueType.Bool,
                            saved = true,
                            networkSynced = false
                        }
                    };
                    fixture.descriptor.expressionParameters = parameters;

                    var primaryBefore = fixture.primary.sharedMesh;
                    var targetBefore = fixture.target.sharedMesh;
                    var lipSyncBefore = fixture.descriptor.lipSync;
                    var generatedBefore = GeneratedFolders();
                    var vrcFuryBefore = fixture.root
                        .GetComponentsInChildren<Component>(true)
                        .Where(component => component != null &&
                                            component.GetType().FullName ==
                                            "VF.Model.VRCFury")
                        .Select(component => component.GetInstanceID())
                        .OrderBy(id => id)
                        .ToArray();

                    LogAssert.Expect(LogType.Error,
                        new System.Text.RegularExpressions.Regex(
                            "Existing tuning parameter.*must be Float"));
                    var accepted = new AdvancedVisemeReconstructorProcessor()
                        .OnPreprocessAvatar(fixture.root);

                    Assert.That(accepted, Is.False);
                    Assert.That(fixture.primary.sharedMesh, Is.SameAs(primaryBefore));
                    Assert.That(fixture.target.sharedMesh, Is.SameAs(targetBefore));
                    Assert.That(fixture.descriptor.lipSync, Is.EqualTo(lipSyncBefore));
                    Assert.That(fixture.root.transform.Cast<Transform>().Any(child =>
                            child.name == "__YUCP Advanced Viseme Controller"),
                        Is.False);
                    CollectionAssert.AreEqual(generatedBefore, GeneratedFolders());
                    CollectionAssert.AreEqual(vrcFuryBefore, fixture.root
                        .GetComponentsInChildren<Component>(true)
                        .Where(component => component != null &&
                                            component.GetType().FullName ==
                                            "VF.Model.VRCFury")
                        .Select(component => component.GetInstanceID())
                        .OrderBy(id => id)
                        .ToArray());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(parameters);
                }
            }
        }

        [Test]
        public void TransactionRollbackRestoresPostInstallAvatarState()
        {
            using (var fixture = new PipelineFixture(withRootLink: false))
            {
                var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
                var replacementMesh = CreateFaceMesh("Temporary replacement", 0.031f);
                var vrcFuryType = FindType("VF.Model.VRCFury");
                var existingVrcFury = fixture.root.AddComponent(vrcFuryType);
                var finalFolder = GeneratedRoot + "/__Transaction_" +
                                  Guid.NewGuid().ToString("N");
                var originalMesh = fixture.primary.sharedMesh;
                var originalLipSync = fixture.descriptor.lipSync;
                fixture.component.profile = profile;
                profile.SetDiagnostics(0.25f, 0.75f);
                Component createdVrcFury = null;
                GameObject controllerHost = null;
                try
                {
                    using (var transaction =
                           new AdvancedVisemeReconstructorProcessor.AvatarBuildTransaction(
                               fixture.root,
                               fixture.descriptor,
                               new[] { fixture.component }))
                    {
                        var staging = transaction.StageGeneratedFolder(finalFolder);
                        AssetDatabase.CreateAsset(
                            new Mesh { name = "Partial staged output" },
                            staging + "/Partial.asset");

                        fixture.primary.sharedMesh = replacementMesh;
                        fixture.descriptor.lipSync =
                            VRCAvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
                        profile.SetDiagnostics(0.9f, 1.2f);
                        createdVrcFury = fixture.root.AddComponent(vrcFuryType);
                        controllerHost = new GameObject(
                            "__YUCP Advanced Viseme Controller");
                        controllerHost.transform.SetParent(
                            fixture.root.transform, false);
                        transaction.TrackCreatedObject(controllerHost);
                        // Deliberately leave the transaction uncommitted.
                    }

                    Assert.That(fixture.primary.sharedMesh, Is.SameAs(originalMesh));
                    Assert.That(fixture.descriptor.lipSync, Is.EqualTo(originalLipSync));
                    Assert.That(profile.LastReconstructionRms,
                        Is.EqualTo(0.25f).Within(1e-6f));
                    Assert.That(profile.LastReconstructionMaximum,
                        Is.EqualTo(0.75f).Within(1e-6f));
                    Assert.That(existingVrcFury, Is.Not.Null);
                    Assert.That(createdVrcFury == null, Is.True,
                        "A VRCFury feature added after the snapshot must be removed.");
                    Assert.That(controllerHost == null, Is.True,
                        "The generated Full Controller host must be removed.");
                    Assert.That(AssetDatabase.IsValidFolder(finalFolder), Is.False);
                    Assert.That(Directory.Exists(finalFolder), Is.False);
                }
                finally
                {
                    AssetDatabase.DeleteAsset(finalFolder);
                    UnityEngine.Object.DestroyImmediate(replacementMesh);
                    UnityEngine.Object.DestroyImmediate(profile);
                }
            }
        }

        [TestCase(209, true)]
        [TestCase(210, false)]
        public void MultipleComponentsValidateTheCumulativeParameterUnion(
            int existingSyncedBits,
            bool expectedSuccess)
        {
            using (var fixture = new PipelineFixture(withRootLink: false))
            {
                var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                try
                {
                    parameters.parameters = Enumerable.Range(0, existingSyncedBits)
                        .Select(index => new VRCExpressionParameters.Parameter
                        {
                            name = "Existing/BudgetBit" + index,
                            valueType = VRCExpressionParameters.ValueType.Bool,
                            networkSynced = true,
                            saved = false
                        })
                        .ToArray();
                    fixture.descriptor.expressionParameters = parameters;

                    var secondaryObject = new GameObject("Secondary AVR outputs");
                    secondaryObject.transform.SetParent(fixture.root.transform, false);
                    var secondary = secondaryObject
                        .AddComponent<AdvancedVisemeReconstructorData>();
                    secondary.mouthOwnership = AdvancedVisemeMouthOwnership.OutputsOnly;
                    secondary.trackingInputs = AdvancedVisemeTrackingInputs.Balanced8;
                    secondary.trackingEncoding =
                        AdvancedVisemeTrackingEncoding.AdaptiveBinary;
                    secondary.parameterPrefix = "YUCP/PipelineParitySecondary";
                    secondary.createFaceTrackingToggle = false;
                    secondary.createTuningMenu = false;

                    var primaryBefore = fixture.primary.sharedMesh;
                    var lipSyncBefore = fixture.descriptor.lipSync;
                    var generatedBefore = GeneratedFolders();
                    if (!expectedSuccess)
                    {
                        // The first component contributes 24 bits. The second
                        // contributes 23 because LipTrackingActive is shared.
                        // 210 + 24 + 23 = 257 and must fail transactionally.
                        LogAssert.Expect(LogType.Error,
                            new System.Text.RegularExpressions.Regex(
                                "Face tracking needs 23 additional synced bits.*only 22 bits available"));
                    }

                    var accepted = new AdvancedVisemeReconstructorProcessor()
                        .OnPreprocessAvatar(fixture.root);
                    Assert.That(accepted, Is.EqualTo(expectedSuccess));

                    if (expectedSuccess)
                    {
                        Assert.That(fixture.descriptor.lipSync,
                            Is.EqualTo(VRCAvatarDescriptor.LipSyncStyle.VisemeParameterOnly));
                        Assert.That(fixture.root
                                .GetComponentsInChildren<Component>(true)
                                .Count(component => component != null &&
                                                    component.GetType().FullName ==
                                                    "VF.Model.VRCFury"),
                            Is.GreaterThanOrEqualTo(2),
                            "Both generated controllers must survive an exact-256-bit build.");
                    }
                    else
                    {
                        Assert.That(fixture.primary.sharedMesh, Is.SameAs(primaryBefore));
                        Assert.That(fixture.descriptor.lipSync, Is.EqualTo(lipSyncBefore));
                        Assert.That(fixture.root.transform.Cast<Transform>().Any(child =>
                                child.name == "__YUCP Advanced Viseme Controller"),
                            Is.False);
                        CollectionAssert.AreEqual(generatedBefore, GeneratedFolders());
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(parameters);
                }
            }
        }

        [Test]
        public void FreshTrackingRejectsUnsyncedInputNameCollisions()
        {
            using (var fixture = new PipelineFixture(withRootLink: false))
            {
                var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                try
                {
                    parameters.parameters = new[]
                    {
                        new VRCExpressionParameters.Parameter
                        {
                            name = Prefix + "/v2/JawOpen1",
                            valueType = VRCExpressionParameters.ValueType.Bool,
                            networkSynced = false,
                            saved = false
                        }
                    };
                    fixture.descriptor.expressionParameters = parameters;
                    var sourceMesh = fixture.primary.sharedMesh;
                    var lipSync = fixture.descriptor.lipSync;
                    var generatedBefore = GeneratedFolders();

                    LogAssert.Expect(LogType.Error,
                        new System.Text.RegularExpressions.Regex(
                            "Existing binary parameter.*JawOpen1.*must be network-synced"));
                    var accepted = new AdvancedVisemeReconstructorProcessor()
                        .OnPreprocessAvatar(fixture.root);

                    Assert.That(accepted, Is.False);
                    Assert.That(fixture.primary.sharedMesh, Is.SameAs(sourceMesh));
                    Assert.That(fixture.descriptor.lipSync, Is.EqualTo(lipSync));
                    CollectionAssert.AreEqual(generatedBefore, GeneratedFolders());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(parameters);
                }
            }
        }

        [Test]
        public void FinalValidatorRejectsParametersGeneratedPastTheEarlyBudgetPass()
        {
            var validator = new AdvancedVisemeFinalParameterValidator();
            Assert.That(validator.callbackOrder, Is.GreaterThan(int.MaxValue - 100),
                "Final memory validation must run after VRCFury's parameter compressor.");
            Assert.That(validator.callbackOrder, Is.LessThan(int.MaxValue),
                "Final memory validation must run before editor-only cleanup callbacks.");

            using (var fixture = new PipelineFixture(withRootLink: false))
            {
                var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                try
                {
                    parameters.parameters = Enumerable.Range(
                            0, VRCExpressionParameters.MAX_PARAMETER_COST + 1)
                        .Select(index => new VRCExpressionParameters.Parameter
                        {
                            name = "LateFeature/Bit" + index,
                            valueType = VRCExpressionParameters.ValueType.Bool,
                            networkSynced = true
                        })
                        .ToArray();
                    fixture.descriptor.expressionParameters = parameters;

                    LogAssert.Expect(LogType.Error,
                        new System.Text.RegularExpressions.Regex(
                            "final merged avatar uses 257 synced parameter bits"));
                    Assert.That(
                        AdvancedVisemeFinalParameterValidator.ValidateFinalParameters(
                            fixture.root, fixture.descriptor),
                        Is.False,
                        "Late VRCFury/Modular Avatar parameters must not bypass the " +
                        "VRChat memory limit merely because AVR runs first.");

                    parameters.parameters = parameters.parameters.Take(
                        VRCExpressionParameters.MAX_PARAMETER_COST).ToArray();
                    Assert.That(
                        AdvancedVisemeFinalParameterValidator.ValidateFinalParameters(
                            fixture.root, fixture.descriptor),
                        Is.True,
                        "The exact 256-bit boundary must remain valid.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(parameters);
                }
            }
        }

        private static void AssertRootTargetOverlay(PipelineFixture fixture)
        {
            Assert.That(fixture.target, Is.Not.Null);
            Assert.That(AnimationUtility.CalculateTransformPath(
                fixture.target.transform, fixture.root.transform), Is.Empty,
                "The linked target must exercise the root Animator path.");
            Assert.That(fixture.target.sharedMesh, Is.Not.SameAs(fixture.targetSource),
                "AVR must install a build-only target mesh clone.");

            var generatedTargetShapes = Enumerable.Range(0, fixture.target.sharedMesh.blendShapeCount)
                .Select(fixture.target.sharedMesh.GetBlendShapeName)
                .Where(name => fixture.targetSource.GetBlendShapeIndex(name) < 0)
                .ToArray();
            Assert.That(generatedTargetShapes, Is.Not.Empty,
                "The target build mesh contains no target-local residuals.");
            var generatedResidualShapes = generatedTargetShapes
                .Where(name => name.Contains("_Residual_", StringComparison.Ordinal))
                .ToArray();
            Assert.That(generatedResidualShapes, Has.Length.EqualTo(
                    VisemeReconstructionProfile.VisemeCount),
                "Every mapped Oculus viseme needs a target-local residual.");
            var generatedCarrierShapes = generatedTargetShapes
                .Where(name => name.Contains("_Ownership_", StringComparison.Ordinal))
                .ToArray();
            Assert.That(generatedCarrierShapes, Is.Not.Empty,
                "The linked pipeline did not exercise measured residual ownership.");
            var signedInverseShape = generatedTargetShapes.SingleOrDefault(name =>
                name.Contains("_Basis_", StringComparison.Ordinal) &&
                name.Contains(SmileShape, StringComparison.Ordinal) &&
                name.EndsWith("_Inverse", StringComparison.Ordinal));
            Assert.That(signedInverseShape, Is.Not.Null.And.Not.Empty,
                "The signed SmileSad channel needs target-local -U geometry.");

            var clips = fixture.FxController.animationClips
                .Where(clip => clip != null)
                .Distinct()
                .ToArray();
            var rootCurves = clips.SelectMany(clip => AnimationUtility.GetCurveBindings(clip)
                    .Where(binding => binding.path == string.Empty &&
                                      binding.type == typeof(SkinnedMeshRenderer))
                    .Select(binding => new ClipBinding(clip, binding)))
                .ToArray();
            Assert.That(rootCurves, Is.Not.Empty,
                "The final VRCFury controller does not drive the root-level linked renderer.");

            var residualProperties = new HashSet<string>(
                generatedResidualShapes.Select(name => "blendShape." + name),
                StringComparer.Ordinal);
            var residualCurves = rootCurves
                .Where(item => residualProperties.Contains(item.binding.propertyName))
                .ToArray();
            Assert.That(residualCurves, Is.Not.Empty,
                "Target-local residual curves were lost during the real VRCFury build.");
            CollectionAssert.IsSubsetOf(
                residualProperties,
                rootCurves.Select(item => item.binding.propertyName).ToArray(),
                "At least one mapped viseme residual is not driven by the final controller.");

            var carrierProperties = generatedCarrierShapes
                .Select(name => "blendShape." + name)
                .ToHashSet(StringComparer.Ordinal);
            var carrierCurves = rootCurves
                .Where(item => carrierProperties.Contains(item.binding.propertyName))
                .ToArray();
            CollectionAssert.IsSubsetOf(
                carrierProperties,
                carrierCurves.Select(item => item.binding.propertyName).ToArray());
            foreach (var item in carrierCurves)
            foreach (var key in AnimationUtility.GetEditorCurve(item.clip, item.binding).keys)
                Assert.That(key.value, Is.InRange(0f, 100f),
                    "Ownership must not depend on a negative final blendshape weight.");

            var inverseProperty = "blendShape." + signedInverseShape;
            var inverseCurves = rootCurves
                .Where(item => item.binding.propertyName == inverseProperty)
                .ToArray();
            Assert.That(inverseCurves, Is.Not.Empty,
                "The final VRCFury controller does not drive the linked signed inverse basis.");
            foreach (var item in inverseCurves)
            foreach (var key in AnimationUtility.GetEditorCurve(
                         item.clip, item.binding).keys)
                Assert.That(key.value, Is.InRange(0f, 100f),
                    "Signed linked articulation must use a nonnegative inverse-shape magnitude.");

            foreach (var item in residualCurves)
            {
                Assert.That(AnimationUtility.GetCurveBindings(item.clip).Count(binding =>
                        binding.path == string.Empty &&
                        binding.type == typeof(SkinnedMeshRenderer) &&
                        binding.propertyName == item.binding.propertyName),
                    Is.EqualTo(1),
                    $"'{item.binding.propertyName}' was contributed more than once to '{item.clip.name}'.");

                Assert.That(AnimationUtility.GetCurveBindings(item.clip).Any(binding =>
                        binding.path == PrimaryPath &&
                        binding.type == typeof(SkinnedMeshRenderer) &&
                        binding.propertyName == item.binding.propertyName),
                    Is.False,
                    "A target-local residual was also copied from the primary mesh.");
            }

            // For mapped authored shapes, the installed VRCFury implementation
            // must be the sole contributor: every target curve is exactly the
            // corresponding primary curve, with no second additive overlay.
            var linkedJaw = rootCurves
                .Where(item => item.binding.propertyName == "blendShape." + JawShape)
                .ToArray();
            var primaryJaw = clips.SelectMany(clip => AnimationUtility.GetCurveBindings(clip)
                    .Where(binding => binding.path == PrimaryPath &&
                                      binding.type == typeof(SkinnedMeshRenderer) &&
                                      binding.propertyName == "blendShape." + JawShape)
                    .Select(binding => new ClipBinding(clip, binding)))
                .ToArray();
            Assert.That(linkedJaw.Length, Is.EqualTo(primaryJaw.Length).And.GreaterThan(0),
                "VRCFury did not copy the primary JawOpen contribution exactly once.");
            foreach (var target in linkedJaw)
            {
                var source = primaryJaw.Single(item => item.clip == target.clip);
                AssertCurvesEqual(
                    AnimationUtility.GetEditorCurve(source.clip, source.binding),
                    AnimationUtility.GetEditorCurve(target.clip, target.binding),
                    target.clip.name + ": root JawOpen");
            }
        }

        private static string[] PublicParameterFingerprint(VRCAvatarDescriptor descriptor)
        {
            var output = new List<string>();
            var controller = GetFxController(descriptor);
            output.AddRange(controller.parameters
                .Where(parameter => parameter.name.StartsWith(Prefix + "/", StringComparison.Ordinal))
                .Select(parameter => string.Join("|",
                    "FX",
                    parameter.name,
                    parameter.type,
                    parameter.defaultBool,
                    parameter.defaultInt,
                    parameter.defaultFloat.ToString("R", CultureInfo.InvariantCulture))));

            if (descriptor.expressionParameters != null &&
                descriptor.expressionParameters.parameters != null)
            {
                output.AddRange(descriptor.expressionParameters.parameters
                    .Where(parameter => parameter != null &&
                                        parameter.name.StartsWith(Prefix + "/", StringComparison.Ordinal))
                    .Select(parameter => string.Join("|",
                        "Expression",
                        parameter.name,
                        parameter.valueType,
                        parameter.defaultValue.ToString("R", CultureInfo.InvariantCulture),
                        parameter.saved,
                        parameter.networkSynced)));
            }

            return output.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        private static bool ContainsConditionalControl(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(
                       "Conditional", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static AnimatorControllerParameter FindPrivateParameter(
            AnimatorController controller,
            string sourceName)
        {
            var matches = controller.parameters
                .Where(parameter => parameter.name.EndsWith(
                    sourceName, StringComparison.Ordinal))
                .ToArray();
            Assert.That(matches, Has.Length.EqualTo(1),
                $"Expected exactly one VRCFury-private rewrite of '{sourceName}'.");
            Assert.That(matches[0].name, Is.Not.EqualTo(sourceName),
                "The conditional math control was accidentally exported as global.");
            return matches[0];
        }

        private static IEnumerable<AnimatorState> DescendantStates(
            AnimatorStateMachine stateMachine)
        {
            if (stateMachine == null) yield break;
            foreach (var child in stateMachine.states)
                if (child.state != null)
                    yield return child.state;
            foreach (var child in stateMachine.stateMachines)
            foreach (var state in DescendantStates(child.stateMachine))
                yield return state;
        }

        private static IEnumerable<BlendTree> DescendantBlendTrees(Motion motion)
        {
            if (!(motion is BlendTree tree)) yield break;
            yield return tree;
            foreach (var child in tree.children)
            foreach (var descendant in DescendantBlendTrees(child.motion))
                yield return descendant;
        }

        private static IEnumerable<AnimationClip> DescendantClips(Motion motion)
        {
            if (motion is AnimationClip clip)
            {
                yield return clip;
                yield break;
            }
            if (!(motion is BlendTree tree)) yield break;
            foreach (var child in tree.children)
            foreach (var descendant in DescendantClips(child.motion))
                yield return descendant;
        }

        private static string[] ExpressionParameterFingerprint(
            VRCAvatarDescriptor descriptor)
        {
            return (descriptor.expressionParameters?.parameters ??
                    Array.Empty<VRCExpressionParameters.Parameter>())
                .Where(parameter => parameter != null)
                .Select(parameter => string.Join("|",
                    parameter.name,
                    parameter.valueType,
                    parameter.defaultValue.ToString("R", CultureInfo.InvariantCulture),
                    parameter.saved,
                    parameter.networkSynced))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private static int SyncedParameterBits(VRCAvatarDescriptor descriptor)
        {
            return (descriptor.expressionParameters?.parameters ??
                    Array.Empty<VRCExpressionParameters.Parameter>())
                .Where(parameter => parameter != null && parameter.networkSynced)
                .Sum(parameter => parameter.valueType ==
                                  VRCExpressionParameters.ValueType.Bool
                    ? 1
                    : 8);
        }

        private static string[] PrimaryCurveFingerprint(AnimatorController controller)
        {
            return controller.animationClips
                .Where(clip => clip != null)
                .Distinct()
                .SelectMany(clip => AnimationUtility.GetCurveBindings(clip)
                    .Where(binding => binding.path == PrimaryPath &&
                                      binding.type == typeof(SkinnedMeshRenderer))
                    .Select(binding => CurveFingerprint(binding,
                        AnimationUtility.GetEditorCurve(clip, binding))))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private static string CurveFingerprint(EditorCurveBinding binding, AnimationCurve curve)
        {
            var output = new StringBuilder(binding.propertyName ?? string.Empty);
            output.Append('|').Append(curve.preWrapMode).Append('|').Append(curve.postWrapMode);
            foreach (var key in curve.keys)
            {
                output.Append('|').Append(key.time.ToString("R", CultureInfo.InvariantCulture))
                    .Append(',').Append(key.value.ToString("R", CultureInfo.InvariantCulture))
                    .Append(',').Append(key.inTangent.ToString("R", CultureInfo.InvariantCulture))
                    .Append(',').Append(key.outTangent.ToString("R", CultureInfo.InvariantCulture))
                    .Append(',').Append(key.inWeight.ToString("R", CultureInfo.InvariantCulture))
                    .Append(',').Append(key.outWeight.ToString("R", CultureInfo.InvariantCulture))
                    .Append(',').Append((int)key.weightedMode);
            }
            return output.ToString();
        }

        private static void AssertCurvesEqual(
            AnimationCurve expected,
            AnimationCurve actual,
            string context)
        {
            Assert.That(actual, Is.Not.Null, context);
            Assert.That(expected, Is.Not.Null, context);
            Assert.That(CurveFingerprint(default, actual),
                Is.EqualTo(CurveFingerprint(default, expected)), context);
        }

        private static AnimatorController GetFxController(VRCAvatarDescriptor descriptor)
        {
            var layer = descriptor.baseAnimationLayers
                .FirstOrDefault(candidate => candidate.type == VRCAvatarDescriptor.AnimLayerType.FX);
            Assert.That(layer.animatorController, Is.InstanceOf<AnimatorController>(),
                "The real VRCFury build did not install an FX AnimatorController.");
            return (AnimatorController)layer.animatorController;
        }

        private static Component AddBlendShapeLink(
            GameObject host,
            SkinnedMeshRenderer baseRenderer,
            SkinnedMeshRenderer linkedRenderer)
        {
            var componentType = FindType("VF.Model.VRCFury");
            var featureType = FindType("VF.Model.Feature.BlendShapeLink");
            var linkSkinType = featureType.GetNestedType(
                "LinkSkin", BindingFlags.Public | BindingFlags.NonPublic);
            var component = host.AddComponent(componentType);
            var feature = Activator.CreateInstance(featureType);
            WriteField(feature, "baseObj", baseRenderer.gameObject.name);
            WriteField(feature, "includeAll", true);
            WriteField(feature, "exactMatch", true);

            var link = Activator.CreateInstance(linkSkinType);
            WriteField(link, "renderer", linkedRenderer);
            ((IList)ReadField(feature, "linkSkins")).Add(link);
            WriteField(component, "content", feature);
            return component;
        }

        private static void AddFullController(
            GameObject host,
            RuntimeAnimatorController controller,
            IEnumerable<string> globalParameters)
        {
            var components = FindType("com.vrcfury.api.FuryComponents");
            var create = components.GetMethod(
                "CreateFullController",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(GameObject) },
                null);
            Assert.That(create, Is.Not.Null,
                "Installed VRCFury no longer exposes its public FullController factory.");
            var fullController = create.Invoke(null, new object[] { host });
            var type = fullController.GetType();
            var addController = type.GetMethod("AddController", BindingFlags.Public |
                                                               BindingFlags.Instance);
            var addGlobal = type.GetMethod("AddGlobalParam", BindingFlags.Public |
                                                             BindingFlags.Instance);
            Assert.That(addController, Is.Not.Null);
            Assert.That(addGlobal, Is.Not.Null);
            addController.Invoke(fullController, new object[]
            {
                controller,
                VRCAvatarDescriptor.AnimLayerType.FX
            });
            foreach (var parameter in globalParameters.Distinct(StringComparer.Ordinal))
                addGlobal.Invoke(fullController, new object[] { parameter });
        }

        private static Type FindType(string fullName)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null,
                $"Installed VRCFury type '{fullName}' was not found.");
            return type;
        }

        private static object ReadField(object instance, string name)
        {
            var field = instance.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                $"Field '{instance.GetType().FullName}.{name}' was not found.");
            return field.GetValue(instance);
        }

        private static void WriteField(object instance, string name, object value)
        {
            var field = instance.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                $"Field '{instance.GetType().FullName}.{name}' was not found.");
            field.SetValue(instance, value);
        }

        private static Mesh CreateFaceMesh(string name, float scale)
        {
            const int vertexCount = 6;
            var mesh = new Mesh { name = name };
            mesh.vertices = Enumerable.Range(0, vertexCount)
                .Select(index => new Vector3(index * 0.01f, 0f, 0f)).ToArray();
            mesh.normals = Enumerable.Repeat(Vector3.forward, vertexCount).ToArray();
            mesh.tangents = Enumerable.Repeat(new Vector4(1f, 0f, 0f, 1f), vertexCount).ToArray();
            mesh.triangles = new[] { 0, 1, 2, 2, 3, 0 };

            var jaw = Delta(vertexCount, 0, scale, 0.25f, -0.1f);
            AddShape(mesh, JawShape, jaw);
            var smile = Delta(vertexCount, 3, scale * 0.8f, -0.2f, 0.15f);
            AddShape(mesh, SmileShape, smile);
            for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
            {
                var delta = new Vector3[vertexCount];
                var normals = new Vector3[vertexCount];
                var tangents = new Vector3[vertexCount];
                var jawAmount = viseme == 1
                    ? -0.35f // Forces a signed ownership residual after NNLS.
                    : viseme == 10
                        ? 1f
                        : viseme == 0 ? 0f : 0.08f + viseme * 0.025f;
                for (var vertex = 0; vertex < vertexCount; vertex++)
                {
                    delta[vertex] = jaw.vertices[vertex] * jawAmount;
                    normals[vertex] = jaw.normals[vertex] * jawAmount;
                    tangents[vertex] = jaw.tangents[vertex] * jawAmount;
                }

                if (viseme > 0)
                {
                    var detailVertex = 1 + viseme % (vertexCount - 1);
                    var detail = scale * (0.12f + viseme * 0.01f);
                    delta[detailVertex] += new Vector3(detail, -detail * 0.4f, detail * 0.2f);
                    normals[detailVertex] += new Vector3(-detail * 0.3f, detail * 0.1f, detail);
                    tangents[detailVertex] += new Vector3(detail * 0.5f, detail * 0.2f, -detail * 0.1f);
                }

                mesh.AddBlendShapeFrame(
                    "vrc.v_" + VisemeReconstructionProfile.VisemeNames[viseme],
                    100f, delta, normals, tangents);
            }
            return mesh;
        }

        private static MeshDelta Delta(
            int vertexCount,
            int vertex,
            float scale,
            float y,
            float z)
        {
            var output = new MeshDelta(vertexCount);
            output.vertices[vertex] = new Vector3(scale, scale * y, scale * z);
            output.normals[vertex] = new Vector3(scale * 0.3f, -scale * 0.2f, scale * 0.5f);
            output.tangents[vertex] = new Vector3(-scale * 0.1f, scale * 0.35f, scale * 0.2f);
            return output;
        }

        private static void AddShape(Mesh mesh, string name, MeshDelta delta)
        {
            mesh.AddBlendShapeFrame(name, 100f,
                delta.vertices, delta.normals, delta.tangents);
        }

        private static string MeshFingerprint(Mesh mesh)
        {
            var output = new StringBuilder();
            output.Append(mesh.vertexCount).Append('|').Append(mesh.blendShapeCount).Append('|');
            for (var shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                output.Append(mesh.GetBlendShapeName(shape)).Append('|');
                for (var frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    output.Append(mesh.GetBlendShapeFrameWeight(shape, frame)
                            .ToString("R", CultureInfo.InvariantCulture))
                        .Append('|');
                    var delta = new MeshDelta(mesh.vertexCount);
                    mesh.GetBlendShapeFrameVertices(
                        shape, frame, delta.vertices, delta.normals, delta.tangents);
                    AppendVectors(output, delta.vertices);
                    AppendVectors(output, delta.normals);
                    AppendVectors(output, delta.tangents);
                }
            }
            return Hash128.Compute(output.ToString()).ToString();
        }

        private static void AppendVectors(StringBuilder output, IEnumerable<Vector3> values)
        {
            foreach (var value in values)
            {
                output.Append(value.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(value.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(value.z.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            }
        }

        private readonly struct ClipBinding
        {
            public readonly AnimationClip clip;
            public readonly EditorCurveBinding binding;

            public ClipBinding(AnimationClip clip, EditorCurveBinding binding)
            {
                this.clip = clip;
                this.binding = binding;
            }
        }

        private sealed class MeshDelta
        {
            public readonly Vector3[] vertices;
            public readonly Vector3[] normals;
            public readonly Vector3[] tangents;

            public MeshDelta(int vertexCount)
            {
                vertices = new Vector3[vertexCount];
                normals = new Vector3[vertexCount];
                tangents = new Vector3[vertexCount];
            }
        }

        private sealed class PipelineFixture : IDisposable
        {
            private readonly string[] generatedFoldersBefore;
            private readonly bool ownsPrimarySource;
            private readonly Scene fixtureScene;
            public readonly GameObject root;
            public readonly VRCAvatarDescriptor descriptor;
            public readonly SkinnedMeshRenderer primary;
            public readonly Mesh primarySource;
            public readonly SkinnedMeshRenderer target;
            public readonly Mesh targetSource;
            public readonly AdvancedVisemeReconstructorData component;

            public PipelineFixture(bool withRootLink, Mesh primarySourceOverride = null)
            {
                generatedFoldersBefore = GeneratedFolders();
                fixtureScene = EditorSceneManager.NewPreviewScene();
                root = new GameObject("AVR Pipeline " + Guid.NewGuid().ToString("N"));
                SceneManager.MoveGameObjectToScene(root, fixtureScene);
                root.AddComponent<Animator>();
                descriptor = root.AddComponent<VRCAvatarDescriptor>();
                // VRCFury's temp-folder cleanup enumerates every loaded
                // descriptor, including the second fixture before it is built.
                // A freshly added SDK descriptor leaves these arrays null until
                // its inspector initializes them, so make the headless fixture a
                // valid SDK object up front.
                descriptor.baseAnimationLayers =
                    Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                descriptor.specialAnimationLayers =
                    Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();

                var primaryObject = new GameObject(PrimaryPath);
                primaryObject.transform.SetParent(root.transform, false);
                primary = primaryObject.AddComponent<SkinnedMeshRenderer>();
                primary.reflectionProbeUsage = ReflectionProbeUsage.Off;
                primary.lightProbeUsage = LightProbeUsage.Off;
                ownsPrimarySource = primarySourceOverride == null;
                primarySource = primarySourceOverride ??
                                CreateFaceMesh("Primary source", 0.013f);
                primary.sharedMesh = primarySource;

                descriptor.lipSync = VRCAvatarDescriptor.LipSyncStyle.VisemeBlendShape;
                descriptor.VisemeSkinnedMesh = primary;
                descriptor.VisemeBlendShapes = VisemeReconstructionProfile.VisemeNames
                    .Select(name => "vrc.v_" + name)
                    .ToArray();

                component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.faceRenderer = primary;
                component.mouthOwnership = AdvancedVisemeMouthOwnership.DriveLowerFace;
                component.reconstructionMode = AdvancedVisemeReconstructionMode.Normal;
                component.trackingInputs = AdvancedVisemeTrackingInputs.Balanced8;
                component.parameterPrefix = Prefix;
                component.createFaceTrackingToggle = false;
                component.createTuningMenu = false;

                if (withRootLink)
                {
                    target = root.AddComponent<SkinnedMeshRenderer>();
                    target.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    target.lightProbeUsage = LightProbeUsage.Off;
                    targetSource = CreateFaceMesh("Root target source", 0.021f);
                    target.sharedMesh = targetSource;
                    AddBlendShapeLink(root, primary, target);
                }
            }

            public AnimatorController FxController => GetFxController(descriptor);

            public void Build()
            {
                Assert.That(RunRegisteredPreprocess(root), Is.True,
                    "The registered avatar preprocess pipeline rejected the synthetic avatar.");
                Assert.That(descriptor.lipSync,
                    Is.EqualTo(VRCAvatarDescriptor.LipSyncStyle.VisemeParameterOnly));
                Assert.That(FxController, Is.Not.Null);
            }

            public void Dispose()
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (ownsPrimarySource && primarySource != null)
                    UnityEngine.Object.DestroyImmediate(primarySource);
                if (targetSource != null) UnityEngine.Object.DestroyImmediate(targetSource);
                if (fixtureScene.IsValid() && fixtureScene.isLoaded)
                    EditorSceneManager.ClosePreviewScene(fixtureScene);

                foreach (var folder in GeneratedFolders()
                             .Except(generatedFoldersBefore, StringComparer.Ordinal))
                    AssetDatabase.DeleteAsset(folder);
            }
        }

        private static string[] GeneratedFolders()
        {
            return AssetDatabase.IsValidFolder(GeneratedRoot)
                ? AssetDatabase.GetSubFolders(GeneratedRoot)
                : Array.Empty<string>();
        }

        private static bool RunRegisteredPreprocess(GameObject avatarRoot)
        {
            var callbacks = FindType(
                "VRC.SDKBase.Editor.BuildPipeline.VRCBuildPipelineCallbacks");
            var method = callbacks.GetMethod(
                "OnPreprocessAvatar",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(GameObject) },
                null);
            Assert.That(method, Is.Not.Null,
                "The installed VRChat SDK no longer exposes its avatar preprocess entry point.");
            return (bool)method.Invoke(null, new object[] { avatarRoot });
        }
    }
}
#endif
