using System;

namespace YUCP.Importer.Editor.PackageManager
{
    [Serializable]
    public class ProtectedImportIntentDescriptor
    {
        public string formatVersion = "1";
        public string packageId = "";
        public string protectedAssetId = "";
        public string protectedPayloadAssetPath = "";
        public string tempInstallAssetPath = "";
        public string manifestBindingSha256 = "";
        public bool requiresProtectedPayload = true;

        public ProtectedImportIntentDescriptor Clone()
        {
            return new ProtectedImportIntentDescriptor
            {
                formatVersion = formatVersion ?? "1",
                packageId = packageId ?? "",
                protectedAssetId = protectedAssetId ?? "",
                protectedPayloadAssetPath = protectedPayloadAssetPath ?? "",
                tempInstallAssetPath = tempInstallAssetPath ?? "",
                manifestBindingSha256 = manifestBindingSha256 ?? "",
                requiresProtectedPayload = requiresProtectedPayload,
            };
        }
    }
}
