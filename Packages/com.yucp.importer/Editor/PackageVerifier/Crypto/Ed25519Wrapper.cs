using System;
using Chaos.NaCl;

namespace YUCP.Importer.Editor.PackageVerifier.Crypto
{
    /// <summary>
    /// Ed25519 verification through the package-pinned Chaos.NaCl assembly.
    /// </summary>
    public static class Ed25519Wrapper
    {
        private const int PublicKeySize = 32;
        private const int SignatureSize = 64;
        /// <summary>
        /// Verify signature with public key
        /// </summary>
        public static bool Verify(byte[] data, byte[] signature, byte[] publicKey)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (signature == null || signature.Length != SignatureSize)
                throw new ArgumentException("Invalid signature (must be 64 bytes)", nameof(signature));
            if (publicKey == null || publicKey.Length != PublicKeySize)
                throw new ArgumentException("Invalid public key (must be 32 bytes)", nameof(publicKey));

            return Ed25519.Verify(signature, data, publicKey);
        }
    }
}



