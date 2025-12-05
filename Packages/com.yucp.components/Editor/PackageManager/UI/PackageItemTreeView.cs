using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Renders a tree view of package items with checkboxes for selection.
    /// </summary>
    internal class PackageItemTreeView
    {
        private readonly VisualElement _container;
        private PackageItemNode _rootNode;
        private readonly Dictionary<string, bool> _expandedStates = new Dictionary<string, bool>();
        private readonly Dictionary<string, Toggle> _toggleMap = new Dictionary<string, Toggle>();

        // Reflection for ImportPackageItem
        private static readonly Type ImportPackageItemType;
        private static readonly PropertyInfo DestinationAssetPathProperty;
        private static readonly PropertyInfo ExistsProperty;
        private static readonly PropertyInfo PathConflictProperty;
        private static readonly PropertyInfo AssetChangedProperty;

        static PackageItemTreeView()
        {
            ImportPackageItemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
            if (ImportPackageItemType != null)
            {
                DestinationAssetPathProperty = ImportPackageItemType.GetProperty("destinationAssetPath");
                ExistsProperty = ImportPackageItemType.GetProperty("exists");
                PathConflictProperty = ImportPackageItemType.GetProperty("pathConflict");
                AssetChangedProperty = ImportPackageItemType.GetProperty("assetChanged");
            }
        }

        public PackageItemTreeView(VisualElement container)
        {
            _container = container;
        }

        public void SetTree(PackageItemNode rootNode)
        {
            _rootNode = rootNode;
            Refresh();
        }

        public void Refresh()
        {
            _container.Clear();
            if (_rootNode == null)
            {
                return;
            }

            if (_rootNode.Children.Count == 0)
            {
                return;
            }

            RenderNode(_rootNode, _container, 0);
        }

        public List<string> GetSelectedPaths()
        {
            var selectedPaths = new List<string>();
            if (_rootNode != null)
            {
                CollectSelectedPaths(_rootNode, selectedPaths);
            }
            return selectedPaths;
        }

        private void CollectSelectedPaths(PackageItemNode node, List<string> paths)
        {
            if (!node.IsFolder && node.IsSelected)
            {
                paths.Add(node.FullPath);
            }

            foreach (var child in node.Children)
            {
                CollectSelectedPaths(child, paths);
            }
        }

        private void RenderNode(PackageItemNode node, VisualElement parent, int depth)
        {
            // Skip root "Assets" node if it has no direct children/files
            if (depth == 0 && node.Children.Count == 0)
            {
                return;
            }

            // Render folder header (skip root, but show if it has direct files)
            if (node.IsFolder && (depth > 0 || node.Children.Any(c => !c.IsFolder)))
            {
                if (depth > 0)
                {
                    var folderRow = CreateFolderRow(node, depth);
                    parent.Add(folderRow);
                }
            }

            // Render children if expanded (root is always considered expanded for rendering)
            bool shouldRenderChildren = depth == 0 || node.IsExpanded;
            if (shouldRenderChildren)
            {
                foreach (var child in node.Children)
                {
                    if (child.IsFolder)
                    {
                        RenderNode(child, parent, depth + 1);
                    }
                    else
                    {
                        var fileRow = CreateFileRow(child, depth + 1);
                        parent.Add(fileRow);
                    }
                }
            }
        }

        private VisualElement CreateFolderRow(PackageItemNode node, int depth)
        {
            var row = new VisualElement();
            row.AddToClassList("yucp-tree-item");
            row.AddToClassList("yucp-tree-folder");
            row.style.paddingLeft = 8 + (depth * 20);

            var content = new VisualElement();
            content.AddToClassList("yucp-tree-item-content");
            content.style.flexDirection = FlexDirection.Row;
            content.style.alignItems = Align.Center;

            // Expand/collapse button
            var expandButton = new Button(() =>
            {
                node.IsExpanded = !node.IsExpanded;
                _expandedStates[node.FullPath] = node.IsExpanded;
                Refresh();
            });
            expandButton.AddToClassList("yucp-tree-expand-button");
            expandButton.text = node.IsExpanded ? "▼" : "▶";
            content.Add(expandButton);

            // Checkbox
            var checkbox = new Toggle { value = node.IsSelected };
            checkbox.AddToClassList("yucp-tree-checkbox");
            
            // Handle mixed state
            if (node.SelectionState.HasValue)
            {
                if (node.SelectionState.Value == 0) // Mixed
                {
                    checkbox.value = true; // Show as checked but with different styling
                    checkbox.AddToClassList("yucp-tree-checkbox-mixed");
                }
            }

            checkbox.RegisterValueChangedCallback(evt =>
            {
                node.SetSelectionRecursive(evt.newValue);
                node.UpdateSelectionState();
                Refresh();
            });

            _toggleMap[node.FullPath] = checkbox;
            content.Add(checkbox);

            // Folder icon - use Unity's folder icon if available, otherwise use text
            var folderIcon = EditorGUIUtility.IconContent("Folder Icon");
            if (folderIcon != null && folderIcon.image != null)
            {
                var iconImage = new Image { image = folderIcon.image as Texture2D };
                iconImage.AddToClassList("yucp-tree-icon");
                iconImage.AddToClassList("yucp-tree-folder-icon");
                iconImage.style.width = 16;
                iconImage.style.height = 16;
                content.Add(iconImage);
            }
            else
            {
                var icon = new Label("[DIR]");
                icon.AddToClassList("yucp-tree-icon");
                icon.AddToClassList("yucp-tree-folder-icon");
                content.Add(icon);
            }

            // Folder name
            var nameLabel = new Label(node.Name);
            nameLabel.AddToClassList("yucp-tree-label");
            nameLabel.AddToClassList("yucp-tree-folder-label");
            nameLabel.tooltip = node.FullPath;
            content.Add(nameLabel);

            row.Add(content);
            return row;
        }

        private VisualElement CreateFileRow(PackageItemNode node, int depth)
        {
            var row = new VisualElement();
            row.AddToClassList("yucp-tree-item");
            row.AddToClassList("yucp-tree-file");
            row.style.paddingLeft = 8 + (depth * 20);

            // Check for file states
            bool exists = GetExists(node.ImportItem);
            bool hasConflict = GetPathConflict(node.ImportItem);
            bool isChanged = GetAssetChanged(node.ImportItem);

            if (exists || hasConflict || isChanged)
            {
                if (hasConflict)
                {
                    row.AddToClassList("yucp-tree-item-conflict");
                }
                else if (exists)
                {
                    row.AddToClassList("yucp-tree-item-exists");
                }
            }

            var content = new VisualElement();
            content.AddToClassList("yucp-tree-item-content");
            content.style.flexDirection = FlexDirection.Row;
            content.style.alignItems = Align.Center;

            // Spacer for alignment (no expand button for files)
            var spacer = new VisualElement();
            spacer.style.width = 20;
            content.Add(spacer);

            // Checkbox
            var checkbox = new Toggle { value = node.IsSelected };
            checkbox.AddToClassList("yucp-tree-checkbox");
            checkbox.RegisterValueChangedCallback(evt =>
            {
                node.IsSelected = evt.newValue;
                // Update parent selection states
                UpdateParentSelectionStates(node);
                Refresh();
            });

            _toggleMap[node.FullPath] = checkbox;
            content.Add(checkbox);

            // File icon (use Unity's icon if available)
            var iconLabel = new Label();
            iconLabel.AddToClassList("yucp-tree-icon");
            iconLabel.AddToClassList("yucp-tree-file-icon");
            
            // Try to get Unity's icon for this file type
            var fileIcon = GetFileIconContent(node.FullPath);
            if (fileIcon != null && fileIcon.image != null)
            {
                var iconImage = new Image { image = fileIcon.image as Texture2D };
                iconImage.AddToClassList("yucp-tree-icon");
                iconImage.AddToClassList("yucp-tree-file-icon");
                iconImage.style.width = 16;
                iconImage.style.height = 16;
                content.Add(iconImage);
            }
            else
            {
                iconLabel.text = GetFileIconText(node.FullPath);
                content.Add(iconLabel);
            }

            // File name
            var nameLabel = new Label(node.Name);
            nameLabel.AddToClassList("yucp-tree-label");
            nameLabel.AddToClassList("yucp-tree-file-label");
            nameLabel.tooltip = node.FullPath;

            // Add status indicators
            if (hasConflict)
            {
                nameLabel.text += " [Conflict]";
                nameLabel.tooltip += "\n⚠ This file already exists and will be overwritten";
            }
            else if (exists && isChanged)
            {
                nameLabel.text += " [Modified]";
                nameLabel.tooltip += "\n⚠ This file exists and will be updated";
            }
            else if (exists)
            {
                nameLabel.text += " [Exists]";
                nameLabel.tooltip += "\nℹ This file already exists";
            }

            content.Add(nameLabel);

            row.Add(content);
            return row;
        }

        private void UpdateParentSelectionStates(PackageItemNode node)
        {
            // Find parent and update its state
            if (_rootNode == null) return;

            PackageItemNode parent = FindParent(_rootNode, node);
            while (parent != null)
            {
                bool changed = parent.UpdateSelectionState();
                if (!changed) break; // No more changes needed up the tree
                parent = FindParent(_rootNode, parent);
            }
        }

        private PackageItemNode FindParent(PackageItemNode searchNode, PackageItemNode target)
        {
            if (searchNode.Children.Contains(target))
            {
                return searchNode;
            }

            foreach (var child in searchNode.Children)
            {
                var found = FindParent(child, target);
                if (found != null) return found;
            }

            return null;
        }

        private GUIContent GetFileIconContent(string path)
        {
            // Try to get Unity's built-in icon for the file type
            string ext = System.IO.Path.GetExtension(path).ToLower();
            string iconName = ext switch
            {
                ".cs" => "cs Script Icon",
                ".prefab" => "Prefab Icon",
                ".mat" => "Material Icon",
                ".png" or ".jpg" or ".jpeg" or ".tga" => "Texture2D Icon",
                ".shader" => "Shader Icon",
                ".asset" => "ScriptableObject Icon",
                ".controller" => "AnimatorController Icon",
                ".unity" => "SceneAsset Icon",
                _ => "DefaultAsset Icon"
            };
            
            return EditorGUIUtility.IconContent(iconName);
        }

        private string GetFileIconText(string path)
        {
            // Text-based icon fallback
            string ext = System.IO.Path.GetExtension(path).ToLower();
            return ext switch
            {
                ".cs" => "C#",
                ".prefab" => "P",
                ".mat" => "M",
                ".png" or ".jpg" or ".jpeg" or ".tga" => "T",
                ".shader" => "SH",
                ".asset" => "A",
                ".controller" => "AC",
                ".unity" => "S",
                _ => "F"
            };
        }

        private bool GetExists(object item)
        {
            if (item == null || ExistsProperty == null) return false;
            object value = ExistsProperty.GetValue(item);
            return value is bool b && b;
        }

        private bool GetPathConflict(object item)
        {
            if (item == null || PathConflictProperty == null) return false;
            object value = PathConflictProperty.GetValue(item);
            return value is bool b && b;
        }

        private bool GetAssetChanged(object item)
        {
            if (item == null || AssetChangedProperty == null) return false;
            object value = AssetChangedProperty.GetValue(item);
            return value is bool b && b;
        }
    }
}

