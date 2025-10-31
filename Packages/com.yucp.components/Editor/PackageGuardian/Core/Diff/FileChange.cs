using System;

namespace PackageGuardian.Core.Diff
{
    /// <summary>
    /// Represents a change to a file between two commits.
    /// </summary>
    public sealed class FileChange
    {
        public string Path { get; }
        public ChangeType Type { get; }
        public string OldOid { get; }
        public string NewOid { get; }
        public bool IsBinary { get; set; }
        
        /// <summary>
        /// For renamed/copied files, this is the new path. Otherwise null.
        /// </summary>
        public string NewPath { get; set; }
        
        /// <summary>
        /// Similarity score for renames/copies (0.0 to 1.0).
        /// </summary>
        public float SimilarityScore { get; set; }
        
        public FileChange(string path, ChangeType type, string oldOid, string newOid)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Type = type;
            OldOid = oldOid;
            NewOid = newOid;
            IsBinary = false;
            NewPath = null;
            SimilarityScore = 0f;
        }
    }
    
    public enum ChangeType
    {
        Added,
        Modified,
        Deleted,
        Renamed,
        Copied
    }
}

