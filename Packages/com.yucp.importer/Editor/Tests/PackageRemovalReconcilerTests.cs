using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public class PackageRemovalReconcilerTests
    {
        private string _testRootAbsolutePath;
        private string _testRootRelativePath;

        [SetUp]
        public void SetUp()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            _testRootRelativePath = $".yucp-test-temp/importer-remove-reconcile-{Guid.NewGuid():N}";
            _testRootAbsolutePath = Path.Combine(projectRoot, _testRootRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(_testRootAbsolutePath);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testRootAbsolutePath))
            {
                Directory.Delete(_testRootAbsolutePath, true);
            }
        }

        [Test]
        public void BuildPlan_ClassifiesSafeMissingModifiedAndSharedPaths()
        {
            string safePath = WriteTrackedFile("Managed/safe.txt", "safe-content");
            string modifiedPath = WriteTrackedFile("Generated/modified.txt", "current-content");
            string sharedPath = WriteTrackedFile("Shared/shared.txt", "shared-content");
            string missingPath = CombineRelativePath("Managed/missing.txt");

            AliasPackageInstallStateManifest manifest = CreateManifest(
                new[] { safePath, missingPath },
                new[] { modifiedPath },
                new[] { sharedPath },
                new[]
                {
                    CreateHashRecord(safePath, "safe-content"),
                    CreateHashRecord(missingPath, "missing-content"),
                    CreateHashRecord(modifiedPath, "original-content"),
                });

            PackageRemovalPlan plan = PackageRemovalReconciler.BuildPlan(CreatePackageInfo(), manifest);

            AssertEntryState(plan, safePath, PackageRemovalReconcileState.SafeToRemove);
            AssertEntryState(plan, missingPath, PackageRemovalReconcileState.MissingByUser);
            AssertEntryState(plan, modifiedPath, PackageRemovalReconcileState.ModifiedByUser);
            AssertEntryState(plan, sharedPath, PackageRemovalReconcileState.SharedPathPreserve);
        }

        [Test]
        public void UninstallPackage_RemovesOnlySafePaths()
        {
            string safePath = WriteTrackedFile("Managed/safe.txt", "safe-content");
            string modifiedPath = WriteTrackedFile("Generated/modified.txt", "current-content");
            string sharedPath = WriteTrackedFile("Shared/shared.txt", "shared-content");

            InstalledPackageInfo packageInfo = CreatePackageInfo();
            AliasPackageInstallStateManifest manifest = CreateManifest(
                new[] { safePath },
                new[] { modifiedPath },
                new[] { sharedPath },
                new[]
                {
                    CreateHashRecord(safePath, "safe-content"),
                    CreateHashRecord(modifiedPath, "original-content"),
                });

            PackageRemovalPlan capturedPlan = null;
            bool success = PackageUninstaller.UninstallPackage(
                packageInfo,
                manifest,
                skipConfirmation: false,
                confirmationHandler: plan =>
                {
                    capturedPlan = plan;
                    return true;
                });

            Assert.That(success, Is.True);
            Assert.That(capturedPlan, Is.Not.Null);
            Assert.That(capturedPlan.HasDestructiveRisk(), Is.True);
            Assert.That(File.Exists(ToAbsolutePath(safePath)), Is.False);
            Assert.That(File.Exists(ToAbsolutePath(modifiedPath)), Is.True);
            Assert.That(File.Exists(ToAbsolutePath(sharedPath)), Is.True);
        }

        [Test]
        public void UninstallPackage_CancelLeavesTrackedFilesUntouched()
        {
            string safePath = WriteTrackedFile("Managed/safe.txt", "safe-content");
            string modifiedPath = WriteTrackedFile("Generated/modified.txt", "current-content");

            InstalledPackageInfo packageInfo = CreatePackageInfo();
            AliasPackageInstallStateManifest manifest = CreateManifest(
                new[] { safePath },
                new[] { modifiedPath },
                null,
                new[]
                {
                    CreateHashRecord(safePath, "safe-content"),
                    CreateHashRecord(modifiedPath, "original-content"),
                });

            bool success = PackageUninstaller.UninstallPackage(
                packageInfo,
                manifest,
                skipConfirmation: false,
                confirmationHandler: _ => false);

            Assert.That(success, Is.False);
            Assert.That(File.Exists(ToAbsolutePath(safePath)), Is.True);
            Assert.That(File.Exists(ToAbsolutePath(modifiedPath)), Is.True);
        }

        private InstalledPackageInfo CreatePackageInfo()
        {
            return new InstalledPackageInfo
            {
                packageId = "creator.alias",
                packageName = "Creator Alias",
                installedFiles = new List<string>(),
            };
        }

        private AliasPackageInstallStateManifest CreateManifest(
            IEnumerable<string> managedPaths,
            IEnumerable<string> generatedPaths,
            IEnumerable<string> sharedPaths,
            IEnumerable<InstallStateFileHashRecord> hashRecords)
        {
            return new AliasPackageInstallStateManifest
            {
                managedPaths = managedPaths != null ? managedPaths.ToList() : new List<string>(),
                generatedPaths = generatedPaths != null ? generatedPaths.ToList() : new List<string>(),
                sharedPaths = sharedPaths != null ? sharedPaths.ToList() : new List<string>(),
                fileHashes = hashRecords != null ? hashRecords.ToList() : new List<InstallStateFileHashRecord>(),
            };
        }

        private InstallStateFileHashRecord CreateHashRecord(string relativePath, string content)
        {
            string hash = ComputeHash(content);
            return new InstallStateFileHashRecord
            {
                path = relativePath,
                expectedSha256 = hash,
                observedSha256 = hash,
            };
        }

        private string WriteTrackedFile(string relativePath, string content)
        {
            string combinedRelativePath = CombineRelativePath(relativePath);
            string absolutePath = ToAbsolutePath(combinedRelativePath);
            string directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(absolutePath, content);
            return combinedRelativePath;
        }

        private string CombineRelativePath(string relativePath)
        {
            return $"{_testRootRelativePath}/{relativePath.Replace('\\', '/')}";
        }

        private string ToAbsolutePath(string relativePath)
        {
            return Path.Combine(
                _testRootAbsolutePath,
                relativePath.Substring(_testRootRelativePath.Length).TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ComputeHash(string content)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
                return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void AssertEntryState(
            PackageRemovalPlan plan,
            string path,
            PackageRemovalReconcileState expectedState)
        {
            PackageRemovalPlanEntry entry = plan.entries.FirstOrDefault(candidate =>
                candidate != null && string.Equals(candidate.path, path, StringComparison.OrdinalIgnoreCase));

            Assert.That(entry, Is.Not.Null, $"Expected tracked entry '{path}' to exist in the reconcile plan.");
            Assert.That(entry.state, Is.EqualTo(expectedState));
        }
    }
}
