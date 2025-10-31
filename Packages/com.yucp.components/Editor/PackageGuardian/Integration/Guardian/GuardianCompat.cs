using System;
using UnityEngine;

namespace YUCP.Components.PackageGuardian.Editor.Integration.Guardian
{
    /// <summary>
    /// Compatibility layer for legacy Guardian API.
    /// Forwards calls to Package Guardian.
    /// </summary>
    [Obsolete("Guardian has been replaced by Package Guardian. Please use RepositoryService instead.")]
    public static class GuardianCompat
    {
        [Obsolete("Use RepositoryService.Instance.CreateSnapshot() instead")]
        public static void CreateBackup(string message)
        {
            try
            {
                Services.RepositoryService.Instance.CreateSnapshot(message);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Guardian Compat] Failed to create backup: {ex.Message}");
            }
        }
        
        [Obsolete("Use RepositoryService.Instance.CreateAutoStash() instead")]
        public static void AutoBackup()
        {
            try
            {
                Services.RepositoryService.Instance.CreateAutoStash("Auto-backup from legacy Guardian API");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Guardian Compat] Failed to create auto-backup: {ex.Message}");
            }
        }
        
        [Obsolete("Use Repository.Stash.List() instead")]
        public static int GetBackupCount()
        {
            try
            {
                var repo = Services.RepositoryService.Instance.Repository;
                return repo.Stash.List().Count;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Guardian Compat] Failed to get backup count: {ex.Message}");
                return 0;
            }
        }
    }
}

