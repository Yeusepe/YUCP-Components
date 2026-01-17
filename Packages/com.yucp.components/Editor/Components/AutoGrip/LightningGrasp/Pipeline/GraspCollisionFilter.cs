using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.AutoGrip.LightningGrasp
{
    /// <summary>
    /// Collision filtering for grasp validation.
    /// Checks for self-collision and object penetration.
    /// </summary>
    public class GraspCollisionFilter
    {
        /// <summary>
        /// Collision filter configuration.
        /// </summary>
        public class Config
        {
            public float penetrationThreshold = 0.002f;  // 2mm
            public float selfCollisionMargin = 0.003f;   // 3mm
            public bool checkSelfCollision = true;
            public bool checkObjectPenetration = true;
        }

        private Config config;

        public GraspCollisionFilter(Config config = null)
        {
            this.config = config ?? new Config();
        }

        /// <summary>
        /// Validate a grasp solution.
        /// </summary>
        public GraspValidationResult ValidateGrasp(
            Animator animator,
            bool isLeftHand,
            Dictionary<HumanBodyBones, Quaternion> jointAngles,
            Collider[] propColliders)
        {
            var result = new GraspValidationResult { isValid = true };

            // Apply joint angles
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
                // Check object penetration
                if (config.checkObjectPenetration)
                {
                    var penetrations = CheckObjectPenetration(animator, isLeftHand, propColliders);
                    if (penetrations.Count > 0)
                    {
                        result.isValid = false;
                        result.penetrationBones = penetrations;
                    }
                }

                // Check self-collision
                if (config.checkSelfCollision)
                {
                    var selfCollisions = CheckSelfCollision(animator, isLeftHand);
                    if (selfCollisions.Count > 0)
                    {
                        result.isValid = false;
                        result.selfCollisionPairs = selfCollisions;
                    }
                }
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

            return result;
        }

        /// <summary>
        /// Check for finger-object penetration.
        /// </summary>
        public List<HumanBodyBones> CheckObjectPenetration(
            Animator animator,
            bool isLeftHand,
            Collider[] propColliders)
        {
            var penetrations = new List<HumanBodyBones>();
            var bones = HandForwardKinematics.GetAllHandBones(isLeftHand);

            foreach (var bone in bones)
            {
                var t = animator.GetBoneTransform(bone);
                if (t == null) continue;

                // Create capsule for bone
                float radius = FingerMeshSampler.EstimateBoneRadius(animator, bone);
                Vector3 start = t.position;
                Vector3 end = t.childCount > 0 
                    ? t.GetChild(0).position 
                    : t.position + t.forward * 0.01f;

                // Check against all prop colliders
                foreach (var col in propColliders)
                {
                    if (col == null) continue;

                    // Approximate with sphere checks at start/end
                    if (Physics.CheckSphere(start, radius, LayerMask.GetMask("Default")))
                    {
                        penetrations.Add(bone);
                        break;
                    }

                    // More accurate: ComputePenetration
                    Vector3 closest = col.ClosestPoint(start);
                    if (Vector3.Distance(closest, start) < radius - config.penetrationThreshold)
                    {
                        penetrations.Add(bone);
                        break;
                    }
                }
            }

            return penetrations;
        }

        /// <summary>
        /// Check for self-collision between finger segments.
        /// </summary>
        public List<(HumanBodyBones, HumanBodyBones)> CheckSelfCollision(
            Animator animator,
            bool isLeftHand)
        {
            var collisions = new List<(HumanBodyBones, HumanBodyBones)>();
            var chains = HandForwardKinematics.GetFingerChains(isLeftHand);

            // Check each finger against other fingers
            for (int f1 = 0; f1 < chains.Length; f1++)
            {
                for (int f2 = f1 + 1; f2 < chains.Length; f2++)
                {
                    // Check all bone pairs between fingers
                    foreach (var bone1 in chains[f1])
                    {
                        var t1 = animator.GetBoneTransform(bone1);
                        if (t1 == null) continue;
                        float r1 = FingerMeshSampler.EstimateBoneRadius(animator, bone1);

                        foreach (var bone2 in chains[f2])
                        {
                            var t2 = animator.GetBoneTransform(bone2);
                            if (t2 == null) continue;
                            float r2 = FingerMeshSampler.EstimateBoneRadius(animator, bone2);

                            float dist = Vector3.Distance(t1.position, t2.position);
                            if (dist < r1 + r2 + config.selfCollisionMargin)
                            {
                                collisions.Add((bone1, bone2));
                            }
                        }
                    }
                }
            }

            return collisions;
        }

        /// <summary>
        /// Filter batch of grasp candidates, keeping only valid ones.
        /// </summary>
        public List<int> FilterValidGrasps(
            Animator animator,
            bool isLeftHand,
            List<Dictionary<HumanBodyBones, Quaternion>> candidates,
            Collider[] propColliders)
        {
            var validIndices = new List<int>();

            for (int i = 0; i < candidates.Count; i++)
            {
                var result = ValidateGrasp(animator, isLeftHand, candidates[i], propColliders);
                if (result.isValid)
                {
                    validIndices.Add(i);
                }
            }

            return validIndices;
        }

        /// <summary>
        /// Assign "free" fingers (not used for grasping) to a natural pose.
        /// </summary>
        public void AssignFreeFingersNaturalPose(
            Animator animator,
            bool isLeftHand,
            HashSet<HumanBodyBones> usedBones,
            float defaultCurl = 30f)
        {
            var chains = HandForwardKinematics.GetFingerChains(isLeftHand);

            foreach (var chain in chains)
            {
                // Check if any bone in chain is used
                bool isUsed = false;
                foreach (var bone in chain)
                {
                    if (usedBones.Contains(bone))
                    {
                        isUsed = true;
                        break;
                    }
                }

                if (isUsed) continue;

                // Apply default curl to unused fingers
                for (int i = 0; i < chain.Length; i++)
                {
                    var t = animator.GetBoneTransform(chain[i]);
                    if (t == null) continue;

                    float segmentCurl = defaultCurl * (i == 0 ? 0.5f : i == 1 ? 0.35f : 0.15f);
                    t.localRotation = t.localRotation * Quaternion.Euler(segmentCurl, 0, 0);
                }
            }
        }
    }

    /// <summary>
    /// Result of grasp validation.
    /// </summary>
    public class GraspValidationResult
    {
        public bool isValid;
        public List<HumanBodyBones> penetrationBones;
        public List<(HumanBodyBones, HumanBodyBones)> selfCollisionPairs;
    }
}
