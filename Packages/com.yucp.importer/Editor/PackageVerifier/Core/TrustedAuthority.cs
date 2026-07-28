using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YUCP.Importer.Editor.PackageVerifier.Settings;

namespace YUCP.Importer.Editor.PackageVerifier
{
    /// <summary>
    /// Hard-coded trusted authority configuration
    /// </summary>
    public static class TrustedAuthority
    {
        public const string AuthorityId = "unitysign.yucp";
        public const string DisplayName = "YUCP Signing Authority";
        public const string ApiBaseUrl = TrustedAuthoritiesSettings.DefaultTrustedUrl;
        public const string PrimaryRootKeyId = "yucp-root";
        public const string LegacyRootKeyId = "yucp-root-2025";
        public const string PinnedRootPublicKeyBase64 = "y+8Zs9/mS1MFZFeF4CFjwqe0nsLW8lCcwmyvBx6H0Zo=";

        private static Dictionary<string, byte[]> _publicKeysByKeyId;
        private static bool _initialized = false;

        static TrustedAuthority()
        {
            Initialize();
        }

        /// <summary>
        /// Initialize trusted authority with the code-pinned root CA keys.
        ///
        /// Deliberately touches no Unity editor API. This runs from a static
        /// constructor, so it executes on whichever thread first uses the type —
        /// during import that is a worker thread, and EditorPrefs is main-thread
        /// only. The cached-key store is not read here because trust is pinned:
        /// only <see cref="PinnedRootPublicKeyBase64"/> is ever accepted, so a
        /// cache read could not add a key this method has not already added.
        /// </summary>
        private static void Initialize()
        {
            if (_initialized) return;

            _publicKeysByKeyId = new Dictionary<string, byte[]>();

            LoadBuiltInAuthorityKeys();

            _initialized = true;
        }

        private static void LoadBuiltInAuthorityKeys()
        {
            byte[] pinnedRootKey = Convert.FromBase64String(PinnedRootPublicKeyBase64);
            _publicKeysByKeyId[PrimaryRootKeyId] = pinnedRootKey;
            _publicKeysByKeyId[LegacyRootKeyId] = pinnedRootKey;
        }

        /// <summary>
        /// Get public key by key ID
        /// </summary>
        public static byte[] GetPublicKey(string keyId)
        {
            // Re-initialize if needed (in case settings were updated)
            if (!_initialized)
            {
                Initialize();
            }

            return _publicKeysByKeyId.TryGetValue(keyId, out byte[] key) ? key : null;
        }

        /// <summary>
        /// Reload all public keys from the trusted URL cache.
        /// Call this after settings are updated or keys are fetched from URLs
        /// </summary>
        public static void ReloadAllKeys()
        {
            _initialized = false;
            Initialize();
        }

        internal static bool IsBuiltInKeyId(string keyId)
        {
            return !string.IsNullOrWhiteSpace(keyId) &&
                (string.Equals(keyId.Trim(), PrimaryRootKeyId, StringComparison.Ordinal) ||
                 string.Equals(keyId.Trim(), LegacyRootKeyId, StringComparison.Ordinal));
        }

        internal static bool TryGetBuiltInPublicKeyBase64(string keyId, out string publicKeyBase64)
        {
            publicKeyBase64 = null;
            if (!IsBuiltInKeyId(keyId))
                return false;

            publicKeyBase64 = PinnedRootPublicKeyBase64;
            return true;
        }

        internal static List<YUCP.Importer.Editor.PackageVerifier.Core.AuthorityKeyFetcher.AuthorityKey> FilterToPinnedKeys(
            IEnumerable<YUCP.Importer.Editor.PackageVerifier.Core.AuthorityKeyFetcher.AuthorityKey> keys)
        {
            var filtered = new List<YUCP.Importer.Editor.PackageVerifier.Core.AuthorityKeyFetcher.AuthorityKey>();
            if (keys == null)
                return filtered;

            foreach (var key in keys)
            {
                if (key == null || string.IsNullOrWhiteSpace(key.publicKey))
                    continue;

                if (!TryGetBuiltInPublicKeyBase64(key.keyId, out string pinnedPublicKeyBase64))
                    continue;

                if (!string.Equals(key.publicKey.Trim(), pinnedPublicKeyBase64, StringComparison.Ordinal))
                    continue;

                filtered.Add(new YUCP.Importer.Editor.PackageVerifier.Core.AuthorityKeyFetcher.AuthorityKey
                {
                    keyId = key.keyId.Trim(),
                    publicKey = pinnedPublicKeyBase64,
                    displayName = string.IsNullOrWhiteSpace(key.displayName) ? key.keyId.Trim() : key.displayName.Trim(),
                });
            }

            return filtered;
        }

        /// <summary>
        /// Reload root public key from settings
        /// </summary>
        [System.Obsolete("Use ReloadAllKeys() instead")]
        public static void ReloadRootPublicKey()
        {
            ReloadAllKeys();
        }

        /// <summary>
        /// Check if a key ID is trusted (exists in our trusted keys)
        /// </summary>
        public static bool IsTrustedKey(string keyId)
        {
            if (!_initialized)
            {
                Initialize();
            }
            return _publicKeysByKeyId.ContainsKey(keyId);
        }
    }
}

