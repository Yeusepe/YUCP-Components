using UnityEditor;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Resources
{
    [CustomEditor(typeof(AttachToBodyPartData))]
    public class AttachToBodyPartDataInstances : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var ui = YUCPInspector.Create(this, "Symmetric Armature Auto-Link");

            var attach = ui.Section("Attach");
            ui.Field(attach, "part", "Body Part");
            ui.Segmented(attach, "side", "Side", "Left", "Right", "Closest");
            ui.Field(attach, "offset", "Offset Path");

            var delete = ui.Section("Delete by side");
            ui.Field(delete, "leftComponentToDelete", "Delete If Left");
            ui.Field(delete, "rightComponentToDelete", "Delete If Right");
            return ui.Root;
        }
    }
}
