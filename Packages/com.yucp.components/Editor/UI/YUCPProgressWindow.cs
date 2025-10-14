using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Components.Editor.UI
{
    /// <summary>
    /// Modal progress window displayed during long-running YUCP component processing operations.
    /// Shows YUCP branding, progress bar, and status text. Styled with custom fonts and colors.
    /// </summary>
    public class YUCPProgressWindow : EditorWindow
    {
        private GUIStyle transparentStyle;
        private Texture2D backgroundTexture;
        [InitializeOnLoadMethod]
        private static void Init()
        {
            EditorApplication.delayCall += () =>
            {
                foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<YUCPProgressWindow>())
                {
                    if (w) w.Close();
                }
            };
        }

        public static YUCPProgressWindow Create()
        {
            var window = CreateInstance<YUCPProgressWindow>();
            var mainWindowPos = GetEditorMainWindowPos();
            var size = new Vector2(650, 350);
            window.position = new Rect(
                mainWindowPos.xMin + (mainWindowPos.width - size.x) * 0.5f,
                mainWindowPos.yMin + 80,
                size.x, size.y
            );
            window.ShowPopup();
            return window;
        }

        private Label label;
        private ProgressBar progress;
        private Label headerLabel;

        public void OnEnable()
        {
            CreateTransparentBackground();
            
            var root = rootVisualElement;
            root.AddToClassList("YUCPProgressWindow");
            root.styleSheets.Add(LoadStyleSheet());
            
            root.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));

            var mainContainer = new VisualElement();
            mainContainer.AddToClassList("main-container");
            mainContainer.style.backgroundColor = new StyleColor(new Color(0.035f, 0.035f, 0.035f, 1f));
            root.Add(mainContainer);

            var contentArea = new VisualElement();
            contentArea.AddToClassList("content-area");
            mainContainer.Add(contentArea);

            // Logo with explicit positioning
            var logo = new Image();
            var logoTexture = LoadLogo();
            logo.image = logoTexture;
            
            // Logo dimensions: 2020x865 pixels, aspect ratio = 2.335
            float logoHeight = 180;
            float logoWidth = logoHeight * (2020f / 865f); // = 420.3px
            
            // Use StretchToFill to force exact dimensions
            logo.scaleMode = ScaleMode.StretchToFill;
            logo.style.width = logoWidth;
            logo.style.height = logoHeight;
            
            // Position at top-left manually
            logo.style.position = Position.Relative;
            logo.style.left = 0;
            logo.style.top = 0;
            logo.style.marginBottom = 15;
            logo.style.flexShrink = 0;
            
            contentArea.Add(logo);

            label = new Label("Loading...");
            label.AddToClassList("status");
            contentArea.Add(label);

            progress = new ProgressBar();
            progress.value = 0;
            progress.AddToClassList("progress-bar");
            progress.style.flexGrow = 1;
            progress.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
            progress.style.borderTopLeftRadius = 0;
            progress.style.borderTopRightRadius = 0;
            progress.style.borderBottomLeftRadius = 0;
            progress.style.borderBottomRightRadius = 0;
            contentArea.Add(progress);
        }
        
        private void CreateTransparentBackground()
        {
            backgroundTexture = new Texture2D(1, 1);
            backgroundTexture.SetPixel(0, 0, new Color(0, 0, 0, 0));
            backgroundTexture.Apply();
            
            transparentStyle = new GUIStyle();
            transparentStyle.normal.background = backgroundTexture;
        }
        
        private void OnGUI()
        {
            if (transparentStyle != null)
            {
                GUI.Box(new Rect(0, 0, position.width, position.height), GUIContent.none, transparentStyle);
            }
        }

        public void Progress(float current, string info)
        {
            var percent = Math.Round(current * 100);
            Debug.Log($"[YUCP Progress] ({percent}%): {info}");
            
            if (label != null) label.text = info;
            if (progress != null)
            {
                progress.value = current * 100;
                progress.title = ""; // No percentage text in the bar
            }
            
            RepaintNow();
        }

        public void CloseWindow()
        {
            if (this != null)
            {
                Close();
            }
        }
        
        private void OnDestroy()
        {
            if (backgroundTexture != null)
            {
                DestroyImmediate(backgroundTexture);
            }
        }

        private void RepaintNow()
        {
            var method = GetType().GetMethod("RepaintImmediately", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null)
            {
                method.Invoke(this, new object[] { });
            }
            else
            {
                Repaint();
            }
            
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private StyleSheet LoadStyleSheet()
        {
            string stylePath = "Packages/com.yucp.components/Editor/UI/YUCPProgressStyle.uss";
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(stylePath);
            
            if (styleSheet == null)
            {
                Debug.LogWarning("[YUCP] Could not load progress window style sheet. Using default styling.");
                var fallbackStyle = new StyleSheet();
                return fallbackStyle;
            }
            
            return styleSheet;
        }

        private Texture2D LoadLogo()
        {
            string logoPath = "Packages/com.yucp.components/Resources/Icons/Logo@2x.png";
            var logo = AssetDatabase.LoadAssetAtPath<Texture2D>(logoPath);
            
            if (logo == null)
            {
                Debug.LogWarning("[YUCP] Could not load logo. Progress window will show without logo.");
                return null;
            }
            
            return logo;
        }

        private static Rect GetEditorMainWindowPos()
        {
            var mainWindow = EditorGUIUtility.GetMainWindowPosition();
            return mainWindow;
        }
    }
}
