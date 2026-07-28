using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.Components.Editor.Tests
{
    public sealed class AdvancedVisemeReviewerRegressionTests
    {
        [Test]
        public void FreshProfilePreservesTheCurrentFieldDefaults()
        {
            var profile =
                ScriptableObject.CreateInstance<VisemeReconstructionProfile>();
            try
            {
                Assert.That(
                    profile.visemeResponseSeconds,
                    Is.EqualTo(0.017f).Within(1e-6f));
                Assert.That(
                    profile.speechLiveliness,
                    Is.Zero.Within(1e-6f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void MissingProfileKeepsSpeechLivelinessAtItsAuthoredDefault()
        {
            var method = typeof(AdvancedVisemeTuning).GetMethod(
                             "ConfiguredValue",
                             BindingFlags.Public | BindingFlags.Static,
                             null,
                             new[]
                             {
                                 typeof(VisemeReconstructionProfile),
                                 typeof(AdvancedVisemeTuningControl)
                             },
                             null) ??
                         typeof(AdvancedVisemeTuning).GetMethod(
                             "DefaultValue",
                             BindingFlags.Public | BindingFlags.Static,
                             null,
                             new[]
                             {
                                 typeof(VisemeReconstructionProfile),
                                 typeof(AdvancedVisemeTuningControl)
                             },
                             null);
            Assert.That(method, Is.Not.Null);
            var value = (float)method.Invoke(
                null,
                new object[]
                {
                    null,
                    AdvancedVisemeTuningControl.SpeechLiveliness
                });

            Assert.That(value, Is.Zero.Within(1e-6f));
        }

        [Test]
        public void PackageAndCompilationCoordinationUsesCompletionRequests()
        {
            string projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string resolver = File.ReadAllText(Path.Combine(
                projectRoot,
                "Packages",
                "com.yucp.importer",
                "Editor",
                "PackageManager",
                "Core",
                "EmbeddedPackageResolver.cs"));
            string verifier = File.ReadAllText(Path.Combine(
                projectRoot,
                "Packages",
                "com.yucp.importer",
                "Editor",
                "PackageManager",
                "Core",
                "PackageImportVerifier.cs"));

            StringAssert.Contains("Client.Resolve();", resolver);
            StringAssert.Contains("Client.List(false, true)", resolver);
            StringAssert.DoesNotContain(
                "Events.registeredPackages +=",
                resolver);
            StringAssert.Contains(
                "CompilationPipeline.compilationFinished +=",
                verifier);
            StringAssert.DoesNotContain(
                "EditorApplication.isCompiling ||",
                verifier);
        }

        [Test]
        public void HostedLifecycleActionsSurfaceThrownFailures()
        {
            string projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string source = File.ReadAllText(Path.Combine(
                projectRoot,
                "Packages",
                "com.yucp.importer",
                "Editor",
                "PackageManager",
                "UI",
                "PackageManagerWindow.cs"));
            int method = source.IndexOf(
                "private async void RunHostedLifecycleAction",
                StringComparison.Ordinal);
            Assert.That(method, Is.GreaterThanOrEqualTo(0),
                "RunHostedLifecycleAction was renamed or removed.");
            int nextMethod = source.IndexOf(
                "private void SetHostedLifecycleControlsEnabled",
                method,
                StringComparison.Ordinal);
            Assert.That(nextMethod, Is.GreaterThan(method),
                "SetHostedLifecycleControlsEnabled no longer follows " +
                "RunHostedLifecycleAction.");
            string body = source.Substring(method, nextMethod - method);

            StringAssert.Contains("catch (Exception", body);
            StringAssert.Contains("UpdateImportButtonEnabled();", body);
        }
    }
}
