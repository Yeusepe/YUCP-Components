using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;
using YUCP.Components.Editor.MeshUtils;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(UVDiscardToggleData))]
    public class UVDiscardToggleDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (UVDiscardToggleData)target;
            var ui = YUCPInspector.Create(this, "UV Discard Toggle");

            var meshes = ui.Section("Meshes");
            ui.Field(meshes, "targetBodyMesh", "Body Mesh");
            ui.Field(meshes, "clothingMesh", "Clothing Mesh");
            ui.Messages(meshes, () => MeshProblems(data));
            ui.Caption(meshes, () => data.clothingMesh != null && data.clothingMesh.GetComponent<AutoBodyHiderData>() != null
                ? "Shares its UV tile with the Auto Body Hider on the clothing mesh." : "");

            var tile = ui.Section("UV discard");
            ui.Field(tile, "uvChannel", "UV Channel");
            ui.Field(tile, "uvDiscardRow", "Row");
            ui.Field(tile, "uvDiscardColumn", "Column");
            ui.Messages(tile, () => new[]
            {
                YUCPInspector.Message.Warning(data.uvDiscardRow == 0 ? "Row 0 can overlap the main texture. Rows 1 to 3 are safe." : null)
            });

            ToggleSettingsInspector.Section(ui,
                menuPath: "menuPath", globalParameter: "globalParameter",
                saved: "saved", defaultOn: "defaultOn",
                slider: "slider", holdButton: "holdButton", security: "securityEnabled",
                useExclusiveTags: "enableExclusiveTag", exclusiveTags: "exclusiveTag",
                useIcon: "enableIcon", icon: "icon",
                messages: () => MenuMessages(data));

            ui.Field(ui.Advanced(), "debugSaveAnimation", "Save Generated Animation");
            return ui.Root;
        }

        private static IEnumerable<YUCPInspector.Message> MeshProblems(UVDiscardToggleData data)
        {
            if (data.targetBodyMesh == null)
                yield return YUCPInspector.Message.Error("Assign the body mesh.");
            else if (data.targetBodyMesh.sharedMesh == null)
                yield return YUCPInspector.Message.Error("Assign a mesh to the body's Skinned Mesh Renderer.");
            else if (!data.targetBodyMesh.sharedMaterials.Any(UVManipulator.IsPoiyomiWithUVSupport))
                yield return YUCPInspector.Message.Warning("Give the body mesh a Poiyomi or FastFur material with UV discard.");

            if (data.clothingMesh == null)
                yield return YUCPInspector.Message.Error("Assign the clothing mesh.");
            else if (data.clothingMesh.sharedMesh == null)
                yield return YUCPInspector.Message.Error("Assign a mesh to the clothing's Skinned Mesh Renderer.");
        }

        private static IEnumerable<YUCPInspector.Message> MenuMessages(UVDiscardToggleData data)
        {
            var hasMenu = !string.IsNullOrEmpty(data.menuPath);
            if (!hasMenu && string.IsNullOrEmpty(data.globalParameter))
                yield return YUCPInspector.Message.Error("Set a menu path or a global parameter.");
            else if (!hasMenu)
                yield return YUCPInspector.Message.Info("No menu item. Only the global parameter turns the clothing on.");
            if (data.defaultOn)
                yield return YUCPInspector.Message.Warning("For the material animation to work, turn off On by Default.");
        }
    }
}
