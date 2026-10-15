using UnityEditor;
using UnityEngine.UIElements;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(RotationDampingConstraintData))]
    public class RotationDampingConstraintDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (RotationDampingConstraintData)target;
            return VRLabsInspector.DampingConstraint(this, "Rotation", data.GetBuildSummary, () => data.GetLastBuildTimeUtc());
        }
    }
}
