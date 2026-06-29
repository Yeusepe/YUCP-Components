using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    public static class UpdateDeliveryService
    {
        internal const string AliasInstallPlanKind = "alias-install-plan-v1";
        internal const string ServerAuthorizedInstallStrategy = "server-authorized";
        internal const string RepoTokenVpmDeliveryMode = "repo-token-vpm-v1";

        private static readonly HttpClient s_http = new HttpClient();
        private static readonly string[] AliasDeliveryRequiredScopes = { "products:read" };

        private const string ReauthenticationMessage =
            "Your YUCP Creator Identity session does not include package delivery access.\n" +
            "Please sign in again and then retry the install.";

        [Serializable]
        public sealed class AliasInstallPlan
        {
            public string kind;
            public long expiresAt;
            public string creatorName;
            public string creatorRepoRef;
            public string productRef;
            public string title;
            public string thumbnailUrl;
            public string repositoryUrl;
            public AliasInstallPlanPackage[] packages = Array.Empty<AliasInstallPlanPackage>();
        }

        [Serializable]
        public sealed class AliasInstallPlanPackage
        {
            public string packageId;
            public string displayName;
            public string version;
            public string channel;
            public string zipSha256;
            public string packageSha256;
            public string sourceKind;
            public string downloadAuthorizationUrl;
            // Server-minted, machine-bound license token (aud=yucp-license-gate) bound to the
            // buyer's canonical licenseSubject. Used to authorize per-buyer coupling for this
            // install. Treat as a secret: never log it or surface it in preview/confirmation text.
            public string licenseToken;
            public AliasPackageMediaSet media = new AliasPackageMediaSet();
            public AliasPackageContract aliasContract;
            public ImporterDeliveryContract importerDelivery;
        }

        [Serializable]
        public sealed class ImporterDeliveryContract
        {
            public string packageInstallStrategy;
            public string repoCatalogDeliveryMode;
            public bool repoCatalogReadOnly;
        }

        [Serializable]
        private sealed class AliasInstallPlanRequest
        {
            public string machineFingerprint;
        }

        [Serializable]
        private sealed class RequestResult
        {
            public long responseCode;
            public string body;
        }

        private sealed class BinaryRequestResult
        {
            public long responseCode;
            public byte[] bytes;
        }

        public static bool TryResolveAuthorizedInstallPlan(
            string serverUrl,
            AliasPackageContract aliasPackage,
            out AliasInstallPlan installPlan,
            out string error)
        {
            installPlan = null;
            error = null;

            try
            {
                installPlan = ResolveAuthorizedInstallPlanAsync(serverUrl, aliasPackage).GetAwaiter().GetResult();
                return installPlan != null;
            }
            catch (ReauthenticationRequiredException)
            {
                CreatorIdentityOAuthService.SignOut();
                error = ReauthenticationMessage;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryResolveAuthorizedInstallPlanForPackage(
            string serverUrl,
            string packageId,
            out AliasInstallPlan installPlan,
            out string error)
        {
            installPlan = null;
            error = null;

            if (string.IsNullOrWhiteSpace(packageId))
            {
                error = "Package ID is missing.";
                return false;
            }

            AliasPackageContract aliasPackage = ResolveInstalledAliasPackage(packageId);
            if (aliasPackage == null)
            {
                error = $"Package '{packageId}' is not an alias package with server-authorized delivery metadata.";
                return false;
            }

            return TryResolveAuthorizedInstallPlan(serverUrl, aliasPackage, out installPlan, out error);
        }

        public static string CheckForUpdate(string packageId, string currentVersion)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return null;
            }

            AliasPackageContract aliasPackage = ResolveInstalledAliasPackage(packageId);
            if (aliasPackage == null)
            {
                return null;
            }

            // Server-authorized alias updates require a buyer-bound install plan request.
            // Passive background polling cannot safely issue that request without an active
            // Creator Identity session and the related product context.
            return null;
        }

        public static void CheckAllUpdates()
        {
            var registry = InstalledPackageRegistry.GetOrCreate();
            var packages = registry.GetAllPackages();

            foreach (var package in packages)
            {
                if (string.IsNullOrEmpty(package.packageId))
                {
                    continue;
                }

                string latestVersion = CheckForUpdate(package.packageId, package.installedVersion);
                package.hasUpdate = !string.IsNullOrEmpty(latestVersion);
                package.latestVersion = package.hasUpdate ? latestVersion : string.Empty;
            }

            registry.Save();
        }

        public static bool TryApplyAuthorizedInstallPlan(
            AliasInstallPlan installPlan,
            out string error)
        {
            error = null;

            try
            {
                ApplyAuthorizedInstallPlan(installPlan);
                return true;
            }
            catch (ReauthenticationRequiredException)
            {
                CreatorIdentityOAuthService.SignOut();
                error = ReauthenticationMessage;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static async Task<AliasInstallPlan> ResolveAuthorizedInstallPlanAsync(
            string serverUrl,
            AliasPackageContract aliasPackage)
        {
            ValidateAliasPackage(aliasPackage);

            string accessToken = await CreatorIdentityOAuthService.GetValidAccessTokenAsync(
                serverUrl,
                AliasDeliveryRequiredScopes).ConfigureAwait(false);
            if (string.IsNullOrEmpty(accessToken))
            {
                throw new ReauthenticationRequiredException();
            }

            Exception lastError = null;
            foreach (string catalogProductId in aliasPackage.catalogProductIds)
            {
                try
                {
                    AliasInstallPlan plan = await GetAliasInstallPlanAsync(serverUrl, accessToken, catalogProductId)
                        .ConfigureAwait(false);
                    ValidateInstallPlan(plan, aliasPackage);
                    return plan;
                }
                catch (Exception ex) when (!(ex is ReauthenticationRequiredException))
                {
                    lastError = ex;
                }
            }

            if (lastError != null)
            {
                throw lastError;
            }

            throw new Exception(
                $"Could not resolve an authorized install plan for alias '{aliasPackage.aliasId}'.");
        }

        private static void ApplyAuthorizedInstallPlan(AliasInstallPlan installPlan)
        {
            ValidateInstallPlanForApply(installPlan);

#if UNITY_INCLUDE_TESTS
            if (UpdateDeliveryServiceTestHooks.ApplyAuthorizedInstallPlanHandler != null)
            {
                UpdateDeliveryServiceTestHooks.ApplyAuthorizedInstallPlanHandler(installPlan);
            }
            else
#endif
            {
                ApplyAuthorizedInstallPlanToProject(installPlan);
            }

            foreach (AliasInstallPlanPackage package in installPlan.packages)
            {
                InstalledPackageInfo installedInfo = BuildInstalledPackageInfo(package, installPlan);
                PersistInstallState(installedInfo);
                RegisterInstalledPackage(installedInfo);
            }
        }

        private static void ValidateInstallPlanForApply(AliasInstallPlan installPlan)
        {
            if (installPlan == null)
            {
                throw new Exception("Alias install plan is missing.");
            }

            if (!string.Equals(installPlan.kind, AliasInstallPlanKind, StringComparison.Ordinal))
            {
                throw new Exception("Alias install plan response used an unexpected contract kind.");
            }

            if (string.IsNullOrWhiteSpace(installPlan.repositoryUrl))
            {
                throw new Exception("Alias install plan response was missing repositoryUrl.");
            }

            if (installPlan.packages == null || installPlan.packages.Length == 0)
            {
                throw new Exception("Alias install plan response did not include any packages.");
            }

            foreach (AliasInstallPlanPackage package in installPlan.packages)
            {
                if (package == null || string.IsNullOrWhiteSpace(package.packageId))
                {
                    throw new Exception("Alias install plan response included an invalid package entry.");
                }

                if (string.IsNullOrWhiteSpace(package.version))
                {
                    throw new Exception(
                        $"Alias install plan for '{package.packageId}' is missing a package version.");
                }

                if (string.IsNullOrWhiteSpace(package.downloadAuthorizationUrl))
                {
                    throw new Exception(
                        $"Alias install plan for '{package.packageId}' is missing a package download authorization URL.");
                }

                if (string.IsNullOrWhiteSpace(package.packageSha256))
                {
                    throw new Exception(
                        $"Alias install plan for '{package.packageId}' is missing a package SHA-256 digest.");
                }

                if (!IsSupportedSourceKind(package.sourceKind))
                {
                    throw new Exception(
                        $"Alias install plan for '{package.packageId}' declared unsupported source kind '{package.sourceKind ?? "<missing>"}'.");
                }
            }
        }

        private static void ApplyAuthorizedInstallPlanToProject(AliasInstallPlan installPlan)
        {
            string projectDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
            if (string.IsNullOrWhiteSpace(projectDir))
            {
                throw new Exception("Could not resolve the current Unity project directory.");
            }

            // Files installed so far across this plan, so a coupling failure on a later package
            // can unwind every package installed in this transaction (fail-closed rollback).
            var installedFilesForRollback = new List<string>();

            foreach (AliasInstallPlanPackage package in installPlan.packages)
            {
                string serverUrl = GetTrustedServerOrigin(
                    package.downloadAuthorizationUrl,
                    installPlan.repositoryUrl);
                string accessToken = CreatorIdentityOAuthService.GetValidAccessTokenAsync(
                    serverUrl,
                    AliasDeliveryRequiredScopes).GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(accessToken))
                {
                    throw new ReauthenticationRequiredException();
                }

                AuthorizedVpmPackageInstaller.AuthorizedPackageInstallResult installResult = null;
#if UNITY_INCLUDE_TESTS
                bool installedThroughHook = false;
                if (UpdateDeliveryServiceTestHooks.AuthorizedPackageInstallerHandler != null)
                {
                    installResult = UpdateDeliveryServiceTestHooks.AuthorizedPackageInstallerHandler(
                        projectDir,
                        serverUrl,
                        package,
                        accessToken);
                    installedThroughHook = true;
                }
                if (!installedThroughHook)
#endif
                {
                    installResult = AuthorizedVpmPackageInstaller.InstallAuthorizedPackage(
                        projectDir,
                        package,
                        accessToken);
                }

                MergeImportedManagedPaths(package, installResult?.managedPaths);
                CacheAuthorizedPackageMedia(
                    projectDir,
                    serverUrl,
                    package,
                    accessToken);

                ApplyPerBuyerCoupling(projectDir, package, installResult, installedFilesForRollback);
            }

            UpdateProjectVpmManifest(projectDir, installPlan);
            Client.Resolve();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Runs the per-buyer protection step for a freshly installed alias/VPM package. Caches the
        /// minted token first (the step authorizes against it), then runs transactionally: any failure
        /// rolls back every package installed so far in this plan and rethrows, leaving the project clean.
        /// </summary>
        private static void ApplyPerBuyerCoupling(
            string projectDir,
            AliasInstallPlanPackage package,
            AuthorizedVpmPackageInstaller.AuthorizedPackageInstallResult installResult,
            List<string> installedFilesForRollback)
        {
            // Cache the server-minted token first; coupling authorizes against it.
            if (!string.IsNullOrWhiteSpace(package.licenseToken))
            {
                LicenseTokenCache.StoreToken(package.packageId, package.licenseToken);
            }

            IReadOnlyList<string> couplingFiles = CollectCouplingFiles(projectDir, package, installResult);
            if (couplingFiles.Count > 0)
            {
                installedFilesForRollback.AddRange(couplingFiles);
            }

            if (couplingFiles.Count == 0)
            {
                return;
            }

            if (!CouplingImportGuard.TryApplyCouplingForFiles(package.packageId, couplingFiles, out string couplingError))
            {
                ImportedAssetRollbackService.TryRollbackImportedAssets(installedFilesForRollback, out _);
                throw new Exception(
                    $"The package protection step could not be completed for '{package.packageId}'. {couplingError}");
            }
        }

        /// <summary>
        /// Resolves the project-relative files installed for a package, used as both the
        /// protection-step inputs and the rollback file set.
        /// </summary>
        private static IReadOnlyList<string> CollectCouplingFiles(
            string projectDir,
            AliasInstallPlanPackage package,
            AuthorizedVpmPackageInstaller.AuthorizedPackageInstallResult installResult)
        {
            if (package == null || string.IsNullOrWhiteSpace(package.packageId))
            {
                return Array.Empty<string>();
            }

            // unitypackage source: the installer already returns the full imported file list.
            if (string.Equals(package.sourceKind, "unitypackage", StringComparison.OrdinalIgnoreCase))
            {
                return installResult?.managedPaths != null
                    ? new List<string>(installResult.managedPaths)
                    : (IReadOnlyList<string>)Array.Empty<string>();
            }

            // zip source: the package was extracted to Packages/{packageId}; enumerate it.
            string packageRoot = System.IO.Path.Combine(projectDir, "Packages", package.packageId);
            if (!System.IO.Directory.Exists(packageRoot))
            {
                return Array.Empty<string>();
            }

            var files = new List<string>();
            foreach (string fullPath in System.IO.Directory.GetFiles(
                         packageRoot, "*", System.IO.SearchOption.AllDirectories))
            {
                string relativeWithinPackage = fullPath
                    .Substring(packageRoot.Length)
                    .Replace('\\', '/')
                    .TrimStart('/');
                files.Add($"Packages/{package.packageId}/{relativeWithinPackage}");
            }

            return files;
        }

        private static void UpdateProjectVpmManifest(string projectDir, AliasInstallPlan installPlan)
        {
            string manifestPath = System.IO.Path.Combine(projectDir, "Packages", "vpm-manifest.json");
            JObject manifest = System.IO.File.Exists(manifestPath)
                ? JObject.Parse(System.IO.File.ReadAllText(manifestPath))
                : new JObject();

            JObject dependencies = manifest["dependencies"] as JObject ?? new JObject();
            JObject locked = manifest["locked"] as JObject ?? new JObject();
            manifest["dependencies"] = dependencies;
            manifest["locked"] = locked;

            foreach (AliasInstallPlanPackage package in installPlan.packages)
            {
                string version = package.version?.Trim();
                dependencies[package.packageId] = new JObject
                {
                    ["version"] = version,
                };

                locked[package.packageId] = new JObject
                {
                    ["version"] = version,
                    ["dependencies"] = new JObject(),
                };
            }

            System.IO.File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
        }

        private static InstalledPackageInfo BuildInstalledPackageInfo(
            AliasInstallPlanPackage package,
            AliasInstallPlan installPlan)
        {
            PackageMetadata metadata = LoadInstalledPackageMetadata(package.packageId);
            if (metadata == null)
            {
                throw new Exception(
                    $"Could not load installed alias metadata for '{package.packageId}' after applying the VPM install plan.");
            }

            metadata.aliasPackage = MergeAliasPackageContract(metadata.aliasPackage, package);
            ApplyCachedPackageMedia(metadata, package.media);

            var installedInfo = InstalledPackageInfoFactory.Create(
                metadata,
                package.packageId ?? string.Empty,
                package.packageSha256 ?? package.zipSha256 ?? string.Empty,
                installPlan.creatorRepoRef ?? string.Empty,
                true,
                ResolveManagedPackagePaths(metadata.aliasPackage, package));
            installedInfo.installedVersion = package.version ?? installedInfo.installedVersion ?? string.Empty;
            installedInfo.version = installedInfo.installedVersion;
            installedInfo.packageName = !string.IsNullOrWhiteSpace(installedInfo.packageName)
                ? installedInfo.packageName
                : package.displayName ?? package.packageId ?? string.Empty;
            installedInfo.SetInstalledDateTime(DateTime.Now);
            return installedInfo;
        }

        /// <summary>
        /// Builds display metadata for an alias package from an already-resolved authorized install
        /// plan, WITHOUT installing anything. Used to show real package details (title, version,
        /// creator) before the user confirms the install. Media textures are attached separately via
        /// <see cref="TryAttachPlanMedia"/>. Returns null if the plan carries no usable package.
        /// </summary>
        internal static PackageMetadata BuildPreviewMetadataFromPlan(
            AliasInstallPlan plan,
            AliasPackageContract requested)
        {
            if (plan == null)
            {
                return null;
            }

            AliasInstallPlanPackage package = SelectPlanPackage(plan, requested);
            if (package == null)
            {
                return null;
            }

            string displayName = FirstNonEmpty(
                package.displayName,
                package.aliasContract?.packageDisplayName,
                requested?.packageDisplayName,
                plan.title,
                package.packageId);

            var metadata = new PackageMetadata(string.IsNullOrWhiteSpace(displayName) ? "Package" : displayName)
            {
                version = FirstNonEmpty(package.version, requested?.packageVersion, requested?.minImporterVersion) ?? string.Empty,
                author = plan.creatorName ?? string.Empty,
                tagline = plan.title ?? string.Empty,
                aliasPackage = MergeAliasPackageContract(requested?.Clone(), package),
            };

            return metadata;
        }

        /// <summary>
        /// Best-effort download of the alias package's icon/banner into in-memory textures for the
        /// pre-install preview. Never throws and never writes to the project; failures simply leave
        /// the corresponding texture unset.
        /// </summary>
        internal static void TryAttachPlanMedia(
            PackageMetadata metadata,
            AliasInstallPlan plan,
            AliasPackageContract requested,
            string serverUrl)
        {
            if (metadata == null || plan == null || string.IsNullOrWhiteSpace(serverUrl))
            {
                return;
            }

            AliasInstallPlanPackage package = SelectPlanPackage(plan, requested);
            if (package?.media == null)
            {
                return;
            }

            string accessToken;
            try
            {
                accessToken = CreatorIdentityOAuthService.GetValidAccessTokenAsync(
                    serverUrl,
                    AliasDeliveryRequiredScopes).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP UpdateDelivery] Could not acquire token for preview media: {ex.Message}");
                return;
            }

            if (string.IsNullOrEmpty(accessToken))
            {
                return;
            }

            Texture2D icon = TryDownloadPreviewTexture(serverUrl, accessToken, package.media.icon);
            if (icon != null)
            {
                metadata.icon = icon;
            }

            Texture2D banner = TryDownloadPreviewTexture(serverUrl, accessToken, package.media.banner);
            if (banner != null)
            {
                metadata.banner = banner;
            }
        }

        private static Texture2D TryDownloadPreviewTexture(
            string serverUrl,
            string accessToken,
            AliasPackageMediaDescriptor descriptor)
        {
            if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.downloadUrl))
            {
                return null;
            }

            try
            {
                EnsureTrustedSameOrigin(descriptor.downloadUrl, serverUrl, "Package media download URL");
                byte[] bytes = DownloadAuthorizedBytesAsync(
                    serverUrl,
                    accessToken,
                    descriptor.downloadUrl,
                    "Could not download package preview media")
                    .GetAwaiter()
                    .GetResult();

                string expectedSha256 = NormalizeSha256(descriptor.sha256);
                if (!string.IsNullOrEmpty(expectedSha256) &&
                    !string.Equals(ComputeSha256(bytes), expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes))
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                    return null;
                }

                return texture;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YUCP UpdateDelivery] Skipping preview media: {ex.Message}");
                return null;
            }
        }

        private static AliasInstallPlanPackage SelectPlanPackage(
            AliasInstallPlan plan,
            AliasPackageContract requested)
        {
            if (plan?.packages == null || plan.packages.Length == 0)
            {
                return null;
            }

            string requestedName = requested?.packageName;
            if (!string.IsNullOrWhiteSpace(requestedName))
            {
                foreach (AliasInstallPlanPackage candidate in plan.packages)
                {
                    if (candidate != null &&
                        string.Equals(candidate.packageId, requestedName, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }

            foreach (AliasInstallPlanPackage candidate in plan.packages)
            {
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return null;
            }

            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static PackageMetadata LoadInstalledPackageMetadata(string packageId)
        {
#if UNITY_INCLUDE_TESTS
            if (UpdateDeliveryServiceTestHooks.InstalledPackageMetadataLoader != null)
            {
                return UpdateDeliveryServiceTestHooks.InstalledPackageMetadataLoader(packageId);
            }
#endif
            return PackageMetadataExtractor.LoadMetadataFromInstalledPackage(packageId);
        }

        private static AliasPackageContract MergeAliasPackageContract(
            AliasPackageContract installedContract,
            AliasInstallPlanPackage package)
        {
            AliasPackageContract merged = installedContract?.Clone() ?? package.aliasContract?.Clone() ?? new AliasPackageContract();
            merged.kind = !string.IsNullOrWhiteSpace(merged.kind) ? merged.kind : "alias-v1";
            merged.aliasId = !string.IsNullOrWhiteSpace(merged.aliasId)
                ? merged.aliasId
                : package.aliasContract?.aliasId ?? string.Empty;
            merged.packageName = !string.IsNullOrWhiteSpace(merged.packageName)
                ? merged.packageName
                : package.packageId ?? string.Empty;
            merged.packageDisplayName = !string.IsNullOrWhiteSpace(merged.packageDisplayName)
                ? merged.packageDisplayName
                : package.displayName ?? string.Empty;
            merged.packageVersion = !string.IsNullOrWhiteSpace(merged.packageVersion)
                ? merged.packageVersion
                : package.version ?? string.Empty;
            merged.channel = !string.IsNullOrWhiteSpace(merged.channel)
                ? merged.channel
                : package.channel ?? string.Empty;
            merged.installStrategy = !string.IsNullOrWhiteSpace(merged.installStrategy)
                ? merged.installStrategy
                : package.aliasContract?.installStrategy ?? ServerAuthorizedInstallStrategy;
            merged.importerPackage = !string.IsNullOrWhiteSpace(merged.importerPackage)
                ? merged.importerPackage
                : package.aliasContract?.importerPackage ?? string.Empty;
            merged.catalogProductIds = merged.catalogProductIds != null && merged.catalogProductIds.Count > 0
                ? merged.catalogProductIds
                : package.aliasContract?.catalogProductIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList()
                    ?? new List<string>();

            merged.resolvedArtifact ??= new AliasResolvedArtifactIdentity();
            if (string.IsNullOrWhiteSpace(merged.resolvedArtifact.sha256))
            {
                merged.resolvedArtifact.sha256 = package.packageSha256 ?? package.zipSha256 ?? string.Empty;
            }

            AddGeneratedPackageMediaPath(merged, package.media?.icon);
            AddGeneratedPackageMediaPath(merged, package.media?.banner);

            return merged;
        }

        internal static void CacheAuthorizedPackageMedia(
            string projectDir,
            string serverUrl,
            AliasInstallPlanPackage package,
            string accessToken)
        {
            if (package?.media == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(projectDir))
            {
                throw new Exception("Could not resolve the current Unity project directory.");
            }

            CacheAuthorizedPackageMediaDescriptor(
                projectDir,
                serverUrl,
                package,
                package.media.icon,
                "icon",
                accessToken);
            CacheAuthorizedPackageMediaDescriptor(
                projectDir,
                serverUrl,
                package,
                package.media.banner,
                "banner",
                accessToken);
        }

        private static void CacheAuthorizedPackageMediaDescriptor(
            string projectDir,
            string serverUrl,
            AliasInstallPlanPackage package,
            AliasPackageMediaDescriptor descriptor,
            string fallbackKind,
            string accessToken)
        {
            if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.downloadUrl))
            {
                return;
            }

            string mediaKind = NormalizeMediaKind(descriptor.kind, fallbackKind);
            if (string.IsNullOrWhiteSpace(mediaKind))
            {
                throw new Exception(
                    $"Alias install plan for '{package.packageId}' included unsupported package media kind '{descriptor.kind ?? "<missing>"}'.");
            }

            EnsureTrustedSameOrigin(descriptor.downloadUrl, serverUrl, "Package media download URL");

            string expectedSha256 = NormalizeSha256(descriptor.sha256);
            if (string.IsNullOrEmpty(expectedSha256))
            {
                throw new Exception(
                    $"Alias install plan for '{package.packageId}' included {mediaKind} media without a valid SHA-256 digest.");
            }

            string extension = GetMediaExtension(descriptor.contentType);
            if (string.IsNullOrEmpty(extension))
            {
                throw new Exception(
                    $"Alias install plan for '{package.packageId}' included unsupported {mediaKind} media content type '{descriptor.contentType ?? "<missing>"}'.");
            }

            byte[] bytes = DownloadAuthorizedBytesAsync(
                serverUrl,
                accessToken,
                descriptor.downloadUrl,
                $"Could not download {mediaKind} media for {package.packageId}@{package.version}")
                .GetAwaiter()
                .GetResult();

            if (descriptor.byteSize > 0 && bytes.LongLength != descriptor.byteSize)
            {
                throw new Exception(
                    $"Downloaded {mediaKind} media for {package.packageId}@{package.version} had {bytes.LongLength} bytes, expected {descriptor.byteSize}.");
            }

            string actualSha256 = ComputeSha256(bytes);
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(
                    $"SHA-256 mismatch for {mediaKind} media on {package.packageId}@{package.version}. Expected {expectedSha256}, got {actualSha256}.");
            }

            string relativePath = BuildPackageMediaRelativePath(package.packageId, package.version, mediaKind, extension);
            WriteProjectRelativeFile(projectDir, relativePath, bytes);
            descriptor.kind = mediaKind;
            descriptor.localPath = relativePath;
        }

        private static async Task<byte[]> DownloadAuthorizedBytesAsync(
            string serverUrl,
            string accessToken,
            string url,
            string fallback)
        {
            BinaryRequestResult result = await SendAuthorizedBytesAsync(
                serverUrl,
                accessToken,
                url,
                AliasDeliveryRequiredScopes).ConfigureAwait(false);
            if (result.responseCode == 200)
            {
                return result.bytes ?? Array.Empty<byte>();
            }

            string body = result.bytes != null && result.bytes.Length > 0
                ? Encoding.UTF8.GetString(result.bytes)
                : string.Empty;
            throw new Exception(BuildServerErrorMessage(result.responseCode, body, fallback));
        }

        private static async Task<BinaryRequestResult> SendAuthorizedBytesAsync(
            string serverUrl,
            string accessToken,
            string url,
            params string[] requiredScopes)
        {
            bool hasRetried = false;

            while (true)
            {
                BinaryRequestResult result = await SendHttpBytesAsync(url, accessToken).ConfigureAwait(false);
                if (result.responseCode == 401)
                {
                    if (!hasRetried)
                    {
                        string refreshed = await CreatorIdentityOAuthService.ForceRefreshAccessTokenAsync(
                            serverUrl,
                            requiredScopes).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(refreshed))
                        {
                            accessToken = refreshed;
                            hasRetried = true;
                            continue;
                        }
                    }

                    throw new ReauthenticationRequiredException();
                }

                string parsedError = ExtractErrorMessage(
                    result.bytes != null && result.bytes.Length > 0
                        ? Encoding.UTF8.GetString(result.bytes)
                        : string.Empty);
                if (result.responseCode == 403 &&
                    !string.IsNullOrEmpty(parsedError) &&
                    parsedError.IndexOf("products:read", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new ReauthenticationRequiredException();
                }

                return result;
            }
        }

        private static async Task<BinaryRequestResult> SendHttpBytesAsync(string url, string accessToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "image/*,application/octet-stream");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            HttpResponseMessage response = await s_http.SendAsync(request).ConfigureAwait(false);
            byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return new BinaryRequestResult
            {
                responseCode = (long)response.StatusCode,
                bytes = bytes,
            };
        }

        private static void ApplyCachedPackageMedia(PackageMetadata metadata, AliasPackageMediaSet media)
        {
            if (metadata == null || media == null)
            {
                return;
            }

            Texture2D icon = LoadCachedMediaTexture(media.icon);
            if (icon != null)
            {
                metadata.icon = icon;
            }

            Texture2D banner = LoadCachedMediaTexture(media.banner);
            if (banner != null)
            {
                metadata.banner = banner;
            }
        }

        private static Texture2D LoadCachedMediaTexture(AliasPackageMediaDescriptor descriptor)
        {
            if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.localPath))
            {
                return null;
            }

            string assetPath = NormalizeProjectRelativePath(descriptor.localPath);
            if (!assetPath.StartsWith(InstalledPackagesOrganizer.RootAssetPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static void AddGeneratedPackageMediaPath(
            AliasPackageContract aliasPackage,
            AliasPackageMediaDescriptor descriptor)
        {
            if (aliasPackage == null || descriptor == null || string.IsNullOrWhiteSpace(descriptor.localPath))
            {
                return;
            }

            aliasPackage.installPlan ??= new AliasInstallPlanMetadata();
            aliasPackage.installPlan.generatedPaths ??= new List<string>();
            string normalizedPath = NormalizeProjectRelativePath(descriptor.localPath);
            if (!aliasPackage.installPlan.generatedPaths.Any(
                    path => string.Equals(
                        NormalizeProjectRelativePath(path),
                        normalizedPath,
                        StringComparison.OrdinalIgnoreCase)))
            {
                aliasPackage.installPlan.generatedPaths.Add(normalizedPath);
            }
        }

        private static void WriteProjectRelativeFile(string projectDir, string relativePath, byte[] bytes)
        {
            string normalizedRelativePath = NormalizeProjectRelativePath(relativePath);
            string projectRoot = Path.GetFullPath(projectDir);
            string fullPath = Path.GetFullPath(Path.Combine(
                projectRoot,
                normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(projectRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Package media path resolved outside the Unity project.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, bytes);
        }

        private static string BuildPackageMediaRelativePath(
            string packageId,
            string version,
            string mediaKind,
            string extension)
        {
            string safePackageId = SanitizeAssetPathSegment(packageId, "package");
            string safeVersion = SanitizeAssetPathSegment(version, "version");
            return $"{InstalledPackagesOrganizer.RootAssetPath}/Media/{safePackageId}/{safeVersion}/{mediaKind}{extension}";
        }

        private static string SanitizeAssetPathSegment(string value, string fallback)
        {
            string candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            var builder = new StringBuilder(candidate.Length);
            foreach (char character in candidate)
            {
                bool allowed =
                    (character >= 'a' && character <= 'z') ||
                    (character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9') ||
                    character == '.' ||
                    character == '-' ||
                    character == '_';
                builder.Append(allowed ? character : '-');
            }

            string sanitized = builder.ToString().Trim('-', '.');
            return string.IsNullOrEmpty(sanitized) ? fallback : sanitized;
        }

        private static string NormalizeProjectRelativePath(string path)
        {
            return (path ?? string.Empty)
                .Replace('\\', '/')
                .Trim()
                .TrimStart('/');
        }

        private static string NormalizeMediaKind(string kind, string fallbackKind)
        {
            string normalized = string.IsNullOrWhiteSpace(kind) ? fallbackKind : kind.Trim();
            if (string.Equals(normalized, "icon", StringComparison.OrdinalIgnoreCase))
            {
                return "icon";
            }

            if (string.Equals(normalized, "banner", StringComparison.OrdinalIgnoreCase))
            {
                return "banner";
            }

            return null;
        }

        private static string GetMediaExtension(string contentType)
        {
            string normalized = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
            return normalized switch
            {
                "image/gif" => ".gif",
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => null,
            };
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using var sha256 = SHA256.Create();
            return BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string NormalizeSha256(string value)
        {
            string normalized = value?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normalized) || normalized.Length != 64)
            {
                return null;
            }

            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!isHex)
                {
                    return null;
                }
            }

            return normalized;
        }

        private static void EnsureTrustedSameOrigin(string url, string expectedOrigin, string label)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                throw new Exception($"{label} is invalid.");
            }

            if (uri.Scheme != Uri.UriSchemeHttps &&
                !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            {
                throw new Exception($"{label} is not trusted.");
            }

            if (!Uri.TryCreate(expectedOrigin, UriKind.Absolute, out Uri expectedUri))
            {
                throw new Exception("Alias install plan repository URL is invalid.");
            }

            if (!string.Equals(
                    uri.GetLeftPart(UriPartial.Authority),
                    expectedUri.GetLeftPart(UriPartial.Authority),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"{label} did not match the package download origin.");
            }
        }

        private static IEnumerable<string> ResolveManagedPackagePaths(
            AliasPackageContract aliasPackage,
            AliasInstallPlanPackage package)
        {
            var resolvedPaths = new List<string>();
            AddDistinctManagedPath(resolvedPaths, $"Packages/{package.packageId}/package.json");
            List<string> managedPaths = aliasPackage?.installPlan?.managedPaths?
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (managedPaths != null && managedPaths.Count > 0)
            {
                foreach (string managedPath in managedPaths)
                {
                    AddDistinctManagedPath(resolvedPaths, managedPath);
                }
            }

            return resolvedPaths;
        }

        private static void MergeImportedManagedPaths(
            AliasInstallPlanPackage package,
            IEnumerable<string> managedPaths)
        {
            if (package == null || managedPaths == null)
            {
                return;
            }

            package.aliasContract ??= new AliasPackageContract
            {
                kind = "alias-v1",
                aliasId = package.packageId ?? string.Empty,
                packageName = package.packageId ?? string.Empty,
                packageVersion = package.version ?? string.Empty,
                installStrategy = ServerAuthorizedInstallStrategy,
                importerPackage = "com.yucp.importer",
            };
            package.aliasContract.installPlan ??= new AliasInstallPlanMetadata();
            package.aliasContract.installPlan.managedPaths ??= new List<string>();
            AddDistinctManagedPath(
                package.aliasContract.installPlan.managedPaths,
                $"Packages/{package.packageId}/package.json");
            foreach (string managedPath in managedPaths)
            {
                AddDistinctManagedPath(package.aliasContract.installPlan.managedPaths, managedPath);
            }
        }

        private static void AddDistinctManagedPath(List<string> paths, string path)
        {
            if (paths == null || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string normalized = path.Trim().Replace('\\', '/').TrimStart('/');
            if (!paths.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                paths.Add(normalized);
            }
        }

        private static void PersistInstallState(InstalledPackageInfo installedInfo)
        {
#if UNITY_INCLUDE_TESTS
            if (UpdateDeliveryServiceTestHooks.PersistInstallStateHandler != null)
            {
                UpdateDeliveryServiceTestHooks.PersistInstallStateHandler(installedInfo);
                return;
            }
#endif
            if (AliasPackageInstallStateStore.TryPersist(installedInfo, out string manifestRelativePath, out string error))
            {
                installedInfo.installStateManifestPath = manifestRelativePath ?? string.Empty;
                return;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new Exception(
                    $"Could not persist alias install-state for '{installedInfo.packageId}': {error}");
            }
        }

        private static void RegisterInstalledPackage(InstalledPackageInfo installedInfo)
        {
#if UNITY_INCLUDE_TESTS
            if (UpdateDeliveryServiceTestHooks.RegisterInstalledPackageHandler != null)
            {
                UpdateDeliveryServiceTestHooks.RegisterInstalledPackageHandler(installedInfo);
                return;
            }
#endif
            InstalledPackageRegistry registry = InstalledPackageRegistry.GetOrCreate();
            registry.RegisterPackage(installedInfo);
        }

        private static void ValidateAliasPackage(AliasPackageContract aliasPackage)
        {
            if (aliasPackage == null)
            {
                throw new Exception("Alias package metadata is missing.");
            }

            if (!string.Equals(aliasPackage.kind, "alias-v1", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(aliasPackage.installStrategy, ServerAuthorizedInstallStrategy, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(
                    $"Alias package '{aliasPackage.aliasId ?? aliasPackage.packageName}' does not use server-authorized delivery.");
            }

            if (aliasPackage.catalogProductIds == null || aliasPackage.catalogProductIds.Count == 0)
            {
                throw new Exception(
                    $"Alias package '{aliasPackage.aliasId ?? aliasPackage.packageName}' is missing catalog product IDs.");
            }
        }

        private static async Task<AliasInstallPlan> GetAliasInstallPlanAsync(
            string serverUrl,
            string accessToken,
            string catalogProductId)
        {
            if (string.IsNullOrWhiteSpace(catalogProductId))
            {
                throw new Exception("Alias package metadata included an empty catalog product ID.");
            }

            string url =
                $"{serverUrl.TrimEnd('/')}/api/backstage/access/products/{Uri.EscapeDataString(catalogProductId)}/install-plan";
            string bodyJson = JsonUtility.ToJson(new AliasInstallPlanRequest
            {
                machineFingerprint = MachineFingerprintService.GetFingerprint(),
            });
            RequestResult result = await SendAuthorizedJsonAsync(
                serverUrl,
                accessToken,
                "POST",
                url,
                bodyJson,
                AliasDeliveryRequiredScopes).ConfigureAwait(false);
            if (result.responseCode != 200)
            {
                throw new Exception(
                    BuildServerErrorMessage(result.responseCode, result.body, "Could not resolve alias install plan"));
            }

            AliasInstallPlan installPlan = JsonUtility.FromJson<AliasInstallPlan>(result.body);
            if (installPlan == null)
            {
                throw new Exception("Alias install plan response was invalid.");
            }

            return installPlan;
        }

        private static void ValidateInstallPlan(AliasInstallPlan installPlan, AliasPackageContract aliasPackage)
        {
            if (installPlan == null)
            {
                throw new Exception("Alias install plan is missing.");
            }

            if (!string.Equals(installPlan.kind, AliasInstallPlanKind, StringComparison.Ordinal))
            {
                throw new Exception("Alias install plan response used an unexpected contract kind.");
            }

            if (string.IsNullOrWhiteSpace(installPlan.repositoryUrl))
            {
                throw new Exception("Alias install plan response was missing repositoryUrl.");
            }

            if (installPlan.packages == null || installPlan.packages.Length == 0)
            {
                throw new Exception("Alias install plan response did not include any packages.");
            }

            bool matchedPackage = false;
            foreach (AliasInstallPlanPackage package in installPlan.packages)
            {
                if (package == null)
                {
                    throw new Exception("Alias install plan response included an invalid package entry.");
                }

                if (package.importerDelivery == null)
                {
                    throw new Exception(
                        $"Alias install plan for '{package.packageId}' is missing importer delivery metadata.");
                }

                if (string.IsNullOrWhiteSpace(package.downloadAuthorizationUrl))
                {
                    throw new Exception(
                        $"Alias install plan for '{package.packageId}' is missing a package download authorization URL.");
                }

                if (string.IsNullOrWhiteSpace(package.packageSha256))
                {
                    throw new Exception(
                        $"Alias install plan for '{package.packageId}' is missing a package SHA-256 digest.");
                }

                if (!IsSupportedSourceKind(package.sourceKind))
                {
                    throw new Exception(
                        $"Alias install plan for '{package.packageId}' declared unsupported source kind '{package.sourceKind ?? "<missing>"}'.");
                }

                if (!string.Equals(
                        package.importerDelivery.packageInstallStrategy,
                        ServerAuthorizedInstallStrategy,
                        StringComparison.Ordinal))
                {
                    throw new Exception(
                        $"Alias install plan for '{package.packageId}' declared unsupported install strategy '{package.importerDelivery.packageInstallStrategy}'.");
                }

                if (!string.Equals(
                        package.importerDelivery.repoCatalogDeliveryMode,
                        RepoTokenVpmDeliveryMode,
                        StringComparison.Ordinal))
                {
                    throw new Exception(
                        $"Alias install plan for '{package.packageId}' declared unsupported repo delivery mode '{package.importerDelivery.repoCatalogDeliveryMode}'.");
                }

                if (!package.importerDelivery.repoCatalogReadOnly)
                {
                    throw new Exception(
                        $"Alias install plan for '{package.packageId}' must keep the repo catalog read-only.");
                }

                if (package.aliasContract == null)
                {
                    throw new Exception(
                        $"Alias install plan for '{package.packageId}' is missing alias contract metadata.");
                }

                if (string.Equals(package.aliasContract.aliasId, aliasPackage.aliasId, StringComparison.Ordinal) ||
                    string.Equals(package.packageId, aliasPackage.packageName, StringComparison.OrdinalIgnoreCase))
                {
                    matchedPackage = true;
                }
            }

            if (!matchedPackage)
            {
                throw new Exception(
                    $"Alias install plan did not include the expected alias package '{aliasPackage.aliasId}'.");
            }
        }

        private static bool IsSupportedSourceKind(string sourceKind)
        {
            return string.Equals(sourceKind, "zip", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sourceKind, "unitypackage", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetTrustedServerOrigin(string url, string expectedRepositoryUrl)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                throw new Exception("Package download authorization URL is invalid.");
            }

            if (uri.Scheme != Uri.UriSchemeHttps &&
                !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            {
                throw new Exception("Package download authorization URL is not trusted.");
            }

            string origin = uri.GetLeftPart(UriPartial.Authority);
            if (!Uri.TryCreate(expectedRepositoryUrl, UriKind.Absolute, out Uri repositoryUri))
            {
                throw new Exception("Alias install plan repository URL is invalid.");
            }

            string repositoryOrigin = repositoryUri.GetLeftPart(UriPartial.Authority);
            if (!string.Equals(origin, repositoryOrigin, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Package download authorization URL did not match the repository origin.");
            }

            return origin;
        }

        private static async Task<RequestResult> SendAuthorizedJsonAsync(
            string serverUrl,
            string accessToken,
            string method,
            string url,
            string bodyJson,
            params string[] requiredScopes)
        {
            bool hasRetried = false;

            while (true)
            {
                RequestResult result = await SendHttpJsonAsync(method, url, accessToken, bodyJson)
                    .ConfigureAwait(false);

                if (result.responseCode == 401)
                {
                    if (!hasRetried)
                    {
                        string refreshed = await CreatorIdentityOAuthService.ForceRefreshAccessTokenAsync(
                            serverUrl,
                            requiredScopes).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(refreshed))
                        {
                            accessToken = refreshed;
                            hasRetried = true;
                            continue;
                        }
                    }

                    throw new ReauthenticationRequiredException();
                }

                string parsedError = ExtractErrorMessage(result.body);
                if (result.responseCode == 403 &&
                    !string.IsNullOrEmpty(parsedError) &&
                    parsedError.IndexOf("products:read", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new ReauthenticationRequiredException();
                }

                return result;
            }
        }

        private static async Task<RequestResult> SendHttpJsonAsync(
            string method,
            string url,
            string accessToken,
            string bodyJson)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            if (!string.IsNullOrEmpty(bodyJson))
            {
                request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response = await s_http.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return new RequestResult
            {
                responseCode = (long)response.StatusCode,
                body = body,
            };
        }

        private static string BuildServerErrorMessage(long responseCode, string body, string fallback)
        {
            string error = ExtractErrorMessage(body);
            if (!string.IsNullOrWhiteSpace(error))
            {
                return error;
            }

            return $"{fallback} (HTTP {responseCode}).";
        }

        private static string ExtractErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                ErrorResponse parsed = JsonUtility.FromJson<ErrorResponse>(body);
                if (!string.IsNullOrWhiteSpace(parsed?.message))
                {
                    return parsed.message;
                }

                if (!string.IsNullOrWhiteSpace(parsed?.error))
                {
                    return parsed.error;
                }
            }
            catch
            {
            }

            return null;
        }

        private static InstalledPackageInfo ResolveInstalledPackage(string packageId)
        {
#if UNITY_INCLUDE_TESTS
            if (ProtectedAssetUnlockServiceTestHooks.InstalledPackageResolver != null)
            {
                return ProtectedAssetUnlockServiceTestHooks.InstalledPackageResolver(packageId);
            }
#endif

            InstalledPackageRegistry registry = InstalledPackageRegistry.Load();
            return registry?.GetPackage(packageId);
        }

        private static AliasPackageContract ResolveInstalledAliasPackage(string packageId)
        {
            InstalledPackageInfo package = ResolveInstalledPackage(packageId);
            if (package?.aliasPackage != null)
            {
                return package.aliasPackage;
            }

            PackageMetadata metadata = LoadInstalledPackageMetadata(packageId);
            return metadata?.aliasPackage;
        }

        [Serializable]
        private sealed class ErrorResponse
        {
            public string error;
            public string message;
        }

        private sealed class ReauthenticationRequiredException : Exception
        {
        }
    }

    internal static class AliasInstallPlanConfirmationService
    {
        internal sealed class ConfirmationRequest
        {
            public string title;
            public string confirmButton;
            public string cancelButton;
            public string message;
        }

        private const int PathPreviewLimit = 4;
        private const int PackagePreviewLimit = 5;

        internal static bool RequiresInstallConfirmation(PackageMetadata metadata)
        {
            return IsAliasPackage(metadata?.aliasPackage);
        }

        internal static bool ConfirmInstall(
            PackageMetadata metadata,
            Func<ConfirmationRequest, bool> confirmationHandler = null)
        {
            ConfirmationRequest request = BuildInstallRequest(metadata);
            if (request == null)
            {
                return true;
            }

            return Confirm(request, confirmationHandler);
        }

        internal static bool ConfirmUpdate(
            InstalledPackageInfo packageInfo,
            UpdateDeliveryService.AliasInstallPlan installPlan,
            Func<ConfirmationRequest, bool> confirmationHandler = null)
        {
            ConfirmationRequest request = BuildUpdateRequest(packageInfo, installPlan);
            if (request == null)
            {
                return true;
            }

            return Confirm(request, confirmationHandler);
        }

        internal static ConfirmationRequest BuildInstallRequest(PackageMetadata metadata)
        {
            AliasPackageContract aliasPackage = metadata?.aliasPackage;
            if (!IsAliasPackage(aliasPackage))
            {
                return null;
            }

            string packageLabel = ResolvePackageLabel(
                aliasPackage.packageDisplayName,
                metadata?.packageName,
                aliasPackage.packageName,
                aliasPackage.aliasId);
            string version = ResolveValue(aliasPackage.packageVersion, metadata?.version);
            string operation = ResolveValue(aliasPackage.installPlan?.operation, "install");

            var message = new StringBuilder();
            message.AppendLine($"Install alias package '{packageLabel}'?");
            message.AppendLine();
            AppendField(message, "Alias", aliasPackage.aliasId);
            AppendField(message, "Version", version);
            AppendField(message, "Install strategy", aliasPackage.installStrategy);
            AppendField(message, "Operation", operation);
            if (aliasPackage.catalogProductIds != null && aliasPackage.catalogProductIds.Count > 0)
            {
                message.AppendLine($"Catalog products: {aliasPackage.catalogProductIds.Count}");
            }

            AppendPlanSummary(message, aliasPackage.installPlan);
            message.AppendLine();
            message.Append("Continue only if this matches the package changes you expect.");

            return new ConfirmationRequest
            {
                title = "Confirm Alias Install",
                confirmButton = "Install Package",
                cancelButton = "Cancel",
                message = message.ToString().TrimEnd(),
            };
        }

        internal static ConfirmationRequest BuildUpdateRequest(
            InstalledPackageInfo packageInfo,
            UpdateDeliveryService.AliasInstallPlan installPlan)
        {
            if (installPlan == null)
            {
                return null;
            }

            string packageLabel = ResolvePackageLabel(
                installPlan.title,
                packageInfo?.packageName,
                packageInfo?.packageId,
                installPlan.productRef);

            var message = new StringBuilder();
            message.AppendLine($"Update alias package '{packageLabel}'?");
            message.AppendLine();
            AppendField(message, "Creator", installPlan.creatorName);
            AppendField(message, "Creator repo", installPlan.creatorRepoRef);
            AppendField(message, "Product", installPlan.productRef);
            AppendField(message, "Repository catalog", installPlan.repositoryUrl);

            if (installPlan.expiresAt > 0)
            {
                DateTimeOffset expiresAt = DateTimeOffset.FromUnixTimeSeconds(installPlan.expiresAt);
                message.AppendLine($"Plan expires: {expiresAt.UtcDateTime.ToString("u", CultureInfo.InvariantCulture)}");
            }

            int packageCount = installPlan.packages?.Length ?? 0;
            message.AppendLine($"Packages to download/apply: {packageCount}");
            AppendPackagePreview(message, installPlan.packages);
            AppendPlanSummary(message, CollectAggregatePlan(installPlan.packages));
            message.AppendLine();
            message.Append("The importer will only proceed if this resolved plan matches the update you expect.");

            return new ConfirmationRequest
            {
                title = "Confirm Alias Update",
                confirmButton = "Apply Update",
                cancelButton = "Cancel",
                message = message.ToString().TrimEnd(),
            };
        }

        private static bool Confirm(
            ConfirmationRequest request,
            Func<ConfirmationRequest, bool> confirmationHandler)
        {
            if (request == null)
            {
                return true;
            }

            if (confirmationHandler != null)
            {
                return confirmationHandler(request);
            }

            return YucpEditorDialog.DisplayDialog(
                request.title,
                request.message,
                request.confirmButton,
                request.cancelButton);
        }

        private static AliasInstallPlanMetadata CollectAggregatePlan(
            UpdateDeliveryService.AliasInstallPlanPackage[] packages)
        {
            var aggregate = new AliasInstallPlanMetadata();
            if (packages == null)
            {
                return aggregate;
            }

            foreach (UpdateDeliveryService.AliasInstallPlanPackage package in packages)
            {
                AliasInstallPlanMetadata packagePlan = package?.aliasContract?.installPlan;
                if (packagePlan == null)
                {
                    continue;
                }

                AddDistinct(aggregate.managedPaths, packagePlan.managedPaths);
                AddDistinct(aggregate.generatedPaths, packagePlan.generatedPaths);
                AddDistinct(aggregate.sharedPaths, packagePlan.sharedPaths);
            }

            return aggregate;
        }

        private static void AddDistinct(List<string> destination, List<string> values)
        {
            if (destination == null || values == null)
            {
                return;
            }

            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!destination.Contains(value))
                {
                    destination.Add(value);
                }
            }
        }

        private static void AppendPackagePreview(
            StringBuilder message,
            UpdateDeliveryService.AliasInstallPlanPackage[] packages)
        {
            if (packages == null || packages.Length == 0)
            {
                return;
            }

            message.AppendLine();
            message.AppendLine("Packages:");
            int count = Math.Min(packages.Length, PackagePreviewLimit);
            for (int i = 0; i < count; i++)
            {
                UpdateDeliveryService.AliasInstallPlanPackage package = packages[i];
                if (package == null)
                {
                    continue;
                }

                string packageLabel = ResolvePackageLabel(
                    package.displayName,
                    package.packageId,
                    package.aliasContract?.packageDisplayName,
                    package.aliasContract?.aliasId);
                string version = ResolveValue(package.version, package.aliasContract?.packageVersion);
                string channel = ResolveValue(package.channel, package.aliasContract?.channel);

                message.Append("- ");
                message.Append(packageLabel);
                if (!string.IsNullOrWhiteSpace(package.packageId) &&
                    !string.Equals(packageLabel, package.packageId, StringComparison.OrdinalIgnoreCase))
                {
                    message.Append($" ({package.packageId})");
                }

                if (!string.IsNullOrWhiteSpace(version))
                {
                    message.Append($" v{version}");
                }

                if (!string.IsNullOrWhiteSpace(channel))
                {
                    message.Append($" [{channel}]");
                }

                message.AppendLine();
            }

            if (packages.Length > count)
            {
                message.AppendLine($"- ...and {packages.Length - count} more");
            }
        }

        private static void AppendPlanSummary(StringBuilder message, AliasInstallPlanMetadata plan)
        {
            if (plan == null)
            {
                return;
            }

            int managedCount = plan.managedPaths?.Count ?? 0;
            int generatedCount = plan.generatedPaths?.Count ?? 0;
            int sharedCount = plan.sharedPaths?.Count ?? 0;
            if (managedCount == 0 && generatedCount == 0 && sharedCount == 0)
            {
                return;
            }

            message.AppendLine();
            message.AppendLine($"Managed paths: {managedCount}");
            message.AppendLine($"Generated paths: {generatedCount}");
            message.AppendLine($"Shared preserved paths: {sharedCount}");
            AppendPathPreview(message, "Managed preview", plan.managedPaths);
            AppendPathPreview(message, "Generated preview", plan.generatedPaths);
            AppendPathPreview(message, "Shared preview", plan.sharedPaths);
        }

        private static void AppendPathPreview(StringBuilder message, string label, List<string> paths)
        {
            if (paths == null || paths.Count == 0)
            {
                return;
            }

            message.AppendLine();
            message.AppendLine($"{label}:");
            int count = Math.Min(paths.Count, PathPreviewLimit);
            for (int i = 0; i < count; i++)
            {
                message.AppendLine($"- {paths[i]}");
            }

            if (paths.Count > count)
            {
                message.AppendLine($"- ...and {paths.Count - count} more");
            }
        }

        private static void AppendField(StringBuilder message, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            message.AppendLine($"{label}: {value.Trim()}");
        }

        private static string ResolvePackageLabel(params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate.Trim();
                }
            }

            return "this package";
        }

        private static string ResolveValue(params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate.Trim();
                }
            }

            return string.Empty;
        }

        private static bool IsAliasPackage(AliasPackageContract aliasPackage)
        {
            return aliasPackage != null &&
                string.Equals(aliasPackage.kind, "alias-v1", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(aliasPackage.aliasId);
        }
    }
}
































