using System;

namespace YUCP.Importer.Editor.PackageManager
{
    [Serializable]
    public class ProtectedPayloadDescriptor
    {
        public string formatVersion = "1";
        public string protectedAssetId = "";
        public string blobAssetPath = "";
        public string cipher = "";
        public string archiveFormat = "";
        public string ciphertextSha256 = "";
        public long ciphertextSize = 0;
        public string plaintextSha256 = "";
        public long plaintextSize = 0;
        public int entryCount = 0;
        public bool requiresOnlineUnlock = false;

        public ProtectedPayloadDescriptor Clone()
        {
            return new ProtectedPayloadDescriptor
            {
                formatVersion = formatVersion ?? "1",
                protectedAssetId = protectedAssetId ?? "",
                blobAssetPath = blobAssetPath ?? "",
                cipher = cipher ?? "",
                archiveFormat = archiveFormat ?? "",
                ciphertextSha256 = ciphertextSha256 ?? "",
                ciphertextSize = ciphertextSize,
                plaintextSha256 = plaintextSha256 ?? "",
                plaintextSize = plaintextSize,
                entryCount = entryCount,
                requiresOnlineUnlock = requiresOnlineUnlock,
            };
        }
    }
}
