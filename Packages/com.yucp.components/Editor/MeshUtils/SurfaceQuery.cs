using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Editor-only surface query utilities for contact point computation.
    /// </summary>
    public static class SurfaceQuery
    {
        /// <summary>
        /// Try to get closest point on collider surface.
        /// </summary>
        public static bool TryClosestPoint(Collider collider, Vector3 worldPoint, out Vector3 closestPoint)
        {
            closestPoint = worldPoint;
            
            if (collider == null) return false;
            
            try
            {
                closestPoint = collider.ClosestPoint(worldPoint);
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }
        
        /// <summary>
        /// Try to get surface normal via raycast from point toward collider.
        /// </summary>
        public static bool TryRaycastNormal(Collider collider, Vector3 fromPoint, Vector3 direction, out Vector3 normal)
        {
            normal = Vector3.up;
            
            if (collider == null) return false;
            
            try
            {
                Ray ray = new Ray(fromPoint, direction.normalized);
                RaycastHit hit;
                
                if (collider.Raycast(ray, out hit, direction.magnitude + 0.1f))
                {
                    normal = hit.normal;
                    return true;
                }
            }
            catch (System.Exception)
            {
                // Fallback to bounds-based normal
            }
            
            // Fallback: compute normal from bounds center
            var bounds = collider.bounds;
            normal = (fromPoint - bounds.center).normalized;
            return false;
        }
        
        /// <summary>
        /// Get combined bounds from all renderers.
        /// </summary>
        public static Bounds GetBounds(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one);
            }
            
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            
            return bounds;
        }
        
        /// <summary>
        /// Try to compute normal from mesh at given point (fallback method).
        /// </summary>
        public static bool TryComputeNormalFromMesh(Mesh mesh, Vector3 point, out Vector3 normal)
        {
            normal = Vector3.up;
            
            if (mesh == null) return false;
            
            try
            {
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                var normals = mesh.normals;
                
                float minDist = float.MaxValue;
                int closestTri = -1;
                
                // Find closest triangle
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 v0 = vertices[triangles[i]];
                    Vector3 v1 = vertices[triangles[i + 1]];
                    Vector3 v2 = vertices[triangles[i + 2]];
                    
                    Vector3 triCenter = (v0 + v1 + v2) / 3f;
                    float dist = Vector3.SqrMagnitude(point - triCenter);
                    
                    if (dist < minDist)
                    {
                        minDist = dist;
                        closestTri = i / 3;
                    }
                }
                
                if (closestTri >= 0)
                {
                    // Use triangle normal
                    Vector3 v0 = vertices[triangles[closestTri * 3]];
                    Vector3 v1 = vertices[triangles[closestTri * 3 + 1]];
                    Vector3 v2 = vertices[triangles[closestTri * 3 + 2]];
                    
                    Vector3 edge1 = v1 - v0;
                    Vector3 edge2 = v2 - v0;
                    normal = Vector3.Cross(edge1, edge2).normalized;
                    return true;
                }
            }
            catch (System.Exception)
            {
                // Fallback
            }
            
            return false;
        }
        
        /// <summary>
        /// Bake skinned mesh renderer to temporary mesh (editor-only).
        /// </summary>
        public static Mesh BakeSkinnedMesh(SkinnedMeshRenderer skinnedRenderer)
        {
            if (skinnedRenderer == null) return null;
            
            try
            {
                var mesh = new Mesh();
                skinnedRenderer.BakeMesh(mesh);
                return mesh;
            }
            catch (System.Exception)
            {
                return null;
            }
        }
        
        /// <summary>
        /// Get all colliders from object and children.
        /// </summary>
        public static Collider[] GetAllColliders(Transform obj)
        {
            if (obj == null) return new Collider[0];
            
            return obj.GetComponentsInChildren<Collider>();
        }
        
        /// <summary>
        /// Get all renderers from object and children.
        /// </summary>
        public static Renderer[] GetAllRenderers(Transform obj)
        {
            if (obj == null) return new Renderer[0];
            
            return obj.GetComponentsInChildren<Renderer>();
        }
    }
}











