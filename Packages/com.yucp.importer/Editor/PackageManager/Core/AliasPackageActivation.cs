using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal sealed class AliasPackageActivationRequest
    {
        internal AliasPackageActivationRequest(
            PackageMetadata metadata,
            string key)
        {
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Key = key ?? throw new ArgumentNullException(nameof(key));
        }

        internal string ActionLabel => "Verify and Import";

        internal AliasPackageContract Alias => Metadata.aliasPackage;

        internal IReadOnlyList<string> CatalogProductIds =>
            Alias.catalogProductIds
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

        internal string Key { get; }

        internal PackageMetadata Metadata { get; }
    }

    /// <summary>
    /// Activates public VPM aliases after Unity registers their package graph.
    /// </summary>
    [InitializeOnLoad]
    internal static class AliasPackageActivation
    {
        private const string LogPrefix =
            "[YUCP PackageManager][AliasActivation]";
        private const string DismissalSessionKeyPrefix =
            "YUCP.PackageManager.AliasActivation.DismissedV1.";
        private const string ManualInstallerMenuPath =
            "Tools/YUCP/Package Manager/Open Product Installer";
        private static readonly HashSet<string> Scheduled =
            new HashSet<string>(StringComparer.Ordinal);

        static AliasPackageActivation()
        {
            if (Application.isBatchMode ||
                !PackageManagerRuntimeSettings.IsEnabled())
            {
                return;
            }

            // Unity raises this event after package registration, compilation,
            // and domain reload. InitializeOnLoad preserves the subscription.
            // https://docs.unity3d.com/2022.3/Documentation/ScriptReference/PackageManager.Events-registeredPackages.html
            Events.registeredPackages += OnRegisteredPackages;

            // Reconcile aliases that arrived in the same package graph as the
            // importer. The importer could not subscribe before it compiled.
            EditorApplication.delayCall += ReconcileUninstalledAliases;
        }

        [MenuItem(ManualInstallerMenuPath, false, 200)]
        private static void OpenPackageInstallerFromMenu()
        {
            if (!PackageManagerRuntimeSettings.IsEnabled())
            {
                bool openSettings = EditorUtility.DisplayDialog(
                    "YUCP Package Manager",
                    "The package manager is disabled for this project.",
                    "Open Settings",
                    "Cancel");
                if (openSettings)
                {
                    SettingsService.OpenProjectSettings(
                        "Project/YUCP Package Manager");
                }
                return;
            }

            AliasPackageActivationRequest[] activations =
                GetRegisteredActivations()
                    .OrderBy(
                        activation => activation.Metadata.packageName,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        activation => activation.Alias.aliasId,
                        StringComparer.Ordinal)
                    .ToArray();
            if (activations.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "YUCP Package Manager",
                    "This project has no registered YUCP product package.",
                    "OK");
                return;
            }

            if (activations.Length == 1)
            {
                OpenManualActivation(activations[0]);
                return;
            }

            var menu = new GenericMenu();
            foreach (AliasPackageActivationRequest activation in activations)
            {
                AliasPackageActivationRequest selected = activation;
                string label = string.IsNullOrWhiteSpace(
                        activation.Metadata.packageName)
                    ? activation.Alias.aliasId
                    : activation.Metadata.packageName;
                menu.AddItem(
                    new GUIContent(label),
                    false,
                    () => OpenManualActivation(selected));
            }
            menu.ShowAsContext();
        }

        internal static bool TryBuildActivation(
            string packageId,
            string packageJson,
            out AliasPackageActivationRequest activation,
            out string error)
        {
            activation = null;
            if (!AliasPackageDiscovery.TryBuildMetadata(
                    packageId,
                    packageJson,
                    out PackageMetadata metadata,
                    out error))
            {
                return false;
            }

            string packageName = metadata.aliasPackage.packageName;
            string packageVersion = metadata.aliasPackage.packageVersion;
            string aliasId = metadata.aliasPackage.aliasId;
            if (string.IsNullOrWhiteSpace(packageName) ||
                string.IsNullOrWhiteSpace(packageVersion))
            {
                error = "Alias package identity is incomplete.";
                return false;
            }

            activation = new AliasPackageActivationRequest(
                metadata,
                $"{packageName}@{packageVersion}:{aliasId}");
            error = null;
            return true;
        }

        internal static string BuildDismissalSessionKey(string activationKey)
        {
            if (string.IsNullOrWhiteSpace(activationKey))
            {
                throw new ArgumentException(
                    "The alias activation key is required.",
                    nameof(activationKey));
            }

            return DismissalSessionKeyPrefix + activationKey;
        }

        internal static void DismissForSession(AliasPackageContract alias)
        {
            if (alias == null ||
                string.IsNullOrWhiteSpace(alias.packageName) ||
                string.IsNullOrWhiteSpace(alias.packageVersion) ||
                string.IsNullOrWhiteSpace(alias.aliasId))
            {
                return;
            }

            string activationKey =
                $"{alias.packageName}@{alias.packageVersion}:{alias.aliasId}";
            SessionState.SetBool(
                BuildDismissalSessionKey(activationKey),
                true);
        }

        private static void OnRegisteredPackages(
            PackageRegistrationEventArgs args)
        {
            if (args == null)
            {
                return;
            }

            SchedulePackages(args.added);
            SchedulePackages(args.changedTo);
        }

        private static void ReconcileUninstalledAliases()
        {
            foreach (AliasPackageActivationRequest activation in
                GetRegisteredActivations())
            {
                string projectPath = Path.GetFullPath(
                    Path.Combine(Application.dataPath, ".."));
                string currentReleaseRoot =
                    PackageLifecycleCoordinator.GetCurrentReleaseRoot(
                        projectPath,
                        activation.Alias.aliasId);
                if (string.Equals(
                        currentReleaseRoot,
                        PackageLifecycleCoordinator.EmptyReleaseRoot,
                        StringComparison.Ordinal))
                {
                    Schedule(activation);
                    continue;
                }

                string cleanupError =
                    VpmBootstrapPackageCleanup.RemoveInstalledAlias(
                        projectPath,
                        activation.Alias.packageName);
                if (!string.IsNullOrWhiteSpace(cleanupError))
                {
                    Debug.LogError(
                        $"{LogPrefix} Installed bootstrap cleanup failed.");
                    continue;
                }

                EditorApplication.delayCall += () =>
                    Client.Resolve();
            }
        }

        private static IEnumerable<AliasPackageActivationRequest>
            GetRegisteredActivations()
        {
            PackageInfo[] packages = PackageInfo.GetAllRegisteredPackages();
            if (packages == null)
            {
                yield break;
            }

            foreach (PackageInfo package in packages)
            {
                if (TryReadActivation(
                        package,
                        out AliasPackageActivationRequest activation))
                {
                    yield return activation;
                }
            }
        }

        private static void OpenManualActivation(
            AliasPackageActivationRequest activation)
        {
            if (activation == null)
            {
                return;
            }

            SessionState.EraseBool(
                BuildDismissalSessionKey(activation.Key));
            Scheduled.Remove(activation.Key);
            PackageManagerWindow.ShowAliasBootstrap(activation.Metadata);
        }

        private static void SchedulePackages(IEnumerable<PackageInfo> packages)
        {
            if (packages == null)
            {
                return;
            }

            foreach (PackageInfo package in packages)
            {
                if (TryReadActivation(
                        package,
                        out AliasPackageActivationRequest activation))
                {
                    Schedule(activation);
                }
            }
        }

        private static bool TryReadActivation(
            PackageInfo package,
            out AliasPackageActivationRequest activation)
        {
            activation = null;
            if (package == null ||
                string.IsNullOrWhiteSpace(package.resolvedPath))
            {
                return false;
            }

            string packageJsonPath = Path.Combine(
                package.resolvedPath,
                "package.json");
            try
            {
                return File.Exists(packageJsonPath) &&
                    TryBuildActivation(
                        package.name,
                        File.ReadAllText(packageJsonPath),
                        out activation,
                        out _);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void Schedule(AliasPackageActivationRequest activation)
        {
            if (activation == null ||
                PackageManagerWindow.IsAliasBootstrapOpen(
                    activation.Alias.aliasId) ||
                !Scheduled.Add(activation.Key))
            {
                return;
            }

            string dismissalKey =
                BuildDismissalSessionKey(activation.Key);
            if (SessionState.GetBool(dismissalKey, false))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix} Queued '{activation.Alias.packageName}' " +
                "for verified package installation.");
            EditorApplication.delayCall += () =>
            {
                try
                {
                    PackageManagerWindow.ShowAliasBootstrap(
                        activation.Metadata);
                }
                catch (Exception exception)
                {
                    Scheduled.Remove(activation.Key);
                    Debug.LogError(
                        $"{LogPrefix} Could not open the importer: " +
                        exception.Message);
                }
            };
        }
    }
}
