using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    public static class ProtectedAssetUnlockService
    {
        private const string SessionKeyPrefix = "yucp.protected-unlock.";
        private const int DiskCacheDays = 30;
        private static readonly HashSet<string> KnownSessionKeys = new HashSet<string>(StringComparer.Ordinal);

        private static string CacheDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".unitysign", "protected-unlocks");

        internal static void ClearAll(IEnumerable<(string packageId, string protectedAssetId)> cacheKeys)
        {
            if (!EditorMainThreadDispatcher.IsMainThread)
            {
                List<(string packageId, string protectedAssetId)> cacheKeyList = cacheKeys != null
                    ? new List<(string packageId, string protectedAssetId)>(cacheKeys)
                    : null;
                EditorMainThreadDispatcher.Invoke(() => ClearAll(cacheKeyList));
                return;
            }

            var sessionKeys = new HashSet<string>(KnownSessionKeys, StringComparer.Ordinal);
            if (cacheKeys != null)
            {
                foreach (var cacheKey in cacheKeys)
                {
                    if (!string.IsNullOrWhiteSpace(cacheKey.packageId) && !string.IsNullOrWhiteSpace(cacheKey.protectedAssetId))
                    {
                        sessionKeys.Add(GetSessionKey(cacheKey.packageId, cacheKey.protectedAssetId));
                    }
                    Evict(cacheKey.packageId, cacheKey.protectedAssetId);
                }
            }

            foreach (string sessionKey in sessionKeys)
            {
                SessionState.EraseString(sessionKey);
            }

            KnownSessionKeys.Clear();

            try
            {
                if (Directory.Exists(CacheDir))
                {
                    Directory.Delete(CacheDir, true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP License] Could not clear protected unlock cache directory: {ex.Message}");
            }
        }

        public static bool TryAuthorizePackage(
            string packageId,
            string protectedAssetId,
            out string wrappedContentKey,
            out string error)
        {
            wrappedContentKey = null;
            ProtectedAssetUnlockGrant grant;
            if (!TryAuthorizePackage(packageId, protectedAssetId, out grant, out error))
                return false;

            wrappedContentKey = grant?.wrappedContentKey;
            return true;
        }

        public static bool TryAuthorizePackage(
            string packageId,
            string protectedAssetId,
            out ProtectedAssetUnlockGrant grant,
            out string error)
        {
            grant = null;
            error = null;

            if (string.IsNullOrEmpty(packageId))
            {
                error = "Package ID is missing.";
                return false;
            }

            string licenseToken = LicenseTokenCache.GetValidToken(packageId);
            if (string.IsNullOrEmpty(licenseToken))
            {
                error = "Please import the package through the YUCP Package Manager and verify your purchase first.";
                return false;
            }

            if (string.IsNullOrEmpty(protectedAssetId))
            {
                grant = new ProtectedAssetUnlockGrant();
                return true;
            }

            if (TryGetCachedUnlock(packageId, protectedAssetId, out grant))
                return true;

            string projectId = ProjectIdentityService.GetOrCreateProjectId();
            if (string.IsNullOrEmpty(projectId))
            {
                error = "Could not create the Unity project identity required for protected asset unlock.";
                return false;
            }

            string machineFingerprint = MachineFingerprintService.GetFingerprint();
            string serverUrl = LicenseServerResolver.GetLicenseServerUrl();
            string bodyJson = JsonUtility.ToJson(new UnlockRequest
            {
                packageId = packageId,
                protectedAssetId = protectedAssetId,
                projectId = projectId,
                machineFingerprint = machineFingerprint,
                licenseToken = licenseToken,
            });

            using var request = new UnityWebRequest($"{serverUrl.TrimEnd('/')}/v1/licenses/unlock-protected", "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(bodyJson));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 20;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept-Encoding", "identity");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                Thread.Sleep(20);
            }

            string responseText = request.downloadHandler?.text ?? string.Empty;
            if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
            {
                var failure = JsonUtility.FromJson<UnlockResponse>(responseText);
                error = !string.IsNullOrEmpty(failure?.error)
                    ? failure.error
                    : $"Protected asset unlock failed (HTTP {request.responseCode}).";
                return false;
            }

            var response = JsonUtility.FromJson<UnlockResponse>(responseText);
            if (response == null || string.IsNullOrEmpty(response.unlockToken))
            {
                error = "Protected asset unlock returned an invalid response.";
                return false;
            }

            if (!YucpJwtTokenUtility.TryValidateProtectedUnlockToken(response.unlockToken, packageId, protectedAssetId, out var claims))
            {
                error = "Protected asset unlock token failed signature or claim validation.";
                return false;
            }

            grant = ProtectedAssetUnlockGrant.FromClaims(claims);
            StoreUnlock(packageId, protectedAssetId, response.unlockToken);
            return true;
        }

        private static bool TryGetCachedUnlock(string packageId, string protectedAssetId, out ProtectedAssetUnlockGrant grant)
        {
            if (!EditorMainThreadDispatcher.IsMainThread)
            {
                ProtectedAssetUnlockGrant marshaledGrant = null;
                bool result = EditorMainThreadDispatcher.Invoke(() => TryGetCachedUnlock(packageId, protectedAssetId, out marshaledGrant));
                grant = marshaledGrant;
                return result;
            }

            grant = null;

            string sessionKey = GetSessionKey(packageId, protectedAssetId);
            string sessionToken = SessionState.GetString(sessionKey, null);
            if (!string.IsNullOrEmpty(sessionToken) &&
                YucpJwtTokenUtility.TryValidateProtectedUnlockToken(sessionToken, packageId, protectedAssetId, out var sessionClaims))
            {
                TrackSessionKey(sessionKey);
                grant = ProtectedAssetUnlockGrant.FromClaims(sessionClaims);
                return true;
            }

            string path = CacheFilePath(packageId, protectedAssetId);
            if (!File.Exists(path))
                return false;

            try
            {
                string json = Decrypt(File.ReadAllBytes(path));
                var cached = JsonUtility.FromJson<CachedUnlock>(json);
                if (cached == null)
                    return false;

                long diskExpiry = cached.cachedAt + (long)DiskCacheDays * 86400;
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > diskExpiry)
                    return false;

                if (!YucpJwtTokenUtility.TryValidateProtectedUnlockToken(cached.token, packageId, protectedAssetId, out var diskClaims))
                    return false;

                SessionState.SetString(sessionKey, cached.token);
                TrackSessionKey(sessionKey);
                grant = ProtectedAssetUnlockGrant.FromClaims(diskClaims);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void StoreUnlock(string packageId, string protectedAssetId, string unlockToken)
        {
            if (!EditorMainThreadDispatcher.IsMainThread)
            {
                EditorMainThreadDispatcher.Invoke(() => StoreUnlock(packageId, protectedAssetId, unlockToken));
                return;
            }

            string sessionKey = GetSessionKey(packageId, protectedAssetId);
            SessionState.SetString(sessionKey, unlockToken);
            TrackSessionKey(sessionKey);

            try
            {
                Directory.CreateDirectory(CacheDir);
                byte[] blob = Encrypt(JsonUtility.ToJson(new CachedUnlock
                {
                    token = unlockToken,
                    packageId = packageId,
                    protectedAssetId = protectedAssetId,
                    cachedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                }));
                File.WriteAllBytes(CacheFilePath(packageId, protectedAssetId), blob);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP License] Could not persist protected unlock token: {ex.Message}");
            }
        }

        private static void Evict(string packageId, string protectedAssetId)
        {
            if (!EditorMainThreadDispatcher.IsMainThread)
            {
                EditorMainThreadDispatcher.Invoke(() => Evict(packageId, protectedAssetId));
                return;
            }

            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(protectedAssetId))
                return;

            string sessionKey = GetSessionKey(packageId, protectedAssetId);
            SessionState.EraseString(sessionKey);
            ForgetSessionKey(sessionKey);

            string path = CacheFilePath(packageId, protectedAssetId);
            if (!File.Exists(path))
                return;

            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP License] Could not delete protected unlock cache '{path}': {ex.Message}");
            }
        }

        private static string GetSessionKey(string packageId, string protectedAssetId)
        {
            return $"{SessionKeyPrefix}{packageId}:{protectedAssetId}";
        }

        private static void TrackSessionKey(string sessionKey)
        {
            if (!string.IsNullOrWhiteSpace(sessionKey))
            {
                KnownSessionKeys.Add(sessionKey);
            }
        }

        private static void ForgetSessionKey(string sessionKey)
        {
            if (!string.IsNullOrWhiteSpace(sessionKey))
            {
                KnownSessionKeys.Remove(sessionKey);
            }
        }

        private static string CacheFilePath(string packageId, string protectedAssetId)
        {
            string safePackageId = packageId.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
            string safeAssetId = protectedAssetId.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
            return Path.Combine(CacheDir, $"{safePackageId}__{safeAssetId}.dat");
        }

        private static byte[] Encrypt(string plaintext)
        {
            byte[] data = Encoding.UTF8.GetBytes(plaintext);

            using var aes = Aes.Create();
            aes.Key = DeriveKey("YUCP_UNLOCK_ENC");
            aes.Mode = CipherMode.CBC;
            aes.GenerateIV();

            byte[] cipher;
            using (var encryptor = aes.CreateEncryptor())
            using (var ms = new MemoryStream())
            {
                ms.Write(aes.IV, 0, aes.IV.Length);
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    cs.Write(data, 0, data.Length);
                cipher = ms.ToArray();
            }

            using var hmac = new HMACSHA256(DeriveKey("YUCP_UNLOCK_MAC"));
            byte[] tag = hmac.ComputeHash(cipher);
            var result = new byte[tag.Length + cipher.Length];
            Buffer.BlockCopy(tag, 0, result, 0, tag.Length);
            Buffer.BlockCopy(cipher, 0, result, tag.Length, cipher.Length);
            return result;
        }

        private static string Decrypt(byte[] blob)
        {
            if (blob == null || blob.Length < 32 + 16 + 1)
                return null;

            byte[] storedTag = new byte[32];
            Buffer.BlockCopy(blob, 0, storedTag, 0, storedTag.Length);
            byte[] cipher = new byte[blob.Length - storedTag.Length];
            Buffer.BlockCopy(blob, storedTag.Length, cipher, 0, cipher.Length);

            using var hmac = new HMACSHA256(DeriveKey("YUCP_UNLOCK_MAC"));
            byte[] expectedTag = hmac.ComputeHash(cipher);
            if (!CryptographicEqual(storedTag, expectedTag))
                return null;

            byte[] iv = new byte[16];
            Buffer.BlockCopy(cipher, 0, iv, 0, iv.Length);

            using var aes = Aes.Create();
            aes.Key = DeriveKey("YUCP_UNLOCK_ENC");
            aes.Mode = CipherMode.CBC;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(cipher, 16, cipher.Length - 16);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var result = new MemoryStream();
            cs.CopyTo(result);
            return Encoding.UTF8.GetString(result.ToArray());
        }

        private static byte[] DeriveKey(string salt)
        {
            string material = $"{Environment.MachineName}|{Environment.UserName}|{salt}";
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(material));
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

        [Serializable]
        private class UnlockRequest
        {
            public string packageId;
            public string protectedAssetId;
            public string projectId;
            public string machineFingerprint;
            public string licenseToken;
        }

        [Serializable]
        private class UnlockResponse
        {
            public bool success;
            public string unlockToken;
            public long expiresAt;
            public string error;
        }

        [Serializable]
        private class CachedUnlock
        {
            public string token;
            public string packageId;
            public string protectedAssetId;
            public long cachedAt;
        }

        public sealed class ProtectedAssetUnlockGrant
        {
            public string unlockMode;
            public string wrappedContentKey;
            public string contentKeyBase64;

            internal static ProtectedAssetUnlockGrant FromClaims(YucpJwtTokenUtility.ProtectedUnlockTokenClaims claims)
            {
                return new ProtectedAssetUnlockGrant
                {
                    unlockMode = claims?.unlock_mode ?? "",
                    wrappedContentKey = claims?.wrapped_content_key ?? "",
                    contentKeyBase64 = claims?.content_key_b64 ?? "",
                };
            }
        }
    }
}
