using System.Collections.Generic;
using UnityEngine;
using YUCP.Components.Editor.MeshUtils;
using YUCP.Components.HandPoses;

namespace YUCP.Components.Editor.AutoGrip
{
    /// <summary>
    /// Represents a capsule collision proxy for a finger segment.
    /// </summary>
    public struct FingerCapsuleProxy
    {
        public Vector3 startPoint;
        public Vector3 endPoint;
        public float radius;
        public HumanBodyBones bone;
        public HandPoses.YUCPFingerType fingerType;
        public int segmentIndex; // 0=proximal, 1=intermediate, 2=distal

        public Vector3 Center => (startPoint + endPoint) * 0.5f;
        public float Length => Vector3.Distance(startPoint, endPoint);
        public Vector3 Direction => (endPoint - startPoint).normalized;
    }

    /// <summary>
    /// Hand collision proxy data containing all finger capsules.
    /// </summary>
    public class HandProxyData
    {
        public bool isLeftHand;
        public Transform handBone;
        public List<FingerCapsuleProxy> thumbProxies = new List<FingerCapsuleProxy>();
        public List<FingerCapsuleProxy> indexProxies = new List<FingerCapsuleProxy>();
        public List<FingerCapsuleProxy> middleProxies = new List<FingerCapsuleProxy>();
        public List<FingerCapsuleProxy> ringProxies = new List<FingerCapsuleProxy>();
        public List<FingerCapsuleProxy> littleProxies = new List<FingerCapsuleProxy>();
        public FingerCapsuleProxy palmProxy;

        public List<FingerCapsuleProxy> AllProxies
        {
            get
            {
                var all = new List<FingerCapsuleProxy>();
                all.AddRange(thumbProxies);
                all.AddRange(indexProxies);
                all.AddRange(middleProxies);
                all.AddRange(ringProxies);
                all.AddRange(littleProxies);
                all.Add(palmProxy);
                return all;
            }
        }

        public List<FingerCapsuleProxy> GetFingerProxies(HandPoses.YUCPFingerType finger)
        {
            switch (finger)
            {
                case HandPoses.YUCPFingerType.Thumb: return thumbProxies;
                case HandPoses.YUCPFingerType.Index: return indexProxies;
                case HandPoses.YUCPFingerType.Middle: return middleProxies;
                case HandPoses.YUCPFingerType.Ring: return ringProxies;
                case HandPoses.YUCPFingerType.Little: return littleProxies;
                default: return new List<FingerCapsuleProxy>();
            }
        }
    }

    /// <summary>
    /// Builds capsule collision proxies for hand fingers using HandAnalyzer data.
    /// </summary>
    public static class HandProxyBuilder
    {
        // Default finger radii in meters (realistic human finger sizes)
        // Human finger radii are typically 3-5mm for fingertips
        private const float DefaultThumbRadius = 0.005f;   // 5mm
        private const float DefaultIndexRadius = 0.004f;   // 4mm
        private const float DefaultMiddleRadius = 0.004f;  // 4mm
        private const float DefaultRingRadius = 0.0035f;   // 3.5mm
        private const float DefaultLittleRadius = 0.003f;  // 3mm
        private const float DefaultPalmRadius = 0.015f;    // 15mm

        /// <summary>
        /// Builds hand collision proxies from avatar animator.
        /// </summary>
        /// <param name="animator">Avatar animator with humanoid rig</param>
        /// <param name="isLeftHand">True for left hand, false for right</param>
        /// <param name="paddingMm">Additional padding in millimeters</param>
        /// <param name="radiusOverrides">Per-finger radius overrides (0 = auto)</param>
        /// <param name="outputRadii">Output detected radii for caching</param>
        public static HandProxyData BuildHandProxy(
            Animator animator, 
            bool isLeftHand, 
            float paddingMm = 2f,
            FingerRadiusOverrides radiusOverrides = null,
            FingerRadiiData outputRadii = null)
        {
            if (animator == null || !animator.isHuman)
            {
                Debug.LogError("[HandProxyBuilder] Animator is null or not humanoid");
                return null;
            }

            var handData = HandAnalyzer.AnalyzeHand(animator, isLeftHand);
            if (handData == null)
            {
                Debug.LogError($"[HandProxyBuilder] Failed to analyze {(isLeftHand ? "left" : "right")} hand");
                return null;
            }

            float paddingM = paddingMm * 0.001f;
            var proxyData = new HandProxyData
            {
                isLeftHand = isLeftHand,
                handBone = handData.handBone
            };

            // Build finger proxies
            BuildFingerProxies(handData.thumbSegments, HandPoses.YUCPFingerType.Thumb, 
                GetRadius(radiusOverrides, HandPoses.YUCPFingerType.Thumb, DefaultThumbRadius), 
                paddingM, proxyData.thumbProxies, outputRadii);
            
            BuildFingerProxies(handData.indexSegments, HandPoses.YUCPFingerType.Index, 
                GetRadius(radiusOverrides, HandPoses.YUCPFingerType.Index, DefaultIndexRadius), 
                paddingM, proxyData.indexProxies, outputRadii);
            
            BuildFingerProxies(handData.middleSegments, HandPoses.YUCPFingerType.Middle, 
                GetRadius(radiusOverrides, HandPoses.YUCPFingerType.Middle, DefaultMiddleRadius), 
                paddingM, proxyData.middleProxies, outputRadii);
            
            BuildFingerProxies(handData.ringSegments, HandPoses.YUCPFingerType.Ring, 
                GetRadius(radiusOverrides, HandPoses.YUCPFingerType.Ring, DefaultRingRadius), 
                paddingM, proxyData.ringProxies, outputRadii);
            
            BuildFingerProxies(handData.littleSegments, HandPoses.YUCPFingerType.Little, 
                GetRadius(radiusOverrides, HandPoses.YUCPFingerType.Little, DefaultLittleRadius), 
                paddingM, proxyData.littleProxies, outputRadii);

            // Build palm proxy (approximate as sphere at hand center)
            proxyData.palmProxy = BuildPalmProxy(handData, paddingM);

            return proxyData;
        }

        private static float GetRadius(FingerRadiusOverrides overrides, HandPoses.YUCPFingerType finger, float defaultRadius)
        {
            if (overrides == null) return defaultRadius;
            // Convert HandPoses.YUCPFingerType to YUCP.Components.YUCPFingerType
            YUCP.Components.YUCPFingerType componentsFinger = ConvertToComponentsFingerType(finger);
            float override_val = overrides.GetRadius(componentsFinger);
            return override_val > 0f ? override_val : defaultRadius;
        }

        private static YUCP.Components.YUCPFingerType ConvertToComponentsFingerType(HandPoses.YUCPFingerType finger)
        {
            switch (finger)
            {
                case HandPoses.YUCPFingerType.Thumb: return YUCP.Components.YUCPFingerType.Thumb;
                case HandPoses.YUCPFingerType.Index: return YUCP.Components.YUCPFingerType.Index;
                case HandPoses.YUCPFingerType.Middle: return YUCP.Components.YUCPFingerType.Middle;
                case HandPoses.YUCPFingerType.Ring: return YUCP.Components.YUCPFingerType.Ring;
                case HandPoses.YUCPFingerType.Little: return YUCP.Components.YUCPFingerType.Little;
                default: return YUCP.Components.YUCPFingerType.Thumb;
            }
        }

        private static void BuildFingerProxies(
            List<HandAnalyzer.FingerSegment> segments,
            HandPoses.YUCPFingerType fingerType,
            float baseRadius,
            float padding,
            List<FingerCapsuleProxy> output,
            FingerRadiiData outputRadii)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (segment.transform == null) continue;

                // Estimate radius from vertex cloud if available
                float estimatedRadius = EstimateRadiusFromVertices(segment, baseRadius);
                
                // Store for caching
                if (outputRadii != null && i == 0)
                {
                    // Convert HandPoses.YUCPFingerType to YUCP.Components.YUCPFingerType
                    YUCP.Components.YUCPFingerType componentsFinger = ConvertToComponentsFingerType(fingerType);
                    outputRadii.SetRadius(componentsFinger, estimatedRadius);
                }

                // Scale radius for different segments (distal is smaller)
                float segmentScale = i == 2 ? 0.85f : (i == 1 ? 0.95f : 1.0f);
                float finalRadius = estimatedRadius * segmentScale + padding;

                // Calculate capsule endpoints
                Vector3 startPoint = segment.transform.position;
                Vector3 endPoint;

                if (segment.transform.childCount > 0)
                {
                    endPoint = segment.transform.GetChild(0).position;
                }
                else
                {
                    // Estimate end point from segment length
                    endPoint = startPoint + segment.transform.forward * segment.segmentLength;
                }

                output.Add(new FingerCapsuleProxy
                {
                    startPoint = startPoint,
                    endPoint = endPoint,
                    radius = finalRadius,
                    bone = segment.bone,
                    fingerType = fingerType,
                    segmentIndex = i
                });
            }
        }

        private static float EstimateRadiusFromVertices(HandAnalyzer.FingerSegment segment, float defaultRadius)
        {
            if (segment.vertices == null || segment.vertices.Length < 3)
            {
                return defaultRadius;
            }

            // Calculate median distance from vertices to bone axis
            var bonePos = segment.transform.position;
            var boneDir = segment.transform.forward;

            var distances = new List<float>();
            foreach (var vertex in segment.vertices)
            {
                // Project vertex onto bone axis and get perpendicular distance
                Vector3 toVertex = vertex - bonePos;
                float projLength = Vector3.Dot(toVertex, boneDir);
                Vector3 projPoint = bonePos + boneDir * projLength;
                float dist = Vector3.Distance(vertex, projPoint);
                distances.Add(dist);
            }

            if (distances.Count == 0) return defaultRadius;

            // Use median for robustness
            distances.Sort();
            int medianIdx = distances.Count / 2;
            float estimatedRadius = distances[medianIdx];

            // Clamp to reasonable range (2mm to 8mm)
            const float minRadius = 0.002f;
            const float maxRadius = 0.008f;
            estimatedRadius = Mathf.Clamp(estimatedRadius, minRadius, maxRadius);

            return estimatedRadius;
        }

        private static FingerCapsuleProxy BuildPalmProxy(HandAnalyzer.HandData handData, float padding)
        {
            Vector3 palmCenter = handData.handBone.position;
            Vector3 palmForward = handData.handBone.forward;

            return new FingerCapsuleProxy
            {
                startPoint = palmCenter - palmForward * 0.02f,
                endPoint = palmCenter + palmForward * 0.02f,
                radius = DefaultPalmRadius + padding,
                bone = handData.indexSegments.Count > 0 ? handData.indexSegments[0].bone : HumanBodyBones.LeftHand,
                fingerType = HandPoses.YUCPFingerType.Index, // Palm associated with index base
                segmentIndex = -1 // Indicates palm
            };
        }

        /// <summary>
        /// Updates proxy positions based on current bone transforms (for animation preview).
        /// </summary>
        public static void UpdateProxyPositions(HandProxyData proxyData, Animator animator)
        {
            if (proxyData == null || animator == null) return;

            UpdateFingerProxyPositions(proxyData.thumbProxies, animator);
            UpdateFingerProxyPositions(proxyData.indexProxies, animator);
            UpdateFingerProxyPositions(proxyData.middleProxies, animator);
            UpdateFingerProxyPositions(proxyData.ringProxies, animator);
            UpdateFingerProxyPositions(proxyData.littleProxies, animator);

            // Update palm
            var handBone = animator.GetBoneTransform(proxyData.isLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (handBone != null)
            {
                proxyData.palmProxy.startPoint = handBone.position - handBone.forward * 0.02f;
                proxyData.palmProxy.endPoint = handBone.position + handBone.forward * 0.02f;
            }
        }

        private static void UpdateFingerProxyPositions(List<FingerCapsuleProxy> proxies, Animator animator)
        {
            for (int i = 0; i < proxies.Count; i++)
            {
                var proxy = proxies[i];
                var boneTransform = animator.GetBoneTransform(proxy.bone);
                if (boneTransform == null) continue;

                proxy.startPoint = boneTransform.position;
                if (boneTransform.childCount > 0)
                {
                    proxy.endPoint = boneTransform.GetChild(0).position;
                }
                else
                {
                    proxy.endPoint = boneTransform.position + boneTransform.forward * proxy.Length;
                }
                proxies[i] = proxy;
            }
        }

        /// <summary>
        /// Checks if a capsule proxy intersects with a collider.
        /// </summary>
        public static bool CapsuleIntersectsCollider(FingerCapsuleProxy proxy, Collider collider)
        {
            if (collider == null) return false;

            // Use Physics.ComputePenetration for accurate collision
            // Create temporary capsule collider
            var tempObj = new GameObject("TempCapsule");
            var capsule = tempObj.AddComponent<CapsuleCollider>();
            
            try
            {
                capsule.center = Vector3.zero;
                capsule.height = proxy.Length + proxy.radius * 2f;
                capsule.radius = proxy.radius;
                capsule.direction = 2; // Z-axis

                tempObj.transform.position = proxy.Center;
                tempObj.transform.rotation = Quaternion.LookRotation(proxy.Direction);

                Vector3 direction;
                float distance;
                bool overlaps = Physics.ComputePenetration(
                    capsule, tempObj.transform.position, tempObj.transform.rotation,
                    collider, collider.transform.position, collider.transform.rotation,
                    out direction, out distance);

                return overlaps;
            }
            finally
            {
                Object.DestroyImmediate(tempObj);
            }
        }

        /// <summary>
        /// Checks for self-collision between two finger proxies.
        /// </summary>
        public static bool ProxiesIntersect(FingerCapsuleProxy a, FingerCapsuleProxy b)
        {
            // Skip adjacent segments on same finger
            if (a.fingerType == b.fingerType && Mathf.Abs(a.segmentIndex - b.segmentIndex) <= 1)
            {
                return false;
            }

            // Capsule-capsule intersection test
            float dist = ClosestDistanceBetweenSegments(a.startPoint, a.endPoint, b.startPoint, b.endPoint);
            return dist < (a.radius + b.radius);
        }

        private static float ClosestDistanceBetweenSegments(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
        {
            Vector3 u = a1 - a0;
            Vector3 v = b1 - b0;
            Vector3 w = a0 - b0;

            float a = Vector3.Dot(u, u);
            float b = Vector3.Dot(u, v);
            float c = Vector3.Dot(v, v);
            float d = Vector3.Dot(u, w);
            float e = Vector3.Dot(v, w);

            float denom = a * c - b * b;
            float sc, tc;

            if (denom < 1e-6f)
            {
                sc = 0f;
                tc = (b > c ? d / b : e / c);
            }
            else
            {
                sc = (b * e - c * d) / denom;
                tc = (a * e - b * d) / denom;
            }

            sc = Mathf.Clamp01(sc);
            tc = Mathf.Clamp01(tc);

            Vector3 closestOnA = a0 + sc * u;
            Vector3 closestOnB = b0 + tc * v;

            return Vector3.Distance(closestOnA, closestOnB);
        }
    }
}
