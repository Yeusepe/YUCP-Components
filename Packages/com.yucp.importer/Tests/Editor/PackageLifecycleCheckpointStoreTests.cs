using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.PackageManager;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class PackageLifecycleCheckpointStoreTests
    {
        [Test]
        public void CheckpointRoundTripPreservesCommittedVerificationContext()
        {
            string project = CreateProject();
            try
            {
                var checkpoint = new PackageLifecycleCheckpoint
                {
                    activeContentDigest = new string('1', 64),
                    activePolicyVersion = "active-content-policy-v1",
                    aliasId = "jammr",
                    brokerTraceId =
                        "0123456789abcdef0123456789abcdef",
                    expectedCurrentReleaseRoot = new string('0', 64),
                    operation = "install",
                    phase = "committed",
                    runId = "run-checkpoint",
                    targetState = new PackageDeliveryInstallState
                    {
                        activeContentDigest = new string('1', 64),
                        activePolicyVersion = "active-content-policy-v1",
                        aliasId = "jammr",
                        receiptId = "receipt-1",
                        receiptPath = "receipts/receipt-1.cbor",
                        releaseRoot = new string('2', 64),
                        versionId = "version-1",
                        files =
                        {
                            new NativePackageBrokerFile
                            {
                                bytes = 1,
                                normalizedPath = "Assets/Product/file.txt",
                                sha256 = new string('3', 64),
                            },
                        },
                    },
                };

                PackageLifecycleCheckpointStore.Write(project, checkpoint);
                PackageLifecycleCheckpoint restored =
                    PackageLifecycleCheckpointStore.Read(
                        project,
                        "run-checkpoint");

                Assert.That(restored.phase, Is.EqualTo("committed"));
                Assert.That(
                    restored.targetState.receiptId,
                    Is.EqualTo("receipt-1"));
                Assert.That(
                    restored.targetState.schemaVersion,
                    Is.EqualTo(5));
                Assert.That(
                    restored.brokerTraceId,
                    Is.EqualTo(
                        "0123456789abcdef0123456789abcdef"));
            }
            finally
            {
                Directory.Delete(project, true);
            }
        }

        [Test]
        public void CheckpointReadersAllowAtomicReplacement()
        {
            string project = CreateProject();
            try
            {
                var checkpoint = CreateCheckpoint(
                    "run-concurrent-snapshot",
                    "prepared");
                PackageLifecycleCheckpointStore.Write(project, checkpoint);
                string path = Path.Combine(
                    project,
                    ".yucp",
                    "transactions",
                    checkpoint.runId,
                    "lifecycle.json");
                string packageRoot = PackageInfo.FindForAssembly(
                    typeof(PackageLifecycleCheckpointStore).Assembly)
                    .resolvedPath;
                string source = File.ReadAllText(
                    Path.Combine(
                        packageRoot,
                        "Editor",
                        "PackageManager",
                        "Core",
                        "PackageLifecycleCheckpointStore.cs"));

                Assert.That(
                    source,
                    Does.Contain(
                        "FileShare.ReadWrite | FileShare.Delete"));
                checkpoint.phase = "committed";
                PackageLifecycleCheckpointStore.Read(
                    project,
                    checkpoint.runId);
                PackageLifecycleCheckpointStore.Write(
                    project,
                    checkpoint);

                Assert.That(
                    PackageLifecycleCheckpointStore.Read(
                        project,
                        checkpoint.runId).phase,
                    Is.EqualTo("committed"));
            }
            finally
            {
                Directory.Delete(project, true);
            }
        }

        [Test]
        public void CheckpointPublicationDoesNotUseMetadataReplacement()
        {
            string packageRoot = PackageInfo.FindForAssembly(
                typeof(PackageLifecycleCheckpointStore).Assembly).resolvedPath;
            string source = File.ReadAllText(
                Path.Combine(
                    packageRoot,
                    "Editor",
                    "PackageManager",
                    "Core",
                    "PackageLifecycleCheckpointStore.cs"));

            Assert.That(source, Does.Not.Contain("File.Replace("));
            Assert.That(
                source,
                Does.Contain("AtomicFilePublisher.Publish("));
        }

        [Test]
        public void ConcurrentCheckpointWritersPublishCompleteSnapshots()
        {
            string project = CreateProject();
            try
            {
                const string runId = "run-concurrent-writers";
                string[] markers = Enumerable.Range(0, 32)
                    .Select(index =>
                        index.ToString("D2") + "-" + new string(
                            (char)('a' + index % 26),
                            16 * 1024))
                    .ToArray();
                using (var gate = new ManualResetEventSlim(false))
                {
                    Task[] writers = markers
                        .Select(marker => Task.Run(() =>
                        {
                            var checkpoint = CreateCheckpoint(
                                runId,
                                "committed");
                            checkpoint.errorMessage = marker;
                            gate.Wait();
                            PackageLifecycleCheckpointStore.Write(
                                project,
                                checkpoint);
                        }))
                        .ToArray();

                    gate.Set();
                    Assert.DoesNotThrow(() => Task.WaitAll(writers));
                }

                PackageLifecycleCheckpoint restored =
                    PackageLifecycleCheckpointStore.Read(project, runId);
                Assert.That(markers, Does.Contain(restored.errorMessage));
                Assert.That(restored.phase, Is.EqualTo("committed"));
            }
            finally
            {
                Directory.Delete(project, true);
            }
        }

        [Test]
        public void CheckpointRejectsARequestBindingMismatch()
        {
            var checkpoint = new PackageLifecycleCheckpoint
            {
                aliasId = "jammr",
                expectedCurrentReleaseRoot = new string('0', 64),
                operation = "install",
                runId = "run-binding",
            };

            Assert.Throws<InvalidDataException>(() =>
                PackageLifecycleCheckpointStore.ValidateBinding(
                    checkpoint,
                    "another-product",
                    "install",
                    new string('0', 64)));
        }

        [Test]
        public void CheckpointRejectsDotPrefixedRunIdentifiers()
        {
            string project = CreateProject();
            try
            {
                Assert.Throws<InvalidDataException>(() =>
                    PackageLifecycleCheckpointStore.Write(
                        project,
                        CreateCheckpoint("..", "prepared")));
                Assert.That(
                    File.Exists(
                        Path.Combine(
                            project,
                            ".yucp",
                            "transactions",
                            "lifecycle.json")),
                    Is.False);
            }
            finally
            {
                Directory.Delete(project, true);
            }
        }

        [Test]
        public void InstallAttemptIdentifierPersistsForThePackageAlias()
        {
            string project = CreateProject();
            try
            {
                MethodInfo getOrCreate =
                    typeof(PackageLifecycleCheckpointStore).GetMethod(
                        "GetOrCreateAttemptId",
                        BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo clear =
                    typeof(PackageLifecycleCheckpointStore).GetMethod(
                        "ClearAttemptId",
                        BindingFlags.NonPublic | BindingFlags.Static);

                Assert.That(getOrCreate, Is.Not.Null);
                Assert.That(clear, Is.Not.Null);
                string first = (string)getOrCreate.Invoke(
                    null,
                    new object[] { project, "jammr" });
                string second = (string)getOrCreate.Invoke(
                    null,
                    new object[] { project, "jammr" });
                Assert.That(second, Is.EqualTo(first));
                clear.Invoke(null, new object[] { project, "jammr" });
                string third = (string)getOrCreate.Invoke(
                    null,
                    new object[] { project, "jammr" });
                Assert.That(third, Is.Not.EqualTo(first));
            }
            finally
            {
                Directory.Delete(project, true);
            }
        }

        [TestCase(0)]
        [TestCase(129)]
        public void InstallAttemptIdentifierRegeneratesAfterCorruption(
            int corruptLength)
        {
            string project = CreateProject();
            try
            {
                MethodInfo getOrCreate =
                    typeof(PackageLifecycleCheckpointStore).GetMethod(
                        "GetOrCreateAttemptId",
                        BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(getOrCreate, Is.Not.Null);
                string first = (string)getOrCreate.Invoke(
                    null,
                    new object[] { project, "jammr" });
                string attemptsDirectory = Path.Combine(
                    project,
                    ".yucp",
                    "package-lifecycle",
                    "attempts");
                string[] attemptPaths = Directory.GetFiles(
                    attemptsDirectory,
                    "*.txt");
                Assert.That(attemptPaths, Has.Length.EqualTo(1));
                File.WriteAllText(
                    attemptPaths[0],
                    new string('x', corruptLength));

                string regenerated = (string)getOrCreate.Invoke(
                    null,
                    new object[] { project, "jammr" });

                Assert.That(regenerated, Has.Length.EqualTo(32));
                Assert.That(regenerated, Is.Not.EqualTo(first));
                Assert.That(
                    File.ReadAllText(attemptPaths[0]).Trim(),
                    Is.EqualTo(regenerated));
            }
            finally
            {
                Directory.Delete(project, true);
            }
        }

        [Test]
        public void InstallAttemptIdentifierRejectsNonHexadecimalText()
        {
            string project = CreateProject();
            try
            {
                string first =
                    PackageLifecycleCheckpointStore.GetOrCreateAttemptId(
                        project,
                        "jammr");
                string attemptPath = Directory.GetFiles(
                    Path.Combine(
                        project,
                        ".yucp",
                        "package-lifecycle",
                        "attempts"),
                    "*.txt").Single();
                File.WriteAllText(
                    attemptPath,
                    "gggggggggggggggggggggggggggggggg");

                string regenerated =
                    PackageLifecycleCheckpointStore.GetOrCreateAttemptId(
                        project,
                        "jammr");

                Assert.That(regenerated, Has.Length.EqualTo(32));
                Assert.That(regenerated, Is.Not.EqualTo(first));
                Assert.That(
                    regenerated.All(character =>
                        character >= '0' && character <= '9' ||
                        character >= 'a' && character <= 'f'),
                    Is.True);
                Assert.That(
                    File.ReadAllText(attemptPath).Trim(),
                    Is.EqualTo(regenerated));
            }
            finally
            {
                Directory.Delete(project, true);
            }
        }

        [Test]
        public void ConcurrentCorruptAttemptRepairCommitsOneIdentifier()
        {
            string project = CreateProject();
            try
            {
                PackageLifecycleCheckpointStore.GetOrCreateAttemptId(
                    project,
                    "jammr");
                string attemptPath = Directory.GetFiles(
                    Path.Combine(
                        project,
                        ".yucp",
                        "package-lifecycle",
                        "attempts"),
                    "*.txt").Single();
                File.WriteAllText(attemptPath, "corrupt");
                using (var gate = new ManualResetEventSlim(false))
                {
                    var tasks = new List<Task<string>>();
                    for (int index = 0; index < 32; index++)
                    {
                        tasks.Add(Task.Run(() =>
                        {
                            gate.Wait();
                            return PackageLifecycleCheckpointStore
                                .GetOrCreateAttemptId(
                                    project,
                                    "jammr");
                        }));
                    }

                    gate.Set();
                    Assert.That(
                        Task.WaitAll(
                            tasks.Cast<Task>().ToArray(),
                            TimeSpan.FromSeconds(10)),
                        Is.True);
                    string[] identifiers = tasks
                        .Select(task => task.Result)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();

                    Assert.That(identifiers, Has.Length.EqualTo(1));
                    Assert.That(
                        File.ReadAllText(attemptPath).Trim(),
                        Is.EqualTo(identifiers[0]));
                }
            }
            finally
            {
                Directory.Delete(project, true);
            }
        }

        [Test]
        public void IncompleteRollbackRetainsTheAttemptForRecovery()
        {
            string project = CreateProject();
            try
            {
                const string aliasId = "jammr";
                string attemptId =
                    PackageLifecycleCheckpointStore.GetOrCreateAttemptId(
                        project,
                        aliasId);
                var checkpoint = new PackageLifecycleCheckpoint
                {
                    aliasId = aliasId,
                    expectedCurrentReleaseRoot = new string('0', 64),
                    operation = "install",
                    phase = "rolling-back",
                    runId = attemptId + "-execute",
                };
                PackageLifecycleCheckpointStore.Write(project, checkpoint);

                PackageLifecycleCoordinator.ClearAttemptIdWhenTerminal(
                    project,
                    aliasId,
                    attemptId + "-execute");

                Assert.That(
                    PackageLifecycleCheckpointStore.TryGetAttemptId(
                        project,
                        aliasId,
                        out string retained),
                    Is.True);
                Assert.That(retained, Is.EqualTo(attemptId));

                checkpoint.phase = "rolled-back";
                PackageLifecycleCheckpointStore.Write(project, checkpoint);
                PackageLifecycleCoordinator.ClearAttemptIdWhenTerminal(
                    project,
                    aliasId,
                    attemptId + "-execute");

                Assert.That(
                    PackageLifecycleCheckpointStore.TryGetAttemptId(
                        project,
                        aliasId,
                        out _),
                    Is.False);
            }
            finally
            {
                Directory.Delete(project, true);
            }
        }

        [Test]
        public void CorruptProjectIdentityRegeneratesAtomically()
        {
            string project = CreateProject();
            try
            {
                string settings = Path.Combine(project, "ProjectSettings");
                Directory.CreateDirectory(settings);
                string identityPath = Path.Combine(
                    settings,
                    "YUCPProjectIdentity.json");
                File.WriteAllText(identityPath, "{not-json");

                string identity =
                    ProjectIdentityService.GetOrCreateProjectIdentity(project);

                Assert.That(identity, Has.Length.EqualTo(64));
                StringAssert.Contains(
                    identity,
                    File.ReadAllText(identityPath));
            }
            finally
            {
                Directory.Delete(project, true);
            }
        }

        [Test]
        public void ConcurrentProjectIdentityCreationConverges()
        {
            string project = CreateProject();
            try
            {
                const int callerCount = 32;
                using (var start = new ManualResetEventSlim(false))
                {
                    Task<string>[] calls = Enumerable.Range(0, callerCount)
                        .Select(_ => Task.Run(() =>
                        {
                            start.Wait();
                            return ProjectIdentityService
                                .GetOrCreateProjectIdentity(project);
                        }))
                        .ToArray();
                    start.Set();
                    Task.WaitAll(calls);

                    string[] identities = calls
                        .Select(call => call.Result)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    Assert.That(identities, Has.Length.EqualTo(1));
                    Assert.That(
                        File.ReadAllText(
                            Path.Combine(
                                project,
                                "ProjectSettings",
                                "YUCPProjectIdentity.json")),
                        Does.Contain(identities[0]));
                }
            }
            finally
            {
                Directory.Delete(project, true);
            }
        }

        private static string CreateProject()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "yucp-lifecycle-checkpoint-" +
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static PackageLifecycleCheckpoint CreateCheckpoint(
            string runId,
            string phase)
        {
            return new PackageLifecycleCheckpoint
            {
                aliasId = "jammr",
                expectedCurrentReleaseRoot = new string('0', 64),
                operation = "install",
                phase = phase,
                runId = runId,
            };
        }
    }
}
