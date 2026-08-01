using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using YUCP.Importer.Editor.PackageVerifier.Crypto;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class BootstrapIntentVerifier
    {
        internal const string SigningKeyId = "package-install-2026-07";
        internal const string SigningPublicKeyBase64Url =
            "ttfbj88fvTQarpNxYFeAh39VKA2IXuUEeXTI01NFjcQ";

        internal enum Verdict
        {
            Trusted,
            Unsigned,
            Tampered,
        }

        internal static string Base64UrlToBase64(string value)
        {
            string padded = (value ?? string.Empty).Replace('-', '+').Replace('_', '/');
            return padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        }

        /// <summary>
        /// Byte-for-byte mirror of yucpBootstrapRequirementsPayload. Any drift
        /// makes every install fail closed, so the shapes must stay identical.
        /// </summary>
        internal static byte[] RequirementsPayload(
            IReadOnlyDictionary<string, string> dependencies,
            IReadOnlyDictionary<string, string> repositories)
        {
            var builder = new StringBuilder();
            builder.Append("{\"purpose\":\"yucp-bootstrap-requirements-v1\",\"vpmDependencies\":");
            AppendSortedMap(builder, dependencies);
            builder.Append(",\"vpmRepositories\":");
            AppendSortedMap(builder, repositories);
            builder.Append('}');
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        internal static string RequirementsDigest(
            IReadOnlyDictionary<string, string> dependencies,
            IReadOnlyDictionary<string, string> repositories)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return string.Concat(
                    sha256
                        .ComputeHash(RequirementsPayload(dependencies, repositories))
                        .Select(value => value.ToString("x2")));
            }
        }

        internal static byte[] IntentSigningPayload(
            string aliasId,
            BootstrapIntentContract intent)
        {
            var builder = new StringBuilder();
            builder.Append("{\"purpose\":\"yucp-bootstrap-intent-v1\",\"aliasId\":");
            AppendJsonString(builder, aliasId?.Trim() ?? string.Empty);
            builder.Append(",\"schemaVersion\":").Append(intent.schemaVersion);
            builder.Append(",\"intentId\":");
            AppendJsonString(builder, intent.intentId?.Trim().ToLowerInvariant() ?? string.Empty);
            builder.Append(",\"mode\":");
            AppendJsonString(builder, intent.mode?.Trim() ?? string.Empty);
            builder.Append(",\"issuedAt\":").Append(intent.issuedAt);
            builder.Append(",\"keyId\":");
            AppendJsonString(builder, intent.keyId?.Trim() ?? string.Empty);
            builder.Append(",\"editionId\":");
            AppendJsonString(builder, intent.editionId?.Trim() ?? string.Empty);
            AppendOptional(builder, "version", intent.version);
            AppendOptional(builder, "versionId", intent.versionId);
            AppendOptional(builder, "releaseRoot", intent.releaseRoot);
            AppendOptional(builder, "requirementsDigest", intent.requirementsDigest);
            builder.Append('}');
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        /// <summary>
        /// Trusted only when the signature verifies against the pinned key and
        /// the requirements the descriptor carries are the ones that were
        /// signed. An intent without a digest predates the binding and is
        /// reported as unsigned rather than trusted.
        /// </summary>
        internal static Verdict Verify(
            string aliasId,
            BootstrapIntentContract intent,
            IReadOnlyDictionary<string, string> dependencies,
            IReadOnlyDictionary<string, string> repositories)
        {
            if (intent == null ||
                string.IsNullOrWhiteSpace(intent.signature) ||
                string.IsNullOrWhiteSpace(intent.requirementsDigest))
            {
                return Verdict.Unsigned;
            }
            if (!string.Equals(intent.keyId?.Trim(), SigningKeyId, StringComparison.Ordinal))
            {
                return Verdict.Tampered;
            }

            byte[] signature;
            byte[] publicKey;
            try
            {
                signature = Convert.FromBase64String(Base64UrlToBase64(intent.signature.Trim()));
                publicKey = Convert.FromBase64String(
                    Base64UrlToBase64(SigningPublicKeyBase64Url));
            }
            catch (FormatException)
            {
                return Verdict.Tampered;
            }
            if (signature.Length != 64 || publicKey.Length != 32)
            {
                return Verdict.Tampered;
            }

            bool signatureValid;
            try
            {
                signatureValid = Ed25519Wrapper.Verify(
                    IntentSigningPayload(aliasId, intent),
                    signature,
                    publicKey);
            }
            catch (Exception)
            {
                return Verdict.Tampered;
            }
            if (!signatureValid)
            {
                return Verdict.Tampered;
            }

            string observed = RequirementsDigest(dependencies, repositories);
            return string.Equals(
                observed,
                intent.requirementsDigest.Trim().ToLowerInvariant(),
                StringComparison.Ordinal)
                ? Verdict.Trusted
                : Verdict.Tampered;
        }

        private static void AppendOptional(StringBuilder builder, string name, string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
            {
                return;
            }
            builder.Append(",\"").Append(name).Append("\":");
            AppendJsonString(builder, normalized);
        }

        private static void AppendSortedMap(
            StringBuilder builder,
            IReadOnlyDictionary<string, string> map)
        {
            builder.Append('{');
            bool first = true;
            IEnumerable<KeyValuePair<string, string>> entries =
                (map ?? new Dictionary<string, string>())
                    .Select(pair => new KeyValuePair<string, string>(
                        pair.Key?.Trim() ?? string.Empty,
                        pair.Value?.Trim() ?? string.Empty))
                    .Where(pair => pair.Key.Length > 0 && pair.Value.Length > 0)
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in entries)
            {
                if (!first)
                {
                    builder.Append(',');
                }
                first = false;
                AppendJsonString(builder, pair.Key);
                builder.Append(':');
                AppendJsonString(builder, pair.Value);
            }
            builder.Append('}');
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char character in value ?? string.Empty)
            {
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u").Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }
            builder.Append('"');
        }
    }
}
