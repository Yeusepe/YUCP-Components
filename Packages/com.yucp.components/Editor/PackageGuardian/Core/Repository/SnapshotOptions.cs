using System;
using System.Collections.Generic;
using System.Threading;

namespace PackageGuardian.Core.Repository
{
    /// <summary>
    /// Additional options controlling snapshot creation.
    /// </summary>
    public sealed class SnapshotOptions
    {
        /// <summary>
        /// Specify an explicit committer. Defaults to the author if null or empty.
        /// </summary>
        public string Committer { get; set; }

        /// <summary>
        /// List of root directories to include. Defaults to Assets/Packages when null.
        /// </summary>
        public IEnumerable<string> IncludeRoots { get; set; }

        /// <summary>
        /// Optional cancellation token for long-running snapshot jobs.
        /// </summary>
        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

        /// <summary>
        /// Callback invoked whenever the snapshot builder reports progress.
        /// </summary>
        public Action<int, int, string> Progress { get; set; }
    }
}



