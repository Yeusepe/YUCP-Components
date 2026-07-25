using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Batch
{
    [Serializable]
    internal sealed class PackageLifecycleRequest
    {
        public int schemaVersion;
        public string runId = string.Empty;
        public string operation = string.Empty;
        public string projectPath = string.Empty;
        public string productAlias = string.Empty;
        public string idempotencyKey = string.Empty;
        public string expectedCurrentReleaseRoot = string.Empty;
        public string targetReleaseRoot = string.Empty;
        public string approvedActiveContentDigest = string.Empty;
        public string approvedPolicyVersion = string.Empty;
    }

    [Serializable]
    internal sealed class PackageLifecycleResult
    {
        public int schemaVersion = 1;
        public string runId = string.Empty;
        public string operation = string.Empty;
        public string status = string.Empty;
        public int exitCode;
        public string traceId = string.Empty;
        public string projectPath = string.Empty;
        public string productAlias = string.Empty;
        public string currentReleaseRoot = string.Empty;
        public string targetReleaseRoot = string.Empty;
        public string activeContentDigest = string.Empty;
        public string policyVersion = string.Empty;
        public List<string> receiptReferences = new List<string>();
        public string journalId = string.Empty;
        public string journalState = string.Empty;
        public string errorCode = string.Empty;
        public string errorMessage = string.Empty;
    }

    [Serializable]
    internal sealed class PackageLifecycleIdempotencyRecord
    {
        public int schemaVersion = 1;
        public string fingerprint = string.Empty;
        public PackageLifecycleResult result = new PackageLifecycleResult();
    }

    [InitializeOnLoad]
    public static class PackageLifecycleEntry
    {
        private const string ExecuteMethodName =
            "YUCP.Importer.Editor.Batch.PackageLifecycleEntry.Run";
        internal const string RequestPathEnvironmentVariable =
            "YUCP_PACKAGE_LIFECYCLE_REQUEST_PATH";
        internal const string ResultPathEnvironmentVariable =
            "YUCP_PACKAGE_LIFECYCLE_RESULT_PATH";
        internal const string ServerUrlEnvironmentVariable =
            "YUCP_PACKAGE_SERVER_URL";
        private const int SuccessExitCode = 0;
        private const int ValidationExitCode = 10;
        private const int TransferExitCode = 20;
        private const int CouplingExitCode = 30;
        private const int ProjectExitCode = 40;
        private const int InternalExitCode = 50;
        private static PackageLifecycleRequest _activeRequest;
        private static Task<PackageLifecycleResult> _execution;
        private static bool _finished;
        private static string _resultPath;
        private static bool _started;

        static PackageLifecycleEntry()
        {
            if (ShouldResumeAfterDomainReload(
                Application.isBatchMode,
                Environment.GetEnvironmentVariable(
                    RequestPathEnvironmentVariable),
                Environment.GetEnvironmentVariable(
                    ResultPathEnvironmentVariable),
                Environment.GetCommandLineArgs()))
            {
                Schedule();
            }
        }

        public static void Run()
        {
            Schedule();
        }

        internal static bool ShouldResumeAfterDomainReload(
            bool isBatchMode,
            string requestPath,
            string resultPath,
            string[] commandLineArguments)
        {
            return BatchCommandLine.ShouldResumeAfterDomainReload(
                isBatchMode,
                requestPath,
                resultPath,
                ExecuteMethodName,
                commandLineArguments);
        }

        private static void Schedule()
        {
            if (_started)
            {
                return;
            }
            _started = true;
            Debug.Log(
                "The package lifecycle batch operation is scheduled.");
            EditorApplication.update += ExecuteOnUpdate;
        }

        private static void ExecuteOnUpdate()
        {
            EditorApplication.update -= ExecuteOnUpdate;
            _execution = ExecuteAsync();
            EditorApplication.update += PollOnUpdate;
        }

        private static async Task<PackageLifecycleResult> ExecuteAsync()
        {
            PackageLifecycleRequest request = null;
            string resultPath = Environment.GetEnvironmentVariable(
                ResultPathEnvironmentVariable);
            _resultPath = resultPath;
            try
            {
                if (!Application.isBatchMode)
                {
                    throw new InvalidOperationException(
                        "The package lifecycle entry point requires Unity batch mode.");
                }
                string requestPath = BatchFileProtocol.RequireAbsoluteFile(
                    Environment.GetEnvironmentVariable(
                        RequestPathEnvironmentVariable),
                    "request");
                resultPath = BatchFileProtocol.RequireAbsolutePath(
                    resultPath,
                    "result");
                request = ReadRequest(requestPath);
                _activeRequest = request;
                ValidateRequest(request);
                string fingerprint = Fingerprint(request);
                string idempotencyPath = IdempotencyPath(request);
                PackageLifecycleIdempotencyRecord prior =
                    ReadIdempotencyRecord(idempotencyPath);
                if (prior != null)
                {
                    if (!string.Equals(
                        prior.fingerprint,
                        fingerprint,
                        StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "The idempotency key is bound to another lifecycle request.");
                    }
                    return prior.result;
                }

                AliasPackageContract alias =
                    AliasPackageDiscovery.FindByAliasId(request.productAlias);
                string serverUrl = RequiresServer(request.operation)
                    ? Environment.GetEnvironmentVariable(
                        ServerUrlEnvironmentVariable)
                    : string.Empty;
                PackageLifecycleExecutionResult execution =
                    await PackageLifecycleCoordinator.ExecuteAsync(
                        serverUrl,
                        alias,
                        request.operation,
                        request.runId,
                        request.idempotencyKey,
                        request.expectedCurrentReleaseRoot,
                        request.targetReleaseRoot,
                        request.approvedActiveContentDigest,
                        request.approvedPolicyVersion);
                var result = new PackageLifecycleResult
                {
                    activeContentDigest = execution.activeContentDigest,
                    currentReleaseRoot = execution.currentReleaseRoot,
                    exitCode = SuccessExitCode,
                    journalId = execution.journalId,
                    journalState = execution.journalState,
                    operation = request.operation,
                    policyVersion = execution.activePolicyVersion,
                    productAlias = request.productAlias,
                    projectPath = Path.GetFullPath(request.projectPath),
                    receiptReferences = execution.receiptReferences,
                    runId = request.runId,
                    status = "succeeded",
                    targetReleaseRoot = execution.targetReleaseRoot,
                    traceId = execution.traceId,
                };
                WriteIdempotencyRecord(
                    idempotencyPath,
                    fingerprint,
                    result);
                return result;
            }
            catch (Exception exception)
            {
                PackageLifecycleResult failure = BuildFailure(request, exception);
                if (request != null)
                {
                    try
                    {
                        WriteIdempotencyRecord(
                            IdempotencyPath(request),
                            Fingerprint(request),
                            failure);
                    }
                    catch
                    {
                    }
                }
                return failure;
            }
        }

        private static void PollOnUpdate()
        {
            if (_execution == null || !_execution.IsCompleted)
            {
                return;
            }
            EditorApplication.update -= PollOnUpdate;
            try
            {
                Finish(_resultPath, _execution.GetAwaiter().GetResult());
            }
            catch (Exception exception)
            {
                Finish(
                    _resultPath,
                    BuildFailure(_activeRequest, exception));
            }
        }

        internal static PackageLifecycleRequest ReadRequest(string path)
        {
            return BatchFileProtocol.ReadJson<PackageLifecycleRequest>(
                path,
                "package lifecycle request",
                64 * 1024);
        }

        internal static void ValidateRequest(PackageLifecycleRequest request)
        {
            if (request == null ||
                request.schemaVersion != 1 ||
                !IsSafeIdentifier(request.runId) ||
                !IsSafeIdentifier(request.idempotencyKey) ||
                !IsSafeIdentifier(request.productAlias) ||
                !IsSupportedOperation(request.operation) ||
                !Path.IsPathRooted(request.projectPath) ||
                !IsSha256(request.expectedCurrentReleaseRoot))
            {
                throw new InvalidDataException(
                    "The package lifecycle request fields are invalid.");
            }
            string openedProject = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            if (!string.Equals(
                Path.GetFullPath(request.projectPath),
                openedProject,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The package lifecycle project path does not match the opened project.");
            }
            if (request.operation == "rollback" &&
                !IsSha256(request.targetReleaseRoot))
            {
                throw new InvalidDataException(
                    "Rollback requires an exact target release root.");
            }
            if (RequiresApproval(request.operation) &&
                (!IsSha256(request.approvedActiveContentDigest) ||
                    string.IsNullOrWhiteSpace(request.approvedPolicyVersion)))
            {
                throw new InvalidDataException(
                    "The package lifecycle approval is invalid.");
            }
        }

        private static PackageLifecycleResult BuildFailure(
            PackageLifecycleRequest request,
            Exception exception)
        {
            int exitCode = InternalExitCode;
            string errorCode = "INTERNAL_ERROR";
            if (exception is InvalidDataException ||
                exception is ArgumentException ||
                exception is CryptographicException)
            {
                exitCode = ValidationExitCode;
                errorCode = "REQUEST_INVALID";
            }
            else if (exception is System.Net.Http.HttpRequestException)
            {
                exitCode = TransferExitCode;
                errorCode = "TRANSFER_FAILED";
            }
            else if (exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                exitCode = ProjectExitCode;
                errorCode = "PROJECT_TRANSACTION_FAILED";
            }
            else if (exception.Message.IndexOf(
                    "materialization",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                exception.Message.IndexOf(
                    "receipt",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exitCode = CouplingExitCode;
                errorCode = "COUPLING_FAILED";
            }
            else if (exception is InvalidOperationException)
            {
                exitCode = ValidationExitCode;
                errorCode = "OPERATION_REJECTED";
            }
            return new PackageLifecycleResult
            {
                currentReleaseRoot = SafeCurrentReleaseRoot(request),
                errorCode = errorCode,
                errorMessage = exception.Message,
                exitCode = exitCode,
                operation = request?.operation ?? string.Empty,
                productAlias = request?.productAlias ?? string.Empty,
                projectPath = request?.projectPath ?? string.Empty,
                runId = request?.runId ?? string.Empty,
                status = "failed",
                targetReleaseRoot = request?.targetReleaseRoot ?? string.Empty,
                traceId = request?.runId ?? string.Empty,
            };
        }

        private static string SafeCurrentReleaseRoot(
            PackageLifecycleRequest request)
        {
            if (request == null ||
                !Path.IsPathRooted(request.projectPath) ||
                !IsSafeIdentifier(request.productAlias))
            {
                return string.Empty;
            }
            try
            {
                return PackageLifecycleCoordinator.GetCurrentReleaseRoot(
                    request.projectPath,
                    request.productAlias);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void Finish(
            string resultPath,
            PackageLifecycleResult result)
        {
            if (_finished)
            {
                return;
            }
            _finished = true;
            EditorApplication.update -= ExecuteOnUpdate;
            EditorApplication.update -= PollOnUpdate;
            int exitCode = result?.exitCode ?? InternalExitCode;
            try
            {
                BatchFileProtocol.WriteJsonAtomically(
                    BatchFileProtocol.RequireAbsolutePath(
                        resultPath,
                        "result"),
                    result);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"The package lifecycle result could not be committed: {exception.Message}");
                exitCode = InternalExitCode;
            }
            EditorApplication.Exit(exitCode);
        }

        private static string IdempotencyPath(PackageLifecycleRequest request)
        {
            string stateRoot = TransferHelperClient.ResolveStateRoot();
            return Path.Combine(
                stateRoot,
                "idempotency",
                HashIdentifier(
                    "yucp:package-lifecycle-idempotency:v1",
                    (request.productAlias ?? string.Empty) +
                        "\n" +
                        (request.idempotencyKey ?? string.Empty)) +
                    ".json");
        }

        private static string HashIdentifier(string purpose, string value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return BitConverter.ToString(
                        sha256.ComputeHash(
                            Encoding.UTF8.GetBytes(
                                purpose + "\n" + (value ?? string.Empty))))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static PackageLifecycleIdempotencyRecord ReadIdempotencyRecord(
            string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            PackageLifecycleIdempotencyRecord record =
                BatchFileProtocol.ReadJson<PackageLifecycleIdempotencyRecord>(
                    path,
                    "package lifecycle idempotency record",
                    1024 * 1024);
            if (record == null ||
                record.schemaVersion != 1 ||
                !IsSha256(record.fingerprint) ||
                record.result == null)
            {
                throw new InvalidDataException(
                    "The package lifecycle idempotency record is invalid.");
            }
            return record;
        }

        private static void WriteIdempotencyRecord(
            string path,
            string fingerprint,
            PackageLifecycleResult result)
        {
            var record = new PackageLifecycleIdempotencyRecord
            {
                fingerprint = fingerprint,
                result = result,
            };
            BatchFileProtocol.WriteJsonAtomically(path, record);
        }

        private static string Fingerprint(PackageLifecycleRequest request)
        {
            string canonical = string.Join(
                "\n",
                new[]
                {
                    request.schemaVersion.ToString(),
                    request.runId ?? string.Empty,
                    request.operation ?? string.Empty,
                    Path.GetFullPath(request.projectPath ?? string.Empty),
                    request.productAlias ?? string.Empty,
                    request.idempotencyKey ?? string.Empty,
                    request.expectedCurrentReleaseRoot ?? string.Empty,
                    request.targetReleaseRoot ?? string.Empty,
                    request.approvedActiveContentDigest ?? string.Empty,
                    request.approvedPolicyVersion ?? string.Empty,
                });
            using (SHA256 sha256 = SHA256.Create())
            {
                return BitConverter.ToString(
                        sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static bool RequiresServer(string operation)
        {
            return operation != "uninstall" && operation != "recover";
        }

        private static bool RequiresApproval(string operation)
        {
            return operation == "install" ||
                operation == "update" ||
                operation == "repair" ||
                operation == "rollback";
        }

        private static bool IsSupportedOperation(string operation)
        {
            return new[]
            {
                "preflight",
                "install",
                "update",
                "repair",
                "rollback",
                "uninstall",
                "recover",
            }.Contains(operation);
        }

        private static bool IsSafeIdentifier(string value)
        {
            return BatchFileProtocol.IsSafeIdentifier(value);
        }

        private static bool IsSha256(string value)
        {
            return value != null &&
                value.Length == 64 &&
                value.All(character =>
                    character >= '0' && character <= '9' ||
                    character >= 'a' && character <= 'f');
        }
    }
}
