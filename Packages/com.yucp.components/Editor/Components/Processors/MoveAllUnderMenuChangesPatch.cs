using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using YUCP.Components;

namespace YUCP.Components.Editor
{
	/// <summary>
	/// Runs inside VRCFury's build pipeline. Patches MenuChangesService.Apply to inject MoveMenuItem
	/// actions for each Move All Under component so moves are applied when VRCFury finalizes the menu.
	/// </summary>
	internal static class MoveAllUnderMenuChangesPatch
	{
		private static bool _applied;
		private static Assembly _vrcfuryRuntime;

		[UnityEditor.InitializeOnLoadMethod]
		private static void Init()
		{
			if (_applied) return;
			try
			{
				_vrcfuryRuntime = Assembly.Load("VRCFury");
				if (_vrcfuryRuntime == null) return;

				Type menuChangesType = VRCFuryReflectionUtils.FindEditorAvatarType("VF.Service.MenuChangesService");
				if (menuChangesType == null) return;

				MethodInfo applyMethod = menuChangesType.GetMethod("Apply", BindingFlags.Public | BindingFlags.Instance);
				if (applyMethod == null) return;

				var harmony = new Harmony("com.yucp.moveallunder");
				harmony.Patch(applyMethod,
					prefix: new HarmonyMethod(typeof(MoveAllUnderMenuChangesPatch), nameof(Prefix)),
					postfix: new HarmonyMethod(typeof(MoveAllUnderMenuChangesPatch), nameof(Postfix)));
				_applied = true;
				Debug.Log("[YUCP Move All Under] Patched VRCFury MenuChangesService.Apply.");
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[YUCP Move All Under] Could not patch MenuChangesService: {ex.Message}");
			}
		}

		private static void Prefix(object __instance)
		{
			if (__instance == null) return;

			try
			{
				GameObject avatarGo = GetAvatarGameObject(__instance);
				if (avatarGo == null) return;

				Transform avatarRoot = avatarGo.transform;
				var dataList = avatarGo.GetComponentsInChildren<MoveAllUnderData>(true);
				if (dataList.Length == 0) return;

				System.Collections.IList extraPreActions = GetExtraPreActions(__instance);
				if (extraPreActions == null) return;

				Type moveMenuItemType = _vrcfuryRuntime?.GetType("VF.Model.Feature.MoveMenuItem");
				if (moveMenuItemType == null) return;

				FieldInfo fromPathField = moveMenuItemType.GetField("fromPath", BindingFlags.Public | BindingFlags.Instance);
				FieldInfo toPathField = moveMenuItemType.GetField("toPath", BindingFlags.Public | BindingFlags.Instance);
				if (fromPathField == null || toPathField == null) return;

				var toggleList = MoveAllUnderProcessor.CollectTogglePathAndTransformsStatic(avatarRoot, _vrcfuryRuntime);
				var seenFromPaths = new HashSet<string>();

				foreach (var (fromPath, toggleTransform) in toggleList)
				{
					if (string.IsNullOrWhiteSpace(fromPath)) continue;
					var chain = MoveAllUnderProcessor.GetMoveAllUnderChain(toggleTransform, avatarRoot);
					if (chain.Count == 0) continue;
					if (!seenFromPaths.Add(fromPath)) continue;

					string toPath = MoveAllUnderProcessor.BuildToPathFromChain(chain, fromPath);
					object moveModel = Activator.CreateInstance(moveMenuItemType);
					fromPathField.SetValue(moveModel, fromPath);
					toPathField.SetValue(moveModel, toPath);
					extraPreActions.Add(moveModel);
				}
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}
		}

		private static GameObject GetAvatarGameObject(object menuChangesService)
		{
			Type t = menuChangesService.GetType();
			FieldInfo globalsField = t.GetField("globals", BindingFlags.NonPublic | BindingFlags.Instance);
			if (globalsField == null) return null;

			object globals = globalsField.GetValue(menuChangesService);
			if (globals == null) return null;

			FieldInfo avatarField = globals.GetType().GetField("avatarObject", BindingFlags.Public | BindingFlags.Instance);
			if (avatarField == null) return null;

			object avatarObj = avatarField.GetValue(globals);
			if (avatarObj == null) return null;

			// VFGameObject has implicit conversion to GameObject; get _gameObject via reflection
			Type vfGoType = avatarObj.GetType();
			if (vfGoType.Name == "VFGameObject")
			{
				FieldInfo goField = vfGoType.GetField("_gameObject", BindingFlags.NonPublic | BindingFlags.Instance);
				if (goField != null)
					return goField.GetValue(avatarObj) as GameObject;
			}

			return avatarObj as GameObject;
		}

		private static System.Collections.IList GetExtraPreActions(object menuChangesService)
		{
			FieldInfo field = menuChangesService.GetType().GetField("extraPreActions", BindingFlags.NonPublic | BindingFlags.Instance);
			return field?.GetValue(menuChangesService) as System.Collections.IList;
		}

		/// <summary>
		/// After moves are applied, remove a submenu only if it has zero controls.
		/// Uses the same menu instance that Apply() modified (menuService.GetMenu().GetRaw()) so we never clean a stale copy.
		/// </summary>
		private static void Postfix(object __instance)
		{
			if (__instance == null) return;
			try
			{
				object raw = GetRootMenuFromService(__instance);
				if (raw is not VRCExpressionsMenu rootMenu) return;

				bool anyRemoved;
				do
				{
					anyRemoved = RemoveEmptySubmenusTyped(rootMenu);
				}
				while (anyRemoved);
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}
		}

		/// <summary>
		/// Gets the exact root menu that MenuChangesService.Apply() modifies (menuService.GetMenu().GetRaw()).
		/// </summary>
		private static object GetRootMenuFromService(object menuChangesService)
		{
			FieldInfo menuServiceField = menuChangesService.GetType().GetField("menuService", BindingFlags.NonPublic | BindingFlags.Instance);
			if (menuServiceField == null) return null;
			object menuService = menuServiceField.GetValue(menuChangesService);
			if (menuService == null) return null;
			MethodInfo getMenu = menuService.GetType().GetMethod("GetMenu", BindingFlags.Public | BindingFlags.Instance);
			if (getMenu == null) return null;
			object menuManager = getMenu.Invoke(menuService, null);
			if (menuManager == null) return null;
			MethodInfo getRaw = menuManager.GetType().GetMethod("GetRaw", BindingFlags.Public | BindingFlags.Instance);
			return getRaw?.Invoke(menuManager, null);
		}

		/// <summary>
		/// Total number of controls in this menu and all nested submenus. Used to ensure we only remove when truly empty.
		/// </summary>
		private static int CountAllControlsInMenu(VRCExpressionsMenu menu, HashSet<VRCExpressionsMenu> seen = null)
		{
			if (menu == null || menu.controls == null) return 0;
			seen ??= new HashSet<VRCExpressionsMenu>();
			if (seen.Contains(menu)) return 0;
			seen.Add(menu);
			int n = menu.controls.Count;
			foreach (var c in menu.controls)
			{
				if (c == null) continue;
				if (c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && c.subMenu != null)
					n += CountAllControlsInMenu(c.subMenu, seen);
			}
			return n;
		}

		/// <summary>
		/// Removes a submenu control only when it is truly empty: zero controls in it and in any nested submenu.
		/// </summary>
		private static bool RemoveEmptySubmenusTyped(VRCExpressionsMenu menu)
		{
			if (menu == null || menu.controls == null) return false;
			if (menu.controls.Count == 0) return false;

			// Recurse into submenus first (post-order)
			bool anyRemoved = false;
			for (int i = 0; i < menu.controls.Count; i++)
			{
				var c = menu.controls[i];
				if (c == null) continue;
				if (c.type != VRCExpressionsMenu.Control.ControlType.SubMenu) continue;
				if (c.subMenu != null && RemoveEmptySubmenusTyped(c.subMenu))
					anyRemoved = true;
			}

			// Remove only when the entire subtree has zero controls (never remove if any item exists anywhere inside)
			for (int i = menu.controls.Count - 1; i >= 0; i--)
			{
				var c = menu.controls[i];
				if (c == null) continue;
				if (c.type != VRCExpressionsMenu.Control.ControlType.SubMenu) continue;
				if (c.subMenu == null) continue;
				if (CountAllControlsInMenu(c.subMenu) != 0) continue;
				menu.controls.RemoveAt(i);
				anyRemoved = true;
			}
			if (anyRemoved)
				EditorUtility.SetDirty(menu);
			return anyRemoved;
		}
	}
}
