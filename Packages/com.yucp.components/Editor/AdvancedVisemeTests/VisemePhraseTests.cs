using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using YUCP.Components.Editor.VisemePhrase;

namespace YUCP.Components.Editor.Tests
{
    public sealed class VisemePhraseTests
    {
        [Test]
        public void ParameterContract_SeparatesStableIdentityFromPublicKey()
        {
            var first = AdvancedVisemeParameterContract.StablePhraseId("  Hello   WORLD ");
            var second = AdvancedVisemeParameterContract.StablePhraseId("hello world");
            Assert.That(first, Is.EqualTo(second));
            Assert.That(
                AdvancedVisemeParameterContract.PhraseMatched(" Demo/Phrase/ ", "hello_world"),
                Is.EqualTo("Demo/Phrase/hello_world/Matched"));
            Assert.That(
                AdvancedVisemeParameterContract.PhraseCarrier("Demo/Phrase", first),
                Is.EqualTo("Demo/Phrase/_Network/" + first));
            Assert.That(
                AdvancedVisemeParameterContract.Viseme("", 10),
                Is.EqualTo("YUCP/AdvancedViseme/Viseme/aa"));
        }

        [Test]
        public void ComponentDefaults_PreserveAutomaticSourceAndClampNetworkCooldown()
        {
            var root = new GameObject("PhraseComponentTest");
            try
            {
                var component = root.AddComponent<VisemePhraseTriggerData>();
                component.sourcePrefix = "  ";
                component.phrases.Add(new VisemePhraseDefinition
                {
                    prompt = "wave hello",
                    cooldownSeconds = 0.1f
                });
                component.EnsureDefaults();

                Assert.That(component.NormalizedSourcePrefix, Is.Empty);
                Assert.That(component.sourcePrefix, Is.Empty);
                Assert.That(component.NormalizedPrefix, Is.EqualTo("YUCP/Phrase"));
                Assert.That(component.phrases[0].id, Does.StartWith("p_"));
                Assert.That(component.phrases[0].parameterKey, Is.EqualTo("wave_hello"));
                Assert.That(component.phrases[0].cooldownSeconds, Is.EqualTo(1.25f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EmptyPhraseDefaults_CreateIndependentPersistedIdsAndDeferPublicKey()
        {
            var firstRoot = new GameObject("First Phrase Component");
            var secondRoot = new GameObject("Second Phrase Component");
            try
            {
                var first = firstRoot.AddComponent<VisemePhraseTriggerData>();
                var second = secondRoot.AddComponent<VisemePhraseTriggerData>();
                first.phrases.Add(new VisemePhraseDefinition());
                second.phrases.Add(new VisemePhraseDefinition());
                first.EnsureDefaults();
                second.EnsureDefaults();

                Assert.That(first.phrases[0].id, Does.StartWith("p_"));
                Assert.That(second.phrases[0].id, Does.StartWith("p_"));
                Assert.That(first.phrases[0].id, Is.Not.EqualTo(second.phrases[0].id));
                Assert.That(first.phrases[0].parameterKey, Is.Empty);
                Assert.That(second.phrases[0].parameterKey, Is.Empty);

                var persistedId = first.phrases[0].id;
                first.phrases[0].prompt = "Open the portal";
                first.EnsureDefaults();
                Assert.That(first.phrases[0].id, Is.EqualTo(persistedId));
                Assert.That(first.phrases[0].parameterKey, Is.EqualTo("open_the_portal"));
            }
            finally
            {
                Object.DestroyImmediate(firstRoot);
                Object.DestroyImmediate(secondRoot);
            }
        }

        [Test]
        public void ProfileLookup_RequiresStableIdAndPromptFingerprint()
        {
            var profile = ScriptableObject.CreateInstance<VisemePhraseEnrollmentProfile>();
            try
            {
                var prompt = "open the menu";
                var id = AdvancedVisemeParameterContract.StablePhraseId(prompt);
                var fingerprint = AdvancedVisemeParameterContract.PromptFingerprint(prompt);
                var enrollment = profile.GetOrCreateEnrollment(id, fingerprint);
                enrollment.compiledModel = new VisemePhraseCompiledModel();

                Assert.That(profile.FindEnrollment(id, fingerprint), Is.SameAs(enrollment));
                Assert.That(profile.FindEnrollment(id, "changed"), Is.Null);
                Assert.That(profile.FindCompiledModel(id, fingerprint), Is.SameAs(enrollment.compiledModel));
                enrollment.compiledModel.modelSchemaVersion++;
                Assert.That(profile.FindCompiledModel(id, fingerprint), Is.Null);
                Assert.That(profile.FindCompiledModel(id, fingerprint, false), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ProfileLookup_RequiresEveryRawSchemaAndDoesNotRelabelOldProfiles()
        {
            var profile = ScriptableObject.CreateInstance<VisemePhraseEnrollmentProfile>();
            try
            {
                var definition = MakeDefinition("open the menu");
                var enrollment = profile.GetOrCreateEnrollment(
                    definition.id,
                    definition.PromptFingerprint);
                enrollment.positiveTakes.Add(MakeTrace("take", 1, 2, 3, 4));
                enrollment.compiledModel = new VisemePhraseCompiledModel();
                Assert.That(profile.FindCompiledModel(
                    definition.id,
                    definition.PromptFingerprint), Is.Not.Null);

                profile.profileSchemaVersion = 0;
                profile.EnsureDefaults();
                Assert.That(profile.profileSchemaVersion, Is.Zero,
                    "Validation must not silently relabel an old profile as current.");
                Assert.That(profile.FindCompiledModel(
                    definition.id,
                    definition.PromptFingerprint), Is.Null);

                profile.profileSchemaVersion = VisemePhraseEnrollmentProfile.CurrentProfileSchemaVersion;
                enrollment.enrollmentSchemaVersion = 0;
                Assert.That(profile.FindCompiledModel(
                    definition.id,
                    definition.PromptFingerprint), Is.Null);

                enrollment.enrollmentSchemaVersion = VisemePhraseEnrollment.CurrentEnrollmentSchemaVersion;
                enrollment.positiveTakes[0].traceSchemaVersion = 0;
                Assert.That(profile.FindCompiledModel(
                    definition.id,
                    definition.PromptFingerprint), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void TrimAndRle_UseSampleClockAndDoNotMutateRawTrace()
        {
            var raw = MakeTrace("take", 1, 1, 2, 2, 3, 3, 4, 4);
            var rawFirstClock = raw.frames[0].sampleClock;
            var rawCount = raw.frames.Count;

            var trimmed = VisemePhraseTraceMath.Trim(raw, 0.025f, 0f);
            var tokens = VisemePhraseTraceMath.RunLengthEncode(trimmed);

            Assert.That(tokens.Select(token => token.viseme), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(tokens, Has.All.Matches<VisemePhraseToken>(token => token.DurationSeconds > 0f));
            Assert.That(raw.frames.Count, Is.EqualTo(rawCount));
            Assert.That(raw.frames[0].sampleClock, Is.EqualTo(rawFirstClock));
            Assert.That(trimmed.recordedUtcTicks, Is.EqualTo(raw.recordedUtcTicks));
        }

        [Test]
        public void Trim_ReconstructsAdvancedVisemeTalkingHangoverFromRawFrames()
        {
            const int sampleRate = 48000;
            const long step = 1024L;
            var trace = new VisemePhraseEnrollmentTrace
            {
                sampleRate = sampleRate,
                durationSamples = 12 * step
            };
            trace.frames.Add(new VisemePhraseTraceFrame
            {
                sampleClock = 0L,
                viseme = 10,
                voice = 0.6f
            });
            for (var i = 1; i < 12; i++)
                trace.frames.Add(new VisemePhraseTraceFrame
                {
                    sampleClock = i * step,
                    viseme = 0,
                    voice = 0f
                });

            var presence = VisemePhraseTraceMath.ReconstructSpeechPresence(
                trace.frames,
                sampleRate);
            Assert.That(presence[0], Is.GreaterThan(
                VisemePhraseTraceMath.EnrollmentTalkingThreshold));
            Assert.That(presence[1], Is.GreaterThan(
                VisemePhraseTraceMath.EnrollmentTalkingThreshold));
            Assert.That(presence[presence.Length - 1], Is.LessThan(
                VisemePhraseTraceMath.EnrollmentTalkingThreshold));

            var trimmed = VisemePhraseTraceMath.Trim(trace, 0.025f, 0f);
            Assert.That(trimmed.frames.Count, Is.GreaterThan(1),
                "AVR Talking hangover should keep causal boundary frames in the trimmed trace.");
        }

        [Test]
        public void Rle_RemovesSubThirtyMillisecondAbaBouncesRegardlessOfVoiceAmplitude()
        {
            var lowShort = VisemePhraseTraceMath.RunLengthEncode(
                MakeBounceTrace(5600L, 0.04f));
            var confidentShort = VisemePhraseTraceMath.RunLengthEncode(
                MakeBounceTrace(5600L, 0.55f));
            var lowLong = VisemePhraseTraceMath.RunLengthEncode(
                MakeBounceTrace(6400L, 0.04f));

            Assert.That(lowShort.Select(token => token.viseme), Is.EqualTo(new[] { 1 }));
            Assert.That(confidentShort.Select(token => token.viseme),
                Is.EqualTo(new[] { 1 }),
                "Voice is microphone amplitude, not classifier confidence.");
            Assert.That(lowLong.Select(token => token.viseme),
                Is.EqualTo(new[] { 1, 2, 1 }));
        }

        [Test]
        public void TransientCleanupRemovesOneBlockAbcWinnerWithoutUsingVoice()
        {
            var tokens = TokensWithDurations(
                new[] { 1, 7, 3 },
                new[] { 0.08f, 1024f / 48000f, 0.09f });
            tokens[1].meanVoice = 1f;

            var cleaned = VisemePhraseTraceMath.RemoveTransientRuns(tokens);

            Assert.That(cleaned.Select(token => token.viseme),
                Is.EqualTo(new[] { 1, 3 }));
            Assert.That(cleaned.Sum(token => token.DurationSeconds),
                Is.EqualTo(tokens.Sum(token => token.DurationSeconds)).Within(1e-5f));
        }

        [Test]
        public void Validation_RequiresExactlyFourUsefulTakesAndThreeRunsEach()
        {
            var definition = MakeDefinition("check the avatar");
            var enrollment = MakeEnrollment(definition,
                new[] { 1, 2, 3, 4, 5, 6 },
                new[] { 1, 2, 3, 4, 5, 6 },
                new[] { 1, 2, 3, 4, 5, 6 });

            var threeTakeMessages = VisemePhraseValidation.Validate(definition, enrollment);
            Assert.That(threeTakeMessages.Any(message =>
                message.code == "positive_take_count" &&
                message.severity == VisemePhraseDiagnosticSeverity.Error), Is.True);

            enrollment.positiveTakes.Add(MakeTrace("take4", 1, 2));
            var shortMessages = VisemePhraseValidation.Validate(definition, enrollment);
            Assert.That(shortMessages.Any(message =>
                message.code == "take_too_few_runs" &&
                message.severity == VisemePhraseDiagnosticSeverity.Error), Is.True);
        }

        [Test]
        public void Compiler_AcceptsCleanThreeShapeCubeTakes()
        {
            var definition = MakeDefinition("Cube");
            definition.strictness = 0.65f;
            var sequence = new[] { 12, 14, 1 };
            var enrollment = MakeEnrollment(
                definition,
                sequence,
                sequence,
                sequence,
                sequence);

            var validation = VisemePhraseValidation.Validate(definition, enrollment);
            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(validation.Any(message =>
                message.severity == VisemePhraseDiagnosticSeverity.Error), Is.False,
                string.Join("\n", validation.Select(message => message.message)));
            Assert.That(validation.Any(message =>
                message.code == "take_low_distinctiveness"), Is.True,
                "A short visual signature should warn without blocking enrollment.");
            Assert.That(result.success, Is.True, Join(result.diagnostics));
            Assert.That(result.processedPositiveTakes, Has.All.Matches<List<VisemePhraseToken>>(
                take => take.Select(token => token.viseme).SequenceEqual(sequence)));
            Assert.That(result.processedPositiveTakes.All(take =>
                VisemePhraseModelCompiler.Score(result.model, take, false) <=
                result.model.acceptanceCost + 1e-5f), Is.True);
        }

        [Test]
        public void Compiler_RejectsNaturalPhraseWhenBoundaryCleanupLeavesTwoStates()
        {
            var definition = MakeDefinition("visually short command");
            var enrollment = MakeEnrollment(
                definition,
                new[] { 1, 2, 3 },
                new[] { 4, 2, 3 },
                new[] { 5, 2, 3 },
                new[] { 6, 2, 3 });

            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(result.success, Is.False);
            Assert.That(result.diagnostics.messages.Any(message =>
                message.code == "phrase_too_few_repeatable_runs" &&
                message.severity == VisemePhraseDiagnosticSeverity.Error), Is.True,
                Join(result.diagnostics));
        }

        [Test]
        public void ConstrainedDtw_IsFiniteSymmetricAndToleratesTimeWarping()
        {
            var reference = Tokens(1, 2, 3, 10, 11, 4);
            var stretched = Tokens(1, 2, 3, 10, 11, 4);
            stretched[2].endSeconds += 0.08f;
            var unrelated = Tokens(7, 6, 5, 14, 13, 8);

            var forward = VisemePhraseTraceMath.SoftDtwDistance(reference, stretched);
            var reverse = VisemePhraseTraceMath.SoftDtwDistance(stretched, reference);
            var negative = VisemePhraseTraceMath.SoftDtwDistance(reference, unrelated);

            Assert.That(float.IsNaN(forward) || float.IsInfinity(forward), Is.False);
            Assert.That(forward, Is.EqualTo(reverse).Within(1e-5f));
            Assert.That(forward, Is.LessThan(negative));
        }

        [Test]
        public void Compiler_TimingEnvelopeAcceptsHalfToDoubleTempoAndRejectsClearExtremes()
        {
            var definition = MakeDefinition("tempo invariant phrase");
            var sequence = new[] { 1, 4, 5, 10, 11, 2 };
            var enrollment = MakeEnrollment(
                definition, sequence, sequence, sequence, sequence);

            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(result.success, Is.True, Join(result.diagnostics));
            var baseline = result.processedPositiveTakes[0];
            foreach (var scale in new[] { 0.5f, 0.75f, 1f, 1.5f, 2f })
            {
                var stretched = ScaleTokens(baseline, scale);
                Assert.That(stretched.Select(token => token.viseme),
                    Is.EqualTo(baseline.Select(token => token.viseme)),
                    "Tempo scaling must not weaken token identity.");
                Assert.That(
                    VisemePhraseModelCompiler.Score(result.model, stretched, false),
                    Is.LessThanOrEqualTo(result.model.acceptanceCost),
                    $"A trained phrase at {scale:0.##}x duration should remain positive.");
            }

            foreach (var scale in new[] { 0.3f, 3f })
            {
                Assert.That(
                    VisemePhraseModelCompiler.Score(
                        result.model,
                        ScaleTokens(baseline, scale),
                        false),
                    Is.GreaterThan(result.model.acceptanceCost),
                    $"A clearly out-of-envelope phrase at {scale:0.##}x duration should be rejected.");
            }

            foreach (var variant in result.model.variants)
            {
                Assert.That(variant.minimumDurationSeconds,
                    Is.LessThanOrEqualTo(variant.medianDurationSeconds * 0.5f + 1e-5f));
                Assert.That(variant.maximumDurationSeconds,
                    Is.GreaterThanOrEqualTo(variant.medianDurationSeconds * 2f - 1e-5f));
                Assert.That(variant.minimumDurationSeconds,
                    Is.GreaterThan(variant.medianDurationSeconds * 0.4f));
                Assert.That(variant.maximumDurationSeconds,
                    Is.LessThan(variant.medianDurationSeconds * 2.5f));
                foreach (var state in variant.states)
                {
                    Assert.That(state.minimumDurationSeconds,
                        Is.LessThanOrEqualTo(state.medianDurationSeconds * 0.5f + 1e-5f));
                    Assert.That(state.maximumDurationSeconds,
                        Is.GreaterThanOrEqualTo(state.medianDurationSeconds * 2f - 1e-5f));
                }
            }
        }

        [Test]
        public void Compiler_BuildsBoundedDeterministicModelAndObservedAliases()
        {
            var definition = MakeDefinition("bring up the avatar menu");
            definition.strictness = 0.15f;
            var enrollment = MakeEnrollment(definition,
                new[] { 1, 4, 5, 10, 11, 2 },
                new[] { 1, 4, 5, 10, 11, 2 },
                new[] { 1, 8, 5, 10, 11, 2 },
                new[] { 1, 8, 5, 10, 11, 2 });

            var first = VisemePhraseModelCompiler.Compile(definition, enrollment);
            var second = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(first.success, Is.True, Join(first.diagnostics));
            Assert.That(first.model.variants.Count, Is.InRange(1, 2));
            Assert.That(first.model.variants, Has.All.Matches<VisemePhraseModelVariant>(
                variant => variant.states.Count <= VisemePhraseCompileOptions.MaximumStatesPerVariant));
            Assert.That(first.model.contentFingerprint, Is.EqualTo(second.model.contentFingerprint));
            Assert.That(first.model.variants.SelectMany(variant => variant.states)
                .Any(state => state.aliasVisemes.Length > 0), Is.True);
        }

        [Test]
        public void Compiler_AddsOnlyOneWeightedClassifierConfusionPerOptionalPath()
        {
            var definition = MakeDefinition("personalized varied phrase");
            definition.mode = VisemePhraseContextMode.PausedCommand;
            definition.strictness = 0.65f;
            var enrollment = MakeEnrollment(
                definition,
                new[] { 1, 10, 7, 12, 9, 2 },
                new[] { 1, 10, 7, 12, 5, 9, 2 },
                new[] { 14, 1, 10, 7, 12, 9, 2 },
                new[] { 1, 10, 7, 12, 9, 2, 4 });

            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(result.success, Is.True, Join(result.diagnostics));
            var inferred = result.model.variants
                .Where(variant => variant.inferredConfusionPath)
                .ToArray();
            Assert.That(inferred.Length, Is.InRange(1,
                VisemePhraseCompileOptions.MaximumConfusionPaths));
            Assert.That(inferred, Has.All.Matches<VisemePhraseModelVariant>(variant =>
                variant.inferredContextPath &&
                variant.inferencePenalty > 0f &&
                !string.IsNullOrEmpty(variant.confusionSourceSequence)));

            foreach (var variant in inferred)
            {
                var source = result.model.variants.First(candidate =>
                    !candidate.inferredConfusionPath &&
                    string.Join(",", candidate.states.Select(state => state.primaryViseme)) ==
                    variant.confusionSourceSequence);
                var changed = source.states.Zip(
                        variant.states,
                        (left, right) => left.primaryViseme != right.primaryViseme)
                    .Count(value => value);
                Assert.That(changed, Is.EqualTo(1),
                    "A generic path may spend exactly one weighted uncertainty operation.");
                CollectionAssert.AreEqual(
                    source.runtimeTimingRectangles.Select(rectangle =>
                        string.Join(",", rectangle.minimumDurationSeconds) + "|" +
                        string.Join(",", rectangle.maximumDurationSeconds)),
                    variant.runtimeTimingRectangles.Select(rectangle =>
                        string.Join(",", rectangle.minimumDurationSeconds) + "|" +
                        string.Join(",", rectangle.maximumDurationSeconds)));

                var sourceTake = result.processedPositiveTakes[variant.sourceTakeIndex];
                if (sourceTake.Count != variant.states.Count) continue;
                var live = TokensWithDurations(
                    variant.states.Select(state => state.primaryViseme).ToArray(),
                    sourceTake.Select(token => token.DurationSeconds).ToArray());
                Assert.That(VisemePhraseModelCompiler.Score(
                        result.model, live, false),
                    Is.LessThanOrEqualTo(result.model.acceptanceCost + 1e-5f));
            }

            var twoCorrections = Tokens(1, 10, 6, 11, 9, 2);
            Assert.That(VisemePhraseModelCompiler.Score(
                    result.model, twoCorrections, false),
                Is.GreaterThan(result.model.acceptanceCost),
                "Two unseen substitutions must not be accepted by combining optional paths.");
            Assert.That(result.diagnostics.messages.Any(message =>
                message.code == "confusion_paths_retained"), Is.True);
        }

        [Test]
        public void Compiler_BackgroundSpeechVetoesCollidingConfusionPath()
        {
            var definition = MakeDefinition("background guarded phrase");
            definition.mode = VisemePhraseContextMode.PausedCommand;
            definition.strictness = 0.65f;
            var basePath = new[] { 1, 10, 7, 12, 9, 2 };
            var enrollment = MakeEnrollment(
                definition,
                basePath,
                basePath,
                new[] { 3, 4, 5, 6, 7, 8 },
                new[] { 3, 4, 5, 6, 7, 9 });
            var collidingBackground = new[] { 1, 10, 7, 11, 9, 2 };
            enrollment.negativeTraces.Add(
                MakeTrace("ordinary-speech", collidingBackground));

            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(result.success, Is.True, Join(result.diagnostics));
            Assert.That(result.model.variants
                .Where(variant => variant.inferredConfusionPath)
                .Any(variant => variant.states.Select(state => state.primaryViseme)
                    .SequenceEqual(collidingBackground)), Is.False);
            var negative = VisemePhraseTraceMath.RemoveTransientRuns(
                VisemePhraseTraceMath.RunLengthEncode(
                    VisemePhraseTraceMath.Trim(enrollment.negativeTraces[0])));
            Assert.That(VisemePhraseModelCompiler.Score(
                    result.model, negative, true),
                Is.GreaterThan(result.model.acceptanceCost));
            Assert.That(result.diagnostics.messages.Any(message =>
                message.code == "confusion_path_pruned"), Is.True,
                Join(result.diagnostics));
        }

        [Test]
        public void Compiler_AcceptsRecordedCubeVariationWithoutDemandingIdenticalVisemes()
        {
            var definition = MakeDefinition("Cube");
            definition.strictness = 0.65f;
            var enrollment = new VisemePhraseEnrollment
            {
                phraseId = definition.id,
                promptFingerprint = definition.PromptFingerprint
            };
            enrollment.positiveTakes.Add(MakeBlockTrace("cube-1",
                (12, 4), (9, 1), (14, 8), (1, 9)));
            enrollment.positiveTakes.Add(MakeBlockTrace("cube-2",
                (1, 1), (12, 7), (14, 11), (5, 5), (9, 2)));
            enrollment.positiveTakes.Add(MakeBlockTrace("cube-3",
                (4, 3), (12, 4), (14, 7), (1, 1), (5, 7)));
            enrollment.positiveTakes.Add(MakeBlockTrace("cube-4",
                (4, 2), (12, 3), (9, 3), (14, 9), (10, 1), (1, 8)));

            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(result.success, Is.True, Join(result.diagnostics));
            Assert.That(result.processedPositiveTakes.Select(take =>
                    take.Select(token => token.viseme).ToArray()),
                Is.EqualTo(new[]
                {
                    new[] { 12, 14, 1 },
                    new[] { 12, 14, 5 },
                    new[] { 12, 14, 5 },
                    new[] { 12, 9, 14, 1 }
                }));
            Assert.That(result.model.variants.Count, Is.LessThanOrEqualTo(2));
            Assert.That(result.model.variants, Has.All.Matches<VisemePhraseModelVariant>(
                variant => variant.states.Count(state => state.allowSkip) <= 1));
            Assert.That(result.processedPositiveTakes.All(take =>
                VisemePhraseModelCompiler.Score(result.model, take, false) <=
                result.model.acceptanceCost + 1e-5f), Is.True);
            Assert.That(result.diagnostics.messages.Any(message =>
                message.code == "natural_boundary_normalized"), Is.True);
        }

        [Test]
        public void Compiler_ClusterSearchSkipsCheapestPartitionWhenRuntimeCannotBakeIt()
        {
            // The deliberately cheapest partition pairs A with B twice. Its
            // singleton substitutions are not a legal runtime alias, while the
            // next statistically credible partition groups the repeated A and
            // B pronunciations and is exactly bakeable.
            var distances = new[,]
            {
                { 0f, 0.1f, 0.2f, 0.8f },
                { 0.1f, 0f, 0.8f, 0.2f },
                { 0.2f, 0.8f, 0f, 0.1f },
                { 0.8f, 0.2f, 0.1f, 0f }
            };
            var a = Tokens(1, 2, 3, 4);
            var b = Tokens(1, 8, 3, 4);
            var positiveTakes = new List<List<VisemePhraseToken>>
            {
                a,
                b,
                Tokens(1, 2, 3, 4),
                Tokens(1, 8, 3, 4)
            };
            var rawTakes = Enumerable.Range(0, 4)
                .Select(index => new VisemePhraseEnrollmentTrace
                {
                    takeId = "partition-" + index
                })
                .ToList();

            var clusters = VisemePhraseModelCompiler.SelectRuntimeCompatibleClusters(
                distances,
                0.65f,
                new VisemePhraseCompileOptions(),
                positiveTakes,
                rawTakes);

            Assert.That(clusters.Count, Is.EqualTo(2));
            Assert.That(clusters[0], Is.EqualTo(new[] { 0, 2 }));
            Assert.That(clusters[1], Is.EqualTo(new[] { 1, 3 }));
        }

        [Test]
        public void Compiler_PreservesSingletonAlternativeAsExactPathWithoutBroadAlias()
        {
            var definition = MakeDefinition("alias evidence phrase");
            definition.strictness = 0f;
            var baseTake = new[] { 1, 2, 3, 4, 5, 6 };
            var alternative = new[] { 1, 8, 3, 4, 5, 6 };
            var enrollment = MakeEnrollment(
                definition, baseTake, baseTake, baseTake, alternative);
            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(result.success, Is.True, Join(result.diagnostics));
            Assert.That(result.model.variants.Count, Is.LessThanOrEqualTo(4));
            Assert.That(result.model.variants.SelectMany(variant => variant.states)
                .Any(state => state.aliasVisemes.Contains(8)), Is.False);
            Assert.That(result.processedPositiveTakes.All(take =>
                VisemePhraseModelCompiler.Score(result.model, take, false) <=
                result.model.acceptanceCost + 1e-5f), Is.True,
                "A singleton observation is an enrolled path, not permission to add its label " +
                "as a combinatorial alias to every compatible path.");
        }

        [Test]
        public void Compiler_SelectsAtMostOneLearnedDeletionPerVariant()
        {
            var definition = MakeDefinition("optional deletion phrase");
            definition.strictness = 0f;
            var longTake = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            var shortTake = new[] { 1, 2, 4, 5, 6, 7, 8 };
            var enrollment = MakeEnrollment(
                definition, longTake, longTake, shortTake, shortTake);

            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(result.success, Is.True, Join(result.diagnostics));
            Assert.That(result.model.variants, Has.All.Matches<VisemePhraseModelVariant>(variant =>
                variant.states.Count(state => state.allowSkip) <= 1));
            Assert.That(result.model.variants.SelectMany(variant => variant.states)
                .Count(state => state.allowSkip), Is.EqualTo(1));
            Assert.That(result.processedPositiveTakes.All(take =>
                VisemePhraseModelCompiler.Score(result.model, take, false) <=
                result.model.acceptanceCost + 1e-5f), Is.True);
        }

        [Test]
        public void Compiler_PreservesPronunciationWithTwoOmittedRunsAsExactPath()
        {
            var definition = MakeDefinition("two deletion phrase");
            definition.strictness = 0f;
            var longTake = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            var shortTake = new[] { 1, 2, 4, 5, 7, 8 };
            var enrollment = MakeEnrollment(
                definition, longTake, longTake, shortTake, shortTake);

            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(result.success, Is.True, Join(result.diagnostics));
            Assert.That(result.model.variants.Count, Is.LessThanOrEqualTo(4));
            Assert.That(result.model.variants, Has.All.Matches<VisemePhraseModelVariant>(variant =>
                variant.states.Count(state => state.allowSkip) <= 1));
            Assert.That(result.processedPositiveTakes.All(take =>
                VisemePhraseModelCompiler.Score(result.model, take, false) <=
                result.model.acceptanceCost + 1e-5f), Is.True,
                "Two omitted mouth shapes should select an enrolled path instead of widening " +
                "one template with two independent deletions.");
        }

        [Test]
        public void Compiler_EndpointOmissionBecomesExactPathInsteadOfOptionalSkip()
        {
            var definition = MakeDefinition("endpoint deletion phrase");
            definition.strictness = 0.65f;
            var longPath = new[] { 1, 4, 7, 10, 13 };
            var shortPath = new[] { 1, 4, 7, 10 };
            var enrollment = MakeEnrollment(
                definition, longPath, longPath, longPath, shortPath);

            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(result.success, Is.True, Join(result.diagnostics));
            Assert.That(result.model.variants.Any(variant =>
                    variant.states.Count > 0 &&
                    (variant.states[0].allowSkip ||
                     variant.states[variant.states.Count - 1].allowSkip)), Is.False,
                "A first/final omission must be represented as a complete pronunciation, " +
                "never an ambiguous optional prefix deletion.");
            Assert.That(result.model.variants.Any(variant =>
                variant.states.Select(state => state.primaryViseme)
                    .SequenceEqual(shortPath)), Is.True);
            Assert.That(result.processedPositiveTakes.All(take =>
                VisemePhraseModelCompiler.Score(result.model, take, false) <=
                result.model.acceptanceCost + 1e-5f), Is.True);
        }

        [Test]
        public void Compiler_PreservesLiveMancojoVariationAsBoundedEnrolledPaths()
        {
            var definition = MakeDefinition("Mancojo");
            definition.strictness = 0.65f;
            var enrollment = new VisemePhraseEnrollment
            {
                phraseId = definition.id,
                promptFingerprint = definition.PromptFingerprint
            };
            enrollment.positiveTakes.Add(MakeBlockTrace("mancojo-1",
                (1, 3), (10, 5), (5, 5), (8, 5), (5, 2), (8, 12)));
            enrollment.positiveTakes.Add(MakeBlockTrace("mancojo-2",
                (1, 6), (10, 4), (5, 6), (13, 4), (8, 2), (13, 3), (8, 10)));
            enrollment.positiveTakes.Add(MakeBlockTrace("mancojo-3",
                (1, 4), (10, 4), (5, 7), (13, 7), (8, 14)));
            enrollment.positiveTakes.Add(MakeBlockTrace("mancojo-4",
                (10, 6), (5, 4), (13, 6), (5, 2), (13, 4), (8, 9)));

            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(result.success, Is.True, Join(result.diagnostics));
            Assert.That(result.processedPositiveTakes.Select(take =>
                    take.Select(token => token.viseme).ToArray()),
                Is.EqualTo(new[]
                {
                    new[] { 1, 10, 5, 8, 5, 8 },
                    new[] { 1, 10, 5, 13, 8, 13, 8 },
                    new[] { 1, 10, 5, 13, 8 },
                    new[] { 10, 5, 13, 5, 13, 8 }
                }),
                "The regression must keep the stabilized hard-Viseme traces captured in Unity.");
            Assert.That(result.model.variants.Count(variant =>
                    !variant.inferredConfusionPath), Is.EqualTo(8),
                "The bounded order-two closure should retain four enrollment paths and " +
                "exactly four locally observed pronunciation bridges.");
            Assert.That(result.model.variants.Count, Is.LessThanOrEqualTo(
                VisemePhraseCompileOptions.MaximumRuntimePaths));
            Assert.That(result.model.variants.Count(variant =>
                    variant.inferredConfusionPath), Is.LessThanOrEqualTo(
                VisemePhraseCompileOptions.MaximumConfusionPaths),
                "Generic classifier guesses are separately bounded and must not displace " +
                "the personalized profile language.");
            Assert.That(result.model.variants.SelectMany(variant => variant.states)
                .Sum(state => state.aliasVisemes?.Length ?? 0), Is.Zero,
                "Singleton differences must stay correlated inside exact paths, not become broad aliases.");
            Assert.That(result.processedPositiveTakes.All(take =>
                VisemePhraseModelCompiler.Score(result.model, take, false) <=
                result.model.acceptanceCost + 1e-5f), Is.True,
                "Every natural enrollment take must replay through the exact baked language.");
            var liveRecombination = TokensWithDurations(
                new[] { 1, 10, 5, 13, 5, 13, 8 },
                new[] { 0.120f, 0.102f, 0.231f, 0.104f, 0.034f, 0.163f, 0.061f });
            Assert.That(VisemePhraseModelCompiler.Score(
                    result.model, liveRecombination, false),
                Is.LessThanOrEqualTo(result.model.acceptanceCost + 1e-5f),
                "A live prefix/suffix recombination whose every trigram and boundary " +
                "pair was enrolled must remain a valid pronunciation.");
            Assert.That(VisemePhraseModelCompiler.Score(
                    result.model,
                    Tokens(1, 10, 5, 13, 2, 13, 8),
                    false),
                Is.EqualTo(1f).Within(1e-5f),
                "A path containing an unseen local trigram must not be inferred.");
            Assert.That(result.diagnostics.messages.Any(message =>
                message.code == "context_paths_retained"), Is.True);
            Assert.That(result.diagnostics.messages.Any(message =>
                message.code == "take_runtime_nonreplay"), Is.False);
        }

        [Test]
        public void Compiler_AcceptsSupportedActiveMancojoBackoffWithNaturalPhoneTiming()
        {
            var definition = MakeDefinition("Mancojo");
            definition.strictness = 0.65f;
            var enrollment = new VisemePhraseEnrollment
            {
                phraseId = definition.id,
                promptFingerprint = definition.PromptFingerprint
            };
            // These are the four stabilized hard-Viseme paths from the active
            // enrollment. The long authored O in takes 1/4 intentionally makes
            // a singleton duration estimate hostile to the shorter live O.
            enrollment.positiveTakes.Add(MakeBlockTrace("active-mancojo-1",
                (10, 5), (5, 7), (13, 17), (8, 6)));
            enrollment.positiveTakes.Add(MakeBlockTrace("active-mancojo-2",
                (1, 5), (14, 5), (10, 5), (8, 5), (5, 7), (13, 5), (5, 6)));
            enrollment.positiveTakes.Add(MakeBlockTrace("active-mancojo-3",
                (1, 5), (14, 5), (10, 5), (8, 5), (5, 7), (13, 5), (5, 6), (8, 6)));
            enrollment.positiveTakes.Add(MakeBlockTrace("active-mancojo-4",
                (1, 5), (10, 5), (5, 11), (13, 17)));

            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(result.success, Is.True, Join(result.diagnostics));
            Assert.That(result.model.modelSchemaVersion,
                Is.EqualTo(VisemePhraseCompiledModel.CurrentModelSchemaVersion));
            Assert.That(result.processedPositiveTakes.Select(take =>
                    take.Select(token => token.viseme).ToArray()),
                Is.EqualTo(new[]
                {
                    new[] { 10, 5, 13, 8 },
                    new[] { 1, 14, 10, 8, 5, 13, 5 },
                    new[] { 1, 14, 10, 8, 5, 13, 5, 8 },
                    new[] { 1, 10, 5, 13 }
                }));

            var liveVisemes = new[] { 1, 10, 5, 13, 5, 13, 8 };
            var liveDurations = new[]
                { 0.120f, 0.102f, 0.231f, 0.104f, 0.034f, 0.163f, 0.061f };
            var liveVariant = result.model.variants.SingleOrDefault(variant =>
                variant.states.Select(state => state.primaryViseme)
                    .SequenceEqual(liveVisemes));
            Assert.That(liveVariant, Is.Not.Null,
                "The bounded language must reserve its supported single-ABA path for the " +
                "consonant-anchored cross-take pronunciation used at runtime.");
            Assert.That(liveVariant.inferredContextPath, Is.True);
            Assert.That(liveVariant.runtimeTimingRectangles, Has.Count.EqualTo(1));
            var rectangle = liveVariant.runtimeTimingRectangles.Single();
            Assert.That(rectangle.minimumDurationSeconds,
                Has.All.LessThanOrEqualTo(0.0201f),
                "Inferred phones use an analyzer-block debounce floor, not a singleton " +
                "enrollment duration as a hard gate.");
            Assert.That(rectangle.maximumDurationSeconds.All(value =>
                !float.IsNaN(value) && !float.IsInfinity(value) && value > 0.02f), Is.True);

            var live = TokensWithDurations(liveVisemes, liveDurations);
            Assert.That(VisemePhraseModelCompiler.Score(result.model, live, false),
                Is.LessThanOrEqualTo(result.model.acceptanceCost + 1e-5f),
                "The exact live stream that previously stopped at 3/8 progress must match.");
            Assert.That(VisemePhraseModelCompiler.Score(
                    result.model,
                    TokensWithDurations(
                        new[] { 1, 10, 5, 13, 5, 14, 8 }, liveDurations),
                    false),
                Is.EqualTo(VisemePhraseRuntimeLanguage.NoMatchCost).Within(1e-5f),
                "Relaxed timing must not admit an unsupported local Viseme transition.");
            Assert.That(result.processedPositiveTakes.All(take =>
                VisemePhraseModelCompiler.Score(result.model, take, false) <=
                result.model.acceptanceCost + 1e-5f), Is.True);

            var repeated = VisemePhraseModelCompiler.Compile(definition, enrollment);
            Assert.That(repeated.success, Is.True, Join(repeated.diagnostics));
            Assert.That(repeated.model.contentFingerprint,
                Is.EqualTo(result.model.contentFingerprint));
        }

        [Test]
        public void Compiler_RetainsDtwAlignedWasbeerSingleSpliceProfiles()
        {
            var definition = MakeDefinition("Wasbeer");
            definition.strictness = 0.65f;
            var enrollment = new VisemePhraseEnrollment
            {
                phraseId = definition.id,
                promptFingerprint = definition.PromptFingerprint
            };
            // Stabilized paths from the active creator enrollment. The short
            // PP run is present in the first three takes but absent in take 4.
            // A live prefix from take 2 plus take 4's suffix is therefore a
            // normal aligned pronunciation, not an unseen arbitrary trigram.
            enrollment.positiveTakes.Add(MakeBlockTrace("wasbeer-1",
                (11, 3), (14, 5), (10, 5), (7, 5), (1, 2), (12, 6), (9, 12)));
            enrollment.positiveTakes.Add(MakeBlockTrace("wasbeer-2",
                (13, 6), (10, 4), (7, 5), (1, 4), (12, 6), (9, 14)));
            enrollment.positiveTakes.Add(MakeBlockTrace("wasbeer-3",
                (9, 2), (10, 5), (7, 5), (1, 2), (12, 5), (9, 12)));
            enrollment.positiveTakes.Add(MakeBlockTrace("wasbeer-4",
                (9, 5), (11, 4), (7, 5), (12, 6), (9, 11)));

            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(result.success, Is.True, Join(result.diagnostics));
            var expected = new[]
            {
                new[] { 13, 10, 7, 12, 9 },
                new[] { 9, 11, 7, 1, 12, 9 }
            };
            foreach (var sequence in expected)
            {
                var variant = result.model.variants.SingleOrDefault(item =>
                    item.inferredContextPath &&
                    item.states.Select(state => state.primaryViseme)
                        .SequenceEqual(sequence));
                Assert.That(variant, Is.Not.Null,
                    "DTW-aligned enrolled prefixes and suffixes must be able to switch " +
                    "once at their shared SS anchor: " + string.Join(">", sequence));
                Assert.That(VisemePhraseModelCompiler.Score(
                        result.model, Tokens(sequence), false),
                    Is.LessThanOrEqualTo(result.model.acceptanceCost + 1e-5f));
            }
            Assert.That(result.model.variants.Count,
                Is.LessThanOrEqualTo(VisemePhraseCompileOptions.MaximumRuntimePaths));
            Assert.That(result.diagnostics.messages.Any(message =>
                message.code == "context_paths_retained"), Is.True);
        }

        [Test]
        public void Compiler_BlocksContextBridgeFoundInNegativeSpeech()
        {
            var definition = MakeDefinition("Mancojo");
            definition.strictness = 0.65f;
            var enrollment = new VisemePhraseEnrollment
            {
                phraseId = definition.id,
                promptFingerprint = definition.PromptFingerprint
            };
            enrollment.positiveTakes.Add(MakeBlockTrace("negative-guard-1",
                (10, 5), (5, 7), (13, 17), (8, 6)));
            enrollment.positiveTakes.Add(MakeBlockTrace("negative-guard-2",
                (1, 5), (14, 5), (10, 5), (8, 5), (5, 7), (13, 5), (5, 6)));
            enrollment.positiveTakes.Add(MakeBlockTrace("negative-guard-3",
                (1, 5), (14, 5), (10, 5), (8, 5), (5, 7), (13, 5), (5, 6), (8, 6)));
            enrollment.positiveTakes.Add(MakeBlockTrace("negative-guard-4",
                (1, 5), (10, 5), (5, 11), (13, 17)));
            enrollment.negativeTraces.Add(MakeBlockTrace("ordinary-speech-homophene",
                (1, 6), (10, 5), (5, 11), (13, 5), (5, 2), (13, 8), (8, 3)));

            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(result.success, Is.False,
                "A generalized context path must remain subordinate to negative-speech " +
                "calibration instead of becoming an unavoidable false positive.");
            Assert.That(result.diagnostics.messages.Any(message =>
                    message.code == "negative_runtime_margin" ||
                    message.code == "negative_runtime_match"), Is.True,
                Join(result.diagnostics));
        }

        [Test]
        public void Compiler_BlocksReducedEnrollmentThatCannotReplayInBakedLanguage()
        {
            var definition = MakeDefinition("long deliberately distinctive command");
            definition.strictness = 1f;
            var firstPattern = Enumerable.Range(1, 14).Select(value => value % 14 + 1).ToArray();
            var secondPattern = Enumerable.Range(1, 14).Select(value => 14 - value % 14).ToArray();
            var enrollment = MakeEnrollment(
                definition,
                firstPattern,
                firstPattern,
                secondPattern,
                secondPattern);
            var options = new VisemePhraseCompileOptions
            {
                minimumVariantImprovement = 0.01f,
                minimumVariantSeparation = 0.01f
            };

            var result = VisemePhraseModelCompiler.Compile(definition, enrollment, options);

            Assert.That(result.success, Is.False,
                "A DTW-reduced template must not be published when its source takes cannot replay exactly.");
            Assert.That(result.model.variants.Count, Is.LessThanOrEqualTo(2));
            Assert.That(result.model.variants, Has.All.Matches<VisemePhraseModelVariant>(
                variant => variant.states.Count <= 12));
            Assert.That(result.diagnostics.messages.Any(message =>
                message.code == "state_cap_applied"), Is.True);
            Assert.That(result.diagnostics.messages.Count(message =>
                message.code == "take_runtime_nonreplay"), Is.EqualTo(4));
        }

        [Test]
        public void RuntimeLanguage_RejectsDtwInsertionThatBakedMatcherCannotConsume()
        {
            var definition = MakeDefinition("exact baked language phrase");
            var sequence = new[] { 1, 4, 5, 10, 11, 2 };
            var enrollment = MakeEnrollment(
                definition, sequence, sequence, sequence, sequence);
            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(result.success, Is.True, Join(result.diagnostics));
            var enrolled = result.processedPositiveTakes[0];
            var inserted = InsertToken(enrolled, enrolled.Count / 2, 14);

            Assert.That(
                VisemePhraseTraceMath.SoftDtwDistance(enrolled, inserted),
                Is.LessThan(1f),
                "The editor DTW may assign a finite enrollment distance to an insertion.");
            Assert.That(
                VisemePhraseModelCompiler.Score(result.model, inserted, false),
                Is.GreaterThan(result.model.acceptanceCost),
                "The baked matcher has no insertion transition and must reject it.");
        }

        [Test]
        public void NegativeCalibration_SeparatesUnrelatedBackgroundWhenPossible()
        {
            var definition = MakeDefinition("please open settings");
            var enrollment = MakeEnrollment(definition,
                new[] { 1, 4, 5, 10, 11, 2 },
                new[] { 1, 4, 5, 10, 11, 2 },
                new[] { 1, 4, 5, 10, 11, 2 },
                new[] { 1, 4, 5, 10, 11, 2 });
            enrollment.negativeTraces.Add(MakeTrace(
                "negative", 7, 6, 14, 13, 8, 9, 7, 6, 14, 13, 8, 9));

            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(result.success, Is.True, Join(result.diagnostics));
            Assert.That(result.model.negativeCalibration.calibrated, Is.True);
            Assert.That(result.model.negativeCalibration.negativeTraceCount, Is.EqualTo(1));
            Assert.That(result.model.acceptanceCost, Is.InRange(0f, 1f));
        }

        [Test]
        public void NegativeCalibration_RejectsUnseenAliasCombinationWithRuntimeCost()
        {
            var definition = MakeDefinition("calibrated alias phrase");
            definition.strictness = 0f;
            var enrollment = MakeEnrollment(definition,
                new[] { 1, 2, 3, 4, 5, 6 },
                new[] { 1, 8, 3, 4, 12, 6 },
                new[] { 1, 8, 3, 9, 5, 6 },
                new[] { 1, 2, 3, 9, 12, 6 });
            enrollment.negativeTraces.Add(MakeTrace(
                "negative-alias-combination", 1, 8, 3, 9, 12, 6));

            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(result.success, Is.True, Join(result.diagnostics));
            Assert.That(result.model.variants.SelectMany(variant => variant.states)
                .Sum(state => state.aliasVisemes.Length), Is.EqualTo(3),
                "All aliases have two-take evidence and should remain available individually.");
            Assert.That(result.processedPositiveTakes.All(take =>
                VisemePhraseModelCompiler.Score(result.model, take, false) <=
                result.model.acceptanceCost + 1e-5f), Is.True,
                "Every enrollment take must replay under the exact baked threshold.");
            var negative = VisemePhraseTraceMath.RunLengthEncode(
                VisemePhraseTraceMath.Trim(enrollment.negativeTraces[0], 0.025f, 0.045f));
            Assert.That(
                VisemePhraseModelCompiler.Score(result.model, negative, true),
                Is.GreaterThan(result.model.acceptanceCost),
                "The unseen combination of three valid aliases must still be rejected.");
            Assert.That(result.model.negativeCalibration.calibrated, Is.True);
        }

        [Test]
        public void RuntimeTimingRectangles_AddSafeMultiTakeProfileBetweenCadences()
        {
            var first = RuntimeState(1, 0.05f, 0.3f);
            var second = RuntimeState(2, 0.05f, 0.3f);
            var variant = new VisemePhraseModelVariant
            {
                id = "correlated",
                minimumDurationSeconds = 0.29f,
                maximumDurationSeconds = 0.31f,
                states = new List<VisemePhraseModelState> { first, second }
            };
            var model = new VisemePhraseCompiledModel
            {
                acceptanceCost = 0.2f,
                variants = new List<VisemePhraseModelVariant> { variant }
            };
            var earlyFirst = TokensWithDurations(
                new[] { 1, 2 }, new[] { 0.1f, 0.2f });
            var lateFirst = TokensWithDurations(
                new[] { 1, 2 }, new[] { 0.2f, 0.1f });
            var positives = new List<List<VisemePhraseToken>>
            {
                earlyFirst, lateFirst, earlyFirst, lateFirst
            };

            VisemePhraseRuntimeLanguage.BuildPositiveTimingRectangles(
                model, positives, new List<VisemePhraseDiagnostic>());

            Assert.That(variant.runtimeTimingRectangles.Count, Is.EqualTo(3));
            Assert.That(variant.runtimeTimingRectangles.Count(rectangle =>
                rectangle.inferredProfile), Is.EqualTo(1));
            Assert.That(VisemePhraseRuntimeLanguage.Score(model, earlyFirst, false), Is.Zero);
            Assert.That(VisemePhraseRuntimeLanguage.Score(model, lateFirst, false), Is.Zero);
            var unseenCorrelation = TokensWithDurations(
                new[] { 1, 2 }, new[] { 0.15f, 0.15f });
            Assert.That(unseenCorrelation.Sum(token => token.DurationSeconds),
                Is.InRange(variant.minimumDurationSeconds, variant.maximumDurationSeconds));
            Assert.That(unseenCorrelation, Has.All.Matches<VisemePhraseToken>(token =>
                token.DurationSeconds >= first.minimumDurationSeconds &&
                token.DurationSeconds <= first.maximumDurationSeconds));
            Assert.That(
                VisemePhraseRuntimeLanguage.Score(model, unseenCorrelation, false),
                Is.Zero,
                "The log-median multi-take profile should accept a natural cadence " +
                "between two enrolled examples.");
            var extremeMix = TokensWithDurations(
                new[] { 1, 2 }, new[] { 0.05f, 0.25f });
            Assert.That(
                VisemePhraseRuntimeLanguage.Score(model, extremeMix, false),
                Is.EqualTo(VisemePhraseRuntimeLanguage.NoMatchCost),
                "Profile interpolation must not become an independent Cartesian " +
                "union of every state's broad bounds.");
        }

        [Test]
        public void InferredTimingProfileNeverReplacesObservedRectangle()
        {
            var observed = new VisemePhraseRuntimeTimingRectangle
            {
                sourceTakeIndex = 2,
                skippedStateIndex = -1,
                inferredProfile = false,
                minimumDurationSeconds = new[] { 0.10f, 0.12f },
                maximumDurationSeconds = new[] { 0.18f, 0.20f }
            };
            var profile = new VisemePhraseRuntimeTimingRectangle
            {
                sourceTakeIndex = -1,
                skippedStateIndex = -1,
                inferredProfile = true,
                minimumDurationSeconds = new[] { 0.08f, 0.10f },
                maximumDurationSeconds = new[] { 0.22f, 0.24f }
            };

            var rectangles = new List<VisemePhraseRuntimeTimingRectangle> { observed };
            VisemePhraseRuntimeLanguage.AddIfNotContained(rectangles, profile);

            Assert.That(rectangles, Has.Count.EqualTo(2));
            Assert.That(rectangles, Does.Contain(observed),
                "An optional interpolated lane must not replace a creator-observed take.");
            Assert.That(rectangles, Does.Contain(profile));
        }

        [Test]
        public void RuntimeScorerUsesTheSamePhraseWideObservationCorridorAsAnimator()
        {
            var variant = new VisemePhraseModelVariant
            {
                id = "v0",
                inferredContextPath = true,
                minimumDurationSeconds = 0.4f,
                maximumDurationSeconds = 0.4f
            };
            foreach (var viseme in new[] { 1, 10, 7, 12 })
                variant.states.Add(RuntimeState(viseme, 0.1f, 0.1f));
            variant.runtimeTimingRectangles.Add(
                new VisemePhraseRuntimeTimingRectangle
                {
                    sourceTakeIndex = 0,
                    skippedStateIndex = -1,
                    minimumDurationSeconds = Enumerable.Repeat(0.1f, 4).ToArray(),
                    maximumDurationSeconds = Enumerable.Repeat(0.1f, 4).ToArray()
                });
            var model = new VisemePhraseCompiledModel
            {
                variants = new List<VisemePhraseModelVariant> { variant }
            };
            var positives = new List<List<VisemePhraseToken>>
            {
                TokensWithDurations(
                    new[] { 1, 10, 7, 12 },
                    new[] { 0.1f, 0.1f, 0.1f, 0.1f })
            };

            VisemePhraseRuntimeLanguage.BuildPositiveTimingRectangles(
                model, positives, new List<VisemePhraseDiagnostic>());

            var rectangle = variant.runtimeTimingRectangles.Single();
            Assert.That(rectangle.includesRuntimeObservationUncertainty, Is.True);
            Assert.That(rectangle.maximumDurationSeconds.Sum(),
                Is.EqualTo(0.43f).Within(0.00001f));
            Assert.That(VisemePhraseModelCompiler.Score(
                    model,
                    TokensWithDurations(
                        new[] { 1, 10, 7, 12 },
                        new[] { 0.107f, 0.1f, 0.1f, 0.1f }),
                    false),
                Is.LessThan(VisemePhraseRuntimeLanguage.NoMatchCost),
                "Calibration must see a duration the generated Animator can accept.");
            Assert.That(VisemePhraseModelCompiler.Score(
                    model,
                    TokensWithDurations(
                        new[] { 1, 10, 7, 12 },
                        new[] { 0.109f, 0.1f, 0.1f, 0.1f }),
                    false),
                Is.EqualTo(VisemePhraseRuntimeLanguage.NoMatchCost),
                "The fixed phrase-wide allowance must not become an unbounded lane.");
        }

        [Test]
        public void ConfusionTimingUsesUniqueSourceVariantNotDuplicatePrimarySequence()
        {
            VisemePhraseModelVariant Source(
                string id,
                float duration,
                bool inferredContext)
            {
                var states = new[] { 1, 10, 7, 12 }
                    .Select(viseme => RuntimeState(viseme, duration, duration))
                    .ToList();
                return new VisemePhraseModelVariant
                {
                    id = id,
                    inferredContextPath = inferredContext,
                    minimumDurationSeconds = duration * states.Count,
                    maximumDurationSeconds = duration * states.Count,
                    states = states,
                    runtimeTimingRectangles = new List<
                        VisemePhraseRuntimeTimingRectangle>
                    {
                        new VisemePhraseRuntimeTimingRectangle
                        {
                            sourceTakeIndex = 0,
                            skippedStateIndex = -1,
                            minimumDurationSeconds = Enumerable.Repeat(
                                duration, states.Count).ToArray(),
                            maximumDurationSeconds = Enumerable.Repeat(
                                duration, states.Count).ToArray()
                        }
                    }
                };
            }

            var fast = Source("v0", 0.10f, false);
            var slow = Source("v1", 0.20f, true);
            var confusion = Source("v2", 0.20f, true);
            confusion.inferredConfusionPath = true;
            confusion.confusionSourceVariantId = "v1";
            confusion.confusionSourceSequence = "1,10,7,12";
            confusion.states[1] = RuntimeState(11, 0.20f, 0.20f);
            confusion.runtimeTimingRectangles.Clear();
            var model = new VisemePhraseCompiledModel
            {
                variants = new List<VisemePhraseModelVariant>
                    { fast, slow, confusion }
            };
            var positives = new List<List<VisemePhraseToken>>
            {
                TokensWithDurations(
                    new[] { 1, 10, 7, 12 },
                    new[] { 0.10f, 0.10f, 0.10f, 0.10f })
            };

            VisemePhraseRuntimeLanguage.BuildPositiveTimingRectangles(
                model, positives, new List<VisemePhraseDiagnostic>());

            Assert.That(confusion.runtimeTimingRectangles, Has.Count.EqualTo(1));
            CollectionAssert.AreEqual(
                slow.runtimeTimingRectangles.Single().maximumDurationSeconds,
                confusion.runtimeTimingRectangles.Single().maximumDurationSeconds,
                "Duplicate primary sequences must not redirect an inferred path to " +
                "another variant's timing lane.");
        }

        [Test]
        public void RuntimeLanguage_NormalizesAdjacentAliasesBeforeScoring()
        {
            var left = RuntimeState(1, 0.05f, 0.2f);
            left.aliasVisemes = new[] { 8 };
            left.aliasLikelihoods = new[] { 0.4f };
            left.emissionLikelihoods[8] = 0.4f;
            var right = RuntimeState(2, 0.05f, 0.2f);
            right.aliasVisemes = new[] { 8 };
            right.aliasLikelihoods = new[] { 0.8f };
            right.emissionLikelihoods[8] = 0.8f;
            var variant = new VisemePhraseModelVariant
            {
                id = "overlap",
                minimumDurationSeconds = 0.1f,
                maximumDurationSeconds = 0.4f,
                states = new List<VisemePhraseModelState> { left, right },
                runtimeTimingRectangles = new List<VisemePhraseRuntimeTimingRectangle>
                {
                    new VisemePhraseRuntimeTimingRectangle
                    {
                        skippedStateIndex = -1,
                        minimumDurationSeconds = new[] { 0.05f, 0.05f },
                        maximumDurationSeconds = new[] { 0.2f, 0.2f }
                    }
                }
            };
            var model = new VisemePhraseCompiledModel
            {
                acceptanceCost = 0.4f,
                variants = new List<VisemePhraseModelVariant> { variant }
            };

            Assert.That(VisemePhraseRuntimeLanguage.TryGetPathAliases(
                variant, -1, out var retained, out var aliases, out var error),
                Is.True, error);
            Assert.That(retained, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(aliases[0], Is.EqualTo(new[] { 1 }));
            Assert.That(aliases[1], Is.EqualTo(new[] { 2, 8 }));
            Assert.That(VisemePhraseRuntimeLanguage.Score(
                    model,
                    TokensWithDurations(new[] { 8, 2 }, new[] { 0.1f, 0.1f }),
                    false),
                Is.EqualTo(VisemePhraseRuntimeLanguage.NoMatchCost),
                "Scoring must reject the alias that the baked path assigns to its neighbor.");
            Assert.That(VisemePhraseRuntimeLanguage.Score(
                    model,
                    TokensWithDurations(new[] { 1, 8 }, new[] { 0.1f, 0.1f }),
                    false),
                Is.LessThanOrEqualTo(model.acceptanceCost));
        }

        [Test]
        public void CrossTakeValidation_WarnsWhileCompilerPreservesDistinctEnrolledPath()
        {
            var definition = MakeDefinition("consistent enrollment phrase");
            var enrollment = MakeEnrollment(definition,
                new[] { 1, 2, 3, 4, 5, 6 },
                new[] { 1, 2, 3, 4, 5, 6 },
                new[] { 1, 2, 3, 4, 5, 6 },
                new[] { 7, 8, 9, 10, 11, 12 });

            var messages = VisemePhraseValidation.Validate(definition, enrollment);
            var result = VisemePhraseModelCompiler.Compile(definition, enrollment);

            Assert.That(messages.Any(message =>
                message.code == "take_outlier"), Is.False,
                "A fixed pairwise cutoff must not block pronunciation modeling first.");
            Assert.That(messages.Any(message =>
                message.code == "enrollment_variability" &&
                message.severity == VisemePhraseDiagnosticSeverity.Warning), Is.True);
            Assert.That(result.success, Is.True, Join(result.diagnostics));
            Assert.That(result.model.variants.Count, Is.LessThanOrEqualTo(4));
            Assert.That(result.processedPositiveTakes.All(take =>
                VisemePhraseModelCompiler.Score(result.model, take, false) <=
                result.model.acceptanceCost + 1e-5f), Is.True);
            Assert.That(result.diagnostics.messages.Any(message =>
                message.code == "take_runtime_nonreplay"), Is.False,
                "A distinct but approved take must remain a correlated path instead of being blamed as bad speech.");
        }

        private static VisemePhraseDefinition MakeDefinition(string prompt)
        {
            var definition = new VisemePhraseDefinition { prompt = prompt };
            definition.EnsureDefaults();
            return definition;
        }

        private static VisemePhraseEnrollment MakeEnrollment(
            VisemePhraseDefinition definition,
            params int[][] takes)
        {
            var enrollment = new VisemePhraseEnrollment
            {
                phraseId = definition.id,
                promptFingerprint = definition.PromptFingerprint
            };
            for (var i = 0; i < takes.Length; i++)
                enrollment.positiveTakes.Add(MakeTrace("take" + i, takes[i]));
            return enrollment;
        }

        private static VisemePhraseEnrollmentTrace MakeTrace(string id, params int[] visemes)
        {
            const int sampleRate = 48000;
            const long step = 2400L;
            var trace = new VisemePhraseEnrollmentTrace
            {
                takeId = id,
                backend = "test",
                sampleRate = sampleRate,
                recordedUtcTicks = 123456789L
            };
            var sequence = new List<int> { 0 };
            sequence.AddRange(visemes);
            sequence.Add(0);
            for (var i = 0; i < sequence.Count; i++)
            {
                trace.frames.Add(new VisemePhraseTraceFrame
                {
                    sampleClock = i * step,
                    viseme = sequence[i],
                    voice = sequence[i] == 0 ? 0f : 0.65f
                });
            }
            trace.durationSamples = sequence.Count * step;
            return trace;
        }

        private static VisemePhraseEnrollmentTrace MakeBlockTrace(
            string id,
            params (int viseme, int blocks)[] runs)
        {
            const int sampleRate = 48000;
            const long step = 1024L;
            var trace = new VisemePhraseEnrollmentTrace
            {
                takeId = id,
                backend = "Oculus LipSync",
                sampleRate = sampleRate,
                recordedUtcTicks = 123456789L
            };
            var clock = 0L;
            for (var block = 0; block < 8; block++, clock += step)
                trace.frames.Add(new VisemePhraseTraceFrame
                {
                    sampleClock = clock,
                    viseme = 0,
                    voice = 0f
                });
            foreach (var run in runs)
            for (var block = 0; block < run.blocks; block++, clock += step)
                trace.frames.Add(new VisemePhraseTraceFrame
                {
                    sampleClock = clock,
                    viseme = run.viseme,
                    voice = 0.65f
                });
            for (var block = 0; block < 30; block++, clock += step)
                trace.frames.Add(new VisemePhraseTraceFrame
                {
                    sampleClock = clock,
                    viseme = 0,
                    voice = 0f
                });
            trace.durationSamples = clock;
            return trace;
        }

        private static VisemePhraseEnrollmentTrace MakeBounceTrace(
            long bounceEndClock,
            float bounceVoice)
        {
            var trace = new VisemePhraseEnrollmentTrace
            {
                takeId = "bounce",
                sampleRate = 48000,
                durationSamples = bounceEndClock + 4800L,
                frames = new List<VisemePhraseTraceFrame>
                {
                    new VisemePhraseTraceFrame { sampleClock = 0L, viseme = 1, voice = 0.6f },
                    new VisemePhraseTraceFrame { sampleClock = 2400L, viseme = 1, voice = 0.6f },
                    new VisemePhraseTraceFrame { sampleClock = 4800L, viseme = 2, voice = bounceVoice },
                    new VisemePhraseTraceFrame { sampleClock = bounceEndClock, viseme = 1, voice = 0.6f },
                    new VisemePhraseTraceFrame { sampleClock = bounceEndClock + 2400L, viseme = 1, voice = 0.6f }
                }
            };
            return trace;
        }

        private static List<VisemePhraseToken> Tokens(params int[] visemes)
        {
            var result = new List<VisemePhraseToken>();
            for (var i = 0; i < visemes.Length; i++)
            {
                result.Add(new VisemePhraseToken
                {
                    viseme = visemes[i],
                    startSeconds = i * 0.05f,
                    endSeconds = (i + 1) * 0.05f,
                    meanVoice = 0.6f,
                    frameCount = 1
                });
            }
            return result;
        }

        private static List<VisemePhraseToken> TokensWithDurations(
            IReadOnlyList<int> visemes,
            IReadOnlyList<float> durations)
        {
            Assert.That(durations.Count, Is.EqualTo(visemes.Count));
            var result = new List<VisemePhraseToken>(visemes.Count);
            var cursor = 0f;
            for (var i = 0; i < visemes.Count; i++)
            {
                result.Add(new VisemePhraseToken
                {
                    viseme = visemes[i],
                    startSeconds = cursor,
                    endSeconds = cursor + durations[i],
                    meanVoice = 0.6f,
                    frameCount = 1
                });
                cursor += durations[i];
            }
            return result;
        }

        private static List<VisemePhraseToken> InsertToken(
            IReadOnlyList<VisemePhraseToken> source,
            int index,
            int viseme)
        {
            var visemes = source.Select(token => token.viseme).ToList();
            var durations = source.Select(token => token.DurationSeconds).ToList();
            var duration = index > 0 && index <= durations.Count
                ? durations[index - 1]
                : 0.05f;
            visemes.Insert(index, viseme);
            durations.Insert(index, duration);
            return TokensWithDurations(visemes, durations);
        }

        private static VisemePhraseModelState RuntimeState(
            int viseme,
            float minimumDuration,
            float maximumDuration)
        {
            var likelihoods = new float[VisemePhraseTraceMath.VisemeCount];
            likelihoods[viseme] = 1f;
            return new VisemePhraseModelState
            {
                primaryViseme = viseme,
                minimumDurationSeconds = minimumDuration,
                maximumDurationSeconds = maximumDuration,
                medianDurationSeconds = (minimumDuration + maximumDuration) * 0.5f,
                emissionLikelihoods = likelihoods
            };
        }

        private static List<VisemePhraseToken> ScaleTokens(
            IReadOnlyList<VisemePhraseToken> source,
            float scale)
        {
            var result = new List<VisemePhraseToken>(source.Count);
            var cursor = 0f;
            for (var i = 0; i < source.Count; i++)
            {
                var duration = source[i].DurationSeconds * scale;
                result.Add(new VisemePhraseToken
                {
                    viseme = source[i].viseme,
                    startSeconds = cursor,
                    endSeconds = cursor + duration,
                    meanVoice = source[i].meanVoice,
                    frameCount = source[i].frameCount
                });
                cursor += duration;
            }
            return result;
        }

        private static string Join(VisemePhraseModelDiagnostics diagnostics)
        {
            return diagnostics == null
                ? "No diagnostics"
                : string.Join(" | ", diagnostics.messages.Select(message =>
                    message.code + ": " + message.message));
        }
    }
}
