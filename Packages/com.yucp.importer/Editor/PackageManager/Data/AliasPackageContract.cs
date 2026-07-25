using System;
using System.Collections.Generic;
using System.Linq;

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
        public List<string> catalogProductIds = new List<string>();
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
                catalogProductIds = catalogProductIds != null
                    ? new List<string>(catalogProductIds.Where(value => !string.IsNullOrWhiteSpace(value)))
                    : new List<string>(),
                rawContractJson = rawContractJson ?? string.Empty,
            };
        }
    }

    [Serializable]
    public class AliasPackageMediaSet
    {
        public AliasPackageMediaDescriptor banner = new AliasPackageMediaDescriptor();
        public AliasPackageMediaDescriptor icon = new AliasPackageMediaDescriptor();

        public AliasPackageMediaSet Clone()
        {
            return new AliasPackageMediaSet
            {
                banner = banner?.Clone() ?? new AliasPackageMediaDescriptor(),
                icon = icon?.Clone() ?? new AliasPackageMediaDescriptor(),
            };
        }
    }

    [Serializable]
    public class AliasPackageMediaDescriptor
    {
        public string kind = "";
        public string downloadUrl = "";
        public string contentType = "";
        public long byteSize;
        public string sha256 = "";
        public string sourcePath = "";
        public string localPath = "";

        public AliasPackageMediaDescriptor Clone()
        {
            return new AliasPackageMediaDescriptor
            {
                kind = kind ?? string.Empty,
                downloadUrl = downloadUrl ?? string.Empty,
                contentType = contentType ?? string.Empty,
                byteSize = byteSize,
                sha256 = sha256 ?? string.Empty,
                sourcePath = sourcePath ?? string.Empty,
                localPath = localPath ?? string.Empty,
            };
        }
    }
}
