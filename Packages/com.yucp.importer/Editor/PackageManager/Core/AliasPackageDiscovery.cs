using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor.PackageManager;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class AliasPackageDiscovery
    {
        internal const string ServerAuthorizedInstallStrategy = "server-authorized";
        private static readonly Regex Sha256Pattern = new Regex(
            "^[a-f0-9]{64}$",
            RegexOptions.CultureInvariant);
        private static readonly Regex SafeIdentifierPattern = new Regex(
            "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$",
            RegexOptions.CultureInvariant);
        private static readonly Regex SignaturePattern = new Regex(
            "^[A-Za-z0-9_-]{86}$",
            RegexOptions.CultureInvariant);
        private static readonly Regex StrictSemverPattern = new Regex(
            "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)" +
            "(?:-((?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)" +
            "(?:\\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?" +
            "(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
            RegexOptions.CultureInvariant);

        internal static bool TryBuildMetadata(
            string packageId,
            string packageJson,
            out PackageMetadata metadata,
            out string error)
        {
            return TryBuildMetadata(
                packageId,
                packageJson,
                null,
                out metadata,
                out error);
        }

        internal static bool TryBuildMetadata(
            string packageId,
            string packageJson,
            string packageRoot,
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
                    error = "This package file couldn’t be read.";
                    return false;
                }

                AliasPackageContract alias = importData.aliasPackage;
                if (!IsServerAuthorized(alias))
                {
                    error = "package.json does not declare a server-authorized YUCP alias.";
                    return false;
                }
                string resolvedPackageId = !string.IsNullOrWhiteSpace(importData.packageName)
                    ? importData.packageName
                    : packageId;
                PackageMetadata embedded =
                    PackageMetadataExtractor.ParseEmbeddedAliasMetadataJson(
                        importData.packageMetadataJson);
                string displayName = FirstNonEmpty(
                    embedded?.packageName,
                    alias.packageDisplayName,
                    importData.displayName,
                    resolvedPackageId);
                metadata = embedded ?? new PackageMetadata();
                metadata.packageName = displayName;
                metadata.version = FirstNonEmpty(metadata.version);
                metadata.author = FirstNonEmpty(
                    metadata.author,
                    importData.author);
                metadata.description = FirstNonEmpty(
                    metadata.description,
                    importData.description);
                metadata.dependencies = importData.dependencies ??
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);
                metadata.aliasPackage = alias.Clone();
                metadata.aliasPackage.packageName =
                    FirstNonEmpty(
                        metadata.aliasPackage.packageName,
                        resolvedPackageId);
                metadata.aliasPackage.packageDisplayName =
                    FirstNonEmpty(
                        metadata.aliasPackage.packageDisplayName,
                        metadata.packageName);
                metadata.aliasPackage.packageVersion =
                    FirstNonEmpty(
                        metadata.aliasPackage.packageVersion,
                        metadata.version);
                if (!string.IsNullOrWhiteSpace(packageRoot))
                {
                    AliasPackageMediaLoader.Apply(
                        metadata,
                        metadata.aliasPackage,
                        packageRoot);
                }
                return true;
            }
            catch (Exception exception)
            {
                PackageMetadataMediaOwnership.Release(metadata);
                metadata = null;
                error = exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Whether a required package comes from VRChat, VRCFury or YUCP rather
        /// than from a community creator. The installer fetches both; this only
        /// tells a buyer who stands behind each one.
        /// </summary>
        internal static bool IsOfficialDependencySource(string packageId)
        {
            string id = packageId?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }
            return id.StartsWith("com.vrchat.", StringComparison.Ordinal) ||
                id.StartsWith("com.vrcfury.", StringComparison.Ordinal) ||
                id.StartsWith("com.yucp.", StringComparison.Ordinal);
        }

        internal static bool IsServerAuthorized(AliasPackageContract alias)
        {
            return alias != null &&
                (string.Equals(alias.kind, "alias-v1", StringComparison.Ordinal) ||
                    (string.Equals(
                            alias.kind,
                            "alias-v2",
                            StringComparison.Ordinal) &&
                        IsValidBootstrapIntent(alias.bootstrapIntent))) &&
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

        private static bool IsValidBootstrapIntent(
            BootstrapIntentContract intent)
        {
            if (intent == null ||
                intent.schemaVersion != 1 ||
                !IsUuidV4(intent.intentId) ||
                intent.issuedAt <= 0 ||
                !SafeIdentifierPattern.IsMatch(intent.keyId ?? string.Empty) ||
                !SafeIdentifierPattern.IsMatch(intent.editionId ?? string.Empty) ||
                !SignaturePattern.IsMatch(intent.signature ?? string.Empty))
            {
                return false;
            }
            if (string.Equals(intent.mode, "latest", StringComparison.Ordinal))
            {
                return string.IsNullOrEmpty(intent.version) &&
                    string.IsNullOrEmpty(intent.versionId) &&
                    string.IsNullOrEmpty(intent.releaseRoot);
            }
            return string.Equals(intent.mode, "specific", StringComparison.Ordinal) &&
                StrictSemverPattern.IsMatch(intent.version ?? string.Empty) &&
                SafeIdentifierPattern.IsMatch(intent.versionId ?? string.Empty) &&
                Sha256Pattern.IsMatch(intent.releaseRoot ?? string.Empty);
        }

        private static bool IsUuidV4(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                value.Length == 36 &&
                value[14] == '4' &&
                "89abAB".IndexOf(value[19]) >= 0 &&
                Guid.TryParseExact(value, "D", out _);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(
                    value => !string.IsNullOrWhiteSpace(value))
                ?.Trim() ?? string.Empty;
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
                if (TryReadPackageJson(
                    packageJsonPath,
                    out string packageJson) &&
                    TryBuildMetadata(
                        package.name,
                        packageJson,
                        out _,
                        out _))
                {
                    return true;
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
                if (!TryReadPackageJson(
                        packageJsonPath,
                        out string packageJson) ||
                    !TryBuildMetadata(
                        package.name,
                        packageJson,
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

        private static bool TryReadPackageJson(
            string path,
            out string packageJson)
        {
            packageJson = string.Empty;
            try
            {
                packageJson = File.ReadAllText(path);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
