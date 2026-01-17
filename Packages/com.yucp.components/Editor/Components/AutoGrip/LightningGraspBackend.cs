#if YUCP_INTERNAL_LG
using UnityEngine;
using YUCP.Components.HandPoses;

namespace YUCP.Components.Editor.AutoGrip.LightningGrasp
{
    /// <summary>
    /// Lightning Grasp backend implementation.
    /// Ports the full Lightning Grasp pipeline to C#.
    /// 
    /// Pipeline stages:
    /// 1. Build contact field for humanoid hand
    /// 2. Sample object poses using contact field
    /// 3. Query contact interactions
    /// 4. Optimize contact points via zeroth-order optimizer
    /// 5. Solve IK to joint angles (simplified)
    /// 6. Filter collisions
    /// 7. Convert to YUCPHandDescriptor
    /// 
    /// NOTE: This is an experimental, non-faithful port.
    /// Production use should prefer ProceduralContactAndCollisionBackend.
    /// </summary>
    public class LightningGraspBackend : IGraspSynthesizerBackend
    {
        public string Name => "Lightning Grasp (Internal)";

        private ContactFieldPort contactField;
        private BatchedGraspOptimizerPort optimizer;

        public LightningGraspBackend()
        {
            contactField = new ContactFieldPort();
            optimizer = new BatchedGraspOptimizerPort(new BatchedGraspOptimizerPort.OptimizerParams
            {
                learningRate = 0.02f,
                maxIterations = 30,
                convergenceThresh = 0.01f,
                batchSize = 8,
                sigma = 0.15f
            });
        }

        public YUCPHandDescriptor SynthesizeGrasp(
            Animator animator,
            bool isLeftHand,
            AutoGripData data,
            PropCollisionData propCollision)
        {
            if (animator == null || !animator.isHuman)
            {
                Debug.LogError("[LightningGraspBackend] Animator is null or not humanoid");
                return null;
            }

            Debug.Log("[LightningGraspBackend] Starting Lightning Grasp synthesis (internal backend)");

            // Stage 1: Build contact field
            contactField = new ContactFieldPort();
            contactField.RegisterHumanoidHandPatches(animator, isLeftHand);

            // Stage 2-3: Sample object poses and contact interactions
            var initialSolution = SampleInitialContactDomain(animator, isLeftHand, propCollision);

            if (!initialSolution.isValid)
            {
                Debug.LogWarning("[LightningGraspBackend] Could not find valid initial contact domain");
                return CreateDefaultPose();
            }

            // Stage 4: Optimize contact points
            var optimized = optimizer.Optimize(initialSolution, solution =>
            {
                return optimizer.ComputeWrenchScore(
                    solution.contacts,
                    propCollision.bounds.center,
                    frictionCoeff: 0.6f);
            });

            if (data.verboseLogging)
            {
                Debug.Log($"[LightningGraspBackend] Optimization complete. Score: {optimized.wrenchScore:F4}");
            }

            // Stage 5-6: IK and collision filtering (simplified)
            var handDescriptor = ConvertSolutionToDescriptor(optimized, isLeftHand);

            // Verify no major collisions
            if (HasMajorCollisions(animator, isLeftHand, handDescriptor, propCollision))
            {
                Debug.LogWarning("[LightningGraspBackend] Solution has collisions, using fallback");
                return CreateDefaultPose();
            }

            return handDescriptor;
        }

        private BatchedGraspOptimizerPort.GraspSolution SampleInitialContactDomain(
            Animator animator,
            bool isLeftHand,
            PropCollisionData propCollision)
        {
            var solution = new BatchedGraspOptimizerPort.GraspSolution
            {
                objectPosition = propCollision.bounds.center,
                objectRotation = Quaternion.identity,
                contacts = new System.Collections.Generic.List<BatchedGraspOptimizerPort.ContactPoint>(),
                isValid = false
            };

            // Sample contacts from contact field patches
            var patches = contactField.AllPatches;
            if (patches.Count == 0)
            {
                return solution;
            }

            // Find patches that can reach the object
            foreach (var patch in patches)
            {
                var bone = (HumanBodyBones)patch.linkId;
                var linkTransform = animator.GetBoneTransform(bone);
                if (linkTransform == null) continue;

                var sampled = contactField.SampleContactGeometry(patches.IndexOf(patch), linkTransform);

                // Check if contact point is near object surface
                Vector3 closestOnObject = PropCollisionSource.GetClosestPoint(propCollision, sampled.positionWorld);
                float dist = Vector3.Distance(sampled.positionWorld, closestOnObject);

                if (dist < 0.05f) // Within 5cm
                {
                    // Get surface normal at contact point
                    Vector3 surfaceNormal = (sampled.positionWorld - closestOnObject).normalized;
                    if (surfaceNormal.sqrMagnitude < 0.1f)
                    {
                        surfaceNormal = -sampled.normalWorld; // Fallback
                    }

                    solution.contacts.Add(new BatchedGraspOptimizerPort.ContactPoint
                    {
                        position = closestOnObject,
                        normal = surfaceNormal,
                        weight = patch.areaWeight
                    });
                }
            }

            solution.isValid = solution.contacts.Count >= 3;
            return solution;
        }

        private YUCPHandDescriptor ConvertSolutionToDescriptor(
            BatchedGraspOptimizerPort.GraspSolution solution,
            bool isLeftHand)
        {
            // Simplified IK: estimate finger curls from contact positions
            var descriptor = new YUCPHandDescriptor();

            // Use contact density to estimate curl per finger
            // This is a major simplification of the actual IK stage

            float thumbCurl = 45f;
            float indexCurl = 60f;
            float middleCurl = 65f;
            float ringCurl = 60f;
            float littleCurl = 55f;

            // Adjust based on contact point positions if available
            if (solution.contacts != null)
            {
                foreach (var contact in solution.contacts)
                {
                    // Very simplified: higher contact = more curl
                    float heightFactor = Mathf.Clamp01((contact.position.y + 0.1f) / 0.2f);
                    float curlAdjust = heightFactor * 20f - 10f;

                    // Apply to all fingers (proper version would identify which finger)
                    indexCurl += curlAdjust * 0.25f;
                    middleCurl += curlAdjust * 0.25f;
                    ringCurl += curlAdjust * 0.25f;
                    littleCurl += curlAdjust * 0.25f;
                }
            }

            descriptor.SetFinger(YUCPFingerType.Thumb, CreateFingerDescriptor(thumbCurl));
            descriptor.SetFinger(YUCPFingerType.Index, CreateFingerDescriptor(indexCurl));
            descriptor.SetFinger(YUCPFingerType.Middle, CreateFingerDescriptor(middleCurl));
            descriptor.SetFinger(YUCPFingerType.Ring, CreateFingerDescriptor(ringCurl));
            descriptor.SetFinger(YUCPFingerType.Little, CreateFingerDescriptor(littleCurl));

            return descriptor;
        }

        private YUCPFingerDescriptor CreateFingerDescriptor(float curlDegrees)
        {
            float proximal = curlDegrees * 0.45f;
            float intermediate = curlDegrees * 0.35f;
            float distal = curlDegrees * 0.2f;

            return new YUCPFingerDescriptor(
                Quaternion.Euler(proximal, 0, 0),
                Quaternion.Euler(intermediate, 0, 0),
                Quaternion.Euler(distal, 0, 0)
            );
        }

        private bool HasMajorCollisions(
            Animator animator,
            bool isLeftHand,
            YUCPHandDescriptor descriptor,
            PropCollisionData propCollision)
        {
            // Simplified collision check
            // Full implementation would apply pose and check all finger proxies
            return false;
        }

        private YUCPHandDescriptor CreateDefaultPose()
        {
            var descriptor = new YUCPHandDescriptor();
            float defaultCurl = 50f;

            foreach (YUCPFingerType finger in System.Enum.GetValues(typeof(YUCPFingerType)))
            {
                descriptor.SetFinger(finger, CreateFingerDescriptor(defaultCurl));
            }

            return descriptor;
        }
    }
}
#endif
