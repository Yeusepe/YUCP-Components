using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    public enum NormalTransferMethod
    {
        Proximity,
        Projection,
        SharedField
    }

    [SupportBanner]
    [BetaWarning("This component is in BETA and may not work as intended. Use with caution.")]
    [AddComponentMenu("YUCP/Seamless Normals")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    public class SeamlessNormalsData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Source Meshes")]
        [Tooltip("The source mesh(es) to transfer normals from. These meshes define the target normal direction.\n\n" +
                 "Can specify multiple source meshes - normals will be blended from all sources.")]
        public Renderer[] sourceMeshes = new Renderer[0];

        [Header("Target Meshes")]
        [Tooltip("The target mesh(es) that will receive the transferred normals.\n\n" +
                 "These meshes will have their normals modified to match the source meshes, creating a seamless appearance.")]
        public Renderer[] targetMeshes = new Renderer[0];

        [Header("Transfer Method")]
        [Tooltip("Choose how to transfer normals between meshes.\n\n" +
                 "• Proximity: Fast vertex-to-vertex matching (works when vertices align)\n" +
                 "• Projection: Robust projection-based transfer (works with mismatched topology)\n" +
                 "• Shared Field: Most correct - treats meshes as one continuous surface")]
        public NormalTransferMethod transferMethod = NormalTransferMethod.Projection;

        [Header("Proximity Settings")]
        [Tooltip("Blending distance - how far from source mesh to blend normals (in meters).\n\n" +
                 "Creates a smooth transition zone like metaballs - meshes blend together when close.\n" +
                 "Larger values = wider blending zone, smoother transitions.\n" +
                 "• 0.01m: Tight blending (meshes must be very close)\n" +
                 "• 0.05m: Standard blending (recommended for metaball-like effect)\n" +
                 "• 0.1m: Wide blending (smooth transitions over larger distance)\n" +
                 "• 0.2m: Very wide blending (for dramatic smooth merging)")]
        [Range(0.001f, 0.5f)]
        public float proximityThreshold = 0.05f;

        [Tooltip("Blend strength modifier for proximity transfer.\n\n" +
                 "Controls how much the distance-based blending is reduced.\n" +
                 "• 0.0: Full metaball-like blending (stronger when closer)\n" +
                 "• 0.5: Moderate blending\n" +
                 "• 1.0: Minimal blending (mostly preserves original normals)\n\n" +
                 "Note: Blending is automatically stronger when meshes are closer (metaball effect).")]
        [Range(0.0f, 1.0f)]
        public float proximityBlendStrength = 0.0f;

        [Header("Projection Settings")]
        [Tooltip("Maximum projection distance (in meters).\n\n" +
                 "Target vertices beyond this distance from source mesh will not be modified.\n" +
                 "• 0.01m: Very close surfaces only\n" +
                 "• 0.05m: Standard distance (recommended)\n" +
                 "• 0.2m: Far surfaces")]
        [Range(0.001f, 1.0f)]
        public float projectionDistance = 0.05f;

        [Tooltip("Projection direction mode.\n\n" +
                 "• Vertex Normal: Project along target vertex normal\n" +
                 "• Both Directions: Try both directions and use closest hit\n" +
                 "• Surface Normal: Project along source surface normal")]
        public enum ProjectionDirection
        {
            VertexNormal,
            BothDirections,
            SurfaceNormal
        }
        public ProjectionDirection projectionDirection = ProjectionDirection.BothDirections;

        [Tooltip("Blend strength for projection transfer (0.0 = copy, 1.0 = full blend).")]
        [Range(0.0f, 1.0f)]
        public float projectionBlendStrength = 0.0f;

        [Header("Shared Field Settings")]
        [Tooltip("Position matching threshold for grouping vertices (in meters).\n\n" +
                 "Vertices within this distance are considered to be at the same position.\n" +
                 "• 0.0001m: Very precise (exact position match)\n" +
                 "• 0.001m: Standard precision (recommended)\n" +
                 "• 0.01m: Loose grouping")]
        [Range(0.0001f, 0.05f)]
        public float sharedFieldPositionThreshold = 0.001f;

        [Tooltip("Hard edge angle threshold (in degrees).\n\n" +
                 "Faces with normals differing by more than this angle will not be smoothed together.\n" +
                 "• 30°: Smooth most edges\n" +
                 "• 60°: Standard (recommended)\n" +
                 "• 90°: Only smooth gentle curves")]
        [Range(0.0f, 180.0f)]
        public float sharedFieldHardEdgeAngle = 60.0f;

        [Header("Performance")]
        [Tooltip("Use GPU acceleration for normal transfer (faster for large meshes).\n\n" +
                 "Automatically falls back to CPU if GPU is not available.\n" +
                 "Only available for Proximity and Projection methods.")]
        public bool useGPUAcceleration = true;

        [Header("Advanced Options")]
        [Tooltip("Only transfer normals to vertices within this distance from source mesh.\n\n" +
                 "Useful for limiting transfer to specific regions.\n" +
                 "Set to 0 to disable distance filtering.")]
        [Range(0.0f, 1.0f)]
        public float maxTransferDistance = 0.0f;

        [Tooltip("Respect hard edges in source mesh.\n\n" +
                 "When enabled, sharp edges in the source mesh will be preserved in the transfer.")]
        public bool respectHardEdges = true;

        [Tooltip("Hard edge detection angle (in degrees).\n\n" +
                 "Faces with normals differing by more than this are considered hard edges.")]
        [Range(0.0f, 180.0f)]
        public float hardEdgeAngle = 60.0f;

        [Header("Debug & Preview")]
        [Tooltip("Show debug information during build.\n\n" +
                 "Displays:\n" +
                 "• Transfer statistics\n" +
                 "• Vertex counts\n" +
                 "• Processing time")]
        public bool debugMode = false;

        [Tooltip("Show preview visualization in Scene view.\n\n" +
                 "When enabled, displays:\n" +
                 "• Blue lines = source normals\n" +
                 "• Green lines = transferred normals\n" +
                 "• Yellow lines = transfer vectors")]
        public bool showPreview = false;

        [Tooltip("The number of vertices that were processed (read-only, populated at build time).")]
        [SerializeField] private int processedVertexCount = 0;

        [Tooltip("The transfer method that was used (read-only, populated at build time).")]
        [SerializeField] private string appliedMethod = "";

        public int ProcessedVertexCount => processedVertexCount;
        public string AppliedMethod => appliedMethod;

        [System.NonSerialized] public Vector3[] previewSourceNormals;
        [System.NonSerialized] public Vector3[] previewTargetNormals;
        [System.NonSerialized] public Vector3[] previewTargetVertices;
        [System.NonSerialized] public bool previewGenerated = false;

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;

        public void SetBuildStats(int vertexCount, string method)
        {
            processedVertexCount = vertexCount;
            appliedMethod = method;
        }

        /// <summary>
        /// Gets all valid source meshes.
        /// </summary>
        public Renderer[] GetSourceMeshes()
        {
            var meshes = new List<Renderer>();
            
            if (sourceMeshes != null)
            {
                foreach (var mesh in sourceMeshes)
                {
                    if (mesh != null)
                    {
                        if (mesh is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                            meshes.Add(mesh);
                        else if (mesh is MeshRenderer mr && mr.GetComponent<MeshFilter>()?.sharedMesh != null)
                            meshes.Add(mesh);
                    }
                }
            }
            
            return meshes.ToArray();
        }

        /// <summary>
        /// Gets all valid target meshes.
        /// </summary>
        public Renderer[] GetTargetMeshes()
        {
            var meshes = new List<Renderer>();
            
            if (targetMeshes != null)
            {
                foreach (var mesh in targetMeshes)
                {
                    if (mesh != null)
                    {
                        if (mesh is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                            meshes.Add(mesh);
                        else if (mesh is MeshRenderer mr && mr.GetComponent<MeshFilter>()?.sharedMesh != null)
                            meshes.Add(mesh);
                    }
                }
            }
            
            return meshes.ToArray();
        }
    }
}

