using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class ProjectIdentityService
    {
        private const int LockRetryMilliseconds = 25;
        private const int LockTimeoutMilliseconds = 5000;
        private const int SchemaVersion = 2;

        private static string IdentityFilePath(string projectPath) =>
            Path.Combine(
                Path.GetFullPath(projectPath),
                "ProjectSettings",
                "YUCPProjectIdentity.json");

        internal static string GetOrCreateProjectIdentity(
            string projectPath)
        {
            string canonicalProjectPath = Path.GetFullPath(projectPath);
            string identityPath = IdentityFilePath(canonicalProjectPath);
            try
            {
                using (AcquireIdentityLock(canonicalProjectPath))
                {
                    ProjectIdentityFile existing =
                        ReadIdentityFile(identityPath);
                    if (IsValid(existing))
                    {
                        return existing.projectIdentity;
                    }

                    string projectIdentity = CreateProjectIdentity();
                    WriteAtomically(
                        identityPath,
                        JsonConvert.SerializeObject(
                            new ProjectIdentityFile
                            {
                                projectIdentity = projectIdentity,
                            },
                            Formatting.Indented));
                    ProjectIdentityFile persisted =
                        ReadIdentityFile(identityPath);
                    if (!IsValid(persisted))
                    {
                        throw new InvalidDataException(
                            "The persisted project identity is invalid.");
                    }
                    return persisted.projectIdentity;
                }
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Unity could not initialize this project's package identity.",
                    exception);
            }
        }

        private static FileStream AcquireIdentityLock(string projectPath)
        {
            string lockPath = Path.Combine(
                projectPath,
                "Library",
                "YUCP",
                "PackageLifecycle",
                "project-identity.lock");
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath));
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(
                LockTimeoutMilliseconds);
            while (true)
            {
                try
                {
                    return new FileStream(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None);
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(LockRetryMilliseconds);
                }
            }
        }

        private static ProjectIdentityFile ReadIdentityFile(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<ProjectIdentityFile>(
                    File.ReadAllText(path));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool IsValid(ProjectIdentityFile identity)
        {
            return identity != null &&
                identity.schemaVersion == SchemaVersion &&
                IsSha256(identity.projectIdentity);
        }

        internal static string CreateProjectIdentity()
        {
            var bytes = new byte[32];
            using (RandomNumberGenerator random =
                RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }
            return string.Concat(bytes.Select(value => value.ToString("x2")));
        }

        private static void WriteAtomically(string path, string value)
        {
            string directory = Path.GetDirectoryName(path) ??
                throw new InvalidOperationException(
                    "The project identity directory is invalid.");
            Directory.CreateDirectory(directory);
            string temporary = path + "." +
                Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                using (var writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(false)))
                {
                    writer.Write(value);
                    writer.Write(Environment.NewLine);
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static bool IsSha256(string value)
        {
            return value != null &&
                value.Length == 64 &&
                value.All(character =>
                    character >= '0' && character <= '9' ||
                    character >= 'a' && character <= 'f');
        }

        [Serializable]
        private class ProjectIdentityFile
        {
            public int schemaVersion = SchemaVersion;
            public string projectIdentity = string.Empty;
        }
    }
}
