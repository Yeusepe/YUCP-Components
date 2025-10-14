using System;
using UnityEngine;

namespace YUCP.Components
{
    /// <summary>
    /// Marks a component as being in beta/experimental state.
    /// Automatically displays a warning in the Inspector.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class BetaWarningAttribute : Attribute
    {
        public string Message { get; }
        
        public BetaWarningAttribute()
        {
            Message = "This component is in BETA and may not work as intended. Use with caution.";
        }
        
        public BetaWarningAttribute(string customMessage)
        {
            Message = customMessage;
        }
    }
}

