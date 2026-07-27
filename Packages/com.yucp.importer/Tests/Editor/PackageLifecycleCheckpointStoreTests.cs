using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
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

        private static string CreateProject()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "yucp-lifecycle-checkpoint-" +
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
