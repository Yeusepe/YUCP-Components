using UnityEngine;
using VRC.SDKBase;  // for IEditorOnly & IPreprocessCallbackBehaviour
using VRC.SDK3.Avatars.Components;

namespace YUCP.Components
{

    public enum EyeAlignment { None, LeftEye, RightEye }

    [SupportBanner]
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("YUCP/View Position & Head Auto-Link")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    public class AttachToViewPositionData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Tooltip("Offset from the avatar's view position, in the avatar's local space.")]
        public Vector3 offset = Vector3.zero;

        [Tooltip("Matches this eye's depth instead of the view position's.")]
        public EyeAlignment eyeAlignment = EyeAlignment.None;

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;
    }
}