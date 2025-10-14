using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using System.Collections.Generic;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Custom editor for Auto Body Hider providing preview visualization and real-time parameter adjustment.
    /// Generates preview data showing which faces will be deleted, updates in real-time when safety margin or symmetry changes.
    /// </summary>
    [CustomEditor(typeof(AutoBodyHiderData))]
    public class AutoBodyHiderDataEditor : UnityEditor.Editor
    {
        private AutoBodyHiderData data;
        private bool isGeneratingPreview = false;
        private float lastCheckedSafetyMargin = -1f;
        private bool lastCheckedMirrorSymmetry = false;
        
        // Cache tracking for settings changes
        private DetectionMethod lastDetectionMethod;
        private float lastProximityThreshold;
        private float lastRaycastDistance;
        private int lastSmartRayDirections;
        private float lastSmartOcclusionThreshold;
        private bool lastSmartUseNormals;
        private bool lastSmartRequireBidirectional;
        private Texture2D lastManualMask;
        private float lastManualMaskThreshold;

        private void OnEnable()
        {
            data = (AutoBodyHiderData)target;
            
            // Initialize cache tracking
            lastDetectionMethod = data.detectionMethod;
            lastProximityThreshold = data.proximityThreshold;
            lastRaycastDistance = data.raycastDistance;
            lastSmartRayDirections = data.smartRayDirections;
            lastSmartOcclusionThreshold = data.smartOcclusionThreshold;
            lastSmartUseNormals = data.smartUseNormals;
            lastSmartRequireBidirectional = data.smartRequireBidirectional;
            lastManualMask = data.manualMask;
            lastManualMaskThreshold = data.manualMaskThreshold;
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Auto Body Hider"));
            
            var container = new IMGUIContainer(() => {
                OnInspectorGUIContent();
            });
            
            root.Add(container);
            return root;
        }

        public override void OnInspectorGUI()
        {
            OnInspectorGUIContent();
        }

        private void OnInspectorGUIContent()
        {
            serializedObject.Update();
            
            // Draw fields manually with conditional visibility
            DrawPropertiesWithConditionalVisibility();
            
            // Check if any detection settings changed that would invalidate the cache
            bool settingsChanged = CheckForSettingsChanges();
            
            if (data.previewGenerated && data.previewRawHiddenVertices != null)
            {
                // If core detection settings changed, clear the entire cache
                if (settingsChanged)
                {
                    Debug.Log("[AutoBodyHider Editor] Detection settings changed, clearing preview cache");
                    ClearPreview();
                }
                else
                {
                    // Only check for safety margin and symmetry changes (these can be updated from cache)
                    bool needsUpdate = false;
                    
                    if (Mathf.Abs(data.safetyMargin - lastCheckedSafetyMargin) > 0.0001f)
                    {
                        needsUpdate = true;
                        lastCheckedSafetyMargin = data.safetyMargin;
                    }
                    
                    if (data.mirrorSymmetry != lastCheckedMirrorSymmetry)
                    {
                        needsUpdate = true;
                        lastCheckedMirrorSymmetry = data.mirrorSymmetry;
                    }
                    
                    if (needsUpdate)
                    {
                        UpdatePreviewFromCache();
                    }
                }
            }

            EditorGUILayout.Space(15);
            
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            
            EditorGUILayout.Space(10);
            
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
            headerStyle.fontSize = 14;
            headerStyle.normal.textColor = Color.cyan;
            EditorGUILayout.LabelField("PREVIEW TOOLS", headerStyle);

            if (data.previewGenerated && data.previewHiddenFaces != null)
            {
                int totalFaces = data.previewTriangles.Length / 3;
                int hiddenFaces = 0;
                foreach (bool hidden in data.previewHiddenFaces)
                {
                    if (hidden) hiddenFaces++;
                }

                EditorGUILayout.HelpBox(
                    $"Preview Generated: {hiddenFaces} / {totalFaces} faces will be deleted\n" +
                    $"({(hiddenFaces * 100f / totalFaces):F1}%)\n\n" +
                    $"VRChat Performance: -{hiddenFaces} tris",
                    MessageType.Info
                );
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No preview generated. Click 'Generate Preview' to visualize what will be deleted.\n" +
                    "Red faces in Scene view = faces that will be deleted at build time.",
                    MessageType.None
                );
            }

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = !isGeneratingPreview && ValidateData();
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button(
                isGeneratingPreview ? "Generating..." : "Generate Preview", 
                GUILayout.Height(40),
                GUILayout.MinWidth(150)))
            {
                Debug.Log("[YUCP Preview] Generate Preview button clicked");
                GeneratePreview();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            GUI.enabled = data.previewGenerated;
            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("Clear Preview", GUILayout.Height(40), GUILayout.MinWidth(100)))
            {
                Debug.Log("[YUCP Preview] Clear Preview button clicked");
                ClearPreview();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(10);
            
            GUI.backgroundColor = new Color(1f, 0.7f, 0.3f);
            if (GUILayout.Button("Clear Detection Cache", GUILayout.Height(30)))
            {
                DetectionCache.ClearCache();
                Debug.Log("[YUCP Preview] Detection cache cleared");
                EditorUtility.DisplayDialog("Cache Cleared", 
                    "Detection cache has been cleared.\n\n" +
                    "Next build will re-run detection for all Auto Body Hider components.", 
                    "OK");
            }
            GUI.backgroundColor = Color.white;

            if (!ValidateData())
            {
                EditorGUILayout.HelpBox(GetValidationError(), MessageType.Warning);
            }
        }

        private void DrawPropertiesWithConditionalVisibility()
        {
            // Target Meshes
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetBodyMesh"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("clothingMesh"));

            EditorGUILayout.Space();

            // Detection Settings
            EditorGUILayout.LabelField("Detection Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("detectionMethod"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("safetyMargin"));

            // Show proximity threshold for methods that use it
            if (data.detectionMethod == DetectionMethod.Proximity || 
                data.detectionMethod == DetectionMethod.Hybrid)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("proximityThreshold"));
            }

            // Show raycast distance for methods that use it
            if (data.detectionMethod == DetectionMethod.Raycast || 
                data.detectionMethod == DetectionMethod.Hybrid || 
                data.detectionMethod == DetectionMethod.Smart)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("raycastDistance"));
            }

            // Show hybrid-specific settings
            if (data.detectionMethod == DetectionMethod.Hybrid)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("hybridExpansionFactor"));
            }

            // Show smart detection settings
            if (data.detectionMethod == DetectionMethod.Smart)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Smart Detection Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("smartRayDirections"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("smartOcclusionThreshold"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("smartUseNormals"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("smartRequireBidirectional"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("smartRayOffset"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("smartConservativeMode"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("smartMinDistanceToClothing"));
            }

            // Show manual mask settings
            if (data.detectionMethod == DetectionMethod.Manual)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Manual Mask (Manual Method Only)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("manualMask"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("manualMaskThreshold"));
            }

            EditorGUILayout.Space();

            // Application Mode
            EditorGUILayout.LabelField("Application Mode", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("applicationMode"));

            // Show UDIM settings only if using UDIM mode
            if (data.applicationMode == ApplicationMode.UDIMDiscard || 
                data.applicationMode == ApplicationMode.AutoDetect)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("UDIM Discard Settings (Poiyomi Only)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("udimUVChannel"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("udimDiscardRow"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("udimDiscardColumn"));

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("UDIM Toggle Settings (Optional)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("createToggle"));

                // Show toggle settings only if createToggle is enabled
                if (data.createToggle)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("debugSaveAnimation"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleType"));
                    
                    // Show help text based on toggle type
                    if (data.toggleType == ToggleType.ObjectToggle)
                    {
                        EditorGUILayout.HelpBox(
                            "Object Toggle: Toggles clothing object ON/OFF + UDIM discard\n" +
                            "• Toggle ON: Clothing visible, body hidden\n" +
                            "• Toggle OFF: Clothing hidden, body visible", 
                            MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            "Hidden Toggle: Only toggles UDIM discard, clothing always visible\n" +
                            "• Toggle ON: Body hidden\n" +
                            "• Toggle OFF: Body visible", 
                            MessageType.Info);
                    }
                    
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleMenuPath"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleGlobalParameter"));
                    
                    // Show info about local vs global parameter control
                    bool hasMenuPath = !string.IsNullOrEmpty(data.toggleMenuPath);
                    bool hasGlobalParam = !string.IsNullOrEmpty(data.toggleGlobalParameter);
                    
                    if (!hasMenuPath && hasGlobalParam)
                    {
                        EditorGUILayout.HelpBox(
                            "Global Parameter Only Mode\n\n" +
                            "No menu item will be created. This clothing's UDIM discard will be controlled " +
                            "only by the global parameter '" + data.toggleGlobalParameter + "'.\n\n" +
                            "Use this for:\n" +
                            "• Outfit groups (all pieces share same parameter)\n" +
                            "• OSC control from external apps\n" +
                            "• Synced outfit switching across players\n\n" +
                            "You'll need to create a separate toggle/driver to control this parameter.",
                            MessageType.Info);
                    }
                    else if (hasMenuPath && hasGlobalParam)
                    {
                        EditorGUILayout.HelpBox(
                            "Menu + Global Sync Mode\n\n" +
                            "Menu toggle: '" + data.toggleMenuPath + "'\n" +
                            "Global parameter: '" + data.toggleGlobalParameter + "'\n\n" +
                            "Menu toggle will control the global parameter, syncing state across all players.",
                            MessageType.Info);
                    }
                    else if (hasMenuPath && !hasGlobalParam)
                    {
                        EditorGUILayout.HelpBox(
                            "Local Toggle Mode (Default)\n\n" +
                            "Menu toggle: '" + data.toggleMenuPath + "'\n" +
                            "Parameter: Auto-generated by VRCFury (prefixed with 'VF##')\n\n" +
                            "Toggle state is local to your client only (not synced).",
                            MessageType.Info);
                    }
                    else // !hasMenuPath && !hasGlobalParam
                    {
                        EditorGUILayout.HelpBox(
                            "Menu path or global parameter is required for toggle functionality!",
                            MessageType.Warning);
                    }
                    
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleSaved"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleDefaultOn"));
                    EditorGUI.indentLevel--;

                    // Show warning if using mesh deletion
                    if (data.applicationMode == ApplicationMode.MeshDeletion)
                    {
                        EditorGUILayout.HelpBox("Toggle only works with UDIM Discard mode, not Mesh Deletion!", MessageType.Warning);
                    }
                }
            }

            EditorGUILayout.Space();

            // Advanced Options
            EditorGUILayout.LabelField("Advanced Options", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("useBoneFiltering"));
            
            if (data.useBoneFiltering)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("filterBones"), true);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("mirrorSymmetry"));

            EditorGUILayout.Space();

            // Multi-Clothing Optimization
            EditorGUILayout.LabelField("Multi-Clothing Optimization", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("optimizeTileUsage"));
            
            if (data.optimizeTileUsage)
            {
                EditorGUILayout.HelpBox(
                    "Tile Usage Optimization Enabled\n\n" +
                    "• Clothing processed from most to least coverage\n" +
                    "• Inner layers fully covered by outer layers won't need overlap tiles\n" +
                    "• Saves significant UDIM tiles for complex layered outfits\n\n" +
                    "Example: Jacket fully covers shirt → no shirt+jacket overlap tile needed\n\n" +
                    "Note: Only affects UDIM Discard mode with multiple clothing pieces.",
                    MessageType.Info);
            }

            EditorGUILayout.Space();

            // Debug & Preview
            EditorGUILayout.LabelField("Debug & Preview", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("debugMode"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("showPreview"));

            serializedObject.ApplyModifiedProperties();
        }

        private bool ValidateData()
        {
            if (data.targetBodyMesh == null) return false;
            if (data.targetBodyMesh.sharedMesh == null) return false;
            if (data.detectionMethod != DetectionMethod.Manual && data.clothingMesh == null) return false;
            if (data.detectionMethod == DetectionMethod.Manual && data.manualMask == null) return false;
            return true;
        }

        private string GetValidationError()
        {
            if (data.targetBodyMesh == null) return "Target Body Mesh is not set.";
            if (data.targetBodyMesh.sharedMesh == null) return "Target Body Mesh has no mesh data.";
            if (data.detectionMethod != DetectionMethod.Manual && data.clothingMesh == null)
                return "Clothing Mesh is required for automatic detection.";
            if (data.detectionMethod == DetectionMethod.Manual && data.manualMask == null)
                return "Manual Mask texture is required for manual detection.";
            return "";
        }

        private void GeneratePreview()
        {
            Debug.Log("[YUCP Preview] Starting preview generation...");
            isGeneratingPreview = true;
            
            try
            {
                if (!ValidateData())
                {
                    Debug.LogError($"[YUCP Preview] Validation failed: {GetValidationError()}");
                    return;
                }
                
                Debug.Log("[YUCP Preview] Validation passed");
                
                GPURaycast.Initialize();
                Debug.Log("[YUCP Preview] GPU initialized");

                Mesh bodyMesh = data.targetBodyMesh.sharedMesh;
                Vector3[] vertices = bodyMesh.vertices;
                Vector3[] normals = bodyMesh.normals;
                
                Debug.Log($"[YUCP Preview] Body mesh has {vertices.Length} vertices");
                
                List<Vector2> uv0List = new List<Vector2>();
                bodyMesh.GetUVs(0, uv0List);
                Vector2[] uv0 = uv0List.ToArray();

                data.previewVertexPositions = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    data.previewVertexPositions[i] = data.targetBodyMesh.transform.TransformPoint(vertices[i]);
                }
                Debug.Log("[YUCP Preview] Generated world positions");

                EditorUtility.DisplayProgressBar("YUCP Preview", "Detecting hidden vertices...", 0.5f);
                Debug.Log($"[YUCP Preview] Running detection with method: {data.detectionMethod}");
                
                data.previewRawHiddenVertices = VertexDetection.DetectHiddenVertices(
                    data,
                    vertices,
                    normals,
                    uv0,
                    null
                );
                
                data.previewLocalVertices = vertices;
                
                data.previewTriangles = bodyMesh.triangles;
                
                Debug.Log("[YUCP Preview] Detection complete - raw data cached");
                
                data.previewHiddenVertices = new bool[data.previewRawHiddenVertices.Length];
                System.Array.Copy(data.previewRawHiddenVertices, data.previewHiddenVertices, data.previewRawHiddenVertices.Length);

                int initialCount = 0;
                foreach (bool h in data.previewHiddenVertices) if (h) initialCount++;
                Debug.Log($"[YUCP Preview DEBUG] Initial hidden vertices: {initialCount}");

                if (data.mirrorSymmetry)
                {
                    Debug.Log("[YUCP Preview] Applying symmetry mirror");
                    data.previewHiddenVertices = ApplySymmetryMirror(data.previewHiddenVertices, vertices);
                    
                    int countAfterMirror = 0;
                    foreach (bool h in data.previewHiddenVertices) if (h) countAfterMirror++;
                    Debug.Log($"[YUCP Preview DEBUG] After symmetry mirror: {countAfterMirror} hidden vertices");
                }

                if (data.safetyMargin > 0.0001f)
                {
                    Debug.Log($"[YUCP Preview] Applying safety margin: {data.safetyMargin}m");
                    data.previewHiddenVertices = ApplySafetyMargin(data.previewHiddenVertices, vertices);
                    
                    int countAfterMargin = 0;
                    foreach (bool h in data.previewHiddenVertices) if (h) countAfterMargin++;
                    Debug.Log($"[YUCP Preview DEBUG] After safety margin: {countAfterMargin} hidden vertices");
                }
                
                data.lastPreviewSafetyMargin = data.safetyMargin;
                data.lastPreviewMirrorSymmetry = data.mirrorSymmetry;
                lastCheckedSafetyMargin = data.safetyMargin;
                lastCheckedMirrorSymmetry = data.mirrorSymmetry;

                CalculateHiddenFaces(data);

                data.previewGenerated = true;
                data.showPreview = true;

                SceneView.RepaintAll();

                EditorUtility.ClearProgressBar();
                
                int totalFaces = data.previewTriangles.Length / 3;
                int hiddenFaces = 0;
                foreach (bool hidden in data.previewHiddenFaces)
                {
                    if (hidden) hiddenFaces++;
                }
                
                Debug.Log($"[YUCP Preview] Preview generated successfully!\n" +
                         $"Total faces: {totalFaces}\n" +
                         $"Hidden faces: {hiddenFaces} ({(hiddenFaces * 100f / totalFaces):F1}%)\n" +
                         $"Look at the Scene view to see red colored faces that will be deleted");
            }
            catch (System.Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[YUCP Preview] Failed to generate preview: {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                isGeneratingPreview = false;
                Repaint();
            }
        }

        private void UpdatePreviewFromCache()
        {
            if (data.previewRawHiddenVertices == null || data.previewLocalVertices == null)
            {
                Debug.LogWarning("[YUCP Preview] Cannot update - no cached data. Generate preview first.");
                return;
            }
            
            Debug.Log("[YUCP Preview] Real-time update: Reapplying post-processing with new parameters");
            
            data.previewHiddenVertices = new bool[data.previewRawHiddenVertices.Length];
            System.Array.Copy(data.previewRawHiddenVertices, data.previewHiddenVertices, data.previewRawHiddenVertices.Length);
            
            if (data.mirrorSymmetry)
            {
                data.previewHiddenVertices = ApplySymmetryMirror(data.previewHiddenVertices, data.previewLocalVertices);
            }
            
            if (data.safetyMargin > 0.0001f)
            {
                data.previewHiddenVertices = ApplySafetyMargin(data.previewHiddenVertices, data.previewLocalVertices);
            }
            
            CalculateHiddenFaces(data);
            
            int totalFaces = data.previewTriangles.Length / 3;
            int hiddenFaces = 0;
            foreach (bool hidden in data.previewHiddenFaces)
            {
                if (hidden) hiddenFaces++;
            }
            
            Debug.Log($"[YUCP Preview] Real-time updated: {hiddenFaces}/{totalFaces} faces hidden " +
                     $"({(hiddenFaces * 100f / totalFaces):F1}%)");
            
            SceneView.RepaintAll();
            Repaint();
        }
        
        private void CalculateHiddenFaces(AutoBodyHiderData data)
        {
            int faceCount = data.previewTriangles.Length / 3;
            data.previewHiddenFaces = new bool[faceCount];
            
            for (int i = 0; i < faceCount; i++)
            {
                int v0 = data.previewTriangles[i * 3];
                int v1 = data.previewTriangles[i * 3 + 1];
                int v2 = data.previewTriangles[i * 3 + 2];
                
                // Match MeshDeleter behavior: face is deleted if ANY vertex is hidden
                // (MeshDeleter only keeps triangles where ALL vertices are visible)
                data.previewHiddenFaces[i] = data.previewHiddenVertices[v0] || 
                                             data.previewHiddenVertices[v1] || 
                                             data.previewHiddenVertices[v2];
            }
        }

        private bool CheckForSettingsChanges()
        {
            bool changed = false;
            
            if (data.detectionMethod != lastDetectionMethod)
            {
                lastDetectionMethod = data.detectionMethod;
                changed = true;
            }
            
            if (Mathf.Abs(data.proximityThreshold - lastProximityThreshold) > 0.0001f)
            {
                lastProximityThreshold = data.proximityThreshold;
                changed = true;
            }
            
            if (Mathf.Abs(data.raycastDistance - lastRaycastDistance) > 0.0001f)
            {
                lastRaycastDistance = data.raycastDistance;
                changed = true;
            }
            
            if (data.smartRayDirections != lastSmartRayDirections)
            {
                lastSmartRayDirections = data.smartRayDirections;
                changed = true;
            }
            
            if (Mathf.Abs(data.smartOcclusionThreshold - lastSmartOcclusionThreshold) > 0.0001f)
            {
                lastSmartOcclusionThreshold = data.smartOcclusionThreshold;
                changed = true;
            }
            
            if (data.smartUseNormals != lastSmartUseNormals)
            {
                lastSmartUseNormals = data.smartUseNormals;
                changed = true;
            }
            
            if (data.smartRequireBidirectional != lastSmartRequireBidirectional)
            {
                lastSmartRequireBidirectional = data.smartRequireBidirectional;
                changed = true;
            }
            
            if (data.manualMask != lastManualMask)
            {
                lastManualMask = data.manualMask;
                changed = true;
            }
            
            if (Mathf.Abs(data.manualMaskThreshold - lastManualMaskThreshold) > 0.0001f)
            {
                lastManualMaskThreshold = data.manualMaskThreshold;
                changed = true;
            }
            
            return changed;
        }
        
        private void ClearPreview()
        {
            data.previewHiddenVertices = null;
            data.previewVertexPositions = null;
            data.previewRawHiddenVertices = null;
            data.previewLocalVertices = null;
            data.previewHiddenFaces = null;
            data.previewTriangles = null;
            data.previewGenerated = false;
            data.showPreview = false;
            lastCheckedSafetyMargin = -1f;
            lastCheckedMirrorSymmetry = false;
            SceneView.RepaintAll();
            Repaint();
            Debug.Log("[YUCP Preview] Cleared preview data");
        }

        private bool[] ApplySymmetryMirror(bool[] hiddenVertices, Vector3[] vertices)
        {
            bool[] result = new bool[hiddenVertices.Length];
            System.Array.Copy(hiddenVertices, result, hiddenVertices.Length);

            for (int i = 0; i < vertices.Length; i++)
            {
                if (hiddenVertices[i])
                {
                    // Find mirror vertex on opposite side
                    Vector3 mirrorPos = new Vector3(-vertices[i].x, vertices[i].y, vertices[i].z);
                    float closestDist = float.MaxValue;
                    int closestIdx = -1;

                    for (int j = 0; j < vertices.Length; j++)
                    {
                        float dist = Vector3.Distance(vertices[j], mirrorPos);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closestIdx = j;
                        }
                    }

                    if (closestIdx != -1 && closestDist < 0.001f)
                    {
                        result[closestIdx] = true;
                    }
                }
            }

            return result;
        }

         private bool[] ApplySafetyMargin(bool[] hiddenVertices, Vector3[] vertices)
         {
             // Safety margin creates a buffer INWARD from edges
             // Higher margin = MORE vertices kept visible (less deletion)
             bool[] shrunk = new bool[hiddenVertices.Length];
             System.Array.Copy(hiddenVertices, shrunk, hiddenVertices.Length);
 
             // Convert to world coordinates to match build-time processor behavior
             Vector3[] worldVertices = new Vector3[vertices.Length];
             for (int i = 0; i < vertices.Length; i++)
             {
                 worldVertices[i] = data.targetBodyMesh.transform.TransformPoint(vertices[i]);
             }
 
             for (int i = 0; i < vertices.Length; i++)
             {
                 if (hiddenVertices[i])
                 {
                     // Check if this hidden vertex is near the edge (close to a visible vertex)
                     bool isNearEdge = false;
                     
                     for (int j = 0; j < vertices.Length; j++)
                     {
                         if (!hiddenVertices[j]) // If j is visible
                         {
                             float dist = Vector3.Distance(worldVertices[i], worldVertices[j]);
                             if (dist < data.safetyMargin)
                             {
                                 // This hidden vertex is within safety margin of a visible vertex
                                 isNearEdge = true;
                                 break;
                             }
                         }
                     }
                     
                     // If near edge, keep it visible (don't hide)
                     if (isNearEdge)
                     {
                         shrunk[i] = false; // Keep visible
                     }
                 }
             }
 
             return shrunk;
         }

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        static void DrawGizmos(AutoBodyHiderData data, GizmoType gizmoType)
        {
            if (!data.showPreview || !data.previewGenerated) return;
            if (data.previewHiddenFaces == null || data.previewTriangles == null) return;
            if (data.previewVertexPositions == null || data.targetBodyMesh == null) return;

            Handles.color = new Color(1f, 0f, 0f, 0.6f);
            
            for (int i = 0; i < data.previewHiddenFaces.Length; i++)
            {
                if (data.previewHiddenFaces[i])
                {
                    int v0 = data.previewTriangles[i * 3];
                    int v1 = data.previewTriangles[i * 3 + 1];
                    int v2 = data.previewTriangles[i * 3 + 2];
                    
                    Vector3 p0 = data.previewVertexPositions[v0];
                    Vector3 p1 = data.previewVertexPositions[v1];
                    Vector3 p2 = data.previewVertexPositions[v2];
                    
                    Handles.DrawAAConvexPolygon(p0, p1, p2);
                }
            }

            Handles.BeginGUI();
            
            int totalFaces = data.previewTriangles.Length / 3;
            int hiddenFaces = 0;
            foreach (bool hidden in data.previewHiddenFaces)
            {
                if (hidden) hiddenFaces++;
            }
            
            GUI.Box(new Rect(10, 10, 280, 90), "");
            
            GUI.Label(new Rect(15, 15, 270, 20), "YUCP Preview", EditorStyles.boldLabel);
            
            GUI.color = new Color(1f, 0f, 0f, 0.6f);
            GUI.Box(new Rect(15, 40, 15, 15), "");
            GUI.color = Color.white;
            GUI.Label(new Rect(35, 38, 230, 20), "= Faces to be deleted");
            
            GUI.Label(new Rect(15, 60, 260, 20), $"{hiddenFaces} / {totalFaces} faces will be deleted");
            GUI.Label(new Rect(15, 75, 260, 20), $"({(hiddenFaces * 100f / totalFaces):F1}%) | VRChat: -{hiddenFaces} tris");
            
            Handles.EndGUI();
        }
    }
}

