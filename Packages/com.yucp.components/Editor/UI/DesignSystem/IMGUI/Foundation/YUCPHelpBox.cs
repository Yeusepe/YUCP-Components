using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Alert boxes with consistent styling for info, warning, error, and success messages.
    /// Help box with custom visual hierarchy.
    /// </summary>
    public static class YUCPHelpBox
    {
        public enum MessageType
        {
            None,
            Info,
            Warning,
            Error,
            Success
        }

        public static void Draw(string message, MessageType type = MessageType.Info)
        {
            EditorGUILayout.Space(4);
            
            UnityEditor.MessageType unityType;
            switch (type)
            {
                case MessageType.Info:
                    unityType = UnityEditor.MessageType.Info;
                    break;
                case MessageType.Warning:
                    unityType = UnityEditor.MessageType.Warning;
                    break;
                case MessageType.Error:
                    unityType = UnityEditor.MessageType.Error;
                    break;
                case MessageType.Success:
                    unityType = UnityEditor.MessageType.Info;
                    break;
                default:
                    unityType = UnityEditor.MessageType.None;
                    break;
            }
            
            EditorGUILayout.HelpBox(message, unityType);
            EditorGUILayout.Space(4);
        }
    }
}

