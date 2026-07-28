using System;
using System.Collections.Generic;

namespace YUCP.Importer.Editor.PackageManager
{
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
        public string rawContractJson = "";

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
                rawContractJson = rawContractJson ?? string.Empty,
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
