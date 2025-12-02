using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Detects and creates surface clusters (multiple triangles) for robust attachment points.
    /// Uses inverse distance weighting to create stable attachment frames that survive mesh deformation.
    /// </summary>
    public static class SurfaceClusterDetector
    {
        public struct TriangleData
        {
            public int triIndex;
            public Vector3 v0, v1, v2;
            public Vector3 center;
            public Vector3 normal;
            public float distanceToTarget;

            public TriangleData(int index, Vector3 vertex0, Vector3 vertex1, Vector3 vertex2, Vector3 targetPos)
            {
                triIndex = index;
                v0 = vertex0;
                v1 = vertex1;
                v2 = vertex2;
                center = (v0 + v1 + v2) / 3f;
                normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                distanceToTarget = Vector3.Distance(center, targetPos);
            }
        }

        /// <summary>
        /// Detect a surface cluster around the target position.
        /// </summary>
        public static SurfaceCluster DetectCluster(
            SkinnedMeshRenderer targetMesh,
            Vector3 targetPosition,
            int clusterSize,
            float searchRadius,
            int manualTriangleIndex = -1)
        {
            if (targetMesh == null || targetMesh.sharedMesh == null)
            {
                Debug.LogError("[SurfaceClusterDetector] Invalid target mesh");
                return null;
            }

            Mesh mesh = targetMesh.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            int triangleCount = triangles.Length / 3;

            // Convert target position to local space
            Vector3 localTarget = targetMesh.transform.InverseTransformPoint(targetPosition);

            // Build list of all triangles with distance data
            List<TriangleData> allTriangles = new List<TriangleData>();
            
            for (int i = 0; i < triangleCount; i++)
            {
                int idx0 = triangles[i * 3];
                int idx1 = triangles[i * 3 + 1];
                int idx2 = triangles[i * 3 + 2];

                Vector3 v0 = vertices[idx0];
                Vector3 v1 = vertices[idx1];
                Vector3 v2 = vertices[idx2];

                TriangleData triData = new TriangleData(i, v0, v1, v2, localTarget);

                // Filter by search radius if specified
                if (searchRadius > 0 && triData.distanceToTarget > searchRadius)
                {
                    continue;
                }

                allTriangles.Add(triData);
            }

            if (allTriangles.Count == 0)
            {
                Debug.LogError($"[SurfaceClusterDetector] No triangles found within search radius {searchRadius}m");
                return null;
            }

            // Sort by distance
            allTriangles = allTriangles.OrderBy(t => t.distanceToTarget).ToList();

            // Create cluster
            SurfaceCluster cluster = new SurfaceCluster();
            cluster.anchors = new List<TriangleAnchor>();

            // If manual triangle is specified and valid, make it the primary anchor
            if (manualTriangleIndex >= 0 && manualTriangleIndex < triangleCount)
            {
                var manualTri = allTriangles.FirstOrDefault(t => t.triIndex == manualTriangleIndex);
                if (manualTri.triIndex == manualTriangleIndex)
                {
                    // Move manual triangle to front
                    allTriangles.Remove(manualTri);
                    allTriangles.Insert(0, manualTri);
                }
            }

            // Take the K closest triangles
            int actualClusterSize = Mathf.Min(clusterSize, allTriangles.Count);
            List<TriangleData> selectedTriangles = allTriangles.Take(actualClusterSize).ToList();

            // Calculate barycentric coordinates for target position relative to each triangle
            float totalWeight = 0f;
            Vector3 clusterCenter = Vector3.zero;
            Vector3 clusterNormal = Vector3.zero;

            foreach (var tri in selectedTriangles)
            {
                // Calculate barycentric coordinates
                Vector3 bary = CalculateBarycentricCoordinates(localTarget, tri.v0, tri.v1, tri.v2);

                // Weight calculation: inverse distance with smoothing
                // Add small epsilon
                float weight = 1f / (tri.distanceToTarget + 0.001f);
                totalWeight += weight;

                TriangleAnchor anchor = new TriangleAnchor(tri.triIndex, bary, weight);
                cluster.anchors.Add(anchor);

                clusterCenter += tri.center * weight;
                clusterNormal += tri.normal * weight;
            }

            // Normalize weights
            cluster.totalWeight = totalWeight;
            foreach (var anchor in cluster.anchors)
            {
                anchor.weight /= totalWeight;
            }

            cluster.centerPosition = clusterCenter / totalWeight;
            cluster.averageNormal = (clusterNormal / totalWeight).normalized;

            Debug.Log($"[SurfaceClusterDetector] Created cluster with {cluster.anchors.Count} triangles, " +
                     $"center: {cluster.centerPosition}, avgDist: {selectedTriangles.Average(t => t.distanceToTarget):F4}m");

            return cluster;
        }

        /// <summary>
        /// Calculate barycentric coordinates of point P within triangle (A, B, C).
        /// Returns (u, v, w) where P = u*A + v*B + w*C and u+v+w=1
        /// </summary>
        private static Vector3 CalculateBarycentricCoordinates(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 v0 = b - a;
            Vector3 v1 = c - a;
            Vector3 v2 = p - a;

            float d00 = Vector3.Dot(v0, v0);
            float d01 = Vector3.Dot(v0, v1);
            float d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0);
            float d21 = Vector3.Dot(v2, v1);

            float denom = d00 * d11 - d01 * d01;
            
            if (Mathf.Abs(denom) < 0.00001f)
            {
                // Degenerate triangle, return center
                return new Vector3(0.333f, 0.333f, 0.333f);
            }

            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            float u = 1.0f - v - w;

            return new Vector3(u, v, w);
        }

        /// <summary>
        /// Evaluate cluster position and normal at a given mesh state.
        /// </summary>
        public static void EvaluateCluster(
            SurfaceCluster cluster,
            Vector3[] vertices,
            int[] triangles,
            out Vector3 position,
            out Vector3 normal,
            out Vector3 tangent)
        {
            position = Vector3.zero;
            normal = Vector3.zero;
            Vector3 edge0Sum = Vector3.zero;

            foreach (var anchor in cluster.anchors)
            {
                int idx0 = triangles[anchor.triIndex * 3];
                int idx1 = triangles[anchor.triIndex * 3 + 1];
                int idx2 = triangles[anchor.triIndex * 3 + 2];

                Vector3 v0 = vertices[idx0];
                Vector3 v1 = vertices[idx1];
                Vector3 v2 = vertices[idx2];

                // Interpolate position using barycentric coordinates
                Vector3 anchorPos = anchor.barycentric.x * v0 +
                                   anchor.barycentric.y * v1 +
                                   anchor.barycentric.z * v2;

                // Calculate triangle normal
                Vector3 triNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                // Weight contribution
                position += anchorPos * anchor.weight;
                normal += triNormal * anchor.weight;
                edge0Sum += (v1 - v0).normalized * anchor.weight;
            }

            normal = normal.normalized;
            
            // Calculate tangent by projecting edge direction onto triangle plane
            Vector3 edgeDirection = edge0Sum.normalized;
            tangent = (edgeDirection - Vector3.Dot(edgeDirection, normal) * normal).normalized;

            // Fallback if tangent is degenerate
            if (tangent.magnitude < 0.1f)
            {
                tangent = Vector3.Cross(normal, Vector3.up).normalized;
                if (tangent.magnitude < 0.1f)
                {
                    tangent = Vector3.Cross(normal, Vector3.forward).normalized;
                }
            }
        }

        /// <summary>
        /// Find the closest triangle to a point.
        /// </summary>
        public static int FindClosestTriangle(
            Mesh mesh,
            Vector3 localPosition,
            float searchRadius = 0f)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            int triangleCount = triangles.Length / 3;

            int closestIndex = -1;
            float closestDistance = float.MaxValue;

            for (int i = 0; i < triangleCount; i++)
            {
                int idx0 = triangles[i * 3];
                int idx1 = triangles[i * 3 + 1];
                int idx2 = triangles[i * 3 + 2];

                Vector3 center = (vertices[idx0] + vertices[idx1] + vertices[idx2]) / 3f;
                float distance = Vector3.Distance(center, localPosition);

                if (searchRadius > 0 && distance > searchRadius)
                {
                    continue;
                }

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = i;
                }
            }

            return closestIndex;
        }
    }
}


