using System;
using System.Collections.Generic;
using UnityEngine;
using YUCP.Components.HandPoses;

namespace YUCP.Components.Editor.AutoGrip.LightningGrasp
{
    /// <summary>
    /// Configuration for contact field generation.
    /// </summary>
    [Serializable]
    public class ContactFieldConfig
    {
        [Header("Sampling")]
        [Tooltip("Number of joint configurations to sample for field generation.")]
        [Range(1000, 100000)]
        public int jointSamples = 20000;

        [Tooltip("Number of keyvectors per patch.")]
        [Range(4, 64)]
        public int keyvectorsPerPatch = 16;

        [Header("Patch Clustering")]
        [Tooltip("Position tolerance for grouping keyvectors into patches (meters).")]
        public float patchPositionTolerance = 0.0075f;

        [Tooltip("Rotation tolerance for grouping keyvectors (radians).")]
        public float patchRotationTolerance = 0.47f; // ~27 degrees

        [Header("Filtering")]
        [Tooltip("Minimum patch size to include.")]
        public int minPatchSize = 4;

        [Tooltip("Include palm contact patches.")]
        public bool includePalm = true;

        [Header("GPU")]
        [Tooltip("Use GPU compute for acceleration structure.")]
        public bool useGPU = true;

        [Tooltip("Maximum object points for interaction query.")]
        public int maxObjectPoints = 4096;

        public static ContactFieldConfig Default => new ContactFieldConfig();
    }

    /// <summary>
    /// The ContactField contains all contact patches for a hand.
    /// This is the main data structure for Lightning Grasp's contact field sampling.
    /// </summary>
    [Serializable]
    public class ContactField
    {
        /// <summary>
        /// All movable contact patches (on finger segments).
        /// </summary>
        public List<ContactPatch> patches = new List<ContactPatch>();

        /// <summary>
        /// Static contact patches (palm, cannot move independently).
        /// </summary>
        public List<ContactPatch> staticPatches = new List<ContactPatch>();

        /// <summary>
        /// Configuration used to build this field.
        /// </summary>
        public ContactFieldConfig config;

        /// <summary>
        /// Mapping: patch index → parent bone.
        /// </summary>
        private Dictionary<int, HumanBodyBones> patchToBone = new Dictionary<int, HumanBodyBones>();

        /// <summary>
        /// Mapping: bone → list of patch indices.
        /// </summary>
        private Dictionary<HumanBodyBones, List<int>> boneToPatchIds = new Dictionary<HumanBodyBones, List<int>>();

        /// <summary>
        /// List of all contact link names (bones that have patches).
        /// </summary>
        public List<HumanBodyBones> contactLinkBones = new List<HumanBodyBones>();

        /// <summary>
        /// Total number of keyvectors across all patches.
        /// </summary>
        public int TotalKeyVectorCount
        {
            get
            {
                int count = 0;
                foreach (var patch in patches) count += patch.KeyVectorCount;
                return count;
            }
        }

        /// <summary>
        /// Register a new patch to the contact field.
        /// </summary>
        public void RegisterPatch(ContactPatch patch)
        {
            if (patch.keyvectors.Count < (config?.minPatchSize ?? 1))
            {
                Debug.LogWarning($"[ContactField] Skipping patch with only {patch.keyvectors.Count} keyvectors");
                return;
            }

            patch.patchId = patches.Count;
            patches.Add(patch);

            patchToBone[patch.patchId] = patch.parentBone;

            if (!boneToPatchIds.ContainsKey(patch.parentBone))
            {
                boneToPatchIds[patch.parentBone] = new List<int>();
                contactLinkBones.Add(patch.parentBone);
            }
            boneToPatchIds[patch.parentBone].Add(patch.patchId);
        }

        /// <summary>
        /// Register a static (palm) patch.
        /// </summary>
        public void RegisterStaticPatch(ContactPatch patch)
        {
            patch.patchId = -1 - staticPatches.Count; // Negative IDs for static patches
            staticPatches.Add(patch);
        }

        /// <summary>
        /// Get all patch IDs for a specific bone.
        /// </summary>
        public List<int> GetPatchIdsForBone(HumanBodyBones bone)
        {
            if (boneToPatchIds.TryGetValue(bone, out var ids))
                return ids;
            return new List<int>();
        }

        /// <summary>
        /// Get patch by ID.
        /// </summary>
        public ContactPatch GetPatch(int patchId)
        {
            if (patchId < 0)
            {
                int staticIdx = -1 - patchId;
                if (staticIdx >= 0 && staticIdx < staticPatches.Count)
                    return staticPatches[staticIdx];
                return null;
            }
            if (patchId >= 0 && patchId < patches.Count)
                return patches[patchId];
            return null;
        }

        /// <summary>
        /// Sample contact geometry from a patch.
        /// </summary>
        public KeyVector SampleContactGeometry(int patchId, int localKeyVectorId = -1)
        {
            var patch = GetPatch(patchId);
            if (patch == null) return new KeyVector(Vector3.zero, Vector3.up);

            if (localKeyVectorId < 0)
                return patch.centroid;

            return patch.GetKeyVector(localKeyVectorId);
        }

        /// <summary>
        /// Get all parent link (bone) names that have contact patches.
        /// </summary>
        public List<HumanBodyBones> GetAllContactLinkBones()
        {
            return new List<HumanBodyBones>(contactLinkBones);
        }

        /// <summary>
        /// Create a selection tensor for filtering patches by link.
        /// Returns: [n_links, n_patches] boolean matrix.
        /// </summary>
        public bool[,] CreateLinkSelectionMatrix()
        {
            var matrix = new bool[contactLinkBones.Count, patches.Count];

            for (int linkIdx = 0; linkIdx < contactLinkBones.Count; linkIdx++)
            {
                var bone = contactLinkBones[linkIdx];
                var patchIds = GetPatchIdsForBone(bone);
                foreach (var patchId in patchIds)
                {
                    if (patchId >= 0 && patchId < patches.Count)
                        matrix[linkIdx, patchId] = true;
                }
            }

            return matrix;
        }

        /// <summary>
        /// Pack all keyvectors into GPU buffers.
        /// </summary>
        public void PackForGPU(
            out Vector4[] positions,
            out Vector4[] normals,
            out int[] patchOffsets,
            out int[] patchBoneIndices)
        {
            int totalKV = TotalKeyVectorCount;
            positions = new Vector4[totalKV];
            normals = new Vector4[totalKV];
            patchOffsets = new int[patches.Count + 1];
            patchBoneIndices = new int[patches.Count];

            int offset = 0;
            for (int p = 0; p < patches.Count; p++)
            {
                var patch = patches[p];
                patchOffsets[p] = offset;
                patchBoneIndices[p] = patch.boneIndex;

                for (int k = 0; k < patch.keyvectors.Count; k++)
                {
                    patch.keyvectors[k].Pack(out positions[offset], out normals[offset]);
                    offset++;
                }
            }
            patchOffsets[patches.Count] = offset;
        }

        /// <summary>
        /// Clear all patches.
        /// </summary>
        public void Clear()
        {
            patches.Clear();
            staticPatches.Clear();
            patchToBone.Clear();
            boneToPatchIds.Clear();
            contactLinkBones.Clear();
        }
    }
}
