using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using PackageGuardian.Core.Validation;

namespace YUCP.Components.PackageGuardian.Editor.Services
{
    /// <summary>
    /// Generates comprehensive health reports for the project
    /// </summary>
    public static class HealthReportGenerator
    {
        /// <summary>
        /// Generates a comprehensive health report and saves it to file
        /// </summary>
        [MenuItem("Tools/Package Guardian/Generate Health Report", priority = 101)]
        public static void GenerateReport()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Package Guardian", "Generating health report...", 0.1f);
                
                var service = RepositoryService.Instance;
                var issues = new List<ValidationIssue>();
                
                // Collect all issues
                issues.AddRange(service.ValidateProject());
                issues.AddRange(service.ValidatePendingChanges());
                
                EditorUtility.DisplayProgressBar("Package Guardian", "Compiling report...", 0.5f);
                
                // Generate report
                var report = GenerateHealthReport(issues);
                
                // Save to file
                var reportsDir = Path.Combine(Application.dataPath, "..", "HealthReports");
                if (!Directory.Exists(reportsDir))
                {
                    Directory.CreateDirectory(reportsDir);
                }
                
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var reportPath = Path.Combine(reportsDir, $"HealthReport_{timestamp}.md");
                
                File.WriteAllText(reportPath, report);
                
                EditorUtility.ClearProgressBar();
                
                if (EditorUtility.DisplayDialog(
                    "Package Guardian",
                    $"Health report generated successfully!\n\nLocation: {reportPath}\n\nWould you like to open it?",
                    "Open",
                    "Close"))
                {
                    Application.OpenURL("file://" + reportPath);
                }
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Package Guardian", $"Failed to generate report: {ex.Message}", "OK");
                Debug.LogError($"[Package Guardian] Report generation failed: {ex}");
            }
        }
        
        /// <summary>
        /// Generates a health report in Markdown format
        /// </summary>
        private static string GenerateHealthReport(List<ValidationIssue> issues)
        {
            var sb = new StringBuilder();
            
            // Header
            sb.AppendLine("# Package Guardian Health Report");
            sb.AppendLine();
            sb.AppendLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"**Unity Version:** {Application.unityVersion}");
            sb.AppendLine($"**Project:** {Application.productName}");
            sb.AppendLine();
            
            // Executive Summary
            sb.AppendLine("## Executive Summary");
            sb.AppendLine();
            
            var critical = issues.Where(i => i.Severity == IssueSeverity.Critical).ToList();
            var errors = issues.Where(i => i.Severity == IssueSeverity.Error).ToList();
            var warnings = issues.Where(i => i.Severity == IssueSeverity.Warning).ToList();
            var info = issues.Where(i => i.Severity == IssueSeverity.Info).ToList();
            
            sb.AppendLine($"- **Total Issues:** {issues.Count}");
            sb.AppendLine($"- **Critical:** {critical.Count}");
            sb.AppendLine($"- **Errors:** {errors.Count}");
            sb.AppendLine($"- **Warnings:** {warnings.Count}");
            sb.AppendLine($"- **Info:** {info.Count}");
            sb.AppendLine();
            
            // Health Status
            string healthStatus;
            if (critical.Count > 0)
            {
                healthStatus = "CRITICAL";
            }
            else if (errors.Count > 0)
            {
                healthStatus = "NEEDS ATTENTION";
            }
            else if (warnings.Count > 0)
            {
                healthStatus = "GOOD";
            }
            else
            {
                healthStatus = "EXCELLENT";
            }
            
            sb.AppendLine($"**Overall Health Status:** {healthStatus}");
            sb.AppendLine();
            
            // Category Breakdown
            sb.AppendLine("## Issues by Category");
            sb.AppendLine();
            
            var byCategory = issues.GroupBy(i => i.Category).OrderByDescending(g => g.Count());
            
            sb.AppendLine("| Category | Count | Critical | Errors | Warnings |");
            sb.AppendLine("|----------|-------|----------|--------|----------|");
            
            foreach (var group in byCategory)
            {
                var catCritical = group.Count(i => i.Severity == IssueSeverity.Critical);
                var catErrors = group.Count(i => i.Severity == IssueSeverity.Error);
                var catWarnings = group.Count(i => i.Severity == IssueSeverity.Warning);
                
                sb.AppendLine($"| {group.Key} | {group.Count()} | {catCritical} | {catErrors} | {catWarnings} |");
            }
            
            sb.AppendLine();
            
            // Detailed Issues
            if (critical.Count > 0)
            {
                sb.AppendLine("## Critical Issues");
                sb.AppendLine();
                sb.AppendLine("These issues require immediate attention and may cause project corruption or data loss.");
                sb.AppendLine();
                AppendIssueDetails(sb, critical);
            }
            
            if (errors.Count > 0)
            {
                sb.AppendLine("## Errors");
                sb.AppendLine();
                sb.AppendLine("These issues should be resolved before continuing development.");
                sb.AppendLine();
                AppendIssueDetails(sb, errors);
            }
            
            if (warnings.Count > 0)
            {
                sb.AppendLine("## Warnings");
                sb.AppendLine();
                sb.AppendLine("These issues may cause problems and should be reviewed.");
                sb.AppendLine();
                AppendIssueDetails(sb, warnings);
            }
            
            if (info.Count > 0)
            {
                sb.AppendLine("## Informational");
                sb.AppendLine();
                sb.AppendLine("These are recommendations for best practices.");
                sb.AppendLine();
                AppendIssueDetails(sb, info);
            }
            
            // System Information
            sb.AppendLine("## System Information");
            sb.AppendLine();
            sb.AppendLine($"- **Operating System:** {SystemInfo.operatingSystem}");
            sb.AppendLine($"- **Processor:** {SystemInfo.processorType} ({SystemInfo.processorCount} cores)");
            sb.AppendLine($"- **Memory:** {SystemInfo.systemMemorySize} MB");
            sb.AppendLine($"- **Graphics:** {SystemInfo.graphicsDeviceName}");
            sb.AppendLine($"- **Unity Version:** {Application.unityVersion}");
            sb.AppendLine($"- **Scripting Backend:** {PlayerSettings.GetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup)}");
            sb.AppendLine($"- **Color Space:** {PlayerSettings.colorSpace}");
            sb.AppendLine();
            
            // Memory Usage
            var totalMemory = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
            var usedMemory = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            var monoMemory = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            
            sb.AppendLine("### Memory Usage");
            sb.AppendLine();
            sb.AppendLine($"- **Total Reserved:** {FormatBytes(totalMemory)}");
            sb.AppendLine($"- **Total Allocated:** {FormatBytes(usedMemory)}");
            sb.AppendLine($"- **Mono Heap:** {FormatBytes(monoMemory)}");
            sb.AppendLine();
            
            // Auto-fixable Issues
            var autoFixable = issues.Where(i => i.CanAutoFix).ToList();
            if (autoFixable.Count > 0)
            {
                sb.AppendLine("## Auto-Fixable Issues");
                sb.AppendLine();
                sb.AppendLine($"**{autoFixable.Count}** issue(s) can be automatically fixed using the Auto-Fix feature.");
                sb.AppendLine();
                
                foreach (var issue in autoFixable)
                {
                    sb.AppendLine($"- **{issue.Title}** ({issue.Severity})");
                }
                
                sb.AppendLine();
            }
            
            // Recommendations
            sb.AppendLine("## Recommendations");
            sb.AppendLine();
            
            if (critical.Count > 0)
            {
                sb.AppendLine("1. Address all **CRITICAL** issues immediately before continuing development.");
            }
            
            if (errors.Count > 0)
            {
                sb.AppendLine("2. Fix all **ERROR** level issues to ensure project stability.");
            }
            
            if (autoFixable.Count > 0)
            {
                sb.AppendLine($"3. Use the Auto-Fix feature to resolve {autoFixable.Count} fixable issue(s) automatically.");
            }
            
            if (warnings.Count > 0)
            {
                sb.AppendLine("4. Review and address **WARNING** level issues when time permits.");
            }
            
            sb.AppendLine("5. Run regular health checks to catch issues early (Tools > Package Guardian > Run Health Check).");
            sb.AppendLine("6. Enable automated health monitoring for continuous protection.");
            sb.AppendLine();
            
            // Footer
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("*Report generated by Package Guardian - Your Unity project health monitoring system*");
            sb.AppendLine();
            
            return sb.ToString();
        }
        
        private static void AppendIssueDetails(StringBuilder sb, List<ValidationIssue> issues)
        {
            int issueNumber = 1;
            
            foreach (var issue in issues)
            {
                sb.AppendLine($"### {issueNumber}. {issue.Title}");
                sb.AppendLine();
                sb.AppendLine($"**Category:** {issue.Category}");
                sb.AppendLine();
                sb.AppendLine($"**Description:** {issue.Description}");
                sb.AppendLine();
                
                if (issue.AffectedPaths != null && issue.AffectedPaths.Length > 0)
                {
                    sb.AppendLine("**Affected Files:**");
                    sb.AppendLine();
                    
                    int maxPaths = Math.Min(10, issue.AffectedPaths.Length);
                    for (int i = 0; i < maxPaths; i++)
                    {
                        sb.AppendLine($"- `{issue.AffectedPaths[i]}`");
                    }
                    
                    if (issue.AffectedPaths.Length > maxPaths)
                    {
                        sb.AppendLine($"- *(and {issue.AffectedPaths.Length - maxPaths} more)*");
                    }
                    
                    sb.AppendLine();
                }
                
                if (!string.IsNullOrEmpty(issue.SuggestedAction))
                {
                    sb.AppendLine($"**Suggested Action:** {issue.SuggestedAction}");
                    sb.AppendLine();
                }
                
                if (issue.CanAutoFix)
                {
                    sb.AppendLine("*This issue can be automatically fixed.*");
                    sb.AppendLine();
                }
                
                issueNumber++;
            }
        }
        
        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}




