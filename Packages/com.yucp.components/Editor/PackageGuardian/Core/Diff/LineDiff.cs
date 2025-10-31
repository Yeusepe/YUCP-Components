using System;

namespace PackageGuardian.Core.Diff
{
    /// <summary>
    /// Represents a single line in a diff.
    /// </summary>
    public sealed class LineDiff
    {
        public DiffLineType Type { get; }
        public int OldLineNumber { get; }
        public int NewLineNumber { get; }
        public string Content { get; }
        
        public LineDiff(DiffLineType type, int oldLineNumber, int newLineNumber, string content)
        {
            Type = type;
            OldLineNumber = oldLineNumber;
            NewLineNumber = newLineNumber;
            Content = content ?? string.Empty;
        }
    }
    
    public enum DiffLineType
    {
        Context,
        Added,
        Deleted
    }
}

