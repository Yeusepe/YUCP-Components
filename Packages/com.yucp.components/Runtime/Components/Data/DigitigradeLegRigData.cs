using System;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    /// <summary>
    /// Builds a joint-limited digitigrade leg rig.
    ///
    /// The humanoid rig is moved onto a hidden plantigrade chain so VRChat's IK and full-body
    /// tracking keep seeing an ordinary three-bone human leg. The visible four-segment leg is
    /// driven by VRC constraints that pick between the plantigrade bone and the solved
    /// digitigrade node, which an FX layer crossfades.
    ///
    /// Requires a four-segment visible leg. Add YUCP/Digitigrade Leg Split first if the rig only has
    /// thigh/shin/foot.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("YUCP/Digitigrade Leg Rig")]
    [SupportBanner]
    public class DigitigradeLegRigData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Visible Leg Chain")]
        [Tooltip("Thigh. Leave empty to use the humanoid Upper Leg.")]
        public Transform leftThigh;

        [Tooltip("Shin. Leave empty to use the humanoid Lower Leg.")]
        public Transform leftShin;

        [Tooltip("Metatarsus -- the extra digitigrade segment. Leave empty to use the bone between shin and foot.")]
        public Transform leftMetatarsus;

        [Tooltip("Paw. Leave empty to use the humanoid Foot.")]
        public Transform leftFoot;

        public Transform rightThigh;
        public Transform rightShin;
        public Transform rightMetatarsus;
        public Transform rightFoot;

        [Header("Joint Limits")]
        [Tooltip("Solve the whole visible leg with knee and hock limits. Unreachable tracking poses move the paw instead of folding through the joint. Disable for the legacy two-pass solve.")]
        public bool enforceJointLimits = true;

        [Tooltip("Knee included angle in degrees: minimum flexed angle, maximum extended angle. 180 is straight. Tune for the character's anatomy.")]
        public Vector2 kneeAngleLimits = new Vector2(40f, 165f);

        [Tooltip("Hock included angle in degrees: minimum flexed angle, maximum extended angle. These are joint angles, not bone Euler rotations.")]
        public Vector2 hockAngleLimits = new Vector2(40f, 165f);

        [Header("Limit Poses")]
        [Tooltip("Metatarsus rotation toward the toes-up limit pose, in degrees.")]
        [Range(0f, 90f)]
        public float ankleUpAngle = 60f;

        [Tooltip("How far the metatarsus drops toward flat when the toes are pressed down, in degrees.")]
        [Range(0f, 90f)]
        public float ankleDownAngle = 25f;

        [Header("Tuning")]
        [Tooltip("Extra paw pitch about the toe tip in digitigrade mode, in degrees. Leave at 0 " +
                 "for a mesh authored digitigrade (the stance is already in the bind pose, and the " +
                 "digi goal is exactly the tracked foot). Only raise this for a mesh authored " +
                 "plantigrade, where the stance has to be created.")]
        [Range(0f, 60f)]
        public float stanceAngle = 0f;

        [Tooltip("Redistributes leg lift between the knee and hock using helper proportions. " +
                 "0 = original humanoid proportions; 1 = a longer helper thigh " +
                 "and a helper shin at ~53% of the real shin. The helper knee moves toward " +
                 "the ankle while the hip and ankle endpoints stay fixed.")]
        [Range(0f, 1f)]
        public float kneeStraightness = 1f;

        [Tooltip("Override the automatically derived bend plane. Leave zero to derive it from the metatarsus bend at rest.")]
        public Vector3 bendNormalOverride = Vector3.zero;

        [Header("Wearer Defaults")]
        [Tooltip("Thigh motion relative to the hips. 0 holds the rest orientation; 1 follows the full solved rotation. Intermediate values scale the motion continuously.")]
        [Range(0f, 1f)]
        public float thighMovement = 1f;

        [Tooltip("Knee motion. 0 holds the nearest safe rest angle; 1 follows the solved knee. Intermediate values blend locally within the configured joint limits.")]
        [Range(0f, 1f)]
        public float kneeMovement = 1f;

        [Tooltip("Hock motion. 0 holds the nearest safe rest angle; 1 follows the solved hock. Intermediate values blend locally within the configured joint limits.")]
        [Range(0f, 1f)]
        public float hockMovement = 1f;

        [InspectorName("Contact Response")]
        [Tooltip("Paw response to toe and ground contacts: 0 ignores contact flattening; 1 fully applies it.")]
        [Range(0f, 1f)]
        public float anklesWeight = 0.5f;

        [InspectorName("Paw Follow")]
        [Tooltip("Paw orientation: 0 follows the hock; 1 follows the tracked foot. Contact Response can add flattening when planted.")]
        [Range(0f, 1f)]
        public float pawFlattenWeight = 0.2f;

        [Header("Menu")]
        [Tooltip("Submenu path for the generated toggle, e.g. \"Legs\". Empty puts it at the root.")]
        public string menuPath = "Legs";

        [Tooltip("Name of the generated menu toggle.")]
        public string menuName = "Digitigrade";

        [Header("Diagnostics")]
        public bool verboseLogging = false;

        [SerializeField, HideInInspector] private string lastBuildSummary;
        [SerializeField, HideInInspector] private long lastBuildTicks;

        public int PreprocessOrder => 0;

        public bool OnPreprocess() => true;

        public void SetBuildSummary(string summary)
        {
            lastBuildSummary = summary;
            lastBuildTicks = DateTime.UtcNow.Ticks;
        }

        public string GetBuildSummary() => lastBuildSummary;

        private void Reset()
        {
            ResolveBones();
        }

        /// <summary>
        /// Fills unassigned slots from the humanoid mapping. The metatarsus is whatever bone sits
        /// between the humanoid Lower Leg and Foot -- which is what the split component inserts.
        /// </summary>
        public bool ResolveBones()
        {
            var animator = GetComponentInParent<Animator>();
            if (animator == null || !animator.isHuman) return false;

            if (leftThigh == null) leftThigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            if (leftShin == null) leftShin = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            if (leftFoot == null) leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (leftMetatarsus == null) leftMetatarsus = FindBetween(leftShin, leftFoot);

            if (rightThigh == null) rightThigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (rightShin == null) rightShin = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            if (rightFoot == null) rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (rightMetatarsus == null) rightMetatarsus = FindBetween(rightShin, rightFoot);

            return IsComplete();
        }

        public bool IsComplete()
        {
            return leftThigh != null && leftShin != null && leftMetatarsus != null && leftFoot != null
                && rightThigh != null && rightShin != null && rightMetatarsus != null && rightFoot != null;
        }

        /// <summary>
        /// The metatarsus for a given shin/foot pair. Two supported layouts: nested (imported
        /// four-segment rigs: shin -> metatarsus -> foot) and sibling (what the Split component
        /// builds for humanoid rigs, where the metatarsus sits beside the foot because Unity's
        /// humanoid cannot tolerate a mapped bone under an inserted joint).
        /// </summary>
        public static Transform FindBetween(Transform upper, Transform lower)
        {
            if (upper == null || lower == null) return null;

            // Nested: exactly one bone between shin and foot.
            if (lower.parent != null && lower.parent != upper && lower.parent.parent == upper) return lower.parent;

            // Sibling: a bone under the shin named like a split joint.
            if (lower.parent == upper)
            {
                for (int i = 0; i < upper.childCount; i++)
                {
                    var child = upper.GetChild(i);
                    if (child == lower) continue;
                    if (child.name.StartsWith("Metatarsus")) return child;
                }
            }

            return null;
        }
    }
}
