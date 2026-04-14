using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class CouplingRuntimeBootstrapService
    {
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

            string licenseToken =
#if UNITY_INCLUDE_TESTS
                (CouplingRuntimeBootstrapServiceTestHooks.GetLicenseToken ?? LicenseTokenCache.GetValidToken)?.Invoke(packageId);
#else
                LicenseTokenCache.GetValidToken(packageId);
#endif
            if (string.IsNullOrWhiteSpace(licenseToken))
            {
                error = "Please import the package through the YUCP Package Manager and verify your purchase first.";
                return false;
            }

            string projectId =
#if UNITY_INCLUDE_TESTS
                (CouplingRuntimeBootstrapServiceTestHooks.GetProjectId ?? ProjectIdentityService.GetOrCreateProjectId)?.Invoke();
#else
                ProjectIdentityService.GetOrCreateProjectId();
#endif
            if (string.IsNullOrWhiteSpace(projectId))
            {
                error = "Could not create the Unity project identity required for runtime bootstrap.";
                return false;
            }

            string machineFingerprint =
#if UNITY_INCLUDE_TESTS
                (CouplingRuntimeBootstrapServiceTestHooks.GetMachineFingerprint ?? MachineFingerprintService.GetFingerprint)?.Invoke();
#else
                MachineFingerprintService.GetFingerprint();
#endif
            if (string.IsNullOrWhiteSpace(machineFingerprint))
            {
                error = "Could not resolve the machine fingerprint required for runtime bootstrap.";
                return false;
            }

            string serverUrl =
#if UNITY_INCLUDE_TESTS
                (CouplingRuntimeBootstrapServiceTestHooks.GetServerUrl ?? LicenseServerResolver.GetLicenseServerUrl)?.Invoke();
#else
                LicenseServerResolver.GetLicenseServerUrl();
#endif
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
                    tokenResult.runtimePackageSha256);
                if (!downloadResult.success)
                {
                    error = downloadResult.error;
                    return false;
                }

                string installRoot = PackageManagerRuntimeSettings.ResolveRuntimeInstallRoot();
                var installResult = InstallRuntimePackage(downloadResult.packageZipBytes, tempRoot, installRoot);
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
#if UNITY_INCLUDE_TESTS
            if (CouplingRuntimeBootstrapServiceTestHooks.ValidateRuntime != null)
                return CouplingRuntimeBootstrapServiceTestHooks.ValidateRuntime();
#endif

            bool success = CouplingRuntimeShimService.TryValidateProtectedMaterializationRuntime(out string error);
            return (success, error);
        }

        private static (bool attempted, bool success, string error) TryRepairRuntimeRegistration()
        {
#if UNITY_INCLUDE_TESTS
            if (CouplingRuntimeBootstrapServiceTestHooks.RepairRuntimeRegistration != null)
            {
                var result = CouplingRuntimeBootstrapServiceTestHooks.RepairRuntimeRegistration();
                return (true, result.success, result.error);
            }
#endif

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

            if (activeState == null || string.IsNullOrWhiteSpace(activeState.activePackageDir))
            {
                return (false, false, null);
            }

            if (!RuntimeExecutionSecurityUtility.TryResolveRuntimePackageDirectory(
                    installRoot,
                    activeState.activePackageDir,
                    "The package protection runtime active package directory",
                    out string activePackageDir,
                    out string packageDirError))
            {
                return (true, false, packageDirError);
            }

            string metadataPath = Path.Combine(activePackageDir, "CouplingRuntimeCom.metadata.json");
            if (!File.Exists(metadataPath))
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

            if (metadata == null ||
                string.IsNullOrWhiteSpace(metadata.sha256) ||
                string.IsNullOrWhiteSpace(metadata.dllName))
            {
                return (false, false, null);
            }

            if (!TryResolveActiveRuntimeDllPath(
                    activePackageDir,
                    activeState.activeDllPath,
                    metadata,
                    out string activeDllPath,
                    out string dllPathError))
            {
                return (true, false, dllPathError);
            }

            if (!RuntimeExecutionSecurityUtility.TryOpenLockedRead(activeDllPath, out var dllLock, out byte[] dllBytes, out string readError))
            {
                return (true, false, readError);
            }

            using (dllLock)
            {
                string actualSha256 = ComputeSha256Hex(dllBytes);
                if (!string.Equals(actualSha256, metadata.sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return (true, false, "The installed package protection runtime failed integrity verification during repair.");
                }

                if (!TryCreateRuntimeRepairStartInfo(activeDllPath, out var startInfo, out string startInfoError))
                {
                    return (true, false, startInfoError);
                }

                try
                {
                    using var process = new Process
                    {
                        StartInfo = startInfo,
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
        }

        private static bool TryResolveActiveRuntimeDllPath(
            string activePackageDir,
            string activeDllPathFromState,
            RuntimeMetadata metadata,
            out string activeDllPath,
            out string error)
        {
            activeDllPath = null;
            error = null;

            if (!RuntimeExecutionSecurityUtility.TryResolveContainedFilePath(
                    activePackageDir,
                    metadata.dllName,
                    ".dll",
                    "The package protection runtime DLL",
                    allowSubdirectories: false,
                    out string resolvedDllPath,
                    out error))
            {
                return false;
            }

            if (!File.Exists(resolvedDllPath))
            {
                error = "The package protection runtime DLL is missing from the active installation.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(activeDllPathFromState))
            {
                string normalizedStatePath;
                try
                {
                    normalizedStatePath = Path.GetFullPath(activeDllPathFromState);
                }
                catch (Exception ex)
                {
                    error = $"The package protection runtime state contained an invalid runtime DLL path: {ex.Message}";
                    return false;
                }

                if (!string.Equals(normalizedStatePath, resolvedDllPath, StringComparison.OrdinalIgnoreCase))
                {
                    error = "The package protection runtime state did not match the verified runtime DLL location.";
                    return false;
                }
            }

            activeDllPath = resolvedDllPath;
            return true;
        }

        private static (bool success, string runtimePackageToken, string runtimePackageSha256, long expiresAt, string error) RequestRuntimePackageToken(
            string packageId,
            string projectId,
            string machineFingerprint,
            string licenseToken)
        {
#if UNITY_INCLUDE_TESTS
            if (CouplingRuntimeBootstrapServiceTestHooks.RequestRuntimePackageToken != null)
            {
                return CouplingRuntimeBootstrapServiceTestHooks.RequestRuntimePackageToken(packageId, projectId, machineFingerprint, licenseToken);
            }
#endif

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

        private static (bool success, byte[] packageZipBytes, string error) DownloadRuntimePackage(
            string serverUrl,
            string runtimePackageToken,
            string expectedSha256)
        {
#if UNITY_INCLUDE_TESTS
            if (CouplingRuntimeBootstrapServiceTestHooks.DownloadRuntimePackage != null)
                return CouplingRuntimeBootstrapServiceTestHooks.DownloadRuntimePackage(serverUrl, runtimePackageToken, expectedSha256);
#endif

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

            return (true, bytes, null);
        }

        private static (bool success, string error) InstallRuntimePackage(byte[] packageZipBytes, string workingRoot, string installRoot)
        {
#if UNITY_INCLUDE_TESTS
            if (CouplingRuntimeBootstrapServiceTestHooks.InstallRuntimePackage != null)
                return CouplingRuntimeBootstrapServiceTestHooks.InstallRuntimePackage(packageZipBytes, workingRoot, installRoot);
#endif

            if (packageZipBytes == null || packageZipBytes.Length == 0)
            {
                return (false, "Runtime package bootstrap could not locate the downloaded package archive.");
            }

            string extractRoot = Path.Combine(workingRoot, "extract");
            Directory.CreateDirectory(extractRoot);

            if (!ExtractArchiveToDirectory(packageZipBytes, extractRoot, out string extractError))
            {
                return (false, extractError);
            }

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

            if (!TryCreateRuntimeInstallStartInfo(extractRoot, manifest, installRoot, out var startInfo, out string startInfoError))
            {
                return (false, startInfoError);
            }

            try
            {
                using var process = new Process
                {
                    StartInfo = startInfo,
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

        private static bool TryCreateRuntimeRepairStartInfo(
            string activeDllPath,
            out ProcessStartInfo startInfo,
            out string error)
        {
            startInfo = null;
            error = null;

            string regsvr32Path = RuntimeExecutionSecurityUtility.ResolveWindowsSystemExecutablePath("regsvr32.exe");
            if (string.IsNullOrWhiteSpace(regsvr32Path))
            {
                error = "The package protection runtime repair could not locate regsvr32.exe in the Windows system directory.";
                return false;
            }

            startInfo = new ProcessStartInfo
            {
                FileName = regsvr32Path,
                Arguments = $"/s \"{activeDllPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            return true;
        }

        private static bool TryCreateRuntimeInstallStartInfo(
            string extractRoot,
            RuntimePackageManifest manifest,
            string installRoot,
            out ProcessStartInfo startInfo,
            out string error)
        {
            startInfo = null;
            error = null;

            if (manifest == null ||
                string.IsNullOrWhiteSpace(manifest.buildDir) ||
                string.IsNullOrWhiteSpace(manifest.installScriptPath))
            {
                error = "Runtime package manifest is incomplete.";
                return false;
            }

            if (!RuntimeExecutionSecurityUtility.TryResolveContainedDirectoryPath(
                    extractRoot,
                    manifest.buildDir,
                    "The staged runtime build payload",
                    out string sourceDir,
                    out error))
            {
                return false;
            }

            if (!Directory.Exists(sourceDir))
            {
                error = "Runtime package bootstrap is missing the staged build payload.";
                return false;
            }

            if (!RuntimeExecutionSecurityUtility.TryResolveContainedFilePath(
                    extractRoot,
                    manifest.installScriptPath,
                    ".ps1",
                    "The runtime install script",
                    allowSubdirectories: false,
                    out string installScriptPath,
                    out error))
            {
                return false;
            }

            if (!File.Exists(installScriptPath))
            {
                error = "Runtime package bootstrap is missing the install script.";
                return false;
            }

            string powershellPath = RuntimeExecutionSecurityUtility.ResolveWindowsSystemExecutablePath(Path.Combine("WindowsPowerShell", "v1.0", "powershell.exe"));
            if (string.IsNullOrWhiteSpace(powershellPath))
            {
                error = "Runtime package bootstrap could not locate powershell.exe in the Windows system directory.";
                return false;
            }

            startInfo = new ProcessStartInfo
            {
                FileName = powershellPath,
                Arguments =
                    $"-NoProfile -NonInteractive -ExecutionPolicy AllSigned -File \"{installScriptPath}\" -SourceDir \"{sourceDir}\" -InstallRoot \"{installRoot}\"",
                WorkingDirectory = Path.GetDirectoryName(installScriptPath) ?? extractRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            return true;
        }

        private static bool ExtractArchiveToDirectory(byte[] packageZipBytes, string extractRoot, out string error)
        {
            error = null;

            try
            {
                using var memory = new MemoryStream(packageZipBytes, writable: false);
                using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (!RuntimeExecutionSecurityUtility.TryResolveContainedDirectoryPath(
                            extractRoot,
                            entry.FullName,
                            "Runtime package entry",
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
                error = $"Runtime package bootstrap archive could not be extracted: {ex.Message}";
                return false;
            }
            catch (IOException ex)
            {
                error = $"Runtime package bootstrap archive could not be extracted: {ex.Message}";
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = $"Runtime package bootstrap archive could not be extracted: {ex.Message}";
                return false;
            }
        }

        private static string ComputeSha256Hex(byte[] data)
        {
            return RuntimeExecutionSecurityUtility.ComputeSha256Hex(data);
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
