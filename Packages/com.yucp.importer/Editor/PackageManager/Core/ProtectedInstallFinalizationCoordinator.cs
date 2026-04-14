using System;

using System.Collections.Generic;

using System.IO;

using System.Linq;

using System.Reflection;

using UnityEditor;

using UnityEngine;

using YUCP.Importer.Editor.PackageManager;



namespace YUCP.Importer.Editor.PackageManager.Core

{

    public interface IProtectedPayloadBrokerBridge

    {

        bool TryFinalizeProtectedInstall(

            InstalledPackageInfo packageInfo,

            out IReadOnlyList<string> materializedAssetPaths,

            out string error,

            out bool pending);

    }



    [InitializeOnLoad]

    internal static class ProtectedInstallFinalizationCoordinator

    {

        private const string PendingFinalizationStateKey = "YUCP.PackageManager.ProtectedPayload.PendingFinalization";

        private const string LegacyPendingPatchPathKey = "YUCP.DerivedFbxBuilder.PendingPatchPath";

        private const double PendingTimeoutSeconds = 180.0;

        private static readonly string[] ManagedWorkspaceFileNames =

        {

            "hdiffz.dll",

            "hpatchz.dll",

            "hdiffinfo.dll",

            "installer.log",

            "install.complete",

            "install.error",

        };



        internal delegate bool TryMaterializePatchAssetsHandler(

            IReadOnlyList<string> patchAssetPaths,

            out IReadOnlyList<string> createdAssetPaths,

            out string error);



        internal delegate bool TryReleaseRuntimeResourcesHandler();

        internal delegate bool TryRollbackImportedAssetsHandler(

            IReadOnlyList<string> assetPaths,

            out string error);



        private static bool _scheduled;

        private static bool _isConsumingPendingFinalization;



        [Serializable]

        private sealed class PendingFinalizationState

        {

            public string packageId = "";

            public string[] extractedAssetPaths = Array.Empty<string>();

            public long queuedAtTicksUtc;

        }



        [Serializable]

        private sealed class PackageInfoJson

        {

            public string packageId;

            public string packageName;

        }



        private enum FinalizationStatus

        {

            Completed,

            Pending,

            Failed,

        }



        static ProtectedInstallFinalizationCoordinator()

        {

            SchedulePendingFinalization();

        }



        internal static void QueuePendingFinalization(

            InstalledPackageInfo packageInfo,

            IReadOnlyList<string> extractedAssetPaths)

        {

            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.packageId))

                return;



            var state = new PendingFinalizationState

            {

                packageId = packageInfo.packageId ?? string.Empty,

                extractedAssetPaths = (extractedAssetPaths ?? Array.Empty<string>())

                    .Where(path => !string.IsNullOrWhiteSpace(path))

                    .Select(NormalizeAssetPath)

                    .Distinct(StringComparer.OrdinalIgnoreCase)

                    .ToArray(),

                queuedAtTicksUtc = DateTime.UtcNow.Ticks,

            };

            ProtectedImportStateTracker.TryAdvance(
                packageInfo,
                ProtectedImportStateTracker.ProtectedImportPhase.payload_extracted,
                out _,
                state.extractedAssetPaths);



            EditorPrefs.SetString(PendingFinalizationStateKey, JsonUtility.ToJson(state));

            SchedulePendingFinalization();

        }



        private static void SchedulePendingFinalization()

        {

            if (_scheduled || !HasPendingFinalization())

                return;



            _scheduled = true;

            EditorApplication.delayCall += TryConsumePendingFinalization;

        }



        private static bool HasPendingFinalization()

        {

            return !string.IsNullOrWhiteSpace(EditorPrefs.GetString(PendingFinalizationStateKey, string.Empty));

        }



        private static PendingFinalizationState LoadPendingFinalizationState()

        {

            string rawState = EditorPrefs.GetString(PendingFinalizationStateKey, string.Empty);

            if (string.IsNullOrWhiteSpace(rawState))

                return null;



            try

            {

                return JsonUtility.FromJson<PendingFinalizationState>(rawState);

            }

            catch

            {

                return null;

            }

        }



        private static void ClearPendingFinalization()

        {

            EditorPrefs.DeleteKey(PendingFinalizationStateKey);

        }



        private static void TryConsumePendingFinalization()

        {

            _scheduled = false;



            if (!HasPendingFinalization())

                return;



            if (_isConsumingPendingFinalization)

                return;



            _isConsumingPendingFinalization = true;

            try

            {

                if (EditorApplication.isCompiling || EditorApplication.isUpdating)

                {

                    SchedulePendingFinalization();

                    return;

                }



                PendingFinalizationState state = LoadPendingFinalizationState();

                if (state == null || string.IsNullOrWhiteSpace(state.packageId))

                {

                    ClearPendingFinalization();

                    return;

                }



                var registry = InstalledPackageRegistry.Load() ?? InstalledPackageRegistry.GetOrCreate();

                var packageInfo = registry?.GetPackage(state.packageId);

                if (IsTimedOut(state))

                {

                    bool timedOutRollbackCleanly = true;

                    string rollbackError = null;

                    if (packageInfo != null)

                    {

                        timedOutRollbackCleanly = RollbackFailedInstall(

                            packageInfo,

                            FindInstalledShellRootAssetPath(packageInfo),

                            state.extractedAssetPaths ?? Array.Empty<string>(),

                            Array.Empty<string>(),

                            out rollbackError);

                    }



                    ClearPendingFinalization();

                    Debug.LogError(timedOutRollbackCleanly

                        ? "[YUCP PackageManager] A required package protection step timed out and the import was rolled back."

                        : $"[YUCP PackageManager] A required package protection step timed out and the import could not be rolled back cleanly. {rollbackError}");

                    return;

                }



                if (packageInfo == null)

                {

                    SchedulePendingFinalization();

                    return;

                }

                if (!ProtectedImportStateTracker.TryValidateResume(
                        packageInfo,
                        ProtectedImportStateTracker.ProtectedImportPhase.payload_extracted,
                        out _,
                        out string stateError))

                {

                    bool resumeRollbackCleanly = RollbackFailedInstall(

                        packageInfo,

                        FindInstalledShellRootAssetPath(packageInfo),

                        state.extractedAssetPaths ?? Array.Empty<string>(),

                        Array.Empty<string>(),

                        out string rollbackError);

                    ClearPendingFinalization();

                    Debug.LogError(resumeRollbackCleanly

                        ? "[YUCP PackageManager] A required package protection step failed and the import was rolled back."

                        : $"[YUCP PackageManager] A required package protection step failed and the import could not be rolled back cleanly. {rollbackError}");

                    EditorUtility.DisplayDialog(

                        "Import Failed",

                        stateError,

                        "OK");

                    return;

                }



                FinalizationStatus status = TryFinalizeProtectedInstall(

                    packageInfo,

                    state.extractedAssetPaths ?? Array.Empty<string>(),

                    out IReadOnlyList<string> committedFiles,

                    out string error,

                    out bool rolledBackCleanly);



                if (status == FinalizationStatus.Pending)

                {

                    SchedulePendingFinalization();

                    return;

                }



                if (status == FinalizationStatus.Failed)

                {

                    ClearPendingFinalization();

                    Debug.LogError(rolledBackCleanly

                        ? "[YUCP PackageManager] A required package protection step failed and the import was rolled back."

                        : "[YUCP PackageManager] A required package protection step failed and the import could not be rolled back cleanly.");

                    EditorUtility.DisplayDialog(

                        "Import Failed",

                        error,

                        "OK");

                    return;

                }



                packageInfo.installedFiles = committedFiles.ToList();

                registry.RegisterPackage(packageInfo);

                ProtectedImportStateTracker.TryAdvance(
                    packageInfo,
                    ProtectedImportStateTracker.ProtectedImportPhase.materialization_finalized,
                    out _,
                    committedFiles);

                ClearLegacyPendingPatchPath();

                ClearPendingFinalization();

                Debug.Log($"[YUCP PackageManager] Finalized protected package install for '{packageInfo.packageName}'.");

            }

            finally

            {

                _isConsumingPendingFinalization = false;

            }

        }



        private static bool IsTimedOut(PendingFinalizationState state)

        {

            if (state == null || state.queuedAtTicksUtc <= 0)

                return false;



            try

            {

                var startedUtc = new DateTime(state.queuedAtTicksUtc, DateTimeKind.Utc);

                return (DateTime.UtcNow - startedUtc).TotalSeconds > PendingTimeoutSeconds;

            }

            catch

            {

                return false;

            }

        }



        private static FinalizationStatus TryFinalizeProtectedInstall(

            InstalledPackageInfo packageInfo,

            IReadOnlyList<string> extractedAssetPaths,

            out IReadOnlyList<string> committedFiles,

            out string error,

            out bool rolledBackCleanly)

        {

            committedFiles = Array.Empty<string>();

            error = null;

            rolledBackCleanly = false;



            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.packageId))

            {

                error = "The package protection step could not be completed.";

                return FinalizationStatus.Failed;

            }



            string shellRootAssetPath = FindInstalledShellRootAssetPath(packageInfo);

            var patchAssetPaths = CollectPatchAssetPaths(extractedAssetPaths);

            var createdAssetPaths = new List<string>();



            if (packageInfo.protectedPayload?.requiresBrokeredMaterialization == true)

            {

                if (!ProtectedPayloadBrokerService.TryFinalizeProtectedInstall(

                        packageInfo,

                        out IReadOnlyList<string> brokerMaterializedAssetPaths,

                        out string brokerError,

                        out bool pending))

                {

                    if (pending)

                    {

                        return FinalizationStatus.Pending;

                    }



                    createdAssetPaths.AddRange(brokerMaterializedAssetPaths ?? Array.Empty<string>());

                    rolledBackCleanly = RollbackFailedInstall(packageInfo, shellRootAssetPath, extractedAssetPaths, createdAssetPaths, out string rollbackError);

                    if (!string.IsNullOrWhiteSpace(brokerError))
                    {
                        UnityEngine.Debug.LogError("[YUCP PackageManager] Protected install broker error.");
                    }

                    error = BuildFailureError(
                        "The package protection step could not be completed on this machine.",
                        rollbackError,
                        rolledBackCleanly);

                    return FinalizationStatus.Failed;

                }



                createdAssetPaths.AddRange(brokerMaterializedAssetPaths ?? Array.Empty<string>());

                AssetDatabase.Refresh();

                var brokerPatchAssetPaths = CollectPatchAssetPaths(brokerMaterializedAssetPaths);
                if (brokerPatchAssetPaths.Count > 0)

                {

                    if (TryMaterializePatchAssets(

                        brokerPatchAssetPaths,

                        out IReadOnlyList<string> materializedAssetPaths,

                        out error,

                        out bool patchMaterializationPending))

                    {

                        createdAssetPaths.AddRange(materializedAssetPaths ?? Array.Empty<string>());

                        AssetDatabase.Refresh();
                    }

                    else

                    {

                        if (patchMaterializationPending)

                            return FinalizationStatus.Pending;



                        createdAssetPaths.AddRange(materializedAssetPaths ?? Array.Empty<string>());

                        rolledBackCleanly = RollbackFailedInstall(packageInfo, shellRootAssetPath, extractedAssetPaths, createdAssetPaths, out string rollbackError);

                        error = BuildFailureError("The package protection step could not be completed on this machine.", rollbackError, rolledBackCleanly);

                        return FinalizationStatus.Failed;
                    }
                }

                if (!TryCleanupManagedArtifacts(packageInfo, shellRootAssetPath, brokerMaterializedAssetPaths, out _))

                {

                    Debug.LogError("[YUCP PackageManager] Protected install cleanup failed.");

                    rolledBackCleanly = RollbackFailedInstall(packageInfo, shellRootAssetPath, extractedAssetPaths, createdAssetPaths, out string rollbackError);

                    error = BuildFailureError("The package protection step could not be completed on this machine.", rollbackError, rolledBackCleanly);

                    return FinalizationStatus.Failed;
                }

                AssetDatabase.Refresh();

                shellRootAssetPath = FindInstalledShellRootAssetPath(packageInfo) ?? shellRootAssetPath;

                committedFiles = BuildCommittedInstalledFiles(

                    packageInfo,

                    extractedAssetPaths,

                    createdAssetPaths,

                    shellRootAssetPath);

                packageInfo.installedFiles = committedFiles.ToList();

                IReadOnlyList<string> brokerCouplingFiles = BuildBrokeredPostCommitCouplingFiles(
                    brokerMaterializedAssetPaths,
                    createdAssetPaths);
                if (!TryApplyCoupling(packageInfo.packageId, brokerCouplingFiles, out _))

                {

                    Debug.LogError("[YUCP PackageManager] Protected install post-commit coupling failed.");

                    rolledBackCleanly = RollbackFailedInstall(packageInfo, shellRootAssetPath, committedFiles, createdAssetPaths, out string rollbackError);

                    error = BuildFailureError("The package protection step could not be completed on this machine.", rollbackError, rolledBackCleanly);

                    return FinalizationStatus.Failed;
                }

                rolledBackCleanly = true;

                return FinalizationStatus.Completed;

            }



            if (patchAssetPaths.Count > 0)

            {

                if (TryMaterializePatchAssets(

                    patchAssetPaths,

                    out IReadOnlyList<string> materializedAssetPaths,

                    out error,

                    out bool pending))

                {

                    createdAssetPaths.AddRange(materializedAssetPaths ?? Array.Empty<string>());

                    AssetDatabase.Refresh();

                    shellRootAssetPath = FindInstalledShellRootAssetPath(packageInfo) ?? shellRootAssetPath;

                }

                else

                {

                    if (pending)

                        return FinalizationStatus.Pending;



                    createdAssetPaths.AddRange(materializedAssetPaths ?? Array.Empty<string>());

                    rolledBackCleanly = RollbackFailedInstall(packageInfo, shellRootAssetPath, extractedAssetPaths, createdAssetPaths, out string rollbackError);

                    error = BuildFailureError("The package protection step could not be completed on this machine.", rollbackError, rolledBackCleanly);

                    return FinalizationStatus.Failed;

                }

            }



            if (!TryCleanupManagedArtifacts(packageInfo, shellRootAssetPath, extractedAssetPaths, out _))

            {

                Debug.LogError("[YUCP PackageManager] Protected install cleanup failed.");

                rolledBackCleanly = RollbackFailedInstall(packageInfo, shellRootAssetPath, extractedAssetPaths, createdAssetPaths, out string rollbackError);

                error = BuildFailureError("The package protection step could not be completed on this machine.", rollbackError, rolledBackCleanly);

                return FinalizationStatus.Failed;

            }



            AssetDatabase.Refresh();

            shellRootAssetPath = FindInstalledShellRootAssetPath(packageInfo) ?? shellRootAssetPath;

            committedFiles = BuildCommittedInstalledFiles(packageInfo, extractedAssetPaths, createdAssetPaths, shellRootAssetPath);

            packageInfo.installedFiles = committedFiles.ToList();

            IReadOnlyList<string> committedCouplingFiles = BuildCouplingFiles(packageInfo, extractedAssetPaths, createdAssetPaths);
            if (!TryApplyCoupling(packageInfo.packageId, committedCouplingFiles, out _))

            {

                Debug.LogError("[YUCP PackageManager] Protected install post-commit coupling failed.");

                rolledBackCleanly = RollbackFailedInstall(packageInfo, shellRootAssetPath, committedFiles, createdAssetPaths, out string rollbackError);

                error = BuildFailureError("The package protection step could not be completed on this machine.", rollbackError, rolledBackCleanly);

                return FinalizationStatus.Failed;

            }



            rolledBackCleanly = true;

            return FinalizationStatus.Completed;

        }



        private static string BuildFailureError(string operationError, string rollbackError, bool rolledBackCleanly)

        {

            string normalizedOperationError = string.IsNullOrWhiteSpace(operationError)

                ? "The package protection step could not be completed on this machine."

                : operationError.Trim();

            if (rolledBackCleanly)

                return normalizedOperationError;



            string normalizedRollbackError = string.IsNullOrWhiteSpace(rollbackError)

                ? "The importer could not remove all files created during the failed protected import."

                : rollbackError.Trim();

            return $"{normalizedOperationError}\n\nThe importer could not roll back the package cleanly: {normalizedRollbackError}";

        }



        private static bool TryMaterializePatchAssets(

            IReadOnlyList<string> patchAssetPaths,

            out IReadOnlyList<string> createdAssetPaths,

            out string error,

            out bool pending)

        {

            createdAssetPaths = Array.Empty<string>();

            error = null;

            pending = false;



            if (patchAssetPaths == null || patchAssetPaths.Count == 0)

                return true;



            #if UNITY_INCLUDE_TESTS
            var overrideHandler = ProtectedInstallFinalizationCoordinatorTestHooks.TryMaterializePatchAssets;

            if (overrideHandler != null)
            {
                var result = overrideHandler(patchAssetPaths);
                createdAssetPaths = result.createdAssetPaths ?? Array.Empty<string>();
                error = result.error;
                return result.success;
            }
            #endif



            Type runtimeType = FindPatchImporterType();

            if (runtimeType == null)

            {

                if (HasTempPackageFolder() || HasLegacyPendingPatchPath())

                {

                    EnsurePatchRuntimeCompilationRequested();

                    pending = true;

                    return false;

                }



                return true;

            }



            var explicitMaterializeMethod = runtimeType.GetMethod(

                "TryMaterializePatchAssets",

                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);



            if (explicitMaterializeMethod == null)

            {

                if (HasTempPackageFolder() || HasLegacyPendingPatchPath())

                {

                    EnsurePatchRuntimeCompilationRequested();

                    pending = true;

                    return false;

                }



                return true;

            }



            try

            {

                object[] args =

                {

                    patchAssetPaths.ToArray(),

                    null,

                    null,

                };



                bool success = (bool)explicitMaterializeMethod.Invoke(null, args);

                createdAssetPaths = (args[1] as string[]) ?? Array.Empty<string>();

                error = success ? null : "The package protection step could not be completed on this machine.";

                return success;

            }

            catch

            {

                error = "The package protection step could not be completed on this machine.";

                return false;

            }

        }



        private static void EnsurePatchRuntimeCompilationRequested()

        {

            try

            {

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();

            }

            catch

            {

            }

        }



        private static Type FindPatchImporterType()

        {
            return Type.GetType("YUCP.PatchCleanup.YUCPPatchImporter, YUCP.PatchRuntime", false);

        }



        private static bool HasTempPackageFolder()

        {

            return Directory.Exists(AssetPathToDiskPath("Packages/com.yucp.temp"));

        }



        private static bool HasLegacyPendingPatchPath()

        {

            return !string.IsNullOrWhiteSpace(EditorPrefs.GetString(LegacyPendingPatchPathKey, string.Empty));

        }



        private static void ClearLegacyPendingPatchPath()

        {

            EditorPrefs.DeleteKey(LegacyPendingPatchPathKey);

        }



        private static bool TryCleanupManagedArtifacts(

            InstalledPackageInfo packageInfo,

            string shellRootAssetPath,

            IReadOnlyList<string> extractedAssetPaths,

            out string error)

        {

            error = null;



            var managedAssetPaths = CollectManagedAssetPaths(packageInfo, shellRootAssetPath, extractedAssetPaths);

            if (managedAssetPaths.Count > 0 &&

                !TryRollbackImportedAssets(managedAssetPaths, out _))

            {

                error = "The package protection step could not be completed on this machine.";

                return false;

            }



            if (!TryDeleteManagedWorkspaceArtifacts())

            {

                error = "The package protection step could not be completed on this machine.";

                return false;

            }



            return true;

        }



        private static IReadOnlyList<string> CollectManagedAssetPaths(

            InstalledPackageInfo packageInfo,

            string shellRootAssetPath,

            IReadOnlyList<string> extractedAssetPaths)

        {

            var managedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);



            string tempPackagePath = "Packages/com.yucp.temp";

            if (Directory.Exists(AssetPathToDiskPath(tempPackagePath)))

            {

                managedPaths.Add(tempPackagePath);

            }



            if (!string.IsNullOrWhiteSpace(shellRootAssetPath))

            {

                string tempShellPath = NormalizeAssetPath(shellRootAssetPath + "/_temp");

                if (Directory.Exists(AssetPathToDiskPath(tempShellPath)))

                {

                    managedPaths.Add(tempShellPath);

                }

            }



            IEnumerable<string> installedPackageFiles = packageInfo?.installedFiles ?? Enumerable.Empty<string>();

            foreach (string assetPath in installedPackageFiles)

            {

                if (IsManagedAssetPath(assetPath))

                    managedPaths.Add(NormalizeAssetPath(assetPath));

            }



            foreach (string assetPath in extractedAssetPaths ?? Array.Empty<string>())

            {

                if (IsManagedAssetPath(assetPath))

                    managedPaths.Add(NormalizeAssetPath(assetPath));

            }



            string installerEditorRoot = InstalledPackagesOrganizer.RootAssetPath + "/Editor";

            string installerEditorDiskRoot = AssetPathToDiskPath(installerEditorRoot);

            if (Directory.Exists(installerEditorDiskRoot))

            {

                foreach (string filePath in Directory.GetFiles(installerEditorDiskRoot, "YUCP_*", SearchOption.TopDirectoryOnly))

                {

                    managedPaths.Add(DiskPathToAssetPath(filePath));

                }

            }



            return managedPaths

                .Where(path => !string.IsNullOrWhiteSpace(path))

                .Distinct(StringComparer.OrdinalIgnoreCase)

                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)

                .ToList();

        }



        private static bool TryDeleteManagedWorkspaceArtifacts()

        {

            try

            {

                if (!TryReleaseManagedRuntimeResources())

                    return false;



                string workspaceRoot = Path.Combine(

                    Path.GetFullPath(Path.Combine(Application.dataPath, "..")),

                    "Library",

                    "YUCP");



                if (!Directory.Exists(workspaceRoot))

                    return true;



                foreach (string fileName in ManagedWorkspaceFileNames)

                {

                    TryDeleteWorkspaceFile(Path.Combine(workspaceRoot, fileName));

                }



                foreach (string manifestPath in Directory.GetFiles(workspaceRoot, "Manifest_*.json", SearchOption.TopDirectoryOnly))

                {

                    TryDeleteWorkspaceFile(manifestPath);

                }



                foreach (string patchDiffPath in Directory.GetFiles(workspaceRoot, "patch_*.hdiff", SearchOption.TopDirectoryOnly))

                {

                    TryDeleteWorkspaceFile(patchDiffPath);

                }



                TryDeleteWorkspaceFile(Path.Combine(workspaceRoot, "guid_swaps.json"));



                TryDeleteEmptyDirectory(workspaceRoot);

                return true;

            }

            catch

            {

                return false;

            }

        }



        private static bool TryReleaseManagedRuntimeResources()

        {

            #if UNITY_INCLUDE_TESTS
            var overrideHandler = ProtectedInstallFinalizationCoordinatorTestHooks.TryReleaseRuntimeResources;

            if (overrideHandler != null)

                return overrideHandler();
            #endif



            try

            {

                foreach (string typeName in new[]

                {

                    "YUCP.PatchRuntime.HDiffPatchWrapper",

                    "YUCP.DevTools.Editor.PackageExporter.HDiffPatchWrapper",

                })

                {

                    Type wrapperType = FindLoadedType(typeName);

                    if (wrapperType == null)

                        continue;



                    var freeDllsMethod = wrapperType.GetMethod("FreeDlls", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                    if (freeDllsMethod == null)

                        continue;



                    freeDllsMethod.Invoke(null, null);

                    break;

                }



                return true;

            }

            catch

            {

                return false;

            }

        }



        private static void TryDeleteWorkspaceFile(string diskPath)

        {

            if (!File.Exists(diskPath))

                return;



            File.SetAttributes(diskPath, FileAttributes.Normal);

            File.Delete(diskPath);

        }



        private static void TryDeleteEmptyDirectory(string diskPath)

        {

            if (!Directory.Exists(diskPath))

                return;



            if (Directory.EnumerateFileSystemEntries(diskPath).Any())

                return;



            Directory.Delete(diskPath, false);

        }



        private static bool TryApplyCoupling(

            string packageId,

            IReadOnlyList<string> installedFiles,

            out string error)

        {

#if UNITY_INCLUDE_TESTS
            var overrideHandler = CouplingImportGuardTestHooks.TryApplyCoupling;

            if (overrideHandler != null)
            {
                var result = overrideHandler(packageId, installedFiles);
                error = result.error;
                return result.success;
            }
#endif



            return CouplingRuntimeService.TryApplyCoupling(packageId, installedFiles, out error);

        }



        private static bool RollbackFailedInstall(

            InstalledPackageInfo packageInfo,

            string shellRootAssetPath,

            IReadOnlyList<string> extractedAssetPaths,

            IReadOnlyList<string> createdAssetPaths,

            out string rollbackError)

        {

            rollbackError = null;

            bool rolledBackCleanly = true;



            try

            {

                var rollbackPaths = BuildRollbackAssetPaths(packageInfo, extractedAssetPaths, createdAssetPaths, shellRootAssetPath);

                if (!TryRollbackImportedAssets(rollbackPaths, out string assetRollbackError))

                {

                    rolledBackCleanly = false;

                    rollbackError = assetRollbackError;

                }



                if (!TryDeleteManagedWorkspaceArtifacts())

                {

                    rolledBackCleanly = false;

                    rollbackError = string.IsNullOrWhiteSpace(rollbackError)

                        ? "The importer workspace could not be cleaned up."

                        : $"{rollbackError} The importer workspace could not be cleaned up.";

                }


                ProtectedImportStateTracker.TryAdvance(
                    packageInfo,
                    ProtectedImportStateTracker.ProtectedImportPhase.rolled_back,
                    out _,
                    createdAssetPaths);



                return rolledBackCleanly;

            }

            finally

            {

                ClearLegacyPendingPatchPath();

                var registry = InstalledPackageRegistry.Load() ?? InstalledPackageRegistry.GetOrCreate();

                registry?.UnregisterPackage(packageInfo?.packageId);

            }

        }



        private static bool TryRollbackImportedAssets(IReadOnlyList<string> assetPaths, out string error)

        {

            #if UNITY_INCLUDE_TESTS
            var overrideHandler = ProtectedInstallFinalizationCoordinatorTestHooks.TryRollbackImportedAssets;

            if (overrideHandler != null)
            {
                var result = overrideHandler(assetPaths);
                error = result.error;
                return result.success;
            }
            #endif



            return ImportedAssetRollbackService.TryRollbackImportedAssets(assetPaths, out error);

        }



        private static IReadOnlyList<string> BuildRollbackAssetPaths(

            InstalledPackageInfo packageInfo,

            IReadOnlyList<string> extractedAssetPaths,

            IReadOnlyList<string> createdAssetPaths,

            string shellRootAssetPath)

        {

            var rollbackPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);



            foreach (string filePath in EnumerateShellFiles(shellRootAssetPath))

            {

                rollbackPaths.Add(filePath);

            }



            IEnumerable<string> installedPackageFiles = packageInfo?.installedFiles ?? Enumerable.Empty<string>();

            foreach (string assetPath in installedPackageFiles)

            {

                AddRollbackPath(rollbackPaths, assetPath);

            }



            foreach (string assetPath in extractedAssetPaths ?? Array.Empty<string>())

            {

                AddRollbackPath(rollbackPaths, assetPath);

            }



            foreach (string assetPath in createdAssetPaths ?? Array.Empty<string>())

            {

                AddRollbackPath(rollbackPaths, assetPath);

            }



            foreach (string assetPath in CollectManagedAssetPaths(packageInfo, shellRootAssetPath, extractedAssetPaths))

            {

                AddRollbackPath(rollbackPaths, assetPath);

            }



            return rollbackPaths

                .Where(path => !string.IsNullOrWhiteSpace(path))

                .Distinct(StringComparer.OrdinalIgnoreCase)

                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)

                .ToList();

        }



        private static void AddRollbackPath(ISet<string> paths, string assetPath)

        {

            string normalizedPath = NormalizeAssetPath(assetPath);

            if (string.IsNullOrWhiteSpace(normalizedPath) || normalizedPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))

                return;



            if (File.Exists(AssetPathToDiskPath(normalizedPath)) || Directory.Exists(AssetPathToDiskPath(normalizedPath)))

            {

                paths.Add(normalizedPath);

            }

        }



        private static List<string> CollectPatchAssetPaths(IReadOnlyList<string> extractedAssetPaths)

        {

            return (extractedAssetPaths ?? Array.Empty<string>())

                .Where(path => !string.IsNullOrWhiteSpace(path))

                .Select(NormalizeAssetPath)

                .Where(path =>

                    path.StartsWith("Packages/com.yucp.temp/Patches/", StringComparison.OrdinalIgnoreCase) &&

                    path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))

                .Distinct(StringComparer.OrdinalIgnoreCase)

                .ToList();

        }



        private static string FindInstalledShellRootAssetPath(InstalledPackageInfo packageInfo)

        {

            if (packageInfo == null)

                return null;



            string installedRootDiskPath = AssetPathToDiskPath(InstalledPackagesOrganizer.RootAssetPath);

            if (Directory.Exists(installedRootDiskPath))

            {

                foreach (string metadataDiskPath in Directory.GetFiles(installedRootDiskPath, "YUCP_PackageInfo.json", SearchOption.AllDirectories))

                {

                    string metadataJson;

                    try

                    {

                        metadataJson = File.ReadAllText(metadataDiskPath);

                    }

                    catch

                    {

                        continue;

                    }



                    PackageInfoJson metadata = null;

                    try

                    {

                        metadata = JsonUtility.FromJson<PackageInfoJson>(metadataJson);

                    }

                    catch

                    {

                    }



                    bool packageIdMatches =

                        !string.IsNullOrWhiteSpace(packageInfo.packageId) &&

                        string.Equals(metadata?.packageId, packageInfo.packageId, StringComparison.OrdinalIgnoreCase);

                    bool packageNameMatches =

                        !string.IsNullOrWhiteSpace(packageInfo.packageName) &&

                        string.Equals(metadata?.packageName, packageInfo.packageName, StringComparison.OrdinalIgnoreCase);



                    if (packageIdMatches || packageNameMatches)

                    {

                        return NormalizeAssetPath(Path.GetDirectoryName(DiskPathToAssetPath(metadataDiskPath)));

                    }

                }

            }



            return TryFindExistingShellRootFromInstalledFiles(packageInfo.installedFiles);

        }



        private static string TryFindExistingShellRootFromInstalledFiles(IReadOnlyList<string> installedFiles)

        {

            foreach (string assetPath in installedFiles ?? Array.Empty<string>())

            {

                string normalizedPath = NormalizeAssetPath(assetPath);

                if (!normalizedPath.StartsWith(InstalledPackagesOrganizer.RootAssetPath + "/", StringComparison.OrdinalIgnoreCase))

                    continue;



                string remainder = normalizedPath.Substring(InstalledPackagesOrganizer.RootAssetPath.Length + 1);

                string firstSegment = remainder.Split('/').FirstOrDefault();

                if (string.IsNullOrWhiteSpace(firstSegment) ||

                    string.Equals(firstSegment, "Editor", StringComparison.OrdinalIgnoreCase))

                {

                    continue;

                }



                string candidateRoot = InstalledPackagesOrganizer.RootAssetPath + "/" + firstSegment;

                if (Directory.Exists(AssetPathToDiskPath(candidateRoot)))

                    return candidateRoot;

            }



            return null;

        }



        private static IReadOnlyList<string> BuildCommittedInstalledFiles(

            InstalledPackageInfo packageInfo,

            IReadOnlyList<string> extractedAssetPaths,

            IReadOnlyList<string> createdAssetPaths,

            string shellRootAssetPath)

        {

            var committedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);



            foreach (string assetPath in EnumerateShellFiles(shellRootAssetPath))

            {

                AddDurableInstalledFile(committedFiles, assetPath);

            }



            IEnumerable<string> installedPackageFiles = packageInfo?.installedFiles ?? Enumerable.Empty<string>();

            foreach (string assetPath in installedPackageFiles)

            {

                AddDurableInstalledFile(committedFiles, assetPath);

            }



            foreach (string assetPath in extractedAssetPaths ?? Array.Empty<string>())

            {

                AddDurableInstalledFile(committedFiles, assetPath);

            }



            foreach (string assetPath in createdAssetPaths ?? Array.Empty<string>())

            {

                AddDurableInstalledFile(committedFiles, assetPath);

            }



            return committedFiles

                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)

                .ToList();

        }



        private static IReadOnlyList<string> BuildCouplingFiles(

            InstalledPackageInfo packageInfo,

            IReadOnlyList<string> extractedAssetPaths,

            IReadOnlyList<string> createdAssetPaths)

        {

            var couplingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);



            foreach (string assetPath in packageInfo?.protectedPayload?.payloadAssetPaths ?? Array.Empty<string>())

            {

                AddDurableInstalledFile(couplingFiles, assetPath);

            }



            foreach (string assetPath in extractedAssetPaths ?? Array.Empty<string>())

            {

                AddDurableInstalledFile(couplingFiles, assetPath);

            }



            foreach (string assetPath in createdAssetPaths ?? Array.Empty<string>())

            {

                AddDurableInstalledFile(couplingFiles, assetPath);

            }



            return couplingFiles

                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)

                .ToList();

        }



        private static IReadOnlyList<string> BuildBrokeredPostCommitCouplingFiles(

            IReadOnlyList<string> brokerMaterializedAssetPaths,

            IReadOnlyList<string> createdAssetPaths)

        {

            var brokerMaterializedSet = new HashSet<string>(

                (brokerMaterializedAssetPaths ?? Array.Empty<string>())

                    .Where(path => !string.IsNullOrWhiteSpace(path))

                    .Select(NormalizeAssetPath),

                StringComparer.OrdinalIgnoreCase);



            var postBrokerCreatedAssetPaths = (createdAssetPaths ?? Array.Empty<string>())

                .Where(path => !string.IsNullOrWhiteSpace(path))

                .Select(NormalizeAssetPath)

                .Where(path => !brokerMaterializedSet.Contains(path))

                .ToList();



            return BuildCouplingFiles(

                packageInfo: null,

                extractedAssetPaths: Array.Empty<string>(),

                createdAssetPaths: postBrokerCreatedAssetPaths);

        }



        private static IEnumerable<string> EnumerateShellFiles(string shellRootAssetPath)

        {

            if (string.IsNullOrWhiteSpace(shellRootAssetPath))

                return Array.Empty<string>();



            string shellRootDiskPath = AssetPathToDiskPath(shellRootAssetPath);

            if (!Directory.Exists(shellRootDiskPath))

                return Array.Empty<string>();



            return Directory.GetFiles(shellRootDiskPath, "*", SearchOption.AllDirectories)

                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))

                .Select(DiskPathToAssetPath)

                .Where(path => !string.IsNullOrWhiteSpace(path));

        }



        private static void AddDurableInstalledFile(ISet<string> committedFiles, string assetPath)

        {

            string normalizedPath = NormalizeAssetPath(assetPath);

            if (string.IsNullOrWhiteSpace(normalizedPath))

                return;



            if (normalizedPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || IsManagedAssetPath(normalizedPath))

                return;



            string diskPath = AssetPathToDiskPath(normalizedPath);

            if (!File.Exists(diskPath))

                return;



            committedFiles.Add(normalizedPath);

        }



        private static bool IsManagedAssetPath(string assetPath)

        {

            string normalizedPath = NormalizeAssetPath(assetPath);

            if (string.IsNullOrWhiteSpace(normalizedPath))

                return false;



            if (normalizedPath.StartsWith("Packages/com.yucp.temp/", StringComparison.OrdinalIgnoreCase) ||

                string.Equals(normalizedPath, "Packages/com.yucp.temp", StringComparison.OrdinalIgnoreCase))

            {

                return true;

            }



            if (normalizedPath.StartsWith("Packages/yucp.installed-packages/Editor/YUCP_", StringComparison.OrdinalIgnoreCase))

                return true;



            return normalizedPath.IndexOf("/_temp/YUCP_TempInstall_", StringComparison.OrdinalIgnoreCase) >= 0;

        }



        private static string NormalizeAssetPath(string assetPath)

        {

            return assetPath?.Replace('\\', '/').Trim();

        }



        private static string AssetPathToDiskPath(string assetPath)

        {

            string normalizedPath = NormalizeAssetPath(assetPath);

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            return Path.Combine(projectRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar));

        }



        private static string DiskPathToAssetPath(string diskPath)

        {

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            string relativePath = Path.GetRelativePath(projectRoot, diskPath);

            return NormalizeAssetPath(relativePath);

        }



        private static class ProtectedPayloadBrokerService

        {

            private static readonly IProtectedPayloadBrokerBridge BuiltInBridge = new ProtectedPayloadComShimBridge();



            internal static bool TryFinalizeProtectedInstall(

                InstalledPackageInfo packageInfo,

                out IReadOnlyList<string> materializedAssetPaths,

                out string error,

                out bool pending)

            {

                materializedAssetPaths = Array.Empty<string>();

                error = null;

                pending = false;



                if (packageInfo?.protectedPayload == null)

                {

                    error = "The package protection step could not be completed on this machine.";

                    return false;

                }



                IProtectedPayloadBrokerBridge bridge = ResolveBridge();

                if (bridge == null)

                {

                    error = "This protected package requires the current package protection runtime. Reconnect the package manager and reinstall the package.";

                    return false;

                }



                bool success = bridge.TryFinalizeProtectedInstall(

                    packageInfo,

                    out materializedAssetPaths,

                    out error,

                    out pending);



                if (!success && string.IsNullOrWhiteSpace(error))

                {

                    error = "The package protection step could not be completed on this machine.";

                }



                materializedAssetPaths ??= Array.Empty<string>();

                return success;

            }



            private static IProtectedPayloadBrokerBridge ResolveBridge()

            {
#if UNITY_INCLUDE_TESTS
                if (ProtectedInstallFinalizationCoordinatorTestHooks.BrokerBridgeOverride != null)
                {
                    return ProtectedInstallFinalizationCoordinatorTestHooks.BrokerBridgeOverride;
                }
#endif

                return BuiltInBridge;

            }

        }



        private static Type FindLoadedType(string fullName)        {

            return fullName switch

            {

                "YUCP.PatchRuntime.HDiffPatchWrapper" => Type.GetType("YUCP.PatchRuntime.HDiffPatchWrapper, YUCP.PatchRuntime", false),

                "YUCP.DevTools.Editor.PackageExporter.HDiffPatchWrapper" => Type.GetType("YUCP.DevTools.Editor.PackageExporter.HDiffPatchWrapper, com.yucp.devtools.Editor", false),

                _ => null,

            };

        }

    }

}





