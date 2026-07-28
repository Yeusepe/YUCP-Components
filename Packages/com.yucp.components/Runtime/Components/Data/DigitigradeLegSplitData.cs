using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    /// <summary>
    /// Inserts extra joints into the lower leg so a plantigrade rig can articulate as a
    /// digitigrade one. Runs after VRCFury's armature link, so every skinned mesh bound to
    /// the leg -- body and armature-linked clothing alike -- is re-weighted by the same rule.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("YUCP/Digitigrade Leg Split")]
    [SupportBanner]
    public class DigitigradeLegSplitData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Serializable]
        public class Split
        {
            [Tooltip("Name given to the generated bone.")]
            public string name = "Metatarsus";

            [Tooltip("Where along the source bone the joint is inserted. 0 = knee, 1 = ankle.")]
            [Range(0.02f, 0.98f)]
            public float position = 0.67f;

            [Tooltip("Moves the joint off the straight knee-to-ankle line, in metres, in the source " +
                     "bone's local frame. The line between two bone origins is a chord through a " +
                     "curved leg, so the real hock usually does not sit on it.")]
            public Vector3 offset = Vector3.zero;

            [Tooltip("Tilt of the joint, in degrees, relative to the source bone. Rotates the cut " +
                     "plane so the weight boundary follows the real crease instead of sitting " +
                     "square to the bone, and gives the generated bone the same rest orientation. " +
                     "Nothing moves at rest -- the tilt is baked into the bind pose.")]
            public Vector3 angle = Vector3.zero;

            public Split() { }

            public Split(string name, float position) : this(name, position, Vector3.zero, Vector3.zero) { }

            public Split(string name, float position, Vector3 offset, Vector3 angle)
            {
                this.name = name;
                this.position = position;
                this.offset = offset;
                this.angle = angle;
            }
        }

        [Header("Bones")]
        [Tooltip("Bone the new joints are inserted into. Leave empty to use the humanoid Lower Leg.")]
        public Transform leftSourceBone;

        [Tooltip("Bone at the far end of the source segment. Leave empty to use the humanoid Foot.")]
        public Transform leftEndBone;

        public Transform rightSourceBone;
        public Transform rightEndBone;

        [Header("Splits")]
        [Tooltip("Joints inserted into each lower leg, ordered from knee to ankle. One split is enough to make a 4-segment digitigrade leg.")]
        public List<Split> splits = new List<Split> { new Split("Metatarsus", 0.67f) };

        [Tooltip("Width of the smooth weight transition either side of each joint, as a fraction of the source bone's length. 0 gives a hard edge.")]
        [Range(0f, 0.25f)]
        public float blendBand = 0.06f;

        [Tooltip("Mirror the offset and tilt onto the right leg. Turn this off if the rig's left " +
                 "and right bone axes are already mirrored, which would double up the flip.")]
        public bool mirrorRightLeg = true;

        [Header("Diagnostics")]
        [Tooltip("Print per-mesh detail while building.")]
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

        public DateTime? GetLastBuildTimeUtc()
        {
            if (lastBuildTicks <= 0) return null;
            try { return new DateTime(lastBuildTicks, DateTimeKind.Utc); }
            catch { return null; }
        }

        /// <summary>Unity calls this when the component is first added.</summary>
        private void Reset()
        {
            ResolveBones();
        }

        /// <summary>
        /// Fills in any unassigned bone slots from the avatar's humanoid mapping.
        /// Returns false when the rig is not humanoid or the leg bones are missing.
        /// </summary>
        public bool ResolveBones()
        {
            var animator = GetComponentInParent<Animator>();
            if (animator == null || !animator.isHuman) return false;

            if (leftSourceBone == null) leftSourceBone = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            if (leftEndBone == null) leftEndBone = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (rightSourceBone == null) rightSourceBone = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            if (rightEndBone == null) rightEndBone = animator.GetBoneTransform(HumanBodyBones.RightFoot);

            return leftSourceBone != null && leftEndBone != null
                && rightSourceBone != null && rightEndBone != null;
        }

        /// <summary>
        /// Splits sorted knee-to-ankle with duplicate positions dropped. Positions and names are
        /// always derived together so their counts cannot drift apart.
        /// </summary>
        public List<Split> GetOrderedSplits()
        {
            var ordered = new List<Split>();
            if (splits == null) return ordered;

            foreach (var split in splits)
            {
                if (split == null) continue;
                ordered.Add(new Split(split.name, Mathf.Clamp(split.position, 0.02f, 0.98f), split.offset, split.angle));
            }

            ordered.Sort((a, b) => a.position.CompareTo(b.position));

            for (int i = ordered.Count - 1; i > 0; i--)
            {
                if (Mathf.Approximately(ordered[i].position, ordered[i - 1].position)) ordered.RemoveAt(i);
            }

            return ordered;
        }

        public float[] GetSortedPositions()
        {
            var ordered = GetOrderedSplits();
            var values = new float[ordered.Count];
            for (int i = 0; i < ordered.Count; i++) values[i] = ordered[i].position;
            return values;
        }

        /// <summary>
        /// Off-axis offsets, in the same order as <see cref="GetSortedPositions"/>. Pass
        /// <paramref name="mirror"/> for the right leg when <see cref="mirrorRightLeg"/> is set.
        /// </summary>
        public Vector3[] GetSortedOffsets(bool mirror)
        {
            var ordered = GetOrderedSplits();
            var offsets = new Vector3[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                var o = ordered[i].offset;
                offsets[i] = mirror ? new Vector3(-o.x, o.y, o.z) : o;
            }
            return offsets;
        }

        /// <summary>Tilt angles, in the same order as <see cref="GetSortedPositions"/>.</summary>
        public Vector3[] GetSortedAngles(bool mirror)
        {
            var ordered = GetOrderedSplits();
            var angles = new Vector3[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                var a = ordered[i].angle;
                angles[i] = mirror ? new Vector3(a.x, -a.y, -a.z) : a;
            }
            return angles;
        }

        /// <summary>Generated bone names, in the same order as <see cref="GetSortedPositions"/>.</summary>
        public string[] GetSortedNames(string suffix)
        {
            var ordered = GetOrderedSplits();
            var names = new string[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                var raw = string.IsNullOrWhiteSpace(ordered[i].name) ? "Split" + i : ordered[i].name.Trim();
                names[i] = raw + suffix;
            }
            return names;
        }
    }
}
