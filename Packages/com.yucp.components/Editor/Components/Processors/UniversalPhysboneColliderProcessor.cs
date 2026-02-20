using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components;

namespace YUCP.Components.Editor
{
	/// <summary>
	/// At avatar build time, finds all UniversalPhysboneColliderData components, then for each
	/// finds every PhysBone on the avatar and adds the selected PhysBone Collider to their collider list.
	/// </summary>
	public class UniversalPhysboneColliderProcessor : IVRCSDKPreprocessAvatarCallback
	{
		public int callbackOrder => int.MinValue + 209;

		private static Type _physBoneType;
		private static Type _physBoneColliderType;
		private static PropertyInfo _collidersProperty;
		private static FieldInfo _collidersField;
		private static bool _typesResolved;
		private static bool _resolutionFailed;

		public bool OnPreprocessAvatar(GameObject avatarRoot)
		{
			var dataList = avatarRoot.GetComponentsInChildren<UniversalPhysboneColliderData>(true);
			if (dataList.Length == 0) return true;

			var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
			if (descriptor == null)
			{
				Debug.LogWarning("[YUCP Universal Physbone Collider] No VRCAvatarDescriptor on avatar.");
				return true;
			}

			if (!TryResolvePhysBoneTypes())
			{
				if (!_resolutionFailed)
				{
					_resolutionFailed = true;
					Debug.LogWarning("[YUCP Universal Physbone Collider] VRC PhysBone types not found. Is the VRChat SDK installed?");
				}
				return true;
			}

			foreach (var data in dataList)
			{
				if (data == null || !data.enabled) continue;
				if (!data.transform.IsChildOf(avatarRoot.transform)) continue;

				var collidersToAdd = ResolveColliders(data.collider, avatarRoot);
				if (collidersToAdd == null || collidersToAdd.Count == 0)
				{
					if (data.collider != null)
						Debug.LogWarning("[YUCP Universal Physbone Collider] No VRC PhysBone Collider found. Assign a GameObject that has PhysBone Colliders (on it or its children) or a single PhysBone Collider component.", data);
					continue;
				}

				var physBones = avatarRoot.GetComponentsInChildren(_physBoneType, true);
				int addCount = 0;
				foreach (var pb in physBones)
				{
					if (pb == null) continue;
					foreach (var colliderComponent in collidersToAdd)
					{
						if (AddColliderToPhysBone(pb, colliderComponent))
							addCount++;
					}
				}

				if (data.verboseLogging)
					Debug.Log($"[YUCP Universal Physbone Collider] Added {collidersToAdd.Count} collider(s) to {physBones.Length} PhysBone(s) ({addCount} total additions).");
			}

			return true;
		}

		/// <summary>
		/// Resolves the assigned Object to a list of VRC PhysBone Collider components.
		/// Accepts a GameObject (collects all PhysBone Colliders on it and its children) or a single PhysBone Collider component.
		/// </summary>
		private static List<Component> ResolveColliders(UnityEngine.Object assigned, GameObject avatarRoot)
		{
			if (assigned == null || avatarRoot == null) return null;

			var list = new List<Component>();

			var go = assigned as GameObject;
			if (go != null)
			{
				if (!go.transform.IsChildOf(avatarRoot.transform))
					return list;
				var components = go.GetComponentsInChildren(_physBoneColliderType, true);
				if (components != null)
				{
					foreach (var c in components)
					{
						if (c != null && _physBoneColliderType.IsAssignableFrom(c.GetType()))
							list.Add(c);
					}
				}
				return list;
			}

			var comp = assigned as Component;
			if (comp != null && _physBoneColliderType.IsAssignableFrom(comp.GetType()) && comp.transform.IsChildOf(avatarRoot.transform))
				list.Add(comp);

			return list;
		}

		private static bool TryResolvePhysBoneTypes()
		{
			if (_typesResolved) return _collidersProperty != null;

			_typesResolved = true;

			var physBoneAssembly = Assembly.Load("VRC.SDK3.Dynamics.PhysBone");
			if (physBoneAssembly == null) return false;

			_physBoneType = physBoneAssembly.GetType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone");
			if (_physBoneType == null)
				_physBoneType = physBoneAssembly.GetType("VRCPhysBone");
			if (_physBoneType == null) return false;

			_physBoneColliderType = physBoneAssembly.GetType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider");
			if (_physBoneColliderType == null)
				_physBoneColliderType = physBoneAssembly.GetType("VRCPhysBoneCollider");
			if (_physBoneColliderType == null) return false;

			_collidersProperty = _physBoneType.GetProperty("Colliders", BindingFlags.Public | BindingFlags.Instance);
			if (_collidersProperty == null)
				_collidersProperty = _physBoneType.GetProperty("colliders", BindingFlags.Public | BindingFlags.Instance);

			_collidersField = _physBoneType.GetField("colliders", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			if (_collidersField == null)
				_collidersField = _physBoneType.GetField("Colliders", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

			return _collidersProperty != null || _collidersField != null;
		}

		private static bool AddColliderToPhysBone(object physBone, Component collider)
		{
			try
			{
				// Prefer property "Colliders" (list we can Add to)
				if (_collidersProperty != null)
				{
					var list = _collidersProperty.GetValue(physBone);
					if (list == null) return false;
					var addMethod = list.GetType().GetMethod("Add", new[] { _physBoneColliderType });
					if (addMethod == null) addMethod = list.GetType().GetMethod("Add", new[] { typeof(object) });
					if (addMethod != null)
					{
						// Avoid adding duplicate
						var contains = list.GetType().GetMethod("Contains", new[] { typeof(object) }) ?? list.GetType().GetMethod("Contains", new[] { _physBoneColliderType });
						if (contains != null && (bool)contains.Invoke(list, new object[] { collider }))
							return false;
						addMethod.Invoke(list, new object[] { collider });
						return true;
					}
				}

				// Fallback: field "colliders"
				if (_collidersField != null)
				{
					var list = _collidersField.GetValue(physBone);
					if (list != null)
					{
						var addMethod = list.GetType().GetMethod("Add", new[] { _physBoneColliderType });
						if (addMethod == null) addMethod = list.GetType().GetMethod("Add", new[] { typeof(object) });
						if (addMethod != null)
						{
							var contains = list.GetType().GetMethod("Contains", new[] { typeof(object) });
							if (contains != null && (bool)contains.Invoke(list, new object[] { collider }))
								return false;
							addMethod.Invoke(list, new object[] { collider });
							return true;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}

			return false;
		}
	}
}
