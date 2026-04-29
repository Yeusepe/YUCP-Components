using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    public static class ProtectedAssetUnlockService
    {
        private const string SessionKeyPrefix = "yucp.protected-unlock.";

        private static string CacheDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".unitysign", "protected-unlocks");

        internal static void ClearAll(IEnumerable<(string packageId, string protectedAssetId)> cacheKeys)
        {
            if (!EditorMainThreadDispatcher.IsMainThread)
            {
                List<(string packageId, string protectedAssetId)> cacheKeyList = cacheKeys != null
                    ? new List<(string packageId, string protectedAssetId)>(cacheKeys)
                    : null;
                EditorMainThreadDispatcher.Invoke(() => ClearAll(cacheKeyList));
                return;
            }

            if (cacheKeys != null)
            {
                foreach (var cacheKey in cacheKeys)
                {
                    if (!string.IsNullOrWhiteSpace(cacheKey.packageId) && !string.IsNullOrWhiteSpace(cacheKey.protectedAssetId))
                    {
                        SessionState.EraseString(GetSessionKey(cacheKey.packageId, cacheKey.protectedAssetId));
                    }
                    Evict(cacheKey.packageId, cacheKey.protectedAssetId);
                }
            }

            try
            {
                if (Directory.Exists(CacheDir))
                {
                    Directory.Delete(CacheDir, true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP License] Could not clear protected unlock cache directory: {ex.Message}");
            }
        }

        public static bool TryAuthorizePackage(
            string packageId,
            string protectedAssetId,
            out string wrappedContentKey,
            out string error)
        {
            wrappedContentKey = null;
            ProtectedAssetUnlockGrant grant;
            if (!TryAuthorizePackage(packageId, protectedAssetId, out grant, out error))
                return false;

            wrappedContentKey = grant?.wrappedContentKey;
            return true;
        }

        public static bool TryAuthorizePackage(
            string packageId,
            string protectedAssetId,
            out ProtectedAssetUnlockGrant grant,
            out string error)
        {
            return TryAuthorizePackage(packageId, protectedAssetId, null, out grant, out error);
        }

        public static bool TryAuthorizePackage(
            string packageId,
            string protectedAssetId,
            string expectedContentHash,
            out ProtectedAssetUnlockGrant grant,
            out string error)
        {
            grant = null;
            error = null;

            if (string.IsNullOrEmpty(packageId))
            {
                error = "Package ID is missing.";
                return false;
            }

            if (IsServerAuthorizedAliasPackage(packageId, out AliasPackageContract aliasPackage))
            {
                string aliasId = aliasPackage?.aliasId ?? packageId;
                error =
                    $"Alias package '{aliasId}' uses server-authorized delivery and must be resolved through UpdateDeliveryService instead of the legacy protected unlock endpoint.";
                return false;
            }

            string licenseToken = LicenseTokenCache.GetValidToken(packageId);
            if (string.IsNullOrEmpty(licenseToken))
            {
                error = "Please import the package through the YUCP Package Manager and verify your purchase first.";
                return false;
            }

            if (string.IsNullOrEmpty(protectedAssetId))
            {
                grant = new ProtectedAssetUnlockGrant();
                return true;
            }

            string projectId = ProjectIdentityService.GetOrCreateProjectId();
            if (string.IsNullOrEmpty(projectId))
            {
                error = "Could not create the Unity project identity required for protected asset unlock.";
                return false;
            }

            string machineFingerprint = MachineFingerprintService.GetFingerprint();
            string serverUrl = LicenseServerResolver.GetLicenseServerUrl();
            string bodyJson = JsonUtility.ToJson(new UnlockRequest
            {
                packageId = packageId,
                protectedAssetId = protectedAssetId,
                projectId = projectId,
                machineFingerprint = machineFingerprint,
                licenseToken = licenseToken,
            });

            using var request = new UnityWebRequest($"{serverUrl.TrimEnd('/')}/v1/licenses/unlock-protected", "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(bodyJson));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 20;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept-Encoding", "identity");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                Thread.Sleep(20);
            }

            string responseText = request.downloadHandler?.text ?? string.Empty;
            if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
            {
                var failure = JsonUtility.FromJson<UnlockResponse>(responseText);
                error = !string.IsNullOrEmpty(failure?.error)
                    ? failure.error
                    : $"Protected asset unlock failed (HTTP {request.responseCode}).";
                return false;
            }

            var response = JsonUtility.FromJson<UnlockResponse>(responseText);
            if (response == null || string.IsNullOrEmpty(response.unlockToken))
            {
                error = "Protected asset unlock returned an invalid response.";
                return false;
            }

            if (!YucpJwtTokenUtility.TryValidateProtectedUnlockToken(
                response.unlockToken,
                packageId,
                protectedAssetId,
                expectedContentHash,
                out var claims))
            {
                error = "Protected asset unlock token failed signature or claim validation.";
                return false;
            }

            grant = ProtectedAssetUnlockGrant.FromClaims(claims);
            Evict(packageId, protectedAssetId);
            return true;
        }

        public static bool TryIssueMaterializationGrant(
            string packageId,
            string protectedAssetId,
            IReadOnlyList<string> assetPaths,
            out ProtectedMaterializationGrant grant,
            out string error)
        {
            grant = null;
            error = null;

            if (string.IsNullOrEmpty(packageId))
            {
                error = "Package ID is missing.";
                return false;
            }

            if (string.IsNullOrEmpty(protectedAssetId))
            {
                error = "Protected asset ID is missing.";
                return false;
            }

            if (assetPaths == null || assetPaths.Count == 0)
            {
                error = "Protected payload descriptor is missing payload asset paths.";
                return false;
            }

            string licenseToken = LicenseTokenCache.GetValidToken(packageId);
            if (string.IsNullOrEmpty(licenseToken))
            {
                error = "Please import the package through the YUCP Package Manager and verify your purchase first.";
                return false;
            }

            string projectId = ProjectIdentityService.GetOrCreateProjectId();
            if (string.IsNullOrEmpty(projectId))
            {
                error = "Could not create the Unity project identity required for protected materialization.";
                return false;
            }

            string machineFingerprint = MachineFingerprintService.GetFingerprint();
            string serverUrl = LicenseServerResolver.GetLicenseServerUrl();
            string bodyJson = JsonUtility.ToJson(new ProtectedMaterializationGrantRequest
            {
                packageId = packageId,
                protectedAssetId = protectedAssetId,
                projectId = projectId,
                machineFingerprint = machineFingerprint,
                licenseToken = licenseToken,
                assetPaths = assetPaths is string[] array ? array : new List<string>(assetPaths).ToArray(),
            });

            using var request = new UnityWebRequest(
                $"{serverUrl.TrimEnd('/')}/v1/licenses/protected-materialization-grant",
                "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(bodyJson));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 20;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept-Encoding", "identity");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                Thread.Sleep(20);
            }

            string responseText = request.downloadHandler?.text ?? string.Empty;
            if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
            {
                var failure = JsonUtility.FromJson<ProtectedMaterializationGrantResponse>(responseText);
                error = !string.IsNullOrEmpty(failure?.error)
                    ? failure.error
                    : $"Protected materialization grant failed (HTTP {request.responseCode}).";
                return false;
            }

            var response = JsonUtility.FromJson<ProtectedMaterializationGrantResponse>(responseText);
            if (response == null || string.IsNullOrEmpty(response.grant))
            {
                error = "Protected materialization grant returned an invalid response.";
                return false;
            }

            grant = new ProtectedMaterializationGrant
            {
                grant = response.grant,
                expiresAt = response.expiresAt,
            };
            return true;
        }

        private static void Evict(string packageId, string protectedAssetId)
        {
            if (!EditorMainThreadDispatcher.IsMainThread)
            {
                EditorMainThreadDispatcher.Invoke(() => Evict(packageId, protectedAssetId));
                return;
            }

            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(protectedAssetId))
                return;

            string sessionKey = GetSessionKey(packageId, protectedAssetId);
            SessionState.EraseString(sessionKey);

            string path = CacheFilePath(packageId, protectedAssetId);
            if (!File.Exists(path))
                return;

            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP License] Could not delete protected unlock cache '{path}': {ex.Message}");
            }
        }

        private static string GetSessionKey(string packageId, string protectedAssetId)
        {
            return $"{SessionKeyPrefix}{packageId}:{protectedAssetId}";
        }

        private static string CacheFilePath(string packageId, string protectedAssetId)
        {
            string safePackageId = packageId.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
            string safeAssetId = protectedAssetId.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
            return Path.Combine(CacheDir, $"{safePackageId}__{safeAssetId}.dat");
        }

        private static bool IsServerAuthorizedAliasPackage(string packageId, out AliasPackageContract aliasPackage)
        {
            aliasPackage = ResolveInstalledPackage(packageId)?.aliasPackage;
            return aliasPackage != null &&
                string.Equals(aliasPackage.kind, "alias-v1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(aliasPackage.installStrategy, UpdateDeliveryService.ServerAuthorizedInstallStrategy, StringComparison.OrdinalIgnoreCase);
        }

        private static InstalledPackageInfo ResolveInstalledPackage(string packageId)
        {
#if UNITY_INCLUDE_TESTS
            if (ProtectedAssetUnlockServiceTestHooks.InstalledPackageResolver != null)
            {
                return ProtectedAssetUnlockServiceTestHooks.InstalledPackageResolver(packageId);
            }
#endif

            InstalledPackageRegistry registry = InstalledPackageRegistry.Load();
            return registry?.GetPackage(packageId);
        }

        [Serializable]
        private class UnlockRequest
        {
            public string packageId;
            public string protectedAssetId;
            public string projectId;
            public string machineFingerprint;
            public string licenseToken;
        }

        [Serializable]
        private class UnlockResponse
        {
            public bool success;
            public string unlockToken;
            public long expiresAt;
            public string error;
        }

        [Serializable]
        private class ProtectedMaterializationGrantRequest
        {
            public string packageId;
            public string protectedAssetId;
            public string projectId;
            public string machineFingerprint;
            public string licenseToken;
            public string[] assetPaths;
        }

        [Serializable]
        private class ProtectedMaterializationGrantResponse
        {
            public bool success;
            public string grant;
            public long expiresAt;
            public string error;
        }

        public sealed class ProtectedAssetUnlockGrant
        {
            public string unlockMode;
            public string wrappedContentKey;
            public string contentKeyBase64;
            public string contentHash;

            internal static ProtectedAssetUnlockGrant FromClaims(YucpJwtTokenUtility.ProtectedUnlockTokenClaims claims)
            {
                return new ProtectedAssetUnlockGrant
                {
                    unlockMode = claims?.unlock_mode ?? "",
                    wrappedContentKey = claims?.wrapped_content_key ?? "",
                    contentKeyBase64 = claims?.content_key_b64 ?? "",
                    contentHash = claims?.content_hash ?? "",
                };
            }
        }

        public sealed class ProtectedMaterializationGrant
        {
            public string grant;
            public long expiresAt;
        }
    }
}
