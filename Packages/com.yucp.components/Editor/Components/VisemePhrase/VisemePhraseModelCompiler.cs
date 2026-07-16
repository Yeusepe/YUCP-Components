using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace YUCP.Components.Editor.VisemePhrase
{
    public sealed class VisemePhraseCompileOptions
    {
        public const int MaximumLearnedVariants = 2;
        public const int MaximumEnrolledPaths = 4;
        public const int MaximumProfilePaths = 8;
        public const int MaximumConfusionPaths = 2;
        public const int MaximumRuntimePaths =
            MaximumProfilePaths + MaximumConfusionPaths;
        public const int MaximumStatesPerVariant = 12;
        public const int MinimumInformativeRuns = 3;
        public const int RecommendedInformativeRuns = 6;

        public float voiceThreshold = 0.025f;
        public float trimPaddingSeconds = 0.045f;
        public float minimumTakeDurationSeconds = 0.08f;
        public float minimumVariantImprovement = 0.25f;
        public float minimumVariantSeparation = 0.16f;
        public VisemePhraseDistanceOptions distance = new VisemePhraseDistanceOptions();

        internal VisemePhraseCompileOptions Sanitized()
        {
            return new VisemePhraseCompileOptions
            {
                voiceThreshold = Mathf.Clamp(FiniteOr(voiceThreshold, 0.025f), 0f, 0.25f),
                trimPaddingSeconds = Mathf.Clamp(FiniteOr(trimPaddingSeconds, 0.045f), 0f, 0.25f),
                minimumTakeDurationSeconds = Mathf.Clamp(
                    FiniteOr(minimumTakeDurationSeconds, 0.08f), 0.04f, 2f),
                minimumVariantImprovement = Mathf.Clamp01(
                    FiniteOr(minimumVariantImprovement, 0.25f)),
                minimumVariantSeparation = Mathf.Clamp01(
                    FiniteOr(minimumVariantSeparation, 0.16f)),
                distance = (distance ?? new VisemePhraseDistanceOptions()).Sanitized()
            };
        }

        private static float FiniteOr(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }
    }

    public sealed class VisemePhraseCompileResult
    {
        public VisemePhraseCompiledModel model;
        public VisemePhraseModelDiagnostics diagnostics;
        public List<List<VisemePhraseToken>> processedPositiveTakes =
            new List<List<VisemePhraseToken>>();

        public bool success => model != null && diagnostics != null && diagnostics.valid;
    }

    public static class VisemePhraseValidation
    {
        public static List<VisemePhraseDiagnostic> Validate(
            VisemePhraseDefinition definition,
            VisemePhraseEnrollment enrollment,
            VisemePhraseCompileOptions options = null)
        {
            var messages = new List<VisemePhraseDiagnostic>();
            var safe = (options ?? new VisemePhraseCompileOptions()).Sanitized();
            if (definition == null)
            {
                Error(messages, "definition_missing", "The phrase definition is missing.");
                return messages;
            }
            if (string.IsNullOrWhiteSpace(definition.prompt))
                Error(messages, "prompt_missing", "Enter the phrase before recording enrollment takes.");
            if (enrollment == null)
            {
                Error(messages, "enrollment_missing", "The phrase has no enrollment data.");
                return messages;
            }
            if (enrollment.enrollmentSchemaVersion !=
                VisemePhraseEnrollment.CurrentEnrollmentSchemaVersion)
            {
                Error(
                    messages,
                    "enrollment_schema_unsupported",
                    "The enrollment was created by a different package data format. Open Record / Improve and record a current enrollment.");
            }

            var expectedId = string.IsNullOrWhiteSpace(definition.id)
                ? AdvancedVisemeParameterContract.StablePhraseId(definition.prompt)
                : AdvancedVisemeParameterContract.NormalizePhraseId(definition.id);
            var expectedPrompt = AdvancedVisemeParameterContract.PromptFingerprint(definition.prompt);
            if (!string.Equals(enrollment.phraseId, expectedId, StringComparison.Ordinal))
                Error(messages, "phrase_id_mismatch", "Enrollment belongs to a different phrase ID.");
            if (!string.Equals(enrollment.promptFingerprint, expectedPrompt, StringComparison.Ordinal))
                Error(messages, "prompt_changed", "The prompt changed after enrollment. Record four new takes for this wording.");

            var takes = enrollment.positiveTakes;
            if (takes == null || takes.Count != VisemePhraseEnrollmentProfile.RequiredPositiveTakeCount)
            {
                Error(
                    messages,
                    "positive_take_count",
                    "Exactly four positive enrollment takes are required.");
                return messages;
            }

            var takeIds = new HashSet<string>(StringComparer.Ordinal);
            var processedTakes = new List<VisemePhraseToken>[takes.Count];
            for (var takeIndex = 0; takeIndex < takes.Count; takeIndex++)
            {
                var trace = takes[takeIndex];
                var label = "Take " + (takeIndex + 1).ToString(CultureInfo.InvariantCulture);
                if (trace == null)
                {
                    Error(messages, "take_missing", label + " is missing.");
                    continue;
                }
                if (trace.traceSchemaVersion !=
                    VisemePhraseEnrollmentTrace.CurrentTraceSchemaVersion)
                {
                    Error(
                        messages,
                        "trace_schema_unsupported",
                        label + " was recorded with a different trace format. Retake it in Record / Improve.");
                }
                if (trace.sampleRate <= 0)
                    Error(messages, "sample_rate_invalid", label + " has an invalid sample rate.");
                if (trace.frames == null || trace.frames.Count < 2)
                {
                    Error(messages, "take_empty", label + " does not contain enough classifier frames.");
                    continue;
                }
                if (!StrictlyIncreasing(trace.frames))
                    Error(messages, "sample_clock_invalid", label + " has duplicate or non-increasing sample timestamps.");
                if (!string.IsNullOrWhiteSpace(trace.takeId) && !takeIds.Add(trace.takeId))
                    Warning(messages, "take_id_duplicate", label + " duplicates another take identifier.");

                var trimmed = VisemePhraseTraceMath.Trim(
                    trace,
                    safe.voiceThreshold,
                    safe.trimPaddingSeconds);
                var tokens = VisemePhraseTraceMath.RemoveTransientRuns(
                    VisemePhraseTraceMath.RunLengthEncode(trimmed));
                processedTakes[takeIndex] = tokens;
                var informativeRuns = tokens.Count(token => token.viseme != 0);
                if (informativeRuns < VisemePhraseCompileOptions.MinimumInformativeRuns)
                {
                    Error(
                        messages,
                        "take_too_few_runs",
                        label + " has fewer than three informative viseme runs. Speak the whole phrase clearly and record it again.");
                }
                else if (informativeRuns < VisemePhraseCompileOptions.RecommendedInformativeRuns)
                {
                    Warning(
                        messages,
                        "take_low_distinctiveness",
                        label + " has fewer than six viseme runs and may match unrelated speech.");
                }
                var duration = tokens.Sum(token => token.DurationSeconds);
                if (duration < safe.minimumTakeDurationSeconds)
                    Error(messages, "take_too_short", label + " is too short to be a reliable phrase.");
            }
            if (enrollment.negativeTraces != null)
            {
                for (var negativeIndex = 0;
                     negativeIndex < enrollment.negativeTraces.Count;
                     negativeIndex++)
                {
                    var trace = enrollment.negativeTraces[negativeIndex];
                    if (trace != null &&
                        trace.traceSchemaVersion ==
                        VisemePhraseEnrollmentTrace.CurrentTraceSchemaVersion)
                        continue;
                    Error(
                        messages,
                        "negative_trace_schema_unsupported",
                        "The optional ordinary-speech sample uses a different trace format. Clear it or record it again in Record / Improve.");
                }
            }
            if (!messages.Any(message =>
                    message.severity == VisemePhraseDiagnosticSeverity.Error) &&
                processedTakes.All(tokens => tokens != null))
            {
                if (definition.mode == VisemePhraseContextMode.NaturalSpeech)
                {
                    // Validate the same boundary-normalized language that will
                    // be baked. A three-run capture must not quietly become an
                    // unsafe two-state matcher after neighboring speech is
                    // removed.
                    VisemePhraseModelCompiler.NormalizeNaturalSpeechBoundaries(
                        processedTakes,
                        safe.distance,
                        new List<VisemePhraseDiagnostic>());
                    if (processedTakes.Any(tokens => tokens.Count(token =>
                            token.viseme != 0) <
                        VisemePhraseCompileOptions.MinimumInformativeRuns))
                    {
                        Error(
                            messages,
                            "phrase_too_few_repeatable_runs",
                            "After removing neighboring speech, this phrase has fewer than three repeatable mouth shapes. It is too visually short to recognize safely.");
                    }
                }
            }
            if (!messages.Any(message =>
                    message.severity == VisemePhraseDiagnosticSeverity.Error) &&
                processedTakes.All(tokens => tokens != null))
            {
                ValidateCrossTakeConsistency(
                    processedTakes,
                    Mathf.Clamp01(definition.strictness),
                    safe.distance,
                    messages);
            }
            return messages;
        }

        private static void ValidateCrossTakeConsistency(
            IReadOnlyList<List<VisemePhraseToken>> takes,
            float strictness,
            VisemePhraseDistanceOptions options,
            ICollection<VisemePhraseDiagnostic> messages)
        {
            var nearest = new float[takes.Count];
            for (var i = 0; i < nearest.Length; i++) nearest[i] = 1f;
            for (var first = 0; first < takes.Count; first++)
            {
                for (var second = first + 1; second < takes.Count; second++)
                {
                    var distance = VisemePhraseTraceMath.DtwDistance(
                        takes[first], takes[second], options);
                    nearest[first] = Mathf.Min(nearest[first], distance);
                    nearest[second] = Mathf.Min(nearest[second], distance);
                }
            }

            var medianNearest = VisemePhraseTraceMath.Median(nearest);
            var absoluteLimit = Mathf.Lerp(0.32f, 0.2f, strictness);
            var relativeLimit = medianNearest * 2.75f + 0.18f;
            var variableCount = nearest.Count(distance =>
                distance > absoluteLimit ||
                medianNearest < 0.12f && distance > relativeLimit);
            if (variableCount <= 0) return;

            // These four recordings are declared positive examples. A fixed
            // pairwise cutoff must not reject ordinary hard-Viseme variation
            // before the multi-take compiler has had a chance to retain it as
            // aliases, an optional state, or a second pronunciation branch.
            Warning(
                messages,
                "enrollment_variability",
                "The recordings contain natural mouth-shape variation. The matcher will retain the repeatable paths instead of asking for identical takes.");
        }

        private static bool StrictlyIncreasing(IReadOnlyList<VisemePhraseTraceFrame> frames)
        {
            var previous = -1L;
            for (var i = 0; i < frames.Count; i++)
            {
                if (frames[i] == null || frames[i].sampleClock <= previous) return false;
                previous = frames[i].sampleClock;
            }
            return true;
        }

        internal static void Error(
            ICollection<VisemePhraseDiagnostic> messages,
            string code,
            string message)
        {
            messages.Add(new VisemePhraseDiagnostic(
                VisemePhraseDiagnosticSeverity.Error, code, message));
        }

        internal static void Warning(
            ICollection<VisemePhraseDiagnostic> messages,
            string code,
            string message)
        {
            messages.Add(new VisemePhraseDiagnostic(
                VisemePhraseDiagnosticSeverity.Warning, code, message));
        }
    }

    /// <summary>
    /// Converts exactly four raw takes into at most two compact, duration-aware
    /// left-to-right templates. No Unity assets or scene state are touched here.
    /// </summary>
    public static class VisemePhraseModelCompiler
    {
        // Speech tempo is multiplicative: twice as fast (0.5x duration) is the
        // perceptual counterpart of twice as slow (2x duration). Learn timing
        // in log space so both sides of the envelope stay symmetric.
        private const float GuaranteedTempoFactor = 2f;
        private const float MaximumLearnedTempoFactor = 2.2f;
        private const float MinimumTimingSeconds = 0.0001f;
        // Oculus LipSync commonly emits one winner per 1024-sample analysis
        // block (~21 ms at 48 kHz). Context-inferred paths therefore use one
        // block as a debounce floor instead of pretending a phone's duration
        // is fixed by the one take in which that exact context appeared.
        private const float ContextRunDebounceSeconds = 0.020f;
        private const float RuntimeCostEpsilon = 0.00001f;
        private const float AcceptanceGuard = 0.005f;

        public static VisemePhraseCompileResult Compile(
            VisemePhraseDefinition definition,
            VisemePhraseEnrollment enrollment,
            VisemePhraseCompileOptions options = null)
        {
            var safe = (options ?? new VisemePhraseCompileOptions()).Sanitized();
            var result = new VisemePhraseCompileResult();
            var diagnostics = new VisemePhraseModelDiagnostics
            {
                positiveTakeCount = enrollment?.positiveTakes?.Count ?? 0,
                negativeTraceCount = enrollment?.negativeTraces?.Count ?? 0
            };
            diagnostics.messages.AddRange(VisemePhraseValidation.Validate(
                definition,
                enrollment,
                safe));
            result.diagnostics = diagnostics;
            if (HasErrors(diagnostics.messages))
            {
                diagnostics.valid = false;
                return result;
            }

            var phraseId = string.IsNullOrWhiteSpace(definition.id)
                ? AdvancedVisemeParameterContract.StablePhraseId(definition.prompt)
                : AdvancedVisemeParameterContract.NormalizePhraseId(definition.id);
            var promptFingerprint = AdvancedVisemeParameterContract.PromptFingerprint(definition.prompt);
            var strictness = Mathf.Clamp01(definition.strictness);
            for (var i = 0; i < enrollment.positiveTakes.Count; i++)
            {
                var trimmed = VisemePhraseTraceMath.Trim(
                    enrollment.positiveTakes[i],
                    safe.voiceThreshold,
                    safe.trimPaddingSeconds);
                result.processedPositiveTakes.Add(VisemePhraseTraceMath.RemoveTransientRuns(
                    VisemePhraseTraceMath.RunLengthEncode(trimmed)));
            }

            if (definition.mode == VisemePhraseContextMode.NaturalSpeech)
                NormalizeNaturalSpeechBoundaries(
                    result.processedPositiveTakes,
                    safe.distance,
                    diagnostics.messages);

            var pairwise = PairwiseDistances(result.processedPositiveTakes, safe.distance);
            diagnostics.meanPairwiseCost = MeanUpperTriangle(pairwise);
            diagnostics.positiveConsistency = Mathf.Clamp01(1f - diagnostics.meanPairwiseCost);
            var clusters = SelectRuntimeCompatibleClusters(
                pairwise,
                strictness,
                safe,
                result.processedPositiveTakes,
                enrollment.positiveTakes);

            var model = new VisemePhraseCompiledModel
            {
                modelSchemaVersion = VisemePhraseCompiledModel.CurrentModelSchemaVersion,
                phraseId = phraseId,
                promptFingerprint = promptFingerprint,
                contextMode = definition.mode,
                strictness = strictness,
                requiresLeadingPause = definition.mode == VisemePhraseContextMode.PausedCommand,
                requiresTrailingPause = definition.mode == VisemePhraseContextMode.PausedCommand,
                minimumNegativeMargin = Mathf.Lerp(0.08f, 0.16f, strictness),
                diagnostics = diagnostics
            };
            for (var clusterIndex = 0; clusterIndex < clusters.Count; clusterIndex++)
            {
                model.variants.Add(BuildVariant(
                    clusterIndex,
                    clusters[clusterIndex],
                    result.processedPositiveTakes,
                    enrollment.positiveTakes,
                    pairwise,
                    strictness,
                    safe,
                    diagnostics.messages));
            }

            // A learned centroid can be useful for supported aliases and one
            // repeatable deletion, but it must not be allowed to invalidate a
            // pronunciation the wearer actually enrolled. Discard learned
            // paths that cannot seed even one approved take, then cover every
            // remaining positive with a correlated whole-sequence path. This
            // is the bounded finite-state equivalent of multi-template QbE:
            // it retains natural variation without creating an unobserved
            // Cartesian product of singleton aliases and skips.
            VisemePhraseRuntimeLanguage.BuildPositiveTimingRectangles(
                model,
                result.processedPositiveTakes,
                diagnostics.messages);
            RemoveUnseededVariants(model);
            var retainedEnrollmentPaths = RetainUnrepresentedEnrollmentPaths(
                model,
                result.processedPositiveTakes,
                enrollment.positiveTakes,
                pairwise,
                strictness,
                diagnostics.messages);
            if (retainedEnrollmentPaths > 0)
            {
                diagnostics.messages.Add(new VisemePhraseDiagnostic(
                    VisemePhraseDiagnosticSeverity.Info,
                    "enrollment_paths_retained",
                    retainedEnrollmentPaths.ToString(CultureInfo.InvariantCulture) +
                    " additional enrolled mouth-shape path" +
                    (retainedEnrollmentPaths == 1 ? " was" : "s were") +
                    " retained instead of forcing natural pronunciation variation into one averaged template."));
            }
            ReindexVariants(model);
            var negativeTokens = PrepareNegativeTokens(enrollment.negativeTraces, safe);
            PruneOptionalFeatures(
                model,
                result.processedPositiveTakes,
                negativeTokens,
                diagnostics.messages);
            // Feature pruning can change which variant/skip path best represents
            // a take. Re-seed the exact runtime rectangles instead of retaining
            // stale paths from the more permissive pre-pruning language.
            VisemePhraseRuntimeLanguage.BuildPositiveTimingRectangles(
                model,
                result.processedPositiveTakes,
                diagnostics.messages);
            RemoveUnseededVariants(model);
            ReindexVariants(model);
            var retainedContextPaths = RetainContextConsistentPaths(
                model,
                result.processedPositiveTakes,
                enrollment.positiveTakes,
                strictness,
                safe.distance);
            if (retainedContextPaths > 0)
            {
                diagnostics.messages.Add(new VisemePhraseDiagnostic(
                    VisemePhraseDiagnosticSeverity.Info,
                    "context_paths_retained",
                    retainedContextPaths.ToString(CultureInfo.InvariantCulture) +
                    " bounded pronunciation bridge" +
                    (retainedContextPaths == 1 ? " was" : "s were") +
                    " retained from locally observed contexts and repeatable " +
                    "classifier edges so natural prefix and suffix variation can " +
                    "recombine safely."));
            }
            ReindexVariants(model);
            var retainedConfusionPaths = RetainBoundedConfusionPaths(
                model,
                result.processedPositiveTakes,
                negativeTokens,
                pairwise,
                strictness,
                diagnostics.messages);
            ReindexVariants(model);
            var calibrationMessages = new List<VisemePhraseDiagnostic>();
            var prunedUnreachableConfusions = 0;
            // Negative calibration can be stricter than the generic threshold
            // used while proposing optional confusion arcs. Find a small fixed
            // point: calibrate, remove only optional arcs whose base penalty can
            // no longer fit the final runtime budget, then calibrate again. A
            // speculative generalization must never invalidate enrolled speech.
            for (var pass = 0;
                 pass <= VisemePhraseCompileOptions.MaximumConfusionPaths;
                 pass++)
            {
                calibrationMessages.Clear();
                model.negativeCalibration = Calibrate(
                    model,
                    result.processedPositiveTakes,
                    negativeTokens,
                    strictness,
                    calibrationMessages);
                model.acceptanceCost =
                    model.negativeCalibration.recommendedAcceptanceCost;
                var removed = RemoveUnreachableConfusionPaths(model);
                if (removed == 0) break;
                prunedUnreachableConfusions += removed;
                ReindexVariants(model);
            }
            foreach (var message in calibrationMessages)
                diagnostics.messages.Add(message);
            var finalConfusionPaths = model.variants.Count(variant =>
                variant != null && variant.inferredConfusionPath);
            if (finalConfusionPaths > 0)
            {
                diagnostics.messages.Add(new VisemePhraseDiagnostic(
                    VisemePhraseDiagnosticSeverity.Info,
                    "confusion_paths_retained",
                    finalConfusionPaths.ToString(CultureInfo.InvariantCulture) +
                    " weighted one-confusion path" +
                    (finalConfusionPaths == 1 ? " was" : "s were") +
                    " retained. Each path changes one visually confusable Oculus " +
                    "winner while preserving the enrolled order and correlated timing."));
            }
            if (retainedConfusionPaths > finalConfusionPaths ||
                prunedUnreachableConfusions > 0)
            {
                diagnostics.messages.Add(new VisemePhraseDiagnostic(
                    VisemePhraseDiagnosticSeverity.Info,
                    "confusion_path_over_budget",
                    Math.Max(prunedUnreachableConfusions,
                            retainedConfusionPaths - finalConfusionPaths)
                        .ToString(CultureInfo.InvariantCulture) +
                    " optional confusion path" +
                    (Math.Max(prunedUnreachableConfusions,
                         retainedConfusionPaths - finalConfusionPaths) == 1
                        ? " was"
                        : "s were") +
                    " omitted because personalized background-speech calibration " +
                    "made its weighted error too expensive."));
            }
            diagnostics.negativeMargin = model.negativeCalibration.separation;
            ValidateRuntimeReplay(
                model,
                result.processedPositiveTakes,
                negativeTokens,
                diagnostics.messages);
            diagnostics.variantCount = model.variants.Count;
            diagnostics.stateCount = model.variants.Sum(variant => variant.states.Count);
            diagnostics.distinctiveness = ComputeDistinctiveness(model.variants);
            if (model.variants.Any(variant => variant.states.Count <
                                              VisemePhraseCompileOptions.RecommendedInformativeRuns))
            {
                VisemePhraseValidation.Warning(
                    diagnostics.messages,
                    "model_low_distinctiveness",
                    "The compiled phrase has fewer than six states and is prone to visual-speech collisions.");
            }
            if (model.variants.Count > 1)
            {
                diagnostics.messages.Add(new VisemePhraseDiagnostic(
                    VisemePhraseDiagnosticSeverity.Info,
                    "pronunciation_variants",
                    model.variants.Count.ToString(CultureInfo.InvariantCulture) +
                    " enrolled pronunciation paths were retained instead of averaging them into one rigid trace."));
            }
            diagnostics.valid = !HasErrors(diagnostics.messages);
            model.contentFingerprint = Fingerprint(model);
            diagnostics.modelFingerprint = model.contentFingerprint;
            result.model = model;
            return result;
        }

        public static float Score(
            VisemePhraseCompiledModel model,
            IReadOnlyList<VisemePhraseToken> tokens,
            bool subsequence) =>
            VisemePhraseRuntimeLanguage.Score(model, tokens, subsequence);

        private static VisemePhraseNegativeCalibration Calibrate(
            VisemePhraseCompiledModel model,
            IReadOnlyList<List<VisemePhraseToken>> positiveTakes,
            IReadOnlyList<List<VisemePhraseToken>> negativeTakes,
            float strictness,
            ICollection<VisemePhraseDiagnostic> messages)
        {
            var stats = EvaluateCalibration(model, positiveTakes, negativeTakes);
            var calibration = new VisemePhraseNegativeCalibration
            {
                worstPositiveCost = stats.worstPositive,
                bestNegativeCost = stats.bestNegative,
                calibrated = stats.negativeCount > 0,
                negativeTraceCount = stats.negativeCount,
                separation = stats.negativeCount > 0 ? stats.separation : 0f
            };

            var fallback = Mathf.Lerp(0.46f, 0.2f, strictness);
            if (stats.negativeCount == 0)
            {
                calibration.recommendedAcceptanceCost = Mathf.Clamp(
                    Mathf.Max(fallback, stats.worstPositive + AcceptanceGuard),
                    0.01f,
                    0.99f);
                messages.Add(new VisemePhraseDiagnostic(
                    VisemePhraseDiagnosticSeverity.Info,
                    "negative_calibration_missing",
                    "No optional background speech was supplied; a conservative generic threshold is used."));
                return calibration;
            }

            var conservative = stats.worstPositive + AcceptanceGuard;
            var permissive = stats.bestNegative - AcceptanceGuard;
            if (calibration.separation + RuntimeCostEpsilon >=
                model.minimumNegativeMargin)
            {
                calibration.recommendedAcceptanceCost = Mathf.Clamp(
                    Mathf.Lerp(permissive, conservative, strictness),
                    Mathf.Max(0.001f, conservative),
                    Mathf.Min(0.999f, permissive));
            }
            else
            {
                calibration.recommendedAcceptanceCost = Mathf.Clamp(
                    Mathf.Max(conservative, fallback),
                    0.01f,
                    0.99f);
                VisemePhraseValidation.Error(
                    messages,
                    "negative_runtime_margin",
                    "The recorded ordinary-speech sample is too close to the four enrolled " +
                    "takes for the baked Animator to keep a safe acceptance margin. Retake " +
                    "the ordinary-speech sample without saying the phrase, use a longer " +
                    "phrase, or choose Paused Command mode.");
            }
            return calibration;
        }

        private static List<List<VisemePhraseToken>> PrepareNegativeTokens(
            IReadOnlyList<VisemePhraseEnrollmentTrace> negativeTraces,
            VisemePhraseCompileOptions options)
        {
            var result = new List<List<VisemePhraseToken>>();
            if (negativeTraces == null) return result;
            for (var i = 0; i < negativeTraces.Count; i++)
            {
                var trimmed = VisemePhraseTraceMath.Trim(
                    negativeTraces[i], options.voiceThreshold, options.trimPaddingSeconds);
                var tokens = VisemePhraseTraceMath.RemoveTransientRuns(
                    VisemePhraseTraceMath.RunLengthEncode(trimmed));
                if (tokens.Count >= 2) result.Add(tokens);
            }
            return result;
        }

        private static void ValidateRuntimeReplay(
            VisemePhraseCompiledModel model,
            IReadOnlyList<List<VisemePhraseToken>> positiveTakes,
            IReadOnlyList<List<VisemePhraseToken>> negativeTakes,
            ICollection<VisemePhraseDiagnostic> messages)
        {
            if (positiveTakes != null)
            {
                for (var takeIndex = 0; takeIndex < positiveTakes.Count; takeIndex++)
                {
                    var take = positiveTakes[takeIndex];
                    var score = VisemePhraseRuntimeLanguage.Score(model, take, false);
                    if (score >= 1f - RuntimeCostEpsilon)
                    {
                        var runCount = take?.Count ?? 0;
                        var detail = runCount >
                                     VisemePhraseCompileOptions.MaximumStatesPerVariant
                            ? "This exceeds the avatar's twelve-state limit for one " +
                              "pronunciation path. Choose a shorter trigger phrase or split " +
                              "the action into two phrases."
                            : "The enrolled path could not fit within the avatar-wide " +
                              "bounded language. Remove another phrase trigger or use a " +
                              "more visually distinct trigger phrase.";
                        VisemePhraseValidation.Error(
                            messages,
                            "take_runtime_nonreplay",
                            "Retake slot " + (takeIndex + 1).ToString(CultureInfo.InvariantCulture) +
                            " contains " + runCount.ToString(CultureInfo.InvariantCulture) +
                            " stable mouth-shape runs. " + detail);
                        continue;
                    }
                    if (score <= model.acceptanceCost + RuntimeCostEpsilon) continue;
                    VisemePhraseValidation.Error(
                        messages,
                        "take_runtime_threshold",
                        "Retake slot " + (takeIndex + 1).ToString(CultureInfo.InvariantCulture) +
                        ": its baked-language cost " + score.ToString("0.000", CultureInfo.InvariantCulture) +
                        " exceeds the calibrated acceptance threshold " +
                        model.acceptanceCost.ToString("0.000", CultureInfo.InvariantCulture) + ".");
                }
            }

            if (negativeTakes == null) return;
            for (var negativeIndex = 0; negativeIndex < negativeTakes.Count; negativeIndex++)
            {
                var score = VisemePhraseRuntimeLanguage.Score(
                    model, negativeTakes[negativeIndex], true);
                if (score > model.acceptanceCost + RuntimeCostEpsilon) continue;
                VisemePhraseValidation.Error(
                    messages,
                    "negative_runtime_match",
                    "Ordinary-speech sample " +
                    (negativeIndex + 1).ToString(CultureInfo.InvariantCulture) +
                    " contains a Viseme subsequence accepted by the baked Animator (cost " +
                    score.ToString("0.000", CultureInfo.InvariantCulture) + "). Retake the " +
                    "sample without saying the phrase, use a longer phrase, increase " +
                    "strictness, or choose Paused Command mode.");
            }
        }

        /// <summary>
        /// Multi-sample query-by-example matchers retain the variability of the
        /// enrolled samples instead of requiring a single mean template to
        /// reproduce every positive. Keep the compact learned variants first,
        /// then add a deterministic exact path only for a declared positive
        /// they cannot consume. The downstream global trie shares common
        /// prefixes and enforces the final Animator state budget.
        /// </summary>
        private static int RetainUnrepresentedEnrollmentPaths(
            VisemePhraseCompiledModel model,
            IReadOnlyList<List<VisemePhraseToken>> positiveTakes,
            IReadOnlyList<VisemePhraseEnrollmentTrace> rawTakes,
            float[,] pairwise,
            float strictness,
            ICollection<VisemePhraseDiagnostic> messages)
        {
            if (model?.variants == null || positiveTakes == null ||
                rawTakes == null) return 0;

            VisemePhraseRuntimeLanguage.BuildPositiveTimingRectangles(
                model,
                positiveTakes,
                messages);
            var uncovered = Enumerable.Range(0, positiveTakes.Count)
                .Where(index => VisemePhraseRuntimeLanguage.Score(
                    model,
                    positiveTakes[index],
                    false) >= 1f - RuntimeCostEpsilon)
                .GroupBy(index => VisemeSequenceKey(positiveTakes[index]))
                .OrderBy(group => group.Min())
                .ToArray();
            var initialCount = model.variants.Count;
            foreach (var group in uncovered)
            {
                if (model.variants.Count >=
                    VisemePhraseCompileOptions.MaximumEnrolledPaths)
                    break;
                var indices = group.OrderBy(index => index).ToArray();
                var tokens = positiveTakes[indices[0]];
                // An exact path must remain exact. Never sample a long trace
                // down to the state cap: doing so would save a model that the
                // runtime language provably cannot replay.
                if (tokens == null ||
                    tokens.Count > VisemePhraseCompileOptions.MaximumStatesPerVariant)
                    continue;
                model.variants.Add(BuildExactVariant(
                    model.variants.Count,
                    indices,
                    positiveTakes,
                    rawTakes,
                    pairwise,
                    strictness));
            }
            VisemePhraseRuntimeLanguage.BuildPositiveTimingRectangles(
                model,
                positiveTakes,
                messages);
            RemoveUnseededVariants(model);
            return Mathf.Max(0, model.variants.Count - initialCount);
        }

        private static VisemePhraseModelVariant BuildExactVariant(
            int variantIndex,
            IReadOnlyList<int> takeIndices,
            IReadOnlyList<List<VisemePhraseToken>> allTokens,
            IReadOnlyList<VisemePhraseEnrollmentTrace> rawTakes,
            float[,] pairwise,
            float strictness)
        {
            var referenceIndex = takeIndices
                .OrderBy(index => takeIndices.Sum(other => pairwise[index, other]))
                .ThenBy(index => index)
                .First();
            var reference = allTokens[referenceIndex];
            var variant = new VisemePhraseModelVariant
            {
                id = "v" + variantIndex.ToString(CultureInfo.InvariantCulture),
                sourceTakeIndex = referenceIndex,
                sourceTakeId = rawTakes[referenceIndex]?.takeId ?? string.Empty,
                cohesion = ClusterCohesion(takeIndices, pairwise)
            };
            var totalDurations = takeIndices.Select(index =>
                allTokens[index].Sum(token => token.DurationSeconds));
            RobustBounds(
                totalDurations,
                strictness,
                out variant.medianDurationSeconds,
                out variant.minimumDurationSeconds,
                out variant.maximumDurationSeconds);

            for (var stateIndex = 0; stateIndex < reference.Count; stateIndex++)
            {
                var observations = takeIndices
                    .Select(index => allTokens[index][stateIndex])
                    .ToArray();
                RobustBounds(
                    observations.Select(token => token.DurationSeconds),
                    strictness,
                    out var median,
                    out var minimum,
                    out var maximum);
                var emissions = new float[VisemePhraseTraceMath.VisemeCount];
                emissions[reference[stateIndex].viseme] = 1f;
                variant.states.Add(new VisemePhraseModelState
                {
                    index = stateIndex,
                    primaryViseme = reference[stateIndex].viseme,
                    aliasVisemes = Array.Empty<int>(),
                    aliasLikelihoods = Array.Empty<float>(),
                    emissionLikelihoods = emissions,
                    medianDurationSeconds = median,
                    minimumDurationSeconds = minimum,
                    maximumDurationSeconds = maximum,
                    meanVoice = Mathf.Clamp01(observations.Average(token => token.meanVoice)),
                    allowSkip = false,
                    skipPenalty = 0f
                });
            }
            return variant;
        }

        private static string VisemeSequenceKey(
            IReadOnlyList<VisemePhraseToken> tokens) =>
            tokens == null
                ? string.Empty
                : string.Join(",", tokens.Select(token =>
                    token.viseme.ToString(CultureInfo.InvariantCulture)));

        private static void RemoveUnseededVariants(VisemePhraseCompiledModel model)
        {
            if (model?.variants == null) return;
            model.variants.RemoveAll(variant =>
                variant == null ||
                variant.runtimeTimingRectangles == null ||
                variant.runtimeTimingRectangles.Count == 0);
        }

        private static void ReindexVariants(VisemePhraseCompiledModel model)
        {
            if (model?.variants == null) return;
            for (var index = 0; index < model.variants.Count; index++)
                model.variants[index].id =
                    "v" + index.ToString(CultureInfo.InvariantCulture);
        }

        private static int RemoveUnreachableConfusionPaths(
            VisemePhraseCompiledModel model)
        {
            if (model?.variants == null) return 0;
            var before = model.variants.Count;
            model.variants.RemoveAll(variant =>
                variant != null &&
                variant.inferredConfusionPath &&
                Mathf.Max(0f, variant.inferencePenalty) >
                Mathf.Clamp01(model.acceptanceCost) *
                Mathf.Max(1, variant.states?.Count ?? 0) +
                RuntimeCostEpsilon);
            return before - model.variants.Count;
        }

        /// <summary>
        /// Four enrollment takes are enough to retain complete personalized
        /// pronunciations, but not enough to observe every hard-winner error the
        /// Oculus classifier will make later. Compile at most two weighted,
        /// one-substitution paths from a small visual-confusion prior. Each path
        /// changes one interior winner, reuses one enrolled path's correlated
        /// timing, and is independently removable by negative calibration or the
        /// avatar-wide state fitter. This is a bounded WFST error arc, not a
        /// Cartesian fuzzy product.
        /// </summary>
        private static int RetainBoundedConfusionPaths(
            VisemePhraseCompiledModel model,
            IReadOnlyList<List<VisemePhraseToken>> positiveTakes,
            IReadOnlyList<List<VisemePhraseToken>> negativeTakes,
            float[,] pairwise,
            float strictness,
            ICollection<VisemePhraseDiagnostic> messages)
        {
            if (model?.variants == null || positiveTakes == null ||
                model.variants.Count >= VisemePhraseCompileOptions.MaximumRuntimePaths)
                return 0;

            // A stable repeated trace needs no generic correction. Require at
            // least three independently observed hard-winner paths before using
            // scarce runtime states on classifier generalization.
            var distinctPositivePaths = positiveTakes
                .Where(take => take != null && take.Count > 0)
                .Select(VisemeSequenceKey)
                .Distinct(StringComparer.Ordinal)
                .Count();
            if (distinctPositivePaths < 3) return 0;

            var sources = model.variants
                .Where(variant => variant?.states != null &&
                                  !variant.inferredContextPath &&
                                  !variant.inferredConfusionPath &&
                                  variant.states.Count >= 4 &&
                                  variant.states.All(state => state != null &&
                                      !state.allowSkip &&
                                      (state.aliasVisemes == null ||
                                       state.aliasVisemes.Length == 0)))
                .OrderBy(variant => MedoidDistance(
                    variant.sourceTakeIndex, positiveTakes.Count, pairwise))
                .ThenBy(variant => variant.states.Count)
                .ThenBy(variant => VisemeSequenceKey(
                    variant.states.Select(state => state.primaryViseme)),
                    StringComparer.Ordinal)
                .ToArray();
            if (sources.Length == 0) return 0;

            var existing = new HashSet<string>(model.variants
                .Where(variant => variant?.states != null)
                .Select(variant => VisemeSequenceKey(
                    variant.states.Select(state => state.primaryViseme))),
                StringComparer.Ordinal);
            var candidates = new List<ConfusionPathCandidate>();
            foreach (var source in sources)
            {
                var sourceSequence = VisemeSequenceKey(
                    source.states.Select(state => state.primaryViseme));
                for (var stateIndex = 1;
                     stateIndex + 1 < source.states.Count;
                     stateIndex++)
                {
                    var primary = source.states[stateIndex].primaryViseme;
                    foreach (var alternative in BoundedConfusions(primary))
                    {
                        if (alternative.viseme ==
                                source.states[stateIndex - 1].primaryViseme ||
                            alternative.viseme ==
                                source.states[stateIndex + 1].primaryViseme)
                            continue;
                        var sequence = source.states
                            .Select((state, index) => index == stateIndex
                                ? alternative.viseme
                                : state.primaryViseme)
                            .ToArray();
                        var key = VisemeSequenceKey(sequence);
                        if (existing.Contains(key)) continue;
                        var likelihood = Mathf.Clamp01(alternative.likelihood *
                            Mathf.Lerp(1f, 0.82f, Mathf.Clamp01(strictness)));
                        var penalty = 1f - likelihood;
                        var fallbackBudget = Mathf.Lerp(0.46f, 0.2f, strictness);
                        if (penalty / Mathf.Max(1, sequence.Length) >
                            fallbackBudget - AcceptanceGuard)
                            continue;
                        candidates.Add(new ConfusionPathCandidate
                        {
                            source = source,
                            sourceSequence = sourceSequence,
                            sequence = sequence,
                            sequenceKey = key,
                            stateIndex = stateIndex,
                            likelihood = likelihood,
                            penalty = penalty,
                            weaknessRank = ConfusionWeaknessRank(primary)
                        });
                    }
                }
            }

            var baseline = EvaluateCalibration(model, positiveTakes, negativeTakes);
            var retained = 0;
            var prunedByBackground = 0;
            foreach (var candidate in candidates
                         .OrderBy(item => item.weaknessRank)
                         .ThenByDescending(item => item.likelihood)
                         .ThenBy(item => item.source.states.Count)
                         .ThenBy(item => item.stateIndex)
                         .ThenBy(item => item.sequenceKey, StringComparer.Ordinal))
            {
                if (retained >= VisemePhraseCompileOptions.MaximumConfusionPaths ||
                    model.variants.Count >=
                    VisemePhraseCompileOptions.MaximumRuntimePaths)
                    break;
                if (!existing.Add(candidate.sequenceKey)) continue;

                var inferred = BuildConfusionVariant(
                    model.variants.Count, candidate);
                model.variants.Add(inferred);
                VisemePhraseRuntimeLanguage.BuildPositiveTimingRectangles(
                    model, positiveTakes, messages);
                if (inferred.runtimeTimingRectangles == null ||
                    inferred.runtimeTimingRectangles.Count == 0)
                {
                    model.variants.Remove(inferred);
                    existing.Remove(candidate.sequenceKey);
                    continue;
                }

                var withCandidate = EvaluateCalibration(
                    model, positiveTakes, negativeTakes);
                var introducedUnsafeNegative = withCandidate.negativeCount > 0 &&
                    withCandidate.bestNegative + RuntimeCostEpsilon <
                    baseline.bestNegative &&
                    withCandidate.separation + RuntimeCostEpsilon <
                    model.minimumNegativeMargin;
                if (introducedUnsafeNegative)
                {
                    model.variants.Remove(inferred);
                    existing.Remove(candidate.sequenceKey);
                    VisemePhraseRuntimeLanguage.BuildPositiveTimingRectangles(
                        model, positiveTakes, messages);
                    prunedByBackground++;
                    continue;
                }

                baseline = withCandidate;
                retained++;
            }

            if (prunedByBackground > 0)
                messages.Add(new VisemePhraseDiagnostic(
                    VisemePhraseDiagnosticSeverity.Info,
                    "confusion_path_pruned",
                    prunedByBackground.ToString(CultureInfo.InvariantCulture) +
                    " generic one-confusion path" +
                    (prunedByBackground == 1 ? " was" : "s were") +
                    " omitted because recorded ordinary speech used the same mouth-shape sequence."));
            return retained;
        }

        private static float MedoidDistance(
            int sourceTakeIndex,
            int takeCount,
            float[,] pairwise)
        {
            if (pairwise == null || sourceTakeIndex < 0 ||
                sourceTakeIndex >= takeCount ||
                pairwise.GetLength(0) <= sourceTakeIndex ||
                pairwise.GetLength(1) < takeCount)
                return float.MaxValue;
            var total = 0f;
            for (var index = 0; index < takeCount; index++)
                total += pairwise[sourceTakeIndex, index];
            return total;
        }

        private static VisemePhraseModelVariant BuildConfusionVariant(
            int variantIndex,
            ConfusionPathCandidate candidate)
        {
            var source = candidate.source;
            var variant = new VisemePhraseModelVariant
            {
                id = "v" + variantIndex.ToString(CultureInfo.InvariantCulture),
                sourceTakeId = source.sourceTakeId,
                sourceTakeIndex = source.sourceTakeIndex,
                inferredContextPath = true,
                inferredConfusionPath = true,
                confusionSourceSequence = candidate.sourceSequence,
                confusionSourceVariantId = source.id,
                inferencePenalty = candidate.penalty,
                cohesion = Mathf.Clamp01(source.cohesion * candidate.likelihood),
                medianDurationSeconds = source.medianDurationSeconds,
                minimumDurationSeconds = source.minimumDurationSeconds,
                maximumDurationSeconds = source.maximumDurationSeconds
            };
            for (var index = 0; index < source.states.Count; index++)
            {
                var original = source.states[index];
                var viseme = candidate.sequence[index];
                var emissions = new float[VisemePhraseTraceMath.VisemeCount];
                emissions[viseme] = 1f;
                variant.states.Add(new VisemePhraseModelState
                {
                    index = index,
                    primaryViseme = viseme,
                    aliasVisemes = Array.Empty<int>(),
                    aliasLikelihoods = Array.Empty<float>(),
                    emissionLikelihoods = emissions,
                    medianDurationSeconds = original.medianDurationSeconds,
                    minimumDurationSeconds = original.minimumDurationSeconds,
                    maximumDurationSeconds = original.maximumDurationSeconds,
                    meanVoice = original.meanVoice,
                    allowSkip = false,
                    skipPenalty = 0f
                });
            }
            return variant;
        }

        private static int ConfusionWeaknessRank(int viseme)
        {
            if (viseme >= 10) return 0;       // vowel arg-max boundaries
            if (viseme == 6 || viseme == 7) return 1; // CH / SS frication
            return 2;                        // DD / nn tongue-place ambiguity
        }

        private static IEnumerable<ConfusionAlternative> BoundedConfusions(int viseme)
        {
            // Likelihoods are deliberately conservative priors. They rank
            // visually neighbouring Oculus classes; creator takes and recorded
            // background speech remain authoritative at compile time.
            switch (viseme)
            {
                case 4: yield return new ConfusionAlternative(8, 0.52f); break;
                case 6: yield return new ConfusionAlternative(7, 0.65f); break;
                case 7: yield return new ConfusionAlternative(6, 0.65f); break;
                case 8: yield return new ConfusionAlternative(4, 0.52f); break;
                case 10: yield return new ConfusionAlternative(11, 0.58f); break;
                case 11:
                    yield return new ConfusionAlternative(12, 0.64f);
                    yield return new ConfusionAlternative(10, 0.58f);
                    break;
                case 12: yield return new ConfusionAlternative(11, 0.64f); break;
                case 13: yield return new ConfusionAlternative(14, 0.65f); break;
                case 14: yield return new ConfusionAlternative(13, 0.65f); break;
            }
        }

        /// <summary>
        /// A set of complete enrollment templates is still too brittle when a
        /// hard arg-max classifier chooses a prefix seen in one take and a
        /// suffix seen in another. Build the bounded order-two path closure of
        /// the enrollment: a generated path must use an observed start pair,
        /// an observed end pair, and observed three-viseme contexts. One ABA
        /// context may back off when both directed edges were independently
        /// repeated in at least two takes; this models an arg-max classifier
        /// bounce without admitting a general unseen transition. This is a
        /// compact partial-order pronunciation lattice, not an arbitrary
        /// per-position alias product. Negative calibration and the downstream
        /// avatar-wide state cap remain authoritative.
        /// </summary>
        private static int RetainContextConsistentPaths(
            VisemePhraseCompiledModel model,
            IReadOnlyList<List<VisemePhraseToken>> positiveTakes,
            IReadOnlyList<VisemePhraseEnrollmentTrace> rawTakes,
            float strictness,
            VisemePhraseDistanceOptions distanceOptions)
        {
            if (model?.variants == null || positiveTakes == null ||
                positiveTakes.Count == 0 || rawTakes == null ||
                model.variants.Count >= VisemePhraseCompileOptions.MaximumProfilePaths)
                return 0;
            var usable = positiveTakes
                .Where(take => take != null && take.Count >= 3)
                .ToArray();
            if (usable.Length < 2) return 0;

            var minimumLength = usable.Min(take => take.Count);
            var maximumLength = Mathf.Min(
                VisemePhraseCompileOptions.MaximumStatesPerVariant,
                usable.Max(take => take.Count));
            var starts = new HashSet<int>();
            var ends = new HashSet<int>();
            var transitions = new Dictionary<int, HashSet<int>>();
            var edgeSupport = new Dictionary<int, int>();
            foreach (var take in usable)
            {
                starts.Add(ContextPairKey(take[0].viseme, take[1].viseme));
                ends.Add(ContextPairKey(
                    take[take.Count - 2].viseme,
                    take[take.Count - 1].viseme));
                for (var index = 0; index + 2 < take.Count; index++)
                {
                    var key = ContextPairKey(
                        take[index].viseme,
                        take[index + 1].viseme);
                    if (!transitions.TryGetValue(key, out var next))
                    {
                        next = new HashSet<int>();
                        transitions[key] = next;
                    }
                    next.Add(take[index + 2].viseme);
                }
                foreach (var edge in Enumerable.Range(0, take.Count - 1)
                             .Select(index => ContextPairKey(
                                 take[index].viseme, take[index + 1].viseme))
                             .Distinct())
                    edgeSupport[edge] = edgeSupport.TryGetValue(edge, out var support)
                        ? support + 1
                        : 1;
            }

            const int maximumEnumeratedPaths = 1024;
            var sequences = new Dictionary<string, EnumeratedContextPath>(
                StringComparer.Ordinal);
            foreach (var start in starts.OrderBy(value => value))
            {
                var path = new List<int>
                {
                    start / VisemePhraseTraceMath.VisemeCount,
                    start % VisemePhraseTraceMath.VisemeCount
                };
                EnumerateContextPaths(
                    path,
                    minimumLength,
                    maximumLength,
                    ends,
                    transitions,
                    edgeSupport,
                    0,
                    sequences,
                    maximumEnumeratedPaths);
                if (sequences.Count >= maximumEnumeratedPaths) break;
            }

            var existingKeys = new HashSet<string>(
                model.variants
                    .Where(variant => variant?.states != null)
                    .Select(variant => VisemeSequenceKey(variant.states.Select(state =>
                        new VisemePhraseToken { viseme = state.primaryViseme }).ToArray())),
                StringComparer.Ordinal);
            var candidates = new List<ContextPathCandidate>();
            foreach (var pair in sequences)
            {
                if (existingKeys.Contains(pair.Key)) continue;
                var seed = BuildContextSeed(pair.Value.sequence, usable);
                if (seed.Count != pair.Value.sequence.Length ||
                    VisemePhraseRuntimeLanguage.Score(model, seed, false) <
                    1f - RuntimeCostEpsilon)
                    continue;
                var closest = Enumerable.Range(0, positiveTakes.Count)
                    .Where(index => positiveTakes[index] != null &&
                                    positiveTakes[index].Count > 0)
                    .Select(index => new
                    {
                        index,
                        distance = VisemePhraseTraceMath.DtwDistance(
                            seed, positiveTakes[index])
                    })
                    .OrderBy(item => item.distance)
                    .ThenBy(item => item.index)
                    .First();
                candidates.Add(new ContextPathCandidate
                {
                    key = pair.Key,
                    sequence = pair.Value.sequence,
                    seed = seed,
                    closestTakeIndex = closest.index,
                    distance = closest.distance,
                    boundaryOverlap = ContextBoundaryOverlap(
                        pair.Value.sequence, usable),
                    backoffCount = pair.Value.backoffCount
                });
            }

            // A strict n-gram closure cannot join two aligned pronunciations
            // when the shared anchor has different neighbours in each take.
            // This is common for a short classifier run which appears in only
            // some repetitions: A-x-B and C-x-D provide no shared trigram even
            // though the creator demonstrated both sides of x. Build the
            // standard profile-language equivalent of one path switch: retain
            // an enrolled prefix through an exact DTW-aligned anchor, then an
            // enrolled suffix after that anchor. Every generated edge is still
            // observed in one of the two source takes, and the single-switch
            // restriction prevents a Cartesian product of per-position aliases.
            foreach (var splice in BuildSingleSpliceCandidates(
                         model,
                         usable,
                         existingKeys,
                         distanceOptions))
            {
                var previous = candidates.FirstOrDefault(item =>
                    string.Equals(item.key, splice.key, StringComparison.Ordinal));
                if (previous == null)
                {
                    candidates.Add(splice);
                    continue;
                }

                previous.spliceSupport = Math.Max(
                    previous.spliceSupport,
                    splice.spliceSupport);
                previous.boundaryEvidenceSeconds = Math.Max(
                    previous.boundaryEvidenceSeconds,
                    splice.boundaryEvidenceSeconds);
                if (splice.distance + RuntimeCostEpsilon < previous.distance)
                {
                    previous.seed = splice.seed;
                    previous.closestTakeIndex = splice.closestTakeIndex;
                    previous.distance = splice.distance;
                }
            }

            var added = 0;
            var ranked = candidates
                .OrderByDescending(item => item.spliceSupport)
                // Prefer a splice whose boundary observations are held long
                // enough to be visible to the render-frame Animator. This is
                // both more robust and more useful than spending scarce states
                // on a path beginning with a one-block classifier flicker.
                .ThenByDescending(item => item.boundaryEvidenceSeconds)
                .ThenBy(item => ContextWeakBoundaryCount(item.sequence))
                .ThenBy(item => item.spliceSupport > 0
                    ? item.sequence.Length
                    : int.MaxValue)
                .ThenBy(item => item.distance + item.backoffCount * 0.08f)
                .ThenBy(item => item.sequence.Length)
                .ThenBy(item => item.key, StringComparer.Ordinal)
                .ToArray();
            // A strict order-two closure cannot represent a classifier bounce
            // such as O-kk-O when each directed edge is repeatable but the
            // complete trigram did not occur in four takes. Reserve the first
            // derived slot for the best single backed-off ABA path; remaining
            // strict or backed-off paths retain deterministic score order.
            var bestBackoff = candidates
                .Where(item => item.backoffCount > 0)
                // Prefer a short classifier-bounce recovery that genuinely
                // bridges boundaries from different enrollment takes. A path
                // whose start and end already coexist in one take is a lower
                // value repetition of an exact template.
                .OrderBy(item => item.boundaryOverlap)
                // Consonantal closures/frication are stronger phrase anchors
                // than vowel-only boundaries, which are heavily confusable in
                // visual speech. Prefer recovery paths that retain them.
                .ThenBy(item => ContextWeakBoundaryCount(item.sequence))
                // A single ABA island models a plausible classifier bounce;
                // extended ABAB chatter is less informative and should not
                // consume the one reserved recovery slot.
                .ThenBy(item => ContextAlternationCount(item.sequence))
                .ThenBy(item => item.sequence.Length)
                .ThenBy(item => ContextPairKey(item.sequence[0], item.sequence[1]))
                .ThenBy(item => ContextPairKey(
                    item.sequence[item.sequence.Length - 2],
                    item.sequence[item.sequence.Length - 1]))
                .ThenBy(item => item.distance)
                .ThenBy(item => item.key, StringComparer.Ordinal)
                .FirstOrDefault();
            var prioritized = bestBackoff == null
                ? ranked
                : new[] { bestBackoff }.Concat(
                    ranked.Where(item => !ReferenceEquals(item, bestBackoff))).ToArray();
            foreach (var candidate in prioritized)
            {
                if (model.variants.Count >=
                    VisemePhraseCompileOptions.MaximumProfilePaths) break;
                var variant = BuildContextVariant(
                    model.variants.Count,
                    candidate,
                    usable,
                    rawTakes,
                    strictness);
                if (variant == null) continue;
                model.variants.Add(variant);
                existingKeys.Add(candidate.key);
                added++;
            }
            return added;
        }

        private static IReadOnlyList<ContextPathCandidate> BuildSingleSpliceCandidates(
            VisemePhraseCompiledModel model,
            IReadOnlyList<List<VisemePhraseToken>> positiveTakes,
            ISet<string> existingKeys,
            VisemePhraseDistanceOptions distanceOptions)
        {
            var aggregates = new Dictionary<string, SplicePathAggregate>(
                StringComparer.Ordinal);
            if (model == null || positiveTakes == null || positiveTakes.Count < 2)
                return Array.Empty<ContextPathCandidate>();

            for (var prefixTakeIndex = 0;
                 prefixTakeIndex < positiveTakes.Count;
                 prefixTakeIndex++)
            for (var suffixTakeIndex = 0;
                 suffixTakeIndex < positiveTakes.Count;
                 suffixTakeIndex++)
            {
                if (prefixTakeIndex == suffixTakeIndex) continue;
                var prefix = positiveTakes[prefixTakeIndex];
                var suffix = positiveTakes[suffixTakeIndex];
                if (prefix == null || suffix == null ||
                    prefix.Count < 3 || suffix.Count < 3)
                    continue;

                var alignment = VisemePhraseTraceMath.Align(
                    prefix, suffix, distanceOptions);
                foreach (var pair in alignment.pairs)
                {
                    // Both takes must contribute a real edge on their side of
                    // the anchor. Exact consonantal anchors are substantially
                    // more stable than Oculus vowel winners, and stop a common
                    // vowel from joining unrelated portions of the word.
                    if (pair.first < 1 || pair.second < 1 ||
                        pair.first >= prefix.Count ||
                        pair.second + 1 >= suffix.Count)
                        continue;
                    var anchor = prefix[pair.first].viseme;
                    if (anchor <= 0 || anchor >= 10 ||
                        suffix[pair.second].viseme != anchor)
                        continue;

                    var sequence = prefix.Take(pair.first + 1)
                        .Select(token => token.viseme)
                        .Concat(suffix.Skip(pair.second + 1)
                            .Select(token => token.viseme))
                        .ToArray();
                    if (sequence.Length < VisemePhraseCompileOptions.MinimumInformativeRuns ||
                        sequence.Length > VisemePhraseCompileOptions.MaximumStatesPerVariant)
                        continue;
                    var key = VisemeSequenceKey(sequence);
                    if (existingKeys.Contains(key)) continue;

                    var seed = BuildSpliceSeed(prefix, pair.first, suffix, pair.second);
                    if (seed.Count != sequence.Length ||
                        VisemePhraseRuntimeLanguage.Score(model, seed, false) <
                        1f - RuntimeCostEpsilon)
                        continue;
                    if (!aggregates.TryGetValue(key, out var aggregate))
                    {
                        aggregate = new SplicePathAggregate
                        {
                            key = key,
                            sequence = sequence
                        };
                        aggregates[key] = aggregate;
                    }
                    aggregate.sourcePairs.Add(
                        prefixTakeIndex.ToString(CultureInfo.InvariantCulture) + ">" +
                        suffixTakeIndex.ToString(CultureInfo.InvariantCulture));
                    aggregate.seeds.Add(seed);
                }
            }

            var output = new List<ContextPathCandidate>();
            foreach (var aggregate in aggregates.Values)
            {
                var best = aggregate.seeds
                    .Select(seed => new
                    {
                        seed,
                        closest = Enumerable.Range(0, positiveTakes.Count)
                            .Where(index => positiveTakes[index] != null &&
                                            positiveTakes[index].Count > 0)
                            .Select(index => new
                            {
                                index,
                                distance = VisemePhraseTraceMath.DtwDistance(
                                    seed, positiveTakes[index], distanceOptions)
                            })
                            .OrderBy(item => item.distance)
                            .ThenBy(item => item.index)
                            .First()
                    })
                    .OrderBy(item => item.closest.distance)
                    .ThenBy(item => item.closest.index)
                    .First();
                output.Add(new ContextPathCandidate
                {
                    key = aggregate.key,
                    sequence = aggregate.sequence,
                    seed = best.seed,
                    closestTakeIndex = best.closest.index,
                    distance = best.closest.distance,
                    boundaryOverlap = ContextBoundaryOverlap(
                        aggregate.sequence, positiveTakes),
                    backoffCount = 0,
                    spliceSupport = aggregate.sourcePairs.Count,
                    boundaryEvidenceSeconds = best.seed[0].DurationSeconds +
                                              best.seed[best.seed.Count - 1]
                                                  .DurationSeconds
                });
            }
            return output;
        }

        private static List<VisemePhraseToken> BuildSpliceSeed(
            IReadOnlyList<VisemePhraseToken> prefix,
            int prefixAnchor,
            IReadOnlyList<VisemePhraseToken> suffix,
            int suffixAnchor)
        {
            var source = prefix.Take(prefixAnchor + 1)
                .Concat(suffix.Skip(suffixAnchor + 1))
                .ToArray();
            var output = new List<VisemePhraseToken>(source.Length);
            var clock = 0f;
            for (var index = 0; index < source.Length; index++)
            {
                var token = source[index];
                var duration = Mathf.Max(MinimumTimingSeconds, token.DurationSeconds);
                // The anchor belongs to both aligned paths. Blend its duration
                // evidence instead of making the arbitrary prefix take wholly
                // authoritative at the switch point.
                if (index == prefixAnchor)
                    duration = Mathf.Max(MinimumTimingSeconds,
                        0.5f * (duration + suffix[suffixAnchor].DurationSeconds));
                output.Add(new VisemePhraseToken
                {
                    viseme = token.viseme,
                    startSeconds = clock,
                    endSeconds = clock + duration,
                    meanVoice = token.meanVoice,
                    frameCount = Math.Max(1, token.frameCount)
                });
                clock += duration;
            }
            return output;
        }

        private static void EnumerateContextPaths(
            IList<int> path,
            int minimumLength,
            int maximumLength,
            ISet<int> endPairs,
            IReadOnlyDictionary<int, HashSet<int>> transitions,
            IReadOnlyDictionary<int, int> edgeSupport,
            int backoffCount,
            IDictionary<string, EnumeratedContextPath> output,
            int maximumPaths)
        {
            if (path.Count >= minimumLength && endPairs.Contains(ContextPairKey(
                    path[path.Count - 2], path[path.Count - 1])))
            {
                var sequence = path.ToArray();
                var key = VisemeSequenceKey(sequence);
                if (!output.TryGetValue(key, out var previous) ||
                    backoffCount < previous.backoffCount)
                    output[key] = new EnumeratedContextPath
                    {
                        sequence = sequence,
                        backoffCount = backoffCount
                    };
                if (output.Count >= maximumPaths) return;
            }
            if (path.Count >= maximumLength) return;
            var context = ContextPairKey(
                path[path.Count - 2], path[path.Count - 1]);
            var nextValues = transitions.TryGetValue(context, out var observed)
                ? observed
                : new HashSet<int>();
            foreach (var next in nextValues.OrderBy(value => value))
            {
                // Equal adjacent winners are one RLE run and therefore cannot
                // form two observable Animator states.
                if (next == path[path.Count - 1]) continue;
                path.Add(next);
                EnumerateContextPaths(
                    path,
                    minimumLength,
                    maximumLength,
                    endPairs,
                    transitions,
                    edgeSupport,
                    backoffCount,
                    output,
                    maximumPaths);
                path.RemoveAt(path.Count - 1);
                if (output.Count >= maximumPaths) return;
            }

            if (backoffCount > 0 || output.Count >= maximumPaths) return;
            var previousViseme = path[path.Count - 2];
            var currentViseme = path[path.Count - 1];
            if (previousViseme == currentViseme ||
                nextValues.Contains(previousViseme) ||
                !edgeSupport.TryGetValue(
                    ContextPairKey(previousViseme, currentViseme),
                    out var forwardSupport) ||
                !edgeSupport.TryGetValue(
                    ContextPairKey(currentViseme, previousViseme),
                    out var reverseSupport) ||
                forwardSupport < 2 || reverseSupport < 2)
                return;
            // One backed-off ABA island is permitted only when both directed
            // edges appeared in at least two distinct takes. This covers hard
            // arg-max coarticulation chatter without allowing a general unseen
            // trigram, a one-off edge, or multiple independent guesses.
            path.Add(previousViseme);
            EnumerateContextPaths(
                path,
                minimumLength,
                maximumLength,
                endPairs,
                transitions,
                edgeSupport,
                1,
                output,
                maximumPaths);
            path.RemoveAt(path.Count - 1);
        }

        private static VisemePhraseModelVariant BuildContextVariant(
            int variantIndex,
            ContextPathCandidate candidate,
            IReadOnlyList<List<VisemePhraseToken>> positiveTakes,
            IReadOnlyList<VisemePhraseEnrollmentTrace> rawTakes,
            float strictness)
        {
            var variant = new VisemePhraseModelVariant
            {
                id = "v" + variantIndex.ToString(CultureInfo.InvariantCulture),
                sourceTakeIndex = candidate.closestTakeIndex,
                sourceTakeId = candidate.closestTakeIndex < rawTakes.Count
                    ? rawTakes[candidate.closestTakeIndex]?.takeId ?? string.Empty
                    : string.Empty,
                inferredContextPath = true,
                cohesion = Mathf.Clamp01(1f - candidate.distance)
            };
            RobustBounds(
                new[] { candidate.seed.Sum(token => token.DurationSeconds) },
                strictness,
                out variant.medianDurationSeconds,
                out variant.minimumDurationSeconds,
                out variant.maximumDurationSeconds);
            for (var stateIndex = 0;
                 stateIndex < candidate.sequence.Length;
                 stateIndex++)
            {
                var observations = ContextObservations(
                    candidate.sequence, stateIndex, positiveTakes);
                if (observations.Count == 0) return null;
                RobustBounds(
                    observations.Select(token => token.DurationSeconds),
                    strictness,
                    out var median,
                    out var minimum,
                    out var maximum);
                var viseme = candidate.sequence[stateIndex];
                var emissions = new float[VisemePhraseTraceMath.VisemeCount];
                emissions[viseme] = 1f;
                variant.states.Add(new VisemePhraseModelState
                {
                    index = stateIndex,
                    primaryViseme = viseme,
                    aliasVisemes = Array.Empty<int>(),
                    aliasLikelihoods = Array.Empty<float>(),
                    emissionLikelihoods = emissions,
                    medianDurationSeconds = median,
                    minimumDurationSeconds = minimum,
                    maximumDurationSeconds = maximum,
                    meanVoice = Mathf.Clamp01(observations.Average(token => token.meanVoice)),
                    allowSkip = false,
                    skipPenalty = 0f
                });
            }
            // A context path is already constrained by its observed boundary,
            // local order-two language, optional single ABA backoff, and the
            // negative calibration below. Per-phone minima are only a debounce
            // guard here: distributing a whole-phrase minimum into individual
            // phones makes natural duration transfer impossible (for example a
            // shorter O followed by a longer kk). Keep learned held-phone
            // maxima, and describe the exact rectangle envelope honestly.
            var minimums = variant.states.Select(state => Mathf.Min(
                    state.minimumDurationSeconds, ContextRunDebounceSeconds))
                .ToArray();
            var maximums = variant.states.Select(state =>
                    Mathf.Max(state.maximumDurationSeconds, ContextRunDebounceSeconds))
                .ToArray();
            var excessMaximum = Mathf.Max(0f,
                maximums.Sum() - variant.maximumDurationSeconds);
            for (var cursor = maximums.Length - 1;
                 cursor >= 0 && excessMaximum > MinimumTimingSeconds;
                 cursor--)
            {
                var seedDuration = candidate.seed[cursor].DurationSeconds;
                var capacity = Mathf.Max(0f, maximums[cursor] - seedDuration);
                var adjustment = Mathf.Min(excessMaximum, capacity);
                maximums[cursor] -= adjustment;
                excessMaximum -= adjustment;
            }
            if (excessMaximum > MinimumTimingSeconds) return null;
            variant.minimumDurationSeconds = minimums.Sum();
            var rectangle = new VisemePhraseRuntimeTimingRectangle
            {
                sourceTakeIndex = candidate.closestTakeIndex,
                skippedStateIndex = -1,
                minimumDurationSeconds = minimums,
                maximumDurationSeconds = maximums
            };
            variant.runtimeTimingRectangles.Add(rectangle);
            return variant;
        }

        private static List<VisemePhraseToken> BuildContextSeed(
            IReadOnlyList<int> sequence,
            IReadOnlyList<List<VisemePhraseToken>> positiveTakes)
        {
            var output = new List<VisemePhraseToken>();
            var clock = 0f;
            for (var index = 0; index < sequence.Count; index++)
            {
                var observations = ContextObservations(sequence, index, positiveTakes);
                if (observations.Count == 0) return new List<VisemePhraseToken>();
                var duration = Mathf.Max(
                    MinimumTimingSeconds,
                    VisemePhraseTraceMath.Median(
                        observations.Select(token => token.DurationSeconds)));
                output.Add(new VisemePhraseToken
                {
                    viseme = sequence[index],
                    startSeconds = clock,
                    endSeconds = clock + duration,
                    meanVoice = Mathf.Clamp01(observations.Average(token => token.meanVoice)),
                    frameCount = Mathf.Max(1, Mathf.RoundToInt((float)
                        observations.Average(token => token.frameCount)))
                });
                clock += duration;
            }
            return output;
        }

        private static List<VisemePhraseToken> ContextObservations(
            IReadOnlyList<int> sequence,
            int sequenceIndex,
            IReadOnlyList<List<VisemePhraseToken>> positiveTakes)
        {
            var output = new List<VisemePhraseToken>();
            foreach (var take in positiveTakes)
            {
                if (take == null) continue;
                for (var takeIndex = 0; takeIndex < take.Count; takeIndex++)
                {
                    if (take[takeIndex].viseme != sequence[sequenceIndex]) continue;
                    var leftMatches = sequenceIndex == 0
                        ? takeIndex == 0 && sequence.Count > 1 && take.Count > 1 &&
                          take[1].viseme == sequence[1]
                        : takeIndex > 0 &&
                          take[takeIndex - 1].viseme == sequence[sequenceIndex - 1];
                    var rightMatches = sequenceIndex == sequence.Count - 1
                        ? takeIndex == take.Count - 1 && sequenceIndex > 0 && takeIndex > 0 &&
                          take[takeIndex - 1].viseme == sequence[sequenceIndex - 1]
                        : takeIndex + 1 < take.Count &&
                          take[takeIndex + 1].viseme == sequence[sequenceIndex + 1];
                    if (leftMatches && rightMatches) output.Add(take[takeIndex]);
                }
            }
            if (output.Count > 0) return output;
            if (sequenceIndex > 0 && sequenceIndex + 1 < sequence.Count &&
                sequence[sequenceIndex - 1] == sequence[sequenceIndex + 1])
            {
                var outer = sequence[sequenceIndex - 1];
                foreach (var take in positiveTakes)
                {
                    if (take == null) continue;
                    for (var takeIndex = 0; takeIndex < take.Count; takeIndex++)
                    {
                        if (take[takeIndex].viseme != sequence[sequenceIndex]) continue;
                        var hasLeftEdge = takeIndex > 0 &&
                                          take[takeIndex - 1].viseme == outer;
                        var hasRightEdge = takeIndex + 1 < take.Count &&
                                           take[takeIndex + 1].viseme == outer;
                        if (hasLeftEdge || hasRightEdge)
                            output.Add(take[takeIndex]);
                    }
                }
                if (output.Count > 0) return output;
            }
            // Endpoint and branch alignment should always find local context,
            // but fall back to the same observed winner rather than inventing
            // timing if boundary normalization removed one neighboring token.
            foreach (var take in positiveTakes)
            foreach (var token in take ?? new List<VisemePhraseToken>())
                if (token.viseme == sequence[sequenceIndex]) output.Add(token);
            return output;
        }

        private static int ContextPairKey(int first, int second) =>
            first * VisemePhraseTraceMath.VisemeCount + second;

        private static int ContextBoundaryOverlap(
            IReadOnlyList<int> sequence,
            IReadOnlyList<List<VisemePhraseToken>> positiveTakes)
        {
            if (sequence == null || sequence.Count < 2 || positiveTakes == null)
                return int.MaxValue;
            var starts = new HashSet<int>();
            var ends = new HashSet<int>();
            for (var index = 0; index < positiveTakes.Count; index++)
            {
                var take = positiveTakes[index];
                if (take == null || take.Count < 2) continue;
                if (take[0].viseme == sequence[0] &&
                    take[1].viseme == sequence[1]) starts.Add(index);
                if (take[take.Count - 2].viseme == sequence[sequence.Count - 2] &&
                    take[take.Count - 1].viseme == sequence[sequence.Count - 1])
                    ends.Add(index);
            }
            starts.IntersectWith(ends);
            return starts.Count;
        }

        private static int ContextWeakBoundaryCount(IReadOnlyList<int> sequence)
        {
            if (sequence == null || sequence.Count == 0) return 2;
            // Oculus indices 1..9 are consonantal mouth classes; 10..14 are
            // vowels. Silence is never expected after enrollment trimming.
            var firstIsWeak = sequence[0] == 0 || sequence[0] >= 10;
            var last = sequence[sequence.Count - 1];
            var lastIsWeak = last == 0 || last >= 10;
            return (firstIsWeak ? 1 : 0) + (lastIsWeak ? 1 : 0);
        }

        private static int ContextAlternationCount(IReadOnlyList<int> sequence)
        {
            if (sequence == null) return int.MaxValue;
            var count = 0;
            for (var index = 0; index + 2 < sequence.Count; index++)
                if (sequence[index] == sequence[index + 2]) count++;
            return count;
        }

        private static string VisemeSequenceKey(IEnumerable<int> visemes) =>
            string.Join(",", visemes.Select(viseme =>
                viseme.ToString(CultureInfo.InvariantCulture)));

        private static CalibrationStats EvaluateCalibration(
            VisemePhraseCompiledModel model,
            IReadOnlyList<List<VisemePhraseToken>> positiveTakes,
            IReadOnlyList<List<VisemePhraseToken>> negativeTakes)
        {
            var result = new CalibrationStats
            {
                bestNegative = 1f,
                negativeCount = negativeTakes?.Count ?? 0
            };
            if (positiveTakes != null)
                for (var i = 0; i < positiveTakes.Count; i++)
                    result.worstPositive = Mathf.Max(
                        result.worstPositive,
                        Score(model, positiveTakes[i], false));
            if (negativeTakes != null)
                for (var i = 0; i < negativeTakes.Count; i++)
                    result.bestNegative = Mathf.Min(
                        result.bestNegative,
                        Score(model, negativeTakes[i], true));
            result.separation = result.negativeCount > 0
                ? result.bestNegative - result.worstPositive
                : 0f;
            return result;
        }

        private static void PruneOptionalFeatures(
            VisemePhraseCompiledModel model,
            IReadOnlyList<List<VisemePhraseToken>> positiveTakes,
            IReadOnlyList<List<VisemePhraseToken>> negativeTakes,
            ICollection<VisemePhraseDiagnostic> messages)
        {
            if (negativeTakes == null || negativeTakes.Count == 0) return;
            var baseline = EvaluateCalibration(model, positiveTakes, negativeTakes);
            if (baseline.separation >= model.minimumNegativeMargin) return;
            var originalWorstPositive = baseline.worstPositive;
            var pruned = 0;
            while (baseline.separation < model.minimumNegativeMargin)
            {
                OptionalFeature bestFeature = null;
                var bestStats = baseline;
                foreach (var feature in OptionalFeatures(model))
                {
                    feature.DisableTemporarily();
                    VisemePhraseRuntimeLanguage.BuildPositiveTimingRectangles(
                        model, positiveTakes, messages);
                    var candidate = EvaluateCalibration(model, positiveTakes, negativeTakes);
                    feature.Restore();
                    VisemePhraseRuntimeLanguage.BuildPositiveTimingRectangles(
                        model, positiveTakes, messages);
                    // An optional feature may only be pruned when every enrolled
                    // take remains representable by the exact baked language.
                    if (candidate.worstPositive >= 1f - RuntimeCostEpsilon ||
                        candidate.worstPositive >
                        Mathf.Max(0.55f, originalWorstPositive + 0.2f))
                        continue;
                    var betterMargin = candidate.separation > bestStats.separation + 1e-5f;
                    var equalMarginBetterRejection =
                        Mathf.Abs(candidate.separation - bestStats.separation) <= 1e-5f &&
                        candidate.bestNegative > bestStats.bestNegative + 1e-5f;
                    if (!betterMargin && !equalMarginBetterRejection) continue;
                    bestFeature = feature;
                    bestStats = candidate;
                }

                if (bestFeature == null) break;
                bestFeature.PrunePermanently();
                VisemePhraseRuntimeLanguage.BuildPositiveTimingRectangles(
                    model, positiveTakes, messages);
                baseline = EvaluateCalibration(model, positiveTakes, negativeTakes);
                pruned++;
            }

            if (pruned <= 0) return;
            messages.Add(new VisemePhraseDiagnostic(
                VisemePhraseDiagnosticSeverity.Info,
                "optional_feature_pruned",
                pruned.ToString(CultureInfo.InvariantCulture) +
                " permissive alias or skip was removed because it matched recorded background speech."));
        }

        private static IEnumerable<OptionalFeature> OptionalFeatures(
            VisemePhraseCompiledModel model)
        {
            foreach (var variant in model.variants)
            foreach (var state in variant.states)
            {
                if (state.allowSkip) yield return OptionalFeature.ForSkip(state);
                if (state.aliasVisemes == null) continue;
                for (var i = 0; i < state.aliasVisemes.Length; i++)
                    yield return OptionalFeature.ForAlias(state, state.aliasVisemes[i]);
            }
        }

        private static VisemePhraseModelVariant BuildVariant(
            int variantIndex,
            IReadOnlyList<int> cluster,
            IReadOnlyList<List<VisemePhraseToken>> allTokens,
            IReadOnlyList<VisemePhraseEnrollmentTrace> rawTakes,
            float[,] pairwise,
            float strictness,
            VisemePhraseCompileOptions options,
            ICollection<VisemePhraseDiagnostic> messages)
        {
            // Prefer the most complete trace as the linear reference. When a
            // shorter pronunciation omits one run, the runtime model can encode
            // that run as its single optional state. Choosing the shorter medoid
            // would turn the same variation into an unrepresentable insertion.
            var referenceIndex = cluster
                .OrderByDescending(index => allTokens[index].Count)
                .ThenBy(index => cluster.Sum(other => pairwise[index, other]))
                .ThenBy(index => index)
                .First();
            var reference = ReduceStates(
                allTokens[referenceIndex],
                VisemePhraseCompileOptions.MaximumStatesPerVariant);
            if (allTokens[referenceIndex].Count > VisemePhraseCompileOptions.MaximumStatesPerVariant)
            {
                VisemePhraseValidation.Warning(
                    messages,
                    "state_cap_applied",
                    "A pronunciation exceeded twelve runs and was deterministically reduced to the Animator state cap.");
            }

            var observations = new List<StateObservation>[reference.Count];
            for (var i = 0; i < observations.Length; i++)
                observations[i] = new List<StateObservation>();
            var totalDurations = new List<float>();
            foreach (var takeIndex in cluster)
            {
                var take = allTokens[takeIndex];
                totalDurations.Add(take.Sum(token => token.DurationSeconds));
                var alignment = VisemePhraseTraceMath.Align(reference, take, options.distance);
                AddAlignedObservations(reference, take, alignment, observations);
            }

            var variant = new VisemePhraseModelVariant
            {
                id = "v" + variantIndex.ToString(CultureInfo.InvariantCulture),
                sourceTakeIndex = referenceIndex,
                sourceTakeId = rawTakes[referenceIndex]?.takeId ?? string.Empty,
                cohesion = ClusterCohesion(cluster, pairwise)
            };
            RobustBounds(totalDurations, strictness, out variant.medianDurationSeconds,
                out variant.minimumDurationSeconds, out variant.maximumDurationSeconds);
            var skipEvidence = new float[reference.Count];
            for (var stateIndex = 0; stateIndex < reference.Count; stateIndex++)
            {
                variant.states.Add(BuildState(
                    stateIndex,
                    reference[stateIndex].viseme,
                    observations[stateIndex],
                    strictness,
                    out skipEvidence[stateIndex]));
            }
            // Endpoint omissions are complete short pronunciations, not safe
            // optional deletions: an omitted final state can become an
            // indistinguishable prefix of the longer path. Whole-sequence path
            // retention preserves those demonstrated short takes explicitly.
            if (skipEvidence.Length > 2)
            {
                var bestSkip = 1;
                for (var i = 2; i + 1 < skipEvidence.Length; i++)
                    if (skipEvidence[i] > skipEvidence[bestSkip] + 1e-7f) bestSkip = i;
                if (skipEvidence[bestSkip] >= 0.12f)
                    variant.states[bestSkip].allowSkip = true;
            }
            return variant;
        }

        private static VisemePhraseModelState BuildState(
            int stateIndex,
            int primaryViseme,
            IReadOnlyList<StateObservation> observations,
            float strictness,
            out float skipEvidence)
        {
            var present = observations
                .Where(observation => observation.viseme >= 0)
                .ToArray();
            var durations = present.Select(observation => observation.duration).ToArray();
            RobustBounds(durations, strictness, out var median, out var minimum, out var maximum);
            var state = new VisemePhraseModelState
            {
                index = stateIndex,
                primaryViseme = primaryViseme,
                medianDurationSeconds = median,
                minimumDurationSeconds = minimum,
                maximumDurationSeconds = maximum,
                meanVoice = present.Length == 0
                    ? 0f
                    : Mathf.Clamp01(present.Average(observation => observation.voice)),
                allowSkip = false,
                skipPenalty = Mathf.Lerp(0.2f, 0.5f, strictness),
                emissionLikelihoods = new float[VisemePhraseTraceMath.VisemeCount]
            };
            state.emissionLikelihoods[primaryViseme] = 1f;

            var requiredSupport = Mathf.Lerp(0.25f, 0.5f, strictness);
            var candidates = present
                .GroupBy(observation => observation.viseme)
                .Where(group => group.Key != primaryViseme)
                .Select(group => new AliasSupport
                {
                    viseme = group.Key,
                    count = group.Count(),
                    support = group.Count() / (float)Mathf.Max(1, present.Length)
                })
                // A single hard-Viseme winner is not enough evidence to widen
                // the runtime language. Position-specific aliases need support
                // from at least two declared positives; a lone different take
                // remains a concrete retake diagnostic instead of becoming a
                // false-positive path for every future utterance.
                .Where(alias => alias.count >= 2 &&
                                alias.support + 1e-6f >= requiredSupport)
                .OrderByDescending(alias => alias.support)
                .ThenBy(alias => alias.viseme)
                .Take(3)
                .ToArray();
            state.aliasVisemes = candidates.Select(alias => alias.viseme).ToArray();
            state.aliasLikelihoods = candidates.Select(alias =>
                Mathf.Clamp01(
                    alias.support * Mathf.Lerp(0.9f, 0.68f, strictness))).ToArray();
            for (var i = 0; i < candidates.Length; i++)
                state.emissionLikelihoods[candidates[i].viseme] = state.aliasLikelihoods[i];
            var missingCount = observations.Count(observation =>
                observation.viseme < 0);
            var meanAlignmentConfidence = observations.Count == 0
                ? 1f
                : observations.Average(observation => observation.alignmentConfidence);
            var brevity = 1f - Mathf.Clamp01(median / 0.12f);
            // A repeatable two-take pronunciation branch has only one possible
            // deletion vote. Requiring two absolute votes made a learned skip
            // mathematically impossible in that branch, even when half of its
            // captures omitted the run. Use proportional evidence instead while
            // still requiring both present and missing observations.
            var hasDeletionEvidence = observations.Count >= 2 &&
                                      missingCount > 0 &&
                                      missingCount < observations.Count &&
                                      missingCount * 2 >= observations.Count;
            skipEvidence = hasDeletionEvidence
                ? (1f - Mathf.Clamp01(meanAlignmentConfidence)) *
                  Mathf.Lerp(0.55f, 1f, brevity)
                : 0f;
            return state;
        }

        private static void AddAlignedObservations(
            IReadOnlyList<VisemePhraseToken> reference,
            IReadOnlyList<VisemePhraseToken> take,
            VisemePhraseAlignment alignment,
            IReadOnlyList<List<StateObservation>> output)
        {
            var mappedReferenceCounts = new Dictionary<int, HashSet<int>>();
            for (var i = 0; i < alignment.pairs.Count; i++)
            {
                var pair = alignment.pairs[i];
                if (pair.first < 0 || pair.second < 0) continue;
                if (!mappedReferenceCounts.TryGetValue(pair.second, out var references))
                {
                    references = new HashSet<int>();
                    mappedReferenceCounts[pair.second] = references;
                }
                references.Add(pair.first);
            }

            for (var referenceIndex = 0; referenceIndex < reference.Count; referenceIndex++)
            {
                var mapped = alignment.pairs
                    .Where(pair => pair.first == referenceIndex && pair.second >= 0)
                    .Select(pair => pair.second)
                    .Distinct()
                    .ToArray();
                if (mapped.Length == 0)
                {
                    output[referenceIndex].Add(new StateObservation
                    {
                        // Missing is deletion evidence. Do not fabricate another
                        // vote for the reference label or its duration.
                        viseme = -1,
                        duration = 0f,
                        voice = 0f,
                        alignmentConfidence = 0f
                    });
                    continue;
                }

                var duration = 0f;
                var voiceSum = 0f;
                var confidenceSum = 0f;
                var visemeDurations = new Dictionary<int, float>();
                for (var i = 0; i < mapped.Length; i++)
                {
                    var token = take[mapped[i]];
                    var divisor = Mathf.Max(1, mappedReferenceCounts[mapped[i]].Count);
                    var share = token.DurationSeconds / divisor;
                    duration += share;
                    voiceSum += token.meanVoice * share;
                    confidenceSum += share / divisor;
                    visemeDurations[token.viseme] =
                        (visemeDurations.TryGetValue(token.viseme, out var previous) ? previous : 0f) + share;
                }
                var observedViseme = visemeDurations
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key)
                    .First().Key;
                output[referenceIndex].Add(new StateObservation
                {
                    viseme = observedViseme,
                    duration = Mathf.Max(0.005f, duration),
                    voice = duration > 1e-6f ? Mathf.Clamp01(voiceSum / duration) : 0f,
                    alignmentConfidence = duration > 1e-6f
                        ? Mathf.Clamp01(confidenceSum / duration)
                        : 0f
                });
            }
        }

        private static void RobustBounds(
            IEnumerable<float> values,
            float strictness,
            out float median,
            out float minimum,
            out float maximum)
        {
            var positive = values?.Where(value => IsFinite(value) && value > 0f).ToArray() ??
                           Array.Empty<float>();
            if (positive.Length == 0)
            {
                median = 0.005f;
                minimum = median / GuaranteedTempoFactor;
                maximum = median * GuaranteedTempoFactor;
                return;
            }

            var logDurations = positive
                .Select(value => Mathf.Log(Mathf.Max(MinimumTimingSeconds, value)))
                .ToArray();
            var medianLogDuration = VisemePhraseTraceMath.Median(logDurations);
            median = Mathf.Max(MinimumTimingSeconds, Mathf.Exp(medianLogDuration));
            var logMad = VisemePhraseTraceMath.MedianAbsoluteDeviation(logDurations);
            var learnedTempoFactor = Mathf.Exp(
                logMad * Mathf.Lerp(3f, 1.65f, Mathf.Clamp01(strictness)));

            // Every learned state and whole-phrase variant accepts the approved
            // 0.5x-2.0x tempo range. Strong repeatable evidence may widen that
            // only slightly, retaining a hard distinction from clearly
            // too-fast or held input.
            var tempoFactor = Mathf.Clamp(
                Mathf.Max(GuaranteedTempoFactor, learnedTempoFactor),
                GuaranteedTempoFactor,
                MaximumLearnedTempoFactor);
            minimum = Mathf.Max(MinimumTimingSeconds, median / tempoFactor);
            maximum = Mathf.Max(minimum + MinimumTimingSeconds, median * tempoFactor);
        }

        private static List<VisemePhraseToken> ReduceStates(
            IReadOnlyList<VisemePhraseToken> source,
            int maximum)
        {
            if (source.Count <= maximum) return source.Select(CloneToken).ToList();
            var result = new List<VisemePhraseToken>(maximum);
            var previous = -1;
            for (var i = 0; i < maximum; i++)
            {
                var index = Mathf.RoundToInt(i * (source.Count - 1f) / (maximum - 1f));
                index = Mathf.Max(previous + 1, Mathf.Min(source.Count - (maximum - i), index));
                result.Add(CloneToken(source[index]));
                previous = index;
            }
            return result;
        }

        private static VisemePhraseToken CloneToken(VisemePhraseToken source)
        {
            return new VisemePhraseToken
            {
                viseme = source.viseme,
                startSeconds = source.startSeconds,
                endSeconds = source.endSeconds,
                meanVoice = source.meanVoice,
                frameCount = source.frameCount
            };
        }

        /// <summary>
        /// Enrollment always contains four takes, so there are only three
        /// distinct balanced 2+2 partitions. Evaluate all statistically
        /// credible partitions against the exact finite language that will be
        /// baked into the Animator instead of trusting one k-means seed. This
        /// keeps pronunciation branches bounded while avoiding a common false
        /// failure where the closest DTW partition happens to require two
        /// deletions and a slightly less compact partition needs only one.
        /// </summary>
        internal static List<List<int>> SelectRuntimeCompatibleClusters(
            float[,] distances,
            float strictness,
            VisemePhraseCompileOptions options,
            IReadOnlyList<List<VisemePhraseToken>> positiveTakes,
            IReadOnlyList<VisemePhraseEnrollmentTrace> rawTakes)
        {
            var count = distances.GetLength(0);
            var all = Enumerable.Range(0, count).ToList();
            var oneCluster = new List<List<int>> { all };
            if (count != 4) return oneCluster;

            var globalMedoid = Medoid(all, distances);
            var oneCost = all.Sum(index => distances[index, globalMedoid]);
            var farthest = 0f;
            for (var first = 0; first < count; first++)
                for (var second = first + 1; second < count; second++)
                    farthest = Mathf.Max(farthest, distances[first, second]);

            // Strictness controls acceptance/timing tolerance. Whether two
            // repeatable pronunciations exist is a property of the recordings,
            // so use the explicit topology threshold directly.
            var requiredImprovement = options.minimumVariantImprovement;
            var requiredSeparation = Mathf.Lerp(
                Mathf.Max(0.28f, options.minimumVariantSeparation),
                options.minimumVariantSeparation,
                strictness);
            var candidates = BalancedPartitions(count)
                .Select(clusters => new ClusterCandidate
                {
                    clusters = clusters,
                    cost = clusters.Sum(cluster =>
                        cluster.Sum(index =>
                            distances[index, Medoid(cluster, distances)]))
                })
                .Select(candidate =>
                {
                    candidate.improvement = oneCost <= 1e-7f
                        ? 0f
                        : (oneCost - candidate.cost) / oneCost;
                    return candidate;
                })
                .Where(candidate =>
                    candidate.improvement + 1e-7f >= requiredImprovement &&
                    farthest + 1e-7f >= requiredSeparation)
                .OrderBy(candidate => candidate.cost)
                .ThenBy(candidate => ClusterKey(candidate.clusters),
                    StringComparer.Ordinal)
                .Select(candidate => candidate.clusters)
                .ToList();

            // A statistical branch is useful only when the bounded runtime
            // matcher can reproduce every declared positive with its exact
            // alias/skip/timing semantics. Try the next legal partition when
            // the lowest-DTW one cannot be baked.
            candidates.Add(oneCluster);
            for (var candidateIndex = 0;
                 candidateIndex < candidates.Count;
                 candidateIndex++)
            {
                if (CanReplayClusters(
                        candidates[candidateIndex],
                        positiveTakes,
                        rawTakes,
                        distances,
                        strictness,
                        options))
                    return candidates[candidateIndex];
            }

            // Preserve the ordinary compiler diagnostics when no legal finite
            // language fits. Validation will identify the concrete take(s)
            // that cannot replay instead of silently expanding the state cap.
            return oneCluster;
        }

        private static IEnumerable<List<List<int>>> BalancedPartitions(int count)
        {
            if (count != 4) yield break;
            // Requiring take zero in the first half removes complement
            // duplicates, leaving exactly the three 2+2 set partitions.
            for (var partner = 1; partner < count; partner++)
            {
                var first = new List<int> { 0, partner };
                var second = Enumerable.Range(0, count)
                    .Where(index => !first.Contains(index))
                    .ToList();
                yield return new List<List<int>> { first, second };
            }
        }

        private static bool CanReplayClusters(
            IReadOnlyList<List<int>> clusters,
            IReadOnlyList<List<VisemePhraseToken>> positiveTakes,
            IReadOnlyList<VisemePhraseEnrollmentTrace> rawTakes,
            float[,] pairwise,
            float strictness,
            VisemePhraseCompileOptions options)
        {
            if (clusters == null || clusters.Count == 0 ||
                positiveTakes == null || positiveTakes.Count == 0)
                return false;
            var scratchMessages = new List<VisemePhraseDiagnostic>();
            var probe = new VisemePhraseCompiledModel();
            for (var clusterIndex = 0; clusterIndex < clusters.Count; clusterIndex++)
            {
                probe.variants.Add(BuildVariant(
                    clusterIndex,
                    clusters[clusterIndex],
                    positiveTakes,
                    rawTakes,
                    pairwise,
                    strictness,
                    options,
                    scratchMessages));
            }

            VisemePhraseRuntimeLanguage.BuildPositiveTimingRectangles(
                probe, positiveTakes, scratchMessages);
            return positiveTakes.All(take =>
                VisemePhraseRuntimeLanguage.Score(probe, take, false) <
                1f - RuntimeCostEpsilon);
        }

        private static string ClusterKey(IReadOnlyList<List<int>> clusters) =>
            string.Join("|", clusters.Select(cluster =>
                string.Join(",", cluster.OrderBy(index => index))));

        /// <summary>
        /// Natural-speech captures commonly contain one classifier winner from
        /// the word immediately before or after the prompt. Those labels are not
        /// part of the phrase and cannot be made repeatable by asking the wearer
        /// to imitate an earlier take. Find two or more 3-of-4 exact consensus
        /// anchors, crop every take to that shared interior, then retain exactly
        /// one post-anchor token when it is present in at least three takes. The
        /// final token remains useful phrase identity; arbitrary release tails do
        /// not. Paused Command deliberately bypasses this normalization because
        /// its recorded silence supplies an explicit boundary.
        /// </summary>
        internal static void NormalizeNaturalSpeechBoundaries(
            IList<List<VisemePhraseToken>> takes,
            VisemePhraseDistanceOptions options,
            ICollection<VisemePhraseDiagnostic> messages)
        {
            if (takes == null || takes.Count < 3 ||
                takes.Any(take => take == null || take.Count == 0)) return;

            var requiredSupport = Mathf.CeilToInt(takes.Count * 0.75f);
            var bestReference = -1;
            var bestAnchors = Array.Empty<int>();
            var bestSupport = -1;
            var bestCost = float.PositiveInfinity;
            for (var candidate = 0; candidate < takes.Count; candidate++)
            {
                var reference = takes[candidate];
                var support = new int[reference.Count];
                var totalCost = 0f;
                for (var takeIndex = 0; takeIndex < takes.Count; takeIndex++)
                {
                    var alignment = VisemePhraseTraceMath.Align(
                        reference, takes[takeIndex], options);
                    totalCost += alignment.normalizedCost;
                    foreach (var pair in alignment.pairs)
                    {
                        if (pair.first < 0 || pair.second < 0 ||
                            pair.first >= reference.Count ||
                            pair.second >= takes[takeIndex].Count) continue;
                        if (reference[pair.first].viseme ==
                            takes[takeIndex][pair.second].viseme)
                            support[pair.first]++;
                    }
                }

                var anchors = Enumerable.Range(0, support.Length)
                    .Where(index => support[index] >= requiredSupport)
                    .ToArray();
                var supportSum = anchors.Sum(index => support[index]);
                if (anchors.Length < bestAnchors.Length ||
                    anchors.Length == bestAnchors.Length &&
                    supportSum < bestSupport ||
                    anchors.Length == bestAnchors.Length &&
                    supportSum == bestSupport &&
                    totalCost > bestCost + 0.000001f) continue;
                bestReference = candidate;
                bestAnchors = anchors;
                bestSupport = supportSum;
                bestCost = totalCost;
            }

            if (bestReference < 0 || bestAnchors.Length < 2) return;
            var selectedReference = takes[bestReference];
            var firstAnchor = bestAnchors[0];
            var lastAnchor = bestAnchors[bestAnchors.Length - 1];
            var ranges = new (int start, int end)[takes.Count];
            var suffixSupport = 0;
            for (var takeIndex = 0; takeIndex < takes.Count; takeIndex++)
            {
                var alignment = VisemePhraseTraceMath.Align(
                    selectedReference, takes[takeIndex], options);
                var start = MappedTokenIndex(alignment, firstAnchor);
                var end = MappedTokenIndex(alignment, lastAnchor);
                if (start < 0 || end < start)
                {
                    // The anchor has 3-of-4 exact support, but a fourth take can
                    // still express it as an aligned alias. If alignment cannot
                    // locate that column at all, leave the take untouched rather
                    // than manufacturing a boundary.
                    ranges[takeIndex] = (0, takes[takeIndex].Count - 1);
                    continue;
                }
                ranges[takeIndex] = (start, end);
                if (end + 1 < takes[takeIndex].Count) suffixSupport++;
            }

            var retainSuffix = suffixSupport >= requiredSupport;
            for (var takeIndex = 0; takeIndex < takes.Count; takeIndex++)
            {
                var source = takes[takeIndex];
                var proposedEnd = ranges[takeIndex].end;
                if (retainSuffix && proposedEnd + 1 < source.Count) proposedEnd++;
                // This is boundary cleanup, never an editor-side shortcut for
                // deleting an inconsistent phrase interior. The capture service
                // can contribute one neighboring hard label at either edge; a
                // larger crop is evidence that these are genuinely different
                // sequences and must be handled by variants or a retake.
                if (ranges[takeIndex].start > 1 ||
                    source.Count - 1 - proposedEnd > 1) return;
            }
            var changed = false;
            for (var takeIndex = 0; takeIndex < takes.Count; takeIndex++)
            {
                var source = takes[takeIndex];
                var start = ranges[takeIndex].start;
                var end = ranges[takeIndex].end;
                if (retainSuffix && end + 1 < source.Count) end++;
                if (start == 0 && end == source.Count - 1) continue;
                takes[takeIndex] = source
                    .Skip(start)
                    .Take(end - start + 1)
                    .Select(CloneToken)
                    .ToList();
                changed = true;
            }

            if (!changed) return;
            messages.Add(new VisemePhraseDiagnostic(
                VisemePhraseDiagnosticSeverity.Info,
                "natural_boundary_normalized",
                "Natural-speech context outside the shared phrase was removed automatically."));
        }

        private static int MappedTokenIndex(
            VisemePhraseAlignment alignment,
            int referenceIndex)
        {
            if (alignment?.pairs == null) return -1;
            for (var i = 0; i < alignment.pairs.Count; i++)
            {
                var pair = alignment.pairs[i];
                if (pair.first == referenceIndex && pair.second >= 0)
                    return pair.second;
            }
            return -1;
        }

        private static int Medoid(IReadOnlyList<int> cluster, float[,] distances)
        {
            var best = cluster[0];
            var bestCost = float.PositiveInfinity;
            for (var candidateIndex = 0; candidateIndex < cluster.Count; candidateIndex++)
            {
                var candidate = cluster[candidateIndex];
                var cost = 0f;
                for (var other = 0; other < cluster.Count; other++)
                    cost += distances[candidate, cluster[other]];
                if (cost < bestCost - 1e-7f ||
                    Mathf.Abs(cost - bestCost) <= 1e-7f && candidate < best)
                {
                    best = candidate;
                    bestCost = cost;
                }
            }
            return best;
        }

        private static float ClusterCohesion(IReadOnlyList<int> cluster, float[,] distances)
        {
            if (cluster.Count < 2) return 1f;
            var sum = 0f;
            var pairs = 0;
            for (var first = 0; first < cluster.Count; first++)
                for (var second = first + 1; second < cluster.Count; second++)
                {
                    sum += distances[cluster[first], cluster[second]];
                    pairs++;
                }
            return Mathf.Clamp01(1f - sum / Mathf.Max(1, pairs));
        }

        private static float[,] PairwiseDistances(
            IReadOnlyList<List<VisemePhraseToken>> takes,
            VisemePhraseDistanceOptions options)
        {
            var distances = new float[takes.Count, takes.Count];
            for (var first = 0; first < takes.Count; first++)
                for (var second = first + 1; second < takes.Count; second++)
                {
                    var distance = VisemePhraseTraceMath.DtwDistance(
                        takes[first], takes[second], options);
                    distances[first, second] = distance;
                    distances[second, first] = distance;
                }
            return distances;
        }

        private static float MeanUpperTriangle(float[,] values)
        {
            var sum = 0f;
            var count = 0;
            for (var row = 0; row < values.GetLength(0); row++)
                for (var column = row + 1; column < values.GetLength(1); column++)
                {
                    sum += values[row, column];
                    count++;
                }
            return count == 0 ? 0f : sum / count;
        }

        private static float ComputeDistinctiveness(
            IReadOnlyList<VisemePhraseModelVariant> variants)
        {
            if (variants == null || variants.Count == 0) return 0f;
            var sum = 0f;
            foreach (var variant in variants)
            {
                var count = variant.states.Count;
                if (count == 0) continue;
                var uniqueVisemes = variant.states.Select(state => state.primaryViseme).Distinct().Count();
                var uniqueTransitions = 0;
                if (count > 1)
                    uniqueTransitions = variant.states.Zip(
                            variant.states.Skip(1),
                            (first, second) => first.primaryViseme + ">" + second.primaryViseme)
                        .Distinct().Count();
                var lengthScore = Mathf.InverseLerp(3f, 8f, count);
                var visemeScore = uniqueVisemes / (float)Mathf.Max(1, Mathf.Min(6, count));
                var transitionScore = count <= 1 ? 0f : uniqueTransitions / (float)(count - 1);
                sum += Mathf.Clamp01(0.45f * lengthScore +
                                     0.25f * visemeScore +
                                     0.3f * transitionScore);
            }
            return Mathf.Clamp01(sum / variants.Count);
        }

        private static string Fingerprint(VisemePhraseCompiledModel model)
        {
            var builder = new StringBuilder();
            builder.Append(model.modelSchemaVersion).Append('|')
                .Append(model.phraseId).Append('|')
                .Append(model.promptFingerprint).Append('|')
                .Append((int)model.contextMode).Append('|')
                .Append(model.strictness.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(model.acceptanceCost.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(model.minimumNegativeMargin.ToString("R", CultureInfo.InvariantCulture));
            foreach (var variant in model.variants)
            {
                builder.Append("|V:").Append(variant.id).Append(':')
                    .Append(variant.sourceTakeIndex).Append(':')
                    .Append(variant.inferredContextPath ? 'I' : 'E').Append(':')
                    .Append(variant.inferredConfusionPath ? 'C' : 'N').Append(':')
                    .Append(variant.confusionSourceSequence ?? string.Empty).Append(':')
                    .Append(variant.confusionSourceVariantId ?? string.Empty).Append(':')
                    .Append(variant.inferencePenalty.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(variant.cohesion.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(variant.medianDurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(variant.minimumDurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(variant.maximumDurationSeconds.ToString("R", CultureInfo.InvariantCulture));
                foreach (var state in variant.states)
                {
                    builder.Append("|S:").Append(state.primaryViseme).Append(':')
                        .Append(state.medianDurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                        .Append(state.minimumDurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                        .Append(state.maximumDurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                        .Append(state.meanVoice.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                        .Append(state.allowSkip ? '1' : '0').Append(':')
                        .Append(state.skipPenalty.ToString("R", CultureInfo.InvariantCulture));
                    if (state.aliasVisemes == null) continue;
                    for (var i = 0; i < state.aliasVisemes.Length; i++)
                    {
                        builder.Append(':').Append(state.aliasVisemes[i]).Append('@')
                            .Append((state.aliasLikelihoods != null &&
                                     i < state.aliasLikelihoods.Length
                                    ? state.aliasLikelihoods[i]
                                    : 0f).ToString("R", CultureInfo.InvariantCulture));
                    }
                }
                foreach (var rectangle in variant.runtimeTimingRectangles ??
                                          new List<VisemePhraseRuntimeTimingRectangle>())
                {
                    builder.Append("|R:").Append(rectangle.sourceTakeIndex).Append(':')
                        .Append(rectangle.skippedStateIndex).Append(':')
                        .Append(rectangle.inferredProfile ? 'P' : 'E').Append(':')
                        .Append(rectangle.includesRuntimeObservationUncertainty
                            ? 'U'
                            : 'N');
                    var minimums = rectangle.minimumDurationSeconds ?? Array.Empty<float>();
                    var maximums = rectangle.maximumDurationSeconds ?? Array.Empty<float>();
                    var count = Mathf.Max(minimums.Length, maximums.Length);
                    for (var index = 0; index < count; index++)
                    {
                        builder.Append(':')
                            .Append(index < minimums.Length
                                ? minimums[index].ToString("R", CultureInfo.InvariantCulture)
                                : "missing")
                            .Append("..")
                            .Append(index < maximums.Length
                                ? maximums[index].ToString("R", CultureInfo.InvariantCulture)
                                : "missing");
                    }
                }
            }
            return AdvancedVisemeParameterContract.StableFingerprint(builder.ToString());
        }

        private static bool HasErrors(IEnumerable<VisemePhraseDiagnostic> messages)
        {
            return messages.Any(message =>
                message != null && message.severity == VisemePhraseDiagnosticSeverity.Error);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private sealed class StateObservation
        {
            internal int viseme;
            internal float duration;
            internal float voice;
            internal float alignmentConfidence;
        }

        private sealed class AliasSupport
        {
            internal int viseme;
            internal int count;
            internal float support;
        }

        private readonly struct ConfusionAlternative
        {
            internal readonly int viseme;
            internal readonly float likelihood;

            internal ConfusionAlternative(int viseme, float likelihood)
            {
                this.viseme = viseme;
                this.likelihood = likelihood;
            }
        }

        private sealed class ConfusionPathCandidate
        {
            internal VisemePhraseModelVariant source;
            internal string sourceSequence;
            internal int[] sequence;
            internal string sequenceKey;
            internal int stateIndex;
            internal float likelihood;
            internal float penalty;
            internal int weaknessRank;
        }

        private sealed class ClusterCandidate
        {
            internal List<List<int>> clusters;
            internal float cost;
            internal float improvement;
        }

        private sealed class ContextPathCandidate
        {
            internal string key;
            internal int[] sequence;
            internal List<VisemePhraseToken> seed;
            internal int closestTakeIndex;
            internal float distance;
            internal int boundaryOverlap;
            internal int backoffCount;
            internal int spliceSupport;
            internal float boundaryEvidenceSeconds;
        }

        private sealed class EnumeratedContextPath
        {
            internal int[] sequence;
            internal int backoffCount;
        }

        private sealed class SplicePathAggregate
        {
            internal string key;
            internal int[] sequence;
            internal readonly HashSet<string> sourcePairs =
                new HashSet<string>(StringComparer.Ordinal);
            internal readonly List<List<VisemePhraseToken>> seeds =
                new List<List<VisemePhraseToken>>();
        }

        private sealed class CalibrationStats
        {
            internal float worstPositive;
            internal float bestNegative = 1f;
            internal float separation;
            internal int negativeCount;
        }

        private sealed class OptionalFeature
        {
            private readonly VisemePhraseModelState state;
            private readonly bool skip;
            private readonly int aliasViseme;
            private float previousLikelihood;
            private bool previousSkip;

            private OptionalFeature(
                VisemePhraseModelState state,
                bool skip,
                int aliasViseme)
            {
                this.state = state;
                this.skip = skip;
                this.aliasViseme = aliasViseme;
            }

            internal static OptionalFeature ForSkip(VisemePhraseModelState state)
            {
                return new OptionalFeature(state, true, -1);
            }

            internal static OptionalFeature ForAlias(
                VisemePhraseModelState state,
                int aliasViseme)
            {
                return new OptionalFeature(state, false, aliasViseme);
            }

            internal void DisableTemporarily()
            {
                if (skip)
                {
                    previousSkip = state.allowSkip;
                    state.allowSkip = false;
                    return;
                }
                previousLikelihood = state.emissionLikelihoods != null &&
                                     aliasViseme >= 0 &&
                                     aliasViseme < state.emissionLikelihoods.Length
                    ? state.emissionLikelihoods[aliasViseme]
                    : 0f;
                if (state.emissionLikelihoods != null &&
                    aliasViseme >= 0 && aliasViseme < state.emissionLikelihoods.Length)
                    state.emissionLikelihoods[aliasViseme] = 0f;
            }

            internal void Restore()
            {
                if (skip)
                {
                    state.allowSkip = previousSkip;
                    return;
                }
                if (state.emissionLikelihoods != null &&
                    aliasViseme >= 0 && aliasViseme < state.emissionLikelihoods.Length)
                    state.emissionLikelihoods[aliasViseme] = previousLikelihood;
            }

            internal void PrunePermanently()
            {
                if (skip)
                {
                    state.allowSkip = false;
                    return;
                }
                if (state.emissionLikelihoods != null &&
                    aliasViseme >= 0 && aliasViseme < state.emissionLikelihoods.Length)
                    state.emissionLikelihoods[aliasViseme] = 0f;
                var aliases = state.aliasVisemes ?? Array.Empty<int>();
                var likelihoods = state.aliasLikelihoods ?? Array.Empty<float>();
                var keptAliases = new List<int>();
                var keptLikelihoods = new List<float>();
                for (var i = 0; i < aliases.Length; i++)
                {
                    if (aliases[i] == aliasViseme) continue;
                    keptAliases.Add(aliases[i]);
                    keptLikelihoods.Add(i < likelihoods.Length ? likelihoods[i] : 0f);
                }
                state.aliasVisemes = keptAliases.ToArray();
                state.aliasLikelihoods = keptLikelihoods.ToArray();
            }
        }
    }

    /// <summary>
    /// The exact finite language that can be baked into an avatar Animator.
    /// Authoring DTW is deliberately absent: one RLE run consumes one state,
    /// with only the single explicitly learned deletion permitted. Timing uses
    /// positive-seeded safe rectangles so every represented combination also
    /// satisfies the learned whole-phrase duration envelope.
    /// </summary>
    internal static class VisemePhraseRuntimeLanguage
    {
        internal const float NoMatchCost = 1f;
        internal const float RuntimeObservationUncertaintySeconds = 0.03f;
        private const float CostEpsilon = 0.00001f;
        private const float TimingEpsilon = 0.0001f;

        private sealed class SeedCandidate
        {
            internal int variantIndex;
            internal int skippedStateIndex;
            internal float cost;
        }

        internal static float Score(
            VisemePhraseCompiledModel model,
            IReadOnlyList<VisemePhraseToken> tokens,
            bool subsequence)
        {
            if (model?.variants == null || model.variants.Count == 0 ||
                tokens == null || tokens.Count == 0)
                return NoMatchCost;
            var best = NoMatchCost;
            foreach (var variant in model.variants)
            {
                if (variant?.states == null || variant.states.Count == 0 ||
                    variant.runtimeTimingRectangles == null) continue;
                foreach (var rectangle in variant.runtimeTimingRectangles)
                {
                    if (!IsUsableRectangle(variant, rectangle)) continue;
                    var consumed = variant.states.Count -
                                   (rectangle.skippedStateIndex >= 0 ? 1 : 0);
                    if (consumed <= 0 || consumed > tokens.Count) continue;
                    if (!subsequence && consumed != tokens.Count) continue;
                    var lastStart = subsequence ? tokens.Count - consumed : 0;
                    for (var start = 0; start <= lastStart; start++)
                    {
                        best = Mathf.Min(best, PathCost(
                            variant, rectangle, tokens, start));
                        if (best <= 0f) return 0f;
                    }
                }
            }
            return Mathf.Clamp01(best);
        }

        internal static float EmissionLikelihood(
            VisemePhraseModelState state,
            int viseme)
        {
            if (state == null || viseme < 0 || viseme >= 15) return 0f;
            if (viseme == state.primaryViseme)
            {
                var primary = state.emissionLikelihoods != null &&
                              viseme < state.emissionLikelihoods.Length
                    ? Mathf.Clamp01(state.emissionLikelihoods[viseme])
                    : 1f;
                return primary > 0f ? primary : 1f;
            }

            var aliases = state.aliasVisemes ?? Array.Empty<int>();
            var aliasIndex = Array.IndexOf(aliases, viseme);
            if (aliasIndex < 0) return 0f;
            if (state.emissionLikelihoods != null &&
                viseme < state.emissionLikelihoods.Length)
                return Mathf.Clamp01(state.emissionLikelihoods[viseme]);
            return state.aliasLikelihoods != null &&
                   aliasIndex < state.aliasLikelihoods.Length
                ? Mathf.Clamp01(state.aliasLikelihoods[aliasIndex])
                : 0f;
        }

        internal static float RuntimeMinimumTotalSeconds(
            VisemePhraseModelVariant variant)
        {
            if (variant == null) return 0f;
            return Mathf.Max(
                0f,
                variant.minimumDurationSeconds -
                RuntimeObservationUncertaintySeconds);
        }

        internal static float RuntimeMaximumTotalSeconds(
            VisemePhraseModelVariant variant)
        {
            if (variant == null) return 0f;
            return variant.maximumDurationSeconds +
                   RuntimeObservationUncertaintySeconds;
        }

        /// <summary>
        /// Resolves the exact alias sets a hard-Viseme Animator path can use.
        /// Adjacent states may not share an observed value: VRChat would keep
        /// that value as one RLE run, so the matcher could not observe the
        /// boundary. The more strongly supported state owns an overlapping
        /// alias, while authored primaries always take precedence. This is
        /// path-specific because a learned skip creates a new adjacency.
        /// </summary>
        internal static bool TryGetPathAliases(
            VisemePhraseModelVariant variant,
            int skippedStateIndex,
            out int[] retainedStateIndices,
            out int[][] pathAliases,
            out string error)
        {
            retainedStateIndices = Array.Empty<int>();
            pathAliases = Array.Empty<int[]>();
            error = null;
            if (variant?.states == null || variant.states.Count == 0)
            {
                error = "contains no runtime states.";
                return false;
            }
            if (skippedStateIndex < -1 || skippedStateIndex >= variant.states.Count ||
                skippedStateIndex >= 0 &&
                (variant.states[skippedStateIndex] == null ||
                 !variant.states[skippedStateIndex].allowSkip))
            {
                error = "contains an invalid learned-skip path.";
                return false;
            }

            retainedStateIndices = Enumerable.Range(0, variant.states.Count)
                .Where(index => index != skippedStateIndex)
                .ToArray();
            var aliases = new HashSet<int>[retainedStateIndices.Length];
            for (var pathIndex = 0; pathIndex < retainedStateIndices.Length; pathIndex++)
            {
                var state = variant.states[retainedStateIndices[pathIndex]];
                if (state == null || state.primaryViseme < 0 || state.primaryViseme >= 15)
                {
                    error = "contains a missing state or invalid primary Viseme.";
                    return false;
                }
                aliases[pathIndex] = new HashSet<int> { state.primaryViseme };
                foreach (var alias in state.aliasVisemes ?? Array.Empty<int>())
                    if (alias >= 0 && alias < 15 &&
                        EmissionLikelihood(state, alias) > 0f)
                        aliases[pathIndex].Add(alias);
            }

            for (var pathIndex = 0; pathIndex + 1 < aliases.Length; pathIndex++)
            {
                var leftState = variant.states[retainedStateIndices[pathIndex]];
                var rightState = variant.states[retainedStateIndices[pathIndex + 1]];
                var left = aliases[pathIndex];
                var right = aliases[pathIndex + 1];
                foreach (var alias in left.Intersect(right).OrderBy(value => value).ToArray())
                {
                    if (alias == leftState.primaryViseme &&
                        alias == rightState.primaryViseme)
                    {
                        error = "contains identical adjacent primary Visemes " + alias + ".";
                        return false;
                    }
                    if (alias == leftState.primaryViseme)
                    {
                        right.Remove(alias);
                        continue;
                    }
                    if (alias == rightState.primaryViseme)
                    {
                        left.Remove(alias);
                        continue;
                    }
                    if (EmissionLikelihood(leftState, alias) + CostEpsilon >=
                        EmissionLikelihood(rightState, alias))
                        right.Remove(alias);
                    else
                        left.Remove(alias);
                }
                left.Add(leftState.primaryViseme);
                right.Add(rightState.primaryViseme);
                if (!left.Overlaps(right)) continue;
                error = "has unresolved adjacent Viseme aliases between states " +
                        retainedStateIndices[pathIndex] + " and " +
                        retainedStateIndices[pathIndex + 1] + ".";
                return false;
            }

            pathAliases = aliases
                .Select(set => set.OrderBy(value => value).ToArray())
                .ToArray();
            return true;
        }

        internal static void BuildPositiveTimingRectangles(
            VisemePhraseCompiledModel model,
            IReadOnlyList<List<VisemePhraseToken>> positiveTakes,
            ICollection<VisemePhraseDiagnostic> messages)
        {
            if (model?.variants == null) return;
            // Context/profile paths are synthesized from several takes and do
            // not necessarily equal any one enrollment trace. Candidate
            // confusion evaluation rebuilds timing repeatedly; retain these
            // already-validated correlated rectangles so a generic guess can
            // never erase the personalized pronunciation language.
            var retainedContextRectangles = model.variants
                .Where(variant => variant != null &&
                                  variant.inferredContextPath &&
                                  !variant.inferredConfusionPath)
                .ToDictionary(
                    variant => variant,
                    variant => (variant.runtimeTimingRectangles ??
                                new List<VisemePhraseRuntimeTimingRectangle>())
                        .Select(CloneRectangle)
                        .ToList());
            foreach (var variant in model.variants)
            {
                if (variant == null) continue;
                if (variant.runtimeTimingRectangles == null)
                    variant.runtimeTimingRectangles =
                        new List<VisemePhraseRuntimeTimingRectangle>();
                else
                    variant.runtimeTimingRectangles.Clear();
            }
            if (positiveTakes == null) return;

            for (var takeIndex = 0; takeIndex < positiveTakes.Count; takeIndex++)
            {
                var take = positiveTakes[takeIndex];
                var candidate = BestSeedCandidate(model, take);
                if (candidate == null) continue;
                var variant = model.variants[candidate.variantIndex];
                if (!TryCreateSafeRectangle(
                        variant,
                        candidate.skippedStateIndex,
                        take,
                        takeIndex,
                        out var rectangle))
                    continue;
                AddIfNotContained(variant.runtimeTimingRectangles, rectangle);
            }

            // A component-wise log-median profile is the small-sample analogue
            // of a DTW barycenter. It admits a natural cadence between two
            // enrolled takes without taking the unsafe Cartesian union of every
            // state's independent bounds.
            foreach (var variant in model.variants.Where(item =>
                         item != null && !item.inferredConfusionPath))
                AddProfileTimingRectangles(variant, positiveTakes);

            foreach (var pair in retainedContextRectangles)
            foreach (var rectangle in pair.Value)
                AddIfNotContained(
                    pair.Key.runtimeTimingRectangles,
                    CloneRectangle(rectangle));

            // A weighted confusion path changes identity, not timing. Reuse the
            // complete correlated timing language of its enrolled source rather
            // than attempting to seed it with a pronunciation nobody recorded.
            foreach (var inferred in model.variants.Where(item =>
                         item != null && item.inferredConfusionPath))
            {
                var source = model.variants.FirstOrDefault(item =>
                    item != null && !item.inferredConfusionPath &&
                    string.Equals(
                        item.id,
                        inferred.confusionSourceVariantId,
                        StringComparison.Ordinal));
                if (source?.runtimeTimingRectangles == null) continue;
                inferred.runtimeTimingRectangles = source.runtimeTimingRectangles
                    .Select(CloneRectangle)
                    .ToList();
            }

            foreach (var variant in model.variants.Where(item => item != null))
            foreach (var rectangle in variant.runtimeTimingRectangles)
                ApplyRuntimeObservationUncertainty(variant, rectangle);

            foreach (var variant in model.variants.Where(item => item != null))
                variant.runtimeTimingRectangles = variant.runtimeTimingRectangles
                    .OrderBy(rectangle => rectangle.skippedStateIndex)
                    .ThenBy(rectangle => RectangleKey(rectangle), StringComparer.Ordinal)
                    .ToList();
        }

        private static void AddProfileTimingRectangles(
            VisemePhraseModelVariant variant,
            IReadOnlyList<List<VisemePhraseToken>> positiveTakes)
        {
            if (variant?.states == null || variant.runtimeTimingRectangles == null ||
                positiveTakes == null) return;
            var groups = variant.runtimeTimingRectangles
                .Where(rectangle => rectangle != null &&
                                    rectangle.sourceTakeIndex >= 0)
                .GroupBy(rectangle => rectangle.skippedStateIndex)
                .ToArray();
            foreach (var group in groups)
            {
                if (group.Count() < 2 || !TryGetPathAliases(
                        variant,
                        group.Key,
                        out var retainedStateIndices,
                        out _,
                        out _))
                    continue;
                var observations = retainedStateIndices.ToDictionary(
                    index => index,
                    _ => new List<float>());
                var totalDurations = new List<float>();
                foreach (var rectangle in group)
                {
                    var takeIndex = rectangle.sourceTakeIndex;
                    if (takeIndex < 0 || takeIndex >= positiveTakes.Count) continue;
                    var take = positiveTakes[takeIndex];
                    if (take == null || take.Count != retainedStateIndices.Length)
                        continue;
                    totalDurations.Add(take.Sum(token => token.DurationSeconds));
                    for (var pathIndex = 0;
                         pathIndex < retainedStateIndices.Length;
                         pathIndex++)
                        observations[retainedStateIndices[pathIndex]].Add(
                            take[pathIndex].DurationSeconds);
                }
                if (observations.Values.Any(values => values.Count < 2)) continue;

                var profileDurations = retainedStateIndices.Select(stateIndex =>
                {
                    var logs = observations[stateIndex]
                        .Where(value => Finite(value) && value > 0f)
                        .Select(value => Mathf.Log(Mathf.Max(0.0001f, value)))
                        .ToArray();
                    return logs.Length < 2
                        ? float.NaN
                        : Mathf.Exp(VisemePhraseTraceMath.Median(logs));
                }).ToArray();
                if (profileDurations.Any(value => !Finite(value) || value <= 0f) ||
                    totalDurations.Count < 2)
                    continue;
                var targetTotal = VisemePhraseTraceMath.Median(totalDurations);
                var profileTotal = profileDurations.Sum();
                var scale = targetTotal / Mathf.Max(0.0001f, profileTotal);
                var seed = new List<VisemePhraseToken>(retainedStateIndices.Length);
                var clock = 0f;
                for (var pathIndex = 0;
                     pathIndex < retainedStateIndices.Length;
                     pathIndex++)
                {
                    var stateIndex = retainedStateIndices[pathIndex];
                    var duration = profileDurations[pathIndex] * scale;
                    seed.Add(new VisemePhraseToken
                    {
                        viseme = variant.states[stateIndex].primaryViseme,
                        startSeconds = clock,
                        endSeconds = clock + duration,
                        meanVoice = variant.states[stateIndex].meanVoice,
                        frameCount = 1
                    });
                    clock += duration;
                }
                if (!TryCreateSafeRectangle(
                        variant, group.Key, seed, -1, out var profile))
                    continue;
                profile.inferredProfile = true;
                AddIfNotContained(variant.runtimeTimingRectangles, profile);
            }
        }

        private static VisemePhraseRuntimeTimingRectangle CloneRectangle(
            VisemePhraseRuntimeTimingRectangle source) =>
            new VisemePhraseRuntimeTimingRectangle
            {
                sourceTakeIndex = source?.sourceTakeIndex ?? -1,
                skippedStateIndex = source?.skippedStateIndex ?? -1,
                inferredProfile = source != null && source.inferredProfile,
                includesRuntimeObservationUncertainty = source != null &&
                    source.includesRuntimeObservationUncertainty,
                minimumDurationSeconds = source?.minimumDurationSeconds?.ToArray() ??
                                         Array.Empty<float>(),
                maximumDurationSeconds = source?.maximumDurationSeconds?.ToArray() ??
                                         Array.Empty<float>()
            };

        private static void ApplyRuntimeObservationUncertainty(
            VisemePhraseModelVariant variant,
            VisemePhraseRuntimeTimingRectangle rectangle)
        {
            if (rectangle == null ||
                rectangle.includesRuntimeObservationUncertainty ||
                !TryGetPathAliases(
                    variant,
                    rectangle.skippedStateIndex,
                    out var retainedStateIndices,
                    out _,
                    out _))
                return;
            var allowance = RuntimeObservationUncertaintySeconds /
                Mathf.Max(1, retainedStateIndices.Length);
            foreach (var stateIndex in retainedStateIndices)
            {
                rectangle.minimumDurationSeconds[stateIndex] = Mathf.Max(
                    0f,
                    rectangle.minimumDurationSeconds[stateIndex] - allowance);
                rectangle.maximumDurationSeconds[stateIndex] += allowance;
            }
            rectangle.includesRuntimeObservationUncertainty = true;
        }

        private static SeedCandidate BestSeedCandidate(
            VisemePhraseCompiledModel model,
            IReadOnlyList<VisemePhraseToken> tokens)
        {
            if (tokens == null || tokens.Count == 0) return null;
            SeedCandidate best = null;
            for (var variantIndex = 0; variantIndex < model.variants.Count; variantIndex++)
            {
                var variant = model.variants[variantIndex];
                if (variant?.states == null || variant.states.Count == 0) continue;
                foreach (var skipped in CandidateSkips(variant, tokens.Count))
                {
                    var cost = SeedPathCost(variant, tokens, skipped);
                    if (cost >= NoMatchCost - CostEpsilon) continue;
                    if (best != null &&
                        (cost > best.cost + CostEpsilon ||
                         Mathf.Abs(cost - best.cost) <= CostEpsilon &&
                         (variantIndex > best.variantIndex ||
                          variantIndex == best.variantIndex &&
                          skipped > best.skippedStateIndex)))
                        continue;
                    best = new SeedCandidate
                    {
                        variantIndex = variantIndex,
                        skippedStateIndex = skipped,
                        cost = cost
                    };
                }
            }
            return best;
        }

        private static IEnumerable<int> CandidateSkips(
            VisemePhraseModelVariant variant,
            int tokenCount)
        {
            if (tokenCount == variant.states.Count)
            {
                yield return -1;
                yield break;
            }
            if (tokenCount != variant.states.Count - 1) yield break;
            for (var index = 0; index < variant.states.Count; index++)
                if (variant.states[index] != null && variant.states[index].allowSkip)
                    yield return index;
        }

        private static float SeedPathCost(
            VisemePhraseModelVariant variant,
            IReadOnlyList<VisemePhraseToken> tokens,
            int skippedStateIndex)
        {
            if (!ValidVariantEnvelope(variant)) return NoMatchCost;
            if (!TryGetPathAliases(
                    variant,
                    skippedStateIndex,
                    out var retainedStateIndices,
                    out var pathAliases,
                    out _)) return NoMatchCost;
            if (tokens.Count != retainedStateIndices.Length) return NoMatchCost;
            var total = tokens.Sum(token => token?.DurationSeconds ?? 0f);
            if (!Within(total, variant.minimumDurationSeconds,
                    variant.maximumDurationSeconds)) return NoMatchCost;
            var totalCost = Mathf.Max(0f, variant.inferencePenalty);
            if (skippedStateIndex >= 0)
                totalCost += Mathf.Clamp01(
                    variant.states[skippedStateIndex].skipPenalty);
            for (var pathIndex = 0;
                 pathIndex < retainedStateIndices.Length;
                 pathIndex++)
            {
                var stateIndex = retainedStateIndices[pathIndex];
                var state = variant.states[stateIndex];
                var token = tokens[pathIndex];
                if (token == null || !Within(
                        token.DurationSeconds,
                        state.minimumDurationSeconds,
                        state.maximumDurationSeconds)) return NoMatchCost;
                if (Array.IndexOf(pathAliases[pathIndex], token.viseme) < 0)
                    return NoMatchCost;
                var likelihood = EmissionLikelihood(state, token.viseme);
                if (likelihood <= 0f) return NoMatchCost;
                totalCost += 1f - likelihood;
            }
            return Mathf.Clamp01(totalCost / Mathf.Max(1, variant.states.Count));
        }

        internal static bool TryCreateSafeRectangle(
            VisemePhraseModelVariant variant,
            int skippedStateIndex,
            IReadOnlyList<VisemePhraseToken> seed,
            int sourceTakeIndex,
            out VisemePhraseRuntimeTimingRectangle rectangle)
        {
            rectangle = null;
            if (SeedPathCost(variant, seed, skippedStateIndex) >=
                NoMatchCost - CostEpsilon) return false;
            var count = variant.states.Count;
            var minimums = new float[count];
            var maximums = new float[count];
            var seedDurations = new float[count];
            var tokenIndex = 0;
            for (var stateIndex = 0; stateIndex < count; stateIndex++)
            {
                var state = variant.states[stateIndex];
                if (state == null || !Finite(state.minimumDurationSeconds) ||
                    !Finite(state.maximumDurationSeconds) ||
                    state.maximumDurationSeconds < state.minimumDurationSeconds)
                    return false;
                minimums[stateIndex] = state.minimumDurationSeconds;
                maximums[stateIndex] = state.maximumDurationSeconds;
                if (stateIndex == skippedStateIndex) continue;
                if (tokenIndex >= seed.Count) return false;
                seedDurations[stateIndex] = seed[tokenIndex++].DurationSeconds;
            }
            if (tokenIndex != seed.Count) return false;

            var active = Enumerable.Range(0, count)
                .Where(index => index != skippedStateIndex).ToArray();
            var minimumSum = active.Sum(index => minimums[index]);
            var needMinimum = Mathf.Max(0f,
                variant.minimumDurationSeconds - minimumSum);
            for (var cursor = active.Length - 1;
                 cursor >= 0 && needMinimum > TimingEpsilon;
                 cursor--)
            {
                var index = active[cursor];
                var capacity = Mathf.Max(0f, seedDurations[index] - minimums[index]);
                var adjustment = Mathf.Min(needMinimum, capacity);
                minimums[index] += adjustment;
                needMinimum -= adjustment;
            }
            if (needMinimum > TimingEpsilon) return false;

            var maximumSum = active.Sum(index => maximums[index]);
            var excessMaximum = Mathf.Max(0f,
                maximumSum - variant.maximumDurationSeconds);
            for (var cursor = active.Length - 1;
                 cursor >= 0 && excessMaximum > TimingEpsilon;
                 cursor--)
            {
                var index = active[cursor];
                var capacity = Mathf.Max(0f, maximums[index] - seedDurations[index]);
                var adjustment = Mathf.Min(excessMaximum, capacity);
                maximums[index] -= adjustment;
                excessMaximum -= adjustment;
            }
            if (excessMaximum > TimingEpsilon) return false;

            rectangle = new VisemePhraseRuntimeTimingRectangle
            {
                sourceTakeIndex = sourceTakeIndex,
                skippedStateIndex = skippedStateIndex,
                minimumDurationSeconds = minimums,
                maximumDurationSeconds = maximums
            };
            return IsUsableRectangle(variant, rectangle);
        }

        private static float PathCost(
            VisemePhraseModelVariant variant,
            VisemePhraseRuntimeTimingRectangle rectangle,
            IReadOnlyList<VisemePhraseToken> tokens,
            int start)
        {
            if (!TryGetPathAliases(
                    variant,
                    rectangle.skippedStateIndex,
                    out var retainedStateIndices,
                    out var pathAliases,
                    out _)) return NoMatchCost;
            var consumed = retainedStateIndices.Length;
            if (start < 0 || start + consumed > tokens.Count) return NoMatchCost;
            var totalDuration = 0f;
            for (var index = start; index < start + consumed; index++)
                totalDuration += tokens[index]?.DurationSeconds ?? 0f;
            var minimumTotal = rectangle.includesRuntimeObservationUncertainty
                ? RuntimeMinimumTotalSeconds(variant)
                : variant.minimumDurationSeconds;
            var maximumTotal = rectangle.includesRuntimeObservationUncertainty
                ? RuntimeMaximumTotalSeconds(variant)
                : variant.maximumDurationSeconds;
            if (!Within(
                    totalDuration,
                    minimumTotal,
                    maximumTotal)) return NoMatchCost;

            var totalCost = Mathf.Max(0f, variant.inferencePenalty);
            if (rectangle.skippedStateIndex >= 0)
                totalCost += Mathf.Clamp01(
                    variant.states[rectangle.skippedStateIndex].skipPenalty);
            for (var pathIndex = 0;
                 pathIndex < retainedStateIndices.Length;
                 pathIndex++)
            {
                var stateIndex = retainedStateIndices[pathIndex];
                var state = variant.states[stateIndex];
                var token = tokens[start + pathIndex];
                if (state == null || token == null || !Within(
                        token.DurationSeconds,
                        rectangle.minimumDurationSeconds[stateIndex],
                        rectangle.maximumDurationSeconds[stateIndex]))
                    return NoMatchCost;
                if (Array.IndexOf(pathAliases[pathIndex], token.viseme) < 0)
                    return NoMatchCost;
                var likelihood = EmissionLikelihood(state, token.viseme);
                if (likelihood <= 0f) return NoMatchCost;
                totalCost += 1f - likelihood;
            }
            return Mathf.Clamp01(totalCost / Mathf.Max(1, variant.states.Count));
        }

        private static bool IsUsableRectangle(
            VisemePhraseModelVariant variant,
            VisemePhraseRuntimeTimingRectangle rectangle)
        {
            if (variant?.states == null || rectangle == null ||
                rectangle.minimumDurationSeconds == null ||
                rectangle.maximumDurationSeconds == null ||
                rectangle.minimumDurationSeconds.Length != variant.states.Count ||
                rectangle.maximumDurationSeconds.Length != variant.states.Count)
                return false;
            if (rectangle.skippedStateIndex < -1 ||
                rectangle.skippedStateIndex >= variant.states.Count ||
                rectangle.skippedStateIndex >= 0 &&
                (variant.states[rectangle.skippedStateIndex] == null ||
                 !variant.states[rectangle.skippedStateIndex].allowSkip))
                return false;
            var minimumSum = 0f;
            var maximumSum = 0f;
            for (var index = 0; index < variant.states.Count; index++)
            {
                if (index == rectangle.skippedStateIndex) continue;
                var minimum = rectangle.minimumDurationSeconds[index];
                var maximum = rectangle.maximumDurationSeconds[index];
                if (!Finite(minimum) || !Finite(maximum) || maximum < minimum)
                    return false;
                minimumSum += minimum;
                maximumSum += maximum;
            }
            var uncertainty = rectangle.includesRuntimeObservationUncertainty
                ? RuntimeObservationUncertaintySeconds
                : 0f;
            return minimumSum + TimingEpsilon >=
                       variant.minimumDurationSeconds - uncertainty &&
                   maximumSum - TimingEpsilon <=
                       variant.maximumDurationSeconds + uncertainty;
        }

        private static bool ValidVariantEnvelope(VisemePhraseModelVariant variant) =>
            variant?.states != null && variant.states.Count > 0 &&
            Finite(variant.minimumDurationSeconds) &&
            Finite(variant.maximumDurationSeconds) &&
            variant.maximumDurationSeconds >= variant.minimumDurationSeconds;

        private static bool Within(float value, float minimum, float maximum)
        {
            if (!Finite(value) || !Finite(minimum) || !Finite(maximum) ||
                value < 0f || maximum < minimum) return false;
            var tolerance = Mathf.Max(TimingEpsilon,
                Mathf.Max(Mathf.Abs(minimum), Mathf.Abs(maximum)) * 1e-5f);
            return value + tolerance >= minimum && value - tolerance <= maximum;
        }

        internal static void AddIfNotContained(
            IList<VisemePhraseRuntimeTimingRectangle> rectangles,
            VisemePhraseRuntimeTimingRectangle candidate)
        {
            for (var index = rectangles.Count - 1; index >= 0; index--)
            {
                var existing = rectangles[index];
                if (existing.skippedStateIndex != candidate.skippedStateIndex) continue;
                if (Contains(existing, candidate))
                {
                    // A synthesized profile may cover a narrower observed take,
                    // but it must never erase that take's protected provenance.
                    if (!candidate.inferredProfile && existing.inferredProfile)
                        continue;
                    return;
                }
                if (Contains(candidate, existing))
                {
                    if (candidate.inferredProfile && !existing.inferredProfile)
                        continue;
                    rectangles.RemoveAt(index);
                }
            }
            rectangles.Add(candidate);
        }

        private static bool Contains(
            VisemePhraseRuntimeTimingRectangle outer,
            VisemePhraseRuntimeTimingRectangle inner)
        {
            if (outer.minimumDurationSeconds.Length != inner.minimumDurationSeconds.Length ||
                outer.maximumDurationSeconds.Length != inner.maximumDurationSeconds.Length)
                return false;
            for (var index = 0; index < outer.minimumDurationSeconds.Length; index++)
            {
                if (outer.minimumDurationSeconds[index] >
                    inner.minimumDurationSeconds[index] + TimingEpsilon) return false;
                if (outer.maximumDurationSeconds[index] <
                    inner.maximumDurationSeconds[index] - TimingEpsilon) return false;
            }
            return true;
        }

        private static string RectangleKey(VisemePhraseRuntimeTimingRectangle rectangle)
        {
            var builder = new StringBuilder(
                rectangle.includesRuntimeObservationUncertainty ? "U|" : "N|");
            for (var index = 0; index < rectangle.minimumDurationSeconds.Length; index++)
            {
                builder.Append(rectangle.minimumDurationSeconds[index]
                        .ToString("R", CultureInfo.InvariantCulture))
                    .Append("..")
                    .Append(rectangle.maximumDurationSeconds[index]
                        .ToString("R", CultureInfo.InvariantCulture))
                    .Append('|');
            }
            return builder.ToString();
        }

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
