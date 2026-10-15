using UnityEditor;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
	[CustomEditor(typeof(DeleteIfDisabledData))]
	[CanEditMultipleObjects]
	public class DeleteIfDisabledDataEditor : UnityEditor.Editor
	{
		public override VisualElement CreateInspectorGUI()
		{
			var data = (DeleteIfDisabledData)target;
			var ui = YUCPInspector.Create(this, "Delete If Disabled");
			ui.Status(() =>
			{
				var avatar = data.GetComponentInParent<VRCAvatarDescriptor>(true);
				return avatar != null && avatar.gameObject == data.gameObject
					? new[] { YUCPInspector.Message.Error("Move this to a child object. It can't be on the avatar root.") }
					: null;
			});

			var build = ui.Section("Build");
			ui.InfoRow(build, "When Built", () => data.gameObject.activeInHierarchy
				? "Kept, because this object is active."
				: "Removed, because this object is inactive.");

			ui.Field(ui.Advanced(), "debugMode", "Verbose Logging");
			return ui.Root;
		}
	}
}
