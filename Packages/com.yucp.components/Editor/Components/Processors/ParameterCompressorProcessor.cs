using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Runs after VRCFury's main merge (-10000), but immediately before its
    /// built-in compressor (int.MaxValue - 100). The final merged assets are
    /// cloned before mutation, so source controllers, menus and parameter assets
    /// remain byte-for-byte untouched. Bringing the clone under budget makes
    /// VRCFury's later compressor naturally no-op.
    /// </summary>
    public sealed class ParameterCompressorProcessor :
        IVRCSDKPreprocessAvatarCallback
    {
        internal const string GeneratedRoot =
            "Assets/YUCP/GeneratedAssets/ParameterCompressor";

        public int callbackOrder => int.MaxValue - 101;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            if (avatarRoot == null) return true;
            var components = avatarRoot.GetComponentsInChildren<
                ParameterCompressorData>(true);
            if (components.Length == 0) return true;
            if (components.Length != 1)
                return Fail(components.FirstOrDefault(),
                    "Use exactly one Parameter Compressor on an avatar. It already " +
                    "collects settings from every prefab and component.");

            var component = components[0];
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
                return Fail(component,
                    "The component must be inside a GameObject with a VRChat Avatar Descriptor.");
            var sourceParameters = descriptor.expressionParameters;
            if (sourceParameters == null)
                return Fail(component,
                    "The final merged avatar has no Expression Parameters asset to compress.");

            try
            {
                return Build(avatarRoot, descriptor, component, sourceParameters);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static bool Build(
            GameObject avatarRoot,
            VRCAvatarDescriptor descriptor,
            ParameterCompressorData component,
            VRCExpressionParameters sourceParameters)
        {
            var phases = new Phases();
            try
            {
                var result = Build(avatarRoot, descriptor, component, sourceParameters, phases);
                Debug.Log("[YUCP Parameter Compressor] Timing: " + phases.Finish(), component);
                return result;
            }
            catch (Exception exception)
            {
                component.SetBuildSummary(new ParameterCompressionBuildSummary
                {
                    message = "Build failed: " + exception.Message
                });
                Debug.LogError(
                    "[YUCP Parameter Compressor] Build failed: " +
                    exception.Message, component);
                Debug.LogException(exception, component);
                return false;
            }
        }

        private static bool Build(
            GameObject avatarRoot,
            VRCAvatarDescriptor descriptor,
            ParameterCompressorData component,
            VRCExpressionParameters sourceParameters,
            Phases phases)
        {
            phases.Begin("Reading the merged avatar's parameters and menus", 0f);
            var currentBits = sourceParameters.CalcTotalCost();
            var targetBits = Math.Max(0,
                ParameterCompressionContract.VrchatParameterBudget -
                component.reserveSyncedBits);
            var catalog = ParameterCompressionCatalog.Scan(
                avatarRoot, descriptor, component);
            if (!ValidateRules(component, sourceParameters, catalog, out var ruleError))
                return Fail(component, ruleError);



            phases.Begin("Finding what remote clients cannot read or tell apart", 0.05f);
            var reductions = PlanLosslessReductions(
                descriptor, sourceParameters, component, catalog);
            var reducedBits = currentBits - reductions.unreadBits -
                              reductions.packs.Sum(group => group.SavedBits);

            var eligible = catalog.entries.Where(entry => entry.eligible &&
                !reductions.Removes(entry.parameter.name)).ToArray();
            var candidates = eligible.Select(entry =>
                new ParameterCompressionCandidate(
                    entry.parameter.name,
                    entry.desiredLevels,
                    (int)entry.priority,
                    entry.CurrentBits,
                    entry.explicitlyIncluded)).ToArray();

            phases.Begin("Planning the event bus", 0.1f);
            var precision = ParameterPrecisionPlan.None;
            var measureSeconds = 0d;
            if (!ParameterCompressionPlanner.TryCreatePlan(
                    reducedBits, targetBits, candidates,
                    out var plan, out var planningError))
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var lossyBase = reducedBits;
                precision = PlanPrecision(avatarRoot, descriptor, sourceParameters,
                    component, catalog, reductions, phases, saved =>
                        ParameterCompressionPlanner.TryCreatePlan(
                            lossyBase - saved, targetBits, candidates,
                            out _, out _));
                measureSeconds = watch.Elapsed.TotalSeconds;
                if (precision == null ||
                    !ParameterCompressionPlanner.TryCreatePlan(
                        reducedBits - precision.SavedBits, targetBits, candidates,
                        out plan, out _))
                {
                    var safeBits = eligible.Sum(entry => entry.CurrentBits);
                    return Fail(component,
                        planningError + " The final avatar uses " + currentBits +
                        " synced bits (" + reducedBits + " after lossless reductions), " +
                        "the selected target is " + targetBits +
                        ", and safe candidates account for " + safeBits + " bits. " +
                        "Lowering the remote precision of every measurable bit-plane " +
                        "value would still not fit.");
                }
                reducedBits -= precision.SavedBits;
            }

            if (!plan.UsesCompression && !reductions.Any && !precision.Any)
            {
                component.SetBuildSummary(new ParameterCompressionBuildSummary
                {
                    hasResult = true,
                    beforeBits = currentBits,
                    afterBits = currentBits,
                    protectedParameters = catalog.entries.Count,
                    message = "The final avatar already fits the selected reserve; no transport was generated."
                });
                ParameterCompressorFinalValidator.Mark(avatarRoot, targetBits);
                return true;
            }

            var byName = eligible.ToDictionary(
                entry => entry.parameter.name, StringComparer.Ordinal);
            var selected = plan.Allocations.Select(allocation => new SelectedParameter
            {
                source = byName[allocation.Name],
                allocation = allocation
            }).ToArray();

            var contentIdentity = component.StableId + "|prefix:" +
                component.NormalizedPrefix + "|avatar:" + avatarRoot.name +
                "|source:" + SourceDependencyIdentity(descriptor,
                    sourceParameters) + "|" + string.Join("|", selected
                .OrderBy(item => item.source.parameter.name, StringComparer.Ordinal)
                .Select(item => item.source.parameter.name + ":" +
                                item.source.parameter.valueType + ":" +
                                item.allocation.Levels + ":" + item.source.minimum + ":" +
                                item.source.maximum + ":" +
                                item.source.priority + ":" +
                                item.source.group)) +
                "|unread:" + string.Join(",", reductions.unread
                    .OrderBy(name => name, StringComparer.Ordinal)) +
                "|packs:" + string.Join(";", reductions.packs.Select(group =>
                    group.Carrier + "=" + string.Join(",", group.Members.Select(member =>
                        member.Candidate.Name + "*" + member.Observation.CellCount)))) +
                "|precision:" + precision.Identity +
                "|bus:" + plan.BusBits +
                "|contract:" + ParameterCompressionContract.ContractVersion;
            var hash = ParameterCompressionContract.StableFingerprint(
                contentIdentity);
            var finalFolder = GeneratedRoot + "/" + Sanitize(avatarRoot.name) +
                              "_" + hash.Substring(0, 12);
            var folder = CreateStagingFolder();

            var oldParameters = descriptor.expressionParameters;
            var oldLayers = descriptor.baseAnimationLayers != null
                ? descriptor.baseAnimationLayers.ToArray()
                : null;
            var oldCustomize = descriptor.customizeAnimationLayers;
            try
            {
                phases.Begin("Copying the FX controller and parameters", 0.45f);
                var clonedParameters = CloneParameters(
                    sourceParameters, folder + "/ParameterCompressorParameters.asset");
                var clonedController = CloneFxController(
                    descriptor, folder + "/ParameterCompressorFX.controller",
                    out var fxLayerIndex);

                if (clonedController.layers.Any(layer =>
                        !layer.name.StartsWith("YUCP Parameter Compressor", StringComparison.Ordinal) &&
                        layer.name.IndexOf("Parameter Compressor", StringComparison.OrdinalIgnoreCase) >= 0))
                    throw new InvalidOperationException(
                        "A different Parameter Compressor layer already exists in the final FX controller. Remove it to avoid nested transports.");

                phases.Begin("Building " + reductions.packs.Count + " packed carriers", 0.55f);
                ParameterCompressionPacking.Build(
                    clonedController, component.NormalizedPrefix, reductions.packs);
                phases.Begin("Building the remote precision decoder", 0.6f);
                ParameterPrecisionReduction.Build(
                    clonedController, component.NormalizedPrefix, precision);

                ParameterCompressorAnimatorBuilder.Result built = null;
                if (plan.UsesCompression)
                {
                    var fxTypes = clonedController.parameters.ToDictionary(
                        parameter => parameter.name, parameter => parameter.type,
                        StringComparer.Ordinal);
                    var builderEntries = selected.Select(item =>
                        new ParameterCompressorAnimatorBuilder.Entry
                        {
                            name = item.source.parameter.name,
                            type = fxTypes.TryGetValue(item.source.parameter.name,
                                out var declared)
                                ? declared
                                : AnimatorType(item.source.parameter.valueType),
                            levels = item.allocation.Levels,
                            minimum = item.source.minimum,
                            maximum = item.source.maximum,
                            defaultValue = item.source.parameter.defaultValue,
                            page = item.allocation.Page,
                            offset = item.allocation.Offset
                        }).ToArray();
                    phases.Begin("Building the bus for " + builderEntries.Length + " values", 0.65f);
                    built = ParameterCompressorAnimatorBuilder.Build(
                        new ParameterCompressorAnimatorBuilder.Request
                        {
                            controller = clonedController,
                            prefix = component.NormalizedPrefix,
                            pageBits = plan.PageBits,
                            entries = builderEntries
                        });
                }

                var unsyncedNames = new HashSet<string>(
                    selected.Select(item => item.source.parameter.name),
                    StringComparer.Ordinal);
                unsyncedNames.UnionWith(reductions.unread);
                unsyncedNames.UnionWith(reductions.packs.SelectMany(group =>
                    group.Members.Select(member => member.Candidate.Name)));
                unsyncedNames.UnionWith(precision.UnsyncedNames);

                foreach (var parameter in clonedParameters.parameters)
                    if (parameter != null && unsyncedNames.Contains(parameter.name))
                        parameter.networkSynced = false;
                AddPackCarrierDeclarations(clonedParameters, reductions.packs);
                if (built != null)
                    AddCarrierDeclarations(
                        clonedParameters, clonedController, built.carrierParameters,
                        component.NormalizedPrefix);

                var expectedBits = plan.UsesCompression ? plan.FinalBits : reducedBits;
                var afterBits = clonedParameters.CalcTotalCost();
                if (afterBits != expectedBits)
                    throw new InvalidOperationException(
                        "The generated parameter asset uses " + afterBits +
                        " bits, but the deterministic plan expected " +
                        expectedBits + ". No source assets were changed.");
                if (afterBits > targetBits)
                    throw new InvalidOperationException(
                        "The generated transport still exceeds the selected " +
                        targetBits + "-bit target.");

                phases.Begin("Saving the generated controller and parameters", 0.85f);
                descriptor.expressionParameters = clonedParameters;
                AssignFxController(descriptor, clonedController, fxLayerIndex);
                descriptor.customizeAnimationLayers = true;
                EditorUtility.SetDirty(clonedController);
                EditorUtility.SetDirty(clonedParameters);
                EditorUtility.SetDirty(descriptor);
                AssetDatabase.SaveAssetIfDirty(clonedController);
                AssetDatabase.SaveAssetIfDirty(clonedParameters);
                CommitStagingFolder(folder, finalFolder);

                var packedCount = reductions.packs.Sum(group => group.Members.Count);
                var droppedPlanes = precision.UnsyncedNames.Count();
                var lossless = reductions.Any
                    ? "Stopped syncing " + reductions.unread.Count +
                      " values no remote Animator reads (" + reductions.unreadBits +
                      " bits); packed " + packedCount + " values into " +
                      reductions.packs.Count + " carriers (" +
                      reductions.packs.Sum(group => group.SavedBits) + " bits). "
                    : string.Empty;
                var busMessage = built == null
                    ? "No event bus was needed."
                    : "Bus values keep native VRChat precision. A change cuts in on the next " +
                      "VRChat sync (at most " +
                      ParameterCompressorAnimatorBuilder.HoldSeconds.ToString("0.0") +
                      " s plus a few frames); idle ticks refresh all " + selected.Length + " values for late " +
                      "joiners in about " + built.estimatedFullRefreshSeconds.ToString("0.0") + " s.";
                var summary = new ParameterCompressionBuildSummary
                {
                    hasResult = true,
                    beforeBits = currentBits,
                    afterBits = afterBits,
                    carrierBits = plan.BusBits +
                                  reductions.packs.Sum(group => group.CarrierBits),
                    compressedParameters = selected.Length + packedCount +
                                           reductions.unread.Count + droppedPlanes,
                    protectedParameters = catalog.entries.Count - selected.Length -
                                          packedCount - reductions.unread.Count -
                                          droppedPlanes,
                    nominalFullRefreshSeconds = built?.estimatedFullRefreshSeconds ?? 0f,
                    transportName = plan.UsesCompression
                        ? "Event bus: one Int code and " + plan.PageBits + " page Bools"
                        : precision.Any ? "Reduced remote precision" : "Exact packing",
                    message = lossless + (precision.Any
                        ? precision.Describe() + " Measured in " +
                          measureSeconds.ToString("0.0") + " s. "
                        : string.Empty) + busMessage
                };
                component.SetBuildSummary(summary);
                Debug.Log("[YUCP Parameter Compressor] " + currentBits + " -> " +
                          afterBits + " synced bits. " + summary.message, component);
                EditorUtility.SetDirty(component);
                ParameterCompressorFinalValidator.Mark(avatarRoot, targetBits);
                return true;
            }
            catch
            {
                descriptor.expressionParameters = oldParameters;
                descriptor.baseAnimationLayers = oldLayers;
                descriptor.customizeAnimationLayers = oldCustomize;
                if (AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.DeleteAsset(folder);
                throw;
            }
        }

        private sealed class SelectedParameter
        {
            internal ParameterCompressionCatalogEntry source;
            internal ParameterCompressionAllocation allocation;
        }

        private sealed class LosslessReductions
        {
            internal readonly HashSet<string> unread =
                new HashSet<string>(StringComparer.Ordinal);
            internal int unreadBits;
            internal IReadOnlyList<ParameterPackGroup> packs =
                Array.Empty<ParameterPackGroup>();
            internal ParameterObservability observability;

            internal bool Any => unread.Count > 0 || packs.Count > 0;

            internal bool Removes(string name)
            {
                return unread.Contains(name) || packs.Any(group =>
                    group.Members.Any(member => member.Candidate.Name == name));
            }
        }

        /// <summary>
        /// Reductions that no remote client can observe. A value no remote
        /// Animator reads stops syncing. Values remote clients can only partly
        /// tell apart share a carrier as mixed-radix digits. Both are skipped
        /// when any playable layer could not be inspected, because an unseen
        /// layer may read anything.
        /// </summary>
        private static LosslessReductions PlanLosslessReductions(
            VRCAvatarDescriptor descriptor,
            VRCExpressionParameters parameters,
            ParameterCompressorData component,
            ParameterCompressionCatalog catalog)
        {
            var output = new LosslessReductions();
            if (!component.automaticSelection) return output;
            var observability = ParameterCompressionObservability.Analyze(
                descriptor, parameters);
            if (!observability.Complete)
            {
                Debug.LogWarning("[YUCP Parameter Compressor] Lossless reductions " +
                                 "were skipped. " + observability.IncompleteReason, component);
                return output;
            }
            output.observability = observability;

            var fxTypes = new Dictionary<string, AnimatorControllerParameterType>(
                StringComparer.Ordinal);
            var fx = (descriptor.baseAnimationLayers ??
                      Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                .FirstOrDefault(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX)
                .animatorController as AnimatorController;
            if (fx != null)
                foreach (var parameter in fx.parameters)
                    fxTypes[parameter.name] = parameter.type;

            var reservedPrefix = component.NormalizedPrefix + "/";
            var packCandidates = new List<ParameterPackCandidate>();
            foreach (var entry in catalog.entries)
            {
                var name = entry.parameter.name;
                if (entry.rule != null &&
                    entry.rule.selection == ParameterCompressionRuleSelection.KeepDirect ||
                    name.StartsWith(reservedPrefix, StringComparison.Ordinal) ||
                    ParameterCompressionCatalog.IsKnownTransportName(name) ||
                    !observability.Observations.TryGetValue(name, out var observation))
                    continue;

                if (!observation.Read)
                {
                    output.unread.Add(name);
                    output.unreadBits += entry.CurrentBits;
                    continue;
                }



                if (observation.RemotelyWritten ||
                    entry.menuRole == ParameterCompressionMenuRole.Radial ||
                    entry.menuRole == ParameterCompressionMenuRole.Puppet)
                    continue;
                var animatorType = fxTypes.TryGetValue(name, out var declared)
                    ? declared
                    : AnimatorType(entry.parameter.valueType);
                if (!ParameterCompressionPacking.CanEncode(
                        entry.parameter.valueType, animatorType))
                    continue;
                packCandidates.Add(new ParameterPackCandidate(
                    observation, animatorType, entry.eligible));
            }

            output.packs = ParameterCompressionPacking.Plan(
                packCandidates, component.NormalizedPrefix);
            return output;
        }

        /// <summary>
        /// Lossy stage: bit-plane values that are synced only for remote decoding,
        /// that no remote driver writes and that only the FX reads, measured on the
        /// avatar and reduced by rate-distortion allocation until <paramref name="fits"/>
        /// holds. Persistent state stays with the bus, which costs no precision.
        /// Returns null when nothing can be reduced safely or it still does not fit.
        /// </summary>
        private static ParameterPrecisionPlan PlanPrecision(
            GameObject avatarRoot,
            VRCAvatarDescriptor descriptor,
            VRCExpressionParameters parameters,
            ParameterCompressorData component,
            ParameterCompressionCatalog catalog,
            LosslessReductions reductions,
            Phases phases,
            Func<int, bool> fits)
        {
            var observability = reductions.observability;
            if (observability == null || !observability.Complete) return null;
            var fx = (descriptor.baseAnimationLayers ??
                      Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                .FirstOrDefault(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX)
                .animatorController as AnimatorController;
            if (fx == null) return null;
            var outside = ParameterCompressionObservability.Analyze(descriptor, parameters,
                type => type != VRCAvatarDescriptor.AnimLayerType.FX);
            if (!outside.Complete) return null;

            var reservedPrefix = component.NormalizedPrefix + "/";
            var usable = catalog.entries.Where(entry =>
            {
                var name = entry.parameter.name;
                return entry.parameter.valueType == VRCExpressionParameters.ValueType.Bool &&
                       !entry.eligible &&
                       !(entry.rule != null &&
                         entry.rule.selection == ParameterCompressionRuleSelection.KeepDirect) &&
                       !reductions.Removes(name) &&
                       !name.StartsWith(reservedPrefix, StringComparison.Ordinal) &&
                       !ParameterCompressionCatalog.IsKnownTransportName(name) &&
                       observability.Observations.TryGetValue(name, out var observation) &&
                       !observation.RemotelyWritten &&
                       !(outside.Observations.TryGetValue(name, out var elsewhere) && elsewhere.Read);
            }).Select(entry => entry.parameter.name);
            var groups = ParameterPrecisionReduction.FindGroups(usable);
            if (groups.Count == 0) return null;
            phases.Begin("Measuring how far each value moves the avatar", 0.15f);
            ParameterCompressionSensitivity.Measure(avatarRoot, fx, parameters, groups,
                (message, progress) => phases.Report(message, 0.15f + 0.25f * progress));
            Debug.Log("[YUCP Parameter Compressor] Remote surface motion at full swing " +
                      "(root of summed squared vertex displacement):\n" + string.Join("\n", groups
                          .OrderByDescending(group => group.Weight)
                          .Select(group => (double.IsInfinity(group.Weight)
                                               ? "kept whole (drives more than blend shapes)"
                                               : group.Weight == 0d
                                                   ? "no effect"
                                                   : (Math.Sqrt(group.Weight) * 1000d).ToString("0.0") + " mm") +
                                           "  " + group.Name)), component);
            phases.Begin("Allocating precision against the bus plan", 0.4f);
            return ParameterPrecisionReduction.Allocate(groups, fits);
        }

        private static void AddPackCarrierDeclarations(
            VRCExpressionParameters parameters,
            IReadOnlyList<ParameterPackGroup> packs)
        {
            if (packs.Count == 0) return;
            var list = (parameters.parameters ??
                        Array.Empty<VRCExpressionParameters.Parameter>()).ToList();
            foreach (var group in packs)
            {
                if (list.Any(parameter => parameter != null &&
                                          parameter.name == group.Carrier))
                    throw new InvalidOperationException(
                        "Carrier '" + group.Carrier + "' already exists.");


                list.Add(new VRCExpressionParameters.Parameter
                {
                    name = group.Carrier,
                    valueType = group.BoolCarrier
                        ? VRCExpressionParameters.ValueType.Bool
                        : VRCExpressionParameters.ValueType.Int,
                    defaultValue = group.DefaultCode,
                    saved = false,
                    networkSynced = true
                });
            }
            parameters.parameters = list.ToArray();
        }

        private static bool ValidateRules(
            ParameterCompressorData component,
            VRCExpressionParameters parameters,
            ParameterCompressionCatalog catalog,
            out string error)
        {
            error = null;
            var declared = new HashSet<string>(
                (parameters.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>())
                .Where(parameter => parameter != null)
                .Select(parameter => parameter.name), StringComparer.Ordinal);
            foreach (var rule in component.EnumerateRules())
            {
                if (rule == null ||
                    rule.selection != ParameterCompressionRuleSelection.Include &&
                    rule.selection !=
                    ParameterCompressionRuleSelection.IncludeUnverified)
                    continue;
                var name = ParameterCompressionContract.NormalizeParameterName(
                    rule.parameterName);
                if (string.IsNullOrEmpty(name))
                {
                    error = "An included parameter rule has no parameter name.";
                    return false;
                }
                if (!declared.Contains(name))
                {
                    error = "Included parameter '" + name +
                            "' is not present in the final merged Expression Parameters asset.";
                    return false;
                }
                var entry = catalog.entries.FirstOrDefault(item =>
                    string.Equals(item.parameter.name, name, StringComparison.Ordinal));
                if (entry == null)
                {
                    error = "Included parameter '" + name +
                            "' is not synchronized, so it already costs no VRChat " +
                            "parameter memory. Remove its compressor rule.";
                    return false;
                }
                if (entry != null && entry.hardUnsafe)
                {
                    error = "Parameter '" + name +
                            "' cannot be compressed safely. " + entry.reason;
                    return false;
                }
            }

            var prefix = component.NormalizedPrefix + "/";
            var collision = (parameters.parameters ??
                             Array.Empty<VRCExpressionParameters.Parameter>())
                .FirstOrDefault(parameter => parameter != null &&
                    parameter.name.StartsWith(prefix, StringComparison.Ordinal));
            if (collision != null)
            {
                error = "Generated transport prefix '" + component.NormalizedPrefix +
                        "' conflicts with existing parameter '" + collision.name + "'.";
                return false;
            }
            return true;
        }

        private static void AddCarrierDeclarations(
            VRCExpressionParameters parameters,
            AnimatorController controller,
            IReadOnlyList<string> carriers,
            string prefix)
        {
            var list = (parameters.parameters ??
                        Array.Empty<VRCExpressionParameters.Parameter>()).ToList();
            var names = new HashSet<string>(
                list.Where(parameter => parameter != null)
                    .Select(parameter => parameter.name), StringComparer.Ordinal);
            foreach (var carrier in carriers)
            {
                if (string.IsNullOrWhiteSpace(carrier) ||
                    !carrier.StartsWith(prefix + "/", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "The Animator builder returned an invalid carrier name.");
                if (!names.Add(carrier))
                    throw new InvalidOperationException(
                        "Carrier '" + carrier + "' already exists.");
                var declared = controller.parameters.First(parameter => parameter.name == carrier);
                list.Add(new VRCExpressionParameters.Parameter
                {
                    name = carrier,
                    valueType = declared.type == AnimatorControllerParameterType.Int
                        ? VRCExpressionParameters.ValueType.Int
                        : VRCExpressionParameters.ValueType.Bool,
                    defaultValue = declared.type == AnimatorControllerParameterType.Int
                        ? declared.defaultInt
                        : 0f,
                    saved = false,
                    networkSynced = true
                });
            }
            parameters.parameters = list.ToArray();
        }

        private static VRCExpressionParameters CloneParameters(
            VRCExpressionParameters source,
            string path)
        {
            var clone = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            clone.name = "YUCP Parameter Compressor Parameters";
            clone.parameters = (source.parameters ??
                Array.Empty<VRCExpressionParameters.Parameter>()).Select(parameter =>
                parameter == null ? null : new VRCExpressionParameters.Parameter
                {
                    name = parameter.name,
                    valueType = parameter.valueType,
                    defaultValue = parameter.defaultValue,
                    saved = parameter.saved,
                    networkSynced = parameter.networkSynced
                }).ToArray();
            AssetDatabase.CreateAsset(clone, path);
            return clone;
        }

        private static AnimatorController CloneFxController(
            VRCAvatarDescriptor descriptor,
            string targetPath,
            out int layerIndex)
        {
            var layers = descriptor.baseAnimationLayers ??
                         Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            layerIndex = Array.FindIndex(layers,
                layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX);
            var runtime = layerIndex >= 0
                ? layers[layerIndex].animatorController
                : null;
            if (runtime != null && !(runtime is AnimatorController))
                throw new InvalidOperationException(
                    "The final FX layer is " + runtime.GetType().Name +
                    ", not an AnimatorController. Merge or bake the override " +
                    "controller before parameter compression.");
            var source = runtime as AnimatorController;
            AnimatorController clone = null;
            var sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : null;
            if (!string.IsNullOrEmpty(sourcePath) &&
                AssetDatabase.CopyAsset(sourcePath, targetPath))
                clone = AssetDatabase.LoadAssetAtPath<AnimatorController>(targetPath);
            if (clone == null && source != null)
            {
                clone = UnityEngine.Object.Instantiate(source);
                clone.name = "YUCP Parameter Compressor FX";
                AssetDatabase.CreateAsset(clone, targetPath);
            }
            if (clone == null)
            {
                clone = new AnimatorController
                {
                    name = "YUCP Parameter Compressor FX"
                };
                AssetDatabase.CreateAsset(clone, targetPath);
            }
            return clone;
        }

        private static void AssignFxController(
            VRCAvatarDescriptor descriptor,
            AnimatorController controller,
            int layerIndex)
        {
            var layers = descriptor.baseAnimationLayers ??
                         Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            if (layerIndex < 0)
            {
                Array.Resize(ref layers, layers.Length + 1);
                layerIndex = layers.Length - 1;
                layers[layerIndex] = new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    isDefault = false
                };
            }
            var layer = layers[layerIndex];
            layer.animatorController = controller;
            layer.isDefault = false;
            layers[layerIndex] = layer;
            descriptor.baseAnimationLayers = layers;
        }

        private static string CreateStagingFolder()
        {
            EnsureFolder(GeneratedRoot);
            var folder = GeneratedRoot + "/__Staging_" +
                         Guid.NewGuid().ToString("N");
            var error = AssetDatabase.CreateFolder(
                GeneratedRoot, Path.GetFileName(folder));
            if (string.IsNullOrEmpty(error) || !AssetDatabase.IsValidFolder(folder))
                throw new InvalidOperationException(
                    "Could not create the generated-asset staging folder.");
            return folder;
        }

        private static void CommitStagingFolder(
            string stagingFolder,
            string finalFolder)
        {
            var backup = finalFolder + "__Backup_" +
                         Guid.NewGuid().ToString("N");
            var hadFinal = AssetDatabase.IsValidFolder(finalFolder);
            if (hadFinal)
            {
                var backupError = AssetDatabase.MoveAsset(finalFolder, backup);
                if (!string.IsNullOrEmpty(backupError))
                    throw new InvalidOperationException(
                        "Could not preserve the previous generated compressor assets: " +
                        backupError);
            }
            var moveError = AssetDatabase.MoveAsset(stagingFolder, finalFolder);
            if (string.IsNullOrEmpty(moveError))
            {
                if (hadFinal) AssetDatabase.DeleteAsset(backup);
                return;
            }
            if (hadFinal && AssetDatabase.IsValidFolder(backup))
                AssetDatabase.MoveAsset(backup, finalFolder);
            throw new InvalidOperationException(
                "Could not commit generated compressor assets: " + moveError);
        }

        private static void EnsureFolder(string folder)
        {
            var current = "Assets";
            foreach (var segment in folder.Substring("Assets/".Length).Split('/'))
            {
                var next = current + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next))
                {
                    var guid = AssetDatabase.CreateFolder(current, segment);
                    if (string.IsNullOrEmpty(guid))
                        throw new InvalidOperationException(
                            "Could not create generated folder '" + next + "'.");
                }
                current = next;
            }
        }

        private static string SourceDependencyIdentity(
            VRCAvatarDescriptor descriptor,
            VRCExpressionParameters parameters)
        {
            var parts = new List<string>();
            void Add(UnityEngine.Object asset)
            {
                if (asset == null) return;
                var path = AssetDatabase.GetAssetPath(asset);
                parts.Add(string.IsNullOrEmpty(path)
                    ? asset.name + ":transient"
                    : path + ":" + AssetDatabase.GetAssetDependencyHash(path));
            }
            Add(parameters);
            Add(descriptor.expressionsMenu);
            var layers = descriptor.baseAnimationLayers ??
                         Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            var fx = layers.FirstOrDefault(layer =>
                layer.type == VRCAvatarDescriptor.AnimLayerType.FX);
            Add(fx.animatorController);
            return string.Join(";", parts);
        }

        private static AnimatorControllerParameterType AnimatorType(
            VRCExpressionParameters.ValueType type)
        {
            switch (type)
            {
                case VRCExpressionParameters.ValueType.Bool:
                    return AnimatorControllerParameterType.Bool;
                case VRCExpressionParameters.ValueType.Int:
                    return AnimatorControllerParameterType.Int;
                default:
                    return AnimatorControllerParameterType.Float;
            }
        }

        private static string Sanitize(string value)
        {
            return new string((value ?? "Avatar")
                .Select(character => char.IsLetterOrDigit(character) ||
                                     character == '-' || character == '_'
                    ? character
                    : '_').ToArray());
        }

        private static bool Fail(
            ParameterCompressorData component,
            string message)
        {
            if (component != null)
                component.SetBuildSummary(new ParameterCompressionBuildSummary
                {
                    message = "Build failed: " + message
                });
            Debug.LogError("[YUCP Parameter Compressor] " + message, component);
            return false;
        }
    }

    /// <summary>Named build phases, shown in the progress bar and timed for the log.</summary>
    internal sealed class Phases
    {
        private readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        private readonly List<string> finished = new List<string>();
        private string current;

        internal void Begin(string name, float progress)
        {
            End();
            current = name;
            watch.Restart();
            Report(name, progress);
        }

        internal void Report(string message, float progress)
        {
            EditorUtility.DisplayProgressBar("YUCP Parameter Compressor", message, progress);
        }

        internal string Finish()
        {
            End();
            return string.Join("; ", finished);
        }

        private void End()
        {
            if (current != null)
                finished.Add(current + " " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s");
            current = null;
        }
    }

    public sealed class ParameterCompressorFinalValidator :
        IVRCSDKPreprocessAvatarCallback
    {
        private sealed class Pending
        {
            internal int targetBits;
        }

        private static readonly ConditionalWeakTable<GameObject, Pending> PendingByAvatar =
            new ConditionalWeakTable<GameObject, Pending>();
        private static readonly object Gate = new object();

        public int callbackOrder => int.MaxValue - 1;

        internal static void Mark(GameObject avatarRoot, int targetBits)
        {
            if (avatarRoot == null) return;
            lock (Gate)
            {
                PendingByAvatar.Remove(avatarRoot);
                PendingByAvatar.Add(avatarRoot, new Pending
                {
                    targetBits = targetBits
                });
            }
        }

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            if (avatarRoot == null) return true;
            Pending pending;
            lock (Gate)
            {
                if (!PendingByAvatar.TryGetValue(avatarRoot, out pending))
                    return true;
                PendingByAvatar.Remove(avatarRoot);
            }
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            var parameters = descriptor != null
                ? descriptor.expressionParameters
                : null;
            if (parameters == null) return true;
            var bits = parameters.CalcTotalCost();
            if (bits <= ParameterCompressionContract.VrchatParameterBudget)
                return true;
            Debug.LogError(
                "[YUCP Parameter Compressor] A processor that ran after compression " +
                "raised the final synced cost to " + bits + " bits, above " +
                ParameterCompressionContract.VrchatParameterBudget +
                ". The reserved headroom may be consumed by later features, but " +
                "the completed avatar must still fit VRChat's limit.", avatarRoot);
            return false;
        }
    }
}
