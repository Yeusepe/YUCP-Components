using System;
using System.Collections.Generic;
using System.Linq;

namespace YUCP.Importer.Editor.PackageManager
{
    internal static class InstalledPackageInfoFactory
    {
        internal static InstalledPackageInfo Create(
            PackageMetadata metadata,
            string packageId,
            string archiveSha256,
            string publisherId,
            bool isVerified,
            IEnumerable<string> installedFiles)
        {
            metadata ??= new PackageMetadata();

            var packageInfo = new InstalledPackageInfo
            {
                packageName = metadata.packageName ?? string.Empty,
                version = metadata.version ?? string.Empty,
                author = metadata.author ?? string.Empty,
                description = metadata.description ?? string.Empty,
                icon = metadata.icon,
                banner = metadata.banner,
                productLinks = CloneProductLinks(metadata.productLinks),
                dependencies = metadata.dependencies != null
                    ? new Dictionary<string, string>(metadata.dependencies)
                    : new Dictionary<string, string>(),
                versionRule = metadata.versionRule ?? "semver",
                versionRuleName = metadata.versionRuleName ?? metadata.versionRule ?? "semver",
                licensePackages = CloneLicensePackages(metadata.licensePackages),
                tagline = metadata.tagline ?? string.Empty,
                category = metadata.category ?? string.Empty,
                supportedPlatforms = metadata.supportedPlatforms != null
                    ? new List<string>(metadata.supportedPlatforms)
                    : new List<string>(),
                minimumUnityVersion = metadata.minimumUnityVersion ?? string.Empty,
                creatorNote = metadata.creatorNote ?? string.Empty,
                releaseNotes = metadata.releaseNotes ?? string.Empty,
                galleryImages = metadata.galleryImages != null
                    ? new List<UnityEngine.Texture2D>(metadata.galleryImages)
                    : new List<UnityEngine.Texture2D>(),
                tags = metadata.tags != null
                    ? new List<string>(metadata.tags)
                    : new List<string>(),
                totalFileCount = metadata.totalFileCount,
                totalFileSize = metadata.totalFileSize,
                assetBreakdown = CloneAssetBreakdown(metadata.assetBreakdown),
                exportDate = metadata.exportDate ?? string.Empty,
                fileHashes = CloneFileHashes(metadata.fileHashes),
                aliasPackage = metadata.aliasPackage?.Clone(),
                packageId = packageId ?? string.Empty,
                archiveSha256 = archiveSha256 ?? string.Empty,
                installedVersion = metadata.version ?? string.Empty,
                isVerified = isVerified,
                publisherId = publisherId ?? string.Empty,
                installedFiles = installedFiles?
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>(),
            };

            packageInfo.SetInstalledDateTime(DateTime.Now);
            return packageInfo;
        }

        private static List<ProductLink> CloneProductLinks(List<ProductLink> links)
        {
            if (links == null || links.Count == 0)
                return new List<ProductLink>();

            return links.Select(link => new ProductLink(link?.url ?? string.Empty, link?.label ?? string.Empty)
            {
                customIcon = link?.customIcon
            }).ToList();
        }

        private static List<LicensePackageRequirement> CloneLicensePackages(List<LicensePackageRequirement> requirements)
        {
            if (requirements == null || requirements.Count == 0)
                return new List<LicensePackageRequirement>();

            return requirements.Select(req => new LicensePackageRequirement
            {
                packageId = req?.packageId ?? string.Empty,
                packageName = req?.packageName ?? string.Empty,
                productId = req?.productId ?? string.Empty,
                gumroadPermalink = req?.gumroadPermalink ?? string.Empty,
                jinxxyProductId = req?.jinxxyProductId ?? string.Empty,
                discordGuildId = req?.discordGuildId ?? string.Empty,
                discordRoleId = req?.discordRoleId ?? string.Empty,
                creatorAuthUserId = req?.creatorAuthUserId ?? string.Empty,
            }).ToList();
        }

        private static List<AssetBreakdownEntry> CloneAssetBreakdown(List<AssetBreakdownEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return new List<AssetBreakdownEntry>();

            return entries.Select(entry => new AssetBreakdownEntry(entry?.type ?? string.Empty, entry?.count ?? 0)).ToList();
        }

        private static List<PackageFileHashEntry> CloneFileHashes(List<PackageFileHashEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return new List<PackageFileHashEntry>();

            return entries.Select(entry => new PackageFileHashEntry
            {
                path = entry?.path ?? string.Empty,
                hash = entry?.hash ?? string.Empty,
            }).ToList();
        }
    }
}
