using UnityEngine;
using System.Collections.Generic;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Mesh sampling utilities for contact point generation.
    /// </summary>
    public static class MeshSampler
    {
        /// <summary>
        /// Sample uniform points on mesh surface using triangle area weighting.
        /// </summary>
        public static List<Vector3> SampleUniformPoints(Mesh mesh, int sampleCount)
        {
            var points = new List<Vector3>();
            if (mesh == null || sampleCount <= 0) return points;
            
            try
            {
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                
                if (triangles.Length < 3) return points;
                
                // Calculate triangle areas and build CDF
                var triangleAreas = new float[triangles.Length / 3];
                var cumulativeAreas = new float[triangles.Length / 3];
                float totalArea = 0f;
                
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 v0 = vertices[triangles[i]];
                    Vector3 v1 = vertices[triangles[i + 1]];
                    Vector3 v2 = vertices[triangles[i + 2]];
                    
                    float area = Vector3.Cross(v1 - v0, v2 - v0).magnitude * 0.5f;
                    triangleAreas[i / 3] = area;
                    totalArea += area;
                }
                
                if (totalArea <= 0f) return points;
                
                // Build cumulative distribution
                float cumulative = 0f;
                for (int i = 0; i < triangleAreas.Length; i++)
                {
                    cumulative += triangleAreas[i];
                    cumulativeAreas[i] = cumulative / totalArea;
                }
                
                // Sample points
                for (int i = 0; i < sampleCount; i++)
                {
                    float random = Random.Range(0f, 1f);
                    
                    // Find triangle using binary search
                    int triIndex = BinarySearch(cumulativeAreas, random) * 3;
                    
                    if (triIndex < triangles.Length)
                    {
                        Vector3 v0 = vertices[triangles[triIndex]];
                        Vector3 v1 = vertices[triangles[triIndex + 1]];
                        Vector3 v2 = vertices[triangles[triIndex + 2]];
                        
                        // Barycentric sampling
                        float u = Random.Range(0f, 1f);
                        float v = Random.Range(0f, 1f);
                        
                        if (u + v > 1f)
                        {
                            u = 1f - u;
                            v = 1f - v;
                        }
                        
                        float w = 1f - u - v;
                        Vector3 point = u * v0 + v * v1 + w * v2;
                        points.Add(point);
                    }
                }
            }
            catch (System.Exception)
            {
                // Fallback: return empty list
            }
            
            return points;
        }
        
        /// <summary>
        /// Find closest point on triangle using barycentric coordinates.
        /// </summary>
        public static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 v0, Vector3 v1, Vector3 v2)
        {
            Vector3 edge0 = v1 - v0;
            Vector3 edge1 = v2 - v0;
            Vector3 v0ToPoint = point - v0;
            
            float dot00 = Vector3.Dot(edge0, edge0);
            float dot01 = Vector3.Dot(edge0, edge1);
            float dot02 = Vector3.Dot(edge0, v0ToPoint);
            float dot11 = Vector3.Dot(edge1, edge1);
            float dot12 = Vector3.Dot(edge1, v0ToPoint);
            
            float invDenom = 1f / (dot00 * dot11 - dot01 * dot01);
            float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            float v = (dot00 * dot12 - dot01 * dot02) * invDenom;
            
            // Clamp to triangle
            if (u < 0f) u = 0f;
            if (v < 0f) v = 0f;
            if (u + v > 1f)
            {
                float temp = u;
                u = 1f - v;
                v = 1f - temp;
            }
            
            float w = 1f - u - v;
            return u * v0 + v * v1 + w * v2;
        }
        
        /// <summary>
        /// Get triangle normal from three vertices.
        /// </summary>
        public static Vector3 GetTriangleNormal(Vector3 v0, Vector3 v1, Vector3 v2)
        {
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            return Vector3.Cross(edge1, edge2).normalized;
        }
        
        /// <summary>
        /// Binary search for cumulative distribution.
        /// </summary>
        private static int BinarySearch(float[] array, float value)
        {
            int left = 0;
            int right = array.Length - 1;
            
            while (left < right)
            {
                int mid = (left + right) / 2;
                if (array[mid] < value)
                {
                    left = mid + 1;
                }
                else
                {
                    right = mid;
                }
            }
            
            return Mathf.Min(left, array.Length - 1);
        }
        
        /// <summary>
        /// Sample points around a center point in a ring pattern.
        /// </summary>
        public static List<Vector3> SampleRingPoints(Vector3 center, Vector3 normal, float radius, int count)
        {
            var points = new List<Vector3>();
            
            if (count <= 0) return points;
            
            // Create orthogonal vectors
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(up, normal)) > 0.95f)
            {
                up = Vector3.right;
            }
            
            Vector3 tangent1 = Vector3.Cross(up, normal).normalized;
            Vector3 tangent2 = Vector3.Cross(normal, tangent1).normalized;
            
            for (int i = 0; i < count; i++)
            {
                float angle = (float)i / count * 2f * Mathf.PI;
                Vector3 offset = tangent1 * Mathf.Cos(angle) + tangent2 * Mathf.Sin(angle);
                points.Add(center + offset * radius);
            }
            
            return points;
        }
    }
}











