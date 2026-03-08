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
				var excludedSet = BuildExclusionSet(data.exclude, physBones, avatarRoot.transform, out var excludedPaths);
				Debug.Log($"[YUCP Universal Physbone Collider] Exclude list: {data.exclude?.Count ?? 0}, Excluded paths: [{string.Join(", ", excludedPaths)}], Excluded set: {excludedSet.Count}, PhysBones: {physBones.Length}");
				int addCount = 0;
				int excludedCount = 0;
				foreach (var pb in physBones)
				{
					if (pb == null) continue;
					if (excludedSet.Contains(pb))
					{
						excludedCount++;
						Transform effRoot = pb.rootTransform != null ? pb.rootTransform : pb.transform;
						Debug.Log($"[YUCP Universal Physbone Collider] Skipping excluded: {GetPathFromRoot(effRoot, avatarRoot.transform)}");
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

		private static HashSet<VRCPhysBone> BuildExclusionSet(List<Object> excludeList, VRCPhysBone[] physBones, Transform avatarRoot, out List<string> excludedPathsOut)
		{
			excludedPathsOut = new List<string>();
			var set = new HashSet<VRCPhysBone>();
			if (excludeList == null || excludeList.Count == 0 || physBones == null || avatarRoot == null) return set;

			// Collect paths to exclude (path-based matching survives avatar cloning)
			var excludedPaths = new HashSet<string>();

			foreach (var obj in excludeList)
			{
				if (obj == null) continue;

				if (obj is VRCPhysBone pb)
				{
					// Direct PhysBone: use path of its effective root
					Transform effectiveRoot = pb.rootTransform != null ? pb.rootTransform : pb.transform;
					string path = GetPathFromRoot(effectiveRoot, avatarRoot);
					if (!string.IsNullOrEmpty(path))
					{
						excludedPaths.Add(path);
						excludedPathsOut.Add(path);
					}
					else
						Debug.Log($"[YUCP Universal Physbone Collider] BuildExclusion: VRCPhysBone path null (effectiveRoot not under avatar?)");
					continue;
				}

				Transform t = obj is GameObject go ? go.transform : obj as Transform;
				if (t == null) continue;

				string excludedPath = GetPathFromRoot(t, avatarRoot);
				if (!string.IsNullOrEmpty(excludedPath))
				{
					excludedPaths.Add(excludedPath);
					excludedPathsOut.Add(excludedPath);
				}
				else
					Debug.Log($"[YUCP Universal Physbone Collider] BuildExclusion: '{t.name}' path null (not under avatar root?)");
			}

			// Match PhysBones by path. Check both effectiveRoot (chain start) and physBone.transform (component location).
			// Use case-insensitive matching and leaf segment matching (handles VRCFury/NDMF hierarchy restructuring).
			foreach (var physBone in physBones)
			{
				if (physBone == null) continue;
				Transform effectiveRoot = physBone.rootTransform != null ? physBone.rootTransform : physBone.transform;
				string rootPath = GetPathFromRoot(effectiveRoot, avatarRoot);
				string componentPath = GetPathFromRoot(physBone.transform, avatarRoot);
				// Check both root path and component path (PhysBone may be on excluded object with root elsewhere)
				string[] pathsToCheck = new[] { rootPath, componentPath };
				bool matched = false;

				foreach (var pathToCheck in pathsToCheck)
				{
					if (string.IsNullOrEmpty(pathToCheck)) continue;
					string pathLower = pathToCheck.ToLowerInvariant();

					foreach (var excludedPath in excludedPaths)
					{
						string exclLower = excludedPath.ToLowerInvariant();
						// Exact or prefix match (case-insensitive)
						if (pathLower == exclLower ||
						    pathLower.StartsWith(exclLower + "/") ||
						    exclLower.StartsWith(pathLower + "/"))
						{
							matched = true;
							break;
						}
						// Excluded leaf segment in path (e.g. "Belt accessories" matches "..belt accessories/.." after hierarchy merge)
						string excludedLeaf = exclLower.Substring(exclLower.LastIndexOf('/') + 1);
						if (!string.IsNullOrEmpty(excludedLeaf) &&
						    (pathLower.Contains("/" + excludedLeaf + "/") || pathLower.EndsWith("/" + excludedLeaf)))
						{
							matched = true;
							break;
						}
					}
					if (matched) break;
				}
				if (matched && !set.Contains(physBone))
					set.Add(physBone);
			}

			return set;
		}

		private static string GetPathFromRoot(Transform t, Transform root)
		{
			if (t == null || root == null) return null;
			// t may be from original avatar; root may be clone. Use t's hierarchy if t not under root.
			Transform pathRoot = (t.IsChildOf(root) || t == root) ? root : GetAvatarRoot(t);
			if (pathRoot == null) return null;
			var parts = new List<string>();
			var cur = t;
			while (cur != null && cur != pathRoot)
			{
				parts.Insert(0, cur.name);
				cur = cur.parent;
			}
			return parts.Count > 0 ? string.Join("/", parts) : null;
		}

		private static Transform GetAvatarRoot(Transform t)
		{
			var cur = t;
			while (cur != null)
			{
				if (cur.GetComponent<VRCAvatarDescriptor>() != null)
					return cur;
				cur = cur.parent;
			}
			return null;
		}
	}
}
