using System;

namespace YUCP.Components.Editor.PackageVerifier.Data
{
    [Serializable]
    public class SignatureData
    {
        public string algorithm;
        public string keyId;
        public string signature; // BASE64
    }
}



