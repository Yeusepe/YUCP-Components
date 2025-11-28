using System;
using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Grouped content sections with headers.
    /// Provides consistent section styling across editors.
    /// </summary>
    public class YUCPSection : IDisposable
    {
        private readonly string title;
        private readonly string subtitle;
        private readonly bool hasHeader;

        public YUCPSection(string title, string subtitle = null)
        {
            this.title = title;
            this.subtitle = subtitle;
            this.hasHeader = !string.IsNullOrEmpty(title);
            
            if (hasHeader)
            {
                EditorGUILayout.Space(16);
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                
                if (!string.IsNullOrEmpty(subtitle))
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField(subtitle, EditorStyles.wordWrappedMiniLabel);
                }
                
                EditorGUILayout.Space(8);
            }
            
            EditorGUILayout.BeginVertical();
        }

        public void Dispose()
        {
            EditorGUILayout.EndVertical();
            if (hasHeader)
            {
                EditorGUILayout.Space(8);
            }
        }
    }
}

