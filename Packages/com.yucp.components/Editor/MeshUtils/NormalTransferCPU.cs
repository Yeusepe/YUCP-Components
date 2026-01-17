using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// CPU implementation of normal transfer methods.
    /// Supports proximity matching, projection-based transfer, and shared normal field computation.
    /// </summary>
    public static class NormalTransferCPU
    {
        /// <summary>
        /// Transfers normals from source meshes to target meshes using the specified method.
        /// </summary>
        public static void TransferNormals(
            Renderer[] sourceRenderers,
            Renderer[] targetRenderers,
            NormalBakeSettings settings,
            bool debugMode = false)
        {
            if (sourceRenderers == null || sourceRenderers.Length == 0)
            {
                Debug.LogError("[NormalTransferCPU] No source meshes provided.");
                return;
            }

            if (targetRenderers == null || targetRenderers.Length == 0)
            {
                Debug.LogError("[NormalTransferCPU] No target meshes provided.");
                return;
            }

            // Extract mesh data from source renderers
            var sourceMeshes = new List<Mesh>();
            var sourceTransforms = new List<Transform>();
            
            foreach (var renderer in sourceRenderers)
            {
                Mesh mesh = GetMeshFromRenderer(renderer);
                if (mesh != null)
                {
                    sourceMeshes.Add(mesh);
                    sourceTransforms.Add(renderer.transform);
                }
            }

            if (sourceMeshes.Count == 0)
            {
                Debug.LogError("[NormalTransferCPU] No valid source meshes found.");
                return;
            }

            // Process each target mesh
            foreach (var targetRenderer in targetRenderers)
            {
                Mesh originalMesh = GetMeshFromRenderer(targetRenderer);
                if (originalMesh == null) continue;

                // Create mesh instance if it's a shared mesh (asset)
                Mesh targetMesh = originalMesh;
                bool isSharedMesh = AssetDatabase.Contains(originalMesh);
                
                if (isSharedMesh)
                {
                    targetMesh = UnityEngine.Object.Instantiate(originalMesh);
                    targetMesh.name = originalMesh.name + "_SeamlessNormals";
                    
                    // Assign the instance to the renderer
                    if (targetRenderer is SkinnedMeshRenderer smr)
                    {
                        smr.sharedMesh = targetMesh;
                    }
                    else if (targetRenderer is MeshRenderer mr)
                    {
                        MeshFilter mf = mr.GetComponent<MeshFilter>();
                        if (mf != null)
                        {
                            mf.sharedMesh = targetMesh;
                        }
                    }
                }

                Vector3[] targetVertices = targetMesh.vertices;
                Vector3[] targetNormals = targetMesh.normals;
                Transform targetTransform = targetRenderer.transform;

                if (targetNormals == null || targetNormals.Length != targetVertices.Length)
                {
                    targetNormals = new Vector3[targetVertices.Length];
                    for (int i = 0; i < targetNormals.Length; i++)
                    {
                        targetNormals[i] = Vector3.up;
                    }
                    targetMesh.normals = targetNormals;
                }

                Vector3[] newNormals = new Vector3[targetNormals.Length];
                Array.Copy(targetNormals, newNormals, targetNormals.Length);

                switch (settings.method)
                {
                    case NormalTransferMethod.Proximity:
                        TransferProximity(sourceMeshes.ToArray(), sourceTransforms.ToArray(),
                            targetMesh, targetTransform, settings, newNormals, debugMode);
                        break;
                    case NormalTransferMethod.Projection:
                        TransferProjection(sourceMeshes.ToArray(), sourceTransforms.ToArray(),
                            targetMesh, targetTransform, settings, newNormals, debugMode);
                        break;
                    case NormalTransferMethod.SharedField:
                        TransferSharedField(sourceMeshes.ToArray(), sourceTransforms.ToArray(),
                            targetMesh, targetTransform, settings, newNormals, debugMode);
                        break;
                }

                // Verify we actually changed some normals
                bool hasChanges = false;
                Vector3[] oldNormals = targetMesh.normals;
                if (oldNormals != null && oldNormals.Length == newNormals.Length)
                {
                    for (int i = 0; i < newNormals.Length; i++)
                    {
                        if (Vector3.Distance(oldNormals[i], newNormals[i]) > 0.0001f)
                        {
                            hasChanges = true;
                            break;
                        }
                    }
                }
                else
                {
                    hasChanges = true; // Normals didn't exist or length changed
                }
                
                if (hasChanges)
                {
                    // Apply new normals to mesh
                    targetMesh.normals = newNormals;
                    
                    // Recalculate tangents to match new normals
                    targetMesh.RecalculateTangents();
                    
                    // Also recalculate bounds in case normals changed significantly
                    targetMesh.RecalculateBounds();
                    
                    // Force mesh to upload new data to GPU
                    targetMesh.UploadMeshData(false);
                    
                    // Mark mesh as dirty
                    EditorUtility.SetDirty(targetMesh);
                    
                    if (debugMode)
                    {
                        Debug.Log($"[NormalTransferCPU] ✓ Applied normals to mesh '{targetMesh.name}' ({newNormals.Length} vertices)");
                    }
                }
                else if (debugMode)
                {
                    Debug.LogWarning($"[NormalTransferCPU] ⚠ No normals changed on mesh '{targetMesh.name}' - check proximity threshold and mesh positions");
                }
            }
        }

        /// <summary>
        /// Method 1: Proximity-based normal transfer with metaball-like smooth blending.
        /// Finds nearest surface point on source mesh and blends normals with distance-based falloff.
        /// </summary>
        private static void TransferProximity(
            Mesh[] sourceMeshes,
            Transform[] sourceTransforms,
            Mesh targetMesh,
            Transform targetTransform,
            NormalBakeSettings settings,
            Vector3[] targetNormals,
            bool debugMode)
        {
            Vector3[] targetVertices = targetMesh.vertices;
            int processedCount = 0;

            // Build spatial index for source mesh triangles (for surface projection)
            var triangleIndex = BuildTriangleSpatialIndex(sourceMeshes, sourceTransforms, settings.proximityThreshold);

            for (int i = 0; i < targetVertices.Length; i++)
            {
                Vector3 worldPos = targetTransform.TransformPoint(targetVertices[i]);
                Vector3 currentNormal = targetTransform.TransformDirection(targetNormals[i]).normalized;
                
                // Find nearest point on source mesh surface (metaball-like behavior)
                var nearestSurface = FindNearestSurfacePoint(worldPos, triangleIndex, sourceMeshes, sourceTransforms, settings.proximityThreshold);
                
                if (nearestSurface.HasValue)
                {
                    var (surfacePos, surfaceNormal, distance) = nearestSurface.Value;
                    
                    // Calculate blend strength based on distance (metaball falloff)
                    // Closer = stronger blend, with smooth falloff
                    float normalizedDistance = Mathf.Clamp01(distance / settings.proximityThreshold);
                    
                    // Use smoothstep for smooth metaball-like falloff
                    float blendFactor = 1f - Mathf.SmoothStep(0f, 1f, normalizedDistance);
                    
                    // Apply user's blend strength on top of distance-based blending
                    float finalBlend = Mathf.Lerp(blendFactor, 0f, settings.proximityBlendStrength);
                    
                    // Blend normals
                    Vector3 blended = Vector3.Slerp(currentNormal, surfaceNormal, finalBlend).normalized;
                    targetNormals[i] = targetTransform.InverseTransformDirection(blended);
                    processedCount++;
                }
            }

            if (debugMode)
            {
                Debug.Log($"[NormalTransferCPU] Proximity: Processed {processedCount}/{targetVertices.Length} vertices with metaball-like blending");
            }
        }

        /// <summary>
        /// Method 2: Projection-based normal transfer.
        /// Projects target vertices onto source mesh and samples normals.
        /// </summary>
        private static void TransferProjection(
            Mesh[] sourceMeshes,
            Transform[] sourceTransforms,
            Mesh targetMesh,
            Transform targetTransform,
            NormalBakeSettings settings,
            Vector3[] targetNormals,
            bool debugMode)
        {
            Vector3[] targetVertices = targetMesh.vertices;
            int processedCount = 0;

            // Create temporary mesh colliders for raycasting
            var colliders = new List<(MeshCollider collider, Transform transform)>();
            try
            {
                GameObject tempGO = new GameObject("TempNormalTransfer");
                tempGO.hideFlags = HideFlags.HideAndDontSave;

                for (int m = 0; m < sourceMeshes.Length; m++)
                {
                    GameObject colliderGO = new GameObject($"Collider_{m}");
                    colliderGO.hideFlags = HideFlags.HideAndDontSave;
                    colliderGO.transform.SetParent(tempGO.transform);
                    colliderGO.transform.position = sourceTransforms[m].position;
                    colliderGO.transform.rotation = sourceTransforms[m].rotation;
                    colliderGO.transform.localScale = sourceTransforms[m].lossyScale;
                    
                    MeshCollider collider = colliderGO.AddComponent<MeshCollider>();
                    collider.sharedMesh = sourceMeshes[m];
                    colliders.Add((collider, colliderGO.transform));
                }

                for (int i = 0; i < targetVertices.Length; i++)
                {
                    Vector3 worldPos = targetTransform.TransformPoint(targetVertices[i]);
                    Vector3 worldNormal = targetTransform.TransformDirection(targetNormals[i]).normalized;

                    Vector3? projectedNormal = null;
                    float minDistance = float.MaxValue;

                    // Try projection based on direction mode
                    if (settings.projectionDirection == SeamlessNormalsData.ProjectionDirection.VertexNormal ||
                        settings.projectionDirection == SeamlessNormalsData.ProjectionDirection.BothDirections)
                    {
                        // Project along vertex normal
                        Ray ray = new Ray(worldPos, worldNormal);
                        var hit = ProjectRay(ray, colliders, settings.projectionDistance);
                        if (hit.HasValue && hit.Value.distance < minDistance)
                        {
                            projectedNormal = hit.Value.normal;
                            minDistance = hit.Value.distance;
                        }
                    }

                    if (settings.projectionDirection == SeamlessNormalsData.ProjectionDirection.BothDirections)
                    {
                        // Try opposite direction
                        Ray ray = new Ray(worldPos, -worldNormal);
                        var hit = ProjectRay(ray, colliders, settings.projectionDistance);
                        if (hit.HasValue && hit.Value.distance < minDistance)
                        {
                            projectedNormal = hit.Value.normal;
                            minDistance = hit.Value.distance;
                        }
                    }
                    
                    // Also try projecting from source surface normal if available
                    if (settings.projectionDirection == SeamlessNormalsData.ProjectionDirection.SurfaceNormal)
                    {
                        // Find closest point on source mesh and use its normal
                        Vector3 closestPos = worldPos;
                        Vector3 closestNormal = worldNormal;
                        float closestDistSq = float.MaxValue;
                        
                        for (int m = 0; m < sourceMeshes.Length; m++)
                        {
                            Mesh mesh = sourceMeshes[m];
                            Transform trans = sourceTransforms[m];
                            Vector3[] verts = mesh.vertices;
                            Vector3[] norms = mesh.normals ?? new Vector3[verts.Length];
                            
                            for (int j = 0; j < verts.Length; j++)
                            {
                                Vector3 sourcePos = trans.TransformPoint(verts[j]);
                                float distSq = (worldPos - sourcePos).sqrMagnitude;
                                if (distSq < settings.projectionDistance * settings.projectionDistance && distSq < closestDistSq)
                                {
                                    closestDistSq = distSq;
                                    closestPos = sourcePos;
                                    closestNormal = trans.TransformDirection(norms[j]).normalized;
                                }
                            }
                        }
                        
                        if (closestDistSq < float.MaxValue)
                        {
                            projectedNormal = closestNormal;
                        }
                    }

                    if (projectedNormal.HasValue)
                    {
                        Vector3 currentNormal = targetTransform.TransformDirection(targetNormals[i]).normalized;
                        Vector3 blended = Vector3.Lerp(projectedNormal.Value, currentNormal, settings.projectionBlendStrength).normalized;
                        targetNormals[i] = targetTransform.InverseTransformDirection(blended);
                        processedCount++;
                    }
                }
            }
            finally
            {
                // Clean up temporary colliders
                if (colliders.Count > 0 && colliders[0].collider != null)
                {
                    GameObject parent = colliders[0].collider.transform.parent?.gameObject;
                    if (parent != null)
                    {
                        UnityEngine.Object.DestroyImmediate(parent);
                    }
                }
            }

            if (debugMode)
            {
                Debug.Log($"[NormalTransferCPU] Projection: Processed {processedCount}/{targetVertices.Length} vertices");
            }
        }

        /// <summary>
        /// Method 3: Shared normal field computation.
        /// Treats all meshes as one continuous surface and recomputes normals.
        /// </summary>
        private static void TransferSharedField(
            Mesh[] sourceMeshes,
            Transform[] sourceTransforms,
            Mesh targetMesh,
            Transform targetTransform,
            NormalBakeSettings settings,
            Vector3[] targetNormals,
            bool debugMode)
        {
            // Combine all meshes (source + target) for shared field computation
            var allMeshes = new List<Mesh>(sourceMeshes) { targetMesh };
            var allTransforms = new List<Transform>(sourceTransforms) { targetTransform };

            // Compute shared normals
            var sharedNormals = MeshAdjacency.ComputeSharedNormals(
                allMeshes.ToArray(),
                allTransforms.ToArray(),
                settings.sharedFieldPositionThreshold,
                settings.sharedFieldHardEdgeAngle);

            // Apply shared normals to target mesh
            int meshIndex = sourceMeshes.Length; // Target mesh is last
            int processedCount = 0;

            for (int i = 0; i < targetMesh.vertices.Length; i++)
            {
                int key = meshIndex * 1000000 + i;
                if (sharedNormals.ContainsKey(key))
                {
                    Vector3 sharedNormal = sharedNormals[key];
                    targetNormals[i] = targetTransform.InverseTransformDirection(sharedNormal);
                    processedCount++;
                }
            }

            if (debugMode)
            {
                Debug.Log($"[NormalTransferCPU] SharedField: Processed {processedCount}/{targetMesh.vertices.Length} vertices");
            }
        }

        /// <summary>
        /// Builds a spatial index (hash grid) for fast nearest neighbor search.
        /// </summary>
        private static Dictionary<Vector3Int, List<(Vector3 position, Vector3 normal)>> BuildSpatialIndex(
            Mesh[] meshes,
            Transform[] transforms,
            float cellSize)
        {
            var index = new Dictionary<Vector3Int, List<(Vector3, Vector3)>>();

            for (int m = 0; m < meshes.Length; m++)
            {
                if (meshes[m] == null || transforms[m] == null) continue;

                Vector3[] vertices = meshes[m].vertices;
                Vector3[] normals = meshes[m].normals;
                Transform transform = transforms[m];

                if (normals == null || normals.Length != vertices.Length)
                {
                    // Generate default normals
                    normals = new Vector3[vertices.Length];
                    for (int i = 0; i < normals.Length; i++)
                    {
                        normals[i] = Vector3.up;
                    }
                }

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 worldPos = transform.TransformPoint(vertices[i]);
                    Vector3 worldNormal = transform.TransformDirection(normals[i]).normalized;
                    Vector3Int cell = QuantizePosition(worldPos, cellSize);

                    if (!index.ContainsKey(cell))
                    {
                        index[cell] = new List<(Vector3, Vector3)>();
                    }

                    index[cell].Add((worldPos, worldNormal));
                }
            }

            return index;
        }

        /// <summary>
        /// Finds the nearest vertex in the spatial index.
        /// </summary>
        private static (Vector3 position, Vector3 normal)? FindNearestVertex(
            Vector3 position,
            Dictionary<Vector3Int, List<(Vector3 position, Vector3 normal)>> spatialIndex,
            float maxDistance)
        {
            Vector3Int centerCell = QuantizePosition(position, maxDistance);
            float maxDistSq = maxDistance * maxDistance;
            (Vector3, Vector3)? nearest = null;
            float nearestDistSq = float.MaxValue;

            // Check center cell and neighboring cells
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        Vector3Int cell = centerCell + new Vector3Int(dx, dy, dz);
                        if (spatialIndex.ContainsKey(cell))
                        {
                            foreach (var (pos, normal) in spatialIndex[cell])
                            {
                                float distSq = (position - pos).sqrMagnitude;
                                if (distSq < maxDistSq && distSq < nearestDistSq)
                                {
                                    nearestDistSq = distSq;
                                    nearest = (pos, normal);
                                }
                            }
                        }
                    }
                }
            }

            return nearest;
        }

        /// <summary>
        /// Finds ALL vertices within the threshold distance (not just nearest).
        /// This is critical for seamless normal blending.
        /// </summary>
        private static List<(Vector3 position, Vector3 normal)> FindAllNearbyVertices(
            Vector3 position,
            Dictionary<Vector3Int, List<(Vector3 position, Vector3 normal)>> spatialIndex,
            float maxDistance)
        {
            Vector3Int centerCell = QuantizePosition(position, maxDistance);
            float maxDistSq = maxDistance * maxDistance;
            var nearby = new List<(Vector3, Vector3)>();

            // Check center cell and neighboring cells
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        Vector3Int cell = centerCell + new Vector3Int(dx, dy, dz);
                        if (spatialIndex.ContainsKey(cell))
                        {
                            foreach (var (pos, normal) in spatialIndex[cell])
                            {
                                float distSq = (position - pos).sqrMagnitude;
                                if (distSq <= maxDistSq)
                                {
                                    nearby.Add((pos, normal));
                                }
                            }
                        }
                    }
                }
            }

            return nearby;
        }

        /// <summary>
        /// Projects a ray onto mesh colliders and returns hit information.
        /// </summary>
        private static (Vector3 normal, float distance)? ProjectRay(
            Ray ray,
            List<(MeshCollider collider, Transform transform)> colliders,
            float maxDistance)
        {
            RaycastHit hit;
            (Vector3 normal, float distance)? bestHit = null;
            float minDistance = float.MaxValue;

            foreach (var (collider, transform) in colliders)
            {
                // Transform ray to collider's local space
                Ray localRay = new Ray(
                    transform.InverseTransformPoint(ray.origin),
                    transform.InverseTransformDirection(ray.direction)
                );
                
                if (collider.Raycast(ray, out hit, maxDistance))
                {
                    if (hit.distance < minDistance)
                    {
                        minDistance = hit.distance;
                        // Transform normal back to world space
                        Vector3 worldNormal = transform.TransformDirection(hit.normal).normalized;
                        bestHit = (worldNormal, hit.distance);
                    }
                }
            }

            return bestHit;
        }

        /// <summary>
        /// Quantizes a position to a grid cell.
        /// </summary>
        private static Vector3Int QuantizePosition(Vector3 position, float cellSize)
        {
            int x = Mathf.FloorToInt(position.x / cellSize);
            int y = Mathf.FloorToInt(position.y / cellSize);
            int z = Mathf.FloorToInt(position.z / cellSize);
            return new Vector3Int(x, y, z);
        }

        /// <summary>
        /// Triangle data for surface projection.
        /// </summary>
        private struct TriangleData
        {
            public Vector3 v0, v1, v2;
            public Vector3 n0, n1, n2;
            public int meshIndex;
        }

        /// <summary>
        /// Builds a spatial index for triangles to enable fast surface point queries.
        /// </summary>
        private static Dictionary<Vector3Int, List<TriangleData>> BuildTriangleSpatialIndex(
            Mesh[] meshes,
            Transform[] transforms,
            float cellSize)
        {
            var index = new Dictionary<Vector3Int, List<TriangleData>>();

            for (int m = 0; m < meshes.Length; m++)
            {
                if (meshes[m] == null || transforms[m] == null) continue;

                Mesh mesh = meshes[m];
                Transform transform = transforms[m];
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals ?? new Vector3[vertices.Length];
                int[] triangles = mesh.triangles;

                if (normals.Length != vertices.Length)
                {
                    normals = new Vector3[vertices.Length];
                    for (int i = 0; i < normals.Length; i++)
                    {
                        normals[i] = Vector3.up;
                    }
                }

                for (int t = 0; t < triangles.Length; t += 3)
                {
                    int i0 = triangles[t];
                    int i1 = triangles[t + 1];
                    int i2 = triangles[t + 2];

                    Vector3 v0 = transform.TransformPoint(vertices[i0]);
                    Vector3 v1 = transform.TransformPoint(vertices[i1]);
                    Vector3 v2 = transform.TransformPoint(vertices[i2]);

                    Vector3 n0 = transform.TransformDirection(normals[i0]).normalized;
                    Vector3 n1 = transform.TransformDirection(normals[i1]).normalized;
                    Vector3 n2 = transform.TransformDirection(normals[i2]).normalized;

                    TriangleData tri = new TriangleData
                    {
                        v0 = v0,
                        v1 = v1,
                        v2 = v2,
                        n0 = n0,
                        n1 = n1,
                        n2 = n2,
                        meshIndex = m
                    };

                    // Add triangle to all cells it overlaps
                    Vector3 center = (v0 + v1 + v2) / 3f;
                    Vector3Int cell = QuantizePosition(center, cellSize);

                    // Check neighboring cells too (triangle might span multiple cells)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                Vector3Int key = cell + new Vector3Int(dx, dy, dz);
                                if (!index.ContainsKey(key))
                                {
                                    index[key] = new List<TriangleData>();
                                }
                                index[key].Add(tri);
                            }
                        }
                    }
                }
            }

            return index;
        }

        /// <summary>
        /// Finds the nearest point on the source mesh surface and returns its position, normal, and distance.
        /// Uses barycentric coordinates for smooth interpolation.
        /// </summary>
        private static (Vector3 position, Vector3 normal, float distance)? FindNearestSurfacePoint(
            Vector3 point,
            Dictionary<Vector3Int, List<TriangleData>> triangleIndex,
            Mesh[] sourceMeshes,
            Transform[] sourceTransforms,
            float maxDistance)
        {
            Vector3Int centerCell = QuantizePosition(point, maxDistance);
            float maxDistSq = maxDistance * maxDistance;
            (Vector3 position, Vector3 normal, float distance)? best = null;
            float bestDistSq = float.MaxValue;

            // Check center cell and neighboring cells
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        Vector3Int cell = centerCell + new Vector3Int(dx, dy, dz);
                        if (triangleIndex.ContainsKey(cell))
                        {
                            foreach (var tri in triangleIndex[cell])
                            {
                                // Project point onto triangle plane
                                var projection = ProjectPointOntoTriangle(point, tri.v0, tri.v1, tri.v2);
                                
                                if (projection.HasValue)
                                {
                                    var (projPos, u, v, w) = projection.Value;
                                    float distSq = (point - projPos).sqrMagnitude;

                                    if (distSq < maxDistSq && distSq < bestDistSq)
                                    {
                                        // Interpolate normal using barycentric coordinates
                                        Vector3 interpNormal = (tri.n0 * w + tri.n1 * u + tri.n2 * v).normalized;
                                        bestDistSq = distSq;
                                        best = (projPos, interpNormal, Mathf.Sqrt(distSq));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Projects a point onto a triangle and returns the projected position and barycentric coordinates.
        /// </summary>
        private static (Vector3 position, float u, float v, float w)? ProjectPointOntoTriangle(
            Vector3 point,
            Vector3 v0,
            Vector3 v1,
            Vector3 v2)
        {
            Vector3 edge0 = v1 - v0;
            Vector3 edge1 = v2 - v0;
            Vector3 v0ToPoint = point - v0;

            float a = Vector3.Dot(edge0, edge0);
            float b = Vector3.Dot(edge0, edge1);
            float c = Vector3.Dot(edge1, edge1);
            float d = Vector3.Dot(edge0, v0ToPoint);
            float e = Vector3.Dot(edge1, v0ToPoint);

            float det = a * c - b * b;
            if (Mathf.Abs(det) < 0.0001f)
                return null; // Degenerate triangle

            float invDet = 1f / det;
            float u = (c * d - b * e) * invDet;
            float v = (a * e - b * d) * invDet;
            float w = 1f - u - v;

            // Check if point is inside triangle
            if (u >= 0f && v >= 0f && w >= 0f)
            {
                Vector3 projPos = v0 * w + v1 * u + v2 * v;
                return (projPos, u, v, w);
            }

            // Point is outside triangle - project to nearest edge or vertex
            // Clamp to triangle bounds
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);
            if (u + v > 1f)
            {
                float sum = u + v;
                u /= sum;
                v /= sum;
            }
            w = 1f - u - v;

            Vector3 clampedPos = v0 * w + v1 * u + v2 * v;
            return (clampedPos, u, v, w);
        }

        /// <summary>
        /// Extracts mesh from a renderer (handles both SkinnedMeshRenderer and MeshRenderer).
        /// </summary>
        private static Mesh GetMeshFromRenderer(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer smr)
            {
                return smr.sharedMesh;
            }
            else if (renderer is MeshRenderer mr)
            {
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                return mf?.sharedMesh;
            }

            return null;
        }
    }
}

