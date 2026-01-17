using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.AutoGrip.LightningGrasp
{
    /// <summary>
    /// A contact patch is a collection of keyvectors on a single finger link.
    /// Each patch represents a region on the finger surface that can make contact with objects.
    /// Multiple patches may exist on a single link (e.g., fingertip pad, side surfaces).
    /// </summary>
    [Serializable]
    public class ContactPatch
    {
        /// <summary>
        /// The humanoid bone this patch is attached to.
        /// </summary>
        public HumanBodyBones parentBone;

        /// <summary>
        /// Index of the bone in the kinematic chain (for GPU batching).
        /// </summary>
        public int boneIndex;

        /// <summary>
        /// All keyvectors in this patch (in link-local space).
        /// </summary>
        public List<KeyVector> keyvectors = new List<KeyVector>();

        /// <summary>
        /// Centroid of the patch (average of all keyvector positions).
        /// </summary>
        public KeyVector centroid;

        /// <summary>
        /// Valid contact angle range (radians). Contacts outside this cone are filtered.
        /// </summary>
        public float angleRange = Mathf.PI * 0.5f; // Default: 90 degrees

        /// <summary>
        /// Unique ID for this patch within the contact field.
        /// </summary>
        public int patchId;

        public ContactPatch(HumanBodyBones bone, int boneIndex)
        {
            this.parentBone = bone;
            this.boneIndex = boneIndex;
        }

        /// <summary>
        /// Add a keyvector to this patch and update centroid.
        /// </summary>
        public void AddKeyVector(KeyVector kv)
        {
            keyvectors.Add(kv);
            UpdateCentroid();
        }

        /// <summary>
        /// Add multiple keyvectors at once.
        /// </summary>
        public void AddKeyVectors(IEnumerable<KeyVector> kvs)
        {
            keyvectors.AddRange(kvs);
            UpdateCentroid();
        }

        private void UpdateCentroid()
        {
            if (keyvectors.Count == 0)
            {
                centroid = new KeyVector(Vector3.zero, Vector3.up);
                return;
            }

            Vector3 avgPos = Vector3.zero;
            Vector3 avgNormal = Vector3.zero;

            foreach (var kv in keyvectors)
            {
                avgPos += kv.position;
                avgNormal += kv.normal;
            }

            avgPos /= keyvectors.Count;
            avgNormal = avgNormal.normalized;

            centroid = new KeyVector(avgPos, avgNormal);
        }

        /// <summary>
        /// Transform all keyvectors by a bone transform.
        /// Returns new transformed keyvectors (original patch is unchanged).
        /// </summary>
        public KeyVector[] TransformAll(Matrix4x4 boneLocalToWorld)
        {
            var result = new KeyVector[keyvectors.Count];
            for (int i = 0; i < keyvectors.Count; i++)
            {
                result[i] = keyvectors[i].Transform(boneLocalToWorld);
            }
            return result;
        }

        /// <summary>
        /// Sample a random keyvector from this patch.
        /// </summary>
        public KeyVector SampleRandom()
        {
            if (keyvectors.Count == 0) return centroid;
            return keyvectors[UnityEngine.Random.Range(0, keyvectors.Count)];
        }

        /// <summary>
        /// Get keyvector by index, or centroid if index is -1.
        /// </summary>
        public KeyVector GetKeyVector(int index)
        {
            if (index < 0 || index >= keyvectors.Count) return centroid;
            return keyvectors[index];
        }

        /// <summary>
        /// Check if a target normal is within the valid angle range.
        /// </summary>
        public bool IsValidContactAngle(Vector3 targetNormal)
        {
            float cosAngle = Vector3.Dot(centroid.normal, -targetNormal);
            return cosAngle > Mathf.Cos(angleRange);
        }

        public int KeyVectorCount => keyvectors.Count;
    }
}
