using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// GPU-accelerated normal transfer using compute shaders.
    /// Falls back to CPU if GPU is not available or initialization fails.
    /// </summary>
    public static class NormalTransferGPU
    {
        private static ComputeShader transferShader;
        private static bool isInitialized = false;
        private static bool gpuAvailable = false;

        // Kernel indices
        private static int kernelProximity = -1;
        private static int kernelProjection = -1;

        // Struct for triangle data
        struct Triangle
        {
            public Vector3 v0;
            public Vector3 v1;
            public Vector3 v2;
            public Vector3 n0;
            public Vector3 n1;
            public Vector3 n2;

            public Triangle(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 n0, Vector3 n1, Vector3 n2)
            {
                this.v0 = v0;
                this.v1 = v1;
                this.v2 = v2;
                this.n0 = n0;
                this.n1 = n1;
                this.n2 = n2;
            }
        }

        /// <summary>
        /// Initialize the GPU normal transfer system. Call once at startup.
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized) return;

            try
            {
                // Check if compute shaders are supported
                if (!SystemInfo.supportsComputeShaders)
                {
                    Debug.LogWarning("[NormalTransferGPU] Compute shaders not supported on this platform. Using CPU fallback.");
                    gpuAvailable = false;
                    isInitialized = true;
                    return;
                }

                // Load the compute shader
                string shaderPath = "Packages/com.yucp.components/Editor/MeshUtils/NormalTransferShader.compute";
                transferShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(shaderPath);

                if (transferShader == null)
                {
                    Debug.LogWarning("[NormalTransferGPU] Could not load compute shader. Using CPU fallback.");
                    gpuAvailable = false;
                    isInitialized = true;
                    return;
                }

                // Find kernel indices
                kernelProximity = transferShader.FindKernel("CSProximityTransfer");
                kernelProjection = transferShader.FindKernel("CSProjectionTransfer");

                gpuAvailable = true;
                Debug.Log("[NormalTransferGPU] GPU normal transfer initialized successfully.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NormalTransferGPU] Failed to initialize GPU normal transfer: {e.Message}. Using CPU fallback.");
                gpuAvailable = false;
            }
            finally
            {
                isInitialized = true;
            }
        }

        /// <summary>
        /// Check if GPU normal transfer is available.
        /// </summary>
        public static bool IsGPUAvailable()
        {
            if (!isInitialized) Initialize();
            return gpuAvailable;
        }

        /// <summary>
        /// Perform proximity-based normal transfer on the GPU.
        /// </summary>
        public static Vector3[] ProximityTransfer(
            Mesh[] sourceMeshes,
            Transform[] sourceTransforms,
            Mesh targetMesh,
            Transform targetTransform,
            NormalBakeSettings settings)
        {
            if (!IsGPUAvailable())
            {
                throw new InvalidOperationException("GPU normal transfer not available. Check IsGPUAvailable() first.");
            }

            int targetVertexCount = targetMesh.vertices.Length;
            Vector3[] targetVertices = targetMesh.vertices;
            Vector3[] targetNormals = targetMesh.normals ?? new Vector3[targetVertexCount];

            // Convert target vertices to world space
            Vector3[] worldTargetVertices = new Vector3[targetVertexCount];
            for (int i = 0; i < targetVertexCount; i++)
            {
                worldTargetVertices[i] = targetTransform.TransformPoint(targetVertices[i]);
            }

            // Collect all source vertices and normals
            var sourceVertices = new List<Vector3>();
            var sourceNormals = new List<Vector3>();

            for (int m = 0; m < sourceMeshes.Length; m++)
            {
                if (sourceMeshes[m] == null || sourceTransforms[m] == null) continue;

                Vector3[] verts = sourceMeshes[m].vertices;
                Vector3[] norms = sourceMeshes[m].normals ?? new Vector3[verts.Length];
                Transform trans = sourceTransforms[m];

                for (int i = 0; i < verts.Length; i++)
                {
                    sourceVertices.Add(trans.TransformPoint(verts[i]));
                    sourceNormals.Add(trans.TransformDirection(norms[i]).normalized);
                }
            }

            if (sourceVertices.Count == 0)
            {
                return targetNormals;
            }

            // Create compute buffers
            ComputeBuffer targetVertexBuffer = new ComputeBuffer(targetVertexCount, sizeof(float) * 3);
            ComputeBuffer sourceVertexBuffer = new ComputeBuffer(sourceVertices.Count, sizeof(float) * 3);
            ComputeBuffer sourceNormalBuffer = new ComputeBuffer(sourceNormals.Count, sizeof(float) * 3);
            ComputeBuffer resultBuffer = new ComputeBuffer(targetVertexCount, sizeof(float) * 3);

            try
            {
                // Upload data to GPU
                targetVertexBuffer.SetData(worldTargetVertices);
                sourceVertexBuffer.SetData(sourceVertices.ToArray());
                sourceNormalBuffer.SetData(sourceNormals.ToArray());

                // Set shader parameters
                transferShader.SetBuffer(kernelProximity, "TargetVertices", targetVertexBuffer);
                transferShader.SetBuffer(kernelProximity, "SourceVertices", sourceVertexBuffer);
                transferShader.SetBuffer(kernelProximity, "SourceNormals", sourceNormalBuffer);
                transferShader.SetBuffer(kernelProximity, "ResultNormals", resultBuffer);
                transferShader.SetFloat("ProximityThreshold", settings.proximityThreshold);
                transferShader.SetFloat("BlendStrength", settings.proximityBlendStrength);
                transferShader.SetInt("SourceVertexCount", sourceVertices.Count);

                // Dispatch compute shader
                int threadGroups = Mathf.CeilToInt(targetVertexCount / 64.0f);
                transferShader.Dispatch(kernelProximity, threadGroups, 1, 1);

                // Read results back from GPU
                Vector3[] resultNormals = new Vector3[targetVertexCount];
                resultBuffer.GetData(resultNormals);

                // Convert back to local space
                Vector3[] localNormals = new Vector3[targetVertexCount];
                for (int i = 0; i < targetVertexCount; i++)
                {
                    localNormals[i] = targetTransform.InverseTransformDirection(resultNormals[i]);
                }

                return localNormals;
            }
            finally
            {
                // Clean up buffers
                targetVertexBuffer?.Release();
                sourceVertexBuffer?.Release();
                sourceNormalBuffer?.Release();
                resultBuffer?.Release();
            }
        }

        /// <summary>
        /// Perform projection-based normal transfer on the GPU.
        /// </summary>
        public static Vector3[] ProjectionTransfer(
            Mesh[] sourceMeshes,
            Transform[] sourceTransforms,
            Mesh targetMesh,
            Transform targetTransform,
            NormalBakeSettings settings)
        {
            if (!IsGPUAvailable())
            {
                throw new InvalidOperationException("GPU normal transfer not available. Check IsGPUAvailable() first.");
            }

            int targetVertexCount = targetMesh.vertices.Length;
            Vector3[] targetVertices = targetMesh.vertices;
            Vector3[] targetNormals = targetMesh.normals ?? new Vector3[targetVertexCount];

            // Convert target vertices and normals to world space
            Vector3[] worldTargetVertices = new Vector3[targetVertexCount];
            Vector3[] worldTargetNormals = new Vector3[targetVertexCount];
            for (int i = 0; i < targetVertexCount; i++)
            {
                worldTargetVertices[i] = targetTransform.TransformPoint(targetVertices[i]);
                worldTargetNormals[i] = targetTransform.TransformDirection(targetNormals[i]).normalized;
            }

            // Get source triangles in world space
            var triangles = new List<Triangle>();
            for (int m = 0; m < sourceMeshes.Length; m++)
            {
                if (sourceMeshes[m] == null || sourceTransforms[m] == null) continue;

                Triangle[] meshTriangles = GetWorldSpaceTriangles(sourceMeshes[m], sourceTransforms[m]);
                triangles.AddRange(meshTriangles);
            }

            if (triangles.Count == 0)
            {
                return targetNormals;
            }

            // Create compute buffers
            ComputeBuffer targetVertexBuffer = new ComputeBuffer(targetVertexCount, sizeof(float) * 3);
            ComputeBuffer targetNormalBuffer = new ComputeBuffer(targetVertexCount, sizeof(float) * 3);
            ComputeBuffer triangleBuffer = new ComputeBuffer(triangles.Count, sizeof(float) * 18); // 6 Vector3s per triangle
            ComputeBuffer resultBuffer = new ComputeBuffer(targetVertexCount, sizeof(float) * 3);

            try
            {
                // Upload data to GPU
                targetVertexBuffer.SetData(worldTargetVertices);
                targetNormalBuffer.SetData(worldTargetNormals);
                triangleBuffer.SetData(triangles.ToArray());

                // Set shader parameters
                transferShader.SetBuffer(kernelProjection, "TargetVertices", targetVertexBuffer);
                transferShader.SetBuffer(kernelProjection, "TargetNormals", targetNormalBuffer);
                transferShader.SetBuffer(kernelProjection, "SourceTriangles", triangleBuffer);
                transferShader.SetBuffer(kernelProjection, "ResultNormals", resultBuffer);
                transferShader.SetFloat("ProjectionDistance", settings.projectionDistance);
                transferShader.SetFloat("BlendStrength", settings.projectionBlendStrength);
                transferShader.SetInt("TriangleCount", triangles.Count);
                transferShader.SetInt("ProjectionDirection", (int)settings.projectionDirection);

                // Dispatch compute shader
                int threadGroups = Mathf.CeilToInt(targetVertexCount / 64.0f);
                transferShader.Dispatch(kernelProjection, threadGroups, 1, 1);

                // Read results back from GPU
                Vector3[] resultNormals = new Vector3[targetVertexCount];
                resultBuffer.GetData(resultNormals);

                // Convert back to local space
                Vector3[] localNormals = new Vector3[targetVertexCount];
                for (int i = 0; i < targetVertexCount; i++)
                {
                    localNormals[i] = targetTransform.InverseTransformDirection(resultNormals[i]);
                }

                return localNormals;
            }
            finally
            {
                // Clean up buffers
                targetVertexBuffer?.Release();
                targetNormalBuffer?.Release();
                triangleBuffer?.Release();
                resultBuffer?.Release();
            }
        }

        /// <summary>
        /// Convert mesh triangles to world space with normals.
        /// </summary>
        private static Triangle[] GetWorldSpaceTriangles(Mesh mesh, Transform transform)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals ?? new Vector3[vertices.Length];
            int[] triangles = mesh.triangles;
            int triangleCount = triangles.Length / 3;

            Triangle[] worldTriangles = new Triangle[triangleCount];

            for (int i = 0; i < triangleCount; i++)
            {
                int idx0 = triangles[i * 3];
                int idx1 = triangles[i * 3 + 1];
                int idx2 = triangles[i * 3 + 2];

                Vector3 v0 = transform.TransformPoint(vertices[idx0]);
                Vector3 v1 = transform.TransformPoint(vertices[idx1]);
                Vector3 v2 = transform.TransformPoint(vertices[idx2]);

                Vector3 n0 = transform.TransformDirection(normals[idx0]).normalized;
                Vector3 n1 = transform.TransformDirection(normals[idx1]).normalized;
                Vector3 n2 = transform.TransformDirection(normals[idx2]).normalized;

                worldTriangles[i] = new Triangle(v0, v1, v2, n0, n1, n2);
            }

            return worldTriangles;
        }
    }
}





