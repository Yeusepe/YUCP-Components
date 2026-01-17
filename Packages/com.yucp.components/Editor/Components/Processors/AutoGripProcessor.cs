using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
using com.vrcfury.api;
using com.vrcfury.api.Components;

namespace YUCP.Components.Editor.AutoGrip
{
    /// <summary>
    /// VRChat build-time processor for AutoGrip component.
    /// Runs BEFORE ParameterToggleProcessor to ensure clips are ready for wiring.
    /// </summary>
    [InitializeOnLoad]
    public class AutoGripProcessor : IVRCSDKPreprocessAvatarCallback
    {
        // Run before ParameterToggleProcessor (int.MinValue + 50)
        public int callbackOrder => int.MinValue + 40;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var components = avatarRoot.GetComponentsInChildren<AutoGripData>(true);

            foreach (var data in components)
            {
                if (data == null || !data.enabled) continue;

                try
                {
                    ProcessAutoGrip(data, avatarRoot);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AutoGripProcessor] Error processing '{data.name}': {ex.Message}\n{ex.StackTrace}", data);
                }
            }

            return true;
        }

        private void ProcessAutoGrip(AutoGripData data, GameObject avatarRoot)
        {
            var animator = avatarRoot.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
            {
                Debug.LogWarning($"[AutoGripProcessor] Skipping '{data.name}': No humanoid animator on avatar root", data);
                return;
            }

            Debug.Log($"[AutoGripProcessor] Processing '{data.name}'", data);

            // Auto-bake if enabled and clips missing
            if (data.autoBakeOnBuild)
            {
                BakeIfNeeded(data, animator);
            }

            // Auto-wire to toggle if enabled
            if (data.autoWireToToggle)
            {
                WireToToggle(data);
            }
        }

        private void BakeIfNeeded(AutoGripData data, Animator animator)
        {
            bool needsLeftBake = data.generateLeftHand && data.leftHandClip == null;
            bool needsRightBake = data.generateRightHand && data.rightHandClip == null;

            // TODO: Also check input hash for changes
            if (!needsLeftBake && !needsRightBake)
            {
                if (data.verboseLogging)
                {
                    Debug.Log($"[AutoGripProcessor] Clips already exist for '{data.name}', skipping bake");
                }
                return;
            }

            Debug.Log($"[AutoGripProcessor] Auto-baking clips for '{data.name}'");

            // Prepare collision data
            var propCollision = PropCollisionSource.PrepareCollisionData(data.transform, data.collisionMask);
            if (propCollision == null)
            {
                Debug.LogWarning($"[AutoGripProcessor] No collision data for '{data.name}', using default pose");
                return;
            }

            try
            {
                var backend = new ProceduralContactAndCollisionBackend();

                if (needsLeftBake)
                {
                    var leftPose = backend.SynthesizeGrasp(animator, true, data, propCollision);
                    if (leftPose != null)
                    {
                        string path = HandMuscleClipBaker.GenerateClipPath(animator.gameObject, data.gameObject, true);
                        data.leftHandClip = HandMuscleClipBaker.BakeHandMuscles(animator, leftPose, true, path);
                    }
                }

                if (needsRightBake)
                {
                    var rightPose = backend.SynthesizeGrasp(animator, false, data, propCollision);
                    if (rightPose != null)
                    {
                        string path = HandMuscleClipBaker.GenerateClipPath(animator.gameObject, data.gameObject, false);
                        data.rightHandClip = HandMuscleClipBaker.BakeHandMuscles(animator, rightPose, false, path);
                    }
                }
            }
            finally
            {
                propCollision?.Cleanup();
            }
        }

        private void WireToToggle(AutoGripData data)
        {
            switch (data.activationMode)
            {
                case ActivationMode.UseExistingActivationSource:
                    WireToExistingSource(data);
                    break;

                case ActivationMode.BindToGestureLeftRight:
                    WireToGesture(data);
                    break;

                case ActivationMode.BindToPickupIsHeld:
                    WireToPickup(data);
                    break;

                case ActivationMode.AlwaysOnWhenObjectEnabled:
                    WireAlwaysOn(data);
                    break;

                case ActivationMode.CreateMenuToggle:
                    WireWithMenuToggle(data);
                    break;
            }
        }

        private void WireToExistingSource(AutoGripData data)
        {
            if (data.activationSource == null)
            {
                Debug.LogWarning($"[AutoGripProcessor] '{data.name}' has UseExistingActivationSource but no source set", data);
                return;
            }

            // Check if it's a ParameterToggleData
            var toggleData = data.activationSource as ParameterToggleData;
            if (toggleData != null)
            {
                // Assign our clip to the toggle's state
                AnimationClip clip = GetAppropriateClip(data);
                if (clip != null && toggleData.state.animationClip == null)
                {
                    toggleData.state.animationClip = clip;
                    Debug.Log($"[AutoGripProcessor] Wired clip to ParameterToggleData '{toggleData.name}'", data);
                }
                return;
            }

            // Check if it's a VRCFury toggle (via reflection)
            var sourceGo = data.activationSource as GameObject;
            if (sourceGo == null)
            {
                var component = data.activationSource as Component;
                sourceGo = component?.gameObject;
            }

            if (sourceGo != null)
            {
                // Try to find FuryToggle component
                WireToVRCFuryToggle(data, sourceGo);
            }
        }

        private void WireToVRCFuryToggle(AutoGripData data, GameObject toggleObject)
        {
            // Use reflection to add animation to VRCFury toggle if present
            var furyComponents = toggleObject.GetComponents<Component>();
            foreach (var comp in furyComponents)
            {
                if (comp == null) continue;
                var typeName = comp.GetType().FullName;
                
                if (typeName != null && typeName.Contains("VRCFury"))
                {
                    Debug.Log($"[AutoGripProcessor] Found VRCFury component on '{toggleObject.name}', attempting to wire", data);
                    // VRCFury toggle wiring would require their API
                    // For now, log a warning
                    Debug.LogWarning($"[AutoGripProcessor] VRCFury toggle wiring not yet implemented. Manually assign the generated clip.", data);
                    return;
                }
            }
        }

        private void WireToGesture(AutoGripData data)
        {
            // Create ParameterToggleData with gesture conditions
            var toggleData = GetOrCreateToggleData(data);
            if (toggleData == null) return;

            // Clear existing conditions
            toggleData.conditionGroups.Clear();

            // Create condition for left hand (if generating left)
            if (data.generateLeftHand && data.leftHandClip != null)
            {
                var group = new ParameterConditionGroup();
                group.conditions.Add(new ParameterCondition
                {
                    parameterName = "GestureLeft",
                    parameterType = ToggleParameterType.Int,
                    conditionMode = ConditionMode.Greater,
                    threshold = 0
                });
                toggleData.conditionGroups.Add(group);
            }

            // Create condition for right hand (if generating right)
            if (data.generateRightHand && data.rightHandClip != null)
            {
                var group = new ParameterConditionGroup();
                group.conditions.Add(new ParameterCondition
                {
                    parameterName = "GestureRight",
                    parameterType = ToggleParameterType.Int,
                    conditionMode = ConditionMode.Greater,
                    threshold = 0
                });
                toggleData.conditionGroups.Add(group);
            }

            // Assign clip
            toggleData.state.animationClip = GetAppropriateClip(data);

            Debug.Log($"[AutoGripProcessor] Created gesture-bound toggle for '{data.name}'", data);
        }

        private void WireToPickup(AutoGripData data)
        {
            // Look for VRC pickup component using reflection to avoid hard dependency
            // Try correct assembly names from VRC SDK
            var pickupType = System.Type.GetType("VRC.SDK3.Components.VRCPickup, VRCSDK3A");
            if (pickupType == null)
            {
                pickupType = System.Type.GetType("VRC.SDK3.Dynamics.Components.VRCPickup, VRC.Dynamics");
            }
            if (pickupType == null)
            {
                pickupType = System.Type.GetType("VRC.SDK3.Dynamics.Components.VRCPickup, VRC.SDK3.Dynamics.Constraint");
            }
            
            if (pickupType == null)
            {
                Debug.LogWarning($"[AutoGripProcessor] '{data.name}' set to BindToPickupIsHeld but VRCPickup type not found. Make sure VRC SDK is installed.", data);
                return;
            }

            var pickup = data.GetComponent(pickupType);
            if (pickup == null)
            {
                Debug.LogWarning($"[AutoGripProcessor] '{data.name}' set to BindToPickupIsHeld but no VRCPickup found", data);
                return;
            }

            var toggleData = GetOrCreateToggleData(data);
            if (toggleData == null) return;

            toggleData.conditionGroups.Clear();

            // VRCPickup uses "IsHeld" parameter when held by local player
            var group = new ParameterConditionGroup();
            group.conditions.Add(new ParameterCondition
            {
                parameterName = "IsHeld",
                parameterType = ToggleParameterType.Bool,
                conditionMode = ConditionMode.If
            });
            toggleData.conditionGroups.Add(group);

            toggleData.state.animationClip = GetAppropriateClip(data);

            Debug.Log($"[AutoGripProcessor] Created pickup-bound toggle for '{data.name}'", data);
        }

        private void WireAlwaysOn(AutoGripData data)
        {
            var toggleData = GetOrCreateToggleData(data);
            if (toggleData == null) return;

            // No conditions = always on when object is active
            toggleData.conditionGroups.Clear();
            toggleData.defaultOn = true;
            toggleData.state.animationClip = GetAppropriateClip(data);

            Debug.Log($"[AutoGripProcessor] Created always-on toggle for '{data.name}'", data);
        }

        private void WireWithMenuToggle(AutoGripData data)
        {
            var toggleData = GetOrCreateToggleData(data);
            if (toggleData == null) return;

            // ParameterToggleProcessor will handle menu creation
            toggleData.conditionGroups.Clear();
            toggleData.state.animationClip = GetAppropriateClip(data);

            Debug.Log($"[AutoGripProcessor] Created menu toggle for '{data.name}'", data);
        }

        private ParameterToggleData GetOrCreateToggleData(AutoGripData data)
        {
            // Look for existing toggle on same object
            var existing = data.GetComponent<ParameterToggleData>();
            if (existing != null) return existing;

            // Create new one
            var toggle = data.gameObject.AddComponent<ParameterToggleData>();
            return toggle;
        }

        private AnimationClip GetAppropriateClip(AutoGripData data)
        {
            // Prefer right hand clip (more common for VRChat)
            if (data.rightHandClip != null) return data.rightHandClip;
            return data.leftHandClip;
        }
    }
}
