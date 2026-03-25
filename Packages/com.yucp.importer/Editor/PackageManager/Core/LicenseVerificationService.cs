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
            private const string CreatorSuiteVerificationLabel = "Creator Suite verification";
            private const string CreatorIdentityReauthenticationMessage =
                "Your YUCP Creator Identity session is no longer valid.\n" +
                "Please sign in again and then retry Verify Purchase.";

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

        public static bool IsCreatorIdentityReauthenticationError(string errorMessage)
        {
            return string.Equals(
                errorMessage?.Trim(),
                CreatorIdentityReauthenticationMessage,
                StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns true when the error indicates the access token is missing required scopes,
        /// meaning the session is stale and the user needs to sign in again.
        /// </summary>
        public static bool IsMissingScopeError(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage)) return false;
            return errorMessage.IndexOf("missing required scopes", StringComparison.OrdinalIgnoreCase) >= 0
                || errorMessage.IndexOf("verification:read", StringComparison.OrdinalIgnoreCase) >= 0;
        }

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

            Debug.Log($"[LicenseVerification] Starting license verification packageId={packageId} provider={provider} url={baseUrl}/v1/licenses/verify");
            yield return req.SendWebRequest();

            string respBody = req.downloadHandler?.text ?? "";
            if (HasTransportError(req))
            {
                string errorMessage = BuildNetworkErrorMessage(req, respBody);
                Debug.LogError(
                    $"[LicenseVerification] License verification transport failure packageId={packageId} provider={provider} responseCode={req.responseCode} result={req.result} error={req.error ?? "<empty>"} body={FormatBodyForLog(respBody)}");
                onError?.Invoke(errorMessage);
                yield break;
            }
            if (req.responseCode == 422 || req.responseCode == 400)
            {
                string errorMessage = ExtractErrorMessage(respBody) ?? "License verification failed";
                Debug.LogWarning(
                    $"[LicenseVerification] License verification rejected packageId={packageId} provider={provider} responseCode={req.responseCode} body={FormatBodyForLog(respBody)}");
                onError?.Invoke(errorMessage);
                yield break;
            }
            if (req.responseCode != 200)
            {
                string errorMessage = BuildServerErrorMessage(req, respBody, "License verification failed");
                Debug.LogError(
                    $"[LicenseVerification] License verification failed packageId={packageId} provider={provider} responseCode={req.responseCode} result={req.result} error={req.error ?? "<empty>"} body={FormatBodyForLog(respBody)}");
                onError?.Invoke(errorMessage);
                yield break;
            }

            var resp = JsonUtility.FromJson<VerifyResponse>(respBody);
            if (resp == null || string.IsNullOrEmpty(resp.token))
            {
                Debug.LogError(
                    $"[LicenseVerification] License verification returned an invalid response packageId={packageId} provider={provider} body={FormatBodyForLog(respBody)}");
                onError?.Invoke("Invalid server response");
                yield break;
            }

            if (!YucpJwtTokenUtility.TryValidateLicenseToken(resp.token, packageId, out _))
            {
                Debug.LogError(
                    $"[LicenseVerification] License verification returned a token that failed signature or claim validation packageId={packageId} provider={provider}");
                onError?.Invoke("The verification token returned by the server could not be trusted.");
                yield break;
            }

            LicenseTokenCache.StoreToken(packageId, resp.token);
            Debug.Log($"[LicenseVerification] License verification succeeded packageId={packageId} provider={provider} responseCode={req.responseCode}");
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
            var tokenTask = CreatorIdentityOAuthService.GetValidAccessTokenAsync(serverUrl);
            while (!tokenTask.IsCompleted)
            {
                yield return null;
            }

            string accessToken = !tokenTask.IsFaulted && !tokenTask.IsCanceled ? tokenTask.Result : null;
            if (string.IsNullOrEmpty(accessToken))
            {
                onError?.Invoke(
                    $"You must be signed in to YUCP to use {CreatorSuiteVerificationLabel}.\n" +
                    "Please sign in via Creator Identity and try again.");
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

            bool retriedAfterRefresh = false;
            while (true)
            {
                using var req = new UnityWebRequest($"{baseUrl}/v1/licenses/verify-discord", "POST");
                req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(bodyJson));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + accessToken);
                req.SetRequestHeader("Accept-Encoding", "identity");

                Debug.Log($"[LicenseVerification] Starting Creator Suite verification packageId={packageId} url={baseUrl}/v1/licenses/verify-discord");
                yield return req.SendWebRequest();

                string respBody = req.downloadHandler?.text ?? "";
                if (HasTransportError(req))
                {
                    string errorMessage = BuildNetworkErrorMessage(req, respBody);
                    Debug.LogError(
                        $"[LicenseVerification] Creator Suite verification transport failure packageId={packageId} responseCode={req.responseCode} result={req.result} error={req.error ?? "<empty>"} body={FormatBodyForLog(respBody)}");
                    onError?.Invoke(errorMessage);
                    yield break;
                }

                if (req.responseCode == 401 && !retriedAfterRefresh)
                {
                    Debug.LogWarning(
                        $"[LicenseVerification] Creator Suite verification received HTTP 401 for packageId={packageId}. Attempting access-token refresh before surfacing the failure. body={FormatBodyForLog(respBody)}");

                    var refreshTask = CreatorIdentityOAuthService.ForceRefreshAccessTokenAsync(serverUrl);
                    while (!refreshTask.IsCompleted)
                    {
                        yield return null;
                    }

                    string refreshedAccessToken = !refreshTask.IsFaulted && !refreshTask.IsCanceled
                        ? refreshTask.Result
                        : null;
                    if (!string.IsNullOrEmpty(refreshedAccessToken))
                    {
                        accessToken = refreshedAccessToken;
                        retriedAfterRefresh = true;
                        continue;
                    }
                }

                if (req.responseCode == 401)
                {
                    Debug.LogWarning(
                        $"[LicenseVerification] Creator Suite verification requires re-authentication packageId={packageId} responseCode={req.responseCode} body={FormatBodyForLog(respBody)}");
                    CreatorIdentityOAuthService.SignOut();
                    onError?.Invoke(BuildCreatorIdentityReauthenticationMessage());
                    yield break;
                }

                if (req.responseCode == 403)
                {
                    string errorMessage = ExtractErrorMessage(respBody) ?? $"{CreatorSuiteVerificationLabel} failed.";
                    Debug.LogWarning(
                        $"[LicenseVerification] Creator Suite verification rejected packageId={packageId} responseCode={req.responseCode} body={FormatBodyForLog(respBody)}");

                    // Token is missing required scopes — sign out so the user gets a fresh token next sign-in
                    if (IsMissingScopeError(errorMessage))
                    {
                        CreatorIdentityOAuthService.SignOut();
                        onError?.Invoke(BuildCreatorIdentityReauthenticationMessage());
                        yield break;
                    }

                    onError?.Invoke(errorMessage);
                    yield break;
                }

                if (req.responseCode != 200)
                {
                    string errorMessage = BuildServerErrorMessage(
                        req,
                        respBody,
                        $"{CreatorSuiteVerificationLabel} failed");
                    Debug.LogError(
                        $"[LicenseVerification] Creator Suite verification failed packageId={packageId} responseCode={req.responseCode} result={req.result} error={req.error ?? "<empty>"} body={FormatBodyForLog(respBody)}");
                    onError?.Invoke(errorMessage);
                    yield break;
                }

                var resp = JsonUtility.FromJson<VerifyResponse>(respBody);
                if (resp == null || string.IsNullOrEmpty(resp.token))
                {
                    Debug.LogError(
                        $"[LicenseVerification] Creator Suite verification returned an invalid response packageId={packageId} body={FormatBodyForLog(respBody)}");
                    onError?.Invoke("Invalid server response");
                    yield break;
                }

                if (!YucpJwtTokenUtility.TryValidateLicenseToken(resp.token, packageId, out _))
                {
                    Debug.LogError(
                        $"[LicenseVerification] Creator Suite verification returned a token that failed signature or claim validation packageId={packageId}");
                    onError?.Invoke("The verification token returned by the server could not be trusted.");
                    yield break;
                }

                LicenseTokenCache.StoreToken(packageId, resp.token);
                Debug.Log($"[LicenseVerification] Creator Suite verification succeeded packageId={packageId} responseCode={req.responseCode}");
                onSuccess?.Invoke(resp.token);
                yield break;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

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
            object pendingYield = null;

            void Tick()
            {
                try
                {
                    if (pendingYield is AsyncOperation asyncOperation)
                    {
                        if (!asyncOperation.isDone)
                        {
                            return;
                        }

                        pendingYield = null;
                    }
                    else if (pendingYield is CustomYieldInstruction customYieldInstruction)
                    {
                        if (customYieldInstruction.keepWaiting)
                        {
                            return;
                        }

                        pendingYield = null;
                    }
                    else if (pendingYield != null)
                    {
                        pendingYield = null;
                    }

                    if (routine.MoveNext())
                    {
                        pendingYield = routine.Current;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }

                EditorApplication.update -= Tick;
            }
            EditorApplication.update += Tick;
        }

        private static bool HasTransportError(UnityWebRequest req) =>
            req.result == UnityWebRequest.Result.ConnectionError ||
            req.result == UnityWebRequest.Result.DataProcessingError;

        private static string BuildNetworkErrorMessage(UnityWebRequest req, string responseBody)
        {
            string requestError = string.IsNullOrWhiteSpace(req?.error)
                ? null
                : req.error.Trim();
            string responseError = ExtractErrorMessage(responseBody);
            if (!string.IsNullOrEmpty(requestError))
            {
                return $"Network error: {requestError}";
            }

            if (!string.IsNullOrEmpty(responseError))
            {
                return $"Network error: {responseError}";
            }

            if (req?.responseCode > 0)
            {
                return $"Network error: server returned HTTP {req.responseCode}.";
            }

            return "Network error: no response details were returned.";
        }

        private static string BuildServerErrorMessage(
            UnityWebRequest req,
            string responseBody,
            string fallbackMessage)
        {
            string responseError = ExtractErrorMessage(responseBody);
            if (!string.IsNullOrEmpty(responseError))
            {
                return responseError;
            }

            if (!string.IsNullOrWhiteSpace(req?.error))
            {
                return $"{fallbackMessage} (HTTP {req.responseCode}: {req.error.Trim()})";
            }

            return $"{fallbackMessage} (HTTP {req?.responseCode ?? 0})";
        }

        private static string BuildCreatorIdentityReauthenticationMessage()
        {
            return CreatorIdentityReauthenticationMessage;
        }

        private static string ExtractErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                var errorResponse = JsonUtility.FromJson<ErrorResponse>(body);
                if (errorResponse != null)
                {
                    if (!string.IsNullOrWhiteSpace(errorResponse.message))
                    {
                        return errorResponse.message.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(errorResponse.error))
                    {
                        return errorResponse.error.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(errorResponse.code))
                    {
                        return errorResponse.code.Trim();
                    }
                }
            }
            catch
            {
            }

            string trimmed = body.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        private static string FormatBodyForLog(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return "<empty>";
            }

            string trimmed = body.Trim().Replace('\n', ' ').Replace('\r', ' ');
            return trimmed.Length <= 240 ? trimmed : trimmed.Substring(0, 240) + "...";
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
        [Serializable] private class ErrorResponse   { public string error; public string code; public string message; }
    }
}
