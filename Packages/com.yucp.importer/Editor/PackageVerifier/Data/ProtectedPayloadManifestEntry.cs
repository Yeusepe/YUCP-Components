using System;

namespace YUCP.Importer.Editor.PackageVerifier.Data
{
    [Serializable]
    public class ProtectedPayloadManifestEntry
    {
        public string formatVersion;
        public string protectedAssetId;
        public string blobAssetPath;
        public string cipher;
        public string archiveFormat;
        public string ciphertextSha256;
        public long ciphertextSize;
        public string plaintextSha256;
        public long plaintextSize;
        public int entryCount;
        public string[] payloadAssetPaths;
        public bool requiresOnlineUnlock;
        public bool requiresBrokeredMaterialization;
        public int brokerProtocolVersion;
        public string manifestBindingSha256;
    }
}
