using UnityEditor;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Resources
{
    [CustomEditor(typeof(AttachToClosestBoneData))]
    public class AttachToClosestBoneDataInstances : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (AttachToClosestBoneData)target;
            var ui = YUCPInspector.Create(this, "Closest Bone Auto-Link");

            var attach = ui.Section("Attach", "Links this object to the nearest bone when the avatar builds.");
            ui.Field(attach, "maxDistance", "Max Distance");
            ui.Field(attach, "offset", "Offset Path");
            ui.ShowWhen(ui.InfoRow(attach, "Linked Bone", () => data.SelectedBonePath), () => !string.IsNullOrEmpty(data.SelectedBonePath));

            var filter = ui.Section("Bone filter");
            ui.Field(filter, "includeNameFilter", "Include Names");
            ui.Field(filter, "excludeNameFilter", "Exclude Names");
            ui.Field(filter, "ignoreHumanoidBones", "Ignore Humanoid Bones");
            return ui.Root;
        }
    }
}
