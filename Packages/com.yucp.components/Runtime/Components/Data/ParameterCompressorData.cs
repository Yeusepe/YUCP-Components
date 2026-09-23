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
        [Tooltip("Final Animator parameter name. Automatic rules may also be matched by a processor-provided stable identity.")]
        public string parameterName = string.Empty;

        [Tooltip("Choose whether this parameter follows automatic safety rules, is explicitly included as persistent state, uses the unverified advanced override, or remains directly synced.")]
        public ParameterCompressionRuleSelection selection =
            ParameterCompressionRuleSelection.Automatic;

        [Tooltip("Controls how quickly this parameter is refreshed relative to other compressed values.")]
        public ParameterCompressionPriority priority =
            ParameterCompressionPriority.Normal;

        [Tooltip("Quantization precision for numeric parameters. Automatic chooses a suitable precision from the parameter's use.")]
        public ParameterCompressionPrecision precision =
            ParameterCompressionPrecision.Automatic;

        [Tooltip("Expected numeric range. Automatic follows the VRChat parameter type and its observed menu use.")]
        public ParameterCompressionRangeMode range =
            ParameterCompressionRangeMode.Automatic;

        [Tooltip("Minimum value when Range is Custom.")]
        public float minimum;

        [Tooltip("Maximum value when Range is Custom.")]
        public float maximum = 1f;

        [Tooltip("Optional scheduling group. Values in the same group are kept together when practical.")]
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
        [Tooltip("Choose safe, persistent menu parameters automatically. Turn this off to compress only parameters explicitly included by rules.")]
        public bool automaticSelection = true;

        [Tooltip("Leave this many synced bits unused for features added later in the build.")]
        [Range(0, ParameterCompressionContract.MaximumReservedBits)]
        public int reserveSyncedBits;

        [Tooltip("0 favors faster remote updates. 1 favors the smallest possible synced footprint.")]
        [Range(0f, 1f)]
        public float optimizationBias = 0.5f;

        [Header("Transport")]
        [Tooltip("Prefix used by generated carrier, framing, latch, and diagnostic parameters.")]
        public string parameterPrefix = ParameterCompressionContract.DefaultPrefix;

        [Header("Rules")]
        [Tooltip("Optional reusable policy shared by multiple avatars or prefabs.")]
        public ParameterCompressionProfile profile;

        [Tooltip("Per-avatar overrides and explicit parameter policies. Component rules are applied after profile rules.")]
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
