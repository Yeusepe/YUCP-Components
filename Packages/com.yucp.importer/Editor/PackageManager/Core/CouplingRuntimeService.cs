using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class CouplingRuntimeService
    {
        private const int RequestTimeoutSeconds = 30;

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".bmp" };

        private static bool IsImageExtension(string extension)
        {
            foreach (string ext in ImageExtensions)
            {
                if (string.Equals(extension, ext, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

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
                if (!IsImageExtension(extension) &&
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

            var jobByAssetPath = new Dictionary<string, CouplingJobEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in jobEntries)
            {
                // The server-issued seed is required to place a v2 mark; entries without one are skipped.
                if (entry == null || string.IsNullOrEmpty(entry.assetPath) ||
                    string.IsNullOrEmpty(entry.tokenHex) || string.IsNullOrEmpty(entry.seedHex))
                {
                    continue;
                }

                jobByAssetPath[NormalizeAssetPath(entry.assetPath)] = entry;
            }

            var work = new List<CouplingApply>();
            foreach (var candidate in candidates)
            {
                if (!jobByAssetPath.TryGetValue(candidate.assetPath, out CouplingJobEntry entry))
                {
                    continue;
                }

                // Candidates are already restricted to image/.fbx in CollectCandidates, so a
                // non-image entry here is always .fbx.
                bool isImage = IsImageExtension(Path.GetExtension(candidate.assetPath));
                work.Add(new CouplingApply
                {
                    fullPath = candidate.fullPath,
                    tokenHex = entry.tokenHex,
                    seedHex = entry.seedHex,
                    isImage = isImage,
                });
            }

            if (work.Count == 0)
            {
                return true;
            }

            string tempRoot = Path.Combine(Path.GetTempPath(), $"YUCPWm_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);

            string runtimePath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}.dll");
            IntPtr module = IntPtr.Zero;
            FileStream lockedRuntime = null;

            try
            {
                // Write the verified bytes to an unpredictable per-process path, then pin them with a
                // READ-ONLY lock (deny writers, allow the loader to map the image) for the whole mapped
                // lifetime. A writable lock makes LoadLibrary fail with ERROR_SHARING_VIOLATION (win32 32),
                // which previously broke coupling on every install. Released in finally.
                File.WriteAllBytes(runtimePath, runtimeBytes);
                lockedRuntime = new FileStream(
                    runtimePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

                module = LoadLibrary(runtimePath);
                if (module == IntPtr.Zero)
                {
                    error = "The package protection step could not be completed on this machine.";
                    return false;
                }

                IntPtr imageProc = GetProcAddress(module, "xg_0122");
                IntPtr fbxProc = GetProcAddress(module, "xg_0124");
                if (imageProc == IntPtr.Zero || fbxProc == IntPtr.Zero)
                {
                    error = "The package protection step could not be completed on this machine.";
                    return false;
                }

                var applyImage = Marshal.GetDelegateForFunctionPointer<CouplingApplyDelegate>(imageProc);
                var applyFbx = Marshal.GetDelegateForFunctionPointer<CouplingApplyDelegate>(fbxProc);

                int failed = 0;
                foreach (var item in work)
                {
                    CouplingApplyDelegate apply = item.isImage ? applyImage : applyFbx;
                    if (apply(Utf8Z(item.fullPath), Utf8Z(item.tokenHex), Utf8Z(item.seedHex)) < 0)
                    {
                        failed++;
                    }
                }

                if (failed > 0)
                {
                    error = "The package protection step could not be completed on this machine.";
                    return false;
                }

                return true;
            }
            catch (Exception)
            {
                error = "The package protection step could not be completed on this machine.";
                return false;
            }
            finally
            {
                if (module != IntPtr.Zero)
                {
                    FreeLibrary(module);
                }

                lockedRuntime?.Dispose();

                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, true);
                    }
                }
                catch
                {
                    // Best-effort temp cleanup.
                }
            }
        }

        private static byte[] Utf8Z(string value)
        {
            return Encoding.UTF8.GetBytes((value ?? string.Empty) + "\0");
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? null
                : path.Replace('\\', '/').Trim().TrimStart('/');
        }

        private static string ComputeSha256(byte[] data)
        {
            return RuntimeExecutionSecurityUtility.ComputeSha256Hex(data);
        }

        // Native entry signature: (UTF-8 path, UTF-8 token, UTF-8 per-job seed) -> status int
        // (negative = failure). The seed (not a baked key) is what places the mark.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int CouplingApplyDelegate(byte[] filePathUtf8, byte[] tokenHexUtf8, byte[] seedHexUtf8);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr hModule);

        private sealed class CouplingApply
        {
            public string fullPath;
            public string tokenHex;
            public string seedHex;
            public bool isImage;
        }

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
            public string seedHex;
        }

        private sealed class CouplingCandidate
        {
            public string assetPath;
            public string fullPath;
        }
    }
}
