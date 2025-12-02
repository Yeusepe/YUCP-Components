// Generates AnimationClips with muscle curves from hand poses
// Converts bone rotations to Unity humanoid muscle values for VRChat compatibility

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.HandPoses
{
    /// <summary>
    /// Generates AnimationClips with muscle curves from hand pose assets.
    /// Converts hand pose bone rotations to Unity humanoid muscle values.
    /// </summary>
    public static class YUCPGripAnimationGenerator
    {
        /// <summary>
        /// Generates an AnimationClip with muscle curves from a hand pose asset.
        /// </summary>
        /// <param name="animator">Avatar animator</param>
        /// <param name="handPoseAsset">Hand pose asset to convert</param>
        /// <param name="grippableData">Grippable data containing settings</param>
        /// <param name="isLeftHand">True for left hand, false for right hand</param>
        /// <returns>AnimationClip with muscle curves, or null if generation failed</returns>
        public static AnimationClip GenerateGripAnimation(
            Animator animator,
            YUCPHandPoseAsset handPoseAsset,
            YUCPGrippableData grippableData,
            bool isLeftHand)
        {
            if (animator == null || handPoseAsset == null || grippableData == null)
            {
                Debug.LogError("[YUCPGripAnimationGenerator] Missing required parameters");
                return null;
            }

            if (!animator.avatar.isHuman)
            {
                Debug.LogError("[YUCPGripAnimationGenerator] Avatar must be humanoid");
                return null;
            }

            // Detect axes for the avatar
            Transform avatarRoot = animator.transform;
            Transform handBone = YUCPAvatarRigHelper.GetWrist(animator, isLeftHand ? YUCPHandSide.Left : YUCPHandSide.Right);
            if (handBone == null)
            {
                Debug.LogError("[YUCPGripAnimationGenerator] Could not find hand bone");
                return null;
            }

            YUCPUniversalLocalAxes handLocalAxes = YUCPAvatarRigHelper.DetectHandAxes(handBone, avatarRoot);
            YUCPUniversalLocalAxes fingerLocalAxes = new YUCPUniversalLocalAxes(Vector3.right, Vector3.up, Vector3.forward);

            // Get hand descriptor using pose type and blend value
            YUCPHandSide handSide = isLeftHand ? YUCPHandSide.Left : YUCPHandSide.Right;
            YUCPHandDescriptor handDescriptor = null;

            if (handPoseAsset.PoseType == YUCPHandPoseType.Blend)
            {
                // Interpolate between open and closed using blend value
                YUCPHandDescriptor openDescriptor = handPoseAsset.GetHandDescriptor(handSide, YUCPHandPoseType.Blend, YUCPBlendPoseType.OpenGrip);
                YUCPHandDescriptor closedDescriptor = handPoseAsset.GetHandDescriptor(handSide, YUCPHandPoseType.Blend, YUCPBlendPoseType.ClosedGrip);
                
                if (openDescriptor != null && closedDescriptor != null)
                {
                    handDescriptor = new YUCPHandDescriptor();
                    handDescriptor.CopyFrom(openDescriptor);
                    handDescriptor.InterpolateTo(closedDescriptor, grippableData.blendValue);
                }
            }
            else
            {
                handDescriptor = handPoseAsset.GetHandDescriptor(handSide);
            }

            if (handDescriptor == null)
            {
                Debug.LogError("[YUCPGripAnimationGenerator] Could not get hand descriptor");
                return null;
            }

            // Convert hand descriptor to runtime descriptor (bone rotations)
            YUCPRuntimeHandDescriptor runtimeDescriptor = new YUCPRuntimeHandDescriptor(
                animator, handSide, handPoseAsset, handPoseAsset.PoseType, 
                handPoseAsset.PoseType == YUCPHandPoseType.Blend ? (grippableData.blendValue > 0.5f ? YUCPBlendPoseType.ClosedGrip : YUCPBlendPoseType.OpenGrip) : YUCPBlendPoseType.None,
                handLocalAxes, fingerLocalAxes);

            // Convert bone rotations to muscle values
            Dictionary<HumanBodyBones, Quaternion> boneRotations = ConvertRuntimeDescriptorToBoneRotations(animator, handSide, runtimeDescriptor);
            Dictionary<string, float> muscleValues = BoneRotationToMuscle.ConvertFingerRotationsToMuscles(animator, isLeftHand, boneRotations);

            // Create animation clip with muscle curves
            AnimationClip clip = CreateAnimationClip(muscleValues, isLeftHand);
            
            return clip;
        }

        /// <summary>
        /// Converts runtime hand descriptor to bone rotations dictionary.
        /// </summary>
        private static Dictionary<HumanBodyBones, Quaternion> ConvertRuntimeDescriptorToBoneRotations(
            Animator animator,
            YUCPHandSide handSide,
            YUCPRuntimeHandDescriptor runtimeDescriptor)
        {
            var rotations = new Dictionary<HumanBodyBones, Quaternion>();

            // Process each finger
            ProcessFingerRotations(animator, handSide, YUCPFingerType.Thumb, runtimeDescriptor.Thumb, rotations);
            ProcessFingerRotations(animator, handSide, YUCPFingerType.Index, runtimeDescriptor.Index, rotations);
            ProcessFingerRotations(animator, handSide, YUCPFingerType.Middle, runtimeDescriptor.Middle, rotations);
            ProcessFingerRotations(animator, handSide, YUCPFingerType.Ring, runtimeDescriptor.Ring, rotations);
            ProcessFingerRotations(animator, handSide, YUCPFingerType.Little, runtimeDescriptor.Little, rotations);

            return rotations;
        }

        /// <summary>
        /// Processes finger rotations and adds them to the dictionary.
        /// Runtime descriptor already contains local rotations, so we use them directly.
        /// </summary>
        private static void ProcessFingerRotations(
            Animator animator,
            YUCPHandSide handSide,
            YUCPFingerType fingerType,
            YUCPRuntimeFingerDescriptor fingerDescriptor,
            Dictionary<HumanBodyBones, Quaternion> rotations)
        {
            if (fingerDescriptor == null) return;

            var (metacarpal, proximal, intermediate, distal) = YUCPAvatarRigHelper.GetFingerBones(animator, handSide, fingerType);

            // Runtime descriptor already contains local rotations, use them directly
            if (fingerDescriptor.HasMetacarpalInfo && metacarpal != null)
            {
                rotations[GetBoneForFinger(handSide, fingerType, 0)] = fingerDescriptor.MetacarpalRotation;
            }

            if (proximal != null)
            {
                rotations[GetBoneForFinger(handSide, fingerType, 1)] = fingerDescriptor.ProximalRotation;
            }

            if (intermediate != null)
            {
                rotations[GetBoneForFinger(handSide, fingerType, 2)] = fingerDescriptor.IntermediateRotation;
            }

            if (distal != null)
            {
                rotations[GetBoneForFinger(handSide, fingerType, 3)] = fingerDescriptor.DistalRotation;
            }
        }

        /// <summary>
        /// Gets HumanBodyBones enum for a finger segment.
        /// </summary>
        private static HumanBodyBones GetBoneForFinger(YUCPHandSide handSide, YUCPFingerType fingerType, int segment)
        {
            bool isLeft = handSide == YUCPHandSide.Left;

            switch (fingerType)
            {
                case YUCPFingerType.Thumb:
                    switch (segment)
                    {
                        case 1: return isLeft ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal;
                        case 2: return isLeft ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate;
                        case 3: return isLeft ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal;
                    }
                    break;
                case YUCPFingerType.Index:
                    switch (segment)
                    {
                        case 1: return isLeft ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal;
                        case 2: return isLeft ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate;
                        case 3: return isLeft ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal;
                    }
                    break;
                case YUCPFingerType.Middle:
                    switch (segment)
                    {
                        case 1: return isLeft ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal;
                        case 2: return isLeft ? HumanBodyBones.LeftMiddleIntermediate : HumanBodyBones.RightMiddleIntermediate;
                        case 3: return isLeft ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal;
                    }
                    break;
                case YUCPFingerType.Ring:
                    switch (segment)
                    {
                        case 1: return isLeft ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal;
                        case 2: return isLeft ? HumanBodyBones.LeftRingIntermediate : HumanBodyBones.RightRingIntermediate;
                        case 3: return isLeft ? HumanBodyBones.LeftRingDistal : HumanBodyBones.RightRingDistal;
                    }
                    break;
                case YUCPFingerType.Little:
                    switch (segment)
                    {
                        case 1: return isLeft ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal;
                        case 2: return isLeft ? HumanBodyBones.LeftLittleIntermediate : HumanBodyBones.RightLittleIntermediate;
                        case 3: return isLeft ? HumanBodyBones.LeftLittleDistal : HumanBodyBones.RightLittleDistal;
                    }
                    break;
            }

            return HumanBodyBones.LastBone;
        }

        /// <summary>
        /// Creates an AnimationClip with muscle curves.
        /// </summary>
        private static AnimationClip CreateAnimationClip(Dictionary<string, float> muscleValues, bool isLeftHand)
        {
            AnimationClip clip = new AnimationClip();
            clip.name = $"YUCPGrip_{(isLeftHand ? "Left" : "Right")}";
            clip.legacy = false;

            foreach (var muscleEntry in muscleValues)
            {
                string muscleName = muscleEntry.Key;
                float value = muscleEntry.Value;

                AnimationCurve curve = AnimationCurve.Constant(0f, 0f, value);

                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                    "",                    // Path (empty for Animator root)
                    typeof(Animator),      // Component type
                    muscleName             // Property name
                );

                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }

            return clip;
        }
    }
}

