using System;
using System.Collections.Generic;
using System.Linq;

namespace YUCP.Components.PackageGuardian.Editor.Windows.Graph
{
    /// <summary>
    /// Represents a group of commits organized by time period or category.
    /// </summary>
    public class CommitGroup
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public List<GraphNode> Commits { get; set; }
        public bool IsStashGroup { get; set; }
        public bool IsExpanded { get; set; }
        public DateTimeOffset GroupDate { get; set; }
        
        public CommitGroup()
        {
            Commits = new List<GraphNode>();
            IsExpanded = true;
        }
        
        public int Count => Commits.Count;
    }
    
    /// <summary>
    /// Organizes commits into groups by date and separates stashes.
    /// </summary>
    public static class CommitGrouper
    {
        public static List<CommitGroup> GroupCommits(List<GraphNode> allNodes, bool showStashesSeparately = true)
        {
            var groups = new List<CommitGroup>();
            var now = DateTimeOffset.Now;
            
            // Separate commits and stashes
            var commits = allNodes.Where(n => !n.IsStash).ToList();
            var stashes = allNodes.Where(n => n.IsStash).ToList();
            
            // Group commits by time period
            var commitGroups = GroupByTimePeriod(commits, now);
            groups.AddRange(commitGroups);
            
            // Add stashes as separate collapsible groups if enabled
            if (showStashesSeparately && stashes.Count > 0)
            {
                var stashGroups = GroupStashesByTimePeriod(stashes, now);
                groups.AddRange(stashGroups);
            }
            else if (!showStashesSeparately)
            {
                // Mix stashes with commits if not separating
                var allMixed = allNodes.OrderByDescending(n => n.Timestamp).ToList();
                groups = GroupByTimePeriod(allMixed, now);
            }
            
            return groups.OrderByDescending(g => g.GroupDate).ToList();
        }
        
        private static List<CommitGroup> GroupByTimePeriod(List<GraphNode> commits, DateTimeOffset now)
        {
            var groups = new List<CommitGroup>();
            
            if (commits.Count == 0)
                return groups;
            
            // Sort by timestamp descending
            var sorted = commits.OrderByDescending(c => c.Timestamp).ToList();
            
            CommitGroup currentGroup = null;
            
            foreach (var commit in sorted)
            {
                var commitDate = DateTimeOffset.FromUnixTimeSeconds(commit.Timestamp);
                var timeDiff = now - commitDate;
                
                string groupTitle;
                string groupSubtitle;
                DateTimeOffset groupDate;
                
                // Determine group using time
                if (timeDiff.TotalHours < 1)
                {
                    groupTitle = "Just Now";
                    groupSubtitle = "Last hour";
                    groupDate = commitDate;
                }
                else if (timeDiff.TotalDays < 1)
                {
                    int hours = (int)timeDiff.TotalHours;
                    groupTitle = $"Today";
                    groupSubtitle = $"{hours} hour{(hours != 1 ? "s" : "")} ago";
                    groupDate = now.Date;
                }
                else if (timeDiff.TotalDays < 2)
                {
                    groupTitle = "Yesterday";
                    groupSubtitle = commitDate.ToString("MMM dd");
                    groupDate = now.Date.AddDays(-1);
                }
                else if (timeDiff.TotalDays < 7)
                {
                    int days = (int)timeDiff.TotalDays;
                    groupTitle = $"This Week";
                    groupSubtitle = $"{days} day{(days != 1 ? "s" : "")} ago";
                    groupDate = now.Date.AddDays(-days);
                }
                else if (timeDiff.TotalDays < 30)
                {
                    int weeks = (int)(timeDiff.TotalDays / 7);
                    groupTitle = $"This Month";
                    groupSubtitle = $"{weeks} week{(weeks != 1 ? "s" : "")} ago";
                    groupDate = now.Date.AddDays(-(int)timeDiff.TotalDays);
                }
                else if (timeDiff.TotalDays < 365)
                {
                    int months = (int)(timeDiff.TotalDays / 30);
                    groupTitle = commitDate.ToString("MMMM yyyy");
                    groupSubtitle = $"{months} month{(months != 1 ? "s" : "")} ago";
                    groupDate = new DateTimeOffset(commitDate.Year, commitDate.Month, 1, 0, 0, 0, commitDate.Offset);
                }
                else
                {
                    groupTitle = commitDate.ToString("yyyy");
                    groupSubtitle = commitDate.ToString("MMMM yyyy");
                    groupDate = new DateTimeOffset(commitDate.Year, 1, 1, 0, 0, 0, commitDate.Offset);
                }
                
                // Check if we need a new group
                if (currentGroup == null || currentGroup.Title != groupTitle)
                {
                    currentGroup = new CommitGroup
                    {
                        Title = groupTitle,
                        Subtitle = groupSubtitle,
                        GroupDate = groupDate,
                        IsStashGroup = false
                    };
                    groups.Add(currentGroup);
                }
                
                currentGroup.Commits.Add(commit);
            }
            
            return groups;
        }
        
        private static List<CommitGroup> GroupStashesByTimePeriod(List<GraphNode> stashes, DateTimeOffset now)
        {
            var groups = new List<CommitGroup>();
            
            if (stashes.Count == 0)
                return groups;
            
            // Group stashes by date (less granular than commits)
            var sorted = stashes.OrderByDescending(s => s.Timestamp).ToList();
            
            CommitGroup currentGroup = null;
            
            foreach (var stash in sorted)
            {
                var stashDate = DateTimeOffset.FromUnixTimeSeconds(stash.Timestamp);
                var timeDiff = now - stashDate;
                
                string groupTitle;
                DateTimeOffset groupDate;
                
                if (timeDiff.TotalDays < 1)
                {
                    groupTitle = "Stashes - Today";
                    groupDate = now.Date;
                }
                else if (timeDiff.TotalDays < 7)
                {
                    groupTitle = "Stashes - This Week";
                    groupDate = now.Date.AddDays(-(int)timeDiff.TotalDays);
                }
                else if (timeDiff.TotalDays < 30)
                {
                    groupTitle = $"Stashes - {stashDate:MMMM yyyy}";
                    groupDate = new DateTimeOffset(stashDate.Year, stashDate.Month, 1, 0, 0, 0, stashDate.Offset);
                }
                else
                {
                    groupTitle = $"Stashes - {stashDate:yyyy}";
                    groupDate = new DateTimeOffset(stashDate.Year, 1, 1, 0, 0, 0, stashDate.Offset);
                }
                
                if (currentGroup == null || currentGroup.Title != groupTitle)
                {
                    // Count stashes that belong to this group
                    int count = sorted.Count(s =>
                    {
                        var sDate = DateTimeOffset.FromUnixTimeSeconds(s.Timestamp);
                        var sDiff = now - sDate;
                        return (sDiff.TotalDays < 1 && groupTitle.Contains("Today")) ||
                               (sDiff.TotalDays >= 1 && sDiff.TotalDays < 7 && groupTitle.Contains("This Week")) ||
                               (sDiff.TotalDays >= 7 && sDiff.TotalDays < 30 && groupTitle.Contains(stashDate.ToString("MMMM"))) ||
                               (sDiff.TotalDays >= 30 && groupTitle.Contains(stashDate.ToString("yyyy")));
                    });
                    
                    currentGroup = new CommitGroup
                    {
                        Title = groupTitle,
                        Subtitle = $"{count} stash{(count != 1 ? "es" : "")}",
                        GroupDate = groupDate,
                        IsStashGroup = true,
                        IsExpanded = false // Stashes collapsed by default
                    };
                    groups.Add(currentGroup);
                }
                
                currentGroup.Commits.Add(stash);
            }
            
            return groups;
        }
    }
}

