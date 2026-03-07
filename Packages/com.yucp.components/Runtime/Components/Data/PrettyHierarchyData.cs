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
        [SerializeField] private PrettyHierarchyPreset preset = PrettyHierarchyPreset.Custom;

        [Header("Background")]
        [SerializeField] private bool useDefaultBackgroundColor = false;
        [SerializeField] private Color backgroundColor = new Color(0.235f, 0.235f, 0.314f, 1f);
        [SerializeField] [Range(0f, 1f)] private float backgroundAlpha = 1f;

        [Header("Background Gradient")]
        [SerializeField] private bool useBackgroundGradient;
        [SerializeField] private Gradient backgroundGradient = new Gradient();
        [SerializeField] [Range(0f, 360f)] private float gradientAngle = 0f;

        [Header("Shadow")]
        [SerializeField] private bool showShadow = false;
        [SerializeField] private Color shadowColor = new Color(0, 0, 0, 0.5f);
        [SerializeField] private Vector2 shadowOffset = new Vector2(2, 2);
        [SerializeField] [Range(0f, 16f)] private float shadowBlur = 4f;

        [Header("Row height")]
        [Tooltip("Use a custom row height so this row takes more space and other rows move down.")]
        [SerializeField] private bool useCustomRowHeight;
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
        [SerializeField] private bool showIcon = true;
        [SerializeField] private bool useCustomIcon;
        [SerializeField] private Texture2D customIcon;
        [SerializeField] private string customIconBuiltInName = "d_GameObject Icon";
        [SerializeField] private bool showCollapseIcon = true;
        [Tooltip("Show closed folder when collapsed and open folder when expanded (only for objects with children).")]
        [SerializeField] private bool showExpandCollapseFolderIcon = true;
        [SerializeField] private string closedFolderIconName = "d_Folder Icon";
        [SerializeField] private string openFolderIconName = "d_FolderOpened Icon";
        [SerializeField] private Texture2D closedFolderCustomIcon;
        [SerializeField] private Texture2D openFolderCustomIcon;
        [Tooltip("Offset the expand/collapse folder icon from its default position (pixels).")]
        [SerializeField] private float folderIconOffsetX;
        [SerializeField] private float folderIconOffsetY;
        [SerializeField] private bool showPrefabIcon = true;
        [SerializeField] private bool showEditPrefabIcon = true;
        [SerializeField] private float iconSize = 16f;

        [Header("Corners")]
        [SerializeField] private bool cornerRadiusUniform = true;
        [SerializeField] private float cornerRadius;
        [SerializeField] private float cornerRadiusTopLeft;
        [SerializeField] private float cornerRadiusTopRight;
        [SerializeField] private float cornerRadiusBottomRight;
        [SerializeField] private float cornerRadiusBottomLeft;

        [Header("Text")]
        [SerializeField] private bool useDefaultTextColor = true;
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Font font;
        [SerializeField] private int fontSize = 12;
        [SerializeField] private FontStyle fontStyle = FontStyle.Normal;
        [SerializeField] private TextAnchor alignment = TextAnchor.UpperLeft;
        [SerializeField] private bool textDropShadow;
        [SerializeField] private float paddingLeft;
        [SerializeField] private float paddingRight;

        [Header("Border")]
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
