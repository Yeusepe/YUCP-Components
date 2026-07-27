using System;
using System.IO;
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
