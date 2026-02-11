using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEditor;
using YUCP.Components;

namespace YUCP.Components.Editor
{
    [InitializeOnLoad]
    internal static class PrettyHierarchySceneHierarchyPatch
    {
        private const float DefaultRowHeight = 16f;

        // Cache per tree-view instance: list of row heights (rebuilt when GetTotalSize runs)
        private static readonly Dictionary<object, List<float>> RowHeightsByTreeView = new Dictionary<object, List<float>>();
        private static readonly object CacheLock = new object();

        static PrettyHierarchySceneHierarchyPatch()
        {
            ApplyOnce();
        }

        private static void ApplyOnce()
        {
            Type treeViewGUIType = Type.GetType("UnityEditor.IMGUI.Controls.TreeViewGUI, UnityEditor.CoreModule");
            if (treeViewGUIType == null)
                return;

            var harmony = new Harmony("com.yucp.prettyhierarchy");

            MethodInfo getRowRect = treeViewGUIType.GetMethod("GetRowRect", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo getTotalSize = treeViewGUIType.GetMethod("GetTotalSize", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo getFirstLast = treeViewGUIType.GetMethod("GetFirstAndLastRowVisible", BindingFlags.Public | BindingFlags.Instance);

            if (getRowRect != null)
                harmony.Patch(getRowRect, postfix: new HarmonyMethod(typeof(PrettyHierarchySceneHierarchyPatch), nameof(GetRowRectPostfix)));
            if (getTotalSize != null)
                harmony.Patch(getTotalSize, postfix: new HarmonyMethod(typeof(PrettyHierarchySceneHierarchyPatch), nameof(GetTotalSizePostfix)));
            if (getFirstLast != null)
                harmony.Patch(getFirstLast, postfix: new HarmonyMethod(typeof(PrettyHierarchySceneHierarchyPatch), nameof(GetFirstAndLastRowVisiblePostfix)));
        }

        private static float GetRowHeightForInstanceId(int instanceId)
        {
            UnityEngine.Object obj = EditorUtility.InstanceIDToObject(instanceId);
            if (obj is not GameObject go)
                return DefaultRowHeight;
            var data = go.GetComponent<PrettyHierarchyData>();
            if (data == null || !data.UseCustomRowHeight)
                return DefaultRowHeight;
            return data.CustomRowHeight;
        }

        private static bool TryGetRowHeights(object guiInstance, out List<float> heights)
        {
            lock (CacheLock)
            {
                return RowHeightsByTreeView.TryGetValue(guiInstance, out heights);
            }
        }

        private static void GetTotalSizePostfix(object __instance, ref Vector2 __result)
        {
            try
            {
                Type guiType = __instance.GetType();
                FieldInfo treeViewField = guiType.GetField("m_TreeView", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (treeViewField == null)
                    return;

                object treeView = treeViewField.GetValue(__instance);
                if (treeView == null)
                    return;

                PropertyInfo isSearchingProp = treeView.GetType().GetProperty("isSearching", BindingFlags.Public | BindingFlags.Instance);
                if (isSearchingProp != null && (bool)isSearchingProp.GetValue(treeView))
                    return;

                PropertyInfo dataProp = treeView.GetType().GetProperty("data", BindingFlags.Public | BindingFlags.Instance);
                if (dataProp == null)
                    return;

                object data = dataProp.GetValue(treeView);
                if (data == null)
                    return;

                MethodInfo getRows = data.GetType().GetMethod("GetRows", BindingFlags.Public | BindingFlags.Instance);
                if (getRows == null)
                    return;

                System.Collections.IList rows = (System.Collections.IList)getRows.Invoke(data, null);
                if (rows == null || rows.Count == 0)
                    return;

                MethodInfo getTotalRect = treeView.GetType().GetMethod("GetTotalRect", BindingFlags.Public | BindingFlags.Instance);
                float width = __result.x;
                if (getTotalRect != null)
                {
                    object rectObj = getTotalRect.Invoke(treeView, null);
                    if (rectObj != null)
                        width = ((Rect)rectObj).width;
                }

                var heights = new List<float>(rows.Count);
                float y = 0f;
                for (int i = 0; i < rows.Count; i++)
                {
                    object item = rows[i];
                    int id = (int)item.GetType().GetProperty("id", BindingFlags.Public | BindingFlags.Instance).GetValue(item);
                    float h = GetRowHeightForInstanceId(id);
                    heights.Add(h);
                    y += h;
                }

                lock (CacheLock)
                {
                    RowHeightsByTreeView[__instance] = heights;
                }

                __result = new Vector2(width, y);
            }
            catch
            {
                // ignore
            }
        }

        private static void GetRowRectPostfix(object __instance, int row, float rowWidth, ref Rect __result)
        {
            if (!TryGetRowHeights(__instance, out List<float> heights) || row < 0 || row >= heights.Count)
                return;

            float y = 0f;
            for (int i = 0; i < row; i++)
                y += heights[i];
            float height = heights[row];
            __result = new Rect(__result.x, y, rowWidth, height);
        }

        private static void GetFirstAndLastRowVisiblePostfix(object __instance, ref int firstRowVisible, ref int lastRowVisible)
        {
            if (!TryGetRowHeights(__instance, out List<float> heights) || heights.Count == 0)
                return;

            try
            {
                Type guiType = __instance.GetType();
                FieldInfo treeViewField = guiType.GetField("m_TreeView", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (treeViewField == null)
                    return;

                object treeView = treeViewField.GetValue(__instance);
                if (treeView == null)
                    return;

                object state = treeView.GetType().GetProperty("state", BindingFlags.Public | BindingFlags.Instance)?.GetValue(treeView);
                float scrollY = 0f;
                if (state != null)
                {
                    object scrollPos = state.GetType().GetProperty("scrollPos")?.GetValue(state);
                    if (scrollPos != null)
                        scrollY = (float)scrollPos.GetType().GetField("y").GetValue(scrollPos);
                }

                MethodInfo getTotalRect = treeView.GetType().GetMethod("GetTotalRect", BindingFlags.Public | BindingFlags.Instance);
                float viewHeight = getTotalRect != null ? ((Rect)getTotalRect.Invoke(treeView, null)).height : 400f;

                int first = -1, last = -1;
                float y = 0f;
                for (int i = 0; i < heights.Count; i++)
                {
                    float h = heights[i];
                    if (y + h > scrollY && y < scrollY + viewHeight)
                    {
                        if (first < 0) first = i;
                        last = i;
                    }
                    y += h;
                }

                firstRowVisible = first >= 0 ? first : 0;
                lastRowVisible = last >= 0 ? last : heights.Count - 1;
            }
            catch
            {
                // ignore
            }
        }
    }
}
