using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
using com.vrcfury.api;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using YUCP.Components.Editor.UI;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Processes Auto Grip Generator components during avatar build.
    /// Generates hand grip animations based on object mesh contact detection.
    /// Creates VRCFury toggles with object enable and grip animation actions.
    /// </summary>
    public class AutoGripProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 50;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var dataList = avatarRoot.GetComponentsInChildren<AutoGripData>(true);
            
            if (dataList.Length == 0) return true;

            var progressWindow = YUCPProgressWindow.Create();
            progressWindow.Progress(0, "Processing auto grip generators...");

            try
            {
                var animator = avatarRoot.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    Debug.LogError("[AutoGripProcessor] No Animator found on avatar");
                    return true;
                }

                for (int i = 0; i < dataList.Length; i++)
                {
                    var data = dataList[i];
                    
                    if (!ValidateData(data)) continue;

                    try
                    {
                        ProcessGripComponent(data, animator);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[AutoGripProcessor] Error processing '{data.name}': {ex.Message}", data);
                        Debug.LogException(ex);
                    }

                    float progress = (float)(i + 1) / dataList.Length;
                    progressWindow.Progress(progress, $"Processed grip {i + 1}/{dataList.Length}");
                }
            }
            finally
            {
                progressWindow.CloseWindow();
            }

            return true;
        }

        private bool ValidateData(AutoGripData data)
        {
            if (data.grippedObject == null)
            {
                Debug.LogError("[AutoGripProcessor] Gripped object is not set", data);
                return false;
            }

            // Gizmo-based grip generation is always enabled
            // No need to check for custom animations since we removed that system

            return true;
        }

        private void ProcessGripComponent(AutoGripData data, Animator animator)
        {
            switch (data.targetHand)
            {
                case HandTarget.Left:
                    ProcessHand(data, animator, true, data.menuPath);
                    break;

                case HandTarget.Right:
                    ProcessHand(data, animator, false, data.menuPath);
                    break;

                case HandTarget.Both:
                    ProcessHand(data, animator, true, data.menuPath + " L");
                    ProcessHand(data, animator, false, data.menuPath + " R");
                    break;

                case HandTarget.Closest:
                    bool isLeft = DetermineClosestHand(data, animator);
                    ProcessHand(data, animator, isLeft, data.menuPath);
                    break;
            }
        }

        private bool DetermineClosestHand(AutoGripData data, Animator animator)
        {
            var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

            if (leftHand == null) return false;
            if (rightHand == null) return true;

            float leftDist = Vector3.Distance(data.transform.position, leftHand.position);
            float rightDist = Vector3.Distance(data.transform.position, rightHand.position);

            return leftDist <= rightDist;
        }

        private void ProcessHand(AutoGripData data, Animator animator, bool isLeftHand, string menuPath)
        {
            AnimationClip gripAnimation = GetOrGenerateGripAnimation(data, animator, isLeftHand);

            if (gripAnimation == null)
            {
                Debug.LogError($"[AutoGripProcessor] Failed to get grip animation for {(isLeftHand ? "left" : "right")} hand", data);
                return;
            }

            var toggle = FuryComponents.CreateToggle(data.gameObject);
            toggle.SetMenuPath(menuPath);

            if (data.saved)
            {
                toggle.SetSaved();
            }

            if (data.defaultOn)
            {
                toggle.SetDefaultOn();
            }

            if (!string.IsNullOrEmpty(data.globalParameter))
            {
                toggle.SetGlobalParameter(data.globalParameter);
            }

            var actions = toggle.GetActions();
            actions.AddTurnOn(data.grippedObject.gameObject);
            actions.AddAnimationClip(gripAnimation);

            HumanBodyBones handBone = isLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            var armatureLink = FuryComponents.CreateArmatureLink(data.grippedObject.gameObject);
            armatureLink.LinkTo(handBone);

            if (data.debugMode)
            {
                Debug.Log($"[AutoGripProcessor] Created grip toggle '{menuPath}' for {(isLeftHand ? "left" : "right")} hand", data);
            }
        }

        private AnimationClip GetOrGenerateGripAnimation(AutoGripData data, Animator animator, bool isLeftHand)
        {
            // Always generate grip from finger tip gizmos
            var gripResult = GripGenerator.GenerateGrip(animator, data.grippedObject, data, isLeftHand);

            if (gripResult == null)
            {
                Debug.LogError("[AutoGripProcessor] Grip generation failed", data);
                return null;
            }

            if (data.debugMode)
            {
                Debug.Log($"[AutoGripProcessor] Generated grip with {gripResult.muscleValues.Count} muscle curves, " +
                         $"{gripResult.contactPoints.Count} contact points");
            }

            data.SetGeneratedInfo($"{(isLeftHand ? "Left" : "Right")} hand: {gripResult.contactPoints.Count} contact points, " +
                                 $"{gripResult.muscleValues.Count} muscles");

            return gripResult.animation;
        }

        private AnimationClip MirrorGripAnimation(AnimationClip source, bool isLeftHand)
        {
            AnimationClip mirrored = new AnimationClip();
            mirrored.name = source.name.Replace("Left", "Right").Replace("Right", "Left");
            mirrored.legacy = false;

            var bindings = AnimationUtility.GetCurveBindings(source);
            
            foreach (var binding in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                
                string newPropertyName = binding.propertyName;
                if (binding.propertyName.Contains("LeftHand"))
                {
                    newPropertyName = binding.propertyName.Replace("LeftHand", "RightHand");
                }
                else if (binding.propertyName.Contains("RightHand"))
                {
                    newPropertyName = binding.propertyName.Replace("RightHand", "LeftHand");
                }

                EditorCurveBinding newBinding = EditorCurveBinding.FloatCurve(
                    binding.path,
                    binding.type,
                    newPropertyName
                );

                AnimationUtility.SetEditorCurve(mirrored, newBinding, curve);
            }

            return mirrored;
        }
    }
}



