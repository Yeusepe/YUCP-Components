using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
	/// <summary>
	/// At avatar build time, finds all PhysBones on the avatar and adds the selected
	/// PhysBone Collider to each of their collider lists. Use this to apply one collider
	/// (e.g. a body or foot collider) to every PhysBone without assigning it manually.
	/// </summary>
	[SupportBanner]
	[AddComponentMenu("YUCP/Universal Physbone Collider")]
	[HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
	[DisallowMultipleComponent]
	public class UniversalPhysboneColliderData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
	{
		[Header("Collider")]
		[Tooltip("Assign a GameObject (all VRC PhysBone Colliders on it and its children will be added to every PhysBone) or a single VRC PhysBone Collider component. Must be under this avatar.")]
		public Object collider;

		[Header("Exclusions")]
		[Tooltip("PhysBones to exclude. Assign VRCPhysBone components, or GameObjects/Transforms (bones). Excludes any PhysBone that contains that bone: as root, as parent of root, or anywhere in the chain.")]
		public List<Object> exclude = new List<Object>();

		[Header("Diagnostics")]
		[Tooltip("Print how many PhysBones were updated when building.")]
		public bool verboseLogging = false;

		public int PreprocessOrder => 0;
		public bool OnPreprocess() => true;
	}
}
