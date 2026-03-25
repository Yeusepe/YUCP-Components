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

        private const string YUCP_ROOT_CA_PUBLIC_KEY_BASE64 = "y+8Zs9/mS1MFZFeF4CFjwqe0nsLW8lCcwmyvBx6H0Zo=";
        private static readonly string[] BuiltInRootKeyIds =
        {
            "yucp-authority-2025",
            "yucp-root-2025",
            "yucp-root-ca",
            "yucp-root",
        };

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
            
            // 1. Load hardcoded YUCP root CA key
            byte[] hardcodedRootKey = LoadHardcodedRootKey();
            if (hardcodedRootKey != null && hardcodedRootKey.Length == 32)
            {
                RegisterBuiltInRootKeyAliases(hardcodedRootKey);
            }
            else
            {
                Debug.LogWarning("[TrustedAuthority] Hardcoded YUCP root CA key not configured. Package verification may fail.");
            }
            
            byte[] settingsRootKey = LoadRootPublicKeyFromSettings();
            if (settingsRootKey != null && settingsRootKey.Length == 32)
            {
                RegisterBuiltInRootKeyAliases(settingsRootKey);
            }
            
            // 3. Load keys from URL-fetched cache
            LoadCachedAuthorityKeys();

            _initialized = true;
        }

        /// <summary>
        /// Load hardcoded YUCP root CA public key
        /// </summary>
        private static byte[] LoadHardcodedRootKey()
        {
            if (string.IsNullOrEmpty(YUCP_ROOT_CA_PUBLIC_KEY_BASE64))
            {
                return null;
            }

            try
            {
                byte[] key = Convert.FromBase64String(YUCP_ROOT_CA_PUBLIC_KEY_BASE64);
                if (key.Length == 32)
                {
                    return key;
                }
                else
                {
                    Debug.LogError($"[TrustedAuthority] Hardcoded root CA key has invalid length: {key.Length} (expected 32)");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TrustedAuthority] Failed to parse hardcoded root CA key: {ex.Message}");
            }

            return null;
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
        /// Load root public key from SigningSettings asset
        /// </summary>
        private static byte[] LoadRootPublicKeyFromSettings()
        {
            try
            {
                // Try to find SigningSettings asset (from com.yucp.devtools package)
                string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    
                    // Try to find SigningSettings type across all assemblies
                    Type signingSettingsType = null;
                    foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        signingSettingsType = assembly.GetType("YUCP.DevTools.Editor.PackageSigning.Data.SigningSettings");
                        if (signingSettingsType != null)
                            break;
                    }
                    
                    if (signingSettingsType == null)
                    {
                        // Try direct type lookup
                        signingSettingsType = Type.GetType("YUCP.DevTools.Editor.PackageSigning.Data.SigningSettings, Assembly-CSharp-Editor");
                    }
                    
                    if (signingSettingsType != null)
                    {
                        var settings = AssetDatabase.LoadAssetAtPath(path, signingSettingsType);
                        
                        if (settings != null)
                        {
                            // Use reflection to get yucpRootPublicKeyBase64 field
                            var field = signingSettingsType.GetField("yucpRootPublicKeyBase64", 
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            
                            if (field != null)
                            {
                                string base64Key = field.GetValue(settings) as string;
                                if (!string.IsNullOrEmpty(base64Key))
                                {
                                    try
                                    {
                                        return Convert.FromBase64String(base64Key);
                                    }
                                    catch
                                    {
                                        Debug.LogError("[TrustedAuthority] Failed to parse root public key from SigningSettings");
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[TrustedAuthority] SigningSettings type not found. Make sure com.yucp.devtools package is installed.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TrustedAuthority] Failed to load root public key from settings: {ex.Message}");
            }

            return null;
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
            
            byte[] rootKey = LoadRootPublicKeyFromSettings();
            if (rootKey != null && rootKey.Length == 32)
            {
                _publicKeysByKeyId[keyId] = rootKey;
                return rootKey;
            }

            return null;
        }

        /// <summary>
        /// Set root public key (for configuration)
        /// </summary>
        public static void SetRootPublicKey(string base64Key)
        {
            try
            {
                byte[] key = Convert.FromBase64String(base64Key);
                RegisterBuiltInRootKeyAliases(key);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TrustedAuthority] Failed to set root public key: {ex.Message}");
            }
        }

        /// <summary>
        /// Reload all public keys (hardcoded root, settings, and cached URL keys)
        /// Call this after settings are updated or keys are fetched from URLs
        /// </summary>
        public static void ReloadAllKeys()
        {
            _initialized = false;
            Initialize();
        }

        internal static bool IsBuiltInKeyId(string keyId)
        {
            if (string.IsNullOrEmpty(keyId))
            {
                return false;
            }

            foreach (string alias in BuiltInRootKeyIds)
            {
                if (string.Equals(alias, keyId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RegisterBuiltInRootKeyAliases(byte[] key)
        {
            foreach (string alias in BuiltInRootKeyIds)
            {
                _publicKeysByKeyId[alias] = key;
            }
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

