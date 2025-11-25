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
            Message = "This project stays free because of you. Every tip directly supports maintenance and new releases. If it helped you, please consider supporting my work!";
        }
        
        public SupportBannerAttribute(string customMessage)
        {
            Message = customMessage;
        }
    }
}








