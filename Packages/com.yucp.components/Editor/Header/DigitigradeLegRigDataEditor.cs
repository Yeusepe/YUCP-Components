using UnityEditor;
using UnityEngine;
using YUCP.Components;

namespace YUCP.Components.Editor
{
    [CustomEditor(typeof(DigitigradeLegRigData))]
    public class DigitigradeLegRigDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var data = (DigitigradeLegRigData)target;

            EditorGUILayout.HelpBox(
                "Builds the Rexouium-style rig at avatar build time: a hidden plantigrade chain for " +
                "the humanoid to ride, four Final IK solvers, the constraint switchboard and the FX " +
                "layer. Nothing is created in the scene -- enter Play Mode to see it, since VRCFury " +
                "builds the avatar on play.", MessageType.None);

            var split = data.GetComponentInParent<Animator>() != null
                ? data.GetComponentInParent<Animator>().GetComponentInChildren<DigitigradeLegSplitData>(true)
                : null;

            data.ResolveBones();

            if (!data.IsComplete())
            {
                var metatarsusMissing = data.leftMetatarsus == null || data.rightMetatarsus == null;

                if (metatarsusMissing && split != null)
                {
                    // Expected: the split inserts the metatarsus during the build, at an earlier
                    // callback order than this component runs, so it cannot exist in the scene yet.
                    EditorGUILayout.HelpBox(
                        "Metatarsus is empty, which is normal — the Digitigrade Leg Split component " +
                        "creates that bone during the build, before this component reads it. Nothing " +
                        "to fix here.", MessageType.Info);
                }
                else if (metatarsusMissing)
                {
                    EditorGUILayout.HelpBox(
                        "No metatarsus bone, and no Digitigrade Leg Split component on this avatar. " +
                        "This rig needs a four-segment leg. Either add the Split component, or assign " +
                        "the metatarsus by hand if the rig already has one.", MessageType.Error);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Some leg bones could not be resolved. Assign them by hand below.", MessageType.Warning);
                }
            }

            EditorGUILayout.Space();
            DrawDefaultInspector();

            var summary = data.GetBuildSummary();
            if (!string.IsNullOrEmpty(summary))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Last build", summary, EditorStyles.wordWrappedMiniLabel);
            }
        }
    }
}
