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
        public void StartupResumesAfterDomainReloadForTheExactBatchCommand()
        {
            bool shouldResume =
                PackageLifecycleEntry.ShouldResumeAfterDomainReload(
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
        public void StartupDoesNotConsumeLifecycleEnvironmentInAnotherCommand()
        {
            bool shouldResume =
                PackageLifecycleEntry.ShouldResumeAfterDomainReload(
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
        public void SessionRequestBindsTheLifecycleOperation()
        {
            var alias = new AliasPackageContract
            {
                aliasId = "jammr",
                catalogProductIds = new System.Collections.Generic.List<string>
                {
                    "catalog-jammr-jinxxy",
                },
            };

            string body = PackageLifecycleCoordinator.BuildSessionRequestBody(
                alias,
                new string('4', 64),
                "preflight-1",
                "preflight",
                string.Empty);

            StringAssert.Contains("\"operation\":\"preflight\"", body);
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
            const string stateEnvironment =
                "YUCP_PACKAGE_DELIVERY_STATE_ROOT";
            string priorStateRoot =
                Environment.GetEnvironmentVariable(stateEnvironment);
            string stateRoot = Path.Combine(
                Path.GetTempPath(),
                new string('r', 96));
            try
            {
                Environment.SetEnvironmentVariable(
                    stateEnvironment,
                    stateRoot);
                string alias = "alias-" + new string('a', 100);
                string key = "request-" + new string('b', 100);
                var request = new PackageLifecycleRequest
                {
                    productAlias = alias,
                    idempotencyKey = key,
                };
                MethodInfo method = typeof(PackageLifecycleEntry).GetMethod(
                    "IdempotencyPath",
                    BindingFlags.NonPublic | BindingFlags.Static);

                string path = (string)method.Invoke(
                    null,
                    new object[] { request });
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
                Assert.LessOrEqual(
                    path.Length,
                    stateRoot.Length + 83);
                StringAssert.DoesNotContain(alias, path);
                StringAssert.DoesNotContain(key, path);
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    stateEnvironment,
                    priorStateRoot);
            }
        }
    }
}
