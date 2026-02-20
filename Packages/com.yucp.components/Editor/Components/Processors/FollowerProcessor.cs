using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.Dynamics;
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
            Debug.Log($"[YUCP Follower] Processing {groups.Count()} groups from {members.Count} members");
            foreach (var group in groups)
            {
                Debug.Log($"[YUCP Follower] Group: speed={group.Key.FollowSpeed}, members={group.Count()}");
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
            if (settings.appliedObject == null)
            {
                Debug.LogError("[YUCP Follower] Target object reference is missing.", component);
                return false;
            }

            if (!settings.appliedObject.transform.IsChildOf(descriptor.transform))
            {
                Debug.LogError("[YUCP Follower] Target object must be inside the avatar descriptor hierarchy.", component);
                return false;
            }

            if (settings.positionTarget != null && !settings.positionTarget.IsChildOf(descriptor.transform))
            {
                Debug.LogError("[YUCP Follower] Position target must be inside the avatar descriptor hierarchy.", component);
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
            var targets = members.Select(m => m.Settings.appliedObject).ToArray();
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
                foreach (var member in members)
                {
                    var settings = member.Settings;
                    var targetKey = AnimationCloneUtility.GetStableTargetKey(settings.appliedObject != null ? settings.appliedObject.transform : null, descriptor.transform);
                    var rootName = AnimationCloneUtility.BuildComponentRootName("Follower", settings.appliedObject != null ? settings.appliedObject.transform : null, descriptor.transform);
                    var clonedController = AnimationCloneUtility.CreateControllerAssetCloneWithRemappedRoot(sourceController, "Follower", prefab.name, rootName, rootName);
                    if (clonedController == null)
                    {
                        Debug.LogError("[YUCP Follower] Failed to clone controller asset.");
                        foreach (var failedMember in members)
                        {
                            failedMember.Component.SetBuildSummary("Build failed");
                        }
                        return false;
                    }
                    var stopBase = string.IsNullOrEmpty(settings.globalParameterStop) ? "Follower/Stop" : settings.globalParameterStop;
                    var paramMap = AnimationCloneUtility.RewriteParameters(
                        clonedController,
                        name =>
                        {
                            if (AnimationCloneUtility.IsIgnoredGlobalParameter(name))
                            {
                                return name;
                            }
                            if (name == "Follower/Stop")
                            {
                                return AnimationCloneUtility.AppendSuffixIfMissing(stopBase, targetKey);
                            }
                            if (name.StartsWith("Follower/", StringComparison.Ordinal))
                            {
                                return AnimationCloneUtility.AppendSuffixIfMissing(name, targetKey);
                            }
                            return name;
                        });

                    ApplyFollowSpeedToController(clonedController, settings.followSpeed);
                    VRCFuryHelper.AddControllerToVRCFury(descriptor, clonedController);
                    RegisterGlobalParameters(descriptor, paramMap);
                    InstallSystem(descriptor, prefab, settings, rootName, clonedController, paramMap);
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

        private static void InstallSystem(VRCAvatarDescriptor descriptor, GameObject prefab, FollowerData.Settings settings, string rootName, AnimatorController controller, Dictionary<string, string> paramMap)
        {
            // Save target's world position and rotation before any changes
            Vector3 targetWorldPosition = Vector3.zero;
            Quaternion targetWorldRotation = Quaternion.identity;
            
            if (settings.appliedObject != null)
            {
                targetWorldPosition = settings.appliedObject.transform.position;
                targetWorldRotation = settings.appliedObject.transform.rotation;
            }

            var rootObject = descriptor.gameObject;
            var followerSystem = UnityEngine.Object.Instantiate(prefab, rootObject.transform);
            followerSystem.name = rootName;

            var followerTarget = followerSystem.transform.Find("Follower Target");
            var positionTarget = settings.positionTarget ?? settings.appliedObject?.transform;
            if (followerTarget != null && positionTarget != null)
            {
                bool targetInsideApplied = settings.appliedObject != null &&
                    (positionTarget == settings.appliedObject.transform || positionTarget.IsChildOf(settings.appliedObject.transform));

                if (!targetInsideApplied)
                {
                    // Parent to the position target so it follows live movement.
                    followerTarget.parent = positionTarget;
                    followerTarget.localPosition = Vector3.zero;
                    followerTarget.localRotation = Quaternion.identity;
                    followerTarget.localScale = Vector3.one;
                }
                else
                {
                    // Keep hierarchy stable and remap animation paths to the follower target.
                    followerTarget.parent = positionTarget.parent;
                    followerTarget.localPosition = positionTarget.localPosition;
                    followerTarget.localRotation = positionTarget.localRotation;
                    followerTarget.localScale = positionTarget.localScale;

                    var oldPath = AnimationUtility.CalculateTransformPath(positionTarget, descriptor.transform);
                    var newPath = AnimationUtility.CalculateTransformPath(followerTarget.transform, descriptor.transform);

                    var allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                        .Where(x => x.animatorController != null)
                        .SelectMany(x => x.animatorController.animationClips)
                        .ToArray();

                    CustomObjectSyncCreator.RenameClipPaths(allClips, false, oldPath, newPath);
                }
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

            if (settings.appliedObject != null)
            {
                var container = followerSystem.transform.Find("Container");
                if (container == null)
                {
                    Debug.LogError("[YUCP Follower] Prefab missing Container object.");
                    return;
                }

                // Position the container at the target's original world position/rotation
                container.position = targetWorldPosition;
                container.rotation = targetWorldRotation;

                var oldPath = AnimationUtility.CalculateTransformPath(settings.appliedObject.transform, descriptor.transform);
                
                // Reparent to container
                settings.appliedObject.transform.parent = container;
                
                // Reset target's local transform to 0,0,0 so it appears at the container's position
                settings.appliedObject.transform.localPosition = Vector3.zero;
                settings.appliedObject.transform.localRotation = Quaternion.identity;
                
                var newPath = AnimationUtility.CalculateTransformPath(settings.appliedObject.transform, descriptor.transform);

                var allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                    .Where(x => x.animatorController != null)
                    .SelectMany(x => x.animatorController.animationClips)
                    .ToArray();

                CustomObjectSyncCreator.RenameClipPaths(allClips, false, oldPath, newPath);
            }

            // Directly modify the constraint's source1 weight for speeds >= 1
            // This bypasses any animation system issues with VRCFury
            Debug.Log($"[YUCP Follower] InstallSystem: followSpeed={settings.followSpeed}");
            if (settings.followSpeed >= 1f)
            {
                Debug.Log($"[YUCP Follower] Trying direct constraint modification for speed {settings.followSpeed}");
                var container = followerSystem.transform.Find("Container");
                Debug.Log($"[YUCP Follower] Container found: {container != null}");
                if (container != null)
                {
                    // The Follower prefab uses VRCPositionConstraint, not VRCParentConstraint
                    var constraint = container.GetComponent<VRC.SDK3.Dynamics.Constraint.Components.VRCPositionConstraint>();
                    Debug.Log($"[YUCP Follower] PositionConstraint found: {constraint != null}, Sources count: {constraint?.Sources.Count ?? 0}");
                    if (constraint != null && constraint.Sources.Count >= 2)
                    {
                        // Higher weight = faster following
                        // Scale exponentially for dramatic differences
                        // speed 1 -> weight 0.01, speed 5 -> weight ~0.5 (capped at 1)
                        float scaledWeight = Mathf.Min(0.01f * Mathf.Pow(settings.followSpeed, 4f), 1f);
                        
                        var source1 = constraint.Sources[1];
                        Debug.Log($"[YUCP Follower] Before: source1.Weight = {source1.Weight}");
                        source1.Weight = scaledWeight;
                        constraint.Sources[1] = source1;
                        Debug.Log($"[YUCP Follower] After: source1.Weight = {constraint.Sources[1].Weight} (set to {scaledWeight})");
                        
                        Debug.Log($"[YUCP Follower] Direct constraint modification: source1.Weight set to {scaledWeight} for speed {settings.followSpeed}");
                    }
                }
            }




            if (settings.generateMenu && !string.IsNullOrEmpty(settings.menuLocation))
            {
                var menu = VRCFuryHelper.GetMenuFromLocation(descriptor, settings.menuLocation);
                if (menu != null)
                {
                    var stopParam = paramMap != null && paramMap.TryGetValue("Follower/Stop", out var mappedStop)
                        ? mappedStop
                        : "Follower/Stop";
                    var label = settings.appliedObject != null ? $"Follower Stop ({settings.appliedObject.name})" : "Follower Stop";
                    VRCFuryHelper.AddMenuToggle(menu, label, stopParam);
                }
            }
        }

        private static void RegisterGlobalParameters(VRCAvatarDescriptor descriptor, Dictionary<string, string> paramMap)
        {
            if (descriptor == null || paramMap == null)
            {
                return;
            }

            foreach (var param in paramMap.Values.Distinct())
            {
                if (AnimationCloneUtility.IsIgnoredGlobalParameter(param))
                {
                    continue;
                }

                VRCFuryHelper.AddGlobalParamToVRCFury(descriptor, param);
            }
        }

        private static void ApplyFollowSpeedToController(AnimatorController controller, float followSpeed)
        {
            if (followSpeed == 1f || controller == null)
            {
                return;
            }

            Debug.Log($"[YUCP Follower] ApplyFollowSpeedToController called with followSpeed={followSpeed}");

            var clips = controller.animationClips;
            Debug.Log($"[YUCP Follower] Found {clips.Length} clips in controller");

            var speedClipSuffixes = new[]
            {
                "Follower Active",
                "Follower Idle",
                "Follower Init"
            };

            var touched = false;
            foreach (var clip in clips)
            {
                if (clip == null)
                {
                    continue;
                }

                var clipName = clip.name;
                Debug.Log($"[YUCP Follower] Checking clip: '{clipName}'");
                
                var isSpeedClip = false;
                foreach (var suffix in speedClipSuffixes)
                {
                    // Use Contains instead of EndsWith because cloned clips have "(Clone)" appended
                    if (clipName.Contains(suffix))
                    {
                        isSpeedClip = true;
                        break;
                    }
                }

                if (!isSpeedClip)
                {
                    Debug.Log($"[YUCP Follower] Skipping clip '{clipName}' - not a speed clip");
                    continue;
                }

                Debug.Log($"[YUCP Follower] Processing speed clip: '{clipName}'");

                // Scale the weight curve values - this controls the actual damping behavior
                // Higher source1.Weight = faster following toward target
                var bindings = AnimationUtility.GetCurveBindings(clip);
                Debug.Log($"[YUCP Follower] Clip '{clipName}' has {bindings.Length} bindings");
                foreach (var binding in bindings)
                {
                    Debug.Log($"[YUCP Follower] Binding: propertyName='{binding.propertyName}' path='{binding.path}'");
                    
                    // Check for source1.Weight - try both exact match and EndsWith
                    bool isSource1Weight = binding.propertyName == "Sources.source1.Weight" || 
                                           binding.propertyName.EndsWith("Sources.source1.Weight", StringComparison.Ordinal) ||
                                           binding.propertyName.Contains("source1.Weight");
                    
                    if (!isSource1Weight)
                    {
                        continue;
                    }

                    
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve != null && curve.keys.Length > 0)
                    {
                        Debug.Log($"[YUCP Follower] Original curve keys: {string.Join(", ", curve.keys.Select(k => k.value))}");
                        
                        var keys = curve.keys;
                        for (int i = 0; i < keys.Length; i++)
                        {
                            var key = keys[i];
                            // Use power of 4 for weight scaling to create bigger differences
                            // followSpeed 0.1 -> 0.0001x, followSpeed 5 -> 625x
                            // This creates much more dramatic weight differences for speed > 1
                            var weightMultiplier = Mathf.Pow(followSpeed, 4f);
                            key.value *= weightMultiplier;
                            // Clamp to reasonable range (0 to 1)
                            key.value = Mathf.Clamp(key.value, 0f, 1f);
                            keys[i] = key;
                        }
                        curve.keys = keys;
                        
                        Debug.Log($"[YUCP Follower] Scaled curve keys (by {followSpeed}^4 = {Mathf.Pow(followSpeed, 4f)}): {string.Join(", ", curve.keys.Select(k => k.value))}");
                        
                        AnimationUtility.SetEditorCurve(clip, binding, curve);
                        EditorUtility.SetDirty(clip);
                        touched = true;
                    }

                }

                // Don't enable looping - the animation should play once to reach final weight.
                // state.speed controls how long it takes to reach that final weight.
            }

            // Apply state.speed as the PRIMARY speed control.
            // AnimatorState properties persist through VRCFury processing,
            // unlike animation clip modifications which may get overwritten.
            var speedClipSuffixesForState = new[] { "Follower Active" };
            bool stateSpeedModified = false;
            foreach (var layer in controller.layers)
            {
                stateSpeedModified |= ApplyFollowSpeedToStateMachine(layer.stateMachine, speedClipSuffixesForState, followSpeed);
            }

            // CRITICAL: Always save the controller after any modifications!
            // VRCFury loads from the asset file, so changes must be persisted to disk.
            if (touched || stateSpeedModified)
            {
                Debug.Log($"[YUCP Follower] Modifications made (curves={touched}, stateSpeed={stateSpeedModified}), saving controller");
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                var controllerPath = AssetDatabase.GetAssetPath(controller);
                if (!string.IsNullOrEmpty(controllerPath))
                {
                    AssetDatabase.ImportAsset(controllerPath);
                    Debug.Log($"[YUCP Follower] Controller saved and reimported: {controllerPath}");
                }
            }
            else
            {
                Debug.LogWarning($"[YUCP Follower] No modifications were made!");
            }
        }

        private static bool ApplyFollowSpeedToStateMachine(AnimatorStateMachine stateMachine, string[] clipNameSuffixes, float followSpeed)
        {
            if (stateMachine == null || clipNameSuffixes == null || clipNameSuffixes.Length == 0)
            {
                return false;
            }

            bool modified = false;
            foreach (var childState in stateMachine.states)
            {
                var state = childState.state;
                if (state == null)
                {
                    continue;
                }

                if (state.motion is AnimationClip clip)
                {
                    var clipName = clip.name;
                    var matches = false;
                    foreach (var suffix in clipNameSuffixes)
                    {
                        // Use Contains instead of EndsWith because cloned clips have "(Clone)" appended
                        if (clipName.Contains(suffix))
                        {
                            matches = true;
                            break;
                        }
                    }

                    if (matches)
                    {
                        // Use state.speed for ALL speed values
                        // VRCFury preserves AnimatorState.speed even though it overwrites animation clips
                        // - For speed < 1: Slow down the animation (smaller values = slower)
                        // - For speed >= 1: Speed up the animation (larger values = faster)
                        float scaledSpeed;
                        if (followSpeed < 1f)
                        {
                            // Power of 10 for extreme slowdown at low speeds
                            scaledSpeed = Mathf.Pow(followSpeed, 10f);
                            Debug.Log($"[YUCP Follower] SLOW MODE: Setting state '{state.name}' speed to {scaledSpeed} (followSpeed={followSpeed}^10)");
                        }
                        else
                        {
                            // For speed >= 1, use state.speed directly to speed up the animation
                            // speed=5 means the animation plays 5x faster
                            scaledSpeed = followSpeed;
                            Debug.Log($"[YUCP Follower] FAST MODE: Setting state '{state.name}' speed to {scaledSpeed} (followSpeed={followSpeed})");
                        }
                        state.speed = scaledSpeed;
                        state.speedParameterActive = false;
                        modified = true;
                    }
                }
            }

            foreach (var child in stateMachine.stateMachines)
            {
                modified |= ApplyFollowSpeedToStateMachine(child.stateMachine, clipNameSuffixes, followSpeed);
            }
            
            return modified;
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
            if (settings.appliedObject == null || descriptor == null)
            {
                return $"__Isolated__/{Guid.NewGuid()}";
            }

            string path = AnimationUtility.CalculateTransformPath(settings.appliedObject.transform, descriptor.transform);
            return $"__Isolated__/{path}";
        }
    }
}
