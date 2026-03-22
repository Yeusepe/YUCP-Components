using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Importer.Editor.PackageManager
{
    internal sealed class PackageManagerSettingsProvider : SettingsProvider
    {
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
                    "Interception"
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

            var description = new Label("Controls importer-owned package interception and the YUCP package manager window.");
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

            var help = new HelpBox(
                "Enabled by default. When disabled, importer interception is skipped and the package manager window will not open.",
                HelpBoxMessageType.Info);
            help.style.marginTop = 8;
            rootElement.Add(help);
        }
    }
}
