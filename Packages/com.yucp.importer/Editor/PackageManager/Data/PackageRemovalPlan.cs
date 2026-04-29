using System;
using System.Collections.Generic;
using System.Linq;

namespace YUCP.Importer.Editor.PackageManager
{
    public enum PackageRemovalOwnershipClass
    {
        Managed,
        Generated,
        Shared,
    }

    public enum PackageRemovalReconcileState
    {
        SafeToRemove,
        MissingByUser,
        ModifiedByUser,
        SharedPathPreserve,
    }

    [Serializable]
    public sealed class PackageRemovalPlanEntry
    {
        public string path = "";
        public PackageRemovalOwnershipClass ownershipClass = PackageRemovalOwnershipClass.Managed;
        public PackageRemovalReconcileState state = PackageRemovalReconcileState.ModifiedByUser;
        public bool existsOnDisk;
        public bool isDirectory;
        public string installedSha256 = "";
        public string currentSha256 = "";
        public string details = "";
    }

    [Serializable]
    public sealed class PackageRemovalPlan
    {
        public List<PackageRemovalPlanEntry> entries = new List<PackageRemovalPlanEntry>();

        public bool HasDestructiveRisk()
        {
            return entries.Any(entry => entry != null && entry.state == PackageRemovalReconcileState.ModifiedByUser);
        }

        public int Count(PackageRemovalReconcileState state)
        {
            return entries.Count(entry => entry != null && entry.state == state);
        }

        public List<PackageRemovalPlanEntry> GetEntries(PackageRemovalReconcileState state)
        {
            return entries
                .Where(entry => entry != null && entry.state == state)
                .OrderBy(entry => entry.path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public sealed class PackageRemovalExecutionResult
    {
        public int deletedCount;
        public int failedCount;
        public int preservedCount;
        public int missingCount;

        public bool Succeeded
        {
            get
            {
                return failedCount == 0;
            }
        }
    }
}
