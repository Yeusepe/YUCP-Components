using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.Components.Editor.Utils;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Custom editor for Avatar Optimizer Plugin with comprehensive settings and modern UI.
    /// Provides preset-based configuration and detailed per-category controls.
    /// Shows prominent install button when d4rkAvatarOptimizer is not installed.
    /// </summary>
    [CustomEditor(typeof(AvatarOptimizerPluginData))]
    public class AvatarOptimizerPluginDataEditor : UnityEditor.Editor
    {
        private AvatarOptimizerPluginData data;
        private static bool? isOptimizerInstalled = null;
        private static Type optimizerType = null;

        // UI Mode
        private enum UIMode
        {
            Preset,      // Preset-based configuration (simple)
            Advanced     // Full detailed control
        }
        
        private UIMode currentMode = UIMode.Preset;

        // Foldout states for Advanced mode
        private bool showMeshMerging = true;
        private bool showAdvancedMerging = false;
        private bool showMaterialOptimization = false;
        private bool showFXLayer = true;
        private bool showComponents = true;
        private bool showBlendshapes = true;
        private bool showAdvancedFeatures = false;
        private bool showExclusions = false;
        private bool showDebug = false;
        private bool showInspectorOptions = false;
        private bool showBuildStats = false;

        private void OnEnable()
        {
            data = (AvatarOptimizerPluginData)target;
            
            // Check for optimizer installation once
            if (!isOptimizerInstalled.HasValue)
            {
                CheckForOptimizer();
            }
            
            // Load UI mode preference
            string modeKey = $"YUCP_OptimizerPlugin_UIMode_{data.GetInstanceID()}";
            currentMode = (UIMode)SessionState.GetInt(modeKey, 0);
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Avatar Optimizer Plugin"));
            
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
            data = (AvatarOptimizerPluginData)target;

            // Validate component placement
            DrawPlacementValidation();

            // If optimizer is not installed, show install UI
            if (isOptimizerInstalled == false)
            {
                DrawNotInstalledUI();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            // Show installation status banner
            DrawInstalledBanner();

            // Draw mode switcher
            DrawModeSwitcher();

            EditorGUILayout.Space(10);

            // Show appropriate UI based on mode
            if (currentMode == UIMode.Preset)
            {
                DrawPresetMode();
            }
            else
            {
                DrawAdvancedMode();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPlacementValidation()
        {
            var avatarDescriptor = data.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            if (avatarDescriptor == null)
            {
                EditorGUILayout.Space(5);
                var originalColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.4f, 0f, 0.8f);
                
                EditorGUILayout.HelpBox(
                    "⚠ INCORRECT PLACEMENT\n\n" +
                    "This component MUST be on the avatar root GameObject (the one with VRCAvatarDescriptor).\n\n" +
                    "Current: NOT on avatar root\n" +
                    "Optimizer settings are per-avatar, not per-object.",
                    MessageType.Error);
                
                if (GUILayout.Button("Find Avatar Root and Move Component", GUILayout.Height(30)))
                {
                    var descriptor = data.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                    if (descriptor != null)
                    {
                        // Move component to avatar root
                        if (EditorUtility.DisplayDialog("Move Component",
                            $"Move Avatar Optimizer Plugin to '{descriptor.gameObject.name}'?",
                            "Yes", "Cancel"))
                        {
                            var newPlugin = descriptor.gameObject.AddComponent<AvatarOptimizerPluginData>();
                            EditorUtility.CopySerialized(data, newPlugin);
                            DestroyImmediate(data);
                            Selection.activeGameObject = descriptor.gameObject;
                        }
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Avatar Root Not Found",
                            "Could not find VRCAvatarDescriptor in parent hierarchy.",
                            "OK");
                    }
                }
                
                GUI.backgroundColor = originalColor;
                EditorGUILayout.Space(5);
            }
        }

        private void DrawNotInstalledUI()
        {
            EditorGUILayout.Space(10);
            
            // Large prominent install button
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);
            
            GUIStyle bigButtonStyle = new GUIStyle(GUI.skin.button);
            bigButtonStyle.fontSize = 14;
            bigButtonStyle.fontStyle = FontStyle.Bold;
            bigButtonStyle.fixedHeight = 60;
            
            if (GUILayout.Button("⬇ Install d4rkAvatarOptimizer (Latest Version)", bigButtonStyle))
            {
                InstallOptimizer();
            }
            GUI.backgroundColor = originalColor;
            
            EditorGUILayout.Space(10);
            
            // Info box
            GUI.backgroundColor = new Color(0.3f, 0.5f, 0.8f, 0.3f);
            EditorGUILayout.HelpBox(
                "Automatic Installation via VPM\n\n" +
                "Click the button above to automatically install d4rkAvatarOptimizer through Unity's VPM package manager.\n\n" +
                "What happens:\n" +
                "1. Adds package to vpm-manifest.json\n" +
                "2. Unity resolves and downloads the package\n" +
                "3. Package compiles automatically\n" +
                "4. This inspector updates to show all settings\n\n" +
                "This usually takes 30-60 seconds.",
                MessageType.Info);
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.Space(10);
            
            // Alternative manual installation
            EditorGUILayout.LabelField("Alternative: Manual Installation", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            GUI.backgroundColor = new Color(0.4f, 0.6f, 1f);
            if (GUILayout.Button("Open GitHub Releases Page", GUILayout.Height(35)))
            {
                D4rkOptimizerInstaller.OpenGitHubReleasePage();
            }
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "Manual Installation Steps:\n" +
                "1. Click button above\n" +
                "2. Download latest .unitypackage from releases\n" +
                "3. Import into Unity project\n" +
                "4. Wait for compilation",
                MessageType.None);
                
            EditorGUILayout.Space(10);
            
            // Refresh check button
            if (GUILayout.Button("Check If Installed", GUILayout.Height(25)))
            {
                isOptimizerInstalled = null;
                CheckForOptimizer();
                Repaint();
                
                if (isOptimizerInstalled == true)
                {
                    EditorUtility.DisplayDialog("Optimizer Detected!",
                        "d4rkAvatarOptimizer is now installed and ready to use!\n\n" +
                        "The inspector will now show all available settings.",
                        "Great!");
                }
                else
                {
                    EditorUtility.DisplayDialog("Not Found",
                        "d4rkAvatarOptimizer is still not detected.\n\n" +
                        "If you just installed it, wait for Unity to finish compiling, then check again.",
                        "OK");
                }
            }
        }
        
        private void InstallOptimizer()
        {
            if (EditorUtility.DisplayDialog(
                "Install d4rkAvatarOptimizer",
                "This will automatically install d4rkAvatarOptimizer via VPM.\n\n" +
                "Unity will:\n" +
                "1. Add package to vpm-manifest.json\n" +
                "2. Resolve and download the package\n" +
                "3. Compile the code\n\n" +
                "This process may take 30-60 seconds.\n\n" +
                "Continue with automatic installation?",
                "Install", "Cancel"))
            {
                D4rkOptimizerInstaller.InstallOptimizer(
                    onSuccess: () => {
                        Debug.Log("[AvatarOptimizerPlugin] Installation initiated successfully");
                        EditorUtility.DisplayDialog(
                            "Installation Started",
                            "d4rkAvatarOptimizer installation is in progress.\n\n" +
                            "Unity is resolving the package in the background. " +
                            "This may take 30-60 seconds.\n\n" +
                            "When complete:\n" +
                            "• Unity will compile the package\n" +
                            "• This inspector will show all settings\n" +
                            "• You may need to click 'Check If Installed'",
                            "OK");
                    },
                    onError: (error) => {
                        EditorUtility.DisplayDialog(
                            "Installation Error",
                            $"Automatic installation failed:\n\n{error}\n\n" +
                            "Please try manual installation from GitHub instead.",
                            "OK");
                    });
            }
        }

        private void DrawInstalledBanner()
        {
            EditorGUILayout.Space(5);
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.3f, 0.3f);
            
            EditorGUILayout.HelpBox(
                $"✓ d4rkAvatarOptimizer Installed & Active\n\n" +
                $"This avatar will be optimized during build with {data.GetEnabledOptimizationCount()} enabled features.",
                MessageType.Info);
            
            GUI.backgroundColor = originalColor;
            EditorGUILayout.Space(5);
        }

        private void DrawModeSwitcher()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fixedWidth = 120;
            buttonStyle.fixedHeight = 25;
            
            GUIStyle selectedButtonStyle = new GUIStyle(buttonStyle);
            selectedButtonStyle.fontStyle = FontStyle.Bold;
            selectedButtonStyle.normal.background = buttonStyle.active.background;
            
            if (GUILayout.Button("Preset Mode", currentMode == UIMode.Preset ? selectedButtonStyle : buttonStyle))
            {
                currentMode = UIMode.Preset;
                SaveUIMode();
            }
            
            if (GUILayout.Button("Advanced Mode", currentMode == UIMode.Advanced ? selectedButtonStyle : buttonStyle))
            {
                currentMode = UIMode.Advanced;
                SaveUIMode();
            }
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            // Mode description
            if (currentMode == UIMode.Preset)
            {
                EditorGUILayout.HelpBox(
                    "Preset Mode: Quick configuration with recommended settings for common scenarios.",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Advanced Mode: Full control over every optimization setting. For experienced users.",
                    MessageType.None);
            }
        }

        private void SaveUIMode()
        {
            string modeKey = $"YUCP_OptimizerPlugin_UIMode_{data.GetInstanceID()}";
            SessionState.SetInt(modeKey, (int)currentMode);
        }

        private void DrawPresetMode()
        {
            // Main enable toggle
            DrawSection("Enable Optimization", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enableOptimizer"), new GUIContent("Enable Optimizer for this Avatar"));
                
                if (!data.enableOptimizer)
                {
                    EditorGUILayout.HelpBox(
                        "Optimization is disabled for this avatar. It will not be processed during build.",
                        MessageType.Warning);
                    return;
                }
            });

            if (!data.enableOptimizer)
            {
                return; // Don't show other settings if disabled
            }

            EditorGUILayout.Space(10);

            // Preset selector
            DrawSection("Optimization Preset", () => {
                EditorGUILayout.LabelField("Choose a preset or use Auto Settings:", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);
                
                // Auto settings toggle
                var autoSettingsProp = serializedObject.FindProperty("useAutoSettings");
                bool wasAutoSettings = autoSettingsProp.boolValue;
                EditorGUILayout.PropertyField(autoSettingsProp, new GUIContent("Auto-Detect Settings (Recommended)"));
                
                if (data.useAutoSettings)
                {
                    EditorGUILayout.HelpBox(
                        "Auto Settings: The optimizer will analyze your avatar and automatically choose the best settings based on complexity, poly count, and features.\n\n" +
                        "This is recommended for most users.",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.Space(10);
                    EditorGUILayout.LabelField("Manual Presets:", EditorStyles.boldLabel);
                    EditorGUILayout.Space(5);
                    
                    // Preset buttons
                    EditorGUILayout.BeginHorizontal();
                    
                    if (PresetButton("Conservative", "Safe settings for complex avatars with many systems", new Color(0.6f, 0.8f, 0.6f)))
                    {
                        ApplyConservativePreset();
                    }
                    
                    if (PresetButton("Balanced", "Good balance of optimization and safety", new Color(0.6f, 0.8f, 1f)))
                    {
                        ApplyBalancedPreset();
                    }
                    
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.Space(5);
                    EditorGUILayout.BeginHorizontal();
                    
                    if (PresetButton("Aggressive", "Maximum optimization, test carefully", new Color(1f, 0.7f, 0.4f)))
                    {
                        ApplyAggressivePreset();
                    }
                    
                    if (PresetButton("Custom", "Use Advanced Mode for full control", new Color(0.8f, 0.8f, 0.8f)))
                    {
                        // Just a visual indicator, user can use advanced mode
                        EditorUtility.DisplayDialog("Custom Settings",
                            "Switch to Advanced Mode using the toggle above to customize all settings individually.",
                            "OK");
                    }
                    
                    EditorGUILayout.EndHorizontal();
                }
            });

            // Quick overview
            EditorGUILayout.Space(10);
            DrawOptimizationOverview();

            // Exclusions (always accessible)
            EditorGUILayout.Space(10);
            DrawSection("Exclusions", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("excludeTransforms"), new GUIContent("Exclude from Optimization"), true);
                
                if (data.excludeTransforms != null && data.excludeTransforms.Count > 0)
                {
                    EditorGUILayout.HelpBox(
                        $"{data.excludeTransforms.Count} transform(s) excluded from all optimizations.",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Add transforms here to exclude them from optimization (e.g., posebones, penetrators, custom systems).",
                        MessageType.None);
                }
            });

            // Build stats - always show
            EditorGUILayout.Space(10);
            DrawBuildStatistics();
        }

        private void DrawAdvancedMode()
        {
            // Enable toggle at top
            DrawSection("Enable Optimization", () => {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enableOptimizer"), new GUIContent("Enable Optimizer"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("useAutoSettings"), new GUIContent("Use Auto Settings"));
                
                if (data.useAutoSettings)
                {
                    EditorGUILayout.HelpBox("Auto Settings is ON - manual settings below will be overridden by auto-detection.", MessageType.Warning);
                }
            });

            if (!data.enableOptimizer)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("Optimization disabled for this avatar.", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(10);

            // Mesh Merging
            showMeshMerging = EditorGUILayout.BeginFoldoutHeaderGroup(showMeshMerging, "Mesh Merging");
            if (showMeshMerging)
            {
                DrawSection("", () => {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeSkinnedMeshes"), new GUIContent("Merge Skinned Meshes"));
                    EditorGUILayout.HelpBox("Combines multiple skinned meshes to reduce draw calls.", MessageType.None);
                    
                    if (data.mergeSkinnedMeshes)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.Space(3);
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeStaticMeshesAsSkinned"), new GUIContent("Include Static Meshes"));
                        EditorGUILayout.HelpBox("Converts static meshes to skinned so they can be merged.", MessageType.None);
                        EditorGUI.indentLevel--;
                    }
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Advanced Merging
            EditorGUILayout.Space(5);
            showAdvancedMerging = EditorGUILayout.BeginFoldoutHeaderGroup(showAdvancedMerging, "Advanced Mesh Merging");
            if (showAdvancedMerging)
            {
                DrawSection("", () => {
                    EditorGUILayout.LabelField("Shader Toggle Merging", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeSkinnedMeshesWithShaderToggle"), new GUIContent("Shader Toggle Level"));
                    EditorGUILayout.HelpBox("0=Off, 1=Basic, 2=Advanced | Requires Windows build target", MessageType.None);
                    
                    if (data.mergeSkinnedMeshesWithShaderToggle > 0 && EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                    {
                        EditorGUILayout.HelpBox("Shader toggles require Windows build target!", MessageType.Warning);
                    }
                    
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("NaNimation Merging", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeSkinnedMeshesWithNaNimation"), new GUIContent("NaNimation Level"));
                    EditorGUILayout.HelpBox("0=Off, 1=Basic, 2=Advanced | Uses NaN in animations for visibility", MessageType.None);
                    
                    if (data.mergeSkinnedMeshesWithNaNimation > 0)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.Space(3);
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("naNimationAllow3BoneSkinning"), new GUIContent("Allow 3-Bone Skinning"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeSkinnedMeshesSeparatedByDefaultEnabledState"), new GUIContent("Separate by Default State"));
                        EditorGUI.indentLevel--;
                    }
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Material Optimization
            EditorGUILayout.Space(5);
            showMaterialOptimization = EditorGUILayout.BeginFoldoutHeaderGroup(showMaterialOptimization, "Material Optimization");
            if (showMaterialOptimization)
            {
                DrawSection("", () => {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeDifferentPropertyMaterials"), new GUIContent("Merge Different Property Materials"));
                    EditorGUILayout.HelpBox("Creates optimized shaders combining multiple materials. Windows only.", MessageType.None);
                    
                    if (data.mergeDifferentPropertyMaterials)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.Space(3);
                        
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeSameDimensionTextures"), new GUIContent("Merge Textures to Arrays"));
                        if (data.mergeSameDimensionTextures)
                        {
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeMainTex"), new GUIContent("Include Main Textures"));
                            EditorGUI.indentLevel--;
                        }
                        
                        EditorGUILayout.Space(3);
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("writePropertiesAsStaticValues"), new GUIContent("Write Static Properties"));
                        
                        EditorGUI.indentLevel--;
                    }
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // FX Layer
            EditorGUILayout.Space(5);
            showFXLayer = EditorGUILayout.BeginFoldoutHeaderGroup(showFXLayer, "FX Layer Optimization");
            if (showFXLayer)
            {
                DrawSection("", () => {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("optimizeFXLayer"), new GUIContent("Optimize FX Layer"));
                    EditorGUILayout.HelpBox("Merges compatible animator layers and removes unused ones.", MessageType.None);
                    
                    if (data.optimizeFXLayer)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.Space(3);
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("combineApproximateMotionTimeAnimations"), new GUIContent("Combine Similar-Length Animations"));
                        EditorGUI.indentLevel--;
                    }
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Components
            EditorGUILayout.Space(5);
            showComponents = EditorGUILayout.BeginFoldoutHeaderGroup(showComponents, "Component Cleanup");
            if (showComponents)
            {
                DrawSection("", () => {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("disablePhysBonesWhenUnused"), new GUIContent("Disable Unused PhysBones"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("deleteUnusedComponents"), new GUIContent("Delete Unused Components"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("deleteUnusedGameObjects"), new GUIContent("Delete Unused GameObjects"));
                    EditorGUILayout.HelpBox("0=Off, 1=Safe, 2=Aggressive", MessageType.None);
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Blendshapes
            EditorGUILayout.Space(5);
            showBlendshapes = EditorGUILayout.BeginFoldoutHeaderGroup(showBlendshapes, "Blendshape Optimization");
            if (showBlendshapes)
            {
                DrawSection("", () => {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeSameRatioBlendShapes"), new GUIContent("Merge Same-Ratio Blendshapes"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("mmdCompatibility"), new GUIContent("MMD Compatibility"));
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Advanced Features
            EditorGUILayout.Space(5);
            showAdvancedFeatures = EditorGUILayout.BeginFoldoutHeaderGroup(showAdvancedFeatures, "Advanced Features");
            if (showAdvancedFeatures)
            {
                DrawSection("", () => {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("useRingFingerAsFootCollider"), new GUIContent("Ring Finger → Foot Collider"));
                    EditorGUILayout.HelpBox("Moves ring finger contact to feet. Disables ring finger interactions.", MessageType.None);
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Exclusions
            EditorGUILayout.Space(5);
            showExclusions = EditorGUILayout.BeginFoldoutHeaderGroup(showExclusions, "Exclusions");
            if (showExclusions)
            {
                DrawSection("", () => {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("excludeTransforms"), new GUIContent("Excluded Transforms"), true);
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Inspector Options
            EditorGUILayout.Space(5);
            showInspectorOptions = EditorGUILayout.BeginFoldoutHeaderGroup(showInspectorOptions, "Inspector Display");
            if (showInspectorOptions)
            {
                DrawSection("", () => {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("showMeshMergePreview"), new GUIContent("Show Mesh Merge Preview"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("showFXLayerMergeResults"), new GUIContent("Show FX Layer Results"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("showDebugInfo"), new GUIContent("Show Debug Info"));
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Debug
            EditorGUILayout.Space(5);
            showDebug = EditorGUILayout.BeginFoldoutHeaderGroup(showDebug, "Debug & Profiling");
            if (showDebug)
            {
                DrawSection("", () => {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("profileTimeUsed"), new GUIContent("Profile Time Used"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("debugMode"), new GUIContent("Debug Logging"));
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // Build stats - always show
            EditorGUILayout.Space(10);
            DrawBuildStatistics();
        }

        private bool PresetButton(string label, string description, Color color)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = color;
            
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fixedHeight = 60;
            buttonStyle.wordWrap = true;
            buttonStyle.alignment = TextAnchor.MiddleCenter;
            
            bool clicked = GUILayout.Button($"{label}\n\n{description}", buttonStyle);
            
            GUI.backgroundColor = originalColor;
            return clicked;
        }

        private void DrawOptimizationOverview()
        {
            DrawSection("Optimization Overview", () => {
                int enabledCount = data.GetEnabledOptimizationCount();
                
                EditorGUILayout.LabelField($"Active Optimizations: {enabledCount}", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);
                
                // Show enabled optimizations
                var optimizations = new System.Collections.Generic.List<(string name, bool enabled)>
                {
                    ("Mesh Merging", data.mergeSkinnedMeshes),
                    ("Static Mesh Merging", data.mergeStaticMeshesAsSkinned),
                    ("Shader Toggle Merging", data.mergeSkinnedMeshesWithShaderToggle > 0),
                    ("NaNimation Merging", data.mergeSkinnedMeshesWithNaNimation > 0),
                    ("Material Merging", data.mergeDifferentPropertyMaterials),
                    ("Texture Arrays", data.mergeSameDimensionTextures),
                    ("FX Layer Optimization", data.optimizeFXLayer),
                    ("Animation Combining", data.combineApproximateMotionTimeAnimations),
                    ("PhysBone Disabling", data.disablePhysBonesWhenUnused),
                    ("Component Cleanup", data.deleteUnusedComponents),
                    ("GameObject Cleanup", data.deleteUnusedGameObjects > 0),
                    ("Blendshape Merging", data.mergeSameRatioBlendShapes),
                };
                
                foreach (var opt in optimizations)
                {
                    if (opt.enabled)
                    {
                        EditorGUILayout.LabelField($"  ✓ {opt.name}", new GUIStyle(EditorStyles.label) { normal = { textColor = Color.green } });
                    }
                }
                
                if (enabledCount == 0)
                {
                    EditorGUILayout.LabelField("  No optimizations enabled", new GUIStyle(EditorStyles.label) { normal = { textColor = Color.yellow } });
                }
            });
        }

        private void DrawBuildStatistics()
        {
            // Refresh button to check for updated stats
            if (GUILayout.Button("Refresh Build Statistics", GUILayout.Height(25)))
            {
                Repaint();
                EditorUtility.SetDirty(data);
            }
            
            EditorGUILayout.Space(5);
            
            // Show a prominent box with last build results
            if (data.OptimizerDetected)
            {
                EditorGUILayout.Space(5);
                
                // Success state - show in green box
                if (data.OptimizationApplied)
                {
                    EditorGUILayout.HelpBox(
                        $"Last Build Result: Successfully applied {data.OptimizationsApplied} optimization settings to avatar", 
                        MessageType.Info
                    );
                }
                // Error state - show in red box
                else if (!string.IsNullOrEmpty(data.BuildError))
                {
                    EditorGUILayout.HelpBox(
                        $"Last Build Result: {data.BuildError}", 
                        MessageType.Warning
                    );
                }
                // Disabled state
                else if (!data.enableOptimizer)
                {
                    EditorGUILayout.HelpBox(
                        "Last Build Result: Optimizer was disabled", 
                        MessageType.Info
                    );
                }
            }
            else
            {
                // No build yet - show instructions
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "Build Status: Not yet applied\n\n" +
                    "To see optimization results:\n" +
                    "1. Configure your desired settings above\n" +
                    "2. Upload your avatar using VRChat SDK\n" +
                    "3. Check this section for confirmation\n\n" +
                    "Note: Statistics are saved during actual builds/uploads.\n" +
                    "NDMF 'Apply on Play' preview mode applies settings but doesn't persist statistics.\n" +
                    "Check the Console for confirmation messages during previews.",
                    MessageType.Info
                );
            }
            
            // Detailed stats in expandable section
            showBuildStats = EditorGUILayout.BeginFoldoutHeaderGroup(showBuildStats, "Detailed Build Statistics");
            if (showBuildStats)
            {
                DrawSection("", () => {
                    GUI.enabled = false;
                    EditorGUILayout.LabelField("Optimizer Detected", data.OptimizerDetected ? "Yes" : "No");
                    EditorGUILayout.LabelField("Optimization Applied", data.OptimizationApplied ? "Yes" : "No");
                    EditorGUILayout.LabelField("Settings Applied", $"{data.OptimizationsApplied}");
                    
                    if (!string.IsNullOrEmpty(data.BuildError))
                    {
                        EditorGUILayout.LabelField("Error", data.BuildError);
                    }
                    GUI.enabled = true;
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void ApplyConservativePreset()
        {
            Undo.RecordObject(data, "Apply Conservative Preset");
            
            data.useAutoSettings = false;
            data.mergeSkinnedMeshes = true;
            data.mergeStaticMeshesAsSkinned = false;
            data.mergeDifferentPropertyMaterials = false;
            data.mergeSameDimensionTextures = false;
            data.mergeMainTex = false;
            data.mergeSkinnedMeshesWithShaderToggle = 0;
            data.mergeSkinnedMeshesWithNaNimation = 0;
            data.naNimationAllow3BoneSkinning = false;
            data.mergeSkinnedMeshesSeparatedByDefaultEnabledState = true;
            data.optimizeFXLayer = true;
            data.combineApproximateMotionTimeAnimations = false;
            data.disablePhysBonesWhenUnused = true;
            data.deleteUnusedComponents = true;
            data.deleteUnusedGameObjects = 1; // Safe only
            data.mergeSameRatioBlendShapes = true;
            data.mmdCompatibility = true;
            data.writePropertiesAsStaticValues = false;
            data.useRingFingerAsFootCollider = false;
            
            EditorUtility.SetDirty(data);
            serializedObject.Update();
            
            Debug.Log("[AvatarOptimizerPlugin] Applied Conservative preset");
        }

        private void ApplyBalancedPreset()
        {
            Undo.RecordObject(data, "Apply Balanced Preset");
            
            data.useAutoSettings = false;
            data.mergeSkinnedMeshes = true;
            data.mergeStaticMeshesAsSkinned = true;
            data.mergeDifferentPropertyMaterials = false;
            data.mergeSameDimensionTextures = false;
            data.mergeMainTex = false;
            data.mergeSkinnedMeshesWithShaderToggle = 0;
            data.mergeSkinnedMeshesWithNaNimation = 1; // Basic
            data.naNimationAllow3BoneSkinning = false;
            data.mergeSkinnedMeshesSeparatedByDefaultEnabledState = true;
            data.optimizeFXLayer = true;
            data.combineApproximateMotionTimeAnimations = true;
            data.disablePhysBonesWhenUnused = true;
            data.deleteUnusedComponents = true;
            data.deleteUnusedGameObjects = 1; // Safe
            data.mergeSameRatioBlendShapes = true;
            data.mmdCompatibility = true;
            data.writePropertiesAsStaticValues = false;
            data.useRingFingerAsFootCollider = false;
            
            EditorUtility.SetDirty(data);
            serializedObject.Update();
            
            Debug.Log("[AvatarOptimizerPlugin] Applied Balanced preset");
        }

        private void ApplyAggressivePreset()
        {
            bool confirm = EditorUtility.DisplayDialog(
                "Aggressive Preset Warning",
                "The Aggressive preset enables powerful optimizations that may break some avatar features.\n\n" +
                "This preset is recommended for:\n" +
                "• Simple avatars without complex systems\n" +
                "• Avatars with very high poly counts\n" +
                "• Final optimization after thorough testing\n\n" +
                "Always test your avatar thoroughly after applying this preset.\n\n" +
                "Continue?",
                "Apply Aggressive", "Cancel");
                
            if (!confirm) return;
            
            Undo.RecordObject(data, "Apply Aggressive Preset");
            
            data.useAutoSettings = false;
            data.mergeSkinnedMeshes = true;
            data.mergeStaticMeshesAsSkinned = true;
            data.mergeDifferentPropertyMaterials = true;
            data.mergeSameDimensionTextures = true;
            data.mergeMainTex = true;
            data.mergeSkinnedMeshesWithShaderToggle = 2; // Advanced
            data.mergeSkinnedMeshesWithNaNimation = 2; // Advanced
            data.naNimationAllow3BoneSkinning = true;
            data.mergeSkinnedMeshesSeparatedByDefaultEnabledState = false;
            data.optimizeFXLayer = true;
            data.combineApproximateMotionTimeAnimations = true;
            data.disablePhysBonesWhenUnused = true;
            data.deleteUnusedComponents = true;
            data.deleteUnusedGameObjects = 2; // Aggressive
            data.mergeSameRatioBlendShapes = true;
            data.mmdCompatibility = false; // Disable for maximum optimization
            data.writePropertiesAsStaticValues = true;
            data.useRingFingerAsFootCollider = true;
            
            EditorUtility.SetDirty(data);
            serializedObject.Update();
            
            Debug.Log("[AvatarOptimizerPlugin] Applied Aggressive preset");
        }

        private void DrawSection(string title, System.Action content)
        {
            if (!string.IsNullOrEmpty(title))
            {
                EditorGUILayout.Space(5);
            }
            
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0f, 0f, 0f, 0.15f);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;
            
            if (!string.IsNullOrEmpty(title))
            {
                var style = new GUIStyle(EditorStyles.boldLabel);
                style.fontSize = 12;
                style.alignment = TextAnchor.MiddleLeft;
                EditorGUILayout.LabelField(title, style);
                EditorGUILayout.Space(3);
            }
            
            content?.Invoke();
            
            EditorGUILayout.EndVertical();
        }

        private void CheckForOptimizer()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                optimizerType = assembly.GetType("d4rkAvatarOptimizer");
                if (optimizerType != null)
                {
                    isOptimizerInstalled = true;
                    Debug.Log("[AvatarOptimizerPlugin Editor] d4rkAvatarOptimizer detected!");
                    return;
                }
            }
            
            isOptimizerInstalled = false;
        }
    }
}
