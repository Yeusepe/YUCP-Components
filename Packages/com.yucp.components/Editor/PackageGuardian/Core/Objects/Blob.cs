using System;

namespace PackageGuardian.Core.Objects
{
    /// <summary>
    /// Represents a file content blob.
    /// </summary>
    public sealed record Blob : PgObject
    {
        /// <summary>
        /// Raw file data.
        /// </summary>
        public byte[] Data { get; }

        public Blob(byte[] data) : base("blob", data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }
    }
}

