using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components;
using YUCP.Components.Editor;
using YUCP.Components.Editor.MeshUtils;
using YUCP.UI.DesignSystem.Utilities;
using System;
using System.Reflection;

namespace YUCP.Components.Editor.UI
{
    [CustomEditor(typeof(AutoUVDiscardData))]
    public class AutoUVDiscardDataEditor : UnityEditor.Editor
    {
        private AutoUVDiscardData data;
        private bool isGeneratingPreview = false;
        
        // State tracking
        private Material[] previousTargetMaterials = null;
        private SkinnedMeshRenderer previousTargetBodyMesh = null;
        private Mesh previousDetectedMesh = null;
        private int cachedDetectedUVChannel = 1;
        private Material[] previousBodyMeshMaterials = null;
        private bool previousAutoDetectUVChannel = true;
        private bool previousAutoAssignUVTile = true;
        private int previousStartRow = -1;
        private int previousStartColumn = -1;

        private void OnEnable()
        {
            data = (AutoUVDiscardData)target;
            previousAutoDetectUVChannel = data.autoDetectUVChannel;
            previousAutoAssignUVTile = data.autoAssignUVTile;
            previousStartRow = data.startRow;
            previousStartColumn = data.startColumn;
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Auto UV Discard"));
            
            var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(AutoUVDiscardData));
            if (betaWarning != null) root.Add(betaWarning);
            
            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(AutoUVDiscardData));
            if (supportBanner != null) root.Add(supportBanner);
            
            // Target Mesh Card
            var targetCard = YUCPUIToolkitHelper.CreateCard("Target Body", "Configure body mesh");
            var targetContent = YUCPUIToolkitHelper.GetCardContent(targetCard);
            targetContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("targetBodyMesh"), "Body Mesh"));
            root.Add(targetCard);
            
            // Material Selection Card
            var materialCard = YUCPUIToolkitHelper.CreateCard("Material Selection", "Select materials to configure");
            var materialContent = YUCPUIToolkitHelper.GetCardContent(materialCard);
            
            var materialPickerContainer = new VisualElement();
            materialPickerContainer.name = "material-picker-container";
            
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
            
            var materialGridContainer = new VisualElement();
            materialGridContainer.name = "material-grid-container";
            materialGridContainer.style.flexDirection = FlexDirection.Row;
            materialGridContainer.style.flexWrap = Wrap.Wrap;
            materialGridContainer.style.marginTop = 5;
            
            materialPickerContainer.Add(materialGridContainer);
            
            var materialHelpContainer = new VisualElement();
            materialHelpContainer.name = "material-help-container";
            materialPickerContainer.Add(materialHelpContainer);
            
            var materialPickerLabel = new Label("Target Materials (Optional)");
            materialPickerLabel.style.fontSize = 13;
            materialPickerLabel.style.marginBottom = 5;
            materialPickerContainer.Insert(0, materialPickerLabel);
            
            materialContent.Add(materialPickerContainer);
            root.Add(materialCard);
            
            // Detection Mode Card
            var detectionModeCard = YUCPUIToolkitHelper.CreateCard("Detection Mode", "Select how to detect UV regions");
            var detectionModeContent = YUCPUIToolkitHelper.GetCardContent(detectionModeCard);
            
            var detectionModeField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("detectionMode"), "Detection Mode");
            detectionModeContent.Add(detectionModeField);
            
            // Mode description label
            var modeDescriptionLabel = new Label();
            modeDescriptionLabel.name = "mode-description-label";
            modeDescriptionLabel.style.fontSize = 11;
            modeDescriptionLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f, 1f));
            modeDescriptionLabel.style.marginTop = 5;
            modeDescriptionLabel.style.whiteSpace = WhiteSpace.Normal;
            detectionModeContent.Add(modeDescriptionLabel);
            
            root.Add(detectionModeCard);
            
            // UV Proximity Settings Card
            var uvProximityCard = YUCPUIToolkitHelper.CreateCard("UV Proximity Settings", "Configure UV clustering");
            uvProximityCard.name = "uv-proximity-card";
            var uvProximityContent = YUCPUIToolkitHelper.GetCardContent(uvProximityCard);
            
            var autoDetectUVField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("autoDetectUVChannel"), "Auto-Detect UV Channel");
            uvProximityContent.Add(autoDetectUVField);
            
            var detectedUVContainer = new VisualElement();
            detectedUVContainer.name = "detected-uv-container";
            uvProximityContent.Add(detectedUVContainer);
            
            var advancedUVFoldout = YUCPUIToolkitHelper.CreateFoldout("Advanced UV Settings", false);
            advancedUVFoldout.name = "advanced-uv-foldout";
            var manualUVSection = new VisualElement();
            manualUVSection.name = "manual-uv-section";
            var uvChannelField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("uvChannel"), "UV Channel");
            manualUVSection.Add(uvChannelField);
            advancedUVFoldout.Add(manualUVSection);
            uvProximityContent.Add(advancedUVFoldout);
            
            uvProximityContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("mergeTolerance"), "Merge Tolerance"));
            uvProximityContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("minRegionSize"), "Min Region Size %"));
            root.Add(uvProximityCard);
            
            // Mask Texture Settings Card
            var maskCard = YUCPUIToolkitHelper.CreateCard("Mask Regions", "Each mask defines one region");
            maskCard.name = "mask-texture-card";
            var maskContent = YUCPUIToolkitHelper.GetCardContent(maskCard);
            
            maskContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Add masks to define regions. White areas in each mask = one region.", YUCPUIToolkitHelper.MessageType.Info));
            maskContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("maskRegions"), "Mask Regions"));
            maskContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("maskUVChannel"), "UV Channel"));
            maskContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("maskThreshold"), "Threshold"));
            root.Add(maskCard);
            
            // UV Seam Settings Card
            var seamCard = YUCPUIToolkitHelper.CreateCard("UV Seam Settings", "Configure seam-based detection");
            seamCard.name = "uv-seam-card";
            var seamContent = YUCPUIToolkitHelper.GetCardContent(seamCard);
            seamContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("seamUVChannel"), "UV Channel"));
            seamContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("seamThreshold"), "Seam Threshold"));
            seamContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("minRegionSize"), "Min Region Size %"));
            root.Add(seamCard);
            
            // Sharp Edge Settings Card
            var sharpCard = YUCPUIToolkitHelper.CreateCard("Sharp Edge Settings", "Configure sharp edge detection");
            sharpCard.name = "sharp-edge-card";
            var sharpContent = YUCPUIToolkitHelper.GetCardContent(sharpCard);
            sharpContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("sharpAngleThreshold"), "Angle Threshold"));
            sharpContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("useImportedSharpEdges"), "Use Imported Sharp Edges"));
            sharpContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("minRegionSize"), "Min Region Size %"));
            root.Add(sharpCard);
            
            // Blender Vertex Groups Card
            var vertexGroupCard = YUCPUIToolkitHelper.CreateCard("Blender Vertex Groups", "Import and configure vertex groups");
            vertexGroupCard.name = "vertex-group-card";
            var vertexGroupContent = YUCPUIToolkitHelper.GetCardContent(vertexGroupCard);
            
            var importGroupsButton = new Button(() => ImportBlenderVertexGroups());
            importGroupsButton.text = "Import Vertex Groups from Mesh";
            importGroupsButton.AddToClassList("yucp-button-primary");
            importGroupsButton.style.height = 30;
            importGroupsButton.style.marginBottom = 10;
            vertexGroupContent.Add(importGroupsButton);
            
            var vertexGroupsList = new VisualElement();
            vertexGroupsList.name = "vertex-groups-list";
            vertexGroupContent.Add(vertexGroupsList);
            
            vertexGroupContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("vertexGroupWeightThreshold"), "Weight Threshold"));
            root.Add(vertexGroupCard);
            
            // Material Slots Card (minimal settings)
            var materialSlotsCard = YUCPUIToolkitHelper.CreateCard("Material Slots Settings", "Detect by mesh material slots");
            materialSlotsCard.name = "material-slots-card";
            var materialSlotsContent = YUCPUIToolkitHelper.GetCardContent(materialSlotsCard);
            materialSlotsContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Each material slot on the clothing mesh will become a separate region.", YUCPUIToolkitHelper.MessageType.Info));
            root.Add(materialSlotsCard);
            
            // Bone Influence Card
            var boneCard = YUCPUIToolkitHelper.CreateCard("Bone Influence Settings", "Configure bone-based detection");
            boneCard.name = "bone-influence-card";
            var boneContent = YUCPUIToolkitHelper.GetCardContent(boneCard);
            boneContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("targetBones"), "Target Bones"));
            boneContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("boneWeightThreshold"), "Weight Threshold"));
            boneContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("includeChildBones"), "Include Child Bones"));
            root.Add(boneCard);
            
            // UV Tile Assignment Card
            var tileCard = YUCPUIToolkitHelper.CreateCard("UV Tile Assignment", "Configure UV tile coordinates");
            var tileContent = YUCPUIToolkitHelper.GetCardContent(tileCard);
            
            var autoAssignTileField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("autoAssignUVTile"), "Auto-Assign UV Tile");
            tileContent.Add(autoAssignTileField);
            
            var tileInfoContainer = new VisualElement();
            tileInfoContainer.name = "tile-info-container";
            var tileInfoLabel = new Label("Tile: Auto-assigned by orchestrator");
            tileInfoLabel.name = "tile-info-label";
            tileInfoLabel.style.fontSize = 12;
            tileInfoLabel.style.color = new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f));
            tileInfoLabel.style.marginTop = 5;
            tileInfoContainer.Add(tileInfoLabel);
            tileContent.Add(tileInfoContainer);
            
            var advancedTileFoldout = YUCPUIToolkitHelper.CreateFoldout("Advanced Tile Settings", false);
            advancedTileFoldout.name = "advanced-tile-foldout";
            var manualTileContainer = new VisualElement();
            manualTileContainer.name = "manual-tile-container";
            manualTileContainer.style.flexDirection = FlexDirection.Row;
            var startRowField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("startRow"), "Start Row");
            startRowField.style.flexGrow = 1;
            startRowField.style.marginRight = 5;
            manualTileContainer.Add(startRowField);
            var startColumnField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("startColumn"), "Start Column");
            startColumnField.style.flexGrow = 1;
            manualTileContainer.Add(startColumnField);
            advancedTileFoldout.Add(manualTileContainer);
            tileContent.Add(advancedTileFoldout);
            root.Add(tileCard);
            
            // Global Parameter Settings Card
            var globalParamCard = YUCPUIToolkitHelper.CreateCard("Global Parameters", "Control regions via VRChat parameters");
            var globalParamContent = YUCPUIToolkitHelper.GetCardContent(globalParamCard);

            var useSingleParamProp = serializedObject.FindProperty("useSingleGlobalParameter");
            
            // Mode Toggle (Pill style)
            var paramModeContainer = new VisualElement();
            paramModeContainer.style.flexDirection = FlexDirection.Row;
            paramModeContainer.style.marginBottom = 10;
            paramModeContainer.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f, 1f));
            // Fix borderRadius shorthand
            paramModeContainer.style.borderTopLeftRadius = 4;
            paramModeContainer.style.borderTopRightRadius = 4;
            paramModeContainer.style.borderBottomLeftRadius = 4;
            paramModeContainer.style.borderBottomRightRadius = 4;
            
            paramModeContainer.style.paddingTop = 2;
            paramModeContainer.style.paddingBottom = 2;
            paramModeContainer.style.paddingLeft = 2;
            paramModeContainer.style.paddingRight = 2;
            
            var multiParamButton = new Button(() => {
                useSingleParamProp.boolValue = false;
                serializedObject.ApplyModifiedProperties();
            });
            multiParamButton.text = "Individual Parameters";
            multiParamButton.style.flexGrow = 1;
            multiParamButton.style.height = 24;
            // Fix borderWidth shorthand
            multiParamButton.style.borderLeftWidth = 0;
            multiParamButton.style.borderRightWidth = 0;
            multiParamButton.style.borderTopWidth = 0;
            multiParamButton.style.borderBottomWidth = 0;
            
            var singleParamButton = new Button(() => {
                useSingleParamProp.boolValue = true;
                serializedObject.ApplyModifiedProperties();
            });
            singleParamButton.text = "Single Parameter";
            singleParamButton.style.flexGrow = 1;
            singleParamButton.style.height = 24;
            // Fix borderWidth shorthand
            singleParamButton.style.borderLeftWidth = 0;
            singleParamButton.style.borderRightWidth = 0;
            singleParamButton.style.borderTopWidth = 0;
            singleParamButton.style.borderBottomWidth = 0;
            
            // Initial styling update in schedule
            root.schedule.Execute(() => {
                bool isSingle = useSingleParamProp.boolValue;
                multiParamButton.style.backgroundColor = !isSingle ? new StyleColor(new Color(0.3f, 0.3f, 0.3f, 1f)) : new StyleColor(Color.clear);
                singleParamButton.style.backgroundColor = isSingle ? new StyleColor(new Color(0.3f, 0.3f, 0.3f, 1f)) : new StyleColor(Color.clear);
            }).Every(100);
            
            paramModeContainer.Add(multiParamButton);
            paramModeContainer.Add(singleParamButton);
            globalParamContent.Add(paramModeContainer);

            // Parameter Name Input
            var paramNameContainer = new VisualElement();
            paramNameContainer.style.marginBottom = 10;
            
            var singleParamField = new TextField("Parameter Name");
            singleParamField.BindProperty(serializedObject.FindProperty("singleGlobalParameterName"));
            singleParamField.name = "single-param-field";
            
            var baseParamField = new TextField("Base Parameter Name");
            baseParamField.BindProperty(serializedObject.FindProperty("globalParameterBaseName"));
            baseParamField.name = "param-base-name-field";
            
            paramNameContainer.Add(singleParamField);
            paramNameContainer.Add(baseParamField);
            globalParamContent.Add(paramNameContainer);

            // Parameter Preview/Help
            var paramHelpBox = new VisualElement();
            paramHelpBox.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f, 0.5f));
            // Fix borderRadius shorthand
            paramHelpBox.style.borderTopLeftRadius = 4;
            paramHelpBox.style.borderTopRightRadius = 4;
            paramHelpBox.style.borderBottomLeftRadius = 4;
            paramHelpBox.style.borderBottomRightRadius = 4;
            
            paramHelpBox.style.paddingTop = 8;
            paramHelpBox.style.paddingBottom = 8;
            paramHelpBox.style.paddingLeft = 8;
            paramHelpBox.style.paddingRight = 8;
            
            var paramPreviewLabel = new Label();
            paramPreviewLabel.name = "param-preview-label";
            paramPreviewLabel.style.fontSize = 11;
            paramPreviewLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f, 1f));
            paramHelpBox.Add(paramPreviewLabel);
            
            globalParamContent.Add(paramHelpBox);
            
            // Add available parameters container
            var availableParamsContainer = new VisualElement();
            availableParamsContainer.name = "available-params-container";
            globalParamContent.Add(availableParamsContainer);
            
            root.Add(globalParamCard);

            // Imported Vertex Groups Card (shown only for BlenderVertexGroups mode)
            var vertexGroupsCard = YUCPUIToolkitHelper.CreateCard("Imported Vertex Groups", "Detected groups from mesh");
            vertexGroupsCard.name = "vertex-groups-card";
            var vertexGroupsContent = YUCPUIToolkitHelper.GetCardContent(vertexGroupsCard);
            
            var importedGroupsList = new VisualElement();
            importedGroupsList.name = "imported-groups-list";
            importedGroupsList.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f, 1f));
            importedGroupsList.style.borderTopLeftRadius = 6;
            importedGroupsList.style.borderTopRightRadius = 6;
            importedGroupsList.style.borderBottomLeftRadius = 6;
            importedGroupsList.style.borderBottomRightRadius = 6;
            importedGroupsList.style.paddingTop = 5;
            importedGroupsList.style.paddingBottom = 5;
            importedGroupsList.style.paddingLeft = 8;
            importedGroupsList.style.paddingRight = 8;
            importedGroupsList.style.minHeight = 40;
            vertexGroupsContent.Add(importedGroupsList);
            
            root.Add(vertexGroupsCard);

            // Build Statistics Card
            var statsCard = YUCPUIToolkitHelper.CreateCard("Build Info", "Last processing results");
            var statsContent = YUCPUIToolkitHelper.GetCardContent(statsCard);
            
            var statsGrid = new VisualElement();
            statsGrid.style.flexDirection = FlexDirection.Row;
            statsGrid.style.justifyContent = Justify.SpaceBetween;
            statsGrid.style.marginBottom = 10;
            
            // Metric 1: Regions
            var regionMetric = CreateMetricElement("Regions Detected", serializedObject.FindProperty("detectedRegions").intValue.ToString());
            regionMetric.name = "regions-metric";
            statsGrid.Add(regionMetric);
            
            // Metric 2: Tiles
            var usedTilesProp = serializedObject.FindProperty("usedUVTiles");
            var tilesMetric = CreateMetricElement("Tiles Used", usedTilesProp.arraySize.ToString());
            tilesMetric.name = "tiles-metric";
            statsGrid.Add(tilesMetric);
            
            statsContent.Add(statsGrid);
            
            var usedTilesContainer = new VisualElement();
            usedTilesContainer.name = "used-tiles-container";
            usedTilesContainer.style.flexDirection = FlexDirection.Row;
            usedTilesContainer.style.flexWrap = Wrap.Wrap;
            usedTilesContainer.style.marginTop = 5;
            statsContent.Add(usedTilesContainer);
            
            root.Add(statsCard);

            // Initialize available parameters display logic
            if (availableParamsContainer != null)
            {
                UpdateAvailableParameters(availableParamsContainer, data);
            }
            
            root.schedule.Execute(() => UpdateParameterPreview(paramPreviewLabel, data)).Every(200);
            
            // Dynamic updates
            root.schedule.Execute(() =>
            {
                serializedObject.Update();
                
                // Update UV channel detection display
                var autoDetectUVProp = serializedObject.FindProperty("autoDetectUVChannel");
                bool autoDetectEnabled = autoDetectUVProp.boolValue;
                var manualUVSection = root.Q<VisualElement>("manual-uv-section");
                
                if (detectedUVContainer != null)
                {
                    Mesh currentMesh = data.targetBodyMesh != null ? data.targetBodyMesh.sharedMesh : null;
                    bool meshChanged = currentMesh != previousDetectedMesh;
                    bool autoDetectChanged = autoDetectEnabled != previousAutoDetectUVChannel;
                    
                    if (meshChanged || autoDetectChanged || detectedUVContainer.childCount == 0)
                    {
                        detectedUVContainer.Clear();
                        
                        if (autoDetectEnabled && currentMesh != null)
                        {
                            cachedDetectedUVChannel = UVManipulator.DetectBestUVChannel(currentMesh);
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
                
                // Update advanced foldout visibility
                var advancedUVFoldout = root.Q<Foldout>("advanced-uv-foldout");
                if (advancedUVFoldout != null)
                {
                    advancedUVFoldout.value = !autoDetectEnabled;
                }
                
                previousAutoDetectUVChannel = autoDetectEnabled;
                
                // Update detection mode card visibility
                var currentMode = (DetectionMode)serializedObject.FindProperty("detectionMode").enumValueIndex;
                
                var uvProximityCard = root.Q<VisualElement>("uv-proximity-card");
                var maskTextureCard = root.Q<VisualElement>("mask-texture-card");
                var uvSeamCard = root.Q<VisualElement>("uv-seam-card");
                var sharpEdgeCard = root.Q<VisualElement>("sharp-edge-card");
                var vertexGroupCard = root.Q<VisualElement>("vertex-group-card");
                var materialSlotsCard = root.Q<VisualElement>("material-slots-card");
                var boneInfluenceCard = root.Q<VisualElement>("bone-influence-card");
                
                if (uvProximityCard != null) uvProximityCard.style.display = currentMode == DetectionMode.UVProximity ? DisplayStyle.Flex : DisplayStyle.None;
                if (maskTextureCard != null) maskTextureCard.style.display = currentMode == DetectionMode.MaskTexture ? DisplayStyle.Flex : DisplayStyle.None;
                if (uvSeamCard != null) uvSeamCard.style.display = currentMode == DetectionMode.UVSeams ? DisplayStyle.Flex : DisplayStyle.None;
                if (sharpEdgeCard != null) sharpEdgeCard.style.display = currentMode == DetectionMode.SharpEdges ? DisplayStyle.Flex : DisplayStyle.None;
                if (vertexGroupCard != null) vertexGroupCard.style.display = currentMode == DetectionMode.BlenderVertexGroups ? DisplayStyle.Flex : DisplayStyle.None;
                if (materialSlotsCard != null) materialSlotsCard.style.display = currentMode == DetectionMode.MaterialSlots ? DisplayStyle.Flex : DisplayStyle.None;
                if (boneInfluenceCard != null) boneInfluenceCard.style.display = currentMode == DetectionMode.BoneInfluence ? DisplayStyle.Flex : DisplayStyle.None;
                
                // Update mode description label
                var modeDescLabel = root.Q<Label>("mode-description-label");
                if (modeDescLabel != null)
                {
                    modeDescLabel.text = currentMode switch
                    {
                        DetectionMode.UVProximity => "Clusters vertices by UV distance. Best for meshes with well-separated UV islands.",
                        DetectionMode.MaskTexture => "Uses a grayscale mask texture to define regions. Different gray levels = different regions.",
                        DetectionMode.UVSeams => "Detects UV seams from mesh unwrapping. Regions are bounded by seam edges.",
                        DetectionMode.SharpEdges => "Uses sharp edges/creases as region boundaries. Works with Blender/Maya marked edges.",
                        DetectionMode.BlenderVertexGroups => "Imports vertex groups from Blender via FBX. Click 'Import' to scan the mesh.",
                        DetectionMode.MaterialSlots => "Each material slot on the clothing mesh becomes a separate region.",
                        DetectionMode.BoneInfluence => "Groups vertices by their dominant bone influence. Select target bones to create regions.",
                        _ => ""
                    };
                }
                
                // Update material picker
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
                
                // Update tile assignment UI
                var autoAssignTileProp = serializedObject.FindProperty("autoAssignUVTile");
                bool autoAssign = autoAssignTileProp.boolValue;
                bool tileChanged = autoAssign != previousAutoAssignUVTile || 
                                  data.startRow != previousStartRow || 
                                  data.startColumn != previousStartColumn;
                
                if (tileInfoContainer != null)
                {
                    tileInfoContainer.style.display = autoAssign ? DisplayStyle.Flex : DisplayStyle.None;
                    
                    if (autoAssign && tileChanged)
                    {
                        var tileInfoLabel = tileInfoContainer.Q<Label>("tile-info-label");
                        if (tileInfoLabel != null)
                        {
                            if (data.startRow >= 0 && data.startColumn >= 0)
                            {
                                tileInfoLabel.text = $"Tile: ({data.startRow}, {data.startColumn}) - Auto-assigned by orchestrator";
                            }
                            else
                            {
                                tileInfoLabel.text = "Tile: Will be auto-assigned by orchestrator";
                            }
                        }
                    }
                }
                
                if (manualTileContainer != null)
                {
                    manualTileContainer.style.display = autoAssign ? DisplayStyle.None : DisplayStyle.Flex;
                }
                
                previousAutoAssignUVTile = autoAssign;
                previousStartRow = data.startRow;
                previousStartColumn = data.startColumn;
                
                // Update global parameter field visibility
                var singleParamField = root.Q<VisualElement>("single-param-field");
                var paramBaseNameField = root.Q<VisualElement>("param-base-name-field");
                if (singleParamField != null)
                {
                    singleParamField.style.display = serializedObject.FindProperty("useSingleGlobalParameter").boolValue ? DisplayStyle.Flex : DisplayStyle.None;
                }
                if (paramBaseNameField != null)
                {
                    paramBaseNameField.style.display = serializedObject.FindProperty("useSingleGlobalParameter").boolValue ? DisplayStyle.None : DisplayStyle.Flex;
                }
                
                // Update available parameters display
                UpdateAvailableParameters(availableParamsContainer, data);
                
                // Update vertex groups card visibility and content
                var vertexGroupsCardEl = root.Q<VisualElement>("vertex-groups-card");
                var importedGroupsListEl = root.Q<VisualElement>("imported-groups-list");
                
                if (vertexGroupsCardEl != null)
                {
                    vertexGroupsCardEl.style.display = currentMode == DetectionMode.BlenderVertexGroups ? DisplayStyle.Flex : DisplayStyle.None;
                    
                    if (currentMode == DetectionMode.BlenderVertexGroups && importedGroupsListEl != null)
                    {
                        UpdateVertexGroupsList(importedGroupsListEl, data);
                    }
                }
                
                // Update build statistics
                UpdateBuildStatistics(usedTilesContainer, usedTilesProp);
                
                serializedObject.ApplyModifiedProperties();
            }).Every(100);
            
            return root;
        }
        
        private void UpdateMaterialPicker(
            AutoUVDiscardData data,
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
            
            Material[] currentBodyMeshMaterials = data.targetBodyMesh != null ? data.targetBodyMesh.sharedMaterials : null;
            bool bodyMeshChanged = data.targetBodyMesh != previousTargetBodyMesh;
            bool materialsOnMeshChanged = false;
            
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
            
            if (materialsOnMeshChanged)
            {
                previousBodyMeshMaterials = currentBodyMeshMaterials != null ? (Material[])currentBodyMeshMaterials.Clone() : null;
            }
            
            bool materialsChanged = previousTargetMaterials == null || 
                                   previousTargetMaterials.Length != selectedMaterials.Length ||
                                   !selectedMaterials.SequenceEqual(previousTargetMaterials ?? new Material[0]);
            
            if (selectedMaterialsList != null)
            {
                selectedMaterialsList.Clear();
            }
            
            var validSelectedMaterials = selectedMaterials.Where(m => m != null).ToArray();
            
            if (validSelectedMaterials.Length > 0)
            {
                int compatibleCount = validSelectedMaterials.Count(m => UVManipulator.IsPoiyomiWithUVSupport(m));
                currentMaterialName.text = $"{validSelectedMaterials.Length} Material(s) Selected";
                currentMaterialShader.text = $"{compatibleCount} compatible";
                
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
                        bool isCompatible = UVManipulator.IsPoiyomiWithUVSupport(mat);
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
            
            if (materialsChanged)
            {
                previousTargetMaterials = validSelectedMaterials;
            }
            previousTargetBodyMesh = data.targetBodyMesh;
            
            if (needsGridRebuild)
            {
                materialGridContainer.Clear();
                
                if (data.targetBodyMesh != null && data.targetBodyMesh.sharedMaterials != null && data.targetBodyMesh.sharedMaterials.Length > 0)
                {
                    var availableMaterials = new List<Material>();
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
            
            if (!needsGridRebuild && materialGridContainer.childCount > 0)
            {
                foreach (var child in materialGridContainer.Children())
                {
                    if (child is VisualElement card && card.userData is Material cardMaterial)
                    {
                        bool isSelected = selectedMaterials != null && selectedMaterials.Contains(cardMaterial);
                        var borderColor = isSelected
                            ? new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f))
                            : new StyleColor(new Color(0.2f, 0.2f, 0.2f, 1f));
                        card.style.borderTopColor = borderColor;
                        card.style.borderRightColor = borderColor;
                        card.style.borderBottomColor = borderColor;
                        card.style.borderLeftColor = borderColor;
                        
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
            
            bool isCompatible = UVManipulator.IsPoiyomiWithUVSupport(material);
            
            var preview = new Image();
            preview.style.width = 84;
            preview.style.height = 60;
            preview.style.marginBottom = 6;
            preview.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 1f));
            
            Texture2D previewTexture = AssetPreview.GetAssetPreview(material);
            if (previewTexture == null)
            {
                previewTexture = AssetPreview.GetMiniThumbnail(material);
                AssetPreview.SetPreviewTextureCacheSize(256);
            }
            if (previewTexture != null)
            {
                preview.image = previewTexture;
            }
            
            card.userData = material;
            card.Add(preview);
            
            var nameLabel = new Label(material.name);
            nameLabel.style.fontSize = 11;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            nameLabel.style.whiteSpace = WhiteSpace.Normal;
            nameLabel.style.maxHeight = 30;
            nameLabel.style.overflow = Overflow.Hidden;
            card.Add(nameLabel);
            
            System.Action toggleMaterial = () =>
            {
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
                
                if (materialsList.Contains(material))
                {
                    materialsList.Remove(material);
                }
                else
                {
                    materialsList.Add(material);
                }
                
                targetMaterialsProp.arraySize = materialsList.Count;
                for (int i = 0; i < materialsList.Count; i++)
                {
                    targetMaterialsProp.GetArrayElementAtIndex(i).objectReferenceValue = materialsList[i];
                }
                
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(so.targetObject);
            };
            
            card.style.transitionDuration = new List<TimeValue> { new TimeValue(150, TimeUnit.Millisecond) };
            card.style.transitionProperty = new List<StylePropertyName> 
            { 
                new StylePropertyName("background-color"),
                new StylePropertyName("border-color"),
                new StylePropertyName("scale")
            };
            
            if (isCompatible)
            {
                var badge = new Label("UV Discard");
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
            
            card.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    evt.StopPropagation();
                    toggleMaterial();
                }
            }, TrickleDown.NoTrickleDown);
            
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
            
            if (isSelected)
            {
                card.style.borderTopColor = new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f));
                card.style.borderRightColor = new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f));
                card.style.borderBottomColor = new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f));
                card.style.borderLeftColor = new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f));
            }
            
            return card;
        }
        
        private (string name, string menuPath, string objectName, string globalParameter) GetVRCFuryToggleInfo(Component toggle, GameObject root)
        {
            try
            {
                var toggleType = toggle.GetType();
                var cField = toggleType.GetField("c", BindingFlags.NonPublic | BindingFlags.Instance);
                if (cField == null) return ("Unknown", "Unknown", "Unknown", null);
                
                var toggleModel = cField.GetValue(toggle);
                if (toggleModel == null) return ("Unknown", "Unknown", "Unknown", null);
                
                var stateField = toggleModel.GetType().GetField("state", BindingFlags.Public | BindingFlags.Instance);
                if (stateField == null) return ("Unknown", "Unknown", "Unknown", null);
                
                var state = stateField.GetValue(toggleModel);
                if (state == null) return ("Unknown", "Unknown", "Unknown", null);
                
                var menuPathField = state.GetType().GetField("menuPath", BindingFlags.Public | BindingFlags.Instance);
                string menuPath = menuPathField != null ? menuPathField.GetValue(state) as string : "Unknown";
                
                var nameField = state.GetType().GetField("name", BindingFlags.Public | BindingFlags.Instance);
                string name = nameField != null ? nameField.GetValue(state) as string : "Toggle";
                
                var globalParameterField = state.GetType().GetField("globalParameter", BindingFlags.Public | BindingFlags.Instance);
                string globalParameter = globalParameterField != null ? globalParameterField.GetValue(state) as string : null;
                
                string objectName = GetGameObjectPath(toggle.gameObject, root);
                
                return (name ?? "Toggle", menuPath ?? "Unknown", objectName, globalParameter);
            }
            catch
            {
                return ("Toggle", "Unknown", GetGameObjectPath(toggle.gameObject, root), null);
            }
        }
        
        private List<Component> GetAvailableVRCFuryToggles(GameObject root)
        {
            var toggles = new List<Component>();
            
            var rootComponents = root.GetComponents<Component>();
            foreach (var comp in rootComponents)
            {
                if (comp != null && comp.GetType().Name == "VRCFury")
                {
                    toggles.Add(comp);
                }
            }
            
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
            
            var path = new List<string>();
            Transform current = obj.transform;
            
            while (current != null && current != root.transform)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }
            
            return string.Join("/", path);
        }
        
        private void UpdateAvailableParameters(VisualElement container, AutoUVDiscardData data)
        {
            container.Clear();
            
            var availableToggles = GetAvailableVRCFuryToggles(data.gameObject);
            var globalParams = new List<(string paramName, string toggleName, string objectName)>();
            
            foreach (var toggle in availableToggles)
            {
                string globalParam = GetGlobalParameterFromToggle(toggle);
                if (!string.IsNullOrEmpty(globalParam))
                {
                    var toggleInfo = GetVRCFuryToggleInfo(toggle, data.gameObject);
                    globalParams.Add((globalParam, toggleInfo.name, toggleInfo.objectName));
                }
            }
            
            if (globalParams.Count > 0)
            {
                YUCPUIToolkitHelper.AddSpacing(container, 5);
                var label = new Label("Available Global Parameters from VRCFury Toggles:");
                label.style.fontSize = 12;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.marginBottom = 5;
                container.Add(label);
                
                foreach (var param in globalParams)
                {
                    var paramItem = new VisualElement();
                    paramItem.style.flexDirection = FlexDirection.Row;
                    paramItem.style.marginBottom = 3;
                    paramItem.style.paddingLeft = 5;
                    
                    var paramLabel = new Label($"• {param.paramName}");
                    paramLabel.style.fontSize = 11;
                    paramLabel.style.color = new StyleColor(new Color(0.212f, 0.749f, 0.694f, 1f));
                    paramLabel.style.flexGrow = 1;
                    paramItem.Add(paramLabel);
                    
                    var sourceLabel = new Label($"({param.toggleName} on {param.objectName})");
                    sourceLabel.style.fontSize = 10;
                    sourceLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f, 1f));
                    paramItem.Add(sourceLabel);
                    
                    container.Add(paramItem);
                }
                
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "These global parameters are available from existing VRCFury toggles. You can reference them in your global parameter base name.",
                    YUCPUIToolkitHelper.MessageType.Info));
            }
            else
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "No VRCFury toggles with global parameters found on this GameObject or its children.",
                    YUCPUIToolkitHelper.MessageType.None));
            }
        }
        
        private string GetGlobalParameterFromToggle(Component toggle)
        {
            try
            {
                var toggleType = toggle.GetType();
                var cField = toggleType.GetField("c", BindingFlags.NonPublic | BindingFlags.Instance);
                if (cField == null) return null;
                
                var toggleModel = cField.GetValue(toggle);
                if (toggleModel == null) return null;
                
                var stateField = toggleModel.GetType().GetField("state", BindingFlags.Public | BindingFlags.Instance);
                if (stateField == null) return null;
                
                var state = stateField.GetValue(toggleModel);
                if (state == null) return null;
                
                var globalParameterField = state.GetType().GetField("globalParameter", BindingFlags.Public | BindingFlags.Instance);
                if (globalParameterField == null) return null;
                
                return globalParameterField.GetValue(state) as string;
            }
            catch
            {
                return null;
            }
        }
        private void UpdateVertexGroupsList(VisualElement container, AutoUVDiscardData data)
        {
            container.Clear();
            
            if (data.blenderVertexGroups != null && data.blenderVertexGroups.Count > 0)
            {
                // Header row
                var headerRow = new VisualElement();
                headerRow.style.flexDirection = FlexDirection.Row;
                headerRow.style.marginBottom = 5;
                headerRow.style.paddingBottom = 5;
                headerRow.style.borderBottomWidth = 1;
                headerRow.style.borderBottomColor = new StyleColor(new Color(1f, 1f, 1f, 0.1f));
                
                var col1 = new Label("Group Name");
                col1.style.width = 150;
                col1.style.fontSize = 11;
                col1.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
                headerRow.Add(col1);
                
                var col2 = new Label("Vertices");
                col2.style.flexGrow = 1;
                col2.style.fontSize = 11;
                col2.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
                headerRow.Add(col2);
                
                container.Add(headerRow);
                
                // Groups list
                var scroll = new ScrollView();
                scroll.style.maxHeight = 200;
                
                Color[] groupColors = new Color[]
                {
                    Color.red, Color.green, new Color(0.212f, 0.749f, 0.694f), Color.yellow,
                    Color.cyan, Color.magenta, new Color(1f, 0.5f, 0f), new Color(0.5f, 0f, 1f)
                };
                
                for (int i = 0; i < data.blenderVertexGroups.Count; i++)
                {
                    var group = data.blenderVertexGroups[i];
                    
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.marginBottom = 2;
                    row.style.alignItems = Align.Center;
                    
                    // Name + Color swatch
                    var nameContainer = new VisualElement();
                    nameContainer.style.flexDirection = FlexDirection.Row;
                    nameContainer.style.alignItems = Align.Center;
                    nameContainer.style.width = 150;
                    
                    var swatch = new VisualElement();
                    swatch.style.width = 10;
                    swatch.style.height = 10;
                    swatch.style.borderTopLeftRadius = 5;
                    swatch.style.borderTopRightRadius = 5;
                    swatch.style.borderBottomLeftRadius = 5;
                    swatch.style.borderBottomRightRadius = 5;
                    swatch.style.backgroundColor = new StyleColor(groupColors[i % groupColors.Length]);
                    swatch.style.marginRight = 6;
                    nameContainer.Add(swatch);
                    
                    var nameLabel = new Label(group.name);
                    nameLabel.style.fontSize = 11;
                    nameLabel.style.overflow = Overflow.Hidden;
                    nameLabel.style.unityTextOverflowPosition = TextOverflowPosition.End;
                    nameContainer.Add(nameLabel);
                    row.Add(nameContainer);
                    
                    // Vertex Count
                    int vertexCount = group.weights != null ? group.weights.Count : 0;
                    var countLabel = new Label(vertexCount.ToString());
                    countLabel.style.flexGrow = 1;
                    countLabel.style.fontSize = 11;
                    row.Add(countLabel);
                    
                    scroll.Add(row);
                }
                
                container.Add(scroll);
            }
            else
            {
                var emptyState = new VisualElement();
                emptyState.style.alignItems = Align.Center;
                emptyState.style.justifyContent = Justify.Center;
                emptyState.style.height = 40;
                
                var emptyLabel = new Label("No vertex groups imported. Click Import in the settings above.");
                emptyLabel.style.fontSize = 11;
                emptyLabel.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
                emptyState.Add(emptyLabel);
                
                container.Add(emptyState);
            }
        }
        
        private VisualElement CreateMetricElement(string label, string value)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;
            container.style.alignItems = Align.Center;
            container.style.flexGrow = 1;
            
            var valueLabel = new Label(value);
            valueLabel.style.fontSize = 18;
            valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            valueLabel.style.color = new StyleColor(new Color(0.212f, 0.749f, 0.694f));
            container.Add(valueLabel);
            
            var nameLabel = new Label(label);
            nameLabel.style.fontSize = 10;
            nameLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
            container.Add(nameLabel);
            
            return container;
        }
        
        private void UpdateParameterPreview(Label previewLabel, AutoUVDiscardData data)
        {
            if (previewLabel == null || data == null) return;
            
            string baseParam = string.IsNullOrEmpty(data.globalParameterBaseName) ? "AutoUVDiscard" : data.globalParameterBaseName;
            
            if (data.useSingleGlobalParameter)
            {
                string singleParam = string.IsNullOrEmpty(data.singleGlobalParameterName) ? $"{baseParam}_All" : data.singleGlobalParameterName;
                previewLabel.text = $"Output: int {singleParam}";
            }
            else
            {
                previewLabel.text = $"Output: bool {baseParam}_RegionName1, bool {baseParam}_RegionName2, ...";
            }
        }
        
        private void UpdateBuildStatistics(VisualElement container, SerializedProperty usedTilesProp)
        {
            container.Clear();
            
            if (usedTilesProp.arraySize > 0)
            {
                var label = new Label("Used Tiles:");
                label.style.fontSize = 12;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.marginBottom = 5;
                container.Add(label);
                
                for (int i = 0; i < usedTilesProp.arraySize; i++)
                {
                    var tileLabel = new Label($"  • {usedTilesProp.GetArrayElementAtIndex(i).stringValue}");
                    tileLabel.style.fontSize = 11;
                    container.Add(tileLabel);
                }
            }
        }
        
        private bool ValidateData()
        {
            if (data.targetBodyMesh == null) return false;
            var clothingRenderer = data.GetComponent<SkinnedMeshRenderer>();
            if (clothingRenderer == null || clothingRenderer.sharedMesh == null) return false;
            return true;
        }
        
        private string GetValidationError()
        {
            if (data.targetBodyMesh == null) return "Target Body Mesh is not set.";
            var clothingRenderer = data.GetComponent<SkinnedMeshRenderer>();
            if (clothingRenderer == null) return "No SkinnedMeshRenderer found on this GameObject.";
            if (clothingRenderer.sharedMesh == null) return "SkinnedMeshRenderer has no mesh data.";
            return "";
        }
        
        private void GeneratePreview()
        {
            isGeneratingPreview = true;
            
            try
            {
                var clothingRenderer = data.GetComponent<SkinnedMeshRenderer>();
                if (clothingRenderer == null || clothingRenderer.sharedMesh == null)
                {
                    EditorUtility.DisplayDialog("Error", "No SkinnedMeshRenderer or mesh found on this object!", "OK");
                    return;
                }

                Mesh mesh = clothingRenderer.sharedMesh;
                int uvChannel = data.autoDetectUVChannel 
                    ? UVManipulator.DetectBestUVChannel(mesh)
                    : data.uvChannel;

                // Mode-specific detection
                List<List<int>> clusters = null;
                Vector2[] uvs = null;
                
                Color[] debugColors = new Color[]
                {
                    Color.red, Color.green, new Color(0.212f, 0.749f, 0.694f), Color.yellow,
                    Color.cyan, Color.magenta, new Color(1f, 0.5f, 0f), new Color(0.5f, 0f, 1f)
                };
                
                data.previewRegions = new List<AutoUVDiscardData.UVRegion>();
                
                switch (data.detectionMode)
                {
                    case DetectionMode.UVProximity:
                        uvs = GetUVChannel(mesh, uvChannel);
                        if (uvs == null || uvs.Length == 0)
                        {
                            EditorUtility.DisplayDialog("Error", $"No UV{uvChannel} data found on mesh!", "OK");
                            return;
                        }
                        clusters = ClusterVerticesByUV(uvs, data.mergeTolerance);
                        int minVertices = Mathf.CeilToInt(mesh.vertexCount * (data.minRegionSize / 100f));
                        clusters = clusters.Where(c => c.Count >= minVertices).ToList();
                        
                        for (int i = 0; i < clusters.Count; i++)
                        {
                            var region = CreateRegionFromCluster(clusters[i], uvs, debugColors[i % debugColors.Length]);
                            data.previewRegions.Add(region);
                        }
                        break;
                        
                    case DetectionMode.MaskTexture:
                        if (data.maskRegions == null || data.maskRegions.Count == 0)
                        {
                            EditorUtility.DisplayDialog("Error", "No mask regions defined! Add at least one mask.", "OK");
                            return;
                        }
                        uvs = GetUVChannel(mesh, data.maskUVChannel);
                        if (uvs == null || uvs.Length == 0)
                        {
                            EditorUtility.DisplayDialog("Error", $"No UV{data.maskUVChannel} data found for mask sampling!", "OK");
                            return;
                        }
                        
                        foreach (var maskDef in data.maskRegions)
                        {
                            if (!maskDef.enabled || maskDef.maskTexture == null)
                                continue;
                                
                            var vertexIndices = new List<int>();
                            for (int v = 0; v < uvs.Length; v++)
                            {
                                float maskValue = UVManipulator.SampleMaskAtUV(maskDef.maskTexture, uvs[v]);
                                if (maskValue >= data.maskThreshold)
                                    vertexIndices.Add(v);
                            }
                            
                            if (vertexIndices.Count > 0)
                            {
                                var region = new AutoUVDiscardData.UVRegion
                                {
                                    vertexIndices = vertexIndices,
                                    name = maskDef.name,
                                    debugColor = maskDef.debugColor
                                };
                                CalculateRegionBounds(region, uvs);
                                data.previewRegions.Add(region);
                            }
                        }
                        break;
                        
                    case DetectionMode.MaterialSlots:
                        int[] triangles = mesh.triangles;
                        int subMeshCount = mesh.subMeshCount;
                        uvs = GetUVChannel(mesh, uvChannel);
                        
                        for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                        {
                            var subMeshDesc = mesh.GetSubMesh(subMesh);
                            var vertexIndices = new HashSet<int>();
                            
                            int startIndex = subMeshDesc.indexStart;
                            int indexCount = subMeshDesc.indexCount;
                            
                            for (int i = startIndex; i < startIndex + indexCount; i++)
                            {
                                vertexIndices.Add(triangles[i]);
                            }
                            
                            if (vertexIndices.Count > 0)
                            {
                                var region = new AutoUVDiscardData.UVRegion
                                {
                                    vertexIndices = vertexIndices.ToList(),
                                    name = $"Material_{subMesh}",
                                    debugColor = debugColors[subMesh % debugColors.Length]
                                };
                                if (uvs != null && uvs.Length > 0)
                                    CalculateRegionBounds(region, uvs);
                                data.previewRegions.Add(region);
                            }
                        }
                        break;
                        
                    case DetectionMode.BoneInfluence:
                        BoneWeight[] boneWeights = mesh.boneWeights;
                        if (boneWeights == null || boneWeights.Length == 0)
                        {
                            EditorUtility.DisplayDialog("Error", "Mesh has no bone weights!", "OK");
                            return;
                        }
                        uvs = GetUVChannel(mesh, uvChannel);
                        
                        var boneGroups = new Dictionary<int, List<int>>();
                        for (int v = 0; v < boneWeights.Length; v++)
                        {
                            int dominantBone = GetDominantBone(boneWeights[v]);
                            if (!boneGroups.ContainsKey(dominantBone))
                                boneGroups[dominantBone] = new List<int>();
                            boneGroups[dominantBone].Add(v);
                        }
                        
                        int boneIdx = 0;
                        foreach (var kvp in boneGroups)
                        {
                            if (kvp.Value.Count > 0)
                            {
                                var region = new AutoUVDiscardData.UVRegion
                                {
                                    vertexIndices = kvp.Value,
                                    name = $"Bone_{kvp.Key}",
                                    debugColor = debugColors[boneIdx % debugColors.Length]
                                };
                                if (uvs != null && uvs.Length > 0)
                                    CalculateRegionBounds(region, uvs);
                                data.previewRegions.Add(region);
                                boneIdx++;
                            }
                        }
                        break;
                        
                    default:
                        // Fallback to UV Proximity for unsupported modes
                        uvs = GetUVChannel(mesh, uvChannel);
                        if (uvs != null && uvs.Length > 0)
                        {
                            clusters = ClusterVerticesByUV(uvs, data.mergeTolerance);
                            for (int i = 0; i < clusters.Count; i++)
                            {
                                var region = CreateRegionFromCluster(clusters[i], uvs, debugColors[i % debugColors.Length]);
                                data.previewRegions.Add(region);
                            }
                        }
                        break;
                }
                
                // Sort regions by UV position
                if (data.previewRegions.Count > 0)
                {
                    data.previewRegions = data.previewRegions
                        .OrderByDescending(r => r.uvCenter.y)
                        .ThenBy(r => r.uvCenter.x)
                        .ToList();
                }
                
                // Assign UV tiles
                int currentRow = data.autoAssignUVTile ? -1 : (data.startRow >= 0 ? data.startRow : 3);
                int currentColumn = data.autoAssignUVTile ? -1 : (data.startColumn >= 0 ? data.startColumn : 0);
                
                for (int i = 0; i < data.previewRegions.Count; i++)
                {
                    var region = data.previewRegions[i];
                    
                    if (data.autoAssignUVTile)
                    {
                        region.assignedRow = -1;
                        region.assignedColumn = -1;
                    }
                    else
                    {
                        region.assignedRow = currentRow;
                        region.assignedColumn = currentColumn;
                        
                        currentColumn++;
                        if (currentColumn > 3)
                        {
                            currentColumn = 0;
                            currentRow++;
                        }
                    }
                    
                    if (string.IsNullOrEmpty(region.name))
                        region.name = $"Region_{i}";
                }

            data.previewGenerated = true;
            EditorUtility.SetDirty(data);
            
            Debug.Log($"[AutoUVDiscard] Preview generated: {data.previewRegions.Count} regions detected");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AutoUVDiscard] Error generating preview: {ex.Message}", data);
                EditorUtility.DisplayDialog("Error", $"Failed to generate preview:\n\n{ex.Message}", "OK");
            }
            finally
            {
                isGeneratingPreview = false;
                Repaint();
            }
        }
        
        private void ClearPreview()
        {
            data.previewRegions = null;
            data.previewGenerated = false;
            EditorUtility.SetDirty(data);
            Repaint();
        }

        private Vector2[] GetUVChannel(Mesh mesh, int channel)
        {
            List<Vector2> uvList = new List<Vector2>();

            switch (channel)
            {
                case 0: mesh.GetUVs(0, uvList); break;
                case 1: mesh.GetUVs(1, uvList); break;
                case 2: mesh.GetUVs(2, uvList); break;
                case 3: mesh.GetUVs(3, uvList); break;
                default: return null;
            }

            return uvList.ToArray();
        }

        private List<List<int>> ClusterVerticesByUV(Vector2[] uvs, float tolerance)
        {
            List<List<int>> clusters = new List<List<int>>();
            bool[] assigned = new bool[uvs.Length];

            for (int i = 0; i < uvs.Length; i++)
            {
                if (assigned[i]) continue;

                List<int> cluster = new List<int>();
                Queue<int> toProcess = new Queue<int>();
                toProcess.Enqueue(i);
                assigned[i] = true;

                while (toProcess.Count > 0)
                {
                    int current = toProcess.Dequeue();
                    cluster.Add(current);

                    for (int j = 0; j < uvs.Length; j++)
                    {
                        if (assigned[j]) continue;

                        float distance = Vector2.Distance(uvs[current], uvs[j]);
                        if (distance <= tolerance)
                        {
                            assigned[j] = true;
                            toProcess.Enqueue(j);
                        }
                    }
                }

                if (cluster.Count > 0)
                    clusters.Add(cluster);
            }

            return clusters;
        }
        
        private AutoUVDiscardData.UVRegion CreateRegionFromCluster(List<int> cluster, Vector2[] uvs, Color debugColor)
        {
            var region = new AutoUVDiscardData.UVRegion
            {
                vertexIndices = cluster,
                debugColor = debugColor
            };
            
            CalculateRegionBounds(region, uvs);
            return region;
        }
        
        private void CalculateRegionBounds(AutoUVDiscardData.UVRegion region, Vector2[] uvs)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            
            foreach (int vertexIdx in region.vertexIndices)
            {
                if (vertexIdx >= 0 && vertexIdx < uvs.Length)
                {
                    Vector2 uv = uvs[vertexIdx];
                    min = Vector2.Min(min, uv);
                    max = Vector2.Max(max, uv);
                }
            }
            
            region.uvBounds = new Bounds(
                new Vector3((min.x + max.x) / 2f, (min.y + max.y) / 2f, 0),
                new Vector3(max.x - min.x, max.y - min.y, 0)
            );
            region.uvCenter = new Vector2((min.x + max.x) / 2f, (min.y + max.y) / 2f);
        }
        
        private int GetDominantBone(BoneWeight bw)
        {
            int dominantBone = bw.boneIndex0;
            float maxWeight = bw.weight0;
            
            if (bw.weight1 > maxWeight) { dominantBone = bw.boneIndex1; maxWeight = bw.weight1; }
            if (bw.weight2 > maxWeight) { dominantBone = bw.boneIndex2; maxWeight = bw.weight2; }
            if (bw.weight3 > maxWeight) { dominantBone = bw.boneIndex3; }
            
            return dominantBone;
        }

        private void ImportBlenderVertexGroups()
        {
            if (data == null) return;
            
            var renderer = data.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null || renderer.sharedMesh == null)
            {
                EditorUtility.DisplayDialog("Import Error", "No SkinnedMeshRenderer with mesh found on this GameObject.", "OK");
                return;
            }

            Mesh mesh = renderer.sharedMesh;
            
            // Unity doesn't preserve Blender vertex group names directly
            // However, we can create groups based on bone influences or submeshes
            
            serializedObject.Update();
            var groupsProp = serializedObject.FindProperty("blenderVertexGroups");
            groupsProp.ClearArray();
            
            // Option 1: Create groups from bone names
            if (renderer.bones != null && renderer.bones.Length > 0)
            {
                BoneWeight[] weights = mesh.boneWeights;
                var boneVertexGroups = new Dictionary<int, List<VertexWeight>>();
                
                for (int v = 0; v < weights.Length; v++)
                {
                    BoneWeight bw = weights[v];
                    
                    // Add to dominant bone group
                    int dominantBone = -1;
                    float maxWeight = 0.1f; // Minimum threshold
                    
                    if (bw.weight0 > maxWeight) { dominantBone = bw.boneIndex0; maxWeight = bw.weight0; }
                    if (bw.weight1 > maxWeight) { dominantBone = bw.boneIndex1; maxWeight = bw.weight1; }
                    if (bw.weight2 > maxWeight) { dominantBone = bw.boneIndex2; maxWeight = bw.weight2; }
                    if (bw.weight3 > maxWeight) { dominantBone = bw.boneIndex3; maxWeight = bw.weight3; }
                    
                    if (dominantBone >= 0 && dominantBone < renderer.bones.Length)
                    {
                        if (!boneVertexGroups.ContainsKey(dominantBone))
                            boneVertexGroups[dominantBone] = new List<VertexWeight>();
                        
                        boneVertexGroups[dominantBone].Add(new VertexWeight 
                        { 
                            vertexIndex = v, 
                            weight = maxWeight 
                        });
                    }
                }
                
                // Only add groups with significant vertex counts
                int minVertices = Mathf.Max(10, mesh.vertexCount / 50);
                Color[] colors = { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta, new Color(1f, 0.5f, 0f), new Color(0.5f, 0f, 1f) };
                int colorIdx = 0;
                
                foreach (var kvp in boneVertexGroups)
                {
                    if (kvp.Value.Count >= minVertices && kvp.Key < renderer.bones.Length && renderer.bones[kvp.Key] != null)
                    {
                        int idx = groupsProp.arraySize;
                        groupsProp.InsertArrayElementAtIndex(idx);
                        var groupProp = groupsProp.GetArrayElementAtIndex(idx);
                        
                        groupProp.FindPropertyRelative("name").stringValue = renderer.bones[kvp.Key].name;
                        groupProp.FindPropertyRelative("enabled").boolValue = true;
                        
                        var colorProp = groupProp.FindPropertyRelative("debugColor");
                        var color = colors[colorIdx % colors.Length];
                        colorProp.colorValue = color;
                        colorIdx++;
                        
                        var weightsProp = groupProp.FindPropertyRelative("weights");
                        weightsProp.ClearArray();
                        
                        foreach (var vw in kvp.Value)
                        {
                            int wIdx = weightsProp.arraySize;
                            weightsProp.InsertArrayElementAtIndex(wIdx);
                            var weightProp = weightsProp.GetArrayElementAtIndex(wIdx);
                            weightProp.FindPropertyRelative("vertexIndex").intValue = vw.vertexIndex;
                            weightProp.FindPropertyRelative("weight").floatValue = vw.weight;
                        }
                    }
                }
            }
            
            serializedObject.ApplyModifiedProperties();
            
            int groupCount = data.blenderVertexGroups?.Count ?? 0;
            if (groupCount > 0)
            {
                EditorUtility.DisplayDialog("Import Complete", 
                    $"Imported {groupCount} vertex groups from bone influences.\n\n" +
                    "Note: Unity doesn't preserve Blender vertex group names directly. " +
                    "Groups were created from dominant bone influences as a starting point.", 
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Import Complete", 
                    "No suitable vertex groups found.\n\n" +
                    "The mesh may not have bone weights, or no bones have enough vertex influence.\n" +
                    "You can manually add vertex groups if needed.", 
                    "OK");
            }
        }
    }
}
