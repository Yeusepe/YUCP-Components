using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    public static class UVManipulator
    {
        /// <summary>
        /// Check if material supports UV discard (Poiyomi or FastFur).
        /// Both shaders use the same property naming convention.
        /// </summary>
        public static bool IsPoiyomiWithUVSupport(Material material)
        {
            if (material == null || material.shader == null)
                return false;
            
            string shaderName = material.shader.name;
            string shaderNameLower = shaderName.ToLower();
            
            // Check for Poiyomi
            if (shaderNameLower.Contains("poiyomi"))
            {
                return material.HasProperty("_EnableUDIMDiscardOptions");
            }
            
            // Check for FastFur (Warren's Fast Fur Shader)
            if (shaderNameLower.Contains("fastfur") || shaderNameLower.Contains("fast fur") || shaderNameLower.Contains("wffs") || shaderNameLower.Contains("warren"))
            {
                // FastFur requires the UV Discard module to be installed
                bool hasUVProperty = material.HasProperty("_EnableUDIMDiscardOptions");
                bool hasFeatureToggle = material.HasProperty("_WFFS_FEATURES_UVDISCARD");
                
                return hasUVProperty || hasFeatureToggle;
            }
            
            return false;
        }
        
        public static string GetShaderDisplayName(Material material)
        {
            if (material == null || material.shader == null)
                return "Unknown";
            
            string shaderName = material.shader.name.ToLower();
            
            if (shaderName.Contains("poiyomi"))
                return "Poiyomi";
            
            if (shaderName.Contains("fastfur") || shaderName.Contains("fast fur") || shaderName.Contains("wffs") || shaderName.Contains("warren"))
                return "FastFur";
            
            return material.shader.name;
        }

        public static Mesh ApplyUVDiscard(
            Mesh originalMesh,
            bool[] hiddenVertices,
            AutoBodyHiderData data)
        {
            int targetUVChannel = GetEffectiveUVChannel(data, originalMesh);
#pragma warning disable 0612, 0618
            return ApplyUVDiscard(originalMesh, hiddenVertices, data.uvDiscardRow, data.uvDiscardColumn, targetUVChannel);
#pragma warning restore 0612, 0618
        }

        public static Mesh ApplyUVDiscard(
            Mesh originalMesh,
            bool[] hiddenVertices,
            int uvRow,
            int uvColumn,
            int targetUVChannel)
        {
            Mesh newMesh = UnityEngine.Object.Instantiate(originalMesh);
            newMesh.name = originalMesh.name + "_UVHidden";
            
            // Get source UV channel (usually UV0 for texture mapping)
            Vector2[] sourceUV = GetUVChannel(originalMesh, 0);
            
            if (sourceUV == null || sourceUV.Length == 0)
            {
                Debug.LogError($"[UVManipulator] No UV0 found on mesh.");
                return originalMesh;
            }
            
            // Get or create target UV channel
            Vector2[] targetUV = GetUVChannel(originalMesh, targetUVChannel);
            
            // If target channel doesn't exist, create it from UV0
            if (targetUV == null || targetUV.Length == 0)
            {
                targetUV = new Vector2[sourceUV.Length];
                Array.Copy(sourceUV, targetUV, sourceUV.Length);
                Debug.Log($"[UVManipulator] UV{targetUVChannel} not found, creating from UV0.");
            }
            else if (targetUV.Length != sourceUV.Length)
            {
                targetUV = new Vector2[sourceUV.Length];
                Array.Copy(sourceUV, targetUV, sourceUV.Length);
                Debug.LogWarning($"[UVManipulator] UV{targetUVChannel} length mismatch, recreating from UV0.");
            }
            
            float uOffset = uvColumn;
            float vOffset = uvRow;
            
            for (int i = 0; i < hiddenVertices.Length && i < targetUV.Length; i++)
            {
                if (hiddenVertices[i])
                {
                    targetUV[i] = new Vector2(targetUV[i].x + uOffset, targetUV[i].y + vOffset);
                }
            }
            
            SetUVChannel(newMesh, targetUVChannel, targetUV);
            
            return newMesh;
        }

        public static void ConfigurePoiyomiMaterial(
            Material material,
            AutoBodyHiderData data,
            Mesh mesh = null)
        {
            if (material == null)
            {
                Debug.LogError("[UVManipulator] Material is null.");
                return;
            }
            
            if (!IsPoiyomiWithUVSupport(material))
            {
                Debug.LogWarning($"[UVManipulator] Material {material.name} doesn't support UV discard.");
                return;
            }
            
            string shaderName = GetShaderDisplayName(material);
            string shaderNameLower = material.shader.name.ToLower();
            
            if (shaderNameLower.Contains("poiyomi"))
            {
                material.EnableKeyword("POI_UDIMDISCARD");
                if (material.HasProperty("_EnableUDIMDiscardOptions"))
                {
                    material.SetFloat("_EnableUDIMDiscardOptions", 1f);
                }
            }
            else if (shaderNameLower.Contains("fastfur") || shaderNameLower.Contains("fast fur") || shaderNameLower.Contains("wffs") || shaderNameLower.Contains("warren"))
            {
                if (material.HasProperty("_WFFS_FEATURES_UVDISCARD"))
                {
                    material.SetFloat("_WFFS_FEATURES_UVDISCARD", 1f);
                    material.EnableKeyword("WFFS_FEATURES_UVDISCARD");
                    UnityEditor.EditorUtility.SetDirty(material);
                }
                
                if (material.HasProperty("_EnableUDIMDiscardOptions"))
                {
                    material.SetFloat("_EnableUDIMDiscardOptions", 1f);
                    UnityEditor.EditorUtility.SetDirty(material);
                }
            }
            
            if (material.HasProperty("_UDIMDiscardMode"))
            {
                material.SetFloat("_UDIMDiscardMode", 0f);  // Vertex mode
            }
            
            int effectiveUVChannel = GetEffectiveUVChannel(data, mesh);
            
            if (material.HasProperty("_UDIMDiscardUV"))
            {
                material.SetFloat("_UDIMDiscardUV", effectiveUVChannel);
            }
            else if (material.HasProperty("_UDIMDiscardUVChannel"))
            {
                material.SetFloat("_UDIMDiscardUVChannel", effectiveUVChannel);
            }
            
#pragma warning disable 0612, 0618
            string propertyName = $"_UDIMDiscardRow{data.uvDiscardRow}_{data.uvDiscardColumn}";
            
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, 1f);
                string autoDetectNote = data.autoDetectUVChannel ? " (auto-detected)" : "";
                Debug.Log($"[UVManipulator] Configured {shaderName} material '{material.name}' with UV tile ({data.uvDiscardRow}, {data.uvDiscardColumn}) on UV channel {effectiveUVChannel}{autoDetectNote}");
            }
#pragma warning restore 0612, 0618
            
            EditorUtility.SetDirty(material);
        }

        private static Vector2[] GetUVChannel(Mesh mesh, int channel)
        {
            List<Vector2> uvList = new List<Vector2>();
            
            switch (channel)
            {
                case 0: mesh.GetUVs(0, uvList); break;
                case 1: mesh.GetUVs(1, uvList); break;
                case 2: mesh.GetUVs(2, uvList); break;
                case 3: mesh.GetUVs(3, uvList); break;
                default:
                    Debug.LogError($"[UVManipulator] Invalid UV channel: {channel}");
                    return null;
            }
            
            return uvList.ToArray();
        }

        private static void SetUVChannel(Mesh mesh, int channel, Vector2[] uvs)
        {
            List<Vector2> uvList = new List<Vector2>(uvs);
            
            switch (channel)
            {
                case 0: mesh.SetUVs(0, uvList); break;
                case 1: mesh.SetUVs(1, uvList); break;
                case 2: mesh.SetUVs(2, uvList); break;
                case 3: mesh.SetUVs(3, uvList); break;
                default:
                    Debug.LogError($"[UVManipulator] Invalid UV channel: {channel}");
                    break;
            }
        }
        
        public static int DetectBestUVChannel(Mesh mesh)
        {
            if (mesh == null)
            {
                Debug.LogWarning("[UVManipulator] Cannot detect UV channel: mesh is null. Defaulting to UV1.");
                return 1;
            }
            
            List<Vector2> uvList = new List<Vector2>();
            
            mesh.GetUVs(1, uvList);
            if (uvList != null && uvList.Count > 0)
            {
                List<Vector2> uv0Check = new List<Vector2>();
                mesh.GetUVs(0, uv0Check);
                if (uv0Check.Count == uvList.Count)
                {
                    return 1;
                }
                else if (uv0Check.Count > 0)
                {
                    return 1;
                }
            }
            
            uvList.Clear();
            mesh.GetUVs(0, uvList);
            if (uvList != null && uvList.Count > 0)
            {
                return 1;
            }
            
            for (int channel = 2; channel <= 3; channel++)
            {
                uvList.Clear();
                mesh.GetUVs(channel, uvList);
                if (uvList != null && uvList.Count > 0)
                {
                    return channel;
                }
            }
            
            return 1;
        }
        
        public static int GetEffectiveUVChannel(AutoBodyHiderData data, Mesh mesh)
        {
            if (data.autoDetectUVChannel)
            {
                return DetectBestUVChannel(mesh);
            }
            else
            {
                return data.uvChannel;
            }
        }

        #region Detection Utilities

        public static float SampleMaskAtUV(Texture2D mask, Vector2 uv)
        {
            if (mask == null) return 0f;

            if (!mask.isReadable)
            {
                Debug.LogWarning($"[UVManipulator] Mask texture '{mask.name}' is not readable. Attempting to read via RenderTexture.");
                return SampleNonReadableTexture(mask, uv);
            }

            float u = uv.x - Mathf.Floor(uv.x);
            float v = uv.y - Mathf.Floor(uv.y);

            int x = Mathf.Clamp(Mathf.RoundToInt(u * (mask.width - 1)), 0, mask.width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(v * (mask.height - 1)), 0, mask.height - 1);

            Color pixel = mask.GetPixel(x, y);
            return pixel.grayscale;
        }

        private static float SampleNonReadableTexture(Texture2D texture, Vector2 uv)
        {
            RenderTexture tmp = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, tmp);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = tmp;

            Texture2D readable = new Texture2D(texture.width, texture.height);
            readable.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
            readable.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(tmp);

            float u = uv.x - Mathf.Floor(uv.x);
            float v = uv.y - Mathf.Floor(uv.y);
            int x = Mathf.Clamp(Mathf.RoundToInt(u * (readable.width - 1)), 0, readable.width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(v * (readable.height - 1)), 0, readable.height - 1);

            Color pixel = readable.GetPixel(x, y);
            UnityEngine.Object.DestroyImmediate(readable);

            return pixel.grayscale;
        }

        public static HashSet<(int, int)> FindUVSeamEdges(Mesh mesh, int uvChannel, float threshold = 0.01f)
        {
            var seamEdges = new HashSet<(int, int)>();

            Vector3[] vertices = mesh.vertices;
            List<Vector2> uvList = new List<Vector2>();
            mesh.GetUVs(uvChannel, uvList);

            if (uvList.Count == 0)
            {
                Debug.LogWarning($"[UVManipulator] No UV{uvChannel} data found on mesh.");
                return seamEdges;
            }

            int[] triangles = mesh.triangles;

            var edgeTriangles = new Dictionary<(int, int), List<int>>();
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int triIndex = t / 3;
                for (int e = 0; e < 3; e++)
                {
                    int v0 = triangles[t + e];
                    int v1 = triangles[t + (e + 1) % 3];
                    var edge = v0 < v1 ? (v0, v1) : (v1, v0);

                    if (!edgeTriangles.ContainsKey(edge))
                        edgeTriangles[edge] = new List<int>();
                    edgeTriangles[edge].Add(triIndex);
                }
            }

            foreach (var kvp in edgeTriangles)
            {
                var edge = kvp.Key;
                var tris = kvp.Value;

                if (tris.Count == 2)
                {
                    int t0 = tris[0] * 3;
                    int t1 = tris[1] * 3;

                    bool hasSeam = false;
                    for (int i = 0; i < 3; i++)
                    {
                        int vi0 = triangles[t0 + i];
                        if (vi0 == edge.Item1 || vi0 == edge.Item2)
                        {
                            Vector2 uv_t0 = uvList[triangles[t0 + i]];
                            for (int j = 0; j < 3; j++)
                            {
                                if (triangles[t1 + j] == vi0)
                                {
                                    Vector2 uv_t1 = uvList[triangles[t1 + j]];
                                    if (Vector2.Distance(uv_t0, uv_t1) > threshold)
                                    {
                                        hasSeam = true;
                                        break;
                                    }
                                }
                            }
                        }
                        if (hasSeam) break;
                    }

                    if (hasSeam) seamEdges.Add(edge);
                }
                else if (tris.Count == 1)
                {
                    seamEdges.Add(edge);
                }
            }

            return seamEdges;
        }

        public static HashSet<(int, int)> FindSharpEdges(Mesh mesh, float angleThreshold)
        {
            var sharpEdges = new HashSet<(int, int)>();

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            Vector3[] faceNormals = new Vector3[triangles.Length / 3];
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int triIndex = t / 3;
                Vector3 v0 = vertices[triangles[t]];
                Vector3 v1 = vertices[triangles[t + 1]];
                Vector3 v2 = vertices[triangles[t + 2]];
                faceNormals[triIndex] = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            }

            var edgeTriangles = new Dictionary<(int, int), List<int>>();
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int triIndex = t / 3;
                for (int e = 0; e < 3; e++)
                {
                    int v0 = triangles[t + e];
                    int v1 = triangles[t + (e + 1) % 3];
                    var edge = v0 < v1 ? (v0, v1) : (v1, v0);

                    if (!edgeTriangles.ContainsKey(edge))
                        edgeTriangles[edge] = new List<int>();
                    edgeTriangles[edge].Add(triIndex);
                }
            }

            float cosThreshold = Mathf.Cos(angleThreshold * Mathf.Deg2Rad);
            foreach (var kvp in edgeTriangles)
            {
                var edge = kvp.Key;
                var tris = kvp.Value;

                if (tris.Count == 2)
                {
                    Vector3 n0 = faceNormals[tris[0]];
                    Vector3 n1 = faceNormals[tris[1]];
                    float dot = Vector3.Dot(n0, n1);

                    if (dot < cosThreshold) sharpEdges.Add(edge);
                }
                else if (tris.Count == 1)
                {
                    sharpEdges.Add(edge);
                }
            }

            return sharpEdges;
        }

        public static int[] GetDominantBonePerVertex(Mesh mesh, float boneWeightThreshold = 0.5f)
        {
            BoneWeight[] weights = mesh.boneWeights;
            int[] dominantBones = new int[mesh.vertexCount];

            for (int i = 0; i < weights.Length; i++)
            {
                BoneWeight bw = weights[i];

                int maxBone = -1;
                float maxWeight = boneWeightThreshold;

                if (bw.weight0 >= maxWeight) { maxBone = bw.boneIndex0; maxWeight = bw.weight0; }
                if (bw.weight1 >= maxWeight) { maxBone = bw.boneIndex1; maxWeight = bw.weight1; }
                if (bw.weight2 >= maxWeight) { maxBone = bw.boneIndex2; maxWeight = bw.weight2; }
                if (bw.weight3 >= maxWeight) { maxBone = bw.boneIndex3; maxWeight = bw.weight3; }

                dominantBones[i] = maxBone;
            }

            for (int i = weights.Length; i < mesh.vertexCount; i++)
            {
                dominantBones[i] = -1;
            }

            return dominantBones;
        }

        public static Dictionary<int, List<int>> GroupVerticesByBone(Mesh mesh, HashSet<int> targetBoneIndices, float boneWeightThreshold = 0.5f)
        {
            var groups = new Dictionary<int, List<int>>();
            foreach (int boneIdx in targetBoneIndices)
            {
                groups[boneIdx] = new List<int>();
            }

            BoneWeight[] weights = mesh.boneWeights;
            for (int v = 0; v < weights.Length; v++)
            {
                BoneWeight bw = weights[v];

                if (bw.weight0 >= boneWeightThreshold && targetBoneIndices.Contains(bw.boneIndex0))
                    groups[bw.boneIndex0].Add(v);
                else if (bw.weight1 >= boneWeightThreshold && targetBoneIndices.Contains(bw.boneIndex1))
                    groups[bw.boneIndex1].Add(v);
                else if (bw.weight2 >= boneWeightThreshold && targetBoneIndices.Contains(bw.boneIndex2))
                    groups[bw.boneIndex2].Add(v);
                else if (bw.weight3 >= boneWeightThreshold && targetBoneIndices.Contains(bw.boneIndex3))
                    groups[bw.boneIndex3].Add(v);
            }

            return groups;
        }

        public static Dictionary<int, List<int>> GroupVerticesBySubmesh(Mesh mesh)
        {
            var groups = new Dictionary<int, List<int>>();

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                groups[submesh] = new List<int>();
                int[] triangles = mesh.GetTriangles(submesh);

                var vertexSet = new HashSet<int>();
                foreach (int tri in triangles)
                {
                    vertexSet.Add(tri);
                }

                groups[submesh].AddRange(vertexSet);
            }

            return groups;
        }

        public static List<List<int>> FloodFillRegionsFromEdges(Mesh mesh, HashSet<(int, int)> boundaryEdges)
        {
            var regions = new List<List<int>>();
            bool[] visited = new bool[mesh.vertexCount];
            int[] triangles = mesh.triangles;

            var adjacency = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                adjacency[i] = new HashSet<int>();
            }

            for (int t = 0; t < triangles.Length; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int v0 = triangles[t + e];
                    int v1 = triangles[t + (e + 1) % 3];
                    var edge = v0 < v1 ? (v0, v1) : (v1, v0);

                    if (!boundaryEdges.Contains(edge))
                    {
                        adjacency[v0].Add(v1);
                        adjacency[v1].Add(v0);
                    }
                }
            }

            for (int start = 0; start < mesh.vertexCount; start++)
            {
                if (visited[start]) continue;

                var region = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(start);
                visited[start] = true;

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    region.Add(current);

                    foreach (int neighbor in adjacency[current])
                    {
                        if (!visited[neighbor])
                        {
                            visited[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                if (region.Count > 0)
                {
                    regions.Add(region);
                }
            }

            return regions;
        }

        #endregion
    }
}

