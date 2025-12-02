using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Solves object transforms using surface cluster deformation.
    /// Supports multiple solver modes for different attachment types.
    /// </summary>
    public static class BlendshapeSolver
    {
        public struct SolverResult
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public bool success;
            public string errorMessage;
        }

        public struct SurfaceFrame
        {
            public Vector3 position;
            public Vector3 normal;
            public Vector3 tangent;
            public Vector3 binormal;
            public Quaternion rotation;

            public static SurfaceFrame FromVectors(Vector3 pos, Vector3 norm, Vector3 tan)
            {
                SurfaceFrame frame = new SurfaceFrame();
                frame.position = pos;
                frame.normal = norm;
                frame.tangent = tan;
                frame.binormal = Vector3.Cross(norm, tan).normalized;
                
                // Build rotation from orthonormal basis
                // X = tangent, Y = binormal, Z = normal
                Matrix4x4 matrix = new Matrix4x4();
                matrix.SetColumn(0, tan);
                matrix.SetColumn(1, frame.binormal);
                matrix.SetColumn(2, norm);
                matrix.SetColumn(3, new Vector4(0, 0, 0, 1));
                
                frame.rotation = matrix.rotation;
                
                return frame;
            }
        }

        /// <summary>
        /// Solve using Rigid mode: single rotation + translation.
        /// Best for small rigid objects like piercings, badges, small jewelry.
        /// </summary>
        public static SolverResult SolveRigid(
            Vector3 clusterPosition,
            Vector3 clusterNormal,
            Vector3 clusterTangent,
            Transform objectTransform,
            Transform targetMeshTransform,
            bool alignRotation,
            Vector3? previousTangent = null,
            float smoothingFactor = 0.3f)
        {
            SolverResult result = new SolverResult
            {
                scale = Vector3.one,
                success = false
            };

            try
            {
                // Apply tangent smoothing
                Vector3 stabilizedTangent = clusterTangent;
                if (previousTangent.HasValue && smoothingFactor > 0f)
                {
                    stabilizedTangent = StabilizeTangent(
                        clusterTangent, 
                        previousTangent.Value, 
                        clusterNormal, 
                        smoothingFactor);
                }

                // Build surface frame
                SurfaceFrame frame = SurfaceFrame.FromVectors(
                    clusterPosition, 
                    clusterNormal, 
                    stabilizedTangent);

                // Apply Gram-Schmidt orthonormalization for stability
                frame = OrthonormalizeFrame(frame);

                // Transform to world space
                Vector3 worldPosition = targetMeshTransform.TransformPoint(frame.position);

                // Calculate rotation
                Quaternion worldRotation;
                if (alignRotation)
                {
                    // Align to surface frame
                    worldRotation = targetMeshTransform.rotation * frame.rotation;
                }
                else
                {
                    // Keep original rotation
                    worldRotation = objectTransform.rotation;
                }

                // Convert to local space (relative to object's parent)
                if (objectTransform.parent != null)
                {
                    result.position = objectTransform.parent.InverseTransformPoint(worldPosition);
                    result.rotation = Quaternion.Inverse(objectTransform.parent.rotation) * worldRotation;
                }
                else
                {
                    result.position = worldPosition;
                    result.rotation = worldRotation;
                }

                result.success = true;
            }
            catch (System.Exception ex)
            {
                result.errorMessage = $"Rigid solver failed: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Solve using Rigid + Normal Offset mode.
        /// Adds a small outward push along the surface normal.
        /// </summary>
        public static SolverResult SolveRigidNormalOffset(
            Vector3 clusterPosition,
            Vector3 clusterNormal,
            Vector3 clusterTangent,
            Transform objectTransform,
            Transform targetMeshTransform,
            bool alignRotation,
            float normalOffset,
            Vector3? previousTangent = null,
            float smoothingFactor = 0.3f)
        {
            // First solve as rigid
            SolverResult rigidResult = SolveRigid(
                clusterPosition,
                clusterNormal,
                clusterTangent,
                objectTransform,
                targetMeshTransform,
                alignRotation,
                previousTangent,
                smoothingFactor);

            if (!rigidResult.success)
            {
                return rigidResult;
            }

            // Add normal offset in world space
            Vector3 worldNormal = targetMeshTransform.TransformDirection(clusterNormal);
            Vector3 worldOffsetPosition = targetMeshTransform.TransformPoint(clusterPosition) + worldNormal * normalOffset;

            // Convert back to local space
            if (objectTransform.parent != null)
            {
                rigidResult.position = objectTransform.parent.InverseTransformPoint(worldOffsetPosition);
            }
            else
            {
                rigidResult.position = worldOffsetPosition;
            }

            return rigidResult;
        }

        /// <summary>
        /// Solve using Affine mode: allows minor shear/scale.
        /// Suitable for wider objects like stickers that need to stretch with skin.
        /// </summary>
        public static SolverResult SolveAffine(
            Vector3 clusterPosition,
            Vector3 clusterNormal,
            Vector3 clusterTangent,
            Transform objectTransform,
            Transform targetMeshTransform,
            Vector3 baseClusterPosition,
            Vector3 baseClusterNormal,
            Vector3 baseClusterTangent,
            bool alignRotation,
            Vector3? previousTangent = null,
            float smoothingFactor = 0.3f)
        {
            // Start with rigid solve
            SolverResult result = SolveRigid(
                clusterPosition,
                clusterNormal,
                clusterTangent,
                objectTransform,
                targetMeshTransform,
                alignRotation,
                previousTangent,
                smoothingFactor);

            if (!result.success)
            {
                return result;
            }

            // Calculate scale using surface deformation
            // Compare deformed frame to base frame
            float baseSize = baseClusterTangent.magnitude;
            float deformedSize = clusterTangent.magnitude;

            if (baseSize > 0.001f)
            {
                float scaleRatio = deformedSize / baseSize;
                
                // Clamp scale to reasonable range
                scaleRatio = Mathf.Clamp(scaleRatio, 0.8f, 1.2f);
                
                result.scale = new Vector3(scaleRatio, scaleRatio, scaleRatio);
            }

            return result;
        }

        /// <summary>
        /// Stabilize tangent vector using smoothing.
        /// </summary>
        private static Vector3 StabilizeTangent(
            Vector3 currentTangent,
            Vector3 previousTangent,
            Vector3 normal,
            float smoothingFactor)
        {
            // Blend current and previous tangents
            Vector3 blended = Vector3.Lerp(currentTangent, previousTangent, smoothingFactor);

            // Project back onto triangle plane (perpendicular to normal)
            blended = (blended - Vector3.Dot(blended, normal) * normal).normalized;

            // Check for flip (dot product with previous should be positive)
            if (Vector3.Dot(blended, previousTangent) < 0f)
            {
                // Flip detected, use previous tangent
                return previousTangent;
            }

            return blended;
        }

        /// <summary>
        /// Apply Gram-Schmidt orthonormalization.
        /// </summary>
        private static SurfaceFrame OrthonormalizeFrame(SurfaceFrame frame)
        {
            SurfaceFrame orthonormal = frame;

            // 1. Normalize normal (should already be normalized, but be safe)
            orthonormal.normal = frame.normal.normalized;

            // 2. Orthogonalize tangent (remove component parallel to normal)
            orthonormal.tangent = (frame.tangent - Vector3.Dot(frame.tangent, orthonormal.normal) * orthonormal.normal).normalized;

            // 3. Recalculate binormal as cross product
            orthonormal.binormal = Vector3.Cross(orthonormal.normal, orthonormal.tangent).normalized;

            // Rebuild rotation matrix
            Matrix4x4 matrix = new Matrix4x4();
            matrix.SetColumn(0, orthonormal.tangent);
            matrix.SetColumn(1, orthonormal.binormal);
            matrix.SetColumn(2, orthonormal.normal);
            matrix.SetColumn(3, new Vector4(0, 0, 0, 1));

            orthonormal.rotation = matrix.rotation;

            return orthonormal;
        }

        /// <summary>
        /// Build a stable reference tangent from a persistent direction.
        /// </summary>
        public static Vector3 BuildReferenceTangent(Vector3 normal, Vector3 worldUp)
        {
            // Project world up onto the surface plane
            Vector3 tangent = worldUp - Vector3.Dot(worldUp, normal) * normal;

            if (tangent.magnitude < 0.1f)
            {
                // If parallel to normal, use world forward instead
                tangent = Vector3.forward - Vector3.Dot(Vector3.forward, normal) * normal;
            }

            return tangent.normalized;
        }

        /// <summary>
        /// Calculate transform delta between two frames (for animation).
        /// </summary>
        public static void CalculateTransformDelta(
            SurfaceFrame baseFrame,
            SurfaceFrame deformedFrame,
            Transform targetMeshTransform,
            out Vector3 positionDelta,
            out Quaternion rotationDelta)
        {
            // Position delta in world space
            Vector3 baseWorldPos = targetMeshTransform.TransformPoint(baseFrame.position);
            Vector3 deformedWorldPos = targetMeshTransform.TransformPoint(deformedFrame.position);
            positionDelta = deformedWorldPos - baseWorldPos;

            // Rotation delta
            Quaternion baseWorldRot = targetMeshTransform.rotation * baseFrame.rotation;
            Quaternion deformedWorldRot = targetMeshTransform.rotation * deformedFrame.rotation;
            rotationDelta = deformedWorldRot * Quaternion.Inverse(baseWorldRot);
        }

        /// <summary>
        /// Solve using Cage/RBF mode: Radial Basis Function deformation for larger meshes.
        /// Defines driver points on the surface and applies smooth deformation to the entire object.
        /// </summary>
        public static SolverResult SolveCageRBF(
            Vector3[] objectVertices,
            SurfaceCluster cluster,
            Vector3[] baseClusterVertices,
            Vector3[] deformedClusterVertices,
            int[] clusterTriangleIndices,
            int driverPointCount,
            float radiusMultiplier,
            bool useGPU = true)
        {
            SolverResult result = new SolverResult
            {
                scale = Vector3.one,
                success = false
            };

            try
            {
                // Extract driver points from the surface cluster
                List<Vector3> baseDriverPoints = SelectDriverPoints(baseClusterVertices, driverPointCount);
                List<Vector3> deformedDriverPoints = SelectDriverPoints(deformedClusterVertices, driverPointCount);

                if (baseDriverPoints.Count != deformedDriverPoints.Count)
                {
                    result.errorMessage = "Driver point count mismatch";
                    return result;
                }

                // Calculate average radius for RBF kernel
                float avgRadius = CalculateAverageDriverDistance(baseDriverPoints) * radiusMultiplier;

                // Build RBF weights matrix
                RBFWeights weights = ComputeRBFWeights(baseDriverPoints, avgRadius);

                // Apply RBF deformation to object vertices
                Vector3[] deformedObjectVertices = ApplyRBFDeformation(
                    objectVertices,
                    baseDriverPoints,
                    deformedDriverPoints,
                    weights,
                    avgRadius,
                    useGPU);

                // Calculate centroid and rotation from deformed vertices
                Vector3 baseCentroid = CalculateCentroid(objectVertices);
                Vector3 deformedCentroid = CalculateCentroid(deformedObjectVertices);

                // For now, use rigid rotation from the first driver point
                // A full implementation would calculate rotation from multiple points
                Vector3 baseDir = (baseDriverPoints[1] - baseDriverPoints[0]).normalized;
                Vector3 deformedDir = (deformedDriverPoints[1] - deformedDriverPoints[0]).normalized;
                
                Quaternion rotation = Quaternion.FromToRotation(baseDir, deformedDir);

                result.position = deformedCentroid;
                result.rotation = rotation;
                result.success = true;
            }
            catch (Exception ex)
            {
                result.errorMessage = $"Cage/RBF solver failed: {ex.Message}";
            }

            return result;
        }

        private static List<Vector3> SelectDriverPoints(Vector3[] vertices, int count)
        {
            List<Vector3> drivers = new List<Vector3>();

            if (vertices.Length <= count)
            {
                drivers.AddRange(vertices);
                return drivers;
            }

            // Use farthest point sampling for good coverage
            List<int> selectedIndices = new List<int>();
            selectedIndices.Add(0); // Start with first vertex

            for (int i = 1; i < count; i++)
            {
                int farthestIndex = -1;
                float maxMinDistance = -1f;

                // Find vertex farthest from all selected vertices
                for (int v = 0; v < vertices.Length; v++)
                {
                    if (selectedIndices.Contains(v)) continue;

                    float minDistance = float.MaxValue;
                    foreach (int selectedIdx in selectedIndices)
                    {
                        float dist = Vector3.Distance(vertices[v], vertices[selectedIdx]);
                        if (dist < minDistance)
                        {
                            minDistance = dist;
                        }
                    }

                    if (minDistance > maxMinDistance)
                    {
                        maxMinDistance = minDistance;
                        farthestIndex = v;
                    }
                }

                if (farthestIndex >= 0)
                {
                    selectedIndices.Add(farthestIndex);
                }
            }

            foreach (int idx in selectedIndices)
            {
                drivers.Add(vertices[idx]);
            }

            return drivers;
        }

        private static float CalculateAverageDriverDistance(List<Vector3> points)
        {
            if (points.Count < 2) return 1f;

            float totalDistance = 0f;
            int count = 0;

            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    totalDistance += Vector3.Distance(points[i], points[j]);
                    count++;
                }
            }

            return count > 0 ? totalDistance / count : 1f;
        }

        private struct RBFWeights
        {
            public float[,] matrix; // Inverse of the RBF kernel matrix
            public int size;
        }

        private static RBFWeights ComputeRBFWeights(List<Vector3> driverPoints, float radius)
        {
            int n = driverPoints.Count;
            RBFWeights weights = new RBFWeights
            {
                size = n,
                matrix = new float[n, n]
            };

            // Build kernel matrix
            float[,] kernel = new float[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    float dist = Vector3.Distance(driverPoints[i], driverPoints[j]);
                    kernel[i, j] = RBFKernel(dist, radius);
                }
            }

            // Invert matrix (simple Gaussian elimination for small matrices)
            weights.matrix = InvertMatrix(kernel, n);

            return weights;
        }

        private static float RBFKernel(float distance, float radius)
        {
            // Gaussian RBF kernel
            if (radius < 0.0001f) return distance < 0.0001f ? 1f : 0f;
            
            float r2 = distance * distance;
            float sigma2 = radius * radius;
            return Mathf.Exp(-r2 / (2f * sigma2));
        }

        private static float[,] InvertMatrix(float[,] matrix, int n)
        {
            // Create augmented matrix [A | I]
            float[,] aug = new float[n, 2 * n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    aug[i, j] = matrix[i, j];
                }
                aug[i, n + i] = 1f; // Identity
            }

            // Gaussian elimination
            for (int i = 0; i < n; i++)
            {
                // Find pivot
                float maxVal = Mathf.Abs(aug[i, i]);
                int maxRow = i;
                for (int k = i + 1; k < n; k++)
                {
                    if (Mathf.Abs(aug[k, i]) > maxVal)
                    {
                        maxVal = Mathf.Abs(aug[k, i]);
                        maxRow = k;
                    }
                }

                // Swap rows
                if (maxRow != i)
                {
                    for (int k = 0; k < 2 * n; k++)
                    {
                        float tmp = aug[i, k];
                        aug[i, k] = aug[maxRow, k];
                        aug[maxRow, k] = tmp;
                    }
                }

                // Scale pivot row
                float pivot = aug[i, i];
                if (Mathf.Abs(pivot) < 0.00001f)
                {
                    pivot = 0.00001f;
                }

                for (int k = 0; k < 2 * n; k++)
                {
                    aug[i, k] /= pivot;
                }

                // Eliminate column
                for (int k = 0; k < n; k++)
                {
                    if (k != i)
                    {
                        float factor = aug[k, i];
                        for (int j = 0; j < 2 * n; j++)
                        {
                            aug[k, j] -= factor * aug[i, j];
                        }
                    }
                }
            }

            // Extract inverse from augmented matrix
            float[,] inverse = new float[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    inverse[i, j] = aug[i, n + j];
                }
            }

            return inverse;
        }

        private static Vector3[] ApplyRBFDeformation(
            Vector3[] vertices,
            List<Vector3> baseDrivers,
            List<Vector3> deformedDrivers,
            RBFWeights weights,
            float radius,
            bool useGPU)
        {
            int n = baseDrivers.Count;
            Vector3[] result = new Vector3[vertices.Length];

            // Calculate displacement vectors for each driver
            Vector3[] displacements = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                displacements[i] = deformedDrivers[i] - baseDrivers[i];
            }

            // For each vertex, interpolate displacement using RBF
            for (int v = 0; v < vertices.Length; v++)
            {
                Vector3 vertex = vertices[v];
                Vector3 totalDisplacement = Vector3.zero;

                // Calculate RBF interpolation
                for (int i = 0; i < n; i++)
                {
                    float kernelValue = 0f;

                    // Sum weighted kernel contributions
                    for (int j = 0; j < n; j++)
                    {
                        float dist = Vector3.Distance(vertex, baseDrivers[j]);
                        float kernel = RBFKernel(dist, radius);
                        kernelValue += weights.matrix[i, j] * kernel;
                    }

                    totalDisplacement += displacements[i] * kernelValue;
                }

                result[v] = vertex + totalDisplacement;
            }

            return result;
        }

        private static Vector3 CalculateCentroid(Vector3[] vertices)
        {
            if (vertices.Length == 0) return Vector3.zero;

            Vector3 sum = Vector3.zero;
            foreach (var v in vertices)
            {
                sum += v;
            }

            return sum / vertices.Length;
        }
    }
}

