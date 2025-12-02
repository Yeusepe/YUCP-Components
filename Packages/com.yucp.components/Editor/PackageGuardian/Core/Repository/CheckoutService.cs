using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PackageGuardian.Core.Hashing;
using PackageGuardian.Core.Objects;
using PackageGuardian.Core.Storage;

namespace PackageGuardian.Core.Repository
{
    /// <summary>
    /// Checkout service for restoring files from commits.
    /// </summary>
    public sealed class CheckoutService
    {
        private readonly string _repositoryRoot;
        private readonly IObjectStore _store;
        private readonly IHasher _hasher;
        private readonly IndexCache _index;
        private readonly List<string> _lockedFiles = new List<string>();

        public CheckoutService(string repositoryRoot, IObjectStore store, IHasher hasher, IndexCache index)
        {
            _repositoryRoot = repositoryRoot ?? throw new ArgumentNullException(nameof(repositoryRoot));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
            _index = index ?? throw new ArgumentNullException(nameof(index));
        }

        /// <summary>
        /// Gets the list of files that were locked during the last checkout operation.
        /// </summary>
        public IReadOnlyList<string> LockedFiles => _lockedFiles.AsReadOnly();

        /// <summary>
        /// Checkout a complete commit to the working directory.
        /// </summary>
        public void CheckoutCommit(string commitId, CheckoutOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(commitId))
                throw new ArgumentNullException(nameof(commitId));
            
            options = options ?? CheckoutOptions.Default;
            
            // Clear locked files list
            _lockedFiles.Clear();
            
            // Read commit
            var commitObj = _store.ReadObject(commitId);
            if (commitObj is not Commit commit)
                throw new InvalidOperationException($"Object {commitId} is not a commit");
            
            // Get tree OID
            string treeOid = BytesToHex(commit.TreeId);
            
            // Collect all files in the target commit
            var targetFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectFilesFromTree(treeOid, "", targetFiles);
            
            // Get all tracked files from current index
            var currentFiles = _index.GetAllPaths().ToHashSet(StringComparer.OrdinalIgnoreCase);
            
            // Delete files that exist in index but not in target commit
            foreach (string filePath in currentFiles)
            {
                if (!targetFiles.Contains(filePath))
                {
                    string fullPath = Path.Combine(_repositoryRoot, filePath);
                    if (File.Exists(fullPath))
                    {
                        try
                        {
                            File.Delete(fullPath);
                            UnityEngine.Debug.Log($"[Package Guardian] Deleted file not in target commit: {filePath}");
                        }
                        catch (Exception ex)
                        {
                            UnityEngine.Debug.LogWarning($"[Package Guardian] Could not delete {filePath}: {ex.Message}");
                            _lockedFiles.Add(filePath);
                        }
                    }
                    
                    // Remove from index
                    _index.Remove(filePath);
                }
            }
            
            // Checkout tree
            CheckoutTree(treeOid, _repositoryRoot, options);
            
            // Update index
            _index.Save();
            
            // If there were locked files, just warn and continue
            if (_lockedFiles.Count > 0)
            {
                // Deduplicate locked files list
                var uniqueLockedFiles = _lockedFiles.Distinct().Take(5).ToList();
                string fileList = string.Join("\n  - ", uniqueLockedFiles);
                if (_lockedFiles.Distinct().Count() > 5)
                    fileList += $"\n  ... and {_lockedFiles.Distinct().Count() - 5} more files";
                    
                UnityEngine.Debug.LogWarning(
                    $"[Package Guardian] Rollback completed with {_lockedFiles.Distinct().Count()} locked file(s):\n  - {fileList}\n\n" +
                    "These are typically native plugin DLLs loaded by Unity. They will be updated next time Unity restarts.");
            }
        }

        /// <summary>
        /// Checkout specific paths from a commit.
        /// </summary>
        public void CheckoutPaths(string commitId, IEnumerable<string> paths, CheckoutOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(commitId))
                throw new ArgumentNullException(nameof(commitId));
            if (paths == null)
                throw new ArgumentNullException(nameof(paths));
            
            options = options ?? CheckoutOptions.Default;
            
            // Read commit
            var commitObj = _store.ReadObject(commitId);
            if (commitObj is not Commit commit)
                throw new InvalidOperationException($"Object {commitId} is not a commit");
            
            // Get tree OID
            string treeOid = BytesToHex(commit.TreeId);
            
            // Checkout each path
            foreach (string path in paths)
            {
                CheckoutPath(treeOid, path, options);
            }
            
            // Update index
            _index.Save();
        }

        private void CheckoutTree(string treeOid, string targetDir, CheckoutOptions options)
        {
            // Read tree
            var treeObj = _store.ReadObject(treeOid);
            if (treeObj is not Tree tree)
                throw new InvalidOperationException($"Object {treeOid} is not a tree");
            
            PathHelper.EnsureDirectoryExists(targetDir);
            
            // Checkout each entry
            foreach (var entry in tree.Entries)
            {
                string entryPath = Path.Combine(targetDir, entry.Name);
                string entryOid = BytesToHex(entry.ObjectId);
                
                if (entry.Mode == "040000")
                {
                    // Directory - recurse
                    CheckoutTree(entryOid, entryPath, options);
                }
                else
                {
                    // File - checkout blob
                    CheckoutBlob(entryOid, entryPath, options);
                }
            }
        }

        private void CheckoutBlob(string blobOid, string targetPath, CheckoutOptions options)
        {
            // Check if file exists and we shouldn't overwrite
            if (File.Exists(targetPath) && !options.Overwrite)
                return;
            
            // Read blob
            var blobObj = _store.ReadObject(blobOid);
            if (blobObj is not Blob blob)
                throw new InvalidOperationException($"Object {blobOid} is not a blob");
            
            // Write to temp file
            string tempPath = targetPath + ".pg_tmp";
            try
            {
                PathHelper.EnsureDirectoryExists(Path.GetDirectoryName(targetPath));
                File.WriteAllBytes(tempPath, blob.Data);
                
                // Verify integrity if requested
                if (options.VerifyIntegrity)
                {
                    byte[] writtenData = File.ReadAllBytes(tempPath);
                    var verifyBlob = new Blob(writtenData);
                    string verifyOid = ObjectSerializer.ComputeObjectId(verifyBlob, _hasher);
                    
                    if (!string.Equals(verifyOid, blobOid, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Integrity check failed for {targetPath}");
                }
                
                // Get relative path (used for both locked files tracking and index)
                string relativePath = PathHelper.GetRelativePath(_repositoryRoot, targetPath);
                
                // Atomic move with retry logic for locked files
                if (!TrySafeFileReplace(tempPath, targetPath))
                {
                    // File is locked, track it and continue
                    _lockedFiles.Add(relativePath);
                    
                    // Clean up temp file
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { /* ignore */ }
                    }
                    return; // Skip this file and continue with others
                }
                
                // Update index
                var fileInfo = new FileInfo(targetPath);
                var entry = new IndexEntry(
                    relativePath,
                    fileInfo.Length,
                    fileInfo.LastWriteTimeUtc.Ticks,
                    blobOid);
                _index.Put(entry);
            }
            catch
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                throw;
            }
        }

        private void CheckoutPath(string rootTreeOid, string relativePath, CheckoutOptions options)
        {
            // Split path into segments
            string normalizedPath = relativePath.Replace('\\', '/');
            string[] segments = normalizedPath.Split('/');
            
            // Navigate tree to find the target
            string currentTreeOid = rootTreeOid;
            
            for (int i = 0; i < segments.Length - 1; i++)
            {
                var treeObj = _store.ReadObject(currentTreeOid);
                if (treeObj is not Tree tree)
                    throw new InvalidOperationException($"Object {currentTreeOid} is not a tree");
                
                var entry = tree.Entries.FirstOrDefault(e => e.Name == segments[i]);
                if (entry == null)
                    throw new FileNotFoundException($"Path not found: {string.Join("/", segments.Take(i + 1))}");
                
                currentTreeOid = BytesToHex(entry.ObjectId);
            }
            
            // Get final entry
            var finalTreeObj = _store.ReadObject(currentTreeOid);
            if (finalTreeObj is not Tree finalTree)
                throw new InvalidOperationException($"Object {currentTreeOid} is not a tree");
            
            string fileName = segments[segments.Length - 1];
            var finalEntry = finalTree.Entries.FirstOrDefault(e => e.Name == fileName);
            if (finalEntry == null)
                throw new FileNotFoundException($"File not found: {relativePath}");
            
            // Checkout the entry
            string targetPath = Path.Combine(_repositoryRoot, relativePath);
            string entryOid = BytesToHex(finalEntry.ObjectId);
            
            if (finalEntry.Mode == "040000")
            {
                CheckoutTree(entryOid, targetPath, options);
            }
            else
            {
                CheckoutBlob(entryOid, targetPath, options);
            }
        }

        private string BytesToHex(byte[] bytes)
        {
            return _hasher.ToHex(bytes);
        }

        /// <summary>
        /// Recursively collects all file paths from a tree.
        /// </summary>
        private void CollectFilesFromTree(string treeOid, string basePath, HashSet<string> filePaths)
        {
            var treeObj = _store.ReadObject(treeOid);
            if (treeObj is not Tree tree)
                return;
            
            foreach (var entry in tree.Entries)
            {
                string entryPath = string.IsNullOrEmpty(basePath) ? entry.Name : $"{basePath}/{entry.Name}";
                string entryOid = BytesToHex(entry.ObjectId);
                
                if (entry.Mode == "040000")
                {
                    // Directory - recurse
                    CollectFilesFromTree(entryOid, entryPath, filePaths);
                }
                else
                {
                    // File - add to set
                    // Normalize path separators
                    string normalizedPath = entryPath.Replace('/', Path.DirectorySeparatorChar);
                    filePaths.Add(normalizedPath);
                }
            }
        }

        /// <summary>
        /// Attempts to safely replace a file with retry logic for locked files.
        /// </summary>
        /// <returns>True if successful, false if file is locked and cannot be replaced.</returns>
        private bool TrySafeFileReplace(string sourcePath, string targetPath)
        {
            const int maxRetries = 3;
            const int delayMs = 100;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    // Try to delete existing file
                    if (File.Exists(targetPath))
                    {
                        // Try to make it writable first
                        try
                        {
                            var attrs = File.GetAttributes(targetPath);
                            if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                            {
                                File.SetAttributes(targetPath, attrs & ~FileAttributes.ReadOnly);
                            }
                        }
                        catch { /* ignore attribute errors */ }

                        File.Delete(targetPath);
                    }

                    // Move the new file into place
                    File.Move(sourcePath, targetPath);
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    // File is locked, try waiting a bit
                    if (attempt < maxRetries - 1)
                    {
                        Thread.Sleep(delayMs);
                        continue;
                    }
                    
                    // All retries exhausted, file is definitely locked
                    return false;
                }
                catch (IOException ex) when (ex.Message.Contains("being used by another process"))
                {
                    // File is in use, try waiting a bit
                    if (attempt < maxRetries - 1)
                    {
                        Thread.Sleep(delayMs);
                        continue;
                    }
                    
                    // All retries exhausted
                    return false;
                }
            }

            return false;
        }
    }
}

