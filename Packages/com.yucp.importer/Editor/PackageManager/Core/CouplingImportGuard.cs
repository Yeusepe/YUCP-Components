using System.Collections.Generic;
using YUCP.Importer.Editor.PackageManager;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal delegate bool TryApplyCouplingHandler(
        string packageId,
        IReadOnlyList<string> installedFiles,
        out string error);

    internal static class CouplingImportGuard
    {
        internal static TryApplyCouplingHandler s_tryApplyCouplingOverride;

        internal static bool TryApplyCouplingOrRollback(InstalledPackageInfo packageInfo, out string error)
        {
            error = null;
            if (packageInfo == null)
            {
                error = "Installed package information was missing.";
                return false;
            }

            if (TryApplyCoupling(packageInfo.packageId, packageInfo.installedFiles, out string couplingError))
            {
                return true;
            }

            if (!ImportedAssetRollbackService.TryRollbackPackage(packageInfo, out string rollbackError))
            {
                error = string.IsNullOrWhiteSpace(rollbackError)
                    ? couplingError
                    : $"{couplingError}\n\nRollback also failed:\n{rollbackError}";
                return false;
            }

            error = couplingError;
            return false;
        }

        private static bool TryApplyCoupling(
            string packageId,
            IReadOnlyList<string> installedFiles,
            out string error)
        {
            var overrideHandler = s_tryApplyCouplingOverride;
            if (overrideHandler != null)
            {
                return overrideHandler(packageId, installedFiles, out error);
            }

            return CouplingRuntimeService.TryApplyCoupling(packageId, installedFiles, out error);
        }
    }
}
