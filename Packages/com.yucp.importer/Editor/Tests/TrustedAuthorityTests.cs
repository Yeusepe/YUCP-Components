using NUnit.Framework;
using YUCP.Importer.Editor.PackageVerifier;

namespace YUCP.Importer.Editor.Tests
{
    public class TrustedAuthorityTests
    {
        [Test]
        public void GetPublicKey_ReturnsBuiltInRootKey_ForYucpRootAlias()
        {
            byte[] canonicalRoot = TrustedAuthority.GetPublicKey("yucp-root-2025");
            byte[] legacyRootAlias = TrustedAuthority.GetPublicKey("yucp-root");

            Assert.That(canonicalRoot, Is.Not.Null);
            Assert.That(legacyRootAlias, Is.Not.Null);
            CollectionAssert.AreEqual(canonicalRoot, legacyRootAlias);
        }
    }
}
