using System;
using UnityEngine;

namespace YUCP.Components
{
    /// <summary>
    /// Marks a component to show a support banner in the Inspector.
    /// Automatically displays a support message with link to donations.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class SupportBannerAttribute : Attribute
    {
        public string Message { get; }
        
        public SupportBannerAttribute()
        {
            Message = "Enjoying these tools? Your support keeps them free and helps create more amazing features!";
        }
        
        public SupportBannerAttribute(string customMessage)
        {
            Message = customMessage;
        }
    }
}








