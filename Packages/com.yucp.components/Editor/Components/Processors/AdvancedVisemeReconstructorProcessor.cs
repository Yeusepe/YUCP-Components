using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

            foreach (var component in components)
            {
                if (!BuildComponent(avatarRoot, descriptor, component)) return false;
            }
            return true;
        }

        private static bool BuildComponent(GameObject avatarRoot, VRCAvatarDescriptor descriptor, AdvancedVisemeReconstructorData component)
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
                var catalog = AdvancedVisemeTrackingCatalog.Scan(avatarRoot, descriptor);
                var existing = catalog.ExpressionParameters;
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
                var hash = StableHash(component, profile, sourceMesh, rendererPath, catalog.DependencyFingerprint);
                var folder = $"{GeneratedRoot}/{Sanitize(avatarRoot.name)}_{hash.Substring(0, 12)}";
                RecreateFolder(folder);

                AdvancedVisemeMeshCalibrator.Result calibration = null;
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
                            try
                            {
                                calibration = AdvancedVisemeMeshCalibrator.BuildFromPoses(
                                    sourceMesh, indices, poseBasis);
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
                        calibration = AdvancedVisemeMeshCalibrator.Build(sourceMesh, indices, basis);
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
                        profile.SetDiagnostics(calibration.fitRms, calibration.fitMaximum);
                        if (AssetDatabase.Contains(profile)) EditorUtility.SetDirty(profile);
                        var meshPath = folder + "/FaceMesh.asset";
                        AssetDatabase.CreateAsset(calibration.mesh, meshPath);
                        AssetDatabase.ImportAsset(meshPath);
                        renderer.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                        EditorUtility.SetDirty(renderer);
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
                    sourceVisemeBlendShapes = visemeNames,
                    calibration = calibration,
                    calibrationBasis = basis,
                    resolvedBlendShapes = resolvedBlendShapes,
                    externalPoses = externalPoses,
                    targetMesh = sourceMesh,
                    trackingEnabled = trackingEnabled,
                    existingExpressionParameters = new HashSet<string>(existing.Keys, StringComparer.Ordinal)
                };
                if (!ValidateTuningParameters(request, existing, out var tuningError))
                    return Fail(component, tuningError);
                var built = AdvancedVisemeAnimatorBuilder.Build(request);

                var controllerHost = new GameObject("__YUCP Advanced Viseme Controller");
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
                        folder + "/TuningMenu.asset", built.tuningParameters);
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
                    ? $", {built.tuningParameters.Count} saved local sliders (0 synced bits)"
                    : string.Empty;
                component.SetBuildSummary($"Built {built.globalParameters.Distinct().Count()} reusable outputs, +{trackingBits} synced bits{tuningText}{trackingText}{calibrationText}");
                EditorUtility.SetDirty(component);
                EditorUtility.SetDirty(descriptor);
                AssetDatabase.SaveAssets();

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
                    continue;
                }

                var suffix = profile.FindBinding(articulator)?.trackingParameter;
                if (string.IsNullOrEmpty(suffix)) continue;
                var name = TrackingParameterName(prefix, suffix);
                if (UsesBinaryTracking(component))
                {
                    foreach (var bitName in BinaryParameterNames(name, articulator, component.trackingEncoding))
                    {
                        if (!existing.TryGetValue(bitName, out var bitParameter) ||
                            bitParameter.valueType == VRCExpressionParameters.ValueType.Bool) continue;
                        error = $"Existing binary parameter '{bitName}' must be a Bool.";
                        return false;
                    }
                }
                else
                {
                    if (existing.TryGetValue(name, out var parameter) && parameter.valueType != VRCExpressionParameters.ValueType.Float)
                    {
                        error = $"Existing parameter '{name}' must be a Float.";
                        return false;
                    }
                }
            }

            if (existing.TryGetValue(activeParameter, out var active) && active.valueType != VRCExpressionParameters.ValueType.Bool)
            {
                error = $"Existing parameter '{activeParameter}' must be a Bool.";
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

        private static bool ValidateTuningParameters(
            AdvancedVisemeAnimatorBuilder.Request request,
            IReadOnlyDictionary<string, VRCExpressionParameters.Parameter> existing,
            out string error)
        {
            error = null;
            if (!request.component.createTuningMenu) return true;
            foreach (var control in AdvancedVisemeTuning.Controls)
            {
                if ((request.component.tuningMenuSections &
                     AdvancedVisemeTuning.Section(control)) == 0 ||
                    !AdvancedVisemeAnimatorBuilder.IsTuningControlRelevant(request, control))
                    continue;
                var name = request.component.TuningParameterName(control);
                if (!existing.TryGetValue(name, out var parameter)) continue;
                if (parameter.valueType != VRCExpressionParameters.ValueType.Float)
                {
                    error = $"Existing tuning parameter '{name}' must be Float, but is " +
                            $"{parameter.valueType}. Change its type or use another parameter prefix.";
                    return false;
                }
                if (parameter.networkSynced)
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
            return true;
        }

        private static int IncrementalTrackingBits(
            AdvancedVisemeReconstructorData component,
            VisemeReconstructionProfile profile,
            string prefix,
            Dictionary<string, VRCExpressionParameters.Parameter> existing,
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

        private static string StableHash(
            AdvancedVisemeReconstructorData component,
            VisemeReconstructionProfile profile,
            Mesh mesh,
            string rendererPath,
            string trackingDependencies)
        {
            var meshPath = mesh != null ? AssetDatabase.GetAssetPath(mesh) : string.Empty;
            var dependency = mesh == null
                ? "no-mesh"
                : string.IsNullOrEmpty(meshPath)
                    ? mesh.GetInstanceID().ToString()
                    : AssetDatabase.GetAssetDependencyHash(meshPath).ToString();
            var profileJson = EditorJsonUtility.ToJson(profile);
            var coarticulationDependency = component.reconstructionMode ==
                                           AdvancedVisemeReconstructionMode.BetaCoarticulation
                ? AdvancedVisemeCoarticulationModel.ModelVersion + ":" +
                  AdvancedVisemeCoarticulationModel.ContentSha256 + ":tongue:" +
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
                                   component.tuningMenuSections;
            var settings = $"{rendererPath}|{dependency}|{trackingDependencies}|{profileJson}|{component.NormalizedPrefix}|{component.mouthOwnership}|{component.reconstructionMode}|{coarticulationDependency}|{component.trackingInputs}|{component.trackingEncoding}|{component.fusionMode}|{toggleDependency}|{tuningDependency}|{component.existingTrackingPrefix}";
            return Hash128.Compute(settings).ToString();
        }

        private static void RecreateFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
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
}
