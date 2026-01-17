using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.AutoGrip.LightningGrasp
{
    /// <summary>
    /// Samples contact patch candidates from finger mesh geometry.
    /// Extracts surface points and normals from the avatar mesh near finger bones.
    /// </summary>
    public static class FingerMeshSampler
    {
        /// <summary>
        /// Result of mesh sampling for a single bone.
        /// </summary>
        public struct BoneMeshSamples
        {
            public HumanBodyBones bone;
            public List<KeyVector> samples;
        }

        /// <summary>
        /// Sample contact patches from avatar mesh for all finger bones.
        /// </summary>
        public static List<BoneMeshSamples> SampleAllFingerPatches(
            Animator animator,
            SkinnedMeshRenderer skinMesh,
            bool isLeftHand,
            ContactFieldConfig config)
        {
            var results = new List<BoneMeshSamples>();

            var bones = HandForwardKinematics.GetAllHandBones(isLeftHand);

            foreach (var bone in bones)
            {
                var t = animator.GetBoneTransform(bone);
                if (t == null) continue;

                var samples = SampleBoneMesh(animator, skinMesh, bone, config);
                if (samples.Count > 0)
                {
                    results.Add(new BoneMeshSamples { bone = bone, samples = samples });
                }
            }

            return results;
        }

        /// <summary>
        /// Sample mesh vertices near a specific bone.
        /// </summary>
        public static List<KeyVector> SampleBoneMesh(
            Animator animator,
            SkinnedMeshRenderer skinMesh,
            HumanBodyBones bone,
            ContactFieldConfig config)
        {
            var boneTransform = animator.GetBoneTransform(bone);
            if (boneTransform == null) return new List<KeyVector>();

            // Get bone bounds for filtering
            float boneRadius = EstimateBoneRadius(animator, bone);

            // Bake mesh to get current vertex positions
            var bakedMesh = new Mesh();
            skinMesh.BakeMesh(bakedMesh);

            var vertices = bakedMesh.vertices;
            var normals = bakedMesh.normals;

            if (normals == null || normals.Length != vertices.Length)
            {
                bakedMesh.RecalculateNormals();
                normals = bakedMesh.normals;
            }

            var samples = new List<KeyVector>();
            Vector3 bonePos = skinMesh.transform.InverseTransformPoint(boneTransform.position);

            // Get bone forward direction (towards child)
            Vector3 boneForward = GetBoneForward(animator, bone);
            boneForward = skinMesh.transform.InverseTransformDirection(boneForward);

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertexPos = vertices[i];
                float dist = Vector3.Distance(vertexPos, bonePos);

                // Only include vertices within bone radius
                if (dist > boneRadius * 1.5f) continue;

                // Filter by normal direction - only include outward-facing surfaces
                Vector3 toVertex = (vertexPos - bonePos).normalized;
                float normalAlignment = Vector3.Dot(normals[i], toVertex);

                // Keep surfaces that face outward (normal points away from bone center)
                if (normalAlignment < 0.3f) continue;

                // Filter by bone forward - prefer contact surfaces on finger pad side
                float forwardAlignment = Vector3.Dot(normals[i], -boneForward);
                if (forwardAlignment < -0.5f) continue; // Skip surfaces facing backward

                // Transform to bone-local space
                Vector3 localPos = boneTransform.InverseTransformPoint(
                    skinMesh.transform.TransformPoint(vertexPos));
                Vector3 localNormal = boneTransform.InverseTransformDirection(
                    skinMesh.transform.TransformDirection(normals[i]));

                samples.Add(new KeyVector(localPos, localNormal));
            }

            Object.DestroyImmediate(bakedMesh);

            // Cluster samples into patches
            return ClusterAndReduceSamples(samples, config);
        }

        /// <summary>
        /// Estimate radius of influence for a bone based on skeleton.
        /// </summary>
        public static float EstimateBoneRadius(Animator animator, HumanBodyBones bone)
        {
            var t = animator.GetBoneTransform(bone);
            if (t == null) return 0.01f;

            // Use distance to child as length, estimate radius as fraction of length
            if (t.childCount > 0)
            {
                float length = Vector3.Distance(t.position, t.GetChild(0).position);
                return Mathf.Clamp(length * 0.4f, 0.005f, 0.02f);
            }

            // For tip bones, estimate smaller
            return 0.008f;
        }

        /// <summary>
        /// Get the forward direction of a bone (towards its child).
        /// </summary>
        public static Vector3 GetBoneForward(Animator animator, HumanBodyBones bone)
        {
            var t = animator.GetBoneTransform(bone);
            if (t == null) return Vector3.forward;

            if (t.childCount > 0)
            {
                return (t.GetChild(0).position - t.position).normalized;
            }

            // For tip bones, use parent direction
            if (t.parent != null)
            {
                return (t.position - t.parent.position).normalized;
            }

            return t.forward;
        }

        /// <summary>
        /// Cluster samples by position and normal, reducing to representative keyvectors.
        /// </summary>
        private static List<KeyVector> ClusterAndReduceSamples(
            List<KeyVector> samples,
            ContactFieldConfig config)
        {
            if (samples.Count <= config.keyvectorsPerPatch)
                return samples;

            var clusters = new List<List<KeyVector>>();

            foreach (var sample in samples)
            {
                bool foundCluster = false;

                foreach (var cluster in clusters)
                {
                    var centroid = ComputeCentroid(cluster);

                    float posDist = Vector3.Distance(sample.position, centroid.position);
                    float normalDot = Vector3.Dot(sample.normal, centroid.normal);

                    if (posDist < config.patchPositionTolerance &&
                        normalDot > Mathf.Cos(config.patchRotationTolerance))
                    {
                        cluster.Add(sample);
                        foundCluster = true;
                        break;
                    }
                }

                if (!foundCluster)
                {
                    clusters.Add(new List<KeyVector> { sample });
                }
            }

            // Take centroid of each cluster
            var result = new List<KeyVector>();
            foreach (var cluster in clusters)
            {
                if (cluster.Count >= config.minPatchSize / 2)
                {
                    result.Add(ComputeCentroid(cluster));
                }
            }

            // Limit to max keyvectors per patch
            if (result.Count > config.keyvectorsPerPatch)
            {
                // Sort by cluster size (implicit in order) and take first N
                result = result.GetRange(0, config.keyvectorsPerPatch);
            }

            return result;
        }

        private static KeyVector ComputeCentroid(List<KeyVector> samples)
        {
            Vector3 avgPos = Vector3.zero;
            Vector3 avgNormal = Vector3.zero;

            foreach (var s in samples)
            {
                avgPos += s.position;
                avgNormal += s.normal;
            }

            return new KeyVector(avgPos / samples.Count, avgNormal.normalized);
        }

        /// <summary>
        /// Generate synthetic contact patches when mesh is unavailable.
        /// Creates circular patches around bone axis.
        /// </summary>
        public static List<KeyVector> GenerateSyntheticPatches(
            Animator animator,
            HumanBodyBones bone,
            int numPatches = 8,
            float radius = 0.006f)
        {
            var t = animator.GetBoneTransform(bone);
            if (t == null) return new List<KeyVector>();

            var samples = new List<KeyVector>();

            // Get bone forward direction
            Vector3 forward = GetBoneForward(animator, bone);
            Vector3 right = Vector3.Cross(forward, Vector3.up).normalized;
            if (right.magnitude < 0.1f)
            {
                right = Vector3.Cross(forward, Vector3.forward).normalized;
            }
            Vector3 up = Vector3.Cross(right, forward).normalized;

            // Generate points around the bone
            for (int i = 0; i < numPatches; i++)
            {
                float angle = (i / (float)numPatches) * Mathf.PI * 2f;

                // Skip the "inside" direction (towards palm for fingers)
                if (Mathf.Abs(angle - Mathf.PI) < 0.5f) continue;

                Vector3 dir = Mathf.Cos(angle) * right + Mathf.Sin(angle) * up;
                Vector3 pos = dir * radius;

                samples.Add(new KeyVector(pos, dir));
            }

            // Add fingertip patch
            float boneLength = EstimateBoneRadius(animator, bone) * 2f;
            samples.Add(new KeyVector(forward * boneLength, forward));

            return samples;
        }
    }
}
