using System.Collections.Generic;
using UnityEngine;
using YUCP.Components;
using YUCP.Components.Editor.UI;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Detects which body vertices are hidden by clothing using various algorithms.
    /// Supports GPU acceleration for raycast-based methods with CPU fallback.
    /// </summary>
    public static class VertexDetection
    {
        public static bool[] DetectHiddenVertices(
            AutoBodyHiderData data,
            Vector3[] bodyVertices,
            Vector3[] bodyNormals,
            Vector2[] bodyUV0,
            YUCPProgressWindow progressWindow = null)
        {
            // Manual detection doesn't use clothing meshes
            if (data.detectionMethod == DetectionMethod.Manual)
            {
                return DetectByManualMask(data, bodyUV0, progressWindow);
            }
            
            // Get all valid clothing meshes
            var clothingMeshes = data.GetClothingMeshes();
            if (clothingMeshes == null || clothingMeshes.Length == 0)
            {
                Debug.LogWarning("[VertexDetection] No valid clothing meshes specified.");
                return new bool[bodyVertices.Length];
            }
            
            // For multiple meshes, combine results using union (if any mesh hides a vertex, it's hidden)
            bool[] combinedResult = new bool[bodyVertices.Length];
            
            for (int meshIndex = 0; meshIndex < clothingMeshes.Length; meshIndex++)
            {
                var clothingMesh = clothingMeshes[meshIndex];
                
                if (progressWindow != null && clothingMeshes.Length > 1)
                {
                    progressWindow.Progress(
                        (float)meshIndex / clothingMeshes.Length,
                        $"Processing clothing mesh {meshIndex + 1}/{clothingMeshes.Length}: {clothingMesh.name}..."
                    );
                }
                
                bool[] meshResult;
                switch (data.detectionMethod)
            {
                case DetectionMethod.Raycast:
                        meshResult = DetectByRaycast(data, clothingMesh, bodyVertices, bodyNormals, progressWindow);
                        break;
                case DetectionMethod.Proximity:
                        meshResult = DetectByProximity(data, clothingMesh, bodyVertices, progressWindow);
                        break;
                case DetectionMethod.Hybrid:
                        meshResult = DetectByHybrid(data, clothingMesh, bodyVertices, bodyNormals, progressWindow);
                        break;
                case DetectionMethod.Smart:
                        meshResult = DetectBySmart(data, clothingMesh, bodyVertices, bodyNormals, progressWindow);
                        break;
                    default:
                        meshResult = new bool[bodyVertices.Length];
                        break;
                }
                
                // Combine: if any mesh hides a vertex, mark it as hidden
                for (int i = 0; i < combinedResult.Length && i < meshResult.Length; i++)
                {
                    if (meshResult[i])
                    {
                        combinedResult[i] = true;
                    }
                }
            }
            
            return combinedResult;
        }

        private static bool[] DetectByRaycast(
            AutoBodyHiderData data,
            SkinnedMeshRenderer clothingMeshRenderer,
            Vector3[] bodyVertices,
            Vector3[] bodyNormals,
            YUCPProgressWindow progressWindow = null)
        {
            bool[] hidden = new bool[bodyVertices.Length];
            
            if (clothingMeshRenderer == null || clothingMeshRenderer.sharedMesh == null)
            {
                Debug.LogWarning("[VertexDetection] No clothing mesh specified for raycast detection.");
                return hidden;
            }

            if (GPURaycast.IsGPUAvailable())
            {
                if (progressWindow != null)
                {
                    progressWindow.Progress(0.1f, "Raycast detection: Using GPU acceleration...");
                }
                
                try
                {
                    hidden = GPURaycast.RaycastDetection(
                        bodyVertices,
                        bodyNormals,
                        data.targetBodyMesh.transform,
                        clothingMeshRenderer.sharedMesh,
                        clothingMeshRenderer.transform,
                        data.raycastDistance
                    );
                    
                    if (progressWindow != null)
                    {
                        progressWindow.Progress(0.9f, $"Raycast detection: GPU complete ({bodyVertices.Length} vertices)");
                    }
                    
                    return hidden;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[VertexDetection] GPU raycast failed, falling back to CPU: {e.Message}");
                }
            }

            if (progressWindow != null)
            {
                progressWindow.Progress(0.1f, "Raycast detection: Using CPU...");
            }
            
            Mesh clothingMesh = clothingMeshRenderer.sharedMesh;
            Transform clothingTransform = clothingMeshRenderer.transform;
            
            var clothingTriangles = GetWorldSpaceTriangles(clothingMesh, clothingTransform);

            for (int i = 0; i < bodyVertices.Length; i++)
            {
                Vector3 worldPos = data.targetBodyMesh.transform.TransformPoint(bodyVertices[i]);
                Vector3 worldNormal = data.targetBodyMesh.transform.TransformDirection(bodyNormals[i]).normalized;
                
                Ray ray = new Ray(worldPos, worldNormal);
                
                if (RayIntersectsClothing(ray, clothingTriangles, data.raycastDistance))
                {
                    hidden[i] = true;
                }
                
                if (progressWindow != null && i % 50 == 0)
                {
                    float progress = 0.1f + (0.8f * i / bodyVertices.Length);
                    progressWindow.Progress(progress, $"Raycast detection (CPU): {i}/{bodyVertices.Length} vertices");
                }
            }

            return hidden;
        }

        private static bool[] DetectByProximity(
            AutoBodyHiderData data,
            SkinnedMeshRenderer clothingMeshRenderer,
            Vector3[] bodyVertices,
            YUCPProgressWindow progressWindow = null)
        {
            bool[] hidden = new bool[bodyVertices.Length];
            
            if (clothingMeshRenderer == null || clothingMeshRenderer.sharedMesh == null)
            {
                Debug.LogWarning("[VertexDetection] No clothing mesh specified for proximity detection.");
                return hidden;
            }

            Mesh clothingMesh = clothingMeshRenderer.sharedMesh;
            Vector3[] clothingVertices = clothingMesh.vertices;
            Transform clothingTransform = clothingMeshRenderer.transform;
            
            Vector3[] clothingWorldVerts = new Vector3[clothingVertices.Length];
            for (int i = 0; i < clothingVertices.Length; i++)
            {
                clothingWorldVerts[i] = clothingTransform.TransformPoint(clothingVertices[i]);
            }

            for (int i = 0; i < bodyVertices.Length; i++)
            {
                Vector3 worldPos = data.targetBodyMesh.transform.TransformPoint(bodyVertices[i]);
                
                float minDistance = float.MaxValue;
                foreach (var clothingVert in clothingWorldVerts)
                {
                    float dist = Vector3.Distance(worldPos, clothingVert);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                    }
                }
                
                if (minDistance < data.proximityThreshold)
                {
                    hidden[i] = true;
                }
                
                if (progressWindow != null && i % 100 == 0)
                {
                    float progress = (float)i / bodyVertices.Length;
                    progressWindow.Progress(progress, $"Proximity detection: {i}/{bodyVertices.Length} vertices checked");
                }
            }

            return hidden;
        }

        private static bool[] DetectByHybrid(
            AutoBodyHiderData data,
            SkinnedMeshRenderer clothingMeshRenderer,
            Vector3[] bodyVertices,
            Vector3[] bodyNormals,
            YUCPProgressWindow progressWindow = null)
        {
            bool[] hidden = new bool[bodyVertices.Length];
            
            if (clothingMeshRenderer == null || clothingMeshRenderer.sharedMesh == null)
            {
                Debug.LogWarning("[VertexDetection] No clothing mesh specified for hybrid detection.");
                return hidden;
            }

            if (progressWindow != null)
            {
                progressWindow.Progress(0.1f, "Hybrid detection: Phase 1 - Proximity filtering...");
            }
            
            float expandedThreshold = data.proximityThreshold * data.hybridExpansionFactor;
            var tempData = new AutoBodyHiderData
            {
                targetBodyMesh = data.targetBodyMesh,
                proximityThreshold = expandedThreshold
            };
            
            bool[] proximityResults = DetectByProximity(tempData, clothingMeshRenderer, bodyVertices, null);
            
            int candidateCount = 0;
            for (int i = 0; i < proximityResults.Length; i++)
            {
                if (proximityResults[i]) candidateCount++;
            }
            
            if (progressWindow != null)
            {
                progressWindow.Progress(0.4f, $"Hybrid detection: Phase 2 - Raycasting {candidateCount} candidates...");
            }
            
            var clothingTriangles = GetWorldSpaceTriangles(clothingMeshRenderer.sharedMesh, clothingMeshRenderer.transform);
            
            int processed = 0;
            for (int i = 0; i < bodyVertices.Length; i++)
            {
                if (proximityResults[i])
                {
                    Vector3 worldPos = data.targetBodyMesh.transform.TransformPoint(bodyVertices[i]);
                    Vector3 worldNormal = data.targetBodyMesh.transform.TransformDirection(bodyNormals[i]).normalized;
                    
                    Ray ray = new Ray(worldPos, worldNormal);
                    
                    if (RayIntersectsClothing(ray, clothingTriangles, data.raycastDistance))
                    {
                        hidden[i] = true;
                    }
                    
                    processed++;
                    
                    if (progressWindow != null && processed % 50 == 0)
                    {
                        float progress = 0.4f + (0.5f * processed / candidateCount);
                        progressWindow.Progress(progress, $"Hybrid detection: {processed}/{candidateCount} candidates checked");
                    }
                }
            }

            return hidden;
        }

        private static bool[] DetectBySmart(
            AutoBodyHiderData data,
            SkinnedMeshRenderer clothingMeshRenderer,
            Vector3[] bodyVertices,
            Vector3[] bodyNormals,
            YUCPProgressWindow progressWindow = null)
        {
            bool[] hidden = new bool[bodyVertices.Length];
            
            if (clothingMeshRenderer == null || clothingMeshRenderer.sharedMesh == null)
            {
                Debug.LogWarning("[VertexDetection] No clothing mesh specified for smart detection.");
                return hidden;
            }

            if (GPURaycast.IsGPUAvailable())
            {
                if (progressWindow != null)
                {
                    progressWindow.Progress(0.1f, "Smart detection: Using GPU acceleration...");
                }
                
                try
                {
                    hidden = GPURaycast.SmartRaycastDetection(
                        bodyVertices,
                        bodyNormals,
                        data.targetBodyMesh.transform,
                        clothingMeshRenderer.sharedMesh,
                        clothingMeshRenderer.transform,
                        data.raycastDistance,
                        data.smartRayDirections,
                        data.smartUseNormals,
                        data.smartRequireBidirectional,
                        data.smartOcclusionThreshold,
                        data.smartRayOffset,
                        data.smartConservativeMode,
                        data.smartMinDistanceToClothing
                    );
                    
                    if (progressWindow != null)
                    {
                        string modeText = data.smartConservativeMode ? " (Conservative)" : "";
                        progressWindow.Progress(0.9f, $"Smart detection: GPU complete{modeText} ({bodyVertices.Length} vertices)");
                    }
                    
                    return hidden;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[VertexDetection] GPU smart raycast failed, falling back to CPU: {e.Message}");
                }
            }

            if (progressWindow != null)
            {
                progressWindow.Progress(0.1f, "Smart detection: Using CPU (this may take a while)...");
            }

            var clothingTriangles = GetWorldSpaceTriangles(clothingMeshRenderer.sharedMesh, clothingMeshRenderer.transform);
            
            Vector3[] testDirections = GenerateFibonacciSphere(data.smartRayDirections);
            
            for (int i = 0; i < bodyVertices.Length; i++)
            {
                Vector3 worldPos = data.targetBodyMesh.transform.TransformPoint(bodyVertices[i]);
                Vector3 worldNormal = data.targetBodyMesh.transform.TransformDirection(bodyNormals[i]).normalized;
                
                // Apply ray offset to prevent self-intersection
                Vector3 rayStart = worldPos + worldNormal * data.smartRayOffset;
                
                int occludedCount = 0;
                int validDirections = 0;
                float minDistFound = float.MaxValue;
                float totalWeight = 0f;
                float weightedOcclusion = 0f;
                
                // Test rays in multiple directions
                foreach (Vector3 direction in testDirections)
                {
                    // If using normals, only test directions in the hemisphere facing outward
                    if (data.smartUseNormals)
                    {
                        float alignment = Vector3.Dot(direction, worldNormal);
                        if (alignment < -0.1f) // Skip directions pointing inward
                            continue;
                    }
                    
                    validDirections++;
                    
                    // Cast ray in this direction from offset position
                    Ray ray = new Ray(rayStart, direction);
                    float hitDistance;
                    
                    if (RayIntersectsClothing(ray, clothingTriangles, data.raycastDistance, out hitDistance))
                    {
                        occludedCount++;
                        
                        // Track minimum distance
                        if (hitDistance < minDistFound)
                        {
                            minDistFound = hitDistance;
                        }
                        
                        // Conservative mode: weight by distance
                        if (data.smartConservativeMode)
                        {
                            float weight = 1f - (hitDistance / data.raycastDistance);
                            weight = Mathf.Max(0f, weight);
                            weightedOcclusion += weight;
                            totalWeight += 1f;
                        }
                        
                        // If bidirectional is required, also check from outside
                        if (data.smartRequireBidirectional)
                        {
                            // Cast ray from outside toward vertex
                            Vector3 outerPoint = worldPos + direction * data.raycastDistance;
                            Ray reverseRay = new Ray(outerPoint, -direction);
                            
                            // Check if reverse ray also hits clothing before reaching vertex
                            if (RayIntersectsClothingBeforePoint(reverseRay, clothingTriangles, worldPos, data.raycastDistance))
                            {
                                // Confirmed bidirectional occlusion
                                continue;
                            }
                            else
                            {
                                // Not truly enclosed, don't count this direction as occluded
                                occludedCount--;
                            }
                        }
                    }
                }
                
                // Check minimum distance requirement
                if (minDistFound > data.smartMinDistanceToClothing && data.smartMinDistanceToClothing > 0.0001f)
                {
                    // Too far from clothing, keep visible
                    continue;
                }
                
                // Calculate occlusion percentage
                if (validDirections > 0)
                {
                    float occlusionPercentage;
                    float effectiveThreshold = data.smartOcclusionThreshold;
                    
                    if (data.smartConservativeMode && totalWeight > 0)
                    {
                        // Use weighted occlusion in conservative mode
                        occlusionPercentage = weightedOcclusion / totalWeight;
                        // Increase threshold by 10% in conservative mode
                        effectiveThreshold = Mathf.Min(1f, data.smartOcclusionThreshold + 0.1f);
                    }
                    else
                    {
                        occlusionPercentage = (float)occludedCount / validDirections;
                    }
                    
                    // Hide if occlusion exceeds threshold
                    if (occlusionPercentage >= effectiveThreshold)
                    {
                        hidden[i] = true;
                    }
                }
                
                // Update progress for smart detection (most expensive operation)
                if (progressWindow != null && i % 25 == 0)
                {
                    float progress = 0.1f + (0.8f * i / bodyVertices.Length);
                    string modeText = data.smartConservativeMode ? " (Conservative)" : "";
                    progressWindow.Progress(progress, $"Smart detection{modeText} (CPU): {i}/{bodyVertices.Length} vertices");
                }
            }

            return hidden;
        }

        private static Vector3[] GenerateFibonacciSphere(int samples)
        {
            Vector3[] points = new Vector3[samples];
            float phi = Mathf.PI * (3f - Mathf.Sqrt(5f)); // Golden angle in radians
            
            for (int i = 0; i < samples; i++)
            {
                float y = 1f - (i / (float)(samples - 1)) * 2f; // y goes from 1 to -1
                float radius = Mathf.Sqrt(1f - y * y); // radius at y
                
                float theta = phi * i; // golden angle increment
                
                float x = Mathf.Cos(theta) * radius;
                float z = Mathf.Sin(theta) * radius;
                
                points[i] = new Vector3(x, y, z).normalized;
            }
            
            return points;
        }

        private static bool RayIntersectsClothingBeforePoint(
            Ray ray, 
            List<Triangle> triangles, 
            Vector3 targetPoint, 
            float maxDistance)
        {
            float targetDistance = Vector3.Distance(ray.origin, targetPoint);
            
            foreach (var triangle in triangles)
            {
                if (RayIntersectsTriangle(ray, triangle, out float distance))
                {
                    // Check if hit is between ray origin and target point
                    if (distance > 0 && distance < targetDistance && distance < maxDistance)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool[] DetectByManualMask(
            AutoBodyHiderData data,
            Vector2[] bodyUV0,
            YUCPProgressWindow progressWindow = null)
        {
            bool[] hidden = new bool[bodyUV0.Length];
            
            if (data.manualMask == null)
            {
                Debug.LogWarning("[VertexDetection] No manual mask texture specified.");
                return hidden;
            }

            if (progressWindow != null)
            {
                progressWindow.Progress(0.1f, "Manual mask detection: Reading texture...");
            }

            Texture2D mask = data.manualMask;
            
            for (int i = 0; i < bodyUV0.Length; i++)
            {
                Vector2 uv = bodyUV0[i];
                
                int x = Mathf.FloorToInt(uv.x * mask.width) % mask.width;
                int y = Mathf.FloorToInt(uv.y * mask.height) % mask.height;
                
                if (x < 0) x += mask.width;
                if (y < 0) y += mask.height;
                
                Color pixel = mask.GetPixel(x, y);
                float brightness = pixel.grayscale;
                
                if (brightness > data.manualMaskThreshold)
                {
                    hidden[i] = true;
                }
                
                if (progressWindow != null && i % 500 == 0)
                {
                    float progress = 0.1f + (0.8f * i / bodyUV0.Length);
                    progressWindow.Progress(progress, $"Manual mask detection: {i}/{bodyUV0.Length} vertices sampled");
                }
            }

            return hidden;
        }

        private static List<Triangle> GetWorldSpaceTriangles(Mesh mesh, Transform transform)
        {
            var triangles = new List<Triangle>();
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;
            
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 v0 = transform.TransformPoint(vertices[indices[i]]);
                Vector3 v1 = transform.TransformPoint(vertices[indices[i + 1]]);
                Vector3 v2 = transform.TransformPoint(vertices[indices[i + 2]]);
                
                triangles.Add(new Triangle(v0, v1, v2));
            }
            
            return triangles;
        }

        private static bool RayIntersectsClothing(Ray ray, List<Triangle> triangles, float maxDistance)
        {
            foreach (var triangle in triangles)
            {
                if (RayIntersectsTriangle(ray, triangle, out float distance))
                {
                    if (distance > 0 && distance < maxDistance)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        
        private static bool RayIntersectsClothing(Ray ray, List<Triangle> triangles, float maxDistance, out float hitDistance)
        {
            hitDistance = float.MaxValue;
            bool hit = false;
            
            foreach (var triangle in triangles)
            {
                if (RayIntersectsTriangle(ray, triangle, out float distance))
                {
                    if (distance > 0 && distance < maxDistance)
                    {
                        if (distance < hitDistance)
                        {
                            hitDistance = distance;
                        }
                        hit = true;
                    }
                }
            }
            return hit;
        }

        private static bool RayIntersectsTriangle(Ray ray, Triangle triangle, out float distance)
        {
            const float EPSILON = 0.0000001f;
            distance = 0;
            
            Vector3 edge1 = triangle.v1 - triangle.v0;
            Vector3 edge2 = triangle.v2 - triangle.v0;
            Vector3 h = Vector3.Cross(ray.direction, edge2);
            float a = Vector3.Dot(edge1, h);
            
            if (a > -EPSILON && a < EPSILON)
                return false;
            
            float f = 1.0f / a;
            Vector3 s = ray.origin - triangle.v0;
            float u = f * Vector3.Dot(s, h);
            
            if (u < 0.0f || u > 1.0f)
                return false;
            
            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(ray.direction, q);
            
            if (v < 0.0f || u + v > 1.0f)
                return false;
            
            float t = f * Vector3.Dot(edge2, q);
            
            if (t > EPSILON)
            {
                distance = t;
                return true;
            }
            
            return false;
        }

        private struct Triangle
        {
            public Vector3 v0, v1, v2;
            
            public Triangle(Vector3 v0, Vector3 v1, Vector3 v2)
            {
                this.v0 = v0;
                this.v1 = v1;
                this.v2 = v2;
            }
        }
    }
}

