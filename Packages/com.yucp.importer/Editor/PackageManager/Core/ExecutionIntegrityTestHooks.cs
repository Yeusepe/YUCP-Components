#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class VerificationIntentServiceTestHooks
    {
        internal static Action<string> OpenUrlHandler;

        internal static void Reset()
        {
            OpenUrlHandler = null;
        }
    }

    internal static class ProtectedAssetUnlockServiceTestHooks
    {
        internal static Func<string, InstalledPackageInfo> InstalledPackageResolver;

        internal static void Reset()
        {
            InstalledPackageResolver = null;
        }
    }

    internal static class CouplingImportGuardTestHooks
    {
        internal static Func<string, IReadOnlyList<string>, (bool success, string error)> TryApplyCoupling;

        internal static void Reset()
        {
            TryApplyCoupling = null;
        }
    }

    internal static class CouplingRuntimeBootstrapServiceTestHooks
    {
        internal static Func<string, string> GetLicenseToken;
        internal static Func<string> GetProjectId;
        internal static Func<string> GetMachineFingerprint;
        internal static Func<string> GetServerUrl;
        internal static Func<(bool success, string error)> ValidateRuntime;
        internal static Func<(bool success, string error)> RepairRuntimeRegistration;
        internal static Func<string, string, string, string, (bool success, string runtimePackageToken, string runtimePackageSha256, long expiresAt, string error)> RequestRuntimePackageToken;
        internal static Func<string, string, string, (bool success, byte[] packageZipBytes, string error)> DownloadRuntimePackage;
        internal static Func<byte[], string, string, (bool success, string error)> InstallRuntimePackage;

        internal static void Reset()
        {
            GetLicenseToken = null;
            GetProjectId = null;
            GetMachineFingerprint = null;
            GetServerUrl = null;
            ValidateRuntime = null;
            RepairRuntimeRegistration = null;
            RequestRuntimePackageToken = null;
            DownloadRuntimePackage = null;
            InstallRuntimePackage = null;
        }
    }

    internal static class ServerAuthorizedPackageDownloadBridgeTestHooks
    {
        internal static Func<string, string> GetLicenseToken;
        internal static Func<string> GetMachineFingerprint;

        internal static void Reset()
        {
            GetLicenseToken = null;
            GetMachineFingerprint = null;
        }
    }

    internal static class ProtectedInstallFinalizationCoordinatorTestHooks
    {
        internal static Func<IReadOnlyList<string>, (bool success, IReadOnlyList<string> createdAssetPaths, string error)> TryMaterializePatchAssets;
        internal static Func<bool> TryReleaseRuntimeResources;
        internal static Func<IReadOnlyList<string>, (bool success, string error)> TryRollbackImportedAssets;
        internal static IProtectedPayloadBrokerBridge BrokerBridgeOverride;

        internal static void Reset()
        {
            TryMaterializePatchAssets = null;
            TryReleaseRuntimeResources = null;
            TryRollbackImportedAssets = null;
            BrokerBridgeOverride = null;
        }
    }

    internal static class UpdateDeliveryServiceTestHooks
    {
        internal static Action<UpdateDeliveryService.AliasInstallPlan> ApplyAuthorizedInstallPlanHandler;
        internal static Func<
            string,
            string,
            UpdateDeliveryService.AliasInstallPlanPackage,
            string,
            AuthorizedVpmPackageInstaller.AuthorizedPackageInstallResult> AuthorizedPackageInstallerHandler;
        internal static Func<string, PackageMetadata> InstalledPackageMetadataLoader;
        internal static Action<InstalledPackageInfo> PersistInstallStateHandler;
        internal static Action<InstalledPackageInfo> RegisterInstalledPackageHandler;

        internal static void Reset()
        {
            ApplyAuthorizedInstallPlanHandler = null;
            AuthorizedPackageInstallerHandler = null;
            InstalledPackageMetadataLoader = null;
            PersistInstallStateHandler = null;
            RegisterInstalledPackageHandler = null;
        }
    }

}
#endif
