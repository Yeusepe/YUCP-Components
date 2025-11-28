using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Resources
{    
    /// <summary>
    /// Header overlay editor for Symmetric Armature Auto-Link components.
    /// </summary>
    [CustomEditor(typeof(AttachToBodyPartData))]
    public class AttachToBodyPartDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCPComponentHeader.CreateHeaderOverlay("Symmetric Armature Auto-Link"));
            
            var targetCard = YUCPUIToolkitHelper.CreateCard("Target Settings", "Configure body part and side");
            var targetContent = YUCPUIToolkitHelper.GetCardContent(targetCard);
            targetContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("part"), "Body Part"));
            targetContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("side"), "Side"));
            targetContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("offset"), "Offset"));
            root.Add(targetCard);
            
            var deleteCard = YUCPUIToolkitHelper.CreateCard("Auto-Delete Components", "Optionally delete components based on side");
            var deleteContent = YUCPUIToolkitHelper.GetCardContent(deleteCard);
            deleteContent.Add(YUCPUIToolkitHelper.CreateHelpBox("Optionally delete components based on which side is selected.", YUCPUIToolkitHelper.MessageType.Info));
            deleteContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("leftComponentToDelete"), "Delete if Left"));
            deleteContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("rightComponentToDelete"), "Delete if Right"));
            root.Add(deleteCard);
            
            root.schedule.Execute(() => serializedObject.ApplyModifiedProperties()).Every(100);
            
            return root;
        }
        
    }
}