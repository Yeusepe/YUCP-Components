using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    [Serializable]
    internal sealed class PackageInstallSessionResponse
    {
        public string deliveryGrant = string.Empty;
        public string installSession = string.Empty;
        public string releaseRoot = string.Empty;
        public string versionId = string.Empty;
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
        public string versionId = string.Empty;
        public List<TransferHelperFile> files = new List<TransferHelperFile>();
    }

    internal sealed class PackageLifecycleExecutionResult
    {
        internal string activeContentDigest = string.Empty;
        internal string activePolicyVersion = string.Empty;
        internal string currentReleaseRoot = string.Empty;
        internal List<string> receiptReferences = new List<string>();
        internal string journalId = string.Empty;
        internal string journalState = string.Empty;
        internal string targetReleaseRoot = string.Empty;
        internal string traceId = string.Empty;
        internal string versionId = string.Empty;
    }

    internal static class PackageLifecycleCoordinator
    {
        internal const string EmptyReleaseRoot =
            "0000000000000000000000000000000000000000000000000000000000000000";
        private const string TrustTarget = "package-install-trust.json";
        private static readonly HttpClient HttpClient = new HttpClient();

        internal static async Task<string> TryInstallAsync(
            string serverUrl,
            AliasPackageContract alias)
        {
            try
            {
                string projectPath = CurrentProjectPath();
                string currentReleaseRoot = GetCurrentReleaseRoot(
                    projectPath,
                    alias.aliasId);
                string lifecycleId = Guid.NewGuid().ToString("N");
                PackageLifecycleExecutionResult preflight = await ExecuteAsync(
                    serverUrl,
                    alias,
                    "preflight",
                    lifecycleId + "-preflight",
                    lifecycleId,
                    currentReleaseRoot,
                    string.Empty,
                    string.Empty,
                    string.Empty);
                bool approved = EditorUtility.DisplayDialog(
                    "Approve Package Content",
                    "The signed package contains executable or active content.\n\n" +
                    $"Policy: {preflight.activePolicyVersion}\n" +
                    $"Digest: {preflight.activeContentDigest}\n\n" +
                    "Approve this exact signed inventory?",
                    "Approve and Install",
                    "Cancel");
                if (!approved)
                {
                    return "The active-content inventory was not approved.";
                }
                string operation = currentReleaseRoot == EmptyReleaseRoot
                    ? "install"
                    : "update";
                await ExecuteAsync(
                    serverUrl,
                    alias,
                    operation,
                    lifecycleId + "-execute",
                    lifecycleId,
                    currentReleaseRoot,
                    preflight.targetReleaseRoot,
                    preflight.activeContentDigest,
                    preflight.activePolicyVersion);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        internal static async Task<PackageLifecycleExecutionResult> ExecuteAsync(
            string serverUrl,
            AliasPackageContract alias,
            string operation,
            string runId,
            string idempotencyKey,
            string expectedCurrentReleaseRoot,
            string targetReleaseRoot,
            string approvedActiveContentDigest,
            string approvedPolicyVersion)
        {
            ValidateAlias(alias);
            string projectPath = CurrentProjectPath();
            PackageLifecycleExecutionResult resumed =
                await TryResumeAsync(
                    projectPath,
                    alias,
                    operation,
                    runId,
                    expectedCurrentReleaseRoot);
            if (resumed != null)
            {
                RequireSuccessfulAliasFinalized(
                    projectPath,
                    alias,
                    operation);
                return resumed;
            }
            string accessToken = null;
            bool hasResolvedAccessToken =
                operation != "recover" && operation != "uninstall";
            if (hasResolvedAccessToken)
            {
                Uri server = RequireServerOrigin(serverUrl);
                accessToken = await CreatorIdentityOAuthService
                    .GetValidAccessTokenAsync(
                        server.ToString(),
                        CreatorIdentityOAuthService
                            .PackageInstallationScopes);
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    throw new InvalidOperationException(
                        "Sign in through the importer before package installation.");
                }
            }
            PackageLifecycleExecutionResult result = await ExecuteCoreAsync(
                serverUrl,
                alias,
                operation,
                runId,
                idempotencyKey,
                expectedCurrentReleaseRoot,
                targetReleaseRoot,
                approvedActiveContentDigest,
                approvedPolicyVersion,
                accessToken,
                hasResolvedAccessToken);
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
            if (!Directory.Exists(packagePath))
            {
                return null;
            }

            return VpmBootstrapPackageCleanup.RemoveInstalledAlias(
                projectPath,
                alias.packageName);
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
                    "could not be removed. " +
                    cleanupError);
            }
        }

        private static async Task<PackageLifecycleExecutionResult> ExecuteCoreAsync(
            string serverUrl,
            AliasPackageContract alias,
            string operation,
            string runId,
            string idempotencyKey,
            string expectedCurrentReleaseRoot,
            string targetReleaseRoot,
            string approvedActiveContentDigest,
            string approvedPolicyVersion,
            string resolvedAccessToken,
            bool hasResolvedAccessToken)
        {
            ValidateAlias(alias);
            string projectPath = CurrentProjectPath();
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
                PackageImportVerifier.ImportAndVerify(
                    projectPath,
                    recoveredState?.files);
                return new PackageLifecycleExecutionResult
                {
                    currentReleaseRoot =
                        recoveredState?.releaseRoot ?? EmptyReleaseRoot,
                    journalId = runId,
                    journalState = recovered.state,
                    targetReleaseRoot =
                        recoveredState?.releaseRoot ?? EmptyReleaseRoot,
                    traceId = runId,
                    versionId = recoveredState?.versionId ?? string.Empty,
                };
            }
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

            if (operation == "uninstall")
            {
                if (current == null)
                {
                    throw new InvalidOperationException(
                        "The package is not installed in this project.");
                }
                List<VerifiedStagingFile> owned = ToVerifiedFiles(current.files);
                owned.Add(ReadInstallStateRecord(projectPath, alias.aliasId));
                var checkpoint = new PackageLifecycleCheckpoint
                {
                    aliasId = alias.aliasId,
                    expectedCurrentReleaseRoot = expectedCurrentReleaseRoot,
                    operation = operation,
                    priorState = current,
                    runId = runId,
                };
                PackageLifecycleCheckpointStore.Write(projectPath, checkpoint);
                ProjectTransactionResult removed =
                    ProjectTransactionJournal.RemoveOwnedFiles(
                        projectPath,
                        runId,
                        owned);
                checkpoint.phase = removed.state;
                PackageLifecycleCheckpointStore.Write(projectPath, checkpoint);
                return await CompleteCommittedCheckpointAsync(
                    projectPath,
                    checkpoint);
            }

            Uri server = RequireServerOrigin(serverUrl);
            string stateRoot = TransferHelperClient.ResolveStateRoot();
            TransferHelperDeviceInfo device =
                TransferHelperClient.ReadDeviceInfo(stateRoot, server);
            string accessToken = resolvedAccessToken;
            if (!hasResolvedAccessToken)
            {
                accessToken = CreatorIdentityOAuthService
                    .GetValidAccessTokenAsync(
                        server.ToString(),
                        CreatorIdentityOAuthService
                            .PackageInstallationScopes)
                    .GetAwaiter()
                    .GetResult();
            }
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException(
                    "Sign in through the importer before package installation.");
            }
            string exactTargetReleaseRoot = ResolveExactTargetReleaseRoot(
                operation,
                currentReleaseRoot,
                targetReleaseRoot);
            PackageInstallSessionResponse session = IssueSession(
                server,
                alias,
                device.deviceKeyThumbprint,
                accessToken,
                idempotencyKey,
                operation,
                exactTargetReleaseRoot);
            if (!string.IsNullOrEmpty(exactTargetReleaseRoot) &&
                !string.Equals(
                    session.releaseRoot,
                    exactTargetReleaseRoot,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The package server resolved a different release root.");
            }
            TransferHelperResult helper = TransferHelperClient.Execute(
                BuildRequest(
                    server,
                    alias.aliasId,
                    currentReleaseRoot,
                    runId,
                    operation == "preflight" ? "preflight" : operation,
                    projectPath,
                    session,
                    stateRoot,
                    approvedActiveContentDigest,
                    approvedPolicyVersion));
            PackageLifecycleExecutionResult result = BuildExecutionResult(
                helper,
                currentReleaseRoot,
                session.releaseRoot);
            if (operation == "preflight")
            {
                return result;
            }
            if (!IsSha256(helper.targetReleaseRoot) ||
                !string.Equals(
                    helper.targetReleaseRoot,
                    session.releaseRoot,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(helper.stagingTree))
            {
                throw new InvalidDataException(
                    "The verified package delivery result is incomplete.");
            }

            List<VerifiedStagingFile> targetFiles = ToVerifiedFiles(helper.files);
            PackageImportVerifier.ValidateUnityPathCompatibility(
                projectPath,
                helper.files,
                Path.DirectorySeparatorChar == '\\');
            targetFiles.Add(WriteInstallState(
                helper.stagingTree,
                alias.aliasId,
                session.releaseRoot,
                session.versionId,
                helper.receiptId,
                helper.receiptPath,
                helper.activeContentDigest,
                helper.activePolicyVersion,
                helper.files));
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
                activeContentDigest = helper.activeContentDigest,
                activePolicyVersion = helper.activePolicyVersion,
                aliasId = alias.aliasId,
                expectedCurrentReleaseRoot = expectedCurrentReleaseRoot,
                operation = operation,
                priorState = current,
                runId = runId,
                targetState = ReadInstallState(
                    helper.stagingTree,
                    alias.aliasId,
                    true),
            };
            PackageLifecycleCheckpointStore.Write(
                projectPath,
                lifecycleCheckpoint);
            ProjectTransactionResult transaction = ProjectTransactionJournal.Apply(
                projectPath,
                helper.stagingTree,
                runId,
                targetFiles,
                previousFiles);
            lifecycleCheckpoint.phase = transaction.state;
            PackageLifecycleCheckpointStore.Write(
                projectPath,
                lifecycleCheckpoint);
            return await CompleteCommittedCheckpointAsync(
                projectPath,
                lifecycleCheckpoint);
        }

        private static async Task<PackageLifecycleExecutionResult> TryResumeAsync(
            string projectPath,
            AliasPackageContract alias,
            string operation,
            string runId,
            string expectedCurrentReleaseRoot)
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
            return await CompleteCommittedCheckpointAsync(
                projectPath,
                checkpoint);
        }

        private static async Task<PackageLifecycleExecutionResult>
            CompleteCommittedCheckpointAsync(
                string projectPath,
                PackageLifecycleCheckpoint checkpoint)
        {
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
                if (inspection.requiresPackageResolution)
                {
                    await EmbeddedPackageResolver.ResolveAsync();
                }
                VerifyRestoredState(projectPath, checkpoint);
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
                VerifyCommittedState(projectPath, checkpoint);
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
                VerifyRestoredState(projectPath, checkpoint);
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

        private static void VerifyCommittedState(
            string projectPath,
            PackageLifecycleCheckpoint checkpoint)
        {
            if (checkpoint.operation == "uninstall")
            {
                PackageImportVerifier.ImportAndVerifyRemoval(
                    projectPath,
                    ToVerifiedFiles(checkpoint.priorState?.files));
                return;
            }
            if (checkpoint.targetState == null)
            {
                throw new InvalidDataException(
                    "The package lifecycle target state is missing.");
            }
            PackageImportVerifier.ImportAndVerify(
                projectPath,
                checkpoint.targetState.files);
        }

        private static void VerifyRestoredState(
            string projectPath,
            PackageLifecycleCheckpoint checkpoint)
        {
            if (checkpoint.priorState == null)
            {
                PackageImportVerifier.ImportAndVerifyRemoval(
                    projectPath,
                    ToVerifiedFiles(checkpoint.targetState?.files));
                return;
            }
            PackageImportVerifier.ImportAndVerify(
                projectPath,
                checkpoint.priorState.files);
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
                traceId = checkpoint.runId,
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

        private static PackageInstallSessionResponse IssueSession(
            Uri server,
            AliasPackageContract alias,
            string deviceKeyThumbprint,
            string accessToken,
            string idempotencyKey,
            string operation,
            string targetReleaseRoot)
        {
            Uri endpoint = new Uri(server, "/api/v2/package-installs/sessions");
            string body = BuildSessionRequestBody(
                alias,
                deviceKeyThumbprint,
                idempotencyKey,
                operation,
                targetReleaseRoot);
            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent(
                    body,
                    Encoding.UTF8,
                    "application/json");
                using (HttpResponseMessage response =
                    HttpClient.SendAsync(request).GetAwaiter().GetResult())
                {
                    string responseBody =
                        response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(
                            $"The package install session request failed with HTTP {(int)response.StatusCode}.");
                    }
                    PackageInstallSessionResponse session =
                        JsonConvert.DeserializeObject<PackageInstallSessionResponse>(
                            responseBody);
                    if (session == null ||
                        !IsSha256(session.releaseRoot) ||
                        string.IsNullOrWhiteSpace(session.versionId) ||
                        string.IsNullOrWhiteSpace(session.installSession) ||
                        string.IsNullOrWhiteSpace(session.deliveryGrant))
                    {
                        throw new InvalidDataException(
                            "The package install session response is invalid.");
                    }
                    return session;
                }
            }
        }

        internal static string BuildSessionRequestBody(
            AliasPackageContract alias,
            string deviceKeyThumbprint,
            string idempotencyKey,
            string operation,
            string targetReleaseRoot)
        {
            return JsonConvert.SerializeObject(
                new
                {
                    aliasId = alias.aliasId,
                    catalogProductIds = alias.catalogProductIds
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray(),
                    deviceKeyThumbprint,
                    idempotencyKey,
                    operation,
                    targetReleaseRoot = string.IsNullOrEmpty(targetReleaseRoot)
                        ? null
                        : targetReleaseRoot,
                },
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                });
        }

        private static TransferHelperRequest BuildRequest(
            Uri server,
            string aliasId,
            string currentReleaseRoot,
            string runId,
            string operation,
            string projectPath,
            PackageInstallSessionResponse session,
            string stateRoot,
            string approvedDigest,
            string approvedPolicy)
        {
            string resultDirectory = Path.Combine(stateRoot, "results");
            Directory.CreateDirectory(resultDirectory);
            return new TransferHelperRequest
            {
                aliasId = aliasId,
                approvedActiveContentDigest = approvedDigest,
                approvedPolicyVersion = approvedPolicy,
                deliveryGrant = session.deliveryGrant,
                expectedCurrentReleaseRoot = currentReleaseRoot,
                idempotencyKey = runId,
                installSession = session.installSession,
                operation = operation,
                projectPath = projectPath,
                resultPath = Path.Combine(resultDirectory, runId + ".json"),
                runId = runId,
                stateRoot = stateRoot,
                targetReleaseRoot = session.releaseRoot,
                tufMetadataUrl = new Uri(
                    server,
                    "/api/v2/package-installer/tuf/metadata/").ToString(),
                tufRootPath = TransferHelperClient.ResolveTrustRootPath(),
                tufTargetsUrl = new Uri(
                    server,
                    "/api/v2/package-installer/tuf/targets/").ToString(),
                tufTrustTarget = TrustTarget,
            };
        }

        private static PackageLifecycleExecutionResult BuildExecutionResult(
            TransferHelperResult helper,
            string currentReleaseRoot,
            string targetReleaseRoot)
        {
            var receipts = new List<string>();
            if (!string.IsNullOrWhiteSpace(helper.receiptId))
            {
                receipts.Add(helper.receiptId);
            }
            if (!string.IsNullOrWhiteSpace(helper.receiptPath))
            {
                receipts.Add(helper.receiptPath);
            }
            return new PackageLifecycleExecutionResult
            {
                activeContentDigest = helper.activeContentDigest,
                activePolicyVersion = helper.activePolicyVersion,
                currentReleaseRoot = currentReleaseRoot,
                receiptReferences = receipts,
                journalState = helper.journalState,
                targetReleaseRoot = targetReleaseRoot,
                traceId = helper.traceId,
                versionId = helper.versionId,
            };
        }

        private static List<VerifiedStagingFile> ToVerifiedFiles(
            IEnumerable<TransferHelperFile> files)
        {
            return (files ?? Enumerable.Empty<TransferHelperFile>())
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
            string receiptId,
            string receiptPath,
            string activeContentDigest,
            string activePolicyVersion,
            List<TransferHelperFile> files)
        {
            string relativePath = InstallStatePath(aliasId);
            string destination = Path.Combine(
                stagingTree,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            var state = new PackageDeliveryInstallState
            {
                activeContentDigest = activeContentDigest,
                activePolicyVersion = activePolicyVersion,
                aliasId = aliasId,
                files = files.Select(file => new TransferHelperFile
                {
                    bytes = file.bytes,
                    normalizedPath = file.normalizedPath,
                    sha256 = file.sha256,
                }).ToList(),
                receiptId = receiptId ?? string.Empty,
                receiptPath = receiptPath ?? string.Empty,
                releaseRoot = releaseRoot,
                versionId = versionId,
            };
            File.WriteAllText(
                destination,
                JsonConvert.SerializeObject(state, Formatting.Indented),
                new UTF8Encoding(false));
            return FileRecord(destination, relativePath);
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
                state.files.Any(file =>
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
                operation != "uninstall" &&
                operation != "recover" &&
                (!IsSha256(approvedDigest) ||
                    string.IsNullOrWhiteSpace(approvedPolicy)))
            {
                throw new InvalidOperationException(
                    "The active-content approval is invalid.");
            }
        }

        private static void ValidateAlias(AliasPackageContract alias)
        {
            if (!AliasPackageDiscovery.IsServerAuthorized(alias) ||
                alias.catalogProductIds == null ||
                alias.catalogProductIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "The package is not a complete server-authorized alias.");
            }
        }

        private static string CurrentProjectPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static Uri RequireServerOrigin(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttps &&
                    !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new InvalidOperationException(
                    "The package server URL must use HTTPS or loopback HTTP.");
            }
            return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
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
