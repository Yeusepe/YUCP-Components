using System.Collections.Generic;
using System.Linq;
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

        internal static bool ShouldApplyDuringShellImport(InstalledPackageInfo packageInfo)
        {
            return packageInfo?.protectedPayload == null;
        }

        internal static IReadOnlyList<string> BuildProtectedPayloadCouplingFiles(
            InstalledPackageInfo packageInfo,
            IReadOnlyList<string> extractedAssetPaths)
        {
            return (packageInfo?.installedFiles ?? new List<string>())
                .Concat(extractedAssetPaths ?? new List<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Replace('\\', '/').Trim())
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static bool TryApplyDeferredProtectedPayloadCouplingOrRollback(
            InstalledPackageInfo packageInfo,
            IReadOnlyList<string> extractedAssetPaths,
            out string error)
        {
            if (packageInfo == null)
            {
                error = "The package protection step could not be completed.";
                return false;
            }

            packageInfo.installedFiles = BuildProtectedPayloadCouplingFiles(packageInfo, extractedAssetPaths).ToList();
            return TryApplyCouplingOrRollback(packageInfo, out error);
        }

        internal static bool TryApplyCouplingOrRollback(InstalledPackageInfo packageInfo, out string error)
        {
            error = null;
            if (packageInfo == null)
            {
                error = "The package protection step could not be completed.";
                return false;
            }

            if (TryApplyCoupling(packageInfo.packageId, packageInfo.installedFiles, out string couplingError))
            {
                return true;
            }

            if (!ImportedAssetRollbackService.TryRollbackPackage(packageInfo, out string rollbackError))
            {
                error = "The package protection step could not be completed and the import could not be rolled back cleanly.";
                return false;
            }

            error = "The package protection step could not be completed on this machine.";
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
