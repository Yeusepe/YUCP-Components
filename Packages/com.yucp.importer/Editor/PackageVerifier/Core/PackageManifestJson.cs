using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YUCP.Importer.Editor.PackageVerifier.Data;

namespace YUCP.Importer.Editor.PackageVerifier.Core
{
    public static class PackageManifestJson
    {
        public static PackageManifest ParseManifest(string manifestJson)
        {
            JObject root = LoadObject(manifestJson);
            if (root == null)
                return null;

            return new PackageManifest
            {
                authorityId = GetString(root, "authorityId"),
                keyId = GetString(root, "keyId"),
                publisherId = GetString(root, "publisherId"),
                packageId = GetString(root, "packageId"),
                version = GetString(root, "version"),
                archiveSha256 = GetString(root, "archiveSha256"),
                vrchatAuthorUserId = GetString(root, "vrchatAuthorUserId"),
                fileHashes = ParseFileHashes(root["fileHashes"]),
                certificateChain = ParseCertificateChain(root["certificateChain"]),
                gumroadProductId = GetString(root, "gumroadProductId"),
                jinxxyProductId = GetString(root, "jinxxyProductId"),
            };
        }

        public static SignatureData ParseSignature(string signatureJson)
        {
            JObject root = LoadObject(signatureJson);
            if (root == null)
                return null;

            return new SignatureData
            {
                algorithm = GetString(root, "algorithm"),
                keyId = GetString(root, "keyId"),
                signature = GetString(root, "signature"),
                certificateIndex = root.Value<int?>("certificateIndex") ?? 0,
            };
        }

        private static JObject LoadObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            using var stringReader = new StringReader(json);
            using var jsonReader = new JsonTextReader(stringReader)
            {
                DateParseHandling = DateParseHandling.None,
            };

            return JObject.Load(jsonReader);
        }

        private static Dictionary<string, string> ParseFileHashes(JToken token)
        {
            var result = new Dictionary<string, string>();
            var fileHashesObject = token as JObject;
            if (fileHashesObject == null)
                return result;

            foreach (var property in fileHashesObject.Properties())
            {
                result[property.Name] = property.Value.Type == JTokenType.Null
                    ? null
                    : property.Value.Value<string>();
            }

            return result;
        }

        private static CertificateData[] ParseCertificateChain(JToken token)
        {
            var chainArray = token as JArray;
            if (chainArray == null)
                return null;

            var certificates = new List<CertificateData>();
            foreach (var certificateToken in chainArray)
            {
                var certificateObject = certificateToken as JObject;
                if (certificateObject == null)
                    continue;

                certificates.Add(ParseCertificate(certificateObject));
            }

            return certificates.ToArray();
        }

        private static CertificateData ParseCertificate(JObject certificateObject)
        {
            var certificate = new CertificateData
            {
                keyId = GetString(certificateObject, "keyId"),
                publicKey = GetString(certificateObject, "publicKey"),
                signature = GetString(certificateObject, "signature"),
                issuerKeyId = GetString(certificateObject, "issuerKeyId"),
                certificateType = ParseCertificateType(certificateObject["certificateType"]),
                publisherId = GetString(certificateObject, "publisherId"),
                notBefore = GetString(certificateObject, "notBefore"),
                notAfter = GetString(certificateObject, "notAfter"),
            };

            if (certificate.certificateType == CertificateType.Root)
            {
                certificate.signature = NullIfEmpty(certificate.signature);
                certificate.issuerKeyId = NullIfEmpty(certificate.issuerKeyId);
                certificate.publisherId = NullIfEmpty(certificate.publisherId);
                certificate.notBefore = NullIfEmpty(certificate.notBefore);
                certificate.notAfter = NullIfEmpty(certificate.notAfter);
            }

            return certificate;
        }

        private static CertificateType ParseCertificateType(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return CertificateType.Root;

            if (token.Type == JTokenType.Integer)
            {
                int enumValue = token.Value<int>();
                if (Enum.IsDefined(typeof(CertificateType), enumValue))
                    return (CertificateType)enumValue;
                return CertificateType.Root;
            }

            string enumName = token.Value<string>();
            if (Enum.TryParse(enumName, true, out CertificateType parsedType))
                return parsedType;

            return CertificateType.Root;
        }

        private static string GetString(JObject obj, string propertyName)
        {
            JToken token = obj[propertyName];
            if (token == null || token.Type == JTokenType.Null)
                return null;
            return token.Value<string>();
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
