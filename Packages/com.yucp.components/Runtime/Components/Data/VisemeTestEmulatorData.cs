using UnityEngine;

namespace YUCP.Components
{
    public enum VisemeTestInput
    {
        Microphone,
        Manual
    }

    public enum VisemeTestAnalysisBackend
    {
        Auto,
        OculusLipSync,
        BuiltIn
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("YUCP/Viseme Test Emulator")]
    [HelpURL("https://creators.vrchat.com/avatars/creating-your-first-avatar/#lip-sync-mode")]
    [SupportBanner]
    public sealed class VisemeTestEmulatorData : MonoBehaviour
    {
        [Tooltip("Use live audio or select visemes by hand.")]
        public VisemeTestInput input = VisemeTestInput.Microphone;

        [Tooltip("The input device used for live preview. Leave empty to use the system default.")]
        public string microphoneDevice = string.Empty;

        [Tooltip("Auto uses the Oculus LipSync Unity plugin when installed, then falls back to YUCP's local analyzer.")]
        public VisemeTestAnalysisBackend analysisBackend = VisemeTestAnalysisBackend.Auto;

        [Tooltip("Automatically boosts quiet speech before analysis. Disable this to leave low-level input at its original volume.")]
        public bool automaticGain = true;

        [Range(0.1f, 5f)]
        [Tooltip("Input gain applied before analysis.")]
        public float microphoneGain = 1f;

        [Range(0f, 0.2f)]
        [Tooltip("Audio below this level is treated as silence.")]
        public float noiseGate = 0.012f;

        [Range(0f, 14f)]
        [Tooltip("Viseme used while Manual input is selected.")]
        public int manualViseme;

        [Range(0f, 1f)]
        [Tooltip("Voice value used while Manual input is selected.")]
        public float manualVoice = 1f;

        [Tooltip("Write Viseme and Voice into every targeted Gesture Manager when it is active.")]
        public bool driveGestureManager = true;

        [Tooltip("Also write the built-in Viseme and Voice parameters on the avatar Animator.")]
        public bool driveAnimator = true;

        [Tooltip("Start microphone/manual emulation automatically when Unity enters Play Mode.")]
        public bool startWithPlayMode = true;

        private void OnValidate()
        {
            microphoneGain = Mathf.Clamp(microphoneGain, 0.1f, 5f);
            noiseGate = Mathf.Clamp(noiseGate, 0f, 0.2f);
            manualViseme = Mathf.Clamp(manualViseme, 0, 14);
            manualVoice = Mathf.Clamp01(manualVoice);
        }

        private void Awake()
        {
#if !UNITY_EDITOR
            Destroy(this);
#endif
        }
    }
}
