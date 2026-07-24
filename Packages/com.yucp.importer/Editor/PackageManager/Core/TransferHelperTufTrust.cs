using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor.PackageManager;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class TransferHelperTufTrust
    {
        internal const string PinnedRootSha256 =
            "f4e31f5a47d4f6558fdd51b97e379c34bd42325bcca32d07a9626596bba724af";

        private static readonly string[] RootPathParts =
        {
            "Editor",
            "PackageManager",
            "Trust",
            "1.root.json",
        };

        internal static byte[] LoadPinnedRoot()
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(TransferHelperTufTrust).Assembly);
            if (package == null || string.IsNullOrWhiteSpace(package.resolvedPath))
            {
                throw new InvalidOperationException("Could not resolve the importer package path.");
            }

            string packagePath = Path.GetFullPath(package.resolvedPath);
            string rootPath = RootPathParts.Aggregate(packagePath, Path.Combine);
            rootPath = Path.GetFullPath(rootPath);
            string boundary = packagePath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!rootPath.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The pinned TUF root path escaped the importer package.");
            }

            return VerifyPinnedRoot(File.ReadAllBytes(rootPath));
        }

        internal static byte[] VerifyPinnedRoot(byte[] rootBytes)
        {
            if (rootBytes == null || rootBytes.Length == 0)
            {
                throw new CryptographicException("The pinned TUF root is empty.");
            }

            string actualDigest;
            using (SHA256 sha256 = SHA256.Create())
            {
                actualDigest = string.Concat(
                    sha256.ComputeHash(rootBytes).Select(value => value.ToString("x2")));
            }
            if (!string.Equals(actualDigest, PinnedRootSha256, StringComparison.Ordinal))
            {
                throw new CryptographicException("The pinned TUF root digest is invalid.");
            }

            return (byte[])rootBytes.Clone();
        }
    }
}
