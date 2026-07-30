using System;
using System.Collections.Generic;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class PackageChangeKind
    {
        internal const string Added = "added";
        internal const string BlockedCollision = "blocked-collision";
        internal const string Removed = "removed";
        internal const string RemovedWithLocalModifications =
            "removed-with-local-modifications";
        internal const string ReplacedUnchanged = "replaced-unchanged";
        internal const string ReplacedWithLocalModifications =
            "replaced-with-local-modifications";
    }

    [Serializable]
    internal sealed class PackageChangePlanEntry
    {
        public string changeKind = string.Empty;
        public string normalizedPath = string.Empty;
        public string observedSha256 = string.Empty;
        public string priorSha256 = string.Empty;
        public string targetSha256 = string.Empty;
        public long targetBytes;

        internal bool RequiresPreservedCopy =>
            string.Equals(
                changeKind,
                PackageChangeKind.ReplacedWithLocalModifications,
                StringComparison.Ordinal) ||
            string.Equals(
                changeKind,
                PackageChangeKind.RemovedWithLocalModifications,
                StringComparison.Ordinal);
    }

    [Serializable]
    internal sealed class PackageChangePlan
    {
        public string installedReleaseRoot = string.Empty;
        public string requestedReleaseRoot = string.Empty;
        public string requestedVersionId = string.Empty;
        public string reviewDigest = string.Empty;
        public string signature = string.Empty;
        public string signatureAlgorithm = string.Empty;
        public string signerKeyId = string.Empty;
        public string targetInventoryDigest = string.Empty;
        public List<PackageChangePlanEntry> entries =
            new List<PackageChangePlanEntry>();

        internal bool HasBlockedCollisions =>
            entries.Exists(entry => string.Equals(
                entry.changeKind,
                PackageChangeKind.BlockedCollision,
                StringComparison.Ordinal));
    }
}
