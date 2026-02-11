using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
	/// <summary>
	/// At avatar build time, all VRCFury menu items (toggles, etc.) on this GameObject or its descendants
	/// are moved so their menu path is prefixed with the target path. E.g. a toggle at Body/Fluff becomes
	/// Customizables/Body/Fluff. If you move a child out of this hierarchy, its menu items stay at their original path.
	/// </summary>
	[SupportBanner]
	[AddComponentMenu("YUCP/Move All Under")]
	[HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
	[DisallowMultipleComponent]
	public class MoveAllUnderData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
	{
		[Header("Target")]
		[Tooltip("Menu path prefix. All toggles and menu items under this object will appear under this path. E.g. \"Customizables\" makes Body/Fluff become Customizables/Body/Fluff. Use slashes for subfolders.")]
		public string targetMenuPath = "Customizables";

		public int PreprocessOrder => 0;
		public bool OnPreprocess() => true;
	}
}
