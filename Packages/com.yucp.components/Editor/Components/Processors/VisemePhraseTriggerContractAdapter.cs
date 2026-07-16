#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace YUCP.Components.Editor.VisemePhrase
{
    internal static class VisemePhraseTriggerContractAdapter
    {
        private const float PausedCommandLeadingSilenceSeconds = 0.18f;
        private const float PausedCommandTrailingSilenceSeconds = 0.22f;
        // Enrollment timestamps audio blocks; the Animator observes the same
        // categorical winner only after the 30 ms run filter. Widen the baked
        // corridor by exactly that observer uncertainty instead of pretending
        // render-frame boundaries are sample exact.
        internal const float RuntimeObservationUncertaintySeconds =
            VisemePhraseRuntimeLanguage.RuntimeObservationUncertaintySeconds;

        internal static float RuntimeObservationUncertaintyPerState(
            int retainedStateCount) =>
            RuntimeObservationUncertaintySeconds /
            Math.Max(1, retainedStateCount);

        internal static bool TryCreatePlan(
            GameObject avatarRoot,
            VRCAvatarDescriptor descriptor,
            IReadOnlyList<VisemePhraseTriggerData> components,
            out VisemePhraseBuildPlan plan,
            out string error)
        {
            plan = null;
            error = null;
            if (avatarRoot == null)
            {
                error = "The avatar root is missing.";
                return false;
            }
            if (descriptor == null)
            {
                error = "The avatar has no VRC Avatar Descriptor.";
                return false;
            }
            if (descriptor.lipSync == VRCAvatarDescriptor.LipSyncStyle.JawFlapBone ||
                descriptor.lipSync == VRCAvatarDescriptor.LipSyncStyle.JawFlapBlendShape)
            {
                error = "Viseme Phrase Trigger cannot read VRChat's 0-14 Viseme index " +
                        "while the avatar uses Jaw Flap lip sync. Configure Viseme Blend Shape " +
                        "or Viseme Parameter Only first.";
                return false;
            }
            if (components == null || components.Count == 0)
            {
                plan = new VisemePhraseBuildPlan();
                return true;
            }

            var allPhrases = components.Sum(component => component?.phrases?.Count ?? 0);
            if (allPhrases == 0)
            {
                error = "Add at least one enrolled phrase before building the avatar.";
                return false;
            }
            if (allPhrases > VisemePhraseBuildPlan.MaximumPhrases)
            {
                error = $"An avatar may contain at most {VisemePhraseBuildPlan.MaximumPhrases} " +
                        $"phrase triggers; this avatar contains {allPhrases}.";
                return false;
            }

            var reconstructors = avatarRoot
                .GetComponentsInChildren<AdvancedVisemeReconstructorData>(true);
            if (reconstructors.Length == 0)
            {
                error = "Viseme Phrase Trigger requires an Advanced Viseme Reconstructor on the same avatar.";
                return false;
            }

            var output = new VisemePhraseBuildPlan();
            foreach (var component in components
                         .Where(item => item != null)
                         .OrderBy(item => StableOwnerKey(avatarRoot.transform, item),
                             StringComparer.Ordinal))
            {
                var sourcePrefix = ResolveSourcePrefix(component, reconstructors, out error);
                if (sourcePrefix == null) return false;
                if (component.enrollmentProfile == null)
                {
                    error = $"'{StableOwnerKey(avatarRoot.transform, component)}' has no enrollment profile.";
                    return false;
                }
                var profilePath = UnityEditor.AssetDatabase.GetAssetPath(
                    component.enrollmentProfile).Replace('\\', '/');
                if (!profilePath.StartsWith(
                        "Assets/YUCP/UserData/PhraseEnrollments/",
                        StringComparison.Ordinal))
                {
                    error = "Phrase enrollment must be personal creator data under " +
                            "'Assets/YUCP/UserData/PhraseEnrollments/'. Use Record / Improve " +
                            "to create your own profile instead of shipping a prefab or package " +
                            "author's voice enrollment.";
                    return false;
                }
                if (UsesInheritedPrefabEnrollment(component, out var prefabPath))
                {
                    error = "Phrase enrollment is inherited from prefab source '" +
                            prefabPath + "'. Record / Improve must create and assign a " +
                            "personal per-instance enrollment profile before building.";
                    return false;
                }
                if (component.enrollmentProfile.profileSchemaVersion !=
                    VisemePhraseEnrollmentProfile.CurrentProfileSchemaVersion)
                {
                    error = "The phrase enrollment profile uses an unsupported schema. " +
                            "Re-record or explicitly upgrade it before building.";
                    return false;
                }
                if (component.phrases == null || component.phrases.Count == 0)
                {
                    error = $"'{StableOwnerKey(avatarRoot.transform, component)}' has no phrases.";
                    return false;
                }

                var outputPrefix = AdvancedVisemeParameterContract.NormalizePrefix(
                    component.parameterPrefix,
                    AdvancedVisemeParameterContract.DefaultPhrasePrefix);
                foreach (var definition in component.phrases)
                {
                    if (definition == null)
                    {
                        error = "A Viseme Phrase Trigger contains a missing phrase entry.";
                        return false;
                    }

                    var prompt = (definition.prompt ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(prompt))
                    {
                        error = "Every phrase needs non-empty prompt text.";
                        return false;
                    }
                    var phraseId = string.IsNullOrWhiteSpace(definition.id)
                        ? AdvancedVisemeParameterContract.StablePhraseId(prompt)
                        : AdvancedVisemeParameterContract.NormalizePhraseId(definition.id);
                    var parameterKey = string.IsNullOrWhiteSpace(definition.parameterKey)
                        ? AdvancedVisemeParameterContract.DefaultParameterKey(prompt, phraseId)
                        : AdvancedVisemeParameterContract.NormalizePhraseId(definition.parameterKey);
                    var fingerprint = AdvancedVisemeParameterContract.PromptFingerprint(prompt);
                    var enrollment = component.enrollmentProfile.FindEnrollment(
                        phraseId,
                        fingerprint);
                    if (!ValidateCurrentEnrollmentSchemas(
                            prompt, enrollment, out error)) return false;
                    var rebuilt = VisemePhraseModelCompiler.Compile(definition, enrollment);
                    if (!rebuilt.success || rebuilt.model == null)
                    {
                        error = $"Phrase '{prompt}' raw enrollment no longer compiles. " +
                                "Resolve its take diagnostics before building.";
                        return false;
                    }
                    var model = rebuilt.model;
                    CacheRebuiltModelIfChanged(
                        component.enrollmentProfile, enrollment, model);
                    if (model.diagnostics == null || !model.diagnostics.valid)
                    {
                        error = $"Phrase '{prompt}' has an invalid compiled model. " +
                                "Resolve its enrollment diagnostics before building.";
                        return false;
                    }
                    if (model.contextMode != definition.mode ||
                        Math.Abs(model.strictness - definition.strictness) > 0.0001f)
                    {
                        error = $"Phrase '{prompt}' settings changed after enrollment was compiled. " +
                                "Compile it again so timing and acceptance remain deterministic.";
                        return false;
                    }
                    if (model.variants == null || model.variants.Count == 0)
                    {
                        error = $"Phrase '{prompt}' has no compiled timing variants.";
                        return false;
                    }

                    var phrase = new VisemePhraseBuildPhrase
                    {
                        ownerKey = StableOwnerKey(avatarRoot.transform, component),
                        prompt = prompt,
                        stableId = phraseId,
                        parameterKey = parameterKey,
                        sourcePrefix = sourcePrefix,
                        talkingParameter = AdvancedVisemeParameterContract.Speech(sourcePrefix, "Talking"),
                        onsetParameter = AdvancedVisemeParameterContract.Speech(sourcePrefix, "Onset"),
                        releaseParameter = AdvancedVisemeParameterContract.Speech(sourcePrefix, "Release"),
                        matchedParameter = AdvancedVisemeParameterContract.PhraseMatched(outputPrefix, parameterKey),
                        confidenceParameter = AdvancedVisemeParameterContract.PhraseConfidence(outputPrefix, parameterKey),
                        progressParameter = AdvancedVisemeParameterContract.PhraseProgress(outputPrefix, parameterKey),
                        carrierParameter = AdvancedVisemeParameterContract.PhraseCarrier(outputPrefix, phraseId),
                        enrollmentFingerprint = EnrollmentFingerprint(
                            component.enrollmentProfile, enrollment),
                        pulseSeconds = FiniteOr(definition.pulseSeconds,
                            VisemePhraseBuildPlan.DefaultPulseSeconds),
                        cooldownSeconds = Math.Max(
                            VisemePhraseBuildPlan.MinimumNetworkCooldownSeconds,
                            FiniteOr(definition.cooldownSeconds,
                                VisemePhraseBuildPlan.MinimumNetworkCooldownSeconds)),
                        requireOnset = definition.mode == VisemePhraseContextMode.PausedCommand,
                        requireRelease = definition.mode == VisemePhraseContextMode.PausedCommand,
                        leadingPauseSeconds = definition.mode == VisemePhraseContextMode.PausedCommand
                            ? PausedCommandLeadingSilenceSeconds
                            : 0f,
                        trailingPauseSeconds = definition.mode == VisemePhraseContextMode.PausedCommand
                            ? PausedCommandTrailingSilenceSeconds
                            : 0f,
                        runtimeAcceptanceCost = Mathf.Clamp01(model.acceptanceCost)
                    };
                    phrase.pulseSeconds = Math.Max(0.05f, phrase.pulseSeconds);
                    phrase.cooldownSeconds = Math.Max(
                        phrase.cooldownSeconds,
                        phrase.pulseSeconds + 1f / 50f);

                    foreach (var sourceVariant in model.variants
                                 .Where(item => item != null)
                                 .OrderBy(item => item.id, StringComparer.Ordinal))
                    {
                        if (sourceVariant.states == null || sourceVariant.states.Count == 0)
                        {
                            error = $"Phrase '{prompt}' contains an empty compiled variant.";
                            return false;
                        }
                        if (sourceVariant.states.Count >
                            VisemePhraseBuildPlan.MaximumStatesPerVariant)
                        {
                            error = $"Phrase '{prompt}' variant '{sourceVariant.id}' has " +
                                    $"{sourceVariant.states.Count} states; the maximum is " +
                                    $"{VisemePhraseBuildPlan.MaximumStatesPerVariant}.";
                            return false;
                        }
                        var rectangles = sourceVariant.runtimeTimingRectangles ??
                                         new List<VisemePhraseRuntimeTimingRectangle>();
                        for (var rectangleIndex = 0;
                             rectangleIndex < rectangles.Count;
                             rectangleIndex++)
                        {
                            var rectangle = rectangles[rectangleIndex];
                            if (!ValidateRuntimeRectangle(
                                    sourceVariant, rectangle, out var rectangleError))
                            {
                                error = $"Phrase '{prompt}' variant '{sourceVariant.id}' " +
                                        rectangleError;
                                return false;
                            }

                            var skipped = rectangle.skippedStateIndex;
                            if (!VisemePhraseRuntimeLanguage.TryGetPathAliases(
                                    sourceVariant, skipped,
                                    out var retainedStateIndices,
                                    out var pathAliases,
                                    out var pathAliasError))
                            {
                                error = $"Phrase '{prompt}' variant '{sourceVariant.id}' " +
                                        pathAliasError;
                                return false;
                            }
                            var variant = new VisemePhraseBuildVariant
                            {
                                id = (sourceVariant.id ?? string.Empty) +
                                     "_rectangle_" + rectangleIndex,
                                inferredContextPath =
                                    sourceVariant.inferredContextPath ||
                                    rectangle.inferredProfile,
                                inferredConfusionPath =
                                    sourceVariant.inferredConfusionPath,
                                inferredTimingProfile = rectangle.inferredProfile,
                                canonicalStateCount = sourceVariant.states.Count,
                                minimumTotalSeconds = rectangle
                                    .includesRuntimeObservationUncertainty
                                    ? VisemePhraseRuntimeLanguage
                                        .RuntimeMinimumTotalSeconds(sourceVariant)
                                    : Math.Max(0f, FiniteOr(
                                        sourceVariant.minimumDurationSeconds, 0f) -
                                        RuntimeObservationUncertaintySeconds),
                                maximumTotalSeconds = rectangle
                                    .includesRuntimeObservationUncertainty
                                    ? VisemePhraseRuntimeLanguage
                                        .RuntimeMaximumTotalSeconds(sourceVariant)
                                    : Math.Max(0f, FiniteOr(
                                        sourceVariant.maximumDurationSeconds,
                                        float.MaxValue) +
                                        RuntimeObservationUncertaintySeconds),
                                runtimeBaseCost = Math.Max(
                                    0f, FiniteOr(sourceVariant.inferencePenalty, 0f)) +
                                    (skipped >= 0
                                        ? Mathf.Clamp01(
                                            sourceVariant.states[skipped].skipPenalty)
                                        : 0f)
                            };
                            var runtimeBudget = phrase.runtimeAcceptanceCost *
                                Math.Max(1, variant.canonicalStateCount);
                            // A generic confusion arc is optional. Personalized
                            // negative calibration may tighten the budget after
                            // it was proposed; silently omit that arc instead of
                            // allowing it to make an enrolled model unbuildable.
                            if (variant.inferredConfusionPath &&
                                variant.runtimeBaseCost > runtimeBudget + 0.000001f)
                                continue;
                            // Observation uncertainty belongs to the phrase
                            // boundary, not independently to every phone. Split
                            // one fixed 30 ms allowance across retained states so
                            // the Cartesian timing rectangle cannot grow once per state.
                            var perStateObservationUncertainty =
                                rectangle.includesRuntimeObservationUncertainty
                                    ? 0f
                                    : RuntimeObservationUncertaintyPerState(
                                        retainedStateIndices.Length);
                            for (var pathIndex = 0;
                                 pathIndex < retainedStateIndices.Length;
                                 pathIndex++)
                            {
                                var stateIndex = retainedStateIndices[pathIndex];
                                var sourceState = sourceVariant.states[stateIndex];
                                var aliases = pathAliases[pathIndex];
                                if (aliases.Length == 0)
                                {
                                    error = $"Phrase '{prompt}' contains a state with no valid " +
                                            "viseme aliases.";
                                    return false;
                                }

                                variant.states.Add(new VisemePhraseBuildState
                                {
                                    aliases = aliases,
                                    minimumSeconds = Math.Max(0f, rectangle
                                        .minimumDurationSeconds[stateIndex] -
                                        perStateObservationUncertainty),
                                    maximumSeconds = rectangle
                                        .maximumDurationSeconds[stateIndex] +
                                        perStateObservationUncertainty,
                                    confidence = Mathf.Clamp01(sourceVariant.cohesion > 0f
                                        ? sourceVariant.cohesion
                                        : 1f),
                                    // The rectangle has already made the one exact skip
                                    // decision validated by the compiler.
                                    allowSkip = false,
                                    emissionLikelihoods = NormalizeEmissions(sourceState),
                                    skipPenalty = 0f
                                });
                            }
                            phrase.variants.Add(variant);
                        }
                    }

                    if (phrase.variants.Count == 0)
                    {
                        error = $"Phrase '{prompt}' has no positive-seeded safe runtime timing " +
                                "rectangles. Recompile or re-record its enrollment.";
                        return false;
                    }

                    // Equivalent trained takes are useful to the compiler but do
                    // not need duplicate runtime paths.
                    var distinctVariants = phrase.variants
                        .GroupBy(item => item.CanonicalRuntimePathFingerprint(),
                            StringComparer.Ordinal)
                        .Select(group => group
                            .OrderBy(item => item.inferredContextPath)
                            .ThenBy(item => item.inferredConfusionPath)
                            .ThenBy(item => item.inferredTimingProfile)
                            .ThenBy(item => SourceVariantPriority(item.id))
                            .ThenBy(item => item.CanonicalFingerprint(), StringComparer.Ordinal)
                            .First())
                        // Compiler ids preserve support ranking: enrolled paths
                        // precede optional inferences, and lower inferred ids are
                        // the stronger profile/splice bridges. Keep that order so
                        // the state-budget fitter removes the weakest bridge,
                        // rather than whichever content hash happens to sort last.
                        .OrderBy(item => item.inferredContextPath)
                        .ThenBy(item => item.inferredConfusionPath)
                        .ThenBy(item => item.inferredTimingProfile)
                        .ThenBy(item => SourceVariantPriority(item.id))
                        .ThenBy(item => item.CanonicalFingerprint(), StringComparer.Ordinal)
                        .ToList();
                    phrase.variants.Clear();
                    phrase.variants.AddRange(distinctVariants);
                    output.phrases.Add(phrase);
                }
            }

            if (!ValidatePhraseIdentityConflicts(output.phrases, out error)) return false;
            if (!TryFitRuntimeLanguage(output, out _, out error)) return false;
            if (!ValidateExistingParameters(descriptor, output.phrases, out error)) return false;
            plan = output;
            return true;
        }

        private static bool UsesInheritedPrefabEnrollment(
            VisemePhraseTriggerData component,
            out string prefabPath)
        {
            prefabPath = string.Empty;
            if (component == null || component.enrollmentProfile == null) return false;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(component);
            while (source != null)
            {
                if (ReferenceEquals(source.enrollmentProfile, component.enrollmentProfile))
                {
                    prefabPath = AssetDatabase.GetAssetPath(source);
                    if (string.IsNullOrEmpty(prefabPath)) prefabPath = source.name;
                    return true;
                }
                source = PrefabUtility.GetCorrespondingObjectFromSource(source);
            }
            return false;
        }

        private static bool ValidateCurrentEnrollmentSchemas(
            string prompt,
            VisemePhraseEnrollment enrollment,
            out string error)
        {
            error = null;
            if (enrollment == null)
            {
                error = $"Phrase '{prompt}' has no matching enrollment. Record its takes " +
                        "before building.";
                return false;
            }
            if (enrollment.enrollmentSchemaVersion !=
                VisemePhraseEnrollment.CurrentEnrollmentSchemaVersion)
            {
                error = $"Phrase '{prompt}' uses an unsupported enrollment schema. " +
                        "Re-record or explicitly upgrade it before building.";
                return false;
            }
            foreach (var trace in (enrollment.positiveTakes ??
                         new List<VisemePhraseEnrollmentTrace>()).Concat(
                         enrollment.negativeTraces ??
                         new List<VisemePhraseEnrollmentTrace>()))
            {
                if (trace != null && trace.traceSchemaVersion ==
                    VisemePhraseEnrollmentTrace.CurrentTraceSchemaVersion) continue;
                error = $"Phrase '{prompt}' contains a missing or unsupported trace schema. " +
                        "Re-record the affected take before building.";
                return false;
            }
            return true;
        }

        private static void CacheRebuiltModelIfChanged(
            VisemePhraseEnrollmentProfile profile,
            VisemePhraseEnrollment enrollment,
            VisemePhraseCompiledModel rebuilt)
        {
            if (profile == null || enrollment == null || rebuilt == null) return;
            var previous = enrollment.compiledModel;
            if (previous != null &&
                previous.modelSchemaVersion == rebuilt.modelSchemaVersion &&
                string.Equals(previous.contentFingerprint,
                    rebuilt.contentFingerprint, StringComparison.Ordinal))
                return;
            // The model is a deterministic cache derived solely from the
            // creator's existing traces and settings. Refreshing it during
            // preflight requires no microphone, no wizard, and never changes
            // a recorded frame.
            enrollment.compiledModel = rebuilt;
            EditorUtility.SetDirty(profile);
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(profile)))
                AssetDatabase.SaveAssetIfDirty(profile);
        }

        internal static string StableOwnerKey(
            Transform avatarRoot,
            Component component)
        {
            if (component == null) return "<missing component>";
            var path = new Stack<string>();
            var cursor = component.transform;
            while (cursor != null)
            {
                path.Push((cursor.name ?? string.Empty) + "[" + cursor.GetSiblingIndex() + "]");
                if (cursor == avatarRoot) break;
                cursor = cursor.parent;
            }
            return string.Join("/", path) + ":" + component.GetType().FullName;
        }

        private static string ResolveSourcePrefix(
            VisemePhraseTriggerData component,
            IReadOnlyList<AdvancedVisemeReconstructorData> reconstructors,
            out string error)
        {
            error = null;
            var requested = component.NormalizedSourcePrefix;
            if (string.IsNullOrEmpty(requested))
            {
                if (reconstructors.Count == 1)
                    return reconstructors[0].NormalizedPrefix;
                error = $"'{component.name}' has a blank Advanced Viseme source, but the avatar " +
                        $"contains {reconstructors.Count} reconstructors. Select one by its exact prefix.";
                return null;
            }

            var matches = reconstructors.Where(reconstructor =>
                    string.Equals(reconstructor.NormalizedPrefix, requested, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 1) return matches[0].NormalizedPrefix;
            error = matches.Length == 0
                ? $"'{component.name}' references Advanced Viseme prefix '{requested}', but no reconstructor uses it."
                : $"Advanced Viseme prefix '{requested}' is ambiguous on this avatar.";
            return null;
        }

        private static bool ValidatePhraseIdentityConflicts(
            IReadOnlyList<VisemePhraseBuildPhrase> phrases,
            out string error)
        {
            error = null;
            var duplicateId = phrases.GroupBy(phrase => phrase.stableId, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateId != null)
            {
                error = $"Stable phrase ID '{duplicateId.Key}' is used more than once on the " +
                        "avatar. Each phrase needs a globally unique enrollment identity.";
                return false;
            }
            var outputNames = phrases.SelectMany(phrase => new[]
                {
                    (name: phrase.matchedParameter, role: "Matched Bool", prompt: phrase.prompt),
                    (name: phrase.confidenceParameter, role: "Confidence Float", prompt: phrase.prompt),
                    (name: phrase.progressParameter, role: "Progress Float", prompt: phrase.prompt),
                    (name: phrase.carrierParameter, role: "network carrier Bool", prompt: phrase.prompt)
                })
                .GroupBy(item => item.name, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (outputNames != null)
            {
                error = $"Generated parameter '{outputNames.Key}' has multiple roles: " +
                        string.Join(", ", outputNames.Select(item =>
                            $"{item.role} for '{item.prompt}'")) + ".";
                return false;
            }
            for (var leftIndex = 0; leftIndex < phrases.Count; leftIndex++)
            for (var rightIndex = leftIndex + 1; rightIndex < phrases.Count; rightIndex++)
            {
                var left = phrases[leftIndex];
                var right = phrases[rightIndex];
                if (string.Equals(left.carrierParameter, right.carrierParameter,
                        StringComparison.Ordinal))
                {
                    error = $"Phrases '{left.prompt}' and '{right.prompt}' share hidden carrier " +
                            $"'{left.carrierParameter}'. Give each phrase a unique stable ID.";
                    return false;
                }

                var sameOutput = string.Equals(left.matchedParameter, right.matchedParameter,
                    StringComparison.Ordinal);
                if (sameOutput)
                {
                    error = $"Phrases '{left.prompt}' and '{right.prompt}' both write " +
                            $"'{left.matchedParameter}'. Give them different parameter keys.";
                    return false;
                }
            }
            return true;
        }

        private static bool ValidatePhraseTraceConflicts(
            IReadOnlyList<VisemePhraseBuildPhrase> phrases,
            out string error)
        {
            error = null;
            for (var leftIndex = 0; leftIndex < phrases.Count; leftIndex++)
            for (var rightIndex = leftIndex + 1; rightIndex < phrases.Count; rightIndex++)
            {
                var left = phrases[leftIndex];
                var right = phrases[rightIndex];
                var leftTraces = new HashSet<string>(left.variants.Select(item =>
                    item.CanonicalTrace()), StringComparer.Ordinal);
                var overlappingTrace = right.variants
                    .Select(item => item.CanonicalTrace())
                    .FirstOrDefault(leftTraces.Contains);
                if (overlappingTrace == null) continue;
                error = $"Phrases '{left.prompt}' and '{right.prompt}' contain the same " +
                        "compiled viseme trace but drive different outputs. Re-enroll them " +
                        "with more distinctive speech or remove one trigger.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Fit optional generalized paths to the actual shared timed-state plan.
        /// Generic confusion paths are removed before creator-trained context
        /// bridges; compiler ranking then removes the weakest remaining bridge.
        /// Directly enrolled paths are immutable, so an exact-only conflict or
        /// oversize language still fails loudly.
        /// </summary>
        internal static bool TryFitRuntimeLanguage(
            VisemePhraseBuildPlan plan,
            out int stateCount,
            out string error)
        {
            stateCount = 0;
            error = null;
            if (plan == null)
            {
                error = "The phrase build plan is missing.";
                return false;
            }

            while (true)
            {
                string attemptError;
                var valid = ValidatePhraseTraceConflicts(plan.phrases, out attemptError) &&
                            VisemePhraseGlobalTrie.TryBuild(
                                plan, out _, out stateCount, out attemptError);
                if (valid && stateCount <= VisemePhraseBuildPlan.MaximumCompiledStates)
                    return true;
                if (valid)
                {
                    attemptError = $"The avatar-wide shared phrase matcher needs {stateCount} " +
                                   $"unique states; the maximum is " +
                                   $"{VisemePhraseBuildPlan.MaximumCompiledStates}.";
                }

                var removable = plan.phrases
                    .SelectMany((phrase, phraseIndex) => phrase.variants
                        .Select((variant, variantIndex) => new
                        {
                            phrase,
                            phraseIndex,
                            variant,
                            variantIndex
                        }))
                    .Where(item => item.variant != null &&
                                   item.variant.inferredContextPath &&
                                   item.phrase.variants.Count > 1)
                    .OrderByDescending(item =>
                        item.variant.inferredConfusionPath)
                    .ThenByDescending(item =>
                        item.variant.inferredTimingProfile)
                    .ThenByDescending(item =>
                        SourceVariantPriority(item.variant.id))
                    .ThenByDescending(item => item.variantIndex)
                    .ThenByDescending(item => item.phraseIndex)
                    .FirstOrDefault();
                if (removable == null)
                {
                    error = attemptError;
                    return false;
                }
                removable.phrase.variants.RemoveAt(removable.variantIndex);
            }
        }

        private static int SourceVariantPriority(string id)
        {
            if (string.IsNullOrEmpty(id) || id[0] != 'v') return int.MaxValue;
            var end = 1;
            while (end < id.Length && char.IsDigit(id[end])) end++;
            return end > 1 && int.TryParse(
                id.Substring(1, end - 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : int.MaxValue;
        }

        private static float[] NormalizeEmissions(VisemePhraseModelState state)
        {
            var output = new float[15];
            for (var viseme = 0; viseme < output.Length; viseme++)
                output[viseme] = VisemePhraseRuntimeLanguage.EmissionLikelihood(
                    state, viseme);
            return output;
        }

        private static bool ValidateRuntimeRectangle(
            VisemePhraseModelVariant variant,
            VisemePhraseRuntimeTimingRectangle rectangle,
            out string error)
        {
            error = null;
            if (rectangle == null ||
                rectangle.minimumDurationSeconds == null ||
                rectangle.maximumDurationSeconds == null ||
                rectangle.minimumDurationSeconds.Length != variant.states.Count ||
                rectangle.maximumDurationSeconds.Length != variant.states.Count)
            {
                error = "contains a malformed runtime timing rectangle.";
                return false;
            }
            if (rectangle.skippedStateIndex < -1 ||
                rectangle.skippedStateIndex >= variant.states.Count)
            {
                error = "contains a runtime timing rectangle with an invalid skipped state.";
                return false;
            }
            if (rectangle.skippedStateIndex >= 0 &&
                (variant.states[rectangle.skippedStateIndex] == null ||
                 !variant.states[rectangle.skippedStateIndex].allowSkip))
            {
                error = "contains a runtime timing rectangle with an unapproved skip.";
                return false;
            }

            var minimumSum = 0f;
            var maximumSum = 0f;
            for (var index = 0; index < variant.states.Count; index++)
            {
                if (variant.states[index] == null)
                {
                    error = "contains a missing compiled state.";
                    return false;
                }
                if (index == rectangle.skippedStateIndex) continue;
                var minimum = rectangle.minimumDurationSeconds[index];
                var maximum = rectangle.maximumDurationSeconds[index];
                if (float.IsNaN(minimum) || float.IsInfinity(minimum) ||
                    float.IsNaN(maximum) || float.IsInfinity(maximum) ||
                    minimum < 0f || maximum < minimum)
                {
                    error = "contains an invalid runtime timing interval.";
                    return false;
                }
                minimumSum += minimum;
                maximumSum += maximum;
            }

            const float tolerance = 0.0001f;
            var uncertainty = rectangle.includesRuntimeObservationUncertainty
                ? RuntimeObservationUncertaintySeconds
                : 0f;
            if (minimumSum + tolerance <
                    variant.minimumDurationSeconds - uncertainty ||
                maximumSum - tolerance >
                    variant.maximumDurationSeconds + uncertainty)
            {
                error = "contains a runtime timing rectangle outside its whole-phrase bounds.";
                return false;
            }
            return true;
        }

        private static bool ValidateExistingParameters(
            VRCAvatarDescriptor descriptor,
            IReadOnlyList<VisemePhraseBuildPhrase> phrases,
            out string error)
        {
            error = null;
            var avatarRoot = descriptor.gameObject;
            var catalog = AdvancedVisemeTrackingCatalog.Scan(avatarRoot, descriptor);
            var expression = catalog.Entries
                .Where(pair => pair.Value.expression != null)
                .ToDictionary(pair => pair.Key, pair => pair.Value.expression,
                    StringComparer.Ordinal);
            var newCarrierCount = 0;
            foreach (var phrase in phrases)
            {
                foreach (var output in new[]
                         {
                             phrase.matchedParameter,
                             phrase.confidenceParameter,
                             phrase.progressParameter,
                             phrase.carrierParameter
                         })
                {
                    if (!catalog.Entries.TryGetValue(output, out var outputEntry) ||
                        !outputEntry.expressionMetadataConflict) continue;
                    error = $"Expression parameter '{output}' is declared with " +
                            "conflicting saved or network-synced metadata by avatar features.";
                    return false;
                }

                if (!expression.TryGetValue(phrase.carrierParameter, out var existing))
                {
                    newCarrierCount++;
                }
                else if (existing.valueType != VRCExpressionParameters.ValueType.Bool ||
                         existing.saved ||
                         !existing.networkSynced)
                {
                    error = $"Expression parameter '{phrase.carrierParameter}' conflicts with " +
                            "the required unsaved, synced Bool phrase carrier.";
                    return false;
                }
                else
                {
                    phrase.declareCarrier = false;
                }

                if (!ValidateLocalExpression(
                        expression, phrase.matchedParameter,
                        VRCExpressionParameters.ValueType.Bool, out error) ||
                    !ValidateLocalExpression(
                        expression, phrase.confidenceParameter,
                        VRCExpressionParameters.ValueType.Float, out error) ||
                    !ValidateLocalExpression(
                        expression, phrase.progressParameter,
                        VRCExpressionParameters.ValueType.Float, out error))
                    return false;
            }

            var existingCost = expression.Values
                .Where(parameter => parameter.networkSynced)
                .Sum(parameter => parameter.valueType ==
                                  VRCExpressionParameters.ValueType.Bool ? 1 : 8);
            if (existingCost + newCarrierCount > VRCExpressionParameters.MAX_PARAMETER_COST)
            {
                error = $"Phrase carriers would use {existingCost + newCarrierCount} synced bits, " +
                        $"exceeding VRChat's {VRCExpressionParameters.MAX_PARAMETER_COST}-bit limit.";
                return false;
            }

            var expected = new Dictionary<string, AnimatorControllerParameterType>(StringComparer.Ordinal)
            {
                ["Viseme"] = AnimatorControllerParameterType.Int,
                ["IsLocal"] = AnimatorControllerParameterType.Float
            };
            foreach (var phrase in phrases)
            {
                expected[phrase.talkingParameter] = AnimatorControllerParameterType.Float;
                expected[phrase.onsetParameter] = AnimatorControllerParameterType.Float;
                expected[phrase.releaseParameter] = AnimatorControllerParameterType.Float;
                expected[phrase.matchedParameter] = AnimatorControllerParameterType.Bool;
                expected[phrase.confidenceParameter] = AnimatorControllerParameterType.Float;
                expected[phrase.progressParameter] = AnimatorControllerParameterType.Float;
                expected[phrase.carrierParameter] = AnimatorControllerParameterType.Bool;
            }

            foreach (var requirement in expected)
            {
                if (!catalog.Entries.TryGetValue(requirement.Key, out var entry)) continue;
                if (entry.expressionTypeConflict)
                {
                    error = $"Expression parameter '{requirement.Key}' is declared with " +
                            "conflicting types by avatar features.";
                    return false;
                }
                if (entry.animatorTypeConflict ||
                    entry.animatorType.HasValue && entry.animatorType.Value != requirement.Value)
                {
                    error = $"Animator parameter '{requirement.Key}' conflicts with the " +
                            $"required {requirement.Value} type in an avatar or referenced " +
                            "VRCFury controller.";
                    return false;
                }
            }
            return true;
        }

        private static bool ValidateLocalExpression(
            IReadOnlyDictionary<string, VRCExpressionParameters.Parameter> expression,
            string parameterName,
            VRCExpressionParameters.ValueType expectedType,
            out string error)
        {
            error = null;
            if (!expression.TryGetValue(parameterName, out var parameter)) return true;
            if (parameter.valueType == expectedType &&
                !parameter.networkSynced && !parameter.saved) return true;
            error = $"Expression parameter '{parameterName}' must remain an unsaved, " +
                    $"unsynced {expectedType} local output.";
            return false;
        }

        private static IEnumerable<AnimatorController> EnumerateAnimatorControllers(
            VRCAvatarDescriptor descriptor)
        {
            var seen = new HashSet<AnimatorController>();
            foreach (var layer in (descriptor.baseAnimationLayers ??
                                   Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                         .Concat(descriptor.specialAnimationLayers ??
                                 Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>()))
            {
                if (!(layer.animatorController is AnimatorController controller) ||
                    !seen.Add(controller)) continue;
                yield return controller;
            }
            var animatorController = descriptor.GetComponent<Animator>()?.runtimeAnimatorController
                as AnimatorController;
            if (animatorController != null && seen.Add(animatorController))
                yield return animatorController;
        }

        private static float FiniteOr(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

        private static string EnrollmentFingerprint(
            VisemePhraseEnrollmentProfile profile,
            VisemePhraseEnrollment enrollment)
        {
            var output = new StringBuilder();
            output.Append("profile:").Append(profile?.profileSchemaVersion ?? 0)
                .Append("|enrollment:").Append(enrollment?.enrollmentSchemaVersion ?? 0)
                .Append("|phrase:").Append(enrollment?.phraseId ?? string.Empty)
                .Append("|prompt:").Append(enrollment?.promptFingerprint ?? string.Empty);
            AppendTraces(output, "positive", enrollment?.positiveTakes);
            AppendTraces(output, "negative", enrollment?.negativeTraces);
            return AdvancedVisemeParameterContract.StableFingerprint(output.ToString());
        }

        private static void AppendTraces(
            StringBuilder output,
            string label,
            IReadOnlyList<VisemePhraseEnrollmentTrace> traces)
        {
            output.Append('|').Append(label).Append(':').Append(traces?.Count ?? 0);
            if (traces == null) return;
            for (var traceIndex = 0; traceIndex < traces.Count; traceIndex++)
            {
                var trace = traces[traceIndex];
                if (trace == null)
                {
                    output.Append("|null");
                    continue;
                }
                output.Append("|T:").Append(trace.traceSchemaVersion)
                    .Append(':').Append(trace.takeId ?? string.Empty)
                    .Append(':').Append(trace.recordedUtcTicks.ToString(
                        CultureInfo.InvariantCulture))
                    .Append(':').Append(trace.backend ?? string.Empty)
                    .Append(':').Append(trace.sampleRate)
                    .Append(':').Append(trace.durationSamples)
                    .Append(':').Append(trace.frames?.Count ?? 0);
                if (trace.frames == null) continue;
                foreach (var frame in trace.frames)
                {
                    if (frame == null)
                    {
                        output.Append("|F:null");
                        continue;
                    }
                    output.Append("|F:").Append(frame.sampleClock)
                        .Append(':').Append(frame.viseme)
                        .Append(':').Append(frame.voice.ToString(
                            "R", CultureInfo.InvariantCulture));
                }
            }
        }
    }
}
#endif
