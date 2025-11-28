using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Nested accordion groups for organizing collapsible content.
    /// Provides organization for collapsible content sections.
    /// </summary>
    public class YUCPAccordion : IDisposable
    {
        private readonly Dictionary<string, bool> expandedStates = new Dictionary<string, bool>();
        private readonly string stateKey;

        public YUCPAccordion(string stateKey = null)
        {
            this.stateKey = stateKey ?? $"YUCPAccordion_{GetHashCode()}";
        }

        public bool DrawGroup(string title, System.Action content, bool defaultExpanded = false)
        {
            string key = $"{stateKey}_{title}";
            
            if (!expandedStates.ContainsKey(key))
            {
                expandedStates[key] = SessionState.GetBool(key, defaultExpanded);
            }
            
            bool isExpanded = expandedStates[key];
            
            using (new YUCPFoldout(title, ref isExpanded))
            {
                if (isExpanded)
                {
                    content?.Invoke();
                }
            }
            
            expandedStates[key] = isExpanded;
            SessionState.SetBool(key, isExpanded);
            
            return isExpanded;
        }

        public void Dispose()
        {
        }
    }
}

