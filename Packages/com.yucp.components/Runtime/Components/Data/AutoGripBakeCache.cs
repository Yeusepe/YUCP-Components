using System;
using UnityEngine;

namespace YUCP.Components
{
    /// <summary>
    /// Caches AutoGrip bake diagnostics and input hash for skip logic.
    /// Stores information about the last successful bake to avoid redundant work.
    /// </summary>
    [CreateAssetMenu(fileName = "AutoGripBakeCache", menuName = "YUCP/AutoGrip Bake Cache")]
    public class AutoGripBakeCache : ScriptableObject
    {
        [Header("Input Hash")]
        [Tooltip("Hash of inputs used for last bake (mesh GUID + bone transforms + gripPoint + settings).")]
        public string inputHash = "";

        [Header("Generated Clips")]
        [Tooltip("Path to generated left hand clip (relative to Assets).")]
        public string generatedClipPathLeft = "";

        [Tooltip("Path to generated right hand clip (relative to Assets).")]
        public string generatedClipPathRight = "";

        [Header("Cached Data")]
        [Tooltip("Cached muscle indices for left hand fingers.")]
        public int[] leftHandMuscleIndices;

        [Tooltip("Cached muscle indices for right hand fingers.")]
        public int[] rightHandMuscleIndices;

        [Tooltip("Best gripPoint transform adjustment found during bake.")]
        public Vector3 bestGripPointPositionOffset;
        public Quaternion bestGripPointRotationOffset = Quaternion.identity;

        [Header("Diagnostics")]
        [Tooltip("Duration of last bake in seconds.")]
        public float lastBakeDuration = 0f;

        [Tooltip("Timestamp of last successful bake.")]
        public string lastBakeTimestamp = "";

        [Tooltip("Number of contact points achieved in best solution.")]
        public int bestContactCount = 0;

        [Tooltip("Score of best solution (lower is better).")]
        public float bestScore = float.MaxValue;

        [Tooltip("Hand mesh that was used for radius estimation.")]
        public string usedHandMeshName = "";

        [Header("Finger Radii")]
        [Tooltip("Detected/computed finger radii in meters.")]
        public FingerRadiiData leftHandRadii = new FingerRadiiData();
        public FingerRadiiData rightHandRadii = new FingerRadiiData();

        /// <summary>
        /// Clears all cached data for a fresh bake.
        /// </summary>
        public void Clear()
        {
            inputHash = "";
            generatedClipPathLeft = "";
            generatedClipPathRight = "";
            leftHandMuscleIndices = null;
            rightHandMuscleIndices = null;
            bestGripPointPositionOffset = Vector3.zero;
            bestGripPointRotationOffset = Quaternion.identity;
            lastBakeDuration = 0f;
            lastBakeTimestamp = "";
            bestContactCount = 0;
            bestScore = float.MaxValue;
            usedHandMeshName = "";
            leftHandRadii = new FingerRadiiData();
            rightHandRadii = new FingerRadiiData();
        }

        /// <summary>
        /// Checks if the cache is valid for the given input hash.
        /// </summary>
        public bool IsValidFor(string newInputHash)
        {
            return !string.IsNullOrEmpty(inputHash) && inputHash == newInputHash;
        }
    }

    /// <summary>
    /// Stores detected finger radii for caching.
    /// </summary>
    [Serializable]
    public class FingerRadiiData
    {
        public float thumb = 0.008f;
        public float index = 0.007f;
        public float middle = 0.007f;
        public float ring = 0.006f;
        public float little = 0.005f;

        public float GetRadius(YUCPFingerType finger)
        {
            switch (finger)
            {
                case YUCPFingerType.Thumb: return thumb;
                case YUCPFingerType.Index: return index;
                case YUCPFingerType.Middle: return middle;
                case YUCPFingerType.Ring: return ring;
                case YUCPFingerType.Little: return little;
                default: return 0.007f;
            }
        }

        public void SetRadius(YUCPFingerType finger, float value)
        {
            switch (finger)
            {
                case YUCPFingerType.Thumb: thumb = value; break;
                case YUCPFingerType.Index: index = value; break;
                case YUCPFingerType.Middle: middle = value; break;
                case YUCPFingerType.Ring: ring = value; break;
                case YUCPFingerType.Little: little = value; break;
            }
        }
    }
}
