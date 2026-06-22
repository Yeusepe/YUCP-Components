using UnityEditor;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    /// <summary>
    /// Pinned P-256 server attestation public key (SPKI, base64). This is a PUBLIC key, so it is safe
    /// to ship in the open importer; the native collector seals the attestation to it. The matching
    /// private key lives only in the closed coupling-service. Resolved from settings so deployments can
    /// rotate it without a code change; the compiled default is intentionally empty until provisioned.
    /// </summary>
    internal static class TrustedAttestationKey
    {
        private const string PrefKey = "YUCP.Attestation.ServerKeySpkiB64";

        // Set this constant at release time to the deployment's pinned attestation public key, or
        // configure it via the pref below. Empty means attestation is not yet provisioned.
        private const string CompiledDefault = "";

        internal static string SpkiBase64
        {
            get
            {
                string fromPrefs = EditorPrefs.GetString(PrefKey, string.Empty);
                return string.IsNullOrEmpty(fromPrefs) ? CompiledDefault : fromPrefs;
            }
        }
    }

    /// <summary>
    /// Resolves the base URL of the closed coupling-service that terminates attestation
    /// (/v1/attestation/challenge and /submit). Defaults to the resolved license server host when a
    /// dedicated coupling-service URL is not configured, since they are commonly co-hosted.
    /// </summary>
    internal static class CouplingServiceResolver
    {
        private const string PrefKey = "YUCP.Attestation.CouplingServiceUrl";

        internal static string GetCouplingServiceUrl()
        {
            string configured = EditorPrefs.GetString(PrefKey, string.Empty);
            if (!string.IsNullOrEmpty(configured))
            {
                return configured;
            }
            return LicenseServerResolver.GetLicenseServerUrl();
        }
    }
}
