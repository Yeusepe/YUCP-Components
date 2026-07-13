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
        TongueY
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
        [Range(0f, 1f)] public float localReliability = 0.95f;
        [Range(0f, 1f)] public float remoteReliability = 0.75f;
    }

    [CreateAssetMenu(fileName = "Viseme Reconstruction Profile", menuName = "YUCP/Viseme Reconstruction Profile")]
    public sealed class VisemeReconstructionProfile : ScriptableObject
    {
        public const int VisemeCount = 15;
        public static readonly string[] VisemeNames =
        {
            "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "I", "O", "U"
        };

        [Header("Observer")]
        [Min(0.005f)] public float visemeResponseSeconds = 0.024f;
        [Min(0.005f)] public float localTrackingResponseSeconds = 0.018f;
        [Min(0.005f)] public float remoteTrackingResponseSeconds = 0.065f;
        [Min(0.005f)] public float trackingBlendResponseSeconds = 0.12f;
        [Range(0f, 1f)] public float quietSpeechFloor = 0.55f;
        [Range(0f, 1f)] public float voiceNoiseFloor = 0.05f;
        [Range(0.01f, 1f)] public float voiceFullScale = 0.25f;

        [Header("Phonetic Constraints")]
        [Range(0f, 1f)] public float bilabialClosure = 0.9f;
        [Range(0f, 1f)] public float labiodentalBite = 0.85f;
        [Range(0f, 1f)] public float sibilantJawMaximum = 0.22f;

        [Header("Viseme Poses")]
        public VisemeArticulationPose[] visemePoses = new VisemeArticulationPose[VisemeCount];

        [Header("Rig Bindings")]
        public ArticulatorRigBinding[] articulatorBindings = Array.Empty<ArticulatorRigBinding>();

        [Header("Calibration Diagnostics")]
        [SerializeField, HideInInspector] private float lastReconstructionRms;
        [SerializeField, HideInInspector] private float lastReconstructionMaximum;

        public float LastReconstructionRms => lastReconstructionRms;
        public float LastReconstructionMaximum => lastReconstructionMaximum;

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

            if (articulatorBindings == null || articulatorBindings.Length == 0)
            {
                articulatorBindings = CreateDefaultBindings();
            }
        }

        [ContextMenu("Reset Profile To Defaults")]
        public void ResetToDefaults()
        {
            visemePoses = new VisemeArticulationPose[VisemeCount];
            for (var i = 0; i < VisemeCount; i++) visemePoses[i] = CreateDefaultPose(i);
            articulatorBindings = CreateDefaultBindings();
            visemeResponseSeconds = 0.024f;
            localTrackingResponseSeconds = 0.018f;
            remoteTrackingResponseSeconds = 0.065f;
            trackingBlendResponseSeconds = 0.12f;
            quietSpeechFloor = 0.55f;
            voiceNoiseFloor = 0.05f;
            voiceFullScale = 0.25f;
            bilabialClosure = 0.9f;
            labiodentalBite = 0.85f;
            sibilantJawMaximum = 0.22f;
        }

        private void OnEnable() => EnsureDefaults();
        private void OnValidate()
        {
            EnsureDefaults();
            voiceNoiseFloor = Mathf.Clamp(voiceNoiseFloor, 0f, 0.99f);
            voiceFullScale = Mathf.Clamp(voiceFullScale, voiceNoiseFloor + 0.001f, 1f);
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
                Binding(AdvancedVisemeArticulator.TongueY, "TongueY")
            };
        }

        private static ArticulatorRigBinding Binding(AdvancedVisemeArticulator articulator, string tracking)
        {
            return new ArticulatorRigBinding { articulator = articulator, trackingParameter = tracking };
        }

        private static VisemeArticulationPose CreateDefaultPose(int index)
        {
            var pose = new VisemeArticulationPose { name = VisemeNames[index] };
            switch (index)
            {
                case 1: pose.lipClose = 1f; pose.lipPucker = 0.12f; break;
                case 2: pose.lipBite = 1f; pose.mouthOpen = 0.12f; break;
                case 3: pose.mouthOpen = 0.25f; pose.tongueOut = 0.55f; break;
                case 4: pose.jawOpen = 0.24f; pose.mouthOpen = 0.25f; break;
                case 5: pose.jawOpen = 0.45f; pose.mouthOpen = 0.35f; break;
                case 6: pose.jawOpen = 0.16f; pose.mouthOpen = 0.24f; pose.lipPucker = 0.1f; break;
                case 7: pose.jawOpen = 0.08f; pose.mouthOpen = 0.12f; pose.smileSad = 0.3f; break;
                case 8: pose.jawOpen = 0.24f; pose.mouthOpen = 0.2f; break;
                case 9: pose.jawOpen = 0.3f; pose.mouthOpen = 0.25f; pose.lipPucker = 0.25f; break;
                case 10: pose.jawOpen = 1f; pose.mouthOpen = 0.75f; break;
                case 11: pose.jawOpen = 0.45f; pose.mouthOpen = 0.45f; pose.smileSad = 0.35f; break;
                case 12: pose.jawOpen = 0.25f; pose.mouthOpen = 0.3f; pose.smileSad = 0.65f; break;
                case 13: pose.jawOpen = 0.55f; pose.mouthOpen = 0.35f; pose.lipFunnel = 0.85f; pose.lipPucker = 0.4f; break;
                case 14: pose.jawOpen = 0.25f; pose.mouthOpen = 0.15f; pose.lipFunnel = 0.5f; pose.lipPucker = 1f; break;
            }
            return pose;
        }
    }
}
