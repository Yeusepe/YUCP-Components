using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal sealed class PackagePreservedCopy
    {
        internal string hiddenPath = string.Empty;
        internal string normalizedPath = string.Empty;
        internal string visibleAssetPath = string.Empty;
    }

    internal static class PackageModifiedAssetPreserver
    {
        private const string HiddenRoot = ".yucp/preserved-changes";
        private const string VisibleRoot =
            "Assets/YUCP Preserved Changes";

        internal static List<PackagePreservedCopy> Preserve(
            string projectPath,
            string aliasId,
            string runId,
            PackageChangePlan plan)
        {
            string projectRoot = Path.GetFullPath(projectPath);
            string safeAlias = SafeSegment(aliasId, "package");
            string safeRun = SafeSegment(runId, "operation");
            List<PackageChangePlanEntry> modified = (plan?.entries ??
                    new List<PackageChangePlanEntry>())
                .Where(entry => entry.RequiresPreservedCopy)
                .ToList();
            var copies = new List<PackagePreservedCopy>();
            if (modified.Count == 0)
            {
                return copies;
            }

            string hiddenRoot = ResolveInside(
                projectRoot,
                $"{HiddenRoot}/{safeAlias}/{safeRun}");
            var visibleSources = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (PackageChangePlanEntry entry in modified)
            {
                string source = ResolveInside(
                    projectRoot,
                    entry.normalizedPath);
                if (!File.Exists(source))
                {
                    throw new FileNotFoundException(
                        "A modified package file disappeared before it " +
                        "could be preserved.",
                        source);
                }
                CopyHidden(
                    projectRoot,
                    hiddenRoot,
                    entry.normalizedPath,
                    copies);
                if (entry.normalizedPath.EndsWith(
                        ".meta",
                        StringComparison.OrdinalIgnoreCase))
                {
                    visibleSources.Add(entry.normalizedPath.Substring(
                        0,
                        entry.normalizedPath.Length - ".meta".Length));
                }
                else
                {
                    visibleSources.Add(entry.normalizedPath);
                    string metaPath = entry.normalizedPath + ".meta";
                    if (File.Exists(ResolveInside(projectRoot, metaPath)))
                    {
                        CopyHidden(
                            projectRoot,
                            hiddenRoot,
                            metaPath,
                            copies);
                    }
                }
            }

            string visibleDirectory =
                $"{VisibleRoot}/{safeAlias}/{safeRun}";
            Directory.CreateDirectory(ResolveInside(
                projectRoot,
                visibleDirectory));
            foreach (string sourceAssetPath in visibleSources)
            {
                string candidate =
                    $"{visibleDirectory}/" +
                    VisibleRelativePath(sourceAssetPath);
                string candidateDirectory =
                    Path.GetDirectoryName(candidate)
                        ?.Replace('\\', '/');
                Directory.CreateDirectory(ResolveInside(
                    projectRoot,
                    candidateDirectory));
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ProjectTransactionJournal.RunAssetEditingTransaction(
                AssetDatabase.StartAssetEditing,
                () =>
                {
                    foreach (string sourceAssetPath in visibleSources)
                    {
                        if (!File.Exists(ResolveInside(
                                projectRoot,
                                sourceAssetPath)))
                        {
                            continue;
                        }
                        string relative = VisibleRelativePath(
                            sourceAssetPath);
                        string candidate =
                            $"{visibleDirectory}/{relative}";
                        string destination =
                            AssetDatabase.GenerateUniqueAssetPath(
                                candidate);
                        if (!AssetDatabase.CopyAsset(
                                sourceAssetPath,
                                destination))
                        {
                            throw new IOException(
                                "Unity couldn’t create a copy of the existing file " +
                                $"copy for '{sourceAssetPath}'.");
                        }
                        PackagePreservedCopy copy = copies.FirstOrDefault(
                            item => string.Equals(
                                item.normalizedPath,
                                sourceAssetPath,
                                StringComparison.OrdinalIgnoreCase));
                        if (copy != null)
                        {
                            copy.visibleAssetPath = destination;
                        }
                    }
                },
                AssetDatabase.StopAssetEditing);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return copies;
        }

        private static void CopyHidden(
            string projectRoot,
            string hiddenRoot,
            string normalizedPath,
            List<PackagePreservedCopy> copies)
        {
            string source = ResolveInside(projectRoot, normalizedPath);
            string destination = Path.GetFullPath(Path.Combine(
                hiddenRoot,
                normalizedPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));
            string destinationDirectory = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(destinationDirectory);
            File.Copy(source, destination, overwrite: false);
            copies.Add(new PackagePreservedCopy
            {
                hiddenPath = destination,
                normalizedPath = normalizedPath,
            });
        }

        private static string VisibleRelativePath(string normalizedPath)
        {
            if (normalizedPath.StartsWith(
                    "Assets/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return normalizedPath.Substring("Assets/".Length);
            }
            if (normalizedPath.StartsWith(
                    "Packages/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Packages/" +
                    normalizedPath.Substring("Packages/".Length);
            }
            return "Project/" + normalizedPath;
        }

        private static string SafeSegment(
            string value,
            string fallback)
        {
            string safe = new string((value ?? string.Empty)
                .Select(character =>
                    char.IsLetterOrDigit(character) ||
                    character == '.' ||
                    character == '-' ||
                    character == '_'
                        ? character
                        : '_')
                .ToArray()).Trim('_');
            return string.IsNullOrWhiteSpace(safe)
                ? fallback
                : safe;
        }

        private static string ResolveInside(
            string projectRoot,
            string normalizedPath)
        {
            string path = Path.GetFullPath(Path.Combine(
                projectRoot,
                normalizedPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));
            string prefix = projectRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!path.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The preserved-change path escapes the project.");
            }
            return path;
        }
    }
}
