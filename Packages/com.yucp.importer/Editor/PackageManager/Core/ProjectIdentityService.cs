using System;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class ProjectIdentityService
    {
        private const string SessionKey = "yucp.project.identity";
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
            string sessionKey = SessionKey + "." +
                HashText(canonicalProjectPath);
            string cached = SessionState.GetString(sessionKey, null);
            if (IsSha256(cached))
            {
                return cached;
            }

            string identityPath = IdentityFilePath(canonicalProjectPath);
            try
            {
                if (File.Exists(identityPath))
                {
                    ProjectIdentityFile existing =
                        JsonConvert.DeserializeObject<ProjectIdentityFile>(
                            File.ReadAllText(identityPath));
                    if (existing != null &&
                        existing.schemaVersion == SchemaVersion &&
                        IsSha256(existing.projectIdentity))
                    {
                        SessionState.SetString(
                            sessionKey,
                            existing.projectIdentity);
                        return existing.projectIdentity;
                    }
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
                SessionState.SetString(sessionKey, projectIdentity);
                return projectIdentity;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Unity could not initialize this project's package identity.",
                    exception);
            }
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

        private static string HashText(string value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return string.Concat(
                    sha256.ComputeHash(
                            Encoding.UTF8.GetBytes(value))
                        .Select(item => item.ToString("x2")));
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
