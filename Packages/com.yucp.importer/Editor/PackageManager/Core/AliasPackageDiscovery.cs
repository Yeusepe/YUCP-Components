using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.PackageManager;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class AliasPackageDiscovery
    {
        internal const string ServerAuthorizedInstallStrategy = "server-authorized";

        internal static bool TryBuildMetadata(
            string packageId,
            string packageJson,
            out PackageMetadata metadata,
            out string error)
        {
            metadata = null;
            error = null;

            try
            {
                PackageMetadataExtractor.PackageJsonImportData importData =
                    PackageMetadataExtractor.ParsePackageJsonImportDataStrict(packageJson);
                if (importData == null)
                {
                    error = "package.json could not be parsed.";
                    return false;
                }

                AliasPackageContract alias = importData.aliasPackage;
                if (!IsServerAuthorized(alias))
                {
                    error = "package.json does not declare a server-authorized YUCP alias.";
                    return false;
                }
                if (alias.catalogProductIds == null || alias.catalogProductIds.Count == 0)
                {
                    error = "Alias package metadata has no catalog product identifiers.";
                    return false;
                }

                string resolvedPackageId = !string.IsNullOrWhiteSpace(importData.packageName)
                    ? importData.packageName
                    : packageId;
                string displayName = !string.IsNullOrWhiteSpace(alias.packageDisplayName)
                    ? alias.packageDisplayName
                    : importData.displayName;
                metadata = new PackageMetadata(
                    !string.IsNullOrWhiteSpace(displayName) ? displayName : resolvedPackageId)
                {
                    version = importData.version ?? string.Empty,
                    author = importData.author ?? string.Empty,
                    description = importData.description ?? string.Empty,
                    dependencies = importData.dependencies ??
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    aliasPackage = alias.Clone(),
                };
                metadata.aliasPackage.packageName =
                    !string.IsNullOrWhiteSpace(metadata.aliasPackage.packageName)
                        ? metadata.aliasPackage.packageName
                        : resolvedPackageId;
                metadata.aliasPackage.packageDisplayName =
                    !string.IsNullOrWhiteSpace(metadata.aliasPackage.packageDisplayName)
                        ? metadata.aliasPackage.packageDisplayName
                        : metadata.packageName;
                metadata.aliasPackage.packageVersion =
                    !string.IsNullOrWhiteSpace(metadata.aliasPackage.packageVersion)
                        ? metadata.aliasPackage.packageVersion
                        : metadata.version;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        internal static bool IsServerAuthorized(AliasPackageContract alias)
        {
            return alias != null &&
                string.Equals(alias.kind, "alias-v1", StringComparison.Ordinal) &&
                string.Equals(
                    alias.installStrategy,
                    ServerAuthorizedInstallStrategy,
                    StringComparison.Ordinal) &&
                string.Equals(
                    alias.importerPackage,
                    "com.yucp.importer",
                    StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(alias.aliasId);
        }

        internal static bool AnyRegistered()
        {
            foreach (PackageInfo package in PackageInfo.GetAllRegisteredPackages())
            {
                if (package == null || string.IsNullOrWhiteSpace(package.resolvedPath))
                {
                    continue;
                }
                string packageJsonPath = Path.Combine(package.resolvedPath, "package.json");
                if (!File.Exists(packageJsonPath))
                {
                    continue;
                }
                try
                {
                    if (TryBuildMetadata(
                        package.name,
                        File.ReadAllText(packageJsonPath),
                        out _,
                        out _))
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
            return false;
        }

        internal static AliasPackageContract FindByAliasId(string aliasId)
        {
            if (string.IsNullOrWhiteSpace(aliasId))
            {
                throw new ArgumentException(
                    "The package alias identifier is required.",
                    nameof(aliasId));
            }
            AliasPackageContract match = null;
            foreach (PackageInfo package in PackageInfo.GetAllRegisteredPackages())
            {
                if (package == null || string.IsNullOrWhiteSpace(package.resolvedPath))
                {
                    continue;
                }
                string packageJsonPath = Path.Combine(package.resolvedPath, "package.json");
                if (!File.Exists(packageJsonPath) ||
                    !TryBuildMetadata(
                        package.name,
                        File.ReadAllText(packageJsonPath),
                        out PackageMetadata metadata,
                        out _) ||
                    !string.Equals(
                        metadata.aliasPackage.aliasId,
                        aliasId,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (match != null)
                {
                    throw new InvalidDataException(
                        "Multiple registered packages use the same package alias.");
                }
                match = metadata.aliasPackage.Clone();
            }
            return match ?? throw new InvalidOperationException(
                "The requested package alias is not installed through VPM.");
        }
    }
}
