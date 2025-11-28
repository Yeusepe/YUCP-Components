using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;
using VRLabs.CustomObjectSyncCreator;
using static VRLabs.CustomObjectSyncCreator.ControllerGenerationMethods;
using com.vrcfury.api;

namespace YUCP.Components.Editor
{
    public class RigidbodyThrowProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 208;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return true;
            }

            var components = avatarRoot.GetComponentsInChildren<RigidbodyThrowData>(true);
            if (components.Length == 0)
            {
                return true;
            }

            var prefab = LoadPrefab();
            if (prefab == null)
            {
                Debug.LogError("[YUCP Rigidbody Throw] Prefab not found at Resources/YUCP.RigidbodyThrow/Rigidbody Throw.prefab.");
                return false;
            }

            var fxController = LoadFXController();
            if (fxController == null)
            {
                Debug.LogError("[YUCP Rigidbody Throw] FX Controller not found. Please ensure the controller is in the package.");
                return false;
            }

            var expressionParameters = LoadExpressionParameters();
            if (expressionParameters == null)
            {
                Debug.LogError("[YUCP Rigidbody Throw] Expression Parameters not found. Please ensure the parameters asset is in the package.");
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
                    ? settings.throwGroupId
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
                if (!BuildGroup(descriptor, prefab, fxController, expressionParameters, group.Key, group.ToList()))
                {
                    hasErrors = true;
                }
            }

            return !hasErrors;
        }

        private static GameObject LoadPrefab()
        {
            return UnityEngine.Resources.Load<GameObject>("YUCP.RigidbodyThrow/Rigidbody Throw");
        }

        private static AnimatorController LoadFXController()
        {
            return UnityEngine.Resources.Load<AnimatorController>("YUCP.RigidbodyThrow/Rigidbody Throw FX");
        }

        private static VRCExpressionParameters LoadExpressionParameters()
        {
            return UnityEngine.Resources.Load<VRCExpressionParameters>("YUCP.RigidbodyThrow/Rigidbody Throw Parameters");
        }

        private static bool ValidateTarget(VRCAvatarDescriptor descriptor, RigidbodyThrowData component, RigidbodyThrowData.Settings settings)
        {
            if (settings.targetObject == null)
            {
                Debug.LogError("[YUCP Rigidbody Throw] Target object reference is missing.", component);
                return false;
            }

            if (!settings.targetObject.transform.IsChildOf(descriptor.transform))
            {
                Debug.LogError("[YUCP Rigidbody Throw] Target object must be inside the avatar descriptor hierarchy.", component);
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
                    Debug.LogWarning($"[YUCP Rigidbody Throw] Group \"{group.Key}\" contains components with mismatched settings. They will be split into {signatures.Count} separate setups.");
                }
            }
        }

        private static bool BuildGroup(VRCAvatarDescriptor descriptor, GameObject prefab, AnimatorController sourceController, VRCExpressionParameters sourceParameters, GroupKey key, List<GroupMember> members)
        {
            var targets = members.Select(m => m.Settings.targetObject).ToArray();
            if (targets.Length == 0)
            {
                return true;
            }

            if (targets.Distinct().Count() != targets.Length)
            {
                var groupLabel = key.IsIsolated ? "Isolated group" : $"Group \"{key.ThrowGroupId}\"";
                Debug.LogError($"[YUCP Rigidbody Throw] {groupLabel} references the same object multiple times. Please ensure each component targets a unique object.");
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary("Build failed");
                }
                return false;
            }

            try
            {
                VRCFuryHelper.AddControllerToVRCFury(descriptor, sourceController);
                VRCFuryHelper.AddParamsToVRCFury(descriptor, sourceParameters);

                foreach (var member in members)
                {
                    var settings = member.Settings;
                    InstallSystem(descriptor, prefab, settings);
                }

                var summaryLabel = key.IsIsolated
                    ? "Rigidbody Throw (isolated)"
                    : $"Rigidbody Throw group \"{key.ThrowGroupId}\"";
                var summary = $"{summaryLabel} built ({targets.Length} object{(targets.Length == 1 ? string.Empty : "s")})";
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary(summary);
                }

                if (key.VerboseLogging)
                {
                    Debug.Log($"[YUCP Rigidbody Throw] Generated group \"{key.ThrowGroupId}\" with {targets.Length} object(s).");
                }

                if (key.IncludeCredits)
                {
                    Debug.Log("[YUCP Rigidbody Throw] Built using VRLabs Rigidbody Throw (MIT). Please credit VRLabs when sharing your avatar.");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Rigidbody Throw] Failed to generate rigidbody throw system: {ex.Message}");
                Debug.LogException(ex);
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary("Build failed");
                }
                return false;
            }
        }

        private static void InstallSystem(VRCAvatarDescriptor descriptor, GameObject prefab, RigidbodyThrowData.Settings settings)
        {
            var rootObject = descriptor.gameObject;
            var throwSystem = UnityEngine.Object.Instantiate(prefab, rootObject.transform);
            throwSystem.name = throwSystem.name.Replace("(Clone)", "");

            var throwTarget = throwSystem.transform.Find("Throw/Throw Target");
            if (throwTarget == null)
            {
                throwTarget = throwSystem.transform.Find("Throw Target");
            }

            if (throwTarget != null && settings.throwTarget != null)
            {
                var oldPath = AnimationUtility.CalculateTransformPath(settings.throwTarget.transform, descriptor.transform);
                throwTarget.parent = settings.throwTarget.parent;
                throwTarget.localPosition = settings.throwTarget.localPosition;
                throwTarget.localRotation = settings.throwTarget.localRotation;
                throwTarget.localScale = settings.throwTarget.localScale;
                var newPath = AnimationUtility.CalculateTransformPath(throwTarget.transform, descriptor.transform);

                var allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                    .Where(x => x.animatorController != null)
                    .SelectMany(x => x.animatorController.animationClips)
                    .ToArray();

                CustomObjectSyncCreator.RenameClipPaths(allClips, false, oldPath, newPath);
            }

            if (settings.targetObject != null)
            {
                var oldPath = AnimationUtility.CalculateTransformPath(settings.targetObject.transform, descriptor.transform);
                settings.targetObject.transform.parent = throwSystem.transform;
                var newPath = AnimationUtility.CalculateTransformPath(settings.targetObject.transform, descriptor.transform);

                var allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                    .Where(x => x.animatorController != null)
                    .SelectMany(x => x.animatorController.animationClips)
                    .ToArray();

                CustomObjectSyncCreator.RenameClipPaths(allClips, false, oldPath, newPath);
            }

            if (settings.enableRotationSync)
            {
                var rotationSync = throwSystem.transform.Find("Throw/Quick Position Sync/Rotation Sync");
                if (rotationSync != null)
                {
                    rotationSync.gameObject.SetActive(true);
                }
            }
        }

        private readonly struct GroupMember
        {
            public GroupMember(RigidbodyThrowData component, RigidbodyThrowData.Settings settings, string groupId, bool isIsolated)
            {
                Component = component;
                Settings = settings;
                GroupId = groupId;
                IsIsolated = isIsolated;
            }

            public RigidbodyThrowData Component { get; }
            public RigidbodyThrowData.Settings Settings { get; }
            public string GroupId { get; }
            public bool IsIsolated { get; }
        }

        private readonly struct GroupSettingsSignature : IEquatable<GroupSettingsSignature>
        {
            public GroupSettingsSignature(RigidbodyThrowData.Settings settings)
            {
                EnableRotationSync = settings.enableRotationSync;
                MenuLocation = settings.menuLocation;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            private bool EnableRotationSync { get; }
            private string MenuLocation { get; }
            private bool VerboseLogging { get; }
            private bool IncludeCredits { get; }

            public bool Equals(GroupSettingsSignature other)
            {
                return EnableRotationSync == other.EnableRotationSync &&
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
                    var hashCode = EnableRotationSync.GetHashCode();
                    hashCode = (hashCode * 397) ^ (MenuLocation != null ? MenuLocation.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private readonly struct GroupKey : IEquatable<GroupKey>
        {
            public GroupKey(RigidbodyThrowData.Settings settings, string groupId, bool isIsolated)
            {
                ThrowGroupId = groupId;
                IsIsolated = isIsolated;
                EnableRotationSync = settings.enableRotationSync;
                MenuLocation = settings.menuLocation;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            public string ThrowGroupId { get; }
            public bool IsIsolated { get; }
            public bool EnableRotationSync { get; }
            public string MenuLocation { get; }
            public bool VerboseLogging { get; }
            public bool IncludeCredits { get; }

            public bool Equals(GroupKey other)
            {
                return ThrowGroupId == other.ThrowGroupId &&
                       IsIsolated == other.IsIsolated &&
                       EnableRotationSync == other.EnableRotationSync &&
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
                    var hashCode = ThrowGroupId != null ? ThrowGroupId.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ IsIsolated.GetHashCode();
                    hashCode = (hashCode * 397) ^ EnableRotationSync.GetHashCode();
                    hashCode = (hashCode * 397) ^ (MenuLocation != null ? MenuLocation.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private static string GetIsolatedGroupId(RigidbodyThrowData.Settings settings, VRCAvatarDescriptor descriptor)
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

