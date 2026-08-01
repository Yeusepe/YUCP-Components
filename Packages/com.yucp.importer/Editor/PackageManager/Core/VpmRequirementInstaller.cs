using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using VRC.PackageManagement.Core;
using VRC.PackageManagement.Core.Types;
using VRC.PackageManagement.Core.Types.Packages;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    /// <summary>
    /// Installs the VPM packages a release declares. The bootstrap installer
    /// only ever runs once and deletes itself, so the importer owns this: a
    /// release that gains a dependency in an update has to reach buyers who
    /// already have the importer.
    /// </summary>
    internal static class VpmRequirementInstaller
    {
        internal static void Install(
            string projectPath,
            IReadOnlyDictionary<string, string> dependencies,
            IReadOnlyDictionary<string, string> repositories)
        {
            List<KeyValuePair<string, string>> requirements =
                PlanRequirements(dependencies);
            if (string.IsNullOrWhiteSpace(projectPath) || requirements.Count == 0)
            {
                return;
            }
            RegisterRepositories(repositories);
            UnityProject project;
            Dictionary<string, string> present;
            try
            {
                project = new UnityProject(projectPath);
                if (!project.valid)
                {
                    LogRequirementFailure(
                        new InvalidOperationException(
                            "this folder is not a Unity project"));
                    return;
                }
                present = new Dictionary<string, string>(
                    project.GetInstalledVersions() ??
                        new Dictionary<string, string>(),
                    StringComparer.Ordinal);
            }
            catch (Exception exception)
            {
                LogRequirementFailure(exception);
                return;
            }
            var installed = new List<string>();
            var unavailable = new List<string>();
            foreach (KeyValuePair<string, string> requirement in requirements)
            {
                // A buyer who already has the package keeps their version:
                // pulling VRCFury forward mid-import breaks more than it fixes.
                if (present.ContainsKey(requirement.Key))
                {
                    continue;
                }
                try
                {
                    IVRCPackage match = Repos.GetPackageWithVersionMatch(
                        requirement.Key,
                        requirement.Value);
                    if (match == null ||
                        !project.AddVPMPackage(match.Id, match.Version, Repos.GetAll))
                    {
                        unavailable.Add(requirement.Key + "@" + requirement.Value);
                        continue;
                    }
                    installed.Add(match.Id + "@" + match.Version);
                    foreach (KeyValuePair<string, string> entry in
                        project.GetInstalledVersions() ??
                            new Dictionary<string, string>())
                    {
                        present[entry.Key] = entry.Value;
                    }
                }
                catch (Exception exception)
                {
                    unavailable.Add(
                        requirement.Key + "@" + requirement.Value +
                        " (" + exception.Message + ")");
                }
            }
            if (installed.Count > 0)
            {
                Client.Resolve();
                AssetDatabase.Refresh();
                Debug.Log(
                    "[YUCP PackageManager] Installed " + installed.Count +
                    " release requirement(s): " + string.Join(", ", installed.ToArray()));
            }
            else if (unavailable.Count == 0)
            {
                Debug.Log(
                    "[YUCP PackageManager] All " + requirements.Count +
                    " release requirement(s) were already installed.");
            }
            if (unavailable.Count > 0)
            {
                Debug.LogWarning(
                    "[YUCP PackageManager] Could not install " + unavailable.Count +
                    " release requirement(s): " + string.Join(", ", unavailable.ToArray()) +
                    ". Add them from the Creator Companion to finish setting up " +
                    "this package.");
            }
        }

        internal static List<KeyValuePair<string, string>> PlanRequirements(
            IReadOnlyDictionary<string, string> requirements)
        {
            var planned = new List<KeyValuePair<string, string>>();
            if (requirements == null)
            {
                return planned;
            }
            foreach (KeyValuePair<string, string> requirement in requirements)
            {
                string id = requirement.Key?.Trim();
                string range = requirement.Value?.Trim();
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(range))
                {
                    continue;
                }
                planned.Add(new KeyValuePair<string, string>(id, range));
            }
            return planned;
        }

        internal static bool IsSupportedListing(string url, out Uri listing)
        {
            return Uri.TryCreate(url?.Trim(), UriKind.Absolute, out listing) &&
                string.Equals(
                    listing.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.Ordinal);
        }

        private static void RegisterRepositories(
            IReadOnlyDictionary<string, string> repositories)
        {
            if (repositories == null)
            {
                return;
            }
            foreach (KeyValuePair<string, string> repository in repositories)
            {
                string url = repository.Value?.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    continue;
                }
                if (!IsSupportedListing(url, out Uri listing))
                {
                    Debug.LogWarning(
                        "[YUCP PackageManager] Skipped the package listing '" +
                        url + "' because it is not an https URL.");
                    continue;
                }
                try
                {
                    if (!Repos.UserRepoExists(listing))
                    {
                        Repos.AddRepo(
                            listing,
                            new Dictionary<string, string>(
                                StringComparer.Ordinal));
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "[YUCP PackageManager] Could not add the package " +
                        "listing '" + url + "': " + exception.Message);
                }
            }
        }

        private static void LogRequirementFailure(Exception exception)
        {
            Debug.LogWarning(
                "[YUCP PackageManager] Could not install the release " +
                "requirements: " + exception.Message);
        }
    }
}
