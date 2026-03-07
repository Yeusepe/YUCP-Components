using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    [SupportBanner]
    [AddComponentMenu("YUCP/Auto Grip")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    public class AutoGripData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        public const int FingerCount = 5;

        [Header("Grip Settings")]
        public HandTarget handTarget = HandTarget.Right;

        [Header("Finger Pose")]
        public Vector3[] fingerTipLocals = new Vector3[FingerCount];
        public bool fingerPoseInitialized = false;

        [Header("Toggle Integration")]
        public bool useExistingToggle = false;
        public Component selectedToggle;
        public bool createToggle = true;
        public string toggleMenuPath = "Props/Grip";
        public bool toggleSaved = true;
        public bool toggleDefaultOn = false;

        [Header("Debug")]
        public bool verboseLogging = false;

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;

        /// <summary>
        /// Fingertip positions are stored in local space relative to this transform.
        /// </summary>
        public Vector3 GetFingerTipWorld(int fingerIndex)
        {
            if (fingerIndex < 0 || fingerIndex >= FingerCount) return transform.position;
            return transform.TransformPoint(fingerTipLocals[fingerIndex]);
        }

        public void SetFingerTipWorld(int fingerIndex, Vector3 worldPos)
        {
            if (fingerIndex < 0 || fingerIndex >= FingerCount) return;
            fingerTipLocals[fingerIndex] = transform.InverseTransformPoint(worldPos);
        }
    }
}
