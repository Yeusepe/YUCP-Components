using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
	/// <summary>
	/// At avatar build time, this GameObject is removed if it is disabled (inactive in the hierarchy).
	/// Attach to any object you want to strip from the build when it is turned off.
	/// </summary>
	[SupportBanner]
	[AddComponentMenu("YUCP/Delete If Disabled")]
	[HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
	[DisallowMultipleComponent]
	public class DeleteIfDisabledData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
	{
		[Header("Debug")]
		[Tooltip("Enable detailed logging during avatar build when this object is deleted.")]
		public bool debugMode = false;

		public int PreprocessOrder => 0;
		public bool OnPreprocess() => true;
	}
}
