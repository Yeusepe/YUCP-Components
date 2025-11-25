using System;
using PackageGuardian.Core.Objects;

namespace PackageGuardian.Core.Storage
{
    /// <summary>
    /// Represents a staged write that can be flushed to persistent storage.
    /// </summary>
    public readonly struct StagedObjectWrite
    {
        public StagedObjectWrite(string objectId, string targetPath, byte[] payload, bool existsAlready, PgObject source)
        {
            ObjectId = objectId ?? throw new ArgumentNullException(nameof(objectId));
            TargetPath = targetPath ?? throw new ArgumentNullException(nameof(targetPath));
            Payload = payload ?? Array.Empty<byte>();
            ExistsAlready = existsAlready;
            SourceObject = source;
        }

        public string ObjectId { get; }
        public string TargetPath { get; }
        public byte[] Payload { get; }
        public bool ExistsAlready { get; }
        public PgObject SourceObject { get; }
    }
}



