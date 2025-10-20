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
            if (data.detectionMethod != DetectionMethod.Manual && data.clothingMesh == null)
            {
                Debug.LogError("[AutoBodyHiderProcessor] Automatic detection requires a clothing mesh reference.", data);
                return false;
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
            
            // Process deletion components (must be sequential)
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
                // Check if this data has a valid tile assigned (row and column should be 0-3)
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
            
            // Check if we should show progress window
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
                
                // Check if any component has optimization enabled
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
                
                // Create toggles for each valid clothing piece
                if (progressWindow != null)
                {
                    progressWindow.Progress(1.0f, "Creating toggles...");
                }
                
                foreach (var data in validComponents)
                {
                    if (data.createToggle)
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
            
            // Mesh deletion must be applied sequentially
            Mesh currentMesh = group.bodyMesh.sharedMesh;
            
            foreach (var data in group.deletionComponents)
            {
                ProcessBodyHider(data);
                // Processor already modifies the mesh, so just continue
            }
        }

        private void ProcessBodyHider(AutoBodyHiderData data)
        {
            Mesh bodyMesh = data.targetBodyMesh.sharedMesh;
            Mesh clothingMesh = data.clothingMesh != null ? data.clothingMesh.sharedMesh : null;
            Vector3[] vertices = bodyMesh.vertices;
            
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
            Material poiyomiMaterial = null;
            
            foreach (var material in materials)
            {
                if (UDIMManipulator.IsPoiyomiWithUDIMSupport(material))
                {
                    Material materialCopy = UnityEngine.Object.Instantiate(material);
                    
                    if (!data.createToggle)
                    {
                        UDIMManipulator.ConfigurePoiyomiMaterial(materialCopy, data);
                    }
                    else
                    {
                        ConfigurePoiyomiForToggle(materialCopy, data);
                    }
                    
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == material)
                        {
                            materials[i] = materialCopy;
                        }
                    }
                    
                    poiyomiMaterial = materialCopy;
                }
            }
            data.targetBodyMesh.sharedMaterials = materials;
            
            // Create toggle or integrate with existing VRCFury toggle
            if (poiyomiMaterial != null)
            {
                // Find VRCFury component by name (it's internal, can't use type directly)
                Component existingToggle = null;
                var components = data.gameObject.GetComponents<Component>();
                foreach (var comp in components)
                {
                    if (comp != null && comp.GetType().Name == "VRCFury")
                    {
                        existingToggle = comp;
                        break;
                    }
                }
                
                if (data.createToggle && existingToggle == null)
                {
                    // Create our own toggle
                    CreateUDIMToggle(data, poiyomiMaterial);
                }
                else if (existingToggle != null)
                {
                    // Integrate with existing VRCFury toggle
                    IntegrateWithVRCFuryToggle(data, poiyomiMaterial, existingToggle);
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

        private void ConfigurePoiyomiForToggle(Material material, AutoBodyHiderData data)
        {
            string shaderNameLower = material.shader.name.ToLower();
            
            material.SetFloat("_EnableUDIMDiscardOptions", 1f);
            
            // Enable appropriate shader keyword
            if (shaderNameLower.Contains("poiyomi"))
            {
                material.EnableKeyword("POI_UDIMDISCARD");
            }
            else if (shaderNameLower.Contains("fastfur") || shaderNameLower.Contains("wffs"))
            {
                material.EnableKeyword("WFFS_FEATURES_UVDISCARD");
                if (material.HasProperty("_WFFS_FEATURES_UVDISCARD"))
                {
                    material.SetFloat("_WFFS_FEATURES_UVDISCARD", 1f);
                }
            }
            
            material.SetFloat("_UDIMDiscardMode", 0f);
            material.SetFloat("_UDIMDiscardUV", 1);
            
            string tilePropertyName = $"_UDIMDiscardRow{data.udimDiscardRow}_{data.udimDiscardColumn}";
            if (material.HasProperty(tilePropertyName))
            {
                // Animation plays when toggle parameter is TRUE (ON state)
                // Material base is used when toggle parameter is FALSE (OFF state)
                // Therefore: base value should represent the OFF state
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
                    // Parameter only mode - disable menu item creation
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
                    // This ensures runtime-created clips work with VRCFury
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
            Mesh clothingMesh = data.clothingMesh != null ? data.clothingMesh.sharedMesh : null;
            Vector3[] vertices = bodyMesh.vertices;
            
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
            
            // Get UV0 (texture coordinates - never modified)
            List<Vector2> uv0List = new List<Vector2>();
            baseMesh.GetUVs(0, uv0List);
            Vector2[] uv0 = uv0List.ToArray();
            
            // Create UV1 from UV0 for discard logic
            Vector2[] uv1 = new Vector2[uv0.Length];
            System.Array.Copy(uv0, uv1, uv0.Length);
            
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
            for (int i = 0; i < uv1.Length; i++)
            {
                if (vertexOwners[i].Count == 0)
                {
                    // Not covered by any clothing - leave in UV0 space
                    continue;
                }
                else if (vertexOwners[i].Count == 1)
                {
                    // Covered by only one clothing piece - use its individual tile
                    var data = vertexOwners[i][0];
                    float uOffset = data.udimDiscardColumn;
                    float vOffset = data.udimDiscardRow;
                    uv1[i] = new Vector2(uv1[i].x + uOffset, uv1[i].y + vOffset);
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
                            uv1[i] = new Vector2(uv1[i].x + uOffset, uv1[i].y + vOffset);
                        }
                        else
                        {
                            // Fallback: use first clothing piece's tile
                            var data = vertexOwners[i][0];
                            float uOffset = data.udimDiscardColumn;
                            float vOffset = data.udimDiscardRow;
                            uv1[i] = new Vector2(uv1[i].x + uOffset, uv1[i].y + vOffset);
                            
                            Debug.LogWarning($"[AutoBodyHiderProcessor] No overlap tile found for vertex {i} covered by {vertexOwners[i].Count} pieces. Using first piece's tile.");
                        }
                    }
                }
            }
            
            // Set UV1 on the mesh
            List<Vector2> uv1List = new List<Vector2>(uv1);
            baseMesh.SetUVs(1, uv1List);
            
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
        /// Only creates overlap tiles when clothing pieces genuinely share vertices
        /// Implements coverage-based optimization when enabled
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
            
            // Check if optimization is enabled on any component
            bool optimizationEnabled = validPieces.Any(p => p.optimizeTileUsage);
            
            Debug.Log($"[AutoBodyHiderProcessor] Analyzing {potentialOverlaps} potential overlaps between {validPieces.Count} clothing pieces (Optimization: {optimizationEnabled})...");
            
            // Track which vertices are already "claimed" by larger pieces (for optimization)
            bool[] claimedVertices = new bool[group.originalMesh.vertexCount];
            
            // If optimization enabled, process pieces in order (largest coverage first)
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
                                Debug.Log($"[AutoBodyHiderProcessor] ⚡ OPTIMIZATION: Skipping overlap tile for '{piece1.name}' + '{piece2.name}' " +
                                         $"({coverageRatio:P0} overlap, piece2 has no toggle, fully covered by piece1)");
                                optimizationSkips++;
                                continue; // Don't create overlap tile
                            }
                        }
                        
                        // Same check for piece1 being covered by piece2
                        if (!piece1.createToggle)
                        {
                            int piece1TotalCoverage = hidden1.Count(h => h);
                            float coverageRatio = (float)sharedCount / piece1TotalCoverage;
                            
                            if (coverageRatio >= 0.95f)
                            {
                                Debug.Log($"[AutoBodyHiderProcessor] ⚡ OPTIMIZATION: Skipping overlap tile for '{piece1.name}' + '{piece2.name}' " +
                                         $"({coverageRatio:P0} overlap, piece1 has no toggle, fully covered by piece2)");
                                optimizationSkips++;
                                continue;
                            }
                        }
                    }
                    
                    // Create overlap tile if there are shared vertices (and not optimized away)
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
            int poiyomiMaterialIndex = -1;
            
            Debug.Log($"[AutoBodyHiderProcessor] ConfigureMaterialsForMultipleUDIM - Searching for compatible material on '{group.bodyMesh.name}'");
            Debug.Log($"[AutoBodyHiderProcessor] Body mesh has {materials.Length} materials:");
            
            // Find the compatible material (Poiyomi or FastFur)
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null)
                {
                    Debug.Log($"[AutoBodyHiderProcessor]   [{i}] NULL material");
                    continue;
                }
                
                string matShaderName = materials[i].shader != null ? materials[i].shader.name : "NULL SHADER";
                Debug.Log($"[AutoBodyHiderProcessor]   [{i}] Material: '{materials[i].name}' | Shader: '{matShaderName}' | Has UDIM prop: {materials[i].HasProperty("_EnableUDIMDiscardOptions")}");
                
                if (UDIMManipulator.IsPoiyomiWithUDIMSupport(materials[i]))
                {
                    poiyomiMaterial = materials[i];
                    poiyomiMaterialIndex = i;
                    Debug.Log($"[AutoBodyHiderProcessor] Found compatible material at index {i}!");
                    break;
                }
            }
            
            if (poiyomiMaterial == null)
            {
                Debug.LogError($"[AutoBodyHiderProcessor] No Poiyomi or FastFur material found on body mesh '{group.bodyMesh.name}'. " +
                             $"UDIM Discard mode requires a Poiyomi or FastFur shader with UDIM support. " +
                             $"Please use Mesh Deletion mode instead, or add a compatible shader to the body mesh.");
                return;
            }
            
            string shaderName = UDIMManipulator.GetShaderDisplayName(poiyomiMaterial);
            Debug.Log($"[AutoBodyHiderProcessor] Configuring multi-UDIM on {shaderName} material '{poiyomiMaterial.name}'");
            
            // Create a copy of the material
            Material materialCopy = UnityEngine.Object.Instantiate(poiyomiMaterial);
            materialCopy.name = poiyomiMaterial.name + "_MultiUDIM";
            
            string shaderNameLower = materialCopy.shader.name.ToLower();
            
            // Enable UDIM discard system
            materialCopy.SetFloat("_EnableUDIMDiscardOptions", 1f);
            
            // Enable appropriate shader keyword
            if (shaderNameLower.Contains("poiyomi"))
            {
                materialCopy.EnableKeyword("POI_UDIMDISCARD");
            }
            else if (shaderNameLower.Contains("fastfur") || shaderNameLower.Contains("wffs"))
            {
                materialCopy.EnableKeyword("WFFS_FEATURES_UVDISCARD");
                if (materialCopy.HasProperty("_WFFS_FEATURES_UVDISCARD"))
                {
                    materialCopy.SetFloat("_WFFS_FEATURES_UVDISCARD", 1f);
                }
            }
            
            materialCopy.SetFloat("_UDIMDiscardMode", 0f); // Vertex mode
            materialCopy.SetFloat("_UDIMDiscardUV", 1); // Use UV1
            
            // Get overlap data from coordinator
            var coordGroup = AutoBodyHiderCoordinator.CoordinatedGroups.ContainsKey(group.bodyMesh) 
                           ? AutoBodyHiderCoordinator.CoordinatedGroups[group.bodyMesh] 
                           : null;
            
            // Configure tiles for each clothing piece
            foreach (var data in group.udimComponents)
            {
                string tilePropertyName = $"_UDIMDiscardRow{data.udimDiscardRow}_{data.udimDiscardColumn}";
                
                if (materialCopy.HasProperty(tilePropertyName))
                {
                    if (data.createToggle)
                    {
                        // For toggles: base = 0 (OFF), animation sets to 1 (ON)
                        materialCopy.SetFloat(tilePropertyName, 0f);
                        materialCopy.SetOverrideTag(tilePropertyName + "Animated", "1");
                    }
                    else
                    {
                        // For non-toggles: always ON
                        materialCopy.SetFloat(tilePropertyName, 1f);
                    }
                    
                    Debug.Log($"[AutoBodyHiderProcessor] Configured individual tile ({data.udimDiscardRow}, {data.udimDiscardColumn}) for '{data.name}' (Toggle: {data.createToggle})");
                }
                else
                {
                    Debug.LogWarning($"[AutoBodyHiderProcessor] Property {tilePropertyName} not found on material");
                }
            }
            
            // Configure overlap tiles
            if (coordGroup != null && coordGroup.overlapRegions.Count > 0)
            {
                Debug.Log($"[AutoBodyHiderProcessor] Configuring {coordGroup.overlapRegions.Count} overlap tiles...");
                
                foreach (var overlap in coordGroup.overlapRegions)
                {
                    if (coordGroup.overlapTiles.ContainsKey(overlap))
                    {
                        var tile = coordGroup.overlapTiles[overlap];
                        string tilePropertyName = $"_UDIMDiscardRow{tile.row}_{tile.col}";
                        
                        if (materialCopy.HasProperty(tilePropertyName))
                        {
                            // Check if ANY of the involved clothing pieces have toggles
                            bool anyHasToggle = overlap.involvedClothing.Any(c => c.createToggle);
                            
                            if (anyHasToggle)
                            {
                                // For toggles: base = 0 (OFF), animation sets to 1 (ON)
                                materialCopy.SetFloat(tilePropertyName, 0f);
                                materialCopy.SetOverrideTag(tilePropertyName + "Animated", "1");
                            }
                            else
                            {
                                // For non-toggles: always ON
                                materialCopy.SetFloat(tilePropertyName, 1f);
                            }
                            
                            Debug.Log($"[AutoBodyHiderProcessor] Configured overlap tile ({tile.row}, {tile.col}) for '{overlap.regionName}' (Toggle: {anyHasToggle})");
                        }
                        else
                        {
                            Debug.LogWarning($"[AutoBodyHiderProcessor] Property {tilePropertyName} not found on material");
                        }
                    }
                }
            }
            
            // Replace the material
            materials[poiyomiMaterialIndex] = materialCopy;
            group.bodyMesh.sharedMaterials = materials;
            
            EditorUtility.SetDirty(materialCopy);
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
            
            CreateUDIMToggle(data, poiyomiMaterial);
        }
    }
}

