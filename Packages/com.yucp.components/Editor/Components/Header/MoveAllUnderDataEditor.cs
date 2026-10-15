using UnityEditor;
using UnityEngine.UIElements;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
	[CustomEditor(typeof(MoveAllUnderData))]
	[CanEditMultipleObjects]
	public class MoveAllUnderDataEditor : UnityEditor.Editor
	{
		public override VisualElement CreateInspectorGUI()
		{
			var ui = YUCPInspector.Create(this, "Move All Under");
			var menu = ui.Section("Avatar menu", "Moves every VRCFury menu item on this object and its children into this folder.");
			ui.Field(menu, "targetMenuPath", "Menu Path");
			return ui.Root;
		}
	}
}
