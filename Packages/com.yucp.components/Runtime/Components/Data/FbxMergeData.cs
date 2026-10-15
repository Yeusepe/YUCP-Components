using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    [SupportBanner]
    [BetaWarning("This component is in beta. It changes renderer and animation bindings when the avatar builds.")]
    [AddComponentMenu("YUCP/Mesh Merger")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    public class FbxMergeData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Targets")]
        [Tooltip("The skinned mesh the attachment merges into, usually the body.")]
        public SkinnedMeshRenderer baseRenderer;

        [Tooltip("The mesh to merge into the base mesh: a Skinned Mesh Renderer, a Mesh Filter, or an object that has one.")]
        public UnityEngine.Object attachmentTarget;

        [Header("Merge Behavior")]
        [Tooltip("Removes the attachment's object from the built avatar once it has merged.")]
        public bool deleteAttachmentObjectAfterMerge = true;

        [Tooltip("Renames attachment blendshapes that share a name with a base blendshape.")]
        public bool renameConflictingAttachmentBlendshapes = true;

        [Tooltip("Added to the start of renamed blendshapes.")]
        public string attachmentBlendshapePrefix = "Merged_";

        [Tooltip("How points with no matching base surface are handled when blendshapes are baked.")]
        public AttachToBlendshapeUnmatchedHandling unmatchedHandling = AttachToBlendshapeUnmatchedHandling.NeighborPropagate;

        [Header("Animation Remap")]
        [Tooltip("Points animations that target the attachment at the merged mesh.")]
        public bool remapAnimations = true;

        [Tooltip("Points the attachment's blendshape curves at the merged blendshapes.")]
        public bool remapBlendshapeAnimations = true;

        [Tooltip("Points the attachment's material curves at the merged material slots.")]
        public bool remapMaterialAnimations = true;

        [Tooltip("Animations that turn the attachment off hide its part of the merged mesh with UV discard instead.")]
        public bool remapRendererAndObjectOffToUvDiscard = true;

        [Tooltip("When the attachment's materials can't use UV discard, hides it by shrinking it with a blendshape.")]
        public bool scaleToZeroFallback = true;

        [Header("UV Discard")]
        [Tooltip("Picks the UV channel for the discard data automatically.")]
        public bool autoDetectUVChannel = true;

        [Tooltip("UV channel for the discard data.")]
        [Range(0, 3)]
        public int uvChannel = 1;

        [Tooltip("UV tile row the attachment is discarded with.")]
        [Range(0, 3)]
        public int uvDiscardRow = 3;

        [Tooltip("UV tile column the attachment is discarded with.")]
        [Range(0, 3)]
        public int uvDiscardColumn = 3;

        [Header("Debug")]
        [Tooltip("Logs each step of the merge to the Console.")]
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

