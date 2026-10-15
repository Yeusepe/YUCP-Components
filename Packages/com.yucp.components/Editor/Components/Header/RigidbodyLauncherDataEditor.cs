using UnityEditor;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(RigidbodyLauncherData))]
    public class RigidbodyLauncherDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (RigidbodyLauncherData)target;
            var ui = YUCPInspector.Create(this, "Rigidbody Launcher");
            ui.Status(() => VRLabsInspector.Placement(data));

            var launch = ui.Section("Launch");
            VRLabsInspector.AvatarObject(ui, launch, "appliedTransform", "Launch Target");
            ui.Field(launch, "launchSpeed");
            ui.Field(launch, "maximumForce");
            ui.Field(launch, "collisionLayers");

            VRLabsInspector.Trigger(ui, "Launch", "launchGesture", "launchParameterName");
            VRLabsInspector.Menu(ui, null, "menuLocation", ("globalParameterControl", "Control Parameter"));
            VRLabsInspector.Advanced(ui, "launcherGroupId", "Based on VRLabs Rigidbody Launcher (MIT). Credit VRLabs when you share the avatar.",
                data.GetBuildSummary, () => data.GetLastBuildTimeUtc());
            return ui.Root;
        }
    }
}
