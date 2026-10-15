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

        // PERF: el header se dibuja en cada repaint https://docs.unity3d.com/ScriptReference/Editor-finishedDefaultHeaderGUI.html
        private static readonly System.Collections.Generic.Dictionary<Type, SupportBannerAttribute> Attributes =
            new System.Collections.Generic.Dictionary<Type, SupportBannerAttribute>();
        private static int _count = EditorPrefs.GetInt(PrefCounterKey, 0);
        private static readonly int Cadence = Mathf.Max(1, EditorPrefs.GetInt(PrefCadenceKey, 1000));

        static SupportBannerDrawer()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnPostHeaderGUI;
            AssemblyReloadEvents.beforeAssemblyReload += SaveCount;
            EditorApplication.quitting += SaveCount;
        }

        private static void SaveCount() => EditorPrefs.SetInt(PrefCounterKey, _count);

        private static void OnPostHeaderGUI(UnityEditor.Editor editor)
        {
            if (editor.target == null) return;

            Type targetType = editor.target.GetType();
            if (!Attributes.TryGetValue(targetType, out var supportAttribute))
                Attributes[targetType] = supportAttribute = (SupportBannerAttribute)Attribute.GetCustomAttribute(targetType, typeof(SupportBannerAttribute));
            if (supportAttribute == null) return;

            if (EditorPrefs.GetBool(PrefNeverKey, false)) return;
            
            if (SessionState.GetBool(SessionDismissKey, false)) return;

            if (++_count % Cadence != 0) return;

            EditorGUILayout.Space(8);
            
            var originalBgColor = GUI.backgroundColor;
            var originalColor = GUI.color;
            
            var boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.padding = new RectOffset(0, 0, 0, 0);
            boxStyle.margin = new RectOffset(4, 4, 0, 0);
            GUI.backgroundColor = new Color(0.28f, 0.28f, 0.28f, 1f);
            EditorGUILayout.BeginVertical(boxStyle);
            GUI.backgroundColor = originalBgColor;
            
            GUILayout.BeginHorizontal();
            
            var oldColor = GUI.color;
            GUI.color = new Color(0.212f, 0.749f, 0.694f, 1f); // YUCP Teal #36BFB1
            GUILayout.Box("", GUILayout.Width(4), GUILayout.ExpandHeight(true));
            GUI.color = oldColor;
            
            GUILayout.Space(10);
            
            GUILayout.BeginVertical();
            GUILayout.Space(8);
            
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
            
            var messageStyle = new GUIStyle(EditorStyles.label);
            messageStyle.wordWrap = true;
            messageStyle.fontSize = 11;
            messageStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            GUILayout.Label(supportAttribute.Message, messageStyle);
            
            GUILayout.Space(10);
            
            GUILayout.BeginHorizontal();
            
            GUI.backgroundColor = new Color(0.212f, 0.749f, 0.694f, 1f); // YUCP Teal #36BFB1
            if (GUILayout.Button("Support", GUILayout.Height(30), GUILayout.Width(100)))
            {
                Application.OpenURL(SupportUrl);
            }
            GUI.backgroundColor = originalBgColor;
            
            GUILayout.FlexibleSpace();
            
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

