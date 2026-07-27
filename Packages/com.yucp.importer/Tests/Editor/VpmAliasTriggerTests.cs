using NUnit.Framework;
using System;
using System.Collections;
using System.IO;
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
                Assert.That(window.PrimaryActionLabel, Is.EqualTo("Verify and Import"));
                Assert.That(window.HasPackageImportItems, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
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
                Assert.That(restored.PrimaryActionLabel, Is.EqualTo("Verify and Import"));
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
                    "Update",
                    "Repair",
                    "Roll back",
                    "Uninstall",
                }));
            Assert.That(
                withoutRollback,
                Is.EqualTo(new[]
                {
                    "Update",
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

        [UnityTest]
        public IEnumerator PreparedUninstallResumePreservesAUserModifiedFile()
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

                Assert.That(
                    File.ReadAllText(ownedPath),
                    Is.EqualTo("user modification"));
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

        private static string Sha256(byte[] value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(value))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }
    }
}
