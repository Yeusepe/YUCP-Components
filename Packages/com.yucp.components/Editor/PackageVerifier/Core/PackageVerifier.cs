using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using YUCP.Components.Editor.PackageVerifier.Crypto;
using YUCP.Components.Editor.PackageVerifier.Data;

namespace YUCP.Components.Editor.PackageVerifier.Core
{
    /// <summary>
    /// Main package verification logic
    /// </summary>
    public static class PackageVerifier
    {
        /// <summary>
        /// Verify a signed package
        /// </summary>
        public static VerificationResult VerifyPackage(string packagePath, PackageManifest manifest, SignatureData signature)
        {
            var result = new VerificationResult();

            try
            {
                // Validate inputs
                if (manifest == null)
                {
                    result.valid = false;
                    result.errors.Add("Manifest is null");
                    return result;
                }
                
                if (signature == null)
                {
                    result.valid = false;
                    result.errors.Add("Signature is null");
                    return result;
                }
                
                // 1. Verify authority
                if (manifest.authorityId != TrustedAuthority.AuthorityId)
                {
                    result.valid = false;
                    result.errors.Add($"Invalid authority ID: {manifest.authorityId}");
                    return result;
                }

                // 2. Extract and validate certificate chain (required - no legacy support)
                if (manifest.certificateChain == null || manifest.certificateChain.Length == 0)
                {
                    result.valid = false;
                    result.errors.Add("Certificate chain is required. Package must include a valid certificate chain in the manifest.");
                    return result;
                }

                List<CertificateData> certificateChain = new List<CertificateData>(manifest.certificateChain);

                if (manifest.certificateChain == null)
                {
                    Debug.LogWarning("[PackageVerifier] manifest.certificateChain is null");
                }

                // 3. Validate certificate chain
                var chainValidationResult = CertificateChainValidator.ValidateChain(manifest, certificateChain);
                if (!chainValidationResult.valid)
                {
                    result.valid = false;
                    result.errors.AddRange(chainValidationResult.errors);
                    return result;
                }

                // 4. Get the certificate that signed the manifest
                // The certificateIndex from the server response is the authoritative source
                int certIndex = 0; // Default to Publisher certificate (index 0)
                
                // Primary: Use certificateIndex from server response if valid
                if (signature.certificateIndex >= 0 && signature.certificateIndex < certificateChain.Count)
                {
                    certIndex = signature.certificateIndex;
                }
                // Fallback: Try to match signature.keyId if certificateIndex is not provided or invalid
                else if (!string.IsNullOrEmpty(signature.keyId))
                {
                    for (int i = 0; i < certificateChain.Count; i++)
                    {
                        if (certificateChain[i].keyId == signature.keyId)
                        {
                            certIndex = i;
                            break;
                        }
                    }
                }

                CertificateData signingCertificate = certificateChain[certIndex];
                if (signingCertificate == null)
                {
                    result.valid = false;
                    result.errors.Add($"Certificate at index {certIndex} not found in chain");
                    return result;
                }

                // 5. Parse public key from signing certificate
                byte[] publicKey;
                try
                {
                    if (string.IsNullOrEmpty(signingCertificate.publicKey))
                    {
                        result.valid = false;
                        result.errors.Add($"Signing certificate '{signingCertificate.keyId}' missing publicKey");
                        return result;
                    }
                    publicKey = Convert.FromBase64String(signingCertificate.publicKey);
                    if (publicKey.Length != 32)
                    {
                        result.valid = false;
                        result.errors.Add($"Signing certificate '{signingCertificate.keyId}' has invalid publicKey length: {publicKey.Length} (expected 32)");
                        return result;
                    }
                }
                catch (FormatException)
                {
                    result.valid = false;
                    result.errors.Add($"Signing certificate '{signingCertificate.keyId}' has invalid publicKey format");
                    return result;
                }

                // 6. Canonicalize manifest (INCLUDING certificateChain - server signs with chain included)
                // The server adds the certificate chain to the manifest before signing it
                string canonicalJson = CanonicalizeManifest(manifest);
                byte[] manifestBytes = Encoding.UTF8.GetBytes(canonicalJson);

                // 7. Verify manifest signature using signing certificate's public key
                if (string.IsNullOrEmpty(signature?.signature))
                {
                    result.valid = false;
                    result.errors.Add("Signature data is missing or invalid");
                    return result;
                }
                
                byte[] signatureBytes;
                try
                {
                    signatureBytes = Convert.FromBase64String(signature.signature);
                    if (signatureBytes.Length != 64)
                    {
                        result.valid = false;
                        result.errors.Add($"Signature has invalid length: {signatureBytes.Length} (expected 64)");
                        return result;
                    }
                }
                catch (FormatException)
                {
                    result.valid = false;
                    result.errors.Add("Signature has invalid format (not base64)");
                    return result;
                }

                bool signatureValid = Ed25519Wrapper.Verify(manifestBytes, signatureBytes, publicKey);

                if (!signatureValid)
                {
                    result.valid = false;
                    result.errors.Add($"Invalid manifest signature (not signed by certificate '{signingCertificate.keyId}')");
                    return result;
                }

                // 8. Verify package integrity (canonical hash over contents, excluding signing data)
                if (!File.Exists(packagePath))
                {
                    result.valid = false;
                    result.errors.Add("Package file not found");
                    return result;
                }

                string computedHash = ComputePackageHashExcludingSigningData(packagePath);
                if (string.IsNullOrEmpty(computedHash))
                {
                    result.valid = false;
                    result.errors.Add("Failed to compute package hash (SharpZipLib may be missing)");
                    return result;
                }

                if (computedHash != manifest.archiveSha256)
                {
                    result.valid = false;
                    result.errors.Add("Package hash mismatch");
                    return result;
                }

                // All checks passed (certificate chain, signature, and content hash)
                result.valid = true;
                // Use publisherId from certificate (not manifest) - cannot be forged
                result.publisherId = chainValidationResult.publisherId;
                result.packageId = manifest.packageId;
                result.version = manifest.version;
                result.vrchatAuthorUserId = manifest.vrchatAuthorUserId;

                return result;
            }
            catch (Exception ex)
            {
                result.valid = false;
                result.errors.Add($"Verification error: {ex.Message}");
                return result;
            }
        }


        private static string CanonicalizeManifest(PackageManifest manifest)
        {
            // Server uses canonicalizeJson which recursively sorts keys at all levels
            // and uses JSON.stringify for primitives (null becomes "null" string)
            // We need to match this exactly
            if (manifest == null)
            {
                Debug.LogError("[PackageVerifier] Manifest is null!");
                return "null";
            }
            return CanonicalizeJsonRecursive(manifest);
        }
        
        /// <summary>
        /// Recursively canonicalize JSON to match server's canonicalizeJson function
        /// Sorts keys alphabetically at all levels, uses JSON.stringify for primitives
        /// </summary>
        private static string CanonicalizeJsonRecursive(object obj)
        {
            if (obj == null)
            {
                return "null";
            }
            
            // Get type early for enum checking
            var objType = obj.GetType();
            
            if (obj is System.Collections.IList list)
            {
                var items = new System.Collections.Generic.List<string>();
                foreach (var item in list)
                {
                    items.Add(CanonicalizeJsonRecursive(item));
                }
                return "[" + string.Join(",", items) + "]";
            }
            
            if (obj is System.Collections.IDictionary dict)
            {
                var sortedKeys = new System.Collections.Generic.List<string>();
                foreach (var key in dict.Keys)
                {
                    sortedKeys.Add(key?.ToString() ?? "");
                }
                sortedKeys.Sort();
                
                var items = new System.Collections.Generic.List<string>();
                foreach (var key in sortedKeys)
                {
                    var value = dict[key];
                    items.Add($"\"{key}\":{CanonicalizeJsonRecursive(value)}");
                }
                return "{" + string.Join(",", items) + "}";
            }
            
            // For other objects, use reflection to get fields/properties and sort them
            if (objType.IsClass && objType != typeof(string))
            {
                var items = new System.Collections.Generic.List<string>();
                
                // Try fields first (for Serializable classes with public fields like PackageManifest)
                var fields = objType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                
                if (fields != null && fields.Length > 0)
                {
                    var sortedFields = new System.Collections.Generic.List<FieldInfo>(fields);
                    sortedFields.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.Ordinal));
                    
                    foreach (var field in sortedFields)
                    {
                        var value = field.GetValue(obj);
                        
                        // Normalize null dictionaries to empty dictionaries to match server behavior
                        // (ManifestBuilder creates empty dict, not null, so server signs with {})
                        if (value == null && field.FieldType.IsGenericType && 
                            field.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        {
                            // Create empty dictionary of the same type
                            var dictType = typeof(Dictionary<,>).MakeGenericType(
                                field.FieldType.GetGenericArguments());
                            value = System.Activator.CreateInstance(dictType);
                        }
                        
                        // Include all values (null becomes "null" string, matching server behavior)
                        items.Add($"\"{field.Name}\":{CanonicalizeJsonRecursive(value)}");
                    }
                }
                else
                {
                    // Fallback to properties
                    var properties = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    var sortedProps = new System.Collections.Generic.List<PropertyInfo>(properties);
                    sortedProps.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.Ordinal));
                    
                    foreach (var prop in sortedProps)
                    {
                        var value = prop.GetValue(obj);
                        
                        // Normalize null dictionaries to empty dictionaries to match server behavior
                        if (value == null && prop.PropertyType.IsGenericType && 
                            prop.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        {
                            var dictType = typeof(Dictionary<,>).MakeGenericType(
                                prop.PropertyType.GetGenericArguments());
                            value = System.Activator.CreateInstance(dictType);
                        }
                        
                        // Include all values (null becomes "null" string)
                        items.Add($"\"{prop.Name}\":{CanonicalizeJsonRecursive(value)}");
                    }
                }
                
                return "{" + string.Join(",", items) + "}";
            }
            
            // Primitive values - use JSON.stringify equivalent
            // JSON.stringify in JavaScript: null -> "null", string -> "\"value\"", number -> "123", bool -> "true"/"false"
            if (obj is string str)
            {
                // Escape JSON string properly (match JSON.stringify behavior)
                var escaped = str
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t")
                    .Replace("\b", "\\b")
                    .Replace("\f", "\\f");
                return "\"" + escaped + "\"";
            }
            
            if (obj is bool b)
            {
                return b ? "true" : "false";
            }
            
            // Handle enums - they need to be quoted like strings (matching JSON.stringify behavior)
            // The server uses JSON.stringify('Publisher') which returns "Publisher" (quoted)
            if (objType != null && objType.IsEnum)
            {
                string enumValue = obj.ToString();
                var escaped = enumValue
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t")
                    .Replace("\b", "\\b")
                    .Replace("\f", "\\f");
                return "\"" + escaped + "\"";
            }
            
            if (obj is System.IConvertible && !(obj is string))
            {
                // Numbers, etc. - convert to string (JSON.stringify for numbers just outputs the number as-is)
                if (obj is float || obj is double || obj is decimal)
                {
                    return System.Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture);
                }
                return System.Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture);
            }
            
            // Fallback - treat as string
            return "\"" + (obj?.ToString() ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string ComputeFileHash(string filePath)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Compute canonical package hash excluding signing data files.
        /// Mirrors the exporter:
        /// - Decompress .unitypackage
        /// - Enumerate all assets
        /// - Ignore Assets/_Signing/*
        /// - Hash UTF8(pathname) + 0x00 + asset-bytes in sorted pathname order
        /// </summary>
        private static string ComputePackageHashExcludingSigningData(string packagePath)
        {
            var tarArchiveType = Type.GetType("ICSharpCode.SharpZipLib.Tar.TarArchive, ICSharpCode.SharpZipLib");
            var gzipInputStreamType = Type.GetType("ICSharpCode.SharpZipLib.GZip.GZipInputStream, ICSharpCode.SharpZipLib");
            
            if (tarArchiveType != null && gzipInputStreamType != null)
            {
                try
                {
                    // Extract package to temp directory
                    string tempExtractDir = Path.Combine(Path.GetTempPath(), $"YUCP_Hash_{Guid.NewGuid():N}");
                    Directory.CreateDirectory(tempExtractDir);
                    
                    try
                    {
                        using (var fileStream = File.OpenRead(packagePath))
                        {
                            var gzipCtor = gzipInputStreamType.GetConstructor(new[] { typeof(Stream) });
                            var gzipStream = gzipCtor.Invoke(new object[] { fileStream });
                            
                            var createInputMethod = tarArchiveType.GetMethod(
                                "CreateInputTarArchive",
                                BindingFlags.Public | BindingFlags.Static,
                                null,
                                new[] { typeof(Stream), typeof(Encoding) },
                                null
                            );
                            var tarArchive = createInputMethod.Invoke(null, new object[] { gzipStream, Encoding.UTF8 });
                            
                            var extractMethod = tarArchiveType.GetMethod(
                                "ExtractContents",
                                BindingFlags.Public | BindingFlags.Instance,
                                null,
                                new[] { typeof(string) },
                                null
                            );
                            extractMethod.Invoke(tarArchive, new object[] { tempExtractDir });
                        }

                        // Collect all non-signing assets
                        var entries = new System.Collections.Generic.List<(string pathname, string assetPath)>();

                        string[] folders = Directory.GetDirectories(tempExtractDir);
                        foreach (string folder in folders)
                        {
                            string pathnameFile = Path.Combine(folder, "pathname");
                            string assetFile = Path.Combine(folder, "asset");

                            if (!File.Exists(pathnameFile) || !File.Exists(assetFile))
                                continue;

                            string pathname = File.ReadAllText(pathnameFile).Trim().Replace('\\', '/');

                            // Skip signing data
                            if (pathname.StartsWith("Assets/_Signing/", StringComparison.OrdinalIgnoreCase))
                                continue;

                            entries.Add((pathname, assetFile));
                        }

                        // Sort for determinism
                        entries.Sort((a, b) => string.CompareOrdinal(a.pathname, b.pathname));

                        using (var sha256 = SHA256.Create())
                        {
                            foreach (var entry in entries)
                            {
                                byte[] pathBytes = Encoding.UTF8.GetBytes(entry.pathname);
                                sha256.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);

                                byte[] sep = new byte[] { 0x00 };
                                sha256.TransformBlock(sep, 0, 1, null, 0);

                                byte[] data = File.ReadAllBytes(entry.assetPath);
                                sha256.TransformBlock(data, 0, data.Length, null, 0);
                            }

                            sha256.TransformFinalBlock(System.Array.Empty<byte>(), 0, 0);
                            return BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant();
                        }
                    }
                    finally
                    {
                        if (Directory.Exists(tempExtractDir))
                        {
                            try { Directory.Delete(tempExtractDir, true); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PackageVerifier] Failed to compute hash excluding signing data: {ex.Message}");
                }
            }
            
            // If we reach here, we couldn't compute the canonical hash
            Debug.LogWarning("[PackageVerifier] SharpZipLib not available or failed - cannot compute canonical package hash.");
            return null;
        }
    }

    /// <summary>
    /// Result of package verification
    /// </summary>
    public class VerificationResult
    {
        public bool valid;
        public System.Collections.Generic.List<string> errors = new System.Collections.Generic.List<string>();
        public string publisherId;
        public string packageId;
        public string version;
        public string vrchatAuthorUserId;
    }
}


