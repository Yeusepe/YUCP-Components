using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class CouplingRuntimeShimService
    {
        internal static RuntimeStatus GetRuntimeStatus()
        {
            var status = new RuntimeStatus
            {
                installRoot = PackageManagerRuntimeSettings.ResolveRuntimeInstallRoot(),
            };

            if (!TryResolveActiveRuntime(out var runtime, out string resolutionError))
            {
                status.status = "missing";
                status.error = resolutionError;
                return status;
            }

            status.activeBuildId = runtime.activeBuildId;
            status.activeVersion = runtime.activeVersion;
            status.activePackageDir = runtime.activePackageDir;
            status.shimPath = runtime.shimPath;

            if (!TryProbe(runtime, out var probe, out string probeError))
            {
                status.status = "degraded";
                status.error = probeError;
                return status;
            }

            status.status = probe.health?.status ?? "healthy";
            status.activeBuildId = string.IsNullOrWhiteSpace(probe.buildId) ? status.activeBuildId : probe.buildId;
            status.activeVersion = string.IsNullOrWhiteSpace(probe.version) ? status.activeVersion : probe.version;
            status.supportsProtectedImport = probe.capabilities?.supportsProtectedImport ?? false;
            status.supportsSynchronousMaterialize = probe.capabilities?.supportsSynchronousMaterialize ?? false;
            status.hostProcessPath = probe.health?.hostProcessPath;
            return status;
        }

        internal static bool TryValidateProtectedMaterializationRuntime(out string error)
        {
            RuntimeStatus status = GetRuntimeStatus();
            if (string.Equals(status.status, "healthy", StringComparison.OrdinalIgnoreCase) &&
                status.supportsProtectedImport &&
                status.supportsSynchronousMaterialize)
            {
                error = null;
                return true;
            }

            error = BuildProtectedMaterializationRuntimeError(status);
            return false;
        }

        internal static bool TryMaterialize(string requestJson, out RuntimeMaterializeResponse response, out string error)
        {
            response = null;
            error = null;

            if (!TryValidateProtectedMaterializationRuntime(out error))
                return false;

            if (!TryResolveActiveRuntime(out var runtime, out error))
                return false;

            string tempPath = Path.Combine(Path.GetTempPath(), $"yucp-runtime-request-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(tempPath, requestJson ?? string.Empty, new UTF8Encoding(false));
                if (!TryInvokeShim(runtime.shimPath, $"materialize-json \"{tempPath}\"", out string stdout, out string stderr, out int exitCode, out error))
                    return false;

                if (exitCode != 0)
                {
                    error = string.IsNullOrWhiteSpace(stderr)
                        ? $"The package protection runtime failed (exit code {exitCode})."
                        : stderr.Trim();
                    return false;
                }

                response = JsonUtility.FromJson<RuntimeMaterializeResponse>(stdout);
                if (response == null)
                {
                    error = "The package protection runtime returned an invalid response.";
                    return false;
                }

                if (!response.success)
                {
                    error = string.IsNullOrWhiteSpace(response.error)
                        ? "The package protection runtime could not complete protected materialization on this machine."
                        : response.error;
                    return false;
                }

                return true;
            }
            catch (IOException ex)
            {
                error = $"The package protection runtime request could not be written: {ex.Message}";
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = $"The package protection runtime request could not be written: {ex.Message}";
                return false;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (IOException ex)
                    {
                        UnityEngine.Debug.LogWarning($"[YUCP Package Protection] Could not delete runtime request file '{tempPath}': {ex.Message}");
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        UnityEngine.Debug.LogWarning($"[YUCP Package Protection] Could not delete runtime request file '{tempPath}': {ex.Message}");
                    }
                }
            }
        }

        private static string BuildProtectedMaterializationRuntimeError(RuntimeStatus status)
        {
            string guidance = BuildRuntimeRepairGuidance(status?.installRoot);
            if (status == null)
            {
                return $"The package protection runtime is not ready for protected imports. {guidance}";
            }

            if (string.Equals(status.status, "healthy", StringComparison.OrdinalIgnoreCase))
            {
                return $"The installed package protection runtime does not support the protected materialization required by this package. {guidance}";
            }

            string reason = string.IsNullOrWhiteSpace(status.error)
                ? "The package protection runtime is not ready for protected imports."
                : status.error.Trim();
            return $"{reason} {guidance}".Trim();
        }

        private static string BuildRuntimeRepairGuidance(string installRoot)
        {
            string runtimeRootSegment = string.IsNullOrWhiteSpace(installRoot)
                ? string.Empty
                : $" Runtime root: {installRoot}.";
            return $"Install or repair the YUCP runtime package before importing protected payloads.{runtimeRootSegment} Open Project Settings > YUCP Package Manager to inspect the runtime status.";
        }

        private static bool TryResolveActiveRuntime(out ActiveRuntimeInstall runtime, out string error)
        {
            runtime = null;
            error = null;

            string installRoot = PackageManagerRuntimeSettings.ResolveRuntimeInstallRoot();
            string activeStatePath = Path.Combine(installRoot, "state", "active.json");
            if (!File.Exists(activeStatePath))
            {
                error = "The package protection runtime is not installed for this Windows user.";
                return false;
            }

            RuntimeActiveState activeState;
            try
            {
                activeState = JsonUtility.FromJson<RuntimeActiveState>(File.ReadAllText(activeStatePath));
            }
            catch (IOException ex)
            {
                error = $"The package protection runtime state could not be read: {ex.Message}";
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = $"The package protection runtime state could not be read: {ex.Message}";
                return false;
            }

            if (activeState == null ||
                string.IsNullOrWhiteSpace(activeState.activePackageDir) ||
                string.IsNullOrWhiteSpace(activeState.activeBuildId) ||
                string.IsNullOrWhiteSpace(activeState.activeVersion))
            {
                error = "The package protection runtime installation is incomplete.";
                return false;
            }

            string metadataPath = Path.Combine(activeState.activePackageDir, "CouplingRuntimeCom.metadata.json");
            if (!File.Exists(metadataPath))
            {
                error = "The package protection runtime metadata is missing from the active installation.";
                return false;
            }

            RuntimeMetadata metadata;
            try
            {
                metadata = JsonUtility.FromJson<RuntimeMetadata>(File.ReadAllText(metadataPath));
            }
            catch (IOException ex)
            {
                error = $"The package protection runtime metadata could not be read: {ex.Message}";
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = $"The package protection runtime metadata could not be read: {ex.Message}";
                return false;
            }

            if (metadata == null || string.IsNullOrWhiteSpace(metadata.clientName))
            {
                error = "The package protection runtime metadata is missing the activation shim.";
                return false;
            }

            string shimPath = Path.Combine(activeState.activePackageDir, metadata.clientName);
            if (!File.Exists(shimPath))
            {
                error = "The package protection runtime activation shim is missing from the active installation.";
                return false;
            }

            runtime = new ActiveRuntimeInstall
            {
                activeBuildId = activeState.activeBuildId,
                activeVersion = activeState.activeVersion,
                activePackageDir = activeState.activePackageDir,
                shimPath = shimPath,
            };
            return true;
        }

        private static bool TryProbe(ActiveRuntimeInstall runtime, out RuntimeProbeResponse response, out string error)
        {
            response = null;
            if (!TryInvokeShim(runtime.shimPath, "probe-json", out string stdout, out string stderr, out int exitCode, out error))
                return false;

            if (exitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(stderr)
                    ? $"The package protection runtime probe failed (exit code {exitCode})."
                    : stderr.Trim();
                return false;
            }

            response = JsonUtility.FromJson<RuntimeProbeResponse>(stdout);
            if (response == null)
            {
                error = "The package protection runtime probe returned an invalid response.";
                return false;
            }

            return true;
        }

        private static bool TryInvokeShim(
            string shimPath,
            string arguments,
            out string stdout,
            out string stderr,
            out int exitCode,
            out string error)
        {
            stdout = string.Empty;
            stderr = string.Empty;
            exitCode = -1;
            error = null;

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = shimPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    }
                };

                if (!process.Start())
                {
                    error = "The package protection runtime could not be started.";
                    return false;
                }

                stdout = process.StandardOutput.ReadToEnd();
                stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                error = $"The package protection runtime could not be started: {ex.Message}";
                return false;
            }
            catch (Win32Exception ex)
            {
                error = $"The package protection runtime could not be started: {ex.Message}";
                return false;
            }
        }

        [Serializable]
        internal sealed class RuntimeStatus
        {
            public string status;
            public string error;
            public string installRoot;
            public string activeBuildId;
            public string activeVersion;
            public string activePackageDir;
            public string shimPath;
            public string hostProcessPath;
            public bool supportsProtectedImport;
            public bool supportsSynchronousMaterialize;
        }

        [Serializable]
        internal sealed class RuntimeMaterializeResponse
        {
            public bool success;
            public string error;
            public string buildId;
            public string hostProcessPath;
            public string[] materializedAssetPaths;
        }

        [Serializable]
        private sealed class RuntimeActiveState
        {
            public string activeBuildId;
            public string activeVersion;
            public string activePackageDir;
        }

        [Serializable]
        private sealed class RuntimeMetadata
        {
            public string clientName;
        }

        [Serializable]
        private sealed class RuntimeProbeResponse
        {
            public string version;
            public string buildId;
            public RuntimeCapabilities capabilities;
            public RuntimeHealth health;
        }

        [Serializable]
        private sealed class RuntimeCapabilities
        {
            public string mode;
            public bool supportsProtectedImport;
            public bool supportsSynchronousMaterialize;
        }

        [Serializable]
        private sealed class RuntimeHealth
        {
            public string status;
            public string version;
            public string buildId;
            public string hostProcessPath;
            public string hostProcessName;
            public string registrationScope;
            public string activation;
        }

        private sealed class ActiveRuntimeInstall
        {
            public string activeBuildId;
            public string activeVersion;
            public string activePackageDir;
            public string shimPath;
        }
    }
}
