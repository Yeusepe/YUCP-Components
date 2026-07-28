using System;
using System.Collections.Generic;
using System.Linq;
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

        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();

        public static bool IsAvailable => FindType(LimbIkTypeName) != null && FindType(ExecutionOrderTypeName) != null;

        public static Type FindType(string fullName)
        {
            if (TypeCache.TryGetValue(fullName, out var cached)) return cached;

            Type found = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch { continue; }

                found = types.FirstOrDefault(t => t.FullName == fullName);
                if (found != null) break;
            }

            TypeCache[fullName] = found;
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
            // bendModifier 1 == Target: take the bend plane from the goal's rotation.
            Set(so, "solver.bendModifier", p => p.enumValueIndex = 1);
            Set(so, "solver.bendModifierWeight", p => p.floatValue = 1f);
            Set(so, "solver.maintainRotationWeight", p => p.floatValue = 0f);
            so.ApplyModifiedPropertiesWithoutUndo();

            return component;
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
