using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.PackageManager
{
    [InitializeOnLoad]
    internal static class ProtectedPayloadInstallService
    {
        private const string PendingPackageIdKey = "YUCP.PackageManager.ProtectedPayload.PackageId";
        private const string PendingStartTicksKey = "YUCP.PackageManager.ProtectedPayload.StartTicksUtc";
        private const double PendingTimeoutSeconds = 120.0;

        private static readonly byte[] BlobMagic = Encoding.ASCII.GetBytes("YUCPBLOB");
        private const byte BlobVersion = 1;
        private const string ExpectedCipher = "aes-256-cbc+hmac-sha256";
        private const string ExpectedArchiveFormat = "zip";

        private static bool _scheduled;

        static ProtectedPayloadInstallService()
        {
            SchedulePendingApply();
        }

        internal static void QueuePendingApply(InstalledPackageInfo packageInfo)
        {
            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.packageId) || packageInfo.protectedPayload == null)
                return;

            EditorPrefs.SetString(PendingPackageIdKey, packageInfo.packageId ?? "");
            EditorPrefs.SetString(PendingStartTicksKey, DateTime.UtcNow.Ticks.ToString());
            SchedulePendingApply();
        }

        internal static void CancelPendingApply(string packageId = null)
        {
            string pendingPackageId = EditorPrefs.GetString(PendingPackageIdKey, "");
            if (!string.IsNullOrWhiteSpace(packageId) &&
                !string.Equals(pendingPackageId, packageId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ClearPendingApply();
        }

        private static void SchedulePendingApply()
        {
            if (_scheduled || !HasPendingApply())
                return;

            _scheduled = true;
            EditorApplication.delayCall += TryConsumePendingApply;
        }

        private static void TryConsumePendingApply()
        {
            _scheduled = false;

            if (!HasPendingApply())
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                SchedulePendingApply();
                return;
            }

            if (InstallerCoordinationState.HasPendingInstallerHandoff())
            {
                SchedulePendingApply();
                return;
            }

            if (IsTimedOut())
            {
                Debug.LogWarning("[YUCP PackageManager] Timed out waiting to apply the protected payload after import.");
                ClearPendingApply();
                return;
            }

            string packageId = EditorPrefs.GetString(PendingPackageIdKey, "");
            var registry = InstalledPackageRegistry.Load() ?? InstalledPackageRegistry.GetOrCreate();
            var packageInfo = registry?.GetPackage(packageId);
            if (packageInfo == null)
            {
                SchedulePendingApply();
                return;
            }

            if (packageInfo.protectedPayload == null)
            {
                ClearPendingApply();
                return;
            }

            if (!ApplyProtectedPayload(packageInfo, out string error))
            {
                ClearPendingApply();
                RollbackRegisteredPackage(packageInfo);
                Debug.LogError("[YUCP PackageManager] A required package protection step failed and the import was rolled back.");
                EditorUtility.DisplayDialog(
                    "Import Failed",
                    error,
                    "OK");
                return;
            }

            Debug.Log($"[YUCP PackageManager] Prepared protected payload for '{packageInfo.packageName}'. Finalizing install.");
            ClearPendingApply();
        }

        internal static bool ApplyProtectedPayload(InstalledPackageInfo packageInfo, out string error)
        {
            error = null;

            if (packageInfo?.protectedPayload == null)
            {
                error = "Package does not contain a protected payload descriptor.";
                return false;
            }

            var descriptor = packageInfo.protectedPayload;
            if (string.IsNullOrWhiteSpace(descriptor.protectedAssetId))
            {
                error = "Protected payload descriptor is missing protectedAssetId.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(descriptor.blobAssetPath))
            {
                error = "Protected payload descriptor is missing blobAssetPath.";
                return false;
            }

            string blobAssetPath = descriptor.blobAssetPath.Replace('\\', '/');
            string blobDiskPath = ResolveAssetPathToDisk(blobAssetPath);
            if (string.IsNullOrEmpty(blobDiskPath) || !File.Exists(blobDiskPath))
            {
                error = $"Protected payload blob was not found at '{blobAssetPath}'.";
                return false;
            }

            if (descriptor.requiresBrokeredMaterialization)
            {
                if (descriptor.brokerProtocolVersion <= 0)
                {
                    error = "The package protection step could not be completed on this machine.";
                    return false;
                }
                ProtectedInstallFinalizationCoordinator.QueuePendingFinalization(packageInfo, Array.Empty<string>());
                return true;
            }

            ProtectedAssetUnlockService.ProtectedAssetUnlockGrant grant;
            if (!ProtectedAssetUnlockService.TryAuthorizePackage(
                packageInfo.packageId,
                descriptor.protectedAssetId,
                descriptor.ciphertextSha256,
                out grant,
                out error))
                return false;

            if (!string.Equals(grant?.unlockMode, "content_key_b64", StringComparison.OrdinalIgnoreCase))
            {
                error = $"Protected payload requires content_key_b64 unlock mode, but received '{grant?.unlockMode ?? ""}'.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(descriptor.ciphertextSha256) &&
                !string.Equals(grant?.contentHash, descriptor.ciphertextSha256, StringComparison.OrdinalIgnoreCase))
            {
                error = "Protected payload unlock grant did not match the package payload.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(grant.contentKeyBase64))
            {
                error = "Protected payload unlock grant did not include a content key.";
                return false;
            }

            string tempRoot = Path.Combine(Path.GetTempPath(), "YUCP-ProtectedPayload", packageInfo.packageId + "-" + Guid.NewGuid().ToString("N"));
            string archivePath = Path.Combine(tempRoot, "payload.zip");
            var extractedAssetPaths = new List<string>();

            try
            {
                Directory.CreateDirectory(tempRoot);

                if (!TryDecryptBlobToArchive(blobDiskPath, descriptor, grant.contentKeyBase64, archivePath, out error))
                    return false;

                ExtractArchiveToProject(archivePath, extractedAssetPaths);
                if (extractedAssetPaths.Count == 0)
                {
                    error = "Protected payload archive did not contain any installable entries.";
                    return false;
                }

                AssetDatabase.Refresh();
                ProtectedInstallFinalizationCoordinator.QueuePendingFinalization(packageInfo, extractedAssetPaths);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static void RollbackRegisteredPackage(InstalledPackageInfo packageInfo)
        {
            if (packageInfo == null)
                return;

            if (!ImportedAssetRollbackService.TryRollbackPackage(packageInfo, out _))
            {
                Debug.LogError("[YUCP PackageManager] The failed protected package import could not be rolled back cleanly.");
            }

            var registry = InstalledPackageRegistry.Load() ?? InstalledPackageRegistry.GetOrCreate();
            registry?.UnregisterPackage(packageInfo.packageId);
        }

        private static bool TryDecryptBlobToArchive(
            string blobDiskPath,
            ProtectedPayloadDescriptor descriptor,
            string contentKeyBase64,
            string archivePath,
            out string error)
        {
            error = null;

            if (!string.IsNullOrEmpty(descriptor.cipher) &&
                !string.Equals(descriptor.cipher, ExpectedCipher, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Unsupported protected payload cipher '{descriptor.cipher}'.";
                return false;
            }

            if (!string.IsNullOrEmpty(descriptor.archiveFormat) &&
                !string.Equals(descriptor.archiveFormat, ExpectedArchiveFormat, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Unsupported protected payload archive format '{descriptor.archiveFormat}'.";
                return false;
            }

            byte[] blobBytes = File.ReadAllBytes(blobDiskPath);
            if (!string.IsNullOrEmpty(descriptor.ciphertextSha256) &&
                !HashesEqual(descriptor.ciphertextSha256, ComputeSha256Hex(blobBytes)))
            {
                error = "Protected payload blob hash verification failed.";
                return false;
            }

            if (blobBytes.Length < BlobMagic.Length + 1 + 16 + 32 + 1)
            {
                error = "Protected payload blob is truncated.";
                return false;
            }

            for (int i = 0; i < BlobMagic.Length; i++)
            {
                if (blobBytes[i] != BlobMagic[i])
                {
                    error = "Protected payload blob has an invalid header.";
                    return false;
                }
            }

            if (blobBytes[BlobMagic.Length] != BlobVersion)
            {
                error = $"Unsupported protected payload blob version '{blobBytes[BlobMagic.Length]}'.";
                return false;
            }

            int offset = BlobMagic.Length + 1;
            byte[] iv = new byte[16];
            Buffer.BlockCopy(blobBytes, offset, iv, 0, iv.Length);
            offset += iv.Length;

            byte[] storedTag = new byte[32];
            Buffer.BlockCopy(blobBytes, offset, storedTag, 0, storedTag.Length);
            offset += storedTag.Length;

            byte[] macInput = new byte[BlobMagic.Length + 1 + iv.Length + (blobBytes.Length - offset)];
            Buffer.BlockCopy(blobBytes, 0, macInput, 0, BlobMagic.Length + 1 + iv.Length);
            Buffer.BlockCopy(blobBytes, offset, macInput, BlobMagic.Length + 1 + iv.Length, blobBytes.Length - offset);

            byte[] contentKey;
            try
            {
                contentKey = Convert.FromBase64String(contentKeyBase64);
            }
            catch (FormatException)
            {
                error = "Protected payload content key was not valid base64.";
                return false;
            }

            using (var hmac = new HMACSHA256(DeriveMacKey(contentKey)))
            {
                byte[] expectedTag = hmac.ComputeHash(macInput);
                if (!CryptographicEqual(storedTag, expectedTag))
                {
                    error = "Protected payload authentication tag verification failed.";
                    return false;
                }
            }

            using var aes = Aes.Create();
            aes.Key = contentKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            Directory.CreateDirectory(Path.GetDirectoryName(archivePath) ?? string.Empty);
            using (var output = File.Create(archivePath))
            using (var cipherStream = new MemoryStream(blobBytes, offset, blobBytes.Length - offset, writable: false))
            using (var cryptoStream = new CryptoStream(cipherStream, aes.CreateDecryptor(), CryptoStreamMode.Read))
            {
                cryptoStream.CopyTo(output);
            }

            byte[] archiveBytes = File.ReadAllBytes(archivePath);
            if (!string.IsNullOrEmpty(descriptor.plaintextSha256) &&
                !HashesEqual(descriptor.plaintextSha256, ComputeSha256Hex(archiveBytes)))
            {
                error = "Protected payload plaintext hash verification failed.";
                return false;
            }

            return true;
        }

        private static void ExtractArchiveToProject(string archivePath, List<string> extractedAssetPaths)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string projectRootWithSeparator = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.FullName))
                    continue;

                string normalizedEntryPath = entry.FullName.Replace('\\', '/').TrimStart('/');
                if (string.IsNullOrWhiteSpace(normalizedEntryPath))
                    continue;

                string destinationPath = Path.GetFullPath(Path.Combine(projectRoot, normalizedEntryPath.Replace('/', Path.DirectorySeparatorChar)));
                if (!destinationPath.StartsWith(projectRootWithSeparator, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Protected payload entry escaped the project root: {entry.FullName}");

                if (normalizedEntryPath.EndsWith("/", StringComparison.Ordinal) || normalizedEntryPath.EndsWith("\\", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                string destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);

                using (var input = entry.Open())
                using (var output = File.Create(destinationPath))
                {
                    input.CopyTo(output);
                }

                extractedAssetPaths.Add(normalizedEntryPath);
            }
        }

        private static bool HasPendingApply()
        {
            return !string.IsNullOrWhiteSpace(EditorPrefs.GetString(PendingPackageIdKey, ""));
        }

        private static bool IsTimedOut()
        {
            string startTicksValue = EditorPrefs.GetString(PendingStartTicksKey, "");
            if (!long.TryParse(startTicksValue, out long startTicks) || startTicks <= 0)
                return false;

            var startedAt = new DateTime(startTicks, DateTimeKind.Utc);
            return (DateTime.UtcNow - startedAt).TotalSeconds > PendingTimeoutSeconds;
        }

        private static void ClearPendingApply()
        {
            EditorPrefs.DeleteKey(PendingPackageIdKey);
            EditorPrefs.DeleteKey(PendingStartTicksKey);
        }

        private static string ResolveAssetPathToDisk(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            if (Path.IsPathRooted(assetPath))
                return File.Exists(assetPath) ? Path.GetFullPath(assetPath) : null;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static byte[] DeriveMacKey(byte[] contentKey)
        {
            byte[] prefix = Encoding.UTF8.GetBytes("YUCP|protected-payload|mac|");
            byte[] material = new byte[prefix.Length + contentKey.Length];
            Buffer.BlockCopy(prefix, 0, material, 0, prefix.Length);
            Buffer.BlockCopy(contentKey, 0, material, prefix.Length, contentKey.Length);
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(material);
        }

        private static string ComputeSha256Hex(byte[] data)
        {
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(data ?? Array.Empty<byte>());
            var builder = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                builder.Append(b.ToString("x2"));
            return builder.ToString();
        }

        private static bool HashesEqual(string expected, string actual)
        {
            return string.Equals((expected ?? "").Trim(), (actual ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool CryptographicEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static void TryDeleteDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return;

            try
            {
                Directory.Delete(directoryPath, true);
            }
            catch
            {
            }
        }
    }
}
