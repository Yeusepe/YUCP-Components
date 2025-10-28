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
        public struct IKResult
        {
            public bool success;
            public string errorMessage;
            public Vector3[] solvedPositions;
            public Quaternion[] solvedRotations;
            public float finalError;
        }

        /// <summary>
        /// Solve IK for a finger bone chain using FABRIK algorithm.
        /// </summary>
        /// <param name="bones">Bone chain transforms (typically 3 bones for fingers)</param>
        /// <param name="targetPosition">Desired end effector position</param>
        /// <param name="iterations">Maximum FABRIK iterations</param>
        /// <param name="delta">Convergence threshold</param>
        /// <param name="pole">Optional pole target for bone orientation</param>
        /// <returns>IK solution with solved positions and rotations</returns>
        public static IKResult SolveFingerChain(
            Transform[] bones,
            Vector3 targetPosition,
            int iterations = 10,
            float delta = 0.001f,
            Transform pole = null)
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
                    Debug.Log($"[FastIKFabric] Target is unreachable (distance: {distanceToTarget:F3}, chain length: {completeLength:F3}) - using constrained IK");
                    Debug.Log($"[FastIKFabric] Root bone position will be preserved: {startPositions[0]}");
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
                    Debug.Log($"[FastIKFabric] Constrained IK completed with final error: {result.finalError:F4}");
                    Debug.Log($"[FastIKFabric] Root bone position preserved: {result.solvedPositions[0]} (original: {startPositions[0]})");
                }

                // Apply pole constraint if provided
                if (pole != null && bones.Length >= 3)
                {
                    ApplyPoleConstraint(result.solvedPositions, pole.position, startPositions[0]);
                }

                // Calculate rotations
                CalculateRotations(result.solvedPositions, startDirections, startRotations, result.solvedRotations);

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
        /// </summary>
        private static void CalculateRotations(
            Vector3[] solvedPositions,
            Vector3[] startDirections,
            Quaternion[] startRotations,
            Quaternion[] solvedRotations)
        {
            for (int i = 0; i < solvedPositions.Length; i++)
            {
                if (i == solvedPositions.Length - 1)
                {
                    // End effector - maintain original rotation
                    solvedRotations[i] = startRotations[i];
                }
                else
                {
                    // Calculate rotation from start direction to solved direction
                    Vector3 solvedDirection = (solvedPositions[i + 1] - solvedPositions[i]).normalized;
                    Quaternion rotationDelta = Quaternion.FromToRotation(startDirections[i], solvedDirection);
                    solvedRotations[i] = rotationDelta * startRotations[i];
                }
            }
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
