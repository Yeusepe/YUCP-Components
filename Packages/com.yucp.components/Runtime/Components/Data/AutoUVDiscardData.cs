using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using VRC.SDKBase;

namespace YUCP.Components
{
    /// <summary>
    /// Detection mode for identifying UV regions to create UV discard toggles.
    /// </summary>
    public enum DetectionMode
    {
        /// <summary>Cluster vertices by UV proximity (current default behavior)</summary>
        UVProximity = 0,
        /// <summary>Use a texture mask to define regions</summary>
        MaskTexture = 1,
        /// <summary>Detect UV seams from mesh data</summary>
        UVSeams = 2,
        /// <summary>Use sharp edges/creases as region boundaries</summary>
        SharpEdges = 3,
        /// <summary>Import vertex groups from Blender FBX/glTF</summary>
        BlenderVertexGroups = 4,
        /// <summary>Separate by material slots on clothing mesh</summary>
        MaterialSlots = 5,
        /// <summary>Group by influencing armature bones</summary>
        BoneInfluence = 6
    }

    /// <summary>
    /// Represents a vertex group imported from Blender.
    /// </summary>
    [System.Serializable]
    public class BlenderVertexGroup
    {
        public string name;
        public List<VertexWeight> weights = new List<VertexWeight>();
        public bool enabled = true;
        public Color debugColor = Color.white;
    }

    /// <summary>
    /// Weight of a vertex in a vertex group.
    /// </summary>
    [System.Serializable]
    public class VertexWeight
    {
        public int vertexIndex;
        public float weight;
    }

    /// <summary>
    /// Defines a mask region for mask-based detection.
    /// </summary>
    [System.Serializable]
    public class MaskRegionDefinition
    {
        [Tooltip("Display name for this region")]
        public string name = "Region";
        
        [Tooltip("Mask texture for this region. White = included, Black = excluded.")]
        public Texture2D maskTexture;
        
        [Tooltip("Enable or disable this region")]
        public bool enabled = true;
        
        [Tooltip("Debug color for preview")]
        public Color debugColor = Color.white;
    }

    /// <summary>
    /// Automatically detects UV islands in clothing meshes and creates UV discard toggles
    /// for each detected region. No manual UV setup required.
    /// </summary>
    [SupportBanner]
    [BetaWarning("This component is in BETA and may not work as intended. Automatic UV region detection is experimental.")]
    [AddComponentMenu("YUCP/Auto UV Discard")]
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    public class AutoUVDiscardData : MonoBehaviour, IEditorOnly
    {
        [Header("Target Body")]
        [Tooltip("The body mesh renderer that should be hidden under this clothing.")]
        public SkinnedMeshRenderer targetBodyMesh;

        [Tooltip("Optional: Select specific material(s) from the body mesh to configure.\n\n" +
                 "If empty, the component will automatically find all compatible materials (Poiyomi/FastFur).\n" +
                 "You can select multiple materials if your body mesh uses multiple Poiyomi/FastFur materials.\n" +
                 "All selected materials will have UV discard configured.")]
        public Material[] targetMaterials = new Material[0];

        [Header("Detection Mode")]
        [Tooltip("Method to use for detecting UV regions.\n\n" +
                 "• UV Proximity: Cluster by UV distance (default)\n" +
                 "• Mask Texture: Use a mask to define regions\n" +
                 "• UV Seams: Detect UV unwrapping seams\n" +
                 "• Sharp Edges: Use mesh sharp edges as boundaries\n" +
                 "• Blender Vertex Groups: Import vertex groups from FBX\n" +
                 "• Material Slots: Each material = one region\n" +
                 "• Bone Influence: Group by armature bones")]
        public DetectionMode detectionMode = DetectionMode.UVProximity;

        [Header("UV Proximity Settings")]
        [Tooltip("Automatically detect the best UV channel for discard.\n\n" +
                 "The system will prefer UV1 (where discard coordinates are written) and fall back to UV0 if needed.\n" +
                 "Disable this to manually specify a UV channel in Advanced Options.")]
        public bool autoDetectUVChannel = true;

        [Tooltip("Which UV channel to use for UV discard (only used when Auto Detect is disabled).\n\n" +
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

        [Header("Mask Texture Settings")]
        [Tooltip("List of mask textures. Each mask defines a separate region.\n" +
                 "White areas in each mask will be detected as that region.")]
        public List<MaskRegionDefinition> maskRegions = new List<MaskRegionDefinition>();

        [Tooltip("UV channel to sample masks from.")]
        [Range(0, 3)]
        public int maskUVChannel = 0;

        [Tooltip("Threshold for mask sampling. Values below this are ignored.")]
        [Range(0f, 1f)]
        public float maskThreshold = 0.5f;

        [Header("UV Seam Settings")]
        [Tooltip("UV channel to analyze for seams.")]
        [Range(0, 3)]
        public int seamUVChannel = 0;

        [Tooltip("Minimum UV distance to consider as a seam.\n" +
                 "Edges where UV coordinates differ by more than this are considered seams.")]
        [Range(0.001f, 1f)]
        public float seamThreshold = 0.01f;

        [Header("Sharp Edge Settings")]
        [Tooltip("Angle threshold for sharp edges (degrees).\n" +
                 "Edges with angle between faces greater than this are considered sharp.")]
        [Range(1f, 180f)]
        public float sharpAngleThreshold = 30f;

        [Tooltip("Try to import sharp edge data from FBX (Blender/Maya marked edges).")]
        public bool useImportedSharpEdges = true;

        [Header("Blender Vertex Groups")]
        [Tooltip("Imported vertex groups from FBX. Click 'Import Groups' to populate.")]
        public List<BlenderVertexGroup> blenderVertexGroups = new List<BlenderVertexGroup>();

        [Tooltip("Minimum weight threshold for including a vertex in a group.")]
        [Range(0f, 1f)]
        public float vertexGroupWeightThreshold = 0.5f;

        [Header("Bone Influence Settings")]
        [Tooltip("Target bones to use for grouping. Each bone becomes a region.")]
        public List<Transform> targetBones = new List<Transform>();

        [Tooltip("Minimum bone weight to include a vertex in that bone's region.")]
        [Range(0f, 1f)]
        public float boneWeightThreshold = 0.5f;

        [Tooltip("Include child bones when grouping vertices.")]
        public bool includeChildBones = true;

        [Header("UV Tile Assignment")]
        [Tooltip("Automatically assign UV tile row/column via the orchestrator.\n\n" +
                 "When enabled:\n" +
                 "• The orchestrator automatically assigns unique tiles to each detected region\n" +
                 "• Prevents tile conflicts when multiple components share the same body mesh\n" +
                 "• Tile assignment is optimized for multiple regions\n\n" +
                 "When disabled:\n" +
                 "• You can manually specify the starting tile row/column in Advanced Options\n" +
                 "• Use when you need specific tile assignments for compatibility reasons")]
        [FormerlySerializedAs("autoAssignUDIMTile")]
        public bool autoAssignUVTile = true;

        [Tooltip("Starting UV tile row for discard (0-3).\n\n" +
                 "Only used when 'Auto Assign UV Tile' is disabled.\n" +
                 "Each detected region will be assigned to consecutive tiles starting from this position.\n" +
                 "When auto-assigned, this value is set by the orchestrator.")]
        [Range(-1, 3)]
        public int startRow = -1;

        [Tooltip("Starting UV tile column for discard (0-3).\n\n" +
                 "Only used when 'Auto Assign UV Tile' is disabled.\n" +
                 "Each detected region will be assigned to consecutive tiles starting from this position.\n" +
                 "When auto-assigned, this value is set by the orchestrator.")]
        [Range(-1, 3)]
        public int startColumn = -1;

        [Header("Global Parameter Settings")]
        [Tooltip("Base name for global parameters.\n\n" +
                 "Each detected region will get a global parameter: 'BaseName_1', 'BaseName_2', etc.\n" +
                 "These parameters will be registered with VRCFury and can be controlled by VRChat worlds or external sources.\n" +
                 "Leave empty to auto-generate parameter names.")]
        [FormerlySerializedAs("globalParameterBaseName")]
        public string globalParameterBaseName = "AutoUVDiscard";
        
        [Tooltip("Use a single global parameter that controls all regions together.\n\n" +
                 "When enabled, all regions share one global parameter.\n" +
                 "When disabled, each region gets its own global parameter.")]
        public bool useSingleGlobalParameter = false;
        
        [Tooltip("Single global parameter name (when using single parameter mode).\n\n" +
                 "Only used when 'Use Single Global Parameter' is enabled.")]
        [FormerlySerializedAs("singleGlobalParameterName")]
        public string singleGlobalParameterName = "AutoUVDiscard_All";

        [Header("Advanced Options")]
        [Tooltip("Preview detected regions in the scene view.")]
        public bool showPreview = true;

        [Tooltip("Color coding for preview regions.")]
        public bool useColorCoding = true;

        [Header("Build Statistics (Read-only)")]
        [Tooltip("Number of regions detected (populated at build time).")]
        [SerializeField] private int detectedRegions = 0;

        [Tooltip("UV tiles used (populated at build time).")]
        [FormerlySerializedAs("usedTiles")]
        [SerializeField] private List<string> usedUVTiles = new List<string>();

        public int DetectedRegions => detectedRegions;
        public List<string> UsedUVTiles => usedUVTiles;

        // Preview data
        [System.NonSerialized] public List<UVRegion> previewRegions;
        [System.NonSerialized] public bool previewGenerated = false;

        public void SetBuildStats(int regions, List<string> tiles)
        {
            detectedRegions = regions;
            usedUVTiles = new List<string>(tiles);
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

