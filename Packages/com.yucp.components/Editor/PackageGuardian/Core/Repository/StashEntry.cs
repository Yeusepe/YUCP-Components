using System;

namespace PackageGuardian.Core.Repository
{
    /// <summary>
    /// Represents a stash entry.
    /// </summary>
    public sealed record StashEntry
    {
        /// <summary>
        /// Ref name (e.g., "refs/stash/auto/20241031-143000").
        /// </summary>
        public string RefName { get; init; }
        
        /// <summary>
        /// Commit ID.
        /// </summary>
        public string CommitId { get; init; }
        
        /// <summary>
        /// Unix timestamp.
        /// </summary>
        public long Timestamp { get; init; }
        
        /// <summary>
        /// Stash message.
        /// </summary>
        public string Message { get; init; }

        public StashEntry(string refName, string commitId, long timestamp, string message)
        {
            RefName = refName ?? throw new ArgumentNullException(nameof(refName));
            CommitId = commitId ?? throw new ArgumentNullException(nameof(commitId));
            Timestamp = timestamp;
            Message = message ?? string.Empty;
        }
    }
}

