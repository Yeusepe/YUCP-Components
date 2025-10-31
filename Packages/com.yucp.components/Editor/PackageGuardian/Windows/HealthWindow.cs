using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components.PackageGuardian.Editor.Windows.UnifiedDashboard;

namespace YUCP.Components.PackageGuardian.Editor.Windows
{
    /// <summary>
    /// Window for displaying health and validation issues.
    /// </summary>
    public sealed class HealthWindow : EditorWindow
    {
        private HealthTab _healthTab;
        
        public static void ShowWindow()
        {
            var window = GetWindow<HealthWindow>();
            window.titleContent = new GUIContent("Package Guardian - Health & Safety");
            window.minSize = new Vector2(600, 400);
            window.Show();
        }
        
        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("pg-window");
            
            // Load stylesheet
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.components/Editor/PackageGuardian/Styles/PackageGuardian.uss");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }
            
            var container = new VisualElement();
            container.AddToClassList("pg-container");
            container.style.flexGrow = 1;
            
            _healthTab = new HealthTab();
            container.Add(_healthTab);
            
            root.Add(container);
        }
        
        private void OnFocus()
        {
            // Refresh when window gains focus
            _healthTab?.Refresh();
        }
    }
}

