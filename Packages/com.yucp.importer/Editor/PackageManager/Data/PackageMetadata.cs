using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager
{
    [Serializable]
    public class PackageMetadata : ISerializationCallbackReceiver
    {
        [SerializeField]
        private List<string> dependencyKeys = new List<string>();

        [SerializeField]
        private List<string> dependencyValues = new List<string>();

        public string packageName = "";
        public string version = "";
        public string author = "";
        public string description = "";
        public Texture2D icon;
        public Texture2D banner;
        public List<ProductLink> productLinks = new List<ProductLink>();
        public Dictionary<string, string> dependencies = new Dictionary<string, string>();
        public string versionRule = "semver";
        public string versionRuleName = "semver";

        public List<LicensePackageRequirement> licensePackages = new List<LicensePackageRequirement>();

        public string tagline = "";
        public string category = "";
        public List<string> supportedPlatforms = new List<string>();
        public string minimumUnityVersion = "";
        public string creatorNote = "";
        public string releaseNotes = "";
        public List<Texture2D> galleryImages = new List<Texture2D>();
        public List<string> tags = new List<string>();
        public int totalFileCount = 0;
        public long totalFileSize = 0;
        public List<AssetBreakdownEntry> assetBreakdown = new List<AssetBreakdownEntry>();
        public string exportDate = "";
        public List<PackageFileHashEntry> fileHashes = new List<PackageFileHashEntry>();

        public AliasPackageContract aliasPackage;

        public PackageMetadata()
        {
        }

        public PackageMetadata(string packageName)
        {
            this.packageName = packageName ?? "";
        }

        public void OnBeforeSerialize()
        {
            dependencyKeys ??= new List<string>();
            dependencyValues ??= new List<string>();
            dependencyKeys.Clear();
            dependencyValues.Clear();
            if (dependencies == null)
            {
                return;
            }

            var keys = new List<string>(dependencies.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                dependencyKeys.Add(key);
                dependencyValues.Add(dependencies[key]);
            }
        }

        public void OnAfterDeserialize()
        {
            dependencies = new Dictionary<string, string>(
                StringComparer.Ordinal);
            if (dependencyKeys == null || dependencyValues == null)
            {
                return;
            }

            int count = Math.Min(
                dependencyKeys.Count,
                dependencyValues.Count);
            for (int index = 0; index < count; index++)
            {
                string key = dependencyKeys[index];
                if (key != null)
                {
                    dependencies[key] = dependencyValues[index];
                }
            }
        }
    }

    [Serializable]
    public class ProductLink
    {
        public string url = "";
        public string label = "";
        public Texture2D customIcon;

        public ProductLink()
        {
        }

        public ProductLink(string url, string label)
        {
            this.url = url ?? "";
            this.label = label ?? "";
        }

        public Texture2D GetDisplayIcon()
        {
            return customIcon;
        }
    }

    [Serializable]
    public class LicensePackageRequirement
    {
        public string packageId = "";
        public string packageName = "";
        public string productId = "";
        public string gumroadPermalink = "";
        public string jinxxyProductId = "";
        public string discordGuildId = "";
        public string discordRoleId = "";
        public string creatorAuthUserId = "";
    }

    [Serializable]
    public class AssetBreakdownEntry
    {
        public string type = "";
        public int count = 0;

        public AssetBreakdownEntry()
        {
        }

        public AssetBreakdownEntry(string type, int count)
        {
            this.type = type ?? "";
            this.count = count;
        }
    }

    [Serializable]
    public class PackageFileHashEntry
    {
        public string path = "";
        public string hash = "";
    }

}
