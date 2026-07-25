using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class PackageImportVerifierTests
    {
        private const string FixtureFolder =
            "Assets/YucpPackageImportVerifierTests";
        private const string FixturePath =
            FixtureFolder + "/coupled-fixture.txt";
        private const string MissingTypeFixturePath =
            FixtureFolder + "/missing-type.asset";

        [Test]
        public void ImportAndVerifyRegistersOwnedAsset()
        {
            string projectPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string diskPath = Path.Combine(
                projectPath,
                FixturePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(diskPath));
                File.WriteAllText(
                    diskPath,
                    "verified coupled content",
                    new UTF8Encoding(false));
                var files = new List<TransferHelperFile>
                {
                    new TransferHelperFile
                    {
                        bytes = new FileInfo(diskPath).Length,
                        normalizedPath = FixturePath,
                        sha256 = Sha256(diskPath),
                    },
                };

                PackageImportVerifier.ImportAndVerify(
                    projectPath,
                    files);

                Assert.That(
                    AssetDatabase.AssetPathToGUID(FixturePath),
                    Is.Not.Empty);
                Assert.That(
                    AssetDatabase.LoadMainAssetAtPath(FixturePath),
                    Is.Not.Null);
            }
            finally
            {
                AssetDatabase.DeleteAsset(FixtureFolder);
                AssetDatabase.Refresh(
                    ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [Test]
        public void ImportAndVerifyReportsTheOwnedPathForByteChanges()
        {
            string projectPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string diskPath = Path.Combine(
                projectPath,
                FixturePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(diskPath));
                File.WriteAllText(
                    diskPath,
                    "changed coupled content",
                    new UTF8Encoding(false));
                var files = new List<TransferHelperFile>
                {
                    new TransferHelperFile
                    {
                        bytes = new FileInfo(diskPath).Length,
                        normalizedPath = FixturePath,
                        sha256 = new string('0', 64),
                    },
                };

                InvalidDataException exception =
                    Assert.Throws<InvalidDataException>(() =>
                        PackageImportVerifier.ImportAndVerify(
                            projectPath,
                            files));

                StringAssert.Contains(FixturePath, exception.Message);
            }
            finally
            {
                AssetDatabase.DeleteAsset(FixtureFolder);
                AssetDatabase.Refresh(
                    ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [Test]
        public void ImportAndVerifyAcceptsARegisteredAssetWithAnUnavailableType()
        {
            string projectPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string diskPath = Path.Combine(
                projectPath,
                MissingTypeFixturePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(diskPath));
                File.WriteAllText(
                    diskPath,
                    "%YAML 1.1\n" +
                    "%TAG !u! tag:unity3d.com,2011:\n" +
                    "--- !u!114 &11400000\n" +
                    "MonoBehaviour:\n" +
                    "  m_ObjectHideFlags: 0\n" +
                    "  m_CorrespondingSourceObject: {fileID: 0}\n" +
                    "  m_PrefabInstance: {fileID: 0}\n" +
                    "  m_PrefabAsset: {fileID: 0}\n" +
                    "  m_GameObject: {fileID: 0}\n" +
                    "  m_Enabled: 1\n" +
                    "  m_EditorHideFlags: 0\n" +
                    "  m_Script: {fileID: 11500000, guid: " +
                    "ffffffffffffffffffffffffffffffff, type: 3}\n" +
                    "  m_Name: MissingTypeFixture\n" +
                    "  m_EditorClassIdentifier: \n",
                    new UTF8Encoding(false));
                AssetDatabase.Refresh(
                    ImportAssetOptions.ForceSynchronousImport);
                Assert.That(
                    AssetDatabase.AssetPathToGUID(
                        MissingTypeFixturePath,
                        AssetPathToGUIDOptions.OnlyExistingAssets),
                    Is.Not.Empty);
                Assert.That(
                    AssetDatabase.LoadMainAssetAtPath(
                        MissingTypeFixturePath),
                    Is.Null);
                var files = new List<TransferHelperFile>
                {
                    new TransferHelperFile
                    {
                        bytes = new FileInfo(diskPath).Length,
                        normalizedPath = MissingTypeFixturePath,
                        sha256 = Sha256(diskPath),
                    },
                };

                Assert.DoesNotThrow(() =>
                    PackageImportVerifier.ImportAndVerify(
                        projectPath,
                        files));
            }
            finally
            {
                AssetDatabase.DeleteAsset(FixtureFolder);
                AssetDatabase.Refresh(
                    ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [Test]
        public void ImportAndVerifyReadsAnOwnedFileBeyondTheWindowsPathLimit()
        {
            string projectPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string normalizedPath =
                ".yucp/package-import-verifier-tests/" +
                new string('a', 90) + "/" +
                new string('b', 90) + "/coupled-fixture.txt";
            string diskPath = Path.Combine(
                projectPath,
                normalizedPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            string extendedDiskPath =
                PackageImportVerifier.ToExtendedWindowsPath(diskPath);
            string fixtureRoot = Path.Combine(
                projectPath,
                ".yucp",
                "package-import-verifier-tests");
            try
            {
                Assert.That(diskPath.Length, Is.GreaterThan(260));
                Directory.CreateDirectory(
                    Path.GetDirectoryName(extendedDiskPath));
                File.WriteAllText(
                    extendedDiskPath,
                    "verified long-path content",
                    new UTF8Encoding(false));
                var files = new List<TransferHelperFile>
                {
                    new TransferHelperFile
                    {
                        bytes = new FileInfo(extendedDiskPath).Length,
                        normalizedPath = normalizedPath,
                        sha256 = Sha256(extendedDiskPath),
                    },
                };

                Assert.DoesNotThrow(() =>
                    PackageImportVerifier.ImportAndVerify(
                        projectPath,
                        files));
            }
            finally
            {
                string extendedFixtureRoot =
                    PackageImportVerifier.ToExtendedWindowsPath(fixtureRoot);
                if (Directory.Exists(extendedFixtureRoot))
                {
                    Directory.Delete(extendedFixtureRoot, true);
                }
            }
        }

        [Test]
        public void ImportAndVerifyRemovalIgnoresRecentlyDeletedAsset()
        {
            string projectPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string diskPath = Path.Combine(
                projectPath,
                FixturePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(diskPath));
                File.WriteAllText(
                    diskPath,
                    "recently deleted coupled content",
                    new UTF8Encoding(false));
                AssetDatabase.Refresh(
                    ImportAssetOptions.ForceSynchronousImport);
                Assert.That(
                    AssetDatabase.AssetPathToGUID(
                        FixturePath,
                        AssetPathToGUIDOptions.OnlyExistingAssets),
                    Is.Not.Empty);

                File.Delete(diskPath);
                File.Delete(diskPath + ".meta");
                var files = new List<VerifiedStagingFile>
                {
                    new VerifiedStagingFile
                    {
                        normalizedPath = FixturePath,
                    },
                };

                PackageImportVerifier.ImportAndVerifyRemoval(
                    projectPath,
                    files);
            }
            finally
            {
                AssetDatabase.DeleteAsset(FixtureFolder);
                AssetDatabase.Refresh(
                    ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                var builder = new StringBuilder(64);
                foreach (byte value in sha256.ComputeHash(stream))
                {
                    builder.Append(value.ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
