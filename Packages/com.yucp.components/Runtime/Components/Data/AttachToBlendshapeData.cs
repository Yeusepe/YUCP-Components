using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    public enum BlendshapeTrackingMode
    {
        All,
        Specific,
        VisemsOnly,
        Smart
    }

    public enum SolverMode
    {
        Rigid,
        RigidNormalOffset,
        Affine,
        CageRBF
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

    [Serializable]
    public class BlendshapeAnimationData
    {
        [Tooltip("Name of the blendshape being tracked")]
        public string blendshapeName;
        
        [Tooltip("Generated animation clip for this blendshape")]
        public AnimationClip animationClip;
        
        [Tooltip("Number of keyframes in the animation")]
        public int keyframeCount;
    }

    [BetaWarning("This component is in BETA and may not work as intended. Blendshape attachment is experimental and may require manual configuration.")]
    [AddComponentMenu("YUCP/Attach to Blendshape")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    public class AttachToBlendshapeData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Target Mesh")]
        [Tooltip("The skinned mesh with blendshapes to attach to (usually the avatar head/body).")]
        public SkinnedMeshRenderer targetMesh;

        [Header("Blendshape Tracking")]
        [Tooltip("Which blendshapes to track:\n\n" +
                 "• All: Track all blendshapes on the target mesh\n" +
                 "• Specific: Only track blendshapes you select\n" +
                 "• Visemes Only: Auto-detect VRChat viseme blendshapes\n" +
                 "• Smart: Auto-detect blendshapes that affect this attachment")]
        public BlendshapeTrackingMode trackingMode = BlendshapeTrackingMode.Smart;

        [Tooltip("Specific blendshapes to track (only used when mode = Specific).\n\n" +
                 "Enter exact blendshape names from the target mesh.")]
        public List<string> specificBlendshapes = new List<string>();

        [Header("Surface Attachment")]
        [Tooltip("Number of triangles to use in the attachment cluster (1-8).\n\n" +
                 "More triangles = more stable during deformation but slower computation.\n" +
                 "Recommended: 3-5 for most attachments.")]
        [Range(1, 8)]
        public int clusterTriangleCount = 4;

        [Tooltip("Search radius in meters for finding attachment surface.\n\n" +
                 "The component will find the closest surface within this radius.\n" +
                 "Set to 0 for unlimited range.")]
        [Min(0f)]
        public float searchRadius = 0.1f;

        [Tooltip("Manual triangle index selection (leave -1 for auto-detect).\n\n" +
                 "If set, this triangle will be the primary anchor.")]
        public int manualTriangleIndex = -1;

        [Header("Solver Configuration")]
        [Tooltip("How to calculate object transforms during deformation:\n\n" +
                 "• Rigid: Simple rotation + translation (best for piercings, badges)\n" +
                 "• Rigid Normal Offset: Rigid + slight outward push along surface normal\n" +
                 "• Affine: Allows minor shear/scale to match skin stretch (stickers, patches)\n" +
                 "• Cage RBF: Advanced smooth deformation for larger objects")]
        public SolverMode solverMode = SolverMode.Rigid;

        [Tooltip("Align object rotation to surface normal and tangent.\n\n" +
                 "When enabled, object rotates to follow the surface orientation.")]
        public bool alignRotationToSurface = true;

        [Tooltip("Normal offset distance in meters (only for RigidNormalOffset mode).\n\n" +
                 "Pushes object away from surface along normal.")]
        [Min(0f)]
        public float normalOffset = 0.001f;

        [Tooltip("Smoothing factor to prevent rotation flips (0-1).\n\n" +
                 "Higher values = smoother transitions but less responsive.\n" +
                 "0 = no smoothing, 1 = maximum smoothing.")]
        [Range(0f, 1f)]
        public float rotationSmoothingFactor = 0.3f;

        [Header("Cage/RBF Settings (CageRBF mode only)")]
        [Tooltip("Number of driver points on the surface (3-16).\n\n" +
                 "More points = more accurate deformation but slower computation.\n" +
                 "Recommended: 4-8 for most objects.")]
        [Range(3, 16)]
        public int rbfDriverPointCount = 6;

        [Tooltip("RBF kernel radius multiplier.\n\n" +
                 "Controls how far each driver point influences the mesh.\n" +
                 "Lower = more localized, Higher = smoother but less detailed.")]
        [Range(0.5f, 3f)]
        public float rbfRadiusMultiplier = 1.5f;

        [Tooltip("Use GPU acceleration for RBF computation (when available).\n\n" +
                 "Significantly faster for meshes with many vertices.")]
        public bool useGPUAcceleration = true;

        [Header("Bone Attachment")]
        [Tooltip("Attach to closest bone for base positioning.\n\n" +
                 "Blendshape animations will be relative to this bone.")]
        public bool attachToClosestBone = true;

        [Tooltip("Bone search radius in meters (0 = unlimited).")]
        [Min(0f)]
        public float boneSearchRadius = 0.5f;

        [Tooltip("Bone name filter (only consider bones containing this text).")]
        public string boneNameFilter = "";

        [Tooltip("Ignore humanoid bones when searching for closest bone.")]
        public bool ignoreHumanoidBones = false;

        [Tooltip("Optional bone path offset for fine-tuning.")]
        public string boneOffset = "";

        [Header("Animation Generation")]
        [Tooltip("Create direct FX layer integration (requires manual Animator Controller setup).\n\n" +
                 "When enabled, animations are saved to Assets/Generated for manual wiring.\n" +
                 "When disabled, animations are temporary (build-time only).")]
        public bool createDirectAnimations = true;

        [Tooltip("Number of sample points per blendshape (2-10).\n\n" +
                 "More samples = smoother animation but larger file size.\n" +
                 "Recommended: 5 for most cases.")]
        [Range(2, 10)]
        public int samplesPerBlendshape = 5;

        [Tooltip("Minimum vertex displacement (in meters) to consider blendshape active.\n\n" +
                 "Used by Smart mode to detect which blendshapes affect the attachment.")]
        [Min(0.0001f)]
        public float smartDetectionThreshold = 0.001f;

        [Header("Advanced Options")]
        [Tooltip("Save generated animation clips as assets for debugging.")]
        public bool debugSaveAnimations = false;

        [Tooltip("Enable debug logging during build.")]
        public bool debugMode = false;

        [Tooltip("Show preview visualization in Scene view.")]
        public bool showPreview = false;

        [Header("Build Statistics (Read-only)")]
        [Tooltip("Surface cluster detected at build time")]
        [SerializeField] private SurfaceCluster detectedCluster;

        [Tooltip("Blendshapes that were tracked")]
        [SerializeField] private List<string> trackedBlendshapes = new List<string>();

        [Tooltip("Number of animation clips generated")]
        [SerializeField] private int generatedAnimationCount = 0;

        [Tooltip("Selected bone path")]
        [SerializeField] private string selectedBonePath = "";

        // Preview data (not serialized)
        [System.NonSerialized] public SurfaceCluster previewCluster;
        [System.NonSerialized] public List<string> previewBlendshapes = new List<string>();
        [System.NonSerialized] public bool previewGenerated = false;

        public SurfaceCluster DetectedCluster => detectedCluster;
        public List<string> TrackedBlendshapes => trackedBlendshapes;
        public int GeneratedAnimationCount => generatedAnimationCount;
        public string SelectedBonePath => selectedBonePath;

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;

        public void SetBuildStats(SurfaceCluster cluster, List<string> blendshapes, int animCount, string bonePath)
        {
            detectedCluster = cluster;
            trackedBlendshapes = new List<string>(blendshapes);
            generatedAnimationCount = animCount;
            selectedBonePath = bonePath;
        }

        private void Reset()
        {
            // Set sensible defaults when component is first added
            clusterTriangleCount = 4;
            searchRadius = 0.1f;
            solverMode = SolverMode.Rigid;
            alignRotationToSurface = true;
            rotationSmoothingFactor = 0.3f;
            createDirectAnimations = true;
            samplesPerBlendshape = 5;
            smartDetectionThreshold = 0.001f;
            attachToClosestBone = true;
            boneSearchRadius = 0.5f;
            rbfDriverPointCount = 6;
            rbfRadiusMultiplier = 1.5f;
            useGPUAcceleration = true;
        }

        private void Awake()
        {
            #if !UNITY_EDITOR
            // Runtime cleanup - remove this component (it's editor-only)
            Destroy(this);
            #endif
        }
    }
}

