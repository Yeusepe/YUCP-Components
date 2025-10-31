using System;

namespace PackageGuardian.Core.Validation
{
    /// <summary>
    /// Represents a validation issue detected in the project.
    /// </summary>
    public sealed class ValidationIssue
    {
        public IssueSeverity Severity { get; }
        public IssueCategory Category { get; }
        public string Title { get; }
        public string Description { get; }
        public string[] AffectedPaths { get; }
        public string SuggestedAction { get; }
        public System.Action AutoFix { get; }
        public bool CanAutoFix => AutoFix != null;
        
        public ValidationIssue(
            IssueSeverity severity,
            IssueCategory category,
            string title,
            string description,
            string[] affectedPaths = null,
            string suggestedAction = null,
            System.Action autoFix = null)
        {
            Severity = severity;
            Category = category;
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            AffectedPaths = affectedPaths ?? Array.Empty<string>();
            SuggestedAction = suggestedAction;
            AutoFix = autoFix;
        }
    }
    
    public enum IssueSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }
    
    public enum IssueCategory
    {
        PackageConflict,
        MissingDependency,
        BrokenReference,
        CompilationError,
        DangerousDeletion,
        LargeFile,
        BinaryConflict,
        LockedFile,
        PerformanceWarning,
        AssetDatabaseCorruption,
        MissingMetaFile,
        DuplicateGUID,
        InvalidFileName,
        ShaderCompilationError,
        VersionMismatch,
        SceneValidation,
        MemoryWarning,
        UnityAPICompatibility,
        ProjectSettingsIssue,
        DependencyIntegrity
    }
}

