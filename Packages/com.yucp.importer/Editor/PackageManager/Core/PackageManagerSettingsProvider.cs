using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Importer.Editor.PackageManager.Core;
using YUCP.Importer.Editor.PackageVerifier;
using YUCP.Importer.Editor.PackageVerifier.Core;
using YUCP.Importer.Editor.PackageVerifier.Settings;

namespace YUCP.Importer.Editor.PackageManager
{
    internal sealed class PackageManagerSettingsProvider : SettingsProvider
    {
        private const string PreferredServerUrlKey = "YUCP.PackageManager.PreferredServerUrl";

        public PackageManagerSettingsProvider(string path, SettingsScope scope)
            : base(path, scope)
        {
        }

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new PackageManagerSettingsProvider("Project/YUCP Package Manager", SettingsScope.Project)
            {
                keywords = new HashSet<string>
                {
                    "YUCP",
                    "Package Manager",
                    "Importer",
                    "Import",
                    "Interception",
                    "License",
                    "Creator Identity",
                    "Trusted",
                    "URL",
                    "Authority"
                }
            };
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            rootElement.style.paddingLeft = 10;
            rootElement.style.paddingRight = 10;
            rootElement.style.paddingTop = 10;
            rootElement.style.paddingBottom = 10;

            var title = new Label("YUCP Package Manager");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 14;
            title.style.marginBottom = 6;
            rootElement.Add(title);

            var description = new Label("Controls importer interception, license verification, and the URLs this project trusts for Creator Identity and authority data.");
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginBottom = 10;
            rootElement.Add(description);

            var enabledToggle = new Toggle("Enable Package Manager")
            {
                value = PackageManagerRuntimeSettings.IsEnabled()
            };
            enabledToggle.RegisterValueChangedCallback(evt =>
            {
                PackageManagerRuntimeSettings.SetEnabled(evt.newValue);
            });
            rootElement.Add(enabledToggle);

            var importerHelp = new HelpBox(
                "Enabled by default. When disabled, importer interception is skipped and the package manager window will not open.",
                HelpBoxMessageType.Info);
            importerHelp.style.marginTop = 8;
            importerHelp.style.marginBottom = 16;
            rootElement.Add(importerHelp);

            // ── Creator Identity ─────────────────────────────────────────────────
            var identityTitle = new Label("Creator Identity");
            identityTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            identityTitle.style.marginBottom = 4;
            rootElement.Add(identityTitle);

            var identityContainer = new VisualElement();
            identityContainer.style.marginBottom = 16;
            rootElement.Add(identityContainer);

            void RefreshIdentitySection()
            {
                identityContainer.Clear();
                if (CreatorIdentityOAuthService.IsSignedIn())
                {
                    string displayName = CreatorIdentityOAuthService.GetDisplayName() ?? "Unknown";

                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;

                    var nameLabel = new Label($"Signed in as {displayName}");
                    nameLabel.style.flexGrow = 1;
                    row.Add(nameLabel);

                    var signOutBtn = new Button(() =>
                    {
                        CreatorIdentityOAuthService.SignOut();
                        RefreshIdentitySection();
                    }) { text = "Sign out" };
                    signOutBtn.style.marginLeft = 8;
                    row.Add(signOutBtn);

                    identityContainer.Add(row);
                }
                else
                {
                    var notSignedIn = new HelpBox(
                        "Not signed in. Open an importable package to sign in with Creator Identity.",
                        HelpBoxMessageType.Info);
                    identityContainer.Add(notSignedIn);
                }
            }

            RefreshIdentitySection();

            var serverTitle = new Label("License Server");
            serverTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            serverTitle.style.marginBottom = 4;
            rootElement.Add(serverTitle);

            var serverDescription = new Label("Choose which trusted server origin the importer should use for Creator Identity sign-in and license verification.");
            serverDescription.style.whiteSpace = WhiteSpace.Normal;
            serverDescription.style.marginBottom = 6;
            rootElement.Add(serverDescription);

            var preferredServerField = new TextField("Preferred URL");
            preferredServerField.value = GetPreferredServerUrl();
            preferredServerField.style.marginBottom = 6;
            rootElement.Add(preferredServerField);

            var serverStatus = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            serverStatus.style.marginBottom = 8;
            rootElement.Add(serverStatus);

            void RefreshServerStatus()
            {
                string normalized = TrustedAuthoritiesSettings.NormalizeUrl(preferredServerField.value);
                if (string.IsNullOrEmpty(preferredServerField.value))
                {
                    serverStatus.text = "Leave blank to fall back to the exporter SigningSettings URL or the first trusted URL.";
                    serverStatus.messageType = HelpBoxMessageType.Info;
                }
                else if (string.IsNullOrEmpty(normalized))
                {
                    serverStatus.text = "Preferred URL must be a valid absolute URL like https://api.creators.yucp.club or http://localhost:3000.";
                    serverStatus.messageType = HelpBoxMessageType.Error;
                }
                else if (!TrustedAuthoritiesSettings.IsTrustedUrl(normalized))
                {
                    serverStatus.text = "This server origin is not in the trusted list yet. Add it below before using it for sign-in or verification.";
                    serverStatus.messageType = HelpBoxMessageType.Warning;
                }
                else
                {
                    serverStatus.text = "This trusted server origin will be used by the importer for Creator Identity and license verification.";
                    serverStatus.messageType = HelpBoxMessageType.Info;
                }
            }

            preferredServerField.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetString(PreferredServerUrlKey, evt.newValue ?? string.Empty);
                RefreshServerStatus();
            });

            var useSigningSettingsButton = new Button(() =>
            {
                string signingUrl = GetSigningSettingsServerUrl();
                if (!string.IsNullOrEmpty(signingUrl))
                {
                    preferredServerField.value = signingUrl;
                    EditorPrefs.SetString(PreferredServerUrlKey, signingUrl);
                }
            })
            {
                text = "Use Exporter Signing URL"
            };
            useSigningSettingsButton.style.marginBottom = 16;
            rootElement.Add(useSigningSettingsButton);

            var trustTitle = new Label("Trusted Servers");
            trustTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            trustTitle.style.marginBottom = 4;
            rootElement.Add(trustTitle);

            var trustDescription = new Label("Add the base server origin only. Unity will fetch authority keys from /v1/keys automatically and use the same trusted origin for Creator Identity sign-in.");
            trustDescription.style.whiteSpace = WhiteSpace.Normal;
            trustDescription.style.marginBottom = 6;
            rootElement.Add(trustDescription);

            var trustedUrlsContainer = new VisualElement();
            trustedUrlsContainer.style.marginBottom = 8;
            rootElement.Add(trustedUrlsContainer);

            var addRow = new VisualElement();
            addRow.style.flexDirection = FlexDirection.Row;
            addRow.style.marginBottom = 8;

            var newUrlField = new TextField();
            newUrlField.style.flexGrow = 1;
            newUrlField.style.marginRight = 6;
            newUrlField.value = string.Empty;
            addRow.Add(newUrlField);

            var addUrlButton = new Button();
            addUrlButton.text = "Add Server";
            addRow.Add(addUrlButton);

            rootElement.Add(addRow);

            var fetchHelp = new HelpBox("When you add or refresh a trusted server, Unity fetches authority keys from /v1/keys, caches them locally, and reloads package verification trust immediately.", HelpBoxMessageType.Info);
            fetchHelp.style.marginBottom = 8;
            rootElement.Add(fetchHelp);

            void RefreshTrustedUrlsList()
            {
                trustedUrlsContainer.Clear();
                List<string> urls = TrustedAuthoritiesSettings.GetUrls();

                if (urls.Count == 0)
                {
                    var empty = new HelpBox("No trusted servers configured yet. Add your local or production API origin here, for example https://api.creators.yucp.club.", HelpBoxMessageType.Warning);
                    trustedUrlsContainer.Add(empty);
                    RefreshServerStatus();
                    return;
                }

                foreach (string url in urls)
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.marginBottom = 6;

                    var label = new Label(url);
                    label.style.flexGrow = 1;
                    label.style.whiteSpace = WhiteSpace.Normal;
                    row.Add(label);

                    DateTime? fetchTime = TrustedAuthoritiesSettings.GetFetchTime(url);
                    if (fetchTime.HasValue)
                    {
                        var timeLabel = new Label($"Fetched {fetchTime.Value.ToLocalTime():g}");
                        timeLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                        timeLabel.style.opacity = 0.7f;
                        timeLabel.style.marginLeft = 6;
                        timeLabel.style.marginRight = 6;
                        row.Add(timeLabel);
                    }

                    var refreshButton = new Button(() => RefreshTrustedUrl(url))
                    {
                        text = "Refresh Keys"
                    };
                    refreshButton.style.marginRight = 6;
                    row.Add(refreshButton);

                    var removeButton = new Button(() =>
                    {
                        TrustedAuthoritiesSettings.RemoveUrl(url);
                        TrustedAuthoritiesSettings.ClearCachedKeys(url);
                        TrustedAuthority.ReloadAllKeys();
                        RefreshTrustedUrlsList();
                    })
                    {
                        text = "Remove"
                    };
                    row.Add(removeButton);

                    trustedUrlsContainer.Add(row);
                }

                RefreshServerStatus();
            }

            addUrlButton.clicked += () =>
            {
                string normalized = TrustedAuthoritiesSettings.NormalizeUrl(newUrlField.value);
                if (string.IsNullOrEmpty(normalized))
                {
                    EditorUtility.DisplayDialog("Invalid URL", "Enter a valid absolute URL before adding it to the trusted list.", "OK");
                    return;
                }

                TrustedAuthoritiesSettings.AddUrl(normalized);
                if (string.IsNullOrWhiteSpace(preferredServerField.value))
                {
                    preferredServerField.value = normalized;
                    EditorPrefs.SetString(PreferredServerUrlKey, normalized);
                }

                newUrlField.value = string.Empty;
                RefreshTrustedUrl(normalized);
                RefreshTrustedUrlsList();
            };

            RefreshTrustedUrlsList();
        }

        private static void RefreshTrustedUrl(string url)
        {
            var result = AuthorityKeyFetcher.FetchKeysFromUrlSync(url);
            if (result.success)
            {
                TrustedAuthoritiesSettings.CacheKeys(url, result.keys, result.fetchTime);
                TrustedAuthority.ReloadAllKeys();
            }
            else
            {
                string fetchUrl = AuthorityKeyFetcher.GetAuthorityDocumentUrl(url);
                EditorUtility.DisplayDialog("Failed to Refresh Trusted URL", $"Could not fetch authority keys from server '{url}'.\nUnity tried '{fetchUrl}'.\n\n{result.error}", "OK");
            }
        }

        private static string GetPreferredServerUrl()
        {
            string preferred = EditorPrefs.GetString(PreferredServerUrlKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                return preferred;
            }

            string signingUrl = GetSigningSettingsServerUrl();
            if (!string.IsNullOrWhiteSpace(signingUrl))
            {
                return signingUrl;
            }

            var trustedUrls = TrustedAuthoritiesSettings.GetUrls();
            if (trustedUrls.Count > 0)
            {
                return trustedUrls[0];
            }

            return TrustedAuthoritiesSettings.DefaultTrustedUrl;
        }

        private static string GetSigningSettingsServerUrl()
        {
            try
            {
                string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
                if (guids.Length == 0)
                {
                    return null;
                }

                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                Type signingSettingsType = null;

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    signingSettingsType = assembly.GetType("YUCP.DevTools.Editor.PackageSigning.Data.SigningSettings");
                    if (signingSettingsType != null)
                    {
                        break;
                    }
                }

                if (signingSettingsType == null)
                {
                    signingSettingsType = Type.GetType("YUCP.DevTools.Editor.PackageSigning.Data.SigningSettings, Assembly-CSharp-Editor");
                }

                if (signingSettingsType == null)
                {
                    return null;
                }

                var settings = AssetDatabase.LoadAssetAtPath(path, signingSettingsType);
                if (settings == null)
                {
                    return null;
                }

                var field = signingSettingsType.GetField("serverUrl", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                return field?.GetValue(settings) as string;
            }
            catch
            {
                return null;
            }
        }
    }
}
