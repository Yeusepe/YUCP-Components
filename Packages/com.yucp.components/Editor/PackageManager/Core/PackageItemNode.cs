using System;
using System.Collections.Generic;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Represents a node in the package import tree (folder or file).
    /// </summary>
    internal class PackageItemNode
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsFolder { get; set; }
        public bool IsExpanded { get; set; } = true;
        public bool IsSelected { get; set; } = true; // Default to selected for import
        public int? SelectionState { get; set; } // -1=unchecked, 0=mixed, 1=checked (null = not a folder)
        public List<PackageItemNode> Children { get; set; } = new List<PackageItemNode>();
        public object ImportItem { get; set; } // Unity's ImportPackageItem (accessed via reflection)
        public int Depth { get; set; }

        public PackageItemNode(string name, string fullPath, bool isFolder, int depth = 0)
        {
            Name = name;
            FullPath = fullPath;
            IsFolder = isFolder;
            Depth = depth;
            if (isFolder)
            {
                SelectionState = 1; // Folders start as checked
            }
        }

        /// <summary>
        /// Update selection state based on children. Returns true if state changed.
        /// </summary>
        public bool UpdateSelectionState()
        {
            if (!IsFolder || Children.Count == 0)
            {
                return false;
            }

            int checkedCount = 0;
            int uncheckedCount = 0;

            foreach (var child in Children)
            {
                if (child.IsFolder)
                {
                    child.UpdateSelectionState();
                }

                if (child.IsSelected || (child.SelectionState.HasValue && child.SelectionState.Value == 1))
                {
                    checkedCount++;
                }
                else if (!child.IsSelected && (!child.SelectionState.HasValue || child.SelectionState.Value == -1))
                {
                    uncheckedCount++;
                }
            }

            int? oldState = SelectionState;
            if (checkedCount == Children.Count)
            {
                SelectionState = 1;
                IsSelected = true;
            }
            else if (uncheckedCount == Children.Count)
            {
                SelectionState = -1;
                IsSelected = false;
            }
            else
            {
                SelectionState = 0; // Mixed
                IsSelected = false; // Mixed means not fully selected
            }

            return oldState != SelectionState;
        }

        /// <summary>
        /// Set selection for this node and all children recursively.
        /// </summary>
        public void SetSelectionRecursive(bool selected)
        {
            IsSelected = selected;
            if (IsFolder)
            {
                SelectionState = selected ? 1 : -1;
                foreach (var child in Children)
                {
                    child.SetSelectionRecursive(selected);
                }
            }
        }
    }
}












