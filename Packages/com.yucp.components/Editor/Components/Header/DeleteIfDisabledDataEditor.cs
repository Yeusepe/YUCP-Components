using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
	[CustomEditor(typeof(DeleteIfDisabledData))]
	[CanEditMultipleObjects]
	public class DeleteIfDisabledDataEditor : UnityEditor.Editor
	{
		private DeleteIfDisabledData data;
		private SerializedProperty debugModeProp;

		private void OnEnable()
		{
			data = (DeleteIfDisabledData)target;
			debugModeProp = serializedObject.FindProperty("debugMode");
		}

		public override VisualElement CreateInspectorGUI()
		{
			serializedObject.Update();

			var root = new VisualElement();
			YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
			root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Delete If Disabled"));

			var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(DeleteIfDisabledData));
			if (supportBanner != null) root.Add(supportBanner);

			var descriptorWarnings = new VisualElement();
			descriptorWarnings.name = "descriptor-warnings";
			root.Add(descriptorWarnings);

			var overviewCard = YUCPUIToolkitHelper.CreateCard("Overview", "This GameObject will be removed at avatar build time if it is disabled (inactive).");
			var overviewContent = YUCPUIToolkitHelper.GetCardContent(overviewCard);
			overviewContent.Add(YUCPUIToolkitHelper.CreateHelpBox(
				"Attach this component to a child object you want to strip from the built avatar when it is turned off. " +
				"Only the object this component is on is affected. If the GameObject is active when you build, it is kept; if it is inactive, it is deleted. " +
				"Cannot be attached to the avatar root. Use a child only.",
				YUCPUIToolkitHelper.MessageType.Info));
			root.Add(overviewCard);

			var optionsCard = YUCPUIToolkitHelper.CreateCard("Options", "Build-time behavior.");
			var optionsContent = YUCPUIToolkitHelper.GetCardContent(optionsCard);
			optionsContent.Add(YUCPUIToolkitHelper.CreateField(debugModeProp, "Debug Mode"));
			optionsContent.Add(YUCPUIToolkitHelper.CreateHelpBox("When enabled, the build pipeline will log when this object is deleted because it was disabled.", YUCPUIToolkitHelper.MessageType.None));
			root.Add(optionsCard);

			YUCPUIToolkitHelper.AddSpacing(root, 6);

			UpdateDescriptorWarnings(descriptorWarnings);

			root.schedule.Execute(() =>
			{
				serializedObject.Update();
				UpdateDescriptorWarnings(descriptorWarnings);
				serializedObject.ApplyModifiedProperties();
			}).Every(100);

			return root;
		}

		private void UpdateDescriptorWarnings(VisualElement container)
		{
			container.Clear();
			var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
			if (descriptor == null)
				return;
			if (data.gameObject == descriptor.gameObject)
			{
				container.Add(YUCPUIToolkitHelper.CreateHelpBox(
					"Delete If Disabled cannot be attached to the avatar root. Attach it to a child object only.",
					YUCPUIToolkitHelper.MessageType.Error));
			}
		}
	}
}
