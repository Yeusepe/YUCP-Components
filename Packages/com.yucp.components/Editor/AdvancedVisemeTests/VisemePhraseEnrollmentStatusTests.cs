using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using YUCP.Components.Editor.VisemePhrase;

namespace YUCP.Components.Editor.Tests
{
    public sealed class VisemePhraseEnrollmentStatusTests
    {
        [Test]
        public void PromptAndTakeIssuesExposeTargetsWithoutChangingMessages()
        {
            var status = VisemePhraseEnrollmentStatus.Evaluate(
                string.Empty,
                Array.Empty<VisemePhraseCapturedTake>());

            var prompt = status.issues.Single(issue =>
                issue.target == VisemePhraseEnrollmentIssueTarget.Prompt);
            Assert.That(prompt.level, Is.EqualTo(VisemePhraseEnrollmentIssueLevel.Blocking));
            Assert.That(prompt.takeIndex, Is.EqualTo(-1));
            Assert.That(prompt.message,
                Is.EqualTo("Enter the phrase you will say before recording takes."));
            Assert.That(status.BlockingReason, Is.EqualTo(prompt.message));

            var takeIssues = status.issues.Where(issue =>
                issue.target == VisemePhraseEnrollmentIssueTarget.PositiveTake).ToArray();
            Assert.That(takeIssues, Has.Length.EqualTo(4));
            for (var i = 0; i < takeIssues.Length; i++)
            {
                Assert.That(takeIssues[i].takeIndex, Is.EqualTo(i));
                Assert.That(takeIssues[i].message,
                    Is.EqualTo($"Record a clear take in slot {i + 1}."));
            }
        }

        [Test]
        public void TakeWarningsRetainTheirSlotAndOptionalSampleIsNonBlocking()
        {
            var takes = new[]
            {
                UsefulTake("YUCP fallback"),
                UsefulTake(),
                UsefulTake(),
                UsefulTake()
            };

            var status = VisemePhraseEnrollmentStatus.Evaluate("open the portal", takes);

            var fallback = status.issues.Single(issue =>
                issue.message.Contains("used YUCP fallback"));
            Assert.That(fallback.target,
                Is.EqualTo(VisemePhraseEnrollmentIssueTarget.PositiveTake));
            Assert.That(fallback.takeIndex, Is.Zero);
            Assert.That(fallback.level, Is.EqualTo(VisemePhraseEnrollmentIssueLevel.Warning));

            var safety = status.issues.Single(issue =>
                issue.target == VisemePhraseEnrollmentIssueTarget.NegativeSample);
            Assert.That(safety.level, Is.EqualTo(VisemePhraseEnrollmentIssueLevel.Info));
            Assert.That(safety.takeIndex, Is.EqualTo(-1));
            Assert.That(safety.message, Is.EqualTo(
                "A 15-second normal-speech sample is optional, but helps estimate accidental-trigger risk."));
            Assert.That(status.IsReady, Is.True);
        }

        [Test]
        public void CleanThreeShapeShortWordIsReadyWithDistinctivenessWarning()
        {
            var takes = Enumerable.Range(0, 4)
                .Select(_ => ThreeShapeTake())
                .ToArray();

            var status = VisemePhraseEnrollmentStatus.Evaluate("Cube", takes);

            Assert.That(status.CompletedTakes, Is.EqualTo(4));
            Assert.That(status.IsReady, Is.True);
            Assert.That(status.issues.Any(issue =>
                issue.level == VisemePhraseEnrollmentIssueLevel.Blocking), Is.False);
            Assert.That(status.issues.Count(issue =>
                issue.level == VisemePhraseEnrollmentIssueLevel.Warning &&
                issue.target == VisemePhraseEnrollmentIssueTarget.PositiveTake),
                Is.EqualTo(4));
            Assert.That(VisemePhraseEnrollmentStatus.TokenRuns(takes[0]),
                Is.EqualTo(new[] { 12, 14, 1 }));
        }

        [Test]
        public void TransientThirdShapeDoesNotManufactureMinimumPhraseLength()
        {
            var take = BlockTake((12, 4), (14, 1), (1, 4));

            Assert.That(VisemePhraseEnrollmentStatus.TokenRuns(take),
                Is.EqualTo(new[] { 12, 1 }));
            Assert.That(VisemePhraseEnrollmentStatus.InformativeRuns(take),
                Is.EqualTo(2));

            var status = VisemePhraseEnrollmentStatus.Evaluate(
                "Cube",
                Enumerable.Repeat(take, 4).ToArray());
            Assert.That(status.IsReady, Is.False);
            Assert.That(status.issues.Any(issue =>
                issue.level == VisemePhraseEnrollmentIssueLevel.Blocking &&
                issue.target == VisemePhraseEnrollmentIssueTarget.PositiveTake), Is.True);
        }

        [Test]
        public void OutdatedProfileIssueRoutesToCompilationReview()
        {
            var root = new GameObject("Enrollment Status Target Test");
            var profile = ScriptableObject.CreateInstance<VisemePhraseEnrollmentProfile>();
            try
            {
                var component = root.AddComponent<VisemePhraseTriggerData>();
                var phrase = new VisemePhraseDefinition { prompt = "open the portal" };
                component.phrases.Add(phrase);
                component.EnsureDefaults();
                profile.profileSchemaVersion =
                    VisemePhraseEnrollmentProfile.CurrentProfileSchemaVersion - 1;
                component.enrollmentProfile = profile;

                var status = VisemePhraseEnrollmentStatus.Evaluate(component, phrase);

                Assert.That(status.issues, Has.Count.EqualTo(1));
                Assert.That(status.issues[0].target,
                    Is.EqualTo(VisemePhraseEnrollmentIssueTarget.Compilation));
                Assert.That(status.issues[0].takeIndex, Is.EqualTo(-1));
                Assert.That(
                    VisemePhraseEnrollmentFlow.StepFor(status.issues[0]),
                    Is.EqualTo(VisemePhraseEnrollmentStep.Review));
                Assert.That(status.issues[0].message, Is.EqualTo(
                    "This enrollment profile uses a different package data format. Open Record / Improve to create a current personal enrollment."));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CompilerOutlierMessageRoutesToItsConcreteTake()
        {
            Assert.That(VisemePhraseEnrollmentStatus.DiagnosticTakeIndex(
                "Retake slot 3: its viseme sequence does not agree with any other take."),
                Is.EqualTo(2));
            Assert.That(VisemePhraseEnrollmentStatus.DiagnosticTakeIndex(
                "Take slot 4 is less consistent than the other recordings."),
                Is.EqualTo(3));
            Assert.That(VisemePhraseEnrollmentStatus.DiagnosticTakeIndex(
                "Take 2 has only two distinct speech shapes."),
                Is.EqualTo(1));
            Assert.That(VisemePhraseEnrollmentStatus.DiagnosticTakeIndex(
                "Retake this example. Take 3 failed validation."),
                Is.EqualTo(2));
            Assert.That(VisemePhraseEnrollmentStatus.DiagnosticTakeIndex(
                "Mistake 2 is not a take diagnostic."),
                Is.EqualTo(-1));
            Assert.That(VisemePhraseEnrollmentStatus.DiagnosticTakeIndex(
                "The compiled enrollment is invalid."),
                Is.EqualTo(-1));
        }

        [Test]
        public void InvalidStoredModelPreservesConcreteCompilerRetake()
        {
            var root = new GameObject("Enrollment Compiler Diagnostic Test");
            var profile = ScriptableObject.CreateInstance<VisemePhraseEnrollmentProfile>();
            try
            {
                var component = root.AddComponent<VisemePhraseTriggerData>();
                var phrase = new VisemePhraseDefinition { prompt = "open the portal" };
                component.phrases.Add(phrase);
                component.EnsureDefaults();
                component.enrollmentProfile = profile;
                var enrollment = profile.GetOrCreateEnrollment(
                    phrase.id,
                    phrase.PromptFingerprint);
                for (var index = 0; index < 4; index++)
                    enrollment.positiveTakes.Add(
                        VisemePhraseEnrollmentDraft.ToTrace(UsefulTake()));
                enrollment.compiledModel = new VisemePhraseCompiledModel
                {
                    contextMode = phrase.mode,
                    strictness = phrase.strictness,
                    diagnostics = new VisemePhraseModelDiagnostics
                    {
                        valid = false,
                        messages =
                        {
                            new VisemePhraseDiagnostic(
                                VisemePhraseDiagnosticSeverity.Error,
                                "take_runtime_nonreplay",
                                "Retake slot 3: its exact Viseme trace cannot be represented.")
                        }
                    }
                };

                var status = VisemePhraseEnrollmentStatus.Evaluate(component, phrase);

                var issue = status.issues.Single(item =>
                    item.message.Contains("Retake slot 3"));
                Assert.That(issue.level,
                    Is.EqualTo(VisemePhraseEnrollmentIssueLevel.Blocking));
                Assert.That(issue.target,
                    Is.EqualTo(VisemePhraseEnrollmentIssueTarget.PositiveTake));
                Assert.That(issue.takeIndex, Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static VisemePhraseCapturedTake UsefulTake(
            string backend = "Oculus LipSync")
        {
            var take = new VisemePhraseCapturedTake
            {
                backend = backend,
                durationSeconds = 0.6d
            };
            for (var i = 0; i < 12; i++)
                take.frames.Add(new VisemePhraseCapturedFrame(
                    1 + i / 2,
                    0.55f,
                    i * 1024L,
                    48000));
            return take;
        }

        private static VisemePhraseCapturedTake ThreeShapeTake()
        {
            var take = new VisemePhraseCapturedTake
            {
                backend = "Oculus LipSync",
                durationSeconds = 0.4d
            };
            var clock = 0L;
            foreach (var viseme in new[] { 12, 14, 1 })
            for (var block = 0; block < 4; block++)
            {
                take.frames.Add(new VisemePhraseCapturedFrame(
                    viseme,
                    0.55f,
                    clock,
                    48000));
                clock += 1024L;
            }
            return take;
        }

        private static VisemePhraseCapturedTake BlockTake(
            params (int viseme, int blocks)[] runs)
        {
            var take = new VisemePhraseCapturedTake
            {
                backend = "Oculus LipSync"
            };
            var clock = 0L;
            foreach (var run in runs)
            for (var block = 0; block < run.blocks; block++)
            {
                take.frames.Add(new VisemePhraseCapturedFrame(
                    run.viseme,
                    0.55f,
                    clock,
                    48000));
                clock += 1024L;
            }
            take.durationSeconds = clock / 48000d;
            return take;
        }
    }
}
