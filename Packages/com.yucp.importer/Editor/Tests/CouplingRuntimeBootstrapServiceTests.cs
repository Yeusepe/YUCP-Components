using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using YUCP.Importer.Editor.PackageManager;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class CouplingRuntimeBootstrapServiceTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), $"yucp-runtime-bootstrap-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempRoot);
            PackageManagerRuntimeSettings.SetRuntimeInstallRootOverride(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            PackageManagerRuntimeSettings.SetRuntimeInstallRootOverride(string.Empty);
            ResetOverrides();
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        [Test]
        public void TryEnsureProtectedMaterializationRuntimeReady_InstallsRuntimeWhenInitialValidationFails()
        {
            Type serviceType = GetBootstrapServiceType();
            int validateCalls = 0;
            string requestedPackageId = null;
            string requestedProjectId = null;
            string requestedMachineFingerprint = null;
            string requestedLicenseToken = null;
            string downloadedServerUrl = null;
            string downloadedToken = null;
            string downloadedSha = null;
            string downloadedTempRoot = null;
            string installedZipPath = null;
            string installedRoot = null;

            SetOverride(serviceType, "s_validateRuntimeOverride", (Func<(bool success, string error)>)(() =>
            {
                validateCalls++;
                return validateCalls == 1
                    ? (false, "The package protection runtime is not installed for this Windows user.")
                    : (true, null);
            }));
            SetOverride(serviceType, "s_getLicenseTokenOverride", (Func<string, string>)(packageId =>
            {
                requestedPackageId = packageId;
                return "license-token";
            }));
            SetOverride(serviceType, "s_getProjectIdOverride", (Func<string>)(() => "0123456789abcdef0123456789abcdef"));
            SetOverride(serviceType, "s_getMachineFingerprintOverride", (Func<string>)(() => "machine-fingerprint"));
            SetOverride(serviceType, "s_getServerUrlOverride", (Func<string>)(() => "https://example.invalid"));
            SetOverride(
                serviceType,
                "s_requestRuntimePackageTokenOverride",
                (Func<string, string, string, string, (bool success, string runtimePackageToken, string runtimePackageSha256, long expiresAt, string error)>)((packageId, projectId, machineFingerprint, licenseToken) =>
                {
                    requestedProjectId = projectId;
                    requestedMachineFingerprint = machineFingerprint;
                    requestedLicenseToken = licenseToken;
                    return (true, "runtime-package-token", "runtime-package-sha", 1234, null);
                }));
            SetOverride(
                serviceType,
                "s_downloadRuntimePackageOverride",
                (Func<string, string, string, string, (bool success, string packageZipPath, string error)>)((serverUrl, runtimePackageToken, expectedSha256, tempRoot) =>
                {
                    downloadedServerUrl = serverUrl;
                    downloadedToken = runtimePackageToken;
                    downloadedSha = expectedSha256;
                    downloadedTempRoot = tempRoot;
                    return (true, Path.Combine(tempRoot, "runtime-package.zip"), null);
                }));
            SetOverride(
                serviceType,
                "s_installRuntimePackageOverride",
                (Func<string, string, (bool success, string error)>)((packageZipPath, installRoot) =>
                {
                    installedZipPath = packageZipPath;
                    installedRoot = installRoot;
                    return (true, null);
                }));

            bool success = InvokeEnsureReady("pkg-bootstrap", out string error);

            Assert.That(success, Is.True);
            Assert.That(error, Is.Null.Or.Empty);
            Assert.That(validateCalls, Is.EqualTo(2));
            Assert.That(requestedPackageId, Is.EqualTo("pkg-bootstrap"));
            Assert.That(requestedProjectId, Is.EqualTo("0123456789abcdef0123456789abcdef"));
            Assert.That(requestedMachineFingerprint, Is.EqualTo("machine-fingerprint"));
            Assert.That(requestedLicenseToken, Is.EqualTo("license-token"));
            Assert.That(downloadedServerUrl, Is.EqualTo("https://example.invalid"));
            Assert.That(downloadedToken, Is.EqualTo("runtime-package-token"));
            Assert.That(downloadedSha, Is.EqualTo("runtime-package-sha"));
            Assert.That(downloadedTempRoot, Does.Contain("yucp-runtime-bootstrap-"));
            Assert.That(installedZipPath, Does.EndWith("runtime-package.zip"));
            Assert.That(installedRoot, Is.EqualTo(_tempRoot));
        }

        [Test]
        public void TryEnsureProtectedMaterializationRuntimeReady_RepairsRuntimeBeforeDownloadingPackage()
        {
            Type serviceType = GetBootstrapServiceType();
            int validateCalls = 0;
            int repairCalls = 0;

            SetOverride(serviceType, "s_validateRuntimeOverride", (Func<(bool success, string error)>)(() =>
            {
                validateCalls++;
                return validateCalls == 1
                    ? (false, "The package protection runtime is not installed for this Windows user.")
                    : (true, null);
            }));
            SetOverride(serviceType, "s_repairRuntimeRegistrationOverride", (Func<(bool success, string error)>)(() =>
            {
                repairCalls++;
                return (true, null);
            }));
            SetOverride(
                serviceType,
                "s_requestRuntimePackageTokenOverride",
                (Func<string, string, string, string, (bool success, string runtimePackageToken, string runtimePackageSha256, long expiresAt, string error)>)((_, __, ___, ____) =>
                {
                    Assert.Fail("Repair should avoid requesting a runtime package token.");
                    return default;
                }));
            SetOverride(
                serviceType,
                "s_downloadRuntimePackageOverride",
                (Func<string, string, string, string, (bool success, string packageZipPath, string error)>)((_, __, ___, ____) =>
                {
                    Assert.Fail("Repair should avoid downloading the runtime package.");
                    return default;
                }));
            SetOverride(
                serviceType,
                "s_installRuntimePackageOverride",
                (Func<string, string, (bool success, string error)>)((_, __) =>
                {
                    Assert.Fail("Repair should avoid reinstalling the runtime package.");
                    return default;
                }));

            bool success = InvokeEnsureReady("pkg-bootstrap", out string error);

            Assert.That(success, Is.True);
            Assert.That(error, Is.Null.Or.Empty);
            Assert.That(repairCalls, Is.EqualTo(1));
            Assert.That(validateCalls, Is.EqualTo(2));
        }

        [Test]
        public void TryEnsureProtectedMaterializationRuntimeReady_FallsBackToInstallWhenRepairDoesNotRestoreProbe()
        {
            Type serviceType = GetBootstrapServiceType();
            int validateCalls = 0;
            int repairCalls = 0;
            bool tokenRequested = false;
            bool downloaded = false;
            bool installed = false;

            SetOverride(serviceType, "s_validateRuntimeOverride", (Func<(bool success, string error)>)(() =>
            {
                validateCalls++;
                return validateCalls < 3
                    ? (false, "The package protection runtime is not installed for this Windows user.")
                    : (true, null);
            }));
            SetOverride(serviceType, "s_repairRuntimeRegistrationOverride", (Func<(bool success, string error)>)(() =>
            {
                repairCalls++;
                return (true, null);
            }));
            SetOverride(serviceType, "s_getLicenseTokenOverride", (Func<string, string>)(_ => "license-token"));
            SetOverride(serviceType, "s_getProjectIdOverride", (Func<string>)(() => "0123456789abcdef0123456789abcdef"));
            SetOverride(serviceType, "s_getMachineFingerprintOverride", (Func<string>)(() => "machine-fingerprint"));
            SetOverride(serviceType, "s_getServerUrlOverride", (Func<string>)(() => "https://example.invalid"));
            SetOverride(
                serviceType,
                "s_requestRuntimePackageTokenOverride",
                (Func<string, string, string, string, (bool success, string runtimePackageToken, string runtimePackageSha256, long expiresAt, string error)>)((_, __, ___, ____) =>
                {
                    tokenRequested = true;
                    return (true, "runtime-package-token", "runtime-package-sha", 1234, null);
                }));
            SetOverride(
                serviceType,
                "s_downloadRuntimePackageOverride",
                (Func<string, string, string, string, (bool success, string packageZipPath, string error)>)((_, __, ___, tempRoot) =>
                {
                    downloaded = true;
                    return (true, Path.Combine(tempRoot, "runtime-package.zip"), null);
                }));
            SetOverride(
                serviceType,
                "s_installRuntimePackageOverride",
                (Func<string, string, (bool success, string error)>)((_, __) =>
                {
                    installed = true;
                    return (true, null);
                }));

            bool success = InvokeEnsureReady("pkg-bootstrap", out string error);

            Assert.That(success, Is.True);
            Assert.That(error, Is.Null.Or.Empty);
            Assert.That(repairCalls, Is.EqualTo(1));
            Assert.That(validateCalls, Is.EqualTo(3));
            Assert.That(tokenRequested, Is.True);
            Assert.That(downloaded, Is.True);
            Assert.That(installed, Is.True);
        }

        [Test]
        public void TryEnsureProtectedMaterializationRuntimeReady_FailsWhenRuntimeStillDoesNotSupportProtectedImportsAfterInstall()
        {
            Type serviceType = GetBootstrapServiceType();
            int validateCalls = 0;

            SetOverride(serviceType, "s_validateRuntimeOverride", (Func<(bool success, string error)>)(() =>
            {
                validateCalls++;
                return (false, validateCalls == 1
                    ? "The package protection runtime is not installed for this Windows user."
                    : "The installed package protection runtime does not support the protected materialization required by this package.");
            }));
            SetOverride(serviceType, "s_getLicenseTokenOverride", (Func<string, string>)(_ => "license-token"));
            SetOverride(serviceType, "s_getProjectIdOverride", (Func<string>)(() => "0123456789abcdef0123456789abcdef"));
            SetOverride(serviceType, "s_getMachineFingerprintOverride", (Func<string>)(() => "machine-fingerprint"));
            SetOverride(serviceType, "s_getServerUrlOverride", (Func<string>)(() => "https://example.invalid"));
            SetOverride(
                serviceType,
                "s_requestRuntimePackageTokenOverride",
                (Func<string, string, string, string, (bool success, string runtimePackageToken, string runtimePackageSha256, long expiresAt, string error)>)((_, __, ___, ____) =>
                    (true, "runtime-package-token", "runtime-package-sha", 1234, null)));
            SetOverride(
                serviceType,
                "s_downloadRuntimePackageOverride",
                (Func<string, string, string, string, (bool success, string packageZipPath, string error)>)((_, __, ___, tempRoot) =>
                    (true, Path.Combine(tempRoot, "runtime-package.zip"), null)));
            SetOverride(
                serviceType,
                "s_installRuntimePackageOverride",
                (Func<string, string, (bool success, string error)>)((_, __) => (true, null)));

            bool success = InvokeEnsureReady("pkg-bootstrap", out string error);

            Assert.That(success, Is.False);
            Assert.That(validateCalls, Is.EqualTo(2));
            Assert.That(
                error,
                Is.EqualTo("The installed package protection runtime does not support the protected materialization required by this package."));
        }

        private static Type GetBootstrapServiceType()
        {
            Type type = typeof(InstalledPackageInfo).Assembly.GetType(
                "YUCP.Importer.Editor.PackageManager.Core.CouplingRuntimeBootstrapService",
                throwOnError: false);
            Assert.That(type, Is.Not.Null);
            return type;
        }

        private static bool InvokeEnsureReady(string packageId, out string error)
        {
            MethodInfo method = GetBootstrapServiceType().GetMethod(
                "TryEnsureProtectedMaterializationRuntimeReady",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);

            object[] args = { packageId, null };
            bool success = (bool)method.Invoke(null, args);
            error = args[1] as string;
            return success;
        }

        private static void SetOverride(Type serviceType, string fieldName, object value)
        {
            FieldInfo field = serviceType.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Expected override field '{fieldName}'.");
            field.SetValue(null, value);
        }

        private static void ResetOverrides()
        {
            Type serviceType = GetBootstrapServiceType();
            foreach (string fieldName in new[]
                     {
                         "s_validateRuntimeOverride",
                         "s_getLicenseTokenOverride",
                         "s_getProjectIdOverride",
                         "s_getMachineFingerprintOverride",
                         "s_getServerUrlOverride",
                         "s_repairRuntimeRegistrationOverride",
                         "s_requestRuntimePackageTokenOverride",
                         "s_downloadRuntimePackageOverride",
                         "s_installRuntimePackageOverride",
                      })
            {
                FieldInfo field = serviceType.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                field?.SetValue(null, null);
            }
        }
    }
}
