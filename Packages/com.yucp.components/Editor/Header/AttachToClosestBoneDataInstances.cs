using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using YUCP.Components;
using YUCP.UI.DesignSystem.Utilities;

namespace YUCP.Components.Resources
{
    /// <summary>
    /// Header overlay editor for Closest Bone Auto-Link components.
    /// </summary>
    [CustomEditor(typeof(AttachToClosestBoneData))]
    public class AttachToClosestBoneDataEditor : UnityEditor.Editor
    {
        private bool showBoneFiltering = false;

        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var data = (AttachToClosestBoneData)target;
            
            var root = new VisualElement();
            YUCPUIToolkitHelper.LoadDesignSystemStyles(root);
            root.Add(YUCPComponentHeader.CreateHeaderOverlay("Closest Bone Auto-Link"));
            
            var settingsCard = YUCPUIToolkitHelper.CreateCard("Settings", "Configure attachment offset and search distance");
            var settingsContent = YUCPUIToolkitHelper.GetCardContent(settingsCard);
            settingsContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("offset"), "Offset"));
            settingsContent.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("maxDistance"), "Max Distance"));
            root.Add(settingsCard);
            
            var foldout = YUCPUIToolkitHelper.CreateFoldout("Bone Filtering", showBoneFiltering);
            foldout.RegisterValueChangedCallback(evt => { showBoneFiltering = evt.newValue; });
            foldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("includeNameFilter"), "Include Name Filter"));
            foldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("excludeNameFilter"), "Exclude Name Filter"));
            foldout.Add(YUCPUIToolkitHelper.CreateField(serializedObject.FindProperty("ignoreHumanoidBones"), "Ignore Humanoid Bones"));
            root.Add(foldout);
            
            root.schedule.Execute(() =>
            {
                var selectedBoneContainer = root.Q<VisualElement>("selected-bone-container");
                if (selectedBoneContainer != null)
                {
                    root.Remove(selectedBoneContainer);
                }
                
                if (!string.IsNullOrEmpty(data.SelectedBonePath))
                {
                    YUCPUIToolkitHelper.AddSpacing(root, 10);
                    var helpBox = YUCPUIToolkitHelper.CreateHelpBox($"Selected Bone: {data.SelectedBonePath}", YUCPUIToolkitHelper.MessageType.Info);
                    helpBox.name = "selected-bone-container";
                    root.Add(helpBox);
                }
            }).Every(100);
            
            root.schedule.Execute(() => serializedObject.ApplyModifiedProperties()).Every(100);
            
            return root;
        }
        
    }
}

