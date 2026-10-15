using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    /// <summary>
    /// A VRCFury-compatible toggle that merges a clothing mesh into the body mesh
    /// and uses UV discard to toggle its visibility.
    /// Optionally integrates with AutoBodyHider for automatic body hiding.
    /// </summary>
    [SupportBanner]
    [BetaWarning("This component is in beta. The UV tile may need to be set by hand.")]
    [AddComponentMenu("YUCP/UV Discard Toggle")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    public class UVDiscardToggleData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Target Meshes")]
        [Tooltip("The body mesh the clothing is merged into. Its material needs Poiyomi or FastFur UV discard.")]
        public SkinnedMeshRenderer targetBodyMesh;

        [Tooltip("The clothing mesh that is merged into the body and toggled.")]
        public SkinnedMeshRenderer clothingMesh;

        [Header("UV Discard Settings")]
        [Tooltip("The UV channel used for discarding. UV1 is recommended.")]
        [Range(0, 3)]
        [UnityEngine.Serialization.FormerlySerializedAs("udimUVChannel")]
        public int uvChannel = 1; // Default to UV1 for merged meshes

        [Tooltip("The UV tile row that hides the clothing. Row 0 can overlap the main texture, and each clothing piece needs its own tile.")]
        [Range(0, 3)]
        [UnityEngine.Serialization.FormerlySerializedAs("udimDiscardRow")]
        public int uvDiscardRow = 3;

        [Tooltip("The UV tile column that hides the clothing. Each clothing piece needs its own tile.")]
        [Range(0, 3)]
        [UnityEngine.Serialization.FormerlySerializedAs("udimDiscardColumn")]
        public int uvDiscardColumn = 3;

        [Header("VRCFury Toggle Settings")]
        [Tooltip("Where the toggle appears in the avatar menu, e.g. Clothing/Jacket. Empty adds no menu item, so set a global parameter.")]
        public string menuPath = "Clothing/Toggle Item";

        [Tooltip("Uses this exact parameter name, so other clothing and systems can share it. Empty lets VRCFury generate one.")]
        public string globalParameter = "";

        [Tooltip("Remembers the toggle's state between worlds and avatar reloads.")]
        public bool saved = true;

        [Tooltip("The clothing starts visible. Leave it off: the material animation needs it off.")]
        public bool defaultOn = false;

        [Tooltip("Uses a radial slider instead of an on/off toggle.")]
        public bool slider = false;

        [Tooltip("The toggle is on only while its button is held.")]
        public bool holdButton = false;

        [Tooltip("Locks the toggle until the avatar's Security Lock is unlocked. Needs a Security Lock component on the avatar.")]
        public bool securityEnabled = false;

        [Tooltip("Turns off other toggles that share a tag when this one turns on.")]
        public bool enableExclusiveTag = false;
        [Tooltip("Comma-separated tags. Toggles that share a tag can't be on together.")]
        public string exclusiveTag = "";

        [Tooltip("Gives the toggle a custom icon in the menu.")]
        public bool enableIcon = false;
        [Tooltip("The icon texture.")]
        public Texture2D icon;

        [Header("Debug Settings")]
        [Tooltip("Saves the generated animation clip as an asset, for debugging.")]
        public bool debugSaveAnimation = false;

        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;
    }
}

