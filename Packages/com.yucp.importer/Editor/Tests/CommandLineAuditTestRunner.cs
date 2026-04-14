using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace YUCP.Importer.Editor.Tests
{
    public static class CommandLineAuditTestRunner
    {
        private static readonly string[] DefaultTestNames =
        {
            "YUCP.Importer.Editor.Tests.ImporterVerificationBypassTests.ResolvePublicKey_DoesNotSeedTrustFromPreferredServer_WhenTrustListIsEmpty",
            "YUCP.Importer.Editor.Tests.ImporterVerificationBypassTests.GetLicenseServerUrl_UsesDefaultTrustedUrl_WhenTrustListIsEmpty",
            "YUCP.Importer.Editor.Tests.ImporterVerificationBypassTests.GetLicenseServerUrl_IgnoresPreferredServerUrl_AfterTrustedUrlsExist",
            "YUCP.Importer.Editor.Tests.ImporterVerificationBypassTests.VerifyInBrowserAsync_RedeemsInvalidToken_AndStillInvokesSuccess",
            "YUCP.Importer.Editor.Tests.PackageManagerWindowHostedVerificationTests.IsHostedImportVerified_TreatsHostedVerificationAsVerifiedWithoutSigningManifest",
            "YUCP.Importer.Editor.Tests.ProtectedImportBootstrapTests.ProtectedImportBootstrapCoordinator_ReconstructsInstalledPackageInfo_WhenSignedManifestLivesInGlobalSigningFolder",
            "YUCP.Importer.Editor.Tests.ProtectedImportBootstrapTests.ProtectedImportBootstrapCoordinator_ReconstructsInstalledPackageInfo_WhenMetadataAssetPathPointsOutsideShellRoot",
            "YUCP.Importer.Editor.Tests.ProtectedImportBootstrapTests.ProtectedImportBootstrapCoordinator_FailsWhenProtectedPayloadDescriptorDoesNotMatchSignedManifest",
            "YUCP.Importer.Editor.Tests.TrustedAuthorityTests.GetPublicKey_ReturnsCachedTrustedUrlKey",
            "YUCP.Importer.Editor.Tests.TrustedAuthorityTests.GetPublicKey_IgnoresCachedKeyThatDoesNotMatchPinnedRoots",
            "YUCP.Importer.Editor.Tests.Ed25519WrapperSecurityTests.TryLoadVerifiedChaosNaClAssembly_RejectsUnexpectedHash",
            "YUCP.Importer.Editor.Tests.Ed25519WrapperSecurityTests.TryLoadVerifiedChaosNaClAssembly_LoadsPinnedPlugin",
            "YUCP.Importer.Editor.Tests.CouplingImportGuardTests.TryApplyCouplingOrRollback_WhenCouplingFails_RollsBackImportedAssets",
            "YUCP.Importer.Editor.Tests.CouplingImportGuardTests.ShouldApplyDuringShellImport_WhenProtectedPayloadExists_ReturnsFalse",
            "YUCP.Importer.Editor.Tests.CouplingImportGuardTests.BuildProtectedPayloadCouplingFiles_WhenExtractedAssetsProvided_MergesShellAndFinalFiles",
            "YUCP.Importer.Editor.Tests.CouplingImportGuardTests.TryApplyDeferredProtectedPayloadCouplingOrRollback_WhenCouplingFails_RollsBackShellAndExtractedAssets",
            "YUCP.Importer.Editor.Tests.CreatorIdentityOAuthServiceTests.VerificationIntentService_OpenVerificationUrl_FromBackgroundThread_MarshalsOpenUrl",
            "YUCP.DevTools.Editor.PackageExporter.Tests.CertificateTrustSyncTests.TryGetTrustedRootPublicKey_UsesPinnedRootForKnownKeyIds",
            "YUCP.DevTools.Editor.PackageExporter.Tests.ExporterExploitProofTests.GetServerUrl_ReturnsNull_WhenMultipleSigningSettingsAssetsExistWithoutCanonicalAsset",
            "YUCP.DevTools.Editor.PackageExporter.Tests.ExporterExploitProofTests.AddonManager_RequiresExplicitTrustRegistration",
            "YUCP.DevTools.Editor.PackageExporter.Tests.ExporterExploitProofTests.AddonManager_InvokesExplicitlyTrustedAddon",
            "YUCP.DevTools.Editor.PackageExporter.Tests.ExporterExploitProofTests.ConfuserExManager_RejectsArchiveHashMismatch",
            "YUCP.DevTools.Editor.PackageExporter.Tests.ExporterExploitProofTests.ConfuserExExtraction_RejectsZipSlipOutsideExtractRoot",
            "YUCP.DevTools.Editor.PackageExporter.Tests.ExporterExploitProofTests.ArchiveExtractionUtility_RejectsTarTraversalOutsideExtractRoot",
            "YUCP.DevTools.Editor.PackageExporter.Tests.ExporterExploitProofTests.DirectVpmInstaller_RejectsPackageWhenHashDoesNotMatchRepositoryMetadata",
            "YUCP.DevTools.Editor.PackageExporter.Tests.ExporterExploitProofTests.DirectVpmInstaller_RejectsPackageWhenRepositoryHashMetadataIsMissing",
            "YUCP.DevTools.Editor.PackageExporter.Tests.ExporterExploitProofTests.DirectVpmInstaller_RejectsUnsafePackageNamePaths",
            "YUCP.DevTools.Editor.PackageExporter.Tests.ExporterExploitProofTests.DirectVpmInstaller_TransitiveDependenciesDoNotExpandRepositoryTrust",
            "YUCP.DevTools.Editor.PackageExporter.Tests.ExporterExploitProofTests.Ed25519Wrapper_OnlyTrustsPinnedChaosNaClBinary",
            "YUCP.DevTools.Editor.PackageExporter.Tests.ExporterExploitProofTests.HDiffPatchWrapper_OnlyTrustsPinnedNativeLibraries",
            "YUCP.DevTools.Editor.PackageExporter.Tests.ExporterExploitProofTests.TrustedMilestoneTracker_ResolvesTypeFromTrustedAssemblyOnly",
            "YUCP.DevTools.Editor.PackageExporter.Tests.GeneratedPackageGuardianTemplateHardeningTests.GuardianTransaction_Rollback_RemovesCreatedDestinationAndRestoresSource",
            "YUCP.DevTools.Editor.PackageExporter.Tests.GeneratedPackageGuardianTemplateHardeningTests.GuardianTransaction_BackupFile_ThrowsWhenSnapshotCannotBeCaptured",
        };

        public static void Run()
        {
            try
            {
                string resultsPath =
                    GetArgumentValue("-auditResultsPath")
                    ?? @"C:\Users\svalp\AppData\Local\Temp\importer-bypass-results.json";

                string[] requestedTests = GetArgumentValues("-auditTest")
                    .DefaultIfEmpty(null)
                    .FirstOrDefault() == null
                    ? DefaultTestNames
                    : GetArgumentValues("-auditTest").ToArray();

                Debug.Log($"[ImporterAudit] Running {requestedTests.Length} audit tests.");

                var runner = ScriptableObject.CreateInstance<TestRunnerApi>();
                runner.RegisterCallbacks(new AuditRunCallbacks(resultsPath, requestedTests));

                var filter = new Filter
                {
                    testMode = TestMode.EditMode,
                    testNames = requestedTests,
                };

                runner.Execute(new ExecutionSettings(filter));
            }
            catch (Exception ex)
            {
                string fallbackResultsPath =
                    GetArgumentValue("-auditResultsPath")
                    ?? @"C:\Users\svalp\AppData\Local\Temp\importer-bypass-results.json";
                WriteFailureAndExit(fallbackResultsPath, $"Failed to start audit test run: {ex}");
            }
        }

        private static string GetArgumentValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static IEnumerable<string> GetArgumentValues(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    yield return args[i + 1];
                }
            }
        }

        private static void WriteFailureAndExit(string resultsPath, string error)
        {
            WriteReport(
                resultsPath,
                new AuditRunReport
                {
                    status = "error",
                    error = error,
                    requestedTests = DefaultTestNames,
                    tests = Array.Empty<AuditTestResult>(),
                });

            EditorApplication.delayCall += () => EditorApplication.Exit(2);
        }

        private static void WriteReport(string resultsPath, AuditRunReport report)
        {
            string directory = Path.GetDirectoryName(resultsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(resultsPath, JsonUtility.ToJson(report, true));
        }

        [Serializable]
        private sealed class AuditRunReport
        {
            public string status;
            public string error;
            public int total;
            public int passed;
            public int failed;
            public int skipped;
            public int inconclusive;
            public string[] requestedTests;
            public AuditTestResult[] tests;
        }

        [Serializable]
        private sealed class AuditTestResult
        {
            public string fullName;
            public string resultState;
            public string message;
            public string stackTrace;
        }

        private sealed class AuditRunCallbacks : ICallbacks
        {
            private readonly string _resultsPath;
            private readonly string[] _requestedTests;
            private readonly List<AuditTestResult> _results = new List<AuditTestResult>();
            private bool _finished;

            public AuditRunCallbacks(string resultsPath, string[] requestedTests)
            {
                _resultsPath = resultsPath;
                _requestedTests = requestedTests;
            }

            public void RunStarted(ITestAdaptor testsToRun)
            {
                Debug.Log("[ImporterAudit] Test run started.");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                if (_finished)
                {
                    return;
                }

                _finished = true;

                int total = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
                var report = new AuditRunReport
                {
                    status = result.FailCount > 0 ? "failed" : "passed",
                    total = total,
                    passed = result.PassCount,
                    failed = result.FailCount,
                    skipped = result.SkipCount,
                    inconclusive = result.InconclusiveCount,
                    requestedTests = _requestedTests,
                    tests = _results.OrderBy(test => test.fullName, StringComparer.Ordinal).ToArray(),
                };

                if (total == 0)
                {
                    report.status = "error";
                    report.error = "Unity reported zero executed tests for the requested audit set.";
                }

                WriteReport(_resultsPath, report);
                int exitCode = report.status == "passed" ? 0 : 1;
                EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.Test == null || result.Test.HasChildren)
                {
                    return;
                }

                _results.Add(
                    new AuditTestResult
                    {
                        fullName = result.Test.FullName,
                        resultState = result.ResultState?.ToString() ?? string.Empty,
                        message = result.Message ?? string.Empty,
                        stackTrace = result.StackTrace ?? string.Empty,
                    });
            }
        }
    }
}
