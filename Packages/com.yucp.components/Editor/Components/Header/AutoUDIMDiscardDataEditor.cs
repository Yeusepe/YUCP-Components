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
    [CustomEditor(typeof(AutoUDIMDiscardData))]
    public class AutoUDIMDiscardDataEditor : UnityEditor.Editor
    {
        private AutoUDIMDiscardData data;
        private bool isGeneratingPreview = false;
        
        // State tracking to prevent UI flickering
        private Material[] previousTargetMaterials = null;
        private SkinnedMeshRenderer previousTargetBodyMesh = null;
        private Mesh previousDetectedMesh = null;
        private int cachedDetectedUVChannel = 1;
        private Material[] previousBodyMeshMaterials = null;
        private bool previousAutoDetectUVChannel = true;
        private bool previousAutoAssignUDIMTile = true;
        private int previousStartRow = -1;
        private int previousStartColumn = -1;

        private void OnEnable()
        {
            data = (AutoUDIMDiscardData)target;
            previousAutoDetectUVChannel = data.autoDetectUVChannel;
            previousAutoAssignUDIMTile = data.autoAssignUDIMTile;
            previousStartRow = data.startRow;
            previousStartColumn = data.startColumn;
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Auto UDIM Discard"));
            
            var betaWarning = BetaWarningHelper.CreateBetaWarningVisualElement(typeof(AutoUDIMDiscardData));
            if (betaWarning != null) root.Add(betaWarning);
            
            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(AutoUDIMDiscardData));
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
            
            // Detection Settings Card
            var detectionCard = YUCPUIToolkitHelper.CreateCard("Detection Settings", "Configure UV region detection");
            var detectionContent = YUCPUIToolkitHelper.GetCardContent(detectionCard);
            
            var autoDetectUVField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("autoDetectUVChannel"), "Auto-Detect UV Channel");
            detectionContent.Add(autoDetectUVField);
            
            var detectedUVContainer = new VisualElement();
            detectedUVContainer.name = "detected-uv-container";
            detectionContent.Add(detectedUVContainer);
            
            var advancedUVFoldout = YUCPUIToolkitHelper.CreateFoldout("Advanced UV Settings", false);
            advancedUVFoldout.name = "advanced-uv-foldout";
            var manualUVSection = new VisualElement();
            manualUVSection.name = "manual-uv-section";
            var uvChannelField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("uvChannel"), "UV Channel");
            manualUVSection.Add(uvChannelField);
            advancedUVFoldout.Add(manualUVSection);
            detectionContent.Add(advancedUVFoldout);
            
            detectionContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("mergeTolerance"), "Merge Tolerance"));
            detectionContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("minRegionSize"), "Min Region Size %"));
            root.Add(detectionCard);
            
            // UDIM Tile Assignment Card
            var tileCard = YUCPUIToolkitHelper.CreateCard("UDIM Tile Assignment", "Configure UDIM tile coordinates");
            var tileContent = YUCPUIToolkitHelper.GetCardContent(tileCard);
            
            var autoAssignTileField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("autoAssignUDIMTile"), "Auto-Assign UDIM Tile");
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
            var globalParamCard = YUCPUIToolkitHelper.CreateCard("Global Parameter Settings", "Configure VRCFury global parameters");
            globalParamCard.name = "global-param-card";
            var globalParamContent = YUCPUIToolkitHelper.GetCardContent(globalParamCard);
            
            var useSingleParamField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("useSingleGlobalParameter"), "Use Single Global Parameter");
            globalParamContent.Add(useSingleParamField);
            
            var singleParamField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("singleGlobalParameterName"), "Single Global Parameter Name");
            singleParamField.name = "single-param-field";
            singleParamField.style.paddingLeft = 15;
            globalParamContent.Add(singleParamField);
            
            var paramBaseNameField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("globalParameterBaseName"), "Global Parameter Base Name");
            paramBaseNameField.name = "param-base-name-field";
            paramBaseNameField.style.paddingLeft = 15;
            globalParamContent.Add(paramBaseNameField);
            
            var availableParamsContainer = new VisualElement();
            availableParamsContainer.name = "available-params-container";
            globalParamContent.Add(availableParamsContainer);
            
            root.Add(globalParamCard);
            
            // Preview Card
            var previewCard = YUCPUIToolkitHelper.CreateCard("Preview", "Preview detected UV regions");
            var previewContent = YUCPUIToolkitHelper.GetCardContent(previewCard);
            
            var previewActionButton = new Button(() => {
                if (data.previewGenerated)
                {
                    ClearPreview();
                }
                else
                {
                    GeneratePreview();
                }
            });
            previewActionButton.name = "preview-action-button";
            previewActionButton.text = "Generate Preview";
            previewActionButton.AddToClassList("yucp-button-primary");
            previewActionButton.style.height = 35;
            previewContent.Add(previewActionButton);
            
            var previewInfo = new VisualElement();
            previewInfo.name = "preview-info";
            previewContent.Add(previewInfo);
            root.Add(previewCard);
            
            // Build Statistics Card
            var statsCard = YUCPUIToolkitHelper.CreateCard("Build Statistics", "Last build results");
            var statsContent = YUCPUIToolkitHelper.GetCardContent(statsCard);
            statsContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("detectedRegions"), "Detected Regions"));
            
            var usedTilesProp = serializedObject.FindProperty("usedTiles");
            var usedTilesContainer = new VisualElement();
            usedTilesContainer.name = "used-tiles-container";
            statsContent.Add(usedTilesContainer);
            root.Add(statsCard);
            
            // Initialize available parameters display
            UpdateAvailableParameters(availableParamsContainer, data);
            
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
                
                // Update advanced foldout visibility
                var advancedUVFoldout = root.Q<Foldout>("advanced-uv-foldout");
                if (advancedUVFoldout != null)
                {
                    advancedUVFoldout.value = !autoDetectEnabled;
                }
                
                previousAutoDetectUVChannel = autoDetectEnabled;
                
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
                var autoAssignTileProp = serializedObject.FindProperty("autoAssignUDIMTile");
                bool autoAssign = autoAssignTileProp.boolValue;
                bool tileChanged = autoAssign != previousAutoAssignUDIMTile || 
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
                
                previousAutoAssignUDIMTile = autoAssign;
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
                
                // Update preview button
                if (previewActionButton != null)
                {
                    bool hasPreview = data.previewGenerated;
                    bool canGenerate = !isGeneratingPreview && ValidateData();
                    
                    previewActionButton.SetEnabled(isGeneratingPreview || hasPreview || canGenerate);
                    
                    if (!canGenerate && !hasPreview && !isGeneratingPreview)
                    {
                        previewActionButton.tooltip = GetValidationError();
                    }
                    else
                    {
                        previewActionButton.tooltip = "";
                    }
                    
                    if (isGeneratingPreview)
                    {
                        previewActionButton.text = "Generating...";
                        previewActionButton.RemoveFromClassList("yucp-button-danger");
                        previewActionButton.AddToClassList("yucp-button-primary");
                    }
                    else if (hasPreview)
                    {
                        previewActionButton.text = "Clear Preview";
                        previewActionButton.RemoveFromClassList("yucp-button-primary");
                        previewActionButton.AddToClassList("yucp-button-danger");
                    }
                    else
                    {
                        previewActionButton.text = "Generate Preview";
                        previewActionButton.RemoveFromClassList("yucp-button-danger");
                        previewActionButton.AddToClassList("yucp-button-primary");
                    }
                }
                
                // Update preview info
                UpdatePreviewInfo(previewInfo, data);
                
                // Update build statistics
                UpdateBuildStatistics(usedTilesContainer, usedTilesProp);
                
                serializedObject.ApplyModifiedProperties();
            }).Every(100);
            
            return root;
        }
        
        private void UpdateMaterialPicker(
            AutoUDIMDiscardData data,
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
                int compatibleCount = validSelectedMaterials.Count(m => UDIMManipulator.IsPoiyomiWithUDIMSupport(m));
                currentMaterialName.text = $"{validSelectedMaterials.Length} Material(s) Selected";
                currentMaterialShader.text = $"{compatibleCount} compatible with UDIM Discard";
                
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
            
            bool isCompatible = UDIMManipulator.IsPoiyomiWithUDIMSupport(material);
            
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
        
        private void UpdateAvailableParameters(VisualElement container, AutoUDIMDiscardData data)
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
        
        private void UpdatePreviewInfo(VisualElement container, AutoUDIMDiscardData data)
        {
            container.Clear();
            
            if (data.previewGenerated && data.previewRegions != null && data.previewRegions.Count > 0)
            {
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
                
                var statsText = new Label($"Detected {data.previewRegions.Count} UV regions");
                statsText.style.fontSize = 11;
                statsText.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f, 1f));
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

                int uvChannel = data.autoDetectUVChannel 
                    ? UDIMManipulator.DetectBestUVChannel(clothingRenderer.sharedMesh)
                    : data.uvChannel;
                
                Vector2[] uvs = GetUVChannel(clothingRenderer.sharedMesh, uvChannel);
            if (uvs == null || uvs.Length == 0)
            {
                    EditorUtility.DisplayDialog("Error", $"No UV{uvChannel} data found on mesh!", "OK");
                return;
            }

            List<List<int>> clusters = ClusterVerticesByUV(uvs, data.mergeTolerance);

            int minVertices = Mathf.CeilToInt(clothingRenderer.sharedMesh.vertexCount * (data.minRegionSize / 100f));
            clusters = clusters.Where(c => c.Count >= minVertices).ToList();

            data.previewRegions = new List<AutoUDIMDiscardData.UVRegion>();
            Color[] debugColors = new Color[]
            {
                Color.red, Color.green, new Color(0.212f, 0.749f, 0.694f), Color.yellow,
                new Color(0.212f, 0.749f, 0.694f), Color.magenta, new Color(1f, 0.5f, 0f), new Color(0.5f, 0f, 1f)
            };

            for (int i = 0; i < clusters.Count; i++)
            {
                var cluster = clusters[i];
                var region = new AutoUDIMDiscardData.UVRegion
                {
                    vertexIndices = cluster,
                    debugColor = debugColors[i % debugColors.Length]
                };

                Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 max = new Vector2(float.MinValue, float.MinValue);

                foreach (int vertexIdx in cluster)
                {
                    Vector2 uv = uvs[vertexIdx];
                    min = Vector2.Min(min, uv);
                    max = Vector2.Max(max, uv);
                }

                region.uvBounds = new Bounds(
                    new Vector3((min.x + max.x) / 2f, (min.y + max.y) / 2f, 0),
                    new Vector3(max.x - min.x, max.y - min.y, 0)
                );
                region.uvCenter = new Vector2((min.x + max.x) / 2f, (min.y + max.y) / 2f);

                data.previewRegions.Add(region);
            }

            data.previewRegions = data.previewRegions.OrderByDescending(r => r.uvCenter.y)
                                                     .ThenBy(r => r.uvCenter.x)
                                                     .ToList();

                int currentRow = data.autoAssignUDIMTile ? -1 : (data.startRow >= 0 ? data.startRow : 3);
                int currentColumn = data.autoAssignUDIMTile ? -1 : (data.startColumn >= 0 ? data.startColumn : 0);

            foreach (var region in data.previewRegions)
                {
                    if (data.autoAssignUDIMTile)
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
                    region.name = $"Region_{region.assignedRow}_{region.assignedColumn}";
            }

            data.previewGenerated = true;
            EditorUtility.SetDirty(data);
            
            Debug.Log($"[AutoUDIMDiscard] Preview generated: {data.previewRegions.Count} regions detected");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AutoUDIMDiscard] Error generating preview: {ex.Message}", data);
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
    }
}
