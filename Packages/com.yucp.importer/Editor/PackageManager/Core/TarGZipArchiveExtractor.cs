using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class TarGZipArchiveExtractor
    {
        private const int TarBlockSize = 512;
        private const int MaximumExtendedHeaderBytes = 1024 * 1024;

        internal static void Extract(Stream compressedArchive, Func<string, string> resolvePath)
        {
            if (compressedArchive == null)
                throw new ArgumentNullException(nameof(compressedArchive));
            if (resolvePath == null)
                throw new ArgumentNullException(nameof(resolvePath));

            using var gzipStream = new GZipStream(
                compressedArchive,
                CompressionMode.Decompress,
                leaveOpen: true);
            byte[] header = new byte[TarBlockSize];
            string globalPaxPath = null;
            string pendingLongName = null;
            string pendingPaxPath = null;
            while (TryReadHeader(gzipStream, header))
            {
                string entryName = ReadString(header, 0, 100);
                string prefix = ReadString(header, 345, 155);
                if (!string.IsNullOrEmpty(prefix))
                {
                    entryName = prefix + "/" + entryName;
                }
                long entrySize = ReadOctal(header, 124, 12);
                char entryType = (char)header[156];
                if (entryType == 'L')
                {
                    pendingLongName = ReadExtendedText(
                        gzipStream,
                        entrySize).TrimEnd('\0', '\r', '\n');
                    continue;
                }
                if (entryType == 'x' || entryType == 'g')
                {
                    string paxPath = ReadPaxPath(
                        ReadExtendedBytes(gzipStream, entrySize));
                    if (entryType == 'g')
                    {
                        if (!string.IsNullOrWhiteSpace(paxPath))
                        {
                            globalPaxPath = paxPath;
                        }
                    }
                    else
                    {
                        pendingPaxPath = paxPath;
                    }
                    continue;
                }
                if (entryType == 'K')
                {
                    SkipEntry(gzipStream, entrySize);
                    continue;
                }

                entryName = FirstNonEmpty(
                    pendingPaxPath,
                    pendingLongName,
                    entryName,
                    globalPaxPath);
                pendingPaxPath = null;
                pendingLongName = null;
                if (string.IsNullOrEmpty(entryName))
                {
                    SkipEntry(gzipStream, entrySize);
                    continue;
                }

                bool isDirectory = entryType == '5' || entryName.EndsWith("/", StringComparison.Ordinal);
                bool isRegularFile = entryType == '\0' || entryType == '0';
                if (!isDirectory && !isRegularFile)
                {
                    SkipEntry(gzipStream, entrySize);
                    continue;
                }
                string destinationPath = resolvePath(entryName);
                if (isDirectory)
                {
                    Directory.CreateDirectory(destinationPath);
                    SkipEntry(gzipStream, entrySize);
                    continue;
                }

                string parentDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parentDirectory))
                {
                    Directory.CreateDirectory(parentDirectory);
                }

                using Stream output = File.Create(destinationPath);
                CopyExactly(gzipStream, output, entrySize);
                SkipPadding(gzipStream, entrySize);
            }
        }

        private static byte[] ReadExtendedBytes(
            Stream input,
            long entrySize)
        {
            if (entrySize < 0 || entrySize > MaximumExtendedHeaderBytes)
            {
                throw new InvalidDataException(
                    "The TAR extended header is too large.");
            }
            var value = new byte[(int)entrySize];
            int read = ReadAtMost(input, value, value.Length);
            if (read != value.Length)
            {
                throw new InvalidDataException(
                    "The TAR archive ended inside an extended header.");
            }
            SkipPadding(input, entrySize);
            return value;
        }

        private static string ReadExtendedText(
            Stream input,
            long entrySize)
        {
            try
            {
                return new UTF8Encoding(false, true).GetString(
                    ReadExtendedBytes(input, entrySize));
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException(
                    "The TAR extended header is not valid UTF-8.",
                    exception);
            }
        }

        private static string ReadPaxPath(byte[] payload)
        {
            int offset = 0;
            string path = null;
            while (offset < payload.Length)
            {
                int space = Array.IndexOf(payload, (byte)' ', offset);
                if (space <= offset)
                {
                    throw new InvalidDataException(
                        "The TAR PAX header is invalid.");
                }
                string lengthText = Encoding.ASCII.GetString(
                    payload,
                    offset,
                    space - offset);
                if (!int.TryParse(lengthText, out int recordLength) ||
                    recordLength <= space - offset + 2 ||
                    offset + recordLength > payload.Length ||
                    payload[offset + recordLength - 1] != (byte)'\n')
                {
                    throw new InvalidDataException(
                        "The TAR PAX record length is invalid.");
                }
                int valueOffset = space + 1;
                int valueLength =
                    offset + recordLength - 1 - valueOffset;
                string record;
                try
                {
                    record = new UTF8Encoding(false, true).GetString(
                        payload,
                        valueOffset,
                        valueLength);
                }
                catch (DecoderFallbackException exception)
                {
                    throw new InvalidDataException(
                        "The TAR PAX record is not valid UTF-8.",
                        exception);
                }
                int equals = record.IndexOf('=');
                if (equals <= 0)
                {
                    throw new InvalidDataException(
                        "The TAR PAX record is invalid.");
                }
                if (string.Equals(
                    record.Substring(0, equals),
                    "path",
                    StringComparison.Ordinal))
                {
                    path = record.Substring(equals + 1);
                }
                offset += recordLength;
            }
            return path;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(
                value => !string.IsNullOrWhiteSpace(value));
        }

        private static bool TryReadHeader(Stream stream, byte[] header)
        {
            int totalRead = ReadAtMost(stream, header, header.Length);
            if (totalRead == 0)
                return false;
            if (totalRead != header.Length)
                throw new InvalidDataException("The TAR archive ended before a header was complete.");
            return header.Any(value => value != 0);
        }

        private static string ReadString(byte[] header, int offset, int length)
        {
            return Encoding.ASCII.GetString(header, offset, length).Trim('\0', ' ');
        }

        private static long ReadOctal(byte[] header, int offset, int length)
        {
            string rawValue = ReadString(header, offset, length);
            if (string.IsNullOrEmpty(rawValue))
                return 0;

            try
            {
                return Convert.ToInt64(rawValue, 8);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException(
                    $"The TAR header contains an invalid size field '{rawValue}'.",
                    exception);
            }
        }

        private static void SkipEntry(Stream input, long entrySize)
        {
            CopyExactly(input, Stream.Null, entrySize);
            SkipPadding(input, entrySize);
        }

        private static void CopyExactly(Stream input, Stream output, long bytesToCopy)
        {
            byte[] buffer = new byte[81920];
            long remaining = bytesToCopy;
            while (remaining > 0)
            {
                int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0)
                    throw new InvalidDataException("The TAR archive ended before an entry was complete.");

                output.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private static void SkipPadding(Stream input, long entrySize)
        {
            long remainder = entrySize % TarBlockSize;
            if (remainder == 0)
                return;

            long padding = TarBlockSize - remainder;
            CopyExactly(input, Stream.Null, padding);
        }

        private static int ReadAtMost(Stream stream, byte[] buffer, int bytesToRead)
        {
            int totalRead = 0;
            while (totalRead < bytesToRead)
            {
                int read = stream.Read(buffer, totalRead, bytesToRead - totalRead);
                if (read == 0)
                    break;
                totalRead += read;
            }
            return totalRead;
        }
    }
}
