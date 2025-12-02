using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    /// <summary>
    /// A VRCFury-compatible toggle that merges a clothing mesh into the body mesh
    /// and uses UDIM discard to toggle its visibility.
    /// Optionally integrates with AutoBodyHider for automatic body hiding.
    /// </summary>
    [SupportBanner]
    [BetaWarning("This component is in BETA and may not work as intended. UV Discard Toggle is experimental and may require manual UDIM configuration.")]
    [AddComponentMenu("YUCP/UV Discard Toggle")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    public class UVDiscardToggleData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Target Meshes")]
        [Tooltip("The body mesh renderer to merge the clothing into.")]
        public SkinnedMeshRenderer targetBodyMesh;

        [Tooltip("The clothing mesh renderer to be merged and toggled.")]
        public SkinnedMeshRenderer clothingMesh;

        [Header("UDIM Discard Settings")]
        [Tooltip("Which UV channel to use for UDIM discard (UV1 is recommended).")]
        [Range(0, 3)]
        public int udimUVChannel = 1; // Default to UV1 for merged meshes

        [Tooltip("Which UDIM tile row to use for discarding (0-3).\n\n" +
                 "⚠️ WARNING: Avoid row 0 (especially 0,0) as it overlaps with main texture!\n" +
                 "• Row 1-3: Safe for discard\n" +
                 "• Row 3 (default): Safest, rarely used\n\n" +
                 "For multiple clothing pieces, each needs a unique tile.")]
        [Range(0, 3)]
        public int udimDiscardRow = 3;

        [Tooltip("Which UDIM tile column to use for discarding (0-3).\n\n" +
                 "⚠️ WARNING: Avoid (row 0, col 0-1) as it overlaps with main texture!\n" +
                 "• Columns 0-3: All usable in rows 1-3\n" +
                 "• Column 3 (default): Safest\n\n" +
                 "For multiple clothing pieces, each needs a unique tile.")]
        [Range(0, 3)]
        public int udimDiscardColumn = 3;

        [Header("VRCFury Toggle Settings")]
        [Tooltip("Menu path for the toggle (e.g., 'Clothing/Jacket').\n\n" +
                 "Leave empty to use global parameter control only (no menu item).")]
        public string menuPath = "Clothing/Toggle Item";

        [Tooltip("OPTIONAL: Global parameter name for network sync or outfit groups.\n\n" +
                 "When EMPTY:\n" +
                 "• Creates a local toggle (parameter auto-named by VRCFury with 'VF##' prefix)\n" +
                 "• Toggle state is local to your client only\n" +
                 "• Standard individual clothing toggle\n\n" +
                 "When SET:\n" +
                 "• Uses this exact parameter name (no prefix)\n" +
                 "• Synced across all players in the instance\n" +
                 "• Multiple clothing pieces can share the same parameter\n\n" +
                 "Advanced: If menuPath is also empty, ONLY global parameter controls this (no menu item).")]
        public string globalParameter = "";

        [Tooltip("Save toggle state across avatar reloads.")]
        public bool saved = true;

        [Tooltip("Toggle starts in the ON state by default.\n\n" +
                 "When ON: Clothing visible\n" +
                 "When OFF: Clothing hidden\n\n" +
                 "Note: Due to VRCFury's resting state system, this MUST be false for material animations to work correctly.")]
        public bool defaultOn = false;

        [Tooltip("If true, the toggle will be a slider instead of a button.")]
        public bool slider = false;

        [Tooltip("If true, the toggle will be a hold button instead of a latching toggle.")]
        public bool holdButton = false;

        [Tooltip("If true, the toggle will be secured, preventing others from toggling it.")]
        public bool securityEnabled = false;

        [Tooltip("If true, this toggle will be mutually exclusive with others sharing the same tag.\n\n" +
                 "Example: 'Outfit1,Outfit2' - only one can be active at a time.")]
        public bool enableExclusiveTag = false;
        public string exclusiveTag = "";

        [Tooltip("If true, the toggle will have a custom icon in the menu.")]
        public bool enableIcon = false;
        public Texture2D icon;

        [Header("Debug Settings")]
        [Tooltip("Save generated animation clip as asset for debugging.")]
        public bool debugSaveAnimation = false;

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;
    }
}

