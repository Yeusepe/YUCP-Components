using UnityEditor;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(CollisionDetectionData))]
    public class CollisionDetectionDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (CollisionDetectionData)target;
            var ui = YUCPInspector.Create(this, "Collision Detection");
            ui.Status(() => VRLabsInspector.Placement(data));

            var collision = ui.Section("Collision");
            ui.Field(collision, "collisionLayers");
            ui.Field(collision, "useTriggers");
            ui.Field(collision, "particleScale", "Detection Size");
            ui.Field(collision, "alwaysReset");

            VRLabsInspector.Menu(ui, null, "menuLocation",
                ("globalParameterReset", "Reset Parameter"), ("globalParameterAlwaysReset", "Always Reset Parameter"));
            VRLabsInspector.Advanced(ui, "collisionGroupId", "Based on VRLabs Collision Detection (MIT). Credit VRLabs when you share the avatar.",
                data.GetBuildSummary, () => data.GetLastBuildTimeUtc());
            return ui.Root;
        }
    }
}
