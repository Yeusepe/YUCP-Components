using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager
{
    /// <summary>
    /// Metadata structure for package information displayed in the import window.
    /// This is read-only from the user's perspective.
    /// </summary>
    [Serializable]
    public class PackageMetadata
    {
        public string packageName = "";
        public string version = "";
        public string author = "";
        public string description = "";
        public Texture2D icon;
        public Texture2D banner;
        public List<ProductLink> productLinks = new List<ProductLink>();
        public string versionRule = "semver";
        public string versionRuleName = "semver";
        public Dictionary<string, string> dependencies = new Dictionary<string, string>(); // package name -> version

        /// <summary>
        /// Packages (per original export profile) that require a verified purchase license
        /// before derived FBX assets are applied. Populated from YUCP_PackageInfo.json at import time.
        /// </summary>
        public List<LicensePackageRequirement> licensePackages = new List<LicensePackageRequirement>();

        public PackageMetadata()
        {
        }

        public PackageMetadata(string packageName, string version = "", string author = "", string description = "")
        {
            this.packageName = packageName;
            this.version = version;
            this.author = author;
            this.description = description;
        }
    }

    /// <summary>
    /// Represents a product link with optional icon.
    /// Mirrors ExportProfile.ProductLink structure.
    /// </summary>
    [Serializable]
    public class ProductLink
    {
        public string label = "";
        public string url = "";
        public Texture2D customIcon;
        public Texture2D icon;

        public ProductLink()
        {
        }

        public ProductLink(string url, string label = "")
        {
            this.url = url;
            this.label = label;
        }

        /// <summary>
        /// Get the icon to display (custom icon takes priority over auto-fetched icon)
        /// </summary>
        public Texture2D GetDisplayIcon()
        {
            return customIcon != null ? customIcon : icon;
        }
    }

    /// <summary>
    /// Describes a YUCP package that requires a verified purchase license
    /// before derived FBX assets are decrypted and applied.
    /// </summary>
    [Serializable]
    public class LicensePackageRequirement
    {
        /// <summary>YUCP packageId (e.g. "yeusepe/MyAvatar").</summary>
        public string packageId = "";
        /// <summary>Friendly display name shown in the license prompt.</summary>
        public string packageName = "";
        /// <summary>Canonical YUCP product ID from the product catalog.</summary>
        public string productId = "";
        /// <summary>Gumroad product permalink used for server-side verification.</summary>
        public string gumroadPermalink = "";
        /// <summary>Jinxxy product ID used for server-side verification.</summary>
        public string jinxxyProductId = "";
        /// <summary>Discord guild ID for role-based verification (creator's server).</summary>
        public string discordGuildId = "";
        /// <summary>Discord role ID required within the creator's guild.</summary>
        public string discordRoleId = "";
        /// <summary>Creator's YUCP auth user ID — needed for Discord entitlement lookup.</summary>
        public string creatorAuthUserId = "";
    }
}





