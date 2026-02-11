using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components;

namespace YUCP.Components.Editor
{
	/// <summary>
	/// Processes Delete If Disabled components during avatar build.
	/// Removes any GameObject that has DeleteIfDisabledData and is currently inactive.
	/// </summary>
	public class DeleteIfDisabledProcessor : IVRCSDKPreprocessAvatarCallback
	{
		public int callbackOrder => int.MinValue + 4;

		public bool OnPreprocessAvatar(GameObject avatarRoot)
		{
			var dataList = avatarRoot.GetComponentsInChildren<DeleteIfDisabledData>(true);
			if (dataList.Length == 0) return true;

			var toDestroy = new List<GameObject>();
			foreach (var data in dataList)
			{
				if (data == null) continue;
				// Only the object this component is attached to is considered. Must be a child, not the avatar root.
				if (data.gameObject == avatarRoot)
				{
					Debug.LogError("[DeleteIfDisabledProcessor] Delete If Disabled cannot be attached to the avatar root. Attach it to a child object only. Skipping.", data);
					continue;
				}
				if (!data.transform.IsChildOf(avatarRoot.transform))
					continue;
				if (!data.gameObject.activeInHierarchy)
				{
					toDestroy.Add(data.gameObject);
					if (data.debugMode)
					{
						Debug.Log($"[DeleteIfDisabledProcessor] Will delete inactive object: '{data.gameObject.name}'", data);
					}
				}
			}

			foreach (var go in toDestroy)
			{
				if (go != null && go != avatarRoot && go.transform.IsChildOf(avatarRoot.transform))
				{
					Object.DestroyImmediate(go);
				}
			}

			return true;
		}
	}
}
