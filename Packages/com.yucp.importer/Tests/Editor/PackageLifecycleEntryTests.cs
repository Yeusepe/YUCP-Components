using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using YUCP.Importer.Editor.Batch;
using YUCP.Importer.Editor.PackageManager;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class PackageLifecycleEntryTests
    {
        [Test]
        public void ValidateRequestAcceptsTheOpenedProjectAndExactApproval()
        {
            string projectPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            var request = new PackageLifecycleRequest
            {
                schemaVersion = 1,
                runId = "run-1",
                operation = "install",
                projectPath = projectPath,
                productAlias = "jammr",
                idempotencyKey = "install-1",
                expectedCurrentReleaseRoot = new string('0', 64),
                targetReleaseRoot = string.Empty,
                approvedActiveContentDigest = new string('1', 64),
                approvedPolicyVersion = "active-content-policy-v1",
            };

            Assert.DoesNotThrow(() =>
                PackageLifecycleEntry.ValidateRequest(request));
        }

        [Test]
        public void ValidateRequestRejectsAProjectPathSubstitution()
        {
            var request = new PackageLifecycleRequest
            {
                schemaVersion = 1,
                runId = "run-2",
                operation = "preflight",
                projectPath = Path.GetFullPath(Path.GetTempPath()),
                productAlias = "jammr",
                idempotencyKey = "preflight-1",
                expectedCurrentReleaseRoot = new string('0', 64),
            };

            Assert.Throws<InvalidDataException>(() =>
                PackageLifecycleEntry.ValidateRequest(request));
        }

        [Test]
        public void FailurePersistenceRejectsAProjectPathSubstitution()
        {
            MethodInfo method = typeof(PackageLifecycleEntry).GetMethod(
                "IsRequestBoundToOpenedProject",
                BindingFlags.NonPublic | BindingFlags.Static);
            var request = new PackageLifecycleRequest
            {
                projectPath = Path.GetFullPath(Path.GetTempPath()),
            };

            Assert.That(method, Is.Not.Null);
            Assert.That(
                (bool)method.Invoke(null, new object[] { request }),
                Is.False);
        }

        [Test]
        public void StartupResumesAfterDomainReloadForAnActiveBatchCommand()
        {
            bool shouldResume =
                PackageLifecycleEntry.ShouldResumeAfterDomainReload(
                    true,
                    true,
                    @"C:\temp\request.json",
                    @"C:\temp\result.json",
                    new[]
                    {
                        "Unity.exe",
                        "-batchmode",
                        "-executeMethod",
                        "YUCP.Importer.Editor.Batch.PackageLifecycleEntry.Run",
                    });

            Assert.IsTrue(shouldResume);
        }

        [Test]
        public void StartupWaitsForExecuteMethodBeforeTheFirstBatchUpdate()
        {
            bool shouldResume =
                PackageLifecycleEntry.ShouldResumeAfterDomainReload(
                    false,
                    true,
                    @"C:\temp\request.json",
                    @"C:\temp\result.json",
                    new[]
                    {
                        "Unity.exe",
                        "-batchmode",
                        "-executeMethod",
                        "YUCP.Importer.Editor.Batch.PackageLifecycleEntry.Run",
                    });

            Assert.IsFalse(shouldResume);
        }

        [Test]
        public void StartupDoesNotConsumeLifecycleEnvironmentInAnotherCommand()
        {
            bool shouldResume =
                PackageLifecycleEntry.ShouldResumeAfterDomainReload(
                    true,
                    true,
                    @"C:\temp\request.json",
                    @"C:\temp\result.json",
                    new[]
                    {
                        "Unity.exe",
                        "-batchmode",
                        "-executeMethod",
                        "Some.Other.Entry.Run",
                    });

            Assert.IsFalse(shouldResume);
        }

        [Test]
        public void CoordinatorExposesAnAsynchronousBatchExecutionBoundary()
        {
            MethodInfo method = typeof(PackageLifecycleCoordinator).GetMethod(
                "ExecuteAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method);
            Assert.AreEqual(
                typeof(Task<PackageLifecycleExecutionResult>),
                method.ReturnType);
        }

        [TestCase(
            "The package delivery broker returned an invalid terminal result.")]
        [TestCase(
            "Package delivery failed with stable error code PACKAGE_LIFECYCLE_FAILED.")]
        [TestCase(
            "The package server resolved a different release root.")]
        public void UserVisibleInstallFailuresHideDeliveryInternals(
            string internalMessage)
        {
            MethodInfo method = typeof(PackageLifecycleCoordinator).GetMethod(
                "BuildUserFacingFailureMessage",
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            string message = (string)method.Invoke(
                null,
                new object[]
                {
                    new InvalidOperationException(internalMessage),
                });

            Assert.That(message, Is.Not.Empty);
            Assert.That(message, Does.Not.Contain("broker").IgnoreCase);
            Assert.That(message, Does.Not.Contain("release root").IgnoreCase);
            Assert.That(message, Does.Not.Contain("terminal result").IgnoreCase);
            Assert.That(message, Does.Not.Contain("stable error").IgnoreCase);
            Assert.That(message, Does.Not.Contain("PACKAGE_"));
        }

        [Test]
        public void ImportFailureMessageIncludesTheVerifierReason()
        {
            string message =
                PackageLifecycleCoordinator.BuildImportFailureMessage(
                    "install",
                    new InvalidDataException(
                        "Unity import changed Assets/Product/file.asset."));

            StringAssert.Contains(
                "The prior project was restored.",
                message);
            StringAssert.Contains(
                "Unity import changed Assets/Product/file.asset.",
                message);
        }

        [Test]
        public void BrokerRequestBindsTheLifecycleOperation()
        {
            string projectPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            NativePackageBrokerRequest request =
                PackageLifecycleCoordinator.BuildBrokerRequest(
                    "jammr",
                    new string('0', 64),
                    "run-preflight",
                    "preflight-1",
                    "preflight",
                    projectPath,
                    string.Empty,
                    string.Empty,
                    string.Empty);

            Assert.That(request.operation, Is.EqualTo("preflight"));
            Assert.That(request.aliasId, Is.EqualTo("jammr"));
            Assert.That(
                NativePackageBrokerClient.SerializeRequest(request),
                Does.Not.Contain("approvedActiveContentDigest"));
        }

        [Test]
        public void InstallStateUsesTheTransactionControlRoot()
        {
            string path =
                PackageLifecycleCoordinator.InstallStatePath("jammr");

            StringAssert.StartsWith(
                ".yucp/package-installs/",
                path);
            StringAssert.IsMatch(
                @"^\.yucp/package-installs/[0-9a-f]{64}\.json$",
                path);
            StringAssert.DoesNotContain("jammr", path);
        }

        [Test]
        public void InstallStatePathStaysBoundedForLongAliases()
        {
            string alias = "alias-" + new string('a', 100);
            string path =
                PackageLifecycleCoordinator.InstallStatePath(alias);

            Assert.AreEqual(92, path.Length);
            StringAssert.DoesNotContain(alias, path);
        }

        [Test]
        public void IdempotencyPathUsesBoundedOpaqueSegments()
        {
            string projectPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string alias = "alias-" + new string('a', 100);
            string key = "request-" + new string('b', 100);
            var request = new PackageLifecycleRequest
            {
                productAlias = alias,
                idempotencyKey = key,
                projectPath = projectPath,
            };
            MethodInfo method = typeof(PackageLifecycleEntry).GetMethod(
                "IdempotencyPath",
                BindingFlags.NonPublic | BindingFlags.Static);

            string path = (string)method.Invoke(
                null,
                new object[] { request });
            string stateRoot = Path.Combine(
                projectPath,
                "Library",
                "YUCP",
                "PackageLifecycle");
            string relative = path.Substring(
                stateRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar).Length + 1);
            string[] segments = relative.Split(
                Path.DirectorySeparatorChar);

            Assert.AreEqual(2, segments.Length);
            Assert.AreEqual("idempotency", segments[0]);
            Assert.AreEqual(69, segments[1].Length);
            StringAssert.EndsWith(".json", segments[1]);
            StringAssert.DoesNotContain(alias, path);
            StringAssert.DoesNotContain(key, path);
        }
    }
}
