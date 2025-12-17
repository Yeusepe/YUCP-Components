using System;
using System.Text;

namespace YUCP.Components.Editor.PackageVerifier.Data
{
    /// <summary>
    /// Certificate type in the chain (Root CA, Intermediate CA, or Publisher)
    /// </summary>
    public enum CertificateType
    {
        Root,
        Intermediate,
        Publisher
    }

    /// <summary>
    /// Represents a certificate in the certificate chain
    /// </summary>
    [Serializable]
    public class CertificateData
    {
        public string keyId;                    // Unique identifier for this certificate's public key
        public string publicKey;                // Base64-encoded Ed25519 public key (32 bytes)
        public string signature;                // Base64-encoded signature (signed by parent certificate)
        public string issuerKeyId;              // Key ID of the parent certificate that signed this one
        public CertificateType certificateType; // Type of certificate (Root, Intermediate, Publisher)
        public string publisherId;              // Publisher identifier (only for Publisher certificates)
        public string notBefore;                // Optional: ISO 8601 date/time when certificate becomes valid
        public string notAfter;                 // Optional: ISO 8601 date/time when certificate expires

        /// <summary>
        /// Get the canonical JSON representation of this certificate for signature verification
        /// This excludes the signature field itself (as it's what we're verifying)
        /// </summary>
        public byte[] GetSerializedData()
        {
            // Build canonical JSON without the signature field
            var sb = new StringBuilder();
            sb.Append("{");

            var fields = new System.Collections.Generic.List<(string name, string value)>();

            if (!string.IsNullOrEmpty(keyId))
                fields.Add(("keyId", $"\"{EscapeJson(keyId)}\""));

            if (!string.IsNullOrEmpty(publicKey))
                fields.Add(("publicKey", $"\"{EscapeJson(publicKey)}\""));

            if (!string.IsNullOrEmpty(issuerKeyId))
                fields.Add(("issuerKeyId", $"\"{EscapeJson(issuerKeyId)}\""));

            fields.Add(("certificateType", $"\"{certificateType}\""));

            if (!string.IsNullOrEmpty(publisherId))
                fields.Add(("publisherId", $"\"{EscapeJson(publisherId)}\""));

            if (!string.IsNullOrEmpty(notBefore))
                fields.Add(("notBefore", $"\"{EscapeJson(notBefore)}\""));

            if (!string.IsNullOrEmpty(notAfter))
                fields.Add(("notAfter", $"\"{EscapeJson(notAfter)}\""));

            // Sort fields alphabetically
            fields.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            for (int i = 0; i < fields.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{fields[i].name}\":{fields[i].value}");
            }

            sb.Append("}");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private static string EscapeJson(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t")
                .Replace("\b", "\\b")
                .Replace("\f", "\\f");
        }
    }
}
























