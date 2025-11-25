// Portions adapted from UltimateXR (MIT License) by VRMADA
// Original source: UltimateXR/Scripts/Avatar/Rig/UxrRuntimeHandDescriptor.cs
// Adapted for YUCP Components - Works with Unity Animator instead of UxrAvatar

using UnityEngine;

namespace YUCP.Components.HandPoses
{
    /// <summary>
    ///     Runtime, lightweight version of <see cref="YUCPHandDescriptor" />. It is used to describe the local orientations of
    ///     finger bones of a <see cref="YUCPHandPoseAsset" /> for a given avatar.
    ///     <see cref="YUCPHandPoseAsset" /> objects contain orientations in a well-known space. They are used to adapt hand
    ///     poses independently of the coordinate system used by each avatar. This means an additional transformation needs to
    ///     be performed to get to each avatar's coordinate system. <see cref="YUCPRuntimeHandDescriptor" /> is used
    ///     to have a high performant version that already contains the bone orientations in each avatar's coordinate system
    ///     so that hand pose blending can be computed using much less processing power.
    /// </summary>
    public class YUCPRuntimeHandDescriptor
    {
        #region Public Types & Data

        public YUCPRuntimeFingerDescriptor Index  { get; }
        public YUCPRuntimeFingerDescriptor Middle { get; }
        public YUCPRuntimeFingerDescriptor Ring   { get; }
        public YUCPRuntimeFingerDescriptor Little { get; }
        public YUCPRuntimeFingerDescriptor Thumb  { get; }

        #endregion

        #region Constructors & Finalizer

        /// <summary>
        ///     Default constructor.
        /// </summary>
        public YUCPRuntimeHandDescriptor()
        {
            Index  = new YUCPRuntimeFingerDescriptor();
            Middle = new YUCPRuntimeFingerDescriptor();
            Ring   = new YUCPRuntimeFingerDescriptor();
            Little = new YUCPRuntimeFingerDescriptor();
            Thumb  = new YUCPRuntimeFingerDescriptor();
        }

        /// <summary>
        ///     Constructor.
        /// </summary>
        /// <param name="animator">Animator to compute the runtime hand descriptor for</param>
        /// <param name="handSide">Which hand to store</param>
        /// <param name="handPoseAsset">Hand pose to transform</param>
        /// <param name="handPoseType">Which hand pose information to store</param>
        /// <param name="blendPoseType">
        ///     If <paramref name="handPoseType" /> is <see cref="YUCPHandPoseType.Blend" />, which pose to
        ///     store
        /// </param>
        /// <param name="handLocalAxes">Hand axes system</param>
        /// <param name="fingerLocalAxes">Finger axes system</param>
        public YUCPRuntimeHandDescriptor(Animator animator, YUCPHandSide handSide, YUCPHandPoseAsset handPoseAsset, YUCPHandPoseType handPoseType, YUCPBlendPoseType blendPoseType, YUCPUniversalLocalAxes handLocalAxes, YUCPUniversalLocalAxes fingerLocalAxes)
        {
            YUCPHandDescriptor handDescriptor = handPoseAsset.GetHandDescriptor(handSide, handPoseType, blendPoseType);
            if (handDescriptor == null) return;

            Index  = new YUCPRuntimeFingerDescriptor(animator, handSide, handDescriptor, YUCPFingerType.Index, handLocalAxes, fingerLocalAxes);
            Middle = new YUCPRuntimeFingerDescriptor(animator, handSide, handDescriptor, YUCPFingerType.Middle, handLocalAxes, fingerLocalAxes);
            Ring   = new YUCPRuntimeFingerDescriptor(animator, handSide, handDescriptor, YUCPFingerType.Ring, handLocalAxes, fingerLocalAxes);
            Little = new YUCPRuntimeFingerDescriptor(animator, handSide, handDescriptor, YUCPFingerType.Little, handLocalAxes, fingerLocalAxes);
            Thumb  = new YUCPRuntimeFingerDescriptor(animator, handSide, handDescriptor, YUCPFingerType.Thumb, handLocalAxes, fingerLocalAxes);
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Copies the data from another descriptor.
        /// </summary>
        /// <param name="handDescriptor">Descriptor to compute the data from</param>
        public void CopyFrom(YUCPRuntimeHandDescriptor handDescriptor)
        {
            if (handDescriptor == null)
            {
                return;
            }

            Index.CopyFrom(handDescriptor.Index);
            Middle.CopyFrom(handDescriptor.Middle);
            Ring.CopyFrom(handDescriptor.Ring);
            Little.CopyFrom(handDescriptor.Little);
            Thumb.CopyFrom(handDescriptor.Thumb);
        }

        /// <summary>
        ///     Interpolates towards another runtime hand descriptor.
        /// </summary>
        /// <param name="handDescriptor">Runtime hand descriptor to interpolate towards</param>
        /// <param name="blend">Interpolation value [0.0, 1.0]</param>
        public void InterpolateTo(YUCPRuntimeHandDescriptor handDescriptor, float blend)
        {
            if (handDescriptor == null)
            {
                return;
            }

            Index.InterpolateTo(handDescriptor.Index, blend);
            Middle.InterpolateTo(handDescriptor.Middle, blend);
            Ring.InterpolateTo(handDescriptor.Ring, blend);
            Little.InterpolateTo(handDescriptor.Little, blend);
            Thumb.InterpolateTo(handDescriptor.Thumb, blend);
        }

        #endregion
    }
}

