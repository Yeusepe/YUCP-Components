using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PackageGuardian.Core.Storage;
using PackageGuardian.Core.Transactions;

namespace PackageGuardian.Core.Repository
{
    /// <summary>
    /// Manages refs (branches, tags, stashes) stored at .pg/refs/.
    /// </summary>
    public sealed class RefDatabase
    {
        private readonly string _refsPath;
        private readonly string _headPath;
        private readonly Journal _journal;

        public RefDatabase(string repositoryRoot, Journal journal)
        {
            if (string.IsNullOrWhiteSpace(repositoryRoot))
                throw new ArgumentNullException(nameof(repositoryRoot));
            
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
            
            string pgDir = Path.Combine(repositoryRoot, ".pg");
            _refsPath = Path.Combine(pgDir, "refs");
            _headPath = Path.Combine(pgDir, "HEAD");
            
            PathHelper.EnsureDirectoryExists(_refsPath);
        }

        /// <summary>
        /// Get the current HEAD reference (e.g., "refs/heads/main").
        /// </summary>
        public string HeadRef
        {
            get
            {
                if (!File.Exists(_headPath))
                    return "refs/heads/main"; // Default
                
                string content = File.ReadAllText(_headPath).Trim();
                
                // HEAD can be symbolic (ref: refs/heads/main) or direct (commit id)
                if (content.StartsWith("ref: "))
                    return content.Substring(5).Trim();
                
                return content; // Direct commit ID
            }
        }

        /// <summary>
        /// Resolve HEAD to a commit ID.
        /// </summary>
        public string ResolveHead()
        {
            string headRef = HeadRef;
            
            // If HEAD is a direct commit ID, return it
            if (!headRef.StartsWith("refs/"))
                return headRef;
            
            // Otherwise resolve the ref
            return ReadRef(headRef);
        }

        /// <summary>
        /// Set HEAD to point to a ref or commit.
        /// </summary>
        public void SetHeadTo(string refNameOrCommitId)
        {
            if (string.IsNullOrWhiteSpace(refNameOrCommitId))
                throw new ArgumentNullException(nameof(refNameOrCommitId));
            
            string content;
            if (refNameOrCommitId.StartsWith("refs/"))
            {
                // Symbolic ref
                content = $"ref: {refNameOrCommitId}";
            }
            else
            {
                // Direct commit ID
                content = refNameOrCommitId;
            }
            
            File.WriteAllText(_headPath, content);
        }

        /// <summary>
        /// Update a ref to point to a commit, with journal protection.
        /// </summary>
        public void UpdateRef(string refName, string commitId, string description = null)
        {
            if (string.IsNullOrWhiteSpace(refName))
                throw new ArgumentNullException(nameof(refName));
            if (string.IsNullOrWhiteSpace(commitId))
                throw new ArgumentNullException(nameof(commitId));
            
            string refPath = GetRefPath(refName);
            string refDir = Path.GetDirectoryName(refPath);
            PathHelper.EnsureDirectoryExists(refDir);
            
            UnityEngine.Debug.Log($"[Package Guardian] UpdateRef: {refName} -> {commitId} at path: {refPath}");
            
            // Use journal for crash safety
            using var transaction = new JournaledRefUpdate(
                _journal,
                refName,
                commitId,
                description ?? $"Update {refName}",
                () => File.WriteAllText(refPath, commitId));
            
            transaction.Commit();
            
            UnityEngine.Debug.Log($"[Package Guardian] Ref written successfully. File exists: {File.Exists(refPath)}");
        }

        /// <summary>
        /// Read a ref value (commit ID).
        /// </summary>
        public string ReadRef(string refName)
        {
            if (string.IsNullOrWhiteSpace(refName))
                return null;
            
            string refPath = GetRefPath(refName);
            if (!File.Exists(refPath))
                return null;
            
            return File.ReadAllText(refPath).Trim();
        }

        /// <summary>
        /// Check if a ref exists.
        /// </summary>
        public bool RefExists(string refName)
        {
            string refPath = GetRefPath(refName);
            return File.Exists(refPath);
        }

        /// <summary>
        /// Delete a ref.
        /// </summary>
        public void DeleteRef(string refName)
        {
            if (string.IsNullOrWhiteSpace(refName))
                throw new ArgumentNullException(nameof(refName));
            
            string refPath = GetRefPath(refName);
            if (File.Exists(refPath))
            {
                File.Delete(refPath);
            }
        }

        /// <summary>
        /// List all refs matching a prefix (e.g., "refs/heads/", "refs/stash/").
        /// </summary>
        public IEnumerable<string> ListRefs(string prefix = "refs/")
        {
            // Remove "refs/" prefix if present since _refsPath already points to .pg/refs/
            string searchPrefix = prefix.StartsWith("refs/") ? prefix.Substring(5) : prefix;
            searchPrefix = searchPrefix.TrimStart('/');
            
            string prefixPath = string.IsNullOrEmpty(searchPrefix) 
                ? _refsPath 
                : Path.Combine(_refsPath, searchPrefix.Replace('/', Path.DirectorySeparatorChar));
            
            if (!Directory.Exists(prefixPath))
            {
                return Enumerable.Empty<string>();
            }
            
            var refs = new List<string>();
            foreach (string file in Directory.EnumerateFiles(prefixPath, "*", SearchOption.AllDirectories))
            {
                string relativePath = PathHelper.GetRelativePath(_refsPath, file);
                // Normalize path separators to forward slashes for ref names
                relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
                string refName = "refs/" + relativePath;
                refs.Add(refName);
            }
            
            return refs;
        }

        private string GetRefPath(string refName)
        {
            // Remove "refs/" prefix if present for path construction
            string relativePath = refName.StartsWith("refs/") ? refName.Substring(5) : refName;
            relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            
            return Path.Combine(_refsPath, relativePath);
        }
    }
}

