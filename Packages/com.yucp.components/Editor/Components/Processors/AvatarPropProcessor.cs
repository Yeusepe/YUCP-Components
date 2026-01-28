using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;
using com.vrcfury.api;
using com.vrcfury.api.Components;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Build-time processor that automatically installs the Avatar Prop system.
    /// Merges FX controller, adds parameters, and creates toggle using VRCFury.
    /// </summary>
    public class AvatarPropProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 207;

        private const string PREFAB_PATH = "YUCP.AvatarProp/Avatar Prop";
        private const string FX_CONTROLLER_PATH = "YUCP.AvatarProp/Avatar Prop FX";
        private const string PARAMETERS_PATH = "YUCP.AvatarProp/Avatar Prop Parameters";

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                return true;
            }

            var components = avatarRoot.GetComponentsInChildren<AvatarPropData>(true);
            if (components.Length == 0)
            {
                return true;
            }

            var prefab = LoadPrefab();
            if (prefab == null)
            {
                Debug.LogError("[YUCP Avatar Prop] Prefab not found at Resources/YUCP.AvatarProp/Avatar Prop.prefab.");
                return false;
            }

            var fxController = LoadFXController();
            if (fxController == null)
            {
                Debug.LogError("[YUCP Avatar Prop] FX Controller not found at Resources/YUCP.AvatarProp/Avatar Prop FX.controller.");
                return false;
            }

            var parameters = LoadParameters();

            var hasErrors = false;

            foreach (var component in components)
            {
                if (component == null || !component.enabled)
                {
                    continue;
                }

                var settings = component.ToSettings();
                if (!ValidateTarget(descriptor, component, settings))
                {
                    component.SetBuildSummary("Build failed - validation error");
                    hasErrors = true;
                    continue;
                }

                try
                {
                    InstallAvatarProp(descriptor, prefab, fxController, parameters, component, settings);
                    component.SetBuildSummary($"Built successfully at {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");

                    if (settings.verboseLogging)
                    {
                        Debug.Log($"[YUCP Avatar Prop] Successfully processed component on '{component.name}'.", component);
                    }

                    if (settings.includeCredits)
                    {
                        Debug.Log("[YUCP Avatar Prop] Built using ThatFatKidsMom's Avatar Prop (MIT). Please credit when sharing your avatar.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[YUCP Avatar Prop] Failed to process component on '{component.name}': {ex.Message}", component);
                    Debug.LogException(ex);
                    component.SetBuildSummary("Build failed - exception");
                    hasErrors = true;
                }
            }

            return !hasErrors;
        }

        private static GameObject LoadPrefab()
        {
            return UnityEngine.Resources.Load<GameObject>(PREFAB_PATH);
        }

        private static AnimatorController LoadFXController()
        {
            return UnityEngine.Resources.Load<AnimatorController>(FX_CONTROLLER_PATH);
        }

        private static VRCExpressionParameters LoadParameters()
        {
            return UnityEngine.Resources.Load<VRCExpressionParameters>(PARAMETERS_PATH);
        }

        private const string ORIGINAL_ENABLE_PARAM = "AvatarProp/Enable";

        /// <summary>
        /// Clones the controller and renames parameters from original to instance-specific names.
        /// </summary>
        private static AnimatorController CloneControllerWithRenamedParameters(
            AnimatorController original, 
            string oldParamName, 
            string newParamName)
        {
            // Create a copy of the controller
            var cloned = UnityEngine.Object.Instantiate(original);
            cloned.name = original.name + "_" + newParamName.Replace("/", "_");

            // Rename parameters in the controller
            var parameters = cloned.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == oldParamName)
                {
                    parameters[i].name = newParamName;
                }
            }
            cloned.parameters = parameters;

            // Rename parameters in all layers' state machines and transitions
            foreach (var layer in cloned.layers)
            {
                RenameParametersInStateMachine(layer.stateMachine, oldParamName, newParamName);
            }

            return cloned;
        }

        private static void RenameParametersInStateMachine(AnimatorStateMachine stateMachine, string oldName, string newName)
        {
            if (stateMachine == null) return;

            // Rename in entry/exit/any state transitions
            foreach (var transition in stateMachine.entryTransitions)
            {
                RenameParametersInTransition(transition, oldName, newName);
            }
            foreach (var transition in stateMachine.anyStateTransitions)
            {
                RenameParametersInTransition(transition, oldName, newName);
            }

            // Rename in states
            foreach (var state in stateMachine.states)
            {
                foreach (var transition in state.state.transitions)
                {
                    RenameParametersInTransition(transition, oldName, newName);
                }

                // Handle blend trees that use parameters
                if (state.state.motion is BlendTree blendTree)
                {
                    RenameParametersInBlendTree(blendTree, oldName, newName);
                }
            }

            // Recurse into child state machines
            foreach (var child in stateMachine.stateMachines)
            {
                RenameParametersInStateMachine(child.stateMachine, oldName, newName);
            }
        }

        private static void RenameParametersInTransition(AnimatorTransitionBase transition, string oldName, string newName)
        {
            if (transition == null) return;

            var conditions = transition.conditions;
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i].parameter == oldName)
                {
                    var cond = conditions[i];
                    cond.parameter = newName;
                    conditions[i] = cond;
                }
            }
            transition.conditions = conditions;
        }

        private static void RenameParametersInBlendTree(BlendTree blendTree, string oldName, string newName)
        {
            if (blendTree == null) return;

            if (blendTree.blendParameter == oldName)
                blendTree.blendParameter = newName;
            if (blendTree.blendParameterY == oldName)
                blendTree.blendParameterY = newName;

            foreach (var child in blendTree.children)
            {
                if (child.motion is BlendTree childTree)
                {
                    RenameParametersInBlendTree(childTree, oldName, newName);
                }
            }
        }

        private static bool ValidateTarget(VRCAvatarDescriptor descriptor, AvatarPropData component, AvatarPropData.Settings settings)
        {
            if (settings.appliedObject == null)
            {
                Debug.LogError("[YUCP Avatar Prop] Component target object reference is missing.", component);
                return false;
            }

            if (!settings.appliedObject.transform.IsChildOf(descriptor.transform))
            {
                Debug.LogError("[YUCP Avatar Prop] Component must be inside the avatar descriptor hierarchy.", component);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Generates a sanitized instance ID from the component's GameObject name.
        /// </summary>
        private static string GetInstanceId(AvatarPropData component)
        {
            string name = component.gameObject.name;
            // Remove invalid characters, keep alphanumeric, underscore, hyphen
            string sanitized = Regex.Replace(name, @"[^a-zA-Z0-9_-]", "");
            return string.IsNullOrEmpty(sanitized) ? "Default" : sanitized;
        }


        private static void InstallAvatarProp(
            VRCAvatarDescriptor descriptor,
            GameObject prefab,
            AnimatorController fxController,
            VRCExpressionParameters parameters,
            AvatarPropData component,
            AvatarPropData.Settings settings)
        {
            // Generate instance ID from component's GameObject name
            string instanceId = GetInstanceId(component);
            string instanceName = $"AvatarProp-{instanceId}";
            string parameterName = $"AvatarProp/{instanceId}/Enable";
            
            // 1. Instantiate the Avatar Prop prefab under the avatar root
            var avatarRoot = descriptor.gameObject;
            var propSystemInstance = UnityEngine.Object.Instantiate(prefab, avatarRoot.transform);
            propSystemInstance.name = instanceName;
            
            // Calculate the path from avatar root to the prefab instance
            string instancePath = AnimationUtility.CalculateTransformPath(propSystemInstance.transform, descriptor.transform);
            
            // 2. Clone the FX controller with renamed parameters (AvatarProp/Enable -> AvatarProp/{id}/Enable)
            var clonedController = CloneControllerWithRenamedParameters(fxController, ORIGINAL_ENABLE_PARAM, parameterName);
            
            // 3. Create VRCFury FullController ON THE PREFAB INSTANCE (not avatar root)
            var fullController = FuryComponents.CreateFullController(propSystemInstance);
            fullController.AddController(clonedController, VRCAvatarDescriptor.AnimLayerType.FX);
            
            // 4. Add path rewriting: animations reference "Avatar Prop/..." but we use "AvatarProp-{instanceId}"
            fullController.AddPathRewrite("Avatar Prop", instancePath);
            
            // 5. Add expression parameters if available
            if (parameters != null)
            {
                fullController.AddParams(parameters);
            }
            
            // 6. Register unique parameter as global
            fullController.AddGlobalParam(parameterName);

            // 7. Find the Object container and create VRCFury Toggle for visibility
            var objectContainer = propSystemInstance.transform.Find("Object container");
            if (objectContainer == null)
            {
                objectContainer = FindChildByNameContains(propSystemInstance.transform, "Object container");
            }
            if (objectContainer == null)
            {
                objectContainer = FindChildByNameContains(propSystemInstance.transform, "Container");
            }

            if (objectContainer != null)
            {
                // Create VRCFury Toggle on the Object container that uses the unique parameter
                CreateObjectToggle(objectContainer.gameObject, parameterName, settings);
                
                // Handle custom prop replacement or target object positioning
                Transform propToMove = null;
                if (settings.customProp != null)
                {
                    propToMove = settings.customProp.transform;
                }
                else if (settings.appliedObject != null)
                {
                    // If no custom prop, use the target object itself
                    propToMove = settings.appliedObject.transform;
                }

                if (propToMove != null)
                {
                    // Store the world position and rotation before reparenting
                    var worldPosition = propToMove.position;
                    var worldRotation = propToMove.rotation;
                    var worldScale = propToMove.lossyScale;

                    // Store original path for animation retargeting
                    var oldPath = AnimationUtility.CalculateTransformPath(propToMove, descriptor.transform);
                    
                    // Reparent to container
                    propToMove.parent = objectContainer;
                    
                    // Reset object's local transform to identity (0,0,0 position, identity rotation, 1,1,1 scale)
                    propToMove.localPosition = Vector3.zero;
                    propToMove.localRotation = Quaternion.identity;
                    propToMove.localScale = Vector3.one;
                    
                    // Adjust container's transform to maintain the object's original world position
                    // Calculate what the container's world transform should be so that the object (at local 0,0,0) 
                    // appears at its original world position
                    objectContainer.position = worldPosition;
                    objectContainer.rotation = worldRotation;
                    
                    // Note: Scale is trickier with lossyScale, so we'll preserve the container's existing scale
                    // The object's local scale is now 1,1,1, so it will use the container's scale
                    
                    // Calculate new path for animation retargeting
                    var newPath = AnimationUtility.CalculateTransformPath(propToMove, descriptor.transform);

                    // Retarget animations if path changed
                    if (oldPath != newPath)
                    {
                        RetargetAnimationPaths(descriptor, oldPath, newPath);
                    }
                }
            }
            else
            {
                Debug.LogWarning("[YUCP Avatar Prop] Could not find Object Container in prefab.", component);
            }

            // 8. Create menu toggle using VRCFury
            CreateMenuToggle(component.gameObject, parameterName, settings);
        }

        /// <summary>
        /// Creates a VRCFury Toggle on the object container that uses the instance-specific parameter.
        /// </summary>
        private static void CreateObjectToggle(GameObject objectContainer, string parameterName, AvatarPropData.Settings settings)
        {
            try
            {
                var toggle = FuryComponents.CreateToggle(objectContainer);
                
                // Use the instance-specific global parameter
                toggle.SetGlobalParameter(parameterName);
                
                // Add turn on action to show the container when parameter is true
                var actions = toggle.GetActions();
                actions.AddTurnOn(objectContainer);
                
                if (settings.verboseLogging)
                {
                    Debug.Log($"[YUCP Avatar Prop] Created visibility toggle on '{objectContainer.name}' using parameter '{parameterName}'");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP Avatar Prop] Could not create object visibility toggle: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates the menu toggle that controls the instance-specific parameter.
        /// </summary>
        private static void CreateMenuToggle(GameObject targetObject, string parameterName, AvatarPropData.Settings settings)
        {
            try
            {
                var toggle = FuryComponents.CreateToggle(targetObject);
                
                // Set menu path if specified, otherwise use toggle name as path
                string menuPath = !string.IsNullOrEmpty(settings.menuLocation) 
                    ? $"{settings.menuLocation}/{settings.toggleName}" 
                    : settings.toggleName;
                toggle.SetMenuPath(menuPath);
                
                toggle.SetGlobalParameter(parameterName);

                if (settings.saved)
                {
                    toggle.SetSaved();
                }

                if (settings.defaultOn)
                {
                    toggle.SetDefaultOn();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP Avatar Prop] Could not create VRCFury toggle: {ex.Message}. Toggle may need to be created manually.");
            }
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent == null) return null;

            foreach (Transform child in parent)
            {
                if (child.name.Contains(name))
                {
                    return child;
                }

                var found = FindChildRecursive(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindChildByNameContains(Transform parent, string nameContains)
        {
            if (parent == null) return null;

            foreach (Transform child in parent)
            {
                if (child.name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return child;
                }

                var found = FindChildByNameContains(child, nameContains);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void RetargetAnimationPaths(VRCAvatarDescriptor descriptor, string oldPath, string newPath)
        {
            if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath) || oldPath == newPath)
            {
                return;
            }

            try
            {
                var allClips = descriptor.baseAnimationLayers
                    .Concat(descriptor.specialAnimationLayers)
                    .Where(x => x.animatorController != null)
                    .SelectMany(x => x.animatorController.animationClips)
                    .Distinct()
                    .ToArray();

                foreach (var clip in allClips)
                {
                    if (clip == null) continue;

                    var bindings = AnimationUtility.GetCurveBindings(clip);
                    foreach (var binding in bindings)
                    {
                        if (binding.path.StartsWith(oldPath))
                        {
                            var newBindingPath = newPath + binding.path.Substring(oldPath.Length);
                            var curve = AnimationUtility.GetEditorCurve(clip, binding);

                            AnimationUtility.SetEditorCurve(clip, binding, null);

                            var newBinding = binding;
                            newBinding.path = newBindingPath;
                            AnimationUtility.SetEditorCurve(clip, newBinding, curve);
                        }
                    }

                    var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                    foreach (var binding in objectBindings)
                    {
                        if (binding.path.StartsWith(oldPath))
                        {
                            var newBindingPath = newPath + binding.path.Substring(oldPath.Length);
                            var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);

                            AnimationUtility.SetObjectReferenceCurve(clip, binding, null);

                            var newBinding = binding;
                            newBinding.path = newBindingPath;
                            AnimationUtility.SetObjectReferenceCurve(clip, newBinding, keyframes);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP Avatar Prop] Could not retarget animation paths: {ex.Message}");
            }
        }
    }
}
