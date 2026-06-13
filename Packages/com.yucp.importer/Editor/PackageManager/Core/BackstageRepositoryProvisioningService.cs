using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    /// <summary>
    /// Asks the YUCP server for the private VPM repository the signed-in account is entitled to and
    /// returns the <c>vcc://vpm/addRepo</c> deep link that registers it (with its per-account access
    /// token header) in VRChat Creator Companion. This is the server-authorized half of the
    /// "install the shim, get your repository" flow surfaced by
    /// <see cref="ServerAuthorizedRepositoryInstaller"/>.
    /// </summary>
    internal static class BackstageRepositoryProvisioningService
    {
        // Matches apps/web/src/lib/backstageAccess.ts -> requestUserBackstageRepoAccess().
        private const string RepoAccessPath = "/api/backstage/repos/access";

        // The repo-access endpoint authenticates the Unity OAuth bearer token and requires the
        // products:read scope (apps/api/src/routes/backstageRepos.ts -> authenticateBackstageAccess).
        private static readonly string[] RequiredScopes = { "products:read" };

        private static readonly HttpClient s_http = new HttpClient();

        [Serializable]
        internal sealed class RepoAccess
        {
            public string creatorName;
            public string creatorRepoRef;
            public string repositoryUrl;
            public string repositoryName;
            public string addRepoUrl;
            public long expiresAt;
        }

        internal sealed class ProvisionResult
        {
            public bool success;
            public bool requiresSignIn;
            public RepoAccess access;
            public string error;
        }

        /// <summary>
        /// Synchronous wrapper that mirrors <see cref="UpdateDeliveryService.TryResolveAuthorizedInstallPlan"/>
        /// so the editor flow can block on a fast HTTP round-trip on the main thread.
        /// </summary>
        internal static ProvisionResult Provision(string serverUrl)
        {
            try
            {
                return ProvisionAsync(serverUrl).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return new ProvisionResult { error = $"Could not authorize repository access: {ex.Message}" };
            }
        }

        internal static async Task<ProvisionResult> ProvisionAsync(string serverUrl)
        {
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                return new ProvisionResult { error = "The YUCP server URL is not configured." };
            }

            string accessToken = await CreatorIdentityOAuthService
                .GetValidAccessTokenAsync(serverUrl, RequiredScopes)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(accessToken))
            {
                return new ProvisionResult
                {
                    requiresSignIn = true,
                    error = "Sign in to YUCP to authorize repository access.",
                };
            }

            string url = serverUrl.TrimEnd('/') + RepoAccessPath;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            HttpResponseMessage response;
            string body;
            try
            {
                response = await s_http.SendAsync(request).ConfigureAwait(false);
                body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new ProvisionResult { error = $"Could not reach the YUCP server: {ex.Message}" };
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new ProvisionResult
                {
                    requiresSignIn = true,
                    error = ExtractErrorMessage(body) ?? "Your YUCP session has expired. Sign in again.",
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ProvisionResult
                {
                    error = ExtractErrorMessage(body)
                        ?? $"The YUCP server returned an unexpected response ({(int)response.StatusCode}).",
                };
            }

            RepoAccess access;
            try
            {
                access = JsonUtility.FromJson<RepoAccess>(body);
            }
            catch (Exception ex)
            {
                return new ProvisionResult { error = $"The YUCP server response could not be read: {ex.Message}" };
            }

            if (access == null ||
                string.IsNullOrWhiteSpace(access.repositoryUrl) ||
                string.IsNullOrWhiteSpace(access.addRepoUrl))
            {
                return new ProvisionResult
                {
                    error = "The YUCP server did not return a repository to add. Make sure your purchase is linked to this account.",
                };
            }

            return new ProvisionResult { success = true, access = access };
        }

        private static string ExtractErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            const string needle = "\"error\"";
            int index = body.IndexOf(needle, StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            index = body.IndexOf('"', index + needle.Length);
            if (index < 0)
            {
                return null;
            }

            int end = body.IndexOf('"', index + 1);
            if (end < 0)
            {
                return null;
            }

            string message = body.Substring(index + 1, end - index - 1).Trim();
            return string.IsNullOrEmpty(message) ? null : message;
        }
    }
}
