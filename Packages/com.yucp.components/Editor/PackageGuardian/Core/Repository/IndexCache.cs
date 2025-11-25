using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Threading;

namespace PackageGuardian.Core.Repository
{
    /// <summary>
    /// Index cache to avoid rehashing unchanged files.
    /// Stored as JSON at .pg/index.json.
    /// </summary>
    public sealed class IndexCache
    {
        private readonly string _indexPath;
        private readonly ConcurrentDictionary<string, IndexEntry> _entries;
        private readonly ReaderWriterLockSlim _persistenceLock = new ReaderWriterLockSlim();

        public IndexCache(string repositoryRoot)
        {
            if (string.IsNullOrWhiteSpace(repositoryRoot))
                throw new ArgumentNullException(nameof(repositoryRoot));
            
            string pgDir = Path.Combine(repositoryRoot, ".pg");
            _indexPath = Path.Combine(pgDir, "index.json");
            _entries = new ConcurrentDictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Load index from disk.
        /// </summary>
        public void Load()
        {
            _persistenceLock.EnterWriteLock();
            try
            {
                if (!File.Exists(_indexPath))
                {
                    _entries.Clear();
                    return;
                }
                
                try
                {
                    string json = File.ReadAllText(_indexPath);
                    var entries = JsonConvert.DeserializeObject<List<IndexEntry>>(json);
                    
                    _entries.Clear();
                    if (entries == null)
                        return;
                    
                    foreach (var entry in entries)
                    {
                        if (entry?.Path == null)
                            continue;
                        _entries[entry.Path] = entry;
                    }
                }
                catch
                {
                    _entries.Clear();
                }
            }
            finally
            {
                _persistenceLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Save index to disk.
        /// </summary>
        public void Save()
        {
            _persistenceLock.EnterWriteLock();
            try
            {
                var entries = new List<IndexEntry>(_entries.Values);
                string json = JsonConvert.SerializeObject(entries, Formatting.Indented);
                
                // Write atomically via temp file
                string tempPath = _indexPath + ".tmp";
                File.WriteAllText(tempPath, json);
                
                try
                {
                    if (File.Exists(_indexPath))
                        File.Delete(_indexPath);
                    File.Move(tempPath, _indexPath);
                }
                catch
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                    throw;
                }
            }
            finally
            {
                _persistenceLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Try to get cached entry for a path.
        /// </summary>
        public bool TryGet(string relativePath, out IndexEntry entry)
        {
            return _entries.TryGetValue(relativePath, out entry);
        }

        /// <summary>
        /// Put or update entry in cache.
        /// </summary>
        public void Put(IndexEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            
            _entries[entry.Path] = entry;
        }

        /// <summary>
        /// Remove entry from cache.
        /// </summary>
        public void Remove(string relativePath)
        {
            _entries.TryRemove(relativePath, out _);
        }

        /// <summary>
        /// Clear all entries from cache.
        /// </summary>
        public void Clear()
        {
            _entries.Clear();
        }

        /// <summary>
        /// Get all cached paths.
        /// </summary>
        public IEnumerable<string> GetAllPaths()
        {
            return new List<string>(_entries.Keys);
        }
    }
}

