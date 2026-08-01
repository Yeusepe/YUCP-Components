using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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
        public string bootstrapIntentJson = string.Empty;
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

        public bool ShouldSerializebootstrapIntentJson() =>
            !string.IsNullOrEmpty(bootstrapIntentJson);

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
        public Dictionary<string, string> vpmDependencies =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> vpmRepositories =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    [Serializable]
    internal sealed class NativePackageBrokerProgress
    {
        public long completedBytes;
        public long completedFiles;
        public string phase = string.Empty;
        public string runId = string.Empty;
        public int schemaVersion;
        public long sequence;
        public long totalBytes;
        public long totalFiles;
    }

    internal sealed class NativePackageBrokerException : Exception
    {
        internal NativePackageBrokerException(
            string errorCode,
            string traceId,
            string message)
            : this(errorCode, traceId, message, null)
        {
        }

        internal NativePackageBrokerException(
            string errorCode,
            string traceId,
            string message,
            Exception innerException)
            : base(message, innerException)
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
            Action<NativePackageBrokerProgress> reportProgress,
            CancellationToken cancellationToken);
    }

    internal interface INativePackageBrokerAuthenticationTransport
    {
        Task<NativePackageBrokerAuthenticationResult> AuthenticateAsync(
            string action,
            CancellationToken cancellationToken);
    }

    [Serializable]
    internal sealed class NativePackageBrokerAuthenticationResult
    {
        public string errorCode = string.Empty;
        public string errorMessage = string.Empty;
        public bool signedIn;
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
    internal sealed class NativePackageBrokerAuthenticationFrame
    {
        public string action = string.Empty;
        public string kind = "authenticate";
        public string operationToken = string.Empty;
        public int schemaVersion = 1;
    }

    [Serializable]
    internal sealed class NativePackageBrokerAuthenticationServerFrame
    {
        public NativePackageBrokerAuthenticationResult authentication;
        public string kind = string.Empty;
        public int schemaVersion;
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
        : INativePackageBrokerTransport,
          INativePackageBrokerAuthenticationTransport
    {
        internal const string ProductionPipeName =
            "yucp.package-broker.v1";
        private const int ConnectTimeoutMilliseconds = 5000;
        private const int DefaultFrameTimeoutMilliseconds = 120000;
        private const int DefaultOperationTimeoutMilliseconds = 14400000;
        private const int MaximumFrameCharacters = 1024 * 1024;
        private const int MaximumFrameBytes = 1024 * 1024;
        private static readonly JsonSerializerSettings
            StrictFrameSerializerSettings =
                new JsonSerializerSettings
                {
                    MissingMemberHandling =
                        MissingMemberHandling.Error,
                };
        private readonly string _pipeName;
        private readonly int _frameTimeoutMilliseconds;
        private readonly int _operationTimeoutMilliseconds;

        internal NamedPipePackageBrokerTransport(
            string pipeName = ProductionPipeName,
            int frameTimeoutMilliseconds = DefaultFrameTimeoutMilliseconds,
            int operationTimeoutMilliseconds =
                DefaultOperationTimeoutMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                throw new ArgumentException(
                    "The package broker pipe name is missing.",
                    nameof(pipeName));
            }
            if (frameTimeoutMilliseconds < 1 ||
                operationTimeoutMilliseconds < frameTimeoutMilliseconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frameTimeoutMilliseconds),
                    "The package broker timeouts are invalid.");
            }
            _pipeName = pipeName;
            _frameTimeoutMilliseconds = frameTimeoutMilliseconds;
            _operationTimeoutMilliseconds = operationTimeoutMilliseconds;
        }

        public async Task<NativePackageBrokerResult> ExecuteAsync(
            NativePackageBrokerRequest request,
            Action<NativePackageBrokerProgress> reportProgress,
            CancellationToken cancellationToken)
        {
            using (var operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken))
            try
            {
                operationCancellation.CancelAfter(
                    _operationTimeoutMilliseconds);
                // https://learn.microsoft.com/dotnet/api/system.io.pipes.namedpipeclientstream.-ctor
                using (var pipe = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous))
                {
                    try
                    {
                        await AwaitWithTimeout(
                            pipe.ConnectAsync(ConnectTimeoutMilliseconds),
                            _frameTimeoutMilliseconds,
                            operationCancellation.Token);
                    }
                    catch (TimeoutException)
                    {
                        throw new NativePackageBrokerException(
                            "BROKER_UNAVAILABLE",
                            TraceId(request.traceparent),
                            "The YUCP package broker is not running.");
                    }
                    catch (Exception exception) when (
                        exception is IOException ||
                        exception is UnauthorizedAccessException)
                    {
                        throw new NativePackageBrokerException(
                            "BROKER_UNAVAILABLE",
                            TraceId(request.traceparent),
                            "The YUCP package broker is not available.");
                    }
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
                        var frameReader = new BoundedFrameReader(reader);
                        writer.NewLine = "\n";
                        writer.AutoFlush = true;
                        string nonce = CreateNonce();
                        await WriteFrameAsync(
                            writer,
                            new NativePackageBrokerBeginFrame
                            {
                                clientNonce = nonce,
                            },
                            _frameTimeoutMilliseconds,
                            operationCancellation.Token);
                        NativePackageBrokerChallengeFrame challenge =
                            await ReadFrameAsync<
                                NativePackageBrokerChallengeFrame>(
                                frameReader,
                                "challenge",
                                _frameTimeoutMilliseconds,
                                operationCancellation.Token);
                        ValidateChallenge(challenge, nonce);

                        await WriteFrameAsync(
                            writer,
                            new NativePackageBrokerOperateFrame
                            {
                                operationToken =
                                    challenge.operationToken,
                                request = request,
                            },
                            _frameTimeoutMilliseconds,
                            operationCancellation.Token);

                        while (true)
                        {
                            NativePackageBrokerServerFrame frame =
                                await ReadFrameAsync<
                                    NativePackageBrokerServerFrame>(
                                    frameReader,
                                    null,
                                    _frameTimeoutMilliseconds,
                                    operationCancellation.Token);
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
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new NativePackageBrokerException(
                    "BROKER_TIMEOUT",
                    TraceId(request.traceparent),
                    "The YUCP desktop app did not respond in time.");
            }
            catch (TimeoutException)
            {
                throw new NativePackageBrokerException(
                    "BROKER_TIMEOUT",
                    TraceId(request.traceparent),
                    "The YUCP desktop app did not respond in time.");
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                throw new NativePackageBrokerException(
                    "BROKER_UNAVAILABLE",
                    TraceId(request.traceparent),
                    "Start the YUCP desktop app, then try again.");
            }
        }

        public async Task<NativePackageBrokerAuthenticationResult>
            AuthenticateAsync(
                string action,
                CancellationToken cancellationToken)
        {
            NativePackageBrokerClient.ValidateAuthenticationAction(action);
            using (var operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken))
            try
            {
                operationCancellation.CancelAfter(
                    _operationTimeoutMilliseconds);
                using (var pipe = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous))
                {
                    try
                    {
                        await AwaitWithTimeout(
                            pipe.ConnectAsync(ConnectTimeoutMilliseconds),
                            _frameTimeoutMilliseconds,
                            operationCancellation.Token);
                    }
                    catch (TimeoutException)
                    {
                        throw new NativePackageBrokerException(
                            "BROKER_UNAVAILABLE",
                            string.Empty,
                            "The YUCP package broker is not running.");
                    }
                    catch (Exception exception) when (
                        exception is IOException ||
                        exception is UnauthorizedAccessException)
                    {
                        throw new NativePackageBrokerException(
                            "BROKER_UNAVAILABLE",
                            string.Empty,
                            "The YUCP package broker is not available.");
                    }
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
                        var frameReader = new BoundedFrameReader(reader);
                        writer.NewLine = "\n";
                        writer.AutoFlush = true;
                        string nonce = CreateNonce();
                        await WriteFrameAsync(
                            writer,
                            new NativePackageBrokerBeginFrame
                            {
                                clientNonce = nonce,
                            },
                            _frameTimeoutMilliseconds,
                            operationCancellation.Token);
                        NativePackageBrokerChallengeFrame challenge =
                            await ReadFrameAsync<
                                NativePackageBrokerChallengeFrame>(
                                frameReader,
                                "challenge",
                                _frameTimeoutMilliseconds,
                                operationCancellation.Token);
                        ValidateChallenge(challenge, nonce);
                        await WriteFrameAsync(
                            writer,
                            new NativePackageBrokerAuthenticationFrame
                            {
                                action = action,
                                operationToken =
                                    challenge.operationToken,
                            },
                            _frameTimeoutMilliseconds,
                            operationCancellation.Token);
                        NativePackageBrokerAuthenticationServerFrame
                            response =
                                await ReadAuthenticationFrameAsync(
                                    frameReader,
                                    _frameTimeoutMilliseconds,
                                    operationCancellation.Token);
                        NativePackageBrokerClient
                            .ValidateAuthenticationResult(
                                response.authentication);
                        return response.authentication;
                    }
                }
            }
            catch (NativePackageBrokerException)
            {
                throw;
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new NativePackageBrokerException(
                    "BROKER_TIMEOUT",
                    string.Empty,
                    "Sign-in took too long. Try again.");
            }
            catch (TimeoutException)
            {
                throw new NativePackageBrokerException(
                    "BROKER_TIMEOUT",
                    string.Empty,
                    "Sign-in took too long. Try again.");
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                throw new NativePackageBrokerException(
                    "BROKER_UNAVAILABLE",
                    string.Empty,
                    "Sign in again, then try again.");
            }
        }

        private static async Task<
            NativePackageBrokerAuthenticationServerFrame>
            ReadAuthenticationFrameAsync(
                BoundedFrameReader reader,
                int timeoutMilliseconds,
                CancellationToken cancellationToken)
        {
            string line = await reader.ReadLineAsync(
                timeoutMilliseconds,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(line) ||
                Encoding.UTF8.GetByteCount(line) > MaximumFrameBytes)
            {
                throw InvalidProtocol();
            }
            NativePackageBrokerAuthenticationServerFrame frame;
            try
            {
                frame = JsonConvert.DeserializeObject<
                    NativePackageBrokerAuthenticationServerFrame>(
                    line,
                    StrictFrameSerializerSettings);
            }
            catch (JsonException)
            {
                throw InvalidProtocol();
            }
            if (frame == null ||
                frame.schemaVersion != 1 ||
                !string.Equals(
                    frame.kind,
                    "authentication",
                    StringComparison.Ordinal) ||
                frame.authentication == null)
            {
                throw InvalidProtocol();
            }
            return frame;
        }

        private static async Task<T> ReadFrameAsync<T>(
            BoundedFrameReader reader,
            string expectedKind,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
            where T : class
        {
            string line = await reader.ReadLineAsync(
                timeoutMilliseconds,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(line) ||
                Encoding.UTF8.GetByteCount(line) > MaximumFrameBytes)
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

        private static async Task WriteFrameAsync<T>(
            StreamWriter writer,
            T frame,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            string line = JsonConvert.SerializeObject(
                frame,
                Formatting.None,
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                });
            if (Encoding.UTF8.GetByteCount(line) > MaximumFrameBytes)
            {
                throw InvalidProtocol();
            }
            await AwaitWithTimeout(
                writer.WriteLineAsync(line),
                timeoutMilliseconds,
                cancellationToken);
        }

        private static async Task AwaitWithTimeout(
            Task operation,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            Task timeout = Task.Delay(
                timeoutMilliseconds,
                cancellationToken);
            Task completed = await Task.WhenAny(operation, timeout);
            if (completed == operation)
            {
                await operation;
                return;
            }
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                "The package broker frame timed out.");
        }

        private sealed class BoundedFrameReader
        {
            private readonly char[] _buffer = new char[4096];
            private readonly StreamReader _reader;
            private int _bufferCount;
            private int _bufferOffset;

            internal BoundedFrameReader(StreamReader reader)
            {
                _reader = reader ?? throw new ArgumentNullException(
                    nameof(reader));
            }

            internal async Task<string> ReadLineAsync(
                int timeoutMilliseconds,
                CancellationToken cancellationToken)
            {
                var line = new StringBuilder();
                while (true)
                {
                    if (_bufferOffset >= _bufferCount)
                    {
                        _bufferOffset = 0;
                        _bufferCount = 0;
                        Task<int> read = _reader.ReadAsync(
                            _buffer,
                            0,
                            _buffer.Length);
                        Task timeout = Task.Delay(
                            timeoutMilliseconds,
                            cancellationToken);
                        Task completed = await Task.WhenAny(read, timeout);
                        if (completed != read)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            throw new TimeoutException(
                                "The package broker frame timed out.");
                        }
                        _bufferCount = await read;
                        if (_bufferCount == 0)
                        {
                            throw InvalidProtocol();
                        }
                    }

                    int newline = Array.IndexOf(
                        _buffer,
                        '\n',
                        _bufferOffset,
                        _bufferCount - _bufferOffset);
                    int end = newline >= 0 ? newline : _bufferCount;
                    line.Append(
                        _buffer,
                        _bufferOffset,
                        end - _bufferOffset);
                    if (line.Length > MaximumFrameCharacters)
                    {
                        throw InvalidProtocol();
                    }
                    _bufferOffset = newline >= 0
                        ? newline + 1
                        : _bufferCount;
                    if (newline < 0)
                    {
                        continue;
                    }
                    if (line.Length > 0 &&
                        line[line.Length - 1] == '\r')
                    {
                        line.Length -= 1;
                    }
                    string value = line.ToString();
                    if (Encoding.UTF8.GetByteCount(value) >
                        MaximumFrameBytes)
                    {
                        throw InvalidProtocol();
                    }
                    return value;
                }
            }
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
            return NativePackageBrokerClient.TraceId(traceparent);
        }
    }

    internal static class NativePackageBrokerClient
    {
        internal const int SchemaVersion = 4;
        internal const int ProgressSchemaVersion = 1;
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
        private static readonly HashSet<string>
            SupportedAuthenticationActions =
                new HashSet<string>(
                    new[] { "sign-in", "sign-out", "status" },
                    StringComparer.Ordinal);
        private static INativePackageBrokerTransport s_transport =
            new NamedPipePackageBrokerTransport();

        internal static Task<NativePackageBrokerResult> ExecuteAsync(
            NativePackageBrokerRequest request,
            Action<NativePackageBrokerProgress> reportProgress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateRequest(request);
            INativePackageBrokerTransport transport = s_transport;
            return transport.ExecuteAsync(request, progress =>
            {
                ValidateProgress(request, progress);
                reportProgress?.Invoke(progress);
            }, cancellationToken);
        }

        internal static string TraceId(string traceparent)
        {
            string[] parts = (traceparent ?? string.Empty).Split('-');
            return parts.Length == 4 ? parts[1] : string.Empty;
        }

        internal static async Task<
            NativePackageBrokerAuthenticationResult>
            AuthenticateAsync(
                string action,
                CancellationToken cancellationToken = default)
        {
            ValidateAuthenticationAction(action);
            if (!(s_transport is
                INativePackageBrokerAuthenticationTransport transport))
            {
                throw new NativePackageBrokerException(
                    "BROKER_PROTOCOL_INVALID",
                    string.Empty,
                    "The YUCP package broker does not support authentication.");
            }
            NativePackageBrokerAuthenticationResult result =
                await transport.AuthenticateAsync(
                    action,
                    cancellationToken);
            ValidateAuthenticationResult(result);
            if (!string.IsNullOrWhiteSpace(result.errorCode))
            {
                throw new NativePackageBrokerException(
                    result.errorCode,
                    string.Empty,
                    string.IsNullOrWhiteSpace(result.errorMessage)
                        ? "We couldn’t finish signing you in."
                        : result.errorMessage);
            }
            return result;
        }

        internal static void ValidateAuthenticationAction(string action)
        {
            if (!SupportedAuthenticationActions.Contains(
                action ?? string.Empty))
            {
                throw new InvalidDataException(
                    "The package authentication action is invalid.");
            }
        }

        internal static void ValidateAuthenticationResult(
            NativePackageBrokerAuthenticationResult result)
        {
            if (result == null ||
                string.IsNullOrWhiteSpace(result.errorCode) !=
                string.IsNullOrWhiteSpace(result.errorMessage) ||
                !string.IsNullOrWhiteSpace(result.errorCode) &&
                    result.signedIn)
            {
                throw new InvalidDataException(
                    "The package authentication result is invalid.");
            }
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
                    return "Opening sign-in";
                case "verifying-access":
                    return "Checking your package access";
                case "personalizing":
                    return "Preparing your copy";
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
                    // Rejecting a phase this build predates would abort a
                    // delivery that is going fine.
                    return "Preparing your package";
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
                !PackageProtocolIdentifier.IsSafe(request.aliasId) ||
                !PackageProtocolIdentifier.IsSafe(
                    request.idempotencyKey) ||
                !PackageProtocolIdentifier.IsSafe(request.runId) ||
                !SupportedOperations.Contains(request.operation ?? string.Empty) ||
                !Path.IsPathRooted(request.projectPath ?? string.Empty) ||
                !IsSha256(request.projectIdentity) ||
                !IsSha256(request.expectedCurrentReleaseRoot) ||
                !IsValidTraceparent(request.traceparent))
            {
                throw new InvalidDataException(
                    "The installation request is invalid.");
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
                    "The installation result is invalid.");
            }

            if (result.status == "succeeded" &&
                (result.exitCode != 0 ||
                    !IsSha256(result.targetReleaseRoot) ||
                    !IsSha256(result.activeContentDigest) ||
                    string.IsNullOrWhiteSpace(
                        result.activePolicyVersion) ||
                    !MatchesSuppliedBinding(
                        request.targetReleaseRoot,
                        result.targetReleaseRoot) ||
                    !MatchesSuppliedBinding(
                        request.approvedActiveContentDigest,
                        result.activeContentDigest) ||
                    !MatchesSuppliedBinding(
                        request.approvedPolicyVersion,
                        result.activePolicyVersion)))
            {
                throw new InvalidDataException(
                    "The completed installation result is incomplete.");
            }

            if (result.status == "failed" &&
                (result.exitCode == 0 ||
                    string.IsNullOrWhiteSpace(result.errorCode)))
            {
                throw new InvalidDataException(
                    "The failed installation result is incomplete.");
            }
        }

        private static bool MatchesSuppliedBinding(
            string requested,
            string returned)
        {
            return string.IsNullOrEmpty(requested) ||
                string.Equals(requested, returned, StringComparison.Ordinal);
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
                    "The installation progress is invalid.");
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
