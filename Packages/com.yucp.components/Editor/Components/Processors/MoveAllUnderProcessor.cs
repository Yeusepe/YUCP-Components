using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components;

namespace YUCP.Components.Editor
{
	/// <summary>
	/// At avatar build time, prefixes the menu path of all VRCFury menu items (toggles, etc.)
	/// that live under each Move All Under component's GameObject. Uses reflection only to access VRCFury.
	/// </summary>
	public class MoveAllUnderProcessor : IVRCSDKPreprocessAvatarCallback
	{
		public int callbackOrder => int.MinValue + 500;

		private static Assembly _vrcfuryRuntime;
		private static Assembly _vrcfuryEditor;
		private static Type _vrcfuryType;
		private static Type _toggleType;
		private static bool _reflectionFailed;

		public bool OnPreprocessAvatar(GameObject avatarRoot)
		{
			var dataList = avatarRoot.GetComponentsInChildren<MoveAllUnderData>(true);
			if (dataList.Length == 0) return true;

			Debug.Log($"[YUCP Move All Under] Found {dataList.Length} MoveAllUnderData component(s).");

			var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
			if (descriptor == null || descriptor.expressionsMenu == null)
			{
				Debug.LogWarning("[YUCP Move All Under] No VRCAvatarDescriptor or expressions menu on avatar.");
				return true;
			}

			if (!TryResolveVRCFuryTypes())
			{
				if (!_reflectionFailed)
				{
					_reflectionFailed = true;
					Debug.LogWarning("[YUCP Move All Under] VRCFury types not found. Move All Under will not run. Is VRCFury installed?");
				}
				return true;
			}

			var menu = descriptor.expressionsMenu;
			object menuManager = TryCreateMenuManager(menu);
			if (menuManager == null)
			{
				Debug.LogWarning("[YUCP Move All Under] Failed to create VRCFury MenuManager.");
				return true;
			}

			MethodInfo moveMethod = menuManager.GetType().GetMethod("Move", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), typeof(string) }, null);
			if (moveMethod == null)
			{
				Debug.LogWarning("[YUCP Move All Under] Move method not found on MenuManager.");
				return true;
			}

			foreach (var data in dataList)
			{
				if (data == null || !data.enabled) continue;
				if (!data.transform.IsChildOf(avatarRoot.transform)) continue;

				string prefix = (data.targetMenuPath ?? "").Trim().TrimEnd('/');
				if (string.IsNullOrEmpty(prefix)) continue;

				Transform root = data.transform;
				var fromPaths = CollectMenuPathsUnderRoot(root);
				Debug.Log($"[YUCP Move All Under] Root '{root.name}' prefix '{prefix}': found {fromPaths.Count} menu path(s).");

				var seen = new HashSet<string>();

				foreach (string fromPath in fromPaths)
				{
					if (string.IsNullOrWhiteSpace(fromPath) || !seen.Add(fromPath)) continue;

					string toPath = string.IsNullOrEmpty(prefix) ? fromPath : prefix + "/" + fromPath;
					try
					{
						object result = moveMethod.Invoke(menuManager, new object[] { fromPath, toPath });
						bool ok = result is bool b && b;
						Debug.Log($"[YUCP Move All Under] Move: {fromPath} -> {toPath} ({(ok ? "ok" : "failed")})");
						if (!ok)
							Debug.LogWarning($"[YUCP Move All Under] Move failed: {fromPath} -> {toPath}", data);
					}
					catch (Exception ex)
					{
						Debug.LogException(ex, data);
					}
				}
			}

			return true;
		}

		private static bool TryResolveVRCFuryTypes()
		{
			if (_vrcfuryType != null) return true;

			_vrcfuryRuntime = Assembly.Load("VRCFury");
			if (_vrcfuryRuntime == null) return false;

			_vrcfuryType = _vrcfuryRuntime.GetType("VF.Model.VRCFury");
			if (_vrcfuryType == null) return false;

			_toggleType = _vrcfuryRuntime.GetType("VF.Model.Feature.Toggle");
			if (_toggleType == null) return false;

			return true;
		}

		private static object TryCreateMenuManager(VRCExpressionsMenu menu)
		{
			_vrcfuryEditor = VRCFuryReflectionUtils.LoadEditorAvatarAssembly();
			if (_vrcfuryEditor == null) return null;

			Type menuManagerType = _vrcfuryEditor.GetType("VF.Utils.MenuManager");
			if (menuManagerType == null) return null;

			try
			{
				return Activator.CreateInstance(menuManagerType, menu, (Func<int>)(() => 0));
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// Collects menu path and transform for each VRCFury Toggle under the given root. Used for nested
		/// Move All Under: path is built from the toggle's hierarchy (bottom to top). Reflection-only.
		/// </summary>
		public static List<(string path, Transform transform)> CollectTogglePathAndTransformsStatic(Transform root, Assembly vrcfuryRuntime)
		{
			var list = new List<(string path, Transform transform)>();
			if (root == null || vrcfuryRuntime == null) return list;

			const string vrcfuryTypeName = "VF.Model.VRCFury";
			GameObject rootGo = root.gameObject;
			MonoBehaviour[] allMono = rootGo.GetComponentsInChildren<MonoBehaviour>(true);

			foreach (var mb in allMono)
			{
				if (mb == null || mb.GetType().FullName != vrcfuryTypeName) continue;

				MethodInfo getAllFeatures = mb.GetType().GetMethod("GetAllFeatures", BindingFlags.Public | BindingFlags.Instance);
				if (getAllFeatures == null) continue;

				object featuresObj;
				try { featuresObj = getAllFeatures.Invoke(mb, null); }
				catch { continue; }
				if (featuresObj == null) continue;

				var features = featuresObj as System.Collections.IList;
				if (features == null) continue;

				foreach (object feature in features)
				{
					if (feature == null || feature.GetType().FullName != "VF.Model.Feature.Toggle") continue;
					FieldInfo nameField = feature.GetType().GetField("name", BindingFlags.Public | BindingFlags.Instance);
					if (nameField == null) continue;
					string path = nameField.GetValue(feature) as string;
					if (string.IsNullOrWhiteSpace(path)) continue;
					list.Add((path, mb.transform));
				}
			}

			return list;
		}

		/// <summary>
		/// Collects menu paths from VRCFury Toggle features under the given root. Used by the Harmony patch
		/// that runs inside VRCFury's pipeline. Reflection-only; requires VRCFury runtime assembly.
		/// </summary>
		public static List<string> CollectMenuPathsUnderRootStatic(Transform root, Assembly vrcfuryRuntime)
		{
			var pairs = CollectTogglePathAndTransformsStatic(root, vrcfuryRuntime);
			var paths = new List<string>(pairs.Count);
			foreach (var (path, _) in pairs)
				paths.Add(path);
			return paths;
		}

		/// <summary>
		/// Builds the chain of Move All Under components from the given transform up to the avatar root,
		/// ordered from top (nearest to avatar) to bottom (nearest to the toggle). Only enabled components.
		/// </summary>
		public static List<MoveAllUnderData> GetMoveAllUnderChain(Transform from, Transform avatarRoot)
		{
			var chain = new List<MoveAllUnderData>();
			if (from == null || avatarRoot == null || !from.IsChildOf(avatarRoot)) return chain;

			Transform t = from;
			while (t != null && t != avatarRoot)
			{
				var data = t.GetComponent<MoveAllUnderData>();
				if (data != null && data.enabled)
					chain.Add(data);
				t = t.parent;
			}

			chain.Reverse();
			return chain;
		}

		/// <summary>
		/// Sanitizes a name for use as a menu path segment (no spaces, no slashes).
		/// </summary>
		public static string SanitizeMenuSegment(string name)
		{
			if (string.IsNullOrWhiteSpace(name)) return "";
			var s = name.Trim();
			var sb = new System.Text.StringBuilder(s.Length);
			foreach (char c in s)
			{
				if (c == ' ' || c == '/') continue;
				if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
			}
			return sb.ToString();
		}

		/// <summary>
		/// Builds the target menu path: prefix from chain (top to bottom), then the original menu path.
		/// Each chain item uses targetMenuPath if set, otherwise sanitized GameObject name.
		/// The original menu path (e.g. "This is a test/toggle") is preserved verbatim—no sanitization,
		/// so result is e.g. "movable/This is a test/toggle".
		/// </summary>
		public static string BuildToPathFromChain(List<MoveAllUnderData> chain, string fromPath)
		{
			if (chain == null || chain.Count == 0) return fromPath;
			var parts = new List<string>();
			foreach (var data in chain)
			{
				string segment = (data.targetMenuPath ?? "").Trim().TrimEnd('/');
				if (string.IsNullOrEmpty(segment))
					segment = SanitizeMenuSegment(data.gameObject.name);
				if (!string.IsNullOrEmpty(segment))
					parts.Add(segment);
			}
			if (parts.Count == 0) return fromPath;
			string prefix = string.Join("/", parts);
			// Preserve full original menu path (slashes, spaces, casing) — only prefix is prepended
			return string.IsNullOrEmpty(fromPath) ? prefix : prefix + "/" + fromPath;
		}

		private static List<string> CollectMenuPathsUnderRoot(Transform root)
		{
			if (!TryResolveVRCFuryTypes()) return new List<string>();
			return CollectMenuPathsUnderRootStatic(root, _vrcfuryRuntime);
		}
	}
}
