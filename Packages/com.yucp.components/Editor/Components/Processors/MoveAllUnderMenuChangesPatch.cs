using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
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

				Type menuChangesType = Assembly.Load("VRCFury-Editor")?.GetType("VF.Service.MenuChangesService");
				if (menuChangesType == null) return;

				MethodInfo applyMethod = menuChangesType.GetMethod("Apply", BindingFlags.Public | BindingFlags.Instance);
				if (applyMethod == null) return;

				var harmony = new Harmony("com.yucp.moveallunder");
				harmony.Patch(applyMethod, prefix: new HarmonyMethod(typeof(MoveAllUnderMenuChangesPatch), nameof(Prefix)));
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
	}
}
