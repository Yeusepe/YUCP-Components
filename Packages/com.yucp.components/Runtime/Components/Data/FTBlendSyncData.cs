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
        [Tooltip("Mesh to adapt. Empty uses the SkinnedMeshRenderer on this object. Original mesh assets are never edited.")]
        public SkinnedMeshRenderer targetRenderer;
        public FTBlendSyncStandard outputStandards = FTBlendSyncStandard.UnifiedExpressions | FTBlendSyncStandard.MMD;
        [Tooltip("Allow geometric left/right splits of combined shapes and approximate expression combinations. The inspector identifies these mappings.")]
        public bool allowApproximations = true;
        [Tooltip("Coordinates for left/right splitting and generated tongue directions. Empty uses the avatar root. +X is the character's right, +Y up, +Z forward.")]
        public Transform faceSpace;
        [Min(0.00001f), Tooltip("Soft seam width in face-space units when splitting a combined shape.")]
        public float splitFeather = 0.002f;
        [Tooltip("Reverse left/right for models whose face-space X axis points left.")]
        public bool reverseLeftRight;
        [Tooltip("Optional explicit mappings for custom names. Source names must exist on the original mesh. Existing target shapes are always preserved.")]
        public List<FTBlendSyncMapping> customMappings = new List<FTBlendSyncMapping>();

        [Tooltip("Generate missing tongue direction shapes using the tongue bone's skin weights. Requires a tongue-out shape and an explicitly assigned bone.")]
        public bool generateTongueDirections;
        public Transform tongueRoot;
        [Range(0, 60)] public float tongueSideAngle = 25;
        [Range(0, 60)] public float tongueUpDownAngle = 20;

        [Tooltip("Add VRCFury MMD Compatibility during build so its optimizer retains the MMD shapes. MMD worlds generally expect a renderer at avatar-root/Body. Disable face tracking in its menu when dancing.")]
        public bool preserveMmdShapes = true;

        public SkinnedMeshRenderer GetRenderer() => targetRenderer != null ? targetRenderer : GetComponent<SkinnedMeshRenderer>();
        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;
    }
}
