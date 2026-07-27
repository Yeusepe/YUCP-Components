using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
        public string brokerTraceId = string.Empty;
        public string errorMessage = string.Empty;
        public string expectedCurrentReleaseRoot = string.Empty;
        public string operation = string.Empty;
        public string phase = "prepared";
        public PackageDeliveryInstallState priorState;
        public string runId = string.Empty;
        public PackageDeliveryInstallState targetState;
    }

    internal static class AtomicFilePublisher
    {
        private const uint MoveFileReplaceExisting = 0x00000001;
        private const uint MoveFileWriteThrough = 0x00000008;
        private static readonly object PublicationLock = new object();

        internal static void Publish(
            string temporaryPath,
            string destinationPath)
        {
            string temporary = Path.GetFullPath(temporaryPath);
            string destination = Path.GetFullPath(destinationPath);
            string temporaryDirectory = Path.GetDirectoryName(temporary);
            string destinationDirectory = Path.GetDirectoryName(destination);
            StringComparison comparison =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
            if (!string.Equals(
                    temporaryDirectory,
                    destinationDirectory,
                    comparison))
            {
                throw new InvalidDataException(
                    "Atomic file publication requires one directory.");
            }

            lock (PublicationLock)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    PublishWindows(temporary, destination);
                    return;
                }
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    PublishLinux(
                        temporary,
                        destination,
                        destinationDirectory);
                    return;
                }
                throw new PlatformNotSupportedException(
                    "Atomic file publication is not supported on this platform.");
            }
        }

        private static void PublishWindows(
            string temporaryPath,
            string destinationPath)
        {
            // https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-movefileexw
            bool moved = MoveFileExWindows(
                ToExtendedWindowsPath(temporaryPath),
                ToExtendedWindowsPath(destinationPath),
                MoveFileReplaceExisting | MoveFileWriteThrough);
            if (!moved)
            {
                ThrowNativePublicationError(
                    Marshal.GetLastWin32Error());
            }
        }

        private static void PublishLinux(
            string temporaryPath,
            string destinationPath,
            string destinationDirectory)
        {
            // https://pubs.opengroup.org/onlinepubs/9799919799/functions/rename.html
            if (RenameLinux(temporaryPath, destinationPath) != 0)
            {
                ThrowNativePublicationError(
                    Marshal.GetLastWin32Error());
            }

            int directoryDescriptor = OpenLinux(
                destinationDirectory,
                0);
            if (directoryDescriptor < 0)
            {
                ThrowNativePublicationError(
                    Marshal.GetLastWin32Error());
            }
            int syncResult = FsyncLinux(directoryDescriptor);
            int syncError = syncResult == 0
                ? 0
                : Marshal.GetLastWin32Error();
            int closeResult = CloseLinux(directoryDescriptor);
            int closeError = closeResult == 0
                ? 0
                : Marshal.GetLastWin32Error();
            if (syncResult != 0)
            {
                ThrowNativePublicationError(syncError);
            }
            if (closeResult != 0)
            {
                ThrowNativePublicationError(closeError);
            }
        }

        private static string ToExtendedWindowsPath(string path)
        {
            if (path.StartsWith(
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

        private static void ThrowNativePublicationError(int error)
        {
            throw new IOException(
                "The durable atomic file publication failed.",
                new Win32Exception(error));
        }

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            EntryPoint = "MoveFileExW",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileExWindows(
            string existingFileName,
            string newFileName,
            uint flags);

        [DllImport(
            "libc",
            CharSet = CharSet.Ansi,
            EntryPoint = "rename",
            SetLastError = true)]
        private static extern int RenameLinux(
            string oldPath,
            string newPath);

        [DllImport(
            "libc",
            CharSet = CharSet.Ansi,
            EntryPoint = "open",
            SetLastError = true)]
        private static extern int OpenLinux(string path, int flags);

        [DllImport(
            "libc",
            EntryPoint = "fsync",
            SetLastError = true)]
        private static extern int FsyncLinux(int descriptor);

        [DllImport(
            "libc",
            EntryPoint = "close",
            SetLastError = true)]
        private static extern int CloseLinux(int descriptor);
    }

    internal static class PackageLifecycleCheckpointStore
    {
        private const int MaximumCheckpointBytes = 16 * 1024 * 1024;
        private static readonly object AttemptLock = new object();

        internal static string GetOrCreateAttemptId(
            string projectPath,
            string aliasId)
        {
            string path = ResolveAttemptPath(projectPath, aliasId);
            lock (AttemptLock)
            {
                string existing = ReadAttemptId(path, false);
                if (existing != null)
                {
                    return existing;
                }

                string attemptId = Guid.NewGuid().ToString("N");
                string temporaryPath = path + "." +
                    Guid.NewGuid().ToString("N") + ".partial";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                try
                {
                    using (var stream = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
                    using (var writer = new StreamWriter(
                        stream,
                        new UTF8Encoding(false)))
                    {
                        writer.Write(attemptId);
                        writer.Write('\n');
                        writer.Flush();
                        stream.Flush(true);
                    }
                    AtomicFilePublisher.Publish(
                        temporaryPath,
                        path);
                    return ReadAttemptId(path) ??
                        throw new InvalidDataException(
                            "The package lifecycle attempt identifier is invalid.");
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            }
        }

        internal static bool TryGetAttemptId(
            string projectPath,
            string aliasId,
            out string attemptId)
        {
            string path = ResolveAttemptPath(projectPath, aliasId);
            lock (AttemptLock)
            {
                attemptId = ReadAttemptId(path, false);
                return attemptId != null;
            }
        }

        internal static void ClearAttemptId(
            string projectPath,
            string aliasId)
        {
            string path = ResolveAttemptPath(projectPath, aliasId);
            lock (AttemptLock)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        internal static void Write(
            string projectPath,
            PackageLifecycleCheckpoint checkpoint)
        {
            Validate(checkpoint);
            string path = ResolvePath(
                projectPath,
                checkpoint.runId);
            string temporaryPath = path + "." +
                Guid.NewGuid().ToString("N") + ".partial";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            byte[] encoded = new UTF8Encoding(false).GetBytes(
                JsonConvert.SerializeObject(checkpoint, Formatting.Indented));
            if (encoded.Length > MaximumCheckpointBytes)
            {
                throw new InvalidDataException(
                    "The package lifecycle checkpoint is too large.");
            }
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    stream.Write(encoded, 0, encoded.Length);
                    stream.Flush(true);
                }
                AtomicFilePublisher.Publish(
                    temporaryPath,
                    path);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        internal static PackageLifecycleCheckpoint Read(
            string projectPath,
            string runId)
        {
            string path = ResolvePath(projectPath, runId);
            PackageLifecycleCheckpoint checkpoint;
            try
            {
                checkpoint =
                    JsonConvert.DeserializeObject<PackageLifecycleCheckpoint>(
                        ReadTextSnapshot(
                            path,
                            MaximumCheckpointBytes,
                            "The package lifecycle checkpoint size is invalid."));
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

        internal static void Delete(
            string projectPath,
            string runId)
        {
            string path = ResolvePath(projectPath, runId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
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
                !PackageProtocolIdentifier.IsSafe(checkpoint.runId) ||
                !PackageProtocolIdentifier.IsSafe(checkpoint.aliasId) ||
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
                    "awaiting-transaction",
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
                !PackageProtocolIdentifier.IsSafe(runId))
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

        private static string ResolveAttemptPath(
            string projectPath,
            string aliasId)
        {
            if (string.IsNullOrWhiteSpace(projectPath) ||
                !Path.IsPathRooted(projectPath) ||
                !PackageProtocolIdentifier.IsSafe(aliasId))
            {
                throw new InvalidDataException(
                    "The package lifecycle attempt path is invalid.");
            }
            string projectRoot = Path.GetFullPath(projectPath);
            string digest;
            using (SHA256 sha256 = SHA256.Create())
            {
                digest = string.Concat(
                    sha256.ComputeHash(
                            Encoding.UTF8.GetBytes(
                                "yucp:package-lifecycle-attempt:v1\n" +
                                aliasId))
                        .Select(value => value.ToString("x2")));
            }
            return Path.Combine(
                projectRoot,
                ".yucp",
                "package-lifecycle",
                "attempts",
                digest + ".txt");
        }

        private static string ReadAttemptId(
            string path,
            bool strict = true)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            string attemptId;
            try
            {
                attemptId = ReadTextSnapshot(
                    path,
                    128,
                    "The package lifecycle attempt identifier is invalid.")
                    .Trim();
            }
            catch (InvalidDataException)
            {
                if (!strict)
                {
                    return null;
                }
                throw;
            }
            if (IsAttemptId(attemptId))
            {
                return attemptId;
            }
            if (!strict)
            {
                return null;
            }
            throw new InvalidDataException(
                "The package lifecycle attempt identifier is invalid.");
        }

        private static string ReadTextSnapshot(
            string path,
            int maximumBytes,
            string invalidSizeMessage)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length <= 0 || stream.Length > maximumBytes)
                {
                    throw new InvalidDataException(invalidSizeMessage);
                }
                int length = checked((int)stream.Length);
                var encoded = new byte[length];
                int offset = 0;
                while (offset < encoded.Length)
                {
                    int read = stream.Read(
                        encoded,
                        offset,
                        encoded.Length - offset);
                    if (read == 0)
                    {
                        throw new EndOfStreamException(
                            "The atomic file snapshot ended early.");
                    }
                    offset += read;
                }
                return new UTF8Encoding(false, true).GetString(encoded);
            }
        }

        private static bool IsAttemptId(string value)
        {
            return value != null &&
                value.Length == 32 &&
                value.All(character =>
                    character >= '0' && character <= '9' ||
                    character >= 'a' && character <= 'f');
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
