using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YUCP.Importer.Editor.PackageManager;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public class ProtectedInstallFinalizationTests
    {
        private const string RealProtectedPackagePath = @"C:\Users\svalp\Downloads\Novaspil Kitbash Test License Verification_1.0.0.unitypackage";
        private readonly List<string> _createdRoots = new List<string>();
        private readonly List<string> _createdWorkspaceRoots = new List<string>();
        private readonly List<string> _registeredPackageIds = new List<string>();
        private static bool s_releaseRuntimeResourcesCalled;
        private static IReadOnlyList<string> s_lastRollbackPaths;
        private static IReadOnlyList<string> s_lastCouplingPaths;
        private static string s_lastCouplingPackageId;

        [TearDown]
        public void TearDown()
        {
            ProtectedInstallFinalizationCoordinatorTestHooks.Reset();
            CouplingImportGuardTestHooks.Reset();
            s_releaseRuntimeResourcesCalled = false;
            s_lastRollbackPaths = null;
            s_lastCouplingPaths = null;
            s_lastCouplingPackageId = null;
            ClearPendingFinalizationState();
            UnregisterTrackedPackages();
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
        public void ProtectedImportFastPath_RemovesTransientInstallerArtifacts_WhenDependenciesAreAlreadySatisfied()
        {
            const string packageId = "pkg-fast-path-ready";
            const string packageName = "Fast Path Ready";

            string shellRoot = CreatePackageShell(packageId, packageName);
            string tempInstall = CreateAssetFile(
                $"{shellRoot}/_temp/YUCP_TempInstall_test.json",
                System.Text.Encoding.UTF8.GetBytes("{}"));
            string installerScript = CreateAssetFile(
                "Packages/yucp.installed-packages/Editor/YUCP_Installer_test.cs.yucp_disabled",
                System.Text.Encoding.UTF8.GetBytes("// installer"));
            string installerAsmdef = CreateAssetFile(
                "Packages/yucp.installed-packages/Editor/YUCP_Installer_test.asmdef.yucp_disabled",
                System.Text.Encoding.UTF8.GetBytes("{}"));
            string guardianScript = CreateAssetFile(
                "Packages/yucp.packageguardian/Editor/PackageGuardianMini.cs",
                System.Text.Encoding.UTF8.GetBytes("// guardian"));

            var packageInfo = new InstalledPackageInfo
            {
                packageId = packageId,
                packageName = packageName,
                dependencies = new Dictionary<string, string>
                {
                    ["com.yucp.importer"] = ">=0.0.0",
                },
                protectedPayload = new ProtectedPayloadDescriptor
                {
                    protectedAssetId = "protected-fast-path",
                },
                installedFiles = new List<string>
                {
                    tempInstall,
                    installerScript,
                    installerAsmdef,
                    guardianScript,
                },
            };

            bool success = ProtectedImportFastPath.TryPrepareForDirectApply(
                packageInfo,
                out ProtectedImportFastPath.PreparedDirectApplyState preparedState,
                out bool requiresAssetRefresh,
                out string message);

            Assert.That(success, Is.True, message ?? "Expected the direct protected-import fast path to activate.");
            Assert.That(requiresAssetRefresh, Is.True);
            Assert.That(preparedState, Is.Not.Null);
            ProtectedImportFastPath.CommitPreparedDirectApply(preparedState);
            Assert.That(message, Does.Contain("skipped the generated installer compile handoff"));
            Assert.That(File.Exists(GetAssetDiskPath(installerScript)), Is.False);
            Assert.That(File.Exists(GetAssetDiskPath(installerAsmdef)), Is.False);
            Assert.That(File.Exists(GetAssetDiskPath(guardianScript)), Is.False);
            Assert.That(packageInfo.installedFiles, Has.Member(tempInstall));
            Assert.That(packageInfo.installedFiles, Has.No.Member(installerScript));
            Assert.That(packageInfo.installedFiles, Has.No.Member(installerAsmdef));
            Assert.That(packageInfo.installedFiles, Has.No.Member(guardianScript));
        }

        [Test]
        public void ProtectedImportFastPath_DoesNotActivate_WhenDependenciesAreMissing()
        {
            const string packageId = "pkg-fast-path-missing-dep";
            const string packageName = "Fast Path Missing Dependency";

            string shellRoot = CreatePackageShell(packageId, packageName);
            string tempInstall = CreateAssetFile(
                $"{shellRoot}/_temp/YUCP_TempInstall_test.json",
                System.Text.Encoding.UTF8.GetBytes("{}"));
            string installerScript = CreateAssetFile(
                "Packages/yucp.installed-packages/Editor/YUCP_Installer_missing_dep.cs.yucp_disabled",
                System.Text.Encoding.UTF8.GetBytes("// installer"));

            var packageInfo = new InstalledPackageInfo
            {
                packageId = packageId,
                packageName = packageName,
                dependencies = new Dictionary<string, string>
                {
                    ["com.example.missing"] = "1.0.0",
                },
                protectedPayload = new ProtectedPayloadDescriptor
                {
                    protectedAssetId = "protected-missing-dependency",
                },
                installedFiles = new List<string>
                {
                    tempInstall,
                    installerScript,
                },
            };

            bool success = ProtectedImportFastPath.TryPrepareForDirectApply(
                packageInfo,
                out ProtectedImportFastPath.PreparedDirectApplyState preparedState,
                out bool requiresAssetRefresh,
                out string message);

            Assert.That(success, Is.False);
            Assert.That(requiresAssetRefresh, Is.False);
            Assert.That(message, Does.Contain("missing required dependency"));
            Assert.That(File.Exists(GetAssetDiskPath(installerScript)), Is.True);
            Assert.That(packageInfo.installedFiles, Has.Member(installerScript));
            Assert.That(preparedState, Is.Null);
        }

        [Test]
        public void ProtectedImportFastPath_DoesNotActivate_WhenUnexpectedDisabledAssetsRemain()
        {
            const string packageId = "pkg-fast-path-extra-disabled";
            const string packageName = "Fast Path Extra Disabled";

            string shellRoot = CreatePackageShell(packageId, packageName);
            string tempInstall = CreateAssetFile(
                $"{shellRoot}/_temp/YUCP_TempInstall_test.json",
                System.Text.Encoding.UTF8.GetBytes("{}"));
            string supportedInstallerScript = CreateAssetFile(
                "Packages/yucp.installed-packages/Editor/YUCP_Installer_extra_disabled.cs.yucp_disabled",
                System.Text.Encoding.UTF8.GetBytes("// installer"));
            string unexpectedDisabledAsset = CreateAssetFile(
                "Packages/yucp.packageguardian/Editor/PackageGuardianMini.cs.yucp_disabled",
                System.Text.Encoding.UTF8.GetBytes("// unexpected"));
            var packageInfo = new InstalledPackageInfo
            {
                packageId = packageId,
                packageName = packageName,
                dependencies = new Dictionary<string, string>
                {
                    ["com.yucp.importer"] = ">=0.0.0",
                },
                protectedPayload = new ProtectedPayloadDescriptor
                {
                    protectedAssetId = "protected-extra-disabled",
                },
                installedFiles = new List<string>
                {
                    tempInstall,
                    supportedInstallerScript,
                    unexpectedDisabledAsset,
                },
            };

            bool success = ProtectedImportFastPath.TryPrepareForDirectApply(
                packageInfo,
                out ProtectedImportFastPath.PreparedDirectApplyState preparedState,
                out bool requiresAssetRefresh,
                out string message);

            Assert.That(success, Is.False);
            Assert.That(requiresAssetRefresh, Is.False);
            Assert.That(message, Does.Contain("non-installer disabled assets"));
            Assert.That(File.Exists(GetAssetDiskPath(supportedInstallerScript)), Is.True);
            Assert.That(File.Exists(GetAssetDiskPath(unexpectedDisabledAsset)), Is.True);
            Assert.That(preparedState, Is.Null);
        }

        [Test]
        public void ProtectedImportFastPath_DoesNotActivate_ForCaretZeroMinorRangeMismatch()
        {
            const string packageId = "pkg-fast-path-caret-zero";
            const string packageName = "Fast Path Caret Zero";

            string shellRoot = CreatePackageShell(packageId, packageName);
            string tempInstall = CreateAssetFile(
                $"{shellRoot}/_temp/YUCP_TempInstall_test.json",
                System.Text.Encoding.UTF8.GetBytes("{}"));
            string installerScript = CreateAssetFile(
                "Packages/yucp.installed-packages/Editor/YUCP_Installer_caret_zero.cs.yucp_disabled",
                System.Text.Encoding.UTF8.GetBytes("// installer"));
            CreateAssetFile(
                "Packages/com.yucp.temp/package.json",
                System.Text.Encoding.UTF8.GetBytes("{\"name\":\"com.yucp.temp\",\"version\":\"0.9.0\"}"));

            var packageInfo = new InstalledPackageInfo
            {
                packageId = packageId,
                packageName = packageName,
                dependencies = new Dictionary<string, string>
                {
                    ["com.yucp.temp"] = "^0.1.3",
                },
                protectedPayload = new ProtectedPayloadDescriptor
                {
                    protectedAssetId = "protected-caret-zero",
                },
                installedFiles = new List<string>
                {
                    tempInstall,
                    installerScript,
                },
            };

            bool success = ProtectedImportFastPath.TryPrepareForDirectApply(
                packageInfo,
                out ProtectedImportFastPath.PreparedDirectApplyState preparedState,
                out bool requiresAssetRefresh,
                out string message);

            Assert.That(success, Is.False);
            Assert.That(requiresAssetRefresh, Is.False);
            Assert.That(message, Does.Contain("requires ^0.1.3"));
            Assert.That(File.Exists(GetAssetDiskPath(installerScript)), Is.True);
            Assert.That(preparedState, Is.Null);
        }

        [Test]
        public void ProtectedImportFastPath_DoesNotActivate_ForPrereleaseDependencyMismatch()
        {
            const string packageId = "pkg-fast-path-prerelease";
            const string packageName = "Fast Path Prerelease";

            string shellRoot = CreatePackageShell(packageId, packageName);
            string tempInstall = CreateAssetFile(
                $"{shellRoot}/_temp/YUCP_TempInstall_test.json",
                System.Text.Encoding.UTF8.GetBytes("{}"));
            string installerScript = CreateAssetFile(
                "Packages/yucp.installed-packages/Editor/YUCP_Installer_prerelease.cs.yucp_disabled",
                System.Text.Encoding.UTF8.GetBytes("// installer"));
            CreateAssetFile(
                "Packages/com.yucp.temp/package.json",
                System.Text.Encoding.UTF8.GetBytes("{\"name\":\"com.yucp.temp\",\"version\":\"1.0.0-preview.1\"}"));

            var packageInfo = new InstalledPackageInfo
            {
                packageId = packageId,
                packageName = packageName,
                dependencies = new Dictionary<string, string>
                {
                    ["com.yucp.temp"] = "1.0.0-preview.2",
                },
                protectedPayload = new ProtectedPayloadDescriptor
                {
                    protectedAssetId = "protected-prerelease",
                },
                installedFiles = new List<string>
                {
                    tempInstall,
                    installerScript,
                },
            };

            bool success = ProtectedImportFastPath.TryPrepareForDirectApply(
                packageInfo,
                out ProtectedImportFastPath.PreparedDirectApplyState preparedState,
                out bool requiresAssetRefresh,
                out string message);

            Assert.That(success, Is.False);
            Assert.That(requiresAssetRefresh, Is.False);
            Assert.That(message, Does.Contain("prerelease version metadata"));
            Assert.That(File.Exists(GetAssetDiskPath(installerScript)), Is.True);
            Assert.That(preparedState, Is.Null);
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

            LogAssert.Expect(LogType.Error, "[YUCP PackageManager] Protected install broker error.");
            object status = method.Invoke(null, args);
            string error = args[3] as string;
            bool rolledBackCleanly = args[4] is bool value && value;

            Assert.That(status?.ToString(), Is.EqualTo("Failed"));
            Assert.That(rolledBackCleanly, Is.True);
            Assert.That(error, Is.EqualTo("The package protection step could not be completed on this machine."));
            Assert.That(error, Does.Not.Contain("runtime is not installed"));
            Assert.That(error, Does.Not.Contain("Runtime root:"));
            Assert.That(error, Does.Not.Contain("Novaspil.fbx"));
            Assert.That(error, Does.Not.Contain("exit code"));
            Assert.That(Directory.Exists(GetAssetDiskPath(shellRoot)), Is.False);
        }

        [Test]
        public void TryConsumePendingFinalization_WithRealProtectedPackageShell_CompletesProtectedMaterialization()
        {
            RealProtectedShellContext context = CreateRealProtectedShellContext(RealProtectedPackagePath);
            AssertProtectedMaterializationRuntimeReady(context.PackageInfo);

            RegisterTrackedPackage(context.PackageInfo);
            InvokeQueuePendingFinalization(context.PackageInfo, Array.Empty<string>());
            InvokeTryConsumePendingFinalization();

            AssetDatabase.Refresh();

            InstalledPackageInfo registeredPackage = InstalledPackageRegistry.Load()?.GetPackage(context.PackageInfo.packageId);
            Assert.That(registeredPackage, Is.Not.Null);
            Assert.That(registeredPackage.installedFiles, Is.Not.Null);
            Assert.That(registeredPackage.installedFiles, Has.Some.EqualTo("Assets/Novaspil_Kitbash/Novaspil.fbx"));
            Assert.That(File.Exists(GetProjectDiskPath("Assets/Novaspil_Kitbash/Novaspil.fbx")), Is.True);
            Assert.That(HasPendingFinalizationState(), Is.False);

            TrackCreatedPaths(registeredPackage.installedFiles);
        }

        [Test]
        public void TryFinalizeProtectedInstall_WithRealProtectedPackageShell_CompletesProtectedMaterialization_WhenInvokedDirectly()
        {
            RealProtectedShellContext context = CreateRealProtectedShellContext(RealProtectedPackagePath);
            AssertProtectedMaterializationRuntimeReady(context.PackageInfo);

            MethodInfo method = GetCoordinatorType().GetMethod(
                "TryFinalizeProtectedInstall",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args =
            {
                context.PackageInfo,
                Array.Empty<string>(),
                null,
                null,
                null,
            };

            object status = method.Invoke(null, args);
            var committedFiles = args[2] as IReadOnlyList<string>;
            string error = args[3] as string;
            bool rolledBackCleanly = args[4] is bool value && value;

            TrackCreatedPaths(committedFiles);
            TrackCreatedPaths(context.PackageInfo.installedFiles);

            Assert.That(status?.ToString(), Is.EqualTo("Completed"), error);
            Assert.That(error, Is.Null.Or.Empty);
            Assert.That(rolledBackCleanly, Is.True);
            Assert.That(committedFiles ?? Array.Empty<string>(), Is.Not.Empty);
        }

        [Test]
        public void TryFinalizeProtectedInstall_WithBrokeredMaterialization_AppliesCouplingToCommittedDerivedOutputs()
        {
            const string packageId = "pkg-finalization-broker-coupling";
            const string packageName = "Broker Coupling Package";
            const string brokerPayloadPng = "Assets/Novabeast_V1_2/Materials/eyes1Tex.png";
            const string brokerPatchAsset = "Packages/com.yucp.temp/Patches/DerivedFbxAsset_Test.asset";
            const string finalFbx = "Assets/Novaspil_Kitbash/Novaspil.fbx";

            string shellRoot = CreatePackageShell(packageId, packageName);
            string shellIcon = CreatePngAssetFile($"{shellRoot}/Embedded/icon.png", Color.cyan);

            var packageInfo = new InstalledPackageInfo
            {
                packageId = packageId,
                packageName = packageName,
                installedFiles = new List<string>
                {
                    $"{shellRoot}/YUCP_PackageInfo.json",
                },
                protectedPayload = new ProtectedPayloadDescriptor
                {
                    protectedAssetId = "brokered-coupling-payload",
                    payloadAssetPaths = new[] { brokerPayloadPng, finalFbx },
                    requiresBrokeredMaterialization = true,
                    brokerProtocolVersion = 1,
                },
            };

            SetProtectedPayloadBrokerBridgeOverride(new SuccessfulBrokerBridge(
                (brokerPayloadPng, new byte[] { 1, 2, 3, 4 }),
                (brokerPatchAsset, System.Text.Encoding.UTF8.GetBytes("patch"))));
            SetTryMaterializePatchAssetsOverride(typeof(ProtectedInstallFinalizationTests).GetMethod(
                nameof(MaterializePatchAssetToFinalFbx),
                BindingFlags.NonPublic | BindingFlags.Static));
            SetTryApplyCouplingOverride(typeof(ProtectedInstallFinalizationTests).GetMethod(
                nameof(CaptureCoupling),
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

            string expectedImportError =
                $"ImportFBX Errors:\nCouldn't read file {GetProjectDiskPath(finalFbx).Replace('\\', '/')}.\nUnexpected file type\n\n";
            LogAssert.Expect(LogType.Error, expectedImportError);
            object status = method.Invoke(null, args);
            var committedFiles = args[2] as IReadOnlyList<string>;
            string error = args[3] as string;
            bool rolledBackCleanly = args[4] is bool value && value;

            TrackCreatedPaths(committedFiles);
            TrackCreatedPaths(packageInfo.installedFiles);

            Assert.That(status?.ToString(), Is.EqualTo("Completed"), error);
            Assert.That(error, Is.Null.Or.Empty);
            Assert.That(rolledBackCleanly, Is.True);
            Assert.That(committedFiles, Has.Member(finalFbx));
            Assert.That(packageInfo.installedFiles, Has.Member(finalFbx));
            Assert.That(File.Exists(GetProjectDiskPath(finalFbx)), Is.True);
            Assert.That(s_lastCouplingPackageId, Is.EqualTo(packageId));
            Assert.That(s_lastCouplingPaths, Is.Not.Null);
            Assert.That(s_lastCouplingPaths, Has.Member(finalFbx));
            Assert.That(s_lastCouplingPaths, Has.No.Member(brokerPayloadPng));
            Assert.That(s_lastCouplingPaths, Has.No.Member(shellIcon));
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

        [Test]
        public void InstallerCoordinationState_DetectsActiveInstallerMarkers_WhenTempInstallDescriptorExists()
        {
            CreateAssetFile(
                "Packages/yucp.installed-packages/TestPackage/_temp/YUCP_TempInstall_test.json",
                System.Text.Encoding.UTF8.GetBytes("{}"));
            CreateWorkspaceFile("Library/YUCP/install.scheduled", Array.Empty<byte>());

            Assert.That(InstallerCoordinationState.HasPendingInstallerHandoff(), Is.True);
        }

        [Test]
        public void InstallerCoordinationState_IgnoresCompleteMarkerWithoutActiveHandoff()
        {
            CreateWorkspaceFile("Library/YUCP/install.complete", Array.Empty<byte>());

            Assert.That(InstallerCoordinationState.HasPendingInstallerHandoff(), Is.False);
        }

        [Test]
        public void InstallerCoordinationState_IgnoresMarkers_WhenNoTempInstallDescriptorExists()
        {
            CreateWorkspaceFile("Library/YUCP/install.scheduled", Array.Empty<byte>());

            Assert.That(InstallerCoordinationState.HasPendingInstallerHandoff(), Is.False);
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

        private static bool CaptureCoupling(string packageId, IReadOnlyList<string> installedFiles, out string error)
        {
            s_lastCouplingPackageId = packageId;
            s_lastCouplingPaths = installedFiles?.ToArray() ?? Array.Empty<string>();
            error = null;
            return true;
        }

        private static bool MaterializePatchAssetToFinalFbx(
            IReadOnlyList<string> patchAssetPaths,
            out IReadOnlyList<string> createdAssetPaths,
            out string error)
        {
            Assert.That(patchAssetPaths, Has.Member("Packages/com.yucp.temp/Patches/DerivedFbxAsset_Test.asset"));

            const string finalFbx = "Assets/Novaspil_Kitbash/Novaspil.fbx";
            string diskPath = GetProjectDiskPath(finalFbx);
            Directory.CreateDirectory(Path.GetDirectoryName(diskPath) ?? string.Empty);
            File.WriteAllBytes(diskPath, new byte[] { 5, 6, 7, 8 });
            AssetDatabase.Refresh();

            createdAssetPaths = new[] { finalFbx };
            error = null;
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

        private sealed class SuccessfulBrokerBridge : YUCP.Importer.Editor.PackageManager.Core.IProtectedPayloadBrokerBridge
        {
            private readonly (string assetPath, byte[] contents)[] _assets;

            public SuccessfulBrokerBridge(params (string assetPath, byte[] contents)[] assets)
            {
                _assets = assets ?? Array.Empty<(string assetPath, byte[] contents)>();
            }

            public bool TryFinalizeProtectedInstall(
                InstalledPackageInfo packageInfo,
                out IReadOnlyList<string> materializedAssetPaths,
                out string error,
                out bool pending)
            {
                error = null;
                pending = false;
                var materializedPaths = new List<string>();
                foreach ((string assetPath, byte[] contents) in _assets)
                {
                    string diskPath = GetProjectDiskPath(assetPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(diskPath) ?? string.Empty);
                    File.WriteAllBytes(diskPath, contents ?? Array.Empty<byte>());
                    materializedPaths.Add(assetPath);
                }
                AssetDatabase.Refresh();
                materializedAssetPaths = materializedPaths;
                return true;
            }
        }

        private sealed class RealProtectedShellContext
        {
            public InstalledPackageInfo PackageInfo { get; set; }
            public IReadOnlyList<string> ImportedPaths { get; set; }
        }

        private RealProtectedShellContext CreateRealProtectedShellContext(string unityPackagePath)
        {
            IReadOnlyList<string> importedPaths = ExpandUnityPackageShellIntoProject(unityPackagePath);

            string metadataAssetPath = importedPaths.FirstOrDefault(
                path => path.EndsWith("/YUCP_PackageInfo.json", StringComparison.OrdinalIgnoreCase));
            string protectedPayloadAssetPath = importedPaths.FirstOrDefault(
                path => path.EndsWith("/YUCP_ProtectedPayload.json", StringComparison.OrdinalIgnoreCase));
            string tempInstallAssetPath = importedPaths.FirstOrDefault(
                path => path.Contains("/_temp/YUCP_TempInstall_", StringComparison.OrdinalIgnoreCase));
            string manifestAssetPath = importedPaths.FirstOrDefault(
                path => path.Equals("Assets/_Signing/PackageManifest.json", StringComparison.OrdinalIgnoreCase));

            Assert.That(metadataAssetPath, Is.Not.Null.And.Not.Empty);
            Assert.That(protectedPayloadAssetPath, Is.Not.Null.And.Not.Empty);
            Assert.That(tempInstallAssetPath, Is.Not.Null.And.Not.Empty);
            Assert.That(manifestAssetPath, Is.Not.Null.And.Not.Empty);

            PackageMetadata metadata = InvokeExtractMetadataFromInstalledShell(
                metadataAssetPath,
                tempInstallAssetPath,
                protectedPayloadAssetPath,
                Path.GetFileNameWithoutExtension(unityPackagePath));
            Assert.That(metadata, Is.Not.Null);

            string manifestJson = File.ReadAllText(GetProjectDiskPath(manifestAssetPath));
            var manifest = YUCP.Importer.Editor.PackageVerifier.Core.PackageManifestJson.ParseManifest(manifestJson);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.packageId, Is.Not.Null.And.Not.Empty);

            InstalledPackageInfo packageInfo = InvokeInstalledPackageInfoFactoryCreate(
                metadata,
                manifest.packageId,
                manifest.archiveSha256,
                manifest.publisherId,
                importedPaths);
            Assert.That(packageInfo, Is.Not.Null);

            TrackCreatedPaths(importedPaths);

            return new RealProtectedShellContext
            {
                PackageInfo = packageInfo,
                ImportedPaths = importedPaths,
            };
        }

        private void AssertProtectedMaterializationRuntimeReady(InstalledPackageInfo packageInfo)
        {
            Assert.That(packageInfo, Is.Not.Null);

            string cachedLicenseToken = InvokeLicenseTokenCacheGetValidToken(packageInfo.packageId);
            if (string.IsNullOrWhiteSpace(cachedLicenseToken))
            {
                Assert.Inconclusive($"No cached YUCP license token was available for package '{packageInfo.packageId}'.");
            }

            if (!InvokeTryValidateProtectedMaterializationRuntime(out string runtimeError))
            {
                Assert.Inconclusive(runtimeError);
            }
        }

        private IReadOnlyList<string> ExpandUnityPackageShellIntoProject(string unityPackagePath)
        {
            string extractRoot = Path.Combine(
                Path.GetTempPath(),
                "YUCP-ProtectedFinalizationTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extractRoot);

            try
            {
                ExtractUnityPackage(unityPackagePath, extractRoot);

                var importedPaths = new List<string>();
                foreach (string entryDirectory in Directory.GetDirectories(extractRoot))
                {
                    string pathnameFile = Path.Combine(entryDirectory, "pathname");
                    if (!File.Exists(pathnameFile))
                    {
                        continue;
                    }

                    string logicalPath = NormalizeUnityPath(File.ReadAllText(pathnameFile));
                    if (string.IsNullOrWhiteSpace(logicalPath) || !ShouldImportProtectedShellPath(logicalPath))
                    {
                        continue;
                    }

                    string assetFile = Path.Combine(entryDirectory, "asset");
                    string assetMetaFile = Path.Combine(entryDirectory, "asset.meta");
                    bool hasAssetFile = File.Exists(assetFile);
                    bool hasAssetMetaFile = File.Exists(assetMetaFile);
                    if (!hasAssetFile && !hasAssetMetaFile)
                    {
                        continue;
                    }

                    string destinationDiskPath = GetProjectDiskPath(logicalPath);
                    string destinationDirectory = Path.GetDirectoryName(destinationDiskPath);
                    if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    if (hasAssetFile)
                    {
                        File.Copy(assetFile, destinationDiskPath, true);
                    }

                    if (hasAssetMetaFile)
                    {
                        File.Copy(assetMetaFile, destinationDiskPath + ".meta", true);
                    }

                    importedPaths.Add(logicalPath);
                    TrackCreatedRoot(logicalPath);
                }

                AssetDatabase.Refresh();
                return importedPaths
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            finally
            {
                if (Directory.Exists(extractRoot))
                {
                    Directory.Delete(extractRoot, true);
                }
            }
        }

        private static bool ShouldImportProtectedShellPath(string logicalPath)
        {
            string normalizedPath = NormalizeUnityPath(logicalPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return false;
            }

            if (normalizedPath.StartsWith("Packages/yucp.installed-packages/Editor/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (normalizedPath.Equals("Packages/yucp.installed-packages/package.json", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (normalizedPath.StartsWith("Packages/yucp.packageguardian/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static string NormalizeUnityPath(string path)
        {
            return path?.Replace('\\', '/').Trim();
        }

        private static void ExtractUnityPackage(string unityPackagePath, string extractRoot)
        {
            Type tarArchiveType = Type.GetType("ICSharpCode.SharpZipLib.Tar.TarArchive, ICSharpCode.SharpZipLib", false);
            Type gzipInputStreamType = Type.GetType("ICSharpCode.SharpZipLib.GZip.GZipInputStream, ICSharpCode.SharpZipLib", false);

            Assert.That(tarArchiveType, Is.Not.Null, "Expected SharpZipLib TarArchive to be available.");
            Assert.That(gzipInputStreamType, Is.Not.Null, "Expected SharpZipLib GZipInputStream to be available.");

            ConstructorInfo gzipConstructor = gzipInputStreamType.GetConstructor(new[] { typeof(Stream) });
            MethodInfo createInputMethod = tarArchiveType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method =>
                {
                    if (!string.Equals(method.Name, "CreateInputTarArchive", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 1)
                    {
                        return typeof(Stream).IsAssignableFrom(parameters[0].ParameterType);
                    }

                    if (parameters.Length == 2)
                    {
                        return typeof(Stream).IsAssignableFrom(parameters[0].ParameterType);
                    }

                    return false;
                });
            MethodInfo extractContentsMethod = tarArchiveType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                {
                    if (!string.Equals(method.Name, "ExtractContents", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType == typeof(string);
                });

            Assert.That(gzipConstructor, Is.Not.Null);
            Assert.That(createInputMethod, Is.Not.Null);
            Assert.That(extractContentsMethod, Is.Not.Null);

            using var fileStream = File.OpenRead(unityPackagePath);
            object gzipStream = gzipConstructor.Invoke(new object[] { fileStream });
            Assert.That(gzipStream, Is.Not.Null);

            ParameterInfo[] createInputParameters = createInputMethod.GetParameters();
            object[] createInputArgs = createInputParameters.Length == 1
                ? new object[] { gzipStream }
                : new object[] { gzipStream, System.Text.Encoding.UTF8 };

            object tarArchive = createInputMethod.Invoke(null, createInputArgs);
            Assert.That(tarArchive, Is.Not.Null);

            try
            {
                extractContentsMethod.Invoke(tarArchive, new object[] { extractRoot });
            }
            finally
            {
                (tarArchive as IDisposable)?.Dispose();
                (gzipStream as IDisposable)?.Dispose();
            }
        }

        private static PackageMetadata InvokeExtractMetadataFromInstalledShell(
            string metadataAssetPath,
            string tempInstallAssetPath,
            string protectedPayloadAssetPath,
            string fallbackPackageName)
        {
            Type extractorType = typeof(InstalledPackageInfo).Assembly.GetType(
                "YUCP.Importer.Editor.PackageManager.PackageMetadataExtractor",
                false);
            Assert.That(extractorType, Is.Not.Null);

            MethodInfo method = extractorType.GetMethod(
                "ExtractMetadataFromInstalledShell",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            return method.Invoke(
                null,
                new object[]
                {
                    metadataAssetPath,
                    tempInstallAssetPath,
                    protectedPayloadAssetPath,
                    fallbackPackageName,
                }) as PackageMetadata;
        }

        private static InstalledPackageInfo InvokeInstalledPackageInfoFactoryCreate(
            PackageMetadata metadata,
            string packageId,
            string archiveSha256,
            string publisherId,
            IEnumerable<string> installedFiles)
        {
            Type factoryType = typeof(InstalledPackageInfo).Assembly.GetType(
                "YUCP.Importer.Editor.PackageManager.InstalledPackageInfoFactory",
                false);
            Assert.That(factoryType, Is.Not.Null);

            MethodInfo method = factoryType.GetMethod(
                "Create",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            return method.Invoke(
                null,
                new object[]
                {
                    metadata,
                    packageId,
                    archiveSha256,
                    publisherId,
                    true,
                    installedFiles?.ToArray() ?? Array.Empty<string>(),
                }) as InstalledPackageInfo;
        }

        private static string InvokeLicenseTokenCacheGetValidToken(string packageId)
        {
            Type cacheType = typeof(InstalledPackageInfo).Assembly.GetType(
                "YUCP.Importer.Editor.PackageManager.Core.LicenseTokenCache",
                false);
            Assert.That(cacheType, Is.Not.Null);

            MethodInfo method = cacheType.GetMethod(
                "GetValidToken",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            return method.Invoke(null, new object[] { packageId }) as string;
        }

        private static bool InvokeTryValidateProtectedMaterializationRuntime(out string error)
        {
            Type shimType = typeof(InstalledPackageInfo).Assembly.GetType(
                "YUCP.Importer.Editor.PackageManager.Core.CouplingRuntimeShimService",
                false);
            Assert.That(shimType, Is.Not.Null);

            MethodInfo method = shimType.GetMethod(
                "TryValidateProtectedMaterializationRuntime",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args = { null };
            bool success = method.Invoke(null, args) is bool result && result;
            error = args[0] as string;
            return success;
        }

        private void TrackCreatedPaths(IEnumerable<string> assetPaths)
        {
            if (assetPaths == null)
            {
                return;
            }

            foreach (string assetPath in assetPaths)
            {
                if (!string.IsNullOrWhiteSpace(assetPath))
                {
                    TrackCreatedRoot(assetPath);
                }
            }
        }

        private void RegisterTrackedPackage(InstalledPackageInfo packageInfo)
        {
            Assert.That(packageInfo, Is.Not.Null);

            var registry = InstalledPackageRegistry.GetOrCreate();
            registry.RegisterPackage(packageInfo);
            _registeredPackageIds.Add(packageInfo.packageId);
        }

        private void UnregisterTrackedPackages()
        {
            if (_registeredPackageIds.Count == 0)
            {
                return;
            }

            var registry = InstalledPackageRegistry.Load() ?? InstalledPackageRegistry.GetOrCreate();
            foreach (string packageId in _registeredPackageIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            {
                registry.UnregisterPackage(packageId);
            }

            _registeredPackageIds.Clear();
            AssetDatabase.Refresh();
        }

        private static void InvokeQueuePendingFinalization(InstalledPackageInfo packageInfo, IReadOnlyList<string> extractedAssetPaths)
        {
            MethodInfo method = GetCoordinatorType().GetMethod(
                "QueuePendingFinalization",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { packageInfo, extractedAssetPaths ?? Array.Empty<string>() });
        }

        private static void InvokeTryConsumePendingFinalization()
        {
            MethodInfo method = GetCoordinatorType().GetMethod(
                "TryConsumePendingFinalization",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, null);
        }

        private static bool HasPendingFinalizationState()
        {
            return !string.IsNullOrWhiteSpace(EditorPrefs.GetString("YUCP.PackageManager.ProtectedPayload.PendingFinalization", string.Empty));
        }

        private static void ClearPendingFinalizationState()
        {
            EditorPrefs.DeleteKey("YUCP.PackageManager.ProtectedPayload.PendingFinalization");
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
            string root = normalizedPath.StartsWith("Packages/yucp.installed-packages/", StringComparison.OrdinalIgnoreCase)
                ? normalizedPath.Equals("Packages/yucp.installed-packages/package.json", StringComparison.OrdinalIgnoreCase)
                    ? normalizedPath
                    : normalizedPath.StartsWith("Packages/yucp.installed-packages/Editor/", StringComparison.OrdinalIgnoreCase)
                        ? "Packages/yucp.installed-packages/Editor"
                        : segments.Length >= 3
                            ? string.Join("/", segments.Take(3))
                            : null
                : normalizedPath.StartsWith("Packages/com.yucp.temp/", StringComparison.OrdinalIgnoreCase)
                    ? "Packages/com.yucp.temp"
                : normalizedPath.StartsWith("Assets/Novaspil_Kitbash/", StringComparison.OrdinalIgnoreCase)
                        ? "Assets/Novaspil_Kitbash"
                        : normalizedPath.StartsWith("Assets/_Signing/", StringComparison.OrdinalIgnoreCase)
                            ? "Assets/_Signing"
                            : normalizedPath.StartsWith("Packages/yucp.packageguardian/", StringComparison.OrdinalIgnoreCase)
                                ? "Packages/yucp.packageguardian"
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
            if (method == null)
            {
                ProtectedInstallFinalizationCoordinatorTestHooks.TryRollbackImportedAssets = null;
                return;
            }

            ProtectedInstallFinalizationCoordinatorTestHooks.TryRollbackImportedAssets = assetPaths =>
            {
                object[] args = { assetPaths, null };
                bool success = (bool)method.Invoke(null, args);
                return (success, args[1] as string);
            };
        }

        private static void SetReleaseRuntimeResourcesOverride(MethodInfo method)
        {
            if (method == null)
            {
                ProtectedInstallFinalizationCoordinatorTestHooks.TryReleaseRuntimeResources = null;
                return;
            }

            ProtectedInstallFinalizationCoordinatorTestHooks.TryReleaseRuntimeResources = () => (bool)method.Invoke(null, null);
        }

        private static void SetTryApplyCouplingOverride(MethodInfo method)
        {
            if (method == null)
            {
                CouplingImportGuardTestHooks.TryApplyCoupling = null;
                return;
            }

            CouplingImportGuardTestHooks.TryApplyCoupling = (packageId, installedFiles) =>
            {
                object[] args = { packageId, installedFiles, null };
                bool success = (bool)method.Invoke(null, args);
                return (success, args[2] as string);
            };
        }

        private static void SetProtectedPayloadBrokerBridgeOverride(object bridge)
        {
            ProtectedInstallFinalizationCoordinatorTestHooks.BrokerBridgeOverride =
                bridge as YUCP.Importer.Editor.PackageManager.Core.IProtectedPayloadBrokerBridge;
        }

        private static void SetTryMaterializePatchAssetsOverride(MethodInfo method)
        {
            if (method == null)
            {
                ProtectedInstallFinalizationCoordinatorTestHooks.TryMaterializePatchAssets = null;
                return;
            }

            ProtectedInstallFinalizationCoordinatorTestHooks.TryMaterializePatchAssets = patchAssetPaths =>
            {
                object[] args = { patchAssetPaths, null, null };
                bool success = (bool)method.Invoke(null, args);
                return (success, args[1] as IReadOnlyList<string>, args[2] as string);
            };
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
