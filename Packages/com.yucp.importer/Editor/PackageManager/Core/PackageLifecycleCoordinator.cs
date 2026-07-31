using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal sealed class PackageLifecycleUserProgress
    {
        public string message = string.Empty;
        public float progress;
    }

    internal sealed class PackageActiveContentReview
    {
        public string approveLabel = string.Empty;
        public string cancelLabel = string.Empty;
        public string message = string.Empty;
        public string title = string.Empty;
    }

    internal sealed class PackageLifecycleInstallResult
    {
        internal bool alreadyInstalled;
        internal bool cancelled;
        internal string errorCode = string.Empty;
        internal string errorMessage = string.Empty;
        internal bool succeeded;
        internal string traceId = string.Empty;
    }

    [Serializable]
    internal sealed class PackageDeliveryInstallState
    {
        public int schemaVersion = 5;
        public string activeContentDigest = string.Empty;
        public string activePolicyVersion = string.Empty;
        public string aliasId = string.Empty;
        public string receiptId = string.Empty;
        public string receiptPath = string.Empty;
        public string releaseRoot = string.Empty;
        public string version = string.Empty;
        public string versionId = string.Empty;
        public string previousActiveContentDigest = string.Empty;
        public string previousActivePolicyVersion = string.Empty;
        public string previousReleaseRoot = string.Empty;
        public string previousVersion = string.Empty;
        public string previousVersionId = string.Empty;
        public List<NativePackageBrokerFile> previousFiles =
            new List<NativePackageBrokerFile>();
        public List<NativePackageBrokerFile> files =
            new List<NativePackageBrokerFile>();
    }

    internal sealed class PackageLifecycleExecutionResult
    {
        internal string activeContentDigest = string.Empty;
        internal string activePolicyVersion = string.Empty;
        internal string currentReleaseRoot = string.Empty;
        internal List<NativePackageBrokerFile> files =
            new List<NativePackageBrokerFile>();
        internal List<string> receiptReferences = new List<string>();
        internal string journalId = string.Empty;
        internal string journalState = string.Empty;
        internal string stagingTree = string.Empty;
        internal string targetReleaseRoot = string.Empty;
        internal string traceId = string.Empty;
        internal string versionId = string.Empty;
    }

    internal static class PackageLifecycleCoordinator
    {
        internal const string EmptyReleaseRoot =
            "0000000000000000000000000000000000000000000000000000000000000000";
        private static readonly TimeSpan BrokerBootstrapTimeout =
            TimeSpan.FromSeconds(120);
        private static readonly INativePackageRuntimeBootstrap
            ProductionRuntimeBootstrap =
                new PackagedNativePackageRuntimeBootstrap();

        internal static void EnsureSupportedClientPlatform(bool isWindows)
        {
            if (!isWindows)
            {
                throw new PlatformNotSupportedException(
                    "Package installation requires a Windows x64 Unity Editor.");
            }
        }

        private static void EnsureSupportedClientPlatform()
        {
            EnsureSupportedClientPlatform(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                RuntimeInformation.OSArchitecture == Architecture.X64);
        }

        internal static Task<NativePackageBrokerAuthenticationResult>
            GetAuthenticationStatusAsync()
        {
            return ExecuteAuthenticationAsync("status");
        }

        internal static Task<NativePackageBrokerAuthenticationResult>
            SignInAsync()
        {
            return ExecuteAuthenticationAsync("sign-in");
        }

        internal static Task<NativePackageBrokerAuthenticationResult>
            SignOutAsync()
        {
            return ExecuteAuthenticationAsync("sign-out");
        }

        private static async Task<
            NativePackageBrokerAuthenticationResult>
            ExecuteAuthenticationAsync(string action)
        {
            EnsureSupportedClientPlatform();
            try
            {
                return await NativePackageBrokerClient.AuthenticateAsync(
                    action);
            }
            catch (NativePackageBrokerException failure)
                when (string.Equals(
                    failure.ErrorCode,
                    "BROKER_UNAVAILABLE",
                    StringComparison.Ordinal))
            {
                string traceId = Guid.NewGuid().ToString("N");
                using (var cancellation =
                    new CancellationTokenSource(BrokerBootstrapTimeout))
                {
                    await ProductionRuntimeBootstrap.EnsureAsync(
                        traceId,
                        cancellation.Token);
                }
                return await NativePackageBrokerClient.AuthenticateAsync(
                    action);
            }
        }

        internal static async Task<PackageLifecycleInstallResult> TryInstallAsync(
            AliasPackageContract alias,
            Action<PackageLifecycleUserProgress> reportProgress)
        {
            string projectPath = null;
            string attemptKey = null;
            string lifecycleRunId = null;
            try
            {
                ValidateAlias(alias);
                EnsureSupportedClientPlatform();
                projectPath = CurrentProjectPath();
                string pendingOperation = GetPendingOperation(
                    projectPath,
                    alias.aliasId);
                if (pendingOperation != null &&
                    !PendingOperationMatches(pendingOperation, "install") &&
                    !PendingOperationMatches(pendingOperation, "update"))
                {
                    throw new InvalidOperationException(
                        "Another package action must finish before installation can start.");
                }
                if (pendingOperation != null)
                {
                    PackageLifecycleInstallResult pending =
                        await TryResumePendingAsync(alias, reportProgress);
                    if (pending != null)
                    {
                        return pending;
                    }
                }
                Report(
                    reportProgress,
                    "checking-access",
                    alias?.packageDisplayName);
                attemptKey = alias.aliasId;
                string currentReleaseRoot = GetCurrentReleaseRoot(
                    projectPath,
                    alias.aliasId);
                PackageDeliveryInstallState currentState = ReadInstallState(
                    projectPath,
                    alias.aliasId,
                    false);
                string lifecycleId =
                    PackageLifecycleCheckpointStore.GetOrCreateAttemptId(
                        projectPath,
                        attemptKey);
                lifecycleRunId = lifecycleId + "-execute";
                Report(
                    reportProgress,
                    "checking-package",
                    alias.packageDisplayName);
                PackageLifecycleExecutionResult preflight = await ExecuteAsync(
                    alias,
                    "preflight",
                    lifecycleId + "-preflight",
                    BuildOperationIdempotencyKey(
                        lifecycleId,
                        "preflight"),
                    currentReleaseRoot,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    reportProgress);
                if (currentReleaseRoot != EmptyReleaseRoot &&
                    string.Equals(
                        preflight.targetReleaseRoot,
                        currentReleaseRoot,
                        StringComparison.Ordinal))
                {
                    AliasPackageActivationStateStore.MarkHandled(
                        projectPath,
                        alias,
                        "update");
                    PackageLifecycleCheckpointStore.ClearAttemptId(
                        projectPath,
                        attemptKey);
                    return new PackageLifecycleInstallResult
                    {
                        alreadyInstalled = true,
                        succeeded = true,
                    };
                }
                if (RequiresActiveContentApproval(
                        preflight.activeContentDigest,
                        preflight.activePolicyVersion,
                        currentState))
                {
                    PackageActiveContentReview review = BuildActiveContentReview(
                        alias.packageDisplayName,
                        preflight.activePolicyVersion,
                        preflight.activeContentDigest);
                    bool approved = await PackageChangePlanReview
                        .RequestApprovalAsync(
                            review.title,
                            review.message,
                            review.approveLabel,
                            review.cancelLabel);
                    if (!approved)
                    {
                        PackageLifecycleCheckpointStore.ClearAttemptId(
                            projectPath,
                            attemptKey);
                        return new PackageLifecycleInstallResult
                        {
                            cancelled = true,
                            errorMessage = "Installation was canceled.",
                        };
                    }
                }
                PackageChangePlan changePlan =
                    PackageChangePlanBuilder.Build(
                        projectPath,
                        currentReleaseRoot,
                        preflight.targetReleaseRoot,
                        preflight.versionId,
                        preflight.files,
                        currentState?.files);
                List<string> dirtyAssets =
                    PackageChangePlanBuilder.FindDirtyAffectedAssets(
                        changePlan);
                string requestedVersion = BuildRequestedTargetLabel(
                    currentState?.version,
                    alias.bootstrapIntent);
                bool approvedChanges =
                    await PackageChangePlanReview.RequestChangePlanAsync(
                        changePlan,
                        dirtyAssets,
                        requestedVersion);
                if (!approvedChanges)
                {
                    PackageLifecycleCheckpointStore.ClearAttemptId(
                        projectPath,
                        attemptKey);
                    return new PackageLifecycleInstallResult
                    {
                        cancelled = true,
                        errorMessage =
                            dirtyAssets.Count > 0
                                ? "Save or revert the affected Unity assets, then retry."
                                : changePlan.HasBlockedCollisions
                                    ? "Resolve the unowned file collisions, then retry."
                                    : "Installation was canceled.",
                    };
                }
                string operation = currentReleaseRoot == EmptyReleaseRoot
                    ? "install"
                    : "update";
                Report(
                    reportProgress,
                    "downloading",
                    alias.packageDisplayName);
                await ExecuteAsync(
                    alias,
                    operation,
                    lifecycleRunId,
                    BuildOperationIdempotencyKey(
                        lifecycleId,
                        operation),
                    currentReleaseRoot,
                    preflight.targetReleaseRoot,
                    preflight.activeContentDigest,
                    preflight.activePolicyVersion,
                    reportProgress,
                    changePlan.reviewDigest,
                    changePlan.signature);
                Report(
                    reportProgress,
                    "finishing",
                    alias.packageDisplayName);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                PackageLifecycleCheckpointStore.ClearAttemptId(
                    projectPath,
                    attemptKey);
                return new PackageLifecycleInstallResult
                {
                    succeeded = true,
                };
            }
            catch (Exception exception)
            {
                ClearAttemptIdWhenTerminal(
                    projectPath,
                    attemptKey,
                    lifecycleRunId);
                string errorCode = GetDiagnosticErrorCode(exception);
                string traceId = GetDiagnosticTraceId(exception);
                LogInstallDiagnostic(errorCode, traceId, exception);
                return new PackageLifecycleInstallResult
                {
                    errorCode = errorCode,
                    errorMessage = BuildUserFacingFailureMessage(exception),
                    traceId = traceId,
                };
            }
        }

        internal static async Task<PackageLifecycleInstallResult>
            TryManageInstalledAsync(
                AliasPackageContract alias,
                string operation,
                Action<PackageLifecycleUserProgress> reportProgress)
        {
            if (string.Equals(operation, "update", StringComparison.Ordinal))
            {
                return await TryInstallAsync(
                    alias,
                    reportProgress);
            }
            string projectPath = null;
            string attemptKey = null;
            string lifecycleRunId = null;
            try
            {
                ValidateAlias(alias);
                EnsureSupportedClientPlatform();
                if (operation != "repair" &&
                    operation != "rollback" &&
                    operation != "uninstall")
                {
                    throw new InvalidOperationException(
                        "The package management action is unsupported.");
                }
                projectPath = CurrentProjectPath();
                string pendingOperation = GetPendingOperation(
                    projectPath,
                    alias.aliasId);
                if (pendingOperation != null &&
                    !PendingOperationMatches(
                        pendingOperation,
                        operation))
                {
                    throw new InvalidOperationException(
                        "Another package action must finish before this action can start.");
                }
                if (pendingOperation != null)
                {
                    PackageLifecycleInstallResult pending =
                        await TryResumePendingAsync(
                            alias,
                            reportProgress);
                    if (pending != null)
                    {
                        return pending;
                    }
                }
                attemptKey = alias.aliasId + "." + operation;
                PackageDeliveryInstallState current = ReadInstallState(
                    projectPath,
                    alias.aliasId,
                    true);
                if (operation == "rollback" &&
                    string.IsNullOrWhiteSpace(current.previousReleaseRoot))
                {
                    throw new InvalidOperationException(
                        "There is no earlier package version to restore.");
                }
                string runId =
                    PackageLifecycleCheckpointStore.GetOrCreateAttemptId(
                        projectPath,
                        attemptKey);
                lifecycleRunId = runId;
                string targetReleaseRoot = operation == "repair"
                    ? current.releaseRoot
                    : operation == "rollback"
                        ? current.previousReleaseRoot
                        : string.Empty;
                string approvedDigest = operation == "rollback"
                    ? current.previousActiveContentDigest
                    : current.activeContentDigest;
                string approvedPolicy = operation == "rollback"
                    ? current.previousActivePolicyVersion
                    : current.activePolicyVersion;
                List<NativePackageBrokerFile> reviewedTargetFiles =
                    operation == "uninstall"
                        ? new List<NativePackageBrokerFile>()
                        : operation == "rollback"
                            ? current.previousFiles
                            : current.files;
                if (operation == "rollback" &&
                    (reviewedTargetFiles == null ||
                     reviewedTargetFiles.Count == 0))
                {
                    throw new InvalidOperationException(
                        "The earlier release has no retained file inventory.");
                }
                PackageChangePlan changePlan =
                    PackageChangePlanBuilder.Build(
                        projectPath,
                        current.releaseRoot,
                        operation == "uninstall"
                            ? EmptyReleaseRoot
                            : targetReleaseRoot,
                        operation == "rollback"
                            ? current.previousVersionId
                            : current.versionId,
                        reviewedTargetFiles,
                        current.files);
                List<string> dirtyAssets =
                    PackageChangePlanBuilder.FindDirtyAffectedAssets(
                        changePlan);
                bool approvedChanges =
                    await PackageChangePlanReview.RequestChangePlanAsync(
                        changePlan,
                        dirtyAssets,
                        operation == "uninstall"
                            ? "Uninstall the current package release"
                            : operation == "rollback"
                                ? "Restore " +
                                    (string.IsNullOrWhiteSpace(
                                        current.previousVersion)
                                        ? "the earlier retained release"
                                        : current.previousVersion)
                                : "Repair the current package release");
                if (!approvedChanges)
                {
                    PackageLifecycleCheckpointStore.ClearAttemptId(
                        projectPath,
                        attemptKey);
                    return new PackageLifecycleInstallResult
                    {
                        cancelled = true,
                        errorMessage =
                            dirtyAssets.Count > 0
                                ? "Save or revert the affected Unity assets, then retry."
                                : changePlan.HasBlockedCollisions
                                    ? "Resolve the unowned file collisions, then retry."
                                    : "Package action was canceled.",
                    };
                }
                Report(
                    reportProgress,
                    operation == "uninstall"
                        ? "updating-project"
                        : "checking-package",
                    alias.packageDisplayName);
                await ExecuteAsync(
                    alias,
                    operation,
                    runId,
                    runId,
                    current.releaseRoot,
                    targetReleaseRoot,
                    approvedDigest,
                    approvedPolicy,
                    reportProgress,
                    changePlan.reviewDigest,
                    changePlan.signature);
                Report(
                    reportProgress,
                    "finishing",
                    alias.packageDisplayName);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                PackageLifecycleCheckpointStore.ClearAttemptId(
                    projectPath,
                    attemptKey);
                return new PackageLifecycleInstallResult
                {
                    succeeded = true,
                };
            }
            catch (Exception exception)
            {
                ClearAttemptIdWhenTerminal(
                    projectPath,
                    attemptKey,
                    lifecycleRunId);
                string errorCode = GetDiagnosticErrorCode(exception);
                string traceId = GetDiagnosticTraceId(exception);
                LogInstallDiagnostic(errorCode, traceId, exception);
                return new PackageLifecycleInstallResult
                {
                    errorCode = errorCode,
                    errorMessage = BuildUserFacingFailureMessage(exception),
                    traceId = traceId,
                };
            }
        }

        private static bool PendingOperationMatches(
            string pendingOperation,
            string requestedOperation)
        {
            return string.Equals(
                pendingOperation,
                requestedOperation,
                StringComparison.Ordinal);
        }

        internal static void ClearAttemptIdWhenTerminal(
            string projectPath,
            string attemptKey,
            string runId)
        {
            if (string.IsNullOrWhiteSpace(projectPath) ||
                string.IsNullOrWhiteSpace(attemptKey))
            {
                return;
            }
            try
            {
                if (!string.IsNullOrWhiteSpace(runId) &&
                    PackageLifecycleCheckpointStore.TryRead(
                        projectPath,
                        runId,
                        out PackageLifecycleCheckpoint checkpoint) &&
                    !string.Equals(
                        checkpoint.phase,
                        "verified",
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        checkpoint.phase,
                        "rolled-back",
                        StringComparison.Ordinal))
                {
                    return;
                }
                PackageLifecycleCheckpointStore.ClearAttemptId(
                    projectPath,
                    attemptKey);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "YUCP could not clear a completed package attempt: " +
                    exception.GetType().Name);
            }
        }

        private sealed class PendingLifecycleAttempt
        {
            internal string attemptKey;
            internal PackageLifecycleCheckpoint checkpoint;
            internal string runId;
        }

        internal static string GetPendingOperation(
            string projectPath,
            string aliasId)
        {
            return TryFindPendingAttempt(
                projectPath,
                aliasId,
                out PendingLifecycleAttempt pending)
                ? pending.checkpoint.operation
                : null;
        }

        internal static async Task<PackageLifecycleInstallResult>
            TryResumePendingAsync(
                AliasPackageContract alias,
                Action<PackageLifecycleUserProgress> reportProgress)
        {
            ValidateAlias(alias);
            EnsureSupportedClientPlatform();
            string projectPath = CurrentProjectPath();
            if (!TryFindPendingAttempt(
                    projectPath,
                    alias.aliasId,
                    out PendingLifecycleAttempt pending))
            {
                return null;
            }
            if (DiscardPreJournalCheckpoint(
                    projectPath,
                    pending.checkpoint))
            {
                PackageLifecycleCheckpointStore.ClearAttemptId(
                    projectPath,
                    pending.attemptKey);
                return null;
            }

            try
            {
                Report(
                    reportProgress,
                    "finishing",
                    alias.packageDisplayName);
                await CompleteCommittedCheckpointAsync(
                    projectPath,
                    pending.checkpoint);
                RequireSuccessfulAliasFinalized(
                    projectPath,
                    alias,
                    pending.checkpoint.operation);
                ClearAttemptIdWhenTerminal(
                    projectPath,
                    pending.attemptKey,
                    pending.runId);
                return new PackageLifecycleInstallResult
                {
                    succeeded = true,
                };
            }
            catch (Exception exception)
            {
                ClearAttemptIdWhenTerminal(
                    projectPath,
                    pending.attemptKey,
                    pending.runId);
                string errorCode = GetDiagnosticErrorCode(exception);
                string traceId = GetDiagnosticTraceId(exception);
                LogInstallDiagnostic(errorCode, traceId, exception);
                return new PackageLifecycleInstallResult
                {
                    errorCode = errorCode,
                    errorMessage = BuildUserFacingFailureMessage(exception),
                    traceId = traceId,
                };
            }
        }

        private static bool TryFindPendingAttempt(
            string projectPath,
            string aliasId,
            out PendingLifecycleAttempt pending)
        {
            pending = null;
            string[] attemptKeys =
            {
                aliasId,
                aliasId + ".repair",
                aliasId + ".rollback",
                aliasId + ".uninstall",
            };
            foreach (string attemptKey in attemptKeys)
            {
                if (!PackageLifecycleCheckpointStore.TryGetAttemptId(
                        projectPath,
                        attemptKey,
                        out string attemptId))
                {
                    continue;
                }
                string runId = string.Equals(
                        attemptKey,
                        aliasId,
                        StringComparison.Ordinal)
                    ? attemptId + "-execute"
                    : attemptId;
                if (!PackageLifecycleCheckpointStore.TryRead(
                        projectPath,
                        runId,
                        out PackageLifecycleCheckpoint checkpoint))
                {
                    continue;
                }
                if (!string.Equals(
                        checkpoint.aliasId,
                        aliasId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The pending package operation identity is invalid.");
                }
                if (pending != null)
                {
                    throw new InvalidDataException(
                        "Multiple package operations require recovery.");
                }
                pending = new PendingLifecycleAttempt
                {
                    attemptKey = attemptKey,
                    checkpoint = checkpoint,
                    runId = runId,
                };
            }
            return pending != null;
        }

        internal static async Task<PackageLifecycleExecutionResult> ExecuteAsync(
            AliasPackageContract alias,
            string operation,
            string runId,
            string idempotencyKey,
            string expectedCurrentReleaseRoot,
            string targetReleaseRoot,
            string approvedActiveContentDigest,
            string approvedPolicyVersion,
            Action<PackageLifecycleUserProgress> reportProgress = null,
            string approvedChangePlanDigest = "",
            string approvedChangePlanSignature = "")
        {
            ValidateAlias(alias);
            EnsureSupportedClientPlatform();
            string projectPath = CurrentProjectPath();
            PackageLifecycleExecutionResult resumed =
                string.Equals(
                    operation,
                    "recover",
                    StringComparison.Ordinal)
                    ? null
                    : await TryResumeAsync(
                        projectPath,
                        alias,
                        operation,
                        runId,
                        expectedCurrentReleaseRoot,
                        reportProgress);
            if (resumed != null)
            {
                RequireSuccessfulAliasFinalized(
                    projectPath,
                    alias,
                    operation);
                return resumed;
            }
            PackageLifecycleExecutionResult result = await ExecuteCoreAsync(
                alias,
                operation,
                runId,
                idempotencyKey,
                expectedCurrentReleaseRoot,
                targetReleaseRoot,
                approvedActiveContentDigest,
                approvedPolicyVersion,
                reportProgress,
                approvedChangePlanDigest,
                approvedChangePlanSignature);
            RequireSuccessfulAliasFinalized(
                projectPath,
                alias,
                operation);
            return result;
        }

        internal static string FinalizeSuccessfulAliasOperation(
            string projectPath,
            AliasPackageContract alias,
            string operation)
        {
            if (string.Equals(
                    operation,
                    "preflight",
                    StringComparison.Ordinal))
            {
                return null;
            }
            if (alias == null ||
                string.IsNullOrWhiteSpace(alias.packageName))
            {
                return "The VPM bootstrap identity is invalid.";
            }

            string packagePath = Path.Combine(
                Path.GetFullPath(projectPath),
                "Packages",
                alias.packageName);
            bool directUnityPackageBootstrap =
                alias.directUnityPackageBootstrap ||
                DirectUnityPackageBootstrapStore.Contains(
                    projectPath,
                    alias);
            if (!directUnityPackageBootstrap &&
                !Directory.Exists(packagePath))
            {
                return "The VPM bootstrap is no longer registered.";
            }

            AliasPackageActivationStateStore.MarkHandled(
                projectPath,
                alias,
                operation);
            return null;
        }

        private static void RequireSuccessfulAliasFinalized(
            string projectPath,
            AliasPackageContract alias,
            string operation)
        {
            string cleanupError = FinalizeSuccessfulAliasOperation(
                projectPath,
                alias,
                operation);
            if (!string.IsNullOrWhiteSpace(cleanupError))
            {
                throw new InvalidOperationException(
                    "The package operation succeeded, but its VPM bootstrap " +
                    "is not registered. " +
                    cleanupError);
            }
        }

        private static async Task<PackageLifecycleExecutionResult> ExecuteCoreAsync(
            AliasPackageContract alias,
            string operation,
            string runId,
            string idempotencyKey,
            string expectedCurrentReleaseRoot,
            string targetReleaseRoot,
            string approvedActiveContentDigest,
            string approvedPolicyVersion,
            Action<PackageLifecycleUserProgress> reportProgress,
            string approvedChangePlanDigest = "",
            string approvedChangePlanSignature = "")
        {
            ValidateAlias(alias);
            string projectPath = CurrentProjectPath();
            PackageDeliveryInstallState current = ReadInstallState(
                projectPath,
                alias.aliasId,
                false);
            string currentReleaseRoot = current?.releaseRoot ?? EmptyReleaseRoot;
            if (!IsSha256(expectedCurrentReleaseRoot) ||
                !string.Equals(
                    currentReleaseRoot,
                    expectedCurrentReleaseRoot,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The expected current release root is stale.");
            }
            ValidateOperationState(
                operation,
                currentReleaseRoot,
                targetReleaseRoot,
                approvedActiveContentDigest,
                approvedPolicyVersion);

            string exactTargetReleaseRoot = ResolveExactTargetReleaseRoot(
                operation,
                currentReleaseRoot,
                targetReleaseRoot);
            NativePackageBrokerRequest brokerRequest = BuildBrokerRequest(
                alias.aliasId,
                currentReleaseRoot,
                runId,
                idempotencyKey,
                operation,
                projectPath,
                exactTargetReleaseRoot,
                approvedActiveContentDigest,
                approvedPolicyVersion);
            brokerRequest.bootstrapIntentJson =
                alias.bootstrapIntent?.rawIntentJson ?? string.Empty;
            Action<NativePackageBrokerProgress> brokerProgress =
                progress => Report(
                    reportProgress,
                    BuildBrokerProgress(
                        progress,
                        alias.packageDisplayName,
                        operation == "preflight"));
            NativePackageBrokerResult broker =
                await ExecuteBrokerWithBootstrapAsync(
                    brokerRequest,
                    ProductionRuntimeBootstrap,
                    brokerProgress,
                    reportProgress,
                    CancellationToken.None);
            NativePackageBrokerClient.ValidateResult(
                brokerRequest,
                broker);
            if (!string.Equals(
                    broker.status,
                    "succeeded",
                    StringComparison.Ordinal))
            {
                throw new NativePackageBrokerException(
                    broker.errorCode,
                    broker.traceId,
                    string.IsNullOrWhiteSpace(broker.errorMessage)
                        ? "The package action could not finish."
                        : broker.errorMessage);
            }
            Report(
                reportProgress,
                operation == "preflight"
                    ? "checking-package"
                    : "verifying-files",
                alias.packageDisplayName);
            PackageLifecycleExecutionResult result = BuildExecutionResult(
                broker,
                currentReleaseRoot,
                broker.targetReleaseRoot);
            if (operation == "preflight")
            {
                return result;
            }

            if (operation == "recover")
            {
                ProjectTransactionResult recovered =
                    ProjectTransactionJournal.Recover(projectPath, runId);
                ProjectTransactionInspection recoveryInspection =
                    ProjectTransactionJournal.Inspect(projectPath, runId);
                if (recoveryInspection.requiresPackageResolution)
                {
                    await EmbeddedPackageResolver.ResolveAsync();
                }
                PackageDeliveryInstallState recoveredState = ReadInstallState(
                    projectPath,
                    alias.aliasId,
                    false);
                string recoveredReleaseRoot =
                    recoveredState?.releaseRoot ?? EmptyReleaseRoot;
                if (!string.Equals(
                        recoveredReleaseRoot,
                        broker.targetReleaseRoot,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The recovered package version does not match " +
                        "the authorized version.");
                }
                await PackageImportVerifier.ImportAndVerify(
                    projectPath,
                    recoveredState?.files);
                result.currentReleaseRoot = recoveredReleaseRoot;
                result.journalId = runId;
                result.journalState = recovered.state;
                result.targetReleaseRoot = recoveredReleaseRoot;
                result.versionId =
                    recoveredState?.versionId ?? broker.versionId;
                return result;
            }

            if (operation == "uninstall")
            {
                if (current == null)
                {
                    throw new InvalidOperationException(
                        "The package is not installed in this project.");
                }
                List<VerifiedStagingFile> owned = ToVerifiedFiles(
                    current.files);
                PackageChangePlan uninstallPlan =
                    PackageChangePlanBuilder.Build(
                        projectPath,
                        current.releaseRoot,
                        EmptyReleaseRoot,
                        current.versionId,
                        Array.Empty<NativePackageBrokerFile>(),
                        current.files);
                if (!string.IsNullOrWhiteSpace(
                        approvedChangePlanDigest) &&
                    !PackageChangePlanSigner.VerifyApproval(
                        uninstallPlan,
                        approvedChangePlanDigest,
                        approvedChangePlanSignature))
                {
                    throw new InvalidOperationException(
                        "The signed package plan is invalid or the project " +
                        "changed after review. Review the operation again.");
                }
                if (uninstallPlan.HasBlockedCollisions ||
                    PackageChangePlanBuilder
                        .FindDirtyAffectedAssets(uninstallPlan).Count > 0)
                {
                    throw new InvalidOperationException(
                        "The project changed after the uninstall review.");
                }
                owned.Add(ReadInstallStateRecord(
                    projectPath,
                    alias.aliasId));
                var uninstallCheckpoint = new PackageLifecycleCheckpoint
                {
                    aliasId = alias.aliasId,
                    brokerTraceId = broker.traceId,
                    expectedCurrentReleaseRoot =
                        expectedCurrentReleaseRoot,
                    operation = operation,
                    phase = "awaiting-transaction",
                    priorState = current,
                    runId = runId,
                };
                PackageLifecycleCheckpointStore.Write(
                    projectPath,
                    uninstallCheckpoint);
                Report(
                    reportProgress,
                    "updating-project",
                    alias.packageDisplayName);
                ProjectTransactionResult removed =
                    ProjectTransactionJournal.RemoveOwnedFiles(
                        projectPath,
                        runId,
                        owned,
                        () => PackageModifiedAssetPreserver.Preserve(
                            projectPath,
                            alias.aliasId,
                            runId,
                            uninstallPlan));
                uninstallCheckpoint.phase = removed.state;
                PackageLifecycleCheckpointStore.Write(
                    projectPath,
                    uninstallCheckpoint);
                Report(
                    reportProgress,
                    "finishing",
                    alias.packageDisplayName);
                return await CompleteCommittedCheckpointAsync(
                    projectPath,
                    uninstallCheckpoint);
            }

            if (!IsSha256(broker.targetReleaseRoot) ||
                string.IsNullOrWhiteSpace(broker.stagingTree))
            {
                throw new InvalidDataException(
                    "The verified package delivery result is incomplete.");
            }
            string stagingRoot =
                ProjectTransactionJournal.RequireSafeStagingRoot(
                    projectPath,
                    broker.stagingTree);

            List<VerifiedStagingFile> targetFiles = ToVerifiedFiles(
                broker.files);
            PackageChangePlan executionPlan =
                PackageChangePlanBuilder.Build(
                    projectPath,
                    currentReleaseRoot,
                    broker.targetReleaseRoot,
                    broker.versionId,
                    broker.files,
                    current?.files);
            if (!string.IsNullOrWhiteSpace(
                    approvedChangePlanDigest) &&
                !PackageChangePlanSigner.VerifyApproval(
                    executionPlan,
                    approvedChangePlanDigest,
                    approvedChangePlanSignature))
            {
                throw new InvalidOperationException(
                    "The signed package plan is invalid or the project " +
                    "changed after review. Review the operation again.");
            }
            if (executionPlan.HasBlockedCollisions)
            {
                throw new IOException(
                    "An unowned project path collides with the package.");
            }
            if (PackageChangePlanBuilder
                .FindDirtyAffectedAssets(executionPlan).Count > 0)
            {
                throw new InvalidOperationException(
                    "An affected Unity asset became dirty after review.");
            }
            PackageImportVerifier.ValidateUnityPathCompatibility(
                projectPath,
                broker.files,
                Path.DirectorySeparatorChar == '\\');
            targetFiles.Add(WriteInstallState(
                stagingRoot,
                alias.aliasId,
                broker.targetReleaseRoot,
                broker.versionId,
                operation == "rollback"
                    ? current?.previousVersion ?? string.Empty
                    : ResolveTargetVersion(alias, broker.versionId),
                broker.receiptId,
                broker.receiptPath,
                broker.activeContentDigest,
                broker.activePolicyVersion,
                broker.files,
                current));
            List<VerifiedStagingFile> previousFiles = current == null
                ? new List<VerifiedStagingFile>()
                : ToVerifiedFiles(current.files);
            if (current != null)
            {
                previousFiles.Add(ReadInstallStateRecord(
                    projectPath,
                    alias.aliasId));
            }
            var lifecycleCheckpoint = new PackageLifecycleCheckpoint
            {
                activeContentDigest = broker.activeContentDigest,
                activePolicyVersion = broker.activePolicyVersion,
                aliasId = alias.aliasId,
                brokerTraceId = broker.traceId,
                expectedCurrentReleaseRoot = expectedCurrentReleaseRoot,
                operation = operation,
                phase = "awaiting-transaction",
                priorState = current,
                runId = runId,
                targetState = ReadInstallState(
                    stagingRoot,
                    alias.aliasId,
                    true),
            };
            PackageLifecycleCheckpointStore.Write(
                projectPath,
                lifecycleCheckpoint);
            Report(
                reportProgress,
                "updating-project",
                alias.packageDisplayName);
            ProjectTransactionResult transaction =
                ProjectTransactionJournal.Prepare(
                    projectPath,
                    stagingRoot,
                    runId,
                    targetFiles,
                    previousFiles);
            PackageModifiedAssetPreserver.Preserve(
                projectPath,
                alias.aliasId,
                runId,
                executionPlan);
            lifecycleCheckpoint.phase = transaction.state;
            PackageLifecycleCheckpointStore.Write(
                projectPath,
                lifecycleCheckpoint);
            transaction = ProjectTransactionJournal.Recover(
                projectPath,
                runId);
            lifecycleCheckpoint.phase = transaction.state;
            PackageLifecycleCheckpointStore.Write(
                projectPath,
                lifecycleCheckpoint);
            Report(
                reportProgress,
                "finishing",
                alias.packageDisplayName);
            return await CompleteCommittedCheckpointAsync(
                projectPath,
                lifecycleCheckpoint);
        }

        internal static async Task<NativePackageBrokerResult>
            ExecuteBrokerWithBootstrapAsync(
                NativePackageBrokerRequest request,
                INativePackageRuntimeBootstrap runtimeBootstrap,
                Action<NativePackageBrokerProgress> reportBrokerProgress,
                Action<PackageLifecycleUserProgress> reportUserProgress,
                CancellationToken cancellationToken)
        {
            if (runtimeBootstrap == null)
            {
                throw new ArgumentNullException(nameof(runtimeBootstrap));
            }

            bool preflight = string.Equals(
                request.operation,
                "preflight",
                StringComparison.Ordinal);
            string traceId = NativePackageBrokerClient.TraceId(
                request.traceparent);
            reportUserProgress?.Invoke(
                Progress(
                    "Preparing secure package delivery...",
                    preflight ? 0.21f : 0.41f));
            using (var bootstrapCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken))
            {
                bootstrapCancellation.CancelAfter(BrokerBootstrapTimeout);
                try
                {
                    await runtimeBootstrap.EnsureAsync(
                        traceId,
                        bootstrapCancellation.Token);
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    throw new NativePackageBrokerException(
                        "BROKER_BOOTSTRAP_TIMEOUT",
                        traceId,
                        "Secure package delivery setup timed out.");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception failure)
                {
                    throw new NativePackageBrokerException(
                        "BROKER_BOOTSTRAP_FAILED",
                        traceId,
                        "Secure package delivery setup failed.",
                        failure);
                }
            }

            try
            {
                return await NativePackageBrokerClient.ExecuteAsync(
                    request,
                    reportBrokerProgress,
                    cancellationToken);
            }
            catch (NativePackageBrokerException failure)
            {
                if (!string.Equals(
                        failure.ErrorCode,
                        "BROKER_UNAVAILABLE",
                        StringComparison.Ordinal))
                {
                    throw;
                }
            }

            return await NativePackageBrokerClient.ExecuteAsync(
                request,
                reportBrokerProgress,
                cancellationToken);
        }

        private static async Task<PackageLifecycleExecutionResult> TryResumeAsync(
            string projectPath,
            AliasPackageContract alias,
            string operation,
            string runId,
            string expectedCurrentReleaseRoot,
            Action<PackageLifecycleUserProgress> reportProgress)
        {
            if (!PackageLifecycleCheckpointStore.TryRead(
                    projectPath,
                    runId,
                    out PackageLifecycleCheckpoint checkpoint))
            {
                return null;
            }
            PackageLifecycleCheckpointStore.ValidateBinding(
                checkpoint,
                alias.aliasId,
                operation,
                expectedCurrentReleaseRoot);
            if (DiscardPreJournalCheckpoint(projectPath, checkpoint))
            {
                return null;
            }
            Report(
                reportProgress,
                "finishing",
                alias.packageDisplayName);
            return await CompleteCommittedCheckpointAsync(
                projectPath,
                checkpoint);
        }

        private static bool DiscardPreJournalCheckpoint(
            string projectPath,
            PackageLifecycleCheckpoint checkpoint)
        {
            if (!string.Equals(
                    checkpoint.phase,
                    "awaiting-transaction",
                    StringComparison.Ordinal) ||
                ProjectTransactionJournal.TryInspect(
                    projectPath,
                    checkpoint.runId,
                    out _))
            {
                return false;
            }
            PackageLifecycleCheckpointStore.Delete(
                projectPath,
                checkpoint.runId);
            return true;
        }

        private static async Task<PackageLifecycleExecutionResult>
            CompleteCommittedCheckpointAsync(
                string projectPath,
                PackageLifecycleCheckpoint checkpoint)
        {
            if (string.Equals(
                    checkpoint.phase,
                    "verified",
                    StringComparison.Ordinal))
            {
                return BuildCheckpointResult(checkpoint);
            }
            if (string.Equals(
                    checkpoint.phase,
                    "rolled-back",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    string.IsNullOrWhiteSpace(checkpoint.errorMessage)
                        ? "Unity rejected the package. The prior project was restored."
                        : checkpoint.errorMessage);
            }
            ProjectTransactionInspection inspection =
                ProjectTransactionJournal.Inspect(
                    projectPath,
                    checkpoint.runId);
            if (string.Equals(
                    checkpoint.phase,
                    "rolling-back",
                    StringComparison.Ordinal) ||
                string.Equals(
                    checkpoint.phase,
                    "rolled-back",
                    StringComparison.Ordinal) ||
                string.Equals(
                    inspection.state,
                    "rolling-back",
                    StringComparison.Ordinal) ||
                string.Equals(
                    inspection.state,
                    "rolled-back",
                    StringComparison.Ordinal))
            {
                if (string.Equals(
                    inspection.state,
                    "committed",
                    StringComparison.Ordinal))
                {
                    ProjectTransactionJournal.RollBackCommitted(
                        projectPath,
                        checkpoint.runId);
                }
                else if (string.Equals(
                    inspection.state,
                    "rolling-back",
                    StringComparison.Ordinal))
                {
                    ProjectTransactionJournal.Recover(
                        projectPath,
                        checkpoint.runId);
                }
                if (inspection.requiresPackageResolution)
                {
                    await EmbeddedPackageResolver.ResolveAsync();
                }
                inspection = ProjectTransactionJournal.Inspect(
                    projectPath,
                    checkpoint.runId);
                await VerifyRestoredState(
                    projectPath,
                    checkpoint,
                    inspection);
                checkpoint.phase = "rolled-back";
                PackageLifecycleCheckpointStore.Write(
                    projectPath,
                    checkpoint);
                throw new InvalidDataException(
                    string.IsNullOrWhiteSpace(checkpoint.errorMessage)
                        ? "Unity rejected the package. The prior project was restored."
                        : checkpoint.errorMessage);
            }
            if (string.Equals(
                    inspection.state,
                    "prepared",
                    StringComparison.Ordinal))
            {
                ProjectTransactionJournal.Recover(
                    projectPath,
                    checkpoint.runId);
                inspection = ProjectTransactionJournal.Inspect(
                    projectPath,
                    checkpoint.runId);
            }
            else if (!string.Equals(
                inspection.state,
                "committed",
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The package lifecycle transaction cannot resume.");
            }
            checkpoint.phase = "committed";
            PackageLifecycleCheckpointStore.Write(projectPath, checkpoint);
            try
            {
                if (inspection.requiresPackageResolution)
                {
                    await EmbeddedPackageResolver.ResolveAsync();
                }
                await VerifyCommittedState(
                    projectPath,
                    checkpoint,
                    inspection);
                checkpoint.phase = "verified";
                PackageLifecycleCheckpointStore.Write(
                    projectPath,
                    checkpoint);
                return BuildCheckpointResult(checkpoint);
            }
            catch (Exception importException)
            {
                checkpoint.errorMessage = BuildImportFailureMessage(
                    checkpoint.operation,
                    importException);
                checkpoint.phase = "rolling-back";
                PackageLifecycleCheckpointStore.Write(
                    projectPath,
                    checkpoint);
                ProjectTransactionJournal.RollBackCommitted(
                    projectPath,
                    checkpoint.runId);
                if (inspection.requiresPackageResolution)
                {
                    await EmbeddedPackageResolver.ResolveAsync();
                }
                inspection = ProjectTransactionJournal.Inspect(
                    projectPath,
                    checkpoint.runId);
                await VerifyRestoredState(
                    projectPath,
                    checkpoint,
                    inspection);
                checkpoint.phase = "rolled-back";
                PackageLifecycleCheckpointStore.Write(
                    projectPath,
                    checkpoint);
                throw new InvalidDataException(
                    checkpoint.errorMessage,
                    importException);
            }
        }

        internal static string BuildImportFailureMessage(
            string operation,
            Exception exception)
        {
            string summary = string.Equals(
                    operation,
                    "uninstall",
                    StringComparison.Ordinal)
                ? "Unity rejected the uninstall. The prior project was restored."
                : "Unity rejected the package. The prior project was restored.";
            Exception cause = exception;
            while (cause?.InnerException != null)
            {
                cause = cause.InnerException;
            }
            return cause == null || string.IsNullOrWhiteSpace(cause.Message)
                ? summary
                : summary + " Reason: " + cause.Message.Trim();
        }

        internal static PackageLifecycleUserProgress BuildProgress(
            string phase,
            string productName)
        {
            string name = string.IsNullOrWhiteSpace(productName)
                ? "your package"
                : "'" + productName.Trim() + "'";
            switch (phase)
            {
                case "checking-access":
                    return Progress(
                        $"Checking access to {name}...",
                        0.08f);
                case "checking-package":
                    return Progress(
                        $"Checking {name} before download...",
                        0.20f);
                case "downloading":
                    return Progress(
                        $"Downloading {name}...",
                        0.40f);
                case "verifying-files":
                    return Progress(
                        $"Checking the downloaded files for {name}...",
                        0.62f);
                case "updating-project":
                    return Progress(
                        $"Updating your Unity project with {name}...",
                        0.78f);
                case "finishing":
                    return Progress(
                        $"Finishing the installation of {name}...",
                        0.95f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(phase),
                        "The package progress phase is invalid.");
            }
        }

        internal static PackageLifecycleUserProgress BuildBrokerProgress(
            NativePackageBrokerProgress brokerProgress,
            string productName,
            bool preflight)
        {
            if (brokerProgress == null)
            {
                throw new ArgumentNullException(nameof(brokerProgress));
            }
            string name = string.IsNullOrWhiteSpace(productName)
                ? "your package"
                : "'" + productName.Trim() + "'";
            if (preflight)
            {
                switch (brokerProgress.phase)
                {
                    case "preparing":
                        return Progress($"Preparing to check {name}...", 0.22f);
                    case "signing-in":
                        return Progress("Opening secure sign-in...", 0.24f);
                    case "verifying-access":
                        return Progress(
                            "Waiting for purchase confirmation...",
                            0.26f);
                    case "downloading":
                        return Progress($"Checking {name}...", 0.28f);
                    case "verifying":
                        return Progress($"Checking {name}...", 0.30f);
                    case "assembling":
                        return Progress($"Preparing {name}...", 0.32f);
                    case "finalizing":
                        return Progress(
                            "Finishing the package check...",
                            0.34f);
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(brokerProgress),
                            "The package progress phase is invalid.");
                }
            }
            switch (brokerProgress.phase)
            {
                case "preparing":
                    return Progress($"Preparing {name}...", 0.36f);
                case "signing-in":
                    return Progress("Opening secure sign-in...", 0.38f);
                case "verifying-access":
                    return Progress(
                        "Waiting for purchase confirmation...",
                        0.40f);
                case "downloading":
                    float ratio = brokerProgress.totalBytes > 0
                        ? Mathf.Clamp01(
                            (float)brokerProgress.completedBytes /
                            brokerProgress.totalBytes)
                        : 0f;
                    string amount = BuildByteProgress(
                        brokerProgress.completedBytes,
                        brokerProgress.totalBytes,
                        ratio);
                    return Progress(
                        $"Downloading {name}...{amount}",
                        0.42f + ratio * 0.18f);
                case "verifying":
                    return Progress(
                        $"Checking the downloaded files for {name}...",
                        0.64f);
                case "assembling":
                    return Progress(
                        $"Preparing {name} for your project...",
                        0.70f);
                case "finalizing":
                    return Progress(
                        $"Finishing the download of {name}...",
                        0.74f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(brokerProgress),
                        "The package progress phase is invalid.");
            }
        }

        internal static string BuildByteProgress(
            long completedBytes,
            long totalBytes,
            float ratio)
        {
            if (completedBytes < 0 || totalBytes < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(completedBytes),
                    "Package byte progress cannot be negative.");
            }
            if (totalBytes == 0)
            {
                return completedBytes == 0
                    ? string.Empty
                    : $" {FormatByteCount(completedBytes)} downloaded";
            }
            int percent = (int)Math.Round(
                Mathf.Clamp01(ratio) * 100d,
                MidpointRounding.AwayFromZero);
            return
                $" {FormatByteCount(completedBytes)} of " +
                $"{FormatByteCount(totalBytes)} ({percent}%)";
        }

        internal static string FormatByteCount(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double amount = bytes;
            int unit = 0;
            while (amount >= 1024d && unit < units.Length - 1)
            {
                amount /= 1024d;
                unit++;
            }
            string format = amount >= 10d || amount % 1d == 0d
                ? "0"
                : "0.0";
            return amount.ToString(
                    format,
                    CultureInfo.InvariantCulture) +
                " " +
                units[unit];
        }

        internal static bool RequiresActiveContentApproval(
            string contentDigest,
            string policyVersion,
            PackageDeliveryInstallState currentState)
        {
            const string emptyInventoryDigest =
                "edd1cf6ff50c01be6abf064f586597fa770c00026deff3c68b9faeb5a8db9aef";
            if (!IsSha256(contentDigest) ||
                string.IsNullOrWhiteSpace(policyVersion))
            {
                throw new InvalidDataException(
                    "The package safety inventory is invalid.");
            }
            if (string.Equals(
                    contentDigest,
                    emptyInventoryDigest,
                    StringComparison.Ordinal) &&
                string.Equals(
                    policyVersion,
                    "active-content-policy-v1",
                    StringComparison.Ordinal))
            {
                return false;
            }
            return currentState == null ||
                !string.Equals(
                    currentState.activeContentDigest,
                    contentDigest,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    currentState.activePolicyVersion,
                    policyVersion,
                    StringComparison.Ordinal);
        }

        internal static string BuildUserFacingFailureMessage(Exception exception)
        {
            if (exception is NativePackageBrokerException delivery &&
                string.Equals(
                    delivery.ErrorCode,
                    "UNITY_WINDOWS_PATH_LIMIT",
                    StringComparison.Ordinal))
            {
                return "The Unity project path is too long for this package. " +
                    "Move the project to a shorter folder, then try again.";
            }
            if (exception is NativePackageBrokerException unavailable &&
                string.Equals(
                    unavailable.ErrorCode,
                    "BROKER_UNAVAILABLE",
                    StringComparison.Ordinal))
            {
                return "YUCP could not reach the package delivery service. " +
                    "Start YUCP, then try again.";
            }
            if (exception?.Message?.IndexOf(
                    "no earlier package version",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "There is no earlier package version to restore.";
            }
            if (exception is InvalidDataException ||
                exception is CryptographicException)
            {
                return "YUCP could not verify this package. " +
                    "Try again. If the problem continues, contact the creator.";
            }
            return "YUCP could not complete the package installation. " +
                "Try again if the problem continues.";
        }

        private static string GetDiagnosticErrorCode(Exception exception)
        {
            return exception is NativePackageBrokerException delivery
                ? delivery.ErrorCode
                : "PACKAGE_INSTALL_FAILED";
        }

        private static string GetDiagnosticTraceId(Exception exception)
        {
            return exception is NativePackageBrokerException delivery
                ? delivery.TraceId
                : string.Empty;
        }

        private static void LogInstallDiagnostic(
            string errorCode,
            string traceId,
            Exception exception)
        {
            var bootstrap =
                (exception as NativePackageBrokerException)
                    ?.InnerException
                as NativePackageRuntimeBootstrapException;
            Debug.LogError(JsonConvert.SerializeObject(new
            {
                eventName = "package_install_failed",
                errorCode,
                traceId,
                bootstrapErrorCode =
                    bootstrap?.ErrorCode ?? string.Empty,
                bootstrapDetail =
                    bootstrap?.Message ?? string.Empty,
            }));
        }

        internal static PackageActiveContentReview BuildActiveContentReview(
            string productName,
            string policyVersion,
            string contentDigest)
        {
            if (string.IsNullOrWhiteSpace(policyVersion) ||
                !IsSha256(contentDigest))
            {
                throw new InvalidOperationException(
                    "The package safety review binding is invalid.");
            }
            string name = string.IsNullOrWhiteSpace(productName)
                ? "This package"
                : productName.Trim();
            return new PackageActiveContentReview
            {
                approveLabel = "Continue with install",
                cancelLabel = "Cancel",
                message =
                    $"{name} includes scripts or other content that can " +
                    "change how your Unity project works.\n\n" +
                    "YUCP checked the package source. Continue only if " +
                    "you trust this creator.",
                title = "Review package safety",
            };
        }

        private static PackageLifecycleUserProgress Progress(
            string message,
            float progress)
        {
            return new PackageLifecycleUserProgress
            {
                message = message,
                progress = progress,
            };
        }

        private static void Report(
            Action<PackageLifecycleUserProgress> reportProgress,
            string phase,
            string productName)
        {
            reportProgress?.Invoke(BuildProgress(phase, productName));
        }

        private static void Report(
            Action<PackageLifecycleUserProgress> reportProgress,
            PackageLifecycleUserProgress progress)
        {
            reportProgress?.Invoke(progress);
        }

        private static async Task VerifyCommittedState(
            string projectPath,
            PackageLifecycleCheckpoint checkpoint,
            ProjectTransactionInspection inspection)
        {
            if (checkpoint.operation == "uninstall")
            {
                await PackageImportVerifier.ImportAndVerifyRemoval(
                    projectPath,
                    inspection.removedFiles,
                    inspection.preservedModifiedFiles);
                return;
            }
            if (checkpoint.targetState == null)
            {
                throw new InvalidDataException(
                    "The package lifecycle target state is missing.");
            }
            await PackageImportVerifier.ImportAndVerify(
                projectPath,
                checkpoint.targetState.files);
        }

        private static async Task VerifyRestoredState(
            string projectPath,
            PackageLifecycleCheckpoint checkpoint,
            ProjectTransactionInspection inspection)
        {
            IReadOnlyList<VerifiedStagingFile> preservedFiles =
                inspection?.preservedModifiedFiles != null
                    ? inspection.preservedModifiedFiles
                    : Array.Empty<VerifiedStagingFile>();
            if (checkpoint.priorState == null)
            {
                if (preservedFiles.Count != 0)
                {
                    throw new InvalidDataException(
                        "The rollback journal preserved an unexpected file.");
                }
                await PackageImportVerifier.ImportAndVerifyRemoval(
                    projectPath,
                    ToVerifiedFiles(checkpoint.targetState?.files));
                return;
            }
            var preservedByPath = preservedFiles.ToDictionary(
                file => file.normalizedPath,
                StringComparer.OrdinalIgnoreCase);
            var priorPaths = new HashSet<string>(
                checkpoint.priorState.files.Select(
                    file => file.normalizedPath),
                StringComparer.OrdinalIgnoreCase);
            if (preservedByPath.Keys.Any(path => !priorPaths.Contains(path)))
            {
                throw new InvalidDataException(
                    "The rollback journal preserved an unowned file.");
            }
            List<NativePackageBrokerFile> restoredFiles =
                checkpoint.priorState.files
                    .Select(file =>
                    {
                        if (!preservedByPath.TryGetValue(
                                file.normalizedPath,
                                out VerifiedStagingFile preserved))
                        {
                            return file;
                        }
                        return new NativePackageBrokerFile
                        {
                            bytes = preserved.bytes,
                            normalizedPath = preserved.normalizedPath,
                            sha256 = preserved.sha256,
                        };
                    })
                    .ToList();
            await PackageImportVerifier.ImportAndVerify(
                projectPath,
                restoredFiles);
        }

        private static PackageLifecycleExecutionResult BuildCheckpointResult(
            PackageLifecycleCheckpoint checkpoint)
        {
            PackageDeliveryInstallState state =
                checkpoint.operation == "uninstall"
                    ? null
                    : checkpoint.targetState;
            var receipts = new List<string>();
            if (!string.IsNullOrWhiteSpace(state?.receiptId))
            {
                receipts.Add(state.receiptId);
            }
            if (!string.IsNullOrWhiteSpace(state?.receiptPath))
            {
                receipts.Add(state.receiptPath);
            }
            return new PackageLifecycleExecutionResult
            {
                activeContentDigest = checkpoint.activeContentDigest,
                activePolicyVersion = checkpoint.activePolicyVersion,
                currentReleaseRoot =
                    state?.releaseRoot ?? EmptyReleaseRoot,
                journalId = checkpoint.runId,
                journalState = "committed",
                receiptReferences = receipts,
                targetReleaseRoot =
                    state?.releaseRoot ?? EmptyReleaseRoot,
                traceId = checkpoint.brokerTraceId,
                versionId =
                    state?.versionId ??
                    checkpoint.priorState?.versionId ??
                    string.Empty,
            };
        }

        internal static string GetCurrentReleaseRoot(
            string projectPath,
            string aliasId)
        {
            return ReadInstallState(projectPath, aliasId, false)?.releaseRoot ??
                EmptyReleaseRoot;
        }

        internal static bool HasPriorRelease(
            string projectPath,
            string aliasId)
        {
            PackageDeliveryInstallState state = ReadInstallState(
                projectPath,
                aliasId,
                false);
            return state != null &&
                IsSha256(state.previousReleaseRoot) &&
                state.previousFiles != null &&
                state.previousFiles.Count > 0;
        }

        internal static string BuildRequestedTargetLabel(
            string currentVersion,
            BootstrapIntentContract intent)
        {
            if (intent == null ||
                !string.Equals(
                    intent.mode,
                    "specific",
                    StringComparison.Ordinal))
            {
                return "Latest stable resolved for this bootstrap";
            }
            string requestedVersion = intent.version ?? string.Empty;
            if (TryCompareSemanticVersions(
                    requestedVersion,
                    currentVersion,
                    out int comparison) &&
                comparison < 0)
            {
                return "Downgrade to " + requestedVersion;
            }
            return "Update to " + requestedVersion;
        }

        private static string ResolveTargetVersion(
            AliasPackageContract alias,
            string versionId)
        {
            BootstrapIntentContract intent = alias?.bootstrapIntent;
            return intent != null &&
                string.Equals(
                    intent.mode,
                    "specific",
                    StringComparison.Ordinal) &&
                string.Equals(
                    intent.versionId,
                    versionId,
                    StringComparison.Ordinal)
                    ? intent.version ?? string.Empty
                    : string.Empty;
        }

        private static bool TryCompareSemanticVersions(
            string left,
            string right,
            out int comparison)
        {
            comparison = 0;
            if (!TryParseSemanticVersion(
                    left,
                    out string[] leftCore,
                    out string[] leftPrerelease) ||
                !TryParseSemanticVersion(
                    right,
                    out string[] rightCore,
                    out string[] rightPrerelease))
            {
                return false;
            }
            for (int index = 0; index < 3; index++)
            {
                comparison = CompareNumericIdentifier(
                    leftCore[index],
                    rightCore[index]);
                if (comparison != 0)
                {
                    return true;
                }
            }
            if (leftPrerelease.Length == 0 ||
                rightPrerelease.Length == 0)
            {
                comparison = leftPrerelease.Length ==
                        rightPrerelease.Length
                    ? 0
                    : leftPrerelease.Length == 0 ? 1 : -1;
                return true;
            }
            int identifiers = Math.Max(
                leftPrerelease.Length,
                rightPrerelease.Length);
            for (int index = 0; index < identifiers; index++)
            {
                if (index >= leftPrerelease.Length ||
                    index >= rightPrerelease.Length)
                {
                    comparison = index >= leftPrerelease.Length
                        ? -1
                        : 1;
                    return true;
                }
                string leftIdentifier = leftPrerelease[index];
                string rightIdentifier = rightPrerelease[index];
                bool leftNumeric = leftIdentifier.All(char.IsDigit);
                bool rightNumeric = rightIdentifier.All(char.IsDigit);
                if (leftNumeric && rightNumeric)
                {
                    comparison = CompareNumericIdentifier(
                        leftIdentifier,
                        rightIdentifier);
                }
                else if (leftNumeric != rightNumeric)
                {
                    comparison = leftNumeric ? -1 : 1;
                }
                else
                {
                    comparison = Math.Sign(string.CompareOrdinal(
                        leftIdentifier,
                        rightIdentifier));
                }
                if (comparison != 0)
                {
                    return true;
                }
            }
            return true;
        }

        private static bool TryParseSemanticVersion(
            string value,
            out string[] core,
            out string[] prerelease)
        {
            core = Array.Empty<string>();
            prerelease = Array.Empty<string>();
            if (string.IsNullOrWhiteSpace(value) ||
                !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                return false;
            }
            string withoutBuild = value.Split('+')[0];
            string[] releaseParts = withoutBuild.Split(
                new[] { '-' },
                2);
            core = releaseParts[0].Split('.');
            if (core.Length != 3 ||
                core.Any(identifier =>
                    string.IsNullOrEmpty(identifier) ||
                    !identifier.All(char.IsDigit) ||
                    identifier.Length > 1 &&
                    identifier[0] == '0'))
            {
                return false;
            }
            prerelease = releaseParts.Length == 2
                ? releaseParts[1].Split('.')
                : Array.Empty<string>();
            return prerelease.All(identifier =>
                !string.IsNullOrEmpty(identifier) &&
                identifier.All(character =>
                    char.IsLetterOrDigit(character) ||
                    character == '-') &&
                (!identifier.All(char.IsDigit) ||
                    identifier.Length == 1 ||
                    identifier[0] != '0'));
        }

        private static int CompareNumericIdentifier(
            string left,
            string right)
        {
            if (left.Length != right.Length)
            {
                return left.Length > right.Length ? 1 : -1;
            }
            return Math.Sign(string.CompareOrdinal(left, right));
        }

        internal static NativePackageBrokerRequest BuildBrokerRequest(
            string aliasId,
            string currentReleaseRoot,
            string runId,
            string idempotencyKey,
            string operation,
            string projectPath,
            string targetReleaseRoot,
            string approvedDigest,
            string approvedPolicy)
        {
            return new NativePackageBrokerRequest
            {
                aliasId = aliasId,
                approvedActiveContentDigest = approvedDigest,
                approvedPolicyVersion = approvedPolicy,
                expectedCurrentReleaseRoot = currentReleaseRoot,
                idempotencyKey = idempotencyKey,
                operation = operation,
                projectIdentity =
                    ProjectIdentityService.GetOrCreateProjectIdentity(
                        projectPath),
                projectPath = projectPath,
                runId = runId,
                targetReleaseRoot = targetReleaseRoot ?? string.Empty,
                traceparent =
                    NativePackageBrokerClient.CreateTraceparent(),
            };
        }

        internal static string BuildOperationIdempotencyKey(
            string attemptId,
            string operation)
        {
            if (!PackageProtocolIdentifier.IsSafe(attemptId) ||
                !PackageProtocolIdentifier.IsSafe(operation))
            {
                throw new InvalidDataException(
                    "The package operation idempotency identity is invalid.");
            }

            string key = attemptId + "-" + operation;
            if (!PackageProtocolIdentifier.IsSafe(key))
            {
                throw new InvalidDataException(
                    "The package operation idempotency key is invalid.");
            }
            return key;
        }

        private static PackageLifecycleExecutionResult BuildExecutionResult(
            NativePackageBrokerResult broker,
            string currentReleaseRoot,
            string targetReleaseRoot)
        {
            var receipts = new List<string>();
            if (!string.IsNullOrWhiteSpace(broker.receiptId))
            {
                receipts.Add(broker.receiptId);
            }
            if (!string.IsNullOrWhiteSpace(broker.receiptPath))
            {
                receipts.Add(broker.receiptPath);
            }
            return new PackageLifecycleExecutionResult
            {
                activeContentDigest = broker.activeContentDigest,
                activePolicyVersion = broker.activePolicyVersion,
                currentReleaseRoot = currentReleaseRoot,
                files = broker.files ?? new List<NativePackageBrokerFile>(),
                receiptReferences = receipts,
                journalState = broker.journalState,
                stagingTree = broker.stagingTree,
                targetReleaseRoot = targetReleaseRoot,
                traceId = broker.traceId,
                versionId = broker.versionId,
            };
        }

        private static List<VerifiedStagingFile> ToVerifiedFiles(
            IEnumerable<NativePackageBrokerFile> files)
        {
            return (files ?? Enumerable.Empty<NativePackageBrokerFile>())
                .Select(file => new VerifiedStagingFile
                {
                    bytes = file.bytes,
                    normalizedPath = file.normalizedPath,
                    sha256 = file.sha256,
                })
                .ToList();
        }

        private static VerifiedStagingFile WriteInstallState(
            string stagingTree,
            string aliasId,
            string releaseRoot,
            string versionId,
            string version,
            string receiptId,
            string receiptPath,
            string activeContentDigest,
            string activePolicyVersion,
            List<NativePackageBrokerFile> files,
            PackageDeliveryInstallState priorState)
        {
            string relativePath = InstallStatePath(aliasId);
            string destination = Path.Combine(
                stagingTree,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            bool changedRelease = priorState != null &&
                !string.Equals(
                    priorState.releaseRoot,
                    releaseRoot,
                    StringComparison.Ordinal);
            var state = new PackageDeliveryInstallState
            {
                activeContentDigest = activeContentDigest,
                activePolicyVersion = activePolicyVersion,
                aliasId = aliasId,
                files = files.Select(file => new NativePackageBrokerFile
                {
                    bytes = file.bytes,
                    normalizedPath = file.normalizedPath,
                    sha256 = file.sha256,
                }).ToList(),
                receiptId = receiptId ?? string.Empty,
                receiptPath = receiptPath ?? string.Empty,
                releaseRoot = releaseRoot,
                version = version ?? string.Empty,
                versionId = versionId,
                previousActiveContentDigest =
                    changedRelease
                        ? priorState.activeContentDigest
                        : priorState?.previousActiveContentDigest ??
                            string.Empty,
                previousActivePolicyVersion =
                    changedRelease
                        ? priorState.activePolicyVersion
                        : priorState?.previousActivePolicyVersion ??
                            string.Empty,
                previousReleaseRoot =
                    changedRelease
                        ? priorState.releaseRoot
                        : priorState?.previousReleaseRoot ?? string.Empty,
                previousVersion =
                    changedRelease
                        ? priorState.version
                        : priorState?.previousVersion ?? string.Empty,
                previousVersionId =
                    changedRelease
                        ? priorState.versionId
                        : priorState?.previousVersionId ?? string.Empty,
                previousFiles =
                    changedRelease
                        ? CloneBrokerFiles(priorState.files)
                        : CloneBrokerFiles(priorState?.previousFiles),
            };
            File.WriteAllText(
                destination,
                JsonConvert.SerializeObject(state, Formatting.Indented),
                new UTF8Encoding(false));
            return FileRecord(destination, relativePath);
        }

        private static List<NativePackageBrokerFile> CloneBrokerFiles(
            IEnumerable<NativePackageBrokerFile> files)
        {
            return (files ?? Enumerable.Empty<NativePackageBrokerFile>())
                .Select(file => new NativePackageBrokerFile
                {
                    bytes = file.bytes,
                    normalizedPath = file.normalizedPath,
                    sha256 = file.sha256,
                })
                .ToList();
        }

        private static PackageDeliveryInstallState ReadInstallState(
            string projectPath,
            string aliasId,
            bool required)
        {
            string statePath = Path.Combine(
                projectPath,
                InstallStatePath(aliasId).Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(statePath))
            {
                if (required)
                {
                    throw new FileNotFoundException(
                        "The package delivery install state does not exist.",
                        statePath);
                }
                return null;
            }
            PackageDeliveryInstallState state =
                JsonConvert.DeserializeObject<PackageDeliveryInstallState>(
                    File.ReadAllText(statePath));
            if (state == null ||
                state.schemaVersion != 5 ||
                !IsSha256(state.activeContentDigest) ||
                string.IsNullOrWhiteSpace(state.activePolicyVersion) ||
                !string.Equals(state.aliasId, aliasId, StringComparison.Ordinal) ||
                !IsSha256(state.releaseRoot) ||
                string.IsNullOrWhiteSpace(state.versionId) ||
                state.files == null ||
                state.files.Count == 0 ||
                state.previousFiles == null ||
                !IsValidPriorReleaseState(state) ||
                state.files.Concat(state.previousFiles).Any(file =>
                    file == null ||
                    file.bytes < 0 ||
                    !IsSha256(file.sha256) ||
                    string.IsNullOrWhiteSpace(file.normalizedPath)))
            {
                throw new InvalidDataException(
                    "The package delivery install state is invalid.");
            }
            return state;
        }

        private static bool IsValidPriorReleaseState(
            PackageDeliveryInstallState state)
        {
            bool hasPrior = !string.IsNullOrWhiteSpace(
                state.previousReleaseRoot);
            if (!hasPrior)
            {
                return string.IsNullOrEmpty(state.previousVersionId) &&
                    string.IsNullOrEmpty(
                        state.previousActiveContentDigest) &&
                    string.IsNullOrEmpty(
                        state.previousActivePolicyVersion);
            }
            return IsSha256(state.previousReleaseRoot) &&
                !string.IsNullOrWhiteSpace(state.previousVersionId) &&
                IsSha256(state.previousActiveContentDigest) &&
                !string.IsNullOrWhiteSpace(
                    state.previousActivePolicyVersion);
        }

        private static VerifiedStagingFile ReadInstallStateRecord(
            string projectPath,
            string aliasId)
        {
            string relativePath = InstallStatePath(aliasId);
            string path = Path.Combine(
                projectPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            return FileRecord(path, relativePath);
        }

        private static VerifiedStagingFile FileRecord(
            string path,
            string normalizedPath)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                throw new FileNotFoundException(
                    "An owned package file is missing.",
                    path);
            }
            return new VerifiedStagingFile
            {
                bytes = info.Length,
                normalizedPath = normalizedPath,
                sha256 = Sha256(path),
            };
        }

        internal static string InstallStatePath(string aliasId)
        {
            if (string.IsNullOrWhiteSpace(aliasId) ||
                aliasId.Any(character =>
                    !char.IsLetterOrDigit(character) &&
                    character != '.' &&
                    character != '_' &&
                    character != '-'))
            {
                throw new InvalidOperationException(
                    "The package alias identifier is invalid.");
            }
            string digest;
            using (SHA256 sha256 = SHA256.Create())
            {
                digest = BitConverter.ToString(
                        sha256.ComputeHash(
                            Encoding.UTF8.GetBytes(
                                "yucp:package-install-state:v1\n" +
                                aliasId)))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
            return $".yucp/package-installs/{digest}.json";
        }

        private static string ResolveExactTargetReleaseRoot(
            string operation,
            string currentReleaseRoot,
            string requestedTargetReleaseRoot)
        {
            if (operation == "repair")
            {
                return currentReleaseRoot;
            }
            if (operation == "rollback")
            {
                return requestedTargetReleaseRoot;
            }
            return string.IsNullOrEmpty(requestedTargetReleaseRoot)
                ? string.Empty
                : requestedTargetReleaseRoot;
        }

        private static void ValidateOperationState(
            string operation,
            string currentReleaseRoot,
            string targetReleaseRoot,
            string approvedDigest,
            string approvedPolicy)
        {
            string[] supported =
            {
                "preflight",
                "install",
                "update",
                "repair",
                "rollback",
                "uninstall",
                "recover",
            };
            if (!supported.Contains(operation))
            {
                throw new InvalidOperationException(
                    "The package lifecycle operation is unsupported.");
            }
            bool installed = currentReleaseRoot != EmptyReleaseRoot;
            if (operation == "install" && installed)
            {
                throw new InvalidOperationException(
                    "Install requires an empty package state.");
            }
            if ((operation == "update" ||
                    operation == "repair" ||
                    operation == "rollback" ||
                    operation == "uninstall") &&
                !installed)
            {
                throw new InvalidOperationException(
                    "The package lifecycle operation requires an installed release.");
            }
            if (operation == "rollback" && !IsSha256(targetReleaseRoot))
            {
                throw new InvalidOperationException(
                    "Rollback requires an exact retained release root.");
            }
            if (operation != "preflight" &&
                (!IsSha256(approvedDigest) ||
                    string.IsNullOrWhiteSpace(approvedPolicy)))
            {
                throw new InvalidOperationException(
                    "The active-content approval is invalid.");
            }
        }

        private static void ValidateAlias(AliasPackageContract alias)
        {
            if (!AliasPackageDiscovery.IsServerAuthorized(alias))
            {
                throw new InvalidOperationException(
                    "The package is not a complete server-authorized alias.");
            }
        }

        private static string CurrentProjectPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static bool IsSha256(string value)
        {
            return value != null &&
                value.Length == 64 &&
                value.All(character =>
                    character >= '0' && character <= '9' ||
                    character >= 'a' && character <= 'f');
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return string.Concat(
                    sha256.ComputeHash(stream).Select(value => value.ToString("x2")));
            }
        }
    }
}
