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
        [Tooltip("Automatically generate grip animation from object mesh.\n\n" +
                 "When enabled, analyzes object shape and avatar hand to create realistic grip.\n" +
                 "Disable to use custom animation clips instead.")]
        public bool autoGenerateGrip = true;

        [Tooltip("Custom grip animation for left hand (overrides auto-generation).\n\n" +
                 "Use this if you want manual control over the grip pose.")]
        public AnimationClip customGripLeft;

        [Tooltip("Custom grip animation for right hand (overrides auto-generation).\n\n" +
                 "Leave empty to mirror left grip if mirrorGrip is enabled.")]
        public AnimationClip customGripRight;

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

        [Tooltip("How to grip the object.\n\n" +
                 "• Auto: Detect based on object shape\n" +
                 "• Wrap: All fingers curl around object (default for most items)\n" +
                 "• Pinch: Thumb + index primarily (small objects)\n" +
                 "• Point: Index extended, others curl (gun grip)")]
        public GripStyle gripStyle = GripStyle.Auto;

        [Tooltip("Custom grip point on the object (optional).\n\n" +
                 "Drag an empty GameObject positioned where you want the hand to grip.\n" +
                 "If not set, uses object center.")]
        public Transform customGripPoint;

        [Header("Advanced Settings")]
        [Tooltip("Maximum distance to search for object contact (in meters).\n\n" +
                 "Should be slightly larger than your hand size.\n" +
                 "Typical range: 0.1-0.2m")]
        [Range(0.05f, 0.3f)]
        public float raycastDistance = 0.15f;

        [Tooltip("Safety margin - distance to stop before actual contact (in meters).\n\n" +
                 "Prevents finger mesh from touching object surface.\n" +
                 "• 0.001m: Minimal gap (1mm)\n" +
                 "• 0.002m: Recommended (2mm)\n" +
                 "• 0.005m: Larger gap (5mm)")]
        [Range(0.0005f, 0.01f)]
        public float contactSafetyMargin = 0.002f;

        [Tooltip("Mirror left hand grip to right hand.\n\n" +
                 "When enabled and no customGripRight is set, creates mirrored version of left grip.\n" +
                 "Useful for symmetric objects.")]
        public bool mirrorGrip = true;

        [Tooltip("Number of vertices to sample per finger segment.\n\n" +
                 "Higher = more accurate contact detection but slower.\n" +
                 "• 5: Fast, good for simple objects\n" +
                 "• 10: Balanced (recommended)\n" +
                 "• 20: Most accurate, slower")]
        [Range(5, 50)]
        public int vertexSamplingDensity = 10;

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



