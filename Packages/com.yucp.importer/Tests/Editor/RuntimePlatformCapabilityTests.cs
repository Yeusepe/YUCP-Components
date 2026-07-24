using NUnit.Framework;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class RuntimePlatformCapabilityTests
    {
        [TestCase(RuntimePlatform.LinuxEditor)]
        [TestCase(RuntimePlatform.OSXEditor)]
        public void ProtectedMaterializationRejectsUnsupportedEditorPlatforms(
            RuntimePlatform platform)
        {
            bool valid = CouplingRuntimeShimService.TryValidateProtectedMaterializationRuntime(
                platform,
                out string error);

            Assert.That(valid, Is.False);
            Assert.That(error, Is.EqualTo(
                "Protected materialization requires the Windows Editor."));
            Assert.That(
                CouplingRuntimeShimService.IsProtectedMaterializationPlatformSupported(platform),
                Is.False);
        }

        [Test]
        public void ProtectedMaterializationAcceptsTheLaunchEditorPlatform()
        {
            Assert.That(
                CouplingRuntimeShimService.IsProtectedMaterializationPlatformSupported(
                    RuntimePlatform.WindowsEditor),
                Is.True);
        }
    }
}
