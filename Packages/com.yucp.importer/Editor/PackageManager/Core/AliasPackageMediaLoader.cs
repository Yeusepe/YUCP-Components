using System;
using System.Collections.Generic;
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
            ApplyFromContent(
                metadata,
                alias,
                descriptor => ReadInstalledPackageContent(
                    packageRoot,
                    descriptor,
                    descriptor?.kind));
        }

        /// <summary>
        /// Applies digest-bound alias presentation media from the bytes exposed by
        /// Unity's import transaction. This is the import-preview counterpart of
        /// <see cref="Apply"/>, so legacy YUCP_PackageInfo handling remains separate.
        /// </summary>
        internal static void ApplyFromImportContent(
            PackageMetadata metadata,
            AliasPackageContract alias,
            Func<AliasPackageMediaDescriptor, byte[]> readContent)
        {
            if (readContent == null)
            {
                throw new ArgumentNullException(nameof(readContent));
            }

            ApplyFromContent(metadata, alias, readContent);
        }

        private static void ApplyFromContent(
            PackageMetadata metadata,
            AliasPackageContract alias,
            Func<AliasPackageMediaDescriptor, byte[]> readContent)
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
            var gallery = new List<Texture2D>();
            var productLinks = new List<ProductLink>();
            try
            {
                icon = TryLoad(
                    alias.media.icon,
                    "icon",
                    readContent);
                banner = TryLoad(
                    alias.media.banner,
                    "banner",
                    readContent);
                foreach (AliasPackageMediaDescriptor descriptor in
                    alias.media.gallery ??
                    new List<AliasPackageMediaDescriptor>())
                {
                    Texture2D image = TryLoad(
                        descriptor,
                        "gallery",
                        readContent);
                    if (image != null)
                    {
                        gallery.Add(image);
                    }
                }
                foreach (AliasPackageMediaDescriptor descriptor in
                    alias.media.productLinks ??
                    new List<AliasPackageMediaDescriptor>())
                {
                    productLinks.Add(new ProductLink(
                        descriptor.url,
                        descriptor.label)
                    {
                        customIcon = TryLoad(
                            descriptor,
                            "product-link",
                            readContent),
                    });
                }
                PackageMetadataMediaOwnership.Replace(
                    metadata,
                    icon,
                    banner,
                    gallery,
                    productLinks);
            }
            catch
            {
                PackageMetadataMediaOwnership.Release(icon);
                PackageMetadataMediaOwnership.Release(banner);
                foreach (Texture2D texture in gallery)
                {
                    PackageMetadataMediaOwnership.Release(texture);
                }
                foreach (ProductLink link in productLinks)
                {
                    PackageMetadataMediaOwnership.Release(link?.customIcon);
                }
                throw;
            }
        }

        /// <summary>
        /// One unreadable image must not erase the whole storefront presentation.
        /// Each descriptor is still digest-bound; a descriptor that fails is
        /// dropped by name so the reason survives in the Unity log.
        /// </summary>
        private static Texture2D TryLoad(
            AliasPackageMediaDescriptor descriptor,
            string expectedKind,
            Func<AliasPackageMediaDescriptor, byte[]> readContent)
        {
            try
            {
                return Load(descriptor, expectedKind, readContent);
            }
            catch (Exception exception) when (
                exception is InvalidDataException ||
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                Debug.LogWarning(
                    "[YUCP PackageManager] Skipped alias " + expectedKind +
                    " media '" + (descriptor?.localPath ?? "<none>") + "': " +
                    exception.Message);
                return null;
            }
        }

        private static Texture2D Load(
            AliasPackageMediaDescriptor descriptor,
            string expectedKind,
            Func<AliasPackageMediaDescriptor, byte[]> readContent)
        {
            if (descriptor == null ||
                string.IsNullOrWhiteSpace(descriptor.localPath))
            {
                return null;
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

            NormalizeRelativePath(descriptor.localPath, expectedKind);
            byte[] bytes = readContent(descriptor);
            if (bytes == null || bytes.LongLength != descriptor.byteSize)
            {
                throw new InvalidDataException(
                    $"Alias package {expectedKind} media has an invalid size.");
            }
            string observedDigest;
            using (SHA256 sha256 = SHA256.Create())
            {
                observedDigest = string.Concat(
                    sha256.ComputeHash(bytes)
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

        private static byte[] ReadInstalledPackageContent(
            string packageRoot,
            AliasPackageMediaDescriptor descriptor,
            string expectedKind)
        {
            if (string.IsNullOrWhiteSpace(packageRoot) ||
                !Path.IsPathRooted(packageRoot))
            {
                throw new InvalidDataException(
                    "Alias package media has no installed package root.");
            }

            string root = Path.GetFullPath(packageRoot);
            string relativePath = NormalizeRelativePath(
                descriptor?.localPath,
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
            return File.ReadAllBytes(path);
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
