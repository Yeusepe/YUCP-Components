using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class CreatorIdentityOAuthService
    {
        public const string ClientId = "yucp-unity-editor";

        private const string KeyToken = "YUCP_OAuth_AccessToken";
        private const string KeyExpiry = "YUCP_OAuth_TokenExpiry";
        private const string KeyUserId = "YUCP_OAuth_UserId";
        private const string KeyDisplayName = "YUCP_OAuth_DisplayName";
        private const string KeySessionVersion = "YUCP_OAuth_SessionVersion";
        private const string CurrentSessionVersion = "2";
        private const int AccessTokenSkewSeconds = 60;
        private static readonly byte[] SessionEntropy = Encoding.UTF8.GetBytes("YUCP.UnityEditor.Session.v2");
        private static readonly object SessionLock = new object();
        private static Task _backgroundRefreshTask;

#if UNITY_EDITOR_WIN
        private const int CryptProtectUiForbidden = 0x1;

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
            if (TryGetCachedSession(out OAuthSessionV2 session))
            {
                if (HasUsableAccessToken(session))
                {
                    PersistPresenceHints(session);
                    return session.accessToken;
                }

                if (!string.IsNullOrEmpty(session.refreshToken))
                {
                    string refreshedAccessToken = await RefreshAccessTokenAsync(serverUrl, session);
                    if (!string.IsNullOrEmpty(refreshedAccessToken))
                    {
                        return refreshedAccessToken;
                    }
                }
            }

            if (TryGetLegacyAccessToken(out string legacyToken, out long legacyExpiry))
            {
                var legacySession = new OAuthSessionV2
                {
                    storageVersion = 1,
                    accessToken = legacyToken,
                    accessTokenExpiresAt = legacyExpiry,
                    userId = EditorPrefs.GetString(KeyUserId, null),
                    displayName = EditorPrefs.GetString(KeyDisplayName, null),
                };
                PersistPresenceHints(legacySession);
                return legacyToken;
            }

            return null;
        }

        public static void SignOut()
        {
            ClearPersistentSession();
            ClearLegacyKeys();
            EditorPrefs.DeleteKey(KeySessionVersion);
        }

        public static async Task SignInAsync(string serverUrl, Action onSuccess, Action<string> onError)
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

                byte[] stateBytes = new byte[16];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(stateBytes);
                }

                string state = Base64UrlEncode(stateBytes);

                int port;
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();

                string redirectUri = $"http://127.0.0.1:{port}/callback";
                string authUrl = BuildAuthUrl(serverUrl, codeChallenge, state, redirectUri);

                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();

                Application.OpenURL(authUrl);

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
                        string message = $"Authorization error: {description}";
                        await SendErrorRedirectAsync(context, serverUrl, message);
                        onError?.Invoke(message);
                        return;
                    }

                    if (!query.TryGetValue("state", out string returnedState) || returnedState != state)
                    {
                        const string message = "State mismatch during Creator Identity sign-in. Please try again.";
                        await SendErrorRedirectAsync(context, serverUrl, message);
                        onError?.Invoke(message);
                        return;
                    }

                    if (!query.TryGetValue("code", out authCode) || string.IsNullOrEmpty(authCode))
                    {
                        const string message = "No authorization code was returned.";
                        await SendErrorRedirectAsync(context, serverUrl, message);
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
                onSuccess?.Invoke();
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Sign-in error: {ex.Message}");
            }
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

        private static async Task<string> RefreshAccessTokenAsync(string serverUrl, OAuthSessionV2 currentSession)
        {
            if (currentSession == null || string.IsNullOrEmpty(currentSession.refreshToken) || string.IsNullOrEmpty(serverUrl))
            {
                return null;
            }

            var form = new WWWForm();
            form.AddField("grant_type", "refresh_token");
            form.AddField("client_id", ClientId);
            form.AddField("refresh_token", currentSession.refreshToken);

            using var tokenRequest = UnityWebRequest.Post($"{serverUrl.TrimEnd('/')}/api/auth/oauth2/token", form);
            tokenRequest.SetRequestHeader("Accept", "application/json");
            tokenRequest.SetRequestHeader("Accept-Encoding", "identity");

            var operation = tokenRequest.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            string tokenJson = tokenRequest.downloadHandler?.text ?? string.Empty;
            if (tokenRequest.result != UnityWebRequest.Result.Success)
            {
                if (IsInvalidGrantResponse(tokenRequest.responseCode, tokenJson))
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

            PersistSession(refreshedSession);
            return refreshedSession.accessToken;
        }

        private static OAuthSessionV2 BuildSessionFromTokenResponse(string tokenJson, OAuthSessionV2 previousSession)
        {
            string accessToken = ExtractJsonString(tokenJson, "access_token");
            if (string.IsNullOrEmpty(accessToken))
            {
                return null;
            }

            long accessTokenExpiresAt = ResolveExpiryTimestamp(tokenJson, "expires_in", "expires_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600 - AccessTokenSkewSeconds);
            string refreshToken = ExtractJsonString(tokenJson, "refresh_token");
            if (string.IsNullOrEmpty(refreshToken))
            {
                refreshToken = previousSession?.refreshToken;
            }

            long refreshTokenExpiresAt = ResolveRefreshExpiryTimestamp(tokenJson, previousSession?.refreshTokenExpiresAt ?? 0);
            string scope = ExtractJsonString(tokenJson, "scope");
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

        private static long ResolveExpiryTimestamp(string tokenJson, string expiresInKey, string expiresAtKey, long fallback)
        {
            string expiresAtRaw = ExtractJsonValue(tokenJson, expiresAtKey);
            if (long.TryParse(expiresAtRaw, out long absoluteExpiry) && absoluteExpiry > 0)
            {
                return absoluteExpiry;
            }

            string expiresInRaw = ExtractJsonValue(tokenJson, expiresInKey);
            if (int.TryParse(expiresInRaw, out int expiresInSeconds) && expiresInSeconds > 0)
            {
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresInSeconds - AccessTokenSkewSeconds;
            }

            return fallback;
        }

        private static long ResolveRefreshExpiryTimestamp(string tokenJson, long previousValue)
        {
            string refreshExpiresAtRaw = ExtractJsonValue(tokenJson, "refresh_token_expires_at");
            if (long.TryParse(refreshExpiresAtRaw, out long absoluteExpiry) && absoluteExpiry > 0)
            {
                return absoluteExpiry;
            }

            string refreshExpiresInRaw = ExtractJsonValue(tokenJson, "refresh_token_expires_in");
            if (int.TryParse(refreshExpiresInRaw, out int expiresInSeconds) && expiresInSeconds > 0)
            {
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresInSeconds;
            }

            return previousValue;
        }

        private static bool TryGetActiveSession(out OAuthSessionV2 session)
        {
            if (TryGetCachedSession(out session))
            {
                if (HasUsableAccessToken(session) || IsRefreshableSession(session))
                {
                    PersistPresenceHints(session);
                    return true;
                }
            }

            if (TryGetLegacyAccessToken(out string legacyToken, out long legacyExpiry))
            {
                session = new OAuthSessionV2
                {
                    storageVersion = 1,
                    accessToken = legacyToken,
                    accessTokenExpiresAt = legacyExpiry,
                    userId = EditorPrefs.GetString(KeyUserId, null),
                    displayName = EditorPrefs.GetString(KeyDisplayName, null),
                };
                PersistPresenceHints(session);
                return true;
            }

            session = null;
            return false;
        }

        private static bool TryGetCachedSession(out OAuthSessionV2 session)
        {
            session = LoadPersistentSession();
            return session != null;
        }

        private static bool TryGetLegacyAccessToken(out string token, out long expiry)
        {
            token = null;
            expiry = 0;

            if (!EditorPrefs.HasKey(KeyToken) || !EditorPrefs.HasKey(KeyExpiry))
            {
                return false;
            }

            token = EditorPrefs.GetString(KeyToken, string.Empty);
            expiry = EditorPrefs.GetInt(KeyExpiry, 0);
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            return expiry > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + AccessTokenSkewSeconds;
        }

        private static bool HasUsableAccessToken(OAuthSessionV2 session)
        {
            return session != null
                && !string.IsNullOrEmpty(session.accessToken)
                && session.accessTokenExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + AccessTokenSkewSeconds;
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

            PersistPresenceHints(session);
            ClearLegacyKeys();

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

        private static void ClearLegacyKeys()
        {
            EditorPrefs.DeleteKey(KeyToken);
            EditorPrefs.DeleteKey(KeyExpiry);
            EditorPrefs.DeleteKey(KeyUserId);
            EditorPrefs.DeleteKey(KeyDisplayName);
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
            return Path.Combine(localAppData, "YUCP", "Auth", "unity-oauth-session-v2.dat");
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

            string error = ExtractJsonString(responseBody, "error");
            if (string.Equals(error, "invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return responseBody.IndexOf("invalid_grant", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static async Task SendSuccessPageAsync(HttpListenerContext context)
        {
            byte[] html = Encoding.UTF8.GetBytes(BuildSuccessHtml());
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = html.Length;
            await context.Response.OutputStream.WriteAsync(html, 0, html.Length);
            context.Response.OutputStream.Close();
        }

        private static async Task SendErrorRedirectAsync(HttpListenerContext context, string serverUrl, string errorMessage)
        {
            try
            {
                string errorUrl = $"{serverUrl.TrimEnd('/')}/oauth/error?error={Uri.EscapeDataString(errorMessage)}";
                context.Response.Redirect(errorUrl);
                context.Response.Close();
            }
            catch
            {
                try
                {
                    byte[] html = Encoding.UTF8.GetBytes(BuildErrorHtml(errorMessage));
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.ContentLength64 = html.Length;
                    await context.Response.OutputStream.WriteAsync(html, 0, html.Length);
                    context.Response.OutputStream.Close();
                }
                catch
                {
                }
            }
        }

        private static string BuildAuthUrl(string serverUrl, string codeChallenge, string state, string redirectUri)
        {
            return $"{serverUrl.TrimEnd('/')}/api/yucp/oauth/authorize"
                + $"?client_id={Uri.EscapeDataString(ClientId)}"
                + "&response_type=code"
                + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
                + "&code_challenge_method=S256"
                + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                + $"&state={Uri.EscapeDataString(state)}"
                + "&scope=cert%3Aissue";
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

        private static string ExtractJsonString(string json, string key)
        {
            string needle = $"\"{key}\"";
            int index = json.IndexOf(needle, StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            index += needle.Length;
            while (index < json.Length && (json[index] == ' ' || json[index] == ':' || json[index] == '\t'))
            {
                index++;
            }

            if (index >= json.Length || json[index] != '"')
            {
                return null;
            }

            index++;
            var builder = new StringBuilder();
            while (index < json.Length && json[index] != '"')
            {
                if (json[index] == '\\' && index + 1 < json.Length)
                {
                    index++;
                    switch (json[index])
                    {
                        case '"':
                            builder.Append('"');
                            break;
                        case '\\':
                            builder.Append('\\');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        default:
                            builder.Append(json[index]);
                            break;
                    }
                }
                else
                {
                    builder.Append(json[index]);
                }

                index++;
            }

            return builder.ToString();
        }

        private static string ExtractJsonValue(string json, string key)
        {
            string needle = $"\"{key}\"";
            int index = json.IndexOf(needle, StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            index += needle.Length;
            while (index < json.Length && (json[index] == ' ' || json[index] == ':' || json[index] == '\t'))
            {
                index++;
            }

            if (index >= json.Length)
            {
                return null;
            }

            var builder = new StringBuilder();
            while (index < json.Length && json[index] != ',' && json[index] != '}' && json[index] != '\r' && json[index] != '\n')
            {
                builder.Append(json[index++]);
            }

            return builder.ToString().Trim().Trim('"');
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

                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                return ExtractJsonString(decoded, claim);
            }
            catch
            {
                return null;
            }
        }

        private static string BuildErrorHtml(string errorMessage)
        {
            string escaped = WebUtility.HtmlEncode(errorMessage);
            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <title>YUCP Creator Identity</title>
  <style>
    body {{ background: #11161d; color: #f5f7fb; font-family: Segoe UI, sans-serif; min-height: 100vh; display:flex; align-items:center; justify-content:center; margin:0; }}
    .card {{ width:min(460px, calc(100vw - 32px)); background: linear-gradient(180deg, #1a2230 0%, #10161f 100%); border:1px solid rgba(255,255,255,.08); border-radius:24px; padding:40px 32px; box-shadow:0 24px 70px rgba(0,0,0,.45); }}
    h1 {{ margin:0 0 10px; font-size:24px; }}
    p {{ color:#a8b3c7; line-height:1.6; }}
    .detail {{ margin-top:18px; padding:14px 16px; background:rgba(255,98,98,.08); border:1px solid rgba(255,98,98,.22); border-radius:14px; color:#ffd8d8; font-family:Consolas, monospace; font-size:12px; }}
  </style>
</head>
<body>
  <div class=""card"">
    <h1>Creator Identity sign-in failed</h1>
    <p>Return to Unity Package Manager and try again.</p>
    <div class=""detail"">{escaped}</div>
  </div>
</body>
</html>";
        }

        private static string BuildSuccessHtml()
        {
            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <title>YUCP Creator Identity</title>
  <style>
    body { background: radial-gradient(circle at top left, #2a8c89 0%, #11161d 38%, #0c1016 100%); color: #f5f7fb; font-family: Segoe UI, sans-serif; min-height: 100vh; display:flex; align-items:center; justify-content:center; margin:0; }
    .card { width:min(480px, calc(100vw - 32px)); background:rgba(10,14,20,.74); backdrop-filter: blur(12px); border:1px solid rgba(255,255,255,.1); border-radius:28px; padding:44px 34px; box-shadow:0 28px 80px rgba(0,0,0,.45); text-align:center; }
    .badge { width:84px; height:84px; border-radius:42px; margin:0 auto 22px; display:flex; align-items:center; justify-content:center; background:rgba(72,214,190,.12); border:1px solid rgba(72,214,190,.34); font-size:38px; color:#48d6be; }
    h1 { margin:0 0 10px; font-size:28px; }
    p { margin:0; color:#b4c1d3; line-height:1.65; }
  </style>
</head>
<body>
  <div class=""card"">
    <div class=""badge"">+</div>
    <h1>Creator Identity connected</h1>
    <p>Return to Unity. Your purchase verification controls are now available in the YUCP Package Manager.</p>
  </div>
</body>
</html>";
        }
    }
}
