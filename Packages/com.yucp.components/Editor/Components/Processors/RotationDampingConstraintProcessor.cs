using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using VRLabs.CustomObjectSyncCreator;

namespace YUCP.Components.Editor
{
    public class RotationDampingConstraintProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 202;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return true;
            }

            var components = avatarRoot.GetComponentsInChildren<RotationDampingConstraintData>(true);
            if (components.Length == 0)
            {
                return true;
            }

            var prefab = LoadPrefab();
            if (prefab == null)
            {
                Debug.LogError("[YUCP Rotation Damping Constraint] Prefab not found at Resources/YUCP.DampingConstraints/Rotation Damping Constraint.prefab.");
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
                    ? settings.constraintGroupId
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
                if (!BuildGroup(descriptor, prefab, group.Key, group.ToList()))
                {
                    hasErrors = true;
                }
            }

            return !hasErrors;
        }

        private static GameObject LoadPrefab()
        {
            return UnityEngine.Resources.Load<GameObject>("YUCP.DampingConstraints/Rotation Damping Constraint");
        }

        private static bool ValidateTarget(VRCAvatarDescriptor descriptor, RotationDampingConstraintData component, RotationDampingConstraintData.Settings settings)
        {
            if (settings.targetObject == null)
            {
                Debug.LogError("[YUCP Rotation Damping Constraint] Target object reference is missing.", component);
                return false;
            }

            if (!settings.targetObject.transform.IsChildOf(descriptor.transform))
            {
                Debug.LogError("[YUCP Rotation Damping Constraint] Target object must be inside the avatar descriptor hierarchy.", component);
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
                    Debug.LogWarning($"[YUCP Rotation Damping Constraint] Group \"{group.Key}\" contains components with mismatched settings. They will be split into {signatures.Count} separate setups.");
                }
            }
        }

        private static bool BuildGroup(VRCAvatarDescriptor descriptor, GameObject prefab, GroupKey key, List<GroupMember> members)
        {
            var targets = members.Select(m => m.Settings.targetObject).ToArray();
            if (targets.Length == 0)
            {
                return true;
            }

            if (targets.Distinct().Count() != targets.Length)
            {
                var groupLabel = key.IsIsolated ? "Isolated group" : $"Group \"{key.ConstraintGroupId}\"";
                Debug.LogError($"[YUCP Rotation Damping Constraint] {groupLabel} references the same object multiple times. Please ensure each component targets a unique object.");
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary("Build failed");
                }
                return false;
            }

            try
            {
                foreach (var member in members)
                {
                    var settings = member.Settings;
                    InstallConstraint(descriptor, prefab, settings);
                }

                var summaryLabel = key.IsIsolated
                    ? "Rotation Damping Constraint (isolated)"
                    : $"Rotation Damping Constraint group \"{key.ConstraintGroupId}\"";
                var summary = $"{summaryLabel} built ({targets.Length} object{(targets.Length == 1 ? string.Empty : "s")})";
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary(summary);
                }

                if (key.VerboseLogging)
                {
                    Debug.Log($"[YUCP Rotation Damping Constraint] Generated group \"{key.ConstraintGroupId}\" with {targets.Length} object(s).");
                }

                if (key.IncludeCredits)
                {
                    Debug.Log("[YUCP Rotation Damping Constraint] Built using VRLabs Damping Constraints (MIT). Please credit VRLabs when sharing your avatar.");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Rotation Damping Constraint] Failed to generate constraint system: {ex.Message}");
                Debug.LogException(ex);
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary("Build failed");
                }
                return false;
            }
        }

        private static void InstallConstraint(VRCAvatarDescriptor descriptor, GameObject prefab, RotationDampingConstraintData.Settings settings)
        {
            var rootObject = descriptor.gameObject;
            var constraintSystem = UnityEngine.Object.Instantiate(prefab, rootObject.transform);
            constraintSystem.name = constraintSystem.name.Replace("(Clone)", "");

            var container = constraintSystem.transform.Find("Container");
            if (container == null)
            {
                Debug.LogError("[YUCP Rotation Damping Constraint] Prefab missing Container object.");
                return;
            }

            var rotationTarget = constraintSystem.transform.Find("Rotation Target");
            if (rotationTarget == null)
            {
                Debug.LogError("[YUCP Rotation Damping Constraint] Prefab missing Rotation Target object.");
                return;
            }

            if (settings.targetTransform != null)
            {
                var oldPath = AnimationUtility.CalculateTransformPath(rotationTarget.transform, descriptor.transform);
                rotationTarget.parent = settings.targetTransform.parent;
                rotationTarget.localPosition = settings.targetTransform.localPosition;
                rotationTarget.localRotation = settings.targetTransform.localRotation;
                rotationTarget.localScale = settings.targetTransform.localScale;
                var newPath = AnimationUtility.CalculateTransformPath(rotationTarget.transform, descriptor.transform);

                var allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                    .Where(x => x.animatorController != null)
                    .SelectMany(x => x.animatorController.animationClips)
                    .ToArray();

                CustomObjectSyncCreator.RenameClipPaths(allClips, false, oldPath, newPath);
            }

            var constraint = container.GetComponent<VRC.SDK3.Dynamics.Constraint.Components.VRCRotationConstraint>();
            if (constraint != null && constraint.Sources.Count >= 2)
            {
                var source = constraint.Sources[1];
                source.Weight = settings.dampingWeight;
                constraint.Sources[1] = source;
            }

            if (settings.targetObject != null)
            {
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
            public GroupMember(RotationDampingConstraintData component, RotationDampingConstraintData.Settings settings, string groupId, bool isIsolated)
            {
                Component = component;
                Settings = settings;
                GroupId = groupId;
                IsIsolated = isIsolated;
            }

            public RotationDampingConstraintData Component { get; }
            public RotationDampingConstraintData.Settings Settings { get; }
            public string GroupId { get; }
            public bool IsIsolated { get; }
        }

        private readonly struct GroupSettingsSignature : IEquatable<GroupSettingsSignature>
        {
            public GroupSettingsSignature(RotationDampingConstraintData.Settings settings)
            {
                DampingWeight = settings.dampingWeight;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            private float DampingWeight { get; }
            private bool VerboseLogging { get; }
            private bool IncludeCredits { get; }

            public bool Equals(GroupSettingsSignature other)
            {
                return Mathf.Approximately(DampingWeight, other.DampingWeight) &&
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
                    var hashCode = DampingWeight.GetHashCode();
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private readonly struct GroupKey : IEquatable<GroupKey>
        {
            public GroupKey(RotationDampingConstraintData.Settings settings, string groupId, bool isIsolated)
            {
                ConstraintGroupId = groupId;
                IsIsolated = isIsolated;
                DampingWeight = settings.dampingWeight;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            public string ConstraintGroupId { get; }
            public bool IsIsolated { get; }
            public float DampingWeight { get; }
            public bool VerboseLogging { get; }
            public bool IncludeCredits { get; }

            public bool Equals(GroupKey other)
            {
                return ConstraintGroupId == other.ConstraintGroupId &&
                       IsIsolated == other.IsIsolated &&
                       Mathf.Approximately(DampingWeight, other.DampingWeight) &&
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
                    var hashCode = ConstraintGroupId != null ? ConstraintGroupId.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ IsIsolated.GetHashCode();
                    hashCode = (hashCode * 397) ^ DampingWeight.GetHashCode();
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private static string GetIsolatedGroupId(RotationDampingConstraintData.Settings settings, VRCAvatarDescriptor descriptor)
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

