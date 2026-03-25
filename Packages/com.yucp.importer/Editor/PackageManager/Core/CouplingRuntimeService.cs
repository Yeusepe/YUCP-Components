using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class CouplingRuntimeService
    {
        private const int RequestTimeoutSeconds = 30;
        private const int RuntimeTimeoutMilliseconds = 300000;

        public static bool TryApplyCoupling(string packageId, IReadOnlyList<string> installedFiles, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(packageId) || installedFiles == null || installedFiles.Count == 0)
            {
                return true;
            }

            string licenseToken = LicenseTokenCache.GetValidToken(packageId);
            if (string.IsNullOrEmpty(licenseToken))
            {
                return true;
            }

            List<CouplingCandidate> candidates = CollectCandidates(installedFiles);
            if (candidates.Count == 0)
            {
                return true;
            }

            string projectId = ProjectIdentityService.GetOrCreateProjectId();
            if (string.IsNullOrEmpty(projectId))
            {
                error = "Could not create the Unity project identity required for coupling.";
                return false;
            }

            string serverUrl = LicenseServerResolver.GetLicenseServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                error = "Could not resolve the YUCP license server required for coupling.";
                return false;
            }

            string machineFingerprint = MachineFingerprintService.GetFingerprint();
            if (!TryRequestCouplingJob(
                    serverUrl,
                    packageId,
                    projectId,
                    machineFingerprint,
                    licenseToken,
                    candidates,
                    out var jobResponse,
                    out error))
            {
                return false;
            }

            if (jobResponse?.files == null || jobResponse.files.Count == 0)
            {
                if (!string.IsNullOrEmpty(jobResponse?.skipReason))
                {
                    Debug.Log($"[YUCP Coupling] Skipping coupling for packageId='{packageId}': {DescribeSkipReason(jobResponse.skipReason)}");
                }

                return true;
            }

            if (!TryDownloadRuntime(serverUrl, jobResponse.runtimeToken, jobResponse.runtimeSha256, out byte[] runtimeBytes, out error))
            {
                return false;
            }

            return TryRunRuntime(runtimeBytes, candidates, jobResponse.files, out error);
        }

        private static List<CouplingCandidate> CollectCandidates(IReadOnlyList<string> installedFiles)
        {
            var candidates = new List<CouplingCandidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            foreach (string installedFile in installedFiles)
            {
                string assetPath = NormalizeAssetPath(installedFile);
                if (string.IsNullOrEmpty(assetPath) || !seen.Add(assetPath))
                {
                    continue;
                }

                string extension = Path.GetExtension(assetPath);
                if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(extension, ".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                candidates.Add(new CouplingCandidate
                {
                    assetPath = assetPath,
                    fullPath = fullPath,
                });
            }

            return candidates;
        }

        private static bool TryRequestCouplingJob(
            string serverUrl,
            string packageId,
            string projectId,
            string machineFingerprint,
            string licenseToken,
            List<CouplingCandidate> candidates,
            out CouplingJobResponse response,
            out string error)
        {
            response = null;
            error = null;

            var requestBody = new CouplingJobRequest
            {
                packageId = packageId,
                projectId = projectId,
                machineFingerprint = machineFingerprint,
                licenseToken = licenseToken,
                assetPaths = candidates.ConvertAll(candidate => candidate.assetPath),
            };

            string bodyJson = JsonUtility.ToJson(requestBody);
            using var request = new UnityWebRequest($"{serverUrl.TrimEnd('/')}/v1/licenses/coupling-job", "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(bodyJson));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = RequestTimeoutSeconds;
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
                response = JsonUtility.FromJson<CouplingJobResponse>(responseText);
                error = !string.IsNullOrEmpty(response?.error)
                    ? response.error
                    : $"Coupling job request failed (HTTP {request.responseCode}).";
                return false;
            }

            response = JsonUtility.FromJson<CouplingJobResponse>(responseText);
            if (response == null)
            {
                error = "Coupling job request returned an invalid response.";
                return false;
            }

            if (response.files == null || response.files.Count == 0)
            {
                return true;
            }

            if (string.IsNullOrEmpty(response.runtimeToken) || string.IsNullOrEmpty(response.runtimeSha256))
            {
                error = "Coupling job request returned an invalid response.";
                return false;
            }

            return true;
        }

        private static bool TryDownloadRuntime(
            string serverUrl,
            string runtimeToken,
            string expectedSha256,
            out byte[] runtimeBytes,
            out string error)
        {
            runtimeBytes = null;
            error = null;

            if (string.IsNullOrEmpty(runtimeToken))
            {
                error = "Coupling runtime token is missing.";
                return false;
            }

            string url = $"{serverUrl.TrimEnd('/')}/v1/licenses/coupling-runtime?token={UnityWebRequest.EscapeURL(runtimeToken)}";
            using var request = UnityWebRequest.Get(url);
            request.timeout = RequestTimeoutSeconds;
            request.SetRequestHeader("Accept-Encoding", "identity");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                Thread.Sleep(20);
            }

            if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
            {
                string responseText = request.downloadHandler?.text ?? string.Empty;
                var failure = JsonUtility.FromJson<CouplingJobResponse>(responseText);
                error = !string.IsNullOrEmpty(failure?.error)
                    ? failure.error
                    : $"Coupling runtime download failed (HTTP {request.responseCode}).";
                return false;
            }

            runtimeBytes = request.downloadHandler?.data;
            if (runtimeBytes == null || runtimeBytes.Length == 0)
            {
                error = "Coupling runtime download returned no data.";
                return false;
            }

            string actualSha256 = ComputeSha256(runtimeBytes);
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                error = "Coupling runtime download failed integrity verification.";
                runtimeBytes = null;
                return false;
            }

            return true;
        }

        private static bool TryRunRuntime(
            byte[] runtimeBytes,
            List<CouplingCandidate> candidates,
            List<CouplingJobEntry> jobEntries,
            out string error)
        {
            error = null;

            var tokenByAssetPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in jobEntries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.assetPath) || string.IsNullOrEmpty(entry.tokenHex))
                {
                    continue;
                }

                tokenByAssetPath[NormalizeAssetPath(entry.assetPath)] = entry.tokenHex;
            }

            var manifestLines = new List<string>();
            foreach (var candidate in candidates)
            {
                if (!tokenByAssetPath.TryGetValue(candidate.assetPath, out string tokenHex))
                {
                    continue;
                }

                manifestLines.Add($"FILE|{candidate.fullPath}|{tokenHex}");
            }

            if (manifestLines.Count == 0)
            {
                return true;
            }

            string tempRoot = Path.Combine(Path.GetTempPath(), $"YUCPWm_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);

            string runtimePath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}.dll");
            string manifestPath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}.dat");
            string resultPath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}.dat");

            try
            {
                File.WriteAllBytes(runtimePath, runtimeBytes);
                manifestLines.Insert(0, $"RESULT|{resultPath}");
                File.WriteAllLines(manifestPath, manifestLines, new UTF8Encoding(false));

                string launchRuntimePath = GetShortPathIfAvailable(runtimePath);
                string launchManifestPath = GetShortPathIfAvailable(manifestPath);
                string rundll32Path = Path.Combine(Environment.SystemDirectory, "rundll32.exe");

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = rundll32Path,
                    Arguments = $"\"{launchRuntimePath}\",EntryPoint \"{launchManifestPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    error = "Could not start the coupling runtime process.";
                    return false;
                }

                if (!process.WaitForExit(RuntimeTimeoutMilliseconds))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }

                    error = "Coupling runtime timed out.";
                    return false;
                }

                if (!File.Exists(resultPath))
                {
                    error = $"Coupling runtime exited with code {process.ExitCode} without producing a result file.";
                    return false;
                }

                string resultText = File.ReadAllText(resultPath);
                if (process.ExitCode != 0 || resultText.IndexOf("status=ok", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    error = $"Coupling runtime failed: {resultText}";
                    return false;
                }

                return true;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, true);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[YUCP Coupling] Failed to clean up temp runtime files: {ex.Message}");
                }
            }
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? null
                : path.Replace('\\', '/').Trim().TrimStart('/');
        }

        private static string ComputeSha256(byte[] data)
        {
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(data);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
            {
                builder.Append(b.ToString("x2"));
            }

            return builder.ToString();
        }

        private static string DescribeSkipReason(string skipReason)
        {
            return string.Equals(skipReason, "capability_disabled", StringComparison.OrdinalIgnoreCase)
                ? "creator plan does not include coupling traceability"
                : skipReason;
        }

        private static string GetShortPathIfAvailable(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            var builder = new StringBuilder(512);
            uint result = GetShortPathName(path, builder, (uint)builder.Capacity);
            return result > 0 && result < builder.Capacity ? builder.ToString() : path;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, uint cchBuffer);

        [Serializable]
        private class CouplingJobRequest
        {
            public string packageId;
            public string projectId;
            public string machineFingerprint;
            public string licenseToken;
            public List<string> assetPaths = new List<string>();
        }

        [Serializable]
        private class CouplingJobResponse
        {
            public bool success;
            public string runtimeToken;
            public string runtimeSha256;
            public long expiresAt;
            public string skipReason;
            public string error;
            public List<CouplingJobEntry> files = new List<CouplingJobEntry>();
        }

        [Serializable]
        private class CouplingJobEntry
        {
            public string assetPath;
            public string tokenHex;
        }

        private sealed class CouplingCandidate
        {
            public string assetPath;
            public string fullPath;
        }
    }
}
