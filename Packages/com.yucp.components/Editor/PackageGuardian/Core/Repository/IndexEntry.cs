using System;
using Newtonsoft.Json;

namespace PackageGuardian.Core.Repository
{
    /// <summary>
    /// Cached metadata for a tracked file.
    /// </summary>
    [JsonObject]
    public sealed class IndexEntry
    {
        /// <summary>
        /// Relative path from repository root.
        /// </summary>
        [JsonProperty("path")]
        public string Path { get; set; }
        
        /// <summary>
        /// File size in bytes.
        /// </summary>
        [JsonProperty("size")]
        public long Size { get; set; }
        
        /// <summary>
        /// Last modified time in UTC ticks.
        /// </summary>
        [JsonProperty("mtime")]
        public long MTimeUtc { get; set; }
        
        /// <summary>
        /// Cached object ID (hex string).
        /// </summary>
        [JsonProperty("oid")]
        public string Oid { get; set; }

        [JsonConstructor]
        public IndexEntry()
        {
        }

        public IndexEntry(string path, long size, long mtimeUtc, string oid)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Size = size;
            MTimeUtc = mtimeUtc;
            Oid = oid ?? throw new ArgumentNullException(nameof(oid));
        }
    }
}

