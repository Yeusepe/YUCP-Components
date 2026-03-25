using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using YUCP.Importer.Editor.PackageVerifier.Settings;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    public static class LicenseServerResolver
    {
        private const string PreferredServerUrlKey = "YUCP.PackageManager.PreferredServerUrl";
        private const string LegacyServerUrlKey = "yucp_server_url";

        public static string GetLicenseServerUrl()
        {
            string preferredUrl = TrustedAuthoritiesSettings.NormalizeUrl(EditorPrefs.GetString(PreferredServerUrlKey, string.Empty));
            string signingUrl = TrustedAuthoritiesSettings.NormalizeUrl(GetSigningSettingsServerUrl());
            string legacyUrl = TrustedAuthoritiesSettings.NormalizeUrl(EditorPrefs.GetString(LegacyServerUrlKey, string.Empty));
            List<string> trustedUrls = TrustedAuthoritiesSettings.GetUrls();

            if (trustedUrls.Count == 0)
            {
                return preferredUrl
                    ?? signingUrl
                    ?? legacyUrl
                    ?? TrustedAuthoritiesSettings.DefaultTrustedUrl;
            }

            if (!string.IsNullOrEmpty(preferredUrl) && TrustedAuthoritiesSettings.IsTrustedUrl(preferredUrl))
                return preferredUrl;

            if (!string.IsNullOrEmpty(signingUrl) && TrustedAuthoritiesSettings.IsTrustedUrl(signingUrl))
                return signingUrl;

            if (!string.IsNullOrEmpty(legacyUrl) && TrustedAuthoritiesSettings.IsTrustedUrl(legacyUrl))
                return legacyUrl;

            return trustedUrls[0];
        }

        public static string GetExpectedIssuer()
        {
            string serverUrl = GetLicenseServerUrl();
            return string.IsNullOrEmpty(serverUrl)
                ? null
                : serverUrl.TrimEnd('/') + "/api/auth";
        }

        private static string GetSigningSettingsServerUrl()
        {
            try
            {
                string[] guids = AssetDatabase.FindAssets("t:SigningSettings");
                if (guids.Length == 0)
                    return null;

                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                Type signingSettingsType = null;

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    signingSettingsType = assembly.GetType("YUCP.DevTools.Editor.PackageSigning.Data.SigningSettings");
                    if (signingSettingsType != null)
                        break;
                }

                if (signingSettingsType == null)
                {
                    signingSettingsType = Type.GetType("YUCP.DevTools.Editor.PackageSigning.Data.SigningSettings, Assembly-CSharp-Editor");
                }

                if (signingSettingsType == null)
                    return null;

                var settings = AssetDatabase.LoadAssetAtPath(path, signingSettingsType);
                if (settings == null)
                    return null;

                var field = signingSettingsType.GetField("serverUrl", BindingFlags.Public | BindingFlags.Instance);
                return field?.GetValue(settings) as string;
            }
            catch
            {
                return null;
            }
        }
    }
}
