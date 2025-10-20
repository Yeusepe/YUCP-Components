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

        // Foldout states
        private bool showSmartDetection = false;
        private bool showAdvancedOptions = false;
        private bool showToggleOptions = false;

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
            
            // Show integration banner if VRCFury Toggle is present
            var vrcFuryComponents = data.GetComponents<Component>();
            Component vrcFuryToggle = null;
            foreach (var comp in vrcFuryComponents)
            {
                if (comp != null && comp.GetType().Name == "VRCFury")
                {
                    vrcFuryToggle = comp;
                    break;
                }
            }
            if (vrcFuryToggle != null)
            {
                EditorGUILayout.Space(5);
                var originalColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.7f, 1f, 0.4f);
                EditorGUILayout.HelpBox(
                    "VRCFury Toggle Integration Detected\n\n" +
                    "This Auto Body Hider will work together with the VRCFury Toggle component. " +
                    "The UDIM discard animation will be added to the toggle's actions automatically during build.",
                    MessageType.Info);
                GUI.backgroundColor = originalColor;
                EditorGUILayout.Space(5);
            }
            
            // Check if any detection settings changed that would invalidate the cache
            bool settingsChanged = CheckForSettingsChanges();
            
            if (data.previewGenerated && data.previewRawHiddenVertices != null)
            {
                if (settingsChanged)
                {
                    Debug.Log("[AutoBodyHider Editor] Detection settings changed, clearing preview cache");
                    ClearPreview();
                }
                else
                {
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

            // Target Meshes
            DrawSection("Target Meshes", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("targetBodyMesh"), new GUIContent("Body Mesh"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("clothingMesh"), new GUIContent("Clothing Mesh"));
            });

            // Detection Settings
            DrawSection("Detection Settings", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("detectionMethod"), new GUIContent("Detection Method"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("safetyMargin"), new GUIContent("Safety Margin"));

                // Method-specific settings
                if (data.detectionMethod == DetectionMethod.Proximity || data.detectionMethod == DetectionMethod.Hybrid)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("proximityThreshold"), new GUIContent("Proximity Threshold"));
                }

                if (data.detectionMethod == DetectionMethod.Raycast || data.detectionMethod == DetectionMethod.Hybrid || data.detectionMethod == DetectionMethod.Smart)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("raycastDistance"), new GUIContent("Raycast Distance"));
                }

                if (data.detectionMethod == DetectionMethod.Hybrid)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("hybridExpansionFactor"), new GUIContent("Expansion Factor"));
                }

                if (data.detectionMethod == DetectionMethod.Manual)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("manualMask"), new GUIContent("Manual Mask"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("manualMaskThreshold"), new GUIContent("Mask Threshold"));
                }
            });

            // Smart Detection Settings (Foldout)
            if (data.detectionMethod == DetectionMethod.Smart)
            {
                EditorGUILayout.Space(5);
                showSmartDetection = EditorGUILayout.BeginFoldoutHeaderGroup(showSmartDetection, "Smart Detection Settings");
                if (showSmartDetection)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("smartRayDirections"), new GUIContent("Ray Directions"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("smartOcclusionThreshold"), new GUIContent("Occlusion Threshold"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("smartUseNormals"), new GUIContent("Use Normals"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("smartRequireBidirectional"), new GUIContent("Require Bidirectional"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("smartRayOffset"), new GUIContent("Ray Offset"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("smartConservativeMode"), new GUIContent("Conservative Mode"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("smartMinDistanceToClothing"), new GUIContent("Min Distance to Clothing"));
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }

            // Application Mode
            DrawSection("Application Mode", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("applicationMode"), new GUIContent("Mode"));
                
                if (data.applicationMode == ApplicationMode.AutoDetect)
                {
                    EditorGUILayout.HelpBox("Auto-detect will use UDIM Discard for Poiyomi/FastFur shaders, Mesh Deletion for others.", MessageType.Info);
                }
            });

            // UDIM Settings (only if UDIM mode)
            if (data.applicationMode == ApplicationMode.UDIMDiscard || data.applicationMode == ApplicationMode.AutoDetect)
            {
                DrawSection("UDIM Discard Settings", () => {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("udimUVChannel"), new GUIContent("UV Channel"));
                    
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("udimDiscardRow"), new GUIContent("Row"), GUILayout.MinWidth(100));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("udimDiscardColumn"), new GUIContent("Column"), GUILayout.MinWidth(100));
                    EditorGUILayout.EndHorizontal();
                });

                // UDIM Toggle Settings
                EditorGUILayout.Space(5);
                var createToggleProp = serializedObject.FindProperty("createToggle");
                
                // Check if UV Discard Toggle or VRCFury Toggle is present
                var uvDiscardToggle = data.GetComponent<UVDiscardToggleData>();
                var hasVRCFuryToggle = vrcFuryToggle != null;
                
                if (uvDiscardToggle != null || hasVRCFuryToggle)
                {
                    GUI.enabled = false;
                    string reason = uvDiscardToggle != null ? "UV Discard Toggle present" : "VRCFury Toggle present";
                    EditorGUILayout.PropertyField(createToggleProp, new GUIContent($"Create Toggle (Disabled - {reason})"));
                    GUI.enabled = true;
                }
                else
                {
                    EditorGUILayout.PropertyField(createToggleProp, new GUIContent("Create Toggle"));
                    
                    // Show helper button to create VRCFury toggle for advanced features
                    if (!createToggleProp.boolValue)
                    {
                        EditorGUILayout.Space(3);
                        GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
                        if (GUILayout.Button("+ Add VRCFury Toggle (Advanced Features)", GUILayout.Height(30)))
                        {
                            CreateVRCFuryToggleComponent();
                        }
                        GUI.backgroundColor = Color.white;
                        EditorGUILayout.HelpBox(
                            "Use VRCFury Toggle for advanced features like blend shapes, material swaps, or multiple animations. " +
                            "The Auto Body Hider will automatically integrate with it.",
                            MessageType.Info);
                    }
                }

                if (createToggleProp.boolValue && uvDiscardToggle == null && !hasVRCFuryToggle)
                {
                    DrawSection("Toggle Configuration", () => {
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleType"), new GUIContent("Toggle Type"));
                        
                        if (data.toggleType == ToggleType.ObjectToggle)
                        {
                            EditorGUILayout.HelpBox("Object Toggle: Toggles clothing + body hiding", MessageType.Info);
                        }
                        else
                        {
                            EditorGUILayout.HelpBox("Hidden Toggle: Only toggles body hiding (clothing always visible)", MessageType.Info);
                        }
                        
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleMenuPath"), new GUIContent("Menu Path"));
                        
                        var toggleSyncedProp = serializedObject.FindProperty("toggleSynced");
                        EditorGUILayout.PropertyField(toggleSyncedProp, new GUIContent("Synced"));
                        
                        if (toggleSyncedProp.boolValue)
                        {
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleParameterName"), new GUIContent("Parameter Name (Optional)"));
                            
                            bool hasMenuPath = !string.IsNullOrEmpty(data.toggleMenuPath);
                            bool hasCustomParam = !string.IsNullOrEmpty(data.toggleParameterName);
                            
                            if (!hasMenuPath && hasCustomParam)
                            {
                                EditorGUILayout.HelpBox($"Parameter Only Mode: Controlled by '{data.toggleParameterName}' (no menu item).", MessageType.Info);
                            }
                            else if (hasCustomParam)
                            {
                                EditorGUILayout.HelpBox($"Custom Synced: Uses parameter '{data.toggleParameterName}' (synced across players).", MessageType.Info);
                            }
                            else
                            {
                                EditorGUILayout.HelpBox("Auto Synced: VRCFury will generate a unique synced parameter name.", MessageType.Info);
                            }
                            
                            EditorGUI.indentLevel--;
                        }
                        else
                        {
                            EditorGUILayout.HelpBox("Local Toggle: State is local to your client only (not synced).", MessageType.Info);
                        }
                        
                        if (string.IsNullOrEmpty(data.toggleMenuPath) && !data.toggleSynced)
                        {
                            EditorGUILayout.HelpBox("Menu path is required for local toggles!", MessageType.Warning);
                        }
                        
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleSaved"), new GUIContent("Saved"), GUILayout.MinWidth(100));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleDefaultOn"), new GUIContent("Default ON"), GUILayout.MinWidth(100));
                        EditorGUILayout.EndHorizontal();
                    });
                    
                    // Advanced Toggle Options (Foldout) - Only show if toggle is enabled
                    EditorGUILayout.Space(5);
                    var showToggleAdvanced = SessionState.GetBool($"AutoBodyHider_ToggleAdvanced_{data.GetInstanceID()}", false);
                    var newShowToggleAdvanced = EditorGUILayout.BeginFoldoutHeaderGroup(showToggleAdvanced, "Advanced Toggle Options");
                    SessionState.SetBool($"AutoBodyHider_ToggleAdvanced_{data.GetInstanceID()}", newShowToggleAdvanced);
                    if (newShowToggleAdvanced)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleSlider"), new GUIContent("Use Slider"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleHoldButton"), new GUIContent("Hold Button"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleExclusiveOffState"), new GUIContent("Exclusive Off State"));
                        
                        EditorGUILayout.Space(3);
                        var enableExclusiveTagProp = serializedObject.FindProperty("toggleEnableExclusiveTag");
                        EditorGUILayout.PropertyField(enableExclusiveTagProp, new GUIContent("Exclusive Tags"));
                        if (enableExclusiveTagProp.boolValue)
                        {
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleExclusiveTag"), new GUIContent("Tag Names"));
                            EditorGUI.indentLevel--;
                        }
                        
                        EditorGUILayout.Space(3);
                        var enableIconProp = serializedObject.FindProperty("toggleEnableIcon");
                        EditorGUILayout.PropertyField(enableIconProp, new GUIContent("Custom Icon"));
                        if (enableIconProp.boolValue)
                        {
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("toggleIcon"), new GUIContent("Icon Texture"));
                            EditorGUI.indentLevel--;
                        }
                        
                        EditorGUILayout.Space(3);
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("debugSaveAnimation"), new GUIContent("Debug: Save Animation"));
                        
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.EndFoldoutHeaderGroup();
                    
                    if (data.applicationMode == ApplicationMode.MeshDeletion)
                    {
                        EditorGUILayout.HelpBox("Toggle only works with UDIM Discard mode, not Mesh Deletion!", MessageType.Warning);
                    }
                    
                    // Check if body mesh has compatible shader
                    if (data.targetBodyMesh != null && data.targetBodyMesh.sharedMaterials != null)
                    {
                        bool hasCompatibleShader = false;
                        foreach (var mat in data.targetBodyMesh.sharedMaterials)
                        {
                            if (UDIMManipulator.IsPoiyomiWithUDIMSupport(mat))
                            {
                                hasCompatibleShader = true;
                                break;
                            }
                        }
                        if (!hasCompatibleShader)
                        {
                            EditorGUILayout.HelpBox("Body mesh needs a Poiyomi or FastFur shader with UDIM support for toggles to work!", MessageType.Warning);
                        }
                    }
                }
                else if (uvDiscardToggle != null)
                {
                    // Show if create toggle is disabled but UV Discard Toggle is present
                    EditorGUILayout.HelpBox(
                        "Toggle disabled: UV Discard Toggle component detected on this object. " +
                        "The UV Discard Toggle will handle the toggle functionality.",
                        MessageType.Info);
                }
            }

            // Advanced Options (Foldout)
            EditorGUILayout.Space(5);
            showAdvancedOptions = EditorGUILayout.BeginFoldoutHeaderGroup(showAdvancedOptions, "Advanced Options");
            if (showAdvancedOptions)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mirrorSymmetry"), new GUIContent("Mirror Symmetry"));
                
                EditorGUILayout.Space(3);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("useBoneFiltering"), new GUIContent("Use Bone Filtering"));
                if (data.useBoneFiltering)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("filterBones"), new GUIContent("Filter Bones"), true);
                    EditorGUI.indentLevel--;
                }
                
                EditorGUILayout.Space(3);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("optimizeTileUsage"), new GUIContent("Optimize Tile Usage"));
                if (data.optimizeTileUsage)
                {
                    EditorGUILayout.HelpBox(
                        "Reduces UDIM tiles for layered outfits by skipping overlap tiles for fully-covered inner layers.",
                        MessageType.Info);
                }
                
                EditorGUILayout.Space(3);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("debugMode"), new GUIContent("Debug Mode"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("showPreview"), new GUIContent("Show Preview"));
                
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Preview Tools
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
                GeneratePreview();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            GUI.enabled = data.previewGenerated;
            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("Clear Preview", GUILayout.Height(40), GUILayout.MinWidth(100)))
            {
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
                EditorUtility.DisplayDialog("Cache Cleared", 
                    "Detection cache has been cleared.\n\nNext build will re-run detection for all Auto Body Hider components.", 
                    "OK");
            }
            GUI.backgroundColor = Color.white;

            // Validation
            EditorGUILayout.Space(10);
            if (!ValidateData())
            {
                EditorGUILayout.HelpBox(GetValidationError(), MessageType.Error);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSection(string title, System.Action content)
        {
            EditorGUILayout.Space(5);
            
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0f, 0f, 0f, 0.1f);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;
            
            if (!string.IsNullOrEmpty(title))
            {
                var style = new GUIStyle(EditorStyles.boldLabel);
                style.alignment = TextAnchor.MiddleLeft;
                EditorGUILayout.LabelField(title, style);
                EditorGUILayout.Space(3);
            }
            
            content?.Invoke();
            
            EditorGUILayout.EndVertical();
        }
        
        private void CreateVRCFuryToggleComponent()
        {
            // Create a VRCFury Toggle component with pre-configured settings
            var toggle = com.vrcfury.api.FuryComponents.CreateToggle(data.gameObject);
            
            // Pre-configure with sensible defaults for body hiding
            toggle.SetMenuPath("Clothing/Hide Body");
            toggle.SetSaved();
            
            // Don't set any actions - the user can add blend shapes, animations, etc. via the VRCFury inspector
            // The Auto Body Hider processor will automatically add the UDIM discard animation during build
            
            EditorUtility.SetDirty(data.gameObject);
            
            Debug.Log($"[AutoBodyHider] Created VRCFury Toggle component on '{data.gameObject.name}'", data);
            
            // Show message to user
            EditorUtility.DisplayDialog(
                "VRCFury Toggle Created",
                "A VRCFury Toggle component has been added to this object with default settings:\n\n" +
                "• Menu Path: Clothing/Hide Body\n" +
                "• Saved: Yes\n" +
                "• Local (not synced)\n\n" +
                "You can now:\n" +
                "1. Configure the toggle settings in the VRCFury component below\n" +
                "2. Add actions like blend shapes, object toggles, animations, etc.\n" +
                "3. The UDIM discard animation will be added automatically during build\n\n" +
                "The 'Create Toggle' option in Auto Body Hider is now disabled.",
                "OK"
            );
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
                
                GPURaycast.Initialize();

                Mesh bodyMesh = data.targetBodyMesh.sharedMesh;
                Vector3[] vertices = bodyMesh.vertices;
                Vector3[] normals = bodyMesh.normals;
                
                List<Vector2> uv0List = new List<Vector2>();
                bodyMesh.GetUVs(0, uv0List);
                Vector2[] uv0 = uv0List.ToArray();

                data.previewVertexPositions = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    data.previewVertexPositions[i] = data.targetBodyMesh.transform.TransformPoint(vertices[i]);
                }

                EditorUtility.DisplayProgressBar("YUCP Preview", "Detecting hidden vertices...", 0.5f);
                
                data.previewRawHiddenVertices = VertexDetection.DetectHiddenVertices(
                    data,
                    vertices,
                    normals,
                    uv0,
                    null
                );
                
                data.previewLocalVertices = vertices;
                data.previewTriangles = bodyMesh.triangles;
                
                data.previewHiddenVertices = new bool[data.previewRawHiddenVertices.Length];
                System.Array.Copy(data.previewRawHiddenVertices, data.previewHiddenVertices, data.previewRawHiddenVertices.Length);

                if (data.mirrorSymmetry)
                {
                    data.previewHiddenVertices = ApplySymmetryMirror(data.previewHiddenVertices, vertices);
                }

                if (data.safetyMargin > 0.0001f)
                {
                    data.previewHiddenVertices = ApplySafetyMargin(data.previewHiddenVertices, vertices);
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
                
                Debug.Log($"[YUCP Preview] Preview generated: {hiddenFaces}/{totalFaces} faces hidden ({(hiddenFaces * 100f / totalFaces):F1}%)");
            }
            catch (System.Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[YUCP Preview] Failed: {e.Message}");
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
            if (data.previewRawHiddenVertices == null || data.previewLocalVertices == null) return;
            
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
                
                data.previewHiddenFaces[i] = data.previewHiddenVertices[v0] || 
                                             data.previewHiddenVertices[v1] || 
                                             data.previewHiddenVertices[v2];
            }
        }

        private bool CheckForSettingsChanges()
        {
            bool changed = false;
            
            if (data.detectionMethod != lastDetectionMethod) { lastDetectionMethod = data.detectionMethod; changed = true; }
            if (Mathf.Abs(data.proximityThreshold - lastProximityThreshold) > 0.0001f) { lastProximityThreshold = data.proximityThreshold; changed = true; }
            if (Mathf.Abs(data.raycastDistance - lastRaycastDistance) > 0.0001f) { lastRaycastDistance = data.raycastDistance; changed = true; }
            if (data.smartRayDirections != lastSmartRayDirections) { lastSmartRayDirections = data.smartRayDirections; changed = true; }
            if (Mathf.Abs(data.smartOcclusionThreshold - lastSmartOcclusionThreshold) > 0.0001f) { lastSmartOcclusionThreshold = data.smartOcclusionThreshold; changed = true; }
            if (data.smartUseNormals != lastSmartUseNormals) { lastSmartUseNormals = data.smartUseNormals; changed = true; }
            if (data.smartRequireBidirectional != lastSmartRequireBidirectional) { lastSmartRequireBidirectional = data.smartRequireBidirectional; changed = true; }
            if (data.manualMask != lastManualMask) { lastManualMask = data.manualMask; changed = true; }
            if (Mathf.Abs(data.manualMaskThreshold - lastManualMaskThreshold) > 0.0001f) { lastManualMaskThreshold = data.manualMaskThreshold; changed = true; }
            
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
        }

        private bool[] ApplySymmetryMirror(bool[] hiddenVertices, Vector3[] vertices)
        {
            bool[] result = new bool[hiddenVertices.Length];
            System.Array.Copy(hiddenVertices, result, hiddenVertices.Length);

            for (int i = 0; i < vertices.Length; i++)
            {
                if (hiddenVertices[i])
                {
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
