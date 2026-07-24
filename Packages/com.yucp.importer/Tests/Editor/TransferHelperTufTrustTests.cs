using System.Security.Cryptography;
using NUnit.Framework;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class TransferHelperTufTrustTests
    {
        [Test]
        public void LoadPinnedRootReturnsVerifiedMetadata()
        {
            byte[] root = TransferHelperTufTrust.LoadPinnedRoot();

            Assert.That(root, Is.Not.Null.And.Not.Empty);
            Assert.That(root[0], Is.EqualTo((byte)'{'));
        }

        [Test]
        public void VerifyPinnedRootRejectsModifiedMetadata()
        {
            byte[] root = TransferHelperTufTrust.LoadPinnedRoot();
            root[0] ^= 0x01;

            Assert.Throws<CryptographicException>(
                () => TransferHelperTufTrust.VerifyPinnedRoot(root));
        }
    }
}
