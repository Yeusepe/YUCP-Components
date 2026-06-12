using System;
using System.Collections.Generic;
using NUnit.Framework;
using YUCP.Importer.Editor.PackageManager;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public class AliasRemovalAndMetadataTests
    {
        [Test]
        public void ResolvePackageIdsToReconcileOnRemoval_SkipsRetainedAndUnmanaged()
        {
            var managed = new Dictionary<string, InstalledPackageInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["com.yucp.alpha"] = new InstalledPackageInfo { packageId = "com.yucp.alpha" },
                ["com.yucp.beta"] = new InstalledPackageInfo { packageId = "com.yucp.beta" },
            };

            // alpha: genuinely removed -> reconcile.
            // beta: removed but also re-added (an update) -> skip.
            // gamma: removed but not YUCP-managed -> skip.
            // duplicate alpha entry must not produce a duplicate id.
            var removed = new[] { "com.yucp.alpha", "com.yucp.alpha", "com.yucp.beta", "com.yucp.gamma" };
            var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "com.yucp.beta" };

            List<string> result = AliasPackageAutoInstaller.ResolvePackageIdsToReconcileOnRemoval(
                removed,
                retained,
                name => managed.TryGetValue(name, out InstalledPackageInfo info) ? info : null);

            Assert.That(result, Is.EquivalentTo(new[] { "com.yucp.alpha" }));
        }

        [Test]
        public void ResolvePackageIdsToReconcileOnRemoval_FallsBackToRemovedNameWhenPackageIdEmpty()
        {
            var managed = new Dictionary<string, InstalledPackageInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["com.yucp.delta"] = new InstalledPackageInfo { packageId = "" },
            };

            List<string> result = AliasPackageAutoInstaller.ResolvePackageIdsToReconcileOnRemoval(
                new[] { "com.yucp.delta" },
                null,
                name => managed.TryGetValue(name, out InstalledPackageInfo info) ? info : null);

            Assert.That(result, Is.EquivalentTo(new[] { "com.yucp.delta" }));
        }

        [Test]
        public void BuildPreviewMetadataFromPlan_MapsServerDetailsForDisplay()
        {
            var requested = new AliasPackageContract
            {
                kind = "alias-v1",
                aliasId = "alias-1",
                packageName = "com.yucp.coolpack",
                installStrategy = UpdateDeliveryService.ServerAuthorizedInstallStrategy,
            };

            var plan = new UpdateDeliveryService.AliasInstallPlan
            {
                kind = "alias-install-plan-v1",
                title = "Cool Pack",
                creatorName = "Studio X",
                packages = new[]
                {
                    new UpdateDeliveryService.AliasInstallPlanPackage
                    {
                        packageId = "com.yucp.coolpack",
                        displayName = "Cool Pack",
                        version = "1.2.3",
                        aliasContract = new AliasPackageContract
                        {
                            kind = "alias-v1",
                            aliasId = "alias-1",
                            packageName = "com.yucp.coolpack",
                            installStrategy = UpdateDeliveryService.ServerAuthorizedInstallStrategy,
                        },
                    },
                },
            };

            PackageMetadata metadata = UpdateDeliveryService.BuildPreviewMetadataFromPlan(plan, requested);

            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.packageName, Is.EqualTo("Cool Pack"));
            Assert.That(metadata.version, Is.EqualTo("1.2.3"));
            Assert.That(metadata.author, Is.EqualTo("Studio X"));
            Assert.That(metadata.tagline, Is.EqualTo("Cool Pack"));
            Assert.That(metadata.aliasPackage, Is.Not.Null);
            Assert.That(metadata.aliasPackage.packageName, Is.EqualTo("com.yucp.coolpack"));
            Assert.That(metadata.aliasPackage.kind, Is.EqualTo("alias-v1"));
        }

        [Test]
        public void BuildPreviewMetadataFromPlan_ReturnsNullWhenPlanHasNoPackages()
        {
            var plan = new UpdateDeliveryService.AliasInstallPlan
            {
                kind = "alias-install-plan-v1",
                packages = Array.Empty<UpdateDeliveryService.AliasInstallPlanPackage>(),
            };

            Assert.That(UpdateDeliveryService.BuildPreviewMetadataFromPlan(plan, null), Is.Null);
        }

        [Test]
        public void BuildEnrichedInstallMessage_IncludesFetchedDetails()
        {
            var preview = new PackageMetadata("Cool Pack")
            {
                version = "1.2.3",
                author = "Studio X",
                tagline = "A very cool pack.",
            };

            string message = AliasPackageAutoInstaller.BuildEnrichedInstallMessage(preview, null);

            Assert.That(message, Does.Contain("Cool Pack"));
            Assert.That(message, Does.Contain("1.2.3"));
            Assert.That(message, Does.Contain("Studio X"));
            Assert.That(message, Does.Contain("A very cool pack."));
        }
    }
}
