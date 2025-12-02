using System;
using System.IO;
using PackageGuardian.Core.Hashing;
using PackageGuardian.Core.Objects;
using PackageGuardian.Core.Utilities;

namespace PackageGuardian.Core.Storage
{
    /// <summary>
    /// File object storage with compression.
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
            var staged = StageObject(obj);
            serializedData = staged.Payload;
            return CommitStagedObject(staged);
        }

        public StagedObjectWrite StageObject(PgObject obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            
            var serializedData = ObjectSerializer.Serialize(obj);
            string oid = ObjectSerializer.ComputeObjectId(obj, _hasher);
            string objectPath = GetObjectPath(oid);
            
            if (File.Exists(objectPath))
                return new StagedObjectWrite(oid, objectPath, Array.Empty<byte>(), true, obj);
            
            byte[] compressed = CompressionHelper.Compress(serializedData);
            return new StagedObjectWrite(oid, objectPath, compressed, false, obj);
        }

        public string CommitStagedObject(StagedObjectWrite staged)
        {
            if (staged.ExistsAlready)
                return staged.ObjectId;
            
            string objectDir = Path.GetDirectoryName(staged.TargetPath);
            PathHelper.EnsureDirectoryExists(objectDir);
            
            string tempPath = staged.TargetPath + ".tmp";
            try
            {
                File.WriteAllBytes(tempPath, staged.Payload);
                
                try
                {
                    File.Move(tempPath, staged.TargetPath);
                }
                catch
                {
                    if (!File.Exists(staged.TargetPath))
                    {
                        File.Copy(tempPath, staged.TargetPath);
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
            
            return staged.ObjectId;
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

