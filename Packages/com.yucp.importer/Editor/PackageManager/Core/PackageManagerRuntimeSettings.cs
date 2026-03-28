using System;
using System.IO;
using UnityEditor;

namespace YUCP.Importer.Editor.PackageManager
{
    public static class PackageManagerRuntimeSettings
    {
        private const string EnabledKey = "YUCP.Importer.PackageManager.Enabled";
        private const string RuntimeInstallRootKey = "YUCP.PackageManager.RuntimeInstallRoot";
        public static readonly string DefaultRuntimeInstallRoot =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "YUCP", "CouplingRuntime");

        public static bool IsEnabled()
        {
            return EditorPrefs.GetBool(EnabledKey, true);
        }

        public static void SetEnabled(bool enabled)
        {
            EditorPrefs.SetBool(EnabledKey, enabled);
        }

        public static string NormalizeRuntimeInstallRoot(string value)
        {
            string trimmed = value?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return null;

            try
            {
                return Path.GetFullPath(trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            catch
            {
                return null;
            }
        }

        public static string GetRuntimeInstallRootOverride()
        {
            return NormalizeRuntimeInstallRoot(EditorPrefs.GetString(RuntimeInstallRootKey, string.Empty));
        }

        public static void SetRuntimeInstallRootOverride(string value)
        {
            string normalized = NormalizeRuntimeInstallRoot(value);
            EditorPrefs.SetString(RuntimeInstallRootKey, normalized ?? string.Empty);
        }

        public static string ResolveRuntimeInstallRoot()
        {
            string overrideRoot = GetRuntimeInstallRootOverride();
            if (!string.IsNullOrWhiteSpace(overrideRoot))
                return overrideRoot;

            string environmentRoot = NormalizeRuntimeInstallRoot(Environment.GetEnvironmentVariable("YUCP_COUPLING_RUNTIME_ROOT"));
            return !string.IsNullOrWhiteSpace(environmentRoot)
                ? environmentRoot
                : DefaultRuntimeInstallRoot;
        }
    }
}
