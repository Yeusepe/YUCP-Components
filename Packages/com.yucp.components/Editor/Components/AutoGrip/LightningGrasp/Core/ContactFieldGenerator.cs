using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace YUCP.Components.Editor.AutoGrip.LightningGrasp
{
    /// <summary>
    /// Generates contact fields by sampling contact patches across joint configurations.
    /// This builds the reachable workspace for each contact patch on the hand.
    /// </summary>
    public class ContactFieldGenerator
    {
        private Animator animator;
        private bool isLeftHand;
        private ContactFieldConfig config;

        public ContactFieldGenerator(Animator animator, bool isLeftHand, ContactFieldConfig config = null)
        {
            this.animator = animator;
            this.isLeftHand = isLeftHand;
            this.config = config ?? ContactFieldConfig.Default;
        }

        /// <summary>
        /// Build the contact field for the hand.
        /// This is the main entry point.
        /// </summary>
        public ContactField BuildContactField(SkinnedMeshRenderer skinMesh = null)
        {
            var field = new ContactField { config = config };

            // Step 1: Extract contact patches from mesh (or generate synthetic)
            var boneSamples = ExtractBonePatches(skinMesh);

            // Step 2: Create patches from samples
            var boneIndexMap = HandForwardKinematics.CreateBoneIndexMap(isLeftHand);

            foreach (var boneSample in boneSamples)
            {
                if (!boneIndexMap.TryGetValue(boneSample.bone, out int boneIdx))
                    continue;

                // Cluster samples into patches
                var patches = ClusterIntoPatchGroups(boneSample.samples, boneSample.bone, boneIdx);

                foreach (var patch in patches)
                {
                    // Determine if static (palm) or movable (finger)
                    if (IsStaticBone(boneSample.bone))
                    {
                        field.RegisterStaticPatch(patch);
                    }
                    else
                    {
                        field.RegisterPatch(patch);
                    }
                }
            }

            Debug.Log($"[ContactFieldGenerator] Built contact field: {field.patches.Count} movable patches, " +
                      $"{field.staticPatches.Count} static patches, {field.TotalKeyVectorCount} keyvectors");

            return field;
        }

        /// <summary>
        /// Sample the contact field across joint configurations.
        /// This generates the reachable workspace data used for BVH construction.
        /// </summary>
        public ContactFieldSamples SampleContactField(ContactField field)
        {
            var samples = new ContactFieldSamples();
            samples.patchSamples = new List<PatchSampleSet>();

            // Generate random joint configurations
            var jointConfigs = HandForwardKinematics.SampleRandomJointConfigs(
                isLeftHand, config.jointSamples);

            int progressInterval = config.jointSamples / 10;
            float startTime = Time.realtimeSinceStartup;

            for (int patchIdx = 0; patchIdx < field.patches.Count; patchIdx++)
            {
                var patch = field.patches[patchIdx];
                var sampleSet = new PatchSampleSet
                {
                    patchId = patchIdx,
                    bone = patch.parentBone,
                    worldPositions = new List<Vector3>(),
                    worldNormals = new List<Vector3>()
                };

                // Sample each joint config
                for (int s = 0; s < jointConfigs.Length; s++)
                {
                    // Apply joint config and compute FK
                    var boneTransforms = HandForwardKinematics.ComputeFKWithJoints(
                        animator, isLeftHand, jointConfigs[s]);

                    // Transform patch keyvectors
                    var boneMatrix = boneTransforms[patch.boneIndex];
                    var transformedKVs = patch.TransformAll(boneMatrix);

                    // Store world positions and normals
                    foreach (var kv in transformedKVs)
                    {
                        sampleSet.worldPositions.Add(kv.position);
                        sampleSet.worldNormals.Add(kv.normal);
                    }

                    // Progress update
                    if (s % progressInterval == 0)
                    {
                        float progress = (patchIdx * jointConfigs.Length + s) /
                                        (float)(field.patches.Count * jointConfigs.Length);
                        EditorUtility.DisplayProgressBar(
                            "Sampling Contact Field",
                            $"Patch {patchIdx + 1}/{field.patches.Count}, Sample {s}/{jointConfigs.Length}",
                            progress);
                    }
                }

                samples.patchSamples.Add(sampleSet);
            }

            EditorUtility.ClearProgressBar();

            float elapsed = Time.realtimeSinceStartup - startTime;
            Debug.Log($"[ContactFieldGenerator] Sampled {config.jointSamples} configs in {elapsed:F2}s");

            return samples;
        }

        private List<FingerMeshSampler.BoneMeshSamples> ExtractBonePatches(SkinnedMeshRenderer skinMesh)
        {
            if (skinMesh != null)
            {
                return FingerMeshSampler.SampleAllFingerPatches(animator, skinMesh, isLeftHand, config);
            }

            // Fallback: generate synthetic patches
            var results = new List<FingerMeshSampler.BoneMeshSamples>();
            var bones = HandForwardKinematics.GetAllHandBones(isLeftHand);

            foreach (var bone in bones)
            {
                var samples = FingerMeshSampler.GenerateSyntheticPatches(
                    animator, bone, config.keyvectorsPerPatch);

                if (samples.Count > 0)
                {
                    results.Add(new FingerMeshSampler.BoneMeshSamples
                    {
                        bone = bone,
                        samples = samples
                    });
                }
            }

            return results;
        }

        private List<ContactPatch> ClusterIntoPatchGroups(
            List<KeyVector> samples,
            HumanBodyBones bone,
            int boneIndex)
        {
            var patches = new List<ContactPatch>();

            if (samples.Count == 0) return patches;

            // Group samples by normal direction
            var normalGroups = new Dictionary<int, List<KeyVector>>();

            foreach (var sample in samples)
            {
                // Quantize normal to octant
                int octant = GetNormalOctant(sample.normal);

                if (!normalGroups.ContainsKey(octant))
                {
                    normalGroups[octant] = new List<KeyVector>();
                }
                normalGroups[octant].Add(sample);
            }

            // Create a patch for each group
            foreach (var group in normalGroups.Values)
            {
                if (group.Count < config.minPatchSize) continue;

                var patch = new ContactPatch(bone, boneIndex);
                patch.AddKeyVectors(group);

                // Compute angle range from normal variance
                patch.angleRange = ComputeAngleRange(group);

                patches.Add(patch);
            }

            return patches;
        }

        private int GetNormalOctant(Vector3 normal)
        {
            int x = normal.x > 0 ? 1 : 0;
            int y = normal.y > 0 ? 2 : 0;
            int z = normal.z > 0 ? 4 : 0;
            return x | y | z;
        }

        private float ComputeAngleRange(List<KeyVector> samples)
        {
            if (samples.Count < 2) return Mathf.PI * 0.25f;

            Vector3 avgNormal = Vector3.zero;
            foreach (var s in samples) avgNormal += s.normal;
            avgNormal.Normalize();

            float maxAngle = 0f;
            foreach (var s in samples)
            {
                float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(s.normal, avgNormal), -1f, 1f));
                maxAngle = Mathf.Max(maxAngle, angle);
            }

            return Mathf.Max(maxAngle * 1.2f, Mathf.PI * 0.15f); // Add margin
        }

        private bool IsStaticBone(HumanBodyBones bone)
        {
            return bone == HumanBodyBones.LeftHand || bone == HumanBodyBones.RightHand;
        }
    }

    /// <summary>
    /// Samples collected from contact field generation.
    /// </summary>
    public class ContactFieldSamples
    {
        public List<PatchSampleSet> patchSamples;

        /// <summary>
        /// Pack all samples into GPU-friendly arrays.
        /// </summary>
        public void PackForGPU(
            out Vector4[] positions,
            out Vector4[] normals,
            out int[] patchOffsets)
        {
            int totalSamples = 0;
            foreach (var ps in patchSamples)
            {
                totalSamples += ps.worldPositions.Count;
            }

            positions = new Vector4[totalSamples];
            normals = new Vector4[totalSamples];
            patchOffsets = new int[patchSamples.Count + 1];

            int offset = 0;
            for (int p = 0; p < patchSamples.Count; p++)
            {
                var ps = patchSamples[p];
                patchOffsets[p] = offset;

                for (int i = 0; i < ps.worldPositions.Count; i++)
                {
                    positions[offset] = new Vector4(
                        ps.worldPositions[i].x,
                        ps.worldPositions[i].y,
                        ps.worldPositions[i].z,
                        1f);
                    normals[offset] = new Vector4(
                        ps.worldNormals[i].x,
                        ps.worldNormals[i].y,
                        ps.worldNormals[i].z,
                        0f);
                    offset++;
                }
            }
            patchOffsets[patchSamples.Count] = offset;
        }
    }

    /// <summary>
    /// Sample set for a single patch.
    /// </summary>
    public class PatchSampleSet
    {
        public int patchId;
        public HumanBodyBones bone;
        public List<Vector3> worldPositions;
        public List<Vector3> worldNormals;
    }
}
