using System;
using System.IO;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager
{
    internal static class InstallerCoordinationState
    {
        private const string MarkerPrefix = "install.";
        private const string TempInstallFilePattern = "YUCP_TempInstall_*.json";

        private static readonly string[] ActiveMarkerNames =
        {
            "scheduled",
            "pending",
            "lock"
        };

        internal static bool HasPendingInstallerHandoff()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string markerRoot = Path.Combine(projectRoot, "Library", "YUCP");
                if (!Directory.Exists(markerRoot))
                {
                    return false;
                }

                if (!HasPendingTempInstallDescriptor(projectRoot))
                {
                    return false;
                }

                foreach (string markerName in ActiveMarkerNames)
                {
                    if (File.Exists(Path.Combine(markerRoot, MarkerPrefix + markerName)))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP PackageManager] Failed to probe installer coordination markers: {ex.Message}");
            }

            return false;
        }

        private static bool HasPendingTempInstallDescriptor(string projectRoot)
        {
            string installedRoot = Path.Combine(projectRoot, "Packages", "yucp.installed-packages");
            if (Directory.Exists(installedRoot) &&
                Directory.GetFiles(installedRoot, TempInstallFilePattern, SearchOption.AllDirectories).Length > 0)
            {
                return true;
            }

            return Directory.GetFiles(Application.dataPath, TempInstallFilePattern, SearchOption.TopDirectoryOnly).Length > 0;
        }
    }
}
