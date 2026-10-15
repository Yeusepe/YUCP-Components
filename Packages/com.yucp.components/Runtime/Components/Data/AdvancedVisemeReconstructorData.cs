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
        [Tooltip("The mesh with the Oculus visemes. Leave empty to use the avatar's viseme mesh.")]
        public SkinnedMeshRenderer faceRenderer;

        [Tooltip("Custom tuning, viseme poses and rig mappings, reusable across avatars. Leave empty to use the built-in tuning.")]
        public VisemeReconstructionProfile profile;

        [Header("Behavior")]
        [Tooltip("Lower Face animates the mouth. Parameters Only writes the results to animator parameters for another controller to use.")]
        public AdvancedVisemeMouthOwnership mouthOwnership = AdvancedVisemeMouthOwnership.DriveLowerFace;

        [Tooltip("Eases each mouth shape into the next using the sounds around it, and infers tongue motion. Adds no synced parameters.")]
        public AdvancedVisemeReconstructionMode reconstructionMode = AdvancedVisemeReconstructionMode.Normal;

        [Tooltip("Automatic uses the avatar's face tracking when it has a compatible setup, otherwise speech only. The Inputs options add their own synced parameters.")]
        public AdvancedVisemeTrackingInputs trackingInputs = AdvancedVisemeTrackingInputs.Auto;

        [Tooltip("Speech corrects closures and sharp consonants that face tracking misses.")]
        public AdvancedVisemeFusionMode fusionMode = AdvancedVisemeFusionMode.PhoneticAssist;

        [Tooltip("How face tracking values are packed into synced bits. Adaptive Binary uses fewer bits for channels that are mostly on or off.")]
        public AdvancedVisemeTrackingEncoding trackingEncoding = AdvancedVisemeTrackingEncoding.AdaptiveBinary;

        [Header("Parameters")]
        [Tooltip("Prefix for the generated animator and face tracking parameters.")]
        public string parameterPrefix = DefaultParameterPrefix;

        [Tooltip("Adds a VRCFury toggle that turns face tracking on and off. It starts on and uses one synced bit.")]
        public bool createFaceTrackingToggle = true;

        [Tooltip("Where the face tracking toggle goes in the avatar menu.")]
        public string faceTrackingMenuPath = "YUCP/Face Tracking";

        [Tooltip("Prefix of the avatar's existing face tracking parameters. Leave empty to auto-detect it.")]
        public string existingTrackingPrefix = "";

        [Header("Avatar Tuning Menu")]
        [Tooltip("Adds radial sliders to the avatar menu for tuning speech while wearing the avatar. Other players see the changes.")]
        public bool createTuningMenu = true;

        [Tooltip("Where the tuning sliders go in the avatar menu.")]
        public string tuningMenuPath = "YUCP/Viseme Settings";

        [Tooltip("Keeps the slider values when changing worlds or avatars.")]
        public bool saveTuningValues = true;

        [Tooltip("Tuning sliders are always shared through one compact synced parameter. Kept for older saves.")]
        public AdvancedVisemeTuningSyncMode tuningSyncMode =
            AdvancedVisemeTuningSyncMode.CompactSynced;

        [Tooltip("Which groups of tuning sliders to add.")]
        public AdvancedVisemeTuningMenuSections tuningMenuSections =
            AdvancedVisemeTuningMenuSections.All;

        [Header("Diagnostics")]
        [Tooltip("Logs build details to the Console.")]
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
            if (settingsVersion < 3)
            {




                tuningSyncMode = AdvancedVisemeTuningSyncMode.CompactSynced;
                settingsVersion = 3;
            }
            tuningSyncMode = AdvancedVisemeTuningSyncMode.CompactSynced;
            tuningMenuPath = string.IsNullOrWhiteSpace(tuningMenuPath)
                ? "YUCP/Viseme Settings"
                : tuningMenuPath.Trim().Trim('/');
            tuningMenuSections &= AdvancedVisemeTuningMenuSections.All;
        }
    }
}
