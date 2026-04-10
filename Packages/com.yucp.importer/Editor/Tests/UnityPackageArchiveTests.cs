using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using YUCP.Importer.Editor.PackageVerifier.Core;
using PackageVerifierCore = YUCP.Importer.Editor.PackageVerifier.Core.PackageVerifier;

namespace YUCP.Importer.Editor.Tests
{
    public class UnityPackageArchiveTests
    {
        [Test]
        public void ExtractSigningData_ReadsManifestAndSignature_FromUnityPackageArchive()
        {
            string packagePath = null;
            try
            {
                const string manifestJson = "{"
                    + "\"authorityId\":\"unitysign.yucp\","
                    + "\"keyId\":\"authority-key\","
                    + "\"publisherId\":\"publisher-123\","
                    + "\"packageId\":\"package-123\","
                    + "\"version\":\"1.0.0\","
                    + "\"archiveSha256\":\"deadbeef\","
                    + "\"fileHashes\":{},"
                    + "\"certificateChain\":[]"
                    + "}";
                const string signatureJson = "{"
                    + "\"algorithm\":\"ed25519\","
                    + "\"keyId\":\"authority-key\","
                    + "\"signature\":\"c2lnbmF0dXJl\","
                    + "\"certificateIndex\":0"
                    + "}";

                packagePath = CreateUnityPackage(new Dictionary<string, byte[]>
                {
                    ["asset-manifest/pathname"] = Encoding.UTF8.GetBytes("Assets/_Signing/PackageManifest.json"),
                    ["asset-manifest/asset"] = Encoding.UTF8.GetBytes(manifestJson),
                    ["asset-signature/pathname"] = Encoding.UTF8.GetBytes("Assets/_Signing/PackageManifest.sig"),
                    ["asset-signature/asset"] = Encoding.UTF8.GetBytes(signatureJson),
                });

                var result = ManifestExtractor.ExtractSigningData(packagePath);

                Assert.That(result.success, Is.True, result.error);
                Assert.That(result.manifest, Is.Not.Null);
                Assert.That(result.signature, Is.Not.Null);
                Assert.That(result.manifest.packageId, Is.EqualTo("package-123"));
                Assert.That(result.signature.keyId, Is.EqualTo("authority-key"));
            }
            finally
            {
                DeleteIfPresent(packagePath);
            }
        }

        [Test]
        public void ComputePackageHashExcludingSigningData_IgnoresSigningFiles()
        {
            string packagePath = null;
            try
            {
                byte[] helloBytes = Encoding.UTF8.GetBytes("hello world");
                byte[] bytesAsset = { 1, 2, 3, 4, 5 };
                packagePath = CreateUnityPackage(new Dictionary<string, byte[]>
                {
                    ["asset-a/pathname"] = Encoding.UTF8.GetBytes("Assets/Zeta.txt"),
                    ["asset-a/asset"] = helloBytes,
                    ["asset-b/pathname"] = Encoding.UTF8.GetBytes("Assets/_Signing/PackageManifest.json"),
                    ["asset-b/asset"] = Encoding.UTF8.GetBytes("{\"ignored\":true}"),
                    ["asset-c/pathname"] = Encoding.UTF8.GetBytes("Assets/Alpha.bytes"),
                    ["asset-c/asset"] = bytesAsset,
                });

                MethodInfo method = typeof(PackageVerifierCore).GetMethod(
                    "ComputePackageHashExcludingSigningData",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null);

                string actualHash = method.Invoke(null, new object[] { packagePath }) as string;
                string expectedHash = ComputeExpectedCanonicalHash(
                    ("Assets/Alpha.bytes", bytesAsset),
                    ("Assets/Zeta.txt", helloBytes));

                Assert.That(actualHash, Is.EqualTo(expectedHash));
            }
            finally
            {
                DeleteIfPresent(packagePath);
            }
        }

        private static string CreateUnityPackage(IReadOnlyDictionary<string, byte[]> entries)
        {
            string packagePath = Path.Combine(Path.GetTempPath(), $"yucp-test-{Guid.NewGuid():N}.unitypackage");
            using var fileStream = File.Create(packagePath);
            using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal, leaveOpen: false);
            foreach (var entry in entries)
            {
                WriteTarEntry(gzipStream, entry.Key.Replace('\\', '/'), entry.Value ?? Array.Empty<byte>());
            }

            gzipStream.Write(new byte[1024], 0, 1024);
            return packagePath;
        }

        private static void WriteTarEntry(Stream stream, string entryName, byte[] data)
        {
            byte[] header = new byte[512];
            WriteAscii(header, 0, 100, entryName);
            WriteOctal(header, 100, 8, 0644);
            WriteOctal(header, 108, 8, 0);
            WriteOctal(header, 116, 8, 0);
            WriteOctal(header, 124, 12, data.LongLength);
            WriteOctal(header, 136, 12, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            for (int i = 148; i < 156; i++)
            {
                header[i] = 0x20;
            }
            header[156] = (byte)'0';
            WriteAscii(header, 257, 6, "ustar");
            WriteAscii(header, 263, 2, "00");

            int checksum = 0;
            for (int i = 0; i < header.Length; i++)
            {
                checksum += header[i];
            }

            string checksumText = Convert.ToString(checksum, 8).PadLeft(6, '0');
            WriteAscii(header, 148, 6, checksumText);
            header[154] = 0;
            header[155] = 0x20;

            stream.Write(header, 0, header.Length);
            if (data.Length > 0)
            {
                stream.Write(data, 0, data.Length);
            }

            int padding = (int)((512 - (data.Length % 512)) % 512);
            if (padding > 0)
            {
                stream.Write(new byte[padding], 0, padding);
            }
        }

        private static void WriteAscii(byte[] buffer, int offset, int length, string value)
        {
            string normalized = value ?? string.Empty;
            byte[] bytes = Encoding.ASCII.GetBytes(normalized);
            Array.Copy(bytes, 0, buffer, offset, Math.Min(length, bytes.Length));
        }

        private static void WriteOctal(byte[] buffer, int offset, int length, long value)
        {
            string octal = Convert.ToString(value, 8);
            string padded = octal.PadLeft(length - 1, '0');
            WriteAscii(buffer, offset, length - 1, padded);
            buffer[offset + length - 1] = 0;
        }

        private static string ComputeExpectedCanonicalHash(params (string pathname, byte[] data)[] entries)
        {
            Array.Sort(entries, (left, right) => string.CompareOrdinal(left.pathname, right.pathname));
            using var sha256 = SHA256.Create();
            foreach (var entry in entries)
            {
                byte[] pathBytes = Encoding.UTF8.GetBytes(entry.pathname);
                sha256.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);
                byte[] separator = { 0x00 };
                sha256.TransformBlock(separator, 0, 1, null, 0);
                sha256.TransformBlock(entry.data, 0, entry.data.Length, null, 0);
            }

            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant();
        }

        private static void DeleteIfPresent(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
