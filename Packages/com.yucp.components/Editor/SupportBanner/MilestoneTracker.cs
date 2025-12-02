using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.SupportBanner
{
    /// <summary>
    /// Tracks milestones and determines which milestone to display.
    /// </summary>
    public static class MilestoneTracker
    {
        private const string PrefComponentUsageCount = "com.yucp.components.milestone.component_usage";
        private const string PrefExportCount = "com.yucp.components.milestone.export_count";
        private const string PrefProfileCount = "com.yucp.components.milestone.profile_count";
        private const string PrefFirstUsageDate = "com.yucp.components.milestone.first_usage_date";
        private const string PrefPerfectStreak = "com.yucp.components.milestone.perfect_streak";
        private const string PrefCleanBuilds = "com.yucp.components.milestone.clean_builds";
        private const string PrefComponentCountOnAvatar = "com.yucp.components.milestone.component_count_avatar";
        private const string PrefLastExportDate = "com.yucp.components.milestone.last_export_date";
        private const string PrefExportStreak = "com.yucp.components.milestone.export_streak";
        private const string PrefAchievedMilestones = "com.yucp.components.milestone.achieved";
        private const string PrefLastMilestoneShown = "com.yucp.components.milestone.last_shown";
        
        private static List<Milestone> _milestones;
        private static Milestone _currentMilestone;
        
        static MilestoneTracker()
        {
            InitializeMilestones();
            
            // Update profile count from existing assets on initialization
            EditorApplication.delayCall += () => {
                UpdateProfileCountFromAssets();
            };
        }
        
        /// <summary>
        /// Gets the current milestone to display, or null if none.
        /// </summary>
        public static Milestone GetCurrentMilestone()
        {
            UpdateMilestones();
            
            return _currentMilestone;
        }
        
        /// <summary>
        /// Increments component usage count.
        /// </summary>
        public static void IncrementComponentUsage()
        {
            int count = EditorPrefs.GetInt(PrefComponentUsageCount, 0) + 1;
            EditorPrefs.SetInt(PrefComponentUsageCount, count);
            
            // Track first usage date
            if (EditorPrefs.GetString(PrefFirstUsageDate, "") == "")
            {
                EditorPrefs.SetString(PrefFirstUsageDate, DateTime.Now.ToString("yyyy-MM-dd"));
            }
            
            UpdateMilestones();
        }
        
        /// <summary>
        /// Increments export count (called from devtools).
        /// </summary>
        public static void IncrementExportCount()
        {
            int count = EditorPrefs.GetInt(PrefExportCount, 0) + 1;
            EditorPrefs.SetInt(PrefExportCount, count);
            
            // Update export streak
            string lastDateStr = EditorPrefs.GetString(PrefLastExportDate, "");
            string todayStr = DateTime.Now.ToString("yyyy-MM-dd");
            
            if (lastDateStr == todayStr)
            {
                // Already exported today, don't increment streak
            }
            else if (lastDateStr == "")
            {
                // First export
                EditorPrefs.SetInt(PrefExportStreak, 1);
            }
            else
            {
                DateTime lastDate = DateTime.Parse(lastDateStr);
                DateTime today = DateTime.Parse(todayStr);
                
                if ((today - lastDate).Days == 1)
                {
                    // Consecutive day
                    int streak = EditorPrefs.GetInt(PrefExportStreak, 0) + 1;
                    EditorPrefs.SetInt(PrefExportStreak, streak);
                }
                else
                {
                    // Streak broken
                    EditorPrefs.SetInt(PrefExportStreak, 1);
                }
            }
            
            EditorPrefs.SetString(PrefLastExportDate, todayStr);
            UpdateMilestones();
        }
        
        /// <summary>
        /// Sets the component count on the current avatar.
        /// </summary>
        public static void SetComponentCountOnAvatar(int count)
        {
            EditorPrefs.SetInt(PrefComponentCountOnAvatar, count);
            UpdateMilestones();
        }
        
        /// <summary>
        /// Increments profile count.
        /// </summary>
        public static void IncrementProfileCount()
        {
            int count = EditorPrefs.GetInt(PrefProfileCount, 0) + 1;
            EditorPrefs.SetInt(PrefProfileCount, count);
            UpdateMilestones();
        }
        
        /// <summary>
        /// Updates profile count by counting existing ExportProfile assets.
        /// </summary>
        public static void UpdateProfileCountFromAssets()
        {
            try
            {
                // Find all ExportProfile assets
                string[] guids = AssetDatabase.FindAssets("t:ExportProfile");
                int count = guids.Length;
                
                // Always update to reflect current count (can increase or decrease)
                int storedCount = EditorPrefs.GetInt(PrefProfileCount, 0);
                if (count != storedCount)
                {
                    EditorPrefs.SetInt(PrefProfileCount, count);
                    UpdateMilestones();
                }
            }
            catch
            {
                // Silently fail
            }
        }
        
        /// <summary>
        /// Increments perfect streak.
        /// </summary>
        public static void IncrementPerfectStreak()
        {
            int streak = EditorPrefs.GetInt(PrefPerfectStreak, 0) + 1;
            EditorPrefs.SetInt(PrefPerfectStreak, streak);
            UpdateMilestones();
        }
        
        /// <summary>
        /// Resets perfect streak.
        /// </summary>
        public static void ResetPerfectStreak()
        {
            EditorPrefs.SetInt(PrefPerfectStreak, 0);
            UpdateMilestones();
        }
        
        /// <summary>
        /// Increments clean builds count.
        /// </summary>
        public static void IncrementCleanBuilds()
        {
            int count = EditorPrefs.GetInt(PrefCleanBuilds, 0) + 1;
            EditorPrefs.SetInt(PrefCleanBuilds, count);
            UpdateMilestones();
        }
        
        private static void InitializeMilestones()
        {
            _milestones = new List<Milestone>();
            
            // Export Milestones
            AddMilestone("export_1", "First Package Exported", "You've exported your first package! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() >= 1);
            AddMilestone("export_5", "Getting Started", "You've exported 5 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() >= 5);
            AddMilestone("export_10", "Building Momentum", "You've exported 10 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() >= 10);
            AddMilestone("export_25", "Quarter Century", "You've exported 25 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() >= 25);
            AddMilestone("export_50", "Half Century", "You've exported 50 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() >= 50);
            AddMilestone("export_100", "Century Club", "You've exported 100 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() >= 100);
            AddMilestone("export_250", "Power User", "You've exported 250 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() >= 250);
            AddMilestone("export_500", "Export Virtuoso", "You've exported 500 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() >= 500);
            AddMilestone("export_1000", "Export Legend", "You've exported 1,000 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() >= 1000);
            AddMilestone("export_2500", "Export Champion", "You've exported 2,500 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() >= 2500);
            AddMilestone("export_5000", "Export Wizard", "You've exported 5,000 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() >= 5000);
            AddMilestone("export_10000", "Export Deity", "You've exported 10,000 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() >= 10000);
            
            // Component Usage Milestones
            AddMilestone("component_1", "Component Newbie", "You've used your first component! Your support keeps these tools free and helps create more amazing features!", c => GetComponentUsageCount() >= 1);
            AddMilestone("component_5", "Component Starter", "You've used 5 components! Your support keeps these tools free and helps create more amazing features!", c => GetComponentUsageCount() >= 5);
            AddMilestone("component_10", "Component Explorer", "You've used 10 components! Your support keeps these tools free and helps create more amazing features!", c => GetComponentUsageCount() >= 10);
            AddMilestone("component_20", "Component Enthusiast", "You've used 20 components! Your support keeps these tools free and helps create more amazing features!", c => GetComponentUsageCount() >= 20);
            AddMilestone("component_25", "Component Ace", "You've used 25 components! Your support keeps these tools free and helps create more amazing features!", c => GetComponentUsageCount() >= 25);
            
            // Special Numbers (Reduced)
            AddMilestone("export_42", "Answer to Everything", "You've exported 42 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() == 42);
            AddMilestone("export_67", "SIXSEVENNNN", "You've exported 67 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() == 67);
            AddMilestone("export_69", "Nice", "You've exported 69 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() == 69);
            AddMilestone("export_420", "Blaze It", "You've exported 420 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() == 420);
            AddMilestone("export_666", "Number of the Beast", "You've exported 666 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() == 666);
            AddMilestone("export_777", "Lucky Sevens", "You've exported 777 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() == 777);
            AddMilestone("export_1337", "Leet Exporter", "You've exported 1337 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() == 1337);
            AddMilestone("export_2024", "Year Exporter", "You've exported 2024 packages! Your support keeps these tools free and helps create more amazing features!", c => GetExportCount() == 2024);
            
            // Component Count Milestones (Sparse)
            AddMilestone("avatar_5", "Multi-Component", "You have 5 components on your avatar! Your support keeps these tools free and helps create more amazing features!", c => GetComponentCountOnAvatar() >= 5);
            AddMilestone("avatar_10", "Component Heavy", "You have 10 components on your avatar! Your support keeps these tools free and helps create more amazing features!", c => GetComponentCountOnAvatar() >= 10);
            AddMilestone("avatar_20", "Component Maxed", "You have 20 components on your avatar! Your support keeps these tools free and helps create more amazing features!", c => GetComponentCountOnAvatar() >= 20);
            AddMilestone("avatar_25", "Component Overload", "You have 25+ components on your avatar! Your support keeps these tools free and helps create more amazing features!", c => GetComponentCountOnAvatar() >= 25);
            
            // Consistency Milestones
            AddMilestone("streak_3", "Three Day Streak", "You've exported for 3 days in a row! Your support keeps these tools free and helps create more amazing features!", c => GetExportStreak() >= 3);
            AddMilestone("streak_7", "Week Streak", "You've exported for 7 days in a row! Your support keeps these tools free and helps create more amazing features!", c => GetExportStreak() >= 7);
            AddMilestone("streak_14", "Two Week Streak", "You've exported for 14 days in a row! Your support keeps these tools free and helps create more amazing features!", c => GetExportStreak() >= 14);
            AddMilestone("streak_30", "Month Streak", "You've exported for 30 days in a row! Your support keeps these tools free and helps create more amazing features!", c => GetExportStreak() >= 30);
            AddMilestone("streak_100", "Century Streak", "You've exported for 100 days in a row! Your support keeps these tools free and helps create more amazing features!", c => GetExportStreak() >= 100);
            
            // Quality Milestones
            AddMilestone("perfect_10", "Perfect Streak", "You've had 10 perfect exports in a row! Your support keeps these tools free and helps create more amazing features!", c => GetPerfectStreak() >= 10);
            AddMilestone("perfect_50", "Flawless Flow", "You've had 50 perfect exports in a row! Your support keeps these tools free and helps create more amazing features!", c => GetPerfectStreak() >= 50);
            AddMilestone("perfect_100", "Perfect Ace", "You've had 100 perfect exports in a row! Your support keeps these tools free and helps create more amazing features!", c => GetPerfectStreak() >= 100);
            
            // Profile Milestones
            AddMilestone("profile_3", "Profile Pioneer", "You've created 3 profiles! Your support keeps these tools free and helps create more amazing features!", c => GetProfileCount() >= 3);
            AddMilestone("profile_10", "Profile Pro", "You've created 10 profiles! Your support keeps these tools free and helps create more amazing features!", c => GetProfileCount() >= 10);
            AddMilestone("profile_25", "Profile Powerhouse", "You've created 25 profiles! Your support keeps these tools free and helps create more amazing features!", c => GetProfileCount() >= 25);
            AddMilestone("profile_50", "Profile Perfectionist", "You've created 50 profiles! Your support keeps these tools free and helps create more amazing features!", c => GetProfileCount() >= 50);
            AddMilestone("profile_100", "Profile Virtuoso", "You've created 100 profiles! Your support keeps these tools free and helps create more amazing features!", c => GetProfileCount() >= 100);
            
            // Achievement Milestones
            AddMilestone("achievement_5", "Milestone Collector", "You've achieved 5 milestones! Your support keeps these tools free and helps create more amazing features!", c => GetAchievedMilestoneCount() >= 5);
            AddMilestone("achievement_10", "Milestone Ace", "You've achieved 10 milestones! Your support keeps these tools free and helps create more amazing features!", c => GetAchievedMilestoneCount() >= 10);
            AddMilestone("achievement_25", "Milestone Champion", "You've achieved 25 milestones! Your support keeps these tools free and helps create more amazing features!", c => GetAchievedMilestoneCount() >= 25);
            AddMilestone("achievement_50", "Milestone Virtuoso", "You've achieved 50 milestones! Your support keeps these tools free and helps create more amazing features!", c => GetAchievedMilestoneCount() >= 50);
            AddMilestone("achievement_100", "Century of Milestones", "You've achieved 100 milestones! Your support keeps these tools free and helps create more amazing features!", c => GetAchievedMilestoneCount() >= 100);
        }
        
        private static void AddMilestone(string id, string title, string subtitle, Func<int, bool> checkFunction)
        {
            _milestones.Add(new Milestone(id, title, subtitle, checkFunction));
        }
        
        private static void UpdateMilestones()
        {
            // Get all achieved milestones
            var achieved = new HashSet<string>(GetAchievedMilestoneIds());
            var newlyAchieved = new List<Milestone>();
            
            // Check all milestones
            foreach (var milestone in _milestones)
            {
                bool isAchieved = milestone.CheckFunction(0);
                
                if (isAchieved && !achieved.Contains(milestone.Id))
                {
                    // Newly achieved
                    newlyAchieved.Add(milestone);
                    achieved.Add(milestone.Id);
                }
            }
            
            // Save achieved milestones
            SaveAchievedMilestones(achieved);
            
            // Determine current milestone to show
            // Priority: newly achieved > highest achievement > most recent
            if (newlyAchieved.Count > 0)
            {
                // Show the most impressive newly achieved milestone
                _currentMilestone = newlyAchieved.OrderByDescending(m => GetMilestonePriority(m)).First();
                EditorPrefs.SetString(PrefLastMilestoneShown, _currentMilestone.Id);
            }
            else
            {
                // Show the most impressive achieved milestone that hasn't been shown recently
                var allAchieved = _milestones.Where(m => achieved.Contains(m.Id))
                    .OrderByDescending(m => GetMilestonePriority(m))
                    .ToList();
                
                if (allAchieved.Count > 0)
                {
                    string lastShown = EditorPrefs.GetString(PrefLastMilestoneShown, "");
                    
                    // Try to show a different milestone than last time
                    var toShow = allAchieved.FirstOrDefault(m => m.Id != lastShown) ?? allAchieved.First();
                    _currentMilestone = toShow;
                    EditorPrefs.SetString(PrefLastMilestoneShown, toShow.Id);
                }
                else
                {
                    _currentMilestone = null;
                }
            }
        }
        
        private static int GetMilestonePriority(Milestone milestone)
        {
            // Higher priority for more impressive milestones
            if (milestone.Id.StartsWith("export_"))
            {
                if (milestone.Id.Contains("10000")) return 1000;
                if (milestone.Id.Contains("5000")) return 900;
                if (milestone.Id.Contains("2500")) return 800;
                if (milestone.Id.Contains("1000")) return 700;
                if (milestone.Id.Contains("500")) return 600;
                if (milestone.Id.Contains("250")) return 500;
                if (milestone.Id.Contains("100")) return 400;
                if (milestone.Id.Contains("50")) return 300;
                if (milestone.Id.Contains("25")) return 200;
                if (milestone.Id.Contains("10")) return 100;
                return 50;
            }
            
            if (milestone.Id.StartsWith("streak_"))
            {
                if (milestone.Id.Contains("100")) return 950;
                if (milestone.Id.Contains("30")) return 850;
                if (milestone.Id.Contains("14")) return 750;
                if (milestone.Id.Contains("7")) return 650;
                return 550;
            }
            
            if (milestone.Id.StartsWith("perfect_"))
            {
                if (milestone.Id.Contains("100")) return 900;
                if (milestone.Id.Contains("50")) return 800;
                return 700;
            }
            
            // Special numbers get high priority
            if (milestone.Id.Contains("67") || milestone.Id.Contains("69") || milestone.Id.Contains("420") || 
                milestone.Id.Contains("666") || milestone.Id.Contains("777") || milestone.Id.Contains("1337"))
            {
                return 850;
            }
            
            return 100;
        }
        
        private static int GetExportCount() => EditorPrefs.GetInt(PrefExportCount, 0);
        private static int GetComponentUsageCount() => EditorPrefs.GetInt(PrefComponentUsageCount, 0);
        private static int GetProfileCount() => EditorPrefs.GetInt(PrefProfileCount, 0);
        private static int GetPerfectStreak() => EditorPrefs.GetInt(PrefPerfectStreak, 0);
        private static int GetExportStreak() => EditorPrefs.GetInt(PrefExportStreak, 0);
        private static int GetComponentCountOnAvatar() => EditorPrefs.GetInt(PrefComponentCountOnAvatar, 0);
        
        private static HashSet<string> GetAchievedMilestoneIds()
        {
            string achievedStr = EditorPrefs.GetString(PrefAchievedMilestones, "");
            if (string.IsNullOrEmpty(achievedStr))
                return new HashSet<string>();
            
            return new HashSet<string>(achievedStr.Split(';'));
        }
        
        private static void SaveAchievedMilestones(HashSet<string> achieved)
        {
            EditorPrefs.SetString(PrefAchievedMilestones, string.Join(";", achieved));
        }
        
        private static int GetAchievedMilestoneCount()
        {
            return GetAchievedMilestoneIds().Count;
        }
    }
}

