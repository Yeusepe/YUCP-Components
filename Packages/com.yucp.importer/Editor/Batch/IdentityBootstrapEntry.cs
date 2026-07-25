using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Batch
{
    [Serializable]
    internal sealed class IdentityBootstrapRequest
    {
        public int schemaVersion;
        public string runId = string.Empty;
        public string serverUrl = string.Empty;
    }

    [Serializable]
    internal sealed class IdentityBootstrapEvent
    {
        public int schemaVersion = 1;
        public string authorizationUrl = string.Empty;
        public string runId = string.Empty;
        public string status = "authorization-required";
    }

    [Serializable]
    internal sealed class IdentityBootstrapResult
    {
        public string displayName = string.Empty;
        public string errorCode = string.Empty;
        public string errorMessage = string.Empty;
        public int exitCode;
        public string runId = string.Empty;
        public int schemaVersion = 1;
        public bool signedIn;
        public string status = "failed";
    }

    [InitializeOnLoad]
    public static class IdentityBootstrapEntry
    {
        private const string ExecuteMethodName =
            "YUCP.Importer.Editor.Batch.IdentityBootstrapEntry.Run";
        internal const string RequestPathEnvironmentVariable =
            "YUCP_IDENTITY_BOOTSTRAP_REQUEST_PATH";
        internal const string EventPathEnvironmentVariable =
            "YUCP_IDENTITY_BOOTSTRAP_EVENT_PATH";
        internal const string ResultPathEnvironmentVariable =
            "YUCP_IDENTITY_BOOTSTRAP_RESULT_PATH";
        private const int SuccessExitCode = 0;
        private const int ValidationExitCode = 10;
        private const int AuthorizationExitCode = 20;
        private const int InternalExitCode = 50;
        private static Task<IdentityBootstrapResult> _execution;
        private static IdentityBootstrapRequest _activeRequest;
        private static string _eventPath;
        private static string _resultPath;
        private static bool _finished;
        private static bool _started;

        static IdentityBootstrapEntry()
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
                "The identity bootstrap batch operation is scheduled.");
            EditorApplication.update += StartOnUpdate;
        }

        private static void StartOnUpdate()
        {
            EditorApplication.update -= StartOnUpdate;
            try
            {
                if (!Application.isBatchMode)
                {
                    throw new InvalidOperationException(
                        "The identity bootstrap requires Unity batch mode.");
                }
                string requestPath = BatchFileProtocol.RequireAbsoluteFile(
                    Environment.GetEnvironmentVariable(
                        RequestPathEnvironmentVariable),
                    "identity bootstrap request");
                _eventPath = BatchFileProtocol.RequireAbsolutePath(
                    Environment.GetEnvironmentVariable(
                        EventPathEnvironmentVariable),
                    "identity bootstrap event");
                _resultPath = BatchFileProtocol.RequireAbsolutePath(
                    Environment.GetEnvironmentVariable(
                        ResultPathEnvironmentVariable),
                    "identity bootstrap result");
                IdentityBootstrapRequest request =
                    BatchFileProtocol.ReadJson<IdentityBootstrapRequest>(
                        requestPath,
                        "identity bootstrap request",
                        16 * 1024);
                ValidateRequest(request);
                _activeRequest = request;
                _execution = ExecuteAsync(request, _eventPath);
                EditorApplication.update += PollOnUpdate;
            }
            catch (Exception exception)
            {
                Finish(BuildFailureResult(_activeRequest, exception));
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
                Finish(_execution.GetAwaiter().GetResult());
            }
            catch (Exception exception)
            {
                Finish(BuildFailureResult(_activeRequest, exception));
            }
        }

        private static async Task<IdentityBootstrapResult> ExecuteAsync(
            IdentityBootstrapRequest request,
            string eventPath)
        {
            string authorizationError = null;
            bool signedIn = false;
            await CreatorIdentityOAuthService
                .SignInWithAuthorizationHandlerAsync(
                    request.serverUrl,
                    authorizationUrl => BatchFileProtocol.WriteJsonAtomically(
                        eventPath,
                        CreateAuthorizationEvent(
                            request.runId,
                            authorizationUrl)),
                    () => signedIn = true,
                    error => authorizationError = error,
                    false);
            if (!signedIn ||
                !CreatorIdentityOAuthService.IsSignedIn())
            {
                throw new IdentityBootstrapAuthorizationException(
                    string.IsNullOrWhiteSpace(authorizationError)
                        ? "Unity identity authorization failed."
                        : authorizationError);
            }
            return new IdentityBootstrapResult
            {
                displayName =
                    CreatorIdentityOAuthService.GetDisplayName() ??
                    string.Empty,
                exitCode = SuccessExitCode,
                runId = request.runId,
                signedIn = true,
                status = "succeeded",
            };
        }

        internal static void ValidateRequest(
            IdentityBootstrapRequest request)
        {
            if (request == null ||
                request.schemaVersion != 1 ||
                !BatchFileProtocol.IsSafeIdentifier(request.runId) ||
                !Uri.TryCreate(
                    request.serverUrl,
                    UriKind.Absolute,
                    out Uri server) ||
                !string.IsNullOrEmpty(server.UserInfo) ||
                !string.IsNullOrEmpty(server.Query) ||
                !string.IsNullOrEmpty(server.Fragment) ||
                (server.AbsolutePath != "/" &&
                    server.AbsolutePath.Length != 0) ||
                (server.Scheme != Uri.UriSchemeHttps &&
                    !(server.Scheme == Uri.UriSchemeHttp &&
                        server.IsLoopback)))
            {
                throw new InvalidDataException(
                    "The identity bootstrap request is invalid.");
            }
        }

        internal static IdentityBootstrapEvent CreateAuthorizationEvent(
            string runId,
            string authorizationUrl)
        {
            if (!BatchFileProtocol.IsSafeIdentifier(runId) ||
                !Uri.TryCreate(
                    authorizationUrl,
                    UriKind.Absolute,
                    out Uri authorization) ||
                (authorization.Scheme != Uri.UriSchemeHttps &&
                    !(authorization.Scheme == Uri.UriSchemeHttp &&
                        authorization.IsLoopback)))
            {
                throw new InvalidDataException(
                    "The identity authorization event is invalid.");
            }
            return new IdentityBootstrapEvent
            {
                authorizationUrl = authorization.AbsoluteUri,
                runId = runId,
            };
        }

        internal static IdentityBootstrapResult BuildFailureResult(
            IdentityBootstrapRequest request,
            Exception exception)
        {
            bool authorizationFailure =
                exception is IdentityBootstrapAuthorizationException;
            bool validationFailure =
                exception is InvalidDataException ||
                exception is ArgumentException;
            return new IdentityBootstrapResult
            {
                errorCode = authorizationFailure
                    ? "AUTHORIZATION_FAILED"
                    : validationFailure
                        ? "REQUEST_INVALID"
                        : "INTERNAL_ERROR",
                errorMessage = exception.Message,
                exitCode = authorizationFailure
                    ? AuthorizationExitCode
                    : validationFailure
                        ? ValidationExitCode
                        : InternalExitCode,
                runId = request?.runId ?? string.Empty,
            };
        }

        private static void Finish(IdentityBootstrapResult result)
        {
            if (_finished)
            {
                return;
            }
            _finished = true;
            EditorApplication.update -= StartOnUpdate;
            EditorApplication.update -= PollOnUpdate;
            int exitCode = result?.exitCode ?? InternalExitCode;
            try
            {
                if (!string.IsNullOrWhiteSpace(_eventPath) &&
                    File.Exists(_eventPath))
                {
                    File.Delete(_eventPath);
                }
                BatchFileProtocol.WriteJsonAtomically(_resultPath, result);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "The identity bootstrap result could not be committed: " +
                    exception.Message);
                exitCode = InternalExitCode;
            }
            EditorApplication.Exit(exitCode);
        }

        private sealed class IdentityBootstrapAuthorizationException :
            Exception
        {
            internal IdentityBootstrapAuthorizationException(string message) :
                base(message)
            {
            }
        }
    }
}
