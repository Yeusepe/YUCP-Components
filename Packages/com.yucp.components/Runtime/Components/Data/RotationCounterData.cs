using UnityEngine;

namespace YUCP.Components
{
    /// <summary>
    /// Configures rotation counter that tracks sector-based rotations and detects cardinal direction flicks.
    /// Generates an Animator Controller with sector-based rotation detection (outputs RotationStep: -1, 0, +1)
    /// and flick detection FSM (outputs FlickEvent: 0=NONE, 1=RIGHT, 2=UP, 3=LEFT, 4=DOWN).
    /// </summary>
    [BetaWarning("This component is in BETA. Rotation counting is experimental and may require tuning.")]
    public class RotationCounterData : MonoBehaviour
    {
        [Header("Input Parameters")]
        [Tooltip("Float parameter for joystick X coordinate.")]
        public string xParameterName = "X";

        [Tooltip("Float parameter for joystick Y coordinate.")]
        public string yParameterName = "Y";

        [Tooltip("Float parameter for angle in degrees (0-360).")]
        public string angleParameterName = "Angle";

        [Header("Output Parameters")]
        [Tooltip("Int parameter that outputs rotation step: -1, 0, or +1.")]
        public string rotationStepParameterName = "RotationStep";

        [Tooltip("Int parameter that outputs flick event: 0=NONE, 1=RIGHT, 2=UP, 3=LEFT, 4=DOWN.")]
        public string flickEventParameterName = "FlickEvent";

        [Tooltip("Enable DebugPhase parameter driver updates for troubleshooting.")]
        public bool createDebugPhaseParameter = true;

        [Tooltip("Optional Int parameter that tracks the active phase (0 idle, 1/2 CW, 3/4 CCW, 5 cooldown).")]
        public string debugPhaseParameterName = "DebugPhase";

        [Header("Rotation Detection")]
        [Tooltip("Number of 360° sectors. Use 8 for 45° wide slices (default), or 12 for 30° slices.")]
        [Range(4, 24)]
        public int numberOfSectors = 12;

        [Header("Flick Detection")]
        [Tooltip("Inner deadzone radius. Flicks won't start from within this radius.")]
        [Range(0f, 1f)]
        public float innerDeadzone = 0.2f;

        [Tooltip("Minimum radius required to start a flick detection.")]
        [Range(0f, 1f)]
        public float flickMinRadius = 0.7f;

        [Tooltip("Release radius threshold. Flick is detected when stick returns below this radius.")]
        [Range(0f, 1f)]
        public float releaseRadius = 0.3f;

        [Tooltip("Angle tolerance in degrees for cardinal direction detection (±tolerance around 0°, 90°, 180°, 270°).")]
        [Range(0f, 90f)]
        public float angleToleranceDeg = 30f;

        [Tooltip("Maximum frames allowed for flick detection before timeout.")]
        [Range(1, 30)]
        public int maxFlickFrames = 6;

        [Tooltip("Name of the animator layer that hosts the generated graph.")]
        public string layerName = "SpinFlick_New";

        [Header("Build Statistics")]
        [Tooltip("Number of sectors created in the most recent generated controller.")]
        public int generatedSectorCount = 0;

        [Tooltip("Controller generation status")]
        public bool controllerGenerated = false;
    }
}









