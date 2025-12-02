using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components;
using YUCP.Components.Editor.Utils;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Custom editor for Avatar Optimizer Plugin with settings and modern UI.
    /// Provides preset configuration and detailed per-category controls.
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
            Preset,      // Preset configuration (simple)
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
        
        // Track previous values to reduce unnecessary UI updates
        private UIMode previousMode = (UIMode)(-1);
        private VRC.SDK3.Avatars.Components.VRCAvatarDescriptor previousAvatarDescriptor = null;
        private bool previousUseAutoSettings = false;

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
            serializedObject.Update();
            data = (AvatarOptimizerPluginData)target;
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Avatar Optimizer Plugin"));
            
            // Placement validation
            var placementValidation = new VisualElement();
            placementValidation.name = "placement-validation";
            root.Add(placementValidation);
            
            // Not installed UI (conditional)
            var notInstalledUI = new VisualElement();
            notInstalledUI.name = "not-installed-ui";
            root.Add(notInstalledUI);
            
            // Installed banner (conditional)
            var installedBanner = new VisualElement();
            installedBanner.name = "installed-banner";
            root.Add(installedBanner);
            
            // Mode switcher
            var modeSwitcher = new VisualElement();
            modeSwitcher.name = "mode-switcher";
            root.Add(modeSwitcher);
            
            // Preset mode UI (conditional)
            var presetModeUI = new VisualElement();
            presetModeUI.name = "preset-mode-ui";
            root.Add(presetModeUI);
            
            // Advanced mode UI (conditional)
            var advancedModeUI = new VisualElement();
            advancedModeUI.name = "advanced-mode-ui";
            root.Add(advancedModeUI);
            
            // Initialize previous values
            previousMode = currentMode;
            previousAvatarDescriptor = data.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            previousUseAutoSettings = data.useAutoSettings;
            
            // Initial population
            UpdatePlacementValidation(placementValidation);
            if (isOptimizerInstalled == false)
            {
                UpdateNotInstalledUI(notInstalledUI);
                presetModeUI.style.display = DisplayStyle.None;
                advancedModeUI.style.display = DisplayStyle.None;
                modeSwitcher.style.display = DisplayStyle.None;
                installedBanner.style.display = DisplayStyle.None;
            }
            else
            {
                notInstalledUI.style.display = DisplayStyle.None;
                UpdateInstalledBanner(installedBanner);
                UpdateModeSwitcher(modeSwitcher);
                
                if (currentMode == UIMode.Preset)
                {
                    presetModeUI.style.display = DisplayStyle.Flex;
                    advancedModeUI.style.display = DisplayStyle.None;
                    UpdatePresetMode(presetModeUI);
                }
                else
                {
                    presetModeUI.style.display = DisplayStyle.None;
                    advancedModeUI.style.display = DisplayStyle.Flex;
                    UpdateAdvancedMode(advancedModeUI);
                }
            }
            
            // Dynamic updates
            root.schedule.Execute(() =>
            {
                serializedObject.Update();
                
                // Update placement validation only when avatar descriptor changes
                var currentAvatarDescriptor = data.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                if (currentAvatarDescriptor != previousAvatarDescriptor)
                {
                    UpdatePlacementValidation(placementValidation);
                    previousAvatarDescriptor = currentAvatarDescriptor;
                }
                
                if (isOptimizerInstalled == false)
                {
                    UpdateNotInstalledUI(notInstalledUI);
                    presetModeUI.style.display = DisplayStyle.None;
                    advancedModeUI.style.display = DisplayStyle.None;
                    modeSwitcher.style.display = DisplayStyle.None;
                    installedBanner.style.display = DisplayStyle.None;
                    serializedObject.ApplyModifiedProperties();
                    return;
                }
                
                notInstalledUI.style.display = DisplayStyle.None;
                UpdateInstalledBanner(installedBanner);
                UpdateModeSwitcher(modeSwitcher);
                
                // Only rebuild mode UI when mode changes
                if (currentMode != previousMode)
                {
                    if (currentMode == UIMode.Preset)
                    {
                        presetModeUI.style.display = DisplayStyle.Flex;
                        advancedModeUI.style.display = DisplayStyle.None;
                        UpdatePresetMode(presetModeUI);
                    }
                    else
                    {
                        presetModeUI.style.display = DisplayStyle.None;
                        advancedModeUI.style.display = DisplayStyle.Flex;
                        UpdateAdvancedMode(advancedModeUI);
                    }
                    previousMode = currentMode;
                }
                
                serializedObject.ApplyModifiedProperties();
            }).Every(100);
            
            return root;
        }

        private void UpdatePlacementValidation(VisualElement container)
        {
            if (container == null) return;
            
            container.Clear();
            
            var avatarDescriptor = data.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            if (avatarDescriptor == null)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "INCORRECT PLACEMENT\n\n" +
                    "This component MUST be on the avatar root GameObject (the one with VRCAvatarDescriptor).\n\n" +
                    "Current: NOT on avatar root\n" +
                    "Optimizer settings are per-avatar, not per-object.",
                    YUCPUIToolkitHelper.MessageType.Error));
                
                var moveButton = YUCPUIToolkitHelper.CreateButton("Find Avatar Root and Move Component", () =>
                {
                    var descriptor = data.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                    if (descriptor != null)
                    {
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
                }, YUCPUIToolkitHelper.ButtonVariant.Primary);
                moveButton.style.height = 30;
                container.Add(moveButton);
            }
        }
        
        private void UpdateNotInstalledUI(VisualElement container)
        {
            if (container == null) return;
            
            container.Clear();
            
            YUCPUIToolkitHelper.AddSpacing(container, 10);
            
            var installButton = YUCPUIToolkitHelper.CreateButton("⬇ Install d4rkAvatarOptimizer (Latest Version)", () => InstallOptimizer(), YUCPUIToolkitHelper.ButtonVariant.Primary);
            installButton.style.height = 60;
            installButton.style.fontSize = 14;
            installButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            installButton.style.backgroundColor = new StyleColor(new Color(0.2f, 0.8f, 0.2f));
            container.Add(installButton);
            
            YUCPUIToolkitHelper.AddSpacing(container, 10);
            
            container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "Automatic Installation via VPM\n\n" +
                "Click the button above to automatically install d4rkAvatarOptimizer through Unity's VPM package manager.\n\n" +
                "What happens:\n" +
                "1. Adds package to vpm-manifest.json\n" +
                "2. Unity resolves and downloads the package\n" +
                "3. Package compiles automatically\n" +
                "4. This inspector updates to show all settings\n\n" +
                "This usually takes 30-60 seconds.",
                YUCPUIToolkitHelper.MessageType.Info));
            
            YUCPUIToolkitHelper.AddSpacing(container, 15);
            container.Add(YUCPUIToolkitHelper.CreateDivider());
            YUCPUIToolkitHelper.AddSpacing(container, 10);
            
            var manualLabel = new Label("Alternative: Manual Installation");
            manualLabel.style.fontSize = 13;
            manualLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            container.Add(manualLabel);
            
            YUCPUIToolkitHelper.AddSpacing(container, 5);
            
            var githubButton = YUCPUIToolkitHelper.CreateButton("Open GitHub Releases Page", () => D4rkOptimizerInstaller.OpenGitHubReleasePage(), YUCPUIToolkitHelper.ButtonVariant.Secondary);
            githubButton.style.height = 35;
            githubButton.style.backgroundColor = new StyleColor(new Color(0.4f, 0.6f, 1f));
            container.Add(githubButton);
            
            YUCPUIToolkitHelper.AddSpacing(container, 5);
            
            container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "Manual Installation Steps:\n" +
                "1. Click button above\n" +
                "2. Download latest .unitypackage from releases\n" +
                "3. Import into Unity project\n" +
                "4. Wait for compilation",
                YUCPUIToolkitHelper.MessageType.None));
            
            YUCPUIToolkitHelper.AddSpacing(container, 10);
            
            var checkButton = YUCPUIToolkitHelper.CreateButton("Check If Installed", () =>
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
            }, YUCPUIToolkitHelper.ButtonVariant.Secondary);
            checkButton.style.height = 25;
            container.Add(checkButton);
        }
        
        private void UpdateInstalledBanner(VisualElement container)
        {
            if (container == null) return;
            
            container.Clear();
            container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                $"d4rkAvatarOptimizer Installed & Active\n\n" +
                $"This avatar will be optimized during build with {data.GetEnabledOptimizationCount()} enabled features.",
                YUCPUIToolkitHelper.MessageType.Info));
        }
        
        private void UpdateModeSwitcher(VisualElement container)
        {
            if (container == null) return;
            
            container.Clear();
            
            var buttonsRow = new VisualElement();
            buttonsRow.style.flexDirection = FlexDirection.Row;
            buttonsRow.style.justifyContent = Justify.Center;
            buttonsRow.style.marginBottom = 5;
            
            var presetButton = YUCPUIToolkitHelper.CreateButton("Preset Mode", () =>
            {
                currentMode = UIMode.Preset;
                SaveUIMode();
            }, currentMode == UIMode.Preset ? YUCPUIToolkitHelper.ButtonVariant.Primary : YUCPUIToolkitHelper.ButtonVariant.Secondary);
            presetButton.style.width = 120;
            presetButton.style.height = 25;
            if (currentMode == UIMode.Preset)
            {
                presetButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            }
            buttonsRow.Add(presetButton);
            
            var advancedButton = YUCPUIToolkitHelper.CreateButton("Advanced Mode", () =>
            {
                currentMode = UIMode.Advanced;
                SaveUIMode();
            }, currentMode == UIMode.Advanced ? YUCPUIToolkitHelper.ButtonVariant.Primary : YUCPUIToolkitHelper.ButtonVariant.Secondary);
            advancedButton.style.width = 120;
            advancedButton.style.height = 25;
            if (currentMode == UIMode.Advanced)
            {
                advancedButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            }
            buttonsRow.Add(advancedButton);
            
            container.Add(buttonsRow);
            
            YUCPUIToolkitHelper.AddSpacing(container, 5);
            
            if (currentMode == UIMode.Preset)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Preset Mode: Quick configuration with recommended settings for common scenarios.",
                    YUCPUIToolkitHelper.MessageType.None));
            }
            else
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Advanced Mode: Full control over every optimization setting. For experienced users.",
                    YUCPUIToolkitHelper.MessageType.None));
            }
        }
        
        private void UpdatePresetMode(VisualElement container)
        {
            if (container == null) return;
            
            container.Clear();
            
            var enableCard = YUCPUIToolkitHelper.CreateCard("Enable Optimization", "Configure optimizer for this avatar");
            var enableContent = YUCPUIToolkitHelper.GetCardContent(enableCard);
            enableContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("enableOptimizer"), "Enable Optimizer for this Avatar"));
            
            var enableWarning = new VisualElement();
            enableWarning.name = "enable-warning";
            enableContent.Add(enableWarning);
            container.Add(enableCard);
            
            if (!data.enableOptimizer)
            {
                enableWarning.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Optimization is disabled for this avatar. It will not be processed during build.",
                    YUCPUIToolkitHelper.MessageType.Warning));
                UpdateOptimizationOverview(container);
                UpdatePresetExclusions(container);
                UpdatePresetBuildStatistics(container);
                return;
            }
            
            YUCPUIToolkitHelper.AddSpacing(container, 10);
            
            var presetCard = YUCPUIToolkitHelper.CreateCard("Optimization Preset", "Choose a preset or use Auto Settings");
            var presetContent = YUCPUIToolkitHelper.GetCardContent(presetCard);
            
            var chooseLabel = new Label("Choose a preset or use Auto Settings:");
            chooseLabel.style.fontSize = 13;
            chooseLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            presetContent.Add(chooseLabel);
            
            YUCPUIToolkitHelper.AddSpacing(presetContent, 5);
            
            presetContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("useAutoSettings"), "Auto-Detect Settings (Recommended)"));
            
            var autoSettingsHelp = new VisualElement();
            autoSettingsHelp.name = "auto-settings-help";
            presetContent.Add(autoSettingsHelp);
            
            var manualPresetsContainer = new VisualElement();
            manualPresetsContainer.name = "manual-presets";
            presetContent.Add(manualPresetsContainer);
            
            container.Add(presetCard);
            
            YUCPUIToolkitHelper.AddSpacing(container, 10);
            
            UpdateOptimizationOverview(container);
            
            YUCPUIToolkitHelper.AddSpacing(container, 10);
            
            UpdatePresetExclusions(container);
            
            YUCPUIToolkitHelper.AddSpacing(container, 10);
            
            UpdatePresetBuildStatistics(container);
            
            // Initial population of auto settings help and manual presets
            UpdateAutoSettingsHelp(autoSettingsHelp, manualPresetsContainer);
            
            // Update auto settings help and manual presets only when useAutoSettings changes
            container.schedule.Execute(() =>
            {
                if (data.useAutoSettings != previousUseAutoSettings)
                {
                    UpdateAutoSettingsHelp(autoSettingsHelp, manualPresetsContainer);
                    previousUseAutoSettings = data.useAutoSettings;
                }
            }).Every(100);
        }
        
        private void UpdateAutoSettingsHelp(VisualElement autoSettingsHelp, VisualElement manualPresetsContainer)
        {
            autoSettingsHelp.Clear();
            manualPresetsContainer.Clear();
            
            if (data.useAutoSettings)
            {
                autoSettingsHelp.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Auto Settings: The optimizer will analyze your avatar and automatically choose the best settings based on complexity, poly count, and features.\n\n" +
                    "This is recommended for most users.",
                    YUCPUIToolkitHelper.MessageType.Info));
                manualPresetsContainer.style.display = DisplayStyle.None;
            }
            else
            {
                autoSettingsHelp.style.display = DisplayStyle.None;
                manualPresetsContainer.style.display = DisplayStyle.Flex;
                
                YUCPUIToolkitHelper.AddSpacing(manualPresetsContainer, 10);
                
                var manualLabel = new Label("Manual Presets:");
                manualLabel.style.fontSize = 13;
                manualLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                manualPresetsContainer.Add(manualLabel);
                
                YUCPUIToolkitHelper.AddSpacing(manualPresetsContainer, 5);
                
                var presetSelector = YUCPUIToolkitHelper.CreatePresetSelector((preset) =>
                {
                    switch (preset)
                    {
                        case YUCPPresetSelector.Preset.Conservative:
                            ApplyConservativePreset();
                            break;
                        case YUCPPresetSelector.Preset.Balanced:
                            ApplyBalancedPreset();
                            break;
                        case YUCPPresetSelector.Preset.Aggressive:
                            ApplyAggressivePreset();
                            break;
                        case YUCPPresetSelector.Preset.Custom:
                            EditorUtility.DisplayDialog("Custom Settings",
                                "Switch to Advanced Mode using the toggle above to customize all settings individually.",
                                "OK");
                            break;
                    }
                });
                manualPresetsContainer.Add(presetSelector);
            }
        }
        
        private void UpdatePresetExclusions(VisualElement container)
        {
            var exclusionsCard = YUCPUIToolkitHelper.CreateCard("Exclusions", "Exclude transforms from optimization");
            var exclusionsContent = YUCPUIToolkitHelper.GetCardContent(exclusionsCard);
            exclusionsContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("excludeTransforms"), "Exclude from Optimization"));
            
            var exclusionsHelp = new VisualElement();
            exclusionsHelp.name = "exclusions-help";
            exclusionsContent.Add(exclusionsHelp);
            container.Add(exclusionsCard);
            
            // Track previous exclude transforms count
            int previousExcludeTransformsCount = data.excludeTransforms != null ? data.excludeTransforms.Count : -1;
            UpdateExclusionsHelp(exclusionsHelp);
            
            container.schedule.Execute(() =>
            {
                int currentExcludeTransformsCount = data.excludeTransforms != null ? data.excludeTransforms.Count : -1;
                if (currentExcludeTransformsCount != previousExcludeTransformsCount)
                {
                    UpdateExclusionsHelp(exclusionsHelp);
                    previousExcludeTransformsCount = currentExcludeTransformsCount;
                }
            }).Every(100);
        }
        
        private void UpdateExclusionsHelp(VisualElement container)
        {
            container.Clear();
            if (data.excludeTransforms != null && data.excludeTransforms.Count > 0)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    $"{data.excludeTransforms.Count} transform(s) excluded from all optimizations.",
                    YUCPUIToolkitHelper.MessageType.Info));
            }
            else
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Add transforms here to exclude them from optimization (e.g., posebones, penetrators, custom systems).",
                    YUCPUIToolkitHelper.MessageType.None));
            }
        }
        
        private void UpdateOptimizationOverview(VisualElement container)
        {
            var overviewCard = YUCPUIToolkitHelper.CreateCard("Optimization Overview", "Summary of enabled optimizations");
            var overviewContent = YUCPUIToolkitHelper.GetCardContent(overviewCard);
            
            var overviewLabel = new VisualElement();
            overviewLabel.name = "overview-label";
            overviewContent.Add(overviewLabel);
            
            var optimizationsList = new VisualElement();
            optimizationsList.name = "optimizations-list";
            overviewContent.Add(optimizationsList);
            
            container.Add(overviewCard);
            
            container.schedule.Execute(() =>
            {
                overviewLabel.Clear();
                optimizationsList.Clear();
                
                int enabledCount = data.GetEnabledOptimizationCount();
                
                var countLabel = new Label($"Active Optimizations: {enabledCount}");
                countLabel.style.fontSize = 13;
                countLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                overviewLabel.Add(countLabel);
                
                YUCPUIToolkitHelper.AddSpacing(optimizationsList, 5);
                
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
                        var label = new Label($"  {opt.name}");
                        label.style.fontSize = 11;
                        label.style.color = new StyleColor(Color.green);
                        optimizationsList.Add(label);
                    }
                }
                
                if (enabledCount == 0)
                {
                    var noOptLabel = new Label("  No optimizations enabled");
                    noOptLabel.style.fontSize = 11;
                    noOptLabel.style.color = new StyleColor(Color.yellow);
                    optimizationsList.Add(noOptLabel);
                }
            }).Every(100);
        }
        
        private void UpdatePresetBuildStatistics(VisualElement container)
        {
            var refreshButton = YUCPUIToolkitHelper.CreateButton("Refresh Build Statistics", () =>
            {
                Repaint();
                EditorUtility.SetDirty(data);
            }, YUCPUIToolkitHelper.ButtonVariant.Secondary);
            refreshButton.style.height = 25;
            container.Add(refreshButton);
            
            YUCPUIToolkitHelper.AddSpacing(container, 5);
            
            var buildStatusContainer = new VisualElement();
            buildStatusContainer.name = "build-status";
            container.Add(buildStatusContainer);
            
            var buildStatsFoldout = YUCPUIToolkitHelper.CreateFoldout("Detailed Build Statistics", showBuildStats);
            buildStatsFoldout.RegisterValueChangedCallback(evt => { showBuildStats = evt.newValue; });
            
            var buildStatsContent = new VisualElement();
            buildStatsContent.name = "build-stats-content";
            buildStatsFoldout.Add(buildStatsContent);
            container.Add(buildStatsFoldout);
            
            container.schedule.Execute(() =>
            {
                buildStatusContainer.Clear();
                buildStatsContent.Clear();
                
                if (data.OptimizerDetected)
                {
                    if (data.OptimizationApplied)
                    {
                        buildStatusContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                            $"Last Build Result: Successfully applied {data.OptimizationsApplied} optimization settings to avatar",
                            YUCPUIToolkitHelper.MessageType.Info));
                    }
                    else if (!string.IsNullOrEmpty(data.BuildError))
                    {
                        buildStatusContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                            $"Last Build Result: {data.BuildError}",
                            YUCPUIToolkitHelper.MessageType.Warning));
                    }
                    else if (!data.enableOptimizer)
                    {
                        buildStatusContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                            "Last Build Result: Optimizer was disabled",
                            YUCPUIToolkitHelper.MessageType.Info));
                    }
                }
                else
                {
                    buildStatusContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                        "Build Status: Not yet applied\n\n" +
                        "To see optimization results:\n" +
                        "1. Configure your desired settings above\n" +
                        "2. Upload your avatar using VRChat SDK\n" +
                        "3. Check this section for confirmation\n\n" +
                        "Note: Statistics are saved during actual builds/uploads.\n" +
                        "NDMF 'Apply on Play' preview mode applies settings but doesn't persist statistics.\n" +
                        "Check the Console for confirmation messages during previews.",
                        YUCPUIToolkitHelper.MessageType.Info));
                }
                
                if (showBuildStats)
                {
                    var optimizerDetectedLabel = new Label($"Optimizer Detected: {(data.OptimizerDetected ? "Yes" : "No")}");
                    optimizerDetectedLabel.SetEnabled(false);
                    optimizerDetectedLabel.style.fontSize = 11;
                    buildStatsContent.Add(optimizerDetectedLabel);
                    
                    var optimizationAppliedLabel = new Label($"Optimization Applied: {(data.OptimizationApplied ? "Yes" : "No")}");
                    optimizationAppliedLabel.SetEnabled(false);
                    optimizationAppliedLabel.style.fontSize = 11;
                    buildStatsContent.Add(optimizationAppliedLabel);
                    
                    var settingsAppliedLabel = new Label($"Settings Applied: {data.OptimizationsApplied}");
                    settingsAppliedLabel.SetEnabled(false);
                    settingsAppliedLabel.style.fontSize = 11;
                    buildStatsContent.Add(settingsAppliedLabel);
                    
                    if (!string.IsNullOrEmpty(data.BuildError))
                    {
                        var errorLabel = new Label($"Error: {data.BuildError}");
                        errorLabel.SetEnabled(false);
                        errorLabel.style.fontSize = 11;
                        buildStatsContent.Add(errorLabel);
                    }
                }
            }).Every(100);
        }
        
        private void UpdateAdvancedMode(VisualElement container)
        {
            if (container == null) return;
            
            container.Clear();
            
            var enableCard = YUCPUIToolkitHelper.CreateCard("Enable Optimization", "Configure optimizer settings");
            var enableContent = YUCPUIToolkitHelper.GetCardContent(enableCard);
            enableContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("enableOptimizer"), "Enable Optimizer"));
            enableContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("useAutoSettings"), "Use Auto Settings"));
            
            var autoSettingsWarning = new VisualElement();
            autoSettingsWarning.name = "auto-settings-warning";
            enableContent.Add(autoSettingsWarning);
            container.Add(enableCard);
            
            var disabledWarning = new VisualElement();
            disabledWarning.name = "disabled-warning";
            container.Add(disabledWarning);
            
            container.schedule.Execute(() =>
            {
                autoSettingsWarning.Clear();
                disabledWarning.Clear();
                
                if (data.useAutoSettings)
                {
                    autoSettingsWarning.Add(YUCPUIToolkitHelper.CreateHelpBox(
                        "Auto Settings is ON - manual settings below will be overridden by auto-detection.",
                        YUCPUIToolkitHelper.MessageType.Warning));
                }
                
                if (!data.enableOptimizer)
                {
                    disabledWarning.Add(YUCPUIToolkitHelper.CreateHelpBox(
                        "Optimization disabled for this avatar.",
                        YUCPUIToolkitHelper.MessageType.Warning));
                }
            }).Every(100);
            
            if (!data.enableOptimizer)
            {
                return;
            }
            
            YUCPUIToolkitHelper.AddSpacing(container, 10);
            
            // Mesh Merging Foldout
            var meshMergingFoldout = YUCPUIToolkitHelper.CreateFoldout("Mesh Merging", showMeshMerging);
            meshMergingFoldout.RegisterValueChangedCallback(evt => { showMeshMerging = evt.newValue; });
            meshMergingFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("mergeSkinnedMeshes"), "Merge Skinned Meshes"));
            meshMergingFoldout.Add(YUCPUIToolkitHelper.CreateHelpBox("Combines multiple skinned meshes to reduce draw calls.", YUCPUIToolkitHelper.MessageType.None));
            
            var staticMeshField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("mergeStaticMeshesAsSkinned"), "Include Static Meshes");
            staticMeshField.name = "static-mesh-field";
            meshMergingFoldout.Add(staticMeshField);
            
            var staticMeshHelp = new VisualElement();
            staticMeshHelp.name = "static-mesh-help";
            meshMergingFoldout.Add(staticMeshHelp);
            container.Add(meshMergingFoldout);
            
            container.schedule.Execute(() =>
            {
                staticMeshField.style.display = data.mergeSkinnedMeshes ? DisplayStyle.Flex : DisplayStyle.None;
                staticMeshHelp.Clear();
                if (data.mergeSkinnedMeshes)
                {
                    staticMeshHelp.Add(YUCPUIToolkitHelper.CreateHelpBox("Converts static meshes to skinned so they can be merged.", YUCPUIToolkitHelper.MessageType.None));
                }
            }).Every(100);
            
            // Advanced Mesh Merging Foldout
            var advancedMergingFoldout = YUCPUIToolkitHelper.CreateFoldout("Advanced Mesh Merging", showAdvancedMerging);
            advancedMergingFoldout.RegisterValueChangedCallback(evt => { showAdvancedMerging = evt.newValue; });
            
            var shaderToggleLabel = new Label("Shader Toggle Merging");
            shaderToggleLabel.style.fontSize = 13;
            shaderToggleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            advancedMergingFoldout.Add(shaderToggleLabel);
            advancedMergingFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("mergeSkinnedMeshesWithShaderToggle"), "Shader Toggle Level"));
            advancedMergingFoldout.Add(YUCPUIToolkitHelper.CreateHelpBox("0=Off, 1=Basic, 2=Advanced | Requires Windows build target", YUCPUIToolkitHelper.MessageType.None));
            
            var windowsWarning = new VisualElement();
            windowsWarning.name = "windows-warning";
            advancedMergingFoldout.Add(windowsWarning);
            
            YUCPUIToolkitHelper.AddSpacing(advancedMergingFoldout, 5);
            
            var nanimationLabel = new Label("NaNimation Merging");
            nanimationLabel.style.fontSize = 13;
            nanimationLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            advancedMergingFoldout.Add(nanimationLabel);
            advancedMergingFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("mergeSkinnedMeshesWithNaNimation"), "NaNimation Level"));
            advancedMergingFoldout.Add(YUCPUIToolkitHelper.CreateHelpBox("0=Off, 1=Basic, 2=Advanced | Uses NaN in animations for visibility", YUCPUIToolkitHelper.MessageType.None));
            
            var nanimationFields = new VisualElement();
            nanimationFields.name = "nanimation-fields";
            advancedMergingFoldout.Add(nanimationFields);
            container.Add(advancedMergingFoldout);
            
            container.schedule.Execute(() =>
            {
                windowsWarning.Clear();
                if (data.mergeSkinnedMeshesWithShaderToggle > 0 && EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                {
                    windowsWarning.Add(YUCPUIToolkitHelper.CreateHelpBox("Shader toggles require Windows build target!", YUCPUIToolkitHelper.MessageType.Warning));
                }
                
                nanimationFields.Clear();
                if (data.mergeSkinnedMeshesWithNaNimation > 0)
                {
                    nanimationFields.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("naNimationAllow3BoneSkinning"), "Allow 3-Bone Skinning"));
                    nanimationFields.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("mergeSkinnedMeshesSeparatedByDefaultEnabledState"), "Separate by Default State"));
                }
            }).Every(100);
            
            // Material Foldout
            var materialFoldout = YUCPUIToolkitHelper.CreateFoldout("Material Optimization", showMaterialOptimization);
            materialFoldout.RegisterValueChangedCallback(evt => { showMaterialOptimization = evt.newValue; });
            materialFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("mergeDifferentPropertyMaterials"), "Merge Different Property Materials"));
            materialFoldout.Add(YUCPUIToolkitHelper.CreateHelpBox("Creates optimized shaders combining multiple materials. Windows only.", YUCPUIToolkitHelper.MessageType.None));
            
            var materialFields = new VisualElement();
            materialFields.name = "material-fields";
            materialFoldout.Add(materialFields);
            container.Add(materialFoldout);
            
            container.schedule.Execute(() =>
            {
                materialFields.Clear();
                if (data.mergeDifferentPropertyMaterials)
                {
                    materialFields.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("mergeSameDimensionTextures"), "Merge Textures to Arrays"));
                    
                    var mainTexField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("mergeMainTex"), "Include Main Textures");
                    mainTexField.name = "main-tex-field";
                    materialFields.Add(mainTexField);
                    
                    container.schedule.Execute(() =>
                    {
                        mainTexField.style.display = data.mergeSameDimensionTextures ? DisplayStyle.Flex : DisplayStyle.None;
                    }).Every(100);
                    
                    YUCPUIToolkitHelper.AddSpacing(materialFields, 3);
                    materialFields.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("writePropertiesAsStaticValues"), "Write Static Properties"));
                }
            }).Every(100);
            
            // FX Layer Foldout
            var fxLayerFoldout = YUCPUIToolkitHelper.CreateFoldout("FX Layer Optimization", showFXLayer);
            fxLayerFoldout.RegisterValueChangedCallback(evt => { showFXLayer = evt.newValue; });
            fxLayerFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("optimizeFXLayer"), "Optimize FX Layer"));
            fxLayerFoldout.Add(YUCPUIToolkitHelper.CreateHelpBox("Merges compatible animator layers and removes unused ones.", YUCPUIToolkitHelper.MessageType.None));
            
            var animationCombineField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("combineApproximateMotionTimeAnimations"), "Combine Similar-Length Animations");
            animationCombineField.name = "animation-combine-field";
            fxLayerFoldout.Add(animationCombineField);
            container.Add(fxLayerFoldout);
            
            container.schedule.Execute(() =>
            {
                animationCombineField.style.display = data.optimizeFXLayer ? DisplayStyle.Flex : DisplayStyle.None;
            }).Every(100);
            
            // Component Cleanup Foldout
            var componentsFoldout = YUCPUIToolkitHelper.CreateFoldout("Component Cleanup", showComponents);
            componentsFoldout.RegisterValueChangedCallback(evt => { showComponents = evt.newValue; });
            componentsFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("disablePhysBonesWhenUnused"), "Disable Unused PhysBones"));
            componentsFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("deleteUnusedComponents"), "Delete Unused Components"));
            componentsFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("deleteUnusedGameObjects"), "Delete Unused GameObjects"));
            componentsFoldout.Add(YUCPUIToolkitHelper.CreateHelpBox("0=Off, 1=Safe, 2=Aggressive", YUCPUIToolkitHelper.MessageType.None));
            container.Add(componentsFoldout);
            
            // Blendshape Foldout
            var blendshapeFoldout = YUCPUIToolkitHelper.CreateFoldout("Blendshape Optimization", showBlendshapes);
            blendshapeFoldout.RegisterValueChangedCallback(evt => { showBlendshapes = evt.newValue; });
            blendshapeFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("mergeSameRatioBlendShapes"), "Merge Same-Ratio Blendshapes"));
            blendshapeFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("mmdCompatibility"), "MMD Compatibility"));
            container.Add(blendshapeFoldout);
            
            // Advanced Features Foldout
            var advancedFeaturesFoldout = YUCPUIToolkitHelper.CreateFoldout("Advanced Features", showAdvancedFeatures);
            advancedFeaturesFoldout.RegisterValueChangedCallback(evt => { showAdvancedFeatures = evt.newValue; });
            advancedFeaturesFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("useRingFingerAsFootCollider"), "Ring Finger → Foot Collider"));
            advancedFeaturesFoldout.Add(YUCPUIToolkitHelper.CreateHelpBox("Moves ring finger contact to feet. Disables ring finger interactions.", YUCPUIToolkitHelper.MessageType.None));
            container.Add(advancedFeaturesFoldout);
            
            // Exclusions Foldout
            var exclusionsFoldout = YUCPUIToolkitHelper.CreateFoldout("Exclusions", showExclusions);
            exclusionsFoldout.RegisterValueChangedCallback(evt => { showExclusions = evt.newValue; });
            exclusionsFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("excludeTransforms"), "Excluded Transforms"));
            container.Add(exclusionsFoldout);
            
            // Inspector Display Foldout
            var inspectorFoldout = YUCPUIToolkitHelper.CreateFoldout("Inspector Display", showInspectorOptions);
            inspectorFoldout.RegisterValueChangedCallback(evt => { showInspectorOptions = evt.newValue; });
            inspectorFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("showMeshMergePreview"), "Show Mesh Merge Preview"));
            inspectorFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("showFXLayerMergeResults"), "Show FX Layer Results"));
            inspectorFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("showDebugInfo"), "Show Debug Info"));
            container.Add(inspectorFoldout);
            
            // Debug & Profiling Foldout
            var debugFoldout = YUCPUIToolkitHelper.CreateFoldout("Debug & Profiling", showDebug);
            debugFoldout.RegisterValueChangedCallback(evt => { showDebug = evt.newValue; });
            debugFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("profileTimeUsed"), "Profile Time Used"));
            debugFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("debugMode"), "Debug Logging"));
            container.Add(debugFoldout);
            
            YUCPUIToolkitHelper.AddSpacing(container, 10);
            
            UpdateAdvancedBuildStatistics(container);
        }
        
        private void UpdateAdvancedBuildStatistics(VisualElement container)
        {
            var refreshButton = YUCPUIToolkitHelper.CreateButton("Refresh Build Statistics", () =>
            {
                Repaint();
                EditorUtility.SetDirty(data);
            }, YUCPUIToolkitHelper.ButtonVariant.Secondary);
            refreshButton.style.height = 25;
            container.Add(refreshButton);
            
            YUCPUIToolkitHelper.AddSpacing(container, 5);
            
            var buildStatusContainer = new VisualElement();
            buildStatusContainer.name = "build-status";
            container.Add(buildStatusContainer);
            
            var buildStatsFoldout = YUCPUIToolkitHelper.CreateFoldout("Detailed Build Statistics", showBuildStats);
            buildStatsFoldout.RegisterValueChangedCallback(evt => { showBuildStats = evt.newValue; });
            
            var buildStatsContent = new VisualElement();
            buildStatsContent.name = "build-stats-content";
            buildStatsFoldout.Add(buildStatsContent);
            container.Add(buildStatsFoldout);
            
            container.schedule.Execute(() =>
            {
                buildStatusContainer.Clear();
                buildStatsContent.Clear();
                
                if (data.OptimizerDetected)
                {
                    if (data.OptimizationApplied)
                    {
                        buildStatusContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                            $"Last Build Result: Successfully applied {data.OptimizationsApplied} optimization settings to avatar",
                            YUCPUIToolkitHelper.MessageType.Info));
                    }
                    else if (!string.IsNullOrEmpty(data.BuildError))
                    {
                        buildStatusContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                            $"Last Build Result: {data.BuildError}",
                            YUCPUIToolkitHelper.MessageType.Warning));
                    }
                    else if (!data.enableOptimizer)
                    {
                        buildStatusContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                            "Last Build Result: Optimizer was disabled",
                            YUCPUIToolkitHelper.MessageType.Info));
                    }
                }
                else
                {
                    buildStatusContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                        "Build Status: Not yet applied\n\n" +
                        "To see optimization results:\n" +
                        "1. Configure your desired settings above\n" +
                        "2. Upload your avatar using VRChat SDK\n" +
                        "3. Check this section for confirmation\n\n" +
                        "Note: Statistics are saved during actual builds/uploads.\n" +
                        "NDMF 'Apply on Play' preview mode applies settings but doesn't persist statistics.\n" +
                        "Check the Console for confirmation messages during previews.",
                        YUCPUIToolkitHelper.MessageType.Info));
                }
                
                if (showBuildStats)
                {
                    var optimizerDetectedLabel = new Label($"Optimizer Detected: {(data.OptimizerDetected ? "Yes" : "No")}");
                    optimizerDetectedLabel.SetEnabled(false);
                    optimizerDetectedLabel.style.fontSize = 11;
                    buildStatsContent.Add(optimizerDetectedLabel);
                    
                    var optimizationAppliedLabel = new Label($"Optimization Applied: {(data.OptimizationApplied ? "Yes" : "No")}");
                    optimizationAppliedLabel.SetEnabled(false);
                    optimizationAppliedLabel.style.fontSize = 11;
                    buildStatsContent.Add(optimizationAppliedLabel);
                    
                    var settingsAppliedLabel = new Label($"Settings Applied: {data.OptimizationsApplied}");
                    settingsAppliedLabel.SetEnabled(false);
                    settingsAppliedLabel.style.fontSize = 11;
                    buildStatsContent.Add(settingsAppliedLabel);
                    
                    if (!string.IsNullOrEmpty(data.BuildError))
                    {
                        var errorLabel = new Label($"Error: {data.BuildError}");
                        errorLabel.SetEnabled(false);
                        errorLabel.style.fontSize = 11;
                        buildStatsContent.Add(errorLabel);
                    }
                }
            }).Every(100);
        }

        public override void OnInspectorGUI()
        {
            // Legacy support - not used anymore
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


        private void SaveUIMode()
        {
            string modeKey = $"YUCP_OptimizerPlugin_UIMode_{data.GetInstanceID()}";
            SessionState.SetInt(modeKey, (int)currentMode);
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
            data.mmdCompatibility = false;
            data.writePropertiesAsStaticValues = true;
            data.useRingFingerAsFootCollider = true;
            
            EditorUtility.SetDirty(data);
            serializedObject.Update();
            
            Debug.Log("[AvatarOptimizerPlugin] Applied Aggressive preset");
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
