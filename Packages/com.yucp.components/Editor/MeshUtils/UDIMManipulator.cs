using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    public static class UDIMManipulator
    {
        public static bool IsPoiyomiWithUDIMSupport(Material material)
        {
            if (material == null || material.shader == null)
                return false;
            
            string shaderName = material.shader.name.ToLower();
            
            if (!shaderName.Contains("poiyomi"))
                return false;
            
            return material.HasProperty("_EnableUDIMDiscardOptions");
        }

        public static Mesh ApplyUDIMDiscard(
            Mesh originalMesh,
            bool[] hiddenVertices,
            AutoBodyHiderData data)
        {
            return ApplyUDIMDiscard(originalMesh, hiddenVertices, data.udimDiscardRow, data.udimDiscardColumn);
        }

        public static Mesh ApplyUDIMDiscard(
            Mesh originalMesh,
            bool[] hiddenVertices,
            int udimRow,
            int udimColumn)
        {
            Mesh newMesh = UnityEngine.Object.Instantiate(originalMesh);
            newMesh.name = originalMesh.name + "_UDIMHidden";
            
            Vector2[] uv0 = GetUVChannel(originalMesh, 0);
            
            if (uv0 == null || uv0.Length == 0)
            {
                Debug.LogError($"[UDIMManipulator] No UV0 found on mesh.");
                return originalMesh;
            }
            
            // Create UV1 from UV0 to separate texture mapping from discard logic
            Vector2[] uv1 = new Vector2[uv0.Length];
            Array.Copy(uv0, uv1, uv0.Length);
            
            float uOffset = udimColumn;
            float vOffset = udimRow;
            
            for (int i = 0; i < hiddenVertices.Length && i < uv1.Length; i++)
            {
                if (hiddenVertices[i])
                {
                    uv1[i] = new Vector2(uv1[i].x + uOffset, uv1[i].y + vOffset);
                }
            }
            
            SetUVChannel(newMesh, 1, uv1);
            
            return newMesh;
        }

        public static void ConfigurePoiyomiMaterial(
            Material material,
            AutoBodyHiderData data)
        {
            if (!IsPoiyomiWithUDIMSupport(material))
            {
                Debug.LogWarning($"[UDIMManipulator] Material {material.name} doesn't support UDIM discard.");
                return;
            }
            
            material.SetFloat("_EnableUDIMDiscardOptions", 1f);
            material.EnableKeyword("POI_UDIMDISCARD");
            material.SetFloat("_UDIMDiscardMode", 0f);
            material.SetFloat("_UDIMDiscardUV", 1);
            
            string propertyName = $"_UDIMDiscardRow{data.udimDiscardRow}_{data.udimDiscardColumn}";
            
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, 1f);
            }
            else
            {
                Debug.LogWarning($"[UDIMManipulator] Property {propertyName} not found on material.");
            }
            
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
                    Debug.LogError($"[UDIMManipulator] Invalid UV channel: {channel}");
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
                    Debug.LogError($"[UDIMManipulator] Invalid UV channel: {channel}");
                    break;
            }
        }
    }
}

