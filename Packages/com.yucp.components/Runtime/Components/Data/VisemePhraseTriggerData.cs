using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    public enum VisemePhraseContextMode
    {
        NaturalSpeech,
        PausedCommand
    }

    [Serializable]
    public sealed class VisemePhraseDefinition
    {
        [Tooltip("Generated once. Recorded takes stay linked to it when the phrase text changes.")]
        public string id = string.Empty;

        [Tooltip("What you say. Use words with at least four distinct mouth shapes.")]
        public string prompt = string.Empty;

        [Tooltip("Name used in this phrase's Matched, Confidence and Progress parameters.")]
        public string parameterKey = string.Empty;

        [Tooltip("Natural Speech matches the phrase inside a sentence. Paused Command needs a short pause before and after it, which avoids accidental matches.")]
        public VisemePhraseContextMode mode = VisemePhraseContextMode.NaturalSpeech;

        [Range(0f, 1f)]
        [Tooltip("Higher rejects speech that differs more from your takes. Lower accepts more variation.")]
        public float strictness = 0.65f;

        [Min(0.05f)]
        [Tooltip("How long Matched stays on after a match, in seconds.")]
        public float pulseSeconds = 0.25f;

        [Min(1.25f)]
        [Tooltip("The shortest time between matches, in seconds. VRChat sync needs at least 1.25.")]
        public float cooldownSeconds = 1.25f;

        public string PromptFingerprint =>
            AdvancedVisemeParameterContract.PromptFingerprint(prompt);

        public void EnsureDefaults()
        {
            prompt = (prompt ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id))
                id = AdvancedVisemeParameterContract.NewPhraseId();
            id = AdvancedVisemeParameterContract.NormalizePhraseId(id);
            if (string.IsNullOrWhiteSpace(parameterKey) &&
                !string.IsNullOrWhiteSpace(prompt))
                parameterKey = AdvancedVisemeParameterContract.DefaultParameterKey(prompt, id);
            parameterKey = string.IsNullOrWhiteSpace(parameterKey)
                ? string.Empty
                : AdvancedVisemeParameterContract.NormalizePhraseId(parameterKey);
            strictness = Mathf.Clamp01(IsFinite(strictness) ? strictness : 0.65f);
            pulseSeconds = Mathf.Max(0.05f, IsFinite(pulseSeconds) ? pulseSeconds : 0.25f);
            cooldownSeconds = Mathf.Max(1.25f, IsFinite(cooldownSeconds) ? cooldownSeconds : 1.25f);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("YUCP/Viseme Phrase Trigger")]
    [HelpURL("https://github.com/Yeusepe/YUCP-Components#viseme-phrase-trigger")]
    [SupportBanner]
    public sealed class VisemePhraseTriggerData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        public const string DefaultParameterPrefix = AdvancedVisemeParameterContract.DefaultPhrasePrefix;
        public const float MinimumCooldownSeconds = 1.25f;

        [Tooltip("Parameter prefix of the Advanced Viseme Reconstructor to read. Leave empty to detect it automatically.")]
        public string sourcePrefix = string.Empty;

        [Tooltip("Prefix for each phrase's Matched (a short pulse), Confidence and Progress parameters.")]
        public string parameterPrefix = DefaultParameterPrefix;

        [Tooltip("Stores your takes as viseme and voice traces, never audio, with the compiled phrase models. Reusable.")]
        public VisemePhraseEnrollmentProfile enrollmentProfile;

        public List<VisemePhraseDefinition> phrases = new List<VisemePhraseDefinition>();

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;

        public string NormalizedPrefix =>
            AdvancedVisemeParameterContract.NormalizePrefix(parameterPrefix, DefaultParameterPrefix);

        public string NormalizedParameterPrefix => NormalizedPrefix;

        public string NormalizedSourcePrefix =>
            string.IsNullOrWhiteSpace(sourcePrefix)
                ? string.Empty
                : AdvancedVisemeParameterContract.NormalizePrefix(sourcePrefix);

        public void EnsureDefaults()
        {
            parameterPrefix = NormalizedPrefix;
            sourcePrefix = NormalizedSourcePrefix;
            if (phrases == null) phrases = new List<VisemePhraseDefinition>();

            var usedIds = new HashSet<string>(StringComparer.Ordinal);
            var usedParameterKeys = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < phrases.Count; i++)
            {
                if (phrases[i] == null) phrases[i] = new VisemePhraseDefinition();
                var phrase = phrases[i];
                phrase.EnsureDefaults();
                if (usedIds.Add(phrase.id)) continue;

                do
                {
                    phrase.id = AdvancedVisemeParameterContract.NewPhraseId();
                } while (!usedIds.Add(phrase.id));
            }

            for (var i = 0; i < phrases.Count; i++)
            {
                var phrase = phrases[i];
                if (string.IsNullOrWhiteSpace(phrase.parameterKey)) continue;
                if (usedParameterKeys.Add(phrase.parameterKey)) continue;
                var baseKey = phrase.parameterKey;
                var suffix = 2;
                do
                {
                    phrase.parameterKey = baseKey + "_" + suffix++;
                } while (!usedParameterKeys.Add(phrase.parameterKey));
            }
        }

        private void OnValidate()
        {
            EnsureDefaults();
        }
    }
}
