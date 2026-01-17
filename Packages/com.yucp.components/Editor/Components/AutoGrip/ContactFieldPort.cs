#if YUCP_INTERNAL_LG
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.AutoGrip.LightningGrasp
{
    /// <summary>
    /// Ported concepts from Lightning Grasp's contact_field.py.
    /// Manages contact patches on hand links for grasp optimization.
    /// 
    /// NOTE: This is a simplified C# port of high-level concepts.
    /// The original uses PyTorch tensors and CUDA acceleration.
    /// This implementation uses CPU-based Unity math.
    /// </summary>
    public class ContactFieldPort
    {
        /// <summary>
        /// Contact patch data for a single link.
        /// </summary>
        public struct ContactPatch
        {
            public string linkName;
            public int linkId;
            public Vector3 centroid;       // Patch center in link-local space
            public Vector3 normal;         // Patch normal in link-local space
            public Vector3 keyVector;      // Direction vector for patch alignment
            public float minAngle;         // Minimum rotation angle
            public float maxAngle;         // Maximum rotation angle
            public float areaWeight;       // Relative contact area weight
        }

        /// <summary>
        /// Sampled contact point with metadata.
        /// </summary>
        public struct SampledContact
        {
            public int patchId;
            public int linkId;
            public Vector3 positionLocal;
            public Vector3 normalLocal;
            public Vector3 positionWorld;
            public Vector3 normalWorld;
            public float score;
        }

        private List<ContactPatch> patches = new List<ContactPatch>();
        private Dictionary<string, int> linkNameToId = new Dictionary<string, int>();

        /// <summary>
        /// Registers a contact patch on a hand link.
        /// Ported from ContactField._register_keyvector().
        /// </summary>
        public void RegisterPatch(
            string linkName,
            int linkId,
            Vector3 centroid,
            Vector3 normal,
            Vector3 keyVector,
            float minAngle = -30f,
            float maxAngle = 30f,
            float areaWeight = 1f)
        {
            patches.Add(new ContactPatch
            {
                linkName = linkName,
                linkId = linkId,
                centroid = centroid,
                normal = normal.normalized,
                keyVector = keyVector.normalized,
                minAngle = minAngle,
                maxAngle = maxAngle,
                areaWeight = areaWeight
            });

            if (!linkNameToId.ContainsKey(linkName))
            {
                linkNameToId[linkName] = linkId;
            }
        }

        /// <summary>
        /// Registers default contact patches for a humanoid hand.
        /// Creates patches on each finger segment.
        /// </summary>
        public void RegisterHumanoidHandPatches(Animator animator, bool isLeftHand)
        {
            var fingerBones = GetFingerBones(isLeftHand);

            int patchId = 0;
            foreach (var (fingerType, bones) in fingerBones)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    var bone = bones[i];
                    var transform = animator.GetBoneTransform(bone);
                    if (transform == null) continue;

                    // Fingertip patch (on distal segment)
                    if (i == bones.Length - 1)
                    {
                        RegisterPatch(
                            linkName: bone.ToString(),
                            linkId: (int)bone,
                            centroid: Vector3.forward * 0.01f, // Tip
                            normal: Vector3.forward,
                            keyVector: Vector3.up,
                            minAngle: -45f,
                            maxAngle: 45f,
                            areaWeight: 2f // Fingertips are important
                        );
                    }
                    else
                    {
                        // Proximal/intermediate patches (on sides)
                        RegisterPatch(
                            linkName: bone.ToString(),
                            linkId: (int)bone,
                            centroid: Vector3.down * 0.005f, // Finger pad side
                            normal: Vector3.down,
                            keyVector: Vector3.forward,
                            areaWeight: 1f
                        );
                    }

                    patchId++;
                }
            }

            // Palm patch
            var handBone = isLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            RegisterPatch(
                linkName: handBone.ToString(),
                linkId: (int)handBone,
                centroid: Vector3.forward * 0.03f,
                normal: isLeftHand ? Vector3.right : -Vector3.right, // Palm faces inward
                keyVector: Vector3.forward,
                minAngle: -15f,
                maxAngle: 15f,
                areaWeight: 3f // Palm is large contact area
            );
        }

        /// <summary>
        /// Samples contact geometry from a patch.
        /// Ported from ContactField.sample_contact_geometry().
        /// </summary>
        public SampledContact SampleContactGeometry(int patchId, Transform linkTransform, float angle = 0f)
        {
            if (patchId < 0 || patchId >= patches.Count)
            {
                return new SampledContact();
            }

            var patch = patches[patchId];

            // Rotate keyvector by angle around normal
            Quaternion rotation = Quaternion.AngleAxis(angle, patch.normal);
            Vector3 rotatedKeyVector = rotation * patch.keyVector;

            // Transform to world space
            Vector3 worldPos = linkTransform.TransformPoint(patch.centroid);
            Vector3 worldNormal = linkTransform.TransformDirection(patch.normal);

            return new SampledContact
            {
                patchId = patchId,
                linkId = patch.linkId,
                positionLocal = patch.centroid,
                normalLocal = patch.normal,
                positionWorld = worldPos,
                normalWorld = worldNormal.normalized,
                score = 0f
            };
        }

        /// <summary>
        /// Gets all patches for a specific link.
        /// </summary>
        public List<ContactPatch> GetPatchesForLink(int linkId)
        {
            return patches.FindAll(p => p.linkId == linkId);
        }

        /// <summary>
        /// Gets total number of registered patches.
        /// </summary>
        public int PatchCount => patches.Count;

        /// <summary>
        /// Gets all registered patches.
        /// </summary>
        public List<ContactPatch> AllPatches => new List<ContactPatch>(patches);

        private Dictionary<YUCPFingerType, HumanBodyBones[]> GetFingerBones(bool isLeftHand)
        {
            var result = new Dictionary<YUCPFingerType, HumanBodyBones[]>();

            if (isLeftHand)
            {
                result[YUCPFingerType.Thumb] = new[] {
                    HumanBodyBones.LeftThumbProximal,
                    HumanBodyBones.LeftThumbIntermediate,
                    HumanBodyBones.LeftThumbDistal
                };
                result[YUCPFingerType.Index] = new[] {
                    HumanBodyBones.LeftIndexProximal,
                    HumanBodyBones.LeftIndexIntermediate,
                    HumanBodyBones.LeftIndexDistal
                };
                result[YUCPFingerType.Middle] = new[] {
                    HumanBodyBones.LeftMiddleProximal,
                    HumanBodyBones.LeftMiddleIntermediate,
                    HumanBodyBones.LeftMiddleDistal
                };
                result[YUCPFingerType.Ring] = new[] {
                    HumanBodyBones.LeftRingProximal,
                    HumanBodyBones.LeftRingIntermediate,
                    HumanBodyBones.LeftRingDistal
                };
                result[YUCPFingerType.Little] = new[] {
                    HumanBodyBones.LeftLittleProximal,
                    HumanBodyBones.LeftLittleIntermediate,
                    HumanBodyBones.LeftLittleDistal
                };
            }
            else
            {
                result[YUCPFingerType.Thumb] = new[] {
                    HumanBodyBones.RightThumbProximal,
                    HumanBodyBones.RightThumbIntermediate,
                    HumanBodyBones.RightThumbDistal
                };
                result[YUCPFingerType.Index] = new[] {
                    HumanBodyBones.RightIndexProximal,
                    HumanBodyBones.RightIndexIntermediate,
                    HumanBodyBones.RightIndexDistal
                };
                result[YUCPFingerType.Middle] = new[] {
                    HumanBodyBones.RightMiddleProximal,
                    HumanBodyBones.RightMiddleIntermediate,
                    HumanBodyBones.RightMiddleDistal
                };
                result[YUCPFingerType.Ring] = new[] {
                    HumanBodyBones.RightRingProximal,
                    HumanBodyBones.RightRingIntermediate,
                    HumanBodyBones.RightRingDistal
                };
                result[YUCPFingerType.Little] = new[] {
                    HumanBodyBones.RightLittleProximal,
                    HumanBodyBones.RightLittleIntermediate,
                    HumanBodyBones.RightLittleDistal
                };
            }

            return result;
        }
    }
}
#endif
