using NUnit.Framework;
using YUCP.Importer.Editor.PackageManager;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class VpmAliasTriggerTests
    {
        [Test]
        public void OfficialVpmAliasContractEntersTheAuthorizedFlow()
        {
            const string packageId = "com.yucp.jammr.alias";
            const string expectedAliasId = "jammr";
            const string packageJson = "{\"name\":\"com.yucp.jammr.alias\"," +
                "\"version\":\"1.0.0\",\"displayName\":\"JAMMR\"," +
                "\"yucp\":{\"kind\":\"alias-v1\",\"aliasId\":\"jammr\"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"," +
                "\"catalogProductIds\":[\"jinxxy-jammr\",\"gumroad-jammr\"]}}";

            bool built = AliasPackageDiscovery.TryBuildMetadata(
                packageId,
                packageJson,
                out PackageMetadata metadata,
                out string error);

            Assert.That(built, Is.True, error);
            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.aliasPackage.aliasId, Is.EqualTo(expectedAliasId));
            Assert.That(
                metadata.aliasPackage.installStrategy,
                Is.EqualTo(AliasPackageDiscovery.ServerAuthorizedInstallStrategy));
        }

        [Test]
        public void LegacyInstallPlanMetadataIsRejected()
        {
            const string packageJson = "{\"name\":\"com.example.alias\",\"version\":\"1.0.0\"," +
                "\"displayName\":\"Alias\",\"yucp\":{\"kind\":\"alias-v1\"," +
                "\"aliasId\":\"example\",\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\",\"catalogProductIds\":[\"catalog-1\"]," +
                "\"installPlan\":{\"id\":\"unsigned\"}}}";

            bool built = AliasPackageDiscovery.TryBuildMetadata(
                "com.example.alias",
                packageJson,
                out _,
                out string error);

            Assert.That(built, Is.False);
            Assert.That(error, Does.Contain("removed delivery field"));
        }
    }
}
