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
	[AddComponentMenu("YUCP/Universal PhysBone Collider")]
	[HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
	[DisallowMultipleComponent]
	public class UniversalPhysboneColliderData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
	{
		[Header("Collider")]
		[Tooltip("A PhysBone Collider, or an object to use every PhysBone Collider on it and its children.")]
		public new Object collider;

		[Header("Exclusions")]
		[Tooltip("PhysBones that don't get the collider. Assign a PhysBone, or a bone to skip every PhysBone whose chain contains it.")]
		public List<Object> exclude = new List<Object>();

		[Header("Diagnostics")]
		[Tooltip("Logs how many PhysBones got the collider when the avatar builds.")]
		public bool verboseLogging = false;

		public int PreprocessOrder => 0;
		public bool OnPreprocess() => true;
	}
}
