using System.Collections.Generic;
using UnityEngine;
using YUCP.Components.HandPoses;

namespace YUCP.Components.Editor.AutoGrip
{
    /// <summary>
    /// Procedural grasp synthesis backend.
    /// Uses iterative finger curling with collision detection.
    /// This is the default ship-safe backend that works without external dependencies.
    /// </summary>
    public class ProceduralContactAndCollisionBackend : IGraspSynthesizerBackend
    {
        public string Name => "Procedural Contact & Collision";

        // Solver constants
        private const float MaxCurlAngle = 90f;
        private const float CurlStepDegrees = 5f;

        public YUCPHandDescriptor SynthesizeGrasp(
            Animator animator,
            bool isLeftHand,
            AutoGripData data,
            PropCollisionData propCollision)
        {
            if (animator == null || !animator.isHuman)
            {
                Debug.LogError("[ProceduralBackend] Animator is null or not humanoid");
                return null;
            }

            if (propCollision == null || propCollision.colliders == null)
            {
                Debug.LogError("[ProceduralBackend] No collision data for prop");
                return null;
            }

            // Build hand proxies
            var outputRadii = new FingerRadiiData();
            var handProxy = HandProxyBuilder.BuildHandProxy(
                animator, isLeftHand, 
                data.fingerPaddingMm, 
                data.fingerRadiusOverrides,
                outputRadii);

            if (handProxy == null)
            {
                Debug.LogError("[ProceduralBackend] Failed to build hand proxy");
                return null;
            }

            // Store radii in cache if available
            if (data.bakeCache != null)
            {
                if (isLeftHand)
                    data.bakeCache.leftHandRadii = outputRadii;
                else
                    data.bakeCache.rightHandRadii = outputRadii;
            }

            // Capture original pose
            var originalPose = CaptureHandPose(animator, isLeftHand);

            try
            {
                // Find optimal curl for each finger
                var fingerCurls = new Dictionary<HandPoses.YUCPFingerType, float>();
                var fingerOrder = new[] { 
                    HandPoses.YUCPFingerType.Middle, 
                    HandPoses.YUCPFingerType.Index, 
                    HandPoses.YUCPFingerType.Ring, 
                    HandPoses.YUCPFingerType.Little, 
                    HandPoses.YUCPFingerType.Thumb 
                };

                int contactCount = 0;
                foreach (var finger in fingerOrder)
                {
                    float curl = FindOptimalCurl(
                        animator, isLeftHand, handProxy, propCollision, finger, 
                        MaxCurlAngle, CurlStepDegrees, 
                        out bool hasContact);

                    fingerCurls[finger] = curl;
                    if (hasContact) contactCount++;

                    // Apply curl to bones
                    ApplyCurlToFinger(animator, isLeftHand, finger, curl);
                }

                if (data.verboseLogging)
                {
                    Debug.Log($"[ProceduralBackend] Found grasp with {contactCount} contacts");
                }

                // Build hand descriptor from current pose
                var handDescriptor = CaptureHandDescriptor(animator, isLeftHand);

                // Store cache info
                if (data.bakeCache != null)
                {
                    data.bakeCache.bestContactCount = contactCount;
                    data.bakeCache.bestScore = 5 - contactCount;
                }

                return handDescriptor;
            }
            finally
            {
                // Restore original pose
                RestoreHandPose(animator, originalPose);
            }
        }

        private float FindOptimalCurl(
            Animator animator,
            bool isLeftHand,
            HandProxyData handProxy,
            PropCollisionData propCollision,
            HandPoses.YUCPFingerType finger,
            float maxAngle,
            float stepAngle,
            out bool hasContact)
        {
            hasContact = false;
            float lastSafeCurl = 0f;

            var fingerProxies = handProxy.GetFingerProxies(finger);
            if (fingerProxies.Count == 0) return 0f;

            // Get finger bones
            var bones = GetFingerBones(animator, isLeftHand, finger);
            if (bones.Count == 0) return 0f;

            // Capture original rotations
            var originalRotations = new List<Quaternion>();
            foreach (var bone in bones)
            {
                originalRotations.Add(bone.localRotation);
            }

            // Linear search for contact
            for (float curl = 0f; curl <= maxAngle; curl += stepAngle)
            {
                // Apply curl
                ApplyCurlToBones(bones, originalRotations, curl, finger);
                
                // Update proxy positions
                HandProxyBuilder.UpdateProxyPositions(handProxy, animator);

                // Check penetration
                bool penetrates = CheckPenetration(handProxy.GetFingerProxies(finger), propCollision);

                if (penetrates)
                {
                    hasContact = true;
                    // Restore to just before contact
                    ApplyCurlToBones(bones, originalRotations, lastSafeCurl, finger);
                    return lastSafeCurl;
                }

                lastSafeCurl = curl;
            }

            // No contact found, use max curl
            ApplyCurlToBones(bones, originalRotations, maxAngle * 0.8f, finger);
            return maxAngle * 0.8f;
        }

        private void ApplyCurlToBones(List<Transform> bones, List<Quaternion> originalRotations, float curlDegrees, HandPoses.YUCPFingerType finger)
        {
            for (int i = 0; i < bones.Count && i < originalRotations.Count; i++)
            {
                // Distribute curl across segments (proximal gets most, distal gets least)
                float segmentFactor = finger == HandPoses.YUCPFingerType.Thumb 
                    ? (i == 0 ? 0.3f : i == 1 ? 0.4f : 0.3f)
                    : (i == 0 ? 0.5f : i == 1 ? 0.35f : 0.15f);
                
                float segmentCurl = curlDegrees * segmentFactor;
                
                // Curl around local X axis (typical for fingers)
                // For non-thumb fingers, rotate around negative X axis (opposite direction)
                Quaternion curlRotation = finger == HandPoses.YUCPFingerType.Thumb
                    ? Quaternion.Euler(segmentCurl, 0f, 0f)
                    : Quaternion.Euler(-segmentCurl, 0f, 0f);
                bones[i].localRotation = originalRotations[i] * curlRotation;
            }
        }

        private void ApplyCurlToFinger(Animator animator, bool isLeftHand, HandPoses.YUCPFingerType finger, float curlDegrees)
        {
            var bones = GetFingerBones(animator, isLeftHand, finger);
            for (int i = 0; i < bones.Count; i++)
            {
                float segmentFactor = finger == HandPoses.YUCPFingerType.Thumb 
                    ? (i == 0 ? 0.3f : i == 1 ? 0.4f : 0.3f)
                    : (i == 0 ? 0.5f : i == 1 ? 0.35f : 0.15f);
                
                float segmentCurl = curlDegrees * segmentFactor;
                
                // Curl around local X axis (typical for fingers)
                // For non-thumb fingers, rotate around negative X axis (opposite direction)
                Quaternion curlRotation = finger == HandPoses.YUCPFingerType.Thumb
                    ? Quaternion.Euler(segmentCurl, 0f, 0f)
                    : Quaternion.Euler(-segmentCurl, 0f, 0f);
                bones[i].localRotation = bones[i].localRotation * curlRotation;
            }
        }

        private bool CheckPenetration(List<FingerCapsuleProxy> fingerProxies, PropCollisionData propCollision)
        {
            foreach (var proxy in fingerProxies)
            {
                if (PropCollisionSource.ComputePenetration(
                    propCollision,
                    proxy.startPoint,
                    proxy.endPoint,
                    proxy.radius,
                    out _, out float dist))
                {
                    if (dist > 0.001f) return true;
                }
            }
            return false;
        }

        private List<Transform> GetFingerBones(Animator animator, bool isLeftHand, HandPoses.YUCPFingerType finger)
        {
            var bones = new List<Transform>();
            
            HumanBodyBones proximal, intermediate, distal;
            
            if (isLeftHand)
            {
                switch (finger)
                {
                    case HandPoses.YUCPFingerType.Thumb:
                        proximal = HumanBodyBones.LeftThumbProximal;
                        intermediate = HumanBodyBones.LeftThumbIntermediate;
                        distal = HumanBodyBones.LeftThumbDistal;
                        break;
                    case HandPoses.YUCPFingerType.Index:
                        proximal = HumanBodyBones.LeftIndexProximal;
                        intermediate = HumanBodyBones.LeftIndexIntermediate;
                        distal = HumanBodyBones.LeftIndexDistal;
                        break;
                    case HandPoses.YUCPFingerType.Middle:
                        proximal = HumanBodyBones.LeftMiddleProximal;
                        intermediate = HumanBodyBones.LeftMiddleIntermediate;
                        distal = HumanBodyBones.LeftMiddleDistal;
                        break;
                    case HandPoses.YUCPFingerType.Ring:
                        proximal = HumanBodyBones.LeftRingProximal;
                        intermediate = HumanBodyBones.LeftRingIntermediate;
                        distal = HumanBodyBones.LeftRingDistal;
                        break;
                    case HandPoses.YUCPFingerType.Little:
                        proximal = HumanBodyBones.LeftLittleProximal;
                        intermediate = HumanBodyBones.LeftLittleIntermediate;
                        distal = HumanBodyBones.LeftLittleDistal;
                        break;
                    default:
                        return bones;
                }
            }
            else
            {
                switch (finger)
                {
                    case HandPoses.YUCPFingerType.Thumb:
                        proximal = HumanBodyBones.RightThumbProximal;
                        intermediate = HumanBodyBones.RightThumbIntermediate;
                        distal = HumanBodyBones.RightThumbDistal;
                        break;
                    case HandPoses.YUCPFingerType.Index:
                        proximal = HumanBodyBones.RightIndexProximal;
                        intermediate = HumanBodyBones.RightIndexIntermediate;
                        distal = HumanBodyBones.RightIndexDistal;
                        break;
                    case HandPoses.YUCPFingerType.Middle:
                        proximal = HumanBodyBones.RightMiddleProximal;
                        intermediate = HumanBodyBones.RightMiddleIntermediate;
                        distal = HumanBodyBones.RightMiddleDistal;
                        break;
                    case HandPoses.YUCPFingerType.Ring:
                        proximal = HumanBodyBones.RightRingProximal;
                        intermediate = HumanBodyBones.RightRingIntermediate;
                        distal = HumanBodyBones.RightRingDistal;
                        break;
                    case HandPoses.YUCPFingerType.Little:
                        proximal = HumanBodyBones.RightLittleProximal;
                        intermediate = HumanBodyBones.RightLittleIntermediate;
                        distal = HumanBodyBones.RightLittleDistal;
                        break;
                    default:
                        return bones;
                }
            }

            var t = animator.GetBoneTransform(proximal);
            if (t != null) bones.Add(t);
            t = animator.GetBoneTransform(intermediate);
            if (t != null) bones.Add(t);
            t = animator.GetBoneTransform(distal);
            if (t != null) bones.Add(t);

            return bones;
        }

        private YUCPHandDescriptor CaptureHandDescriptor(Animator animator, bool isLeftHand)
        {
            var descriptor = new YUCPHandDescriptor();
            var handSide = isLeftHand ? YUCPHandSide.Left : YUCPHandSide.Right;
            
            Transform wrist = YUCPAvatarRigHelper.GetWrist(animator, handSide);
            if (wrist == null) return descriptor;

            var handLocalAxes = new YUCPUniversalLocalAxes();
            var fingerLocalAxes = new YUCPUniversalLocalAxes();

            // Compute each finger
            foreach (HandPoses.YUCPFingerType fingerType in System.Enum.GetValues(typeof(HandPoses.YUCPFingerType)))
            {
                if (fingerType == HandPoses.YUCPFingerType.None) continue;

                var (metacarpal, proximal, intermediate, distal) = YUCPAvatarRigHelper.GetFingerBones(animator, handSide, fingerType);
                if (proximal == null) continue;

                var fingerDesc = new YUCPFingerDescriptor();
                fingerDesc.Compute(wrist, metacarpal, proximal, intermediate, distal, handLocalAxes, fingerLocalAxes, false);
                descriptor.SetFinger(fingerType, fingerDesc);
            }

            return descriptor;
        }

        private Dictionary<HumanBodyBones, Quaternion> CaptureHandPose(Animator animator, bool isLeftHand)
        {
            var pose = new Dictionary<HumanBodyBones, Quaternion>();
            
            var handBones = isLeftHand
                ? new[] {
                    HumanBodyBones.LeftHand,
                    HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
                    HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
                    HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
                    HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
                    HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal
                }
                : new[] {
                    HumanBodyBones.RightHand,
                    HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
                    HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
                    HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
                    HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
                    HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal
                };

            foreach (var bone in handBones)
            {
                var t = animator.GetBoneTransform(bone);
                if (t != null)
                {
                    pose[bone] = t.localRotation;
                }
            }

            return pose;
        }

        private void RestoreHandPose(Animator animator, Dictionary<HumanBodyBones, Quaternion> pose)
        {
            foreach (var kvp in pose)
            {
                var t = animator.GetBoneTransform(kvp.Key);
                if (t != null)
                {
                    t.localRotation = kvp.Value;
                }
            }
        }
    }
}








