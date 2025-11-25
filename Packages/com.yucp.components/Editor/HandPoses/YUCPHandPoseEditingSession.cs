// Portions adapted from UltimateXR (MIT License) by VRMADA
// Inspired by UxrHandPoseEditorWindow finger manipulation utilities.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.HandPoses.Editor
{
    internal enum FingerSegment
    {
        Metacarpal,
        Proximal,
        Intermediate,
        Distal
    }

    internal sealed class FingerSegmentState
    {
        public Transform Bone { get; }
        public Vector3 Axis { get; }
        public float MinAngle { get; }
        public float MaxAngle { get; }
        public float Value { get; set; }

        private Quaternion _restLocalRotation;

        public FingerSegmentState(Transform bone, Vector3 axis, float minAngle, float maxAngle)
        {
            Bone = bone;
            Axis = axis;
            MinAngle = minAngle;
            MaxAngle = maxAngle;
            Value = 0f;
            _restLocalRotation = bone != null ? bone.localRotation : Quaternion.identity;
        }

        public void Apply()
        {
            if (Bone == null)
            {
                return;
            }

            Bone.localRotation = _restLocalRotation * Quaternion.AngleAxis(Value, Axis);
        }

        public void Reset()
        {
            if (Bone == null)
            {
                return;
            }

            Value = 0f;
            Bone.localRotation = _restLocalRotation;
        }

        public void UpdateRest()
        {
            if (Bone == null)
            {
                return;
            }

            _restLocalRotation = Bone.localRotation;
            Value = 0f;
        }
    }

    internal sealed class YUCPHandPoseEditingSession
    {
        private readonly Animator _animator;
        private readonly Dictionary<(YUCPHandSide hand, YUCPFingerType finger, FingerSegment segment), FingerSegmentState> _segments;
        private readonly Dictionary<YUCPHandSide, Dictionary<HumanBodyBones, Quaternion>> _restRotations;

        public Animator Animator => _animator;

        public YUCPHandPoseEditingSession(Animator animator)
        {
            _animator = animator;
            _segments = new Dictionary<(YUCPHandSide, YUCPFingerType, FingerSegment), FingerSegmentState>();
            _restRotations = new Dictionary<YUCPHandSide, Dictionary<HumanBodyBones, Quaternion>>
            {
                { YUCPHandSide.Left, new Dictionary<HumanBodyBones, Quaternion>() },
                { YUCPHandSide.Right, new Dictionary<HumanBodyBones, Quaternion>() }
            };

            InitializeSegments();
        }

        private void InitializeSegments()
        {
            if (_animator == null || _animator.avatar == null || !_animator.avatar.isHuman)
            {
                return;
            }

            SetupHand(YUCPHandSide.Left);
            SetupHand(YUCPHandSide.Right);
        }

        private void SetupHand(YUCPHandSide hand)
        {
            bool isLeft = hand == YUCPHandSide.Left;
            HumanBodyBones wristBone = isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            Transform wrist = _animator.GetBoneTransform(wristBone);
            if (wrist == null)
            {
                return;
            }

            Transform metacarpal = null;
            Transform proximal   = null;
            Transform intermediate = null;
            Transform distal     = null;

            foreach (YUCPFingerType finger in Enum.GetValues(typeof(YUCPFingerType)))
            {
                if (finger == YUCPFingerType.None)
                {
                    continue;
                }

                var bones = YUCPAvatarRigHelper.GetFingerBones(_animator, hand, finger);
                metacarpal   = bones.metacarpal;
                proximal     = bones.proximal;
                intermediate = bones.intermediate;
                distal       = bones.distal;

                AddSegment(hand, finger, FingerSegment.Metacarpal, metacarpal, Vector3.right, -25f, 25f);
                AddSegment(hand, finger, FingerSegment.Proximal, proximal, Vector3.right, -10f, 90f);
                AddSegment(hand, finger, FingerSegment.Intermediate, intermediate, Vector3.right, 0f, 90f);
                AddSegment(hand, finger, FingerSegment.Distal, distal, Vector3.right, 0f, 90f);
            }

            CacheRestRotations(hand);
        }

        private void AddSegment(
            YUCPHandSide hand,
            YUCPFingerType finger,
            FingerSegment segment,
            Transform bone,
            Vector3 axis,
            float minAngle,
            float maxAngle)
        {
            if (bone == null)
            {
                return;
            }

            var key = (hand, finger, segment);
            _segments[key] = new FingerSegmentState(bone, axis, minAngle, maxAngle);
        }

        private void CacheRestRotations(YUCPHandSide hand)
        {
            var rest = _restRotations[hand];
            rest.Clear();

            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                Transform t = _animator.GetBoneTransform(bone);
                if (t != null)
                {
                    rest[bone] = t.localRotation;
                }
            }

            foreach (var entry in EnumerateSegments(hand))
            {
                entry.state.UpdateRest();
            }
        }

        public IEnumerable<(YUCPFingerType finger, FingerSegment segment, FingerSegmentState state)> EnumerateSegments(YUCPHandSide hand)
        {
            foreach (var kvp in _segments)
            {
                if (kvp.Key.hand != hand)
                {
                    continue;
                }

                yield return (kvp.Key.finger, kvp.Key.segment, kvp.Value);
            }
        }

        public void ResetHand(YUCPHandSide hand)
        {
            foreach (var entry in EnumerateSegments(hand))
            {
                entry.state.Reset();
            }
        }

        public void ApplyPose(YUCPHandPoseAsset asset, YUCPHandSide handSide, float blend = 1f)
        {
            if (_animator == null || asset == null)
            {
                return;
            }

            YUCPUniversalLocalAxes handAxes = YUCPAvatarRigHelper.DetectHandAxes(YUCPAvatarRigHelper.GetWrist(_animator, handSide), _animator.transform);
            YUCPUniversalLocalAxes fingerAxes = new YUCPUniversalLocalAxes(Vector3.right, Vector3.up, Vector3.forward);

            var runtime = new YUCPRuntimeHandDescriptor(_animator, handSide, asset, asset.PoseType, blend >= 0.5f ? YUCPBlendPoseType.ClosedGrip : YUCPBlendPoseType.OpenGrip, handAxes, fingerAxes);
            YUCPHandPoseApplier.UpdateHandUsingRuntimeDescriptor(_animator, handSide, runtime);
            CacheRestRotations(handSide);
        }

        public void ApplyDescriptor(YUCPHandDescriptor descriptor, YUCPHandSide handSide)
        {
            if (descriptor == null)
            {
                return;
            }

            YUCPHandPoseAsset tempAsset = ScriptableObject.CreateInstance<YUCPHandPoseAsset>();
            tempAsset.PoseType = YUCPHandPoseType.Fixed;
            if (handSide == YUCPHandSide.Left)
            {
                tempAsset.HandDescriptorLeft = descriptor;
                tempAsset.HandDescriptorRight = descriptor.Mirrored();
            }
            else
            {
                tempAsset.HandDescriptorRight = descriptor;
                tempAsset.HandDescriptorLeft = descriptor.Mirrored();
            }

            ApplyPose(tempAsset, handSide, 1f);
            UnityEngine.Object.DestroyImmediate(tempAsset);
        }

        public YUCPHandDescriptor CaptureHand(YUCPHandSide hand)
        {
            Transform wrist = YUCPAvatarRigHelper.GetWrist(_animator, hand);
            if (wrist == null)
            {
                return null;
            }

            var descriptor = new YUCPHandDescriptor();
            foreach (YUCPFingerType finger in Enum.GetValues(typeof(YUCPFingerType)))
            {
                if (finger == YUCPFingerType.None)
                {
                    continue;
                }

                var (metacarpal, proximal, intermediate, distal) = YUCPAvatarRigHelper.GetFingerBones(_animator, hand, finger);
                if (proximal == null)
                {
                    continue;
                }

                YUCPUniversalLocalAxes handAxes = YUCPAvatarRigHelper.DetectHandAxes(wrist, _animator.transform);
                YUCPUniversalLocalAxes fingerAxes = YUCPUniversalLocalAxes.FromTransform(proximal, proximal.parent);

                var fingerDescriptor = new YUCPFingerDescriptor();
                fingerDescriptor.Compute(wrist, metacarpal, proximal, intermediate, distal, handAxes, fingerAxes, false);
                descriptor.SetFinger(finger, fingerDescriptor);
            }

            return descriptor;
        }

        public void SetValue(YUCPHandSide hand, YUCPFingerType finger, FingerSegment segment, float value)
        {
            var key = (hand, finger, segment);
            if (!_segments.TryGetValue(key, out FingerSegmentState state))
            {
                return;
            }

            state.Value = Mathf.Clamp(value, state.MinAngle, state.MaxAngle);
            state.Apply();
        }

        public float GetValue(YUCPHandSide hand, YUCPFingerType finger, FingerSegment segment)
        {
            var key = (hand, finger, segment);
            return _segments.TryGetValue(key, out FingerSegmentState state) ? state.Value : 0f;
        }

        public void RestoreRestPose(YUCPHandSide hand)
        {
            if (!_restRotations.TryGetValue(hand, out var rest))
            {
                return;
            }

            foreach (var kv in rest)
            {
                Transform t = _animator.GetBoneTransform(kv.Key);
                if (t != null)
                {
                    t.localRotation = kv.Value;
                }
            }

            ResetHand(hand);
        }
    }
}
