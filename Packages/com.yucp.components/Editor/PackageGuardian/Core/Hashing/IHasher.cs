using System;

namespace PackageGuardian.Core.Hashing
{
    /// <summary>
    /// Interface for content hashing algorithms used in Package Guardian.
    /// </summary>
    public interface IHasher
    {
        /// <summary>
        /// Compute hash of the given data.
        /// </summary>
        /// <param name="data">Data to hash</param>
        /// <returns>Raw hash bytes</returns>
        byte[] Compute(ReadOnlySpan<byte> data);
        
        /// <summary>
        /// Convert hash bytes to lowercase hexadecimal string.
        /// </summary>
        /// <param name="hash">Hash bytes</param>
        /// <returns>Hex string representation</returns>
        string ToHex(ReadOnlySpan<byte> hash);
        
        /// <summary>
        /// Size of the hash digest in bytes.
        /// </summary>
        int DigestSize { get; }
    }
}

