using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Tab navigation system for switching between content panels.
    /// Provides consistent tab styling and state management.
    /// </summary>
    public class YUCPTabs
    {
        private readonly List<string> tabLabels = new List<string>();
        private int selectedTab = 0;
        private readonly string stateKey;

        public YUCPTabs(string stateKey = null)
        {
            this.stateKey = stateKey ?? $"YUCPTabs_{GetHashCode()}";
            selectedTab = SessionState.GetInt(stateKey, 0);
        }

        public void AddTab(string label)
        {
            tabLabels.Add(label);
        }

        public int DrawTabs()
        {
            EditorGUILayout.BeginHorizontal();
            
            for (int i = 0; i < tabLabels.Count; i++)
            {
                var style = GetTabStyle(i == selectedTab);
                if (GUILayout.Button(tabLabels[i], style))
                {
                    selectedTab = i;
                    SessionState.SetInt(stateKey, selectedTab);
                }
            }
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
            
            return selectedTab;
        }

        public int SelectedTab => selectedTab;

        private GUIStyle GetTabStyle(bool isSelected)
        {
            var style = new GUIStyle(EditorStyles.toolbarButton);
            
            if (isSelected)
            {
                style.normal.background = CreateColorTexture(new Color(0.165f, 0.165f, 0.165f, 1f));
                style.normal.textColor = new Color(0.212f, 0.749f, 0.694f, 1f);
                style.fontStyle = FontStyle.Bold;
            }
            else
            {
                style.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            }
            
            return style;
        }

        private Texture2D CreateColorTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}



