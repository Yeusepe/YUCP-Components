using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Creates and configures Final IK components without a compile-time dependency on them.
    ///
    /// Final IK ships as loose scripts under Assets/Plugins with no assembly definition, so it
    /// compiles into Assembly-CSharp-firstpass. Predefined assemblies are built after asmdefs, which
    /// means a package assembly can never reference those types directly. Components are therefore
    /// added by runtime type lookup and configured through SerializedObject property paths, which
    /// works on any MonoBehaviour regardless of which assembly declares it.
    /// </summary>
    public static class FinalIkBridge
    {
        public const string LimbIkTypeName = "RootMotion.FinalIK.LimbIK";
        public const string ExecutionOrderTypeName = "RootMotion.FinalIK.IKExecutionOrder";
        public const string CcdIkTypeName = "RootMotion.FinalIK.CCDIK";
        public const string HingeTypeName = "RootMotion.FinalIK.RotationLimitHinge";

        private static readonly Dictionary<string, Type> CachedTypes = new Dictionary<string, Type>();

        public static bool IsAvailable => FindType(LimbIkTypeName) != null && FindType(ExecutionOrderTypeName) != null;

        public static bool JointLimitsAvailable => FindType(CcdIkTypeName) != null &&
            FindType(HingeTypeName) != null;

        public static Vector2 CalculateHingeLimits(float restAngle, Vector2 limits)
        {

            var margin = Mathf.Min(0.2f, (limits.y - limits.x) * 0.25f);
            return new Vector2(restAngle - limits.y + margin, restAngle - limits.x - margin);
        }

        public static MonoBehaviour AddConstrainedLeg(GameObject host, Transform[] bones, Transform target,
            Vector3 hockNormal, Vector2 kneeLimits, Vector2 hockLimits)
        {
            for (int i = 1; i <= 2; i++)
            {
                var incoming = bones[i].position - bones[i - 1].position;
                var outgoing = bones[i + 1].position - bones[i].position;
                var axis = Vector3.Cross(incoming, outgoing);
                if (axis.sqrMagnitude < 1e-8f) axis = i == 1 ? -hockNormal : hockNormal;
                var restAngle = Vector3.Angle(-incoming, outgoing);
                var limits = i == 1 ? kneeLimits : hockLimits;
                var range = CalculateHingeLimits(restAngle, limits);
                var hinge = bones[i].gameObject.AddComponent(FindType(HingeTypeName));
                var hingeObject = new SerializedObject(hinge);
                Set(hingeObject, "axis", p => p.vector3Value = bones[i].InverseTransformDirection(axis.normalized));
                Set(hingeObject, "min", p => p.floatValue = range.x);
                Set(hingeObject, "max", p => p.floatValue = range.y);
                Set(hingeObject, "useLimits", p => p.boolValue = true);
                hingeObject.ApplyModifiedPropertiesWithoutUndo();
            }

            var component = host.AddComponent(FindType(CcdIkTypeName)) as MonoBehaviour;
            var so = new SerializedObject(component);


            Set(so, "fixTransforms", p => p.boolValue = false);
            var chain = so.FindProperty("solver.bones");
            chain.arraySize = bones.Length;
            for (int i = 0; i < bones.Length; i++)
            {
                chain.GetArrayElementAtIndex(i).FindPropertyRelative("transform").objectReferenceValue = bones[i];


                chain.GetArrayElementAtIndex(i).FindPropertyRelative("weight").floatValue = 1f - i / 3f;
            }
            Set(so, "solver.target", p => p.objectReferenceValue = target);
            Set(so, "solver.IKPositionWeight", p => p.floatValue = 1f);
            Set(so, "solver.useRotationLimits", p => p.boolValue = true);
            Set(so, "solver.maxIterations", p => p.intValue = 32);
            Set(so, "solver.tolerance", p => p.floatValue = Vector3.Distance(bones[0].position, bones[3].position) * 0.0001f);
            so.ApplyModifiedPropertiesWithoutUndo();
            return component;
        }

        public static Type FindType(string fullName)
        {
            if (CachedTypes.TryGetValue(fullName, out var cached)) return cached;

            Type found = null;
            foreach (var type in UnityEditor.TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
            {
                if (type.FullName != fullName)
                    continue;

                found = type;
                break;
            }

            CachedTypes[fullName] = found;
            return found;
        }

        /// <summary>
        /// Adds a LimbIK solving bone1-bone2-bone3 towards <paramref name="target"/>.
        /// <paramref name="bendNormal"/> is the normal of the bend plane; flipping it is what turns
        /// a knee into a hock.
        /// </summary>
        public static MonoBehaviour AddLimbIk(
            GameObject host,
            Transform bone1,
            Transform bone2,
            Transform bone3,
            Transform target,
            Vector3 bendNormal)
        {
            var type = FindType(LimbIkTypeName);
            if (type == null) return null;

            var component = host.AddComponent(type) as MonoBehaviour;
            if (component == null) return null;

            var so = new SerializedObject(component);
            Set(so, "fixTransforms", p => p.boolValue = true);
            Set(so, "solver.bone1.transform", p => p.objectReferenceValue = bone1);
            Set(so, "solver.bone2.transform", p => p.objectReferenceValue = bone2);
            Set(so, "solver.bone3.transform", p => p.objectReferenceValue = bone3);
            Set(so, "solver.target", p => p.objectReferenceValue = target);
            Set(so, "solver.bendNormal", p => p.vector3Value = bendNormal);
            Set(so, "solver.IKPositionWeight", p => p.floatValue = 1f);
            Set(so, "solver.IKRotationWeight", p => p.floatValue = 1f);

            Set(so, "solver.bendModifier", p => p.enumValueIndex = 1);
            Set(so, "solver.bendModifierWeight", p => p.floatValue = 1f);
            Set(so, "solver.maintainRotationWeight", p => p.floatValue = 0f);
            so.ApplyModifiedPropertiesWithoutUndo();

            return component;
        }

        /// <summary>
        /// Bends a LimbIK towards <paramref name="goal"/>. Unlike the Target modifier this has no
        /// state from the pose the solver initiates in, which for a nearly straight finger is noise.
        /// </summary>
        public static void UseBendGoal(MonoBehaviour limb, Transform goal)
        {
            var so = new SerializedObject(limb);

            Set(so, "solver.bendModifier", p => p.enumValueIndex = 4);
            Set(so, "solver.bendGoal", p => p.objectReferenceValue = goal);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Adds an IKExecutionOrder driving the given solvers in order. Without it the solve order
        /// between the plantigrade and digitigrade passes is undefined and the legs jitter.
        /// </summary>
        public static MonoBehaviour AddExecutionOrder(GameObject host, Animator animator, IList<MonoBehaviour> solvers)
        {
            var type = FindType(ExecutionOrderTypeName);
            if (type == null) return null;

            var component = host.AddComponent(type) as MonoBehaviour;
            if (component == null) return null;

            var so = new SerializedObject(component);
            var array = so.FindProperty("IKComponents");
            if (array != null)
            {
                array.arraySize = solvers.Count;
                for (int i = 0; i < solvers.Count; i++)
                {
                    array.GetArrayElementAtIndex(i).objectReferenceValue = solvers[i];
                }
            }
            Set(so, "animator", p => p.objectReferenceValue = animator);
            so.ApplyModifiedPropertiesWithoutUndo();

            return component;
        }

        private static void Set(SerializedObject so, string path, Action<SerializedProperty> apply)
        {
            var property = so.FindProperty(path);
            if (property != null) apply(property);
        }
    }
}
