using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using System.Collections.Generic;
using System.Linq;
using YUCP.UI.DesignSystem.Utilities;

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
        
        // Track previous values to reduce unnecessary UI updates
        private Component previousVrcFuryToggle = null;
        private string previousValidationError = null;
        private bool previousCreateToggle = false;
        private bool previousUseExistingToggle = false;
        private Component previousSelectedToggle = null;
        private bool previousHasUVDiscardToggle = false;
        private ApplicationMode previousAppMode = ApplicationMode.AutoDetect;
        private Material[] previousTargetMaterials = null;
        private SkinnedMeshRenderer previousTargetBodyMesh = null;
        private Mesh previousDetectedMesh = null;
        private int cachedDetectedUVChannel = 1;
        private Material[] previousBodyMeshMaterials = null; // Track materials on body mesh

        // Foldout states
        private bool showSmartDetection = false;
        private bool showAdvancedOptions = false;

        private void OnEnable()
        {
            data = (AutoBodyHiderData)target;
            
            lastDetectionMethod = data.detectionMethod;
            lastProximityThreshold = data.proximityThreshold;
            lastRaycastDistance = data.raycastDistance;
            lastSmartRayDirections = data.smartRayDirections;
            lastSmartOcclusionThreshold = data.smartOcclusionThreshold;
            lastSmartUseNormals = data.smartUseNormals;
            lastSmartRequireBidirectional = data.smartRequireBidirectional;
            lastManualMask = data.manualMask;
            lastManualMaskThreshold = data.manualMaskThreshold;
            
            // Initialize toggle section state
            previousCreateToggle = data.createToggle;
            previousUseExistingToggle = data.useExistingToggle;
            previousSelectedToggle = data.selectedToggle;
            previousHasUVDiscardToggle = data.GetComponent<UVDiscardToggleData>() != null;
            previousAppMode = data.applicationMode;
            previousVrcFuryToggle = null;
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Auto Body Hider"));
            
            var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(AutoBodyHiderData));
            if (betaWarning != null) root.Add(betaWarning);
            
            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(AutoBodyHiderData));
            if (supportBanner != null) root.Add(supportBanner);
            
            // VRCFury integration banner (will be updated dynamically)
            var vrcFuryBanner = new VisualElement();
            vrcFuryBanner.name = "vrcfury-banner";
            root.Add(vrcFuryBanner);
            
            // Target Meshes Card
            var targetCard = YUCPUIToolkitHelper.CreateCard("Target Meshes", "Configure body and clothing meshes");
            var targetContent = YUCPUIToolkitHelper.GetCardContent(targetCard);
            targetContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("targetBodyMesh"), "Body Mesh"));
            targetContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("clothingMeshes"), "Clothing Meshes"));
            root.Add(targetCard);
            
            // Detection Settings Card
            var detectionCard = YUCPUIToolkitHelper.CreateCard("Detection Settings", "Configure body hiding detection method");
            var detectionContent = YUCPUIToolkitHelper.GetCardContent(detectionCard);
            detectionContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("detectionMethod"), "Detection Method"));
            detectionContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("safetyMargin"), "Safety Margin"));
            
            var proximityThresholdField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("proximityThreshold"), "Proximity Threshold");
            proximityThresholdField.name = "proximity-threshold";
            detectionContent.Add(proximityThresholdField);
            
            var raycastDistanceField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("raycastDistance"), "Raycast Distance");
            raycastDistanceField.name = "raycast-distance";
            detectionContent.Add(raycastDistanceField);
            
            var hybridExpansionField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("hybridExpansionFactor"), "Expansion Factor");
            hybridExpansionField.name = "hybrid-expansion";
            detectionContent.Add(hybridExpansionField);
            
            var manualMaskField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("manualMask"), "Manual Mask");
            manualMaskField.name = "manual-mask";
            detectionContent.Add(manualMaskField);
            
            var manualMaskThresholdField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("manualMaskThreshold"), "Mask Threshold");
            manualMaskThresholdField.name = "manual-mask-threshold";
            detectionContent.Add(manualMaskThresholdField);
            
            root.Add(detectionCard);
            
            // Smart Detection Foldout
            var smartFoldout = YUCPUIToolkitHelper.CreateFoldout("Smart Detection Settings", showSmartDetection);
            smartFoldout.RegisterValueChangedCallback(evt => { showSmartDetection = evt.newValue; });
            smartFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("smartRayDirections"), "Ray Directions"));
            smartFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("smartOcclusionThreshold"), "Occlusion Threshold"));
            smartFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("smartUseNormals"), "Use Normals"));
            smartFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("smartRequireBidirectional"), "Require Bidirectional"));
            smartFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("smartRayOffset"), "Ray Offset"));
            smartFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("smartConservativeMode"), "Conservative Mode"));
            smartFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("smartMinDistanceToClothing"), "Min Distance to Clothing"));
            smartFoldout.name = "smart-detection-foldout";
            root.Add(smartFoldout);
            
            // Application Mode Card
            var appModeCard = YUCPUIToolkitHelper.CreateCard("Application Mode", "Configure how body hiding is applied");
            var appModeContent = YUCPUIToolkitHelper.GetCardContent(appModeCard);
            appModeContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("applicationMode"), "Mode"));
            var autoDetectHelp = YUCPUIToolkitHelper.CreateHelpBox("Auto-detect will use UDIM Discard for Poiyomi/FastFur shaders, Mesh Deletion for others.", YUCPUIToolkitHelper.MessageType.Info);
            autoDetectHelp.name = "auto-detect-help";
            appModeContent.Add(autoDetectHelp);
            
            // Material selection for UDIM Discard mode
            var materialPickerContainer = new VisualElement();
            materialPickerContainer.name = "material-picker-container";
            
            // Store references for dynamic updates
            var currentSelectionContainer = new VisualElement();
            currentSelectionContainer.name = "current-material-selection";
            currentSelectionContainer.style.flexDirection = FlexDirection.Row;
            currentSelectionContainer.style.marginBottom = 10;
            currentSelectionContainer.style.paddingTop = 5;
            currentSelectionContainer.style.paddingBottom = 5;
            currentSelectionContainer.style.paddingLeft = 8;
            currentSelectionContainer.style.paddingRight = 8;
            currentSelectionContainer.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 1f));
            currentSelectionContainer.style.borderTopLeftRadius = 6;
            currentSelectionContainer.style.borderTopRightRadius = 6;
            currentSelectionContainer.style.borderBottomLeftRadius = 6;
            currentSelectionContainer.style.borderBottomRightRadius = 6;
            
            var currentMaterialPreview = new Image();
            currentMaterialPreview.name = "current-material-preview";
            currentMaterialPreview.style.width = 40;
            currentMaterialPreview.style.height = 40;
            currentMaterialPreview.style.marginRight = 8;
            currentMaterialPreview.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 1f));
            currentSelectionContainer.Add(currentMaterialPreview);
            
            var currentMaterialInfo = new VisualElement();
            currentMaterialInfo.style.flexGrow = 1;
            
            var currentMaterialName = new Label("None (Auto-detect)");
            currentMaterialName.name = "current-material-name";
            currentMaterialName.style.fontSize = 13;
            currentMaterialName.style.unityFontStyleAndWeight = FontStyle.Bold;
            currentMaterialInfo.Add(currentMaterialName);
            
            var currentMaterialShader = new Label("Will auto-detect all compatible materials");
            currentMaterialShader.name = "current-material-shader";
            currentMaterialShader.style.fontSize = 11;
            currentMaterialShader.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f, 1f));
            currentMaterialInfo.Add(currentMaterialShader);
            
            // Selected materials list (for multiple selection)
            var selectedMaterialsList = new VisualElement();
            selectedMaterialsList.name = "selected-materials-list";
            selectedMaterialsList.style.marginTop = 5;
            currentMaterialInfo.Add(selectedMaterialsList);
            
            currentSelectionContainer.Add(currentMaterialInfo);
            
            var clearMaterialButton = new Button(() => {
                var targetMaterialsProp = serializedObject.FindProperty("targetMaterials");
                targetMaterialsProp.arraySize = 0;
                serializedObject.ApplyModifiedProperties();
            });
            clearMaterialButton.text = "Clear All";
            clearMaterialButton.style.height = 24;
            clearMaterialButton.style.width = 80;
            clearMaterialButton.style.marginLeft = 8;
            currentSelectionContainer.Add(clearMaterialButton);
            
            materialPickerContainer.Add(currentSelectionContainer);
            
            // Material grid
            var materialGridContainer = new VisualElement();
            materialGridContainer.name = "material-grid-container";
            materialGridContainer.style.flexDirection = FlexDirection.Row;
            materialGridContainer.style.flexWrap = Wrap.Wrap;
            materialGridContainer.style.marginTop = 5;
            
            materialPickerContainer.Add(materialGridContainer);
            
            var materialHelpContainer = new VisualElement();
            materialHelpContainer.name = "material-help-container";
            materialPickerContainer.Add(materialHelpContainer);
            
            // Store references for updates - add label at the beginning
            var materialPickerLabel = new Label("Target Materials (Optional)");
            materialPickerLabel.style.fontSize = 13;
            materialPickerLabel.style.marginBottom = 5;
            materialPickerContainer.Insert(0, materialPickerLabel);
            
            appModeContent.Add(materialPickerContainer);
            
            root.Add(appModeCard);
            
            // UDIM Discard Settings Card (conditional)
            var udimCard = YUCPUIToolkitHelper.CreateCard("UDIM Discard Settings", "Configure UDIM tile coordinates");
            var udimContent = YUCPUIToolkitHelper.GetCardContent(udimCard);
            
            // Auto-detect UV channel toggle
            var autoDetectUVField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("autoDetectUVChannel"), "Auto-Detect UV Channel");
            udimContent.Add(autoDetectUVField);
            
            // Detected UV channel display (when auto-detect is enabled)
            var detectedUVContainer = new VisualElement();
            detectedUVContainer.name = "detected-uv-container";
            udimContent.Add(detectedUVContainer);
            
            // Manual UV channel field (will be moved to advanced, but keep reference for now)
            var manualUVChannelField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("udimUVChannel"), "UV Channel");
            manualUVChannelField.name = "manual-uv-channel-field";
            
            // Auto-assign tile toggle
            var autoAssignTileProp = serializedObject.FindProperty("autoAssignUDIMTile");
            udimContent.Add(YUCPUIToolkitHelper.CreateField(autoAssignTileProp, "Auto Assign Tile"));
            
            // Tile assignment info display when auto-assigned
            var tileInfoContainer = new VisualElement();
            tileInfoContainer.name = "tile-info-container";
            tileInfoContainer.style.marginTop = 5;
            tileInfoContainer.style.marginBottom = 5;
            tileInfoContainer.style.paddingTop = 6;
            tileInfoContainer.style.paddingBottom = 6;
            tileInfoContainer.style.paddingLeft = 8;
            tileInfoContainer.style.paddingRight = 8;
            tileInfoContainer.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 1f));
            tileInfoContainer.style.borderTopLeftRadius = 4;
            tileInfoContainer.style.borderTopRightRadius = 4;
            tileInfoContainer.style.borderBottomLeftRadius = 4;
            tileInfoContainer.style.borderBottomRightRadius = 4;
            
            var tileInfoLabel = new Label("Tile: Auto-assigned by orchestrator");
            tileInfoLabel.name = "tile-info-label";
            tileInfoLabel.style.fontSize = 11;
            tileInfoLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f, 1f));
            tileInfoContainer.Add(tileInfoLabel);
            udimContent.Add(tileInfoContainer);
            
            // Manual tile assignment shown when auto-assign is disabled
            var manualTileContainer = new VisualElement();
            manualTileContainer.name = "manual-tile-container";
            manualTileContainer.style.marginTop = 5;
            
            var rowColumnContainer = new VisualElement();
            rowColumnContainer.style.flexDirection = FlexDirection.Row;
            rowColumnContainer.style.marginBottom = 5;
            var rowField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("udimDiscardRow"), "Row");
            rowField.style.flexGrow = 1;
            rowField.style.marginRight = 5;
            rowColumnContainer.Add(rowField);
            var columnField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("udimDiscardColumn"), "Column");
            columnField.style.flexGrow = 1;
            rowColumnContainer.Add(columnField);
            manualTileContainer.Add(rowColumnContainer);
            
            var manualTileHelp = YUCPUIToolkitHelper.CreateHelpBox(
                "Manually specify the UDIM tile coordinates. Make sure this tile is not used by other clothing pieces on the same body mesh.",
                YUCPUIToolkitHelper.MessageType.Info);
            manualTileContainer.Add(manualTileHelp);
            
            udimContent.Add(manualTileContainer);
            udimCard.name = "udim-card";
            root.Add(udimCard);
            
            // VRCFury Toggle Section
            var toggleCard = YUCPUIToolkitHelper.CreateCard("VRCFury Toggle", "Configure toggle integration with VRCFury");
            var toggleCardContent = YUCPUIToolkitHelper.GetCardContent(toggleCard);
            toggleCard.name = "toggle-card";
            root.Add(toggleCard);
            
            var toggleSection = new VisualElement();
            toggleSection.name = "toggle-section";
            toggleCardContent.Add(toggleSection);
            
            // Advanced Options Foldout
            var advancedFoldout = YUCPUIToolkitHelper.CreateFoldout("Advanced Options", showAdvancedOptions);
            advancedFoldout.RegisterValueChangedCallback(evt => { showAdvancedOptions = evt.newValue; });
            advancedFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("mirrorSymmetry"), "Mirror Symmetry"));
            
            YUCPUIToolkitHelper.AddSpacing(advancedFoldout, 3);
            var useBoneFilteringField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("useBoneFiltering"), "Use Bone Filtering");
            advancedFoldout.Add(useBoneFilteringField);
            
            var filterBonesContainer = new VisualElement();
            filterBonesContainer.style.paddingLeft = 15;
            filterBonesContainer.name = "filter-bones-container";
            var filterBonesField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("filterBones"), "Filter Bones");
            filterBonesContainer.Add(filterBonesField);
            advancedFoldout.Add(filterBonesContainer);
            
            YUCPUIToolkitHelper.AddSpacing(advancedFoldout, 3);
            var optimizeTileField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("optimizeTileUsage"), "Optimize Tile Usage");
            advancedFoldout.Add(optimizeTileField);
            var optimizeHelp = YUCPUIToolkitHelper.CreateHelpBox("Reduces UDIM tiles for layered outfits by skipping overlap tiles for fully-covered inner layers.", YUCPUIToolkitHelper.MessageType.Info);
            optimizeHelp.name = "optimize-help";
            advancedFoldout.Add(optimizeHelp);
            
            YUCPUIToolkitHelper.AddSpacing(advancedFoldout, 3);
            advancedFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("debugMode"), "Debug Mode"));
            advancedFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("showPreview"), "Show Preview"));
            root.Add(advancedFoldout);
            
            // Preview Tools Section
            YUCPUIToolkitHelper.AddSpacing(root, 15);
            
            var previewCard = YUCPUIToolkitHelper.CreateCard("Preview", "Visualize what will be hidden before building");
            var previewCardContent = YUCPUIToolkitHelper.GetCardContent(previewCard);
            previewCard.name = "preview-card";
            root.Add(previewCard);
            
            var previewInfo = new VisualElement();
            previewInfo.name = "preview-info";
            previewCardContent.Add(previewInfo);
            
            YUCPUIToolkitHelper.AddSpacing(previewCardContent, 8);
            
            // Single context-sensitive button
            var previewActionButton = YUCPUIToolkitHelper.CreateButton("Generate Preview", () => 
            {
                if (data.previewGenerated)
                {
                    ClearPreview();
                }
                else
                {
                    GeneratePreview();
                }
            }, YUCPUIToolkitHelper.ButtonVariant.Primary);
            previewActionButton.style.height = 36;
            previewActionButton.style.marginBottom = 8;
            previewActionButton.name = "preview-action-button";
            previewCardContent.Add(previewActionButton);
            
            // Clear cache button
            var clearCacheButton = YUCPUIToolkitHelper.CreateButton("Clear Detection Cache", () =>
            {
                DetectionCache.ClearCache();
                EditorUtility.DisplayDialog("Cache Cleared", 
                    "Detection cache has been cleared.\n\nNext build will re-run detection for all Auto Body Hider components.", 
                    "OK");
            }, YUCPUIToolkitHelper.ButtonVariant.Secondary);
            clearCacheButton.style.height = 32;
            clearCacheButton.style.marginBottom = 8;
            clearCacheButton.name = "clear-cache-button";
            previewCardContent.Add(clearCacheButton);
            
            // Help text
            var previewHelp = new Label("Preview shows red faces in Scene view that will be hidden. Toggle 'Show Preview' in Advanced settings to enable visualization.");
            previewHelp.style.fontSize = 11;
            previewHelp.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f, 1f));
            previewHelp.style.whiteSpace = WhiteSpace.Normal;
            previewHelp.style.marginTop = 4;
            previewCardContent.Add(previewHelp);
            
            YUCPUIToolkitHelper.AddSpacing(root, 10);
            
            var validationError = new VisualElement();
            validationError.name = "validation-error";
            root.Add(validationError);
            
            // Initialize toggle section on first load
            var vrcFuryComponents = data.GetComponents<Component>();
            Component initialVrcFuryToggle = null;
            foreach (var comp in vrcFuryComponents)
            {
                if (comp != null && comp.GetType().Name == "VRCFury")
                {
                    initialVrcFuryToggle = comp;
                    break;
                }
            }
            UpdateToggleSection(toggleSection, data, serializedObject, initialVrcFuryToggle);
            
            // Dynamic updates using schedule.Execute
            root.schedule.Execute(() =>
            {
                serializedObject.Update();
                var detectionMethod = serializedObject.FindProperty("detectionMethod");
                var detectionMethodValue = (DetectionMethod)detectionMethod.enumValueIndex;
                
                proximityThresholdField.style.display = (detectionMethodValue == DetectionMethod.Proximity || detectionMethodValue == DetectionMethod.Hybrid) ? DisplayStyle.Flex : DisplayStyle.None;
                raycastDistanceField.style.display = (detectionMethodValue == DetectionMethod.Raycast || detectionMethodValue == DetectionMethod.Hybrid || detectionMethodValue == DetectionMethod.Smart) ? DisplayStyle.Flex : DisplayStyle.None;
                hybridExpansionField.style.display = (detectionMethodValue == DetectionMethod.Hybrid) ? DisplayStyle.Flex : DisplayStyle.None;
                manualMaskField.style.display = (detectionMethodValue == DetectionMethod.Manual) ? DisplayStyle.Flex : DisplayStyle.None;
                manualMaskThresholdField.style.display = (detectionMethodValue == DetectionMethod.Manual) ? DisplayStyle.Flex : DisplayStyle.None;
                smartFoldout.style.display = (detectionMethodValue == DetectionMethod.Smart) ? DisplayStyle.Flex : DisplayStyle.None;
                
                var appMode = serializedObject.FindProperty("applicationMode");
                var appModeValue = (ApplicationMode)appMode.enumValueIndex;
                autoDetectHelp.style.display = (appModeValue == ApplicationMode.AutoDetect) ? DisplayStyle.Flex : DisplayStyle.None;
                udimCard.style.display = (appModeValue == ApplicationMode.UDIMDiscard || appModeValue == ApplicationMode.AutoDetect) ? DisplayStyle.Flex : DisplayStyle.None;
                
                // Toggle card visible for UDIM Discard or Auto Detect
                var toggleCard = root.Q<VisualElement>("toggle-card");
                if (toggleCard != null)
                {
                    toggleCard.style.display = (appModeValue == ApplicationMode.UDIMDiscard || appModeValue == ApplicationMode.AutoDetect) ? DisplayStyle.Flex : DisplayStyle.None;
                }
                
                // Update UV channel detection display
                var autoDetectUVProp = serializedObject.FindProperty("autoDetectUVChannel");
                bool autoDetectEnabled = autoDetectUVProp.boolValue;
                var detectedUVContainer = root.Q<VisualElement>("detected-uv-container");
                var manualUVSection = root.Q<VisualElement>("manual-uv-section");
                
                if (detectedUVContainer != null)
                {
                    // Update if mesh changed or container is empty
                    Mesh currentMesh = data.targetBodyMesh != null ? data.targetBodyMesh.sharedMesh : null;
                    bool meshChanged = currentMesh != previousDetectedMesh;
                    
                    if (meshChanged || detectedUVContainer.childCount == 0)
                    {
                        detectedUVContainer.Clear();
                        
                        if (autoDetectEnabled && currentMesh != null)
                        {
                            // Cache the detection result
                            cachedDetectedUVChannel = UDIMManipulator.DetectBestUVChannel(currentMesh);
                            previousDetectedMesh = currentMesh;
                            
                            var detectedLabel = new Label($"Detected UV Channel: UV{cachedDetectedUVChannel}");
                            detectedLabel.style.fontSize = 12;
                            detectedLabel.style.color = new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f));
                            detectedLabel.style.marginTop = 5;
                            detectedLabel.style.marginBottom = 5;
                            detectedUVContainer.Add(detectedLabel);
                            
                            string channelInfo = cachedDetectedUVChannel == 1 
                                ? "UV1: discard coordinates are written here." 
                                : cachedDetectedUVChannel == 0 
                                    ? "UV0 detected (UV1 not available). The system will create UV1 during processing."
                                    : $"UV{cachedDetectedUVChannel} detected (unusual). Ensure this channel is available on your mesh.";
                            
                            detectedUVContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(channelInfo, YUCPUIToolkitHelper.MessageType.Info));
                        }
                        else if (!autoDetectEnabled)
                        {
                            detectedUVContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                                "Auto-detection disabled. Using manual UV channel selection from Advanced Options.",
                                YUCPUIToolkitHelper.MessageType.None));
                            previousDetectedMesh = null;
                        }
                        else if (currentMesh == null)
                        {
                            previousDetectedMesh = null;
                        }
                    }
                }
                
                if (manualUVSection != null)
                {
                    manualUVSection.style.display = autoDetectEnabled ? DisplayStyle.None : DisplayStyle.Flex;
                }
                
                // Show/hide material picker for UDIM Discard or Auto-Detect
                bool showMaterialField = (appModeValue == ApplicationMode.UDIMDiscard || appModeValue == ApplicationMode.AutoDetect);
                materialPickerContainer.style.display = showMaterialField ? DisplayStyle.Flex : DisplayStyle.None;
                
                // Update material picker when field is visible
                if (showMaterialField)
                {
                    var currentSelection = materialPickerContainer.Q<VisualElement>("current-material-selection");
                    var currentPreview = materialPickerContainer.Q<Image>("current-material-preview");
                    var currentName = materialPickerContainer.Q<Label>("current-material-name");
                    var currentShader = materialPickerContainer.Q<Label>("current-material-shader");
                    var gridContainer = materialPickerContainer.Q<VisualElement>("material-grid-container");
                    var helpContainer = materialPickerContainer.Q<VisualElement>("material-help-container");
                    var selectedMaterialsList = currentSelection != null ? currentSelection.Q<VisualElement>("selected-materials-list") : null;
                    
                    if (currentSelection != null && currentPreview != null && currentName != null && currentShader != null && gridContainer != null && helpContainer != null)
                    {
                        UpdateMaterialPicker(data, serializedObject, currentSelection, currentPreview, currentName, currentShader, gridContainer, helpContainer, selectedMaterialsList);
                    }
                }
                
                var optimizeTileUsage = serializedObject.FindProperty("optimizeTileUsage");
                optimizeHelp.style.display = optimizeTileUsage.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
                
                var useBoneFiltering = serializedObject.FindProperty("useBoneFiltering");
                filterBonesContainer.style.display = useBoneFiltering.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
                
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
                
                if (vrcFuryToggle != previousVrcFuryToggle)
                {
                    UpdateVrcFuryBanner(vrcFuryBanner, vrcFuryToggle);
                    previousVrcFuryToggle = vrcFuryToggle;
                }
                
                // Update toggle section when state changes
                var createToggleProp = serializedObject.FindProperty("createToggle");
                var useExistingToggleProp = serializedObject.FindProperty("useExistingToggle");
                var selectedToggleProp = serializedObject.FindProperty("selectedToggle");
                bool currentCreateToggle = createToggleProp.boolValue;
                bool currentUseExistingToggle = useExistingToggleProp.boolValue;
                Component currentSelectedToggle = selectedToggleProp.objectReferenceValue as Component;
                var uvDiscardToggle = data.GetComponent<UVDiscardToggleData>();
                bool currentHasUVDiscardToggle = uvDiscardToggle != null;
                bool needsToggleUpdate = 
                    currentCreateToggle != previousCreateToggle ||
                    currentUseExistingToggle != previousUseExistingToggle ||
                    currentSelectedToggle != previousSelectedToggle ||
                    currentHasUVDiscardToggle != previousHasUVDiscardToggle ||
                    vrcFuryToggle != previousVrcFuryToggle ||
                    appModeValue != previousAppMode ||
                    toggleSection.childCount == 0; // Update if section is empty on initial load
                
                if (needsToggleUpdate)
                {
                    UpdateToggleSection(toggleSection, data, serializedObject, vrcFuryToggle);
                    previousCreateToggle = currentCreateToggle;
                    previousUseExistingToggle = currentUseExistingToggle;
                    previousSelectedToggle = currentSelectedToggle;
                    previousHasUVDiscardToggle = currentHasUVDiscardToggle;
                    previousAppMode = appModeValue;
                }
                
                UpdatePreviewInfo(previewInfo, data);
                
                // Update tile assignment UI
                var autoAssignTileProp = serializedObject.FindProperty("autoAssignUDIMTile");
                bool autoAssign = autoAssignTileProp.boolValue;
                
                var tileInfoContainer = root.Q<VisualElement>("tile-info-container");
                var manualTileContainer = root.Q<VisualElement>("manual-tile-container");
                
                if (tileInfoContainer != null)
                {
                    tileInfoContainer.style.display = autoAssign ? DisplayStyle.Flex : DisplayStyle.None;
                    
                    if (autoAssign)
                    {
                        var tileInfoLabel = tileInfoContainer.Q<Label>("tile-info-label");
                        if (tileInfoLabel != null)
                        {
                            // Display current assigned tile
                            tileInfoLabel.text = $"Tile: ({data.udimDiscardRow}, {data.udimDiscardColumn}) - Auto-assigned by orchestrator";
                        }
                    }
                }
                
                if (manualTileContainer != null)
                {
                    manualTileContainer.style.display = autoAssign ? DisplayStyle.None : DisplayStyle.Flex;
                }
                
                // Update context-sensitive button
                var previewActionButton = root.Q<Button>("preview-action-button");
                if (previewActionButton != null)
                {
                    bool hasPreview = data.previewGenerated;
                    bool canGenerate = !isGeneratingPreview && ValidateData();
                    
                    // Button is enabled if: generating (to show progress), or has preview (to clear), or can generate
                    previewActionButton.SetEnabled(isGeneratingPreview || hasPreview || canGenerate);
                    
                    if (isGeneratingPreview)
                    {
                        previewActionButton.text = "Generating...";
                        // Keep primary color during generation
                        previewActionButton.RemoveFromClassList("yucp-button-danger");
                        previewActionButton.AddToClassList("yucp-button-primary");
                    }
                    else if (hasPreview)
                    {
                        previewActionButton.text = "Clear Preview";
                        // Change to danger variant for clear action
                        previewActionButton.RemoveFromClassList("yucp-button-primary");
                        previewActionButton.AddToClassList("yucp-button-danger");
                    }
                    else
                    {
                        previewActionButton.text = "Generate Preview";
                        // Use primary variant for generate action
                        previewActionButton.RemoveFromClassList("yucp-button-danger");
                        previewActionButton.AddToClassList("yucp-button-primary");
                    }
                }
                
                string currentValidationError = ValidateData() ? null : GetValidationError();
                if (currentValidationError != previousValidationError)
                {
                    UpdateValidationError(validationError, currentValidationError);
                    previousValidationError = currentValidationError;
                }
                
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
                
                serializedObject.ApplyModifiedProperties();
            }).Every(100);
            
            return root;
        }
        
        private void UpdateVrcFuryBanner(VisualElement container, Component vrcFuryToggle)
        {
            container.Clear();
            if (vrcFuryToggle != null)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "VRCFury Toggle Integration Detected\n\n" +
                    "This Auto Body Hider will work together with the VRCFury Toggle component. " +
                    "The UDIM discard animation will be added to the toggle's actions automatically during build.",
                    YUCPUIToolkitHelper.MessageType.Info));
            }
        }
        
        private void UpdateValidationError(VisualElement container, string error)
        {
            container.Clear();
            if (error != null)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(error, YUCPUIToolkitHelper.MessageType.Error));
            }
        }
        
        private void UpdateToggleSection(VisualElement container, AutoBodyHiderData data, SerializedObject so, Component vrcFuryToggle)
        {
            container.Clear();
            
            var appMode = so.FindProperty("applicationMode");
            var appModeValue = (ApplicationMode)appMode.enumValueIndex;
            
            // Show toggle section with content
            if (appModeValue != ApplicationMode.UDIMDiscard && appModeValue != ApplicationMode.AutoDetect)
            {
                // Show message that toggle is available for UDIM Discard mode
                var helpBox = YUCPUIToolkitHelper.CreateHelpBox(
                    "Toggle options are only available when using UDIM Discard mode (or Auto Detect with compatible shaders).",
                    YUCPUIToolkitHelper.MessageType.Info);
                container.Add(helpBox);
                return;
            }
            
            YUCPUIToolkitHelper.AddSpacing(container, 5);
            var useExistingToggleProp = so.FindProperty("useExistingToggle");
            var createToggleProp = so.FindProperty("createToggle");
            var selectedToggleProp = so.FindProperty("selectedToggle");
            var uvDiscardToggle = data.GetComponent<UVDiscardToggleData>();
            var hasVRCFuryToggle = vrcFuryToggle != null;
            
            // Get all available VRCFury toggles from GameObject and children
            var availableToggles = GetAvailableVRCFuryToggles(data.gameObject);
            
            // Use Existing Toggle section
            container.Add(YUCPUIToolkitHelper.CreateField(useExistingToggleProp, "Use Existing Toggle"));
            
            if (useExistingToggleProp.boolValue)
            {
                var toggleSelectionContainer = new VisualElement();
                toggleSelectionContainer.style.paddingLeft = 15;
                toggleSelectionContainer.name = "toggle-selection-container";
                
                if (availableToggles.Count == 0)
                {
                    toggleSelectionContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                        "No VRCFury Toggle components found on this GameObject or its children.",
                        YUCPUIToolkitHelper.MessageType.Warning));
                }
                else
                {
                    // Current selection display
                    var currentToggleContainer = new VisualElement();
                    currentToggleContainer.name = "current-toggle-selection";
                    currentToggleContainer.style.flexDirection = FlexDirection.Row;
                    currentToggleContainer.style.marginBottom = 10;
                    currentToggleContainer.style.paddingTop = 8;
                    currentToggleContainer.style.paddingBottom = 8;
                    currentToggleContainer.style.paddingLeft = 10;
                    currentToggleContainer.style.paddingRight = 10;
                    currentToggleContainer.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 1f));
                    currentToggleContainer.style.borderTopLeftRadius = 6;
                    currentToggleContainer.style.borderTopRightRadius = 6;
                    currentToggleContainer.style.borderBottomLeftRadius = 6;
                    currentToggleContainer.style.borderBottomRightRadius = 6;
                    
                    var currentToggleInfo = new VisualElement();
                    currentToggleInfo.style.flexGrow = 1;
                    
                    var currentToggleName = new Label("None Selected");
                    currentToggleName.name = "current-toggle-name";
                    currentToggleName.style.fontSize = 13;
                    currentToggleName.style.unityFontStyleAndWeight = FontStyle.Bold;
                    currentToggleInfo.Add(currentToggleName);
                    
                    var currentTogglePath = new Label("Select a toggle from the grid below");
                    currentTogglePath.name = "current-toggle-path";
                    currentTogglePath.style.fontSize = 11;
                    currentTogglePath.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f, 1f));
                    currentToggleInfo.Add(currentTogglePath);
                    
                    currentToggleContainer.Add(currentToggleInfo);
                    
                    var clearToggleButton = new Button(() => {
                        selectedToggleProp.objectReferenceValue = null;
                        so.ApplyModifiedProperties();
                        UpdateCurrentToggleDisplay(null, data, currentToggleName, currentTogglePath);
                        // Update all cards' border colors
                        var gridContainer = toggleSelectionContainer.Q<VisualElement>("toggle-grid-container");
                        if (gridContainer != null)
                        {
                            foreach (var child in gridContainer.Children())
                            {
                                if (child is VisualElement childCard)
                                {
                                    var childBorderColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 1f));
                                    childCard.style.borderTopColor = childBorderColor;
                                    childCard.style.borderRightColor = childBorderColor;
                                    childCard.style.borderBottomColor = childBorderColor;
                                    childCard.style.borderLeftColor = childBorderColor;
                                }
                            }
                        }
                    });
                    clearToggleButton.text = "Clear";
                    clearToggleButton.style.height = 24;
                    clearToggleButton.style.width = 60;
                    clearToggleButton.style.marginLeft = 8;
                    currentToggleContainer.Add(clearToggleButton);
                    
                    toggleSelectionContainer.Add(currentToggleContainer);
                    
                    // Toggle grid
                    var toggleGridLabel = new Label("Available Toggles");
                    toggleGridLabel.style.fontSize = 12;
                    toggleGridLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    toggleGridLabel.style.marginTop = 12;
                    toggleGridLabel.style.marginBottom = 8;
                    toggleGridLabel.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f, 1f));
                    toggleSelectionContainer.Add(toggleGridLabel);
                    
                    var toggleGridContainer = new VisualElement();
                    toggleGridContainer.name = "toggle-grid-container";
                    toggleGridContainer.style.flexDirection = FlexDirection.Row;
                    toggleGridContainer.style.flexWrap = Wrap.Wrap;
                    toggleGridContainer.style.marginTop = 0;
                    toggleGridContainer.style.marginBottom = 8;
                    toggleGridContainer.style.paddingTop = 12;
                    toggleGridContainer.style.paddingBottom = 12;
                    toggleGridContainer.style.paddingLeft = 8;
                    toggleGridContainer.style.paddingRight = 8;
                    toggleGridContainer.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.08f, 1f));
                    toggleGridContainer.style.borderTopLeftRadius = 8;
                    toggleGridContainer.style.borderTopRightRadius = 8;
                    toggleGridContainer.style.borderBottomLeftRadius = 8;
                    toggleGridContainer.style.borderBottomRightRadius = 8;
                    
                    foreach (var toggle in availableToggles)
                    {
                        var toggleCard = CreateToggleCard(toggle, data, selectedToggleProp, so, currentToggleName, currentTogglePath);
                        toggleGridContainer.Add(toggleCard);
                    }
                    
                    toggleSelectionContainer.Add(toggleGridContainer);
                    
                    // Update current selection display
                    UpdateCurrentToggleDisplay(selectedToggleProp.objectReferenceValue as Component, data, currentToggleName, currentTogglePath);
                }
                
                container.Add(toggleSelectionContainer);
                
                // Disable create toggle when using existing
                createToggleProp.boolValue = false;
            }
            
            if (uvDiscardToggle != null || hasVRCFuryToggle)
            {
                var disabledField = YUCPUIToolkitHelper.CreateField(createToggleProp, $"Create Toggle (Disabled - {(uvDiscardToggle != null ? "UV Discard Toggle present" : "VRCFury Toggle present")})");
                disabledField.SetEnabled(false);
                container.Add(disabledField);
            }
            else if (!useExistingToggleProp.boolValue)
            {
                container.Add(YUCPUIToolkitHelper.CreateField(createToggleProp, "Create Toggle"));
                
                if (!createToggleProp.boolValue)
                {
                    YUCPUIToolkitHelper.AddSpacing(container, 3);
                    var vrcFuryButton = YUCPUIToolkitHelper.CreateButton("+ Add VRCFury Toggle (Advanced Features)", () => CreateVRCFuryToggleComponent(), YUCPUIToolkitHelper.ButtonVariant.Secondary);
                    vrcFuryButton.style.height = 30;
                    vrcFuryButton.style.backgroundColor = new StyleColor(new Color(0.6f, 0.8f, 1f));
                    container.Add(vrcFuryButton);
                    container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                        "Use VRCFury Toggle for advanced features like blend shapes, material swaps, or multiple animations. " +
                        "The Auto Body Hider will automatically integrate with it.",
                        YUCPUIToolkitHelper.MessageType.Info));
                }
            }
            
            if (createToggleProp.boolValue && uvDiscardToggle == null && !hasVRCFuryToggle && !useExistingToggleProp.boolValue)
            {
                var toggleCard = YUCPUIToolkitHelper.CreateCard("Toggle Configuration", "Configure toggle menu and parameter settings");
                var toggleContent = YUCPUIToolkitHelper.GetCardContent(toggleCard);
                toggleContent.Add(YUCPUIToolkitHelper.CreateField(so.FindProperty("toggleType"), "Toggle Type"));
                
                var toggleType = so.FindProperty("toggleType");
                var toggleTypeValue = (ToggleType)toggleType.enumValueIndex;
                if (toggleTypeValue == ToggleType.ObjectToggle)
                {
                    toggleContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Object Toggle: Toggles clothing + body hiding", YUCPUIToolkitHelper.MessageType.Info));
                }
                else
                {
                    toggleContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Hidden Toggle: Only toggles body hiding (clothing always visible)", YUCPUIToolkitHelper.MessageType.Info));
                }
                
                toggleContent.Add(YUCPUIToolkitHelper.CreateField(so.FindProperty("toggleMenuPath"), "Menu Path"));
                
                var toggleSyncedProp = so.FindProperty("toggleSynced");
                toggleContent.Add(YUCPUIToolkitHelper.CreateField(toggleSyncedProp, "Synced"));
                
                var paramNameContainer = new VisualElement();
                paramNameContainer.style.paddingLeft = 15;
                paramNameContainer.name = "param-name-container";
                paramNameContainer.Add(YUCPUIToolkitHelper.CreateField(so.FindProperty("toggleParameterName"), "Parameter Name (Optional)"));
                toggleContent.Add(paramNameContainer);
                
                var syncedHelpContainer = new VisualElement();
                syncedHelpContainer.style.paddingLeft = 15;
                syncedHelpContainer.name = "synced-help-container";
                toggleContent.Add(syncedHelpContainer);
                
                var savedDefaultContainer = new VisualElement();
                savedDefaultContainer.style.flexDirection = FlexDirection.Row;
                savedDefaultContainer.style.marginBottom = 5;
                var savedField = YUCPUIToolkitHelper.CreateField(so.FindProperty("toggleSaved"), "Saved");
                savedField.style.flexGrow = 1;
                savedField.style.marginRight = 5;
                savedDefaultContainer.Add(savedField);
                savedDefaultContainer.Add(YUCPUIToolkitHelper.CreateField(so.FindProperty("toggleDefaultOn"), "Default ON"));
                toggleContent.Add(savedDefaultContainer);
                
                var hasMenuPath = !string.IsNullOrEmpty(data.toggleMenuPath);
                var hasCustomParam = !string.IsNullOrEmpty(data.toggleParameterName);
                syncedHelpContainer.Clear();
                if (toggleSyncedProp.boolValue)
                {
                    paramNameContainer.style.display = DisplayStyle.Flex;
                    if (!hasMenuPath && hasCustomParam)
                    {
                        syncedHelpContainer.Add(YUCPUIToolkitHelper.CreateHelpBox($"Parameter Only Mode: Controlled by '{data.toggleParameterName}' (no menu item).", YUCPUIToolkitHelper.MessageType.Info));
                    }
                    else if (hasCustomParam)
                    {
                        syncedHelpContainer.Add(YUCPUIToolkitHelper.CreateHelpBox($"Custom Synced: Uses parameter '{data.toggleParameterName}' (synced across players).", YUCPUIToolkitHelper.MessageType.Info));
                    }
                    else
                    {
                        syncedHelpContainer.Add(YUCPUIToolkitHelper.CreateHelpBox("Auto Synced: VRCFury will generate a unique synced parameter name.", YUCPUIToolkitHelper.MessageType.Info));
                    }
                }
                else
                {
                    paramNameContainer.style.display = DisplayStyle.None;
                    syncedHelpContainer.Add(YUCPUIToolkitHelper.CreateHelpBox("Local Toggle: State is local to your client only (not synced).", YUCPUIToolkitHelper.MessageType.Info));
                }
                
                if (string.IsNullOrEmpty(data.toggleMenuPath) && !data.toggleSynced)
                {
                    toggleContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Menu path is required for local toggles!", YUCPUIToolkitHelper.MessageType.Warning));
                }
                
                // Advanced toggle options
                var showToggleAdvanced = SessionState.GetBool($"AutoBodyHider_ToggleAdvanced_{data.GetInstanceID()}", false);
                var toggleAdvancedFoldout = YUCPUIToolkitHelper.CreateFoldout("Advanced Toggle Options", showToggleAdvanced);
                toggleAdvancedFoldout.RegisterValueChangedCallback(evt => { SessionState.SetBool($"AutoBodyHider_ToggleAdvanced_{data.GetInstanceID()}", evt.newValue); });
                toggleAdvancedFoldout.Add(YUCPUIToolkitHelper.CreateField(so.FindProperty("toggleSlider"), "Use Slider"));
                toggleAdvancedFoldout.Add(YUCPUIToolkitHelper.CreateField(so.FindProperty("toggleHoldButton"), "Hold Button"));
                toggleAdvancedFoldout.Add(YUCPUIToolkitHelper.CreateField(so.FindProperty("toggleExclusiveOffState"), "Exclusive Off State"));
                
                YUCPUIToolkitHelper.AddSpacing(toggleAdvancedFoldout, 3);
                var enableExclusiveTagProp = so.FindProperty("toggleEnableExclusiveTag");
                toggleAdvancedFoldout.Add(YUCPUIToolkitHelper.CreateField(enableExclusiveTagProp, "Exclusive Tags"));
                
                var exclusiveTagContainer = new VisualElement();
                exclusiveTagContainer.style.paddingLeft = 15;
                exclusiveTagContainer.name = "exclusive-tag-container";
                exclusiveTagContainer.Add(YUCPUIToolkitHelper.CreateField(so.FindProperty("toggleExclusiveTag"), "Tag Names"));
                toggleAdvancedFoldout.Add(exclusiveTagContainer);
                
                YUCPUIToolkitHelper.AddSpacing(toggleAdvancedFoldout, 3);
                var enableIconProp = so.FindProperty("toggleEnableIcon");
                toggleAdvancedFoldout.Add(YUCPUIToolkitHelper.CreateField(enableIconProp, "Custom Icon"));
                
                var iconContainer = new VisualElement();
                iconContainer.style.paddingLeft = 15;
                iconContainer.name = "icon-container";
                iconContainer.Add(YUCPUIToolkitHelper.CreateField(so.FindProperty("toggleIcon"), "Icon Texture"));
                toggleAdvancedFoldout.Add(iconContainer);
                
                YUCPUIToolkitHelper.AddSpacing(toggleAdvancedFoldout, 3);
                toggleAdvancedFoldout.Add(YUCPUIToolkitHelper.CreateField(so.FindProperty("debugSaveAnimation"), "Debug: Save Animation"));
                
                exclusiveTagContainer.style.display = enableExclusiveTagProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
                iconContainer.style.display = enableIconProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
                
                toggleContent.Add(toggleAdvancedFoldout);
                
                if (appModeValue == ApplicationMode.MeshDeletion)
                {
                    toggleContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Toggle only works with UDIM Discard mode, not Mesh Deletion!", YUCPUIToolkitHelper.MessageType.Warning));
                }
                
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
                        toggleContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Body mesh needs a Poiyomi or FastFur shader with UDIM support for toggles to work!", YUCPUIToolkitHelper.MessageType.Warning));
                    }
                }
                
                container.Add(toggleCard);
            }
            else if (uvDiscardToggle != null)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Toggle disabled: UV Discard Toggle component detected on this object. " +
                    "The UV Discard Toggle will handle the toggle functionality.",
                    YUCPUIToolkitHelper.MessageType.Info));
            }
        }
        
        private void UpdatePreviewInfo(VisualElement container, AutoBodyHiderData data)
        {
            container.Clear();
            
            if (data.previewGenerated && data.previewHiddenFaces != null)
            {
                int totalFaces = data.previewTriangles.Length / 3;
                int hiddenFaces = 0;
                foreach (bool hidden in data.previewHiddenFaces)
                {
                    if (hidden) hiddenFaces++;
                }
                
                var statsContainer = new VisualElement();
                statsContainer.style.paddingTop = 6;
                statsContainer.style.paddingBottom = 6;
                statsContainer.style.paddingLeft = 10;
                statsContainer.style.paddingRight = 10;
                statsContainer.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 1f));
                statsContainer.style.borderTopLeftRadius = 6;
                statsContainer.style.borderTopRightRadius = 6;
                statsContainer.style.borderBottomLeftRadius = 6;
                statsContainer.style.borderBottomRightRadius = 6;
                statsContainer.style.marginBottom = 8;
                
                var statusLabel = new Label("Preview Ready");
                statusLabel.style.fontSize = 12;
                statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                statusLabel.style.color = new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f));
                statusLabel.style.marginBottom = 6;
                statsContainer.Add(statusLabel);
                
                var statsText = new Label($"Hidden: {hiddenFaces:N0} / {totalFaces:N0} faces ({(hiddenFaces * 100f / totalFaces):F1}%)\n" +
                                         $"Performance: -{hiddenFaces:N0} triangles");
                statsText.style.fontSize = 11;
                statsText.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f, 1f));
                statsText.style.whiteSpace = WhiteSpace.Normal;
                statsContainer.Add(statsText);
                
                container.Add(statsContainer);
            }
            else
            {
                var emptyState = new VisualElement();
                emptyState.style.paddingTop = 8;
                emptyState.style.paddingBottom = 8;
                emptyState.style.paddingLeft = 10;
                emptyState.style.paddingRight = 10;
                emptyState.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f, 1f));
                emptyState.style.borderTopLeftRadius = 6;
                emptyState.style.borderTopRightRadius = 6;
                emptyState.style.borderBottomLeftRadius = 6;
                emptyState.style.borderBottomRightRadius = 6;
                emptyState.style.marginBottom = 8;
                
                var emptyLabel = new Label("No preview generated yet");
                emptyLabel.style.fontSize = 11;
                emptyLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f, 1f));
                emptyState.Add(emptyLabel);
                
                container.Add(emptyState);
            }
        }

        public override void OnInspectorGUI()
        {
            // Legacy support - not used anymore
        }

        
        private void CreateVRCFuryToggleComponent()
        {
            try
            {
                var toggle = com.vrcfury.api.FuryComponents.CreateToggle(data.gameObject);
                
                if (toggle == null)
                {
                    EditorUtility.DisplayDialog(
                        "Error",
                        "Failed to create VRCFury Toggle component. Please ensure VRCFury is installed and up to date.",
                        "OK"
                    );
                    return;
                }
                
                toggle.SetMenuPath("Clothing/Hide Body");
                toggle.SetSaved();
                
                // The user can add blend shapes, animations, etc. via the VRCFury inspector
                // The Auto Body Hider processor will automatically add the UDIM discard animation during build
                
                EditorUtility.SetDirty(data.gameObject);
                serializedObject.Update();
                
                // Force UI update
                previousVrcFuryToggle = null; // Force toggle section to update
                Repaint();
                
                Debug.Log($"[AutoBodyHider] Created VRCFury Toggle component on '{data.gameObject.name}'", data);
                
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
            catch (System.Exception ex)
            {
                Debug.LogError($"[AutoBodyHider] Error creating VRCFury Toggle: {ex.Message}", data);
                EditorUtility.DisplayDialog(
                    "Error",
                    $"Failed to create VRCFury Toggle component:\n\n{ex.Message}\n\nPlease ensure VRCFury is installed and up to date.",
                    "OK"
                );
            }
        }

        private bool ValidateData()
        {
            if (data.targetBodyMesh == null) return false;
            if (data.targetBodyMesh.sharedMesh == null) return false;
            if (data.detectionMethod != DetectionMethod.Manual)
            {
                if (data.clothingMeshes == null || data.clothingMeshes.Length == 0)
                {
                    return false;
                }
                
                // Check for at least one valid clothing mesh
                bool hasValidMesh = false;
                foreach (var mesh in data.clothingMeshes)
                {
                    if (mesh != null && mesh.sharedMesh != null)
                    {
                        hasValidMesh = true;
                        break;
                    }
                }
                if (!hasValidMesh) return false;
            }
            if (data.detectionMethod == DetectionMethod.Manual && data.manualMask == null) return false;
            return true;
        }

        private string GetValidationError()
        {
            if (data.targetBodyMesh == null) return "Target Body Mesh is not set.";
            if (data.targetBodyMesh.sharedMesh == null) return "Target Body Mesh has no mesh data.";
            if (data.detectionMethod != DetectionMethod.Manual)
            {
                if (data.clothingMeshes == null || data.clothingMeshes.Length == 0)
                {
                    return "At least one Clothing Mesh is required for automatic detection.";
                }
                
                // Check for at least one valid clothing mesh
                bool hasValidMesh = false;
                foreach (var mesh in data.clothingMeshes)
                {
                    if (mesh != null && mesh.sharedMesh != null)
                    {
                        hasValidMesh = true;
                        break;
                    }
                }
                if (!hasValidMesh)
                    return "At least one valid Clothing Mesh is required for automatic detection.";
            }
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
        
        private List<Component> GetAvailableVRCFuryToggles(GameObject root)
        {
            var toggles = new List<Component>();
            
            // Check root GameObject
            var rootComponents = root.GetComponents<Component>();
            foreach (var comp in rootComponents)
            {
                if (comp != null && comp.GetType().Name == "VRCFury")
                {
                    toggles.Add(comp);
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
                        toggles.Add(comp);
                    }
                }
            }
            
            return toggles;
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
        
        private void UpdateMaterialPicker(
            AutoBodyHiderData data,
            SerializedObject so,
            VisualElement currentSelectionContainer,
            Image currentMaterialPreview,
            Label currentMaterialName,
            Label currentMaterialShader,
            VisualElement materialGridContainer,
            VisualElement materialHelpContainer,
            VisualElement selectedMaterialsList)
        {
            var targetMaterialsProp = so.FindProperty("targetMaterials");
            Material[] selectedMaterials = new Material[targetMaterialsProp.arraySize];
            for (int i = 0; i < targetMaterialsProp.arraySize; i++)
            {
                selectedMaterials[i] = targetMaterialsProp.GetArrayElementAtIndex(i).objectReferenceValue as Material;
            }
            
            // Check if grid rebuild is needed when body mesh or its materials changed
            Material[] currentBodyMeshMaterials = data.targetBodyMesh != null ? data.targetBodyMesh.sharedMaterials : null;
            bool bodyMeshChanged = data.targetBodyMesh != previousTargetBodyMesh;
            bool materialsOnMeshChanged = false;
            
            // Check if materials on the mesh actually changed
            if (bodyMeshChanged || previousBodyMeshMaterials == null)
            {
                materialsOnMeshChanged = true;
            }
            else if (currentBodyMeshMaterials != null && previousBodyMeshMaterials != null)
            {
                if (currentBodyMeshMaterials.Length != previousBodyMeshMaterials.Length)
                {
                    materialsOnMeshChanged = true;
                }
                else
                {
                    for (int i = 0; i < currentBodyMeshMaterials.Length; i++)
                    {
                        if (currentBodyMeshMaterials[i] != previousBodyMeshMaterials[i])
                        {
                            materialsOnMeshChanged = true;
                            break;
                        }
                    }
                }
            }
            
            bool needsGridRebuild = materialsOnMeshChanged || (materialGridContainer.childCount == 0);
            
            // Update tracking
            if (materialsOnMeshChanged)
            {
                previousBodyMeshMaterials = currentBodyMeshMaterials != null ? (Material[])currentBodyMeshMaterials.Clone() : null;
            }
            
            // Check if materials actually changed (for state tracking)
            bool materialsChanged = previousTargetMaterials == null || 
                                   previousTargetMaterials.Length != selectedMaterials.Length ||
                                   !selectedMaterials.SequenceEqual(previousTargetMaterials ?? new Material[0]);
            
            // Update current selection display to reflect current state
            if (selectedMaterialsList != null)
            {
                selectedMaterialsList.Clear();
            }
            
            // Filter out null materials
            var validSelectedMaterials = selectedMaterials.Where(m => m != null).ToArray();
            
            if (validSelectedMaterials.Length > 0)
            {
                int compatibleCount = validSelectedMaterials.Count(m => UDIMManipulator.IsPoiyomiWithUDIMSupport(m));
                currentMaterialName.text = $"{validSelectedMaterials.Length} Material(s) Selected";
                currentMaterialShader.text = $"{compatibleCount} compatible with UDIM Discard";
                
                // Show first material preview
                Material firstMaterial = validSelectedMaterials[0];
                if (firstMaterial != null)
                {
                    Texture2D preview = AssetPreview.GetAssetPreview(firstMaterial);
                    if (preview == null)
                    {
                        preview = AssetPreview.GetMiniThumbnail(firstMaterial);
                        AssetPreview.SetPreviewTextureCacheSize(256);
                    }
                    currentMaterialPreview.image = preview;
                }
                else
                {
                    currentMaterialPreview.image = null;
                }
                
                // Show list of selected materials
                if (selectedMaterialsList != null)
                {
                    foreach (var mat in validSelectedMaterials)
                    {
                        var materialItem = new VisualElement();
                        materialItem.style.flexDirection = FlexDirection.Row;
                        materialItem.style.marginTop = 2;
                        materialItem.style.marginBottom = 2;
                        
                        var matLabel = new Label($"• {mat.name}");
                        matLabel.style.fontSize = 11;
                        bool isCompatible = UDIMManipulator.IsPoiyomiWithUDIMSupport(mat);
                        matLabel.style.color = isCompatible 
                            ? new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f))
                            : new StyleColor(new Color(0.8f, 0.5f, 0.3f, 1f));
                        materialItem.Add(matLabel);
                        
                        selectedMaterialsList.Add(materialItem);
                    }
                }
            }
            else
            {
                currentMaterialName.text = "None (Auto-detect)";
                currentMaterialShader.text = "Will auto-detect all compatible materials";
                currentMaterialPreview.image = null;
            }
            
            // Update state tracking (after display update)
            if (materialsChanged)
            {
                previousTargetMaterials = validSelectedMaterials;
            }
            previousTargetBodyMesh = data.targetBodyMesh;
            
            // Update material grid when needed
            if (needsGridRebuild)
            {
                materialGridContainer.Clear();
                
                if (data.targetBodyMesh != null && data.targetBodyMesh.sharedMaterials != null && data.targetBodyMesh.sharedMaterials.Length > 0)
            {
                var availableMaterials = new System.Collections.Generic.List<Material>();
                foreach (var mat in data.targetBodyMesh.sharedMaterials)
                {
                    if (mat != null)
                    {
                        availableMaterials.Add(mat);
                    }
                }
                
                if (availableMaterials.Count > 0)
                {
                    foreach (var mat in availableMaterials)
                    {
                        var materialCard = CreateMaterialCard(mat, selectedMaterials, targetMaterialsProp, so);
                        materialGridContainer.Add(materialCard);
                    }
                    
                    materialHelpContainer.Clear();
                    materialHelpContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                        $"Select one or more materials from the grid above, or leave empty to auto-detect all compatible materials.",
                        YUCPUIToolkitHelper.MessageType.Info));
                }
                else
                {
                    materialHelpContainer.Clear();
                    materialHelpContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                        "No materials found on the body mesh.",
                        YUCPUIToolkitHelper.MessageType.Warning));
                }
            }
            else
            {
                    materialHelpContainer.Clear();
                    materialHelpContainer.Add(YUCPUIToolkitHelper.CreateHelpBox(
                        "Assign a body mesh to see available materials.",
                        YUCPUIToolkitHelper.MessageType.Info));
                }
            }
            
            // Update selection border on cards when selection changes
            if (!needsGridRebuild && materialGridContainer.childCount > 0)
            {
                foreach (var child in materialGridContainer.Children())
                {
                    if (child is VisualElement card && card.userData is Material cardMaterial)
                    {
                        // Update border color
                        bool isSelected = selectedMaterials != null && selectedMaterials.Contains(cardMaterial);
                        var borderColor = isSelected
                            ? new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f))
                            : new StyleColor(new Color(0.2f, 0.2f, 0.2f, 1f));
                        card.style.borderTopColor = borderColor;
                        card.style.borderRightColor = borderColor;
                        card.style.borderBottomColor = borderColor;
                        card.style.borderLeftColor = borderColor;
                        
                        // Also update preview if it wasn't ready before
                        var previewImage = card.Q<Image>();
                        if (previewImage != null && (previewImage.image == null || previewImage.image == EditorGUIUtility.FindTexture("Material Icon")))
                        {
                            Texture2D newPreview = AssetPreview.GetAssetPreview(cardMaterial);
                            if (newPreview != null)
                            {
                                previewImage.image = newPreview;
                            }
                        }
                    }
                }
            }
        }
        
        private VisualElement CreateMaterialCard(Material material, Material[] selectedMaterials, SerializedProperty targetMaterialsProp, SerializedObject so)
        {
            var card = new VisualElement();
            card.name = $"material-card-{material.GetInstanceID()}";
            card.style.width = 100;
            card.style.height = 120;
            card.style.marginRight = 8;
            card.style.marginBottom = 8;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.paddingLeft = 8;
            card.style.paddingRight = 8;
            card.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f, 1f));
            card.style.borderTopLeftRadius = 6;
            card.style.borderTopRightRadius = 6;
            card.style.borderBottomLeftRadius = 6;
            card.style.borderBottomRightRadius = 6;
            card.style.borderTopWidth = 2;
            card.style.borderRightWidth = 2;
            card.style.borderBottomWidth = 2;
            card.style.borderLeftWidth = 2;
            bool isSelected = selectedMaterials != null && selectedMaterials.Contains(material);
            var borderColor = isSelected
                ? new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f)) 
                : new StyleColor(new Color(0.2f, 0.2f, 0.2f, 1f));
            card.style.borderTopColor = borderColor;
            card.style.borderRightColor = borderColor;
            card.style.borderBottomColor = borderColor;
            card.style.borderLeftColor = borderColor;
            
            bool isCompatible = UDIMManipulator.IsPoiyomiWithUDIMSupport(material);
            string shaderName = material.shader != null ? material.shader.name : "No Shader";
            
            // Preview image
            var preview = new Image();
            preview.style.width = 84;
            preview.style.height = 60;
            preview.style.marginBottom = 6;
            preview.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 1f));
            
            Texture2D previewTexture = AssetPreview.GetAssetPreview(material);
            if (previewTexture == null)
            {
                previewTexture = AssetPreview.GetMiniThumbnail(material);
                // Request preview generation for future frames
                AssetPreview.SetPreviewTextureCacheSize(256);
            }
            if (previewTexture != null)
            {
                preview.image = previewTexture;
            }
            
            // Store material reference in userData for easy access
            card.userData = material;
            
            card.Add(preview);
            
            // Material name
            var nameLabel = new Label(material.name);
            nameLabel.style.fontSize = 11;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            nameLabel.style.whiteSpace = WhiteSpace.Normal;
            nameLabel.style.maxHeight = 30;
            nameLabel.style.overflow = Overflow.Hidden;
            card.Add(nameLabel);
            
            // Function to toggle material in array (following AvatarUploader pattern)
            System.Action toggleMaterial = () =>
            {
                // Update serialized object to get current state
                so.Update();
                
                var materialsList = new List<Material>();
                for (int i = 0; i < targetMaterialsProp.arraySize; i++)
                {
                    var mat = targetMaterialsProp.GetArrayElementAtIndex(i).objectReferenceValue as Material;
                    if (mat != null)
                    {
                        materialsList.Add(mat);
                    }
                }
                
                // Toggle: if selected, remove it; if not selected, add it
                if (materialsList.Contains(material))
                {
                    materialsList.Remove(material);
                }
                else
                {
                    materialsList.Add(material);
                }
                
                // Update array
                targetMaterialsProp.arraySize = materialsList.Count;
                for (int i = 0; i < materialsList.Count; i++)
                {
                    targetMaterialsProp.GetArrayElementAtIndex(i).objectReferenceValue = materialsList[i];
                }
                
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(so.targetObject);
            };
            
            // Add smooth transitions for animations
            card.style.transitionDuration = new List<TimeValue> { new TimeValue(150, TimeUnit.Millisecond) };
            card.style.transitionProperty = new List<StylePropertyName> 
            { 
                new StylePropertyName("background-color"),
                new StylePropertyName("border-color"),
                new StylePropertyName("scale")
            };
            
            // Compatibility badge
            if (isCompatible)
            {
                var badge = new Label("UDIM");
                badge.style.fontSize = 9;
                badge.style.unityTextAlign = TextAnchor.MiddleCenter;
                badge.style.marginTop = 2;
                badge.style.backgroundColor = new StyleColor(new Color(0.212f, 0.749f, 0.694f, 0.3f));
                badge.style.color = new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f));
                badge.style.paddingTop = 2;
                badge.style.paddingBottom = 2;
                badge.style.paddingLeft = 4;
                badge.style.paddingRight = 4;
                badge.style.borderTopLeftRadius = 3;
                badge.style.borderTopRightRadius = 3;
                badge.style.borderBottomLeftRadius = 3;
                badge.style.borderBottomRightRadius = 3;
                card.Add(badge);
            }
            
            // Click handler - toggle selection when clicking anywhere on card
            // Following AvatarUploader pattern: use MouseDownEvent with stop propagation
            card.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    evt.StopPropagation();
                    toggleMaterial();
                }
            }, TrickleDown.NoTrickleDown);
            
            // Hover effect with smooth animation
            card.RegisterCallback<MouseEnterEvent>(evt =>
            {
                card.style.backgroundColor = new StyleColor(new Color(0.22f, 0.22f, 0.22f, 1f));
                card.style.scale = new Scale(new Vector2(1.02f, 1.02f));
            });
            
            card.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                card.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f, 1f));
                card.style.scale = new Scale(new Vector2(1f, 1f));
            });
            
            // Update border color with animation
            if (isSelected)
            {
                card.style.borderTopColor = new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f));
                card.style.borderRightColor = new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f));
                card.style.borderBottomColor = new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f));
                card.style.borderLeftColor = new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f));
            }
            
            return card;
        }

        private VisualElement CreateToggleCard(Component toggle, AutoBodyHiderData data, SerializedProperty selectedToggleProp, SerializedObject so, Label currentToggleName, Label currentTogglePath)
        {
            var card = new VisualElement();
            card.name = $"toggle-card-{toggle.GetInstanceID()}";
            card.style.width = 220;
            card.style.height = 150;
            card.style.marginRight = 12;
            card.style.marginBottom = 12;
            card.style.paddingTop = 14;
            card.style.paddingBottom = 14;
            card.style.paddingLeft = 14;
            card.style.paddingRight = 14;
            card.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f, 1f));
            card.style.borderTopLeftRadius = 8;
            card.style.borderTopRightRadius = 8;
            card.style.borderBottomLeftRadius = 8;
            card.style.borderBottomRightRadius = 8;
            card.style.borderTopWidth = 2;
            card.style.borderRightWidth = 2;
            card.style.borderBottomWidth = 2;
            card.style.borderLeftWidth = 2;
            
            bool isSelected = selectedToggleProp.objectReferenceValue == toggle;
            var borderColor = isSelected
                ? new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f)) 
                : new StyleColor(new Color(0.25f, 0.25f, 0.25f, 1f));
            card.style.borderTopColor = borderColor;
            card.style.borderRightColor = borderColor;
            card.style.borderBottomColor = borderColor;
            card.style.borderLeftColor = borderColor;
            
            // Store toggle reference
            card.userData = toggle;
            
            // Get VRCFury toggle information
            var toggleInfo = GetVRCFuryToggleInfo(toggle);
            
            // Menu path (primary identifier for differentiation)
            string displayName = !string.IsNullOrEmpty(toggleInfo.menuPath) ? toggleInfo.menuPath : toggle.name;
            var nameLabel = new Label(displayName);
            nameLabel.style.fontSize = 13;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            nameLabel.style.whiteSpace = WhiteSpace.Normal;
            nameLabel.style.maxHeight = 40;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.marginTop = 0;
            nameLabel.style.marginBottom = 6;
            nameLabel.style.color = new StyleColor(new Color(0.95f, 0.95f, 0.95f, 1f));
            card.Add(nameLabel);
            
            // Info container for badges and details
            var infoContainer = new VisualElement();
            infoContainer.style.flexDirection = FlexDirection.Column;
            infoContainer.style.alignItems = Align.Center;
            infoContainer.style.marginTop = 4;
            
            // Global parameter badge (if exists)
            if (!string.IsNullOrEmpty(toggleInfo.globalParam))
            {
                var paramBadge = new Label($"Param: {toggleInfo.globalParam}");
                paramBadge.style.fontSize = 9;
                paramBadge.style.unityTextAlign = TextAnchor.MiddleCenter;
                paramBadge.style.marginBottom = 4;
                paramBadge.style.paddingTop = 3;
                paramBadge.style.paddingBottom = 3;
                paramBadge.style.paddingLeft = 8;
                paramBadge.style.paddingRight = 8;
                paramBadge.style.backgroundColor = new StyleColor(new Color(0.3f, 0.5f, 0.7f, 0.35f));
                paramBadge.style.color = new StyleColor(new Color(0.7f, 0.85f, 1f, 1f));
                paramBadge.style.borderTopLeftRadius = 4;
                paramBadge.style.borderTopRightRadius = 4;
                paramBadge.style.borderBottomLeftRadius = 4;
                paramBadge.style.borderBottomRightRadius = 4;
                infoContainer.Add(paramBadge);
            }
            
            // Action summary (what the toggle does)
            if (!string.IsNullOrEmpty(toggleInfo.actionSummary))
            {
                var actionLabel = new Label(toggleInfo.actionSummary);
                actionLabel.style.fontSize = 10;
                actionLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                actionLabel.style.color = new StyleColor(new Color(0.65f, 0.65f, 0.65f, 1f));
                actionLabel.style.marginBottom = 4;
                actionLabel.style.whiteSpace = WhiteSpace.Normal;
                actionLabel.style.maxHeight = 24;
                actionLabel.style.overflow = Overflow.Hidden;
                infoContainer.Add(actionLabel);
            }
            
            // GameObject path
            string path = GetGameObjectPath(toggle.gameObject, data.gameObject);
            var pathLabel = new Label(path);
            pathLabel.style.fontSize = 9;
            pathLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            pathLabel.style.color = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 1f));
            pathLabel.style.marginBottom = 6;
            pathLabel.style.whiteSpace = WhiteSpace.Normal;
            pathLabel.style.maxHeight = 18;
            pathLabel.style.overflow = Overflow.Hidden;
            infoContainer.Add(pathLabel);
            
            // Location badge (Root/Child) and action count
            bool isOnRoot = toggle.gameObject == data.gameObject;
            string locationText = isOnRoot ? "Root" : "Child";
            if (toggleInfo.actionCount > 0)
            {
                locationText += $" • {toggleInfo.actionCount} action{(toggleInfo.actionCount == 1 ? "" : "s")}";
            }
            var locationBadge = new Label(locationText);
            locationBadge.style.fontSize = 9;
            locationBadge.style.unityTextAlign = TextAnchor.MiddleCenter;
            locationBadge.style.paddingTop = 4;
            locationBadge.style.paddingBottom = 4;
            locationBadge.style.paddingLeft = 8;
            locationBadge.style.paddingRight = 8;
            locationBadge.style.backgroundColor = new StyleColor(isOnRoot ? new Color(0.212f, 0.749f, 0.694f, 0.25f) : new Color(0.5f, 0.5f, 0.5f, 0.25f));
            locationBadge.style.color = new StyleColor(isOnRoot ? new Color(0.212f, 0.749f, 0.694f, 1f) : new Color(0.75f, 0.75f, 0.75f, 1f));
            locationBadge.style.borderTopLeftRadius = 4;
            locationBadge.style.borderTopRightRadius = 4;
            locationBadge.style.borderBottomLeftRadius = 4;
            locationBadge.style.borderBottomRightRadius = 4;
            infoContainer.Add(locationBadge);
            
            card.Add(infoContainer);
            
            // Add smooth transitions for animations
            card.style.transitionDuration = new List<TimeValue> { new TimeValue(150, TimeUnit.Millisecond) };
            card.style.transitionProperty = new List<StylePropertyName> 
            { 
                new StylePropertyName("background-color"),
                new StylePropertyName("border-color"),
                new StylePropertyName("scale")
            };
            
            // Click handler - select toggle
            card.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    evt.StopPropagation();
                    
                    // Update selection
                    selectedToggleProp.objectReferenceValue = toggle;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(so.targetObject);
                    
                    // Update current selection display
                    UpdateCurrentToggleDisplay(toggle, data, currentToggleName, currentTogglePath);
                    
                    // Update all cards' border colors
                    var gridContainer = card.parent;
                    if (gridContainer != null)
                    {
                        foreach (var child in gridContainer.Children())
                        {
                            if (child is VisualElement childCard && childCard.userData is Component childToggle)
                            {
                                bool childIsSelected = childToggle == toggle;
                                var childBorderColor = childIsSelected
                                    ? new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f))
                                    : new StyleColor(new Color(0.2f, 0.2f, 0.2f, 1f));
                                childCard.style.borderTopColor = childBorderColor;
                                childCard.style.borderRightColor = childBorderColor;
                                childCard.style.borderBottomColor = childBorderColor;
                                childCard.style.borderLeftColor = childBorderColor;
                            }
                        }
                    }
                }
            }, TrickleDown.NoTrickleDown);
            
            // Hover effect with smooth animation
            card.RegisterCallback<MouseEnterEvent>(evt =>
            {
                card.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.18f, 1f));
                card.style.scale = new Scale(new Vector2(1.03f, 1.03f));
                if (!isSelected)
                {
                    card.style.borderTopColor = new StyleColor(new Color(0.35f, 0.35f, 0.35f, 1f));
                    card.style.borderRightColor = new StyleColor(new Color(0.35f, 0.35f, 0.35f, 1f));
                    card.style.borderBottomColor = new StyleColor(new Color(0.35f, 0.35f, 0.35f, 1f));
                    card.style.borderLeftColor = new StyleColor(new Color(0.35f, 0.35f, 0.35f, 1f));
                }
            });
            
            card.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                card.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f, 1f));
                card.style.scale = new Scale(new Vector2(1f, 1f));
                if (!isSelected)
                {
                    card.style.borderTopColor = borderColor;
                    card.style.borderRightColor = borderColor;
                    card.style.borderBottomColor = borderColor;
                    card.style.borderLeftColor = borderColor;
                }
            });
            
            return card;
        }
        
        private void UpdateCurrentToggleDisplay(Component selectedToggle, AutoBodyHiderData data, Label nameLabel, Label pathLabel)
        {
            if (selectedToggle != null)
            {
                var toggleInfo = GetVRCFuryToggleInfo(selectedToggle);
                nameLabel.text = !string.IsNullOrEmpty(toggleInfo.menuPath) ? toggleInfo.menuPath : selectedToggle.name;
                string path = GetGameObjectPath(selectedToggle.gameObject, data.gameObject);
                pathLabel.text = path;
            }
            else
            {
                nameLabel.text = "None Selected";
                pathLabel.text = "Select a toggle from the grid below";
            }
        }
        
        private (string menuPath, string globalParam, int actionCount, string actionSummary) GetVRCFuryToggleInfo(Component vrcFuryComponent)
        {
            string menuPath = "";
            string globalParam = "";
            int actionCount = 0;
            string actionSummary = "";
            
            try
            {
                // Get the content field via reflection
                var contentField = vrcFuryComponent.GetType().GetField("content", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (contentField != null)
                {
                    var content = contentField.GetValue(vrcFuryComponent);
                    if (content != null && content.GetType().Name == "Toggle")
                    {
                        // Get menu path (name property)
                        var nameField = content.GetType().GetField("name", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (nameField != null)
                        {
                            menuPath = nameField.GetValue(content) as string ?? "";
                        }
                        
                        // Get global parameter
                        var useGlobalParamField = content.GetType().GetField("useGlobalParam", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        var globalParamField = content.GetType().GetField("globalParam", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (useGlobalParamField != null && globalParamField != null)
                        {
                            bool useGlobal = (bool)(useGlobalParamField.GetValue(content) ?? false);
                            if (useGlobal)
                            {
                                globalParam = globalParamField.GetValue(content) as string ?? "";
                            }
                        }
                        
                        // Get action count and summary
                        var stateField = content.GetType().GetField("state", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (stateField != null)
                        {
                            var state = stateField.GetValue(content);
                            var actionsField = state.GetType().GetField("actions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            if (actionsField != null)
                            {
                                var actionsList = actionsField.GetValue(state) as System.Collections.IList;
                                if (actionsList != null)
                                {
                                    actionCount = actionsList.Count;
                                    
                                    // Create summary of action types
                                    var actionTypes = new System.Collections.Generic.HashSet<string>();
                                    foreach (var action in actionsList)
                                    {
                                        if (action != null)
                                        {
                                            string actionTypeName = action.GetType().Name;
                                            // Remove "Action" suffix from action type name
                                            if (actionTypeName.EndsWith("Action"))
                                            {
                                                actionTypeName = actionTypeName.Substring(0, actionTypeName.Length - 6);
                                            }
                                            actionTypes.Add(actionTypeName);
                                        }
                                    }
                                    
                                    if (actionTypes.Count > 0)
                                    {
                                        actionSummary = string.Join(", ", actionTypes);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // Silently fail - return empty info
            }
            
            return (menuPath, globalParam, actionCount, actionSummary);
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
