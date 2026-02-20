using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
    public static class FbxMergeToggleUvDiscardMapper
    {
        public static int ResolveUvChannel(FbxMergeData data, Mesh mesh)
        {
            if (mesh == null)
            {
                return Mathf.Clamp(data != null ? data.uvChannel : 1, 0, 3);
            }

            if (data != null && data.autoDetectUVChannel)
            {
                return Mathf.Clamp(UVManipulator.DetectBestUVChannel(mesh), 0, 3);
            }

            return Mathf.Clamp(data != null ? data.uvChannel : 1, 0, 3);
        }

        public static string GetTilePropertyName(int row, int col)
        {
            return $"_UDIMDiscardRow{row}_{col}";
        }

        public static string GetMaterialTilePropertyPath(int materialIndex, string tilePropertyName)
        {
            if (materialIndex <= 0)
            {
                return $"material.{tilePropertyName}";
            }

            return $"materials.Array.data[{materialIndex}].{tilePropertyName}";
        }

        public static void ApplyAttachmentTileToMergedMesh(
            Mesh mergedMesh,
            int attachmentVertexStart,
            int attachmentVertexCount,
            int uvChannel,
            int row,
            int col)
        {
            if (mergedMesh == null || attachmentVertexCount <= 0)
            {
                return;
            }

            List<Vector2> uv0 = new List<Vector2>();
            mergedMesh.GetUVs(0, uv0);
            if (uv0.Count == 0)
            {
                return;
            }

            List<Vector2> targetUv = new List<Vector2>();
            mergedMesh.GetUVs(uvChannel, targetUv);
            if (targetUv.Count != uv0.Count)
            {
                targetUv = new List<Vector2>(uv0);
            }

            int end = Mathf.Min(attachmentVertexStart + attachmentVertexCount, targetUv.Count);
            for (int i = Mathf.Max(attachmentVertexStart, 0); i < end; i++)
            {
                Vector2 uv = targetUv[i];
                targetUv[i] = new Vector2(uv.x + col, uv.y + row);
            }

            mergedMesh.SetUVs(uvChannel, targetUv);
        }

        public static List<int> ConfigureAttachmentMaterialsForUvDiscard(
            SkinnedMeshRenderer baseRenderer,
            int attachmentMaterialStartIndex,
            int attachmentMaterialCount,
            int uvChannel,
            int row,
            int col,
            bool debugMode,
            UnityEngine.Object context = null,
            bool defaultHidden = false)
        {
            List<int> configuredIndices = new List<int>();
            if (baseRenderer == null)
            {
                return configuredIndices;
            }

            Material[] materials = baseRenderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                return configuredIndices;
            }

            int start = Mathf.Max(0, attachmentMaterialStartIndex);
            int end = Mathf.Min(materials.Length, start + Mathf.Max(0, attachmentMaterialCount));
            string tileProperty = GetTilePropertyName(row, col);

            for (int i = start; i < end; i++)
            {
                var mat = materials[i];
                if (!UVManipulator.IsPoiyomiWithUVSupport(mat))
                {
                    continue;
                }

                Material copy = Object.Instantiate(mat);
                copy.name = mat.name + "_MergedFbx";

                string shaderNameLower = copy.shader != null ? copy.shader.name.ToLower() : string.Empty;
                if (shaderNameLower.Contains("poiyomi"))
                {
                    copy.EnableKeyword("POI_UDIMDISCARD");
                }
                else if (shaderNameLower.Contains("fastfur") || shaderNameLower.Contains("fast fur") || shaderNameLower.Contains("wffs") || shaderNameLower.Contains("warren"))
                {
                    copy.EnableKeyword("WFFS_FEATURES_UVDISCARD");
                    if (copy.HasProperty("_WFFS_FEATURES_UVDISCARD"))
                    {
                        copy.SetFloat("_WFFS_FEATURES_UVDISCARD", 1f);
                        try { copy.SetInt("_WFFS_FEATURES_UVDISCARD", 1); } catch { }
                    }
                }

                if (copy.HasProperty("_EnableUDIMDiscardOptions"))
                {
                    copy.SetFloat("_EnableUDIMDiscardOptions", 1f);
                    try { copy.SetInt("_EnableUDIMDiscardOptions", 1); } catch { }
                }

                if (copy.HasProperty("_UDIMDiscardMode"))
                {
                    copy.SetFloat("_UDIMDiscardMode", 0f);
                }

                if (copy.HasProperty("_UDIMDiscardUV"))
                {
                    copy.SetFloat("_UDIMDiscardUV", uvChannel);
                }
                else if (copy.HasProperty("_UDIMDiscardUVChannel"))
                {
                    copy.SetFloat("_UDIMDiscardUVChannel", uvChannel);
                }

                if (copy.HasProperty(tileProperty))
                {
                    copy.SetFloat(tileProperty, defaultHidden ? 1f : 0f);
                    copy.SetOverrideTag(tileProperty + "Animated", "1");
                }

                materials[i] = copy;
                configuredIndices.Add(i);
                EditorUtility.SetDirty(copy);
            }

            baseRenderer.sharedMaterials = materials;
            EditorUtility.SetDirty(baseRenderer);

            if (debugMode)
            {
                Debug.Log($"[Mesh Merger] Configured {configuredIndices.Count} attachment material(s) for UV discard tile ({row},{col})", context);
            }

            return configuredIndices;
        }
    }
}

