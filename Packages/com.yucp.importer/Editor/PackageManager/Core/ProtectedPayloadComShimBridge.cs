using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal sealed class ProtectedPayloadComShimBridge : IProtectedPayloadBrokerBridge
    {
        public bool TryFinalizeProtectedInstall(
            InstalledPackageInfo packageInfo,
            out IReadOnlyList<string> materializedAssetPaths,
            out string error,
            out bool pending)
        {
            materializedAssetPaths = Array.Empty<string>();
            error = null;
            pending = false;

            var descriptor = packageInfo?.protectedPayload;
            if (packageInfo == null || descriptor == null)
            {
                error = "The package protection step could not be completed on this machine.";
                return false;
            }

            string[] payloadAssetPaths = (descriptor.payloadAssetPaths ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Replace('\\', '/').Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (payloadAssetPaths.Length == 0)
            {
                error = "The protected package shell is missing signed protected payload file paths.";
                return false;
            }


            if (!CouplingRuntimeBootstrapService.TryEnsureProtectedMaterializationRuntimeReady(packageInfo.packageId, out error))
            {
                return false;
            }

            if (!ProtectedAssetUnlockService.TryIssueMaterializationGrant(
                    packageInfo.packageId,
                    descriptor.protectedAssetId,
                    payloadAssetPaths,
                    out var grant,
                    out error))
            {
                return false;
            }


            string blobDiskPath = ResolveAssetPathToDisk(descriptor.blobAssetPath);
            if (string.IsNullOrWhiteSpace(blobDiskPath) || !File.Exists(blobDiskPath))
            {
                error = "The package protection step could not locate the protected payload blob.";
                return false;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string requestJson = JsonUtility.ToJson(new MaterializeRequest
            {
                grant = grant.grant,
                blobFilePath = blobDiskPath,
                projectRoot = projectRoot,
                licenseServerUrl = LicenseServerResolver.GetLicenseServerUrl(),
                descriptor = descriptor.Clone(),
            });


            if (!CouplingRuntimeShimService.TryMaterialize(requestJson, out var response, out error))
            {
                return false;
            }

            if (response == null || response.materializedAssetPaths == null || response.materializedAssetPaths.Length == 0)
            {
                error = "The package protection step did not produce any protected files.";
                return false;
            }

            materializedAssetPaths = response.materializedAssetPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Replace('\\', '/').Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (!TryValidateNestedProtectedDerivedAssets(packageInfo.packageId, projectRoot, materializedAssetPaths, out string nestedAssetError))
            {
                error = "The package protection step could not be completed on this machine.";
                return false;
            }

            return true;
        }

        private static string ResolveAssetPathToDisk(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            if (Path.IsPathRooted(assetPath))
                return File.Exists(assetPath) ? Path.GetFullPath(assetPath) : null;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static bool TryValidateNestedProtectedDerivedAssets(
            string expectedPackageId,
            string projectRoot,
            IReadOnlyList<string> materializedAssetPaths,
            out string error)
        {
            error = null;
            if (materializedAssetPaths == null || materializedAssetPaths.Count == 0)
                return true;

            foreach (string assetPath in materializedAssetPaths)
            {
                string normalizedPath = assetPath?.Replace('\\', '/').Trim() ?? string.Empty;
                if (!IsDerivedPatchAssetPath(normalizedPath))
                    continue;

                string diskPath = ResolveProjectRelativePath(projectRoot, normalizedPath);
                if (!File.Exists(diskPath))
                {
                    error = $"Nested derived patch asset is missing from disk: {normalizedPath}";
                    return false;
                }

                string yaml = File.ReadAllText(diskPath);
                if (!TryGetYamlScalarValue(yaml, "requiresLicense", out string requiresLicenseValue) ||
                    !TryGetYamlScalarValue(yaml, "licensePackageId", out string licensePackageId) ||
                    !TryGetYamlScalarValue(yaml, "requiresServerUnlock", out string requiresServerUnlockValue) ||
                    !TryGetYamlScalarValue(yaml, "protectedAssetId", out string protectedAssetId))
                {
                    error = $"Nested derived patch asset is missing required protection fields: {normalizedPath}";
                    return false;
                }

                bool requiresLicense = IsTruthyYamlScalar(requiresLicenseValue);
                bool requiresServerUnlock = IsTruthyYamlScalar(requiresServerUnlockValue);
                if (!requiresLicense)
                {
                    error = $"Nested derived patch asset is not purchase-gated: {normalizedPath}";
                    return false;
                }

                if (!string.Equals((licensePackageId ?? string.Empty).Trim(), expectedPackageId ?? string.Empty, StringComparison.Ordinal))
                {
                    error = $"Nested derived patch asset is not purchase-gated: {normalizedPath}";
                    return false;
                }

                if (!requiresServerUnlock)
                {
                    error = $"Nested derived patch asset still allows local key recovery: {normalizedPath}";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(protectedAssetId))
                {
                    error = $"Nested derived patch asset is missing protectedAssetId: {normalizedPath}";
                    return false;
                }
            }

            return true;
        }

        private static bool IsDerivedPatchAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return false;

            return assetPath.StartsWith("Packages/com.yucp.temp/Patches/DerivedFbxAsset_", StringComparison.OrdinalIgnoreCase) &&
                   assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveProjectRelativePath(string projectRoot, string assetPath)
        {
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static bool TryGetYamlScalarValue(string yaml, string key, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(yaml) || string.IsNullOrWhiteSpace(key))
                return false;

            string prefix = key + ":";
            using (var reader = new StringReader(yaml))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.TrimStart();
                    if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
                        continue;

                    value = trimmed.Substring(prefix.Length).Trim().Trim('"');
                    return true;
                }
            }

            return false;
        }

        private static bool IsTruthyYamlScalar(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            return string.Equals(normalized, "1", StringComparison.Ordinal) ||
                   string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase);
        }

        [Serializable]
        private sealed class MaterializeRequest
        {
            public string grant;
            public string blobFilePath;
            public string projectRoot;
            public string licenseServerUrl;
            public ProtectedPayloadDescriptor descriptor;
        }
    }
}
