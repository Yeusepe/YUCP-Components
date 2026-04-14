using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using NUnit.Framework;
using YUCP.Importer.Editor.PackageManager;
using YUCP.Importer.Editor.PackageManager.Core;

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
            CouplingRuntimeBootstrapServiceTestHooks.Reset();
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        [Test]
        public void TryEnsureProtectedMaterializationRuntimeReady_InstallsRuntimeWhenInitialValidationFails()
        {
            int validateCalls = 0;
            string requestedPackageId = null;
            string requestedProjectId = null;
            string requestedMachineFingerprint = null;
            string requestedLicenseToken = null;
            string downloadedServerUrl = null;
            string downloadedToken = null;
            string downloadedSha = null;
            byte[] installedArchive = null;
            string installedWorkingRoot = null;
            string installedRoot = null;

            CouplingRuntimeBootstrapServiceTestHooks.ValidateRuntime = () =>
            {
                validateCalls++;
                return validateCalls == 1
                    ? (false, "The package protection runtime is not installed for this Windows user.")
                    : (true, null);
            };
            CouplingRuntimeBootstrapServiceTestHooks.GetLicenseToken = packageId =>
            {
                requestedPackageId = packageId;
                return "license-token";
            };
            CouplingRuntimeBootstrapServiceTestHooks.GetProjectId = () => "0123456789abcdef0123456789abcdef";
            CouplingRuntimeBootstrapServiceTestHooks.GetMachineFingerprint = () => "machine-fingerprint";
            CouplingRuntimeBootstrapServiceTestHooks.GetServerUrl = () => "https://example.invalid";
            CouplingRuntimeBootstrapServiceTestHooks.RequestRuntimePackageToken = (packageId, projectId, machineFingerprint, licenseToken) =>
            {
                requestedProjectId = projectId;
                requestedMachineFingerprint = machineFingerprint;
                requestedLicenseToken = licenseToken;
                return (true, "runtime-package-token", "runtime-package-sha", 1234, null);
            };
            CouplingRuntimeBootstrapServiceTestHooks.DownloadRuntimePackage = (serverUrl, runtimePackageToken, expectedSha256) =>
            {
                downloadedServerUrl = serverUrl;
                downloadedToken = runtimePackageToken;
                downloadedSha = expectedSha256;
                return (true, new byte[] { 1, 2, 3, 4 }, null);
            };
            CouplingRuntimeBootstrapServiceTestHooks.InstallRuntimePackage = (packageZipBytes, workingRoot, installRoot) =>
            {
                installedArchive = packageZipBytes;
                installedWorkingRoot = workingRoot;
                installedRoot = installRoot;
                return (true, null);
            };

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
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, installedArchive);
            Assert.That(installedWorkingRoot, Does.Contain("yucp-runtime-bootstrap-"));
            Assert.That(installedRoot, Is.EqualTo(_tempRoot));
        }

        [Test]
        public void TryEnsureProtectedMaterializationRuntimeReady_RepairsRuntimeBeforeDownloadingPackage()
        {
            int validateCalls = 0;
            int repairCalls = 0;

            CouplingRuntimeBootstrapServiceTestHooks.ValidateRuntime = () =>
            {
                validateCalls++;
                return validateCalls == 1
                    ? (false, "The package protection runtime is not installed for this Windows user.")
                    : (true, null);
            };
            CouplingRuntimeBootstrapServiceTestHooks.RepairRuntimeRegistration = () =>
            {
                repairCalls++;
                return (true, null);
            };
            CouplingRuntimeBootstrapServiceTestHooks.RequestRuntimePackageToken = (_, _, _, _) =>
            {
                Assert.Fail("Repair should avoid requesting a runtime package token.");
                return default;
            };
            CouplingRuntimeBootstrapServiceTestHooks.DownloadRuntimePackage = (_, _, _) =>
            {
                Assert.Fail("Repair should avoid downloading the runtime package.");
                return default;
            };
            CouplingRuntimeBootstrapServiceTestHooks.InstallRuntimePackage = (_, _, _) =>
            {
                Assert.Fail("Repair should avoid reinstalling the runtime package.");
                return default;
            };

            bool success = InvokeEnsureReady("pkg-bootstrap", out string error);

            Assert.That(success, Is.True);
            Assert.That(error, Is.Null.Or.Empty);
            Assert.That(repairCalls, Is.EqualTo(1));
            Assert.That(validateCalls, Is.EqualTo(2));
        }

        [Test]
        public void TryEnsureProtectedMaterializationRuntimeReady_FallsBackToInstallWhenRepairDoesNotRestoreProbe()
        {
            int validateCalls = 0;
            int repairCalls = 0;
            bool tokenRequested = false;
            bool downloaded = false;
            bool installed = false;

            CouplingRuntimeBootstrapServiceTestHooks.ValidateRuntime = () =>
            {
                validateCalls++;
                return validateCalls < 3
                    ? (false, "The package protection runtime is not installed for this Windows user.")
                    : (true, null);
            };
            CouplingRuntimeBootstrapServiceTestHooks.RepairRuntimeRegistration = () =>
            {
                repairCalls++;
                return (true, null);
            };
            CouplingRuntimeBootstrapServiceTestHooks.GetLicenseToken = _ => "license-token";
            CouplingRuntimeBootstrapServiceTestHooks.GetProjectId = () => "0123456789abcdef0123456789abcdef";
            CouplingRuntimeBootstrapServiceTestHooks.GetMachineFingerprint = () => "machine-fingerprint";
            CouplingRuntimeBootstrapServiceTestHooks.GetServerUrl = () => "https://example.invalid";
            CouplingRuntimeBootstrapServiceTestHooks.RequestRuntimePackageToken = (_, _, _, _) =>
            {
                tokenRequested = true;
                return (true, "runtime-package-token", "runtime-package-sha", 1234, null);
            };
            CouplingRuntimeBootstrapServiceTestHooks.DownloadRuntimePackage = (_, _, _) =>
            {
                downloaded = true;
                return (true, new byte[] { 5, 6, 7 }, null);
            };
            CouplingRuntimeBootstrapServiceTestHooks.InstallRuntimePackage = (_, _, _) =>
            {
                installed = true;
                return (true, null);
            };

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
            int validateCalls = 0;

            CouplingRuntimeBootstrapServiceTestHooks.ValidateRuntime = () =>
            {
                validateCalls++;
                return (false, validateCalls == 1
                    ? "The package protection runtime is not installed for this Windows user."
                    : "The installed package protection runtime does not support the protected materialization required by this package.");
            };
            CouplingRuntimeBootstrapServiceTestHooks.GetLicenseToken = _ => "license-token";
            CouplingRuntimeBootstrapServiceTestHooks.GetProjectId = () => "0123456789abcdef0123456789abcdef";
            CouplingRuntimeBootstrapServiceTestHooks.GetMachineFingerprint = () => "machine-fingerprint";
            CouplingRuntimeBootstrapServiceTestHooks.GetServerUrl = () => "https://example.invalid";
            CouplingRuntimeBootstrapServiceTestHooks.RequestRuntimePackageToken = (_, _, _, _) =>
                (true, "runtime-package-token", "runtime-package-sha", 1234, null);
            CouplingRuntimeBootstrapServiceTestHooks.DownloadRuntimePackage = (_, _, _) =>
                (true, new byte[] { 1, 2, 3 }, null);
            CouplingRuntimeBootstrapServiceTestHooks.InstallRuntimePackage = (_, _, _) => (true, null);

            bool success = InvokeEnsureReady("pkg-bootstrap", out string error);

            Assert.That(success, Is.False);
            Assert.That(validateCalls, Is.EqualTo(2));
            Assert.That(
                error,
                Is.EqualTo("The installed package protection runtime does not support the protected materialization required by this package."));
        }

        [Test]
        public void InstallRuntimePackage_RejectsNestedInstallScriptPaths()
        {
            byte[] archiveBytes = CreateRuntimePackageArchive(
                ("runtime-package-manifest.json", "{ \"buildDir\": \"build\", \"installScriptPath\": \"Scripts/install-runtime.ps1\" }"),
                ("build/payload.txt", "payload"),
                ("Scripts/install-runtime.ps1", "Write-Host 'install'"));

            string workingRoot = Path.Combine(_tempRoot, "working");
            Directory.CreateDirectory(workingRoot);

            bool success = InvokeInstallRuntimePackage(archiveBytes, workingRoot, _tempRoot, out string error);

            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("must not contain nested path segments"));
        }

        [Test]
        public void TryRepairRuntimeRegistration_RejectsStateDllPathOutsideVerifiedPackageRoot()
        {
            string packageDir = Path.Combine(_tempRoot, "packages", "build-a");
            string rogueDir = Path.Combine(_tempRoot, "rogue");
            string stateDir = Path.Combine(_tempRoot, "state");
            Directory.CreateDirectory(packageDir);
            Directory.CreateDirectory(rogueDir);
            Directory.CreateDirectory(stateDir);

            byte[] runtimeBytes = { 9, 8, 7, 6 };
            string verifiedDllPath = Path.Combine(packageDir, "CouplingRuntimeCom.dll");
            string rogueDllPath = Path.Combine(rogueDir, "CouplingRuntimeCom.dll");
            File.WriteAllBytes(verifiedDllPath, runtimeBytes);
            File.WriteAllBytes(rogueDllPath, runtimeBytes);

            string metadataJson =
                "{\n" +
                "  \"dllName\": \"CouplingRuntimeCom.dll\",\n" +
                $"  \"sha256\": \"{ComputeSha256Hex(runtimeBytes)}\"\n" +
                "}";
            File.WriteAllText(Path.Combine(packageDir, "CouplingRuntimeCom.metadata.json"), metadataJson);

            string activeStateJson =
                "{\n" +
                "  \"activeBuildId\": \"build-a\",\n" +
                "  \"activeVersion\": \"0.0.1-dev\",\n" +
                $"  \"activePackageDir\": \"{EscapeJson(packageDir)}\",\n" +
                $"  \"activeDllPath\": \"{EscapeJson(rogueDllPath)}\"\n" +
                "}";
            File.WriteAllText(Path.Combine(stateDir, "active.json"), activeStateJson);

            InvokeTryRepairRuntimeRegistration(out bool attempted, out bool success, out string error);

            Assert.That(attempted, Is.True);
            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("did not match the verified runtime DLL location"));
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

        private static bool InvokeInstallRuntimePackage(byte[] packageZipBytes, string workingRoot, string installRoot, out string error)
        {
            MethodInfo method = GetBootstrapServiceType().GetMethod(
                "InstallRuntimePackage",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            object result = method.Invoke(null, new object[] { packageZipBytes, workingRoot, installRoot });
            bool success = GetTupleField<bool>(result, 1);
            error = GetTupleField<string>(result, 2);
            return success;
        }

        private static void InvokeTryRepairRuntimeRegistration(out bool attempted, out bool success, out string error)
        {
            MethodInfo method = GetBootstrapServiceType().GetMethod(
                "TryRepairRuntimeRegistration",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            object result = method.Invoke(null, null);
            attempted = GetTupleField<bool>(result, 1);
            success = GetTupleField<bool>(result, 2);
            error = GetTupleField<string>(result, 3);
        }

        private static T GetTupleField<T>(object tuple, int itemIndex)
        {
            FieldInfo field = tuple.GetType().GetField($"Item{itemIndex}");
            Assert.That(field, Is.Not.Null, $"Expected tuple field Item{itemIndex}.");
            return (T)field.GetValue(tuple);
        }

        private static byte[] CreateRuntimePackageArchive(params (string path, string contents)[] entries)
        {
            using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var entry in entries)
                {
                    ZipArchiveEntry archiveEntry = archive.CreateEntry(entry.path);
                    using var writer = new StreamWriter(archiveEntry.Open());
                    writer.Write(entry.contents);
                }
            }

            return memory.ToArray();
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\");
        }

        private static string ComputeSha256Hex(byte[] data)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            byte[] hash = sha256.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
