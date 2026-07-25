using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class PackageContractGoldenVectorTests
    {
        [Serializable]
        private sealed class GoldenVectorDocument
        {
            public string keyIdHex;
            public string publicKeyHex;
            public int schemaVersion;
            public GoldenVector[] vectors;
        }

        [Serializable]
        private sealed class GoldenVector
        {
            public string coseSign1Hex;
            public string payloadHex;
            public string payloadSha256;
            public string purpose;
        }

        [Test]
        public void PackageHashFramingMatchesTypeScriptAndNative()
        {
            byte[] digest = PackageContractV2.HashFields(
                "yucp:chunk:v2",
                Encoding.UTF8.GetBytes("abc"));

            Assert.That(
                ToHex(digest),
                Is.EqualTo("55667f9928396d23fe784fdaee6e73c5317d775214d770878e7f7d623214db3a"));
            Assert.Throws<ArgumentException>(() =>
                PackageContractV2.HashFields("chunk", Array.Empty<byte>()));
        }

        [Test]
        public void EveryTypeScriptGoldenVectorVerifiesInUnity()
        {
            GoldenVectorDocument document = LoadVectors();
            Assert.That(document.schemaVersion, Is.EqualTo(1));
            byte[] keyId = ParseHex(document.keyIdHex);
            byte[] publicKey = ParseHex(document.publicKeyHex);

            foreach (GoldenVector vector in document.vectors)
            {
                byte[] payload = PackageContractV2.VerifySignedPayload(
                    ParseHex(vector.coseSign1Hex),
                    vector.purpose,
                    keyId,
                    publicKey);

                Assert.That(ToHex(payload), Is.EqualTo(vector.payloadHex), vector.purpose);
                Assert.That(Sha256Hex(payload), Is.EqualTo(vector.payloadSha256), vector.purpose);
            }
        }

        [Test]
        public void InstallSessionGoldenVectorMatchesRequestedInstall()
        {
            GoldenVectorDocument document = LoadVectors();
            GoldenVector vector = document.vectors.Single(
                item => item.purpose == PackageContractV2.InstallSessionPurpose);

            InstallSessionV2 session = InstallSessionV2Verifier.VerifyAndValidate(
                ParseHex(vector.coseSign1Hex),
                ParseHex(document.keyIdHex),
                ParseHex(document.publicKeyHex),
                ValidInstallContext());

            Assert.That(session.CreatorId, Is.EqualTo("creator-1"));
            Assert.That(session.BuyerId, Is.EqualTo("buyer-1"));
            Assert.That(session.ProductId, Is.EqualTo("product-1"));
            Assert.That(session.Version, Is.EqualTo("1.2.3"));
            Assert.That(session.Bootstrap.Select(item => item.Kind), Is.EquivalentTo(
                new[] { "release-descriptor", "delivery-binding" }));
        }

        [Test]
        public void InstallSessionRejectsPurposeAndBindingSubstitution()
        {
            GoldenVectorDocument document = LoadVectors();
            GoldenVector vector = document.vectors.Single(
                item => item.purpose == PackageContractV2.InstallSessionPurpose);
            byte[] coseSign1 = ParseHex(vector.coseSign1Hex);
            byte[] keyId = ParseHex(document.keyIdHex);
            byte[] publicKey = ParseHex(document.publicKeyHex);

            Assert.Throws<FormatException>(() => PackageContractV2.VerifySignedPayload(
                coseSign1,
                "delivery-grant-v2",
                keyId,
                publicKey));

            InstallSessionValidationContext wrongAlias = ValidInstallContext();
            wrongAlias.AliasId = "creator.other-product";
            Assert.Throws<FormatException>(() => InstallSessionV2Verifier.VerifyAndValidate(
                coseSign1,
                keyId,
                publicKey,
                wrongAlias));

            InstallSessionValidationContext wrongOrigin = ValidInstallContext();
            wrongOrigin.AllowedArtifactOrigins = new[] { "https://other.example.test" };
            Assert.Throws<FormatException>(() => InstallSessionV2Verifier.VerifyAndValidate(
                coseSign1,
                keyId,
                publicKey,
                wrongOrigin));
        }

        [Test]
        public void InstallSessionRejectsExpiryAndNoncanonicalCbor()
        {
            GoldenVectorDocument document = LoadVectors();
            GoldenVector vector = document.vectors.Single(
                item => item.purpose == PackageContractV2.InstallSessionPurpose);
            InstallSessionValidationContext expired = ValidInstallContext();
            expired.Now = 1800;

            Assert.Throws<FormatException>(() => InstallSessionV2Verifier.VerifyAndValidate(
                ParseHex(vector.coseSign1Hex),
                ParseHex(document.keyIdHex),
                ParseHex(document.publicKeyHex),
                expired));
            Assert.Throws<FormatException>(() =>
                PackageContractV2.AssertCanonicalPayload(new byte[] { 0x18, 0x01 }));
        }

        [Test]
        public void MaterializationReceiptGoldenVectorBindsExactServerRendition()
        {
            GoldenVectorDocument document = LoadVectors();
            GoldenVector vector = document.vectors.Single(
                item => item.purpose == PackageContractV2.MaterializationReceiptPurpose);

            MaterializationReceiptV2 receipt = MaterializationReceiptV2Verifier.VerifyAndValidate(
                ParseHex(vector.coseSign1Hex),
                ParseHex(document.keyIdHex),
                ParseHex(document.publicKeyHex),
                new MaterializationReceiptValidationContext
                {
                    Now = 1300,
                    ProductId = "product-1",
                    ReleaseRoot = RepeatByte(0x11),
                    RenditionSha256 = RepeatByte(0x77),
                    RenditionBytes = 2048,
                });

            Assert.That(receipt.ReceiptId, Is.EqualTo("receipt-1"));
            Assert.That(receipt.Rendition.StorageRole, Is.EqualTo("renditions"));
            Assert.That(receipt.Rendition.ProviderVersion, Is.EqualTo("01JVERSION"));
            Assert.That(receipt.OutputFiles.Select(file => file.NormalizedPath), Is.EqualTo(
                new[] { "Assets/Product/protected.png" }));

            var substitutedRendition = new MaterializationReceiptValidationContext
            {
                Now = 1300,
                ProductId = "product-1",
                ReleaseRoot = RepeatByte(0x11),
                RenditionSha256 = RepeatByte(0x66),
                RenditionBytes = 2048,
            };
            Assert.Throws<FormatException>(() => MaterializationReceiptV2Verifier.VerifyAndValidate(
                ParseHex(vector.coseSign1Hex),
                ParseHex(document.keyIdHex),
                ParseHex(document.publicKeyHex),
                substitutedRendition));
        }

        [Test]
        public void ResolvedInstallSessionIsReverifiedBeforeProjectMutation()
        {
            GoldenVectorDocument document = LoadVectors();
            GoldenVector vector = document.vectors.Single(
                item => item.purpose == PackageContractV2.InstallSessionPurpose);
            VerifiedInstallSessionV2 resolved = InstallSessionV2Verifier.Resolve(
                ParseHex(vector.coseSign1Hex),
                ParseHex(document.keyIdHex),
                ParseHex(document.publicKeyHex),
                ValidInstallContext());

            InstallSessionValidationContext currentContext = ValidInstallContext();
            currentContext.Now = 1250;
            InstallSessionV2 currentSession =
                resolved.ValidateBeforeProjectMutation(currentContext);
            Assert.That(currentSession.SessionId, Is.EqualTo(
                "018f8c03-3880-7d40-a8d5-b190a64141cc"));

            currentContext.AliasId = "creator.substituted-product";
            Assert.Throws<FormatException>(() =>
                resolved.ValidateBeforeProjectMutation(currentContext));

            InstallSessionValidationContext expiredContext = ValidInstallContext();
            expiredContext.Now = 1800;
            Assert.Throws<FormatException>(() =>
                resolved.ValidateBeforeProjectMutation(expiredContext));
        }

        private static InstallSessionValidationContext ValidInstallContext()
        {
            return new InstallSessionValidationContext
            {
                AliasId = "creator.avatar-tools",
                AllowedApiOrigins = new[] { "https://api.example.test" },
                AllowedArtifactOrigins = new[] { "https://delivery.example.test" },
                Audience = "yucp-unity-importer",
                BindingRoot = RepeatByte(0x22),
                DeviceKeyThumbprint = RepeatByte(0x33),
                Issuer = "https://api.example.test",
                Now = 1200,
                ReleaseRoot = RepeatByte(0x11),
            };
        }

        private static GoldenVectorDocument LoadVectors()
        {
            string packagePath = PackageInfo.FindForAssembly(
                typeof(PackageContractV2).Assembly).resolvedPath;
            string fixturePath = Path.Combine(
                packagePath,
                "Tests",
                "Editor",
                "Fixtures",
                "package-contracts-v2.json");
            string json = File.ReadAllText(fixturePath);
            GoldenVectorDocument document = JsonUtility.FromJson<GoldenVectorDocument>(json);
            Assert.That(document, Is.Not.Null);
            Assert.That(document.vectors, Is.Not.Null.And.Not.Empty);
            return document;
        }

        private static byte[] ParseHex(string hex)
        {
            if (hex == null || hex.Length % 2 != 0)
                throw new FormatException("Golden vector hex is invalid.");
            var bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }
            return bytes;
        }

        private static string ToHex(byte[] bytes)
        {
            return string.Concat(bytes.Select(value => value.ToString("x2")));
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(bytes));
            }
        }

        private static byte[] RepeatByte(byte value)
        {
            return Enumerable.Repeat(value, 32).ToArray();
        }
    }
}
