// Helper class to extract hand/finger bone information from VRChat avatars
// Uses Unity Animator and HumanBodyBones instead of UltimateXR's custom rig system

using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace YUCP.Components.HandPoses
{
    /// <summary>
    /// Helper class to extract hand and finger bone information from VRChat avatars.
    /// </summary>
    public static class YUCPAvatarRigHelper
    {
        /// <summary>
        /// Gets the wrist transform for the specified hand.
        /// </summary>
        public static Transform GetWrist(Animator animator, YUCPHandSide handSide)
        {
            if (animator == null) return null;
            return animator.GetBoneTransform(handSide == YUCPHandSide.Left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        }

        /// <summary>
        /// Gets finger bone transforms for the specified finger and hand.
        /// </summary>
        /// <returns>Tuple of (metacarpal, proximal, intermediate, distal) - any can be null</returns>
        public static (Transform metacarpal, Transform proximal, Transform intermediate, Transform distal) GetFingerBones(
            Animator animator, 
            YUCPHandSide handSide, 
            YUCPFingerType fingerType)
        {
            if (animator == null) return (null, null, null, null);

            HumanBodyBones proximalBone;
            HumanBodyBones intermediateBone;
            HumanBodyBones distalBone;
            HumanBodyBones? metacarpalBone = null;

            bool isLeft = handSide == YUCPHandSide.Left;

            switch (fingerType)
            {
                case YUCPFingerType.Thumb:
                    proximalBone = isLeft ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal;
                    intermediateBone = isLeft ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate;
                    distalBone = isLeft ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal;
                    break;
                case YUCPFingerType.Index:
                    proximalBone = isLeft ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal;
                    intermediateBone = isLeft ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate;
                    distalBone = isLeft ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal;
                    break;
                case YUCPFingerType.Middle:
                    proximalBone = isLeft ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal;
                    intermediateBone = isLeft ? HumanBodyBones.LeftMiddleIntermediate : HumanBodyBones.RightMiddleIntermediate;
                    distalBone = isLeft ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal;
                    break;
                case YUCPFingerType.Ring:
                    proximalBone = isLeft ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal;
                    intermediateBone = isLeft ? HumanBodyBones.LeftRingIntermediate : HumanBodyBones.RightRingIntermediate;
                    distalBone = isLeft ? HumanBodyBones.LeftRingDistal : HumanBodyBones.RightRingDistal;
                    break;
                case YUCPFingerType.Little:
                    proximalBone = isLeft ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal;
                    intermediateBone = isLeft ? HumanBodyBones.LeftLittleIntermediate : HumanBodyBones.RightLittleIntermediate;
                    distalBone = isLeft ? HumanBodyBones.LeftLittleDistal : HumanBodyBones.RightLittleDistal;
                    break;
                default:
                    return (null, null, null, null);
            }

            Transform proximal = animator.GetBoneTransform(proximalBone);
            Transform intermediate = animator.GetBoneTransform(intermediateBone);
            Transform distal = animator.GetBoneTransform(distalBone);

            return (null, proximal, intermediate, distal);
        }

        /// <summary>
        /// Detects universal local axes for a hand bone.
        /// </summary>
        public static YUCPUniversalLocalAxes DetectHandAxes(Transform handBone, Transform avatarRoot)
        {
            if (handBone == null) return new YUCPUniversalLocalAxes();
            return YUCPUniversalLocalAxes.FromTransform(handBone, avatarRoot);
        }

        /// <summary>
        /// Detects universal local axes for finger bones.
        /// Uses a simplified detection based on bone direction.
        /// </summary>
        public static YUCPUniversalLocalAxes DetectFingerAxes(Transform fingerBone, Transform parentBone)
        {
            if (fingerBone == null || parentBone == null)
            {
                return new YUCPUniversalLocalAxes();
            }

            Vector3 boneDirection = (fingerBone.position - parentBone.position).normalized;
            
            Vector3 localForward = fingerBone.InverseTransformDirection(boneDirection);
            Vector3 localUp = fingerBone.InverseTransformDirection(Vector3.up);
            Vector3 localRight = Vector3.Cross(localUp, localForward).normalized;
            
            if (localRight.magnitude < 0.1f)
            {
                localRight = fingerBone.InverseTransformDirection(Vector3.right);
            }

            return new YUCPUniversalLocalAxes(localRight, localUp, localForward);
        }
    }
}

