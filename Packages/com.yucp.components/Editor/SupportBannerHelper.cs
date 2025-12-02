using System;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.UI.DesignSystem.Utilities;
using YUCP.Components.Editor.SupportBanner;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Helper utilities for displaying support banners in custom editors.
    /// </summary>
    public static class SupportBannerHelper
    {
        private const string SupportUrl = "http://patreon.com/Yeusepe";
        private const string PrefNeverKey = "com.yucp.components.support.never";
        private const string PrefCounterKey = "com.yucp.components.support.counter";
        private const string PrefCadenceKey = "com.yucp.components.support.cadence";
        private const string SessionDismissKey = "com.yucp.components.support.dismissed.session";
        
        /// <summary>
        /// Draws a support banner using IMGUI if the component has a SupportBanner attribute.
        /// Call this at the top of your OnInspectorGUI() method.
        /// </summary>
        public static void DrawSupportBannerIMGUI(Type componentType)
        {
            var supportAttribute = (SupportBannerAttribute)Attribute.GetCustomAttribute(
                componentType, 
                typeof(SupportBannerAttribute)
            );
            
            if (supportAttribute == null)
                return;
            
            // Track component usage for milestones
            MilestoneTracker.IncrementComponentUsage();
            
            // Check if user has permanently dismissed
            if (EditorPrefs.GetBool(PrefNeverKey, false)) return;
            
            // Check if dismissed for this session
            if (SessionState.GetBool(SessionDismissKey, false)) return;

            // Check if there's a milestone - if so, always show (bypass cadence)
            Milestone milestone = MilestoneTracker.GetCurrentMilestone();
            bool hasMilestone = milestone != null;
            
            // Count inspector draws and check cadence (unless we have a milestone)
            int count = EditorPrefs.GetInt(PrefCounterKey, 0) + 1;
            EditorPrefs.SetInt(PrefCounterKey, count);

            if (!hasMilestone)
            {
                int cadence = Mathf.Max(1, EditorPrefs.GetInt(PrefCadenceKey, 1000));
                if (count % cadence != 0) return;
            }
            
            EditorGUILayout.Space(5);
            
            var originalBgColor = GUI.backgroundColor;
            var originalColor = GUI.color;
            
            // Yellow support banner with image, title, subtitle, and buttons
            var bannerStyle = new GUIStyle(EditorStyles.helpBox);
            bannerStyle.padding = new RectOffset(12, 12, 10, 10);
            bannerStyle.margin = new RectOffset(0, 0, 0, 0);
            bannerStyle.normal.background = MakeTex(2, 2, new Color(0.886f, 0.647f, 0.290f, 0.2f));
            GUI.backgroundColor = new Color(0.886f, 0.647f, 0.290f, 0.2f);
            
            EditorGUILayout.BeginVertical(bannerStyle);
            GUI.backgroundColor = originalBgColor;
            
            GUILayout.BeginHorizontal();
            
            // Left: Icon/Image
            var heartIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.components/Resources/Icons/NucleoArcade/heart.png");
            if (heartIcon != null)
            {
                var iconStyle = new GUIStyle();
                iconStyle.normal.background = null;
                var iconRect = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32), GUILayout.Height(32));
                GUI.DrawTexture(iconRect, heartIcon, ScaleMode.ScaleToFit, true, 0, new Color(0.886f, 0.647f, 0.290f, 1f), 0, 0);
            }
            
            GUILayout.Space(10);
            
            // Middle: Title and Subtitle
            GUILayout.BeginVertical();
            GUILayout.Space(2);
            
            // Get current milestone (already retrieved above)
            var titleStyle = new GUIStyle(EditorStyles.boldLabel);
            titleStyle.fontSize = 13;
            titleStyle.normal.textColor = new Color(1f, 0.95f, 0.8f, 1f);
            string title = milestone != null ? milestone.Title : "Your Support Keeps This Free";
            GUILayout.Label(title, titleStyle);
            
            var subtitleStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
            subtitleStyle.fontSize = 10;
            subtitleStyle.normal.textColor = new Color(0.85f, 0.8f, 0.65f, 1f);
            subtitleStyle.padding = new RectOffset(0, 0, 0, 0);
            subtitleStyle.margin = new RectOffset(0, 0, 0, 0);
            string subtitle = milestone != null ? milestone.Subtitle : supportAttribute.Message;
            GUILayout.Label(subtitle, subtitleStyle, GUILayout.MaxWidth(400));
            
            GUILayout.EndVertical();
            
            GUILayout.FlexibleSpace();
            
            // Right: Action buttons
            GUILayout.BeginVertical();
            GUILayout.Space(4);
            
            GUILayout.BeginHorizontal();
            
            // Support button - warm yellow/gold
            var supportButtonStyle = new GUIStyle(GUI.skin.button);
            supportButtonStyle.fontSize = 11;
            supportButtonStyle.fontStyle = FontStyle.Bold;
            supportButtonStyle.normal.textColor = new Color(0.1f, 0.1f, 0.1f, 1f);
            supportButtonStyle.hover.textColor = new Color(0.1f, 0.1f, 0.1f, 1f);
            supportButtonStyle.active.textColor = new Color(0.1f, 0.1f, 0.1f, 1f);
            supportButtonStyle.padding = new RectOffset(12, 12, 5, 5);
            supportButtonStyle.border = new RectOffset(0, 0, 0, 0);
            supportButtonStyle.normal.background = MakeTex(2, 2, new Color(0.886f, 0.647f, 0.290f, 1f));
            supportButtonStyle.hover.background = MakeTex(2, 2, new Color(0.961f, 0.784f, 0.392f, 1f));
            supportButtonStyle.active.background = MakeTex(2, 2, new Color(0.737f, 0.537f, 0.227f, 1f));
            
            GUI.backgroundColor = new Color(0.886f, 0.647f, 0.290f, 1f);
            if (GUILayout.Button("Support", supportButtonStyle, GUILayout.Height(24), GUILayout.Width(85)))
            {
                Application.OpenURL(SupportUrl);
            }
            GUI.backgroundColor = originalBgColor;
            
            GUILayout.EndHorizontal();
            
            GUILayout.Space(4);
            
            GUILayout.BeginHorizontal();
            
            // Dismiss and Never show links
            var linkStyle = new GUIStyle(EditorStyles.label);
            linkStyle.fontSize = 9;
            linkStyle.normal.textColor = new Color(0.7f, 0.65f, 0.5f, 1f);
            linkStyle.hover.textColor = new Color(0.9f, 0.85f, 0.7f, 1f);
            linkStyle.padding = new RectOffset(2, 2, 2, 2);
            
            var dismissRect = GUILayoutUtility.GetRect(new GUIContent("Dismiss"), linkStyle);
            if (GUI.Button(dismissRect, "Dismiss", linkStyle))
            {
                SessionState.SetBool(SessionDismissKey, true);
                GUI.FocusControl(null);
            }
            
            GUILayout.Space(6);
            
            var neverRect = GUILayoutUtility.GetRect(new GUIContent("Never show"), linkStyle);
            if (GUI.Button(neverRect, "Never show", linkStyle))
            {
                EditorPrefs.SetBool(PrefNeverKey, true);
                GUI.FocusControl(null);
            }
            
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
            
            GUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
            
            GUI.backgroundColor = originalBgColor;
            GUI.color = originalColor;
            
            EditorGUILayout.Space(5);
        }
        
        /// <summary>
        /// Creates a VisualElement support banner container if the component has a SupportBanner attribute.
        /// Returns null if no banner attribute is present or if it should not be shown.
        /// Uses UI Toolkit components (no IMGUI).
        /// </summary>
        public static VisualElement CreateSupportBannerVisualElement(Type componentType)
        {
            var supportAttribute = (SupportBannerAttribute)Attribute.GetCustomAttribute(
                componentType, 
                typeof(SupportBannerAttribute)
            );
            
            if (supportAttribute == null)
                return null;
            
            // Track component usage for milestones
            MilestoneTracker.IncrementComponentUsage();
            
            // Check if user has permanently dismissed
            if (EditorPrefs.GetBool(PrefNeverKey, false)) return null;
            
            // Check if dismissed for this session
            if (SessionState.GetBool(SessionDismissKey, false)) return null;

            // Check if there's a milestone - if so, always show (bypass cadence)
            Milestone milestone = MilestoneTracker.GetCurrentMilestone();
            bool hasMilestone = milestone != null;
            
            // Count inspector draws and check cadence (unless we have a milestone)
            int count = EditorPrefs.GetInt(PrefCounterKey, 0) + 1;
            EditorPrefs.SetInt(PrefCounterKey, count);

            if (!hasMilestone)
            {
                int cadence = Mathf.Max(1, EditorPrefs.GetInt(PrefCadenceKey, 1000));
                if (count % cadence != 0) return null;
            }
            
            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Components/YUCPSupportBanner.uxml");
            
            if (template == null)
                return null;
            
            var banner = template.Instantiate();
            
            var iconImage = banner.Q<UnityEngine.UIElements.Image>("support-icon");
            if (iconImage != null)
            {
                var heartIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.yucp.components/Resources/Icons/NucleoArcade/heart.png");
                if (heartIcon != null)
                {
                    iconImage.image = heartIcon;
                }
            }
            
            // Get current milestone (already retrieved above)
            var titleLabel = banner.Q<Label>("support-title");
            if (titleLabel != null)
            {
                titleLabel.text = milestone != null ? milestone.Title : "Your Support Keeps This Free";
            }
            
            var subtitleLabel = banner.Q<Label>("support-subtitle");
            if (subtitleLabel != null)
            {
                subtitleLabel.text = milestone != null ? milestone.Subtitle : supportAttribute.Message;
            }
            
            var supportButton = banner.Q<Button>("support-button");
            if (supportButton != null)
            {
                supportButton.clicked += () => Application.OpenURL(SupportUrl);
            }
            
            var dismissButton = banner.Q<Button>("support-dismiss");
            if (dismissButton != null)
            {
                dismissButton.clicked += () => {
                    SessionState.SetBool(SessionDismissKey, true);
                    banner.style.display = DisplayStyle.None;
                };
            }
            
            var neverButton = banner.Q<Button>("support-never");
            if (neverButton != null)
            {
                neverButton.clicked += () => {
                    EditorPrefs.SetBool(PrefNeverKey, true);
                    banner.style.display = DisplayStyle.None;
                };
            }
            
            return banner;
        }
        
        private static Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;
            
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
    }
}

