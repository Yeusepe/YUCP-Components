using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    public static class UDIMManipulator
    {
        /// <summary>
        /// Check if material supports UDIM discard (Poiyomi or FastFur).
        /// Both shaders use the same property naming convention.
        /// </summary>
        public static bool IsPoiyomiWithUDIMSupport(Material material)
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
            // Common shader names: "Warren/FastFur", "WFFS", "FastFur"
            if (shaderNameLower.Contains("fastfur") || shaderNameLower.Contains("wffs") || shaderNameLower.Contains("warren"))
            {
                // FastFur requires the UV Discard module to be installed (enabled)
                // Check for either the main property or the feature toggle
                bool hasUDIMProperty = material.HasProperty("_EnableUDIMDiscardOptions");
                bool hasFeatureToggle = material.HasProperty("_WFFS_FEATURES_UVDISCARD");
                
                return hasUDIMProperty || hasFeatureToggle;
            }
            
            // Unknown shader
            return false;
        }
        
        /// <summary>
        /// Get the shader name for logging/display purposes.
        /// </summary>
        public static string GetShaderDisplayName(Material material)
        {
            if (material == null || material.shader == null)
                return "Unknown";
            
            string shaderName = material.shader.name.ToLower();
            
            if (shaderName.Contains("poiyomi"))
                return "Poiyomi";
            
            if (shaderName.Contains("fastfur") || shaderName.Contains("wffs") || shaderName.Contains("warren"))
                return "FastFur";
            
            return material.shader.name;
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
            
            string shaderName = GetShaderDisplayName(material);
            string shaderNameLower = material.shader.name.ToLower();
            
            material.SetFloat("_EnableUDIMDiscardOptions", 1f);
            
            // Enable shader keywords - different for each shader
            if (shaderNameLower.Contains("poiyomi"))
            {
                material.EnableKeyword("POI_UDIMDISCARD");
            }
            else if (shaderNameLower.Contains("fastfur") || shaderNameLower.Contains("wffs"))
            {
                material.EnableKeyword("WFFS_FEATURES_UVDISCARD");
                // Also need to enable the toggle property for FastFur
                if (material.HasProperty("_WFFS_FEATURES_UVDISCARD"))
                {
                    material.SetFloat("_WFFS_FEATURES_UVDISCARD", 1f);
                }
            }
            
            material.SetFloat("_UDIMDiscardMode", 0f);  // Vertex mode
            material.SetFloat("_UDIMDiscardUV", 1);     // Use UV1
            
            string propertyName = $"_UDIMDiscardRow{data.udimDiscardRow}_{data.udimDiscardColumn}";
            
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, 1f);
                Debug.Log($"[UDIMManipulator] Configured {shaderName} material '{material.name}' with UDIM tile ({data.udimDiscardRow}, {data.udimDiscardColumn})");
            }
            else
            {
                Debug.LogWarning($"[UDIMManipulator] Property {propertyName} not found on {shaderName} material '{material.name}'.");
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

