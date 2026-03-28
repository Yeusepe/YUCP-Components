using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using YUCP.Importer.Editor.PackageVerifier.Data;

namespace YUCP.Importer.Editor.PackageManager
{
    public static class ProtectedPayloadIntegrityUtility
    {
        public static string ComputeManifestBindingSha256(ProtectedPayloadDescriptor descriptor)
        {
            if (descriptor == null)
                return string.Empty;

            string payload = string.Join("\n", new[]
            {
                "yucp-protected-payload-binding-v1",
                NormalizeUnityPath(descriptor.formatVersion),
                NormalizeUnityPath(descriptor.protectedAssetId),
                NormalizeUnityPath(descriptor.blobAssetPath),
                NormalizeUnityPath(descriptor.cipher),
                NormalizeUnityPath(descriptor.archiveFormat),
                NormalizeUnityPath(descriptor.ciphertextSha256),
                descriptor.ciphertextSize.ToString(CultureInfo.InvariantCulture),
                NormalizeUnityPath(descriptor.plaintextSha256),
                descriptor.plaintextSize.ToString(CultureInfo.InvariantCulture),
                descriptor.entryCount.ToString(CultureInfo.InvariantCulture),
                string.Join("\n", NormalizeUnityPaths(descriptor.payloadAssetPaths)),
                descriptor.requiresOnlineUnlock ? "1" : "0",
                descriptor.requiresBrokeredMaterialization ? "1" : "0",
                descriptor.brokerProtocolVersion.ToString(CultureInfo.InvariantCulture),
            });

            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var builder = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
                builder.Append(value.ToString("x2"));
            return builder.ToString();
        }

        public static ProtectedPayloadManifestEntry CreateManifestEntry(ProtectedPayloadDescriptor descriptor)
        {
            if (descriptor == null)
                return null;

            string bindingSha256 = ComputeManifestBindingSha256(descriptor);
            return new ProtectedPayloadManifestEntry
            {
                formatVersion = descriptor.formatVersion ?? "1",
                protectedAssetId = descriptor.protectedAssetId ?? string.Empty,
                blobAssetPath = NormalizeUnityPath(descriptor.blobAssetPath),
                cipher = descriptor.cipher ?? string.Empty,
                archiveFormat = descriptor.archiveFormat ?? string.Empty,
                ciphertextSha256 = descriptor.ciphertextSha256 ?? string.Empty,
                ciphertextSize = descriptor.ciphertextSize,
                plaintextSha256 = descriptor.plaintextSha256 ?? string.Empty,
                plaintextSize = descriptor.plaintextSize,
                entryCount = descriptor.entryCount,
                payloadAssetPaths = NormalizeUnityPaths(descriptor.payloadAssetPaths),
                requiresOnlineUnlock = descriptor.requiresOnlineUnlock,
                requiresBrokeredMaterialization = descriptor.requiresBrokeredMaterialization,
                brokerProtocolVersion = descriptor.brokerProtocolVersion,
                manifestBindingSha256 = bindingSha256,
            };
        }

        public static bool DescriptorMatchesManifest(
            ProtectedPayloadDescriptor descriptor,
            ProtectedPayloadManifestEntry manifestEntry)
        {
            if (descriptor == null || manifestEntry == null)
                return false;

            string descriptorBinding = descriptor.manifestBindingSha256 ?? string.Empty;
            string manifestBinding = manifestEntry.manifestBindingSha256 ?? string.Empty;
            var comparableDescriptor = descriptor.Clone();
            comparableDescriptor.blobAssetPath = manifestEntry.blobAssetPath ?? string.Empty;
            string expectedBinding = ComputeManifestBindingSha256(comparableDescriptor);

            return string.Equals(descriptor.formatVersion ?? "1", manifestEntry.formatVersion ?? "1", StringComparison.Ordinal) &&
                   string.Equals(descriptor.protectedAssetId ?? string.Empty, manifestEntry.protectedAssetId ?? string.Empty, StringComparison.Ordinal) &&
                   BlobPathsMatch(descriptor.blobAssetPath, manifestEntry.blobAssetPath) &&
                   string.Equals(descriptor.cipher ?? string.Empty, manifestEntry.cipher ?? string.Empty, StringComparison.Ordinal) &&
                   string.Equals(descriptor.archiveFormat ?? string.Empty, manifestEntry.archiveFormat ?? string.Empty, StringComparison.Ordinal) &&
                   string.Equals(descriptor.ciphertextSha256 ?? string.Empty, manifestEntry.ciphertextSha256 ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   descriptor.ciphertextSize == manifestEntry.ciphertextSize &&
                   string.Equals(descriptor.plaintextSha256 ?? string.Empty, manifestEntry.plaintextSha256 ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   descriptor.plaintextSize == manifestEntry.plaintextSize &&
                   descriptor.entryCount == manifestEntry.entryCount &&
                   string.Equals(
                       string.Join("\n", NormalizeUnityPaths(descriptor.payloadAssetPaths)),
                       string.Join("\n", NormalizeUnityPaths(manifestEntry.payloadAssetPaths)),
                       StringComparison.Ordinal) &&
                   descriptor.requiresOnlineUnlock == manifestEntry.requiresOnlineUnlock &&
                   descriptor.requiresBrokeredMaterialization == manifestEntry.requiresBrokeredMaterialization &&
                   descriptor.brokerProtocolVersion == manifestEntry.brokerProtocolVersion &&
                    string.Equals(expectedBinding, descriptorBinding, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(expectedBinding, manifestBinding, StringComparison.OrdinalIgnoreCase);
        }

        private static bool BlobPathsMatch(string descriptorPath, string manifestPath)
        {
            string normalizedDescriptor = NormalizeUnityPath(descriptorPath);
            string normalizedManifest = NormalizeUnityPath(manifestPath);

            if (string.Equals(normalizedDescriptor, normalizedManifest, StringComparison.Ordinal))
                return true;

            if (string.IsNullOrEmpty(normalizedDescriptor) || string.IsNullOrEmpty(normalizedManifest))
                return false;

            return normalizedDescriptor.EndsWith("/" + normalizedManifest, StringComparison.Ordinal) ||
                   normalizedDescriptor.EndsWith(normalizedManifest, StringComparison.Ordinal);
        }

        public static string[] NormalizeUnityPaths(IEnumerable<string> paths)
        {
            if (paths == null)
                return Array.Empty<string>();

            return paths
                .Select(NormalizeUnityPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        public static string NormalizeUnityPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/').Trim();
        }
    }
}
