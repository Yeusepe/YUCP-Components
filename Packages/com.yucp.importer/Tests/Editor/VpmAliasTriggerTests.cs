using NUnit.Framework;
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine.UIElements;
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
                "\"importerPackage\":\"com.yucp.importer\"," +
                "\"catalogProductIds\":[\"jinxxy-jammr\",\"gumroad-jammr\"]}}";

            bool built = AliasPackageActivation.TryBuildActivation(
                "com.yucp.jammr.alias",
                packageJson,
                out AliasPackageActivationRequest activation,
                out string error);

            Assert.That(built, Is.True, error);
            Assert.That(activation.Alias.aliasId, Is.EqualTo("jammr"));
            Assert.That(activation.ActionLabel, Is.EqualTo("Verify and Import"));
            Assert.That(
                activation.CatalogProductIds,
                Is.EqualTo(new[] { "gumroad-jammr", "jinxxy-jammr" }));
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
                "\"importerPackage\":\"com.yucp.importer\"," +
                "\"catalogProductIds\":[\"jinxxy-jammr\"]}}";
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
                "\"importerPackage\":\"com.yucp.importer\"," +
                "\"catalogProductIds\":[\"jinxxy-jammr\"]}}";
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
                "\"importerPackage\":\"com.yucp.importer\"," +
                "\"catalogProductIds\":[\"jinxxy-jammr\"]}}";
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
                "\"importerPackage\":\"com.yucp.importer\"," +
                "\"catalogProductIds\":[\"jinxxy-jammr\"]}}";
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
        public void PackageInstallationDoesNotTrustAnUnscopedSignInHint()
        {
            Assert.That(
                CreatorIdentityOAuthService.PackageInstallationScopes,
                Is.EqualTo(new[]
                {
                    "verification:read",
                    "products:read",
                }));
            Assert.That(
                PackageManagerWindow.HasPackageInstallationAuthorization(
                    true,
                    null),
                Is.False);
            Assert.That(
                PackageManagerWindow.HasPackageInstallationAuthorization(
                    true,
                    "scoped-access-token"),
                Is.True);
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
        public void LifecycleCompletionRemovesOnlyTheVpmAliasBootstrap()
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
                    "\"importerPackage\":\"com.yucp.importer\"," +
                    "\"catalogProductIds\":[\"catalog-test\"]}}");
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
                Assert.That(Directory.Exists(aliasPath), Is.False);
                Assert.That(Directory.Exists(importerPath), Is.True);
                string manifest = File.ReadAllText(
                    Path.Combine(packagesPath, "vpm-manifest.json"));
                Assert.That(manifest, Does.Not.Contain(aliasName));
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
    }
}
