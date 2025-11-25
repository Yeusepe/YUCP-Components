// Portions adapted from UltimateXR (MIT License) by VRMADA
// Original source: UltimateXR/Scripts/Manipulation/HandPoses/UxrHandPoseAsset.cs
// Adapted for YUCP Components

using System;
using UnityEngine;

namespace YUCP.Components.HandPoses
{
    [Serializable]
    public class YUCPHandPoseAsset : ScriptableObject
    {
        public const int CurrentVersion = 1;

        [SerializeField] private int               _handPoseAssetVersion;
        [SerializeField] private YUCPHandPoseType   _poseType;
        [SerializeField] private YUCPHandDescriptor _handDescriptorLeft;
        [SerializeField] private YUCPHandDescriptor _handDescriptorRight;
        [SerializeField] private YUCPHandDescriptor _handDescriptorOpenLeft;
        [SerializeField] private YUCPHandDescriptor _handDescriptorOpenRight;
        [SerializeField] private YUCPHandDescriptor _handDescriptorClosedLeft;
        [SerializeField] private YUCPHandDescriptor _handDescriptorClosedRight;

        public int Version
        {
            get => _handPoseAssetVersion;
            set => _handPoseAssetVersion = value;
        }

        public YUCPHandPoseType PoseType
        {
            get => _poseType;
            set => _poseType = value;
        }

        public YUCPHandDescriptor HandDescriptorLeft
        {
            get => _handDescriptorLeft;
            set => _handDescriptorLeft = value;
        }

        public YUCPHandDescriptor HandDescriptorRight
        {
            get => _handDescriptorRight;
            set => _handDescriptorRight = value;
        }

        public YUCPHandDescriptor HandDescriptorOpenLeft
        {
            get => _handDescriptorOpenLeft;
            set => _handDescriptorOpenLeft = value;
        }

        public YUCPHandDescriptor HandDescriptorOpenRight
        {
            get => _handDescriptorOpenRight;
            set => _handDescriptorOpenRight = value;
        }

        public YUCPHandDescriptor HandDescriptorClosedLeft
        {
            get => _handDescriptorClosedLeft;
            set => _handDescriptorClosedLeft = value;
        }

        public YUCPHandDescriptor HandDescriptorClosedRight
        {
            get => _handDescriptorClosedRight;
            set => _handDescriptorClosedRight = value;
        }

        public YUCPHandDescriptor GetHandDescriptor(
            YUCPHandSide handSide,
            YUCPBlendPoseType blendPoseType = YUCPBlendPoseType.None)
        {
            return PoseType switch
            {
                YUCPHandPoseType.Fixed => handSide == YUCPHandSide.Left ? _handDescriptorLeft : _handDescriptorRight,
                YUCPHandPoseType.Blend when blendPoseType == YUCPBlendPoseType.OpenGrip => handSide == YUCPHandSide.Left ? _handDescriptorOpenLeft : _handDescriptorOpenRight,
                YUCPHandPoseType.Blend when blendPoseType == YUCPBlendPoseType.ClosedGrip => handSide == YUCPHandSide.Left ? _handDescriptorClosedLeft : _handDescriptorClosedRight,
                _ => null
            };
        }

        public YUCPHandDescriptor GetHandDescriptor(
            YUCPHandSide handSide,
            YUCPHandPoseType poseType,
            YUCPBlendPoseType blendPoseType = YUCPBlendPoseType.None)
        {
            return poseType switch
            {
                YUCPHandPoseType.Fixed => handSide == YUCPHandSide.Left ? _handDescriptorLeft : _handDescriptorRight,
                YUCPHandPoseType.Blend when blendPoseType == YUCPBlendPoseType.OpenGrip => handSide == YUCPHandSide.Left ? _handDescriptorOpenLeft : _handDescriptorOpenRight,
                YUCPHandPoseType.Blend when blendPoseType == YUCPBlendPoseType.ClosedGrip => handSide == YUCPHandSide.Left ? _handDescriptorClosedLeft : _handDescriptorClosedRight,
                _ => null
            };
        }
    }
}
