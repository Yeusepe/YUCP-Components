using UnityEditor;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(FollowerData))]
    public class FollowerDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (FollowerData)target;
            var ui = YUCPInspector.Create(this, "Follower");
            ui.Status(() => VRLabsInspector.Placement(data, data.appliedObjectOverride != null ? data.appliedObjectOverride : data.transform));

            var follow = ui.Section("Follow");
            ui.Field(follow, "appliedObjectOverride", "Followed Object");
            ui.Field(follow, "positionTarget");
            ui.Field(follow, "lookTarget");
            ui.Field(follow, "followSpeed");

            VRLabsInspector.Menu(ui, "generateMenu", "menuLocation", ("globalParameterStop", "Stop Parameter"));
            VRLabsInspector.Advanced(ui, "followerGroupId", "Based on VRLabs Follower (MIT). Credit VRLabs when you share the avatar.",
                data.GetBuildSummary, () => data.GetLastBuildTimeUtc());
            return ui.Root;
        }
    }
}
