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
    public class ContactTrackerProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 204;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return true;
            }

            var components = avatarRoot.GetComponentsInChildren<ContactTrackerData>(true);
            if (components.Length == 0)
            {
                return true;
            }

            var prefab = LoadPrefab();
            if (prefab == null)
            {
                Debug.LogError("[YUCP Contact Tracker] Prefab not found at Resources/YUCP.ContactTracker/Contact Tracker.prefab.");
                return false;
            }

            var fxController = LoadFXController();
            if (fxController == null)
            {
                Debug.LogError("[YUCP Contact Tracker] FX Controller not found. Please ensure the controller is in the package.");
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
                    ? settings.trackerGroupId
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
            return UnityEngine.Resources.Load<GameObject>("YUCP.ContactTracker/Contact Tracker");
        }

        private static AnimatorController LoadFXController()
        {
            return UnityEngine.Resources.Load<AnimatorController>("YUCP.ContactTracker/Contact Tracker FX");
        }

        private static bool ValidateTarget(VRCAvatarDescriptor descriptor, ContactTrackerData component, ContactTrackerData.Settings settings)
        {
            if (settings.targetObject == null)
            {
                Debug.LogError("[YUCP Contact Tracker] Target object reference is missing.", component);
                return false;
            }

            if (!settings.targetObject.transform.IsChildOf(descriptor.transform))
            {
                Debug.LogError("[YUCP Contact Tracker] Target object must be inside the avatar descriptor hierarchy.", component);
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
                    Debug.LogWarning($"[YUCP Contact Tracker] Group \"{group.Key}\" contains components with mismatched settings. They will be split into {signatures.Count} separate setups.");
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
                var groupLabel = key.IsIsolated ? "Isolated group" : $"Group \"{key.TrackerGroupId}\"";
                Debug.LogError($"[YUCP Contact Tracker] {groupLabel} references the same object multiple times. Please ensure each component targets a unique object.");
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
                    ? "Contact Tracker (isolated)"
                    : $"Contact Tracker group \"{key.TrackerGroupId}\"";
                var summary = $"{summaryLabel} built ({targets.Length} object{(targets.Length == 1 ? string.Empty : "s")})";
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary(summary);
                }

                if (key.VerboseLogging)
                {
                    Debug.Log($"[YUCP Contact Tracker] Generated group \"{key.TrackerGroupId}\" with {targets.Length} object(s).");
                }

                if (key.IncludeCredits)
                {
                    Debug.Log("[YUCP Contact Tracker] Built using VRLabs Contact Tracker (MIT). Please credit VRLabs when sharing your avatar.");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Contact Tracker] Failed to generate contact tracker system: {ex.Message}");
                Debug.LogException(ex);
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary("Build failed");
                }
                return false;
            }
        }

        private static void InstallSystem(VRCAvatarDescriptor descriptor, GameObject prefab, ContactTrackerData.Settings settings)
        {
            var rootObject = descriptor.gameObject;
            var trackerSystem = UnityEngine.Object.Instantiate(prefab, rootObject.transform);
            trackerSystem.name = trackerSystem.name.Replace("(Clone)", "");

            var trackingPoints = trackerSystem.transform.Find("Tracking Points");
            if (trackingPoints != null && settings.collisionTags != null && settings.collisionTags.Length >= 6)
            {
                string[] contactNames = { "X+", "X-", "Y+", "Y-", "Z+", "Z-" };
                for (int i = 0; i < 6; i++)
                {
                    var contactObj = trackingPoints.Find(contactNames[i]);
                    if (contactObj != null)
                    {
                        var contactReceiver = contactObj.GetComponent<VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver>();
                        if (contactReceiver != null && !string.IsNullOrEmpty(settings.collisionTags[i]))
                        {
                            var tags = new System.Collections.Generic.List<string> { settings.collisionTags[i] };
                            contactReceiver.collisionTags = tags;
                        }
                    }
                }
            }

            var trackerTarget = trackerSystem.transform.Find("Tracker Target");
            if (trackerTarget != null && settings.trackerTarget != null)
            {
                var oldPath = AnimationUtility.CalculateTransformPath(settings.trackerTarget.transform, descriptor.transform);
                trackerTarget.parent = settings.trackerTarget.parent;
                trackerTarget.localPosition = settings.trackerTarget.localPosition;
                trackerTarget.localRotation = settings.trackerTarget.localRotation;
                trackerTarget.localScale = settings.trackerTarget.localScale;
                var newPath = AnimationUtility.CalculateTransformPath(trackerTarget.transform, descriptor.transform);

                var allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                    .Where(x => x.animatorController != null)
                    .SelectMany(x => x.animatorController.animationClips)
                    .ToArray();

                CustomObjectSyncCreator.RenameClipPaths(allClips, false, oldPath, newPath);
            }

            if (settings.targetObject != null)
            {
                var container = trackerSystem.transform.Find("Container");
                if (container == null)
                {
                    Debug.LogError("[YUCP Contact Tracker] Prefab missing Container object.");
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

            if (settings.sizeParameter != 0f)
            {
                var fxLayer = descriptor.baseAnimationLayers
                    .FirstOrDefault(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX);
                var fxController = fxLayer.animatorController as AnimatorController;
                if (fxController != null)
                {
                    var param = fxController.parameters.FirstOrDefault(p => p.name == "ContactTracker/Size");
                    if (param != null)
                    {
                        var clips = fxController.animationClips;
                        foreach (var clip in clips)
                        {
                            if (clip != null)
                            {
                                var bindings = AnimationUtility.GetCurveBindings(clip);
                                foreach (var binding in bindings)
                                {
                                    if (binding.path.Contains("Contact Tracker") && binding.propertyName.Contains("Size"))
                                    {
                                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                                        if (curve != null && curve.keys.Length > 0)
                                        {
                                            for (int i = 0; i < curve.keys.Length; i++)
                                            {
                                                var key = curve.keys[i];
                                                key.value = settings.sizeParameter;
                                                curve.MoveKey(i, key);
                                            }
                                            AnimationUtility.SetEditorCurve(clip, binding, curve);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private readonly struct GroupMember
        {
            public GroupMember(ContactTrackerData component, ContactTrackerData.Settings settings, string groupId, bool isIsolated)
            {
                Component = component;
                Settings = settings;
                GroupId = groupId;
                IsIsolated = isIsolated;
            }

            public ContactTrackerData Component { get; }
            public ContactTrackerData.Settings Settings { get; }
            public string GroupId { get; }
            public bool IsIsolated { get; }
        }

        private readonly struct GroupSettingsSignature : IEquatable<GroupSettingsSignature>
        {
            public GroupSettingsSignature(ContactTrackerData.Settings settings)
            {
                MenuLocation = settings.menuLocation;
                CollisionTags = settings.collisionTags != null ? (string[])settings.collisionTags.Clone() : new string[6];
                SizeParameter = settings.sizeParameter;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            private string MenuLocation { get; }
            private string[] CollisionTags { get; }
            private float SizeParameter { get; }
            private bool VerboseLogging { get; }
            private bool IncludeCredits { get; }

            public bool Equals(GroupSettingsSignature other)
            {
                if (CollisionTags == null && other.CollisionTags == null)
                    return MenuLocation == other.MenuLocation &&
                           Mathf.Approximately(SizeParameter, other.SizeParameter) &&
                           VerboseLogging == other.VerboseLogging &&
                           IncludeCredits == other.IncludeCredits;
                
                if (CollisionTags == null || other.CollisionTags == null)
                    return false;
                
                if (CollisionTags.Length != other.CollisionTags.Length)
                    return false;
                
                for (int i = 0; i < CollisionTags.Length; i++)
                {
                    if (CollisionTags[i] != other.CollisionTags[i])
                        return false;
                }
                
                return MenuLocation == other.MenuLocation &&
                       Mathf.Approximately(SizeParameter, other.SizeParameter) &&
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
                    if (CollisionTags != null)
                    {
                        foreach (var tag in CollisionTags)
                        {
                            hashCode = (hashCode * 397) ^ (tag != null ? tag.GetHashCode() : 0);
                        }
                    }
                    hashCode = (hashCode * 397) ^ SizeParameter.GetHashCode();
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private readonly struct GroupKey : IEquatable<GroupKey>
        {
            public GroupKey(ContactTrackerData.Settings settings, string groupId, bool isIsolated)
            {
                TrackerGroupId = groupId;
                IsIsolated = isIsolated;
                MenuLocation = settings.menuLocation;
                CollisionTags = settings.collisionTags != null ? (string[])settings.collisionTags.Clone() : new string[6];
                SizeParameter = settings.sizeParameter;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            public string TrackerGroupId { get; }
            public bool IsIsolated { get; }
            public string MenuLocation { get; }
            public string[] CollisionTags { get; }
            public float SizeParameter { get; }
            public bool VerboseLogging { get; }
            public bool IncludeCredits { get; }

            public bool Equals(GroupKey other)
            {
                if (CollisionTags == null && other.CollisionTags == null)
                    return TrackerGroupId == other.TrackerGroupId &&
                           IsIsolated == other.IsIsolated &&
                           MenuLocation == other.MenuLocation &&
                           Mathf.Approximately(SizeParameter, other.SizeParameter) &&
                           VerboseLogging == other.VerboseLogging &&
                           IncludeCredits == other.IncludeCredits;
                
                if (CollisionTags == null || other.CollisionTags == null)
                    return false;
                
                if (CollisionTags.Length != other.CollisionTags.Length)
                    return false;
                
                for (int i = 0; i < CollisionTags.Length; i++)
                {
                    if (CollisionTags[i] != other.CollisionTags[i])
                        return false;
                }
                
                return TrackerGroupId == other.TrackerGroupId &&
                       IsIsolated == other.IsIsolated &&
                       MenuLocation == other.MenuLocation &&
                       Mathf.Approximately(SizeParameter, other.SizeParameter) &&
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
                    var hashCode = TrackerGroupId != null ? TrackerGroupId.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ IsIsolated.GetHashCode();
                    hashCode = (hashCode * 397) ^ (MenuLocation != null ? MenuLocation.GetHashCode() : 0);
                    if (CollisionTags != null)
                    {
                        foreach (var tag in CollisionTags)
                        {
                            hashCode = (hashCode * 397) ^ (tag != null ? tag.GetHashCode() : 0);
                        }
                    }
                    hashCode = (hashCode * 397) ^ SizeParameter.GetHashCode();
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private static string GetIsolatedGroupId(ContactTrackerData.Settings settings, VRCAvatarDescriptor descriptor)
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

