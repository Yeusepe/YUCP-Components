using System;
using System.Collections.Generic;

namespace YUCP.Components.Editor.PackageVerifier.Data
{
    [Serializable]
    public class PackageManifest
    {
        public string authorityId;
        public string keyId;
        public string publisherId;
        public string packageId;
        public string version;
        public string archiveSha256;
        public string vrchatAuthorUserId;
        public Dictionary<string, string> fileHashes;
        public CertificateData[] certificateChain; // Certificate chain: [Publisher, Intermediate?, Root]
    }
}




