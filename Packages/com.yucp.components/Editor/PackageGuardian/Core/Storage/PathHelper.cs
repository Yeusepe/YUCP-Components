using System;
using System.IO;
using System.Linq;

namespace PackageGuardian.Core.Storage
{
    /// <summary>
    /// Safe path manipulation.
    /// </summary>
    public static class PathHelper
    {
        /// <summary>
        /// Validate that a path is safe (no traversal, no absolute paths).
        /// </summary>
        public static bool IsSafePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            
            // Reject absolute paths
            if (Path.IsPathRooted(path))
                return false;
            
            // Reject paths with traversal attempts
            string normalizedPath = path.Replace('\\', '/');
            string[] segments = normalizedPath.Split('/');
            
            foreach (var segment in segments)
            {
                if (segment == ".." || segment == ".")
                    return false;
            }
            
            // Reject paths with invalid characters
            char[] invalidChars = Path.GetInvalidPathChars();
            if (path.IndexOfAny(invalidChars) >= 0)
                return false;
            
            return true;
        }

        /// <summary>
        /// Normalize path separators to forward slashes.
        /// </summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            
            return path.Replace('\\', '/').Trim('/');
        }

        /// <summary>
        /// Ensure directory exists, create if necessary.
        /// </summary>
        public static void EnsureDirectoryExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        /// <summary>
        /// Get relative path from base to target.
        /// </summary>
        public static string GetRelativePath(string basePath, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(basePath))
                throw new ArgumentNullException(nameof(basePath));
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentNullException(nameof(targetPath));
            
            Uri baseUri = new Uri(Path.GetFullPath(basePath) + Path.DirectorySeparatorChar);
            Uri targetUri = new Uri(Path.GetFullPath(targetPath));
            
            Uri relativeUri = baseUri.MakeRelativeUri(targetUri);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());
            
            return NormalizePath(relativePath);
        }

        /// <summary>
        /// Check if path is under a given root directory.
        /// </summary>
        public static bool IsPathUnderRoot(string path, string root)
        {
            string fullPath = Path.GetFullPath(path);
            string fullRoot = Path.GetFullPath(root);
            
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sanitize filename by removing invalid characters.
        /// </summary>
        public static string SanitizeFilename(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                return "unnamed";
            
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string sanitized = new string(filename.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
            
            return sanitized;
        }
    }
}

