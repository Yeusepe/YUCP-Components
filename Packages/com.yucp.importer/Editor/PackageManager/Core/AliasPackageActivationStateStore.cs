using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    [Serializable]
    internal sealed class AliasPackageActivationState
    {
        public string aliasId = string.Empty;
        public string handledOperation = string.Empty;
        public string packageName = string.Empty;
        public string packageVersion = string.Empty;
        public int schemaVersion = 1;
    }

    internal static class AliasPackageActivationStateStore
    {
        private const string StateRoot = ".yucp/alias-activation";

        internal static bool IsHandled(
            string projectPath,
            AliasPackageContract alias)
        {
            Validate(projectPath, alias);
            string path = StatePath(projectPath, alias);
            if (!File.Exists(path))
            {
                return false;
            }
            AliasPackageActivationState state;
            try
            {
                state =
                    JsonConvert.DeserializeObject<
                        AliasPackageActivationState>(
                        File.ReadAllText(path));
            }
            catch (JsonException)
            {
                return false;
            }
            return state != null &&
                state.schemaVersion == 1 &&
                string.Equals(
                    state.aliasId,
                    alias.aliasId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    state.packageName,
                    alias.packageName,
                    StringComparison.Ordinal) &&
                string.Equals(
                    state.packageVersion,
                    alias.packageVersion,
                    StringComparison.Ordinal) &&
                (state.handledOperation == "install" ||
                    state.handledOperation == "update" ||
                    state.handledOperation == "repair" ||
                    state.handledOperation == "rollback" ||
                    state.handledOperation == "recover" ||
                    state.handledOperation == "uninstall");
        }

        internal static void MarkHandled(
            string projectPath,
            AliasPackageContract alias,
            string operation)
        {
            Validate(projectPath, alias);
            if (operation != "install" &&
                operation != "update" &&
                operation != "repair" &&
                operation != "rollback" &&
                operation != "recover" &&
                operation != "uninstall")
            {
                throw new InvalidOperationException(
                    "The alias activation operation is invalid.");
            }

            string path = StatePath(projectPath, alias);
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(
                directory,
                "." + Guid.NewGuid().ToString("N") + ".partial");
            var state = new AliasPackageActivationState
            {
                aliasId = alias.aliasId,
                handledOperation = operation,
                packageName = alias.packageName,
                packageVersion = alias.packageVersion,
            };
            byte[] bytes = new UTF8Encoding(false).GetBytes(
                JsonConvert.SerializeObject(state, Formatting.Indented));
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
                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static string StatePath(
            string projectPath,
            AliasPackageContract alias)
        {
            string key =
                alias.packageName + "\n" +
                alias.packageVersion + "\n" +
                alias.aliasId;
            string digest;
            using (SHA256 sha256 = SHA256.Create())
            {
                digest = BitConverter.ToString(
                        sha256.ComputeHash(Encoding.UTF8.GetBytes(key)))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
            return Path.Combine(
                Path.GetFullPath(projectPath),
                StateRoot.Replace('/', Path.DirectorySeparatorChar),
                digest + ".json");
        }

        private static void Validate(
            string projectPath,
            AliasPackageContract alias)
        {
            if (string.IsNullOrWhiteSpace(projectPath) ||
                !Path.IsPathRooted(projectPath) ||
                !Directory.Exists(projectPath) ||
                alias == null ||
                string.IsNullOrWhiteSpace(alias.aliasId) ||
                string.IsNullOrWhiteSpace(alias.packageName) ||
                string.IsNullOrWhiteSpace(alias.packageVersion))
            {
                throw new InvalidOperationException(
                    "The alias activation state identity is invalid.");
            }
        }
    }
}
