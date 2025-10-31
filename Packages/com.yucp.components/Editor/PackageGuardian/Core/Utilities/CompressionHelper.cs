using System;
using System.IO;
using System.IO.Compression;

namespace PackageGuardian.Core.Utilities
{
    /// <summary>
    /// Helper methods for compression and decompression using Deflate.
    /// </summary>
    public static class CompressionHelper
    {
        /// <summary>
        /// Compress data using Deflate algorithm.
        /// </summary>
        public static byte[] Compress(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            
            using var outputStream = new MemoryStream();
            using (var deflateStream = new DeflateStream(outputStream, CompressionLevel.Optimal))
            {
                deflateStream.Write(data, 0, data.Length);
            }
            return outputStream.ToArray();
        }

        /// <summary>
        /// Decompress data using Deflate algorithm.
        /// </summary>
        public static byte[] Decompress(byte[] compressedData)
        {
            if (compressedData == null)
                throw new ArgumentNullException(nameof(compressedData));
            
            using var inputStream = new MemoryStream(compressedData);
            using var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();
            
            deflateStream.CopyTo(outputStream);
            return outputStream.ToArray();
        }

        /// <summary>
        /// Compress stream to output stream.
        /// </summary>
        public static void CompressStream(Stream input, Stream output)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            
            using var deflateStream = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true);
            input.CopyTo(deflateStream);
        }

        /// <summary>
        /// Decompress stream to output stream.
        /// </summary>
        public static void DecompressStream(Stream input, Stream output)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            
            using var deflateStream = new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true);
            deflateStream.CopyTo(output);
        }
    }
}

