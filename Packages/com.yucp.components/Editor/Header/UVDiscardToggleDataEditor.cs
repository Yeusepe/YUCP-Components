using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using YUCP.Components.Editor.UI;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(UVDiscardToggleData))]
    public class UVDiscardToggleDataEditor : UnityEditor.Editor
    {
        private bool showAdvancedToggle = false;
        private bool showDebug = false;
        
        // Track previous values to reduce unnecessary UI updates
        private string previousMenuPath = null;
        private string previousGlobalParameter = null;
        private SkinnedMeshRenderer previousTargetBodyMesh = null;
        private SkinnedMeshRenderer previousClothingMesh = null;

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var data = (UVDiscardToggleData)target;

            var root = new VisualElement();
            
            // Load design system stylesheets
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("UV Discard Toggle"));

            var autoBodyHider = data.clothingMesh != null ? data.clothingMesh.GetComponent<AutoBodyHiderData>() : null;
            if (autoBodyHider != null)
            {
                root.Add(YUCPUIToolkitHelper.CreateHelpBox(
                    "Auto Body Hider Integration Detected\n\n" +
                    "This UV Discard Toggle will work together with the AutoBodyHider component on the clothing mesh. " +
                    "Both will use the same UDIM tile for coordinated body hiding and clothing toggling.",
                    YUCPUIToolkitHelper.MessageType.Info));
            }

            var targetMeshesCard = YUCPUIToolkitHelper.CreateCard("Target Meshes", "Configure the meshes for UV discard");
            var targetMeshesContent = YUCPUIToolkitHelper.GetCardContent(targetMeshesCard);
            targetMeshesContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("targetBodyMesh"), "Body Mesh"));
            targetMeshesContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("clothingMesh"), "Clothing Mesh"));
            root.Add(targetMeshesCard);

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
            udimContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Avoid row 0 (especially 0,0) as it overlaps with the main texture. Row 3, Column 3 is safest.", YUCPUIToolkitHelper.MessageType.None));
            root.Add(udimCard);

            var toggleCard = YUCPUIToolkitHelper.CreateCard("Toggle Settings", "Configure menu and parameter settings");
            var toggleContent = YUCPUIToolkitHelper.GetCardContent(toggleCard);
            toggleContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("menuPath"), "Menu Path"));
            toggleContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("globalParameter"), "Global Parameter (Optional)"));

            var menuPathProp = serializedObject.FindProperty("menuPath");
            var globalParameterProp = serializedObject.FindProperty("globalParameter");
            var dynamicHelpBoxContainer = new VisualElement();
            toggleContent.Add(dynamicHelpBoxContainer);
            
            // Initialize previous values
            previousMenuPath = menuPathProp.stringValue;
            previousGlobalParameter = globalParameterProp.stringValue;
            UpdateDynamicHelpBox(dynamicHelpBoxContainer, menuPathProp, globalParameterProp);
            
            root.schedule.Execute(() =>
            {
                string currentMenuPath = menuPathProp.stringValue;
                string currentGlobalParameter = globalParameterProp.stringValue;
                
                if (currentMenuPath != previousMenuPath || currentGlobalParameter != previousGlobalParameter)
                {
                    UpdateDynamicHelpBox(dynamicHelpBoxContainer, menuPathProp, globalParameterProp);
                    previousMenuPath = currentMenuPath;
                    previousGlobalParameter = currentGlobalParameter;
                }
            }).Every(100);

            var savedDefaultContainer = new VisualElement();
            savedDefaultContainer.style.flexDirection = FlexDirection.Row;
            savedDefaultContainer.style.marginBottom = 5;
            
            var savedField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("saved"), "Saved");
            savedField.style.flexGrow = 1;
            savedField.style.marginRight = 5;
            savedDefaultContainer.Add(savedField);
            
            var defaultOnField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("defaultOn"), "Default ON");
            defaultOnField.style.flexGrow = 1;
            savedDefaultContainer.Add(defaultOnField);
            toggleContent.Add(savedDefaultContainer);
            root.Add(toggleCard);

            var advancedFoldout = YUCPUIToolkitHelper.CreateFoldout("Advanced Toggle Options", showAdvancedToggle);
            advancedFoldout.RegisterValueChangedCallback(evt => { showAdvancedToggle = evt.newValue; });
            root.Add(advancedFoldout);

            var advancedContent = new VisualElement();
            advancedContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("slider"), "Use Slider"));
            advancedContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("holdButton"), "Hold Button"));
            advancedContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("securityEnabled"), "Security Enabled"));
            
            YUCPUIToolkitHelper.AddSpacing(advancedContent, 3);
            
            var enableExclusiveTagProp = serializedObject.FindProperty("enableExclusiveTag");
            var exclusiveTagField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("exclusiveTag"), "Tag Name");
            var exclusiveTagContainer = new VisualElement();
            exclusiveTagContainer.style.paddingLeft = 15;
            exclusiveTagContainer.Add(exclusiveTagField);
            
            advancedContent.Add(YUCPUIToolkitHelper.CreateField(enableExclusiveTagProp, "Exclusive Tags"));
            advancedContent.Add(exclusiveTagContainer);
            
            root.schedule.Execute(() =>
            {
                exclusiveTagField.style.display = enableExclusiveTagProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
            }).Every(100);
            
            YUCPUIToolkitHelper.AddSpacing(advancedContent, 3);
            
            var enableIconProp = serializedObject.FindProperty("enableIcon");
            var iconField = YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("icon"), "Icon Texture");
            var iconContainer = new VisualElement();
            iconContainer.style.paddingLeft = 15;
            iconContainer.Add(iconField);
            
            advancedContent.Add(YUCPUIToolkitHelper.CreateField(enableIconProp, "Custom Icon"));
            advancedContent.Add(iconContainer);
            
            root.schedule.Execute(() =>
            {
                iconField.style.display = enableIconProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
            }).Every(100);
            
            advancedFoldout.Add(advancedContent);

            var debugFoldout = YUCPUIToolkitHelper.CreateFoldout("Debug Options", showDebug);
            debugFoldout.RegisterValueChangedCallback(evt => { showDebug = evt.newValue; });
            debugFoldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("debugSaveAnimation"), "Save Animation to Assets"));
            root.Add(debugFoldout);

            YUCPUIToolkitHelper.AddSpacing(root, 10);

            var validationContainer = new VisualElement();
            validationContainer.name = "validation-container";
            root.Add(validationContainer);

            // Initialize previous values
            previousTargetBodyMesh = data.targetBodyMesh;
            previousClothingMesh = data.clothingMesh;
            UpdateValidationContainer(validationContainer, data);

            root.schedule.Execute(() =>
            {
                if (data.targetBodyMesh != previousTargetBodyMesh || data.clothingMesh != previousClothingMesh ||
                    data.menuPath != previousMenuPath || data.globalParameter != previousGlobalParameter)
                {
                    UpdateValidationContainer(validationContainer, data);
                    previousTargetBodyMesh = data.targetBodyMesh;
                    previousClothingMesh = data.clothingMesh;
                    previousMenuPath = data.menuPath;
                    previousGlobalParameter = data.globalParameter;
                }
            }).Every(100);

            root.schedule.Execute(() => serializedObject.ApplyModifiedProperties()).Every(100);

            return root;
        }
        
        private void UpdateDynamicHelpBox(VisualElement container, SerializedProperty menuPathProp, SerializedProperty globalParameterProp)
        {
            container.Clear();
            bool hasMenuPath = !string.IsNullOrEmpty(menuPathProp.stringValue);
            bool hasGlobalParam = !string.IsNullOrEmpty(globalParameterProp.stringValue);

            VisualElement helpBox = null;
            if (!hasMenuPath && hasGlobalParam)
            {
                helpBox = YUCPUIToolkitHelper.CreateHelpBox(
                    $"Global Parameter Only: Controlled by '{globalParameterProp.stringValue}' (no menu item).",
                    YUCPUIToolkitHelper.MessageType.Info);
            }
            else if (hasMenuPath && hasGlobalParam)
            {
                helpBox = YUCPUIToolkitHelper.CreateHelpBox(
                    $"Synced Toggle: Menu controls '{globalParameterProp.stringValue}' (synced across players).",
                    YUCPUIToolkitHelper.MessageType.Info);
            }
            else if (hasMenuPath && !hasGlobalParam)
            {
                helpBox = YUCPUIToolkitHelper.CreateHelpBox(
                    "Local Toggle: VRCFury auto-generates local parameter (not synced).",
                    YUCPUIToolkitHelper.MessageType.Info);
            }
            else
            {
                helpBox = YUCPUIToolkitHelper.CreateHelpBox(
                    "Menu path or global parameter is required!",
                    YUCPUIToolkitHelper.MessageType.Warning);
            }
            
            if (helpBox != null)
            {
                container.Add(helpBox);
            }
        }
        
        private void UpdateValidationContainer(VisualElement container, UVDiscardToggleData data)
        {
            container.Clear();

            if (data.targetBodyMesh == null)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Target Body Mesh is required", YUCPUIToolkitHelper.MessageType.Error));
            }
            else if (data.clothingMesh == null)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Clothing Mesh is required", YUCPUIToolkitHelper.MessageType.Error));
            }
            else if (data.targetBodyMesh.sharedMesh == null)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Target Body Mesh has no mesh data", YUCPUIToolkitHelper.MessageType.Error));
            }
            else if (data.clothingMesh.sharedMesh == null)
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Clothing Mesh has no mesh data", YUCPUIToolkitHelper.MessageType.Error));
            }
            else if (string.IsNullOrEmpty(data.menuPath) && string.IsNullOrEmpty(data.globalParameter))
            {
                container.Add(YUCPUIToolkitHelper.CreateHelpBox("Either Menu Path or Global Parameter must be set", YUCPUIToolkitHelper.MessageType.Error));
            }
            else if (data.targetBodyMesh != null && data.targetBodyMesh.sharedMaterials != null)
            {
                bool hasPoiyomi = false;
                foreach (var mat in data.targetBodyMesh.sharedMaterials)
                {
                    if (UDIMManipulator.IsPoiyomiWithUDIMSupport(mat))
                    {
                        hasPoiyomi = true;
                        break;
                    }
                }
                if (!hasPoiyomi)
                {
                    container.Add(YUCPUIToolkitHelper.CreateHelpBox("Body mesh needs a Poiyomi or FastFur material with UDIM support", YUCPUIToolkitHelper.MessageType.Warning));
                }
            }
        }
    }
}

