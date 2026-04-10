using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.Importer.Editor.PackageVerifier.Core
{
    /// <summary>
    /// Fetches and validates authority keys from remote URLs.
    /// Supports both the legacy authority list payload and the current JWK Set payload.
    /// </summary>
    public static class AuthorityKeyFetcher
    {
        private const string DefaultAuthorityPath = "/v1/keys";

        /// <summary>
        /// Authority key data structure.
        /// </summary>
        [Serializable]
        public class AuthorityKey
        {
            public string keyId;
            public string publicKey;
            public string displayName;
        }

        /// <summary>
        /// Legacy JSON response format from authority URL.
        /// </summary>
        [Serializable]
        public class AuthorityResponse
        {
            public AuthorityKey[] authorities;
        }

        [Serializable]
        private class JwkSetResponse
        {
            public JwkKey[] keys;
        }

        [Serializable]
        private class JwkKey
        {
            public string kid;
            public string x;
            public string kty;
            public string crv;
        }

        /// <summary>
        /// Result of fetching keys from a URL.
        /// </summary>
        public class FetchResult
        {
            public bool success;
            public List<AuthorityKey> keys = new List<AuthorityKey>();
            public string error;
            public DateTime fetchTime;
        }

        public static string GetAuthorityDocumentUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return DefaultAuthorityPath;
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri uri))
            {
                return url.Trim();
            }

            string path = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
            if (path == "/")
            {
                return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + DefaultAuthorityPath;
            }

            if (path.EndsWith(DefaultAuthorityPath, StringComparison.OrdinalIgnoreCase))
            {
                return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            }

            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        /// <summary>
        /// Fetch authority keys from a URL.
        /// </summary>
        public static IEnumerator FetchKeysFromUrlCoroutine(string url, Action<FetchResult> callback)
        {
            var result = new FetchResult();
            string fetchUrl = GetAuthorityDocumentUrl(url);

            using (UnityWebRequest request = UnityWebRequest.Get(fetchUrl))
            {
                request.SetRequestHeader("Accept-Encoding", "identity");
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    result.success = false;
                    result.error = BuildRequestError(url, fetchUrl, request);
                    callback(result);
                    yield break;
                }

                try
                {
                    string jsonText = request.downloadHandler.text;
                    result = ParseAuthorityResponse(jsonText, fetchUrl);
                    result.fetchTime = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    result.success = false;
                    result.error = $"Parse error while reading '{fetchUrl}': {ex.Message}";
                    callback(result);
                    yield break;
                }
            }

            callback(result);
        }

        /// <summary>
        /// Parse JSON authority response and validate keys.
        /// </summary>
        public static FetchResult ParseAuthorityResponse(string jsonText)
        {
            return ParseAuthorityResponse(jsonText, "the authority endpoint");
        }

        private static FetchResult ParseAuthorityResponse(string jsonText, string fetchUrl)
        {
            var result = new FetchResult();
            string trimmed = jsonText == null ? string.Empty : jsonText.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                result.success = false;
                result.error = $"The authority document at '{fetchUrl}' was empty.";
                return result;
            }

            if (TryParseLegacyAuthorities(trimmed, result) || TryParseJwkSet(trimmed, result))
            {
                if (result.keys.Count == 0)
                {
                    result.success = false;
                    result.error = $"The authority document at '{fetchUrl}' did not contain any valid Ed25519 public keys.";
                    return result;
                }

                result.success = true;
                return result;
            }

            result.success = false;
            if (trimmed.StartsWith("<", StringComparison.Ordinal))
            {
                result.error = $"Expected JSON authority keys from '{fetchUrl}', but the server returned HTML instead. Enter the base server URL and make sure {DefaultAuthorityPath} is available.";
            }
            else
            {
                result.error =
                    $"Expected JSON authority keys from '{fetchUrl}'. Supported formats are '{{\"authorities\": [...]}}' and JWK Set '{{\"keys\": [...]}}'.";
            }

            return result;
        }

        private static bool TryParseLegacyAuthorities(string jsonText, FetchResult result)
        {
            var response = JsonUtility.FromJson<AuthorityResponse>(jsonText);
            if (response == null || response.authorities == null)
            {
                return false;
            }

            foreach (AuthorityKey authority in response.authorities)
            {
                AddValidatedKey(result, authority);
            }

            return true;
        }

        private static bool TryParseJwkSet(string jsonText, FetchResult result)
        {
            var response = JsonUtility.FromJson<JwkSetResponse>(jsonText);
            if (response == null || response.keys == null)
            {
                return false;
            }

            foreach (JwkKey key in response.keys)
            {
                if (key == null)
                {
                    continue;
                }

                AddValidatedKey(result, new AuthorityKey
                {
                    keyId = key.kid,
                    publicKey = key.x,
                    displayName = string.IsNullOrEmpty(key.kid) ? "YUCP Signing Key" : key.kid
                });
            }

            return true;
        }

        private static void AddValidatedKey(FetchResult result, AuthorityKey authority)
        {
            if (authority == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(authority.keyId))
            {
                Debug.LogWarning("[AuthorityKeyFetcher] Skipping authority with missing keyId");
                return;
            }

            if (string.IsNullOrEmpty(authority.publicKey))
            {
                Debug.LogWarning($"[AuthorityKeyFetcher] Skipping authority '{authority.keyId}' with missing publicKey");
                return;
            }

            try
            {
                byte[] keyBytes = Convert.FromBase64String(authority.publicKey);
                if (keyBytes.Length != 32)
                {
                    Debug.LogWarning($"[AuthorityKeyFetcher] Skipping authority '{authority.keyId}': invalid key length {keyBytes.Length} (expected 32)");
                    return;
                }

                result.keys.Add(authority);
            }
            catch (FormatException)
            {
                Debug.LogWarning($"[AuthorityKeyFetcher] Skipping authority '{authority.keyId}': invalid base64 publicKey");
            }
        }

        private static string BuildRequestError(string sourceUrl, string fetchUrl, UnityWebRequest request)
        {
            string prefix = request.responseCode > 0
                ? $"HTTP {request.responseCode}"
                : "Network error";

            if (request.responseCode == 404 && !string.Equals(sourceUrl?.TrimEnd('/'), fetchUrl?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                return $"{prefix} while fetching '{fetchUrl}'. Unity fetches authority keys from {DefaultAuthorityPath} automatically, so make sure that endpoint exists on the trusted server.";
            }

            return $"{prefix} while fetching '{fetchUrl}': {request.error}";
        }

        /// <summary>
        /// Synchronously fetch keys from URL (for use in non-coroutine contexts).
        /// </summary>
        public static FetchResult FetchKeysFromUrlSync(string url)
        {
            var result = new FetchResult();
            string fetchUrl = GetAuthorityDocumentUrl(url);

            try
            {
                using (UnityWebRequest request = UnityWebRequest.Get(fetchUrl))
                {
                    request.timeout = 15;
                    request.SetRequestHeader("Accept-Encoding", "identity");
                    request.SendWebRequest();

                    while (!request.isDone)
                    {
                        System.Threading.Thread.Sleep(10);
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        result.success = false;
                        result.error = BuildRequestError(url, fetchUrl, request);
                        return result;
                    }

                    string jsonText = request.downloadHandler.text;
                    result = ParseAuthorityResponse(jsonText, fetchUrl);
                    result.fetchTime = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                result.success = false;
                result.error = $"Error while fetching '{fetchUrl}': {ex.Message}";
            }

            return result;
        }
    }
}
