using System.Collections.Generic;
using System.Linq;

namespace PackageGuardian.Core.Validation
{
    /// <summary>
    /// Calculates project health score using validation issues.
    /// </summary>
    public static class HealthScoreCalculator
    {
        /// <summary>
        /// Calculates overall health score from validation issues.
        /// Scores range from 0-100, with 100 being perfect health.
        /// </summary>
        public static int CalculateHealthScore(List<ValidationIssue> issues)
        {
            if (issues == null || issues.Count == 0)
                return 100;
            
            int score = 100;
            
            foreach (var issue in issues)
            {
                switch (issue.Severity)
                {
                    case IssueSeverity.Critical:
                        score -= 20;
                        break;
                    case IssueSeverity.Error:
                        score -= 10;
                        break;
                    case IssueSeverity.Warning:
                        score -= 3;
                        break;
                    case IssueSeverity.Info:
                        score -= 1;
                        break;
                }
            }
            
            return System.Math.Max(0, score);
        }
        
        /// <summary>
        /// Gets severity breakdown counts for display.
        /// </summary>
        public static SeverityBreakdown GetSeverityBreakdown(List<ValidationIssue> issues)
        {
            if (issues == null)
                return new SeverityBreakdown();
            
            return new SeverityBreakdown
            {
                Critical = issues.Count(i => i.Severity == IssueSeverity.Critical),
                Error = issues.Count(i => i.Severity == IssueSeverity.Error),
                Warning = issues.Count(i => i.Severity == IssueSeverity.Warning),
                Info = issues.Count(i => i.Severity == IssueSeverity.Info)
            };
        }
        
        /// <summary>
        /// Gets category breakdown for analytics.
        /// </summary>
        public static Dictionary<IssueCategory, int> GetCategoryBreakdown(List<ValidationIssue> issues)
        {
            if (issues == null)
                return new Dictionary<IssueCategory, int>();
            
            return issues.GroupBy(i => i.Category)
                .ToDictionary(g => g.Key, g => g.Count());
        }
        
        /// <summary>
        /// Severity breakdown structure.
        /// </summary>
        public struct SeverityBreakdown
        {
            public int Critical { get; set; }
            public int Error { get; set; }
            public int Warning { get; set; }
            public int Info { get; set; }
            
            public int Total => Critical + Error + Warning + Info;
        }
    }
}








