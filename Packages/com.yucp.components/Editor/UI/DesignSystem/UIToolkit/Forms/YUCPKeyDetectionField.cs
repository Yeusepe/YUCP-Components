using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace YUCP.UI.DesignSystem.Utilities
{
    /// <summary>
    /// UI Toolkit component for keyboard key detection with visual feedback.
    /// </summary>
    public class YUCPKeyDetectionField : VisualElement
    {
        private Button assignButton;
        private Button clearButton;
        private VisualElement helpContainer;
        private bool isDetecting = false;
        private KeyCode currentKey = KeyCode.None;
        private Action<KeyCode> onKeyAssigned;
        
        public KeyCode Key
        {
            get => currentKey;
            set
            {
                currentKey = value;
                UpdateButtonText();
                UpdateClearButtonVisibility();
            }
        }
        
        public YUCPKeyDetectionField()
        {
            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Forms/YUCPKeyDetectionField.uxml");
            
            if (template != null)
            {
                template.CloneTree(this);
            }
            
            assignButton = this.Q<Button>("key-assign-button");
            clearButton = this.Q<Button>("key-clear-button");
            helpContainer = this.Q<VisualElement>("key-detection-help");
            
            if (assignButton != null)
            {
                assignButton.clicked += OnAssignButtonClicked;
            }
            
            if (clearButton != null)
            {
                clearButton.clicked += OnClearButtonClicked;
            }
            
            UpdateButtonText();
            UpdateClearButtonVisibility();
            
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<MouseDownEvent>(OnMouseDown);
        }
        
        public void SetOnKeyAssigned(Action<KeyCode> callback)
        {
            onKeyAssigned = callback;
        }
        
        private void OnAssignButtonClicked()
        {
            if (isDetecting)
            {
                CancelDetection();
            }
            else
            {
                StartDetection();
            }
        }
        
        private void OnClearButtonClicked()
        {
            Key = KeyCode.None;
            onKeyAssigned?.Invoke(KeyCode.None);
        }
        
        private void StartDetection()
        {
            isDetecting = true;
            UpdateButtonText();
            UpdateHelpText();
            
            if (assignButton != null)
            {
                assignButton.AddToClassList("detecting");
            }
            
            Focus();
        }
        
        private void CancelDetection()
        {
            isDetecting = false;
            UpdateButtonText();
            UpdateHelpText();
            
            if (assignButton != null)
            {
                assignButton.RemoveFromClassList("detecting");
            }
        }
        
        private void OnKeyDown(KeyDownEvent evt)
        {
            if (!isDetecting) return;
            
            Key = evt.keyCode;
            onKeyAssigned?.Invoke(evt.keyCode);
            CancelDetection();
            evt.StopPropagation();
        }
        
        private void OnMouseDown(MouseDownEvent evt)
        {
            if (!isDetecting) return;
            
            KeyCode mouseKey = KeyCode.Mouse0 + (int)evt.button;
            Key = mouseKey;
            onKeyAssigned?.Invoke(mouseKey);
            CancelDetection();
            evt.StopPropagation();
        }
        
        private void UpdateButtonText()
        {
            if (assignButton == null) return;
            
            if (isDetecting)
            {
                assignButton.text = "Press any key...";
            }
            else if (currentKey == KeyCode.None)
            {
                assignButton.text = "Click to assign key";
            }
            else
            {
                assignButton.text = currentKey.ToString();
            }
        }
        
        private void UpdateClearButtonVisibility()
        {
            if (clearButton == null) return;
            
            clearButton.style.display = (currentKey != KeyCode.None) ? DisplayStyle.Flex : DisplayStyle.None;
        }
        
        private void UpdateHelpText()
        {
            if (helpContainer == null) return;
            
            helpContainer.Clear();
            
            if (isDetecting)
            {
                var helpBox = YUCPUIToolkitHelper.CreateHelpBox(
                    "Press any key or mouse button to assign. Click the button again to cancel.",
                    YUCPUIToolkitHelper.MessageType.Info);
                helpContainer.Add(helpBox);
                helpContainer.style.display = DisplayStyle.Flex;
            }
            else
            {
                helpContainer.style.display = DisplayStyle.None;
            }
        }
    }
}


