using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Build-time utilities to merge an attachment mesh into a base SkinnedMeshRenderer mesh.
    /// This is used by AttachToBlendshape MergeIntoBaseMesh output mode.
    /// </summary>
    public static class SkinnedMeshMerge
    {
        public struct MergeResult
        {
            public Mesh mergedMesh;
            public int baseVertexCount;
            public int attachmentVertexStart;
            public int attachmentVertexCount;
            public int baseSubmeshCount;
            public int attachmentSubmeshCount;
            public Material[] mergedMaterials;
        }

        public static int[] GetCombinedTriangles(Mesh mesh)
        {
            if (mesh == null) return Array.Empty<int>();
            var tris = new List<int>();
            int sub = Math.Max(1, mesh.subMeshCount);
            for (int s = 0; s < sub; s++)
            {
                try
                {
                    tris.AddRange(mesh.GetTriangles(s));
                }
                catch
                {
                    // If submesh access fails, fall back to mesh.triangles
                    return mesh.triangles ?? Array.Empty<int>();
                }
            }
            return tris.ToArray();
        }

        public static MergeResult Merge(
            SkinnedMeshRenderer baseSmr,
            Mesh attachmentMesh,
            Transform attachmentTransform,
            Material[] attachmentMaterials,
            ClosestSurfaceMapper.SurfaceMap[] surfaceMaps,
            int[] baseTrianglesCombined)
        {
            return Merge(
                baseSmr,
                attachmentMesh,
                attachmentTransform,
                attachmentMaterials,
                surfaceMaps,
                baseTrianglesCombined,
                null);
        }

        public static MergeResult Merge(
            SkinnedMeshRenderer baseSmr,
            Mesh attachmentMesh,
            Transform attachmentTransform,
            Material[] attachmentMaterials,
            ClosestSurfaceMapper.SurfaceMap[] surfaceMaps,
            int[] baseTrianglesCombined,
            AttachToBlendshapeData data)
        {
            if (baseSmr == null || baseSmr.sharedMesh == null || attachmentMesh == null || attachmentTransform == null)
            {
                return default;
            }

            var baseMesh = baseSmr.sharedMesh;

            // --- Build combined vertex attributes ---
            int baseV = baseMesh.vertexCount;
            int attV = attachmentMesh.vertexCount;
            int totalV = baseV + attV;

            var baseVerts = baseMesh.vertices;
            var baseNormals = baseMesh.normals;
            var baseTangents = baseMesh.tangents;
            var baseColors = baseMesh.colors;

            var baseUv0 = new List<Vector2>();
            baseMesh.GetUVs(0, baseUv0);
            var baseUv1 = new List<Vector2>();
            baseMesh.GetUVs(1, baseUv1);

            var attVertsLocal = attachmentMesh.vertices;
            var attNormalsLocal = attachmentMesh.normals;
            var attTangentsLocal = attachmentMesh.tangents;
            var attColors = attachmentMesh.colors;
            var attUv0 = new List<Vector2>();
            attachmentMesh.GetUVs(0, attUv0);
            var attUv1 = new List<Vector2>();
            attachmentMesh.GetUVs(1, attUv1);

            var verts = new Vector3[totalV];
            var normals = new Vector3[totalV];
            var tangents = new Vector4[totalV];
            Color[] colors = null;

            bool hasColors = (baseColors != null && baseColors.Length == baseV) || (attColors != null && attColors.Length == attV);
            if (hasColors) colors = new Color[totalV];

            // Copy base
            Array.Copy(baseVerts, 0, verts, 0, baseV);
            if (baseNormals != null && baseNormals.Length == baseV) Array.Copy(baseNormals, 0, normals, 0, baseV);
            else for (int i = 0; i < baseV; i++) normals[i] = Vector3.up;

            if (baseTangents != null && baseTangents.Length == baseV) Array.Copy(baseTangents, 0, tangents, 0, baseV);
            else for (int i = 0; i < baseV; i++) tangents[i] = new Vector4(1, 0, 0, 1);

            if (colors != null)
            {
                if (baseColors != null && baseColors.Length == baseV) Array.Copy(baseColors, 0, colors, 0, baseV);
                else for (int i = 0; i < baseV; i++) colors[i] = Color.white;
            }

            // Transform attachment vertices into base local space
            for (int i = 0; i < attV; i++)
            {
                var wPos = attachmentTransform.TransformPoint(attVertsLocal[i]);
                verts[baseV + i] = baseSmr.transform.InverseTransformPoint(wPos);

                Vector3 nLocal = (attNormalsLocal != null && attNormalsLocal.Length == attV) ? attNormalsLocal[i] : Vector3.up;
                var wN = attachmentTransform.TransformDirection(nLocal);
                normals[baseV + i] = baseSmr.transform.InverseTransformDirection(wN).normalized;

                Vector4 tLocal4 = (attTangentsLocal != null && attTangentsLocal.Length == attV) ? attTangentsLocal[i] : new Vector4(1, 0, 0, 1);
                var wT = attachmentTransform.TransformDirection(new Vector3(tLocal4.x, tLocal4.y, tLocal4.z));
                var bT = baseSmr.transform.InverseTransformDirection(wT).normalized;
                tangents[baseV + i] = new Vector4(bT.x, bT.y, bT.z, tLocal4.w);

                if (colors != null)
                {
                    colors[baseV + i] = (attColors != null && attColors.Length == attV) ? attColors[i] : Color.white;
                }
            }

            // UVs
            var uv0 = new List<Vector2>(totalV);
            var uv1 = new List<Vector2>(totalV);

            // Base UV0
            if (baseUv0.Count == baseV) uv0.AddRange(baseUv0);
            else
            {
                uv0.AddRange(new Vector2[baseV]);
            }
            // Attachment UV0
            if (attUv0.Count == attV) uv0.AddRange(attUv0);
            else
            {
                uv0.AddRange(new Vector2[attV]);
            }

            // Base UV1
            if (baseUv1.Count == baseV) uv1.AddRange(baseUv1);
            else
            {
                uv1.AddRange(new Vector2[baseV]);
            }
            // Attachment UV1
            if (attUv1.Count == attV) uv1.AddRange(attUv1);
            else
            {
                uv1.AddRange(new Vector2[attV]);
            }

            // --- Bone weights ---
            var baseBoneWeights = baseMesh.boneWeights;
            if (baseBoneWeights == null || baseBoneWeights.Length != baseV)
            {
                // If base mesh has no valid skinning, we cannot merge.
                Debug.LogError("[SkinnedMeshMerge] Base mesh has no valid bone weights; cannot merge.");
                return default;
            }

            var outBoneWeights = new BoneWeight[totalV];
            Array.Copy(baseBoneWeights, 0, outBoneWeights, 0, baseV);

            // Compute attachment bone weights by barycentric interpolation on the base surface.
            for (int i = 0; i < attV; i++)
            {
                outBoneWeights[baseV + i] = InterpolateBoneWeight(surfaceMaps, i, baseTrianglesCombined, baseBoneWeights);
            }

            // --- Submeshes / triangles ---
            var merged = new Mesh();
            merged.name = $"{baseMesh.name}_YUCP_Merged_{attachmentMesh.name}";
            merged.vertices = verts;
            merged.normals = normals;
            merged.tangents = tangents;
            if (colors != null) merged.colors = colors;
            merged.SetUVs(0, uv0);
            merged.SetUVs(1, uv1);

            merged.bindposes = baseMesh.bindposes;
            merged.boneWeights = outBoneWeights;

            int baseSub = Math.Max(1, baseMesh.subMeshCount);
            int attSub = Math.Max(1, attachmentMesh.subMeshCount);
            merged.subMeshCount = baseSub + attSub;

            for (int s = 0; s < baseSub; s++)
            {
                merged.SetTriangles(baseMesh.GetTriangles(s), s);
            }

            for (int s = 0; s < attSub; s++)
            {
                var tris = attachmentMesh.GetTriangles(s);
                for (int t = 0; t < tris.Length; t++)
                {
                    tris[t] += baseV;
                }
                merged.SetTriangles(tris, baseSub + s);
            }

            merged.RecalculateBounds();

            // Materials
            var baseMats = baseSmr.sharedMaterials ?? Array.Empty<Material>();
            var mats = new List<Material>(baseMats.Length + attSub);
            mats.AddRange(baseMats);

            if (attachmentMaterials == null) attachmentMaterials = Array.Empty<Material>();
            if (attachmentMaterials.Length == 0)
            {
                // Keep material slots aligned to submesh count even if missing.
                for (int i = 0; i < attSub; i++) mats.Add(null);
            }
            else if (attachmentMaterials.Length == attSub)
            {
                mats.AddRange(attachmentMaterials);
            }
            else
            {
                // Best effort: repeat first material to fill.
                var first = attachmentMaterials[0];
                for (int i = 0; i < attSub; i++) mats.Add(i < attachmentMaterials.Length ? attachmentMaterials[i] : first);
            }

            return new MergeResult
            {
                mergedMesh = merged,
                baseVertexCount = baseV,
                attachmentVertexStart = baseV,
                attachmentVertexCount = attV,
                baseSubmeshCount = baseSub,
                attachmentSubmeshCount = attSub,
                mergedMaterials = mats.ToArray()
            };
        }

        private static BoneWeight InterpolateBoneWeight(
            ClosestSurfaceMapper.SurfaceMap[] maps,
            int attachmentVertexIndex,
            int[] baseTrianglesCombined,
            BoneWeight[] baseBoneWeights)
        {
            if (maps == null || attachmentVertexIndex < 0 || attachmentVertexIndex >= maps.Length)
            {
                return DefaultBoneWeight();
            }

            var map = maps[attachmentVertexIndex];
            if (!map.matched || map.triIndex < 0 || baseTrianglesCombined == null)
            {
                return DefaultBoneWeight();
            }

            int t0 = map.triIndex * 3;
            if (t0 + 2 >= baseTrianglesCombined.Length)
            {
                return DefaultBoneWeight();
            }

            int i0 = baseTrianglesCombined[t0];
            int i1 = baseTrianglesCombined[t0 + 1];
            int i2 = baseTrianglesCombined[t0 + 2];
            if ((uint)i0 >= (uint)baseBoneWeights.Length ||
                (uint)i1 >= (uint)baseBoneWeights.Length ||
                (uint)i2 >= (uint)baseBoneWeights.Length)
            {
                return DefaultBoneWeight();
            }

            Vector3 b = map.barycentric;

            var accum = new Dictionary<int, float>(16);
            Accum(accum, baseBoneWeights[i0], b.x);
            Accum(accum, baseBoneWeights[i1], b.y);
            Accum(accum, baseBoneWeights[i2], b.z);

            // Keep top 4 influences
            var top = accum
                .Where(kv => kv.Value > 0f)
                .OrderByDescending(kv => kv.Value)
                .Take(4)
                .ToArray();

            if (top.Length == 0) return DefaultBoneWeight();

            float sum = top.Sum(kv => kv.Value);
            if (sum <= 1e-8f) return DefaultBoneWeight();

            var bw = new BoneWeight();
            for (int i = 0; i < top.Length; i++)
            {
                float w = top[i].Value / sum;
                switch (i)
                {
                    case 0: bw.boneIndex0 = top[i].Key; bw.weight0 = w; break;
                    case 1: bw.boneIndex1 = top[i].Key; bw.weight1 = w; break;
                    case 2: bw.boneIndex2 = top[i].Key; bw.weight2 = w; break;
                    case 3: bw.boneIndex3 = top[i].Key; bw.weight3 = w; break;
                }
            }

            return bw;
        }

        private static void Accum(Dictionary<int, float> accum, BoneWeight bw, float factor)
        {
            if (factor <= 0f) return;
            Add(accum, bw.boneIndex0, bw.weight0 * factor);
            Add(accum, bw.boneIndex1, bw.weight1 * factor);
            Add(accum, bw.boneIndex2, bw.weight2 * factor);
            Add(accum, bw.boneIndex3, bw.weight3 * factor);
        }

        private static void Add(Dictionary<int, float> accum, int bone, float w)
        {
            if (w <= 0f) return;
            if (accum.TryGetValue(bone, out var existing))
            {
                accum[bone] = existing + w;
            }
            else
            {
                accum[bone] = w;
            }
        }

        private static BoneWeight DefaultBoneWeight()
        {
            return new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        }
    }
}

