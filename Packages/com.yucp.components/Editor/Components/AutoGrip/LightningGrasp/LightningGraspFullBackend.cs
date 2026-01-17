#if YUCP_INTERNAL_LG
using System.Collections.Generic;
using UnityEngine;
using YUCP.Components.HandPoses;
using YUCP.Components.Editor.AutoGrip.LightningGrasp;

namespace YUCP.Components.Editor.AutoGrip
{
    /// <summary>
    /// Lightning Grasp backend implementing full grasp synthesis pipeline.
    /// Only available when YUCP_INTERNAL_LG is defined.
    /// </summary>
    public class LightningGraspFullBackend : IGraspSynthesizerBackend
    {
        private LightningGraspPipeline pipeline;
        private LightningGraspPipeline.PipelineConfig config;
        private GraspResult cachedResult;
        private Dictionary<HumanBodyBones, ContactDomain> cachedDomains;
        private int[,] cachedInteractionMatrix;

        public LightningGraspFullBackend()
        {
            config = new LightningGraspPipeline.PipelineConfig();
        }

        public YUCPHandDescriptor SynthesizeGrasp(
            Animator animator,
            bool isLeftHand,
            Collider[] propColliders,
            float fingerPadding,
            AutoGripData data)
        {
            // Initialize pipeline if needed
            if (pipeline == null)
            {
                pipeline = new LightningGraspPipeline(animator, isLeftHand, config);
                pipeline.LoadShaders();
            }

            // Build contact field if not already
            var contactField = pipeline.GetContactField();
            if (contactField == null)
            {
                var skinMesh = GetSkinnedMeshRenderer(animator);
                pipeline.BuildContactField(skinMesh);
            }

            // Sample object surface points
            var (objectPoints, objectNormals) = SamplePropSurface(propColliders, config.contactFieldConfig.maxObjectPoints);

            // Run full pipeline
            cachedResult = pipeline.SynthesizeGrasp(objectPoints, objectNormals, propColliders);

            if (cachedResult == null || cachedResult.jointAngles == null)
            {
                Debug.LogError("[LightningGraspFullBackend] Pipeline returned null result");
                return null;
            }

            // Convert to YUCPHandDescriptor
            return ConvertToHandDescriptor(animator, isLeftHand, cachedResult.jointAngles);
        }

        private SkinnedMeshRenderer GetSkinnedMeshRenderer(Animator animator)
        {
            // Find body mesh
            var skinMeshes = animator.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var sm in skinMeshes)
            {
                if (sm.sharedMesh != null && sm.sharedMesh.vertexCount > 1000)
                {
                    return sm;
                }
            }
            return skinMeshes.Length > 0 ? skinMeshes[0] : null;
        }

        private (Vector3[], Vector3[]) SamplePropSurface(Collider[] colliders, int numSamples)
        {
            var points = new List<Vector3>();
            var normals = new List<Vector3>();

            // Combine bounds of all colliders
            if (colliders.Length == 0)
            {
                return (new Vector3[0], new Vector3[0]);
            }

            Bounds combinedBounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
            {
                combinedBounds.Encapsulate(colliders[i].bounds);
            }

            // Sample points within bounds
            int attempts = 0;
            int maxAttempts = numSamples * 10;

            while (points.Count < numSamples && attempts < maxAttempts)
            {
                attempts++;

                // Random direction from center
                Vector3 dir = Random.onUnitSphere;
                Vector3 rayStart = combinedBounds.center + dir * combinedBounds.extents.magnitude * 2f;

                Ray ray = new Ray(rayStart, -dir);

                foreach (var col in colliders)
                {
                    if (col.Raycast(ray, out RaycastHit hit, combinedBounds.extents.magnitude * 4f))
                    {
                        // Check for duplicates (approximately)
                        bool isDuplicate = false;
                        foreach (var existing in points)
                        {
                            if (Vector3.Distance(existing, hit.point) < 0.002f)
                            {
                                isDuplicate = true;
                                break;
                            }
                        }

                        if (!isDuplicate)
                        {
                            points.Add(hit.point);
                            normals.Add(hit.normal);
                        }
                        break;
                    }
                }
            }

            // Also try mesh-based sampling if MeshColliders available
            foreach (var col in colliders)
            {
                if (col is MeshCollider mc && mc.sharedMesh != null && points.Count < numSamples)
                {
                    var mesh = mc.sharedMesh;
                    var vertices = mesh.vertices;
                    var meshNormals = mesh.normals;

                    int samplesToAdd = Mathf.Min(numSamples - points.Count, vertices.Length / 10);
                    for (int i = 0; i < samplesToAdd; i++)
                    {
                        int idx = Random.Range(0, vertices.Length);
                        Vector3 worldPos = col.transform.TransformPoint(vertices[idx]);
                        Vector3 worldNormal = col.transform.TransformDirection(
                            meshNormals.Length > idx ? meshNormals[idx] : Vector3.up).normalized;

                        points.Add(worldPos);
                        normals.Add(worldNormal);
                    }
                }
            }

            return (points.ToArray(), normals.ToArray());
        }

        private YUCPHandDescriptor ConvertToHandDescriptor(
            Animator animator,
            bool isLeftHand,
            Dictionary<HumanBodyBones, Quaternion> jointAngles)
        {
            // Store original rotations
            var originalRotations = new Dictionary<HumanBodyBones, Quaternion>();
            foreach (var kvp in jointAngles)
            {
                var t = animator.GetBoneTransform(kvp.Key);
                if (t != null)
                {
                    originalRotations[kvp.Key] = t.localRotation;
                    t.localRotation = kvp.Value;
                }
            }

            try
            {
                // Capture the posed hand
                var descriptor = new YUCPHandDescriptor();
                var handSide = isLeftHand ? YUCPHandSide.Left : YUCPHandSide.Right;
                descriptor.Compute(animator, handSide);
                return descriptor;
            }
            finally
            {
                // Restore original rotations
                foreach (var kvp in originalRotations)
                {
                    var t = animator.GetBoneTransform(kvp.Key);
                    if (t != null)
                    {
                        t.localRotation = kvp.Value;
                    }
                }
            }
        }

        /// <summary>
        /// Get the cached grasp result for visualization.
        /// </summary>
        public GraspResult GetCachedResult() => cachedResult;

        /// <summary>
        /// Get contact field for visualization.
        /// </summary>
        public ContactField GetContactField() => pipeline?.GetContactField();

        /// <summary>
        /// Get contact domains for visualization.
        /// </summary>
        public Dictionary<HumanBodyBones, ContactDomain> GetCachedDomains() => cachedDomains;

        /// <summary>
        /// Force rebuild contact field.
        /// </summary>
        public void RebuildContactField(Animator animator, bool isLeftHand)
        {
            if (pipeline != null)
            {
                pipeline.Release();
            }
            
            pipeline = new LightningGraspPipeline(animator, isLeftHand, config);
            pipeline.LoadShaders();

            var skinMesh = GetSkinnedMeshRenderer(animator);
            pipeline.BuildContactField(skinMesh);
        }

        public void Release()
        {
            pipeline?.Release();
            pipeline = null;
            cachedResult = null;
            cachedDomains = null;
            cachedInteractionMatrix = null;
        }
    }
}
#endif
