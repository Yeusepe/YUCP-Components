using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PackageGuardian.Core.Hashing;
using PackageGuardian.Core.Ignore;
using PackageGuardian.Core.Storage;
using PackageGuardian.Core.Transactions;

namespace PackageGuardian.Core.Repository
{
    /// <summary>
    /// Main entry point for Package Guardian repository operations.
    /// </summary>
    public sealed class Repository
    {
        /// <summary>
        /// Repository root directory (Unity project root).
        /// </summary>
        public string Root { get; }
        
        private IObjectStore _store;
        
        /// <summary>
        /// Object storage backend.
        /// </summary>
        public IObjectStore Store => _store;
        
        /// <summary>
        /// Refs database for branches and tags.
        /// </summary>
        public RefDatabase Refs { get; }
        
        /// <summary>
        /// Index cache for fast snapshots.
        /// </summary>
        public IndexCache Index { get; }
        
        /// <summary>
        /// Snapshot builder.
        /// </summary>
        public SnapshotBuilder Snapshots { get; }
        
        /// <summary>
        /// Checkout service.
        /// </summary>
        public CheckoutService Checkout { get; }
        
        /// <summary>
        /// Stash manager.
        /// </summary>
        public StashManager Stash { get; }
        
        /// <summary>
        /// Hasher for content addressing.
        /// </summary>
        public IHasher Hasher { get; }
        
        /// <summary>
        /// Ignore rules engine.
        /// </summary>
        public IgnoreRules IgnoreRules { get; }
        
        /// <summary>
        /// Journal for crash recovery.
        /// </summary>
        public Journal Journal { get; }

        private Repository(string root, IHasher hasher)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            Hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
            
            Journal = new Journal(root);
            _store = new FileObjectStore(root, hasher);
            Refs = new RefDatabase(root, Journal);
            Index = new IndexCache(root);
            
            // Load ignore rules
            string pgignorePath = Path.Combine(root, ".pgignore");
            IgnoreRules = IgnoreRules.FromFile(pgignorePath);
            
            Snapshots = new SnapshotBuilder(root, Store, hasher, Index, IgnoreRules);
            Checkout = new CheckoutService(root, Store, hasher, Index);
            Stash = new StashManager(this);
            
            // Load index
            Index.Load();
            
            // Recover from journal if needed
            RecoverFromJournal();
        }

        /// <summary>
        /// Open or initialize a repository at the given root path.
        /// </summary>
        public static Repository Open(string rootPath, IHasher hasher = null, bool enableObjectCache = true)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentNullException(nameof(rootPath));
            
            if (!Directory.Exists(rootPath))
                throw new DirectoryNotFoundException($"Directory not found: {rootPath}");
            
            hasher = hasher ?? new Sha256Hasher();
            
            // Initialize .pg directory if needed
            string pgDir = Path.Combine(rootPath, ".pg");
            if (!Directory.Exists(pgDir))
            {
                InitializeRepository(rootPath);
            }
            
            var repo = new Repository(rootPath, hasher);
            
            // Wrap object store with cache for better performance
            if (enableObjectCache)
            {
                repo._store = new CachedObjectStore(repo._store, maxCacheSize: 5000);
                UnityEngine.Debug.Log("[Package Guardian] Object caching enabled (5000 objects)");
            }
            
            return repo;
        }

        /// <summary>
        /// Initialize a new repository structure.
        /// </summary>
        private static void InitializeRepository(string rootPath)
        {
            string pgDir = Path.Combine(rootPath, ".pg");
            
            // Create directory structure
            Directory.CreateDirectory(pgDir);
            Directory.CreateDirectory(Path.Combine(pgDir, "objects"));
            Directory.CreateDirectory(Path.Combine(pgDir, "refs", "heads"));
            Directory.CreateDirectory(Path.Combine(pgDir, "refs", "stash", "auto"));
            
            // Create HEAD pointing to main branch
            string headPath = Path.Combine(pgDir, "HEAD");
            File.WriteAllText(headPath, "ref: refs/heads/main");
            
            // Create empty index
            string indexPath = Path.Combine(pgDir, "index.json");
            File.WriteAllText(indexPath, "[]");
            
            // Create config file
            string configPath = Path.Combine(pgDir, "config.json");
            File.WriteAllText(configPath, "{}");
        }

        /// <summary>
        /// Recover from journal after potential crash.
        /// </summary>
        private void RecoverFromJournal()
        {
            var recovered = Journal.Recover();
            
            if (recovered.Count > 0)
            {
                Console.WriteLine($"Recovering {recovered.Count} incomplete transactions from journal");
                
                foreach (var refUpdate in recovered)
                {
                    try
                    {
                        // Complete the ref update
                        string refPath = Path.Combine(Root, ".pg", "refs", refUpdate.RefName.Replace('/', Path.DirectorySeparatorChar));
                        string refDir = Path.GetDirectoryName(refPath);
                        PathHelper.EnsureDirectoryExists(refDir);
                        
                        File.WriteAllText(refPath, refUpdate.NewValue);
                        Console.WriteLine($"Recovered ref: {refUpdate.RefName} -> {refUpdate.NewValue}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to recover ref {refUpdate.RefName}: {ex.Message}");
                    }
                }
                
                // Clear journal after recovery
                Journal.Clear();
            }
        }

        /// <summary>
        /// Create a snapshot commit.
        /// </summary>
        public string CreateSnapshot(string message, string author, string committer = null)
        {
            var options = new SnapshotOptions
            {
                Committer = committer,
                IncludeRoots = new List<string> { "Assets", "Packages" }
            };
            return CreateSnapshot(message, author, options);
        }

        public string CreateSnapshot(string message, string author, SnapshotOptions options)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentNullException(nameof(message));
            if (string.IsNullOrWhiteSpace(author))
                throw new ArgumentNullException(nameof(author));
            options ??= new SnapshotOptions();
            
            string parentCommitId = Refs.ResolveHead();
            string committer = string.IsNullOrWhiteSpace(options.Committer) ? author : options.Committer;
            var roots = options.IncludeRoots ?? new List<string> { "Assets", "Packages" };
            
            if (options.CancellationToken != CancellationToken.None)
            {
                Snapshots.ConfigureExecution(options.CancellationToken);
            }

            if (options.Progress != null)
            {
                Snapshots.OnProgress = options.Progress;
            }

            string commitId = Snapshots.BuildSnapshotCommit(message, author, committer, parentCommitId, roots);
            
            Snapshots.OnProgress = null;
            
            string headRef = Refs.HeadRef;
            if (headRef.StartsWith("refs/"))
            {
                Refs.UpdateRef(headRef, commitId, $"Snapshot: {message}");
            }
            else
            {
                Refs.SetHeadTo(commitId);
            }
            
            return commitId;
        }
    }
}

