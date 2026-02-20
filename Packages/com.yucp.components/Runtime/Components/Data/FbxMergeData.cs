using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    [SupportBanner]
    [BetaWarning("This component is in BETA and may not work as intended. Mesh Merger modifies renderer/animation bindings at build time.")]
    [AddComponentMenu("YUCP/Mesh Merger")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    public class FbxMergeData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Targets")]
        [Tooltip("Base skinned renderer that receives merged geometry.")]
        public SkinnedMeshRenderer baseRenderer;

        [Tooltip("Attachment source to merge into the base renderer.\n\nSupported:\n• SkinnedMeshRenderer\n• MeshFilter (with MeshRenderer)\n• GameObject containing either of the above")]
        public UnityEngine.Object attachmentTarget;

        [Header("Merge Behavior")]
        [Tooltip("Delete the source attachment GameObject after successful merge/remap.")]
        public bool deleteAttachmentObjectAfterMerge = true;

        [Tooltip("Rename attachment blendshapes when names conflict with base blendshapes.")]
        public bool renameConflictingAttachmentBlendshapes = true;

        [Tooltip("Prefix used when renaming conflicting attachment blendshapes.")]
        public string attachmentBlendshapePrefix = "Merged_";

        [Tooltip("How unmatched closest-surface mapping points are handled during blendshape bake.")]
        public AttachToBlendshapeUnmatchedHandling unmatchedHandling = AttachToBlendshapeUnmatchedHandling.NeighborPropagate;

        [Header("Animation Remap")]
        [Tooltip("Remap animation clips that target the attachment to the merged base renderer.")]
        public bool remapAnimations = true;

        [Tooltip("Remap attachment blendshape curves to merged blendshape names/paths.")]
        public bool remapBlendshapeAnimations = true;

        [Tooltip("Remap attachment material curves to merged material slots.")]
        public bool remapMaterialAnimations = true;

        [Tooltip("Convert attachment renderer/object OFF animation states into UV discard animation on merged materials.")]
        public bool remapRendererAndObjectOffToUvDiscard = true;

        [Tooltip("When attachment materials lack UV discard support, collapse attachment vertices via a blendshape to simulate toggling off.")]
        public bool scaleToZeroFallback = true;

        [Header("UV Discard")]
        [Tooltip("Automatically detect which UV channel to use for discard data.")]
        public bool autoDetectUVChannel = true;

        [Tooltip("UV channel used for discard data when auto detect is disabled.")]
        [Range(0, 3)]
        public int uvChannel = 1;

        [Tooltip("UV tile row used for merged attachment discard.")]
        [Range(0, 3)]
        public int uvDiscardRow = 3;

        [Tooltip("UV tile column used for merged attachment discard.")]
        [Range(0, 3)]
        public int uvDiscardColumn = 3;

        [Header("Debug")]
        [Tooltip("Enable detailed build logs.")]
        public bool debugMode = false;

        [Header("Build Statistics (Read-only)")]
        [SerializeField] private int mergedVertexCount = 0;
        [SerializeField] private int mergedSubmeshCount = 0;
        [SerializeField] private int mergedBlendshapeCount = 0;
        [SerializeField] private int remappedCurveCount = 0;
        [SerializeField] private int warningCount = 0;
        [SerializeField] private List<string> remapWarnings = new List<string>();

        public int MergedVertexCount => mergedVertexCount;
        public int MergedSubmeshCount => mergedSubmeshCount;
        public int MergedBlendshapeCount => mergedBlendshapeCount;
        public int RemappedCurveCount => remappedCurveCount;
        public int WarningCount => warningCount;
        public IReadOnlyList<string> RemapWarnings => remapWarnings;

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;

        public void SetBuildStats(
            int vertexCount,
            int submeshCount,
            int blendshapeCount,
            int curvesRemapped,
            int warnings,
            List<string> warningMessages = null)
        {
            mergedVertexCount = vertexCount;
            mergedSubmeshCount = submeshCount;
            mergedBlendshapeCount = blendshapeCount;
            remappedCurveCount = curvesRemapped;
            warningCount = warnings;
            remapWarnings = warningMessages != null ? new List<string>(warningMessages) : new List<string>();
        }
    }
}

