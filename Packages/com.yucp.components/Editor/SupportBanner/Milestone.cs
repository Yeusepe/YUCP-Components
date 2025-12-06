using System;

namespace YUCP.Components.Editor.SupportBanner
{
    /// <summary>
    /// Represents a milestone achievement with title and subtitle.
    /// </summary>
    public class Milestone
    {
        public string Id { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public Func<int, bool> CheckFunction { get; }
        
        public Milestone(string id, string title, string subtitle, Func<int, bool> checkFunction)
        {
            Id = id;
            Title = title;
            Subtitle = subtitle;
            CheckFunction = checkFunction;
        }
    }
}











