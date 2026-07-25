using System;
using System.Collections.Generic;

namespace YUCP.Importer.Editor.PackageManager
{
    [Serializable]
    public class AliasPackageInstallStateManifest
    {
        public string formatVersion = "2";
        public string aliasId = "";
        public string aliasVersion = "";
        public string packageId = "";
        public string packageName = "";
        public string packageDisplayName = "";
        public string installedVersion = "";
        public string installedAt = "";
        public List<string> managedPaths = new List<string>();
        public List<string> generatedPaths = new List<string>();
        public List<string> sharedPaths = new List<string>();
        public List<InstallStateFileHashRecord> fileHashes = new List<InstallStateFileHashRecord>();
    }

    [Serializable]
    public class InstallStateFileHashRecord
    {
        public string path = "";
        public string expectedSha256 = "";
        public string observedSha256 = "";
    }
}
