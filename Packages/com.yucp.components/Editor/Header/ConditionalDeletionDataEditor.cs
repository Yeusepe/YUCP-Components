using UnityEditor;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
	[CustomEditor(typeof(ConditionalDeletionData))]
	public class ConditionalDeletionDataEditor : UnityEditor.Editor
	{
		public override VisualElement CreateInspectorGUI()
		{
			var ui = YUCPInspector.Create(this, "Conditional Deletion");

			var conditions = ui.Section("Conditions", "Checked against the avatar when it builds.");
			ui.Field(conditions, "conditionGroups", "Condition Groups");
			ui.Caption(conditions, "Groups can nest up to 8 levels deep.");

			var delete = ui.Section("Objects to delete");
			ui.Field(delete, "objectsToDelete", "Objects");
			ui.Segmented(delete, "deleteOnTrue", "Delete When", "No Group True", "Any Group True");

			ui.Field(ui.Advanced(), "debugMode", "Verbose Logging");
			return ui.Root;
		}
	}
}
