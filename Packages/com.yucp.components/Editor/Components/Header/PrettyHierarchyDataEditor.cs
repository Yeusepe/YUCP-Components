using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(PrettyHierarchyData))]
    [CanEditMultipleObjects]
    public class PrettyHierarchyDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var ui = YUCPInspector.Create(this, "Pretty Hierarchy");
            // The Hierarchy window is the live preview.
            ui.Root.TrackSerializedObjectValue(serializedObject, _ => EditorApplication.RepaintHierarchyWindow());
            ui.Root.TrackPropertyValue(serializedObject.FindProperty("preset"), _ => ApplyPreset());
            bool On(string path) => serializedObject.FindProperty(path).boolValue;
            VisualElement Dependent(VisualElement parent, string toggle) => ui.ShowWhen(ui.Indent(parent), () => On(toggle));

            var background = ui.Section("Background");
            ui.Field(background, "preset");
            Fill(ui, background, "useDefaultBackgroundColor", "useBackgroundGradient", "backgroundColor", "backgroundGradient", "gradientAngle",
                "Default uses the hierarchy's own color. Color and Gradient use yours.");
            ui.Field(background, "backgroundAlpha", "Opacity");
            ui.Field(background, "showShadow", "Shadow");
            var shadow = Dependent(background, "showShadow");
            ui.Field(shadow, "shadowColor", "Color");
            ui.Field(shadow, "shadowOffset", "Offset");
            ui.Field(shadow, "shadowBlur", "Blur");

            var text = ui.Section("Text");
            Fill(ui, text, "useDefaultTextColor", "useTextGradient", "textColor", "textGradient", "textGradientAngle",
                "Default uses the hierarchy's text color. Color and Gradient use yours.");
            // The drawer ignores the drop shadow on gradient text.
            ui.ShowWhen(ui.Field(text, "textDropShadow", "Drop Shadow"), () => !On("useTextGradient"));
            ui.Field(text, "font");
            ui.Field(text, "fontSize");
            ui.Field(text, "fontStyle");
            ui.Field(text, "alignment");

            var icons = ui.Section("Icons");
            ui.Field(icons, "showIcon", "Show Icon");
            var icon = Dependent(icons, "showIcon");
            ui.Field(icon, "useCustomIcon", "Custom Icon");
            var custom = Dependent(icon, "useCustomIcon");
            ui.Segmented(custom, "useCustomIconAsTexture", "Source", "Built-in", "Texture");
            ui.ShowWhen(ui.Field(custom, "customIconBuiltInName", "Icon Name"), () => !On("useCustomIconAsTexture"));
            ui.ShowWhen(ui.Field(custom, "customIcon", "Texture"), () => On("useCustomIconAsTexture"));
            ui.Field(icons, "showEditPrefabIcon", "Show Prefab Arrow");
            ui.Field(icons, "showExpandCollapseFolderIcon", "Show Folder Icon");
            var folder = Dependent(icons, "showExpandCollapseFolderIcon");
            ui.Field(folder, "folderIconOffsetX", "Offset X");
            ui.Field(folder, "folderIconOffsetY", "Offset Y");
            var closed = ui.Group(folder, "Closed folder");
            ui.Field(closed, "closedFolderIconName", "Icon Name");
            ui.Field(closed, "closedFolderCustomIcon", "Texture");
            var open = ui.Group(folder, "Open folder");
            ui.Field(open, "openFolderIconName", "Icon Name");
            ui.Field(open, "openFolderCustomIcon", "Texture");

            var shape = ui.Section("Corners and border");
            // cornerRadiusUniform: false is Independent, true is Uniform.
            ui.Segmented(shape, "cornerRadiusUniform", "Corners", "Independent", "Uniform");
            ui.ShowWhen(ui.Field(shape, "cornerRadius", "Radius"), () => On("cornerRadiusUniform"));
            var corners = new VisualElement();
            shape.Add(corners);
            ui.Field(corners, "cornerRadiusTopLeft", "Top Left");
            ui.Field(corners, "cornerRadiusTopRight", "Top Right");
            ui.Field(corners, "cornerRadiusBottomRight", "Bottom Right");
            ui.Field(corners, "cornerRadiusBottomLeft", "Bottom Left");
            ui.ShowWhen(corners, () => !On("cornerRadiusUniform"));
            var border = ui.Group(shape);
            ui.Field(border, "borderWidth");
            ui.Field(ui.ShowWhen(ui.Indent(border), () => serializedObject.FindProperty("borderWidth").floatValue > 0f), "borderColor");

            var layout = ui.Section("Layout");
            ui.Field(layout, "useCustomRowHeight", "Custom Row Height");
            ui.Field(Dependent(layout, "useCustomRowHeight"), "customRowHeight", "Row Height");
            ui.Field(layout, "iconSize");
            var margins = ui.Disclosure(layout, "Margins and Padding", "margins");
            Sides(ui, ui.Group(margins, "Row"), "margin");
            Sides(ui, ui.Group(margins, "Icon"), "iconMargin");
            var textMargins = ui.Group(margins, "Text");
            Sides(ui, textMargins, "textMargin");
            ui.Field(textMargins, "paddingLeft", "Padding Left");
            ui.Field(textMargins, "paddingRight", "Padding Right");

            ui.Button(ui.Advanced(), "Save as Prefab…", SaveAsPrefab, YUCPUIToolkitHelper.ButtonVariant.Secondary,
                "Saves this style as a prefab so you can reuse it.");
            return ui.Root;
        }

        private static void Sides(YUCPInspector ui, VisualElement parent, string prefix)
        {
            foreach (var side in new[] { "Left", "Right", "Top", "Bottom" })
                ui.Field(parent, prefix + side, side);
        }

        /// <summary>
        /// Default, Color or Gradient: three exclusive fills stored as two bools. The drawer checks
        /// the gradient first, so the segments do too.
        /// </summary>
        private static void Fill(YUCPInspector ui, VisualElement parent, string useDefault, string useGradient,
            string color, string gradient, string angle, string tooltip)
        {
            var so = ui.Object;
            int Get() => so.FindProperty(useGradient).boolValue ? 2 : so.FindProperty(useDefault).boolValue ? 0 : 1;
            var fill = new SegmentedField("Fill", new[] { "Default", "Color", "Gradient" }) { tooltip = tooltip };
            fill.RegisterValueChangedCallback(e =>
            {
                so.Update();
                so.FindProperty(useDefault).boolValue = e.newValue == 0;
                so.FindProperty(useGradient).boolValue = e.newValue == 2;
                so.ApplyModifiedProperties();
                ui.Refresh();
            });
            fill.SetValueWithoutNotify(Get());
            ui.OnRefresh(() => fill.SetValueWithoutNotify(Get()));
            parent.Add(fill);

            ui.ShowWhen(ui.Field(parent, color, "Color"), () => Get() == 1);
            var gradientFields = new VisualElement();
            parent.Add(gradientFields);
            ui.Field(gradientFields, gradient, "Gradient");
            ui.Field(gradientFields, angle, "Gradient Angle");
            ui.ShowWhen(gradientFields, () => Get() == 2);
        }

        /// <summary>Choosing a preset fills in its background. Custom keeps the current colors.</summary>
        private void ApplyPreset()
        {
            serializedObject.Update();
            var preset = (PrettyHierarchyPreset)serializedObject.FindProperty("preset").enumValueIndex;
            if (preset == PrettyHierarchyPreset.Custom) return;

            var gradient = GetPresetGradient(preset);
            serializedObject.FindProperty("backgroundColor").colorValue = GetPresetColor(preset);
            serializedObject.FindProperty("useBackgroundGradient").boolValue = gradient != null;
            if (gradient != null) serializedObject.FindProperty("backgroundGradient").gradientValue = gradient;
            serializedObject.FindProperty("useDefaultBackgroundColor").boolValue = false;
            serializedObject.ApplyModifiedProperties();
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

        /// <summary>The preset's background gradient, or null for a solid preset.</summary>
        private static Gradient GetPresetGradient(PrettyHierarchyPreset preset)
        {
            Color[] keys = preset switch
            {
                PrettyHierarchyPreset.Sunset => new[] { new Color(0.5f, 0.15f, 0.25f), new Color(0.9f, 0.4f, 0.35f), new Color(0.95f, 0.7f, 0.4f) },
                PrettyHierarchyPreset.Ocean => new[] { new Color(0.05f, 0.2f, 0.35f), new Color(0.2f, 0.5f, 0.7f), new Color(0.4f, 0.75f, 0.9f) },
                PrettyHierarchyPreset.Forest => new[] { new Color(0.05f, 0.25f, 0.1f), new Color(0.2f, 0.5f, 0.3f), new Color(0.4f, 0.75f, 0.5f) },
                PrettyHierarchyPreset.Midnight => new[] { new Color(0.02f, 0.05f, 0.12f), new Color(0.15f, 0.2f, 0.35f) },
                _ => null
            };
            if (keys == null) return null;
            var gradient = new Gradient();
            gradient.SetKeys(keys.Select((c, i) => new GradientColorKey(c, i / (keys.Length - 1f))).ToArray(),
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }

        private void SaveAsPrefab()
        {
            var path = EditorUtility.SaveFilePanelInProject("Save as Prefab", "PrettyHierarchyStyle", "prefab", "Choose where to save this style.");
            if (string.IsNullOrEmpty(path)) return;
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)) &&
                !EditorUtility.DisplayDialog("Replace the existing prefab?", path + " already exists. Replacing it can't be undone.", "Replace", "Cancel"))
                return;
            var go = new GameObject("PrettyHierarchyStyle");
            EditorUtility.CopySerialized(target, go.AddComponent<PrettyHierarchyData>());
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            DestroyImmediate(go);
            EditorGUIUtility.PingObject(prefab);
        }
    }
}
