using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.Components.Editor.PackageVerifier.Core
{
    /// <summary>
    /// Fetches and validates authority keys from remote URLs
    /// </summary>
    public static class AuthorityKeyFetcher
    {
        /// <summary>
        /// Authority key data structure
        /// </summary>
        [Serializable]
        public class AuthorityKey
        {
            public string keyId;
            public string publicKey;
            public string displayName;
        }

        /// <summary>
        /// JSON response format from authority URL
        /// </summary>
        [Serializable]
        public class AuthorityResponse
        {
            public AuthorityKey[] authorities;
        }

        /// <summary>
        /// Result of fetching keys from a URL
        /// </summary>
        public class FetchResult
        {
            public bool success;
            public List<AuthorityKey> keys = new List<AuthorityKey>();
            public string error;
            public DateTime fetchTime;
        }

        /// <summary>
        /// Fetch authority keys from a URL
        /// </summary>
        public static IEnumerator FetchKeysFromUrlCoroutine(string url, System.Action<FetchResult> callback)
        {
            var result = new FetchResult();

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    result.success = false;
                    result.error = $"Network error: {request.error}";
                    callback(result);
                    yield break;
                }

                try
                {
                    string jsonText = request.downloadHandler.text;
                    result = ParseAuthorityResponse(jsonText);
                    result.fetchTime = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    result.success = false;
                    result.error = $"Parse error: {ex.Message}";
                    callback(result);
                    yield break;
                }
            }

            callback(result);
        }

        /// <summary>
        /// Parse JSON authority response and validate keys
        /// </summary>
        public static FetchResult ParseAuthorityResponse(string jsonText)
        {
            var result = new FetchResult();

            try
            {
                var response = JsonUtility.FromJson<AuthorityResponse>(jsonText);

                if (response == null || response.authorities == null)
                {
                    result.success = false;
                    result.error = "Invalid JSON format: missing 'authorities' field";
                    return result;
                }

                foreach (var authority in response.authorities)
                {
                    if (string.IsNullOrEmpty(authority.keyId))
                    {
                        Debug.LogWarning("[AuthorityKeyFetcher] Skipping authority with missing keyId");
                        continue;
                    }

                    if (string.IsNullOrEmpty(authority.publicKey))
                    {
                        Debug.LogWarning($"[AuthorityKeyFetcher] Skipping authority '{authority.keyId}' with missing publicKey");
                        continue;
                    }

                    // Validate public key format (should be base64-encoded 32-byte Ed25519 key)
                    try
                    {
                        byte[] keyBytes = Convert.FromBase64String(authority.publicKey);
                        if (keyBytes.Length != 32)
                        {
                            Debug.LogWarning($"[AuthorityKeyFetcher] Skipping authority '{authority.keyId}': invalid key length {keyBytes.Length} (expected 32)");
                            continue;
                        }

                        result.keys.Add(authority);
                    }
                    catch (FormatException)
                    {
                        Debug.LogWarning($"[AuthorityKeyFetcher] Skipping authority '{authority.keyId}': invalid base64 publicKey");
                        continue;
                    }
                }

                result.success = true;
            }
            catch (Exception ex)
            {
                result.success = false;
                result.error = $"Failed to parse JSON: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Synchronously fetch keys from URL (for use in non-coroutine contexts)
        /// </summary>
        public static FetchResult FetchKeysFromUrlSync(string url)
        {
            var result = new FetchResult();

            try
            {
                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    request.SendWebRequest();

                    // Wait for completion
                    while (!request.isDone)
                    {
                        System.Threading.Thread.Sleep(10);
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        result.success = false;
                        result.error = $"Network error: {request.error}";
                        return result;
                    }

                    string jsonText = request.downloadHandler.text;
                    result = ParseAuthorityResponse(jsonText);
                    result.fetchTime = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                result.success = false;
                result.error = $"Error: {ex.Message}";
            }

            return result;
        }
    }
}

