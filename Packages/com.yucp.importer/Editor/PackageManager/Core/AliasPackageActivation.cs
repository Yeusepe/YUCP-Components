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
    internal sealed class AliasPackageActivationRequest : IDisposable
    {
        private bool _ownsMetadataMedia = true;
        private bool _mediaLoaded;
        private string _mediaPackageRoot = string.Empty;

        internal AliasPackageActivationRequest(
            PackageMetadata metadata,
            string key,
            string mediaPackageRoot = null)
        {
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Key = key ?? throw new ArgumentNullException(nameof(key));
            _mediaPackageRoot = mediaPackageRoot ?? string.Empty;
            _mediaLoaded = !string.IsNullOrWhiteSpace(mediaPackageRoot);
        }

        internal string ActionLabel => "Verify and Import";

        internal AliasPackageContract Alias => Metadata.aliasPackage;

        internal string Key { get; }

        internal PackageMetadata Metadata { get; }

        public void Dispose()
        {
            if (!_ownsMetadataMedia)
            {
                return;
            }
            PackageMetadataMediaOwnership.Release(Metadata);
            _ownsMetadataMedia = false;
        }

        internal void SetMediaPackageRoot(string packageRoot)
        {
            _mediaPackageRoot = packageRoot ?? string.Empty;
        }

        internal void EnsureMediaLoaded()
        {
            if (_mediaLoaded ||
                string.IsNullOrWhiteSpace(_mediaPackageRoot))
            {
                return;
            }
            AliasPackageMediaLoader.Apply(
                Metadata,
                Alias,
                _mediaPackageRoot);
            _mediaLoaded = true;
        }

        internal PackageMetadata TransferMetadataOwnership()
        {
            _ownsMetadataMedia = false;
            return Metadata;
        }
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
            EditorApplication.delayCall +=
                ResumePendingUnityPackageActivations;
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
                GetRegisteredActivations(false)
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
            return TryBuildActivation(
                packageId,
                packageJson,
                null,
                out activation,
                out error);
        }

        internal static bool TryBuildActivation(
            string packageId,
            string packageJson,
            string packageRoot,
            out AliasPackageActivationRequest activation,
            out string error)
        {
            activation = null;
            if (!AliasPackageDiscovery.TryBuildMetadata(
                    packageId,
                    packageJson,
                    packageRoot,
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
                PackageMetadataMediaOwnership.Release(metadata);
                error = "Alias package identity is incomplete.";
                return false;
            }

            activation = new AliasPackageActivationRequest(
                metadata,
                BuildActivationKey(metadata.aliasPackage),
                packageRoot);
            error = null;
            return true;
        }

        internal static bool TryBuildUnityPackageActivation(
            string descriptorJson,
            out AliasPackageActivationRequest activation,
            out string error)
        {
            bool built = TryBuildActivation(
                string.Empty,
                descriptorJson,
                out activation,
                out error);
            if (built)
            {
                activation.Alias.directUnityPackageBootstrap = true;
            }
            return built;
        }

        /// <summary>
        /// Receives a complete bootstrap descriptor from the source-built direct
        /// Unitypackage installer after com.yucp.importer is available.
        /// </summary>
        public static void SubmitUnityPackageDescriptor(
            string descriptorJson)
        {
            if (!TryBuildUnityPackageActivation(
                    descriptorJson,
                    out AliasPackageActivationRequest activation,
                    out string error))
            {
                throw new FormatException(
                    "The Unitypackage bootstrap descriptor is invalid: " +
                    error);
            }

            string projectPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));

            string mediaPackageRoot = ResolveBootstrapMediaRoot(
                projectPath,
                activation.Alias);
            if (mediaPackageRoot != null)
            {
                activation.SetMediaPackageRoot(mediaPackageRoot);
            }

            if (AliasPackageActivationStateStore.IsHandled(
                    projectPath,
                    activation.Alias))
            {
                activation.Dispose();
                return;
            }
            DirectUnityPackageBootstrapStore.Persist(
                projectPath,
                activation.Alias,
                descriptorJson);
            Schedule(activation);
        }

        /// <summary>
        /// A bootstrap descriptor carries the media manifest but not the bytes.
        /// The Unitypackage already wrote those under the bootstrap's package
        /// folder, so the activation has to be pointed at it: with no root the
        /// loader has nothing to read and the installer opens with no
        /// presentation at all. Returns null when there is nothing to read.
        /// </summary>
        internal static string ResolveBootstrapMediaRoot(
            string projectPath,
            AliasPackageContract alias)
        {
            if (string.IsNullOrWhiteSpace(projectPath) ||
                string.IsNullOrWhiteSpace(alias?.packageName) ||
                alias.packageName.Contains("/") ||
                alias.packageName.Contains("\\") ||
                alias.packageName.Contains(".."))
            {
                return null;
            }
            string root = Path.Combine(projectPath, "Packages", alias.packageName);
            return Directory.Exists(root) ? root : null;
        }

        private static void ResumePendingUnityPackageActivations()
        {
            string projectPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            IReadOnlyList<string> descriptors;
            try
            {
                descriptors =
                    DirectUnityPackageBootstrapStore.ReadAll(projectPath);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"{LogPrefix} Could not read pending Unitypackage " +
                    "bootstraps: " + exception.Message);
                return;
            }

            foreach (string descriptor in descriptors)
            {
                try
                {
                    SubmitUnityPackageDescriptor(descriptor);
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"{LogPrefix} A pending Unitypackage bootstrap " +
                        "is invalid: " + exception.Message);
                }
            }
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

            SessionState.SetBool(
                BuildDismissalSessionKey(BuildActivationKey(alias)),
                true);
        }

        internal static string BuildActivationKey(
            AliasPackageContract alias)
        {
            if (alias == null)
            {
                throw new ArgumentNullException(nameof(alias));
            }
            if (string.Equals(alias.kind, "alias-v2", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(alias.bootstrapIntent?.intentId))
            {
                return alias.bootstrapIntent.intentId;
            }
            return $"{alias.packageName}@{alias.packageVersion}:{alias.aliasId}";
        }

        internal static bool ShouldSchedule(
            string currentReleaseRoot,
            bool activationHandled)
        {
            return string.Equals(
                    currentReleaseRoot,
                    PackageLifecycleCoordinator.EmptyReleaseRoot,
                    StringComparison.Ordinal) &&
                !activationHandled;
        }

        internal static bool ShouldSchedule(
            AliasPackageContract alias,
            string currentReleaseRoot,
            bool activationHandled)
        {
            if (activationHandled || alias == null)
            {
                return false;
            }
            if (string.Equals(alias.kind, "alias-v2", StringComparison.Ordinal))
            {
                return !string.IsNullOrWhiteSpace(
                    alias.bootstrapIntent?.intentId);
            }
            return ShouldSchedule(currentReleaseRoot, false);
        }

        internal static bool ShouldScheduleForProject(
            string projectPath,
            AliasPackageActivationRequest activation)
        {
            if (activation == null)
            {
                return false;
            }
            if (PackageLifecycleCoordinator.GetPendingOperation(
                    projectPath,
                    activation.Alias.aliasId) != null)
            {
                return true;
            }
            string currentReleaseRoot =
                PackageLifecycleCoordinator.GetMaterializedReleaseRoot(
                    projectPath,
                    activation.Alias.aliasId);
            bool activationHandled =
                AliasPackageActivationStateStore.IsHandled(
                    projectPath,
                    activation.Alias);
            return ShouldSchedule(
                activation.Alias,
                currentReleaseRoot,
                activationHandled);
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

        private static IEnumerable<AliasPackageActivationRequest>
            GetRegisteredActivations(bool loadMedia = true)
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
                        loadMedia,
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
            try
            {
                activation.EnsureMediaLoaded();
                PackageManagerWindow.ShowAliasBootstrap(
                    activation.TransferMetadataOwnership());
            }
            catch
            {
                activation.Dispose();
                throw;
            }
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
                        true,
                        out AliasPackageActivationRequest activation))
                {
                    Schedule(activation);
                }
            }
        }

        private static bool TryReadActivation(
            PackageInfo package,
            bool loadMedia,
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
                bool built = File.Exists(packageJsonPath) &&
                    TryBuildActivation(
                        package.name,
                        File.ReadAllText(packageJsonPath),
                        loadMedia
                            ? package.resolvedPath
                            : null,
                        out activation,
                        out _);
                if (built && !loadMedia)
                {
                    activation.SetMediaPackageRoot(
                        package.resolvedPath);
                }
                return built;
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
            if (activation == null)
            {
                return;
            }
            if (PackageManagerWindow.IsAliasBootstrapOpen(
                    activation.Alias.aliasId))
            {
                activation.Dispose();
                return;
            }

            string projectPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            if (!ShouldScheduleForProject(
                    projectPath,
                    activation))
            {
                activation.Dispose();
                return;
            }

            string dismissalKey =
                BuildDismissalSessionKey(activation.Key);
            if (SessionState.GetBool(dismissalKey, false))
            {
                activation.Dispose();
                return;
            }
            if (!Scheduled.Add(activation.Key))
            {
                activation.Dispose();
                return;
            }

            Debug.Log(
                $"{LogPrefix} Queued '{activation.Alias.packageName}' " +
                "for verified package installation.");
            EditorApplication.delayCall += () =>
            {
                try
                {
                    // Descriptor-built activations defer their media, so load it
                    // here the way the manual entry point does.
                    activation.EnsureMediaLoaded();
                    PackageManagerWindow.ShowAliasBootstrap(
                        activation.TransferMetadataOwnership());
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
