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
        public void SignedContractsRejectPurposeSubstitutionAndNoncanonicalPayloads()
        {
            GoldenVectorDocument document = LoadVectors();
            GoldenVector vector = document.vectors.First();

            Assert.Throws<FormatException>(() => PackageContractV2.VerifySignedPayload(
                ParseHex(vector.coseSign1Hex),
                "delivery-grant-v2",
                ParseHex(document.keyIdHex),
                ParseHex(document.publicKeyHex)));
            Assert.Throws<FormatException>(() =>
                PackageContractV2.AssertCanonicalPayload(new byte[] { 0x18, 0x01 }));
        }

        [Test]
        public void UntrustedCborUsesTheStableFormatExceptionBoundary()
        {
            Assert.Throws<FormatException>(() =>
                PackageContractV2.VerifySignedPayload(
                    new byte[] { 0x01 },
                    PackageContractV2.MaterializationReceiptPurpose,
                    new byte[] { 0x01 },
                    new byte[32]));

            MethodInfo parse = typeof(MaterializationReceiptV2Verifier).GetMethod(
                "Parse",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(parse, Is.Not.Null);
            TargetInvocationException invocation =
                Assert.Throws<TargetInvocationException>(() =>
                    parse.Invoke(null, new object[] { new byte[] { 0x01 } }));
            Assert.That(invocation.InnerException, Is.TypeOf<FormatException>());
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
