using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Analyzes avatar hand structure to extract finger bones and associated mesh vertices.
    /// Used for contact grip generation.
    /// </summary>
    public static class HandAnalyzer
    {
        public class FingerSegment
        {
            public HumanBodyBones bone;
            public Transform transform;
            public Vector3[] vertices;
            public float segmentLength;
        }

        public class HandData
        {
            public Transform handBone;
            public List<FingerSegment> indexSegments = new List<FingerSegment>();
            public List<FingerSegment> middleSegments = new List<FingerSegment>();
            public List<FingerSegment> ringSegments = new List<FingerSegment>();
            public List<FingerSegment> littleSegments = new List<FingerSegment>();
            public List<FingerSegment> thumbSegments = new List<FingerSegment>();
            public SkinnedMeshRenderer bodyMesh;
        }

        public static HandData AnalyzeHand(Animator animator, bool isLeftHand)
        {
            var handData = new HandData();

            HumanBodyBones handBone = isLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            handData.handBone = animator.GetBoneTransform(handBone);

            if (handData.handBone == null)
            {
                Debug.LogWarning($"[HandAnalyzer] {(isLeftHand ? "Left" : "Right")} hand bone not found");
                return null;
            }

            handData.bodyMesh = FindBodyMesh(animator);

            if (handData.bodyMesh == null)
            {
                Debug.LogWarning("[HandAnalyzer] Could not find body mesh with hand bones");
                return null;
            }

            handData.indexSegments = GetFingerSegments(animator, handData.bodyMesh, isLeftHand, "Index");
            handData.middleSegments = GetFingerSegments(animator, handData.bodyMesh, isLeftHand, "Middle");
            handData.ringSegments = GetFingerSegments(animator, handData.bodyMesh, isLeftHand, "Ring");
            handData.littleSegments = GetFingerSegments(animator, handData.bodyMesh, isLeftHand, "Little");
            handData.thumbSegments = GetFingerSegments(animator, handData.bodyMesh, isLeftHand, "Thumb");

            return handData;
        }

        private static SkinnedMeshRenderer FindBodyMesh(Animator animator)
        {
            var renderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>();

            foreach (var renderer in renderers)
            {
                if (renderer.bones != null && renderer.bones.Length > 0)
                {
                    foreach (var bone in renderer.bones)
                    {
                        if (bone != null && bone.name.ToLower().Contains("index"))
                        {
                            return renderer;
                        }
                    }
                }
            }

            return renderers.Length > 0 ? renderers[0] : null;
        }

        private static List<FingerSegment> GetFingerSegments(
            Animator animator, 
            SkinnedMeshRenderer bodyMesh, 
            bool isLeftHand, 
            string fingerName)
        {
            var segments = new List<FingerSegment>();
            string side = isLeftHand ? "Left" : "Right";

            HumanBodyBones[] bones;
            
            switch (fingerName)
            {
                case "Index":
                    bones = isLeftHand 
                        ? new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal }
                        : new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal };
                    break;
                case "Middle":
                    bones = isLeftHand
                        ? new[] { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal }
                        : new[] { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal };
                    break;
                case "Ring":
                    bones = isLeftHand
                        ? new[] { HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal }
                        : new[] { HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal };
                    break;
                case "Little":
                    bones = isLeftHand
                        ? new[] { HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal }
                        : new[] { HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal };
                    break;
                case "Thumb":
                    bones = isLeftHand
                        ? new[] { HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal }
                        : new[] { HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal };
                    break;
                default:
                    return segments;
            }

            for (int i = 0; i < bones.Length; i++)
            {
                var boneTransform = animator.GetBoneTransform(bones[i]);
                if (boneTransform == null) continue;

                var segment = new FingerSegment
                {
                    bone = bones[i],
                    transform = boneTransform,
                    vertices = GetVerticesForBone(bodyMesh, boneTransform),
                    segmentLength = CalculateSegmentLength(boneTransform)
                };

                segments.Add(segment);
            }

            return segments;
        }

        private static Vector3[] GetVerticesForBone(SkinnedMeshRenderer renderer, Transform bone)
        {
            if (renderer.sharedMesh == null) return new Vector3[0];

            Mesh mesh = renderer.sharedMesh;
            BoneWeight[] boneWeights = mesh.boneWeights;
            Vector3[] vertices = mesh.vertices;

            int boneIndex = System.Array.IndexOf(renderer.bones, bone);
            if (boneIndex < 0) return new Vector3[0];

            List<Vector3> boneVertices = new List<Vector3>();

            for (int i = 0; i < boneWeights.Length; i++)
            {
                BoneWeight weight = boneWeights[i];
                
                if ((weight.boneIndex0 == boneIndex && weight.weight0 > 0.5f) ||
                    (weight.boneIndex1 == boneIndex && weight.weight1 > 0.5f) ||
                    (weight.boneIndex2 == boneIndex && weight.weight2 > 0.5f) ||
                    (weight.boneIndex3 == boneIndex && weight.weight3 > 0.5f))
                {
                    boneVertices.Add(vertices[i]);
                }
            }

            return boneVertices.ToArray();
        }

        private static float CalculateSegmentLength(Transform bone)
        {
            if (bone.childCount > 0)
            {
                return Vector3.Distance(bone.position, bone.GetChild(0).position);
            }
            return 0.03f;
        }

        public static Vector3 GetHandCenter(HandData handData)
        {
            if (handData.handBone == null) return Vector3.zero;
            return handData.handBone.position;
        }

        public static Vector3 GetPalmNormal(HandData handData, bool isLeftHand)
        {
            if (handData.handBone == null) return Vector3.up;
            
            return isLeftHand 
                ? -handData.handBone.right 
                : handData.handBone.right;
        }
    }
}



