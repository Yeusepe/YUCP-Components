using UnityEngine;

namespace YUCP.Components
{
    /// <summary>
    /// Configures rotation counter that tracks cumulative rotations from angle input.
    /// Generates an Animator Controller with zone states to detect wraparounds and increment RotationIndex.
    /// </summary>
    [BetaWarning("This component is in BETA. Rotation counting is experimental and may require tuning.")]
    public class RotationCounterData : MonoBehaviour
    {
        [Header("Angle Input")]
        [Tooltip("Name of the angle parameter (Float, 0-1 representing 0-360 degrees). Must be set by Gesture Manager Input Emulator or other system.")]
        public string angleParameterName = "StickAngle";

        [Header("Rotation Output")]
        [Tooltip("Name of the rotation index parameter (Int) that will be incremented on each full rotation")]
        public string rotationIndexParameterName = "RotationIndex";

        [Header("Rotation Detection")]
        [Tooltip("Number of zones to divide the angle range (8 = 45° each, 16 = 22.5° each, etc.). More zones = more accurate but more states.")]
        [Range(4, 32)]
        public int numberOfZones = 8;

        [Tooltip("Threshold for detecting near-zero angle (0-1 range). Angle below this is considered near 0°.")]
        [Range(0f, 0.5f)]
        public float nearZeroThreshold = 0.1f;

        [Tooltip("Threshold for detecting near-maximum angle (0-1 range). Angle above this is considered near 360°.")]
        [Range(0.5f, 1f)]
        public float nearMaxThreshold = 0.9f;

        [Tooltip("Number of zones to traverse for one count increment. 1 = every zone boundary, up to numberOfZones = full rotation.")]
        [Range(1, 32)]
        public int sectionsPerCount = 4;

        [Tooltip("If true, clockwise rotation increments RotationIndex; otherwise decrements.")]
        public bool clockwiseIsPositive = true;

        [Tooltip("Hysteresis epsilon to stabilize arming/trigger thresholds (0-1)." )]
        [Range(0f, 0.1f)]
        public float hysteresisEpsilon = 0.005f;

        [Header("Build Statistics")]
        [Tooltip("Number of zones created in generated controller")]
        public int generatedZonesCount = 0;

        [Tooltip("Controller generation status")]
        public bool controllerGenerated = false;
    }
}


