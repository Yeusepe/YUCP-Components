using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.AutoGrip.LightningGrasp
{
    /// <summary>
    /// Inverse kinematics solver for finger bones.
    /// Given target contact points, solves for finger joint angles.
    /// </summary>
    public class FingerIKSolver
    {
        /// <summary>
        /// IK configuration.
        /// </summary>
        public class Config
        {
            public int maxIterations = 20;
            public float positionTolerance = 0.001f;  // 1mm
            public float normalTolerance = 0.1f;      // cos tolerance
            public float learningRate = 0.5f;
            public float maxCurlAngle = 90f;
            public float maxSpreadAngle = 30f;
        }

        private Config config;

        public FingerIKSolver(Config config = null)
        {
            this.config = config ?? new Config();
        }

        /// <summary>
        /// Solve IK for a single finger chain to reach a target position.
        /// </summary>
        public FingerIKResult SolveFinger(
            Animator animator,
            HumanBodyBones[] fingerChain,
            Vector3 targetPosition,
            Vector3 targetNormal)
        {
            var result = new FingerIKResult
            {
                success = false,
                jointAngles = new Dictionary<HumanBodyBones, Quaternion>()
            };

            if (fingerChain == null || fingerChain.Length == 0)
                return result;

            // Get transforms
            var transforms = new List<Transform>();
            foreach (var bone in fingerChain)
            {
                var t = animator.GetBoneTransform(bone);
                if (t == null) return result;
                transforms.Add(t);
            }

            // Get tip position (end of last bone)
            Transform tip = transforms[transforms.Count - 1];
            Vector3 tipOffset = GetTipOffset(tip);

            // Iterative IK
            for (int iter = 0; iter < config.maxIterations; iter++)
            {
                Vector3 currentTip = tip.TransformPoint(tipOffset);
                Vector3 error = targetPosition - currentTip;

                if (error.magnitude < config.positionTolerance)
                {
                    result.success = true;
                    break;
                }

                // Apply corrections from root to tip
                for (int j = 0; j < transforms.Count; j++)
                {
                    var joint = transforms[j];
                    Vector3 jointPos = joint.position;
                    Vector3 toTip = currentTip - jointPos;
                    Vector3 toTarget = targetPosition - jointPos;

                    if (toTip.magnitude < 0.001f || toTarget.magnitude < 0.001f)
                        continue;

                    // Compute rotation to align current tip direction with target direction
                    Quaternion correction = Quaternion.FromToRotation(toTip.normalized, toTarget.normalized);
                    correction = Quaternion.Slerp(Quaternion.identity, correction, config.learningRate);

                    // Apply with joint limits
                    joint.rotation = correction * joint.rotation;
                    ClampJointRotation(joint, fingerChain[j], j == 0);

                    // Update tip position
                    currentTip = tip.TransformPoint(tipOffset);
                }
            }

            // Store final joint angles
            for (int i = 0; i < transforms.Count; i++)
            {
                result.jointAngles[fingerChain[i]] = transforms[i].localRotation;
            }

            result.finalPosition = tip.TransformPoint(tipOffset);
            result.positionError = Vector3.Distance(result.finalPosition, targetPosition);

            return result;
        }

        /// <summary>
        /// Solve IK for multiple fingers to reach multiple contact points.
        /// </summary>
        public List<FingerIKResult> SolveMultipleFingers(
            Animator animator,
            bool isLeftHand,
            List<HumanBodyBones> targetBones,
            List<Vector3> targetPositions,
            List<Vector3> targetNormals)
        {
            var results = new List<FingerIKResult>();
            var chains = HandForwardKinematics.GetFingerChains(isLeftHand);

            for (int i = 0; i < targetBones.Count; i++)
            {
                // Find chain containing this bone
                HumanBodyBones[] chain = FindChainForBone(chains, targetBones[i]);
                if (chain == null)
                {
                    results.Add(new FingerIKResult { success = false });
                    continue;
                }

                var result = SolveFinger(animator, chain, targetPositions[i], targetNormals[i]);
                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// Iterative contact adjustment: project contact points and refine IK.
        /// </summary>
        public void IterativeContactAdjustment(
            Animator animator,
            bool isLeftHand,
            ref Vector3[] contactPositions,
            ref Vector3[] contactNormals,
            HumanBodyBones[] contactBones,
            Vector3[] objectPoints,
            Vector3[] objectNormals,
            int numIterations = 5)
        {
            var chains = HandForwardKinematics.GetFingerChains(isLeftHand);

            for (int iter = 0; iter < numIterations; iter++)
            {
                // Step 1: Solve IK to current contact targets
                for (int c = 0; c < contactBones.Length; c++)
                {
                    var chain = FindChainForBone(chains, contactBones[c]);
                    if (chain == null) continue;

                    SolveFinger(animator, chain, contactPositions[c], contactNormals[c]);
                }

                // Step 2: Get actual finger tip positions after IK
                for (int c = 0; c < contactBones.Length; c++)
                {
                    var chain = FindChainForBone(chains, contactBones[c]);
                    if (chain == null) continue;

                    var tipTransform = animator.GetBoneTransform(chain[chain.Length - 1]);
                    if (tipTransform == null) continue;

                    Vector3 actualTip = tipTransform.TransformPoint(GetTipOffset(tipTransform));

                    // Step 3: Project to nearest object point
                    int nearest = FindNearestPoint(actualTip, objectPoints);
                    contactPositions[c] = objectPoints[nearest];
                    contactNormals[c] = objectNormals[nearest];
                }
            }
        }

        private Vector3 GetTipOffset(Transform bone)
        {
            // Estimate tip as extension along bone direction
            if (bone.childCount > 0)
            {
                return Vector3.zero; // Will use child position  
            }
            // For distal bones, extend by typical length
            return bone.InverseTransformDirection(
                (bone.position - bone.parent.position).normalized * 0.015f);
        }

        private void ClampJointRotation(Transform joint, HumanBodyBones bone, bool isProximal)
        {
            // Convert to euler and clamp
            Vector3 euler = joint.localEulerAngles;

            // Normalize to -180 to 180
            if (euler.x > 180) euler.x -= 360;
            if (euler.y > 180) euler.y -= 360;
            if (euler.z > 180) euler.z -= 360;

            // Curl (X axis): 0 to maxCurl
            euler.x = Mathf.Clamp(euler.x, -10f, config.maxCurlAngle);

            // Spread (Y axis): only for proximal
            if (isProximal)
            {
                euler.y = Mathf.Clamp(euler.y, -config.maxSpreadAngle, config.maxSpreadAngle);
            }
            else
            {
                euler.y = Mathf.Clamp(euler.y, -5f, 5f);
            }

            // Twist (Z axis): minimal
            euler.z = Mathf.Clamp(euler.z, -10f, 10f);

            joint.localEulerAngles = euler;
        }

        private HumanBodyBones[] FindChainForBone(HumanBodyBones[][] chains, HumanBodyBones bone)
        {
            foreach (var chain in chains)
            {
                foreach (var b in chain)
                {
                    if (b == bone) return chain;
                }
            }
            return null;
        }

        private int FindNearestPoint(Vector3 pos, Vector3[] points)
        {
            int nearest = 0;
            float minDist = float.MaxValue;

            for (int i = 0; i < points.Length; i++)
            {
                float dist = Vector3.SqrMagnitude(pos - points[i]);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = i;
                }
            }

            return nearest;
        }
    }

    /// <summary>
    /// Result of finger IK solving.
    /// </summary>
    public class FingerIKResult
    {
        public bool success;
        public Dictionary<HumanBodyBones, Quaternion> jointAngles;
        public Vector3 finalPosition;
        public float positionError;
    }
}
