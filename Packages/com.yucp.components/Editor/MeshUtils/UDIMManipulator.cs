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
            // Common shader names: "Warren/FastFur", "WFFS", "FastFur", "Fast Fur"
            if (shaderNameLower.Contains("fastfur") || shaderNameLower.Contains("fast fur") || shaderNameLower.Contains("wffs") || shaderNameLower.Contains("warren"))
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
            
            if (shaderName.Contains("fastfur") || shaderName.Contains("fast fur") || shaderName.Contains("wffs") || shaderName.Contains("warren"))
                return "FastFur";
            
            return material.shader.name;
        }

        public static Mesh ApplyUDIMDiscard(
            Mesh originalMesh,
            bool[] hiddenVertices,
            AutoBodyHiderData data)
        {
            int targetUVChannel = GetEffectiveUVChannel(data, originalMesh);
            return ApplyUDIMDiscard(originalMesh, hiddenVertices, data.udimDiscardRow, data.udimDiscardColumn, targetUVChannel);
        }

        public static Mesh ApplyUDIMDiscard(
            Mesh originalMesh,
            bool[] hiddenVertices,
            int udimRow,
            int udimColumn,
            int targetUVChannel)
        {
            Mesh newMesh = UnityEngine.Object.Instantiate(originalMesh);
            newMesh.name = originalMesh.name + "_UDIMHidden";
            
            // Get source UV channel (usually UV0 for texture mapping)
            Vector2[] sourceUV = GetUVChannel(originalMesh, 0);
            
            if (sourceUV == null || sourceUV.Length == 0)
            {
                Debug.LogError($"[UDIMManipulator] No UV0 found on mesh.");
                return originalMesh;
            }
            
            // Get or create target UV channel
            Vector2[] targetUV = GetUVChannel(originalMesh, targetUVChannel);
            
            // If target channel doesn't exist, create it from UV0
            if (targetUV == null || targetUV.Length == 0)
            {
                targetUV = new Vector2[sourceUV.Length];
                Array.Copy(sourceUV, targetUV, sourceUV.Length);
                Debug.Log($"[UDIMManipulator] UV{targetUVChannel} not found, creating from UV0.");
            }
            else if (targetUV.Length != sourceUV.Length)
            {
                // Length mismatch - recreate from UV0
                targetUV = new Vector2[sourceUV.Length];
                Array.Copy(sourceUV, targetUV, sourceUV.Length);
                Debug.LogWarning($"[UDIMManipulator] UV{targetUVChannel} length mismatch, recreating from UV0.");
            }
            
            float uOffset = udimColumn;
            float vOffset = udimRow;
            
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
                Debug.LogError("[UDIMManipulator] Material is null.");
                return;
            }
            
            if (!IsPoiyomiWithUDIMSupport(material))
            {
                Debug.LogWarning($"[UDIMManipulator] Material {material.name} doesn't support UDIM discard.");
                return;
            }
            
            string shaderName = GetShaderDisplayName(material);
            string shaderNameLower = material.shader.name.ToLower();
            
            // Enable shader keywords and feature toggles FIRST (especially for FastFur)
            if (shaderNameLower.Contains("poiyomi"))
            {
                material.EnableKeyword("POI_UDIMDISCARD");
                // Validate and set main UDIM discard option
                if (material.HasProperty("_EnableUDIMDiscardOptions"))
                {
                    material.SetFloat("_EnableUDIMDiscardOptions", 1f);
                }
                else
                {
                    Debug.LogWarning($"[UDIMManipulator] Material '{material.name}' missing '_EnableUDIMDiscardOptions' property.");
                }
            }
            else if (shaderNameLower.Contains("fastfur") || shaderNameLower.Contains("fast fur") || shaderNameLower.Contains("wffs") || shaderNameLower.Contains("warren"))
            {
                // FastFur: Check and enable "Install UV Discard Module" toggle FIRST
                if (material.HasProperty("_WFFS_FEATURES_UVDISCARD"))
                {
                    // Check if module is already installed
                    float currentValue = material.GetFloat("_WFFS_FEATURES_UVDISCARD");
                    Debug.Log($"[UDIMManipulator] FastFur material '{material.name}' - _WFFS_FEATURES_UVDISCARD current value: {currentValue}");
                    
                    if (currentValue < 0.5f)
                    {
                        // Install the module by setting the property to 1
                        // Try both SetFloat and SetInt in case Unity stores it differently
                        material.SetFloat("_WFFS_FEATURES_UVDISCARD", 1f);
                        if (material.HasProperty("_WFFS_FEATURES_UVDISCARD"))
                        {
                            try { material.SetInt("_WFFS_FEATURES_UVDISCARD", 1); } catch { }
                        }
                        
                        // Also explicitly enable the keyword
                        material.EnableKeyword("WFFS_FEATURES_UVDISCARD");
                        
                        // Force update the material
                        UnityEditor.EditorUtility.SetDirty(material);
                        material.name = material.name; // Force Unity to recognize changes
                        
                        float newValue = material.GetFloat("_WFFS_FEATURES_UVDISCARD");
                        Debug.Log($"[UDIMManipulator] Installed UV Discard module on FastFur material '{material.name}' (was {currentValue}, now {newValue}, keyword enabled: {material.IsKeywordEnabled("WFFS_FEATURES_UVDISCARD")})");
                    }
                    else
                    {
                        // Module already installed, just ensure keyword is enabled
                        material.EnableKeyword("WFFS_FEATURES_UVDISCARD");
                        Debug.Log($"[UDIMManipulator] FastFur material '{material.name}' - UV Discard module already installed, ensuring keyword is enabled");
                    }
                }
                else
                {
                    Debug.LogWarning($"[UDIMManipulator] Material '{material.name}' missing '_WFFS_FEATURES_UVDISCARD' property. This shader may not support UV Discard.");
                }
                
                // Enable UDIM discard option (this property becomes available after module is installed)
                if (material.HasProperty("_EnableUDIMDiscardOptions"))
                {
                    // Try both SetFloat and SetInt since it's a ToggleUI (Int) but FastFur uses GetFloat
                    material.SetFloat("_EnableUDIMDiscardOptions", 1f);
                    try { material.SetInt("_EnableUDIMDiscardOptions", 1); } catch { }
                    
                    UnityEditor.EditorUtility.SetDirty(material);
                    material.name = material.name; // Force Unity to recognize changes
                    
                    float enabledValue = material.GetFloat("_EnableUDIMDiscardOptions");
                    Debug.Log($"[UDIMManipulator] Enabled UV Discard on FastFur material '{material.name}' - _EnableUDIMDiscardOptions value: {enabledValue}");
                }
                else
                {
                    Debug.LogWarning($"[UDIMManipulator] Material '{material.name}' missing '_EnableUDIMDiscardOptions' property after installing module. Module may not be properly installed.");
                }
            }
            
            // Set UDIM discard mode and UV channel
            if (material.HasProperty("_UDIMDiscardMode"))
            {
                material.SetFloat("_UDIMDiscardMode", 0f);  // Vertex mode
            }
            
            // Get effective UV channel (auto-detect if enabled)
            int effectiveUVChannel = GetEffectiveUVChannel(data, mesh);
            
            if (material.HasProperty("_UDIMDiscardUV"))
            {
                material.SetFloat("_UDIMDiscardUV", effectiveUVChannel);
            }
            else if (material.HasProperty("_UDIMDiscardUVChannel"))
            {
                // Alternative property name
                material.SetFloat("_UDIMDiscardUVChannel", effectiveUVChannel);
            }
            
            string propertyName = $"_UDIMDiscardRow{data.udimDiscardRow}_{data.udimDiscardColumn}";
            
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, 1f);
                string autoDetectNote = data.autoDetectUVChannel ? " (auto-detected)" : "";
                Debug.Log($"[UDIMManipulator] Configured {shaderName} material '{material.name}' with UDIM tile ({data.udimDiscardRow}, {data.udimDiscardColumn}) on UV channel {effectiveUVChannel}{autoDetectNote}");
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
        
        /// <summary>
        /// Automatically detect the best UV channel for UDIM discard.
        /// Strategy:
        /// 1. Prefer UV1 if it exists (common for discard operations, keeps UV0 for textures)
        /// 2. If UV1 doesn't exist, prefer UV0 (will create UV1 from it)
        /// 3. Check UV2/UV3 as fallback
        /// 4. Default to UV1 (will be created during processing)
        /// </summary>
        public static int DetectBestUVChannel(Mesh mesh)
        {
            if (mesh == null)
            {
                Debug.LogWarning("[UDIMManipulator] Cannot detect UV channel: mesh is null. Defaulting to UV1.");
                return 1;
            }
            
            List<Vector2> uvList = new List<Vector2>();
            
            // Strategy 1: Check if UV1 exists and has proper length
            mesh.GetUVs(1, uvList);
            if (uvList != null && uvList.Count > 0)
            {
                // Verify it has the same vertex count as UV0
                List<Vector2> uv0Check = new List<Vector2>();
                mesh.GetUVs(0, uv0Check);
                if (uv0Check.Count == uvList.Count)
                {
                    // UV1 exists and matches UV0 - perfect for discard operations
                    return 1;
                }
                else if (uv0Check.Count > 0)
                {
                    // UV1 exists but length mismatch - will recreate during processing
                    return 1;
                }
            }
            
            // Strategy 2: UV0 exists - use it as source, will write to UV1
            uvList.Clear();
            mesh.GetUVs(0, uvList);
            if (uvList != null && uvList.Count > 0)
            {
                // UV1 doesn't exist, will create from UV0 during processing
                return 1;
            }
            
            // Strategy 3: Check UV2 and UV3 as last resort
            for (int channel = 2; channel <= 3; channel++)
            {
                uvList.Clear();
                mesh.GetUVs(channel, uvList);
                if (uvList != null && uvList.Count > 0)
                {
                    // Unusual case - UV0 and UV1 not available
                    return channel;
                }
            }
            
            // Strategy 4: No UVs found - default to UV1 (will be created)
            return 1;
        }
        
        /// <summary>
        /// Get the effective UV channel to use, considering auto-detection.
        /// </summary>
        public static int GetEffectiveUVChannel(AutoBodyHiderData data, Mesh mesh)
        {
            if (data.autoDetectUVChannel)
            {
                return DetectBestUVChannel(mesh);
            }
            else
            {
                return data.udimUVChannel;
            }
        }
    }
}

