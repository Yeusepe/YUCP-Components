using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class VerificationIntentService
    {
        private const string ReauthenticationMessage =
            "Your YUCP Creator Identity session is no longer valid.\n" +
            "Please sign in again and then retry Verify Purchase.";

        private static readonly HttpClient s_http = new HttpClient();

        [Serializable]
        internal sealed class VerificationRequirement
        {
            public string methodKey;
            public string providerKey;
            public string kind;
            public string title;
            public string description;
            public string creatorAuthUserId;
            public string productId;
            public string providerProductRef;
        }

        [Serializable]
        private sealed class CreateIntentRequest
        {
            public string packageId;
            public string packageName;
            public string machineFingerprint;
            public string codeChallenge;
            public string returnUrl;
            public string idempotencyKey;
            public VerificationRequirement[] requirements;
        }

        [Serializable]
        private sealed class VerificationIntentResponse
        {
            public string id;
            public string packageId;
            public string packageName;
            public string status;
            public string verificationUrl;
            public string returnUrl;
            public string verifiedMethodKey;
            public string errorCode;
            public string errorMessage;
            public string grantToken;
            public bool grantAvailable;
            public long expiresAt;
        }

        [Serializable]
        private sealed class RedemptionResponse
        {
            public bool success;
            public string token;
            public long expiresAt;
        }

        [Serializable]
        private sealed class RedemptionRequest
        {
            public string codeVerifier;
            public string machineFingerprint;
            public string grantToken;
        }

        [Serializable]
        private sealed class ErrorResponse
        {
            public string error;
            public string code;
            public string message;
        }

        private sealed class RequestResult
        {
            public long responseCode;
            public string body;
        }

        internal static void VerifyInBrowserAsync(
            string serverUrl,
            string packageId,
            string packageName,
            VerificationRequirement[] requirements,
            Action<string> onSuccess,
            Action<string> onError,
            Action<string> openUrlHandler = null)
        {
#pragma warning disable CS4014
            VerifyInBrowserInternalAsync(serverUrl, packageId, packageName, requirements, onSuccess, onError, openUrlHandler);
#pragma warning restore CS4014
        }

        private static async Task VerifyInBrowserInternalAsync(
            string serverUrl,
            string packageId,
            string packageName,
            VerificationRequirement[] requirements,
            Action<string> onSuccess,
            Action<string> onError,
            Action<string> openUrlHandler)
        {
            try
            {
                if (requirements == null || requirements.Length == 0)
                {
                    onError?.Invoke("This package does not expose any browser verification methods.");
                    return;
                }

                Debug.Log($"[YUCP VerificationIntent] Getting access token from server: {serverUrl}");
                string accessToken = await CreatorIdentityOAuthService.GetValidAccessTokenAsync(serverUrl);
                if (string.IsNullOrEmpty(accessToken))
                {
                    Debug.LogWarning("[YUCP VerificationIntent] No access token available — user not signed in or token refresh failed");
                    onError?.Invoke(
                        "You must be signed in to YUCP before verification can continue.\n" +
                        "Please sign in and then retry Verify Purchase.");
                    return;
                }

                Debug.Log($"[YUCP VerificationIntent] Got access token, creating intent for packageId='{packageId}'");
                string machineFingerprint = MachineFingerprintService.GetFingerprint();
                string codeVerifier = CreateRandomBase64Url(32);
                string codeChallenge = ComputeCodeChallenge(codeVerifier);
                string idempotencyKey = $"{packageId}:{machineFingerprint}:{Guid.NewGuid():N}";

                using var listener = CreateLoopbackListener(out string returnUrl);
                Debug.Log($"[YUCP VerificationIntent] Loopback listener ready at: {returnUrl}");

                VerificationIntentResponse intent = await CreateIntentAsync(
                    serverUrl,
                    accessToken,
                    packageId,
                    packageName,
                    machineFingerprint,
                    codeChallenge,
                    returnUrl,
                    idempotencyKey,
                    requirements);

                Debug.Log($"[YUCP VerificationIntent] Intent created (id='{intent.id}'), opening: {intent.verificationUrl}");
                OpenVerificationUrl(intent.verificationUrl, openUrlHandler);

                string completedIntentId = intent.id;
                string grantToken = null;

                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    Task<HttpListenerContext> contextTask = listener.GetContextAsync();
                    Task finished = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cts.Token));
                    cts.Cancel();

                    if (finished == contextTask)
                    {
                        HttpListenerContext context = await contextTask;
                        var query = ParseQueryString(context.Request.Url?.Query ?? string.Empty);

                        if (query.TryGetValue("error", out string error))
                        {
                            string description = query.TryGetValue("error_description", out string errorDescription)
                                ? errorDescription
                                : error;
                            await SendReturnPageAsync(context, false, description);
                            onError?.Invoke(description);
                            return;
                        }

                        completedIntentId = query.TryGetValue("intent_id", out string returnedIntentId) && !string.IsNullOrEmpty(returnedIntentId)
                            ? returnedIntentId
                            : intent.id;
                        grantToken = query.TryGetValue("grant", out string returnedGrant) ? returnedGrant : null;

                        if (string.IsNullOrEmpty(grantToken))
                        {
                            await SendReturnPageAsync(
                                context,
                                false,
                                "Verification did not return a completion grant. Return to Unity and try again.");
                            onError?.Invoke("Verification did not return a completion grant. Please try again.");
                            return;
                        }

                        await SendReturnPageAsync(
                            context,
                            true,
                            "Verification complete. Return to Unity to finish redeeming access for this machine.");
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    try
                    {
                        listener.Stop();
                    }
                    catch
                    {
                    }
                }

                if (string.IsNullOrEmpty(grantToken))
                {
                    VerificationIntentResponse refreshedIntent = await GetIntentAsync(serverUrl, accessToken, intent.id);
                    if (refreshedIntent != null)
                    {
                        completedIntentId = refreshedIntent.id;
                        grantToken = refreshedIntent.grantToken;
                        if (string.IsNullOrEmpty(grantToken) && !string.IsNullOrEmpty(refreshedIntent.errorMessage))
                        {
                            onError?.Invoke(refreshedIntent.errorMessage);
                            return;
                        }
                    }
                }

                if (string.IsNullOrEmpty(grantToken))
                {
                    onError?.Invoke(
                        "Verification did not finish before Unity stopped waiting.\n" +
                        "If the browser is still open, complete the flow there and retry from Unity.");
                    return;
                }

                RedemptionResponse redemption = await RedeemIntentAsync(
                    serverUrl,
                    accessToken,
                    completedIntentId,
                    codeVerifier,
                    machineFingerprint,
                    grantToken);

                if (redemption == null || !redemption.success || string.IsNullOrEmpty(redemption.token))
                {
                    onError?.Invoke("Verification finished, but Unity could not redeem the completion grant.");
                    return;
                }

                LicenseTokenCache.StoreToken(packageId, redemption.token);
                onSuccess?.Invoke(redemption.token);
            }
            catch (ReauthenticationRequiredException)
            {
                Debug.LogWarning("[YUCP VerificationIntent] Session invalid (ReauthenticationRequired) — signing out");
                CreatorIdentityOAuthService.SignOut();
                onError?.Invoke(ReauthenticationMessage);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP VerificationIntent] Unexpected exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                onError?.Invoke(ex.Message);
            }
        }

        private static async Task<VerificationIntentResponse> CreateIntentAsync(
            string serverUrl,
            string accessToken,
            string packageId,
            string packageName,
            string machineFingerprint,
            string codeChallenge,
            string returnUrl,
            string idempotencyKey,
            VerificationRequirement[] requirements)
        {
            string url = $"{serverUrl.TrimEnd('/')}/api/public/v2/verification-intents";
            string bodyJson = JsonUtility.ToJson(new CreateIntentRequest
            {
                packageId = packageId,
                packageName = packageName,
                machineFingerprint = machineFingerprint,
                codeChallenge = codeChallenge,
                returnUrl = returnUrl,
                idempotencyKey = idempotencyKey,
                requirements = requirements,
            });

            RequestResult result = await SendAuthorizedJsonAsync(serverUrl, accessToken, "POST", url, bodyJson);
            if (result.responseCode != 200)
            {
                throw new Exception(BuildServerErrorMessage(result.responseCode, result.body, "Could not create verification intent"));
            }

            VerificationIntentResponse intent = JsonUtility.FromJson<VerificationIntentResponse>(result.body);
            if (intent == null || string.IsNullOrEmpty(intent.id) || string.IsNullOrEmpty(intent.verificationUrl))
            {
                throw new Exception("Verification intent response was missing required fields.");
            }

            return intent;
        }

        private static async Task<VerificationIntentResponse> GetIntentAsync(
            string serverUrl,
            string accessToken,
            string intentId)
        {
            string url = $"{serverUrl.TrimEnd('/')}/api/public/v2/verification-intents/{Uri.EscapeDataString(intentId)}";
            RequestResult result = await SendAuthorizedJsonAsync(serverUrl, accessToken, "GET", url, null);
            if (result.responseCode != 200)
            {
                return null;
            }

            return JsonUtility.FromJson<VerificationIntentResponse>(result.body);
        }

        private static async Task<RedemptionResponse> RedeemIntentAsync(
            string serverUrl,
            string accessToken,
            string intentId,
            string codeVerifier,
            string machineFingerprint,
            string grantToken)
        {
            string url = $"{serverUrl.TrimEnd('/')}/api/public/v2/verification-intents/{Uri.EscapeDataString(intentId)}/redeem";
            string bodyJson = JsonUtility.ToJson(new RedemptionRequest
            {
                codeVerifier = codeVerifier,
                machineFingerprint = machineFingerprint,
                grantToken = grantToken,
            });

            RequestResult result = await SendAuthorizedJsonAsync(serverUrl, accessToken, "POST", url, bodyJson);
            if (result.responseCode != 200)
            {
                throw new Exception(BuildServerErrorMessage(result.responseCode, result.body, "Could not redeem verification grant"));
            }

            RedemptionResponse response = JsonUtility.FromJson<RedemptionResponse>(result.body);
            if (response == null || !response.success || string.IsNullOrEmpty(response.token))
            {
                throw new Exception("Redemption response did not include a usable token.");
            }

            return response;
        }

        private static async Task<RequestResult> SendAuthorizedJsonAsync(
            string serverUrl,
            string accessToken,
            string method,
            string url,
            string bodyJson)
        {
            bool hasRetried = false;

            while (true)
            {
                RequestResult result = await SendHttpJsonAsync(method, url, accessToken, bodyJson);

                if (result.responseCode == 401)
                {
                    if (!hasRetried)
                    {
                        string refreshed = await CreatorIdentityOAuthService.ForceRefreshAccessTokenAsync(serverUrl);
                        if (!string.IsNullOrEmpty(refreshed))
                        {
                            accessToken = refreshed;
                            hasRetried = true;
                            continue;
                        }
                    }

                    throw new ReauthenticationRequiredException();
                }

                string parsedError = ExtractErrorMessage(result.body);
                if (result.responseCode == 403 && IsMissingScopeError(parsedError))
                {
                    throw new ReauthenticationRequiredException();
                }

                return result;
            }
        }

        private static async Task<RequestResult> SendHttpJsonAsync(string method, string url, string accessToken, string bodyJson)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            if (!string.IsNullOrEmpty(bodyJson))
                request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await s_http.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return new RequestResult
            {
                responseCode = (long)response.StatusCode,
                body = body,
            };
        }

        private static void OpenVerificationUrl(string verificationUrl, Action<string> openUrlHandler = null)
        {
            void Open()
            {
                if (openUrlHandler != null)
                {
                    openUrlHandler(verificationUrl);
                    return;
                }

#if UNITY_INCLUDE_TESTS
                var openOverride = Interlocked.Exchange(ref VerificationIntentServiceTestHooks.OpenUrlHandler, null);
                if (openOverride != null)
                {
                    openOverride(verificationUrl);
                    return;
                }
#endif

                Application.OpenURL(verificationUrl);
            }

            if (EditorMainThreadDispatcher.IsMainThread)
            {
                Open();
                return;
            }

            EditorMainThreadDispatcher.Invoke(Open);
        }

        private static HttpListener CreateLoopbackListener(out string returnUrl)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            returnUrl = $"http://127.0.0.1:{port}/callback";
            return listener;
        }

        private static string BuildServerErrorMessage(long responseCode, string responseBody, string fallbackMessage)
        {
            string parsedError = ExtractErrorMessage(responseBody);
            if (!string.IsNullOrEmpty(parsedError))
            {
                return parsedError;
            }

            return $"{fallbackMessage} (HTTP {responseCode})";
        }

        private static string ExtractErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                ErrorResponse error = JsonUtility.FromJson<ErrorResponse>(body);
                if (error != null)
                {
                    if (!string.IsNullOrWhiteSpace(error.message))
                    {
                        return error.message.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(error.error))
                    {
                        return error.error.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(error.code))
                    {
                        return error.code.Trim();
                    }
                }
            }
            catch
            {
            }

            string trimmed = body.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        private static bool IsMissingScopeError(string errorMessage)
        {
            return !string.IsNullOrWhiteSpace(errorMessage)
                && errorMessage.IndexOf("missing required scopes", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string CreateRandomBase64Url(int byteCount)
        {
            byte[] bytes = new byte[byteCount];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Base64UrlEncode(bytes);
        }

        private static string ComputeCodeChallenge(string codeVerifier)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
            return Base64UrlEncode(hash);
        }

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string trimmed = query?.TrimStart('?');
            if (string.IsNullOrEmpty(trimmed))
            {
                return result;
            }

            foreach (string part in trimmed.Split('&'))
            {
                int separator = part.IndexOf('=');
                if (separator < 0)
                {
                    continue;
                }

                result[Uri.UnescapeDataString(part.Substring(0, separator))] =
                    Uri.UnescapeDataString(part.Substring(separator + 1));
            }

            return result;
        }

        private static async Task SendReturnPageAsync(HttpListenerContext context, bool success, string message)
        {
            try
            {
                byte[] html = Encoding.UTF8.GetBytes(BuildReturnHtml(success, message));
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = html.Length;
                await context.Response.OutputStream.WriteAsync(html, 0, html.Length).ConfigureAwait(false);
                context.Response.OutputStream.Close();
            }
            catch
            {
            }
        }

        private static string BuildReturnHtml(bool success, string message)
        {
            string title       = success ? "Verification complete" : "Verification not completed";
            string bodyMessage = success
                ? "Your purchase has been verified for this machine."
                : "Something went wrong during the verification process.";

            string safeMessage = WebUtility.HtmlEncode(message ?? string.Empty);

            string detailHtml;
            string accentStart;
            string accentEnd;

            if (success)
            {
                detailHtml = "<div class=\"detail-card detail-card-success\">" +
                             "<span class=\"detail-label\">Next</span>" +
                             "<div class=\"detail-body\">You can close this tab and return to Unity.</div>" +
                             "</div>";
                accentStart = "#36bfb1";
                accentEnd   = "#2da89c";
            }
            else
            {
                detailHtml = "<div class=\"detail-card detail-card-error\">" +
                             "<span class=\"detail-label\">Details</span>" +
                             $"<div class=\"detail-body\">{safeMessage}</div>" +
                             "</div>";
                accentStart = "#fb7185";
                accentEnd   = "#f59e0b";
            }

            return CallbackPageHtmlBuilder.Build(title, bodyMessage, detailHtml, accentStart, accentEnd);
        }

        private sealed class ReauthenticationRequiredException : Exception
        {
        }
    }
}
