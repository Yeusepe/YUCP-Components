using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Solves for muscle values that place finger tips at target positions.
    /// Uses inverse kinematics to calculate the required muscle values.
    /// </summary>
    public static class FingerTipSolver
    {
        public struct FingerTipTarget
        {
            public Vector3 thumbTip;
            public Vector3 indexTip;
            public Vector3 middleTip;
            public Vector3 ringTip;
            public Vector3 littleTip;
        }

        public struct FingerTipResult
        {
            public Dictionary<string, float> muscleValues;
            public bool success;
            public string errorMessage;
        }

        /// <summary>
        /// Solve for muscle values that place finger tips at target positions.
        /// </summary>
        public static FingerTipResult SolveFingerTips(Animator animator, FingerTipTarget targets, bool isLeftHand)
        {
            var result = new FingerTipResult
            {
                muscleValues = new Dictionary<string, float>(),
                success = false,
                errorMessage = ""
            };

            if (animator == null || !animator.avatar.isHuman)
            {
                result.errorMessage = "Invalid animator or non-human avatar";
                return result;
            }

            try
            {
                // Get current human pose
                var humanPose = new HumanPose();
                var humanPoseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
                humanPoseHandler.GetHumanPose(ref humanPose);

                // Solve for each finger
                SolveFingerMuscles(humanPose, targets.thumbTip, isLeftHand ? "Left" : "Right", "Thumb", result.muscleValues);
                SolveFingerMuscles(humanPose, targets.indexTip, isLeftHand ? "Left" : "Right", "Index", result.muscleValues);
                SolveFingerMuscles(humanPose, targets.middleTip, isLeftHand ? "Left" : "Right", "Middle", result.muscleValues);
                SolveFingerMuscles(humanPose, targets.ringTip, isLeftHand ? "Left" : "Right", "Ring", result.muscleValues);
                SolveFingerMuscles(humanPose, targets.littleTip, isLeftHand ? "Left" : "Right", "Little", result.muscleValues);

                result.success = true;
            }
            catch (System.Exception e)
            {
                result.errorMessage = $"Finger tip solving failed: {e.Message}";
            }

            return result;
        }

        private static void SolveFingerMuscles(HumanPose humanPose, Vector3 targetTip, string hand, string finger, Dictionary<string, float> muscleValues)
        {
            if (targetTip == Vector3.zero) return; // Skip if not set

            // Get finger bone positions from current pose
            var fingerBones = GetFingerBonePositions(humanPose, hand, finger);
            if (fingerBones.Count == 0) return;

            // Calculate distances from each bone to target
            var distances = new List<float>();
            foreach (var bone in fingerBones)
            {
                distances.Add(Vector3.Distance(bone, targetTip));
            }

            // Simple heuristic: closer bones should have more curl
            // This is a simplified approach - a full IK solver would be more complex
            for (int i = 0; i < fingerBones.Count; i++)
            {
                float distance = distances[i];
                float normalizedDistance = Mathf.Clamp01(distance / 0.1f); // Normalize to 10cm range
                
                // Convert distance to muscle value (closer = more curl)
                float muscleValue = Mathf.Lerp(1f, 0f, normalizedDistance);
                
                // Apply to appropriate muscle
                string muscleName = GetFingerMuscleName(hand, finger, i + 1);
                if (!string.IsNullOrEmpty(muscleName))
                {
                    muscleValues[muscleName] = muscleValue;
                }
            }
        }

        private static List<Vector3> GetFingerBonePositions(HumanPose humanPose, string hand, string finger)
        {
            // This is a simplified version - in a real implementation, you'd need to:
            // 1. Get the actual bone transforms from the avatar
            // 2. Calculate their world positions
            // 3. Return the positions
            
            // For now, return empty list - this would need proper bone tracking
            return new List<Vector3>();
        }

        private static string GetFingerMuscleName(string hand, string finger, int segment)
        {
            // Convert to the format expected by the animation system
            string handPrefix = hand == "Left" ? "LeftHand" : "RightHand";
            return $"{handPrefix}.{finger}.{segment} Stretched";
        }

        /// <summary>
        /// Get finger tip positions from current avatar pose.
        /// </summary>
        public static FingerTipTarget GetCurrentFingerTips(Animator animator, bool isLeftHand)
        {
            var targets = new FingerTipTarget();
            
            if (animator == null || !animator.avatar.isHuman)
                return targets;

            // This would need to be implemented to get actual finger tip positions
            // from the current avatar pose. For now, return zero positions.
            
            return targets;
        }

        /// <summary>
        /// Initialize finger tip positions to reasonable defaults based on object.
        /// </summary>
        public static FingerTipTarget InitializeFingerTips(Transform grippedObject, bool isLeftHand)
        {
            var targets = new FingerTipTarget();
            
            if (grippedObject == null)
                return targets;

            Vector3 objectCenter = grippedObject.position;
            Vector3 objectSize = GetObjectSize(grippedObject);
            
            // Calculate default positions around the object
            float gripRadius = Mathf.Max(objectSize.x, objectSize.y, objectSize.z) * 0.6f;
            
            if (isLeftHand)
            {
                // Left hand positions (mirror these for right hand)
                targets.thumbTip = objectCenter + Vector3.left * gripRadius * 0.8f + Vector3.up * gripRadius * 0.3f;
                targets.indexTip = objectCenter + Vector3.left * gripRadius + Vector3.up * gripRadius * 0.1f;
                targets.middleTip = objectCenter + Vector3.left * gripRadius * 1.1f;
                targets.ringTip = objectCenter + Vector3.left * gripRadius * 1.2f + Vector3.down * gripRadius * 0.1f;
                targets.littleTip = objectCenter + Vector3.left * gripRadius * 1.3f + Vector3.down * gripRadius * 0.2f;
            }
            else
            {
                // Right hand positions
                targets.thumbTip = objectCenter + Vector3.right * gripRadius * 0.8f + Vector3.up * gripRadius * 0.3f;
                targets.indexTip = objectCenter + Vector3.right * gripRadius + Vector3.up * gripRadius * 0.1f;
                targets.middleTip = objectCenter + Vector3.right * gripRadius * 1.1f;
                targets.ringTip = objectCenter + Vector3.right * gripRadius * 1.2f + Vector3.down * gripRadius * 0.1f;
                targets.littleTip = objectCenter + Vector3.right * gripRadius * 1.3f + Vector3.down * gripRadius * 0.2f;
            }
            
            return targets;
        }

        private static Vector3 GetObjectSize(Transform obj)
        {
            // Try to get size from collider first
            var collider = obj.GetComponent<Collider>();
            if (collider != null)
            {
                return collider.bounds.size;
            }

            // Try to get size from renderer
            var renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
            {
                return renderer.bounds.size;
            }

            // Default size
            return Vector3.one * 0.1f;
        }
    }
}


