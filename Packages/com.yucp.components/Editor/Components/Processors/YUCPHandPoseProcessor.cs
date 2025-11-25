using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
using com.vrcfury.api;
using YUCP.Components;
using YUCP.Components.HandPoses;
using YUCP.Components.Editor.UI;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Processes Grippable Object components during avatar build.
    /// Generates hand grip animations from hand pose assets and creates VRCFury toggles.
    /// </summary>
    public class YUCPHandPoseProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 50;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var dataList = avatarRoot.GetComponentsInChildren<YUCPGrippableData>(true);
            
            if (dataList.Length == 0) return true;

            var progressWindow = YUCPProgressWindow.Create();
            progressWindow.Progress(0, "Processing grippable objects...");

            try
            {
                var animator = avatarRoot.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    Debug.LogError("[YUCPHandPoseProcessor] No Animator found on avatar");
                    return true;
                }

                for (int i = 0; i < dataList.Length; i++)
                {
                    var data = dataList[i];
                    
                    if (!ValidateData(data)) continue;

                    try
                    {
                        ProcessGrippableComponent(data, animator);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[YUCPHandPoseProcessor] Error processing '{data.name}': {ex.Message}", data);
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

        private bool ValidateData(YUCPGrippableData data)
        {
            if (data.grippedObject == null)
            {
                Debug.LogError("[YUCPHandPoseProcessor] Gripped object is not set", data);
                return false;
            }

            if (data.handPoseAsset == null)
            {
                Debug.LogError("[YUCPHandPoseProcessor] Hand pose asset is not set", data);
                return false;
            }

            return true;
        }

        private void ProcessGrippableComponent(YUCPGrippableData data, Animator animator)
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

        private bool DetermineClosestHand(YUCPGrippableData data, Animator animator)
        {
            var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

            if (leftHand == null) return false;
            if (rightHand == null) return true;

            float leftDist = Vector3.Distance(data.transform.position, leftHand.position);
            float rightDist = Vector3.Distance(data.transform.position, rightHand.position);

            return leftDist <= rightDist;
        }

        private void ProcessHand(YUCPGrippableData data, Animator animator, bool isLeftHand, string menuPath)
        {
            AnimationClip gripAnimation = GenerateGripAnimation(data, animator, isLeftHand);

            if (gripAnimation == null)
            {
                Debug.LogError($"[YUCPHandPoseProcessor] Failed to generate grip animation for {(isLeftHand ? "left" : "right")} hand", data);
                return;
            }

            // Create toggle
            var toggle = FuryComponents.CreateToggle(data.gameObject);
            
            if (!string.IsNullOrEmpty(menuPath))
            {
                toggle.SetMenuPath(menuPath);
            }

            if (data.saved)
            {
                toggle.SetSaved();
            }

            if (data.defaultOn)
            {
                toggle.SetDefaultOn();
            }

            // Set global parameter if specified
            if (!string.IsNullOrEmpty(data.globalParameter))
            {
                toggle.SetGlobalParameter(data.globalParameter);
            }

            var actions = toggle.GetActions();
            actions.AddTurnOn(data.grippedObject.gameObject);
            actions.AddAnimationClip(gripAnimation);

            // Create armature link
            HumanBodyBones handBone = isLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            var armatureLink = FuryComponents.CreateArmatureLink(data.grippedObject.gameObject);
            armatureLink.LinkTo(handBone);

            if (data.debugMode)
            {
                Debug.Log($"[YUCPHandPoseProcessor] Created grip toggle{(string.IsNullOrEmpty(data.globalParameter) ? "" : $" with global parameter '{data.globalParameter}'")} for {(isLeftHand ? "left" : "right")} hand", data);
            }
        }

        private AnimationClip GenerateGripAnimation(YUCPGrippableData data, Animator animator, bool isLeftHand)
        {
            var clip = YUCPGripAnimationGenerator.GenerateGripAnimation(animator, data.handPoseAsset, data, isLeftHand);

            if (clip == null)
            {
                Debug.LogError("[YUCPHandPoseProcessor] Grip animation generation failed", data);
                return null;
            }

            if (data.debugMode)
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                Debug.Log($"[YUCPHandPoseProcessor] Generated grip animation with {bindings.Length} muscle curves for {(isLeftHand ? "left" : "right")} hand", data);
            }

            data.SetGeneratedInfo($"{(isLeftHand ? "Left" : "Right")} hand: {AnimationUtility.GetCurveBindings(clip).Length} muscles");

            return clip;
        }
    }
}

