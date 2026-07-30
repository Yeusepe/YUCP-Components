using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class PackageChangePlanBuilder
    {
        internal static PackageChangePlan Build(
            string projectPath,
            string installedReleaseRoot,
            string requestedReleaseRoot,
            string requestedVersionId,
            IEnumerable<NativePackageBrokerFile> targetFiles,
            IEnumerable<NativePackageBrokerFile> priorFiles)
        {
            string projectRoot = Path.GetFullPath(projectPath);
            var target = Normalize(targetFiles);
            var prior = Normalize(priorFiles);
            var targetByPath = target.ToDictionary(
                file => file.normalizedPath,
                StringComparer.OrdinalIgnoreCase);
            var priorByPath = prior.ToDictionary(
                file => file.normalizedPath,
                StringComparer.OrdinalIgnoreCase);
            var allPaths = new HashSet<string>(
                targetByPath.Keys,
                StringComparer.OrdinalIgnoreCase);
            allPaths.UnionWith(priorByPath.Keys);

            var plan = new PackageChangePlan
            {
                installedReleaseRoot = installedReleaseRoot ?? string.Empty,
                requestedReleaseRoot = requestedReleaseRoot ?? string.Empty,
                requestedVersionId = requestedVersionId ?? string.Empty,
                targetInventoryDigest = ComputeInventoryDigest(target),
            };
            foreach (string normalizedPath in allPaths.OrderBy(
                path => path,
                StringComparer.Ordinal))
            {
                targetByPath.TryGetValue(
                    normalizedPath,
                    out NativePackageBrokerFile targetFile);
                priorByPath.TryGetValue(
                    normalizedPath,
                    out NativePackageBrokerFile priorFile);
                string livePath = ResolveInside(
                    projectRoot,
                    normalizedPath);
                string observedSha256 = File.Exists(livePath)
                    ? Sha256(livePath)
                    : string.Empty;
                string changeKind = Classify(
                    targetFile,
                    priorFile,
                    observedSha256);
                plan.entries.Add(new PackageChangePlanEntry
                {
                    changeKind = changeKind,
                    normalizedPath = normalizedPath,
                    observedSha256 = observedSha256,
                    priorSha256 = priorFile?.sha256 ?? string.Empty,
                    targetBytes = targetFile?.bytes ?? 0,
                    targetSha256 = targetFile?.sha256 ?? string.Empty,
                });
            }
            plan.reviewDigest = ComputeReviewDigest(plan);
            PackageChangePlanSigner.Sign(plan);
            return plan;
        }

        internal static List<string> FindDirtyAffectedAssets(
            PackageChangePlan plan)
        {
            var dirty = new List<string>();
            foreach (PackageChangePlanEntry entry in plan?.entries ??
                Enumerable.Empty<PackageChangePlanEntry>())
            {
                if (!entry.normalizedPath.StartsWith(
                        "Assets/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string assetPath = entry.normalizedPath.EndsWith(
                    ".meta",
                    StringComparison.OrdinalIgnoreCase)
                    ? entry.normalizedPath.Substring(
                        0,
                        entry.normalizedPath.Length - ".meta".Length)
                    : entry.normalizedPath;
                if (assetPath.EndsWith(
                        ".unity",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var scene = EditorSceneManager.GetSceneByPath(assetPath);
                    if (scene.IsValid() && scene.isDirty)
                    {
                        dirty.Add(assetPath);
                    }
                }
                foreach (UnityEngine.Object asset in
                    AssetDatabase.LoadAllAssetsAtPath(assetPath))
                {
                    if (asset != null && EditorUtility.IsDirty(asset))
                    {
                        dirty.Add(assetPath);
                        break;
                    }
                }
            }
            return dirty
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
        }

        private static string Classify(
            NativePackageBrokerFile target,
            NativePackageBrokerFile prior,
            string observedSha256)
        {
            bool liveExists = !string.IsNullOrEmpty(observedSha256);
            if (prior == null)
            {
                return liveExists
                    ? PackageChangeKind.BlockedCollision
                    : PackageChangeKind.Added;
            }
            bool locallyModified = liveExists &&
                !string.Equals(
                    observedSha256,
                    prior.sha256,
                    StringComparison.Ordinal);
            if (target == null)
            {
                return locallyModified
                    ? PackageChangeKind.RemovedWithLocalModifications
                    : PackageChangeKind.Removed;
            }
            if (!liveExists)
            {
                return PackageChangeKind.Added;
            }
            return locallyModified
                ? PackageChangeKind.ReplacedWithLocalModifications
                : PackageChangeKind.ReplacedUnchanged;
        }

        private static List<NativePackageBrokerFile> Normalize(
            IEnumerable<NativePackageBrokerFile> files)
        {
            return (files ?? Enumerable.Empty<NativePackageBrokerFile>())
                .Where(file => file != null)
                .OrderBy(file => file.normalizedPath, StringComparer.Ordinal)
                .ToList();
        }

        private static string ComputeInventoryDigest(
            IEnumerable<NativePackageBrokerFile> files)
        {
            var canonical = new StringBuilder();
            foreach (NativePackageBrokerFile file in files)
            {
                canonical.Append(file.normalizedPath);
                canonical.Append('\0');
                canonical.Append(file.bytes);
                canonical.Append('\0');
                canonical.Append(file.sha256);
                canonical.Append('\n');
            }
            using (SHA256 sha256 = SHA256.Create())
            {
                return string.Concat(
                    sha256.ComputeHash(
                            Encoding.UTF8.GetBytes(canonical.ToString()))
                        .Select(value => value.ToString("x2")));
            }
        }

        internal static string ComputeReviewDigest(PackageChangePlan plan)
        {
            var canonical = new StringBuilder();
            canonical.Append(plan.installedReleaseRoot);
            canonical.Append('\n');
            canonical.Append(plan.requestedReleaseRoot);
            canonical.Append('\n');
            canonical.Append(plan.requestedVersionId);
            canonical.Append('\n');
            canonical.Append(plan.targetInventoryDigest);
            canonical.Append('\n');
            foreach (PackageChangePlanEntry entry in plan.entries)
            {
                canonical.Append(entry.changeKind);
                canonical.Append('\0');
                canonical.Append(entry.normalizedPath);
                canonical.Append('\0');
                canonical.Append(entry.observedSha256);
                canonical.Append('\0');
                canonical.Append(entry.priorSha256);
                canonical.Append('\0');
                canonical.Append(entry.targetBytes);
                canonical.Append('\0');
                canonical.Append(entry.targetSha256);
                canonical.Append('\n');
            }
            using (SHA256 sha256 = SHA256.Create())
            {
                return string.Concat(
                    sha256.ComputeHash(
                            Encoding.UTF8.GetBytes(canonical.ToString()))
                        .Select(value => value.ToString("x2")));
            }
        }

        private static string ResolveInside(
            string projectRoot,
            string normalizedPath)
        {
            string path = Path.GetFullPath(Path.Combine(
                projectRoot,
                (normalizedPath ?? string.Empty).Replace(
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
                    "The package change-plan path escapes the project.");
            }
            return path;
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return string.Concat(
                    sha256.ComputeHash(stream)
                        .Select(value => value.ToString("x2")));
            }
        }
    }
}
