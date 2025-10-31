using UnityEngine.UIElements;

namespace YUCP.Components.PackageGuardian.Editor.Controls
{
    /// <summary>
    /// Styled label helper.
    /// </summary>
    public static class PgLabel
    {
        public static Label Create(string text, bool secondary = false)
        {
            var label = new Label(text);
            label.AddToClassList("pg-label");
            
            if (secondary)
            {
                label.AddToClassList("pg-label-secondary");
            }
            
            return label;
        }
        
        public static Label CreateStatus(string text, StatusType status)
        {
            var label = Create(text);
            
            switch (status)
            {
                case StatusType.Good:
                    label.AddToClassList("pg-status-good");
                    break;
                case StatusType.Warning:
                    label.AddToClassList("pg-status-warning");
                    break;
                case StatusType.Error:
                    label.AddToClassList("pg-status-error");
                    break;
            }
            
            return label;
        }
    }
    
    public enum StatusType
    {
        Good,
        Warning,
        Error
    }
}

