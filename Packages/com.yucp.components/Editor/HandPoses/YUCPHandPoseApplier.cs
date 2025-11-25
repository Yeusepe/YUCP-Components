// Portions adapted from UltimateXR (MIT License) by VRMADA
// Original source: UltimateXR/Scripts/Avatar/Rig/UxrAvatarRig.HandRuntimeTransformation.cs
// Adapted for YUCP Components - Works with Unity Animator and HumanBodyBones
// NOTE: This is for editor preview only. Runtime uses muscle curves in AnimationClip.

using UnityEngine;

namespace YUCP.Components.HandPoses
{
    /// <summary>
    /// Applies hand poses to bone transforms (editor preview only).
    /// Runtime uses muscle curves in AnimationClip instead of direct bone manipulation.
    /// </summary>
    public static class YUCPHandPoseApplier
    {
        /// <summary>
        /// Updates the hand transforms using a runtime hand descriptor.
        /// </summary>
        /// <param name="animator">Animator to update</param>
        /// <param name="handSide">The hand to update</param>
        /// <param name="handDescriptor">The runtime descriptor of the hand pose</param>
        public static void UpdateHandUsingRuntimeDescriptor(Animator animator, YUCPHandSide handSide, YUCPRuntimeHandDescriptor handDescriptor)
        {
            if (animator == null || handDescriptor == null) return;

            UpdateFingerUsingRuntimeDescriptor(animator, handSide, YUCPFingerType.Thumb,  handDescriptor.Thumb);
            UpdateFingerUsingRuntimeDescriptor(animator, handSide, YUCPFingerType.Index,  handDescriptor.Index);
            UpdateFingerUsingRuntimeDescriptor(animator, handSide, YUCPFingerType.Middle, handDescriptor.Middle);
            UpdateFingerUsingRuntimeDescriptor(animator, handSide, YUCPFingerType.Ring,   handDescriptor.Ring);
            UpdateFingerUsingRuntimeDescriptor(animator, handSide, YUCPFingerType.Little, handDescriptor.Little);
        }

        /// <summary>
        /// Updates the hand transforms blending between two runtime hand descriptors.
        /// </summary>
        /// <param name="animator">Animator to update</param>
        /// <param name="handSide">The hand to update</param>
        /// <param name="handDescriptorA">The runtime descriptor of the hand pose to blend from</param>
        /// <param name="handDescriptorB">The runtime descriptor of the hand pose to blend to</param>
        /// <param name="blend">Interpolation value [0.0, 1.0]</param>
        public static void UpdateHandUsingRuntimeDescriptor(Animator animator, YUCPHandSide handSide, YUCPRuntimeHandDescriptor handDescriptorA, YUCPRuntimeHandDescriptor handDescriptorB, float blend)
        {
            if (animator == null || handDescriptorA == null || handDescriptorB == null) return;

            UpdateFingerUsingRuntimeDescriptor(animator, handSide, YUCPFingerType.Thumb,  handDescriptorA.Thumb,  handDescriptorB.Thumb,  blend);
            UpdateFingerUsingRuntimeDescriptor(animator, handSide, YUCPFingerType.Index,  handDescriptorA.Index,  handDescriptorB.Index,  blend);
            UpdateFingerUsingRuntimeDescriptor(animator, handSide, YUCPFingerType.Middle, handDescriptorA.Middle, handDescriptorB.Middle, blend);
            UpdateFingerUsingRuntimeDescriptor(animator, handSide, YUCPFingerType.Ring,   handDescriptorA.Ring,   handDescriptorB.Ring,   blend);
            UpdateFingerUsingRuntimeDescriptor(animator, handSide, YUCPFingerType.Little, handDescriptorA.Little, handDescriptorB.Little, blend);
        }

        /// <summary>
        /// Updates a finger's transforms from a runtime finger descriptor.
        /// </summary>
        /// <param name="animator">Animator containing the finger bones</param>
        /// <param name="handSide">Which hand</param>
        /// <param name="fingerType">The finger to update</param>
        /// <param name="fingerDescriptor">The runtime descriptor to get the data from</param>
        private static void UpdateFingerUsingRuntimeDescriptor(Animator animator, YUCPHandSide handSide, YUCPFingerType fingerType, YUCPRuntimeFingerDescriptor fingerDescriptor)
        {
            if (fingerDescriptor == null) return;

            var (metacarpal, proximal, intermediate, distal) = YUCPAvatarRigHelper.GetFingerBones(animator, handSide, fingerType);

            if (fingerDescriptor.HasMetacarpalInfo && metacarpal != null)
            {
                metacarpal.localRotation = fingerDescriptor.MetacarpalRotation;
            }

            if (proximal != null)
            {
                proximal.localRotation = fingerDescriptor.ProximalRotation;
            }

            if (intermediate != null)
            {
                intermediate.localRotation = fingerDescriptor.IntermediateRotation;
            }

            if (distal != null)
            {
                distal.localRotation = fingerDescriptor.DistalRotation;
            }
        }

        /// <summary>
        /// Updates a finger's transforms from a runtime finger descriptor with blending.
        /// </summary>
        /// <param name="animator">Animator containing the finger bones</param>
        /// <param name="handSide">Which hand</param>
        /// <param name="fingerType">The finger to update</param>
        /// <param name="fingerDescriptorA">The runtime descriptor to blend from</param>
        /// <param name="fingerDescriptorB">The runtime descriptor to blend to</param>
        /// <param name="blend">The interpolation parameter [0.0, 1.0]</param>
        private static void UpdateFingerUsingRuntimeDescriptor(Animator animator, YUCPHandSide handSide, YUCPFingerType fingerType, YUCPRuntimeFingerDescriptor fingerDescriptorA, YUCPRuntimeFingerDescriptor fingerDescriptorB, float blend)
        {
            if (fingerDescriptorA == null || fingerDescriptorB == null) return;

            var (metacarpal, proximal, intermediate, distal) = YUCPAvatarRigHelper.GetFingerBones(animator, handSide, fingerType);

            if (fingerDescriptorA.HasMetacarpalInfo && fingerDescriptorB.HasMetacarpalInfo && metacarpal != null)
            {
                metacarpal.localRotation = Quaternion.Slerp(fingerDescriptorA.MetacarpalRotation, fingerDescriptorB.MetacarpalRotation, blend);
            }

            if (proximal != null)
            {
                proximal.localRotation = Quaternion.Slerp(fingerDescriptorA.ProximalRotation, fingerDescriptorB.ProximalRotation, blend);
            }

            if (intermediate != null)
            {
                intermediate.localRotation = Quaternion.Slerp(fingerDescriptorA.IntermediateRotation, fingerDescriptorB.IntermediateRotation, blend);
            }

            if (distal != null)
            {
                distal.localRotation = Quaternion.Slerp(fingerDescriptorA.DistalRotation, fingerDescriptorB.DistalRotation, blend);
            }
        }
    }
}

