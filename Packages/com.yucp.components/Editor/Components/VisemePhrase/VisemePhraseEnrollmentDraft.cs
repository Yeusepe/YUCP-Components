using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace YUCP.Components.Editor.VisemePhrase
{
    internal sealed class VisemePhraseEnrollmentDraft : IVisemePhraseEnrollmentDraft
    {
        internal const string DraftRoot = "Assets/YUCP/UserData/PhraseEnrollments";

        private readonly VisemePhraseTriggerData component;
        private readonly VisemePhraseDefinition phrase;
        private readonly GameObject avatarRoot;
        private VisemePhraseEnrollmentProfile profile;

        internal VisemePhraseTriggerData Component => component;
        internal VisemePhraseDefinition Phrase => phrase;
        public UnityEngine.Object TargetObject => component;
        public GameObject AvatarRoot => avatarRoot;
        public string DisplayName => string.IsNullOrWhiteSpace(phrase.prompt)
            ? "New phrase"
            : phrase.prompt.Trim();
        public string Prompt
        {
            get => phrase.prompt ?? string.Empty;
            set
            {
                value = value ?? string.Empty;
                if (string.Equals(phrase.prompt, value, StringComparison.Ordinal)) return;
                Undo.RecordObject(component, "Edit Viseme Phrase Prompt");
                phrase.prompt = value;
                EditorUtility.SetDirty(component);
            }
        }
        public UnityEngine.Object ProfileAsset => profile;
        public string AssetPath => profile != null ? AssetDatabase.GetAssetPath(profile) : string.Empty;

        public IReadOnlyList<VisemePhraseCapturedTake> Takes
        {
            get
            {
                var result = new VisemePhraseCapturedTake[VisemePhraseEnrollmentProfile.RequiredPositiveTakeCount];
                var enrollment = FindEnrollment();
                if (enrollment?.positiveTakes == null) return result;
                for (var i = 0; i < result.Length && i < enrollment.positiveTakes.Count; i++)
                    result[i] = FromTrace(enrollment.positiveTakes[i]);
                return result;
            }
        }

        public VisemePhraseCapturedTake NegativeSample
        {
            get
            {
                var traces = FindEnrollment()?.negativeTraces;
                return traces != null && traces.Count > 0 ? FromTrace(traces[0]) : null;
            }
        }

        internal VisemePhraseEnrollmentDraft(
            VisemePhraseTriggerData component,
            VisemePhraseDefinition phrase,
            bool createProfile = true)
        {
            this.component = component != null
                ? component
                : throw new ArgumentNullException(nameof(component));
            this.phrase = phrase ?? throw new ArgumentNullException(nameof(phrase));
            avatarRoot = ResolveAvatarRoot(component.gameObject);
            var assignedProfile = component.enrollmentProfile;
            var replaceAssignedProfile = assignedProfile != null &&
                                         !CanUseAsPersonalEnrollment(component, assignedProfile);
            profile = replaceAssignedProfile ? null : assignedProfile;
            if (profile == null && createProfile)
                profile = CreateAndAssignProfile(
                    component,
                    avatarRoot,
                    createBlank: replaceAssignedProfile);
            phrase.EnsureDefaults();
        }

        public void SavePrompt()
        {
            phrase.EnsureDefaults();
            EditorUtility.SetDirty(component);
            // An automatic Play Mode handoff opens an assetless draft. Merely
            // arming the microphone or viewing the guide must not create and
            // assign a user asset; SaveTake/SaveNegativeSample do that only
            // after an accepted recording exists.
            if (profile == null) return;
            var enrollment = profile.FindEnrollment(
                phrase.id,
                phrase.PromptFingerprint);
            if (enrollment == null) return;

            // Mode and strictness are part of the baked matcher contract. The
            // four raw takes remain valid when either setting changes, so rebake
            // them immediately instead of leaving the creator in a build ->
            // enrollment -> build stale-model loop.
            if (enrollment.positiveTakes != null &&
                enrollment.positiveTakes.Count ==
                VisemePhraseEnrollmentProfile.RequiredPositiveTakeCount &&
                enrollment.positiveTakes.All(take => take != null))
            {
                Undo.RecordObject(profile, "Recompile Viseme Phrase Enrollment");
                enrollment.compiledModel = null;
                CompileIfComplete(enrollment);
            }
            SaveProfile();
        }

        public void SaveTake(int index, VisemePhraseCapturedTake take)
        {
            if (index < 0 || index >= VisemePhraseEnrollmentProfile.RequiredPositiveTakeCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (take == null) throw new ArgumentNullException(nameof(take));
            EnsureProfile();
            Undo.RecordObject(profile, index < PositiveCount() ? "Retake Viseme Phrase" : "Record Viseme Phrase");
            var enrollment = CurrentEnrollment();
            while (enrollment.positiveTakes.Count <= index) enrollment.positiveTakes.Add(null);
            enrollment.positiveTakes[index] = ToTrace(take);
            if (enrollment.positiveTakes.Count > VisemePhraseEnrollmentProfile.RequiredPositiveTakeCount)
                enrollment.positiveTakes.RemoveRange(
                    VisemePhraseEnrollmentProfile.RequiredPositiveTakeCount,
                    enrollment.positiveTakes.Count - VisemePhraseEnrollmentProfile.RequiredPositiveTakeCount);
            enrollment.compiledModel = null;
            CompileIfComplete(enrollment);
            SaveProfile();
        }

        public void SaveNegativeSample(VisemePhraseCapturedTake take)
        {
            if (take == null) throw new ArgumentNullException(nameof(take));
            EnsureProfile();
            Undo.RecordObject(profile, "Record Viseme Phrase Negative Sample");
            var enrollment = CurrentEnrollment();
            enrollment.negativeTraces.Clear();
            enrollment.negativeTraces.Add(ToTrace(take));
            enrollment.compiledModel = null;
            CompileIfComplete(enrollment);
            SaveProfile();
        }

        public void ClearNegativeSample()
        {
            var enrollment = FindEnrollment();
            if (profile == null || enrollment?.negativeTraces == null || enrollment.negativeTraces.Count == 0) return;
            Undo.RecordObject(profile, "Clear Viseme Phrase Negative Sample");
            enrollment.negativeTraces.Clear();
            enrollment.compiledModel = null;
            CompileIfComplete(enrollment);
            SaveProfile();
        }

        internal VisemePhraseEnrollment Enrollment => FindEnrollment();

        internal bool WouldCompileWithReplacement(
            int index,
            VisemePhraseCapturedTake take)
        {
            if (index < 0 ||
                index >= VisemePhraseEnrollmentProfile.RequiredPositiveTakeCount ||
                take == null)
                return false;
            var source = FindEnrollment();
            if (source?.positiveTakes == null ||
                source.positiveTakes.Count !=
                VisemePhraseEnrollmentProfile.RequiredPositiveTakeCount ||
                source.positiveTakes.Any(candidate => candidate == null))
                return false;

            // Compile a list-only candidate. The compiler is pure and the raw
            // source traces remain untouched, so a failed optional retake can
            // never overwrite a known-good personal enrollment.
            var candidate = new VisemePhraseEnrollment
            {
                enrollmentSchemaVersion = source.enrollmentSchemaVersion,
                phraseId = source.phraseId,
                promptFingerprint = source.promptFingerprint
            };
            candidate.positiveTakes.AddRange(source.positiveTakes);
            candidate.positiveTakes[index] = ToTrace(take);
            if (source.negativeTraces != null)
                candidate.negativeTraces.AddRange(source.negativeTraces);
            return VisemePhraseModelCompiler.Compile(phrase, candidate).success;
        }

        internal VisemePhraseCompileResult Compile()
        {
            EnsureProfile();
            var enrollment = CurrentEnrollment();
            var result = VisemePhraseModelCompiler.Compile(phrase, enrollment);
            // Keep an invalid model's diagnostics with the draft. Build
            // integration still rejects diagnostics.valid == false, while the
            // wizard can now route a concrete runtime-replay error to the right
            // take instead of showing a generic compilation page.
            enrollment.compiledModel = result.model;
            SaveProfile();
            return result;
        }

        internal static VisemePhraseCapturedTake FromTrace(VisemePhraseEnrollmentTrace trace)
        {
            if (trace == null) return null;
            var take = new VisemePhraseCapturedTake
            {
                backend = trace.backend ?? string.Empty,
                durationSeconds = trace.DurationSeconds
            };
            if (trace.frames == null) return take;
            for (var i = 0; i < trace.frames.Count; i++)
            {
                var frame = trace.frames[i];
                if (frame == null) continue;
                take.frames.Add(new VisemePhraseCapturedFrame(
                    frame.viseme,
                    frame.voice,
                    frame.sampleClock,
                    trace.sampleRate));
            }
            return take;
        }

        internal static VisemePhraseEnrollmentTrace ToTrace(VisemePhraseCapturedTake take)
        {
            var sampleRate = take.frames.Count > 0 ? take.frames[0].sampleRate : 48000;
            var trace = new VisemePhraseEnrollmentTrace
            {
                takeId = Guid.NewGuid().ToString("N"),
                backend = take.backend ?? string.Empty,
                recordedUtcTicks = DateTime.UtcNow.Ticks,
                sampleRate = Math.Max(1, sampleRate),
                durationSamples = take.frames.Count > 1
                    ? Math.Max(0L, take.frames[take.frames.Count - 1].sampleClock)
                    : (long)Math.Round(Math.Max(0d, take.durationSeconds) * Math.Max(1, sampleRate)),
                frames = take.frames.Select(frame => new VisemePhraseTraceFrame
                {
                    sampleClock = frame.sampleClock,
                    viseme = frame.viseme,
                    voice = frame.voice
                }).ToList()
            };
            return trace;
        }

        private void EnsureProfile()
        {
            if (profile == null)
                profile = CreateAndAssignProfile(component, avatarRoot, createBlank: false);
            if (profile == null) throw new InvalidOperationException("Could not create the enrollment profile asset.");
        }

        private VisemePhraseEnrollment CurrentEnrollment()
        {
            phrase.EnsureDefaults();
            return profile.GetOrCreateEnrollment(phrase.id, phrase.PromptFingerprint);
        }

        private VisemePhraseEnrollment FindEnrollment()
        {
            if (profile == null) return null;
            phrase.EnsureDefaults();
            return profile.FindEnrollment(phrase.id, phrase.PromptFingerprint);
        }

        private int PositiveCount()
        {
            return FindEnrollment()?.positiveTakes?.Count ?? 0;
        }

        private void SaveProfile()
        {
            profile.EnsureDefaults();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        private void CompileIfComplete(VisemePhraseEnrollment enrollment)
        {
            if (enrollment?.positiveTakes == null ||
                enrollment.positiveTakes.Count != VisemePhraseEnrollmentProfile.RequiredPositiveTakeCount ||
                enrollment.positiveTakes.Any(take => take == null))
                return;
            var result = VisemePhraseModelCompiler.Compile(phrase, enrollment);
            enrollment.compiledModel = result.model;
        }

        private static VisemePhraseEnrollmentProfile CreateAndAssignProfile(
            VisemePhraseTriggerData component,
            GameObject avatarRoot,
            bool createBlank = false)
        {
            EnsureFolder(DraftRoot);
            component.EnsureDefaults();
            var identity = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
            if (string.IsNullOrWhiteSpace(identity) || identity.EndsWith("-0-0", StringComparison.Ordinal))
                identity = component.gameObject.scene.path + ":" + component.transform.GetHierarchyPath();
            var suffix = Hash128.Compute(identity).ToString().Substring(0, 8);
            var avatarName = SanitizeFileName(avatarRoot != null ? avatarRoot.name : component.name);
            var path = $"{DraftRoot}/{avatarName}_{suffix}.asset";
            var created = createBlank
                ? null
                : AssetDatabase.LoadAssetAtPath<VisemePhraseEnrollmentProfile>(path);
            if (created == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                    path = AssetDatabase.GenerateUniqueAssetPath(path);
                created = ScriptableObject.CreateInstance<VisemePhraseEnrollmentProfile>();
                created.name = $"{avatarName} Phrase Enrollments";
                AssetDatabase.CreateAsset(created, path);
                AssetDatabase.SaveAssetIfDirty(created);
            }

            Undo.RecordObject(component, "Create Viseme Phrase Enrollment Profile");
            component.enrollmentProfile = created;
            EditorUtility.SetDirty(component);
            return created;
        }

        internal static bool CanUseAsPersonalEnrollment(
            VisemePhraseTriggerData component,
            VisemePhraseEnrollmentProfile candidate)
        {
            if (component == null || candidate == null) return false;
            if (!UsesCurrentRawSchema(candidate)) return false;
            var path = (AssetDatabase.GetAssetPath(candidate) ?? string.Empty)
                .Replace('\\', '/');
            if (!path.StartsWith(DraftRoot + "/", StringComparison.OrdinalIgnoreCase))
                return false;

            // A profile serialized on the prefab source is the prefab author's
            // enrollment, even when its asset happens to live under UserData.
            // Enrollment must instead be an instance-local reference so importing
            // or reusing a prefab never imports another person's speech traces.
            var source = PrefabUtility.GetCorrespondingObjectFromSource(component);
            return source == null || source.enrollmentProfile != candidate;
        }

        private static bool UsesCurrentRawSchema(
            VisemePhraseEnrollmentProfile candidate)
        {
            if (candidate.profileSchemaVersion !=
                VisemePhraseEnrollmentProfile.CurrentProfileSchemaVersion)
                return false;
            if (candidate.enrollments == null) return true;
            foreach (var enrollment in candidate.enrollments)
            {
                if (enrollment == null ||
                    enrollment.enrollmentSchemaVersion !=
                    VisemePhraseEnrollment.CurrentEnrollmentSchemaVersion)
                    return false;
                if (!TracesAreCurrent(enrollment.positiveTakes) ||
                    !TracesAreCurrent(enrollment.negativeTraces))
                    return false;
            }
            return true;
        }

        private static bool TracesAreCurrent(
            IReadOnlyList<VisemePhraseEnrollmentTrace> traces)
        {
            if (traces == null) return true;
            foreach (var trace in traces)
            {
                if (trace == null ||
                    trace.traceSchemaVersion !=
                    VisemePhraseEnrollmentTrace.CurrentTraceSchemaVersion)
                    return false;
            }
            return true;
        }

        private static void EnsureFolder(string path)
        {
            var segments = path.Split('/');
            var current = segments[0];
            for (var i = 1; i < segments.Length; i++)
            {
                var next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }

        private static string SanitizeFileName(string value)
        {
            var sanitized = string.IsNullOrWhiteSpace(value) ? "Avatar" : value.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars()) sanitized = sanitized.Replace(invalid, '_');
            return sanitized;
        }

        private static GameObject ResolveAvatarRoot(GameObject source)
        {
            if (source == null) return null;
            var descriptor = source.GetComponentInParent<VRCAvatarDescriptor>();
            return descriptor != null ? descriptor.gameObject : source.transform.root.gameObject;
        }
    }

    internal static class TransformPhrasePathExtensions
    {
        internal static string GetHierarchyPath(this Transform transform)
        {
            if (transform == null) return string.Empty;
            var path = transform.name + "[" + transform.GetSiblingIndex() + "]";
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "[" + transform.GetSiblingIndex() + "]/" + path;
            }
            return path;
        }
    }
}
