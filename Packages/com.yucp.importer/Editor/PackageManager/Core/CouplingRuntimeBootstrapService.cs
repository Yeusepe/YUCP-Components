using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class CouplingRuntimeBootstrapService
    {
        internal static Func<(bool success, string error)> s_validateRuntimeOverride;
        internal static Func<string, string> s_getLicenseTokenOverride;
        internal static Func<string> s_getProjectIdOverride;
        internal static Func<string> s_getMachineFingerprintOverride;
        internal static Func<string> s_getServerUrlOverride;
        internal static Func<(bool success, string error)> s_repairRuntimeRegistrationOverride;
        internal static Func<string, string, string, string, (bool success, string runtimePackageToken, string runtimePackageSha256, long expiresAt, string error)> s_requestRuntimePackageTokenOverride;
        internal static Func<string, string, string, string, (bool success, string packageZipPath, string error)> s_downloadRuntimePackageOverride;
        internal static Func<string, string, (bool success, string error)> s_installRuntimePackageOverride;

        internal static bool TryEnsureProtectedMaterializationRuntimeReady(string packageId, out string error)
        {
            error = null;

            var initialValidation = ValidateRuntime();
            if (initialValidation.success)
                return true;

            var repairResult = TryRepairRuntimeRegistration();
            if (repairResult.attempted)
            {
                if (!repairResult.success)
                {
                    error = repairResult.error;
                    return false;
                }

                var postRepairValidation = ValidateRuntime();
                if (postRepairValidation.success)
                    return true;
            }

            if (string.IsNullOrWhiteSpace(packageId))
            {
                error = string.IsNullOrWhiteSpace(initialValidation.error)
                    ? "The package protection runtime is not ready for protected imports."
                    : initialValidation.error;
                return false;
            }

            string licenseToken = (s_getLicenseTokenOverride ?? LicenseTokenCache.GetValidToken)?.Invoke(packageId);
            if (string.IsNullOrWhiteSpace(licenseToken))
            {
                error = "Please import the package through the YUCP Package Manager and verify your purchase first.";
                return false;
            }

            string projectId = (s_getProjectIdOverride ?? ProjectIdentityService.GetOrCreateProjectId)?.Invoke();
            if (string.IsNullOrWhiteSpace(projectId))
            {
                error = "Could not create the Unity project identity required for runtime bootstrap.";
                return false;
            }

            string machineFingerprint = (s_getMachineFingerprintOverride ?? MachineFingerprintService.GetFingerprint)?.Invoke();
            if (string.IsNullOrWhiteSpace(machineFingerprint))
            {
                error = "Could not resolve the machine fingerprint required for runtime bootstrap.";
                return false;
            }

            string serverUrl = (s_getServerUrlOverride ?? LicenseServerResolver.GetLicenseServerUrl)?.Invoke();
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                error = "Could not resolve the YUCP server URL required for runtime bootstrap.";
                return false;
            }

            var tokenResult = RequestRuntimePackageToken(packageId, projectId, machineFingerprint, licenseToken);
            if (!tokenResult.success)
            {
                error = tokenResult.error;
                return false;
            }

            string tempRoot = Path.Combine(Path.GetTempPath(), $"yucp-runtime-bootstrap-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);
            try
            {
                var downloadResult = DownloadRuntimePackage(
                    serverUrl,
                    tokenResult.runtimePackageToken,
                    tokenResult.runtimePackageSha256,
                    tempRoot);
                if (!downloadResult.success)
                {
                    error = downloadResult.error;
                    return false;
                }

                string installRoot = PackageManagerRuntimeSettings.ResolveRuntimeInstallRoot();
                var installResult = InstallRuntimePackage(downloadResult.packageZipPath, installRoot);
                if (!installResult.success)
                {
                    error = installResult.error;
                    return false;
                }

                var postInstallValidation = ValidateRuntime();
                if (!postInstallValidation.success)
                {
                    error = string.IsNullOrWhiteSpace(postInstallValidation.error)
                        ? "The installed package protection runtime still does not support protected imports."
                        : postInstallValidation.error;
                    return false;
                }

                return true;
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static (bool success, string error) ValidateRuntime()
        {
            if (s_validateRuntimeOverride != null)
                return s_validateRuntimeOverride();

            bool success = CouplingRuntimeShimService.TryValidateProtectedMaterializationRuntime(out string error);
            return (success, error);
        }

        private static (bool attempted, bool success, string error) TryRepairRuntimeRegistration()
        {
            if (s_repairRuntimeRegistrationOverride != null)
            {
                var result = s_repairRuntimeRegistrationOverride();
                return (true, result.success, result.error);
            }

            string installRoot = PackageManagerRuntimeSettings.ResolveRuntimeInstallRoot();
            string activeStatePath = Path.Combine(installRoot, "state", "active.json");
            if (!File.Exists(activeStatePath))
                return (false, false, null);

            ActiveRuntimeState activeState;
            try
            {
                activeState = JsonUtility.FromJson<ActiveRuntimeState>(File.ReadAllText(activeStatePath));
            }
            catch (IOException ex)
            {
                return (true, false, $"The package protection runtime state could not be read during repair: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                return (true, false, $"The package protection runtime state could not be read during repair: {ex.Message}");
            }

            if (activeState == null ||
                string.IsNullOrWhiteSpace(activeState.activeDllPath) ||
                string.IsNullOrWhiteSpace(activeState.activePackageDir))
            {
                return (false, false, null);
            }

            string metadataPath = Path.Combine(activeState.activePackageDir, "CouplingRuntimeCom.metadata.json");
            if (!File.Exists(activeState.activeDllPath) || !File.Exists(metadataPath))
                return (false, false, null);

            RuntimeMetadata metadata;
            try
            {
                metadata = JsonUtility.FromJson<RuntimeMetadata>(File.ReadAllText(metadataPath));
            }
            catch (IOException ex)
            {
                return (true, false, $"The package protection runtime metadata could not be read during repair: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                return (true, false, $"The package protection runtime metadata could not be read during repair: {ex.Message}");
            }

            if (metadata == null || string.IsNullOrWhiteSpace(metadata.sha256))
                return (false, false, null);

            string actualSha256;
            try
            {
                actualSha256 = ComputeSha256Hex(File.ReadAllBytes(activeState.activeDllPath));
            }
            catch (IOException ex)
            {
                return (true, false, $"The package protection runtime DLL could not be read during repair: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                return (true, false, $"The package protection runtime DLL could not be read during repair: {ex.Message}");
            }

            if (!string.Equals(actualSha256, metadata.sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return (true, false, "The installed package protection runtime failed integrity verification during repair.");
            }

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "regsvr32.exe",
                        Arguments = $"/s \"{activeState.activeDllPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    }
                };

                if (!process.Start())
                {
                    return (true, false, "The package protection runtime repair could not start COM registration.");
                }

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    string details = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
                    return (
                        true,
                        false,
                        string.IsNullOrWhiteSpace(details)
                            ? $"The package protection runtime repair failed with exit code {process.ExitCode}."
                            : details
                    );
                }

                return (true, true, null);
            }
            catch (InvalidOperationException ex)
            {
                return (true, false, $"The package protection runtime repair could not start COM registration: {ex.Message}");
            }
            catch (Win32Exception ex)
            {
                return (true, false, $"The package protection runtime repair could not start COM registration: {ex.Message}");
            }
        }

        private static (bool success, string runtimePackageToken, string runtimePackageSha256, long expiresAt, string error) RequestRuntimePackageToken(
            string packageId,
            string projectId,
            string machineFingerprint,
            string licenseToken)
        {
            if (s_requestRuntimePackageTokenOverride != null)
            {
                return s_requestRuntimePackageTokenOverride(packageId, projectId, machineFingerprint, licenseToken);
            }

            string serverUrl = LicenseServerResolver.GetLicenseServerUrl();
            string bodyJson = JsonUtility.ToJson(new RuntimePackageTokenRequest
            {
                packageId = packageId,
                projectId = projectId,
                machineFingerprint = machineFingerprint,
                licenseToken = licenseToken,
            });

            using var request = new UnityWebRequest($"{serverUrl.TrimEnd('/')}/v1/licenses/runtime-package-token", "POST");
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
                var failure = JsonUtility.FromJson<RuntimePackageTokenResponse>(responseText);
                return (
                    false,
                    null,
                    null,
                    0,
                    !string.IsNullOrEmpty(failure?.error)
                        ? failure.error
                        : $"Runtime package token request failed (HTTP {request.responseCode})."
                );
            }

            var response = JsonUtility.FromJson<RuntimePackageTokenResponse>(responseText);
            if (response == null || string.IsNullOrWhiteSpace(response.runtimePackageToken))
            {
                return (false, null, null, 0, "Runtime package token request returned an invalid response.");
            }

            return (
                true,
                response.runtimePackageToken,
                response.runtimePackageSha256,
                response.expiresAt,
                null
            );
        }

        private static (bool success, string packageZipPath, string error) DownloadRuntimePackage(
            string serverUrl,
            string runtimePackageToken,
            string expectedSha256,
            string tempRoot)
        {
            if (s_downloadRuntimePackageOverride != null)
                return s_downloadRuntimePackageOverride(serverUrl, runtimePackageToken, expectedSha256, tempRoot);

            using var request = UnityWebRequest.Get($"{serverUrl.TrimEnd('/')}/v1/licenses/runtime-package?token={Uri.EscapeDataString(runtimePackageToken)}");
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
                string error = !string.IsNullOrWhiteSpace(responseText) && responseText.TrimStart().StartsWith("{", StringComparison.Ordinal)
                    ? (JsonUtility.FromJson<RuntimePackageErrorResponse>(responseText)?.error ?? string.Empty)
                    : string.Empty;
                return (
                    false,
                    null,
                    !string.IsNullOrWhiteSpace(error)
                        ? error
                        : $"Runtime package download failed (HTTP {request.responseCode})."
                );
            }

            byte[] bytes = request.downloadHandler?.data;
            if (bytes == null || bytes.Length == 0)
            {
                return (false, null, "Runtime package download returned an empty response.");
            }

            string actualSha256 = ComputeSha256Hex(bytes);
            if (!string.IsNullOrWhiteSpace(expectedSha256) &&
                !string.Equals(expectedSha256.Trim(), actualSha256, StringComparison.OrdinalIgnoreCase))
            {
                return (false, null, "Runtime package download failed integrity verification.");
            }

            string zipPath = Path.Combine(tempRoot, "runtime-package.zip");
            File.WriteAllBytes(zipPath, bytes);
            return (true, zipPath, null);
        }

        private static (bool success, string error) InstallRuntimePackage(string packageZipPath, string installRoot)
        {
            if (s_installRuntimePackageOverride != null)
                return s_installRuntimePackageOverride(packageZipPath, installRoot);

            if (string.IsNullOrWhiteSpace(packageZipPath) || !File.Exists(packageZipPath))
            {
                return (false, "Runtime package bootstrap could not locate the downloaded package archive.");
            }

            string extractRoot = Path.Combine(Path.GetDirectoryName(packageZipPath) ?? Path.GetTempPath(), "extract");
            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(packageZipPath, extractRoot);

            string manifestPath = Path.Combine(extractRoot, "runtime-package-manifest.json");
            if (!File.Exists(manifestPath))
            {
                return (false, "Runtime package bootstrap is missing runtime-package-manifest.json.");
            }

            RuntimePackageManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<RuntimePackageManifest>(File.ReadAllText(manifestPath));
            }
            catch (IOException ex)
            {
                return (false, $"Runtime package manifest could not be read: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                return (false, $"Runtime package manifest could not be read: {ex.Message}");
            }

            if (manifest == null ||
                string.IsNullOrWhiteSpace(manifest.buildDir) ||
                string.IsNullOrWhiteSpace(manifest.installScriptPath))
            {
                return (false, "Runtime package manifest is incomplete.");
            }

            string sourceDir = ResolvePackagePath(extractRoot, manifest.buildDir);
            string installScriptPath = ResolvePackagePath(extractRoot, manifest.installScriptPath);
            if (!Directory.Exists(sourceDir))
            {
                return (false, "Runtime package bootstrap is missing the staged build payload.");
            }

            if (!File.Exists(installScriptPath))
            {
                return (false, "Runtime package bootstrap is missing the install script.");
            }

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments =
                            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{installScriptPath}\" -SourceDir \"{sourceDir}\" -InstallRoot \"{installRoot}\"",
                        WorkingDirectory = Path.GetDirectoryName(installScriptPath) ?? extractRoot,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    }
                };

                if (!process.Start())
                {
                    return (false, "Runtime package bootstrap could not start the runtime installer.");
                }

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    string details = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
                    return (
                        false,
                        string.IsNullOrWhiteSpace(details)
                            ? $"Runtime package bootstrap failed with exit code {process.ExitCode}."
                            : details
                    );
                }

                return (true, null);
            }
            catch (InvalidOperationException ex)
            {
                return (false, $"Runtime package bootstrap could not start the runtime installer: {ex.Message}");
            }
            catch (Win32Exception ex)
            {
                return (false, $"Runtime package bootstrap could not start the runtime installer: {ex.Message}");
            }
        }

        private static string ResolvePackagePath(string packageRoot, string relativePath)
        {
            string normalized = (relativePath ?? string.Empty)
                .Replace('/', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string resolved = Path.GetFullPath(Path.Combine(packageRoot, normalized));
            string rootWithSeparator = Path.GetFullPath(packageRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Runtime package path escaped the package root: {relativePath}");
            }

            return resolved;
        }

        private static string ComputeSha256Hex(byte[] data)
        {
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(data ?? Array.Empty<byte>());
            var builder = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                builder.Append(b.ToString("x2"));
            return builder.ToString();
        }

        private static void TryDeleteDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return;

            try
            {
                Directory.Delete(directoryPath, true);
            }
            catch
            {
            }
        }

        [Serializable]
        private sealed class RuntimePackageTokenRequest
        {
            public string packageId;
            public string projectId;
            public string machineFingerprint;
            public string licenseToken;
        }

        [Serializable]
        private sealed class RuntimePackageTokenResponse
        {
            public bool success;
            public string runtimePackageToken;
            public string runtimePackageSha256;
            public long expiresAt;
            public string error;
        }

        [Serializable]
        private sealed class RuntimePackageErrorResponse
        {
            public string error;
        }

        [Serializable]
        private sealed class RuntimePackageManifest
        {
            public int version;
            public string buildDir;
            public string installScriptPath;
            public string repairScriptPath;
        }

        [Serializable]
        private sealed class ActiveRuntimeState
        {
            public string activeBuildId;
            public string activeVersion;
            public string activePackageDir;
            public string activeDllPath;
        }

        [Serializable]
        private sealed class RuntimeMetadata
        {
            public string version;
            public string buildId;
            public string dllName;
            public string clientName;
            public string materializeScriptName;
            public string couplingDllName;
            public string sha256;
        }
    }
}
