using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    public enum AdvancedVisemeMouthOwnership
    {
        DriveLowerFace,
        OutputsOnly
    }

    public enum AdvancedVisemeTrackingInputs
    {
        Disabled,
        ReuseExisting,
        Balanced8,
        Quality12,
        Auto,
        FullTongue18
    }

    public enum AdvancedVisemeFusionMode
    {
        PhoneticAssist,
        TrackerAuthoritative
    }

    public enum AdvancedVisemeReconstructionMode
    {
        Normal,
        BetaCoarticulation
    }

    public enum AdvancedVisemeTrackingEncoding
    {
        AdaptiveBinary,
        Uniform4BitBinary,
        FullFloat
    }

    public enum AdvancedVisemeTuningSyncMode
    {
        LocalOnly,
        CompactSynced
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("YUCP/Advanced Viseme Reconstructor")]
    [HelpURL("https://github.com/Yeusepe/YUCP-Components#advanced-viseme-reconstructor")]
    [SupportBanner]
    public sealed class AdvancedVisemeReconstructorData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        public const string DefaultParameterPrefix =
            AdvancedVisemeParameterContract.DefaultAdvancedVisemePrefix;

        [Header("Face Rig")]
        [Tooltip("Renderer containing the Oculus visemes. When empty, the renderer configured on the avatar descriptor is used.")]
        public SkinnedMeshRenderer faceRenderer;

        [Tooltip("Optional reusable reconstruction and face-tracking calibration profile. Built-in defaults are used when empty.")]
        public VisemeReconstructionProfile profile;

        [Header("Behavior")]
        [Tooltip("Drive the lower-face rig, or only publish reconstructed Animator parameters for another controller to consume.")]
        public AdvancedVisemeMouthOwnership mouthOwnership = AdvancedVisemeMouthOwnership.DriveLowerFace;

        [Tooltip("Normal preserves the established observer. Beta Coarticulation uses recent viseme context to lead articulatory transitions without adding synced parameters.")]
        public AdvancedVisemeReconstructionMode reconstructionMode = AdvancedVisemeReconstructionMode.Normal;

        [Tooltip("VRCFaceTracking inputs to reuse or explicitly generate. Auto only reuses an existing compatible stream and otherwise stays speech-only.")]
        public AdvancedVisemeTrackingInputs trackingInputs = AdvancedVisemeTrackingInputs.Auto;

        [Tooltip("Phonetic Assist preserves required closures while tracking. Tracker Authoritative follows tracking without constraint projection.")]
        public AdvancedVisemeFusionMode fusionMode = AdvancedVisemeFusionMode.PhoneticAssist;

        [Tooltip("Binary modes use VRCFaceTracking bit parameters and reconstruct local floats in the FX controller. Adaptive Binary uses fewer bits on gate-like channels.")]
        public AdvancedVisemeTrackingEncoding trackingEncoding = AdvancedVisemeTrackingEncoding.AdaptiveBinary;

        [Header("Parameters")]
        [Tooltip("Prefix used for generated output and VRCFaceTracking parameters.")]
        public string parameterPrefix = DefaultParameterPrefix;

        [Tooltip("Create a VRCFury Face Tracking menu toggle. The toggle starts enabled.")]
        public bool createFaceTrackingToggle = true;

        [Tooltip("Optional menu path for the generated tracking toggle.")]
        public string faceTrackingMenuPath = "YUCP/Face Tracking";

        [Tooltip("When reusing existing VRCFaceTracking inputs, select their prefix. Leave empty to detect it by /v2/ suffix coverage.")]
        public string existingTrackingPrefix = "";

        [Header("Avatar Tuning Menu")]
        [Tooltip("Add radial sliders for live viseme tuning. They can stay local or be shared through the compact quantized transport below.")]
        public bool createTuningMenu = true;

        [Tooltip("Menu path that contains the generated Speech, Tracking, Phonetics, and Tongue slider groups.")]
        public string tuningMenuPath = "YUCP/Viseme Settings";

        [Tooltip("Remember local tuning slider values when changing worlds or avatars.")]
        public bool saveTuningValues = true;

        [Tooltip("Compact Synced shares every generated tuning slider through one quantized network bus. Local values stay full precision and can remain saved; remote avatars receive 255 evenly spaced levels using exactly 13 synced bits.")]
        public AdvancedVisemeTuningSyncMode tuningSyncMode =
            AdvancedVisemeTuningSyncMode.CompactSynced;

        [Tooltip("Choose which groups of tuning sliders are generated.")]
        public AdvancedVisemeTuningMenuSections tuningMenuSections =
            AdvancedVisemeTuningMenuSections.All;

        [Header("Diagnostics")]
        public bool verboseLogging;

        [SerializeField, HideInInspector]
        private string lastBuildSummary;

        [SerializeField, HideInInspector]
        private int settingsVersion;

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;

        public string NormalizedPrefix
        {
            get
            {
                return AdvancedVisemeParameterContract.NormalizePrefix(
                    parameterPrefix,
                    DefaultParameterPrefix);
            }
        }

        public void SetBuildSummary(string summary)
        {
            lastBuildSummary = summary ?? string.Empty;
        }

        public string GetBuildSummary() => lastBuildSummary;

        public string TuningParameterName(AdvancedVisemeTuningControl control)
        {
            return NormalizedPrefix + "/Tuning/" +
                   AdvancedVisemeTuning.ParameterSuffix(control);
        }

        private void OnValidate()
        {
            parameterPrefix = NormalizedPrefix;
            faceTrackingMenuPath = string.IsNullOrWhiteSpace(faceTrackingMenuPath)
                ? "YUCP/Face Tracking"
                : faceTrackingMenuPath.Trim().Trim('/');
            existingTrackingPrefix = (existingTrackingPrefix ?? string.Empty).Trim().TrimEnd('/');
            if (settingsVersion < 1)
            {
                createTuningMenu = true;
                saveTuningValues = true;
                tuningMenuSections = AdvancedVisemeTuningMenuSections.All;
                settingsVersion = 1;
            }
            if (settingsVersion < 2)
            {
                tuningSyncMode = AdvancedVisemeTuningSyncMode.CompactSynced;
                settingsVersion = 2;
            }
            if (!System.Enum.IsDefined(typeof(AdvancedVisemeTuningSyncMode), tuningSyncMode))
                tuningSyncMode = AdvancedVisemeTuningSyncMode.CompactSynced;
            tuningMenuPath = string.IsNullOrWhiteSpace(tuningMenuPath)
                ? "YUCP/Viseme Settings"
                : tuningMenuPath.Trim().Trim('/');
            tuningMenuSections &= AdvancedVisemeTuningMenuSections.All;
        }
    }
}
