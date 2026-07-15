using System;
using System.Globalization;
using System.Text;

namespace YUCP.Components
{
    /// <summary>
    /// Stable names shared by Advanced Viseme producers and consumers. Keeping the
    /// contract in Runtime lets editor-only generators agree without depending on
    /// one another's implementation assemblies.
    /// </summary>
    public static class AdvancedVisemeParameterContract
    {
        public const int ContractVersion = 1;
        public const string DefaultAdvancedVisemePrefix = "YUCP/AdvancedViseme";
        public const string DefaultPhrasePrefix = "YUCP/Phrase";

        private static readonly string[] VisemeNames =
        {
            "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS",
            "nn", "RR", "aa", "E", "I", "O", "U"
        };

        public static string NormalizePrefix(
            string prefix,
            string fallback = DefaultAdvancedVisemePrefix)
        {
            var normalizedFallback = string.IsNullOrWhiteSpace(fallback)
                ? DefaultAdvancedVisemePrefix
                : fallback.Trim().Trim('/');
            var value = string.IsNullOrWhiteSpace(prefix)
                ? normalizedFallback
                : prefix.Trim().Trim('/');
            return string.IsNullOrEmpty(value) ? normalizedFallback : value;
        }

        public static string Viseme(string prefix, int index)
        {
            index = Math.Max(0, Math.Min(VisemeNames.Length - 1, index));
            return Viseme(prefix, VisemeNames[index]);
        }

        public static string Viseme(string prefix, string name)
        {
            var suffix = string.IsNullOrWhiteSpace(name) ? "sil" : name.Trim().Trim('/');
            return NormalizePrefix(prefix) + "/Viseme/" + suffix;
        }

        public static string Speech(string prefix, string suffix)
        {
            var value = string.IsNullOrWhiteSpace(suffix) ? "Energy" : suffix.Trim().Trim('/');
            return NormalizePrefix(prefix) + "/Speech/" + value;
        }

        public static string PhraseMatched(string prefix, string parameterKey)
        {
            return PhraseBase(prefix, parameterKey) + "/Matched";
        }

        public static string PhraseConfidence(string prefix, string parameterKey)
        {
            return PhraseBase(prefix, parameterKey) + "/Confidence";
        }

        public static string PhraseProgress(string prefix, string parameterKey)
        {
            return PhraseBase(prefix, parameterKey) + "/Progress";
        }

        public static string PhraseCarrier(string prefix, string phraseId)
        {
            return NormalizePrefix(prefix, DefaultPhrasePrefix) + "/_Network/" +
                   NormalizePhraseId(phraseId);
        }

        public static string StablePhraseId(string prompt)
        {
            return "p_" + StableFingerprint(NormalizePrompt(prompt)).Substring(0, 12);
        }

        /// <summary>
        /// Creates a persisted enrollment identity for a newly-authored phrase.
        /// Prompt hashes are useful for comparison, but are not suitable as object
        /// identities: two independently-authored prefab components may have the
        /// same prompt (or no prompt yet). The serialized random ID keeps those
        /// components independent while remaining stable after it is first saved.
        /// </summary>
        public static string NewPhraseId()
        {
            return "p_" + Guid.NewGuid().ToString("N");
        }

        public static string PromptFingerprint(string prompt)
        {
            return StableFingerprint(NormalizePrompt(prompt));
        }

        public static string DefaultParameterKey(string prompt, string phraseId)
        {
            var normalizedPrompt = NormalizePhraseId(NormalizePrompt(prompt));
            var fallback = NormalizePhraseId(phraseId);
            if (string.IsNullOrWhiteSpace(prompt)) return fallback;
            if (normalizedPrompt.Length <= 32) return normalizedPrompt;
            return normalizedPrompt.Substring(0, 25).TrimEnd('_') + "_" +
                   PromptFingerprint(prompt).Substring(0, 6);
        }

        public static string StableFingerprint(string value)
        {
            // FNV-1a is intentionally implemented here instead of using
            // string.GetHashCode(), whose result is not stable across runtimes.
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

        public static string NormalizePhraseId(string phraseId)
        {
            if (string.IsNullOrWhiteSpace(phraseId)) return StablePhraseId(string.Empty);
            var source = phraseId.Trim();
            var builder = new StringBuilder(source.Length);
            var previousSeparator = false;
            for (var i = 0; i < source.Length; i++)
            {
                var character = source[i];
                var accepted = character >= 'a' && character <= 'z' ||
                               character >= 'A' && character <= 'Z' ||
                               character >= '0' && character <= '9' ||
                               character == '_';
                if (accepted)
                {
                    builder.Append(character);
                    previousSeparator = false;
                }
                else if (!previousSeparator && builder.Length > 0)
                {
                    builder.Append('_');
                    previousSeparator = true;
                }
            }

            var result = builder.ToString().Trim('_');
            return string.IsNullOrEmpty(result) ? StablePhraseId(source) : result;
        }

        public static string NormalizePrompt(string prompt)
        {
            var source = (prompt ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim();
            var builder = new StringBuilder(source.Length);
            var pendingSpace = false;
            for (var i = 0; i < source.Length; i++)
            {
                if (char.IsWhiteSpace(source[i]))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }
                if (pendingSpace) builder.Append(' ');
                builder.Append(char.ToLowerInvariant(source[i]));
                pendingSpace = false;
            }
            return builder.ToString();
        }

        private static string PhraseBase(string prefix, string parameterKey)
        {
            return NormalizePrefix(prefix, DefaultPhrasePrefix) + "/" +
                   NormalizePhraseId(parameterKey);
        }
    }
}
