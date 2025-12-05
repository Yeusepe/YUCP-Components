using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.PackageManager
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
}






