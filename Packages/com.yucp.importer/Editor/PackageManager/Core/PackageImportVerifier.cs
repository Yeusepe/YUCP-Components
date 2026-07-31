using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class PackageImportVerifier
    {
        private const int UnityWindowsMaximumPathCharacters = 260;
        private const string DeactivatedSuffix = ".yucp_disabled";
        private const string MetaSuffix = ".meta";

        /// <summary>
        /// Deactivated assets are renamed to their real extension by the package
        /// guardian's resolver on import, so an owned file recorded at its
        /// deactivated path is equally valid at its activated one. Null when the
        /// path was not deactivated to begin with.
        /// </summary>
        internal static string ActivatedOwnedPath(string normalizedPath)
        {
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return null;
            }
            if (normalizedPath.EndsWith(
                    DeactivatedSuffix + MetaSuffix,
                    StringComparison.Ordinal))
            {
                return normalizedPath.Remove(
                    normalizedPath.Length -
                        DeactivatedSuffix.Length -
                        MetaSuffix.Length,
                    DeactivatedSuffix.Length);
            }
            return normalizedPath.EndsWith(
                    DeactivatedSuffix,
                    StringComparison.Ordinal)
                ? normalizedPath.Substring(
                    0,
                    normalizedPath.Length - DeactivatedSuffix.Length)
                : null;
        }

        internal static void ValidateUnityPathCompatibility(
            string projectPath,
            IReadOnlyList<NativePackageBrokerFile> files,
            bool windowsPathLimitApplies)
        {
            if (!windowsPathLimitApplies)
            {
                return;
            }
            string projectRoot = RequireProjectRoot(projectPath);
            foreach (NativePackageBrokerFile file in
                files ?? Array.Empty<NativePackageBrokerFile>())
            {
                string diskPath = ResolveOwnedFilePath(
                    projectRoot,
                    file.normalizedPath);
                if (diskPath.Length >= UnityWindowsMaximumPathCharacters)
                {
                    throw new InvalidDataException(
                        "The Unity project path is too long for this package on Windows. " +
                        "Move the project to a shorter folder. Unsupported package path: " +
                        file.normalizedPath);
                }
            }
        }

        internal static async Task ImportAndVerify(
            string projectPath,
            IReadOnlyList<NativePackageBrokerFile> files)
        {
            string projectRoot = RequireProjectRoot(projectPath);
            await RefreshAndRequireSuccessfulCompilation();
            foreach (NativePackageBrokerFile file in
                files ?? Array.Empty<NativePackageBrokerFile>())
            {
                string ownedPath = file.normalizedPath;
                string diskPath = ResolveOwnedFile(projectRoot, ownedPath);
                if (!File.Exists(diskPath))
                {
                    string activatedPath = ActivatedOwnedPath(ownedPath);
                    if (activatedPath != null)
                    {
                        ownedPath = activatedPath;
                        diskPath = ResolveOwnedFile(projectRoot, ownedPath);
                    }
                }
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
                if (IsImportableAsset(ownedPath) &&
                    string.IsNullOrWhiteSpace(
                        AssetDatabase.AssetPathToGUID(
                            ownedPath,
                            AssetPathToGUIDOptions.OnlyExistingAssets)))
                {
                    throw new InvalidDataException(
                        "Unity did not register the owned package asset: " +
                        ownedPath);
                }
            }
        }

        internal static async Task ImportAndVerifyRemoval(
            string projectPath,
            IReadOnlyList<VerifiedStagingFile> removedFiles)
        {
            await ImportAndVerifyRemoval(
                projectPath,
                removedFiles,
                Array.Empty<VerifiedStagingFile>());
        }

        internal static async Task ImportAndVerifyRemoval(
            string projectPath,
            IReadOnlyList<VerifiedStagingFile> removedFiles,
            IReadOnlyList<VerifiedStagingFile> preservedFiles)
        {
            string projectRoot = RequireProjectRoot(projectPath);
            await RefreshAndRequireSuccessfulCompilation();
            foreach (VerifiedStagingFile file in
                removedFiles ?? Array.Empty<VerifiedStagingFile>())
            {
                foreach (string ownedPath in new[]
                {
                    file.normalizedPath,
                    ActivatedOwnedPath(file.normalizedPath),
                })
                {
                    if (ownedPath == null)
                    {
                        continue;
                    }
                    if (File.Exists(ResolveOwnedFile(projectRoot, ownedPath)))
                    {
                        throw new InvalidDataException(
                            "Unity retained the removed package file: " +
                            ownedPath);
                    }
                    if (IsImportableAsset(ownedPath) &&
                        !string.IsNullOrWhiteSpace(
                            AssetDatabase.AssetPathToGUID(
                                ownedPath,
                                AssetPathToGUIDOptions.OnlyExistingAssets)))
                    {
                        throw new InvalidDataException(
                            "Unity retained the removed package asset: " +
                            ownedPath);
                    }
                }
            }
            // Modified owned files are removed from their package paths too.
            // Their byte-for-byte journal snapshot and user-visible copy are
            // verified before this import verification boundary.
        }

        private static async Task RefreshAndRequireSuccessfulCompilation()
        {
            const int compilationTimeoutMilliseconds = 120000;
            var compilationErrors = new List<string>();
            var compilationCompleted = new TaskCompletionSource<bool>();
            bool compilationStarted = false;
            void OnCompilationStarted(object _)
            {
                compilationStarted = true;
            }
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
            void OnCompilationFinished(object _)
            {
                compilationCompleted.TrySetResult(true);
            }

            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished +=
                OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            EditorApplication.LockReloadAssemblies();
            try
            {
                AssetDatabase.Refresh(
                    ImportAssetOptions.ForceSynchronousImport);
                await NextEditorUpdate();
                if (compilationStarted || EditorApplication.isCompiling)
                {
                    using (var timeoutCancellation =
                        new CancellationTokenSource())
                    {
                        Task timeout = Task.Delay(
                            compilationTimeoutMilliseconds,
                            timeoutCancellation.Token);
                        Task completed = await Task.WhenAny(
                            compilationCompleted.Task,
                            timeout);
                        if (completed != compilationCompleted.Task)
                        {
                            throw new TimeoutException(
                                "Unity script compilation did not finish in time.");
                        }
                        timeoutCancellation.Cancel();
                        await compilationCompleted.Task;
                    }
                }
                if (EditorUtility.scriptCompilationFailed ||
                    compilationErrors.Count > 0)
                {
                    string detail = compilationErrors.Count == 0
                        ? "Unity reported a script compilation failure."
                        : string.Join("\n", compilationErrors);
                    throw new InvalidDataException(detail);
                }
            }
            finally
            {
                try
                {
                    CompilationPipeline.compilationStarted -=
                        OnCompilationStarted;
                    CompilationPipeline.assemblyCompilationFinished -=
                        OnAssemblyCompilationFinished;
                    CompilationPipeline.compilationFinished -=
                        OnCompilationFinished;
                }
                finally
                {
                    EditorApplication.UnlockReloadAssemblies();
                }
            }
        }

        private static Task NextEditorUpdate()
        {
            var completion = new TaskCompletionSource<bool>();
            void Complete()
            {
                EditorApplication.delayCall -= Complete;
                completion.TrySetResult(true);
            }
            EditorApplication.delayCall += Complete;
            return completion.Task;
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
            return ToExtendedWindowsPath(
                ResolveOwnedFilePath(projectRoot, normalizedPath));
        }

        private static string ResolveOwnedFilePath(
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
            return resolved;
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
