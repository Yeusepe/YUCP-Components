using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PackageGuardian.Core.Validation;

namespace YUCP.Components.PackageGuardian.Editor.Services
{
    /// <summary>
    /// Automated health monitoring service that performs scheduled checks
    /// </summary>
    [InitializeOnLoad]
    public static class HealthMonitorService
    {
        private static double _lastCheckTime;
        private static double _checkInterval = 300.0; // 5 minutes default
        private static List<ValidationIssue> _lastKnownIssues = new List<ValidationIssue>();
        private static bool _enabled = true;
        private static bool _showNotifications = true;
        
        static HealthMonitorService()
        {
            // Subscribe to editor update
            EditorApplication.update += OnEditorUpdate;
            
            // Load preferences
            LoadPreferences();
            
            // Run initial check after a short delay
            EditorApplication.delayCall += () => {
                _lastCheckTime = EditorApplication.timeSinceStartup;
            };
        }
        
        private static void OnEditorUpdate()
        {
            if (!_enabled) return;
            
            // Check if it's time for a health check
            if (EditorApplication.timeSinceStartup - _lastCheckTime >= _checkInterval)
            {
                _lastCheckTime = EditorApplication.timeSinceStartup;
                PerformHealthCheck();
            }
        }
        
        /// <summary>
        /// Performs a comprehensive health check
        /// </summary>
        public static void PerformHealthCheck()
        {
            try
            {
                var service = RepositoryService.Instance;
                var issues = new List<ValidationIssue>();
                
                // Get project validation issues
                issues.AddRange(service.ValidateProject());
                
                // Check for new critical issues
                var newCriticalIssues = issues.FindAll(i => 
                    i.Severity == IssueSeverity.Critical && 
                    !_lastKnownIssues.Exists(old => old.Title == i.Title));
                
                if (newCriticalIssues.Count > 0 && _showNotifications)
                {
                    // Show notification for new critical issues
                    foreach (var issue in newCriticalIssues)
                    {
                        Debug.LogError($"[Package Guardian] CRITICAL: {issue.Title} - {issue.Description}");
                    }
                    
                    if (EditorUtility.DisplayDialog(
                        "Package Guardian - Critical Issues Detected",
                        $"Found {newCriticalIssues.Count} new critical issue(s) that require immediate attention!\n\nWould you like to view them now?",
                        "View Issues",
                        "Dismiss"))
                    {
                        Windows.HealthWindow.ShowWindow();
                    }
                }
                
                // Check for new errors
                var newErrors = issues.FindAll(i => 
                    i.Severity == IssueSeverity.Error && 
                    !_lastKnownIssues.Exists(old => old.Title == i.Title));
                
                if (newErrors.Count > 0 && _showNotifications)
                {
                    Debug.LogWarning($"[Package Guardian] Found {newErrors.Count} new error(s). Run health check for details.");
                }
                
                // Update last known issues
                _lastKnownIssues = issues;
                
                // Log summary
                if (issues.Count > 0)
                {
                    var critical = issues.FindAll(i => i.Severity == IssueSeverity.Critical).Count;
                    var errors = issues.FindAll(i => i.Severity == IssueSeverity.Error).Count;
                    var warnings = issues.FindAll(i => i.Severity == IssueSeverity.Warning).Count;
                    
                    Debug.Log($"[Package Guardian] Health check complete: {critical} critical, {errors} errors, {warnings} warnings");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Package Guardian] Health check failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Gets the last health check results
        /// </summary>
        public static List<ValidationIssue> GetLastResults()
        {
            return new List<ValidationIssue>(_lastKnownIssues);
        }
        
        /// <summary>
        /// Sets the check interval in seconds
        /// </summary>
        public static void SetCheckInterval(double intervalSeconds)
        {
            _checkInterval = Math.Max(60.0, intervalSeconds); // Minimum 1 minute
            SavePreferences();
        }
        
        /// <summary>
        /// Gets the current check interval in seconds
        /// </summary>
        public static double GetCheckInterval()
        {
            return _checkInterval;
        }
        
        /// <summary>
        /// Enables or disables automatic health monitoring
        /// </summary>
        public static void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            SavePreferences();
            
            if (enabled)
            {
                Debug.Log("[Package Guardian] Automated health monitoring enabled");
            }
            else
            {
                Debug.Log("[Package Guardian] Automated health monitoring disabled");
            }
        }
        
        /// <summary>
        /// Gets whether automated monitoring is enabled
        /// </summary>
        public static bool IsEnabled()
        {
            return _enabled;
        }
        
        /// <summary>
        /// Enables or disables notifications
        /// </summary>
        public static void SetShowNotifications(bool show)
        {
            _showNotifications = show;
            SavePreferences();
        }
        
        /// <summary>
        /// Gets whether notifications are enabled
        /// </summary>
        public static bool GetShowNotifications()
        {
            return _showNotifications;
        }
        
        /// <summary>
        /// Forces an immediate health check
        /// </summary>
        [MenuItem("Tools/Package Guardian/Run Health Check", priority = 100)]
        public static void ForceHealthCheck()
        {
            Debug.Log("[Package Guardian] Running manual health check...");
            PerformHealthCheck();
            Windows.HealthWindow.ShowWindow();
        }
        
        private static void LoadPreferences()
        {
            _enabled = EditorPrefs.GetBool("PackageGuardian_HealthMonitor_Enabled", true);
            _checkInterval = EditorPrefs.GetFloat("PackageGuardian_HealthMonitor_Interval", 300f);
            _showNotifications = EditorPrefs.GetBool("PackageGuardian_HealthMonitor_Notifications", true);
        }
        
        private static void SavePreferences()
        {
            EditorPrefs.SetBool("PackageGuardian_HealthMonitor_Enabled", _enabled);
            EditorPrefs.SetFloat("PackageGuardian_HealthMonitor_Interval", (float)_checkInterval);
            EditorPrefs.SetBool("PackageGuardian_HealthMonitor_Notifications", _showNotifications);
        }
    }
}

