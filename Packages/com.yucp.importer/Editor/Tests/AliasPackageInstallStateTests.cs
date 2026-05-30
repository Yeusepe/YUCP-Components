using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
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
        public void AliasPackageAutoInstaller_BuildsMetadataForServerAuthorizedShim()
        {
            const string packageJson = "{"
                + "\"name\":\"com.creator.alias\","
                + "\"displayName\":\"Creator Alias\","
                + "\"version\":\"1.2.3\","
                + "\"description\":\"Alias package shell\","
                + "\"vpmDependencies\":{\"com.yucp.importer\":\">=0.4.0\"},"
                + "\"yucp\":{"
                + "\"kind\":\"alias-v1\","
                + "\"aliasId\":\"creator.alias\","
                + "\"installStrategy\":\"server-authorized\","
                + "\"importerPackage\":\"com.yucp.importer\","
                + "\"minImporterVersion\":\"0.4.0\","
                + "\"channel\":\"stable\","
                + "\"catalogProductIds\":[\"product-primary\"]"
                + "}"
                + "}";

            bool built = AliasPackageAutoInstaller.TryBuildAliasPackageMetadata(
                "com.creator.alias",
                packageJson,
                out PackageMetadata metadata,
                out string error);

            Assert.That(built, Is.True, error);
            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.packageName, Is.EqualTo("Creator Alias"));
            Assert.That(metadata.version, Is.EqualTo("1.2.3"));
            Assert.That(metadata.dependencies["com.yucp.importer"], Is.EqualTo(">=0.4.0"));
            Assert.That(metadata.aliasPackage, Is.Not.Null);
            Assert.That(metadata.aliasPackage.packageName, Is.EqualTo("com.creator.alias"));
            Assert.That(metadata.aliasPackage.packageVersion, Is.EqualTo("1.2.3"));
            Assert.That(metadata.aliasPackage.catalogProductIds, Is.EqualTo(new[] { "product-primary" }));
            Assert.That(AliasPackageAutoInstaller.BuildSessionKey(metadata), Does.Contain("com.creator.alias@1.2.3"));
        }

        [Test]
        public void AliasPackageAutoInstaller_PrefersAliasPackageDisplayNameOverSanitizedUnityDisplayName()
        {
            const string packageJson = "{"
                + "\"name\":\"com.creator.alias\","
                + "\"displayName\":\"Song Thing - Your Spotify library within VRChat - VRCFury Ready\","
                + "\"version\":\"1.2.3\","
                + "\"vpmDependencies\":{\"com.yucp.importer\":\">=0.4.0\"},"
                + "\"yucp\":{"
                + "\"kind\":\"alias-v1\","
                + "\"aliasId\":\"creator.alias\","
                + "\"packageDisplayName\":\"Song Thing | Your Spotify library within VRChat | VRCFury Ready\","
                + "\"installStrategy\":\"server-authorized\","
                + "\"importerPackage\":\"com.yucp.importer\""
                + "}"
                + "}";

            bool built = AliasPackageAutoInstaller.TryBuildAliasPackageMetadata(
                "com.creator.alias",
                packageJson,
                out PackageMetadata metadata,
                out string error);

            Assert.That(built, Is.True, error);
            Assert.That(metadata.packageName, Is.EqualTo("Song Thing | Your Spotify library within VRChat | VRCFury Ready"));
            Assert.That(metadata.aliasPackage.packageDisplayName, Is.EqualTo("Song Thing | Your Spotify library within VRChat | VRCFury Ready"));
        }

        [Test]
        public void AliasPackageAutoInstaller_DiscoversEmbeddedAliasPackageFolders()
        {
            string packagesRoot = Path.Combine(Path.GetTempPath(), "YUCP-AliasScan-" + System.Guid.NewGuid().ToString("N"));
            string aliasDirectory = Path.Combine(packagesRoot, "com.creator.alias");
            Directory.CreateDirectory(aliasDirectory);
            try
            {
                File.WriteAllText(Path.Combine(aliasDirectory, "package.json"), "{"
                    + "\"name\":\"com.creator.alias\","
                    + "\"displayName\":\"Creator Alias\","
                    + "\"version\":\"1.2.3\","
                    + "\"yucp\":{"
                    + "\"kind\":\"alias-v1\","
                    + "\"aliasId\":\"creator.alias\","
                    + "\"installStrategy\":\"server-authorized\""
                    + "}"
                    + "}");

                string[] packageJsonPaths = AliasPackageAutoInstaller.FindEmbeddedPackageJsonPaths(
                    packagesRoot,
                    new HashSet<string>(System.StringComparer.OrdinalIgnoreCase));

                Assert.That(packageJsonPaths, Is.EqualTo(new[] { Path.Combine(aliasDirectory, "package.json") }));
            }
            finally
            {
                Directory.Delete(packagesRoot, true);
            }
        }

        [Test]
        public void AliasPackageAutoInstaller_RejectsNonServerAuthorizedPackages()
        {
            const string packageJson = "{"
                + "\"name\":\"com.creator.alias\","
                + "\"displayName\":\"Creator Alias\","
                + "\"version\":\"1.2.3\","
                + "\"yucp\":{"
                + "\"kind\":\"alias-v1\","
                + "\"aliasId\":\"creator.alias\","
                + "\"installStrategy\":\"direct\""
                + "}"
                + "}";

            bool built = AliasPackageAutoInstaller.TryBuildAliasPackageMetadata(
                "com.creator.alias",
                packageJson,
                out PackageMetadata metadata,
                out string error);

            Assert.That(built, Is.False);
            Assert.That(metadata, Is.Null);
            Assert.That(error, Does.Contain("server-authorized"));
        }

        [Test]
        public void AliasPackageAutoInstaller_BuildsClearInstallPrompt()
        {
            var metadata = new PackageMetadata("Song Thing | Your Spotify library within VRChat | VRCFury Ready");

            string message = AliasPackageAutoInstaller.BuildInstallPromptMessage(metadata);

            Assert.That(message, Does.Contain("Ready to finish installing"));
            Assert.That(message, Does.Contain("Verify access"));
            Assert.That(message, Does.Not.Contain("shim"));
            Assert.That(message, Does.Not.Contain("YUCP needs"));
        }

        [Test]
        public void AliasPackageAutoInstaller_ClearsAttemptGateAfterFailedVerification()
        {
            var metadata = new PackageMetadata("Song Thing")
            {
                version = "1.2.3",
                aliasPackage = new AliasPackageContract
                {
                    kind = "alias-v1",
                    aliasId = "song-thing",
                    packageName = "com.yucp.songthing",
                    packageVersion = "1.2.3",
                    installStrategy = UpdateDeliveryService.ServerAuthorizedInstallStrategy,
                },
            };

            string sessionKey = AliasPackageAutoInstaller.BuildSessionKey(metadata);
            SessionState.SetBool(sessionKey, true);

            MethodInfo clearAttempt = typeof(AliasPackageAutoInstaller).GetMethod(
                "ClearInstallAttemptForRetry",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(clearAttempt, Is.Not.Null, "Failed verification must clear the alias attempt gate so removing and re-adding the shim prompts again.");
            clearAttempt.Invoke(null, new object[] { metadata });

            Assert.That(SessionState.GetBool(sessionKey, false), Is.False);
        }

        [Test]
        public void AliasPackageAutoInstaller_SessionKeyChangesWhenPackageFolderIsRecreated()
        {
            string packagesRoot = Path.Combine(Path.GetTempPath(), "YUCP-AliasAttempt-" + System.Guid.NewGuid().ToString("N"));
            string aliasDirectory = Path.Combine(packagesRoot, "com.yucp.songthing");
            string packageJsonPath = Path.Combine(aliasDirectory, "package.json");

            try
            {
                Directory.CreateDirectory(aliasDirectory);
                File.WriteAllText(packageJsonPath, "{}");

                var metadata = new PackageMetadata("Song Thing")
                {
                    version = "1.0.6",
                    aliasPackage = new AliasPackageContract
                    {
                        kind = "alias-v1",
                        aliasId = "song-thing",
                        packageName = "com.yucp.songthing",
                        packageVersion = "1.0.6",
                        installStrategy = UpdateDeliveryService.ServerAuthorizedInstallStrategy,
                    },
                };

                DateTime firstTimestamp = new DateTime(2026, 5, 2, 18, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(packageJsonPath, firstTimestamp);
                Directory.SetLastWriteTimeUtc(aliasDirectory, firstTimestamp);
                MethodInfo buildSessionKey = typeof(AliasPackageAutoInstaller).GetMethod(
                    "BuildSessionKey",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(PackageMetadata), typeof(string) },
                    null);

                Assert.That(buildSessionKey, Is.Not.Null);
                string firstSessionKey = buildSessionKey.Invoke(null, new object[] { metadata, packageJsonPath }) as string;

                DateTime secondTimestamp = firstTimestamp.AddMinutes(5);
                File.SetLastWriteTimeUtc(packageJsonPath, secondTimestamp);
                Directory.SetLastWriteTimeUtc(aliasDirectory, secondTimestamp);
                string secondSessionKey = buildSessionKey.Invoke(null, new object[] { metadata, packageJsonPath }) as string;

                Assert.That(secondSessionKey, Is.Not.EqualTo(firstSessionKey));
            }
            finally
            {
                if (Directory.Exists(packagesRoot))
                {
                    Directory.Delete(packagesRoot, true);
                }
            }
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



