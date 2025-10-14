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

        [Header("Detection Settings")]
        [Tooltip("UV channel to analyze for detecting regions.\n\n" +
                 "UV0 is the main texture channel.")]
        [Range(0, 3)]
        public int uvChannel = 0;

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
        [Tooltip("Starting UDIM tile for discard.\n\n" +
                 "Each detected region will be assigned to consecutive tiles.")]
        [Range(0, 3)]
        public int startRow = 3;

        [Range(0, 3)]
        public int startColumn = 0;

        [Header("Toggle Settings")]
        [Tooltip("Create individual toggles for each detected region.")]
        public bool createToggles = true;

        [Tooltip("Menu path prefix for toggles.\n\n" +
                 "Each region will be 'Prefix/Region 1', 'Prefix/Region 2', etc.")]
        public string toggleMenuPath = "Clothing/Body Parts";

        [Tooltip("Save toggle states across avatar reloads.")]
        public bool toggleSaved = true;

        [Tooltip("Use a single master toggle that controls all regions together.")]
        public bool useMasterToggle = false;

        [Tooltip("Master toggle menu path.")]
        public string masterTogglePath = "Clothing/Hide All Body";

        [Tooltip("Create parameter drivers instead of toggles.\n\n" +
                 "Useful for linking to other systems.")]
        public bool useParameterDriver = false;

        [Tooltip("Base parameter name for drivers.\n\n" +
                 "Each region will be 'ParameterBase_1', 'ParameterBase_2', etc.")]
        public string parameterBaseName = "BodyHide";

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

