using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    /// <summary>
    /// Two-tier cache for license verification tokens: an in-session memory tier and a
    /// disk tier under ~/.unitysign/licenses, valid for up to 30 days. The disk blob is
    /// sealed per-user so it is not transferable to another machine or user. Package and
    /// machine claims are re-validated after every read.
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

        // ── At-rest protection (Windows DPAPI, per-user) ─────────────────────
        // The blob is sealed to the current Windows user via crypt32 DPAPI, so it can only be
        // unsealed by the same user on the same machine, and the key is not re-derivable from
        // public machine properties. DPAPI also authenticates: tampering fails the unseal.

        private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("YUCP.License.Cache.v2");

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref DataBlob pDataIn, string szDescription, ref DataBlob pEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDescription, ref DataBlob pEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        private static DataBlob ToBlob(byte[] data)
        {
            var blob = new DataBlob { cbData = data?.Length ?? 0, pbData = IntPtr.Zero };
            if (data != null && data.Length > 0)
            {
                blob.pbData = Marshal.AllocHGlobal(data.Length);
                Marshal.Copy(data, 0, blob.pbData, data.Length);
            }
            return blob;
        }

        private static byte[] FromBlob(DataBlob blob)
        {
            if (blob.pbData == IntPtr.Zero || blob.cbData <= 0) return Array.Empty<byte>();
            byte[] data = new byte[blob.cbData];
            Marshal.Copy(blob.pbData, data, 0, blob.cbData);
            return data;
        }

        private static byte[] Encrypt(string plaintext)
        {
            DataBlob inBlob = ToBlob(Encoding.UTF8.GetBytes(plaintext));
            DataBlob entropy = ToBlob(DpapiEntropy);
            var outBlob = new DataBlob();
            try
            {
                if (!CryptProtectData(ref inBlob, null, ref entropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
                    throw new CryptographicException("DPAPI protect failed.");
                return FromBlob(outBlob);
            }
            finally
            {
                if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
                if (entropy.pbData != IntPtr.Zero) Marshal.FreeHGlobal(entropy.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }

        private static string Decrypt(byte[] blob)
        {
            if (blob == null || blob.Length == 0) return null;

            DataBlob inBlob = ToBlob(blob);
            DataBlob entropy = ToBlob(DpapiEntropy);
            var outBlob = new DataBlob();
            try
            {
                if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
                    return null; // tampered, wrong user/machine, or legacy/foreign format
                return Encoding.UTF8.GetString(FromBlob(outBlob));
            }
            catch
            {
                return null;
            }
            finally
            {
                if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
                if (entropy.pbData != IntPtr.Zero) Marshal.FreeHGlobal(entropy.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
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
