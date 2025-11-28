using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PackageGuardian.Core.Objects;
using PackageGuardian.Core.Storage;
using PackageGuardian.Core.Ignore;
using PackageGuardian.Core.Hashing;

namespace PackageGuardian.Core.Diff
{
        /// <summary>
        /// Compares commits and generates diffs.
        /// </summary>
        public sealed class DiffEngine
        {
            private readonly IObjectStore _store;
            
            public DiffEngine(IObjectStore store)
            {
                _store = store ?? throw new ArgumentNullException(nameof(store));
            }
            
            /// <summary>
            /// Safely read an object, returning null if corrupted.
            /// </summary>
            private PgObject SafeReadObject(string oid)
            {
                if (string.IsNullOrEmpty(oid))
                    return null;
                
                try
                {
                    return _store.ReadObject(oid);
                }
                catch (InvalidDataException)
                {
                    UnityEngine.Debug.LogWarning($"[DiffEngine] Corrupted object {oid.Substring(0, Math.Min(8, oid.Length))}... - skipping");
                    return null;
                }
                catch (FileNotFoundException)
                {
                    return null;
                }
            }
            
            /// <summary>
            /// Compare two commits and return the list of changed files.
            /// </summary>
            public List<FileChange> CompareCommits(string oldCommitId, string newCommitId)
            {
                return CompareCommits(oldCommitId, newCommitId, DiffOptions.Default);
            }

            /// <summary>
            /// Compare two commits with options and return the list of changed files.
            /// </summary>
            public List<FileChange> CompareCommits(string oldCommitId, string newCommitId, DiffOptions options)
            {
                if (string.IsNullOrEmpty(oldCommitId))
                    throw new ArgumentNullException(nameof(oldCommitId));
                if (string.IsNullOrEmpty(newCommitId))
                    throw new ArgumentNullException(nameof(newCommitId));
                
                options = options ?? DiffOptions.Default;
                
                // Load commits
                var oldCommit = SafeReadObject(oldCommitId) as Commit;
                var newCommit = SafeReadObject(newCommitId) as Commit;
                
                if (oldCommit == null || newCommit == null)
                {
                    string errorMsg = "Failed to load commit";
                    if (oldCommit == null && newCommit == null)
                        errorMsg = $"Failed to load commits: old commit '{oldCommitId.Substring(0, Math.Min(8, oldCommitId.Length))}...' and new commit '{newCommitId.Substring(0, Math.Min(8, newCommitId.Length))}...' may be corrupted or missing";
                    else if (oldCommit == null)
                        errorMsg = $"Failed to load old commit '{oldCommitId.Substring(0, Math.Min(8, oldCommitId.Length))}...' - may be corrupted or missing";
                    else
                        errorMsg = $"Failed to load new commit '{newCommitId.Substring(0, Math.Min(8, newCommitId.Length))}...' - may be corrupted or missing";
                    throw new InvalidOperationException(errorMsg);
                }
                
                string oldTreeId = BytesToHex(oldCommit.TreeId);
                string newTreeId = BytesToHex(newCommit.TreeId);
                
                var changes = CompareTrees("", oldTreeId, newTreeId);
                
                // Apply rename detection if enabled
                if (options.DetectRenames)
                {
                    changes = DetectRenames(changes, options);
                }
                
                return changes;
            }
            
            /// <summary>
            /// Compare two trees recursively.
            /// </summary>
            public List<FileChange> CompareTrees(string basePath, string oldTreeId, string newTreeId)
            {
                var changes = new List<FileChange>();
                
                // Handle null tree IDs (e.g., comparing against nothing)
                if (string.IsNullOrEmpty(oldTreeId) && string.IsNullOrEmpty(newTreeId))
                    return changes;
                
                // Load trees
                Tree oldTree = null;
                Tree newTree = null;
                
                if (!string.IsNullOrEmpty(oldTreeId))
                    oldTree = SafeReadObject(oldTreeId) as Tree;
                
                if (!string.IsNullOrEmpty(newTreeId))
                    newTree = SafeReadObject(newTreeId) as Tree;
            
            // If old tree is null, all files in new tree are added
            if (oldTree == null && newTree != null)
            {
                foreach (var entry in newTree.Entries)
                {
                    string fullPath = string.IsNullOrEmpty(basePath) ? entry.Name : $"{basePath}/{entry.Name}";
                    if (entry.Mode == "040000")
                        AddNewTreeFiles(fullPath, BytesToHex(entry.ObjectId), changes);
                    else
                        changes.Add(new FileChange(fullPath, ChangeType.Added, null, BytesToHex(entry.ObjectId)));
                }
                return changes;
            }
            
            // If new tree is null, all files in old tree are deleted
            if (newTree == null && oldTree != null)
            {
                foreach (var entry in oldTree.Entries)
                {
                    string fullPath = string.IsNullOrEmpty(basePath) ? entry.Name : $"{basePath}/{entry.Name}";
                    if (entry.Mode == "040000")
                        AddDeletedTreeFiles(fullPath, BytesToHex(entry.ObjectId), changes);
                    else
                        changes.Add(new FileChange(fullPath, ChangeType.Deleted, BytesToHex(entry.ObjectId), null));
                }
                return changes;
            }
            
            // Both trees are null
            if (oldTree == null && newTree == null)
                return changes;
            
            // Build lookup dictionaries
            var oldEntries = oldTree.Entries.ToDictionary(e => e.Name, e => e);
            var newEntries = newTree.Entries.ToDictionary(e => e.Name, e => e);
            
            // Find deleted and modified
            foreach (var oldEntry in oldEntries.Values)
            {
                string fullPath = string.IsNullOrEmpty(basePath) 
                    ? oldEntry.Name 
                    : $"{basePath}/{oldEntry.Name}";
                
                if (!newEntries.TryGetValue(oldEntry.Name, out var newEntry))
                {
                    // Deleted
                    if (oldEntry.Mode == "040000") // Directory
                    {
                        // Add all files in this directory as deleted
                        AddDeletedTreeFiles(fullPath, BytesToHex(oldEntry.ObjectId), changes);
                    }
                    else
                    {
                        changes.Add(new FileChange(fullPath, ChangeType.Deleted, 
                            BytesToHex(oldEntry.ObjectId), null));
                    }
                }
                else
                {
                    string oldOid = BytesToHex(oldEntry.ObjectId);
                    string newOid = BytesToHex(newEntry.ObjectId);
                    
                    if (oldOid != newOid)
                    {
                        // Modified
                        if (oldEntry.Mode == "040000" && newEntry.Mode == "040000")
                        {
                            // Both directories - recurse
                            changes.AddRange(CompareTrees(fullPath, oldOid, newOid));
                        }
                        else
                        {
                            changes.Add(new FileChange(fullPath, ChangeType.Modified, oldOid, newOid));
                        }
                    }
                }
            }
            
            // Find added
            foreach (var newEntry in newEntries.Values)
            {
                if (!oldEntries.ContainsKey(newEntry.Name))
                {
                    string fullPath = string.IsNullOrEmpty(basePath) 
                        ? newEntry.Name 
                        : $"{basePath}/{newEntry.Name}";
                    
                    if (newEntry.Mode == "040000") // Directory
                    {
                        // Add all files in this directory as new
                        AddNewTreeFiles(fullPath, BytesToHex(newEntry.ObjectId), changes);
                    }
                    else
                    {
                        changes.Add(new FileChange(fullPath, ChangeType.Added, 
                            null, BytesToHex(newEntry.ObjectId)));
                    }
                }
            }
            
            return changes;
        }
        
        private void AddDeletedTreeFiles(string basePath, string treeId, List<FileChange> changes)
        {
            var tree = SafeReadObject(treeId) as Tree;
            if (tree == null) return;
            
            foreach (var entry in tree.Entries)
            {
                string fullPath = $"{basePath}/{entry.Name}";
                
                if (entry.Mode == "040000")
                {
                    AddDeletedTreeFiles(fullPath, BytesToHex(entry.ObjectId), changes);
                }
                else
                {
                    changes.Add(new FileChange(fullPath, ChangeType.Deleted, 
                        BytesToHex(entry.ObjectId), null));
                }
            }
        }
        
        private void AddNewTreeFiles(string basePath, string treeId, List<FileChange> changes)
        {
            var tree = SafeReadObject(treeId) as Tree;
            if (tree == null) return;
            
            foreach (var entry in tree.Entries)
            {
                string fullPath = $"{basePath}/{entry.Name}";
                
                if (entry.Mode == "040000")
                {
                    AddNewTreeFiles(fullPath, BytesToHex(entry.ObjectId), changes);
                }
                else
                {
                    changes.Add(new FileChange(fullPath, ChangeType.Added, 
                        null, BytesToHex(entry.ObjectId)));
                }
            }
        }
        
        /// <summary>
        /// Generate a line-by-line diff for text files.
        /// </summary>
        public List<LineDiff> DiffTextFiles(string oldOid, string newOid)
        {
            var oldLines = oldOid != null ? GetFileLines(oldOid) : new string[0];
            var newLines = newOid != null ? GetFileLines(newOid) : new string[0];
            
            return DiffLines(oldLines, newLines);
        }
        
        private string[] GetFileLines(string blobOid)
        {
            var blob = SafeReadObject(blobOid) as Blob;
            if (blob == null) return new string[0];
            
            try
            {
                string text = Encoding.UTF8.GetString(blob.Data);
                return text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            }
            catch
            {
                return new string[0];
            }
        }
        
        /// <summary>
        /// Myers diff algorithm (simplified version).
        /// </summary>
        private List<LineDiff> DiffLines(string[] oldLines, string[] newLines)
        {
            var result = new List<LineDiff>();
            int oldIndex = 0;
            int newIndex = 0;
            
            // Simple line-by-line comparison
            while (oldIndex < oldLines.Length || newIndex < newLines.Length)
            {
                if (oldIndex >= oldLines.Length)
                {
                    // Rest are additions
                    result.Add(new LineDiff(DiffLineType.Added, -1, newIndex + 1, newLines[newIndex]));
                    newIndex++;
                }
                else if (newIndex >= newLines.Length)
                {
                    // Rest are deletions
                    result.Add(new LineDiff(DiffLineType.Deleted, oldIndex + 1, -1, oldLines[oldIndex]));
                    oldIndex++;
                }
                else if (oldLines[oldIndex] == newLines[newIndex])
                {
                    // Context line
                    result.Add(new LineDiff(DiffLineType.Context, oldIndex + 1, newIndex + 1, oldLines[oldIndex]));
                    oldIndex++;
                    newIndex++;
                }
                else
                {
                    // Check if we can find a match ahead
                    int oldMatch = FindMatchInRange(oldLines, oldIndex + 1, newLines[newIndex], 10);
                    int newMatch = FindMatchInRange(newLines, newIndex + 1, oldLines[oldIndex], 10);
                    
                    if (oldMatch >= 0 && (newMatch < 0 || oldMatch - oldIndex <= newMatch - newIndex))
                    {
                        // Lines were deleted from old
                        while (oldIndex < oldMatch)
                        {
                            result.Add(new LineDiff(DiffLineType.Deleted, oldIndex + 1, -1, oldLines[oldIndex]));
                            oldIndex++;
                        }
                    }
                    else if (newMatch >= 0)
                    {
                        // Lines were added to new
                        while (newIndex < newMatch)
                        {
                            result.Add(new LineDiff(DiffLineType.Added, -1, newIndex + 1, newLines[newIndex]));
                            newIndex++;
                        }
                    }
                    else
                    {
                        // Line was modified
                        result.Add(new LineDiff(DiffLineType.Deleted, oldIndex + 1, -1, oldLines[oldIndex]));
                        result.Add(new LineDiff(DiffLineType.Added, -1, newIndex + 1, newLines[newIndex]));
                        oldIndex++;
                        newIndex++;
                    }
                }
            }
            
            return result;
        }
        
        private int FindMatchInRange(string[] lines, int startIndex, string target, int maxLookAhead)
        {
            int endIndex = Math.Min(startIndex + maxLookAhead, lines.Length);
            for (int i = startIndex; i < endIndex; i++)
            {
                if (lines[i] == target)
                    return i;
            }
            return -1;
        }
        
        private string BytesToHex(byte[] bytes)
        {
            return string.Concat(System.Linq.Enumerable.Select(bytes, b => b.ToString("x2")));
        }

        /// <summary>
        /// Detect renames in a list of changes. Inspired by Git's rename detection.
        /// </summary>
        private List<FileChange> DetectRenames(List<FileChange> changes, DiffOptions options)
        {
            // Separate additions and deletions
            var added = changes.Where(c => c.Type == ChangeType.Added).ToList();
            var deleted = changes.Where(c => c.Type == ChangeType.Deleted).ToList();
            var other = changes.Where(c => c.Type != ChangeType.Added && c.Type != ChangeType.Deleted).ToList();

            if (added.Count == 0 || deleted.Count == 0)
                return changes; // Nothing to detect

            // Apply rename limit to prevent performance issues
            int renameLimit = options.RenameLimit;
            if (renameLimit > 0 && added.Count * deleted.Count > renameLimit * renameLimit)
            {
                UnityEngine.Debug.LogWarning($"[Package Guardian] Rename detection skipped: too many files ({added.Count} added, {deleted.Count} deleted). Increase RenameLimit or disable rename detection.");
                return changes;
            }

            // Find best matches
            var renames = new List<FileChange>();
            var matchedAdded = new HashSet<FileChange>();
            var matchedDeleted = new HashSet<FileChange>();

            // Phase 1: Exact content matches (same OID)
            foreach (var del in deleted)
            {
                var exactMatch = added.FirstOrDefault(add => 
                    !matchedAdded.Contains(add) && add.NewOid == del.OldOid);
                
                if (exactMatch != null)
                {
                    renames.Add(CreateRenameChange(del, exactMatch, 1.0f));
                    matchedAdded.Add(exactMatch);
                    matchedDeleted.Add(del);
                }
            }

            // Phase 2: Inexact matches (similarity scoring)
            var remainingAdded = added.Where(a => !matchedAdded.Contains(a)).ToList();
            var remainingDeleted = deleted.Where(d => !matchedDeleted.Contains(d)).ToList();

            // Build candidate list with scores
            var candidates = new List<(FileChange deleted, FileChange added, float score)>();

            foreach (var del in remainingDeleted)
            {
                foreach (var add in remainingAdded)
                {
                    // Skip if files are too large
                    if (options.MaxFileSizeForRenameDetection > 0)
                    {
                        var delBlob = SafeReadObject(del.OldOid) as Blob;
                        var addBlob = SafeReadObject(add.NewOid) as Blob;
                        
                        // Skip size check if either blob is corrupted (null)
                        if (delBlob == null || addBlob == null)
                            continue;
                        
                        if (delBlob.Data.Length > options.MaxFileSizeForRenameDetection ||
                            addBlob.Data.Length > options.MaxFileSizeForRenameDetection)
                            continue;
                    }

                    // Quick filter: same basename gives bonus, different basename needs high similarity
                    bool sameBasename = SimilarityCalculator.HasSameBasename(del.Path, add.Path);
                    float minRequired = sameBasename ? options.RenameThreshold * 0.7f : options.RenameThreshold;

                    float score = CalculateFileSimilarity(del, add);
                    
                    if (score >= minRequired)
                    {
                        candidates.Add((del, add, score));
                    }
                }
            }

            // Sort by score (descending) and assign best matches
            candidates = candidates.OrderByDescending(c => c.score).ToList();

            foreach (var (del, add, score) in candidates)
            {
                if (matchedDeleted.Contains(del) || matchedAdded.Contains(add))
                    continue; // Already matched

                renames.Add(CreateRenameChange(del, add, score));
                matchedAdded.Add(add);
                matchedDeleted.Add(del);
            }

            // Phase 3: Check for copies if enabled
            if (options.DetectCopies)
            {
                var stillAdded = added.Where(a => !matchedAdded.Contains(a)).ToList();
                
                // For remaining additions, check if they're copies of existing files
                foreach (var add in stillAdded)
                {
                    foreach (var del in deleted)
                    {
                        if (matchedDeleted.Contains(del))
                            continue;

                        float score = CalculateFileSimilarity(del, add);
                        
                        if (score >= options.RenameThreshold)
                        {
                            var copy = new FileChange(del.Path, ChangeType.Copied, del.OldOid, add.NewOid);
                            copy.NewPath = add.Path;
                            copy.SimilarityScore = score;
                            renames.Add(copy);
                            matchedAdded.Add(add);
                            break;
                        }
                    }
                }
            }

            // Rebuild change list
            var result = new List<FileChange>();
            result.AddRange(other);
            result.AddRange(renames);
            result.AddRange(added.Where(a => !matchedAdded.Contains(a)));
            result.AddRange(deleted.Where(d => !matchedDeleted.Contains(d)));

            return result;
        }

        private float CalculateFileSimilarity(FileChange deleted, FileChange added)
        {
            try
            {
                var oldBlob = SafeReadObject(deleted.OldOid) as Blob;
                var newBlob = SafeReadObject(added.NewOid) as Blob;

                if (oldBlob == null || newBlob == null)
                    return 0f;

                // Exact match
                if (deleted.OldOid == added.NewOid)
                    return 1.0f;

                // Content similarity
                float contentScore = SimilarityCalculator.CalculateSimilarity(oldBlob.Data, newBlob.Data);
                
                // Name similarity bonus
                float nameScore = SimilarityCalculator.CalculateNameSimilarity(deleted.Path, added.Path);
                
                // Weighted combination: 80% content, 20% name
                return contentScore * 0.8f + nameScore * 0.2f;
            }
            catch
            {
                return 0f;
            }
        }

        private FileChange CreateRenameChange(FileChange deleted, FileChange added, float score)
        {
            var rename = new FileChange(deleted.Path, ChangeType.Renamed, deleted.OldOid, added.NewOid);
            rename.NewPath = added.Path;
            rename.SimilarityScore = score;
            return rename;
        }
        
        /// <summary>
        /// Compare working directory against a tree.
        /// </summary>
        public List<FileChange> CompareWorkingDirectory(string workingDir, string treeId, IgnoreRules ignoreRules)
        {
            var changes = new List<FileChange>();
            var hasher = new Sha256Hasher();
            
            // Build a map of tree entries
            var treeFiles = new Dictionary<string, (string oid, string mode)>(StringComparer.OrdinalIgnoreCase);
            CollectTreeFilesForWorkingDiff(treeId, "", treeFiles);
            
            // Scan working directory
            var workingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ScanWorkingDirectory(workingDir, "", workingFiles, ignoreRules);
            
            // Find added and modified files
            foreach (var filePath in workingFiles)
            {
                string fullPath = Path.Combine(workingDir, filePath);
                
                if (!File.Exists(fullPath))
                    continue;
                
                // Hash the file
                byte[] fileData = File.ReadAllBytes(fullPath);
                byte[] hash = hasher.Compute(fileData);
                string oid = hasher.ToHex(hash);
                
                if (treeFiles.TryGetValue(filePath, out var treeEntry))
                {
                    // File exists in tree, check if modified
                    if (treeEntry.oid != oid)
                    {
                        changes.Add(new FileChange(filePath, ChangeType.Modified, treeEntry.oid, oid));
                    }
                }
                else
                {
                    // New file
                    changes.Add(new FileChange(filePath, ChangeType.Added, null, oid));
                }
            }
            
            // Find deleted files
            foreach (var kvp in treeFiles)
            {
                if (!workingFiles.Contains(kvp.Key))
                {
                    changes.Add(new FileChange(kvp.Key, ChangeType.Deleted, kvp.Value.oid, null));
                }
            }
            
            return changes;
        }
        
        private void CollectTreeFilesForWorkingDiff(string treeId, string basePath, Dictionary<string, (string oid, string mode)> files)
        {
            var tree = SafeReadObject(treeId) as Tree;
            if (tree == null) return;
            
            foreach (var entry in tree.Entries)
            {
                string fullPath = string.IsNullOrEmpty(basePath) ? entry.Name : $"{basePath}/{entry.Name}";
                string entryOid = BytesToHex(entry.ObjectId);
                
                if (entry.Mode == "040000")
                {
                    // Directory - recurse
                    CollectTreeFilesForWorkingDiff(entryOid, fullPath, files);
                }
                else
                {
                    // File - normalize path separators
                    string normalizedPath = fullPath.Replace('/', Path.DirectorySeparatorChar);
                    files[normalizedPath] = (entryOid, entry.Mode);
                }
            }
        }
        
        private void ScanWorkingDirectory(string rootDir, string relativePath, HashSet<string> files, IgnoreRules ignoreRules)
        {
            string fullPath = string.IsNullOrEmpty(relativePath) ? rootDir : Path.Combine(rootDir, relativePath);
            
            if (!Directory.Exists(fullPath))
                return;
            
            // Scan files
            foreach (string file in Directory.GetFiles(fullPath))
            {
                string fileName = Path.GetFileName(file);
                string fileRelPath = string.IsNullOrEmpty(relativePath) ? fileName : Path.Combine(relativePath, fileName);
                
                if (!ignoreRules.IsIgnored(fileRelPath))
                {
                    files.Add(fileRelPath);
                }
            }
            
            // Scan subdirectories
            foreach (string dir in Directory.GetDirectories(fullPath))
            {
                string dirName = Path.GetFileName(dir);
                string dirRelPath = string.IsNullOrEmpty(relativePath) ? dirName : Path.Combine(relativePath, dirName);
                
                if (!ignoreRules.IsIgnored(dirRelPath))
                {
                    ScanWorkingDirectory(rootDir, dirRelPath, files, ignoreRules);
                }
            }
        }
    }
}

