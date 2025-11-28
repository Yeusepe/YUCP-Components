using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Resources
{
    /// <summary>
    /// Header overlay editor for View Position & Head Auto-Link components.
    /// </summary>
    [CustomEditor(typeof(AttachToViewPositionData))]
    public class AttachToViewPositionDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCPComponentHeader.CreateHeaderOverlay("View Position & Head Auto-Link"));
            
            var settingsCard = YUCPUIToolkitHelper.CreateCard("Settings", "Configure view position and eye alignment");
            var settingsContent = YUCPUIToolkitHelper.GetCardContent(settingsCard);
            settingsContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("offset"), "Offset"));
            settingsContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("eyeAlignment"), "Eye Alignment"));
            root.Add(settingsCard);
            
            root.Add(YUCPUIToolkitHelper.CreateHelpBox(
                "Positions this object at the avatar's view position (eye level) and links it to the head bone.",
                YUCPUIToolkitHelper.MessageType.Info));
            
            root.schedule.Execute(() => serializedObject.ApplyModifiedProperties()).Every(100);
            
            return root;
        }
        
    }
}
