using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Compilation;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class PackageImportVerifier
    {
        internal static void ImportAndVerify(
            string projectPath,
            IReadOnlyList<TransferHelperFile> files)
        {
            string projectRoot = RequireProjectRoot(projectPath);
            RefreshAndRequireSuccessfulCompilation();
            foreach (TransferHelperFile file in
                files ?? Array.Empty<TransferHelperFile>())
            {
                string diskPath = ResolveOwnedFile(
                    projectRoot,
                    file.normalizedPath);
                var info = new FileInfo(diskPath);
                if (!info.Exists ||
                    info.Length != file.bytes ||
                    !string.Equals(
                        Sha256(diskPath),
                        file.sha256,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Unity import changed the owned package file: " +
                        file.normalizedPath);
                }
                if (IsImportableAsset(file.normalizedPath) &&
                    string.IsNullOrWhiteSpace(
                        AssetDatabase.AssetPathToGUID(
                            file.normalizedPath,
                            AssetPathToGUIDOptions.OnlyExistingAssets)))
                {
                    throw new InvalidDataException(
                        "Unity did not register the owned package asset: " +
                        file.normalizedPath);
                }
            }
        }

        internal static void ImportAndVerifyRemoval(
            string projectPath,
            IReadOnlyList<VerifiedStagingFile> removedFiles)
        {
            string projectRoot = RequireProjectRoot(projectPath);
            RefreshAndRequireSuccessfulCompilation();
            foreach (VerifiedStagingFile file in
                removedFiles ?? Array.Empty<VerifiedStagingFile>())
            {
                string diskPath = ResolveOwnedFile(
                    projectRoot,
                    file.normalizedPath);
                if (!File.Exists(diskPath) &&
                    IsImportableAsset(file.normalizedPath) &&
                    !string.IsNullOrWhiteSpace(
                        AssetDatabase.AssetPathToGUID(
                            file.normalizedPath,
                            AssetPathToGUIDOptions.OnlyExistingAssets)))
                {
                    throw new InvalidDataException(
                        "Unity retained a removed package asset.");
                }
            }
        }

        private static void RefreshAndRequireSuccessfulCompilation()
        {
            var compilationErrors = new List<string>();
            void OnAssemblyCompilationFinished(
                string assemblyPath,
                CompilerMessage[] messages)
            {
                compilationErrors.AddRange(
                    messages
                        .Where(message =>
                            message.type == CompilerMessageType.Error)
                        .Select(message =>
                            $"{Path.GetFileName(assemblyPath)}: " +
                            message.message));
            }

            CompilationPipeline.assemblyCompilationFinished +=
                OnAssemblyCompilationFinished;
            try
            {
                AssetDatabase.Refresh(
                    ImportAssetOptions.ForceSynchronousImport);
            }
            finally
            {
                CompilationPipeline.assemblyCompilationFinished -=
                    OnAssemblyCompilationFinished;
            }
            if (EditorApplication.isCompiling ||
                EditorUtility.scriptCompilationFailed ||
                compilationErrors.Count > 0)
            {
                string detail = compilationErrors.Count == 0
                    ? "Unity reported a script compilation failure."
                    : string.Join("\n", compilationErrors);
                throw new InvalidDataException(detail);
            }
        }

        private static string RequireProjectRoot(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !Path.IsPathRooted(value))
            {
                throw new InvalidDataException(
                    "The Unity project path must be absolute.");
            }
            return Path.GetFullPath(value);
        }

        private static string ResolveOwnedFile(
            string projectRoot,
            string normalizedPath)
        {
            if (string.IsNullOrWhiteSpace(normalizedPath) ||
                normalizedPath.Contains("\\") ||
                Path.IsPathRooted(normalizedPath) ||
                normalizedPath.Split('/').Any(segment =>
                    segment.Length == 0 ||
                    segment == "." ||
                    segment == ".."))
            {
                throw new InvalidDataException(
                    "An owned package path is invalid.");
            }
            string resolved = Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    normalizedPath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));
            string boundary = projectRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(
                boundary,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "An owned package path escapes the Unity project.");
            }
            return ToExtendedWindowsPath(resolved);
        }

        internal static string ToExtendedWindowsPath(string path)
        {
            if (Path.DirectorySeparatorChar != '\\' ||
                path.StartsWith(
                    @"\\?\",
                    StringComparison.Ordinal))
            {
                return path;
            }
            if (path.StartsWith(
                @"\\",
                StringComparison.Ordinal))
            {
                return @"\\?\UNC\" + path.Substring(2);
            }
            return @"\\?\" + path;
        }

        private static bool IsImportableAsset(string normalizedPath)
        {
            return normalizedPath.StartsWith(
                    "Assets/",
                    StringComparison.Ordinal) &&
                !normalizedPath.EndsWith(
                    ".meta",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return string.Concat(
                    sha256
                        .ComputeHash(stream)
                        .Select(value => value.ToString("x2")));
            }
        }
    }
}
