using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using YUCP.Importer.Editor.Batch;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class IdentityBootstrapEntryTests
    {
        [Test]
        public void ValidateRequestAcceptsLoopbackHttp()
        {
            var request = new IdentityBootstrapRequest
            {
                schemaVersion = 1,
                runId = "identity-1",
                serverUrl = "http://127.0.0.1:3000",
            };

            Assert.DoesNotThrow(() =>
                IdentityBootstrapEntry.ValidateRequest(request));
        }

        [Test]
        public void ValidateRequestRejectsRemoteHttp()
        {
            var request = new IdentityBootstrapRequest
            {
                schemaVersion = 1,
                runId = "identity-2",
                serverUrl = "http://192.0.2.1:3000",
            };

            Assert.Throws<InvalidDataException>(() =>
                IdentityBootstrapEntry.ValidateRequest(request));
        }

        [Test]
        public void AuthorizationEventContainsNoCredentialMaterial()
        {
            IdentityBootstrapEvent value =
                IdentityBootstrapEntry.CreateAuthorizationEvent(
                    "identity-3",
                    "https://creator.example/api/yucp/oauth/authorize?state=state-1");
            string json = JsonUtility.ToJson(value);

            StringAssert.Contains("\"authorizationUrl\"", json);
            StringAssert.DoesNotContain("codeVerifier", json);
            StringAssert.DoesNotContain("accessToken", json);
            StringAssert.DoesNotContain("refreshToken", json);
        }

        [Test]
        public void AuthorizationTargetsBetterAuthWithRefreshScope()
        {
            string url = CreatorIdentityOAuthService.BuildAuthUrl(
                "https://creator.example",
                "challenge-1",
                "state-1",
                "http://127.0.0.1:49152/callback");

            StringAssert.StartsWith(
                "https://creator.example/api/auth/oauth2/authorize?",
                url);
            StringAssert.Contains("offline_access", url);
            StringAssert.Contains(
                "redirect_uri=http%3A%2F%2F127.0.0.1%3A49152%2Fcallback",
                url);
            StringAssert.DoesNotContain("/api/yucp/oauth/", url);
        }

        [Test]
        public void FailureResultPreservesRunIdentifier()
        {
            var request = new IdentityBootstrapRequest
            {
                schemaVersion = 1,
                runId = "identity-failure-1",
                serverUrl = "http://127.0.0.1:3001",
            };

            IdentityBootstrapResult result =
                IdentityBootstrapEntry.BuildFailureResult(
                    request,
                    new InvalidOperationException("failed"));

            Assert.AreEqual("identity-failure-1", result.runId);
        }

        [Test]
        public void StartupResumesAfterDomainReloadForTheIdentityCommand()
        {
            bool shouldResume =
                IdentityBootstrapEntry.ShouldResumeAfterDomainReload(
                    true,
                    @"C:\temp\request.json",
                    @"C:\temp\result.json",
                    new[]
                    {
                        "Unity.exe",
                        "-batchmode",
                        "-executeMethod",
                        "YUCP.Importer.Editor.Batch.IdentityBootstrapEntry.Run",
                    });

            Assert.IsTrue(shouldResume);
        }
    }
}
