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
        [Tooltip("Microphone listens to your voice. Manual shows the viseme you choose.")]
        public VisemeTestInput input = VisemeTestInput.Microphone;

        [Tooltip("The microphone the preview listens to.")]
        public string microphoneDevice = string.Empty;

        [Tooltip("Auto uses Oculus LipSync, like VRChat, when it's installed, and YUCP's approximate classifier otherwise.")]
        public VisemeTestAnalysisBackend analysisBackend = VisemeTestAnalysisBackend.Auto;

        [Tooltip("Boosts quiet speech before analysis.")]
        public bool automaticGain = true;

        [Range(0.1f, 5f)]
        [Tooltip("Gain applied to the microphone before analysis.")]
        public float microphoneGain = 1f;

        [Range(0f, 0.2f)]
        [Tooltip("Input below this level counts as silence.")]
        public float noiseGate = 0.012f;

        [Range(0f, 14f)]
        [Tooltip("The viseme shown while Source is Manual.")]
        public int manualViseme;

        [Range(0f, 1f)]
        [Tooltip("The voice level used while Source is Manual.")]
        public float manualVoice = 1f;

        [Tooltip("Writes Viseme and Voice to each active Gesture Manager that targets the avatar.")]
        public bool driveGestureManager = true;

        [Tooltip("Also writes the built-in Viseme and Voice parameters on the avatar's Animator.")]
        public bool driveAnimator = true;

        [Tooltip("Starts the preview when you enter Play Mode.")]
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

#if UNITY_EDITOR
        public static event System.Action<VisemeTestEmulatorData> Enabled;

        private void OnEnable() => Enabled?.Invoke(this);
#endif
    }
}
