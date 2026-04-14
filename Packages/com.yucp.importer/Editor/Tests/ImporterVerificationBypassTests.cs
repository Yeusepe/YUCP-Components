using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YUCP.Importer.Editor.PackageManager;
using YUCP.Importer.Editor.PackageManager.Core;
using YUCP.Importer.Editor.PackageVerifier;
using YUCP.Importer.Editor.PackageVerifier.Settings;

namespace YUCP.Importer.Editor.Tests
{
    public class ImporterVerificationBypassTests
    {
        private const string PreferredServerUrlKey = "YUCP.PackageManager.PreferredServerUrl";
        private const string LegacyServerUrlKey = "yucp_server_url";
        private const string TestPackageId = "pkg-browser-verify-test";
        private const string TestKeyId = "ATTACKER-KEY-2026";
        private const string TestPublicKey = "SQF9r3TkKGwwQ6jGLBOABnq3UeOcHayQS3WbEJeUhnc=";

        [SetUp]
        public void SetUp()
        {
            ResetTrustBootstrapState();
            ClearVerificationArtifacts();
        }

        [TearDown]
        public void TearDown()
        {
            VerificationIntentServiceTestHooks.Reset();
            ResetTrustBootstrapState();
            ClearVerificationArtifacts();
        }

        [Test]
        public void ResolvePublicKey_DoesNotSeedTrustFromPreferredServer_WhenTrustListIsEmpty()
        {
            using var server = new LocalHttpServer(HandleAuthorityRequest);

            EditorPrefs.SetString(PreferredServerUrlKey, server.BaseUrl);

            byte[] resolvedKey = InvokeResolvePublicKey(TestKeyId);

            Assert.That(server.KeysEndpointHit, Is.False);
            Assert.That(resolvedKey, Is.Null);
            Assert.That(TrustedAuthoritiesSettings.GetCachedKeys().ContainsKey(TestKeyId), Is.False);
        }

        [Test]
        public void GetLicenseServerUrl_UsesDefaultTrustedUrl_WhenTrustListIsEmpty()
        {
            EditorPrefs.SetString(PreferredServerUrlKey, "http://127.0.0.1:43123");
            EditorPrefs.SetString(LegacyServerUrlKey, "https://legacy.example");

            string resolvedUrl = LicenseServerResolver.GetLicenseServerUrl();

            Assert.That(resolvedUrl, Is.EqualTo(TrustedAuthoritiesSettings.DefaultTrustedUrl));
        }

        [Test]
        public void GetLicenseServerUrl_IgnoresPreferredServerUrl_AfterTrustedUrlsExist()
        {
            TrustedAuthoritiesSettings.SetUrls(new List<string> { TrustedAuthoritiesSettings.DefaultTrustedUrl });
            EditorPrefs.SetString(PreferredServerUrlKey, "http://127.0.0.1:43123");

            string resolvedUrl = LicenseServerResolver.GetLicenseServerUrl();

            Assert.That(resolvedUrl, Is.EqualTo(TrustedAuthoritiesSettings.DefaultTrustedUrl));
        }

        [UnityTest]
        public System.Collections.IEnumerator VerifyInBrowserAsync_RedeemsInvalidToken_AndStillInvokesSuccess()
        {
            PersistVerificationSession();

            using var server = new LocalHttpServer(HandleVerificationIntentRequest);

            string observedSuccessToken = null;
            string observedError = null;
            Task callbackTask = null;
            using var successSignal = new ManualResetEventSlim(false);
            using var errorSignal = new ManualResetEventSlim(false);
            VerificationIntentServiceTestHooks.OpenUrlHandler = verificationUrl =>
            {
                callbackTask = Task.Run(server.TriggerVerificationCallbackAsync);
            };

            InvokeVerifyInBrowserAsync(
                server.BaseUrl,
                TestPackageId,
                "Browser Verify Test",
                CreateVerificationRequirements(
                    ("gumroad", "gumroad", "license", "Gumroad", "Test verification requirement")),
                token =>
                {
                    observedSuccessToken = token;
                    successSignal.Set();
                },
                error =>
                {
                    observedError = error;
                    errorSignal.Set();
                });

            float deadline = Time.realtimeSinceStartup + 30f;
            while (!successSignal.IsSet && !errorSignal.IsSet && Time.realtimeSinceStartup < deadline)
            {
                if (callbackTask != null && callbackTask.IsFaulted)
                {
                    observedError = callbackTask.Exception?.GetBaseException().Message;
                    errorSignal.Set();
                    break;
                }

                yield return null;
            }

            Assert.That(observedError, Is.Null.Or.Empty);
            Assert.That(callbackTask, Is.Not.Null);
            Assert.That(callbackTask.IsCompletedSuccessfully, Is.True);
            Assert.That(successSignal.IsSet, Is.True, "Expected verification flow to invoke success after redeem.");
            Assert.That(observedSuccessToken, Is.EqualTo("not-a-jwt"));
            Assert.That(SessionState.GetString(GetLicenseSessionKey(), null), Is.EqualTo("not-a-jwt"));
            Assert.That(LicenseVerificationService.GetCachedToken(TestPackageId), Is.Null);
        }

        private static void ClearVerificationArtifacts()
        {
            EditorPrefs.DeleteKey(PreferredServerUrlKey);
            EditorPrefs.DeleteKey(LegacyServerUrlKey);
            LicenseTokenCache.Evict(TestPackageId);
            InvokePrivateSignOutWithoutRegistry();
        }

        private static void ResetTrustBootstrapState()
        {
            TrustedAuthoritiesSettings.SetUrls(new List<string>());
            TrustedAuthoritiesSettings.ClearCachedKeys();
            TrustedAuthority.ReloadAllKeys();
            EditorPrefs.DeleteKey(PreferredServerUrlKey);
            EditorPrefs.DeleteKey(LegacyServerUrlKey);
        }

        private static byte[] InvokeResolvePublicKey(string keyId)
        {
            MethodInfo resolvePublicKey = typeof(YucpJwtTokenUtility).GetMethod(
                "ResolvePublicKey",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(resolvePublicKey, Is.Not.Null);
            return resolvePublicKey.Invoke(null, new object[] { keyId }) as byte[];
        }

        private static string GetLicenseSessionKey()
        {
            MethodInfo getSessionKey = typeof(LicenseTokenCache).GetMethod(
                "GetSessionKey",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(getSessionKey, Is.Not.Null);
            return getSessionKey.Invoke(null, new object[] { TestPackageId }) as string;
        }

        private static void PersistVerificationSession()
        {
            Type oauthType = typeof(CreatorIdentityOAuthService);
            Type sessionType = oauthType.GetNestedType("OAuthSessionV2", BindingFlags.NonPublic);
            Assert.That(sessionType, Is.Not.Null);

            object session = Activator.CreateInstance(sessionType, nonPublic: true);
            SetField(sessionType, session, "storageVersion", 2);
            SetField(sessionType, session, "accessToken", "access-token");
            SetField(sessionType, session, "accessTokenExpiresAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600);
            SetField(sessionType, session, "refreshToken", null);
            SetField(sessionType, session, "refreshTokenExpiresAt", 0L);
            SetField(sessionType, session, "userId", "user-123");
            SetField(sessionType, session, "displayName", "Test User");
            SetField(sessionType, session, "scope", "verification:read");

            MethodInfo persistSession = oauthType.GetMethod(
                "PersistSession",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(persistSession, Is.Not.Null);
            persistSession.Invoke(null, new object[] { session });
        }

        private static void InvokePrivateSignOutWithoutRegistry()
        {
            MethodInfo signOut = typeof(CreatorIdentityOAuthService).GetMethod(
                "SignOut",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(IReadOnlyList<InstalledPackageInfo>) },
                null);

            Assert.That(signOut, Is.Not.Null);
            signOut.Invoke(null, new object[] { new List<InstalledPackageInfo>() });
        }

        private static void SetField(Type type, object instance, string fieldName, object value)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected field '{fieldName}' on '{type.FullName}'.");
            field.SetValue(instance, value);
        }

        private static void InvokeVerifyInBrowserAsync(
            string serverUrl,
            string packageId,
            string packageName,
            Array requirements,
            Action<string> onSuccess,
            Action<string> onError)
        {
            MethodInfo verifyInBrowserAsync = GetCoreType("YUCP.Importer.Editor.PackageManager.Core.VerificationIntentService")
                .GetMethod("VerifyInBrowserAsync", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

            Assert.That(verifyInBrowserAsync, Is.Not.Null);
            verifyInBrowserAsync.Invoke(null, new object[] { serverUrl, packageId, packageName, requirements, onSuccess, onError, null });
        }

        private static Array CreateVerificationRequirements(params (string methodKey, string providerKey, string kind, string title, string description)[] requirements)
        {
            Type requirementType = GetCoreType("YUCP.Importer.Editor.PackageManager.Core.VerificationIntentService")
                .GetNestedType("VerificationRequirement", BindingFlags.NonPublic | BindingFlags.Public);

            Assert.That(requirementType, Is.Not.Null);

            Array array = Array.CreateInstance(requirementType, requirements.Length);
            for (int i = 0; i < requirements.Length; i++)
            {
                object requirement = Activator.CreateInstance(requirementType, nonPublic: true);
                SetField(requirementType, requirement, "methodKey", requirements[i].methodKey);
                SetField(requirementType, requirement, "providerKey", requirements[i].providerKey);
                SetField(requirementType, requirement, "kind", requirements[i].kind);
                SetField(requirementType, requirement, "title", requirements[i].title);
                SetField(requirementType, requirement, "description", requirements[i].description);
                array.SetValue(requirement, i);
            }

            return array;
        }

        private static Type GetCoreType(string fullName)
        {
            Type type = typeof(InstalledPackageInfo).Assembly.GetType(fullName, false);
            Assert.That(type, Is.Not.Null, $"Expected type '{fullName}'.");
            return type;
        }

        private static async Task HandleAuthorityRequest(HttpListenerContext context, LocalHttpServer server)
        {
            if (context.Request.HttpMethod == "GET" &&
                string.Equals(context.Request.Url.AbsolutePath, "/v1/keys", StringComparison.OrdinalIgnoreCase))
            {
                server.KeysEndpointHit = true;
                await server.WriteJsonAsync(
                    context,
                    "{\"authorities\":[{\"keyId\":\"" + TestKeyId + "\",\"publicKey\":\"" + TestPublicKey + "\",\"displayName\":\"Attacker Key\"}]}");
                return;
            }

            context.Response.StatusCode = 404;
            context.Response.Close();
        }

        private static async Task HandleVerificationIntentRequest(HttpListenerContext context, LocalHttpServer server)
        {
            string path = context.Request.Url.AbsolutePath;
            if (context.Request.HttpMethod == "POST" &&
                string.Equals(path, "/api/public/v2/verification-intents", StringComparison.OrdinalIgnoreCase))
            {
                var probe = JsonUtility.FromJson<VerificationIntentCreateProbe>(await server.ReadBodyAsync(context.Request));
                server.CapturedReturnUrl = probe?.returnUrl;

                await server.WriteJsonAsync(
                    context,
                    "{\"id\":\"intent-1\",\"packageId\":\"" + TestPackageId + "\",\"packageName\":\"Browser Verify Test\",\"status\":\"pending\",\"verificationUrl\":\"" + server.BaseUrl + "/browser\"}");
                return;
            }

            if (context.Request.HttpMethod == "POST" &&
                string.Equals(path, "/api/public/v2/verification-intents/intent-1/redeem", StringComparison.OrdinalIgnoreCase))
            {
                await server.WriteJsonAsync(
                    context,
                    "{\"success\":true,\"token\":\"not-a-jwt\",\"expiresAt\":4102444800}");
                return;
            }

            if (context.Request.HttpMethod == "GET" &&
                string.Equals(path, "/browser", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 204;
                context.Response.Close();
                return;
            }

            context.Response.StatusCode = 404;
            context.Response.Close();
        }

        [Serializable]
        private sealed class VerificationIntentCreateProbe
        {
            public string returnUrl;
        }

        private sealed class LocalHttpServer : IDisposable
        {
            private readonly HttpListener _listener;
            private readonly Func<HttpListenerContext, LocalHttpServer, Task> _handler;
            private readonly Task _listenTask;

            internal LocalHttpServer(Func<HttpListenerContext, LocalHttpServer, Task> handler)
            {
                _handler = handler;

                int port = FindFreePort();
                BaseUrl = $"http://127.0.0.1:{port}";
                _listener = new HttpListener();
                _listener.Prefixes.Add(BaseUrl + "/");
                _listener.Start();
                _listenTask = Task.Run(ListenAsync);
            }

            internal string BaseUrl { get; }
            internal bool KeysEndpointHit { get; set; }
            internal string CapturedReturnUrl { get; set; }

            public void Dispose()
            {
                try
                {
                    _listener.Stop();
                    _listener.Close();
                }
                catch
                {
                }

                try
                {
                    _listenTask.Wait(TimeSpan.FromSeconds(1));
                }
                catch
                {
                }
            }

            internal async Task<string> ReadBodyAsync(HttpListenerRequest request)
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
                return await reader.ReadToEndAsync();
            }

            internal async Task WriteJsonAsync(HttpListenerContext context, string json)
            {
                byte[] body = Encoding.UTF8.GetBytes(json);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body, 0, body.Length);
                context.Response.Close();
            }

            internal async Task TriggerVerificationCallbackAsync()
            {
                DateTime deadlineUtc = DateTime.UtcNow.AddSeconds(5);
                while (string.IsNullOrEmpty(CapturedReturnUrl) && DateTime.UtcNow < deadlineUtc)
                {
                    await Task.Delay(20);
                }

                if (string.IsNullOrEmpty(CapturedReturnUrl))
                {
                    throw new InvalidOperationException("Expected verification intent creation to capture the loopback return URL.");
                }

                using var client = new HttpClient();
                using HttpResponseMessage response = await client.GetAsync(CapturedReturnUrl + "?intent_id=intent-1&grant=grant-token");
                response.EnsureSuccessStatusCode();
            }

            private async Task ListenAsync()
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync();
                    }
                    catch
                    {
                        break;
                    }

                    _ = Task.Run(() => _handler(context, this));
                }
            }

            private static int FindFreePort()
            {
                var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                int port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                return port;
            }
        }
    }
}
