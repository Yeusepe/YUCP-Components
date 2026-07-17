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
            VRCExpressionParameters sourceParameters)
        {
            var currentBits = sourceParameters.CalcTotalCost();
            var targetBits = Math.Max(0,
                ParameterCompressionContract.VrchatParameterBudget -
                component.reserveSyncedBits);
            var catalog = ParameterCompressionCatalog.Scan(
                avatarRoot, descriptor, component);
            if (!ValidateRules(component, sourceParameters, catalog, out var ruleError))
                return Fail(component, ruleError);

            var eligible = catalog.entries.Where(entry => entry.eligible).ToArray();
            var candidates = eligible.Select(entry =>
                new ParameterCompressionCandidate(
                    entry.parameter.name,
                    ValueKind(entry.parameter.valueType),
                    entry.minimumLevels,
                    entry.desiredLevels,
                    // Compress background state before latency-sensitive state.
                    // The public enum already runs Immediate=0..Background=3,
                    // and the planner selects larger preservation scores first.
                    (int)entry.priority,
                    entry.CurrentBits,
                    entry.explicitlyIncluded)).ToArray();

            var maximumWires = component.optimizationBias >= 0.82f
                ? 4
                : component.optimizationBias >= 0.64f
                    ? 5
                    : 7;
            // The runtime transport sends positional snapshot blocks. Its value
            // width is governed by the largest individual domain, not by the sum
            // of every domain used by the foreground mixed-record codec. Give the
            // planner enough enumerative capacity to preserve every requested
            // level; lowering precision here would not make snapshot playback
            // faster and would only discard information needlessly.
            var policy = new ParameterCompressionBusPolicy(
                3, maximumWires, 16, 0.1f);
            if (!ParameterCompressionPlanner.TryCreatePlan(
                    currentBits, targetBits, candidates, policy,
                    out var plan, out var planningError))
            {
                var safeBits = eligible.Sum(entry => entry.CurrentBits);
                return Fail(component,
                    planningError + " The final avatar uses " + currentBits +
                    " synced bits, the selected target is " + targetBits +
                    ", and safe candidates account for " + safeBits + " bits.");
            }

            if (!plan.UsesCompression)
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
            var selected = plan.Allocations.Select(allocation =>
            {
                var source = byName[allocation.Name];
                return new SelectedParameter
                {
                    source = source,
                    levels = allocation.Levels
                };
            }).ToArray();

            var contentIdentity = component.StableId + "|prefix:" +
                component.NormalizedPrefix + "|avatar:" + avatarRoot.name +
                "|source:" + SourceDependencyIdentity(descriptor,
                    sourceParameters) + "|" + string.Join("|", selected
                .OrderBy(item => item.source.parameter.name, StringComparer.Ordinal)
                .Select(item => item.source.parameter.name + ":" +
                                item.source.parameter.valueType + ":" +
                                item.levels + ":" + item.source.minimum + ":" +
                                item.source.maximum + ":" +
                                item.source.priority + ":" +
                                item.source.group)) +
                "|bus:" + plan.BusBits + "|block:" + BlockSize(component) +
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

                var builderEntries = selected.Select(item =>
                    new ParameterCompressorAnimatorBuilder.Entry
                    {
                        name = item.source.parameter.name,
                        type = AnimatorType(item.source.parameter.valueType),
                        levels = item.levels,
                        minimum = item.source.minimum,
                        maximum = item.source.maximum,
                        priority = (int)item.source.priority,
                        group = item.source.group
                    }).ToArray();
                var built = ParameterCompressorAnimatorBuilder.Build(
                    new ParameterCompressorAnimatorBuilder.Request
                    {
                        controller = clonedController,
                        prefix = component.NormalizedPrefix,
                        busBits = plan.BusBits,
                        entries = builderEntries,
                        blockSize = BlockSize(component),
                        signalSeconds = 0.1f,
                        spacerSeconds = 0.1f
                    });

                var selectedNames = new HashSet<string>(
                    selected.Select(item => item.source.parameter.name),
                    StringComparer.Ordinal);
                foreach (var parameter in clonedParameters.parameters)
                    if (parameter != null && selectedNames.Contains(parameter.name))
                        parameter.networkSynced = false;
                AddCarrierDeclarations(
                    clonedParameters, built.carrierParameters,
                    component.NormalizedPrefix);

                var afterBits = clonedParameters.CalcTotalCost();
                if (afterBits != plan.FinalBits)
                    throw new InvalidOperationException(
                        "The generated parameter asset uses " + afterBits +
                        " bits, but the deterministic plan expected " +
                        plan.FinalBits + ". No source assets were changed.");
                if (afterBits > targetBits)
                    throw new InvalidOperationException(
                        "The generated transport still exceeds the selected " +
                        targetBits + "-bit target.");

                descriptor.expressionParameters = clonedParameters;
                AssignFxController(descriptor, clonedController, fxLayerIndex);
                descriptor.customizeAnimationLayers = true;
                EditorUtility.SetDirty(clonedController);
                EditorUtility.SetDirty(clonedParameters);
                EditorUtility.SetDirty(descriptor);
                AssetDatabase.SaveAssetIfDirty(clonedController);
                AssetDatabase.SaveAssetIfDirty(clonedParameters);
                CommitStagingFolder(folder, finalFolder);

                var reducedPrecision = selected.Count(item =>
                    item.levels < item.source.desiredLevels);
                component.SetBuildSummary(new ParameterCompressionBuildSummary
                {
                    hasResult = true,
                    beforeBits = currentBits,
                    afterBits = afterBits,
                    carrierBits = plan.BusBits,
                    compressedParameters = selected.Length,
                    protectedParameters = catalog.entries.Count - selected.Length,
                    nominalFullRefreshSeconds = built.estimatedFullRefreshSeconds,
                    transportName = plan.BusBits +
                                    "-Bool delay-insensitive radix-" + plan.Radix,
                    message = reducedPrecision == 0
                        ? "Native VRChat precision is preserved. Blocks commit atomically and replay for late joiners."
                        : reducedPrecision +
                          " values use planned lower precision to cross a radix boundary."
                });
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
            internal int levels;
        }

        private static int BlockSize(ParameterCompressorData component)
        {
            return Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Lerp(4f, 12f, component.optimizationBias)),
                4, 12);
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
                list.Add(new VRCExpressionParameters.Parameter
                {
                    name = carrier,
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
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

        private static ParameterCompressionValueKind ValueKind(
            VRCExpressionParameters.ValueType type)
        {
            switch (type)
            {
                case VRCExpressionParameters.ValueType.Bool:
                    return ParameterCompressionValueKind.Bool;
                case VRCExpressionParameters.ValueType.Int:
                    return ParameterCompressionValueKind.Int;
                default:
                    return ParameterCompressionValueKind.Float;
            }
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
