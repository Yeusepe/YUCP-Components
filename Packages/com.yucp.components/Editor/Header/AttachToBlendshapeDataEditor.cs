using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using YUCP.Components.Editor.Utils;

namespace YUCP.Components.Resources
{
    /// <summary>
    /// Custom editor for Attach to Blendshape component.
    /// Provides preview visualization, blendshape list, and configuration UI
    /// </summary>
    [CustomEditor(typeof(AttachToBlendshapeData))]
    public class AttachToBlendshapeDataEditor : UnityEditor.Editor
    {
        private AttachToBlendshapeData data;
        
        // Foldout states
        private bool showTargetSettings = true;
        private bool showBlendshapeTracking = true;
        private bool showSurfaceCluster = false;
        private bool showSolverConfiguration = true;
        private bool showBoneAttachment = false;
        private bool showAnimationSettings = false;
        private bool showAdvancedOptions = false;
        private bool showBuildStats = false;
        
        private Vector2 blendshapeScrollPos;

        // Cache to prevent recalculation spam
        private List<string> cachedDetectedVisemes;
        private int cachedVisemeFrameCount = -1;

        private void OnEnable()
        {
            data = (AttachToBlendshapeData)target;
            LoadFoldoutStates();
        }

        private void OnDisable()
        {
            SaveFoldoutStates();
        }

        private void LoadFoldoutStates()
        {
            int id = data.GetInstanceID();
            showTargetSettings = SessionState.GetBool($"AttachBlendshape_Target_{id}", true);
            showBlendshapeTracking = SessionState.GetBool($"AttachBlendshape_Tracking_{id}", true);
            showSurfaceCluster = SessionState.GetBool($"AttachBlendshape_Cluster_{id}", false);
            showSolverConfiguration = SessionState.GetBool($"AttachBlendshape_Solver_{id}", true);
            showBoneAttachment = SessionState.GetBool($"AttachBlendshape_Bone_{id}", false);
            showAnimationSettings = SessionState.GetBool($"AttachBlendshape_Anim_{id}", false);
            showAdvancedOptions = SessionState.GetBool($"AttachBlendshape_Advanced_{id}", false);
            showBuildStats = SessionState.GetBool($"AttachBlendshape_Stats_{id}", false);
        }

        private void SaveFoldoutStates()
        {
            int id = data.GetInstanceID();
            SessionState.SetBool($"AttachBlendshape_Target_{id}", showTargetSettings);
            SessionState.SetBool($"AttachBlendshape_Tracking_{id}", showBlendshapeTracking);
            SessionState.SetBool($"AttachBlendshape_Cluster_{id}", showSurfaceCluster);
            SessionState.SetBool($"AttachBlendshape_Solver_{id}", showSolverConfiguration);
            SessionState.SetBool($"AttachBlendshape_Bone_{id}", showBoneAttachment);
            SessionState.SetBool($"AttachBlendshape_Anim_{id}", showAnimationSettings);
            SessionState.SetBool($"AttachBlendshape_Advanced_{id}", showAdvancedOptions);
            SessionState.SetBool($"AttachBlendshape_Stats_{id}", showBuildStats);
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(YUCPComponentHeader.CreateHeaderOverlay("Attach to Blendshape"));

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
            data = (AttachToBlendshapeData)target;

            // Validation banner at top
            DrawValidationBanner();

            EditorGUILayout.Space(5);

            // Target Mesh Section
            showTargetSettings = DrawFoldoutSection("Target Mesh Configuration", showTargetSettings, () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("targetMesh"), new GUIContent("Target Mesh"));

                if (data.targetMesh != null)
                {
                    EditorGUILayout.Space(3);
                    
                    if (!PoseSampler.HasBlendshapes(data.targetMesh))
                    {
                        var errorColor = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(1f, 0.3f, 0.3f, 0.3f);
                        EditorGUILayout.HelpBox("Target mesh has no blendshapes!", MessageType.Error);
                        GUI.backgroundColor = errorColor;
                    }
                    else
                    {
                        int blendshapeCount = data.targetMesh.sharedMesh.blendShapeCount;
                        var infoColor = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f, 0.2f);
                        EditorGUILayout.HelpBox($"Found {blendshapeCount} blendshape{(blendshapeCount != 1 ? "s" : "")} on target mesh", MessageType.Info);
                        GUI.backgroundColor = infoColor;
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Select a SkinnedMeshRenderer with blendshapes", MessageType.None);
                }
            });

            // Blendshape Tracking Section
            EditorGUILayout.Space(5);
            showBlendshapeTracking = DrawFoldoutSection("Blendshape Tracking", showBlendshapeTracking, () => {
                // Mode selector buttons (visual)
                DrawTrackingModeSelector();

                EditorGUILayout.Space(8);

                // Mode-specific UI
                switch (data.trackingMode)
                {
                    case BlendshapeTrackingMode.All:
                        var allColor = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f, 0.2f);
                        EditorGUILayout.HelpBox("All blendshapes on the target mesh will be tracked.\n\nGood for: Complete facial tracking", MessageType.Info);
                        GUI.backgroundColor = allColor;
                        break;

                    case BlendshapeTrackingMode.Specific:
                        DrawSpecificBlendshapesList();
                        break;

                    case BlendshapeTrackingMode.VisemsOnly:
                        var visemeColor = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.5f, 0.8f, 0.5f, 0.2f);
                        EditorGUILayout.HelpBox("Only VRChat viseme blendshapes will be tracked.\n\nGood for: Lip piercings, mouth jewelry", MessageType.Info);
                        GUI.backgroundColor = visemeColor;
                        DrawDetectedVisemes();
                        break;

                    case BlendshapeTrackingMode.Smart:
                        var smartColor = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.8f, 0.5f, 1f, 0.2f);
                        EditorGUILayout.HelpBox("Automatically detects blendshapes that move this attachment.\n\nGood for: Optimal performance, localized areas", MessageType.Info);
                        GUI.backgroundColor = smartColor;
                        
                        EditorGUILayout.Space(3);
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("smartDetectionThreshold"), 
                            new GUIContent("Detection Threshold", "Minimum displacement in meters"));
                        break;
                }
            });

            // Surface Cluster Section
            EditorGUILayout.Space(5);
            showSurfaceCluster = DrawFoldoutSection("Surface Cluster Settings", showSurfaceCluster, () => {
                EditorGUILayout.HelpBox("Surface cluster uses multiple triangles for stable attachment during deformation.", MessageType.None);
                
                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("clusterTriangleCount"), 
                    new GUIContent("Triangle Count", "1-8 triangles, more = more stable"));
                
                EditorGUILayout.PropertyField(serializedObject.FindProperty("searchRadius"), 
                    new GUIContent("Search Radius", "Detection radius in meters, 0 = unlimited"));
                
                EditorGUILayout.Space(3);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("manualTriangleIndex"), 
                    new GUIContent("Manual Triangle", "Leave -1 for auto-detect"));

                if (data.manualTriangleIndex >= 0)
                {
                    var manualColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(1f, 0.8f, 0.3f, 0.2f);
                    EditorGUILayout.HelpBox($"Manual mode: Using triangle #{data.manualTriangleIndex} as primary anchor", MessageType.Info);
                    GUI.backgroundColor = manualColor;
                }
            });

            // Solver Configuration Section
            EditorGUILayout.Space(5);
            showSolverConfiguration = DrawFoldoutSection("Solver Configuration", showSolverConfiguration, () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("solverMode"), 
                    new GUIContent("Solver Mode"));
                
                EditorGUILayout.Space(5);
                DrawSolverModeCard(data.solverMode);

                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("alignRotationToSurface"), 
                    new GUIContent("Align to Surface", "Rotate object with surface normal"));

                // Mode-specific settings
                if (data.solverMode == SolverMode.RigidNormalOffset)
                {
                    EditorGUILayout.Space(5);
                    DrawSubSection("Normal Offset Settings", () => {
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("normalOffset"), 
                            new GUIContent("Offset Distance", "Distance to push outward"));
                    });
                }

                if (data.solverMode == SolverMode.CageRBF)
                {
                    EditorGUILayout.Space(5);
                    DrawSubSection("RBF Deformation Settings", () => {
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("rbfDriverPointCount"), 
                            new GUIContent("Driver Points", "3-16 points on surface"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("rbfRadiusMultiplier"), 
                            new GUIContent("Radius Multiplier", "Influence range"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("useGPUAcceleration"), 
                            new GUIContent("GPU Acceleration", "Faster for large meshes"));
                    });
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("rotationSmoothingFactor"), 
                    new GUIContent("Rotation Smoothing", "Prevents orientation flips"));
            });

            // Bone Attachment Section
            EditorGUILayout.Space(5);
            showBoneAttachment = DrawFoldoutSection("Bone Attachment (Base Positioning)", showBoneAttachment, () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("attachToClosestBone"), 
                    new GUIContent("Enable Bone Attachment", "Links object to nearest bone"));

                if (data.attachToClosestBone)
                {
                    EditorGUILayout.Space(5);
                    DrawSubSection("Bone Detection Settings", () => {
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("boneSearchRadius"), 
                            new GUIContent("Search Radius", "Max distance to bones"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("boneNameFilter"), 
                            new GUIContent("Name Filter", "Only bones containing text"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("ignoreHumanoidBones"), 
                            new GUIContent("Ignore Humanoid Bones", "Only use extra bones"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("boneOffset"), 
                            new GUIContent("Bone Offset Path", "Optional offset string"));
                    });

                    EditorGUILayout.Space(3);
                    var boneInfoColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.5f, 0.8f, 0.9f, 0.2f);
                    EditorGUILayout.HelpBox("Bone attachment provides base positioning.\nBlendshape animations are applied relative to the bone.", MessageType.None);
                    GUI.backgroundColor = boneInfoColor;
                }
                else
                {
                    EditorGUILayout.HelpBox("Bone attachment disabled - object will stay in place without base bone link.", MessageType.Warning);
                }
            });

            // Animation Generation Section
            EditorGUILayout.Space(5);
            showAnimationSettings = DrawFoldoutSection("Animation Generation", showAnimationSettings, () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("createDirectAnimations"), 
                    new GUIContent("Create Animation Assets", "Save animations to Assets/Generated"));
                
                if (data.createDirectAnimations)
                {
                    var infoColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.5f, 0.8f, 1f, 0.2f);
                    EditorGUILayout.HelpBox("Animations will be saved to Assets/Generated/AttachToBlendshape/\n\n" +
                        "You'll need to manually wire these to your FX layer or use VRCFury's Direct Tree Controller.", MessageType.Info);
                    GUI.backgroundColor = infoColor;
                }
                
                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("samplesPerBlendshape"), 
                    new GUIContent("Samples Per Blendshape", "2-10 keyframes per animation"));
                
                EditorGUILayout.Space(3);
                int samples = data.samplesPerBlendshape;
                int estimatedKeyframes = samples * 7; // position xyz + rotation xyzw
                var samplingColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.7f, 0.7f, 0.9f, 0.2f);
                EditorGUILayout.HelpBox($"{samples} samples = ~{estimatedKeyframes} keyframes per blendshape\n" +
                    $"More samples = smoother animation but larger file size", MessageType.None);
                GUI.backgroundColor = samplingColor;
            });

            // Advanced Options Section
            EditorGUILayout.Space(5);
            showAdvancedOptions = DrawFoldoutSection("Advanced Options", showAdvancedOptions, () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("debugSaveAnimations"), 
                    new GUIContent("Save Animations", "Export as .anim files in Assets/Generated"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("debugMode"), 
                    new GUIContent("Debug Logging", "Detailed console output"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("showPreview"), 
                    new GUIContent("Show Preview Gizmos", "Visualize in Scene view"));
            });

            // Preview Tools
            EditorGUILayout.Space(15);
            DrawPreviewToolsSection();

            // Build Statistics
            if (data.TrackedBlendshapes != null && data.TrackedBlendshapes.Count > 0)
            {
                EditorGUILayout.Space(5);
                showBuildStats = DrawFoldoutSection("Build Statistics", showBuildStats, () => {
                    var statsColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f, 0.2f);
                    
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUI.backgroundColor = statsColor;
                    
                    GUI.enabled = false;
                    EditorGUILayout.LabelField("Last Build Results:", EditorStyles.boldLabel);
                    EditorGUILayout.Space(3);
                    
                    EditorGUILayout.LabelField($"  - Tracked Blendshapes: {data.TrackedBlendshapes.Count}");
                    EditorGUILayout.LabelField($"  - Generated Animations: {data.GeneratedAnimationCount}");
                    EditorGUILayout.LabelField($"  - Selected Bone: {(string.IsNullOrEmpty(data.SelectedBonePath) ? "None" : data.SelectedBonePath)}");
                    
                    if (data.DetectedCluster != null)
                    {
                        EditorGUILayout.LabelField($"  - Cluster Triangles: {data.DetectedCluster.anchors.Count}");
                        EditorGUILayout.LabelField($"  - Cluster Center: {data.DetectedCluster.centerPosition.ToString("F3")}");
                    }
                    
                    GUI.enabled = true;
                    EditorGUILayout.EndVertical();
                });
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ========== UI HELPER METHODS ==========

        private void DrawValidationBanner()
        {
            if (!ValidateData())
            {
                EditorGUILayout.Space(5);
                var errorColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
                
                EditorGUILayout.HelpBox($"Configuration Error\n\n{GetValidationError()}", MessageType.Error);
                
                GUI.backgroundColor = errorColor;
                EditorGUILayout.Space(5);
            }
        }

        private bool DrawFoldoutSection(string title, bool foldout, System.Action content)
        {
            EditorGUILayout.Space(2);
            
            var rect = EditorGUILayout.GetControlRect(false, 25);
            var boxRect = new Rect(rect.x - 2, rect.y, rect.width + 4, rect.height);
            
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            GUI.Box(boxRect, "", EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;
            
            var foldoutRect = new Rect(rect.x + 5, rect.y + 4, rect.width - 10, 16);
            var style = new GUIStyle(EditorStyles.foldout);
            style.fontStyle = FontStyle.Bold;
            style.fontSize = 12;
            
            bool newFoldout = EditorGUI.Foldout(foldoutRect, foldout, title, true, style);
            
            if (newFoldout)
            {
                EditorGUILayout.Space(2);
                
                var contentColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0f, 0f, 0f, 0.1f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.backgroundColor = contentColor;
                
                EditorGUILayout.Space(5);
                content?.Invoke();
                EditorGUILayout.Space(5);
                
                EditorGUILayout.EndVertical();
            }
            
            return newFoldout;
        }

        private void DrawSubSection(string title, System.Action content)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.5f);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;
            
            if (!string.IsNullOrEmpty(title))
            {
                var style = new GUIStyle(EditorStyles.label);
                style.fontStyle = FontStyle.Bold;
                style.fontSize = 11;
                EditorGUILayout.LabelField(title, style);
                EditorGUILayout.Space(3);
            }
            
            content?.Invoke();
            
            EditorGUILayout.EndVertical();
        }

        private void DrawTrackingModeSelector()
        {
            EditorGUILayout.LabelField("Select Tracking Mode:", EditorStyles.boldLabel);
            EditorGUILayout.Space(3);
            
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fixedHeight = 35;
            buttonStyle.wordWrap = true;
            buttonStyle.alignment = TextAnchor.MiddleCenter;
            buttonStyle.fontSize = 10;
            
            GUIStyle selectedStyle = new GUIStyle(buttonStyle);
            selectedStyle.fontStyle = FontStyle.Bold;
            selectedStyle.normal.textColor = Color.white;
            
            EditorGUILayout.BeginHorizontal();
            
            // All mode
            var allColor = GUI.backgroundColor;
            GUI.backgroundColor = data.trackingMode == BlendshapeTrackingMode.All ? new Color(0.212f, 0.749f, 0.694f) : new Color(0.3f, 0.3f, 0.3f);
            if (GUILayout.Button("All\nBlendshapes", data.trackingMode == BlendshapeTrackingMode.All ? selectedStyle : buttonStyle))
            {
                data.trackingMode = BlendshapeTrackingMode.All;
                EditorUtility.SetDirty(data);
            }
            GUI.backgroundColor = allColor;
            
            // Specific mode
            GUI.backgroundColor = data.trackingMode == BlendshapeTrackingMode.Specific ? new Color(1f, 0.7f, 0.3f) : new Color(0.3f, 0.3f, 0.3f);
            if (GUILayout.Button("Specific\nList", data.trackingMode == BlendshapeTrackingMode.Specific ? selectedStyle : buttonStyle))
            {
                data.trackingMode = BlendshapeTrackingMode.Specific;
                EditorUtility.SetDirty(data);
            }
            GUI.backgroundColor = allColor;
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            
            // Visemes mode
            GUI.backgroundColor = data.trackingMode == BlendshapeTrackingMode.VisemsOnly ? new Color(0.5f, 0.8f, 0.5f) : new Color(0.3f, 0.3f, 0.3f);
            if (GUILayout.Button("Visemes\nOnly", data.trackingMode == BlendshapeTrackingMode.VisemsOnly ? selectedStyle : buttonStyle))
            {
                data.trackingMode = BlendshapeTrackingMode.VisemsOnly;
                EditorUtility.SetDirty(data);
            }
            GUI.backgroundColor = allColor;
            
            // Smart mode
            GUI.backgroundColor = data.trackingMode == BlendshapeTrackingMode.Smart ? new Color(0.8f, 0.5f, 1f) : new Color(0.3f, 0.3f, 0.3f);
            if (GUILayout.Button("Smart\nDetect", data.trackingMode == BlendshapeTrackingMode.Smart ? selectedStyle : buttonStyle))
            {
                data.trackingMode = BlendshapeTrackingMode.Smart;
                EditorUtility.SetDirty(data);
            }
            GUI.backgroundColor = allColor;
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSolverModeCard(SolverMode mode)
        {
            var cardColor = GUI.backgroundColor;
            
            switch (mode)
            {
                case SolverMode.Rigid:
                    GUI.backgroundColor = new Color(0.5f, 0.7f, 1f, 0.2f);
                    break;
                case SolverMode.RigidNormalOffset:
                    GUI.backgroundColor = new Color(0.7f, 0.8f, 0.5f, 0.2f);
                    break;
                case SolverMode.Affine:
                    GUI.backgroundColor = new Color(1f, 0.7f, 0.5f, 0.2f);
                    break;
                case SolverMode.CageRBF:
                    GUI.backgroundColor = new Color(0.8f, 0.5f, 1f, 0.2f);
                    break;
            }
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = cardColor;
            
            string title = "";
            string description = "";
            string goodFor = "";
            
            switch (mode)
            {
                case SolverMode.Rigid:
                    title = "Rigid Transform";
                    description = "Simple rotation + translation";
                    goodFor = "Piercings, small badges, hard jewelry";
                    break;
                case SolverMode.RigidNormalOffset:
                    title = "Rigid + Normal Offset";
                    description = "Rigid with outward push along surface normal";
                    goodFor = "Raised jewelry, studs, embellishments";
                    break;
                case SolverMode.Affine:
                    title = "Affine Transform";
                    description = "Allows minor shear/scale to match skin stretch";
                    goodFor = "Stickers, patches, wider decorations";
                    break;
                case SolverMode.CageRBF:
                    title = "Cage/RBF Deformation";
                    description = "Smooth deformation using driver points (advanced)";
                    goodFor = "Masks, large patches, complex meshes";
                    break;
            }
            
            var titleStyle = new GUIStyle(EditorStyles.label);
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.fontSize = 11;
            EditorGUILayout.LabelField(title, titleStyle);
            
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            
            EditorGUILayout.Space(2);
            var goodForStyle = new GUIStyle(EditorStyles.miniLabel);
            goodForStyle.normal.textColor = new Color(0.7f, 0.9f, 0.7f);
            EditorGUILayout.LabelField($"Good for: {goodFor}", goodForStyle);
            
            EditorGUILayout.EndVertical();
        }

        private void DrawPreviewToolsSection()
        {
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.Space(5);

            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
            headerStyle.fontSize = 13;
            headerStyle.normal.textColor = new Color(0.212f, 0.749f, 0.694f);
            EditorGUILayout.LabelField("PREVIEW & DETECTION", headerStyle);
            
            EditorGUILayout.Space(8);

            DrawPreviewStatusBox();
        }

        private void DrawPreviewStatusBox()
        {
            var boxColor = GUI.backgroundColor;
            
            if (data.previewGenerated && data.previewCluster != null)
            {
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f, 0.2f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.backgroundColor = boxColor;
                
                EditorGUILayout.LabelField("Preview Generated", EditorStyles.boldLabel);
                EditorGUILayout.Space(3);
                
                EditorGUILayout.LabelField($"Surface Cluster:", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"  - {data.previewCluster.anchors.Count} triangles", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"  - Center: {data.previewCluster.centerPosition.ToString("F3")}", EditorStyles.miniLabel);
                
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField($"Detected Blendshapes: {data.previewBlendshapes.Count}", EditorStyles.miniLabel);
                
                if (data.previewBlendshapes.Count > 0 && data.previewBlendshapes.Count <= 20)
                {
                    EditorGUILayout.Space(3);
                    blendshapeScrollPos = EditorGUILayout.BeginScrollView(blendshapeScrollPos, GUILayout.Height(100));
                    foreach (string name in data.previewBlendshapes)
                    {
                        EditorGUILayout.LabelField($"  - {name}", EditorStyles.miniLabel);
                    }
                    EditorGUILayout.EndScrollView();
                }
                else if (data.previewBlendshapes.Count > 20)
                {
                    EditorGUILayout.LabelField($"  (Too many to display - {data.previewBlendshapes.Count} total)", EditorStyles.miniLabel);
                }
                
                EditorGUILayout.EndVertical();
            }
            else
            {
                GUI.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.backgroundColor = boxColor;
                
                EditorGUILayout.LabelField("Preview Not Generated", EditorStyles.boldLabel);
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Click 'Generate Preview' to:", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("  - Detect surface cluster", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("  - Find relevant blendshapes", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("  - Visualize in Scene view", EditorStyles.miniLabel);
                
                EditorGUILayout.EndVertical();
            }
            
            EditorGUILayout.Space(8);
            
            // Preview buttons
            EditorGUILayout.BeginHorizontal();
            
            GUI.enabled = ValidateData();
            var previewBtnColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.9f, 0.4f);
            
            GUIStyle btnStyle = new GUIStyle(GUI.skin.button);
            btnStyle.fontStyle = FontStyle.Bold;
            btnStyle.fontSize = 11;
            
            if (GUILayout.Button("Generate Preview", btnStyle, GUILayout.Height(40)))
            {
                GeneratePreview();
            }
            GUI.backgroundColor = previewBtnColor;
            GUI.enabled = true;
            
            GUI.enabled = data.previewGenerated;
            GUI.backgroundColor = new Color(1f, 0.5f, 0.3f);
            if (GUILayout.Button("Clear", btnStyle, GUILayout.Height(40), GUILayout.Width(80)))
            {
                ClearPreview();
            }
            GUI.backgroundColor = previewBtnColor;
            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSpecificBlendshapesList()
        {
            EditorGUILayout.Space(5);
            
            if (data.targetMesh != null && data.targetMesh.sharedMesh != null)
            {
                Mesh mesh = data.targetMesh.sharedMesh;
                
                var infoColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.5f, 0.7f, 0.9f, 0.2f);
                EditorGUILayout.HelpBox($"Target mesh has {mesh.blendShapeCount} blendshapes available", MessageType.None);
                GUI.backgroundColor = infoColor;
                
                EditorGUILayout.Space(3);
                
                // Add blendshape button
                var btnColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.4f, 0.8f, 0.5f);
                if (GUILayout.Button("+ Add Blendshape from List", GUILayout.Height(28)))
                {
                    ShowBlendshapeSelectionMenu(mesh);
                }
                GUI.backgroundColor = btnColor;
            }
            else
            {
                EditorGUILayout.HelpBox("Assign target mesh to select blendshapes", MessageType.Warning);
            }

            EditorGUILayout.Space(5);

            // List current specific blendshapes with styled boxes
            if (data.specificBlendshapes.Count > 0)
            {
                EditorGUILayout.LabelField($"Selected Blendshapes ({data.specificBlendshapes.Count}):", EditorStyles.boldLabel);
                EditorGUILayout.Space(3);
                
                var listColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0f, 0f, 0f, 0.2f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.backgroundColor = listColor;
                
                for (int i = 0; i < data.specificBlendshapes.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    
                    // Index
                    var indexStyle = new GUIStyle(EditorStyles.label);
                    indexStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
                    EditorGUILayout.LabelField($"{i + 1}.", indexStyle, GUILayout.Width(25));
                    
                    // Blendshape name
                    data.specificBlendshapes[i] = EditorGUILayout.TextField(data.specificBlendshapes[i]);
                    
                    // Remove button
                    var removeColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
                    if (GUILayout.Button("×", GUILayout.Width(25), GUILayout.Height(18)))
                    {
                        data.specificBlendshapes.RemoveAt(i);
                        EditorUtility.SetDirty(data);
                        break;
                    }
                    GUI.backgroundColor = removeColor;
                    
                    EditorGUILayout.EndHorizontal();
                    
                    if (i < data.specificBlendshapes.Count - 1)
                    {
                        EditorGUILayout.Space(2);
                    }
                }
                
                EditorGUILayout.EndVertical();
            }
            else
            {
                var warningColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.8f, 0.3f, 0.2f);
                EditorGUILayout.HelpBox("No blendshapes selected.\nClick 'Add Blendshape from List' to choose specific blendshapes to track.", MessageType.Warning);
                GUI.backgroundColor = warningColor;
            }
        }

        private void ShowBlendshapeSelectionMenu(Mesh mesh)
        {
            GenericMenu menu = new GenericMenu();

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string blendshapeName = mesh.GetBlendShapeName(i);
                bool alreadyAdded = data.specificBlendshapes.Contains(blendshapeName);

                if (alreadyAdded)
                {
                    menu.AddDisabledItem(new GUIContent($"{blendshapeName} (already added)"));
                }
                else
                {
                    menu.AddItem(new GUIContent(blendshapeName), false, () => {
                        data.specificBlendshapes.Add(blendshapeName);
                        EditorUtility.SetDirty(data);
                    });
                }
            }

            menu.ShowAsContext();
        }

        private void DrawDetectedVisemes()
        {
            if (data.targetMesh == null || data.targetMesh.sharedMesh == null)
            {
                return;
            }

            EditorGUILayout.Space(5);

            // Cache viseme detection to prevent recalculation every frame
            int currentFrame = Time.frameCount;
            List<string> detectedVisemes;

            if (cachedDetectedVisemes != null && cachedVisemeFrameCount == currentFrame)
            {
                detectedVisemes = cachedDetectedVisemes;
            }
            else
            {
                // Try to find avatar root for descriptor-based detection
                GameObject avatarRoot = null;
                var avatarDescriptor = data.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                if (avatarDescriptor != null)
                {
                    avatarRoot = avatarDescriptor.gameObject;
                }

                detectedVisemes = VRChatVisemeDetector.GetVisemeBlendshapes(data.targetMesh, avatarRoot);
                cachedDetectedVisemes = detectedVisemes;
                cachedVisemeFrameCount = currentFrame;
            }

            if (detectedVisemes.Count > 0)
            {
                var listColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0f, 0f, 0f, 0.15f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.backgroundColor = listColor;
                
                EditorGUILayout.LabelField($"Detected Visemes ({detectedVisemes.Count}):", EditorStyles.boldLabel);
                EditorGUILayout.Space(3);
                
                int maxDisplay = 15;
                int displayed = Mathf.Min(detectedVisemes.Count, maxDisplay);
                
                for (int i = 0; i < displayed; i++)
                {
                    EditorGUILayout.LabelField($"  - {detectedVisemes[i]}", EditorStyles.miniLabel);
                }
                
                if (detectedVisemes.Count > maxDisplay)
                {
                    EditorGUILayout.LabelField($"  ... and {detectedVisemes.Count - maxDisplay} more", EditorStyles.miniLabel);
                }
                
                EditorGUILayout.EndVertical();
            }
            else
            {
                var warningColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.6f, 0.3f, 0.2f);
                EditorGUILayout.HelpBox("No viseme blendshapes detected.\nCheck Avatar Descriptor or ensure blendshapes use standard VRChat naming.", MessageType.Warning);
                GUI.backgroundColor = warningColor;
            }
        }


        private void GeneratePreview()
        {
            Debug.Log("[AttachToBlendshape Preview] Generating preview...");

            try
            {
                if (!ValidateData())
                {
                    EditorUtility.DisplayDialog("Validation Failed", GetValidationError(), "OK");
                    return;
                }

                // Detect cluster
                data.previewCluster = SurfaceClusterDetector.DetectCluster(
                    data.targetMesh,
                    data.transform.position,
                    data.clusterTriangleCount,
                    data.searchRadius,
                    data.manualTriangleIndex);

                if (data.previewCluster == null)
                {
                    EditorUtility.DisplayDialog("Preview Failed", "Failed to detect surface cluster.", "OK");
                    return;
                }

                // Determine blendshapes based on mode
                GameObject avatarRoot = null;
                var avatarDescriptor = data.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                if (avatarDescriptor != null)
                {
                    avatarRoot = avatarDescriptor.gameObject;
                }

                switch (data.trackingMode)
                {
                    case BlendshapeTrackingMode.All:
                        data.previewBlendshapes = PoseSampler.GetAllBlendshapeNames(data.targetMesh.sharedMesh);
                        break;

                    case BlendshapeTrackingMode.Specific:
                        data.previewBlendshapes = new List<string>(data.specificBlendshapes)
                            .Where(name => data.targetMesh.sharedMesh.GetBlendShapeIndex(name) >= 0)
                            .ToList();
                        break;

                    case BlendshapeTrackingMode.VisemsOnly:
                        data.previewBlendshapes = VRChatVisemeDetector.GetVisemeBlendshapes(data.targetMesh, avatarRoot);
                        break;

                    case BlendshapeTrackingMode.Smart:
                        EditorUtility.DisplayProgressBar("Smart Detection", "Analyzing blendshapes...", 0.5f);
                        data.previewBlendshapes = VRChatVisemeDetector.DetectActiveBlendshapes(
                            data.targetMesh,
                            data.previewCluster,
                            data.smartDetectionThreshold);
                        EditorUtility.ClearProgressBar();
                        break;
                }

                data.previewGenerated = true;
                data.showPreview = true;

                SceneView.RepaintAll();
                Repaint();

                Debug.Log($"[AttachToBlendshape Preview] Preview generated: {data.previewBlendshapes.Count} blendshapes, {data.previewCluster.anchors.Count} triangles");
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[AttachToBlendshape Preview] Failed: {ex.Message}");
                Debug.LogException(ex);
            }
        }

        private void ClearPreview()
        {
            data.previewCluster = null;
            data.previewBlendshapes.Clear();
            data.previewGenerated = false;
            data.showPreview = false;

            SceneView.RepaintAll();
            Repaint();

            Debug.Log("[AttachToBlendshape Preview] Preview cleared");
        }

        private bool ValidateData()
        {
            if (data.targetMesh == null) return false;
            if (data.targetMesh.sharedMesh == null) return false;
            if (!PoseSampler.HasBlendshapes(data.targetMesh)) return false;
            if (data.trackingMode == BlendshapeTrackingMode.Specific && 
                (data.specificBlendshapes == null || data.specificBlendshapes.Count == 0)) return false;
            return true;
        }

        private string GetValidationError()
        {
            if (data.targetMesh == null) return "Target mesh is not set.";
            if (data.targetMesh.sharedMesh == null) return "Target mesh has no mesh data.";
            if (!PoseSampler.HasBlendshapes(data.targetMesh)) return "Target mesh has no blendshapes.";
            if (data.trackingMode == BlendshapeTrackingMode.Specific && 
                (data.specificBlendshapes == null || data.specificBlendshapes.Count == 0))
                return "Specific mode requires at least one blendshape name.";
            return "";
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        static void DrawGizmos(AttachToBlendshapeData data, GizmoType gizmoType)
        {
            if (!data.showPreview || !data.previewGenerated) return;
            if (data.previewCluster == null || data.targetMesh == null) return;

            Mesh mesh = data.targetMesh.sharedMesh;
            if (mesh == null) return;

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            // Draw cluster triangles
            Handles.color = new Color(0f, 1f, 0f, 0.3f);

            foreach (var anchor in data.previewCluster.anchors)
            {
                int idx0 = triangles[anchor.triIndex * 3];
                int idx1 = triangles[anchor.triIndex * 3 + 1];
                int idx2 = triangles[anchor.triIndex * 3 + 2];

                Vector3 v0 = data.targetMesh.transform.TransformPoint(vertices[idx0]);
                Vector3 v1 = data.targetMesh.transform.TransformPoint(vertices[idx1]);
                Vector3 v2 = data.targetMesh.transform.TransformPoint(vertices[idx2]);

                Handles.DrawAAConvexPolygon(v0, v1, v2);
            }

            // Draw cluster center and normal
            Vector3 worldCenter = data.targetMesh.transform.TransformPoint(data.previewCluster.centerPosition);
            Vector3 worldNormal = data.targetMesh.transform.TransformDirection(data.previewCluster.averageNormal);

            Handles.color = Color.yellow;
            Handles.SphereHandleCap(0, worldCenter, Quaternion.identity, 0.01f, EventType.Repaint);
            
            Handles.color = new Color(0.212f, 0.749f, 0.694f);
            Handles.DrawLine(worldCenter, worldCenter + worldNormal * 0.05f);

            // Draw info overlay
            Handles.BeginGUI();

            GUI.Box(new Rect(10, 10, 300, 120), "");
            GUI.Label(new Rect(15, 15, 290, 20), "Attach to Blendshape Preview", EditorStyles.boldLabel);

            GUI.color = new Color(0f, 1f, 0f, 0.3f);
            GUI.Box(new Rect(15, 40, 15, 15), "");
            GUI.color = Color.white;
            GUI.Label(new Rect(35, 38, 250, 20), $"= Surface cluster ({data.previewCluster.anchors.Count} triangles)");

            GUI.color = Color.yellow;
            GUI.Box(new Rect(15, 60, 15, 15), "");
            GUI.color = Color.white;
            GUI.Label(new Rect(35, 58, 250, 20), "= Cluster center");

            GUI.color = new Color(0.212f, 0.749f, 0.694f);
            GUI.Box(new Rect(15, 80, 15, 15), "");
            GUI.color = Color.white;
            GUI.Label(new Rect(35, 78, 250, 20), "= Surface normal");

            GUI.Label(new Rect(15, 100, 280, 20), $"Tracking {data.previewBlendshapes.Count} blendshapes");

            Handles.EndGUI();
        }
    }
}

