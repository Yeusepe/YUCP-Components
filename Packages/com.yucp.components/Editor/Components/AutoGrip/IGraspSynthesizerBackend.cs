using YUCP.Components.HandPoses;
using UnityEngine;

namespace YUCP.Components.Editor.AutoGrip
{
    /// <summary>
    /// Interface for grasp synthesis backends.
    /// Implementations generate hand poses that grip props without clipping.
    /// </summary>
    public interface IGraspSynthesizerBackend
    {
        /// <summary>
        /// Synthesizes a grasp pose for the specified hand.
        /// </summary>
        /// <param name="animator">Avatar animator with humanoid rig</param>
        /// <param name="isLeftHand">True for left hand, false for right</param>
        /// <param name="data">AutoGrip component data with settings</param>
        /// <param name="propCollision">Prepared prop collision data</param>
        /// <returns>Hand descriptor with grasp pose, or null on failure</returns>
        YUCPHandDescriptor SynthesizeGrasp(
            Animator animator,
            bool isLeftHand,
            AutoGripData data,
            PropCollisionData propCollision);

        /// <summary>
        /// Gets the name of this backend for logging.
        /// </summary>
        string Name { get; }
    }
}
