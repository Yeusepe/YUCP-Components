using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager;

namespace YUCP.Importer.Editor.Tests
{
    public class CouplingImportGuardTests
    {
        private string _testAssetRoot;

        [TearDown]
        public void TearDown()
        {
            SetTryApplyCouplingOverride(null);
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
            Assert.That(error, Does.Contain("forced coupling failure"));
            Assert.That(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath), Is.Null);
            Assert.That(File.Exists(GetAssetDiskPath(assetPath)), Is.False);
        }

        private static bool FailCoupling(string packageId, IReadOnlyList<string> installedFiles, out string error)
        {
            error = "forced coupling failure";
            return false;
        }

        private static MethodInfo GetGuardMethod()
        {
            MethodInfo method = GetCoreType("YUCP.Importer.Editor.PackageManager.Core.CouplingImportGuard")
                .GetMethod("TryApplyCouplingOrRollback", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);
            return method;
        }

        private static void SetTryApplyCouplingOverride(MethodInfo method)
        {
            Type guardType = GetCoreType("YUCP.Importer.Editor.PackageManager.Core.CouplingImportGuard");
            FieldInfo field = guardType.GetField("s_tryApplyCouplingOverride", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(field, Is.Not.Null);
            if (method == null)
            {
                field.SetValue(null, null);
                return;
            }

            Delegate callback = Delegate.CreateDelegate(field.FieldType, method);
            field.SetValue(null, callback);
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
