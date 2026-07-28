using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class PackageMetadataMediaOwnership
    {
        internal static void Replace(
            PackageMetadata metadata,
            Texture2D icon,
            Texture2D banner,
            List<Texture2D> gallery = null,
            List<ProductLink> productLinks = null)
        {
            if (metadata == null)
            {
                return;
            }
            HashSet<Texture2D> previous = Collect(metadata);
            metadata.icon = icon;
            metadata.banner = banner;
            metadata.galleryImages = gallery ?? new List<Texture2D>();
            metadata.productLinks = productLinks ?? new List<ProductLink>();
            Release(previous, Collect(metadata));
        }

        internal static void Release(
            PackageMetadata metadata,
            PackageMetadata retained = null)
        {
            if (metadata == null ||
                ReferenceEquals(metadata, retained))
            {
                return;
            }
            Release(Collect(metadata), Collect(retained));
            metadata.icon = null;
            metadata.banner = null;
            if (metadata.galleryImages != null)
            {
                metadata.galleryImages.Clear();
            }
            if (metadata.productLinks != null)
            {
                foreach (ProductLink link in metadata.productLinks)
                {
                    if (link != null)
                    {
                        link.customIcon = null;
                    }
                }
            }
        }

        internal static void Release(Texture2D texture)
        {
            if (CanRelease(texture, null))
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void Release(
            IEnumerable<Texture2D> textures,
            HashSet<Texture2D> retained)
        {
            if (textures == null)
            {
                return;
            }
            foreach (Texture2D texture in new HashSet<Texture2D>(textures))
            {
                if (CanRelease(texture, retained))
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        private static bool CanRelease(
            Texture2D texture,
            HashSet<Texture2D> retained)
        {
            return texture != null &&
                (retained == null || !retained.Contains(texture)) &&
                IsOwned(texture);
        }

        private static HashSet<Texture2D> Collect(PackageMetadata metadata)
        {
            var textures = new HashSet<Texture2D>();
            if (metadata == null)
            {
                return textures;
            }
            if (metadata.icon != null)
            {
                textures.Add(metadata.icon);
            }
            if (metadata.banner != null)
            {
                textures.Add(metadata.banner);
            }
            if (metadata.galleryImages != null)
            {
                foreach (Texture2D texture in metadata.galleryImages)
                {
                    if (texture != null)
                    {
                        textures.Add(texture);
                    }
                }
            }
            if (metadata.productLinks != null)
            {
                foreach (ProductLink link in metadata.productLinks)
                {
                    if (link?.customIcon != null)
                    {
                        textures.Add(link.customIcon);
                    }
                }
            }
            return textures;
        }

        private static bool IsOwned(Texture2D texture)
        {
            return texture != null &&
                (texture.hideFlags & HideFlags.HideAndDontSave) ==
                    HideFlags.HideAndDontSave &&
                !EditorUtility.IsPersistent(texture) &&
                !AssetDatabase.Contains(texture);
        }
    }
}
