using System;
using System.Collections.Generic;

namespace YUCP.Importer.Editor.PackageManager
{
    [Serializable]
    public class AliasPackageRequirement
    {
        public string name = "";
        public string range = "";
    }

    [Serializable]
    public class AliasPackageContract
    {
        public string kind = "";
        public string aliasId = "";
        public string packageName = "";
        public string packageDisplayName = "";
        public string packageVersion = "";
        public string installStrategy = "";
        public string importerPackage = "";
        public string minImporterVersion = "";
        public string channel = "";
        public AliasPackageMediaSet media = new AliasPackageMediaSet();
        public BootstrapIntentContract bootstrapIntent;
        // What the release pulls in, for the import screen only. Lists, not a
        // dictionary, so the set survives a domain reload with the window.
        public List<AliasPackageRequirement> releaseVpmDependencies =
            new List<AliasPackageRequirement>();
        public List<AliasPackageRequirement> releaseVpmRepositories =
            new List<AliasPackageRequirement>();
        public string rawContractJson = "";
        // Never read from package.json: ParseAliasPackageContract fills this
        // contract field by field. Serialized to survive domain reloads.
        public bool directUnityPackageBootstrap;

        public AliasPackageContract Clone()
        {
            return new AliasPackageContract
            {
                kind = kind ?? string.Empty,
                aliasId = aliasId ?? string.Empty,
                packageName = packageName ?? string.Empty,
                packageDisplayName = packageDisplayName ?? string.Empty,
                packageVersion = packageVersion ?? string.Empty,
                installStrategy = installStrategy ?? string.Empty,
                importerPackage = importerPackage ?? string.Empty,
                minImporterVersion = minImporterVersion ?? string.Empty,
                channel = channel ?? string.Empty,
                media = media?.Clone() ?? new AliasPackageMediaSet(),
                bootstrapIntent = bootstrapIntent?.Clone(),
                releaseVpmDependencies = CloneRequirements(releaseVpmDependencies),
                releaseVpmRepositories = CloneRequirements(releaseVpmRepositories),
                rawContractJson = rawContractJson ?? string.Empty,
                directUnityPackageBootstrap = directUnityPackageBootstrap,
            };
        }

        internal static Dictionary<string, string> ToMap(
            List<AliasPackageRequirement> requirements)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (requirements == null)
            {
                return map;
            }
            foreach (AliasPackageRequirement requirement in requirements)
            {
                string name = requirement?.name?.Trim();
                string range = requirement?.range?.Trim();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(range))
                {
                    continue;
                }
                map[name] = range;
            }
            return map;
        }

        private static List<AliasPackageRequirement> CloneRequirements(
            List<AliasPackageRequirement> requirements)
        {
            var clone = new List<AliasPackageRequirement>();
            if (requirements == null)
            {
                return clone;
            }
            foreach (AliasPackageRequirement requirement in requirements)
            {
                if (requirement == null)
                {
                    continue;
                }
                clone.Add(new AliasPackageRequirement
                {
                    name = requirement.name ?? string.Empty,
                    range = requirement.range ?? string.Empty,
                });
            }
            return clone;
        }
    }

    [Serializable]
    public class BootstrapIntentContract
    {
        public int schemaVersion;
        public string intentId = "";
        public string mode = "";
        public long issuedAt;
        public string keyId = "";
        public string editionId = "";
        public string version = "";
        public string versionId = "";
        public string releaseRoot = "";
        public string requirementsDigest = "";
        public string signature = "";
        public string rawIntentJson = "";

        public BootstrapIntentContract Clone()
        {
            return new BootstrapIntentContract
            {
                schemaVersion = schemaVersion,
                intentId = intentId ?? string.Empty,
                mode = mode ?? string.Empty,
                issuedAt = issuedAt,
                keyId = keyId ?? string.Empty,
                editionId = editionId ?? string.Empty,
                version = version ?? string.Empty,
                versionId = versionId ?? string.Empty,
                releaseRoot = releaseRoot ?? string.Empty,
                requirementsDigest = requirementsDigest ?? string.Empty,
                signature = signature ?? string.Empty,
                rawIntentJson = rawIntentJson ?? string.Empty,
            };
        }
    }

    [Serializable]
    public class AliasPackageMediaSet
    {
        public AliasPackageMediaDescriptor banner = new AliasPackageMediaDescriptor();
        public List<AliasPackageMediaDescriptor> gallery =
            new List<AliasPackageMediaDescriptor>();
        public AliasPackageMediaDescriptor icon = new AliasPackageMediaDescriptor();
        public List<AliasPackageMediaDescriptor> productLinks =
            new List<AliasPackageMediaDescriptor>();

        public AliasPackageMediaSet Clone()
        {
            return new AliasPackageMediaSet
            {
                banner = banner?.Clone() ?? new AliasPackageMediaDescriptor(),
                gallery = gallery?.ConvertAll(item =>
                    item?.Clone() ?? new AliasPackageMediaDescriptor()) ??
                    new List<AliasPackageMediaDescriptor>(),
                icon = icon?.Clone() ?? new AliasPackageMediaDescriptor(),
                productLinks = productLinks?.ConvertAll(item =>
                    item?.Clone() ?? new AliasPackageMediaDescriptor()) ??
                    new List<AliasPackageMediaDescriptor>(),
            };
        }
    }

    [Serializable]
    public class AliasPackageMediaDescriptor
    {
        public string kind = "";
        public string contentType = "";
        public long byteSize;
        public string label = "";
        public int ordinal = -1;
        public string sha256 = "";
        public string localPath = "";
        public string url = "";

        public AliasPackageMediaDescriptor Clone()
        {
            return new AliasPackageMediaDescriptor
            {
                kind = kind ?? string.Empty,
                contentType = contentType ?? string.Empty,
                byteSize = byteSize,
                label = label ?? string.Empty,
                ordinal = ordinal,
                sha256 = sha256 ?? string.Empty,
                localPath = localPath ?? string.Empty,
                url = url ?? string.Empty,
            };
        }
    }
}
