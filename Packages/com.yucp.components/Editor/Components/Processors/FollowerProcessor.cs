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
    public class FollowerProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 205;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return true;
            }

            var components = avatarRoot.GetComponentsInChildren<FollowerData>(true);
            if (components.Length == 0)
            {
                return true;
            }

            var prefab = LoadPrefab();
            if (prefab == null)
            {
                Debug.LogError("[YUCP Follower] Prefab not found at Resources/YUCP.Follower/Follower.prefab.");
                return false;
            }

            var fxController = LoadFXController();
            if (fxController == null)
            {
                Debug.LogError("[YUCP Follower] FX Controller not found. Please ensure the controller is in the package.");
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
                    ? settings.followerGroupId
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
            return UnityEngine.Resources.Load<GameObject>("YUCP.Follower/Follower");
        }

        private static AnimatorController LoadFXController()
        {
            return UnityEngine.Resources.Load<AnimatorController>("YUCP.Follower/Follower FX");
        }

        private static bool ValidateTarget(VRCAvatarDescriptor descriptor, FollowerData component, FollowerData.Settings settings)
        {
            if (settings.targetObject == null)
            {
                Debug.LogError("[YUCP Follower] Target object reference is missing.", component);
                return false;
            }

            if (!settings.targetObject.transform.IsChildOf(descriptor.transform))
            {
                Debug.LogError("[YUCP Follower] Target object must be inside the avatar descriptor hierarchy.", component);
                return false;
            }

            return true;
        }

        private static void WarnAboutDivergentGroups(IEnumerable<GroupMember> members)
        {
            var byGroupId = members.Where(m => !m.IsIsolated).GroupBy(m => m.GroupId);
            foreach (var group in byGroupId)
            {
                var signatures = new HashSet<GroupSettingsSignature>(group.Select(m => new GroupSettingsSignature(m.Settings)));
                if (signatures.Count > 1)
                {
                    Debug.LogWarning($"[YUCP Follower] Group \"{group.Key}\" contains components with mismatched settings. They will be split into {signatures.Count} separate setups.");
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
                var groupLabel = key.IsIsolated ? "Isolated group" : $"Group \"{key.FollowerGroupId}\"";
                Debug.LogError($"[YUCP Follower] {groupLabel} references the same object multiple times. Please ensure each component targets a unique object.");
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

                var summaryLabel = key.IsIsolated
                    ? "Follower (isolated)"
                    : $"Follower group \"{key.FollowerGroupId}\"";
                var summary = $"{summaryLabel} built ({targets.Length} object{(targets.Length == 1 ? string.Empty : "s")})";
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary(summary);
                }

                if (key.VerboseLogging)
                {
                    Debug.Log($"[YUCP Follower] Generated group \"{key.FollowerGroupId}\" with {targets.Length} object(s).");
                }

                if (key.IncludeCredits)
                {
                    Debug.Log("[YUCP Follower] Built using VRLabs Follower (MIT). Please credit VRLabs when sharing your avatar.");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Follower] Failed to generate follower system: {ex.Message}");
                Debug.LogException(ex);
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary("Build failed");
                }
                return false;
            }
        }

        private static void InstallSystem(VRCAvatarDescriptor descriptor, GameObject prefab, FollowerData.Settings settings)
        {
            var rootObject = descriptor.gameObject;
            var followerSystem = UnityEngine.Object.Instantiate(prefab, rootObject.transform);
            followerSystem.name = followerSystem.name.Replace("(Clone)", "");

            var followerTarget = followerSystem.transform.Find("Follower Target");
            if (followerTarget != null && settings.followerTarget != null)
            {
                var oldPath = AnimationUtility.CalculateTransformPath(settings.followerTarget.transform, descriptor.transform);
                followerTarget.parent = settings.followerTarget.parent;
                followerTarget.localPosition = settings.followerTarget.localPosition;
                followerTarget.localRotation = settings.followerTarget.localRotation;
                followerTarget.localScale = settings.followerTarget.localScale;
                var newPath = AnimationUtility.CalculateTransformPath(followerTarget.transform, descriptor.transform);

                var allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                    .Where(x => x.animatorController != null)
                    .SelectMany(x => x.animatorController.animationClips)
                    .ToArray();

                CustomObjectSyncCreator.RenameClipPaths(allClips, false, oldPath, newPath);
            }

            if (settings.lookTarget != null)
            {
                var lookTargetObj = followerSystem.transform.Find("Follower Target/Look Target");
                if (lookTargetObj != null)
                {
                    var oldPath = AnimationUtility.CalculateTransformPath(lookTargetObj.transform, descriptor.transform);
                    lookTargetObj.parent = settings.lookTarget.parent;
                    lookTargetObj.localPosition = settings.lookTarget.localPosition;
                    lookTargetObj.localRotation = settings.lookTarget.localRotation;
                    lookTargetObj.localScale = settings.lookTarget.localScale;
                    var newPath = AnimationUtility.CalculateTransformPath(lookTargetObj.transform, descriptor.transform);

                    var allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                        .Where(x => x.animatorController != null)
                        .SelectMany(x => x.animatorController.animationClips)
                        .ToArray();

                    CustomObjectSyncCreator.RenameClipPaths(allClips, false, oldPath, newPath);
                }
            }

            if (settings.followSpeed != 1f)
            {
                var fxLayer = descriptor.baseAnimationLayers
                    .FirstOrDefault(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX);
                var fxController = fxLayer.animatorController as AnimatorController;
                if (fxController != null)
                {
                    var clips = fxController.animationClips;
                    foreach (var clip in clips)
                    {
                        if (clip != null && clip.name.Contains("Follow"))
                        {
                            var bindings = AnimationUtility.GetCurveBindings(clip);
                            foreach (var binding in bindings)
                            {
                                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                                if (curve != null && curve.keys.Length > 0)
                                {
                                    for (int i = 0; i < curve.keys.Length; i++)
                                    {
                                        var key = curve.keys[i];
                                        key.value *= settings.followSpeed;
                                        curve.MoveKey(i, key);
                                    }
                                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                                }
                            }
                        }
                    }
                }
            }

            if (settings.targetObject != null)
            {
                var container = followerSystem.transform.Find("Container");
                if (container == null)
                {
                    Debug.LogError("[YUCP Follower] Prefab missing Container object.");
                    return;
                }

                var oldPath = AnimationUtility.CalculateTransformPath(settings.targetObject.transform, descriptor.transform);
                settings.targetObject.transform.parent = container;
                var newPath = AnimationUtility.CalculateTransformPath(settings.targetObject.transform, descriptor.transform);

                var allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                    .Where(x => x.animatorController != null)
                    .SelectMany(x => x.animatorController.animationClips)
                    .ToArray();

                CustomObjectSyncCreator.RenameClipPaths(allClips, false, oldPath, newPath);
            }
        }

        private readonly struct GroupMember
        {
            public GroupMember(FollowerData component, FollowerData.Settings settings, string groupId, bool isIsolated)
            {
                Component = component;
                Settings = settings;
                GroupId = groupId;
                IsIsolated = isIsolated;
            }

            public FollowerData Component { get; }
            public FollowerData.Settings Settings { get; }
            public string GroupId { get; }
            public bool IsIsolated { get; }
        }

        private readonly struct GroupSettingsSignature : IEquatable<GroupSettingsSignature>
        {
            public GroupSettingsSignature(FollowerData.Settings settings)
            {
                MenuLocation = settings.menuLocation;
                FollowSpeed = settings.followSpeed;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            private string MenuLocation { get; }
            private float FollowSpeed { get; }
            private bool VerboseLogging { get; }
            private bool IncludeCredits { get; }

            public bool Equals(GroupSettingsSignature other)
            {
                return MenuLocation == other.MenuLocation &&
                       Mathf.Approximately(FollowSpeed, other.FollowSpeed) &&
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
                    var hashCode = MenuLocation != null ? MenuLocation.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ FollowSpeed.GetHashCode();
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private readonly struct GroupKey : IEquatable<GroupKey>
        {
            public GroupKey(FollowerData.Settings settings, string groupId, bool isIsolated)
            {
                FollowerGroupId = groupId;
                IsIsolated = isIsolated;
                MenuLocation = settings.menuLocation;
                FollowSpeed = settings.followSpeed;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            public string FollowerGroupId { get; }
            public bool IsIsolated { get; }
            public string MenuLocation { get; }
            public float FollowSpeed { get; }
            public bool VerboseLogging { get; }
            public bool IncludeCredits { get; }

            public bool Equals(GroupKey other)
            {
                return FollowerGroupId == other.FollowerGroupId &&
                       IsIsolated == other.IsIsolated &&
                       MenuLocation == other.MenuLocation &&
                       Mathf.Approximately(FollowSpeed, other.FollowSpeed) &&
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
                    var hashCode = FollowerGroupId != null ? FollowerGroupId.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ IsIsolated.GetHashCode();
                    hashCode = (hashCode * 397) ^ (MenuLocation != null ? MenuLocation.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ FollowSpeed.GetHashCode();
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private static string GetIsolatedGroupId(FollowerData.Settings settings, VRCAvatarDescriptor descriptor)
        {
            if (settings.targetObject == null || descriptor == null)
            {
                return $"__Isolated__/{Guid.NewGuid()}";
            }

            string path = AnimationUtility.CalculateTransformPath(settings.targetObject.transform, descriptor.transform);
            return $"__Isolated__/{path}";
        }
    }
}

