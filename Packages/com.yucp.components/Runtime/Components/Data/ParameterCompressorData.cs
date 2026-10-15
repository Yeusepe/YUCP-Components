using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    [Serializable]
    public sealed class ParameterCompressionRule
    {
        [Tooltip("The Animator parameter this rule applies to, by its final name.")]
        public string parameterName = string.Empty;

        [Tooltip("Automatic follows the safety checks. Include compresses it as saved state, Include Unverified skips the checks, and Keep Direct syncs it as is.")]
        public ParameterCompressionRuleSelection selection =
            ParameterCompressionRuleSelection.Automatic;

        [Tooltip("How often this parameter updates compared with other compressed values.")]
        public ParameterCompressionPriority priority =
            ParameterCompressionPriority.Normal;

        [Tooltip("Bits per value for numbers. Automatic chooses from how the parameter is used.")]
        public ParameterCompressionPrecision precision =
            ParameterCompressionPrecision.Automatic;

        [Tooltip("The values the parameter can take. Automatic follows its type and how the menu uses it.")]
        public ParameterCompressionRangeMode range =
            ParameterCompressionRangeMode.Automatic;

        [Tooltip("The lowest value when Range is Custom.")]
        public float minimum;

        [Tooltip("The highest value when Range is Custom.")]
        public float maximum = 1f;

        [Tooltip("Values in the same group are sent together when possible. Optional.")]
        public string group = ParameterCompressionContract.DefaultGroup;

        [SerializeField, HideInInspector]
        private string stableId;

        public string StableId => string.IsNullOrEmpty(stableId)
            ? ParameterCompressionContract.StableFingerprint(
                ParameterCompressionContract.NormalizeParameterName(parameterName) + "|" +
                ParameterCompressionContract.NormalizeGroup(group))
            : stableId;

        public void EnsureValid()
        {
            parameterName = ParameterCompressionContract.NormalizeParameterName(
                parameterName);
            group = ParameterCompressionContract.NormalizeGroup(group);
            if (string.IsNullOrEmpty(stableId))
                stableId = ParameterCompressionContract.NewStableId();
            if (!Enum.IsDefined(typeof(ParameterCompressionRuleSelection), selection))
                selection = ParameterCompressionRuleSelection.Automatic;
            if (!Enum.IsDefined(typeof(ParameterCompressionPriority), priority))
                priority = ParameterCompressionPriority.Normal;
            if (!Enum.IsDefined(typeof(ParameterCompressionPrecision), precision))
                precision = ParameterCompressionPrecision.Automatic;
            if (!Enum.IsDefined(typeof(ParameterCompressionRangeMode), range))
                range = ParameterCompressionRangeMode.Automatic;
            if (float.IsNaN(minimum) || float.IsInfinity(minimum)) minimum = 0f;
            if (float.IsNaN(maximum) || float.IsInfinity(maximum)) maximum = 1f;
            if (maximum < minimum)
            {
                var swap = minimum;
                minimum = maximum;
                maximum = swap;
            }
            if (Mathf.Approximately(minimum, maximum)) maximum = minimum + 1f;
        }
    }

    [CreateAssetMenu(
        fileName = "Parameter Compression Profile",
        menuName = "YUCP/Parameter Compression Profile")]
    public sealed class ParameterCompressionProfile : ScriptableObject
    {
        [Tooltip("Reusable parameter policies applied before rules stored directly on a compressor component.")]
        public List<ParameterCompressionRule> rules =
            new List<ParameterCompressionRule>();

        private void OnValidate()
        {
            if (rules == null) rules = new List<ParameterCompressionRule>();
            foreach (var rule in rules)
                rule?.EnsureValid();
        }
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("YUCP/Parameter Compressor")]
    [HelpURL("https://github.com/Yeusepe/YUCP-Components#parameter-compressor")]
    [SupportBanner]
    public sealed class ParameterCompressorData : MonoBehaviour,
        IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Automatic Setup")]
        [Tooltip("Compresses safe, saved menu parameters automatically. When off, only parameters a rule includes are compressed.")]
        public bool automaticSelection = true;

        [Tooltip("Synced bits kept free for parameters other tools add later in the build.")]
        [Range(0, ParameterCompressionContract.MaximumReservedBits)]
        public int reserveSyncedBits;

        [Tooltip("How the compressor trades update speed for synced bits.")]
        [Range(0f, 1f)]
        public float optimizationBias = 0.5f;

        [Header("Transport")]
        [Tooltip("Prefix for the parameters the compressor generates.")]
        public string parameterPrefix = ParameterCompressionContract.DefaultPrefix;

        [Header("Rules")]
        [Tooltip("Rules shared by several avatars or prefabs. They apply before this component's rules. Optional.")]
        public ParameterCompressionProfile profile;

        [Tooltip("Rules for this avatar. They apply after the profile's rules.")]
        public List<ParameterCompressionRule> rules =
            new List<ParameterCompressionRule>();

        [SerializeField, HideInInspector]
        private string stableId;

        [SerializeField, HideInInspector]
        private int settingsVersion;

        [SerializeField, HideInInspector]
        private ParameterCompressionBuildSummary lastBuildSummary;

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;

        public string NormalizedPrefix =>
            ParameterCompressionContract.NormalizePrefix(parameterPrefix);

        public string StableId => string.IsNullOrEmpty(stableId)
            ? "pc_" + ParameterCompressionContract.StableFingerprint(NormalizedPrefix)
            : stableId;

        public IEnumerable<ParameterCompressionRule> EnumerateRules()
        {
            if (profile != null && profile.rules != null)
                foreach (var rule in profile.rules)
                    if (rule != null) yield return rule;
            if (rules != null)
                foreach (var rule in rules)
                    if (rule != null) yield return rule;
        }

        public IReadOnlyList<ParameterCompressionRule> EffectiveRules()
        {
            return EnumerateRules().ToArray();
        }

        public ParameterCompressionBuildSummary GetBuildSummary()
        {
            return lastBuildSummary;
        }

        public void SetBuildSummary(ParameterCompressionBuildSummary summary)
        {
            lastBuildSummary = summary;
        }

        public void ClearBuildSummary()
        {
            lastBuildSummary = ParameterCompressionBuildSummary.Empty;
        }

        private void OnValidate()
        {
            parameterPrefix = NormalizedPrefix;
            reserveSyncedBits = Mathf.Clamp(
                reserveSyncedBits, 0,
                ParameterCompressionContract.MaximumReservedBits);
            optimizationBias = Mathf.Clamp01(optimizationBias);
            if (rules == null) rules = new List<ParameterCompressionRule>();
            foreach (var rule in rules)
                rule?.EnsureValid();
            if (string.IsNullOrEmpty(stableId))
                stableId = ParameterCompressionContract.NewStableId();
            if (settingsVersion < ParameterCompressionContract.ContractVersion)
                settingsVersion = ParameterCompressionContract.ContractVersion;
        }
    }
}
