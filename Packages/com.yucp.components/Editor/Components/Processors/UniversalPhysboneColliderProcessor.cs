using System.Collections.Generic;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components;

namespace YUCP.Components.Editor
{
	/// <summary>
	/// At avatar build time, finds all UniversalPhysboneColliderData components, then for each
	/// finds every PhysBone on the avatar and adds the selected PhysBone Collider(s) to their collider list.
	/// </summary>
	public class UniversalPhysboneColliderProcessor : IVRCSDKPreprocessAvatarCallback
	{
		public int callbackOrder => int.MinValue + 209;

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

			foreach (var data in dataList)
			{
				if (data == null || !data.enabled) continue;
				if (!data.transform.IsChildOf(avatarRoot.transform)) continue;

				var collidersToAdd = ResolveColliders(data.collider, avatarRoot);
				if (collidersToAdd == null || collidersToAdd.Count == 0)
				{
					if (data.collider != null)
						Debug.LogWarning("[YUCP Universal Physbone Collider] No VRC PhysBone Collider found on the assigned object. Assign a GameObject with PhysBone Colliders (on it or its children) or a single PhysBone Collider component.", data);
					continue;
				}

				var physBones = avatarRoot.GetComponentsInChildren<VRCPhysBone>(true);
				var excludedSet = BuildExclusionSet(data.exclude, physBones);
				int addCount = 0;
				int excludedCount = 0;
				foreach (var pb in physBones)
				{
					if (pb == null) continue;
					if (excludedSet.Contains(pb))
					{
						excludedCount++;
						continue;
					}
					foreach (var collider in collidersToAdd)
					{
						if (pb.colliders.Contains(collider)) continue;
						pb.colliders.Add(collider);
						addCount++;
					}
				}

				if (data.verboseLogging)
					Debug.Log($"[YUCP Universal Physbone Collider] Added {collidersToAdd.Count} collider(s) to {physBones.Length - excludedCount} PhysBone(s) ({addCount} total additions, {excludedCount} excluded).");
			}

			return true;
		}

		private static List<VRCPhysBoneColliderBase> ResolveColliders(UnityEngine.Object assigned, GameObject avatarRoot)
		{
			if (assigned == null || avatarRoot == null) return null;

			var list = new List<VRCPhysBoneColliderBase>();

			if (assigned is GameObject go)
			{
				if (!go.transform.IsChildOf(avatarRoot.transform))
					return list;
				var found = go.GetComponentsInChildren<VRCPhysBoneCollider>(true);
				if (found != null)
					list.AddRange(found);
				return list;
			}

			if (assigned is VRCPhysBoneCollider collider && collider.transform.IsChildOf(avatarRoot.transform))
				list.Add(collider);

			return list;
		}

		private static HashSet<VRCPhysBone> BuildExclusionSet(List<Object> excludeList, VRCPhysBone[] physBones)
		{
			var set = new HashSet<VRCPhysBone>();
			if (excludeList == null || excludeList.Count == 0 || physBones == null) return set;

			foreach (var obj in excludeList)
			{
				if (obj == null) continue;

				if (obj is VRCPhysBone pb)
				{
					set.Add(pb);
					continue;
				}

				Transform t = obj is GameObject go ? go.transform : obj as Transform;
				if (t == null) continue;

				foreach (var physBone in physBones)
				{
					if (physBone == null) continue;
					// Use effective root: when rootTransform is null, PhysBone uses the component's transform
					Transform effectiveRoot = physBone.rootTransform != null ? physBone.rootTransform : physBone.transform;
					// Exclude when: t is the root, t is parent of root, OR t is in the chain (descendant of root)
					bool match = effectiveRoot == t || effectiveRoot.IsChildOf(t) || t.IsChildOf(effectiveRoot);
					if (match)
						set.Add(physBone);
				}
			}

			return set;
		}
	}
}
