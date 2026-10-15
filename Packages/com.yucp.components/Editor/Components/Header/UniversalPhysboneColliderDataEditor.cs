using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Editor
{
	[CustomEditor(typeof(UniversalPhysboneColliderData))]
	[CanEditMultipleObjects]
	public class UniversalPhysboneColliderDataEditor : UnityEditor.Editor
	{
		private const string ColliderType = "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider";

		public override VisualElement CreateInspectorGUI()
		{
			var data = (UniversalPhysboneColliderData)target;
			var ui = YUCPInspector.Create(this, "Universal PhysBone Collider");
			ui.Status(() => Avatar(data) == null ? new[] { YUCPInspector.Message.Error("Put this inside an avatar.") } : null);

			var collider = ui.Section("Collider", "Added to every PhysBone on the avatar when it builds.");
			ui.Field(collider, "collider", "Collider");
			ui.Messages(collider, () => new[] { YUCPInspector.Message.Warning(ColliderProblem(data)) });
			ui.Field(collider, "exclude", "Excluded PhysBones");

			ui.Field(ui.Advanced(), "verboseLogging", "Verbose Logging");
			return ui.Root;
		}

		private static VRCAvatarDescriptor Avatar(Component data) => data.GetComponentInParent<VRCAvatarDescriptor>(true);

		/// <summary>What is wrong with the assigned collider, or null.</summary>
		private static string ColliderProblem(UniversalPhysboneColliderData data)
		{
			var avatar = Avatar(data);
			if (avatar == null) return null;
			if (data.collider == null) return "Assign a PhysBone Collider, or an object with PhysBone Colliders.";
			switch (data.collider)
			{
				case GameObject go when !go.transform.IsChildOf(avatar.transform):
					return "Choose an object on this avatar.";
				case GameObject go:
					var type = System.Type.GetType(ColliderType + ", VRC.SDK3.Dynamics.PhysBone");
					return type != null && go.GetComponentsInChildren(type, true).Length == 0
						? "Add a PhysBone Collider to this object or its children." : null;
				case Component c when c.GetType().FullName == ColliderType || c.GetType().Name == "VRCPhysBoneCollider":
					return c.transform.IsChildOf(avatar.transform) ? null : "Choose a PhysBone Collider on this avatar.";
				default:
					return "Assign a PhysBone Collider, or an object with PhysBone Colliders.";
			}
		}
	}
}
