using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace YUCP.Components
{
    public enum ParameterCompressionRuleSelection
    {
        [InspectorName("Automatic")]
        Automatic,

        [InspectorName("Include in Compression")]
        Include,

        [InspectorName("Include Unverified (Advanced)")]
        IncludeUnverified,

        [InspectorName("Keep Direct")]
        KeepDirect
    }

    public enum ParameterCompressionPriority
    {
        [InspectorName("Immediate")]
        Immediate,

        [InspectorName("Interactive")]
        Interactive,

        [InspectorName("Normal")]
        Normal,

        [InspectorName("Background")]
        Background
    }

    public enum ParameterCompressionPrecision
    {
        [InspectorName("Automatic")]
        Automatic,

        [InspectorName("8-bit / Native")]
        Bits8,

        [InspectorName("7-bit")]
        Bits7,

        [InspectorName("6-bit")]
        Bits6,

        [InspectorName("5-bit")]
        Bits5,

        [InspectorName("4-bit")]
        Bits4,

        [InspectorName("3-bit")]
        Bits3,

        [InspectorName("2-bit")]
        Bits2,

        [InspectorName("1-bit")]
        Bits1
    }

    public enum ParameterCompressionRangeMode
    {
        [InspectorName("Automatic")]
        Automatic,

        [InspectorName("0 to 1")]
        ZeroToOne,

        [InspectorName("-1 to 1")]
        SignedUnit,

        [InspectorName("Custom")]
        Custom
    }

    /// <summary>
    /// Stable protocol and naming helpers shared by the generic compressor's
    /// component, inspector, planner, controller generator, and tests.
    /// </summary>
    public static class ParameterCompressionContract
    {
        public const int ContractVersion = 1;
        public const int VrchatParameterBudget = 256;
        public const int MaximumReservedBits = 128;
        public const string DefaultPrefix = "YUCP/ParameterCompressor";
        public const string DefaultGroup = "General";

        public static string NormalizePrefix(string prefix)
        {
            var value = string.IsNullOrWhiteSpace(prefix)
                ? DefaultPrefix
                : prefix.Trim().Trim('/');
            return string.IsNullOrEmpty(value) ? DefaultPrefix : value;
        }

        public static string NormalizeParameterName(string parameterName)
        {
            return (parameterName ?? string.Empty).Trim().Trim('/');
        }

        public static string NormalizeGroup(string group)
        {
            var value = (group ?? string.Empty).Trim();
            return string.IsNullOrEmpty(value) ? DefaultGroup : value;
        }

        public static string NewStableId()
        {
            return "pc_" + Guid.NewGuid().ToString("N");
        }

        public static string StableFingerprint(string value)
        {
            // string.GetHashCode() is deliberately avoided because it is not
            // stable across runtimes or editor sessions.
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            unchecked
            {
                const ulong offset = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                var hash = offset;
                for (var i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= prime;
                }

                return hash.ToString("x16", CultureInfo.InvariantCulture);
            }
        }

        public static int PrecisionBits(ParameterCompressionPrecision precision)
        {
            switch (precision)
            {
                case ParameterCompressionPrecision.Bits8: return 8;
                case ParameterCompressionPrecision.Bits7: return 7;
                case ParameterCompressionPrecision.Bits6: return 6;
                case ParameterCompressionPrecision.Bits5: return 5;
                case ParameterCompressionPrecision.Bits4: return 4;
                case ParameterCompressionPrecision.Bits3: return 3;
                case ParameterCompressionPrecision.Bits2: return 2;
                case ParameterCompressionPrecision.Bits1: return 1;
                default: return 0;
            }
        }

        public static int QuantizationLevelCount(
            ParameterCompressionPrecision precision)
        {
            var bits = PrecisionBits(precision);
            return bits <= 0 ? 0 : 1 << bits;
        }

        public static void ResolveRange(
            ParameterCompressionRangeMode mode,
            float customMinimum,
            float customMaximum,
            out float minimum,
            out float maximum)
        {
            switch (mode)
            {
                case ParameterCompressionRangeMode.ZeroToOne:
                    minimum = 0f;
                    maximum = 1f;
                    return;
                case ParameterCompressionRangeMode.SignedUnit:
                    minimum = -1f;
                    maximum = 1f;
                    return;
                case ParameterCompressionRangeMode.Custom:
                    minimum = Mathf.Min(customMinimum, customMaximum);
                    maximum = Mathf.Max(customMinimum, customMaximum);
                    if (Mathf.Approximately(minimum, maximum)) maximum = minimum + 1f;
                    return;
                default:
                    minimum = 0f;
                    maximum = 1f;
                    return;
            }
        }
    }

    [Serializable]
    public struct ParameterCompressionBuildSummary
    {
        public bool hasResult;
        public int beforeBits;
        public int afterBits;
        public int carrierBits;
        public int compressedParameters;
        public int protectedParameters;
        public float nominalFullRefreshSeconds;
        public string transportName;
        public string message;

        public static ParameterCompressionBuildSummary Empty =>
            new ParameterCompressionBuildSummary
            {
                transportName = string.Empty,
                message = string.Empty
            };
    }
}
