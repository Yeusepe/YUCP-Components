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
            while (TryReadHeader(gzipStream, header))
            {
                string entryName = ReadString(header, 0, 100);
                long entrySize = ReadOctal(header, 124, 12);
                char entryType = (char)header[156];

                if (string.IsNullOrEmpty(entryName))
                {
                    SkipEntry(gzipStream, entrySize);
                    continue;
                }

                string destinationPath = resolvePath(entryName);
                bool isDirectory = entryType == '5' || entryName.EndsWith("/", StringComparison.Ordinal);
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
