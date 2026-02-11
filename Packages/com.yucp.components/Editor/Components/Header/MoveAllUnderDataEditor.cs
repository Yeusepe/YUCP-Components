using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
	[CustomEditor(typeof(MoveAllUnderData))]
	[CanEditMultipleObjects]
	public class MoveAllUnderDataEditor : UnityEditor.Editor
	{
		private MoveAllUnderData data;
		private SerializedProperty targetMenuPathProp;

		private void OnEnable()
		{
			data = (MoveAllUnderData)target;
			targetMenuPathProp = serializedObject.FindProperty("targetMenuPath");
		}

		public override VisualElement CreateInspectorGUI()
		{
			serializedObject.Update();

			var root = new VisualElement();
			YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
			root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Move All Under"));

			var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(MoveAllUnderData));
			if (supportBanner != null) root.Add(supportBanner);

			var overviewCard = YUCPUIToolkitHelper.CreateCard("Overview", "Prefix the expression menu path of all VRCFury menu items under this object.");
			var overviewContent = YUCPUIToolkitHelper.GetCardContent(overviewCard);
			overviewContent.Add(YUCPUIToolkitHelper.CreateHelpBox(
				"All toggles and menu items whose VRCFury component is on this GameObject or any descendant will be moved so their path is prefixed with the target path. " +
				"E.g. a toggle at Body/Fluff with target \"Customizables\" becomes Customizables/Body/Fluff. " +
				"If you move a child out of this hierarchy, its menu items stay at their original path.",
				YUCPUIToolkitHelper.MessageType.Info));
			root.Add(overviewCard);

			var optionsCard = YUCPUIToolkitHelper.CreateCard("Options", "Target menu path.");
			var optionsContent = YUCPUIToolkitHelper.GetCardContent(optionsCard);
			optionsContent.Add(YUCPUIToolkitHelper.CreateField(targetMenuPathProp, "Target Menu Path"));
			optionsContent.Add(YUCPUIToolkitHelper.CreateHelpBox(
				"Menu path prefix (e.g. \"Customizables\" or \"Outfit/Accessories\"). Use slashes for subfolders.",
				YUCPUIToolkitHelper.MessageType.None));
			root.Add(optionsCard);

			YUCPUIToolkitHelper.AddSpacing(root, 6);

			root.schedule.Execute(() =>
			{
				serializedObject.Update();
				serializedObject.ApplyModifiedProperties();
			}).Every(100);

			return root;
		}
	}
}
