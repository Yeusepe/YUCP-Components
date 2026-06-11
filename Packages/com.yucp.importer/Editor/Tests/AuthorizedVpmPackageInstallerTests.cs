using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using YUCP.Importer.Editor.PackageManager;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.Tests
{
    public class AuthorizedVpmPackageInstallerTests
    {
        private const string TestPackageId = "com.yucp.songthing";

        [Test]
        public void InstallAuthorizedPackage_DownloadsVpmZipAndInstallsPackageJsonAtRoot()
        {
            string projectDir = null;
            try
            {
                projectDir = CreateTemporaryProjectDirectory();
                byte[] zipBytes = CreateVpmPackageZip(
                    TestPackageId,
                    "1.0.12",
                    ("Runtime/Marker.txt", "installed through authorized VPM ZIP"));
                string zipSha256 = ComputeSha256(zipBytes);

                using var server = new LocalPackageDownloadServer(zipBytes, zipSha256);
                var package = new UpdateDeliveryService.AliasInstallPlanPackage
                {
                    packageId = TestPackageId,
                    version = "1.0.12",
                    channel = "stable",
                    packageSha256 = zipSha256,
                    zipSha256 = zipSha256,
                    sourceKind = "zip",
                    downloadAuthorizationUrl = server.BaseUrl + "/api/backstage/access/products/catalog_1/packages/com.yucp.songthing/download",
                };

                AuthorizedVpmPackageInstaller.AuthorizedPackageInstallResult result =
                    AuthorizedVpmPackageInstaller.InstallAuthorizedPackage(
                    projectDir,
                    package,
                    "access-token");

                string installedPackageDir = Path.Combine(projectDir, "Packages", TestPackageId);
                string installedPackageJson = Path.Combine(installedPackageDir, "package.json");
                string markerPath = Path.Combine(installedPackageDir, "Runtime", "Marker.txt");

                Assert.That(File.Exists(installedPackageJson), Is.True);
                Assert.That(File.ReadAllText(installedPackageJson), Does.Contain("\"name\":\"" + TestPackageId + "\""));
                Assert.That(File.ReadAllText(installedPackageJson), Does.Contain("\"version\":\"1.0.12\""));
                Assert.That(File.ReadAllText(markerPath), Is.EqualTo("installed through authorized VPM ZIP"));
                Assert.That(server.AuthorizationRequests, Is.EqualTo(1));
                Assert.That(server.DownloadRequests, Is.EqualTo(1));
                Assert.That(server.CapturedAuthorizationHeader, Is.EqualTo("Bearer access-token"));
                Assert.That(server.CapturedAuthorizationBody, Does.Contain("\"version\":\"1.0.12\""));
                Assert.That(server.CapturedAuthorizationBody, Does.Contain("\"channel\":\"stable\""));
            }
            finally
            {
                DeleteDirectoryIfPresent(projectDir);
            }
        }

        [Test]
        public void InstallAuthorizedPackage_DownloadsUnityPackagePayloadAndImportsPathnameAssets()
        {
            string projectDir = null;
            try
            {
                projectDir = CreateTemporaryProjectDirectory();
                string packageDirectory = Path.Combine(projectDir, "Packages", TestPackageId);
                Directory.CreateDirectory(packageDirectory);
                File.WriteAllText(
                    Path.Combine(packageDirectory, "package.json"),
                    "{\"name\":\"" + TestPackageId + "\",\"version\":\"1.0.12\",\"displayName\":\"Song Thing\"}");

                byte[] unityPackageBytes = CreateUnityPackage(
                    ("asset-guid/pathname", "Assets/SongThing/Marker.txt"),
                    ("asset-guid/asset", "imported from authorized unitypackage"),
                    ("asset-guid/asset.meta", "fileFormatVersion: 2\n"));
                string unityPackageSha256 = ComputeSha256(unityPackageBytes);

                using var server = new LocalPackageDownloadServer(
                    unityPackageBytes,
                    unityPackageSha256,
                    sourceKind: "unitypackage",
                    contentType: "application/octet-stream",
                    deliveryName: "Song-Thing_1.0.12.unitypackage",
                    downloadPath: "/downloads/Song-Thing_1.0.12.unitypackage");
                var package = new UpdateDeliveryService.AliasInstallPlanPackage
                {
                    packageId = TestPackageId,
                    version = "1.0.12",
                    channel = "stable",
                    packageSha256 = unityPackageSha256,
                    sourceKind = "unitypackage",
                    downloadAuthorizationUrl = server.BaseUrl + "/api/backstage/access/products/catalog_1/packages/com.yucp.songthing/download",
                };

                AuthorizedVpmPackageInstaller.AuthorizedPackageInstallResult result =
                    AuthorizedVpmPackageInstaller.InstallAuthorizedPackage(
                        projectDir,
                        package,
                        "access-token");

                string importedAssetPath = Path.Combine(projectDir, "Assets", "SongThing", "Marker.txt");
                string installedPackageJson = Path.Combine(packageDirectory, "package.json");

                Assert.That(File.Exists(installedPackageJson), Is.True);
                Assert.That(File.ReadAllText(installedPackageJson), Does.Contain("\"name\":\"" + TestPackageId + "\""));
                Assert.That(File.Exists(importedAssetPath), Is.True);
                Assert.That(File.ReadAllText(importedAssetPath), Is.EqualTo("imported from authorized unitypackage"));
                Assert.That(File.Exists(importedAssetPath + ".meta"), Is.True);
                Assert.That(result.managedPaths, Has.Member("Assets/SongThing/Marker.txt"));
                Assert.That(result.managedPaths, Has.Member("Assets/SongThing/Marker.txt.meta"));
                Assert.That(server.AuthorizationRequests, Is.EqualTo(1));
                Assert.That(server.DownloadRequests, Is.EqualTo(1));
            }
            finally
            {
                DeleteDirectoryIfPresent(projectDir);
            }
        }

        [Test]
        public void CacheAuthorizedPackageMedia_DownloadsMediaOutsideInstalledShim()
        {
            string projectDir = null;
            try
            {
                projectDir = CreateTemporaryProjectDirectory();
                byte[] iconBytes = Encoding.UTF8.GetBytes("icon-bytes");
                byte[] bannerBytes = Encoding.UTF8.GetBytes("banner-bytes");
                string iconSha256 = ComputeSha256(iconBytes);
                string bannerSha256 = ComputeSha256(bannerBytes);

                using var server = new LocalPackageDownloadServer(
                    Array.Empty<byte>(),
                    new string('0', 64),
                    iconBytes,
                    bannerBytes);
                var package = new UpdateDeliveryService.AliasInstallPlanPackage
                {
                    packageId = TestPackageId,
                    version = "1.0.12",
                    channel = "stable",
                    media = new AliasPackageMediaSet
                    {
                        icon = new AliasPackageMediaDescriptor
                        {
                            kind = "icon",
                            downloadUrl = server.BaseUrl + "/api/backstage/access/products/catalog_1/packages/com.yucp.songthing/media/icon",
                            contentType = "image/png",
                            byteSize = iconBytes.Length,
                            sha256 = iconSha256,
                        },
                        banner = new AliasPackageMediaDescriptor
                        {
                            kind = "banner",
                            downloadUrl = server.BaseUrl + "/api/backstage/access/products/catalog_1/packages/com.yucp.songthing/media/banner",
                            contentType = "image/webp",
                            byteSize = bannerBytes.Length,
                            sha256 = bannerSha256,
                        },
                    },
                };

                UpdateDeliveryService.CacheAuthorizedPackageMedia(
                    projectDir,
                    server.BaseUrl,
                    package,
                    "access-token");

                Assert.That(package.media.icon.localPath, Is.EqualTo("Packages/yucp.installed-packages/Media/com.yucp.songthing/1.0.12/icon.png"));
                Assert.That(package.media.banner.localPath, Is.EqualTo("Packages/yucp.installed-packages/Media/com.yucp.songthing/1.0.12/banner.webp"));
                Assert.That(
                    File.ReadAllBytes(Path.Combine(projectDir, package.media.icon.localPath.Replace('/', Path.DirectorySeparatorChar))),
                    Is.EqualTo(iconBytes));
                Assert.That(
                    File.ReadAllBytes(Path.Combine(projectDir, package.media.banner.localPath.Replace('/', Path.DirectorySeparatorChar))),
                    Is.EqualTo(bannerBytes));
                Assert.That(server.IconMediaRequests, Is.EqualTo(1));
                Assert.That(server.BannerMediaRequests, Is.EqualTo(1));
                Assert.That(server.CapturedMediaAuthorizationHeader, Is.EqualTo("Bearer access-token"));
            }
            finally
            {
                DeleteDirectoryIfPresent(projectDir);
            }
        }

        private static string CreateTemporaryProjectDirectory()
        {
            string projectDir = Path.Combine(Path.GetTempPath(), $"yucp-authorized-vpm-install-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(projectDir, "Packages"));
            Directory.CreateDirectory(Path.Combine(projectDir, "Assets"));
            return projectDir;
        }

        private static byte[] CreateVpmPackageZip(
            string packageId,
            string version,
            params (string path, string contents)[] entries)
        {
            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteZipEntry(
                    archive,
                    "package.json",
                    "{\"name\":\"" + packageId + "\",\"version\":\"" + version + "\",\"displayName\":\"Song Thing\"}");

                foreach ((string path, string contents) in entries)
                {
                    WriteZipEntry(archive, path, contents);
                }
            }

            return output.ToArray();
        }

        private static byte[] CreateUnityPackage(params (string path, string contents)[] entries)
        {
            using var tarOutput = new MemoryStream();
            foreach ((string path, string contents) in entries)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(contents);
                byte[] header = BuildTarHeader(path.Replace('\\', '/'), bytes.Length);
                tarOutput.Write(header, 0, header.Length);
                tarOutput.Write(bytes, 0, bytes.Length);
                int remainder = bytes.Length % 512;
                if (remainder != 0)
                {
                    byte[] padding = new byte[512 - remainder];
                    tarOutput.Write(padding, 0, padding.Length);
                }
            }

            tarOutput.Write(new byte[1024], 0, 1024);

            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                byte[] tarBytes = tarOutput.ToArray();
                gzip.Write(tarBytes, 0, tarBytes.Length);
            }

            return compressed.ToArray();
        }

        private static byte[] BuildTarHeader(string path, int size)
        {
            byte[] header = new byte[512];
            WriteAscii(header, 0, 100, path);
            WriteOctal(header, 100, 8, 0x1a4);
            WriteOctal(header, 108, 8, 0);
            WriteOctal(header, 116, 8, 0);
            WriteOctal(header, 124, 12, size);
            WriteOctal(header, 136, 12, 0);
            for (int index = 148; index < 156; index++)
            {
                header[index] = 0x20;
            }

            header[156] = (byte)'0';
            WriteAscii(header, 257, 6, "ustar");
            WriteAscii(header, 263, 2, "00");

            int checksum = 0;
            foreach (byte value in header)
            {
                checksum += value;
            }

            WriteAscii(header, 148, 6, Convert.ToString(checksum, 8).PadLeft(6, '0'));
            header[154] = 0;
            header[155] = 0x20;
            return header;
        }

        private static void WriteAscii(byte[] target, int offset, int length, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
            Array.Copy(bytes, 0, target, offset, Math.Min(length, bytes.Length));
        }

        private static void WriteOctal(byte[] target, int offset, int length, int value)
        {
            string encoded = Convert.ToString(value, 8).PadLeft(length - 1, '0');
            WriteAscii(target, offset, length - 1, encoded);
            target[offset + length - 1] = 0;
        }

        private static void WriteZipEntry(ZipArchive archive, string path, string contents)
        {
            ZipArchiveEntry entry = archive.CreateEntry(path.Replace('\\', '/'), CompressionLevel.Optimal);
            using Stream stream = entry.Open();
            byte[] bytes = Encoding.UTF8.GetBytes(contents);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using var sha256 = SHA256.Create();
            return BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void DeleteDirectoryIfPresent(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private sealed class LocalPackageDownloadServer : IDisposable
        {
            private readonly byte[] _zipBytes;
            private readonly string _zipSha256;
            private readonly byte[] _iconBytes;
            private readonly byte[] _bannerBytes;
            private readonly string _sourceKind;
            private readonly string _contentType;
            private readonly string _deliveryName;
            private readonly string _downloadPath;
            private readonly HttpListener _listener;
            private readonly Task _listenTask;

            internal LocalPackageDownloadServer(
                byte[] zipBytes,
                string zipSha256,
                byte[] iconBytes = null,
                byte[] bannerBytes = null,
                string sourceKind = "zip",
                string contentType = "application/zip",
                string deliveryName = "vrc-get-com.yucp.songthing-1.0.12.zip",
                string downloadPath = "/downloads/vrc-get-com.yucp.songthing-1.0.12.zip")
            {
                _zipBytes = zipBytes;
                _zipSha256 = zipSha256;
                _iconBytes = iconBytes;
                _bannerBytes = bannerBytes;
                _sourceKind = sourceKind;
                _contentType = contentType;
                _deliveryName = deliveryName;
                _downloadPath = downloadPath;

                int port = FindFreePort();
                BaseUrl = $"http://127.0.0.1:{port}";
                _listener = new HttpListener();
                _listener.Prefixes.Add(BaseUrl + "/");
                _listener.Start();
                _listenTask = Task.Run(ListenAsync);
            }

            internal string BaseUrl { get; }
            internal string CapturedAuthorizationHeader { get; private set; }
            internal string CapturedMediaAuthorizationHeader { get; private set; }
            internal string CapturedAuthorizationBody { get; private set; }
            internal int AuthorizationRequests { get; private set; }
            internal int DownloadRequests { get; private set; }
            internal int IconMediaRequests { get; private set; }
            internal int BannerMediaRequests { get; private set; }

            public void Dispose()
            {
                try
                {
                    _listener.Stop();
                    _listener.Close();
                }
                catch
                {
                }

                try
                {
                    _listenTask.Wait(TimeSpan.FromSeconds(1));
                }
                catch
                {
                }
            }

            private async Task ListenAsync()
            {
                while (_listener.IsListening)
                {
                    try
                    {
                        HttpListenerContext context = await _listener.GetContextAsync();
                        await HandleAsync(context);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (HttpListenerException)
                    {
                        return;
                    }
                }
            }

            private async Task HandleAsync(HttpListenerContext context)
            {
                string path = context.Request.Url.AbsolutePath;
                if (context.Request.HttpMethod == "POST" &&
                    string.Equals(
                        path,
                        "/api/backstage/access/products/catalog_1/packages/com.yucp.songthing/download",
                        StringComparison.OrdinalIgnoreCase))
                {
                    AuthorizationRequests++;
                    CapturedAuthorizationHeader = context.Request.Headers["Authorization"];
                    using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                    {
                        CapturedAuthorizationBody = await reader.ReadToEndAsync();
                    }

                    await WriteJsonAsync(
                        context,
                        "{"
                        + "\"downloadUrl\":\"" + BaseUrl + _downloadPath + "\","
                        + "\"packageSha256\":\"" + _zipSha256 + "\","
                        + "\"sourceKind\":\"" + _sourceKind + "\","
                        + "\"version\":\"1.0.12\","
                        + "\"channel\":\"stable\","
                        + "\"contentType\":\"" + _contentType + "\","
                        + "\"deliveryName\":\"" + _deliveryName + "\""
                        + "}");
                    return;
                }

                if (context.Request.HttpMethod == "GET" &&
                    string.Equals(path, _downloadPath, StringComparison.OrdinalIgnoreCase))
                {
                    DownloadRequests++;
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = _contentType;
                    context.Response.ContentLength64 = _zipBytes.LongLength;
                    await context.Response.OutputStream.WriteAsync(_zipBytes, 0, _zipBytes.Length);
                    context.Response.Close();
                    return;
                }

                if (context.Request.HttpMethod == "GET" &&
                    string.Equals(path, "/api/backstage/access/products/catalog_1/packages/com.yucp.songthing/media/icon", StringComparison.OrdinalIgnoreCase))
                {
                    IconMediaRequests++;
                    CapturedMediaAuthorizationHeader = context.Request.Headers["Authorization"];
                    await WriteBytesAsync(context, _iconBytes ?? Array.Empty<byte>(), "image/png");
                    return;
                }

                if (context.Request.HttpMethod == "GET" &&
                    string.Equals(path, "/api/backstage/access/products/catalog_1/packages/com.yucp.songthing/media/banner", StringComparison.OrdinalIgnoreCase))
                {
                    BannerMediaRequests++;
                    CapturedMediaAuthorizationHeader = context.Request.Headers["Authorization"];
                    await WriteBytesAsync(context, _bannerBytes ?? Array.Empty<byte>(), "image/webp");
                    return;
                }

                context.Response.StatusCode = 404;
                context.Response.Close();
            }

            private static async Task WriteJsonAsync(HttpListenerContext context, string json)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                context.Response.Close();
            }

            private static async Task WriteBytesAsync(HttpListenerContext context, byte[] bytes, string contentType)
            {
                context.Response.StatusCode = 200;
                context.Response.ContentType = contentType;
                context.Response.ContentLength64 = bytes.LongLength;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                context.Response.Close();
            }

            private static int FindFreePort()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
        }
    }
}
