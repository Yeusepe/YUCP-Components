#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    public static class CouplingRuntimeBootstrapServiceTestHooks
    {
        public static Func<(bool success, string error)> ValidateRuntime;
        public static Func<string, string> GetLicenseToken;
        public static Func<string> GetProjectId;
        public static Func<string> GetMachineFingerprint;
        public static Func<string> GetServerUrl;
        public static Func<(bool success, string error)> RepairRuntimeRegistration;
        public static Func<string, string, string, string, (bool success, string runtimePackageToken, string runtimePackageSha256, long expiresAt, string error)> RequestRuntimePackageToken;
        public static Func<string, string, string, (bool success, byte[] packageZipBytes, string error)> DownloadRuntimePackage;
        public static Func<byte[], string, string, (bool success, string error)> InstallRuntimePackage;

        public static void Reset()
        {
            ValidateRuntime = null;
            GetLicenseToken = null;
            GetProjectId = null;
            GetMachineFingerprint = null;
            GetServerUrl = null;
            RepairRuntimeRegistration = null;
            RequestRuntimePackageToken = null;
            DownloadRuntimePackage = null;
            InstallRuntimePackage = null;
        }
    }

    public static class CouplingImportGuardTestHooks
    {
        public static Func<string, IReadOnlyList<string>, (bool success, string error)> TryApplyCoupling;

        public static void Reset()
        {
            TryApplyCoupling = null;
        }
    }

    public static class ProtectedInstallFinalizationCoordinatorTestHooks
    {
        public static Func<IReadOnlyList<string>, (bool success, IReadOnlyList<string> createdAssetPaths, string error)> TryMaterializePatchAssets;
        public static Func<bool> TryReleaseRuntimeResources;
        public static Func<IReadOnlyList<string>, (bool success, string error)> TryRollbackImportedAssets;
        public static IProtectedPayloadBrokerBridge BrokerBridgeOverride;

        public static void Reset()
        {
            TryMaterializePatchAssets = null;
            TryReleaseRuntimeResources = null;
            TryRollbackImportedAssets = null;
            BrokerBridgeOverride = null;
        }
    }

    public static class VerificationIntentServiceTestHooks
    {
        public static Action<string> OpenUrlHandler;

        public static void Reset()
        {
            OpenUrlHandler = null;
        }
    }
}
#endif
