using System;
using System.IO;
using PackageGuardian.Core.Hashing;
using PackageGuardian.Core.Objects;
using PackageGuardian.Core.Utilities;

namespace PackageGuardian.Core.Storage
{
    /// <summary>
    /// File-based object storage with compression.
    /// Objects stored at .pg/objects/{first2hex}/{remaining38hex}
    /// </summary>
    public sealed class FileObjectStore : IObjectStore
    {
        private readonly string _objectsPath;
        private readonly IHasher _hasher;

        public FileObjectStore(string repositoryRoot, IHasher hasher)
        {
            if (string.IsNullOrWhiteSpace(repositoryRoot))
                throw new ArgumentNullException(nameof(repositoryRoot));
            
            _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
            _objectsPath = Path.Combine(repositoryRoot, ".pg", "objects");
            
            PathHelper.EnsureDirectoryExists(_objectsPath);
        }

        public string WriteObject(PgObject obj, out byte[] serializedData)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            
            // Serialize object
            serializedData = ObjectSerializer.Serialize(obj);
            
            // Compute object ID
            string oid = ObjectSerializer.ComputeObjectId(obj, _hasher);
            
            // Check if already exists
            string objectPath = GetObjectPath(oid);
            if (File.Exists(objectPath))
                return oid; // Already exists, no need to write
            
            // Compress and write
            byte[] compressed = CompressionHelper.Compress(serializedData);
            
            // Ensure directory exists
            string objectDir = Path.GetDirectoryName(objectPath);
            PathHelper.EnsureDirectoryExists(objectDir);
            
            // Write atomically via temp file
            string tempPath = objectPath + ".tmp";
            try
            {
                File.WriteAllBytes(tempPath, compressed);
                
                // Atomic rename (or copy+delete on Windows if rename fails)
                try
                {
                    File.Move(tempPath, objectPath);
                }
                catch
                {
                    if (!File.Exists(objectPath))
                    {
                        File.Copy(tempPath, objectPath);
                    }
                    File.Delete(tempPath);
                }
            }
            catch
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                throw;
            }
            
            return oid;
        }

        public PgObject ReadObject(string oid)
        {
            if (string.IsNullOrWhiteSpace(oid))
                throw new ArgumentNullException(nameof(oid));
            
            string objectPath = GetObjectPath(oid);
            if (!File.Exists(objectPath))
                throw new FileNotFoundException($"Object not found: {oid}", objectPath);
            
            // Read and decompress
            byte[] compressed = File.ReadAllBytes(objectPath);
            byte[] decompressed = CompressionHelper.Decompress(compressed);
            
            // Deserialize
            PgObject obj = ObjectSerializer.Deserialize(decompressed);
            
            // Validate integrity
            if (!ObjectSerializer.ValidateObjectId(obj, oid, _hasher))
                throw new InvalidDataException($"Object integrity check failed for {oid}");
            
            return obj;
        }

        public bool Contains(string oid)
        {
            if (string.IsNullOrWhiteSpace(oid))
                return false;
            
            string objectPath = GetObjectPath(oid);
            return File.Exists(objectPath);
        }

        private string GetObjectPath(string oid)
        {
            if (oid.Length < 2)
                throw new ArgumentException("Object ID too short", nameof(oid));
            
            string dir = oid.Substring(0, 2);
            string file = oid.Substring(2);
            
            return Path.Combine(_objectsPath, dir, file);
        }
    }
}

