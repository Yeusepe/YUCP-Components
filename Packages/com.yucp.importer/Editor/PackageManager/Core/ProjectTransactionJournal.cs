using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    [Serializable]
    internal sealed class VerifiedStagingFile
    {
        public long bytes;
        public string normalizedPath = string.Empty;
        public string sha256 = string.Empty;
    }

    [Serializable]
    internal sealed class ProjectTransactionResult
    {
        public string journalPath = string.Empty;
        public string state = string.Empty;
    }

    internal sealed class ProjectTransactionInspection
    {
        public string journalPath = string.Empty;
        public bool requiresPackageResolution;
        public string state = string.Empty;
    }

    [Serializable]
    internal sealed class ProjectTransactionDocument
    {
        public int schemaVersion = 3;
        public string runId = string.Empty;
        public string projectPath = string.Empty;
        public string stagingPath = string.Empty;
        public string state = "prepared";
        public List<ProjectTransactionEntry> entries = new List<ProjectTransactionEntry>();
    }

    [Serializable]
    internal sealed class ProjectTransactionEntry
    {
        public string backupPath = string.Empty;
        public string expectedPriorSha256 = string.Empty;
        public bool hadPriorFile;
        public string normalizedPath = string.Empty;
        public string operation = "write";
        public string state = "pending";
        public string targetSha256 = string.Empty;
    }

    internal static class ProjectTransactionJournal
    {
        private const int SchemaVersion = 3;
        private const string TransactionRoot = ".yucp/transactions";

        internal static ProjectTransactionResult Apply(
            string projectPath,
            string stagingPath,
            string runId,
            IEnumerable<VerifiedStagingFile> files,
            IEnumerable<VerifiedStagingFile> previousFiles = null)
        {
            string projectRoot = RequireAbsoluteDirectory(projectPath, "project");
            using (AcquireProjectLock(projectRoot, runId))
            {
                Prepare(projectRoot, stagingPath, runId, files, previousFiles);
                return RecoverWithoutLock(projectRoot, runId);
            }
        }

        internal static ProjectTransactionResult Prepare(
            string projectPath,
            string stagingPath,
            string runId,
            IEnumerable<VerifiedStagingFile> files,
            IEnumerable<VerifiedStagingFile> previousFiles = null)
        {
            string projectRoot = RequireAbsoluteDirectory(projectPath, "project");
            string stagingRoot = RequireAbsoluteDirectory(stagingPath, "staging");
            ValidateSeparateRoots(projectRoot, stagingRoot);
            ValidateRunId(runId);

            List<VerifiedStagingFile> verifiedFiles = NormalizeFileRecords(files, false);
            var targetPaths = new HashSet<string>(
                verifiedFiles.Select(file => file.normalizedPath),
                StringComparer.OrdinalIgnoreCase);
            foreach (VerifiedStagingFile file in verifiedFiles)
            {
                VerifyStagingFile(stagingRoot, file);
            }
            List<VerifiedStagingFile> priorFiles =
                NormalizeFileRecords(previousFiles, true);

            string journalPath = JournalPath(projectRoot, runId);
            if (File.Exists(IoPath(journalPath)))
            {
                throw new InvalidOperationException(
                    "The project transaction identifier already exists.");
            }
            string backupRoot = Path.Combine(
                Path.GetDirectoryName(journalPath),
                "backups");
            Directory.CreateDirectory(IoPath(backupRoot));
            var document = new ProjectTransactionDocument
            {
                runId = runId,
                projectPath = projectRoot,
                stagingPath = stagingRoot,
            };
            foreach (VerifiedStagingFile file in verifiedFiles)
            {
                string livePath = ResolveInside(projectRoot, file.normalizedPath);
                document.entries.Add(new ProjectTransactionEntry
                {
                    backupPath = ResolveInside(backupRoot, file.normalizedPath),
                    hadPriorFile = File.Exists(IoPath(livePath)),
                    normalizedPath = file.normalizedPath,
                    operation = "write",
                    targetSha256 = file.sha256,
                });
            }
            foreach (VerifiedStagingFile file in priorFiles)
            {
                if (targetPaths.Contains(file.normalizedPath))
                {
                    continue;
                }
                string livePath = ResolveInside(projectRoot, file.normalizedPath);
                document.entries.Add(new ProjectTransactionEntry
                {
                    backupPath = ResolveInside(backupRoot, file.normalizedPath),
                    expectedPriorSha256 = file.sha256,
                    hadPriorFile = File.Exists(IoPath(livePath)),
                    normalizedPath = file.normalizedPath,
                    operation = "delete",
                });
            }
            if (document.entries.Count == 0)
            {
                throw new InvalidOperationException(
                    "The project transaction contains no file operations.");
            }
            document.entries = document.entries
                .OrderBy(
                    entry => entry.normalizedPath.StartsWith(
                        ".yucp",
                        StringComparison.Ordinal) ? 1 : 0)
                .ThenBy(entry => entry.normalizedPath, StringComparer.Ordinal)
                .ToList();
            WriteJournal(journalPath, document);
            return Result(journalPath, document.state);
        }

        internal static ProjectTransactionResult Recover(
            string projectPath,
            string runId)
        {
            string projectRoot = RequireAbsoluteDirectory(projectPath, "project");
            ValidateRunId(runId);
            using (AcquireProjectLock(projectRoot, runId))
            {
                return RecoverWithoutLock(projectRoot, runId);
            }
        }

        internal static ProjectTransactionInspection Inspect(
            string projectPath,
            string runId)
        {
            string projectRoot = RequireAbsoluteDirectory(projectPath, "project");
            ValidateRunId(runId);
            string journalPath = JournalPath(projectRoot, runId);
            ProjectTransactionDocument document = ReadJournal(journalPath);
            ValidateJournal(projectRoot, runId, document);
            return new ProjectTransactionInspection
            {
                journalPath = journalPath,
                requiresPackageResolution = document.entries.Any(entry =>
                    EmbeddedPackageResolver.IsEmbeddedPackageDescriptorPath(
                        entry.normalizedPath)),
                state = document.state,
            };
        }

        internal static bool TryInspect(
            string projectPath,
            string runId,
            out ProjectTransactionInspection inspection)
        {
            inspection = null;
            string projectRoot = RequireAbsoluteDirectory(projectPath, "project");
            ValidateRunId(runId);
            if (!File.Exists(IoPath(JournalPath(projectRoot, runId))))
            {
                return false;
            }
            inspection = Inspect(projectRoot, runId);
            return true;
        }

        internal static ProjectTransactionResult RollBackCommitted(
            string projectPath,
            string runId)
        {
            string projectRoot = RequireAbsoluteDirectory(
                projectPath,
                "project");
            ValidateRunId(runId);
            using (AcquireProjectLock(projectRoot, runId))
            {
                string journalPath = JournalPath(projectRoot, runId);
                ProjectTransactionDocument document =
                    ReadJournal(journalPath);
                ValidateJournal(projectRoot, runId, document);
                if (string.Equals(
                    document.state,
                    "rolled-back",
                    StringComparison.Ordinal))
                {
                    return Result(journalPath, document.state);
                }
                if (!string.Equals(
                    document.state,
                    "committed",
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Only a committed project transaction can roll back.");
                }
                RunUnityAssetEditingTransaction(
                    () => RollBack(
                        projectRoot,
                        document,
                        journalPath));
                return Result(journalPath, document.state);
            }
        }

        private static ProjectTransactionResult RecoverWithoutLock(
            string projectRoot,
            string runId)
        {
            string journalPath = JournalPath(projectRoot, runId);
            ProjectTransactionDocument document = ReadJournal(journalPath);
            ValidateJournal(projectRoot, runId, document);
            if (string.Equals(document.state, "committed", StringComparison.Ordinal))
            {
                return Result(journalPath, document.state);
            }

            try
            {
                RunUnityAssetEditingTransaction(() =>
                {
                    foreach (ProjectTransactionEntry entry in document.entries)
                    {
                        ContinueEntry(
                            projectRoot,
                            document,
                            entry,
                            journalPath);
                    }
                    document.state = "committed";
                    WriteJournal(journalPath, document);
                });
                return Result(journalPath, document.state);
            }
            catch
            {
                RunUnityAssetEditingTransaction(
                    () => RollBack(
                        projectRoot,
                        document,
                        journalPath));
                throw;
            }
        }

        internal static void RunAssetEditingTransaction(
            Action begin,
            Action operation,
            Action end)
        {
            if (begin == null || operation == null || end == null)
            {
                throw new ArgumentNullException(
                    "The asset editing transaction actions are required.");
            }
            begin();
            try
            {
                operation();
            }
            finally
            {
                end();
            }
        }

        private static void RunUnityAssetEditingTransaction(
            Action operation)
        {
            RunAssetEditingTransaction(
                AssetDatabase.StartAssetEditing,
                operation,
                AssetDatabase.StopAssetEditing);
        }

        internal static ProjectTransactionResult RemoveOwnedFiles(
            string projectPath,
            string runId,
            IEnumerable<VerifiedStagingFile> files)
        {
            string projectRoot = RequireAbsoluteDirectory(projectPath, "project");
            ValidateRunId(runId);
            using (AcquireProjectLock(projectRoot, runId))
            {
                List<VerifiedStagingFile> ownedFiles =
                    NormalizeFileRecords(files, false);
                string journalPath = JournalPath(projectRoot, runId);
                if (File.Exists(IoPath(journalPath)))
                {
                    throw new InvalidOperationException(
                        "The project transaction identifier already exists.");
                }
                string backupRoot = Path.Combine(
                    Path.GetDirectoryName(journalPath),
                    "backups");
                Directory.CreateDirectory(IoPath(backupRoot));
                var document = new ProjectTransactionDocument
                {
                    runId = runId,
                    projectPath = projectRoot,
                    entries = ownedFiles
                        .Select(file => new ProjectTransactionEntry
                        {
                            backupPath = ResolveInside(
                                backupRoot,
                                file.normalizedPath),
                            expectedPriorSha256 = file.sha256,
                            hadPriorFile = File.Exists(IoPath(
                                ResolveInside(projectRoot, file.normalizedPath))),
                            normalizedPath = file.normalizedPath,
                            operation = "delete",
                        })
                        .OrderBy(
                            entry => entry.normalizedPath.StartsWith(
                                ".yucp",
                                StringComparison.Ordinal) ? 1 : 0)
                        .ThenBy(entry => entry.normalizedPath, StringComparer.Ordinal)
                        .ToList(),
                };
                WriteJournal(journalPath, document);
                return RecoverWithoutLock(projectRoot, runId);
            }
        }

        private static void ContinueEntry(
            string projectRoot,
            ProjectTransactionDocument document,
            ProjectTransactionEntry entry,
            string journalPath)
        {
            if (string.Equals(entry.state, "committed", StringComparison.Ordinal) ||
                string.Equals(entry.state, "preserved-modified", StringComparison.Ordinal) ||
                string.Equals(entry.state, "skipped-missing", StringComparison.Ordinal))
            {
                return;
            }
            ValidateEntry(projectRoot, document, entry);
            if (string.Equals(entry.operation, "delete", StringComparison.Ordinal))
            {
                ContinueDelete(projectRoot, document, entry, journalPath);
                return;
            }
            ContinueWrite(projectRoot, document, entry, journalPath);
        }

        private static void ContinueWrite(
            string projectRoot,
            ProjectTransactionDocument document,
            ProjectTransactionEntry entry,
            string journalPath)
        {
            string sourcePath = ResolveInside(document.stagingPath, entry.normalizedPath);
            if (!File.Exists(IoPath(sourcePath)) ||
                !string.Equals(Sha256(sourcePath), entry.targetSha256, StringComparison.Ordinal))
            {
                throw new CryptographicException(
                    $"The staged file digest is invalid for '{entry.normalizedPath}'.");
            }
            string livePath = ResolveInside(projectRoot, entry.normalizedPath);
            if (string.Equals(entry.state, "pending", StringComparison.Ordinal))
            {
                BackupPriorFile(livePath, entry);
                entry.state = "backed-up";
                WriteJournal(journalPath, document);
            }
            string liveDirectory = Path.GetDirectoryName(livePath);
            Directory.CreateDirectory(IoPath(liveDirectory));
            string temporaryPath = Path.Combine(
                liveDirectory,
                $".{Path.GetFileName(livePath)}.{document.runId}.partial");
            CopyDurably(sourcePath, temporaryPath);
            PublishReplacement(temporaryPath, livePath);
            entry.state = "committed";
            WriteJournal(journalPath, document);
        }

        private static void ContinueDelete(
            string projectRoot,
            ProjectTransactionDocument document,
            ProjectTransactionEntry entry,
            string journalPath)
        {
            string livePath = ResolveInside(projectRoot, entry.normalizedPath);
            if (!File.Exists(IoPath(livePath)))
            {
                entry.state = "skipped-missing";
                WriteJournal(journalPath, document);
                return;
            }
            if (!string.Equals(
                    Sha256(livePath),
                    entry.expectedPriorSha256,
                    StringComparison.Ordinal))
            {
                entry.state = "preserved-modified";
                WriteJournal(journalPath, document);
                return;
            }
            if (string.Equals(entry.state, "pending", StringComparison.Ordinal))
            {
                BackupPriorFile(livePath, entry);
                entry.state = "backed-up";
                WriteJournal(journalPath, document);
            }
            File.Delete(IoPath(livePath));
            entry.state = "committed";
            WriteJournal(journalPath, document);
        }

        private static void BackupPriorFile(
            string livePath,
            ProjectTransactionEntry entry)
        {
            if (!entry.hadPriorFile || !File.Exists(IoPath(livePath)))
            {
                return;
            }
            CopyDurably(livePath, entry.backupPath);
        }

        private static void RollBack(
            string projectRoot,
            ProjectTransactionDocument document,
            string journalPath)
        {
            for (int index = document.entries.Count - 1; index >= 0; index--)
            {
                ProjectTransactionEntry entry = document.entries[index];
                if (!string.Equals(entry.state, "committed", StringComparison.Ordinal))
                {
                    continue;
                }
                string livePath = ResolveInside(projectRoot, entry.normalizedPath);
                if (entry.hadPriorFile && File.Exists(IoPath(entry.backupPath)))
                {
                    string temporaryPath = livePath + ".rollback.partial";
                    CopyDurably(entry.backupPath, temporaryPath);
                    PublishReplacement(temporaryPath, livePath);
                }
                else if (string.Equals(entry.operation, "write", StringComparison.Ordinal) &&
                    File.Exists(IoPath(livePath)) &&
                    string.Equals(
                        Sha256(livePath),
                        entry.targetSha256,
                        StringComparison.Ordinal))
                {
                    File.Delete(IoPath(livePath));
                }
                entry.state = "rolled-back";
                WriteJournal(journalPath, document);
            }
            document.state = "rolled-back";
            WriteJournal(journalPath, document);
        }

        private static List<VerifiedStagingFile> NormalizeFileRecords(
            IEnumerable<VerifiedStagingFile> files,
            bool allowEmpty)
        {
            List<VerifiedStagingFile> normalized = (files ??
                    Enumerable.Empty<VerifiedStagingFile>())
                .OrderBy(file => file?.normalizedPath, StringComparer.Ordinal)
                .ToList();
            if (!allowEmpty && normalized.Count == 0)
            {
                throw new InvalidOperationException(
                    "The project transaction contains no owned files.");
            }
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (VerifiedStagingFile file in normalized)
            {
                if (file == null ||
                    file.bytes < 0 ||
                    !IsSha256(file.sha256) ||
                    !paths.Add(file.normalizedPath ?? string.Empty))
                {
                    throw new CryptographicException(
                        "The verified file record is invalid.");
                }
            }
            return normalized;
        }

        private static void VerifyStagingFile(
            string stagingRoot,
            VerifiedStagingFile file)
        {
            string stagedPath = ResolveInside(stagingRoot, file.normalizedPath);
            var info = new FileInfo(IoPath(stagedPath));
            if (!info.Exists || info.Length != file.bytes)
            {
                throw new CryptographicException(
                    $"The staged file length is invalid for '{file.normalizedPath}'.");
            }
            if (!string.Equals(Sha256(stagedPath), file.sha256, StringComparison.Ordinal))
            {
                throw new CryptographicException(
                    $"The staged file digest is invalid for '{file.normalizedPath}'.");
            }
        }

        private static void ValidateJournal(
            string projectRoot,
            string runId,
            ProjectTransactionDocument document)
        {
            if (document == null ||
                document.schemaVersion != SchemaVersion ||
                !string.Equals(document.runId, runId, StringComparison.Ordinal) ||
                !string.Equals(
                    Path.GetFullPath(document.projectPath),
                    projectRoot,
                    StringComparison.OrdinalIgnoreCase) ||
                (document.state != "prepared" &&
                    document.state != "committed" &&
                    document.state != "rolled-back") ||
                document.entries == null ||
                document.entries.Count == 0)
            {
                throw new InvalidDataException(
                    "The project transaction journal is invalid.");
            }
            if (document.entries.Any(entry =>
                    string.Equals(entry.operation, "write", StringComparison.Ordinal)))
            {
                string stagingRoot = RequireAbsoluteDirectory(
                    document.stagingPath,
                    "staging");
                ValidateSeparateRoots(projectRoot, stagingRoot);
                document.stagingPath = stagingRoot;
            }
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProjectTransactionEntry entry in document.entries)
            {
                ValidateEntry(projectRoot, document, entry);
                if (!paths.Add(entry.normalizedPath))
                {
                    throw new InvalidDataException(
                        "The project transaction journal contains a duplicate path.");
                }
            }
        }

        private static void ValidateEntry(
            string projectRoot,
            ProjectTransactionDocument document,
            ProjectTransactionEntry entry)
        {
            if (entry == null ||
                (entry.operation != "write" && entry.operation != "delete") ||
                (entry.operation == "write" && !IsSha256(entry.targetSha256)) ||
                (entry.operation == "delete" && !IsSha256(entry.expectedPriorSha256)) ||
                (entry.state != "pending" &&
                    entry.state != "backed-up" &&
                    entry.state != "committed" &&
                    entry.state != "preserved-modified" &&
                    entry.state != "skipped-missing" &&
                    entry.state != "rolled-back"))
            {
                throw new InvalidDataException(
                    "The project transaction entry is invalid.");
            }
            ResolveInside(projectRoot, entry.normalizedPath);
            string transactionDirectory = Path.GetDirectoryName(
                JournalPath(projectRoot, document.runId));
            string backupPath = Path.GetFullPath(entry.backupPath);
            if (!IsInside(transactionDirectory, backupPath))
            {
                throw new InvalidDataException(
                    "The project transaction backup path is invalid.");
            }
        }

        private static string JournalPath(string projectRoot, string runId)
        {
            return Path.Combine(
                ResolveInside(projectRoot, $"{TransactionRoot}/{runId}"),
                "journal.json");
        }

        private static ProjectTransactionDocument ReadJournal(string journalPath)
        {
            if (!File.Exists(IoPath(journalPath)))
            {
                throw new FileNotFoundException(
                    "The project transaction journal does not exist.",
                    journalPath);
            }
            try
            {
                return JsonConvert.DeserializeObject<ProjectTransactionDocument>(
                    File.ReadAllText(IoPath(journalPath)));
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "The project transaction journal is not valid JSON.",
                    exception);
            }
        }

        private static ProjectTransactionResult Result(
            string journalPath,
            string state)
        {
            return new ProjectTransactionResult
            {
                journalPath = journalPath,
                state = state,
            };
        }

        private static string ResolveInside(string root, string normalizedPath)
        {
            if (string.IsNullOrWhiteSpace(normalizedPath) ||
                normalizedPath.Contains("\\") ||
                normalizedPath.StartsWith("/", StringComparison.Ordinal) ||
                normalizedPath.Split('/').Any(
                    segment => string.IsNullOrWhiteSpace(segment) ||
                        segment == "." ||
                        segment == ".."))
            {
                throw new InvalidOperationException(
                    "A project transaction path is invalid.");
            }
            if (!normalizedPath.StartsWith("Assets/", StringComparison.Ordinal) &&
                !normalizedPath.StartsWith("Packages/", StringComparison.Ordinal) &&
                !normalizedPath.StartsWith(".yucp/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A project transaction path is outside the owned roots.");
            }
            string resolved = Path.GetFullPath(
                Path.Combine(root, normalizedPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInside(root, resolved))
            {
                throw new InvalidOperationException(
                    "A project transaction path escaped its root.");
            }
            RejectReparsePoint(root, resolved);
            return resolved;
        }

        private static FileStream AcquireProjectLock(
            string projectRoot,
            string runId)
        {
            string lockDirectory = Path.Combine(projectRoot, ".yucp", "locks");
            Directory.CreateDirectory(IoPath(lockDirectory));
            string lockPath = Path.Combine(
                lockDirectory,
                "package-lifecycle.lock");
            FileStream stream;
            try
            {
                stream = new FileStream(
                    IoPath(lockPath),
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException exception)
            {
                throw new IOException(
                    "Another package lifecycle operation holds the project lock.",
                    exception);
            }
            byte[] owner = Encoding.UTF8.GetBytes(runId + "\n");
            stream.SetLength(0);
            stream.Write(owner, 0, owner.Length);
            stream.Flush(true);
            return stream;
        }

        private static void RejectReparsePoint(string root, string candidate)
        {
            string rootPath = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string relative = candidate.Substring(rootPath.Length).TrimStart(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string current = rootPath;
            foreach (string segment in relative.Split(
                new[]
                {
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar,
                },
                StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (!Directory.Exists(IoPath(current)) &&
                    !File.Exists(IoPath(current)))
                {
                    continue;
                }
                if ((File.GetAttributes(IoPath(current)) &
                    FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        "A project transaction path contains a reparse point.");
                }
            }
        }

        private static string RequireAbsoluteDirectory(string path, string name)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            {
                throw new InvalidOperationException(
                    $"The {name} path must be absolute.");
            }
            string resolved = Path.GetFullPath(path);
            if (!Directory.Exists(IoPath(resolved)))
            {
                throw new DirectoryNotFoundException(
                    $"The {name} directory does not exist.");
            }
            return resolved;
        }

        private static void ValidateRunId(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId) ||
                runId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                runId.Contains("/") ||
                runId.Contains("\\"))
            {
                throw new InvalidOperationException(
                    "The transaction run identifier is invalid.");
            }
        }

        private static void ValidateSeparateRoots(
            string projectRoot,
            string stagingRoot)
        {
            if (IsInside(projectRoot, stagingRoot) ||
                IsInside(stagingRoot, projectRoot) ||
                string.Equals(
                    projectRoot,
                    stagingRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The verified staging tree must be outside the Unity project.");
            }
        }

        private static bool IsInside(string root, string candidate)
        {
            string boundary = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(candidate);
            return resolved.StartsWith(boundary, StringComparison.OrdinalIgnoreCase);
        }

        private static void CopyDurably(string source, string destination)
        {
            string directory = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(IoPath(directory));
            using (FileStream input = new FileStream(
                IoPath(source),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (FileStream output = new FileStream(
                IoPath(destination),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(true);
            }
        }

        private static void PublishReplacement(
            string temporaryPath,
            string livePath)
        {
            if (File.Exists(IoPath(livePath)))
            {
                File.Replace(IoPath(temporaryPath), IoPath(livePath), null);
            }
            else
            {
                File.Move(IoPath(temporaryPath), IoPath(livePath));
            }
        }

        private static void WriteJournal(
            string journalPath,
            ProjectTransactionDocument document)
        {
            string encoded = JsonConvert.SerializeObject(document, Formatting.Indented);
            string temporaryPath = journalPath + ".partial";
            Directory.CreateDirectory(IoPath(Path.GetDirectoryName(journalPath)));
            using (var stream = new FileStream(
                IoPath(temporaryPath),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(encoded);
                writer.Flush();
                stream.Flush(true);
            }
            if (File.Exists(IoPath(journalPath)))
            {
                File.Replace(IoPath(temporaryPath), IoPath(journalPath), null);
            }
            else
            {
                File.Move(IoPath(temporaryPath), IoPath(journalPath));
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

        private static string Sha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(IoPath(path)))
            {
                return BitConverter.ToString(sha256.ComputeHash(stream))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static string IoPath(string path)
        {
            string resolved = Path.GetFullPath(path);
            if (Path.DirectorySeparatorChar != '\\' ||
                resolved.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return resolved;
            }
            if (resolved.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return @"\\?\UNC\" + resolved.Substring(2);
            }
            return @"\\?\" + resolved;
        }
    }
}
