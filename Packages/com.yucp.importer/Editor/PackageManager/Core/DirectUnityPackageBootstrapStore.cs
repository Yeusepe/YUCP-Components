using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class DirectUnityPackageBootstrapStore
    {
        private const string PendingRoot =
            ".yucp/pending-bootstrap-intents";

        internal static void Persist(
            string projectPath,
            AliasPackageContract alias,
            string descriptorJson)
        {
            Validate(projectPath, alias);
            if (string.IsNullOrWhiteSpace(descriptorJson))
            {
                throw new InvalidOperationException(
                    "The Unitypackage bootstrap descriptor is empty.");
            }

            string path = DescriptorPath(projectPath, alias);
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            byte[] bytes = new UTF8Encoding(false).GetBytes(descriptorJson);
            if (File.Exists(path))
            {
                byte[] existing = File.ReadAllBytes(path);
                if (!existing.SequenceEqual(bytes))
                {
                    throw new InvalidOperationException(
                        "A different Unitypackage bootstrap already uses " +
                        "this signed intent identity.");
                }
                return;
            }

            string temporaryPath = path + "." +
                Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        internal static IReadOnlyList<string> ReadAll(string projectPath)
        {
            string directory = RootPath(projectPath);
            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }
            return Directory
                .GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText)
                .ToArray();
        }

        internal static void Remove(
            string projectPath,
            AliasPackageContract alias)
        {
            ValidateIdentity(projectPath, alias);
            string path = DescriptorPath(projectPath, alias);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        internal static bool Contains(
            string projectPath,
            AliasPackageContract alias)
        {
            ValidateIdentity(projectPath, alias);
            return File.Exists(DescriptorPath(projectPath, alias));
        }

        private static string DescriptorPath(
            string projectPath,
            AliasPackageContract alias)
        {
            string identity =
                alias.aliasId + "\n" +
                (alias.bootstrapIntent?.intentId ?? string.Empty) + "\n" +
                alias.packageName + "\n" +
                alias.packageVersion;
            string digest;
            using (SHA256 sha256 = SHA256.Create())
            {
                digest = string.Concat(
                    sha256.ComputeHash(Encoding.UTF8.GetBytes(identity))
                        .Select(value => value.ToString("x2")));
            }
            return Path.Combine(RootPath(projectPath), digest + ".json");
        }

        private static string RootPath(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath) ||
                !Path.IsPathRooted(projectPath) ||
                !Directory.Exists(projectPath))
            {
                throw new InvalidOperationException(
                    "The Unitypackage bootstrap project path is invalid.");
            }
            return Path.Combine(
                Path.GetFullPath(projectPath),
                PendingRoot.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
        }

        private static void Validate(
            string projectPath,
            AliasPackageContract alias)
        {
            ValidateIdentity(projectPath, alias);
            if (!alias.directUnityPackageBootstrap)
            {
                throw new InvalidOperationException(
                    "The bootstrap did not originate from a Unitypackage.");
            }
        }

        private static void ValidateIdentity(
            string projectPath,
            AliasPackageContract alias)
        {
            RootPath(projectPath);
            if (alias == null ||
                string.IsNullOrWhiteSpace(alias.aliasId) ||
                string.IsNullOrWhiteSpace(alias.packageName) ||
                string.IsNullOrWhiteSpace(alias.packageVersion))
            {
                throw new InvalidOperationException(
                    "The direct Unitypackage bootstrap identity is invalid.");
            }
        }
    }
}
