using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager;
using YUCP.Importer.Editor.PackageVerifier.Data;

namespace YUCP.Importer.Editor.Tests
{
    public class ProtectedImportBootstrapTests
    {
        private string _testAssetRoot;
        private string _tempPackagePath;
        private object _packageImportWizardInstance;
        private FieldInfo _packageImportWizardPathField;
        private string _previousPackageImportWizardPath;
        private readonly List<string> _additionalCleanupAssetPaths = new List<string>();

        [TearDown]
        public void TearDown()
        {
            RestorePackageImportWizardPath();
            DeleteTestAssets();
            TryDeleteFile(_tempPackagePath);
            EditorPrefs.DeleteKey("YUCP.PendingProtectedImportBootstrap");
        }

        [Test]
        public void DirectVpmInstaller_TryGetCurrentImportPackagePath_ReadsUnityImportWizardState()
        {
            _tempPackagePath = Path.Combine(Path.GetTempPath(), $"yucp-bootstrap-{Guid.NewGuid():N}.unitypackage");
            File.WriteAllBytes(_tempPackagePath, new byte[] { 1, 2, 3, 4 });

            CapturePackageImportWizardState();
            Assert.That(_packageImportWizardInstance, Is.Not.Null);
            Assert.That(_packageImportWizardPathField, Is.Not.Null);

            _packageImportWizardPathField.SetValue(_packageImportWizardInstance, _tempPackagePath);

            string observedPath = InvokeDirectInstallerTryGetCurrentImportPackagePath();
            Assert.That(observedPath, Is.EqualTo(_tempPackagePath));
        }

        [Test]
        public void ProtectedImportBootstrapCoordinator_ReconstructsInstalledPackageInfo_FromImportedShell()
        {
            string rootAssetPath = CreateImportedShell();
            string metadataAssetPath = $"{rootAssetPath}/YUCP_PackageInfo.json";
            string tempInstallAssetPath = $"{rootAssetPath}/_temp/YUCP_TempInstall_Test.json";
            string protectedPayloadAssetPath = $"{rootAssetPath}/YUCP_ProtectedPayload.json";

            object state = CreatePendingProtectedImportState(
                packageName: "Protected Shell",
                shellRootAssetPath: rootAssetPath,
                tempInstallAssetPath: tempInstallAssetPath,
                metadataAssetPath: metadataAssetPath,
                protectedPayloadAssetPath: protectedPayloadAssetPath,
                originalPackagePath: string.Empty);

            MethodInfo reconstructMethod = GetEditorType("YUCP.Importer.Editor.PackageManager.Core.ProtectedImportBootstrapCoordinator")
                .GetMethod(
                    "TryReconstructInstalledPackageInfo",
                    BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(reconstructMethod, Is.Not.Null);

            object[] args = { state, null, null };
            bool success = (bool)reconstructMethod.Invoke(null, args);
            string error = args[2] as string;

            Assert.That(success, Is.True, error ?? "Expected shell reconstruction to succeed.");

            var packageInfo = args[1] as InstalledPackageInfo;
            Assert.That(packageInfo, Is.Not.Null);
            Assert.That(packageInfo.packageId, Is.EqualTo("pkg-test-123"));
            Assert.That(packageInfo.archiveSha256, Is.EqualTo("archive-sha-123"));
            Assert.That(packageInfo.publisherId, Is.EqualTo("publisher-123"));
            Assert.That(packageInfo.isVerified, Is.False);
            Assert.That(packageInfo.packageName, Is.EqualTo("Protected Shell"));
            Assert.That(packageInfo.version, Is.EqualTo("1.2.3"));
            Assert.That(packageInfo.author, Is.EqualTo("YUCP"));
            Assert.That(packageInfo.description, Is.EqualTo("Protected shell metadata."));
            Assert.That(packageInfo.tagline, Is.EqualTo("Importer bootstrap test"));
            Assert.That(packageInfo.category, Is.EqualTo("Avatar"));
            Assert.That(packageInfo.minimumUnityVersion, Is.EqualTo("2022.3"));
            Assert.That(packageInfo.creatorNote, Is.EqualTo("Creator note"));
            Assert.That(packageInfo.releaseNotes, Is.EqualTo("Release notes"));
            Assert.That(packageInfo.exportDate, Is.EqualTo("2026-03-25T00:00:00Z"));
            Assert.That(packageInfo.icon, Is.Not.Null);
            Assert.That(packageInfo.banner, Is.Not.Null);
            Assert.That(packageInfo.galleryImages, Has.Count.EqualTo(1));
            Assert.That(packageInfo.galleryImages[0], Is.Not.Null);
            Assert.That(packageInfo.productLinks, Has.Count.EqualTo(1));
            Assert.That(packageInfo.productLinks[0].label, Is.EqualTo("Store"));
            Assert.That(packageInfo.productLinks[0].url, Is.EqualTo("https://example.invalid/product"));
            Assert.That(packageInfo.productLinks[0].customIcon, Is.Not.Null);
            Assert.That(packageInfo.licensePackages, Has.Count.EqualTo(1));
            Assert.That(packageInfo.licensePackages[0].packageId, Is.EqualTo("license-package"));
            Assert.That(packageInfo.licensePackages[0].productId, Is.EqualTo("gumroad-product"));
            Assert.That(packageInfo.licensePackages[0].creatorAuthUserId, Is.EqualTo("creator-auth-user"));
            Assert.That(packageInfo.dependencies["com.yucp.importer"], Is.EqualTo("1.0.0"));
            Assert.That(packageInfo.dependencies["com.example.extra"], Is.EqualTo("2.0.0"));
            Assert.That(packageInfo.protectedPayload, Is.Not.Null);
            Assert.That(packageInfo.protectedPayload.protectedAssetId, Is.EqualTo("protected-asset-123"));
            Assert.That(packageInfo.protectedPayload.blobAssetPath, Is.EqualTo($"{rootAssetPath}/Protected/payload.blob"));
            Assert.That(packageInfo.installedFiles, Has.Some.EqualTo($"{rootAssetPath}/_Signing/PackageManifest.json"));
            Assert.That(packageInfo.installedFiles, Has.Some.EqualTo($"{rootAssetPath}/Protected/payload.blob"));
            Assert.That(packageInfo.installedFiles.Any(path => path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)), Is.False);
        }

        [Test]
        public void ProtectedImportBootstrapCoordinator_ReconstructsInstalledPackageInfo_WhenSignedManifestLivesInGlobalSigningFolder()
        {
            string rootAssetPath = CreateImportedShell(useGlobalSigningManifest: true);
            string metadataAssetPath = $"{rootAssetPath}/YUCP_PackageInfo.json";
            string tempInstallAssetPath = $"{rootAssetPath}/_temp/YUCP_TempInstall_Test.json";
            string protectedPayloadAssetPath = $"{rootAssetPath}/YUCP_ProtectedPayload.json";

            object state = CreatePendingProtectedImportState(
                packageName: "Protected Shell",
                shellRootAssetPath: rootAssetPath,
                tempInstallAssetPath: tempInstallAssetPath,
                metadataAssetPath: metadataAssetPath,
                protectedPayloadAssetPath: protectedPayloadAssetPath,
                originalPackagePath: string.Empty);

            MethodInfo reconstructMethod = GetEditorType("YUCP.Importer.Editor.PackageManager.Core.ProtectedImportBootstrapCoordinator")
                .GetMethod(
                    "TryReconstructInstalledPackageInfo",
                    BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(reconstructMethod, Is.Not.Null);

            object[] args = { state, null, null };
            bool success = (bool)reconstructMethod.Invoke(null, args);
            string error = args[2] as string;

            Assert.That(success, Is.True, error ?? "Expected shell reconstruction to succeed with a global signing manifest.");

            var packageInfo = args[1] as InstalledPackageInfo;
            Assert.That(packageInfo, Is.Not.Null);
            Assert.That(packageInfo.installedFiles, Has.Some.EqualTo("Assets/_Signing/PackageManifest.json"));
            Assert.That(packageInfo.installedFiles, Has.Some.EqualTo("Assets/_Signing/PackageManifest.sig"));
            Assert.That(packageInfo.installedFiles, Has.Some.EqualTo($"{rootAssetPath}/YUCP_ProtectedPayload.json"));
        }

        [Test]
        public void ProtectedImportBootstrapCoordinator_FailsWhenProtectedPayloadDescriptorDoesNotMatchSignedManifest()
        {
            string rootAssetPath = CreateImportedShell();
            WriteManifest(
                rootAssetPath,
                new ProtectedPayloadManifestEntry
                {
                    formatVersion = "1",
                    protectedAssetId = "protected-asset-123",
                    blobAssetPath = $"{rootAssetPath}/Protected/payload.blob",
                    cipher = "aes-256-cbc+hmac-sha256",
                    archiveFormat = "zip",
                    ciphertextSha256 = "tampered-ciphertext-sha",
                    plaintextSha256 = "plaintext-sha",
                    payloadAssetPaths = new[] { "Assets/Protected/source.prefab" },
                    requiresOnlineUnlock = true,
                    requiresBrokeredMaterialization = true,
                    brokerProtocolVersion = 1,
                    manifestBindingSha256 = "tampered-binding",
                });

            object state = CreatePendingProtectedImportState(
                packageName: "Protected Shell",
                shellRootAssetPath: rootAssetPath,
                tempInstallAssetPath: $"{rootAssetPath}/_temp/YUCP_TempInstall_Test.json",
                metadataAssetPath: $"{rootAssetPath}/YUCP_PackageInfo.json",
                protectedPayloadAssetPath: $"{rootAssetPath}/YUCP_ProtectedPayload.json",
                originalPackagePath: string.Empty);

            MethodInfo reconstructMethod = GetEditorType("YUCP.Importer.Editor.PackageManager.Core.ProtectedImportBootstrapCoordinator")
                .GetMethod(
                    "TryReconstructInstalledPackageInfo",
                    BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(reconstructMethod, Is.Not.Null);

            object[] args = { state, null, null };
            bool success = (bool)reconstructMethod.Invoke(null, args);
            string error = args[2] as string;

            Assert.That(success, Is.False);
            StringAssert.Contains("signed payload descriptor", error ?? string.Empty);
        }

        [Test]
        public void PackageManagerWindow_CancelProtectedResume_CleansImportedShellAndPendingApplyState()
        {
            string rootAssetPath = CreateImportedShell();
            InstalledPackageInfo packageInfo = ReconstructInstalledPackageInfo(rootAssetPath);
            Assert.That(packageInfo, Is.Not.Null);

            var registry = InstalledPackageRegistry.GetOrCreate();
            registry.RegisterPackage(packageInfo);

            EditorPrefs.SetString("YUCP.PackageManager.ProtectedPayload.PackageId", packageInfo.packageId);
            EditorPrefs.SetString("YUCP.PackageManager.ProtectedPayload.StartTicksUtc", DateTime.UtcNow.Ticks.ToString());

            MethodInfo cleanupMethod = typeof(PackageManagerWindow).GetMethod(
                "TryCleanupCancelledProtectedResume",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(cleanupMethod, Is.Not.Null);

            object[] args = { packageInfo, null };
            bool success = (bool)cleanupMethod.Invoke(null, args);
            string error = args[1] as string;

            Assert.That(success, Is.True, error ?? "Expected protected resume cancel cleanup to succeed.");
            Assert.That(EditorPrefs.GetString("YUCP.PackageManager.ProtectedPayload.PackageId", string.Empty), Is.Empty);
            Assert.That(EditorPrefs.GetString("YUCP.PackageManager.ProtectedPayload.StartTicksUtc", string.Empty), Is.Empty);
            Assert.That(registry.GetPackage(packageInfo.packageId), Is.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(rootAssetPath), Is.Null);
        }

        [Test]
        public void PackageMetadataExtractor_GetPackageJsonDestinationPath_PrefersTempInstallDescriptorOverContainerPackageJson()
        {
            Type importPackageItemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
            Assert.That(importPackageItemType, Is.Not.Null);

            FieldInfo destinationAssetPathField = importPackageItemType.GetField("destinationAssetPath");
            Assert.That(destinationAssetPathField, Is.Not.Null);

            Array importItems = Array.CreateInstance(importPackageItemType, 2);
            object containerPackageJson = FormatterServices.GetUninitializedObject(importPackageItemType);
            object tempInstallPackageJson = FormatterServices.GetUninitializedObject(importPackageItemType);

            destinationAssetPathField.SetValue(containerPackageJson, "Packages/yucp.installed-packages/package.json");
            destinationAssetPathField.SetValue(tempInstallPackageJson, "Packages/yucp.installed-packages/Wasbeer/_temp/YUCP_TempInstall_Test.json");

            importItems.SetValue(containerPackageJson, 0);
            importItems.SetValue(tempInstallPackageJson, 1);

            MethodInfo getPackageJsonDestinationPath = typeof(PackageMetadataExtractor).GetMethod(
                "GetPackageJsonDestinationPath",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(getPackageJsonDestinationPath, Is.Not.Null);

            string destinationPath = getPackageJsonDestinationPath.Invoke(null, new object[] { importItems }) as string;
            Assert.That(destinationPath, Is.EqualTo("Packages/yucp.installed-packages/Wasbeer/_temp/YUCP_TempInstall_Test.json"));
        }

        [Test]
        public void ExtractProtectedImportIntentDescriptor_PrefersInstalledShellAsset_WhenAvailable()
        {
            string rootAssetPath = CreateImportedShell();
            string intentAssetPath = $"{rootAssetPath}/YUCP_ProtectedImportIntent.json";
            string protectedPayloadAssetPath = $"{rootAssetPath}/YUCP_ProtectedPayload.json";
            string tempInstallAssetPath = $"{rootAssetPath}/_temp/YUCP_TempInstall_Test.json";

            var protectedPayload = PackageMetadataExtractor.ExtractProtectedPayloadDescriptorFromAssetPath(protectedPayloadAssetPath);
            Assert.That(protectedPayload, Is.Not.Null);

            File.WriteAllText(
                GetAssetDiskPath(intentAssetPath),
                JsonUtility.ToJson(new ProtectedImportIntentDescriptor
                {
                    formatVersion = "1",
                    packageId = "pkg-test-123",
                    protectedAssetId = protectedPayload.protectedAssetId,
                    protectedPayloadAssetPath = protectedPayloadAssetPath,
                    tempInstallAssetPath = tempInstallAssetPath,
                    manifestBindingSha256 = protectedPayload.manifestBindingSha256,
                    requiresProtectedPayload = true,
                }, true));

            AssetDatabase.Refresh();

            Type importPackageItemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
            Assert.That(importPackageItemType, Is.Not.Null);

            FieldInfo destinationAssetPathField = importPackageItemType.GetField("destinationAssetPath");
            FieldInfo sourceFolderField = importPackageItemType.GetField("sourceFolder");
            FieldInfo exportedAssetPathField = importPackageItemType.GetField("exportedAssetPath");
            Assert.That(destinationAssetPathField, Is.Not.Null);
            Assert.That(sourceFolderField, Is.Not.Null);
            Assert.That(exportedAssetPathField, Is.Not.Null);

            Array importItems = Array.CreateInstance(importPackageItemType, 1);
            object intentItem = FormatterServices.GetUninitializedObject(importPackageItemType);
            destinationAssetPathField.SetValue(intentItem, intentAssetPath);
            sourceFolderField.SetValue(intentItem, "Temp/Export Package/missing/intent");
            exportedAssetPathField.SetValue(intentItem, "asset");
            importItems.SetValue(intentItem, 0);

            ProtectedImportIntentDescriptor descriptor =
                PackageMetadataExtractor.ExtractProtectedImportIntentDescriptor(
                    importItems,
                    new[] { intentAssetPath, protectedPayloadAssetPath, tempInstallAssetPath });

            Assert.That(descriptor, Is.Not.Null);
            Assert.That(descriptor.packageId, Is.EqualTo("pkg-test-123"));
            Assert.That(descriptor.protectedAssetId, Is.EqualTo(protectedPayload.protectedAssetId));
            Assert.That(descriptor.protectedPayloadAssetPath, Is.EqualTo(protectedPayloadAssetPath));
            Assert.That(descriptor.tempInstallAssetPath, Is.EqualTo(tempInstallAssetPath));
            Assert.That(descriptor.manifestBindingSha256, Is.EqualTo(protectedPayload.manifestBindingSha256));
        }

        private void CapturePackageImportWizardState()
        {
            var unityEditorAssembly = typeof(UnityEditor.Editor).Assembly;
            Type wizardType = unityEditorAssembly.GetType("UnityEditor.PackageImportWizard", false);
            Assert.That(wizardType, Is.Not.Null);

            Type singletonType = unityEditorAssembly.GetType("UnityEditor.ScriptableSingleton`1", false);
            Assert.That(singletonType, Is.Not.Null);

            Type genericSingletonType = singletonType.MakeGenericType(wizardType);
            PropertyInfo instanceProperty = genericSingletonType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
            Assert.That(instanceProperty, Is.Not.Null);

            _packageImportWizardInstance = instanceProperty.GetValue(null);
            _packageImportWizardPathField = wizardType.GetField("m_PackagePath", BindingFlags.NonPublic | BindingFlags.Instance);
            _previousPackageImportWizardPath = _packageImportWizardPathField?.GetValue(_packageImportWizardInstance) as string;
        }

        private void RestorePackageImportWizardPath()
        {
            if (_packageImportWizardInstance == null || _packageImportWizardPathField == null)
                return;

            _packageImportWizardPathField.SetValue(_packageImportWizardInstance, _previousPackageImportWizardPath);
        }

        private string InvokeDirectInstallerTryGetCurrentImportPackagePath()
        {
            MethodInfo method = GetLoadedType("YUCP.DirectVpmInstaller.DirectVpmInstaller")
                .GetMethod("TryGetCurrentImportPackagePath", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, null) as string;
        }

        private object CreatePendingProtectedImportState(
            string packageName,
            string shellRootAssetPath,
            string tempInstallAssetPath,
            string metadataAssetPath,
            string protectedPayloadAssetPath,
            string originalPackagePath)
        {
            Type coordinatorType = GetEditorType("YUCP.Importer.Editor.PackageManager.Core.ProtectedImportBootstrapCoordinator");
            Type stateType = coordinatorType.GetNestedType("PendingProtectedImportState", BindingFlags.NonPublic);
            Assert.That(stateType, Is.Not.Null);

            object state = Activator.CreateInstance(stateType, true);
            SetField(stateType, state, "packageName", packageName);
            SetField(stateType, state, "shellRootAssetPath", shellRootAssetPath);
            SetField(stateType, state, "tempInstallAssetPath", tempInstallAssetPath);
            SetField(stateType, state, "metadataAssetPath", metadataAssetPath);
            SetField(stateType, state, "protectedPayloadAssetPath", protectedPayloadAssetPath);
            SetField(stateType, state, "originalPackagePath", originalPackagePath);
            return state;
        }

        private static void SetField(Type type, object instance, string fieldName, object value)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, $"Expected field '{fieldName}' on '{type.FullName}'.");
            field.SetValue(instance, value);
        }

        private InstalledPackageInfo ReconstructInstalledPackageInfo(string rootAssetPath)
        {
            object state = CreatePendingProtectedImportState(
                packageName: "Protected Shell",
                shellRootAssetPath: rootAssetPath,
                tempInstallAssetPath: $"{rootAssetPath}/_temp/YUCP_TempInstall_Test.json",
                metadataAssetPath: $"{rootAssetPath}/YUCP_PackageInfo.json",
                protectedPayloadAssetPath: $"{rootAssetPath}/YUCP_ProtectedPayload.json",
                originalPackagePath: string.Empty);

            MethodInfo reconstructMethod = GetEditorType("YUCP.Importer.Editor.PackageManager.Core.ProtectedImportBootstrapCoordinator")
                .GetMethod(
                    "TryReconstructInstalledPackageInfo",
                    BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(reconstructMethod, Is.Not.Null);

            object[] args = { state, null, null };
            bool success = (bool)reconstructMethod.Invoke(null, args);
            string error = args[2] as string;

            Assert.That(success, Is.True, error ?? "Expected shell reconstruction to succeed.");
            return args[1] as InstalledPackageInfo;
        }

        private string CreateImportedShell(bool useGlobalSigningManifest = false)
        {
            _testAssetRoot = $"Assets/YUCP_TempTests/ProtectedBootstrap_{Guid.NewGuid():N}";
            string rootDiskPath = GetAssetDiskPath(_testAssetRoot);

            Directory.CreateDirectory(rootDiskPath);
            Directory.CreateDirectory(Path.Combine(rootDiskPath, "_temp"));
            Directory.CreateDirectory(Path.Combine(rootDiskPath, "Protected"));

            WritePng(Path.Combine(rootDiskPath, "icon.png"), Color.cyan);
            WritePng(Path.Combine(rootDiskPath, "banner.png"), Color.magenta);
            WritePng(Path.Combine(rootDiskPath, "gallery.png"), Color.yellow);
            WritePng(Path.Combine(rootDiskPath, "link.png"), Color.green);
            File.WriteAllBytes(Path.Combine(rootDiskPath, "Protected", "payload.blob"), new byte[] { 9, 8, 7, 6 });

            File.WriteAllText(
                Path.Combine(rootDiskPath, "YUCP_PackageInfo.json"),
                "{"
                + "\"packageName\":\"Protected Shell\","
                + "\"version\":\"1.2.3\","
                + "\"author\":\"YUCP\","
                + "\"description\":\"Protected shell metadata.\","
                + "\"icon\":\"icon.png\","
                + "\"banner\":\"banner.png\","
                + "\"productLinks\":[{\"label\":\"Store\",\"url\":\"https://example.invalid/product\",\"icon\":\"link.png\"}],"
                + "\"versionRule\":\"semver\","
                + "\"versionRuleName\":\"Semantic Versioning\","
                + "\"licensePackages\":[{\"packageId\":\"license-package\",\"packageName\":\"License Package\",\"productId\":\"gumroad-product\",\"creatorAuthUserId\":\"creator-auth-user\"}],"
                + "\"tagline\":\"Importer bootstrap test\","
                + "\"category\":\"Avatar\","
                + "\"supportedPlatforms\":[\"Standalone\"],"
                + "\"minimumUnityVersion\":\"2022.3\","
                + "\"creatorNote\":\"Creator note\","
                + "\"releaseNotes\":\"Release notes\","
                + "\"galleryImages\":[\"gallery.png\"],"
                + "\"tags\":[\"one\",\"two\"],"
                + "\"totalFileCount\":4,"
                + "\"totalFileSize\":1234,"
                + "\"assetBreakdown\":[{\"type\":\"Prefab\",\"count\":2}],"
                + "\"exportDate\":\"2026-03-25T00:00:00Z\""
                + "}");

            File.WriteAllText(
                Path.Combine(rootDiskPath, "_temp", "YUCP_TempInstall_Test.json"),
                "{"
                + "\"name\":\"com.example.protected-shell\","
                + "\"vpmDependencies\":{"
                + "\"com.yucp.importer\":\"1.0.0\","
                + "\"com.example.extra\":\"2.0.0\""
                + "}"
                + "}");

            var protectedPayloadDescriptor = new ProtectedPayloadDescriptor
            {
                formatVersion = "1",
                protectedAssetId = "protected-asset-123",
                blobAssetPath = "Protected/payload.blob",
                cipher = "aes-256-cbc+hmac-sha256",
                archiveFormat = "zip",
                ciphertextSha256 = "ciphertext-sha",
                plaintextSha256 = "plaintext-sha",
                payloadAssetPaths = new[] { "Assets/Protected/source.prefab" },
                requiresOnlineUnlock = true,
                requiresBrokeredMaterialization = true,
                brokerProtocolVersion = 1,
            };
            protectedPayloadDescriptor.manifestBindingSha256 =
                ProtectedPayloadIntegrityUtility.ComputeManifestBindingSha256(protectedPayloadDescriptor);

            File.WriteAllText(
                Path.Combine(rootDiskPath, "YUCP_ProtectedPayload.json"),
                JsonUtility.ToJson(protectedPayloadDescriptor, true));

            string manifestRootAssetPath = useGlobalSigningManifest ? "Assets" : _testAssetRoot;
            if (useGlobalSigningManifest)
            {
                RegisterAdditionalCleanupAssetPath("Assets/_Signing/PackageManifest.json");
                RegisterAdditionalCleanupAssetPath("Assets/_Signing/PackageManifest.sig");
                RegisterAdditionalCleanupAssetPath("Assets/_Signing");
            }

            WriteManifest(
                manifestRootAssetPath,
                ProtectedPayloadIntegrityUtility.CreateManifestEntry(protectedPayloadDescriptor),
                includeSignatureFile: useGlobalSigningManifest);

            AssetDatabase.Refresh();
            return _testAssetRoot.Replace('\\', '/');
        }

        private static void WriteManifest(
            string rootAssetPath,
            ProtectedPayloadManifestEntry protectedPayloadEntry,
            bool includeSignatureFile = false)
        {
            string rootDiskPath = GetAssetDiskPath(rootAssetPath);
            string signingDiskPath = Path.Combine(rootDiskPath, "_Signing");
            Directory.CreateDirectory(signingDiskPath);
            string manifestJson = "{"
                + "\"publisherId\":\"publisher-123\","
                + "\"packageId\":\"pkg-test-123\","
                + "\"version\":\"1.2.3\","
                + "\"archiveSha256\":\"archive-sha-123\","
                + "\"fileHashes\":{},"
                + "\"protectedPayloads\":" + JsonUtility.ToJson(new ProtectedPayloadManifestWrapper
                {
                    protectedPayloads = protectedPayloadEntry != null ? new[] { protectedPayloadEntry } : Array.Empty<ProtectedPayloadManifestEntry>(),
                }).Replace("{\"protectedPayloads\":", string.Empty).TrimEnd('}')
                + "}";

            File.WriteAllText(Path.Combine(signingDiskPath, "PackageManifest.json"), manifestJson);
            if (includeSignatureFile)
            {
                File.WriteAllText(Path.Combine(signingDiskPath, "PackageManifest.sig"), "{\"signature\":\"test-signature\"}");
            }
        }

        [Serializable]
        private sealed class ProtectedPayloadManifestWrapper
        {
            public ProtectedPayloadManifestEntry[] protectedPayloads;
        }

        private void DeleteTestAssets()
        {
            DeleteAdditionalCleanupAssets();

            if (string.IsNullOrWhiteSpace(_testAssetRoot))
                return;

            FileUtil.DeleteFileOrDirectory(_testAssetRoot);
            FileUtil.DeleteFileOrDirectory(_testAssetRoot + ".meta");

            string parent = Path.GetDirectoryName(_testAssetRoot)?.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(parent) && AssetDatabase.IsValidFolder(parent))
            {
                string parentDiskPath = GetAssetDiskPath(parent);
                if (Directory.Exists(parentDiskPath) && !Directory.EnumerateFileSystemEntries(parentDiskPath).Any())
                {
                    FileUtil.DeleteFileOrDirectory(parent);
                    FileUtil.DeleteFileOrDirectory(parent + ".meta");

                    string grandParent = Path.GetDirectoryName(parent)?.Replace('\\', '/');
                    if (!string.IsNullOrWhiteSpace(grandParent) && AssetDatabase.IsValidFolder(grandParent))
                    {
                        string grandParentDiskPath = GetAssetDiskPath(grandParent);
                        if (Directory.Exists(grandParentDiskPath) && !Directory.EnumerateFileSystemEntries(grandParentDiskPath).Any())
                        {
                            FileUtil.DeleteFileOrDirectory(grandParent);
                            FileUtil.DeleteFileOrDirectory(grandParent + ".meta");
                        }
                    }
                }
            }

            AssetDatabase.Refresh();
            _testAssetRoot = null;
        }

        private void RegisterAdditionalCleanupAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || _additionalCleanupAssetPaths.Contains(assetPath))
                return;

            _additionalCleanupAssetPaths.Add(assetPath);
        }

        private void DeleteAdditionalCleanupAssets()
        {
            if (_additionalCleanupAssetPaths.Count == 0)
                return;

            foreach (string assetPath in _additionalCleanupAssetPaths
                         .OrderByDescending(path => path.Count(c => c == '/' || c == '\\')))
            {
                string diskPath = GetAssetDiskPath(assetPath);
                if (File.Exists(diskPath))
                {
                    FileUtil.DeleteFileOrDirectory(assetPath);
                    FileUtil.DeleteFileOrDirectory(assetPath + ".meta");
                    continue;
                }

                if (Directory.Exists(diskPath) && !Directory.EnumerateFileSystemEntries(diskPath).Any())
                {
                    FileUtil.DeleteFileOrDirectory(assetPath);
                    FileUtil.DeleteFileOrDirectory(assetPath + ".meta");
                }
            }

            _additionalCleanupAssetPaths.Clear();
            AssetDatabase.Refresh();
        }

        private static string GetAssetDiskPath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void WritePng(string path, Color color)
        {
            var texture = new Texture2D(2, 2);
            texture.SetPixels(new[] { color, color, color, color });
            texture.Apply();
            try
            {
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            File.Delete(path);
        }

        private static Type GetEditorType(string fullName)
        {
            Type editorType = typeof(InstalledPackageInfo).Assembly.GetType(fullName, false);
            Assert.That(editorType, Is.Not.Null, $"Expected to load type '{fullName}'.");
            return editorType;
        }

        private static Type GetLoadedType(string fullName)
        {
            Type loadedType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(type => type != null);

            Assert.That(loadedType, Is.Not.Null, $"Expected to load type '{fullName}' from the current AppDomain.");
            return loadedType;
        }
    }
}
