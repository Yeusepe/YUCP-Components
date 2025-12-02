using System;

namespace PackageGuardian.Core.Diff
{
    /// <summary>
    /// Options for controlling diff behavior, inspired by Git's diff options.
    /// </summary>
    public sealed class DiffOptions
    {
        /// <summary>
        /// Default options with rename detection enabled.
        /// </summary>
        public static DiffOptions Default => new DiffOptions();

        /// <summary>
        /// Detect renames. When true, files that are deleted and added with similar content
        /// will be detected as renames.
        /// </summary>
        public bool DetectRenames { get; set; } = true;

        /// <summary>
        /// Similarity threshold for rename detection (0.0 to 1.0).
        /// 0.5 means 50% similarity required. Default is 0.5 (Git's default).
        /// </summary>
        public float RenameThreshold { get; set; } = 0.5f;

        /// <summary>
        /// Maximum number of files to check for renames.
        /// Set to -1 for unlimited. Default is 1000 (Git's default is 400).
        /// </summary>
        public int RenameLimit { get; set; } = 1000;

        /// <summary>
        /// Detect copies in addition to renames.
        /// </summary>
        public bool DetectCopies { get; set; } = false;

        /// <summary>
        /// Include binary files in diff output.
        /// </summary>
        public bool IncludeBinaryFiles { get; set; } = true;

        /// <summary>
        /// Maximum file size to process for rename detection (in bytes).
        /// Files larger than this will not be compared for similarity.
        /// Default is 10MB.
        /// </summary>
        public long MaxFileSizeForRenameDetection { get; set; } = 10 * 1024 * 1024;

        public DiffOptions()
        {
        }

        public DiffOptions Clone()
        {
            return new DiffOptions
            {
                DetectRenames = this.DetectRenames,
                RenameThreshold = this.RenameThreshold,
                RenameLimit = this.RenameLimit,
                DetectCopies = this.DetectCopies,
                IncludeBinaryFiles = this.IncludeBinaryFiles,
                MaxFileSizeForRenameDetection = this.MaxFileSizeForRenameDetection
            };
        }
    }
}

