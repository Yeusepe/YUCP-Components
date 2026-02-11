using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(PrettyHierarchyData))]
    [CanEditMultipleObjects]
    public class PrettyHierarchyDataEditor : UnityEditor.Editor
    {
        private SerializedProperty presetProp;
        private SerializedProperty useDefaultBackgroundColorProp, backgroundColorProp, backgroundAlphaProp;
        private SerializedProperty useBackgroundGradientProp, backgroundGradientProp, gradientAngleProp;
        private SerializedProperty showShadowProp, shadowColorProp, shadowOffsetProp, shadowBlurProp;
        private SerializedProperty useCustomRowHeightProp, customRowHeightProp;
        private SerializedProperty marginLeftProp, marginRightProp, marginTopProp, marginBottomProp;
        private SerializedProperty iconMarginLeftProp, iconMarginRightProp, iconMarginTopProp, iconMarginBottomProp;
        private SerializedProperty textMarginLeftProp, textMarginRightProp, textMarginTopProp, textMarginBottomProp;
        private SerializedProperty showIconProp, useCustomIconProp, customIconProp, customIconBuiltInNameProp;
        private SerializedProperty showCollapseIconProp, showExpandCollapseFolderIconProp, closedFolderIconNameProp, openFolderIconNameProp, folderIconOffsetXProp, folderIconOffsetYProp, showPrefabIconProp, showEditPrefabIconProp, iconSizeProp;
        private SerializedProperty cornerRadiusUniformProp, cornerRadiusProp;
        private SerializedProperty cornerRadiusTopLeftProp, cornerRadiusTopRightProp, cornerRadiusBottomRightProp, cornerRadiusBottomLeftProp;
        private SerializedProperty useDefaultTextColorProp, textColorProp, fontProp, fontSizeProp, fontStyleProp, alignmentProp, textDropShadowProp;
        private SerializedProperty paddingLeftProp, paddingRightProp;
        private SerializedProperty borderWidthProp, borderColorProp;

        private void OnEnable()
        {
            presetProp = serializedObject.FindProperty("preset");
            useDefaultBackgroundColorProp = serializedObject.FindProperty("useDefaultBackgroundColor");
            backgroundColorProp = serializedObject.FindProperty("backgroundColor");
            backgroundAlphaProp = serializedObject.FindProperty("backgroundAlpha");
            useBackgroundGradientProp = serializedObject.FindProperty("useBackgroundGradient");
            backgroundGradientProp = serializedObject.FindProperty("backgroundGradient");
            gradientAngleProp = serializedObject.FindProperty("gradientAngle");
            showShadowProp = serializedObject.FindProperty("showShadow");
            shadowColorProp = serializedObject.FindProperty("shadowColor");
            shadowOffsetProp = serializedObject.FindProperty("shadowOffset");
            shadowBlurProp = serializedObject.FindProperty("shadowBlur");
            useCustomRowHeightProp = serializedObject.FindProperty("useCustomRowHeight");
            customRowHeightProp = serializedObject.FindProperty("customRowHeight");
            marginLeftProp = serializedObject.FindProperty("marginLeft");
            marginRightProp = serializedObject.FindProperty("marginRight");
            marginTopProp = serializedObject.FindProperty("marginTop");
            marginBottomProp = serializedObject.FindProperty("marginBottom");
            iconMarginLeftProp = serializedObject.FindProperty("iconMarginLeft");
            iconMarginRightProp = serializedObject.FindProperty("iconMarginRight");
            iconMarginTopProp = serializedObject.FindProperty("iconMarginTop");
            iconMarginBottomProp = serializedObject.FindProperty("iconMarginBottom");
            textMarginLeftProp = serializedObject.FindProperty("textMarginLeft");
            textMarginRightProp = serializedObject.FindProperty("textMarginRight");
            textMarginTopProp = serializedObject.FindProperty("textMarginTop");
            textMarginBottomProp = serializedObject.FindProperty("textMarginBottom");
            showIconProp = serializedObject.FindProperty("showIcon");
            useCustomIconProp = serializedObject.FindProperty("useCustomIcon");
            customIconProp = serializedObject.FindProperty("customIcon");
            customIconBuiltInNameProp = serializedObject.FindProperty("customIconBuiltInName");
            showCollapseIconProp = serializedObject.FindProperty("showCollapseIcon");
            showExpandCollapseFolderIconProp = serializedObject.FindProperty("showExpandCollapseFolderIcon");
            closedFolderIconNameProp = serializedObject.FindProperty("closedFolderIconName");
            openFolderIconNameProp = serializedObject.FindProperty("openFolderIconName");
            folderIconOffsetXProp = serializedObject.FindProperty("folderIconOffsetX");
            folderIconOffsetYProp = serializedObject.FindProperty("folderIconOffsetY");
            showPrefabIconProp = serializedObject.FindProperty("showPrefabIcon");
            showEditPrefabIconProp = serializedObject.FindProperty("showEditPrefabIcon");
            iconSizeProp = serializedObject.FindProperty("iconSize");
            cornerRadiusUniformProp = serializedObject.FindProperty("cornerRadiusUniform");
            cornerRadiusProp = serializedObject.FindProperty("cornerRadius");
            cornerRadiusTopLeftProp = serializedObject.FindProperty("cornerRadiusTopLeft");
            cornerRadiusTopRightProp = serializedObject.FindProperty("cornerRadiusTopRight");
            cornerRadiusBottomRightProp = serializedObject.FindProperty("cornerRadiusBottomRight");
            cornerRadiusBottomLeftProp = serializedObject.FindProperty("cornerRadiusBottomLeft");
            useDefaultTextColorProp = serializedObject.FindProperty("useDefaultTextColor");
            textColorProp = serializedObject.FindProperty("textColor");
            fontProp = serializedObject.FindProperty("font");
            fontSizeProp = serializedObject.FindProperty("fontSize");
            fontStyleProp = serializedObject.FindProperty("fontStyle");
            alignmentProp = serializedObject.FindProperty("alignment");
            textDropShadowProp = serializedObject.FindProperty("textDropShadow");
            paddingLeftProp = serializedObject.FindProperty("paddingLeft");
            paddingRightProp = serializedObject.FindProperty("paddingRight");
            borderWidthProp = serializedObject.FindProperty("borderWidth");
            borderColorProp = serializedObject.FindProperty("borderColor");
        }

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);

            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Pretty Hierarchy"));
            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(PrettyHierarchyData));
            if (supportBanner != null) root.Add(supportBanner);

            // Preset
            var presetCard = YUCPUIToolkitHelper.CreateCard("Preset", "Quick style presets.");
            var presetContent = YUCPUIToolkitHelper.GetCardContent(presetCard);
            var presetField = YUCPUIToolkitHelper.CreateField(presetProp, "Preset Style");
            presetField.RegisterValueChangeCallback(_ => ApplyPreset());
            presetContent.Add(presetField);
            root.Add(presetCard);

            YUCPUIToolkitHelper.AddSpacing(root, 6);

            // Layout
            var layoutCard = YUCPUIToolkitHelper.CreateCard("Layout", "Margins and row size.");
            var layoutContent = YUCPUIToolkitHelper.GetCardContent(layoutCard);
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(useCustomRowHeightProp, "Custom Row Height"));
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(customRowHeightProp, "Row Height (px)"));
            layoutContent.Add(YUCPUIToolkitHelper.CreateHelpBox("When enabled, this row uses the set height and other hierarchy rows shift accordingly.", YUCPUIToolkitHelper.MessageType.None));
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(marginLeftProp, "Row Margin Left"));
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(marginRightProp, "Row Margin Right"));
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(marginTopProp, "Row Margin Top"));
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(marginBottomProp, "Row Margin Bottom"));
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(iconMarginLeftProp, "Icon Margin Left"));
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(iconMarginRightProp, "Icon Margin Right"));
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(iconMarginTopProp, "Icon Margin Top"));
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(iconMarginBottomProp, "Icon Margin Bottom"));
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(textMarginLeftProp, "Text Margin Left"));
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(textMarginRightProp, "Text Margin Right"));
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(textMarginTopProp, "Text Margin Top"));
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(textMarginBottomProp, "Text Margin Bottom"));
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(paddingLeftProp, "Text Padding Left"));
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(paddingRightProp, "Text Padding Right"));
            layoutContent.Add(YUCPUIToolkitHelper.CreateField(iconSizeProp, "Icon Size"));
            root.Add(layoutCard);

            YUCPUIToolkitHelper.AddSpacing(root, 6);

            // Background
            var backgroundCard = YUCPUIToolkitHelper.CreateCard("Background", "Solid background and gradient.");
            var backgroundContent = YUCPUIToolkitHelper.GetCardContent(backgroundCard);
            backgroundContent.Add(YUCPUIToolkitHelper.CreateField(useDefaultBackgroundColorProp, "Use Default Color"));
            backgroundContent.Add(YUCPUIToolkitHelper.CreateField(backgroundColorProp, "Background Color"));
            backgroundContent.Add(YUCPUIToolkitHelper.CreateField(backgroundAlphaProp, "Background Alpha"));
            backgroundContent.Add(YUCPUIToolkitHelper.CreateField(useBackgroundGradientProp, "Use Gradient"));
            backgroundContent.Add(YUCPUIToolkitHelper.CreateField(backgroundGradientProp, "Gradient"));
            backgroundContent.Add(YUCPUIToolkitHelper.CreateField(gradientAngleProp, "Gradient Angle"));
            root.Add(backgroundCard);

            YUCPUIToolkitHelper.AddSpacing(root, 6);

            // Corners
            var cornersCard = YUCPUIToolkitHelper.CreateCard("Corners", "Rounded corner radius.");
            var cornersContent = YUCPUIToolkitHelper.GetCardContent(cornersCard);
            var uniformField = YUCPUIToolkitHelper.CreateField(cornerRadiusUniformProp, "Uniform Radius");
            cornersContent.Add(uniformField);
            var uniformContainer = new VisualElement();
            uniformContainer.Add(YUCPUIToolkitHelper.CreateField(cornerRadiusProp, "Corner Radius"));
            var perCornerContainer = new VisualElement();
            perCornerContainer.Add(YUCPUIToolkitHelper.CreateField(cornerRadiusTopLeftProp, "Top Left"));
            perCornerContainer.Add(YUCPUIToolkitHelper.CreateField(cornerRadiusTopRightProp, "Top Right"));
            perCornerContainer.Add(YUCPUIToolkitHelper.CreateField(cornerRadiusBottomRightProp, "Bottom Right"));
            perCornerContainer.Add(YUCPUIToolkitHelper.CreateField(cornerRadiusBottomLeftProp, "Bottom Left"));
            bool isUniform = cornerRadiusUniformProp.boolValue;
            uniformContainer.style.display = isUniform ? DisplayStyle.Flex : DisplayStyle.None;
            perCornerContainer.style.display = !isUniform ? DisplayStyle.Flex : DisplayStyle.None;
            cornersContent.Add(uniformContainer);
            cornersContent.Add(perCornerContainer);
            uniformField.RegisterValueChangeCallback(_ =>
            {
                serializedObject.Update();
                bool u = cornerRadiusUniformProp.boolValue;
                uniformContainer.style.display = u ? DisplayStyle.Flex : DisplayStyle.None;
                perCornerContainer.style.display = !u ? DisplayStyle.Flex : DisplayStyle.None;
            });
            root.Add(cornersCard);

            YUCPUIToolkitHelper.AddSpacing(root, 6);

            // Border
            var borderCard = YUCPUIToolkitHelper.CreateCard("Border", "Outline around the row.");
            var borderContent = YUCPUIToolkitHelper.GetCardContent(borderCard);
            borderContent.Add(YUCPUIToolkitHelper.CreateField(borderWidthProp, "Width"));
            borderContent.Add(YUCPUIToolkitHelper.CreateField(borderColorProp, "Color"));
            root.Add(borderCard);

            YUCPUIToolkitHelper.AddSpacing(root, 6);

            // Shadow
            var shadowCard = YUCPUIToolkitHelper.CreateCard("Shadow", "Drop shadow effect.");
            var shadowContent = YUCPUIToolkitHelper.GetCardContent(shadowCard);
            shadowContent.Add(YUCPUIToolkitHelper.CreateField(showShadowProp, "Enable Shadow"));
            shadowContent.Add(YUCPUIToolkitHelper.CreateField(shadowColorProp, "Color"));
            shadowContent.Add(YUCPUIToolkitHelper.CreateField(shadowOffsetProp, "Offset"));
            shadowContent.Add(YUCPUIToolkitHelper.CreateField(shadowBlurProp, "Blur"));
            root.Add(shadowCard);

            YUCPUIToolkitHelper.AddSpacing(root, 6);

            // Icons
            var iconsCard = YUCPUIToolkitHelper.CreateCard("Icons", "Icon visibility and custom icon.");
            var iconsContent = YUCPUIToolkitHelper.GetCardContent(iconsCard);
            iconsContent.Add(YUCPUIToolkitHelper.CreateField(showIconProp, "Show Icon"));
            iconsContent.Add(YUCPUIToolkitHelper.CreateField(useCustomIconProp, "Use Custom Icon"));
            iconsContent.Add(YUCPUIToolkitHelper.CreateField(customIconProp, "Custom Texture"));
            iconsContent.Add(YUCPUIToolkitHelper.CreateField(customIconBuiltInNameProp, "Built-in Icon Name"));
            iconsContent.Add(YUCPUIToolkitHelper.CreateField(showCollapseIconProp, "Show Collapse Icon"));
            iconsContent.Add(YUCPUIToolkitHelper.CreateField(showExpandCollapseFolderIconProp, "Show Expand/Collapse Folder Icon"));
            iconsContent.Add(YUCPUIToolkitHelper.CreateField(closedFolderIconNameProp, "Closed Folder Icon (Built-in Name)"));
            iconsContent.Add(YUCPUIToolkitHelper.CreateField(openFolderIconNameProp, "Open Folder Icon (Built-in Name)"));
            iconsContent.Add(YUCPUIToolkitHelper.CreateField(folderIconOffsetXProp, "Folder Icon Offset X"));
            iconsContent.Add(YUCPUIToolkitHelper.CreateField(folderIconOffsetYProp, "Folder Icon Offset Y"));
            iconsContent.Add(YUCPUIToolkitHelper.CreateField(showPrefabIconProp, "Show Prefab Icon"));
            iconsContent.Add(YUCPUIToolkitHelper.CreateField(showEditPrefabIconProp, "Show Edit Prefab Icon"));
            root.Add(iconsCard);

            YUCPUIToolkitHelper.AddSpacing(root, 6);

            // Text
            var textCard = YUCPUIToolkitHelper.CreateCard("Text", "Label style and alignment.");
            var textContent = YUCPUIToolkitHelper.GetCardContent(textCard);
            textContent.Add(YUCPUIToolkitHelper.CreateField(useDefaultTextColorProp, "Use Default Color"));
            textContent.Add(YUCPUIToolkitHelper.CreateField(textColorProp, "Text Color"));
            textContent.Add(YUCPUIToolkitHelper.CreateField(fontProp, "Font"));
            textContent.Add(YUCPUIToolkitHelper.CreateField(fontSizeProp, "Font Size"));
            textContent.Add(YUCPUIToolkitHelper.CreateField(fontStyleProp, "Font Style"));
            textContent.Add(YUCPUIToolkitHelper.CreateField(alignmentProp, "Alignment"));
            textContent.Add(YUCPUIToolkitHelper.CreateField(textDropShadowProp, "Text Drop Shadow"));
            root.Add(textCard);

            YUCPUIToolkitHelper.AddSpacing(root, 6);

            var refreshBtn = YUCPUIToolkitHelper.CreateButton("Refresh Hierarchy View", () => EditorApplication.RepaintHierarchyWindow(), YUCPUIToolkitHelper.ButtonVariant.Secondary);
            root.Add(refreshBtn);

            return root;
        }

        private void ApplyPreset()
        {
            // Preset application can be implemented to apply colors/settings from the enum.
        }
    }
}
