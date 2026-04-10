#if !YUCP_PACKAGE_MANAGER_DISABLED
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YUCP.Importer.Editor.PackageVerifier.Core;
using YUCP.Importer.Editor.PackageVerifier.Data;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    [InitializeOnLoad]
    internal static class ProtectedImportBootstrapCoordinator
    {
        private const string PendingProtectedImportStateKey = "YUCP.PendingProtectedImportBootstrap";
        private static bool _scheduled;
        [Serializable]
        private sealed class PendingProtectedImportState
        {
            public string packageName;
            public string shellRootAssetPath;
            public string tempInstallAssetPath;
            public string metadataAssetPath;
            public string protectedPayloadAssetPath;
            public string originalPackagePath;
        }

        static ProtectedImportBootstrapCoordinator()
        {
            ScheduleResume();
        }

        private static void ScheduleResume()
        {
            if (_scheduled)
                return;

            _scheduled = true;
            EditorApplication.delayCall += TryResumePendingProtectedImport;
        }

        private static void TryResumePendingProtectedImport()
        {
            _scheduled = false;

            if (!PackageManagerRuntimeSettings.IsEnabled())
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ScheduleResume();
                return;
            }

            PendingProtectedImportState state = LoadState();
            if (state == null)
                return;

            if (!string.IsNullOrWhiteSpace(state.originalPackagePath) && File.Exists(state.originalPackagePath))
            {
                string replayPath = state.originalPackagePath;
                Debug.Log($"[YUCP ProtectedBootstrap] Reopening protected package in YUCP Importer: {replayPath}");
                ClearState();
                EditorApplication.delayCall += () => AssetDatabase.ImportPackage(replayPath, true);
                return;
            }

            if (!TryResumeFromImportedShell(state, out string error))
            {
                Debug.LogError($"[YUCP ProtectedBootstrap] Failed to resume protected package setup: {error}");
                EditorUtility.DisplayDialog(
                    "Protected Package Setup Failed",
                    $"The YUCP Importer was installed, but the protected package could not be resumed automatically.\n\n{error}",
                    "OK");
                ClearState();
                return;
            }

            ClearState();
        }

        private static bool TryResumeFromImportedShell(PendingProtectedImportState state, out string error)
        {
            error = null;
            if (state == null)
            {
                error = "Pending protected import state was missing.";
                return false;
            }

            if (!TryReconstructInstalledPackageInfo(state, out InstalledPackageInfo packageInfo, out error))
                return false;

            if (CouplingImportGuard.ShouldApplyDuringShellImport(packageInfo) &&
                !CouplingImportGuard.TryApplyCouplingOrRollback(packageInfo, out string couplingError))
            {
                error = couplingError;
                return false;
            }

            var registry = InstalledPackageRegistry.GetOrCreate();
            registry.RegisterPackage(packageInfo);
            PackageManagerWindow.ShowResumeProtectedPackage(packageInfo);
            return true;
        }

        private static bool TryReconstructInstalledPackageInfo(
            PendingProtectedImportState state,
            out InstalledPackageInfo packageInfo,
            out string error)
        {
            packageInfo = null;
            error = null;

            if (string.IsNullOrWhiteSpace(state.shellRootAssetPath))
            {
                error = "The imported protected package shell could not be located.";
                return false;
            }

            var metadata = PackageMetadataExtractor.ExtractMetadataFromInstalledShell(
                state.metadataAssetPath,
                state.tempInstallAssetPath,
                state.protectedPayloadAssetPath,
                state.packageName);

            if (!TryLoadManifest(
                    state.shellRootAssetPath,
                    out PackageManifest manifest,
                    out string manifestAssetPath,
                    out string signatureAssetPath,
                    out error))
                return false;
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.packageId))
            {
                error = "The imported protected package shell did not include a usable signed package manifest.";
                return false;
            }

            if (!TryValidateProtectedPayloadManifest(manifest, metadata?.protectedPayload, out error))
                return false;

            List<string> installedFiles = CollectInstalledFiles(
                state.shellRootAssetPath,
                manifestAssetPath,
                signatureAssetPath);
            packageInfo = InstalledPackageInfoFactory.Create(
                metadata,
                manifest.packageId,
                manifest.archiveSha256,
                manifest.publisherId,
                isVerified: false,
                installedFiles: installedFiles);

            return true;
        }

        private static bool TryValidateProtectedPayloadManifest(
            PackageManifest manifest,
            ProtectedPayloadDescriptor descriptor,
            out string error)
        {
            error = null;

            if (descriptor == null)
                return true;
            if (manifest?.protectedPayloads == null || manifest.protectedPayloads.Length == 0)
            {
                error = "The protected package shell is missing signed protected payload metadata.";
                return false;
            }

            ProtectedPayloadManifestEntry matchingEntry = manifest.protectedPayloads.FirstOrDefault(
                entry =>
                    entry != null &&
                    (
                        string.Equals(entry.protectedAssetId ?? string.Empty, descriptor.protectedAssetId ?? string.Empty, StringComparison.Ordinal) ||
                        string.Equals(entry.blobAssetPath ?? string.Empty, descriptor.blobAssetPath ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    ));

            if (matchingEntry == null)
            {
                error = "The protected package shell is missing the signed payload descriptor for this package.";
                return false;
            }

            if (!ProtectedPayloadIntegrityUtility.DescriptorMatchesManifest(descriptor, matchingEntry))
            {
                error = "The protected package shell did not match its signed payload descriptor.";
                return false;
            }
            return true;
        }

        private static bool TryLoadManifest(
            string shellRootAssetPath,
            out PackageManifest manifest,
            out string manifestAssetPath,
            out string signatureAssetPath,
            out string error)
        {
            manifest = null;
            manifestAssetPath = null;
            signatureAssetPath = null;
            error = null;

            string shellRootDiskPath = AssetPathToDiskPath(shellRootAssetPath);
            if (string.IsNullOrWhiteSpace(shellRootDiskPath) || !Directory.Exists(shellRootDiskPath))
            {
                error = $"Protected shell root '{shellRootAssetPath}' does not exist on disk.";
                return false;
            }

            string manifestPath = Directory.GetFiles(shellRootDiskPath, "PackageManifest.json", SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Replace('\\', '/').EndsWith("/_Signing/PackageManifest.json", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                string fallbackManifestAssetPath = "Assets/_Signing/PackageManifest.json";
                string fallbackManifestDiskPath = AssetPathToDiskPath(fallbackManifestAssetPath);
                if (!string.IsNullOrWhiteSpace(fallbackManifestDiskPath) && File.Exists(fallbackManifestDiskPath))
                {
                    manifestPath = fallbackManifestDiskPath;
                    manifestAssetPath = fallbackManifestAssetPath;
                }
            }

            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                error = "Could not find _Signing/PackageManifest.json in the imported protected package shell.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(manifestAssetPath))
            {
                manifestAssetPath = DiskPathToAssetPath(manifestPath);
            }

            string signaturePath = Path.Combine(Path.GetDirectoryName(manifestPath) ?? string.Empty, "PackageManifest.sig");
            if (!string.IsNullOrWhiteSpace(signaturePath) && File.Exists(signaturePath))
            {
                signatureAssetPath = DiskPathToAssetPath(signaturePath);
            }

            try
            {
                manifest = PackageManifestJson.ParseManifest(File.ReadAllText(manifestPath));
                if (manifest == null)
                {
                    error = $"Manifest '{manifestPath}' could not be parsed.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to read manifest '{manifestPath}': {ex.Message}";
                return false;
            }
        }

        private static List<string> CollectInstalledFiles(string shellRootAssetPath, params string[] additionalAssetPaths)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string shellRootDiskPath = AssetPathToDiskPath(shellRootAssetPath);
            if (string.IsNullOrWhiteSpace(shellRootDiskPath) || !Directory.Exists(shellRootDiskPath))
                return result;

            foreach (string diskPath in Directory.GetFiles(shellRootDiskPath, "*", SearchOption.AllDirectories))
            {
                if (diskPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                string assetPath = DiskPathToAssetPath(diskPath);
                if (!string.IsNullOrWhiteSpace(assetPath) && seen.Add(assetPath))
                    result.Add(assetPath);
            }

            foreach (string assetPath in additionalAssetPaths ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(assetPath) ||
                    assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
                    !seen.Add(assetPath))
                {
                    continue;
                }

                result.Add(assetPath);
            }

            return result;
        }

        private static string AssetPathToDiskPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string DiskPathToAssetPath(string diskPath)
        {
            if (string.IsNullOrWhiteSpace(diskPath))
                return null;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(diskPath);
            if (!fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                return null;

            return fullPath.Substring(projectRoot.Length).Replace('\\', '/');
        }

        private static PendingProtectedImportState LoadState()
        {
            string json = EditorPrefs.GetString(PendingProtectedImportStateKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonUtility.FromJson<PendingProtectedImportState>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP ProtectedBootstrap] Ignoring invalid pending bootstrap state: {ex.Message}");
                ClearState();
                return null;
            }
        }

        private static void ClearState()
        {
            EditorPrefs.DeleteKey(PendingProtectedImportStateKey);
        }
    }
}
#endif
