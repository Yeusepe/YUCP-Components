using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class RuntimeExecutionSecurityUtility
    {
        internal static string ComputeSha256Hex(byte[] data)
        {
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(data ?? Array.Empty<byte>());
            var builder = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
            {
                builder.Append(b.ToString("x2"));
            }

            return builder.ToString();
        }

        internal static bool TryResolveContainedDirectoryPath(
            string rootPath,
            string relativePath,
            string description,
            out string resolvedPath,
            out string error)
        {
            return TryResolveContainedPath(
                rootPath,
                relativePath,
                description,
                allowSubdirectories: true,
                out resolvedPath,
                out error);
        }

        internal static bool TryResolveContainedFilePath(
            string rootPath,
            string relativePath,
            string expectedExtension,
            string description,
            bool allowSubdirectories,
            out string resolvedPath,
            out string error)
        {
            resolvedPath = null;
            if (!TryResolveContainedPath(
                    rootPath,
                    relativePath,
                    description,
                    allowSubdirectories,
                    out string candidatePath,
                    out error))
            {
                return false;
            }

            if (!string.Equals(Path.GetExtension(candidatePath), expectedExtension, StringComparison.OrdinalIgnoreCase))
            {
                error = $"{description} must use the {expectedExtension} file extension.";
                return false;
            }

            resolvedPath = candidatePath;
            return true;
        }

        internal static bool TryResolveRuntimePackageDirectory(
            string installRoot,
            string packageDirectory,
            string description,
            out string resolvedPath,
            out string error)
        {
            resolvedPath = null;
            error = null;
            if (string.IsNullOrWhiteSpace(packageDirectory))
            {
                error = $"{description} is missing.";
                return false;
            }

            if (!Path.IsPathRooted(packageDirectory))
            {
                error = $"{description} must be an absolute path under the configured runtime install root.";
                return false;
            }

            string normalizedPackagePath;
            try
            {
                normalizedPackagePath = Path.GetFullPath(packageDirectory);
            }
            catch (Exception ex)
            {
                error = $"{description} could not be normalized: {ex.Message}";
                return false;
            }

            if (!IsPathUnderRoot(installRoot, normalizedPackagePath))
            {
                error = $"{description} escaped the configured runtime install root.";
                return false;
            }

            resolvedPath = normalizedPackagePath;
            return true;
        }

        internal static string ResolveWindowsSystemExecutablePath(string executableFileName)
        {
            if (string.IsNullOrWhiteSpace(executableFileName))
                return null;

            string executablePath = Path.Combine(Environment.SystemDirectory, executableFileName);
            return File.Exists(executablePath)
                ? executablePath
                : null;
        }

        internal static bool TryOpenLockedRead(
            string filePath,
            out FileStream stream,
            out byte[] bytes,
            out string error)
        {
            stream = null;
            bytes = null;
            error = null;

            try
            {
                stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                bytes = copy.ToArray();
                stream.Position = 0;
                return true;
            }
            catch (IOException ex)
            {
                stream?.Dispose();
                stream = null;
                error = $"The file '{filePath}' could not be read securely: {ex.Message}";
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                stream?.Dispose();
                stream = null;
                error = $"The file '{filePath}' could not be read securely: {ex.Message}";
                return false;
            }
        }

        internal static FileStream CreateLockedArtifactFile(string filePath, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("Locked artifact path must have a parent directory."));

            var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            stream.Write(bytes ?? Array.Empty<byte>(), 0, bytes?.Length ?? 0);
            stream.Flush(true);
            stream.Position = 0;
            return stream;
        }

        internal static FileStream CreateLockedTextFile(string filePath, string contents, Encoding encoding)
        {
            return CreateLockedArtifactFile(filePath, (encoding ?? Encoding.UTF8).GetBytes(contents ?? string.Empty));
        }

        internal static bool IsPathUnderRoot(string rootPath, string candidatePath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(candidatePath))
                return false;

            try
            {
                string normalizedRoot = NormalizeRoot(rootPath);
                string normalizedCandidate = Path.GetFullPath(candidatePath);
                return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryResolveContainedPath(
            string rootPath,
            string relativePath,
            string description,
            bool allowSubdirectories,
            out string resolvedPath,
            out string error)
        {
            resolvedPath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                error = $"{description} root is missing.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                error = $"{description} path is missing.";
                return false;
            }

            string normalizedRelativePath = relativePath
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Trim();

            if (Path.IsPathRooted(normalizedRelativePath) ||
                normalizedRelativePath.StartsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                error = $"{description} must stay relative to the validated runtime root.";
                return false;
            }

            if (!allowSubdirectories &&
                normalizedRelativePath.IndexOf(Path.DirectorySeparatorChar) >= 0)
            {
                error = $"{description} must not contain nested path segments.";
                return false;
            }

            try
            {
                resolvedPath = Path.GetFullPath(Path.Combine(rootPath, normalizedRelativePath));
            }
            catch (Exception ex)
            {
                error = $"{description} could not be normalized: {ex.Message}";
                return false;
            }

            if (!IsPathUnderRoot(rootPath, resolvedPath))
            {
                error = $"{description} escaped the validated runtime root.";
                return false;
            }

            return true;
        }

        private static string NormalizeRoot(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        }
    }
}
