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

    public enum GripStyle
    {
        Auto,
        Wrap,
        Pinch,
        Point
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

        [Header("Grip Generation")]
        [Tooltip("Show finger tip gizmos in Scene view for manual grip positioning.\n\n" +
                 "Click 'Show Gizmos' to display colored finger tip handles that you can drag to position.\n" +
                 "The system will calculate muscle values to reach these finger tip positions.")]
        public bool showGizmos = false;

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


        [Header("Grip Parameters")]
        [Tooltip("How much to curl fingers (0 = barely curl, 1 = full grip).\n\n" +
                 "• 0.3: Light touch\n" +
                 "• 0.7: Normal grip (recommended)\n" +
                 "• 1.0: Tight grip")]
        [Range(0f, 1f)]
        public float gripStrength = 0.7f;

        [Tooltip("Adjust finger spreading (-1 = fingers together, 1 = fingers spread apart).\n\n" +
                 "• -0.5: Fingers closer together\n" +
                 "• 0.0: Natural spread (recommended)\n" +
                 "• 0.5: Fingers spread wider")]
        [Range(-1f, 1f)]
        public float fingerSpread = 0.0f;


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



