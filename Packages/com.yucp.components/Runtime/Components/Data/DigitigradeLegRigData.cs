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

        [Tooltip("The extra digitigrade segment. Leave empty to use the bone between the shin and the foot.")]
        public Transform leftMetatarsus;

        [Tooltip("Paw. Leave empty to use the humanoid Foot.")]
        public Transform leftFoot;

        [Tooltip("Thigh. Leave empty to use the humanoid Upper Leg.")]
        public Transform rightThigh;

        [Tooltip("Shin. Leave empty to use the humanoid Lower Leg.")]
        public Transform rightShin;

        [Tooltip("The extra digitigrade segment. Leave empty to use the bone between the shin and the foot.")]
        public Transform rightMetatarsus;

        [Tooltip("Paw. Leave empty to use the humanoid Foot.")]
        public Transform rightFoot;

        [Header("Joint Limits")]
        [Tooltip("Solves the leg within the knee and hock limits, so an unreachable pose moves the paw instead of folding the joint. Turn off for the original two-pass fold.")]
        public bool enforceJointLimits = true;

        [Tooltip("Smallest (flexed) and largest (extended) knee angle, in degrees. 180 is straight.")]
        public Vector2 kneeAngleLimits = new Vector2(40f, 165f);

        [Tooltip("Smallest (flexed) and largest (extended) hock angle, in degrees. These are joint angles, not bone rotations.")]
        public Vector2 hockAngleLimits = new Vector2(40f, 165f);

        [Header("Limit Poses")]
        [Tooltip("Metatarsus rotation toward the toes-up limit pose, in degrees.")]
        [Range(0f, 90f)]
        public float ankleUpAngle = 60f;

        [Tooltip("How far the metatarsus drops toward flat when the toes are pressed down, in degrees.")]
        [Range(0f, 90f)]
        public float ankleDownAngle = 25f;

        [Header("Tuning")]
        [Tooltip("Extra paw pitch about the toe tip, in degrees. Leave at 0 for a mesh modelled " +
                 "digitigrade; raise it only for a mesh modelled plantigrade.")]
        [Range(0f, 60f)]
        public float stanceAngle = 0f;

        [Tooltip("Shifts leg lift from the knee toward the hock. 0 keeps the humanoid proportions; " +
                 "1 uses a longer helper thigh and a helper shin about half as long.")]
        [Range(0f, 1f)]
        public float kneeStraightness = 1f;

        [Tooltip("The plane the leg bends in. Leave at zero to derive it from the metatarsus bend at rest.")]
        public Vector3 bendNormalOverride = Vector3.zero;

        [Header("Wearer Defaults")]
        [Tooltip("How far the thigh follows the solve: 0 holds its rest orientation, 1 follows it fully.")]
        [Range(0f, 1f)]
        public float thighMovement = 1f;

        [Tooltip("How far the knee follows the solve: 0 holds the nearest safe rest angle, 1 follows it fully within the joint limits.")]
        [Range(0f, 1f)]
        public float kneeMovement = 1f;

        [Tooltip("How far the hock follows the solve: 0 holds the nearest safe rest angle, 1 follows it fully within the joint limits.")]
        [Range(0f, 1f)]
        public float hockMovement = 1f;

        [InspectorName("Contact Response")]
        [Tooltip("How much toe and ground contacts flatten the paw: 0 ignores them, 1 applies them fully.")]
        [Range(0f, 1f)]
        public float anklesWeight = 0.5f;

        [InspectorName("Paw Follow")]
        [Tooltip("Paw orientation: 0 follows the hock, 1 follows the tracked foot. Contact Response can flatten it further when planted.")]
        [Range(0f, 1f)]
        public float pawFlattenWeight = 0.2f;

        [Header("Menu")]
        [Tooltip("Submenu for the toggle, for example Legs. Leave empty for the menu root.")]
        public string menuPath = "Legs";

        [Tooltip("Name of the toggle in the avatar menu.")]
        public string menuName = "Digitigrade";

        [Header("Diagnostics")]
        [Tooltip("Logs each step of the build to the Console.")]
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
