using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using com.vrcfury.api;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components.Editor.MeshUtils;
using YUCP.Components.Editor.Utils;

namespace YUCP.Components.Editor
{
    public sealed class AdvancedVisemeReconstructorProcessor : IVRCSDKPreprocessAvatarCallback
    {
        private const string GeneratedRoot = "Assets/YUCP/GeneratedAssets/AdvancedVisemeReconstructor";
        private const int ParameterBudget = 256;

        private static readonly AdvancedVisemeArticulator[] BalancedArticulators =
        {
            AdvancedVisemeArticulator.JawOpen,
            AdvancedVisemeArticulator.LipClose,
            AdvancedVisemeArticulator.MouthOpen,
            AdvancedVisemeArticulator.LipFunnel,
            AdvancedVisemeArticulator.LipPucker,
            AdvancedVisemeArticulator.LipSuck,
            AdvancedVisemeArticulator.SmileSad,
            AdvancedVisemeArticulator.TongueOut
        };

        private static readonly AdvancedVisemeArticulator[] QualityExtraArticulators =
        {
            AdvancedVisemeArticulator.JawX,
            AdvancedVisemeArticulator.JawZ,
            AdvancedVisemeArticulator.MouthX,
            AdvancedVisemeArticulator.TongueY
        };

        private static readonly AdvancedVisemeArticulator[] FullTongueExtraArticulators =
        {
            AdvancedVisemeArticulator.TongueX,
            AdvancedVisemeArticulator.TongueRoll,
            AdvancedVisemeArticulator.TongueArchY,
            AdvancedVisemeArticulator.TongueShape,
            AdvancedVisemeArticulator.TongueTwistRight,
            AdvancedVisemeArticulator.TongueTwistLeft
        };

        public int callbackOrder => int.MinValue + 190;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return true;
            var components = avatarRoot.GetComponentsInChildren<AdvancedVisemeReconstructorData>(true);
            if (components.Length == 0) return true;
            AdvancedVisemeFinalParameterValidator.Mark(avatarRoot);

            if (components.Count(c => c.mouthOwnership == AdvancedVisemeMouthOwnership.DriveLowerFace) > 1)
            {
                Debug.LogError("[YUCP Advanced Viseme] Only one component may own the lower face on an avatar.", avatarRoot);
                return false;
            }

            var duplicatePrefix = components.GroupBy(c => c.NormalizedPrefix, StringComparer.Ordinal)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicatePrefix != null)
            {
                Debug.LogError($"[YUCP Advanced Viseme] Multiple components use the parameter prefix '{duplicatePrefix.Key}'.", avatarRoot);
                return false;
            }

            if (components.Any(c => c.mouthOwnership == AdvancedVisemeMouthOwnership.DriveLowerFace) && HasVrcFuryAdvancedVisemes(avatarRoot))
            {
                Debug.LogError("[YUCP Advanced Viseme] Remove VRCFury Advanced Visemes before enabling lower-face ownership. Use Outputs Only if the existing feature must remain.", avatarRoot);
                return false;
            }

            // Resolve the creator-authored avatar once, before any AVR component
            // adds a staged controller or parameter asset. Otherwise a second
            // Outputs Only component could discover the first component's random
            // staging path and receive a non-deterministic hash/input source.
            var sourceTrackingCatalog =
                AdvancedVisemeTrackingCatalog.Scan(avatarRoot, descriptor);
            var sourceBlendShapeLinkCatalog =
                AdvancedVisemeBlendShapeLinkCatalog.Scan(avatarRoot);

            using (var transaction = new AvatarBuildTransaction(
                       avatarRoot, descriptor, components))
            {
                foreach (var component in components)
                {
                    if (!BuildComponent(
                            avatarRoot, descriptor, component, transaction,
                            sourceTrackingCatalog,
                            sourceBlendShapeLinkCatalog))
                        return false;
                }

                try
                {
                    transaction.Complete();
                    return true;
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[YUCP Advanced Viseme] Could not commit generated assets: " +
                        exception.Message, avatarRoot);
                    Debug.LogException(exception, avatarRoot);
                    return false;
                }
            }
        }

        private static bool BuildComponent(
            GameObject avatarRoot,
            VRCAvatarDescriptor descriptor,
            AdvancedVisemeReconstructorData component,
            AvatarBuildTransaction transaction,
            AdvancedVisemeTrackingCatalog sourceTrackingCatalog,
            AdvancedVisemeBlendShapeLinkCatalog sourceBlendShapeLinkCatalog)
        {
            VisemeReconstructionProfile temporaryProfile = null;
            try
            {
                var profile = component.profile;
                if (profile == null)
                {
                    temporaryProfile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
                    profile = temporaryProfile;
                }
                profile.EnsureDefaults();

                var renderer = component.faceRenderer != null ? component.faceRenderer : descriptor.VisemeSkinnedMesh;
                var ownsMouth = component.mouthOwnership == AdvancedVisemeMouthOwnership.DriveLowerFace;
                if (ownsMouth && (renderer == null || renderer.sharedMesh == null))
                    return Fail(component, "Lower-face ownership requires a face SkinnedMeshRenderer with a readable mesh.");
                if (renderer != null && !renderer.transform.IsChildOf(avatarRoot.transform) && renderer.transform != avatarRoot.transform)
                    return Fail(component, "The face renderer must be inside the avatar hierarchy.");

                var sourceMesh = renderer != null ? renderer.sharedMesh : null;
                var blendShapeLinkCatalog = sourceBlendShapeLinkCatalog;
                if (ownsMouth && !blendShapeLinkCatalog.IsReliable)
                    return Fail(component,
                        "VRCFury BlendShape Link compatibility could not be verified. " +
                        blendShapeLinkCatalog.Error);
                var visemeNames = sourceMesh != null
                    ? ResolveVisemeNames(descriptor, sourceMesh)
                    : new string[VisemeReconstructionProfile.VisemeCount];
                var missingVisemes = new List<string>();
                for (var i = 1; ownsMouth && i < visemeNames.Length; i++)
                {
                    if (string.IsNullOrEmpty(visemeNames[i]) && profile.visemePoses[i].animationOverride == null)
                        missingVisemes.Add(VisemeReconstructionProfile.VisemeNames[i]);
                }
                if (missingVisemes.Count > 0)
                    return Fail(component,
                        $"Visemes '{string.Join("', '", missingVisemes)}' are not mapped and have no animation overrides.");

                var trackingEnabled = component.trackingInputs != AdvancedVisemeTrackingInputs.Disabled;
                var catalog = sourceTrackingCatalog;
                var existing = transaction.ExpressionParametersIncludingPlanned(
                    catalog.ExpressionParameters);
                var effectiveTrackingInputs = component.trackingInputs;
                var reuseExistingTracking = false;
                var trackingPrefix = component.NormalizedPrefix;
                var trackingActiveParameter = "LipTrackingActive";
                AnimatorControllerParameterType? trackingActiveAnimatorType = null;
                var trackingActiveDefault = 0f;
                Dictionary<AdvancedVisemeArticulator, string> trackingParameterNames = null;
                Dictionary<string, string> auxiliaryTrackingParameterNames = null;
                AdvancedVisemeTrackingResolution trackingResolution = null;

                if (trackingEnabled && (component.trackingInputs == AdvancedVisemeTrackingInputs.Auto ||
                                        component.trackingInputs == AdvancedVisemeTrackingInputs.ReuseExisting))
                {
                    trackingResolution = catalog.Resolve(profile, component.existingTrackingPrefix, out var resolutionError);
                    if (trackingResolution != null)
                    {
                        reuseExistingTracking = true;
                        effectiveTrackingInputs = trackingResolution.fullTongue
                            ? AdvancedVisemeTrackingInputs.FullTongue18
                            : trackingResolution.quality
                                ? AdvancedVisemeTrackingInputs.Quality12
                                : AdvancedVisemeTrackingInputs.Balanced8;
                        trackingPrefix = trackingResolution.prefix;
                        trackingActiveParameter = trackingResolution.activeParameter;
                        trackingActiveAnimatorType = trackingResolution.activeAnimatorType;
                        trackingActiveDefault = trackingResolution.activeAnimatorDefault;
                        trackingParameterNames = trackingResolution.parameters;
                        auxiliaryTrackingParameterNames = trackingResolution.auxiliaryParameters;
                    }
                    else if (component.trackingInputs == AdvancedVisemeTrackingInputs.ReuseExisting)
                    {
                        return Fail(component, resolutionError ?? "No compatible decoded VRCFaceTracking float source was found.");
                    }
                    else
                    {
                        // Auto may legitimately find no face-tracking installation,
                        // in which case speech-only fallback is safe. It must not,
                        // however, silently take lower-face ownership after finding
                        // an ambiguous or malformed installation: the generated
                        // Override layer would then hide the working template.
                        if (ownsMouth && !string.IsNullOrEmpty(resolutionError))
                            return Fail(component, resolutionError);
                        effectiveTrackingInputs = AdvancedVisemeTrackingInputs.Disabled;
                    }
                }

                trackingEnabled = ShouldEnableTracking(
                    component.trackingInputs, trackingResolution != null);

                if (trackingEnabled && !ValidateTrackingParameters(
                        component, profile, trackingPrefix, existing, effectiveTrackingInputs,
                        reuseExistingTracking, catalog, trackingParameterNames, trackingActiveParameter,
                        out var parameterError))
                    return Fail(component, parameterError);

                var externalPoses = reuseExistingTracking
                    ? catalog.ExtractPoses(trackingResolution)
                    : new Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose>();

                var resolvedBlendShapes = sourceMesh != null
                    ? ResolveArticulatorBlendShapes(sourceMesh, profile)
                    : new Dictionary<AdvancedVisemeArticulator, string>();
                var duplicateBlendShape = resolvedBlendShapes
                    .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(group => group.Count() > 1);
                if (duplicateBlendShape != null &&
                    component.mouthOwnership == AdvancedVisemeMouthOwnership.DriveLowerFace)
                {
                    return Fail(component,
                        $"Blendshape '{duplicateBlendShape.Key}' is mapped to multiple articulators " +
                        $"({string.Join(", ", duplicateBlendShape.Select(pair => pair.Key))}). Give each driven articulator a unique shape or animation override.");
                }
                var hasArticulatorAnimation = profile.articulatorBindings.Any(b =>
                    b != null && (b.animationOverride != null || b.negativeAnimationOverride != null)) ||
                    externalPoses.Count > 0;
                if (trackingEnabled && component.mouthOwnership == AdvancedVisemeMouthOwnership.DriveLowerFace &&
                    resolvedBlendShapes.Count == 0 && !hasArticulatorAnimation)
                {
                    return Fail(component, "Driven face tracking needs mapped articulator blendshapes or articulator animation overrides. Use Outputs Only for an unmapped rig.");
                }

                var rendererPath = renderer != null
                    ? AnimationUtility.CalculateTransformPath(renderer.transform, avatarRoot.transform)
                    : string.Empty;
                if (renderer != null && !HasUniqueAnimatorRendererBinding(
                        avatarRoot, renderer, out var rendererBindingError))
                    return Fail(component, rendererBindingError);
                var useSharedParameterCompressor =
                    component.tuningSyncMode ==
                    AdvancedVisemeTuningSyncMode.CompactSynced &&
                    avatarRoot.GetComponentsInChildren<ParameterCompressorData>(true)
                        .Length == 1;
                var hash = StableHash(
                    component, profile, sourceMesh, rendererPath,
                    catalog.DependencyFingerprint,
                    blendShapeLinkCatalog.DependencyFingerprint,
                    trackingEnabled,
                    useSharedParameterCompressor);
                // Keep primary calibration identities stable when a creator adds,
                // removes, or retargets a BlendShape Link. Linked geometry still
                // participates in the full build hash/folder, but it must not
                // rename curves on the authoritative face renderer.
                var primaryHash = StableHash(
                    component, profile, sourceMesh, rendererPath,
                    catalog.DependencyFingerprint,
                    string.Empty,
                    trackingEnabled,
                    useSharedParameterCompressor);
                var finalFolder =
                    $"{GeneratedRoot}/{Sanitize(avatarRoot.name)}_{hash.Substring(0, 12)}";
                var folder = transaction.StageGeneratedFolder(finalFolder);
                var primaryGeneratedPrefix = "YUCP_AVR_P_" + primaryHash.Substring(0, 10);

                AdvancedVisemeMeshCalibrator.Result calibration = null;
                Mesh generatedPrimaryMesh = null;
                IReadOnlyList<bool> observableCalibrationColumns = Array.Empty<bool>();
                var basis = sourceMesh != null
                    ? BuildCalibrationBasis(sourceMesh, resolvedBlendShapes)
                    : new List<AdvancedVisemeMeshCalibrator.BasisInput>();
                var hasVisemeOverrides = profile.visemePoses.Any(p => p != null && p.animationOverride != null);
                var hasArticulatorOverrides = profile.articulatorBindings.Any(binding =>
                    binding != null &&
                    (binding.animationOverride != null || binding.negativeAnimationOverride != null));
                if (component.mouthOwnership == AdvancedVisemeMouthOwnership.DriveLowerFace && sourceMesh != null &&
                    !hasVisemeOverrides && !hasArticulatorOverrides)
                {
                    var indices = visemeNames.Select(name => string.IsNullOrEmpty(name) ? -1 : sourceMesh.GetBlendShapeIndex(name)).ToArray();
                    if (reuseExistingTracking)
                    {
                        var poseBasis = new List<AdvancedVisemeMeshCalibrator.PoseBasisInput>();
                        foreach (var pair in externalPoses.OrderBy(pair => (int)pair.Key))
                        {
                            if (pair.Value?.positive != null)
                                poseBasis.Add(new AdvancedVisemeMeshCalibrator.PoseBasisInput(
                                    pair.Key, 1, pair.Value.positive, rendererPath));
                            if (pair.Value?.negative != null)
                                poseBasis.Add(new AdvancedVisemeMeshCalibrator.PoseBasisInput(
                                    pair.Key, -1, pair.Value.negative, rendererPath));
                        }

                        if (poseBasis.Count > 0)
                        {
                            observableCalibrationColumns = poseBasis
                                .Select(input => IsObservableCalibrationArticulator(
                                    input.articulator, trackingEnabled, true,
                                    effectiveTrackingInputs, trackingParameterNames,
                                    profile))
                                .ToArray();
                            try
                            {
                                calibration = AdvancedVisemeMeshCalibrator.BuildFromPoses(
                                    sourceMesh, indices, poseBasis,
                                    observableCalibrationColumns,
                                    primaryGeneratedPrefix);
                            }
                            catch (Exception exception)
                            {
                                Debug.LogWarning(
                                    $"[YUCP Advanced Viseme] Tailored tracking residual calibration was unavailable: " +
                                    $"{exception.Message}. Falling back to direct viseme poses.", component);
                                calibration = null;
                            }
                        }
                    }
                    else if (basis.Count > 0)
                    {
                        observableCalibrationColumns = basis
                            .Select(input => IsObservableCalibrationArticulator(
                                input.articulator, trackingEnabled, false,
                                effectiveTrackingInputs, trackingParameterNames,
                                profile))
                            .ToArray();
                        calibration = AdvancedVisemeMeshCalibrator.Build(
                            sourceMesh, indices, basis,
                            observableCalibrationColumns,
                            primaryGeneratedPrefix);
                    }

                    if (calibration != null && !calibration.success)
                    {
                        var source = reuseExistingTracking ? "Tailored tracking residual" : "Residual";
                        Debug.LogWarning(
                            $"[YUCP Advanced Viseme] {source} calibration was unavailable: " +
                            $"{calibration.error}. Falling back to direct viseme poses.", component);
                        calibration = null;
                    }

                    if (calibration != null)
                    {
                        var meshPath = folder + "/FaceMesh.asset";
                        AssetDatabase.CreateAsset(calibration.mesh, meshPath);
                        AssetDatabase.ImportAsset(meshPath);
                        generatedPrimaryMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    }
                }

                var linkedRendererOutputs = new List<AdvancedVisemeAnimatorBuilder.LinkedRendererOutput>();
                var linkedRendererSummary = LinkedRendererSummary.Empty;
                if (ownsMouth && calibration != null && calibration.success && sourceMesh != null)
                {
                    linkedRendererOutputs = BuildLinkedRendererOutputs(
                        avatarRoot, component, renderer, sourceMesh, rendererPath,
                        visemeNames, basis, calibration,
                        calibration.observableBasisColumns ??
                        observableCalibrationColumns.ToArray(),
                        blendShapeLinkCatalog, folder, out linkedRendererSummary);
                    renderer.sharedMesh = generatedPrimaryMesh;
                    EditorUtility.SetDirty(renderer);
                    var finalBlendShapeLinkCatalog =
                        AdvancedVisemeBlendShapeLinkCatalog.Scan(avatarRoot);
                    if (!blendShapeLinkCatalog.HasEquivalentResolvedMappings(
                            finalBlendShapeLinkCatalog, out var mappingDrift))
                    {
                        renderer.sharedMesh = sourceMesh;
                        foreach (var linked in linkedRendererOutputs)
                        {
                            if (linked?.renderer == null || linked.sourceMesh == null) continue;
                            linked.renderer.sharedMesh = linked.sourceMesh;
                        }
                        return Fail(component,
                            "VRCFury BlendShape Link mappings changed after reconstruction " +
                            "geometry was generated. The build was stopped to prevent a " +
                            "duplicate or missing mouth drive. " + mappingDrift);
                    }
                }

                var request = new AdvancedVisemeAnimatorBuilder.Request
                {
                    controllerPath = folder + "/AdvancedViseme.controller",
                    parametersPath = folder + "/TrackingParameters.asset",
                    rendererPath = rendererPath,
                    component = component,
                    profile = profile,
                    trackingPrefix = trackingPrefix,
                    effectiveTrackingInputs = effectiveTrackingInputs,
                    reuseExistingTracking = reuseExistingTracking,
                    trackingActiveParameter = trackingActiveParameter,
                    trackingActiveAnimatorType = trackingActiveAnimatorType,
                    trackingActiveDefault = trackingActiveDefault,
                    trackingParameterNames = trackingParameterNames,
                    auxiliaryTrackingParameterNames = auxiliaryTrackingParameterNames,
                    directPoseArticulators = trackingResolution != null
                        ? (IReadOnlyCollection<AdvancedVisemeArticulator>)
                            trackingResolution.directPoseArticulators
                        : Array.Empty<AdvancedVisemeArticulator>(),
                    sourceVisemeBlendShapes = visemeNames,
                    calibration = calibration,
                    calibrationBasis = basis,
                    linkedRendererOutputs = linkedRendererOutputs,
                    resolvedBlendShapes = resolvedBlendShapes,
                    externalPoses = externalPoses,
                    targetMesh = sourceMesh,
                    trackingEnabled = trackingEnabled,
                    useSharedParameterCompressor = useSharedParameterCompressor,
                    existingExpressionParameters = new HashSet<string>(existing.Keys, StringComparer.Ordinal)
                };
                if (!ValidateTuningParameters(request, existing, out var tuningError))
                    return Fail(component, tuningError);
                var built = AdvancedVisemeAnimatorBuilder.Build(request);
                transaction.RegisterGeneratedParameters(built.parameters);

                var controllerHost = new GameObject("__YUCP Advanced Viseme Controller");
                transaction.TrackCreatedObject(controllerHost);
                controllerHost.transform.SetParent(avatarRoot.transform, false);
                controllerHost.transform.SetAsLastSibling();
                var fullController = FuryComponents.CreateFullController(controllerHost);
                fullController.AddController(built.controller, VRCAvatarDescriptor.AnimLayerType.FX);
                if (built.parameters != null && built.parameters.parameters != null && built.parameters.parameters.Length > 0)
                    fullController.AddParams(built.parameters);
                foreach (var global in built.globalParameters.Concat(built.externalParameters).Distinct())
                    fullController.AddGlobalParam(global);

                if (built.tuningParameters.Count > 0)
                {
                    var tuningMenu = AdvancedVisemeRuntimeMenuBuilder.Build(
                        folder + "/TuningMenu.asset", built.tuningParameters,
                        built.tuningSyncFocusParameter);
                    fullController.AddMenu(tuningMenu, component.tuningMenuPath);
                }

                if (ShouldCreateTrackingToggle(component, trackingEnabled))
                {
                    var toggle = FuryComponents.CreateToggle(avatarRoot);
                    toggle.SetMenuPath(component.faceTrackingMenuPath);
                    toggle.SetDefaultOn();
                    toggle.SetGlobalParameter(built.manualTrackingParameter);
                }

                if (component.mouthOwnership == AdvancedVisemeMouthOwnership.DriveLowerFace)
                    descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly;

                if (calibration != null)
                {
                    profile.SetDiagnostics(calibration.fitRms, calibration.fitMaximum);
                    if (AssetDatabase.Contains(profile)) EditorUtility.SetDirty(profile);
                }

                var trackingBits = IncrementalTrackingBits(
                    component, profile, trackingPrefix, existing, effectiveTrackingInputs,
                    reuseExistingTracking, trackingActiveParameter);
                var calibrationText = calibration != null
                    ? component.reconstructionMode == AdvancedVisemeReconstructionMode.Normal
                        ? $", residual fit RMS {calibration.fitRms:G4} (exact convex reconstruction)"
                        : $", residual fit RMS {calibration.fitRms:G4} (exact Beta endpoints)"
                    : ", direct-pose fallback";
                var trackingText = trackingResolution != null ? $", {trackingResolution.Summary}" : string.Empty;
                var tuningText = built.tuningParameters.Count > 0
                    ? component.tuningSyncMode == AdvancedVisemeTuningSyncMode.CompactSynced
                        ? useSharedParameterCompressor
                            ? $", {built.tuningParameters.Count} saved sliders registered " +
                              "with the shared Parameter Compressor"
                            : $", {built.tuningParameters.Count} saved sliders shared through " +
                              $"{built.tuningSyncBits} compact synced bits"
                        : $", {built.tuningParameters.Count} saved local sliders (0 synced bits)"
                    : string.Empty;
                component.SetBuildSummary($"Built {built.globalParameters.Distinct().Count()} reusable outputs, +{trackingBits + built.tuningSyncBits} synced bits{tuningText}{trackingText}{calibrationText}{linkedRendererSummary.Text}");
                EditorUtility.SetDirty(component);
                EditorUtility.SetDirty(descriptor);

                if (component.verboseLogging)
                    Debug.Log($"[YUCP Advanced Viseme] {component.GetBuildSummary()}", component);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[YUCP Advanced Viseme] Build failed: {exception.Message}", component);
                Debug.LogException(exception, component);
                component.SetBuildSummary("Build failed");
                return false;
            }
            finally
            {
                if (temporaryProfile != null) UnityEngine.Object.DestroyImmediate(temporaryProfile);
            }
        }

        private static bool ValidateTrackingParameters(
            AdvancedVisemeReconstructorData component,
            VisemeReconstructionProfile profile,
            string prefix,
            Dictionary<string, VRCExpressionParameters.Parameter> existing,
            AdvancedVisemeTrackingInputs effectiveInputs,
            bool reuseExisting,
            AdvancedVisemeTrackingCatalog catalog,
            Dictionary<AdvancedVisemeArticulator, string> resolvedNames,
            string activeParameter,
            out string error)
        {
            error = null;
            var required = EnabledTrackingArticulators(effectiveInputs);
            foreach (var articulator in required)
            {
                if (reuseExisting)
                {
                    if (resolvedNames == null || !resolvedNames.TryGetValue(articulator, out var resolvedName))
                        continue; // Missing external channels fall back to reconstructed speech.
                    if (!catalog.HasAnimatorFloat(resolvedName))
                    {
                        error = $"Existing tracking source '{resolvedName}' must be a Float Animator parameter.";
                        return false;
                    }
                    if (catalog.Entries.TryGetValue(resolvedName, out var resolvedEntry) &&
                        resolvedEntry.expression != null &&
                        !resolvedEntry.expression.networkSynced)
                    {
                        error = $"Existing tracking source '{resolvedName}' is declared as an " +
                                "unsynced expression parameter. Use a synced wire channel or a " +
                                "controller-only decoded proxy so remote avatars receive tracking.";
                        return false;
                    }
                    continue;
                }

                var suffix = profile.FindBinding(articulator)?.trackingParameter;
                if (string.IsNullOrEmpty(suffix)) continue;
                var name = TrackingParameterName(prefix, suffix);
                if (UsesBinaryTracking(component))
                {
                    foreach (var bitName in BinaryParameterNames(name, articulator, component.trackingEncoding))
                    {
                        if (!existing.TryGetValue(bitName, out var bitParameter)) continue;
                        if (bitParameter.valueType != VRCExpressionParameters.ValueType.Bool)
                        {
                            error = $"Existing binary parameter '{bitName}' must be a Bool.";
                            return false;
                        }
                        if (!bitParameter.networkSynced)
                        {
                            error = $"Existing binary parameter '{bitName}' must be " +
                                    "network-synced so remote avatars receive face tracking.";
                            return false;
                        }
                    }
                }
                else
                {
                    if (existing.TryGetValue(name, out var parameter) &&
                        parameter.valueType != VRCExpressionParameters.ValueType.Float)
                    {
                        error = $"Existing parameter '{name}' must be a Float.";
                        return false;
                    }
                    if (parameter != null && !parameter.networkSynced)
                    {
                        error = $"Existing parameter '{name}' must be network-synced " +
                                "so remote avatars receive face tracking.";
                        return false;
                    }
                }
            }

            if (existing.TryGetValue(activeParameter, out var active) &&
                active.valueType != VRCExpressionParameters.ValueType.Bool)
            {
                error = $"Existing parameter '{activeParameter}' must be a Bool.";
                return false;
            }
            if (active != null && !active.networkSynced)
            {
                error = $"Existing parameter '{activeParameter}' must be network-synced " +
                        "so remote avatars receive tracking activity.";
                return false;
            }

            var manual = component.NormalizedPrefix + "/FaceTrackingEnabled";
            if (component.trackingInputs != AdvancedVisemeTrackingInputs.Auto &&
                component.createFaceTrackingToggle &&
                existing.TryGetValue(manual, out var manualParameter) &&
                manualParameter.valueType != VRCExpressionParameters.ValueType.Bool)
            {
                error = $"Existing parameter '{manual}' must be a Bool.";
                return false;
            }
            if (component.trackingInputs != AdvancedVisemeTrackingInputs.Auto &&
                component.createFaceTrackingToggle &&
                existing.TryGetValue(manual, out manualParameter) &&
                !manualParameter.networkSynced)
            {
                error = $"Existing parameter '{manual}' must be network-synced " +
                        "so the face-tracking toggle behaves consistently for remote avatars.";
                return false;
            }

            var currentBits = existing.Values.Where(p => p.networkSynced).Sum(ParameterBits);
            var incremental = IncrementalTrackingBits(
                component, profile, prefix, existing, effectiveInputs, reuseExisting, activeParameter);
            if (currentBits + incremental > ParameterBudget)
            {
                error = $"Face tracking needs {incremental} additional synced bits, but the avatar has only {ParameterBudget - currentBits} bits available.";
                return false;
            }
            return true;
        }

        private sealed class LinkedRendererPlan
        {
            private readonly Dictionary<string, IReadOnlyList<string>> linkedByBase;

            public readonly SkinnedMeshRenderer renderer;
            public readonly string rendererPath;

            public LinkedRendererPlan(
                SkinnedMeshRenderer renderer,
                string rendererPath,
                IEnumerable<AdvancedVisemeBlendShapeLinkCatalog.Mapping> mappings)
            {
                this.renderer = renderer;
                this.rendererPath = rendererPath;
                linkedByBase = mappings
                    .Distinct()
                    .GroupBy(mapping => mapping.baseBlendShape, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<string>)group
                            .Select(mapping => mapping.linkedBlendShape)
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(name => name, StringComparer.Ordinal)
                            .ToArray(),
                        StringComparer.Ordinal);
            }

            public IReadOnlyList<string> LinkedShapesFor(string baseBlendShape)
            {
                return !string.IsNullOrEmpty(baseBlendShape) &&
                       linkedByBase.TryGetValue(baseBlendShape, out var linked)
                    ? linked
                    : Array.Empty<string>();
            }
        }

        private sealed class PendingLinkedRendererOutput
        {
            public int planIndex;
            public LinkedRendererPlan plan;
            public AdvancedVisemeMeshCalibrator.Result calibration;
        }

        private readonly struct LinkedRendererSummary
        {
            public static readonly LinkedRendererSummary Empty =
                new LinkedRendererSummary(0, 0, 0);

            public readonly int calibrated;
            public readonly int nativeOnly;
            public readonly int skipped;

            public LinkedRendererSummary(int calibrated, int nativeOnly, int skipped)
            {
                this.calibrated = calibrated;
                this.nativeOnly = nativeOnly;
                this.skipped = skipped;
            }

            public string Text
            {
                get
                {
                    var parts = new List<string>();
                    if (calibrated > 0)
                        parts.Add($"{calibrated} exact VRCFury linked renderer{(calibrated == 1 ? string.Empty : "s")}");
                    if (nativeOnly > 0)
                        parts.Add($"{nativeOnly} articulation-only link{(nativeOnly == 1 ? string.Empty : "s")} kept native");
                    if (skipped > 0)
                        parts.Add($"{skipped} unsafe link{(skipped == 1 ? string.Empty : "s")} skipped");
                    return parts.Count == 0 ? string.Empty : ", " + string.Join(", ", parts);
                }
            }
        }

        private static List<AdvancedVisemeAnimatorBuilder.LinkedRendererOutput>
            BuildLinkedRendererOutputs(
                GameObject avatarRoot,
                AdvancedVisemeReconstructorData component,
                SkinnedMeshRenderer primaryRenderer,
                Mesh primarySourceMesh,
                string primaryRendererPath,
                IReadOnlyList<string> primaryVisemeNames,
                IReadOnlyList<AdvancedVisemeMeshCalibrator.BasisInput> primaryBasis,
                AdvancedVisemeMeshCalibrator.Result primaryCalibration,
                IReadOnlyList<bool> observableColumns,
                AdvancedVisemeBlendShapeLinkCatalog catalog,
                string folder,
                out LinkedRendererSummary summary)
        {
            var outputs = new List<AdvancedVisemeAnimatorBuilder.LinkedRendererOutput>();
            var pending = new List<PendingLinkedRendererOutput>();
            var calibratedCount = 0;
            var nativeOnlyCount = 0;
            var skippedCount = 0;
            var plans = BuildLinkedRendererPlans(
                avatarRoot, component, primaryRenderer, primaryRendererPath,
                primarySourceMesh, primaryVisemeNames, catalog, ref skippedCount);

            var coefficientColumns = primaryCalibration.coefficients?.GetLength(1) ?? 0;
            var externalAxes = primaryCalibration.poseBasisAxes ??
                               Array.Empty<AdvancedVisemeMeshCalibrator.PoseBasisAxis>();
            var expectedColumns = externalAxes.Length > 0
                ? externalAxes.Length
                : primaryBasis?.Count ?? 0;
            if (coefficientColumns != expectedColumns ||
                observableColumns == null || observableColumns.Count != coefficientColumns)
            {
                throw new InvalidOperationException(
                    "VRCFury BlendShape Link residuals cannot be generated because " +
                    "the primary calibration columns are not aligned.");
            }

            try
            {
                for (var planIndex = 0; planIndex < plans.Count; planIndex++)
                {
                    var plan = plans[planIndex];
                    var targetSourceMesh = plan.renderer.sharedMesh;
                    var targetVisemes = new AdvancedVisemeMeshCalibrator.BlendShapePoseInput[
                        VisemeReconstructionProfile.VisemeCount];
                    var hasMappedViseme = false;
                    for (var viseme = 0; viseme < targetVisemes.Length; viseme++)
                    {
                        targetVisemes[viseme] = BuildLinkedShapePose(
                            plan, targetSourceMesh,
                            primaryVisemeNames != null && viseme < primaryVisemeNames.Count
                                ? primaryVisemeNames[viseme]
                                : null,
                            100f);
                        hasMappedViseme |= targetVisemes[viseme].isMapped;
                    }

                    var targetBasis = new AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput[
                        coefficientColumns];
                    if (externalAxes.Length > 0)
                    {
                        for (var column = 0; column < targetBasis.Length; column++)
                        {
                            var axis = externalAxes[column];
                            targetBasis[column] = new AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput(
                                axis.articulator,
                                axis.direction,
                                BuildLinkedClipPose(
                                    plan, targetSourceMesh, axis.clip,
                                    axis.rendererPath));
                        }
                    }
                    else
                    {
                        for (var column = 0; column < targetBasis.Length; column++)
                        {
                            var input = primaryBasis[column];
                            var sourceShape = input.blendShapeIndex >= 0 &&
                                              input.blendShapeIndex < primarySourceMesh.blendShapeCount
                                ? primarySourceMesh.GetBlendShapeName(input.blendShapeIndex)
                                : null;
                            targetBasis[column] = new AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput(
                                input.articulator,
                                1,
                                BuildLinkedShapePose(
                                    plan, targetSourceMesh, sourceShape, 100f));
                        }
                    }

                    // Unsigned articulation-only links remain entirely native to
                    // VRCFury. Signed coordinates are different: VRCFury's copied
                    // positive shape is clamped at zero for a negative value, so a
                    // mapped signed basis still needs a target-local -U carrier even
                    // when the target has no authored Oculus visemes.
                    if (!RequiresLinkedRendererCalibration(
                            hasMappedViseme, targetBasis))
                    {
                        nativeOnlyCount++;
                        continue;
                    }

                    var stableToken = "T" + planIndex.ToString("D2") + "_" +
                                      Hash128.Compute(plan.rendererPath + "|" +
                                                      catalog.DependencyFingerprint)
                                          .ToString().Substring(0, 10);
                    var linkedCalibration = BuildCollisionFreeLinkedCalibration(
                        primarySourceMesh,
                        targetSourceMesh,
                        targetVisemes,
                        targetBasis,
                        primaryCalibration.coefficients,
                        observableColumns,
                        stableToken);
                    if (linkedCalibration == null || !linkedCalibration.success)
                    {
                        if (linkedCalibration?.mesh != null)
                            UnityEngine.Object.DestroyImmediate(linkedCalibration.mesh);
                        throw new InvalidOperationException(
                            $"VRCFury BlendShape Link target '{DisplayPath(plan.rendererPath)}' " +
                            $"cannot preserve authored viseme detail: " +
                            $"{linkedCalibration?.error ?? "unknown calibration error"}.");
                    }

                    pending.Add(new PendingLinkedRendererOutput
                    {
                        planIndex = planIndex,
                        plan = plan,
                        calibration = linkedCalibration
                    });
                }
            }
            catch
            {
                foreach (var item in pending)
                    if (item.calibration?.mesh != null)
                        UnityEngine.Object.DestroyImmediate(item.calibration.mesh);
                throw;
            }

            foreach (var item in pending)
            {
                var targetSourceMesh = item.plan.renderer.sharedMesh;
                var pathHash = Hash128.Compute(item.plan.rendererPath).ToString().Substring(0, 8);
                var meshPath = folder + "/LinkedFace_" + item.planIndex.ToString("D2") + "_" +
                               Sanitize(item.plan.renderer.gameObject.name) + "_" + pathHash + ".asset";
                AssetDatabase.CreateAsset(item.calibration.mesh, meshPath);
                AssetDatabase.ImportAsset(meshPath);
                item.plan.renderer.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                EditorUtility.SetDirty(item.plan.renderer);

                outputs.Add(new AdvancedVisemeAnimatorBuilder.LinkedRendererOutput
                {
                    rendererPath = item.plan.rendererPath,
                    label = DisplayPath(item.plan.rendererPath),
                    renderer = item.plan.renderer,
                    sourceMesh = targetSourceMesh,
                    calibration = item.calibration
                });
                calibratedCount++;
            }

            summary = new LinkedRendererSummary(
                calibratedCount, nativeOnlyCount, skippedCount);
            return outputs;
        }

        internal static bool RequiresLinkedRendererCalibration(
            bool hasMappedViseme,
            IReadOnlyList<AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput> targetBasis)
        {
            if (hasMappedViseme) return true;
            if (targetBasis == null) return false;
            for (var column = 0; column < targetBasis.Count; column++)
            {
                var input = targetBasis[column];
                if (input.pose.isMapped &&
                    AdvancedVisemeMath.IsSignedTrackingArticulator(input.articulator))
                    return true;
            }
            return false;
        }

        private static List<LinkedRendererPlan> BuildLinkedRendererPlans(
            GameObject avatarRoot,
            AdvancedVisemeReconstructorData component,
            SkinnedMeshRenderer primaryRenderer,
            string primaryRendererPath,
            Mesh primarySourceMesh,
            IReadOnlyList<string> primaryVisemeNames,
            AdvancedVisemeBlendShapeLinkCatalog catalog,
            ref int skippedCount)
        {
            var plans = new List<LinkedRendererPlan>();
            if (catalog == null || primaryRenderer == null || primarySourceMesh == null)
                return plans;

            // Discover the complete reachable graph so direct links can be
            // calibrated and indirect authored-viseme chains can fail closed.
            // Installed VRCFury applies each feature once, so treating graph
            // reachability as order-independent runtime propagation would be
            // incorrect. The fixed-point queue is used only for provenance and is
            // cycle-safe because every renderer has a finite name relation.
            var outgoing = catalog.Targets
                .Where(target => target?.baseRenderer != null &&
                                 target.linkedRenderer != null)
                .GroupBy(target => target.baseRenderer)
                .ToDictionary(group => group.Key, group => group.ToArray());
            var composed = new Dictionary<SkinnedMeshRenderer,
                HashSet<AdvancedVisemeBlendShapeLinkCatalog.Mapping>>();
            var unsafeReasons = new Dictionary<SkinnedMeshRenderer, HashSet<string>>();
            var identity = new HashSet<AdvancedVisemeBlendShapeLinkCatalog.Mapping>();
            for (var index = 0; index < primarySourceMesh.blendShapeCount; index++)
            {
                var name = primarySourceMesh.GetBlendShapeName(index);
                identity.Add(new AdvancedVisemeBlendShapeLinkCatalog.Mapping(name, name));
            }
            composed[primaryRenderer] = identity;
            var depth = new Dictionary<SkinnedMeshRenderer, int>
            {
                [primaryRenderer] = 0
            };
            var queue = new Queue<SkinnedMeshRenderer>();
            var queued = new HashSet<SkinnedMeshRenderer> { primaryRenderer };
            queue.Enqueue(primaryRenderer);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                queued.Remove(current);
                if (!outgoing.TryGetValue(current, out var edges)) continue;
                foreach (var edgeGroup in edges.GroupBy(edge => edge.linkedRenderer))
                {
                    var linked = edgeGroup.Key;
                    if (linked == null) continue;
                    if (linked == primaryRenderer)
                    {
                        if (current == primaryRenderer)
                        {
                            var authoredVisemeSources = edgeGroup
                                .SelectMany(edge => edge.mappings)
                                .Select(mapping => mapping.baseBlendShape)
                                .Where(name => !string.IsNullOrEmpty(name) &&
                                               primaryVisemeNames != null &&
                                               primaryVisemeNames.Contains(name))
                                .Distinct(StringComparer.Ordinal)
                                .OrderBy(name => name, StringComparer.Ordinal)
                                .ToArray();
                            if (authoredVisemeSources.Length > 0)
                                throw new InvalidOperationException(
                                    $"VRCFury BlendShape Link on the primary face " +
                                    $"'{DisplayPath(primaryRendererPath)}' maps authored Oculus " +
                                    $"viseme source(s) back onto the same renderer " +
                                    $"({string.Join(", ", authoredVisemeSources)}). This self-link " +
                                    $"cannot preserve Advanced Viseme ownership; remove those " +
                                    $"viseme mappings or link them to a different renderer.");

                            var renamedMappings = edgeGroup
                                .SelectMany(edge => edge.mappings)
                                .Where(mapping => !string.Equals(
                                    mapping.baseBlendShape,
                                    mapping.linkedBlendShape,
                                    StringComparison.Ordinal))
                                .Distinct()
                                .OrderBy(mapping => mapping.baseBlendShape,
                                    StringComparer.Ordinal)
                                .ThenBy(mapping => mapping.linkedBlendShape,
                                    StringComparer.Ordinal)
                                .ToArray();
                            if (renamedMappings.Length > 0)
                                throw new InvalidOperationException(
                                    $"VRCFury BlendShape Link on the primary face " +
                                    $"'{DisplayPath(primaryRendererPath)}' renames blendshapes " +
                                    "back onto the same renderer (" +
                                    string.Join(", ", renamedMappings.Select(mapping =>
                                        $"{mapping.baseBlendShape} -> {mapping.linkedBlendShape}")) +
                                    "). Only identity articulation self-links are inert; " +
                                    "move renamed mappings to a separate renderer.");

                            // Articulation-only self-links do not need an AVR overlay. Leave
                            // their native VRCFury behavior alone without making unrelated
                            // direct links inherit a false cycle diagnostic.
                            continue;
                        }
                        AddUnsafeLinkReason(
                            unsafeReasons, current,
                            "the reachable BlendShape Link graph contains a cycle back to the primary face");
                        continue;
                    }
                    if (!composed.TryGetValue(linked, out var targetMappings))
                    {
                        targetMappings = new HashSet<AdvancedVisemeBlendShapeLinkCatalog.Mapping>();
                        composed[linked] = targetMappings;
                    }
                    var candidateDepth = depth[current] + 1;
                    if (!depth.TryGetValue(linked, out var existingDepth) ||
                        candidateDepth < existingDepth)
                        depth[linked] = candidateDepth;

                    var edgeMappings = edgeGroup.SelectMany(edge => edge.mappings)
                        .Distinct()
                        .ToLookup(mapping => mapping.baseBlendShape, StringComparer.Ordinal);
                    var changed = false;
                    foreach (var upstream in composed[current])
                    foreach (var edge in edgeMappings[upstream.linkedBlendShape])
                    {
                        changed |= targetMappings.Add(
                            new AdvancedVisemeBlendShapeLinkCatalog.Mapping(
                                upstream.baseBlendShape,
                                edge.linkedBlendShape));
                    }

                    var diagnostics = edgeGroup
                        .Where(edge => !edge.isExact)
                        .Select(edge => edge.diagnostic)
                        .Where(value => !string.IsNullOrEmpty(value));
                    foreach (var diagnostic in diagnostics)
                        AddUnsafeLinkReason(unsafeReasons, linked, diagnostic);
                    if (unsafeReasons.TryGetValue(current, out var inherited))
                    foreach (var reason in inherited)
                        AddUnsafeLinkReason(unsafeReasons, linked, reason);

                    if (changed && queued.Add(linked)) queue.Enqueue(linked);
                }
            }

            foreach (var pair in composed
                         .Where(pair => pair.Key != primaryRenderer)
                         .OrderBy(pair => AnimationUtility.CalculateTransformPath(
                                 pair.Key.transform, avatarRoot.transform),
                             StringComparer.Ordinal))
            {
                var linkedRenderer = pair.Key;
                var path = AnimationUtility.CalculateTransformPath(
                    linkedRenderer.transform, avatarRoot.transform);
                var mappings = pair.Value
                    .OrderBy(mapping => mapping.baseBlendShape, StringComparer.Ordinal)
                    .ThenBy(mapping => mapping.linkedBlendShape, StringComparer.Ordinal)
                    .ToArray();
                var combinedAmbiguities = mappings
                    .GroupBy(mapping => mapping.linkedBlendShape, StringComparer.Ordinal)
                    .Where(mappingGroup => mappingGroup
                        .Select(mapping => mapping.baseBlendShape)
                        .Distinct(StringComparer.Ordinal)
                        .Count() > 1)
                    .Select(mappingGroup => mappingGroup.Key + " <- " +
                                            string.Join(", ", mappingGroup
                                                .Select(mapping => mapping.baseBlendShape)
                                                .Distinct(StringComparer.Ordinal)
                                                .OrderBy(name => name, StringComparer.Ordinal)))
                    .ToArray();
                foreach (var ambiguity in combinedAmbiguities)
                    AddUnsafeLinkReason(unsafeReasons, linkedRenderer, ambiguity);

                if (depth.TryGetValue(linkedRenderer, out var linkDepth) && linkDepth > 1)
                    AddUnsafeLinkReason(
                        unsafeReasons, linkedRenderer,
                        "indirect BlendShape Link chains depend on VRCFury feature order; " +
                        "link this renderer directly to the primary face");

                var ownedTargetShapes = mappings
                    .Select(mapping => mapping.linkedBlendShape)
                    .ToHashSet(StringComparer.Ordinal);
                var competingIncoming = catalog.Targets
                    .Where(target => target.linkedRenderer == linkedRenderer &&
                                     target.baseRenderer != primaryRenderer)
                    .SelectMany(target => target.mappings.Select(mapping => new
                    {
                        target.basePath,
                        mapping.baseBlendShape,
                        mapping.linkedBlendShape
                    }))
                    .Where(mapping => ownedTargetShapes.Contains(mapping.linkedBlendShape))
                    .Select(mapping => $"{DisplayPath(mapping.basePath)}/" +
                                       $"{mapping.baseBlendShape} -> {mapping.linkedBlendShape}")
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                if (competingIncoming.Length > 0)
                    AddUnsafeLinkReason(
                        unsafeReasons, linkedRenderer,
                        "another base renderer writes AVR-owned target shapes: " +
                        string.Join(", ", competingIncoming));

                string reason = null;
                if (linkedRenderer.sharedMesh == null)
                    reason = "the linked renderer has no mesh";
                else if (linkedRenderer.transform != avatarRoot.transform &&
                         !linkedRenderer.transform.IsChildOf(avatarRoot.transform))
                    reason = "the linked renderer is outside the avatar hierarchy";
                else if (!HasUniqueAnimatorRendererBinding(
                             avatarRoot, linkedRenderer, out var bindingError))
                    reason = bindingError;
                else if (string.Equals(path, primaryRendererPath, StringComparison.Ordinal))
                    reason = "its Animator binding is indistinguishable from the primary renderer";
                else if (unsafeReasons.TryGetValue(linkedRenderer, out var reasons) &&
                         reasons.Count > 0)
                    reason = string.Join("; ", reasons.OrderBy(value => value, StringComparer.Ordinal));
                else if (mappings.Length == 0)
                    reason = "no original face blendshapes reach this renderer";

                if (reason != null)
                {
                    if (MappingsContainAuthoredViseme(mappings, primaryVisemeNames))
                        throw new InvalidOperationException(
                            $"VRCFury BlendShape Link target '{DisplayPath(path)}' cannot " +
                            $"preserve authored viseme detail: {reason}.");
                    skippedCount++;
                    Debug.LogWarning(
                        $"[YUCP Advanced Viseme] Skipped VRCFury BlendShape Link target " +
                        $"'{DisplayPath(path)}': {reason}.", component);
                    continue;
                }
                plans.Add(new LinkedRendererPlan(linkedRenderer, path, mappings));
            }
            return plans;
        }

        private static bool MappingsContainAuthoredViseme(
            IEnumerable<AdvancedVisemeBlendShapeLinkCatalog.Mapping> mappings,
            IReadOnlyList<string> primaryVisemeNames)
        {
            if (mappings == null || primaryVisemeNames == null) return false;
            var visemes = primaryVisemeNames
                .Where(name => !string.IsNullOrEmpty(name))
                .ToHashSet(StringComparer.Ordinal);
            return mappings.Any(mapping => visemes.Contains(mapping.baseBlendShape));
        }

        private static void AddUnsafeLinkReason(
            IDictionary<SkinnedMeshRenderer, HashSet<string>> reasons,
            SkinnedMeshRenderer renderer,
            string reason)
        {
            if (renderer == null || string.IsNullOrWhiteSpace(reason)) return;
            if (!reasons.TryGetValue(renderer, out var values))
            {
                values = new HashSet<string>(StringComparer.Ordinal);
                reasons[renderer] = values;
            }
            values.Add(reason);
        }

        private static AdvancedVisemeMeshCalibrator.BlendShapePoseInput BuildLinkedShapePose(
            LinkedRendererPlan plan,
            Mesh targetMesh,
            string primaryShape,
            float endpointWeight)
        {
            if (plan == null || targetMesh == null || string.IsNullOrEmpty(primaryShape))
                return default;
            var elements = plan.LinkedShapesFor(primaryShape)
                .Select(targetMesh.GetBlendShapeIndex)
                .Where(index => index >= 0)
                .Distinct()
                .OrderBy(index => index)
                .Select(index => new AdvancedVisemeMeshCalibrator.BlendShapePoseElement(
                    index, endpointWeight))
                .ToArray();
            return elements.Length == 0
                ? default
                : new AdvancedVisemeMeshCalibrator.BlendShapePoseInput(elements);
        }

        private static AdvancedVisemeMeshCalibrator.BlendShapePoseInput BuildLinkedClipPose(
            LinkedRendererPlan plan,
            Mesh targetMesh,
            AnimationClip clip,
            string primaryRendererPath)
        {
            if (plan == null || targetMesh == null || clip == null)
                return default;
            var elements = new Dictionary<int, float>();
            var sampleTime = Mathf.Max(0f, clip.length);
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(SkinnedMeshRenderer) ||
                    !string.Equals(binding.path, primaryRendererPath, StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(binding.propertyName) ||
                    !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                    continue;
                var primaryShape = binding.propertyName.Substring("blendShape.".Length);
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) continue;
                var endpointWeight = curve.Evaluate(sampleTime);
                if (Mathf.Abs(endpointWeight) <= 1e-5f) continue;
                foreach (var targetShape in plan.LinkedShapesFor(primaryShape))
                {
                    var targetIndex = targetMesh.GetBlendShapeIndex(targetShape);
                    if (targetIndex >= 0) elements[targetIndex] = endpointWeight;
                }
            }
            return elements.Count == 0
                ? default
                : new AdvancedVisemeMeshCalibrator.BlendShapePoseInput(elements
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new AdvancedVisemeMeshCalibrator.BlendShapePoseElement(
                        pair.Key, pair.Value))
                    .ToArray());
        }

        private static AdvancedVisemeMeshCalibrator.Result BuildCollisionFreeLinkedCalibration(
            Mesh primarySourceMesh,
            Mesh targetSourceMesh,
            IReadOnlyList<AdvancedVisemeMeshCalibrator.BlendShapePoseInput> targetVisemes,
            IReadOnlyList<AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput> targetBasis,
            float[,] referenceCoefficients,
            IReadOnlyList<bool> observableColumns,
            string stableToken)
        {
            AdvancedVisemeMeshCalibrator.Result result = null;
            for (var attempt = 0; attempt < 32; attempt++)
            {
                result = AdvancedVisemeMeshCalibrator.BuildLinkedTarget(
                    targetSourceMesh,
                    targetVisemes,
                    targetBasis,
                    referenceCoefficients,
                    attempt == 0 ? stableToken : stableToken + "_" + (attempt + 1),
                    observableColumns);
                if (result == null || !result.success ||
                    !GeneratedNamesOverlapPrimary(primarySourceMesh, result))
                    return result;
                UnityEngine.Object.DestroyImmediate(result.mesh);
                result = null;
            }
            return new AdvancedVisemeMeshCalibrator.Result
            {
                error = "Could not allocate a target-only residual blendshape namespace."
            };
        }

        private static bool GeneratedNamesOverlapPrimary(
            Mesh primarySourceMesh,
            AdvancedVisemeMeshCalibrator.Result result)
        {
            if (primarySourceMesh == null || result == null) return false;
            var generated = (result.residualBlendShapeNames ?? Array.Empty<string>())
                .Concat(result.ownershipCarrierBlendShapeNames ?? Array.Empty<string>())
                .Concat(result.ownershipNegativeCarrierBlendShapeNames ?? Array.Empty<string>())
                .Concat(new[] { result.hiddenPhoneResidualBlendShapeName })
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(NormalizeVrcFuryShapeName)
                .ToHashSet(StringComparer.Ordinal);
            for (var index = 0; index < primarySourceMesh.blendShapeCount; index++)
            {
                if (generated.Contains(NormalizeVrcFuryShapeName(
                        primarySourceMesh.GetBlendShapeName(index))))
                    return true;
            }
            return false;
        }

        private static string NormalizeVrcFuryShapeName(string value)
        {
            return new string((value ?? string.Empty)
                .Where(character => !char.IsWhiteSpace(character))
                .Select(char.ToLower)
                .ToArray());
        }

        private static string DisplayPath(string path)
        {
            return string.IsNullOrEmpty(path) ? "<avatar root>" : path;
        }

        private static bool HasUniqueAnimatorRendererBinding(
            GameObject avatarRoot,
            SkinnedMeshRenderer renderer,
            out string error)
        {
            error = null;
            if (avatarRoot == null || renderer == null)
            {
                error = "The face renderer or avatar root is missing.";
                return false;
            }

            var path = AnimationUtility.CalculateTransformPath(
                renderer.transform, avatarRoot.transform);
            var matchingTransforms = avatarRoot.GetComponentsInChildren<Transform>(true)
                .Where(transform => string.Equals(
                    AnimationUtility.CalculateTransformPath(
                        transform, avatarRoot.transform),
                    path,
                    StringComparison.Ordinal))
                .ToArray();
            if (matchingTransforms.Length != 1 || matchingTransforms[0] != renderer.transform)
            {
                error = $"Animator path '{DisplayPath(path)}' is ambiguous because the avatar " +
                        "contains same-named hierarchy branches. Give the renderer a unique path.";
                return false;
            }

            var renderersOnObject = renderer.GetComponents<SkinnedMeshRenderer>();
            if (renderersOnObject.Length != 1)
            {
                error = $"Animator path '{DisplayPath(path)}' contains " +
                        $"{renderersOnObject.Length} SkinnedMeshRenderers on one GameObject. " +
                        "Blendshape curves cannot select one of them; put each renderer on its own object.";
                return false;
            }
            return true;
        }

        private static bool ValidateTuningParameters(
            AdvancedVisemeAnimatorBuilder.Request request,
            IReadOnlyDictionary<string, VRCExpressionParameters.Parameter> existing,
            out string error)
        {
            error = null;
            if (!request.component.createTuningMenu) return true;
            var generatedControls = new List<AdvancedVisemeTuningControl>();
            foreach (var control in AdvancedVisemeTuning.Controls)
            {
                if ((request.component.tuningMenuSections &
                     AdvancedVisemeTuning.Section(control)) == 0 ||
                    !AdvancedVisemeAnimatorBuilder.IsTuningControlRelevant(request, control))
                    continue;
                generatedControls.Add(control);
                var name = request.component.TuningParameterName(control);
                if (!existing.TryGetValue(name, out var parameter)) continue;
                if (parameter.valueType != VRCExpressionParameters.ValueType.Float)
                {
                    error = $"Existing tuning parameter '{name}' must be Float, but is " +
                            $"{parameter.valueType}. Change its type or use another parameter prefix.";
                    return false;
                }
                if (parameter.networkSynced &&
                    !request.useSharedParameterCompressor)
                {
                    error = $"Existing tuning parameter '{name}' is synced. Tuning sliders are " +
                            "local-only by design; make it unsynced or use another parameter prefix.";
                    return false;
                }
                if (parameter.saved != request.component.saveTuningValues)
                {
                    error = $"Existing tuning parameter '{name}' has Saved=" +
                            $"{parameter.saved}, but this component requests Saved=" +
                            $"{request.component.saveTuningValues}. Match the setting or use another prefix.";
                    return false;
                }
            }

            if (request.component.tuningSyncMode !=
                    AdvancedVisemeTuningSyncMode.CompactSynced ||
                generatedControls.Count == 0)
                return true;

            // The generic compressor runs against the final merged assets. AVR
            // must expose its saved tuning values as ordinary synchronized
            // candidates here instead of reserving its private carrier budget.
            if (request.useSharedParameterCompressor) return true;

            var dataParameter = AdvancedVisemeTuning.CompactSyncDataParameter(
                request.component.NormalizedPrefix);
            if (!ValidateCompactTuningParameter(
                    existing, dataParameter,
                    VRCExpressionParameters.ValueType.Int,
                    false, true, out error))
                return false;

            var focusParameter = AdvancedVisemeTuning.CompactSyncFocusParameter(
                request.component.NormalizedPrefix);
            if (!ValidateCompactTuningParameter(
                    existing, focusParameter,
                    VRCExpressionParameters.ValueType.Int,
                    false, false, out error))
                return false;

            var indexBits = AdvancedVisemeTuning.CompactSyncTransportIndexBits;
            var incrementalTuningBits = existing.ContainsKey(dataParameter) ? 0 : 8;
            for (var bit = 0; bit < indexBits; bit++)
            {
                var name = AdvancedVisemeTuning.CompactSyncIndexParameter(
                    request.component.NormalizedPrefix, bit);
                if (!ValidateCompactTuningParameter(
                        existing, name,
                        VRCExpressionParameters.ValueType.Bool,
                        false, true, out error))
                    return false;
                if (!existing.ContainsKey(name)) incrementalTuningBits++;
            }

            var currentBits = existing.Values
                .Where(parameter => parameter.networkSynced)
                .Sum(ParameterBits);
            var trackingBits = IncrementalTrackingBits(
                request.component, request.profile, request.trackingPrefix,
                existing,
                request.effectiveTrackingInputs, request.reuseExistingTracking,
                request.trackingActiveParameter);
            if (currentBits + trackingBits + incrementalTuningBits > ParameterBudget)
            {
                error = $"Compact viseme-setting sync needs {incrementalTuningBits} " +
                        $"additional bits, but only " +
                        $"{ParameterBudget - currentBits - trackingBits} remain after " +
                        $"face-tracking inputs.";
                return false;
            }
            return true;
        }

        private static bool ValidateCompactTuningParameter(
            IReadOnlyDictionary<string, VRCExpressionParameters.Parameter> existing,
            string name,
            VRCExpressionParameters.ValueType type,
            bool saved,
            bool synced,
            out string error)
        {
            error = null;
            if (!existing.TryGetValue(name, out var parameter)) return true;
            if (parameter.valueType != type ||
                parameter.saved != saved ||
                parameter.networkSynced != synced)
            {
                error = $"Existing compact tuning parameter '{name}' must be " +
                        $"{type}, Saved={saved}, Synced={synced}. Change the " +
                        $"conflicting parameter or use another prefix.";
                return false;
            }
            return true;
        }

        private static int IncrementalTrackingBits(
            AdvancedVisemeReconstructorData component,
            VisemeReconstructionProfile profile,
            string prefix,
            IReadOnlyDictionary<string, VRCExpressionParameters.Parameter> existing,
            AdvancedVisemeTrackingInputs effectiveInputs,
            bool reuseExisting,
            string activeParameter)
        {
            if (effectiveInputs == AdvancedVisemeTrackingInputs.Disabled) return 0;
            var bits = 0;
            if (!reuseExisting)
            {
                var required = EnabledTrackingArticulators(effectiveInputs);
                foreach (var articulator in required)
                {
                    var suffix = profile.FindBinding(articulator)?.trackingParameter;
                    if (string.IsNullOrEmpty(suffix)) continue;
                    var name = TrackingParameterName(prefix, suffix);
                    if (UsesBinaryTracking(component))
                    {
                        foreach (var bitName in BinaryParameterNames(name, articulator, component.trackingEncoding))
                            if (!existing.ContainsKey(bitName)) bits += 1;
                    }
                    else if (!existing.ContainsKey(name)) bits += 8;
                }
            }
            if (component.trackingInputs != AdvancedVisemeTrackingInputs.Auto)
            {
                if (!existing.ContainsKey(activeParameter)) bits += 1;
                if (component.createFaceTrackingToggle &&
                    !existing.ContainsKey(component.NormalizedPrefix + "/FaceTrackingEnabled")) bits += 1;
            }
            return bits;
        }

        internal static bool ShouldCreateTrackingToggle(
            AdvancedVisemeReconstructorData component,
            bool trackingEnabled)
        {
            return trackingEnabled &&
                   component != null &&
                   component.trackingInputs != AdvancedVisemeTrackingInputs.Auto &&
                   component.createFaceTrackingToggle;
        }

        private static int ParameterBits(VRCExpressionParameters.Parameter parameter)
        {
            return parameter.valueType == VRCExpressionParameters.ValueType.Bool ? 1 : 8;
        }

        internal static string[] ResolveVisemeNames(VRCAvatarDescriptor descriptor, Mesh mesh)
        {
            var output = new string[VisemeReconstructionProfile.VisemeCount];
            var field = typeof(VRCAvatarDescriptor).GetField("VisemeBlendShapes", BindingFlags.Public | BindingFlags.Instance);
            var mapped = field?.GetValue(descriptor) as string[];
            for (var i = 0; i < output.Length; i++)
            {
                if (mapped != null && i < mapped.Length && mesh.GetBlendShapeIndex(mapped[i]) >= 0)
                    output[i] = mapped[i];
            }

            for (var i = 0; i < output.Length; i++)
            {
                if (!string.IsNullOrEmpty(output[i])) continue;
                output[i] = FindBlendShape(mesh, VRChatVisemeDetector.GetVisemeNameCandidates(i));
            }
            return output;
        }

        internal static Dictionary<AdvancedVisemeArticulator, string> ResolveArticulatorBlendShapes(
            Mesh mesh,
            VisemeReconstructionProfile profile)
        {
            var output = new Dictionary<AdvancedVisemeArticulator, string>();
            foreach (var binding in profile.articulatorBindings)
            {
                if (binding == null) continue;
                var explicitName = binding.blendShapeName;
                var aliases = ArticulatorAliases(binding.articulator, binding.trackingParameter);
                var found = FindBlendShape(mesh, new[] { explicitName }.Concat(aliases).Where(s => !string.IsNullOrEmpty(s)).ToArray());
                if (!string.IsNullOrEmpty(found)) output[binding.articulator] = found;
            }
            return output;
        }

        internal static List<AdvancedVisemeMeshCalibrator.BasisInput> BuildCalibrationBasis(
            Mesh mesh,
            Dictionary<AdvancedVisemeArticulator, string> resolved)
        {
            var result = new List<AdvancedVisemeMeshCalibrator.BasisInput>();
            var used = new HashSet<int>();
            foreach (var pair in resolved)
            {
                var index = mesh.GetBlendShapeIndex(pair.Value);
                if (index < 0 || !used.Add(index)) continue;
                result.Add(new AdvancedVisemeMeshCalibrator.BasisInput(pair.Key, index));
            }
            return result;
        }

        private static string[] ArticulatorAliases(AdvancedVisemeArticulator articulator, string tracking)
        {
            switch (articulator)
            {
                case AdvancedVisemeArticulator.JawOpen: return new[] { tracking, "jawOpen" };
                case AdvancedVisemeArticulator.LipClose: return new[] { tracking, "MouthClose", "mouthClose" };
                case AdvancedVisemeArticulator.MouthOpen: return new[] { tracking, "mouthOpen" };
                case AdvancedVisemeArticulator.LipFunnel: return new[] { tracking, "MouthFunnel", "mouthFunnel" };
                case AdvancedVisemeArticulator.LipPucker: return new[] { tracking, "MouthPucker", "mouthPucker" };
                case AdvancedVisemeArticulator.LipSuck: return new[] { tracking, "MouthRollLower", "mouthRollLower" };
                case AdvancedVisemeArticulator.SmileSad: return new[] { tracking, "SmileFrown", "SmileSad" };
                case AdvancedVisemeArticulator.TongueOut: return new[] { tracking, "tongueOut" };
                default: return new[] { tracking, articulator.ToString() };
            }
        }

        internal static bool ShouldEnableTracking(
            AdvancedVisemeTrackingInputs mode,
            bool compatibleExistingSource)
        {
            return mode != AdvancedVisemeTrackingInputs.Disabled &&
                   (mode != AdvancedVisemeTrackingInputs.Auto || compatibleExistingSource);
        }

        private static IEnumerable<AdvancedVisemeArticulator> EnabledTrackingArticulators(
            AdvancedVisemeTrackingInputs mode)
        {
            if (mode == AdvancedVisemeTrackingInputs.Disabled ||
                mode == AdvancedVisemeTrackingInputs.Auto ||
                mode == AdvancedVisemeTrackingInputs.ReuseExisting)
                yield break;

            foreach (var articulator in BalancedArticulators) yield return articulator;
            if (mode == AdvancedVisemeTrackingInputs.Quality12 ||
                mode == AdvancedVisemeTrackingInputs.FullTongue18)
            {
                foreach (var articulator in QualityExtraArticulators) yield return articulator;
            }
            if (mode == AdvancedVisemeTrackingInputs.FullTongue18)
            {
                foreach (var articulator in FullTongueExtraArticulators) yield return articulator;
            }
        }

        internal static bool IsObservableCalibrationArticulator(
            AdvancedVisemeArticulator articulator,
            bool trackingEnabled,
            bool reuseExistingTracking,
            AdvancedVisemeTrackingInputs effectiveInputs,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> resolvedTrackingNames,
            VisemeReconstructionProfile profile)
        {
            if (!trackingEnabled) return false;
            if (reuseExistingTracking)
                return resolvedTrackingNames != null &&
                       resolvedTrackingNames.TryGetValue(articulator, out var name) &&
                       !string.IsNullOrEmpty(name);
            return EnabledTrackingArticulators(effectiveInputs).Contains(articulator) &&
                   !string.IsNullOrEmpty(profile?.FindBinding(articulator)?.trackingParameter);
        }

        private static string FindBlendShape(Mesh mesh, params string[] candidates)
        {
            if (mesh == null) return null;
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                var actual = mesh.GetBlendShapeName(i);
                foreach (var candidate in candidates)
                {
                    if (string.IsNullOrEmpty(candidate)) continue;
                    if (string.Equals(actual, candidate, StringComparison.OrdinalIgnoreCase) ||
                        actual.EndsWith("." + candidate, StringComparison.OrdinalIgnoreCase) ||
                        actual.EndsWith("/" + candidate, StringComparison.OrdinalIgnoreCase))
                        return actual;
                }
            }
            return null;
        }

        private static string TrackingParameterName(string prefix, string suffix)
        {
            prefix = (prefix ?? string.Empty).Trim().Trim('/');
            suffix = (suffix ?? string.Empty).Trim().Trim('/');
            return string.IsNullOrEmpty(prefix) ? "v2/" + suffix : prefix + "/v2/" + suffix;
        }

        private static bool UsesBinaryTracking(AdvancedVisemeReconstructorData component)
        {
            return component.trackingInputs != AdvancedVisemeTrackingInputs.Disabled &&
                   component.trackingInputs != AdvancedVisemeTrackingInputs.ReuseExisting &&
                   component.trackingEncoding != AdvancedVisemeTrackingEncoding.FullFloat;
        }

        private static IEnumerable<string> BinaryParameterNames(
            string baseName,
            AdvancedVisemeArticulator articulator,
            AdvancedVisemeTrackingEncoding encoding)
        {
            var bitCount = AdvancedVisemeMath.TrackingMagnitudeBits(articulator, encoding);
            for (var bit = 0; bit < bitCount; bit++) yield return baseName + (1 << bit);
            if (AdvancedVisemeMath.IsSignedTrackingArticulator(articulator)) yield return baseName + "Negative";
        }

        private static bool HasVrcFuryAdvancedVisemes(GameObject avatarRoot)
        {
            foreach (var component in avatarRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().FullName != "VF.Model.VRCFury") continue;
                var field = component.GetType().GetField("content", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var content = field?.GetValue(component);
                if (content != null && content.GetType().Name == "Visemes") return true;
            }
            return false;
        }

        /// <summary>
        /// Avatar preprocessing is transactional. Calibration temporarily swaps
        /// build-only meshes before the Animator and VRCFury features exist, so a
        /// later validation failure must restore the complete avatar rather than
        /// leave a half-installed mouth. Generated assets are written into sibling
        /// staging folders and promoted only after every AVR component succeeds.
        /// </summary>
        internal sealed class AvatarBuildTransaction : IDisposable
        {
            private const string VrcFuryComponentName = "VF.Model.VRCFury";

            private readonly GameObject avatarRoot;
            private readonly VRCAvatarDescriptor descriptor;
            private readonly VRC_AvatarDescriptor.LipSyncStyle originalLipSync;
            private readonly Dictionary<SkinnedMeshRenderer, Mesh> originalMeshes;
            private readonly HashSet<int> originalVrcFuryComponents;
            private readonly List<ProfileDiagnosticsSnapshot> profileDiagnostics;
            private readonly List<GameObject> createdObjects = new List<GameObject>();
            private readonly List<GeneratedFolderStage> generatedFolders =
                new List<GeneratedFolderStage>();
            private readonly Dictionary<string, VRCExpressionParameters.Parameter>
                plannedExpressionParameters =
                    new Dictionary<string, VRCExpressionParameters.Parameter>(
                        StringComparer.Ordinal);
            private bool completed;

            public AvatarBuildTransaction(
                GameObject avatarRoot,
                VRCAvatarDescriptor descriptor,
                IEnumerable<AdvancedVisemeReconstructorData> components)
            {
                this.avatarRoot = avatarRoot;
                this.descriptor = descriptor;
                originalLipSync = descriptor.lipSync;
                originalMeshes = avatarRoot
                    .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(renderer => renderer != null)
                    .ToDictionary(renderer => renderer, renderer => renderer.sharedMesh);
                originalVrcFuryComponents = avatarRoot
                    .GetComponentsInChildren<Component>(true)
                    .Where(IsVrcFuryComponent)
                    .Select(component => component.GetInstanceID())
                    .ToHashSet();
                profileDiagnostics = components
                    .Select(component => component != null ? component.profile : null)
                    .Where(profile => profile != null)
                    .Distinct()
                    .Select(profile => new ProfileDiagnosticsSnapshot(profile))
                    .ToList();
            }

            public string StageGeneratedFolder(string finalFolder)
            {
                if (string.IsNullOrEmpty(finalFolder))
                    throw new ArgumentException("A generated asset folder is required.",
                        nameof(finalFolder));
                if (generatedFolders.Any(stage => string.Equals(
                        stage.finalFolder, finalFolder, StringComparison.Ordinal)))
                    throw new InvalidOperationException(
                        $"Generated folder '{finalFolder}' was requested more than once.");

                var token = Guid.NewGuid().ToString("N");
                var stage = new GeneratedFolderStage(
                    finalFolder,
                    finalFolder + "__staging_" + token,
                    finalFolder + "__backup_" + token);
                generatedFolders.Add(stage);
                Directory.CreateDirectory(stage.stagingFolder);
                AssetDatabase.Refresh();
                if (!AssetDatabase.IsValidFolder(stage.stagingFolder))
                    throw new IOException(
                        $"Unity could not create generated staging folder '{stage.stagingFolder}'.");
                return stage.stagingFolder;
            }

            public void TrackCreatedObject(GameObject createdObject)
            {
                if (createdObject != null) createdObjects.Add(createdObject);
            }

            public Dictionary<string, VRCExpressionParameters.Parameter>
                ExpressionParametersIncludingPlanned(
                    IReadOnlyDictionary<string, VRCExpressionParameters.Parameter> source)
            {
                var merged = new Dictionary<string, VRCExpressionParameters.Parameter>(
                    StringComparer.Ordinal);
                if (source != null)
                foreach (var pair in source) merged[pair.Key] = pair.Value;
                foreach (var pair in plannedExpressionParameters)
                    merged[pair.Key] = pair.Value;
                return merged;
            }

            public void RegisterGeneratedParameters(
                VRCExpressionParameters parameters)
            {
                if (parameters?.parameters == null) return;
                foreach (var parameter in parameters.parameters)
                {
                    if (parameter == null || string.IsNullOrEmpty(parameter.name))
                        continue;
                    if (plannedExpressionParameters.TryGetValue(
                            parameter.name, out var previous) &&
                        (previous.valueType != parameter.valueType ||
                         previous.networkSynced != parameter.networkSynced ||
                         previous.saved != parameter.saved))
                        throw new InvalidOperationException(
                            $"Generated parameter '{parameter.name}' has conflicting " +
                            "type, sync, or Saved declarations across Advanced Viseme components.");
                    plannedExpressionParameters[parameter.name] = parameter;
                }
            }

            public void Complete()
            {
                if (completed) return;
                foreach (var stage in generatedFolders) Promote(stage);
                AssetDatabase.SaveAssets();
                // Refresh while every previous final folder still exists as a
                // backup. If this fails, Dispose can restore all folders and
                // avatar mutations in one pass.
                AssetDatabase.Refresh();
                completed = true;

                // Backup cleanup happens after the commit boundary and is
                // deliberately best-effort. A cleanup failure cannot invalidate
                // the already-saved new build or trigger an impossible rollback
                // after another backup was deleted.
                foreach (var stage in generatedFolders)
                {
                    if (!stage.backupMoved) continue;
                    try
                    {
                        DeleteGeneratedFolderOrThrow(stage.backupFolder);
                        stage.backupMoved = false;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning(
                            $"[YUCP Advanced Viseme] Could not remove generated asset " +
                            $"backup '{stage.backupFolder}': {exception.Message}. The new " +
                            "build is valid, and the unused backup may be deleted safely.",
                            avatarRoot);
                    }
                }
                try { AssetDatabase.Refresh(); }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"[YUCP Advanced Viseme] Generated assets were committed, but the " +
                        $"post-cleanup refresh failed: {exception.Message}", avatarRoot);
                }
            }

            public void Dispose()
            {
                if (completed) return;
                try
                {
                    RollBackGeneratedFolders();
                }
                catch (Exception exception)
                {
                    // Asset cleanup may fail because Unity is refreshing or an
                    // external process locked a file. Avatar-object restoration
                    // must still run, so never let this escape Dispose.
                    Debug.LogError(
                        $"[YUCP Advanced Viseme] Generated asset rollback failed: " +
                        exception.Message, avatarRoot);
                }

                foreach (var pair in originalMeshes)
                {
                    if (pair.Key == null) continue;
                    pair.Key.sharedMesh = pair.Value;
                    EditorUtility.SetDirty(pair.Key);
                }

                if (descriptor != null)
                {
                    descriptor.lipSync = originalLipSync;
                    EditorUtility.SetDirty(descriptor);
                }

                foreach (var current in avatarRoot != null
                             ? avatarRoot.GetComponentsInChildren<Component>(true)
                             : Array.Empty<Component>())
                {
                    if (!IsVrcFuryComponent(current) ||
                        originalVrcFuryComponents.Contains(current.GetInstanceID()))
                        continue;
                    UnityEngine.Object.DestroyImmediate(current);
                }

                for (var index = createdObjects.Count - 1; index >= 0; index--)
                {
                    if (createdObjects[index] != null)
                        UnityEngine.Object.DestroyImmediate(createdObjects[index]);
                }

                var restoredPersistentProfile = false;
                foreach (var snapshot in profileDiagnostics)
                {
                    if (snapshot.profile == null) continue;
                    if (Mathf.Approximately(
                            snapshot.profile.LastReconstructionRms, snapshot.rms) &&
                        Mathf.Approximately(
                            snapshot.profile.LastReconstructionMaximum, snapshot.maximum))
                        continue;
                    snapshot.profile.SetDiagnostics(snapshot.rms, snapshot.maximum);
                    if (!AssetDatabase.Contains(snapshot.profile)) continue;
                    EditorUtility.SetDirty(snapshot.profile);
                    restoredPersistentProfile = true;
                }

                if (restoredPersistentProfile)
                {
                    try
                    {
                        AssetDatabase.SaveAssets();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            "[YUCP Advanced Viseme] Failed to persist restored profile " +
                            $"diagnostics: {exception.Message}", avatarRoot);
                    }
                }
            }

            private static bool IsVrcFuryComponent(Component component)
            {
                return component != null &&
                       string.Equals(component.GetType().FullName,
                           VrcFuryComponentName, StringComparison.Ordinal);
            }

            private static void Promote(GeneratedFolderStage stage)
            {
                if (AssetDatabase.IsValidFolder(stage.finalFolder))
                {
                    MoveAssetOrThrow(stage.finalFolder, stage.backupFolder);
                    stage.backupMoved = true;
                }

                MoveAssetOrThrow(stage.stagingFolder, stage.finalFolder);
                stage.promoted = true;
            }

            private void RollBackGeneratedFolders()
            {
                for (var index = generatedFolders.Count - 1; index >= 0; index--)
                {
                    var stage = generatedFolders[index];
                    try
                    {
                        if (stage.promoted)
                        {
                            DeleteGeneratedFolderOrThrow(stage.finalFolder);
                            stage.promoted = false;
                        }
                        if (stage.backupMoved)
                        {
                            if (!AssetDatabase.IsValidFolder(stage.backupFolder))
                                throw new IOException(
                                    $"Generated backup '{stage.backupFolder}' is missing.");
                            MoveAssetOrThrow(stage.backupFolder, stage.finalFolder);
                            stage.backupMoved = false;
                        }
                        DeleteGeneratedFolderOrThrow(
                            stage.stagingFolder, allowMissing: true);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            $"[YUCP Advanced Viseme] Failed to roll back generated folder " +
                            $"'{stage.finalFolder}': {exception.Message}", avatarRoot);
                    }
                }
                try { AssetDatabase.Refresh(); }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[YUCP Advanced Viseme] AssetDatabase refresh after rollback " +
                        $"failed: {exception.Message}", avatarRoot);
                }
            }

            private static void DeleteGeneratedFolderOrThrow(
                string folder,
                bool allowMissing = false)
            {
                if (AssetDatabase.IsValidFolder(folder))
                {
                    if (!AssetDatabase.DeleteAsset(folder))
                        throw new IOException(
                            $"Unity could not delete generated folder '{folder}'.");
                    return;
                }

                var generatedRoot = Path.GetFullPath(GeneratedRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                var fullPath = Path.GetFullPath(folder);
                if (!fullPath.StartsWith(generatedRoot,
                        StringComparison.OrdinalIgnoreCase))
                    throw new IOException(
                        $"Refusing to remove path outside '{GeneratedRoot}': '{folder}'.");
                if (!Directory.Exists(fullPath))
                {
                    if (allowMissing) return;
                    throw new DirectoryNotFoundException(
                        $"Generated folder '{folder}' does not exist.");
                }

                FileUtil.DeleteFileOrDirectory(fullPath);
                FileUtil.DeleteFileOrDirectory(fullPath + ".meta");
                if (Directory.Exists(fullPath))
                    throw new IOException(
                        $"Unity could not remove physical generated folder '{folder}'.");
            }

            private static void MoveAssetOrThrow(string source, string destination)
            {
                var error = AssetDatabase.MoveAsset(source, destination);
                if (!string.IsNullOrEmpty(error))
                    throw new IOException(
                        $"Could not move generated assets from '{source}' to " +
                        $"'{destination}': {error}");
            }

            private sealed class GeneratedFolderStage
            {
                public readonly string finalFolder;
                public readonly string stagingFolder;
                public readonly string backupFolder;
                public bool backupMoved;
                public bool promoted;

                public GeneratedFolderStage(
                    string finalFolder,
                    string stagingFolder,
                    string backupFolder)
                {
                    this.finalFolder = finalFolder;
                    this.stagingFolder = stagingFolder;
                    this.backupFolder = backupFolder;
                }
            }

            private readonly struct ProfileDiagnosticsSnapshot
            {
                public readonly VisemeReconstructionProfile profile;
                public readonly float rms;
                public readonly float maximum;

                public ProfileDiagnosticsSnapshot(
                    VisemeReconstructionProfile profile)
                {
                    this.profile = profile;
                    rms = profile.LastReconstructionRms;
                    maximum = profile.LastReconstructionMaximum;
                }
            }
        }

        private static string StableHash(
            AdvancedVisemeReconstructorData component,
            VisemeReconstructionProfile profile,
            Mesh mesh,
            string rendererPath,
            string trackingDependencies,
            string blendShapeLinkDependencies,
            bool trackingEnabled,
            bool useSharedParameterCompressor)
        {
            var meshPath = mesh != null ? AssetDatabase.GetAssetPath(mesh) : string.Empty;
            var dependency = mesh == null
                ? "no-mesh"
                : string.IsNullOrEmpty(meshPath)
                    ? TransientMeshContentHash(mesh)
                    : AssetDatabase.GetAssetDependencyHash(meshPath).ToString();
            var profileJson = StableProfileJson(profile);
            var coarticulationDependency = component.reconstructionMode ==
                                           AdvancedVisemeReconstructionMode.BetaCoarticulation
                ? AdvancedVisemeCoarticulationModel.ModelVersion + ":" +
                  AdvancedVisemeCoarticulationModel.ContentSha256 + ":reconstruction:" +
                  AdvancedVisemeCoarticulationModel.ReconstructionVersion + ":tongue:" +
                  AdvancedVisemeVisibleTongueResidual.ModelVersion + ":" +
                  AdvancedVisemeVisibleTongueResidual.BalancedContentSha256 + ":" +
                  AdvancedVisemeVisibleTongueResidual.QualityContentSha256 + ":phone:" +
                  AdvancedVisemeHiddenPhonePosterior.ModelVersion + ":" +
                  AdvancedVisemeHiddenPhonePosterior.ContentSha256
                : "normal";
            var toggleDependency = component.trackingInputs == AdvancedVisemeTrackingInputs.Auto
                ? "auto-reuse"
                : component.createFaceTrackingToggle + ":" + component.faceTrackingMenuPath;
            var tuningDependency = component.createTuningMenu + ":" +
                                   component.tuningMenuPath + ":" +
                                   component.saveTuningValues + ":" +
                                   component.tuningSyncMode + ":" +
                                   component.tuningMenuSections + ":shared:" +
                                   useSharedParameterCompressor;
            var haloDependency = AdvancedVisemeAnimatorBuilder
                .ShouldUseOculusHalo(trackingEnabled)
                ? "static-interruptible:" +
                  AdvancedVisemeOculusHalo.ModelVersion + ":" +
                  AdvancedVisemeOculusHalo.ContentSha256 + ":" +
                  AdvancedVisemeOculusHalo.TableSha256 + ":topK:" +
                  AdvancedVisemeOculusHalo.TopK + ":dynamics:" +
                  AdvancedVisemeOculusDynamics.ModelVersion + ":" +
                  AdvancedVisemeOculusDynamics.ContentSha256 + ":" +
                  AdvancedVisemeOculusDynamics.ModelSha256 + ":" +
                  AdvancedVisemeOculusDynamics.TrajectoryDurationSeconds.ToString(
                      "R", CultureInfo.InvariantCulture) + ":" +
                  AdvancedVisemeOculusDynamics.TrajectoryCoreDurationSeconds.ToString(
                      "R", CultureInfo.InvariantCulture) + ":" +
                  AdvancedVisemeOculusDynamics.TargetCrossfadeSeconds.ToString(
                      "R", CultureInfo.InvariantCulture)
                : "disabled";
            var settings = $"{rendererPath}|{dependency}|{trackingDependencies}|{blendShapeLinkDependencies}|{profileJson}|{component.NormalizedPrefix}|{component.mouthOwnership}|{component.reconstructionMode}|{coarticulationDependency}|{component.trackingInputs}|trackingEnabled:{trackingEnabled}|halo:{haloDependency}|{component.trackingEncoding}|{component.fusionMode}|{toggleDependency}|{tuningDependency}|optimizer:{AdvancedVisemeAnimatorGraphOptimizer.Version}|{component.existingTrackingPrefix}";
            return Hash128.Compute(settings).ToString();
        }

        private static string StableProfileJson(VisemeReconstructionProfile profile)
        {
            if (profile == null) return string.Empty;
            var clone = UnityEngine.Object.Instantiate(profile);
            clone.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                // These fields are editor diagnostics/migration bookkeeping, not
                // rendering inputs. Normalizing them prevents build 1 from
                // changing build 2's content address after calibration writes its
                // error report back to a reusable profile.
                clone.SetDiagnostics(0f, 0f);
                var defaultsVersion = typeof(VisemeReconstructionProfile).GetField(
                    "defaultsVersion",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                defaultsVersion?.SetValue(clone, 0);

                // EditorJsonUtility serializes Unity object references using
                // session identity. Strip those references from the structural
                // JSON and append semantic GUID/local-id/dependency hashes in a
                // stable field order instead. In-place clip edits now regenerate
                // outputs, while an editor reload does not rename them.
                var animationDependencies = new List<string>();
                for (var index = 0; index < clone.visemePoses.Length; index++)
                {
                    var pose = clone.visemePoses[index];
                    animationDependencies.Add(
                        $"viseme:{index}:{StableObjectReference(pose?.animationOverride)}");
                    if (pose != null) pose.animationOverride = null;
                }
                for (var index = 0;
                     index < clone.articulatorBindings.Length;
                     index++)
                {
                    var binding = clone.articulatorBindings[index];
                    animationDependencies.Add(
                        $"articulator:{index}:positive:" +
                        StableObjectReference(binding?.animationOverride));
                    animationDependencies.Add(
                        $"articulator:{index}:negative:" +
                        StableObjectReference(binding?.negativeAnimationOverride));
                    if (binding == null) continue;
                    binding.animationOverride = null;
                    binding.negativeAnimationOverride = null;
                }
                return EditorJsonUtility.ToJson(clone) + "|animations|" +
                       string.Join("|", animationDependencies);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clone);
            }
        }

        private static string StableObjectReference(UnityEngine.Object value)
        {
            if (value == null) return "null";
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    value, out var guid, out long localId))
            {
                var path = AssetDatabase.GetAssetPath(value);
                var dependency = string.IsNullOrEmpty(path)
                    ? "no-path"
                    : AssetDatabase.GetAssetDependencyHash(path).ToString();
                return $"{value.GetType().FullName}:{guid}:{localId}:{dependency}";
            }

            // Unsaved profile clips have no cross-session identity. Their
            // serialized content is still deterministic for the current build
            // and changes whenever the creator edits the in-memory clip.
            return value.GetType().FullName + ":memory:" +
                   Hash128.Compute(EditorJsonUtility.ToJson(value));
        }

        private static string TransientMeshContentHash(Mesh mesh)
        {
            if (mesh == null) return "no-mesh";
            try
            {
                using (var sha256 = SHA256.Create())
                using (var crypto = new CryptoStream(
                           Stream.Null, sha256, CryptoStreamMode.Write))
                using (var writer = new BinaryWriter(
                           crypto, Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(mesh.vertexCount);
                    writer.Write(mesh.subMeshCount);
                    writer.Write((int)mesh.indexFormat);
                    writer.Write(mesh.bounds.center.x);
                    writer.Write(mesh.bounds.center.y);
                    writer.Write(mesh.bounds.center.z);
                    writer.Write(mesh.bounds.extents.x);
                    writer.Write(mesh.bounds.extents.y);
                    writer.Write(mesh.bounds.extents.z);
                    WriteVectors(writer, mesh.vertices);
                    WriteVectors(writer, mesh.normals);
                    WriteVector4s(writer, mesh.tangents);
                    WriteColors(writer, mesh.colors);
                    for (var channel = 0; channel < 8; channel++)
                    {
                        var uv = new List<Vector4>();
                        mesh.GetUVs(channel, uv);
                        WriteVector4s(writer, uv);
                    }

                    var bonesPerVertex = mesh.GetBonesPerVertex();
                    var boneWeights = mesh.GetAllBoneWeights();
                    try
                    {
                        writer.Write(bonesPerVertex.Length);
                        foreach (var count in bonesPerVertex) writer.Write(count);
                        writer.Write(boneWeights.Length);
                        foreach (var weight in boneWeights)
                        {
                            writer.Write(weight.boneIndex);
                            writer.Write(weight.weight);
                        }
                    }
                    finally
                    {
                        if (bonesPerVertex.IsCreated) bonesPerVertex.Dispose();
                        if (boneWeights.IsCreated) boneWeights.Dispose();
                    }
                    var bindPoses = mesh.bindposes;
                    writer.Write(bindPoses.Length);
                    foreach (var matrix in bindPoses)
                    for (var row = 0; row < 4; row++)
                    for (var column = 0; column < 4; column++)
                        writer.Write(matrix[row, column]);

                    for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                    {
                        var descriptor = mesh.GetSubMesh(subMesh);
                        writer.Write((int)descriptor.topology);
                        writer.Write(descriptor.indexStart);
                        writer.Write(descriptor.indexCount);
                        writer.Write(descriptor.baseVertex);
                        writer.Write(descriptor.firstVertex);
                        writer.Write(descriptor.vertexCount);
                        writer.Write(descriptor.bounds.center.x);
                        writer.Write(descriptor.bounds.center.y);
                        writer.Write(descriptor.bounds.center.z);
                        writer.Write(descriptor.bounds.extents.x);
                        writer.Write(descriptor.bounds.extents.y);
                        writer.Write(descriptor.bounds.extents.z);
                        var indices = mesh.GetIndices(subMesh);
                        writer.Write(indices.Length);
                        foreach (var index in indices) writer.Write(index);
                    }

                    writer.Write(mesh.blendShapeCount);
                    var deltaVertices = new Vector3[mesh.vertexCount];
                    var deltaNormals = new Vector3[mesh.vertexCount];
                    var deltaTangents = new Vector3[mesh.vertexCount];
                    for (var shape = 0; shape < mesh.blendShapeCount; shape++)
                    {
                        writer.Write(mesh.GetBlendShapeName(shape) ?? string.Empty);
                        var frameCount = mesh.GetBlendShapeFrameCount(shape);
                        writer.Write(frameCount);
                        for (var frame = 0; frame < frameCount; frame++)
                        {
                            writer.Write(mesh.GetBlendShapeFrameWeight(shape, frame));
                            mesh.GetBlendShapeFrameVertices(
                                shape, frame,
                                deltaVertices, deltaNormals, deltaTangents);
                            WriteVectors(writer, deltaVertices);
                            WriteVectors(writer, deltaNormals);
                            WriteVectors(writer, deltaTangents);
                        }
                    }

                    writer.Flush();
                    crypto.FlushFinalBlock();
                    return BitConverter.ToString(sha256.Hash)
                        .Replace("-", string.Empty)
                        .ToLowerInvariant();
                }
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Transient face mesh '{mesh.name}' could not be content-hashed. " +
                    "Keep the generated mesh readable during avatar preprocessing.",
                    exception);
            }
        }

        private static void WriteVectors(BinaryWriter writer, ICollection<Vector3> values)
        {
            writer.Write(values.Count);
            foreach (var value in values)
            {
                writer.Write(value.x);
                writer.Write(value.y);
                writer.Write(value.z);
            }
        }

        private static void WriteVector4s(BinaryWriter writer, ICollection<Vector4> values)
        {
            writer.Write(values.Count);
            foreach (var value in values)
            {
                writer.Write(value.x);
                writer.Write(value.y);
                writer.Write(value.z);
                writer.Write(value.w);
            }
        }

        private static void WriteColors(BinaryWriter writer, ICollection<Color> values)
        {
            writer.Write(values.Count);
            foreach (var value in values)
            {
                writer.Write(value.r);
                writer.Write(value.g);
                writer.Write(value.b);
                writer.Write(value.a);
            }
        }

        private static string Sanitize(string value)
        {
            var chars = (value ?? "Avatar").Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray();
            return new string(chars);
        }

        private static bool Fail(AdvancedVisemeReconstructorData component, string message)
        {
            component.SetBuildSummary("Build failed: " + message);
            Debug.LogError("[YUCP Advanced Viseme] " + message, component);
            return false;
        }
    }

    /// <summary>
    /// Rechecks the real merged parameter asset after VRCFury, Modular Avatar,
    /// and other preprocessors have materialized their pending declarations.
    /// The early AVR pass can budget known assets and all AVR components, but it
    /// cannot safely predict every third-party feature's generated parameters.
    /// </summary>
    public sealed class AdvancedVisemeFinalParameterValidator :
        IVRCSDKPreprocessAvatarCallback
    {
        private sealed class PendingMarker { }

        private static readonly ConditionalWeakTable<GameObject, PendingMarker> Pending =
            new ConditionalWeakTable<GameObject, PendingMarker>();
        private static readonly object PendingLock = new object();

        // VRCFury's parameter compressor runs at int.MaxValue - 100 and YUCP's
        // optimizer bridge runs at -200. Validate after both (and after other
        // late optimizers), while remaining ahead of editor-only cleanup hooks
        // registered at int.MaxValue itself.
        public int callbackOrder => int.MaxValue - 1;

        internal static void Mark(GameObject avatarRoot)
        {
            if (avatarRoot == null) return;
            lock (PendingLock)
            {
                Pending.Remove(avatarRoot);
                Pending.Add(avatarRoot, new PendingMarker());
            }
        }

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            if (avatarRoot == null) return true;
            lock (PendingLock)
            {
                if (!Pending.TryGetValue(avatarRoot, out _)) return true;
                Pending.Remove(avatarRoot);
            }

            return ValidateFinalParameters(
                avatarRoot,
                avatarRoot.GetComponent<VRCAvatarDescriptor>());
        }

        internal static bool ValidateFinalParameters(
            GameObject avatarRoot,
            VRCAvatarDescriptor descriptor)
        {
            var parameters = descriptor != null
                ? descriptor.expressionParameters
                : null;
            if (parameters == null) return true;
            var bits = parameters.CalcTotalCost();
            if (bits <= VRCExpressionParameters.MAX_PARAMETER_COST) return true;

            Debug.LogError(
                $"[YUCP Advanced Viseme] The final merged avatar uses {bits} synced " +
                $"parameter bits, exceeding VRChat's " +
                $"{VRCExpressionParameters.MAX_PARAMETER_COST}-bit limit. The early " +
                "Advanced Viseme estimate included existing assets and every AVR " +
                "component, but another VRCFury, Modular Avatar, or avatar feature " +
                "added parameters later in the build. Reduce synced parameters or " +
                "reuse an existing decoded face-tracking stream.",
                avatarRoot);
            return false;
        }
    }
}
