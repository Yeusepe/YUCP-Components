using UnityEditor;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(RaycastPrefabData))]
    public class RaycastPrefabDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (RaycastPrefabData)target;
            var ui = YUCPInspector.Create(this, "Raycast Prefab");
            ui.Status(() => VRLabsInspector.Placement(data));

            var raycast = ui.Section("Raycast");
            VRLabsInspector.AvatarObject(ui, raycast, "raycastOrigin");
            ui.Field(raycast, "grounderLayers", "Raycast Layers");
            ui.Field(raycast, "raycastDistance");

            VRLabsInspector.Menu(ui, "generateMenu", "menuLocation");
            VRLabsInspector.Advanced(ui, "raycastGroupId", "Based on VRLabs Raycast Prefab (MIT). Credit VRLabs when you share the avatar.",
                data.GetBuildSummary, () => data.GetLastBuildTimeUtc());
            return ui.Root;
        }
    }
}
