using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    [Flags]
    public enum FTBlendSyncStandard { UnifiedExpressions = 1, ARKit = 2, SRanipal = 4, MMD = 8 }
    public enum FTBlendSyncRegion { Whole, Left, Right }

    [Serializable]
    public class FTBlendSyncSource
    {
        public string shape;
        public float weight = 1;
        public FTBlendSyncRegion region;
    }

    [Serializable]
    public class FTBlendSyncMapping
    {
        public string target;
        public List<FTBlendSyncSource> sources = new List<FTBlendSyncSource>();
    }

    [DisallowMultipleComponent, AddComponentMenu("YUCP/FT Blendsync"), SupportBanner]
    public class FTBlendSyncData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Tooltip("The face mesh. Empty uses the mesh on this object; the original mesh is never changed.")]
        public SkinnedMeshRenderer targetRenderer;
        [Tooltip("Expression sets added to the mesh when the avatar builds. Use one face-tracking controller that matches.")]
        public FTBlendSyncStandard outputStandards = FTBlendSyncStandard.UnifiedExpressions | FTBlendSyncStandard.MMD;
        [Tooltip("Splits combined shapes into left and right, and combines shapes to approximate missing expressions. Shape coverage lists them.")]
        public bool allowApproximations = true;
        [Tooltip("Space for left/right splits and tongue directions, with +X to the character's right, +Y up and +Z forward. Empty uses the avatar root.")]
        public Transform faceSpace;
        [Min(0.00001f), Tooltip("Width of the soft seam where a combined shape is split, in face-space units.")]
        public float splitFeather = 0.002f;
        [Tooltip("Swaps left and right, for models whose face-space X axis points left.")]
        public bool reverseLeftRight;
        [Tooltip("Optional. Builds each target shape from shapes on the original mesh; existing target shapes are kept.")]
        public List<FTBlendSyncMapping> customMappings = new List<FTBlendSyncMapping>();

        [Tooltip("Creates the missing tongue direction shapes from the tongue bone's skin weights. They move only with TongueX and TongueY tracking; iPhone ARKit tracks tongue-out only.")]
        public bool generateTongueDirections;
        [Tooltip("The tongue's root bone. Its skin weights shape the generated directions.")]
        public Transform tongueRoot;
        [Range(0, 60), Tooltip("How far the tongue turns left and right, in degrees.")] public float tongueSideAngle = 25;
        [Range(0, 60), Tooltip("How far the tongue turns up and down, in degrees.")] public float tongueUpDownAngle = 20;

        [Tooltip("Keeps the MMD shapes through VRCFury's optimizer. Dance worlds expect the face mesh at avatar-root/Body; turn face tracking off while dancing.")]
        public bool preserveMmdShapes = true;

        public SkinnedMeshRenderer GetRenderer() => targetRenderer != null ? targetRenderer : GetComponent<SkinnedMeshRenderer>();
        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;
    }
}
