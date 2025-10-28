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
    /// Custom inspector for ModelRevisionBase ScriptableObject
    /// Shows registered variants, quick-access buttons, and visual tree of base → variants
    /// </summary>
    [CustomEditor(typeof(ModelRevisionBase))]
    public class ModelRevisionBaseEditor : UnityEditor.Editor
    {
        private ModelRevisionBase revisionBase;
        private VisualElement rootContainer;
        private VisualElement variantsContainer;
        private VisualElement statusContainer;
        
        public override VisualElement CreateInspectorGUI()
        {
            revisionBase = (ModelRevisionBase)target;
            
            // Create root container with YUCP header
            rootContainer = new VisualElement();
            rootContainer.Add(YUCPComponentHeader.CreateHeaderOverlay("Model Revision Base"));
            
            // Create main content
            var content = new VisualElement();
            content.style.paddingLeft = 10;
            content.style.paddingRight = 10;
            content.style.paddingTop = 5;
            content.style.paddingBottom = 10;
            
            // Base configuration section
            CreateBaseConfigurationSection(content);
            
            // Variants section
            CreateVariantsSection(content);
            
            // Status section
            CreateStatusSection(content);
            
            // Quick actions section
            CreateQuickActionsSection(content);
            
            rootContainer.Add(content);
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
            
            // Base prefab field
            var basePrefabField = new ObjectField("Base Prefab");
            basePrefabField.objectType = typeof(GameObject);
            basePrefabField.bindingPath = "basePrefab";
            basePrefabField.style.marginBottom = 5;
            section.Add(basePrefabField);
            
            // Source variant field
            var sourceVariantField = new ObjectField("Source Variant");
            sourceVariantField.objectType = typeof(GameObject);
            sourceVariantField.bindingPath = "sourceVariant";
            sourceVariantField.style.marginBottom = 5;
            section.Add(sourceVariantField);
            
            // Version info
            var versionField = new TextField("Version");
            versionField.bindingPath = "version";
            versionField.style.marginBottom = 5;
            section.Add(versionField);
            
            parent.Add(section);
        }
        
        private void CreateVariantsSection(VisualElement parent)
        {
            var section = new VisualElement();
            section.style.marginBottom = 15;
            
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 8;
            
            var title = new Label("Registered Variants");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.8f, 0.8f, 0.8f);
            header.Add(title);
            
            var addButton = new Button(() => AddVariant()) { text = "Add Variant" };
            addButton.style.width = 100;
            addButton.style.height = 25;
            addButton.style.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
            addButton.style.color = Color.white;
            header.Add(addButton);
            
            section.Add(header);
            
            // Variants container
            variantsContainer = new VisualElement();
            variantsContainer.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.5f);
            variantsContainer.style.borderTopLeftRadius = 5;
            variantsContainer.style.borderTopRightRadius = 5;
            variantsContainer.style.borderBottomLeftRadius = 5;
            variantsContainer.style.borderBottomRightRadius = 5;
            variantsContainer.style.paddingLeft = 8;
            variantsContainer.style.paddingRight = 8;
            variantsContainer.style.paddingTop = 5;
            variantsContainer.style.paddingBottom = 5;
            variantsContainer.style.marginBottom = 5;
            
            RefreshVariantsList();
            section.Add(variantsContainer);
            
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
            
            var validateButton = new Button(() => ValidateConfiguration()) { text = "Validate" };
            validateButton.style.flexGrow = 1;
            validateButton.style.marginLeft = 5;
            validateButton.style.height = 30;
            validateButton.style.backgroundColor = new Color(0.8f, 0.6f, 0.2f);
            validateButton.style.color = Color.white;
            buttonContainer.Add(validateButton);
            
            section.Add(buttonContainer);
            parent.Add(section);
        }
        
        private void RefreshVariantsList()
        {
            variantsContainer.Clear();
            
            if (revisionBase.registeredVariants == null || revisionBase.registeredVariants.Count == 0)
            {
                var noVariantsLabel = new Label("No variants registered");
                noVariantsLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                noVariantsLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                variantsContainer.Add(noVariantsLabel);
                return;
            }
            
            for (int i = 0; i < revisionBase.registeredVariants.Count; i++)
            {
                var variant = revisionBase.registeredVariants[i];
                var variantItem = CreateVariantItem(variant, i);
                variantsContainer.Add(variantItem);
            }
        }
        
        private VisualElement CreateVariantItem(GameObject variant, int index)
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
            
            // Status indicator
            var statusIndicator = new VisualElement();
            statusIndicator.style.width = 8;
            statusIndicator.style.height = 8;
            statusIndicator.style.borderTopLeftRadius = 4;
            statusIndicator.style.borderTopRightRadius = 4;
            statusIndicator.style.borderBottomLeftRadius = 4;
            statusIndicator.style.borderBottomRightRadius = 4;
            statusIndicator.style.marginRight = 8;
            
            // Set status color based on variant state
            if (variant == null)
            {
                statusIndicator.style.backgroundColor = Color.red;
            }
            else
            {
                var variantComponent = variant.GetComponent<ModelRevisionVariant>();
                if (variantComponent == null)
                {
                    statusIndicator.style.backgroundColor = Color.yellow;
                }
                else
                {
                    switch (variantComponent.status)
                    {
                        case VariantStatus.Synced:
                            statusIndicator.style.backgroundColor = Color.green;
                            break;
                        case VariantStatus.HasOverrides:
                            statusIndicator.style.backgroundColor = new Color(0.212f, 0.749f, 0.694f);
                            break;
                        case VariantStatus.HasConflicts:
                            statusIndicator.style.backgroundColor = Color.red;
                            break;
                        default:
                            statusIndicator.style.backgroundColor = Color.gray;
                            break;
                    }
                }
            }
            
            leftSide.Add(statusIndicator);
            
            // Variant name
            var nameLabel = new Label(variant != null ? variant.name : "Missing Reference");
            nameLabel.style.color = variant != null ? Color.white : Color.red;
            leftSide.Add(nameLabel);
            
            item.Add(leftSide);
            
            // Right side buttons
            var rightSide = new VisualElement();
            rightSide.style.flexDirection = FlexDirection.Row;
            
            if (variant != null)
            {
                var editButton = new Button(() => EditVariant(variant)) { text = "Edit" };
                editButton.style.width = 50;
                editButton.style.height = 20;
                editButton.style.marginRight = 3;
                editButton.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
                editButton.style.color = Color.white;
                rightSide.Add(editButton);
            }
            
            var removeButton = new Button(() => RemoveVariant(index)) { text = "×" };
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
            
            var issues = revisionBase.ValidateConfiguration();
            
            if (issues.Count == 0)
            {
                var successLabel = new Label("✓ Configuration is valid");
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
            
            // Show last transfer info
            if (revisionBase.lastTransferReport != null)
            {
                var lastTransferLabel = new Label($"Last transfer: {revisionBase.lastTransferReport.timestamp}");
                lastTransferLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                lastTransferLabel.style.fontSize = 11;
                lastTransferLabel.style.marginTop = 5;
                statusContainer.Add(lastTransferLabel);
            }
        }
        
        private void AddVariant()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("No Selection", "Please select a GameObject to add as a variant.", "OK");
                return;
            }
            
            revisionBase.RegisterVariant(selected);
            RefreshVariantsList();
            RefreshStatusInfo();
            EditorUtility.SetDirty(revisionBase);
        }
        
        private void RemoveVariant(int index)
        {
            if (index >= 0 && index < revisionBase.registeredVariants.Count)
            {
                revisionBase.UnregisterVariant(revisionBase.registeredVariants[index]);
                RefreshVariantsList();
                RefreshStatusInfo();
                EditorUtility.SetDirty(revisionBase);
            }
        }
        
        private void EditVariant(GameObject variant)
        {
            Selection.activeGameObject = variant;
            EditorGUIUtility.PingObject(variant);
        }
        
        private void OpenWizard()
        {
            // This will be implemented when we create the ModelRevisionWizard
            EditorUtility.DisplayDialog("Wizard", "Model Revision Wizard will be opened here.", "OK");
        }
        
        private void ValidateConfiguration()
        {
            RefreshStatusInfo();
            EditorUtility.SetDirty(revisionBase);
        }
    }
}
