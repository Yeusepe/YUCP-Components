using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager;

namespace YUCP.Importer.Editor.Tests
{
    [InitializeOnLoad]
    public static class ProtectedImportBatchRepro
    {
        private const string StateKey = "YUCP.Tests.ProtectedImportBatchRepro.State";
        private const string PendingFinalizationStateKey = "YUCP.PackageManager.ProtectedPayload.PendingFinalization";
        private const string TargetPackageId = "2ad28c0fc7d54920b01606a8f6a28236";
        private const string TargetPackageName = "Novaspil Kitbash Test License Verification";
        private const string TargetAssetPath = "Assets/Novaspil_Kitbash/Novaspil.fbx";
        private const string FailureMarker = "[YUCP PackageManager] A required package protection step failed";
        private const string SuccessMarker = "[YUCP PackageManager] Finalized protected package install";
        private const string BatchFailurePrefix = "[YUCP Batch Repro] FAILURE: ";
        private const string PendingApplyPackageIdKey = "YUCP.PackageManager.ProtectedPayload.PackageId";
        private const string PendingApplyStartTicksKey = "YUCP.PackageManager.ProtectedPayload.StartTicksUtc";
        private const string PendingProtectedImportBootstrapKey = "YUCP.PendingProtectedImportBootstrap";

        private static bool s_attached;

        static ProtectedImportBatchRepro()
        {
            AttachCallbacksIfNeeded();
        }

        public static void Run()
        {
            string packagePath = Environment.GetEnvironmentVariable("YUCP_REPRO_PACKAGE_PATH");
            string outputPath = Environment.GetEnvironmentVariable("YUCP_REPRO_OUTPUT_PATH");

            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                WriteResultAndExit(false, $"Package path was not found: '{packagePath ?? string.Empty}'.", outputPath, null);
                return;
            }

            var state = new ReproState
            {
                packagePath = packagePath,
                outputPath = outputPath ?? string.Empty,
                startedUtcTicks = DateTime.UtcNow.Ticks,
                timeoutSeconds = 300,
                phase = "starting",
            };

            SaveState(state);
            AttachCallbacksIfNeeded();
            CleanupPreviousImportArtifacts();

            state.phase = "launching-wizard-import";
            SaveState(state);

            AssetDatabase.Refresh();
            StartWizardImport(packagePath, state);
        }

        private static void AttachCallbacksIfNeeded()
        {
            if (s_attached)
            {
                return;
            }

            Application.logMessageReceived += OnLogMessageReceived;
            EditorApplication.update += OnEditorUpdate;
            s_attached = true;
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            ReproState state = LoadState();
            if (state == null || string.IsNullOrWhiteSpace(condition))
            {
                return;
            }

            if (condition.Contains(SuccessMarker, StringComparison.Ordinal))
            {
                state.observedSuccessMarker = true;
                state.phase = "observed-success-marker";
            }

            if (condition.Contains(FailureMarker, StringComparison.Ordinal))
            {
                state.failureDetected = true;
                state.failureMessage = condition.Trim();
            }

            if (condition.Contains("RegisterPackageAfterImport starting", StringComparison.Ordinal))
            {
                state.phase = "registering-package";
            }

            if (condition.Contains("Queued protected payload apply", StringComparison.Ordinal))
            {
                state.phase = "queued-protected-apply";
            }

            if (condition.Contains("Prepared protected payload", StringComparison.Ordinal))
            {
                state.phase = "prepared-protected-payload";
            }

            if (condition.Contains("Import initiated, waiting for completion", StringComparison.Ordinal))
            {
                state.phase = "waiting-for-import-completion";
            }

            if (condition.Contains("could not be rolled back cleanly", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(state.failureMessage))
            {
                state.failureMessage = condition.Trim();
            }

            SaveState(state);
        }

        private static void OnEditorUpdate()
        {
            ReproState state = LoadState();
            if (state == null)
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            TryAutoAdvanceImportWindow(state);

            if (IsTimedOut(state))
            {
                Fail(state, "Timed out waiting for protected import finalization.");
                return;
            }

            if (state.failureDetected)
            {
                Fail(state, state.failureMessage ?? "Protected import failed.");
                return;
            }

            var registry = InstalledPackageRegistry.Load();
            InstalledPackageInfo packageInfo = registry?.GetPackage(TargetPackageId)
                ?? registry?.GetAllPackages()?.FirstOrDefault(package =>
                    string.Equals(package?.packageName, TargetPackageName, StringComparison.OrdinalIgnoreCase));

            bool pendingFinalization = !string.IsNullOrWhiteSpace(EditorPrefs.GetString(PendingFinalizationStateKey, string.Empty));
            bool targetAssetExists = File.Exists(GetProjectDiskPath(TargetAssetPath));
            bool registryHasTargetAsset = packageInfo?.installedFiles != null &&
                                          packageInfo.installedFiles.Contains(TargetAssetPath);

            if (packageInfo != null && targetAssetExists && registryHasTargetAsset && !pendingFinalization)
            {
                state.phase = "completed";
                SaveState(state);
                WriteResultAndExit(true, "Protected import completed successfully.", state.outputPath, state);
            }
        }

        private static void Fail(ReproState state, string message)
        {
            state.phase = "failed";
            state.failureDetected = true;
            state.failureMessage = string.IsNullOrWhiteSpace(message) ? "Protected import failed." : message.Trim();
            SaveState(state);
            Debug.LogError(BatchFailurePrefix + state.failureMessage);
            WriteResultAndExit(false, state.failureMessage, state.outputPath, state);
        }

        private static bool IsTimedOut(ReproState state)
        {
            DateTime startedUtc = new DateTime(state.startedUtcTicks, DateTimeKind.Utc);
            return (DateTime.UtcNow - startedUtc).TotalSeconds > Math.Max(30, state.timeoutSeconds);
        }

        private static void CleanupPreviousImportArtifacts()
        {
            EditorPrefs.DeleteKey(PendingApplyPackageIdKey);
            EditorPrefs.DeleteKey(PendingApplyStartTicksKey);
            EditorPrefs.DeleteKey(PendingFinalizationStateKey);
            EditorPrefs.DeleteKey(PendingProtectedImportBootstrapKey);

            AssetDatabase.DeleteAsset("Assets/Novaspil_Kitbash");
            AssetDatabase.DeleteAsset("Packages/yucp.installed-packages/Novaspil-Kitbash-Test-License-Verification");

            var registry = InstalledPackageRegistry.Load();
            if (registry != null)
            {
                registry.UnregisterPackage(TargetPackageId);
            }

            AssetDatabase.Refresh();
        }

        private static void StartWizardImport(string packagePath, ReproState state)
        {
            Type packageUtilityType = Type.GetType("UnityEditor.PackageUtility, UnityEditor.CoreModule");
            Type wizardType = Type.GetType("UnityEditor.PackageImportWizard, UnityEditor.CoreModule");
            Type singletonType = Type.GetType("UnityEditor.ScriptableSingleton`1, UnityEditor.CoreModule");
            if (packageUtilityType == null || wizardType == null || singletonType == null)
            {
                Fail(state, "Could not locate Unity package import internals.");
                return;
            }

            MethodInfo extractMethod = packageUtilityType.GetMethod(
                "ExtractAndPrepareAssetList",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo startImportMethod = wizardType.GetMethod(
                "StartImport",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[]
                {
                    typeof(string),
                    Type.GetType("UnityEditor.ImportPackageItem[], UnityEditor.CoreModule"),
                    typeof(string),
                },
                null);

            Type genericSingletonType = singletonType.MakeGenericType(wizardType);
            PropertyInfo instanceProperty = genericSingletonType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);

            if (extractMethod == null || startImportMethod == null || instanceProperty == null)
            {
                Fail(state, "Could not prepare Unity's package import wizard methods.");
                return;
            }

            object[] extractArgs = { packagePath, null, null };
            Array importItems = extractMethod.Invoke(null, extractArgs) as Array;
            string iconPath = extractArgs[1] as string;

            if (importItems == null || importItems.Length == 0)
            {
                Fail(state, "Unity did not return any import items for the repro package.");
                return;
            }

            object wizardInstance = instanceProperty.GetValue(null);
            if (wizardInstance == null)
            {
                Fail(state, "Unity did not provide a PackageImportWizard instance.");
                return;
            }

            state.phase = "waiting-for-import-window";
            state.wizardImportStarted = true;
            SaveState(state);
            Debug.Log($"[YUCP Batch Repro] Starting wizard import for '{Path.GetFileName(packagePath)}' with {importItems.Length} items.");
            startImportMethod.Invoke(wizardInstance, new object[] { packagePath, importItems, iconPath ?? string.Empty });
        }

        private static void TryAutoAdvanceImportWindow(ReproState state)
        {
            if (state == null || !state.wizardImportStarted)
            {
                return;
            }

            if (state.lastAutoAdvanceUtcTicks > 0)
            {
                DateTime lastAdvanceUtc = new DateTime(state.lastAutoAdvanceUtcTicks, DateTimeKind.Utc);
                if ((DateTime.UtcNow - lastAdvanceUtc).TotalSeconds < 2.0)
                {
                    return;
                }
            }

            PackageManagerWindow window = Resources.FindObjectsOfTypeAll<PackageManagerWindow>()
                .FirstOrDefault(candidate => candidate != null);
            if (window == null)
            {
                return;
            }

            FieldInfo resumeModeField = typeof(PackageManagerWindow)
                .GetField("_isResumeVerificationMode", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo waitingForImportCompletionField = typeof(PackageManagerWindow)
                .GetField("_waitingForImportCompletion", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo pendingImportAfterVerificationField = typeof(PackageManagerWindow)
                .GetField("_pendingImportAfterVerification", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo creatorIdentitySigningInField = typeof(PackageManagerWindow)
                .GetField("_isCreatorIdentitySigningIn", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo currentImportItemsField = typeof(PackageManagerWindow)
                .GetField("_currentImportItems", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo requiresVerificationMethod = typeof(PackageManagerWindow)
                .GetMethod("RequiresVerificationBeforeImport", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo onImportClickedMethod = typeof(PackageManagerWindow)
                .GetMethod("OnImportClicked", BindingFlags.NonPublic | BindingFlags.Instance);

            if (resumeModeField == null ||
                waitingForImportCompletionField == null ||
                pendingImportAfterVerificationField == null ||
                creatorIdentitySigningInField == null ||
                currentImportItemsField == null ||
                requiresVerificationMethod == null ||
                onImportClickedMethod == null)
            {
                return;
            }

            bool isResumeMode = (bool)resumeModeField.GetValue(window);
            bool isWaitingForImportCompletion = (bool)waitingForImportCompletionField.GetValue(window);
            bool isPendingImportAfterVerification = (bool)pendingImportAfterVerificationField.GetValue(window);
            bool isCreatorIdentitySigningIn = (bool)creatorIdentitySigningInField.GetValue(window);
            Array currentImportItems = currentImportItemsField.GetValue(window) as Array;
            bool requiresVerification = (bool)requiresVerificationMethod.Invoke(window, null);
            if (!isResumeMode && (currentImportItems == null || currentImportItems.Length == 0))
            {
                return;
            }

            if (isWaitingForImportCompletion || isPendingImportAfterVerification || isCreatorIdentitySigningIn)
            {
                return;
            }

            if (requiresVerification)
            {
                TryStartDirectVerification(window, state);
                return;
            }

            state.phase = isResumeMode ? "auto-confirming-protected-resume" : "auto-confirming-import";
            state.autoAdvanceCount++;
            state.lastAutoAdvanceUtcTicks = DateTime.UtcNow.Ticks;
            SaveState(state);

            Debug.Log($"[YUCP Batch Repro] Auto-confirming {(isResumeMode ? "protected resume" : "import")} window. attempt={state.autoAdvanceCount}");
            onImportClickedMethod.Invoke(window, null);
        }

        private static void TryStartDirectVerification(PackageManagerWindow window, ReproState state)
        {
            if (state.directVerificationStarted || state.directVerificationCompleted)
            {
                return;
            }

            MethodInfo getNextUnverifiedLicenseRequirementMethod = typeof(PackageManagerWindow)
                .GetMethod("GetNextUnverifiedLicenseRequirement", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo onImportClickedMethod = typeof(PackageManagerWindow)
                .GetMethod("OnImportClicked", BindingFlags.NonPublic | BindingFlags.Instance);
            if (getNextUnverifiedLicenseRequirementMethod == null || onImportClickedMethod == null)
            {
                return;
            }

            object requirement = getNextUnverifiedLicenseRequirementMethod.Invoke(window, null);
            if (requirement == null)
            {
                return;
            }

            string packageId = GetMemberValue<string>(requirement, "packageId");
            string productId = GetMemberValue<string>(requirement, "productId");
            string creatorAuthUserId = GetMemberValue<string>(requirement, "creatorAuthUserId");

            if (string.IsNullOrWhiteSpace(packageId) ||
                string.IsNullOrWhiteSpace(productId) ||
                string.IsNullOrWhiteSpace(creatorAuthUserId))
            {
                return;
            }

            Type licenseServerResolverType = typeof(PackageManagerWindow).Assembly
                .GetType("YUCP.Importer.Editor.PackageManager.Core.LicenseServerResolver", throwOnError: false);
            Type licenseVerificationServiceType = typeof(PackageManagerWindow).Assembly
                .GetType("YUCP.Importer.Editor.PackageManager.Core.LicenseVerificationService", throwOnError: false);

            MethodInfo getLicenseServerUrlMethod = licenseServerResolverType?.GetMethod(
                "GetLicenseServerUrl",
                BindingFlags.Public | BindingFlags.Static);
            MethodInfo verifyDiscordAsyncMethod = licenseVerificationServiceType?.GetMethod(
                "VerifyDiscordAsync",
                BindingFlags.Public | BindingFlags.Static);

            if (getLicenseServerUrlMethod == null || verifyDiscordAsyncMethod == null)
            {
                return;
            }

            string serverUrl = getLicenseServerUrlMethod.Invoke(null, null) as string;
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                Fail(state, "Could not determine the license server URL for direct verification.");
                return;
            }

            state.directVerificationStarted = true;
            state.phase = "direct-verification";
            state.lastAutoAdvanceUtcTicks = DateTime.UtcNow.Ticks;
            SaveState(state);

            Debug.Log($"[YUCP Batch Repro] Refreshing Creator Identity license token directly for packageId='{packageId}'.");

            Action<string> onSuccess = _ =>
            {
                ReproState latest = LoadState();
                if (latest == null)
                {
                    return;
                }

                latest.directVerificationStarted = false;
                latest.directVerificationCompleted = true;
                latest.phase = "direct-verification-succeeded";
                SaveState(latest);
                Debug.Log($"[YUCP Batch Repro] Direct verification succeeded for packageId='{packageId}'.");
                EditorApplication.delayCall += () => onImportClickedMethod.Invoke(window, null);
            };

            Action<string> onError = error =>
            {
                ReproState latest = LoadState();
                if (latest == null)
                {
                    return;
                }

                latest.directVerificationStarted = false;
                latest.failureDetected = true;
                latest.failureMessage = "Direct Creator Identity verification failed: " + (error ?? "Unknown error.");
                latest.phase = "direct-verification-failed";
                SaveState(latest);
                Debug.LogError("[YUCP Batch Repro] " + latest.failureMessage);
            };

            verifyDiscordAsyncMethod.Invoke(null, new object[] { serverUrl, packageId, productId, creatorAuthUserId, onSuccess, onError });
        }

        private static T GetMemberValue<T>(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
            {
                return default;
            }

            Type type = instance.GetType();
            FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.GetValue(instance) is T fieldValue)
            {
                return fieldValue;
            }

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null && property.GetValue(instance) is T propertyValue)
            {
                return propertyValue;
            }

            return default;
        }

        private static string GetProjectDiskPath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string relativePath = assetPath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(projectRoot, relativePath);
        }

        private static ReproState LoadState()
        {
            string raw = EditorPrefs.GetString(StateKey, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<ReproState>(raw);
            }
            catch
            {
                return null;
            }
        }

        private static void SaveState(ReproState state)
        {
            if (state == null)
            {
                EditorPrefs.DeleteKey(StateKey);
                return;
            }

            EditorPrefs.SetString(StateKey, JsonUtility.ToJson(state));
        }

        private static void WriteResultAndExit(bool success, string message, string outputPath, ReproState state)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    string directory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var result = new ReproResult
                    {
                        success = success,
                        message = message ?? string.Empty,
                        observedSuccessMarker = state?.observedSuccessMarker ?? false,
                        failureDetected = state?.failureDetected ?? !success,
                        targetAssetExists = File.Exists(GetProjectDiskPath(TargetAssetPath)),
                        pendingFinalization = !string.IsNullOrWhiteSpace(EditorPrefs.GetString(PendingFinalizationStateKey, string.Empty)),
                    };

                    File.WriteAllText(outputPath, JsonUtility.ToJson(result, true));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP Batch Repro] Failed to write repro output: {ex.Message}");
            }
            finally
            {
                SaveState(null);
                EditorApplication.Exit(success ? 0 : 1);
            }
        }

        [Serializable]
        private sealed class ReproState
        {
            public string packagePath;
            public string outputPath;
            public string phase;
            public string failureMessage;
            public long startedUtcTicks;
            public int timeoutSeconds;
            public bool observedSuccessMarker;
            public bool failureDetected;
            public bool wizardImportStarted;
            public int autoAdvanceCount;
            public long lastAutoAdvanceUtcTicks;
            public bool directVerificationStarted;
            public bool directVerificationCompleted;
        }

        [Serializable]
        private sealed class ReproResult
        {
            public bool success;
            public string message;
            public bool observedSuccessMarker;
            public bool failureDetected;
            public bool targetAssetExists;
            public bool pendingFinalization;
        }
    }
}
