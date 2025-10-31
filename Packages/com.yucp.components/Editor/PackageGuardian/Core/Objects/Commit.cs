using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PackageGuardian.Core.Objects
{
    /// <summary>
    /// Represents a snapshot commit with metadata.
    /// Format: UTF-8 headers (tree, parent*, author, committer, timestamp), blank line, message.
    /// </summary>
    public sealed record Commit : PgObject
    {
        /// <summary>
        /// Object ID of the root tree (32 bytes).
        /// </summary>
        public byte[] TreeId { get; }
        
        /// <summary>
        /// Parent commit IDs (32 bytes each).
        /// </summary>
        public IReadOnlyList<byte[]> Parents { get; }
        
        /// <summary>
        /// Author name and email.
        /// </summary>
        public string Author { get; }
        
        /// <summary>
        /// Committer name and email.
        /// </summary>
        public string Committer { get; }
        
        /// <summary>
        /// Unix timestamp in seconds.
        /// </summary>
        public long Timestamp { get; }
        
        /// <summary>
        /// Commit message.
        /// </summary>
        public string Message { get; }

        public Commit(byte[] treeId, IEnumerable<byte[]> parents, string author, string committer, long timestamp, string message)
            : base("commit", SerializeCommit(treeId, parents, author, committer, timestamp, message))
        {
            TreeId = treeId ?? throw new ArgumentNullException(nameof(treeId));
            Parents = parents?.ToList() ?? new List<byte[]>();
            Author = author ?? throw new ArgumentNullException(nameof(author));
            Committer = committer ?? throw new ArgumentNullException(nameof(committer));
            Timestamp = timestamp;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            
            if (treeId.Length != 32)
                throw new ArgumentException("TreeId must be exactly 32 bytes", nameof(treeId));
            
            foreach (var parent in Parents)
            {
                if (parent.Length != 32)
                    throw new ArgumentException("Each parent must be exactly 32 bytes", nameof(parents));
            }
        }

        private Commit(byte[] treeId, IReadOnlyList<byte[]> parents, string author, string committer, long timestamp, string message, byte[] payload)
            : base("commit", payload)
        {
            TreeId = treeId;
            Parents = parents;
            Author = author;
            Committer = committer;
            Timestamp = timestamp;
            Message = message;
        }

        private static byte[] SerializeCommit(byte[] treeId, IEnumerable<byte[]> parents, string author, string committer, long timestamp, string message)
        {
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms, Encoding.UTF8);
            
            // Write tree header
            writer.Write("tree ");
            writer.Write(BytesToHex(treeId));
            writer.Write('\n');
            
            // Write parent headers
            foreach (var parent in parents ?? Enumerable.Empty<byte[]>())
            {
                writer.Write("parent ");
                writer.Write(BytesToHex(parent));
                writer.Write('\n');
            }
            
            // Write metadata
            writer.Write("author ");
            writer.Write(author);
            writer.Write('\n');
            
            writer.Write("committer ");
            writer.Write(committer);
            writer.Write('\n');
            
            writer.Write("timestamp ");
            writer.Write(timestamp);
            writer.Write('\n');
            
            // Blank line separates headers from message
            writer.Write('\n');
            
            // Write message
            writer.Write(message);
            
            writer.Flush();
            return ms.ToArray();
        }

        /// <summary>
        /// Deserialize commit from payload bytes.
        /// </summary>
        public static Commit Deserialize(byte[] payload)
        {
            using var ms = new MemoryStream(payload);
            using var reader = new StreamReader(ms, Encoding.UTF8);
            
            byte[] treeId = null;
            var parents = new List<byte[]>();
            string author = null;
            string committer = null;
            long timestamp = 0;
            
            // Parse headers
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrEmpty(line))
                    break; // End of headers
                
                var parts = line.Split(new[] { ' ' }, 2);
                if (parts.Length != 2)
                    throw new InvalidDataException($"Invalid commit header: {line}");
                
                string key = parts[0];
                string value = parts[1];
                
                switch (key)
                {
                    case "tree":
                        treeId = HexToBytes(value);
                        break;
                    case "parent":
                        parents.Add(HexToBytes(value));
                        break;
                    case "author":
                        author = value;
                        break;
                    case "committer":
                        committer = value;
                        break;
                    case "timestamp":
                        timestamp = long.Parse(value);
                        break;
                    default:
                        throw new InvalidDataException($"Unknown commit header: {key}");
                }
            }
            
            // Read message (rest of the stream)
            string message = reader.ReadToEnd();
            
            if (treeId == null)
                throw new InvalidDataException("Commit missing tree header");
            if (author == null)
                throw new InvalidDataException("Commit missing author header");
            if (committer == null)
                throw new InvalidDataException("Commit missing committer header");
            
            return new Commit(treeId, parents, author, committer, timestamp, message, payload);
        }

        private static string BytesToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        private static byte[] HexToBytes(string hex)
        {
            if (hex.Length % 2 != 0)
                throw new ArgumentException("Hex string must have even length", nameof(hex));
            
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
    }
}

