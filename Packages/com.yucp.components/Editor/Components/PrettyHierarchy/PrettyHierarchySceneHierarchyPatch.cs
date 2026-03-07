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
        private static readonly Dictionary<object, int> LastRowCountByTreeView = new Dictionary<object, int>();
        /// <summary>Last total height we computed (sum of row heights), keyed by TreeView GUI instance. Used so TreeViewController can use it for content rect.</summary>
        private static readonly Dictionary<object, float> LastTotalHeightByGui = new Dictionary<object, float>();
        private static readonly object CacheLock = new object();

        static PrettyHierarchySceneHierarchyPatch()
        {
            ApplyOnce();
        }

        private static void ApplyOnce()
        {
            Type treeViewGUIType = Type.GetType("UnityEditor.IMGUI.Controls.TreeViewGUI, UnityEditor.CoreModule");
            Type treeViewType = Type.GetType("UnityEditor.IMGUI.Controls.TreeView, UnityEditor.CoreModule");
            Type treeViewControlGUIType = treeViewType != null ? treeViewType.GetNestedType("TreeViewControlGUI", BindingFlags.NonPublic) : null;
            if (treeViewGUIType == null)
                return;

            var harmony = new Harmony("com.yucp.prettyhierarchy");

            // Patch TreeViewGUI (base) - GameObjectTreeViewGUI inherits these, so patches run for the hierarchy
            MethodInfo getRowRect = treeViewGUIType.GetMethod("GetRowRect", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo getTotalSize = treeViewGUIType.GetMethod("GetTotalSize", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo getFirstLast = treeViewGUIType.GetMethod("GetFirstAndLastRowVisible", BindingFlags.Public | BindingFlags.Instance);

            if (getRowRect != null)
                harmony.Patch(getRowRect, postfix: new HarmonyMethod(typeof(PrettyHierarchySceneHierarchyPatch), nameof(GetRowRectPostfix)));
            if (getTotalSize != null)
                harmony.Patch(getTotalSize, prefix: new HarmonyMethod(typeof(PrettyHierarchySceneHierarchyPatch), nameof(GetTotalSizePrefix)));
            if (getFirstLast != null)
                harmony.Patch(getFirstLast, postfix: new HarmonyMethod(typeof(PrettyHierarchySceneHierarchyPatch), nameof(GetFirstAndLastRowVisiblePostfix)));

            // TreeViewControlGUI (Project Browser etc - not hierarchy)
            if (treeViewControlGUIType != null)
            {
                MethodInfo controlGetTotalSize = treeViewControlGUIType.GetMethod("GetTotalSize", BindingFlags.Public | BindingFlags.Instance);
                if (controlGetTotalSize != null)
                    harmony.Patch(controlGetTotalSize, postfix: new HarmonyMethod(typeof(PrettyHierarchySceneHierarchyPatch), nameof(TreeViewControlGUI_GetTotalSizePostfix)));
                MethodInfo controlGetFirstLast = treeViewControlGUIType.GetMethod("GetFirstAndLastRowVisible", BindingFlags.Public | BindingFlags.Instance);
                if (controlGetFirstLast != null)
                    harmony.Patch(controlGetFirstLast, postfix: new HarmonyMethod(typeof(PrettyHierarchySceneHierarchyPatch), nameof(GetFirstAndLastRowVisiblePostfix)));
            }

            if (treeViewType != null)
            {
                MethodInfo getCustomRowHeight = treeViewType.GetMethod("GetCustomRowHeight", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (getCustomRowHeight != null)
                    harmony.Patch(getCustomRowHeight, postfix: new HarmonyMethod(typeof(PrettyHierarchySceneHierarchyPatch), nameof(GetCustomRowHeightPostfix)));
            }

            Type controllerType = Type.GetType("UnityEditor.IMGUI.Controls.TreeViewController, UnityEditor.CoreModule");
            if (controllerType != null)
            {
                MethodInfo onGUI = controllerType.GetMethod("OnGUI", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Rect), typeof(int) }, null);
                FieldInfo totalRectField = controllerType.GetField("m_TotalRect", BindingFlags.NonPublic | BindingFlags.Instance);
                if (onGUI != null && totalRectField != null)
                    harmony.Patch(onGUI, prefix: new HarmonyMethod(typeof(PrettyHierarchySceneHierarchyPatch), nameof(TreeViewController_OnGUIPrefix)));
            }
        }

        /// <summary>Unity only updates m_TotalRect on Repaint, so viewport is stale on Layout etc. Set it every frame so the list area matches the window size.</summary>
        private static void TreeViewController_OnGUIPrefix(object __instance, Rect rect)
        {
            if (__instance == null) return;
            try
            {
                FieldInfo totalRectField = __instance.GetType().GetField("m_TotalRect", BindingFlags.NonPublic | BindingFlags.Instance);
                totalRectField?.SetValue(__instance, rect);
            }
            catch { }
        }

        private static float GetRowHeightForInstanceId(int instanceId)
        {
            UnityEngine.Object obj = EditorUtility.InstanceIDToObject(instanceId);
            if (obj is not GameObject go)
                return DefaultRowHeight;
            var data = go.GetComponent<PrettyHierarchyData>();
            if (data == null)
                return DefaultRowHeight;
            if (data.UseCustomRowHeight)
                return data.CustomRowHeight;
            return Mathf.Max(DefaultRowHeight, data.FontSize + 8f);
        }

        /// <summary>Patch TreeView.GetCustomRowHeight so Unity's layout uses our per-row heights (fixes scroll content and clipping).</summary>
        /// <remarks>Parameter name "item" must match Unity's GetCustomRowHeight(int row, TreeViewItem item).</remarks>
        private static void GetCustomRowHeightPostfix(object __instance, int row, object item, ref float __result)
        {
            if (item == null) return;
            try
            {
                PropertyInfo idProp = item.GetType().GetProperty("id", BindingFlags.Public | BindingFlags.Instance);
                if (idProp == null) return;
                int id = (int)idProp.GetValue(item);
                float h = GetRowHeightForInstanceId(id);
                if (h != DefaultRowHeight)
                    __result = h;
            }
            catch { }
        }

        private static bool TryGetRowHeights(object guiInstance, out List<float> heights)
        {
            lock (CacheLock)
            {
                return RowHeightsByTreeView.TryGetValue(guiInstance, out heights);
            }
        }

        /// <summary>Build or refresh the row-heights cache for this TreeView GUI so it matches the current flattened rows (e.g. after expand/collapse).</summary>
        private static List<float> EnsureRowHeights(object guiInstance)
        {
            try
            {
                Type guiType = guiInstance.GetType();
                FieldInfo treeViewField = guiType.GetField("m_TreeView", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (treeViewField == null)
                    return null;

                object treeView = treeViewField.GetValue(guiInstance);
                if (treeView == null)
                    return null;

                PropertyInfo isSearchingProp = treeView.GetType().GetProperty("isSearching", BindingFlags.Public | BindingFlags.Instance);
                if (isSearchingProp != null && (bool)isSearchingProp.GetValue(treeView))
                    return null;

                PropertyInfo dataProp = treeView.GetType().GetProperty("data", BindingFlags.Public | BindingFlags.Instance);
                if (dataProp == null)
                    return null;

                object data = dataProp.GetValue(treeView);
                if (data == null)
                    return null;

                MethodInfo getRows = data.GetType().GetMethod("GetRows", BindingFlags.Public | BindingFlags.Instance);
                if (getRows == null)
                    return null;

                System.Collections.IList rows = (System.Collections.IList)getRows.Invoke(data, null);
                if (rows == null || rows.Count == 0)
                    return null;

                var heights = new List<float>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    object item = rows[i];
                    int id = (int)item.GetType().GetProperty("id", BindingFlags.Public | BindingFlags.Instance).GetValue(item);
                    heights.Add(GetRowHeightForInstanceId(id));
                }

                lock (CacheLock)
                {
                    RowHeightsByTreeView[guiInstance] = heights;
                    int prevCount = LastRowCountByTreeView.TryGetValue(guiInstance, out int c) ? c : -1;
                    if (prevCount != heights.Count)
                    {
                        LastRowCountByTreeView[guiInstance] = heights.Count;
                        if (prevCount >= 0)
                            EditorApplication.delayCall += () => EditorApplication.RepaintHierarchyWindow();
                    }
                }
                return heights;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Extra height added to reported content so the scroll view never clips the last row.</summary>
        private const float ContentHeightPadding = 2f;

        /// <summary>TreeViewControlGUI.GetTotalSize postfix: always ensure content height is at least our computed total (+ padding) so the scroll container never cuts the bottom.</summary>
        private static void TreeViewControlGUI_GetTotalSizePostfix(object __instance, ref Vector2 __result)
        {
            if (__instance == null) return;
            try
            {
                // Always compute our own total from row heights so we never report too small a height (e.g. when m_RowRects is null with many objects, or stale when scrolled).
                List<float> heights = EnsureRowHeights(__instance);
                float ourTotal = 0f;
                if (heights != null && heights.Count > 0)
                {
                    for (int i = 0; i < heights.Count; i++)
                        ourTotal += heights[i];
                }
                // Use the larger of Unity's value and ours, plus padding so the scroll area doesn't clip the last row
                float contentHeight = Mathf.Max(__result.y, ourTotal) + ContentHeightPadding;
                __result.y = contentHeight;
            }
            catch { }
        }

        /// <summary>When we have custom row heights, set total size ourselves and skip original so our height is always used (fixes cut-off).</summary>
        private static bool GetTotalSizePrefix(object __instance, ref Vector2 __result)
        {
            try
            {
                List<float> heights = EnsureRowHeights(__instance);
                if (heights == null || heights.Count == 0)
                    return true;

                float y = 0f;
                for (int i = 0; i < heights.Count; i++)
                    y += heights[i];
                float defaultTotal = heights.Count * DefaultRowHeight;
                bool hasCustomHeights = Math.Abs(y - defaultTotal) > 0.01f;
                if (!hasCustomHeights)
                    return true;

                // We have custom row heights: set result and skip original. Padding so scroll doesn't clip last row.
                __result = new Vector2(1f, y + ContentHeightPadding);

                lock (CacheLock)
                {
                    if (__instance != null)
                        LastTotalHeightByGui[__instance] = y;
                }

                Type guiType = __instance.GetType();
                FieldInfo treeViewField = guiType.GetField("m_TreeView", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (treeViewField != null && treeViewField.GetValue(__instance) is object treeViewForRefresh)
                {
                    try
                    {
                        MethodInfo refresh = treeViewForRefresh.GetType().GetMethod("RefreshCustomRowHeights", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        refresh?.Invoke(treeViewForRefresh, null);
                    }
                    catch { }
                }
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static void GetRowRectPostfix(object __instance, int row, float rowWidth, ref Rect __result)
        {
            if (!TryGetRowHeights(__instance, out List<float> cached) || cached == null || row < 0 || row >= cached.Count)
                cached = EnsureRowHeights(__instance);
            if (cached == null || row < 0 || row >= cached.Count)
                return;
            List<float> heights = cached;

            float y = 0f;
            for (int i = 0; i < row; i++)
                y += heights[i];
            float height = heights[row];
            __result = new Rect(__result.x, y, rowWidth, height);
        }

        private static void GetFirstAndLastRowVisiblePostfix(object __instance, ref int firstRowVisible, ref int lastRowVisible)
        {
            List<float> heights = EnsureRowHeights(__instance);
            if (heights == null || heights.Count == 0)
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
                    FieldInfo scrollPosField = state.GetType().GetField("scrollPos", BindingFlags.Public | BindingFlags.Instance);
                    if (scrollPosField != null && scrollPosField.GetValue(state) is Vector2 scrollPos)
                        scrollY = scrollPos.y;
                }

                MethodInfo getTotalRect = treeView.GetType().GetMethod("GetTotalRect", BindingFlags.Public | BindingFlags.Instance);
                float viewHeight = getTotalRect != null ? ((Rect)getTotalRect.Invoke(treeView, null)).height : 400f;

                int first = -1, last = -1;
                float y = 0f;
                for (int i = 0; i < heights.Count; i++)
                {
                    float h = heights[i];
                    // Include row if it overlaps visible range (with small tolerance for rounding)
                    if (y + h >= scrollY - 1f && y <= scrollY + viewHeight + 1f)
                    {
                        if (first < 0) first = i;
                        last = i;
                    }
                    y += h;
                }

                // Pad by two rows above/below so bottom (and top) stay drawn when window is small or heavily scrolled
                firstRowVisible = first >= 0 ? Math.Max(0, first - 2) : 0;
                lastRowVisible = last >= 0 ? Math.Min(heights.Count - 1, last + 2) : heights.Count - 1;
            }
            catch
            {
                // ignore
            }
        }
    }
}
