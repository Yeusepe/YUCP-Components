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
using com.vrcfury.api;

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
            if (settings.appliedObject == null)
            {
                Debug.LogError("[YUCP Collision Detection] Target object reference is missing.", component);
                return false;
            }

            if (!settings.appliedObject.transform.IsChildOf(descriptor.transform))
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
            var targets = members.Select(m => m.Settings.appliedObject).ToArray();
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
                VRCFuryHelper.AddControllerToVRCFury(descriptor, sourceController);

                foreach (var member in members)
                {
                    var settings = member.Settings;
                    InstallSystem(descriptor, prefab, settings);
                }

                var menuLocation = members.Count > 0 ? members[0].Settings.menuLocation : string.Empty;
                var globalParamReset = members.Count > 0 ? members[0].Settings.globalParameterReset : string.Empty;
                var globalParamAlwaysReset = members.Count > 0 ? members[0].Settings.globalParameterAlwaysReset : string.Empty;
                if (!string.IsNullOrEmpty(menuLocation))
                {
                    var menu = VRCFuryHelper.GetMenuFromLocation(descriptor, menuLocation);
                    if (menu != null)
                    {
                        if (!string.IsNullOrEmpty(globalParamReset))
                        {
                            VRCFuryHelper.AddGlobalParamToVRCFury(descriptor, globalParamReset);
                        }
                        if (!string.IsNullOrEmpty(globalParamAlwaysReset))
                        {
                            VRCFuryHelper.AddGlobalParamToVRCFury(descriptor, globalParamAlwaysReset);
                        }
                        VRCFuryHelper.AddMenuToggle(menu, "Collision Detection Reset", "CollisionDetection/Reset");
                        VRCFuryHelper.AddMenuToggle(menu, "Collision Detection Always Reset", "CollisionDetection/AlwaysReset");
                    }
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

            var particleSystem = collisionSystem.GetComponent<ParticleSystem>();
            if (particleSystem != null)
            {
                var collision = particleSystem.collision;
                var triggers = particleSystem.trigger;
                
                if (settings.useTriggers)
                {
                    collision.enabled = false;
                    triggers.enabled = true;
                }
                else
                {
                    collision.enabled = true;
                    triggers.enabled = false;
                    collision.collidesWith = settings.collisionLayers;
                }

                if (settings.particleScale != 1f)
                {
                    var shape = particleSystem.shape;
                    shape.scale = new Vector3(settings.particleScale, settings.particleScale, settings.particleScale);
                    collisionSystem.transform.localScale = Vector3.one * settings.particleScale;
                }
            }

            if (settings.appliedObject != null)
            {
                var container = collisionSystem.transform.Find("Container");
                if (container == null)
                {
                    // If Container doesn't exist, use the collisionSystem itself as container
                    container = collisionSystem.transform;
                }

                // Store the world position and rotation before reparenting
                var worldPosition = settings.appliedObject.transform.position;
                var worldRotation = settings.appliedObject.transform.rotation;
                var worldScale = settings.appliedObject.transform.lossyScale;

                var oldPath = AnimationUtility.CalculateTransformPath(settings.appliedObject.transform, descriptor.transform);
                
                // Reparent to container
                settings.appliedObject.transform.parent = container;
                
                // Reset object's local transform to identity (0,0,0 position, identity rotation, 1,1,1 scale)
                settings.appliedObject.transform.localPosition = Vector3.zero;
                settings.appliedObject.transform.localRotation = Quaternion.identity;
                settings.appliedObject.transform.localScale = Vector3.one;
                
                // Adjust container's transform to maintain the object's original world position
                // Calculate what the container's world transform should be so that the object (at local 0,0,0) 
                // appears at its original world position
                container.position = worldPosition;
                container.rotation = worldRotation;
                
                // Note: Scale is trickier with lossyScale, so we'll preserve the container's existing scale
                // The object's local scale is now 1,1,1, so it will use the container's scale
                
                var newPath = AnimationUtility.CalculateTransformPath(settings.appliedObject.transform, descriptor.transform);

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
                CollisionLayers = settings.collisionLayers;
                UseTriggers = settings.useTriggers;
                ParticleScale = settings.particleScale;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            private bool AlwaysReset { get; }
            private string MenuLocation { get; }
            private LayerMask CollisionLayers { get; }
            private bool UseTriggers { get; }
            private float ParticleScale { get; }
            private bool VerboseLogging { get; }
            private bool IncludeCredits { get; }

            public bool Equals(GroupSettingsSignature other)
            {
                return AlwaysReset == other.AlwaysReset &&
                       MenuLocation == other.MenuLocation &&
                       CollisionLayers == other.CollisionLayers &&
                       UseTriggers == other.UseTriggers &&
                       Mathf.Approximately(ParticleScale, other.ParticleScale) &&
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
                    hashCode = (hashCode * 397) ^ CollisionLayers.GetHashCode();
                    hashCode = (hashCode * 397) ^ UseTriggers.GetHashCode();
                    hashCode = (hashCode * 397) ^ ParticleScale.GetHashCode();
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
                CollisionLayers = settings.collisionLayers;
                UseTriggers = settings.useTriggers;
                ParticleScale = settings.particleScale;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            public string CollisionGroupId { get; }
            public bool IsIsolated { get; }
            public bool AlwaysReset { get; }
            public string MenuLocation { get; }
            public LayerMask CollisionLayers { get; }
            public bool UseTriggers { get; }
            public float ParticleScale { get; }
            public bool VerboseLogging { get; }
            public bool IncludeCredits { get; }

            public bool Equals(GroupKey other)
            {
                return CollisionGroupId == other.CollisionGroupId &&
                       IsIsolated == other.IsIsolated &&
                       AlwaysReset == other.AlwaysReset &&
                       MenuLocation == other.MenuLocation &&
                       CollisionLayers == other.CollisionLayers &&
                       UseTriggers == other.UseTriggers &&
                       Mathf.Approximately(ParticleScale, other.ParticleScale) &&
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
                    hashCode = (hashCode * 397) ^ CollisionLayers.GetHashCode();
                    hashCode = (hashCode * 397) ^ UseTriggers.GetHashCode();
                    hashCode = (hashCode * 397) ^ ParticleScale.GetHashCode();
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private static string GetIsolatedGroupId(CollisionDetectionData.Settings settings, VRCAvatarDescriptor descriptor)
        {
            if (settings.appliedObject == null || descriptor == null)
            {
                return $"__Isolated__/{Guid.NewGuid()}";
            }

            string path = AnimationUtility.CalculateTransformPath(settings.appliedObject.transform, descriptor.transform);
            return $"__Isolated__/{path}";
        }

        private static void RenameClipPaths(AnimationClip[] clips, bool replaceEntire, string oldPath, string newPath)
        {
            CustomObjectSyncCreator.RenameClipPaths(clips, replaceEntire, oldPath, newPath);
        }
    }
}

