using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using YUCP.Importer.Editor.PackageManager;

namespace YUCP.Importer.Editor.Tests
{
    public class ProtectedImportStateTrackerTests
    {
        private const string PackageId = "pkg-state-tracker";
        private const string ProtectedAssetId = "1234567890abcdef1234567890abcdef";
        private const string ManifestBindingSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        [TearDown]
        public void TearDown()
        {
            ClearState(PackageId);
        }

        [Test]
        public void TryAdvance_PersistsForwardOnlyResumeState()
        {
            var packageInfo = CreatePackageInfo(ManifestBindingSha256);

            AssertAdvance(packageInfo, "shell_imported");
            AssertAdvance(packageInfo, "intent_verified");
            AssertAdvance(packageInfo, "apply_queued");
            AssertAdvance(
                packageInfo,
                "payload_extracted",
                new[] { @"Assets\Protected\Mesh.prefab", "Assets/Protected/Mesh.prefab", "Assets/Protected/Icon.png" });

            AssertValidateResume(packageInfo, "apply_queued", out object state);
            Assert.That(GetField<string>(state, "phase"), Is.EqualTo("payload_extracted"));
            CollectionAssert.AreEquivalent(
                new[] { "Assets/Protected/Mesh.prefab", "Assets/Protected/Icon.png" },
                GetField<string[]>(state, "extractedAssetPaths"));
        }

        [Test]
        public void TryValidateResume_FailsWhenManifestBindingChanges()
        {
            var original = CreatePackageInfo(ManifestBindingSha256);
            AssertAdvance(original, "shell_imported");

            var changed = CreatePackageInfo("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
            bool success = InvokeValidateResume(changed, "shell_imported", out _, out string error);

            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("manifest-bound protected payload descriptor"));
        }

        [Test]
        public void TryAdvance_AllowsFreshShellImportAfterFinalization()
        {
            var packageInfo = CreatePackageInfo(ManifestBindingSha256);
            AssertAdvance(packageInfo, "shell_imported");
            AssertAdvance(packageInfo, "materialization_finalized");

            bool success = InvokeAdvance(packageInfo, "shell_imported", null, out string error);

            Assert.That(success, Is.True, error);
        }

        private static InstalledPackageInfo CreatePackageInfo(string manifestBindingSha256)
        {
            return new InstalledPackageInfo
            {
                packageId = PackageId,
                packageName = "State Tracker",
                protectedPayload = new ProtectedPayloadDescriptor
                {
                    protectedAssetId = ProtectedAssetId,
                    manifestBindingSha256 = manifestBindingSha256,
                },
                installedFiles = new List<string>(),
            };
        }

        private static void AssertAdvance(
            InstalledPackageInfo packageInfo,
            string phaseName,
            IReadOnlyList<string> extractedAssetPaths = null)
        {
            bool success = InvokeAdvance(packageInfo, phaseName, extractedAssetPaths, out string error);
            Assert.That(success, Is.True, error);
        }

        private static void AssertValidateResume(
            InstalledPackageInfo packageInfo,
            string minimumPhaseName,
            out object state)
        {
            bool success = InvokeValidateResume(packageInfo, minimumPhaseName, out state, out string error);
            Assert.That(success, Is.True, error);
        }

        private static bool InvokeAdvance(
            InstalledPackageInfo packageInfo,
            string phaseName,
            IReadOnlyList<string> extractedAssetPaths,
            out string error)
        {
            MethodInfo method = GetTrackerType().GetMethod("TryAdvance", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object phase = Enum.Parse(GetPhaseType(), phaseName);
            object[] args = { packageInfo, phase, null, extractedAssetPaths };
            bool success = (bool)method.Invoke(null, args);
            error = args[2] as string;
            return success;
        }

        private static bool InvokeValidateResume(
            InstalledPackageInfo packageInfo,
            string minimumPhaseName,
            out object state,
            out string error)
        {
            MethodInfo method = GetTrackerType().GetMethod("TryValidateResume", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object phase = Enum.Parse(GetPhaseType(), minimumPhaseName);
            object[] args = { packageInfo, phase, null, null };
            bool success = (bool)method.Invoke(null, args);
            state = args[2];
            error = args[3] as string;
            return success;
        }

        private static void ClearState(string packageId)
        {
            MethodInfo method = GetTrackerType().GetMethod("Clear", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { packageId });
        }

        private static Type GetTrackerType()
        {
            Type trackerType = typeof(PackageManagerWindow).Assembly.GetType(
                "YUCP.Importer.Editor.PackageManager.Core.ProtectedImportStateTracker",
                throwOnError: false);
            Assert.That(trackerType, Is.Not.Null);
            return trackerType;
        }

        private static Type GetPhaseType()
        {
            Type phaseType = GetTrackerType().GetNestedType("ProtectedImportPhase", BindingFlags.NonPublic);
            Assert.That(phaseType, Is.Not.Null);
            return phaseType;
        }

        private static T GetField<T>(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(instance);
        }
    }
}
