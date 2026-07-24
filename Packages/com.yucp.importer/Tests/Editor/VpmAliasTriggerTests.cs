using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class VpmAliasTriggerTests
    {
        [Test]
        public void OfficialVpmAliasIsRegisteredAndEntersTheAuthorizedFlow()
        {
            string packageId = Environment.GetEnvironmentVariable(
                "YUCP_VPM_ALIAS_TRIGGER_PACKAGE_ID");
            string expectedAliasId = Environment.GetEnvironmentVariable(
                "YUCP_VPM_ALIAS_TRIGGER_ALIAS_ID");
            if (string.IsNullOrWhiteSpace(packageId) ||
                string.IsNullOrWhiteSpace(expectedAliasId))
            {
                Assert.Ignore("The focused VPM alias trigger environment is not active.");
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string packageJsonPath = Path.Combine(
                projectRoot,
                "Packages",
                packageId,
                "package.json");
            Assert.That(File.Exists(packageJsonPath), Is.True);
            string packageJson = File.ReadAllText(packageJsonPath);

            bool built = AliasPackageAutoInstaller.TryBuildAliasPackageMetadata(
                packageId,
                packageJson,
                out PackageMetadata metadata,
                out string error);

            Assert.That(built, Is.True, error);
            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.aliasPackage.aliasId, Is.EqualTo(expectedAliasId));
            Assert.That(
                metadata.aliasPackage.installStrategy,
                Is.EqualTo(UpdateDeliveryService.ServerAuthorizedInstallStrategy));
            Assert.That(
                AliasPackageAutoInstaller.AnyServerAuthorizedAliasPackageRegistered(),
                Is.True);
        }
    }
}
