using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            if (settings.appliedObject == null)
            {
                Debug.LogError("[YUCP Rigidbody Throw] Target object reference is missing.", component);
                return false;
            }

            if (!settings.appliedObject.transform.IsChildOf(descriptor.transform))
            {
                Debug.LogError("[YUCP Rigidbody Throw] Target object must be inside the avatar descriptor hierarchy.", component);
                return false;
            }

            if (settings.useGlobalParameters)
            {
                if (string.IsNullOrWhiteSpace(settings.throwParameterName))
                {
                    Debug.LogError("[YUCP Rigidbody Throw] Throw parameter name is required when using global parameters.", component);
                    return false;
                }

                if (settings.parameterMode == ParameterMode.Dual && string.IsNullOrWhiteSpace(settings.resetParameterName))
                {
                    Debug.LogError("[YUCP Rigidbody Throw] Reset parameter name is required when using dual parameter mode.", component);
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
                    Debug.LogWarning($"[YUCP Rigidbody Throw] Group \"{group.Key}\" contains components with mismatched settings. They will be split into {signatures.Count} separate setups.");
                }
            }
        }

        private static bool BuildGroup(VRCAvatarDescriptor descriptor, GameObject prefab, AnimatorController sourceController, VRCExpressionParameters sourceParameters, GroupKey key, List<GroupMember> members)
        {
            var targets = members.Select(m => m.Settings.appliedObject).ToArray();
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
                VRCFuryHelper.AddParamsToVRCFury(descriptor, sourceParameters);

                foreach (var member in members)
                {
                    var settings = member.Settings;
                    var targetKey = AnimationCloneUtility.GetStableTargetKey(settings.appliedObject != null ? settings.appliedObject.transform : null, descriptor.transform);
                    var rootName = AnimationCloneUtility.BuildComponentRootName("Rigidbody_Throw", settings.appliedObject != null ? settings.appliedObject.transform : null, descriptor.transform);
                    var controller = AnimationCloneUtility.CreateControllerAssetCloneWithRemappedRoot(sourceController, "RigidbodyThrow", prefab.name, rootName, rootName);
                    if (controller == null)
                    {
                        Debug.LogError("[YUCP Rigidbody Throw] Failed to clone controller asset.");
                        foreach (var failedMember in members)
                        {
                            failedMember.Component.SetBuildSummary("Build failed");
                        }
                        return false;
                    }
                    RenameControllerParameters(controller, key);
                    var paramMap = AnimationCloneUtility.RewriteParameters(
                        controller,
                        name =>
                        {
                            if (AnimationCloneUtility.IsIgnoredGlobalParameter(name))
                            {
                                return name;
                            }
                            if (!string.IsNullOrEmpty(key.ThrowParameterName) && name == key.ThrowParameterName)
                            {
                                return AnimationCloneUtility.AppendSuffixIfMissing(key.ThrowParameterName, targetKey);
                            }
                            if (!string.IsNullOrEmpty(key.ResetParameterName) && name == key.ResetParameterName)
                            {
                                return AnimationCloneUtility.AppendSuffixIfMissing(key.ResetParameterName, targetKey);
                            }
                            if (name.StartsWith("RigidbodyThrow", StringComparison.Ordinal))
                            {
                                return AnimationCloneUtility.AppendSuffixIfMissing(name, targetKey);
                            }
                            return name;
                        });

                    VRCFuryHelper.AddControllerToVRCFury(descriptor, controller);
                    RegisterGlobalParameters(descriptor, paramMap);
                    InstallSystem(descriptor, prefab, settings, rootName);
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

        private static void InstallSystem(VRCAvatarDescriptor descriptor, GameObject prefab, RigidbodyThrowData.Settings settings, string rootName)
        {
            var rootObject = descriptor.gameObject;
            var throwSystem = UnityEngine.Object.Instantiate(prefab, rootObject.transform);
            throwSystem.name = rootName;

            var throwTarget = throwSystem.transform.Find("Throw/Throw Target");
            if (throwTarget == null)
            {
                throwTarget = throwSystem.transform.Find("Throw Target");
            }

            if (throwTarget != null && settings.appliedTransform != null)
            {
                var oldPath = AnimationUtility.CalculateTransformPath(settings.appliedTransform.transform, descriptor.transform);
                throwTarget.parent = settings.appliedTransform.parent;
                throwTarget.localPosition = settings.appliedTransform.localPosition;
                throwTarget.localRotation = settings.appliedTransform.localRotation;
                throwTarget.localScale = settings.appliedTransform.localScale;
                var newPath = AnimationUtility.CalculateTransformPath(throwTarget.transform, descriptor.transform);

                var allClips = descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                    .Where(x => x.animatorController != null)
                    .SelectMany(x => x.animatorController.animationClips)
                    .ToArray();

                CustomObjectSyncCreator.RenameClipPaths(allClips, false, oldPath, newPath);
            }

            if (settings.appliedObject != null)
            {
                var container = throwSystem.transform.Find("Throw/Container");
                if (container == null)
                {
                    container = throwSystem.transform.Find("Container");
                }
                if (container == null)
                {
                    Debug.LogError("[YUCP Rigidbody Throw] Prefab missing Container object.");
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

            if (settings.physicsMaterial != null)
            {
                var collisionCollider = throwSystem.transform.Find("Throw/Container/Colliders/Collision Collider");
                if (collisionCollider != null)
                {
                    var collider = collisionCollider.GetComponent<Collider>();
                    if (collider != null)
                    {
                        collider.material = settings.physicsMaterial;
                    }
                }
            }

            // Add global parameters to VRCFury if enabled
            if (settings.useGlobalParameters)
            {
                VRCFuryHelper.AddGlobalParamToVRCFury(descriptor, settings.throwParameterName);
                if (settings.parameterMode == ParameterMode.Dual)
                {
                    VRCFuryHelper.AddGlobalParamToVRCFury(descriptor, settings.resetParameterName);
                }
            }

            var particleSystem = throwSystem.GetComponentInChildren<ParticleSystem>();
            if (particleSystem != null)
            {
                var collisionModule = particleSystem.collision;
                collisionModule.collidesWith = settings.collisionLayers;
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

        private static void RenameControllerParameters(AnimatorController controller, GroupKey key)
        {
            // Use VRCFury's RewriteParameters method via reflection
            // Assembly name is "VRCFury-Editor" from the asmdef file
            Assembly vrcfuryAssembly = Assembly.Load("VRCFury-Editor");
            if (vrcfuryAssembly == null)
            {
                Debug.LogError("[YUCP Rigidbody Throw] Failed to load VRCFury-Editor assembly.");
                return;
            }

            // Get the VFController type from the assembly
            Type vfControllerType = vrcfuryAssembly.GetType("VF.Utils.Controller.VFController");
            if (vfControllerType == null)
            {
                Debug.LogError("[YUCP Rigidbody Throw] Failed to find VF.Utils.Controller.VFController type in VRCFury-Editor assembly.");
                return;
            }

            // Create instance using constructor: VFController(AnimatorController ctrl)
            object vfController = Activator.CreateInstance(vfControllerType, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, new object[] { controller }, null);
            if (vfController == null)
            {
                Debug.LogError("[YUCP Rigidbody Throw] Failed to create VFController instance.");
                return;
            }

            // Get RewriteParameters method with signature: RewriteParameters(Func<string, string>, bool, bool, ICollection<VFLayer>)
            // Need to get VFLayer type first to construct the method signature
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
                Debug.LogError("[YUCP Rigidbody Throw] Failed to find RewriteParameters method on VFController.");
                return;
            }

            // Create parameter rename function
            Func<string, string> renameParam = (string paramName) =>
            {
                // Only rename gesture parameters
                if (paramName == "GestureRight" || paramName == "GestureLeft")
                {
                    if (key.UseGlobalParameters)
                    {
                        // For global parameters, we need to map based on context (throw vs reset)
                        // This will be handled by the transition modification below
                        return key.ThrowParameterName;
                    }
                    else
                    {
                        // For gesture mode, rename to selected hand
                        return key.GestureHand == GestureHand.Left ? "GestureLeft" : "GestureRight";
                    }
                }
                return paramName;
            };

            // Call VRCFury's RewriteParameters method
            rewriteParametersMethod.Invoke(vfController, new object[] { renameParam, true, true, null });

            // For global parameters, we still need to handle throw vs reset threshold mapping
            if (key.UseGlobalParameters)
            {
                RenameGlobalParameterTransitions(controller, key);
            }
            else
            {
                // For gesture mode, update thresholds if needed
                RenameGestureThresholds(controller, key);
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

        private static void RenameGlobalParameterTransitions(AnimatorController controller, GroupKey key)
        {
            foreach (var layer in controller.layers)
            {
                RenameGlobalParameterTransitionsInStateMachine(layer.stateMachine, key);
            }
        }

        private static void RenameGlobalParameterTransitionsInStateMachine(AnimatorStateMachine stateMachine, GroupKey key)
        {
            if (stateMachine == null) return;

            foreach (var state in stateMachine.states)
            {
                if (state.state == null) continue;
                foreach (AnimatorStateTransition transition in state.state.transitions)
                {
                    RenameGlobalParameterTransition(transition, key);
                }
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                if (childStateMachine.stateMachine != null)
                {
                    RenameGlobalParameterTransitionsInStateMachine(childStateMachine.stateMachine, key);
                }
            }

            foreach (var transition in stateMachine.anyStateTransitions)
            {
                RenameGlobalParameterTransition(transition, key);
            }

            foreach (var transition in stateMachine.entryTransitions)
            {
                RenameGlobalParameterTransition(transition, key);
            }
        }

        private static void RenameGlobalParameterTransition(AnimatorTransitionBase transition, GroupKey key)
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

                // If this condition uses the throw parameter, check if we need to adjust threshold
                if (condition.parameter == key.ThrowParameterName)
                {
                    if (key.ParameterMode == ParameterMode.Single)
                    {
                        // Single mode: throw (was threshold 2) -> 1.0, reset (was threshold 1) -> 0.0
                        if (condition.mode == AnimatorConditionMode.Equals && condition.threshold == 2f)
                        {
                            newCondition.threshold = 1.0f;
                        }
                        else if (condition.mode == AnimatorConditionMode.Equals && condition.threshold == 1f)
                        {
                            newCondition.threshold = 0.0f;
                        }
                    }
                    else // Dual mode
                    {
                        // Dual mode: throw (threshold 2) -> throw param 1.0, reset (threshold 1) -> reset param 1.0
                        if (condition.mode == AnimatorConditionMode.Equals && condition.threshold == 1f)
                        {
                            newCondition.parameter = key.ResetParameterName;
                            newCondition.threshold = 1.0f;
                        }
                        else if (condition.mode == AnimatorConditionMode.Equals && condition.threshold == 2f)
                        {
                            newCondition.threshold = 1.0f;
                        }
                    }
                }

                newConditions.Add(newCondition);
            }

            transition.conditions = newConditions.ToArray();
        }

        private static void RenameGestureThresholds(AnimatorController controller, GroupKey key)
        {
            foreach (var layer in controller.layers)
            {
                RenameGestureThresholdsInStateMachine(layer.stateMachine, key);
            }
        }

        private static void RenameGestureThresholdsInStateMachine(AnimatorStateMachine stateMachine, GroupKey key)
        {
            if (stateMachine == null) return;

            foreach (var state in stateMachine.states)
            {
                if (state.state == null) continue;
                foreach (AnimatorStateTransition transition in state.state.transitions)
                {
                    RenameGestureThresholdTransition(transition, key);
                }
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                if (childStateMachine.stateMachine != null)
                {
                    RenameGestureThresholdsInStateMachine(childStateMachine.stateMachine, key);
                }
            }

            foreach (var transition in stateMachine.anyStateTransitions)
            {
                RenameGestureThresholdTransition(transition, key);
            }

            foreach (var transition in stateMachine.entryTransitions)
            {
                RenameGestureThresholdTransition(transition, key);
            }
        }

        private static void RenameGestureThresholdTransition(AnimatorTransitionBase transition, GroupKey key)
        {
            if (transition == null || transition.conditions == null) return;

            var gestureParamName = key.GestureHand == GestureHand.Left ? "GestureLeft" : "GestureRight";
            var newConditions = new List<AnimatorCondition>();
            
            foreach (var condition in transition.conditions)
            {
                var newCondition = new AnimatorCondition
                {
                    mode = condition.mode,
                    threshold = condition.threshold,
                    parameter = condition.parameter
                };

                // Update thresholds for gesture parameters if they match defaults
                if (condition.parameter == gestureParamName)
                {
                    if (condition.mode == AnimatorConditionMode.Equals && condition.threshold == 2f)
                    {
                        newCondition.threshold = key.ThrowGesture;
                    }
                    else if (condition.mode == AnimatorConditionMode.Equals && condition.threshold == 1f)
                    {
                        newCondition.threshold = key.ResetGesture;
                    }
                }

                newConditions.Add(newCondition);
            }

            transition.conditions = newConditions.ToArray();
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
                ThrowGesture = settings.throwGesture;
                ResetGesture = settings.resetGesture;
                GestureHand = settings.gestureHand;
                CollisionLayers = settings.collisionLayers;
                UseGlobalParameters = settings.useGlobalParameters;
                ParameterMode = settings.parameterMode;
                ThrowParameterName = settings.throwParameterName;
                ResetParameterName = settings.resetParameterName;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            private bool EnableRotationSync { get; }
            private string MenuLocation { get; }
            private int ThrowGesture { get; }
            private int ResetGesture { get; }
            private GestureHand GestureHand { get; }
            private LayerMask CollisionLayers { get; }
            private bool UseGlobalParameters { get; }
            private ParameterMode ParameterMode { get; }
            private string ThrowParameterName { get; }
            private string ResetParameterName { get; }
            private bool VerboseLogging { get; }
            private bool IncludeCredits { get; }

            public bool Equals(GroupSettingsSignature other)
            {
                return EnableRotationSync == other.EnableRotationSync &&
                       MenuLocation == other.MenuLocation &&
                       ThrowGesture == other.ThrowGesture &&
                       ResetGesture == other.ResetGesture &&
                       GestureHand == other.GestureHand &&
                       CollisionLayers == other.CollisionLayers &&
                       UseGlobalParameters == other.UseGlobalParameters &&
                       ParameterMode == other.ParameterMode &&
                       ThrowParameterName == other.ThrowParameterName &&
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
                    var hashCode = EnableRotationSync.GetHashCode();
                    hashCode = (hashCode * 397) ^ (MenuLocation != null ? MenuLocation.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ ThrowGesture.GetHashCode();
                    hashCode = (hashCode * 397) ^ ResetGesture.GetHashCode();
                    hashCode = (hashCode * 397) ^ GestureHand.GetHashCode();
                    hashCode = (hashCode * 397) ^ CollisionLayers.GetHashCode();
                    hashCode = (hashCode * 397) ^ UseGlobalParameters.GetHashCode();
                    hashCode = (hashCode * 397) ^ ParameterMode.GetHashCode();
                    hashCode = (hashCode * 397) ^ (ThrowParameterName != null ? ThrowParameterName.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (ResetParameterName != null ? ResetParameterName.GetHashCode() : 0);
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
                ThrowGesture = settings.throwGesture;
                ResetGesture = settings.resetGesture;
                GestureHand = settings.gestureHand;
                CollisionLayers = settings.collisionLayers;
                UseGlobalParameters = settings.useGlobalParameters;
                ParameterMode = settings.parameterMode;
                ThrowParameterName = settings.throwParameterName;
                ResetParameterName = settings.resetParameterName;
                VerboseLogging = settings.verboseLogging;
                IncludeCredits = settings.includeCredits;
            }

            public string ThrowGroupId { get; }
            public bool IsIsolated { get; }
            public bool EnableRotationSync { get; }
            public string MenuLocation { get; }
            public int ThrowGesture { get; }
            public int ResetGesture { get; }
            public GestureHand GestureHand { get; }
            public LayerMask CollisionLayers { get; }
            public bool UseGlobalParameters { get; }
            public ParameterMode ParameterMode { get; }
            public string ThrowParameterName { get; }
            public string ResetParameterName { get; }
            public bool VerboseLogging { get; }
            public bool IncludeCredits { get; }

            public bool Equals(GroupKey other)
            {
                return ThrowGroupId == other.ThrowGroupId &&
                       IsIsolated == other.IsIsolated &&
                       EnableRotationSync == other.EnableRotationSync &&
                       MenuLocation == other.MenuLocation &&
                       ThrowGesture == other.ThrowGesture &&
                       ResetGesture == other.ResetGesture &&
                       GestureHand == other.GestureHand &&
                       CollisionLayers == other.CollisionLayers &&
                       UseGlobalParameters == other.UseGlobalParameters &&
                       ParameterMode == other.ParameterMode &&
                       ThrowParameterName == other.ThrowParameterName &&
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
                    var hashCode = ThrowGroupId != null ? ThrowGroupId.GetHashCode() : 0;
                    hashCode = (hashCode * 397) ^ IsIsolated.GetHashCode();
                    hashCode = (hashCode * 397) ^ EnableRotationSync.GetHashCode();
                    hashCode = (hashCode * 397) ^ (MenuLocation != null ? MenuLocation.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ ThrowGesture.GetHashCode();
                    hashCode = (hashCode * 397) ^ ResetGesture.GetHashCode();
                    hashCode = (hashCode * 397) ^ GestureHand.GetHashCode();
                    hashCode = (hashCode * 397) ^ CollisionLayers.GetHashCode();
                    hashCode = (hashCode * 397) ^ UseGlobalParameters.GetHashCode();
                    hashCode = (hashCode * 397) ^ ParameterMode.GetHashCode();
                    hashCode = (hashCode * 397) ^ (ThrowParameterName != null ? ThrowParameterName.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (ResetParameterName != null ? ResetParameterName.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ VerboseLogging.GetHashCode();
                    hashCode = (hashCode * 397) ^ IncludeCredits.GetHashCode();
                    return hashCode;
                }
            }
        }

        private static string GetIsolatedGroupId(RigidbodyThrowData.Settings settings, VRCAvatarDescriptor descriptor)
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
