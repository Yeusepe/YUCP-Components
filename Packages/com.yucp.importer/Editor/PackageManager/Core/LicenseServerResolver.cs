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
                return TrustedAuthoritiesSettings.DefaultTrustedUrl;
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
                Type packageSigningServiceType = null;

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    packageSigningServiceType = assembly.GetType("YUCP.DevTools.Editor.PackageSigning.Core.PackageSigningService");
                    if (packageSigningServiceType != null)
                        break;
                }

                if (packageSigningServiceType == null)
                {
                    packageSigningServiceType = Type.GetType("YUCP.DevTools.Editor.PackageSigning.Core.PackageSigningService, Assembly-CSharp-Editor");
                }

                if (packageSigningServiceType == null)
                    return null;

                MethodInfo getServerUrlMethod = packageSigningServiceType.GetMethod(
                    "GetServerUrl",
                    BindingFlags.Public | BindingFlags.Static);
                return getServerUrlMethod?.Invoke(null, null) as string;
            }
            catch
            {
                return null;
            }
        }
    }
}
