using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Array/list editor with visual feedback, drag handles, and quick actions.
    /// </summary>
    public class YUCPArrayEditor
    {
        private readonly SerializedProperty property;
        private readonly string label;
        private readonly bool allowAdd;
        private readonly bool allowRemove;
        private readonly bool allowReorder;
        private readonly bool showElementLabels;
        private ReorderableList reorderableList;

        public YUCPArrayEditor(SerializedProperty property, string label = null, bool allowAdd = true, bool allowRemove = true, bool allowReorder = true, bool showElementLabels = true)
        {
            this.property = property;
            this.label = label ?? property.displayName;
            this.allowAdd = allowAdd;
            this.allowRemove = allowRemove;
            this.allowReorder = allowReorder;
            this.showElementLabels = showElementLabels;
            
            InitializeReorderableList();
        }

        private void InitializeReorderableList()
        {
            reorderableList = new ReorderableList(property.serializedObject, property, allowReorder, true, allowAdd, allowRemove);
            
            reorderableList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, label);
            };
            
            reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = property.GetArrayElementAtIndex(index);
                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;
                
                if (showElementLabels)
                {
                    EditorGUI.PropertyField(rect, element, new GUIContent($"Element {index}"), true);
                }
                else
                {
                    EditorGUI.PropertyField(rect, element, GUIContent.none, true);
                }
            };
            
            reorderableList.elementHeightCallback = (int index) =>
            {
                var element = property.GetArrayElementAtIndex(index);
                return EditorGUI.GetPropertyHeight(element, true) + 4;
            };
        }

        public void DoLayoutList()
        {
            if (reorderableList != null)
            {
                reorderableList.DoLayoutList();
            }
        }

        public void DoList(Rect rect)
        {
            if (reorderableList != null)
            {
                reorderableList.DoList(rect);
            }
        }
    }
}

