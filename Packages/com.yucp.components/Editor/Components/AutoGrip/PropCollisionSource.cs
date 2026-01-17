using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.AutoGrip
{
    /// <summary>
    /// Prepared collision data for a prop object.
    /// </summary>
    public class PropCollisionData
    {
        public Collider[] colliders;
        public Bounds bounds;
        public bool usesTemporaryColliders;
        public List<GameObject> temporaryObjects = new List<GameObject>();
        public Mesh sourceMesh;
        public Vector3[] meshVertices;
        public Vector3[] meshNormals;

        public void Cleanup()
        {
            foreach (var obj in temporaryObjects)
            {
                if (obj != null)
                {
                    Object.DestroyImmediate(obj);
                }
            }
            temporaryObjects.Clear();
        }
    }

    /// <summary>
    /// Prepares prop colliders for collision checks during grip synthesis.
    /// Handles existing colliders or generates temporary ones from meshes.
    /// </summary>
    public static class PropCollisionSource
    {
        private const int MaxCollisionChecks = 1000;

        /// <summary>
        /// Prepares collision data for a prop object.
        /// </summary>
        /// <param name="propTransform">Transform of the prop object</param>
        /// <param name="collisionMask">Layer mask for collider selection</param>
        /// <returns>Prepared collision data, or null on failure</returns>
        public static PropCollisionData PrepareCollisionData(Transform propTransform, LayerMask collisionMask)
        {
            if (propTransform == null)
            {
                Debug.LogError("[PropCollisionSource] Prop transform is null");
                return null;
            }

            var data = new PropCollisionData();

            // Try to find existing colliders first
            var existingColliders = propTransform.GetComponentsInChildren<Collider>();
            var validColliders = new List<Collider>();

            foreach (var col in existingColliders)
            {
                if (((1 << col.gameObject.layer) & collisionMask) != 0 && col.enabled)
                {
                    validColliders.Add(col);
                }
            }

            if (validColliders.Count > 0)
            {
                data.colliders = validColliders.ToArray();
                data.usesTemporaryColliders = false;
                data.bounds = CalculateCombinedBounds(data.colliders);
                Debug.Log($"[PropCollisionSource] Using {data.colliders.Length} existing colliders");
                return data;
            }

            // No existing colliders - generate from mesh
            Debug.Log("[PropCollisionSource] No colliders found, generating from mesh");
            return GenerateCollidersFromMesh(propTransform, data);
        }

        private static PropCollisionData GenerateCollidersFromMesh(Transform propTransform, PropCollisionData data)
        {
            // Try SkinnedMeshRenderer first
            var skinnedRenderer = propTransform.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinnedRenderer != null && skinnedRenderer.sharedMesh != null)
            {
                return GenerateFromSkinnedMesh(skinnedRenderer, data);
            }

            // Try MeshFilter
            var meshFilter = propTransform.GetComponentInChildren<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                return GenerateFromMeshFilter(meshFilter, data);
            }

            Debug.LogWarning("[PropCollisionSource] No mesh found on prop for collision generation");
            return null;
        }

        private static PropCollisionData GenerateFromSkinnedMesh(SkinnedMeshRenderer renderer, PropCollisionData data)
        {
            // Bake current pose to mesh
            var bakedMesh = new Mesh();
            renderer.BakeMesh(bakedMesh);

            data.sourceMesh = bakedMesh;
            data.meshVertices = bakedMesh.vertices;
            data.meshNormals = bakedMesh.normals;

            // Create temporary collider
            var tempObj = CreateTemporaryCollider(bakedMesh, renderer.transform);
            data.temporaryObjects.Add(tempObj);
            data.colliders = new[] { tempObj.GetComponent<Collider>() };
            data.usesTemporaryColliders = true;
            data.bounds = CalculateCombinedBounds(data.colliders);

            return data;
        }

        private static PropCollisionData GenerateFromMeshFilter(MeshFilter meshFilter, PropCollisionData data)
        {
            var mesh = meshFilter.sharedMesh;
            data.sourceMesh = mesh;
            data.meshVertices = mesh.vertices;
            data.meshNormals = mesh.normals;

            // Create temporary collider
            var tempObj = CreateTemporaryCollider(mesh, meshFilter.transform);
            data.temporaryObjects.Add(tempObj);
            data.colliders = new[] { tempObj.GetComponent<Collider>() };
            data.usesTemporaryColliders = true;
            data.bounds = CalculateCombinedBounds(data.colliders);

            return data;
        }

        private static GameObject CreateTemporaryCollider(Mesh mesh, Transform sourceTransform)
        {
            var tempObj = new GameObject("TempPropCollider");
            tempObj.hideFlags = HideFlags.HideAndDontSave;
            tempObj.transform.position = sourceTransform.position;
            tempObj.transform.rotation = sourceTransform.rotation;
            tempObj.transform.localScale = sourceTransform.lossyScale;

            // Prefer convex mesh collider for fast collision checks
            var meshCollider = tempObj.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
            meshCollider.convex = true; // Required for collision queries

            return tempObj;
        }

        private static Bounds CalculateCombinedBounds(Collider[] colliders)
        {
            if (colliders == null || colliders.Length == 0)
            {
                return new Bounds();
            }

            var bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
            {
                bounds.Encapsulate(colliders[i].bounds);
            }
            return bounds;
        }

        /// <summary>
        /// Finds the closest point on prop colliders to a given position.
        /// </summary>
        public static Vector3 GetClosestPoint(PropCollisionData data, Vector3 position)
        {
            if (data == null || data.colliders == null || data.colliders.Length == 0)
            {
                return position;
            }

            float minDist = float.MaxValue;
            Vector3 closestPoint = position;

            foreach (var collider in data.colliders)
            {
                Vector3 point = collider.ClosestPoint(position);
                float dist = Vector3.Distance(position, point);
                if (dist < minDist)
                {
                    minDist = dist;
                    closestPoint = point;
                }
            }

            return closestPoint;
        }

        /// <summary>
        /// Checks if a point is inside any of the prop colliders.
        /// </summary>
        public static bool IsPointInside(PropCollisionData data, Vector3 point)
        {
            if (data == null || data.colliders == null) return false;

            foreach (var collider in data.colliders)
            {
                Vector3 closest = collider.ClosestPoint(point);
                if (Vector3.Distance(closest, point) < 0.0001f)
                {
                    // Point is on or inside the collider
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Computes penetration between a capsule and the prop colliders.
        /// Returns true if penetration exists.
        /// </summary>
        public static bool ComputePenetration(
            PropCollisionData data,
            Vector3 capsuleStart, 
            Vector3 capsuleEnd, 
            float capsuleRadius,
            out Vector3 direction,
            out float distance)
        {
            direction = Vector3.up;
            distance = 0f;

            if (data == null || data.colliders == null || data.colliders.Length == 0)
            {
                return false;
            }

            // Create temporary capsule collider
            var tempObj = new GameObject("TempCapsuleQuery");
            tempObj.hideFlags = HideFlags.HideAndDontSave;
            var capsule = tempObj.AddComponent<CapsuleCollider>();

            try
            {
                Vector3 center = (capsuleStart + capsuleEnd) * 0.5f;
                float height = Vector3.Distance(capsuleStart, capsuleEnd) + capsuleRadius * 2f;
                Vector3 dir = (capsuleEnd - capsuleStart).normalized;

                capsule.center = Vector3.zero;
                capsule.height = height;
                capsule.radius = capsuleRadius;
                capsule.direction = 2; // Z-axis

                tempObj.transform.position = center;
                tempObj.transform.rotation = dir != Vector3.zero ? Quaternion.LookRotation(dir) : Quaternion.identity;

                float maxDistance = 0f;
                Vector3 maxDirection = Vector3.up;
                bool anyPenetration = false;

                foreach (var collider in data.colliders)
                {
                    if (Physics.ComputePenetration(
                        capsule, tempObj.transform.position, tempObj.transform.rotation,
                        collider, collider.transform.position, collider.transform.rotation,
                        out Vector3 d, out float dist))
                    {
                        if (dist > maxDistance)
                        {
                            maxDistance = dist;
                            maxDirection = d;
                            anyPenetration = true;
                        }
                    }
                }

                direction = maxDirection;
                distance = maxDistance;
                return anyPenetration;
            }
            finally
            {
                Object.DestroyImmediate(tempObj);
            }
        }

        /// <summary>
        /// Samples contact points on the prop surface near a position.
        /// </summary>
        public static List<(Vector3 position, Vector3 normal)> SampleContactPoints(
            PropCollisionData data, 
            Vector3 center, 
            float radius, 
            int maxSamples = 32)
        {
            var contacts = new List<(Vector3, Vector3)>();

            if (data == null || data.meshVertices == null || data.meshNormals == null)
            {
                return contacts;
            }

            float radiusSq = radius * radius;

            for (int i = 0; i < data.meshVertices.Length && contacts.Count < maxSamples; i++)
            {
                Vector3 worldPos = data.colliders[0].transform.TransformPoint(data.meshVertices[i]);
                if ((worldPos - center).sqrMagnitude <= radiusSq)
                {
                    Vector3 worldNormal = data.colliders[0].transform.TransformDirection(data.meshNormals[i]).normalized;
                    contacts.Add((worldPos, worldNormal));
                }
            }

            return contacts;
        }
    }
}
