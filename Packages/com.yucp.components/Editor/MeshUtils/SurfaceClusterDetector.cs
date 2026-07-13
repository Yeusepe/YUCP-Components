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
            public Vector3 closestPoint;
            public Vector3 closestBarycentric;
            public float distanceToTarget;

            public TriangleData(int index, Vector3 vertex0, Vector3 vertex1, Vector3 vertex2, Vector3 targetPos)
            {
                triIndex = index;
                v0 = vertex0;
                v1 = vertex1;
                v2 = vertex2;
                center = (v0 + v1 + v2) / 3f;
                normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                closestBarycentric = ClosestPointBarycentric(targetPos, v0, v1, v2);
                closestPoint = closestBarycentric.x * v0 + closestBarycentric.y * v1 + closestBarycentric.z * v2;
                distanceToTarget = Vector3.Distance(closestPoint, targetPos);
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
            int manualTriangleIndex = -1,
            IReadOnlyCollection<string> preferredBlendshapes = null)
        {
            if (targetMesh == null || targetMesh.sharedMesh == null)
            {
                Debug.LogError("[SurfaceClusterDetector] Invalid target mesh");
                return null;
            }

            Mesh mesh = targetMesh.sharedMesh;
            Vector3[] vertices = GetCurrentPoseVertices(targetMesh, mesh);
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

                // Degenerate triangles have no surface frame. They can still be closest
                // by center distance, but selecting them makes position appear to work
                // while rotation falls back to an arbitrary axis.
                if (triData.normal.sqrMagnitude <= 1e-10f)
                {
                    continue;
                }

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

            TriangleData seed = SelectDeformationAwareSeed(mesh, allTriangles, triangles, preferredBlendshapes);
            if (manualTriangleIndex >= 0 && manualTriangleIndex < triangleCount)
            {
                int manualIndex = allTriangles.FindIndex(t => t.triIndex == manualTriangleIndex);
                if (manualIndex >= 0) seed = allTriangles[manualIndex];
            }

            // Grow a coherent patch from one seed instead of taking globally-nearest
            // triangles. Around a mouth or eyelid, disconnected surfaces can be closer
            // in Euclidean space than the next triangle on the intended surface.
            int requestedClusterSize = Mathf.Clamp(clusterSize, 1, allTriangles.Count);
            var selectedTriangles = new List<TriangleData> { seed };
            var selectedIndices = new HashSet<int> { seed.triIndex };
            while (selectedTriangles.Count < requestedClusterSize)
            {
                int bestCandidate = -1;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < allTriangles.Count; i++)
                {
                    TriangleData candidate = allTriangles[i];
                    if (selectedIndices.Contains(candidate.triIndex)) continue;
                    if (Mathf.Abs(Vector3.Dot(candidate.normal, seed.normal)) < 0.25f) continue;
                    if (!selectedTriangles.Any(selected => ShareEdge(selected.triIndex, candidate.triIndex, triangles, vertices))) continue;
                    if (candidate.distanceToTarget >= bestDistance) continue;
                    bestDistance = candidate.distanceToTarget;
                    bestCandidate = i;
                }

                if (bestCandidate < 0) break;
                TriangleData next = allTriangles[bestCandidate];
                selectedTriangles.Add(next);
                selectedIndices.Add(next.triIndex);
            }

            // Calculate barycentric coordinates for target position relative to each triangle
            float totalWeight = 0f;
            Vector3 clusterCenter = Vector3.zero;
            Vector3 clusterNormal = Vector3.zero;
            Vector3 clusterReferenceNormal = Vector3.zero;

            foreach (var tri in selectedTriangles)
            {
                Vector3 bary = tri.closestBarycentric;

                // Weight calculation: inverse distance with smoothing
                // Add small epsilon
                float weight = 1f / (tri.distanceToTarget + 0.001f);
                totalWeight += weight;

                TriangleAnchor anchor = new TriangleAnchor(tri.triIndex, bary, weight);
                cluster.anchors.Add(anchor);

                clusterCenter += tri.closestPoint * weight;
                Vector3 coherentNormal = tri.normal;
                if (coherentNormal.sqrMagnitude > 1e-10f)
                {
                    if (clusterReferenceNormal.sqrMagnitude <= 1e-10f) clusterReferenceNormal = coherentNormal;
                    else if (Vector3.Dot(coherentNormal, clusterReferenceNormal) < 0f) coherentNormal = -coherentNormal;
                }
                clusterNormal += coherentNormal * weight;
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

        private static Vector3[] GetCurrentPoseVertices(SkinnedMeshRenderer renderer, Mesh fallback)
        {
            var baked = new Mesh { name = "__YUCP_SurfaceCluster_CurrentPose" };
            try
            {
                renderer.BakeMesh(baked);
                Vector3[] vertices = baked.vertices;
                if (vertices != null && vertices.Length == fallback.vertexCount) return vertices;
            }
            catch
            {
                // Validation elsewhere reports unusable renderer/mesh data. Cluster
                // detection can still attempt the imported mesh as a fallback.
            }
            finally
            {
                if (Application.isPlaying) Object.Destroy(baked);
                else Object.DestroyImmediate(baked);
            }
            return fallback.vertices;
        }

        private static TriangleData SelectDeformationAwareSeed(
            Mesh mesh,
            List<TriangleData> sortedTriangles,
            int[] triangles,
            IReadOnlyCollection<string> preferredBlendshapes)
        {
            TriangleData nearest = sortedTriangles[0];
            if (preferredBlendshapes == null || preferredBlendshapes.Count == 0 || mesh.blendShapeCount == 0)
                return nearest;

            float ambiguityBand = Mathf.Max(0.006f, nearest.distanceToTarget * 2f);
            List<TriangleData> candidates = sortedTriangles
                .TakeWhile(triangle => triangle.distanceToTarget <= nearest.distanceToTarget + ambiguityBand)
                .ToList();
            if (candidates.Count <= 1) return nearest;

            var scores = new float[candidates.Count];
            var deltaVertices = new Vector3[mesh.vertexCount];
            var deltaNormals = new Vector3[mesh.vertexCount];
            var deltaTangents = new Vector3[mesh.vertexCount];
            foreach (string shapeName in preferredBlendshapes)
            {
                int shapeIndex = mesh.GetBlendShapeIndex(shapeName);
                if (shapeIndex < 0 || mesh.GetBlendShapeFrameCount(shapeIndex) == 0) continue;
                int frame = mesh.GetBlendShapeFrameCount(shapeIndex) - 1;
                mesh.GetBlendShapeFrameVertices(shapeIndex, frame, deltaVertices, deltaNormals, deltaTangents);
                for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                {
                    TriangleData candidate = candidates[candidateIndex];
                    int triangleOffset = candidate.triIndex * 3;
                    int i0 = triangles[triangleOffset];
                    int i1 = triangles[triangleOffset + 1];
                    int i2 = triangles[triangleOffset + 2];
                    Vector3 bary = candidate.closestBarycentric;
                    Vector3 translation = deltaVertices[i0] * bary.x + deltaVertices[i1] * bary.y + deltaVertices[i2] * bary.z;
                    float differential = Mathf.Max(
                        (deltaVertices[i0] - deltaVertices[i1]).magnitude,
                        Mathf.Max((deltaVertices[i1] - deltaVertices[i2]).magnitude,
                                  (deltaVertices[i2] - deltaVertices[i0]).magnitude));
                    scores[candidateIndex] = Mathf.Max(scores[candidateIndex], translation.magnitude + differential * 0.5f);
                }
            }

            float maximumMotion = scores.Max();
            if (maximumMotion <= 1e-6f) return nearest;
            float meaningfulMotion = maximumMotion * 0.5f;
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (scores[i] < meaningfulMotion || candidates[i].distanceToTarget >= bestDistance) continue;
                best = i;
                bestDistance = candidates[i].distanceToTarget;
            }
            return best >= 0 ? candidates[best] : nearest;
        }

        private static bool ShareEdge(int triangleA, int triangleB, int[] triangles, Vector3[] vertices)
        {
            int a = triangleA * 3;
            int b = triangleB * 3;
            int shared = 0;
            for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                if (triangles[a + i] == triangles[b + j]) shared++;
            if (shared >= 2) return true;

            // Imported meshes often split vertices at UV/normal seams. Treat two
            // coincident endpoints as the same topological edge.
            int coincident = 0;
            for (int i = 0; i < 3; i++)
            {
                Vector3 point = vertices[triangles[a + i]];
                for (int j = 0; j < 3; j++)
                {
                    if ((point - vertices[triangles[b + j]]).sqrMagnitude > 1e-10f) continue;
                    coincident++;
                    break;
                }
            }
            return coincident >= 2;
        }

        private static Vector3 ClosestPointBarycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = p - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return new Vector3(1f, 0f, 0f);

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return new Vector3(0f, 1f, 0f);

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return new Vector3(1f - v, v, 0f);
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return new Vector3(0f, 0f, 1f);

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return new Vector3(1f - w, 0f, w);
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return new Vector3(0f, 1f - w, w);
            }

            float denominator = 1f / (va + vb + vc);
            float baryV = vb * denominator;
            float baryW = vc * denominator;
            return new Vector3(1f - baryV - baryW, baryV, baryW);
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
            Vector3 referenceNormal = Vector3.zero;
            Vector3 referenceTangent = Vector3.zero;

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
                if (triNormal.sqrMagnitude > 1e-10f)
                {
                    if (referenceNormal.sqrMagnitude <= 1e-10f) referenceNormal = triNormal;
                    else if (Vector3.Dot(triNormal, referenceNormal) < 0f) triNormal = -triNormal;
                }

                // Meshes commonly contain coincident triangles with opposite winding.
                // Keep their tangent direction coherent as well, otherwise the surface
                // frame can retain position while its rotation axes cancel to zero.
                Vector3 triTangent = Vector3.ProjectOnPlane(v1 - v0, triNormal).normalized;
                if (triTangent.sqrMagnitude > 1e-10f)
                {
                    if (referenceTangent.sqrMagnitude <= 1e-10f) referenceTangent = triTangent;
                    else if (Vector3.Dot(triTangent, referenceTangent) < 0f) triTangent = -triTangent;
                }

                // Weight contribution
                position += anchorPos * anchor.weight;
                normal += triNormal * anchor.weight;
                edge0Sum += triTangent * anchor.weight;
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


