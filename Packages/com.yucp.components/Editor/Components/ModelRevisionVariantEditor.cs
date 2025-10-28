using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using YUCP.Components;
using YUCP.Components.Resources;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Custom inspector for ModelRevisionVariant component
    /// Shows base reference, override list, and wizard launch button
    /// </summary>
    [CustomEditor(typeof(ModelRevisionVariant))]
    public class ModelRevisionVariantEditor : UnityEditor.Editor
    {
        private ModelRevisionVariant variant;
        private VisualElement rootContainer;
        private VisualElement overridesContainer;
        private VisualElement statusContainer;
        
        public override VisualElement CreateInspectorGUI()
        {
            variant = (ModelRevisionVariant)target;
            
            // Create root container with YUCP header
            rootContainer = new VisualElement();
            rootContainer.Add(YUCPComponentHeader.CreateHeaderOverlay("Model Revision Variant"));
            
            // Create main content
            var content = new VisualElement();
            content.style.paddingLeft = 10;
            content.style.paddingRight = 10;
            content.style.paddingTop = 5;
            content.style.paddingBottom = 10;
            
            // Base configuration section
            CreateBaseConfigurationSection(content);
            
            // Overrides section
            CreateOverridesSection(content);
            
            // Status section
            CreateStatusSection(content);
            
            // Quick actions section
            CreateQuickActionsSection(content);
            
            rootContainer.Add(content);
            rootContainer.Bind(serializedObject);
            return rootContainer;
        }
        
        private void CreateBaseConfigurationSection(VisualElement parent)
        {
            var section = new VisualElement();
            section.style.marginBottom = 15;
            
            var title = new Label("Base Configuration");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.8f, 0.8f, 0.8f);
            title.style.marginBottom = 8;
            section.Add(title);
            
            // Revision base field
            var revisionBaseField = new PropertyField(serializedObject.FindProperty("revisionBase"), "Revision Base");
            revisionBaseField.style.marginBottom = 5;
            section.Add(revisionBaseField);
            
            // Transfer settings  
            var receiveTransfersField = new PropertyField(serializedObject.FindProperty("receiveTransfers"));
            receiveTransfersField.style.marginBottom = 3;
            section.Add(receiveTransfersField);
            
            var sendTransfersField = new PropertyField(serializedObject.FindProperty("sendTransfers"));
            sendTransfersField.style.marginBottom = 5;
            section.Add(sendTransfersField);
            
            parent.Add(section);
        }
        
        private void CreateOverridesSection(VisualElement parent)
        {
            var section = new VisualElement();
            section.style.marginBottom = 15;
            
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 8;
            
            var title = new Label("Component Overrides");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.8f, 0.8f, 0.8f);
            header.Add(title);
            
            var clearButton = new Button(() => ClearOverrides()) { text = "Clear All" };
            clearButton.style.width = 80;
            clearButton.style.height = 20;
            clearButton.style.backgroundColor = new Color(0.7f, 0.3f, 0.3f);
            clearButton.style.color = Color.white;
            header.Add(clearButton);
            
            section.Add(header);
            
            // Overrides container
            overridesContainer = new VisualElement();
            overridesContainer.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.5f);
            overridesContainer.style.borderTopLeftRadius = 5;
            overridesContainer.style.borderTopRightRadius = 5;
            overridesContainer.style.borderBottomLeftRadius = 5;
            overridesContainer.style.borderBottomRightRadius = 5;
            overridesContainer.style.paddingLeft = 8;
            overridesContainer.style.paddingRight = 8;
            overridesContainer.style.paddingTop = 5;
            overridesContainer.style.paddingBottom = 5;
            overridesContainer.style.marginBottom = 5;
            
            RefreshOverridesList();
            section.Add(overridesContainer);
            
            parent.Add(section);
        }
        
        private void CreateStatusSection(VisualElement parent)
        {
            var section = new VisualElement();
            section.style.marginBottom = 15;
            
            var title = new Label("Status & Validation");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.8f, 0.8f, 0.8f);
            title.style.marginBottom = 8;
            section.Add(title);
            
            statusContainer = new VisualElement();
            statusContainer.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.5f);
            statusContainer.style.borderTopLeftRadius = 5;
            statusContainer.style.borderTopRightRadius = 5;
            statusContainer.style.borderBottomLeftRadius = 5;
            statusContainer.style.borderBottomRightRadius = 5;
            statusContainer.style.paddingLeft = 8;
            statusContainer.style.paddingRight = 8;
            statusContainer.style.paddingTop = 5;
            statusContainer.style.paddingBottom = 5;
            
            RefreshStatusInfo();
            section.Add(statusContainer);
            
            parent.Add(section);
        }
        
        private void CreateQuickActionsSection(VisualElement parent)
        {
            var section = new VisualElement();
            
            var title = new Label("Quick Actions");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.8f, 0.8f, 0.8f);
            title.style.marginBottom = 8;
            section.Add(title);
            
            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.justifyContent = Justify.SpaceBetween;
            
            var openWizardButton = new Button(() => OpenWizard()) { text = "Open Wizard" };
            openWizardButton.style.flexGrow = 1;
            openWizardButton.style.marginRight = 5;
            openWizardButton.style.height = 30;
            openWizardButton.style.backgroundColor = new Color(0.2f, 0.5f, 0.8f);
            openWizardButton.style.color = Color.white;
            buttonContainer.Add(openWizardButton);
            
            var validateButton = new Button(() => ValidateVariant()) { text = "Validate" };
            validateButton.style.flexGrow = 1;
            validateButton.style.marginLeft = 5;
            validateButton.style.height = 30;
            validateButton.style.backgroundColor = new Color(0.8f, 0.6f, 0.2f);
            validateButton.style.color = Color.white;
            buttonContainer.Add(validateButton);
            
            section.Add(buttonContainer);
            parent.Add(section);
        }
        
        private void RefreshOverridesList()
        {
            overridesContainer.Clear();
            
            if (variant.componentOverrides == null || variant.componentOverrides.Count == 0)
            {
                var noOverridesLabel = new Label("No component overrides");
                noOverridesLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                noOverridesLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                overridesContainer.Add(noOverridesLabel);
                return;
            }
            
            for (int i = 0; i < variant.componentOverrides.Count; i++)
            {
                var overrideData = variant.componentOverrides[i];
                var overrideItem = CreateOverrideItem(overrideData, i);
                overridesContainer.Add(overrideItem);
            }
        }
        
        private VisualElement CreateOverrideItem(ComponentOverride overrideData, int index)
        {
            var item = new VisualElement();
            item.style.flexDirection = FlexDirection.Row;
            item.style.justifyContent = Justify.SpaceBetween;
            item.style.alignItems = Align.Center;
            item.style.marginBottom = 3;
            item.style.paddingLeft = 5;
            item.style.paddingRight = 5;
            item.style.paddingTop = 3;
            item.style.paddingBottom = 3;
            item.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
            item.style.borderTopLeftRadius = 3;
            item.style.borderTopRightRadius = 3;
            item.style.borderBottomLeftRadius = 3;
            item.style.borderBottomRightRadius = 3;
            
            var leftSide = new VisualElement();
            leftSide.style.flexDirection = FlexDirection.Row;
            leftSide.style.alignItems = Align.Center;
            
            // Override indicator
            var overrideIndicator = new VisualElement();
            overrideIndicator.style.width = 8;
            overrideIndicator.style.height = 8;
            overrideIndicator.style.borderTopLeftRadius = 4;
            overrideIndicator.style.borderTopRightRadius = 4;
            overrideIndicator.style.borderBottomLeftRadius = 4;
            overrideIndicator.style.borderBottomRightRadius = 4;
            overrideIndicator.style.marginRight = 8;
            overrideIndicator.style.backgroundColor = new Color(0.212f, 0.749f, 0.694f); // Teal for manual overrides
            leftSide.Add(overrideIndicator);
            
            // Component type and bone path
            var infoLabel = new Label($"{overrideData.componentType} on {overrideData.bonePath}");
            infoLabel.style.color = Color.white;
            leftSide.Add(infoLabel);
            
            item.Add(leftSide);
            
            // Right side buttons
            var rightSide = new VisualElement();
            rightSide.style.flexDirection = FlexDirection.Row;
            
            var removeButton = new Button(() => RemoveOverride(index)) { text = "×" };
            removeButton.style.width = 20;
            removeButton.style.height = 20;
            removeButton.style.backgroundColor = new Color(0.7f, 0.2f, 0.2f);
            removeButton.style.color = Color.white;
            rightSide.Add(removeButton);
            
            item.Add(rightSide);
            
            return item;
        }
        
        private void RefreshStatusInfo()
        {
            statusContainer.Clear();
            
            var issues = variant.ValidateVariant();
            
            if (issues.Count == 0)
            {
                var successLabel = new Label("✓ Variant configuration is valid");
                successLabel.style.color = Color.green;
                statusContainer.Add(successLabel);
            }
            else
            {
                foreach (var issue in issues)
                {
                    var issueLabel = new Label($"⚠ {issue}");
                    issueLabel.style.color = Color.yellow;
                    issueLabel.style.marginBottom = 2;
                    statusContainer.Add(issueLabel);
                }
            }
            
            // Show variant status
            var statusLabel = new Label($"Status: {variant.status}");
            statusLabel.style.color = GetStatusColor(variant.status);
            statusLabel.style.fontSize = 11;
            statusLabel.style.marginTop = 5;
            statusContainer.Add(statusLabel);
            
            // Show last sync info
            if (!string.IsNullOrEmpty(variant.lastSyncTimestamp))
            {
                var syncLabel = new Label($"Last sync: {variant.lastSyncTimestamp}");
                syncLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                syncLabel.style.fontSize = 11;
                syncLabel.style.marginTop = 2;
                statusContainer.Add(syncLabel);
            }
        }
        
        private Color GetStatusColor(VariantStatus status)
        {
            switch (status)
            {
                case VariantStatus.Synced:
                    return Color.green;
                case VariantStatus.HasOverrides:
                    return new Color(0.212f, 0.749f, 0.694f);
                case VariantStatus.HasConflicts:
                    return Color.red;
                case VariantStatus.OutOfSync:
                    return Color.yellow;
                default:
                    return Color.gray;
            }
        }
        
        private void ClearOverrides()
        {
            if (EditorUtility.DisplayDialog("Clear Overrides", 
                "Are you sure you want to clear all component overrides? This action cannot be undone.", 
                "Yes", "Cancel"))
            {
                variant.ClearVariantData();
                RefreshOverridesList();
                RefreshStatusInfo();
                EditorUtility.SetDirty(variant);
            }
        }
        
        private void RemoveOverride(int index)
        {
            if (index >= 0 && index < variant.componentOverrides.Count)
            {
                variant.componentOverrides.RemoveAt(index);
                RefreshOverridesList();
                RefreshStatusInfo();
                EditorUtility.SetDirty(variant);
            }
        }
        
        private void OpenWizard()
        {
            // This will be implemented when we create the ModelRevisionWizard
            EditorUtility.DisplayDialog("Wizard", "Model Revision Wizard will be opened here.", "OK");
        }
        
        private void ValidateVariant()
        {
            RefreshStatusInfo();
            EditorUtility.SetDirty(variant);
        }
    }
}
