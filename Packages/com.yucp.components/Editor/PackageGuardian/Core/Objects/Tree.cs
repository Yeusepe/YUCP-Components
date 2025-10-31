using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PackageGuardian.Core.Objects
{
    /// <summary>
    /// Represents a directory tree with entries sorted by name.
    /// Binary format: [mode name\0 32-byte-oid]+
    /// </summary>
    public sealed record Tree : PgObject
    {
        /// <summary>
        /// Sorted list of tree entries.
        /// </summary>
        public IReadOnlyList<TreeEntry> Entries { get; }

        public Tree(IEnumerable<TreeEntry> entries) : base("tree", SerializeEntries(entries))
        {
            // Sort entries by name for consistent hashing
            Entries = entries.OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        }

        private Tree(IReadOnlyList<TreeEntry> entries, byte[] payload) : base("tree", payload)
        {
            Entries = entries;
        }

        private static byte[] SerializeEntries(IEnumerable<TreeEntry> entries)
        {
            using var ms = new MemoryStream();
            
            foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                // Write: mode name\0 [32-byte oid]
                var modeBytes = Encoding.UTF8.GetBytes(entry.Mode);
                ms.Write(modeBytes, 0, modeBytes.Length);
                ms.WriteByte((byte)' ');
                
                var nameBytes = Encoding.UTF8.GetBytes(entry.Name);
                ms.Write(nameBytes, 0, nameBytes.Length);
                ms.WriteByte(0); // null terminator
                
                ms.Write(entry.ObjectId, 0, 32);
            }
            
            return ms.ToArray();
        }

        /// <summary>
        /// Deserialize tree from payload bytes.
        /// </summary>
        public static Tree Deserialize(byte[] payload)
        {
            var entries = new List<TreeEntry>();
            int pos = 0;
            
            while (pos < payload.Length)
            {
                // Read mode (until space)
                int spacePos = Array.IndexOf(payload, (byte)' ', pos);
                if (spacePos == -1)
                    throw new InvalidDataException("Invalid tree format: missing space after mode");
                
                string mode = Encoding.UTF8.GetString(payload, pos, spacePos - pos);
                pos = spacePos + 1;
                
                // Read name (until null)
                int nullPos = Array.IndexOf(payload, (byte)0, pos);
                if (nullPos == -1)
                    throw new InvalidDataException("Invalid tree format: missing null after name");
                
                string name = Encoding.UTF8.GetString(payload, pos, nullPos - pos);
                pos = nullPos + 1;
                
                // Read 32-byte object ID
                if (pos + 32 > payload.Length)
                    throw new InvalidDataException("Invalid tree format: incomplete object ID");
                
                byte[] objectId = new byte[32];
                Array.Copy(payload, pos, objectId, 0, 32);
                pos += 32;
                
                entries.Add(new TreeEntry(mode, name, objectId));
            }
            
            return new Tree(entries, payload);
        }
    }
}

