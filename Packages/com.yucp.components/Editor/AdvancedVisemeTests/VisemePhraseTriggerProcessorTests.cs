#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using YUCP.Components.Editor.VisemePhrase;

namespace YUCP.Components.Editor.Tests
{
    public sealed class VisemePhraseTriggerProcessorTests
    {
        private string generatedFolder;
        private string personalFolder;

        [SetUp]
        public void SetUp()
        {
            EnsureFolder("Assets/YUCP/GeneratedAssets");
            EnsureFolder("Assets/YUCP/GeneratedAssets/__VisemePhraseTests");
            generatedFolder = "Assets/YUCP/GeneratedAssets/__VisemePhraseTests/" +
                              Guid.NewGuid().ToString("N");
            EnsureFolder(generatedFolder);
            EnsureFolder("Assets/YUCP/UserData/PhraseEnrollments");
            personalFolder = "Assets/YUCP/UserData/PhraseEnrollments/__Tests_" +
                             Guid.NewGuid().ToString("N");
            EnsureFolder(personalFolder);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(generatedFolder) &&
                AssetDatabase.IsValidFolder(generatedFolder))
                AssetDatabase.DeleteAsset(generatedFolder);
            if (!string.IsNullOrEmpty(personalFolder) &&
                AssetDatabase.IsValidFolder(personalFolder))
                AssetDatabase.DeleteAsset(personalFolder);
            const string generatedRoot =
                "Assets/YUCP/GeneratedAssets/__VisemePhraseTests";
            if (AssetDatabase.IsValidFolder(generatedRoot) &&
                AssetDatabase.GetSubFolders(generatedRoot).Length == 0)
                AssetDatabase.DeleteAsset(generatedRoot);
        }

        [Test]
        public void GuardAndGeneratorOrdersBracketAdvancedViseme()
        {
            Assert.That(new VisemePhraseTriggerPreflightProcessor().callbackOrder,
                Is.EqualTo(int.MinValue + 189));
            Assert.That(new AdvancedVisemeReconstructorProcessor().callbackOrder,
                Is.EqualTo(int.MinValue + 190));
            Assert.That(new VisemePhraseTriggerProcessor().callbackOrder,
                Is.EqualTo(int.MinValue + 191));
            Assert.That(new VisemePhraseBuildPlan().CanonicalFingerprint(),
                Does.StartWith("YUCP_VISPHRASE_CONTROLLER_V4_CONTRACT_"),
                "Generator semantic changes must invalidate content-addressed controllers.");
        }

        [TestCase(VRCAvatarDescriptor.LipSyncStyle.JawFlapBone)]
        [TestCase(VRCAvatarDescriptor.LipSyncStyle.JawFlapBlendShape)]
        public void PreflightRejectsJawFlapBeforeEnrollmentValidation(
            VRCAvatarDescriptor.LipSyncStyle style)
        {
            var root = new GameObject("Phrase jaw flap test");
            try
            {
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.lipSync = style;
                root.AddComponent<VisemePhraseTriggerData>();
                LogAssert.Expect(LogType.Error,
                    new System.Text.RegularExpressions.Regex(
                        "Viseme Phrase Trigger.*Jaw Flap"));
                Assert.That(new VisemePhraseTriggerPreflightProcessor()
                    .OnPreprocessAvatar(root), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PreflightRejectsUnsupportedProfileSchemaBeforeCompilation()
        {
            var root = new GameObject("Phrase schema test");
            try
            {
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.lipSync = VRCAvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
                root.AddComponent<AdvancedVisemeReconstructorData>();
                var trigger = root.AddComponent<VisemePhraseTriggerData>();
                trigger.enrollmentProfile = CreatePersonalProfile("SchemaProfile");
                trigger.enrollmentProfile.profileSchemaVersion =
                    VisemePhraseEnrollmentProfile.CurrentProfileSchemaVersion + 1;
                trigger.phrases.Add(new VisemePhraseDefinition
                {
                    id = "schema_phrase",
                    prompt = "schema phrase",
                    parameterKey = "schema_phrase"
                });

                Assert.That(VisemePhraseTriggerContractAdapter.TryCreatePlan(
                    root, descriptor, new[] { trigger }, out _, out var error), Is.False);
                Assert.That(error, Does.Contain("unsupported schema"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PreflightRejectsEnrollmentInheritedFromPrefabSource()
        {
            var profile = CreatePersonalProfile("InheritedProfile");
            var source = new GameObject("Phrase prefab source");
            GameObject instance = null;
            try
            {
                var descriptor = source.AddComponent<VRCAvatarDescriptor>();
                descriptor.lipSync = VRCAvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
                source.AddComponent<AdvancedVisemeReconstructorData>();
                var trigger = source.AddComponent<VisemePhraseTriggerData>();
                trigger.enrollmentProfile = profile;
                trigger.phrases.Add(new VisemePhraseDefinition
                {
                    id = "inherited_phrase",
                    prompt = "inherited phrase",
                    parameterKey = "inherited_phrase"
                });
                var prefab = PrefabUtility.SaveAsPrefabAsset(
                    source, generatedFolder + "/PhraseSource.prefab");
                UnityEngine.Object.DestroyImmediate(source);
                source = null;
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var instanceDescriptor = instance.GetComponent<VRCAvatarDescriptor>();
                var instanceTrigger = instance.GetComponent<VisemePhraseTriggerData>();

                Assert.That(VisemePhraseTriggerContractAdapter.TryCreatePlan(
                    instance, instanceDescriptor, new[] { instanceTrigger },
                    out _, out var error), Is.False);
                Assert.That(error, Does.Contain("inherited from prefab source"));
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                if (source != null) UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ConflictingCarrierMetadataFailsInEitherDeclarationOrder()
        {
            foreach (var reverse in new[] { false, true })
            {
                var root = new GameObject("Phrase metadata conflict");
                var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                try
                {
                    var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                    var phrase = Phrase("metadata", new[] { State(1, 0.04f, 0.16f) });
                    var valid = ExpressionParameter(
                        phrase.carrierParameter, false, true);
                    var conflict = ExpressionParameter(
                        phrase.carrierParameter, true, true);
                    parameters.parameters = reverse
                        ? new[] { conflict, valid }
                        : new[] { valid, conflict };
                    descriptor.expressionParameters = parameters;

                    Assert.That(InvokeExistingParameterValidation(
                        descriptor, new[] { phrase }, out var error), Is.False);
                    Assert.That(error, Does.Contain("conflicting saved or network-synced metadata"));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(parameters);
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        [Test]
        public void IdenticalCarrierMetadataDuplicatesReuseOneSyncedBit()
        {
            var root = new GameObject("Phrase metadata duplicate");
            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            try
            {
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                var phrase = Phrase("metadata_same", new[] { State(1, 0.04f, 0.16f) });
                parameters.parameters = new[]
                {
                    ExpressionParameter(phrase.carrierParameter, false, true),
                    ExpressionParameter(phrase.carrierParameter, false, true)
                };
                descriptor.expressionParameters = parameters;

                Assert.That(InvokeExistingParameterValidation(
                    descriptor, new[] { phrase }, out var error), Is.True, error);
                Assert.That(phrase.declareCarrier, Is.False,
                    "An identical existing carrier is reused instead of spending another bit.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parameters);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TimedSubsetPlannerPreservesDisjointTimingCandidatesOnSharedChild()
        {
            var plan = new VisemePhraseBuildPlan();
            plan.phrases.Add(Phrase("alpha", new[]
            {
                State(1, 0.04f, 0.11f), State(2, 0.05f, 0.16f),
                State(4, 0.05f, 0.16f)
            }));
            plan.phrases.Add(Phrase("beta", new[]
            {
                State(1, 0.09f, 0.24f), State(2, 0.05f, 0.16f),
                State(5, 0.05f, 0.17f)
            }));

            Assert.That(VisemePhraseGlobalTrie.TryBuild(
                plan, out var root, out var count, out var error), Is.True, error);
            Assert.That(root.children, Has.Count.EqualTo(1));
            var sharedFirst = root.children[0];
            Assert.That(sharedFirst.children, Has.Count.EqualTo(1),
                "The identical second token must stay one trie child.");
            var alphaId = root.candidates.Single(candidate => candidate.phraseIndex == 0).id;
            var betaId = root.candidates.Single(candidate => candidate.phraseIndex == 1).id;
            Assert.That(sharedFirst.candidateStates[alphaId].minimumSeconds,
                Is.EqualTo(0.04f).Within(1e-6f));
            Assert.That(sharedFirst.candidateStates[alphaId].maximumSeconds,
                Is.EqualTo(0.11f).Within(1e-6f));
            Assert.That(sharedFirst.candidateStates[betaId].minimumSeconds,
                Is.EqualTo(0.09f).Within(1e-6f));
            Assert.That(sharedFirst.candidateStates[betaId].maximumSeconds,
                Is.EqualTo(0.24f).Within(1e-6f));

            Assert.That(VisemePhraseTimedSubsetPlanner.TryPlan(
                root, plan, out var graph, out var plannedCount, out error), Is.True, error);
            Assert.That(count, Is.EqualTo(plannedCount));
            var first = graph.states.Single(state =>
                ReferenceEquals(state.node, sharedFirst) && !state.pausedSession);
            var sharedDestinations = first.advances.Where(advance =>
                    ReferenceEquals(advance.destination.node, sharedFirst.children[0]))
                .ToArray();
            Assert.That(sharedDestinations.Any(advance =>
                advance.destination.candidateIds.SequenceEqual(new[] { alphaId })), Is.True);
            Assert.That(sharedDestinations.Any(advance =>
                advance.destination.candidateIds.SequenceEqual(new[] { betaId })), Is.True);
            Assert.That(sharedDestinations.Any(advance =>
                advance.destination.candidateIds.SequenceEqual(
                    new[] { alphaId, betaId }.OrderBy(id => id))), Is.True,
                "Only the actual timing overlap may retain both candidates.");
        }

        [Test]
        public void RuntimeAliasNormalizationMatchesScorerIncludingSkipAdjacency()
        {
            var leftEmissions = new float[15];
            leftEmissions[1] = 1f;
            leftEmissions[2] = 0.2f;
            leftEmissions[4] = 0.8f;
            var middleEmissions = new float[15];
            middleEmissions[6] = 1f;
            var rightEmissions = new float[15];
            rightEmissions[2] = 1f;
            rightEmissions[1] = 0.2f;
            rightEmissions[4] = 0.3f;
            var source = new List<VisemePhraseModelState>
            {
                new VisemePhraseModelState
                {
                    primaryViseme = 1,
                    aliasVisemes = new[] { 2, 4 },
                    emissionLikelihoods = leftEmissions
                },
                new VisemePhraseModelState
                {
                    primaryViseme = 6,
                    allowSkip = true,
                    emissionLikelihoods = middleEmissions
                },
                new VisemePhraseModelState
                {
                    primaryViseme = 2,
                    aliasVisemes = new[] { 1, 4 },
                    emissionLikelihoods = rightEmissions
                }
            };
            var variant = new VisemePhraseModelVariant { states = source };
            Assert.That(VisemePhraseRuntimeLanguage.TryGetPathAliases(
                variant, 1, out var retained, out var aliases, out var error), Is.True, error);
            CollectionAssert.AreEqual(new[] { 0, 2 }, retained);
            Assert.That(aliases[0].Intersect(aliases[1]), Is.Empty,
                "One held raw viseme must never satisfy two adjacent learned states.");
            CollectionAssert.Contains(aliases[0], 1);
            CollectionAssert.Contains(aliases[0], 4,
                "The higher-likelihood shared alias stays on the left state.");
            CollectionAssert.Contains(aliases[1], 2);
            CollectionAssert.DoesNotContain(aliases[1], 4);
        }

        [Test]
        public void NaturalAndPausedShareSourceWhileCrossSourceStartFailsClosed()
        {
            var natural = Phrase("natural", new[]
            {
                State(1, 0.04f, 0.16f), State(2, 0.04f, 0.16f)
            });
            var paused = Phrase("paused_collision", new[]
            {
                State(1, 0.04f, 0.16f), State(3, 0.04f, 0.16f)
            });
            paused.leadingPauseSeconds = 0.18f;
            var contexts = new VisemePhraseBuildPlan();
            contexts.phrases.Add(natural);
            contexts.phrases.Add(paused);
            Assert.That(VisemePhraseGlobalTrie.TryBuild(
                contexts, out _, out _, out var contextError), Is.True, contextError);

            var built = Build(contexts);
            var matcher = built.controller.layers.Single(layer =>
                layer.name == "YUCP Phrase Shared Matcher").stateMachine;
            var matcherReady = StateNamed(matcher, "Ready");
            var modeEntries = matcherReady.transitions.Where(transition =>
                    transition.destinationState.name.StartsWith("Timed ",
                        StringComparison.Ordinal) &&
                    transition.destinationState.name.Contains(" depth 1 "))
                .ToArray();
            Assert.That(modeEntries.Any(transition => transition.destinationState.name
                .Contains(" paused ")), Is.True);
            Assert.That(modeEntries.Any(transition => transition.destinationState.name
                .Contains(" natural ")), Is.True);
            Assert.That(Array.FindIndex(modeEntries, transition => transition.destinationState.name
                    .Contains(" paused ")),
                Is.LessThan(Array.FindIndex(modeEntries, transition => transition.destinationState.name
                    .Contains(" natural "))),
                "A valid pause boundary must select the pause-bounded session first.");
            var pausedTerminal = matcher.states.Select(item => item.state).First(state =>
                state.name.Contains(" depth 2 [3] paused ") && state.transitions.Any(
                    transition => transition.destinationState != null &&
                                  transition.destinationState.name.StartsWith(
                                      "Emit paused_collision", StringComparison.Ordinal)));
            Assert.That(pausedTerminal, Is.Not.Null,
                "Paused terminals retain boundary provenance in their logical state identity.");
            Assert.That(matcher.states.Select(item => item.state)
                .Where(state => state.name.Contains(" depth 2 [3] natural "))
                .SelectMany(state => state.transitions)
                .Any(transition => transition.destinationState != null &&
                    transition.destinationState.name.StartsWith(
                        "Emit paused_collision", StringComparison.Ordinal)), Is.False);

            paused.leadingPauseSeconds = 0f;
            paused.sourcePrefix = "YUCP/SecondAVR";
            var sources = new VisemePhraseBuildPlan();
            sources.phrases.Add(natural);
            sources.phrases.Add(paused);
            Assert.That(VisemePhraseGlobalTrie.TryBuild(
                sources, out _, out _, out var sourceError), Is.False);
            Assert.That(sourceError, Does.Contain("multiple source prefixes"));
        }

        [Test]
        public void WeightedAliasBudgetRejectsOnlyTheCombinedLowLikelihoodPath()
        {
            var first = StateWithAliases(new[] { 1, 2 });
            first.emissionLikelihoods[1] = 1f;
            first.emissionLikelihoods[2] = 0.6f;
            var second = StateWithAliases(new[] { 3, 4 });
            second.emissionLikelihoods[3] = 1f;
            second.emissionLikelihoods[4] = 0.6f;
            var phrase = Phrase("weighted", new[]
            {
                first, second
            });
            phrase.runtimeAcceptanceCost = 0.3f;
            var plan = new VisemePhraseBuildPlan();
            plan.phrases.Add(phrase);
            Assert.That(VisemePhraseGlobalTrie.TryBuild(
                plan, out var root, out _, out var error), Is.True, error);
            Assert.That(root.candidates, Has.Count.EqualTo(3));
            CollectionAssert.AreEquivalent(new[] { 0f, 0.4f, 0.4f },
                root.candidates.Select(candidate => candidate.runtimePathCost)
                    .Select(value => (float)Math.Round(value, 3)));
            Assert.That(root.candidates.Any(candidate =>
                candidate.runtimePathCost > 0.600001f), Is.False,
                "Two 0.4 penalties exceed the calibrated 0.6 unnormalized budget.");
        }

        [Test]
        public void MaximumThirtyTwoLogicalStateConfigurationBuilds()
        {
            var sequences = new[]
            {
                new[] { 1, 5, 9, 13, 2, 6, 10, 14 },
                new[] { 2, 6, 10, 14, 3, 7, 11, 1 },
                new[] { 3, 7, 11, 1, 4, 8, 12, 2 },
                new[] { 4, 8, 12, 2, 5, 9, 13, 3 }
            };
            var plan = new VisemePhraseBuildPlan();
            for (var phraseIndex = 0; phraseIndex < sequences.Length; phraseIndex++)
                plan.phrases.Add(Phrase("limit" + phraseIndex,
                    sequences[phraseIndex].Select(viseme =>
                        State(viseme, 0.03f, 0.12f)).ToArray()));
            Assert.That(VisemePhraseGlobalTrie.TryBuild(
                plan, out _, out var count, out var error), Is.True, error);
            Assert.That(count, Is.EqualTo(VisemePhraseBuildPlan.MaximumCompiledStates));
            Assert.DoesNotThrow(() => Build(plan));
        }

        [Test]
        public void RuntimeBudgetPrunesOnlyOptionalContextPaths()
        {
            var sequences = new[]
            {
                new[] { 1, 5, 9, 13, 2, 6, 10, 14 },
                new[] { 2, 6, 10, 14, 3, 7, 11, 1 },
                new[] { 3, 7, 11, 1, 4, 8, 12, 2 },
                new[] { 4, 8, 12, 2, 5, 9, 13, 3 }
            };
            var plan = new VisemePhraseBuildPlan();
            for (var phraseIndex = 0; phraseIndex < sequences.Length; phraseIndex++)
                plan.phrases.Add(Phrase("fit" + phraseIndex,
                    sequences[phraseIndex].Select(viseme =>
                        State(viseme, 0.03f, 0.12f)).ToArray()));
            var optional = RuntimeVariant(
                "context_v1",
                new[] { 1, 5, 9, 13, 2, 6, 10, 14, 4 },
                Enumerable.Repeat(0.06f, 9).ToArray());
            optional.inferredContextPath = true;
            plan.phrases[0].variants.Add(optional);

            Assert.That(VisemePhraseGlobalTrie.TryBuild(
                plan, out _, out var before, out _), Is.False);
            Assert.That(before,
                Is.GreaterThan(VisemePhraseBuildPlan.MaximumCompiledStates));
            Assert.That(VisemePhraseTriggerContractAdapter.TryFitRuntimeLanguage(
                plan, out var fitted, out var error), Is.True, error);
            Assert.That(fitted, Is.EqualTo(VisemePhraseBuildPlan.MaximumCompiledStates));
            Assert.That(plan.phrases.SelectMany(phrase => phrase.variants),
                Has.All.Matches<VisemePhraseBuildVariant>(variant =>
                    !variant.inferredContextPath));
            Assert.That(plan.phrases, Has.All.Matches<VisemePhraseBuildPhrase>(phrase =>
                phrase.variants.Count == 1),
                "The four directly enrolled paths are protected while only the optional " +
                "context bridge is removed.");
        }

        [Test]
        public void RuntimeBudgetPrunesInferredCrossPhraseHomopheneButNotExactConflict()
        {
            var plan = new VisemePhraseBuildPlan();
            var first = Phrase("first", new[]
            {
                State(1, 0.03f, 0.12f), State(2, 0.03f, 0.12f),
                State(3, 0.03f, 0.12f)
            });
            var optional = RuntimeVariant(
                "context_collision", new[] { 4, 5, 6 },
                new[] { 0.06f, 0.06f, 0.06f });
            optional.inferredContextPath = true;
            first.variants.Add(optional);
            var second = Phrase("second", new[]
            {
                State(4, 0.03f, 0.12f), State(5, 0.03f, 0.12f),
                State(6, 0.03f, 0.12f)
            });
            plan.phrases.Add(first);
            plan.phrases.Add(second);

            Assert.That(VisemePhraseTriggerContractAdapter.TryFitRuntimeLanguage(
                plan, out _, out var error), Is.True, error);
            Assert.That(first.variants, Has.Count.EqualTo(1));
            Assert.That(first.variants.Single().inferredContextPath, Is.False);

            var exactConflict = new VisemePhraseBuildPlan();
            exactConflict.phrases.Add(Phrase("exact_a", new[]
            {
                State(7, 0.03f, 0.12f), State(8, 0.03f, 0.12f),
                State(9, 0.03f, 0.12f)
            }));
            exactConflict.phrases.Add(Phrase("exact_b", new[]
            {
                State(7, 0.03f, 0.12f), State(8, 0.03f, 0.12f),
                State(9, 0.03f, 0.12f)
            }));
            Assert.That(VisemePhraseTriggerContractAdapter.TryFitRuntimeLanguage(
                exactConflict, out _, out var exactError), Is.False);
            Assert.That(exactError, Does.Contain("same compiled viseme trace"));
            Assert.That(exactConflict.phrases,
                Has.All.Matches<VisemePhraseBuildPhrase>(phrase =>
                    phrase.variants.Count == 1));
        }

        [Test]
        public void ActiveMancojoEnrollmentKeepsLiveBridgeThroughAdapterAndControllerBuild()
        {
            var root = new GameObject("Active Mancojo adapter regression");
            try
            {
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.lipSync = VRCAvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
                root.AddComponent<AdvancedVisemeReconstructorData>();
                var trigger = root.AddComponent<VisemePhraseTriggerData>();
                trigger.enrollmentProfile = CreatePersonalProfile("ActiveMancojoProfile");
                var definition = new VisemePhraseDefinition
                {
                    prompt = "Mancojo",
                    strictness = 0.65f
                };
                definition.EnsureDefaults();
                trigger.phrases.Add(definition);
                var enrollment = trigger.enrollmentProfile.GetOrCreateEnrollment(
                    definition.id, definition.PromptFingerprint);
                enrollment.positiveTakes.Add(MakeBlockTrace("mancojo-1",
                    (10, 5), (5, 7), (13, 17), (8, 6)));
                enrollment.positiveTakes.Add(MakeBlockTrace("mancojo-2",
                    (1, 5), (14, 5), (10, 5), (8, 5), (5, 7), (13, 5), (5, 6)));
                enrollment.positiveTakes.Add(MakeBlockTrace("mancojo-3",
                    (1, 5), (14, 5), (10, 5), (8, 5), (5, 7), (13, 5), (5, 6), (8, 6)));
                enrollment.positiveTakes.Add(MakeBlockTrace("mancojo-4",
                    (1, 5), (10, 5), (5, 11), (13, 17)));
                var compiled = VisemePhraseModelCompiler.Compile(definition, enrollment);
                Assert.That(compiled.success, Is.True,
                    string.Join(" | ", compiled.diagnostics.messages.Select(message =>
                        message.code + ": " + message.message)));
                enrollment.compiledModel = compiled.model;
                enrollment.compiledModel.modelSchemaVersion =
                    VisemePhraseCompiledModel.CurrentModelSchemaVersion - 1;

                Assert.That(VisemePhraseTriggerContractAdapter.TryCreatePlan(
                    root, descriptor, new[] { trigger }, out var plan, out var error),
                    Is.True, error);
                var live = new[] { 1, 10, 5, 13, 5, 13, 8 };
                Assert.That(plan.phrases.Single().variants.Any(variant =>
                    variant.inferredContextPath &&
                    variant.states.Select(state => state.aliases.Single())
                        .SequenceEqual(live)), Is.True,
                    "The state-budget fitter must retain the highest-value live bridge.");
                Assert.That(enrollment.compiledModel.modelSchemaVersion,
                    Is.EqualTo(VisemePhraseCompiledModel.CurrentModelSchemaVersion),
                    "A package update should rebake complete current traces automatically, " +
                    "without opening the microphone wizard or asking for another take.");
                Assert.That(VisemePhraseGlobalTrie.TryBuild(
                    plan, out _, out var stateCount, out error), Is.True, error);
                Assert.That(stateCount,
                    Is.LessThanOrEqualTo(VisemePhraseBuildPlan.MaximumCompiledStates));
                Assert.That(Build(plan).controller, Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ActiveWasbeerSpliceProfilesSurviveAdapterAndDroppedAnalyzerRun()
        {
            var root = new GameObject("Active Wasbeer adapter regression");
            try
            {
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.lipSync = VRCAvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
                root.AddComponent<AdvancedVisemeReconstructorData>();
                var trigger = root.AddComponent<VisemePhraseTriggerData>();
                trigger.enrollmentProfile = CreatePersonalProfile("ActiveWasbeerProfile");
                var definition = new VisemePhraseDefinition
                {
                    prompt = "Wasbeer",
                    strictness = 0.65f
                };
                definition.EnsureDefaults();
                trigger.phrases.Add(definition);
                var enrollment = trigger.enrollmentProfile.GetOrCreateEnrollment(
                    definition.id, definition.PromptFingerprint);
                enrollment.positiveTakes.Add(MakeBlockTrace("wasbeer-1",
                    (11, 3), (14, 5), (10, 5), (7, 5), (1, 2), (12, 6), (9, 12)));
                enrollment.positiveTakes.Add(MakeBlockTrace("wasbeer-2",
                    (13, 6), (10, 4), (7, 5), (1, 2), (12, 6), (9, 14)));
                enrollment.positiveTakes.Add(MakeBlockTrace("wasbeer-3",
                    (9, 2), (10, 5), (7, 5), (1, 2), (12, 5), (9, 12)));
                enrollment.positiveTakes.Add(MakeBlockTrace("wasbeer-4",
                    (9, 5), (11, 4), (7, 5), (12, 6), (9, 11)));

                Assert.That(VisemePhraseTriggerContractAdapter.TryCreatePlan(
                    root, descriptor, new[] { trigger }, out var plan, out var error),
                    Is.True, error);
                var expected = new[]
                {
                    new[] { 13, 10, 7, 12, 9 },
                    new[] { 9, 11, 7, 1, 12, 9 }
                };
                foreach (var sequence in expected)
                    Assert.That(plan.phrases.Single().variants.Any(variant =>
                        variant.inferredContextPath &&
                        variant.states.Select(state => state.aliases.Single())
                            .SequenceEqual(sequence)), Is.True,
                        "The post-adapter state budget must retain supported profile path " +
                        string.Join(">", sequence) + ". Retained: " +
                        string.Join(" | ", plan.phrases.Single().variants.Select(variant =>
                            (variant.inferredContextPath ? "I:" : "E:") +
                            string.Join(">", variant.states.Select(state =>
                                state.aliases.Single())))));

                // Enrollment sees every 21.33 ms analyzer block, while the
                // Animator samples at render frames. Enumerate relative frame
                // phases and prove that a real 20 FPS phase drops the two-block
                // PP without dropping the surrounding stable phones.
                var source = new[] { 13, 10, 7, 1, 12, 9 };
                var durations = new[]
                {
                    6f * 1024f / 48000f,
                    4f * 1024f / 48000f,
                    5f * 1024f / 48000f,
                    2f * 1024f / 48000f,
                    6f * 1024f / 48000f,
                    14f * 1024f / 48000f
                };
                var dropped = Enumerable.Range(0, 120)
                    .Select(index => SampleAtFramePhase(
                        source, durations, 20f, index / 120f / 20f))
                    .FirstOrDefault(sequence =>
                        sequence.SequenceEqual(expected[0]));
                Assert.That(dropped, Is.Not.Null,
                    "The regression must exercise a real asynchronous phase that misses " +
                    "the two-block classifier run instead of rounding it up to one frame.");
                Assert.That(Build(plan).controller, Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void LogicalAndGeneratedTimingSegmentCapsFailClosed()
        {
            var sequences = new[]
            {
                new[] { 1, 5, 9, 13, 2, 6, 10, 14, 4 },
                new[] { 2, 6, 10, 14, 3, 7, 11, 1 },
                new[] { 3, 7, 11, 1, 4, 8, 12, 2 },
                new[] { 4, 8, 12, 2, 5, 9, 13, 3 }
            };
            var logicalOverflow = new VisemePhraseBuildPlan();
            for (var index = 0; index < sequences.Length; index++)
                logicalOverflow.phrases.Add(Phrase("overflow" + index,
                    sequences[index].Select(viseme =>
                        State(viseme, 0.03f, 0.12f)).ToArray()));
            Assert.That(VisemePhraseGlobalTrie.TryBuild(
                logicalOverflow, out _, out var logicalCount, out var logicalError), Is.False);
            Assert.That(logicalCount,
                Is.GreaterThan(VisemePhraseBuildPlan.MaximumCompiledStates));
            Assert.That(logicalError, Does.Contain("logical states"));

            var timingOverflow = new VisemePhraseBuildPlan();
            var phrase = Phrase("timing_overflow", new[] { State(1, 0.001f, 1f) });
            phrase.variants.Clear();
            for (var index = 0; index < 65; index++)
            {
                var state = State(1, 0.001f + index * 0.002f,
                    1f + index * 0.002f);
                var variant = new VisemePhraseBuildVariant
                {
                    id = "rectangle_" + index,
                    canonicalStateCount = 1,
                    minimumTotalSeconds = state.minimumSeconds,
                    maximumTotalSeconds = state.maximumSeconds
                };
                variant.states.Add(state);
                phrase.variants.Add(variant);
            }
            timingOverflow.phrases.Add(phrase);
            Assert.That(VisemePhraseGlobalTrie.TryBuild(
                timingOverflow, out _, out _, out var timingError), Is.False);
            Assert.That(timingError, Does.Contain("generated timing segments"));
        }

        [Test]
        public void ConcreteOptionalSkipPrefixAmbiguityFailsClosed()
        {
            var phrase = Phrase("optional_prefix", new[]
            {
                State(1, 0.04f, 0.16f), State(2, 0.04f, 0.16f)
            });
            phrase.variants.Clear();
            var skipped = new VisemePhraseBuildVariant
            {
                id = "a_skipped",
                canonicalStateCount = 2,
                runtimeBaseCost = 0.2f
            };
            skipped.states.Add(State(2, 0.04f, 0.16f));
            var longer = new VisemePhraseBuildVariant
            {
                id = "b_longer",
                canonicalStateCount = 2
            };
            longer.states.Add(State(2, 0.04f, 0.16f));
            longer.states.Add(State(3, 0.04f, 0.16f));
            phrase.variants.Add(skipped);
            phrase.variants.Add(longer);
            var plan = new VisemePhraseBuildPlan();
            plan.phrases.Add(phrase);
            Assert.That(VisemePhraseGlobalTrie.TryBuild(
                plan, out _, out _, out var error), Is.False);
            Assert.That(error, Does.Contain("prefix"));
        }

        [Test]
        public void GeneratedControllerUsesExactExitTimeSegmentsWithoutAnyState()
        {
            var plan = new VisemePhraseBuildPlan();
            plan.phrases.Add(Phrase("hello", new[]
            {
                State(1, 0.05f, 0.20f), State(2, 0.06f, 0.22f)
            }));
            var built = Build(plan);
            var matcher = built.controller.layers.Single(layer =>
                layer.name == "YUCP Phrase Shared Matcher").stateMachine;
            Assert.That(built.controller.layers.Count(layer =>
                layer.name == "YUCP Phrase Shared Matcher"), Is.EqualTo(1));
            AssertNoAnyState(built.controller);

            var firstGate = matcher.states.Select(item => item.state).FirstOrDefault(state =>
                state.name.Contains("_0 depth 1 [1] natural "));
            Assert.That(firstGate, Is.Not.Null);
            Assert.That(firstGate.motion.averageDuration,
                Is.EqualTo(0.05f).Within(0.001f));
            var heldCompletion = firstGate.transitions.Single(transition =>
                transition.hasExitTime &&
                transition.destinationState.name.Contains("_1 depth 1 [1] natural ") &&
                HasCondition(transition, "Viseme", AnimatorConditionMode.Equals, 1f));
            Assert.That(heldCompletion.exitTime, Is.EqualTo(1f));
            var boundaryAdvance = firstGate.transitions.FirstOrDefault(transition =>
                transition.hasExitTime && transition.destinationState != null &&
                transition.destinationState.name.Contains(" depth 2 [2] ") &&
                HasCondition(transition, "Viseme", AnimatorConditionMode.Equals, 2f));
            Assert.That(boundaryAdvance, Is.Not.Null,
                "The first sample at the learned minimum must advance directly.");
            var earlyChange = firstGate.transitions.FirstOrDefault(transition =>
                !transition.hasExitTime && HasCondition(
                    transition, "Viseme", AnimatorConditionMode.Equals, 2f));
            Assert.That(earlyChange, Is.Not.Null,
                "A next token arriving before the minimum hold must fail immediately.");
            Assert.That(earlyChange.destinationState.name, Is.EqualTo("Ready"));

            var terminal = matcher.states.Select(item => item.state).Single(state =>
                state.name.Contains("_1 depth 2 [2] natural ") &&
                state.transitions.Any(transition => transition.destinationState != null &&
                    transition.destinationState.name.StartsWith("Emit hello",
                        StringComparison.Ordinal)));
            var silenceTransitions = terminal.transitions
                .Where(transition => HasCondition(
                    transition, "Viseme", AnimatorConditionMode.Equals, 0f))
                .ToArray();
            Assert.That(silenceTransitions.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(silenceTransitions[0].destinationState.name,
                Does.StartWith("Emit hello"),
                "Terminal acceptance must beat the same-frame sil failure link.");
            Assert.That(terminal.transitions.Any(transition =>
                transition.hasExitTime && transition.destinationState.name == "Ready"), Is.True,
                "Holding the terminal beyond its maximum must reject it.");
        }

        [Test]
        public void FailureLinksConsumeObservationAndUseLongestSafePrefix()
        {
            var plan = new VisemePhraseBuildPlan();
            plan.phrases.Add(Phrase("outer", new[]
            {
                State(1, 0.04f, 0.16f), State(2, 0.04f, 0.16f),
                State(4, 0.04f, 0.16f)
            }));
            plan.phrases.Add(Phrase("suffix", new[]
            {
                State(2, 0.04f, 0.16f), State(3, 0.04f, 0.16f),
                State(5, 0.04f, 0.16f)
            }));
            plan.phrases.Add(Phrase("restart", new[]
            {
                State(7, 0.04f, 0.16f), State(8, 0.04f, 0.16f)
            }));
            var built = Build(plan);
            var matcher = built.controller.layers.Single(layer =>
                layer.name == "YUCP Phrase Shared Matcher").stateMachine;

            var outerSecond = matcher.states.Select(item => item.state).Single(state =>
                state.name.Contains(" depth 2 [2] natural ") &&
                state.name.EndsWith("candidates 0", StringComparison.Ordinal));
            var sameObservationFallbacks = outerSecond.transitions.Where(transition =>
                HasCondition(transition, "Viseme", AnimatorConditionMode.Equals, 3f)).ToArray();
            var longestIndex = Array.FindIndex(sameObservationFallbacks, transition =>
                transition.destinationState != null && transition.destinationState.name
                    .Contains(" depth 2 [3] natural "));
            Assert.That(longestIndex, Is.EqualTo(0),
                "[1,2] + 3 must retain the already observed [2,3] suffix before Ready.");

            var outerFirst = matcher.states.Select(item => item.state).Single(state =>
                state.name.Contains(" depth 1 [1] natural ") &&
                state.name.EndsWith("candidates 0", StringComparison.Ordinal));
            Assert.That(outerFirst.transitions.Any(transition =>
                HasCondition(transition, "Viseme", AnimatorConditionMode.Equals, 7f) &&
                transition.destinationState != null && transition.destinationState.name
                    .Contains(" depth 1 [7] natural ")), Is.True,
                "A one-frame token that starts another phrase must bypass Ready.");

            var selfOverlap = new VisemePhraseBuildPlan();
            selfOverlap.phrases.Add(Phrase("ababc", new[]
            {
                State(1, 0.04f, 0.16f), State(2, 0.04f, 0.16f),
                State(1, 0.04f, 0.16f), State(2, 0.04f, 0.16f),
                State(3, 0.04f, 0.16f)
            }));
            built = Build(selfOverlap);
            matcher = built.controller.layers.Single(layer =>
                layer.name == "YUCP Phrase Shared Matcher").stateMachine;
            var abab = matcher.states.Select(item => item.state).Single(state =>
                state.name.Contains(" depth 4 [2] natural ") &&
                state.name.EndsWith("candidates 0", StringComparison.Ordinal));
            Assert.That(abab.transitions.Where(transition =>
                    HasCondition(transition, "Viseme", AnimatorConditionMode.Equals, 1f))
                .First().destinationState.name, Does.Contain(" depth 3 [1] natural "),
                "ABAB + A must fall to ABA so ABABABC still contains ABABC.");
        }

        [Test]
        public void SamePhraseMayRetainARecordedPathThatPrefixesAnother()
        {
            var plan = new VisemePhraseBuildPlan();
            var phrase = Phrase("natural_variants", new[]
            {
                State(1, 0.04f, 0.16f), State(10, 0.04f, 0.16f),
                State(5, 0.04f, 0.16f), State(13, 0.04f, 0.16f),
                State(8, 0.04f, 0.16f)
            });
            var longer = new VisemePhraseBuildVariant
            {
                id = "v1",
                canonicalStateCount = 7,
                minimumTotalSeconds = 0.28f,
                maximumTotalSeconds = 1.12f
            };
            longer.states.AddRange(new[]
            {
                State(1, 0.04f, 0.16f), State(10, 0.04f, 0.16f),
                State(5, 0.04f, 0.16f), State(13, 0.04f, 0.16f),
                State(8, 0.04f, 0.16f), State(13, 0.04f, 0.16f),
                State(8, 0.04f, 0.16f)
            });
            phrase.variants.Add(longer);
            plan.phrases.Add(phrase);

            Assert.That(VisemePhraseGlobalTrie.TryBuild(
                plan, out _, out var logicalStates, out var error), Is.True, error);
            Assert.That(logicalStates,
                Is.LessThanOrEqualTo(VisemePhraseBuildPlan.MaximumCompiledStates));

            var built = Build(plan);
            var matcher = built.controller.layers.Single(layer =>
                layer.name == "YUCP Phrase Shared Matcher").stateMachine;
            var sharedTerminal = matcher.states.Select(item => item.state)
                .Where(state => state.name.Contains(" depth 5 [8] natural "))
                .First(state => state.transitions.Any(transition =>
                    HasCondition(transition, "Viseme", AnimatorConditionMode.Equals, 13f)));
            Assert.That(sharedTerminal.transitions.Any(transition =>
                HasCondition(transition, "Viseme", AnimatorConditionMode.Equals, 13f) &&
                transition.destinationState != null &&
                transition.destinationState.name.Contains(" depth 6 [13] natural ")), Is.True,
                "A recorded longer continuation must advance instead of prematurely firing the shorter path.");
        }

        [Test]
        public void LiveMancojoEnrolledPathCoverFitsTheAnimatorBudget()
        {
            var plan = new VisemePhraseBuildPlan();
            var phrase = Phrase("mancojo_live", new[] { State(1, 0.032f, 0.128f) });
            phrase.variants.Clear();
            phrase.variants.Add(RuntimeVariant("v0",
                new[] { 1, 10, 5, 8, 5, 8 },
                new[] { 0.064f, 0.107f, 0.107f, 0.107f, 0.043f, 0.256f }));
            phrase.variants.Add(RuntimeVariant("v1",
                new[] { 1, 10, 5, 13, 8, 13, 8 },
                new[] { 0.128f, 0.085f, 0.128f, 0.085f, 0.043f, 0.064f, 0.213f }));
            phrase.variants.Add(RuntimeVariant("v2",
                new[] { 1, 10, 5, 13, 8 },
                new[] { 0.085f, 0.085f, 0.149f, 0.149f, 0.299f }));
            phrase.variants.Add(RuntimeVariant("v3",
                new[] { 10, 5, 13, 5, 13, 8 },
                new[] { 0.128f, 0.085f, 0.128f, 0.043f, 0.085f, 0.192f }));
            plan.phrases.Add(phrase);

            Assert.That(VisemePhraseGlobalTrie.TryBuild(
                plan, out _, out var logicalStates, out var error), Is.True, error);
            Assert.That(logicalStates,
                Is.LessThanOrEqualTo(VisemePhraseBuildPlan.MaximumCompiledStates));
            Assert.That(Build(plan).controller, Is.Not.Null);
        }

        [Test]
        public void ExitTimeSegmentsDoNotClampShortLearnedPhones()
        {
            var plan = new VisemePhraseBuildPlan();
            plan.phrases.Add(Phrase("short_first", new[]
            {
                // A 40 ms median phone at the compiler's 0.5x boundary is 20 ms.
                State(6, 0.02f, 0.08f), State(7, 0.005f, 0.03f)
            }));
            var built = Build(plan);
            var matcher = built.controller.layers.Single(layer =>
                layer.name == "YUCP Phrase Shared Matcher").stateMachine;
            var entry = matcher.states.Select(item => item.state).FirstOrDefault(state =>
                state.name.Contains("_0 depth 1 [6] natural "));
            Assert.That(entry, Is.Not.Null,
                string.Join(" | ", matcher.states.Select(item => item.state.name)));
            Assert.That(entry.motion.averageDuration, Is.EqualTo(0.02f).Within(0.0005f));
            Assert.That(entry.transitions.Any(transition => transition.hasExitTime &&
                transition.destinationState != null && transition.destinationState.name
                    .Contains("_1 depth 1 [6] natural ")), Is.True);

            var subsequent = matcher.states.Select(item => item.state).FirstOrDefault(state =>
                state.name.Contains("_0 depth 2 [7] natural "));
            Assert.That(subsequent, Is.Not.Null);
            Assert.That(subsequent.motion.averageDuration,
                Is.EqualTo(0.005f).Within(0.0005f),
                "Pure timing segments must preserve a learned five-millisecond minimum.");
            foreach (var fps in new[] { 15, 30, 60, 90, 144 })
            {
                var sampledDuration = Math.Ceiling(0.02 * fps) / fps;
                Assert.That(sampledDuration, Is.LessThanOrEqualTo(0.02 + 1.0 / fps),
                    "Frame sampling may add at most one observation interval, never another fixed gate.");
            }

            var subDriverMinimum = new VisemePhraseBuildPlan();
            subDriverMinimum.phrases.Add(Phrase("sub_driver_first", new[]
            {
                State(8, 0.005f, 0.03f), State(9, 0.005f, 0.03f)
            }));
            built = Build(subDriverMinimum);
            matcher = built.controller.layers.Single(layer =>
                layer.name == "YUCP Phrase Shared Matcher").stateMachine;
            entry = matcher.states.Select(item => item.state).First(state =>
                state.name.Contains("_0 depth 1 [8] natural "));
            var eligible = matcher.states.Select(item => item.state).First(state =>
                state.name.Contains("_1 depth 1 [8] natural "));
            Assert.That(entry.motion.averageDuration, Is.EqualTo(0.005f).Within(0.0005f));
            var totalWindow = entry.motion.averageDuration + eligible.motion.averageDuration;
            Assert.That(totalWindow, Is.EqualTo(0.03f).Within(0.0005f),
                "Segment durations must sum to the exact learned maximum.");
        }

        [Test]
        public void NetworkCarrierIsOneBitAndOnlyOwnerMatcherWritesIt()
        {
            var plan = new VisemePhraseBuildPlan();
            plan.phrases.Add(Phrase("network", new[]
            {
                State(4, 0.04f, 0.18f), State(5, 0.04f, 0.18f)
            }));
            var built = Build(plan);
            Assert.That(built.parameters.CalcTotalCost(), Is.EqualTo(1));
            var declaration = built.parameters.parameters.Single();
            Assert.That(declaration.valueType, Is.EqualTo(VRCExpressionParameters.ValueType.Bool));
            Assert.That(declaration.saved, Is.False);
            Assert.That(declaration.networkSynced, Is.True);

            var carrier = plan.phrases[0].carrierParameter;
            var carrierDrivers = AllStates(built.controller)
                .SelectMany(state => state.behaviours.OfType<VRCAvatarParameterDriver>())
                .Where(driver => driver.parameters.Any(parameter => parameter.name == carrier))
                .ToArray();
            Assert.That(carrierDrivers, Has.Length.EqualTo(2));
            Assert.That(carrierDrivers.All(driver => driver.localOnly), Is.True);
            CollectionAssert.AreEquivalent(new[] { 0f, 1f }, carrierDrivers
                .SelectMany(driver => driver.parameters)
                .Where(parameter => parameter.name == carrier)
                .Select(parameter => parameter.value));
            var phrase = plan.phrases[0];
            var matcher = built.controller.layers.Single(layer =>
                layer.name == "YUCP Phrase Shared Matcher").stateMachine;
            var emit = matcher.states.Select(item => item.state).First(state =>
                state.name.StartsWith("Emit network", StringComparison.Ordinal));
            var emitBindings = AnimationUtility.GetCurveBindings((AnimationClip)emit.motion);
            Assert.That(emitBindings.Any(binding =>
                binding.propertyName == phrase.confidenceParameter), Is.False,
                "Emit/trailing states must preserve the final live budget margin.");
            Assert.That(emitBindings.Any(binding =>
                binding.propertyName == phrase.progressParameter), Is.True);
        }

        [Test]
        public void EdgeDecoderHandlesBothDirectionsAndRearmsAfterOneResetFrame()
        {
            var plan = new VisemePhraseBuildPlan();
            plan.phrases.Add(Phrase("edge", new[]
            {
                State(6, 0.04f, 0.18f), State(7, 0.04f, 0.18f)
            }));
            var built = Build(plan);
            var edge = built.controller.layers.Single(layer =>
                layer.name == "YUCP Phrase Edge edge").stateMachine;
            Assert.That(edge.defaultState.name, Is.EqualTo("Initialize without pulse"));
            Assert.That(edge.defaultState.motion.averageDuration,
                Is.EqualTo(VisemePhraseBuildPlan.InitialNetworkSuppressionSeconds)
                    .Within(0.001f));
            Assert.That(StateNamed(edge, "Pulse rising edge").motion.averageDuration,
                Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(StateNamed(edge, "Pulse falling edge").motion.averageDuration,
                Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(StateNamed(edge, "Reset pulse").motion.averageDuration,
                Is.InRange(0.02f, 0.021f));
            Assert.That(edge.states.Any(state =>
                state.state.name.IndexOf("Network cooldown", StringComparison.Ordinal) >= 0),
                Is.False, "A second delayed network edge must not be lost in decoder cooldown.");
            Assert.That(StateNamed(edge, "Armed 0").transitions.Single()
                .destinationState.name, Is.EqualTo("Pulse rising edge"));
            Assert.That(StateNamed(edge, "Armed 1").transitions.Single()
                .destinationState.name, Is.EqualTo("Pulse falling edge"));
        }

        [Test]
        public void CooldownsArePerPhraseAndDoNotParkSharedMatcher()
        {
            var plan = new VisemePhraseBuildPlan();
            plan.phrases.Add(Phrase("a", new[]
            {
                State(1, 0.04f, 0.16f), State(2, 0.04f, 0.16f)
            }));
            plan.phrases.Add(Phrase("b", new[]
            {
                State(3, 0.04f, 0.16f), State(4, 0.04f, 0.16f)
            }));
            var built = Build(plan);
            Assert.That(built.controller.layers.Count(layer =>
                layer.name.StartsWith("YUCP Phrase Cooldown ", StringComparison.Ordinal)),
                Is.EqualTo(2));
            var matcher = built.controller.layers.Single(layer =>
                layer.name == "YUCP Phrase Shared Matcher").stateMachine;
            Assert.That(matcher.states.Any(state => state.state.name.StartsWith(
                "Match cooldown", StringComparison.Ordinal)), Is.False);
            foreach (var emit in matcher.states.Select(item => item.state)
                         .Where(state => state.name.StartsWith("Emit ", StringComparison.Ordinal)))
                Assert.That(emit.transitions.Single().destinationState.name, Is.EqualTo("Ready"));
        }

        [Test]
        public void PausedBoundaryLatchesAcrossAdjacentOnsetAndTalkingFrames()
        {
            var plan = new VisemePhraseBuildPlan();
            var phrase = Phrase("paused", new[]
            {
                State(10, 0.04f, 0.16f), State(11, 0.04f, 0.16f)
            });
            phrase.requireOnset = true;
            phrase.requireRelease = true;
            phrase.leadingPauseSeconds = 0.18f;
            phrase.trailingPauseSeconds = 0.22f;
            plan.phrases.Add(phrase);
            var built = Build(plan);
            var boundary = built.controller.layers.Single(layer =>
                layer.name.StartsWith("YUCP Phrase Boundary ", StringComparison.Ordinal))
                .stateMachine;
            Assert.That(StateNamed(boundary, "Leading silence").motion.averageDuration,
                Is.EqualTo(0.18f).Within(0.001f));
            Assert.That(StateNamed(boundary, "Consume boundary").motion.averageDuration,
                Is.InRange(0.10f, 0.12f));
            var ready = built.controller.layers.Single(layer =>
                layer.name == "YUCP Phrase Shared Matcher").stateMachine.defaultState;
            Assert.That(ready.name, Is.EqualTo("Remote"));
            var matcherReady = StateNamed(
                built.controller.layers.Single(layer =>
                    layer.name == "YUCP Phrase Shared Matcher").stateMachine,
                "Ready");
            Assert.That(matcherReady.transitions
                .Where(transition => transition.destinationState.name.Contains(
                    " paused "))
                .All(transition => transition.conditions.All(condition =>
                    condition.parameter != phrase.onsetParameter)), Is.True,
                "The latched armed boundary, not same-frame Onset, gates paused starts.");
        }

        [Test]
        public void ExistingContentAddressedAssetsLoadWithoutChangingGuid()
        {
            var plan = new VisemePhraseBuildPlan();
            plan.phrases.Add(Phrase("stable", new[]
            {
                State(8, 0.04f, 0.16f), State(9, 0.04f, 0.16f)
            }));
            var built = Build(plan);
            var path = AssetDatabase.GetAssetPath(built.controller);
            var guid = AssetDatabase.AssetPathToGUID(path);
            Assert.That(VisemePhraseTriggerAnimatorBuilder.TryLoadExisting(
                path,
                generatedFolder + "/Parameters.asset",
                plan,
                out var loaded), Is.True);
            Assert.That(loaded.controller, Is.SameAs(built.controller));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
        }

        private VisemePhraseTriggerAnimatorBuilder.Result Build(VisemePhraseBuildPlan plan)
        {
            return VisemePhraseTriggerAnimatorBuilder.Build(
                new VisemePhraseTriggerAnimatorBuilder.Request
                {
                    controllerPath = generatedFolder + "/Controller.controller",
                    parametersPath = generatedFolder + "/Parameters.asset",
                    plan = plan
                });
        }

        private VisemePhraseEnrollmentProfile CreatePersonalProfile(string name)
        {
            var profile = ScriptableObject.CreateInstance<VisemePhraseEnrollmentProfile>();
            profile.name = name;
            AssetDatabase.CreateAsset(profile, personalFolder + "/" + name + ".asset");
            return profile;
        }

        private static VRCExpressionParameters.Parameter ExpressionParameter(
            string name,
            bool saved,
            bool networkSynced) => new VRCExpressionParameters.Parameter
        {
            name = name,
            valueType = VRCExpressionParameters.ValueType.Bool,
            saved = saved,
            networkSynced = networkSynced
        };

        private static bool InvokeExistingParameterValidation(
            VRCAvatarDescriptor descriptor,
            IReadOnlyList<VisemePhraseBuildPhrase> phrases,
            out string error)
        {
            var method = typeof(VisemePhraseTriggerContractAdapter).GetMethod(
                "ValidateExistingParameters",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var arguments = new object[] { descriptor, phrases, null };
            var result = (bool)method.Invoke(null, arguments);
            error = arguments[2] as string;
            return result;
        }

        private static VisemePhraseBuildPhrase Phrase(
            string key,
            IReadOnlyList<VisemePhraseBuildState> states)
        {
            var prefix = "YUCP/TestPhrase";
            var id = "id_" + key;
            var phrase = new VisemePhraseBuildPhrase
            {
                ownerKey = "Avatar[0]/" + key,
                prompt = key,
                stableId = id,
                parameterKey = key,
                sourcePrefix = "YUCP/TestAVR",
                talkingParameter = "YUCP/TestAVR/Speech/Talking",
                onsetParameter = "YUCP/TestAVR/Speech/Onset",
                releaseParameter = "YUCP/TestAVR/Speech/Release",
                matchedParameter = AdvancedVisemeParameterContract.PhraseMatched(prefix, key),
                confidenceParameter = AdvancedVisemeParameterContract.PhraseConfidence(prefix, key),
                progressParameter = AdvancedVisemeParameterContract.PhraseProgress(prefix, key),
                carrierParameter = AdvancedVisemeParameterContract.PhraseCarrier(prefix, id),
                pulseSeconds = 0.25f,
                cooldownSeconds = 1.25f,
                requireOnset = false,
                requireRelease = false,
                enrollmentFingerprint = "trace_" + key
            };
            var variant = new VisemePhraseBuildVariant
            {
                id = "v0",
                canonicalStateCount = states.Count,
                minimumTotalSeconds = states.Sum(state => state.minimumSeconds),
                maximumTotalSeconds = states.Sum(state => state.maximumSeconds)
            };
            variant.states.AddRange(states);
            phrase.variants.Add(variant);
            return phrase;
        }

        private static VisemePhraseBuildState State(
            int viseme,
            float minimum,
            float maximum)
        {
            var emissions = new float[15];
            emissions[viseme] = 1f;
            return new VisemePhraseBuildState
            {
                aliases = new[] { viseme },
                minimumSeconds = minimum,
                maximumSeconds = maximum,
                confidence = 0.9f,
                emissionLikelihoods = emissions
            };
        }

        private static VisemePhraseBuildVariant RuntimeVariant(
            string id,
            IReadOnlyList<int> visemes,
            IReadOnlyList<float> durations)
        {
            Assert.That(visemes.Count, Is.EqualTo(durations.Count));
            var variant = new VisemePhraseBuildVariant
            {
                id = id,
                canonicalStateCount = visemes.Count,
                minimumTotalSeconds = durations.Sum() * 0.5f,
                maximumTotalSeconds = durations.Sum() * 2f
            };
            for (var index = 0; index < visemes.Count; index++)
                variant.states.Add(State(
                    visemes[index],
                    durations[index] * 0.5f,
                    durations[index] * 2f));
            return variant;
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

        private static int[] SampleAtFramePhase(
            IReadOnlyList<int> visemes,
            IReadOnlyList<float> durations,
            float framesPerSecond,
            float phaseSeconds)
        {
            Assert.That(visemes.Count, Is.EqualTo(durations.Count));
            var ends = new float[durations.Count];
            var total = 0f;
            for (var index = 0; index < durations.Count; index++)
            {
                total += durations[index];
                ends[index] = total;
            }
            var output = new List<int>();
            var step = 1f / Mathf.Max(1f, framesPerSecond);
            for (var time = Mathf.Max(0f, phaseSeconds);
                 time < total - 0.000001f;
                 time += step)
            {
                var token = Array.FindIndex(ends, end => time < end);
                if (token < 0) break;
                var viseme = visemes[token];
                if (output.Count == 0 || output[output.Count - 1] != viseme)
                    output.Add(viseme);
            }
            return output.ToArray();
        }

        private static VisemePhraseBuildState StateWithAliases(int[] aliases)
        {
            var emissions = new float[15];
            foreach (var alias in aliases) emissions[alias] = 1f;
            return new VisemePhraseBuildState
            {
                aliases = aliases,
                minimumSeconds = 0.04f,
                maximumSeconds = 0.18f,
                confidence = 0.9f,
                emissionLikelihoods = emissions
            };
        }

        private static AnimatorState StateNamed(
            AnimatorStateMachine machine,
            string prefix) => machine.states.Select(item => item.state)
            .FirstOrDefault(state => state.name.StartsWith(prefix, StringComparison.Ordinal));

        private static bool HasCondition(
            AnimatorStateTransition transition,
            string parameter,
            AnimatorConditionMode mode,
            float threshold) => transition.conditions.Any(condition =>
            condition.parameter == parameter && condition.mode == mode &&
            Math.Abs(condition.threshold - threshold) < 0.0001f);

        private static IEnumerable<AnimatorState> AllStates(AnimatorController controller) =>
            controller.layers.SelectMany(layer => layer.stateMachine.states)
                .Select(item => item.state);

        private static void AssertNoAnyState(AnimatorController controller)
        {
            foreach (var layer in controller.layers)
                Assert.That(layer.stateMachine.anyStateTransitions, Is.Empty, layer.name);
        }

        private static void EnsureFolder(string path)
        {
            var parts = path.Replace('\\', '/').Split('/');
            var cursor = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = cursor + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cursor, parts[index]);
                cursor = next;
            }
        }
    }
}
#endif
