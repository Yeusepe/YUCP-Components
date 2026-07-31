using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using YUCP.Importer.Editor.PackageManager;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class VpmAliasTriggerTests
    {
        [Test]
        public void PackageIdentityAloneEntersTheAuthorizedFlow()
        {
            const string packageId = "com.yucp.jammr";
            const string packageJson = "{\"name\":\"com.yucp.jammr.alias\"," +
                "\"version\":\"1.0.0\",\"displayName\":\"JAMMR\"," +
                "\"yucp\":{\"kind\":\"alias-v1\"," +
                "\"aliasId\":\"com.yucp.jammr\"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"}}";

            bool built = AliasPackageDiscovery.TryBuildMetadata(
                packageId,
                packageJson,
                out PackageMetadata metadata,
                out string error);

            Assert.That(built, Is.True, error);
            Assert.That(metadata.aliasPackage.aliasId, Is.EqualTo(packageId));
        }

        [Test]
        public void AliasMediaUsesTheSameBytesForDigestAndDecode()
        {
            string source = File.ReadAllText(
                Path.Combine(
                    Path.GetFullPath(
                        Path.Combine(Application.dataPath, "..")),
                    "Packages",
                    "com.yucp.importer",
                    "Editor",
                    "PackageManager",
                    "Core",
                    "AliasPackageMediaLoader.cs"));

            Assert.That(
                source.Split(new[] { "File.ReadAllBytes(path)" },
                    StringSplitOptions.None).Length - 1,
                Is.EqualTo(1));
            Assert.That(
                source,
                Does.Not.Contain("File.OpenRead(path)"));
        }

        [Test]
        public void PackageMetadataDependenciesSurviveUnitySerialization()
        {
            var metadata = new PackageMetadata("JAMMR");
            metadata.dependencies["com.vrchat.avatars"] = ">=3.7.0";
            metadata.dependencies["com.yucp.importer"] = ">=0.1.54";

            string json = JsonUtility.ToJson(metadata);
            PackageMetadata restored =
                JsonUtility.FromJson<PackageMetadata>(json);

            Assert.That(
                restored.dependencies,
                Is.EqualTo(metadata.dependencies));
        }

        [Test]
        public void InstallEntryPointFiltersPendingLifecycleOperations()
        {
            string source = File.ReadAllText(
                Path.Combine(
                    Path.GetFullPath(
                        Path.Combine(Application.dataPath, "..")),
                    "Packages",
                    "com.yucp.importer",
                    "Editor",
                    "PackageManager",
                    "Core",
                    "PackageLifecycleCoordinator.cs"));
            int start = source.IndexOf(
                "TryInstallAsync(",
                StringComparison.Ordinal);
            int end = source.IndexOf(
                "TryManageInstalledAsync(",
                start,
                StringComparison.Ordinal);
            string installEntryPoint = source.Substring(
                start,
                end - start);

            Assert.That(
                installEntryPoint,
                Does.Contain("GetPendingOperation("));
            Assert.That(
                installEntryPoint,
                Does.Contain("PendingOperationMatches("));
        }

        [Test]
        public void LifecycleProgressUsesOneWindowLifetimeGuard()
        {
            MethodInfo method = typeof(PackageManagerWindow).GetMethod(
                "CreateLifecycleProgressReporter",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
        }

        [Test]
        public void BootstrapVersionIsNotPresentedAsTheProductVersion()
        {
            const string packageJson =
                "{\"name\":\"com.yucp.jammr.alias\"," +
                "\"version\":\"1.700000.125\",\"displayName\":\"JAMMR\"," +
                "\"yucp\":{\"kind\":\"alias-v1\"," +
                "\"aliasId\":\"com.yucp.jammr\"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"," +
                "\"packageMetadata\":{\"packageName\":\"JAMMR\"," +
                "\"author\":\"Mapache\"}}}";

            bool built = AliasPackageDiscovery.TryBuildMetadata(
                "com.yucp.jammr.alias",
                packageJson,
                out PackageMetadata metadata,
                out string error);

            Assert.That(built, Is.True, error);
            Assert.That(metadata.version, Is.Empty);
            Assert.That(metadata.aliasPackage.packageVersion, Is.EqualTo("1.700000.125"));
        }

        [Test]
        public void OfficialVpmAliasContractEntersTheAuthorizedFlow()
        {
            const string packageId = "com.yucp.jammr.alias";
            const string expectedAliasId = "jammr";
            const string packageJson = "{\"name\":\"com.yucp.jammr.alias\"," +
                "\"version\":\"1.0.0\",\"displayName\":\"JAMMR\"," +
                "\"yucp\":{\"kind\":\"alias-v1\",\"aliasId\":\"jammr\"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"}}";

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
        public void AliasUsesEmbeddedFriendlyProductMetadata()
        {
            const string packageJson =
                "{\"name\":\"com.yucp.alias.c6396665\"," +
                "\"version\":\"1.0.0\"," +
                "\"displayName\":\"YUCP Product Bootstrap C6396665\"," +
                "\"description\":\"Public bootstrap.\"," +
                "\"author\":{\"name\":\"YUCP Club\"}," +
                "\"yucp\":{\"kind\":\"alias-v1\"," +
                "\"aliasId\":\"jammr\"," +
                "\"packageDisplayName\":\"JAMMR\"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"," +
                "\"packageMetadata\":{\"packageName\":\"JAMMR\"," +
                "\"author\":\"Druffle\"," +
                "\"description\":\"Create and join music sessions.\"," +
                "\"tagline\":\"Music together in VR.\"}}}";

            bool built = AliasPackageDiscovery.TryBuildMetadata(
                "com.yucp.alias.c6396665",
                packageJson,
                out PackageMetadata metadata,
                out string error);

            Assert.That(built, Is.True, error);
            Assert.That(metadata.packageName, Is.EqualTo("JAMMR"));
            Assert.That(metadata.version, Is.Empty);
            Assert.That(metadata.author, Is.EqualTo("Druffle"));
            Assert.That(
                metadata.description,
                Is.EqualTo("Create and join music sessions."));
            Assert.That(metadata.tagline, Is.EqualTo("Music together in VR."));
        }

        [Test]
        public void AliasLoadsOnlyDigestBoundLocalMedia()
        {
            string packageRoot = Path.Combine(
                Path.GetTempPath(),
                "yucp-alias-media-" + Guid.NewGuid().ToString("N"));
            Texture2D source = null;
            Texture2D loadedBanner = null;
            Texture2D loadedIcon = null;
            try
            {
                string mediaDirectory = Path.Combine(packageRoot, "media");
                Directory.CreateDirectory(mediaDirectory);
                source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                source.SetPixels(new[]
                {
                    Color.red,
                    Color.green,
                    Color.blue,
                    Color.white,
                });
                source.Apply();
                byte[] bytes = source.EncodeToPNG();
                string bannerPath = Path.Combine(mediaDirectory, "banner.png");
                string iconPath = Path.Combine(mediaDirectory, "icon.png");
                File.WriteAllBytes(bannerPath, bytes);
                File.WriteAllBytes(iconPath, bytes);
                string packageJson =
                    "{\"name\":\"com.yucp.alias.jammr\"," +
                    "\"version\":\"1.0.0\",\"displayName\":\"JAMMR\"," +
                    "\"yucp\":{\"kind\":\"alias-v1\"," +
                    "\"aliasId\":\"jammr\"," +
                    "\"installStrategy\":\"server-authorized\"," +
                    "\"importerPackage\":\"com.yucp.importer\"," +
                    "\"media\":[{\"kind\":\"banner\"," +
                    "\"localPath\":\"media/banner.png\"," +
                    "\"contentType\":\"image/png\"," +
                    "\"byteSize\":" + bytes.Length + "," +
                    "\"sha256\":\"" + Sha256(bytes) + "\"}," +
                    "{\"kind\":\"icon\"," +
                    "\"localPath\":\"media/icon.png\"," +
                    "\"contentType\":\"image/png\"," +
                    "\"byteSize\":" + bytes.Length + "," +
                    "\"sha256\":\"" + Sha256(bytes) + "\"}]}}";

                bool built = AliasPackageDiscovery.TryBuildMetadata(
                    "com.yucp.alias.jammr",
                    packageJson,
                    packageRoot,
                    out PackageMetadata metadata,
                    out string error);

                Assert.That(built, Is.True, error);
                Assert.That(metadata.banner, Is.Not.Null);
                Assert.That(metadata.banner.width, Is.EqualTo(2));
                Assert.That(metadata.banner.height, Is.EqualTo(2));
                Assert.That(metadata.icon, Is.Not.Null);
                Assert.That(metadata.icon.width, Is.EqualTo(2));
                Assert.That(metadata.icon.height, Is.EqualTo(2));
                loadedBanner = metadata.banner;
                loadedIcon = metadata.icon;
            }
            finally
            {
                if (source != null)
                {
                    UnityEngine.Object.DestroyImmediate(source);
                }
                if (loadedBanner != null)
                {
                    UnityEngine.Object.DestroyImmediate(loadedBanner);
                }
                if (loadedIcon != null)
                {
                    UnityEngine.Object.DestroyImmediate(loadedIcon);
                }
                if (Directory.Exists(packageRoot))
                {
                    Directory.Delete(packageRoot, true);
                }
            }
        }

        [Test]
        public void AliasLoadsGalleryAndProductLinkMedia()
        {
            string packageRoot = Path.Combine(
                Path.GetTempPath(),
                "yucp-alias-rich-media-" + Guid.NewGuid().ToString("N"));
            Texture2D source = null;
            PackageMetadata metadata = null;
            try
            {
                source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                source.SetPixels(new[]
                {
                    Color.red,
                    Color.green,
                    Color.blue,
                    Color.white,
                });
                source.Apply();
                byte[] bytes = source.EncodeToPNG();
                string galleryPath = Path.Combine(
                    packageRoot,
                    "Documentation~",
                    "YUCP",
                    "gallery",
                    "000.png");
                string productLinkPath = Path.Combine(
                    packageRoot,
                    "Documentation~",
                    "YUCP",
                    "product-links",
                    "000.png");
                Directory.CreateDirectory(Path.GetDirectoryName(galleryPath));
                Directory.CreateDirectory(Path.GetDirectoryName(productLinkPath));
                File.WriteAllBytes(galleryPath, bytes);
                File.WriteAllBytes(productLinkPath, bytes);
                string packageJson =
                    "{\"name\":\"com.yucp.alias.jammr\"," +
                    "\"version\":\"1.0.0\",\"displayName\":\"JAMMR\"," +
                    "\"yucp\":{\"kind\":\"alias-v1\"," +
                    "\"aliasId\":\"jammr\"," +
                    "\"installStrategy\":\"server-authorized\"," +
                    "\"importerPackage\":\"com.yucp.importer\"," +
                    "\"media\":[{\"kind\":\"gallery\",\"ordinal\":0," +
                    "\"localPath\":\"Documentation~/YUCP/gallery/000.png\"," +
                    "\"contentType\":\"image/png\",\"byteSize\":" +
                    bytes.Length + ",\"sha256\":\"" + Sha256(bytes) + "\"}," +
                    "{\"kind\":\"product-link\",\"ordinal\":0," +
                    "\"label\":\"Gumroad\"," +
                    "\"url\":\"https://creator.gumroad.com/l/jammr\"," +
                    "\"localPath\":\"Documentation~/YUCP/product-links/000.png\"," +
                    "\"contentType\":\"image/png\",\"byteSize\":" +
                    bytes.Length + ",\"sha256\":\"" + Sha256(bytes) + "\"}]}}";

                bool built = AliasPackageDiscovery.TryBuildMetadata(
                    "com.yucp.alias.jammr",
                    packageJson,
                    packageRoot,
                    out metadata,
                    out string error);

                Assert.That(built, Is.True, error);
                Assert.That(metadata.galleryImages, Has.Count.EqualTo(1));
                Assert.That(metadata.productLinks, Has.Count.EqualTo(1));
                Assert.That(
                    metadata.productLinks[0].label,
                    Is.EqualTo("Gumroad"));
                Assert.That(
                    metadata.productLinks[0].url,
                    Is.EqualTo("https://creator.gumroad.com/l/jammr"));
                Assert.That(metadata.productLinks[0].customIcon, Is.Not.Null);
            }
            finally
            {
                PackageMetadataMediaOwnership.Release(metadata);
                if (source != null)
                {
                    UnityEngine.Object.DestroyImmediate(source);
                }
                if (Directory.Exists(packageRoot))
                {
                    Directory.Delete(packageRoot, true);
                }
            }
        }

        [Test]
        public void AliasLoadsPresentationMediaFromUnityPackageImportContent()
        {
            Texture2D source = null;
            PackageMetadata metadata = null;
            try
            {
                source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                source.SetPixels(new[]
                {
                    Color.red,
                    Color.green,
                    Color.blue,
                    Color.white,
                });
                source.Apply();
                byte[] bytes = source.EncodeToPNG();
                const string iconPath = "Documentation~/YUCP/icon.png";
                const string galleryPath = "Documentation~/YUCP/gallery/000.png";
                const string productLinkPath =
                    "Documentation~/YUCP/product-links/000.png";
                var content = new Dictionary<string, byte[]>
                {
                    [iconPath] = bytes,
                    [galleryPath] = bytes,
                    [productLinkPath] = bytes,
                };
                string packageJson =
                    "{\"name\":\"com.yucp.alias.jammr\"," +
                    "\"version\":\"1.0.0\",\"displayName\":\"JAMMR\"," +
                    "\"yucp\":{\"kind\":\"alias-v1\"," +
                    "\"aliasId\":\"jammr\"," +
                    "\"installStrategy\":\"server-authorized\"," +
                    "\"importerPackage\":\"com.yucp.importer\"," +
                    "\"media\":[{\"kind\":\"icon\",\"localPath\":\"" + iconPath +
                    "\",\"contentType\":\"image/png\",\"byteSize\":" +
                    bytes.Length + ",\"sha256\":\"" + Sha256(bytes) + "\"}," +
                    "{\"kind\":\"gallery\",\"ordinal\":0,\"localPath\":\"" + galleryPath +
                    "\",\"contentType\":\"image/png\",\"byteSize\":" +
                    bytes.Length + ",\"sha256\":\"" + Sha256(bytes) + "\"}," +
                    "{\"kind\":\"product-link\",\"ordinal\":0,\"label\":\"Gumroad\"," +
                    "\"url\":\"https://creator.gumroad.com/l/jammr\",\"localPath\":\"" +
                    productLinkPath + "\",\"contentType\":\"image/png\",\"byteSize\":" +
                    bytes.Length + ",\"sha256\":\"" + Sha256(bytes) + "\"}]}}";

                bool built = AliasPackageDiscovery.TryBuildMetadata(
                    "com.yucp.alias.jammr",
                    packageJson,
                    out metadata,
                    out string error);

                Assert.That(built, Is.True, error);
                AliasPackageMediaLoader.ApplyFromImportContent(
                    metadata,
                    metadata.aliasPackage,
                    descriptor => content.TryGetValue(descriptor.localPath, out byte[] value)
                        ? value
                        : null);

                Assert.That(metadata.icon, Is.Not.Null);
                Assert.That(metadata.galleryImages, Has.Count.EqualTo(1));
                Assert.That(metadata.productLinks, Has.Count.EqualTo(1));
                Assert.That(metadata.productLinks[0].customIcon, Is.Not.Null);
            }
            finally
            {
                PackageMetadataMediaOwnership.Release(metadata);
                if (source != null)
                {
                    UnityEngine.Object.DestroyImmediate(source);
                }
            }
        }

        [Test]
        public void UnityPackageMetadataExtractorLoadsModernAliasMedia()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "yucp-unitypackage-metadata-" + Guid.NewGuid().ToString("N"));
            Texture2D source = null;
            PackageMetadata metadata = null;
            try
            {
                source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                source.SetPixels(new[]
                {
                    Color.red,
                    Color.green,
                    Color.blue,
                    Color.white,
                });
                source.Apply();
                byte[] bytes = source.EncodeToPNG();
                const string packageRoot = "Packages/com.yucp.alias.jammr";
                const string iconPath = "Documentation~/YUCP/icon.png";
                const string galleryPath = "Documentation~/YUCP/gallery/000.png";
                string packageJson =
                    "{\"name\":\"com.yucp.alias.jammr\"," +
                    "\"version\":\"1.0.0\",\"displayName\":\"JAMMR\"," +
                    "\"yucp\":{\"kind\":\"alias-v1\"," +
                    "\"aliasId\":\"jammr\"," +
                    "\"installStrategy\":\"server-authorized\"," +
                    "\"importerPackage\":\"com.yucp.importer\"," +
                    "\"packageMetadata\":{\"packageName\":\"JAMMR\"}," +
                    "\"media\":[{\"kind\":\"icon\",\"localPath\":\"" + iconPath +
                    "\",\"contentType\":\"image/png\",\"byteSize\":" +
                    bytes.Length + ",\"sha256\":\"" + Sha256(bytes) + "\"}," +
                    "{\"kind\":\"gallery\",\"ordinal\":0,\"localPath\":\"" + galleryPath +
                    "\",\"contentType\":\"image/png\",\"byteSize\":" +
                    bytes.Length + ",\"sha256\":\"" + Sha256(bytes) + "\"}]}}";

                System.Array importItems = CreateImportItems(
                    new Dictionary<string, byte[]>
                    {
                        [packageRoot + "/package.json"] =
                            System.Text.Encoding.UTF8.GetBytes(packageJson),
                        [packageRoot + "/" + iconPath] = bytes,
                        [packageRoot + "/" + galleryPath] = bytes,
                    },
                    root);

                metadata = PackageMetadataExtractor.ExtractMetadataFromImportItems(
                    importItems,
                    "jammr.unitypackage");

                Assert.That(metadata.packageName, Is.EqualTo("JAMMR"));
                Assert.That(metadata.icon, Is.Not.Null);
                Assert.That(metadata.galleryImages, Has.Count.EqualTo(1));
            }
            finally
            {
                PackageMetadataMediaOwnership.Release(metadata);
                if (source != null)
                {
                    UnityEngine.Object.DestroyImmediate(source);
                }
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void AliasRejectsMediaThatEscapesTheInstalledPackage()
        {
            string packageRoot = Path.Combine(
                Path.GetTempPath(),
                "yucp-alias-media-escape-" + Guid.NewGuid().ToString("N"));
            string outsidePath = packageRoot + ".png";
            try
            {
                Directory.CreateDirectory(packageRoot);
                File.WriteAllBytes(outsidePath, new byte[] { 1, 2, 3 });
                const string packageJson =
                    "{\"name\":\"com.yucp.alias.jammr\"," +
                    "\"version\":\"1.0.0\",\"displayName\":\"JAMMR\"," +
                    "\"yucp\":{\"kind\":\"alias-v1\"," +
                    "\"aliasId\":\"jammr\"," +
                    "\"installStrategy\":\"server-authorized\"," +
                    "\"importerPackage\":\"com.yucp.importer\"," +
                    "\"media\":[{\"kind\":\"icon\"," +
                    "\"localPath\":\"../outside.png\"," +
                    "\"contentType\":\"image/png\",\"byteSize\":3," +
                    "\"sha256\":\"039058c6f2c0cb492c533b0a4d14ef77" +
                    "cc0f78abccced5287d84a1a2011cfb81\"}]}}";

                bool built = AliasPackageDiscovery.TryBuildMetadata(
                    "com.yucp.alias.jammr",
                    packageJson,
                    packageRoot,
                    out PackageMetadata metadata,
                    out string error);

                Assert.That(built, Is.False);
                Assert.That(metadata, Is.Null);
                Assert.That(error, Does.Contain("media"));
            }
            finally
            {
                if (File.Exists(outsidePath))
                {
                    File.Delete(outsidePath);
                }
                if (Directory.Exists(packageRoot))
                {
                    Directory.Delete(packageRoot, true);
                }
            }
        }

        [Test]
        public void RemovedInstallPlanMetadataIsRejected()
        {
            const string packageJson = "{\"name\":\"com.example.alias\",\"version\":\"1.0.0\"," +
                "\"displayName\":\"Alias\",\"yucp\":{\"kind\":\"alias-v1\"," +
                "\"aliasId\":\"example\",\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"," +
                "\"installPlan\":{\"id\":\"unsigned\"}}}";

            bool built = AliasPackageDiscovery.TryBuildMetadata(
                "com.example.alias",
                packageJson,
                out _,
                out string error);

            Assert.That(built, Is.False);
            Assert.That(error, Does.Contain("removed delivery field"));
        }

        [Test]
        public void AliasTreatsNullMediaAsAbsent()
        {
            const string packageJson =
                "{\"name\":\"com.example.alias\",\"version\":\"1.0.0\"," +
                "\"displayName\":\"Alias\",\"yucp\":{\"kind\":\"alias-v1\"," +
                "\"aliasId\":\"example\",\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\",\"media\":null}}";

            bool built = AliasPackageDiscovery.TryBuildMetadata(
                "com.example.alias",
                packageJson,
                out PackageMetadata metadata,
                out string error);

            Assert.That(built, Is.True, error);
            Assert.That(metadata.aliasPackage.media.icon.kind, Is.Empty);
            Assert.That(metadata.aliasPackage.media.banner.kind, Is.Empty);
        }

        [Test]
        public void LegacyInstallStateMigratesOwnedPathsAndHashes()
        {
            const string legacyJson =
                "{\"formatVersion\":\"1\",\"aliasId\":\"jammr\"," +
                "\"aliasVersion\":\"1.2.3\",\"packageId\":\"com.example.jammr\"," +
                "\"managedPaths\":[\"Assets/JAMMR/owned.asset\"]," +
                "\"generatedPaths\":[\"Assets/JAMMR/generated.asset\"]," +
                "\"sharedPaths\":[\"Assets/JAMMR/shared.asset\"]," +
                "\"fileHashes\":[{\"path\":\"Assets/JAMMR/owned.asset\"," +
                "\"expectedSha256\":\"" +
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"," +
                "\"observedSha256\":\"\"}]}";

            AliasPackageInstallStateManifest migrated =
                AliasPackageInstallStateStore.Deserialize(legacyJson);

            Assert.That(migrated, Is.Not.Null);
            Assert.That(migrated.formatVersion, Is.EqualTo("2"));
            Assert.That(
                migrated.managedPaths,
                Is.EqualTo(new[] { "Assets/JAMMR/owned.asset" }));
            Assert.That(
                migrated.generatedPaths,
                Is.EqualTo(new[] { "Assets/JAMMR/generated.asset" }));
            Assert.That(
                migrated.sharedPaths,
                Is.EqualTo(new[] { "Assets/JAMMR/shared.asset" }));
            Assert.That(migrated.fileHashes, Has.Count.EqualTo(1));
            Assert.That(
                migrated.fileHashes[0].path,
                Is.EqualTo("Assets/JAMMR/owned.asset"));
            Assert.That(
                migrated.fileHashes[0].expectedSha256,
                Has.Length.EqualTo(64));
        }

        [Test]
        public void AliasActivationUsesUnityPackageRegistrationBoundary()
        {
            object[] attributes = typeof(AliasPackageActivation)
                .GetCustomAttributes(typeof(InitializeOnLoadAttribute), false);

            Assert.That(attributes, Has.Length.EqualTo(1));
        }

        [Test]
        public void RegisteredAliasBuildsOneVerifyAndImportActivation()
        {
            const string packageJson = "{\"name\":\"com.yucp.jammr.alias\"," +
                "\"version\":\"1.0.0\",\"displayName\":\"JAMMR\"," +
                "\"yucp\":{\"kind\":\"alias-v1\",\"aliasId\":\"jammr\"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"}}";

            bool built = AliasPackageActivation.TryBuildActivation(
                "com.yucp.jammr.alias",
                packageJson,
                out AliasPackageActivationRequest activation,
                out string error);

            Assert.That(built, Is.True, error);
            Assert.That(activation.Alias.aliasId, Is.EqualTo("jammr"));
            Assert.That(activation.ActionLabel, Is.EqualTo("Verify and Import"));
            Assert.That(
                activation.Key,
                Is.EqualTo("com.yucp.jammr.alias@1.0.0:jammr"));
        }

        [Test]
        public void VersionedAliasActivationUsesTheSignedIntentIdentity()
        {
            const string intentId =
                "11111111-1111-4111-8111-111111111111";
            const string packageJson =
                "{\"name\":\"com.yucp.jammr\",\"version\":\"2.4.0-beta.1\"," +
                "\"displayName\":\"JAMMR\",\"yucp\":{\"kind\":\"alias-v2\"," +
                "\"aliasId\":\"com.yucp.jammr\"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"," +
                "\"bootstrapIntent\":{\"schemaVersion\":1," +
                "\"intentId\":\"" + intentId + "\",\"mode\":\"specific\"," +
                "\"issuedAt\":1785384000,\"keyId\":\"bootstrap-key\"," +
                "\"editionId\":\"pro\",\"version\":\"2.4.0-beta.1\"," +
                "\"versionId\":\"version-beta-1\",\"releaseRoot\":\"" +
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"," +
                "\"signature\":\"" +
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
                "\"}}}";

            Assert.That(
                AliasPackageActivation.TryBuildActivation(
                    "com.yucp.jammr",
                    packageJson,
                    out AliasPackageActivationRequest activation,
                    out string error),
                Is.True,
                error);
            Assert.That(activation.Key, Is.EqualTo(intentId));
            Assert.That(
                activation.Alias.bootstrapIntent.releaseRoot,
                Is.EqualTo(
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
            Assert.That(
                activation.Alias.bootstrapIntent.mode,
                Is.EqualTo("specific"));
        }

        [Test]
        public void UnityPackageBootstrapHandoffPreservesTheSignedIntent()
        {
            const string intentId =
                "33333333-3333-4333-8333-333333333333";
            const string intentJson =
                "{\"schemaVersion\":1," +
                "\"intentId\":\"" + intentId + "\",\"mode\":\"specific\"," +
                "\"issuedAt\":1785384000,\"keyId\":\"bootstrap-key\"," +
                "\"editionId\":\"pro\",\"version\":\"2.4.0-beta.1\"," +
                "\"versionId\":\"version-beta-1\",\"releaseRoot\":\"" +
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"," +
                "\"signature\":\"" +
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
                "\"}";
            const string packageJson =
                "{\"name\":\"com.yucp.jammr\",\"version\":\"2.4.0-beta.1\"," +
                "\"displayName\":\"JAMMR\",\"yucp\":{\"kind\":\"alias-v2\"," +
                "\"aliasId\":\"com.yucp.jammr\"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"," +
                "\"bootstrapIntent\":" + intentJson + "}}";

            Assert.That(
                AliasPackageActivation.TryBuildUnityPackageActivation(
                    packageJson,
                    out AliasPackageActivationRequest activation,
                    out string error),
                Is.True,
                error);
            Assert.That(activation.Key, Is.EqualTo(intentId));
            Assert.That(
                activation.Alias.bootstrapIntent.rawIntentJson,
                Is.EqualTo(intentJson));
            Assert.That(
                activation.Alias.directUnityPackageBootstrap,
                Is.True);
        }

        [Test]
        public void ActivatingADeactivatedAssetDoesNotLookLikeCorruption()
        {
            Assert.That(
                PackageImportVerifier.ActivatedOwnedPath(
                    "Assets/Song Thing/InAJam.anim.yucp_disabled"),
                Is.EqualTo("Assets/Song Thing/InAJam.anim"));
            Assert.That(
                PackageImportVerifier.ActivatedOwnedPath(
                    "Assets/Song Thing/InAJam.anim.yucp_disabled.meta"),
                Is.EqualTo("Assets/Song Thing/InAJam.anim.meta"));
            Assert.That(
                PackageImportVerifier.ActivatedOwnedPath(
                    "Assets/Song Thing/InAJam.anim"),
                Is.Null);
            Assert.That(
                PackageImportVerifier.ActivatedOwnedPath(null),
                Is.Null);

            string projectPath = Path.Combine(
                Path.GetTempPath(),
                "yucp-activated-presence-" + Guid.NewGuid().ToString("N"));
            var state = new PackageDeliveryInstallState
            {
                aliasId = "com.yucp.songthing",
                files =
                {
                    new NativePackageBrokerFile
                    {
                        bytes = 5,
                        normalizedPath = "Assets/InAJam.anim.yucp_disabled",
                    },
                },
            };
            try
            {
                Directory.CreateDirectory(Path.Combine(projectPath, "Assets"));
                File.WriteAllText(
                    Path.Combine(projectPath, "Assets", "InAJam.anim"),
                    "12345");
                Assert.That(
                    PackageLifecycleCoordinator.RecordedReleaseIsOnDisk(
                        projectPath,
                        state),
                    Is.True,
                    "An owned file the resolver activated is still installed.");
            }
            finally
            {
                if (Directory.Exists(projectPath))
                {
                    Directory.Delete(projectPath, true);
                }
            }
        }

        [Test]
        public void ARecordedReleaseIsNotInstalledOnceItsFilesAreGone()
        {
            string projectPath = Path.Combine(
                Path.GetTempPath(),
                "yucp-release-presence-" + Guid.NewGuid().ToString("N"));
            string owned = Path.Combine(projectPath, "Assets", "Druffle.prefab");
            var state = new PackageDeliveryInstallState
            {
                aliasId = "com.lunararray.druffle",
                files =
                {
                    new NativePackageBrokerFile
                    {
                        bytes = 5,
                        normalizedPath = "Assets/Druffle.prefab",
                    },
                },
            };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(owned));
                File.WriteAllText(owned, "12345");
                Assert.That(
                    PackageLifecycleCoordinator.RecordedReleaseIsOnDisk(
                        projectPath,
                        state),
                    Is.True);

                File.WriteAllText(owned, "1234");
                Assert.That(
                    PackageLifecycleCoordinator.RecordedReleaseIsOnDisk(
                        projectPath,
                        state),
                    Is.False,
                    "A truncated owned file must not read as installed.");

                File.Delete(owned);
                Assert.That(
                    PackageLifecycleCoordinator.RecordedReleaseIsOnDisk(
                        projectPath,
                        state),
                    Is.False,
                    "A deleted owned file must not read as installed.");

                Assert.That(
                    PackageLifecycleCoordinator.RecordedReleaseIsOnDisk(
                        projectPath,
                        new PackageDeliveryInstallState()),
                    Is.False);
            }
            finally
            {
                if (Directory.Exists(projectPath))
                {
                    Directory.Delete(projectPath, true);
                }
            }
        }

        [Test]
        public void InterceptedUnityPackageCompletesWithoutAPackagesFolder()
        {
            const string packageJson = "{\"name\":\"com.lunararray.druffle\"," +
                "\"version\":\"1.0.0\",\"displayName\":\"Druffle Avatar\"," +
                "\"yucp\":{\"kind\":\"alias-v1\"," +
                "\"aliasId\":\"com.lunararray.druffle\"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"}}";
            AliasPackageContract alias =
                PackageMetadataExtractor.MergePackageJsonImportData(
                    PackageMetadataExtractor.ParsePackageJsonImportDataStrict(
                        packageJson),
                    null).aliasPackage;
            Assert.That(alias.packageName, Is.EqualTo("com.lunararray.druffle"));
            Assert.That(alias.packageVersion, Is.EqualTo("1.0.0"));

            string projectPath = Path.Combine(
                Path.GetTempPath(),
                "yucp-intercepted-completion-" +
                Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(projectPath);

                Assert.That(
                    PackageLifecycleCoordinator.FinalizeSuccessfulAliasOperation(
                        projectPath,
                        alias,
                        "update"),
                    Is.Null);
            }
            finally
            {
                if (Directory.Exists(projectPath))
                {
                    Directory.Delete(projectPath, true);
                }
            }
        }

        [Test]
        public void DirectUnityPackageCompletionDoesNotRequireRegisteredVpmBootstrap()
        {
            string projectPath = Path.Combine(
                Path.GetTempPath(),
                "yucp-unitypackage-completion-" +
                Guid.NewGuid().ToString("N"));
            var alias = new AliasPackageContract
            {
                aliasId = "jammr",
                bootstrapIntent = new BootstrapIntentContract
                {
                    intentId =
                        "44444444-4444-4444-8444-444444444444",
                },
                directUnityPackageBootstrap = true,
                kind = "alias-v2",
                packageName = "com.yucp.jammr",
                packageVersion = "2.4.0",
            };
            try
            {
                Directory.CreateDirectory(projectPath);
                DirectUnityPackageBootstrapStore.Persist(
                    projectPath,
                    alias,
                    "{\"name\":\"com.yucp.jammr\"}");
                alias.directUnityPackageBootstrap = false;

                string error =
                    PackageLifecycleCoordinator.FinalizeSuccessfulAliasOperation(
                        projectPath,
                        alias,
                        "update");

                Assert.That(error, Is.Null);
                Assert.That(
                    AliasPackageActivationStateStore.IsHandled(
                        projectPath,
                        alias),
                    Is.True);
                Assert.That(
                    DirectUnityPackageBootstrapStore.ReadAll(projectPath),
                    Is.Empty);
            }
            finally
            {
                if (Directory.Exists(projectPath))
                {
                    Directory.Delete(projectPath, true);
                }
            }
        }

        [Test]
        public void VpmCompletionStillRequiresARegisteredBootstrap()
        {
            string projectPath = Path.Combine(
                Path.GetTempPath(),
                "yucp-vpm-missing-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(projectPath);

                string error =
                    PackageLifecycleCoordinator.FinalizeSuccessfulAliasOperation(
                        projectPath,
                        new AliasPackageContract
                        {
                            aliasId = "jammr",
                            kind = "alias-v2",
                            packageName = "com.yucp.jammr",
                            packageVersion = "2.4.0",
                            bootstrapIntent = new BootstrapIntentContract
                            {
                                intentId =
                                    "55555555-5555-4555-8555-555555555555",
                            },
                        },
                        "update");

                Assert.That(
                    error,
                    Is.EqualTo(
                        "The VPM bootstrap is no longer registered."));
            }
            finally
            {
                if (Directory.Exists(projectPath))
                {
                    Directory.Delete(projectPath, true);
                }
            }
        }

        [Test]
        public void VersionedAliasWithoutAnIntentIsRejected()
        {
            const string packageJson =
                "{\"name\":\"com.yucp.jammr\",\"version\":\"2.4.0\"," +
                "\"yucp\":{\"kind\":\"alias-v2\"," +
                "\"aliasId\":\"com.yucp.jammr\"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"}}";

            Assert.That(
                AliasPackageActivation.TryBuildActivation(
                    "com.yucp.jammr",
                    packageJson,
                    out AliasPackageActivationRequest activation,
                    out string error),
                Is.False);
            Assert.That(activation, Is.Null);
            Assert.That(error, Does.Contain("server-authorized"));
        }

        [Test]
        public void VersionedIntentSchedulesAnExplicitUpdateButLegacyAliasDoesNot()
        {
            var versioned = new AliasPackageContract
            {
                kind = "alias-v2",
                bootstrapIntent = new BootstrapIntentContract
                {
                    intentId = "22222222-2222-4222-8222-222222222222",
                },
            };
            var legacy = new AliasPackageContract { kind = "alias-v1" };

            Assert.That(
                AliasPackageActivation.ShouldSchedule(
                    versioned,
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    false),
                Is.True);
            Assert.That(
                AliasPackageActivation.ShouldSchedule(
                    legacy,
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    false),
                Is.False);
        }

        [Test]
        public void AliasActivationDoesNotDiscoverUpdatesOnEditorStartup()
        {
            string source = File.ReadAllText(
                Path.Combine(
                    Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                    "Packages",
                    "com.yucp.importer",
                    "Editor",
                    "PackageManager",
                    "Core",
                    "AliasPackageActivation.cs"));

            Assert.That(
                source,
                Does.Not.Contain(
                    "EditorApplication.delayCall += ReconcileRegisteredAliases"));
        }

        [Test]
        public void RegisteredAliasRejectsABlankAliasIdentifier()
        {
            const string packageJson = "{\"name\":\"com.yucp.blank.alias\"," +
                "\"version\":\"1.0.0\",\"displayName\":\"Blank Alias\"," +
                "\"yucp\":{\"kind\":\"alias-v1\",\"aliasId\":\" \"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"}}";

            bool built = AliasPackageActivation.TryBuildActivation(
                "com.yucp.blank.alias",
                packageJson,
                out AliasPackageActivationRequest activation,
                out string error);

            Assert.That(built, Is.False);
            Assert.That(activation, Is.Null);
            Assert.That(error, Does.Contain("server-authorized"));
        }

        [Test]
        public void BootstrapWindowAcceptsAliasWithoutUnityPackageItems()
        {
            const string packageJson = "{\"name\":\"com.yucp.jammr.alias\"," +
                "\"version\":\"1.0.0\",\"displayName\":\"JAMMR\"," +
                "\"yucp\":{\"kind\":\"alias-v1\",\"aliasId\":\"jammr\"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"}}";
            Assert.That(
                AliasPackageActivation.TryBuildActivation(
                    "com.yucp.jammr.alias",
                    packageJson,
                    out AliasPackageActivationRequest activation,
                    out string error),
                Is.True,
                error);

            var window = UnityEngine.ScriptableObject.CreateInstance<PackageManagerWindow>();
            try
            {
                window.InitializeForAlias(activation.Metadata, false);

                Assert.That(window.IsAliasBootstrapFlow, Is.True);
                Assert.That(
                    window.PrimaryActionLabel,
                    Is.EqualTo("Checking sign-in..."));
                Assert.That(window.HasPackageImportItems, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void SignedOutBootstrapUsesClickableSingleFlightSignInAction()
        {
            const string packageJson = "{\"name\":\"com.yucp.jammr.alias\"," +
                "\"version\":\"1.0.0\",\"displayName\":\"JAMMR\"," +
                "\"yucp\":{\"kind\":\"alias-v1\",\"aliasId\":\"jammr\"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"}}";
            Assert.That(
                AliasPackageActivation.TryBuildActivation(
                    "com.yucp.jammr.alias",
                    packageJson,
                    out AliasPackageActivationRequest activation,
                    out string error),
                Is.True,
                error);

            var window =
                ScriptableObject.CreateInstance<PackageManagerWindow>();
            try
            {
                window.InitializeForAlias(activation.Metadata, false);
                window.CreateGUI();

                FieldInfo signedInField =
                    typeof(PackageManagerWindow).GetField(
                        "_isBrokerSignedIn",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo actionField =
                    typeof(PackageManagerWindow).GetField(
                        "_authenticationOperation",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo buttonField =
                    typeof(PackageManagerWindow).GetField(
                        "_importButton",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo updateEnabled =
                    typeof(PackageManagerWindow).GetMethod(
                        "UpdateImportButtonEnabled",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(signedInField, Is.Not.Null);
                Assert.That(actionField, Is.Not.Null);
                Assert.That(buttonField, Is.Not.Null);
                Assert.That(updateEnabled, Is.Not.Null);

                signedInField.SetValue(window, false);
                actionField.SetValue(
                    window,
                    Enum.Parse(actionField.FieldType, "None"));
                updateEnabled.Invoke(window, null);

                var button = (Button)buttonField.GetValue(window);
                Assert.That(
                    window.PrimaryActionLabel,
                    Is.EqualTo("Sign in with YUCP"));
                Assert.That(button.enabledSelf, Is.True);
                Assert.That(
                    button.Q<Image>(className: "yucp-cta-icon"),
                    Is.Not.Null,
                    "The signed-out action must preserve the YUCP bag icon.");

                actionField.SetValue(
                    window,
                    Enum.Parse(actionField.FieldType, "SignIn"));
                updateEnabled.Invoke(window, null);

                Assert.That(
                    window.PrimaryActionLabel,
                    Is.EqualTo("Signing in..."));
                Assert.That(button.enabledSelf, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void RepeatedAliasRefreshAndCloseReleasesOwnedMedia()
        {
            string texturePrefix =
                "YUCP owned media " + Guid.NewGuid().ToString("N");
            int baseline = CountTextures(texturePrefix);
            Texture2D packageTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Packages/com.yucp.importer/Editor/PackageManager/" +
                "Resources/Bag.png");
            Assert.That(packageTexture, Is.Not.Null);
            var window =
                ScriptableObject.CreateInstance<PackageManagerWindow>();
            try
            {
                for (int index = 0; index < 3; index++)
                {
                    PackageMetadata metadata = OwnedAliasMetadata(
                        texturePrefix + " " + index);
                    if (index == 2)
                    {
                        UnityEngine.Object.DestroyImmediate(metadata.icon);
                        metadata.icon = packageTexture;
                    }
                    window.InitializeForAlias(metadata, false);
                    window.CreateGUI();
                    window.CreateGUI();

                    Assert.That(
                        CountTextures(texturePrefix),
                        Is.LessThanOrEqualTo(baseline + 2));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }

            Assert.That(CountTextures(texturePrefix), Is.EqualTo(baseline));
            Assert.That(packageTexture, Is.Not.Null);
            Assert.That(AssetDatabase.Contains(packageTexture), Is.True);
        }

        [TestCase("already-open")]
        [TestCase("dismissed")]
        [TestCase("duplicate")]
        public void DiscardedAliasActivationReleasesOwnedMedia(string branch)
        {
            string suffix = Guid.NewGuid().ToString("N");
            string aliasId = "media-" + suffix;
            PackageMetadata discarded = OwnedAliasMetadata(
                "YUCP discarded media " + suffix);
            discarded.aliasPackage.aliasId = aliasId;
            discarded.aliasPackage.packageName =
                "com.yucp.alias." + suffix;
            var activation = new AliasPackageActivationRequest(
                discarded,
                discarded.aliasPackage.packageName +
                "@1.0.0:" + aliasId);
            PackageManagerWindow existing = null;
            FieldInfo scheduledField = typeof(AliasPackageActivation).GetField(
                "Scheduled",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo schedule = typeof(AliasPackageActivation).GetMethod(
                "Schedule",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(scheduledField, Is.Not.Null);
            Assert.That(schedule, Is.Not.Null);
            var scheduled =
                (HashSet<string>)scheduledField.GetValue(null);
            string dismissalKey =
                AliasPackageActivation.BuildDismissalSessionKey(
                    activation.Key);
            try
            {
                if (branch == "already-open")
                {
                    existing =
                        ScriptableObject.CreateInstance<PackageManagerWindow>();
                    PackageMetadata openMetadata = OwnedAliasMetadata(
                        "YUCP open media " + suffix);
                    openMetadata.aliasPackage.aliasId = aliasId;
                    existing.InitializeForAlias(openMetadata, false);
                }
                else if (branch == "dismissed")
                {
                    SessionState.SetBool(dismissalKey, true);
                }
                else
                {
                    scheduled.Add(activation.Key);
                }

                schedule.Invoke(null, new object[] { activation });

                Assert.That(discarded.icon, Is.Null);
                Assert.That(discarded.banner, Is.Null);
            }
            finally
            {
                SessionState.EraseBool(dismissalKey);
                scheduled.Remove(activation.Key);
                if (existing != null)
                {
                    UnityEngine.Object.DestroyImmediate(existing);
                }
                if (discarded.icon != null)
                {
                    UnityEngine.Object.DestroyImmediate(discarded.icon);
                }
                if (discarded.banner != null)
                {
                    UnityEngine.Object.DestroyImmediate(discarded.banner);
                }
            }
        }

        [Test]
        public void BootstrapWindowRemainsVisibleAfterUnityBuildsItsVisualTree()
        {
            const string packageJson = "{\"name\":\"com.yucp.jammr.alias\"," +
                "\"version\":\"1.0.0\",\"displayName\":\"JAMMR\"," +
                "\"yucp\":{\"kind\":\"alias-v1\",\"aliasId\":\"jammr\"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"}}";
            Assert.That(
                AliasPackageActivation.TryBuildActivation(
                    "com.yucp.jammr.alias",
                    packageJson,
                    out AliasPackageActivationRequest activation,
                    out string error),
                Is.True,
                error);

            var window = UnityEngine.ScriptableObject.CreateInstance<PackageManagerWindow>();
            try
            {
                window.InitializeForAlias(activation.Metadata, false);

                MethodInfo createGui = typeof(PackageManagerWindow).GetMethod(
                    "CreateGUI",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);
                Assert.That(createGui, Is.Not.Null);
                createGui.Invoke(window, null);

                VisualElement installer = window.rootVisualElement.Q<VisualElement>(
                    className: "yucp-installer-root");
                Assert.That(installer, Is.Not.Null);
                Assert.That(
                    installer.style.display.value,
                    Is.EqualTo(DisplayStyle.Flex));
                Assert.That(installer.parent, Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void BootstrapWindowRestoresItsAliasAfterDomainReloadSerialization()
        {
            const string packageJson = "{\"name\":\"com.yucp.jammr.alias\"," +
                "\"version\":\"1.0.0\",\"displayName\":\"JAMMR\"," +
                "\"yucp\":{\"kind\":\"alias-v1\",\"aliasId\":\"jammr\"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"}}";
            Assert.That(
                AliasPackageActivation.TryBuildActivation(
                    "com.yucp.jammr.alias",
                    packageJson,
                    out AliasPackageActivationRequest activation,
                    out string error),
                Is.True,
                error);

            var original = UnityEngine.ScriptableObject.CreateInstance<PackageManagerWindow>();
            var restored = UnityEngine.ScriptableObject.CreateInstance<PackageManagerWindow>();
            try
            {
                original.InitializeForAlias(activation.Metadata, false);
                string serialized = EditorJsonUtility.ToJson(original);

                EditorJsonUtility.FromJsonOverwrite(serialized, restored);
                restored.CreateGUI();

                Assert.That(restored.IsAliasBootstrapFlow, Is.True);
                Assert.That(
                    restored.PrimaryActionLabel,
                    Is.EqualTo("Checking sign-in..."));
                VisualElement installer = restored.rootVisualElement.Q<VisualElement>(
                    className: "yucp-installer-root");
                Assert.That(installer, Is.Not.Null);
                Assert.That(
                    installer.style.display.value,
                    Is.EqualTo(DisplayStyle.Flex));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(original);
                UnityEngine.Object.DestroyImmediate(restored);
            }
        }

        [Test]
        public void AliasDismissalStateDoesNotReuseTheLegacyAttemptMarker()
        {
            string key = AliasPackageActivation.BuildDismissalSessionKey(
                "com.yucp.jammr.alias@1.0.0:jammr");

            Assert.That(
                key,
                Is.EqualTo(
                    "YUCP.PackageManager.AliasActivation.DismissedV1." +
                    "com.yucp.jammr.alias@1.0.0:jammr"));
            Assert.That(key, Does.Not.Contain("Attempted"));
        }

        [Test]
        public void BootstrapWindowReportsItsActiveAlias()
        {
            const string packageJson = "{\"name\":\"com.yucp.jammr.alias\"," +
                "\"version\":\"1.0.0\",\"displayName\":\"JAMMR\"," +
                "\"yucp\":{\"kind\":\"alias-v1\",\"aliasId\":\"jammr\"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"}}";
            Assert.That(
                AliasPackageActivation.TryBuildActivation(
                    "com.yucp.jammr.alias",
                    packageJson,
                    out AliasPackageActivationRequest activation,
                    out string error),
                Is.True,
                error);

            var window = UnityEngine.ScriptableObject.CreateInstance<PackageManagerWindow>();
            try
            {
                window.InitializeForAlias(activation.Metadata, false);

                Assert.That(
                    PackageManagerWindow.IsAliasBootstrapOpen("jammr"),
                    Is.True);
                Assert.That(
                    PackageManagerWindow.IsAliasBootstrapOpen("other"),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ManualPackageInstallerHasAUnityMenuEntry()
        {
            MethodInfo method = typeof(AliasPackageActivation).GetMethod(
                "OpenPackageInstallerFromMenu",
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            MenuItem menuItem = method.GetCustomAttribute<MenuItem>();
            Assert.That(menuItem, Is.Not.Null);
            Assert.That(
                menuItem.menuItem,
                Is.EqualTo(
                    "Tools/YUCP/Package Manager/Open Product Installer"));
        }

        [Test]
        public void LifecycleCompletionKeepsTheVpmAliasBootstrapRegistered()
        {
            string projectPath = Path.Combine(
                Path.GetTempPath(),
                "yucp-vpm-cleanup-" + Guid.NewGuid().ToString("N"));
            string packagesPath = Path.Combine(projectPath, "Packages");
            string aliasName = "com.yucp.alias.test";
            string aliasPath = Path.Combine(packagesPath, aliasName);
            string importerPath = Path.Combine(
                packagesPath,
                "com.yucp.importer");
            try
            {
                Directory.CreateDirectory(Path.Combine(projectPath, "Assets"));
                Directory.CreateDirectory(Path.Combine(
                    projectPath,
                    "ProjectSettings"));
                Directory.CreateDirectory(aliasPath);
                Directory.CreateDirectory(importerPath);
                File.WriteAllText(
                    Path.Combine(aliasPath, "package.json"),
                    "{\"name\":\"com.yucp.alias.test\"," +
                    "\"version\":\"1.0.0\",\"displayName\":\"Test\"," +
                    "\"yucp\":{\"kind\":\"alias-v1\"," +
                    "\"aliasId\":\"test\"," +
                    "\"installStrategy\":\"server-authorized\"," +
                    "\"importerPackage\":\"com.yucp.importer\"}}");
                File.WriteAllText(
                    Path.Combine(
                        projectPath,
                        "ProjectSettings",
                        "ProjectVersion.txt"),
                    "m_EditorVersion: 2022.3.22f1\n");
                File.WriteAllText(
                    Path.Combine(packagesPath, "manifest.json"),
                    "{\"dependencies\":{}}");
                File.WriteAllText(
                    Path.Combine(packagesPath, "vpm-manifest.json"),
                    "{\"dependencies\":{\"com.yucp.alias.test\":" +
                    "{\"version\":\"1.0.0\"}}," +
                    "\"locked\":{\"com.yucp.alias.test\":" +
                    "{\"version\":\"1.0.0\",\"dependencies\":" +
                    "{\"com.yucp.importer\":\">=0.1.25\"}}," +
                    "\"com.yucp.importer\":{\"version\":\"0.1.29\"," +
                    "\"dependencies\":{}}}}");

                string error =
                    PackageLifecycleCoordinator.FinalizeSuccessfulAliasOperation(
                        projectPath,
                        new AliasPackageContract
                        {
                            aliasId = "test",
                            packageName = aliasName,
                            packageVersion = "1.0.0",
                        },
                        "update");

                Assert.That(error, Is.Null);
                Assert.That(Directory.Exists(aliasPath), Is.True);
                Assert.That(Directory.Exists(importerPath), Is.True);
                string manifest = File.ReadAllText(
                    Path.Combine(packagesPath, "vpm-manifest.json"));
                Assert.That(manifest, Does.Contain(aliasName));
                Assert.That(manifest, Does.Contain("com.yucp.importer"));
            }
            finally
            {
                if (Directory.Exists(projectPath))
                {
                    Directory.Delete(projectPath, true);
                }
            }
        }

        [Test]
        public void ActivationStateSupportsLongValidProjectPaths()
        {
            string basePath = Path.Combine(
                Path.GetTempPath(),
                "yucp-alias-long-" + Guid.NewGuid().ToString("N"));
            int suffixLength = Math.Max(
                8,
                150 - basePath.Length - 1);
            string projectPath = Path.Combine(
                basePath,
                new string('a', suffixLength));
            var alias = new AliasPackageContract
            {
                aliasId = "jammr",
                packageName = "com.yucp.alias.jammr",
                packageVersion = "1.2.3",
            };
            try
            {
                Directory.CreateDirectory(projectPath);

                AliasPackageActivationStateStore.MarkHandled(
                    projectPath,
                    alias,
                    "install");

                Assert.That(
                    AliasPackageActivationStateStore.IsHandled(
                        projectPath,
                        alias),
                    Is.True);
                string stateRoot = Path.Combine(
                    projectPath,
                    ".yucp",
                    "alias-activation");
                Assert.That(
                    Directory.GetFiles(stateRoot, "*.json").Length,
                    Is.EqualTo(1));
                Assert.That(
                    Directory.GetFiles(stateRoot, "*.partial").Length,
                    Is.Zero);
            }
            finally
            {
                if (Directory.Exists(basePath))
                {
                    Directory.Delete(basePath, true);
                }
            }
        }

        [Test]
        public void LockedAliasActivationStateIsTreatedAsNotHandled()
        {
            string project = Path.Combine(
                Path.GetTempPath(),
                "yucp-alias-state-lock-" + Guid.NewGuid().ToString("N"));
            var alias = new AliasPackageContract
            {
                aliasId = "locked-alias",
                packageName = "com.yucp.locked.alias",
                packageVersion = "1.0.0",
            };
            try
            {
                Directory.CreateDirectory(project);
                AliasPackageActivationStateStore.MarkHandled(
                    project,
                    alias,
                    "install");
                string statePath = Directory.GetFiles(
                    Path.Combine(project, ".yucp", "alias-activation"),
                    "*.json",
                    SearchOption.TopDirectoryOnly).Single();
                using (new FileStream(
                    statePath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None))
                {
                    Assert.That(
                        AliasPackageActivationStateStore.IsHandled(
                            project,
                            alias),
                        Is.False);
                }
            }
            finally
            {
                if (Directory.Exists(project))
                {
                    Directory.Delete(project, true);
                }
            }
        }

        [Test]
        public void ExplicitUninstallDoesNotReactivateTheBootstrapOnRestart()
        {
            string projectPath = Path.Combine(
                Path.GetTempPath(),
                "yucp-alias-activation-" + Guid.NewGuid().ToString("N"));
            var alias = new AliasPackageContract
            {
                aliasId = "jammr",
                packageName = "com.yucp.alias.jammr",
                packageVersion = "1.2.3",
            };
            try
            {
                Directory.CreateDirectory(projectPath);
                AliasPackageActivationStateStore.MarkHandled(
                    projectPath,
                    alias,
                    "uninstall");

                Assert.That(
                    AliasPackageActivationStateStore.IsHandled(
                        projectPath,
                        alias),
                    Is.True);
                Assert.That(
                    AliasPackageActivation.ShouldSchedule(
                        PackageLifecycleCoordinator.EmptyReleaseRoot,
                        true),
                    Is.False);
                Assert.That(
                    AliasPackageActivation.ShouldSchedule(
                        PackageLifecycleCoordinator.EmptyReleaseRoot,
                        false),
                    Is.True);
            }
            finally
            {
                if (Directory.Exists(projectPath))
                {
                    Directory.Delete(projectPath, true);
                }
            }
        }

        [Test]
        public void RegisteredPackageEventsRespectThePersistentActivationState()
        {
            string projectPath = Path.Combine(
                Path.GetTempPath(),
                "yucp-alias-event-" + Guid.NewGuid().ToString("N"));
            var alias = new AliasPackageContract
            {
                aliasId = "jammr",
                packageName = "com.yucp.alias.jammr",
                packageVersion = "1.2.3",
            };
            var activation = new AliasPackageActivationRequest(
                new PackageMetadata("JAMMR")
                {
                    aliasPackage = alias,
                },
                "com.yucp.alias.jammr@1.2.3:jammr");
            try
            {
                Directory.CreateDirectory(projectPath);
                AliasPackageActivationStateStore.MarkHandled(
                    projectPath,
                    alias,
                    "install");
                MethodInfo method = typeof(AliasPackageActivation).GetMethod(
                    "ShouldScheduleForProject",
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

                Assert.That(
                    method,
                    Is.Not.Null,
                    "Every package-registration path must use the persistent loop guard.");
                Assert.That(
                    method.Invoke(
                        null,
                        new object[] { projectPath, activation }),
                    Is.False,
                    "A completed bootstrap must not reopen after package registration.");
            }
            finally
            {
                if (Directory.Exists(projectPath))
                {
                    Directory.Delete(projectPath, true);
                }
            }
        }

        [Test]
        public void PendingInstallResumesAsInstallAfterDomainReload()
        {
            string projectPath = Path.Combine(
                Path.GetTempPath(),
                "yucp-alias-reload-" + Guid.NewGuid().ToString("N"));
            var alias = new AliasPackageContract
            {
                aliasId = "jammr",
                packageName = "com.yucp.alias.jammr",
                packageVersion = "1.2.3",
            };
            var activation = new AliasPackageActivationRequest(
                new PackageMetadata("JAMMR")
                {
                    aliasPackage = alias,
                },
                "com.yucp.alias.jammr@1.2.3:jammr");
            try
            {
                Directory.CreateDirectory(projectPath);
                string packagePath = Path.Combine(
                    projectPath,
                    "Packages",
                    alias.packageName);
                Directory.CreateDirectory(packagePath);
                string releaseRoot = new string('2', 64);
                MethodInfo writeState = typeof(PackageLifecycleCoordinator)
                    .GetMethod(
                        "WriteInstallState",
                        BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(writeState, Is.Not.Null);
                writeState.Invoke(
                    null,
                    new object[]
                    {
                        projectPath,
                        alias.aliasId,
                        releaseRoot,
                        "version-2",
                        "2.0.0",
                        "receipt-2",
                        "receipts/receipt-2.cbor",
                        new string('a', 64),
                        "active-content-policy-v1",
                        new List<NativePackageBrokerFile>
                        {
                            new NativePackageBrokerFile
                            {
                                bytes = 1,
                                normalizedPath =
                                    "Assets/Product/file.txt",
                                sha256 = new string('b', 64),
                            },
                        },
                        null,
                    });
                string attemptId =
                    PackageLifecycleCheckpointStore.GetOrCreateAttemptId(
                        projectPath,
                        alias.aliasId);
                PackageLifecycleCheckpointStore.Write(
                    projectPath,
                    new PackageLifecycleCheckpoint
                    {
                        aliasId = alias.aliasId,
                        expectedCurrentReleaseRoot =
                            PackageLifecycleCoordinator.EmptyReleaseRoot,
                        operation = "install",
                        phase = "committed",
                        runId = attemptId + "-execute",
                        targetState = new PackageDeliveryInstallState
                        {
                            aliasId = alias.aliasId,
                            releaseRoot = releaseRoot,
                            versionId = "version-2",
                        },
                    });

                Assert.That(
                    PackageLifecycleCoordinator.GetPendingOperation(
                        projectPath,
                        alias.aliasId),
                    Is.EqualTo("install"));
                Assert.That(
                    AliasPackageActivation.ShouldScheduleForProject(
                        projectPath,
                        activation),
                    Is.True,
                    "A committed install must reopen for verification.");
            }
            finally
            {
                if (Directory.Exists(projectPath))
                {
                    Directory.Delete(projectPath, true);
                }
            }
        }

        [TestCase("verified")]
        [TestCase("rolled-back")]
        public void TerminalAttemptRemainsDiscoverableUntilFinalization(
            string phase)
        {
            string projectPath = Path.Combine(
                Path.GetTempPath(),
                "yucp-alias-terminal-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(projectPath);
                string attemptId =
                    PackageLifecycleCheckpointStore.GetOrCreateAttemptId(
                        projectPath,
                        "jammr");
                PackageLifecycleCheckpointStore.Write(
                    projectPath,
                    new PackageLifecycleCheckpoint
                    {
                        aliasId = "jammr",
                        expectedCurrentReleaseRoot =
                            PackageLifecycleCoordinator.EmptyReleaseRoot,
                        operation = "install",
                        phase = phase,
                        runId = attemptId + "-execute",
                    });

                Assert.That(
                    PackageLifecycleCoordinator.GetPendingOperation(
                        projectPath,
                        "jammr"),
                    Is.EqualTo("install"));
                Assert.That(
                    PackageLifecycleCheckpointStore.TryGetAttemptId(
                        projectPath,
                        "jammr",
                        out string persisted),
                    Is.True);
                Assert.That(persisted, Is.EqualTo(attemptId));
            }
            finally
            {
                if (Directory.Exists(projectPath))
                {
                    Directory.Delete(projectPath, true);
                }
            }
        }

        [UnityTest]
        public IEnumerator VerifiedCheckpointResumeDoesNotReplayTheTransaction()
        {
            string projectPath = Path.Combine(
                Path.GetTempPath(),
                "yucp-verified-resume-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(projectPath);
                var checkpoint = new PackageLifecycleCheckpoint
                {
                    aliasId = "jammr",
                    expectedCurrentReleaseRoot =
                        PackageLifecycleCoordinator.EmptyReleaseRoot,
                    operation = "install",
                    phase = "verified",
                    runId = "verified-resume",
                    targetState = new PackageDeliveryInstallState
                    {
                        aliasId = "jammr",
                        releaseRoot = new string('2', 64),
                        versionId = "version-2",
                    },
                };
                MethodInfo method = typeof(PackageLifecycleCoordinator)
                    .GetMethod(
                        "CompleteCommittedCheckpointAsync",
                        BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null);

                var completion =
                    (Task<PackageLifecycleExecutionResult>)method.Invoke(
                        null,
                        new object[] { projectPath, checkpoint });

                while (!completion.IsCompleted)
                {
                    yield return null;
                }
                if (completion.IsFaulted)
                {
                    throw completion.Exception.GetBaseException();
                }
                Assert.That(
                    completion.Result.targetReleaseRoot,
                    Is.EqualTo(new string('2', 64)));
            }
            finally
            {
                if (Directory.Exists(projectPath))
                {
                    Directory.Delete(projectPath, true);
                }
            }
        }

        [Test]
        public void FreshAliasVersionSchedulesOnceAndStopsAfterCompletion()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string aliasId = "fresh-" + suffix;
            string packageName = "com.yucp.alias." + suffix;
            string packageJson =
                "{\"name\":\"" + packageName + "\"," +
                "\"version\":\"1.0.1\",\"displayName\":\"JAMMR\"," +
                "\"yucp\":{\"kind\":\"alias-v1\"," +
                "\"aliasId\":\"" + aliasId + "\"," +
                "\"installStrategy\":\"server-authorized\"," +
                "\"importerPackage\":\"com.yucp.importer\"}}";
            Assert.That(
                AliasPackageActivation.TryBuildActivation(
                    packageName,
                    packageJson,
                    out AliasPackageActivationRequest activation,
                    out string error),
                Is.True,
                error);
            string projectPath = Path.Combine(
                Path.GetTempPath(),
                "yucp-alias-1-0-1-" + suffix);

            try
            {
                Directory.CreateDirectory(projectPath);
                Assert.That(
                    AliasPackageActivation.ShouldScheduleForProject(
                        projectPath,
                        activation),
                    Is.True);
                Assert.That(
                    activation.Key,
                    Is.EqualTo(packageName + "@1.0.1:" + aliasId));

                AliasPackageActivationStateStore.MarkHandled(
                    projectPath,
                    activation.Alias,
                    "install");

                Assert.That(
                    AliasPackageActivation.ShouldScheduleForProject(
                        projectPath,
                        activation),
                    Is.False,
                    "A completed 1.0.1 alias must not schedule again.");
            }
            finally
            {
                if (Directory.Exists(projectPath))
                {
                    Directory.Delete(projectPath, true);
                }
            }
        }

        [Test]
        public void LifecycleProgressUsesFriendlyRealPhases()
        {
            MethodInfo method = typeof(PackageLifecycleCoordinator).GetMethod(
                "BuildProgress",
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            string[] phases =
            {
                "checking-access",
                "checking-package",
                "downloading",
                "verifying-files",
                "updating-project",
                "finishing",
            };
            float priorProgress = -1f;
            foreach (string phase in phases)
            {
                object result = method.Invoke(
                    null,
                    new object[] { phase, "JAMMR" });
                Assert.That(result, Is.Not.Null);
                Type type = result.GetType();
                string message = (string)type
                    .GetField("message")
                    .GetValue(result);
                float progress = (float)type
                    .GetField("progress")
                    .GetValue(result);

                Assert.That(message, Is.Not.Empty);
                Assert.That(message, Does.Not.Contain("staging"));
                Assert.That(message, Does.Not.Contain("digest"));
                Assert.That(message, Does.Not.Contain("Desync"));
                Assert.That(progress, Is.GreaterThan(priorProgress));
                priorProgress = progress;
            }
        }

        [Test]
        public void DownloadProgressShowsTransferredAndTotalBytes()
        {
            PackageLifecycleUserProgress progress =
                PackageLifecycleCoordinator.BuildBrokerProgress(
                    new NativePackageBrokerProgress
                    {
                        completedBytes = 1024 * 1024,
                        phase = "downloading",
                        totalBytes = 8 * 1024 * 1024,
                    },
                    "JAMMR",
                    false);

            Assert.That(progress.message, Does.Contain("1 MB"));
            Assert.That(progress.message, Does.Contain("8 MB"));
            Assert.That(progress.message, Does.Contain("13%"));
            Assert.That(progress.progress, Is.GreaterThan(0.42f));
        }

        [Test]
        public void ActiveContentReviewUsesFriendlySafetyLanguage()
        {
            MethodInfo method = typeof(PackageLifecycleCoordinator).GetMethod(
                "BuildActiveContentReview",
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            object result = method.Invoke(
                null,
                new object[]
                {
                    "JAMMR",
                    "active-content-policy-v1",
                    new string('a', 64),
                });
            Type type = result.GetType();
            string title = (string)type.GetField("title").GetValue(result);
            string message = (string)type.GetField("message").GetValue(result);

            Assert.That(title, Is.EqualTo("Review package safety"));
            Assert.That(message, Does.Contain("JAMMR"));
            Assert.That(message, Does.Contain("scripts"));
            Assert.That(message, Does.Not.Contain("Digest"));
            Assert.That(message, Does.Not.Contain("inventory"));
            Assert.That(message, Does.Not.Contain("policy"));
        }

        [Test]
        public void ActiveContentReviewIsRequiredOnlyForNewExecutableContent()
        {
            MethodInfo method = typeof(PackageLifecycleCoordinator).GetMethod(
                "RequiresActiveContentApproval",
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            const string emptyInventory =
                "edd1cf6ff50c01be6abf064f586597fa770c00026deff3c68b9faeb5a8db9aef";
            string changedInventory = new string('a', 64);

            Assert.That(
                method.Invoke(
                    null,
                    new object[]
                    {
                        emptyInventory,
                        "active-content-policy-v1",
                        null,
                    }),
                Is.False,
                "An empty executable-content inventory must not interrupt installation.");
            Assert.That(
                method.Invoke(
                    null,
                    new object[]
                    {
                        changedInventory,
                        "active-content-policy-v1",
                        new PackageDeliveryInstallState
                        {
                            activeContentDigest = changedInventory,
                            activePolicyVersion = "active-content-policy-v1",
                        },
                    }),
                Is.False,
                "An unchanged approved inventory must not interrupt an update.");
            Assert.That(
                method.Invoke(
                    null,
                    new object[]
                    {
                        changedInventory,
                        "active-content-policy-v1",
                        null,
                    }),
                Is.True,
                "New executable content must require an exact safety approval.");
            Assert.That(
                method.Invoke(
                    null,
                    new object[]
                    {
                        new string('b', 64),
                        "active-content-policy-v1",
                        new PackageDeliveryInstallState
                        {
                            activeContentDigest = changedInventory,
                            activePolicyVersion = "active-content-policy-v1",
                        },
                    }),
                Is.True,
                "Changed executable content must require a new safety approval.");
        }

        [Test]
        public void InstalledAliasOffersOwnershipAwareLifecycleActions()
        {
            MethodInfo method = typeof(PackageManagerWindow).GetMethod(
                "GetHostedLifecycleActionLabels",
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            string[] withRollback = (string[])method.Invoke(
                null,
                new object[] { true, true });
            string[] withoutRollback = (string[])method.Invoke(
                null,
                new object[] { true, false });
            string[] notInstalled = (string[])method.Invoke(
                null,
                new object[] { false, false });

            Assert.That(
                withRollback,
                Is.EqualTo(new[]
                {
                    "Repair",
                    "Roll back",
                    "Uninstall",
                }));
            Assert.That(
                withoutRollback,
                Is.EqualTo(new[]
                {
                    "Repair",
                    "Uninstall",
                }));
            Assert.That(notInstalled, Is.Empty);
            Assert.That(
                typeof(PackageLifecycleCoordinator).GetMethod(
                    "TryManageInstalledAsync",
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic),
                Is.Not.Null,
                "Hosted controls must use the ownership-aware lifecycle coordinator.");
        }

        [Test]
        public void ExplicitBootstrapLabelsOlderSpecificTargetsAsDowngrades()
        {
            Assert.That(
                PackageLifecycleCoordinator.BuildRequestedTargetLabel(
                    "2.4.0",
                    new BootstrapIntentContract
                    {
                        mode = "specific",
                        version = "2.3.1",
                    }),
                Is.EqualTo("Downgrade to 2.3.1"));
            Assert.That(
                PackageLifecycleCoordinator.BuildRequestedTargetLabel(
                    "2.4.0",
                    new BootstrapIntentContract
                    {
                        mode = "specific",
                        version = "2.5.0-beta.1",
                    }),
                Is.EqualTo("Update to 2.5.0-beta.1"));
            Assert.That(
                PackageLifecycleCoordinator.BuildRequestedTargetLabel(
                    "2.4.0",
                    new BootstrapIntentContract
                    {
                        mode = "latest",
                    }),
                Is.EqualTo(
                    "Latest stable resolved for this bootstrap"));
        }

        [Test]
        public void PendingLifecycleResumeRequiresTheRequestedOperation()
        {
            MethodInfo method = typeof(PackageLifecycleCoordinator).GetMethod(
                "PendingOperationMatches",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);
            Assert.That(
                method.Invoke(null, new object[] { "repair", "repair" }),
                Is.True);
            Assert.That(
                method.Invoke(null, new object[] { "install", "uninstall" }),
                Is.False);
        }

        [Test]
        public void CommittedCheckpointResultPreservesTheBrokerTrace()
        {
            var checkpoint = new PackageLifecycleCheckpoint
            {
                activeContentDigest = new string('1', 64),
                activePolicyVersion = "active-content-policy-v1",
                aliasId = "jammr",
                expectedCurrentReleaseRoot =
                    PackageLifecycleCoordinator.EmptyReleaseRoot,
                operation = "install",
                phase = "verified",
                runId = "trace-checkpoint",
                targetState = new PackageDeliveryInstallState
                {
                    aliasId = "jammr",
                    files = new List<NativePackageBrokerFile>(),
                    releaseRoot = new string('2', 64),
                    versionId = "version-2",
                },
            };
            FieldInfo traceField = typeof(PackageLifecycleCheckpoint).GetField(
                "brokerTraceId",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            MethodInfo buildResult = typeof(PackageLifecycleCoordinator)
                .GetMethod(
                    "BuildCheckpointResult",
                    BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(traceField, Is.Not.Null);
            Assert.That(buildResult, Is.Not.Null);
            traceField.SetValue(
                checkpoint,
                "0123456789abcdef0123456789abcdef");
            var result = (PackageLifecycleExecutionResult)buildResult.Invoke(
                null,
                new object[] { checkpoint });

            Assert.That(
                result.traceId,
                Is.EqualTo("0123456789abcdef0123456789abcdef"));
        }

        [Test]
        public void LicensedAssetLookupIsOwnedByEachImporterWindow()
        {
            FieldInfo pathsField = typeof(PackageManagerWindow).GetField(
                "_licensedAssetPaths",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo lookup = typeof(PackageManagerWindow).GetMethod(
                "IsLicensedAssetPath",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            MethodInfo list = typeof(PackageManagerWindow).GetMethod(
                "GetLicensedAssetPaths",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(pathsField, Is.Not.Null);
            Assert.That(pathsField.IsStatic, Is.False);
            Assert.That(lookup, Is.Not.Null);
            Assert.That(lookup.IsStatic, Is.False);
            Assert.That(list, Is.Not.Null);
            Assert.That(list.GetParameters(), Is.Empty);

            var first = ScriptableObject.CreateInstance<PackageManagerWindow>();
            var second = ScriptableObject.CreateInstance<PackageManagerWindow>();
            try
            {
                var firstPaths =
                    (HashSet<string>)pathsField.GetValue(first);
                firstPaths.Add("Assets/Product/Licensed.asset");

                Assert.That(
                    lookup.Invoke(
                        first,
                        new object[] { "Assets/Product/Licensed.asset" }),
                    Is.True);
                Assert.That(
                    lookup.Invoke(
                        second,
                        new object[] { "Assets/Product/Licensed.asset" }),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void SameReleaseOperationPreservesTheEarlierRollbackTarget()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "yucp-same-release-state-" + Guid.NewGuid().ToString("N"));
            const string aliasId = "jammr";
            string currentRelease = new string('2', 64);
            string previousRelease = new string('1', 64);
            try
            {
                var prior = new PackageDeliveryInstallState
                {
                    activeContentDigest = new string('a', 64),
                    activePolicyVersion = "active-content-policy-v1",
                    aliasId = aliasId,
                    files = new List<NativePackageBrokerFile>
                    {
                        new NativePackageBrokerFile
                        {
                            bytes = 7,
                            normalizedPath = "Assets/Product/file.txt",
                            sha256 = new string('b', 64),
                        },
                    },
                    previousActiveContentDigest = new string('c', 64),
                    previousActivePolicyVersion = "active-content-policy-v1",
                    previousReleaseRoot = previousRelease,
                    previousVersion = "1.0.0",
                    previousVersionId = "version-1",
                    previousFiles = new List<NativePackageBrokerFile>
                    {
                        new NativePackageBrokerFile
                        {
                            bytes = 6,
                            normalizedPath = "Assets/Product/file.txt",
                            sha256 = new string('c', 64),
                        },
                    },
                    releaseRoot = currentRelease,
                    version = "2.0.0",
                    versionId = "version-2",
                };
                MethodInfo write = typeof(PackageLifecycleCoordinator).GetMethod(
                    "WriteInstallState",
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo read = typeof(PackageLifecycleCoordinator).GetMethod(
                    "ReadInstallState",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(write, Is.Not.Null);
                Assert.That(read, Is.Not.Null);

                write.Invoke(
                    null,
                    new object[]
                    {
                        root,
                        aliasId,
                        currentRelease,
                        "version-2",
                        "2.0.0",
                        string.Empty,
                        string.Empty,
                        new string('a', 64),
                        "active-content-policy-v1",
                        prior.files,
                        prior,
                    });
                var state = (PackageDeliveryInstallState)read.Invoke(
                    null,
                    new object[] { root, aliasId, true });

                Assert.That(
                    state.previousReleaseRoot,
                    Is.EqualTo(previousRelease));
                Assert.That(
                    state.previousVersionId,
                    Is.EqualTo("version-1"));
                Assert.That(
                    state.previousVersion,
                    Is.EqualTo("1.0.0"));
                Assert.That(
                    state.previousFiles.Single().sha256,
                    Is.EqualTo(new string('c', 64)));
                Assert.That(
                    state.previousActiveContentDigest,
                    Is.EqualTo(new string('c', 64)));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [UnityTest]
        public IEnumerator PreJournalFailureRetriesFreshStagingAfterRestart(
            [Values("corrupt-file", "missing-tree")]
            string failureMode)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "yucp-pre-journal-retry-" + Guid.NewGuid().ToString("N"));
            string project = Path.Combine(root, "project");
            string staging = Path.Combine(root, "staging");
            string relativePath = ".yucp/product/file.txt";
            string stagedPath = Path.Combine(
                staging,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            const string runId = "pre-journal-retry";
            const string aliasId = "jammr";
            try
            {
                Directory.CreateDirectory(project);
                if (failureMode == "corrupt-file")
                {
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(stagedPath));
                    File.WriteAllText(stagedPath, "corrupt");
                }
                var expected = new VerifiedStagingFile
                {
                    bytes = "verified".Length,
                    normalizedPath = relativePath,
                    sha256 = Sha256(
                        System.Text.Encoding.UTF8.GetBytes("verified")),
                };
                var checkpoint = new PackageLifecycleCheckpoint
                {
                    aliasId = aliasId,
                    expectedCurrentReleaseRoot =
                        PackageLifecycleCoordinator.EmptyReleaseRoot,
                    operation = "install",
                    phase = "awaiting-transaction",
                    runId = runId,
                };
                PackageLifecycleCheckpointStore.Write(project, checkpoint);

                Exception failure = Assert.Catch<Exception>(() =>
                    ProjectTransactionJournal.Apply(
                        project,
                        staging,
                        runId,
                        new[] { expected },
                        Array.Empty<VerifiedStagingFile>()));
                if (failureMode == "corrupt-file")
                {
                    Assert.That(
                        failure,
                        Is.TypeOf<CryptographicException>());
                }
                else
                {
                    Assert.That(
                        failure,
                        Is.TypeOf<DirectoryNotFoundException>());
                }
                Assert.That(
                    ProjectTransactionJournal.TryInspect(
                        project,
                        runId,
                        out _),
                    Is.False);

                MethodInfo resume = typeof(PackageLifecycleCoordinator)
                    .GetMethod(
                        "TryResumeAsync",
                        BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(resume, Is.Not.Null);
                var resumed =
                    (Task<PackageLifecycleExecutionResult>)resume.Invoke(
                        null,
                        new object[]
                        {
                            project,
                            new AliasPackageContract
                            {
                                aliasId = aliasId,
                                packageDisplayName = "JAMMR",
                            },
                            "install",
                            runId,
                            PackageLifecycleCoordinator.EmptyReleaseRoot,
                            null,
                        });

                while (!resumed.IsCompleted)
                {
                    yield return null;
                }
                if (resumed.IsFaulted)
                {
                    throw resumed.Exception.GetBaseException();
                }
                Assert.That(resumed.Result, Is.Null);
                Assert.That(
                    PackageLifecycleCheckpointStore.TryRead(
                        project,
                        runId,
                        out _),
                    Is.False);

                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath));
                File.WriteAllText(stagedPath, "verified");
                ProjectTransactionResult retried =
                    ProjectTransactionJournal.Apply(
                        project,
                        staging,
                        runId,
                        new[] { expected },
                        Array.Empty<VerifiedStagingFile>());

                Assert.That(retried.state, Is.EqualTo("committed"));
                Assert.That(
                    File.ReadAllText(
                        Path.Combine(
                            project,
                            relativePath.Replace(
                                '/',
                                Path.DirectorySeparatorChar))),
                    Is.EqualTo("verified"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [UnityTest]
        public IEnumerator AwaitingCheckpointRecoversAnExistingJournal()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "yucp-awaiting-journal-" + Guid.NewGuid().ToString("N"));
            string project = Path.Combine(root, "project");
            string staging = Path.Combine(root, "staging");
            string relativePath = ".yucp/product/file.txt";
            string stagedPath = Path.Combine(
                staging,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            const string runId = "awaiting-existing-journal";
            try
            {
                Directory.CreateDirectory(project);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath));
                File.WriteAllText(stagedPath, "verified");
                var target = new VerifiedStagingFile
                {
                    bytes = new FileInfo(stagedPath).Length,
                    normalizedPath = relativePath,
                    sha256 = Sha256(File.ReadAllBytes(stagedPath)),
                };
                ProjectTransactionJournal.Prepare(
                    project,
                    staging,
                    runId,
                    new[] { target },
                    Array.Empty<VerifiedStagingFile>());
                var checkpoint = new PackageLifecycleCheckpoint
                {
                    aliasId = "jammr",
                    expectedCurrentReleaseRoot =
                        PackageLifecycleCoordinator.EmptyReleaseRoot,
                    operation = "install",
                    phase = "awaiting-transaction",
                    runId = runId,
                    targetState = new PackageDeliveryInstallState
                    {
                        aliasId = "jammr",
                        files = new List<NativePackageBrokerFile>
                        {
                            new NativePackageBrokerFile
                            {
                                bytes = target.bytes,
                                normalizedPath = target.normalizedPath,
                                sha256 = target.sha256,
                            },
                        },
                        releaseRoot = new string('2', 64),
                        versionId = "version-2",
                    },
                };
                PackageLifecycleCheckpointStore.Write(project, checkpoint);
                MethodInfo resume = typeof(PackageLifecycleCoordinator)
                    .GetMethod(
                        "TryResumeAsync",
                        BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(resume, Is.Not.Null);
                var completion =
                    (Task<PackageLifecycleExecutionResult>)resume.Invoke(
                        null,
                        new object[]
                        {
                            project,
                            new AliasPackageContract
                            {
                                aliasId = "jammr",
                                packageDisplayName = "JAMMR",
                            },
                            "install",
                            runId,
                            PackageLifecycleCoordinator.EmptyReleaseRoot,
                            null,
                        });

                while (!completion.IsCompleted)
                {
                    yield return null;
                }
                if (completion.IsFaulted)
                {
                    throw completion.Exception.GetBaseException();
                }

                Assert.That(
                    completion.Result.targetReleaseRoot,
                    Is.EqualTo(new string('2', 64)));
                Assert.That(
                    PackageLifecycleCheckpointStore.Read(
                        project,
                        runId).phase,
                    Is.EqualTo("verified"));
                Assert.That(
                    ProjectTransactionJournal.Inspect(project, runId).state,
                    Is.EqualTo("committed"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [UnityTest]
        public IEnumerator RollbackRecoveryPreservesModifiedOwnedFiles()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "yucp-rollback-preserved-" + Guid.NewGuid().ToString("N"));
            string project = Path.Combine(root, "project");
            string modifiedRelativePath = ".yucp/product/modified.txt";
            string restoredRelativePath = ".yucp/product/restored.txt";
            string modifiedPath = Path.Combine(
                project,
                modifiedRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            string restoredPath = Path.Combine(
                project,
                restoredRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            const string runId = "rollback-preserved-modification";
            try
            {
                Directory.CreateDirectory(
                    Path.GetDirectoryName(modifiedPath));
                File.WriteAllText(modifiedPath, "installed modified");
                File.WriteAllText(restoredPath, "installed restored");
                var modifiedOwned = new VerifiedStagingFile
                {
                    bytes = new FileInfo(modifiedPath).Length,
                    normalizedPath = modifiedRelativePath,
                    sha256 = Sha256(File.ReadAllBytes(modifiedPath)),
                };
                var restoredOwned = new VerifiedStagingFile
                {
                    bytes = new FileInfo(restoredPath).Length,
                    normalizedPath = restoredRelativePath,
                    sha256 = Sha256(File.ReadAllBytes(restoredPath)),
                };
                File.WriteAllText(modifiedPath, "user modification");
                ProjectTransactionJournal.RemoveOwnedFiles(
                    project,
                    runId,
                    new[] { modifiedOwned, restoredOwned });
                var checkpoint = new PackageLifecycleCheckpoint
                {
                    aliasId = "jammr",
                    errorMessage =
                        "Unity rejected the uninstall. The prior project was restored.",
                    expectedCurrentReleaseRoot = new string('1', 64),
                    operation = "uninstall",
                    phase = "rolling-back",
                    priorState = new PackageDeliveryInstallState
                    {
                        aliasId = "jammr",
                        files = new List<NativePackageBrokerFile>
                        {
                            new NativePackageBrokerFile
                            {
                                bytes = modifiedOwned.bytes,
                                normalizedPath =
                                    modifiedOwned.normalizedPath,
                                sha256 = modifiedOwned.sha256,
                            },
                            new NativePackageBrokerFile
                            {
                                bytes = restoredOwned.bytes,
                                normalizedPath =
                                    restoredOwned.normalizedPath,
                                sha256 = restoredOwned.sha256,
                            },
                        },
                        releaseRoot = new string('1', 64),
                    },
                    runId = runId,
                };
                PackageLifecycleCheckpointStore.Write(project, checkpoint);
                MethodInfo complete = typeof(PackageLifecycleCoordinator)
                    .GetMethod(
                        "CompleteCommittedCheckpointAsync",
                        BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(complete, Is.Not.Null);
                var completion =
                    (Task<PackageLifecycleExecutionResult>)complete.Invoke(
                        null,
                        new object[] { project, checkpoint });

                while (!completion.IsCompleted)
                {
                    yield return null;
                }

                Assert.That(completion.IsFaulted, Is.True);
                Assert.That(
                    completion.Exception.GetBaseException(),
                    Is.TypeOf<InvalidDataException>());
                Assert.That(
                    File.ReadAllText(modifiedPath),
                    Is.EqualTo("user modification"));
                Assert.That(
                    File.ReadAllText(restoredPath),
                    Is.EqualTo("installed restored"));
                Assert.That(
                    ProjectTransactionJournal.Inspect(project, runId).state,
                    Is.EqualTo("rolled-back"));
                Assert.That(
                    PackageLifecycleCheckpointStore.Read(
                        project,
                        runId).phase,
                    Is.EqualTo("rolled-back"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [UnityTest]
        public IEnumerator PreparedUninstallResumeSnapshotsAndRemovesAUserModifiedFile()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "yucp-uninstall-resume-" + Guid.NewGuid().ToString("N"));
            string project = Path.Combine(root, "project");
            string staging = Path.Combine(root, "staging");
            string ownedRelativePath = ".yucp/product/owned.txt";
            string sentinelRelativePath = ".yucp/product/sentinel.txt";
            string ownedPath = Path.Combine(
                project,
                ownedRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            string sentinelPath = Path.Combine(
                staging,
                sentinelRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            const string runId = "resume-prepared-uninstall";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ownedPath));
                Directory.CreateDirectory(Path.GetDirectoryName(sentinelPath));
                File.WriteAllText(ownedPath, "installed");
                File.WriteAllText(sentinelPath, "transaction fixture");
                var owned = new VerifiedStagingFile
                {
                    bytes = new FileInfo(ownedPath).Length,
                    normalizedPath = ownedRelativePath,
                    sha256 = Sha256(File.ReadAllBytes(ownedPath)),
                };
                var sentinel = new VerifiedStagingFile
                {
                    bytes = new FileInfo(sentinelPath).Length,
                    normalizedPath = sentinelRelativePath,
                    sha256 = Sha256(File.ReadAllBytes(sentinelPath)),
                };
                ProjectTransactionJournal.Prepare(
                    project,
                    staging,
                    runId,
                    new[] { sentinel },
                    new[] { owned });
                File.WriteAllText(ownedPath, "user modification");
                var checkpoint = new PackageLifecycleCheckpoint
                {
                    aliasId = "jammr",
                    expectedCurrentReleaseRoot = new string('1', 64),
                    operation = "uninstall",
                    priorState = new PackageDeliveryInstallState
                    {
                        aliasId = "jammr",
                        files = new List<NativePackageBrokerFile>
                        {
                            new NativePackageBrokerFile
                            {
                                bytes = owned.bytes,
                                normalizedPath = owned.normalizedPath,
                                sha256 = owned.sha256,
                            },
                        },
                        releaseRoot = new string('1', 64),
                    },
                    runId = runId,
                };
                PackageLifecycleCheckpointStore.Write(project, checkpoint);
                MethodInfo method = typeof(PackageLifecycleCoordinator)
                    .GetMethod(
                        "CompleteCommittedCheckpointAsync",
                        BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null);

                var completion =
                    (Task<PackageLifecycleExecutionResult>)method.Invoke(
                        null,
                        new object[] { project, checkpoint });
                while (!completion.IsCompleted)
                {
                    yield return null;
                }
                if (completion.IsFaulted)
                {
                    throw completion.Exception.GetBaseException();
                }

                Assert.That(File.Exists(ownedPath), Is.False);
                ProjectTransactionInspection inspection =
                    ProjectTransactionJournal.Inspect(project, runId);
                Assert.That(inspection.state, Is.EqualTo("committed"));
                Assert.That(
                    inspection.preservedModifiedFiles,
                    Has.Exactly(1).Matches<VerifiedStagingFile>(
                        file => file.normalizedPath == ownedRelativePath));
                Assert.That(
                    PackageLifecycleCheckpointStore.Read(project, runId).phase,
                    Is.EqualTo("verified"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private static System.Array CreateImportItems(
            Dictionary<string, byte[]> entries,
            string root)
        {
            Type itemType = Type.GetType(
                "UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
            Assert.That(itemType, Is.Not.Null);
            FieldInfo destinationPath = itemType.GetField("destinationAssetPath");
            FieldInfo sourceFolder = itemType.GetField("sourceFolder");
            FieldInfo exportedPath = itemType.GetField("exportedAssetPath");
            Assert.That(destinationPath, Is.Not.Null);
            Assert.That(sourceFolder, Is.Not.Null);
            Assert.That(exportedPath, Is.Not.Null);

            System.Array items = System.Array.CreateInstance(
                itemType,
                entries.Count);
            int index = 0;
            foreach (KeyValuePair<string, byte[]> entry in entries)
            {
                string source = Path.Combine(root, index.ToString("D2"));
                Directory.CreateDirectory(source);
                File.WriteAllBytes(Path.Combine(source, "asset"), entry.Value);
                object item = Activator.CreateInstance(itemType, true);
                destinationPath.SetValue(item, entry.Key);
                sourceFolder.SetValue(item, source);
                exportedPath.SetValue(item, Path.GetFileName(entry.Key));
                items.SetValue(item, index++);
            }
            return items;
        }

        private static string Sha256(byte[] value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(value))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static PackageMetadata OwnedAliasMetadata(
            string textureNamePrefix)
        {
            return new PackageMetadata("JAMMR")
            {
                aliasPackage = new AliasPackageContract
                {
                    aliasId = "jammr",
                    installStrategy =
                        AliasPackageDiscovery
                            .ServerAuthorizedInstallStrategy,
                    kind = "alias-v1",
                    importerPackage = "com.yucp.importer",
                    packageDisplayName = "JAMMR",
                    packageName = "com.yucp.alias.jammr",
                    packageVersion = "1.0.0",
                },
                banner = OwnedTexture(textureNamePrefix + " banner"),
                icon = OwnedTexture(textureNamePrefix + " icon"),
            };
        }

        private static Texture2D OwnedTexture(string textureName)
        {
            return new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = textureName,
            };
        }

        private static int CountTextures(string namePrefix)
        {
            return Resources.FindObjectsOfTypeAll<Texture2D>()
                .Count(texture =>
                    texture != null &&
                    texture.name.StartsWith(
                        namePrefix,
                        StringComparison.Ordinal));
        }
    }
}
