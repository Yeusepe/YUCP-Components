using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class AuthorizedVpmPackageInstaller
    {
        private const int FriendlyWindowsPathLimit = 240;
        private const int WebRequestTimeoutMilliseconds = 60000;
        private const string ZipPackageSourceKind = "zip";
        private const string UnityPackageSourceKind = "unitypackage";
        private const string RepoTokenHeaderName = "X-YUCP-Repo-Token";

        private sealed class ResolvedPackageDownload
        {
            public string downloadUrl;
            public string resolvedVersion;
            public string expectedArchiveHash;
            public string deliverySourceKind;
            public Dictionary<string, string> requestHeaders;
        }

        private sealed class TimeoutWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                if (request == null)
                {
                    return null;
                }

                request.Timeout = WebRequestTimeoutMilliseconds;
                if (request is HttpWebRequest httpRequest)
                {
                    httpRequest.ReadWriteTimeout = WebRequestTimeoutMilliseconds;
                }

                return request;
            }
        }

        public static void InstallAuthorizedPackage(
            string projectDir,
            UpdateDeliveryService.AliasInstallPlanPackage package,
            string accessToken)
        {
            if (package == null)
            {
                throw new InvalidOperationException("Authorized package install plan entry is missing.");
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("Package download access token is missing.");
            }

            if (!IsSafePackageName(package.packageId))
            {
                throw new InvalidDataException($"Invalid package name '{package.packageId}'.");
            }

            string sourceKind = NormalizeSourceKind(package.sourceKind);
            if (sourceKind == null)
            {
                throw new InvalidOperationException(
                    $"Package '{package.packageId}' declared an unsupported source kind '{package.sourceKind ?? "<missing>"}'.");
            }

            string expectedHash = NormalizeSha256(package.packageSha256);
            if (string.IsNullOrEmpty(expectedHash))
            {
                throw new InvalidOperationException(
                    $"Package '{package.packageId}' did not include a valid package SHA-256 digest.");
            }

            JObject authorizationResponse = RequestAuthorizedPackageDownload(package, accessToken);
            string downloadUrl = authorizationResponse["downloadUrl"]?.ToString();
            if (!IsTrustedWebUrl(downloadUrl))
            {
                throw new InvalidOperationException("Package download authorization returned an untrusted URL.");
            }

            string responseHash = NormalizeSha256(authorizationResponse["packageSha256"]?.ToString());
            if (string.IsNullOrEmpty(responseHash))
            {
                throw new InvalidOperationException("Package download authorization did not include a valid SHA-256 digest.");
            }

            if (!string.Equals(responseHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Package download authorization hash did not match the install plan for {package.packageId}@{package.version}.");
            }

            string responseSourceKind = NormalizeSourceKind(authorizationResponse["sourceKind"]?.ToString());
            if (!string.IsNullOrEmpty(responseSourceKind) &&
                !string.Equals(responseSourceKind, sourceKind, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Package download authorization source kind did not match the install plan for {package.packageId}@{package.version}.");
            }

            var resolution = new ResolvedPackageDownload
            {
                downloadUrl = downloadUrl,
                resolvedVersion = string.IsNullOrWhiteSpace(authorizationResponse["version"]?.ToString())
                    ? package.version?.Trim()
                    : authorizationResponse["version"]?.ToString()?.Trim(),
                expectedArchiveHash = expectedHash,
                deliverySourceKind = sourceKind,
            };

            InstallResolvedPackage(projectDir, package.packageId, resolution);
        }

        public static void InstallPackage(
            string projectDir,
            string repositoryUrl,
            string packageName,
            string version,
            string installPlanSha256)
        {
            if (string.IsNullOrWhiteSpace(projectDir))
            {
                throw new InvalidOperationException("Project directory is missing.");
            }

            if (!IsSafePackageName(packageName))
            {
                throw new InvalidDataException($"Invalid package name '{packageName}'.");
            }

            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidDataException($"Package '{packageName}' is missing a version.");
            }

            if (!TryResolvePackageDownload(
                    repositoryUrl,
                    packageName,
                    version.Trim(),
                    installPlanSha256,
                    out ResolvedPackageDownload resolution))
            {
                throw new InvalidOperationException(
                    $"Could not resolve authorized package '{packageName}' version '{version}' from the repository catalog.");
            }

            InstallResolvedPackage(projectDir, packageName, resolution);
        }

        private static void InstallResolvedPackage(
            string projectDir,
            string packageName,
            ResolvedPackageDownload resolution)
        {
            string workspaceRoot = Path.Combine(projectDir, ".yucp-dvi", "AuthorizedVpmInstall");
            Directory.CreateDirectory(workspaceRoot);
            string tempDownloadPath = Path.Combine(workspaceRoot, $"{Guid.NewGuid():N}.zip");
            string stagingDirectory = Path.Combine(workspaceRoot, $"{Guid.NewGuid():N}");

            try
            {
                using (var downloadClient = new TimeoutWebClient())
                {
                    downloadClient.Headers.Add(HttpRequestHeader.UserAgent, "YUCP-Importer/1.0");
                    downloadClient.Headers.Add(HttpRequestHeader.Accept, "application/octet-stream");

                    if (resolution.requestHeaders != null)
                    {
                        foreach (KeyValuePair<string, string> header in resolution.requestHeaders)
                        {
                            downloadClient.Headers[header.Key] = header.Value;
                        }
                    }

                    downloadClient.DownloadFile(resolution.downloadUrl, tempDownloadPath);
                }

                string actualArchiveHash = NormalizeSha256(ComputeFileSha256(tempDownloadPath));
                if (!string.Equals(actualArchiveHash, resolution.expectedArchiveHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"SHA-256 mismatch for {packageName}@{resolution.resolvedVersion}. Expected {resolution.expectedArchiveHash}, got {actualArchiveHash}.");
                }

                ExtractDownloadedPackageToDirectorySafely(
                    tempDownloadPath,
                    resolution.deliverySourceKind,
                    stagingDirectory,
                    packageName);

                string stagedPackageJsonPath = Path.Combine(stagingDirectory, "package.json");
                if (!File.Exists(stagedPackageJsonPath))
                {
                    throw new InvalidDataException(
                        $"Downloaded archive for {packageName}@{resolution.resolvedVersion} did not contain package.json.");
                }

                JObject stagedPackageJson = JObject.Parse(File.ReadAllText(stagedPackageJsonPath));
                string stagedPackageName = stagedPackageJson["name"]?.ToString();
                string stagedPackageVersion = stagedPackageJson["version"]?.ToString();

                if (!string.Equals(stagedPackageName, packageName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Downloaded archive name mismatch. Expected {packageName}, got {stagedPackageName ?? "<missing>"}.");
                }

                if (!string.Equals(stagedPackageVersion, resolution.resolvedVersion, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Downloaded archive version mismatch. Expected {resolution.resolvedVersion}, got {stagedPackageVersion ?? "<missing>"}.");
                }

                string packageDestination = GetValidatedPackageDestination(projectDir, packageName);
                MoveDirectoryIntoPlace(stagingDirectory, packageDestination);
                stagingDirectory = null;
            }
            finally
            {
                TryDeleteFile(tempDownloadPath);
                TryDeleteDirectory(stagingDirectory);
            }
        }

        private static JObject RequestAuthorizedPackageDownload(
            UpdateDeliveryService.AliasInstallPlanPackage package,
            string accessToken)
        {
            if (!IsTrustedWebUrl(package.downloadAuthorizationUrl))
            {
                throw new InvalidOperationException("Package download authorization endpoint is not trusted.");
            }

            var requestBody = new JObject
            {
                ["version"] = string.IsNullOrWhiteSpace(package.version) ? null : package.version.Trim(),
                ["channel"] = string.IsNullOrWhiteSpace(package.channel) ? null : package.channel.Trim(),
            };

            using var client = new TimeoutWebClient();
            client.Headers.Add(HttpRequestHeader.UserAgent, "YUCP-Importer/1.0");
            client.Headers.Add(HttpRequestHeader.Accept, "application/json");
            client.Headers.Add(HttpRequestHeader.Authorization, $"Bearer {accessToken}");
            client.Headers.Add(HttpRequestHeader.ContentType, "application/json");

            string responseText = client.UploadString(
                package.downloadAuthorizationUrl,
                "POST",
                requestBody.ToString(Newtonsoft.Json.Formatting.None));
            JObject response = JObject.Parse(responseText);
            if (string.IsNullOrWhiteSpace(response["downloadUrl"]?.ToString()))
            {
                throw new InvalidOperationException("Package download authorization returned an invalid response.");
            }

            return response;
        }

        private static bool TryResolvePackageDownload(
            string repositoryUrl,
            string packageName,
            string version,
            string installPlanSha256,
            out ResolvedPackageDownload resolution)
        {
            resolution = null;

            if (!IsTrustedWebUrl(repositoryUrl))
            {
                throw new InvalidOperationException($"Repository URL '{repositoryUrl}' is not trusted.");
            }

            using var repoClient = new TimeoutWebClient();
            repoClient.Headers.Add(HttpRequestHeader.UserAgent, "YUCP-Importer/1.0");
            repoClient.Headers.Add(HttpRequestHeader.Accept, "application/json");

            JObject repoData = JObject.Parse(repoClient.DownloadString(repositoryUrl));
            JObject versionMetadata = repoData["packages"]?[packageName]?["versions"]?[version] as JObject;
            if (versionMetadata == null)
            {
                return false;
            }

            string deliveryMode = versionMetadata["yucpDeliveryMode"]?.ToString();
            if (!string.Equals(deliveryMode, UpdateDeliveryService.RepoTokenVpmDeliveryMode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Repository catalog did not declare authorized delivery for {packageName}@{version}.");
            }

            string sourceKind = versionMetadata["yucpDeliverySourceKind"]?.ToString();
            if (!string.Equals(sourceKind, ZipPackageSourceKind, StringComparison.Ordinal) &&
                !string.Equals(sourceKind, UnityPackageSourceKind, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Repository catalog declared an unsupported package source kind for {packageName}@{version}.");
            }

            string downloadUrl = ResolveDownloadUrl(repositoryUrl, versionMetadata["url"]?.ToString());
            if (!IsTrustedWebUrl(downloadUrl))
            {
                throw new InvalidOperationException(
                    $"Repository catalog declared an untrusted package URL for {packageName}@{version}.");
            }

            string repoHash = NormalizeSha256(versionMetadata["zipSHA256"]?.ToString())
                ?? NormalizeSha256(versionMetadata["sha256"]?.ToString());
            string planHash = NormalizeSha256(installPlanSha256);
            string expectedHash = planHash ?? repoHash;
            if (string.IsNullOrEmpty(expectedHash))
            {
                throw new InvalidOperationException(
                    $"Repository catalog did not provide a SHA-256 for {packageName}@{version}.");
            }

            if (!string.IsNullOrEmpty(repoHash) &&
                !string.IsNullOrEmpty(planHash) &&
                !string.Equals(repoHash, planHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Repository catalog hash did not match the install plan for {packageName}@{version}.");
            }

            Dictionary<string, string> requestHeaders = ReadManifestRequestHeaders(versionMetadata);
            if (requestHeaders == null ||
                !requestHeaders.TryGetValue(RepoTokenHeaderName, out string repoToken) ||
                string.IsNullOrWhiteSpace(repoToken))
            {
                throw new InvalidOperationException(
                    $"Repository catalog did not provide an authorized download token for {packageName}@{version}.");
            }

            resolution = new ResolvedPackageDownload
            {
                downloadUrl = downloadUrl,
                resolvedVersion = version,
                expectedArchiveHash = expectedHash,
                deliverySourceKind = sourceKind,
                requestHeaders = requestHeaders,
            };
            return true;
        }

        private static Dictionary<string, string> ReadManifestRequestHeaders(JObject versionMetadata)
        {
            if (!(versionMetadata?["headers"] is JObject headersObject))
            {
                return null;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (JProperty property in headersObject.Properties())
            {
                string headerName = property.Name?.Trim();
                string headerValue = property.Value?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(headerName) || string.IsNullOrWhiteSpace(headerValue))
                {
                    continue;
                }

                headers[headerName] = headerValue;
            }

            return headers.Count > 0 ? headers : null;
        }

        private static string ResolveDownloadUrl(string repositoryUrl, string candidateUrl)
        {
            if (string.IsNullOrWhiteSpace(candidateUrl))
            {
                return null;
            }

            if (Uri.TryCreate(candidateUrl, UriKind.Absolute, out Uri absoluteUri))
            {
                return absoluteUri.ToString();
            }

            if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out Uri repositoryUri))
            {
                return null;
            }

            return new Uri(repositoryUri, candidateUrl).ToString();
        }

        private static bool IsTrustedWebUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            if (uri.Scheme == Uri.UriSchemeHttps)
            {
                return true;
            }

            return uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        }

        private static bool IsSafePackageName(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName) ||
                packageName.Contains("/") ||
                packageName.Contains("\\") ||
                packageName.Contains(":"))
            {
                return false;
            }

            string[] segments = packageName.Split('.');
            if (segments.Length < 2)
            {
                return false;
            }

            foreach (string segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
                {
                    return false;
                }

                foreach (char c in segment)
                {
                    if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static string GetValidatedPackageDestination(string projectDir, string packageName)
        {
            string packagesRoot = Path.GetFullPath(Path.Combine(projectDir, "Packages"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string destination = Path.GetFullPath(Path.Combine(packagesRoot, packageName));
            if (!destination.StartsWith(packagesRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Package '{packageName}' resolves outside the Packages directory.");
            }

            return destination;
        }

        private static string NormalizeSha256(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                return null;
            }

            string normalized = hash.Trim().Replace("-", string.Empty).ToUpperInvariant();
            return normalized.Length == 64 && normalized.All(Uri.IsHexDigit)
                ? normalized
                : null;
        }

        private static string NormalizeSourceKind(string sourceKind)
        {
            if (string.Equals(sourceKind, ZipPackageSourceKind, StringComparison.OrdinalIgnoreCase))
            {
                return ZipPackageSourceKind;
            }

            if (string.Equals(sourceKind, UnityPackageSourceKind, StringComparison.OrdinalIgnoreCase))
            {
                return UnityPackageSourceKind;
            }

            return null;
        }

        private static string ComputeFileSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static string GetValidatedExtractionPath(string extractionRoot, string entryName, string sourceDescription)
        {
            string normalizedRoot = Path.GetFullPath(extractionRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalizedEntry = entryName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, normalizedEntry));
            if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Archive entry '{entryName}' from '{sourceDescription}' escapes '{normalizedRoot}'.");
            }

            return candidate;
        }

        private static void ExtractDownloadedPackageToDirectorySafely(
            string downloadPath,
            string sourceKind,
            string destinationDirectory,
            string packageLabel)
        {
            if (string.Equals(sourceKind, UnityPackageSourceKind, StringComparison.Ordinal))
            {
                ExtractUnityPackageToDirectorySafely(downloadPath, destinationDirectory, packageLabel);
                return;
            }

            ExtractZipToDirectorySafely(downloadPath, destinationDirectory, packageLabel);
        }

        private static void ExtractZipToDirectorySafely(string zipPath, string destinationDirectory, string packageLabel)
        {
            EnsureCreatorFriendlyPathLength(destinationDirectory, packageLabel);
            Directory.CreateDirectory(destinationDirectory);

            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.FullName))
                {
                    continue;
                }

                string destinationPath = GetValidatedExtractionPath(destinationDirectory, entry.FullName, zipPath);
                EnsureCreatorFriendlyPathLength(destinationPath, packageLabel);
                bool isDirectory = string.IsNullOrEmpty(entry.Name) &&
                    (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                     entry.FullName.EndsWith("\\", StringComparison.Ordinal));
                if (isDirectory)
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                string parentDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parentDirectory))
                {
                    EnsureCreatorFriendlyPathLength(parentDirectory, packageLabel);
                    Directory.CreateDirectory(parentDirectory);
                }

                using Stream input = entry.Open();
                using Stream output = File.Create(destinationPath);
                input.CopyTo(output);
            }
        }

        private static void ExtractUnityPackageToDirectorySafely(
            string unityPackagePath,
            string destinationDirectory,
            string packageLabel)
        {
            EnsureCreatorFriendlyPathLength(destinationDirectory, packageLabel);
            Directory.CreateDirectory(destinationDirectory);

            using var fileStream = File.OpenRead(unityPackagePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            byte[] header = new byte[512];
            while (TryReadTarHeader(gzipStream, header))
            {
                string entryName = ReadTarString(header, 0, 100);
                if (string.IsNullOrEmpty(entryName))
                {
                    continue;
                }

                long entrySize = ReadTarOctal(header, 124, 12);
                char entryType = (char)header[156];
                string destinationPath = GetValidatedExtractionPath(destinationDirectory, entryName, unityPackagePath);
                bool isDirectory = entryType == '5' || entryName.EndsWith("/", StringComparison.Ordinal);
                if (isDirectory)
                {
                    EnsureCreatorFriendlyPathLength(destinationPath, packageLabel);
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                EnsureCreatorFriendlyPathLength(destinationPath, packageLabel);
                string parentDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parentDirectory))
                {
                    EnsureCreatorFriendlyPathLength(parentDirectory, packageLabel);
                    Directory.CreateDirectory(parentDirectory);
                }

                using Stream output = File.Create(destinationPath);
                CopyTarEntryContents(gzipStream, output, entrySize);
                SkipTarPadding(gzipStream, entrySize);
            }
        }

        private static bool TryReadTarHeader(Stream stream, byte[] header)
        {
            int totalRead = 0;
            while (totalRead < header.Length)
            {
                int bytesRead = stream.Read(header, totalRead, header.Length - totalRead);
                if (bytesRead == 0)
                {
                    if (totalRead == 0)
                    {
                        return false;
                    }

                    throw new InvalidDataException("Authorized unitypackage archive ended before the next TAR header was complete.");
                }

                totalRead += bytesRead;
            }

            return header.Any(value => value != 0);
        }

        private static string ReadTarString(byte[] header, int offset, int length)
        {
            return Encoding.ASCII.GetString(header, offset, length).Trim('\0', ' ');
        }

        private static long ReadTarOctal(byte[] header, int offset, int length)
        {
            string rawValue = ReadTarString(header, offset, length);
            if (string.IsNullOrEmpty(rawValue))
            {
                return 0;
            }

            try
            {
                return Convert.ToInt64(rawValue, 8);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException(
                    $"Authorized unitypackage TAR header contained an invalid size field '{rawValue}'.",
                    ex);
            }
        }

        private static void CopyTarEntryContents(Stream input, Stream output, long bytesToCopy)
        {
            byte[] buffer = new byte[81920];
            long remaining = bytesToCopy;
            while (remaining > 0)
            {
                int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0)
                {
                    throw new InvalidDataException("Authorized unitypackage archive ended before a TAR entry was fully read.");
                }

                output.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private static void SkipTarPadding(Stream input, long entrySize)
        {
            long remainder = entrySize % 512;
            if (remainder == 0)
            {
                return;
            }

            long padding = 512 - remainder;
            byte[] buffer = new byte[512];
            while (padding > 0)
            {
                int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, padding));
                if (read == 0)
                {
                    throw new InvalidDataException("Authorized unitypackage archive ended before TAR padding was fully skipped.");
                }

                padding -= read;
            }
        }

        private static void MoveDirectoryIntoPlace(string sourceDirectory, string destinationDirectory)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    Directory.Move(sourceDirectory, destinationDirectory);
                    return;
                }
                catch (Exception) when (attempt == 0 && (Directory.Exists(destinationDirectory) || File.Exists(destinationDirectory)))
                {
                    if (Directory.Exists(destinationDirectory))
                    {
                        Directory.Delete(destinationDirectory, true);
                    }
                    else if (File.Exists(destinationDirectory))
                    {
                        File.Delete(destinationDirectory);
                    }
                }
            }

            Directory.Move(sourceDirectory, destinationDirectory);
        }

        private static void EnsureCreatorFriendlyPathLength(string path, string packageLabel)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (path.Length < FriendlyWindowsPathLimit)
            {
                return;
            }

            throw new IOException(
                $"Windows could not unpack {packageLabel} because this project is stored in a very long folder path. Move the project to a shorter folder, such as C:\\Unity\\MyAvatar, and try again.");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }
    }
}
