using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using YUCP.Importer.Editor.PackageManager;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class CouplingRuntimeShimServiceTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), $"yucp-runtime-status-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempRoot);
            PackageManagerRuntimeSettings.SetRuntimeInstallRootOverride(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            PackageManagerRuntimeSettings.SetRuntimeInstallRootOverride(string.Empty);
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        [Test]
        public void GetRuntimeStatus_ReturnsMissingWhenActiveStateDoesNotExist()
        {
            object status = InvokeGetRuntimeStatus();

            Assert.That(GetField(status, "status"), Is.EqualTo("missing"));
            Assert.That(GetField(status, "error"), Is.EqualTo("The package protection runtime is not installed for this Windows user."));
            Assert.That(GetField(status, "installRoot"), Is.EqualTo(_tempRoot));
        }

        [Test]
        public void TryValidateProtectedMaterializationRuntime_ReturnsInstallGuidanceWhenActiveStateDoesNotExist()
        {
            bool success = InvokeTryValidateProtectedMaterializationRuntime(out string error);

            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("The package protection runtime is not installed for this Windows user."));
            Assert.That(error, Does.Contain("Install or repair the YUCP runtime package before importing protected payloads."));
            Assert.That(error, Does.Contain(_tempRoot));
            Assert.That(error, Does.Contain("Open Project Settings > YUCP Package Manager"));
        }

        [Test]
        public void GetRuntimeStatus_ReturnsMissingWhenShimIsAbsentFromActiveInstallation()
        {
            string packageDir = Path.Combine(_tempRoot, "packages", "build-a");
            string stateDir = Path.Combine(_tempRoot, "state");
            Directory.CreateDirectory(packageDir);
            Directory.CreateDirectory(stateDir);

            File.WriteAllText(
                Path.Combine(stateDir, "active.json"),
                "{\n" +
                "  \"activeBuildId\": \"build-a\",\n" +
                "  \"activeVersion\": \"0.0.1-dev\",\n" +
                $"  \"activePackageDir\": \"{EscapeJson(packageDir)}\"\n" +
                "}");
            File.WriteAllText(
                Path.Combine(packageDir, "CouplingRuntimeCom.metadata.json"),
                "{\n" +
                "  \"clientName\": \"CouplingRuntimeProbeClient.exe\"\n" +
                "}");

            object status = InvokeGetRuntimeStatus();

            Assert.That(GetField(status, "status"), Is.EqualTo("missing"));
            Assert.That(
                GetField(status, "error"),
                Is.EqualTo("The package protection runtime activation shim is missing from the active installation."));
            Assert.That(GetField(status, "activeBuildId"), Is.Empty);
        }

        private static bool InvokeTryValidateProtectedMaterializationRuntime(out string error)
        {
            Type serviceType = typeof(InstalledPackageInfo).Assembly.GetType(
                "YUCP.Importer.Editor.PackageManager.Core.CouplingRuntimeShimService",
                throwOnError: false);
            Assert.That(serviceType, Is.Not.Null);

            MethodInfo method = serviceType.GetMethod(
                "TryValidateProtectedMaterializationRuntime",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);

            object[] args = { null };
            bool success = (bool)method.Invoke(null, args);
            error = args[0] as string;
            return success;
        }

        private static object InvokeGetRuntimeStatus()
        {
            Type serviceType = typeof(InstalledPackageInfo).Assembly.GetType(
                "YUCP.Importer.Editor.PackageManager.Core.CouplingRuntimeShimService",
                throwOnError: false);
            Assert.That(serviceType, Is.Not.Null);

            MethodInfo method = serviceType.GetMethod(
                "GetRuntimeStatus",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, null);
        }

        private static string GetField(object instance, string name)
        {
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected runtime status field '{name}'.");
            return field.GetValue(instance) as string ?? string.Empty;
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\");
        }
    }
}
