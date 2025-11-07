using UnityEngine;
using UnityEditor;
using System;
using YUCP.Components;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Automatically displays a support banner at the top of Inspector for components marked with [SupportBanner].
    /// </summary>
    [InitializeOnLoad]
    public static class SupportBannerDrawer
    {
        private const string SupportUrl = "https://buymeacoffee.com/yeusepe";
        private const string PrefNeverKey = "com.yucp.components.support.never";
        private const string PrefCounterKey = "com.yucp.components.support.counter";
        private const string PrefCadenceKey = "com.yucp.components.support.cadence";
        private const string SessionDismissKey = "com.yucp.components.support.dismissed.session";
        
        static SupportBannerDrawer()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnPostHeaderGUI;
        }

        private static void OnPostHeaderGUI(UnityEditor.Editor editor)
        {
            if (editor.target == null) return;

            // Check if this component has the SupportBanner attribute
            Type targetType = editor.target.GetType();
            var supportAttribute = (SupportBannerAttribute)Attribute.GetCustomAttribute(
                targetType, 
                typeof(SupportBannerAttribute)
            );

            if (supportAttribute == null) return;

            // Check if user has permanently dismissed
            if (EditorPrefs.GetBool(PrefNeverKey, false)) return;
            
            // Check if dismissed for this session
            if (SessionState.GetBool(SessionDismissKey, false)) return;

            // Count inspector draws and check cadence
            int count = EditorPrefs.GetInt(PrefCounterKey, 0) + 1;
            EditorPrefs.SetInt(PrefCounterKey, count);

            int cadence = Mathf.Max(1, EditorPrefs.GetInt(PrefCadenceKey, 1000));
            if (count % cadence != 0) return;

            // Display support banner
            EditorGUILayout.Space(8);
            
            var originalBgColor = GUI.backgroundColor;
            var originalColor = GUI.color;
            
            // Outer container with border and rounded corners
            var boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.padding = new RectOffset(0, 0, 0, 0);
            boxStyle.margin = new RectOffset(4, 4, 0, 0);
            GUI.backgroundColor = new Color(0.28f, 0.28f, 0.28f, 1f);
            EditorGUILayout.BeginVertical(boxStyle);
            GUI.backgroundColor = originalBgColor;
            
            // Inner card with left accent
            GUILayout.BeginHorizontal();
            
            // Left teal accent bar - same color as heart
            var oldColor = GUI.color;
            GUI.color = new Color(0.212f, 0.749f, 0.694f, 1f); // YUCP Teal #36BFB1
            GUILayout.Box("", GUILayout.Width(4), GUILayout.ExpandHeight(true));
            GUI.color = oldColor;
            
            GUILayout.Space(10);
            
            // Content area
            GUILayout.BeginVertical();
            GUILayout.Space(8);
            
            // Header row: heart + title + close button
            GUILayout.BeginHorizontal();
            
            var heartStyle = new GUIStyle(EditorStyles.label);
            heartStyle.fontSize = 16;
            heartStyle.normal.textColor = new Color(0.212f, 0.749f, 0.694f, 1f); // YUCP Teal #36BFB1
            GUILayout.Label("♥", heartStyle, GUILayout.Width(20));
            
            var titleStyle = new GUIStyle(EditorStyles.label);
            titleStyle.fontSize = 12;
            titleStyle.fontStyle = FontStyle.Bold;
            GUILayout.Label("Support YUCP", titleStyle);
            
            GUILayout.FlexibleSpace();
            
            // Close X button - text button style
            var closeStyle = new GUIStyle(EditorStyles.label);
            closeStyle.fontSize = 16;
            closeStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            closeStyle.alignment = TextAnchor.MiddleCenter;
            closeStyle.hover.textColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            
            var closeRect = GUILayoutUtility.GetRect(new GUIContent("×"), closeStyle, GUILayout.Width(20), GUILayout.Height(20));
            if (GUI.Button(closeRect, "×", closeStyle))
            {
                SessionState.SetBool(SessionDismissKey, true);
                GUI.FocusControl(null); // Force refresh
            }
            
            GUILayout.EndHorizontal();
            
            GUILayout.Space(6);
            
            // Message
            var messageStyle = new GUIStyle(EditorStyles.label);
            messageStyle.wordWrap = true;
            messageStyle.fontSize = 11;
            messageStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            GUILayout.Label(supportAttribute.Message, messageStyle);
            
            GUILayout.Space(10);
            
            // Buttons row
            GUILayout.BeginHorizontal();
            
            // Support button - teal, prominent (same as heart)
            GUI.backgroundColor = new Color(0.212f, 0.749f, 0.694f, 1f); // YUCP Teal #36BFB1
            if (GUILayout.Button("Support", GUILayout.Height(30), GUILayout.Width(100)))
            {
                Application.OpenURL(SupportUrl);
            }
            GUI.backgroundColor = originalBgColor;
            
            GUILayout.FlexibleSpace();
            
            // Never show again - subtle text button
            var neverStyle = new GUIStyle(EditorStyles.label);
            neverStyle.fontSize = 10;
            neverStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            neverStyle.hover.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            neverStyle.alignment = TextAnchor.MiddleRight;
            
            var neverRect = GUILayoutUtility.GetRect(new GUIContent("Never show again"), neverStyle);
            if (GUI.Button(neverRect, "Never show again", neverStyle))
            {
                EditorPrefs.SetBool(PrefNeverKey, true);
                GUI.FocusControl(null); // Force refresh
            }
            
            GUILayout.EndHorizontal();
            
            GUILayout.Space(8);
            
            GUILayout.EndVertical();
            
            GUILayout.Space(10);
            
            GUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical(); // End outer container
            
            EditorGUILayout.Space(8);
        }
    }
}

