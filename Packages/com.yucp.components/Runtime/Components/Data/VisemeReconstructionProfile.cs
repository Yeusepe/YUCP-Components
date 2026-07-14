using System;
using UnityEngine;

namespace YUCP.Components
{
    public enum AdvancedVisemeArticulator
    {
        JawOpen,
        LipClose,
        MouthOpen,
        LipFunnel,
        LipPucker,
        LipSuck,
        SmileSad,
        LipBite,
        TongueOut,
        JawX,
        JawZ,
        MouthX,
        TongueY,
        TongueX,
        TongueRoll,
        TongueArchY,
        TongueShape,
        TongueTwistRight,
        TongueTwistLeft
    }

    [Serializable]
    public sealed class VisemeArticulationPose
    {
        public string name;
        public AnimationClip animationOverride;
        [Range(0f, 1f)] public float jawOpen;
        [Range(0f, 1f)] public float lipClose;
        [Range(0f, 1f)] public float mouthOpen;
        [Range(0f, 1f)] public float lipFunnel;
        [Range(0f, 1f)] public float lipPucker;
        [Range(0f, 1f)] public float lipSuck;
        [Range(-1f, 1f)] public float smileSad;
        [Range(0f, 1f)] public float lipBite;
        [Range(0f, 1f)] public float tongueOut;
        [Range(-1f, 1f)] public float jawX;
        [Range(-1f, 1f)] public float jawZ;
        [Range(-1f, 1f)] public float mouthX;
        [Range(-1f, 1f)] public float tongueY;
        [Range(-1f, 1f)] public float tongueX;
        [Range(0f, 1f)] public float tongueRoll;
        [Range(-1f, 1f)] public float tongueArchY;
        [Range(-1f, 1f)] public float tongueShape;
        [Range(0f, 1f)] public float tongueTwistRight;
        [Range(0f, 1f)] public float tongueTwistLeft;

        public float Get(AdvancedVisemeArticulator articulator)
        {
            switch (articulator)
            {
                case AdvancedVisemeArticulator.JawOpen: return jawOpen;
                case AdvancedVisemeArticulator.LipClose: return lipClose;
                case AdvancedVisemeArticulator.MouthOpen: return mouthOpen;
                case AdvancedVisemeArticulator.LipFunnel: return lipFunnel;
                case AdvancedVisemeArticulator.LipPucker: return lipPucker;
                case AdvancedVisemeArticulator.LipSuck: return lipSuck;
                case AdvancedVisemeArticulator.SmileSad: return smileSad;
                case AdvancedVisemeArticulator.LipBite: return lipBite;
                case AdvancedVisemeArticulator.TongueOut: return tongueOut;
                case AdvancedVisemeArticulator.JawX: return jawX;
                case AdvancedVisemeArticulator.JawZ: return jawZ;
                case AdvancedVisemeArticulator.MouthX: return mouthX;
                case AdvancedVisemeArticulator.TongueY: return tongueY;
                case AdvancedVisemeArticulator.TongueX: return tongueX;
                case AdvancedVisemeArticulator.TongueRoll: return tongueRoll;
                case AdvancedVisemeArticulator.TongueArchY: return tongueArchY;
                case AdvancedVisemeArticulator.TongueShape: return tongueShape;
                case AdvancedVisemeArticulator.TongueTwistRight: return tongueTwistRight;
                case AdvancedVisemeArticulator.TongueTwistLeft: return tongueTwistLeft;
                default: return 0f;
            }
        }
    }

    [Serializable]
    public sealed class ArticulatorRigBinding
    {
        public AdvancedVisemeArticulator articulator;
        [Tooltip("Unified Expressions v2 suffix, without the prefix or /v2/ portion.")]
        public string trackingParameter;
        [Tooltip("Blendshape driven by this articulator. Common ARKit and Unified Expressions aliases are auto-detected when empty.")]
        public string blendShapeName;
        [Tooltip("Optional pose animation used when no blendshape binding is available.")]
        public AnimationClip animationOverride;
        [Tooltip("Optional negative pose for signed channels such as SmileSad, JawX, JawZ, MouthX, and TongueY.")]
        public AnimationClip negativeAnimationOverride;
        public float trackingScale = 1f;
        public float trackingOffset;
        [Range(0f, 1f)] public float localReliability = 1f;
        [Range(0f, 1f)] public float remoteReliability = 0.85f;
    }

    [CreateAssetMenu(fileName = "Viseme Reconstruction Profile", menuName = "YUCP/Viseme Reconstruction Profile")]
    public sealed class VisemeReconstructionProfile : ScriptableObject
    {
        public const int VisemeCount = 15;
        private const int CurrentDefaultsVersion = 8;
        public static readonly string[] VisemeNames =
        {
            "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "I", "O", "U"
        };

        [Header("Observer")]
        [Range(0.006f, 0.12f)] public float visemeResponseSeconds = 0.024f;
        [Tooltip("Release time for accumulated speech history after VRChat reports sil. Sustained speech earns more protection from brief gaps; Voice alone cannot extend it, and active viseme timing is unchanged.")]
        [Range(0.04f, 0.4f)] public float speechHangoverSeconds = 0.16f;
        [Range(0.006f, 0.08f)] public float localTrackingResponseSeconds = 0.018f;
        [Range(0.015f, 0.2f)] public float remoteTrackingResponseSeconds = 0.065f;
        [Range(0.005f, 0.1f)] public float trackingAcquireResponseSeconds = 0.035f;
        [Range(0.02f, 0.5f)] public float trackingBlendResponseSeconds = 0.12f;
        [Range(0f, 1f)] public float quietSpeechFloor = 0.55f;
        [Range(0f, 1f)] public float voiceNoiseFloor = 0.05f;
        [Range(0.01f, 1f)] public float voiceFullScale = 0.25f;

        [SerializeField, Range(0f, 1f)]
        private float betaCoarticulationStrength = 1f;

        [Header("Style Strengths")]
        [Range(0f, 1f)] public float speechMotionStrength = 1f;
        [Tooltip("How strongly speech-only rendering follows the fast observer. This fades out continuously as face tracking becomes active.")]
        [Range(0f, 1f)] public float speechLiveliness = 0.5f;
        [Range(0f, 1f)] public float authoredResidualDetail = 1f;
        [Range(0f, 1f)] public float remoteTrackingTrust = 1f;
        [Range(0f, 1f)] public float phoneticConstraintStrength = 1f;
        [Range(0f, 1f)] public float bilabialAssistStrength = 1f;
        [Range(0f, 1f)] public float labiodentalAssistStrength = 1f;
        [Range(0f, 1f)] public float sibilantAssistStrength = 1f;
        [Range(0f, 1f)] public float hiddenPhoneStrength = 1f;
        [Range(0f, 1f)] public float hiddenDetailStrength = 1f;
        [Range(0f, 1f)] public float tongueInferenceStrength = 1f;

        [Header("Tongue Axis Strengths")]
        [Range(0f, 1f)] public float tongueOutStrength = 1f;
        [Range(0f, 1f)] public float tongueYStrength = 1f;
        [Range(0f, 1f)] public float tongueXStrength = 1f;
        [Range(0f, 1f)] public float tongueRollStrength = 1f;
        [Range(0f, 1f)] public float tongueArchStrength = 1f;
        [Range(0f, 1f)] public float tongueShapeStrength = 1f;
        [Range(0f, 1f)] public float tongueTwistStrength = 1f;

        [Header("Phonetic Constraints")]
        [Range(0f, 1f)] public float bilabialClosure = 0.9f;
        [Range(0f, 1f)] public float labiodentalBite = 0.85f;
        [Range(0f, 1f)] public float sibilantJawMaximum = 0.22f;

        [Header("Complementary Fusion")]
        [Tooltip("Legacy compatibility value. Measured vowel coordinates are now authoritative; vowel identity is retained only in unobserved articulators.")]
        [Range(0f, 0.75f)] public float vowelIdentityRetention;
        [Tooltip("How strongly measured face-tracking axes own matching authored surface motion. Unmeasured tongue, teeth, and mouth-interior detail is preserved.")]
        [Range(0f, 1f)] public float residualMismatchFade = 1f;

        [Header("Viseme Poses")]
        public VisemeArticulationPose[] visemePoses = new VisemeArticulationPose[VisemeCount];

        [Header("Rig Bindings")]
        public ArticulatorRigBinding[] articulatorBindings = Array.Empty<ArticulatorRigBinding>();

        [Header("Calibration Diagnostics")]
        [SerializeField, HideInInspector] private float lastReconstructionRms;
        [SerializeField, HideInInspector] private float lastReconstructionMaximum;
        [SerializeField, HideInInspector] private int defaultsVersion;

        public float LastReconstructionRms => lastReconstructionRms;
        public float LastReconstructionMaximum => lastReconstructionMaximum;
        public float BetaCoarticulationStrength => Mathf.Clamp01(betaCoarticulationStrength);

        public void SetDiagnostics(float rms, float maximum)
        {
            lastReconstructionRms = Mathf.Max(0f, rms);
            lastReconstructionMaximum = Mathf.Max(0f, maximum);
        }

        public ArticulatorRigBinding FindBinding(AdvancedVisemeArticulator articulator)
        {
            EnsureDefaults();
            foreach (var binding in articulatorBindings)
            {
                if (binding != null && binding.articulator == articulator) return binding;
            }
            return null;
        }

        public void EnsureDefaults()
        {
            if (visemePoses == null || visemePoses.Length != VisemeCount)
            {
                var previous = visemePoses;
                visemePoses = new VisemeArticulationPose[VisemeCount];
                if (previous != null) Array.Copy(previous, visemePoses, Mathf.Min(previous.Length, VisemeCount));
            }

            for (var i = 0; i < VisemeCount; i++)
            {
                if (visemePoses[i] == null) visemePoses[i] = CreateDefaultPose(i);
                visemePoses[i].name = VisemeNames[i];
            }

            if (articulatorBindings == null)
            {
                articulatorBindings = defaultsVersion == 0
                    ? CreateDefaultBindings()
                    : Array.Empty<ArticulatorRigBinding>();
            }
            else if (articulatorBindings.Length == 0 && defaultsVersion == 0)
            {
                articulatorBindings = CreateDefaultBindings();
            }
            else if (articulatorBindings.Length > 0 && defaultsVersion < 2)
            {
                // Version 2 introduced the extended tongue channels. Add only those new
                // bindings once; repeatedly restoring every default binding would make it
                // impossible for a profile author to intentionally remove a channel.
                var bindings = new System.Collections.Generic.List<ArticulatorRigBinding>(articulatorBindings);
                foreach (var fallback in CreateDefaultBindings())
                {
                    if (!IsExtendedTongue(fallback.articulator) ||
                        bindings.Exists(binding => binding != null && binding.articulator == fallback.articulator))
                        continue;
                    bindings.Add(fallback);
                }
                articulatorBindings = bindings.ToArray();

            }

            if (defaultsVersion < 2)
            {
                for (var i = 0; i < VisemeCount; i++)
                {
                    var defaults = CreateDefaultPose(i);
                    visemePoses[i].tongueX = defaults.tongueX;
                    visemePoses[i].tongueRoll = defaults.tongueRoll;
                    visemePoses[i].tongueArchY = defaults.tongueArchY;
                    visemePoses[i].tongueShape = defaults.tongueShape;
                    visemePoses[i].tongueTwistRight = defaults.tongueTwistRight;
                    visemePoses[i].tongueTwistLeft = defaults.tongueTwistLeft;
                }
            }

            if (defaultsVersion < 3) betaCoarticulationStrength = 1f;
            if (defaultsVersion < 4)
            {
                // Upgrade untouched historical defaults without overwriting an
                // explicitly calibrated reliability. Healthy local observations
                // must be able to reach their exact measured endpoint.
                foreach (var binding in articulatorBindings)
                {
                    if (binding == null) continue;
                    if (Mathf.Approximately(binding.localReliability, 0.95f))
                        binding.localReliability = 1f;
                    if (Mathf.Approximately(binding.remoteReliability, 0.75f))
                        binding.remoteReliability = 0.85f;
                }
                if (Mathf.Approximately(vowelIdentityRetention, 0.25f))
                    vowelIdentityRetention = 0f;
            }
            if (defaultsVersion < 5 && Mathf.Approximately(residualMismatchFade, 0.9f))
            {
                // A fully authoritative measured surface must be able to own its
                // complete calibrated subspace instead of retaining ten percent
                // of duplicate authored motion.
                residualMismatchFade = 1f;
            }
            if (defaultsVersion < 6)
            {
                // Newly introduced style controls are neutral multipliers. Unity
                // deserializes missing floats as zero, so initialize them once
                // without rewriting profiles after this migration.
                trackingAcquireResponseSeconds = 0.035f;
                speechMotionStrength = 1f;
                authoredResidualDetail = 1f;
                remoteTrackingTrust = 1f;
                phoneticConstraintStrength = 1f;
                bilabialAssistStrength = 1f;
                labiodentalAssistStrength = 1f;
                sibilantAssistStrength = 1f;
                hiddenPhoneStrength = 1f;
                hiddenDetailStrength = 1f;
                tongueInferenceStrength = 1f;
                tongueOutStrength = 1f;
                tongueYStrength = 1f;
                tongueXStrength = 1f;
                tongueRollStrength = 1f;
                tongueArchStrength = 1f;
                tongueShapeStrength = 1f;
                tongueTwistStrength = 1f;
            }
            if (defaultsVersion < 7)
            {
                // Version 7 adds a short speech hangover. Unity deserializes the
                // missing float as zero, so initialize it exactly once without
                // rewriting a subsequently customized stability setting.
                speechHangoverSeconds = 0.16f;
            }
            if (defaultsVersion < 8)
            {
                // Version 8 adds a bounded fast-observer lead for speech-only
                // rendering. Initialize the missing serialized float once; zero
                // remains a valid deliberate legacy-response preference after
                // the profile has been upgraded.
                speechLiveliness = 0.5f;
            }
            defaultsVersion = CurrentDefaultsVersion;
        }

        [ContextMenu("Reset Profile To Defaults")]
        public void ResetToDefaults()
        {
            visemePoses = new VisemeArticulationPose[VisemeCount];
            for (var i = 0; i < VisemeCount; i++) visemePoses[i] = CreateDefaultPose(i);
            articulatorBindings = CreateDefaultBindings();
            visemeResponseSeconds = 0.024f;
            speechHangoverSeconds = 0.16f;
            localTrackingResponseSeconds = 0.018f;
            remoteTrackingResponseSeconds = 0.065f;
            trackingAcquireResponseSeconds = 0.035f;
            trackingBlendResponseSeconds = 0.12f;
            quietSpeechFloor = 0.55f;
            voiceNoiseFloor = 0.05f;
            voiceFullScale = 0.25f;
            betaCoarticulationStrength = 1f;
            speechMotionStrength = 1f;
            speechLiveliness = 0.5f;
            authoredResidualDetail = 1f;
            remoteTrackingTrust = 1f;
            phoneticConstraintStrength = 1f;
            bilabialAssistStrength = 1f;
            labiodentalAssistStrength = 1f;
            sibilantAssistStrength = 1f;
            hiddenPhoneStrength = 1f;
            hiddenDetailStrength = 1f;
            tongueInferenceStrength = 1f;
            tongueOutStrength = 1f;
            tongueYStrength = 1f;
            tongueXStrength = 1f;
            tongueRollStrength = 1f;
            tongueArchStrength = 1f;
            tongueShapeStrength = 1f;
            tongueTwistStrength = 1f;
            bilabialClosure = 0.9f;
            labiodentalBite = 0.85f;
            sibilantJawMaximum = 0.22f;
            vowelIdentityRetention = 0f;
            residualMismatchFade = 1f;
            defaultsVersion = CurrentDefaultsVersion;
        }

        private void OnEnable() => EnsureDefaults();
        private void OnValidate()
        {
            EnsureDefaults();
            speechHangoverSeconds = Mathf.Clamp(speechHangoverSeconds, 0.04f, 0.4f);
            voiceNoiseFloor = Mathf.Clamp(voiceNoiseFloor, 0f, 0.99f);
            voiceFullScale = Mathf.Clamp(voiceFullScale, voiceNoiseFloor + 0.001f, 1f);
            betaCoarticulationStrength = Mathf.Clamp01(betaCoarticulationStrength);
            trackingAcquireResponseSeconds = Mathf.Clamp(
                trackingAcquireResponseSeconds, 0.005f, 0.1f);
            speechMotionStrength = Mathf.Clamp01(speechMotionStrength);
            speechLiveliness = Mathf.Clamp01(speechLiveliness);
            authoredResidualDetail = Mathf.Clamp01(authoredResidualDetail);
            remoteTrackingTrust = Mathf.Clamp01(remoteTrackingTrust);
            phoneticConstraintStrength = Mathf.Clamp01(phoneticConstraintStrength);
            bilabialAssistStrength = Mathf.Clamp01(bilabialAssistStrength);
            labiodentalAssistStrength = Mathf.Clamp01(labiodentalAssistStrength);
            sibilantAssistStrength = Mathf.Clamp01(sibilantAssistStrength);
            hiddenPhoneStrength = Mathf.Clamp01(hiddenPhoneStrength);
            hiddenDetailStrength = Mathf.Clamp01(hiddenDetailStrength);
            tongueInferenceStrength = Mathf.Clamp01(tongueInferenceStrength);
            tongueOutStrength = Mathf.Clamp01(tongueOutStrength);
            tongueYStrength = Mathf.Clamp01(tongueYStrength);
            tongueXStrength = Mathf.Clamp01(tongueXStrength);
            tongueRollStrength = Mathf.Clamp01(tongueRollStrength);
            tongueArchStrength = Mathf.Clamp01(tongueArchStrength);
            tongueShapeStrength = Mathf.Clamp01(tongueShapeStrength);
            tongueTwistStrength = Mathf.Clamp01(tongueTwistStrength);
        }

        public static VisemeReconstructionProfile CreateDefaultRuntimeProfile()
        {
            var profile = CreateInstance<VisemeReconstructionProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            profile.ResetToDefaults();
            return profile;
        }

        private static ArticulatorRigBinding[] CreateDefaultBindings()
        {
            return new[]
            {
                Binding(AdvancedVisemeArticulator.JawOpen, "JawOpen"),
                Binding(AdvancedVisemeArticulator.LipClose, "MouthClosed"),
                Binding(AdvancedVisemeArticulator.MouthOpen, "MouthOpen"),
                Binding(AdvancedVisemeArticulator.LipFunnel, "LipFunnel"),
                Binding(AdvancedVisemeArticulator.LipPucker, "LipPucker"),
                Binding(AdvancedVisemeArticulator.LipSuck, "LipSuck"),
                Binding(AdvancedVisemeArticulator.SmileSad, "SmileSad"),
                Binding(AdvancedVisemeArticulator.LipBite, ""),
                Binding(AdvancedVisemeArticulator.TongueOut, "TongueOut"),
                Binding(AdvancedVisemeArticulator.JawX, "JawX"),
                Binding(AdvancedVisemeArticulator.JawZ, "JawZ"),
                Binding(AdvancedVisemeArticulator.MouthX, "MouthX"),
                Binding(AdvancedVisemeArticulator.TongueY, "TongueY"),
                Binding(AdvancedVisemeArticulator.TongueX, "TongueX"),
                Binding(AdvancedVisemeArticulator.TongueRoll, "TongueRoll"),
                Binding(AdvancedVisemeArticulator.TongueArchY, "TongueArchY"),
                Binding(AdvancedVisemeArticulator.TongueShape, "TongueShape"),
                Binding(AdvancedVisemeArticulator.TongueTwistRight, "TongueTwistRight"),
                Binding(AdvancedVisemeArticulator.TongueTwistLeft, "TongueTwistLeft")
            };
        }

        private static ArticulatorRigBinding Binding(AdvancedVisemeArticulator articulator, string tracking)
        {
            return new ArticulatorRigBinding { articulator = articulator, trackingParameter = tracking };
        }

        private static bool IsExtendedTongue(AdvancedVisemeArticulator articulator)
        {
            return articulator == AdvancedVisemeArticulator.TongueX ||
                   articulator == AdvancedVisemeArticulator.TongueRoll ||
                   articulator == AdvancedVisemeArticulator.TongueArchY ||
                   articulator == AdvancedVisemeArticulator.TongueShape ||
                   articulator == AdvancedVisemeArticulator.TongueTwistRight ||
                   articulator == AdvancedVisemeArticulator.TongueTwistLeft;
        }

        private static VisemeArticulationPose CreateDefaultPose(int index)
        {
            var pose = new VisemeArticulationPose { name = VisemeNames[index] };
            switch (index)
            {
                case 1: pose.lipClose = 1f; pose.lipPucker = 0.12f; break;
                case 2: pose.lipBite = 1f; pose.mouthOpen = 0.12f; break;
                case 3: pose.mouthOpen = 0.25f; pose.tongueOut = 0.55f; break;
                case 4: pose.jawOpen = 0.24f; pose.mouthOpen = 0.25f; pose.tongueY = 0.55f; pose.tongueArchY = 0.45f; break;
                case 5: pose.jawOpen = 0.45f; pose.mouthOpen = 0.35f; pose.tongueY = -0.35f; pose.tongueArchY = -0.55f; break;
                case 6: pose.jawOpen = 0.16f; pose.mouthOpen = 0.24f; pose.lipPucker = 0.1f; pose.tongueY = 0.35f; pose.tongueArchY = 0.55f; break;
                case 7: pose.jawOpen = 0.08f; pose.mouthOpen = 0.12f; pose.smileSad = 0.3f; pose.tongueY = 0.25f; pose.tongueArchY = 0.45f; break;
                case 8: pose.jawOpen = 0.24f; pose.mouthOpen = 0.2f; pose.tongueY = 0.7f; pose.tongueArchY = 0.65f; break;
                case 9: pose.jawOpen = 0.3f; pose.mouthOpen = 0.25f; pose.lipPucker = 0.25f; pose.tongueY = 0.15f; pose.tongueRoll = 0.35f; pose.tongueArchY = 0.8f; pose.tongueShape = -0.2f; break;
                case 10: pose.jawOpen = 1f; pose.mouthOpen = 0.75f; pose.tongueY = -0.5f; pose.tongueArchY = -0.25f; break;
                case 11: pose.jawOpen = 0.45f; pose.mouthOpen = 0.45f; pose.smileSad = 0.35f; pose.tongueY = 0.15f; pose.tongueArchY = 0.25f; break;
                case 12: pose.jawOpen = 0.25f; pose.mouthOpen = 0.3f; pose.smileSad = 0.65f; pose.tongueY = 0.55f; pose.tongueArchY = 0.4f; pose.tongueShape = 0.2f; break;
                case 13: pose.jawOpen = 0.55f; pose.mouthOpen = 0.35f; pose.lipFunnel = 0.85f; pose.lipPucker = 0.4f; pose.tongueY = -0.05f; pose.tongueArchY = -0.25f; break;
                case 14: pose.jawOpen = 0.25f; pose.mouthOpen = 0.15f; pose.lipFunnel = 0.5f; pose.lipPucker = 1f; pose.tongueY = 0.35f; pose.tongueArchY = -0.4f; break;
            }
            return pose;
        }
    }
}
