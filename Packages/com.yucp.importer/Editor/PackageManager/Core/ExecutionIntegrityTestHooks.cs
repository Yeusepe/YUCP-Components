#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    public static class VerificationIntentServiceTestHooks
    {
        public static Action<string> OpenUrlHandler;

        public static void Reset()
        {
            OpenUrlHandler = null;
        }
    }

    public static class ProtectedAssetUnlockServiceTestHooks
    {
        public static Func<string, InstalledPackageInfo> InstalledPackageResolver;

        public static void Reset()
        {
            InstalledPackageResolver = null;
        }
    }

    public static class UpdateDeliveryServiceTestHooks
    {
        public static Action<UpdateDeliveryService.AliasInstallPlan> ApplyAuthorizedInstallPlanHandler;
        public static Func<string, PackageMetadata> InstalledPackageMetadataLoader;
        public static Action<InstalledPackageInfo> PersistInstallStateHandler;
        public static Action<InstalledPackageInfo> RegisterInstalledPackageHandler;

        public static void Reset()
        {
            ApplyAuthorizedInstallPlanHandler = null;
            InstalledPackageMetadataLoader = null;
            PersistInstallStateHandler = null;
            RegisterInstalledPackageHandler = null;
        }
    }

}
#endif
