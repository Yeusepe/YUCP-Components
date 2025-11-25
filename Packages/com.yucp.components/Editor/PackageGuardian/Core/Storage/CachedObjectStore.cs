using System;
using System.Collections.Concurrent;
using PackageGuardian.Core.Objects;

namespace PackageGuardian.Core.Storage
{
    /// <summary>
    /// Wraps an IObjectStore with an in-memory cache. Inspired by Git's parsed object pool.
    /// This significantly improves performance when the same objects are read multiple times.
    /// </summary>
    public sealed class CachedObjectStore : IObjectStore
    {
        private readonly IObjectStore _inner;
        private readonly ConcurrentDictionary<string, PgObject> _cache;
        private readonly int _maxCacheSize;
        private long _hits;
        private long _misses;

        public CachedObjectStore(IObjectStore inner, int maxCacheSize = 10000)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _cache = new ConcurrentDictionary<string, PgObject>(StringComparer.OrdinalIgnoreCase);
            _maxCacheSize = maxCacheSize;
            _hits = 0;
            _misses = 0;
        }

        public PgObject ReadObject(string oid)
        {
            if (string.IsNullOrWhiteSpace(oid))
                throw new ArgumentNullException(nameof(oid));

            // Try cache first
            if (_cache.TryGetValue(oid, out var cached))
            {
                System.Threading.Interlocked.Increment(ref _hits);
                return cached;
            }

            // Cache miss - read from underlying store
            System.Threading.Interlocked.Increment(ref _misses);
            var obj = _inner.ReadObject(oid);

            // Add to cache (with size limit)
            if (_cache.Count < _maxCacheSize)
            {
                _cache.TryAdd(oid, obj);
            }
            else if (_cache.Count == _maxCacheSize)
            {
                // Log warning once when cache is full
                UnityEngine.Debug.LogWarning($"[Package Guardian] Object cache full ({_maxCacheSize} objects). Consider increasing cache size.");
            }

            return obj;
        }

        public string WriteObject(PgObject obj, out byte[] serializedData)
        {
            string oid = _inner.WriteObject(obj, out serializedData);
            
            // Add to cache after writing
            if (_cache.Count < _maxCacheSize)
            {
                _cache.TryAdd(oid, obj);
            }
            
            return oid;
        }

        public StagedObjectWrite StageObject(PgObject obj)
        {
            return _inner.StageObject(obj);
        }

        public string CommitStagedObject(StagedObjectWrite staged)
        {
            string oid = _inner.CommitStagedObject(staged);
            if (!_cache.ContainsKey(oid) && _cache.Count < _maxCacheSize && staged.SourceObject != null)
            {
                _cache.TryAdd(oid, staged.SourceObject);
            }
            return oid;
        }

        public bool Contains(string oid)
        {
            // Check cache first
            if (_cache.ContainsKey(oid))
                return true;

            return _inner.Contains(oid);
        }
        
        private string BytesToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            
            var sb = new System.Text.StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Clear the cache. Useful after large operations or to free memory.
        /// </summary>
        public void ClearCache()
        {
            _cache.Clear();
            UnityEngine.Debug.Log($"[Package Guardian] Object cache cleared. Stats: {_hits} hits, {_misses} misses ({GetHitRate():F1}% hit rate)");
            _hits = 0;
            _misses = 0;
        }

        /// <summary>
        /// Get cache statistics.
        /// </summary>
        public (int size, long hits, long misses, double hitRate) GetStats()
        {
            long totalRequests = _hits + _misses;
            double hitRate = totalRequests > 0 ? (_hits * 100.0 / totalRequests) : 0;
            return (_cache.Count, _hits, _misses, hitRate);
        }

        private double GetHitRate()
        {
            long total = _hits + _misses;
            return total > 0 ? (_hits * 100.0 / total) : 0;
        }

        /// <summary>
        /// Get the underlying store (useful for direct access when needed).
        /// </summary>
        public IObjectStore Inner => _inner;
    }
}

