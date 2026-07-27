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
                ExtractTo(
                    root,
                    ("././@LongLink", 'L', longName + "\0"),
                    ("truncated-name.txt", '0', "payload"));

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

        [Test]
        public void ExtractUsesTheLocalPaxUtf8Path()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "yucp-tar-pax-local-" + Guid.NewGuid().ToString("N"));
            const string paxPath =
                "package/Assets/Accented-\u00E9/ProductPayload.txt";
            try
            {
                ExtractTo(
                    root,
                    ("pax-local", 'x', BuildPaxPathRecord(paxPath)),
                    ("ignored.txt", '0', "payload"));

                Assert.That(
                    File.ReadAllText(
                        Path.Combine(
                            root,
                            paxPath.Replace(
                                '/',
                                Path.DirectorySeparatorChar))),
                    Is.EqualTo("payload"));
                Assert.That(
                    File.Exists(Path.Combine(root, "ignored.txt")),
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

        [Test]
        public void ExtractPrefersTheLocalPaxPathOverTheGlobalPath()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "yucp-tar-pax-precedence-" +
                Guid.NewGuid().ToString("N"));
            const string globalPath =
                "package/Assets/Global/ProductPayload.txt";
            const string localPath =
                "package/Assets/Local/ProductPayload.txt";
            try
            {
                ExtractTo(
                    root,
                    ("pax-global", 'g', BuildPaxPathRecord(globalPath)),
                    ("pax-local", 'x', BuildPaxPathRecord(localPath)),
                    ("ignored.txt", '0', "payload"));

                Assert.That(
                    File.ReadAllText(
                        Path.Combine(
                            root,
                            localPath.Replace(
                                '/',
                                Path.DirectorySeparatorChar))),
                    Is.EqualTo("payload"));
                Assert.That(
                    File.Exists(
                        Path.Combine(
                            root,
                            globalPath.Replace(
                                '/',
                                Path.DirectorySeparatorChar))),
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

        [Test]
        public void ExtractPrefersEachHeaderNameOverTheGlobalPaxDefault()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "yucp-tar-pax-global-" + Guid.NewGuid().ToString("N"));
            const string globalPath =
                "package/Assets/Global/DefaultPayload.txt";
            try
            {
                ExtractTo(
                    root,
                    ("pax-global", 'g', BuildPaxPathRecord(globalPath)),
                    ("package/Assets/First.txt", '0', "first"),
                    ("package/Assets/Second.txt", '0', "second"));

                Assert.That(
                    File.ReadAllText(
                        Path.Combine(root, "package", "Assets", "First.txt")),
                    Is.EqualTo("first"));
                Assert.That(
                    File.ReadAllText(
                        Path.Combine(root, "package", "Assets", "Second.txt")),
                    Is.EqualTo("second"));
                Assert.That(
                    File.Exists(
                        Path.Combine(
                            root,
                            globalPath.Replace(
                                '/',
                                Path.DirectorySeparatorChar))),
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

        private static string BuildPaxPathRecord(string path)
        {
            string payload = " path=" + path + "\n";
            int recordLength = Encoding.UTF8.GetByteCount(payload) + 1;
            while (true)
            {
                string record = recordLength + payload;
                int encodedLength = Encoding.UTF8.GetByteCount(record);
                if (encodedLength == recordLength)
                {
                    return record;
                }
                recordLength = encodedLength;
            }
        }

        private static void ExtractTo(
            string root,
            params (string name, char type, string body)[] entries)
        {
            Directory.CreateDirectory(root);
            using (MemoryStream archive = BuildArchive(entries))
            {
                TarGZipArchiveExtractor.Extract(
                    archive,
                    name => Path.Combine(
                        root,
                        name.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));
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
