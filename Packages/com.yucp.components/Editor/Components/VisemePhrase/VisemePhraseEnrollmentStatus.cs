using System;
using System.Collections.Generic;
using System.Linq;

namespace YUCP.Components.Editor.VisemePhrase
{
    internal enum VisemePhraseEnrollmentIssueLevel
    {
        Info,
        Warning,
        Blocking
    }

    internal enum VisemePhraseEnrollmentIssueTarget
    {
        General,
        Prompt,
        PositiveTake,
        NegativeSample,
        Compilation
    }

    internal readonly struct VisemePhraseEnrollmentIssue
    {
        internal readonly VisemePhraseEnrollmentIssueLevel level;
        internal readonly string message;
        internal readonly VisemePhraseEnrollmentIssueTarget target;
        internal readonly int takeIndex;

        internal VisemePhraseEnrollmentIssue(
            VisemePhraseEnrollmentIssueLevel level,
            string message,
            VisemePhraseEnrollmentIssueTarget target = VisemePhraseEnrollmentIssueTarget.General,
            int takeIndex = -1)
        {
            this.level = level;
            this.message = message ?? string.Empty;
            this.target = target;
            this.takeIndex = takeIndex;
        }
    }

    /// <summary>
    /// Pure enrollment validation shared by the inspector, wizard and build guard.
    /// The build callback itself is deliberately owned by the processor integration.
    /// </summary>
    internal sealed class VisemePhraseEnrollmentStatus
    {
        internal const int RequiredTakeCount = 4;
        // Short single-syllable words such as "Cube" commonly stabilize to
        // only three Oculus visemes (I -> U -> PP/kk). Requiring a fourth run
        // rewards classifier flicker instead of useful phonetic information.
        internal const int MinimumInformativeRuns = 3;
        internal const int HealthyInformativeRuns = 6;

        internal readonly List<VisemePhraseEnrollmentIssue> issues =
            new List<VisemePhraseEnrollmentIssue>();

        internal int CompletedTakes { get; private set; }
        internal bool HasBlockingIssues => issues.Any(issue =>
            issue.level == VisemePhraseEnrollmentIssueLevel.Blocking);
        internal bool IsReady => !HasBlockingIssues && CompletedTakes == RequiredTakeCount;
        internal string BlockingReason => issues.FirstOrDefault(issue =>
            issue.level == VisemePhraseEnrollmentIssueLevel.Blocking).message;

        internal static VisemePhraseEnrollmentStatus Evaluate(
            string prompt,
            IReadOnlyList<VisemePhraseCapturedTake> takes,
            VisemePhraseCapturedTake negativeSample = null)
        {
            var status = new VisemePhraseEnrollmentStatus();
            if (string.IsNullOrWhiteSpace(prompt))
                status.issues.Add(new VisemePhraseEnrollmentIssue(
                    VisemePhraseEnrollmentIssueLevel.Blocking,
                    "Enter the phrase you will say before recording takes.",
                    VisemePhraseEnrollmentIssueTarget.Prompt));

            var takeCount = takes?.Count ?? 0;
            for (var i = 0; i < RequiredTakeCount; i++)
            {
                var take = i < takeCount ? takes[i] : null;
                if (take == null || !take.IsUseful())
                {
                    status.issues.Add(new VisemePhraseEnrollmentIssue(
                        VisemePhraseEnrollmentIssueLevel.Blocking,
                        $"Record a clear take in slot {i + 1}.",
                        VisemePhraseEnrollmentIssueTarget.PositiveTake,
                        i));
                    continue;
                }

                var runs = InformativeRuns(take);
                if (runs < MinimumInformativeRuns)
                {
                    status.issues.Add(new VisemePhraseEnrollmentIssue(
                        VisemePhraseEnrollmentIssueLevel.Blocking,
                        $"Take {i + 1} has only {runs} distinct speech shapes; at least {MinimumInformativeRuns} are required.",
                        VisemePhraseEnrollmentIssueTarget.PositiveTake,
                        i));
                    continue;
                }
                status.CompletedTakes++;
                if (runs < HealthyInformativeRuns)
                    status.issues.Add(new VisemePhraseEnrollmentIssue(
                        VisemePhraseEnrollmentIssueLevel.Warning,
                        $"Take {i + 1} has {runs} distinct speech shapes. Six or more usually separates a phrase more reliably.",
                        VisemePhraseEnrollmentIssueTarget.PositiveTake,
                        i));

                if (!string.Equals(take.backend, "Oculus LipSync", StringComparison.OrdinalIgnoreCase))
                    status.issues.Add(new VisemePhraseEnrollmentIssue(
                        VisemePhraseEnrollmentIssueLevel.Warning,
                        $"Take {i + 1} used {FallbackName(take.backend)}. Install Oculus LipSync and retake it for the closest VRChat classifier match.",
                        VisemePhraseEnrollmentIssueTarget.PositiveTake,
                        i));
            }

            if (takeCount > RequiredTakeCount)
                status.issues.Add(new VisemePhraseEnrollmentIssue(
                    VisemePhraseEnrollmentIssueLevel.Blocking,
                    $"Enrollment must contain exactly {RequiredTakeCount} phrase takes.",
                    VisemePhraseEnrollmentIssueTarget.PositiveTake));

            if (negativeSample == null)
                status.issues.Add(new VisemePhraseEnrollmentIssue(
                    VisemePhraseEnrollmentIssueLevel.Info,
                    "A 15-second normal-speech sample is optional, but helps estimate accidental-trigger risk.",
                    VisemePhraseEnrollmentIssueTarget.NegativeSample));
            else if (negativeSample.durationSeconds < 14.5d)
                status.issues.Add(new VisemePhraseEnrollmentIssue(
                    VisemePhraseEnrollmentIssueLevel.Warning,
                    "The optional normal-speech sample is shorter than 15 seconds.",
                    VisemePhraseEnrollmentIssueTarget.NegativeSample));

            return status;
        }

        internal static VisemePhraseEnrollmentStatus Evaluate(
            VisemePhraseTriggerData component,
            VisemePhraseDefinition phrase)
        {
            if (component == null || phrase == null)
                return Evaluate(string.Empty, Array.Empty<VisemePhraseCapturedTake>());

            phrase.EnsureDefaults();
            if (component.enrollmentProfile != null &&
                component.enrollmentProfile.profileSchemaVersion !=
                VisemePhraseEnrollmentProfile.CurrentProfileSchemaVersion)
            {
                var outdated = new VisemePhraseEnrollmentStatus();
                outdated.issues.Add(new VisemePhraseEnrollmentIssue(
                    VisemePhraseEnrollmentIssueLevel.Blocking,
                    "This enrollment profile uses a different package data format. Open Record / Improve to create a current personal enrollment.",
                    VisemePhraseEnrollmentIssueTarget.Compilation));
                return outdated;
            }
            var enrollment = component.enrollmentProfile?.FindEnrollment(
                phrase.id,
                phrase.PromptFingerprint);
            var takes = enrollment?.positiveTakes == null
                ? Array.Empty<VisemePhraseCapturedTake>()
                : enrollment.positiveTakes
                    .Select(VisemePhraseEnrollmentDraft.FromTrace)
                    .ToArray();
            var negative = enrollment?.negativeTraces != null && enrollment.negativeTraces.Count > 0
                ? VisemePhraseEnrollmentDraft.FromTrace(enrollment.negativeTraces[0])
                : null;
            var status = Evaluate(phrase.prompt, takes, negative);
            if (status.HasBlockingIssues || enrollment == null) return status;

            var compilerMessages = VisemePhraseValidation.Validate(phrase, enrollment);
            var compilerHasErrors = compilerMessages.Any(message =>
                message != null &&
                message.severity == VisemePhraseDiagnosticSeverity.Error);
            for (var i = 0; i < compilerMessages.Count; i++)
            {
                var message = compilerMessages[i];
                if (message == null || string.IsNullOrWhiteSpace(message.message)) continue;
                AddDiagnostic(status, message);
            }

            var model = enrollment.compiledModel;
            if (!compilerHasErrors)
            {
                if (model == null ||
                    model.modelSchemaVersion !=
                    VisemePhraseCompiledModel.CurrentModelSchemaVersion ||
                    model.contextMode != phrase.mode ||
                    Math.Abs(model.strictness - phrase.strictness) > 0.0001f)
                {
                    // Raw traces are the creator-authored data; the model is a
                    // derived cache. Preview the deterministic current bake so
                    // a package/schema or settings update does not falsely send
                    // a completed creator back into the microphone wizard. The
                    // build preflight persists this cache only after it passes.
                    model = VisemePhraseModelCompiler.Compile(
                        phrase, enrollment).model;
                }
                if (model == null)
                {
                    status.issues.Add(new VisemePhraseEnrollmentIssue(
                        VisemePhraseEnrollmentIssueLevel.Blocking,
                        "The existing takes could not be compiled. Review their diagnostics before continuing.",
                        VisemePhraseEnrollmentIssueTarget.Compilation));
                }
                else if (model.diagnostics == null || !model.diagnostics.valid)
                {
                    var previousIssueCount = status.issues.Count;
                    if (model.diagnostics?.messages != null)
                    {
                        foreach (var message in model.diagnostics.messages)
                        {
                            if (message == null ||
                                string.IsNullOrWhiteSpace(message.message) ||
                                status.issues.Any(issue => string.Equals(
                                    issue.message,
                                    message.message,
                                    StringComparison.Ordinal)))
                                continue;
                            AddDiagnostic(status, message);
                        }
                    }

                    if (status.issues.Count == previousIssueCount)
                        status.issues.Add(new VisemePhraseEnrollmentIssue(
                            VisemePhraseEnrollmentIssueLevel.Blocking,
                            "The compiled enrollment is invalid. Review the take diagnostics and retake the highlighted example.",
                            VisemePhraseEnrollmentIssueTarget.Compilation));
                }
            }
            return status;
        }

        private static void AddDiagnostic(
            VisemePhraseEnrollmentStatus status,
            VisemePhraseDiagnostic message)
        {
            if (status == null || message == null ||
                string.IsNullOrWhiteSpace(message.message)) return;
            var takeIndex = DiagnosticTakeIndex(message.message);
            var target = takeIndex >= 0
                ? VisemePhraseEnrollmentIssueTarget.PositiveTake
                : VisemePhraseEnrollmentIssueTarget.Compilation;
            switch (message.severity)
            {
                case VisemePhraseDiagnosticSeverity.Error:
                    status.issues.Add(new VisemePhraseEnrollmentIssue(
                        VisemePhraseEnrollmentIssueLevel.Blocking,
                        message.message,
                        target,
                        takeIndex));
                    break;
                case VisemePhraseDiagnosticSeverity.Warning:
                    status.issues.Add(new VisemePhraseEnrollmentIssue(
                        VisemePhraseEnrollmentIssueLevel.Warning,
                        message.message,
                        target,
                        takeIndex));
                    break;
            }
        }

        internal static int DiagnosticTakeIndex(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return -1;
            foreach (var markerText in new[] { "slot ", "take " })
            {
                var searchFrom = 0;
                while (searchFrom < message.Length)
                {
                    var marker = message.IndexOf(
                        markerText,
                        searchFrom,
                        StringComparison.OrdinalIgnoreCase);
                    if (marker < 0) break;
                    searchFrom = marker + 1;
                    if (marker > 0 && char.IsLetterOrDigit(message[marker - 1]))
                        continue;

                    marker += markerText.Length;
                    var value = 0;
                    var foundDigit = false;
                    while (marker < message.Length && char.IsDigit(message[marker]))
                    {
                        foundDigit = true;
                        value = value * 10 + (message[marker] - '0');
                        marker++;
                    }
                    if (foundDigit && value >= 1 && value <= RequiredTakeCount)
                        return value - 1;
                }
            }
            return -1;
        }

        internal static VisemePhraseEnrollmentStatus Evaluate(
            IVisemePhraseEnrollmentDraft draft)
        {
            if (draft is VisemePhraseEnrollmentDraft stored)
                return Evaluate(stored.Component, stored.Phrase);
            return Evaluate(
                draft?.Prompt,
                draft?.Takes,
                draft?.NegativeSample);
        }

        internal static bool TryGetBlockingReason(
            VisemePhraseTriggerData component,
            out string reason)
        {
            if (component == null)
            {
                reason = "The Viseme Phrase Trigger component is missing.";
                return true;
            }
            if (component.phrases == null || component.phrases.Count == 0)
            {
                reason = "Add at least one phrase to the Viseme Phrase Trigger.";
                return true;
            }

            for (var i = 0; i < component.phrases.Count; i++)
            {
                var status = Evaluate(component, component.phrases[i]);
                if (!status.HasBlockingIssues) continue;
                var label = string.IsNullOrWhiteSpace(component.phrases[i]?.prompt)
                    ? $"Phrase {i + 1}"
                    : component.phrases[i].prompt.Trim();
                reason = $"{label}: {status.BlockingReason}";
                return true;
            }

            reason = string.Empty;
            return false;
        }

        internal static int InformativeRuns(VisemePhraseCapturedTake take)
        {
            return StableTokens(take).Count(token => token.viseme != 0);
        }

        internal static IReadOnlyList<int> TokenRuns(VisemePhraseCapturedTake take)
        {
            return StableTokens(take)
                .Where(token => token.viseme != 0)
                .Select(token => token.viseme)
                .ToArray();
        }

        private static IReadOnlyList<VisemePhraseToken> StableTokens(
            VisemePhraseCapturedTake take)
        {
            if (take == null) return Array.Empty<VisemePhraseToken>();
            return VisemePhraseTraceMath.RemoveTransientRuns(
                VisemePhraseTraceMath.RunLengthEncode(
                    VisemePhraseTraceMath.Trim(
                        VisemePhraseEnrollmentDraft.ToTrace(take))));
        }

        private static string FallbackName(string backend)
        {
            return string.IsNullOrWhiteSpace(backend) ? "the fallback classifier" : backend.Trim();
        }
    }
}
