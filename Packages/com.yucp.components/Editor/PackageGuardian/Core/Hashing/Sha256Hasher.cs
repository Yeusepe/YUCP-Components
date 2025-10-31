using System;
using System.Security.Cryptography;
using System.Text;

namespace PackageGuardian.Core.Hashing
{
    /// <summary>
    /// SHA-256 implementation of IHasher.
    /// </summary>
    public sealed class Sha256Hasher : IHasher
    {
        private readonly SHA256 _sha256;

        public Sha256Hasher()
        {
            _sha256 = SHA256.Create();
        }

        public int DigestSize => 32; // SHA-256 produces 32 bytes

        public byte[] Compute(ReadOnlySpan<byte> data)
        {
            return _sha256.ComputeHash(data.ToArray());
        }

        public string ToHex(ReadOnlySpan<byte> hash)
        {
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
    }
}

