using System;
using System.Collections.Generic;
using System.Linq;

namespace YUCP.Components.Editor.VisemePhrase
{
    internal enum VisemePhraseEnrollmentStep
    {
        Phrase,
        Microphone,
        Takes,
        Review
    }

    internal enum VisemePhraseEnrollmentNavigationResult
    {
        Success,
        AlreadyThere,
        RecordingInProgress,
        ValidationBlocked,
        InvalidTarget,
        NoIncompletePhrase,
        AllPhrasesReady
    }

    internal readonly struct VisemePhraseEnrollmentLocation
    {
        internal readonly int phraseIndex;
        internal readonly VisemePhraseEnrollmentStep step;
        internal readonly int takeIndex;
        internal readonly VisemePhraseEnrollmentIssue issue;

        internal VisemePhraseEnrollmentLocation(
            int phraseIndex,
            VisemePhraseEnrollmentStep step,
            int takeIndex,
            VisemePhraseEnrollmentIssue issue)
        {
            this.phraseIndex = phraseIndex;
            this.step = step;
            this.takeIndex = takeIndex;
            this.issue = issue;
        }
    }

    /// <summary>
    /// Pure navigation state for the enrollment window. It owns no visual elements,
    /// microphone resources, or avatar assets and re-evaluates drafts whenever a
    /// decision is made so UI enablement is never the only validation boundary.
    /// </summary>
    internal sealed class VisemePhraseEnrollmentFlow
    {
        private IReadOnlyList<IVisemePhraseEnrollmentDraft> drafts =
            Array.Empty<IVisemePhraseEnrollmentDraft>();

        internal IReadOnlyList<IVisemePhraseEnrollmentDraft> Drafts => drafts;
        internal int PhraseIndex { get; private set; } = -1;
        internal int TakeIndex { get; private set; }
        internal VisemePhraseEnrollmentStep Step { get; private set; }
        internal bool MicrophoneReady { get; private set; }
        internal bool RecordingLocked { get; private set; }
        internal IVisemePhraseEnrollmentDraft CurrentDraft =>
            PhraseIndex >= 0 && PhraseIndex < drafts.Count ? drafts[PhraseIndex] : null;
        internal VisemePhraseEnrollmentStatus CurrentStatus =>
            CurrentDraft == null ? null : StatusAt(PhraseIndex);
        internal int ReadyPhraseCount => drafts.Count(candidate =>
            VisemePhraseEnrollmentStatus.Evaluate(candidate).IsReady);
        internal bool AllPhrasesReady => drafts.Count > 0 && ReadyPhraseCount == drafts.Count;

        internal VisemePhraseEnrollmentFlow(
            IReadOnlyList<IVisemePhraseEnrollmentDraft> drafts,
            int startIndex = -1)
        {
            Reset(drafts, startIndex);
        }

        internal void Reset(
            IReadOnlyList<IVisemePhraseEnrollmentDraft> nextDrafts,
            int startIndex = -1)
        {
            if (nextDrafts == null)
            {
                drafts = Array.Empty<IVisemePhraseEnrollmentDraft>();
            }
            else
            {
                var ordered = nextDrafts.ToArray();
                if (ordered.Any(candidate => candidate == null))
                    throw new ArgumentException(
                        "Enrollment drafts cannot contain null entries.",
                        nameof(nextDrafts));
                drafts = ordered;
            }

            MicrophoneReady = false;
            RecordingLocked = false;
            TakeIndex = 0;
            if (drafts.Count == 0)
            {
                PhraseIndex = -1;
                Step = VisemePhraseEnrollmentStep.Phrase;
                return;
            }

            PhraseIndex = startIndex >= 0 && startIndex < drafts.Count
                ? startIndex
                : FirstBlockingPhraseIndex();
            if (PhraseIndex < 0) PhraseIndex = 0;
            ApplyRecommendedStep();
        }

        internal void SetMicrophoneReady(bool ready = true)
        {
            MicrophoneReady = ready;
        }

        internal void SetRecordingLocked(bool locked)
        {
            RecordingLocked = locked;
        }

        internal VisemePhraseEnrollmentStatus StatusAt(int phraseIndex)
        {
            if (phraseIndex < 0 || phraseIndex >= drafts.Count)
                throw new ArgumentOutOfRangeException(nameof(phraseIndex));
            return VisemePhraseEnrollmentStatus.Evaluate(drafts[phraseIndex]);
        }

        internal VisemePhraseEnrollmentNavigationResult SelectPhrase(int phraseIndex)
        {
            if (phraseIndex < 0 || phraseIndex >= drafts.Count)
                return VisemePhraseEnrollmentNavigationResult.InvalidTarget;
            if (phraseIndex == PhraseIndex)
                return VisemePhraseEnrollmentNavigationResult.AlreadyThere;
            if (RecordingLocked)
                return VisemePhraseEnrollmentNavigationResult.RecordingInProgress;

            PhraseIndex = phraseIndex;
            ApplyRecommendedStep();
            return VisemePhraseEnrollmentNavigationResult.Success;
        }

        internal VisemePhraseEnrollmentNavigationResult SelectTake(int takeIndex)
        {
            if (takeIndex < 0 ||
                takeIndex >= VisemePhraseEnrollmentStatus.RequiredTakeCount ||
                CurrentDraft == null)
                return VisemePhraseEnrollmentNavigationResult.InvalidTarget;
            if (Step == VisemePhraseEnrollmentStep.Takes && TakeIndex == takeIndex)
                return VisemePhraseEnrollmentNavigationResult.AlreadyThere;
            if (RecordingLocked)
                return VisemePhraseEnrollmentNavigationResult.RecordingInProgress;

            TakeIndex = takeIndex;
            Step = VisemePhraseEnrollmentStep.Takes;
            return VisemePhraseEnrollmentNavigationResult.Success;
        }

        internal VisemePhraseEnrollmentNavigationResult TryNext()
        {
            if (CurrentDraft == null)
                return VisemePhraseEnrollmentNavigationResult.InvalidTarget;
            if (RecordingLocked)
                return VisemePhraseEnrollmentNavigationResult.RecordingInProgress;

            var status = CurrentStatus;
            switch (Step)
            {
                case VisemePhraseEnrollmentStep.Phrase:
                {
                    if (HasBlockingIssue(status, VisemePhraseEnrollmentIssueTarget.Prompt))
                        return VisemePhraseEnrollmentNavigationResult.ValidationBlocked;
                    Step = StepAfterPhrase(status);
                    SelectFirstProblemTake(status);
                    return VisemePhraseEnrollmentNavigationResult.Success;
                }
                case VisemePhraseEnrollmentStep.Microphone:
                    if (!MicrophoneReady)
                        return VisemePhraseEnrollmentNavigationResult.ValidationBlocked;
                    Step = VisemePhraseEnrollmentStep.Takes;
                    SelectFirstProblemTake(status);
                    return VisemePhraseEnrollmentNavigationResult.Success;
                case VisemePhraseEnrollmentStep.Takes:
                {
                    var blocker = status.issues.FirstOrDefault(issue =>
                        issue.level == VisemePhraseEnrollmentIssueLevel.Blocking &&
                        (issue.target == VisemePhraseEnrollmentIssueTarget.Prompt ||
                         issue.target == VisemePhraseEnrollmentIssueTarget.PositiveTake));
                    if (!string.IsNullOrEmpty(blocker.message) ||
                        status.CompletedTakes != VisemePhraseEnrollmentStatus.RequiredTakeCount)
                    {
                        if (blocker.takeIndex >= 0) TakeIndex = blocker.takeIndex;
                        return VisemePhraseEnrollmentNavigationResult.ValidationBlocked;
                    }
                    Step = VisemePhraseEnrollmentStep.Review;
                    return VisemePhraseEnrollmentNavigationResult.Success;
                }
                case VisemePhraseEnrollmentStep.Review:
                    if (!status.IsReady)
                        return VisemePhraseEnrollmentNavigationResult.ValidationBlocked;
                    return SelectNextIncompletePhrase()
                        ? VisemePhraseEnrollmentNavigationResult.Success
                        : VisemePhraseEnrollmentNavigationResult.AllPhrasesReady;
                default:
                    return VisemePhraseEnrollmentNavigationResult.InvalidTarget;
            }
        }

        internal VisemePhraseEnrollmentNavigationResult TryBack()
        {
            if (CurrentDraft == null)
                return VisemePhraseEnrollmentNavigationResult.InvalidTarget;
            if (RecordingLocked)
                return VisemePhraseEnrollmentNavigationResult.RecordingInProgress;

            switch (Step)
            {
                case VisemePhraseEnrollmentStep.Review:
                    Step = VisemePhraseEnrollmentStep.Takes;
                    return VisemePhraseEnrollmentNavigationResult.Success;
                case VisemePhraseEnrollmentStep.Takes:
                    Step = VisemePhraseEnrollmentStep.Microphone;
                    return VisemePhraseEnrollmentNavigationResult.Success;
                case VisemePhraseEnrollmentStep.Microphone:
                    return VisemePhraseEnrollmentNavigationResult.NoIncompletePhrase;
                default:
                    return VisemePhraseEnrollmentNavigationResult.NoIncompletePhrase;
            }
        }

        internal VisemePhraseEnrollmentNavigationResult RouteToRecommendedStep()
        {
            if (CurrentDraft == null)
                return VisemePhraseEnrollmentNavigationResult.InvalidTarget;
            var next = RecommendedStep(PhraseIndex);
            var nextTake = FirstProblemTake(StatusAt(PhraseIndex));
            if (next == Step && (next != VisemePhraseEnrollmentStep.Takes || nextTake == TakeIndex))
                return VisemePhraseEnrollmentNavigationResult.AlreadyThere;
            if (RecordingLocked)
                return VisemePhraseEnrollmentNavigationResult.RecordingInProgress;
            Step = next;
            if (next == VisemePhraseEnrollmentStep.Takes) TakeIndex = nextTake;
            return VisemePhraseEnrollmentNavigationResult.Success;
        }

        internal bool TryGetFirstBlockingLocation(
            out VisemePhraseEnrollmentLocation location)
        {
            for (var phraseIndex = 0; phraseIndex < drafts.Count; phraseIndex++)
            {
                var status = StatusAt(phraseIndex);
                var blocker = status.issues.FirstOrDefault(issue =>
                    issue.level == VisemePhraseEnrollmentIssueLevel.Blocking);
                if (string.IsNullOrEmpty(blocker.message)) continue;
                location = new VisemePhraseEnrollmentLocation(
                    phraseIndex,
                    StepFor(blocker),
                    blocker.takeIndex,
                    blocker);
                return true;
            }

            location = default;
            return false;
        }

        internal static VisemePhraseEnrollmentStep StepFor(
            VisemePhraseEnrollmentIssue issue)
        {
            switch (issue.target)
            {
                case VisemePhraseEnrollmentIssueTarget.Prompt:
                    return VisemePhraseEnrollmentStep.Phrase;
                case VisemePhraseEnrollmentIssueTarget.PositiveTake:
                    return VisemePhraseEnrollmentStep.Takes;
                default:
                    return VisemePhraseEnrollmentStep.Review;
            }
        }

        private int FirstBlockingPhraseIndex()
        {
            for (var i = 0; i < drafts.Count; i++)
                if (StatusAt(i).HasBlockingIssues)
                    return i;
            return -1;
        }

        private VisemePhraseEnrollmentStep RecommendedStep(int phraseIndex)
        {
            var status = StatusAt(phraseIndex);
            if (HasBlockingIssue(status, VisemePhraseEnrollmentIssueTarget.Prompt))
                return VisemePhraseEnrollmentStep.Phrase;
            if (HasBlockingIssue(status, VisemePhraseEnrollmentIssueTarget.Compilation))
                return VisemePhraseEnrollmentStep.Review;
            if (status.CompletedTakes == VisemePhraseEnrollmentStatus.RequiredTakeCount &&
                !HasBlockingIssue(status, VisemePhraseEnrollmentIssueTarget.PositiveTake))
                return VisemePhraseEnrollmentStep.Review;
            if (status.CompletedTakes > 0) return VisemePhraseEnrollmentStep.Takes;
            return MicrophoneReady
                ? VisemePhraseEnrollmentStep.Takes
                : VisemePhraseEnrollmentStep.Microphone;
        }

        private VisemePhraseEnrollmentStep StepAfterPhrase(
            VisemePhraseEnrollmentStatus status)
        {
            if (HasBlockingIssue(status, VisemePhraseEnrollmentIssueTarget.Compilation))
                return VisemePhraseEnrollmentStep.Review;
            if (status.CompletedTakes == VisemePhraseEnrollmentStatus.RequiredTakeCount &&
                !HasBlockingIssue(status, VisemePhraseEnrollmentIssueTarget.PositiveTake))
                return VisemePhraseEnrollmentStep.Review;
            return MicrophoneReady
                ? VisemePhraseEnrollmentStep.Takes
                : VisemePhraseEnrollmentStep.Microphone;
        }

        private void ApplyRecommendedStep()
        {
            Step = RecommendedStep(PhraseIndex);
            SelectFirstProblemTake(StatusAt(PhraseIndex));
        }

        private void SelectFirstProblemTake(VisemePhraseEnrollmentStatus status)
        {
            TakeIndex = FirstProblemTake(status);
        }

        private static int FirstProblemTake(VisemePhraseEnrollmentStatus status)
        {
            var blocker = status.issues
                .Where(issue =>
                    issue.level == VisemePhraseEnrollmentIssueLevel.Blocking &&
                    issue.target == VisemePhraseEnrollmentIssueTarget.PositiveTake &&
                    issue.takeIndex >= 0)
                .Select(issue => (int?)issue.takeIndex)
                .FirstOrDefault();
            return blocker ?? 0;
        }

        private bool SelectNextIncompletePhrase()
        {
            for (var offset = 1; offset < drafts.Count; offset++)
            {
                var index = (PhraseIndex + offset) % drafts.Count;
                if (StatusAt(index).IsReady) continue;
                PhraseIndex = index;
                ApplyRecommendedStep();
                return true;
            }
            return false;
        }

        private static bool HasBlockingIssue(
            VisemePhraseEnrollmentStatus status,
            VisemePhraseEnrollmentIssueTarget target)
        {
            return status.issues.Any(issue =>
                issue.level == VisemePhraseEnrollmentIssueLevel.Blocking &&
                issue.target == target);
        }
    }
}
