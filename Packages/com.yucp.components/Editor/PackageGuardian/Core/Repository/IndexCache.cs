using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace PackageGuardian.Core.Repository
{
    /// <summary>
    /// Index cache to avoid rehashing unchanged files.
    /// Stored as JSON at .pg/index.json.
    /// </summary>
    public sealed class IndexCache
    {
        private readonly string _indexPath;
        private Dictionary<string, IndexEntry> _entries;
        private readonly object _lock = new object();

        public IndexCache(string repositoryRoot)
        {
            if (string.IsNullOrWhiteSpace(repositoryRoot))
                throw new ArgumentNullException(nameof(repositoryRoot));
            
            string pgDir = Path.Combine(repositoryRoot, ".pg");
            _indexPath = Path.Combine(pgDir, "index.json");
            _entries = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Load index from disk.
        /// </summary>
        public void Load()
        {
            lock (_lock)
            {
                if (!File.Exists(_indexPath))
                {
                    _entries = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);
                    return;
                }
                
                try
                {
                    string json = File.ReadAllText(_indexPath);
                    var entries = JsonConvert.DeserializeObject<List<IndexEntry>>(json);
                    
                    _entries = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);
                    if (entries != null)
                    {
                        foreach (var entry in entries)
                        {
                            _entries[entry.Path] = entry;
                        }
                    }
                }
                catch
                {
                    // If index is corrupted, start fresh
                    _entries = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        /// <summary>
        /// Save index to disk.
        /// </summary>
        public void Save()
        {
            lock (_lock)
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
        }

        /// <summary>
        /// Try to get cached entry for a path.
        /// </summary>
        public bool TryGet(string relativePath, out IndexEntry entry)
        {
            lock (_lock)
            {
                return _entries.TryGetValue(relativePath, out entry);
            }
        }

        /// <summary>
        /// Put or update entry in cache.
        /// </summary>
        public void Put(IndexEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            
            lock (_lock)
            {
                _entries[entry.Path] = entry;
            }
        }

        /// <summary>
        /// Remove entry from cache.
        /// </summary>
        public void Remove(string relativePath)
        {
            lock (_lock)
            {
                _entries.Remove(relativePath);
            }
        }

        /// <summary>
        /// Clear all entries from cache.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }

        /// <summary>
        /// Get all cached paths.
        /// </summary>
        public IEnumerable<string> GetAllPaths()
        {
            lock (_lock)
            {
                return new List<string>(_entries.Keys);
            }
        }
    }
}

