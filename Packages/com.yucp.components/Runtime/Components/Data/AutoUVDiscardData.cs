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
        [Tooltip("The region's name.")]
        public string name = "Region";
        
        [Tooltip("White areas are in the region; black areas are not.")]
        public Texture2D maskTexture;
        
        [Tooltip("Includes this region in the build.")]
        public bool enabled = true;
        
        [Tooltip("The region's color in previews.")]
        public Color debugColor = Color.white;
    }

    /// <summary>
    /// Automatically detects UV islands in clothing meshes and creates UV discard toggles
    /// for each detected region. No manual UV setup required.
    /// </summary>
    [SupportBanner]
    [BetaWarning("This component is in beta. Automatic UV region detection may miss regions.")]
    [AddComponentMenu("YUCP/Auto UV Discard")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    public class AutoUVDiscardData : MonoBehaviour, IEditorOnly
    {
        [Header("Target Body")]
        [Tooltip("The body mesh to hide under this clothing.")]
        public SkinnedMeshRenderer targetBodyMesh;

        [Tooltip("The body materials that get UV discard. Leave it empty to use every Poiyomi or FastFur material with UV support.")]
        public Material[] targetMaterials = new Material[0];

        [Header("Detection Mode")]
        [Tooltip("How this clothing is split into regions. Each region gets its own UV tile and global parameter.")]
        public DetectionMode detectionMode = DetectionMode.UVProximity;

        [Header("UV Proximity Settings")]
        [Tooltip("Picks the UV channel for discard automatically, preferring UV1. Turn it off to choose the channel yourself.")]
        public bool autoDetectUVChannel = true;

        [Tooltip("The UV channel used for discard. UV1 is recommended.")]
        [Range(0, 3)]
        public int uvChannel = 1;

        [Tooltip("Merges UV islands closer than this into one region. Higher gives fewer, larger regions.")]
        [Range(0f, 0.5f)]
        public float mergeTolerance = 0.05f;

        [Tooltip("Ignores regions smaller than this percentage of the mesh.")]
        [Range(0f, 20f)]
        public float minRegionSize = 1f;

        [Header("Mask Texture Settings")]
        [Tooltip("One mask per region. A region is the area where its mask is at or above the threshold.")]
        public List<MaskRegionDefinition> maskRegions = new List<MaskRegionDefinition>();

        [Tooltip("The UV channel the masks are sampled with.")]
        [Range(0, 3)]
        public int maskUVChannel = 0;

        [Tooltip("Mask values below this are left out of the region.")]
        [Range(0f, 1f)]
        public float maskThreshold = 0.5f;

        [Header("UV Seam Settings")]
        [Tooltip("The UV channel to find seams in.")]
        [Range(0, 3)]
        public int seamUVChannel = 0;

        [Tooltip("Edges whose UVs differ by more than this count as seams.")]
        [Range(0.001f, 1f)]
        public float seamThreshold = 0.01f;

        [Header("Sharp Edge Settings")]
        [Tooltip("Edges whose faces meet at more than this angle, in degrees, count as sharp.")]
        [Range(1f, 180f)]
        public float sharpAngleThreshold = 30f;

        [Tooltip("Uses sharp edges marked in Blender or Maya when the FBX has them.")]
        public bool useImportedSharpEdges = true;

        [Header("Blender Vertex Groups")]
        [Tooltip("Groups imported from the mesh. Use Import Vertex Groups to fill this.")]
        public List<BlenderVertexGroup> blenderVertexGroups = new List<BlenderVertexGroup>();

        [Tooltip("Includes a vertex in a group only at or above this weight.")]
        [Range(0f, 1f)]
        public float vertexGroupWeightThreshold = 0.5f;

        [Header("Bone Influence Settings")]
        [Tooltip("Each bone becomes a region of the vertices it moves.")]
        public List<Transform> targetBones = new List<Transform>();

        [Tooltip("Includes a vertex in a bone's region only at or above this weight.")]
        [Range(0f, 1f)]
        public float boneWeightThreshold = 0.5f;

        [Tooltip("Counts the weights of each bone's children toward its region.")]
        public bool includeChildBones = true;

        [Header("UV Tile Assignment")]
        [Tooltip("Gives each region its own UV tile at build, avoiding tiles other clothing on the same body uses. Turn it off to choose the starting tile yourself.")]
        [FormerlySerializedAs("autoAssignUDIMTile")]
        public bool autoAssignUVTile = true;

        [Tooltip("The UV tile row of the first region, 0 to 3. Later regions use the following tiles; -1 starts at row 3.")]
        [Range(-1, 3)]
        public int startRow = -1;

        [Tooltip("The UV tile column of the first region, 0 to 3. Later regions use the following tiles; -1 starts at column 0.")]
        [Range(-1, 3)]
        public int startColumn = -1;

        [Header("Global Parameter Settings")]
        [Tooltip("Each region's global parameter is this name plus _1, _2 and so on. Empty uses AutoUVDiscard.")]
        [FormerlySerializedAs("globalParameterBaseName")]
        public string globalParameterBaseName = "AutoUVDiscard";
        
        [Tooltip("Drives every region with one global parameter instead of one per region.")]
        public bool useSingleGlobalParameter = false;
        
        [Tooltip("The global parameter that drives every region. Empty uses AutoUVDiscard_All.")]
        [FormerlySerializedAs("singleGlobalParameterName")]
        public string singleGlobalParameterName = "AutoUVDiscard_All";

        [Header("Build Statistics (Read-only)")]
        [Tooltip("Regions found by the last build.")]
        [SerializeField] private int detectedRegions = 0;

        [Tooltip("UV tiles used by the last build.")]
        [FormerlySerializedAs("usedTiles")]
        [SerializeField] private List<string> usedUVTiles = new List<string>();

        public int DetectedRegions => detectedRegions;
        public List<string> UsedUVTiles => usedUVTiles;


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

