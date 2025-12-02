using UnityEditor;
using UnityEngine;
using YUCP.Components.Editor.SupportBanner;

namespace YUCP.Components.Editor.SupportBanner
{
    /// <summary>
    /// Temporary debug menu for testing milestones.
    /// </summary>
    public static class MilestoneDebugMenu
    {
        [MenuItem("YUCP/Milestones/Debug/Increment Export Count")]
        private static void IncrementExportCount()
        {
            MilestoneTracker.IncrementExportCount();
            Debug.Log("[MilestoneDebug] Manually incremented export count");
        }
        
        [MenuItem("YUCP/Milestones/Debug/Increment Component Usage")]
        private static void IncrementComponentUsage()
        {
            MilestoneTracker.IncrementComponentUsage();
            Debug.Log("[MilestoneDebug] Manually incremented component usage");
        }
        
        [MenuItem("YUCP/Milestones/Debug/Increment Profile Count")]
        private static void IncrementProfileCount()
        {
            MilestoneTracker.IncrementProfileCount();
            Debug.Log("[MilestoneDebug] Manually incremented profile count");
        }
        
        [MenuItem("YUCP/Milestones/Debug/Set Export Count to 1")]
        private static void SetExportCount1()
        {
            EditorPrefs.SetInt("com.yucp.components.milestone.export_count", 1);
            MilestoneTracker.GetCurrentMilestone(); // Trigger update
            Debug.Log("[MilestoneDebug] Set export count to 1");
        }
        
        [MenuItem("YUCP/Milestones/Debug/Set Export Count to 5")]
        private static void SetExportCount5()
        {
            EditorPrefs.SetInt("com.yucp.components.milestone.export_count", 5);
            MilestoneTracker.GetCurrentMilestone(); // Trigger update
            Debug.Log("[MilestoneDebug] Set export count to 5");
        }
        
        [MenuItem("YUCP/Milestones/Debug/Set Export Count to 67")]
        private static void SetExportCount67()
        {
            EditorPrefs.SetInt("com.yucp.components.milestone.export_count", 67);
            MilestoneTracker.GetCurrentMilestone(); // Trigger update
            Debug.Log("[MilestoneDebug] Set export count to 67 (SIXSEVENNNN)");
        }
        
        [MenuItem("YUCP/Milestones/Debug/Set Export Count to 69")]
        private static void SetExportCount69()
        {
            EditorPrefs.SetInt("com.yucp.components.milestone.export_count", 69);
            MilestoneTracker.GetCurrentMilestone(); // Trigger update
            Debug.Log("[MilestoneDebug] Set export count to 69 (Nice)");
        }
        
        [MenuItem("YUCP/Milestones/Debug/Update Profile Count from Assets")]
        private static void UpdateProfileCount()
        {
            MilestoneTracker.UpdateProfileCountFromAssets();
            Debug.Log("[MilestoneDebug] Profile count updated from existing assets");
        }
        
        [MenuItem("YUCP/Milestones/Debug/Show Current Status")]
        private static void ShowCurrentStatus()
        {
            var milestone = MilestoneTracker.GetCurrentMilestone();
            Debug.Log($"[MilestoneDebug] === MILESTONE STATUS ===");
            Debug.Log($"[MilestoneDebug] Export Count: {EditorPrefs.GetInt("com.yucp.components.milestone.export_count", 0)}");
            Debug.Log($"[MilestoneDebug] Component Usage: {EditorPrefs.GetInt("com.yucp.components.milestone.component_usage", 0)}");
            Debug.Log($"[MilestoneDebug] Profile Count: {EditorPrefs.GetInt("com.yucp.components.milestone.profile_count", 0)}");
            Debug.Log($"[MilestoneDebug] Export Streak: {EditorPrefs.GetInt("com.yucp.components.milestone.export_streak", 0)}");
            Debug.Log($"[MilestoneDebug] Component Count on Avatar: {EditorPrefs.GetInt("com.yucp.components.milestone.component_count_avatar", 0)}");
            Debug.Log($"[MilestoneDebug] Achieved Milestones: {EditorPrefs.GetString("com.yucp.components.milestone.achieved", "none")}");
            if (milestone != null)
            {
                Debug.Log($"[MilestoneDebug] Current Milestone: {milestone.Title} ({milestone.Id})");
            }
            else
            {
                Debug.Log($"[MilestoneDebug] Current Milestone: None");
            }
            Debug.Log($"[MilestoneDebug] =========================");
        }
        
        [MenuItem("YUCP/Milestones/Debug/Reset All Milestones")]
        private static void ResetAllMilestones()
        {
            EditorPrefs.DeleteKey("com.yucp.components.milestone.export_count");
            EditorPrefs.DeleteKey("com.yucp.components.milestone.component_usage");
            EditorPrefs.DeleteKey("com.yucp.components.milestone.profile_count");
            EditorPrefs.DeleteKey("com.yucp.components.milestone.export_streak");
            EditorPrefs.DeleteKey("com.yucp.components.milestone.perfect_streak");
            EditorPrefs.DeleteKey("com.yucp.components.milestone.clean_builds");
            EditorPrefs.DeleteKey("com.yucp.components.milestone.component_count_avatar");
            EditorPrefs.DeleteKey("com.yucp.components.milestone.achieved");
            EditorPrefs.DeleteKey("com.yucp.components.milestone.last_shown");
            EditorPrefs.DeleteKey("com.yucp.components.milestone.first_usage_date");
            EditorPrefs.DeleteKey("com.yucp.components.milestone.last_export_date");
            Debug.Log("[MilestoneDebug] All milestone data reset!");
        }
    }
}

