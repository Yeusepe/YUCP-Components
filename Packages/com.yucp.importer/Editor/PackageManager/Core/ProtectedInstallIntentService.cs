using System;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class ProtectedInstallIntentService
    {
        private const string SessionKeyPrefix = "yucp.protected-install-intent.";
        private const string EditorPrefsKeyPrefix = "YUCP.PackageManager.ProtectedInstallIntent.";

        internal static bool TryAuthorizeInstall(
            string packageId,
            string protectedAssetId,
            string manifestBindingSha256,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(packageId))
            {
                error = "Package ID is missing.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(protectedAssetId))
            {
                error = "Protected asset ID is missing.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(manifestBindingSha256))
            {
                error = "Protected payload descriptor is missing the manifest binding hash required for install authorization.";
                return false;
            }

            if (TryGetValidCachedToken(packageId, protectedAssetId, manifestBindingSha256))
                return true;

            string licenseToken = LicenseTokenCache.GetValidToken(packageId);
            if (string.IsNullOrEmpty(licenseToken))
            {
                error = "Please import the package through the YUCP Package Manager and verify your purchase first.";
                return false;
            }

            string projectId = ProjectIdentityService.GetOrCreateProjectId();
            if (string.IsNullOrEmpty(projectId))
            {
                error = "Could not create the Unity project identity required for protected install authorization.";
                return false;
            }

            string machineFingerprint = MachineFingerprintService.GetFingerprint();
            string serverUrl = LicenseServerResolver.GetLicenseServerUrl();
            string bodyJson = JsonUtility.ToJson(new InstallIntentRequest
            {
                packageId = packageId,
                protectedAssetId = protectedAssetId,
                machineFingerprint = machineFingerprint,
                projectId = projectId,
                manifestBindingSha256 = manifestBindingSha256,
                licenseToken = licenseToken,
            });

            using var request = new UnityWebRequest($"{serverUrl.TrimEnd('/')}/v1/licenses/protected-install-intent", "POST");
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
                var failure = JsonUtility.FromJson<InstallIntentResponse>(responseText);
                error = !string.IsNullOrEmpty(failure?.error)
                    ? failure.error
                    : $"Protected install authorization failed (HTTP {request.responseCode}).";
                return false;
            }

            var response = JsonUtility.FromJson<InstallIntentResponse>(responseText);
            if (response == null || string.IsNullOrEmpty(response.installIntentToken))
            {
                error = "Protected install authorization returned an invalid response.";
                return false;
            }

            if (!YucpJwtTokenUtility.TryValidateProtectedInstallIntentToken(
                    response.installIntentToken,
                    packageId,
                    protectedAssetId,
                    manifestBindingSha256,
                    out _))
            {
                error = "Protected install intent token failed signature or claim validation.";
                return false;
            }

            StoreToken(packageId, response.installIntentToken);
            return true;
        }

        internal static void Clear(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return;

            SessionState.EraseString(GetSessionKey(packageId));
            EditorPrefs.DeleteKey(GetEditorPrefsKey(packageId));
        }

        private static bool TryGetValidCachedToken(
            string packageId,
            string protectedAssetId,
            string manifestBindingSha256)
        {
            string token = SessionState.GetString(GetSessionKey(packageId), null);
            if (IsTokenValid(token, packageId, protectedAssetId, manifestBindingSha256))
                return true;

            token = EditorPrefs.GetString(GetEditorPrefsKey(packageId), null);
            if (IsTokenValid(token, packageId, protectedAssetId, manifestBindingSha256))
            {
                SessionState.SetString(GetSessionKey(packageId), token);
                return true;
            }

            Clear(packageId);
            return false;
        }

        private static bool IsTokenValid(
            string token,
            string packageId,
            string protectedAssetId,
            string manifestBindingSha256)
        {
            return !string.IsNullOrEmpty(token) &&
                YucpJwtTokenUtility.TryValidateProtectedInstallIntentToken(
                    token,
                    packageId,
                    protectedAssetId,
                    manifestBindingSha256,
                    out _);
        }

        private static void StoreToken(string packageId, string token)
        {
            SessionState.SetString(GetSessionKey(packageId), token);
            EditorPrefs.SetString(GetEditorPrefsKey(packageId), token);
        }

        private static string GetSessionKey(string packageId)
        {
            return SessionKeyPrefix + packageId;
        }

        private static string GetEditorPrefsKey(string packageId)
        {
            return EditorPrefsKeyPrefix + packageId;
        }

        [Serializable]
        private class InstallIntentRequest
        {
            public string packageId;
            public string protectedAssetId;
            public string machineFingerprint;
            public string projectId;
            public string manifestBindingSha256;
            public string licenseToken;
        }

        [Serializable]
        private class InstallIntentResponse
        {
            public string installIntentToken;
            public long expiresAt;
            public string error;
        }
    }
}
