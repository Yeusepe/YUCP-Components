using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    /// <summary>
    /// Drives the downloadable-installer flow: once the importer is in the project, this asks the
    /// server to authorize the signed-in account and then registers the private VPM repository the
    /// account owns in VRChat Creator Companion. Triggered explicitly from the menu and offered once
    /// per project on first load so importing the installer "just works".
    /// </summary>
    [InitializeOnLoad]
    internal static class ServerAuthorizedRepositoryInstaller
    {
        private const string LogPrefix = "[YUCP Installer][RepositoryInstaller]";
        private const string MenuPath = "Tools/YUCP/Add My Repository to VCC";
        private const string FirstRunPromptKeyPrefix = "YUCP.Installer.RepositoryFirstRunPromptedV1.";

        private static bool s_running;

        static ServerAuthorizedRepositoryInstaller()
        {
            if (!PackageManagerRuntimeSettings.IsEnabled())
            {
                return;
            }

            EditorApplication.delayCall += MaybePromptFirstRun;
        }

        [MenuItem(MenuPath, false, 2000)]
        internal static void StartFromMenu()
        {
            BeginFlow();
        }

        private static void MaybePromptFirstRun()
        {
            string promptKey = FirstRunPromptKeyPrefix + ResolveProjectIdentity();
            if (EditorPrefs.GetBool(promptKey, false))
            {
                return;
            }

            // Only offer once per project, whether or not the creator accepts, so reopening Unity
            // does not re-pop the browser prompt.
            EditorPrefs.SetBool(promptKey, true);

            bool proceed = YucpEditorDialog.DisplayDialog(
                "Add Your YUCP Packages",
                "Connect your YUCP account to add the package repository you own to VRChat Creator Companion.\n\n" +
                "YUCP verifies your access in your browser, then adds your private repository to VCC automatically. " +
                "You can also run this any time from Tools → YUCP → Add My Repository to VCC.",
                "Connect and Add to VCC",
                "Later");
            if (proceed)
            {
                BeginFlow();
            }
        }

        internal static void BeginFlow()
        {
            if (s_running)
            {
                return;
            }

            string serverUrl = LicenseServerResolver.GetLicenseServerUrl();
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                YucpEditorDialog.DisplayErrorDialog(
                    "YUCP Installer",
                    "The YUCP server URL is not configured. Open Project Settings → YUCP Package Manager and choose a trusted server, then try again.");
                return;
            }

            s_running = true;

            if (CreatorIdentityOAuthService.IsSignedIn())
            {
                ProvisionAndAddToVcc(serverUrl, reauthenticationAttempted: false);
                return;
            }

            SignInThenProvision(serverUrl, reauthenticationAttempted: false);
        }

        private static void SignInThenProvision(string serverUrl, bool reauthenticationAttempted)
        {
            _ = CreatorIdentityOAuthService.SignInAsync(
                serverUrl,
                onSuccess: () => EditorApplication.delayCall +=
                    () => ProvisionAndAddToVcc(serverUrl, reauthenticationAttempted),
                onError: error => EditorApplication.delayCall += () => Fail(
                    string.IsNullOrWhiteSpace(error) ? "Sign-in failed." : error));
        }

        private static void ProvisionAndAddToVcc(string serverUrl, bool reauthenticationAttempted)
        {
            BackstageRepositoryProvisioningService.ProvisionResult result;
            try
            {
                EditorUtility.DisplayProgressBar(
                    "YUCP Installer",
                    "Authorizing the repository you own…",
                    0.45f);
                result = BackstageRepositoryProvisioningService.Provision(serverUrl);
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Fail($"Could not authorize repository access: {ex.Message}");
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (result.requiresSignIn && !reauthenticationAttempted)
            {
                // The cached session lapsed between IsSignedIn() and the request; re-authenticate once.
                CreatorIdentityOAuthService.SignOut();
                SignInThenProvision(serverUrl, reauthenticationAttempted: true);
                return;
            }

            if (!result.success || result.access == null)
            {
                Fail(string.IsNullOrWhiteSpace(result.error)
                    ? "Could not authorize your repository."
                    : result.error);
                return;
            }

            AddToVcc(result.access);
        }

        private static void AddToVcc(BackstageRepositoryProvisioningService.RepoAccess access)
        {
            s_running = false;

            // Copy the catalog URL up front so the creator has a manual fallback even if the
            // vcc:// handler is not registered (VCC not installed / protocol not associated).
            EditorGUIUtility.systemCopyBuffer = access.repositoryUrl;

            try
            {
                Application.OpenURL(access.addRepoUrl);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} Could not open the VCC add-repository link: {ex.Message}");
            }

            string repositoryLabel = string.IsNullOrWhiteSpace(access.repositoryName)
                ? access.repositoryUrl
                : access.repositoryName;

            Debug.Log($"{LogPrefix} Authorized repository '{repositoryLabel}' and opened the VCC add-repository link.");

            YucpEditorDialog.DisplayDialog(
                "Repository Ready",
                $"Opening VRChat Creator Companion to add:\n\n{repositoryLabel}\n\n" +
                "If VCC did not open, the repository URL has been copied to your clipboard — paste it into " +
                "VCC → Settings → Packages → Add Repository.\n\n" +
                access.repositoryUrl,
                "Done");
        }

        private static void Fail(string message)
        {
            s_running = false;
            EditorUtility.ClearProgressBar();
            YucpEditorDialog.DisplayErrorDialog(
                "YUCP Installer",
                string.IsNullOrWhiteSpace(message)
                    ? "Could not add your repository to VCC."
                    : message);
        }

        private static string ResolveProjectIdentity()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return projectRoot
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .ToLowerInvariant();
            }
            catch (Exception)
            {
                return "unknown-project";
            }
        }
    }
}
