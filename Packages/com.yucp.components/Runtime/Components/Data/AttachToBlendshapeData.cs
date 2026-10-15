using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    public enum AttachToBlendshapeOutputMode
    {
        Auto,
        LinkedRendererBake,
        MergeIntoBaseMesh
    }

    public enum AttachToBlendshapeBakeMethod
    {
        /// <summary>
        /// Use rigid pivot motion solved from the surface cluster (existing approach).
        /// Best for small rigid objects.
        /// </summary>
        RigidPivotTransform,

        /// <summary>
        /// Bake per-vertex deltas based on closest-surface correspondence on the base mesh.
        /// Best for merge mode and robust deformation.
        /// </summary>
        ClosestSurfaceDisplacement
    }

    public enum AttachToBlendshapeUnmatchedHandling
    {
        Skip,
        NeighborPropagate,
        SmoothDiffusion
    }

    public enum AttachToBlendshapeVisibilityMode
    {
        SizeBlendshape = 0,
        UVDiscard = 1
    }

    public enum BlendshapeTrackingMode
    {
        All,
        Specific,
        VisemsOnly,
        Smart
    }

    public enum SolverMode
    {
        Rigid = 0,
        RigidNormalOffset = 1,
        Affine = 2
    }

    [Serializable]
    public class TriangleAnchor
    {
        [Tooltip("Triangle index in the mesh")]
        public int triIndex;

        [Tooltip("Barycentric coordinates within the triangle")]
        public Vector3 barycentric;

        [Tooltip("Weight of this anchor in the cluster")]
        public float weight;

        public TriangleAnchor() { }

        public TriangleAnchor(int index, Vector3 bary, float w)
        {
            triIndex = index;
            barycentric = bary;
            weight = w;
        }
    }

    [Serializable]
    public class SurfaceCluster
    {
        [Tooltip("Triangle anchors in the cluster")]
        public List<TriangleAnchor> anchors = new List<TriangleAnchor>();

        [Tooltip("Total weight of all anchors (for normalization)")]
        public float totalWeight = 1f;

        [Tooltip("Center position of the cluster in local space")]
        public Vector3 centerPosition;

        [Tooltip("Average normal of the cluster")]
        public Vector3 averageNormal;
    }


    [SupportBanner]
    [BetaWarning("This component is in beta. Blendshape attachment may need manual setup.")]
    [AddComponentMenu("YUCP/Attach to Blendshape")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    public class AttachToBlendshapeData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Source Mesh")]
        [Tooltip("The avatar's skinned mesh whose blendshapes move the attachment, usually the face or body.")]
        public SkinnedMeshRenderer targetMesh;

        [Header("Target Mesh to Modify")]
        [Tooltip("The mesh that follows the blendshapes: a Skinned Mesh Renderer, a Mesh Filter (made skinned at build time), or an object with one. Empty uses the mesh on this object.")]
        public UnityEngine.Object targetMeshToModify;

        [Header("Blendshape Tracking")]
        [Tooltip("Which body blendshapes move the attachment. Smart finds the ones that move the surface under it.")]
        public BlendshapeTrackingMode trackingMode = BlendshapeTrackingMode.Smart;

        [Tooltip("The body blendshapes to track when Tracking is Specific.")]
        public List<string> specificBlendshapes = new List<string>();

        [Header("Surface Attachment")]
        [Tooltip("How many nearby body triangles the attachment follows. More holds steadier on stretching skin; 3 to 5 suits most attachments.")]
        [Range(1, 8)]
        public int clusterTriangleCount = 4;

        [Tooltip("How far, in meters, to look for the body surface. 0 searches the whole mesh.")]
        [Min(0f)]
        public float searchRadius = 0.1f;

        [Tooltip("The body triangle to anchor to. -1 uses the closest one.")]
        public int manualTriangleIndex = -1;

        [Header("Solver Configuration")]
        [Tooltip("How the attachment follows the surface. Rigid moves and turns it, Rigid Offset also pushes it out along the normal, and Affine also stretches it with the skin.")]
        public SolverMode solverMode = SolverMode.Rigid;

        [Tooltip("Turns the attachment with the surface as it deforms.")]
        public bool alignRotationToSurface = true;

        [Tooltip("How far, in meters, Rigid Offset pushes the attachment out from the surface.")]
        [Min(0f)]
        public float normalOffset = 0.001f;

        [Tooltip("Smooths the preview's rotation to prevent flips. 0 is off; 1 is smoothest.")]
        [Range(0f, 1f)]
        public float rotationSmoothingFactor = 0.3f;

        [Header("Bone Attachment")]
        [Tooltip("Links the attachment to the closest bone with a VRCFury Armature Link, so it moves with that bone as well as with the blendshapes.")]
        public bool attachToClosestBone = true;

        [Tooltip("How far, in meters, to look for a bone. 0 searches the whole armature.")]
        [Min(0f)]
        public float boneSearchRadius = 0.5f;

        [Tooltip("Optional. Only bones whose names contain this text are used.")]
        public string boneNameFilter = "";

        [Tooltip("Skips the avatar's humanoid bones when looking for the closest bone.")]
        public bool ignoreHumanoidBones = false;

        [Tooltip("Optional. A child path under the closest bone to link to instead.")]
        public string boneOffset = "";

        [Header("Blendshape Transfer")]
        [Tooltip("Keyframes baked per blendshape. More follows curved motion more smoothly but makes the avatar larger.")]
        [Range(2, 10)]
        public int samplesPerBlendshape = 5;

        [Tooltip("How far, in meters, a blendshape must move the surface for Smart tracking to include it.")]
        [Min(0.0001f)]
        public float smartDetectionThreshold = 0.001f;

        [Header("Output (Advanced)")]
        [Tooltip("How the attachment is built. Merged lets visemes and other built-in blendshapes move it; Linked keeps it a separate mesh; Automatic picks one from the avatar's animations.")]
        public AttachToBlendshapeOutputMode outputMode = AttachToBlendshapeOutputMode.Auto;

        [Tooltip("How motion is baked into the merged mesh. Rigid Pivot Transform moves the whole attachment; Closest Surface Displacement moves each vertex with the surface under it.")]
        public AttachToBlendshapeBakeMethod bakeMethod = AttachToBlendshapeBakeMethod.RigidPivotTransform;

        [Tooltip("What to do with attachment vertices that find no body surface when merged. Neighbor Propagate copies motion from nearby vertices; Smooth Diffusion blends it over the mesh and is slower.")]
        public AttachToBlendshapeUnmatchedHandling unmatchedHandling = AttachToBlendshapeUnmatchedHandling.NeighborPropagate;

        [Tooltip("Keeps the attachment separate when Automatic finds animations that move it, other than on/off toggles.")]
        public bool autoAvoidMergeIfComplexTransformAnimation = true;

        [Tooltip("Keeps the attachment separate when Automatic finds material animations on it, which can't be moved to a merged mesh reliably.")]
        public bool autoAvoidMergeIfMaterialAnimation = true;

        [Tooltip(
            "When enabled, toggle-style transform animations (constant/on-off) will be preserved in Merge mode by converting them into blendshapes on the merged mesh and rewriting clip bindings.\n" +
            "Complex continuous transform animations are not converted (Auto will fall back to Linked Renderer Bake).")]
        public bool preserveToggleStyleTransformAnimationsOnMerge = true;

        [Tooltip("Turns off the attachment's own renderer after merging, so it isn't drawn twice.")]
        public bool disableOriginalRendererOnMerge = true;

        [Header("Merged Visibility")]
        [Tooltip("How the merged attachment is hidden when it's toggled off: a blendshape that shrinks it, or a UV discard tile.")]
        public AttachToBlendshapeVisibilityMode visibilityMode = AttachToBlendshapeVisibilityMode.SizeBlendshape;

        [Tooltip("Picks a free UV channel for UV discard.")]
        public bool autoDetectUVChannel = true;

        [Tooltip("The UV channel for UV discard when it isn't picked automatically.")]
        [Range(0, 7)]
        public int uvChannel = 1;

        [Tooltip("The UV discard tile row for this attachment.")]
        [Range(0, 3)]
        public int uvDiscardRow = 3;

        [Tooltip("The UV discard tile column for this attachment.")]
        [Range(0, 3)]
        public int uvDiscardColumn = 3;

        [Tooltip("Moves the attachment's existing blendshape, material, transform, renderer and visibility animations to the merged mesh.")]
        public bool remapVisibilityAnimations = true;

        [Header("Advanced Options")]
        [Tooltip("Logs each build and preview step to the Console.")]
        public bool debugMode = false;

        [Tooltip("Shows the surface the attachment follows in the Scene view while a preview is generated.")]
        public bool showPreview = false;

        [Header("Build Statistics (Read-only)")]
        [Tooltip("Surface cluster detected at build time")]
        [SerializeField] private SurfaceCluster detectedCluster;

        [Tooltip("Blendshapes that were tracked")]
        [SerializeField] private List<string> trackedBlendshapes = new List<string>();

        [Tooltip("Number of blendshapes transferred")]
        [SerializeField] private int transferredBlendshapeCount = 0;

        [Tooltip("Selected bone path")]
        [SerializeField] private string selectedBonePath = "";

        [Tooltip("Output mode that was used at build time (read-only)")]
        [SerializeField] private AttachToBlendshapeOutputMode appliedOutputMode = AttachToBlendshapeOutputMode.Auto;

        [SerializeField] private AttachToBlendshapeVisibilityMode appliedVisibilityMode = AttachToBlendshapeVisibilityMode.SizeBlendshape;
        [SerializeField] private string generatedVisibilityBlendshape = "";
        [SerializeField] private int generatedBlendshapeCount = 0;
        [SerializeField] private int remappedCurveCount = 0;
        [SerializeField] private List<string> buildWarnings = new List<string>();


        [System.NonSerialized] public SurfaceCluster previewCluster;
        [System.NonSerialized] public List<string> previewBlendshapes = new List<string>();
        [System.NonSerialized] public bool previewGenerated = false;
        [System.NonSerialized] public Dictionary<string, float> previewBlendshapeWeights = new Dictionary<string, float>();
        [System.NonSerialized] public Dictionary<string, float> previewOriginalWeights = new Dictionary<string, float>();
        [System.NonSerialized] public Vector3 previewBasePosition;
        [System.NonSerialized] public Vector3 previewBaseNormal;
        [System.NonSerialized] public Vector3 previewBaseTangent;
        [System.NonSerialized] public bool previewBaseCaptured;
        [System.NonSerialized] public Vector3 previewOriginalLocalPosition;
        [System.NonSerialized] public Quaternion previewOriginalLocalRotation;
        [System.NonSerialized] public bool previewOriginalTransformCaptured;
        [System.NonSerialized] public Vector3 previewLastTangent;
        [System.NonSerialized] public bool previewHasLastTangent;
        [System.NonSerialized] public Vector3 previewBaseSolverPosition;
        [System.NonSerialized] public Quaternion previewBaseSolverRotation;
        [System.NonSerialized] public bool previewHasBaseSolver;
        [System.NonSerialized] public Vector3 previewPositionOffset;
        [System.NonSerialized] public Quaternion previewRotationOffset = Quaternion.identity;
        [System.NonSerialized] public SkinnedMeshRenderer previewTempSkinnedMesh;
        [System.NonSerialized] public MeshFilter previewOriginalMeshFilter;
        [System.NonSerialized] public MeshRenderer previewOriginalMeshRenderer;
        [System.NonSerialized] public Mesh previewOriginalMesh;
        [System.NonSerialized] public Mesh previewWorkingMesh;
        [System.NonSerialized] public Mesh previewOriginalTargetMesh;
        [System.NonSerialized] public bool previewUsesMergedMesh;
        [System.NonSerialized] public Mesh previewOriginalBaseMesh;
        [System.NonSerialized] public Material[] previewOriginalBaseMaterials;
        [System.NonSerialized] public Material[] previewOriginalAttachmentMaterials;
        [System.NonSerialized] public Material[] previewMergedMaterials;
        [System.NonSerialized] public SkinnedMeshRenderer previewMergedSourceSkinnedRenderer;
        [System.NonSerialized] public MeshRenderer previewMergedSourceMeshRenderer;
        [System.NonSerialized] public bool previewMergedSourceRendererEnabled;

        public SurfaceCluster DetectedCluster => detectedCluster;
        public List<string> TrackedBlendshapes => trackedBlendshapes;
        public int TransferredBlendshapeCount => transferredBlendshapeCount;
        public string SelectedBonePath => selectedBonePath;
        public AttachToBlendshapeOutputMode AppliedOutputMode => appliedOutputMode;
        public AttachToBlendshapeVisibilityMode AppliedVisibilityMode => appliedVisibilityMode;
        public string GeneratedVisibilityBlendshape => generatedVisibilityBlendshape;
        public int GeneratedBlendshapeCount => generatedBlendshapeCount;
        public int RemappedCurveCount => remappedCurveCount;
        public IReadOnlyList<string> BuildWarnings => buildWarnings;

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;

        public void SetBuildStats(
            SurfaceCluster cluster,
            List<string> blendshapes,
            int transferredCount,
            string bonePath,
            AttachToBlendshapeOutputMode outputModeUsed = AttachToBlendshapeOutputMode.Auto,
            AttachToBlendshapeVisibilityMode visibilityModeUsed = AttachToBlendshapeVisibilityMode.SizeBlendshape,
            string visibilityBlendshapeName = "",
            int actualGeneratedBlendshapeCount = 0,
            int curvesRemapped = 0,
            List<string> warnings = null)
        {
            detectedCluster = cluster;
            trackedBlendshapes = new List<string>(blendshapes);
            transferredBlendshapeCount = transferredCount;
            selectedBonePath = bonePath;
            appliedOutputMode = outputModeUsed;
            appliedVisibilityMode = visibilityModeUsed;
            generatedVisibilityBlendshape = visibilityBlendshapeName ?? "";
            generatedBlendshapeCount = actualGeneratedBlendshapeCount;
            remappedCurveCount = curvesRemapped;
            buildWarnings = warnings != null ? new List<string>(warnings) : new List<string>();
        }

        private void OnValidate()
        {

            if ((int)solverMode == 3)
                solverMode = SolverMode.Rigid;
        }

        private void Reset()
        {

            clusterTriangleCount = 4;
            searchRadius = 0.1f;
            solverMode = SolverMode.Rigid;
            alignRotationToSurface = true;
            rotationSmoothingFactor = 0.3f;
            samplesPerBlendshape = 5;
            smartDetectionThreshold = 0.001f;
            attachToClosestBone = true;
            boneSearchRadius = 0.5f;

            outputMode = AttachToBlendshapeOutputMode.Auto;
            bakeMethod = AttachToBlendshapeBakeMethod.RigidPivotTransform;
            unmatchedHandling = AttachToBlendshapeUnmatchedHandling.NeighborPropagate;
            autoAvoidMergeIfComplexTransformAnimation = true;
            autoAvoidMergeIfMaterialAnimation = true;
            preserveToggleStyleTransformAnimationsOnMerge = true;
            disableOriginalRendererOnMerge = true;
            visibilityMode = AttachToBlendshapeVisibilityMode.SizeBlendshape;
            autoDetectUVChannel = true;
            uvChannel = 1;
            uvDiscardRow = 3;
            uvDiscardColumn = 3;
            remapVisibilityAnimations = true;
        }

        private void Awake()
        {
            #if !UNITY_EDITOR

            Destroy(this);
            #endif
        }
    }
}
