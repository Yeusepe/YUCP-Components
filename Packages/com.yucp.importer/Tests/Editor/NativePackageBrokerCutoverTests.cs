using System;
using System.Collections;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
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

        [Test]
        public void BrokerIdentifiersRejectNonAsciiCharacters()
        {
            NativePackageBrokerRequest request = ValidRequest("preflight");
            request.runId = "r\u00FAn";

            Assert.Throws<InvalidDataException>(() =>
                NativePackageBrokerClient.ValidateRequest(request));
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
        public void ImporterRestoresBrokerBackedAuthenticationControls()
        {
            string packageRoot = PackageInfo.FindForAssembly(
                typeof(PackageContractV2).Assembly).resolvedPath;
            string windowSource = File.ReadAllText(
                Path.Combine(
                    packageRoot,
                    "Editor",
                    "PackageManager",
                    "UI",
                    "PackageManagerWindow.cs"));
            string brokerSource = File.ReadAllText(
                Path.Combine(
                    packageRoot,
                    "Editor",
                    "PackageManager",
                    "Core",
                    "NativePackageBrokerClient.cs"));

            Assert.That(windowSource, Does.Contain("Sign in with YUCP"));
            Assert.That(windowSource, Does.Contain("Signed in with YUCP"));
            Assert.That(windowSource, Does.Contain("Sign out"));
            Assert.That(
                windowSource,
                Does.Contain("RefreshAuthenticationStatusAsync"));
            Assert.That(
                brokerSource,
                Does.Contain("\"authenticate\""));
            Assert.That(
                brokerSource,
                Does.Contain("\"authentication\""));
            Assert.That(
                brokerSource,
                Does.Not.Contain("accessToken").IgnoreCase);
            Assert.That(
                brokerSource,
                Does.Not.Contain("refreshToken").IgnoreCase);
        }

        [Test]
        public void ImportVerificationDefersAssemblyReloadUntilCompilationEnds()
        {
            string packageRoot = PackageInfo.FindForAssembly(
                typeof(PackageContractV2).Assembly).resolvedPath;
            string source = File.ReadAllText(
                Path.Combine(
                    packageRoot,
                    "Editor",
                    "PackageManager",
                    "Core",
                    "PackageImportVerifier.cs"));

            Assert.That(
                source,
                Does.Contain("EditorApplication.LockReloadAssemblies()"));
            Assert.That(
                source,
                Does.Contain("EditorApplication.UnlockReloadAssemblies()"));
            Assert.That(
                source,
                Does.Contain("new CancellationTokenSource()"));
            Assert.That(
                source,
                Does.Match(
                    @"Task\.Delay\(\s*" +
                    @"compilationTimeoutMilliseconds,\s*" +
                    @"timeoutCancellation\.Token\)"));
        }

        [Test]
        public void LifecycleValidatesStagingBeforeWritingInstallState()
        {
            string packageRoot = PackageInfo.FindForAssembly(
                typeof(PackageContractV2).Assembly).resolvedPath;
            string source = File.ReadAllText(
                Path.Combine(
                    packageRoot,
                    "Editor",
                    "PackageManager",
                    "Core",
                    "PackageLifecycleCoordinator.cs"));
            int validation = source.IndexOf(
                "ProjectTransactionJournal.RequireSafeStagingRoot(",
                StringComparison.Ordinal);
            int stateWrite = source.IndexOf(
                "WriteInstallState(",
                StringComparison.Ordinal);

            Assert.That(validation, Is.GreaterThanOrEqualTo(0));
            Assert.That(stateWrite, Is.GreaterThan(validation));
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

        [Test]
        public void BrokerPipeTransportUsesTheCrossPlatformConstructor()
        {
            string packageRoot = PackageInfo.FindForAssembly(
                typeof(PackageContractV2).Assembly).resolvedPath;
            string source = File.ReadAllText(
                Path.Combine(
                    packageRoot,
                    "Editor",
                    "PackageManager",
                    "Core",
                    "NativePackageBrokerClient.cs"));

            Assert.That(
                source,
                Does.Not.Contain("TokenImpersonationLevel"));
            Assert.That(
                source,
                Does.Match(
                    @"new NamedPipeClientStream\(\s*" +
                    @"""\."",\s*_pipeName,\s*" +
                    @"PipeDirection\.InOut,\s*" +
                    @"PipeOptions\.Asynchronous\)"));
        }

        [Test]
        public void AvailableBrokerRefreshesRuntimeBeforeFirstRequest()
        {
            var bootstrap = new RecordingRuntimeBootstrap();
            var transport = new SuccessfulTransport(
                () => bootstrap.CallCount);
            try
            {
                NativePackageBrokerClient.SetTransportForTests(transport);

                NativePackageBrokerResult result =
                    PackageLifecycleCoordinator
                        .ExecuteBrokerWithBootstrapAsync(
                            ValidRequest("preflight"),
                            bootstrap,
                            _ => { },
                            _ => { },
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();

                Assert.That(result.status, Is.EqualTo("succeeded"));
                Assert.That(bootstrap.CallCount, Is.EqualTo(1));
                Assert.That(transport.CallCount, Is.EqualTo(1));
                Assert.That(
                    transport.BootstrapCallCountAtExecute,
                    Is.EqualTo(1),
                    "TUF refresh must finish before reusing a running broker.");
            }
            finally
            {
                NativePackageBrokerClient.SetTransportForTests(null);
            }
        }

        [Test]
        public void BrokerUnavailableBootstrapsOnceAndRetriesTheSameRequest()
        {
            var transport = new UnavailableThenSuccessfulTransport();
            var bootstrap = new RecordingRuntimeBootstrap();
            NativePackageBrokerRequest request = ValidRequest("preflight");
            var userProgress =
                new System.Collections.Generic.List<
                    PackageLifecycleUserProgress>();
            try
            {
                NativePackageBrokerClient.SetTransportForTests(transport);
                Task<NativePackageBrokerResult> task =
                    PackageLifecycleCoordinator
                        .ExecuteBrokerWithBootstrapAsync(
                            request,
                            bootstrap,
                            _ => { },
                            userProgress.Add,
                            CancellationToken.None);

                NativePackageBrokerResult result =
                    task.GetAwaiter().GetResult();

                Assert.That(result.status, Is.EqualTo("succeeded"));
                Assert.That(transport.CallCount, Is.EqualTo(2));
                Assert.That(bootstrap.CallCount, Is.EqualTo(1));
                Assert.That(
                    bootstrap.TraceId,
                    Is.EqualTo(new string('a', 32)));
                Assert.That(bootstrap.TokenWasCancelable, Is.True);
                Assert.That(
                    transport.Requests,
                    Has.All.SameAs(request),
                    "Retry must preserve the authenticated request binding.");
                Assert.That(userProgress, Has.Count.EqualTo(1));
                Assert.That(
                    userProgress[0].message,
                    Does.Contain("package delivery"));
                Assert.That(
                    userProgress[0].message,
                    Does.Not.Contain("TUF"));
            }
            finally
            {
                NativePackageBrokerClient.SetTransportForTests(null);
            }
        }

        [Test]
        public void PackagedProductionRuntimePassesUnityVerification()
        {
            string projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string packageRoot = Path.Combine(
                projectRoot,
                "Packages",
                "com.yucp.importer");
            string localRoot = Path.Combine(
                Path.GetTempPath(),
                "yucp-packaged-runtime-" +
                Guid.NewGuid().ToString("N"));
            NativePackageRuntimeInvocation invocation =
                PackagedNativePackageRuntimeBootstrap
                    .BuildInvocationForTests(
                        packageRoot,
                        localRoot,
                        true,
                        new string('a', 32));

            Assert.That(
                invocation.executablePath,
                Is.EqualTo(
                    Path.Combine(
                        packageRoot,
                        "Editor",
                        "PackageManager",
                        "Runtime",
                        "Windows",
                        "x64",
                        "yucp-transfer-helper.exe")));
        }

        [Test]
        public void PackagedRuntimeUsesOnlyImmutableProductionInputs()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "yucp-runtime-bootstrap-" + Guid.NewGuid().ToString("N"));
            string packageRoot = Path.Combine(root, "package");
            string localRoot = Path.Combine(root, "local");
            try
            {
                string executable = Path.Combine(
                    packageRoot,
                    "Editor",
                    "PackageManager",
                    "Runtime",
                    "Windows",
                    "x64",
                    "yucp-transfer-helper.exe");
                string trustedRoot = Path.Combine(
                    packageRoot,
                    "Editor",
                    "PackageManager",
                    "Trust",
                    "1.root.json");
                Directory.CreateDirectory(
                    Path.GetDirectoryName(executable));
                Directory.CreateDirectory(
                    Path.GetDirectoryName(trustedRoot));
                Directory.CreateDirectory(localRoot);
                File.WriteAllBytes(
                    executable,
                    new byte[] { (byte)'M', (byte)'Z' });
                File.WriteAllText(
                    trustedRoot,
                    "{\"signed\":{\"_type\":\"root\",\"version\":1}," +
                    "\"signatures\":[{}]}",
                    new UTF8Encoding(false));
                var publisher = new RecordingPublisherVerifier();
                const string metadataUrl =
                    "http://127.0.0.1:3001/api/v2/package-installer/" +
                    "tuf/metadata";
                const string targetsUrl =
                    "http://127.0.0.1:3001/api/v2/package-installer/" +
                    "tuf/targets";
                var trust = new NativePackageRuntimeTrust(
                    Sha256File(executable),
                    Sha256File(trustedRoot),
                    metadataUrl,
                    targetsUrl,
                    "CN=YUCP Test Publisher",
                    new string('c', 64),
                    "system",
                    publisher);

                NativePackageRuntimeInvocation invocation =
                    PackagedNativePackageRuntimeBootstrap
                        .BuildInvocationForTests(
                            packageRoot,
                            localRoot,
                            true,
                            new string('a', 32),
                            trust);

                Assert.That(
                    invocation.executablePath,
                    Is.EqualTo(executable));
                Assert.That(
                    invocation.arguments,
                    Does.Contain(metadataUrl));
                Assert.That(
                    invocation.arguments,
                    Does.Contain(targetsUrl));
                Assert.That(
                    invocation.arguments,
                    Does.Contain("\"--http-timeout\" \"30s\""));
                Assert.That(
                    invocation.arguments,
                    Does.Contain("\"--startup-timeout\" \"20s\""));
                Assert.That(
                    invocation.arguments,
                    Does.Not.Contain("runtime-target"));
                Assert.That(
                    invocation.arguments,
                    Does.Not.Contain("jammr"));
                Assert.That(
                    invocation.installRoot,
                    Is.Not.EqualTo(invocation.stateRoot));
                Assert.That(publisher.CallCount, Is.EqualTo(1));
                Assert.That(
                    publisher.ExpectedSubject,
                    Is.EqualTo("CN=YUCP Test Publisher"));
                Assert.That(
                    publisher.ExpectedTrustMode,
                    Is.EqualTo("system"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void PackagedRuntimeFailsClosedBeforeProcessStart()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "yucp-runtime-invalid-" + Guid.NewGuid().ToString("N"));
            string packageRoot = Path.Combine(root, "package");
            string localRoot = Path.Combine(root, "local");
            try
            {
                Directory.CreateDirectory(packageRoot);
                Directory.CreateDirectory(localRoot);
                Assert.Throws<PlatformNotSupportedException>(() =>
                    PackagedNativePackageRuntimeBootstrap
                        .BuildInvocationForTests(
                            packageRoot,
                            localRoot,
                            false,
                            string.Empty));
                Assert.Throws<InvalidDataException>(() =>
                    PackagedNativePackageRuntimeBootstrap
                        .BuildInvocationForTests(
                            packageRoot,
                            localRoot,
                            true,
                            string.Empty));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void PackagedRuntimeRejectsUnpinnedOrUnpublishedBytes()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "yucp-runtime-tamper-" + Guid.NewGuid().ToString("N"));
            string packageRoot = Path.Combine(root, "package");
            string localRoot = Path.Combine(root, "local");
            try
            {
                string executable = Path.Combine(
                    packageRoot,
                    "Editor",
                    "PackageManager",
                    "Runtime",
                    "Windows",
                    "x64",
                    "yucp-transfer-helper.exe");
                string trustedRoot = Path.Combine(
                    packageRoot,
                    "Editor",
                    "PackageManager",
                    "Trust",
                    "1.root.json");
                Directory.CreateDirectory(
                    Path.GetDirectoryName(executable));
                Directory.CreateDirectory(
                    Path.GetDirectoryName(trustedRoot));
                Directory.CreateDirectory(localRoot);
                File.WriteAllBytes(
                    executable,
                    new byte[] { (byte)'M', (byte)'Z' });
                File.WriteAllText(
                    trustedRoot,
                    "{\"signed\":{\"_type\":\"root\",\"version\":1}," +
                    "\"signatures\":[{}]}",
                    new UTF8Encoding(false));
                var publisher = new RecordingPublisherVerifier();
                var trust = new NativePackageRuntimeTrust(
                    Sha256File(executable),
                    Sha256File(trustedRoot),
                    "http://127.0.0.1:3001/api/v2/package-installer/" +
                        "tuf/metadata",
                    "http://127.0.0.1:3001/api/v2/package-installer/" +
                        "tuf/targets",
                    "CN=YUCP Test Publisher",
                    new string('c', 64),
                    "system",
                    publisher);
                File.WriteAllBytes(
                    executable,
                    new byte[] { (byte)'M', (byte)'Z', 1 });

                Assert.Throws<InvalidDataException>(() =>
                    PackagedNativePackageRuntimeBootstrap
                        .BuildInvocationForTests(
                            packageRoot,
                            localRoot,
                            true,
                            new string('a', 32),
                            trust));

                File.WriteAllBytes(
                    executable,
                    new byte[] { (byte)'M', (byte)'Z' });
                File.AppendAllText(trustedRoot, " ");
                Assert.Throws<InvalidDataException>(() =>
                    PackagedNativePackageRuntimeBootstrap
                        .BuildInvocationForTests(
                            packageRoot,
                            localRoot,
                            true,
                            new string('a', 32),
                            trust));
                Assert.That(publisher.CallCount, Is.EqualTo(0));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void PublisherTrustAcceptsOnlyExplicitLocalUntrustedRoots()
        {
            const int untrustedRoot = unchecked((int)0x800B0109);
            const int badDigest = unchecked((int)0x80096010);

            Assert.That(
                WindowsAuthenticodePublisherVerifier
                    .IsAuthenticodeResultAcceptedForTests(
                        0,
                        "system"),
                Is.True);
            Assert.That(
                WindowsAuthenticodePublisherVerifier
                    .IsAuthenticodeResultAcceptedForTests(
                        untrustedRoot,
                        "system"),
                Is.False);
            Assert.That(
                WindowsAuthenticodePublisherVerifier
                    .IsAuthenticodeResultAcceptedForTests(
                        untrustedRoot,
                        "pinned-development"),
                Is.True);
            Assert.That(
                WindowsAuthenticodePublisherVerifier
                    .IsAuthenticodeResultAcceptedForTests(
                        badDigest,
                        "pinned-development"),
                Is.False);
            Assert.That(
                WindowsAuthenticodePublisherVerifier
                    .IsAuthenticodeResultAcceptedForTests(
                        untrustedRoot,
                        "pinned-production"),
                Is.True);
            Assert.That(
                WindowsAuthenticodePublisherVerifier
                    .IsAuthenticodeResultAcceptedForTests(
                        badDigest,
                        "pinned-production"),
                Is.False);
        }

        [Test]
        public void PinnedProductionPublisherRequiresCanonicalProductionIdentity()
        {
            var publisher = new RecordingPublisherVerifier();

            Assert.DoesNotThrow(() =>
                new NativePackageRuntimeTrust(
                    new string('a', 64),
                    new string('b', 64),
                    "https://verify.creators.yucp.club/api/v2/" +
                        "package-installer/tuf/metadata",
                    "https://verify.creators.yucp.club/api/v2/" +
                        "package-installer/tuf/targets",
                    "CN=YUCP Package Runtime",
                    new string('c', 64),
                    "pinned-production",
                    publisher));
            Assert.Throws<InvalidDataException>(() =>
                new NativePackageRuntimeTrust(
                    new string('a', 64),
                    new string('b', 64),
                    "https://verify.creators.yucp.club/api/v2/" +
                        "package-installer/tuf/metadata",
                    "https://verify.creators.yucp.club/api/v2/" +
                        "package-installer/tuf/targets",
                    "CN=Different Publisher",
                    new string('c', 64),
                    "pinned-production",
                    publisher));
        }

        [Test]
        public void ProductionPublisherTrustPermitsOnlineRevocationChecks()
        {
            const uint cacheOnlyUrlRetrieval = 0x1000;
            const uint revocationCheckChain = 0x40;
            uint flags = WindowsAuthenticodePublisherVerifier
                .ProviderFlagsForTests("system");

            Assert.That(
                flags & cacheOnlyUrlRetrieval,
                Is.EqualTo(0));
            Assert.That(
                flags & revocationCheckChain,
                Is.EqualTo(revocationCheckChain));
        }

        [Test]
        public void PackageRuntimePathRejectsAReparseDirectoryComponent()
        {
            string packageRoot = Path.GetFullPath(
                Path.Combine(Path.GetTempPath(), "yucp-package"));
            string runtimePath = Path.Combine(
                packageRoot,
                "Runtime",
                "Windows",
                "helper.exe");

            Assert.Throws<InvalidDataException>(() =>
                PackagedNativePackageRuntimeBootstrap
                    .ValidatePathComponentsForTests(
                        packageRoot,
                        runtimePath,
                        path => path.EndsWith(
                                Path.Combine("Runtime", "Windows"),
                                StringComparison.Ordinal)
                            ? FileAttributes.Directory |
                                FileAttributes.ReparsePoint
                            : FileAttributes.Normal));
        }

        [Test]
        public void BootstrapFailureDoesNotCallTheBroker()
        {
            var transport = new UnavailableThenSuccessfulTransport();
            var bootstrap = new RecordingRuntimeBootstrap
            {
                Failure = new NativePackageRuntimeBootstrapException(
                    "RUNTIME_INSTALL_FAILED",
                    "The signed runtime repository was temporarily unavailable."),
            };
            try
            {
                NativePackageBrokerClient.SetTransportForTests(transport);
                NativePackageBrokerException failure =
                    Assert.Throws<NativePackageBrokerException>(() =>
                        PackageLifecycleCoordinator
                            .ExecuteBrokerWithBootstrapAsync(
                                ValidRequest("preflight"),
                                bootstrap,
                                _ => { },
                                _ => { },
                                CancellationToken.None)
                            .GetAwaiter()
                            .GetResult());

                Assert.That(
                    failure.ErrorCode,
                    Is.EqualTo("BROKER_BOOTSTRAP_FAILED"));
                Assert.That(
                    failure.InnerException,
                    Is.SameAs(bootstrap.Failure));
                Assert.That(transport.CallCount, Is.EqualTo(0));
                Assert.That(bootstrap.CallCount, Is.EqualTo(1));
            }
            finally
            {
                NativePackageBrokerClient.SetTransportForTests(null);
            }
        }

        [Test]
        public void BootstrapHelperFailurePreservesStructuredDiagnostic()
        {
            const string traceId =
                "0123456789abcdef0123456789abcdef";
            NativePackageRuntimeBootstrapException failure =
                Assert.Throws<NativePackageRuntimeBootstrapException>(() =>
                    PackagedNativePackageRuntimeBootstrap
                        .ValidateResultForTests(
                            "{\"errorCode\":\"RUNTIME_INSTALL_FAILED\"," +
                            "\"message\":\"refresh trusted TUF metadata: " +
                            "repository returned 503\"," +
                            "\"status\":\"ERROR\"," +
                            "\"traceId\":\"" + traceId + "\"}",
                            1,
                            traceId));

            Assert.That(
                failure.ErrorCode,
                Is.EqualTo("RUNTIME_INSTALL_FAILED"));
            Assert.That(
                failure.Message,
                Does.Contain("repository returned 503"));
        }

        [Test]
        public void NonUnavailableBrokerFailureStillRefreshesRuntime()
        {
            var transport = new AlwaysFailingTransport("BROKER_TIMEOUT");
            var bootstrap = new RecordingRuntimeBootstrap();
            try
            {
                NativePackageBrokerClient.SetTransportForTests(transport);
                NativePackageBrokerException failure =
                    Assert.Throws<NativePackageBrokerException>(() =>
                        PackageLifecycleCoordinator
                            .ExecuteBrokerWithBootstrapAsync(
                                ValidRequest("preflight"),
                                bootstrap,
                                _ => { },
                                _ => { },
                                CancellationToken.None)
                            .GetAwaiter()
                            .GetResult());

                Assert.That(
                    failure.ErrorCode,
                    Is.EqualTo("BROKER_TIMEOUT"));
                Assert.That(transport.CallCount, Is.EqualTo(1));
                Assert.That(bootstrap.CallCount, Is.EqualTo(1));
            }
            finally
            {
                NativePackageBrokerClient.SetTransportForTests(null);
            }
        }

        [Test]
        public void SecondUnavailableFailureDoesNotBootstrapAgain()
        {
            var transport = new AlwaysFailingTransport(
                "BROKER_UNAVAILABLE");
            var bootstrap = new RecordingRuntimeBootstrap();
            try
            {
                NativePackageBrokerClient.SetTransportForTests(transport);
                NativePackageBrokerException failure =
                    Assert.Throws<NativePackageBrokerException>(() =>
                        PackageLifecycleCoordinator
                            .ExecuteBrokerWithBootstrapAsync(
                                ValidRequest("preflight"),
                                bootstrap,
                                _ => { },
                                _ => { },
                                CancellationToken.None)
                            .GetAwaiter()
                            .GetResult());

                Assert.That(
                    failure.ErrorCode,
                    Is.EqualTo("BROKER_UNAVAILABLE"));
                Assert.That(transport.CallCount, Is.EqualTo(2));
                Assert.That(bootstrap.CallCount, Is.EqualTo(1));
            }
            finally
            {
                NativePackageBrokerClient.SetTransportForTests(null);
            }
        }

        [UnityTest]
        public IEnumerator MissingNamedPipeIsReportedUnavailable()
        {
            string pipeName =
                "yucp-package-broker-missing-" +
                Guid.NewGuid().ToString("N");
            var transport = new NamedPipePackageBrokerTransport(
                pipeName,
                25,
                100);
            Task<NativePackageBrokerResult> execution =
                transport.ExecuteAsync(
                    ValidRequest("preflight"),
                    null,
                    CancellationToken.None);

            while (!execution.IsCompleted)
            {
                yield return null;
            }

            var failure = execution.Exception?.GetBaseException()
                as NativePackageBrokerException;
            Assert.That(failure, Is.Not.Null);
            Assert.That(
                failure.ErrorCode,
                Is.EqualTo("BROKER_UNAVAILABLE"));
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

        [UnityTest]
        public IEnumerator CleanMachineImporterBootstrapsSignedProductionBroker()
        {
            if (!string.Equals(
                Environment.GetEnvironmentVariable(
                    "YUCP_TEST_CLEAN_PACKAGE_BOOTSTRAP"),
                "1",
                StringComparison.Ordinal))
            {
                Assert.Ignore(
                    "Set YUCP_TEST_CLEAN_PACKAGE_BOOTSTRAP=1 in an isolated " +
                    "clean-state project.");
            }

            NativePackageBrokerRequest request =
                ValidRequest("preflight");
            var transport = new NamedPipePackageBrokerTransport();
            Task<NativePackageBrokerResult> execution =
                transport.ExecuteAsync(
                    request,
                    null,
                    CancellationToken.None);
            while (!execution.IsCompleted)
            {
                yield return null;
            }

            if (execution.IsFaulted)
            {
                var failure = execution.Exception.GetBaseException()
                    as NativePackageBrokerException;
                Assert.Fail(
                    failure == null
                        ? execution.Exception.GetBaseException().Message
                        : failure.ErrorCode + ": " + failure.Message);
            }

            NativePackageBrokerResult result = execution.Result;
            NativePackageBrokerClient.ValidateResult(request, result);
            Assert.That(result.status, Is.EqualTo("succeeded"));
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

        private static string Sha256File(string path)
        {
            using (var stream = File.OpenRead(path))
            using (SHA256 sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(stream))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private sealed class UnavailableThenSuccessfulTransport
            : INativePackageBrokerTransport
        {
            internal int CallCount { get; private set; }

            internal System.Collections.Generic.List<
                NativePackageBrokerRequest> Requests { get; } =
                    new System.Collections.Generic.List<
                        NativePackageBrokerRequest>();

            public Task<NativePackageBrokerResult> ExecuteAsync(
                NativePackageBrokerRequest request,
                Action<NativePackageBrokerProgress> reportProgress,
                CancellationToken cancellationToken)
            {
                CallCount++;
                Requests.Add(request);
                if (CallCount == 1)
                {
                    throw new NativePackageBrokerException(
                        "BROKER_UNAVAILABLE",
                        new string('a', 32),
                        "The package broker is not running.");
                }
                return Task.FromResult(new NativePackageBrokerResult
                {
                    activeContentDigest = new string('2', 64),
                    activePolicyVersion = "active-content-policy-v1",
                    exitCode = 0,
                    files =
                        new System.Collections.Generic.List<
                            NativePackageBrokerFile>(),
                    operation = request.operation,
                    runId = request.runId,
                    schemaVersion = NativePackageBrokerClient.SchemaVersion,
                    status = "succeeded",
                    targetReleaseRoot = new string('1', 64),
                    traceId = new string('b', 32),
                });
            }
        }

        private sealed class RecordingRuntimeBootstrap
            : INativePackageRuntimeBootstrap
        {
            internal int CallCount { get; private set; }
            internal Exception Failure { get; set; }
            internal bool TokenWasCancelable { get; private set; }
            internal string TraceId { get; private set; } = string.Empty;

            public Task EnsureAsync(
                string traceId,
                CancellationToken cancellationToken)
            {
                CallCount++;
                TokenWasCancelable = cancellationToken.CanBeCanceled;
                TraceId = traceId;
                if (Failure != null)
                {
                    throw Failure;
                }
                return Task.CompletedTask;
            }
        }

        private sealed class SuccessfulTransport
            : INativePackageBrokerTransport
        {
            private readonly Func<int> _bootstrapCallCount;

            internal SuccessfulTransport(Func<int> bootstrapCallCount)
            {
                _bootstrapCallCount = bootstrapCallCount;
            }

            internal int BootstrapCallCountAtExecute { get; private set; }
            internal int CallCount { get; private set; }

            public Task<NativePackageBrokerResult> ExecuteAsync(
                NativePackageBrokerRequest request,
                Action<NativePackageBrokerProgress> reportProgress,
                CancellationToken cancellationToken)
            {
                CallCount++;
                BootstrapCallCountAtExecute = _bootstrapCallCount();
                return Task.FromResult(new NativePackageBrokerResult
                {
                    activeContentDigest = new string('2', 64),
                    activePolicyVersion = "active-content-policy-v1",
                    exitCode = 0,
                    files =
                        new System.Collections.Generic.List<
                            NativePackageBrokerFile>(),
                    operation = request.operation,
                    runId = request.runId,
                    schemaVersion = NativePackageBrokerClient.SchemaVersion,
                    status = "succeeded",
                    targetReleaseRoot = new string('1', 64),
                    traceId = new string('b', 32),
                });
            }
        }

        private sealed class AlwaysFailingTransport
            : INativePackageBrokerTransport
        {
            private readonly string _errorCode;

            internal AlwaysFailingTransport(string errorCode)
            {
                _errorCode = errorCode;
            }

            internal int CallCount { get; private set; }

            public Task<NativePackageBrokerResult> ExecuteAsync(
                NativePackageBrokerRequest request,
                Action<NativePackageBrokerProgress> reportProgress,
                CancellationToken cancellationToken)
            {
                CallCount++;
                throw new NativePackageBrokerException(
                    _errorCode,
                    new string('a', 32),
                    "The broker failed.");
            }
        }

        private sealed class RecordingPublisherVerifier
            : INativePackagePublisherVerifier
        {
            internal int CallCount { get; private set; }
            internal string ExpectedSubject { get; private set; } =
                string.Empty;
            internal string ExpectedTrustMode { get; private set; } =
                string.Empty;

            public void Verify(
                string executablePath,
                string expectedSubject,
                string expectedCertificateSha256,
                string trustMode)
            {
                CallCount++;
                ExpectedSubject = expectedSubject;
                ExpectedTrustMode = trustMode;
                Assert.That(File.Exists(executablePath), Is.True);
                Assert.That(
                    expectedCertificateSha256,
                    Is.EqualTo(new string('c', 64)));
            }
        }
    }
}
