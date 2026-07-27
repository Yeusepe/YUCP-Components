using System;
using System.Collections;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.TestTools;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Tests.Editor
{
    public sealed class NativePackageBrokerCutoverTests
    {
        private static readonly string[] ExpectedRequestFields =
        {
            "aliasId",
            "approvedActiveContentDigest",
            "approvedPolicyVersion",
            "expectedCurrentReleaseRoot",
            "idempotencyKey",
            "operation",
            "projectIdentity",
            "projectPath",
            "runId",
            "schemaVersion",
            "targetReleaseRoot",
            "traceparent",
        };

        private static readonly string[] ForbiddenSourceNames =
        {
            "CallbackPageHtmlBuilder.cs",
            "CreatorIdentityOAuthService.cs",
            "LicenseTokenCache.cs",
            "LicenseVerificationService.cs",
            "PendingVerifyRelay.cs",
            "TransferHelperTufTrust.cs",
            "VerificationIntentService.cs",
            "YucpJwtTokenUtility.cs",
        };

        private static readonly string[] ForbiddenUnitySecretTerms =
        {
            "accessToken",
            "deliveryGrant",
            "installSession",
            "refreshToken",
            "tufMetadataUrl",
            "tufRootPath",
            "tufTargetsUrl",
            "tufTrustTarget",
        };

        [Test]
        public void BrokerRequestContainsOnlyHighLevelOperationFields()
        {
            Type requestType = typeof(PackageContractV2).Assembly.GetType(
                "YUCP.Importer.Editor.PackageManager.Core." +
                "NativePackageBrokerRequest");

            Assert.That(
                requestType,
                Is.Not.Null,
                "The native package broker request is missing.");

            string[] fields = requestType
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(fields, Is.EqualTo(ExpectedRequestFields));
        }

        [UnityTest]
        public IEnumerator NamedPipeTransportRejectsAnUnboundedFrameBeforeNewline()
        {
            string pipeName =
                "yucp-package-broker-oversized-" +
                Guid.NewGuid().ToString("N");
            using (var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous))
            {
                Task serverTask = RunOversizedFrameServerAsync(server);
                var transport = new NamedPipePackageBrokerTransport(pipeName);
                Task<NativePackageBrokerResult> execution =
                    transport.ExecuteAsync(
                        ValidRequest("preflight"),
                        null,
                        CancellationToken.None);
                while (!execution.IsCompleted || !serverTask.IsCompleted)
                {
                    yield return null;
                }
                if (serverTask.IsFaulted)
                {
                    throw serverTask.Exception;
                }
                Assert.That(execution.IsFaulted, Is.True);
                var failure = execution.Exception.GetBaseException()
                    as NativePackageBrokerException;
                Assert.That(failure, Is.Not.Null);
                Assert.That(
                    failure.ErrorCode,
                    Is.EqualTo("BROKER_PROTOCOL_INVALID"));
            }
        }

        [Test]
        public void UnityPackageContainsNoCredentialOrTrustOwner()
        {
            string packageRoot = PackageInfo.FindForAssembly(
                typeof(PackageContractV2).Assembly).resolvedPath;

            foreach (string sourceName in ForbiddenSourceNames)
            {
                string[] matches = Directory
                    .GetFiles(
                        packageRoot,
                        sourceName,
                        SearchOption.AllDirectories);
                Assert.That(
                    matches,
                    Is.Empty,
                    $"{sourceName} keeps credential ownership in Unity.");
            }

            string[] sourceFiles = Directory.GetFiles(
                Path.Combine(packageRoot, "Editor"),
                "*.cs",
                SearchOption.AllDirectories);
            foreach (string sourcePath in sourceFiles)
            {
                string source = File.ReadAllText(sourcePath);
                foreach (string forbidden in ForbiddenUnitySecretTerms)
                {
                    Assert.That(
                        source,
                        Does.Not.Contain(forbidden).IgnoreCase,
                        $"{Path.GetFileName(sourcePath)} exposes {forbidden}.");
                }
            }
        }

        [Test]
        public void PackageManifestUsesFriendlyProductLanguage()
        {
            string packageRoot = PackageInfo.FindForAssembly(
                typeof(PackageContractV2).Assembly).resolvedPath;
            string manifest = File.ReadAllText(
                Path.Combine(packageRoot, "package.json"));

            Assert.That(
                manifest,
                Does.Contain(
                    "\"description\": \"Installs licensed YUCP products " +
                    "and supports update, repair, and removal.\""));
            foreach (string jargon in new[]
            {
                "Desync",
                "FBX-derived",
                "TUF",
                "transfer helper",
            })
            {
                Assert.That(
                    manifest,
                    Does.Not.Contain(jargon).IgnoreCase);
            }
        }

        [Test]
        public void BrokerProgressUsesOnlyFriendlyUserMessages()
        {
            Assert.That(
                NativePackageBrokerClient.GetFriendlyProgressMessage(
                    "preparing",
                    0,
                    0),
                Is.EqualTo("Preparing your package"));
            Assert.That(
                NativePackageBrokerClient.GetFriendlyProgressMessage(
                    "signing-in",
                    0,
                    0),
                Is.EqualTo("Opening secure sign-in"));
            Assert.That(
                NativePackageBrokerClient.GetFriendlyProgressMessage(
                    "verifying-access",
                    0,
                    0),
                Is.EqualTo("Checking your package access"));
            Assert.That(
                NativePackageBrokerClient.GetFriendlyProgressMessage(
                    "downloading",
                    50,
                    100),
                Is.EqualTo("Downloading your package (50%)"));
            Assert.That(
                NativePackageBrokerClient.GetFriendlyProgressMessage(
                    "verifying",
                    100,
                    100),
                Is.EqualTo("Checking package integrity"));
            Assert.That(
                NativePackageBrokerClient.GetFriendlyProgressMessage(
                    "assembling",
                    100,
                    100),
                Is.EqualTo("Preparing files for Unity"));
            Assert.That(
                NativePackageBrokerClient.GetFriendlyProgressMessage(
                    "finalizing",
                    100,
                    100),
                Is.EqualTo("Finishing installation"));
        }

        [Test]
        public void EveryBrokerPhaseHasExplicitFriendlyLifecycleProgress()
        {
            string[] phases =
            {
                "preparing",
                "signing-in",
                "verifying-access",
                "downloading",
                "verifying",
                "assembling",
                "finalizing",
            };
            foreach (bool preflight in new[] { true, false })
            {
                float priorProgress = -1f;
                foreach (string phase in phases)
                {
                    PackageLifecycleUserProgress progress =
                        PackageLifecycleCoordinator.BuildBrokerProgress(
                            new NativePackageBrokerProgress
                            {
                                completedBytes = 50,
                                phase = phase,
                                runId = "run-progress",
                                schemaVersion = 1,
                                sequence = 1,
                                totalBytes = 100,
                            },
                            "JAMMR",
                            preflight);

                    Assert.That(progress.message, Is.Not.Empty);
                    Assert.That(progress.progress, Is.InRange(0f, 1f));
                    Assert.That(
                        progress.progress,
                        Is.GreaterThan(priorProgress),
                        $"{phase} must visibly advance the flow.");
                    Assert.That(
                        progress.message,
                        Does.Not.Contain("broker").IgnoreCase);
                    Assert.That(
                        progress.message,
                        Does.Not.Contain("pipe").IgnoreCase);
                    Assert.That(
                        progress.message,
                        Does.Not.Contain("digest").IgnoreCase);
                    Assert.That(
                        progress.message,
                        Does.Not.Contain("policy").IgnoreCase);
                    priorProgress = progress.progress;
                }
            }

            Assert.That(
                PackageLifecycleCoordinator.BuildBrokerProgress(
                    new NativePackageBrokerProgress
                    {
                        phase = "signing-in",
                    },
                    "JAMMR",
                    false).message,
                Is.EqualTo("Opening secure sign-in..."));
            Assert.That(
                PackageLifecycleCoordinator.BuildBrokerProgress(
                    new NativePackageBrokerProgress
                    {
                        phase = "verifying-access",
                    },
                    "JAMMR",
                    false).message,
                Is.EqualTo("Waiting for purchase confirmation..."));
        }
        [Test]
        public void BrokerRejectsUnknownChallengeProgressAndResultFields()
        {
            Assert.Throws<NativePackageBrokerException>(() =>
                ReadFrame<NativePackageBrokerChallengeFrame>(
                    "{\"schemaVersion\":1,\"kind\":\"challenge\"," +
                    "\"clientNonce\":\"" + new string('A', 43) + "\"," +
                    "\"operationToken\":\"" + new string('B', 43) + "\"," +
                    "\"expiresAt\":\"2026-07-26T10:00:00Z\"," +
                    "\"unknownChallenge\":true}",
                    "challenge"));
            Assert.Throws<NativePackageBrokerException>(() =>
                ReadFrame<NativePackageBrokerServerFrame>(
                    "{\"schemaVersion\":1,\"kind\":\"progress\"," +
                    "\"progress\":{\"schemaVersion\":1," +
                    "\"runId\":\"run-preflight\",\"sequence\":1," +
                    "\"phase\":\"preparing\",\"completedBytes\":0," +
                    "\"totalBytes\":0,\"unknownProgress\":true}}",
                    null));
            Assert.Throws<NativePackageBrokerException>(() =>
                ReadFrame<NativePackageBrokerServerFrame>(
                    "{\"schemaVersion\":1,\"kind\":\"result\"," +
                    "\"result\":{\"schemaVersion\":3," +
                    "\"runId\":\"run-preflight\"," +
                    "\"operation\":\"preflight\"," +
                    "\"status\":\"failed\",\"exitCode\":1," +
                    "\"errorCode\":\"BROKER_BUSY\"," +
                    "\"errorMessage\":\"Try again.\"," +
                    "\"traceId\":\"" + new string('a', 32) + "\"," +
                    "\"targetReleaseRoot\":\"" + new string('0', 64) + "\"," +
                    "\"activeContentDigest\":\"\"," +
                    "\"activePolicyVersion\":\"\"," +
                    "\"versionId\":\"\",\"logicalBytes\":0," +
                    "\"logicalFiles\":0,\"stagingTree\":\"\"," +
                    "\"journalState\":\"\",\"files\":[]," +
                    "\"unknownResult\":true}}",
                    null));
        }
        [Test]
        public void PreflightSerializationOmitsUnknownAndApprovalFields()
        {
            NativePackageBrokerRequest request = ValidRequest(
                "preflight");

            string json =
                NativePackageBrokerClient.SerializeRequest(request);

            Assert.That(
                json,
                Does.Not.Contain("\"targetReleaseRoot\""));
            Assert.That(
                json,
                Does.Not.Contain(
                    "\"approvedActiveContentDigest\""));
            Assert.That(
                json,
                Does.Not.Contain("\"approvedPolicyVersion\""));
            foreach (string field in new[]
            {
                "aliasId",
                "expectedCurrentReleaseRoot",
                "idempotencyKey",
                "operation",
                "projectIdentity",
                "projectPath",
                "runId",
                "schemaVersion",
                "traceparent",
            })
            {
                Assert.That(
                    json,
                    Does.Contain("\"" + field + "\""));
            }
        }

        [Test]
        public void EveryLifecycleOperationBuildsAValidBrokerRequest()
        {
            foreach (string operation in new[]
            {
                "preflight",
                "install",
                "update",
                "repair",
                "rollback",
                "recover",
                "uninstall",
            })
            {
                NativePackageBrokerRequest request =
                    ValidRequest(operation);
                if (!string.Equals(
                    operation,
                    "preflight",
                    StringComparison.Ordinal))
                {
                    request.approvedActiveContentDigest =
                        new string('6', 64);
                    request.approvedPolicyVersion =
                        "active-content-policy-v1";
                    request.targetReleaseRoot =
                        new string('7', 64);
                }

                string json =
                    NativePackageBrokerClient.SerializeRequest(request);

                Assert.That(
                    json,
                    Does.Contain(
                        "\"operation\":\"" + operation + "\""));
                Assert.That(
                    json,
                    Does.Not.Contain("installSession"));
                Assert.That(json, Does.Not.Contain("deliveryGrant"));
                Assert.That(json, Does.Not.Contain("refreshToken"));
            }
        }

        [TestCase("targetReleaseRoot")]
        [TestCase("approvedActiveContentDigest")]
        [TestCase("approvedPolicyVersion")]
        public void SuccessfulBrokerResultRejectsMismatchedRequestBinding(
            string binding)
        {
            NativePackageBrokerRequest request = ValidRequest("update");
            request.targetReleaseRoot = new string('7', 64);
            request.approvedActiveContentDigest = new string('8', 64);
            request.approvedPolicyVersion = "active-content-policy-v1";
            var result = new NativePackageBrokerResult
            {
                activeContentDigest = request.approvedActiveContentDigest,
                activePolicyVersion = request.approvedPolicyVersion,
                exitCode = 0,
                files = new System.Collections.Generic.List<
                    NativePackageBrokerFile>(),
                operation = request.operation,
                runId = request.runId,
                schemaVersion = NativePackageBrokerClient.SchemaVersion,
                status = "succeeded",
                targetReleaseRoot = request.targetReleaseRoot,
            };
            switch (binding)
            {
                case "targetReleaseRoot":
                    result.targetReleaseRoot = new string('9', 64);
                    break;
                case "approvedActiveContentDigest":
                    result.activeContentDigest = new string('a', 64);
                    break;
                case "approvedPolicyVersion":
                    result.activePolicyVersion =
                        "active-content-policy-v2";
                    break;
                default:
                    Assert.Fail("The test binding is invalid.");
                    break;
            }

            Assert.Throws<InvalidDataException>(() =>
                NativePackageBrokerClient.ValidateResult(request, result));
        }

        [Test]
        public void BrokerTransportContractSupportsCancellation()
        {
            MethodInfo method = typeof(INativePackageBrokerTransport).GetMethod(
                "ExecuteAsync");

            Assert.That(method, Is.Not.Null);
            ParameterInfo[] parameters = method.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(3));
            Assert.That(
                parameters[2].ParameterType,
                Is.EqualTo(typeof(CancellationToken)));
        }

        [UnityTest]
        public IEnumerator NamedPipeTransportCompletesTheChallengeAndStreamsProgress()
        {
            string pipeName =
                "yucp-package-broker-test-" +
                Guid.NewGuid().ToString("N");
            var receivedProgress =
                new System.Collections.Generic.List<
                    NativePackageBrokerProgress>();
            using (var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous))
            {
                Task serverTask = RunBrokerServerAsync(server);
                var transport =
                    new NamedPipePackageBrokerTransport(pipeName);
                NativePackageBrokerRequest request =
                    ValidRequest("preflight");

                Task<NativePackageBrokerResult> execution =
                    transport.ExecuteAsync(
                        request,
                        receivedProgress.Add,
                        CancellationToken.None);
                while (!execution.IsCompleted || !serverTask.IsCompleted)
                {
                    yield return null;
                }
                if (execution.IsFaulted)
                {
                    throw execution.Exception;
                }
                if (serverTask.IsFaulted)
                {
                    throw serverTask.Exception;
                }
                NativePackageBrokerResult result = execution.Result;

                Assert.That(result.status, Is.EqualTo("succeeded"));
                Assert.That(result.runId, Is.EqualTo(request.runId));
                Assert.That(receivedProgress, Has.Count.EqualTo(1));
                Assert.That(
                    receivedProgress[0].phase,
                    Is.EqualTo("verifying-access"));
            }
        }

        [UnityTest]
        public IEnumerator FrozenGoBrokerCompletesProductionPipePreflight()
        {
            if (!string.Equals(
                Environment.GetEnvironmentVariable(
                    "YUCP_TEST_REAL_PACKAGE_BROKER"),
                "1",
                StringComparison.Ordinal))
            {
                Assert.Ignore(
                    "Set YUCP_TEST_REAL_PACKAGE_BROKER=1 and start the " +
                    "frozen Go broker harness.");
            }

            var receivedProgress =
                new System.Collections.Generic.List<
                    NativePackageBrokerProgress>();
            NativePackageBrokerRequest request =
                ValidRequest("preflight");
            var transport = new NamedPipePackageBrokerTransport();
            Task<NativePackageBrokerResult> execution =
                transport.ExecuteAsync(
                    request,
                    receivedProgress.Add,
                    CancellationToken.None);
            while (!execution.IsCompleted)
            {
                yield return null;
            }
            if (execution.IsFaulted)
            {
                throw execution.Exception;
            }
            NativePackageBrokerResult result = execution.Result;

            NativePackageBrokerClient.ValidateResult(request, result);
            Assert.That(result.status, Is.EqualTo("succeeded"));
            Assert.That(result.runId, Is.EqualTo(request.runId));
            Assert.That(
                result.targetReleaseRoot,
                Is.EqualTo(new string('1', 64)));
            Assert.That(
                result.activeContentDigest,
                Is.EqualTo(new string('3', 64)));
            Assert.That(
                result.activePolicyVersion,
                Is.EqualTo("integration-test-policy-v1"));
            Assert.That(receivedProgress, Is.Not.Empty);
            Assert.That(
                receivedProgress.All(progress =>
                    progress.runId == request.runId),
                Is.True);
        }

        private static async Task RunBrokerServerAsync(
            NamedPipeServerStream server)
        {
            await server.WaitForConnectionAsync();
            using (var reader = new StreamReader(
                server,
                new UTF8Encoding(false, true),
                true,
                4096,
                true))
            using (var writer = new StreamWriter(
                server,
                new UTF8Encoding(false, true),
                4096,
                true))
            {
                writer.NewLine = "\n";
                writer.AutoFlush = true;
                NativePackageBrokerBeginFrame begin =
                    JsonUtility.FromJson<
                        NativePackageBrokerBeginFrame>(
                        await reader.ReadLineAsync());
                Assert.That(
                    begin.schemaVersion,
                    Is.EqualTo(1));
                Assert.That(
                    begin.kind,
                    Is.EqualTo("begin"));
                string nonce = begin.clientNonce;
                Assert.That(nonce, Has.Length.EqualTo(43));

                await writer.WriteLineAsync(
                    JsonUtility.ToJson(
                        new NativePackageBrokerChallengeFrame
                    {
                        schemaVersion = 1,
                        kind = "challenge",
                        clientNonce = nonce,
                        operationToken = new string('A', 43),
                        expiresAt =
                            DateTimeOffset.UtcNow
                                .AddSeconds(30)
                                .ToString("O"),
                    }));
                NativePackageBrokerOperateFrame operate =
                    JsonUtility.FromJson<
                        NativePackageBrokerOperateFrame>(
                        await reader.ReadLineAsync());
                Assert.That(
                    operate.kind,
                    Is.EqualTo("operate"));
                Assert.That(
                    operate.request?.schemaVersion,
                    Is.EqualTo(3));
                string runId = operate.request?.runId;

                await writer.WriteLineAsync(
                    JsonUtility.ToJson(
                        new NativePackageBrokerServerFrame
                    {
                        schemaVersion = 1,
                        kind = "progress",
                        progress =
                            new NativePackageBrokerProgress
                        {
                            schemaVersion = 1,
                            runId = runId,
                            sequence = 1,
                            phase = "verifying-access",
                            completedBytes = 0,
                            totalBytes = 0,
                        },
                    }));
                await writer.WriteLineAsync(
                    JsonUtility.ToJson(
                        new NativePackageBrokerServerFrame
                    {
                        schemaVersion = 1,
                        kind = "result",
                        result =
                            new NativePackageBrokerResult
                        {
                            schemaVersion = 3,
                            runId = runId,
                            operation = "preflight",
                            status = "succeeded",
                            exitCode = 0,
                            errorCode = "",
                            errorMessage = "",
                            traceId =
                                new string('a', 32),
                            targetReleaseRoot =
                                new string('1', 64),
                            activeContentDigest =
                                new string('2', 64),
                            activePolicyVersion =
                                "active-content-policy-v1",
                            versionId = "version-1",
                            logicalBytes = 0,
                            logicalFiles = 0,
                            stagingTree = "",
                            journalState = "preflight-complete",
                            files =
                                new System.Collections.Generic.List<
                                    NativePackageBrokerFile>(),
                        },
                    }));
            }
        }

        private static async Task RunOversizedFrameServerAsync(
            NamedPipeServerStream server)
        {
            await server.WaitForConnectionAsync();
            using (var reader = new StreamReader(
                server,
                new UTF8Encoding(false, true),
                true,
                4096,
                true))
            using (var writer = new StreamWriter(
                server,
                new UTF8Encoding(false, true),
                4096,
                true))
            {
                await reader.ReadLineAsync();
                await writer.WriteAsync(new string('x', 1024 * 1024 + 1));
                await writer.FlushAsync();
            }
        }

        private static void ReadFrame<T>(
            string json,
            string expectedKind)
            where T : class
        {
            NamedPipePackageBrokerTransport.DeserializeFrame<T>(
                json,
                expectedKind);
        }
        private static NativePackageBrokerRequest ValidRequest(
            string operation)
        {
            return new NativePackageBrokerRequest
            {
                aliasId = "jammr",
                expectedCurrentReleaseRoot =
                    new string('0', 64),
                idempotencyKey = operation + "-1",
                operation = operation,
                projectIdentity = new string('3', 64),
                projectPath = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..")),
                runId = "run-" + operation,
                traceparent =
                    "00-" +
                    new string('4', 32) +
                    "-" +
                    new string('5', 16) +
                    "-01",
            };
        }
    }
}
