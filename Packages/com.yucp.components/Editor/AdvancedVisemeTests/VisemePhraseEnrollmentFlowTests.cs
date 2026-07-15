using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using YUCP.Components.Editor.VisemePhrase;

namespace YUCP.Components.Editor.Tests
{
    public sealed class VisemePhraseEnrollmentFlowTests
    {
        [Test]
        public void ResetPreservesDraftOrderAndSelectsFirstBlockingPhrase()
        {
            var ready = ReadyDraft("ready");
            var missingPrompt = new FakeDraft("missing prompt", string.Empty);
            var missingTakes = new FakeDraft("missing takes", "open the portal");
            var ordered = new IVisemePhraseEnrollmentDraft[]
            {
                ready,
                missingPrompt,
                missingTakes
            };

            var flow = new VisemePhraseEnrollmentFlow(ordered);

            Assert.That(flow.Drafts, Is.EqualTo(ordered));
            Assert.That(flow.PhraseIndex, Is.EqualTo(1));
            Assert.That(flow.CurrentDraft, Is.SameAs(missingPrompt));
            Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Phrase));
        }

        [Test]
        public void ExplicitStartSelectsPhraseWithoutReorderingDrafts()
        {
            var first = new FakeDraft("first", string.Empty);
            var second = new FakeDraft("second", "second phrase");
            var third = new FakeDraft("third", "third phrase");
            var ordered = new IVisemePhraseEnrollmentDraft[] { first, second, third };

            var flow = new VisemePhraseEnrollmentFlow(ordered, 2);

            Assert.That(flow.PhraseIndex, Is.EqualTo(2));
            Assert.That(flow.CurrentDraft, Is.SameAs(third));
            Assert.That(flow.Drafts, Is.EqualTo(ordered));
        }

        [Test]
        public void MicrophoneReadinessIsSharedAcrossPhraseNavigation()
        {
            var first = new FakeDraft("first", "first phrase");
            var second = new FakeDraft("second", "second phrase");
            var flow = new VisemePhraseEnrollmentFlow(
                new IVisemePhraseEnrollmentDraft[] { first, second },
                0);

            Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Microphone));
            flow.SetMicrophoneReady();
            Assert.That(
                flow.TryNext(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.Success));
            Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Takes));

            Assert.That(
                flow.SelectPhrase(1),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.Success));
            Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Takes));

            flow.SetMicrophoneReady(false);
            Assert.That(
                flow.SelectPhrase(0),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.Success));
            Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Microphone));
        }

        [Test]
        public void RecordingLocksPhraseTakeAndStepNavigation()
        {
            var first = new FakeDraft("first", "first phrase");
            var second = new FakeDraft("second", "second phrase");
            var flow = new VisemePhraseEnrollmentFlow(
                new IVisemePhraseEnrollmentDraft[] { first, second },
                0);
            flow.SetMicrophoneReady();
            flow.TryNext();
            flow.SetRecordingLocked(true);

            Assert.That(
                flow.SelectPhrase(1),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.RecordingInProgress));
            Assert.That(
                flow.SelectTake(1),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.RecordingInProgress));
            Assert.That(
                flow.TryNext(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.RecordingInProgress));
            Assert.That(
                flow.TryBack(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.RecordingInProgress));
            Assert.That(flow.PhraseIndex, Is.Zero);
            Assert.That(flow.TakeIndex, Is.Zero);
            Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Takes));

            flow.SetRecordingLocked(false);
            Assert.That(
                flow.SelectTake(1),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.Success));
            Assert.That(flow.TakeIndex, Is.EqualTo(1));
        }

        [Test]
        public void ReadyReviewRoutesToNextIncompletePhraseAndWrapsStableOrder()
        {
            var incomplete = new FakeDraft("incomplete", "first phrase");
            var readyMiddle = ReadyDraft("ready middle");
            var readyLast = ReadyDraft("ready last");
            var flow = new VisemePhraseEnrollmentFlow(
                new IVisemePhraseEnrollmentDraft[] { incomplete, readyMiddle, readyLast },
                2);

            Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Review));
            Assert.That(
                flow.TryNext(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.Success));
            Assert.That(flow.PhraseIndex, Is.Zero,
                "The forward search should wrap and keep the original phrase order.");
            Assert.That(flow.CurrentDraft, Is.SameAs(incomplete));
            Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Microphone));
        }

        [Test]
        public void CompletingLastIncompletePhraseReportsAvatarReady()
        {
            var first = ReadyDraft("first");
            var last = new FakeDraft("last", "last phrase");
            var flow = new VisemePhraseEnrollmentFlow(
                new IVisemePhraseEnrollmentDraft[] { first, last },
                1);
            flow.SetMicrophoneReady();

            for (var i = 0; i < VisemePhraseEnrollmentStatus.RequiredTakeCount; i++)
                last.SaveTake(i, UsefulTake());
            Assert.That(
                flow.RouteToRecommendedStep(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.Success));
            Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Review));
            Assert.That(flow.ReadyPhraseCount, Is.EqualTo(2));
            Assert.That(flow.AllPhrasesReady, Is.True);
            Assert.That(
                flow.TryNext(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.AllPhrasesReady));
            Assert.That(flow.PhraseIndex, Is.EqualTo(1));
        }

        [Test]
        public void WarningOnlyEnrollmentCanFinishWithoutSafetySample()
        {
            var warning = ReadyDraft("fallback", "YUCP fallback");
            var flow = new VisemePhraseEnrollmentFlow(
                new IVisemePhraseEnrollmentDraft[] { warning });

            Assert.That(flow.CurrentStatus.IsReady, Is.True);
            Assert.That(flow.CurrentStatus.issues.Any(issue =>
                issue.level == VisemePhraseEnrollmentIssueLevel.Warning), Is.True);
            Assert.That(flow.CurrentStatus.issues.Any(issue =>
                issue.target == VisemePhraseEnrollmentIssueTarget.NegativeSample &&
                issue.level == VisemePhraseEnrollmentIssueLevel.Info), Is.True);
            Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Review));
            Assert.That(
                flow.TryNext(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.AllPhrasesReady));
        }

        [Test]
        public void FirstBlockingLocationNamesPhraseStepAndTake()
        {
            var ready = ReadyDraft("ready");
            var broken = ReadyDraft("broken");
            broken.SaveTake(2, null);
            var laterPrompt = new FakeDraft("later", string.Empty);
            var flow = new VisemePhraseEnrollmentFlow(
                new IVisemePhraseEnrollmentDraft[] { ready, broken, laterPrompt },
                0);

            Assert.That(flow.TryGetFirstBlockingLocation(out var location), Is.True);
            Assert.That(location.phraseIndex, Is.EqualTo(1));
            Assert.That(location.step, Is.EqualTo(VisemePhraseEnrollmentStep.Takes));
            Assert.That(location.takeIndex, Is.EqualTo(2));
            Assert.That(location.issue.target,
                Is.EqualTo(VisemePhraseEnrollmentIssueTarget.PositiveTake));
            Assert.That(location.issue.message, Is.EqualTo("Record a clear take in slot 3."));
        }

        [Test]
        public void TryNextGatesEachConsumerStepAgainstLiveDraftState()
        {
            var draft = new FakeDraft("phrase", string.Empty);
            var flow = new VisemePhraseEnrollmentFlow(
                new IVisemePhraseEnrollmentDraft[] { draft });

            Assert.That(
                flow.TryNext(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.ValidationBlocked));
            draft.Prompt = "open the portal";
            Assert.That(
                flow.TryNext(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.Success));
            Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Microphone));
            Assert.That(
                flow.TryNext(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.ValidationBlocked));

            flow.SetMicrophoneReady();
            Assert.That(
                flow.TryNext(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.Success));
            Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Takes));
            Assert.That(
                flow.TryNext(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.ValidationBlocked));
            Assert.That(flow.TakeIndex, Is.Zero);

            for (var i = 0; i < VisemePhraseEnrollmentStatus.RequiredTakeCount; i++)
                draft.SaveTake(i, UsefulTake());
            Assert.That(
                flow.TryNext(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.Success));
            Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Review));
        }

        [Test]
        public void BackTraversesTheThreeConsumerStepsInOrder()
        {
            var flow = new VisemePhraseEnrollmentFlow(
                new IVisemePhraseEnrollmentDraft[] { ReadyDraft("ready") });

            Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Review));
            Assert.That(flow.TryBack(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.Success));
            Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Takes));
            Assert.That(flow.TryBack(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.Success));
            Assert.That(flow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Microphone));
            Assert.That(flow.TryBack(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.NoIncompletePhrase));
        }

        [Test]
        public void NewPhraseStartsAtMicrophoneAndInProgressPhraseResumesAtBlocker()
        {
            var draft = new FakeDraft("phrase", "open the portal");
            var newFlow = new VisemePhraseEnrollmentFlow(
                new IVisemePhraseEnrollmentDraft[] { draft });

            Assert.That(newFlow.CurrentStatus.CompletedTakes, Is.Zero);
            Assert.That(newFlow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Microphone));
            newFlow.SetMicrophoneReady();
            Assert.That(newFlow.TryNext(),
                Is.EqualTo(VisemePhraseEnrollmentNavigationResult.Success));
            Assert.That(newFlow.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Takes));

            draft.SaveTake(0, UsefulTake());
            var resumed = new VisemePhraseEnrollmentFlow(
                new IVisemePhraseEnrollmentDraft[] { draft });

            Assert.That(resumed.CurrentStatus.CompletedTakes, Is.EqualTo(1));
            Assert.That(resumed.Step, Is.EqualTo(VisemePhraseEnrollmentStep.Takes));
            Assert.That(resumed.TakeIndex, Is.EqualTo(1));
        }

        private static FakeDraft ReadyDraft(
            string name,
            string backend = "Oculus LipSync")
        {
            var draft = new FakeDraft(name, name + " phrase");
            for (var i = 0; i < VisemePhraseEnrollmentStatus.RequiredTakeCount; i++)
                draft.SaveTake(i, UsefulTake(backend));
            return draft;
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

        private sealed class FakeDraft : IVisemePhraseEnrollmentDraft
        {
            private readonly List<VisemePhraseCapturedTake> takes =
                Enumerable.Repeat<VisemePhraseCapturedTake>(
                    null,
                    VisemePhraseEnrollmentStatus.RequiredTakeCount).ToList();

            internal FakeDraft(string name, string prompt)
            {
                DisplayName = name;
                Prompt = prompt;
            }

            public UnityEngine.Object TargetObject => null;
            public GameObject AvatarRoot => null;
            public string DisplayName { get; }
            public string Prompt { get; set; }
            public IReadOnlyList<VisemePhraseCapturedTake> Takes => takes;
            public VisemePhraseCapturedTake NegativeSample { get; private set; }
            public UnityEngine.Object ProfileAsset => null;
            public string AssetPath => string.Empty;

            public void SavePrompt()
            {
            }

            public void SaveTake(int index, VisemePhraseCapturedTake take)
            {
                takes[index] = take;
            }

            public void SaveNegativeSample(VisemePhraseCapturedTake take)
            {
                NegativeSample = take;
            }

            public void ClearNegativeSample()
            {
                NegativeSample = null;
            }
        }
    }
}
