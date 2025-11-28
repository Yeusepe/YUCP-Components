using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
using YUCP.Components;
using YUCP.Components.Editor.UI;

namespace YUCP.Components.Editor
{
	/// <summary>
	/// Processes conditional deletion components during avatar build.
	/// Evaluates configurable conditions on the avatar and deletes specified GameObjects based on logical gate evaluation.
	/// </summary>
	public class ConditionalDeletionProcessor : IVRCSDKPreprocessAvatarCallback
	{
		public int callbackOrder => int.MinValue + 5;

		public bool OnPreprocessAvatar(GameObject avatarRoot)
		{
			var dataList = avatarRoot.GetComponentsInChildren<ConditionalDeletionData>(true);
			if (dataList.Length == 0) return true;

			var progress = YUCPProgressWindow.Create();
			try
			{
				var animator = avatarRoot.GetComponentInChildren<Animator>();
				if (animator == null)
				{
					Debug.LogError("[ConditionalDeletionProcessor] Animator missing on avatar.", avatarRoot);
					return true;
				}

				for (int i = 0; i < dataList.Length; i++)
				{
					var data = dataList[i];
					try
					{
						ProcessOne(data, animator, avatarRoot);
					}
					catch (Exception ex)
					{
						Debug.LogError($"[ConditionalDeletionProcessor] Error processing '{data.name}': {ex.Message}", data);
						Debug.LogException(ex);
					}

					float p = (float)(i + 1) / dataList.Length;
					progress.Progress(p, $"Processed conditional deletion {i + 1}/{dataList.Length}");
				}
			}
			finally
			{
				progress.CloseWindow();
			}

			return true;
		}

		private void ProcessOne(ConditionalDeletionData data, Animator animator, GameObject avatarRoot)
		{
			if (data.conditionGroups == null || data.conditionGroups.Count == 0)
			{
				if (data.debugMode)
				{
					Debug.LogWarning($"[ConditionalDeletionProcessor] '{data.name}' has no condition groups defined. Skipping.", data);
				}
				return;
			}

			if (data.objectsToDelete == null || data.objectsToDelete.Count == 0)
			{
				if (data.debugMode)
				{
					Debug.LogWarning($"[ConditionalDeletionProcessor] '{data.name}' has no objects to delete. Skipping.", data);
				}
				return;
			}

			bool finalResult = EvaluateConditionGroups(data.conditionGroups, animator, avatarRoot, data.debugMode);

			if (data.debugMode)
			{
				Debug.Log($"[ConditionalDeletionProcessor] '{data.name}' evaluated to: {finalResult} (deleteOnTrue={data.deleteOnTrue})", data);
			}

			bool shouldDelete = data.deleteOnTrue ? finalResult : !finalResult;

			if (shouldDelete)
			{
				foreach (var obj in data.objectsToDelete)
				{
					if (obj != null)
					{
						if (obj.transform.IsChildOf(avatarRoot.transform) || obj == avatarRoot)
						{
							if (data.debugMode)
							{
								Debug.Log($"[ConditionalDeletionProcessor] Deleting '{obj.name}' from '{data.name}'", data);
							}
							UnityEngine.Object.DestroyImmediate(obj);
						}
						else if (data.debugMode)
						{
							Debug.LogWarning($"[ConditionalDeletionProcessor] '{obj.name}' is not a child of avatar root. Skipping deletion.", data);
						}
					}
				}
			}
			else if (data.debugMode)
			{
				Debug.Log($"[ConditionalDeletionProcessor] '{data.name}' conditions not met. No objects deleted.", data);
			}
		}

		private const int MAX_NESTING_DEPTH = 10;

		private bool EvaluateConditionGroups(List<ConditionalDeletionData.ConditionGroup> groups, Animator animator, GameObject avatarRoot, bool debugMode)
		{
			if (groups == null || groups.Count == 0) return false;

			bool anyGroupTrue = false;
			foreach (var group in groups)
			{
				bool groupResult = EvaluateConditionGroup(group, animator, avatarRoot, debugMode, 0);
				if (groupResult)
				{
					anyGroupTrue = true;
					if (debugMode) Debug.Log($"[ConditionalDeletionProcessor] Condition group evaluated to TRUE", null);
					break;
				}
			}

			return anyGroupTrue;
		}

		private bool EvaluateConditionGroup(ConditionalDeletionData.ConditionGroup group, Animator animator, GameObject avatarRoot, bool debugMode, int depth)
		{
			if (group == null) return false;

			if (depth >= MAX_NESTING_DEPTH)
			{
				if (debugMode)
				{
					Debug.LogWarning($"[ConditionalDeletionProcessor] Maximum nesting depth ({MAX_NESTING_DEPTH}) exceeded. Skipping nested group evaluation.", null);
				}
				return false;
			}

			List<bool> allResults = new List<bool>();

			foreach (var condition in group.conditions)
			{
				if (condition == null) continue;
				bool result = EvaluateCondition(condition, animator, avatarRoot, debugMode);
				if (condition.invert) result = !result;
				allResults.Add(result);
			}

			foreach (var nestedGroup in group.nestedGroups)
			{
				if (nestedGroup == null) continue;
				bool nestedResult = EvaluateConditionGroup(nestedGroup, animator, avatarRoot, debugMode, depth + 1);
				allResults.Add(nestedResult);
			}

			if (allResults.Count == 0) return false;

			bool groupResult = CombineResults(allResults, group.groupOperator);

			if (group.invertGroup)
			{
				groupResult = !groupResult;
			}

			return groupResult;
		}

		private bool CombineResults(List<bool> results, ConditionalDeletionData.LogicalOperator op)
		{
			if (results == null || results.Count == 0) return false;

			switch (op)
			{
				case ConditionalDeletionData.LogicalOperator.And:
					return results.All(r => r);

				case ConditionalDeletionData.LogicalOperator.Or:
					return results.Any(r => r);

				case ConditionalDeletionData.LogicalOperator.Not:
					if (results.Count == 1)
						return !results[0];
					return !results.All(r => r);

				case ConditionalDeletionData.LogicalOperator.Nand:
					return !results.All(r => r);

				case ConditionalDeletionData.LogicalOperator.Xor:
					int trueCount = results.Count(r => r);
					return trueCount == 1;

				default:
					return false;
			}
		}

		private bool EvaluateCondition(ConditionalDeletionData.Condition condition, Animator animator, GameObject avatarRoot, bool debugMode)
		{
			if (condition == null) return false;

			switch (condition.conditionType)
			{
				case ConditionalDeletionData.ConditionType.MeshExists:
					return CheckMeshExists(condition, avatarRoot, debugMode);

				case ConditionalDeletionData.ConditionType.BoneExists:
					return CheckBoneExists(condition, animator, avatarRoot, debugMode);

				case ConditionalDeletionData.ConditionType.ComponentExists:
					return CheckComponentExists(condition, avatarRoot, debugMode);

				case ConditionalDeletionData.ConditionType.GameObjectExists:
					return CheckGameObjectExists(condition, avatarRoot, debugMode);

				case ConditionalDeletionData.ConditionType.TagMatches:
					return CheckTagMatches(condition, avatarRoot, debugMode);

				case ConditionalDeletionData.ConditionType.LayerMatches:
					return CheckLayerMatches(condition, avatarRoot, debugMode);

				default:
					return false;
			}
		}

		private bool CheckMeshExists(ConditionalDeletionData.Condition condition, GameObject avatarRoot, bool debugMode)
		{
			if (string.IsNullOrEmpty(condition.meshNameOrPath)) return false;

			Transform target = FindByPath(avatarRoot.transform, condition.meshNameOrPath);
			if (target == null) return false;

			bool found = false;
			switch (condition.meshType)
			{
				case ConditionalDeletionData.MeshType.SkinnedMeshRenderer:
					found = target.GetComponent<SkinnedMeshRenderer>() != null;
					break;
				case ConditionalDeletionData.MeshType.MeshRenderer:
					found = target.GetComponent<MeshRenderer>() != null;
					break;
				case ConditionalDeletionData.MeshType.Either:
					found = target.GetComponent<SkinnedMeshRenderer>() != null || target.GetComponent<MeshRenderer>() != null;
					break;
			}

			if (debugMode && found)
			{
				Debug.Log($"[ConditionalDeletionProcessor] Mesh found: {condition.meshNameOrPath}", null);
			}

			return found;
		}

		private bool CheckBoneExists(ConditionalDeletionData.Condition condition, Animator animator, GameObject avatarRoot, bool debugMode)
		{
			Transform bone = null;

			if (condition.boneTargetType == ConditionalDeletionData.BoneTargetType.HumanoidBone)
			{
				bone = animator.GetBoneTransform(condition.humanoidBone);
			}
			else if (!string.IsNullOrEmpty(condition.bonePath))
			{
				bone = FindByPath(avatarRoot.transform, condition.bonePath);
			}

			if (debugMode && bone != null)
			{
				Debug.Log($"[ConditionalDeletionProcessor] Bone found: {(condition.boneTargetType == ConditionalDeletionData.BoneTargetType.HumanoidBone ? condition.humanoidBone.ToString() : condition.bonePath)}", null);
			}

			return bone != null;
		}

		private bool CheckComponentExists(ConditionalDeletionData.Condition condition, GameObject avatarRoot, bool debugMode)
		{
			if (string.IsNullOrEmpty(condition.componentTypeName)) return false;

			Type componentType = Type.GetType(condition.componentTypeName);
			if (componentType == null)
			{
				if (debugMode)
				{
					Debug.LogWarning($"[ConditionalDeletionProcessor] Component type not found: {condition.componentTypeName}", null);
				}
				return false;
			}

			Component found = avatarRoot.GetComponentInChildren(componentType, true);
			bool exists = found != null;

			if (debugMode && exists)
			{
				Debug.Log($"[ConditionalDeletionProcessor] Component found: {condition.componentTypeName}", null);
			}

			return exists;
		}

		private bool CheckGameObjectExists(ConditionalDeletionData.Condition condition, GameObject avatarRoot, bool debugMode)
		{
			if (string.IsNullOrEmpty(condition.gameObjectNameOrPath)) return false;

			Transform found = FindByPath(avatarRoot.transform, condition.gameObjectNameOrPath);
			bool exists = found != null;

			if (debugMode && exists)
			{
				Debug.Log($"[ConditionalDeletionProcessor] GameObject found: {condition.gameObjectNameOrPath}", null);
			}

			return exists;
		}

		private bool CheckTagMatches(ConditionalDeletionData.Condition condition, GameObject avatarRoot, bool debugMode)
		{
			if (string.IsNullOrEmpty(condition.tagCheckGameObjectPath)) return false;
			if (string.IsNullOrEmpty(condition.tagName)) return false;

			Transform target = FindByPath(avatarRoot.transform, condition.tagCheckGameObjectPath);
			if (target == null) return false;

			bool matches = target.CompareTag(condition.tagName);

			if (debugMode && matches)
			{
				Debug.Log($"[ConditionalDeletionProcessor] Tag matches: {condition.tagCheckGameObjectPath} has tag '{condition.tagName}'", null);
			}

			return matches;
		}

		private bool CheckLayerMatches(ConditionalDeletionData.Condition condition, GameObject avatarRoot, bool debugMode)
		{
			if (string.IsNullOrEmpty(condition.layerCheckGameObjectPath)) return false;

			Transform target = FindByPath(avatarRoot.transform, condition.layerCheckGameObjectPath);
			if (target == null) return false;

			int targetLayer = target.gameObject.layer;
			int checkLayer = LayerMask.NameToLayer(condition.layerName);
			
			bool matches = checkLayer != -1 && targetLayer == checkLayer;

			if (debugMode && matches)
			{
				Debug.Log($"[ConditionalDeletionProcessor] Layer matches: {condition.layerCheckGameObjectPath} is on layer '{condition.layerName}'", null);
			}

			return matches;
		}

		private Transform FindByPath(Transform root, string path)
		{
			if (root == null || string.IsNullOrEmpty(path)) return null;

			if (path.StartsWith("/") || path.StartsWith("\\"))
			{
				path = path.Substring(1);
			}

			Transform current = root;
			string[] parts = path.Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

			foreach (string part in parts)
			{
				if (part == ".") continue;
				if (part == "..")
				{
					current = current.parent;
					if (current == null) return null;
					continue;
				}

				Transform child = current.Find(part);
				if (child == null)
				{
					for (int i = 0; i < current.childCount; i++)
					{
						if (current.GetChild(i).name == part)
						{
							child = current.GetChild(i);
							break;
						}
					}
				}

				if (child == null) return null;
				current = child;
			}

			return current;
		}
	}
}

