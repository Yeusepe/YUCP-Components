using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components
{
    public enum VisemePhraseDiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    [Serializable]
    public sealed class VisemePhraseDiagnostic
    {
        public VisemePhraseDiagnosticSeverity severity;
        public string code = string.Empty;
        public string message = string.Empty;

        public VisemePhraseDiagnostic()
        {
        }

        public VisemePhraseDiagnostic(
            VisemePhraseDiagnosticSeverity severity,
            string code,
            string message)
        {
            this.severity = severity;
            this.code = code ?? string.Empty;
            this.message = message ?? string.Empty;
        }
    }

    [Serializable]
    public sealed class VisemePhraseTraceFrame
    {
        [Tooltip("Sample offset from the beginning of this take.")]
        public long sampleClock;
        [Range(0, 14)] public int viseme;
        [Range(0f, 1f)] public float voice;
    }

    [Serializable]
    public sealed class VisemePhraseEnrollmentTrace
    {
        public const int CurrentTraceSchemaVersion = 1;

        public int traceSchemaVersion = CurrentTraceSchemaVersion;
        public string takeId = string.Empty;
        public string backend = string.Empty;
        public long recordedUtcTicks = DateTime.UtcNow.Ticks;
        public int sampleRate = 48000;
        public long durationSamples;
        public List<VisemePhraseTraceFrame> frames = new List<VisemePhraseTraceFrame>();

        public double DurationSeconds
        {
            get
            {
                var rate = Math.Max(1, sampleRate);
                var samples = Math.Max(0L, durationSamples);
                if (frames != null && frames.Count > 1)
                {
                    var last = Math.Max(0L, frames[frames.Count - 1].sampleClock);
                    var previous = Math.Max(0L, frames[frames.Count - 2].sampleClock);
                    samples = Math.Max(samples, last + Math.Max(1L, last - previous));
                }
                return samples / (double)rate;
            }
        }
    }

    [Serializable]
    public sealed class VisemePhraseToken
    {
        [Range(0, 14)] public int viseme;
        public float startSeconds;
        public float endSeconds;
        [Range(0f, 1f)] public float meanVoice;
        public int frameCount;

        public float DurationSeconds => Mathf.Max(0f, endSeconds - startSeconds);
    }

    [Serializable]
    public sealed class VisemePhraseModelState
    {
        public int index;
        [Range(0, 14)] public int primaryViseme;
        public int[] aliasVisemes = Array.Empty<int>();
        public float[] aliasLikelihoods = Array.Empty<float>();
        public float[] emissionLikelihoods = new float[15];
        public float medianDurationSeconds;
        public float minimumDurationSeconds;
        public float maximumDurationSeconds;
        [Range(0f, 1f)] public float meanVoice;
        public bool allowSkip;
        [Range(0f, 1f)] public float skipPenalty = 0.35f;
    }

    [Serializable]
    public sealed class VisemePhraseRuntimeTimingRectangle
    {
        [Tooltip("Positive enrollment slot that seeded this safe runtime timing rectangle.")]
        public int sourceTakeIndex = -1;
        [Tooltip("-1 for the complete path, otherwise the one learned state omitted by this path.")]
        public int skippedStateIndex = -1;
        public float[] minimumDurationSeconds = Array.Empty<float>();
        public float[] maximumDurationSeconds = Array.Empty<float>();
    }

    [Serializable]
    public sealed class VisemePhraseModelVariant
    {
        public string id = string.Empty;
        public string sourceTakeId = string.Empty;
        public int sourceTakeIndex;
        [Tooltip("True when this bounded path was inferred from repeatable local enrollment contexts rather than recorded as one complete take.")]
        public bool inferredContextPath;
        public float cohesion;
        public float medianDurationSeconds;
        public float minimumDurationSeconds;
        public float maximumDurationSeconds;
        public List<VisemePhraseModelState> states = new List<VisemePhraseModelState>();
        public List<VisemePhraseRuntimeTimingRectangle> runtimeTimingRectangles =
            new List<VisemePhraseRuntimeTimingRectangle>();
    }

    [Serializable]
    public sealed class VisemePhraseNegativeCalibration
    {
        public bool calibrated;
        public int negativeTraceCount;
        public float worstPositiveCost;
        public float bestNegativeCost = 1f;
        public float separation;
        public float recommendedAcceptanceCost = 0.3f;
    }

    [Serializable]
    public sealed class VisemePhraseModelDiagnostics
    {
        public bool valid;
        public int positiveTakeCount;
        public int negativeTraceCount;
        public int variantCount;
        public int stateCount;
        [Range(0f, 1f)] public float positiveConsistency;
        [Range(0f, 1f)] public float distinctiveness;
        public float negativeMargin;
        public float meanPairwiseCost;
        public string modelFingerprint = string.Empty;
        public List<VisemePhraseDiagnostic> messages = new List<VisemePhraseDiagnostic>();
    }

    [Serializable]
    public sealed class VisemePhraseCompiledModel
    {
        // Schema 4 retains recorded pronunciations as protected whole-sequence
        // paths and marks separately inferred, bounded context paths. Those
        // optional paths may be pruned to the avatar's finite Animator budget;
        // a creator's four enrolled takes may never be pruned.
        public const int CurrentModelSchemaVersion = 4;

        public int modelSchemaVersion = CurrentModelSchemaVersion;
        public string phraseId = string.Empty;
        public string promptFingerprint = string.Empty;
        public VisemePhraseContextMode contextMode = VisemePhraseContextMode.NaturalSpeech;
        [Range(0f, 1f)] public float strictness = 0.65f;
        public float acceptanceCost = 0.3f;
        public float minimumNegativeMargin = 0.05f;
        public bool requiresLeadingPause;
        public bool requiresTrailingPause;
        public List<VisemePhraseModelVariant> variants = new List<VisemePhraseModelVariant>();
        public VisemePhraseNegativeCalibration negativeCalibration = new VisemePhraseNegativeCalibration();
        public VisemePhraseModelDiagnostics diagnostics = new VisemePhraseModelDiagnostics();
        public string contentFingerprint = string.Empty;
    }

    [Serializable]
    public sealed class VisemePhraseEnrollment
    {
        public const int CurrentEnrollmentSchemaVersion = 1;

        public int enrollmentSchemaVersion = CurrentEnrollmentSchemaVersion;
        public string phraseId = string.Empty;
        public string promptFingerprint = string.Empty;
        public List<VisemePhraseEnrollmentTrace> positiveTakes =
            new List<VisemePhraseEnrollmentTrace>();
        public List<VisemePhraseEnrollmentTrace> negativeTraces =
            new List<VisemePhraseEnrollmentTrace>();
        public VisemePhraseCompiledModel compiledModel;
    }

    [CreateAssetMenu(
        fileName = "Viseme Phrase Enrollment Profile",
        menuName = "YUCP/Viseme Phrase Enrollment Profile")]
    public sealed class VisemePhraseEnrollmentProfile : ScriptableObject
    {
        public const int CurrentProfileSchemaVersion = 1;
        public const int RequiredPositiveTakeCount = 4;

        public int profileSchemaVersion = CurrentProfileSchemaVersion;
        public List<VisemePhraseEnrollment> enrollments = new List<VisemePhraseEnrollment>();

        public VisemePhraseEnrollment FindEnrollment(
            string phraseId,
            string promptFingerprint)
        {
            if (enrollments == null) return null;
            var normalizedId = AdvancedVisemeParameterContract.NormalizePhraseId(phraseId);
            var fingerprint = promptFingerprint ?? string.Empty;
            for (var i = 0; i < enrollments.Count; i++)
            {
                var enrollment = enrollments[i];
                if (enrollment == null) continue;
                if (!string.Equals(enrollment.phraseId, normalizedId, StringComparison.Ordinal)) continue;
                if (string.Equals(enrollment.promptFingerprint, fingerprint, StringComparison.Ordinal))
                    return enrollment;
            }
            return null;
        }

        public VisemePhraseEnrollment FindEnrollmentForPrompt(
            string phraseId,
            string prompt)
        {
            return FindEnrollment(
                phraseId,
                AdvancedVisemeParameterContract.PromptFingerprint(prompt));
        }

        public VisemePhraseCompiledModel FindCompiledModel(
            string phraseId,
            string promptFingerprint,
            bool requireCurrentSchema = true)
        {
            var enrollment = FindEnrollment(phraseId, promptFingerprint);
            var model = enrollment?.compiledModel;
            if (model == null) return null;
            if (requireCurrentSchema)
            {
                if (profileSchemaVersion != CurrentProfileSchemaVersion ||
                    enrollment.enrollmentSchemaVersion !=
                    VisemePhraseEnrollment.CurrentEnrollmentSchemaVersion ||
                    model.modelSchemaVersion !=
                    VisemePhraseCompiledModel.CurrentModelSchemaVersion ||
                    !TracesUseCurrentSchema(enrollment.positiveTakes) ||
                    !TracesUseCurrentSchema(enrollment.negativeTraces))
                    return null;
            }
            return model;
        }

        public VisemePhraseEnrollment GetOrCreateEnrollment(
            string phraseId,
            string promptFingerprint)
        {
            EnsureDefaults();
            var existing = FindEnrollment(phraseId, promptFingerprint);
            if (existing != null) return existing;
            var created = new VisemePhraseEnrollment
            {
                phraseId = AdvancedVisemeParameterContract.NormalizePhraseId(phraseId),
                promptFingerprint = promptFingerprint ?? string.Empty
            };
            enrollments.Add(created);
            return created;
        }

        public void EnsureDefaults()
        {
            if (enrollments == null) enrollments = new List<VisemePhraseEnrollment>();
            for (var i = 0; i < enrollments.Count; i++)
            {
                if (enrollments[i] == null) enrollments[i] = new VisemePhraseEnrollment();
                var enrollment = enrollments[i];
                enrollment.phraseId = AdvancedVisemeParameterContract.NormalizePhraseId(enrollment.phraseId);
                enrollment.promptFingerprint = enrollment.promptFingerprint ?? string.Empty;
                if (enrollment.positiveTakes == null)
                    enrollment.positiveTakes = new List<VisemePhraseEnrollmentTrace>();
                if (enrollment.negativeTraces == null)
                    enrollment.negativeTraces = new List<VisemePhraseEnrollmentTrace>();
            }
        }

        private static bool TracesUseCurrentSchema(
            IReadOnlyList<VisemePhraseEnrollmentTrace> traces)
        {
            if (traces == null) return true;
            for (var i = 0; i < traces.Count; i++)
            {
                if (traces[i] == null ||
                    traces[i].traceSchemaVersion !=
                    VisemePhraseEnrollmentTrace.CurrentTraceSchemaVersion)
                    return false;
            }
            return true;
        }

        private void OnValidate()
        {
            EnsureDefaults();
        }
    }
}
