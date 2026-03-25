using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    /// <summary>
    /// Stores and retrieves license verification tokens for YUCP packages.
    ///
    /// Two-tier caching:
    ///   1. SessionState (in-memory, per-Unity-session) — instant, no I/O.
    ///   2. Disk cache (AES-256-CBC + HMAC-SHA256 authenticated) at
    ///      ~/.unitysign/licenses/{packageId_safe}.dat — survives Editor restarts,
    ///      valid for up to 30 days.
    ///
    /// The disk key and MAC key are derived from machine properties, making the
    /// encrypted blob non-transferable to other machines without re-verification.
    ///
    /// Security note: a local attacker who knows the derivation formula CAN derive
    /// the keys since all inputs (machine name, username) are accessible to any
    /// process on the machine.  The practical protection is against cross-machine
    /// license sharing, not against a fully compromised local environment.
    /// </summary>
    internal static class LicenseTokenCache
    {
        // SessionState keys follow the pattern used by YucpOAuthService
        private const string SessionKeyPrefix = "yucp.license.";
        private const int DiskCacheDays = 30;
        private static readonly HashSet<string> KnownSessionKeys = new HashSet<string>(StringComparer.Ordinal);

        // ── Path helpers ─────────────────────────────────────────────────────

        private static string CacheDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".unitysign", "licenses");

        private static string CacheFilePath(string packageId)
        {
            // URL-encode the package ID to make it a safe filename
            string safe = packageId.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
            return Path.Combine(CacheDir, $"{safe}.dat");
        }

        private static string GetSessionKey(string packageId)
        {
            return SessionKeyPrefix + packageId;
        }

        // ── Key derivation ───────────────────────────────────────────────────

        private static byte[] DeriveKey(string salt)
        {
            string material = $"{Environment.MachineName}|{Environment.UserName}|{salt}";
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(material));
        }

        private static byte[] EncKey => DeriveKey("YUCP_LICENSE_ENC");
        private static byte[] MacKey => DeriveKey("YUCP_LICENSE_MAC");

        // ── Encryption helpers (AES-256-CBC + HMAC-SHA256) ──────────────────

        private static byte[] Encrypt(string plaintext)
        {
            byte[] data = Encoding.UTF8.GetBytes(plaintext);

            using var aes = Aes.Create();
            aes.Key = EncKey;
            aes.Mode = CipherMode.CBC;
            aes.GenerateIV();

            byte[] cipher;
            using (var encryptor = aes.CreateEncryptor())
            using (var ms = new MemoryStream())
            {
                ms.Write(aes.IV, 0, aes.IV.Length);
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    cs.Write(data, 0, data.Length);
                cipher = ms.ToArray(); // IV (16) + ciphertext
            }

            // Prepend HMAC-SHA256 over the IV+ciphertext for integrity
            using var hmac = new HMACSHA256(MacKey);
            byte[] tag = hmac.ComputeHash(cipher);

            var result = new byte[tag.Length + cipher.Length]; // 32 + (16 + ct)
            Buffer.BlockCopy(tag, 0, result, 0, tag.Length);
            Buffer.BlockCopy(cipher, 0, result, tag.Length, cipher.Length);
            return result;
        }

        private static string Decrypt(byte[] blob)
        {
            if (blob == null || blob.Length < 32 + 16 + 1) return null;

            // Verify HMAC
            byte[] storedTag = new byte[32];
            Buffer.BlockCopy(blob, 0, storedTag, 0, 32);
            byte[] cipher = new byte[blob.Length - 32];
            Buffer.BlockCopy(blob, 32, cipher, 0, cipher.Length);

            using var hmac = new HMACSHA256(MacKey);
            byte[] expectedTag = hmac.ComputeHash(cipher);
            if (!CryptographicEqual(storedTag, expectedTag)) return null; // tampered

            // Decrypt
            byte[] iv = new byte[16];
            Buffer.BlockCopy(cipher, 0, iv, 0, 16);

            using var aes = Aes.Create();
            aes.Key = EncKey;
            aes.Mode = CipherMode.CBC;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(cipher, 16, cipher.Length - 16);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var result = new MemoryStream();
            cs.CopyTo(result);
            return Encoding.UTF8.GetString(result.ToArray());
        }

        private static bool CryptographicEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns a valid, non-expired license token for the given package,
        /// or null if none is cached. Also checks that machine fingerprint
        /// and package_id claims match the current machine / package.
        /// </summary>
        public static string GetValidToken(string packageId)
        {
            if (!EditorMainThreadDispatcher.IsMainThread)
                return EditorMainThreadDispatcher.Invoke(() => GetValidToken(packageId));

            // 1. Check in-session cache first
            string sessionKey = GetSessionKey(packageId);
            string sessionToken = SessionState.GetString(sessionKey, null);
            if (!string.IsNullOrEmpty(sessionToken) && IsTokenValid(sessionToken, packageId))
            {
                TrackSessionKey(sessionKey);
                return sessionToken;
            }

            // 2. Try disk cache
            string path = CacheFilePath(packageId);
            if (!File.Exists(path)) return null;

            try
            {
                byte[] blob = File.ReadAllBytes(path);
                string json = Decrypt(blob);
                if (json == null) return null;

                var cached = JsonUtility.FromJson<CachedLicense>(json);
                if (cached == null) return null;

                // Check disk cache expiry (independent of JWT exp)
                long diskExpiry = cached.cachedAt + (long)DiskCacheDays * 86400;
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > diskExpiry) return null;

                if (!IsTokenValid(cached.token, packageId)) return null;

                // Promote to session cache
                SessionState.SetString(sessionKey, cached.token);
                TrackSessionKey(sessionKey);
                return cached.token;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Stores a license token in both the session cache and the encrypted disk cache.
        /// </summary>
        public static void StoreToken(string packageId, string jwt)
        {
            if (!EditorMainThreadDispatcher.IsMainThread)
            {
                EditorMainThreadDispatcher.Invoke(() => StoreToken(packageId, jwt));
                return;
            }

            if (string.IsNullOrEmpty(jwt)) return;

            // Write to session state immediately
            string sessionKey = GetSessionKey(packageId);
            SessionState.SetString(sessionKey, jwt);
            TrackSessionKey(sessionKey);

            // Write to disk
            try
            {
                Directory.CreateDirectory(CacheDir);
                var cached = new CachedLicense
                {
                    token = jwt,
                    packageId = packageId,
                    cachedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                };
                byte[] blob = Encrypt(JsonUtility.ToJson(cached));
                File.WriteAllBytes(CacheFilePath(packageId), blob);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP License] Could not write disk cache: {ex.Message}");
            }
        }

        /// <summary>Evicts the cached token for a package from both tiers.</summary>
        public static void Evict(string packageId)
        {
            if (!EditorMainThreadDispatcher.IsMainThread)
            {
                EditorMainThreadDispatcher.Invoke(() => Evict(packageId));
                return;
            }

            if (string.IsNullOrEmpty(packageId))
                return;

            string sessionKey = GetSessionKey(packageId);
            SessionState.EraseString(sessionKey);
            ForgetSessionKey(sessionKey);
            string path = CacheFilePath(packageId);
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }

        internal static void ClearAll(IEnumerable<string> packageIds)
        {
            if (!EditorMainThreadDispatcher.IsMainThread)
            {
                List<string> packageIdList = packageIds != null ? new List<string>(packageIds) : null;
                EditorMainThreadDispatcher.Invoke(() => ClearAll(packageIdList));
                return;
            }

            var sessionKeys = new HashSet<string>(KnownSessionKeys, StringComparer.Ordinal);
            if (packageIds != null)
            {
                foreach (string packageId in packageIds)
                {
                    if (string.IsNullOrWhiteSpace(packageId))
                        continue;

                    sessionKeys.Add(GetSessionKey(packageId));
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
                Debug.LogWarning($"[YUCP License] Could not clear cached license directory: {ex.Message}");
            }
        }

        // ── Token validation ──────────────────────────────────────────────────

        private static bool IsTokenValid(string jwt, string expectedPackageId)
        {
            return YucpJwtTokenUtility.TryValidateLicenseToken(jwt, expectedPackageId, out _);
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

        // ── Serialization helpers ────────────────────────────────────────────

        [Serializable]
        private class CachedLicense
        {
            public string token;
            public string packageId;
            public long cachedAt;
        }

    }
}
