using System;
using System.Collections.Generic;
using System.Linq;

namespace YUCP.Components.Editor.VisemePhrase
{
    internal readonly struct VisemePhraseTimingSummary
    {
        internal readonly bool available;
        internal readonly float minimum;
        internal readonly float median;
        internal readonly float maximum;

        internal VisemePhraseTimingSummary(bool available, float minimum, float median, float maximum)
        {
            this.available = available;
            this.minimum = minimum;
            this.median = median;
            this.maximum = maximum;
        }
    }

    internal readonly struct VisemePhraseParameterMemorySummary
    {
        internal readonly int phraseCount;
        internal readonly int existingBits;
        internal readonly int newBits;

        internal int estimatedTotal => existingBits + newBits;

        internal VisemePhraseParameterMemorySummary(int phraseCount, int existingBits, int newBits)
        {
            this.phraseCount = Math.Max(0, phraseCount);
            this.existingBits = Math.Max(0, existingBits);
            this.newBits = Math.Max(0, newBits);
        }
    }

    internal static class VisemePhraseInspectorDiagnostics
    {
        internal static IReadOnlyList<string> Branches(VisemePhraseCompiledModel model)
        {
            var result = new List<string>();
            if (model?.variants == null) return result;
            for (var variantIndex = 0; variantIndex < model.variants.Count; variantIndex++)
            {
                var variant = model.variants[variantIndex];
                if (variant?.states == null) continue;
                result.Add($"Branch {variantIndex + 1}: " +
                           string.Join("  →  ", variant.states.Select(StateAliases)));
            }
            return result;
        }

        internal static VisemePhraseTimingSummary Timing(VisemePhraseCompiledModel model)
        {
            var states = model?.variants?
                .Where(variant => variant?.states != null)
                .SelectMany(variant => variant.states)
                .Where(state => state != null)
                .ToArray() ?? Array.Empty<VisemePhraseModelState>();
            if (states.Length == 0) return new VisemePhraseTimingSummary(false, 0f, 0f, 0f);
            var medians = states.Select(state => state.medianDurationSeconds)
                .OrderBy(value => value)
                .ToArray();
            return new VisemePhraseTimingSummary(
                true,
                states.Min(state => state.minimumDurationSeconds),
                medians[medians.Length / 2],
                states.Max(state => state.maximumDurationSeconds));
        }

        internal static string Calibration(VisemePhraseCompiledModel model)
        {
            var calibration = model?.negativeCalibration;
            return calibration != null && calibration.calibrated
                ? $"Negative calibration: {calibration.separation:0.000} margin from " +
                  $"{calibration.negativeTraceCount} sample(s)"
                : "Negative calibration: not recorded (optional 15-second sample)";
        }

        internal static VisemePhraseParameterMemorySummary ParameterMemory(
            int existingBits,
            IEnumerable<string> existingParameterNames,
            IEnumerable<string> carrierNames)
        {
            var existing = new HashSet<string>(
                existingParameterNames ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            var carriers = (carrierNames ?? Array.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
            var newBits = carriers
                .Distinct(StringComparer.Ordinal)
                .Count(name => !existing.Contains(name));
            return new VisemePhraseParameterMemorySummary(
                carriers.Length,
                existingBits,
                newBits);
        }

        private static string StateAliases(VisemePhraseModelState state)
        {
            if (state == null) return "?";
            var aliases = new[] { state.primaryViseme }
                .Concat(state.aliasVisemes ?? Array.Empty<int>())
                .Where(index => index >= 0 && index < VisemeTestMath.VisemeNames.Length)
                .Distinct()
                .Select(index => VisemeTestMath.VisemeNames[index])
                .ToArray();
            return aliases.Length <= 1
                ? aliases.FirstOrDefault() ?? "?"
                : "[" + string.Join(" | ", aliases) + "]";
        }
    }
}
