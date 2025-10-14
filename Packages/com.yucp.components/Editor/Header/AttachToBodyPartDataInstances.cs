using UnityEditor;
using UnityEngine.UIElements;
using YUCP.Components;
namespace YUCP.Components.Resources
{    
    /// <summary>
    /// Header overlay editor for Symmetric Armature Auto-Link components.
    /// </summary>
    [CustomEditor(typeof(AttachToBodyPartData))]
    public class AttachToBodyPartDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(YUCPComponentHeader.CreateHeaderOverlay("Symetric Armature Auto-Link"));
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