using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace YUCP.Components.Editor.PackageManager
{
    /// <summary>
    /// Builds a hierarchical tree structure from Unity's ImportPackageItem array.
    /// </summary>
    internal static class PackageItemTreeBuilder
    {
        private static readonly Type ImportPackageItemType;
        private static readonly FieldInfo DestinationAssetPathField;
        private static readonly FieldInfo IsFolderField;
        private static readonly FieldInfo EnabledStatusField;

        static PackageItemTreeBuilder()
        {
            // Use reflection to access Unity's internal ImportPackageItem type
            ImportPackageItemType = Type.GetType("UnityEditor.ImportPackageItem, UnityEditor.CoreModule");
            if (ImportPackageItemType != null)
            {
                DestinationAssetPathField = ImportPackageItemType.GetField("destinationAssetPath");
                IsFolderField = ImportPackageItemType.GetField("isFolder");
                EnabledStatusField = ImportPackageItemType.GetField("enabledStatus");
            }
        }

        /// <summary>
        /// Build a tree from an array of ImportPackageItem objects.
        /// </summary>
        public static PackageItemNode BuildTree(System.Array importItems)
        {
            if (importItems == null || importItems.Length == 0)
            {
                return new PackageItemNode("Assets", "Assets", true, 0);
            }

            var root = new PackageItemNode("Assets", "Assets", true, 0);
            root.IsExpanded = true;

            // Group items by path
            var pathMap = new Dictionary<string, PackageItemNode>();
            pathMap["Assets"] = root;

            foreach (var item in importItems)
            {
                if (item == null) continue;

                string destinationPath = GetDestinationPath(item);
                if (string.IsNullOrEmpty(destinationPath))
                    continue;

                bool isFolder = GetIsFolder(item);
                int enabledStatus = GetEnabledStatus(item);

                // Normalize path
                if (!destinationPath.StartsWith("Assets/") && !destinationPath.StartsWith("Assets\\"))
                {
                    destinationPath = "Assets/" + destinationPath.TrimStart('/', '\\');
                }

                // Get or create folder nodes for the path
                string parentPath = GetParentPath(destinationPath);
                PackageItemNode parentNode = GetOrCreateFolderPath(root, parentPath, pathMap);

                // Create or get the node for this item
                PackageItemNode node;
                if (pathMap.TryGetValue(destinationPath, out node))
                {
                    // Node already exists (folder), just update the import item
                    node.ImportItem = item;
                    if (!isFolder)
                    {
                        // This shouldn't happen, but handle it
                        node.IsFolder = false;
                    }
                }
                else
                {
                    // Create new node
                    string name = GetFileName(destinationPath);
                    node = new PackageItemNode(name, destinationPath, isFolder, GetDepth(destinationPath));
                    node.ImportItem = item;

                    // Set initial selection based on enabledStatus
                    // enabledStatus: -1=disabled, 0=none, 1=enabled, 2=mixed
                    if (enabledStatus == 1 || enabledStatus == 2)
                    {
                        node.IsSelected = true;
                        if (isFolder) node.SelectionState = enabledStatus == 2 ? 0 : 1;
                    }
                    else
                    {
                        node.IsSelected = false;
                        if (isFolder) node.SelectionState = -1;
                    }

                    parentNode.Children.Add(node);
                    pathMap[destinationPath] = node;
                }
            }

            // Sort the tree
            SortTree(root);

            // Update selection states for all folders
            UpdateAllSelectionStates(root);

            return root;
        }

        private static string GetDestinationPath(object item)
        {
            if (DestinationAssetPathField == null || item == null) return null;
            try
            {
                return DestinationAssetPathField.GetValue(item) as string;
            }
            catch
            {
                return null;
            }
        }

        private static bool GetIsFolder(object item)
        {
            if (IsFolderField == null || item == null) return false;
            try
            {
                object value = IsFolderField.GetValue(item);
                return value is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private static int GetEnabledStatus(object item)
        {
            if (EnabledStatusField == null || item == null) return 1; // Default to enabled
            try
            {
                object value = EnabledStatusField.GetValue(item);
                return value is int i ? i : 1;
            }
            catch
            {
                return 1;
            }
        }

        private static string GetParentPath(string path)
        {
            int lastSlash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            if (lastSlash <= 0) return "Assets";
            return path.Substring(0, lastSlash);
        }

        private static string GetFileName(string path)
        {
            int lastSlash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            if (lastSlash < 0) return path;
            return path.Substring(lastSlash + 1);
        }

        private static int GetDepth(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            int depth = 0;
            for (int i = 0; i < path.Length; i++)
            {
                if (path[i] == '/' || path[i] == '\\')
                {
                    depth++;
                }
            }
            return depth;
        }

        private static PackageItemNode GetOrCreateFolderPath(PackageItemNode root, string folderPath, Dictionary<string, PackageItemNode> pathMap)
        {
            if (pathMap.TryGetValue(folderPath, out var existing))
            {
                return existing;
            }

            // Build path from root
            string[] segments = folderPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments[0] != "Assets")
            {
                return root;
            }

            PackageItemNode current = root;
            string currentPath = "Assets";

            for (int i = 1; i < segments.Length; i++)
            {
                currentPath = currentPath + "/" + segments[i];

                if (pathMap.TryGetValue(currentPath, out var child))
                {
                    current = child;
                }
                else
                {
                    var newFolder = new PackageItemNode(segments[i], currentPath, true, i);
                    newFolder.IsExpanded = true;
                    current.Children.Add(newFolder);
                    pathMap[currentPath] = newFolder;
                    current = newFolder;
                }
            }

            return current;
        }

        private static void SortTree(PackageItemNode node)
        {
            // Sort: folders first, then files, both alphabetically
            node.Children = node.Children
                .OrderBy(c => !c.IsFolder) // Folders first
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var child in node.Children)
            {
                SortTree(child);
            }
        }

        private static void UpdateAllSelectionStates(PackageItemNode node)
        {
            if (node.IsFolder)
            {
                foreach (var child in node.Children)
                {
                    UpdateAllSelectionStates(child);
                }
                node.UpdateSelectionState();
            }
        }
    }
}






