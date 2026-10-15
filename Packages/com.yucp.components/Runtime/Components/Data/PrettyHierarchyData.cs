using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    public enum PrettyHierarchyPreset
    {
        Custom = 0,
        Red,
        Orange,
        Yellow,
        Green,
        Blue,
        Purple,
        Pink,
        Gray,
        Black,
        White,
        Midnight,
        Sunset,
        Ocean,
        Forest
    }

    public enum PrettyHierarchyGradientDirection
    {
        Horizontal,
        Vertical
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("YUCP/Pretty Hierarchy")]
    [HelpURL("https://github.com/NCEEGEE/PrettyHierarchy")]
    [SupportBanner]
    public class PrettyHierarchyData : MonoBehaviour, IEditorOnly
    {
        [Header("Preset")]
        [Tooltip("Applies a ready-made background style. Custom keeps your own colors.")]
        [SerializeField] private PrettyHierarchyPreset preset = PrettyHierarchyPreset.Custom;

        [Header("Background")]
        [SerializeField] private bool useDefaultBackgroundColor = false;
        [SerializeField] private Color backgroundColor = new Color(0.235f, 0.235f, 0.314f, 1f);
        [Tooltip("Opacity of the row background.")]
        [SerializeField] [Range(0f, 1f)] private float backgroundAlpha = 1f;

        [Header("Background Gradient")]
        [SerializeField] private bool useBackgroundGradient;
        [SerializeField] private Gradient backgroundGradient = new Gradient();
        [Tooltip("Direction of the gradient, in degrees.")]
        [SerializeField] [Range(0f, 360f)] private float gradientAngle = 0f;

        [Header("Shadow")]
        [Tooltip("Draws a shadow under the row background.")]
        [SerializeField] private bool showShadow = false;
        [SerializeField] private Color shadowColor = new Color(0, 0, 0, 0.5f);
        [Tooltip("Shadow offset, in pixels.")]
        [SerializeField] private Vector2 shadowOffset = new Vector2(2, 2);
        [Tooltip("Shadow softness, in pixels.")]
        [SerializeField] [Range(0f, 16f)] private float shadowBlur = 4f;

        [Header("Row height")]
        [Tooltip("Sets this row's height. Rows below move to fit.")]
        [SerializeField] private bool useCustomRowHeight;
        [Tooltip("Row height, in pixels.")]
        [SerializeField] [Range(16f, 64f)] private float customRowHeight = 24f;

        [Header("Margins - Row")]
        [SerializeField] private float marginLeft;
        [SerializeField] private float marginRight;
        [SerializeField] private float marginTop;
        [SerializeField] private float marginBottom;

        [Header("Margins - Icon")]
        [SerializeField] private float iconMarginLeft;
        [SerializeField] private float iconMarginRight;
        [SerializeField] private float iconMarginTop;
        [SerializeField] private float iconMarginBottom;

        [Header("Margins - Text")]
        [SerializeField] private float textMarginLeft;
        [SerializeField] private float textMarginRight;
        [SerializeField] private float textMarginTop;
        [SerializeField] private float textMarginBottom;

        [Header("Icons")]
        [Tooltip("Shows the object's icon before its name.")]
        [SerializeField] private bool showIcon = true;
        [Tooltip("Replaces the object's icon with one you choose.")]
        [SerializeField] private bool useCustomIcon;
        [Tooltip("Built-in uses a Unity editor icon by name. Texture uses your own image.")]
        [SerializeField] private bool useCustomIconAsTexture;
        [Tooltip("The image to show as the icon.")]
        [SerializeField] private Texture2D customIcon;
        [Tooltip("The name of a Unity editor icon, such as d_GameObject Icon.")]
        [SerializeField] private string customIconBuiltInName = "d_GameObject Icon";
        [SerializeField] private bool showCollapseIcon = true;
        [Tooltip("Shows a folder icon on objects with children, open while expanded. Click it to expand or collapse.")]
        [SerializeField] private bool showExpandCollapseFolderIcon = true;
        [Tooltip("The name of a Unity editor icon to show while the object is collapsed.")]
        [SerializeField] private string closedFolderIconName = "d_Folder Icon";
        [Tooltip("The name of a Unity editor icon to show while the object is expanded.")]
        [SerializeField] private string openFolderIconName = "d_FolderOpened Icon";
        [Tooltip("Your own image to show while the object is collapsed. Overrides the icon name.")]
        [SerializeField] private Texture2D closedFolderCustomIcon;
        [Tooltip("Your own image to show while the object is expanded. Overrides the icon name.")]
        [SerializeField] private Texture2D openFolderCustomIcon;
        [Tooltip("Moves the folder icon right, in pixels.")]
        [SerializeField] private float folderIconOffsetX;
        [Tooltip("Moves the folder icon down, in pixels.")]
        [SerializeField] private float folderIconOffsetY;
        [SerializeField] private bool showPrefabIcon = true;
        [Tooltip("Shows the arrow that opens a prefab at the end of the row.")]
        [SerializeField] private bool showEditPrefabIcon = true;
        [Tooltip("Icon size, in pixels.")]
        [SerializeField] private float iconSize = 16f;

        [Header("Corners")]
        [Tooltip("Uniform uses one radius for every corner. Independent sets each corner.")]
        [SerializeField] private bool cornerRadiusUniform = true;
        [Tooltip("Corner radius, in pixels.")]
        [SerializeField] private float cornerRadius;
        [SerializeField] private float cornerRadiusTopLeft;
        [SerializeField] private float cornerRadiusTopRight;
        [SerializeField] private float cornerRadiusBottomRight;
        [SerializeField] private float cornerRadiusBottomLeft;

        [Header("Text")]
        [SerializeField] private bool useDefaultTextColor = true;
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private bool useTextGradient;
        [SerializeField] private Gradient textGradient = new Gradient();
        [Tooltip("Direction of the gradient, in degrees.")]
        [SerializeField] [Range(0f, 360f)] private float textGradientAngle = 0f;
        [Tooltip("Leave empty to use the editor font.")]
        [SerializeField] private Font font;
        [Tooltip("Text size, in pixels.")]
        [SerializeField] private int fontSize = 12;
        [SerializeField] private FontStyle fontStyle = FontStyle.Normal;
        [Tooltip("Horizontal alignment of the name. The name is always centered vertically.")]
        [SerializeField] private TextAnchor alignment = TextAnchor.UpperLeft;
        [Tooltip("Draws a shadow behind the name.")]
        [SerializeField] private bool textDropShadow;
        [Tooltip("Extra space before the name, in pixels.")]
        [SerializeField] private float paddingLeft;
        [Tooltip("Extra space after the name, in pixels.")]
        [SerializeField] private float paddingRight;

        [Header("Border")]
        [Tooltip("Border thickness, in pixels. 0 turns the border off.")]
        [SerializeField] private float borderWidth;
        [SerializeField] private Color borderColor = new Color(0.4f, 0.4f, 0.47f, 1f);

        public PrettyHierarchyPreset Preset => preset;

        public bool UseDefaultBackgroundColor => useDefaultBackgroundColor;
        public Color BackgroundColor => backgroundColor;
        public float BackgroundAlpha => backgroundAlpha;

        public bool UseBackgroundGradient => useBackgroundGradient;
        public Gradient BackgroundGradient => backgroundGradient;
        public float GradientAngle => gradientAngle;

        public bool ShowShadow => showShadow;
        public Color ShadowColor => shadowColor;
        public Vector2 ShadowOffset => shadowOffset;
        public float ShadowBlur => shadowBlur;

        public bool UseCustomRowHeight => useCustomRowHeight;
        public float CustomRowHeight => Mathf.Clamp(customRowHeight, 16f, 64f);

        public float MarginLeft => marginLeft;
        public float MarginRight => marginRight;
        public float MarginTop => marginTop;
        public float MarginBottom => marginBottom;

        public float IconMarginLeft => iconMarginLeft;
        public float IconMarginRight => iconMarginRight;
        public float IconMarginTop => iconMarginTop;
        public float IconMarginBottom => iconMarginBottom;

        public float TextMarginLeft => textMarginLeft;
        public float TextMarginRight => textMarginRight;
        public float TextMarginTop => textMarginTop;
        public float TextMarginBottom => textMarginBottom;

        public bool ShowIcon => showIcon;
        public bool UseCustomIcon => useCustomIcon;
        public bool UseCustomIconAsTexture => useCustomIconAsTexture;
        public Texture2D CustomIcon => customIcon;
        public string CustomIconBuiltInName => string.IsNullOrEmpty(customIconBuiltInName) ? "d_GameObject Icon" : customIconBuiltInName;
        public bool ShowCollapseIcon => showCollapseIcon;
        public bool ShowExpandCollapseFolderIcon => showExpandCollapseFolderIcon;
        public string ClosedFolderIconName => string.IsNullOrEmpty(closedFolderIconName) ? "d_Folder Icon" : closedFolderIconName;
        public string OpenFolderIconName => string.IsNullOrEmpty(openFolderIconName) ? "d_FolderOpened Icon" : openFolderIconName;
        public Texture2D ClosedFolderCustomIcon => closedFolderCustomIcon;
        public Texture2D OpenFolderCustomIcon => openFolderCustomIcon;
        public float FolderIconOffsetX => folderIconOffsetX;
        public float FolderIconOffsetY => folderIconOffsetY;
        public bool ShowPrefabIcon => showPrefabIcon;
        public bool ShowEditPrefabIcon => showEditPrefabIcon;
        public float IconSize => Mathf.Max(4f, iconSize);

        public bool CornerRadiusUniform => cornerRadiusUniform;
        public float CornerRadius => Mathf.Max(0f, cornerRadius);
        public float CornerRadiusTopLeft => cornerRadiusUniform ? CornerRadius : Mathf.Max(0f, cornerRadiusTopLeft);
        public float CornerRadiusTopRight => cornerRadiusUniform ? CornerRadius : Mathf.Max(0f, cornerRadiusTopRight);
        public float CornerRadiusBottomRight => cornerRadiusUniform ? CornerRadius : Mathf.Max(0f, cornerRadiusBottomRight);
        public float CornerRadiusBottomLeft => cornerRadiusUniform ? CornerRadius : Mathf.Max(0f, cornerRadiusBottomLeft);

        public bool UseDefaultTextColor => useDefaultTextColor;
        public Color TextColor => textColor;
        public bool UseTextGradient => useTextGradient;
        public Gradient TextGradient => textGradient;
        public float TextGradientAngle => textGradientAngle;
        public Font Font => font;
        public int FontSize => fontSize;
        public FontStyle FontStyle => fontStyle;
        public TextAnchor Alignment => alignment;
        public bool TextDropShadow => textDropShadow;
        public float PaddingLeft => paddingLeft;
        public float PaddingRight => paddingRight;

        public float BorderWidth => Mathf.Max(0f, borderWidth);
        public Color BorderColor => borderColor;
    }
}
