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
        public static readonly IReadOnlyList<AdvancedVisemeTuningControl> Controls =
            (AdvancedVisemeTuningControl[])Enum.GetValues(
                typeof(AdvancedVisemeTuningControl));

        /// <summary>
        /// A compact, terminology-free view over the same tuning parameters used
        /// by the full menu. Keeping this at eight entries lets VRChat display the
        /// complete friendly surface in one menu without adding parameters.
        /// </summary>
        public static readonly IReadOnlyList<AdvancedVisemeTuningControl> SimpleControls =
            new[]
            {
                AdvancedVisemeTuningControl.SpeechMotion,
                AdvancedVisemeTuningControl.SpeechLiveliness,
                AdvancedVisemeTuningControl.QuietMotion,
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
                case AdvancedVisemeTuningControl.SpeechMotion: return "Expression Strength";
                case AdvancedVisemeTuningControl.SpeechLiveliness: return "Speech Liveliness";
                case AdvancedVisemeTuningControl.QuietMotion: return "Quiet Speech Detail";
                case AdvancedVisemeTuningControl.SpeechSmoothness: return "Reaction Speed";
                case AdvancedVisemeTuningControl.SilenceStability: return "Pause Stability";
                case AdvancedVisemeTuningControl.ConstraintAmount: return "Pronunciation Help";
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

        public static float DefaultValue(
            VisemeReconstructionProfile profile,
            AdvancedVisemeTuningControl control)
        {
            if (profile == null)
            {
                if (control == AdvancedVisemeTuningControl.QuietMotion) return 0.55f;
                if (control == AdvancedVisemeTuningControl.SpeechLiveliness) return 0.5f;
                return IsCenteredControl(control) ? 0.5f : 1f;
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
                    return Mathf.Clamp01(profile.speechMotionStrength);
                case AdvancedVisemeTuningControl.SpeechLiveliness:
                    return Mathf.Clamp01(profile.speechLiveliness);
                case AdvancedVisemeTuningControl.AuthoredDetail:
                    return Mathf.Clamp01(profile.authoredResidualDetail);
                case AdvancedVisemeTuningControl.Coarticulation:
                    return profile.BetaCoarticulationStrength;
                case AdvancedVisemeTuningControl.RemoteTrust:
                    return Mathf.Clamp01(profile.remoteTrackingTrust);
                case AdvancedVisemeTuningControl.ContradictionFade:
                    return Mathf.Clamp01(profile.residualMismatchFade);
                case AdvancedVisemeTuningControl.ConstraintAmount:
                    return Mathf.Clamp01(profile.phoneticConstraintStrength);
                case AdvancedVisemeTuningControl.BilabialAssist:
                    return Mathf.Clamp01(profile.bilabialAssistStrength);
                case AdvancedVisemeTuningControl.LabiodentalAssist:
                    return Mathf.Clamp01(profile.labiodentalAssistStrength);
                case AdvancedVisemeTuningControl.SibilantAssist:
                    return Mathf.Clamp01(profile.sibilantAssistStrength);
                case AdvancedVisemeTuningControl.HiddenPhone:
                    return Mathf.Clamp01(profile.hiddenPhoneStrength);
                case AdvancedVisemeTuningControl.HiddenDetail:
                    return Mathf.Clamp01(profile.hiddenDetailStrength);
                case AdvancedVisemeTuningControl.TongueInference:
                    return Mathf.Clamp01(profile.tongueInferenceStrength);
                case AdvancedVisemeTuningControl.TongueOut:
                    return Mathf.Clamp01(profile.tongueOutStrength);
                case AdvancedVisemeTuningControl.TongueVertical:
                    return Mathf.Clamp01(profile.tongueYStrength);
                case AdvancedVisemeTuningControl.TongueLateral:
                    return Mathf.Clamp01(profile.tongueXStrength);
                case AdvancedVisemeTuningControl.TongueRoll:
                    return Mathf.Clamp01(profile.tongueRollStrength);
                case AdvancedVisemeTuningControl.TongueArch:
                    return Mathf.Clamp01(profile.tongueArchStrength);
                case AdvancedVisemeTuningControl.TongueShape:
                    return Mathf.Clamp01(profile.tongueShapeStrength);
                case AdvancedVisemeTuningControl.TongueTwist:
                    return Mathf.Clamp01(profile.tongueTwistStrength);
                default:
                    return 1f;
            }
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
