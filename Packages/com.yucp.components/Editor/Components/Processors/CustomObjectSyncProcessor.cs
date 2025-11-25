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
    public class CustomObjectSyncProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 200;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return true;
            }

            var components = avatarRoot.GetComponentsInChildren<CustomObjectSyncData>(true);
            if (components.Length == 0)
            {
                return true;
            }

            var prefab = LoadPrefab();
            if (prefab == null)
            {
                Debug.LogError("[YUCP Custom Object Sync] Prefab not found at Resources/YUCP.CustomObjectSync/Custom Object Sync.prefab.");
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
                    ? settings.syncGroupId
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
            return UnityEngine.Resources.Load<GameObject>("YUCP.CustomObjectSync/Custom Object Sync");
        }

        private static bool ValidateTarget(VRCAvatarDescriptor descriptor, CustomObjectSyncData component, CustomObjectSyncData.Settings settings)
        {
            if (settings.targetObject == null)
            {
                Debug.LogError("[YUCP Custom Object Sync] Target object reference is missing.", component);
                return false;
            }

            if (!settings.targetObject.transform.IsChildOf(descriptor.transform))
            {
                Debug.LogError("[YUCP Custom Object Sync] Target object must be inside the avatar descriptor hierarchy.", component);
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
                Debug.LogWarning("[YUCP Custom Object Sync] Expressions menu is not assigned on this avatar. Falling back to root menu for the generated toggle.");
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
                    Debug.LogWarning($"[YUCP Custom Object Sync] Group \"{group.Key}\" contains components with mismatched settings. They will be split into {signatures.Count} separate sync rigs.");
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
                var groupLabel = key.IsIsolated ? "Isolated group" : $"Group \"{key.SyncGroupId}\"";
                Debug.LogError($"[YUCP Custom Object Sync] {groupLabel} references the same object multiple times. Please ensure each component targets a unique object.");
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary("Build failed");
                }
                return false;
            }

            ConfigureCreator(descriptor, prefab, key, targets);

            var creator = CustomObjectSyncCreator.instance;
            try
            {
                creator.Generate();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Custom Object Sync] Failed to generate sync system for group \"{key.SyncGroupId}\": {ex.Message}");
                Debug.LogException(ex);
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary("Build failed");
                }
                return false;
            }
            finally
            {
                creator.silentMode = false;
            }

            var summaryLabel = key.IsIsolated
                ? $"{(key.QuickSync ? "Quick" : "Bit")} Sync (isolated)"
                : $"{(key.QuickSync ? "Quick" : "Bit")} Sync group \"{key.SyncGroupId}\"";
            var summary = $"{summaryLabel} built ({targets.Length} object{(targets.Length == 1 ? string.Empty : "s")})";
            foreach (var member in members)
            {
                member.Component.SetBuildSummary(summary);
            }

            if (key.VerboseLogging)
            {
                Debug.Log($"[YUCP Custom Object Sync] Generated {(key.QuickSync ? "Quick" : "Bit")} Sync group \"{key.SyncGroupId}\" with {targets.Length} object(s).");
            }

            if (key.IncludeCredits)
            {
                Debug.Log("[YUCP Custom Object Sync] Built using VRLabs Custom Object Sync (MIT). Please credit VRLabs when sharing your avatar.");
            }

            return true;
        }

        private static void ConfigureCreator(VRCAvatarDescriptor descriptor, GameObject prefab, GroupKey key, GameObject[] targets)
        {
            var creator = CustomObjectSyncCreator.instance;
            creator.resourcePrefab = prefab;
            creator.silentMode = true;
            creator.syncObjects = targets;
            creator.useMultipleObjects = targets.Length > 1;

            creator.bitCount = key.BitCount;
            creator.maxRadius = key.MaxRadius;
            creator.positionPrecision = key.PositionPrecision;
            creator.rotationPrecision = key.RotationPrecision;
            creator.rotationEnabled = key.RotationEnabled;

            creator.quickSync = key.QuickSync;
            creator.centeredOnAvatar = key.QuickSync || key.ReferenceFrame == CustomObjectSyncData.ReferenceFrame.AvatarCentered;
            creator.addDampeningConstraint = key.AddDampingConstraint;
            creator.dampingConstraintValue = Mathf.Clamp(key.DampingConstraintValue, 0.01f, 1f);
            creator.addLocalDebugView = key.AddLocalDebugView;
            creator.writeDefaults = key.WriteDefaults;
            creator.menuLocation = ResolveMenuLocation(descriptor, key.MenuLocation);
        }

        private readonly struct GroupMember
        {
            public GroupMember(CustomObjectSyncData component, CustomObjectSyncData.Settings settings, string groupId, bool isIsolated)
            {
                Component = component;
                Settings = settings;
                GroupId = groupId;
                IsIsolated = isIsolated;
            }

            public CustomObjectSyncData Component { get; }
            public CustomObjectSyncData.Settings Settings { get; }
            public string GroupId { get; }
            public bool IsIsolated { get; }
        }

        private readonly struct GroupSettingsSignature : IEquatable<GroupSettingsSignature>
        {
            public GroupSettingsSignature(CustomObjectSyncData.Settings settings)
            {
                QuickSync = settings.quickSync;
                ReferenceFrame = settings.referenceFrame;
                MaxRadius = settings.maxRadius;
                PositionPrecision = settings.positionPrecision;
                RotationPrecision = settings.rotationPrecision;
                BitCount = settings.bitCount;
                RotationEnabled = settings.rotationEnabled;
                AddDamping = settings.addDampingConstraint;
                DampingValue = settings.dampingConstraintValue;
                AddDebugView = settings.addLocalDebugView;
                WriteDefaults = settings.writeDefaults;
                MenuLocation = settings.menuLocation;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            private bool QuickSync { get; }
            private CustomObjectSyncData.ReferenceFrame ReferenceFrame { get; }
            private int MaxRadius { get; }
            private int PositionPrecision { get; }
            private int RotationPrecision { get; }
            private int BitCount { get; }
            private bool RotationEnabled { get; }
            private bool AddDamping { get; }
            private float DampingValue { get; }
            private bool AddDebugView { get; }
            private bool WriteDefaults { get; }
            private string MenuLocation { get; }
            private bool VerboseLogging { get; }
            private bool IncludeCredits { get; }

            public bool Equals(GroupSettingsSignature other)
            {
                return QuickSync == other.QuickSync &&
                       ReferenceFrame == other.ReferenceFrame &&
                       MaxRadius == other.MaxRadius &&
                       PositionPrecision == other.PositionPrecision &&
                       RotationPrecision == other.RotationPrecision &&
                       BitCount == other.BitCount &&
                       RotationEnabled == other.RotationEnabled &&
                       AddDamping == other.AddDamping &&
                       Mathf.Approximately(DampingValue, other.DampingValue) &&
                       AddDebugView == other.AddDebugView &&
                       WriteDefaults == other.WriteDefaults &&
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
                    var hashCode = QuickSync.GetHashCode();
                    hashCode = (hashCode * 397) ^ ReferenceFrame.GetHashCode();
                    hashCode = (hashCode * 397) ^ MaxRadius;
                    hashCode = (hashCode * 397) ^ PositionPrecision;
                    hashCode = (hashCode * 397) ^ RotationPrecision;
                    hashCode = (hashCode * 397) ^ BitCount;
                    hashCode = (hashCode * 397) ^ RotationEnabled.GetHashCode();
                    hashCode = (hashCode * 397) ^ AddDamping.GetHashCode();
                    hashCode = (hashCode * 397) ^ DampingValue.GetHashCode();
                    hashCode = (hashCode * 397) ^ AddDebugView.GetHashCode();
                    hashCode = (hashCode * 397) ^ WriteDefaults.GetHashCode();
                    hashCode = (hashCode * 397) ^ (MenuLocation != null ? MenuLocation.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private readonly struct GroupKey : IEquatable<GroupKey>
        {
            public GroupKey(CustomObjectSyncData.Settings settings, string groupId, bool isIsolated)
            {
                SyncGroupId = groupId;
                IsIsolated = isIsolated;
                QuickSync = settings.quickSync;
                ReferenceFrame = settings.referenceFrame;
                MaxRadius = settings.maxRadius;
                PositionPrecision = settings.positionPrecision;
                RotationPrecision = settings.rotationPrecision;
                BitCount = settings.bitCount;
                RotationEnabled = settings.rotationEnabled;
                AddDampingConstraint = settings.addDampingConstraint;
                DampingConstraintValue = settings.dampingConstraintValue;
                AddLocalDebugView = settings.addLocalDebugView;
                WriteDefaults = settings.writeDefaults;
                MenuLocation = settings.menuLocation;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            public string SyncGroupId { get; }
            public bool IsIsolated { get; }
            public bool QuickSync { get; }
            public CustomObjectSyncData.ReferenceFrame ReferenceFrame { get; }
            public int MaxRadius { get; }
            public int PositionPrecision { get; }
            public int RotationPrecision { get; }
            public int BitCount { get; }
            public bool RotationEnabled { get; }
            public bool AddDampingConstraint { get; }
            public float DampingConstraintValue { get; }
            public bool AddLocalDebugView { get; }
            public bool WriteDefaults { get; }
            public string MenuLocation { get; }
            public bool VerboseLogging { get; }
            public bool IncludeCredits { get; }

            public bool Equals(GroupKey other)
            {
                return SyncGroupId == other.SyncGroupId &&
                       IsIsolated == other.IsIsolated &&
                       QuickSync == other.QuickSync &&
                       ReferenceFrame == other.ReferenceFrame &&
                       MaxRadius == other.MaxRadius &&
                       PositionPrecision == other.PositionPrecision &&
                       RotationPrecision == other.RotationPrecision &&
                       BitCount == other.BitCount &&
                       RotationEnabled == other.RotationEnabled &&
                       AddDampingConstraint == other.AddDampingConstraint &&
                       Mathf.Approximately(DampingConstraintValue, other.DampingConstraintValue) &&
                       AddLocalDebugView == other.AddLocalDebugView &&
                       WriteDefaults == other.WriteDefaults &&
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
                    var hashCode = SyncGroupId != null ? SyncGroupId.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ IsIsolated.GetHashCode();
                    hashCode = (hashCode * 397) ^ QuickSync.GetHashCode();
                    hashCode = (hashCode * 397) ^ ReferenceFrame.GetHashCode();
                    hashCode = (hashCode * 397) ^ MaxRadius;
                    hashCode = (hashCode * 397) ^ PositionPrecision;
                    hashCode = (hashCode * 397) ^ RotationPrecision;
                    hashCode = (hashCode * 397) ^ BitCount;
                    hashCode = (hashCode * 397) ^ RotationEnabled.GetHashCode();
                    hashCode = (hashCode * 397) ^ AddDampingConstraint.GetHashCode();
                    hashCode = (hashCode * 397) ^ DampingConstraintValue.GetHashCode();
                    hashCode = (hashCode * 397) ^ AddLocalDebugView.GetHashCode();
                    hashCode = (hashCode * 397) ^ WriteDefaults.GetHashCode();
                    hashCode = (hashCode * 397) ^ (MenuLocation != null ? MenuLocation.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private static string GetIsolatedGroupId(CustomObjectSyncData.Settings settings, VRCAvatarDescriptor descriptor)
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

