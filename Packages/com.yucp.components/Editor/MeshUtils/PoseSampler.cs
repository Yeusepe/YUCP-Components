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


            var originalWeights = new float[sharedMesh.blendShapeCount];
            for (int i = 0; i < sharedMesh.blendShapeCount; i++)
            {
                originalWeights[i] = renderer.GetBlendShapeWeight(i);
            }

            Mesh bakeMesh = new Mesh();
            bakeMesh.name = "SmartDetection_Temp";

            try
            {

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


                for (int i = 0; i < sharedMesh.blendShapeCount; i++)
                {
                    string blendshapeName = sharedMesh.GetBlendShapeName(i);

                    if (i > 0)
                        renderer.SetBlendShapeWeight(i - 1, 0f);
                    renderer.SetBlendShapeWeight(i, weight);

                    renderer.BakeMesh(bakeMesh);

                    SurfaceClusterDetector.EvaluateCluster(
                        cluster,
                        bakeMesh.vertices,
                        bakeMesh.triangles,
                        out Vector3 deformedPos,
                        out _,
                        out _);


                    Vector3 displacement = deformedPos - basePos;
                    results[blendshapeName] = displacement;
                }
            }
            finally
            {

                for (int i = 0; i < originalWeights.Length; i++)
                    renderer.SetBlendShapeWeight(i, originalWeights[i]);

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
