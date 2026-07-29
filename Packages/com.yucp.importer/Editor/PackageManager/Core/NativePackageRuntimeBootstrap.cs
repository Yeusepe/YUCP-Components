using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.PackageManager;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal interface INativePackageRuntimeBootstrap
    {
        Task EnsureAsync(
            string traceId,
            CancellationToken cancellationToken);
    }

    internal sealed class NativePackageRuntimeInvocation
    {
        internal string arguments = string.Empty;
        internal string executablePath = string.Empty;
        internal string installRoot = string.Empty;
        internal string stateRoot = string.Empty;
    }

    internal sealed class NativePackageRuntimeBootstrapException : Exception
    {
        internal NativePackageRuntimeBootstrapException(
            string errorCode,
            string message)
            : base(message)
        {
            ErrorCode = errorCode ?? string.Empty;
        }

        internal string ErrorCode { get; }
    }

    internal sealed class PackagedNativePackageRuntimeBootstrap
        : INativePackageRuntimeBootstrap
    {
        internal const string RuntimeExecutableRelativePath =
            "Editor/PackageManager/Runtime/Windows/x64/" +
            "yucp-transfer-helper.exe";
        internal const string TrustedRootRelativePath =
            "Editor/PackageManager/Trust/1.root.json";
        private const int MaximumBootstrapBytes = 256 * 1024 * 1024;
        private const int MaximumOutputCharacters = 64 * 1024;
        private const int MaximumTrustedRootBytes = 512 * 1024;
        private const int OverallTimeoutMilliseconds = 120 * 1000;

        public async Task EnsureAsync(
            string traceId,
            CancellationToken cancellationToken)
        {
            PackageInfo package = PackageInfo.FindForAssembly(
                typeof(PackagedNativePackageRuntimeBootstrap).Assembly);
            if (package == null ||
                string.IsNullOrWhiteSpace(package.resolvedPath))
            {
                throw new InvalidDataException(
                    "The package runtime bootstrap is unavailable.");
            }
            string localApplicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            bool isWindowsX64 = IsWindowsX64();
            NativePackageRuntimeInvocation invocation =
                BuildInvocation(
                    package.resolvedPath,
                    localApplicationData,
                    isWindowsX64,
                    traceId,
                    LoadProductionTrust(isWindowsX64));
            await RunAsync(invocation, traceId, cancellationToken);
        }

        internal static NativePackageRuntimeInvocation
            BuildInvocationForTests(
                string packageRoot,
                string localApplicationData,
                bool isWindowsX64,
                string traceId)
        {
            NativePackageRuntimeTrust trust =
                LoadProductionTrust(isWindowsX64);
            return BuildInvocation(
                packageRoot,
                localApplicationData,
                isWindowsX64,
                traceId,
                trust);
        }

        internal static NativePackageRuntimeInvocation
            BuildInvocationForTests(
                string packageRoot,
                string localApplicationData,
                bool isWindowsX64,
                string traceId,
                NativePackageRuntimeTrust trust)
        {
            return BuildInvocation(
                packageRoot,
                localApplicationData,
                isWindowsX64,
                traceId,
                trust);
        }

        private static NativePackageRuntimeInvocation BuildInvocation(
            string packageRoot,
            string localApplicationData,
            bool isWindowsX64,
            string traceId,
            NativePackageRuntimeTrust trust)
        {
            if (!isWindowsX64)
            {
                throw new PlatformNotSupportedException(
                    "Package installation requires a Windows x64 Unity Editor.");
            }
            if (string.IsNullOrWhiteSpace(packageRoot) ||
                string.IsNullOrWhiteSpace(localApplicationData) ||
                !Path.IsPathRooted(packageRoot) ||
                !Path.IsPathRooted(localApplicationData) ||
                trust == null)
            {
                throw new InvalidDataException(
                    "The package runtime bootstrap configuration is invalid.");
            }

            string exactPackageRoot = Path.GetFullPath(packageRoot);
            string executablePath = RequirePackageFile(
                exactPackageRoot,
                RuntimeExecutableRelativePath,
                MaximumBootstrapBytes);
            string trustedRootPath = RequirePackageFile(
                exactPackageRoot,
                TrustedRootRelativePath,
                MaximumTrustedRootBytes);
            RequirePinnedHash(
                executablePath,
                trust.executableSha256);
            RequirePinnedHash(
                trustedRootPath,
                trust.trustedRootSha256);
            ValidateTrustedRoot(trustedRootPath);
            trust.publisherVerifier.Verify(
                executablePath,
                trust.publisherSubject,
                trust.publisherCertificateSha256,
                trust.publisherTrustMode);

            string runtimeRoot = Path.GetFullPath(
                Path.Combine(
                    localApplicationData,
                    "YUCP",
                    "PackageDelivery"));
            string installRoot = Path.Combine(runtimeRoot, "runtime");
            string stateRoot = Path.Combine(runtimeRoot, "state");
            var arguments = new StringBuilder();
            AppendArgument(arguments, "runtime-ensure");
            AppendOption(arguments, "--root", trustedRootPath);
            AppendOption(
                arguments,
                "--metadata-url",
                trust.metadataUrl);
            AppendOption(
                arguments,
                "--targets-url",
                trust.targetsUrl);
            AppendOption(arguments, "--install-root", installRoot);
            AppendOption(arguments, "--state-root", stateRoot);
            AppendOption(arguments, "--http-timeout", "30s");
            AppendOption(arguments, "--startup-timeout", "20s");
            if (!string.IsNullOrWhiteSpace(traceId))
            {
                if (!IsTraceId(traceId))
                {
                    throw new InvalidDataException(
                        "The package runtime trace identifier is invalid.");
                }
                AppendOption(arguments, "--trace-id", traceId);
            }

            return new NativePackageRuntimeInvocation
            {
                arguments = arguments.ToString(),
                executablePath = executablePath,
                installRoot = installRoot,
                stateRoot = stateRoot,
            };
        }

        private static NativePackageRuntimeTrust LoadProductionTrust(
            bool isWindowsX64)
        {
            if (!isWindowsX64)
            {
                throw new PlatformNotSupportedException(
                    "Package installation requires a Windows x64 Unity Editor.");
            }
            return NativePackageRuntimeReleaseTrust.Load();
        }

        private static async Task RunAsync(
            NativePackageRuntimeInvocation invocation,
            string traceId,
            CancellationToken cancellationToken)
        {
            var start = new ProcessStartInfo
            {
                Arguments = invocation.arguments,
                CreateNoWindow = true,
                FileName = invocation.executablePath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(
                    invocation.executablePath),
            };
            RestrictEnvironment(start);
            using (var process = new Process { StartInfo = start })
            {
                try
                {
                    if (!process.Start())
                    {
                        throw new InvalidOperationException(
                            "Secure package delivery setup could not start.");
                    }
                }
                catch (Exception failure)
                    when (!(failure is InvalidOperationException))
                {
                    throw new InvalidOperationException(
                        "Secure package delivery setup could not start.",
                        failure);
                }

                Task<BoundedText> output = ReadBoundedAsync(
                    process.StandardOutput,
                    MaximumOutputCharacters);
                Task<BoundedText> error = ReadBoundedAsync(
                    process.StandardError,
                    MaximumOutputCharacters);
                bool exited;
                using (cancellationToken.Register(() =>
                    TryTerminate(process)))
                {
                    exited = await Task.Run(() =>
                        process.WaitForExit(OverallTimeoutMilliseconds));
                    if (!exited)
                    {
                        TryTerminate(process);
                    }
                    await Task.WhenAll(output, error);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (!exited)
                {
                    throw new TimeoutException(
                        "Secure package delivery setup timed out.");
                }
                if (output.Result.exceeded || error.Result.exceeded)
                {
                    throw new InvalidDataException(
                        "The package runtime bootstrap result is too large.");
                }

                NativePackageRuntimeResult result =
                    ParseResult(output.Result.value);
                ValidateResult(
                    result,
                    process.ExitCode,
                    traceId);
            }
        }

        internal static void ValidateResultForTests(
            string json,
            int exitCode,
            string traceId)
        {
            ValidateResult(ParseResult(json), exitCode, traceId);
        }

        private static void ValidateResult(
            NativePackageRuntimeResult result,
            int exitCode,
            string traceId)
        {
            if (exitCode != 0 ||
                string.Equals(
                    result.status,
                    "ERROR",
                    StringComparison.Ordinal))
            {
                if (!string.Equals(
                        result.traceId ?? string.Empty,
                        traceId ?? string.Empty,
                        StringComparison.Ordinal) ||
                    !IsErrorCode(result.errorCode) ||
                    string.IsNullOrWhiteSpace(result.message) ||
                    result.message.Length > 4096)
                {
                    throw new InvalidDataException(
                        "The package runtime bootstrap failure is invalid.");
                }
                throw new NativePackageRuntimeBootstrapException(
                    result.errorCode,
                    result.message.Trim());
            }
            if (result.schemaVersion != 1 ||
                    !string.Equals(
                        result.status,
                        "OK",
                        StringComparison.Ordinal) ||
                    result.brokerProcessId < 1 ||
                    !IsSha256(result.brokerSha256) ||
                    !IsSha256(result.helperSha256) ||
                    !IsSha256(result.runtimeDescriptorSha256) ||
                    !Path.IsPathRooted(
                        result.activeRecordPath ?? string.Empty) ||
                    !Path.IsPathRooted(
                        result.brokerPath ?? string.Empty) ||
                    !Path.IsPathRooted(
                        result.helperPath ?? string.Empty) ||
                    !string.Equals(
                        result.traceId ?? string.Empty,
                        traceId ?? string.Empty,
                        StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Secure package delivery setup failed.");
            }
        }

        private static NativePackageRuntimeResult ParseResult(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException(
                    "The package runtime bootstrap returned no result.");
            }
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text))
            {
                var serializer = JsonSerializer.Create(
                    new JsonSerializerSettings
                    {
                        MissingMemberHandling =
                            MissingMemberHandling.Error,
                    });
                NativePackageRuntimeResult result =
                    serializer.Deserialize<NativePackageRuntimeResult>(
                        reader);
                if (result == null || reader.Read())
                {
                    throw new InvalidDataException(
                        "The package runtime bootstrap result is invalid.");
                }
                return result;
            }
        }

        private static async Task<BoundedText> ReadBoundedAsync(
            StreamReader reader,
            int maximumCharacters)
        {
            var value = new StringBuilder();
            var buffer = new char[4096];
            bool exceeded = false;
            int read;
            while ((read = await reader.ReadAsync(
                       buffer,
                       0,
                       buffer.Length)) > 0)
            {
                int remaining = maximumCharacters - value.Length;
                if (remaining > 0)
                {
                    value.Append(buffer, 0, Math.Min(read, remaining));
                }
                if (read > remaining)
                {
                    exceeded = true;
                }
            }
            return new BoundedText
            {
                exceeded = exceeded,
                value = value.ToString(),
            };
        }

        private static string RequirePackageFile(
            string packageRoot,
            string relativePath,
            int maximumBytes)
        {
            string path = Path.GetFullPath(
                Path.Combine(
                    packageRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));
            string relative = Path.GetRelativePath(packageRoot, path);
            if (Path.IsPathRooted(relative) ||
                relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal) ||
                !File.Exists(path))
            {
                throw new InvalidDataException(
                    "A required package runtime file is missing.");
            }
            ValidatePathComponents(
                packageRoot,
                path,
                File.GetAttributes);
            var info = new FileInfo(path);
            if (info.Length < 1 ||
                info.Length > maximumBytes ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "A required package runtime file is invalid.");
            }
            return path;
        }

        internal static void ValidatePathComponentsForTests(
            string packageRoot,
            string path,
            Func<string, FileAttributes> readAttributes)
        {
            ValidatePathComponents(packageRoot, path, readAttributes);
        }

        private static void ValidatePathComponents(
            string packageRoot,
            string path,
            Func<string, FileAttributes> readAttributes)
        {
            if (readAttributes == null)
            {
                throw new ArgumentNullException(nameof(readAttributes));
            }
            string exactRoot = Path.GetFullPath(packageRoot);
            string exactPath = Path.GetFullPath(path);
            string relative = Path.GetRelativePath(exactRoot, exactPath);
            if (Path.IsPathRooted(relative) ||
                relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The package runtime file escapes its package.");
            }
            string current = exactRoot;
            foreach (string component in relative.Split(
                new[]
                {
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar,
                },
                StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, component);
                if ((readAttributes(current) &
                        FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "A package runtime path uses a reparse point.");
                }
            }
        }

        private static void RequirePinnedHash(
            string path,
            string expectedSha256)
        {
            string actual;
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (SHA256 sha256 = SHA256.Create())
            {
                actual = BitConverter.ToString(
                        sha256.ComputeHash(stream))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
            if (!string.Equals(
                    actual,
                    expectedSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "A package runtime file does not match its release.");
            }
        }

        private static void ValidateTrustedRoot(string path)
        {
            JObject document;
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (var reader = new StreamReader(
                stream,
                new UTF8Encoding(false, true),
                true,
                4096))
            using (var json = new JsonTextReader(reader))
            {
                document = JObject.Load(json);
                if (json.Read())
                {
                    throw new InvalidDataException(
                        "The reviewed package trust root is invalid.");
                }
            }
            var signed = document["signed"] as JObject;
            var signatures = document["signatures"] as JArray;
            if (signed == null ||
                signatures == null ||
                signatures.Count < 1 ||
                !string.Equals(
                    (string)signed["_type"],
                    "root",
                    StringComparison.Ordinal) ||
                (int?)signed["version"] != 1)
            {
                throw new InvalidDataException(
                    "The reviewed package trust root is invalid.");
            }
        }

        private static void RestrictEnvironment(ProcessStartInfo start)
        {
            start.EnvironmentVariables.Clear();
            CopyEnvironment(start, "SystemRoot");
            CopyEnvironment(start, "WINDIR");
            CopyEnvironment(start, "TEMP");
            CopyEnvironment(start, "TMP");
        }

        private static void CopyEnvironment(
            ProcessStartInfo start,
            string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                start.EnvironmentVariables[name] = value;
            }
        }

        private static void AppendOption(
            StringBuilder arguments,
            string name,
            string value)
        {
            AppendArgument(arguments, name);
            AppendArgument(arguments, value);
        }

        private static void AppendArgument(
            StringBuilder arguments,
            string value)
        {
            if (arguments.Length > 0)
            {
                arguments.Append(' ');
            }
            arguments.Append(QuoteWindowsArgument(value));
        }

        private static string QuoteWindowsArgument(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            var result = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (character == '"')
                {
                    result.Append('\\', backslashes * 2 + 1);
                    result.Append('"');
                    backslashes = 0;
                    continue;
                }
                result.Append('\\', backslashes);
                result.Append(character);
                backslashes = 0;
            }
            result.Append('\\', backslashes * 2);
            result.Append('"');
            return result.ToString();
        }

        private static void TryTerminate(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // Process exit races with timeout and cancellation.
            }
        }

        private static bool IsWindowsX64()
        {
            return Environment.Is64BitOperatingSystem &&
                Environment.Is64BitProcess &&
                Environment.OSVersion.Platform == PlatformID.Win32NT;
        }

        private static bool IsTraceId(string value)
        {
            return value != null &&
                value.Length == 32 &&
                IsLowerHex(value);
        }

        private static bool IsSha256(string value)
        {
            return value != null &&
                value.Length == 64 &&
                IsLowerHex(value);
        }

        private static bool IsErrorCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length > 64)
            {
                return false;
            }
            foreach (char character in value)
            {
                if (!(character >= 'A' && character <= 'Z') &&
                    !(character >= '0' && character <= '9') &&
                    character != '_')
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsLowerHex(string value)
        {
            foreach (char character in value)
            {
                if (!(character >= '0' && character <= '9') &&
                    !(character >= 'a' && character <= 'f'))
                {
                    return false;
                }
            }
            return true;
        }

        [Serializable]
        private sealed class NativePackageRuntimeResult
        {
            public string activeRecordPath = string.Empty;
            public string brokerPath = string.Empty;
            public int brokerProcessId;
            public string brokerSha256 = string.Empty;
            public bool brokerStarted;
            public string errorCode = string.Empty;
            public string helperPath = string.Empty;
            public string helperSha256 = string.Empty;
            public string message = string.Empty;
            public string runtimeDescriptorSha256 = string.Empty;
            public int schemaVersion;
            public string status = string.Empty;
            public string traceId = string.Empty;
        }

        private sealed class BoundedText
        {
            internal bool exceeded;
            internal string value = string.Empty;
        }
    }
}
