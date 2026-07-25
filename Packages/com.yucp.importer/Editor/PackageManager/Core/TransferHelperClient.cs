using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using UnityEditor.PackageManager;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    [Serializable]
    internal sealed class TransferHelperDeviceInfo
    {
        public string deviceKeyThumbprint = string.Empty;
        public int schemaVersion;
    }

    [Serializable]
    internal sealed class TransferHelperUpdateResult
    {
        public long byteLength;
        public bool cached;
        public string errorCode = string.Empty;
        public string message = string.Empty;
        public string path = string.Empty;
        public string sha256 = string.Empty;
        public string status = string.Empty;
        public string target = string.Empty;
        public string traceId = string.Empty;
    }

    [Serializable]
    internal sealed class TransferHelperRequest
    {
        public int schemaVersion = 2;
        public string runId = string.Empty;
        public string operation = string.Empty;
        public string projectPath = string.Empty;
        public string stateRoot = string.Empty;
        public string resultPath = string.Empty;
        public string aliasId = string.Empty;
        public string idempotencyKey = string.Empty;
        public string expectedCurrentReleaseRoot = string.Empty;
        public string targetReleaseRoot = string.Empty;
        public string approvedActiveContentDigest = string.Empty;
        public string approvedPolicyVersion = string.Empty;
        public string installSession = string.Empty;
        public string deliveryGrant = string.Empty;
        public string tufMetadataUrl = string.Empty;
        public string tufRootPath = string.Empty;
        public string tufTargetsUrl = string.Empty;
        public string tufTrustTarget = string.Empty;
    }

    [Serializable]
    internal sealed class TransferHelperFile
    {
        public long bytes;
        public string normalizedPath = string.Empty;
        public string sha256 = string.Empty;
    }

    [Serializable]
    internal sealed class TransferHelperResult
    {
        public string activeContentDigest = string.Empty;
        public string activePolicyVersion = string.Empty;
        public string errorCode = string.Empty;
        public string errorMessage = string.Empty;
        public int exitCode;
        public List<TransferHelperFile> files = new List<TransferHelperFile>();
        public string journalState = string.Empty;
        public long logicalBytes;
        public int logicalFiles;
        public string operation = string.Empty;
        public string receiptId = string.Empty;
        public string receiptPath = string.Empty;
        public string runId = string.Empty;
        public int schemaVersion;
        public string stagingTree = string.Empty;
        public string status = string.Empty;
        public string targetReleaseRoot = string.Empty;
        public string traceId = string.Empty;
        public string versionId = string.Empty;
    }

    internal static class TransferHelperClient
    {
        internal const string StateRootEnvironmentVariable =
            "YUCP_PACKAGE_DELIVERY_STATE_ROOT";
        private const string HelperRelativePath =
            "Editor/PackageManager/Native/windows-x64/yucp-transfer-helper.exe";
        private const string HelperTarget =
            "helper/windows-amd64/yucp-transfer-helper.exe";
        private const string TrustRootRelativePath =
            "Editor/PackageManager/Trust/1.root.json";

        internal static TransferHelperDeviceInfo ReadDeviceInfo(
            string stateRoot,
            Uri server)
        {
            string resolvedStateRoot = RequireAbsolutePath(stateRoot, "state root");
            Directory.CreateDirectory(resolvedStateRoot);
            ProcessResult process = Run(
                ResolveUpdatedHelperPath(server, resolvedStateRoot),
                $"device-info --state-root {Quote(resolvedStateRoot)}",
                null);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "The package delivery helper could not create a secure device identity.");
            }
            TransferHelperDeviceInfo result =
                JsonConvert.DeserializeObject<TransferHelperDeviceInfo>(process.StandardOutput);
            if (result == null ||
                result.schemaVersion != 1 ||
                !IsSha256(result.deviceKeyThumbprint))
            {
                throw new InvalidDataException(
                    "The package delivery helper returned invalid device metadata.");
            }
            return result;
        }

        internal static TransferHelperResult Execute(TransferHelperRequest request)
        {
            ValidateRequest(request);
            string requestJson = JsonConvert.SerializeObject(request, Formatting.None);
            ProcessResult process = Run(
                ResolveUpdatedHelperPath(
                    RequireCommonTufOrigin(request),
                    request.stateRoot),
                "execute",
                requestJson);
            if (!File.Exists(request.resultPath))
            {
                throw new InvalidDataException(
                    "The package delivery helper did not commit a terminal result.");
            }
            TransferHelperResult result =
                JsonConvert.DeserializeObject<TransferHelperResult>(
                    File.ReadAllText(request.resultPath));
            if (result == null ||
                result.schemaVersion != 2 ||
                !string.Equals(result.runId, request.runId, StringComparison.Ordinal) ||
                !string.Equals(result.operation, request.operation, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The package delivery helper returned an invalid terminal result.");
            }
            if (process.ExitCode != 0 ||
                !string.Equals(result.status, "succeeded", StringComparison.Ordinal) ||
                result.exitCode != 0)
            {
                string code = string.IsNullOrWhiteSpace(result.errorCode)
                    ? "HELPER_FAILED"
                    : result.errorCode;
                throw new InvalidOperationException(
                    $"Package delivery failed with stable error code {code}.");
            }
            return result;
        }

        internal static string ResolveStateRoot()
        {
            string localData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            string configured = Environment.GetEnvironmentVariable(
                StateRootEnvironmentVariable);
            return ResolveStateRoot(localData, configured);
        }

        internal static string ResolveStateRoot(
            string localData,
            string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (!Path.IsPathRooted(configured))
                {
                    throw new InvalidOperationException(
                        "The package delivery state root must be absolute.");
                }
                return Path.GetFullPath(configured);
            }
            if (string.IsNullOrWhiteSpace(localData))
            {
                throw new InvalidOperationException(
                    "The local application data directory is unavailable.");
            }
            return Path.GetFullPath(Path.Combine(localData, "YUCP", "PackageDelivery"));
        }

        internal static string ResolveTrustRootPath()
        {
            string packageRoot = ResolvePackageRoot();
            string trustRootPath = Path.GetFullPath(
                Path.Combine(
                    packageRoot,
                    TrustRootRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            string boundary = packageRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!trustRootPath.StartsWith(boundary, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(trustRootPath))
            {
                throw new FileNotFoundException(
                    "The pinned package installer trust root is unavailable.");
            }
            return trustRootPath;
        }

        private static void ValidateRequest(TransferHelperRequest request)
        {
            if (request == null ||
                request.schemaVersion != 2 ||
                string.IsNullOrWhiteSpace(request.runId) ||
                string.IsNullOrWhiteSpace(request.operation) ||
                !Path.IsPathRooted(request.projectPath) ||
                !Path.IsPathRooted(request.stateRoot) ||
                !Path.IsPathRooted(request.resultPath) ||
                !Path.IsPathRooted(request.tufRootPath) ||
                !IsSha256(request.expectedCurrentReleaseRoot) ||
                !IsSha256(request.targetReleaseRoot) ||
                string.IsNullOrWhiteSpace(request.installSession) ||
                string.IsNullOrWhiteSpace(request.deliveryGrant))
            {
                throw new InvalidOperationException(
                    "The package delivery lifecycle request is invalid.");
            }
        }

        private static string ResolveUpdatedHelperPath(
            Uri server,
            string stateRoot)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            string resolvedStateRoot = RequireAbsolutePath(stateRoot, "state root");
            string destination = Path.GetFullPath(
                Path.Combine(
                    resolvedStateRoot,
                    "helper",
                    "current",
                    "yucp-transfer-helper.exe"));
            string metadataCache = Path.GetFullPath(
                Path.Combine(resolvedStateRoot, "helper", "metadata"));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            Directory.CreateDirectory(metadataCache);
            string traceId = Guid.NewGuid().ToString("N");
            string arguments =
                $"update --root {Quote(ResolveTrustRootPath())} " +
                $"--metadata-url {Quote(new Uri(server, "/api/v2/package-installer/tuf/metadata/").ToString())} " +
                $"--targets-url {Quote(new Uri(server, "/api/v2/package-installer/tuf/targets/").ToString())} " +
                $"--metadata-cache {Quote(metadataCache)} " +
                $"--target {Quote(HelperTarget)} " +
                $"--destination {Quote(destination)} " +
                $"--trace-id {Quote(traceId)}";
            ProcessResult process = Run(ResolveBootstrapHelperPath(), arguments, null);
            TransferHelperUpdateResult result =
                JsonConvert.DeserializeObject<TransferHelperUpdateResult>(
                    process.StandardOutput);
            if (process.ExitCode != 0 ||
                result == null ||
                !string.Equals(result.status, "OK", StringComparison.Ordinal) ||
                !string.Equals(result.target, HelperTarget, StringComparison.Ordinal) ||
                !string.Equals(result.traceId, traceId, StringComparison.Ordinal) ||
                !string.Equals(
                    Path.GetFullPath(result.path ?? string.Empty),
                    destination,
                    StringComparison.OrdinalIgnoreCase) ||
                !IsSha256(result.sha256) ||
                result.byteLength < 1 ||
                !File.Exists(destination))
            {
                throw new InvalidOperationException(
                    "The signed package delivery helper update failed.");
            }
            return destination;
        }

        private static Uri RequireCommonTufOrigin(TransferHelperRequest request)
        {
            Uri metadata = RequireSecureOrLoopbackUri(
                request.tufMetadataUrl,
                "TUF metadata");
            Uri targets = RequireSecureOrLoopbackUri(
                request.tufTargetsUrl,
                "TUF targets");
            if (!string.Equals(
                metadata.GetLeftPart(UriPartial.Authority),
                targets.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The TUF metadata and target origins must match.");
            }
            return new Uri(metadata.GetLeftPart(UriPartial.Authority) + "/");
        }

        private static Uri RequireSecureOrLoopbackUri(string value, string name)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttps &&
                    !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new InvalidOperationException(
                    $"The {name} URL must use HTTPS or loopback HTTP.");
            }
            return uri;
        }

        private static ProcessResult Run(
            string helperPath,
            string arguments,
            string standardInput)
        {
            var startInfo = new ProcessStartInfo
            {
                Arguments = arguments,
                CreateNoWindow = true,
                FileName = helperPath,
                RedirectStandardError = true,
                RedirectStandardInput = standardInput != null,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException(
                        "The package delivery helper did not start.");
                }
                if (standardInput != null)
                {
                    process.StandardInput.Write(standardInput);
                    process.StandardInput.Close();
                }
                string standardOutput = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return new ProcessResult
                {
                    ExitCode = process.ExitCode,
                    StandardError = standardError,
                    StandardOutput = standardOutput,
                };
            }
        }

        private static string ResolveBootstrapHelperPath()
        {
            string packageRoot = ResolvePackageRoot();
            string helperPath = Path.GetFullPath(
                Path.Combine(
                    packageRoot,
                    HelperRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            string boundary = packageRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!helperPath.StartsWith(boundary, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(helperPath))
            {
                throw new FileNotFoundException(
                    "The signed package delivery helper is unavailable.");
            }
            return helperPath;
        }

        private static string ResolvePackageRoot()
        {
            PackageInfo package = PackageInfo.FindForAssembly(
                typeof(TransferHelperClient).Assembly);
            if (package == null || string.IsNullOrWhiteSpace(package.resolvedPath))
            {
                throw new InvalidOperationException(
                    "The importer package path is unavailable.");
            }
            string packageRoot = Path.GetFullPath(package.resolvedPath);
            return packageRoot;
        }

        private static string RequireAbsolutePath(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
            {
                throw new InvalidOperationException($"The {name} must be absolute.");
            }
            return Path.GetFullPath(value);
        }

        private static bool IsSha256(string value)
        {
            return value != null &&
                value.Length == 64 &&
                value.All(character =>
                    character >= '0' && character <= '9' ||
                    character >= 'a' && character <= 'f');
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private sealed class ProcessResult
        {
            internal int ExitCode;
            internal string StandardError;
            internal string StandardOutput;
        }
    }
}
