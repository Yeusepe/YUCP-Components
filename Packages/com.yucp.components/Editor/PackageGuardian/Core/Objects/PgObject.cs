using System;

namespace PackageGuardian.Core.Objects
{
    /// <summary>
    /// Base class for all Package Guardian objects (blobs, trees, commits).
    /// </summary>
    public abstract record PgObject
    {
        /// <summary>
        /// Object type identifier (blob, tree, commit).
        /// </summary>
        public string Type { get; }
        
        /// <summary>
        /// Serialized payload bytes.
        /// </summary>
        public byte[] Payload { get; }

        protected PgObject(string type, byte[] payload)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }
    }
}

