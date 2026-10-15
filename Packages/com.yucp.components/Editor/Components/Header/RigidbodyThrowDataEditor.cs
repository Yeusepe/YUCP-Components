using UnityEditor;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(RigidbodyThrowData))]
    public class RigidbodyThrowDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (RigidbodyThrowData)target;
            var ui = YUCPInspector.Create(this, "Rigidbody Throw");
            ui.Status(() => VRLabsInspector.Placement(data));

            var section = ui.Section("Throw");
            VRLabsInspector.AvatarObject(ui, section, "appliedTransform", "Throw Target");
            ui.Field(section, "physicsMaterial");
            ui.Field(section, "collisionLayers");
            ui.Field(section, "enableRotationSync", "Sync Rotation");

            VRLabsInspector.Trigger(ui, "Throw", "throwGesture", "throwParameterName");
            VRLabsInspector.Menu(ui, "generateMenu", "menuLocation");
            VRLabsInspector.Advanced(ui, "throwGroupId", "Based on VRLabs Rigidbody Throw (MIT). Credit VRLabs when you share the avatar.",
                data.GetBuildSummary, () => data.GetLastBuildTimeUtc());
            return ui.Root;
        }
    }
}
