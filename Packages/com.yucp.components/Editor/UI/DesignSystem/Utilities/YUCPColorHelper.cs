using UnityEngine;

namespace YUCP.UI.DesignSystem.Utilities
{
    /// <summary>
    /// Color helper utilities for YUCP design system.
    /// Provides access to the YUCP color palette.
    /// </summary>
    public static class YUCPColorHelper
    {
        public static readonly Color Teal = new Color(0.212f, 0.749f, 0.694f, 1f); // #36BFB1
        public static readonly Color TealHover = new Color(0.282f, 0.820f, 0.765f, 1f); // #48d1c3
        public static readonly Color TealActive = new Color(0.176f, 0.659f, 0.612f, 1f); // #2da89c
        
        public static readonly Color Warning = new Color(0.886f, 0.647f, 0.290f, 1f); // #E2A54A
        public static readonly Color Danger = new Color(0.886f, 0.290f, 0.290f, 1f); // #E24A4A
        public static readonly Color Info = new Color(0.227f, 0.639f, 1f, 1f); // #5BA3FF
        
        public static readonly Color BackgroundPrimary = new Color(0.035f, 0.035f, 0.035f, 1f); // #090909
        public static readonly Color BackgroundSecondary = new Color(0.102f, 0.102f, 0.102f, 1f); // #1a1a1a
        public static readonly Color BackgroundTertiary = new Color(0.165f, 0.165f, 0.165f, 1f); // #2a2a2a
        public static readonly Color BackgroundHover = new Color(0.325f, 0.325f, 0.325f, 1f); // #525252
        
        public static readonly Color TextPrimary = Color.white;
        public static readonly Color TextSecondary = new Color(0.69f, 0.69f, 0.69f, 1f); // #b0b0b0
        public static readonly Color TextTertiary = new Color(0.5f, 0.5f, 0.5f, 1f); // #808080
        
        public static readonly Color BorderPrimary = new Color(0.165f, 0.165f, 0.165f, 1f); // #2a2a2a
        public static readonly Color BorderSecondary = new Color(0.231f, 0.231f, 0.231f, 1f); // #3a3a3a
    }
}



