using UnityEditor;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class PackageMetadataMediaOwnership
    {
        internal static void Replace(
            PackageMetadata metadata,
            Texture2D icon,
            Texture2D banner)
        {
            if (metadata == null)
            {
                return;
            }
            Texture2D previousIcon = metadata.icon;
            Texture2D previousBanner = metadata.banner;
            metadata.icon = icon;
            metadata.banner = banner;
            Release(previousIcon, icon, banner);
            Release(previousBanner, icon, banner);
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
            Texture2D retainedIcon = retained?.icon;
            Texture2D retainedBanner = retained?.banner;
            if (CanRelease(
                    metadata.icon,
                    retainedIcon,
                    retainedBanner))
            {
                Release(metadata.icon);
                metadata.icon = null;
            }
            if (CanRelease(
                    metadata.banner,
                    retainedIcon,
                    retainedBanner))
            {
                Release(metadata.banner);
                metadata.banner = null;
            }
        }

        internal static void Release(Texture2D texture)
        {
            Release(texture, null, null);
        }

        private static void Release(
            Texture2D texture,
            Texture2D retainedIcon,
            Texture2D retainedBanner)
        {
            if (!CanRelease(
                    texture,
                    retainedIcon,
                    retainedBanner))
            {
                return;
            }
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static bool CanRelease(
            Texture2D texture,
            Texture2D retainedIcon,
            Texture2D retainedBanner)
        {
            return texture != null &&
                !ReferenceEquals(texture, retainedIcon) &&
                !ReferenceEquals(texture, retainedBanner) &&
                IsOwned(texture);
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
