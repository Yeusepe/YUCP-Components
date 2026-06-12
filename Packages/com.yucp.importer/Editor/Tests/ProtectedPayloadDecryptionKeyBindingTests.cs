using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using YUCP.Importer.Editor.PackageManager;

namespace YUCP.Importer.Editor.Tests
{
    /// <summary>
    /// Adversarial confirmation of the core anti-bypass property:
    /// a client that has the encrypted protected-payload blob but NOT the exact
    /// content key the server issues cannot decrypt it — no matter how many
    /// client-side checks it edits out. The content key is the only secret, it
    /// never ships in the package, and the blob is authenticated (AES-256-CBC +
    /// HMAC-SHA256), so a wrong/guessed/forged key fails the MAC before any
    /// plaintext is produced.
    ///
    /// These tests reconstruct the exact YUCPBLOB wire format used by
    /// ProtectedPayloadInstallService and drive its private TryDecryptBlobToArchive
    /// via reflection, so they break if the format or the key-binding ever weakens.
    /// </summary>
    public class ProtectedPayloadDecryptionKeyBindingTests
    {
        private const string BlobMagic = "YUCPBLOB";
        private const byte BlobVersion = 1;
        private const string MacKeyPrefix = "YUCP|protected-payload|mac|";

        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "YUCP-PayloadKeyBindingTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempRoot))
                    Directory.Delete(_tempRoot, true);
            }
            catch
            {
            }
        }

        [Test]
        public void Decrypt_WithCorrectServerIssuedKey_Succeeds()
        {
            byte[] contentKey = NewKey(seed: 1);
            byte[] plaintext = Encoding.UTF8.GetBytes("PK fake-zip payload bytes for the protected asset");

            string blobPath = Path.Combine(_tempRoot, "payload.blob");
            File.WriteAllBytes(blobPath, BuildBlob(contentKey, plaintext, out _));

            string archivePath = Path.Combine(_tempRoot, "out-good.zip");
            bool ok = InvokeTryDecrypt(blobPath, NewDescriptor(), Convert.ToBase64String(contentKey), archivePath, out string error);

            Assert.That(ok, Is.True, $"Decryption with the correct content key should succeed. Error: {error}");
            Assert.That(File.Exists(archivePath), Is.True);
            Assert.That(File.ReadAllBytes(archivePath), Is.EqualTo(plaintext),
                "Decrypted output must match the original plaintext exactly.");
        }

        [Test]
        public void Decrypt_WithWrongKey_FailsAuthentication_AndWritesNoPlaintext()
        {
            byte[] realKey = NewKey(seed: 1);
            byte[] attackerKey = NewKey(seed: 2); // same length, wrong value — simulates a forged/guessed key
            byte[] plaintext = Encoding.UTF8.GetBytes("the secret asset bytes");

            string blobPath = Path.Combine(_tempRoot, "payload.blob");
            File.WriteAllBytes(blobPath, BuildBlob(realKey, plaintext, out _));

            string archivePath = Path.Combine(_tempRoot, "out-bad.zip");
            bool ok = InvokeTryDecrypt(blobPath, NewDescriptor(), Convert.ToBase64String(attackerKey), archivePath, out string error);

            Assert.That(ok, Is.False, "A wrong content key MUST NOT be able to decrypt the protected payload.");
            Assert.That(error, Does.Contain("authentication tag").IgnoreCase,
                "Wrong key must be rejected at the HMAC step, before any AES decryption is trusted.");
            // The MAC gate runs before decryption, so no plaintext archive should be produced.
            Assert.That(File.Exists(archivePath), Is.False,
                "No plaintext may be written to disk when authentication fails.");
        }

        [Test]
        public void Decrypt_WithTamperedCiphertext_FailsAuthentication()
        {
            byte[] contentKey = NewKey(seed: 1);
            byte[] plaintext = Encoding.UTF8.GetBytes("the secret asset bytes");

            byte[] blob = BuildBlob(contentKey, plaintext, out int ciphertextOffset);
            blob[ciphertextOffset] ^= 0xFF; // flip a ciphertext byte

            string blobPath = Path.Combine(_tempRoot, "payload-tampered.blob");
            File.WriteAllBytes(blobPath, blob);

            string archivePath = Path.Combine(_tempRoot, "out-tampered.zip");
            bool ok = InvokeTryDecrypt(blobPath, NewDescriptor(), Convert.ToBase64String(contentKey), archivePath, out string error);

            Assert.That(ok, Is.False, "Tampered ciphertext must be rejected even with the correct key.");
            Assert.That(error, Does.Contain("authentication tag").IgnoreCase);
            Assert.That(File.Exists(archivePath), Is.False);
        }

        // ── Helpers: reflectively drive the production decryptor ──────────────

        private static bool InvokeTryDecrypt(
            string blobDiskPath,
            ProtectedPayloadDescriptor descriptor,
            string contentKeyBase64,
            string archivePath,
            out string error)
        {
            Type serviceType = typeof(ProtectedPayloadDescriptor).Assembly
                .GetType("YUCP.Importer.Editor.PackageManager.ProtectedPayloadInstallService", throwOnError: false);
            Assert.That(serviceType, Is.Not.Null, "Could not locate ProtectedPayloadInstallService.");

            MethodInfo method = serviceType.GetMethod(
                "TryDecryptBlobToArchive",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null,
                "TryDecryptBlobToArchive not found — the protected-payload decrypt path may have been renamed.");

            object[] args = { blobDiskPath, descriptor, contentKeyBase64, archivePath, null };
            bool result = (bool)method.Invoke(null, args);
            error = args[4] as string;
            return result;
        }

        private static ProtectedPayloadDescriptor NewDescriptor()
        {
            // Leave cipher/archiveFormat/hash fields empty so the optional pre-checks are
            // skipped and the test isolates the key-binding + authentication property itself.
            return new ProtectedPayloadDescriptor
            {
                protectedAssetId = "asset-under-test",
                blobAssetPath = "Assets/whatever.blob",
            };
        }

        /// <summary>Reproduces the exact YUCPBLOB layout written by the signing/build pipeline.</summary>
        private static byte[] BuildBlob(byte[] contentKey, byte[] plaintext, out int ciphertextOffset)
        {
            byte[] magic = Encoding.ASCII.GetBytes(BlobMagic);
            byte[] iv = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(iv);

            byte[] ciphertext;
            using (var aes = Aes.Create())
            {
                aes.Key = contentKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using var encryptor = aes.CreateEncryptor();
                ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }

            // macInput = magic + version + IV + ciphertext  (everything except the tag)
            byte[] macInput = new byte[magic.Length + 1 + iv.Length + ciphertext.Length];
            int p = 0;
            Buffer.BlockCopy(magic, 0, macInput, p, magic.Length); p += magic.Length;
            macInput[p++] = BlobVersion;
            Buffer.BlockCopy(iv, 0, macInput, p, iv.Length); p += iv.Length;
            Buffer.BlockCopy(ciphertext, 0, macInput, p, ciphertext.Length);

            byte[] tag;
            using (var hmac = new HMACSHA256(DeriveMacKey(contentKey)))
                tag = hmac.ComputeHash(macInput);

            // wire layout: magic | version | IV | tag(32) | ciphertext
            byte[] blob = new byte[magic.Length + 1 + iv.Length + tag.Length + ciphertext.Length];
            int o = 0;
            Buffer.BlockCopy(magic, 0, blob, o, magic.Length); o += magic.Length;
            blob[o++] = BlobVersion;
            Buffer.BlockCopy(iv, 0, blob, o, iv.Length); o += iv.Length;
            Buffer.BlockCopy(tag, 0, blob, o, tag.Length); o += tag.Length;
            ciphertextOffset = o;
            Buffer.BlockCopy(ciphertext, 0, blob, o, ciphertext.Length);
            return blob;
        }

        private static byte[] DeriveMacKey(byte[] contentKey)
        {
            byte[] prefix = Encoding.UTF8.GetBytes(MacKeyPrefix);
            byte[] material = new byte[prefix.Length + contentKey.Length];
            Buffer.BlockCopy(prefix, 0, material, 0, prefix.Length);
            Buffer.BlockCopy(contentKey, 0, material, prefix.Length, contentKey.Length);
            using var sha = SHA256.Create();
            return sha.ComputeHash(material);
        }

        private static byte[] NewKey(byte seed)
        {
            byte[] key = new byte[32];
            for (int i = 0; i < key.Length; i++)
                key[i] = (byte)(seed * 37 + i);
            return key;
        }
    }
}
