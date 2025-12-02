// Portions adapted from UltimateXR (MIT License) by VRMADA
// Original source: UltimateXR/Scripts/Avatar/Rig/UxrRuntimeHandPose.cs
// Adapted for YUCP Components - Works with Unity Animator instead of UxrAvatar

using System;
using UnityEngine;

namespace YUCP.Components.HandPoses
{
    /// <summary>
    ///     Runtime, lightweight version of <see cref="YUCPHandPoseAsset" />. It is used to describe the local orientations of
    ///     finger bones of a <see cref="YUCPHandPoseAsset" /> for a given avatar.
    ///     <see cref="YUCPHandPoseAsset" /> objects contain orientations in a well-known space. They are used to adapt hand
    ///     poses independently of the coordinate system used by each avatar. This means an additional transformation needs to
    ///     be performed to get to each avatar's coordinate system. <see cref="YUCPRuntimeHandPose" /> is used
    ///     to have a high performant version that already contains the bone orientations in each avatar's coordinate system
    ///     so that hand pose blending can be computed using much less processing power.
    /// </summary>
    public class YUCPRuntimeHandPose
    {
        #region Public Types & Data

        public string          PoseName { get; }
        public YUCPHandPoseType PoseType { get; }

        #endregion

        #region Constructors & Finalizer

        /// <summary>
        ///     Constructor.
        /// </summary>
        /// <param name="animator">Animator to compute the runtime hand pose for</param>
        /// <param name="handPoseAsset">Hand pose in a well-known coordinate system</param>
        /// <param name="handLocalAxes">Hand axes system</param>
        /// <param name="fingerLocalAxes">Finger axes system</param>
        public YUCPRuntimeHandPose(Animator animator, YUCPHandPoseAsset handPoseAsset, YUCPUniversalLocalAxes handLocalAxes, YUCPUniversalLocalAxes fingerLocalAxes)
        {
            PoseName                  = handPoseAsset.name;
            PoseType                  = handPoseAsset.PoseType;
            HandDescriptorLeft        = new YUCPRuntimeHandDescriptor(animator, YUCPHandSide.Left,  handPoseAsset, YUCPHandPoseType.Fixed, YUCPBlendPoseType.None, handLocalAxes, fingerLocalAxes);
            HandDescriptorRight       = new YUCPRuntimeHandDescriptor(animator, YUCPHandSide.Right, handPoseAsset, YUCPHandPoseType.Fixed, YUCPBlendPoseType.None, handLocalAxes, fingerLocalAxes);
            HandDescriptorOpenLeft    = new YUCPRuntimeHandDescriptor(animator, YUCPHandSide.Left,  handPoseAsset, YUCPHandPoseType.Blend, YUCPBlendPoseType.OpenGrip, handLocalAxes, fingerLocalAxes);
            HandDescriptorOpenRight   = new YUCPRuntimeHandDescriptor(animator, YUCPHandSide.Right, handPoseAsset, YUCPHandPoseType.Blend, YUCPBlendPoseType.OpenGrip, handLocalAxes, fingerLocalAxes);
            HandDescriptorClosedLeft  = new YUCPRuntimeHandDescriptor(animator, YUCPHandSide.Left,  handPoseAsset, YUCPHandPoseType.Blend, YUCPBlendPoseType.ClosedGrip, handLocalAxes, fingerLocalAxes);
            HandDescriptorClosedRight = new YUCPRuntimeHandDescriptor(animator, YUCPHandSide.Right, handPoseAsset, YUCPHandPoseType.Blend, YUCPBlendPoseType.ClosedGrip, handLocalAxes, fingerLocalAxes);
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Gets the given hand descriptor using the <see cref="PoseType" />.
        /// </summary>
        /// <param name="handSide">Hand to get the descriptor for</param>
        /// <param name="blendPoseType">
        ///     If <see cref="PoseType" /> is <see cref="YUCPHandPoseType.Blend" />, whether to get the open or
        ///     closed pose descriptor.
        /// </param>
        /// <returns>Hand descriptor</returns>
        public YUCPRuntimeHandDescriptor GetHandDescriptor(YUCPHandSide handSide, YUCPBlendPoseType blendPoseType = YUCPBlendPoseType.None)
        {
            return PoseType switch
                   {
                               YUCPHandPoseType.Fixed                                                   => handSide == YUCPHandSide.Left ? HandDescriptorLeft : HandDescriptorRight,
                               YUCPHandPoseType.Blend when blendPoseType == YUCPBlendPoseType.OpenGrip   => handSide == YUCPHandSide.Left ? HandDescriptorOpenLeft : HandDescriptorOpenRight,
                               YUCPHandPoseType.Blend when blendPoseType == YUCPBlendPoseType.ClosedGrip => handSide == YUCPHandSide.Left ? HandDescriptorClosedLeft : HandDescriptorClosedRight,
                               _                                                                       => throw new ArgumentOutOfRangeException(nameof(blendPoseType), blendPoseType, null)
                   };
        }

        #endregion

        #region Private Types & Data

        private YUCPRuntimeHandDescriptor HandDescriptorLeft        { get; }
        private YUCPRuntimeHandDescriptor HandDescriptorRight       { get; }
        private YUCPRuntimeHandDescriptor HandDescriptorOpenLeft    { get; }
        private YUCPRuntimeHandDescriptor HandDescriptorOpenRight   { get; }
        private YUCPRuntimeHandDescriptor HandDescriptorClosedLeft  { get; }
        private YUCPRuntimeHandDescriptor HandDescriptorClosedRight { get; }

        #endregion
    }
}

