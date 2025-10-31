using System;
using System.Collections.Generic;
using System.Linq;
using PackageGuardian.Core.Objects;
using PackageGuardian.Core.Repository;
using YUCP.Components.PackageGuardian.Editor.Services;

namespace YUCP.Components.PackageGuardian.Editor.Windows.Dashboard
{
    /// <summary>
    /// View model for dashboard data.
    /// </summary>
    public class DashboardViewModel
    {
        public string CurrentRef { get; private set; }
        public string LastSnapshotTime { get; private set; }
        public int PendingChanges { get; private set; }
        public List<ActivityItem> RecentActivity { get; private set; }
        
        public DashboardViewModel()
        {
            RecentActivity = new List<ActivityItem>();
        }
        
        public void Refresh()
        {
            try
            {
                var service = RepositoryService.Instance;
                var repo = service.Repository;
                
                // Get current ref
                CurrentRef = repo.Refs.HeadRef;
                if (CurrentRef.StartsWith("refs/heads/"))
                {
                    CurrentRef = CurrentRef.Substring("refs/heads/".Length);
                }
                
                // Get last snapshot time
                string headCommitId = repo.Refs.ResolveHead();
                if (!string.IsNullOrWhiteSpace(headCommitId))
                {
                    try
                    {
                        var commitObj = repo.Store.ReadObject(headCommitId);
                        if (commitObj is Commit commit)
                        {
                            var dt = DateTimeOffset.FromUnixTimeSeconds(commit.Timestamp);
                            LastSnapshotTime = dt.ToLocalTime().ToString("g");
                        }
                        else
                        {
                            LastSnapshotTime = "Never";
                        }
                    }
                    catch
                    {
                        LastSnapshotTime = "Unknown";
                    }
                }
                else
                {
                    LastSnapshotTime = "Never";
                }
                
                // TODO: Calculate pending changes by comparing working dir to HEAD
                PendingChanges = 0;
                
                // Load recent activity (last 20 commits and stashes)
                LoadRecentActivity(repo);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[Package Guardian] Failed to refresh dashboard: {ex.Message}");
                CurrentRef = "Error";
                LastSnapshotTime = "Error";
                PendingChanges = 0;
                RecentActivity = new List<ActivityItem>();
            }
        }
        
        private void LoadRecentActivity(Repository repo)
        {
            var activity = new List<ActivityItem>();
            
            // Load commits from all branches (simplified - just main branch for now)
            try
            {
                string headCommitId = repo.Refs.ResolveHead();
                if (!string.IsNullOrWhiteSpace(headCommitId))
                {
                    // Walk commit history
                    var visited = new HashSet<string>();
                    var queue = new Queue<string>();
                    queue.Enqueue(headCommitId);
                    
                    while (queue.Count > 0 && activity.Count < 20)
                    {
                        string commitId = queue.Dequeue();
                        if (visited.Contains(commitId))
                            continue;
                        visited.Add(commitId);
                        
                        try
                        {
                            var commitObj = repo.Store.ReadObject(commitId);
                            if (commitObj is Commit commit)
                            {
                                activity.Add(new ActivityItem
                                {
                                    Type = ActivityType.Commit,
                                    CommitId = commitId,
                                    Message = commit.Message,
                                    Author = commit.Author,
                                    Timestamp = commit.Timestamp
                                });
                                
                                // Queue parents
                                foreach (var parentId in commit.Parents)
                                {
                                    string parentHex = BytesToHex(parentId);
                                    queue.Enqueue(parentHex);
                                }
                            }
                        }
                        catch
                        {
                            // Skip invalid commits
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Package Guardian] Failed to load commits: {ex.Message}");
            }
            
            // Load stashes
            try
            {
                var stashes = repo.Stash.List();
                foreach (var stash in stashes.Take(10))
                {
                    activity.Add(new ActivityItem
                    {
                        Type = ActivityType.Stash,
                        CommitId = stash.CommitId,
                        Message = stash.Message,
                        Author = "",
                        Timestamp = stash.Timestamp
                    });
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Package Guardian] Failed to load stashes: {ex.Message}");
            }
            
            // Sort by timestamp descending
            RecentActivity = activity.OrderByDescending(a => a.Timestamp).Take(20).ToList();
        }
        
        private string BytesToHex(byte[] bytes)
        {
            return string.Concat(bytes.Select(b => b.ToString("x2")));
        }
    }
    
    public class ActivityItem
    {
        public ActivityType Type { get; set; }
        public string CommitId { get; set; }
        public string Message { get; set; }
        public string Author { get; set; }
        public long Timestamp { get; set; }
        
        public string GetTimeDisplay()
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(Timestamp);
            return dt.ToLocalTime().ToString("g");
        }
    }
    
    public enum ActivityType
    {
        Commit,
        Stash
    }
}

