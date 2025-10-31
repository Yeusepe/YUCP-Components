using System;
using System.IO;

namespace PackageGuardian.Core.Diff
{
    /// <summary>
    /// Calculates similarity between two files, inspired by Git's similarity scoring.
    /// Uses a combination of size, hash, and content comparison.
    /// </summary>
    public static class SimilarityCalculator
    {
        /// <summary>
        /// Calculate similarity score between two byte arrays.
        /// Returns a value between 0.0 (completely different) and 1.0 (identical).
        /// </summary>
        public static float CalculateSimilarity(byte[] oldData, byte[] newData)
        {
            if (oldData == null || newData == null)
                return 0f;

            // Identical content
            if (oldData.Length == newData.Length && AreEqual(oldData, newData))
                return 1.0f;

            // Very different sizes = probably not similar
            int minSize = Math.Min(oldData.Length, newData.Length);
            int maxSize = Math.Max(oldData.Length, newData.Length);
            
            if (maxSize == 0)
                return 1.0f; // Both empty
            
            if (minSize == 0)
                return 0f; // One is empty
            
            // If size difference is too large, probably not similar
            float sizeRatio = (float)minSize / maxSize;
            if (sizeRatio < 0.5f)
                return 0f;

            // Use a simple but effective algorithm:
            // Compare blocks of data and count matching blocks
            return CalculateBlockSimilarity(oldData, newData);
        }

        /// <summary>
        /// Fast byte array equality check.
        /// </summary>
        private static bool AreEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Calculate similarity based on matching blocks.
        /// This is a simplified version of Git's approach using a rolling hash.
        /// </summary>
        private static float CalculateBlockSimilarity(byte[] oldData, byte[] newData)
        {
            const int blockSize = 64; // 64-byte blocks

            if (oldData.Length < blockSize && newData.Length < blockSize)
            {
                // For small files, just compare bytes directly
                return CalculateDirectSimilarity(oldData, newData);
            }

            // Create hash set of blocks from old file
            var oldBlocks = new System.Collections.Generic.HashSet<ulong>();
            for (int i = 0; i <= oldData.Length - blockSize; i += blockSize)
            {
                ulong hash = ComputeBlockHash(oldData, i, blockSize);
                oldBlocks.Add(hash);
            }

            // Count matching blocks in new file
            int matchingBlocks = 0;
            int totalBlocks = 0;
            
            for (int i = 0; i <= newData.Length - blockSize; i += blockSize)
            {
                ulong hash = ComputeBlockHash(newData, i, blockSize);
                if (oldBlocks.Contains(hash))
                    matchingBlocks++;
                totalBlocks++;
            }

            if (totalBlocks == 0)
                return 0f;

            // Calculate percentage of matching blocks
            float blockMatchRatio = (float)matchingBlocks / totalBlocks;
            
            // Weight by size similarity
            int minSize = Math.Min(oldData.Length, newData.Length);
            int maxSize = Math.Max(oldData.Length, newData.Length);
            float sizeRatio = (float)minSize / maxSize;
            
            // Combined score
            return blockMatchRatio * 0.8f + sizeRatio * 0.2f;
        }

        /// <summary>
        /// Direct byte-by-byte comparison for small files.
        /// </summary>
        private static float CalculateDirectSimilarity(byte[] oldData, byte[] newData)
        {
            int minLength = Math.Min(oldData.Length, newData.Length);
            int matches = 0;

            for (int i = 0; i < minLength; i++)
            {
                if (oldData[i] == newData[i])
                    matches++;
            }

            int maxLength = Math.Max(oldData.Length, newData.Length);
            return (float)matches / maxLength;
        }

        /// <summary>
        /// Compute a simple hash for a block of bytes using FNV-1a algorithm.
        /// </summary>
        private static ulong ComputeBlockHash(byte[] data, int offset, int length)
        {
            const ulong FNV_OFFSET_BASIS = 14695981039346656037;
            const ulong FNV_PRIME = 1099511628211;

            ulong hash = FNV_OFFSET_BASIS;
            int end = Math.Min(offset + length, data.Length);

            for (int i = offset; i < end; i++)
            {
                hash ^= data[i];
                hash *= FNV_PRIME;
            }

            return hash;
        }

        /// <summary>
        /// Check if two file paths have the same basename (filename without directory).
        /// This is used as a fast pre-filter for rename detection.
        /// </summary>
        public static bool HasSameBasename(string path1, string path2)
        {
            string basename1 = Path.GetFileName(path1);
            string basename2 = Path.GetFileName(path2);
            
            return string.Equals(basename1, basename2, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Calculate name similarity score based on path similarity.
        /// Returns a value between 0.0 (completely different) and 1.0 (identical).
        /// </summary>
        public static float CalculateNameSimilarity(string path1, string path2)
        {
            if (string.Equals(path1, path2, StringComparison.OrdinalIgnoreCase))
                return 1.0f;

            string name1 = Path.GetFileName(path1);
            string name2 = Path.GetFileName(path2);

            if (string.Equals(name1, name2, StringComparison.OrdinalIgnoreCase))
                return 0.9f; // Same filename, different directory

            // Simple Levenshtein-like comparison
            int distance = LevenshteinDistance(name1, name2);
            int maxLength = Math.Max(name1.Length, name2.Length);
            
            if (maxLength == 0)
                return 1.0f;

            return 1.0f - ((float)distance / maxLength);
        }

        /// <summary>
        /// Calculate Levenshtein distance between two strings.
        /// </summary>
        private static int LevenshteinDistance(string s1, string s2)
        {
            int n = s1.Length;
            int m = s2.Length;
            int[,] d = new int[n + 1, m + 1];

            if (n == 0) return m;
            if (m == 0) return n;

            for (int i = 0; i <= n; i++)
                d[i, 0] = i;
            for (int j = 0; j <= m; j++)
                d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (char.ToLowerInvariant(s1[i - 1]) == char.ToLowerInvariant(s2[j - 1])) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }
    }
}

