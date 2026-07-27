using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class AliasPackageMediaLoader
    {
        private const long MaximumMediaBytes = 16 * 1024 * 1024;
        private const int MaximumImageDimension = 4096;

        internal static void Apply(
            PackageMetadata metadata,
            AliasPackageContract alias,
            string packageRoot)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }
            if (alias?.media == null)
            {
                return;
            }

            Texture2D icon = null;
            Texture2D banner = null;
            try
            {
                icon = Load(
                    packageRoot,
                    alias.media.icon,
                    "icon");
                banner = Load(
                    packageRoot,
                    alias.media.banner,
                    "banner");
                PackageMetadataMediaOwnership.Replace(
                    metadata,
                    icon,
                    banner);
            }
            catch
            {
                PackageMetadataMediaOwnership.Release(icon);
                PackageMetadataMediaOwnership.Release(banner);
                throw;
            }
        }

        private static Texture2D Load(
            string packageRoot,
            AliasPackageMediaDescriptor descriptor,
            string expectedKind)
        {
            if (descriptor == null ||
                string.IsNullOrWhiteSpace(descriptor.localPath))
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(packageRoot) ||
                !Path.IsPathRooted(packageRoot))
            {
                throw new InvalidDataException(
                    "Alias package media has no installed package root.");
            }
            if (!string.Equals(
                    descriptor.kind,
                    expectedKind,
                    StringComparison.Ordinal) ||
                descriptor.byteSize < 1 ||
                descriptor.byteSize > MaximumMediaBytes ||
                !IsSha256(descriptor.sha256) ||
                !IsSupportedContentType(descriptor.contentType))
            {
                throw new InvalidDataException(
                    $"Alias package {expectedKind} media is invalid.");
            }

            string root = Path.GetFullPath(packageRoot);
            string relativePath = NormalizeRelativePath(
                descriptor.localPath,
                expectedKind);
            string path = Path.GetFullPath(
                Path.Combine(
                    root,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));
            string boundary = root.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!path.StartsWith(
                    boundary,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Alias package {expectedKind} media escapes its package.");
            }
            RejectReparsePoints(root, path, expectedKind);

            var info = new FileInfo(path);
            if (!info.Exists || info.Length != descriptor.byteSize)
            {
                throw new InvalidDataException(
                    $"Alias package {expectedKind} media has an invalid size.");
            }
            string observedDigest;
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha256 = SHA256.Create())
            {
                observedDigest = string.Concat(
                    sha256.ComputeHash(stream)
                        .Select(value => value.ToString("x2")));
            }
            if (!string.Equals(
                    observedDigest,
                    descriptor.sha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Alias package {expectedKind} media failed verification.");
            }

            byte[] bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "YUCP " + expectedKind,
            };
            if (!ImageConversion.LoadImage(texture, bytes, true) ||
                texture.width < 1 ||
                texture.height < 1 ||
                texture.width > MaximumImageDimension ||
                texture.height > MaximumImageDimension)
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw new InvalidDataException(
                    $"Alias package {expectedKind} media cannot be decoded.");
            }
            return texture;
        }

        private static string NormalizeRelativePath(
            string value,
            string kind)
        {
            string normalized = (value ?? string.Empty)
                .Replace('\\', '/')
                .Trim();
            if (Path.IsPathRooted(normalized) ||
                normalized.StartsWith("/", StringComparison.Ordinal) ||
                normalized.Split('/').Any(segment =>
                    string.IsNullOrWhiteSpace(segment) ||
                    segment == "." ||
                    segment == ".."))
            {
                throw new InvalidDataException(
                    $"Alias package {kind} media path is invalid.");
            }
            return normalized;
        }

        private static void RejectReparsePoints(
            string root,
            string path,
            string kind)
        {
            string relative = path.Substring(root.Length).TrimStart(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string current = root;
            foreach (string segment in relative.Split(
                new[]
                {
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar,
                },
                StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (!Directory.Exists(current) && !File.Exists(current))
                {
                    continue;
                }
                if ((File.GetAttributes(current) &
                        FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        $"Alias package {kind} media path is not allowed.");
                }
            }
        }

        private static bool IsSupportedContentType(string value)
        {
            return string.Equals(
                    value,
                    "image/png",
                    StringComparison.Ordinal) ||
                string.Equals(
                    value,
                    "image/jpeg",
                    StringComparison.Ordinal);
        }

        private static bool IsSha256(string value)
        {
            return value != null &&
                value.Length == 64 &&
                value.All(character =>
                    character >= '0' && character <= '9' ||
                    character >= 'a' && character <= 'f');
        }
    }
}
