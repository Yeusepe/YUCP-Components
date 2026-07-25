using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    [Serializable]
    internal sealed class PackageLifecycleCheckpoint
    {
        public int schemaVersion = 1;
        public string activeContentDigest = string.Empty;
        public string activePolicyVersion = string.Empty;
        public string aliasId = string.Empty;
        public string errorMessage = string.Empty;
        public string expectedCurrentReleaseRoot = string.Empty;
        public string operation = string.Empty;
        public string phase = "prepared";
        public PackageDeliveryInstallState priorState;
        public string runId = string.Empty;
        public PackageDeliveryInstallState targetState;
    }

    internal static class PackageLifecycleCheckpointStore
    {
        private const int MaximumCheckpointBytes = 16 * 1024 * 1024;

        internal static void Write(
            string projectPath,
            PackageLifecycleCheckpoint checkpoint)
        {
            Validate(checkpoint);
            string path = ResolvePath(
                projectPath,
                checkpoint.runId);
            string temporaryPath = path + ".partial";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            byte[] encoded = new UTF8Encoding(false).GetBytes(
                JsonConvert.SerializeObject(checkpoint, Formatting.Indented));
            if (encoded.Length > MaximumCheckpointBytes)
            {
                throw new InvalidDataException(
                    "The package lifecycle checkpoint is too large.");
            }
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(encoded, 0, encoded.Length);
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

        internal static PackageLifecycleCheckpoint Read(
            string projectPath,
            string runId)
        {
            string path = ResolvePath(projectPath, runId);
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                throw new FileNotFoundException(
                    "The package lifecycle checkpoint does not exist.",
                    path);
            }
            if (info.Length <= 0 || info.Length > MaximumCheckpointBytes)
            {
                throw new InvalidDataException(
                    "The package lifecycle checkpoint size is invalid.");
            }
            PackageLifecycleCheckpoint checkpoint;
            try
            {
                checkpoint =
                    JsonConvert.DeserializeObject<PackageLifecycleCheckpoint>(
                        File.ReadAllText(path, Encoding.UTF8));
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "The package lifecycle checkpoint is not valid JSON.",
                    exception);
            }
            Validate(checkpoint);
            return checkpoint;
        }

        internal static bool TryRead(
            string projectPath,
            string runId,
            out PackageLifecycleCheckpoint checkpoint)
        {
            checkpoint = null;
            if (!File.Exists(ResolvePath(projectPath, runId)))
            {
                return false;
            }
            checkpoint = Read(projectPath, runId);
            return true;
        }

        internal static void ValidateBinding(
            PackageLifecycleCheckpoint checkpoint,
            string aliasId,
            string operation,
            string expectedCurrentReleaseRoot)
        {
            Validate(checkpoint);
            if (!string.Equals(
                    checkpoint.aliasId,
                    aliasId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    checkpoint.operation,
                    operation,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    checkpoint.expectedCurrentReleaseRoot,
                    expectedCurrentReleaseRoot,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The package lifecycle checkpoint binding is invalid.");
            }
        }

        private static void Validate(PackageLifecycleCheckpoint checkpoint)
        {
            if (checkpoint == null ||
                checkpoint.schemaVersion != 1 ||
                !IsSafeIdentifier(checkpoint.runId) ||
                !IsSafeIdentifier(checkpoint.aliasId) ||
                !IsSha256(checkpoint.expectedCurrentReleaseRoot) ||
                !new[]
                {
                    "preflight",
                    "install",
                    "update",
                    "repair",
                    "rollback",
                    "uninstall",
                    "recover",
                }.Contains(checkpoint.operation) ||
                !new[]
                {
                    "prepared",
                    "committed",
                    "rolling-back",
                    "rolled-back",
                    "verified",
                }.Contains(checkpoint.phase))
            {
                throw new InvalidDataException(
                    "The package lifecycle checkpoint is invalid.");
            }
        }

        private static string ResolvePath(string projectPath, string runId)
        {
            if (string.IsNullOrWhiteSpace(projectPath) ||
                !Path.IsPathRooted(projectPath) ||
                !IsSafeIdentifier(runId))
            {
                throw new InvalidDataException(
                    "The package lifecycle checkpoint path is invalid.");
            }
            string projectRoot = Path.GetFullPath(projectPath);
            string path = Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    ".yucp",
                    "transactions",
                    runId,
                    "lifecycle.json"));
            string boundary = projectRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!path.StartsWith(
                boundary,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The package lifecycle checkpoint escaped the project.");
            }
            return path;
        }

        private static bool IsSafeIdentifier(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                value.Length <= 128 &&
                value.All(character =>
                    char.IsLetterOrDigit(character) ||
                    character == '.' ||
                    character == '_' ||
                    character == '-');
        }

        private static bool IsSha256(string value)
        {
            return value != null &&
                value.Length == 64 &&
                value.All(character =>
                    character >= '0' && character <= '9' ||
                    character >= 'a' && character <= 'f');
        }
    }
}
