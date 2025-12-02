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
            root.style.backgroundColor = new StyleColor(new Color(0.09f, 0.09f, 0.09f));
            
            // Load stylesheet
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.yucp.components/Editor/PackageGuardian/Styles/PackageGuardian.uss");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }
            
            _healthTab = new HealthTab();
            root.Add(_healthTab);
        }
        
        private void OnFocus()
        {
            // Light refresh when window gains focus
            // User must click "Full Scan" to trigger async validation
        }
    }
}

