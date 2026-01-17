using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Helper class for building mesh adjacency structures and computing shared normal fields.
    /// Used by the SharedField normal transfer method.
    /// </summary>
    public static class MeshAdjacency
    {
        /// <summary>
        /// Groups vertices by position and computes shared normals using angle-weighted averaging.
        /// </summary>
        public static Dictionary<int, Vector3> ComputeSharedNormals(
            Mesh[] meshes,
            Transform[] transforms,
            float positionThreshold,
            float hardEdgeAngle)
        {
            // Build position hash map: Position → List<(meshIndex, vertexIndex)>
            var positionGroups = new Dictionary<Vector3Int, List<(int meshIndex, int vertexIndex)>>();
            
            // Collect all vertices with their positions
            for (int meshIdx = 0; meshIdx < meshes.Length; meshIdx++)
            {
                if (meshes[meshIdx] == null || transforms[meshIdx] == null)
                    continue;
                    
                Mesh mesh = meshes[meshIdx];
                Transform transform = transforms[meshIdx];
                Vector3[] vertices = mesh.vertices;
                
                for (int vIdx = 0; vIdx < vertices.Length; vIdx++)
                {
                    Vector3 worldPos = transform.TransformPoint(vertices[vIdx]);
                    Vector3Int quantizedPos = QuantizePosition(worldPos, positionThreshold);
                    
                    if (!positionGroups.ContainsKey(quantizedPos))
                    {
                        positionGroups[quantizedPos] = new List<(int, int)>();
                    }
                    
                    positionGroups[quantizedPos].Add((meshIdx, vIdx));
                }
            }
            
            // For each position group, compute shared normal
            var sharedNormals = new Dictionary<int, Vector3>();
            
            foreach (var group in positionGroups.Values)
            {
                if (group.Count == 0) continue;
                
                // Collect all adjacent face normals for vertices in this group
                List<Vector3> faceNormals = new List<Vector3>();
                List<float> cornerAngles = new List<float>();
                
                foreach (var (meshIdx, vertexIdx) in group)
                {
                    Mesh mesh = meshes[meshIdx];
                    Transform transform = transforms[meshIdx];
                    
                    // Get triangles containing this vertex
                    int[] triangles = mesh.triangles;
                    Vector3[] vertices = mesh.vertices;
                    Vector3[] normals = mesh.normals;
                    
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        int v0 = triangles[t];
                        int v1 = triangles[t + 1];
                        int v2 = triangles[t + 2];
                        
                        if (v0 == vertexIdx || v1 == vertexIdx || v2 == vertexIdx)
                        {
                            // This triangle contains our vertex
                            Vector3 worldV0 = transform.TransformPoint(vertices[v0]);
                            Vector3 worldV1 = transform.TransformPoint(vertices[v1]);
                            Vector3 worldV2 = transform.TransformPoint(vertices[v2]);
                            
                            // Compute face normal
                            Vector3 faceNormal = Vector3.Cross(worldV1 - worldV0, worldV2 - worldV0).normalized;
                            
                            // Compute corner angle at this vertex
                            float angle = ComputeCornerAngle(worldV0, worldV1, worldV2, vertexIdx);
                            
                            faceNormals.Add(faceNormal);
                            cornerAngles.Add(angle);
                        }
                    }
                }
                
                // Compute angle-weighted average normal
                Vector3 sharedNormal = ComputeAngleWeightedNormal(faceNormals, cornerAngles, hardEdgeAngle);
                
                // Assign to all vertices in group
                foreach (var (meshIdx, vertexIdx) in group)
                {
                    int key = GetVertexKey(meshIdx, vertexIdx);
                    sharedNormals[key] = sharedNormal;
                }
            }
            
            return sharedNormals;
        }
        
        /// <summary>
        /// Quantizes a position to a grid cell for grouping nearby vertices.
        /// </summary>
        private static Vector3Int QuantizePosition(Vector3 position, float threshold)
        {
            int x = Mathf.FloorToInt(position.x / threshold);
            int y = Mathf.FloorToInt(position.y / threshold);
            int z = Mathf.FloorToInt(position.z / threshold);
            return new Vector3Int(x, y, z);
        }
        
        /// <summary>
        /// Computes the corner angle at a vertex in a triangle.
        /// </summary>
        private static float ComputeCornerAngle(Vector3 v0, Vector3 v1, Vector3 v2, int vertexIdx)
        {
            Vector3 a, b, c;
            
            if (vertexIdx == 0)
            {
                a = v0;
                b = v1;
                c = v2;
            }
            else if (vertexIdx == 1)
            {
                a = v1;
                b = v0;
                c = v2;
            }
            else
            {
                a = v2;
                b = v0;
                c = v1;
            }
            
            Vector3 ab = (b - a).normalized;
            Vector3 ac = (c - a).normalized;
            
            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(ab, ac), -1f, 1f));
        }
        
        /// <summary>
        /// Computes angle-weighted average normal, respecting hard edges.
        /// </summary>
        private static Vector3 ComputeAngleWeightedNormal(List<Vector3> faceNormals, List<float> cornerAngles, float hardEdgeAngle)
        {
            if (faceNormals.Count == 0)
                return Vector3.up;
            
            if (faceNormals.Count == 1)
                return faceNormals[0];
            
            // Group normals by similarity (smoothing groups)
            var groups = new List<List<int>>();
            var used = new bool[faceNormals.Count];
            
            for (int i = 0; i < faceNormals.Count; i++)
            {
                if (used[i]) continue;
                
                var group = new List<int> { i };
                used[i] = true;
                
                // Find all normals within hard edge angle
                for (int j = i + 1; j < faceNormals.Count; j++)
                {
                    if (used[j]) continue;
                    
                    float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(faceNormals[i], faceNormals[j]), -1f, 1f)) * Mathf.Rad2Deg;
                    
                    if (angle <= hardEdgeAngle)
                    {
                        group.Add(j);
                        used[j] = true;
                    }
                }
                
                groups.Add(group);
            }
            
            // Compute weighted average for each group, then average groups
            Vector3 result = Vector3.zero;
            float totalWeight = 0f;
            
            foreach (var group in groups)
            {
                Vector3 groupNormal = Vector3.zero;
                float groupWeight = 0f;
                
                foreach (int idx in group)
                {
                    float weight = cornerAngles[idx];
                    groupNormal += faceNormals[idx] * weight;
                    groupWeight += weight;
                }
                
                if (groupWeight > 0f)
                {
                    groupNormal /= groupWeight;
                    result += groupNormal.normalized * group.Count;
                    totalWeight += group.Count;
                }
            }
            
            if (totalWeight > 0f)
            {
                result /= totalWeight;
            }
            
            return result.normalized;
        }
        
        /// <summary>
        /// Generates a unique key for a vertex across all meshes.
        /// </summary>
        private static int GetVertexKey(int meshIndex, int vertexIndex)
        {
            // Use a simple hash combining mesh index and vertex index
            // Assuming max 1000 meshes and 1M vertices per mesh
            return meshIndex * 1000000 + vertexIndex;
        }
        
        /// <summary>
        /// Extracts mesh index and vertex index from a vertex key.
        /// </summary>
        public static void DecodeVertexKey(int key, out int meshIndex, out int vertexIndex)
        {
            meshIndex = key / 1000000;
            vertexIndex = key % 1000000;
        }
    }
}





