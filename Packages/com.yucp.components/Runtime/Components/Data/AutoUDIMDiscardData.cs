using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    /// <summary>
    /// Automatically detects UV islands in clothing meshes and creates UDIM discard toggles
    /// for each detected region. No manual UV setup required.
    /// </summary>
    [BetaWarning("This component is in BETA and may not work as intended. Automatic UV region detection is experimental.")]
    [AddComponentMenu("YUCP/Auto UDIM Discard")]
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    public class AutoUDIMDiscardData : MonoBehaviour, IEditorOnly
    {
        [Header("Target Body")]
        [Tooltip("The body mesh renderer that should be hidden under this clothing.")]
        public SkinnedMeshRenderer targetBodyMesh;

        [Tooltip("Optional: Select specific material(s) from the body mesh to configure.\n\n" +
                 "If empty, the component will automatically find all compatible materials (Poiyomi/FastFur).\n" +
                 "You can select multiple materials if your body mesh uses multiple Poiyomi/FastFur materials.\n" +
                 "All selected materials will have UDIM discard configured.")]
        public Material[] targetMaterials = new Material[0];

        [Header("Detection Settings")]
        [Tooltip("Automatically detect the best UV channel for discard.\n\n" +
                 "The system will prefer UV1 (where discard coordinates are written) and fall back to UV0 if needed.\n" +
                 "Disable this to manually specify a UV channel in Advanced Options.")]
        public bool autoDetectUVChannel = true;

        [Tooltip("Which UV channel to use for UDIM discard (only used when Auto Detect is disabled).\n\n" +
                 "• UV1 (Channel 1): Recommended - where discard coordinates are written\n" +
                 "• UV0 (Channel 0): Main texture UV, use only if UV1 is unavailable\n" +
                 "• UV2-3: Alternative channels if needed\n\n" +
                 "Note: The system always writes discard coordinates to UV1, so UV1 is the recommended channel.")]
        [Range(0, 3)]
        public int uvChannel = 1;

        [Tooltip("Merge UV islands that are close together into single regions.\n\n" +
                 "Higher values = fewer, larger regions.\n" +
                 "Lower values = more, smaller regions.")]
        [Range(0f, 0.5f)]
        public float mergeTolerance = 0.05f;

        [Tooltip("Minimum percentage of mesh that a region must cover to be included.\n\n" +
                 "Filters out tiny UV islands that are probably not important.")]
        [Range(0f, 20f)]
        public float minRegionSize = 1f;

        [Header("UDIM Tile Assignment")]
        [Tooltip("Automatically assign UDIM tile row/column via the orchestrator.\n\n" +
                 "When enabled:\n" +
                 "• The orchestrator automatically assigns unique tiles to each detected region\n" +
                 "• Prevents tile conflicts when multiple components share the same body mesh\n" +
                 "• Tile assignment is optimized for multiple regions\n\n" +
                 "When disabled:\n" +
                 "• You can manually specify the starting tile row/column in Advanced Options\n" +
                 "• Use when you need specific tile assignments for compatibility reasons")]
        public bool autoAssignUDIMTile = true;

        [Tooltip("Starting UDIM tile row for discard (0-3).\n\n" +
                 "Only used when 'Auto Assign UDIM Tile' is disabled.\n" +
                 "Each detected region will be assigned to consecutive tiles starting from this position.\n" +
                 "When auto-assigned, this value is set by the orchestrator.")]
        [Range(-1, 3)]
        public int startRow = -1;

        [Tooltip("Starting UDIM tile column for discard (0-3).\n\n" +
                 "Only used when 'Auto Assign UDIM Tile' is disabled.\n" +
                 "Each detected region will be assigned to consecutive tiles starting from this position.\n" +
                 "When auto-assigned, this value is set by the orchestrator.")]
        [Range(-1, 3)]
        public int startColumn = -1;

        [Header("Global Parameter Settings")]
        [Tooltip("Base name for global parameters.\n\n" +
                 "Each detected region will get a global parameter: 'BaseName_1', 'BaseName_2', etc.\n" +
                 "These parameters will be registered with VRCFury and can be controlled by VRChat worlds or external sources.\n" +
                 "Leave empty to auto-generate parameter names.")]
        public string globalParameterBaseName = "AutoUDIMDiscard";
        
        [Tooltip("Use a single global parameter that controls all regions together.\n\n" +
                 "When enabled, all regions share one global parameter.\n" +
                 "When disabled, each region gets its own global parameter.")]
        public bool useSingleGlobalParameter = false;
        
        [Tooltip("Single global parameter name (when using single parameter mode).\n\n" +
                 "Only used when 'Use Single Global Parameter' is enabled.")]
        public string singleGlobalParameterName = "AutoUDIMDiscard_All";

        [Header("Advanced Options")]
        [Tooltip("Preview detected regions in the scene view.")]
        public bool showPreview = true;

        [Tooltip("Color coding for preview regions.")]
        public bool useColorCoding = true;

        [Header("Build Statistics (Read-only)")]
        [Tooltip("Number of regions detected (populated at build time).")]
        [SerializeField] private int detectedRegions = 0;

        [Tooltip("UDIM tiles used (populated at build time).")]
        [SerializeField] private List<string> usedTiles = new List<string>();

        public int DetectedRegions => detectedRegions;
        public List<string> UsedTiles => usedTiles;

        // Preview data
        [System.NonSerialized] public List<UVRegion> previewRegions;
        [System.NonSerialized] public bool previewGenerated = false;

        public void SetBuildStats(int regions, List<string> tiles)
        {
            detectedRegions = regions;
            usedTiles = new List<string>(tiles);
        }

        [System.Serializable]
        public class UVRegion
        {
            public List<int> vertexIndices = new List<int>();
            public Bounds uvBounds;
            public Vector2 uvCenter;
            public int assignedRow;
            public int assignedColumn;
            public string name;
            public Color debugColor;
        }
    }
}

