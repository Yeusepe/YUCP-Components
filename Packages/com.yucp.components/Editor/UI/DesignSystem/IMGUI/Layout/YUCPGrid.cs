using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Grid layout helper for organizing fields in a grid pattern.
    /// Automatically handles column wrapping and spacing.
    /// </summary>
    public class YUCPGrid : IDisposable
    {
        private readonly int columns;
        private readonly float spacing;
        private int currentColumn;
        private readonly List<Action> deferredActions = new List<Action>();

        public YUCPGrid(int columns = 2, float spacing = 8f)
        {
            this.columns = columns;
            this.spacing = spacing;
            this.currentColumn = 0;
            EditorGUILayout.BeginHorizontal();
        }

        public void AddCell(System.Action content)
        {
            if (currentColumn >= columns)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                currentColumn = 0;
            }

            content?.Invoke();
            
            if (currentColumn < columns - 1)
            {
                GUILayout.Space(spacing);
            }
            
            currentColumn++;
        }

        public void Dispose()
        {
            EditorGUILayout.EndHorizontal();
        }
    }
}



