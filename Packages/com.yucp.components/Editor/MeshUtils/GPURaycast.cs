using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// GPU-accelerated raycasting for vertex occlusion detection using compute shaders.
    /// Falls back to CPU if GPU is not available or initialization fails.
    /// </summary>
    public static class GPURaycast
    {
        private static ComputeShader raycastShader;
        private static bool isInitialized = false;
        private static bool gpuAvailable = false;

        // Kernel indices
        private static int kernelRaycast = -1;
        private static int kernelSmartRaycast = -1;

        // Struct for triangle data
        struct Triangle
        {
            public Vector3 v0;
            public Vector3 v1;
            public Vector3 v2;

            public Triangle(Vector3 v0, Vector3 v1, Vector3 v2)
            {
                this.v0 = v0;
                this.v1 = v1;
                this.v2 = v2;
            }
        }

        /// <summary>
        /// Initialize the GPU raycasting system. Call once at startup.
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized) return;

            try
            {
                // Check if compute shaders are supported
                if (!SystemInfo.supportsComputeShaders)
                {
                    Debug.LogWarning("[GPURaycast] Compute shaders not supported on this platform. Using CPU fallback.");
                    gpuAvailable = false;
                    isInitialized = true;
                    return;
                }

                // Load the compute shader
                string shaderPath = "Packages/com.yucp.components/Editor/MeshUtils/VertexRaycastShader.compute";
                raycastShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(shaderPath);

                if (raycastShader == null)
                {
                    Debug.LogWarning("[GPURaycast] Could not load compute shader. Using CPU fallback.");
                    gpuAvailable = false;
                    isInitialized = true;
                    return;
                }

                // Find kernel indices
                kernelRaycast = raycastShader.FindKernel("CSRaycast");
                kernelSmartRaycast = raycastShader.FindKernel("CSSmartRaycast");

                gpuAvailable = true;
                Debug.Log("[GPURaycast] GPU raycasting initialized successfully.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GPURaycast] Failed to initialize GPU raycasting: {e.Message}. Using CPU fallback.");
                gpuAvailable = false;
            }
            finally
            {
                isInitialized = true;
            }
        }

        /// <summary>
        /// Check if GPU raycasting is available.
        /// </summary>
        public static bool IsGPUAvailable()
        {
            if (!isInitialized) Initialize();
            return gpuAvailable;
        }

        /// <summary>
        /// Perform simple raycast detection on the GPU.
        /// </summary>
        public static bool[] RaycastDetection(
            Vector3[] bodyVertices,
            Vector3[] bodyNormals,
            Transform bodyTransform,
            Mesh clothingMesh,
            Transform clothingTransform,
            float raycastDistance)
        {
            if (!IsGPUAvailable())
            {
                throw new InvalidOperationException("GPU raycasting not available. Check IsGPUAvailable() first.");
            }

            int vertexCount = bodyVertices.Length;
            
            // Convert body vertices and normals to world space
            Vector3[] worldVertices = new Vector3[vertexCount];
            Vector3[] worldNormals = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                worldVertices[i] = bodyTransform.TransformPoint(bodyVertices[i]);
                worldNormals[i] = bodyTransform.TransformDirection(bodyNormals[i]).normalized;
            }

            // Get clothing triangles in world space
            Triangle[] clothingTriangles = GetWorldSpaceTriangles(clothingMesh, clothingTransform);

            // Create compute buffers
            ComputeBuffer vertexBuffer = new ComputeBuffer(vertexCount, sizeof(float) * 3);
            ComputeBuffer normalBuffer = new ComputeBuffer(vertexCount, sizeof(float) * 3);
            ComputeBuffer triangleBuffer = new ComputeBuffer(clothingTriangles.Length, sizeof(float) * 9);
            ComputeBuffer resultBuffer = new ComputeBuffer(vertexCount, sizeof(int));

            try
            {
                // Upload data to GPU
                vertexBuffer.SetData(worldVertices);
                normalBuffer.SetData(worldNormals);
                triangleBuffer.SetData(clothingTriangles);

                // Set shader parameters
                raycastShader.SetBuffer(kernelRaycast, "BodyVertices", vertexBuffer);
                raycastShader.SetBuffer(kernelRaycast, "BodyNormals", normalBuffer);
                raycastShader.SetBuffer(kernelRaycast, "ClothingTriangles", triangleBuffer);
                raycastShader.SetBuffer(kernelRaycast, "Results", resultBuffer);
                raycastShader.SetFloat("RaycastDistance", raycastDistance);
                raycastShader.SetInt("TriangleCount", clothingTriangles.Length);

                // Dispatch compute shader
                int threadGroups = Mathf.CeilToInt(vertexCount / 64.0f);
                raycastShader.Dispatch(kernelRaycast, threadGroups, 1, 1);

                // Read results back from GPU
                int[] results = new int[vertexCount];
                resultBuffer.GetData(results);

                // Convert to bool array
                bool[] hidden = new bool[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    hidden[i] = results[i] == 1;
                }

                return hidden;
            }
            finally
            {
                // Clean up buffers
                vertexBuffer?.Release();
                normalBuffer?.Release();
                triangleBuffer?.Release();
                resultBuffer?.Release();
            }
        }

        /// <summary>
        /// Perform smart multi-directional raycast detection on the GPU.
        /// </summary>
        public static bool[] SmartRaycastDetection(
            Vector3[] bodyVertices,
            Vector3[] bodyNormals,
            Transform bodyTransform,
            Mesh clothingMesh,
            Transform clothingTransform,
            float raycastDistance,
            int rayDirections,
            bool useNormals,
            bool requireBidirectional,
            float occlusionThreshold,
            float rayOffset = 0.005f,
            bool conservativeMode = false,
            float minDistanceToClothing = 0.01f)
        {
            if (!IsGPUAvailable())
            {
                throw new InvalidOperationException("GPU raycasting not available. Check IsGPUAvailable() first.");
            }

            int vertexCount = bodyVertices.Length;
            
            // Convert body vertices and normals to world space
            Vector3[] worldVertices = new Vector3[vertexCount];
            Vector3[] worldNormals = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                worldVertices[i] = bodyTransform.TransformPoint(bodyVertices[i]);
                worldNormals[i] = bodyTransform.TransformDirection(bodyNormals[i]).normalized;
            }

            // Get clothing triangles in world space
            Triangle[] clothingTriangles = GetWorldSpaceTriangles(clothingMesh, clothingTransform);

            // Generate test directions using Fibonacci sphere
            Vector3[] testDirections = GenerateFibonacciSphere(rayDirections);

            // Create compute buffers
            ComputeBuffer vertexBuffer = new ComputeBuffer(vertexCount, sizeof(float) * 3);
            ComputeBuffer normalBuffer = new ComputeBuffer(vertexCount, sizeof(float) * 3);
            ComputeBuffer triangleBuffer = new ComputeBuffer(clothingTriangles.Length, sizeof(float) * 9);
            ComputeBuffer directionBuffer = new ComputeBuffer(testDirections.Length, sizeof(float) * 3);
            ComputeBuffer resultBuffer = new ComputeBuffer(vertexCount, sizeof(int));

            try
            {
                // Upload data to GPU
                vertexBuffer.SetData(worldVertices);
                normalBuffer.SetData(worldNormals);
                triangleBuffer.SetData(clothingTriangles);
                directionBuffer.SetData(testDirections);

                // Set shader parameters
                raycastShader.SetBuffer(kernelSmartRaycast, "BodyVertices", vertexBuffer);
                raycastShader.SetBuffer(kernelSmartRaycast, "BodyNormals", normalBuffer);
                raycastShader.SetBuffer(kernelSmartRaycast, "ClothingTriangles", triangleBuffer);
                raycastShader.SetBuffer(kernelSmartRaycast, "TestDirections", directionBuffer);
                raycastShader.SetBuffer(kernelSmartRaycast, "Results", resultBuffer);
                raycastShader.SetFloat("RaycastDistance", raycastDistance);
                raycastShader.SetInt("TriangleCount", clothingTriangles.Length);
                raycastShader.SetInt("DirectionCount", testDirections.Length);
                raycastShader.SetFloat("OcclusionThreshold", occlusionThreshold);
                raycastShader.SetInt("UseNormals", useNormals ? 1 : 0);
                raycastShader.SetInt("RequireBidirectional", requireBidirectional ? 1 : 0);
                raycastShader.SetFloat("RayOffset", rayOffset);
                raycastShader.SetInt("ConservativeMode", conservativeMode ? 1 : 0);
                raycastShader.SetFloat("MinDistanceToClothing", minDistanceToClothing);

                // Dispatch compute shader
                int threadGroups = Mathf.CeilToInt(vertexCount / 64.0f);
                raycastShader.Dispatch(kernelSmartRaycast, threadGroups, 1, 1);

                // Read results back from GPU
                int[] results = new int[vertexCount];
                resultBuffer.GetData(results);

                // Convert to bool array
                bool[] hidden = new bool[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    hidden[i] = results[i] == 1;
                }

                return hidden;
            }
            finally
            {
                // Clean up buffers
                vertexBuffer?.Release();
                normalBuffer?.Release();
                triangleBuffer?.Release();
                directionBuffer?.Release();
                resultBuffer?.Release();
            }
        }

        /// <summary>
        /// Convert mesh triangles to world space.
        /// </summary>
        private static Triangle[] GetWorldSpaceTriangles(Mesh mesh, Transform transform)
        {
            Vector3[] vertices = mesh.vertices;
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

                worldTriangles[i] = new Triangle(v0, v1, v2);
            }

            return worldTriangles;
        }

        /// <summary>
        /// Generate evenly distributed points on a sphere using Fibonacci lattice.
        /// </summary>
        private static Vector3[] GenerateFibonacciSphere(int samples)
        {
            Vector3[] points = new Vector3[samples];
            float phi = Mathf.PI * (3.0f - Mathf.Sqrt(5.0f)); // Golden angle in radians

            for (int i = 0; i < samples; i++)
            {
                float y = 1 - (i / (float)(samples - 1)) * 2; // y goes from 1 to -1
                float radius = Mathf.Sqrt(1 - y * y); // radius at y

                float theta = phi * i;

                float x = Mathf.Cos(theta) * radius;
                float z = Mathf.Sin(theta) * radius;

                points[i] = new Vector3(x, y, z).normalized;
            }

            return points;
        }
    }
}

