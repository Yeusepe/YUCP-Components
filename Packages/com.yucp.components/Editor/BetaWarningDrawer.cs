using UnityEngine;
using UnityEditor;
using System;
using YUCP.Components;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Automatically displays a red warning at the top of Inspector for components marked with [BetaWarning].
    /// </summary>
    [InitializeOnLoad]
    public static class BetaWarningDrawer
    {
        static BetaWarningDrawer()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnPostHeaderGUI;
        }

        private static void OnPostHeaderGUI(UnityEditor.Editor editor)
        {
            if (editor.target == null) return;

            // Check if this component has the BetaWarning attribute
            Type targetType = editor.target.GetType();
            var betaAttribute = (BetaWarningAttribute)Attribute.GetCustomAttribute(
                targetType, 
                typeof(BetaWarningAttribute)
            );

            if (betaAttribute == null) return;

            // Display red warning box
            EditorGUILayout.Space(5);
            
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.3f, 0.3f); // Red background
            
            EditorGUILayout.HelpBox(
                betaAttribute.Message,
                MessageType.Warning
            );
            
            GUI.backgroundColor = originalColor;
            EditorGUILayout.Space(5);
        }
    }
}

