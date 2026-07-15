#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace YUCP.Components.Editor.VisemePhrase
{
    /// <summary>
    /// Canonical, editor-only representation consumed by the controller builder.
    /// Keeping the serialized enrollment contract behind this boundary makes the
    /// generated Animator deterministic and independently testable.
    /// </summary>
    internal sealed class VisemePhraseBuildPlan
    {
        internal const int MaximumPhrases = 4;
        internal const int MaximumCompiledStates = 32;
        internal const int MaximumGeneratedTimingSegments = 128;
        internal const int MaximumStatesPerVariant = 12;
        // Bump whenever generated Animator semantics change. Content-addressed
        // folders may outlive a package update, so the parameter contract alone
        // is not a sufficient cache key for controller topology/timing changes.
        internal const int ControllerGeneratorSchemaVersion = 4;
        internal const float InitialNetworkSuppressionSeconds = 1.25f;
        internal const float DefaultPulseSeconds = 0.25f;
        internal const float MinimumNetworkCooldownSeconds = 1.25f;

        internal readonly List<VisemePhraseBuildPhrase> phrases =
            new List<VisemePhraseBuildPhrase>();

        internal string CanonicalFingerprint()
        {
            var output = new StringBuilder("YUCP_VISPHRASE_CONTROLLER_V")
                .Append(ControllerGeneratorSchemaVersion)
                .Append("_CONTRACT_")
                .Append(AdvancedVisemeParameterContract.ContractVersion);
            foreach (var phrase in phrases
                         .OrderBy(item => item.matchedParameter, StringComparer.Ordinal))
            {
                output.Append("|phrase:").Append(phrase.CanonicalFingerprint());
            }

            return output.ToString();
        }

        internal string ContentHash() =>
            Hash128.Compute(CanonicalFingerprint()).ToString();
    }

    internal sealed class VisemePhraseBuildPhrase
    {
        internal string ownerKey;
        internal string prompt;
        internal string stableId;
        internal string parameterKey;
        internal string sourcePrefix;
        internal string talkingParameter;
        internal string onsetParameter;
        internal string releaseParameter;
        internal string matchedParameter;
        internal string confidenceParameter;
        internal string progressParameter;
        internal string carrierParameter;
        internal bool declareCarrier = true;
        internal string enrollmentFingerprint;
        internal float pulseSeconds = VisemePhraseBuildPlan.DefaultPulseSeconds;
        internal float cooldownSeconds =
            VisemePhraseBuildPlan.MinimumNetworkCooldownSeconds;
        internal bool requireOnset = true;
        internal bool requireRelease;
        internal float leadingPauseSeconds;
        internal float trailingPauseSeconds;
        internal float runtimeAcceptanceCost = 1f;
        // Natural and pause-bounded phrases consume the same raw viseme stream.
        // Boundary mode is session state, not a second matcher: splitting it here
        // makes two transitions eligible for the same observation and turns the
        // result into Animator transition-order roulette.
        internal string MatcherClassKey => sourcePrefix ?? string.Empty;
        internal readonly List<VisemePhraseBuildVariant> variants =
            new List<VisemePhraseBuildVariant>();

        internal string CanonicalFingerprint()
        {
            var output = new StringBuilder();
            Append(output, ownerKey);
            Append(output, prompt);
            Append(output, stableId);
            Append(output, parameterKey);
            Append(output, sourcePrefix);
            Append(output, talkingParameter);
            Append(output, onsetParameter);
            Append(output, releaseParameter);
            Append(output, matchedParameter);
            Append(output, confidenceParameter);
            Append(output, progressParameter);
            Append(output, carrierParameter);
            Append(output, declareCarrier ? "declare" : "reuse");
            Append(output, enrollmentFingerprint);
            Append(output, pulseSeconds.ToString("R", CultureInfo.InvariantCulture));
            Append(output, cooldownSeconds.ToString("R", CultureInfo.InvariantCulture));
            Append(output, requireOnset ? "onset" : "continuous");
            Append(output, requireRelease ? "release" : "embedded");
            Append(output, leadingPauseSeconds.ToString("R", CultureInfo.InvariantCulture));
            Append(output, trailingPauseSeconds.ToString("R", CultureInfo.InvariantCulture));
            Append(output, runtimeAcceptanceCost.ToString("R", CultureInfo.InvariantCulture));
            foreach (var variant in variants
                         .OrderBy(item => item.CanonicalTrace(), StringComparer.Ordinal))
                Append(output, variant.CanonicalFingerprint());
            return output.ToString();
        }

        internal string TraceSetFingerprint() => string.Join("||", variants
            .Select(item => item.CanonicalTrace())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal));

        private static void Append(StringBuilder output, string value)
        {
            value = value ?? string.Empty;
            output.Append(value.Length).Append(':').Append(value).Append(';');
        }
    }

    internal sealed class VisemePhraseBuildVariant
    {
        internal string id;
        // Context-derived paths are optional language bridges. The avatar-wide
        // budget fitter may remove them, but never a directly enrolled path.
        internal bool inferredContextPath;
        internal int canonicalStateCount;
        internal float minimumTotalSeconds;
        internal float maximumTotalSeconds = float.MaxValue;
        // Cost paid before observing any retained state (currently the one
        // compiler-approved skipped state). Runtime rectangles have already
        // selected the skip; the Animator must not invent alternate paths.
        internal float runtimeBaseCost;
        internal float runtimePathCost;
        internal readonly List<VisemePhraseBuildState> states =
            new List<VisemePhraseBuildState>();

        internal string CanonicalTrace() => string.Join(">", states.Select(
            state => string.Join(",", state.aliases.OrderBy(alias => alias))));

        internal string CanonicalRuntimePathFingerprint() =>
            canonicalStateCount + "@" +
            minimumTotalSeconds.ToString("R", CultureInfo.InvariantCulture) + ".." +
            maximumTotalSeconds.ToString("R", CultureInfo.InvariantCulture) + "$" +
            runtimeBaseCost.ToString("R", CultureInfo.InvariantCulture) + "+" +
            runtimePathCost.ToString("R", CultureInfo.InvariantCulture) + "=" +
            string.Join(">", states.Select(state => state.CanonicalFingerprint()));

        internal string CanonicalFingerprint() =>
            (id ?? string.Empty) + (inferredContextPath ? "#inferred#" : "#enrolled#") +
            CanonicalRuntimePathFingerprint();
    }

    internal sealed class VisemePhraseBuildState
    {
        internal int[] aliases = Array.Empty<int>();
        internal float minimumSeconds;
        internal float maximumSeconds;
        internal float confidence = 1f;
        internal bool allowSkip;
        internal float[] emissionLikelihoods = new float[15];
        internal float skipPenalty;

        internal string CanonicalFingerprint()
        {
            var canonicalAliases = aliases
                .Distinct()
                .OrderBy(alias => alias)
                .Select(alias => alias.ToString(CultureInfo.InvariantCulture));
            var emissions = string.Join(",", (emissionLikelihoods ?? Array.Empty<float>())
                .Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
            return string.Join(",", canonicalAliases) + "@" +
                   minimumSeconds.ToString("R", CultureInfo.InvariantCulture) + ".." +
                   maximumSeconds.ToString("R", CultureInfo.InvariantCulture) + "#" +
                   confidence.ToString("R", CultureInfo.InvariantCulture) +
                   (allowSkip ? "?" : "!") + "$" +
                   skipPenalty.ToString("R", CultureInfo.InvariantCulture) + "$" + emissions;
        }

        internal string TransitionSignature()
        {
            var canonicalAliases = aliases
                .Distinct()
                .OrderBy(alias => alias)
                .Select(alias => alias.ToString(CultureInfo.InvariantCulture));
            return string.Join(",", canonicalAliases) + "@" +
                   minimumSeconds.ToString("R", CultureInfo.InvariantCulture) + ".." +
                   maximumSeconds.ToString("R", CultureInfo.InvariantCulture) +
                   (allowSkip ? "?" : "!");
        }

        internal string TokenIdentity()
        {
            var canonicalAliases = aliases
                .Distinct()
                .OrderBy(alias => alias)
                .Select(alias => alias.ToString(CultureInfo.InvariantCulture));
            return string.Join(",", canonicalAliases) + (allowSkip ? "?" : "!");
        }
    }

    /// <summary>
    /// One deterministic avatar-wide prefix trie shared by every component and
    /// phrase. Membership is retained on each node so the controller can guard
    /// shared transitions by source/boundary class without duplicating the DFA.
    /// </summary>
    internal static class VisemePhraseGlobalTrie
    {
        internal sealed class Node
        {
            internal VisemePhraseBuildState value;
            internal int depth;
            internal readonly HashSet<int> phraseIndices = new HashSet<int>();
            internal readonly HashSet<int> acceptingPhraseIndices = new HashSet<int>();
            internal readonly Dictionary<int, VisemePhraseBuildState> candidateStates =
                new Dictionary<int, VisemePhraseBuildState>();
            internal readonly HashSet<int> acceptingCandidateIds = new HashSet<int>();
            internal readonly List<Node> children = new List<Node>();
            internal readonly List<Candidate> candidates = new List<Candidate>();
        }

        internal sealed class Candidate
        {
            internal int id;
            internal int phraseIndex;
            internal string variantId;
            internal int canonicalStateCount;
            internal float minimumTotalSeconds;
            internal float maximumTotalSeconds;
            internal float runtimePathCost;
        }

        internal static bool TryBuild(
            VisemePhraseBuildPlan plan,
            out Node root,
            out int stateCount,
            out string error)
        {
            root = new Node();
            stateCount = 0;
            error = null;
            var candidateId = 0;
            for (var phraseIndex = 0; phraseIndex < plan.phrases.Count; phraseIndex++)
            {
                var phrase = plan.phrases[phraseIndex];
                foreach (var sourceVariant in phrase.variants
                             .OrderBy(item => item.CanonicalFingerprint(), StringComparer.Ordinal))
                {
                    if (!TryExpandRuntimePaths(
                            phrase, sourceVariant, out var runtimeVariants, out error))
                        return false;
                    foreach (var variant in runtimeVariants)
                    {
                    var candidate = new Candidate
                    {
                        id = candidateId++,
                        phraseIndex = phraseIndex,
                        variantId = variant.id ?? string.Empty,
                        canonicalStateCount = variant.canonicalStateCount,
                        minimumTotalSeconds = variant.minimumTotalSeconds,
                        maximumTotalSeconds = variant.maximumTotalSeconds,
                        runtimePathCost = variant.runtimePathCost
                    };
                    root.candidates.Add(candidate);
                    var cursor = root;
                    cursor.phraseIndices.Add(phraseIndex);
                    for (var index = 0; index < variant.states.Count; index++)
                    {
                        if (cursor.acceptingPhraseIndices.Any(existing =>
                                existing != phraseIndex &&
                                string.Equals(
                                    plan.phrases[existing].MatcherClassKey,
                                    phrase.MatcherClassKey,
                                    StringComparison.Ordinal)))
                        {
                            error = $"Phrase '{phrase.prompt}' has a trace whose prefix " +
                                    "is already an accepting phrase in the same speech context.";
                            return false;
                        }

                        var state = variant.states[index];
                        var identity = state.TokenIdentity();
                        var child = cursor.children.FirstOrDefault(candidate =>
                            candidate.value.TokenIdentity() == identity);
                        if (child == null)
                        {
                            var aliases = new HashSet<int>(state.aliases);
                            var ambiguous = cursor.children.FirstOrDefault(candidate =>
                                candidate.value.aliases.Any(aliases.Contains) &&
                                candidate.phraseIndices.Any(existing =>
                                    string.Equals(
                                        plan.phrases[existing].MatcherClassKey,
                                        phrase.MatcherClassKey,
                                        StringComparison.Ordinal)));
                            if (ambiguous != null)
                            {
                                error = $"Phrase '{phrase.prompt}' has an ambiguous trained " +
                                      $"branch after state {index}: aliases " +
                                      $"[{string.Join(", ", state.aliases)}] overlap " +
                                      $"[{string.Join(", ", ambiguous.value.aliases)}] in " +
                                      "the same source/context class.";
                                return false;
                            }

                            child = new Node
                            {
                                value = Clone(state),
                                depth = index + 1
                            };
                            cursor.children.Add(child);
                            stateCount++;
                        }
                        child.phraseIndices.Add(phraseIndex);
                        child.candidateStates[candidate.id] = Clone(state);
                        cursor = child;
                    }

                    if (cursor.children.Any(child => ContainsOtherPhraseClass(
                            child,
                            phraseIndex,
                            phrase.MatcherClassKey,
                            plan.phrases)))
                    {
                        error = $"Phrase '{phrase.prompt}' is a strict prefix of another " +
                                "phrase in the same speech context.";
                        return false;
                    }
                    cursor.acceptingPhraseIndices.Add(phraseIndex);
                    cursor.acceptingCandidateIds.Add(candidate.id);
                    }
                }
            }

            if (!ValidateRootClassAliases(plan, out error)) return false;
            if (!ValidateOptionalReachability(root, plan, out error)) return false;
            return VisemePhraseTimedSubsetPlanner.TryPlan(
                root, plan, out _, out stateCount, out error);
        }

        private static bool TryExpandRuntimePaths(
            VisemePhraseBuildPhrase phrase,
            VisemePhraseBuildVariant source,
            out List<VisemePhraseBuildVariant> variants,
            out string error)
        {
            const int maximumExpansions = 1024;
            var expandedVariants = new List<VisemePhraseBuildVariant>();
            variants = expandedVariants;
            error = null;
            var canonicalCount = Math.Max(1,
                source.canonicalStateCount > 0
                    ? source.canonicalStateCount
                    : source.states.Count);
            var budget = Mathf.Clamp01(phrase.runtimeAcceptanceCost) * canonicalCount;
            var path = new List<VisemePhraseBuildState>();
            var overflow = false;

            void Expand(int stateIndex, float cost)
            {
                if (overflow || cost > budget + 0.000001f) return;
                if (stateIndex >= source.states.Count)
                {
                    if (path.Count == 0) return;
                    if (expandedVariants.Count >= maximumExpansions)
                    {
                        overflow = true;
                        return;
                    }
                    var expanded = new VisemePhraseBuildVariant
                    {
                        id = (source.id ?? string.Empty) + "_runtime_" + expandedVariants.Count,
                        inferredContextPath = source.inferredContextPath,
                        canonicalStateCount = canonicalCount,
                        minimumTotalSeconds = source.minimumTotalSeconds,
                        maximumTotalSeconds = source.maximumTotalSeconds,
                        runtimeBaseCost = source.runtimeBaseCost,
                        runtimePathCost = cost
                    };
                    expanded.states.AddRange(path.Select(Clone));
                    expandedVariants.Add(expanded);
                    return;
                }

                var state = source.states[stateIndex];
                var aliases = (state.aliases ?? Array.Empty<int>())
                    .Where(alias => alias >= 0 && alias < 15)
                    .Distinct().OrderBy(alias => alias);
                foreach (var alias in aliases)
                {
                    var aliasCost = 1f - Mathf.Clamp01(
                        state.emissionLikelihoods != null &&
                        alias < state.emissionLikelihoods.Length
                            ? state.emissionLikelihoods[alias]
                            : 0f);
                    var concrete = Clone(state);
                    // Preserve the concrete observation history. Equal-cost
                    // aliases are not merged because longest-suffix recovery
                    // must distinguish (for example) {1} from the observed 4
                    // inside an otherwise equal-likelihood {1,4} tier.
                    concrete.aliases = new[] { alias };
                    concrete.allowSkip = false;
                    path.Add(concrete);
                    Expand(stateIndex + 1, cost + aliasCost);
                    path.RemoveAt(path.Count - 1);
                }
            }

            // The compiler emits one safe timing rectangle per learned path,
            // including its selected optional skip. Start with that fixed cost
            // and only expand emission-likelihood tiers here.
            Expand(0, Math.Max(0f, source.runtimeBaseCost));
            if (overflow)
            {
                error = $"Phrase '{phrase.prompt}' expands beyond {maximumExpansions} exact " +
                        "runtime alias/skip paths. Remove low-confidence aliases or use a " +
                        "more distinctive enrollment.";
                return false;
            }
            variants = expandedVariants
                .GroupBy(variant => variant.CanonicalFingerprint(), StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(variant => variant.CanonicalFingerprint(), StringComparer.Ordinal)
                .ToList();
            if (variants.Count > 0) return true;
            error = $"Phrase '{phrase.prompt}' has no alias/skip path within its calibrated " +
                    "runtime acceptance budget. Recompile or re-record the enrollment.";
            return false;
        }

        private static bool ValidateOptionalReachability(
            Node root,
            VisemePhraseBuildPlan plan,
            out string error)
        {
            error = null;
            foreach (var node in new[] { root }.Concat(Descendants(root)))
            {
                var classes = node.phraseIndices
                    .Select(index => plan.phrases[index].MatcherClassKey)
                    .Distinct(StringComparer.Ordinal);
                foreach (var matcherClass in classes)
                {
                    var reachable = new List<Node>();
                    AddReachable(node, matcherClass, plan, reachable,
                        new HashSet<Node>());
                    var aliases = new HashSet<int>();
                    foreach (var child in reachable)
                    foreach (var alias in child.value.aliases.Distinct())
                    {
                        if (aliases.Add(alias)) continue;
                        error = $"Optional-state expansion makes viseme {alias} " +
                                $"ambiguous in source/context class '{matcherClass}'.";
                        return false;
                    }
                }
            }
            return true;
        }

        private static void AddReachable(
            Node node,
            string matcherClass,
            VisemePhraseBuildPlan plan,
            ICollection<Node> output,
            ISet<Node> seen)
        {
            foreach (var child in node.children)
            {
                var inClass = child.phraseIndices.Any(index => string.Equals(
                    plan.phrases[index].MatcherClassKey,
                    matcherClass,
                    StringComparison.Ordinal));
                if (!inClass) continue;
                if (seen.Add(child)) output.Add(child);
                if (child.value.allowSkip)
                    AddReachable(child, matcherClass, plan, output, seen);
            }
        }

        private static IEnumerable<Node> Descendants(Node node)
        {
            foreach (var child in node.children)
            {
                yield return child;
                foreach (var descendant in Descendants(child))
                    yield return descendant;
            }
        }

        private static bool ValidateRootClassAliases(
            VisemePhraseBuildPlan plan,
            out string error)
        {
            error = null;
            var classesByAlias = new Dictionary<int, HashSet<string>>();
            foreach (var phrase in plan.phrases)
            foreach (var variant in phrase.variants)
            {
                for (var stateIndex = 0; stateIndex < variant.states.Count; stateIndex++)
                {
                    var state = variant.states[stateIndex];
                    foreach (var alias in state.aliases.Distinct())
                    {
                        if (!classesByAlias.TryGetValue(alias, out var classes))
                        {
                            classes = new HashSet<string>(StringComparer.Ordinal);
                            classesByAlias[alias] = classes;
                        }
                        classes.Add(phrase.MatcherClassKey);
                        if (classes.Count > 1)
                        {
                            error = $"Viseme {alias} can begin phrases from multiple source " +
                                    "prefixes. Their source gates can be true simultaneously, " +
                                    "so matching would depend on Animator transition order. " +
                                    "Use one source prefix for that starting viseme or re-enroll " +
                                    "a distinct phrase.";
                            return false;
                        }
                    }
                    if (!state.allowSkip) break;
                }
            }
            return true;
        }

        private static VisemePhraseBuildState Clone(VisemePhraseBuildState source) =>
            new VisemePhraseBuildState
            {
                aliases = source.aliases.ToArray(),
                minimumSeconds = source.minimumSeconds,
                maximumSeconds = source.maximumSeconds,
                confidence = source.confidence,
                allowSkip = source.allowSkip,
                emissionLikelihoods = (source.emissionLikelihoods ?? Array.Empty<float>()).ToArray(),
                skipPenalty = source.skipPenalty
            };

        private static bool ContainsClass(
            Node node,
            string matcherClass,
            IReadOnlyList<VisemePhraseBuildPhrase> phrases)
        {
            if (node.phraseIndices.Any(index => string.Equals(
                    phrases[index].MatcherClassKey,
                    matcherClass,
                    StringComparison.Ordinal))) return true;
            return node.children.Any(child => ContainsClass(child, matcherClass, phrases));
        }

        private static bool ContainsOtherPhraseClass(
            Node node,
            int phraseIndex,
            string matcherClass,
            IReadOnlyList<VisemePhraseBuildPhrase> phrases)
        {
            if (node.phraseIndices.Any(index =>
                    index != phraseIndex &&
                    string.Equals(
                        phrases[index].MatcherClassKey,
                        matcherClass,
                        StringComparison.Ordinal))) return true;
            return node.children.Any(child => ContainsOtherPhraseClass(
                child,
                phraseIndex,
                matcherClass,
                phrases));
        }
    }

    /// <summary>
    /// Determinizes the timed candidate NFA. A logical state carries the exact
    /// surviving enrollment variants; timing-invalid candidates never reappear
    /// after a shared prefix. Timing intervals are edges rather than widened
    /// properties on trie nodes.
    /// </summary>
    internal static class VisemePhraseTimedSubsetPlanner
    {
        internal sealed class Graph
        {
            internal VisemePhraseGlobalTrie.Node root;
            internal readonly List<State> states = new List<State>();
            internal readonly List<RootStart> rootStarts = new List<RootStart>();
        }

        internal sealed class State
        {
            internal string key;
            internal int index;
            internal VisemePhraseGlobalTrie.Node node;
            internal string matcherClass;
            internal bool pausedSession;
            internal int[] candidateIds = Array.Empty<int>();
            internal readonly List<Advance> advances = new List<Advance>();
        }

        internal sealed class RootStart
        {
            internal State state;
            internal bool paused;
            internal int[] phraseIndices = Array.Empty<int>();
        }

        internal sealed class Advance
        {
            internal State destination;
            internal TimingBucket bucket;
            internal int[] aliases = Array.Empty<int>();
        }

        internal sealed class TimingBucket
        {
            internal float minimumSeconds;
            internal float maximumSeconds;
            internal int[] candidateIds = Array.Empty<int>();
        }

        internal static bool TryPlan(
            VisemePhraseGlobalTrie.Node root,
            VisemePhraseBuildPlan plan,
            out Graph graph,
            out int stateCount,
            out string error)
        {
            var plannedGraph = new Graph { root = root };
            graph = plannedGraph;
            stateCount = 0;
            error = null;
            if (root == null || plan == null)
            {
                error = "The timed phrase graph is missing its trie or build plan.";
                return false;
            }

            var nodeIds = new Dictionary<VisemePhraseGlobalTrie.Node, int>();
            var nodeIndex = 0;
            foreach (var node in Descendants(root)) nodeIds[node] = nodeIndex++;
            var byKey = new Dictionary<string, State>(StringComparer.Ordinal);
            var pending = new Queue<State>();

            State Intern(
                VisemePhraseGlobalTrie.Node node,
                string matcherClass,
                bool pausedSession,
                IEnumerable<int> candidateIds)
            {
                var candidates = candidateIds.Distinct().OrderBy(id => id).ToArray();
                var key = nodeIds[node] + "|" + matcherClass + "|" +
                          (pausedSession ? "P|" : "N|") + string.Join(",", candidates);
                if (byKey.TryGetValue(key, out var existing)) return existing;
                var created = new State
                {
                    key = key,
                    index = plannedGraph.states.Count,
                    node = node,
                    matcherClass = matcherClass,
                    pausedSession = pausedSession,
                    candidateIds = candidates
                };
                byKey[key] = created;
                plannedGraph.states.Add(created);
                pending.Enqueue(created);
                return created;
            }

            foreach (var matcherClass in plan.phrases
                         .Select(phrase => phrase.MatcherClassKey)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(value => value, StringComparer.Ordinal))
            foreach (var child in ReachableNodes(root, matcherClass, root, plan))
            {
                var candidates = child.candidateStates.Keys.Where(candidateId =>
                        CandidateUsesClass(root, plan, candidateId, matcherClass) &&
                        CanReach(root, child, candidateId))
                    .OrderBy(id => id)
                    .ToArray();
                if (candidates.Length == 0) continue;
                var naturalPhrases = CandidatePhrases(root, candidates)
                    .Where(index => plan.phrases[index].leadingPauseSeconds <= 0f)
                    .Distinct().OrderBy(index => index).ToArray();
                if (naturalPhrases.Length > 0)
                    plannedGraph.rootStarts.Add(new RootStart
                    {
                        state = Intern(child, matcherClass, false, candidates),
                        paused = false,
                        phraseIndices = naturalPhrases
                    });
                var pausedPhrases = CandidatePhrases(root, candidates)
                    .Where(index => plan.phrases[index].leadingPauseSeconds > 0f)
                    .Distinct().OrderBy(index => index).ToArray();
                if (pausedPhrases.Length > 0)
                    plannedGraph.rootStarts.Add(new RootStart
                    {
                        state = Intern(child, matcherClass, true, candidates),
                        paused = true,
                        phraseIndices = pausedPhrases
                    });
            }

            while (pending.Count > 0)
            {
                var state = pending.Dequeue();
                var candidatesByChild = new Dictionary<VisemePhraseGlobalTrie.Node, HashSet<int>>();
                foreach (var candidateId in state.candidateIds)
                foreach (var child in ReachableNodes(
                             state.node, state.matcherClass, root, plan, candidateId))
                {
                    if (!candidatesByChild.TryGetValue(child, out var candidates))
                    {
                        candidates = new HashSet<int>();
                        candidatesByChild[child] = candidates;
                    }
                    candidates.Add(candidateId);
                }

                foreach (var pair in candidatesByChild.OrderBy(item => nodeIds[item.Key]))
                foreach (var bucket in TimingBuckets(state.node, pair.Value))
                {
                    var destination = Intern(pair.Key, state.matcherClass,
                        state.pausedSession, bucket.candidateIds);
                    state.advances.Add(new Advance
                    {
                        destination = destination,
                        bucket = bucket,
                        aliases = pair.Key.value.aliases.Distinct().OrderBy(alias => alias).ToArray()
                    });
                    if (plannedGraph.states.Count <= VisemePhraseBuildPlan.MaximumCompiledStates) continue;
                    stateCount = plannedGraph.states.Count;
                    error = $"Exact per-phrase timing expands the shared matcher to " +
                            $"{stateCount} logical states; the maximum is " +
                            VisemePhraseBuildPlan.MaximumCompiledStates + ". " +
                            "Reduce phrase variants or choose more distinctive phrases.";
                    return false;
                }
            }

            stateCount = plannedGraph.states.Count;
            var generatedSegments = plannedGraph.states.Sum(state =>
                TimingSegments(state.node, state.candidateIds).Count);
            if (generatedSegments > VisemePhraseBuildPlan.MaximumGeneratedTimingSegments)
            {
                error = $"Exact per-phrase timing expands the shared matcher to " +
                        $"{generatedSegments} generated timing segments; the maximum is " +
                        VisemePhraseBuildPlan.MaximumGeneratedTimingSegments + ". " +
                        "Reduce phrase variants or choose more distinctive phrases.";
                return false;
            }
            return true;
        }

        internal static IReadOnlyList<TimingBucket> TimingSegments(
            VisemePhraseGlobalTrie.Node node,
            IEnumerable<int> candidateIds)
        {
            var candidates = candidateIds.Distinct()
                .Where(node.candidateStates.ContainsKey)
                .OrderBy(id => id)
                .ToArray();
            var boundaries = new[] { 0f }.Concat(candidates.SelectMany(id => new[]
                {
                    node.candidateStates[id].minimumSeconds,
                    node.candidateStates[id].maximumSeconds
                }))
                .Where(value => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            var output = new List<TimingBucket>();
            for (var index = 0; index + 1 < boundaries.Length; index++)
            {
                var minimum = boundaries[index];
                var maximum = boundaries[index + 1];
                if (maximum <= minimum) continue;
                var sample = minimum + (maximum - minimum) * 0.5f;
                output.Add(new TimingBucket
                {
                    minimumSeconds = minimum,
                    maximumSeconds = maximum,
                    candidateIds = candidates.Where(id =>
                    {
                        var timing = node.candidateStates[id];
                        return sample >= timing.minimumSeconds &&
                               sample <= timing.maximumSeconds;
                    }).ToArray()
                });
            }
            return output;
        }

        internal static IReadOnlyList<TimingBucket> TimingBuckets(
            VisemePhraseGlobalTrie.Node node,
            IEnumerable<int> candidateIds)
        {
            return TimingSegments(node, candidateIds)
                .Where(segment => segment.candidateIds.Length > 0)
                .ToArray();
        }

        internal static IReadOnlyList<TimingBucket> AcceptanceBuckets(
            VisemePhraseGlobalTrie.Node node,
            IEnumerable<int> candidateIds,
            int phraseIndex,
            VisemePhraseGlobalTrie.Node root)
        {
            var accepting = candidateIds.Where(id =>
                    node.acceptingCandidateIds.Contains(id) &&
                    CandidatePhrase(root, id) == phraseIndex)
                .ToArray();
            return TimingBuckets(node, accepting);
        }

        internal static int CandidatePhrase(
            VisemePhraseGlobalTrie.Node root,
            int candidateId) => root.candidates.First(candidate =>
                candidate.id == candidateId).phraseIndex;

        private static IEnumerable<int> CandidatePhrases(
            VisemePhraseGlobalTrie.Node root,
            IEnumerable<int> candidateIds) => candidateIds.Select(id =>
                CandidatePhrase(root, id));

        private static bool CandidateUsesClass(
            VisemePhraseGlobalTrie.Node root,
            VisemePhraseBuildPlan plan,
            int candidateId,
            string matcherClass) => string.Equals(
            plan.phrases[CandidatePhrase(root, candidateId)].MatcherClassKey,
            matcherClass, StringComparison.Ordinal);

        private static IEnumerable<VisemePhraseGlobalTrie.Node> ReachableNodes(
            VisemePhraseGlobalTrie.Node node,
            string matcherClass,
            VisemePhraseGlobalTrie.Node root,
            VisemePhraseBuildPlan plan,
            int? candidateId = null)
        {
            var output = new List<VisemePhraseGlobalTrie.Node>();
            foreach (var child in node.children)
            {
                var candidates = child.candidateStates.Keys.Where(id =>
                    (!candidateId.HasValue || id == candidateId.Value) &&
                    CandidateUsesClass(root, plan, id, matcherClass)).ToArray();
                if (candidates.Length == 0) continue;
                output.Add(child);
                if (candidates.Any(id => child.candidateStates[id].allowSkip))
                    output.AddRange(ReachableNodes(child, matcherClass, root, plan,
                        candidateId));
            }
            return output.Distinct();
        }

        private static bool CanReach(
            VisemePhraseGlobalTrie.Node from,
            VisemePhraseGlobalTrie.Node target,
            int candidateId)
        {
            foreach (var child in from.children)
            {
                if (!child.candidateStates.ContainsKey(candidateId)) continue;
                if (ReferenceEquals(child, target)) return true;
                if (child.candidateStates[candidateId].allowSkip &&
                    CanReach(child, target, candidateId)) return true;
            }
            return false;
        }

        private static IEnumerable<VisemePhraseGlobalTrie.Node> Descendants(
            VisemePhraseGlobalTrie.Node node)
        {
            foreach (var child in node.children
                         .OrderBy(item => item.value.TokenIdentity(), StringComparer.Ordinal))
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
    }
}
#endif
