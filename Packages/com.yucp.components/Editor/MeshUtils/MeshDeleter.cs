using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Utilities for physically deleting vertices from a mesh.
    /// </summary>
    public static class MeshDeleter
    {
        /// <summary>
        /// Create a new mesh with hidden vertices removed.
        /// </summary>
        public static Mesh DeleteHiddenVertices(
            Mesh originalMesh,
            bool[] hiddenVertices)
        {
            // Create vertex mapping (old index -> new index)
            int[] vertexMapping = new int[hiddenVertices.Length];
            int newVertexCount = 0;
            
            for (int i = 0; i < hiddenVertices.Length; i++)
            {
                if (!hiddenVertices[i])
                {
                    vertexMapping[i] = newVertexCount;
                    newVertexCount++;
                }
                else
                {
                    vertexMapping[i] = -1; // Mark as deleted
                }
            }
            
            if (newVertexCount == hiddenVertices.Length)
            {
                // No vertices hidden, return original
                return originalMesh;
            }
            
            // Create new mesh
            Mesh newMesh = new Mesh();
            newMesh.name = originalMesh.name + "_Deleted";
            
            // Copy vertex data
            CopyVertexData(originalMesh, newMesh, hiddenVertices, vertexMapping, newVertexCount);
            
            // Rebuild triangles
            RebuildTriangles(originalMesh, newMesh, vertexMapping);
            
            // Copy blend shapes
            CopyBlendShapes(originalMesh, newMesh, hiddenVertices, vertexMapping, newVertexCount);
            
            // Recalculate bounds
            newMesh.RecalculateBounds();
            newMesh.RecalculateNormals();
            newMesh.RecalculateTangents();
            
            return newMesh;
        }

        /// <summary>
        /// Copy vertex data (positions, normals, UVs, colors, etc.) excluding hidden vertices.
        /// </summary>
        private static void CopyVertexData(
            Mesh source,
            Mesh dest,
            bool[] hiddenVertices,
            int[] vertexMapping,
            int newVertexCount)
        {
            // Positions
            Vector3[] sourcePositions = source.vertices;
            Vector3[] newPositions = new Vector3[newVertexCount];
            
            // Normals
            Vector3[] sourceNormals = source.normals;
            Vector3[] newNormals = sourceNormals.Length > 0 ? new Vector3[newVertexCount] : null;
            
            // Tangents
            Vector4[] sourceTangents = source.tangents;
            Vector4[] newTangents = sourceTangents.Length > 0 ? new Vector4[newVertexCount] : null;
            
            // Colors
            Color[] sourceColors = source.colors;
            Color[] newColors = sourceColors.Length > 0 ? new Color[newVertexCount] : null;
            
            // UVs
            List<Vector2> sourceUV0List = new List<Vector2>();
            source.GetUVs(0, sourceUV0List);
            Vector2[] sourceUV0 = sourceUV0List.ToArray();
            Vector2[] newUV0 = sourceUV0.Length > 0 ? new Vector2[newVertexCount] : null;
            
            List<Vector2> sourceUV1List = new List<Vector2>();
            source.GetUVs(1, sourceUV1List);
            Vector2[] sourceUV1 = sourceUV1List.ToArray();
            Vector2[] newUV1 = sourceUV1.Length > 0 ? new Vector2[newVertexCount] : null;
            
            List<Vector2> sourceUV2List = new List<Vector2>();
            source.GetUVs(2, sourceUV2List);
            Vector2[] sourceUV2 = sourceUV2List.ToArray();
            Vector2[] newUV2 = sourceUV2.Length > 0 ? new Vector2[newVertexCount] : null;
            
            List<Vector2> sourceUV3List = new List<Vector2>();
            source.GetUVs(3, sourceUV3List);
            Vector2[] sourceUV3 = sourceUV3List.ToArray();
            Vector2[] newUV3 = sourceUV3.Length > 0 ? new Vector2[newVertexCount] : null;
            
            // Bone weights
            BoneWeight[] sourceBoneWeights = source.boneWeights;
            BoneWeight[] newBoneWeights = sourceBoneWeights.Length > 0 ? new BoneWeight[newVertexCount] : null;
            
            // Copy data for visible vertices
            for (int i = 0; i < hiddenVertices.Length; i++)
            {
                if (!hiddenVertices[i])
                {
                    int newIndex = vertexMapping[i];
                    
                    newPositions[newIndex] = sourcePositions[i];
                    
                    if (newNormals != null && i < sourceNormals.Length)
                        newNormals[newIndex] = sourceNormals[i];
                    
                    if (newTangents != null && i < sourceTangents.Length)
                        newTangents[newIndex] = sourceTangents[i];
                    
                    if (newColors != null && i < sourceColors.Length)
                        newColors[newIndex] = sourceColors[i];
                    
                    if (newUV0 != null && i < sourceUV0.Length)
                        newUV0[newIndex] = sourceUV0[i];
                    
                    if (newUV1 != null && i < sourceUV1.Length)
                        newUV1[newIndex] = sourceUV1[i];
                    
                    if (newUV2 != null && i < sourceUV2.Length)
                        newUV2[newIndex] = sourceUV2[i];
                    
                    if (newUV3 != null && i < sourceUV3.Length)
                        newUV3[newIndex] = sourceUV3[i];
                    
                    if (newBoneWeights != null && i < sourceBoneWeights.Length)
                        newBoneWeights[newIndex] = sourceBoneWeights[i];
                }
            }
            
            // Apply to destination mesh
            dest.vertices = newPositions;
            
            if (newNormals != null)
                dest.normals = newNormals;
            
            if (newTangents != null)
                dest.tangents = newTangents;
            
            if (newColors != null)
                dest.colors = newColors;
            
            if (newUV0 != null)
                dest.SetUVs(0, new List<Vector2>(newUV0));
            
            if (newUV1 != null)
                dest.SetUVs(1, new List<Vector2>(newUV1));
            
            if (newUV2 != null)
                dest.SetUVs(2, new List<Vector2>(newUV2));
            
            if (newUV3 != null)
                dest.SetUVs(3, new List<Vector2>(newUV3));
            
            if (newBoneWeights != null)
            {
                dest.boneWeights = newBoneWeights;
                dest.bindposes = source.bindposes;
            }
        }

        /// <summary>
        /// Rebuild triangle indices, excluding triangles that reference hidden vertices.
        /// </summary>
        private static void RebuildTriangles(
            Mesh source,
            Mesh dest,
            int[] vertexMapping)
        {
            dest.subMeshCount = source.subMeshCount;
            
            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
            {
                int[] sourceIndices = source.GetTriangles(subMesh);
                List<int> newIndices = new List<int>();
                
                // Process triangles
                for (int i = 0; i < sourceIndices.Length; i += 3)
                {
                    int v0 = sourceIndices[i];
                    int v1 = sourceIndices[i + 1];
                    int v2 = sourceIndices[i + 2];
                    
                    // Only keep triangle if all vertices are visible
                    if (vertexMapping[v0] >= 0 && 
                        vertexMapping[v1] >= 0 && 
                        vertexMapping[v2] >= 0)
                    {
                        newIndices.Add(vertexMapping[v0]);
                        newIndices.Add(vertexMapping[v1]);
                        newIndices.Add(vertexMapping[v2]);
                    }
                }
                
                dest.SetTriangles(newIndices, subMesh);
            }
        }

        /// <summary>
        /// Copy blend shapes, adjusting for removed vertices.
        /// </summary>
        private static void CopyBlendShapes(
            Mesh source,
            Mesh dest,
            bool[] hiddenVertices,
            int[] vertexMapping,
            int newVertexCount)
        {
            int blendShapeCount = source.blendShapeCount;
            
            if (blendShapeCount == 0)
                return;
            
            for (int shapeIndex = 0; shapeIndex < blendShapeCount; shapeIndex++)
            {
                string shapeName = source.GetBlendShapeName(shapeIndex);
                int frameCount = source.GetBlendShapeFrameCount(shapeIndex);
                
                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    float frameWeight = source.GetBlendShapeFrameWeight(shapeIndex, frameIndex);
                    
                    Vector3[] deltaVertices = new Vector3[source.vertexCount];
                    Vector3[] deltaNormals = new Vector3[source.vertexCount];
                    Vector3[] deltaTangents = new Vector3[source.vertexCount];
                    
                    source.GetBlendShapeFrameVertices(shapeIndex, frameIndex, deltaVertices, deltaNormals, deltaTangents);
                    
                    // Filter out hidden vertices
                    Vector3[] newDeltaVertices = new Vector3[newVertexCount];
                    Vector3[] newDeltaNormals = new Vector3[newVertexCount];
                    Vector3[] newDeltaTangents = new Vector3[newVertexCount];
                    
                    for (int i = 0; i < hiddenVertices.Length; i++)
                    {
                        if (!hiddenVertices[i])
                        {
                            int newIndex = vertexMapping[i];
                            newDeltaVertices[newIndex] = deltaVertices[i];
                            newDeltaNormals[newIndex] = deltaNormals[i];
                            newDeltaTangents[newIndex] = deltaTangents[i];
                        }
                    }
                    
                    dest.AddBlendShapeFrame(shapeName, frameWeight, newDeltaVertices, newDeltaNormals, newDeltaTangents);
                }
            }
        }
    }
}

