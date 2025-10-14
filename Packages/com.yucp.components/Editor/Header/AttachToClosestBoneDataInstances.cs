using UnityEditor;
using UnityEngine.UIElements;
using YUCP.Components;

namespace YUCP.Components.Resources
{
    /// <summary>
    /// Header overlay editor for Closest Bone Auto-Link components.
    /// </summary>
    [CustomEditor(typeof(AttachToClosestBoneData))]
    public class AttachToClosestBoneDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(YUCPComponentHeader.CreateHeaderOverlay("Closest Bone Auto-Link"));
            var container = new IMGUIContainer(() => {
                serializedObject.Update();
                DrawPropertiesExcluding(serializedObject, "m_Script");
                serializedObject.ApplyModifiedProperties();
            });
            root.Add(container);
            return root;
        }
    }
}

