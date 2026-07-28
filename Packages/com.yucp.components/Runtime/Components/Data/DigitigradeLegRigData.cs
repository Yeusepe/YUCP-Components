using System;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    /// <summary>
    /// Builds the Rexouium-style digitigrade leg rig at build time.
    ///
    /// The humanoid rig is moved onto a hidden plantigrade chain so VRChat's IK and full-body
    /// tracking keep seeing an ordinary three-bone human leg. The visible four-segment leg is then
    /// driven by VRC rotation constraints that pick between "copy the plantigrade bone" and "copy
    /// the Final IK solved digitigrade node", which an FX layer crossfades.
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

        [Header("Limit Poses")]
        [Tooltip("How far the metatarsus straightens when the toes are pressed up, in degrees.")]
        [Range(0f, 90f)]
        public float ankleUpAngle = 60f;

        [Tooltip("How far the metatarsus drops toward flat when the toes are pressed down, in degrees.")]
        [Range(0f, 90f)]
        public float ankleDownAngle = 25f;

        [Header("Tuning")]
        [Tooltip("Extra paw pitch about the toe tip in digitigrade mode, in degrees. Leave at 0 " +
                 "for a mesh authored digitigrade (the stance is already in the bind pose, and the " +
                 "digi goal is exactly the tracked foot, as on the Rexouium). Only raise this for " +
                 "a mesh authored plantigrade, where the stance has to be created.")]
        [Range(0f, 60f)]
        public float stanceAngle = 0f;

        [Tooltip("How much the knee stays open while the hock absorbs leg lift -- the Rexouium's " +
                 "signature fold. Implemented the way the live Rex rig does it: the internal " +
                 "first-pass solver leg is built SHORTER than the real leg, so its solved thigh " +
                 "aims more directly at the foot and the digitigrade chain folds at the hock " +
                 "instead of the knee. 0 = knee and hock share the fold; 1 = Rex-measured " +
                 "proportions (first-pass shin at ~53% of the real shin).")]
        [Range(0f, 1f)]
        public float kneeStraightness = 1f;

        [Tooltip("Default reactivity to toe curl and ground contact. 0 is a static digitigrade pose.")]
        [Range(0f, 1f)]
        public float anklesWeight = 0.75f;

        [Tooltip("How much the paw is pulled toward the tracked foot's orientation, so it stays flat on the ground.")]
        [Range(0f, 1f)]
        public float pawFlattenWeight = 0.2f;

        [Tooltip("Override the automatically derived bend plane. Leave zero to derive it from the metatarsus bend at rest.")]
        public Vector3 bendNormalOverride = Vector3.zero;

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
