using UnityEditor;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(ContactTrackerData))]
    public class ContactTrackerDataEditor : UnityEditor.Editor
    {
        // One proximity contact per direction, in the order the build reads collisionTags.
        private static readonly string[] Directions = { "X+", "X-", "Y+", "Y-", "Z+", "Z-" };

        public override VisualElement CreateInspectorGUI()
        {
            var data = (ContactTrackerData)target;
            var ui = YUCPInspector.Create(this, "Contact Tracker");
            ui.Status(() => VRLabsInspector.Placement(data));

            var tracking = ui.Section("Tracking");
            VRLabsInspector.AvatarObject(ui, tracking, "appliedTransform", "Tracker Target");
            ui.Field(tracking, "sizeParameter", "Default Size");
            var tags = ui.Group(tracking, "Collision tags");
            if (ui.Object.FindProperty("collisionTags").arraySize >= Directions.Length)
            {
                for (var i = 0; i < Directions.Length; i++)
                    ui.Field(tags, $"collisionTags.Array.data[{i}]", Directions[i]);
            }
            else
            {
                // ponytail: old data with fewer than six tags gets the plain list until it has six again.
                ui.Field(tags, "collisionTags");
                ui.Messages(tags, () => new[] { YUCPInspector.Message.Warning(data.collisionTags == null || data.collisionTags.Length < Directions.Length
                    ? "Add six collision tags, one per direction: X+, X-, Y+, Y-, Z+, Z-." : null) });
            }

            VRLabsInspector.Menu(ui, null, "menuLocation", ("globalParameterControl", "Control Parameter"));
            VRLabsInspector.Advanced(ui, "trackerGroupId", "Based on VRLabs Contact Tracker (MIT). Credit VRLabs when you share the avatar.",
                data.GetBuildSummary, () => data.GetLastBuildTimeUtc());
            return ui.Root;
        }
    }
}
