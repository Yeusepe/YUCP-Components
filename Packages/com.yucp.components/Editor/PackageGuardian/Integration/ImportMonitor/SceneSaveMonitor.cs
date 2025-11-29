using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using YUCP.Components.PackageGuardian.Editor.Services;
using YUCP.Components.PackageGuardian.Editor.Settings;
using global::PackageGuardian.Core.Diff;

namespace YUCP.Components.PackageGuardian.Editor.Integration.ImportMonitor
{
    [InitializeOnLoad]
    public static class SceneSaveMonitor
    {
        static SceneSaveMonitor()
        {
            EditorSceneManager.sceneSaved += OnSceneSaved;
        }

        private static void OnSceneSaved(Scene scene)
        {
            try
            {
                if (!PackageGuardianSettings.IsEnabled())
                    return;
                    
                var settings = PackageGuardianSettings.Instance;
                if (settings == null || !settings.autoStashOnSceneSave)
                    return;

                string scenePath = scene.path;
                var service = RepositoryService.Instance;
                
                string baseMessage = string.IsNullOrEmpty(scenePath)
                    ? "After Scene Save"
                    : $"After Scene Save: {scenePath}";
                
                _ = GuardianTaskRunner.Run("Compute Scene Save Summary", ct =>
                {
                    try
                    {
                        string summary = ComputeChangeSummary();
                        return $"{baseMessage} - {summary}";
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Package Guardian] Failed to compute change summary: {ex.Message}");
                        return baseMessage;
                    }
                }).ContinueWith(summaryTask =>
                {
                    if (summaryTask.IsFaulted)
                    {
                        Debug.LogWarning($"[Package Guardian] Failed to compute change summary: {summaryTask.Exception?.GetBaseException().Message}");
                        _ = service.CreateAutoStashAsync(baseMessage).ContinueWith(stashTask =>
                        {
                            if (stashTask.IsFaulted)
                            {
                                Debug.LogWarning($"[Package Guardian] Failed to create scene save stash: {stashTask.Exception?.GetBaseException().Message}");
                            }
                            else
                            {
                                Debug.Log($"[Package Guardian] Auto-stash queued for scene save");
                            }
                        });
                        return;
                    }
                    
                    string message = summaryTask.Result;
                    _ = service.CreateAutoStashAsync(message).ContinueWith(stashTask =>
                    {
                        if (stashTask.IsFaulted)
                        {
                            Debug.LogWarning($"[Package Guardian] Failed to create scene save stash: {stashTask.Exception?.GetBaseException().Message}");
                        }
                        else
                        {
                            Debug.Log($"[Package Guardian] Auto-stash queued for scene save");
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Package Guardian] Failed to create scene save stash: {ex.Message}");
            }
        }

        private static string ComputeChangeSummary()
        {
            try
            {
                var repo = RepositoryService.Instance.Repository;
                var currentTreeOid = repo.Snapshots.BuildTreeFromDisk(repo.Root, new System.Collections.Generic.List<string> { "Assets", "Packages" });
                var headCommit = repo.Refs.ResolveHead();
                string oldTreeOid = null;
                if (!string.IsNullOrEmpty(headCommit))
                {
                    var commit = repo.Store.ReadObject(headCommit) as global::PackageGuardian.Core.Objects.Commit;
                    if (commit != null) oldTreeOid = repo.Hasher.ToHex(commit.TreeId);
                }
                var diffEngine = new DiffEngine(repo.Store);
                var changes = diffEngine.CompareTrees("", oldTreeOid, currentTreeOid);
                int added = 0, modified = 0, deleted = 0;
                foreach (var c in changes)
                {
                    if (c.Type == global::PackageGuardian.Core.Diff.ChangeType.Added) added++;
                    else if (c.Type == global::PackageGuardian.Core.Diff.ChangeType.Modified) modified++;
                    else if (c.Type == global::PackageGuardian.Core.Diff.ChangeType.Deleted) deleted++;
                }
                return $"{changes.Count} file(s) changed (+{added} ~{modified} -{deleted})";
            }
            catch
            {
                return "changes recorded";
            }
        }
    }
}



