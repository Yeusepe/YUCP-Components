using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components
{
    [Flags]
    public enum AdvancedVisemeTuningMenuSections
    {
        None = 0,
        Speech = 1 << 0,
        Tracking = 1 << 1,
        Phonetics = 1 << 2,
        Tongue = 1 << 3,
        All = Speech | Tracking | Phonetics | Tongue
    }

    public enum AdvancedVisemeTuningControl
    {
        SpeechSmoothness,
        VoiceSensitivity,
        QuietMotion,
        SpeechMotion,
        AuthoredDetail,
        Coarticulation,
        TrackingSmoothness,
        TrackingRelease,
        RemoteTrust,
        ContradictionFade,
        ConstraintAmount,
        BilabialAssist,
        LabiodentalAssist,
        SibilantAssist,
        HiddenPhone,
        HiddenDetail,
        TongueInference,
        TongueOut,
        TongueVertical,
        TongueLateral,
        TongueRoll,
        TongueArch,
        TongueShape,
        TongueTwist,
        SilenceStability,
        // Keep new controls appended so serialized enum values remain stable.
        SpeechLiveliness
    }

    /// <summary>
    /// Stable metadata shared by the Animator generator, component inspector,
    /// menu builder, and tests. Menu inputs are preferences; reconstructed
    /// visemes and articulator outputs remain read-only.
    /// </summary>
    public static class AdvancedVisemeTuning
    {
        public const int CompactSyncQuantizationMaximum = 254;
        public const float SliderDefault = 0.5f;

        public readonly struct EffectiveRange
        {
            public readonly float minimum;
            public readonly float configured;
            public readonly float maximum;

            public EffectiveRange(float minimum, float configured, float maximum)
            {
                this.minimum = minimum;
                this.configured = configured;
                this.maximum = maximum;
            }

            public float Evaluate(float slider)
            {
                slider = Mathf.Clamp01(slider);
                return slider <= SliderDefault
                    ? Mathf.Lerp(minimum, configured, slider / SliderDefault)
                    : Mathf.Lerp(configured, maximum,
                        (slider - SliderDefault) / SliderDefault);
            }
        }

        public static readonly IReadOnlyList<AdvancedVisemeTuningControl> Controls =
            (AdvancedVisemeTuningControl[])Enum.GetValues(
                typeof(AdvancedVisemeTuningControl));

        /// <summary>
        /// A compact, terminology-free view over the same tuning parameters used
        /// by the full menu. These sit at the ROOT of the tuning menu, so the
        /// count must leave one slot for the Advanced submenu inside VRChat's
        /// eight-control limit — seven entries, not eight.
        /// QuietMotion was removed: it only acts through the voice envelope,
        /// which the lookahead-FIR mouth path deliberately does not apply, so
        /// the slider moved nothing on the lower face.
        /// </summary>
        public static readonly IReadOnlyList<AdvancedVisemeTuningControl> SimpleControls =
            new[]
            {
                AdvancedVisemeTuningControl.SpeechMotion,
                AdvancedVisemeTuningControl.SpeechLiveliness,
                AdvancedVisemeTuningControl.SpeechSmoothness,
                AdvancedVisemeTuningControl.SilenceStability,
                AdvancedVisemeTuningControl.ConstraintAmount,
                AdvancedVisemeTuningControl.ContradictionFade,
                AdvancedVisemeTuningControl.TongueInference
            };

        public static AdvancedVisemeTuningMenuSections Section(
            AdvancedVisemeTuningControl control)
        {
            switch (control)
            {
                case AdvancedVisemeTuningControl.SpeechSmoothness:
                case AdvancedVisemeTuningControl.VoiceSensitivity:
                case AdvancedVisemeTuningControl.QuietMotion:
                case AdvancedVisemeTuningControl.SpeechMotion:
                case AdvancedVisemeTuningControl.AuthoredDetail:
                case AdvancedVisemeTuningControl.Coarticulation:
                case AdvancedVisemeTuningControl.SilenceStability:
                case AdvancedVisemeTuningControl.SpeechLiveliness:
                    return AdvancedVisemeTuningMenuSections.Speech;
                case AdvancedVisemeTuningControl.TrackingSmoothness:
                case AdvancedVisemeTuningControl.TrackingRelease:
                case AdvancedVisemeTuningControl.RemoteTrust:
                case AdvancedVisemeTuningControl.ContradictionFade:
                    return AdvancedVisemeTuningMenuSections.Tracking;
                case AdvancedVisemeTuningControl.ConstraintAmount:
                case AdvancedVisemeTuningControl.BilabialAssist:
                case AdvancedVisemeTuningControl.LabiodentalAssist:
                case AdvancedVisemeTuningControl.SibilantAssist:
                case AdvancedVisemeTuningControl.HiddenPhone:
                case AdvancedVisemeTuningControl.HiddenDetail:
                    return AdvancedVisemeTuningMenuSections.Phonetics;
                default:
                    return AdvancedVisemeTuningMenuSections.Tongue;
            }
        }

        public static string SectionLabel(AdvancedVisemeTuningMenuSections section)
        {
            switch (section)
            {
                case AdvancedVisemeTuningMenuSections.Speech: return "Speech";
                case AdvancedVisemeTuningMenuSections.Tracking: return "Tracking";
                case AdvancedVisemeTuningMenuSections.Phonetics: return "Phonetics";
                case AdvancedVisemeTuningMenuSections.Tongue: return "Tongue";
                default: return "Tuning";
            }
        }

        public static string Label(AdvancedVisemeTuningControl control)
        {
            switch (control)
            {
                case AdvancedVisemeTuningControl.SpeechSmoothness: return "Speech Smoothness";
                case AdvancedVisemeTuningControl.VoiceSensitivity: return "Voice Sensitivity";
                case AdvancedVisemeTuningControl.QuietMotion: return "Quiet Motion";
                case AdvancedVisemeTuningControl.SpeechMotion: return "Speech Motion";
                case AdvancedVisemeTuningControl.AuthoredDetail: return "Authored Detail";
                case AdvancedVisemeTuningControl.Coarticulation: return "Coarticulation";
                case AdvancedVisemeTuningControl.SilenceStability: return "Silence Stability";
                case AdvancedVisemeTuningControl.SpeechLiveliness: return "Speech Lead";
                case AdvancedVisemeTuningControl.TrackingSmoothness: return "Tracking Smoothness";
                case AdvancedVisemeTuningControl.TrackingRelease: return "Tracker Release";
                case AdvancedVisemeTuningControl.RemoteTrust: return "Remote Trust";
                case AdvancedVisemeTuningControl.ContradictionFade: return "Tracked Surface Yield";
                case AdvancedVisemeTuningControl.ConstraintAmount: return "Constraint Amount";
                case AdvancedVisemeTuningControl.BilabialAssist: return "PP Closure";
                case AdvancedVisemeTuningControl.LabiodentalAssist: return "FF Bite";
                case AdvancedVisemeTuningControl.SibilantAssist: return "Sibilants";
                case AdvancedVisemeTuningControl.HiddenPhone: return "M/N Recognition";
                case AdvancedVisemeTuningControl.HiddenDetail: return "Hidden Detail";
                case AdvancedVisemeTuningControl.TongueInference: return "Inference";
                case AdvancedVisemeTuningControl.TongueOut: return "Out";
                case AdvancedVisemeTuningControl.TongueVertical: return "Vertical";
                case AdvancedVisemeTuningControl.TongueLateral: return "Lateral";
                case AdvancedVisemeTuningControl.TongueRoll: return "Roll";
                case AdvancedVisemeTuningControl.TongueArch: return "Arch";
                case AdvancedVisemeTuningControl.TongueShape: return "Shape";
                case AdvancedVisemeTuningControl.TongueTwist: return "Twist";
                default: return control.ToString();
            }
        }

        public static string SimpleLabel(AdvancedVisemeTuningControl control)
        {
            switch (control)
            {
                case AdvancedVisemeTuningControl.SpeechMotion: return "Exaggeration";
                case AdvancedVisemeTuningControl.SpeechLiveliness: return "Snappiness";
                case AdvancedVisemeTuningControl.QuietMotion: return "Soft Speech";
                case AdvancedVisemeTuningControl.SpeechSmoothness: return "Reaction Speed";
                case AdvancedVisemeTuningControl.SilenceStability: return "Smooth Pauses";
                case AdvancedVisemeTuningControl.ConstraintAmount: return "Clear Consonants";
                case AdvancedVisemeTuningControl.ContradictionFade: return "Follow My Face";
                case AdvancedVisemeTuningControl.TongueInference: return "Tongue Motion";
                case AdvancedVisemeTuningControl.Coarticulation: return "Natural Transitions";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(control), control,
                        "The control is not part of the simple viseme menu.");
            }
        }

        public static string ParameterSuffix(AdvancedVisemeTuningControl control)
        {
            return control.ToString();
        }

        public static string CompactSyncDataParameter(string prefix)
        {
            return NormalizePrefix(prefix) + "/_TuningSync/Data";
        }

        public static string CompactSyncIndexParameter(string prefix, int bit)
        {
            if (bit < 0) throw new ArgumentOutOfRangeException(nameof(bit));
            return NormalizePrefix(prefix) + "/_TuningSync/Index" + bit;
        }

        public static string CompactSyncFocusParameter(string prefix)
        {
            return NormalizePrefix(prefix) + "/_TuningSync/Focused";
        }

        public static int CompactSyncIndexBits(int controlCount)
        {
            if (controlCount <= 0) return 0;
            var requiredValues = controlCount + 1; // Channel zero is never applied.
            var bits = 0;
            for (var capacity = 1; capacity < requiredValues; capacity <<= 1) bits++;
            return bits;
        }

        public static int CompactSyncBits(int controlCount)
        {
            return controlCount <= 0 ? 0 : 8 + CompactSyncIndexBits(controlCount);
        }

        /// <summary>
        /// The on-wire channel id is tied to the serialized enum order rather
        /// than the controls that happen to be relevant to one platform's rig.
        /// This keeps PC and Quest carrier packets semantically identical even
        /// when one mesh cannot expose one of the optional sliders.
        /// </summary>
        public static int CompactSyncChannelId(AdvancedVisemeTuningControl control)
        {
            for (var index = 0; index < Controls.Count; index++)
                if (Controls[index] == control) return index + 1;
            throw new ArgumentOutOfRangeException(nameof(control), control,
                "The tuning control has no compact sync channel.");
        }

        public static int CompactSyncTransportIndexBits =>
            CompactSyncIndexBits(Controls.Count);

        public static int CompactSyncTransportBits(int activeControlCount)
        {
            return activeControlCount <= 0
                ? 0
                : 8 + CompactSyncTransportIndexBits;
        }

        public static int QuantizeCompactSync(float value)
        {
            // Match Avatar Parameter Driver exactly: the sender maps into
            // [0.5, 254.5] and the Float-to-Int copy truncates the result.
            // Floor(x + 0.5) is round-half-up, unlike Mathf.RoundToInt's
            // round-half-to-even behavior at exact code boundaries.
            return Mathf.FloorToInt(
                Mathf.Clamp01(value) * CompactSyncQuantizationMaximum + 0.5f);
        }

        public static float DequantizeCompactSync(int value)
        {
            return Mathf.Clamp(value, 0, CompactSyncQuantizationMaximum) /
                   (float)CompactSyncQuantizationMaximum;
        }

        private static string NormalizePrefix(string prefix)
        {
            prefix = (prefix ?? string.Empty).Trim().Trim('/');
            return string.IsNullOrEmpty(prefix)
                ? AdvancedVisemeParameterContract.DefaultAdvancedVisemePrefix
                : prefix;
        }

        public static float DefaultValue(
            VisemeReconstructionProfile profile,
            AdvancedVisemeTuningControl control)
        {
            // Public tuning parameters are preferences, not physical gains.
            // Keeping their neutral point identical lets every radial start in
            // the middle, serialize predictably, and use the same compact wire
            // quantizer. The profile's authored value is recovered by the
            // internal centered response below.
            return SliderDefault;
        }

        public static float ConfiguredValue(
            VisemeReconstructionProfile profile,
            AdvancedVisemeTuningControl control)
        {
            if (profile == null)
            {
                if (control == AdvancedVisemeTuningControl.QuietMotion) return 0.55f;
                if (control == AdvancedVisemeTuningControl.SpeechLiveliness) return 0f;
                return IsCenteredControl(control) ? SliderDefault : 1f;
            }

            switch (control)
            {
                case AdvancedVisemeTuningControl.SpeechSmoothness:
                case AdvancedVisemeTuningControl.VoiceSensitivity:
                case AdvancedVisemeTuningControl.TrackingSmoothness:
                case AdvancedVisemeTuningControl.TrackingRelease:
                case AdvancedVisemeTuningControl.SilenceStability:
                    return 0.5f;
                case AdvancedVisemeTuningControl.QuietMotion:
                    return Mathf.Clamp01(profile.quietSpeechFloor);
                case AdvancedVisemeTuningControl.SpeechMotion:
                    return Mathf.Clamp(profile.speechMotionStrength, 0f, 2f);
                case AdvancedVisemeTuningControl.SpeechLiveliness:
                    return Mathf.Clamp(profile.speechLiveliness, -1f, 1f);
                case AdvancedVisemeTuningControl.AuthoredDetail:
                    return Mathf.Clamp(profile.authoredResidualDetail, 0f, 1.5f);
                case AdvancedVisemeTuningControl.Coarticulation:
                    return profile.BetaCoarticulationStrength;
                case AdvancedVisemeTuningControl.RemoteTrust:
                    return Mathf.Clamp01(profile.remoteTrackingTrust);
                case AdvancedVisemeTuningControl.ContradictionFade:
                    return Mathf.Clamp(profile.residualMismatchFade, 0f, 2f);
                case AdvancedVisemeTuningControl.ConstraintAmount:
                    return Mathf.Clamp(profile.phoneticConstraintStrength, 0f, 2f);
                case AdvancedVisemeTuningControl.BilabialAssist:
                    return Mathf.Clamp(profile.bilabialAssistStrength, 0f, 2f);
                case AdvancedVisemeTuningControl.LabiodentalAssist:
                    return Mathf.Clamp(profile.labiodentalAssistStrength, 0f, 2f);
                case AdvancedVisemeTuningControl.SibilantAssist:
                    return Mathf.Clamp(profile.sibilantAssistStrength, 0f, 2f);
                case AdvancedVisemeTuningControl.HiddenPhone:
                    return Mathf.Clamp(profile.hiddenPhoneStrength, 0f, 1.5f);
                case AdvancedVisemeTuningControl.HiddenDetail:
                    return Mathf.Clamp(profile.hiddenDetailStrength, 0f, 1.5f);
                case AdvancedVisemeTuningControl.TongueInference:
                    return Mathf.Clamp(profile.tongueInferenceStrength, 0f, 2f);
                case AdvancedVisemeTuningControl.TongueOut:
                    return Mathf.Clamp(profile.tongueOutStrength, 0f, 2f);
                case AdvancedVisemeTuningControl.TongueVertical:
                    return Mathf.Clamp(profile.tongueYStrength, 0f, 2f);
                case AdvancedVisemeTuningControl.TongueLateral:
                    return Mathf.Clamp(profile.tongueXStrength, 0f, 2f);
                case AdvancedVisemeTuningControl.TongueRoll:
                    return Mathf.Clamp(profile.tongueRollStrength, 0f, 2f);
                case AdvancedVisemeTuningControl.TongueArch:
                    return Mathf.Clamp(profile.tongueArchStrength, 0f, 2f);
                case AdvancedVisemeTuningControl.TongueShape:
                    return Mathf.Clamp(profile.tongueShapeStrength, 0f, 2f);
                case AdvancedVisemeTuningControl.TongueTwist:
                    return Mathf.Clamp(profile.tongueTwistStrength, 0f, 2f);
                default:
                    return 1f;
            }
        }

        public static EffectiveRange Range(
            VisemeReconstructionProfile profile,
            AdvancedVisemeTuningControl control)
        {
            var configured = ConfiguredValue(profile, control);
            switch (control)
            {
                // These are normalized selectors whose consumers provide the
                // strongly separated physical endpoints (response time,
                // sensitivity, acquisition, release, and hangover behavior).
                case AdvancedVisemeTuningControl.SpeechSmoothness:
                case AdvancedVisemeTuningControl.VoiceSensitivity:
                case AdvancedVisemeTuningControl.TrackingSmoothness:
                case AdvancedVisemeTuningControl.TrackingRelease:
                case AdvancedVisemeTuningControl.SilenceStability:
                    return new EffectiveRange(0f, SliderDefault, 1f);

                case AdvancedVisemeTuningControl.QuietMotion:
                    return new EffectiveRange(0f, configured, 1f);
                case AdvancedVisemeTuningControl.SpeechMotion:
                    // Above one boosts quiet speech into the authored range; the
                    // final speech envelope is saturated, so poses never exceed
                    // their authored animation capability.
                    return new EffectiveRange(0f, configured, 2f);
                case AdvancedVisemeTuningControl.SpeechLiveliness:
                    // Negative values select the calmer persistent stage; zero
                    // is the authored behavior and positive values add snap.
                    return new EffectiveRange(-1f, configured, 1f);
                case AdvancedVisemeTuningControl.AuthoredDetail:
                case AdvancedVisemeTuningControl.HiddenDetail:
                    return new EffectiveRange(0f, configured, 1.5f);
                case AdvancedVisemeTuningControl.Coarticulation:
                case AdvancedVisemeTuningControl.HiddenPhone:
                    return new EffectiveRange(0f, configured, 1.5f);
                case AdvancedVisemeTuningControl.ConstraintAmount:
                case AdvancedVisemeTuningControl.BilabialAssist:
                case AdvancedVisemeTuningControl.LabiodentalAssist:
                case AdvancedVisemeTuningControl.SibilantAssist:
                case AdvancedVisemeTuningControl.TongueInference:
                case AdvancedVisemeTuningControl.ContradictionFade:
                    return new EffectiveRange(0f, configured, 2f);
                case AdvancedVisemeTuningControl.TongueOut:
                case AdvancedVisemeTuningControl.TongueVertical:
                case AdvancedVisemeTuningControl.TongueLateral:
                case AdvancedVisemeTuningControl.TongueRoll:
                case AdvancedVisemeTuningControl.TongueArch:
                case AdvancedVisemeTuningControl.TongueShape:
                case AdvancedVisemeTuningControl.TongueTwist:
                    return new EffectiveRange(0f, configured, 2f);
                case AdvancedVisemeTuningControl.RemoteTrust:
                    return new EffectiveRange(0f, configured, 1f);
                default:
                    return new EffectiveRange(0f, configured, 1f);
            }
        }

        public static float Evaluate(
            VisemeReconstructionProfile profile,
            AdvancedVisemeTuningControl control,
            float slider)
        {
            return Range(profile, control).Evaluate(slider);
        }

        public static bool IsCenteredControl(AdvancedVisemeTuningControl control)
        {
            return control == AdvancedVisemeTuningControl.SpeechSmoothness ||
                   control == AdvancedVisemeTuningControl.VoiceSensitivity ||
                   control == AdvancedVisemeTuningControl.TrackingSmoothness ||
                   control == AdvancedVisemeTuningControl.TrackingRelease ||
                   control == AdvancedVisemeTuningControl.SilenceStability;
        }
    }
}
