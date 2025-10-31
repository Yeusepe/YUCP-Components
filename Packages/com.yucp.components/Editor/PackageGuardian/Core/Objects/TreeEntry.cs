using System;

namespace PackageGuardian.Core.Objects
{
    /// <summary>
    /// A single entry in a tree object representing a file or subdirectory.
    /// </summary>
    public sealed record TreeEntry
    {
        /// <summary>
        /// File mode (e.g., "100644" for regular file, "040000" for directory).
        /// </summary>
        public string Mode { get; }
        
        /// <summary>
        /// File or directory name.
        /// </summary>
        public string Name { get; }
        
        /// <summary>
        /// Object ID of the blob or tree (32 raw bytes).
        /// </summary>
        public byte[] ObjectId { get; }

        public TreeEntry(string mode, string name, byte[] objectId)
        {
            Mode = mode ?? throw new ArgumentNullException(nameof(mode));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ObjectId = objectId ?? throw new ArgumentNullException(nameof(objectId));
            
            if (objectId.Length != 32)
                throw new ArgumentException("ObjectId must be exactly 32 bytes (SHA-256)", nameof(objectId));
        }
    }
}

