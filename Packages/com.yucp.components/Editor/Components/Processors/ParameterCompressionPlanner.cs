using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace YUCP.Components.Editor
{
    internal enum ParameterCompressionValueKind
    {
        Bool,
        Int,
        Float
    }

    /// <summary>
    /// Neutral planner input. Levels are the number of distinct on-wire values,
    /// not bits. A lossless VRChat Float therefore requests 255 levels, while an
    /// Int requests 256 and a Bool requests two.
    /// </summary>
    internal sealed class ParameterCompressionCandidate
    {
        internal ParameterCompressionCandidate(
            string name,
            ParameterCompressionValueKind valueKind,
            int minimumLevels,
            int desiredLevels,
            int priority,
            int currentBits,
            bool required = false)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A candidate needs a stable name.", nameof(name));
            if (minimumLevels <= 0)
                throw new ArgumentOutOfRangeException(nameof(minimumLevels));
            if (desiredLevels < minimumLevels)
                throw new ArgumentOutOfRangeException(nameof(desiredLevels));
            if (currentBits <= 0)
                throw new ArgumentOutOfRangeException(nameof(currentBits));

            Name = name.Trim();
            ValueKind = valueKind;
            MinimumLevels = minimumLevels;
            DesiredLevels = desiredLevels;
            Priority = priority;
            CurrentBits = currentBits;
            Required = required;
        }

        internal string Name { get; }
        internal ParameterCompressionValueKind ValueKind { get; }
        internal int MinimumLevels { get; }
        internal int DesiredLevels { get; }
        internal int Priority { get; }
        internal int CurrentBits { get; }
        internal bool Required { get; }
    }

    internal sealed class ParameterCompressionBusPolicy
    {
        internal ParameterCompressionBusPolicy(
            int minimumWireBits,
            int maximumWireBits,
            int maximumDigits,
            float secondsPerWireWord)
        {
            if (minimumWireBits < 3)
                throw new ArgumentOutOfRangeException(nameof(minimumWireBits));
            if (maximumWireBits < minimumWireBits || maximumWireBits > 16)
                throw new ArgumentOutOfRangeException(nameof(maximumWireBits));
            if (maximumDigits < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumDigits));
            if (float.IsNaN(secondsPerWireWord) ||
                float.IsInfinity(secondsPerWireWord) ||
                secondsPerWireWord <= 0f)
                throw new ArgumentOutOfRangeException(nameof(secondsPerWireWord));

            MinimumWireBits = minimumWireBits;
            MaximumWireBits = maximumWireBits;
            MaximumDigits = maximumDigits;
            SecondsPerWireWord = secondsPerWireWord;
        }

        internal int MinimumWireBits { get; }
        internal int MaximumWireBits { get; }
        internal int MaximumDigits { get; }
        internal float SecondsPerWireWord { get; }
    }

    internal sealed class ParameterCompressionAllocation
    {
        internal ParameterCompressionAllocation(
            ParameterCompressionCandidate candidate,
            int levels,
            long offset)
        {
            Candidate = candidate;
            Levels = levels;
            Offset = offset;
        }

        internal ParameterCompressionCandidate Candidate { get; }
        internal string Name => Candidate.Name;
        internal int Levels { get; }
        internal long Offset { get; }
    }

    internal sealed class ParameterCompressionPlan
    {
        internal ParameterCompressionPlan(
            int currentBits,
            int targetBits,
            int busBits,
            int radix,
            int digitCount,
            int finalBits,
            long totalCardinality,
            float estimatedFrameSeconds,
            float estimatedFullRefreshSeconds,
            IReadOnlyList<ParameterCompressionAllocation> allocations,
            ParameterCompressionEnumerativeLayout layout)
        {
            CurrentBits = currentBits;
            TargetBits = targetBits;
            BusBits = busBits;
            Radix = radix;
            DigitCount = digitCount;
            FinalBits = finalBits;
            TotalCardinality = totalCardinality;
            EstimatedFrameSeconds = estimatedFrameSeconds;
            EstimatedFullRefreshSeconds = estimatedFullRefreshSeconds;
            Allocations = allocations;
            Layout = layout;
        }

        internal int CurrentBits { get; }
        internal int TargetBits { get; }
        internal int BusBits { get; }
        internal int CarrierBits => BusBits;
        internal int Radix { get; }
        internal int DigitCount { get; }
        internal int FinalBits { get; }
        internal long TotalCardinality { get; }
        internal float EstimatedFrameSeconds { get; }
        internal float EstimatedFullRefreshSeconds { get; }
        internal IReadOnlyList<ParameterCompressionAllocation> Allocations { get; }
        internal IReadOnlyList<ParameterCompressionAllocation> SelectedCandidates =>
            Allocations;
        internal ParameterCompressionEnumerativeLayout Layout { get; }
        internal bool UsesCompression => BusBits > 0;
    }

    /// <summary>
    /// Deterministic budget and precision planner. It evaluates every allowed
    /// constant-weight bus width, selects enough candidates to meet the target,
    /// and water-fills numeric precision inside an exact radix^digits capacity.
    /// </summary>
    internal static class ParameterCompressionPlanner
    {
        internal static ParameterCompressionPlan CreatePlan(
            int currentSyncedBits,
            int targetSyncedBits,
            IEnumerable<ParameterCompressionCandidate> candidates,
            ParameterCompressionBusPolicy policy)
        {
            if (!TryCreatePlan(
                    currentSyncedBits,
                    targetSyncedBits,
                    candidates,
                    policy,
                    out var plan,
                    out var error))
                throw new InvalidOperationException(error);
            return plan;
        }

        internal static bool TryCreatePlan(
            int currentSyncedBits,
            int targetSyncedBits,
            IEnumerable<ParameterCompressionCandidate> candidates,
            ParameterCompressionBusPolicy policy,
            out ParameterCompressionPlan plan,
            out string error)
        {
            plan = null;
            error = null;
            if (currentSyncedBits < 0)
            {
                error = "Current synchronized bits cannot be negative.";
                return false;
            }
            if (targetSyncedBits < 0)
            {
                error = "Target synchronized bits cannot be negative.";
                return false;
            }
            if (candidates == null)
            {
                error = "Compression candidates are required.";
                return false;
            }
            if (policy == null)
            {
                error = "A compression bus policy is required.";
                return false;
            }

            var all = candidates.ToArray();
            if (all.Any(candidate => candidate == null))
            {
                error = "A compression candidate cannot be null.";
                return false;
            }
            var duplicate = all.GroupBy(candidate => candidate.Name, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                error = "Duplicate compression candidate '" + duplicate.Key + "'.";
                return false;
            }

            var required = all.Where(candidate => candidate.Required).ToArray();
            if (currentSyncedBits <= targetSyncedBits && required.Length == 0)
            {
                plan = NoCompression(currentSyncedBits, targetSyncedBits);
                return true;
            }

            ParameterCompressionPlan best = null;
            string lastFailure = null;
            for (var wireBits = policy.MinimumWireBits;
                 wireBits <= policy.MaximumWireBits;
                 wireBits++)
            {
                var alphabet = new ParameterCompressionAlphabet(wireBits);
                var selected = SelectCandidates(
                    currentSyncedBits, targetSyncedBits, wireBits, all);
                if (selected == null)
                {
                    lastFailure = "The available candidates cannot save enough bits with a " +
                                  wireBits + "-wire bus.";
                    continue;
                }
                if (selected.Count == 0)
                {
                    var noCompression = NoCompression(
                        currentSyncedBits, targetSyncedBits);
                    if (IsBetter(noCompression, best)) best = noCompression;
                    continue;
                }

                long capacity;
                try
                {
                    capacity = ParameterCompressionEnumerativeLayout.Capacity(
                        alphabet.Radix, policy.MaximumDigits);
                }
                catch (OverflowException)
                {
                    capacity = long.MaxValue;
                }

                if (!TryAllocateLevels(selected, capacity,
                        out var levelByName, out var allocationError))
                {
                    lastFailure = allocationError;
                    continue;
                }

                var ordered = selected
                    .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
                    .ToArray();
                var domains = ordered.Select(candidate =>
                    new ParameterCompressionDomain(
                        candidate.Name, levelByName[candidate.Name])).ToArray();
                var layout = new ParameterCompressionEnumerativeLayout(domains);
                var digitCount = ParameterCompressionEnumerativeLayout.DigitsRequired(
                    layout.TotalCardinality, alphabet.Radix);
                if (digitCount > policy.MaximumDigits)
                {
                    lastFailure = "The selected domains exceed the configured digit limit.";
                    continue;
                }

                var allocations = layout.Entries.Select((entry, index) =>
                    new ParameterCompressionAllocation(
                        ordered[index], entry.Cardinality, entry.Offset)).ToArray();
                var finalBits = currentSyncedBits + wireBits -
                                selected.Sum(candidate => candidate.CurrentBits);
                var frameWords = 5 + 2 * digitCount;
                var frameSeconds = frameWords * policy.SecondsPerWireWord;
                var fullRefreshSeconds = frameSeconds * selected.Count;
                var candidatePlan = new ParameterCompressionPlan(
                    currentSyncedBits,
                    targetSyncedBits,
                    wireBits,
                    alphabet.Radix,
                    digitCount,
                    finalBits,
                    layout.TotalCardinality,
                    frameSeconds,
                    fullRefreshSeconds,
                    new ReadOnlyCollection<ParameterCompressionAllocation>(allocations),
                    layout);
                if (IsBetter(candidatePlan, best)) best = candidatePlan;
            }

            if (best == null)
            {
                error = lastFailure ??
                        "No allowed constant-weight bus can satisfy the compression target.";
                return false;
            }
            plan = best;
            return true;
        }

        private static List<ParameterCompressionCandidate> SelectCandidates(
            int currentBits,
            int targetBits,
            int busBits,
            IReadOnlyCollection<ParameterCompressionCandidate> candidates)
        {
            var selected = candidates.Where(candidate => candidate.Required)
                .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
                .ToList();
            var selectedNames = new HashSet<string>(
                selected.Select(candidate => candidate.Name), StringComparer.Ordinal);
            var finalBits = currentBits + busBits -
                            selected.Sum(candidate => candidate.CurrentBits);
            var optional = candidates.Where(candidate => !selectedNames.Contains(candidate.Name))
                .OrderByDescending(candidate => candidate.Priority)
                .ThenByDescending(candidate => candidate.CurrentBits)
                .ThenBy(candidate => candidate.Name, StringComparer.Ordinal);
            foreach (var candidate in optional)
            {
                if (finalBits <= targetBits) break;
                selected.Add(candidate);
                selectedNames.Add(candidate.Name);
                finalBits -= candidate.CurrentBits;
            }
            return finalBits <= targetBits ? selected : null;
        }

        private static bool TryAllocateLevels(
            IReadOnlyCollection<ParameterCompressionCandidate> selected,
            long capacity,
            out Dictionary<string, int> levelByName,
            out string error)
        {
            levelByName = selected.ToDictionary(
                candidate => candidate.Name,
                candidate => candidate.MinimumLevels,
                StringComparer.Ordinal);
            error = null;
            var minimumTotal = selected.Sum(candidate => (long)candidate.MinimumLevels);
            if (minimumTotal > capacity)
            {
                error = "Minimum requested precision needs " + minimumTotal +
                        " enumerative values, but the radix capacity is only " + capacity + ".";
                return false;
            }

            var desiredTotal = selected.Sum(candidate => (long)candidate.DesiredLevels);
            if (desiredTotal <= capacity)
            {
                foreach (var candidate in selected)
                    levelByName[candidate.Name] = candidate.DesiredLevels;
                return true;
            }

            var remaining = capacity - minimumTotal;
            var adjustable = selected
                .Where(candidate => candidate.DesiredLevels > candidate.MinimumLevels)
                .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
                .ToArray();
            while (remaining > 0)
            {
                ParameterCompressionCandidate next = null;
                foreach (var candidate in adjustable)
                {
                    if (levelByName[candidate.Name] >= candidate.DesiredLevels) continue;
                    if (next == null || HasLowerFill(candidate, next, levelByName))
                        next = candidate;
                }
                if (next == null) break;
                levelByName[next.Name]++;
                remaining--;
            }
            return true;
        }

        private static bool HasLowerFill(
            ParameterCompressionCandidate candidate,
            ParameterCompressionCandidate current,
            IReadOnlyDictionary<string, int> levels)
        {
            var candidateProgress = levels[candidate.Name] - candidate.MinimumLevels;
            var candidateSpan = candidate.DesiredLevels - candidate.MinimumLevels;
            var currentProgress = levels[current.Name] - current.MinimumLevels;
            var currentSpan = current.DesiredLevels - current.MinimumLevels;
            var left = (long)candidateProgress * currentSpan;
            var right = (long)currentProgress * candidateSpan;
            if (left != right) return left < right;
            if (candidate.Priority != current.Priority)
                return candidate.Priority > current.Priority;
            return string.CompareOrdinal(candidate.Name, current.Name) < 0;
        }

        private static bool IsBetter(
            ParameterCompressionPlan candidate,
            ParameterCompressionPlan current)
        {
            if (candidate == null) return false;
            if (current == null) return true;
            var latency = candidate.EstimatedFullRefreshSeconds.CompareTo(
                current.EstimatedFullRefreshSeconds);
            if (latency != 0) return latency < 0;
            if (candidate.TotalCardinality != current.TotalCardinality)
                return candidate.TotalCardinality > current.TotalCardinality;
            if (candidate.BusBits != current.BusBits)
                return candidate.BusBits < current.BusBits;
            if (candidate.Allocations.Count != current.Allocations.Count)
                return candidate.Allocations.Count < current.Allocations.Count;
            return false;
        }

        private static ParameterCompressionPlan NoCompression(
            int currentBits,
            int targetBits)
        {
            return new ParameterCompressionPlan(
                currentBits,
                targetBits,
                0,
                0,
                0,
                currentBits,
                0,
                0f,
                0f,
                Array.Empty<ParameterCompressionAllocation>(),
                null);
        }
    }
}
