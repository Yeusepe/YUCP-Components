using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class CreatorIdentityOAuthService
    {
        private const string UnityOAuthScopeRejectionMarker = "This YUCP server has not enabled Unity purchase verification yet.";
        private const string CurrentSessionVersion = "2";
        private const int AccessTokenSkewSeconds = 60;
        private static readonly object SessionLock = new object();
        private static Task _backgroundRefreshTask;
        private static readonly HttpClient s_tokenHttpClient = new HttpClient();

        // Relay URL written by PackageManagerWindow before a sign-in-then-verify flow;
        // consumed once by SendSuccessPageAsync so the OAuth success page auto-redirects.
        internal static string s_pendingVerifyRelayUrl;

        private sealed class OAuthDomainConfig
        {
            public OAuthDomainConfig(
                string clientId,
                string[] requestedScopes,
                string requiredScope,
                string editorPrefsPrefix,
                string sessionFileName,
                string sessionEntropyLabel)
            {
                ClientId = clientId;
                RequestedScopes = requestedScopes;
                RequiredScope = requiredScope;
                EditorPrefsPrefix = editorPrefsPrefix;
                SessionFileName = sessionFileName;
                SessionEntropyLabel = sessionEntropyLabel;
            }

            public string ClientId { get; }
            public string[] RequestedScopes { get; }
            public string RequiredScope { get; }
            public string EditorPrefsPrefix { get; }
            public string SessionFileName { get; }
            public string SessionEntropyLabel { get; }
            public string RequestedScopeValue => string.Join(" ", RequestedScopes);

            public string GetEditorPrefKey(string suffix)
            {
                return $"{EditorPrefsPrefix}_{suffix}";
            }
        }

        private static readonly OAuthDomainConfig Domain = new OAuthDomainConfig(
            clientId: "yucp-unity-user",
            requestedScopes: new[]
            {
                "verification:read",
                "products:read",
                "offline_access",
            },
            requiredScope: "verification:read",
            editorPrefsPrefix: "YUCP_UserOAuth",
            sessionFileName: "unity-user-oauth-session-v2.dat",
            sessionEntropyLabel: "YUCP.UnityEditor.User.Session.v2");

        public static string ClientId => Domain.ClientId;

        internal static string[] PackageInstallationScopes => new[]
        {
            "verification:read",
            "products:read",
        };

        private static string KeyToken => Domain.GetEditorPrefKey("AccessToken");
        private static string KeyExpiry => Domain.GetEditorPrefKey("TokenExpiry");
        private static string KeyUserId => Domain.GetEditorPrefKey("UserId");
        private static string KeyDisplayName => Domain.GetEditorPrefKey("DisplayName");
        private static string KeySessionVersion => Domain.GetEditorPrefKey("SessionVersion");
        private static readonly byte[] SessionEntropy = Encoding.UTF8.GetBytes(Domain.SessionEntropyLabel);

#if UNITY_EDITOR_WIN
        private const int CryptProtectUiForbidden = 0x1;
        private const int SwRestore = 9;
        private const int SwShow = 5;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DataBlob pDataIn,
            string szDataDescr,
            ref DataBlob pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            out DataBlob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(
            ref DataBlob pDataIn,
            StringBuilder ppszDataDescr,
            ref DataBlob pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            out DataBlob pDataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsIconic(IntPtr hWnd);
#endif

        [Serializable]
        private class OAuthSessionV2
        {
            public int storageVersion = 2;
            public string accessToken;
            public long accessTokenExpiresAt;
            public string refreshToken;
            public long refreshTokenExpiresAt;
            public string userId;
            public string displayName;
            public string scope;
        }

        public static bool IsSignedIn()
        {
            return TryGetActiveSession(out _);
        }

        public static string GetAccessToken()
        {
            return TryGetActiveSession(out OAuthSessionV2 session) && HasUsableAccessToken(session)
                ? session.accessToken
                : null;
        }

        public static string GetDisplayName()
        {
            if (TryGetCachedSession(out OAuthSessionV2 session) && !string.IsNullOrEmpty(session.displayName))
            {
                return session.displayName;
            }

            string name = EditorPrefs.GetString(KeyDisplayName, null);
            return string.IsNullOrEmpty(name) ? null : name;
        }

        public static bool IsUnityOAuthScopeRejectionError(string errorMessage)
        {
            return !string.IsNullOrWhiteSpace(errorMessage) &&
                errorMessage.IndexOf(UnityOAuthScopeRejectionMarker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void TryBeginBackgroundRefresh(string serverUrl, Action onStateChanged = null)
        {
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            if (!TryGetCachedSession(out OAuthSessionV2 session) || HasUsableAccessToken(session) || string.IsNullOrEmpty(session.refreshToken))
            {
                return;
            }

            lock (SessionLock)
            {
                if (_backgroundRefreshTask != null && !_backgroundRefreshTask.IsCompleted)
                {
                    return;
                }

                _backgroundRefreshTask = RefreshInBackgroundAsync(serverUrl, onStateChanged);
            }
        }

        public static async Task<string> GetValidAccessTokenAsync(string serverUrl)
        {
            return await GetValidAccessTokenAsync(serverUrl, Domain.RequiredScope).ConfigureAwait(false);
        }

        public static async Task<string> GetValidAccessTokenAsync(string serverUrl, params string[] requiredScopes)
        {
            string[] normalizedRequiredScopes = NormalizeRequiredScopes(requiredScopes);
            if (TryGetCachedSession(out OAuthSessionV2 session))
            {
                if (HasUsableAccessToken(session, normalizedRequiredScopes))
                {
                    PersistPresenceHints(session);
                    return session.accessToken;
                }

                if (!string.IsNullOrEmpty(session.refreshToken))
                {
                    string refreshedAccessToken = await RefreshAccessTokenAsync(serverUrl, session, normalizedRequiredScopes)
                        .ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(refreshedAccessToken))
                    {
                        return refreshedAccessToken;
                    }
                }
            }

            return null;
        }

        public static async Task<string> ForceRefreshAccessTokenAsync(string serverUrl)
        {
            return await ForceRefreshAccessTokenAsync(serverUrl, Domain.RequiredScope).ConfigureAwait(false);
        }

        public static async Task<string> ForceRefreshAccessTokenAsync(string serverUrl, params string[] requiredScopes)
        {
            string[] normalizedRequiredScopes = NormalizeRequiredScopes(requiredScopes);
            if (TryGetCachedSession(out OAuthSessionV2 session) && !string.IsNullOrEmpty(session.refreshToken))
            {
                string refreshedAccessToken = await RefreshAccessTokenAsync(serverUrl, session, normalizedRequiredScopes)
                    .ConfigureAwait(false);
                if (!string.IsNullOrEmpty(refreshedAccessToken))
                {
                    return refreshedAccessToken;
                }
            }

            return null;
        }

        public static void SignOut()
        {
            SignOut(LoadInstalledPackagesForCacheEviction());
        }

        private static void SignOut(IReadOnlyList<InstalledPackageInfo> installedPackages)
        {
            ClearAuthorizationCaches(installedPackages);
            ClearPersistentSession();
            ClearCurrentDomainKeys();
        }

        private static List<InstalledPackageInfo> LoadInstalledPackagesForCacheEviction()
        {
            var registry = InstalledPackageRegistry.Load();
            return registry != null ? registry.GetAllPackages() : new List<InstalledPackageInfo>();
        }

        private static void ClearAuthorizationCaches(IReadOnlyList<InstalledPackageInfo> installedPackages)
        {
            var packageIds = new List<string>();

            if (installedPackages != null)
            {
                foreach (var package in installedPackages)
                {
                    if (package == null || string.IsNullOrWhiteSpace(package.packageId))
                    {
                        continue;
                    }

                    packageIds.Add(package.packageId);
                }
            }

            LicenseTokenCache.ClearAll(packageIds);
        }

        public static Task SignInAsync(
            string serverUrl,
            Action onSuccess,
            Action<string> onError,
            bool focusUnityOnSuccess = true)
        {
            return SignInWithAuthorizationHandlerAsync(
                serverUrl,
                Application.OpenURL,
                onSuccess,
                onError,
                focusUnityOnSuccess);
        }

        internal static async Task SignInWithAuthorizationHandlerAsync(
            string serverUrl,
            Action<string> onAuthorizationUrl,
            Action onSuccess,
            Action<string> onError,
            bool focusUnityOnSuccess = true)
        {
            try
            {
                byte[] verifierBytes = new byte[32];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(verifierBytes);
                }

                string codeVerifier = Base64UrlEncode(verifierBytes);

                byte[] challengeBytes;
                using (var sha = SHA256.Create())
                {
                    challengeBytes = sha.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
                }

                string codeChallenge = Base64UrlEncode(challengeBytes);

                byte[] stateBytes = new byte[24];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(stateBytes);
                }

                string state = Base64UrlEncode(stateBytes);

                HttpListener listener = StartLoopbackListener(
                    out string redirectUri);
                string authUrl = BuildAuthUrl(serverUrl, codeChallenge, state, redirectUri);
                onAuthorizationUrl?.Invoke(authUrl);

                HttpListenerContext context = null;
                string authCode = null;

                try
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120)))
                    {
                        Task<HttpListenerContext> contextTask = listener.GetContextAsync();
                        Task timeoutTask = Task.Delay(Timeout.Infinite, cts.Token);

                        Task finished = await Task.WhenAny(contextTask, timeoutTask);
                        cts.Cancel();

                        if (finished != contextTask)
                        {
                            listener.Stop();
                            onError?.Invoke("Sign-in timed out after 2 minutes. Please try again.");
                            return;
                        }

                        context = await contextTask;
                    }

                    var query = ParseQueryString(context.Request.Url?.Query ?? "");
                    if (query.TryGetValue("error", out string authError))
                    {
                        string description = query.TryGetValue("error_description", out string errorDescription)
                            ? Uri.UnescapeDataString(errorDescription)
                            : authError;
                        string message = BuildAuthorizationErrorMessage(description, Domain.RequestedScopeValue);
                        await SendErrorPageAsync(context, message);
                        onError?.Invoke(message);
                        return;
                    }

                    if (!query.TryGetValue("state", out string returnedState) || returnedState != state)
                    {
                        const string message = "State mismatch during Creator Identity sign-in. Please try again.";
                        await SendErrorPageAsync(context, message);
                        onError?.Invoke(message);
                        return;
                    }

                    if (!query.TryGetValue("code", out authCode) || string.IsNullOrEmpty(authCode))
                    {
                        const string message = "No authorization code was returned.";
                        await SendErrorPageAsync(context, message);
                        onError?.Invoke(message);
                        return;
                    }

                    await SendSuccessPageAsync(context);
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

                var form = new WWWForm();
                form.AddField("grant_type", "authorization_code");
                form.AddField("client_id", ClientId);
                form.AddField("code", authCode);
                form.AddField("code_verifier", codeVerifier);
                form.AddField("redirect_uri", redirectUri);

                using var tokenRequest = UnityWebRequest.Post($"{serverUrl.TrimEnd('/')}/api/auth/oauth2/token", form);
                tokenRequest.SetRequestHeader("Accept", "application/json");
                tokenRequest.SetRequestHeader("Accept-Encoding", "identity");

                var operation = tokenRequest.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                string tokenJson = tokenRequest.downloadHandler.text;
                if (tokenRequest.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"Token exchange failed ({tokenRequest.responseCode}): {tokenRequest.error} — {tokenJson}");
                    return;
                }

                OAuthSessionV2 session = BuildSessionFromTokenResponse(tokenJson, null);
                if (session == null || string.IsNullOrEmpty(session.accessToken))
                {
                    onError?.Invoke("The server response did not include an access token.");
                    return;
                }

                PersistSession(session);
                if (focusUnityOnSuccess)
                {
                    QueueFocusRelevantWindows();
                }
                onSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP OAuth] Sign-in exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                onError?.Invoke($"Sign-in error: {ex.Message}");
            }
        }

        private static HttpListener StartLoopbackListener(
            out string redirectUri)
        {
            const int attemptLimit = 10;
            Exception lastError = null;
            for (int attempt = 0; attempt < attemptLimit; attempt++)
            {
                int port;
                var probe = new System.Net.Sockets.TcpListener(
                    IPAddress.Loopback,
                    0);
                try
                {
                    probe.Start();
                    port = ((IPEndPoint)probe.LocalEndpoint).Port;
                }
                finally
                {
                    probe.Stop();
                }

                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                try
                {
                    listener.Start();
                    redirectUri = $"http://127.0.0.1:{port}/callback";
                    return listener;
                }
                catch (HttpListenerException exception)
                {
                    lastError = exception;
                    listener.Close();
                }
            }

            throw new InvalidOperationException(
                "A secure loopback callback listener could not start.",
                lastError);
        }

        private static void QueueFocusRelevantWindows()
        {
            int attempts = 0;

            void RestoreEditorWindows()
            {
                attempts++;
                // Verification is browser-hosted; only re-focus an installer window that is already
                // open (during an active import). Never spawn a standalone window here.
                EditorWindow.FocusWindowIfItsOpen<YUCP.Importer.Editor.PackageManager.PackageManagerWindow>();
                TryBringUnityEditorToFront();

                if (attempts >= 8)
                {
                    EditorApplication.update -= RestoreEditorWindows;
                }
            }

            EditorApplication.update += RestoreEditorWindows;
        }

        private static void TryBringUnityEditorToFront()
        {
#if UNITY_EDITOR_WIN
            try
            {
                using var currentProcess = global::System.Diagnostics.Process.GetCurrentProcess();
                currentProcess.Refresh();

                IntPtr windowHandle = currentProcess.MainWindowHandle;
                if (windowHandle == IntPtr.Zero)
                {
                    return;
                }

                ShowWindowAsync(windowHandle, IsIconic(windowHandle) ? SwRestore : SwShow);
                BringWindowToTop(windowHandle);
                SetForegroundWindow(windowHandle);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP OAuth] Failed to restore Unity window: {ex.Message}");
            }
#endif
        }

        private static async Task RefreshInBackgroundAsync(string serverUrl, Action onStateChanged)
        {
            try
            {
                await GetValidAccessTokenAsync(serverUrl);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP OAuth] Background refresh failed: {ex.Message}");
            }
            finally
            {
                if (onStateChanged != null)
                {
                    EditorApplication.delayCall += () => onStateChanged();
                }
            }
        }

        private static async Task<string> RefreshAccessTokenAsync(
            string serverUrl,
            OAuthSessionV2 currentSession,
            params string[] requiredScopes)
        {
            if (currentSession == null || string.IsNullOrEmpty(currentSession.refreshToken) || string.IsNullOrEmpty(serverUrl))
            {
                return null;
            }

            string[] normalizedRequiredScopes = NormalizeRequiredScopes(requiredScopes);

            string formBody = $"grant_type=refresh_token&client_id={Uri.EscapeDataString(ClientId)}&refresh_token={Uri.EscapeDataString(currentSession.refreshToken)}";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{serverUrl.TrimEnd('/')}/api/auth/oauth2/token");
            httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json");
            httpRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
            httpRequest.Content = new StringContent(formBody, Encoding.UTF8, "application/x-www-form-urlencoded");

            HttpResponseMessage httpResponse;
            string tokenJson;
            try
            {
                httpResponse = await s_tokenHttpClient.SendAsync(httpRequest);
                tokenJson = await httpResponse.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP OAuth] Token refresh network error: {ex.Message}");
                return null;
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                if (IsInvalidGrantResponse((long)httpResponse.StatusCode, tokenJson))
                {
                    SignOut();
                }

                return null;
            }

            OAuthSessionV2 refreshedSession = BuildSessionFromTokenResponse(tokenJson, currentSession);
            if (refreshedSession == null || string.IsNullOrEmpty(refreshedSession.accessToken))
            {
                return null;
            }

            if (!HasRequiredScopes(refreshedSession.scope, normalizedRequiredScopes))
            {
                Debug.LogWarning(
                    $"[YUCP OAuth] Refreshed session is missing required scope '{string.Join(" ", normalizedRequiredScopes)}'.");
                if (ShouldClearSessionForMissingRequiredScopes(normalizedRequiredScopes))
                {
                    SignOut();
                }
                return null;
            }

            PersistSession(refreshedSession);
            return refreshedSession.accessToken;
        }

        private static OAuthSessionV2 BuildSessionFromTokenResponse(string tokenJson, OAuthSessionV2 previousSession)
        {
            JObject token;
            try
            {
                token = JObject.Parse(tokenJson);
            }
            catch
            {
                return null;
            }

            string accessToken = token.Value<string>("access_token");
            if (string.IsNullOrEmpty(accessToken))
            {
                return null;
            }

            long accessTokenExpiresAt = ResolveExpiryTimestamp(
                token,
                "expires_in",
                "expires_at",
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() +
                    3600 -
                    AccessTokenSkewSeconds);
            string refreshToken = token.Value<string>("refresh_token");
            if (string.IsNullOrEmpty(refreshToken))
            {
                refreshToken = previousSession?.refreshToken;
            }

            long refreshTokenExpiresAt = ResolveRefreshExpiryTimestamp(
                token,
                previousSession?.refreshTokenExpiresAt ?? 0);
            string scope = token.Value<string>("scope");
            if (string.IsNullOrEmpty(scope))
            {
                scope = previousSession?.scope;
            }

            string userId = ParseJwtClaim(accessToken, "sub");
            if (string.IsNullOrEmpty(userId))
            {
                userId = previousSession?.userId;
            }

            string displayName = ParseJwtClaim(accessToken, "name");
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = previousSession?.displayName;
            }

            return new OAuthSessionV2
            {
                storageVersion = 2,
                accessToken = accessToken,
                accessTokenExpiresAt = accessTokenExpiresAt,
                refreshToken = refreshToken,
                refreshTokenExpiresAt = refreshTokenExpiresAt,
                userId = userId,
                displayName = displayName,
                scope = scope,
            };
        }

        private static long ResolveExpiryTimestamp(
            JObject token,
            string expiresInKey,
            string expiresAtKey,
            long fallback)
        {
            long? absoluteExpiry = token.Value<long?>(expiresAtKey);
            if (absoluteExpiry > 0)
            {
                return absoluteExpiry.Value;
            }

            int? expiresInSeconds = token.Value<int?>(expiresInKey);
            if (expiresInSeconds > 0)
            {
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() +
                    expiresInSeconds.Value -
                    AccessTokenSkewSeconds;
            }

            return fallback;
        }

        private static long ResolveRefreshExpiryTimestamp(
            JObject token,
            long previousValue)
        {
            long? absoluteExpiry = token.Value<long?>(
                "refresh_token_expires_at");
            if (absoluteExpiry > 0)
            {
                return absoluteExpiry.Value;
            }

            int? expiresInSeconds = token.Value<int?>(
                "refresh_token_expires_in");
            if (expiresInSeconds > 0)
            {
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() +
                    expiresInSeconds.Value;
            }

            return previousValue;
        }

        private static bool TryGetActiveSession(out OAuthSessionV2 session)
        {
            if (TryGetCachedSession(out session))
            {
                if (HasUsableAccessToken(session) || (IsRefreshableSession(session) && HasRequiredScope(session.scope, Domain.RequiredScope)))
                {
                    PersistPresenceHints(session);
                    return true;
                }
            }

            session = null;
            return false;
        }

        private static bool TryGetCachedSession(out OAuthSessionV2 session)
        {
            session = LoadPersistentSession();
            return session != null;
        }

        private static bool HasUsableAccessToken(OAuthSessionV2 session)
        {
            return HasUsableAccessToken(session, Domain.RequiredScope);
        }

        private static bool HasUsableAccessToken(OAuthSessionV2 session, params string[] requiredScopes)
        {
            string[] normalizedRequiredScopes = NormalizeRequiredScopes(requiredScopes);
            return session != null
                && !string.IsNullOrEmpty(session.accessToken)
                && HasRequiredScopes(session.scope, normalizedRequiredScopes)
                && session.accessTokenExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + AccessTokenSkewSeconds;
        }

        private static bool HasRequiredScope(string scopeValue, string requiredScope)
        {
            return HasRequiredScopes(scopeValue, requiredScope);
        }

        private static bool HasRequiredScopes(string scopeValue, params string[] requiredScopes)
        {
            string[] normalizedRequiredScopes = NormalizeRequiredScopes(requiredScopes);
            if (string.IsNullOrWhiteSpace(scopeValue) || normalizedRequiredScopes.Length == 0)
            {
                return false;
            }

            string[] scopes = scopeValue.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string requiredScope in normalizedRequiredScopes)
            {
                bool matched = false;
                foreach (string scope in scopes)
                {
                    if (string.Equals(scope.Trim(), requiredScope, StringComparison.Ordinal))
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    return false;
                }
            }

            return true;
        }

        private static string[] NormalizeRequiredScopes(params string[] requiredScopes)
        {
            if (requiredScopes == null || requiredScopes.Length == 0)
            {
                return new[] { Domain.RequiredScope };
            }

            var normalized = new List<string>();
            foreach (string requiredScope in requiredScopes)
            {
                string value = requiredScope?.Trim();
                if (!string.IsNullOrEmpty(value) && !normalized.Contains(value))
                {
                    normalized.Add(value);
                }
            }

            return normalized.Count == 0 ? new[] { Domain.RequiredScope } : normalized.ToArray();
        }

        private static bool ShouldClearSessionForMissingRequiredScopes(IReadOnlyList<string> requiredScopes)
        {
            return requiredScopes != null && requiredScopes.Count > 0;
        }

        private static bool IsRefreshableSession(OAuthSessionV2 session)
        {
            if (session == null || string.IsNullOrEmpty(session.refreshToken))
            {
                return false;
            }

            if (session.refreshTokenExpiresAt <= 0)
            {
                return SupportsProtectedSessionStorage();
            }

            return SupportsProtectedSessionStorage() && session.refreshTokenExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private static void PersistSession(OAuthSessionV2 session)
        {
            if (session == null)
            {
                return;
            }

            ClearCurrentDomainKeys();
            PersistPresenceHints(session);

            if (!SupportsProtectedSessionStorage())
            {
                if (HasUsableAccessToken(session))
                {
                    EditorPrefs.SetString(KeyToken, session.accessToken);
                    EditorPrefs.SetInt(KeyExpiry, (int)session.accessTokenExpiresAt);
                }
                return;
            }

            string sessionJson = JsonUtility.ToJson(session);
            byte[] sessionBytes = Encoding.UTF8.GetBytes(sessionJson);

#if UNITY_EDITOR_WIN
            byte[] protectedBytes = ProtectForCurrentUser(sessionBytes);
            string sessionPath = GetSessionFilePath();
            string sessionDir = Path.GetDirectoryName(sessionPath);
            if (!string.IsNullOrEmpty(sessionDir))
            {
                Directory.CreateDirectory(sessionDir);
            }

            string tempPath = sessionPath + ".tmp";
            File.WriteAllBytes(tempPath, protectedBytes);
            if (File.Exists(sessionPath))
            {
                File.Delete(sessionPath);
            }
            File.Move(tempPath, sessionPath);
#endif
        }

        private static OAuthSessionV2 LoadPersistentSession()
        {
            if (!SupportsProtectedSessionStorage())
            {
                return null;
            }

            try
            {
                string sessionPath = GetSessionFilePath();
                if (!File.Exists(sessionPath))
                {
                    return null;
                }

#if UNITY_EDITOR_WIN
                byte[] protectedBytes = File.ReadAllBytes(sessionPath);
                byte[] sessionBytes = UnprotectForCurrentUser(protectedBytes);
                string sessionJson = Encoding.UTF8.GetString(sessionBytes);
                var session = JsonUtility.FromJson<OAuthSessionV2>(sessionJson);
                if (session == null || session.storageVersion < 2)
                {
                    ClearPersistentSession();
                    return null;
                }

                return session;
#else
                return null;
#endif
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP OAuth] Failed to read persistent session: {ex.Message}");
                ClearPersistentSession();
                return null;
            }
        }

        private static void ClearPersistentSession()
        {
            if (!SupportsProtectedSessionStorage())
            {
                return;
            }

            try
            {
                string sessionPath = GetSessionFilePath();
                if (File.Exists(sessionPath))
                {
                    File.Delete(sessionPath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP OAuth] Failed to clear persistent session: {ex.Message}");
            }
        }

        private static void PersistPresenceHints(OAuthSessionV2 session)
        {
            if (session == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(session.userId))
            {
                EditorPrefs.SetString(KeyUserId, session.userId);
            }

            if (!string.IsNullOrEmpty(session.displayName))
            {
                EditorPrefs.SetString(KeyDisplayName, session.displayName);
            }

            EditorPrefs.SetString(KeySessionVersion, CurrentSessionVersion);
        }

        private static void ClearCurrentDomainKeys()
        {
            EditorPrefs.DeleteKey(KeyToken);
            EditorPrefs.DeleteKey(KeyExpiry);
            EditorPrefs.DeleteKey(KeyUserId);
            EditorPrefs.DeleteKey(KeyDisplayName);
            EditorPrefs.DeleteKey(KeySessionVersion);
        }

        private static bool SupportsProtectedSessionStorage()
        {
#if UNITY_EDITOR_WIN
            return true;
#else
            return false;
#endif
        }

        private static string GetSessionFilePath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "YUCP", "Auth", Domain.SessionFileName);
        }

#if UNITY_EDITOR_WIN
        private static byte[] ProtectForCurrentUser(byte[] data)
        {
            return RunCryptOperation(data, true);
        }

        private static byte[] UnprotectForCurrentUser(byte[] data)
        {
            return RunCryptOperation(data, false);
        }

        private static byte[] RunCryptOperation(byte[] data, bool protect)
        {
            if (data == null || data.Length == 0)
            {
                return Array.Empty<byte>();
            }

            DataBlob inputBlob = default;
            DataBlob entropyBlob = default;
            DataBlob outputBlob = default;

            try
            {
                inputBlob = CreateBlob(data);
                entropyBlob = CreateBlob(SessionEntropy);

                bool success = protect
                    ? CryptProtectData(ref inputBlob, "YUCP Unity Session", ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob)
                    : CryptUnprotectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob);

                if (!success)
                {
                    throw new InvalidOperationException("Windows DPAPI operation failed.");
                }

                byte[] result = new byte[outputBlob.cbData];
                Marshal.Copy(outputBlob.pbData, result, 0, outputBlob.cbData);
                return result;
            }
            finally
            {
                FreeBlob(ref inputBlob);
                FreeBlob(ref entropyBlob);
                FreeBlob(ref outputBlob, true);
            }
        }

        private static DataBlob CreateBlob(byte[] data)
        {
            var blob = new DataBlob();
            if (data == null || data.Length == 0)
            {
                return blob;
            }

            blob.cbData = data.Length;
            blob.pbData = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, blob.pbData, data.Length);
            return blob;
        }

        private static void FreeBlob(ref DataBlob blob, bool useLocalFree = false)
        {
            if (blob.pbData == IntPtr.Zero)
            {
                return;
            }

            if (useLocalFree)
            {
                LocalFree(blob.pbData);
            }
            else
            {
                Marshal.FreeHGlobal(blob.pbData);
            }

            blob.pbData = IntPtr.Zero;
            blob.cbData = 0;
        }
#endif

        private static bool IsInvalidGrantResponse(long responseCode, string responseBody)
        {
            if (responseCode != 400 && responseCode != 401)
            {
                return false;
            }

            string error = null;
            try
            {
                error = JObject.Parse(responseBody).Value<string>("error");
            }
            catch
            {
            }
            if (string.Equals(error, "invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return responseBody.IndexOf("invalid_grant", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static async Task SendSuccessPageAsync(HttpListenerContext context)
        {
            string relayUrl = Interlocked.Exchange(ref s_pendingVerifyRelayUrl, null);
            byte[] html = Encoding.UTF8.GetBytes(BuildSuccessHtml(relayUrl));
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = html.Length;
            await context.Response.OutputStream.WriteAsync(html, 0, html.Length);
            context.Response.OutputStream.Close();
        }

        private static async Task SendErrorPageAsync(HttpListenerContext context, string errorMessage)
        {
            try
            {
                byte[] html = Encoding.UTF8.GetBytes(BuildErrorHtml(errorMessage));
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = html.Length;
                await context.Response.OutputStream.WriteAsync(html, 0, html.Length);
                context.Response.OutputStream.Close();
            }
            catch
            {
            }
        }

        internal static string BuildAuthUrl(
            string serverUrl,
            string codeChallenge,
            string state,
            string redirectUri)
        {
            return $"{serverUrl.TrimEnd('/')}/api/auth/oauth2/authorize"
                + $"?client_id={Uri.EscapeDataString(ClientId)}"
                + "&response_type=code"
                + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
                + "&code_challenge_method=S256"
                + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                + $"&state={Uri.EscapeDataString(state)}"
                + $"&scope={Uri.EscapeDataString(Domain.RequestedScopeValue)}";
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

        private static string ParseJwtClaim(string jwt, string claim)
        {
            try
            {
                string[] parts = jwt.Split('.');
                if (parts.Length < 2)
                {
                    return null;
                }

                string payload = parts[1].Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2:
                        payload += "==";
                        break;
                    case 3:
                        payload += "=";
                        break;
                }

                string decoded = Encoding.UTF8.GetString(
                    Convert.FromBase64String(payload));
                return JObject.Parse(decoded).Value<string>(claim);
            }
            catch
            {
                return null;
            }
        }

        private static string BuildAuthorizationErrorMessage(string description, string expectedScope)
        {
            string normalized = NormalizeAuthorizationDescription(description);
            if (TryExtractInvalidScope(normalized, out string invalidScope))
            {
                string scopeLabel = string.IsNullOrEmpty(invalidScope) ? expectedScope : invalidScope;
                return
                    $"Authorization error: {UnityOAuthScopeRejectionMarker} " +
                    $"The deployment rejected the required Unity scope '{scopeLabel}'. " +
                    "Return to Unity and sign in again later.";
            }

            return $"Authorization error: {normalized}";
        }

        private static string NormalizeAuthorizationDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return "The server returned an unknown authorization error.";
            }

            return description.Replace('+', ' ').Trim();
        }

        private static bool TryExtractInvalidScope(string description, out string scope)
        {
            const string marker = "The following scopes are invalid:";
            int index = description.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                scope = null;
                return false;
            }

            string remainder = description.Substring(index + marker.Length).Trim();
            if (string.IsNullOrEmpty(remainder))
            {
                scope = null;
                return true;
            }

            int separator = remainder.IndexOfAny(new[] { ',', ';' });
            scope = (separator >= 0 ? remainder.Substring(0, separator) : remainder).Trim();
            return true;
        }

        private static string BuildErrorHtml(string errorMessage)
        {
            string escaped = WebUtility.HtmlEncode(errorMessage);
            string details = $"<div class=\"detail-card detail-card-error\"><span class=\"detail-label\">Details</span><div class=\"detail-body\">{escaped}</div></div>";
            return CallbackPageHtmlBuilder.Build(
                "We could not finish the YUCP sign-in",
                "Return to Unity, review the details below, and try again once the server is ready.",
                details,
                "#fb7185",
                "#f59e0b");
        }

        private static string BuildSuccessHtml(string pendingVerifyRelayUrl = null)
        {
            if (pendingVerifyRelayUrl != null)
            {
                return CallbackPageHtmlBuilder.Build(
                    "Signed in!",
                    "Redirecting to purchase verification\u2026",
                    string.Empty,
                    "#36bfb1",
                    "#2da89c",
                    redirectUrl: pendingVerifyRelayUrl);
            }

            return CallbackPageHtmlBuilder.Build(
                "Creator Identity is ready",
                "Return to Unity. Your purchase verification controls are now available in the YUCP Package Manager.",
                "<div class=\"detail-card detail-card-success\"><span class=\"detail-label\">Next</span><div class=\"detail-body\">You can close this tab and continue in Unity.</div></div>",
                "#36bfb1",
                "#2da89c");
        }

    }
}
