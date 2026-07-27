using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    [Serializable]
    internal sealed class NativePackageBrokerRequest
    {
        public string aliasId = string.Empty;
        public string approvedActiveContentDigest = string.Empty;
        public string approvedPolicyVersion = string.Empty;
        public string expectedCurrentReleaseRoot = string.Empty;
        public string idempotencyKey = string.Empty;
        public string operation = string.Empty;
        public string projectIdentity = string.Empty;
        public string projectPath = string.Empty;
        public string runId = string.Empty;
        public int schemaVersion = NativePackageBrokerClient.SchemaVersion;
        public string targetReleaseRoot = string.Empty;
        public string traceparent = string.Empty;

        public bool ShouldSerializeapprovedActiveContentDigest() =>
            !string.IsNullOrEmpty(approvedActiveContentDigest);

        public bool ShouldSerializeapprovedPolicyVersion() =>
            !string.IsNullOrEmpty(approvedPolicyVersion);

        public bool ShouldSerializetargetReleaseRoot() =>
            !string.IsNullOrEmpty(targetReleaseRoot);
    }

    [Serializable]
    internal sealed class NativePackageBrokerFile
    {
        public long bytes;
        public string normalizedPath = string.Empty;
        public string sha256 = string.Empty;
    }

    [Serializable]
    internal sealed class NativePackageBrokerResult
    {
        public string activeContentDigest = string.Empty;
        public string activePolicyVersion = string.Empty;
        public string errorCode = string.Empty;
        public string errorMessage = string.Empty;
        public int exitCode;
        public List<NativePackageBrokerFile> files =
            new List<NativePackageBrokerFile>();
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

    [Serializable]
    internal sealed class NativePackageBrokerProgress
    {
        public long completedBytes;
        public string phase = string.Empty;
        public string runId = string.Empty;
        public int schemaVersion;
        public long sequence;
        public long totalBytes;
    }

    internal sealed class NativePackageBrokerException : Exception
    {
        internal NativePackageBrokerException(
            string errorCode,
            string traceId,
            string message)
            : base(message)
        {
            ErrorCode = errorCode ?? string.Empty;
            TraceId = traceId ?? string.Empty;
        }

        internal string ErrorCode { get; }
        internal string TraceId { get; }
    }

    internal interface INativePackageBrokerTransport
    {
        Task<NativePackageBrokerResult> ExecuteAsync(
            NativePackageBrokerRequest request,
            Action<NativePackageBrokerProgress> reportProgress);
    }

    [Serializable]
    internal sealed class NativePackageBrokerBeginFrame
    {
        public string clientNonce = string.Empty;
        public string kind = "begin";
        public int schemaVersion = 1;
    }

    [Serializable]
    internal sealed class NativePackageBrokerChallengeFrame
    {
        public string clientNonce = string.Empty;
        public string expiresAt = string.Empty;
        public string kind = string.Empty;
        public string operationToken = string.Empty;
        public int schemaVersion;
    }

    [Serializable]
    internal sealed class NativePackageBrokerOperateFrame
    {
        public string kind = "operate";
        public string operationToken = string.Empty;
        public NativePackageBrokerRequest request;
        public int schemaVersion = 1;
    }

    [Serializable]
    internal sealed class NativePackageBrokerServerFrame
    {
        public string kind = string.Empty;
        public NativePackageBrokerProgress progress;
        public NativePackageBrokerResult result;
        public int schemaVersion;
    }

    internal sealed class NamedPipePackageBrokerTransport
        : INativePackageBrokerTransport
    {
        internal const string ProductionPipeName =
            "yucp.package-broker.v1";
        private const int ConnectTimeoutMilliseconds = 5000;
        private static readonly JsonSerializerSettings
            StrictFrameSerializerSettings =
                new JsonSerializerSettings
                {
                    MissingMemberHandling =
                        MissingMemberHandling.Error,
                };
        private readonly string _pipeName;

        internal NamedPipePackageBrokerTransport(
            string pipeName = ProductionPipeName)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                throw new ArgumentException(
                    "The package broker pipe name is missing.",
                    nameof(pipeName));
            }
            _pipeName = pipeName;
        }

        public async Task<NativePackageBrokerResult> ExecuteAsync(
            NativePackageBrokerRequest request,
            Action<NativePackageBrokerProgress> reportProgress)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous,
                    TokenImpersonationLevel.Impersonation))
                {
                    await pipe.ConnectAsync(
                        ConnectTimeoutMilliseconds);
                    using (var reader = new StreamReader(
                        pipe,
                        new UTF8Encoding(false, true),
                        true,
                        4096,
                        true))
                    using (var writer = new StreamWriter(
                        pipe,
                        new UTF8Encoding(false, true),
                        4096,
                        true))
                    {
                        writer.NewLine = "\n";
                        writer.AutoFlush = true;
                        string nonce = CreateNonce();
                        await WriteFrameAsync(
                            writer,
                            new NativePackageBrokerBeginFrame
                            {
                                clientNonce = nonce,
                            });
                        NativePackageBrokerChallengeFrame challenge =
                            await ReadFrameAsync<
                                NativePackageBrokerChallengeFrame>(
                                reader,
                                "challenge");
                        ValidateChallenge(challenge, nonce);

                        await WriteFrameAsync(
                            writer,
                            new NativePackageBrokerOperateFrame
                            {
                                operationToken =
                                    challenge.operationToken,
                                request = request,
                            });

                        while (true)
                        {
                            NativePackageBrokerServerFrame frame =
                                await ReadFrameAsync<
                                    NativePackageBrokerServerFrame>(
                                    reader,
                                    null);
                            if (string.Equals(
                                    frame.kind,
                                    "progress",
                                    StringComparison.Ordinal))
                            {
                                if (frame.progress == null)
                                {
                                    throw InvalidProtocol();
                                }
                                reportProgress?.Invoke(frame.progress);
                                continue;
                            }
                            if (string.Equals(
                                    frame.kind,
                                    "result",
                                    StringComparison.Ordinal) &&
                                frame.result != null)
                            {
                                return frame.result;
                            }
                            throw InvalidProtocol();
                        }
                    }
                }
            }
            catch (NativePackageBrokerException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is TimeoutException ||
                exception is UnauthorizedAccessException)
            {
                throw new NativePackageBrokerException(
                    "BROKER_UNAVAILABLE",
                    TraceId(request.traceparent),
                    "Start the YUCP desktop app, then try again.");
            }
        }

        private static async Task<T> ReadFrameAsync<T>(
            StreamReader reader,
            string expectedKind)
            where T : class
        {
            string line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line) ||
                Encoding.UTF8.GetByteCount(line) > 1024 * 1024)
            {
                throw InvalidProtocol();
            }
            return DeserializeFrame<T>(line, expectedKind);
        }

        internal static T DeserializeFrame<T>(
            string line,
            string expectedKind)
            where T : class
        {
            T frame;
            try
            {
                frame = JsonConvert.DeserializeObject<T>(
                    line,
                    StrictFrameSerializerSettings);
            }
            catch (JsonException)
            {
                throw InvalidProtocol();
            }
            if (frame == null)
            {
                throw InvalidProtocol();
            }
            if (expectedKind != null)
            {
                NativePackageBrokerChallengeFrame challenge =
                    frame as NativePackageBrokerChallengeFrame;
                if (challenge == null ||
                    challenge.schemaVersion != 1 ||
                    !string.Equals(
                        challenge.kind,
                        expectedKind,
                        StringComparison.Ordinal))
                {
                    throw InvalidProtocol();
                }
            }
            else
            {
                NativePackageBrokerServerFrame serverFrame =
                    frame as NativePackageBrokerServerFrame;
                if (serverFrame == null ||
                    serverFrame.schemaVersion != 1)
                {
                    throw InvalidProtocol();
                }
            }
            return frame;
        }

        private static Task WriteFrameAsync<T>(
            StreamWriter writer,
            T frame)
        {
            return writer.WriteLineAsync(JsonConvert.SerializeObject(
                frame,
                Formatting.None,
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                }));
        }

        private static void ValidateChallenge(
            NativePackageBrokerChallengeFrame challenge,
            string expectedNonce)
        {
            if (!string.Equals(
                    challenge.clientNonce,
                    expectedNonce,
                    StringComparison.Ordinal) ||
                !IsCanonicalBase64Url32(challenge.operationToken) ||
                !DateTimeOffset.TryParse(
                    challenge.expiresAt,
                    out DateTimeOffset expiresAt) ||
                expiresAt <= DateTimeOffset.UtcNow ||
                expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                throw InvalidProtocol();
            }
        }

        private static string CreateNonce()
        {
            var nonce = new byte[32];
            using (RandomNumberGenerator random =
                RandomNumberGenerator.Create())
            {
                random.GetBytes(nonce);
            }
            return Convert.ToBase64String(nonce)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static bool IsCanonicalBase64Url32(string value)
        {
            if (value == null ||
                value.Length != 43 ||
                value.Any(character =>
                    !(character >= 'A' && character <= 'Z') &&
                    !(character >= 'a' && character <= 'z') &&
                    !(character >= '0' && character <= '9') &&
                    character != '-' &&
                    character != '_'))
            {
                return false;
            }
            try
            {
                string base64 = value
                    .Replace('-', '+')
                    .Replace('_', '/') + "=";
                return Convert.FromBase64String(base64).Length == 32;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static NativePackageBrokerException InvalidProtocol()
        {
            return new NativePackageBrokerException(
                "BROKER_PROTOCOL_INVALID",
                string.Empty,
                "The YUCP desktop app returned an invalid response.");
        }

        private static string TraceId(string traceparent)
        {
            string[] parts = (traceparent ?? string.Empty).Split('-');
            return parts.Length == 4 ? parts[1] : string.Empty;
        }
    }

    internal static class NativePackageBrokerClient
    {
        internal const int SchemaVersion = 3;
        internal const int ProgressSchemaVersion = 1;
        private static readonly Regex SafeIdentifier = new Regex(
            "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$",
            RegexOptions.CultureInvariant);
        private static readonly Regex Traceparent = new Regex(
            "^00-[0-9a-f]{32}-[0-9a-f]{16}-(00|01)$",
            RegexOptions.CultureInvariant);
        private static readonly HashSet<string> SupportedOperations =
            new HashSet<string>(
                new[]
                {
                    "preflight",
                    "install",
                    "update",
                    "repair",
                    "rollback",
                    "recover",
                    "uninstall",
                },
                StringComparer.Ordinal);
        private static INativePackageBrokerTransport s_transport =
            new NamedPipePackageBrokerTransport();

        internal static Task<NativePackageBrokerResult> ExecuteAsync(
            NativePackageBrokerRequest request,
            Action<NativePackageBrokerProgress> reportProgress = null)
        {
            ValidateRequest(request);
            INativePackageBrokerTransport transport = s_transport;
            return transport.ExecuteAsync(request, progress =>
            {
                ValidateProgress(request, progress);
                reportProgress?.Invoke(progress);
            });
        }

        internal static string SerializeRequest(
            NativePackageBrokerRequest request)
        {
            ValidateRequest(request);
            return JsonConvert.SerializeObject(
                request,
                Formatting.None,
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    DefaultValueHandling = DefaultValueHandling.Ignore,
                });
        }

        internal static string GetFriendlyProgressMessage(
            string phase,
            long completedBytes,
            long totalBytes)
        {
            switch (phase)
            {
                case "preparing":
                    return "Preparing your package";
                case "signing-in":
                    return "Opening secure sign-in";
                case "verifying-access":
                    return "Checking your package access";
                case "downloading":
                    if (totalBytes > 0 &&
                        completedBytes >= 0 &&
                        completedBytes <= totalBytes)
                    {
                        long percent = Math.Min(
                            100,
                            completedBytes * 100 / totalBytes);
                        return $"Downloading your package ({percent}%)";
                    }
                    return "Downloading your package";
                case "verifying":
                    return "Checking package integrity";
                case "assembling":
                    return "Preparing files for Unity";
                case "finalizing":
                    return "Finishing installation";
                default:
                    throw new InvalidDataException(
                        "The package delivery progress phase is invalid.");
            }
        }

        internal static string CreateTraceparent()
        {
            var traceId = new byte[16];
            var parentId = new byte[8];
            using (RandomNumberGenerator random =
                RandomNumberGenerator.Create())
            {
                do
                {
                    random.GetBytes(traceId);
                }
                while (traceId.All(value => value == 0));
                do
                {
                    random.GetBytes(parentId);
                }
                while (parentId.All(value => value == 0));
            }
            return "00-" +
                string.Concat(
                    traceId.Select(value => value.ToString("x2"))) +
                "-" +
                string.Concat(
                    parentId.Select(value => value.ToString("x2"))) +
                "-01";
        }

        internal static void ValidateRequest(
            NativePackageBrokerRequest request)
        {
            if (request == null ||
                request.schemaVersion != SchemaVersion ||
                !SafeIdentifier.IsMatch(request.aliasId ?? string.Empty) ||
                !SafeIdentifier.IsMatch(
                    request.idempotencyKey ?? string.Empty) ||
                !SafeIdentifier.IsMatch(request.runId ?? string.Empty) ||
                !SupportedOperations.Contains(request.operation ?? string.Empty) ||
                !Path.IsPathRooted(request.projectPath ?? string.Empty) ||
                !IsSha256(request.projectIdentity) ||
                !IsSha256(request.expectedCurrentReleaseRoot) ||
                !IsValidTraceparent(request.traceparent))
            {
                throw new InvalidDataException(
                    "The package delivery request is invalid.");
            }

            if (!string.IsNullOrEmpty(request.targetReleaseRoot) &&
                !IsSha256(request.targetReleaseRoot))
            {
                throw new InvalidDataException(
                    "The target package version is invalid.");
            }

            bool preflight = string.Equals(
                request.operation,
                "preflight",
                StringComparison.Ordinal);
            if (preflight)
            {
                if (!string.IsNullOrEmpty(
                        request.approvedActiveContentDigest) ||
                    !string.IsNullOrEmpty(request.approvedPolicyVersion))
                {
                    throw new InvalidDataException(
                        "Package review approval is not valid for preflight.");
                }
            }
            else if (!IsSha256(request.approvedActiveContentDigest) ||
                string.IsNullOrWhiteSpace(request.approvedPolicyVersion))
            {
                throw new InvalidDataException(
                    "The approved package review is invalid.");
            }
        }

        internal static void ValidateResult(
            NativePackageBrokerRequest request,
            NativePackageBrokerResult result)
        {
            ValidateRequest(request);
            if (result == null ||
                result.schemaVersion != SchemaVersion ||
                !string.Equals(
                    result.runId,
                    request.runId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    result.operation,
                    request.operation,
                    StringComparison.Ordinal) ||
                (result.status != "succeeded" &&
                    result.status != "failed") ||
                result.exitCode < 0 ||
                !IsSafeRelativeOrAbsoluteBrokerPath(
                    result.stagingTree) ||
                !IsSafeRelativeOrAbsoluteBrokerPath(
                    result.receiptPath) ||
                result.files == null ||
                result.files.Any(file =>
                    file == null ||
                    file.bytes < 0 ||
                    !IsSha256(file.sha256) ||
                    string.IsNullOrWhiteSpace(file.normalizedPath)))
            {
                throw new InvalidDataException(
                    "The package delivery result is invalid.");
            }

            if (result.status == "succeeded" &&
                (result.exitCode != 0 ||
                    !IsSha256(result.targetReleaseRoot) ||
                    !IsSha256(result.activeContentDigest) ||
                    string.IsNullOrWhiteSpace(
                        result.activePolicyVersion)))
            {
                throw new InvalidDataException(
                    "The successful package delivery result is incomplete.");
            }

            if (result.status == "failed" &&
                (result.exitCode == 0 ||
                    string.IsNullOrWhiteSpace(result.errorCode)))
            {
                throw new InvalidDataException(
                    "The failed package delivery result is incomplete.");
            }
        }

        private static void ValidateProgress(
            NativePackageBrokerRequest request,
            NativePackageBrokerProgress progress)
        {
            if (progress == null ||
                progress.schemaVersion != ProgressSchemaVersion ||
                !string.Equals(
                    progress.runId,
                    request.runId,
                    StringComparison.Ordinal) ||
                progress.sequence <= 0 ||
                progress.completedBytes < 0 ||
                progress.totalBytes < 0 ||
                progress.totalBytes > 0 &&
                    progress.completedBytes > progress.totalBytes)
            {
                throw new InvalidDataException(
                    "The package delivery progress is invalid.");
            }

            GetFriendlyProgressMessage(
                progress.phase,
                progress.completedBytes,
                progress.totalBytes);
        }

        private static bool IsValidTraceparent(string value)
        {
            if (!Traceparent.IsMatch(value ?? string.Empty))
            {
                return false;
            }
            string[] parts = value.Split('-');
            return parts[1].Any(character => character != '0') &&
                parts[2].Any(character => character != '0');
        }

        private static bool IsSha256(string value)
        {
            return value != null &&
                value.Length == 64 &&
                value.All(character =>
                    character >= '0' && character <= '9' ||
                    character >= 'a' && character <= 'f');
        }

        private static bool IsSafeRelativeOrAbsoluteBrokerPath(string value)
        {
            return string.IsNullOrEmpty(value) ||
                Path.IsPathRooted(value);
        }

#if UNITY_INCLUDE_TESTS
        internal static void SetTransportForTests(
            INativePackageBrokerTransport transport)
        {
            s_transport = transport ??
                new NamedPipePackageBrokerTransport();
        }
#endif
    }
}
