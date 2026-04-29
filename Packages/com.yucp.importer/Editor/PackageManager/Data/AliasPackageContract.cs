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
        public AliasResolvedReleaseIdentity resolvedRelease = new AliasResolvedReleaseIdentity();
        public AliasResolvedArtifactIdentity resolvedArtifact = new AliasResolvedArtifactIdentity();
        public AliasInstallPlanMetadata installPlan = new AliasInstallPlanMetadata();
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
                resolvedRelease = resolvedRelease?.Clone() ?? new AliasResolvedReleaseIdentity(),
                resolvedArtifact = resolvedArtifact?.Clone() ?? new AliasResolvedArtifactIdentity(),
                installPlan = installPlan?.Clone() ?? new AliasInstallPlanMetadata(),
                rawContractJson = rawContractJson ?? string.Empty,
            };
        }
    }

    [Serializable]
    public class AliasResolvedReleaseIdentity
    {
        public string releaseId = "";
        public string version = "";
        public string channel = "";
        public string artifactId = "";

        public AliasResolvedReleaseIdentity Clone()
        {
            return new AliasResolvedReleaseIdentity
            {
                releaseId = releaseId ?? string.Empty,
                version = version ?? string.Empty,
                channel = channel ?? string.Empty,
                artifactId = artifactId ?? string.Empty,
            };
        }
    }

    [Serializable]
    public class AliasResolvedArtifactIdentity
    {
        public string artifactId = "";
        public string version = "";
        public string sha256 = "";
        public string downloadUrl = "";

        public AliasResolvedArtifactIdentity Clone()
        {
            return new AliasResolvedArtifactIdentity
            {
                artifactId = artifactId ?? string.Empty,
                version = version ?? string.Empty,
                sha256 = sha256 ?? string.Empty,
                downloadUrl = downloadUrl ?? string.Empty,
            };
        }
    }

    [Serializable]
    public class AliasInstallPlanMetadata
    {
        public string planId = "";
        public string planVersion = "";
        public string operation = "";
        public string status = "";
        public List<string> managedPaths = new List<string>();
        public List<string> generatedPaths = new List<string>();
        public List<string> sharedPaths = new List<string>();
        public string rawPlanJson = "";

        public AliasInstallPlanMetadata Clone()
        {
            return new AliasInstallPlanMetadata
            {
                planId = planId ?? string.Empty,
                planVersion = planVersion ?? string.Empty,
                operation = operation ?? string.Empty,
                status = status ?? string.Empty,
                managedPaths = managedPaths != null
                    ? new List<string>(managedPaths.Where(value => !string.IsNullOrWhiteSpace(value)))
                    : new List<string>(),
                generatedPaths = generatedPaths != null
                    ? new List<string>(generatedPaths.Where(value => !string.IsNullOrWhiteSpace(value)))
                    : new List<string>(),
                sharedPaths = sharedPaths != null
                    ? new List<string>(sharedPaths.Where(value => !string.IsNullOrWhiteSpace(value)))
                    : new List<string>(),
                rawPlanJson = rawPlanJson ?? string.Empty,
            };
        }
    }
}
