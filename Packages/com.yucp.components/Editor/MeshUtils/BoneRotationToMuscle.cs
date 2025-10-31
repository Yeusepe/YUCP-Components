using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Converts bone rotations from IK solutions to Unity humanoid muscle values.
    /// Essential for VRChat compatibility since animations must use muscle values, not bone rotations.
    /// </summary>
    public static class BoneRotationToMuscle
    {
        /// <summary>
        /// Convert a finger bone's local rotation to muscle value.
        /// </summary>
        /// <param name="currentRotation">Current bone rotation from IK solution</param>
        /// <param name="restRotation">Rest pose rotation of the bone</param>
        /// <param name="bendAxis">Axis around which the bone bends (typically local X)</param>
        /// <param name="minAngle">Minimum bend angle in degrees</param>
        /// <param name="maxAngle">Maximum bend angle in degrees</param>
        /// <returns>Muscle value (-1 to 1)</returns>
        public static float RotationToMuscleValue(
            Quaternion currentRotation,
            Quaternion restRotation,
            Vector3 bendAxis,
            float minAngle = -90f,
            float maxAngle = 90f)
        {
            // Calculate rotation difference
            Quaternion rotationDelta = currentRotation * Quaternion.Inverse(restRotation);
            
            // Extract angle around bend axis
            Vector3 axis;
            float angle;
            rotationDelta.ToAngleAxis(out angle, out axis);
            
            // Project angle onto bend axis
            float projectedAngle = Vector3.Dot(axis, bendAxis) * angle;
            
            // Normalize to muscle range
            float normalizedAngle = Mathf.Clamp(projectedAngle, minAngle, maxAngle);
            float muscleValue = Mathf.InverseLerp(minAngle, maxAngle, normalizedAngle);
            
            // Convert to muscle range (-1 to 1)
            return Mathf.Lerp(-1f, 1f, muscleValue);
        }

        /// <summary>
        /// Convert all finger bone rotations for one hand to muscle dictionary.
        /// </summary>
        /// <param name="animator">Avatar animator</param>
        /// <param name="isLeftHand">True for left hand, false for right hand</param>
        /// <param name="solvedRotations">Dictionary of bone rotations from IK solution</param>
        /// <returns>Dictionary of muscle names to values</returns>
        public static Dictionary<string, float> ConvertFingerRotationsToMuscles(
            Animator animator,
            bool isLeftHand,
            Dictionary<HumanBodyBones, Quaternion> solvedRotations)
        {
            var muscleValues = new Dictionary<string, float>();
            
            if (animator == null || !animator.avatar.isHuman)
            {
                Debug.LogWarning("[BoneRotationToMuscle] Invalid animator or non-human avatar");
                return muscleValues;
            }

            string handPrefix = isLeftHand ? "Left" : "Right";
            
            // Process each finger
            ProcessFingerMuscles(animator, handPrefix, "Thumb", solvedRotations, muscleValues);
            ProcessFingerMuscles(animator, handPrefix, "Index", solvedRotations, muscleValues);
            ProcessFingerMuscles(animator, handPrefix, "Middle", solvedRotations, muscleValues);
            ProcessFingerMuscles(animator, handPrefix, "Ring", solvedRotations, muscleValues);
            ProcessFingerMuscles(animator, handPrefix, "Little", solvedRotations, muscleValues);

            return muscleValues;
        }

        /// <summary>
        /// Process muscle values for a single finger.
        /// </summary>
        private static void ProcessFingerMuscles(
            Animator animator,
            string handPrefix,
            string fingerName,
            Dictionary<HumanBodyBones, Quaternion> solvedRotations,
            Dictionary<string, float> muscleValues)
        {
            // Get finger bone hierarchy
            var fingerBones = GetFingerBoneHierarchy(handPrefix, fingerName);
            
            for (int segment = 0; segment < fingerBones.Length; segment++)
            {
                var bone = fingerBones[segment];
                if (bone == HumanBodyBones.LastBone) continue; // Invalid bone
                
                // Get bone transform
                Transform boneTransform = animator.GetBoneTransform(bone);
                if (boneTransform == null) continue;
                
                // Get solved rotation
                if (!solvedRotations.ContainsKey(bone)) continue;
                Quaternion solvedRotation = solvedRotations[bone];
                
                // Get rest pose rotation (from avatar)
                Quaternion restRotation = GetRestPoseRotation(animator, bone);
                
                // Determine bend axis (typically local X for finger curl)
                Vector3 bendAxis = GetFingerBendAxis(fingerName, segment);
                
                // Get muscle limits from Unity's humanoid system
                float minAngle, maxAngle;
                GetMuscleLimits(handPrefix, fingerName, segment, out minAngle, out maxAngle);
                
                // Convert rotation to muscle value
                float muscleValue = RotationToMuscleValue(solvedRotation, restRotation, bendAxis, minAngle, maxAngle);
                
                // Get muscle name in animation format
                string muscleName = GetMuscleName(handPrefix, fingerName, segment + 1);
                if (!string.IsNullOrEmpty(muscleName))
                {
                    muscleValues[muscleName] = muscleValue;
                }
            }
        }

        /// <summary>
        /// Get finger bone hierarchy for a specific finger.
        /// </summary>
        private static HumanBodyBones[] GetFingerBoneHierarchy(string handPrefix, string fingerName)
        {
            if (handPrefix == "Left")
            {
                switch (fingerName)
                {
                    case "Thumb":
                        return new HumanBodyBones[] { 
                            HumanBodyBones.LeftThumbProximal, 
                            HumanBodyBones.LeftThumbIntermediate, 
                            HumanBodyBones.LeftThumbDistal 
                        };
                    case "Index":
                        return new HumanBodyBones[] { 
                            HumanBodyBones.LeftIndexProximal, 
                            HumanBodyBones.LeftIndexIntermediate, 
                            HumanBodyBones.LeftIndexDistal 
                        };
                    case "Middle":
                        return new HumanBodyBones[] { 
                            HumanBodyBones.LeftMiddleProximal, 
                            HumanBodyBones.LeftMiddleIntermediate, 
                            HumanBodyBones.LeftMiddleDistal 
                        };
                    case "Ring":
                        return new HumanBodyBones[] { 
                            HumanBodyBones.LeftRingProximal, 
                            HumanBodyBones.LeftRingIntermediate, 
                            HumanBodyBones.LeftRingDistal 
                        };
                    case "Little":
                        return new HumanBodyBones[] { 
                            HumanBodyBones.LeftLittleProximal, 
                            HumanBodyBones.LeftLittleIntermediate, 
                            HumanBodyBones.LeftLittleDistal 
                        };
                }
            }
            else // Right hand
            {
                switch (fingerName)
                {
                    case "Thumb":
                        return new HumanBodyBones[] { 
                            HumanBodyBones.RightThumbProximal, 
                            HumanBodyBones.RightThumbIntermediate, 
                            HumanBodyBones.RightThumbDistal 
                        };
                    case "Index":
                        return new HumanBodyBones[] { 
                            HumanBodyBones.RightIndexProximal, 
                            HumanBodyBones.RightIndexIntermediate, 
                            HumanBodyBones.RightIndexDistal 
                        };
                    case "Middle":
                        return new HumanBodyBones[] { 
                            HumanBodyBones.RightMiddleProximal, 
                            HumanBodyBones.RightMiddleIntermediate, 
                            HumanBodyBones.RightMiddleDistal 
                        };
                    case "Ring":
                        return new HumanBodyBones[] { 
                            HumanBodyBones.RightRingProximal, 
                            HumanBodyBones.RightRingIntermediate, 
                            HumanBodyBones.RightRingDistal 
                        };
                    case "Little":
                        return new HumanBodyBones[] { 
                            HumanBodyBones.RightLittleProximal, 
                            HumanBodyBones.RightLittleIntermediate, 
                            HumanBodyBones.RightLittleDistal 
                        };
                }
            }
            
            return new HumanBodyBones[0];
        }

        /// <summary>
        /// Get the bend axis for a finger segment.
        /// </summary>
        private static Vector3 GetFingerBendAxis(string fingerName, int segment)
        {
            // For most fingers, the primary bend axis is local X
            // Thumb might be different due to its orientation
            if (fingerName == "Thumb")
            {
                return segment == 0 ? Vector3.right : Vector3.forward; // Thumb proximal bends differently
            }
            else
            {
                return Vector3.right; // Standard finger curl axis
            }
        }

        /// <summary>
        /// Get muscle limits from Unity's humanoid system.
        /// </summary>
        private static void GetMuscleLimits(string handPrefix, string fingerName, int segment, out float minAngle, out float maxAngle)
        {
            // Default limits - these could be refined by querying Unity's muscle system
            minAngle = -90f; // Fully extended
            maxAngle = 90f;  // Fully curled
            
            // Thumb has different limits
            if (fingerName == "Thumb")
            {
                if (segment == 0) // Proximal
                {
                    minAngle = -45f;
                    maxAngle = 45f;
                }
                else // Intermediate/Distal
                {
                    minAngle = -60f;
                    maxAngle = 60f;
                }
            }
            else // Other fingers
            {
                if (segment == 0) // Proximal
                {
                    minAngle = -90f;
                    maxAngle = 90f;
                }
                else // Intermediate/Distal
                {
                    minAngle = -90f;
                    maxAngle = 90f;
                }
            }
        }

        /// <summary>
        /// Get rest pose rotation for a bone from the avatar.
        /// </summary>
        private static Quaternion GetRestPoseRotation(Animator animator, HumanBodyBones bone)
        {
            Transform boneTransform = animator.GetBoneTransform(bone);
            if (boneTransform == null) return Quaternion.identity;
            
            // For now, return identity - in a full implementation, this would
            // query the avatar's rest pose or T-pose
            return Quaternion.identity;
        }

        /// <summary>
        /// Get muscle name in Unity animation format.
        /// </summary>
        private static string GetMuscleName(string handPrefix, string fingerName, int segment)
        {
            string handPrefixAnim = handPrefix == "Left" ? "LeftHand" : "RightHand";
            return $"{handPrefixAnim}.{fingerName}.{segment} Stretched";
        }

        /// <summary>
        /// Debug method to log all available muscle names for a hand.
        /// </summary>
        public static void LogAvailableMuscles(Animator animator, bool isLeftHand)
        {
            if (animator == null || !animator.avatar.isHuman) return;
            
            string handPrefix = isLeftHand ? "Left" : "Right";
            Debug.Log($"[BoneRotationToMuscle] Available muscles for {handPrefix} hand:");
            
            string[] muscleNames = HumanTrait.MuscleName;
            foreach (string muscleName in muscleNames)
            {
                if (muscleName.Contains(handPrefix) && muscleName.Contains("Stretched"))
                {
                    Debug.Log($"  {muscleName}");
                }
            }
        }
    }
}





