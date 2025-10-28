using UnityEngine;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Generates finger tip positions and rotations for different grip types.
    /// </summary>
    public static class GripPresets
    {
        /// <summary>
        /// Apply a grip preset to automatically position finger gizmos.
        /// </summary>
        /// <param name="gripType">Type of grip to apply</param>
        /// <param name="grippedObject">Object being gripped</param>
        /// <param name="isLeftHand">True for left hand, false for right hand</param>
        /// <param name="thumbTip">Output thumb tip position</param>
        /// <param name="indexTip">Output index tip position</param>
        /// <param name="middleTip">Output middle tip position</param>
        /// <param name="ringTip">Output ring tip position</param>
        /// <param name="littleTip">Output little tip position</param>
        /// <param name="thumbRotation">Output thumb rotation</param>
        /// <param name="indexRotation">Output index rotation</param>
        /// <param name="middleRotation">Output middle rotation</param>
        /// <param name="ringRotation">Output ring rotation</param>
        /// <param name="littleRotation">Output little rotation</param>
        public static void ApplyGripPreset(
            Transform grippedObject,
            Transform handTransform,
            Animator animator,
            bool isLeftHand,
            out Vector3 thumbTip,
            out Vector3 indexTip,
            out Vector3 middleTip,
            out Vector3 ringTip,
            out Vector3 littleTip,
            out Quaternion thumbRotation,
            out Quaternion indexRotation,
            out Quaternion middleRotation,
            out Quaternion ringRotation,
            out Quaternion littleRotation)
        {
            // Initialize outputs
            thumbTip = indexTip = middleTip = ringTip = littleTip = Vector3.zero;
            thumbRotation = indexRotation = middleRotation = ringRotation = littleRotation = Quaternion.identity;

            if (grippedObject == null)
            {
                Debug.LogWarning("[GripPresets] Gripped object is null");
                return;
            }

            // Use SDF-based contact planner for mathematical surface placement
            var weights = SDFContactPlanner.CostWeights.GetDefaults();
            var targets = SDFContactPlanner.PlanContacts(grippedObject, animator, handTransform, weights);
            
            // Apply the calculated positions
            thumbTip = targets.thumb.position;
            indexTip = targets.index.position;
            middleTip = targets.middle.position;
            ringTip = targets.ring.position;
            littleTip = targets.little.position;
            
            // Apply the calculated orientations directly from the planner
            thumbRotation = targets.thumb.orientation;
            indexRotation = targets.index.orientation;
            middleRotation = targets.middle.orientation;
            ringRotation = targets.ring.orientation;
            littleRotation = targets.little.orientation;
        }

        /// <summary>
        /// Get grip description for UI display.
        /// </summary>
        public static string GetGripDescription()
        {
            return "Manual grip positioning - position finger gizmos manually in Scene view";
        }
    }
}
