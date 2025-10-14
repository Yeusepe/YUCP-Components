using System;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using YUCP.Components;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Helper utilities for displaying beta warnings in custom editors.
    /// </summary>
    public static class BetaWarningHelper
    {
        /// <summary>
        /// Draws a beta warning using IMGUI if the component has a BetaWarning attribute.
        /// Call this at the top of your OnInspectorGUI() method.
        /// </summary>
        public static void DrawBetaWarningIMGUI(Type componentType)
        {
            var betaAttribute = (BetaWarningAttribute)Attribute.GetCustomAttribute(
                componentType, 
                typeof(BetaWarningAttribute)
            );
            
            if (betaAttribute != null)
            {
                EditorGUILayout.Space(5);
                var originalColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.3f, 0.3f); // Red background
                EditorGUILayout.HelpBox(betaAttribute.Message, MessageType.Warning);
                GUI.backgroundColor = originalColor;
                EditorGUILayout.Space(5);
            }
        }
        
        /// <summary>
        /// Creates a VisualElement beta warning container if the component has a BetaWarning attribute.
        /// Returns null if no warning attribute is present.
        /// </summary>
        public static VisualElement CreateBetaWarningVisualElement(Type componentType)
        {
            var betaAttribute = (BetaWarningAttribute)Attribute.GetCustomAttribute(
                componentType, 
                typeof(BetaWarningAttribute)
            );
            
            if (betaAttribute == null)
                return null;
            
            var warningBox = new IMGUIContainer(() => {
                EditorGUILayout.Space(5);
                var originalColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
                EditorGUILayout.HelpBox(betaAttribute.Message, MessageType.Warning);
                GUI.backgroundColor = originalColor;
                EditorGUILayout.Space(5);
            });
            
            return warningBox;
        }
    }
}

