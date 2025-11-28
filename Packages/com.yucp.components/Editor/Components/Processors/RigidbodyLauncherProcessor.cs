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
    public class RigidbodyLauncherProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 207;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return true;
            }

            var components = avatarRoot.GetComponentsInChildren<RigidbodyLauncherData>(true);
            if (components.Length == 0)
            {
                return true;
            }

            var prefab = LoadPrefab();
            if (prefab == null)
            {
                Debug.LogError("[YUCP Rigidbody Launcher] Prefab not found at Resources/YUCP.RigidbodyLauncher/Rigidbody Launcher.prefab.");
                return false;
            }

            var fxController = LoadFXController();
            if (fxController == null)
            {
                Debug.LogError("[YUCP Rigidbody Launcher] FX Controller not found. Please ensure the controller is in the package.");
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
                    ? settings.launcherGroupId
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
            return UnityEngine.Resources.Load<GameObject>("YUCP.RigidbodyLauncher/Rigidbody Launcher");
        }

        private static AnimatorController LoadFXController()
        {
            return UnityEngine.Resources.Load<AnimatorController>("YUCP.RigidbodyLauncher/Rigidbody Launcher FX");
        }

        private static bool ValidateTarget(VRCAvatarDescriptor descriptor, RigidbodyLauncherData component, RigidbodyLauncherData.Settings settings)
        {
            if (settings.targetObject == null)
            {
                Debug.LogError("[YUCP Rigidbody Launcher] Target object reference is missing.", component);
                return false;
            }

            if (!settings.targetObject.transform.IsChildOf(descriptor.transform))
            {
                Debug.LogError("[YUCP Rigidbody Launcher] Target object must be inside the avatar descriptor hierarchy.", component);
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
                    Debug.LogWarning($"[YUCP Rigidbody Launcher] Group \"{group.Key}\" contains components with mismatched settings. They will be split into {signatures.Count} separate setups.");
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
                var groupLabel = key.IsIsolated ? "Isolated group" : $"Group \"{key.LauncherGroupId}\"";
                Debug.LogError($"[YUCP Rigidbody Launcher] {groupLabel} references the same object multiple times. Please ensure each component targets a unique object.");
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
                var globalParamControl = members.Count > 0 ? members[0].Settings.globalParameterControl : string.Empty;
                if (!string.IsNullOrEmpty(menuLocation))
                {
                    var menu = VRCFuryHelper.GetMenuFromLocation(descriptor, menuLocation);
                    if (menu != null)
                    {
                        if (!string.IsNullOrEmpty(globalParamControl))
                        {
                            VRCFuryHelper.AddGlobalParamToVRCFury(descriptor, globalParamControl);
                        }
                        VRCFuryHelper.AddMenuToggle(menu, "Rigidbody Launcher Control", "RigidbodyLauncher/Control");
                    }
                }

                var summaryLabel = key.IsIsolated
                    ? "Rigidbody Launcher (isolated)"
                    : $"Rigidbody Launcher group \"{key.LauncherGroupId}\"";
                var summary = $"{summaryLabel} built ({targets.Length} object{(targets.Length == 1 ? string.Empty : "s")})";
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary(summary);
                }

                if (key.VerboseLogging)
                {
                    Debug.Log($"[YUCP Rigidbody Launcher] Generated group \"{key.LauncherGroupId}\" with {targets.Length} object(s).");
                }

                if (key.IncludeCredits)
                {
                    Debug.Log("[YUCP Rigidbody Launcher] Built using VRLabs Rigidbody Launcher (MIT). Please credit VRLabs when sharing your avatar.");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Rigidbody Launcher] Failed to generate rigidbody launcher system: {ex.Message}");
                Debug.LogException(ex);
                foreach (var member in members)
                {
                    member.Component.SetBuildSummary("Build failed");
                }
                return false;
            }
        }

        private static void InstallSystem(VRCAvatarDescriptor descriptor, GameObject prefab, RigidbodyLauncherData.Settings settings)
        {
            var rootObject = descriptor.gameObject;
            var launcherSystem = UnityEngine.Object.Instantiate(prefab, rootObject.transform);
            launcherSystem.name = launcherSystem.name.Replace("(Clone)", "");

            var launcherTarget = launcherSystem.transform.Find("Rigidbody Launcher Target");
            if (launcherTarget != null && settings.launcherTarget != null)
            {
                var oldPath = AnimationUtility.CalculateTransformPath(settings.launcherTarget.transform, descriptor.transform);
                launcherTarget.parent = settings.launcherTarget.parent;
                launcherTarget.localPosition = settings.launcherTarget.localPosition;
                launcherTarget.localRotation = settings.launcherTarget.localRotation;
                launcherTarget.localScale = settings.launcherTarget.localScale;
                var newPath = AnimationUtility.CalculateTransformPath(launcherTarget.transform, descriptor.transform);

                var allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                    .Where(x => x.animatorController != null)
                    .SelectMany(x => x.animatorController.animationClips)
                    .ToArray();

                CustomObjectSyncCreator.RenameClipPaths(allClips, false, oldPath, newPath);
            }

            var collision = launcherSystem.transform.Find("Kinematic Rigidbody/Collision");
            if (collision != null)
            {
                var joint = collision.GetComponent<ConfigurableJoint>();
                if (joint != null)
                {
                    var xDrive = joint.xDrive;
                    var yDrive = joint.yDrive;
                    var zDrive = joint.zDrive;
                    xDrive.maximumForce = settings.maximumForce;
                    yDrive.maximumForce = settings.maximumForce;
                    zDrive.maximumForce = settings.maximumForce;
                    joint.xDrive = xDrive;
                    joint.yDrive = yDrive;
                    joint.zDrive = zDrive;
                }

                if (settings.launchSpeed != -10f)
                {
                    var fxLayer = descriptor.baseAnimationLayers
                        .FirstOrDefault(x => x.type == VRCAvatarDescriptor.AnimLayerType.FX);
                    var fxController = fxLayer.animatorController as AnimatorController;
                    if (fxController != null)
                    {
                        var clips = fxController.animationClips;
                        foreach (var clip in clips)
                        {
                            if (clip != null && clip.name.Contains("Launcher Fire"))
                            {
                                var bindings = AnimationUtility.GetCurveBindings(clip);
                                foreach (var binding in bindings)
                                {
                                    if (binding.propertyName.Contains("Target Velocity"))
                                    {
                                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                                        if (curve != null && curve.keys.Length > 0)
                                        {
                                            for (int i = 0; i < curve.keys.Length; i++)
                                            {
                                                var key = curve.keys[i];
                                                key.value = settings.launchSpeed;
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

            var particleSystem = launcherSystem.GetComponentInChildren<ParticleSystem>();
            if (particleSystem != null)
            {
                var collisionModule = particleSystem.collision;
                collisionModule.collidesWith = settings.collisionLayers;
            }

            if (settings.targetObject != null)
            {
                var container = launcherSystem.transform.Find("Container");
                if (container == null)
                {
                    Debug.LogError("[YUCP Rigidbody Launcher] Prefab missing Container object.");
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
            public GroupMember(RigidbodyLauncherData component, RigidbodyLauncherData.Settings settings, string groupId, bool isIsolated)
            {
                Component = component;
                Settings = settings;
                GroupId = groupId;
                IsIsolated = isIsolated;
            }

            public RigidbodyLauncherData Component { get; }
            public RigidbodyLauncherData.Settings Settings { get; }
            public string GroupId { get; }
            public bool IsIsolated { get; }
        }

        private readonly struct GroupSettingsSignature : IEquatable<GroupSettingsSignature>
        {
            public GroupSettingsSignature(RigidbodyLauncherData.Settings settings)
            {
                MenuLocation = settings.menuLocation;
                LaunchSpeed = settings.launchSpeed;
                MaximumForce = settings.maximumForce;
                CollisionLayers = settings.collisionLayers;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            private string MenuLocation { get; }
            private float LaunchSpeed { get; }
            private float MaximumForce { get; }
            private LayerMask CollisionLayers { get; }
            private bool VerboseLogging { get; }
            private bool IncludeCredits { get; }

            public bool Equals(GroupSettingsSignature other)
            {
                return MenuLocation == other.MenuLocation &&
                       Mathf.Approximately(LaunchSpeed, other.LaunchSpeed) &&
                       Mathf.Approximately(MaximumForce, other.MaximumForce) &&
                       CollisionLayers == other.CollisionLayers &&
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
                    hashCode = (hashCode * 397) ^ LaunchSpeed.GetHashCode();
                    hashCode = (hashCode * 397) ^ MaximumForce.GetHashCode();
                    hashCode = (hashCode * 397) ^ CollisionLayers.GetHashCode();
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private readonly struct GroupKey : IEquatable<GroupKey>
        {
            public GroupKey(RigidbodyLauncherData.Settings settings, string groupId, bool isIsolated)
            {
                LauncherGroupId = groupId;
                IsIsolated = isIsolated;
                MenuLocation = settings.menuLocation;
                LaunchSpeed = settings.launchSpeed;
                MaximumForce = settings.maximumForce;
                CollisionLayers = settings.collisionLayers;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            public string LauncherGroupId { get; }
            public bool IsIsolated { get; }
            public string MenuLocation { get; }
            public float LaunchSpeed { get; }
            public float MaximumForce { get; }
            public LayerMask CollisionLayers { get; }
            public bool VerboseLogging { get; }
            public bool IncludeCredits { get; }

            public bool Equals(GroupKey other)
            {
                return LauncherGroupId == other.LauncherGroupId &&
                       IsIsolated == other.IsIsolated &&
                       MenuLocation == other.MenuLocation &&
                       Mathf.Approximately(LaunchSpeed, other.LaunchSpeed) &&
                       Mathf.Approximately(MaximumForce, other.MaximumForce) &&
                       CollisionLayers == other.CollisionLayers &&
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
                    var hashCode = LauncherGroupId != null ? LauncherGroupId.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ IsIsolated.GetHashCode();
                    hashCode = (hashCode * 397) ^ (MenuLocation != null ? MenuLocation.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ LaunchSpeed.GetHashCode();
                    hashCode = (hashCode * 397) ^ MaximumForce.GetHashCode();
                    hashCode = (hashCode * 397) ^ CollisionLayers.GetHashCode();
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private static string GetIsolatedGroupId(RigidbodyLauncherData.Settings settings, VRCAvatarDescriptor descriptor)
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

