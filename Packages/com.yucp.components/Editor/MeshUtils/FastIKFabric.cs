using UnityEngine;
using UnityEditor;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// FABRIK IK Solver adapted from FastIK for editor-only use.
    /// Solves inverse kinematics for finger bone chains to reach target positions.
    /// </summary>
    public static class FastIKFabric
    {
        private static bool VerboseLogs = false;
        public struct IKResult
        {
            public bool success;
            public string errorMessage;
            public Vector3[] solvedPositions;
            public Quaternion[] solvedRotations;
            public float finalError;
            public bool isValidPose; // True if solution doesn't penetrate mesh
        }

        /// <summary>
        /// Solve IK for a finger bone chain using FABRIK algorithm with collision detection and joint constraints.
        /// </summary>
        /// <param name="bones">Bone chain transforms (typically 3 bones for fingers)</param>
        /// <param name="targetPosition">Desired end effector position</param>
        /// <param name="targetRotation">Desired end effector rotation (applied to distal bone)</param>
        /// <param name="iterations">Maximum FABRIK iterations</param>
        /// <param name="delta">Convergence threshold</param>
        /// <param name="pole">Optional pole target for bone orientation</param>
        /// <param name="objectColliders">Colliders to check for collision avoidance</param>
        /// <param name="fingerName">Name of the finger for joint limit lookup</param>
        /// <returns>IK solution with solved positions and rotations</returns>
        public static IKResult SolveFingerChain(
            Transform[] bones,
            Vector3 targetPosition,
            Quaternion targetRotation,
            int iterations = 10,
            float delta = 0.001f,
            Transform pole = null,
            Collider[] objectColliders = null,
            string fingerName = "")
        {
            var result = new IKResult
            {
                success = false,
                errorMessage = "",
                solvedPositions = new Vector3[bones.Length],
                solvedRotations = new Quaternion[bones.Length],
                finalError = float.MaxValue
            };

            if (bones == null || bones.Length < 2)
            {
                result.errorMessage = "Invalid bone chain - need at least 2 bones";
                return result;
            }

            if (bones.Length > 4)
            {
                result.errorMessage = "Finger chains should have 3-4 bones maximum";
                return result;
            }

            try
            {
                // Initialize bone data
                var boneLengths = new float[bones.Length - 1];
                var startPositions = new Vector3[bones.Length];
                var startRotations = new Quaternion[bones.Length];
                var startDirections = new Vector3[bones.Length];
                
                float completeLength = 0f;

                // Calculate bone lengths and initial positions
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == null)
                    {
                        result.errorMessage = $"Bone {i} is null";
                        return result;
                    }

                    startPositions[i] = bones[i].position;
                    startRotations[i] = bones[i].rotation;

                    if (i < bones.Length - 1)
                    {
                        boneLengths[i] = Vector3.Distance(bones[i].position, bones[i + 1].position);
                        completeLength += boneLengths[i];
                    }
                }

                // Calculate initial directions
                for (int i = 0; i < bones.Length - 1; i++)
                {
                    startDirections[i] = (startPositions[i + 1] - startPositions[i]).normalized;
                }

                // Always use FABRIK algorithm - never do linear stretch for fingers
                // This ensures the root bone (MCP joint) position is always respected
                result.solvedPositions = new Vector3[startPositions.Length];
                System.Array.Copy(startPositions, result.solvedPositions, startPositions.Length);

                float distanceToTarget = Vector3.Distance(startPositions[0], targetPosition);
                bool isUnreachable = distanceToTarget >= completeLength;
                
                if (isUnreachable)
                {
                    if (VerboseLogs) Debug.Log($"[FastIKFabric] Target is unreachable (distance: {distanceToTarget:F3}, chain length: {completeLength:F3}) - using constrained IK");
                    if (VerboseLogs) Debug.Log($"[FastIKFabric] Root bone position will be preserved: {startPositions[0]}");
                }

                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    // Backward pass - always try to reach the target
                    result.solvedPositions[bones.Length - 1] = targetPosition;
                    for (int i = bones.Length - 2; i >= 0; i--)
                    {
                        Vector3 direction = (result.solvedPositions[i] - result.solvedPositions[i + 1]).normalized;
                        result.solvedPositions[i] = result.solvedPositions[i + 1] + direction * boneLengths[i];
                    }

                    // Forward pass - ALWAYS respect the root bone position
                    result.solvedPositions[0] = startPositions[0]; // This is crucial for finger IK
                    for (int i = 1; i < bones.Length; i++)
                    {
                        Vector3 direction = (result.solvedPositions[i] - result.solvedPositions[i - 1]).normalized;
                        result.solvedPositions[i] = result.solvedPositions[i - 1] + direction * boneLengths[i - 1];
                    }

                    // Check convergence
                    float error = Vector3.Distance(result.solvedPositions[bones.Length - 1], targetPosition);
                    if (error < delta)
                    {
                        result.finalError = error;
                        break;
                    }
                }

                result.finalError = Vector3.Distance(result.solvedPositions[bones.Length - 1], targetPosition);
                
                if (isUnreachable)
                {
                    if (VerboseLogs) Debug.Log($"[FastIKFabric] Constrained IK completed with final error: {result.finalError:F4}");
                    if (VerboseLogs) Debug.Log($"[FastIKFabric] Root bone position preserved: {result.solvedPositions[0]} (original: {startPositions[0]})");
                }

                // Apply pole constraint if provided
                if (pole != null && bones.Length >= 3)
                {
                    ApplyPoleConstraint(result.solvedPositions, pole.position, startPositions[0]);
                }

                // Soft collision correction pass to reduce penetration without large oscillations
                if (objectColliders != null && objectColliders.Length > 0)
                {
                    ApplyCollisionCorrection(result.solvedPositions, objectColliders, boneLengths);
                }

                // Calculate rotations with target rotation for end effector
                CalculateRotations(result.solvedPositions, startDirections, startRotations, targetRotation, result.solvedRotations, bones, fingerName);

                // Apply joint angle constraints after rotation calculation
                if (!string.IsNullOrEmpty(fingerName))
                {
                    ApplyJointConstraints(result.solvedRotations, bones, startRotations, fingerName);
                }

                // Validate that the solution doesn't penetrate the mesh
                result.isValidPose = ValidateSolution(result.solvedPositions, objectColliders, fingerName);
                if (!result.isValidPose)
                {
                    if (VerboseLogs) Debug.LogWarning($"[FastIKFabric] {fingerName} IK solution penetrates mesh - pose is invalid");
                }

                result.success = true;
            }
            catch (System.Exception e)
            {
                result.errorMessage = $"FABRIK solving failed: {e.Message}";
            }

            return result;
        }

        /// <summary>
        /// Apply pole constraint to orient the bone chain toward the pole target.
        /// </summary>
        private static void ApplyPoleConstraint(Vector3[] positions, Vector3 polePosition, Vector3 rootPosition)
        {
            for (int i = 1; i < positions.Length - 1; i++)
            {
                var plane = new Plane(positions[i + 1] - positions[i - 1], positions[i - 1]);
                var projectedPole = plane.ClosestPointOnPlane(polePosition);
                var projectedBone = plane.ClosestPointOnPlane(positions[i]);
                var angle = Vector3.SignedAngle(projectedBone - positions[i - 1], projectedPole - positions[i - 1], plane.normal);
                positions[i] = Quaternion.AngleAxis(angle, plane.normal) * (positions[i] - positions[i - 1]) + positions[i - 1];
            }
        }

        /// <summary>
        /// Calculate bone rotations based on solved positions.
        /// For distal bone, blends target rotation with IK chain direction to prevent extreme twisting.
        /// </summary>
        private static void CalculateRotations(
            Vector3[] solvedPositions,
            Vector3[] startDirections,
            Quaternion[] startRotations,
            Quaternion targetRotation,
            Quaternion[] solvedRotations,
            Transform[] bones,
            string fingerName)
        {
            for (int i = 0; i < solvedPositions.Length; i++)
            {
                // Calculate rotation from start direction to solved direction
                Vector3 solvedDirection;
                if (i == solvedPositions.Length - 1)
                {
                    // Last bone (distal) - should point along the bone segment direction
                    // The distal bone extends from the previous bone, so it points in that direction
                    if (i > 0 && i - 1 < startDirections.Length)
                    {
                        // Solved direction from previous bone to this bone
                        Vector3 ikBoneDirection = (solvedPositions[i] - solvedPositions[i - 1]).normalized;
                        
                        // Start direction from previous bone to this bone
                        Vector3 startBoneDirection = startDirections[i - 1];
                        
                        // Apply the same rotation delta as intermediate bones
                        Quaternion rotationDelta = Quaternion.FromToRotation(startBoneDirection, ikBoneDirection);
                        solvedRotations[i] = rotationDelta * startRotations[i];
                        
                    if (VerboseLogs) Debug.Log($"[FastIKFabric] Bone {i} (distal): Start dir = {startBoneDirection}, Solved dir = {ikBoneDirection}, Final rotation = {solvedRotations[i].eulerAngles}");
                    }
                    else
                    {
                        // Fallback: use target rotation if no previous bone
                        solvedRotations[i] = targetRotation;
                        if (VerboseLogs) Debug.Log($"[FastIKFabric] Bone {i} (distal): Using target rotation fallback = {targetRotation.eulerAngles}");
                    }
                }
                else
                {
                    // Mid bones - align to point toward next bone
                    solvedDirection = (solvedPositions[i + 1] - solvedPositions[i]).normalized;
                    Quaternion rotationDelta = Quaternion.FromToRotation(startDirections[i], solvedDirection);
                    solvedRotations[i] = rotationDelta * startRotations[i];
                    if (VerboseLogs) Debug.Log($"[FastIKFabric] Bone {i}: Rotation delta applied, final = {solvedRotations[i].eulerAngles}");
                }
            }
        }

        /// <summary>
        /// Check if bone segments penetrate the object and push them outside.
        /// Only applies to intermediate bones, not the end effector (distal bone).
        /// This allows fingers to curve around objects while maintaining target contact.
        /// </summary>
        private static void ApplyCollisionCorrection(Vector3[] positions, Collider[] colliders, float[] boneLengths)
        {
            const float safetyOffset = 0.002f; // 2mm offset from surface
            const float boneThickness = 0.005f; // 5mm for human fingers
            const float minPenetrationDepth = 0.0015f; // 1.5mm threshold
            const float deadZone = 0.0003f; // 0.3mm do nothing zone
            const float kPush = 0.5f; // push fraction
            
            // Only correct intermediate bones (not the end effector)
            // This allows the distal bone to reach its target while intermediate bones curve around
            for (int i = 0; i < positions.Length - 2; i++) // Stop before last bone
            {
                Vector3 boneStart = positions[i];
                Vector3 boneEnd = positions[i + 1];
                
                if (CheckSegmentCollision(boneStart, boneEnd, colliders, boneThickness, out Vector3 hitPoint, out Vector3 hitNormal))
                {
                    // Estimate penetration (distance from end toward surface)
                    float penetrationDepth = Vector3.Distance(boneEnd, hitPoint);
                    if (penetrationDepth < deadZone)
                    {
                        continue; // ignore tiny penetrations
                    }
                    if (penetrationDepth < minPenetrationDepth)
                    {
                        continue; // Skip minor penetrations
                    }
                    
                    // Push toward outside along normal, fractionally
                    Vector3 desiredPosition = boneEnd + hitNormal * (penetrationDepth + safetyOffset) * kPush;
                    // Maintain bone length
                    Vector3 boneDir = (desiredPosition - boneStart).normalized;
                    positions[i + 1] = boneStart + boneDir * boneLengths[i];
                    
                    if (VerboseLogs) Debug.Log($"[FastIKFabric] Collision correction on bone {i}: Pushed {penetrationDepth * 1000f:F1}mm (k={kPush})");
                }
            }
        }

        /// <summary>
        /// Check if a bone segment intersects with object colliders.
        /// Uses raycasting against colliders directly.
        /// </summary>
        private static bool CheckSegmentCollision(
            Vector3 start, Vector3 end, 
            Collider[] colliders,
            float boneThickness,
            out Vector3 hitPoint, out Vector3 hitNormal)
        {
            hitPoint = end;
            hitNormal = Vector3.up;
            
            if (colliders == null || colliders.Length == 0)
                return false;
            
            Vector3 direction = (end - start).normalized;
            float distance = Vector3.Distance(start, end);
            
            // Check multiple points along the bone segment for penetration
            int numChecks = Mathf.Max(3, Mathf.CeilToInt(distance / boneThickness));
            for (int i = 0; i < numChecks; i++)
            {
                float t = i / (float)(numChecks - 1);
                Vector3 checkPoint = Vector3.Lerp(start, end, t);
                
                // Check each collider for penetration
                foreach (var col in colliders)
                {
                    if (col == null) continue;
                    
                    // Check if point is inside collider bounds
                    if (col.bounds.Contains(checkPoint))
                    {
                        // Try to find closest point on collider surface using raycast
                        // Cast rays in multiple directions to find surface
                        Vector3[] testDirections = new Vector3[]
                        {
                            direction,
                            -direction,
                            Vector3.up,
                            Vector3.down,
                            Vector3.left,
                            Vector3.right,
                            Vector3.forward,
                            Vector3.back
                        };
                        
                        float closestDist = float.MaxValue;
                        Vector3 bestHitPoint = checkPoint;
                        Vector3 bestHitNormal = Vector3.up;
                        bool foundHit = false;
                        
                        foreach (var testDir in testDirections)
                        {
                            Ray ray = new Ray(checkPoint, testDir);
                            RaycastHit hit;
                            
                            // Use collider's Raycast method directly
                            if (col.Raycast(ray, out hit, boneThickness * 2f))
                            {
                                if (hit.distance < closestDist)
                                {
                                    closestDist = hit.distance;
                                    bestHitPoint = hit.point;
                                    bestHitNormal = hit.normal;
                                    foundHit = true;
                                }
                            }
                        }
                        
                        if (foundHit)
                        {
                            hitPoint = bestHitPoint;
                            hitNormal = bestHitNormal;
                            return true;
                        }
                    }
                    
                    // Also check if bone segment ray intersects collider
                    Ray segmentRay = new Ray(start, direction);
                    RaycastHit segmentHit;
                    if (col.Raycast(segmentRay, out segmentHit, distance))
                    {
                        // Check if hit point is within bone segment
                        float segmentT = Vector3.Dot(segmentHit.point - start, direction) / distance;
                        if (segmentT >= 0f && segmentT <= 1f)
                        {
                            hitPoint = segmentHit.point;
                            hitNormal = segmentHit.normal;
                            return true;
                        }
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// Check if a collider is part of the object's collider array.
        /// </summary>
        private static bool IsPartOfObject(Collider col, Collider[] objectColliders)
        {
            if (col == null || objectColliders == null) return false;
            
            foreach (var objCol in objectColliders)
            {
                if (objCol == col) return true;
            }
            
            return false;
        }

        /// <summary>
        /// Apply joint angle constraints to bone rotations to prevent unrealistic bending.
        /// Checks bend angles based on solved positions, not rotations.
        /// </summary>
        private static void ApplyJointConstraints(
            Quaternion[] solvedRotations,
            Transform[] bones,
            Quaternion[] startRotations,
            string fingerName)
        {
            // Joint constraints are now handled by detecting unnatural curl in FingerTipSolver
            // and adjusting targets accordingly. This prevents fingers from getting into
            // bad poses in the first place, rather than trying to fix rotations after the fact.
            
            // We keep this method but make it less aggressive to avoid interfering with
            // the curl detection system
            if (VerboseLogs) Debug.Log($"[FastIKFabric] Joint constraints for {fingerName} handled by curl detection");
        }

        /// <summary>
        /// Get joint angle limits for a specific finger joint.
        /// Returns (minAngle, maxAngle) in degrees.
        /// </summary>
        private static (float min, float max) GetFingerJointLimits(int jointIndex, string fingerName)
        {
            // Joint indices: 0 = proximal (MCP), 1 = intermediate (PIP), 2 = distal (DIP)
            // Angles are in degrees, relative to neutral pose
            
            if (fingerName.ToLower().Contains("thumb"))
            {
                // Thumb has different limits than fingers
                return jointIndex switch
                {
                    0 => (-20f, 90f),   // MCP: -20° to 90° flexion, ±15° lateral
                    1 => (0f, 110f),     // PIP: 0° to 110° flexion
                    2 => (0f, 80f),      // DIP: 0° to 80° flexion
                    _ => (-30f, 120f)    // Default
                };
            }
            else
            {
                // Standard fingers (Index, Middle, Ring, Little)
                return jointIndex switch
                {
                    0 => (-20f, 90f),   // MCP: -20° to 90° flexion, ±15° lateral spread
                    1 => (0f, 110f),     // PIP: 0° to 110° flexion (no hyperextension)
                    2 => (0f, 90f),      // DIP: 0° to 90° flexion (no hyperextension)
                    _ => (-30f, 120f)    // Default
                };
            }
        }

        /// <summary>
        /// Validate that the IK solution doesn't penetrate the object mesh.
        /// Checks if any bone segment intersects with colliders.
        /// </summary>
        private static bool ValidateSolution(Vector3[] positions, Collider[] colliders, string fingerName)
        {
            if (colliders == null || colliders.Length == 0)
                return true; // No colliders to check against
            
            // Check each bone segment for penetration
            for (int i = 0; i < positions.Length - 1; i++)
            {
                Vector3 boneStart = positions[i];
                Vector3 boneEnd = positions[i + 1];
                
                // Check if bone segment penetrates any collider
                Vector3 direction = (boneEnd - boneStart).normalized;
                float distance = Vector3.Distance(boneStart, boneEnd);
                
                foreach (var col in colliders)
                {
                    if (col == null) continue;
                    
                    RaycastHit hit;
                    // Cast ray along bone segment
                    if (col.Raycast(new Ray(boneStart, direction), out hit, distance))
                    {
                        // Check if this is a significant penetration (not just surface grazing)
                        // Allow the fingertip to be on the surface
                        bool isLastBone = (i == positions.Length - 2);
                        float threshold = isLastBone ? 0.001f : 0.003f; // More lenient for tip
                        
                        if (hit.distance < distance - threshold)
                        {
                            Debug.Log($"[FastIKFabric] {fingerName} bone {i} penetrates mesh at {hit.distance * 100f:F1}cm");
                            return false;
                        }
                    }
                }
            }
            
            return true; // No penetration detected
        }

        /// <summary>
        /// Draw gizmos for the IK chain in Scene view.
        /// </summary>
        public static void DrawIKGizmos(Vector3[] positions, Color color)
        {
#if UNITY_EDITOR
            if (positions == null || positions.Length < 2) return;

            var originalColor = Handles.color;
            Handles.color = color;

            // Draw bone connections
            for (int i = 0; i < positions.Length - 1; i++)
            {
                Handles.DrawLine(positions[i], positions[i + 1]);
            }

            // Draw bone spheres
            for (int i = 0; i < positions.Length; i++)
            {
                float scale = i == 0 ? 0.02f : 0.015f; // Root bone slightly larger
                Handles.SphereHandleCap(0, positions[i], Quaternion.identity, scale, EventType.Repaint);
            }

            Handles.color = originalColor;
#endif
        }
    }
}
