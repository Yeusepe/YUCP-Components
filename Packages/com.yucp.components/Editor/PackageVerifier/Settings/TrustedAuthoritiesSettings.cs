using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.PackageVerifier.Settings
{
    /// <summary>
    /// Settings for trusted authority URLs and cached keys
    /// Uses EditorPrefs for storage
    /// </summary>
    [Serializable]
    public class TrustedAuthoritiesSettings
    {
        private const string PrefsKeyUrls = "YUCP.PackageVerifier.TrustedAuthorityUrls";
        private const string PrefsKeyCachedKeys = "YUCP.PackageVerifier.CachedAuthorityKeys";
        private const string PrefsKeyFetchTimes = "YUCP.PackageVerifier.KeyFetchTimes";

        [Serializable]
        public class CachedKey
        {
            public string keyId;
            public string publicKey;
            public string displayName;
            public string fetchTime;
        }

        [Serializable]
        public class CachedKeysData
        {
            public List<CachedKey> keys = new List<CachedKey>();
        }

        /// <summary>
        /// Get list of configured authority URLs
        /// </summary>
        public static List<string> GetUrls()
        {
            string urlsJson = EditorPrefs.GetString(PrefsKeyUrls, "[]");
            try
            {
                var wrapper = JsonUtility.FromJson<UrlListWrapper>(urlsJson);
                return wrapper.urls ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Set list of authority URLs
        /// </summary>
        public static void SetUrls(List<string> urls)
        {
            var wrapper = new UrlListWrapper { urls = urls ?? new List<string>() };
            string urlsJson = JsonUtility.ToJson(wrapper);
            EditorPrefs.SetString(PrefsKeyUrls, urlsJson);
        }

        /// <summary>
        /// Get cached keys from all URLs
        /// </summary>
        public static Dictionary<string, CachedKey> GetCachedKeys()
        {
            string cachedKeysJson = EditorPrefs.GetString(PrefsKeyCachedKeys, "{}");
            try
            {
                var data = JsonUtility.FromJson<CachedKeysData>(cachedKeysJson);
                var result = new Dictionary<string, CachedKey>();

                if (data?.keys != null)
                {
                    foreach (var key in data.keys)
                    {
                        if (!string.IsNullOrEmpty(key.keyId))
                        {
                            result[key.keyId] = key;
                        }
                    }
                }

                return result;
            }
            catch
            {
                return new Dictionary<string, CachedKey>();
            }
        }

        /// <summary>
        /// Cache keys from a fetch result
        /// </summary>
        public static void CacheKeys(string url, List<Core.AuthorityKeyFetcher.AuthorityKey> keys, DateTime fetchTime)
        {
            var cachedKeys = GetCachedKeys();

            foreach (var key in keys)
            {
                var cachedKey = new CachedKey
                {
                    keyId = key.keyId,
                    publicKey = key.publicKey,
                    displayName = key.displayName ?? "",
                    fetchTime = fetchTime.ToString("O")
                };

                cachedKeys[key.keyId] = cachedKey;
            }


            var data = new CachedKeysData
            {
                keys = cachedKeys.Values.ToList()
            };

            string cachedKeysJson = JsonUtility.ToJson(data);
            EditorPrefs.SetString(PrefsKeyCachedKeys, cachedKeysJson);


            var fetchTimes = GetFetchTimes();
            fetchTimes[url] = fetchTime.ToString("O");
            SaveFetchTimes(fetchTimes);
        }

        /// <summary>
        /// Get fetch times for URLs
        /// </summary>
        private static Dictionary<string, string> GetFetchTimes()
        {
            string fetchTimesJson = EditorPrefs.GetString(PrefsKeyFetchTimes, "{}");
            try
            {
                var wrapper = JsonUtility.FromJson<FetchTimesWrapper>(fetchTimesJson);
                var result = new Dictionary<string, string>();

                if (wrapper?.entries != null)
                {
                    foreach (var entry in wrapper.entries)
                    {
                        if (!string.IsNullOrEmpty(entry.url))
                        {
                            result[entry.url] = entry.time;
                        }
                    }
                }

                return result;
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Save fetch times
        /// </summary>
        private static void SaveFetchTimes(Dictionary<string, string> fetchTimes)
        {
            var wrapper = new FetchTimesWrapper
            {
                entries = new List<FetchTimeEntry>()
            };

            if (fetchTimes != null)
            {
                foreach (var kvp in fetchTimes)
                {
                    wrapper.entries.Add(new FetchTimeEntry { url = kvp.Key, time = kvp.Value });
                }
            }

            string fetchTimesJson = JsonUtility.ToJson(wrapper);
            EditorPrefs.SetString(PrefsKeyFetchTimes, fetchTimesJson);
        }

        /// <summary>
        /// Get fetch time for a specific URL
        /// </summary>
        public static DateTime? GetFetchTime(string url)
        {
            var fetchTimes = GetFetchTimes();
            if (fetchTimes.TryGetValue(url, out string timeStr))
            {
                if (DateTime.TryParse(timeStr, out DateTime time))
                {
                    return time;
                }
            }
            return null;
        }


        [Serializable]
        private class UrlListWrapper
        {
            public List<string> urls;
        }

        [Serializable]
        private class FetchTimesWrapper
        {
            public List<FetchTimeEntry> entries;
        }

        [Serializable]
        private class FetchTimeEntry
        {
            public string url;
            public string time;
        }
    }
}

