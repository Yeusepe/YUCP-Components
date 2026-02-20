using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using VRLabs.CustomObjectSyncCreator;

namespace YUCP.Components.Editor
{
    public class RaycastPrefabProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 206;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return true;
            }

            var components = avatarRoot.GetComponentsInChildren<RaycastPrefabData>(true);
            if (components.Length == 0)
            {
                return true;
            }

            var prefab = LoadPrefab();
            if (prefab == null)
            {
                Debug.LogError("[YUCP Raycast Prefab] Prefab not found at Resources/YUCP.RaycastPrefab/Raycast.prefab.");
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
                    ? settings.raycastGroupId
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
            return UnityEngine.Resources.Load<GameObject>("YUCP.RaycastPrefab/Raycast");
        }

        private static bool ValidateTarget(VRCAvatarDescriptor descriptor, RaycastPrefabData component, RaycastPrefabData.Settings settings)
        {
            if (settings.appliedObject == null)
            {
                Debug.LogError("[YUCP Raycast Prefab] Target object reference is missing.", component);
                return false;
            }

            if (!settings.appliedObject.transform.IsChildOf(descriptor.transform))
            {
                Debug.LogError("[YUCP Raycast Prefab] Target object must be inside the avatar descriptor hierarchy.", component);
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
                    Debug.LogWarning($"[YUCP Raycast Prefab] Group \"{group.Key}\" contains components with mismatched settings. They will be split into {signatures.Count} separate setups.");
                }
            }
        }

        private static bool BuildGroup(VRCAvatarDescriptor descriptor, GameObject prefab, GroupKey key, List<GroupMember> members)
        {
            var targets = members.Select(m => m.Settings.appliedObject).ToArray();
            if (targets.Length == 0)
            {
                return true;
            }

            if (targets.Distinct().Count() != targets.Length)
            {
                var groupLabel = key.IsIsolated ? "Isolated group" : $"Group \"{key.RaycastGroupId}\"";
                Debug.LogError($"[YUCP Raycast Prefab] {groupLabel} references the same object multiple times. Please ensure each component targets a unique object.");
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
                    InstallSystem(descriptor, prefab, settings);
                }

                var summaryLabel = key.IsIsolated
                    ? "Raycast Prefab (isolated)"
                    : $"Raycast Prefab group \"{key.RaycastGroupId}\"";
                var summary = $"{summaryLabel} built ({targets.Length} object{(targets.Length == 1 ? string.Empty : "s")})";
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary(summary);
                }

                if (key.VerboseLogging)
                {
                    Debug.Log($"[YUCP Raycast Prefab] Generated group \"{key.RaycastGroupId}\" with {targets.Length} object(s).");
                }

                if (key.IncludeCredits)
                {
                    Debug.Log("[YUCP Raycast Prefab] Built using VRLabs Raycast Prefab (MIT). Please credit VRLabs when sharing your avatar.");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Raycast Prefab] Failed to generate raycast prefab system: {ex.Message}");
                Debug.LogException(ex);
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary("Build failed");
                }
                return false;
            }
        }

        private static Type FindGrounderIKType()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("RootMotion.FinalIK.GrounderIK"))
                .FirstOrDefault(t => t != null);
        }

        private static void InstallSystem(VRCAvatarDescriptor descriptor, GameObject prefab, RaycastPrefabData.Settings settings)
        {
            var rootObject = descriptor.gameObject;
            var raycastSystem = UnityEngine.Object.Instantiate(prefab, rootObject.transform);
            raycastSystem.name = raycastSystem.name.Replace("(Clone)", "");

            var castingTarget = raycastSystem.transform.Find("Casting Target");
            if (castingTarget != null && settings.raycastOrigin != null)
            {
                castingTarget.parent = settings.raycastOrigin.parent;
                castingTarget.localPosition = settings.raycastOrigin.localPosition;
                castingTarget.localRotation = settings.raycastOrigin.localRotation;
                castingTarget.localScale = settings.raycastOrigin.localScale;
            }

            var grounder = raycastSystem.transform.Find("IK/Grounder");
            if (grounder != null)
            {
                var grounderIKType = FindGrounderIKType();
                if (grounderIKType != null)
                {
                    var grounderIK = grounder.GetComponent(grounderIKType);
                    if (grounderIK != null)
                    {
                        var layersField = grounderIKType.GetField("layers");
                        if (layersField != null)
                        {
                            layersField.SetValue(grounderIK, (LayerMask)settings.grounderLayers);
                        }

                        var solverProperty = grounderIKType.GetProperty("solver");
                        if (solverProperty != null)
                        {
                            var solver = solverProperty.GetValue(grounderIK);
                            if (solver != null)
                            {
                                var maxStepField = solver.GetType().GetField("maxStep");
                                if (maxStepField != null)
                                {
                                    maxStepField.SetValue(solver, settings.raycastDistance);
                                }
                            }
                        }
                    }
                }
                else if (settings.verboseLogging)
                {
                    Debug.LogWarning("[YUCP Raycast Prefab] GrounderIK type not found in any loaded assembly. Grounder settings will use prefab defaults.");
                }
            }

            if (settings.appliedObject != null)
            {
                var container = raycastSystem.transform.Find("Container");
                if (container == null)
                {
                    Debug.LogError("[YUCP Raycast Prefab] Prefab missing Container object.");
                    return;
                }

                var oldPath = AnimationUtility.CalculateTransformPath(settings.appliedObject.transform, descriptor.transform);
                settings.appliedObject.transform.parent = container;
                var newPath = AnimationUtility.CalculateTransformPath(settings.appliedObject.transform, descriptor.transform);

                var allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                    .Where(x => x.animatorController != null)
                    .SelectMany(x => x.animatorController.animationClips)
                    .ToArray();

                CustomObjectSyncCreator.RenameClipPaths(allClips, false, oldPath, newPath);
            }
        }

        private readonly struct GroupMember
        {
            public GroupMember(RaycastPrefabData component, RaycastPrefabData.Settings settings, string groupId, bool isIsolated)
            {
                Component = component;
                Settings = settings;
                GroupId = groupId;
                IsIsolated = isIsolated;
            }

            public RaycastPrefabData Component { get; }
            public RaycastPrefabData.Settings Settings { get; }
            public string GroupId { get; }
            public bool IsIsolated { get; }
        }

        private readonly struct GroupSettingsSignature : IEquatable<GroupSettingsSignature>
        {
            public GroupSettingsSignature(RaycastPrefabData.Settings settings)
            {
                GenerateMenu = settings.generateMenu;
                MenuLocation = settings.menuLocation;
                GrounderLayers = settings.grounderLayers;
                RaycastDistance = settings.raycastDistance;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            private bool GenerateMenu { get; }
            private string MenuLocation { get; }
            private LayerMask GrounderLayers { get; }
            private float RaycastDistance { get; }
            private bool VerboseLogging { get; }
            private bool IncludeCredits { get; }

            public bool Equals(GroupSettingsSignature other)
            {
                return GenerateMenu == other.GenerateMenu &&
                       MenuLocation == other.MenuLocation &&
                       GrounderLayers == other.GrounderLayers &&
                       Mathf.Approximately(RaycastDistance, other.RaycastDistance) &&
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
                    var hashCode = GenerateMenu.GetHashCode();
                    hashCode = (hashCode * 397) ^ (MenuLocation != null ? MenuLocation.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ GrounderLayers.GetHashCode();
                    hashCode = (hashCode * 397) ^ RaycastDistance.GetHashCode();
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private readonly struct GroupKey : IEquatable<GroupKey>
        {
            public GroupKey(RaycastPrefabData.Settings settings, string groupId, bool isIsolated)
            {
                RaycastGroupId = groupId;
                IsIsolated = isIsolated;
                GenerateMenu = settings.generateMenu;
                MenuLocation = settings.menuLocation;
                GrounderLayers = settings.grounderLayers;
                RaycastDistance = settings.raycastDistance;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            public string RaycastGroupId { get; }
            public bool IsIsolated { get; }
            public bool GenerateMenu { get; }
            public string MenuLocation { get; }
            public LayerMask GrounderLayers { get; }
            public float RaycastDistance { get; }
            public bool VerboseLogging { get; }
            public bool IncludeCredits { get; }

            public bool Equals(GroupKey other)
            {
                return RaycastGroupId == other.RaycastGroupId &&
                       IsIsolated == other.IsIsolated &&
                       GenerateMenu == other.GenerateMenu &&
                       MenuLocation == other.MenuLocation &&
                       GrounderLayers == other.GrounderLayers &&
                       Mathf.Approximately(RaycastDistance, other.RaycastDistance) &&
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
                    var hashCode = RaycastGroupId != null ? RaycastGroupId.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ IsIsolated.GetHashCode();
                    hashCode = (hashCode * 397) ^ GenerateMenu.GetHashCode();
                    hashCode = (hashCode * 397) ^ (MenuLocation != null ? MenuLocation.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ GrounderLayers.GetHashCode();
                    hashCode = (hashCode * 397) ^ RaycastDistance.GetHashCode();
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private static string GetIsolatedGroupId(RaycastPrefabData.Settings settings, VRCAvatarDescriptor descriptor)
        {
            if (settings.appliedObject == null || descriptor == null)
            {
                return $"__Isolated__/{Guid.NewGuid()}";
            }

            string path = AnimationUtility.CalculateTransformPath(settings.appliedObject.transform, descriptor.transform);
            return $"__Isolated__/{path}";
        }
    }
}

