using System;
using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Card container with rounded corners, shadows, and optional header.
    /// Provides consistent card styling across all YUCP editors.
    /// </summary>
    public class YUCPCard : IDisposable
    {
        private readonly string title;
        private readonly string subtitle;
        private readonly bool hasHeader;
        private GUIStyle cardStyle;
        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;

        public YUCPCard(string title = null, string subtitle = null)
        {
            this.title = title;
            this.subtitle = subtitle;
            this.hasHeader = !string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(subtitle);
            
            EnsureStyles();
            
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginVertical(cardStyle);
            
            if (hasHeader)
            {
                if (!string.IsNullOrEmpty(title))
                {
                    EditorGUILayout.LabelField(title, titleStyle);
                }
                
                if (!string.IsNullOrEmpty(subtitle))
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField(subtitle, subtitleStyle);
                }
                
                if (hasHeader)
                {
                    EditorGUILayout.Space(8);
                }
            }
        }

        private void EnsureStyles()
        {
            if (cardStyle == null)
            {
                cardStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(12, 12, 12, 12)
                };
            }

            if (titleStyle == null)
            {
                titleStyle = EditorStyles.boldLabel;
            }

            if (subtitleStyle == null)
            {
                subtitleStyle = EditorStyles.wordWrappedMiniLabel;
            }
        }

        public void Dispose()
        {
            EditorGUILayout.EndVertical();
        }
    }
}

