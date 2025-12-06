using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YUCP.Components.Editor.PackageVerifier.Core;

namespace YUCP.Components.Editor.PackageVerifier.Settings
{
    /// <summary>
    /// Unity Settings Provider for managing trusted authority URLs
    /// </summary>
    public class TrustedAuthoritiesSettingsProvider : SettingsProvider
    {
        private Vector2 scrollPosition;
        private Dictionary<string, string> urlInputFields = new Dictionary<string, string>();
        private Dictionary<string, bool> fetchingStatus = new Dictionary<string, bool>();
        private Dictionary<string, string> fetchErrors = new Dictionary<string, string>();

        public TrustedAuthoritiesSettingsProvider(string path, SettingsScope scope = SettingsScope.Project)
            : base(path, scope)
        {
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            var provider = new TrustedAuthoritiesSettingsProvider("YUCP/Package Verification/Trusted Authorities", SettingsScope.Project);
            return provider;
        }

        public override void OnGUI(string searchContext)
        {
            EditorGUILayout.LabelField("Trusted Authority URLs", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Configure URLs that provide trusted authority public keys. Keys are fetched and cached locally.",
                MessageType.Info
            );

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            var urls = TrustedAuthoritiesSettings.GetUrls();
            var cachedKeys = TrustedAuthoritiesSettings.GetCachedKeys();

            // Display existing URLs
            for (int i = 0; i < urls.Count; i++)
            {
                string url = urls[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                
                // URL display/edit field
                if (!urlInputFields.ContainsKey(url))
                {
                    urlInputFields[url] = url;
                }
                urlInputFields[url] = EditorGUILayout.TextField("URL", urlInputFields[url]);

                // Fetch button
                bool isFetching = fetchingStatus.ContainsKey(url) && fetchingStatus[url];
                EditorGUI.BeginDisabledGroup(isFetching);
                if (GUILayout.Button(isFetching ? "Fetching..." : "Fetch Keys", GUILayout.Width(100)))
                {
                    FetchKeysForUrl(urlInputFields[url]);
                }
                EditorGUI.EndDisabledGroup();

                // Remove button
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                {
                    var updatedUrls = new List<string>(urls);
                    updatedUrls.RemoveAt(i);
                    TrustedAuthoritiesSettings.SetUrls(updatedUrls);
                    urlInputFields.Remove(url);
                    fetchingStatus.Remove(url);
                    fetchErrors.Remove(url);
                    break;
                }

                EditorGUILayout.EndHorizontal();

                // Status display
                if (fetchErrors.ContainsKey(url) && !string.IsNullOrEmpty(fetchErrors[url]))
                {
                    EditorGUILayout.HelpBox($"Error: {fetchErrors[url]}", MessageType.Error);
                }
                else if (cachedKeys.Count > 0)
                {
                    var keysFromUrl = cachedKeys.Values.Where(k => 
                        !string.IsNullOrEmpty(k.keyId) && cachedKeys.ContainsKey(k.keyId)
                    ).ToList();
                    
                    if (keysFromUrl.Count > 0)
                    {
                        EditorGUILayout.LabelField($"Cached Keys: {keysFromUrl.Count}", EditorStyles.miniLabel);
                        foreach (var key in keysFromUrl)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField($"  • {key.keyId}", EditorStyles.miniLabel, GUILayout.Width(200));
                            if (!string.IsNullOrEmpty(key.displayName))
                            {
                                EditorGUILayout.LabelField($"({key.displayName})", EditorStyles.miniLabel);
                            }
                            EditorGUILayout.EndHorizontal();
                            
                            // Show public key preview
                            if (!string.IsNullOrEmpty(key.publicKey))
                            {
                                string preview = key.publicKey.Length > 32 
                                    ? key.publicKey.Substring(0, 32) + "..." 
                                    : key.publicKey;
                                EditorGUILayout.LabelField($"    Key: {preview}", EditorStyles.miniLabel);
                            }
                        }

                        var fetchTime = TrustedAuthoritiesSettings.GetFetchTime(url);
                        if (fetchTime.HasValue)
                        {
                            EditorGUILayout.LabelField($"Last fetched: {fetchTime.Value:g}", EditorStyles.miniLabel);
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField("No keys cached", EditorStyles.miniLabel);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("No keys cached", EditorStyles.miniLabel);
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }

            // Add new URL button
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add URL"))
            {
                var newUrls = new List<string>(urls) { "https://example.com/authorities.json" };
                TrustedAuthoritiesSettings.SetUrls(newUrls);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();

            // YUCP Root CA info
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Hardcoded Root CA", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "YUCP Root CA public key is hardcoded in TrustedAuthority.cs for security.",
                MessageType.Info
            );
        }

        private void FetchKeysForUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                fetchErrors[url] = "URL cannot be empty";
                return;
            }

            fetchingStatus[url] = true;
            fetchErrors.Remove(url);

            // Use synchronous fetch (will block UI briefly, but simpler for Settings Provider)
            var result = AuthorityKeyFetcher.FetchKeysFromUrlSync(url);

            fetchingStatus[url] = false;

            if (result != null)
            {
                if (result.success && result.keys.Count > 0)
                {
                    TrustedAuthoritiesSettings.CacheKeys(url, result.keys, result.fetchTime);
                    Debug.Log($"[TrustedAuthoritiesSettingsProvider] Successfully fetched {result.keys.Count} keys from {url}");
                    
                    // Reload keys in TrustedAuthority
                    TrustedAuthority.ReloadAllKeys();
                    
                    var urls = TrustedAuthoritiesSettings.GetUrls();
                    if (!urls.Contains(url) && urlInputFields.ContainsKey(url))
                    {
                        // Update existing entry
                        int index = urls.FindIndex(u => urlInputFields.ContainsKey(u) && urlInputFields[u] == url);
                        if (index >= 0)
                        {
                            urls[index] = url;
                            TrustedAuthoritiesSettings.SetUrls(urls);
                        }
                    }
                }
                else
                {
                    fetchErrors[url] = result.error ?? "Failed to fetch keys";
                    Debug.LogError($"[TrustedAuthoritiesSettingsProvider] Failed to fetch keys from {url}: {fetchErrors[url]}");
                }
            }
            else
            {
                fetchErrors[url] = "Failed to fetch keys (unknown error)";
            }
        }

        public override void OnDeactivate()
        {
            // Update URLs from input fields
            var urls = TrustedAuthoritiesSettings.GetUrls();
            var updatedUrls = new List<string>();
            
            foreach (var kvp in urlInputFields)
            {
                if (!string.IsNullOrEmpty(kvp.Value))
                {
                    updatedUrls.Add(kvp.Value);
                }
            }
            
            // Add any URLs that weren't in input fields (shouldn't happen, but safety check)
            foreach (var url in urls)
            {
                if (!urlInputFields.ContainsKey(url) && !updatedUrls.Contains(url))
                {
                    updatedUrls.Add(url);
                }
            }
            
            TrustedAuthoritiesSettings.SetUrls(updatedUrls);
        }
    }
}

