using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Samples mesh deformations at different blendshape weights using SkinnedMeshRenderer.BakeMesh.
    /// Provides an efficient way to evaluate mesh states without modifying the original renderer.
    /// </summary>
    public static class PoseSampler
    {
        public class PoseSample
        {
            public float blendshapeWeight;
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector3 clusterPosition;
            public Vector3 clusterNormal;
            public Vector3 clusterTangent;
        }

        /// <summary>
        /// Sample a blendshape at multiple weight values.
        /// </summary>
        public static List<PoseSample> SampleBlendshape(
            SkinnedMeshRenderer renderer,
            string blendshapeName,
            SurfaceCluster cluster,
            int sampleCount = 5)
        {
            if (renderer == null || renderer.sharedMesh == null)
            {
                Debug.LogError("[PoseSampler] Invalid renderer or mesh");
                return null;
            }

            Mesh sharedMesh = renderer.sharedMesh;
            int blendshapeIndex = sharedMesh.GetBlendShapeIndex(blendshapeName);

            if (blendshapeIndex < 0)
            {
                Debug.LogError($"[PoseSampler] Blendshape '{blendshapeName}' not found on mesh");
                return null;
            }

            List<PoseSample> samples = new List<PoseSample>();

            // Store original blendshape weights
            Dictionary<int, float> originalWeights = new Dictionary<int, float>();
            for (int i = 0; i < sharedMesh.blendShapeCount; i++)
            {
                originalWeights[i] = renderer.GetBlendShapeWeight(i);
            }

            // Create temporary mesh for baking
            Mesh bakeMesh = new Mesh();
            bakeMesh.name = "PoseSample_Temp";

            try
            {
                // Sample at different weights
                for (int i = 0; i < sampleCount; i++)
                {
                    float weight = i / (float)(sampleCount - 1) * 100f; // 0 to 100

                    // Set only this blendshape, zero others
                    for (int b = 0; b < sharedMesh.blendShapeCount; b++)
                    {
                        renderer.SetBlendShapeWeight(b, b == blendshapeIndex ? weight : 0f);
                    }

                    // Bake the mesh at this pose
                    renderer.BakeMesh(bakeMesh);

                    // Extract data
                    Vector3[] vertices = bakeMesh.vertices;
                    Vector3[] normals = bakeMesh.normals;
                    int[] triangles = bakeMesh.triangles;

                    // Evaluate cluster at this pose
                    SurfaceClusterDetector.EvaluateCluster(
                        cluster,
                        vertices,
                        triangles,
                        out Vector3 clusterPos,
                        out Vector3 clusterNorm,
                        out Vector3 clusterTan);

                    PoseSample sample = new PoseSample
                    {
                        blendshapeWeight = weight,
                        vertices = vertices,
                        normals = normals,
                        clusterPosition = clusterPos,
                        clusterNormal = clusterNorm,
                        clusterTangent = clusterTan
                    };

                    samples.Add(sample);
                }
            }
            finally
            {
                // Restore original weights
                foreach (var kvp in originalWeights)
                {
                    renderer.SetBlendShapeWeight(kvp.Key, kvp.Value);
                }

                // Cleanup temp mesh
                Object.DestroyImmediate(bakeMesh);
            }

            return samples;
        }

        /// <summary>
        /// Sample all blendshapes at a single weight value (for smart detection).
        /// </summary>
        public static Dictionary<string, Vector3> SampleAllBlendshapesAtWeight(
            SkinnedMeshRenderer renderer,
            SurfaceCluster cluster,
            float weight = 100f)
        {
            if (renderer == null || renderer.sharedMesh == null)
            {
                return new Dictionary<string, Vector3>();
            }

            Mesh sharedMesh = renderer.sharedMesh;
            Dictionary<string, Vector3> results = new Dictionary<string, Vector3>();

            // Store original weights
            Dictionary<int, float> originalWeights = new Dictionary<int, float>();
            for (int i = 0; i < sharedMesh.blendShapeCount; i++)
            {
                originalWeights[i] = renderer.GetBlendShapeWeight(i);
            }

            Mesh bakeMesh = new Mesh();
            bakeMesh.name = "SmartDetection_Temp";

            try
            {
                // Get base position (all blendshapes at 0)
                for (int b = 0; b < sharedMesh.blendShapeCount; b++)
                {
                    renderer.SetBlendShapeWeight(b, 0f);
                }
                renderer.BakeMesh(bakeMesh);
                
                SurfaceClusterDetector.EvaluateCluster(
                    cluster,
                    bakeMesh.vertices,
                    bakeMesh.triangles,
                    out Vector3 basePos,
                    out _,
                    out _);

                // Test each blendshape individually
                for (int i = 0; i < sharedMesh.blendShapeCount; i++)
                {
                    string blendshapeName = sharedMesh.GetBlendShapeName(i);

                    // Set this blendshape to weight, others to 0
                    for (int b = 0; b < sharedMesh.blendShapeCount; b++)
                    {
                        renderer.SetBlendShapeWeight(b, b == i ? weight : 0f);
                    }

                    renderer.BakeMesh(bakeMesh);

                    SurfaceClusterDetector.EvaluateCluster(
                        cluster,
                        bakeMesh.vertices,
                        bakeMesh.triangles,
                        out Vector3 deformedPos,
                        out _,
                        out _);

                    // Calculate displacement
                    Vector3 displacement = deformedPos - basePos;
                    results[blendshapeName] = displacement;
                }
            }
            finally
            {
                // Restore original weights
                foreach (var kvp in originalWeights)
                {
                    renderer.SetBlendShapeWeight(kvp.Key, kvp.Value);
                }

                Object.DestroyImmediate(bakeMesh);
            }

            return results;
        }

        /// <summary>
        /// Get list of all blendshape names from a mesh.
        /// </summary>
        public static List<string> GetAllBlendshapeNames(Mesh mesh)
        {
            List<string> names = new List<string>();
            
            if (mesh == null) return names;

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                names.Add(mesh.GetBlendShapeName(i));
            }

            return names;
        }

        /// <summary>
        /// Check if a mesh has blendshapes.
        /// </summary>
        public static bool HasBlendshapes(SkinnedMeshRenderer renderer)
        {
            return renderer != null && 
                   renderer.sharedMesh != null && 
                   renderer.sharedMesh.blendShapeCount > 0;
        }
    }
}





