using System.Reflection;
using NUnit.Framework;
using YUCP.Importer.Editor.PackageVerifier.Data;

namespace YUCP.Importer.Editor.Tests
{
    public class PackageManifestShapeTests
    {
        [Test]
        public void PackageManifest_IncludesMarketplaceProductIds_ForSignatureCanonicalization()
        {
            Assert.That(typeof(PackageManifest).GetField("gumroadProductId", BindingFlags.Public | BindingFlags.Instance), Is.Not.Null);
            Assert.That(typeof(PackageManifest).GetField("jinxxyProductId", BindingFlags.Public | BindingFlags.Instance), Is.Not.Null);
        }
    }
}
