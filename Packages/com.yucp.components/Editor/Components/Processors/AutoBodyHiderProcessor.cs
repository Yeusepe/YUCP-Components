using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
using com.vrcfury.api;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using YUCP.Components.Editor.UI;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Processes Auto Body Hider components during avatar build.
    /// Detects which body vertices are hidden by clothing and either moves their UVs to a discard tile (Poiyomi)
    /// or physically deletes them from the mesh (other shaders).
    /// Supports GPU-accelerated detection with multiple algorithms (Raycast, Proximity, Hybrid, Smart, Manual).
    /// </summary>
    public class AutoBodyHiderProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 100;

        private class BodyMeshProcessing
        {
            public SkinnedMeshRenderer bodyMesh;
            public Mesh originalMesh;
            public List<AutoBodyHiderData> udimComponents = new List<AutoBodyHiderData>();
            public List<AutoBodyHiderData> deletionComponents = new List<AutoBodyHiderData>();
            public Dictionary<AutoBodyHiderData, bool[]> hiddenVerticesMap = new Dictionary<AutoBodyHiderData, bool[]>();
        }

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var dataList = avatarRoot.GetComponentsInChildren<AutoBodyHiderData>(true);
            
            // Group components by body mesh and application mode
            Dictionary<SkinnedMeshRenderer, BodyMeshProcessing> bodyMeshes = new Dictionary<SkinnedMeshRenderer, BodyMeshProcessing>();
            
            foreach (var data in dataList)
            {
                if (data.debugMode)
                {
                    Debug.Log($"[AutoBodyHiderProcessor] Processing '{data.name}'", data);
                }

                // Validate required references
                if (!ValidateData(data))
                {
                    continue;
                }

                try
                {
                    // Get or create body mesh processing group
                    if (!bodyMeshes.ContainsKey(data.targetBodyMesh))
                    {
                        bodyMeshes[data.targetBodyMesh] = new BodyMeshProcessing
                        {
                            bodyMesh = data.targetBodyMesh,
                            originalMesh = data.targetBodyMesh.sharedMesh
                        };
                    }
                    
                    var group = bodyMeshes[data.targetBodyMesh];
                    ApplicationMode mode = DetermineApplicationMode(data);
                    
                    if (mode == ApplicationMode.UDIMDiscard)
                    {
                        group.udimComponents.Add(data);
                    }
                    else
                    {
                        group.deletionComponents.Add(data);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[AutoBodyHiderProcessor] Error processing '{data.name}': {ex.Message}", data);
                    Debug.LogException(ex);
                }
            }
            
            // Process each body mesh group
            foreach (var group in bodyMeshes.Values)
            {
                ProcessBodyMeshGroup(group);
            }

            return true;
        }

        private bool ValidateData(AutoBodyHiderData data)
        {
            if (data.targetBodyMesh == null)
            {
                Debug.LogError("[AutoBodyHiderProcessor] No target body mesh specified.", data);
                return false;
            }

            if (data.targetBodyMesh.sharedMesh == null)
            {
                Debug.LogError("[AutoBodyHiderProcessor] Target body mesh has no mesh data.", data);
                return false;
            }

            // Validate detection method requirements
            if (data.detectionMethod != DetectionMethod.Manual)
            {
                bool hasValidClothingMesh = false;
                if (data.clothingMeshes != null && data.clothingMeshes.Length > 0)
                {
                    foreach (var mesh in data.clothingMeshes)
                    {
                        if (mesh != null && mesh.sharedMesh != null)
                        {
                            hasValidClothingMesh = true;
                            break;
                        }
                    }
                }
                
                if (!hasValidClothingMesh)
                {
                    Debug.LogError("[AutoBodyHiderProcessor] Automatic detection requires at least one valid clothing mesh reference.", data);
                    return false;
                }
            }

            if (data.detectionMethod == DetectionMethod.Manual && data.manualMask == null)
            {
                Debug.LogError("[AutoBodyHiderProcessor] Manual detection requires a mask texture.", data);
                return false;
            }

            return true;
        }
        
        private void ProcessBodyMeshGroup(BodyMeshProcessing group)
        {
            // Process UDIM components (can be combined)
            if (group.udimComponents.Count > 0)
            {
                ProcessUDIMGroup(group);
            }
            
            // Process deletion components sequentially
            if (group.deletionComponents.Count > 0)
            {
                ProcessDeletionGroup(group);
            }
        }
        
        private void ProcessUDIMGroup(BodyMeshProcessing group)
        {
            Debug.Log($"[AutoBodyHiderProcessor] Processing {group.udimComponents.Count} UDIM discard components for '{group.bodyMesh.name}'");
            
            // Check if any pieces were skipped due to tile limit
            int skippedCount = 0;
            foreach (var data in group.udimComponents)
            {
                // Check if this data has a valid tile assigned (row and column are 0-3)
                if (data.udimDiscardRow < 0 || data.udimDiscardRow > 3 || 
                    data.udimDiscardColumn < 0 || data.udimDiscardColumn > 3)
                {
                    skippedCount++;
                    Debug.LogWarning($"[AutoBodyHiderProcessor] Skipping '{data.name}' - no valid UDIM tile assigned (tile limit exceeded)", data);
                    data.SetBuildStats(0, "Skipped (Tile Limit)");
                }
            }
            
            // Filter out skipped components
            List<AutoBodyHiderData> validComponents = new List<AutoBodyHiderData>();
            foreach (var data in group.udimComponents)
            {
                if (data.udimDiscardRow >= 0 && data.udimDiscardRow <= 3 && 
                    data.udimDiscardColumn >= 0 && data.udimDiscardColumn <= 3)
                {
                    validComponents.Add(data);
                }
            }
            
            if (validComponents.Count == 0)
            {
                Debug.LogWarning($"[AutoBodyHiderProcessor] No valid UDIM components to process for '{group.bodyMesh.name}'");
                return;
            }
            
            Debug.Log($"[AutoBodyHiderProcessor] Processing {validComponents.Count} valid UDIM components (skipped {skippedCount})");
            
            Mesh originalMesh = group.originalMesh;
            Vector3[] vertices = originalMesh.vertices;
            
            // Determine if progress window should be shown
            YUCPProgressWindow progressWindow = null;
            bool showProgress = validComponents.Count > 1 || 
                               (validComponents.Count > 0 && ShouldShowProgress(validComponents[0], vertices.Length));
            
            if (showProgress)
            {
                progressWindow = YUCPProgressWindow.Create();
                progressWindow.Progress(0, $"Processing {validComponents.Count} clothing pieces...");
            }
            
            try
            {
                int totalSteps = validComponents.Count + 2; // Detection per piece + merge + configure
                int currentStep = 0;
                
                // Detect hidden vertices for each clothing piece (unsorted initially)
                Dictionary<AutoBodyHiderData, int> coverageMap = new Dictionary<AutoBodyHiderData, int>();
                
                foreach (var data in validComponents)
                {
                    if (progressWindow != null)
                    {
                        float progress = (float)currentStep / totalSteps;
                        progressWindow.Progress(progress, $"Detecting hidden vertices for '{data.name}'...");
                    }
                    
                    bool[] hiddenVertices = DetectHiddenVerticesForData(data, originalMesh);
                    
                    if (hiddenVertices != null)
                    {
                        // Apply symmetry and safety margin
                        int hiddenCount = 0;
                        foreach (bool h in hiddenVertices) if (h) hiddenCount++;
                        
                        hiddenVertices = ApplyPostProcessing(data, vertices, hiddenVertices, ref hiddenCount);
                        
                        group.hiddenVerticesMap[data] = hiddenVertices;
                        coverageMap[data] = hiddenCount;
                        data.SetBuildStats(hiddenCount, "UDIM Discard (Multi)");
                    }
                    
                    currentStep++;
                }
                
                // Check if any component has tile usage enabled
                bool anyOptimizationEnabled = validComponents.Any(c => c.optimizeTileUsage);
                
                if (anyOptimizationEnabled)
                {
                    // Sort by coverage (descending) - process largest first
                    validComponents = validComponents
                        .OrderByDescending(c => coverageMap.ContainsKey(c) ? coverageMap[c] : 0)
                        .ToList();
                    
                    Debug.Log($"[AutoBodyHiderProcessor] Optimization enabled - sorted {validComponents.Count} pieces by coverage:");
                    foreach (var data in validComponents)
                    {
                        int coverage = coverageMap.ContainsKey(data) ? coverageMap[data] : 0;
                        Debug.Log($"  • '{data.name}': {coverage} vertices");
                    }
                }
                
                // Detect ACTUAL overlaps now that we have hidden vertex data
                if (validComponents.Count >= 2)
                {
                    if (progressWindow != null)
                    {
                        progressWindow.Progress((float)currentStep / totalSteps, "Detecting actual overlaps between clothing pieces...");
                    }
                    DetectAndAssignActualOverlaps(group);
                }
                
                // Merge all UDIM modifications into one mesh
                if (progressWindow != null)
                {
                    float progress = (float)currentStep / totalSteps;
                    progressWindow.Progress(progress, "Merging UDIM discards into body mesh...");
                }
                
                Mesh modifiedMesh = MergeUDIMDiscards(group);
                group.bodyMesh.sharedMesh = modifiedMesh;
                currentStep++;
                
                // Configure materials with all needed UDIM tiles
                if (progressWindow != null)
                {
                    float progress = (float)currentStep / totalSteps;
                    progressWindow.Progress(progress, "Configuring material UDIM tiles...");
                }
                
                ConfigureMaterialsForMultipleUDIM(group);
                currentStep++;
                
                // Create toggles or integrate with existing toggles for each valid clothing piece
                if (progressWindow != null)
                {
                    progressWindow.Progress(1.0f, "Creating toggles...");
                }
                
                foreach (var data in validComponents)
                {
                    // Handle both creating new toggles and using existing toggles
                    if (data.createToggle || data.useExistingToggle)
                    {
                        CreateUDIMToggleForComponent(data, group);
                    }
                }
                
                if (progressWindow != null)
                {
                    progressWindow.Progress(1.0f, "UDIM discard processing complete!");
                }
            }
            finally
            {
                if (progressWindow != null)
                {
                    progressWindow.CloseWindow();
                }
            }
        }
        
        private void ProcessDeletionGroup(BodyMeshProcessing group)
        {
            Debug.Log($"[AutoBodyHiderProcessor] Processing {group.deletionComponents.Count} mesh deletion components for '{group.bodyMesh.name}'");
            
            // Mesh deletion applied sequentially
            Mesh currentMesh = group.bodyMesh.sharedMesh;
            
            foreach (var data in group.deletionComponents)
            {
                ProcessBodyHider(data);
                // Processor modifies the mesh, continue to next component
            }
        }

        private void ProcessBodyHider(AutoBodyHiderData data)
        {
            Mesh bodyMesh = data.targetBodyMesh.sharedMesh;
            Vector3[] vertices = bodyMesh.vertices;
            
            // Get first clothing mesh for cache compatibility (hash generation uses all meshes)
            var clothingMeshes = data.GetClothingMeshes();
            Mesh clothingMesh = (clothingMeshes != null && clothingMeshes.Length > 0) ? clothingMeshes[0].sharedMesh : null;
            
            YUCPProgressWindow progressWindow = null;
            if (ShouldShowProgress(data, vertices.Length))
            {
                progressWindow = YUCPProgressWindow.Create();
            }
            
            try
            {
                bool[] hiddenVertices;
                int hiddenCount;
                
                if (DetectionCache.TryGetCachedResult(data, bodyMesh, clothingMesh, out hiddenVertices, out hiddenCount))
                {
                    if (data.debugMode)
                    {
                        Debug.Log($"[AutoBodyHiderProcessor] Using cached result: {hiddenCount} hidden vertices");
                    }
                    if (progressWindow != null)
                    {
                        progressWindow.Progress(0.5f, "Using cached detection result...");
                    }
                }
                else
                {
                    Vector3[] normals = bodyMesh.normals;
                    List<Vector2> uv0List = new List<Vector2>();
                    bodyMesh.GetUVs(0, uv0List);
                    Vector2[] uv0 = uv0List.ToArray();

                    if (data.debugMode)
                    {
                        Debug.Log($"[AutoBodyHiderProcessor] Body mesh has {vertices.Length} vertices");
                    }

                    if (progressWindow != null)
                    {
                        progressWindow.Progress(0, "Starting vertex detection...");
                    }

                    var startTime = System.Diagnostics.Stopwatch.StartNew();
                    hiddenVertices = VertexDetection.DetectHiddenVertices(data, vertices, normals, uv0, progressWindow);
                    startTime.Stop();
                    
                    if (progressWindow != null)
                    {
                        progressWindow.Progress(1.0f, "Detection complete!");
                    }

                    hiddenCount = 0;
                    foreach (bool hidden in hiddenVertices)
                    {
                        if (hidden) hiddenCount++;
                    }

                    if (data.debugMode)
                    {
                        Debug.Log($"[AutoBodyHiderProcessor] Detected {hiddenCount} hidden vertices ({(hiddenCount * 100f / vertices.Length):F1}%) in {startTime.ElapsedMilliseconds}ms");
                    }
                    
                    DetectionCache.CacheResult(data, bodyMesh, clothingMesh, hiddenVertices, hiddenCount);
                }
                
                ProcessHiddenVertices(data, bodyMesh, vertices, hiddenVertices, hiddenCount);
            }
            finally
            {
                if (progressWindow != null)
                {
                    progressWindow.CloseWindow();
                }
            }
        }
        
        private void ProcessHiddenVertices(AutoBodyHiderData data, Mesh bodyMesh, Vector3[] vertices, bool[] hiddenVertices, int hiddenCount)
        {
            if (hiddenCount == 0)
            {
                Debug.LogWarning("[AutoBodyHiderProcessor] No hidden vertices detected. Check your settings.", data);
                data.SetBuildStats(0, "None");
                return;
            }

            Debug.Log($"[AutoBodyHider DEBUG] Initial hidden vertices: {hiddenCount}", data);

            if (data.mirrorSymmetry)
            {
                hiddenVertices = ApplySymmetryMirror(hiddenVertices, vertices);
                
                int countAfterMirror = 0;
                foreach (bool hidden in hiddenVertices)
                {
                    if (hidden) countAfterMirror++;
                }
                Debug.Log($"[AutoBodyHider DEBUG] After symmetry mirror: {countAfterMirror} hidden vertices", data);
                hiddenCount = countAfterMirror;
            }
            
            if (data.safetyMargin > 0.0001f)
            {
                hiddenVertices = ApplySafetyMargin(hiddenVertices, vertices, data);
                
                hiddenCount = 0;
                foreach (bool hidden in hiddenVertices)
                {
                    if (hidden) hiddenCount++;
                }
                
                Debug.Log($"[AutoBodyHider DEBUG] After safety margin: {hiddenCount} hidden vertices", data);
            }

            ApplicationMode mode = DetermineApplicationMode(data);

            if (mode == ApplicationMode.UDIMDiscard)
            {
                ApplyUDIMDiscardMode(data, bodyMesh, hiddenVertices, hiddenCount);
            }
            else
            {
                ApplyMeshDeletionMode(data, bodyMesh, hiddenVertices, hiddenCount);
            }
        }

        private ApplicationMode DetermineApplicationMode(AutoBodyHiderData data)
        {
            Material[] materials = data.targetBodyMesh.sharedMaterials;
            
            // If user specified target materials, check those first
            if (data.targetMaterials != null && data.targetMaterials.Length > 0)
            {
                var validTargetMaterials = data.targetMaterials.Where(m => m != null).ToArray();
                if (validTargetMaterials.Length > 0)
                {
                    Debug.Log($"[AutoBodyHiderProcessor] Using {validTargetMaterials.Length} user-specified target material(s)");
                    
                    if (data.applicationMode == ApplicationMode.UDIMDiscard)
                    {
                        // Check if any target material is compatible
                        bool hasCompatible = validTargetMaterials.Any(m => UDIMManipulator.IsPoiyomiWithUDIMSupport(m));
                        if (hasCompatible)
                        {
                            return ApplicationMode.UDIMDiscard;
                        }
                        else
                        {
                            Debug.LogError($"[AutoBodyHiderProcessor] UDIM Discard mode selected but none of the target materials have compatible shaders (Poiyomi/FastFur). Falling back to Mesh Deletion.", data);
                            return ApplicationMode.MeshDeletion;
                        }
                    }
                    else if (data.applicationMode == ApplicationMode.MeshDeletion)
                    {
                        return ApplicationMode.MeshDeletion;
                    }
                    else
                    {
                        // Auto-detect on specified materials
                        bool hasCompatible = validTargetMaterials.Any(m => UDIMManipulator.IsPoiyomiWithUDIMSupport(m));
                        if (hasCompatible)
                        {
                            var compatibleMaterials = validTargetMaterials.Where(m => UDIMManipulator.IsPoiyomiWithUDIMSupport(m)).ToArray();
                            string shaderName = compatibleMaterials.Length > 0 ? UDIMManipulator.GetShaderDisplayName(compatibleMaterials[0]) : "Poiyomi/FastFur";
                            Debug.Log($"[AutoBodyHiderProcessor] Auto-detected {shaderName} shader with UDIM support on {compatibleMaterials.Length} target material(s), using UDIM discard mode");
                            return ApplicationMode.UDIMDiscard;
                        }
                    }
                }
            }
            
            // Log all materials on the body mesh for debugging
            Debug.Log($"[AutoBodyHiderProcessor] Body mesh '{data.targetBodyMesh.name}' has {materials.Length} materials:");
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && materials[i].shader != null)
                {
                    Debug.Log($"[AutoBodyHiderProcessor]   [{i}] '{materials[i].name}' - Shader: '{materials[i].shader.name}'");
                }
                else
                {
                    Debug.Log($"[AutoBodyHiderProcessor]   [{i}] NULL or missing shader");
                }
            }
            
            // Check if user forced a specific mode
            if (data.applicationMode == ApplicationMode.UDIMDiscard)
            {
                // User specifically requested UDIM mode - validate it's possible
                bool hasCompatibleShader = false;
                foreach (var material in materials)
                {
                    if (UDIMManipulator.IsPoiyomiWithUDIMSupport(material))
                    {
                        hasCompatibleShader = true;
                        break;
                    }
                }
                
                if (!hasCompatibleShader)
                {
                    Debug.LogError($"[AutoBodyHiderProcessor] UDIM Discard mode selected but body mesh '{data.targetBodyMesh.name}' has no compatible shader (Poiyomi/FastFur). Falling back to Mesh Deletion.", data);
                    return ApplicationMode.MeshDeletion;
                }
                
                return ApplicationMode.UDIMDiscard;
            }
            else if (data.applicationMode == ApplicationMode.MeshDeletion)
            {
                return ApplicationMode.MeshDeletion;
            }
            
            // Auto-detect mode
            foreach (var material in materials)
            {
                if (UDIMManipulator.IsPoiyomiWithUDIMSupport(material))
                {
                    string shaderName = UDIMManipulator.GetShaderDisplayName(material);
                    Debug.Log($"[AutoBodyHiderProcessor] Auto-detected {shaderName} shader with UDIM support on '{material.name}', using UDIM discard mode");
                    return ApplicationMode.UDIMDiscard;
                }
            }

            Debug.Log($"[AutoBodyHiderProcessor] No UDIM-compatible shader found on body mesh, using mesh deletion mode");
            return ApplicationMode.MeshDeletion;
        }

        private void ApplyUDIMDiscardMode(AutoBodyHiderData data, Mesh originalMesh, bool[] hiddenVertices, int hiddenCount)
        {
            Mesh modifiedMesh = UDIMManipulator.ApplyUDIMDiscard(originalMesh, hiddenVertices, data);
            
            data.targetBodyMesh.sharedMesh = modifiedMesh;
            
            Material[] materials = data.targetBodyMesh.sharedMaterials;
            List<Material> configuredMaterials = new List<Material>();
            
            // If user specified target materials, configure those
            if (data.targetMaterials != null && data.targetMaterials.Length > 0)
            {
                var validTargetMaterials = data.targetMaterials.Where(m => m != null).ToArray();
                
                foreach (var targetMaterial in validTargetMaterials)
                {
                    // Find the material in the materials array - match by reference or shader name
                    for (int i = 0; i < materials.Length; i++)
                    {
                        bool isMatchingMaterial = materials[i] == targetMaterial;
                        if (!isMatchingMaterial && materials[i] != null && targetMaterial != null)
                        {
                            // Also check by shader name in case material instances differ
                            string meshShaderName = materials[i].shader != null ? materials[i].shader.name.ToLower() : "";
                            string targetShaderName = targetMaterial.shader != null ? targetMaterial.shader.name.ToLower() : "";
                            if (meshShaderName == targetShaderName && !string.IsNullOrEmpty(meshShaderName))
                            {
                                isMatchingMaterial = true;
                            }
                        }
                        
                        if (isMatchingMaterial && UDIMManipulator.IsPoiyomiWithUDIMSupport(materials[i]))
                        {
                            Material materialCopy = UnityEngine.Object.Instantiate(materials[i]);
                            
                            // Configure for toggle if creating toggle OR using existing toggle
                            bool shouldConfigureForToggle = data.createToggle || data.useExistingToggle;
                            
                            if (!shouldConfigureForToggle)
                            {
                                UDIMManipulator.ConfigurePoiyomiMaterial(materialCopy, data, originalMesh);
                            }
                            else
                            {
                                ConfigurePoiyomiForToggle(materialCopy, data, originalMesh);
                            }
                            
                            materials[i] = materialCopy;
                            configuredMaterials.Add(materialCopy);
                        }
                    }
                }
                
                if (configuredMaterials.Count == 0)
                {
                    Debug.LogWarning($"[AutoBodyHiderProcessor] None of the {validTargetMaterials.Length} target material(s) were found or compatible. Auto-detecting instead.", data);
                }
                else
                {
                    Debug.Log($"[AutoBodyHiderProcessor] Configured {configuredMaterials.Count} target material(s) for UDIM discard.", data);
                }
            }
            
            // If no target materials specified or none found, auto-detect all compatible materials
            if (configuredMaterials.Count == 0)
            {
                foreach (var material in materials)
                {
                    if (UDIMManipulator.IsPoiyomiWithUDIMSupport(material))
                    {
                        Material materialCopy = UnityEngine.Object.Instantiate(material);
                        
                        // Configure for toggle if creating toggle OR using existing toggle
                        bool shouldConfigureForToggle = data.createToggle || data.useExistingToggle;
                        
                        if (!shouldConfigureForToggle)
                        {
                            UDIMManipulator.ConfigurePoiyomiMaterial(materialCopy, data, originalMesh);
                        }
                        else
                        {
                            ConfigurePoiyomiForToggle(materialCopy, data, originalMesh);
                        }
                        
                        for (int i = 0; i < materials.Length; i++)
                        {
                            if (materials[i] == material)
                            {
                                materials[i] = materialCopy;
                            }
                        }
                        
                        configuredMaterials.Add(materialCopy);
                    }
                }
                
                if (configuredMaterials.Count > 0)
                {
                    Debug.Log($"[AutoBodyHiderProcessor] Auto-detected and configured {configuredMaterials.Count} compatible material(s) for UDIM discard.", data);
                }
            }
            
            data.targetBodyMesh.sharedMaterials = materials;
            
            // Create toggle or integrate with existing VRCFury toggle
            // Use the first configured material for toggle integration
            Material materialForToggle = configuredMaterials.Count > 0 ? configuredMaterials[0] : null;
            
            if (materialForToggle != null)
            {
                Component toggleToUse = null;
                
                // Check if user selected a specific toggle
                if (data.useExistingToggle && data.selectedToggle != null)
                {
                    toggleToUse = data.selectedToggle;
                    Debug.Log($"[AutoBodyHiderProcessor] Using selected toggle: {data.selectedToggle.name} on {GetGameObjectPath(data.selectedToggle.gameObject, data.gameObject)}", data);
                }
                else if (data.useExistingToggle)
                {
                    // Auto-detect first VRCFury toggle from GameObject or children
                    toggleToUse = FindVRCFuryToggleInHierarchy(data.gameObject);
                    if (toggleToUse != null)
                    {
                        Debug.Log($"[AutoBodyHiderProcessor] Auto-detected toggle: {toggleToUse.name} on {GetGameObjectPath(toggleToUse.gameObject, data.gameObject)}", data);
                    }
                }
                else
                {
                    // Find VRCFury component on this GameObject
                    var components = data.gameObject.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        if (comp != null && comp.GetType().Name == "VRCFury")
                        {
                            toggleToUse = comp;
                            break;
                        }
                    }
                }
                
                if (data.createToggle && toggleToUse == null)
                {
                    // Create our own toggle
                    CreateUDIMToggle(data, materialForToggle);
                }
                else if (toggleToUse != null)
                {
                    // Integrate with existing VRCFury toggle
                    IntegrateWithVRCFuryToggle(data, materialForToggle, toggleToUse);
                }
            }
            
            data.SetBuildStats(hiddenCount, "UDIM Discard");
            
            if (data.debugMode)
            {
                Debug.Log($"[AutoBodyHiderProcessor] Applied UDIM discard mode. Hidden {hiddenCount} vertices.", data);
            }
        }

        private void ApplyMeshDeletionMode(AutoBodyHiderData data, Mesh originalMesh, bool[] hiddenVertices, int hiddenCount)
        {
            Mesh modifiedMesh = MeshDeleter.DeleteHiddenVertices(originalMesh, hiddenVertices);
            
            data.targetBodyMesh.sharedMesh = modifiedMesh;
            
            data.SetBuildStats(hiddenCount, "Mesh Deletion");
            
            if (data.debugMode)
            {
                Debug.Log($"[AutoBodyHiderProcessor] Applied mesh deletion mode. Removed {hiddenCount} vertices.", data);
                Debug.Log($"[AutoBodyHiderProcessor] New mesh has {modifiedMesh.vertexCount} vertices (was {originalMesh.vertexCount})");
            }
        }

        private bool[] ApplySymmetryMirror(bool[] hiddenVertices, Vector3[] vertices)
        {
            bool[] result = new bool[hiddenVertices.Length];
            System.Array.Copy(hiddenVertices, result, hiddenVertices.Length);
            
            for (int i = 0; i < hiddenVertices.Length; i++)
            {
                if (hiddenVertices[i])
                {
                    Vector3 targetPos = new Vector3(-vertices[i].x, vertices[i].y, vertices[i].z);
                    
                    float minDistance = float.MaxValue;
                    int closestIndex = -1;
                    
                    for (int j = 0; j < vertices.Length; j++)
                    {
                        float dist = Vector3.Distance(vertices[j], targetPos);
                        if (dist < minDistance && dist < 0.001f)
                        {
                            minDistance = dist;
                            closestIndex = j;
                        }
                    }
                    
                    if (closestIndex >= 0)
                    {
                        result[closestIndex] = true;
                    }
                }
            }
            
            return result;
        }

        private bool[] ApplySafetyMargin(bool[] hiddenVertices, Vector3[] vertices, AutoBodyHiderData data)
        {
            bool[] shrunk = new bool[hiddenVertices.Length];
            System.Array.Copy(hiddenVertices, shrunk, hiddenVertices.Length);
            
            Vector3[] worldVertices = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                worldVertices[i] = data.targetBodyMesh.transform.TransformPoint(vertices[i]);
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                if (hiddenVertices[i])
                {
                    bool isNearEdge = false;
                    
                    for (int j = 0; j < vertices.Length; j++)
                    {
                        if (!hiddenVertices[j])
                        {
                            float dist = Vector3.Distance(worldVertices[i], worldVertices[j]);
                            if (dist < data.safetyMargin)
                            {
                                isNearEdge = true;
                                break;
                            }
                        }
                    }
                    
                    if (isNearEdge)
                    {
                        shrunk[i] = false;
                    }
                }
            }
            
            return shrunk;
        }

        private bool ShouldShowProgress(AutoBodyHiderData data, int vertexCount)
        {
            switch (data.detectionMethod)
            {
                case DetectionMethod.Smart:
                    return true;
                case DetectionMethod.Raycast:
                case DetectionMethod.Hybrid:
                    return vertexCount > 5000;
                default:
                    return false;
            }
        }

        private void ConfigurePoiyomiForToggle(Material material, AutoBodyHiderData data, Mesh mesh = null)
        {
            string shaderNameLower = material.shader.name.ToLower();
            
            // Enable shader keywords and feature toggles FIRST (especially for FastFur)
            if (shaderNameLower.Contains("poiyomi"))
            {
                material.EnableKeyword("POI_UDIMDISCARD");
                if (material.HasProperty("_EnableUDIMDiscardOptions"))
                {
                    material.SetFloat("_EnableUDIMDiscardOptions", 1f);
                }
            }
            else if (shaderNameLower.Contains("fastfur") || shaderNameLower.Contains("wffs"))
            {
                // FastFur: Check and enable "Install UV Discard Module" toggle FIRST
                if (material.HasProperty("_WFFS_FEATURES_UVDISCARD"))
                {
                    // Check if module is already installed
                    float currentValue = material.GetFloat("_WFFS_FEATURES_UVDISCARD");
                    Debug.Log($"[AutoBodyHiderProcessor] FastFur material '{material.name}' - _WFFS_FEATURES_UVDISCARD current value: {currentValue}");
                    
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
                        Debug.Log($"[AutoBodyHiderProcessor] Installed UV Discard module on FastFur material '{material.name}' (was {currentValue}, now {newValue}, keyword enabled: {material.IsKeywordEnabled("WFFS_FEATURES_UVDISCARD")})");
                    }
                    else
                    {
                        // Module already installed, just ensure keyword is enabled
                        material.EnableKeyword("WFFS_FEATURES_UVDISCARD");
                        Debug.Log($"[AutoBodyHiderProcessor] FastFur material '{material.name}' - UV Discard module already installed, ensuring keyword is enabled");
                    }
                }
                else
                {
                    Debug.LogWarning($"[AutoBodyHiderProcessor] Material '{material.name}' missing '_WFFS_FEATURES_UVDISCARD' property. This shader may not support UV Discard.");
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
                    Debug.Log($"[AutoBodyHiderProcessor] Enabled UV Discard on FastFur material '{material.name}' - _EnableUDIMDiscardOptions value: {enabledValue}");
                }
                else
                {
                    Debug.LogWarning($"[AutoBodyHiderProcessor] Material '{material.name}' missing '_EnableUDIMDiscardOptions' property after installing module. Module may not be properly installed.");
                }
            }
            
            material.SetFloat("_UDIMDiscardMode", 0f);
            
            // Use effective UV channel (auto-detect if enabled)
            if (mesh == null && data.targetBodyMesh != null)
            {
                mesh = data.targetBodyMesh.sharedMesh;
            }
            int uvChannel = UDIMManipulator.GetEffectiveUVChannel(data, mesh);
            material.SetFloat("_UDIMDiscardUV", uvChannel);
            
            string tilePropertyName = $"_UDIMDiscardRow{data.udimDiscardRow}_{data.udimDiscardColumn}";
            if (material.HasProperty(tilePropertyName))
            {
                // Animation plays when toggle parameter is TRUE (ON state)
                // Material base is used when toggle parameter is FALSE (OFF state)
                // Base value represents the OFF state
                float baseValue = 0f; // discard OFF (body visible)
                material.SetFloat(tilePropertyName, baseValue);
                material.SetOverrideTag(tilePropertyName + "Animated", "1");
                
                string shaderName = UDIMManipulator.GetShaderDisplayName(material);
                Debug.Log($"[AutoBodyHider] Configured {shaderName} for toggle: {tilePropertyName} = {baseValue} (OFF state), Animated tag set", data);
            }
            else
            {
                Debug.LogError($"[AutoBodyHider] Property {tilePropertyName} not found on material!", data);
            }
        }

        private void IntegrateWithVRCFuryToggle(AutoBodyHiderData data, Material poiyomiMaterial, Component vrcFuryComponent)
        {
            try
            {
                Debug.Log($"[AutoBodyHider] Integrating with existing VRCFury Toggle on '{data.gameObject.name}'", data);
                
                // Get the content field via reflection
                var contentField = vrcFuryComponent.GetType().GetField("content", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (contentField == null)
                {
                    Debug.LogWarning($"[AutoBodyHider] Could not find 'content' field on VRCFury component", data);
                    return;
                }
                
                var content = contentField.GetValue(vrcFuryComponent);
                if (content != null && content.GetType().Name == "Toggle")
                {
                    // Create the UDIM animation
                    AnimationClip toggleAnimation = CreateUDIMToggleAnimation(data, poiyomiMaterial);
                    
                    if (toggleAnimation != null)
                    {
                        // Get the state field
                        var stateField = content.GetType().GetField("state", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (stateField == null)
                        {
                            Debug.LogWarning($"[AutoBodyHider] Could not find 'state' field on Toggle", data);
                            return;
                        }
                        
                        var state = stateField.GetValue(content);
                        var actionsField = state.GetType().GetField("actions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (actionsField == null)
                        {
                            Debug.LogWarning($"[AutoBodyHider] Could not find 'actions' field on State", data);
                            return;
                        }
                        
                        var actionsList = actionsField.GetValue(state) as System.Collections.IList;
                        if (actionsList == null)
                        {
                            Debug.LogWarning($"[AutoBodyHider] Actions list is null", data);
                            return;
                        }
                        
                        // Create AnimationClipAction via reflection (it's internal)
                        var animActionType = System.Type.GetType("VF.Model.StateAction.AnimationClipAction, VRCFury");
                        if (animActionType == null)
                        {
                            Debug.LogWarning($"[AutoBodyHider] Could not find AnimationClipAction type", data);
                            return;
                        }
                        
                        var animAction = System.Activator.CreateInstance(animActionType);
                        
                        // Set the motion field
                        var motionField = animActionType.GetField("motion", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (motionField != null)
                        {
                            motionField.SetValue(animAction, toggleAnimation);
                        }
                        
                        actionsList.Add(animAction);
                        
                        Debug.Log($"[AutoBodyHider] Added UDIM discard animation to existing VRCFury Toggle", data);
                        EditorUtility.SetDirty(vrcFuryComponent);
                    }
                }
                else
                {
                    Debug.LogWarning($"[AutoBodyHider] VRCFury component content is not a Toggle type, cannot integrate", data);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AutoBodyHider] Error integrating with VRCFury Toggle: {ex.Message}", data);
                Debug.LogException(ex);
            }
        }
        
        private void CreateUDIMToggle(AutoBodyHiderData data, Material poiyomiMaterial)
        {
            try
            {
                // For ObjectToggle mode, disable the clothing object by default
                // Toggle ON will enable it + apply discard
                // Toggle OFF will disable it (no discard needed)
                if (data.toggleType == ToggleType.ObjectToggle && !data.toggleDefaultOn)
                {
                    data.gameObject.SetActive(false);
                }
                
                var toggle = FuryComponents.CreateToggle(data.gameObject);                
                
                bool hasMenuPath = !string.IsNullOrEmpty(data.toggleMenuPath);
                bool hasCustomParam = !string.IsNullOrEmpty(data.toggleParameterName);
                
                if (hasMenuPath)
                {
                    toggle.SetMenuPath(data.toggleMenuPath);
                }
                else if (data.toggleSynced && hasCustomParam)
                {
                    // Parameter only mode: disable menu item creation
                    var toggleType = toggle.GetType();
                    var cField = toggleType.GetField("c", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var toggleModel = cField.GetValue(toggle);
                    var addMenuItemField = toggleModel.GetType().GetField("addMenuItem", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (addMenuItemField != null)
                    {
                        addMenuItemField.SetValue(toggleModel, false);
                        Debug.Log($"[AutoBodyHider] Parameter only mode - menu item disabled", data);
                    }
                }

                if (data.toggleSaved)
                    toggle.SetSaved();

                if (data.toggleDefaultOn)
                    toggle.SetDefaultOn();
                    
                if (data.toggleSlider)
                    toggle.SetSlider();
                    
                if (data.toggleExclusiveOffState)
                    toggle.SetExclusiveOffState();

                // Set global parameter if synced (with or without custom name)
                if (data.toggleSynced)
                {
                    if (hasCustomParam)
                        toggle.SetGlobalParameter(data.toggleParameterName);
                    else
                        toggle.SetGlobalParameter(""); // VRCFury auto-generates synced parameter name
                }
                
                // Apply advanced toggle options via reflection
                if (data.toggleHoldButton || data.toggleEnableExclusiveTag || data.toggleEnableIcon)
                {
                    var toggleType = toggle.GetType();
                    var cField = toggleType.GetField("c", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var toggleModel = cField.GetValue(toggle);
                    
                    if (data.toggleHoldButton)
                    {
                        var holdButtonField = toggleModel.GetType().GetField("holdButton", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (holdButtonField != null) holdButtonField.SetValue(toggleModel, true);
                    }
                    
                    if (data.toggleEnableExclusiveTag && !string.IsNullOrEmpty(data.toggleExclusiveTag))
                    {
                        toggle.AddExclusiveTag(data.toggleExclusiveTag);
                    }
                    
                    if (data.toggleEnableIcon && data.toggleIcon != null)
                    {
                        var enableIconField = toggleModel.GetType().GetField("enableIcon", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        var iconField = toggleModel.GetType().GetField("icon", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (enableIconField != null && iconField != null)
                        {
                            enableIconField.SetValue(toggleModel, true);
                            // Use reflection to create GuidTexture2d instance via implicit operator
                            var guidTexture2dType = iconField.FieldType;
                            var implicitOp = guidTexture2dType.GetMethod("op_Implicit", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new Type[] { typeof(Texture2D) }, null);
                            if (implicitOp != null)
                            {
                                var guidTextureInstance = implicitOp.Invoke(null, new object[] { data.toggleIcon });
                                iconField.SetValue(toggleModel, guidTextureInstance);
                            }
                        }
                    }
                }

                var actions = toggle.GetActions();

                if (data.toggleType == ToggleType.ObjectToggle)
                    actions.AddTurnOn(data.gameObject);

                AnimationClip toggleAnimation = CreateUDIMToggleAnimation(data, poiyomiMaterial);

                if (toggleAnimation != null)
                {
                    // Use reflection to set the motion field directly
                    // Runtime-created clips work with VRCFury
                    actions.AddAnimationClip(toggleAnimation);
                    
                    // Now use reflection to access the last added action and set its motion field
                    var toggleType = toggle.GetType();
                    var cField = toggleType.GetField("c", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var toggleModel = cField.GetValue(toggle);
                    var stateField = toggleModel.GetType().GetField("state", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    var state = stateField.GetValue(toggleModel);
                    var actionsField = state.GetType().GetField("actions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    var actionsList = actionsField.GetValue(state) as System.Collections.IList;
                    
                    if (actionsList != null && actionsList.Count > 0)
                    {
                        var lastAction = actionsList[actionsList.Count - 1];
                        var motionField = lastAction.GetType().GetField("motion", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (motionField != null)
                        {
                            motionField.SetValue(lastAction, toggleAnimation);
                            Debug.Log($"[AutoBodyHider] Animation added with motion field set via reflection", data);
                        }
                        else
                        {
                            Debug.LogWarning($"[AutoBodyHider] Could not find motion field, animation may not work", data);
                        }
                    }
                }
                else
                {
                    Debug.LogError($"[AutoBodyHiderProcessor] Failed to create UDIM toggle animation", data);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AutoBodyHiderProcessor] Error creating UDIM toggle: {ex.Message}", data);
                Debug.LogException(ex);
            }
        }

        private AnimationClip CreateUDIMToggleAnimation(AutoBodyHiderData data, Material poiyomiMaterial)
        {
            AnimationClip clip = new AnimationClip();
            clip.name = $"UDIM_Discard_Toggle_{data.gameObject.name}";

            string rendererPath = GetRelativePath(data.targetBodyMesh.transform, data.transform.root);

            Material[] materials = data.targetBodyMesh.sharedMaterials;
            int materialIndex = -1;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == poiyomiMaterial)
                {
                    materialIndex = i;
                    break;
                }
            }

            if (materialIndex == -1)
            {
                Debug.LogError($"[AutoBodyHiderProcessor] Could not find material on renderer", data);
                return null;
            }

            // Animation plays when toggle parameter is TRUE (ON state)
            // Set to 1 = discard ON (body hidden)
            float animValue = 1f;
            
            AnimationCurve discardCurve = new AnimationCurve();
            discardCurve.AddKey(0f, animValue);
            discardCurve.AddKey(1f/60f, animValue);
            
            // Get coordination data to find overlap tiles this clothing is involved in
            List<string> tilesToAnimate = new List<string>();
            
            // 1. Add this clothing's individual tile
            string individualTile = $"_UDIMDiscardRow{data.udimDiscardRow}_{data.udimDiscardColumn}";
            tilesToAnimate.Add(individualTile);
            
            // 2. Add any overlap tiles this clothing is involved in
            var coordGroup = AutoBodyHiderCoordinator.CoordinatedGroups.ContainsKey(data.targetBodyMesh) 
                           ? AutoBodyHiderCoordinator.CoordinatedGroups[data.targetBodyMesh] 
                           : null;
            
            if (coordGroup != null)
            {
                foreach (var overlap in coordGroup.overlapRegions)
                {
                    if (overlap.involvedClothing.Contains(data))
                    {
                        var tile = coordGroup.overlapTiles[overlap];
                        string overlapTileName = $"_UDIMDiscardRow{tile.row}_{tile.col}";
                        tilesToAnimate.Add(overlapTileName);
                        Debug.Log($"[AutoBodyHider] Toggle for '{data.name}' will also control overlap tile ({tile.row},{tile.col}) shared with {string.Join(",", overlap.involvedClothing.Where(c => c != data).Select(c => c.name))}", data);
                    }
                }
            }
            
            // Create animation curves for all tiles this toggle controls
            foreach (var tilePropertyName in tilesToAnimate)
            {
                if (!poiyomiMaterial.HasProperty(tilePropertyName))
                {
                    Debug.LogWarning($"[AutoBodyHiderProcessor] Material doesn't have '{tilePropertyName}' property", data);
                    continue;
                }
                
                string propertyPath = $"material.{tilePropertyName}";

                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                    rendererPath,
                    typeof(SkinnedMeshRenderer),
                    propertyPath
                );

                AnimationUtility.SetEditorCurve(clip, binding, discardCurve);
                Debug.Log($"[AutoBodyHider] Added animation curve for tile '{tilePropertyName}'", data);
            }

            // Optionally save clip to file for debugging
            if (data.debugSaveAnimation)
            {
                string debugPath = $"Assets/Generated/YUCP_UDIM_Toggle_{data.gameObject.name}.anim";
                AssetDatabase.CreateAsset(clip, debugPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[AutoBodyHider] Animation saved to: {debugPath}", data);
            }
            
            Debug.Log($"[AutoBodyHider] === Animation Clip Details ===", data);
            Debug.Log($"[AutoBodyHider] Clip name: {clip.name}", data);
            Debug.Log($"[AutoBodyHider] Renderer path: '{rendererPath}'", data);
            Debug.Log($"[AutoBodyHider] Material index: {materialIndex}", data);
            Debug.Log($"[AutoBodyHider] Animating {tilesToAnimate.Count} tiles (individual + overlaps)", data);
            Debug.Log($"[AutoBodyHider] Animation value: {animValue} (discard ON)", data);
            Debug.Log($"[AutoBodyHider] Total bindings: {AnimationUtility.GetCurveBindings(clip).Length}", data);
            
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                Debug.Log($"[AutoBodyHider]   Binding: path='{b.path}' | type={b.type.Name} | property='{b.propertyName}' | value={curve.keys[0].value}", data);
            }
            
            Debug.Log($"[AutoBodyHider] Clip being added to toggle on GameObject: {data.gameObject.name}", data);
            Debug.Log($"[AutoBodyHider] ==============================", data);

            return clip;
        }

        private string GetRelativePath(Transform target, Transform root)
        {
            if (target == root)
                return "";

            List<string> path = new List<string>();
            Transform current = target;

            while (current != null && current != root)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", path);
        }
        
        // ==== NEW METHODS FOR MULTI-CLOTHING UDIM SUPPORT ====
        
        private bool[] DetectHiddenVerticesForData(AutoBodyHiderData data, Mesh bodyMesh)
        {
            Vector3[] vertices = bodyMesh.vertices;
            
            // Get first clothing mesh for cache compatibility (hash generation uses all meshes)
            var clothingMeshes = data.GetClothingMeshes();
            Mesh clothingMesh = (clothingMeshes != null && clothingMeshes.Length > 0) ? clothingMeshes[0].sharedMesh : null;
            
            bool[] hiddenVertices;
            int hiddenCount;
            
            if (DetectionCache.TryGetCachedResult(data, bodyMesh, clothingMesh, out hiddenVertices, out hiddenCount))
            {
                if (data.debugMode)
                {
                    Debug.Log($"[AutoBodyHiderProcessor] Using cached result for '{data.name}': {hiddenCount} hidden vertices");
                }
                return hiddenVertices;
            }
            
            Vector3[] normals = bodyMesh.normals;
            List<Vector2> uv0List = new List<Vector2>();
            bodyMesh.GetUVs(0, uv0List);
            Vector2[] uv0 = uv0List.ToArray();
            
            if (data.debugMode)
            {
                Debug.Log($"[AutoBodyHiderProcessor] Detecting hidden vertices for '{data.name}' ({vertices.Length} vertices)");
            }
            
            hiddenVertices = VertexDetection.DetectHiddenVertices(data, vertices, normals, uv0, null);
            
            hiddenCount = 0;
            foreach (bool hidden in hiddenVertices)
            {
                if (hidden) hiddenCount++;
            }
            
            if (data.debugMode)
            {
                Debug.Log($"[AutoBodyHiderProcessor] Detected {hiddenCount} hidden vertices for '{data.name}' ({(hiddenCount * 100f / vertices.Length):F1}%)");
            }
            
            DetectionCache.CacheResult(data, bodyMesh, clothingMesh, hiddenVertices, hiddenCount);
            
            return hiddenVertices;
        }
        
        private bool[] ApplyPostProcessing(AutoBodyHiderData data, Vector3[] vertices, bool[] hiddenVertices, ref int hiddenCount)
        {
            if (data.mirrorSymmetry)
            {
                hiddenVertices = ApplySymmetryMirror(hiddenVertices, vertices);
                
                hiddenCount = 0;
                foreach (bool hidden in hiddenVertices)
                {
                    if (hidden) hiddenCount++;
                }
                
                if (data.debugMode)
                {
                    Debug.Log($"[AutoBodyHiderProcessor] After symmetry: {hiddenCount} hidden vertices for '{data.name}'");
                }
            }
            
            if (data.safetyMargin > 0.0001f)
            {
                hiddenVertices = ApplySafetyMargin(hiddenVertices, vertices, data);
                
                hiddenCount = 0;
                foreach (bool hidden in hiddenVertices)
                {
                    if (hidden) hiddenCount++;
                }
                
                if (data.debugMode)
                {
                    Debug.Log($"[AutoBodyHiderProcessor] After safety margin: {hiddenCount} hidden vertices for '{data.name}'");
                }
            }
            
            return hiddenVertices;
        }
        
        private Mesh MergeUDIMDiscards(BodyMeshProcessing group)
        {
            // Start with the original mesh
            Mesh baseMesh = UnityEngine.Object.Instantiate(group.originalMesh);
            baseMesh.name = group.originalMesh.name + "_MultiUDIM";
            
            // Determine UV channel to use for merged components
            int targetUVChannel = 1; // Default
            if (group.udimComponents.Count > 0)
            {
                targetUVChannel = UDIMManipulator.GetEffectiveUVChannel(group.udimComponents[0], group.originalMesh);
            }
            
            // Get UV0 texture coordinates
            List<Vector2> uv0List = new List<Vector2>();
            baseMesh.GetUVs(0, uv0List);
            Vector2[] uv0 = uv0List.ToArray();
            
            // Get or create target UV channel for discard logic
            List<Vector2> targetUVChannelList = new List<Vector2>();
            baseMesh.GetUVs(targetUVChannel, targetUVChannelList);
            Vector2[] targetUV;
            
            if (targetUVChannelList.Count == 0 || targetUVChannelList.Count != uv0.Length)
            {
                // Target channel doesn't exist or has wrong length - create from UV0
                targetUV = new Vector2[uv0.Length];
                System.Array.Copy(uv0, targetUV, uv0.Length);
            }
            else
            {
                targetUV = targetUVChannelList.ToArray();
            }
            
            // Track which vertices are assigned to which clothing pieces
            List<AutoBodyHiderData>[] vertexOwners = new List<AutoBodyHiderData>[uv0.Length];
            for (int i = 0; i < vertexOwners.Length; i++)
            {
                vertexOwners[i] = new List<AutoBodyHiderData>();
            }
            
            // First pass: determine which clothing pieces cover each vertex
            foreach (var kvp in group.hiddenVerticesMap)
            {
                var data = kvp.Key;
                var hiddenVertices = kvp.Value;
                
                for (int i = 0; i < hiddenVertices.Length; i++)
                {
                    if (hiddenVertices[i])
                    {
                        vertexOwners[i].Add(data);
                    }
                }
            }
            
            // Get overlap data from coordinator
            var coordGroup = AutoBodyHiderCoordinator.CoordinatedGroups.ContainsKey(group.bodyMesh) 
                           ? AutoBodyHiderCoordinator.CoordinatedGroups[group.bodyMesh] 
                           : null;
            
            // Second pass: assign vertices to appropriate tiles (individual or overlap)
            for (int i = 0; i < targetUV.Length; i++)
            {
                if (vertexOwners[i].Count == 0)
                {
                    // Not covered by any clothing - leave in original space
                    continue;
                }
                else if (vertexOwners[i].Count == 1)
                {
                    // Covered by one clothing piece: use its individual tile
                    var data = vertexOwners[i][0];
                    float uOffset = data.udimDiscardColumn;
                    float vOffset = data.udimDiscardRow;
                    targetUV[i] = new Vector2(targetUV[i].x + uOffset, targetUV[i].y + vOffset);
                }
                else
                {
                    // Covered by multiple pieces - find overlap tile
                    if (coordGroup != null)
                    {
                        var overlap = FindOverlapRegion(coordGroup, vertexOwners[i]);
                        if (overlap != null && coordGroup.overlapTiles.ContainsKey(overlap))
                        {
                            var tile = coordGroup.overlapTiles[overlap];
                            float uOffset = tile.col;
                            float vOffset = tile.row;
                            targetUV[i] = new Vector2(targetUV[i].x + uOffset, targetUV[i].y + vOffset);
                        }
                        else
                        {
                            // Fallback: use first clothing piece's tile
                            var data = vertexOwners[i][0];
                            float uOffset = data.udimDiscardColumn;
                            float vOffset = data.udimDiscardRow;
                            targetUV[i] = new Vector2(targetUV[i].x + uOffset, targetUV[i].y + vOffset);
                            
                            Debug.LogWarning($"[AutoBodyHiderProcessor] No overlap tile found for vertex {i} covered by {vertexOwners[i].Count} pieces. Using first piece's tile.");
                        }
                    }
                }
            }
            
            // Set target UV channel on the mesh
            List<Vector2> finalUVList = new List<Vector2>(targetUV);
            baseMesh.SetUVs(targetUVChannel, finalUVList);
            
            // Log statistics
            int[] tileCounts = new int[16];
            foreach (var kvp in group.hiddenVerticesMap)
            {
                var data = kvp.Key;
                int count = kvp.Value.Count(h => h);
                Debug.Log($"[AutoBodyHiderProcessor] '{data.name}' covers {count} vertices (tile {data.udimDiscardRow},{data.udimDiscardColumn})");
            }
            
            return baseMesh;
        }
        
        /// <summary>
        /// Detect ACTUAL overlaps by comparing hidden vertex arrays
        /// Creates overlap tiles when clothing pieces share vertices
        /// </summary>
        private void DetectAndAssignActualOverlaps(BodyMeshProcessing group)
        {
            var coordGroup = AutoBodyHiderCoordinator.CoordinatedGroups.ContainsKey(group.bodyMesh) 
                           ? AutoBodyHiderCoordinator.CoordinatedGroups[group.bodyMesh] 
                           : null;
            
            if (coordGroup == null)
            {
                Debug.LogWarning("[AutoBodyHiderProcessor] No coordinator data found for overlap detection");
                return;
            }
            
            var validPieces = group.hiddenVerticesMap.Keys.ToList();
            int potentialOverlaps = validPieces.Count * (validPieces.Count - 1) / 2;
            int actualOverlaps = 0;
            int optimizationSkips = 0;
            
            // Check if tile usage is enabled on any component
            bool optimizationEnabled = validPieces.Any(p => p.optimizeTileUsage);
            
            Debug.Log($"[AutoBodyHiderProcessor] Analyzing {potentialOverlaps} potential overlaps between {validPieces.Count} clothing pieces (Optimization: {optimizationEnabled})...");
            
            // Track which vertices are already "claimed" by larger pieces
            bool[] claimedVertices = new bool[group.originalMesh.vertexCount];
            
            // If tile usage enabled, process pieces in order (largest coverage first)
            // Mark vertices as "claimed" by pieces with larger coverage
            if (optimizationEnabled)
            {
                // Pieces are already sorted by coverage in validComponents
                foreach (var data in validPieces)
                {
                    if (!data.optimizeTileUsage) continue;
                    
                    var hiddenVertices = group.hiddenVerticesMap[data];
                    for (int v = 0; v < Math.Min(hiddenVertices.Length, claimedVertices.Length); v++)
                    {
                        if (hiddenVertices[v])
                        {
                            claimedVertices[v] = true;
                        }
                    }
                }
            }
            
            // Check all pairwise combinations for actual overlap
            for (int i = 0; i < validPieces.Count; i++)
            {
                for (int j = i + 1; j < validPieces.Count; j++)
                {
                    var piece1 = validPieces[i];
                    var piece2 = validPieces[j];
                    
                    var hidden1 = group.hiddenVerticesMap[piece1];
                    var hidden2 = group.hiddenVerticesMap[piece2];
                    
                    // Count shared vertices
                    int sharedCount = 0;
                    int minLength = Math.Min(hidden1.Length, hidden2.Length);
                    
                    for (int v = 0; v < minLength; v++)
                    {
                        if (hidden1[v] && hidden2[v])
                        {
                            sharedCount++;
                        }
                    }
                    
                    // OPTIMIZATION: Skip overlap if one piece has no toggle and is fully covered
                    if (optimizationEnabled && sharedCount > 0)
                    {
                        // Check if piece2 (smaller) has no toggle and is fully covered by piece1 (larger)
                        if (!piece2.createToggle)
                        {
                            int piece2TotalCoverage = hidden2.Count(h => h);
                            float coverageRatio = (float)sharedCount / piece2TotalCoverage;
                            
                            // If 95%+ of piece2's coverage overlaps with piece1, skip this overlap tile
                            if (coverageRatio >= 0.95f)
                            {
                                Debug.Log($"[AutoBodyHiderProcessor] ⚡ Skipping overlap tile for '{piece1.name}' + '{piece2.name}' " +
                                         $"({coverageRatio:P0} overlap, piece2 has no toggle, fully covered by piece1)");
                                optimizationSkips++;
                                continue;
                            }
                        }
                        
                        // Same check for piece1 being covered by piece2
                        if (!piece1.createToggle)
                        {
                            int piece1TotalCoverage = hidden1.Count(h => h);
                            float coverageRatio = (float)sharedCount / piece1TotalCoverage;
                            
                            if (coverageRatio >= 0.95f)
                            {
                                Debug.Log($"[AutoBodyHiderProcessor] ⚡ Skipping overlap tile for '{piece1.name}' + '{piece2.name}' " +
                                         $"({coverageRatio:P0} overlap, piece1 has no toggle, fully covered by piece2)");
                                optimizationSkips++;
                                continue;
                            }
                        }
                    }
                    
                    // Create overlap tile if there are shared vertices
                    if (sharedCount > 0)
                    {
                        // Try to allocate a tile
                        while (coordGroup.usedTiles.Contains((coordGroup.nextAvailableRow, coordGroup.nextAvailableCol)))
                        {
                            coordGroup.nextAvailableCol++;
                            if (coordGroup.nextAvailableCol >= 4)
                            {
                                coordGroup.nextAvailableCol = 0;
                                coordGroup.nextAvailableRow++;
                                if (coordGroup.nextAvailableRow >= 4)
                                {
                                    Debug.LogWarning($"[AutoBodyHiderProcessor] Ran out of tiles for overlaps! " +
                                                   $"Overlap between '{piece1.name}' and '{piece2.name}' ({sharedCount} shared vertices) will not be handled optimally.");
                                    return;
                                }
                            }
                        }
                        
                        var tile = (coordGroup.nextAvailableRow, coordGroup.nextAvailableCol);
                        coordGroup.usedTiles.Add(tile);
                        
                        var overlap = new AutoBodyHiderCoordinator.OverlapRegion(new List<AutoBodyHiderData> { piece1, piece2 });
                        coordGroup.overlapRegions.Add(overlap);
                        coordGroup.overlapTiles[overlap] = tile;
                        
                        actualOverlaps++;
                        
                        Debug.Log($"[AutoBodyHiderProcessor] ✓ REAL overlap detected: '{piece1.name}' + '{piece2.name}' " +
                                 $"share {sharedCount} vertices → tile ({tile.Item1},{tile.Item2})");
                        
                        // Move to next tile
                        coordGroup.nextAvailableCol++;
                        if (coordGroup.nextAvailableCol >= 4)
                        {
                            coordGroup.nextAvailableCol = 0;
                            coordGroup.nextAvailableRow++;
                        }
                    }
                    else
                    {
                        Debug.Log($"[AutoBodyHiderProcessor] ✗ No overlap: '{piece1.name}' + '{piece2.name}' (0 shared vertices, skipped)");
                    }
                }
            }
            
            Debug.Log($"[AutoBodyHiderProcessor] Overlap detection complete: " +
                     $"{actualOverlaps} real overlaps created, {optimizationSkips} optimized away, " +
                     $"{potentialOverlaps - actualOverlaps - optimizationSkips} naturally skipped (no shared vertices). " +
                     $"Total tiles used: {coordGroup.assignedTiles.Count} individual + {actualOverlaps} overlap = " +
                     $"{coordGroup.assignedTiles.Count + actualOverlaps} tiles");
        }
        
        /// <summary>
        /// Find the overlap region that matches the given set of clothing pieces
        /// </summary>
        private AutoBodyHiderCoordinator.OverlapRegion FindOverlapRegion(AutoBodyHiderCoordinator.BodyMeshGroup group, List<AutoBodyHiderData> clothingPieces)
        {
            var testRegion = new AutoBodyHiderCoordinator.OverlapRegion(clothingPieces);
            
            foreach (var overlap in group.overlapRegions)
            {
                if (overlap.Equals(testRegion))
                {
                    return overlap;
                }
            }
            
            return null;
        }
        
        private void ConfigureMaterialsForMultipleUDIM(BodyMeshProcessing group)
        {
            Material[] materials = group.bodyMesh.sharedMaterials;
            Material poiyomiMaterial = null;
            
            Debug.Log($"[AutoBodyHiderProcessor] ConfigureMaterialsForMultipleUDIM - Searching for compatible material on '{group.bodyMesh.name}'");
            Debug.Log($"[AutoBodyHiderProcessor] Body mesh has {materials.Length} materials:");
            
            // Collect all target materials from components
            List<Material> targetMaterials = new List<Material>();
            foreach (var data in group.udimComponents)
            {
                if (data.targetMaterials != null && data.targetMaterials.Length > 0)
                {
                    foreach (var mat in data.targetMaterials)
                    {
                        if (mat != null && !targetMaterials.Contains(mat))
                        {
                            targetMaterials.Add(mat);
                        }
                    }
                }
            }
            
            if (targetMaterials.Count > 0)
            {
                Debug.Log($"[AutoBodyHiderProcessor] Using {targetMaterials.Count} target material(s) from components");
            }
            
            // Find all compatible materials (Poiyomi or FastFur)
            List<int> poiyomiMaterialIndices = new List<int>();
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null)
                {
                    continue;
                }
                
                bool shouldInclude = false;
                if (targetMaterials.Count > 0)
                {
                    // Use target materials if specified - match by reference or by shader name
                    bool isTargetMaterial = targetMaterials.Contains(materials[i]);
                    if (!isTargetMaterial)
                    {
                        // Also check by shader name in case material instances differ
                        string meshShaderName = materials[i].shader != null ? materials[i].shader.name.ToLower() : "";
                        foreach (var targetMat in targetMaterials)
                        {
                            if (targetMat != null && targetMat.shader != null)
                            {
                                string targetShaderName = targetMat.shader.name.ToLower();
                                if (meshShaderName == targetShaderName)
                                {
                                    isTargetMaterial = true;
                                    break;
                                }
                            }
                        }
                    }
                    
                    if (isTargetMaterial && UDIMManipulator.IsPoiyomiWithUDIMSupport(materials[i]))
                    {
                        shouldInclude = true;
                    }
                }
                else
                {
                    // Auto-detect all compatible materials
                    if (UDIMManipulator.IsPoiyomiWithUDIMSupport(materials[i]))
                    {
                        shouldInclude = true;
                    }
                }
                
                if (shouldInclude)
                {
                    if (poiyomiMaterial == null)
                    {
                        poiyomiMaterial = materials[i];
                    }
                    poiyomiMaterialIndices.Add(i);
                    Debug.Log($"[AutoBodyHiderProcessor] Found compatible material at index {i}: '{materials[i].name}' (Shader: '{materials[i].shader.name}')");
                }
                else
                {
                    // Log why material wasn't included for debugging
                    if (targetMaterials.Count > 0)
                    {
                        bool isCompatible = UDIMManipulator.IsPoiyomiWithUDIMSupport(materials[i]);
                        Debug.Log($"[AutoBodyHiderProcessor] Material at index {i}: '{materials[i].name}' (Shader: '{materials[i].shader.name}') - Compatible: {isCompatible}");
                    }
                }
            }
            
            if (poiyomiMaterialIndices.Count == 0)
            {
                Debug.LogError($"[AutoBodyHiderProcessor] No Poiyomi or FastFur material found on body mesh '{group.bodyMesh.name}'. " +
                             $"UDIM Discard mode requires a Poiyomi or FastFur shader with UDIM support. " +
                             $"Please use Mesh Deletion mode instead, or add a compatible shader to the body mesh.");
                return;
            }
            
            string shaderName = poiyomiMaterial != null ? UDIMManipulator.GetShaderDisplayName(poiyomiMaterial) : "Poiyomi/FastFur";
            Debug.Log($"[AutoBodyHiderProcessor] Configuring multi-UDIM on {poiyomiMaterialIndices.Count} material(s)");
            
            // Use effective UV channel (auto-detect if enabled)
            int uvChannel = 1; // Default to UV1
            if (group.udimComponents.Count > 0)
            {
                uvChannel = UDIMManipulator.GetEffectiveUVChannel(group.udimComponents[0], group.originalMesh);
            }
            
            // Configure all compatible materials
            foreach (int materialIndex in poiyomiMaterialIndices)
            {
                Material materialToConfigure = materials[materialIndex];
                Material materialCopy = UnityEngine.Object.Instantiate(materialToConfigure);
                materialCopy.name = materialToConfigure.name + "_MultiUDIM";
                
                string shaderNameLower = materialCopy.shader.name.ToLower();
                Debug.Log($"[AutoBodyHiderProcessor] Configuring material '{materialCopy.name}' with shader '{materialCopy.shader.name}' (lowercase: '{shaderNameLower}')");
                
                // Enable appropriate shader keyword and feature toggle FIRST (especially for FastFur)
                if (shaderNameLower.Contains("poiyomi"))
                {
                    materialCopy.EnableKeyword("POI_UDIMDISCARD");
                    // Enable UDIM discard system
                    if (materialCopy.HasProperty("_EnableUDIMDiscardOptions"))
                    {
                        materialCopy.SetFloat("_EnableUDIMDiscardOptions", 1f);
                    }
                }
                else if (shaderNameLower.Contains("fastfur") || shaderNameLower.Contains("fast fur") || shaderNameLower.Contains("wffs") || shaderNameLower.Contains("warren"))
                {
                    // FastFur: Check and enable "Install UV Discard Module" toggle FIRST
                    if (materialCopy.HasProperty("_WFFS_FEATURES_UVDISCARD"))
                    {
                        // Check if module is already installed
                        float currentValue = materialCopy.GetFloat("_WFFS_FEATURES_UVDISCARD");
                        Debug.Log($"[AutoBodyHiderProcessor] FastFur material '{materialCopy.name}' - _WFFS_FEATURES_UVDISCARD current value: {currentValue}");
                        
                        if (currentValue < 0.5f)
                        {
                            // Install the module by setting the property to 1
                            // Try both SetFloat and SetInt in case Unity stores it differently
                            materialCopy.SetFloat("_WFFS_FEATURES_UVDISCARD", 1f);
                            if (materialCopy.HasProperty("_WFFS_FEATURES_UVDISCARD"))
                            {
                                try { materialCopy.SetInt("_WFFS_FEATURES_UVDISCARD", 1); } catch { }
                            }
                            
                            // Also explicitly enable the keyword
                            materialCopy.EnableKeyword("WFFS_FEATURES_UVDISCARD");
                            
                            // Force update the material
                            UnityEditor.EditorUtility.SetDirty(materialCopy);
                            materialCopy.name = materialCopy.name; // Force Unity to recognize changes
                            
                            float newValue = materialCopy.GetFloat("_WFFS_FEATURES_UVDISCARD");
                            Debug.Log($"[AutoBodyHiderProcessor] Installed UV Discard module on FastFur material '{materialCopy.name}' (was {currentValue}, now {newValue}, keyword enabled: {materialCopy.IsKeywordEnabled("WFFS_FEATURES_UVDISCARD")})");
                        }
                        else
                        {
                            // Module already installed, just ensure keyword is enabled
                            materialCopy.EnableKeyword("WFFS_FEATURES_UVDISCARD");
                            Debug.Log($"[AutoBodyHiderProcessor] FastFur material '{materialCopy.name}' - UV Discard module already installed, ensuring keyword is enabled");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[AutoBodyHiderProcessor] Material '{materialCopy.name}' missing '_WFFS_FEATURES_UVDISCARD' property. This shader may not support UV Discard.");
                    }
                    
                    // Enable UDIM discard system (this property becomes available after module is installed)
                    if (materialCopy.HasProperty("_EnableUDIMDiscardOptions"))
                    {
                        // Try both SetFloat and SetInt since it's a ToggleUI (Int) but FastFur uses GetFloat
                        materialCopy.SetFloat("_EnableUDIMDiscardOptions", 1f);
                        try { materialCopy.SetInt("_EnableUDIMDiscardOptions", 1); } catch { }
                        
                        UnityEditor.EditorUtility.SetDirty(materialCopy);
                        materialCopy.name = materialCopy.name; // Force Unity to recognize changes
                        
                        float enabledValue = materialCopy.GetFloat("_EnableUDIMDiscardOptions");
                        Debug.Log($"[AutoBodyHiderProcessor] Enabled UV Discard on FastFur material '{materialCopy.name}' - _EnableUDIMDiscardOptions value: {enabledValue}");
                    }
                    else
                    {
                        Debug.LogWarning($"[AutoBodyHiderProcessor] Material '{materialCopy.name}' missing '_EnableUDIMDiscardOptions' property after installing module. Module may not be properly installed.");
                    }
                }
                
                // Set UDIM discard mode and UV channel
                if (materialCopy.HasProperty("_UDIMDiscardMode"))
                {
                    materialCopy.SetFloat("_UDIMDiscardMode", 0f); // Vertex mode
                }
                
                if (materialCopy.HasProperty("_UDIMDiscardUV"))
                {
                    materialCopy.SetFloat("_UDIMDiscardUV", uvChannel);
                }
                else if (materialCopy.HasProperty("_UDIMDiscardUVChannel"))
                {
                    // Alternative property name
                    materialCopy.SetFloat("_UDIMDiscardUVChannel", uvChannel);
                }
            
                // Get overlap data from coordinator
                var coordGroup = AutoBodyHiderCoordinator.CoordinatedGroups.ContainsKey(group.bodyMesh) 
                               ? AutoBodyHiderCoordinator.CoordinatedGroups[group.bodyMesh] 
                               : null;
                
                // Configure tiles for each clothing piece on this material
                foreach (var data in group.udimComponents)
                {
                    string tilePropertyName = $"_UDIMDiscardRow{data.udimDiscardRow}_{data.udimDiscardColumn}";
                    
                    if (materialCopy.HasProperty(tilePropertyName))
                    {
                        // Check if this component uses a toggle (either creating one or using existing)
                        bool hasToggle = data.createToggle || data.useExistingToggle;
                        
                        if (hasToggle)
                        {
                            // For toggles: base = 0 (OFF), animation sets to 1 (ON)
                            materialCopy.SetFloat(tilePropertyName, 0f);
                            materialCopy.SetOverrideTag(tilePropertyName + "Animated", "1");
                        }
                        else
                        {
                            // For non-toggles: set to ON
                            materialCopy.SetFloat(tilePropertyName, 1f);
                        }
                        
                        Debug.Log($"[AutoBodyHiderProcessor] Configured individual tile ({data.udimDiscardRow}, {data.udimDiscardColumn}) for '{data.name}' on material '{materialCopy.name}' (Toggle: {hasToggle})");
                    }
                    else
                    {
                        Debug.LogWarning($"[AutoBodyHiderProcessor] Property {tilePropertyName} not found on material '{materialCopy.name}'");
                    }
                }
                
                // Configure overlap tiles
                if (coordGroup != null && coordGroup.overlapRegions.Count > 0)
                {
                    foreach (var overlap in coordGroup.overlapRegions)
                    {
                        if (coordGroup.overlapTiles.ContainsKey(overlap))
                        {
                            var tile = coordGroup.overlapTiles[overlap];
                            string tilePropertyName = $"_UDIMDiscardRow{tile.row}_{tile.col}";
                            
                            if (materialCopy.HasProperty(tilePropertyName))
                            {
                                // Check if ANY of the involved clothing pieces have toggles (creating or using existing)
                                bool anyHasToggle = overlap.involvedClothing.Any(c => c.createToggle || c.useExistingToggle);
                                
                                if (anyHasToggle)
                                {
                                    // For toggles: base = 0 (OFF), animation sets to 1 (ON)
                                    materialCopy.SetFloat(tilePropertyName, 0f);
                                    materialCopy.SetOverrideTag(tilePropertyName + "Animated", "1");
                                }
                                else
                                {
                                    // For non-toggles: set to ON
                                    materialCopy.SetFloat(tilePropertyName, 1f);
                                }
                                
                                Debug.Log($"[AutoBodyHiderProcessor] Configured overlap tile ({tile.row}, {tile.col}) for '{overlap.regionName}' on material '{materialCopy.name}' (Toggle: {anyHasToggle})");
                            }
                            else
                            {
                                Debug.LogWarning($"[AutoBodyHiderProcessor] Property {tilePropertyName} not found on material '{materialCopy.name}'");
                            }
                        }
                    }
                }
                
                // Replace the material in array
                materials[materialIndex] = materialCopy;
                EditorUtility.SetDirty(materialCopy);
            }
            
            // Update all materials on the mesh
            group.bodyMesh.sharedMaterials = materials;
            EditorUtility.SetDirty(group.bodyMesh);
        }
        
        private void CreateUDIMToggleForComponent(AutoBodyHiderData data, BodyMeshProcessing group)
        {
            Material[] materials = group.bodyMesh.sharedMaterials;
            Material poiyomiMaterial = null;
            
            foreach (var mat in materials)
            {
                if (mat.name.Contains("MultiUDIM") && UDIMManipulator.IsPoiyomiWithUDIMSupport(mat))
                {
                    poiyomiMaterial = mat;
                    break;
                }
            }
            
            if (poiyomiMaterial == null)
            {
                Debug.LogError($"[AutoBodyHiderProcessor] Could not find multi-UDIM material for toggle on '{data.name}'");
                return;
            }
            
            // Check if using existing toggle
            Component toggleToUse = null;
            if (data.useExistingToggle && data.selectedToggle != null)
            {
                toggleToUse = data.selectedToggle;
            }
            else if (data.useExistingToggle)
            {
                toggleToUse = FindVRCFuryToggleInHierarchy(data.gameObject);
            }
            
            if (toggleToUse != null)
            {
                IntegrateWithVRCFuryToggle(data, poiyomiMaterial, toggleToUse);
            }
            else if (data.createToggle)
            {
                CreateUDIMToggle(data, poiyomiMaterial);
            }
        }
        
        private Component FindVRCFuryToggleInHierarchy(GameObject root)
        {
            // Check root GameObject
            var rootComponents = root.GetComponents<Component>();
            foreach (var comp in rootComponents)
            {
                if (comp != null && comp.GetType().Name == "VRCFury")
                {
                    return comp;
                }
            }
            
            // Check all children
            foreach (Transform child in root.transform)
            {
                var childComponents = child.GetComponents<Component>();
                foreach (var comp in childComponents)
                {
                    if (comp != null && comp.GetType().Name == "VRCFury")
                    {
                        return comp;
                    }
                }
            }
            
            return null;
        }
        
        private string GetGameObjectPath(GameObject obj, GameObject root)
        {
            if (obj == root)
                return obj.name;
            
            var path = new System.Collections.Generic.List<string>();
            Transform current = obj.transform;
            
            while (current != null && current != root.transform)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }
            
            return string.Join("/", path);
        }
    }
}

