using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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
        private sealed class RequestResult
        {
            public long responseCode;
            public string body;
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

            InstalledPackageInfo package = ResolveInstalledPackage(packageId);
            if (package?.aliasPackage == null)
            {
                error = $"Package '{packageId}' is not an alias package with server-authorized delivery metadata.";
                return false;
            }

            return TryResolveAuthorizedInstallPlan(serverUrl, package.aliasPackage, out installPlan, out error);
        }

        public static string CheckForUpdate(string packageId, string currentVersion)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return null;
            }

            InstalledPackageInfo package = ResolveInstalledPackage(packageId);
            if (package?.aliasPackage == null)
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

#if UNITY_INCLUDE_TESTS
                if (UpdateDeliveryServiceTestHooks.AuthorizedPackageInstallerHandler != null)
                {
                    UpdateDeliveryServiceTestHooks.AuthorizedPackageInstallerHandler(
                        projectDir,
                        serverUrl,
                        package,
                        accessToken);
                    continue;
                }
#endif
                AuthorizedVpmPackageInstaller.InstallAuthorizedPackage(
                    projectDir,
                    package,
                    accessToken);
            }

            UpdateProjectVpmManifest(projectDir, installPlan);
            Client.Resolve();
            AssetDatabase.Refresh();
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

            return merged;
        }

        private static IEnumerable<string> ResolveManagedPackagePaths(
            AliasPackageContract aliasPackage,
            AliasInstallPlanPackage package)
        {
            List<string> managedPaths = aliasPackage?.installPlan?.managedPaths?
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (managedPaths != null && managedPaths.Count > 0)
            {
                return managedPaths;
            }

            return new[]
            {
                $"Packages/{package.packageId}/package.json"
            };
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
            RequestResult result = await SendAuthorizedJsonAsync(
                serverUrl,
                accessToken,
                "POST",
                url,
                null,
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

            return EditorUtility.DisplayDialog(
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
































