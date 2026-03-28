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
    public class ProtectedInstallFinalizationTests
    {
        private readonly List<string> _createdRoots = new List<string>();
        private readonly List<string> _createdWorkspaceRoots = new List<string>();
        private static bool s_releaseRuntimeResourcesCalled;
        private static IReadOnlyList<string> s_lastRollbackPaths;

        [TearDown]
        public void TearDown()
        {
            SetReleaseRuntimeResourcesOverride(null);
            SetRollbackImportedAssetsOverride(null);
            SetProtectedPayloadBrokerBridgeOverride(null);
            s_releaseRuntimeResourcesCalled = false;
            s_lastRollbackPaths = null;
            DeleteCreatedRoots();
            DeleteCreatedWorkspaceRoots();
        }

        [Test]
        public void FindInstalledShellRootAssetPath_UsesCurrentMetadataRoot()
        {
            const string packageId = "pkg-finalization-root";
            const string packageName = "Moved Package Root";
            string shellRoot = CreatePackageShell(packageId, packageName);

            var packageInfo = new InstalledPackageInfo
            {
                packageId = packageId,
                packageName = packageName,
            };

            MethodInfo method = GetCoordinatorType().GetMethod(
                "FindInstalledShellRootAssetPath",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args = { packageInfo };
            string resolvedRoot = method.Invoke(null, args) as string;

            Assert.That(resolvedRoot, Is.EqualTo(shellRoot));
        }

        [Test]
        public void BuildCommittedInstalledFiles_ExcludesManagedArtifactsAndKeepsFinalDerivedOutputs()
        {
            const string packageId = "pkg-finalization-commit";
            const string packageName = "Commit Root";

            string shellRoot = CreatePackageShell(packageId, packageName);
            string packageInfoPath = $"{shellRoot}/YUCP_PackageInfo.json";
            string shellIcon = CreatePngAssetFile($"{shellRoot}/Embedded/icon.png", Color.cyan);
            string shellManifest = CreateAssetFile("Assets/_Signing/PackageManifest.json", System.Text.Encoding.UTF8.GetBytes("{}"));
            string finalFbx = CreateAssetFile("Assets/Novaspil_Kitbash/Novaspil.bytes", new byte[] { 5, 6, 7, 8 });
            string patchAsset = CreateAssetFile("Packages/com.yucp.temp/Patches/DerivedFbxAsset_Test.asset", System.Text.Encoding.UTF8.GetBytes("patch"));
            string runtimeScript = CreateAssetFile("Packages/com.yucp.temp/Editor/YUCPPatchImporter.cs", System.Text.Encoding.UTF8.GetBytes("// temp runtime"));
            string tempInstall = CreateAssetFile($"{shellRoot}/_temp/YUCP_TempInstall_test.json", System.Text.Encoding.UTF8.GetBytes("{}"));
            string installerPreflight = CreateAssetFile("Packages/yucp.installed-packages/Editor/YUCP_InstallerPreflight_test.cs", System.Text.Encoding.UTF8.GetBytes("// temp installer"));

            var packageInfo = new InstalledPackageInfo
            {
                packageId = packageId,
                packageName = packageName,
                installedFiles = new List<string>
                {
                    shellManifest,
                    patchAsset,
                    tempInstall,
                    installerPreflight,
                },
            };

            MethodInfo method = GetCoordinatorType().GetMethod(
                "BuildCommittedInstalledFiles",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args =
            {
                packageInfo,
                new List<string> { patchAsset, runtimeScript, finalFbx, tempInstall },
                new List<string> { finalFbx },
                shellRoot,
            };
            var committedFiles = method.Invoke(null, args) as IReadOnlyList<string>;

            Assert.That(committedFiles, Is.Not.Null);
            Assert.That(committedFiles, Has.Member(packageInfoPath));
            Assert.That(committedFiles, Has.Member(shellIcon));
            Assert.That(committedFiles, Has.Member(shellManifest));
            Assert.That(committedFiles, Has.Member(finalFbx));
            Assert.That(committedFiles, Has.No.Member(patchAsset));
            Assert.That(committedFiles, Has.No.Member(runtimeScript));
            Assert.That(committedFiles, Has.No.Member(tempInstall));
            Assert.That(committedFiles, Has.No.Member(installerPreflight));
        }

        [Test]
        public void TryFinalizeProtectedInstall_RollsBackWhenProtectedPayloadIsMissingPaths()
        {
            const string packageId = "pkg-finalization-broker";
            const string packageName = "Broker Required Package";

            string shellRoot = CreatePackageShell(packageId, packageName);
            var packageInfo = new InstalledPackageInfo
            {
                packageId = packageId,
                packageName = packageName,
                protectedPayload = new ProtectedPayloadDescriptor
                {
                    protectedAssetId = "brokered-payload",
                    requiresBrokeredMaterialization = true,
                    brokerProtocolVersion = 1,
                },
            };

            MethodInfo method = GetCoordinatorType().GetMethod(
                "TryFinalizeProtectedInstall",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args =
            {
                packageInfo,
                Array.Empty<string>(),
                null,
                null,
                null,
            };

            object status = method.Invoke(null, args);
            var committedFiles = args[2] as IReadOnlyList<string>;
            string error = args[3] as string;
            bool rolledBackCleanly = args[4] is bool value && value;

            Assert.That(status?.ToString(), Is.EqualTo("Failed"));
            Assert.That(committedFiles ?? Array.Empty<string>(), Is.Empty);
            Assert.That(rolledBackCleanly, Is.True);
            Assert.That(
                error,
                Is.EqualTo("The protected package shell is missing signed protected payload file paths."));
            Assert.That(Directory.Exists(GetAssetDiskPath(shellRoot)), Is.False);
        }

        [Test]
        public void TryFinalizeProtectedInstall_SurfacesRollbackFailureWhenCleanupCannotComplete()
        {
            const string packageId = "pkg-finalization-rollback-failure";
            const string packageName = "Rollback Failure Package";

            string shellRoot = CreatePackageShell(packageId, packageName);
            var packageInfo = new InstalledPackageInfo
            {
                packageId = packageId,
                packageName = packageName,
                protectedPayload = new ProtectedPayloadDescriptor
                {
                    protectedAssetId = "brokered-payload",
                    requiresBrokeredMaterialization = true,
                    brokerProtocolVersion = 1,
                },
            };

            SetRollbackImportedAssetsOverride(typeof(ProtectedInstallFinalizationTests).GetMethod(
                nameof(FailRollbackImportedAssets),
                BindingFlags.NonPublic | BindingFlags.Static));

            MethodInfo method = GetCoordinatorType().GetMethod(
                "TryFinalizeProtectedInstall",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args =
            {
                packageInfo,
                Array.Empty<string>(),
                null,
                null,
                null,
            };

            object status = method.Invoke(null, args);
            string error = args[3] as string;
            bool rolledBackCleanly = args[4] is bool value && value;

            Assert.That(status?.ToString(), Is.EqualTo("Failed"));
            Assert.That(rolledBackCleanly, Is.False);
            Assert.That(error, Does.Contain("The protected package shell is missing signed protected payload file paths."));
            Assert.That(error, Does.Contain("The importer could not roll back the package cleanly: Simulated rollback failure."));
            Assert.That(s_lastRollbackPaths, Is.Not.Null);
            Assert.That(s_lastRollbackPaths, Has.Member($"{shellRoot}/YUCP_PackageInfo.json"));
            Assert.That(Directory.Exists(GetAssetDiskPath(shellRoot)), Is.True);
        }

        [Test]
        public void TryFinalizeProtectedInstall_DoesNotSurfaceBrokerDiagnosticsToUser()
        {
            const string packageId = "pkg-finalization-broker-redacted";
            const string packageName = "Broker Redaction Package";

            string shellRoot = CreatePackageShell(packageId, packageName);
            var packageInfo = new InstalledPackageInfo
            {
                packageId = packageId,
                packageName = packageName,
                protectedPayload = new ProtectedPayloadDescriptor
                {
                    protectedAssetId = "brokered-payload",
                    requiresBrokeredMaterialization = true,
                    brokerProtocolVersion = 1,
                },
            };

            SetProtectedPayloadBrokerBridgeOverride(
                new FailingBrokerBridge(
                    "The package protection runtime is not installed for this Windows user. Runtime root: C:\\Users\\Example\\AppData\\Local\\Programs\\YUCP\\CouplingRuntime"));

            MethodInfo method = GetCoordinatorType().GetMethod(
                "TryFinalizeProtectedInstall",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args =
            {
                packageInfo,
                Array.Empty<string>(),
                null,
                null,
                null,
            };

            object status = method.Invoke(null, args);
            string error = args[3] as string;
            bool rolledBackCleanly = args[4] is bool value && value;

            Assert.That(status?.ToString(), Is.EqualTo("Failed"));
            Assert.That(rolledBackCleanly, Is.True);
            Assert.That(error, Is.EqualTo("The package protection step could not be completed on this machine."));
            Assert.That(error, Does.Not.Contain("runtime is not installed"));
            Assert.That(error, Does.Not.Contain("Runtime root:"));
            Assert.That(Directory.Exists(GetAssetDiskPath(shellRoot)), Is.False);
        }

        [Test]
        public void NestedProtectedDerivedAssetValidation_RejectsLegacyLocalRecoveryPatchAsset()
        {
            string patchAsset = CreateAssetFile(
                "Packages/com.yucp.temp/Patches/DerivedFbxAsset_Test.asset",
                System.Text.Encoding.UTF8.GetBytes(
                    "requiresLicense: 0\n" +
                    "licensePackageId: \n" +
                    "requiresServerUnlock: 0\n" +
                    "protectedAssetId: \n"));

            MethodInfo method = GetProtectedPayloadComShimBridgeType().GetMethod(
                "TryValidateNestedProtectedDerivedAssets",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args =
            {
                "pkg-protected-import",
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                new[] { patchAsset },
                null,
            };

            bool success = (bool)method.Invoke(null, args);
            string error = args[3] as string;

            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo($"Nested derived patch asset is not purchase-gated: {patchAsset}"));
        }

        [Test]
        public void NestedProtectedDerivedAssetValidation_AcceptsPurchaseBoundServerUnlockedPatchAsset()
        {
            string patchAsset = CreateAssetFile(
                "Packages/com.yucp.temp/Patches/DerivedFbxAsset_Test.asset",
                System.Text.Encoding.UTF8.GetBytes(
                    "requiresLicense: 1\n" +
                    "licensePackageId: pkg-protected-import\n" +
                    "requiresServerUnlock: 1\n" +
                    "protectedAssetId: 91dc8de801b44d5b8ea51d210b56c323\n"));

            MethodInfo method = GetProtectedPayloadComShimBridgeType().GetMethod(
                "TryValidateNestedProtectedDerivedAssets",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args =
            {
                "pkg-protected-import",
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                new[] { patchAsset },
                null,
            };

            bool success = (bool)method.Invoke(null, args);
            string error = args[3] as string;

            Assert.That(success, Is.True);
            Assert.That(error, Is.Null);
        }

        [Test]
        public void TryDeleteManagedWorkspaceArtifacts_ReleasesRuntimeResourcesAndDeletesPatchScratch()
        {
            string workspaceDll = CreateWorkspaceFile("Library/YUCP/hdiffz.dll", new byte[] { 1, 2, 3 });
            string workspacePatch = CreateWorkspaceFile("Library/YUCP/patch_test.hdiff", new byte[] { 4, 5, 6 });
            string workspaceSwap = CreateWorkspaceFile("Library/YUCP/guid_swaps.json", System.Text.Encoding.UTF8.GetBytes("{}"));

            SetReleaseRuntimeResourcesOverride(typeof(ProtectedInstallFinalizationTests).GetMethod(
                nameof(SucceedReleaseRuntimeResources),
                BindingFlags.NonPublic | BindingFlags.Static));

            MethodInfo method = GetCoordinatorType().GetMethod(
                "TryDeleteManagedWorkspaceArtifacts",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            bool success = (bool)method.Invoke(null, null);

            Assert.That(success, Is.True);
            Assert.That(s_releaseRuntimeResourcesCalled, Is.True);
            Assert.That(File.Exists(GetProjectDiskPath(workspaceDll)), Is.False);
            Assert.That(File.Exists(GetProjectDiskPath(workspacePatch)), Is.False);
            Assert.That(File.Exists(GetProjectDiskPath(workspaceSwap)), Is.False);
        }

        private static bool FailRollbackImportedAssets(IReadOnlyList<string> assetPaths, out string error)
        {
            s_lastRollbackPaths = assetPaths?.ToArray() ?? Array.Empty<string>();
            error = "Simulated rollback failure.";
            return false;
        }

        private static bool SucceedReleaseRuntimeResources()
        {
            s_releaseRuntimeResourcesCalled = true;
            return true;
        }

        private sealed class FailingBrokerBridge : YUCP.Importer.Editor.PackageManager.Core.IProtectedPayloadBrokerBridge
        {
            private readonly string _error;

            public FailingBrokerBridge(string error)
            {
                _error = error;
            }

            public bool TryFinalizeProtectedInstall(
                InstalledPackageInfo packageInfo,
                out IReadOnlyList<string> materializedAssetPaths,
                out string error,
                out bool pending)
            {
                materializedAssetPaths = Array.Empty<string>();
                error = _error;
                pending = false;
                return false;
            }
        }

        private string CreatePackageShell(string packageId, string packageName)
        {
            string shellRoot = $"Packages/yucp.installed-packages/finalization-{Guid.NewGuid():N}";
            CreateAssetFile(
                $"{shellRoot}/YUCP_PackageInfo.json",
                System.Text.Encoding.UTF8.GetBytes("{\n" +
                    $"  \"packageId\": \"{packageId}\",\n" +
                    $"  \"packageName\": \"{packageName}\"\n" +
                    "}"));
            return shellRoot;
        }

        private static Type GetProtectedPayloadComShimBridgeType()
        {
            var coordinatorAssembly = GetCoordinatorType().Assembly;
            var bridgeType = coordinatorAssembly.GetType("YUCP.Importer.Editor.PackageManager.Core.ProtectedPayloadComShimBridge");
            Assert.That(bridgeType, Is.Not.Null);
            return bridgeType;
        }

        private string CreatePngAssetFile(string assetPath, Color color)

        {

            var texture = new Texture2D(2, 2);

            texture.SetPixels(new[] { color, color, color, color });

            texture.Apply();

            try

            {

                return CreateAssetFile(assetPath, texture.EncodeToPNG());

            }

            finally

            {

                UnityEngine.Object.DestroyImmediate(texture);

            }

        }



        private string CreateAssetFile(string assetPath, byte[] contents)
        {
            string normalizedPath = assetPath.Replace('\\', '/');
            string diskPath = GetAssetDiskPath(normalizedPath);
            string directory = Path.GetDirectoryName(diskPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(diskPath, contents);
            TrackCreatedRoot(normalizedPath);
            AssetDatabase.Refresh();
            return normalizedPath;
        }

        private string CreateWorkspaceFile(string relativePath, byte[] contents)
        {
            string normalizedPath = relativePath.Replace('\\', '/');
            string diskPath = GetProjectDiskPath(normalizedPath);
            string directory = Path.GetDirectoryName(diskPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(diskPath, contents);
            TrackWorkspaceRoot(normalizedPath);
            return normalizedPath;
        }

        private void TrackCreatedRoot(string assetPath)
        {
            string normalizedPath = assetPath.Replace('\\', '/');
            string[] segments = normalizedPath.Split('/');
            string root = normalizedPath.StartsWith("Packages/yucp.installed-packages/finalization-", StringComparison.OrdinalIgnoreCase)
                ? segments.Length >= 3
                    ? string.Join("/", segments.Take(3))
                    : null
                : normalizedPath.StartsWith("Packages/com.yucp.temp/", StringComparison.OrdinalIgnoreCase)
                    ? "Packages/com.yucp.temp"
                    : normalizedPath.StartsWith("Assets/Novaspil_Kitbash/", StringComparison.OrdinalIgnoreCase)
                        ? "Assets/Novaspil_Kitbash"
                        : normalizedPath.StartsWith("Assets/_Signing/", StringComparison.OrdinalIgnoreCase)
                            ? "Assets/_Signing"
                            : normalizedPath.StartsWith("Packages/yucp.installed-packages/Editor/", StringComparison.OrdinalIgnoreCase)
                                ? "Packages/yucp.installed-packages/Editor"
                                : null;

            if (!string.IsNullOrWhiteSpace(root) && !_createdRoots.Contains(root))
            {
                _createdRoots.Add(root);
            }
        }

        private void DeleteCreatedRoots()
        {
            foreach (string root in _createdRoots.OrderByDescending(path => path.Length))
            {
                FileUtil.DeleteFileOrDirectory(root);
                FileUtil.DeleteFileOrDirectory(root + ".meta");
            }

            AssetDatabase.Refresh();
            _createdRoots.Clear();
        }

        private void TrackWorkspaceRoot(string relativePath)
        {
            if (relativePath.StartsWith("Library/YUCP/", StringComparison.OrdinalIgnoreCase) &&
                !_createdWorkspaceRoots.Contains("Library/YUCP"))
            {
                _createdWorkspaceRoots.Add("Library/YUCP");
            }
        }

        private void DeleteCreatedWorkspaceRoots()
        {
            foreach (string root in _createdWorkspaceRoots.OrderByDescending(path => path.Length))
            {
                string diskPath = GetProjectDiskPath(root);
                if (Directory.Exists(diskPath))
                {
                    Directory.Delete(diskPath, true);
                }
            }

            _createdWorkspaceRoots.Clear();
        }

        private static string GetAssetDiskPath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string GetProjectDiskPath(string relativePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void SetRollbackImportedAssetsOverride(MethodInfo method)
        {
            Type coordinatorType = GetCoordinatorType();
            FieldInfo field = coordinatorType.GetField("s_tryRollbackImportedAssetsOverride", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(field, Is.Not.Null);
            if (method == null)
            {
                field.SetValue(null, null);
                return;
            }

            Delegate callback = Delegate.CreateDelegate(field.FieldType, method);
            field.SetValue(null, callback);
        }

        private static void SetReleaseRuntimeResourcesOverride(MethodInfo method)
        {
            Type coordinatorType = GetCoordinatorType();
            FieldInfo field = coordinatorType.GetField("s_tryReleaseRuntimeResourcesOverride", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(field, Is.Not.Null);
            if (method == null)
            {
                field.SetValue(null, null);
                return;
            }

            Delegate callback = Delegate.CreateDelegate(field.FieldType, method);
            field.SetValue(null, callback);
        }

        private static void SetProtectedPayloadBrokerBridgeOverride(object bridge)
        {
            Type brokerServiceType = GetCoordinatorType().GetNestedType(
                "ProtectedPayloadBrokerService",
                BindingFlags.NonPublic);
            Assert.That(brokerServiceType, Is.Not.Null, "Expected to load ProtectedPayloadBrokerService.");

            FieldInfo cachedBridgeField = brokerServiceType.GetField("_cachedBridge", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo resolvedBridgeField = brokerServiceType.GetField("_resolvedBridge", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(cachedBridgeField, Is.Not.Null);
            Assert.That(resolvedBridgeField, Is.Not.Null);

            cachedBridgeField.SetValue(null, bridge);
            resolvedBridgeField.SetValue(null, bridge != null);
        }

        private static Type GetCoordinatorType()
        {
            Type editorType = typeof(InstalledPackageInfo).Assembly.GetType(
                "YUCP.Importer.Editor.PackageManager.Core.ProtectedInstallFinalizationCoordinator",
                false);
            Assert.That(editorType, Is.Not.Null, "Expected to load ProtectedInstallFinalizationCoordinator.");
            return editorType;
        }
    }
}
