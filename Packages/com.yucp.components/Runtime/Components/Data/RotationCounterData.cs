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
        [Header("Required Parameters")]
        [Tooltip("Float (0-1) angle parameter coming from your input source.")]
        public string angle01ParameterName = "Angle01";

        [Tooltip("Float (0-1) stick magnitude parameter.")]
        public string magnitudeParameterName = "Mag";

        [Tooltip("Int parameter that accumulates your rotation count.")]
        public string rotationIndexParameterName = "Index";

        [Tooltip("Int parameter reporting the most recent flick direction (1, -1, 0 idle).")]
        public string directionParameterName = "Direction";



        [Tooltip("Enable DebugPhase parameter driver updates for troubleshooting.")]
        public bool createDebugPhaseParameter = true;

        [Tooltip("Optional Int parameter that tracks the active phase (0 idle, 1/2 CW, 3/4 CCW, 5 cooldown).")]
        public string debugPhaseParameterName = "DebugPhase";

        [Header("Spin Flick Detection")]
        [Tooltip("Number of 360° sectors. Use 12 for 30° wide slices.")]
        [Range(4, 24)]
        public int numberOfSectors = 12;

        [Tooltip("Margin kept inside each sector to prevent rapid toggling on boundaries (hysteresis).")]
        [Range(0f, 0.05f)]
        public float sectorHysteresis = 0.02f;

        [Tooltip("Minimum stick magnitude required to track rotation changes.")]
        [Range(0f, 1f)]
        public float magnitudeThreshold = 0.5f;

        [Tooltip("Name of the animator layer that hosts the generated graph.")]
        public string layerName = "SpinFlick_New";

        [Header("Build Statistics")]
        [Tooltip("Number of sectors created in the most recent generated controller.")]
        public int generatedSectorCount = 0;

        [Tooltip("Controller generation status")]
        public bool controllerGenerated = false;
    }
}









