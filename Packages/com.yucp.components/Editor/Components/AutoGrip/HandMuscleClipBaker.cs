using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using YUCP.Components.HandPoses;

namespace YUCP.Components.Editor.AutoGrip
{
    /// <summary>
    /// Bakes hand poses to AnimationClips containing only hand/finger muscle curves.
    /// Uses patterns from AvatarMusclePoserDataEditor.
    /// </summary>
    public static class HandMuscleClipBaker
    {
        // Muscle name prefixes for filtering
        private static readonly string[] LeftHandMusclePrefixes = {
            "Left Thumb", "Left Index", "Left Middle", "Left Ring", "Left Little"
        };
        private static readonly string[] RightHandMusclePrefixes = {
            "Right Thumb", "Right Index", "Right Middle", "Right Ring", "Right Little"
        };

        /// <summary>
        /// Bakes a hand pose to an AnimationClip file.
        /// </summary>
        /// <param name="animator">Avatar animator with humanoid rig</param>
        /// <param name="handPose">Hand pose descriptor</param>
        /// <param name="isLeftHand">True for left hand, false for right</param>
        /// <param name="savePath">Asset path to save clip (Assets/...)</param>
        /// <returns>Created AnimationClip, or null on failure</returns>
        public static AnimationClip BakeHandMuscles(
            Animator animator,
            YUCPHandDescriptor handPose,
            bool isLeftHand,
            string savePath)
        {
            if (animator == null || !animator.isHuman)
            {
                Debug.LogError("[HandMuscleClipBaker] Animator is null or not humanoid");
                return null;
            }

            if (handPose == null)
            {
                Debug.LogError("[HandMuscleClipBaker] Hand pose is null");
                return null;
            }

            // Apply pose to bones temporarily
            var originalPose = CaptureCurrentPose(animator);
            ApplyHandPose(animator, handPose, isLeftHand);

            try
            {
                // Read muscle values
                var muscleValues = ReadMuscleValues(animator);

                // Create animation clip
                var clip = CreateMuscleAnimationClip(muscleValues, isLeftHand);

                // Ensure directory exists
                string directory = System.IO.Path.GetDirectoryName(savePath);
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                // Save or update existing
                var existingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(savePath);
                if (existingClip != null)
                {
                    EditorUtility.CopySerialized(clip, existingClip);
                    Object.DestroyImmediate(clip);
                    clip = existingClip;
                    EditorUtility.SetDirty(clip);
                }
                else
                {
                    AssetDatabase.CreateAsset(clip, savePath);
                }

                AssetDatabase.SaveAssets();
                Debug.Log($"[HandMuscleClipBaker] Saved {(isLeftHand ? "left" : "right")} hand clip to {savePath}");

                return clip;
            }
            finally
            {
                // Restore original pose
                RestorePose(animator, originalPose);
            }
        }

        /// <summary>
        /// Gets the cached muscle indices for hand muscles.
        /// </summary>
        public static int[] GetHandMuscleIndices(bool isLeftHand)
        {
            var indices = new List<int>();
            string[] prefixes = isLeftHand ? LeftHandMusclePrefixes : RightHandMusclePrefixes;
            string[] allMuscleNames = HumanTrait.MuscleName;

            for (int i = 0; i < allMuscleNames.Length; i++)
            {
                foreach (var prefix in prefixes)
                {
                    if (allMuscleNames[i].StartsWith(prefix))
                    {
                        indices.Add(i);
                        break;
                    }
                }
            }

            return indices.ToArray();
        }

        private static Dictionary<int, float> ReadMuscleValues(Animator animator)
        {
            var poseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
            var humanPose = new HumanPose();
            poseHandler.GetHumanPose(ref humanPose);

            var muscleValues = new Dictionary<int, float>();
            for (int i = 0; i < humanPose.muscles.Length; i++)
            {
                muscleValues[i] = humanPose.muscles[i];
            }

            return muscleValues;
        }

        private static AnimationClip CreateMuscleAnimationClip(Dictionary<int, float> muscleValues, bool isLeftHand)
        {
            var clip = new AnimationClip();
            clip.name = isLeftHand ? "LeftHandGrip" : "RightHandGrip";

            string[] muscleNames = HumanTrait.MuscleName;
            int[] handIndices = GetHandMuscleIndices(isLeftHand);

            foreach (int i in handIndices)
            {
                if (i >= muscleNames.Length || !muscleValues.ContainsKey(i)) continue;

                float value = muscleValues[i];
                if (Mathf.Abs(value) < 0.001f) continue; // Skip near-zero values

                string animFormatName = ConvertToAnimFormat(muscleNames[i]);
                if (string.IsNullOrEmpty(animFormatName)) continue;

                // Create curve with two keyframes (Unity requirement)
                var curve = AnimationCurve.Constant(0f, 0.001f, value);
                var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), animFormatName);
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }

            return clip;
        }

        /// <summary>
        /// Converts HumanTrait muscle name to Animation property format.
        /// Adapted from AvatarMusclePoserDataEditor.ConvertToAnimFormat.
        /// </summary>
        private static string ConvertToAnimFormat(string humanTraitName)
        {
            if (string.IsNullOrEmpty(humanTraitName)) return null;

            string result = humanTraitName;

            // Hand muscle name conversions
            result = result.Replace("Left Thumb", "LeftHand.Thumb");
            result = result.Replace("Left Index", "LeftHand.Index");
            result = result.Replace("Left Middle", "LeftHand.Middle");
            result = result.Replace("Left Ring", "LeftHand.Ring");
            result = result.Replace("Left Little", "LeftHand.Little");

            result = result.Replace("Right Thumb", "RightHand.Thumb");
            result = result.Replace("Right Index", "RightHand.Index");
            result = result.Replace("Right Middle", "RightHand.Middle");
            result = result.Replace("Right Ring", "RightHand.Ring");
            result = result.Replace("Right Little", "RightHand.Little");

            // Segment number conversions
            result = result.Replace(" 1 ", ".1 ");
            result = result.Replace(" 2 ", ".2 ");
            result = result.Replace(" 3 ", ".3 ");
            result = result.Replace(" Spread", ".Spread");

            return result;
        }

        private static void ApplyHandPose(Animator animator, YUCPHandDescriptor handPose, bool isLeftHand)
        {
            var handSide = isLeftHand ? YUCPHandSide.Left : YUCPHandSide.Right;

            // Convert to runtime descriptor for application
            var runtimeDescriptor = ConvertToRuntimeDescriptor(animator, handSide, handPose);
            YUCPHandPoseApplier.UpdateHandUsingRuntimeDescriptor(animator, handSide, runtimeDescriptor);
        }

        private static YUCPRuntimeHandDescriptor ConvertToRuntimeDescriptor(Animator animator, YUCPHandSide handSide, YUCPHandDescriptor handPose)
        {
            if (animator == null || handPose == null)
            {
                return new YUCPRuntimeHandDescriptor();
            }

            // Use default local axes for conversion
            var handLocalAxes = new YUCPUniversalLocalAxes();
            var fingerLocalAxes = new YUCPUniversalLocalAxes();

            var runtime = new YUCPRuntimeHandDescriptor();

            // Create runtime finger descriptors using the proper constructor
            var thumbRuntime = new YUCPRuntimeFingerDescriptor(animator, handSide, handPose, HandPoses.YUCPFingerType.Thumb, handLocalAxes, fingerLocalAxes);
            var indexRuntime = new YUCPRuntimeFingerDescriptor(animator, handSide, handPose, HandPoses.YUCPFingerType.Index, handLocalAxes, fingerLocalAxes);
            var middleRuntime = new YUCPRuntimeFingerDescriptor(animator, handSide, handPose, HandPoses.YUCPFingerType.Middle, handLocalAxes, fingerLocalAxes);
            var ringRuntime = new YUCPRuntimeFingerDescriptor(animator, handSide, handPose, HandPoses.YUCPFingerType.Ring, handLocalAxes, fingerLocalAxes);
            var littleRuntime = new YUCPRuntimeFingerDescriptor(animator, handSide, handPose, HandPoses.YUCPFingerType.Little, handLocalAxes, fingerLocalAxes);

            runtime.Thumb.CopyFrom(thumbRuntime);
            runtime.Index.CopyFrom(indexRuntime);
            runtime.Middle.CopyFrom(middleRuntime);
            runtime.Ring.CopyFrom(ringRuntime);
            runtime.Little.CopyFrom(littleRuntime);

            return runtime;
        }

        private static Dictionary<HumanBodyBones, Quaternion> CaptureCurrentPose(Animator animator)
        {
            var pose = new Dictionary<HumanBodyBones, Quaternion>();

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = (HumanBodyBones)i;
                var transform = animator.GetBoneTransform(bone);
                if (transform != null)
                {
                    pose[bone] = transform.localRotation;
                }
            }

            return pose;
        }

        private static void RestorePose(Animator animator, Dictionary<HumanBodyBones, Quaternion> pose)
        {
            foreach (var kvp in pose)
            {
                var transform = animator.GetBoneTransform(kvp.Key);
                if (transform != null)
                {
                    transform.localRotation = kvp.Value;
                }
            }
        }

        /// <summary>
        /// Generates stable asset path based on avatar and prop GUIDs.
        /// </summary>
        public static string GenerateClipPath(GameObject avatar, GameObject prop, bool isLeftHand)
        {
            string avatarGuid = GetStableGuid(avatar);
            string propGuid = GetStableGuid(prop);
            string handSide = isLeftHand ? "Left" : "Right";

            return $"Assets/YUCP/AutoGrip/Generated/{avatarGuid}/{propGuid}/{handSide}HandGrip.anim";
        }

        private static string GetStableGuid(GameObject obj)
        {
            if (obj == null) return "unknown";

            // Try to get prefab GUID
            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath))
            {
                return AssetDatabase.AssetPathToGUID(assetPath);
            }

            // Fallback to instance ID
            return obj.GetInstanceID().ToString();
        }
    }
}
