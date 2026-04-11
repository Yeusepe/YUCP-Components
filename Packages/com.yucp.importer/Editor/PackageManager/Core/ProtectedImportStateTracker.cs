using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class ProtectedImportStateTracker
    {
        private const string EditorPrefsKeyPrefix = "YUCP.PackageManager.ProtectedImportState.";

        internal enum ProtectedImportPhase
        {
            shell_imported = 0,
            intent_verified = 1,
            apply_queued = 2,
            unlock_verified = 3,
            payload_extracted = 4,
            materialization_finalized = 5,
            rolled_back = 6,
        }

        [Serializable]
        internal sealed class ProtectedImportState
        {
            public string packageId = "";
            public string protectedAssetId = "";
            public string manifestBindingSha256 = "";
            public string phase = "";
            public string[] extractedAssetPaths = Array.Empty<string>();
            public long updatedAtTicksUtc;
        }

        internal static bool TryAdvance(
            InstalledPackageInfo packageInfo,
            ProtectedImportPhase targetPhase,
            out string error,
            IReadOnlyList<string> extractedAssetPaths = null)
        {
            error = null;
            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.packageId))
            {
                error = "Protected import state could not be recorded because the package ID is missing.";
                return false;
            }

            if (packageInfo.protectedPayload == null || string.IsNullOrWhiteSpace(packageInfo.protectedPayload.protectedAssetId))
            {
                error = "Protected import state could not be recorded because the protected payload descriptor is missing.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(packageInfo.protectedPayload.manifestBindingSha256))
            {
                error = "Protected import state could not be recorded because the manifest binding hash is missing.";
                return false;
            }

            string packageId = packageInfo.packageId ?? string.Empty;
            var existing = Load(packageId);
            if (existing != null && !Matches(existing, packageInfo))
            {
                if (targetPhase != ProtectedImportPhase.shell_imported)
                {
                    error = "Protected import resume state did not match the manifest-bound protected payload descriptor.";
                    return false;
                }
            }

            if (targetPhase != ProtectedImportPhase.shell_imported &&
                existing != null &&
                !IsAllowedTransition(ParsePhase(existing.phase), targetPhase))
            {
                error = $"Protected import state transition '{existing.phase}' -> '{targetPhase}' is not allowed.";
                return false;
            }

            var next = new ProtectedImportState
            {
                packageId = packageId,
                protectedAssetId = packageInfo.protectedPayload.protectedAssetId ?? string.Empty,
                manifestBindingSha256 = packageInfo.protectedPayload.manifestBindingSha256 ?? string.Empty,
                phase = targetPhase.ToString(),
                extractedAssetPaths = NormalizeAssetPaths(
                    extractedAssetPaths ?? existing?.extractedAssetPaths ?? Array.Empty<string>()),
                updatedAtTicksUtc = DateTime.UtcNow.Ticks,
            };

            EditorPrefs.SetString(GetKey(packageId), UnityEngine.JsonUtility.ToJson(next));
            return true;
        }

        internal static bool TryValidateResume(
            InstalledPackageInfo packageInfo,
            ProtectedImportPhase minimumPhase,
            out ProtectedImportState state,
            out string error)
        {
            error = null;
            state = null;

            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.packageId))
            {
                error = "Protected import resume state could not be checked because the package ID is missing.";
                return false;
            }

            state = Load(packageInfo.packageId);
            if (state == null)
            {
                error = "Protected import resume state was missing.";
                return false;
            }

            if (!Matches(state, packageInfo))
            {
                error = "Protected import resume state did not match the manifest-bound protected payload descriptor.";
                return false;
            }

            ProtectedImportPhase phase = ParsePhase(state.phase);
            if (phase == ProtectedImportPhase.rolled_back)
            {
                error = "Protected import resume state was already rolled back.";
                return false;
            }

            if (GetPhaseRank(phase) < GetPhaseRank(minimumPhase))
            {
                error = $"Protected import resume state was '{phase}' but expected at least '{minimumPhase}'.";
                return false;
            }

            return true;
        }

        internal static void Clear(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return;

            EditorPrefs.DeleteKey(GetKey(packageId));
        }

        private static ProtectedImportState Load(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return null;

            string raw = EditorPrefs.GetString(GetKey(packageId), string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            try
            {
                return UnityEngine.JsonUtility.FromJson<ProtectedImportState>(raw);
            }
            catch
            {
                return null;
            }
        }

        private static bool Matches(ProtectedImportState state, InstalledPackageInfo packageInfo)
        {
            return state != null &&
                packageInfo?.protectedPayload != null &&
                string.Equals(state.packageId ?? string.Empty, packageInfo.packageId ?? string.Empty, StringComparison.Ordinal) &&
                string.Equals(state.protectedAssetId ?? string.Empty, packageInfo.protectedPayload.protectedAssetId ?? string.Empty, StringComparison.Ordinal) &&
                string.Equals(state.manifestBindingSha256 ?? string.Empty, packageInfo.protectedPayload.manifestBindingSha256 ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetKey(string packageId)
        {
            return EditorPrefsKeyPrefix + packageId;
        }

        private static ProtectedImportPhase ParsePhase(string phase)
        {
            return Enum.TryParse(phase, out ProtectedImportPhase parsed)
                ? parsed
                : ProtectedImportPhase.shell_imported;
        }

        private static bool IsAllowedTransition(ProtectedImportPhase current, ProtectedImportPhase next)
        {
            if (current == next)
                return true;

            if (current == ProtectedImportPhase.materialization_finalized || current == ProtectedImportPhase.rolled_back)
                return false;

            if (next == ProtectedImportPhase.rolled_back)
                return true;

            return GetPhaseRank(next) >= GetPhaseRank(current);
        }

        private static int GetPhaseRank(ProtectedImportPhase phase)
        {
            return phase switch
            {
                ProtectedImportPhase.shell_imported => 0,
                ProtectedImportPhase.intent_verified => 1,
                ProtectedImportPhase.apply_queued => 2,
                ProtectedImportPhase.unlock_verified => 3,
                ProtectedImportPhase.payload_extracted => 4,
                ProtectedImportPhase.materialization_finalized => 5,
                ProtectedImportPhase.rolled_back => 6,
                _ => 0,
            };
        }

        private static string[] NormalizeAssetPaths(IEnumerable<string> assetPaths)
        {
            return (assetPaths ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Replace('\\', '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
