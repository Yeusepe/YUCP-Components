using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    /// <summary>
    /// Writes the requirements a release reported into the descriptor the direct
    /// VPM installer already consumes, so repository registration, archive
    /// hashes, manifest writes and the domain reload stay in the one component
    /// that implements them.
    /// </summary>
    internal static class ReleaseDependencyHandoff
    {
        private const string InstalledPackagesFolder = "yucp.installed-packages";
        private const string TempFolder = "_temp";
        private const int MaximumDependencies = 64;
        private const int MaximumRepositories = 16;

        internal static string ResolveWorkspaceName(string value, string fallback)
        {
            string source = string.IsNullOrWhiteSpace(value) ? fallback : value;
            if (string.IsNullOrWhiteSpace(source))
            {
                return "YUCP-Bootstrap";
            }
            var builder = new StringBuilder(source.Length);
            foreach (char character in source.Trim())
            {
                builder.Append(
                    char.IsLetterOrDigit(character) ||
                    character == '.' ||
                    character == '_' ||
                    character == '-'
                        ? character
                        : '-');
            }
            string normalized = builder.ToString().Trim('-', '.');
            return normalized.Length == 0
                ? "YUCP-Bootstrap"
                : normalized.Substring(0, Math.Min(normalized.Length, 80));
        }

        /// <summary>
        /// Returns the descriptor path when one was written, or null when the
        /// release requires nothing beyond what the project already has.
        /// </summary>
        internal static string Write(
            string projectPath,
            AliasPackageContract alias,
            IReadOnlyDictionary<string, string> dependencies,
            IReadOnlyDictionary<string, string> repositories)
        {
            if (string.IsNullOrWhiteSpace(projectPath) ||
                !Directory.Exists(projectPath) ||
                alias == null ||
                dependencies == null ||
                dependencies.Count == 0)
            {
                return null;
            }
            if (dependencies.Count > MaximumDependencies ||
                (repositories?.Count ?? 0) > MaximumRepositories)
            {
                throw new InvalidOperationException(
                    "The release declares more VPM requirements than the installer accepts.");
            }

            string workspace = ResolveWorkspaceName(
                alias.packageDisplayName,
                alias.packageName);
            string directory = Path.Combine(
                Path.GetFullPath(projectPath),
                "Packages",
                InstalledPackagesFolder,
                workspace,
                TempFolder);
            Directory.CreateDirectory(directory);

            string path = Path.Combine(
                directory,
                "YUCP_TempInstall_" + Guid.NewGuid().ToString("N") + ".json");
            var descriptor = new Dictionary<string, object>
            {
                ["name"] = alias.packageName ?? string.Empty,
                ["displayName"] = alias.packageDisplayName ?? alias.packageName ?? string.Empty,
                ["version"] = alias.packageVersion ?? string.Empty,
                ["vpmDependencies"] = dependencies.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase),
            };
            if (repositories != null && repositories.Count > 0)
            {
                descriptor["vpmRepositories"] = repositories.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
            }

            File.WriteAllText(
                path,
                JsonConvert.SerializeObject(descriptor, Formatting.Indented) + "\n",
                new UTF8Encoding(false));
            return path;
        }
    }
}
