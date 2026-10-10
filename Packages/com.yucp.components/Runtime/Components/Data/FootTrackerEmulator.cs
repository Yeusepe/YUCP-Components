using UnityEngine;

namespace YUCP.Components
{
    /// <summary>
    /// Drives the humanoid foot goals from two draggable transforms, standing in for a pair of
    /// foot trackers.
    ///
    /// Av3Emulator reports the TrackingType parameter and applies tracking-control states, but it
    /// never moves a foot -- so it cannot show how a rig responds to a foot being planted, rotated
    /// or lifted. Unity's own humanoid IK is not VRChat's, but the distinction does not matter for
    /// validating a digitigrade rig: the rig's entire contract is "wherever the humanoid foot ends
    /// up, the visible paw must follow", and this puts the humanoid foot wherever you drag it.
    ///
    /// Deliberately NOT IEditorOnly: the SDK strips those during preprocessing, including the
    /// build VRCFury runs when you enter Play Mode, so the component would never get to run. It is
    /// not on VRChat's component whitelist, so an actual upload removes it anyway.
    /// </summary>
    [AddComponentMenu("YUCP/Foot Tracker Emulator (Testing)")]
    [RequireComponent(typeof(Animator))]
    public class FootTrackerEmulator : MonoBehaviour
    {
        [Tooltip("Drag this in Play Mode. The left humanoid foot is pinned to it.")]
        public Transform leftTracker;

        [Tooltip("Drag this in Play Mode. The right humanoid foot is pinned to it.")]
        public Transform rightTracker;

        [Range(0f, 1f)]
        [Tooltip("How strongly the feet are pinned. 0 hands the legs back to the animator.")]
        public float weight = 1f;

        [Tooltip("Also pin foot rotation, which is what a real puck gives you. Off leaves rotation to the animator.")]
        public bool applyRotation = true;

        [Tooltip("Create the tracker handles at the avatar's feet on the first Play Mode frame.")]
        public bool autoCreateHandles = true;

        [Header("Readout")]
        [Tooltip("Distance between the visible paw and the tracked foot. This is the number that says whether the rig is holding together -- it should stay near zero however you drag the trackers.")]
        public float leftPawError;
        public float rightPawError;

        private Animator animator;

        private void Start()
        {
            animator = GetComponent<Animator>();
            if (!autoCreateHandles) return;

            leftTracker = leftTracker != null ? leftTracker : MakeHandle(HumanBodyBones.LeftFoot, "LeftFootTracker");
            rightTracker = rightTracker != null ? rightTracker : MakeHandle(HumanBodyBones.RightFoot, "RightFootTracker");
        }

        private Transform MakeHandle(HumanBodyBones bone, string handleName)
        {
            var source = animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null;
            if (source == null) return null;

            var handle = new GameObject(handleName).transform;
            handle.SetPositionAndRotation(source.position, source.rotation);
            return handle;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (animator == null || !animator.isHuman) return;

            Apply(AvatarIKGoal.LeftFoot, leftTracker);
            Apply(AvatarIKGoal.RightFoot, rightTracker);
        }

        private void Apply(AvatarIKGoal goal, Transform tracker)
        {
            if (tracker == null) return;

            animator.SetIKPositionWeight(goal, weight);
            animator.SetIKPosition(goal, tracker.position);

            if (!applyRotation) return;
            animator.SetIKRotationWeight(goal, weight);
            animator.SetIKRotation(goal, tracker.rotation);
        }

        private void LateUpdate()
        {
            leftPawError = MeasurePawError("Metatarsus.L", HumanBodyBones.LeftFoot);
            rightPawError = MeasurePawError("Metatarsus.R", HumanBodyBones.RightFoot);
        }

        /// <summary>
        /// Distance from the visible paw -- the bone hanging under the generated metatarsus -- to the
        /// humanoid foot it is supposed to coincide with.
        /// </summary>
        private float MeasurePawError(string metatarsusName, HumanBodyBones foot)
        {
            if (animator == null || !animator.isHuman) return 0f;

            var tracked = animator.GetBoneTransform(foot);
            if (tracked == null) return 0f;

            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name != metatarsusName || t.childCount == 0) continue;
                return Vector3.Distance(t.GetChild(0).position, tracked.position);
            }
            return 0f;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            foreach (var tracker in new[] { leftTracker, rightTracker })
            {
                if (tracker == null) continue;
                Gizmos.DrawWireSphere(tracker.position, 0.05f);
                Gizmos.DrawRay(tracker.position, tracker.forward * 0.15f);
            }
        }
    }
}
