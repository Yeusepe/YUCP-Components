using UnityEditor;
using UnityEngine.UIElements;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(PositionDampingConstraintData))]
    public class PositionDampingConstraintDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var data = (PositionDampingConstraintData)target;
            return VRLabsInspector.DampingConstraint(this, "Position", data.GetBuildSummary, () => data.GetLastBuildTimeUtc());
        }
    }
}
