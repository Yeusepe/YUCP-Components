using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using System.Collections.Generic;
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
        
        // Track previous values to prevent unnecessary UI updates
        private Component previousVrcFuryToggle = null;
        private string previousValidationError = null;

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
            targetContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("clothingMesh"), "Clothing Mesh"));
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
            root.Add(appModeCard);
            
            // UDIM Discard Settings Card (conditional)
            var udimCard = YUCPUIToolkitHelper.CreateCard("UDIM Discard Settings", "Configure UDIM tile coordinates");
            var udimContent = YUCPUIToolkitHelper.GetCardContent(udimCard);
            udimContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("udimUVChannel"), "UV Channel"));
            
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
            udimContent.Add(rowColumnContainer);
            udimCard.name = "udim-card";
            root.Add(udimCard);
            
            var toggleSection = new VisualElement();
            toggleSection.name = "toggle-section";
            root.Add(toggleSection);
            
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
            root.Add(YUCPUIToolkitHelper.CreateDivider());
            YUCPUIToolkitHelper.AddSpacing(root, 10);
            
            var previewHeader = new Label("PREVIEW TOOLS");
            previewHeader.style.fontSize = 14;
            previewHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            previewHeader.style.color = new StyleColor(new Color(0.212f, 0.749f, 0.694f));
            previewHeader.style.marginBottom = 10;
            root.Add(previewHeader);
            
            var previewInfo = new VisualElement();
            previewInfo.name = "preview-info";
            root.Add(previewInfo);
            
            var previewButtons = new VisualElement();
            previewButtons.style.flexDirection = FlexDirection.Row;
            previewButtons.style.marginBottom = 10;
            
            var generateButton = YUCPUIToolkitHelper.CreateButton("Generate Preview", () => GeneratePreview(), YUCPUIToolkitHelper.ButtonVariant.Primary);
            generateButton.style.height = 40;
            generateButton.style.minWidth = 150;
            generateButton.style.flexGrow = 0;
            generateButton.name = "generate-button";
            previewButtons.Add(generateButton);
            
            var clearButton = YUCPUIToolkitHelper.CreateButton("Clear Preview", () => ClearPreview(), YUCPUIToolkitHelper.ButtonVariant.Danger);
            clearButton.style.height = 40;
            clearButton.style.minWidth = 100;
            clearButton.style.flexGrow = 0;
            clearButton.name = "clear-button";
            previewButtons.Add(clearButton);
            
            root.Add(previewButtons);
            
            var clearCacheButton = YUCPUIToolkitHelper.CreateButton("Clear Detection Cache", () =>
            {
                DetectionCache.ClearCache();
                EditorUtility.DisplayDialog("Cache Cleared", 
                    "Detection cache has been cleared.\n\nNext build will re-run detection for all Auto Body Hider components.", 
                    "OK");
            }, YUCPUIToolkitHelper.ButtonVariant.Secondary);
            clearCacheButton.style.height = 30;
            clearCacheButton.style.backgroundColor = new StyleColor(new Color(1f, 0.7f, 0.3f));
            root.Add(clearCacheButton);
            
            YUCPUIToolkitHelper.AddSpacing(root, 10);
            
            var validationError = new VisualElement();
            validationError.name = "validation-error";
            root.Add(validationError);
            
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
                
                UpdateToggleSection(toggleSection, data, serializedObject, vrcFuryToggle);
                
                UpdatePreviewInfo(previewInfo, data);
                
                generateButton.SetEnabled(!isGeneratingPreview && ValidateData());
                generateButton.text = isGeneratingPreview ? "Generating..." : "Generate Preview";
                clearButton.SetEnabled(data.previewGenerated);
                
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
            
            if (appModeValue != ApplicationMode.UDIMDiscard && appModeValue != ApplicationMode.AutoDetect)
                return;
            
            YUCPUIToolkitHelper.AddSpacing(container, 5);
            var createToggleProp = so.FindProperty("createToggle");
            var uvDiscardToggle = data.GetComponent<UVDiscardToggleData>();
            var hasVRCFuryToggle = vrcFuryToggle != null;
            
            if (uvDiscardToggle != null || hasVRCFuryToggle)
            {
                var disabledField = YUCPUIToolkitHelper.CreateField(createToggleProp, $"Create Toggle (Disabled - {(uvDiscardToggle != null ? "UV Discard Toggle present" : "VRCFury Toggle present")})");
                disabledField.SetEnabled(false);
                container.Add(disabledField);
            }
            else
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
            
            if (createToggleProp.boolValue && uvDiscardToggle == null && !hasVRCFuryToggle)
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
                
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    $"Preview Generated: {hiddenFaces} / {totalFaces} faces will be deleted\n" +
                    $"({(hiddenFaces * 100f / totalFaces):F1}%)\n\n" +
                    $"VRChat Performance: -{hiddenFaces} tris",
                    YUCPUIToolkitHelper.MessageType.Info));
            }
            else
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "No preview generated. Click 'Generate Preview' to visualize what will be deleted.\n" +
                    "Red faces in Scene view = faces that will be deleted at build time.",
                    YUCPUIToolkitHelper.MessageType.None));
            }
        }

        public override void OnInspectorGUI()
        {
            // Legacy support - not used anymore
        }

        
        private void CreateVRCFuryToggleComponent()
        {
            var toggle = com.vrcfury.api.FuryComponents.CreateToggle(data.gameObject);
            
            toggle.SetMenuPath("Clothing/Hide Body");
            toggle.SetSaved();
            
            // Don't set any actions - the user can add blend shapes, animations, etc. via the VRCFury inspector
            // The Auto Body Hider processor will automatically add the UDIM discard animation during build
            
            EditorUtility.SetDirty(data.gameObject);
            
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
