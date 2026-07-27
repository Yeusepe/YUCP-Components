using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public sealed class TarGZipArchiveExtractorTests
    {
        [Test]
        public void ExtractUsesTheGnuLongNameForTheFollowingFile()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "yucp-tar-long-name-" + Guid.NewGuid().ToString("N"));
            const string longName =
                "package/Assets/VeryLongFolderName/AnotherLongFolderName/" +
                "AThirdLongFolderName/ProductPayload.txt";
            try
            {
                Directory.CreateDirectory(root);
                using (var archive = BuildArchive(
                    ("././@LongLink", 'L', longName + "\0"),
                    ("truncated-name.txt", '0', "payload")))
                {
                    TarGZipArchiveExtractor.Extract(
                        archive,
                        name => Path.Combine(
                            root,
                            name.Replace('/', Path.DirectorySeparatorChar)));
                }

                Assert.That(
                    File.ReadAllText(
                        Path.Combine(
                            root,
                            longName.Replace(
                                '/',
                                Path.DirectorySeparatorChar))),
                    Is.EqualTo("payload"));
                Assert.That(
                    File.Exists(Path.Combine(root, "truncated-name.txt")),
                    Is.False);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private static MemoryStream BuildArchive(
            params (string name, char type, string body)[] entries)
        {
            var compressed = new MemoryStream();
            using (var gzip = new GZipStream(
                compressed,
                CompressionMode.Compress,
                true))
            {
                foreach (var entry in entries)
                {
                    byte[] body = Encoding.UTF8.GetBytes(entry.body);
                    byte[] header = new byte[512];
                    WriteAscii(header, 0, 100, entry.name);
                    WriteAscii(
                        header,
                        124,
                        12,
                        Convert.ToString(body.Length, 8).PadLeft(11, '0'));
                    header[156] = (byte)entry.type;
                    gzip.Write(header, 0, header.Length);
                    gzip.Write(body, 0, body.Length);
                    int padding = (512 - body.Length % 512) % 512;
                    if (padding > 0)
                    {
                        gzip.Write(new byte[padding], 0, padding);
                    }
                }
                gzip.Write(new byte[1024], 0, 1024);
            }
            compressed.Position = 0;
            return compressed;
        }

        private static void WriteAscii(
            byte[] destination,
            int offset,
            int length,
            string value)
        {
            byte[] encoded = Encoding.ASCII.GetBytes(value);
            Array.Copy(
                encoded,
                0,
                destination,
                offset,
                Math.Min(length, encoded.Length));
        }
    }
}
