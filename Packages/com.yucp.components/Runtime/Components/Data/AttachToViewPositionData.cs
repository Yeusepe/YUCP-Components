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
        [Tooltip("Offset from the avatar's ViewPosition in local space.")]
        public Vector3 offset = Vector3.zero;

        [Tooltip("Optionally align the X-axis toward a specific eye bone.")]
        public EyeAlignment eyeAlignment = EyeAlignment.None;

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;
    }
}