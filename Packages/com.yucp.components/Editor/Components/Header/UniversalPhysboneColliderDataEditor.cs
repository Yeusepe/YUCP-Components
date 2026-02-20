using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
	[CustomEditor(typeof(UniversalPhysboneColliderData))]
	[CanEditMultipleObjects]
	public class UniversalPhysboneColliderDataEditor : UnityEditor.Editor
	{
		private UniversalPhysboneColliderData data;
		private SerializedProperty colliderProp;
		private SerializedProperty verboseLoggingProp;

		private void OnEnable()
		{
			data = (UniversalPhysboneColliderData)target;
			colliderProp = serializedObject.FindProperty("collider");
			verboseLoggingProp = serializedObject.FindProperty("verboseLogging");
		}

		public override VisualElement CreateInspectorGUI()
		{
			serializedObject.Update();

			var root = new VisualElement();
			YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
			root.Add(YUCP.Components.Resources.YUCPComponentHeader.CreateHeaderOverlay("Universal Physbone Collider"));

			var supportBanner = SupportBannerHelper.CreateSupportBannerVisualElement(typeof(UniversalPhysboneColliderData));
			if (supportBanner != null) root.Add(supportBanner);

			var overviewCard = YUCPUIToolkitHelper.CreateCard("Overview", "Add PhysBone Colliders to every PhysBone on the avatar at build time.");
			var overviewContent = YUCPUIToolkitHelper.GetCardContent(overviewCard);
			overviewContent.Add(YUCPUIToolkitHelper.CreateHelpBox(
				"At avatar build, all VRC PhysBone components will have the selected collider(s) added to their collider list. " +
				"Assign a GameObject (all PhysBone Colliders on it and its children are used) or a single PhysBone Collider component.",
				YUCPUIToolkitHelper.MessageType.Info));
			root.Add(overviewCard);

			var colliderCard = YUCPUIToolkitHelper.CreateCard("Collider", "GameObject or PhysBone Collider to add to all PhysBones.");
			var colliderContent = YUCPUIToolkitHelper.GetCardContent(colliderCard);
			colliderContent.Add(YUCPUIToolkitHelper.CreateField(colliderProp, "Collider Source"));
			colliderContent.Add(YUCPUIToolkitHelper.CreateHelpBox(
				"GameObject: every VRC PhysBone Collider on this object and its children will be added to every PhysBone. Or assign a single PhysBone Collider component.",
				YUCPUIToolkitHelper.MessageType.None));
			root.Add(colliderCard);

			var diagnosticsCard = YUCPUIToolkitHelper.CreateCard("Diagnostics", "Build logging.");
			var diagnosticsContent = YUCPUIToolkitHelper.GetCardContent(diagnosticsCard);
			diagnosticsContent.Add(YUCPUIToolkitHelper.CreateField(verboseLoggingProp, "Verbose Logging"));
			root.Add(diagnosticsCard);

			var warningsContainer = new VisualElement();
			warningsContainer.name = "descriptor-warnings";
			root.Add(warningsContainer);

			YUCPUIToolkitHelper.AddSpacing(root, 6);

			UpdateWarnings(warningsContainer);

			root.schedule.Execute(() =>
			{
				serializedObject.Update();
				UpdateWarnings(warningsContainer);
				serializedObject.ApplyModifiedProperties();
			}).Every(100);

			return root;
		}

		private void UpdateWarnings(VisualElement container)
		{
			container.Clear();

			var descriptor = data.GetComponentInParent<VRCAvatarDescriptor>();
			if (descriptor == null)
			{
				container.Add(YUCPUIToolkitHelper.CreateHelpBox(
					"This component must be placed under a VRCAvatarDescriptor so the collider can be applied to the avatar's PhysBones.",
					YUCPUIToolkitHelper.MessageType.Error));
				return;
			}

			if (data.collider == null)
			{
				container.Add(YUCPUIToolkitHelper.CreateHelpBox(
					"Assign a GameObject (with PhysBone Colliders on it or its children) or a single VRC PhysBone Collider component.",
					YUCPUIToolkitHelper.MessageType.Warning));
				return;
			}

			if (data.collider is GameObject go)
			{
				if (!go.transform.IsChildOf(descriptor.transform))
				{
					container.Add(YUCPUIToolkitHelper.CreateHelpBox(
						"The assigned GameObject must be under the avatar hierarchy (VRCAvatarDescriptor).",
						YUCPUIToolkitHelper.MessageType.Warning));
					return;
				}
				int count = CountPhysBoneCollidersOn(go);
				if (count == 0)
				{
					container.Add(YUCPUIToolkitHelper.CreateHelpBox(
						"This GameObject has no VRC PhysBone Collider components on it or its children. Add at least one PhysBone Collider.",
						YUCPUIToolkitHelper.MessageType.Warning));
				}
				return;
			}

			if (data.collider is Component comp)
			{
				var colliderType = comp.GetType();
				if (colliderType.FullName != "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider" &&
				    colliderType.Name != "VRCPhysBoneCollider")
				{
					container.Add(YUCPUIToolkitHelper.CreateHelpBox(
						"The assigned component is not a VRC PhysBone Collider. Assign a GameObject with PhysBone Colliders or a PhysBone Collider component.",
						YUCPUIToolkitHelper.MessageType.Warning));
				}
				else if (!comp.transform.IsChildOf(descriptor.transform))
				{
					container.Add(YUCPUIToolkitHelper.CreateHelpBox(
						"The assigned PhysBone Collider must be on the same avatar hierarchy (under the VRCAvatarDescriptor).",
						YUCPUIToolkitHelper.MessageType.Warning));
				}
			}
		}

		private static int CountPhysBoneCollidersOn(GameObject root)
		{
			var pbColliderType = System.Type.GetType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider, VRC.SDK3.Dynamics.PhysBone");
			if (pbColliderType == null) return 0;
			var components = root.GetComponentsInChildren(pbColliderType, true);
			return components != null ? components.Length : 0;
		}
	}
}
