using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PackageGuardian.Core.Hashing;
using PackageGuardian.Core.Ignore;
using PackageGuardian.Core.Objects;
using PackageGuardian.Core.Storage;

namespace PackageGuardian.Core.Repository
{
    /// <summary>
    /// Builds snapshots of the working directory.
    /// </summary>
    public sealed class SnapshotBuilder
    {
        private readonly string _repositoryRoot;
        private readonly IObjectStore _store;
        private readonly IHasher _hasher;
        private readonly IndexCache _index;
        private readonly IgnoreRules _ignoreRules;
        
        // Progress tracking
        private int _totalFiles;
        private int _processedFiles;
        public Action<int, int, string> OnProgress;

        public SnapshotBuilder(string repositoryRoot, IObjectStore store, IHasher hasher, IndexCache index, IgnoreRules ignoreRules)
        {
            _repositoryRoot = repositoryRoot ?? throw new ArgumentNullException(nameof(repositoryRoot));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
            _index = index ?? throw new ArgumentNullException(nameof(index));
            _ignoreRules = ignoreRules ?? throw new ArgumentNullException(nameof(ignoreRules));
        }

        /// <summary>
        /// Build a complete snapshot commit of the working directory.
        /// </summary>
        public string BuildSnapshotCommit(string message, string author, string committer, string parentCommitId = null, IEnumerable<string> includeRoots = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentNullException(nameof(message));
            if (string.IsNullOrWhiteSpace(author))
                throw new ArgumentNullException(nameof(author));
            if (string.IsNullOrWhiteSpace(committer))
                committer = author;
            
            // Default roots: Assets and Packages
            var roots = includeRoots?.ToList() ?? new List<string> { "Assets", "Packages" };
            
            // Build tree from disk
            string treeOid = BuildTreeFromDisk(".", roots);
            
            // Convert tree OID from hex to bytes
            byte[] treeId = HexToBytes(treeOid);
            
            // Build parent list
            var parents = new List<byte[]>();
            if (!string.IsNullOrWhiteSpace(parentCommitId))
            {
                parents.Add(HexToBytes(parentCommitId));
            }
            
            // Create commit
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var commit = new Commit(treeId, parents, author, committer, timestamp, message);
            
            // Write commit to store
            string commitOid = _store.WriteObject(commit, out _);
            
            // Save index
            _index.Save();
            
            return commitOid;
        }

        /// <summary>
        /// Build tree from disk starting at a relative path.
        /// </summary>
        public string BuildTreeFromDisk(string rootRelativePath = ".", IEnumerable<string> includeRoots = null)
        {
            string fullPath = Path.Combine(_repositoryRoot, rootRelativePath);
            if (!Directory.Exists(fullPath))
                throw new DirectoryNotFoundException($"Directory not found: {fullPath}");
            
            var entries = new List<TreeEntry>();
            var roots = includeRoots?.ToList() ?? new List<string> { "Assets", "Packages" };
            
            // Process each root directory
            foreach (string root in roots)
            {
                string rootPath = Path.Combine(_repositoryRoot, root);
                if (!Directory.Exists(rootPath))
                    continue;
                
                string rootOid = BuildTreeRecursive(root);
                if (!string.IsNullOrWhiteSpace(rootOid))
                {
                    entries.Add(new TreeEntry("040000", root, HexToBytes(rootOid)));
                }
            }
            
            // Create root tree
            if (entries.Count == 0)
            {
                // Empty tree
                var emptyTree = new Tree(Enumerable.Empty<TreeEntry>());
                return _store.WriteObject(emptyTree, out _);
            }
            
            var tree = new Tree(entries);
            return _store.WriteObject(tree, out _);
        }

        private string BuildTreeRecursive(string relativePath)
        {
            string fullPath = Path.Combine(_repositoryRoot, relativePath);
            
            if (!Directory.Exists(fullPath))
                return null;
            
            var entries = new List<TreeEntry>();
            
            // Process files
            foreach (string file in Directory.GetFiles(fullPath))
            {
                string relPath = PathHelper.GetRelativePath(_repositoryRoot, file);
                
                // Check ignore rules
                if (_ignoreRules.IsIgnored(relPath))
                    continue;
                
                // Hash file and store as blob
                string blobOid = HashFile(file, relPath);
                if (!string.IsNullOrWhiteSpace(blobOid))
                {
                    string fileName = Path.GetFileName(file);
                    entries.Add(new TreeEntry("100644", fileName, HexToBytes(blobOid)));
                }
            }
            
            // Process subdirectories
            foreach (string dir in Directory.GetDirectories(fullPath))
            {
                string relPath = PathHelper.GetRelativePath(_repositoryRoot, dir);
                
                // Check ignore rules
                if (_ignoreRules.IsIgnored(relPath))
                    continue;
                
                string treeOid = BuildTreeRecursive(relPath);
                if (!string.IsNullOrWhiteSpace(treeOid))
                {
                    string dirName = Path.GetFileName(dir);
                    entries.Add(new TreeEntry("040000", dirName, HexToBytes(treeOid)));
                }
            }
            
            if (entries.Count == 0)
                return null; // Empty directory, skip
            
            // Create tree object
            var tree = new Tree(entries);
            return _store.WriteObject(tree, out _);
        }

        private string HashFile(string fullPath, string relativePath)
        {
            try
            {
                var fileInfo = new FileInfo(fullPath);
                if (!fileInfo.Exists)
                    return null;
                
                // Report progress
                _processedFiles++;
                OnProgress?.Invoke(_processedFiles, _totalFiles, relativePath);
                
                // Check index cache
                if (_index.TryGet(relativePath, out var cachedEntry))
                {
                    // Use cached OID if size and mtime match
                    if (cachedEntry.Size == fileInfo.Length &&
                        cachedEntry.MTimeUtc == fileInfo.LastWriteTimeUtc.Ticks)
                    {
                        return cachedEntry.Oid;
                    }
                }
                
                // Read file and create blob
                byte[] content = File.ReadAllBytes(fullPath);
                var blob = new Blob(content);
                string oid = _store.WriteObject(blob, out _);
                
                // Update index
                var entry = new IndexEntry(
                    relativePath,
                    fileInfo.Length,
                    fileInfo.LastWriteTimeUtc.Ticks,
                    oid);
                _index.Put(entry);
                
                return oid;
            }
            catch (Exception ex)
            {
                // Log error but continue
                Console.WriteLine($"Error hashing file {relativePath}: {ex.Message}");
                return null;
            }
        }

        private byte[] HexToBytes(string hex)
        {
            if (hex.Length % 2 != 0)
                throw new ArgumentException("Hex string must have even length", nameof(hex));
            
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
    }
}

