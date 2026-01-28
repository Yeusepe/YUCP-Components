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
            if (settings.appliedObject == null)
            {
                Debug.LogError("[YUCP Rigidbody Launcher] Target object reference is missing.", component);
                return false;
            }

            if (!settings.appliedObject.transform.IsChildOf(descriptor.transform))
            {
                Debug.LogError("[YUCP Rigidbody Launcher] Target object must be inside the avatar descriptor hierarchy.", component);
                return false;
            }

            if (settings.useGlobalParameters)
            {
                if (string.IsNullOrWhiteSpace(settings.launchParameterName))
                {
                    Debug.LogError("[YUCP Rigidbody Launcher] Launch parameter name is required when using global parameters.", component);
                    return false;
                }

                if (settings.parameterMode == ParameterMode.Dual && string.IsNullOrWhiteSpace(settings.resetParameterName))
                {
                    Debug.LogError("[YUCP Rigidbody Launcher] Reset parameter name is required when using dual parameter mode.", component);
                    return false;
                }
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
            var targets = members.Select(m => m.Settings.appliedObject).ToArray();
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
                // Clone and rename parameters in the controller based on settings
                var modifiedController = RenameControllerParameters(sourceController, key);
                
                VRCFuryHelper.AddControllerToVRCFury(descriptor, modifiedController);

                foreach (var member in members)
                {
                    var settings = member.Settings;
                    InstallSystem(descriptor, prefab, settings);
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
            if (launcherTarget != null && settings.appliedTransform != null)
            {
                var oldPath = AnimationUtility.CalculateTransformPath(settings.appliedTransform.transform, descriptor.transform);
                launcherTarget.parent = settings.appliedTransform.parent;
                launcherTarget.localPosition = settings.appliedTransform.localPosition;
                launcherTarget.localRotation = settings.appliedTransform.localRotation;
                launcherTarget.localScale = settings.appliedTransform.localScale;
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

            if (settings.appliedObject != null)
            {
                var container = launcherSystem.transform.Find("Container");
                if (container == null)
                {
                    Debug.LogError("[YUCP Rigidbody Launcher] Prefab missing Container object.");
                    return;
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

                CustomObjectSyncCreator.RenameClipPaths(allClips, false, oldPath, newPath);
            }

            // Add global parameters to VRCFury if enabled
            if (settings.useGlobalParameters)
            {
                VRCFuryHelper.AddGlobalParamToVRCFury(descriptor, settings.launchParameterName);
                if (settings.parameterMode == ParameterMode.Dual)
                {
                    VRCFuryHelper.AddGlobalParamToVRCFury(descriptor, settings.resetParameterName);
                }
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
                GestureHand = settings.gestureHand;
                LaunchGesture = settings.launchGesture;
                ResetGesture = settings.resetGesture;
                CollisionLayers = settings.collisionLayers;
                UseGlobalParameters = settings.useGlobalParameters;
                ParameterMode = settings.parameterMode;
                LaunchParameterName = settings.launchParameterName;
                ResetParameterName = settings.resetParameterName;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            private string MenuLocation { get; }
            private float LaunchSpeed { get; }
            private float MaximumForce { get; }
            private GestureHand GestureHand { get; }
            private int LaunchGesture { get; }
            private int ResetGesture { get; }
            private LayerMask CollisionLayers { get; }
            private bool UseGlobalParameters { get; }
            private ParameterMode ParameterMode { get; }
            private string LaunchParameterName { get; }
            private string ResetParameterName { get; }
            private bool VerboseLogging { get; }
            private bool IncludeCredits { get; }

            public bool Equals(GroupSettingsSignature other)
            {
                return MenuLocation == other.MenuLocation &&
                       Mathf.Approximately(LaunchSpeed, other.LaunchSpeed) &&
                       Mathf.Approximately(MaximumForce, other.MaximumForce) &&
                       GestureHand == other.GestureHand &&
                       LaunchGesture == other.LaunchGesture &&
                       ResetGesture == other.ResetGesture &&
                       CollisionLayers == other.CollisionLayers &&
                       UseGlobalParameters == other.UseGlobalParameters &&
                       ParameterMode == other.ParameterMode &&
                       LaunchParameterName == other.LaunchParameterName &&
                       ResetParameterName == other.ResetParameterName &&
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
                    hashCode = (hashCode * 397) ^ GestureHand.GetHashCode();
                    hashCode = (hashCode * 397) ^ LaunchGesture.GetHashCode();
                    hashCode = (hashCode * 397) ^ ResetGesture.GetHashCode();
                    hashCode = (hashCode * 397) ^ CollisionLayers.GetHashCode();
                    hashCode = (hashCode * 397) ^ UseGlobalParameters.GetHashCode();
                    hashCode = (hashCode * 397) ^ ParameterMode.GetHashCode();
                    hashCode = (hashCode * 397) ^ (LaunchParameterName != null ? LaunchParameterName.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (ResetParameterName != null ? ResetParameterName.GetHashCode() : 0);
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
                GestureHand = settings.gestureHand;
                LaunchGesture = settings.launchGesture;
                ResetGesture = settings.resetGesture;
                CollisionLayers = settings.collisionLayers;
                UseGlobalParameters = settings.useGlobalParameters;
                ParameterMode = settings.parameterMode;
                LaunchParameterName = settings.launchParameterName;
                ResetParameterName = settings.resetParameterName;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            public string LauncherGroupId { get; }
            public bool IsIsolated { get; }
            public string MenuLocation { get; }
            public float LaunchSpeed { get; }
            public float MaximumForce { get; }
            public GestureHand GestureHand { get; }
            public int LaunchGesture { get; }
            public int ResetGesture { get; }
            public LayerMask CollisionLayers { get; }
            public bool UseGlobalParameters { get; }
            public ParameterMode ParameterMode { get; }
            public string LaunchParameterName { get; }
            public string ResetParameterName { get; }
            public bool VerboseLogging { get; }
            public bool IncludeCredits { get; }

            public bool Equals(GroupKey other)
            {
                return LauncherGroupId == other.LauncherGroupId &&
                       IsIsolated == other.IsIsolated &&
                       MenuLocation == other.MenuLocation &&
                       Mathf.Approximately(LaunchSpeed, other.LaunchSpeed) &&
                       Mathf.Approximately(MaximumForce, other.MaximumForce) &&
                       GestureHand == other.GestureHand &&
                       LaunchGesture == other.LaunchGesture &&
                       ResetGesture == other.ResetGesture &&
                       CollisionLayers == other.CollisionLayers &&
                       UseGlobalParameters == other.UseGlobalParameters &&
                       ParameterMode == other.ParameterMode &&
                       LaunchParameterName == other.LaunchParameterName &&
                       ResetParameterName == other.ResetParameterName &&
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
                    hashCode = (hashCode * 397) ^ GestureHand.GetHashCode();
                    hashCode = (hashCode * 397) ^ LaunchGesture.GetHashCode();
                    hashCode = (hashCode * 397) ^ ResetGesture.GetHashCode();
                    hashCode = (hashCode * 397) ^ CollisionLayers.GetHashCode();
                    hashCode = (hashCode * 397) ^ UseGlobalParameters.GetHashCode();
                    hashCode = (hashCode * 397) ^ ParameterMode.GetHashCode();
                    hashCode = (hashCode * 397) ^ (LaunchParameterName != null ? LaunchParameterName.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (ResetParameterName != null ? ResetParameterName.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private static string GetIsolatedGroupId(RigidbodyLauncherData.Settings settings, VRCAvatarDescriptor descriptor)
        {
            if (settings.appliedObject == null || descriptor == null)
            {
                return $"__Isolated__/{Guid.NewGuid()}";
            }

            string path = AnimationUtility.CalculateTransformPath(settings.appliedObject.transform, descriptor.transform);
            return $"__Isolated__/{path}";
        }

        private static AnimatorController RenameControllerParameters(AnimatorController sourceController, GroupKey key)
        {
            // Clone the controller to avoid modifying the original
            var controller = UnityEngine.Object.Instantiate(sourceController);
            controller.name = sourceController.name;

            // The Launcher FX controller uses "RigidbodyLauncher/Control" as a bool parameter
            if (key.UseGlobalParameters)
            {
                // Global parameter mode: rename RigidbodyLauncher/Control to the user's launch parameter name
                RenameParameterUsingVRCFury(controller, "RigidbodyLauncher/Control", key.LaunchParameterName);
            }
            else
            {
                // Gesture mode: Add gesture parameter and modify transitions to use gestures
                var gestureParamName = key.GestureHand == GestureHand.Left ? "GestureLeft" : "GestureRight";
                
                // Add the gesture parameter to the controller if it doesn't exist
                var parameters = new List<AnimatorControllerParameter>(controller.parameters);
                if (!parameters.Exists(p => p.name == gestureParamName))
                {
                    parameters.Add(new AnimatorControllerParameter
                    {
                        name = gestureParamName,
                        type = AnimatorControllerParameterType.Int,
                        defaultInt = 0
                    });
                    controller.parameters = parameters.ToArray();
                }

                // Modify transitions: replace RigidbodyLauncher/Control with gesture-based conditions
                foreach (var layer in controller.layers)
                {
                    ModifyTransitionsForGestures(layer.stateMachine, gestureParamName, key.LaunchGesture, key.ResetGesture);
                }
            }

            return controller;
        }

        private static void RenameParameterUsingVRCFury(AnimatorController controller, string oldName, string newName)
        {
            Assembly vrcfuryAssembly = Assembly.Load("VRCFury-Editor");
            if (vrcfuryAssembly == null)
            {
                Debug.LogError("[YUCP Rigidbody Launcher] Failed to load VRCFury-Editor assembly.");
                return;
            }

            Type vfControllerType = vrcfuryAssembly.GetType("VF.Utils.Controller.VFController");
            if (vfControllerType == null)
            {
                Debug.LogError("[YUCP Rigidbody Launcher] Failed to find VF.Utils.Controller.VFController type in VRCFury-Editor assembly.");
                return;
            }

            object vfController = Activator.CreateInstance(vfControllerType, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, new object[] { controller }, null);
            if (vfController == null)
            {
                Debug.LogError("[YUCP Rigidbody Launcher] Failed to create VFController instance.");
                return;
            }

            Type vfLayerType = vrcfuryAssembly.GetType("VF.Utils.Controller.VFLayer");
            Type iCollectionType = typeof(ICollection<>);
            Type iCollectionVFLayerType = vfLayerType != null ? iCollectionType.MakeGenericType(vfLayerType) : null;

            MethodInfo rewriteParametersMethod = vfControllerType.GetMethod("RewriteParameters",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(Func<string, string>), typeof(bool), typeof(bool), iCollectionVFLayerType ?? typeof(ICollection<object>) },
                null);

            if (rewriteParametersMethod == null)
            {
                Debug.LogError("[YUCP Rigidbody Launcher] Failed to find RewriteParameters method on VFController.");
                return;
            }

            Func<string, string> renameParam = (string paramName) =>
            {
                if (paramName == oldName)
                {
                    return newName;
                }
                return paramName;
            };

            rewriteParametersMethod.Invoke(vfController, new object[] { renameParam, true, true, null });
        }

        private static void ModifyTransitionsForGestures(AnimatorStateMachine stateMachine, string gestureParamName, int launchGesture, int resetGesture)
        {
            if (stateMachine == null) return;

            foreach (var state in stateMachine.states)
            {
                if (state.state == null) continue;
                foreach (AnimatorStateTransition transition in state.state.transitions)
                {
                    ModifyTransitionForGestures(transition, gestureParamName, launchGesture, resetGesture);
                }
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                if (childStateMachine.stateMachine != null)
                {
                    ModifyTransitionsForGestures(childStateMachine.stateMachine, gestureParamName, launchGesture, resetGesture);
                }
            }

            foreach (var transition in stateMachine.anyStateTransitions)
            {
                ModifyTransitionForGestures(transition, gestureParamName, launchGesture, resetGesture);
            }

            foreach (var transition in stateMachine.entryTransitions)
            {
                ModifyTransitionForGestures(transition, gestureParamName, launchGesture, resetGesture);
            }
        }

        private static void ModifyTransitionForGestures(AnimatorTransitionBase transition, string gestureParamName, int launchGesture, int resetGesture)
        {
            if (transition == null || transition.conditions == null) return;

            var newConditions = new List<AnimatorCondition>();
            foreach (var condition in transition.conditions)
            {
                var newCondition = new AnimatorCondition
                {
                    mode = condition.mode,
                    threshold = condition.threshold,
                    parameter = condition.parameter
                };

                // Replace RigidbodyLauncher/Control with gesture-based conditions
                if (condition.parameter == "RigidbodyLauncher/Control")
                {
                    newCondition.parameter = gestureParamName;
                    
                    // The Launcher uses bool conditions: If true (mode 1) -> Fire, If false (mode 2) -> Reset
                    // We need to convert to gesture thresholds: Equals launchGesture -> Fire, Equals resetGesture -> Reset
                    if (condition.mode == AnimatorConditionMode.If) // true -> launch gesture
                    {
                        newCondition.mode = AnimatorConditionMode.Equals;
                        newCondition.threshold = launchGesture;
                    }
                    else if (condition.mode == AnimatorConditionMode.IfNot) // false -> reset gesture
                    {
                        newCondition.mode = AnimatorConditionMode.Equals;
                        newCondition.threshold = resetGesture;
                    }
                }

                newConditions.Add(newCondition);
            }

            transition.conditions = newConditions.ToArray();
        }
    }
}
