using System.Reflection;
using NUnit.Framework;
using YUCP.Importer.Editor.PackageVerifier.Core;
using YUCP.Importer.Editor.PackageVerifier.Data;
using PackageVerifierCore = YUCP.Importer.Editor.PackageVerifier.Core.PackageVerifier;

namespace YUCP.Importer.Editor.Tests
{
    public class PackageManifestJsonTests
    {
        [Test]
        public void ParseManifest_NormalizesEmptyRootCertificateFields_ForSignatureCanonicalization()
        {
            const string manifestJson = "{"
                + "\"authorityId\":\"unitysign.yucp\","
                + "\"keyId\":\"yucp-authority-2025\","
                + "\"publisherId\":\"publisher-123\","
                + "\"packageId\":\"package-123\","
                + "\"version\":\"1.0.0\","
                + "\"archiveSha256\":\"abc123\","
                + "\"vrchatAuthorUserId\":\"\","
                + "\"certificateChain\":["
                + "{"
                + "\"keyId\":\"yucp-publisher:test\","
                + "\"publicKey\":\"publisher-public-key\","
                + "\"signature\":\"publisher-signature\","
                + "\"issuerKeyId\":\"yucp-root-2025\","
                + "\"certificateType\":2,"
                + "\"publisherId\":\"publisher-123\","
                + "\"notBefore\":\"2026-03-24T00:00:00.000Z\","
                + "\"notAfter\":\"2026-06-24T00:00:00.000Z\""
                + "},"
                + "{"
                + "\"keyId\":\"yucp-root-2025\","
                + "\"publicKey\":\"root-public-key\","
                + "\"signature\":\"\","
                + "\"issuerKeyId\":\"\","
                + "\"certificateType\":0,"
                + "\"publisherId\":\"\","
                + "\"notBefore\":\"\","
                + "\"notAfter\":\"\""
                + "}"
                + "],"
                + "\"gumroadProductId\":\"gumroad-product\","
                + "\"jinxxyProductId\":\"\","
                + "\"fileHashes\":{}"
                + "}";

            var manifest = PackageManifestJson.ParseManifest(manifestJson);

            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.fileHashes, Is.Not.Null);
            Assert.That(manifest.fileHashes, Is.Empty);
            Assert.That(manifest.certificateChain, Has.Length.EqualTo(2));

            var root = manifest.certificateChain[1];
            Assert.That(root.certificateType, Is.EqualTo(CertificateType.Root));
            Assert.That(root.signature, Is.Null);
            Assert.That(root.issuerKeyId, Is.Null);
            Assert.That(root.publisherId, Is.Null);
            Assert.That(root.notBefore, Is.Null);
            Assert.That(root.notAfter, Is.Null);

            var canonicalizeMethod = typeof(PackageVerifierCore).GetMethod(
                "CanonicalizeManifest",
                BindingFlags.NonPublic | BindingFlags.Static
            );

            Assert.That(canonicalizeMethod, Is.Not.Null);

            string canonical = canonicalizeMethod.Invoke(null, new object[] { manifest }) as string;

            Assert.That(canonical, Is.Not.Null);
            StringAssert.Contains("\"fileHashes\":{}", canonical);
            StringAssert.Contains("\"issuerKeyId\":null", canonical);
            StringAssert.Contains("\"signature\":null", canonical);
            StringAssert.DoesNotContain("\"issuerKeyId\":\"\"", canonical);
            StringAssert.DoesNotContain("\"signature\":\"\"", canonical);
        }

        [Test]
        public void ParseManifest_ParsesProtectedPayloadEntries()
        {
            const string binding = "binding-sha-123";
            string manifestJson = "{"
                + "\"authorityId\":\"unitysign.yucp\","
                + "\"keyId\":\"yucp-authority-2025\","
                + "\"publisherId\":\"publisher-123\","
                + "\"packageId\":\"package-123\","
                + "\"version\":\"1.0.0\","
                + "\"archiveSha256\":\"abc123\","
                + "\"fileHashes\":{},"
                + "\"protectedPayloads\":[{"
                + "\"formatVersion\":\"1\","
                + "\"protectedAssetId\":\"protected-asset-123\","
                + "\"blobAssetPath\":\"Assets/Protected/payload.blob\","
                + "\"cipher\":\"aes-256-cbc+hmac-sha256\","
                + "\"archiveFormat\":\"zip\","
                + "\"ciphertextSha256\":\"ciphertext-sha\","
                + "\"ciphertextSize\":42,"
                + "\"plaintextSha256\":\"plaintext-sha\","
                + "\"plaintextSize\":24,"
                + "\"entryCount\":2,"
                + "\"payloadAssetPaths\":[\"Assets/A.prefab\",\"Assets/B.png\"],"
                + "\"requiresOnlineUnlock\":true,"
                + "\"requiresBrokeredMaterialization\":true,"
                + "\"brokerProtocolVersion\":1,"
                + "\"manifestBindingSha256\":\"" + binding + "\""
                + "}],"
                + "\"certificateChain\":[]"
                + "}";

            var manifest = PackageManifestJson.ParseManifest(manifestJson);

            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.protectedPayloads, Has.Length.EqualTo(1));
            Assert.That(manifest.protectedPayloads[0].protectedAssetId, Is.EqualTo("protected-asset-123"));
            Assert.That(manifest.protectedPayloads[0].payloadAssetPaths, Is.EqualTo(new[] { "Assets/A.prefab", "Assets/B.png" }));
            Assert.That(manifest.protectedPayloads[0].requiresBrokeredMaterialization, Is.True);
            Assert.That(manifest.protectedPayloads[0].brokerProtocolVersion, Is.EqualTo(1));
            Assert.That(manifest.protectedPayloads[0].manifestBindingSha256, Is.EqualTo(binding));
        }
    }
}
