using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
    internal static class ThryMaterialOptimizerBridge
    {
        private static readonly Type ShaderEditorType = FindType("Thry.ShaderEditor");
        private static readonly Type ShaderOptimizerType = FindType("Thry.ThryEditor.ShaderOptimizer");
        private static readonly Type ProgressBarType = FindType("Thry.ThryEditor.ShaderOptimizer+ProgressBar");

        public static void UnlockIfNeeded(IEnumerable<Material> materials, UnityEngine.Object context)
        {
            Material[] lockedMaterials = GetDistinctPoiyomiMaterials(materials)
                .Where(IsMaterialLocked)
                .ToArray();

            if (lockedMaterials.Length == 0)
            {
                return;
            }

            if (!InvokeMaterialOptimizer("UnlockMaterials", lockedMaterials, context))
            {
                Debug.LogWarning("[ThryMaterialOptimizerBridge] Could not unlock Poiyomi material copies before UV discard setup. Upload may keep an outdated locked shader variant.", context);
            }
        }

        public static void SyncAndLock(IEnumerable<Material> materials, UnityEngine.Object context)
        {
            Material[] materialArray = GetDistinctPoiyomiMaterials(materials).ToArray();
            if (materialArray.Length == 0)
            {
                return;
            }

            InvokeFixKeywords(materialArray, context);

            if (!InvokeMaterialOptimizer("LockMaterials", materialArray, context))
            {
                Debug.LogWarning("[ThryMaterialOptimizerBridge] Could not lock generated Poiyomi material copies after UV discard setup. Poiyomi's upload callback may still lock them later, but this can leave stale shader variants in edge cases.", context);
            }
        }

        private static IEnumerable<Material> GetDistinctPoiyomiMaterials(IEnumerable<Material> materials)
        {
            if (materials == null)
            {
                return Enumerable.Empty<Material>();
            }

            return materials
                .Where(IsPoiyomiOrLockedThryMaterial)
                .Distinct();
        }

        private static bool IsPoiyomiOrLockedThryMaterial(Material material)
        {
            if (material == null || material.shader == null)
            {
                return false;
            }

            return material.shader.name.ToLower().Contains("poiyomi") || IsMaterialLocked(material);
        }

        private static bool IsMaterialLocked(Material material)
        {
            if (ShaderOptimizerType == null)
            {
                return false;
            }

            MethodInfo method = ShaderOptimizerType.GetMethod("IsMaterialLocked", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                return false;
            }

            try
            {
                return method.Invoke(null, new object[] { material }) is bool isLocked && isLocked;
            }
            catch
            {
                return false;
            }
        }

        private static void InvokeFixKeywords(Material[] materials, UnityEngine.Object context)
        {
            if (ShaderEditorType == null)
            {
                return;
            }

            MethodInfo method = ShaderEditorType.GetMethod("FixKeywords", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                return;
            }

            try
            {
                method.Invoke(null, new object[] { materials });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ThryMaterialOptimizerBridge] Failed to sync Poiyomi keywords: {GetBaseMessage(ex)}", context);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static bool InvokeMaterialOptimizer(string methodName, Material[] materials, UnityEngine.Object context)
        {
            if (ShaderOptimizerType == null || ProgressBarType == null)
            {
                return false;
            }

            MethodInfo method = ShaderOptimizerType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                return false;
            }

            try
            {
                object noProgress = Enum.Parse(ProgressBarType, "None");
                object result = method.Invoke(null, new[] { (object)materials, noProgress });
                return !(result is bool succeeded) || succeeded;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ThryMaterialOptimizerBridge] Failed to call Thry ShaderOptimizer.{methodName}: {GetBaseMessage(ex)}", context);
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static string GetBaseMessage(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null)
            {
                ex = ex.InnerException;
            }

            return ex.Message;
        }
    }
}
