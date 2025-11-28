using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using VRLabs.CustomObjectSyncCreator;
using static VRLabs.CustomObjectSyncCreator.ControllerGenerationMethods;

namespace YUCP.Components.Editor
{
    public class CollisionDetectionProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 203;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return true;
            }

            var components = avatarRoot.GetComponentsInChildren<CollisionDetectionData>(true);
            if (components.Length == 0)
            {
                return true;
            }

            var prefab = LoadPrefab();
            if (prefab == null)
            {
                Debug.LogError("[YUCP Collision Detection] Prefab not found at Resources/YUCP.CollisionDetection/Collision Detection.prefab.");
                return false;
            }

            var fxController = LoadFXController();
            if (fxController == null)
            {
                Debug.LogError("[YUCP Collision Detection] FX Controller not found. Please ensure the controller is in the package.");
                return false;
            }

            var members = new List<GroupMember>();
            var hasErrors = false;

            foreach (var component in components)
            {
                var settings = component.ToSettings();
                if (!ValidateTarget(descriptor, component, settings))
                {
                    component.SetBuildSummary("Build failed");
                    hasErrors = true;
                    continue;
                }

                string effectiveGroupId = settings.enableGrouping
                    ? settings.collisionGroupId
                    : GetIsolatedGroupId(settings, descriptor);

                members.Add(new GroupMember(component, settings, effectiveGroupId, !settings.enableGrouping));
            }

            if (members.Count == 0)
            {
                return !hasErrors;
            }

            WarnAboutDivergentGroups(members);

            var groups = members.GroupBy(m => new GroupKey(m.Settings, m.GroupId, m.IsIsolated));
            foreach (var group in groups)
            {
                if (!BuildGroup(descriptor, prefab, fxController, group.Key, group.ToList()))
                {
                    hasErrors = true;
                }
            }

            return !hasErrors;
        }

        private static GameObject LoadPrefab()
        {
            return UnityEngine.Resources.Load<GameObject>("YUCP.CollisionDetection/Collision Detection");
        }

        private static AnimatorController LoadFXController()
        {
            return UnityEngine.Resources.Load<AnimatorController>("YUCP.CollisionDetection/Collision Detection FX");
        }

        private static bool ValidateTarget(VRCAvatarDescriptor descriptor, CollisionDetectionData component, CollisionDetectionData.Settings settings)
        {
            if (settings.targetObject == null)
            {
                Debug.LogError("[YUCP Collision Detection] Target object reference is missing.", component);
                return false;
            }

            if (!settings.targetObject.transform.IsChildOf(descriptor.transform))
            {
                Debug.LogError("[YUCP Collision Detection] Target object must be inside the avatar descriptor hierarchy.", component);
                return false;
            }

            return true;
        }

        private static string ResolveMenuLocation(VRCAvatarDescriptor descriptor, string requestedLocation)
        {
            var trimmed = (requestedLocation ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return string.Empty;
            }

            if (descriptor == null || descriptor.expressionsMenu == null)
            {
                Debug.LogWarning("[YUCP Collision Detection] Expressions menu is not assigned on this avatar. Falling back to root menu for the generated toggle.");
                return string.Empty;
            }

            return trimmed;
        }

        private static void WarnAboutDivergentGroups(IEnumerable<GroupMember> members)
        {
            var byGroupId = members.Where(m => !m.IsIsolated).GroupBy(m => m.GroupId);
            foreach (var group in byGroupId)
            {
                var signatures = new HashSet<GroupSettingsSignature>(group.Select(m => new GroupSettingsSignature(m.Settings)));
                if (signatures.Count > 1)
                {
                    Debug.LogWarning($"[YUCP Collision Detection] Group \"{group.Key}\" contains components with mismatched settings. They will be split into {signatures.Count} separate setups.");
                }
            }
        }

        private static bool BuildGroup(VRCAvatarDescriptor descriptor, GameObject prefab, AnimatorController sourceController, GroupKey key, List<GroupMember> members)
        {
            var targets = members.Select(m => m.Settings.targetObject).ToArray();
            if (targets.Length == 0)
            {
                return true;
            }

            if (targets.Distinct().Count() != targets.Length)
            {
                var groupLabel = key.IsIsolated ? "Isolated group" : $"Group \"{key.CollisionGroupId}\"";
                Debug.LogError($"[YUCP Collision Detection] {groupLabel} references the same object multiple times. Please ensure each component targets a unique object.");
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary("Build failed");
                }
                return false;
            }

            try
            {
                descriptor.customizeAnimationLayers = true;
                var existingController = descriptor.baseAnimationLayers
                    .Where(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX)
                    .Select(x => x.animatorController)
                    .FirstOrDefault();

                AnimatorController mergedController = existingController == null ? null : (AnimatorController)existingController;

                System.IO.Directory.CreateDirectory("Assets/VRLabs/GeneratedAssets/CollisionDetection/Animators/");
                string uniqueControllerPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRLabs/GeneratedAssets/CollisionDetection/Animators/CollisionDetection.controller");

                if (mergedController == null)
                {
                    mergedController = new AnimatorController();
                    AssetDatabase.CreateAsset(mergedController, uniqueControllerPath);
                }
                else
                {
                    AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(mergedController), uniqueControllerPath);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                mergedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(uniqueControllerPath);

                if (mergedController == null)
                {
                    Debug.LogError("[YUCP Collision Detection] Failed to create controller.");
                    foreach (var member in members)
                    {
                        member.Component.SetBuildSummary("Build failed");
                    }
                    return false;
                }

                foreach (var layer in sourceController.layers)
                {
                    var clonedStateMachine = UnityEngine.Object.Instantiate(layer.stateMachine);
                    var newLayer = new AnimatorControllerLayer
                    {
                        name = layer.name,
                        defaultWeight = layer.defaultWeight,
                        avatarMask = layer.avatarMask,
                        blendingMode = layer.blendingMode,
                        syncedLayerAffectsTiming = layer.syncedLayerAffectsTiming,
                        syncedLayerIndex = layer.syncedLayerIndex,
                        stateMachine = clonedStateMachine
                    };
                    mergedController.AddLayer(newLayer);
                }

                foreach (var param in sourceController.parameters)
                {
                    if (mergedController.parameters.All(p => p.name != param.name))
                    {
                        mergedController.AddParameter(param);
                    }
                }

                ControllerGenerationMethods.SerializeController(mergedController);

                var fxLayer = descriptor.baseAnimationLayers.FirstOrDefault(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX);
                fxLayer.isDefault = false;
                fxLayer.animatorController = mergedController;
                var layers = descriptor.baseAnimationLayers.ToList();
                var fxIndex = layers.FindIndex(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX);
                layers[fxIndex] = fxLayer;
                descriptor.baseAnimationLayers = layers.ToArray();

                foreach (var member in members)
                {
                    var settings = member.Settings;
                    InstallSystem(descriptor, prefab, settings);
                }

                var summaryLabel = key.IsIsolated
                    ? "Collision Detection (isolated)"
                    : $"Collision Detection group \"{key.CollisionGroupId}\"";
                var summary = $"{summaryLabel} built ({targets.Length} object{(targets.Length == 1 ? string.Empty : "s")})";
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary(summary);
                }

                if (key.VerboseLogging)
                {
                    Debug.Log($"[YUCP Collision Detection] Generated group \"{key.CollisionGroupId}\" with {targets.Length} object(s).");
                }

                if (key.IncludeCredits)
                {
                    Debug.Log("[YUCP Collision Detection] Built using VRLabs Collision Detection (MIT). Please credit VRLabs when sharing your avatar.");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Collision Detection] Failed to generate collision detection system: {ex.Message}");
                Debug.LogException(ex);
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary("Build failed");
                }
                return false;
            }
        }

        private static void InstallSystem(VRCAvatarDescriptor descriptor, GameObject prefab, CollisionDetectionData.Settings settings)
        {
            var rootObject = descriptor.gameObject;
            var collisionSystem = UnityEngine.Object.Instantiate(prefab, rootObject.transform);
            collisionSystem.name = collisionSystem.name.Replace("(Clone)", "");

            if (settings.targetObject != null)
            {
                var oldPath = AnimationUtility.CalculateTransformPath(settings.targetObject.transform, descriptor.transform);
                settings.targetObject.transform.parent = collisionSystem.transform;
                var newPath = AnimationUtility.CalculateTransformPath(settings.targetObject.transform, descriptor.transform);

                var allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                    .Where(x => x.animatorController != null)
                    .SelectMany(x => x.animatorController.animationClips)
                    .ToArray();

                RenameClipPaths(allClips, false, oldPath, newPath);
            }
        }

        private readonly struct GroupMember
        {
            public GroupMember(CollisionDetectionData component, CollisionDetectionData.Settings settings, string groupId, bool isIsolated)
            {
                Component = component;
                Settings = settings;
                GroupId = groupId;
                IsIsolated = isIsolated;
            }

            public CollisionDetectionData Component { get; }
            public CollisionDetectionData.Settings Settings { get; }
            public string GroupId { get; }
            public bool IsIsolated { get; }
        }

        private readonly struct GroupSettingsSignature : IEquatable<GroupSettingsSignature>
        {
            public GroupSettingsSignature(CollisionDetectionData.Settings settings)
            {
                AlwaysReset = settings.alwaysReset;
                MenuLocation = settings.menuLocation;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            private bool AlwaysReset { get; }
            private string MenuLocation { get; }
            private bool VerboseLogging { get; }
            private bool IncludeCredits { get; }

            public bool Equals(GroupSettingsSignature other)
            {
                return AlwaysReset == other.AlwaysReset &&
                       MenuLocation == other.MenuLocation &&
                       VerboseLogging == other.VerboseLogging &&
                       IncludeCredits == other.IncludeCredits;
            }

            public override bool Equals(object obj)
            {
                return obj is GroupSettingsSignature other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = AlwaysReset.GetHashCode();
                    hashCode = (hashCode * 397) ^ (MenuLocation != null ? MenuLocation.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private readonly struct GroupKey : IEquatable<GroupKey>
        {
            public GroupKey(CollisionDetectionData.Settings settings, string groupId, bool isIsolated)
            {
                CollisionGroupId = groupId;
                IsIsolated = isIsolated;
                AlwaysReset = settings.alwaysReset;
                MenuLocation = settings.menuLocation;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            public string CollisionGroupId { get; }
            public bool IsIsolated { get; }
            public bool AlwaysReset { get; }
            public string MenuLocation { get; }
            public bool VerboseLogging { get; }
            public bool IncludeCredits { get; }

            public bool Equals(GroupKey other)
            {
                return CollisionGroupId == other.CollisionGroupId &&
                       IsIsolated == other.IsIsolated &&
                       AlwaysReset == other.AlwaysReset &&
                       MenuLocation == other.MenuLocation &&
                       VerboseLogging == other.VerboseLogging &&
                       IncludeCredits == other.IncludeCredits;
            }

            public override bool Equals(object obj)
            {
                return obj is GroupKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = CollisionGroupId != null ? CollisionGroupId.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ IsIsolated.GetHashCode();
                    hashCode = (hashCode * 397) ^ AlwaysReset.GetHashCode();
                    hashCode = (hashCode * 397) ^ (MenuLocation != null ? MenuLocation.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private static string GetIsolatedGroupId(CollisionDetectionData.Settings settings, VRCAvatarDescriptor descriptor)
        {
            if (settings.targetObject == null || descriptor == null)
            {
                return $"__Isolated__/{Guid.NewGuid()}";
            }

            string path = AnimationUtility.CalculateTransformPath(settings.targetObject.transform, descriptor.transform);
            return $"__Isolated__/{path}";
        }

        private static void RenameClipPaths(AnimationClip[] clips, bool replaceEntire, string oldPath, string newPath)
        {
            CustomObjectSyncCreator.RenameClipPaths(clips, replaceEntire, oldPath, newPath);
        }
    }
}

