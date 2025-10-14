using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    public enum DetectionMethod
    {
        Raycast,
        Proximity,
        Hybrid,
        Smart,
        Manual
    }

    public enum ApplicationMode
    {
        AutoDetect,
        UDIMDiscard,
        MeshDeletion
    }

    public enum ToggleType
    {
        ObjectToggle,
        HiddenToggle
    }

    [AddComponentMenu("YUCP/Auto Body Hider")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    public class AutoBodyHiderData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Target Meshes")]
        [Tooltip("The body mesh that should have parts hidden (usually the avatar body).")]
        public SkinnedMeshRenderer targetBodyMesh;

        [Tooltip("The clothing mesh that covers the body (usually the object this component is on).")]
        public SkinnedMeshRenderer clothingMesh;

        [Header("Detection Settings")]
        [Tooltip("Choose how to detect which body parts to hide.\n\n" +
                 "• Raycast: Fast single-direction raycasting (recommended)\n" +
                 "• Proximity: Distance-based (fastest)\n" +
                 "• Hybrid: Proximity + raycasting (balanced)\n" +
                 "• Smart: Multi-directional with bidirectional check (best for open clothing)\n" +
                 "• Manual: Use texture mask for full control")]
        public DetectionMethod detectionMethod = DetectionMethod.Raycast;

        [Tooltip("Safety margin - buffer inward from edges (in meters).\n\n" +
                 "Creates a buffer zone that REDUCES deletion near edges.\n" +
                 "Higher margin = LESS deletion (more conservative)\n\n" +
                 "• 0.00m: No margin (delete everything detected)\n" +
                 "• 0.01m: Small buffer (recommended)\n" +
                 "• 0.03m: Medium buffer (safer, less deletion)\n" +
                 "• 0.05m: Large buffer (very safe, minimal deletion)\n\n" +
                 "Prevents deleting polygons too close to visible edges.")]
        [Range(0.0f, 0.1f)]
        public float safetyMargin = 0.01f;

        [Tooltip("Distance in meters. Body vertices within this distance from clothing will be hidden.\n\n" +
                 "For tight clothing: 0.01-0.02m\n" +
                 "For loose clothing: 0.02-0.05m")]
        [Range(0.001f, 0.1f)]
        public float proximityThreshold = 0.02f;

        [Tooltip("Number of vertices to sample per unit area. Higher = more accurate but slower.\n\n" +
                 "Only used for certain detection methods. Usually can be left at default.")]
        [Range(1, 100)]
        public int samplingDensity = 10;

        [Tooltip("For raycast method: maximum distance to check for occlusion.\n\n" +
                 "Should be slightly larger than your clothing thickness.\n" +
                 "Typical range: 0.05-0.15m")]
        [Range(0.01f, 0.5f)]
        public float raycastDistance = 0.05f;

        [Tooltip("For hybrid method: how much to expand proximity detection before raycasting.\n\n" +
                 "Higher values = more initial candidates = slower but more thorough.\n" +
                 "1.5x is a good balance for most cases.")]
        [Range(1.0f, 3.0f)]
        public float hybridExpansionFactor = 1.5f;

        [Header("Smart Detection Settings")]
        [Tooltip("Number of ray directions to test per vertex (more = more accurate but slower).\n\n" +
                 "• 4 rays: Very fast, basic coverage\n" +
                 "• 8 rays: Balanced (recommended)\n" +
                 "• 12-16 rays: Most accurate, slower\n\n" +
                 "Use higher values for complex clothing with many openings.")]
        [Range(4, 16)]
        public int smartRayDirections = 8;

        [Tooltip("Percentage of rays that must hit clothing for vertex to be hidden (0.0 - 1.0).\n\n" +
                 "• 0.5 (50%): More aggressive hiding\n" +
                 "• 0.75 (75%): Balanced (recommended)\n" +
                 "• 0.9 (90%): Conservative, less hiding\n\n" +
                 "Lower values hide more; higher values are safer for open clothing.")]
        [Range(0.0f, 1.0f)]
        public float smartOcclusionThreshold = 0.75f;

        [Tooltip("Consider surface normals when determining occlusion.\n\n" +
                 "When enabled, only tests rays facing outward from the body surface.\n" +
                 "Recommended: ON (prevents testing rays pointing into the body)")]
        public bool smartUseNormals = true;

        [Tooltip("Require bidirectional occlusion (clothing must surround the vertex).\n\n" +
                 "When enabled, checks if clothing blocks the vertex from BOTH sides.\n" +
                 "Essential for open jackets and clothing with gaps.\n" +
                 "Recommended: ON for any clothing with openings")]
        public bool smartRequireBidirectional = true;
        
        [Tooltip("Ray offset distance from body surface (in meters).\n\n" +
                 "Prevents false positives by starting rays slightly away from the body.\n" +
                 "• 0.001m: Minimal offset\n" +
                 "• 0.005m: Recommended for most cases\n" +
                 "• 0.01m: Higher offset for problem areas\n\n" +
                 "Increase if visible areas are being deleted incorrectly.")]
        [Range(0.0001f, 0.02f)]
        public float smartRayOffset = 0.005f;
        
        [Tooltip("Conservative mode - more strict about hiding vertices.\n\n" +
                 "When enabled:\n" +
                 "• Increases occlusion threshold automatically\n" +
                 "• Requires more rays to agree before hiding\n" +
                 "• Adds distance-based weighting (closer = more likely to hide)\n\n" +
                 "Use when visible polygons are being deleted incorrectly.")]
        public bool smartConservativeMode = false;
        
        [Tooltip("Minimum distance from clothing to hide (in meters).\n\n" +
                 "Vertices must be at least this close to clothing to be hidden.\n" +
                 "• 0.0m: No minimum (hide anything occluded)\n" +
                 "• 0.01m: Only hide if very close to clothing\n" +
                 "• 0.05m: Conservative (recommended for open clothing)\n\n" +
                 "Higher values are safer but may leave visible body parts.")]
        [Range(0.0f, 0.1f)]
        public float smartMinDistanceToClothing = 0.01f;

        [Header("Manual Mask (Manual Method Only)")]
        [Tooltip("Manually painted texture mask. White = hide, Black = show. Use UV0 of body mesh.\n\n" +
                 "Create a texture where:\n" +
                 "• White/bright areas = hide these vertices\n" +
                 "• Black/dark areas = keep these vertices visible\n\n" +
                 "Paint directly on your body UV layout.")]
        public Texture2D manualMask;

        [Tooltip("Threshold for manual mask. Pixels brighter than this will be hidden.\n\n" +
                 "• 0.1: Hide almost everything (aggressive)\n" +
                 "• 0.5: Balanced (recommended)\n" +
                 "• 0.9: Only hide very bright areas (conservative)")]
        [Range(0f, 1f)]
        public float manualMaskThreshold = 0.5f;

        [Header("Application Mode")]
        [Tooltip("How to hide the body parts. Auto-detect will choose based on shader.\n\n" +
                 "• Auto-Detect: Automatically uses UDIM for Poiyomi, mesh deletion for others\n" +
                 "• UDIM Discard: Non-destructive, requires Poiyomi shader\n" +
                 "• Mesh Deletion: Works with any shader, reduces poly count")]
        public ApplicationMode applicationMode = ApplicationMode.AutoDetect;

        [Header("UDIM Discard Settings (Poiyomi Only)")]
        [Tooltip("Which UV channel to use for UDIM discard.\n\n" +
                 "• UV0 (Channel 0): Main UV, most common\n" +
                 "• UV1-3: Alternative channels if UV0 is used for other purposes\n\n" +
                 "Usually leave at 0 unless you know your body mesh uses a different channel.")]
        [Range(0, 3)]
        public int udimUVChannel = 0;

        [Tooltip("Which UDIM tile row to use for discarding (0-3).\n\n" +
                 "Poiyomi will hide vertices with UVs in this tile.\n" +
                 "Row 3 (default) is usually safe as it's rarely used for textures.")]
        [Range(0, 3)]
        public int udimDiscardRow = 3;

        [Tooltip("Which UDIM tile column to use for discarding (0-3).\n\n" +
                 "Poiyomi will hide vertices with UVs in this tile.\n" +
                 "Column 3 (default) is usually safe as it's rarely used for textures.")]
        [Range(0, 3)]
        public int udimDiscardColumn = 3;

        [Header("UDIM Toggle Settings (Optional)")]
        [Tooltip("Create a toggle to enable/disable the body hiding effect.\n\n" +
                 "When enabled, adds a menu toggle that can turn the UDIM discard on/off.\n\n" +
                 "Note: Only works with UDIM Discard mode, not Mesh Deletion.")]
        public bool createToggle = false;

        [Tooltip("Save generated animation clip as asset for debugging.\n\n" +
                 "When enabled, the animation will be saved to Assets/Generated for inspection.")]
        public bool debugSaveAnimation = false;

        [Tooltip("Toggle type:\n\n" +
                 "• Object Toggle: Toggles clothing object ON/OFF + UDIM discard\n" +
                 "  (Clothing visible = body hidden, clothing hidden = body visible)\n\n" +
                 "• Hidden Toggle: Only toggles UDIM discard, clothing always visible\n" +
                 "  (Useful for toggling body hiding while keeping clothing on)")]
        public ToggleType toggleType = ToggleType.ObjectToggle;

        [Tooltip("Menu path for the toggle (e.g., 'Clothing/Hide Body').\n\n" +
                 "This is where the toggle will appear in your avatar menu.\n" +
                 "Leave empty to use global parameter control only (no menu item).")]
        public string toggleMenuPath = "Clothing/Hide Body";

        [Tooltip("Save toggle state across avatar reloads.")]
        public bool toggleSaved = true;

        [Tooltip("Toggle starts in the ON state by default.\n\n" +
                 "When ON: Clothing visible, body hidden (discard applied)\n" +
                 "When OFF: Clothing hidden, body visible (no discard)\n\n" +
                 "Note: Due to VRCFury's resting state system, this MUST be false for material animations to work correctly.")]
        public bool toggleDefaultOn = false;

        [Tooltip("Sync toggle state across all players in the instance.\n\n" +
                 "When OFF (default):\n" +
                 "• Toggle state is local to your client only\n" +
                 "• VRCFury creates a local parameter (not synced)\n\n" +
                 "When ON:\n" +
                 "• Toggle state is synced across all players\n" +
                 "• VRCFury auto-generates a synced parameter name, or uses the one you specify below\n" +
                 "• Useful for outfit coordination or showing clothing state to others")]
        public bool toggleSynced = false;

        [Tooltip("OPTIONAL: Custom parameter name for synced toggle.\n\n" +
                 "When EMPTY:\n" +
                 "• VRCFury auto-generates a unique parameter name\n" +
                 "• Each toggle gets its own synced parameter\n\n" +
                 "When SET:\n" +
                 "• Uses this exact parameter name\n" +
                 "• Multiple clothing pieces can share the same parameter for outfit groups\n\n" +
                 "Examples:\n" +
                 "• 'Outfit1' - All Outfit 1 pieces share this (outfit group)\n" +
                 "• 'JacketSync' - Specific name for this jacket\n\n" +
                 "Advanced: If menuPath is also empty, ONLY this parameter controls the toggle (no menu item).")]
        public string toggleParameterName = "";

        [Header("Advanced Toggle Options")]
        [Tooltip("Use slider instead of button.")]
        public bool toggleSlider = false;

        [Tooltip("Hold button instead of latching toggle.")]
        public bool toggleHoldButton = false;

        [Tooltip("Enable exclusive off state (only one toggle in the group can be on at a time).")]
        public bool toggleExclusiveOffState = false;

        [Tooltip("Enable exclusive tags (mutually exclusive with other toggles sharing the same tags).")]
        public bool toggleEnableExclusiveTag = false;

        [Tooltip("Exclusive tags (comma-separated). Toggles sharing tags are mutually exclusive.")]
        public string toggleExclusiveTag = "";

        [Tooltip("Enable custom menu icon.")]
        public bool toggleEnableIcon = false;

        [Tooltip("Custom menu icon texture.")]
        public Texture2D toggleIcon;

        [Header("Advanced Options")]
        [Tooltip("If true, only process vertices that are weighted to specific bones.\n\n" +
                 "Useful if you want to limit which body parts can be hidden.\n" +
                 "For example: only hide chest/torso bones but not head/limbs.")]
        public bool useBoneFiltering = false;

        [Tooltip("Only hide vertices weighted to these bones (if bone filtering is enabled).\n\n" +
                 "Drag bones from your avatar armature into this list.\n" +
                 "Only vertices influenced by these bones will be considered for hiding.")]
        public Transform[] filterBones = new Transform[0];

        [Tooltip("If true, mirror the detection across the X axis (for symmetric clothing).\n\n" +
                 "When enabled, if a vertex on one side is hidden, its mirror on the opposite side is also hidden.\n" +
                 "Useful for symmetric jackets, shirts, etc.")]
        public bool mirrorSymmetry = false;

        [Header("Multi-Clothing Optimization")]
        [Tooltip("Optimize UDIM tile usage for multiple clothing pieces (UDIM Discard mode only).\n\n" +
                 "When enabled:\n" +
                 "• Clothing is processed from most coverage to least coverage\n" +
                 "• Outer layers 'claim' body areas first\n" +
                 "• Inner layers without toggles that are fully covered won't get tiles\n" +
                 "• Dramatically reduces tile usage for layered outfits\n\n" +
                 "Example: If jacket covers entire shirt:\n" +
                 "  ON: Jacket gets tile, shirt skipped (saves 1 tile)\n" +
                 "  OFF: Both get tiles (works like before)\n\n" +
                 "Recommended: ON for complex multi-layer outfits")]
        public bool optimizeTileUsage = true;

        [Header("Debug & Preview")]
        [Tooltip("Show debug information during build.\n\n" +
                 "Displays:\n" +
                 "• Cache hit/miss status\n" +
                 "• Vertex counts\n" +
                 "• Detection time\n" +
                 "• Applied settings\n\n" +
                 "Enable this if you're troubleshooting or optimizing settings.")]
        public bool debugMode = false;
        
        [Tooltip("Show preview visualization in Scene view.\n\n" +
                 "When enabled, displays:\n" +
                 "• Red spheres = vertices that will be hidden\n" +
                 "• Green spheres = vertices that will remain visible\n" +
                 "• Ray directions (if using Smart detection)\n\n" +
                 "Use 'Generate Preview' button to test without building.")]
        public bool showPreview = false;

        [Tooltip("The number of vertices that were hidden (read-only, populated at build time).")]
        [SerializeField] private int hiddenVertexCount = 0;

        [Tooltip("The application mode that was used (read-only, populated at build time).")]
        [SerializeField] private string appliedMode = "";

        public int HiddenVertexCount => hiddenVertexCount;
        public string AppliedMode => appliedMode;
        
        [System.NonSerialized] public bool[] previewHiddenVertices;
        [System.NonSerialized] public Vector3[] previewVertexPositions;
        [System.NonSerialized] public bool[] previewHiddenFaces;
        [System.NonSerialized] public int[] previewTriangles;
        [System.NonSerialized] public bool previewGenerated = false;
        
        [System.NonSerialized] public bool[] previewRawHiddenVertices;
        [System.NonSerialized] public Vector3[] previewLocalVertices;
        [System.NonSerialized] public float lastPreviewSafetyMargin = -1f;
        [System.NonSerialized] public bool lastPreviewMirrorSymmetry = false;

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;

        public void SetBuildStats(int vertexCount, string mode)
        {
            hiddenVertexCount = vertexCount;
            appliedMode = mode;
        }
    }
}

