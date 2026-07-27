using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Batch
{
    internal static class BatchFileProtocol
    {
        internal static T ReadJson<T>(
            string path,
            string label,
            long maximumBytes)
        {
            string resolvedPath = RequireAbsoluteFile(path, label);
            string json;
            using (var stream = new FileStream(
                resolvedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                json = ReadBoundedUtf8(stream, maximumBytes, label);
            }
            T value = JsonConvert.DeserializeObject<T>(json);
            if (value == null)
            {
                throw new InvalidDataException(
                    $"The {label} file is invalid.");
            }
            return value;
        }

        internal static string ReadBoundedUtf8(
            Stream stream,
            long maximumBytes,
            string label)
        {
            if (stream == null ||
                maximumBytes < 2 ||
                maximumBytes > int.MaxValue)
            {
                throw new InvalidDataException(
                    $"The {label} file size is invalid.");
            }
            var buffer = new byte[8192];
            using (var content = new MemoryStream())
            {
                long totalBytes = 0;
                while (true)
                {
                    int remainingWithOverflowByte = checked(
                        (int)Math.Min(
                            buffer.Length,
                            maximumBytes - totalBytes + 1));
                    int read = stream.Read(
                        buffer,
                        0,
                        remainingWithOverflowByte);
                    if (read == 0)
                    {
                        break;
                    }
                    totalBytes += read;
                    if (totalBytes > maximumBytes)
                    {
                        throw new InvalidDataException(
                            $"The {label} file size is invalid.");
                    }
                    content.Write(buffer, 0, read);
                }
                if (totalBytes < 2)
                {
                    throw new InvalidDataException(
                        $"The {label} file size is invalid.");
                }
                content.Position = 0;
                using (var reader = new StreamReader(
                    content,
                    new UTF8Encoding(false),
                    true,
                    1024,
                    true))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        internal static void WriteJsonAtomically(string path, object value)
        {
            string resolvedPath = RequireAbsolutePath(path, "output");
            string directory = Path.GetDirectoryName(resolvedPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidDataException(
                    "The output directory is invalid.");
            }
            Directory.CreateDirectory(directory);
            string temporaryPath = resolvedPath + ".partial";
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(false)))
            {
                writer.Write(
                    JsonConvert.SerializeObject(value, Formatting.Indented));
                writer.Flush();
                stream.Flush(true);
            }
            if (File.Exists(resolvedPath))
            {
                File.Replace(temporaryPath, resolvedPath, null);
            }
            else
            {
                File.Move(temporaryPath, resolvedPath);
            }
        }

        internal static string RequireAbsoluteFile(
            string value,
            string label)
        {
            string path = RequireAbsolutePath(value, label);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"The {label} file does not exist.",
                    path);
            }
            return path;
        }

        internal static string RequireAbsolutePath(
            string value,
            string label)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !Path.IsPathRooted(value))
            {
                throw new InvalidDataException(
                    $"The {label} path must be absolute.");
            }
            return Path.GetFullPath(value);
        }

        internal static bool IsSafeIdentifier(string value)
        {
            return PackageProtocolIdentifier.IsSafe(value);
        }
    }

    internal static class BatchCommandLine
    {
        private const string ExecuteMethodArgument = "-executeMethod";

        internal static bool ShouldResumeAfterDomainReload(
            bool isBatchMode,
            string requestPath,
            string resultPath,
            string executeMethodName,
            string[] commandLineArguments)
        {
            if (!isBatchMode ||
                string.IsNullOrWhiteSpace(requestPath) ||
                string.IsNullOrWhiteSpace(resultPath) ||
                string.IsNullOrWhiteSpace(executeMethodName) ||
                commandLineArguments == null)
            {
                return false;
            }
            for (int index = 0;
                index < commandLineArguments.Length - 1;
                index++)
            {
                if (string.Equals(
                        commandLineArguments[index],
                        ExecuteMethodArgument,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        commandLineArguments[index + 1],
                        executeMethodName,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
