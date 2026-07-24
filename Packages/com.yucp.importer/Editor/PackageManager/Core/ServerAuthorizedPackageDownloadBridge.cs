using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.Importer.Editor.PackageManager
{
    public static class ServerAuthorizedPackageDownloadBridge
    {
        private const string ZipSourceKind = "zip";
        private const string UnityPackageSourceKind = "unitypackage";

        public static string StagePackageDownloadJson(
            string packageId,
            string version,
            string channel,
            string requestUrl,
            string sourceKind,
            string stagingDirectory)
        {
            var result = new StagePackageDownloadResult();

            try
            {
                if (string.IsNullOrWhiteSpace(packageId))
                {
                    result.error = "Package ID is missing.";
                    return JsonUtility.ToJson(result);
                }

                if (!IsTrustedWebUrl(requestUrl))
                {
                    result.error = "Package download authorization endpoint is not trusted.";
                    return JsonUtility.ToJson(result);
                }

                if (!string.Equals(sourceKind, ZipSourceKind, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(sourceKind, UnityPackageSourceKind, StringComparison.OrdinalIgnoreCase))
                {
                    result.error = $"Unsupported package download source kind '{sourceKind ?? "<missing>"}'.";
                    return JsonUtility.ToJson(result);
                }

                if (string.IsNullOrWhiteSpace(stagingDirectory))
                {
                    result.error = "Package staging directory is missing.";
                    return JsonUtility.ToJson(result);
                }

                string licenseToken =
#if UNITY_INCLUDE_TESTS
                    (Core.ServerAuthorizedPackageDownloadBridgeTestHooks.GetLicenseToken ?? Core.LicenseTokenCache.GetValidToken)(packageId);
#else
                    Core.LicenseTokenCache.GetValidToken(packageId);
#endif
                if (string.IsNullOrWhiteSpace(licenseToken))
                {
                    result.error = "Please import the package through the YUCP Package Manager and verify your purchase first.";
                    return JsonUtility.ToJson(result);
                }

                string machineFingerprint =
#if UNITY_INCLUDE_TESTS
                    (Core.ServerAuthorizedPackageDownloadBridgeTestHooks.GetMachineFingerprint ?? MachineFingerprintService.GetFingerprint)();
#else
                    MachineFingerprintService.GetFingerprint();
#endif
                if (string.IsNullOrWhiteSpace(machineFingerprint))
                {
                    result.error = "Could not resolve the machine fingerprint required for package download.";
                    return JsonUtility.ToJson(result);
                }

                var authorizationResponse = RequestAuthorizedDownload(
                    packageId,
                    version,
                    channel,
                    requestUrl,
                    machineFingerprint,
                    licenseToken);
                if (!authorizationResponse.success)
                {
                    result.error = authorizationResponse.error;
                    return JsonUtility.ToJson(result);
                }

                if (!IsTrustedWebUrl(authorizationResponse.downloadUrl))
                {
                    result.error = "Package download response returned an untrusted download URL.";
                    return JsonUtility.ToJson(result);
                }

                string normalizedSha256 = NormalizeSha256(authorizationResponse.packageSha256);
                if (string.IsNullOrWhiteSpace(normalizedSha256))
                {
                    result.error = "Package download response did not include a valid SHA-256 digest.";
                    return JsonUtility.ToJson(result);
                }

                var downloadResponse = DownloadAuthorizedPackage(authorizationResponse.downloadUrl, normalizedSha256);
                if (!downloadResponse.success)
                {
                    result.error = downloadResponse.error;
                    return JsonUtility.ToJson(result);
                }

                Directory.CreateDirectory(stagingDirectory);
                if (string.Equals(sourceKind, UnityPackageSourceKind, StringComparison.OrdinalIgnoreCase))
                {
                    if (!ExtractUnityPackageToDirectory(downloadResponse.packageBytes, stagingDirectory, out string unityPackageError))
                    {
                        result.error = unityPackageError;
                        return JsonUtility.ToJson(result);
                    }
                }
                else
                {
                    if (!ExtractZipToDirectory(downloadResponse.packageBytes, stagingDirectory, out string zipError))
                    {
                        result.error = zipError;
                        return JsonUtility.ToJson(result);
                    }
                }

                result.success = true;
                result.version = string.IsNullOrWhiteSpace(authorizationResponse.version)
                    ? version
                    : authorizationResponse.version;
                result.channel = authorizationResponse.channel;
                result.packageSha256 = normalizedSha256;
                result.contentType = authorizationResponse.contentType;
                result.deliveryName = authorizationResponse.deliveryName;
            }
            catch (Exception ex)
            {
                result.error = $"Package download could not be staged: {ex.Message}";
            }

            return JsonUtility.ToJson(result);
        }

        private static (bool success, string downloadUrl, string packageSha256, string version, string channel, string contentType, string deliveryName, string error) RequestAuthorizedDownload(
            string packageId,
            string version,
            string channel,
            string requestUrl,
            string machineFingerprint,
            string licenseToken)
        {
            string bodyJson = JsonUtility.ToJson(new PackageDownloadAuthorizationRequest
            {
                packageId = packageId,
                machineFingerprint = machineFingerprint,
                licenseToken = licenseToken,
                version = string.IsNullOrWhiteSpace(version) ? null : version,
                channel = string.IsNullOrWhiteSpace(channel) ? null : channel,
            });

            using var request = new UnityWebRequest(requestUrl, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(bodyJson));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 30;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept-Encoding", "identity");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                System.Threading.Thread.Sleep(20);
            }

            string responseText = request.downloadHandler?.text ?? string.Empty;
            if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
            {
                string error = ExtractErrorMessage(responseText);
                return (
                    false,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    !string.IsNullOrWhiteSpace(error)
                        ? error
                        : $"Package download authorization failed (HTTP {request.responseCode}).");
            }

            var response = JsonUtility.FromJson<PackageDownloadAuthorizationResponse>(responseText);
            if (response == null || string.IsNullOrWhiteSpace(response.downloadUrl))
            {
                return (false, null, null, null, null, null, null, "Package download authorization returned an invalid response.");
            }

            return (
                true,
                response.downloadUrl,
                response.packageSha256,
                response.version,
                response.channel,
                response.contentType,
                response.deliveryName,
                null);
        }

        private static (bool success, byte[] packageBytes, string error) DownloadAuthorizedPackage(
            string downloadUrl,
            string expectedSha256)
        {
            using var request = UnityWebRequest.Get(downloadUrl);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 60;
            request.SetRequestHeader("Accept-Encoding", "identity");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                System.Threading.Thread.Sleep(20);
            }

            string responseText = request.downloadHandler?.text ?? string.Empty;
            if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
            {
                string error = ExtractErrorMessage(responseText);
                return (
                    false,
                    null,
                    !string.IsNullOrWhiteSpace(error)
                        ? error
                        : $"Authorized package download failed (HTTP {request.responseCode}).");
            }

            byte[] packageBytes = request.downloadHandler?.data;
            if (packageBytes == null || packageBytes.Length == 0)
            {
                return (false, null, "Authorized package download returned an empty response.");
            }

            string actualSha256 = Core.RuntimeExecutionSecurityUtility.ComputeSha256Hex(packageBytes);
            if (!string.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase))
            {
                return (false, null, "Authorized package download failed integrity verification.");
            }

            return (true, packageBytes, null);
        }

        private static bool ExtractZipToDirectory(byte[] packageZipBytes, string extractRoot, out string error)
        {
            error = null;

            try
            {
                using var memory = new MemoryStream(packageZipBytes, writable: false);
                using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (!Core.RuntimeExecutionSecurityUtility.TryResolveContainedDirectoryPath(
                            extractRoot,
                            entry.FullName,
                            "Authorized package archive entry",
                            out string destinationPath,
                            out error))
                    {
                        return false;
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destinationPath);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? extractRoot);
                    using Stream entryStream = entry.Open();
                    using FileStream outputStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    entryStream.CopyTo(outputStream);
                }

                return true;
            }
            catch (InvalidDataException ex)
            {
                error = $"Authorized package archive could not be extracted: {ex.Message}";
                return false;
            }
            catch (IOException ex)
            {
                error = $"Authorized package archive could not be extracted: {ex.Message}";
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = $"Authorized package archive could not be extracted: {ex.Message}";
                return false;
            }
        }

        private static bool ExtractUnityPackageToDirectory(byte[] unityPackageBytes, string extractRoot, out string error)
        {
            error = null;

            try
            {
                using var memory = new MemoryStream(unityPackageBytes, writable: false);
                Core.TarGZipArchiveExtractor.Extract(memory, entryName =>
                {
                    if (!Core.RuntimeExecutionSecurityUtility.TryResolveContainedDirectoryPath(
                            extractRoot,
                            entryName,
                            "Authorized unitypackage entry",
                            out string destinationPath,
                            out string pathError))
                    {
                        throw new InvalidDataException(pathError);
                    }
                    return destinationPath;
                });

                return true;
            }
            catch (InvalidDataException ex)
            {
                error = $"Authorized unitypackage archive could not be extracted: {ex.Message}";
                return false;
            }
            catch (IOException ex)
            {
                error = $"Authorized unitypackage archive could not be extracted: {ex.Message}";
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = $"Authorized unitypackage archive could not be extracted: {ex.Message}";
                return false;
            }
        }

        private static bool IsTrustedWebUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return false;

            if (uri.Scheme == Uri.UriSchemeHttps)
                return true;

            return uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        }

        private static string NormalizeSha256(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
                return null;

            string normalized = hash.Trim().Replace("-", string.Empty).ToLowerInvariant();
            if (normalized.Length != 64)
                return null;

            foreach (char c in normalized)
            {
                if (!Uri.IsHexDigit(c))
                    return null;
            }

            return normalized;
        }

        private static string ExtractErrorMessage(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
                return null;

            try
            {
                var errorResponse = JsonUtility.FromJson<PackageDownloadErrorResponse>(responseText);
                if (!string.IsNullOrWhiteSpace(errorResponse?.error))
                    return errorResponse.error;
                if (!string.IsNullOrWhiteSpace(errorResponse?.message))
                    return errorResponse.message;
            }
            catch
            {
            }

            return null;
        }

        [Serializable]
        private sealed class PackageDownloadAuthorizationRequest
        {
            public string packageId;
            public string machineFingerprint;
            public string licenseToken;
            public string version;
            public string channel;
        }

        [Serializable]
        private sealed class PackageDownloadAuthorizationResponse
        {
            public string downloadUrl;
            public string packageSha256;
            public string version;
            public string channel;
            public string contentType;
            public string deliveryName;
        }

        [Serializable]
        private sealed class PackageDownloadErrorResponse
        {
            public string error;
            public string message;
        }

        [Serializable]
        private sealed class StagePackageDownloadResult
        {
            public bool success;
            public string error;
            public string version;
            public string channel;
            public string packageSha256;
            public string contentType;
            public string deliveryName;
        }
    }
}
