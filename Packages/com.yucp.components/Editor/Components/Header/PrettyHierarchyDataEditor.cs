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
        private SerializedProperty presetProp, useDefaultBackgroundColorProp, backgroundColorProp, backgroundAlphaProp;
        private SerializedProperty useBackgroundGradientProp, backgroundGradientProp, gradientAngleProp;
        private SerializedProperty showShadowProp, shadowColorProp, shadowOffsetProp, shadowBlurProp;
        private SerializedProperty useCustomRowHeightProp, customRowHeightProp;
        private SerializedProperty marginLeftProp, marginRightProp, marginTopProp, marginBottomProp;
        private SerializedProperty iconMarginLeftProp, iconMarginRightProp, iconMarginTopProp, iconMarginBottomProp;
        private SerializedProperty textMarginLeftProp, textMarginRightProp, textMarginTopProp, textMarginBottomProp;
        private SerializedProperty showIconProp, useCustomIconProp, useCustomIconAsTextureProp, customIconProp, customIconBuiltInNameProp;
        private SerializedProperty showCollapseIconProp, showExpandCollapseFolderIconProp, closedFolderIconNameProp, openFolderIconNameProp, closedFolderCustomIconProp, openFolderCustomIconProp, folderIconOffsetXProp, folderIconOffsetYProp, showPrefabIconProp, showEditPrefabIconProp, iconSizeProp;
        private SerializedProperty cornerRadiusUniformProp, cornerRadiusProp;
        private SerializedProperty cornerRadiusTopLeftProp, cornerRadiusTopRightProp, cornerRadiusBottomRightProp, cornerRadiusBottomLeftProp;
        private SerializedProperty useDefaultTextColorProp, textColorProp, useTextGradientProp, textGradientProp, textGradientAngleProp, fontProp, fontSizeProp, fontStyleProp, alignmentProp, textDropShadowProp;
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
            useCustomIconAsTextureProp = serializedObject.FindProperty("useCustomIconAsTexture");
            customIconProp = serializedObject.FindProperty("customIcon");
            customIconBuiltInNameProp = serializedObject.FindProperty("customIconBuiltInName");
            showCollapseIconProp = serializedObject.FindProperty("showCollapseIcon");
            showExpandCollapseFolderIconProp = serializedObject.FindProperty("showExpandCollapseFolderIcon");
            closedFolderIconNameProp = serializedObject.FindProperty("closedFolderIconName");
            openFolderIconNameProp = serializedObject.FindProperty("openFolderIconName");
            closedFolderCustomIconProp = serializedObject.FindProperty("closedFolderCustomIcon");
            openFolderCustomIconProp = serializedObject.FindProperty("openFolderCustomIcon");
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
            useTextGradientProp = serializedObject.FindProperty("useTextGradient");
            textGradientProp = serializedObject.FindProperty("textGradient");
            textGradientAngleProp = serializedObject.FindProperty("textGradientAngle");
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
            
            // Standard YUCP Styles instead of hardcoded background
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);

            root.TrackSerializedObjectValue(serializedObject, _ => EditorApplication.RepaintHierarchyWindow());

            // --- YUCP HEADER & BANNER ---
            root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Pretty Hierarchy"));
            var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(PrettyHierarchyData));
            if (supportBanner != null) root.Add(supportBanner);

            // --- PROFILE CARD ---
            var profileFoldout = new Foldout { value = true, text = "Style Profile" };
            var profileCard = YUCPUIToolkitHelper.CreateCard("Style Profile", "Load built-in styles or configure your setup.");
            var profileContent = YUCPUIToolkitHelper.GetCardContent(profileCard);
            
            var profileRow = CustomUI.CreateRow();
            profileRow.Add(CustomUI.CreateLabel("Preset", 60));
            profileRow.Add(CustomUI.CreateNativeField(presetProp, true));
            profileContent.Add(profileRow);
            root.TrackPropertyValue(presetProp, _ => ApplyPresetToData());
            
            var saveBtn = YUCPUIToolkitHelper.CreateButton("Save as Style Prefab", SaveAsPrefab, YUCPUIToolkitHelper.ButtonVariant.Ghost);
            saveBtn.style.marginTop = 10;
            profileContent.Add(saveBtn);
            profileFoldout.Add(profileCard);
            root.Add(profileFoldout);

            YUCPUIToolkitHelper.AddSpacing(root, 6);

            // --- BACKGROUND & BORDER CARD ---
            var fillCard = YUCPUIToolkitHelper.CreateCard("Background & Border", "Define background color and borders.");
            var fillContent = YUCPUIToolkitHelper.GetCardContent(fillCard);

            var useDefaultBgRow = CustomUI.CreateRow(Justify.SpaceBetween);
            useDefaultBgRow.Add(CustomUI.CreateLabel("Use Default Hierarchy Color", 100));
            var useDefaultBgToggle = YUCPUIToolkitHelper.CreateField(useDefaultBackgroundColorProp, "");
            useDefaultBgRow.Add(useDefaultBgToggle);
            fillContent.Add(useDefaultBgRow);
            
            var gradientModeRow = CustomUI.CreateRow(Justify.SpaceBetween);
            gradientModeRow.Add(CustomUI.CreateLabel("Use Gradient", 100));
            var gradientToggle = YUCPUIToolkitHelper.CreateField(useBackgroundGradientProp, "");
            gradientModeRow.Add(gradientToggle);
            fillContent.Add(gradientModeRow);

            var solidFillRow = CustomUI.CreateRow();
            solidFillRow.Add(CustomUI.CreateLabel("Color", 60));
            solidFillRow.Add(CustomUI.CreateNativeField(backgroundColorProp, true));
            
            var gradientFillRow = CustomUI.CreateRow();
            gradientFillRow.Add(CustomUI.CreateLabel("Color", 60));
            gradientFillRow.Add(CustomUI.CreateNativeField(backgroundGradientProp, true));

            var alphaRow = CustomUI.CreateRow();
            alphaRow.Add(CustomUI.CreateLabel("Alpha", 60));
            alphaRow.Add(CustomUI.CreateCompactSlider(backgroundAlphaProp, 0f, 1f));

            fillContent.Add(solidFillRow);
            fillContent.Add(gradientFillRow);
            fillContent.Add(alphaRow);

            void UpdateFill()
            {
                bool useDefault = useDefaultBackgroundColorProp.boolValue;
                bool grad = useBackgroundGradientProp.boolValue;
                bool showColorControls = !useDefault;
                solidFillRow.style.display = (showColorControls && !grad) ? DisplayStyle.Flex : DisplayStyle.None;
                gradientFillRow.style.display = (showColorControls && grad) ? DisplayStyle.Flex : DisplayStyle.None;
                gradientModeRow.style.display = showColorControls ? DisplayStyle.Flex : DisplayStyle.None;
            }
            useDefaultBgToggle.RegisterValueChangeCallback(_ => UpdateFill());
            gradientToggle.RegisterValueChangeCallback(_ => UpdateFill());
            UpdateFill();

            fillContent.Add(YUCPUIToolkitHelper.CreateDivider());
            
            var strokeColorRow = CustomUI.CreateRow();
            strokeColorRow.Add(CustomUI.CreateLabel("Border Color", 80));
            strokeColorRow.Add(CustomUI.CreateNativeField(borderColorProp, true));
            fillContent.Add(strokeColorRow);

            var strokeWidthRow = CustomUI.CreateRow();
            strokeWidthRow.Add(CustomUI.CreateLabel("Border Width", 80));
            strokeWidthRow.Add(CustomUI.CreateCompactField(borderWidthProp));
            fillContent.Add(strokeWidthRow);

            var fillFoldout = new Foldout { value = true, text = "Background & Border" };
            fillFoldout.Add(fillCard);
            root.Add(fillFoldout);
            YUCPUIToolkitHelper.AddSpacing(root, 6);

            // --- CORNER RADIUS CARD ---
            var cornerCard = YUCPUIToolkitHelper.CreateCard("Corner Radius", "Adjust panel corner roundness.");
            var cornerContent = YUCPUIToolkitHelper.GetCardContent(cornerCard);
            var cornerBox = CustomUI.CreateCornerRadiusBox(
                cornerRadiusUniformProp, cornerRadiusProp, 
                cornerRadiusTopLeftProp, cornerRadiusTopRightProp, 
                cornerRadiusBottomLeftProp, cornerRadiusBottomRightProp);
            cornerContent.Add(cornerBox);
            var cornerFoldout = new Foldout { value = true, text = "Corner Radius" };
            cornerFoldout.Add(cornerCard);
            root.Add(cornerFoldout);
            YUCPUIToolkitHelper.AddSpacing(root, 6);

            // --- LAYOUT & PADDING CARD ---
            var layoutCard = YUCPUIToolkitHelper.CreateCard("Layout & Padding", "Configure row height, margins, and offsets.");
            var layoutContent = YUCPUIToolkitHelper.GetCardContent(layoutCard);

            var heightRow = CustomUI.CreateRow(Justify.SpaceBetween);
            heightRow.Add(CustomUI.CreateLabel("Custom Row Height"));
            var heightTgl = YUCPUIToolkitHelper.CreateField(useCustomRowHeightProp, "");
            var heightVal = CustomUI.CreateCompactField(customRowHeightProp, "H");
            var heightContainer = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            heightContainer.Add(heightVal);
            heightContainer.Add(new VisualElement { style = { width = 10 } });
            heightContainer.Add(heightTgl);
            heightRow.Add(heightContainer);
            layoutContent.Add(heightRow);
            
            void UpdateHeightDisplay() => heightVal.style.display = useCustomRowHeightProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
            heightTgl.RegisterValueChangeCallback(_ => UpdateHeightDisplay());
            UpdateHeightDisplay();

            void ApplyMarginsAndRepaint()
            {
                serializedObject.ApplyModifiedProperties();
                EditorApplication.RepaintHierarchyWindow();
            }

            var boxModelsFoldout = YUCPUIToolkitHelper.CreateFoldout("Padding & Margins", true);
            var boxModelsContainer = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, justifyContent = Justify.SpaceAround, paddingTop = 10 } };
            
            var mContainer1 = new VisualElement { style = { marginBottom = 15, alignItems = Align.Center } };
            mContainer1.Add(CustomUI.CreateLabel("Row Margins", 80, true));
            mContainer1.Add(CustomUI.CreatePaddingBoxModel(marginTopProp, marginBottomProp, marginLeftProp, marginRightProp, ApplyMarginsAndRepaint));
            boxModelsContainer.Add(mContainer1);

            var mContainer2 = new VisualElement { style = { marginBottom = 15, alignItems = Align.Center } };
            mContainer2.Add(CustomUI.CreateLabel("Text Margins", 80, true));
            mContainer2.Add(CustomUI.CreatePaddingBoxModel(textMarginTopProp, textMarginBottomProp, textMarginLeftProp, textMarginRightProp, ApplyMarginsAndRepaint));
            boxModelsContainer.Add(mContainer2);

            var mContainer3 = new VisualElement { style = { marginBottom = 15, alignItems = Align.Center } };
            mContainer3.Add(CustomUI.CreateLabel("Icon Margins", 80, true));
            mContainer3.Add(CustomUI.CreatePaddingBoxModel(iconMarginTopProp, iconMarginBottomProp, iconMarginLeftProp, iconMarginRightProp, ApplyMarginsAndRepaint));
            boxModelsContainer.Add(mContainer3);
            
            boxModelsFoldout.Add(boxModelsContainer);
            layoutContent.Add(boxModelsFoldout);

            var layoutFoldout = new Foldout { value = true, text = "Layout & Padding" };
            layoutFoldout.Add(layoutCard);
            root.Add(layoutFoldout);
            YUCPUIToolkitHelper.AddSpacing(root, 6);

            // --- TYPOGRAPHY CARD ---
            var typeCard = YUCPUIToolkitHelper.CreateCard("Typography", "Configure text font, sizing, color, and alignment.");
            var typeContent = YUCPUIToolkitHelper.GetCardContent(typeCard);
            
            typeContent.Add(CustomUI.CreateTypographyEditor(useDefaultTextColorProp, textColorProp, useTextGradientProp, textGradientProp, textGradientAngleProp, fontProp, fontSizeProp, fontStyleProp, alignmentProp));
            
            var typeFoldout = new Foldout { value = true, text = "Typography" };
            typeFoldout.Add(typeCard);
            root.Add(typeFoldout);
            YUCPUIToolkitHelper.AddSpacing(root, 6);

            // --- HIERARCHY ELEMENTS CARD ---
            var extraCard = YUCPUIToolkitHelper.CreateCard("Hierarchy Elements", "Toggle and customize elements drawn in the hierarchy.");
            var extraContent = YUCPUIToolkitHelper.GetCardContent(extraCard);
            
            var visContainer = new VisualElement();
            var visTitle = new Label("TOGGLE VISIBLE ELEMENTS") { style = { fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.6f, 0.6f, 0.6f, 1f), marginBottom = 8, marginTop = 4 } };
            visContainer.Add(visTitle);

            var visGrid = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            visGrid.Add(CustomUI.CreateListToggle("Show GameObject Icon", showIconProp));
            visGrid.Add(CustomUI.CreateListToggle("Show Prefab Icon", showPrefabIconProp));
            visGrid.Add(CustomUI.CreateListToggle("Show Edit Prefab Arrow", showEditPrefabIconProp));
            visGrid.Add(CustomUI.CreateListToggle("Show Expand/Collapse Arrow", showCollapseIconProp));
            visGrid.Add(CustomUI.CreateListToggle("Show Folder Icons", showExpandCollapseFolderIconProp));
            visContainer.Add(visGrid);
            extraContent.Add(visContainer);

            extraContent.Add(new VisualElement { style = { height=1, backgroundColor=new Color(1,1,1,0.05f), marginTop=12, marginBottom=12 } });

            extraContent.Add(CustomUI.CreateCustomIconPicker(useCustomIconProp, useCustomIconAsTextureProp, customIconProp, customIconBuiltInNameProp));
            extraContent.Add(CustomUI.CreateDropShadowEditor(showShadowProp, shadowColorProp, shadowOffsetProp, shadowBlurProp));
            extraContent.Add(CustomUI.CreateVirtualFoldersEditor(closedFolderIconNameProp, openFolderIconNameProp, closedFolderCustomIconProp, openFolderCustomIconProp, folderIconOffsetXProp, folderIconOffsetYProp));

            var extraFoldout = new Foldout { value = true, text = "Hierarchy Elements" };
            extraFoldout.Add(extraCard);
            root.Add(extraFoldout);
            YUCPUIToolkitHelper.AddSpacing(root, 10);

            return root;
        }

        private void ApplyPresetToData()
        {
            var preset = (PrettyHierarchyPreset)presetProp.enumValueIndex;
            if (preset == PrettyHierarchyPreset.Custom) return;

            serializedObject.Update();
            backgroundColorProp.colorValue = GetPresetColor(preset);
            if (PresetUsesGradient(preset))
            {
                useBackgroundGradientProp.boolValue = true;
                backgroundGradientProp.gradientValue = GetPresetGradient(preset);
            }
            else
            {
                useBackgroundGradientProp.boolValue = false;
            }
            useDefaultBackgroundColorProp.boolValue = false;
            serializedObject.ApplyModifiedProperties();
            EditorApplication.RepaintHierarchyWindow();
        }

        private static Color GetPresetColor(PrettyHierarchyPreset preset)
        {
            return preset switch
            {
                PrettyHierarchyPreset.Red => new Color(0.85f, 0.25f, 0.25f, 1f),
                PrettyHierarchyPreset.Orange => new Color(0.95f, 0.55f, 0.2f, 1f),
                PrettyHierarchyPreset.Yellow => new Color(0.95f, 0.85f, 0.25f, 1f),
                PrettyHierarchyPreset.Green => new Color(0.25f, 0.75f, 0.35f, 1f),
                PrettyHierarchyPreset.Blue => new Color(0.25f, 0.5f, 0.9f, 1f),
                PrettyHierarchyPreset.Purple => new Color(0.55f, 0.35f, 0.85f, 1f),
                PrettyHierarchyPreset.Pink => new Color(0.95f, 0.45f, 0.7f, 1f),
                PrettyHierarchyPreset.Gray => new Color(0.4f, 0.4f, 0.45f, 1f),
                PrettyHierarchyPreset.Black => new Color(0.12f, 0.12f, 0.14f, 1f),
                PrettyHierarchyPreset.White => new Color(0.9f, 0.9f, 0.92f, 1f),
                PrettyHierarchyPreset.Midnight => new Color(0.08f, 0.1f, 0.18f, 1f),
                PrettyHierarchyPreset.Sunset => new Color(0.6f, 0.25f, 0.35f, 1f),
                PrettyHierarchyPreset.Ocean => new Color(0.15f, 0.35f, 0.5f, 1f),
                PrettyHierarchyPreset.Forest => new Color(0.15f, 0.35f, 0.2f, 1f),
                _ => new Color(0.235f, 0.235f, 0.314f, 1f)
            };
        }

        private static bool PresetUsesGradient(PrettyHierarchyPreset preset)
        {
            return preset is PrettyHierarchyPreset.Sunset or PrettyHierarchyPreset.Ocean or PrettyHierarchyPreset.Forest or PrettyHierarchyPreset.Midnight;
        }

        private static Gradient GetPresetGradient(PrettyHierarchyPreset preset)
        {
            var g = new Gradient();
            switch (preset)
            {
                case PrettyHierarchyPreset.Red: g.SetKeys(new[] { new GradientColorKey(new Color(0.7f, 0.15f, 0.15f), 0f), new GradientColorKey(new Color(0.95f, 0.35f, 0.35f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }); break;
                case PrettyHierarchyPreset.Orange: g.SetKeys(new[] { new GradientColorKey(new Color(0.8f, 0.4f, 0.1f), 0f), new GradientColorKey(new Color(0.95f, 0.7f, 0.3f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }); break;
                case PrettyHierarchyPreset.Yellow: g.SetKeys(new[] { new GradientColorKey(new Color(0.7f, 0.65f, 0.1f), 0f), new GradientColorKey(new Color(0.95f, 0.9f, 0.4f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }); break;
                case PrettyHierarchyPreset.Green: g.SetKeys(new[] { new GradientColorKey(new Color(0.1f, 0.5f, 0.2f), 0f), new GradientColorKey(new Color(0.35f, 0.85f, 0.45f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }); break;
                case PrettyHierarchyPreset.Blue: g.SetKeys(new[] { new GradientColorKey(new Color(0.1f, 0.35f, 0.7f), 0f), new GradientColorKey(new Color(0.35f, 0.6f, 0.95f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }); break;
                case PrettyHierarchyPreset.Purple: g.SetKeys(new[] { new GradientColorKey(new Color(0.4f, 0.2f, 0.7f), 0f), new GradientColorKey(new Color(0.65f, 0.45f, 0.95f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }); break;
                case PrettyHierarchyPreset.Pink: g.SetKeys(new[] { new GradientColorKey(new Color(0.7f, 0.3f, 0.5f), 0f), new GradientColorKey(new Color(0.95f, 0.55f, 0.75f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }); break;
                case PrettyHierarchyPreset.Sunset: g.SetKeys(new[] { new GradientColorKey(new Color(0.5f, 0.15f, 0.25f), 0f), new GradientColorKey(new Color(0.9f, 0.4f, 0.35f), 0.5f), new GradientColorKey(new Color(0.95f, 0.7f, 0.4f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }); break;
                case PrettyHierarchyPreset.Ocean: g.SetKeys(new[] { new GradientColorKey(new Color(0.05f, 0.2f, 0.35f), 0f), new GradientColorKey(new Color(0.2f, 0.5f, 0.7f), 0.5f), new GradientColorKey(new Color(0.4f, 0.75f, 0.9f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }); break;
                case PrettyHierarchyPreset.Forest: g.SetKeys(new[] { new GradientColorKey(new Color(0.05f, 0.25f, 0.1f), 0f), new GradientColorKey(new Color(0.2f, 0.5f, 0.3f), 0.5f), new GradientColorKey(new Color(0.4f, 0.75f, 0.5f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }); break;
                case PrettyHierarchyPreset.Midnight: g.SetKeys(new[] { new GradientColorKey(new Color(0.02f, 0.05f, 0.12f), 0f), new GradientColorKey(new Color(0.15f, 0.2f, 0.35f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }); break;
                default: g.SetKeys(new[] { new GradientColorKey(new Color(0.2f, 0.2f, 0.28f), 0f), new GradientColorKey(new Color(0.3f, 0.3f, 0.38f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }); break;
            }
            return g;
        }

        private void SaveAsPrefab()
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Style", "PrettyHierarchyStyle", "prefab", "Save preset.");
            if (string.IsNullOrEmpty(path)) return;
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)) && !EditorUtility.DisplayDialog("Overwrite?", $"A file already exists at:\n{path}\n\nOverwrite?", "Overwrite", "Cancel"))
                return;
            var go = new GameObject("PrettyHierarchyStyle");
            EditorUtility.CopySerialized(target as PrettyHierarchyData, go.AddComponent<PrettyHierarchyData>());
            PrefabUtility.SaveAsPrefabAsset(go, path);
            DestroyImmediate(go);
            Debug.Log($"Saved prefab to: {path}");
        }
    }

    public static class CustomUI
    {
        public static VisualElement CreateRow(Justify justify = Justify.FlexStart)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = justify;
            row.style.marginBottom = 6;
            return row;
        }

        public static Label CreateLabel(string text, float minWidth = 0, bool bold = false)
        {
            var lbl = new Label(text);
            lbl.style.color = new Color(1f, 1f, 1f, 0.65f); // grey text
            lbl.style.fontSize = 12;
            if (bold) {
                lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                lbl.style.color = new Color(1f, 1f, 1f, 0.85f);
                lbl.style.marginBottom = 6;
            }
            if (minWidth > 0) lbl.style.minWidth = minWidth;
            return lbl;
        }

        public static VisualElement CreateEffectsPicker(string title, SerializedProperty toggleProp, System.Action<VisualElement> buildContent)
        {
            var container = new VisualElement { style = { backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f), borderTopLeftRadius=6, borderTopRightRadius=6, borderBottomLeftRadius=6, borderBottomRightRadius=6, borderTopWidth=1, borderBottomWidth=1, borderLeftWidth=1, borderRightWidth=1, borderTopColor=new Color(1,1,1,0.05f), borderBottomColor=new Color(1,1,1,0.05f), borderLeftColor=new Color(1,1,1,0.05f), borderRightColor=new Color(1,1,1,0.05f), marginBottom = 8, overflow = Overflow.Hidden } };
            
            var headerRow = CreateRow(Justify.SpaceBetween);
            headerRow.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1f);
            headerRow.style.paddingTop = 8; headerRow.style.paddingBottom = 8; headerRow.style.paddingLeft = 12; headerRow.style.paddingRight = 12; headerRow.style.marginBottom = 0;
            
            var lbl = new Label(title) { style = { fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.9f, 0.9f, 0.9f, 1f) } };
            headerRow.Add(lbl);

            if (toggleProp != null) {
                var tgl = new Toggle(); tgl.BindProperty(toggleProp); headerRow.Add(tgl);
            }
            container.Add(headerRow);

            var content = new VisualElement { style = { paddingTop=12, paddingBottom=12, paddingLeft=12, paddingRight=12 } };
            buildContent(content);
            container.Add(content);
            
            if (toggleProp != null) {
                void UpdateState() => content.style.display = toggleProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
                content.schedule.Execute(UpdateState).Every(50);
                UpdateState();
            }
            return container;
        }

        public static VisualElement CreateListToggle(string labelText, SerializedProperty prop)
        {
            var btn = new Button() { text = labelText, style = { backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f), color = new Color(0.6f, 0.6f, 0.6f, 1f), borderTopLeftRadius=6, borderTopRightRadius=6, borderBottomLeftRadius=6, borderBottomRightRadius=6, borderTopWidth=1, borderBottomWidth=1, borderLeftWidth=1, borderRightWidth=1, borderTopColor=new Color(1,1,1,0.05f), borderBottomColor=new Color(1,1,1,0.05f), borderLeftColor=new Color(1,1,1,0.05f), borderRightColor=new Color(1,1,1,0.05f), height = 28, unityFontStyleAndWeight = FontStyle.Bold, fontSize = 11, marginBottom = 4, marginLeft = 0, marginRight = 5, paddingLeft = 10, paddingRight = 10 } };
            void UpdateState() {
                bool on = prop.boolValue;
                btn.style.backgroundColor = on ? new Color(0.21f, 0.75f, 0.69f, 0.4f) : new Color(0.15f, 0.15f, 0.15f, 1f);
                btn.style.borderTopColor = on ? new Color(0.21f, 0.75f, 0.69f, 1f) : new Color(1,1,1,0.05f);
                btn.style.borderBottomColor = on ? new Color(0.21f, 0.75f, 0.69f, 1f) : new Color(1,1,1,0.05f);
                btn.style.borderLeftColor = on ? new Color(0.21f, 0.75f, 0.69f, 1f) : new Color(1,1,1,0.05f);
                btn.style.borderRightColor = on ? new Color(0.21f, 0.75f, 0.69f, 1f) : new Color(1,1,1,0.05f);
                btn.style.color = on ? Color.white : new Color(0.6f, 0.6f, 0.6f, 1f);
            }
            btn.clicked += () => {
                prop.boolValue = !prop.boolValue;
                prop.serializedObject.ApplyModifiedProperties();
                UpdateState();
            };
            btn.schedule.Execute(UpdateState).Every(100);
            UpdateState();
            return btn;
        }

        public static VisualElement CreateCustomIconPicker(SerializedProperty useProp, SerializedProperty useTextureProp, SerializedProperty texProp, SerializedProperty nameProp)
        {
            return CreateEffectsPicker("Override GameObject Icon", useProp, content => {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                
                var previewBox = new VisualElement { style = { width = 48, height = 48, backgroundColor = new Color(0.08f, 0.08f, 0.08f, 1f), borderTopWidth=1, borderBottomWidth=1, borderLeftWidth=1, borderRightWidth=1, borderTopColor=new Color(0.3f, 0.3f, 0.3f, 1f), borderBottomColor=new Color(0.3f, 0.3f, 0.3f, 1f), borderLeftColor=new Color(0.3f, 0.3f, 0.3f, 1f), borderRightColor=new Color(0.3f, 0.3f, 0.3f, 1f), borderTopLeftRadius=4, borderTopRightRadius=4, borderBottomLeftRadius=4, borderBottomRightRadius=4, marginRight = 12, alignItems = Align.Center, justifyContent = Justify.Center } };
                
                var previewImg = new Image();
                previewImg.style.width = 32; previewImg.style.height = 32; previewImg.scaleMode = ScaleMode.ScaleToFit;
                previewBox.Add(previewImg);
                
                var textureModeRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
                textureModeRow.Add(new Label("Use Custom Texture") { style = { color = new Color(1,1,1,0.6f), fontSize = 11, width = 110 } });
                var useTextureTgl = new Toggle(); useTextureTgl.BindProperty(useTextureProp); textureModeRow.Add(useTextureTgl);
                
                var fields = new VisualElement { style = { flexGrow = 1, justifyContent = Justify.Center } };
                fields.Add(textureModeRow);
                var texField = YUCPUIToolkitHelper.CreateField(texProp, "Custom Texture 2D"); texField.style.marginTop = 5;
                var nameF = YUCPUIToolkitHelper.CreateField(nameProp, "Unity Internal Icon Name"); nameF.style.marginTop = 5;
                fields.Add(texField);
                fields.Add(nameF);
                
                row.Add(previewBox);
                row.Add(fields);
                content.Add(row);
                
                void UpdateIconFieldsVisibility()
                {
                    bool useTex = useTextureProp.boolValue;
                    texField.style.display = useTex ? DisplayStyle.Flex : DisplayStyle.None;
                    nameF.style.display = useTex ? DisplayStyle.None : DisplayStyle.Flex;
                }
                useTextureTgl.RegisterValueChangedCallback(_ => UpdateIconFieldsVisibility());
                content.schedule.Execute(UpdateIconFieldsVisibility).Every(50);
                
                void UpdateImg() {
                    if (texProp.objectReferenceValue != null) previewImg.image = (Texture2D)texProp.objectReferenceValue;
                    else previewImg.image = null;
                }
                content.schedule.Execute(UpdateImg).Every(100);
            });
        }

        public static VisualElement CreateDropShadowEditor(SerializedProperty useProp, SerializedProperty colorProp, SerializedProperty offsetProp, SerializedProperty blurProp)
        {
            return CreateEffectsPicker("Text Drop Shadow", useProp, content => {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                
                var previewCont = new VisualElement { style = { width = 48, height = 48, backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f), borderTopLeftRadius=4, borderTopRightRadius=4, borderBottomLeftRadius=4, borderBottomRightRadius=4, marginRight=12, alignItems = Align.Center, justifyContent = Justify.Center, position = Position.Relative, overflow = Overflow.Hidden } };
                var shadowBox = new VisualElement { style = { width = 24, height = 24, backgroundColor = Color.black, borderTopLeftRadius=4, borderTopRightRadius=4, borderBottomLeftRadius=4, borderBottomRightRadius=4, position = Position.Absolute } };
                var mainBox = new VisualElement { style = { width = 24, height = 24, backgroundColor = new Color(0.7f, 0.95f, 0.9f, 1f), borderTopLeftRadius=4, borderTopRightRadius=4, borderBottomLeftRadius=4, borderBottomRightRadius=4, position = Position.Absolute } };
                previewCont.Add(shadowBox);
                previewCont.Add(mainBox);
                
                var fields = new VisualElement { style = { flexGrow = 1 } };
                fields.Add(YUCPUIToolkitHelper.CreateField(colorProp, "Shadow Color"));
                var oF = YUCPUIToolkitHelper.CreateField(offsetProp, "Shadow Offset (X, Y)"); oF.style.marginTop = 3; oF.style.marginBottom = 3;
                fields.Add(oF);
                fields.Add(YUCPUIToolkitHelper.CreateField(blurProp, "Shadow Blur"));
                
                row.Add(previewCont);
                row.Add(fields);
                content.Add(row);
                
                void UpdatePreview() {
                    shadowBox.style.backgroundColor = colorProp.colorValue;
                    var off = offsetProp.vector2Value;
                    shadowBox.style.left = 12 + off.x;
                    shadowBox.style.top = 12 + off.y;
                }
                content.schedule.Execute(UpdatePreview).Every(50);
            });
        }

        public static VisualElement CreateVirtualFoldersEditor(SerializedProperty closedProp, SerializedProperty openProp, SerializedProperty customClosedProp, SerializedProperty customOpenProp, SerializedProperty offX, SerializedProperty offY)
        {
            return CreateEffectsPicker("Configure Folder Icons", null, content => {
                var grid = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, justifyContent = Justify.SpaceBetween } };
                
                var col1 = new VisualElement { style = { flexBasis = new StyleLength(new Length(48, LengthUnit.Percent)), flexGrow = 1, minWidth = 180, paddingRight = 10 } };
                var r1 = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } };
                r1.Add(new Label("Closed Folder") { style = { fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.6f, 0.6f, 0.6f, 1f) } });
                col1.Add(r1);
                
                var row1 = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                var previewBox1 = new VisualElement { style = { width = 48, height = 48, backgroundColor = new Color(0.08f, 0.08f, 0.08f, 1f), borderTopWidth=1, borderBottomWidth=1, borderLeftWidth=1, borderRightWidth=1, borderTopColor=new Color(0.3f, 0.3f, 0.3f, 1f), borderBottomColor=new Color(0.3f, 0.3f, 0.3f, 1f), borderLeftColor=new Color(0.3f, 0.3f, 0.3f, 1f), borderRightColor=new Color(0.3f, 0.3f, 0.3f, 1f), borderTopLeftRadius=4, borderTopRightRadius=4, borderBottomLeftRadius=4, borderBottomRightRadius=4, marginRight = 12, alignItems = Align.Center, justifyContent = Justify.Center } };
                var previewImg1 = new Image();
                previewImg1.style.width = 32; previewImg1.style.height = 32; previewImg1.scaleMode = ScaleMode.ScaleToFit;
                previewBox1.Add(previewImg1);
                
                var fields1 = new VisualElement { style = { flexGrow = 1, justifyContent = Justify.Center } };
                fields1.Add(YUCPUIToolkitHelper.CreateField(customClosedProp, "Custom Texture 2D"));
                var of1 = YUCPUIToolkitHelper.CreateField(closedProp, "Unity Icon Name"); of1.style.marginTop = 5;
                fields1.Add(of1);
                
                row1.Add(previewBox1);
                row1.Add(fields1);
                col1.Add(row1);

                var col2 = new VisualElement { style = { flexBasis = new StyleLength(new Length(48, LengthUnit.Percent)), flexGrow = 1, minWidth = 180 } };
                var r2 = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } };
                r2.Add(new Label("Open Folder") { style = { fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.6f, 0.6f, 0.6f, 1f) } });
                col2.Add(r2);
                
                var row2 = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                var previewBox2 = new VisualElement { style = { width = 48, height = 48, backgroundColor = new Color(0.08f, 0.08f, 0.08f, 1f), borderTopWidth=1, borderBottomWidth=1, borderLeftWidth=1, borderRightWidth=1, borderTopColor=new Color(0.3f, 0.3f, 0.3f, 1f), borderBottomColor=new Color(0.3f, 0.3f, 0.3f, 1f), borderLeftColor=new Color(0.3f, 0.3f, 0.3f, 1f), borderRightColor=new Color(0.3f, 0.3f, 0.3f, 1f), borderTopLeftRadius=4, borderTopRightRadius=4, borderBottomLeftRadius=4, borderBottomRightRadius=4, marginRight = 12, alignItems = Align.Center, justifyContent = Justify.Center } };
                var previewImg2 = new Image();
                previewImg2.style.width = 32; previewImg2.style.height = 32; previewImg2.scaleMode = ScaleMode.ScaleToFit;
                previewBox2.Add(previewImg2);
                
                var fields2 = new VisualElement { style = { flexGrow = 1, justifyContent = Justify.Center } };
                fields2.Add(YUCPUIToolkitHelper.CreateField(customOpenProp, "Custom Texture 2D"));
                var of2 = YUCPUIToolkitHelper.CreateField(openProp, "Unity Icon Name"); of2.style.marginTop = 5;
                fields2.Add(of2);

                row2.Add(previewBox2);
                row2.Add(fields2);
                col2.Add(row2);

                var col3 = new VisualElement { style = { flexBasis = new StyleLength(new Length(100, LengthUnit.Percent)), flexGrow = 1, marginTop = 12 } };
                var offsetRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, backgroundColor = new Color(1,1,1,0.02f), borderTopLeftRadius=4, borderTopRightRadius=4, borderBottomLeftRadius=4, borderBottomRightRadius=4, paddingBottom=8, paddingTop=8, paddingLeft=8, paddingRight=8 } };
                offsetRow.Add(new Label("Icon Offsets") { style = { color = new Color(1,1,1,0.6f), width = 90, fontSize = 11 } });
                offsetRow.Add(CreateCompactField(offX, "X"));
                offsetRow.Add(new VisualElement { style = { width = 10 } });
                offsetRow.Add(CreateCompactField(offY, "Y"));
                col3.Add(offsetRow);

                grid.Add(col1);
                grid.Add(col2);
                grid.Add(col3);
                content.Add(grid);
                
                void UpdateImg() {
                    if (customClosedProp.objectReferenceValue != null) previewImg1.image = (Texture2D)customClosedProp.objectReferenceValue;
                    else previewImg1.image = null;
                    if (customOpenProp.objectReferenceValue != null) previewImg2.image = (Texture2D)customOpenProp.objectReferenceValue;
                    else previewImg2.image = null;
                }
                content.schedule.Execute(UpdateImg).Every(100);
            });
        }

        public static VisualElement CreateTypographyEditor(SerializedProperty useDefaultColorProp, SerializedProperty colorProp, SerializedProperty useTextGradientProp, SerializedProperty textGradientProp, SerializedProperty textGradientAngleProp, SerializedProperty fontProp, SerializedProperty sizeProp, SerializedProperty styleProp, SerializedProperty alignProp)
        {
            var container = new VisualElement { style = { backgroundColor = new Color(0,0,0,0.15f), borderTopLeftRadius=6, borderTopRightRadius=6, borderBottomLeftRadius=6, borderBottomRightRadius=6, paddingTop=12, paddingBottom=12, paddingLeft=14, paddingRight=14, borderTopWidth=1, borderBottomWidth=1, borderLeftWidth=1, borderRightWidth=1, borderTopColor=new Color(1,1,1,0.05f), borderBottomColor=new Color(1,1,1,0.05f), borderLeftColor=new Color(1,1,1,0.05f), borderRightColor=new Color(1,1,1,0.05f) } };
            
            var previewCont = new VisualElement { style = { height = 44, backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f), borderTopLeftRadius=4, borderTopRightRadius=4, borderBottomLeftRadius=4, borderBottomRightRadius=4, borderTopWidth=1, borderBottomWidth=1, borderLeftWidth=1, borderRightWidth=1, borderTopColor=new Color(1,1,1,0.05f), borderBottomColor=new Color(1,1,1,0.05f), borderLeftColor=new Color(1,1,1,0.05f), borderRightColor=new Color(1,1,1,0.05f), marginBottom=15, overflow = Overflow.Hidden, justifyContent = Justify.Center, paddingLeft=12, paddingRight=12 } };
            
            var previewText = new Label("Hierarchy Text Preview");
            previewCont.Add(previewText);
            container.Add(previewCont);
            
            // Text Color Row
            var colorRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, alignItems = Align.Center, backgroundColor = new Color(1,1,1,0.02f), borderTopLeftRadius=4, borderTopRightRadius=4, borderBottomLeftRadius=4, borderBottomRightRadius=4, paddingLeft=8, paddingRight=8, paddingTop=6, paddingBottom=6, marginBottom=6 } };
            
            var colorToggleBox = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            colorToggleBox.Add(new Label("Use System Color") { style = { color = new Color(1,1,1,0.6f), fontSize = 11, paddingRight = 8 } });
            var defaultToggle = new Toggle(); defaultToggle.BindProperty(useDefaultColorProp); colorToggleBox.Add(defaultToggle);
            colorRow.Add(colorToggleBox);
            
            var colorPickerBox = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            colorPickerBox.Add(new Label("Override") { style = { color = new Color(1,1,1,0.6f), fontSize = 11, paddingRight = 8 } });
            var cField = CreateNativeField(colorProp, true); cField.style.width = 100; colorPickerBox.Add(cField);
            colorRow.Add(colorPickerBox);
            
            container.Add(colorRow);

            var gradientRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, alignItems = Align.Center, backgroundColor = new Color(1,1,1,0.02f), borderTopLeftRadius=4, borderTopRightRadius=4, borderBottomLeftRadius=4, borderBottomRightRadius=4, paddingLeft=8, paddingRight=8, paddingTop=6, paddingBottom=6, marginBottom=6 } };
            gradientRow.Add(new Label("Use Text Gradient") { style = { color = new Color(1,1,1,0.6f), fontSize = 11 } });
            var gradientTgl = new Toggle(); gradientTgl.BindProperty(useTextGradientProp); gradientRow.Add(gradientTgl);
            container.Add(gradientRow);

            var gradientFields = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginBottom = 6 } };
            gradientFields.Add(YUCPUIToolkitHelper.CreateField(textGradientProp, "Gradient"));
            gradientFields.Add(YUCPUIToolkitHelper.CreateField(textGradientAngleProp, "Angle"));
            container.Add(gradientFields);

            void UpdateGradientVisibility()
            {
                bool show = useTextGradientProp.boolValue;
                gradientFields.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
            gradientTgl.RegisterValueChangedCallback(_ => UpdateGradientVisibility());
            container.schedule.Execute(UpdateGradientVisibility).Every(50);
            
            // Font Settings Main Row
            var fontMainRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, alignItems = Align.Center, backgroundColor = new Color(1,1,1,0.02f), borderTopLeftRadius=4, borderTopRightRadius=4, borderBottomLeftRadius=4, borderBottomRightRadius=4, paddingLeft=8, paddingRight=8, paddingTop=6, paddingBottom=6, marginBottom=6 } };
            
            var fontAssetBox = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexGrow = 1 } };
            fontAssetBox.Add(new Label("Font Asset") { style = { color = new Color(1,1,1,0.6f), fontSize = 11, width = 70 } });
            var fField = CreateNativeField(fontProp, true); fontAssetBox.Add(fField);
            fontMainRow.Add(fontAssetBox);
            
            var fontSizeBox = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 10 } };
            fontSizeBox.Add(new Label("Size") { style = { color = new Color(1,1,1,0.6f), fontSize = 11, paddingRight = 6 } });
            fontSizeBox.Add(CreateCompactIntField(sizeProp));
            fontMainRow.Add(fontSizeBox);
            
            container.Add(fontMainRow);

            // Alignment & Style Row
            var alignStyleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, alignItems = Align.Center, backgroundColor = new Color(1,1,1,0.02f), borderTopLeftRadius=4, borderTopRightRadius=4, borderBottomLeftRadius=4, borderBottomRightRadius=4, paddingLeft=8, paddingRight=8, paddingTop=6, paddingBottom=6 } };

            var alignBox = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexGrow = 1, paddingRight = 10 } };
            alignBox.Add(new Label("Align") { style = { color = new Color(1,1,1,0.6f), fontSize = 11, width = 70 } });
            var aField = CreateNativeField(alignProp, true); alignBox.Add(aField);
            alignStyleRow.Add(alignBox);

            var styleBox = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexGrow = 1 } };
            styleBox.Add(new Label("Style") { style = { color = new Color(1,1,1,0.6f), fontSize = 11, width = 45 } });
            var sField = CreateNativeField(styleProp, true); styleBox.Add(sField);
            alignStyleRow.Add(styleBox);

            container.Add(alignStyleRow);
            
            void UpdatePreview() {
                bool useDef = useDefaultColorProp.boolValue;
                colorPickerBox.style.display = useDef ? DisplayStyle.None : DisplayStyle.Flex;
                previewText.style.color = useDef ? new Color(0.8f, 0.8f, 0.8f, 1f) : colorProp.colorValue;
                
                previewText.style.fontSize = sizeProp.intValue > 0 ? sizeProp.intValue : 12;
                
                var anchor = (TextAnchor)alignProp.enumValueIndex;
                previewText.style.unityTextAlign = anchor;

                var style = (FontStyle)styleProp.enumValueIndex;
                previewText.style.unityFontStyleAndWeight = style;
                
                if (fontProp.objectReferenceValue != null && fontProp.objectReferenceValue is Font f) {
                    previewText.style.unityFont = f;
                } else {
                    previewText.style.unityFont = StyleKeyword.Null;
                }
            }
            container.schedule.Execute(UpdatePreview).Every(50);
            
            return container;
        }

        public static VisualElement CreateNativeField(SerializedProperty prop, bool hideLabel = false)
        {
            var pField = new PropertyField(prop, hideLabel ? "" : prop.displayName);
            pField.style.flexGrow = 1;
            pField.style.marginTop = 0; pField.style.marginBottom = 0; pField.style.marginLeft = 0; pField.style.marginRight = 0;
            pField.RegisterCallback<GeometryChangedEvent>(e => {
                var label = pField.Q<Label>();
                if (label != null && hideLabel) label.style.display = DisplayStyle.None;
            });
            return pField;
        }

        public static VisualElement CreateCompactIntField(SerializedProperty prop, string innerLabel = null)
        {
            var container = new VisualElement { style = { flexDirection = FlexDirection.Row, backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f), borderTopLeftRadius=4, borderTopRightRadius=4, borderBottomLeftRadius=4, borderBottomRightRadius=4, borderTopWidth=1, borderBottomWidth=1, borderLeftWidth=1, borderRightWidth=1, borderTopColor = new Color(1,1,1,0.1f), borderBottomColor = new Color(1,1,1,0.1f), borderLeftColor = new Color(1,1,1,0.1f), borderRightColor = new Color(1,1,1,0.1f), overflow = Overflow.Hidden, alignItems = Align.Center } };
            if (!string.IsNullOrEmpty(innerLabel))
            {
                var lbl = new Label(innerLabel) { style = { color = new Color(1,1,1,0.4f), fontSize = 11, paddingLeft = 6, paddingRight = 4 } };
                container.Add(lbl);
            }
            var field = new IntegerField();
            field.BindProperty(prop);
            field.style.flexGrow = 1;
            field.style.width = 45;
            field.RegisterCallback<GeometryChangedEvent>(e => {
                var input = field.Q(className: "unity-base-field__input");
                if (input != null) {
                    input.style.backgroundColor = Color.clear;
                    input.style.borderTopWidth = 0; input.style.borderBottomWidth = 0; input.style.borderLeftWidth = 0; input.style.borderRightWidth = 0;
                    input.style.color = new Color(1,1,1,0.9f);
                }
            });
            container.Add(field);
            return container;
        }

        public static VisualElement CreateCompactField(SerializedProperty prop, string innerLabel = null)
        {
            var container = new VisualElement { style = { flexDirection = FlexDirection.Row, backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f), borderTopLeftRadius=4, borderTopRightRadius=4, borderBottomLeftRadius=4, borderBottomRightRadius=4, borderTopWidth=1, borderBottomWidth=1, borderLeftWidth=1, borderRightWidth=1, borderTopColor = new Color(1,1,1,0.1f), borderBottomColor = new Color(1,1,1,0.1f), borderLeftColor = new Color(1,1,1,0.1f), borderRightColor = new Color(1,1,1,0.1f), overflow = Overflow.Hidden, alignItems = Align.Center } };
            
            if (!string.IsNullOrEmpty(innerLabel))
            {
                var lbl = new Label(innerLabel) { style = { color = new Color(1,1,1,0.4f), fontSize = 11, paddingLeft = 6, paddingRight = 4 } };
                container.Add(lbl);
            }

            var field = new FloatField();
            field.BindProperty(prop);
            field.style.flexGrow = 1;
            field.style.width = 45;
            
            field.RegisterCallback<GeometryChangedEvent>(e => {
                var input = field.Q(className: "unity-base-field__input");
                if (input != null) {
                    input.style.backgroundColor = Color.clear;
                    input.style.borderTopWidth = 0; input.style.borderBottomWidth = 0; input.style.borderLeftWidth = 0; input.style.borderRightWidth = 0;
                    input.style.color = new Color(1,1,1,0.9f);
                }
            });

            container.Add(field);
            return container;
        }

        public static VisualElement CreateCompactSlider(SerializedProperty prop, float min, float max)
        {
            var slider = new Slider(min, max);
            slider.BindProperty(prop);
            slider.style.flexGrow = 1;
            return slider;
        }

        public static Toggle CreateToggle(SerializedProperty prop, bool invert = false)
        {
            var toggle = new Toggle();
            toggle.BindProperty(prop);
            return toggle;
        }

        public static VisualElement CreatePaddingBoxModel(SerializedProperty top, SerializedProperty bot, SerializedProperty left, SerializedProperty right, System.Action onValueChanged = null)
        {
            var container = new VisualElement { style = { width = new StyleLength(new Length(100, LengthUnit.Percent)), alignItems = Align.Center, marginTop = 0, marginBottom = 10, flexGrow = 1, minWidth = 150 } };
            
            var outer = new VisualElement { style = { width = 150, height = 90, alignItems = Align.Center, justifyContent = Justify.Center, borderTopWidth=1, borderBottomWidth=1, borderLeftWidth=1, borderRightWidth=1, borderTopColor = new Color(1,1,1,0.2f), borderBottomColor=new Color(1,1,1,0.2f), borderLeftColor=new Color(1,1,1,0.2f), borderRightColor=new Color(1,1,1,0.2f), borderTopLeftRadius=4, borderTopRightRadius=4, borderBottomLeftRadius=4, borderBottomRightRadius=4, position = Position.Relative } };
            
            var inner = new VisualElement { style = { width = 60, height = 30, backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f), borderTopLeftRadius=3, borderTopRightRadius=3, borderBottomLeftRadius=3, borderBottomRightRadius=3, alignItems = Align.Center, justifyContent = Justify.Center, borderTopWidth=1, borderBottomWidth=1, borderLeftWidth=1, borderRightWidth=1, borderTopColor=new Color(1,1,1,0.1f), borderBottomColor=new Color(1,1,1,0.1f), borderLeftColor=new Color(1,1,1,0.1f), borderRightColor=new Color(1,1,1,0.1f) } };
            inner.Add(new Label("CONTENT") { style = { fontSize = 9, color = new Color(1,1,1,0.4f), unityFontStyleAndWeight = FontStyle.Bold } });
            
            outer.Add(inner);

            var tField = CreateFloatInvisible(top, onValueChanged); tField.style.position = Position.Absolute; tField.style.top = 2; tField.style.width = 40;
            var bField = CreateFloatInvisible(bot, onValueChanged); bField.style.position = Position.Absolute; bField.style.bottom = 2; bField.style.width = 40;
            var lField = CreateFloatInvisible(left, onValueChanged); lField.style.position = Position.Absolute; lField.style.left = 2; lField.style.width = 40;
            var rField = CreateFloatInvisible(right, onValueChanged); rField.style.position = Position.Absolute; rField.style.right = 2; rField.style.width = 40;

            outer.Add(tField); outer.Add(bField); outer.Add(lField); outer.Add(rField);
            container.Add(outer);
            return container;
        }

        public static FloatField CreateFloatInvisible(SerializedProperty prop, System.Action onValueChanged = null)
        {
            var f = new FloatField();
            f.BindProperty(prop);
            if (onValueChanged != null)
                f.RegisterValueChangedCallback(_ => onValueChanged());
            f.RegisterCallback<GeometryChangedEvent>(e => {
                var input = f.Q(className: "unity-base-field__input");
                if (input != null) {
                    input.style.backgroundColor = Color.clear;
                    input.style.borderTopWidth = 0; input.style.borderBottomWidth = 0; input.style.borderLeftWidth = 0; input.style.borderRightWidth = 0;
                    input.style.color = new Color(1,1,1,0.8f);
                    input.style.unityTextAlign = TextAnchor.MiddleCenter;
                    input.style.fontSize = 11;
                }
            });
            return f;
        }

        public static VisualElement CreateCornerRadiusBox(SerializedProperty uniformTgl, SerializedProperty global, SerializedProperty tl, SerializedProperty tr, SerializedProperty bl, SerializedProperty br)
        {
            var container = new VisualElement { style = { width = new StyleLength(new Length(100, LengthUnit.Percent)), alignItems = Align.Center, marginTop = 0, marginBottom = 5 } };
            
            var rowOpts = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.Center, marginBottom = 15, alignItems =Align.Center } };
            rowOpts.Add(CreateLabel("Independent Corners", 0, true));
            var indTgl = CreateToggle(uniformTgl, true); 
            indTgl.style.marginLeft = 10;
            rowOpts.Add(indTgl);
            container.Add(rowOpts);

            var boxCont = new VisualElement { style = { width = 160, height = 100, position = Position.Relative, alignItems = Align.Center, justifyContent = Justify.Center } };
            
            var visualBox = new VisualElement { style = { position = Position.Absolute, width = 100, height = 60, backgroundColor = new Color(0.21f, 0.75f, 0.69f, 0.4f), borderTopColor = new Color(0.21f, 0.75f, 0.69f, 1f), borderBottomColor = new Color(0.21f, 0.75f, 0.69f, 1f), borderLeftColor = new Color(0.21f, 0.75f, 0.69f, 1f), borderRightColor = new Color(0.21f, 0.75f, 0.69f, 1f), borderTopWidth = 2, borderBottomWidth = 2, borderLeftWidth = 2, borderRightWidth = 2 } };
            
            var globalFld = CreateFloatInvisible(global); globalFld.style.width=40; globalFld.style.position = Position.Absolute;
            
            var tlFld = CreateCompactField(tl); tlFld.style.position = Position.Absolute; tlFld.style.top = 0; tlFld.style.left = 0; tlFld.style.width=40;
            var trFld = CreateCompactField(tr); trFld.style.position = Position.Absolute; trFld.style.top = 0; trFld.style.right = 0; trFld.style.width=40;
            var blFld = CreateCompactField(bl); blFld.style.position = Position.Absolute; blFld.style.bottom = 0; blFld.style.left = 0; blFld.style.width=40;
            var brFld = CreateCompactField(br); brFld.style.position = Position.Absolute; brFld.style.bottom = 0; brFld.style.right = 0; brFld.style.width=40;

            boxCont.Add(visualBox);
            boxCont.Add(globalFld);
            boxCont.Add(tlFld); boxCont.Add(trFld); boxCont.Add(blFld); boxCont.Add(brFld);

            void UpdateBox()
            {
                bool uniform = uniformTgl.boolValue;
                globalFld.style.display = uniform ? DisplayStyle.Flex : DisplayStyle.None;
                tlFld.style.display = uniform ? DisplayStyle.None : DisplayStyle.Flex;
                trFld.style.display = uniform ? DisplayStyle.None : DisplayStyle.Flex;
                blFld.style.display = uniform ? DisplayStyle.None : DisplayStyle.Flex;
                brFld.style.display = uniform ? DisplayStyle.None : DisplayStyle.Flex;

                if (uniform) {
                    float r = global.floatValue;
                    visualBox.style.borderTopLeftRadius = r; visualBox.style.borderTopRightRadius = r; visualBox.style.borderBottomLeftRadius = r; visualBox.style.borderBottomRightRadius = r;
                } else {
                    visualBox.style.borderTopLeftRadius = tl.floatValue; visualBox.style.borderTopRightRadius = tr.floatValue; visualBox.style.borderBottomLeftRadius = bl.floatValue; visualBox.style.borderBottomRightRadius = br.floatValue;
                }
            }
            
            boxCont.schedule.Execute(UpdateBox).Every(30);
            container.Add(boxCont);
            return container;
        }
    }
}
