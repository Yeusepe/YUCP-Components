using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public class CouplingImportGuardTests
    {
        private string _testAssetRoot;

        [TearDown]
        public void TearDown()
        {
            CouplingImportGuardTestHooks.Reset();
            DeleteTestAssets();
        }

        [Test]
        public void TryApplyCouplingOrRollback_WhenCouplingFails_RollsBackImportedAssets()
        {
            string assetPath = CreateImportedAsset();
            var packageInfo = new InstalledPackageInfo
            {
                packageId = "pkg-coupling-guard-test",
                installedFiles = new List<string> { assetPath },
            };

            SetTryApplyCouplingOverride(typeof(CouplingImportGuardTests).GetMethod(
                nameof(FailCoupling),
                BindingFlags.NonPublic | BindingFlags.Static));

            object[] args = { packageInfo, null };
            bool success = (bool)GetGuardMethod().Invoke(null, args);
            string error = args[1] as string;

            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo("The package protection step could not be completed on this machine."));
            Assert.That(error, Does.Not.Contain("forced coupling failure"));
            Assert.That(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath), Is.Null);
            Assert.That(File.Exists(GetAssetDiskPath(assetPath)), Is.False);
        }

        [Test]
        public void ShouldApplyDuringShellImport_WhenProtectedPayloadExists_ReturnsFalse()
        {
            var packageInfo = new InstalledPackageInfo
            {
                packageId = "pkg-protected-shell",
                protectedPayload = new ProtectedPayloadDescriptor
                {
                    protectedAssetId = "protected-asset-123",
                    blobAssetPath = "Assets/Protected/payload.blob",
                },
            };

            MethodInfo method = GetGuardType().GetMethod("ShouldApplyDuringShellImport", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args = { packageInfo };
            bool shouldApply = (bool)method.Invoke(null, args);

            Assert.That(shouldApply, Is.False);
        }

        [Test]
        public void BuildProtectedPayloadCouplingFiles_WhenExtractedAssetsProvided_MergesShellAndFinalFiles()
        {
            const string shellPng = "Packages/yucp.installed-packages/pkg/Embedded/icon.png";
            const string shellManifest = "Assets/_Signing/PackageManifest.json";
            const string finalFbx = "Assets/Novaspil_Kitbash/Novaspil.bytes";

            var packageInfo = new InstalledPackageInfo
            {
                packageId = "pkg-protected-shell",
                installedFiles = new List<string>
                {
                    shellPng,
                    shellManifest,
                },
            };

            MethodInfo method = GetGuardType().GetMethod("BuildProtectedPayloadCouplingFiles", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args = {
                packageInfo,
                new List<string>
                {
                    finalFbx,
                    shellPng,
                },
            };
            var mergedFiles = method.Invoke(null, args) as IReadOnlyList<string>;

            Assert.That(mergedFiles, Is.Not.Null);
            Assert.That(mergedFiles, Has.Member(shellPng));
            Assert.That(mergedFiles, Has.Member(shellManifest));
            Assert.That(mergedFiles, Has.Member(finalFbx));
            Assert.That(mergedFiles.Count(path => path == shellPng), Is.EqualTo(1));
        }

        [Test]
        public void TryApplyDeferredProtectedPayloadCouplingOrRollback_WhenCouplingFails_RollsBackShellAndExtractedAssets()
        {
            string shellAssetPath = CreateImportedAsset();
            string finalFbxPath = CreateImportedFile("artifact.bytes", new byte[] { 1, 2, 3, 4 });
            var packageInfo = new InstalledPackageInfo
            {
                packageId = "pkg-protected-shell",
                installedFiles = new List<string> { shellAssetPath },
            };

            SetTryApplyCouplingOverride(typeof(CouplingImportGuardTests).GetMethod(
                nameof(FailCoupling),
                BindingFlags.NonPublic | BindingFlags.Static));

            MethodInfo method = GetGuardType().GetMethod(
                "TryApplyDeferredProtectedPayloadCouplingOrRollback",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args = { packageInfo, new List<string> { finalFbxPath }, null };
            bool success = (bool)method.Invoke(null, args);
            string error = args[2] as string;

            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo("The package protection step could not be completed on this machine."));
            Assert.That(File.Exists(GetAssetDiskPath(shellAssetPath)), Is.False);
            Assert.That(File.Exists(GetAssetDiskPath(finalFbxPath)), Is.False);
        }

        private static bool FailCoupling(string packageId, IReadOnlyList<string> installedFiles, out string error)
        {
            error = "forced coupling failure";
            return false;
        }

        private static MethodInfo GetGuardMethod()
        {
            MethodInfo method = GetGuardType()
                .GetMethod("TryApplyCouplingOrRollback", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);
            return method;
        }

        private static Type GetGuardType()
        {
            return GetCoreType("YUCP.Importer.Editor.PackageManager.Core.CouplingImportGuard");
        }

        private static void SetTryApplyCouplingOverride(MethodInfo method)
        {
            if (method == null)
            {
                CouplingImportGuardTestHooks.Reset();
                return;
            }

            CouplingImportGuardTestHooks.TryApplyCoupling = (packageId, installedFiles) =>
            {
                object[] args = { packageId, installedFiles, null };
                bool success = (bool)method.Invoke(null, args);
                return (success, args[2] as string);
            };
        }

        private string CreateImportedAsset()
        {
            _testAssetRoot = $"Assets/YUCP_TempTests/CouplingGuard_{Guid.NewGuid():N}";
            string diskRoot = GetAssetDiskPath(_testAssetRoot);
            Directory.CreateDirectory(diskRoot);

            string assetPath = $"{_testAssetRoot}/artifact.png";
            WritePng(GetAssetDiskPath(assetPath), Color.cyan);
            AssetDatabase.Refresh();
            return assetPath;
        }

        private string CreateImportedFile(string fileName, byte[] contents)
        {
            if (string.IsNullOrWhiteSpace(_testAssetRoot))
            {
                _testAssetRoot = $"Assets/YUCP_TempTests/CouplingGuard_{Guid.NewGuid():N}";
            }

            string diskRoot = GetAssetDiskPath(_testAssetRoot);
            Directory.CreateDirectory(diskRoot);

            string assetPath = $"{_testAssetRoot}/{fileName}";
            File.WriteAllBytes(GetAssetDiskPath(assetPath), contents);
            AssetDatabase.Refresh();
            return assetPath;
        }

        private void DeleteTestAssets()
        {
            if (string.IsNullOrWhiteSpace(_testAssetRoot))
                return;

            FileUtil.DeleteFileOrDirectory(_testAssetRoot);
            FileUtil.DeleteFileOrDirectory(_testAssetRoot + ".meta");

            string parent = Path.GetDirectoryName(_testAssetRoot)?.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(parent) && AssetDatabase.IsValidFolder(parent))
            {
                string parentDiskPath = GetAssetDiskPath(parent);
                if (Directory.Exists(parentDiskPath) && !Directory.EnumerateFileSystemEntries(parentDiskPath).Any())
                {
                    FileUtil.DeleteFileOrDirectory(parent);
                    FileUtil.DeleteFileOrDirectory(parent + ".meta");
                }
            }

            AssetDatabase.Refresh();
            _testAssetRoot = null;
        }

        private static string GetAssetDiskPath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void WritePng(string path, Color color)
        {
            var texture = new Texture2D(2, 2);
            texture.SetPixels(new[] { color, color, color, color });
            texture.Apply();
            try
            {
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static Type GetCoreType(string fullName)
        {
            Type editorType = typeof(InstalledPackageInfo).Assembly.GetType(fullName, false);
            Assert.That(editorType, Is.Not.Null, $"Expected to load type '{fullName}'.");
            return editorType;
        }
    }
}
