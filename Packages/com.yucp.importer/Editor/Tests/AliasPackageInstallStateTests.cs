using System.Collections.Generic;
using NUnit.Framework;
using YUCP.Importer.Editor.PackageManager;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public class AliasPackageInstallStateTests
    {
        [Test]
        public void ParsePackageJsonImportData_ExtractsAliasContractAndEmbeddedMetadata()
        {
            const string packageJson = "{"
                + "\"name\":\"com.creator.alias\","
                + "\"displayName\":\"Creator Alias\","
                + "\"version\":\"1.2.3\","
                + "\"description\":\"Alias package shell\","
                + "\"author\":{\"name\":\"Creator\"},"
                + "\"vpmDependencies\":{\"com.yucp.importer\":\">=0.4.0\"},"
                + "\"yucp\":{"
                + "\"kind\":\"alias-v1\","
                + "\"aliasId\":\"creator.alias\","
                + "\"installStrategy\":\"server-authorized\","
                + "\"importerPackage\":\"com.yucp.importer\","
                + "\"minImporterVersion\":\"0.4.0\","
                + "\"channel\":\"stable\","
                + "\"catalogProductIds\":[\"product-primary\",\"product-secondary\"],"
                + "\"resolvedRelease\":{\"id\":\"release-123\",\"version\":\"2025.04.29\",\"artifactId\":\"artifact-123\"},"
                + "\"resolvedArtifact\":{\"id\":\"artifact-123\",\"version\":\"2025.04.29\",\"sha256\":\"artifact-sha\"},"
                + "\"installPlan\":{\"id\":\"plan-123\",\"version\":\"1\",\"operation\":\"install\",\"managedPaths\":[\"Packages/com.creator.alias/package.json\"],\"sharedPaths\":[\"Packages/packages-lock.json\"]},"
                + "\"packageMetadata\":{"
                + "\"packageName\":\"Creator Alias\","
                + "\"version\":\"1.2.3\","
                + "\"author\":\"Creator\","
                + "\"description\":\"Package metadata from alias shell\","
                + "\"versionRule\":\"semver\","
                + "\"versionRuleName\":\"semver\","
                + "\"fileHashes\":[{\"path\":\"Packages/com.creator.alias/package.json\",\"hash\":\"expected-sha\"}]"
                + "}"
                + "}"
                + "}";

            PackageMetadataExtractor.PackageJsonImportData importData =
                PackageMetadataExtractor.ParsePackageJsonImportData(packageJson);
            PackageMetadata metadata =
                PackageMetadataExtractor.ParsePackageMetadataJson(importData.packageMetadataJson, null, null, null);

            Assert.That(importData, Is.Not.Null);
            Assert.That(importData.packageName, Is.EqualTo("com.creator.alias"));
            Assert.That(importData.displayName, Is.EqualTo("Creator Alias"));
            Assert.That(importData.dependencies["com.yucp.importer"], Is.EqualTo(">=0.4.0"));
            Assert.That(importData.aliasPackage, Is.Not.Null);
            Assert.That(importData.aliasPackage.aliasId, Is.EqualTo("creator.alias"));
            Assert.That(importData.aliasPackage.packageName, Is.EqualTo("com.creator.alias"));
            Assert.That(importData.aliasPackage.packageDisplayName, Is.EqualTo("Creator Alias"));
            Assert.That(importData.aliasPackage.packageVersion, Is.EqualTo("1.2.3"));
            Assert.That(importData.aliasPackage.resolvedRelease.releaseId, Is.EqualTo("release-123"));
            Assert.That(importData.aliasPackage.resolvedArtifact.artifactId, Is.EqualTo("artifact-123"));
            Assert.That(importData.aliasPackage.installPlan.managedPaths, Is.EqualTo(new[] { "Packages/com.creator.alias/package.json" }));
            Assert.That(importData.aliasPackage.installPlan.sharedPaths, Is.EqualTo(new[] { "Packages/packages-lock.json" }));

            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.packageName, Is.EqualTo("Creator Alias"));
            Assert.That(metadata.description, Is.EqualTo("Package metadata from alias shell"));
            Assert.That(metadata.fileHashes, Has.Count.EqualTo(1));
            Assert.That(metadata.fileHashes[0].path, Is.EqualTo("Packages/com.creator.alias/package.json"));
            Assert.That(metadata.fileHashes[0].hash, Is.EqualTo("expected-sha"));
        }

        [Test]
        public void MergePackageJsonImportData_PrefersPackageJsonYucpMetadataWhenPresent()
        {
            const string packageJson = "{"
                + "\"name\":\"com.creator.alias\","
                + "\"displayName\":\"Creator Alias\","
                + "\"version\":\"1.2.3\","
                + "\"yucp\":{"
                + "\"kind\":\"alias-v1\","
                + "\"aliasId\":\"creator.alias\","
                + "\"packageMetadata\":{"
                + "\"packageName\":\"Embedded Metadata\","
                + "\"description\":\"from package.json\""
                + "}"
                + "}"
                + "}";
            const string packageJsonYucp = "{"
                + "\"packageName\":\"Sidecar Metadata\","
                + "\"description\":\"from package.json.yucp\","
                + "\"fileHashes\":[{\"path\":\"Packages/com.creator.alias/package.json\",\"hash\":\"sidecar-sha\"}]"
                + "}";

            PackageMetadataExtractor.PackageJsonImportData importData =
                PackageMetadataExtractor.MergePackageJsonImportData(
                    PackageMetadataExtractor.ParsePackageJsonImportData(packageJson),
                    packageJsonYucp);
            PackageMetadata metadata =
                PackageMetadataExtractor.ParsePackageMetadataJson(importData.packageMetadataJson, null, null, null);

            Assert.That(importData, Is.Not.Null);
            Assert.That(importData.aliasPackage, Is.Not.Null);
            Assert.That(importData.packageMetadataJson, Does.Contain("Sidecar Metadata"));
            Assert.That(metadata.packageName, Is.EqualTo("Sidecar Metadata"));
            Assert.That(metadata.description, Is.EqualTo("from package.json.yucp"));
            Assert.That(metadata.fileHashes, Has.Count.EqualTo(1));
            Assert.That(metadata.fileHashes[0].hash, Is.EqualTo("sidecar-sha"));
        }

        [Test]
        public void BuildManifest_RoundTripsAliasInstallState()
        {
            var packageInfo = new InstalledPackageInfo
            {
                packageId = "creator.alias",
                packageName = "Creator Alias",
                version = "1.2.3",
                installedVersion = "1.2.3",
                installedDate = "2025-04-29T00:00:00.0000000Z",
                installedFiles = new List<string>
                {
                    "Packages/com.creator.alias/package.json",
                    "Packages/com.creator.alias/Runtime/Avatar.asset",
                },
                fileHashes = new List<PackageFileHashEntry>
                {
                    new PackageFileHashEntry
                    {
                        path = "Packages/com.creator.alias/package.json",
                        hash = "expected-package-json-sha",
                    },
                },
                aliasPackage = new AliasPackageContract
                {
                    kind = "alias-v1",
                    aliasId = "creator.alias",
                    packageName = "com.creator.alias",
                    packageDisplayName = "Creator Alias",
                    packageVersion = "1.2.3",
                    resolvedRelease = new AliasResolvedReleaseIdentity
                    {
                        releaseId = "release-123",
                        artifactId = "artifact-123",
                    },
                    resolvedArtifact = new AliasResolvedArtifactIdentity
                    {
                        artifactId = "artifact-123",
                        sha256 = "artifact-sha",
                    },
                    installPlan = new AliasInstallPlanMetadata
                    {
                        planId = "plan-123",
                        planVersion = "1",
                        operation = "install",
                        managedPaths = new List<string> { "Packages/com.creator.alias/package.json" },
                        generatedPaths = new List<string> { ".yucp-dvi/Importer/generated-note.txt" },
                        sharedPaths = new List<string> { "Packages/packages-lock.json" },
                    }
                }
            };

            const string manifestPath = ".yucp-dvi/Importer/InstallState/creator.alias.install-state.json";
            AliasPackageInstallStateManifest manifest =
                AliasPackageInstallStateStore.BuildManifest(packageInfo, manifestPath);
            string json = AliasPackageInstallStateStore.Serialize(manifest);
            AliasPackageInstallStateManifest roundTrip = AliasPackageInstallStateStore.Deserialize(json);

            Assert.That(roundTrip, Is.Not.Null);
            Assert.That(roundTrip.aliasId, Is.EqualTo("creator.alias"));
            Assert.That(roundTrip.aliasVersion, Is.EqualTo("1.2.3"));
            Assert.That(roundTrip.managedPaths, Is.EqualTo(new[] { "Packages/com.creator.alias/package.json" }));
            Assert.That(roundTrip.generatedPaths, Contains.Item(manifestPath));
            Assert.That(roundTrip.generatedPaths, Contains.Item(".yucp-dvi/Importer/generated-note.txt"));
            Assert.That(roundTrip.sharedPaths, Is.EqualTo(new[] { "Packages/packages-lock.json" }));
            Assert.That(roundTrip.fileHashes, Has.Some.Matches<InstallStateFileHashRecord>(entry =>
                entry.path == "Packages/com.creator.alias/package.json" &&
                entry.expectedSha256 == "expected-package-json-sha"));
        }
    }
}
