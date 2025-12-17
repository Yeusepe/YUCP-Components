using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Information about an installed package, extending PackageMetadata with installation-specific data
    /// </summary>
    [Serializable]
    public class InstalledPackageInfo : PackageMetadata
    {
        /// <summary>
        /// Package ID from manifest - used for update tracking
        /// </summary>
        public string packageId = "";

        /// <summary>
        /// Archive SHA-256 hash for verification
        /// </summary>
        public string archiveSha256 = "";

        /// <summary>
        /// Date and time when package was installed (ISO 8601 format)
        /// </summary>
        public string installedDate = "";

        /// <summary>
        /// Version that was installed
        /// </summary>
        public string installedVersion = "";

        /// <summary>
        /// Whether the package signature was verified
        /// </summary>
        public bool isVerified = false;

        /// <summary>
        /// Publisher ID from manifest
        /// </summary>
        public string publisherId = "";

        /// <summary>
        /// List of asset paths that were installed by this package
        /// </summary>
        public List<string> installedFiles = new List<string>();

        /// <summary>
        /// Latest version available (for update checking)
        /// </summary>
        public string latestVersion = "";

        /// <summary>
        /// Whether an update is available
        /// </summary>
        public bool hasUpdate = false;

        /// <summary>
        /// Get installed date as DateTime
        /// </summary>
        public DateTime GetInstalledDateTime()
        {
            if (string.IsNullOrEmpty(installedDate))
                return DateTime.MinValue;

            if (DateTime.TryParse(installedDate, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime result))
                return result;

            return DateTime.MinValue;
        }

        /// <summary>
        /// Set installed date from DateTime
        /// </summary>
        public void SetInstalledDateTime(DateTime dateTime)
        {
            installedDate = dateTime.ToString("O"); // ISO 8601 format
        }
    }
}




















