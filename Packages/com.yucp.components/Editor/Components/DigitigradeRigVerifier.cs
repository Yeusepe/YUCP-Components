using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Checks a built digitigrade rig against the structural invariants taken from the Rexouium
    /// reference implementation.
    ///
    /// The rig is only assembled during the avatar build, so point this at either the clone VRCFury
    /// produces when you enter Play Mode, or an avatar the rig preview has been applied to.
    /// </summary>
    public static class DigitigradeRigVerifier
    {
        private const string LimbIkType = "RootMotion.FinalIK.LimbIK";
        private const string ExecutionOrderType = "RootMotion.FinalIK.IKExecutionOrder";

        [MenuItem("Tools/YUCP/Digitigrade/Verify Rig Conformance", false, 100)]
        private static void VerifySelection()
        {
            var target = Selection.activeGameObject;
            if (target == null)
            {
                EditorUtility.DisplayDialog("YUCP Digitigrade", "Select an avatar with a built rig first.", "OK");
                return;
            }

            var report = Verify(target);
            Debug.Log("[YUCP Digitigrade] Rig conformance for '" + target.name + "'\n" + report.ToString(), target);

            EditorUtility.DisplayDialog("YUCP Digitigrade",
                report.Failed == 0
                    ? $"All {report.Passed} checks passed.\n\nSee the console for detail."
                    : $"{report.Passed} passed, {report.Failed} FAILED.\n\nSee the console for detail.",
                "OK");
        }

        [MenuItem("Tools/YUCP/Digitigrade/Verify Rig Conformance", true)]
        private static bool VerifySelectionEnabled() => Selection.activeGameObject != null;

        public class Report
        {
            public int Passed;
            public int Failed;
            private readonly List<string> lines = new List<string>();

            public void Check(string name, bool ok, string detail = "")
            {
                if (ok) Passed++; else Failed++;
                lines.Add((ok ? "  PASS  " : "  FAIL  ") + name + (string.IsNullOrEmpty(detail) ? "" : "   " + detail));
            }

            public override string ToString() =>
                string.Join("\n", lines) + $"\n\nRESULT: {Passed} passed, {Failed} failed";
        }

        public static Report Verify(GameObject avatarRoot)
        {
            var report = new Report();

            var animator = avatarRoot.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
            {
                report.Check("humanoid avatar", false, "no humanoid Animator on the selected object");
                return report;
            }

            var solvers = Components(avatarRoot, LimbIkType);
            var executors = Components(avatarRoot, ExecutionOrderType);

            // The rig is assembled during the avatar build, so an un-built avatar has none of it.
            // Reporting that as a list of failures is just noise -- say what actually happened.
            var plantiBones = avatarRoot.GetComponentsInChildren<Transform>(true)
                .Count(t => t.name.StartsWith("Planti_"));

            if (solvers.Count == 0 && executors.Count == 0 && plantiBones == 0)
            {
                var hasRigComponent = avatarRoot.GetComponentInChildren<DigitigradeLegRigData>(true) != null;
                report.Check("rig has been built on this object", false,
                    hasRigComponent
                        ? "not built yet. The rig is assembled during the avatar build, not in the scene. "
                          + "Enter Play Mode (VRCFury builds the avatar) and run this on the result."
                        : "no Digitigrade Leg Rig component on this avatar, and nothing built. Add the component first.");
                return report;
            }

            report.Check("4 LimbIK solvers", solvers.Count == 4, solvers.Count.ToString());
            report.Check("1 IKExecutionOrder", executors.Count == 1, executors.Count.ToString());

            // The plantigrade passes normalise both legs before the digitigrade passes consume them.
            // Any other order leaves the digi chain reading last frame's solve, which jitters.
            if (executors.Count == 1)
            {
                var array = new SerializedObject(executors[0]).FindProperty("IKComponents");
                var order = Enumerable.Range(0, array.arraySize)
                    .Select(i => array.GetArrayElementAtIndex(i).objectReferenceValue as Component)
                    .Select(c => c == null ? "<null>" : c.gameObject.name)
                    .ToList();

                report.Check("both plantigrade passes run before both digitigrade passes",
                    order.Count == 4 && order.Take(2).All(n => n.StartsWith("Goal")) && order.Skip(2).All(n => n.Contains("Digi Solver")),
                    string.Join(" -> ", order));
            }

            var goal = solvers.FirstOrDefault(s => s.gameObject.name == "Goal.L");
            var digi = solvers.FirstOrDefault(s => s.gameObject.name == "Digi Solver.L");

            if (goal != null && digi != null)
            {
                var plantiBend = Vector3Property(goal, "solver.bendNormal");
                var digiBend = Vector3Property(digi, "solver.bendNormal");
                var dot = Vector3.Dot(plantiBend.normalized, digiBend.normalized);

                // Opposing bend planes are the entire trick: it is what makes the hock fold backwards
                // where the knee folds forwards.
                report.Check("knee and hock bend planes oppose", dot < -0.5f, "dot=" + dot.ToString("F2"));

                // The digi solver must NOT share the plantigrade goal outright: on a mesh authored
                // straight, solving to the identical foot pose just re-derives the bind pose and
                // the toggle does nothing. It targets the stance-pitched node instead, which rides
                // the plantigrade goal as a child so it still follows the tracked foot everywhere.
                var plantiTarget = ObjectProperty(goal, "solver.target") as Transform;
                var digiTarget = ObjectProperty(digi, "solver.target") as Transform;
                report.Check("digi goal rides the tracked foot goal",
                    plantiTarget != null && digiTarget != null && digiTarget.IsChildOf(plantiTarget) && digiTarget != plantiTarget,
                    (plantiTarget == null ? "<null>" : plantiTarget.name) + " <- " + (digiTarget == null ? "<null>" : digiTarget.name));

                report.Check("digitigrade solver spans three nodes",
                    ObjectProperty(digi, "solver.bone1.transform") != null &&
                    ObjectProperty(digi, "solver.bone2.transform") != null &&
                    ObjectProperty(digi, "solver.bone3.transform") != null);
            }
            else
            {
                report.Check("left leg solvers present", false, "Goal.L / Digi Solver.L not found");
            }

            // The humanoid must ride the hidden chain, or VRChat's IK fights the constraints.
            var upperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            report.Check("humanoid rides the hidden plantigrade chain",
                upperLeg != null && upperLeg.name.StartsWith("Planti_") &&
                foot != null && foot.name.StartsWith("Planti_"),
                (upperLeg == null ? "<null>" : upperLeg.name) + " / " + (foot == null ? "<null>" : foot.name));

            report.Check("toes left unmapped",
                animator.GetBoneTransform(HumanBodyBones.LeftToes) == null);

            // The metatarsus switchboard: rest / solved / ankle-down / ankle-up.
            var metatarsus = avatarRoot.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name.StartsWith("Metatarsus") && t.name.EndsWith(".L"));
            if (metatarsus != null)
            {
                var constraint = metatarsus.GetComponents<Component>()
                    .FirstOrDefault(c => c != null && c.GetType().Name.Contains("Constraint"));

                if (constraint == null) report.Check("metatarsus constraint", false, "none");
                else
                {
                    var so = new SerializedObject(constraint);
                    var sources = Enumerable.Range(0, 8)
                        .Count(i => so.FindProperty("Sources.source" + i + ".SourceTransform")?.objectReferenceValue != null);
                    report.Check("metatarsus has 4 sources (Rex TopFut layout)", sources == 4, sources.ToString());

                    var weights = Enumerable.Range(0, 4)
                        .Select(i => so.FindProperty("Sources.source" + i + ".Weight")?.floatValue ?? 0f).ToArray();
                    report.Check("rest state is plantigrade",
                        Mathf.Approximately(weights[0], 1f) && weights.Skip(1).All(w => Mathf.Approximately(w, 0f)),
                        string.Join("/", weights.Select(w => w.ToString("0.##"))));
                }
            }
            else report.Check("metatarsus bone present", false, "none found");

            // Nothing may reference a destroyed bone -- that silently collapses the mesh.
            var nullBones = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Sum(s => s.bones.Count(b => b == null));
            report.Check("no dangling bone references", nullBones == 0, nullBones.ToString());

            return report;
        }

        private static List<MonoBehaviour> Components(GameObject root, string typeName) =>
            root.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(m => m != null && m.GetType().FullName == typeName)
                .ToList();

        private static Vector3 Vector3Property(Object target, string path) =>
            new SerializedObject(target).FindProperty(path)?.vector3Value ?? Vector3.zero;

        private static Object ObjectProperty(Object target, string path) =>
            new SerializedObject(target).FindProperty(path)?.objectReferenceValue;
    }
}
