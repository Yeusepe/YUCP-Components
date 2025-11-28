using System;
using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Vertical/horizontal stack layouts with consistent spacing.
    /// Provides flexible layout options for organizing UI elements.
    /// </summary>
    public class YUCPStack : IDisposable
    {
        private readonly bool isHorizontal;
        private readonly float spacing;

        public YUCPStack(bool horizontal = false, float spacing = 8f)
        {
            this.isHorizontal = horizontal;
            this.spacing = spacing;
            
            if (horizontal)
            {
                EditorGUILayout.BeginHorizontal();
            }
            else
            {
                EditorGUILayout.BeginVertical();
            }
        }

        public void Dispose()
        {
            if (isHorizontal)
            {
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.EndVertical();
            }
        }
    }
}



