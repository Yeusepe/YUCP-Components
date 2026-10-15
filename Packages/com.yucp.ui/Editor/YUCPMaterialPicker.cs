using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.UI.DesignSystem.Utilities
{
    /// <summary>
    /// Chooses some of a renderer's materials (HIG Entering data: offer choices instead of text entry).
    /// Bound to a Material array or list property; an empty selection usually means "all". Tiles wrap to
    /// the inspector's width, and each tile is a checkbox, so selection never relies on color alone.
    /// Every change is a SerializedProperty write, so it is undoable.
    /// </summary>
    public sealed class YUCPMaterialPicker : VisualElement
    {
        private readonly SerializedObject serializedObject;
        private readonly string path;
        private readonly Func<Renderer> source;
        private readonly Func<Material, string> badge;
        private readonly string emptySelection;
        private readonly string noSource;
        private readonly Label summary;
        private readonly Button clear;
        private readonly VisualElement grid;
        private readonly Label empty;
        private string shown;

        /// <param name="label">The name on the field label column, e.g. "Materials".</param>
        /// <param name="source">The renderer whose materials are offered.</param>
        /// <param name="emptySelection">What an empty selection means, e.g. "All supported materials".</param>
        /// <param name="noSource">What to do while there is no renderer, e.g. "Assign the body mesh to choose its materials."</param>
        /// <param name="badge">Optional short badge text for a material (null for none).</param>
        public YUCPMaterialPicker(SerializedObject serializedObject, string path, string label, Func<Renderer> source,
            string emptySelection, string noSource, Func<Material, string> badge = null)
        {
            this.serializedObject = serializedObject;
            this.path = path;
            this.source = source;
            this.badge = badge;
            this.emptySelection = emptySelection;
            this.noSource = noSource;
            AddToClassList("yucp-material-picker");
            tooltip = serializedObject.FindProperty(path)?.tooltip;

            var header = new VisualElement();
            header.AddToClassList("yucp-info-row");
            var name = new Label(label);
            name.AddToClassList("yucp-info-label");
            header.Add(name);
            summary = new Label();
            summary.AddToClassList("yucp-info-value");
            header.Add(summary);
            clear = YUCPUIToolkitHelper.CreateButton("Clear Selection", () => Write(new List<Material>()), YUCPUIToolkitHelper.ButtonVariant.Ghost);
            header.Add(clear);
            Add(header);

            grid = new VisualElement();
            grid.AddToClassList("yucp-material-grid");
            Add(grid);
            empty = new Label();
            empty.AddToClassList("yucp-list-empty");
            Add(empty);
            Refresh();
        }

        /// <summary>Syncs the selection and previews. Tiles are rebuilt only when the offered materials change.</summary>
        public void Refresh()
        {
            var selected = Read();
            var renderer = source();
            var options = (renderer != null ? renderer.sharedMaterials : Array.Empty<Material>())
                .Where(m => m != null).Concat(selected).Distinct().ToList();
            var key = string.Join(",", options.Select(m => m.GetInstanceID()));
            if (key != shown)
            {
                shown = key;
                grid.Clear();
                foreach (var material in options) grid.Add(Tile(material));
            }
            foreach (var tile in grid.Children().OfType<Toggle>())
            {
                var on = selected.Contains((Material)tile.userData);
                if (tile.value != on) tile.SetValueWithoutNotify(on);
                tile.EnableInClassList("yucp-material-tile--selected", on);
                var preview = tile.Q<Image>();
                if (preview.userData == null) LoadPreview(preview, (Material)tile.userData);
            }

            var text = selected.Count == 0 ? emptySelection : selected.Count == 1 ? selected[0].name : selected.Count + " selected";
            if (summary.text != text) summary.text = text;
            clear.style.display = selected.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            var hint = renderer == null ? noSource : options.Count == 0 ? "The mesh has no materials." : "";
            if (empty.text != hint) empty.text = hint;
            empty.style.display = hint.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private VisualElement Tile(Material material)
        {
            var tile = new Toggle { text = material.name, userData = material, tooltip = material.shader != null ? material.shader.name : null };
            tile.AddToClassList("yucp-material-tile");
            var preview = new Image { scaleMode = ScaleMode.ScaleToFit };
            preview.AddToClassList("yucp-material-tile__preview");
            tile.Insert(0, preview);
            var badgeText = badge?.Invoke(material);
            if (!string.IsNullOrEmpty(badgeText))
            {
                var label = new Label(badgeText);
                label.AddToClassList("yucp-badge");
                label.AddToClassList("yucp-badge-default");
                tile.Add(label);
            }
            tile.RegisterValueChangedCallback(e =>
            {
                e.StopPropagation();
                serializedObject.Update();
                var selected = Read();
                selected.Remove(material);
                if (e.newValue) selected.Add(material);
                Write(selected);
            });
            return tile;
        }

        // Asset previews load in the background; until then the tile shows the material icon.
        private static void LoadPreview(Image image, Material material)
        {
            var preview = AssetPreview.GetAssetPreview(material);
            if (preview != null) image.image = preview;
            else if (image.image == null) image.image = AssetPreview.GetMiniThumbnail(material);
            if (preview != null || !AssetPreview.IsLoadingAssetPreview(material.GetInstanceID())) image.userData = true;
        }

        private List<Material> Read()
        {
            var property = serializedObject.FindProperty(path);
            var materials = new List<Material>();
            for (var i = 0; i < property.arraySize; i++)
                if (property.GetArrayElementAtIndex(i).objectReferenceValue is Material material && !materials.Contains(material))
                    materials.Add(material);
            return materials;
        }

        private void Write(List<Material> materials)
        {
            serializedObject.Update();
            var property = serializedObject.FindProperty(path);
            property.arraySize = materials.Count;
            for (var i = 0; i < materials.Count; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = materials[i];
            serializedObject.ApplyModifiedProperties();
            Refresh();
        }
    }

    public static class YUCPMaterialPickerExtensions
    {
        /// <summary>A material picker bound to <paramref name="path"/> that refreshes with the inspector.</summary>
        public static YUCPMaterialPicker MaterialPicker(this YUCPInspector ui, VisualElement parent, string path, string label,
            Func<Renderer> source, string emptySelection, string noSource, Func<Material, string> badge = null)
        {
            var picker = new YUCPMaterialPicker(ui.Object, path, label, source, emptySelection, noSource, badge);
            parent.Add(picker);
            ui.OnRefresh(picker.Refresh);
            return picker;
        }
    }
}
