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
        private static bool VerboseLogs = false;
        private static readonly System.Collections.Generic.Dictionary<string, Transform> s_PoleByFinger = new System.Collections.Generic.Dictionary<string, Transform>();
        private static readonly System.Collections.Generic.Dictionary<string, int> s_UnnaturalCount = new System.Collections.Generic.Dictionary<string, int>();
        private static readonly System.Collections.Generic.Dictionary<string, Vector3> s_LastAdjustedTarget = new System.Collections.Generic.Dictionary<string, Vector3>();
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
            public bool isValidPose; // True if all finger IK solutions are valid (no mesh penetration)
        }

        /// <summary>
        /// Solve for muscle values that place finger tips at target positions using IK.
        /// </summary>
        public static FingerTipResult SolveFingerTips(Animator animator, FingerTipTarget targets, bool isLeftHand, Transform grippedObject = null)
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
                // Get colliders for collision detection (object + avatar), avatar used for clearance to prevent clipping
                Collider[] objectColliders = grippedObject != null ? YUCP.Components.Editor.MeshUtils.SurfaceQuery.GetAllColliders(grippedObject) : new Collider[0];
                Collider[] avatarColliders = animator != null ? animator.GetComponentsInChildren<Collider>(true) : new Collider[0];
                var combinedColliders = new List<Collider>();
                if (objectColliders != null) combinedColliders.AddRange(objectColliders);
                if (avatarColliders != null) combinedColliders.AddRange(avatarColliders);
                // Remove nulls and duplicates
                combinedColliders.RemoveAll(c => c == null);
                combinedColliders = combinedColliders.Distinct().ToList();
                if (VerboseLogs) Debug.Log($"[FingerTipSolver] Colliders -> Object: {objectColliders?.Length ?? 0}, Avatar: {avatarColliders?.Length ?? 0}, Combined: {combinedColliders.Count}");
                
                // Log target rotations for each finger to verify they're different
                if (VerboseLogs) Debug.Log($"[FingerTipSolver] Target rotations - Thumb: {targets.thumbRotation.eulerAngles}, Index: {targets.indexRotation.eulerAngles}, Middle: {targets.middleRotation.eulerAngles}, Ring: {targets.ringRotation.eulerAngles}, Little: {targets.littleRotation.eulerAngles}");
                
                // Solve IK for each finger with target rotations and collision detection
                // Track validation status for each finger
                bool allValid = true;
                allValid &= SolveFingerIK(animator, targets.thumbTip, targets.thumbRotation, isLeftHand, "Thumb", result.solvedRotations, combinedColliders.ToArray());
                allValid &= SolveFingerIK(animator, targets.indexTip, targets.indexRotation, isLeftHand, "Index", result.solvedRotations, combinedColliders.ToArray());
                allValid &= SolveFingerIK(animator, targets.middleTip, targets.middleRotation, isLeftHand, "Middle", result.solvedRotations, combinedColliders.ToArray());
                allValid &= SolveFingerIK(animator, targets.ringTip, targets.ringRotation, isLeftHand, "Ring", result.solvedRotations, combinedColliders.ToArray());
                allValid &= SolveFingerIK(animator, targets.littleTip, targets.littleRotation, isLeftHand, "Little", result.solvedRotations, combinedColliders.ToArray());

                // Convert bone rotations to muscle values
                result.muscleValues = BoneRotationToMuscle.ConvertFingerRotationsToMuscles(
                    animator, isLeftHand, result.solvedRotations);

                result.isValidPose = allValid;
                result.success = true;
            }
            catch (System.Exception e)
            {
                result.errorMessage = $"Finger tip solving failed: {e.Message}";
            }

            return result;
        }

        /// <summary>
        /// Solve IK for a single finger using FABRIK algorithm with target rotation and collision detection.
        /// Returns true if the solution is valid (no mesh penetration).
        /// </summary>
        private static bool SolveFingerIK(Animator animator, Vector3 targetTip, Quaternion targetRotation, bool isLeftHand, string fingerName, Dictionary<HumanBodyBones, Quaternion> solvedRotations, Collider[] objectColliders = null)
        {
            if (targetTip == Vector3.zero) return true; // Skip if not set, consider valid

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
                    return false;
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
                    return false;
                }
            }
            
            // Calculate chain length for debugging
            float chainLength = 0f;
            for (int i = 0; i < boneTransforms.Length - 1; i++)
            {
                chainLength += Vector3.Distance(boneTransforms[i].position, boneTransforms[i + 1].position);
            }
            float targetDistance = Vector3.Distance(boneTransforms[0].position, targetTip);
            
            if (VerboseLogs) Debug.Log($"[FingerTipSolver] Solving IK for {fingerName} {(isLeftHand ? "left" : "right")} hand: " +
                     $"Target: {targetTip}, Bones: {fingerBones.Length}, " +
                     $"Chain length: {chainLength * 100f:F1}cm, Target distance: {targetDistance * 100f:F1}cm, " +
                     $"Chain: {string.Join(" -> ", fingerBones.Select(b => b.ToString()))}");

            // Validate bone chain before solving IK
            if (!ValidateBoneChain(boneTransforms, fingerName))
            {
                if (VerboseLogs) Debug.LogWarning($"[FingerTipSolver] Bone chain validation failed for {fingerName}");
                return false;
            }

            // Validate that the target is reachable without clipping through the object or avatar
            Vector3 validatedTarget = targetTip;
            if (objectColliders != null && objectColliders.Length > 0)
            {
                // Filter out colliders that belong to this finger chain to avoid self-collision
                var filteredCols = new List<Collider>();
                foreach (var col in objectColliders)
                {
                    if (col == null) continue;
                    if (!IsTransformInChain(col.transform, boneTransforms)) filteredCols.Add(col);
                }
                validatedTarget = ValidateTargetReachable(boneTransforms[0].position, targetTip, filteredCols.ToArray(), fingerName);
                if (Vector3.Distance(validatedTarget, targetTip) > 0.001f)
                {
                    if (VerboseLogs) Debug.Log($"[FingerTipSolver] {fingerName} target adjusted from {targetTip} to {validatedTarget} to avoid clipping");
                }
            }

            // Compute a pole position to lock bending plane toward the palm
            Transform hand = animator.GetBoneTransform(isLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Vector3 palmUp = hand != null ? hand.up : Vector3.up;
            Vector3 rootPos = boneTransforms[0].position;
            Vector3 toTargetDir = (validatedTarget - rootPos).normalized;
            if (toTargetDir.sqrMagnitude < 1e-6f) toTargetDir = (hand != null ? hand.forward : Vector3.forward);
            // Lateral axis across the finger
            Vector3 lateral = Vector3.Cross(palmUp, toTargetDir).normalized;
            if (lateral.sqrMagnitude < 1e-6f) lateral = Vector3.right;
            // Place the pole above the finger toward palm and slightly offset laterally
            Vector3 polePos = rootPos + palmUp * 0.05f - lateral * 0.02f;

            // Get or create a persistent pole per finger to stabilize the bend plane
            string poleKey = ($"{(isLeftHand ? "L" : "R")}:{fingerName}");
            if (!s_PoleByFinger.TryGetValue(poleKey, out var poleT) || poleT == null)
            {
                var go = new GameObject($"IKPole_{poleKey}") { hideFlags = HideFlags.HideAndDontSave };
                poleT = go.transform;
                s_PoleByFinger[poleKey] = poleT;
            }
            poleT.position = polePos;

            // Solve IK using FABRIK with pole and collision validation
            string fingerKey = ($"{(isLeftHand ? "L" : "R")}:{fingerName}");
            // Filter colliders for IK as well (exclude this finger's own colliders)
            Collider[] filteredForIK = null;
            if (objectColliders != null)
            {
                var tmp = new List<Collider>();
                foreach (var col in objectColliders)
                {
                    if (col == null) continue;
                    if (!IsTransformInChain(col.transform, boneTransforms)) tmp.Add(col);
                }
                filteredForIK = tmp.ToArray();
            }
            var ikResult = FastIKFabric.SolveFingerChain(boneTransforms, validatedTarget, targetRotation, 10, 0.001f, poleT, filteredForIK, fingerName);
            
            if (!ikResult.success)
            {
                Debug.LogWarning($"[FingerTipSolver] IK solving failed for {fingerName}: {ikResult.errorMessage}");
                
                // Fallback: apply a basic finger curl pose
                ApplyFallbackFingerPose(fingerBones, solvedRotations, fingerName);
                return false;
            }
            
            // Check if the IK solution creates unnatural finger curling
            bool unnaturalCurl = CheckForUnnaturalCurl(ikResult.solvedPositions, boneTransforms, fingerName);
            
            if (unnaturalCurl)
            {
                // Hysteresis: require multiple consecutive invalid frames before adjusting
                if (!s_UnnaturalCount.ContainsKey(fingerKey)) s_UnnaturalCount[fingerKey] = 0;
                s_UnnaturalCount[fingerKey] = Mathf.Min(1000, s_UnnaturalCount[fingerKey] + 1);
                if (s_UnnaturalCount[fingerKey] >= 3)
                {
                    if (VerboseLogs) Debug.LogWarning($"[FingerTipSolver] {fingerName} has unnatural curl - adjusting target closer");
                    
                    // Move target closer with smoothing to prevent oscillation
                    Vector3 adjustedTarget = AdjustTargetForNaturalCurlSmoothed(fingerKey, boneTransforms, validatedTarget, ikResult.solvedPositions, fingerName);
                    
                    // Re-solve IK with adjusted target
                    ikResult = FastIKFabric.SolveFingerChain(boneTransforms, adjustedTarget, targetRotation, 10, 0.001f, poleT, objectColliders, fingerName);
                    
                    if (!ikResult.success)
                    {
                        Debug.LogWarning($"[FingerTipSolver] IK solving failed for {fingerName} after adjustment: {ikResult.errorMessage}");
                        ApplyFallbackFingerPose(fingerBones, solvedRotations, fingerName);
                        return false;
                    }
                    
                    if (VerboseLogs) Debug.Log($"[FingerTipSolver] {fingerName} re-solved with adjusted target - curl is now natural");
                }
            }
            
            if (VerboseLogs) Debug.Log($"[FingerTipSolver] IK solved successfully for {fingerName}: " +
                     $"Final error: {ikResult.finalError:F4}, Rotations: {ikResult.solvedRotations.Length}, Valid: {ikResult.isValidPose}");

            // Reset hysteresis counter on valid pose
            if (ikResult.isValidPose)
            {
                s_UnnaturalCount[fingerKey] = 0;
            }
            
            // Store solved rotations (target rotation is already baked into IK solution)
            for (int i = 0; i < fingerBones.Length; i++)
            {
                solvedRotations[fingerBones[i]] = ikResult.solvedRotations[i];
            }
            
            if (VerboseLogs) Debug.Log($"[FingerTipSolver] Applied target rotation to {fingerName} via FABRIK IK solver");
            
            // Keep pole transform for stability; it will be reused per finger

            // Return validation status
            return ikResult.isValidPose;
        }

        private static bool IsTransformInChain(Transform t, Transform[] chain)
        {
            if (t == null || chain == null) return false;
            foreach (var b in chain)
            {
                if (b == null) continue;
                // If collider is the bone or a child of the bone, consider it part of the chain
                Transform cur = t;
                while (cur != null)
                {
                    if (cur == b) return true;
                    cur = cur.parent;
                }
            }
            return false;
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
        /// Check if the IK solution creates unnatural finger curling.
        /// Each finger joint can only bend 0-100 degrees relative to the previous bone.
        /// </summary>
        private static bool CheckForUnnaturalCurl(Vector3[] solvedPositions, Transform[] bones, string fingerName)
        {
            if (solvedPositions.Length < 3) return false; // Need at least 3 bones to check a joint
            
            // Check the angle at each joint (between consecutive bone segments)
            // Joint 0: angle between proximal and intermediate bones
            // Joint 1: angle between intermediate and distal bones
            for (int i = 0; i < solvedPositions.Length - 2; i++)
            {
                // Direction of first bone segment (toward the joint)
                Vector3 bone1Dir = (solvedPositions[i + 1] - solvedPositions[i]).normalized;
                
                // Direction of second bone segment (away from the joint)
                Vector3 bone2Dir = (solvedPositions[i + 2] - solvedPositions[i + 1]).normalized;
                
                // The joint angle is the angle between these two bone segments
                float jointAngle = Vector3.Angle(bone1Dir, bone2Dir);
                
                // Finger joints can bend 0-100 degrees
                // 0° = fully extended (straight)
                // 100° = fully curled
                // >100° = bending backward (unnatural)
                const float maxJointBend = 100f;
                
                if (jointAngle > maxJointBend)
                {
                    Debug.LogWarning($"[FingerTipSolver] {fingerName} joint {i} (between bones {i} and {i+1}) bent {jointAngle:F1}° (max allowed: {maxJointBend:F1}°)");
                    return true;
                }
                
                // Check for extreme backward bending (bones pointing opposite directions)
                float dotProduct = Vector3.Dot(bone1Dir, bone2Dir);
                if (dotProduct < -0.1f) // Allow small negative for nearly-straight joints
                {
                    Debug.LogWarning($"[FingerTipSolver] {fingerName} joint {i} bending backward! Angle: {jointAngle:F1}°, Dot: {dotProduct:F2}");
                    return true;
                }
            }
            
            return false; // Curl is natural
        }

        /// <summary>
        /// Adjust target position to allow natural finger curl.
        /// Moves target closer to finger base to prevent backward bending.
        /// </summary>
        private static Vector3 AdjustTargetForNaturalCurl(Transform[] bones, Vector3 currentTarget, Vector3[] solvedPositions, string fingerName)
        {
            Vector3 fingerBase = bones[0].position;
            Vector3 direction = (currentTarget - fingerBase).normalized;
            float currentDistance = Vector3.Distance(fingerBase, currentTarget);
            
            // Calculate total bone length
            float totalLength = 0f;
            for (int i = 0; i < bones.Length - 1; i++)
            {
                totalLength += Vector3.Distance(bones[i].position, bones[i + 1].position);
            }
            
            // When backward bending is detected, we need to significantly reduce the target distance
            // Natural finger curl uses 50-70% of full extension, not 80-100%
            float maxNaturalReach = totalLength * 0.65f;
            
            // Always ensure target is within natural reach
            if (currentDistance > maxNaturalReach)
            {
                // Move target much closer to prevent overextension
                Vector3 adjustedTarget = fingerBase + direction * maxNaturalReach;
                Debug.LogWarning($"[FingerTipSolver] {fingerName} target TOO FAR! Moved from {currentDistance * 100f:F1}cm to {maxNaturalReach * 100f:F1}cm (max natural: {maxNaturalReach * 100f:F1}cm)");
                return adjustedTarget;
            }
            
            // For moderate distances that still cause problems, reduce by 30%
            float safeDistance = currentDistance * 0.7f;
            Vector3 safeTarget = fingerBase + direction * safeDistance;
            Debug.LogWarning($"[FingerTipSolver] {fingerName} target adjusted from {currentDistance * 100f:F1}cm to {safeDistance * 100f:F1}cm to prevent backward bending");
            return safeTarget;
        }

        /// <summary>
        /// Smoothed adjustment to avoid oscillation: lerp toward adjusted target and clamp per-frame delta.
        /// </summary>
        private static Vector3 AdjustTargetForNaturalCurlSmoothed(string fingerKey, Transform[] bones, Vector3 currentTarget, Vector3[] solvedPositions, string fingerName)
        {
            // Compute the raw adjusted target using existing heuristic
            Vector3 rawAdjusted = AdjustTargetForNaturalCurl(bones, currentTarget, solvedPositions, fingerName);

            // Smooth the change to avoid large jumps
            Vector3 fingerBase = bones[0].position;
            float currentDist = Vector3.Distance(fingerBase, currentTarget);
            float targetDist = Vector3.Distance(fingerBase, rawAdjusted);
            float smoothedDist = Mathf.Lerp(currentDist, targetDist, 0.35f); // move 35% toward adjusted

            // Clamp per-frame distance change to max 5 mm to reduce flutter
            float maxStep = 0.005f;
            float delta = Mathf.Clamp(smoothedDist - currentDist, -maxStep, maxStep);
            float finalDist = currentDist + delta;

            Vector3 finalTarget = fingerBase + (rawAdjusted - fingerBase).normalized * finalDist;

            // Debounce: if very close to last adjusted target (<0.5 mm), keep last to avoid micro-churn
            if (s_LastAdjustedTarget.TryGetValue(fingerKey, out var last) && (finalTarget - last).sqrMagnitude < (0.0005f * 0.0005f))
            {
                return last;
            }

            s_LastAdjustedTarget[fingerKey] = finalTarget;
            return finalTarget;
        }

        /// <summary>
        /// Validate that the target position is reachable without clipping through the object mesh.
        /// Uses a swept sphere from finger base to target to check for obstacles.
        /// If path is blocked, returns adjusted position on the blocking surface.
        /// </summary>
        private static Vector3 ValidateTargetReachable(Vector3 fingerBase, Vector3 targetPosition, Collider[] objectColliders, string fingerName)
        {
            if (objectColliders == null || objectColliders.Length == 0)
                return targetPosition;
            
            Vector3 direction = (targetPosition - fingerBase).normalized;
            float distance = Vector3.Distance(fingerBase, targetPosition);
            
            // Use a sphere radius representing finger thickness
            const float fingerRadius = 0.008f; // 8mm radius for finger
            
            // Find nearest blocking collider to decide if it's the avatar or the object
            Collider nearestBlocker = null;
            float nearestDist = float.MaxValue;
            Vector3 nearestHitPoint = targetPosition;
            Vector3 nearestNormal = Vector3.up;
            
            // Cast a sphere from base toward target
            foreach (var col in objectColliders)
            {
                if (col == null) continue;
                
                RaycastHit hit;
                // Use SphereCast to check if the finger path is blocked
                if (col.Raycast(new Ray(fingerBase, direction), out hit, distance))
                {
                    if (hit.distance < nearestDist)
                    {
                        nearestDist = hit.distance;
                        nearestBlocker = col;
                        nearestHitPoint = hit.point;
                        nearestNormal = hit.normal;
                    }
                }
            }
            
            // If path blocked, check if blocker is avatar (SkinnedMeshRenderer or part of avatar rig)
            if (nearestBlocker != null)
            {
                bool isAvatarBlocker = IsAvatarCollider(nearestBlocker);
                
                if (isAvatarBlocker)
                {
                    // Avatar blocking: don't pull target closer; nudge it away from avatar slightly
                    Vector3 awayFromAvatar = nearestHitPoint + nearestNormal * 0.01f; // 1cm clearance
                    // Only use adjusted target if it's further than original (don't curl tighter)
                    float origDist = Vector3.Distance(fingerBase, targetPosition);
                    float awayDist = Vector3.Distance(fingerBase, awayFromAvatar);
                    if (awayDist >= origDist * 0.9f)
                    {
                        if (VerboseLogs) Debug.Log($"[FingerTipSolver] {fingerName} avatar blocking at {nearestDist * 100f:F1}cm, nudged target away");
                        return awayFromAvatar;
                    }
                    else
                    {
                        // Target would be pulled closer - keep original to avoid over-curl
                        if (VerboseLogs) Debug.Log($"[FingerTipSolver] {fingerName} avatar blocking but keeping original target to prevent over-curl");
                        return targetPosition;
                    }
                }
                else
                {
                    // Object blocking: snap to surface as before
                    Vector3 validPosition = nearestHitPoint + nearestNormal * 0.002f; // 2mm outside surface
                    if (VerboseLogs) Debug.Log($"[FingerTipSolver] {fingerName} path blocked at {nearestDist * 100f:F1}cm, adjusted target to object surface");
                    return validPosition;
                }
            }
            
            // Path is clear - return original target
            return targetPosition;
        }

        /// <summary>
        /// Check if a collider belongs to the avatar (SkinnedMeshRenderer or rigged bones).
        /// </summary>
        private static bool IsAvatarCollider(Collider col)
        {
            if (col == null) return false;
            // Check if collider's GameObject or any parent has SkinnedMeshRenderer or Animator
            Transform t = col.transform;
            while (t != null)
            {
                if (t.GetComponent<SkinnedMeshRenderer>() != null) return true;
                if (t.GetComponent<Animator>() != null) return true;
                t = t.parent;
            }
            return false;
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



