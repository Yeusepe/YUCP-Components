using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.AutoGrip.LightningGrasp
{
    /// <summary>
    /// Forward kinematics for humanoid hand bones.
    /// Computes world transforms for all finger bones given an animator and joint configuration.
    /// </summary>
    public static class HandForwardKinematics
    {
        /// <summary>
        /// Finger bone chains for left hand.
        /// </summary>
        public static readonly HumanBodyBones[][] LeftHandChains = new[]
        {
            new[] { HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal },
            new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal },
            new[] { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal },
            new[] { HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal },
            new[] { HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal }
        };

        /// <summary>
        /// Finger bone chains for right hand.
        /// </summary>
        public static readonly HumanBodyBones[][] RightHandChains = new[]
        {
            new[] { HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal },
            new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal },
            new[] { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal },
            new[] { HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal },
            new[] { HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal }
        };

        /// <summary>
        /// Get finger chains for a hand side.
        /// </summary>
        public static HumanBodyBones[][] GetFingerChains(bool isLeftHand)
        {
            return isLeftHand ? LeftHandChains : RightHandChains;
        }

        /// <summary>
        /// Get wrist bone for a hand side.
        /// </summary>
        public static HumanBodyBones GetWristBone(bool isLeftHand)
        {
            return isLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
        }

        /// <summary>
        /// Get all finger bones for a hand (including wrist).
        /// </summary>
        public static List<HumanBodyBones> GetAllHandBones(bool isLeftHand)
        {
            var bones = new List<HumanBodyBones> { GetWristBone(isLeftHand) };
            var chains = GetFingerChains(isLeftHand);
            foreach (var chain in chains)
            {
                bones.AddRange(chain);
            }
            return bones;
        }

        /// <summary>
        /// Create a bone index mapping for GPU operations.
        /// </summary>
        public static Dictionary<HumanBodyBones, int> CreateBoneIndexMap(bool isLeftHand)
        {
            var bones = GetAllHandBones(isLeftHand);
            var map = new Dictionary<HumanBodyBones, int>();
            for (int i = 0; i < bones.Count; i++)
            {
                map[bones[i]] = i;
            }
            return map;
        }

        /// <summary>
        /// Compute world transforms for all hand bones.
        /// Returns array of Matrix4x4 indexed by bone index.
        /// </summary>
        public static Matrix4x4[] ComputeForwardKinematics(Animator animator, bool isLeftHand)
        {
            var bones = GetAllHandBones(isLeftHand);
            var transforms = new Matrix4x4[bones.Count];

            for (int i = 0; i < bones.Count; i++)
            {
                var t = animator.GetBoneTransform(bones[i]);
                if (t != null)
                {
                    transforms[i] = t.localToWorldMatrix;
                }
                else
                {
                    transforms[i] = Matrix4x4.identity;
                }
            }

            return transforms;
        }

        /// <summary>
        /// Apply a joint configuration (curl values per bone) and compute FK.
        /// </summary>
        public static Matrix4x4[] ComputeFKWithJoints(
            Animator animator,
            bool isLeftHand,
            Dictionary<HumanBodyBones, Quaternion> jointRotations)
        {
            var bones = GetAllHandBones(isLeftHand);
            var originalRotations = new Dictionary<HumanBodyBones, Quaternion>();

            // Store original rotations
            foreach (var bone in bones)
            {
                var t = animator.GetBoneTransform(bone);
                if (t != null)
                {
                    originalRotations[bone] = t.localRotation;
                }
            }

            // Apply joint rotations
            foreach (var kvp in jointRotations)
            {
                var t = animator.GetBoneTransform(kvp.Key);
                if (t != null)
                {
                    t.localRotation = originalRotations.GetValueOrDefault(kvp.Key, Quaternion.identity) * kvp.Value;
                }
            }

            // Compute FK
            var result = ComputeForwardKinematics(animator, isLeftHand);

            // Restore original rotations
            foreach (var kvp in originalRotations)
            {
                var t = animator.GetBoneTransform(kvp.Key);
                if (t != null)
                {
                    t.localRotation = kvp.Value;
                }
            }

            return result;
        }

        /// <summary>
        /// Sample random joint configurations within humanoid limits.
        /// Returns Dictionary of bone -> rotation for each sample.
        /// </summary>
        public static Dictionary<HumanBodyBones, Quaternion>[] SampleRandomJointConfigs(
            bool isLeftHand,
            int numSamples,
            float maxCurlDegrees = 90f,
            float maxSpreadDegrees = 20f)
        {
            var configs = new Dictionary<HumanBodyBones, Quaternion>[numSamples];
            var chains = GetFingerChains(isLeftHand);

            for (int s = 0; s < numSamples; s++)
            {
                var config = new Dictionary<HumanBodyBones, Quaternion>();

                foreach (var chain in chains)
                {
                    // Random curl for entire finger
                    float fingerCurl = Random.Range(0f, maxCurlDegrees);
                    float fingerSpread = Random.Range(-maxSpreadDegrees, maxSpreadDegrees);

                    for (int i = 0; i < chain.Length; i++)
                    {
                        // Distribute curl: proximal gets most, distal gets least
                        float segmentCurl = fingerCurl * (i == 0 ? 0.5f : i == 1 ? 0.35f : 0.15f);

                        // Only proximal gets spread
                        float segmentSpread = (i == 0) ? fingerSpread : 0f;

                        config[chain[i]] = Quaternion.Euler(segmentCurl, segmentSpread, 0f);
                    }
                }

                configs[s] = config;
            }

            return configs;
        }

        /// <summary>
        /// Batch compute FK for multiple joint configurations.
        /// Returns: [numSamples, numBones] array of matrices.
        /// </summary>
        public static Matrix4x4[,] BatchComputeFK(
            Animator animator,
            bool isLeftHand,
            Dictionary<HumanBodyBones, Quaternion>[] jointConfigs)
        {
            var bones = GetAllHandBones(isLeftHand);
            var results = new Matrix4x4[jointConfigs.Length, bones.Count];

            for (int s = 0; s < jointConfigs.Length; s++)
            {
                var transforms = ComputeFKWithJoints(animator, isLeftHand, jointConfigs[s]);
                for (int b = 0; b < bones.Count; b++)
                {
                    results[s, b] = transforms[b];
                }
            }

            return results;
        }
    }
}
