using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace YUCP.UI.DesignSystem.Utilities
{
    /// <summary>
    /// UI Toolkit component for managing a list of blendshape names.
    /// </summary>
    public class YUCPBlendshapeListEditor : VisualElement
    {
        private VisualElement meshInfoContainer;
        private Button addButton;
        private VisualElement listContainer;
        private VisualElement emptyStateContainer;
        
        private List<string> blendshapeList;
        private Mesh targetMesh;
        private Action<List<string>> onListChanged;
        
        public List<string> BlendshapeList
        {
            get => blendshapeList ?? new List<string>();
            set
            {
                blendshapeList = value ?? new List<string>();
                RefreshList();
            }
        }
        
        public Mesh TargetMesh
        {
            get => targetMesh;
            set
            {
                targetMesh = value;
                UpdateMeshInfo();
                UpdateAddButtonState();
            }
        }
        
        public YUCPBlendshapeListEditor()
        {
            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Advanced/YUCPBlendshapeListEditor.uxml");
            
            if (template != null)
            {
                template.CloneTree(this);
            }
            
            meshInfoContainer = this.Q<VisualElement>("mesh-info");
            addButton = this.Q<Button>("add-blendshape-button");
            listContainer = this.Q<VisualElement>("blendshape-list");
            emptyStateContainer = this.Q<VisualElement>("empty-state");
            
            if (addButton != null)
            {
                addButton.clicked += OnAddButtonClicked;
            }
            
            blendshapeList = new List<string>();
            RefreshList();
        }
        
        public void SetOnListChanged(Action<List<string>> callback)
        {
            onListChanged = callback;
        }
        
        private void UpdateMeshInfo()
        {
            if (meshInfoContainer == null) return;
            
            meshInfoContainer.Clear();
            
            if (targetMesh != null)
            {
                var helpBox = YUCPUIToolkitHelper.CreateHelpBox(
                    $"Target mesh has {targetMesh.blendShapeCount} blendshapes available",
                    YUCPUIToolkitHelper.MessageType.None);
                meshInfoContainer.Add(helpBox);
            }
            else
            {
                var helpBox = YUCPUIToolkitHelper.CreateHelpBox(
                    "Assign target mesh to select blendshapes",
                    YUCPUIToolkitHelper.MessageType.Warning);
                meshInfoContainer.Add(helpBox);
            }
        }
        
        private void UpdateAddButtonState()
        {
            if (addButton == null) return;
            
            addButton.SetEnabled(targetMesh != null);
        }
        
        private void OnAddButtonClicked()
        {
            if (targetMesh == null) return;
            
            ShowBlendshapeSelectionMenu();
        }
        
        private void ShowBlendshapeSelectionMenu()
        {
            if (targetMesh == null) return;
            
            GenericMenu menu = new GenericMenu();
            
            for (int i = 0; i < targetMesh.blendShapeCount; i++)
            {
                string blendshapeName = targetMesh.GetBlendShapeName(i);
                bool alreadyAdded = blendshapeList.Contains(blendshapeName);
                
                if (alreadyAdded)
                {
                    menu.AddDisabledItem(new GUIContent($"{blendshapeName} (already added)"));
                }
                else
                {
                    string name = blendshapeName;
                    menu.AddItem(new GUIContent(blendshapeName), false, () => {
                        if (blendshapeList == null)
                            blendshapeList = new List<string>();
                        blendshapeList.Add(name);
                        RefreshList();
                        onListChanged?.Invoke(blendshapeList);
                    });
                }
            }
            
            menu.ShowAsContext();
        }
        
        private void RefreshList()
        {
            if (listContainer == null || emptyStateContainer == null) return;
            
            listContainer.Clear();
            emptyStateContainer.Clear();
            
            if (blendshapeList == null || blendshapeList.Count == 0)
            {
                listContainer.style.display = DisplayStyle.None;
                emptyStateContainer.style.display = DisplayStyle.Flex;
                
                var helpBox = YUCPUIToolkitHelper.CreateHelpBox(
                    "No blendshapes selected.\nClick 'Add Blendshape from List' to choose specific blendshapes to track.",
                    YUCPUIToolkitHelper.MessageType.Warning);
                emptyStateContainer.Add(helpBox);
            }
            else
            {
                listContainer.style.display = DisplayStyle.Flex;
                emptyStateContainer.style.display = DisplayStyle.None;
                
                var headerLabel = new Label($"Selected Blendshapes ({blendshapeList.Count}):");
                headerLabel.style.fontSize = 13;
                headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                headerLabel.style.marginBottom = 3;
                listContainer.Add(headerLabel);
                
                for (int i = 0; i < blendshapeList.Count; i++)
                {
                    int index = i;
                    var item = CreateBlendshapeItem(index, blendshapeList[i]);
                    listContainer.Add(item);
                }
            }
        }
        
        private VisualElement CreateBlendshapeItem(int index, string blendshapeName)
        {
            var item = new VisualElement();
            item.AddToClassList("yucp-blendshape-item");
            
            var indexLabel = new Label($"{index + 1}.");
            indexLabel.AddToClassList("yucp-blendshape-index");
            item.Add(indexLabel);
            
            var nameField = new TextField { value = blendshapeName };
            nameField.AddToClassList("yucp-blendshape-name-field");
            nameField.RegisterValueChangedCallback(evt => {
                if (index < blendshapeList.Count)
                {
                    blendshapeList[index] = evt.newValue;
                    onListChanged?.Invoke(blendshapeList);
                }
            });
            item.Add(nameField);
            
            var removeButton = YUCPUIToolkitHelper.CreateButton("×", () => {
                if (index < blendshapeList.Count)
                {
                    blendshapeList.RemoveAt(index);
                    RefreshList();
                    onListChanged?.Invoke(blendshapeList);
                }
            }, YUCPUIToolkitHelper.ButtonVariant.Danger);
            removeButton.AddToClassList("yucp-blendshape-remove-button");
            item.Add(removeButton);
            
            return item;
        }
    }
}

