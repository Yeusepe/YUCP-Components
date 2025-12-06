using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YUCP.Components.Editor.PackageVerifier.Crypto;
using YUCP.Components.Editor.PackageVerifier.Data;

namespace YUCP.Components.Editor.PackageVerifier.Core
{
    /// <summary>
    /// Validates certificate chains for package verification
    /// </summary>
    public static class CertificateChainValidator
    {
        /// <summary>
        /// Result of certificate chain validation
        /// </summary>
        public class ValidationResult
        {
            public bool valid;
            public List<string> errors = new List<string>();
            public string publisherId; // Extracted from Publisher certificate
            public CertificateData publisherCertificate;
            public CertificateData rootCertificate;
        }

        /// <summary>
        /// Validate a certificate chain
        /// Chain should be ordered: [Publisher, Intermediate?, Root]
        /// </summary>
        public static ValidationResult ValidateChain(PackageManifest manifest, List<CertificateData> chain)
        {
            var result = new ValidationResult();

            try
            {
                // 1. Verify chain is non-empty
                if (chain == null || chain.Count == 0)
                {
                    result.valid = false;
                    result.errors.Add("Certificate chain is empty");
                    return result;
                }

                // 2. Verify chain is properly ordered (should end with Root)
                // Expected order: [Publisher, Intermediate?, Root]
                // Or: [Publisher, Root] for 2-level chain
                if (chain.Count < 2 || chain.Count > 3)
                {
                    result.valid = false;
                    result.errors.Add($"Invalid certificate chain length: {chain.Count} (expected 2 or 3)");
                    return result;
                }

                // Extract certificates by type
                var publisherCert = chain.FirstOrDefault(c => c.certificateType == CertificateType.Publisher);
                var intermediateCert = chain.FirstOrDefault(c => c.certificateType == CertificateType.Intermediate);
                var rootCert = chain.FirstOrDefault(c => c.certificateType == CertificateType.Root);

                if (publisherCert == null)
                {
                    result.valid = false;
                    result.errors.Add("Certificate chain missing Publisher certificate");
                    // Debug: Log what certificate types we actually found
                    var foundTypes = chain.Select(c => c.certificateType.ToString()).Distinct();
                    Debug.LogWarning($"[CertificateChainValidator] Found certificate types in chain: {string.Join(", ", foundTypes)}");
                    return result;
                }

                if (rootCert == null)
                {
                    result.valid = false;
                    result.errors.Add("Certificate chain missing Root certificate");
                    return result;
                }

                // Verify chain order: Root should be last
                if (chain.Last().certificateType != CertificateType.Root)
                {
                    result.valid = false;
                    result.errors.Add("Certificate chain not properly ordered: Root certificate must be last");
                    return result;
                }

                // If intermediate exists, it should be in the middle
                if (intermediateCert != null && chain.Count == 3)
                {
                    if (chain[1].certificateType != CertificateType.Intermediate)
                    {
                        result.valid = false;
                        result.errors.Add("Certificate chain not properly ordered: Intermediate certificate must be in the middle");
                        return result;
                    }
                }

                result.rootCertificate = rootCert;
                result.publisherCertificate = publisherCert;

                // 3. Verify root is trusted
                if (string.IsNullOrEmpty(rootCert.keyId))
                {
                    result.valid = false;
                    result.errors.Add("Root certificate missing keyId");
                    return result;
                }

                byte[] rootPublicKey = ParsePublicKey(rootCert.publicKey);
                if (rootPublicKey == null)
                {
                    result.valid = false;
                    result.errors.Add("Root certificate has invalid publicKey format");
                    return result;
                }

                // Check if root is trusted (hardcoded YUCP root or from URL cache)
                if (!TrustedAuthority.IsTrustedKey(rootCert.keyId))
                {
                    // Also try to get by the public key itself (if keyId doesn't match but key does)
                    byte[] trustedRootKey = TrustedAuthority.GetPublicKey(rootCert.keyId);
                    if (trustedRootKey == null || !trustedRootKey.SequenceEqual(rootPublicKey))
                    {
                        result.valid = false;
                        result.errors.Add($"Root certificate with keyId '{rootCert.keyId}' is not trusted");
                        return result;
                    }
                }

                // Verify root certificate signature (if present, root may be self-signed or unsigned)
                // For root certificates, we trust them if they're in our trusted list
                // The signature on a root cert is typically self-signed or can be omitted

                // 4. Validate certificate validity dates (if present)
                if (!ValidateCertificateValidityDates(publisherCert, result.errors))
                {
                    result.valid = false;
                    return result;
                }

                if (intermediateCert != null && !ValidateCertificateValidityDates(intermediateCert, result.errors))
                {
                    result.valid = false;
                    return result;
                }

                if (!ValidateCertificateValidityDates(rootCert, result.errors))
                {
                    result.valid = false;
                    return result;
                }

                // 5. Verify certificate signatures in chain
                if (intermediateCert != null)
                {
                    // Verify Root → Intermediate signature
                    if (!VerifyCertificateSignature(intermediateCert, rootCert, result.errors))
                    {
                        result.valid = false;
                        return result;
                    }

                    // Verify Intermediate → Publisher signature
                    if (!VerifyCertificateSignature(publisherCert, intermediateCert, result.errors))
                    {
                        result.valid = false;
                        return result;
                    }
                }
                else
                {
                    // Verify Root → Publisher signature (direct, no intermediate)
                    if (!VerifyCertificateSignature(publisherCert, rootCert, result.errors))
                    {
                        result.valid = false;
                        return result;
                    }
                }

                // 6. Extract publisherId from Publisher certificate
                if (string.IsNullOrEmpty(publisherCert.publisherId))
                {
                    result.valid = false;
                    result.errors.Add("Publisher certificate missing publisherId");
                    return result;
                }

                result.publisherId = publisherCert.publisherId;

                // All checks passed
                result.valid = true;
                return result;
            }
            catch (Exception ex)
            {
                result.valid = false;
                result.errors.Add($"Certificate chain validation error: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Verify that a certificate is signed by its issuer
        /// </summary>
        private static bool VerifyCertificateSignature(CertificateData certificate, CertificateData issuer, List<string> errors)
        {
            try
            {
                // Verify issuerKeyId matches
                if (string.IsNullOrEmpty(certificate.issuerKeyId))
                {
                    errors.Add($"Certificate '{certificate.keyId}' missing issuerKeyId");
                    return false;
                }

                if (certificate.issuerKeyId != issuer.keyId)
                {
                    errors.Add($"Certificate '{certificate.keyId}' issuerKeyId '{certificate.issuerKeyId}' does not match issuer keyId '{issuer.keyId}'");
                    return false;
                }

                // Parse public key from issuer
                byte[] issuerPublicKey = ParsePublicKey(issuer.publicKey);
                if (issuerPublicKey == null)
                {
                    errors.Add($"Issuer certificate '{issuer.keyId}' has invalid publicKey format");
                    return false;
                }

                // Parse signature from certificate
                if (string.IsNullOrEmpty(certificate.signature))
                {
                    errors.Add($"Certificate '{certificate.keyId}' missing signature");
                    return false;
                }

                byte[] signatureBytes;
                try
                {
                    signatureBytes = Convert.FromBase64String(certificate.signature);
                    if (signatureBytes.Length != 64)
                    {
                        errors.Add($"Certificate '{certificate.keyId}' signature has invalid length: {signatureBytes.Length} (expected 64)");
                        return false;
                    }
                }
                catch (FormatException)
                {
                    errors.Add($"Certificate '{certificate.keyId}' has invalid signature format (not base64)");
                    return false;
                }

                // Get canonical serialization of certificate (without signature field)
                byte[] certificateData = certificate.GetSerializedData();

                // Verify signature
                bool signatureValid = Ed25519Wrapper.Verify(certificateData, signatureBytes, issuerPublicKey);
                if (!signatureValid)
                {
                    errors.Add($"Certificate '{certificate.keyId}' signature verification failed (not signed by '{issuer.keyId}')");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errors.Add($"Error verifying certificate signature: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Validate certificate validity dates
        /// </summary>
        private static bool ValidateCertificateValidityDates(CertificateData certificate, List<string> errors)
        {
            try
            {
                DateTime now = DateTime.UtcNow;

                if (!string.IsNullOrEmpty(certificate.notBefore))
                {
                    if (DateTime.TryParse(certificate.notBefore, out DateTime notBefore))
                    {
                        if (now < notBefore)
                        {
                            errors.Add($"Certificate '{certificate.keyId}' is not yet valid (valid from {notBefore:O})");
                            return false;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[CertificateChainValidator] Certificate '{certificate.keyId}' has invalid notBefore date format: {certificate.notBefore}");
                    }
                }

                if (!string.IsNullOrEmpty(certificate.notAfter))
                {
                    if (DateTime.TryParse(certificate.notAfter, out DateTime notAfter))
                    {
                        if (now > notAfter)
                        {
                            errors.Add($"Certificate '{certificate.keyId}' has expired (expired on {notAfter:O})");
                            return false;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[CertificateChainValidator] Certificate '{certificate.keyId}' has invalid notAfter date format: {certificate.notAfter}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errors.Add($"Error validating certificate validity dates: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Parse base64-encoded public key to byte array
        /// </summary>
        private static byte[] ParsePublicKey(string publicKeyBase64)
        {
            if (string.IsNullOrEmpty(publicKeyBase64))
            {
                return null;
            }

            try
            {
                byte[] keyBytes = Convert.FromBase64String(publicKeyBase64);
                if (keyBytes.Length != 32)
                {
                    return null;
                }
                return keyBytes;
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}

