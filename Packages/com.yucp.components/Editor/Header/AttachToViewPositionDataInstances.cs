using UnityEditor;
using UnityEngine.UIElements;
using YUCP.Components;

namespace YUCP.Components.Resources
{
    /// <summary>
    /// Header overlay editor for View Position & Head Auto-Link components.
    /// </summary>
    [CustomEditor(typeof(AttachToViewPositionData))]
    public class AttachToViewPositionDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(YUCPComponentHeader.CreateHeaderOverlay("View Position & Head Auto-Link"));
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