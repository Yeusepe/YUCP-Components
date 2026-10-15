using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
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
        [FormerlySerializedAs("UDIMDiscard")]
        UVDiscard,
        MeshDeletion
    }

    public enum ToggleType
    {
        ObjectToggle,
        HiddenToggle
    }

    [SupportBanner]
    [AddComponentMenu("YUCP/Auto Body Hider")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    public class AutoBodyHiderData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Target Meshes")]
        [Tooltip("The body mesh to hide parts of, usually the avatar's body.")]
        public SkinnedMeshRenderer targetBodyMesh;

        [Tooltip("The clothing that covers the body. Add every layer, such as a shirt and a jacket, to detect them together.")]
        public SkinnedMeshRenderer[] clothingMeshes = new SkinnedMeshRenderer[0];

        [Header("Detection Settings")]
        [Tooltip("How covered body parts are found. Raycast suits most clothing, Smart suits open clothing, and Manual uses a painted mask.")]
        public DetectionMethod detectionMethod = DetectionMethod.Raycast;

        [Tooltip("Keeps body faces within this distance of a clothing edge visible, in meters. Raise it if gaps show at the clothing's edges.")]
        [Range(0.0f, 0.1f)]
        public float safetyMargin = 0.01f;

        [Tooltip("Hides body vertices within this distance of the clothing, in meters. Use about 0.01 to 0.02 for tight clothing and up to 0.05 for loose clothing.")]
        [Range(0.001f, 0.1f)]
        public float proximityThreshold = 0.02f;

        [Tooltip("Samples per unit area for some detection methods. Higher is more accurate but slower.")]
        [Range(1, 100)]
        public int samplingDensity = 10;

        [Tooltip("How far to look for clothing over each body vertex, in meters. Set it a little above the clothing's thickness.")]
        [Range(0.01f, 0.5f)]
        public float raycastDistance = 0.05f;

        [Tooltip("How much wider the proximity pass looks before raycasting. Higher finds more candidates but is slower.")]
        [Range(1.0f, 3.0f)]
        public float hybridExpansionFactor = 1.5f;

        [Header("Smart Detection Settings")]
        [Tooltip("Rays cast from each vertex. More rays are more accurate but slower; use 12 to 16 for clothing with many openings.")]
        [Range(4, 16)]
        public int smartRayDirections = 8;

        [Tooltip("The share of rays that must hit clothing to hide a vertex. Lower hides more; higher is safer for open clothing.")]
        [Range(0.0f, 1.0f)]
        public float smartOcclusionThreshold = 0.75f;

        [Tooltip("Casts rays only outward from the body surface.")]
        public bool smartUseNormals = true;

        [Tooltip("Hides a vertex only when clothing covers it from both sides. Use it for open jackets and clothing with gaps.")]
        public bool smartRequireBidirectional = true;
        
        [Tooltip("Starts rays this far from the body, in meters. Raise it if visible areas are hidden by mistake.")]
        [Range(0.0001f, 0.02f)]
        public float smartRayOffset = 0.005f;
        
        [Tooltip("Hides fewer vertices: more rays must agree, and nearer clothing counts more. Use it if visible areas are hidden by mistake.")]
        public bool smartConservativeMode = false;
        
        [Tooltip("Only hides vertices within this distance of the clothing, in meters. At 0, any vertex the rays find covered is hidden.")]
        [Range(0.0f, 0.1f)]
        public float smartMinDistanceToClothing = 0.01f;

        [Header("Manual Mask (Manual Method Only)")]
        [Tooltip("A mask painted on the body's UV0 layout. White areas are hidden; black areas stay visible.")]
        public Texture2D manualMask;

        [Tooltip("Hides the areas of the mask brighter than this. Lower hides more.")]
        [Range(0f, 1f)]
        public float manualMaskThreshold = 0.5f;

        [Header("Application Mode")]
        [Tooltip("How hidden faces are removed. Automatic uses UV Discard on Poiyomi and FastFur materials and Mesh Deletion on others.")]
        public ApplicationMode applicationMode = ApplicationMode.AutoDetect;
        
        [Tooltip("The body materials that get UV discard. Leave it empty to use every Poiyomi or FastFur material with UV support.")]
        public Material[] targetMaterials = new Material[0];

        [Header("UV Discard Settings (Poiyomi/FastFur)")]
        [Tooltip("Picks the UV channel for discard automatically, preferring UV1. Turn it off to choose the channel yourself.")]
        public bool autoDetectUVChannel = true;
        
        [Tooltip("The UV channel used for discard. UV1 is recommended.")]
        [Range(0, 3)]
        [FormerlySerializedAs("udimUVChannel")]
        public int uvChannel = 1;

        [Tooltip("Gives each clothing piece on the same body its own UV tile at build. Turn it off to choose the tile yourself.")]
        [FormerlySerializedAs("autoAssignUDIMTile")]
        public bool autoAssignUVTile = true;

        [Tooltip("The UV tile row to discard, 0 to 3. Make sure no other clothing on the same body uses this tile.")]
        [Range(0, 3)]
        [FormerlySerializedAs("udimDiscardRow")]
        public int uvDiscardRow = 3;

        [Tooltip("The UV tile column to discard, 0 to 3. Make sure no other clothing on the same body uses this tile.")]
        [Range(0, 3)]
        [FormerlySerializedAs("udimDiscardColumn")]
        public int uvDiscardColumn = 3;

        [Header("UV Toggle Settings (Optional)")]
        [Tooltip("Adds body hiding to a VRCFury toggle on this object or a child instead of creating a toggle.")]
        public bool useExistingToggle = false;
        
        [Tooltip("The VRCFury toggle that turns body hiding on and off. Automatic uses the first toggle on this object or its children.")]
        public Component selectedToggle;
        
        [Tooltip("Adds an avatar menu toggle that turns body hiding on and off. Needs UV discard.")]
        public bool createToggle = false;

        [Tooltip("Saves the generated toggle animation to Assets/Generated for inspection.")]
        public bool debugSaveAnimation = false;

        [Tooltip("Object Toggle turns the clothing and body hiding on and off together. Hidden Toggle turns only body hiding on and off.")]
        public ToggleType toggleType = ToggleType.ObjectToggle;

        [Tooltip("Where the toggle appears in the avatar menu, such as Clothing/Hide Body. Leave it empty for a toggle driven only by its global parameter.")]
        public string toggleMenuPath = "Clothing/Hide Body";

        [Tooltip("Remembers the toggle's state when you change worlds or reload the avatar.")]
        public bool toggleSaved = true;

        [Tooltip("Starts with the toggle on: clothing visible and body hidden. Leave it off for material animations to work with VRCFury.")]
        public bool toggleDefaultOn = false;

        [Tooltip("Syncs the toggle's state to other players. Off keeps it local to you.")]
        public bool toggleSynced = false;

        [Tooltip("The global parameter that drives the toggle. Leave it empty for a generated name, or share a name to group clothing into an outfit.")]
        public string toggleParameterName = "";

        [Header("Advanced Toggle Options")]
        [Tooltip("Shows a slider instead of a button.")]
        public bool toggleSlider = false;

        [Tooltip("Stays on only while held.")]
        public bool toggleHoldButton = false;

        [Tooltip("Makes this toggle the one that's on when every other toggle with the same exclusive tags is off.")]
        public bool toggleExclusiveOffState = false;

        [Tooltip("Turns off other toggles that share a tag with this one when it turns on.")]
        public bool toggleEnableExclusiveTag = false;

        [Tooltip("Comma-separated tags. Toggles that share a tag can't be on at the same time.")]
        public string toggleExclusiveTag = "";

        [Tooltip("Shows a custom icon in the avatar menu.")]
        public bool toggleEnableIcon = false;

        [Tooltip("The avatar menu icon.")]
        public Texture2D toggleIcon;

        [Header("Advanced Options")]
        [Tooltip("Hides only vertices weighted to the filter bones. Use it to keep parts such as the head or limbs visible.")]
        public bool useBoneFiltering = false;

        [Tooltip("The bones whose vertices can be hidden.")]
        public Transform[] filterBones = new Transform[0];

        [Tooltip("Also hides the mirrored vertex on the other side of the X axis. Use it for symmetric clothing.")]
        public bool mirrorSymmetry = false;

        [Header("Multi-Clothing Optimization")]
        [Tooltip("Saves UV tiles on layered outfits: an inner layer fully covered by an outer one, and without its own toggle, gets no tile.")]
        public bool optimizeTileUsage = true;

        [Header("Debug & Preview")]
        [Tooltip("Logs cache use, vertex counts, timing and settings during the build.")]
        public bool debugMode = false;
        
        [Tooltip("Shows the generated preview in the Scene view.")]
        public bool showPreview = false;

        [Tooltip("Vertices hidden by the last build.")]
        [SerializeField] private int hiddenVertexCount = 0;

        [Tooltip("How the last build hid them.")]
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
        
        /// <summary>
        /// Gets all valid clothing meshes.
        /// </summary>
        public SkinnedMeshRenderer[] GetClothingMeshes()
        {
            var meshes = new List<SkinnedMeshRenderer>();
            
            if (clothingMeshes != null)
            {
                foreach (var mesh in clothingMeshes)
                {
                    if (mesh != null && mesh.sharedMesh != null)
                    {
                        meshes.Add(mesh);
                    }
                }
            }
            
            return meshes.ToArray();
        }
    }
}

