using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

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
            var info = new FileInfo(resolvedPath);
            if (info.Length < 2 || info.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    $"The {label} file size is invalid.");
            }
            T value = JsonConvert.DeserializeObject<T>(
                File.ReadAllText(resolvedPath));
            if (value == null)
            {
                throw new InvalidDataException(
                    $"The {label} file is invalid.");
            }
            return value;
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
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            {
                return false;
            }
            foreach (char character in value)
            {
                if (!char.IsLetterOrDigit(character) &&
                    character != '.' &&
                    character != '_' &&
                    character != '-')
                {
                    return false;
                }
            }
            return true;
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
