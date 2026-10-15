using UnityEditor;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Resources
{
    [CustomEditor(typeof(AttachToViewPositionData))]
    public class AttachToViewPositionDataInstances : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var ui = YUCPInspector.Create(this, "View Position & Head Auto-Link");

            var position = ui.Section("Position", "Moves this object to the avatar's view position and links it to the head.");
            ui.Field(position, "offset", "Offset");
            ui.Segmented(position, "eyeAlignment", "Eye Alignment", "None", "Left Eye", "Right Eye");
            return ui.Root;
        }
    }
}
