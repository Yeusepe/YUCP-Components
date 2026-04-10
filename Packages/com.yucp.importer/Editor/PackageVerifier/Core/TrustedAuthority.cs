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
        public const string ApiBaseUrl = "https://signing.yucp.club"; // configurable

        private static Dictionary<string, byte[]> _publicKeysByKeyId;
        private static bool _initialized = false;

        static TrustedAuthority()
        {
            Initialize();
        }

        /// <summary>
        /// Initialize trusted authority with hardcoded root CA key and URL-fetched keys
        /// </summary>
        private static void Initialize()
        {
            if (_initialized) return;

            _publicKeysByKeyId = new Dictionary<string, byte[]>();

            // Load keys from the trusted-URL cache only.
            LoadCachedAuthorityKeys();

            _initialized = true;
        }

        /// <summary>
        /// Load cached authority keys from TrustedAuthoritiesSettings
        /// </summary>
        private static void LoadCachedAuthorityKeys()
        {
            try
            {
                var cachedKeys = TrustedAuthoritiesSettings.GetCachedKeys();
                int loadedCount = 0;

                foreach (var kvp in cachedKeys)
                {
                    if (string.IsNullOrEmpty(kvp.Value?.publicKey))
                        continue;

                    try
                    {
                        byte[] keyBytes = Convert.FromBase64String(kvp.Value.publicKey);
                        if (keyBytes.Length == 32)
                        {
                            _publicKeysByKeyId[kvp.Key] = keyBytes;
                            loadedCount++;
                        }
                        else
                        {
                            Debug.LogWarning($"[TrustedAuthority] Cached key '{kvp.Key}' has invalid length: {keyBytes.Length} (expected 32)");
                        }
                    }
                    catch (FormatException)
                    {
                        Debug.LogWarning($"[TrustedAuthority] Failed to parse cached key '{kvp.Key}': invalid base64");
                    }
                }

            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TrustedAuthority] Failed to load cached authority keys: {ex.Message}");
            }
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

            if (_publicKeysByKeyId.TryGetValue(keyId, out byte[] key))
            {
                return key;
            }
            
            // Try to reload from cache in case keys were updated
            LoadCachedAuthorityKeys();
            
            if (_publicKeysByKeyId.TryGetValue(keyId, out key))
            {
                return key;
            }

            return null;
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
            return false;
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

