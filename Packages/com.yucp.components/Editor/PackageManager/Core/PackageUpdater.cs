using System;
using System.Collections;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Handles periodic update checking and update operations for installed packages
    /// </summary>
    public static class PackageUpdater
    {
        private static double s_lastUpdateCheck = 0;
        private static readonly double UPDATE_CHECK_INTERVAL = 300.0; // 5 minutes in seconds

        /// <summary>
        /// Initialize periodic update checking
        /// Should be called from EditorApplication.update
        /// </summary>
        public static void Update()
        {
            double currentTime = EditorApplication.timeSinceStartup;
            
            if (currentTime - s_lastUpdateCheck >= UPDATE_CHECK_INTERVAL)
            {
                s_lastUpdateCheck = currentTime;
                CheckForUpdates();
            }
        }

        /// <summary>
        /// Check for updates for all installed packages
        /// </summary>
        public static void CheckForUpdates()
        {
            try
            {
                UpdateDeliveryService.CheckAllUpdates();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PackageUpdater] Error checking for updates: {ex.Message}");
            }
        }

        /// <summary>
        /// Force an immediate update check
        /// </summary>
        public static void ForceUpdateCheck()
        {
            s_lastUpdateCheck = 0; // Reset timer to force immediate check
            CheckForUpdates();
        }
    }
}





