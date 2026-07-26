using System;
using System.IO;
using UnityEngine;
using VRC.PackageManagement.Core.Types;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    /// <summary>
    /// Removes a completed product bootstrap through the VPM project graph.
    /// </summary>
    internal static class VpmBootstrapPackageCleanup
    {
        internal static string RemoveInstalledAlias(
            string projectPath,
            string packageName)
        {
            if (string.IsNullOrWhiteSpace(projectPath) ||
                !Path.IsPathRooted(projectPath) ||
                string.IsNullOrWhiteSpace(packageName))
            {
                return "The VPM bootstrap cleanup request is invalid.";
            }

            string resolvedProjectPath = Path.GetFullPath(projectPath);
            string packageJsonPath = Path.Combine(
                resolvedProjectPath,
                "Packages",
                packageName,
                "package.json");
            try
            {
                if (!File.Exists(packageJsonPath) ||
                    !AliasPackageActivation.TryBuildActivation(
                        packageName,
                        File.ReadAllText(packageJsonPath),
                        out _,
                        out _))
                {
                    return "The installed package is not a valid YUCP bootstrap.";
                }

                // This uses the same VPM project operation as the supported
                // `vpm remove package` command.
                // https://vcc.docs.vrchat.com/vpm/cli/#remove-package
                var project = new UnityProject(resolvedProjectPath);
                if (!project.RemoveVPMPackage(packageName, true, null))
                {
                    return "VPM did not remove the product bootstrap.";
                }
                return null;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[YUCP PackageManager][BootstrapCleanup] " +
                    "VPM cleanup failed with " +
                    exception.GetType().Name +
                    ".");
                return "VPM could not remove the product bootstrap.";
            }
        }
    }
}
