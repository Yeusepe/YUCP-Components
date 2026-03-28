using System;
using System.IO;
using NUnit.Framework;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class PackageManagerRuntimeSettingsTests
    {
        private string _previousRuntimeRoot;

        [SetUp]
        public void SetUp()
        {
            _previousRuntimeRoot = Environment.GetEnvironmentVariable("YUCP_COUPLING_RUNTIME_ROOT");

            Environment.SetEnvironmentVariable("YUCP_COUPLING_RUNTIME_ROOT", null);
            YUCP.Importer.Editor.PackageManager.PackageManagerRuntimeSettings.SetRuntimeInstallRootOverride(string.Empty);
        }

        [TearDown]
        public void TearDown()
        {
            YUCP.Importer.Editor.PackageManager.PackageManagerRuntimeSettings.SetRuntimeInstallRootOverride(string.Empty);
            Environment.SetEnvironmentVariable("YUCP_COUPLING_RUNTIME_ROOT", _previousRuntimeRoot);
        }

        [Test]
        public void ResolveRuntimeInstallRoot_ReturnsDefaultWhenUnset()
        {
            string resolved = YUCP.Importer.Editor.PackageManager.PackageManagerRuntimeSettings.ResolveRuntimeInstallRoot();

            Assert.That(
                resolved,
                Is.EqualTo(YUCP.Importer.Editor.PackageManager.PackageManagerRuntimeSettings.DefaultRuntimeInstallRoot));
        }

        [Test]
        public void ResolveRuntimeInstallRoot_PrefersEditorOverrideOverEnvironment()
        {
            string environmentRoot = Path.Combine(Path.GetTempPath(), "env-runtime-root");
            string editorRoot = Path.Combine(Path.GetTempPath(), "editor-runtime-root");
            Environment.SetEnvironmentVariable("YUCP_COUPLING_RUNTIME_ROOT", environmentRoot);
            YUCP.Importer.Editor.PackageManager.PackageManagerRuntimeSettings.SetRuntimeInstallRootOverride(editorRoot);

            string resolved = YUCP.Importer.Editor.PackageManager.PackageManagerRuntimeSettings.ResolveRuntimeInstallRoot();

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(editorRoot)));
        }

        [Test]
        public void NormalizeRuntimeInstallRoot_RejectsWhitespace()
        {
            string normalized = YUCP.Importer.Editor.PackageManager.PackageManagerRuntimeSettings.NormalizeRuntimeInstallRoot("   ");

            Assert.That(normalized, Is.Null);
        }

        [Test]
        public void NormalizeRuntimeInstallRoot_ReturnsFullPath()
        {
            string relativePath = Path.Combine(".", "Library", "..", "Temp", "yucp-runtime");

            string normalized = YUCP.Importer.Editor.PackageManager.PackageManagerRuntimeSettings.NormalizeRuntimeInstallRoot(relativePath);

            Assert.That(normalized, Is.EqualTo(Path.GetFullPath(relativePath)));
        }
    }
}
