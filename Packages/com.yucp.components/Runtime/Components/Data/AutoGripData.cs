using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    public enum HandTarget
    {
        Left,
        Right,
        Both,
        Closest
    }


    /// <summary>
    /// Automatically generates hand grip animations based on object mesh geometry.
    /// Creates VRCFury toggle to enable object and play grip animation.
    /// Uses contact-based detection to prevent finger mesh clipping.
    /// </summary>
    [BetaWarning("This component is in BETA and may not work as intended. Grip generation is experimental and may produce unexpected results.")]
    [AddComponentMenu("YUCP/Auto Grip Generator")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    public class AutoGripData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Grip Configuration")]
        [Tooltip("Auto-display gizmos in Scene view when component is selected")]
        public bool autoShowGizmos = true;

        [Header("Grip Target")]
        [Tooltip("The object that will be gripped (usually this GameObject or a child mesh).")]
        public Transform grippedObject;

        [Tooltip("Which hand(s) should grip this object.\n\n" +
                 "• Left: Always left hand\n" +
                 "• Right: Always right hand\n" +
                 "• Both: Create grips for both hands with separate toggles\n" +
                 "• Closest: Automatically detect nearest hand")]
        public HandTarget targetHand = HandTarget.Closest;

        [Header("Toggle Settings")]
        [Tooltip("Menu path for the toggle (e.g., 'Props/Phone').\n\n" +
                 "If using Both hands, will create 'Props/Phone L' and 'Props/Phone R'")]
        public string menuPath = "Props/Object";

        [Tooltip("Save toggle state across avatar reloads.")]
        public bool saved = true;

        [Tooltip("Object starts enabled by default.")]
        public bool defaultOn = false;

        [Tooltip("Optional global parameter name for syncing across network.\n\n" +
                 "Leave empty for local-only toggle.\n" +
                 "If set, creates a synced bool parameter with this name.")]
        public string globalParameter = "";

        [Header("Manual Finger Positioning")]
        [Tooltip("Position for left hand thumb tip in world space.\n\n" +
                 "Drag the gizmo in Scene view to position the thumb tip.\n" +
                 "The system will calculate muscle values to reach this position.")]
        public Vector3 leftThumbTip = Vector3.zero;

        [Tooltip("Position for left hand index finger tip in world space.")]
        public Vector3 leftIndexTip = Vector3.zero;

        [Tooltip("Position for left hand middle finger tip in world space.")]
        public Vector3 leftMiddleTip = Vector3.zero;

        [Tooltip("Position for left hand ring finger tip in world space.")]
        public Vector3 leftRingTip = Vector3.zero;

        [Tooltip("Position for left hand little finger tip in world space.")]
        public Vector3 leftLittleTip = Vector3.zero;

        [Tooltip("Position for right hand thumb tip in world space.")]
        public Vector3 rightThumbTip = Vector3.zero;

        [Tooltip("Position for right hand index finger tip in world space.")]
        public Vector3 rightIndexTip = Vector3.zero;

        [Tooltip("Position for right hand middle finger tip in world space.")]
        public Vector3 rightMiddleTip = Vector3.zero;

        [Tooltip("Position for right hand ring finger tip in world space.")]
        public Vector3 rightRingTip = Vector3.zero;

        [Tooltip("Position for right hand little finger tip in world space.")]
        public Vector3 rightLittleTip = Vector3.zero;

        [Tooltip("Rotation for left hand thumb tip")]
        public Quaternion leftThumbRotation = Quaternion.identity;

        [Tooltip("Rotation for left hand index finger tip")]
        public Quaternion leftIndexRotation = Quaternion.identity;

        [Tooltip("Rotation for left hand middle finger tip")]
        public Quaternion leftMiddleRotation = Quaternion.identity;

        [Tooltip("Rotation for left hand ring finger tip")]
        public Quaternion leftRingRotation = Quaternion.identity;

        [Tooltip("Rotation for left hand little finger tip")]
        public Quaternion leftLittleRotation = Quaternion.identity;

        [Tooltip("Rotation for right hand thumb tip")]
        public Quaternion rightThumbRotation = Quaternion.identity;

        [Tooltip("Rotation for right hand index finger tip")]
        public Quaternion rightIndexRotation = Quaternion.identity;

        [Tooltip("Rotation for right hand middle finger tip")]
        public Quaternion rightMiddleRotation = Quaternion.identity;

        [Tooltip("Rotation for right hand ring finger tip")]
        public Quaternion rightRingRotation = Quaternion.identity;

        [Tooltip("Rotation for right hand little finger tip")]
        public Quaternion rightLittleRotation = Quaternion.identity;


        [Header("Debug & Preview")]
        [Tooltip("Show debug information during build.")]
        public bool debugMode = false;

        [Tooltip("Show preview visualization in Scene view.\n\n" +
                 "Displays contact points, rays, and calculated muscle values.")]
        public bool showPreview = false;

        [SerializeField] private string generatedGripInfo = "";
        public string GeneratedGripInfo => generatedGripInfo;

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;

        public void SetGeneratedInfo(string info)
        {
            generatedGripInfo = info;
        }
    }
}



