using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Importer.Editor.PackageVerifier;
using YUCP.Importer.Editor.PackageVerifier.Core;
using YUCP.Importer.Editor.PackageVerifier.Settings;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.PackageManager
{
    internal sealed class PackageManagerSettingsProvider :
        SettingsProvider
    {
        private PackageManagerSettingsProvider(
            string path,
            SettingsScope scope)
            : base(path, scope)
        {
        }

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new PackageManagerSettingsProvider(
                "Project/YUCP Package Manager",
                SettingsScope.Project)
            {
                keywords = new HashSet<string>
                {
                    "YUCP",
                    "Importer",
                    "Package Manager",
                    "Signature",
                    "Trusted",
                },
            };
        }

        public override void OnActivate(
            string searchContext,
            VisualElement rootElement)
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

            var description = new Label(
                "Controls importer interception and package signature " +
                "trust. The YUCP desktop service manages Creator Account access.");
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginBottom = 10;
            rootElement.Add(description);

            var enabledToggle = new Toggle("Enable Package Manager")
            {
                value = PackageManagerRuntimeSettings.IsEnabled(),
            };
            enabledToggle.RegisterValueChangedCallback(
                change => PackageManagerRuntimeSettings.SetEnabled(
                    change.newValue));
            rootElement.Add(enabledToggle);

            var accountHelp = new HelpBox(
                "Sign-in and purchase verification happen in the YUCP " +
                "desktop service. Unity does not store account credentials.",
                HelpBoxMessageType.Info);
            accountHelp.style.marginTop = 8;
            accountHelp.style.marginBottom = 16;
            rootElement.Add(accountHelp);

            var trustTitle = new Label("Trusted signature servers");
            trustTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            trustTitle.style.marginBottom = 4;
            rootElement.Add(trustTitle);

            var trustDescription = new Label(
                "Add only server origins that publish the pinned YUCP " +
                "package-signing roots.");
            trustDescription.style.whiteSpace = WhiteSpace.Normal;
            trustDescription.style.marginBottom = 6;
            rootElement.Add(trustDescription);

            var trustedUrls = new VisualElement();
            trustedUrls.style.marginBottom = 8;
            rootElement.Add(trustedUrls);

            var addRow = new VisualElement();
            addRow.style.flexDirection = FlexDirection.Row;
            addRow.style.marginBottom = 8;
            var newUrl = new TextField();
            newUrl.style.flexGrow = 1;
            newUrl.style.marginRight = 6;
            addRow.Add(newUrl);
            var addButton = new Button
            {
                text = "Add server",
            };
            addRow.Add(addButton);
            rootElement.Add(addRow);

            void RefreshList()
            {
                trustedUrls.Clear();
                List<string> urls =
                    TrustedAuthoritiesSettings.GetUrls();
                if (urls.Count == 0)
                {
                    trustedUrls.Add(new HelpBox(
                        "No package-signing servers are configured.",
                        HelpBoxMessageType.Warning));
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

                    var refresh = new Button
                    {
                        text = "Validate roots",
                    };
                    refresh.style.marginRight = 6;
                    row.Add(refresh);
                    var remove = new Button(() =>
                    {
                        TrustedAuthoritiesSettings.RemoveUrl(url);
                        TrustedAuthoritiesSettings.ClearCachedKeys(url);
                        TrustedAuthority.ReloadAllKeys();
                        RefreshList();
                    })
                    {
                        text = "Remove",
                    };
                    refresh.clicked += async () =>
                    {
                        refresh.SetEnabled(false);
                        remove.SetEnabled(false);
                        refresh.text = "Validating...";
                        try
                        {
                            await RefreshTrustedUrlAsync(url);
                        }
                        finally
                        {
                            refresh.text = "Validate roots";
                            refresh.SetEnabled(true);
                            remove.SetEnabled(true);
                        }
                    };
                    row.Add(remove);
                    trustedUrls.Add(row);
                }
            }

            addButton.clicked += async () =>
            {
                string normalized =
                    TrustedAuthoritiesSettings.NormalizeUrl(newUrl.value);
                if (string.IsNullOrEmpty(normalized))
                {
                    YucpEditorDialog.DisplayDialog(
                        "Invalid URL",
                        "Enter a valid server origin.",
                        "OK");
                    return;
                }
                addButton.SetEnabled(false);
                newUrl.SetEnabled(false);
                addButton.text = "Validating...";
                bool valid;
                try
                {
                    valid = await RefreshTrustedUrlAsync(normalized);
                }
                finally
                {
                    addButton.text = "Add server";
                    addButton.SetEnabled(true);
                    newUrl.SetEnabled(true);
                }
                if (!valid)
                {
                    return;
                }
                TrustedAuthoritiesSettings.AddUrl(normalized);
                newUrl.value = string.Empty;
                RefreshList();
            };

            RefreshList();
        }

        private static async Task<bool> RefreshTrustedUrlAsync(string url)
        {
            AuthorityKeyFetcher.FetchResult result =
                await AuthorityKeyFetcher.FetchKeysFromUrlAsync(url);
            List<AuthorityKeyFetcher.AuthorityKey> pinnedKeys =
                result.success
                    ? TrustedAuthority.FilterToPinnedKeys(result.keys)
                    : new List<AuthorityKeyFetcher.AuthorityKey>();
            if (result.success && pinnedKeys.Count > 0)
            {
                TrustedAuthoritiesSettings.CacheKeys(
                    url,
                    pinnedKeys,
                    result.fetchTime);
                TrustedAuthority.ReloadAllKeys();
                return true;
            }

            string failure = result.success
                ? "The server did not publish a pinned signing root."
                : result.error;
            YucpEditorDialog.DisplayDialog(
                "We couldn’t validate the server",
                failure,
                "OK");
            return false;
        }
    }
}
