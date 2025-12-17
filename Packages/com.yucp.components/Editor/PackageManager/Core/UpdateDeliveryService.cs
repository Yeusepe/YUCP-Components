using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Service for checking and delivering updates for installed packages
    /// </summary>
    public static class UpdateDeliveryService
    {
        /// <summary>
        /// Check for updates for a specific package by packageId
        /// Returns the latest version if available, or null if no update
        /// </summary>
        public static string CheckForUpdate(string packageId, string currentVersion)
        {
            if (string.IsNullOrEmpty(packageId))
                return null;

            // TODO: Query server API for updates by packageId
            // For now, we don't have a server API endpoint, so updates are not checked
            // This will be implemented when the server API is available
            
            return null;
        }

        /// <summary>
        /// Check for updates for all installed packages
        /// Updates the hasUpdate flag in InstalledPackageRegistry
        /// </summary>
        public static void CheckAllUpdates()
        {
            var registry = InstalledPackageRegistry.GetOrCreate();
            var packages = registry.GetAllPackages();

            foreach (var package in packages)
            {
                if (string.IsNullOrEmpty(package.packageId))
                    continue;

                string latestVersion = CheckForUpdate(package.packageId, package.installedVersion);
                package.hasUpdate = !string.IsNullOrEmpty(latestVersion);
                if (package.hasUpdate)
                {
                    package.latestVersion = latestVersion;
                }
            }

            registry.Save();
        }

        /// <summary>
        /// Compare two version strings (simple semantic version comparison)
        /// Returns: >0 if v1 > v2, 0 if equal, <0 if v1 < v2
        /// </summary>
        private static int CompareVersions(string v1, string v2)
        {
            if (v1 == v2) return 0;
            if (string.IsNullOrEmpty(v1)) return -1;
            if (string.IsNullOrEmpty(v2)) return 1;

            // Simple split by dots and compare
            var parts1 = v1.Split('.');
            var parts2 = v2.Split('.');

            int maxLength = Math.Max(parts1.Length, parts2.Length);
            for (int i = 0; i < maxLength; i++)
            {
                int part1 = i < parts1.Length && int.TryParse(parts1[i], out int p1) ? p1 : 0;
                int part2 = i < parts2.Length && int.TryParse(parts2[i], out int p2) ? p2 : 0;

                if (part1 != part2)
                    return part1.CompareTo(part2);
            }

            return 0;
        }
    }
}




















