using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Solves for muscle values that place finger tips at target positions.
    /// Uses FABRIK IK solver to calculate bone rotations, then converts to muscle values.
    /// </summary>
    public static class FingerTipSolver
    {
        /// <summary>
        /// Debug flag to disable rotation application entirely.
        /// </summary>
        public static bool DisableRotationApplication = false;
        
        public struct FingerTipTarget
        {
            public Vector3 thumbTip;
            public Vector3 indexTip;
            public Vector3 middleTip;
            public Vector3 ringTip;
            public Vector3 littleTip;
            
            public Quaternion thumbRotation;
            public Quaternion indexRotation;
            public Quaternion middleRotation;
            public Quaternion ringRotation;
            public Quaternion littleRotation;
        }

        public struct FingerTipResult
        {
            public Dictionary<string, float> muscleValues;
            public bool success;
            public string errorMessage;
            public Dictionary<HumanBodyBones, Quaternion> solvedRotations;
        }

        /// <summary>
        /// Solve for muscle values that place finger tips at target positions using IK.
        /// </summary>
        public static FingerTipResult SolveFingerTips(Animator animator, FingerTipTarget targets, bool isLeftHand)
        {
            var result = new FingerTipResult
            {
                muscleValues = new Dictionary<string, float>(),
                solvedRotations = new Dictionary<HumanBodyBones, Quaternion>(),
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
                // Solve IK for each finger with target rotations
                SolveFingerIK(animator, targets.thumbTip, targets.thumbRotation, isLeftHand, "Thumb", result.solvedRotations);
                SolveFingerIK(animator, targets.indexTip, targets.indexRotation, isLeftHand, "Index", result.solvedRotations);
                SolveFingerIK(animator, targets.middleTip, targets.middleRotation, isLeftHand, "Middle", result.solvedRotations);
                SolveFingerIK(animator, targets.ringTip, targets.ringRotation, isLeftHand, "Ring", result.solvedRotations);
                SolveFingerIK(animator, targets.littleTip, targets.littleRotation, isLeftHand, "Little", result.solvedRotations);

                // Convert bone rotations to muscle values
                result.muscleValues = BoneRotationToMuscle.ConvertFingerRotationsToMuscles(
                    animator, isLeftHand, result.solvedRotations);

                result.success = true;
            }
            catch (System.Exception e)
            {
                result.errorMessage = $"Finger tip solving failed: {e.Message}";
            }

            return result;
        }

        /// <summary>
        /// Solve IK for a single finger using FABRIK algorithm with target rotation.
        /// </summary>
        private static void SolveFingerIK(Animator animator, Vector3 targetTip, Quaternion targetRotation, bool isLeftHand, string fingerName, Dictionary<HumanBodyBones, Quaternion> solvedRotations)
        {
            if (targetTip == Vector3.zero) return; // Skip if not set

            // Get finger bone chain using dynamic discovery
            var fingerBones = GetFingerBoneChainDynamic(animator, isLeftHand, fingerName);
            if (fingerBones.Length == 0) 
            {
                Debug.LogWarning($"[FingerTipSolver] Dynamic discovery failed for {fingerName}, trying fallback method");
                // Fallback to hardcoded method
                fingerBones = GetFingerBoneChain(isLeftHand, fingerName);
                if (fingerBones.Length == 0)
                {
                    Debug.LogWarning($"[FingerTipSolver] No bones found for {fingerName} {(isLeftHand ? "left" : "right")} hand");
                    return;
                }
            }

            // Get bone transforms
            var boneTransforms = new Transform[fingerBones.Length];
            for (int i = 0; i < fingerBones.Length; i++)
            {
                boneTransforms[i] = animator.GetBoneTransform(fingerBones[i]);
                if (boneTransforms[i] == null)
                {
                    Debug.LogWarning($"[FingerTipSolver] Could not find bone transform for {fingerBones[i]}");
                    return;
                }
            }
            
            // Calculate chain length for debugging
            float chainLength = 0f;
            for (int i = 0; i < boneTransforms.Length - 1; i++)
            {
                chainLength += Vector3.Distance(boneTransforms[i].position, boneTransforms[i + 1].position);
            }
            float targetDistance = Vector3.Distance(boneTransforms[0].position, targetTip);
            
            Debug.Log($"[FingerTipSolver] Solving IK for {fingerName} {(isLeftHand ? "left" : "right")} hand: " +
                     $"Target: {targetTip}, Bones: {fingerBones.Length}, " +
                     $"Chain length: {chainLength * 100f:F1}cm, Target distance: {targetDistance * 100f:F1}cm, " +
                     $"Chain: {string.Join(" -> ", fingerBones.Select(b => b.ToString()))}");

            // Validate bone chain before solving IK
            if (!ValidateBoneChain(boneTransforms, fingerName))
            {
                Debug.LogWarning($"[FingerTipSolver] Bone chain validation failed for {fingerName}");
                return;
            }

            // Solve IK using FABRIK for position
            var ikResult = FastIKFabric.SolveFingerChain(boneTransforms, targetTip, 10, 0.001f);
            
            if (!ikResult.success)
            {
                Debug.LogWarning($"[FingerTipSolver] IK solving failed for {fingerName}: {ikResult.errorMessage}");
                
                // Fallback: apply a basic finger curl pose
                ApplyFallbackFingerPose(fingerBones, solvedRotations, fingerName);
                return;
            }
            
            Debug.Log($"[FingerTipSolver] IK solved successfully for {fingerName}: " +
                     $"Final error: {ikResult.finalError:F4}, Rotations: {ikResult.solvedRotations.Length}");

            // Store solved rotations
            for (int i = 0; i < fingerBones.Length; i++)
            {
                solvedRotations[fingerBones[i]] = ikResult.solvedRotations[i];
            }
            
            // Apply target rotation more conservatively - only to the tip bone and only if significantly different
            if (!DisableRotationApplication && fingerBones.Length > 0 && targetRotation != Quaternion.identity)
            {
                HumanBodyBones distalBone = fingerBones[fingerBones.Length - 1];
                Quaternion ikRotation = ikResult.solvedRotations[fingerBones.Length - 1];
                
                // Only apply rotation if it's significantly different from identity
                float rotationAngle = Quaternion.Angle(targetRotation, Quaternion.identity);
                if (rotationAngle > 10f) // Only if rotation is more than 10 degrees
                {
                    // Apply a very conservative blend to avoid breaking the finger
                    Quaternion conservativeRotation = Quaternion.Slerp(ikRotation, targetRotation, 0.2f);
                    solvedRotations[distalBone] = conservativeRotation;
                    
                    Debug.Log($"[FingerTipSolver] Applied conservative rotation to {fingerName} distal bone: " +
                             $"Angle: {rotationAngle:F1}°, Blend: 20%");
                }
            }
            else if (DisableRotationApplication)
            {
                Debug.Log($"[FingerTipSolver] Rotation application disabled for debugging - {fingerName}");
            }
        }

        /// <summary>
        /// Apply a basic fallback finger pose when IK fails.
        /// </summary>
        private static void ApplyFallbackFingerPose(HumanBodyBones[] fingerBones, Dictionary<HumanBodyBones, Quaternion> solvedRotations, string fingerName)
        {
            Debug.Log($"[FingerTipSolver] Applying fallback pose for {fingerName}");
            
            // Apply basic finger curl rotations
            for (int i = 0; i < fingerBones.Length; i++)
            {
                // Basic finger curl - each joint bends progressively more
                float curlAngle = (i + 1) * 15f; // 15, 30, 45 degrees
                Quaternion curlRotation = Quaternion.AngleAxis(curlAngle, Vector3.right);
                
                solvedRotations[fingerBones[i]] = curlRotation;
            }
        }

        /// <summary>
        /// Validate that the bone chain is properly connected and has reasonable lengths.
        /// </summary>
        private static bool ValidateBoneChain(Transform[] boneTransforms, string fingerName)
        {
            if (boneTransforms.Length < 2) return false;
            
            float totalLength = 0f;
            for (int i = 0; i < boneTransforms.Length - 1; i++)
            {
                float boneLength = Vector3.Distance(boneTransforms[i].position, boneTransforms[i + 1].position);
                totalLength += boneLength;
                
                // Check for reasonable bone lengths (should be 1-10cm for fingers)
                if (boneLength < 0.001f || boneLength > 0.1f)
                {
                    Debug.LogWarning($"[FingerTipSolver] Unusual bone length for {fingerName} bone {i}: {boneLength * 100f:F1}cm");
                    return false;
                }
            }
            
            Debug.Log($"[FingerTipSolver] {fingerName} bone chain validated: {boneTransforms.Length} bones, total length: {totalLength * 100f:F1}cm");
            return true;
        }

        /// <summary>
        /// Get ALL bones in a finger chain using hardcoded fallback (for when dynamic discovery fails).
        /// </summary>
        private static HumanBodyBones[] GetFingerBoneChain(bool isLeftHand, string fingerName)
        {
            if (isLeftHand)
            {
                return fingerName switch
                {
                    "Thumb" => new HumanBodyBones[] { 
                        HumanBodyBones.LeftThumbProximal, 
                        HumanBodyBones.LeftThumbIntermediate, 
                        HumanBodyBones.LeftThumbDistal 
                    },
                    "Index" => new HumanBodyBones[] { 
                        HumanBodyBones.LeftIndexProximal, 
                        HumanBodyBones.LeftIndexIntermediate, 
                        HumanBodyBones.LeftIndexDistal 
                    },
                    "Middle" => new HumanBodyBones[] { 
                        HumanBodyBones.LeftMiddleProximal, 
                        HumanBodyBones.LeftMiddleIntermediate, 
                        HumanBodyBones.LeftMiddleDistal 
                    },
                    "Ring" => new HumanBodyBones[] { 
                        HumanBodyBones.LeftRingProximal, 
                        HumanBodyBones.LeftRingIntermediate, 
                        HumanBodyBones.LeftRingDistal 
                    },
                    "Little" => new HumanBodyBones[] { 
                        HumanBodyBones.LeftLittleProximal, 
                        HumanBodyBones.LeftLittleIntermediate, 
                        HumanBodyBones.LeftLittleDistal 
                    },
                    _ => new HumanBodyBones[0]
                };
            }
            else // Right hand
            {
                return fingerName switch
                {
                    "Thumb" => new HumanBodyBones[] { 
                        HumanBodyBones.RightThumbProximal, 
                        HumanBodyBones.RightThumbIntermediate, 
                        HumanBodyBones.RightThumbDistal 
                    },
                    "Index" => new HumanBodyBones[] { 
                        HumanBodyBones.RightIndexProximal, 
                        HumanBodyBones.RightIndexIntermediate, 
                        HumanBodyBones.RightIndexDistal 
                    },
                    "Middle" => new HumanBodyBones[] { 
                        HumanBodyBones.RightMiddleProximal, 
                        HumanBodyBones.RightMiddleIntermediate, 
                        HumanBodyBones.RightMiddleDistal 
                    },
                    "Ring" => new HumanBodyBones[] { 
                        HumanBodyBones.RightRingProximal, 
                        HumanBodyBones.RightRingIntermediate, 
                        HumanBodyBones.RightRingDistal 
                    },
                    "Little" => new HumanBodyBones[] { 
                        HumanBodyBones.RightLittleProximal, 
                        HumanBodyBones.RightLittleIntermediate, 
                        HumanBodyBones.RightLittleDistal 
                    },
                    _ => new HumanBodyBones[0]
                };
            }
        }
        
        /// <summary>
        /// Dynamically discover finger bone chain by traversing the actual bone hierarchy.
        /// This works with any humanoid armature structure.
        /// </summary>
        private static HumanBodyBones[] GetFingerBoneChainDynamic(Animator animator, bool isLeftHand, string fingerName)
        {
            // Get the root bone for this finger
            HumanBodyBones rootBone = GetFingerRootBone(isLeftHand, fingerName);
            if (rootBone == HumanBodyBones.LastBone)
            {
                Debug.LogWarning($"[FingerTipSolver] Could not find root bone for {fingerName} {(isLeftHand ? "left" : "right")} hand");
                return new HumanBodyBones[0];
            }
            
            // Get the root transform
            Transform rootTransform = animator.GetBoneTransform(rootBone);
            if (rootTransform == null)
            {
                Debug.LogWarning($"[FingerTipSolver] Root transform not found for {rootBone}");
                return new HumanBodyBones[0];
            }
            
            // Traverse the actual bone hierarchy to find all finger bones
            var boneChain = new List<HumanBodyBones>();
            TraverseActualBoneHierarchy(rootTransform, boneChain, animator, fingerName);
            
            Debug.Log($"[FingerTipSolver] Dynamically found {boneChain.Count} bones for {fingerName} {(isLeftHand ? "left" : "right")} hand: " +
                     string.Join(" -> ", boneChain.Select(b => b.ToString())));
            
            return boneChain.ToArray();
        }
        
        /// <summary>
        /// Traverse the actual bone hierarchy to discover finger bones.
        /// </summary>
        private static void TraverseActualBoneHierarchy(Transform currentTransform, List<HumanBodyBones> boneChain, Animator animator, string fingerName)
        {
            // Find which HumanBodyBones this transform corresponds to
            HumanBodyBones currentBone = FindHumanBodyBoneForTransform(currentTransform, animator);
            if (currentBone != HumanBodyBones.LastBone && !boneChain.Contains(currentBone))
            {
                boneChain.Add(currentBone);
                
                // Continue traversing children
                for (int i = 0; i < currentTransform.childCount; i++)
                {
                    Transform childTransform = currentTransform.GetChild(i);
                    TraverseActualBoneHierarchy(childTransform, boneChain, animator, fingerName);
                }
            }
        }
        
        /// <summary>
        /// Find which HumanBodyBones a transform corresponds to.
        /// </summary>
        private static HumanBodyBones FindHumanBodyBoneForTransform(Transform transform, Animator animator)
        {
            // Check all possible HumanBodyBones to see which one matches this transform
            foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;
                
                Transform boneTransform = animator.GetBoneTransform(bone);
                if (boneTransform == transform)
                {
                    return bone;
                }
            }
            
            return HumanBodyBones.LastBone; // Not found
        }
        
        /// <summary>
        /// Get the root bone for a specific finger.
        /// </summary>
        private static HumanBodyBones GetFingerRootBone(bool isLeftHand, string fingerName)
        {
            if (isLeftHand)
            {
                return fingerName switch
                {
                    "Thumb" => HumanBodyBones.LeftThumbProximal,
                    "Index" => HumanBodyBones.LeftIndexProximal,
                    "Middle" => HumanBodyBones.LeftMiddleProximal,
                    "Ring" => HumanBodyBones.LeftRingProximal,
                    "Little" => HumanBodyBones.LeftLittleProximal,
                    _ => HumanBodyBones.LastBone
                };
            }
            else
            {
                return fingerName switch
                {
                    "Thumb" => HumanBodyBones.RightThumbProximal,
                    "Index" => HumanBodyBones.RightIndexProximal,
                    "Middle" => HumanBodyBones.RightMiddleProximal,
                    "Ring" => HumanBodyBones.RightRingProximal,
                    "Little" => HumanBodyBones.RightLittleProximal,
                    _ => HumanBodyBones.LastBone
                };
            }
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


