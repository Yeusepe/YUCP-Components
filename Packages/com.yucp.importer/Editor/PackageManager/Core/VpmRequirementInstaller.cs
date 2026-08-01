using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using VRC.PackageManagement.Core;
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
            if (string.IsNullOrWhiteSpace(projectPath) ||
                dependencies == null ||
                dependencies.Count == 0)
            {
                return;
            }
            RegisterRepositories(repositories);
            VPMProjectManifest manifest;
            try
            {
                manifest = VPMProjectManifest.Load(projectPath);
            }
            catch (Exception exception)
            {
                LogRequirementFailure(exception);
                return;
            }
            if (manifest == null)
            {
                LogRequirementFailure(
                    new InvalidOperationException(
                        "this project has no VPM manifest"));
                return;
            }
            if (manifest.dependencies == null)
            {
                manifest.dependencies =
                    new Dictionary<string, VPMProjectManifest
                        .VPMPackageInfoMinimal>(StringComparer.Ordinal);
            }
            var declared = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, VPMProjectManifest.VPMPackageInfoMinimal>
                         entry in manifest.dependencies)
            {
                declared[entry.Key] = entry.Value?.version;
            }
            List<KeyValuePair<string, string>> requested =
                PlanRequirements(declared, dependencies);
            foreach (KeyValuePair<string, string> requirement in requested)
            {
                manifest.dependencies[requirement.Key] =
                    new VPMProjectManifest.VPMPackageInfoMinimal
                    {
                        version = requirement.Value,
                    };
            }
            try
            {
                if (requested.Count > 0)
                {
                    manifest.Save();
                }
                if (!VPMProjectManifest.ResolveIsNeeded(projectPath))
                {
                    return;
                }
                VPMProjectManifest.Resolve(projectPath);
                Client.Resolve();
                AssetDatabase.Refresh();
                Debug.Log(
                    "[YUCP PackageManager] Installed " +
                    dependencies.Count +
                    " release requirement(s).");
            }
            catch (Exception exception)
            {
                LogRequirementFailure(exception);
            }
        }

        /// <summary>
        /// The requirements whose manifest entry has to change. A buyer who
        /// already declares the same range keeps their locked version.
        /// </summary>
        internal static List<KeyValuePair<string, string>> PlanRequirements(
            IReadOnlyDictionary<string, string> declared,
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
                if (declared != null &&
                    declared.TryGetValue(id, out string existing) &&
                    string.Equals(existing, range, StringComparison.Ordinal))
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
