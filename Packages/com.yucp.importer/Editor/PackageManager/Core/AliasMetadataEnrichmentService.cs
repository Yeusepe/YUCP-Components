using System;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    /// <summary>
    /// Resolves the server's authorized install plan for an alias package and turns it into rich
    /// display metadata (title, version, creator, icon, banner) so the installer can show what is
    /// about to be imported BEFORE the user confirms. This is purely a read/preview step — nothing
    /// is downloaded into the project and nothing is installed.
    /// </summary>
    internal static class AliasMetadataEnrichmentService
    {
        private const string LogPrefix = "[YUCP PackageManager][AliasMetadata]";

        /// <summary>
        /// Attempts to build enriched, display-ready metadata for the given alias package.
        /// Returns false (with a message in <paramref name="error"/>) when the plan cannot be
        /// resolved; in that case callers should fall back to the minimal embedded metadata.
        /// </summary>
        internal static bool TryEnrich(
            string serverUrl,
            AliasPackageContract aliasPackage,
            out PackageMetadata metadata,
            out string error)
        {
            metadata = null;
            error = null;

            if (aliasPackage == null)
            {
                error = "No alias contract was provided.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                error = "The YUCP verification server URL is not configured.";
                return false;
            }

            if (!UpdateDeliveryService.TryResolveAuthorizedInstallPlan(
                serverUrl,
                aliasPackage,
                out UpdateDeliveryService.AliasInstallPlan installPlan,
                out string resolveError))
            {
                error = string.IsNullOrWhiteSpace(resolveError)
                    ? "Could not resolve the authorized install plan."
                    : resolveError;
                return false;
            }

            metadata = UpdateDeliveryService.BuildPreviewMetadataFromPlan(installPlan, aliasPackage);
            if (metadata == null)
            {
                error = "The authorized install plan did not include any package details.";
                return false;
            }

            // Best-effort: never let media failures block showing the textual metadata.
            try
            {
                UpdateDeliveryService.TryAttachPlanMedia(metadata, installPlan, aliasPackage, serverUrl);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} Could not attach preview media: {ex.Message}");
            }

            return true;
        }
    }
}
