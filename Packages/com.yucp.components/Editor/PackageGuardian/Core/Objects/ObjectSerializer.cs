using System;
using System.IO;
using System.Text;
using PackageGuardian.Core.Hashing;

namespace PackageGuardian.Core.Objects
{
    /// <summary>
    /// Serializes and deserializes Package Guardian objects.
    /// Format: type + " " + size + "\0" + payload
    /// </summary>
    public static class ObjectSerializer
    {
        /// <summary>
        /// Serialize object to bytes (header + payload).
        /// </summary>
        public static byte[] Serialize(PgObject obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            
            string header = $"{obj.Type} {obj.Payload.Length}\0";
            byte[] headerBytes = Encoding.UTF8.GetBytes(header);
            
            byte[] result = new byte[headerBytes.Length + obj.Payload.Length];
            Array.Copy(headerBytes, 0, result, 0, headerBytes.Length);
            Array.Copy(obj.Payload, 0, result, headerBytes.Length, obj.Payload.Length);
            
            return result;
        }

        /// <summary>
        /// Compute object ID from serialized form.
        /// </summary>
        public static string ComputeObjectId(PgObject obj, IHasher hasher)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            if (hasher == null)
                throw new ArgumentNullException(nameof(hasher));
            
            byte[] serialized = Serialize(obj);
            byte[] hash = hasher.Compute(serialized);
            return hasher.ToHex(hash);
        }

        /// <summary>
        /// Deserialize object from bytes (header + payload).
        /// </summary>
        public static PgObject Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            
            // Find null terminator in header
            int nullPos = Array.IndexOf(data, (byte)0);
            if (nullPos == -1)
                throw new InvalidDataException("Invalid object format: missing null terminator in header");
            
            // Parse header: "type size\0"
            string header = Encoding.UTF8.GetString(data, 0, nullPos);
            var parts = header.Split(' ');
            if (parts.Length != 2)
                throw new InvalidDataException($"Invalid object header format: {header}");
            
            string type = parts[0];
            if (!long.TryParse(parts[1], out long size))
                throw new InvalidDataException($"Invalid object size: {parts[1]}");
            
            // Extract payload
            int payloadStart = nullPos + 1;
            long payloadLength = data.Length - payloadStart;
            
            if (payloadLength != size)
                throw new InvalidDataException($"Payload size mismatch: expected {size}, got {payloadLength}");
            
            byte[] payload = new byte[size];
            Array.Copy(data, payloadStart, payload, 0, size);
            
            // Deserialize specific object type
            return type switch
            {
                "blob" => new Blob(payload),
                "tree" => Tree.Deserialize(payload),
                "commit" => Commit.Deserialize(payload),
                _ => throw new InvalidDataException($"Unknown object type: {type}")
            };
        }

        /// <summary>
        /// Validate object integrity: re-serialize and compare hash.
        /// </summary>
        public static bool ValidateObjectId(PgObject obj, string expectedOid, IHasher hasher)
        {
            string actualOid = ComputeObjectId(obj, hasher);
            return string.Equals(actualOid, expectedOid, StringComparison.OrdinalIgnoreCase);
        }
    }
}

