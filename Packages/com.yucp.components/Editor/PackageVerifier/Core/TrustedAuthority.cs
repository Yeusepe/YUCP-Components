using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.PackageVerifier
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
        /// Initialize trusted authority with root public key from settings
        /// </summary>
        private static void Initialize()
        {
            if (_initialized) return;

            _publicKeysByKeyId = new Dictionary<string, byte[]>();
            
            // Try to load root public key from SigningSettings
            byte[] rootKey = LoadRootPublicKeyFromSettings();
            
            if (rootKey != null && rootKey.Length == 32)
            {
                _publicKeysByKeyId["yucp-authority-2025"] = rootKey;
                _publicKeysByKeyId["yucp-root-2025"] = rootKey; // Also support the key ID used in certificates
                Debug.Log("[TrustedAuthority] Loaded root public key from SigningSettings");
            }
            else
            {
                Debug.LogWarning("[TrustedAuthority] Root public key not configured in SigningSettings. Package verification will fail.");
            }

            _initialized = true;
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
            
            // Try to reload from settings in case it was updated
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
                _publicKeysByKeyId["yucp-authority-2025"] = key;
                _publicKeysByKeyId["yucp-root-2025"] = key;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TrustedAuthority] Failed to set root public key: {ex.Message}");
            }
        }

        /// <summary>
        /// Reload root public key from settings (call after settings are updated)
        /// </summary>
        public static void ReloadRootPublicKey()
        {
            _initialized = false;
            Initialize();
        }
    }
}
