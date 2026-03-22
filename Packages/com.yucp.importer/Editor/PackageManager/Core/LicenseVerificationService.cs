using System;
using System.Collections;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    /// <summary>
    /// Calls the YUCP server to verify a Gumroad, Jinxxy, or Discord purchase license,
    /// then stores the resulting token via <see cref="LicenseTokenCache"/>.
    /// </summary>
    internal static class LicenseVerificationService
    {
        /// <summary>Verify a Gumroad or Jinxxy license key.</summary>
        public static void VerifyAsync(
            string serverUrl,
            string packageId,
            string licenseKey,
            string provider,
            string productPermalink,
            Action<string> onSuccess,
            Action<string> onError)
        {
            RunOwnerless(DoVerify(serverUrl, packageId, licenseKey, provider, productPermalink, onSuccess, onError));
        }

        /// <summary>
        /// Verify entitlement via Discord role membership.
        /// Requires the buyer to be signed in with their YUCP account first.
        /// </summary>
        public static void VerifyDiscordAsync(
            string serverUrl,
            string packageId,
            string productId,
            string creatorAuthUserId,
            Action<string> onSuccess,
            Action<string> onError)
        {
            RunOwnerless(DoVerifyDiscord(serverUrl, packageId, productId, creatorAuthUserId, onSuccess, onError));
        }

        /// <summary>Returns cached token for this package, or null if none.</summary>
        public static string GetCachedToken(string packageId) =>
            LicenseTokenCache.GetValidToken(packageId);

        // ── Private coroutines ────────────────────────────────────────────────

        private static IEnumerator DoVerify(
            string serverUrl,
            string packageId,
            string licenseKey,
            string provider,
            string productPermalink,
            Action<string> onSuccess,
            Action<string> onError)
        {
            string fingerprint = MachineFingerprintService.GetFingerprint();
            string nonce = GenerateNonce();
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string baseUrl = serverUrl.TrimEnd('/');

            string bodyJson = JsonUtility.ToJson(new VerifyRequest
            {
                packageId = packageId,
                licenseKey = licenseKey,
                provider = provider,
                productPermalink = productPermalink,
                machineFingerprint = fingerprint,
                nonce = nonce,
                timestamp = timestamp,
            });

            using var req = new UnityWebRequest($"{baseUrl}/v1/licenses/verify", "POST");
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(bodyJson));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept-Encoding", "identity");

            yield return req.SendWebRequest();

            string respBody = req.downloadHandler?.text ?? "";
            if (req.result != UnityWebRequest.Result.Success) { onError?.Invoke($"Network error: {req.error}"); yield break; }
            if (req.responseCode == 422 || req.responseCode == 400)
            {
                var e = JsonUtility.FromJson<ErrorResponse>(respBody);
                onError?.Invoke(e?.error ?? "License verification failed");
                yield break;
            }
            if (req.responseCode != 200) { onError?.Invoke($"Server error ({req.responseCode})"); yield break; }

            var resp = JsonUtility.FromJson<VerifyResponse>(respBody);
            if (resp == null || string.IsNullOrEmpty(resp.token)) { onError?.Invoke("Invalid server response"); yield break; }

            LicenseTokenCache.StoreToken(packageId, resp.token);
            onSuccess?.Invoke(resp.token);
        }

        private static IEnumerator DoVerifyDiscord(
            string serverUrl,
            string packageId,
            string productId,
            string creatorAuthUserId,
            Action<string> onSuccess,
            Action<string> onError)
        {
            // Buyer must be signed in to YUCP so the server can look up their Discord account.
            // Token is stored in EditorPrefs by YucpOAuthService (com.yucp.devtools).
            string accessToken = GetYucpAccessToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                onError?.Invoke(
                    "You must be signed in to YUCP to use Discord verification.\n" +
                    "Please sign in via the YUCP Package Exporter and try again.");
                yield break;
            }

            string fingerprint = MachineFingerprintService.GetFingerprint();
            string nonce = GenerateNonce();
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string baseUrl = serverUrl.TrimEnd('/');

            string bodyJson = JsonUtility.ToJson(new DiscordVerifyRequest
            {
                packageId = packageId,
                creatorAuthUserId = creatorAuthUserId,
                productId = productId,
                machineFingerprint = fingerprint,
                nonce = nonce,
                timestamp = timestamp,
            });

            using var req = new UnityWebRequest($"{baseUrl}/v1/licenses/verify-discord", "POST");
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(bodyJson));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + accessToken);
            req.SetRequestHeader("Accept-Encoding", "identity");

            yield return req.SendWebRequest();

            string respBody = req.downloadHandler?.text ?? "";
            if (req.result != UnityWebRequest.Result.Success) { onError?.Invoke($"Network error: {req.error}"); yield break; }
            if (req.responseCode == 401 || req.responseCode == 403)
            {
                var e = JsonUtility.FromJson<ErrorResponse>(respBody);
                onError?.Invoke(e?.error ?? "Discord verification failed");
                yield break;
            }
            if (req.responseCode != 200) { onError?.Invoke($"Server error ({req.responseCode})"); yield break; }

            var resp = JsonUtility.FromJson<VerifyResponse>(respBody);
            if (resp == null || string.IsNullOrEmpty(resp.token)) { onError?.Invoke("Invalid server response"); yield break; }

            LicenseTokenCache.StoreToken(packageId, resp.token);
            onSuccess?.Invoke(resp.token);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the YUCP access token from EditorPrefs (stored there by YucpOAuthService in com.yucp.devtools).
        /// Returns null if not signed in or token is expired.
        /// </summary>
        private static string GetYucpAccessToken()
        {
            const string KeyToken  = "YUCP_OAuth_AccessToken";
            const string KeyExpiry = "YUCP_OAuth_TokenExpiry";
            if (!EditorPrefs.HasKey(KeyToken) || !EditorPrefs.HasKey(KeyExpiry)) return null;
            string token = EditorPrefs.GetString(KeyToken, "");
            if (string.IsNullOrEmpty(token)) return null;
            long expiry = (long)EditorPrefs.GetInt(KeyExpiry, 0);
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiry) return null;
            return token;
        }

        private static string GenerateNonce()
        {
            byte[] bytes = new byte[16];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        private static void RunOwnerless(IEnumerator routine)
        {
            if (routine == null) throw new ArgumentNullException(nameof(routine));
            void Tick()
            {
                try { if (routine.MoveNext()) return; }
                catch (Exception ex) { Debug.LogException(ex); }
                EditorApplication.update -= Tick;
            }
            EditorApplication.update += Tick;
        }

        // ── Serialization ─────────────────────────────────────────────────────

        [Serializable] private class VerifyRequest
        {
            public string packageId, licenseKey, provider, productPermalink, machineFingerprint, nonce;
            public long timestamp;
        }

        [Serializable] private class DiscordVerifyRequest
        {
            public string packageId, creatorAuthUserId, productId, machineFingerprint, nonce;
            public long timestamp;
        }

        [Serializable] private class VerifyResponse  { public bool success; public string token; public long expiresAt; }
        [Serializable] private class ErrorResponse   { public string error; }
    }
}
