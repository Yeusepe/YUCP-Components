using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Neutral planner input. Levels are the number of distinct values remote
    /// clients can see, not bits: a Bool has two, a [0,1] Float 128, a [-1,1]
    /// Float 255 and a full Int 256.
    /// </summary>
    internal sealed class ParameterCompressionCandidate
    {
        internal ParameterCompressionCandidate(
            string name,
            int levels,
            int priority,
            int currentBits,
            bool required = false)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A candidate needs a stable name.", nameof(name));
            if (levels < 2 || levels > ParameterCompressionPlanner.PageSize)
                throw new ArgumentOutOfRangeException(nameof(levels));
            if (currentBits <= 0)
                throw new ArgumentOutOfRangeException(nameof(currentBits));

            Name = name.Trim();
            Levels = levels;
            Priority = priority;
            CurrentBits = currentBits;
            Required = required;
        }

        internal string Name { get; }
        internal int Levels { get; }
        internal int Priority { get; }
        internal int CurrentBits { get; }
        internal bool Required { get; }
    }

    /// <summary>Where one value's levels sit in the bus code space.</summary>
    internal sealed class ParameterCompressionAllocation
    {
        internal ParameterCompressionAllocation(
            ParameterCompressionCandidate candidate,
            int page,
            int offset)
        {
            Candidate = candidate;
            Page = page;
            Offset = offset;
        }

        internal ParameterCompressionCandidate Candidate { get; }
        internal string Name => Candidate.Name;
        internal int Levels => Candidate.Levels;
        internal int Page { get; }
        internal int Offset { get; }
    }

    internal sealed class ParameterCompressionPlan
    {
        internal ParameterCompressionPlan(
            int finalBits,
            int pageBits,
            IReadOnlyList<ParameterCompressionAllocation> allocations)
        {
            FinalBits = finalBits;
            PageBits = pageBits;
            Allocations = allocations;
        }

        internal int FinalBits { get; }
        internal int PageBits { get; }
        internal IReadOnlyList<ParameterCompressionAllocation> Allocations { get; }
        internal bool UsesCompression => Allocations.Count > 0;
        internal int BusBits => UsesCompression ? 8 + PageBits : 0;
    }

    /// <summary>
    /// Deterministic budget planner for the event bus. One message names a value
    /// and its level, so a code space of sum(levels) is enough
    /// (enumerative coding, Cover 1973: https://isl.stanford.edu/~cover/papers/transIT/0073cove.pdf).
    /// The code is one synced Int plus page Bools that pick a 256-code page; first
    /// fit decreasing packs values into pages so none straddles two.
    /// </summary>
    internal static class ParameterCompressionPlanner
    {
        internal const int PageSize = 256;

        internal static ParameterCompressionPlan CreatePlan(
            int currentSyncedBits,
            int targetSyncedBits,
            IEnumerable<ParameterCompressionCandidate> candidates)
        {
            if (!TryCreatePlan(currentSyncedBits, targetSyncedBits, candidates,
                    out var plan, out var error))
                throw new InvalidOperationException(error);
            return plan;
        }

        internal static bool TryCreatePlan(
            int currentSyncedBits,
            int targetSyncedBits,
            IEnumerable<ParameterCompressionCandidate> candidates,
            out ParameterCompressionPlan plan,
            out string error)
        {
            plan = null;
            error = null;
            if (currentSyncedBits < 0 || targetSyncedBits < 0)
            {
                error = "Synchronized bit counts cannot be negative.";
                return false;
            }
            var all = (candidates ?? throw new ArgumentNullException(nameof(candidates)))
                .ToArray();
            var duplicate = all.GroupBy(candidate => candidate.Name, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                error = "Duplicate compression candidate '" + duplicate.Key + "'.";
                return false;
            }

            var selected = all.Where(candidate => candidate.Required)
                .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
                .ToList();
            if (selected.Count == 0 && currentSyncedBits <= targetSyncedBits)
            {
                plan = new ParameterCompressionPlan(currentSyncedBits, 0,
                    Array.Empty<ParameterCompressionAllocation>());
                return true;
            }

            // Recorderis: a value saves at least one bit and grows the bus by at most
            // one page bit, so adding candidates never makes the budget worse.
            var optional = new Queue<ParameterCompressionCandidate>(all
                .Where(candidate => !candidate.Required)
                .OrderByDescending(candidate => candidate.Priority)
                .ThenByDescending(candidate => candidate.CurrentBits)
                .ThenBy(candidate => candidate.Name, StringComparer.Ordinal));
            while (true)
            {
                var allocations = Pack(selected, out var pageBits);
                var finalBits = currentSyncedBits + 8 + pageBits -
                                selected.Sum(candidate => candidate.CurrentBits);
                if (selected.Count > 0 && finalBits <= targetSyncedBits)
                {
                    plan = new ParameterCompressionPlan(finalBits, pageBits, allocations);
                    return true;
                }
                if (optional.Count == 0)
                {
                    error = "Moving every safe value onto the event bus still leaves " +
                            finalBits + " synced bits, above the " + targetSyncedBits +
                            "-bit target.";
                    return false;
                }
                selected.Add(optional.Dequeue());
            }
        }

        /// <summary>First fit decreasing.</summary>
        private static IReadOnlyList<ParameterCompressionAllocation> Pack(
            IEnumerable<ParameterCompressionCandidate> selected,
            out int pageBits)
        {
            var used = new List<int> { 0 };
            var allocations = new List<ParameterCompressionAllocation>();
            foreach (var candidate in selected
                         .OrderByDescending(candidate => candidate.Levels)
                         .ThenBy(candidate => candidate.Name, StringComparer.Ordinal))
            {
                var page = used.FindIndex(fill => fill + candidate.Levels <= PageSize);
                if (page < 0)
                {
                    page = used.Count;
                    used.Add(0);
                }
                allocations.Add(new ParameterCompressionAllocation(
                    candidate, page, used[page]));
                used[page] += candidate.Levels;
            }
            pageBits = 0;
            while (1 << pageBits < used.Count) pageBits++;
            return new ReadOnlyCollection<ParameterCompressionAllocation>(allocations
                .OrderBy(allocation => allocation.Name, StringComparer.Ordinal).ToArray());
        }
    }
}
